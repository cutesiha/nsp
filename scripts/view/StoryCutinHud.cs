using Godot;
using NSP.Core;
using NSP.Data;
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

    // 이름은 그 직원의 고유색으로 쓴다 — 배치표 · 지도 · CCTV 목록이 쓰는
    // EmployeeDef.IconColor 와 같은 색이다(색을 새로 정의하지 않는다).
    // 다만 그 색을 그대로 쓰면 고양이(진한 파랑)처럼 어두운 창 위에서 읽히지 않는다.
    // 색조만 가져오고 밝기는 글자용으로 올린다. 화자가 없는 줄은 기존 호박색 그대로.
    private static Color NameColorOf(EmployeeDef def)
    {
        if (def == null) return Amber;
        Color c = def.IconColor;
        return Color.FromHsv(c.H, Mathf.Min(c.S, 0.62f), Mathf.Max(c.V, 0.95f));
    }

    private Control _root;
    private TextureRect _floorShade;
    private EmployeeStandingPortrait _left, _right;
    private Panel _panel;
    private HologramFrame _frame;
    private VBoxContainer _col;
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

    // ── 어디에 그리는가 ──────────────────────────────────────────────
    //
    // 기본은 이 CanvasLayer(화면 전체) — 프롤로그 · DAY0 교육이 쓰는 자리다.
    // DAY1~5 메인 스토리는 **모니터2 영상 안**에서 돌아야 하므로(연출 규칙 §3),
    // 스탠딩과 대사창이 든 _root 를 그 화면(StoryMonitorView.StoryLayer)으로 옮겨 붙인다.
    //
    // 옮기는 것은 _root 뿐이다. 관리자 선택지(_choiceBox)는 모니터 안에 넣지 않는다 —
    // 그건 직원들의 대화가 아니라 플레이어가 하는 조작이라, 늘 메인 UI 자리에 뜬다(§8).
    private Control _target;

    public bool IsOnMonitor => _target != null;

    public void SetRenderTarget(Control target)
    {
        if (_target == target) return;
        var parent = _root.GetParent();
        parent?.RemoveChild(_root);

        _target = target;
        if (target != null && IsInstanceValid(target))
        {
            target.AddChild(_root);
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _root.Position = Vector2.Zero;
            _root.Size = target.Size;
        }
        else
        {
            AddChild(_root);
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _root.Position = Vector2.Zero;
        }
        ApplyScale(_target != null);
        LayoutPortraits(animate: false);
    }

    // 화면 전체(CanvasLayer)와 모니터2 영상(744×460)은 크기가 다섯 배 가까이 차이 난다.
    // 글자 크기와 대사창 자리를 그대로 두면 모니터 안에서 글이 칸을 넘치고 스탠딩이
    // 화면을 통째로 가린다(연출 규칙 §15 — 휴게실 3D 가 일부는 보여야 한다).
    private void ApplyScale(bool onMonitor)
    {
        int nameSize = onMonitor ? ViewFont.S(15) : ViewFont.FS(19);
        int bodySize = onMonitor ? ViewFont.S(14) : ViewFont.FS(18);
        _speaker.AddThemeFontSizeOverride("font_size", nameSize);
        _message.AddThemeFontSizeOverride("normal_font_size", bodySize);
        _arrow.AddThemeFontSizeOverride("font_size", onMonitor ? ViewFont.S(12) : ViewFont.FS(17));

        // 대사창 — 모니터 안에서는 좌우를 거의 다 쓰고 영상 **맨 아래**에 붙인다.
        // 띄워 두면 자막과 CCTV 화면 사이에 검은 띠가 남아 따로 떠 있는 것처럼 보인다.
        _panel.AnchorLeft = onMonitor ? 0.03f : 0.20f;
        _panel.AnchorRight = onMonitor ? 0.97f : 0.80f;
        _panel.AnchorTop = onMonitor ? 0.757f : 0.715f;
        _panel.AnchorBottom = onMonitor ? 0.997f : 0.865f;
        _panel.OffsetLeft = _panel.OffsetRight = _panel.OffsetTop = _panel.OffsetBottom = 0f;

        // 글이 들어가는 칸. 전체화면 값은 예전 그대로다 — 손대면 프롤로그 · DAY0 교육
        // 화면의 글 자리가 같이 움직인다.
        float topGap = onMonitor ? 3f : 16f;
        float side = onMonitor ? 16f : 26f;
        _col.OffsetLeft = side; _col.OffsetRight = -side;
        _col.OffsetTop = topGap; _col.OffsetBottom = onMonitor ? -8f : -14f;
        _col.AddThemeConstantOverride("separation", onMonitor ? 5 : 10);

        // 이름 띠 — 높이를 **이름 글자에서 거꾸로** 잡는다. 30px 로 고정해 두면 글자가
        // 커질수록 이름이 띠 아래로 삐져나온다(실제로 두 화면 다 그랬다).
        // 이름이 선 자리를 옮기는 게 아니라, 그 자리를 덮을 만큼만 띠를 잡는다.
        _frame.BandHeight = Mathf.Round(
            topGap + ViewFont.Default.GetHeight(nameSize) + (onMonitor ? 3f : 5f));
        // 폭은 모니터 안에서만 아주 살짝 줄인다(전체화면은 예전처럼 창 폭을 다 쓴다).
        _frame.BandInset = onMonitor ? 8f : 0f;

        var box = (StyleBoxFlat)_panel.GetThemeStylebox("panel");
        float m = onMonitor ? 10f : 26f;
        box.ContentMarginLeft = box.ContentMarginRight = m;
        box.ContentMarginTop = onMonitor ? 6f : 18f;
        box.ContentMarginBottom = onMonitor ? 6f : 18f;

        // 스탠딩 — 모니터 안에서는 작게. 화자 강조가 목적이지 화면을 채우는 게 아니다.
        _left.Zoom = _right.Zoom = onMonitor ? MonitorPortraitZoom : PortraitZoom;
    }

    // 모니터2 안에서 쓰는 스탠딩 배율.
    //
    // **1 보다 작으면 안 된다.** StandingPortraitLayout 은 발끝을 자리 아래에 붙인 뒤
    // avail*(zoom-1) 만큼 더 내리므로, 1 보다 작으면 그만큼 위로 떠서 원화가 허공에
    // 뜬 것처럼 보인다(실제로 0.62 를 써서 그랬다).
    // 1 이면 끝다리가 자리 아래선에 딱 닿고, 1 보다 크면 그만큼 더 내려가 잘린다 —
    // 1.18 은 허벅지 끝이 CCTV 영상 아래로 조금 넘어가 살짝만 보이는 값이다.
    private const float MonitorPortraitZoom = 1.28f;

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

        _col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _col.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(_col);
        var col = _col;

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

        BuildChoiceUi(font);
    }

    // ── 관리자 선택지 ────────────────────────────────────────────────────
    //
    // 세계관상 이것은 **전화 응답**이다 — 휴게실 공용 전화가 연결돼 있고 관리자가 답한다.
    // 새 통신 수단을 만들지 않는다(NSP_STORY_TUTORIAL_REWORK §2.1).
    //
    // 선택지 셋이 한 번에 뜬다. 마우스로 눌러도 되고 숫자키 1 · 2 · 3 으로도 고른다
    // (키 입력은 PrologueAdvanceInput 이 모아 RequestChoice 로 넘긴다).
    // 고르는 동안 근무는 멈춰 있고, 마지막으로 말한 직원의 스탠딩은 입을 다문 채 그대로 선다.

    private Control _choiceBox;
    private Label _choicePrompt;
    private readonly System.Collections.Generic.List<Button> _choiceButtons = new();
    private int _chosen = -1;

    // 지금 선택지가 떠 있는가 — 떠 있는 동안에는 대사 넘기기가 먹지 않는다.
    public bool IsChoosing => _choiceBox?.Visible ?? false;
    public int ChosenIndex => _chosen;
    public int ChoiceCount { get; private set; }

    private void BuildChoiceUi(Font font)
    {
        // **_root 가 아니라 이 CanvasLayer 에 직접 붙인다.**
        // _root 는 메인 스토리 동안 모니터2 영상 안으로 들어가는데(SetRenderTarget),
        // 선택지까지 따라 들어가면 모니터 테두리에 잘려 세 개가 다 보이지 않는다.
        // 선택지는 직원들의 대화가 아니라 플레이어의 조작이므로 늘 메인 UI 자리에 뜬다(§8).
        _choiceBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _choiceBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_choiceBox);

        var col = new VBoxContainer
        {
            AnchorLeft = 0.20f, AnchorRight = 0.80f, AnchorTop = 0.715f, AnchorBottom = 0.94f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        col.AddThemeConstantOverride("separation", 8);
        _choiceBox.AddChild(col);

        _choicePrompt = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _choicePrompt.AddThemeFontOverride("font", font);
        _choicePrompt.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        _choicePrompt.AddThemeColorOverride("font_color", Amber);
        // 물음은 스탠딩 위에 겹쳐 뜬다 — 바탕이 없으므로 검은 테두리를 둘러야 읽힌다.
        _choicePrompt.AddThemeColorOverride("font_outline_color", Colors.Black);
        _choicePrompt.AddThemeConstantOverride("outline_size", 5);
        col.AddChild(_choicePrompt);

        for (int i = 0; i < MaxOptions; i++)
        {
            int idx = i;
            var b = new Button
            {
                Text = "", Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 46),
            };
            b.AddThemeFontOverride("font", font);
            b.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
            b.AddThemeColorOverride("font_color", new Color(0.82f, 0.96f, 0.98f));
            b.AddThemeColorOverride("font_hover_color", Cyan);
            b.AddThemeColorOverride("font_pressed_color", Cyan);
            b.AddThemeStyleboxOverride("normal", OptionBox(Cyan with { A = 0.45f }));
            b.AddThemeStyleboxOverride("hover", OptionBox(Cyan));
            b.AddThemeStyleboxOverride("pressed", OptionBox(Cyan));
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            b.Pressed += () => Choose(idx);
            col.AddChild(b);
            _choiceButtons.Add(b);
        }
    }

    // 한 화면에 같이 띄우는 선택지 수.
    public const int MaxOptions = 3;

    private static StyleBoxFlat OptionBox(Color border) => new()
    {
        BgColor = new Color(0.03f, 0.09f, 0.11f, 0.88f),
        BorderColor = border,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
    };

    // 선택지를 띄운다. 고를 때까지 _chosen 은 -1 이다.
    public void ShowChoice(StoryChoice choice)
    {
        if (choice == null) return;
        _chosen = -1;
        ChoiceCount = Mathf.Min(choice.Options.Count, MaxOptions);
        _choicePrompt.Text = choice.Prompt ?? "";
        _choicePrompt.Visible = !string.IsNullOrEmpty(choice.Prompt);

        for (int i = 0; i < _choiceButtons.Count; i++)
        {
            bool on = i < ChoiceCount;
            _choiceButtons[i].Visible = on;
            _choiceButtons[i].MouseFilter = on ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            if (on) _choiceButtons[i].Text = $"{i + 1}.  {choice.Options[i].Text}";
        }
        _choiceBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _choiceBox.Visible = true;
        // 대사창은 내린다 — 같은 자리를 쓰므로 그대로 두면 물음과 지난 대사가 겹쳐 읽힌다.
        // 스탠딩은 그대로 서 있는다(마지막으로 말한 직원이 답을 기다리는 그림).
        _panel.Visible = false;
        // 고르는 동안에는 대사 넘기기 화살표를 내린다 — 클릭이 둘 다로 가면 안 된다.
        _arrow.Visible = false;
        IsWaitingForInput = false;
        // 마지막으로 말한 직원은 그대로 서 있되 입은 다문다.
        EmployeeMouthAnimator.StopTalking();
    }

    // 숫자키 · 마우스 양쪽이 들어오는 입구.
    public void Choose(int index)
    {
        if (!IsChoosing || index < 0 || index >= ChoiceCount || _chosen >= 0) return;
        _chosen = index;
        Sfx.Instance?.Play("tick", -10f);
        HideChoice();
    }

    public void HideChoice()
    {
        if (_choiceBox == null) return;
        _choiceBox.Visible = false;
        if (_panel != null) _panel.Visible = true;
        foreach (var b in _choiceButtons) b.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    // --- 스탠딩 자리 ------------------------------------------------------
    //
    // 한 명이면 화면 한가운데. 두 명이 되면 먼저 있던 쪽이 왼쪽으로 부드럽게 비켜 주고
    // 새로 오는 쪽이 오른쪽에서 페이드로 들어온다. 한 명이 빠지면 남은 한 명이 다시
    // 가운데로 돌아온다. 이동은 트윈, 등장 · 퇴장은 전부 페이드다.

    private const float BoxWidthFrac = 0.42f;   // 자리 하나의 가로 폭(화면 비율)
    private const float BoxTopFrac = 0.14f;     // 자리 위선
    // 모니터2 안에서 쓰는 자리.
    //
    // 위선(TopFrac)이 곧 스탠딩의 키다 — StandingPortraitLayout 은 자리의 **아래**에
    // 발끝을 붙이고 위로 쌓아 올리기 때문이다. 0.175 면 제일 큰 원화가 영상 높이의
    // 약 80% 를 차지하고 끝다리가 CCTV 영상 아래 끝에 닿는다.
    // 가로 폭은 원화가 잘리지 않을 만큼 넉넉히 둔다(폭은 크기에 영향을 주지 않고
    // ClipContents 로 잘라내기만 한다).
    private const float MonitorBoxWidthFrac = 0.44f;
    private const float MonitorBoxTopFrac = 0.16f;
    private const float MonitorPairLeftFrac = 0.33f;
    private const float MonitorPairRightFrac = 0.67f;
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
        bool onMon = _target != null;
        return p == _left
            ? (onMon ? MonitorPairLeftFrac : PairLeftFrac)
            : (onMon ? MonitorPairRightFrac : PairRightFrac);
    }

    private Vector2 TargetPosOf(EmployeeStandingPortrait p, Vector2 vp, float boxW, float top) =>
        new(vp.X * CenterFracOf(p) - boxW / 2f, top);

    private void LayoutPortraits(bool animate)
    {
        Vector2 vp = _root.Size;
        if (vp.X <= 0f || vp.Y <= 0f) return;

        bool onMon = _target != null;
        float boxW = vp.X * (onMon ? MonitorBoxWidthFrac : BoxWidthFrac);
        float top = vp.Y * (onMon ? MonitorBoxTopFrac : BoxTopFrac);
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
        HideChoice();
        HideNowInner();
    }

    private void HideNowInner()
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
            _speaker.AddThemeColorOverride("font_color", NameColorOf(def));
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
