using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Prologue;

namespace NSP.View;

// 스토리 컷인 화면 — 실제 3D 관제실 화면 **위에** 스탠딩 한두 명과 이름 · 대사만 올린다.
//
// 풀스크린 비주얼 노벨 장면이 아니다(문서 §24 · §37). 배경은 바꾸지 않고, 화면을
// 빼앗지도 않는다. 아래쪽을 아주 옅게 눌러 글자가 읽히게만 한다.
//
// 넘기기는 프롤로그 · 교육과 똑같은 규칙이다(문서 §33).
//   · 타이핑 중 클릭 → 그 문장 즉시 완성
//   · 완성 상태에서 클릭 → 다음 줄
// 입력은 PrologueAdvanceInput 이 모아서 RequestAdvance() 로 넘겨 준다.
//
// 이 창은 스스로 연출을 결정하지 않는다 — 어떤 대사를 어떤 순서로 띄울지는
// StoryCutinDirector 가 StoryBeat 를 보고 정한다.
public partial class StoryCutinHud : CanvasLayer
{
    public static StoryCutinHud Instance { get; private set; }

    // 자막 띠(112)보다 위, 통화창(114)보다 아래. 컷인은 통화 중에 뜨지 않으므로
    // 둘이 겹칠 일은 없고, 혹시 겹치면 통화가 위로 온다.
    private const int CanvasLayerIndex = 113;

    // 통화창과 같은 타이핑 속도.
    private const double CharDelay = 0.028;
    private const float FadeSeconds = 0.32f;

    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Amber = new(1f, 0.78f, 0.35f);

    private Control _root;
    private TextureRect _floorShade;
    private EmployeeStandingPortrait _left, _right;
    private Panel _panel;
    private HologramFrame _frame;
    private Label _speaker;
    private RichTextLabel _message;
    private Label _arrow;

    private string _fullText = "";
    private double _typeTimer;
    private int _shownChars;
    private bool _typing;
    private float _arrowTime;

    // 지금 줄을 넘길 수 있는가 — PrologueAdvanceInput 이 이 값을 보고 클릭을 가져간다.
    public bool IsWaitingForInput { get; private set; }
    public bool IsShown => _root?.Visible ?? false;
    // 검사용.
    public string CurrentText => _fullText;
    public bool IsTyping => _typing;
    public string CurrentSpeakerName => _speaker?.Text ?? "";
    public EmployeeStandingPortrait LeftPortrait => _left;
    public EmployeeStandingPortrait RightPortrait => _right;

    // 한 줄을 다 넘겼다 — 디렉터가 다음 줄로 간다.
    public event System.Action LineAdvanced;
    // 플레이어가 줄을 넘긴 횟수. 디렉터는 이 값이 바뀌는 것을 보고 다음 줄로 간다
    // (TutorialDirector 와 같은 폴링 방식 — 이벤트 구독이 꼬여 교육이 멈추는 일이 없게).
    public int AdvanceCount { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        Layer = CanvasLayerIndex;
        BuildUi();
        Visible = true;
    }

    public override void _ExitTree()
    {
        // 씬이 내려가도 전역 입 모양 상태가 남지 않게 한다.
        if (EmployeeMouthAnimator.Speaker == _left?.EmployeeId
            || EmployeeMouthAnimator.Speaker == _right?.EmployeeId)
            EmployeeMouthAnimator.Reset();
        if (Instance == this) Instance = null;
    }

    private void BuildUi()
    {
        var font = ViewFont.Default;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // 아래쪽만 아주 옅게 누른다 — 3D 화면을 가리지 않으면서 글자가 읽히게.
        // 단색 띠로 깔면 화면을 가로지르는 직선이 생긴다. 위에서 아래로 서서히 짙어지는
        // 그라데이션이어야 관제 화면 위에 얹힌 티가 나지 않는다.
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.004f, 0.012f, 0.016f, 0f));
        grad.SetColor(1, new Color(0.004f, 0.012f, 0.016f, 0.46f));
        _floorShade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = grad, Width = 4, Height = 256,
                FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.46f, AnchorBottom = 1f,
        };
        _root.AddChild(_floorShade);

        // 스탠딩 두 자리. 자리는 고정이 아니다 — 혼자면 화면 한가운데, 둘이면 좌우로
        // 갈라선다(LayoutPortraits). 이동은 전부 부드러운 트윈이다.
        _left = NewPortrait();
        _right = NewPortrait();

        // 대사창 — 통화창과 같은 홀로그램 톤.
        _panel = new Panel
        {
            AnchorLeft = 0.20f, AnchorRight = 0.80f, AnchorTop = 0.715f, AnchorBottom = 0.865f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.09f, 0.11f, 0.82f),
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        _root.AddChild(_panel);

        _frame = new HologramFrame { MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(_frame);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.OffsetLeft = 26; col.OffsetTop = 16; col.OffsetRight = -26; col.OffsetBottom = -14;
        col.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(col);

        _speaker = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _speaker.AddThemeFontOverride("font", font);
        _speaker.AddThemeFontSizeOverride("font_size", ViewFont.FS(19));
        _speaker.AddThemeColorOverride("font_color", Amber);
        col.AddChild(_speaker);

        _message = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _message.AddThemeFontOverride("normal_font", font);
        _message.AddThemeFontSizeOverride("normal_font_size", ViewFont.FS(18));
        _message.AddThemeColorOverride("default_color", new Color(0.82f, 0.96f, 0.98f));
        col.AddChild(_message);

        // ▶ — "지금 아무 데나 눌러도 된다"는 표시. 자막 띠와 같은 모양으로 살랑인다.
        _arrow = new Label
        {
            Text = "▶", MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visible = false,
        };
        _arrow.AddThemeFontOverride("font", font);
        _arrow.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        _arrow.AddThemeColorOverride("font_color", Cyan with { A = 0.8f });
        _arrow.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _arrow.OffsetLeft = -64; _arrow.OffsetTop = -36; _arrow.OffsetRight = -18; _arrow.OffsetBottom = -10;
        _panel.AddChild(_arrow);
    }

    // --- 스탠딩 자리 ------------------------------------------------------
    //
    // 한 명이면 화면 한가운데. 두 명이 되면 먼저 있던 쪽이 왼쪽으로 부드럽게 비켜 주고
    // 새로 오는 쪽이 오른쪽에서 페이드로 들어온다. 한 명이 빠지면 남은 한 명이 다시
    // 가운데로 돌아온다. 이동은 트윈, 등장 · 퇴장은 전부 페이드다.

    private const float BoxWidthFrac = 0.42f;   // 자리 하나의 가로 폭(화면 비율)
    private const float BoxTopFrac = 0.14f;     // 자리 위선
    private const float SingleCenterFrac = 0.5f;
    private const float PairLeftFrac = 0.30f;
    private const float PairRightFrac = 0.70f;
    private const float SlideSeconds = 0.42f;
    // 전원에게 똑같이 걸리는 확대 — 여섯 명의 키 비율은 그대로 두고 크기만 키운다.
    private const float PortraitZoom = 1.25f;

    private EmployeeStandingPortrait NewPortrait()
    {
        var p = new EmployeeStandingPortrait { Zoom = PortraitZoom };
        _root.AddChild(p);
        return p;
    }

    // 그 자리에 사람이 서 있기로 되어 있는가. 페이드 알파가 아니라 이 값으로 센다 —
    // 페이드가 도는 중에도 "지금 몇 명짜리 구도인가" 가 흔들리면 안 된다.
    private bool _leftOn, _rightOn;

    private int ActiveCount => (_leftOn ? 1 : 0) + (_rightOn ? 1 : 0);

    // 그 자리가 지금 서야 할 가로 중심(화면 비율).
    private float CenterFracOf(EmployeeStandingPortrait p)
    {
        if (ActiveCount < 2) return SingleCenterFrac;
        return p == _left ? PairLeftFrac : PairRightFrac;
    }

    private Vector2 TargetPosOf(EmployeeStandingPortrait p, Vector2 vp, float boxW, float top) =>
        new(vp.X * CenterFracOf(p) - boxW / 2f, top);

    private void LayoutPortraits(bool animate)
    {
        Vector2 vp = _root.Size;
        if (vp.X <= 0f || vp.Y <= 0f) return;

        float boxW = vp.X * BoxWidthFrac;
        float top = vp.Y * BoxTopFrac;
        var boxSize = new Vector2(boxW, vp.Y - top);
        _lastViewport = vp;

        Place(_left, _leftOn);
        Place(_right, _rightOn);

        void Place(EmployeeStandingPortrait p, bool on)
        {
            p.Size = boxSize;
            var target = TargetPosOf(p, vp, boxW, top);
            // 빠지는 중인 자리는 움직이지 않는다 — 사라지면서 미끄러지면 산만하다.
            if (!on) return;
            // 처음 들어오는 자리는 트윈 없이 제자리에서 페이드로 나타난다
            // (화면 밖에서 미끄러져 들어오면 안 된다).
            if (!animate || !p.IsVisibleOnScreen) p.SnapTo(target);
            else p.MoveTo(target, SlideSeconds);
        }
    }

    // 새로 들어오는 자리를 **최종 자리에** 미리 놓는다(페이드로만 나타나게).
    private void PlaceEntering(EmployeeStandingPortrait p)
    {
        Vector2 vp = _root.Size;
        if (vp.X <= 0f || vp.Y <= 0f) return;
        float boxW = vp.X * BoxWidthFrac;
        float top = vp.Y * BoxTopFrac;
        p.Size = new Vector2(boxW, vp.Y - top);
        p.SnapTo(TargetPosOf(p, vp, boxW, top));
    }

    // 창 크기가 바뀌면 자리도 다시 잡는다(트윈 없이 즉시).
    private Vector2 _lastViewport = Vector2.Zero;

    // --- 묶음 여닫기 ------------------------------------------------------

    // 컷인을 띄운다(아직 대사는 없다). 등장할 자리를 미리 비워 둔다.
    public void BeginBeat()
    {
        _root.Visible = true;
        _speaker.Text = "";
        _message.Text = "";
        _fullText = "";
        _typing = false;
        IsWaitingForInput = false;
        _leftOn = _rightOn = false;
        _left.ClearNow();
        _right.ClearNow();
        LayoutPortraits(animate: false);
        _root.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(_root, "modulate:a", 1f, FadeSeconds);
    }

    // 묶음을 닫는다. 끝나면 _root 가 꺼지고 전역 입 모양도 평소로 돌아간다.
    public async System.Threading.Tasks.Task EndBeat()
    {
        IsWaitingForInput = false;
        _typing = false;
        EmployeeMouthAnimator.Reset();
        Sfx.Instance?.StopVoiceBlip();
        if (!IsInstanceValid(this)) return;

        var tw = CreateTween();
        tw.TweenProperty(_root, "modulate:a", 0f, FadeSeconds);
        await ToSignal(tw, Tween.SignalName.Finished);
        if (!IsInstanceValid(this)) return;
        HideNow();
    }

    // 예외 · 중단 — 연출 없이 즉시 걷는다(페일세이프 경로).
    public void HideNow()
    {
        IsWaitingForInput = false;
        _typing = false;
        if (_root != null) { _root.Visible = false; _root.Modulate = new Color(1, 1, 1, 0); }
        _speaker.Text = "";
        _message.Text = "";
        _fullText = "";
        _leftOn = _rightOn = false;
        _left?.ClearNow();
        _right?.ClearNow();
        EmployeeMouthAnimator.Reset();
        Sfx.Instance?.StopVoiceBlip();
    }

    // 한 자리를 퇴장시킨다 — 페이드로 사라지고, 남은 한 명은 가운데로 부드럽게 돌아온다.
    public void ExitPortrait(CutinSide side)
    {
        var slot = side == CutinSide.Left ? _left : _right;
        bool on = side == CutinSide.Left ? _leftOn : _rightOn;
        if (!on) return;

        if (side == CutinSide.Left) _leftOn = false; else _rightOn = false;
        slot.FadeOut(FadeSeconds);
        // 남은 사람이 가운데로 가는 것은 지금 바로 시작한다 — 사라지는 것과 같이 보여야
        // "한 명이 빠지고 화면이 한 명짜리 구도로 돌아간다" 가 한 동작으로 읽힌다.
        LayoutPortraits(animate: true);
    }

    // --- 한 줄 ------------------------------------------------------------

    public void ShowLine(StoryLine line)
    {
        if (line == null) return;

        // 먼저 빠질 사람을 내보낸다 — 그래야 남은 한 명이 가운데로 돌아간다.
        if (line.ExitSide.HasValue) ExitPortrait(line.ExitSide.Value);

        var slot = line.Side == CutinSide.Left ? _left : _right;
        string speakerId = line.SpeakerEmployeeId ?? "";

        if (!string.IsNullOrEmpty(speakerId))
        {
            bool entering = slot == _left ? !_leftOn : !_rightOn;

            // 같은 자리에 다른 사람이 서 있으면 교체, 같은 사람이면 표정만 바꾼다.
            if (slot.EmployeeId != speakerId) slot.SetEmployee(speakerId, line.Expression);
            else if (!string.IsNullOrEmpty(line.Expression)) slot.SetExpression(line.Expression);

            if (entering)
            {
                // 이 자리가 켜졌으니 구도가 1인 → 2인으로 바뀐다. 먼저 서 있던 사람은
                // 가운데에서 옆으로 부드럽게 비켜 주고(LayoutPortraits), 새로 오는 쪽은
                // **최종 자리에서** 페이드로만 나타난다(미끄러져 들어오지 않는다).
                if (slot == _left) _leftOn = true; else _rightOn = true;
                slot.Modulate = new Color(1, 1, 1, 0);
                LayoutPortraits(animate: true);
                PlaceEntering(slot);
                slot.FadeIn(FadeSeconds);
            }
            else LayoutPortraits(animate: true);

            var def = FacilitySimulation.Instance?.GetEmployeeDef(speakerId);
            _speaker.Text = def?.Codename ?? speakerId;
        }
        else
        {
            // 화자가 없는 줄(나레이션) — 이름만 비운다. 서 있는 스탠딩은 그대로 둔다.
            _speaker.Text = "";
        }

        _fullText = line.Text ?? "";
        _message.Text = DialogueHighlight.Colorize(_fullText);
        _message.VisibleCharacters = 0;
        _typeTimer = 0;
        _shownChars = 0;
        _typing = true;
        IsWaitingForInput = true;

        // 말하는 쪽만 입이 움직인다. 표정은 스크립트가 지정했으면 그 표정으로 고정된다.
        if (!string.IsNullOrEmpty(speakerId))
            EmployeeMouthAnimator.StartTalking(speakerId, _fullText,
                string.IsNullOrEmpty(line.Expression) ? null : line.Expression);
        else
            EmployeeMouthAnimator.StopTalking();

        // 자동 판정으로 정해진 표정을 스탠딩에도 반영한다(스크립트가 지정하지 않은 경우).
        if (!string.IsNullOrEmpty(speakerId) && string.IsNullOrEmpty(line.Expression))
            slot.SetExpression(EmployeeMouthAnimator.Expression);
    }

    // 문장이 다 찍혔는가.
    public bool LineComplete => !_typing;

    // 프롤로그 · 교육과 같은 규칙(문서 §33).
    //   타이핑 중이면 그 문장을 즉시 완성하고, 완성된 상태면 다음 줄로 넘긴다.
    public void RequestAdvance()
    {
        if (!IsWaitingForInput) return;

        if (_typing)
        {
            FinishTyping();
            return;
        }
        IsWaitingForInput = false;
        AdvanceCount++;
        LineAdvanced?.Invoke();
    }

    // 디렉터가 스스로 줄을 닫는다(HoldSeconds 가 지났을 때). 플레이어가 넘긴 것이
    // 아니므로 AdvanceCount 는 올리지 않는다.
    public void ConsumeLine()
    {
        if (_typing) FinishTyping();
        IsWaitingForInput = false;
    }

    private void FinishTyping()
    {
        // 남은 글자의 문장부호까지 입 모양에 반영한 뒤 입을 닫는다.
        for (int i = _shownChars; i < _fullText.Length; i++)
            EmployeeMouthAnimator.NoticeCharacter(_fullText[i]);
        _shownChars = _fullText.Length;
        _message.VisibleCharacters = -1;
        _typing = false;
        EmployeeMouthAnimator.StopTalking();
        Sfx.Instance?.StopVoiceBlip();
    }

    public override void _Process(double delta)
    {
        if (!IsShown) return;

        // 창 크기가 바뀌면 자리를 다시 잡는다(트윈 없이 즉시).
        if (_root.Size != _lastViewport) LayoutPortraits(animate: false);

        // 입 모양은 전역 하나다. 같은 프레임에 두 번 돌지 않도록 막혀 있다(EmployeeMouthAnimator).
        EmployeeMouthAnimator.Tick(delta);

        if (_typing)
        {
            _typeTimer += delta;
            int shown = Mathf.Min(_fullText.Length, (int)(_typeTimer / CharDelay));
            if (shown != _shownChars)
            {
                for (int i = _shownChars; i < shown; i++)
                {
                    Sfx.Instance?.PlayVoiceBlip(_speakerVoiceId, _fullText[i]);
                    EmployeeMouthAnimator.NoticeCharacter(_fullText[i]);
                }
                _shownChars = shown;
            }
            _message.VisibleCharacters = shown;
            if (shown >= _fullText.Length) FinishTyping();
        }

        bool canAdvance = IsWaitingForInput && !_typing;
        _arrow.Visible = canAdvance;
        if (canAdvance)
        {
            _arrowTime += (float)delta;
            _arrow.Modulate = new Color(1f, 1f, 1f,
                0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(_arrowTime * 3.4f)));
        }
    }

    // 보이스는 지금 말하는 직원의 것으로 울린다(기존 통화와 같은 재생 경로).
    private string _speakerVoiceId => EmployeeMouthAnimator.Speaker;
}
