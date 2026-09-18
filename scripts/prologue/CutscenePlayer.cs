using System;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 왼쪽 CRT 안에서 재생되는 '영상'.
//
// 실제 CCTV 시스템(CCTVMonitorView)과는 완전히 별개다 — 시뮬레이션 상태를 전혀 읽지 않고
// PrologueScript 의 슬라이드 데이터만 재생한다. 화면은 "컴퓨터로 녹화 영상을 트는 창"처럼
// 보이게 만든다: 제목 표시줄 · 영상 영역 · 재생바/타임코드 · 상태 표시줄.
//
// 아트 교체: 슬라이드의 image:(배경) / figure:(인물 일러스트) 경로에 파일이 있으면 그 이미지를
// 쓰고, 없으면 같은 자리에 "[ IMAGE ] / [ FIGURE ] + 장면 설명" 임시 패널을 그린다.
public partial class CutscenePlayer : Control
{
    public static CutscenePlayer Instance { get; private set; }

    public event Action Finished;
    public bool IsPlaying => _cutscene != null;
    // 대사 슬라이드는 자동으로 넘어가지 않는다 — 스페이스/엔터/클릭을 기다리는 중인가.
    public bool IsWaitingForInput { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Ink = new(0.88f, 0.92f, 0.90f);
    private static readonly Color Dim = new(0.48f, 0.56f, 0.55f);
    private static readonly Color Amber = new(0.95f, 0.76f, 0.30f);
    private static readonly Color Chrome = new(0.105f, 0.115f, 0.135f);

    // --- 영상 창 레이아웃 ---------------------------------------------------
    private static readonly Rect2 TitleBar = new(0f, 0f, 800f, 30f);
    private static readonly Rect2 VideoArea = new(10f, 36f, 780f, 448f);
    private static readonly Rect2 ControlBar = new(10f, 490f, 780f, 46f);
    private static readonly Rect2 StatusBar = new(10f, 542f, 780f, 28f);
    private static readonly Rect2 FigureBox = new(462f, 80f, 300f, 360f);
    private static readonly Rect2 SubtitleBox = new(26f, 382f, 748f, 96f);

    private Font _font;
    private Control _frame;          // 흔들림이 걸리는 영상 내용물
    private TextureRect _image;
    private PlaceholderPanel _placeholder;
    private TextureRect _figure;
    private PlaceholderPanel _figurePlaceholder;
    private Label _title;
    private Label _recDot;
    private Label _overlay;
    private Panel _subtitleBox;
    private Label _speaker;
    private Label _text;
    private Label _clickHint;
    private ColorRect _tint;
    private ColorRect _fade;
    private GlitchBars _glitch;
    private PlayerChrome _chrome;

    private PrologueScript.Cutscene _cutscene;
    private PrologueScript.Slide _slide;
    private int _index = -1;
    private double _slideElapsed;
    private double _slideHold;
    private double _typeSeconds;
    private PrologueScript.SlideFx _fx;
    private double _fxTime;
    private double _joltUntil;
    private bool _impactFallStarted;

    // 재생바용 — 컷씬 전체 길이와 지금까지 흐른 시간(진짜 영상처럼 보이게).
    private double _totalSeconds;
    private double _elapsedBeforeSlide;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;
        BuildUi();
        SetProcess(true);
        Clear();
    }

    public override void _ExitTree()
    {
        StopAllLoops();
        if (Instance == this) Instance = null;
    }

    // --- 재생 제어 -------------------------------------------------------

    public void Play(string cutsceneId)
    {
        _cutscene = PrologueScript.GetCutscene(cutsceneId);
        if (_cutscene == null || _cutscene.Slides.Count == 0)
        {
            GD.PushWarning($"CutscenePlayer: 컷씬 '{cutsceneId}' 를 찾지 못했습니다.");
            _cutscene = null;
            Finished?.Invoke();
            return;
        }
        Visible = true;
        _chrome.Visible = true;
        _chrome.FileName = FileNameFor(cutsceneId);

        // 전체 길이를 미리 재 둔다 — 재생바/타임코드가 진짜 영상처럼 움직이게.
        _totalSeconds = 0;
        foreach (var s in _cutscene.Slides) _totalSeconds += NominalSeconds(s);
        _elapsedBeforeSlide = 0;

        _index = -1;
        Advance();
    }

    // 화면을 비운다(영상 종료 후 검은 CRT 상태).
    public void Clear()
    {
        StopAllLoops();
        _cutscene = null;
        _slide = null;
        _index = -1;
        IsWaitingForInput = false;
        _impactFallStarted = false;
        _image.Visible = false;
        _placeholder.Visible = false;
        _figure.Visible = false;
        _figurePlaceholder.Visible = false;
        _title.Text = "";
        _recDot.Visible = false;
        _overlay.Text = "";
        _subtitleBox.Visible = false;
        _clickHint.Visible = false;
        _tint.Color = _tint.Color with { A = 0f };
        _fade.Color = _fade.Color with { A = 0f };
        _glitch.Amount = 0f;
        _glitch.QueueRedraw();
        _frame.Position = Vector2.Zero;
        _chrome.Visible = false;
        _chrome.Progress = 0f;
        _chrome.QueueRedraw();
    }

    // 스페이스/엔터/클릭. 타이핑 중이면 먼저 문장을 다 드러내고, 이미 다 떴으면 다음 슬라이드로.
    public void RequestAdvance()
    {
        if (_cutscene == null) return;
        if (IsTyping()) { FinishTyping(); return; }
        Advance();
    }

    private bool IsTyping() =>
        (_subtitleBox.Visible && _text.VisibleRatio < 1f) || (_overlay.Text.Length > 0 && _overlay.VisibleRatio < 1f);

    private void FinishTyping()
    {
        _text.VisibleRatio = 1f;
        _overlay.VisibleRatio = 1f;
        _slideElapsed = Math.Max(_slideElapsed, _typeSeconds);
    }

    private void Advance()
    {
        if (_cutscene == null) return;
        if (_slide != null) _elapsedBeforeSlide += NominalSeconds(_slide);

        _index++;
        if (_index >= _cutscene.Slides.Count)
        {
            StopAllLoops();
            _cutscene = null;
            _slide = null;
            IsWaitingForInput = false;
            Finished?.Invoke();
            return;
        }
        ShowSlide(_cutscene.Slides[_index]);
    }

    private void ShowSlide(PrologueScript.Slide s)
    {
        _slide = s;
        _slideElapsed = 0;
        _fxTime = 0;
        _slideHold = s.Hold;
        _fx = s.Fx;
        _impactFallStarted = false;
        _joltUntil = s.Jolt > 0f ? s.Jolt : 0.0;

        // 배경 이미지: 최종 파일이 있으면 그걸, 없으면 같은 자리에 임시 패널.
        var tex = LoadImage(s.ImagePath);
        _image.Texture = tex;
        _image.Visible = tex != null;
        _placeholder.Visible = tex == null && (!string.IsNullOrEmpty(s.ImageNote) || !string.IsNullOrEmpty(s.ImagePath));
        _placeholder.Note = s.ImageNote;
        _placeholder.QueueRedraw();

        // 인물 일러스트(배경 앞에 선다).
        var figTex = LoadImage(s.FigurePath);
        bool hasFigure = figTex != null || !string.IsNullOrEmpty(s.FigureNote) || !string.IsNullOrEmpty(s.FigurePath);
        _figure.Texture = figTex;
        _figure.Visible = figTex != null;
        _figurePlaceholder.Visible = hasFigure && figTex == null;
        _figurePlaceholder.Note = s.FigureNote;
        _figurePlaceholder.QueueRedraw();

        _title.Text = s.Title ?? "";
        _recDot.Visible = !string.IsNullOrEmpty(s.Title);

        // 경고 문구도 자막과 같은 속도로 타이핑된다.
        _overlay.Text = s.Overlay ?? "";
        _overlay.VisibleRatio = 0f;

        bool hasText = !string.IsNullOrEmpty(s.Text);
        _subtitleBox.Visible = hasText;
        _speaker.Text = s.Speaker ?? "";
        _speaker.Visible = !string.IsNullOrEmpty(s.Speaker);
        _text.Text = s.Text ?? "";
        _text.VisibleRatio = hasText ? 0f : 1f;

        _typeSeconds = Math.Max(
            hasText ? PrologueTextStyle.TypeSeconds(s.Text) : 0f,
            _overlay.Text.Length > 0 ? PrologueTextStyle.TypeSeconds(s.Overlay) : 0f);

        // 대사가 있는 슬라이드는 절대 저절로 넘어가지 않는다(hold: 값은 무시된다).
        // 대사가 없는 컷(몽타주·경고 문구)은 타이핑이 끝난 뒤 hold: 만큼 더 보여주고 넘어가되,
        // hold: 0 으로 적으면 그 컷도 입력을 기다린다.
        IsWaitingForInput = hasText || s.Hold <= 0f;
        _clickHint.Visible = false;

        _tint.Color = TintFor(_fx) with { A = 0f };
        if (_fx != PrologueScript.SlideFx.Blackout && _fx != PrologueScript.SlideFx.Impact)
            _fade.Color = _fade.Color with { A = 0f };

        if (!string.IsNullOrEmpty(s.SfxLoopStop))
        {
            Sfx.Instance?.StopLoop(s.SfxLoopStop);
            if (_activeLoop == s.SfxLoopStop) _activeLoop = "";
        }
        if (!string.IsNullOrEmpty(s.SfxLoopStart))
        {
            Sfx.Instance?.Loop(s.SfxLoopStart, -9f);
            _activeLoop = s.SfxLoopStart;
        }
        if (!string.IsNullOrEmpty(s.Sfx)) Sfx.Instance?.Play(s.Sfx, -5f);

        // 플레이어가 충격을 받는 순간 — 제어실 카메라까지 같이 흔든다.
        if (_fx == PrologueScript.SlideFx.Impact)
        {
            Sfx.Instance?.Play("impact_blunt", -1f);
            ControlRoom3DController.Instance?.ShakeCamera(7.5f, 0.5f);
        }
    }

    private string _activeLoop = "";

    private void StopAllLoops()
    {
        if (string.IsNullOrEmpty(_activeLoop)) return;
        Sfx.Instance?.StopLoop(_activeLoop);
        _activeLoop = "";
    }

    // 재생바 계산용 — 입력 대기 슬라이드는 타이핑 시간을 명목 길이로 본다.
    private static double NominalSeconds(PrologueScript.Slide s)
    {
        double type = Math.Max(
            string.IsNullOrEmpty(s.Text) ? 0f : PrologueTextStyle.TypeSeconds(s.Text),
            string.IsNullOrEmpty(s.Overlay) ? 0f : PrologueTextStyle.TypeSeconds(s.Overlay));
        return Math.Max(Math.Max(s.Hold, type), 0.4);
    }

    private static string FileNameFor(string cutsceneId) => cutsceneId switch
    {
        "prologue_archive" => "ARCHIVE_01.AVI",
        "prologue_disaster" => "ARCHIVE_02_EMERGENCY.AVI",
        "outage_ghost" => "REC_UNKNOWN.AVI",
        _ => cutsceneId.ToUpperInvariant() + ".AVI",
    };

    private static Texture2D LoadImage(string path) =>
        string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path) ? null : GD.Load<Texture2D>(path);

    private static Color TintFor(PrologueScript.SlideFx fx) => fx switch
    {
        PrologueScript.SlideFx.Siren => new Color(0.85f, 0.12f, 0.10f),
        PrologueScript.SlideFx.Cut => new Color(1f, 1f, 1f),
        PrologueScript.SlideFx.Glitch => new Color(0.75f, 0.85f, 1f),
        PrologueScript.SlideFx.Impact => new Color(0.9f, 0.1f, 0.08f),
        _ => new Color(0f, 0f, 0f),
    };

    public override void _Process(double delta)
    {
        if (_cutscene == null) return;

        _slideElapsed += delta;
        _fxTime += delta;

        // 자막/경고 타이핑 — GUIDE-0 와 완전히 같은 속도(PrologueTextStyle).
        if (_subtitleBox.Visible && _text.VisibleRatio < 1f)
            _text.VisibleRatio = PrologueTextStyle.Ratio(_text.Text, _slideElapsed);
        if (_overlay.Text.Length > 0 && _overlay.VisibleRatio < 1f)
            _overlay.VisibleRatio = PrologueTextStyle.Ratio(_overlay.Text, _slideElapsed);

        ApplyFx();
        UpdateChrome();

        // 입력 대기 슬라이드는 타이핑이 끝난 뒤 안내를 띄우고 계속 기다린다.
        if (IsWaitingForInput)
        {
            _clickHint.Visible = !IsTyping();
            return;
        }
        // 경고 문구가 다 찍힌 뒤부터 hold 를 센다 — 다 읽기 전에 넘어가지 않게.
        if (_slideElapsed >= _typeSeconds + _slideHold) Advance();
    }

    private void UpdateChrome()
    {
        double shown = Math.Min(_slideElapsed, NominalSeconds(_slide));
        double elapsed = _elapsedBeforeSlide + shown;
        _chrome.Progress = _totalSeconds > 0.01 ? (float)Mathf.Clamp(elapsed / _totalSeconds, 0.0, 1.0) : 0f;
        _chrome.Elapsed = elapsed;
        _chrome.Total = _totalSeconds;
        _chrome.QueueRedraw();
    }

    private void ApplyFx()
    {
        float t = (float)_fxTime;
        // 슬라이드에 지정된 지속 흔들림(#2 대재난 내내 화면이 떨린다).
        float baseShake = _slide?.Shake ?? 0f;
        Vector2 offset = baseShake > 0f
            ? new Vector2(Mathf.Sin(t * 37f) * baseShake, Mathf.Cos(t * 29f) * baseShake * 0.6f)
            : Vector2.Zero;

        // 슬라이드가 뜨는 순간의 짧은 좌우 흔들림(무전 대사 등).
        if (_joltUntil > 0 && t < _joltUntil)
        {
            float decay = 1f - t / (float)_joltUntil;
            offset.X += Mathf.Sin(t * 95f) * 11f * decay;
        }

        switch (_fx)
        {
            case PrologueScript.SlideFx.Glitch:
                _glitch.Amount = 0.55f + 0.45f * Mathf.Sin(t * 26f);
                _tint.Color = _tint.Color with { A = 0.06f + 0.05f * Mathf.Sin(t * 18f) };
                offset.X += Mathf.Sin(t * 41f) * 3f;
                break;

            case PrologueScript.SlideFx.Siren:
                _tint.Color = _tint.Color with { A = 0.18f + 0.16f * Mathf.Sin(t * 7f) };
                _glitch.Amount = 0.2f;
                break;

            case PrologueScript.SlideFx.Cut:
                _tint.Color = _tint.Color with { A = Mathf.Max(0f, 0.5f - t * 3.2f) };
                _glitch.Amount = 0.08f;
                break;

            case PrologueScript.SlideFx.Shake:
                offset += new Vector2(Mathf.Sin(t * 57f) * 7f, Mathf.Cos(t * 43f) * 5f);
                _glitch.Amount = 0.35f;
                break;

            case PrologueScript.SlideFx.Blackout:
                _fade.Color = _fade.Color with { A = Mathf.Min(1f, t / 1.1f) };
                _glitch.Amount = 0.05f;
                break;

            case PrologueScript.SlideFx.Typing:
                _glitch.Amount = 0.08f;
                break;

            // 머리를 세게 맞고 → 책상에 엎어지는 연출.
            case PrologueScript.SlideFx.Impact:
                // 0.0~0.45s : 강한 충격 흔들림 + 붉은 섬광
                float hit = Mathf.Max(0f, 1f - t / 0.45f);
                offset += new Vector2(Mathf.Sin(t * 88f) * 26f * hit, Mathf.Cos(t * 71f) * 18f * hit);
                _tint.Color = _tint.Color with { A = 0.45f * hit };
                _glitch.Amount = 0.3f + 0.7f * hit;
                // 0.45s~ : 상체가 책상으로 무너지듯 화면이 아래로 내려앉으며 어두워진다
                if (t > 0.45f)
                {
                    if (!_impactFallStarted)
                    {
                        _impactFallStarted = true;
                        Sfx.Instance?.Play("body_fall", -2f);
                        ControlRoom3DController.Instance?.ShakeCamera(3.2f, 0.35f);
                    }
                    float fall = Mathf.Clamp((t - 0.45f) / 0.85f, 0f, 1f);
                    offset.Y += fall * fall * 120f;
                    _fade.Color = _fade.Color with { A = fall };
                }
                break;

            default:
                _glitch.Amount = 0.05f;
                _tint.Color = _tint.Color with { A = 0f };
                break;
        }

        _glitch.QueueRedraw();
        _frame.Position = offset;
    }

    // CRT 를 직접 클릭해도 넘어간다(PrologueAdvanceInput 이 화면 아무 곳이나 받아 준다).
    public override void _GuiInput(InputEvent e)
    {
        if (_cutscene == null) return;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            RequestAdvance();
            AcceptEvent();
        }
    }

    // --- UI --------------------------------------------------------------

    private void BuildUi()
    {
        var bg = new ColorRect { Color = new Color(0.02f, 0.022f, 0.026f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 창(플레이어) 껍데기 — 제목 표시줄 / 재생바 / 상태 표시줄.
        _chrome = new PlayerChrome { MouseFilter = MouseFilterEnum.Ignore, Size = Canvas };
        AddChild(_chrome);

        _frame = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = Canvas, ClipContents = false };
        AddChild(_frame);

        _placeholder = new PlaceholderPanel
        {
            Position = VideoArea.Position + new Vector2(16f, 14f),
            Size = VideoArea.Size - new Vector2(32f, 28f),
            Kind = "IMAGE",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frame.AddChild(_placeholder);

        _image = new TextureRect
        {
            Position = VideoArea.Position,
            Size = VideoArea.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frame.AddChild(_image);

        // 인물 일러스트 — 배경 위, 자막 위쪽에 선다.
        _figurePlaceholder = new PlaceholderPanel
        {
            Position = FigureBox.Position,
            Size = FigureBox.Size,
            Kind = "FIGURE",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frame.AddChild(_figurePlaceholder);

        _figure = new TextureRect
        {
            Position = FigureBox.Position,
            Size = FigureBox.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frame.AddChild(_figure);

        _recDot = MakeLabel("● REC", 15, new Color(0.9f, 0.25f, 0.22f), VideoArea.Position + new Vector2(14f, 12f));
        _frame.AddChild(_recDot);

        _title = MakeLabel("", 16, Dim, VideoArea.Position + new Vector2(74f, 13f));
        _title.Size = new Vector2(VideoArea.Size.X - 90f, 22f);
        _frame.AddChild(_title);

        _overlay = MakeLabel("", 30, Amber, new Vector2(VideoArea.Position.X, 226f));
        _overlay.Size = new Vector2(VideoArea.Size.X, 60f);
        _overlay.HorizontalAlignment = HorizontalAlignment.Center;
        _overlay.VerticalAlignment = VerticalAlignment.Center;
        _overlay.AddThemeColorOverride("font_outline_color", Colors.Black);
        _overlay.AddThemeConstantOverride("outline_size", 6);
        _frame.AddChild(_overlay);

        _subtitleBox = new Panel
        {
            Position = SubtitleBox.Position,
            Size = SubtitleBox.Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _subtitleBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.62f),
            BorderColor = new Color(0.4f, 0.48f, 0.46f, 0.45f),
            BorderWidthTop = 1,
        });
        _frame.AddChild(_subtitleBox);

        _speaker = MakeLabel("", 14, Amber, new Vector2(16f, 8f));
        _subtitleBox.AddChild(_speaker);

        _text = MakeLabel("", 19, Ink, new Vector2(16f, 30f));
        _text.Size = new Vector2(SubtitleBox.Size.X - 32f, 58f);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _subtitleBox.AddChild(_text);

        _glitch = new GlitchBars { MouseFilter = MouseFilterEnum.Ignore, Size = Canvas, Area = VideoArea };
        AddChild(_glitch);

        _tint = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _tint.Position = VideoArea.Position;
        _tint.Size = VideoArea.Size;
        AddChild(_tint);

        _fade = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _fade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fade);

        _clickHint = MakeLabel("▶  SPACE / ENTER / 클릭", 14, new Color(0.62f, 0.72f, 0.72f),
            new Vector2(0f, VideoArea.Position.Y + VideoArea.Size.Y - 26f));
        _clickHint.Size = new Vector2(Canvas.X - 34f, 20f);
        _clickHint.HorizontalAlignment = HorizontalAlignment.Right;
        _clickHint.Visible = false;
        AddChild(_clickHint);
    }

    private Label MakeLabel(string text, int size, Color col, Vector2 pos)
    {
        var l = new Label { Text = text, Position = pos, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // --- 영상 재생 창 껍데기 -------------------------------------------------
    // "모니터 안에서 녹화 영상을 재생 중"이라는 걸 한눈에 알 수 있게 하는 UI.
    private partial class PlayerChrome : Control
    {
        public string FileName = "ARCHIVE.AVI";
        public float Progress;
        public double Elapsed;
        public double Total;

        public override void _Draw()
        {
            var font = ViewFont.Default;

            // 창 배경 + 테두리
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.07f, 0.075f, 0.09f));
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.30f, 0.34f, 0.40f), false, 1.5f);

            // 제목 표시줄
            DrawRect(TitleBar, Chrome);
            DrawString(font, new Vector2(12f, 21f), "▶  " + FileName + "  —  NSP MEDIA PLAYER",
                HorizontalAlignment.Left, 600f, 15, new Color(0.72f, 0.78f, 0.82f));
            DrawString(font, new Vector2(Size.X - 96f, 21f), "—   □   ✕",
                HorizontalAlignment.Left, 90f, 15, new Color(0.55f, 0.60f, 0.65f));

            // 영상 영역(검은 바탕) + 얇은 테두리
            DrawRect(VideoArea, Colors.Black);
            DrawRect(VideoArea, new Color(0.22f, 0.26f, 0.30f), false, 1f);

            // 재생 컨트롤 바
            DrawRect(ControlBar, Chrome);
            DrawString(font, ControlBar.Position + new Vector2(14f, 30f), "▶",
                HorizontalAlignment.Left, 24f, 18, new Color(0.80f, 0.86f, 0.90f));
            DrawString(font, ControlBar.Position + new Vector2(Size.X - 84f, 30f), "🔊",
                HorizontalAlignment.Left, 30f, 15, new Color(0.60f, 0.66f, 0.70f));

            var track = new Rect2(ControlBar.Position + new Vector2(44f, 20f), new Vector2(ControlBar.Size.X - 216f, 6f));
            DrawRect(track, new Color(0.20f, 0.23f, 0.27f));
            DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * Mathf.Clamp(Progress, 0f, 1f), track.Size.Y)),
                new Color(0.42f, 0.78f, 0.92f));
            // 재생 헤드
            DrawCircle(track.Position + new Vector2(track.Size.X * Mathf.Clamp(Progress, 0f, 1f), 3f), 4.5f,
                new Color(0.78f, 0.92f, 1f));

            DrawString(font, ControlBar.Position + new Vector2(ControlBar.Size.X - 168f, 26f),
                $"{Clock(Elapsed)} / {Clock(Total)}", HorizontalAlignment.Left, 120f, 14,
                new Color(0.70f, 0.76f, 0.80f));

            // 상태 표시줄
            DrawRect(StatusBar, new Color(0.085f, 0.095f, 0.115f));
            DrawString(font, StatusBar.Position + new Vector2(12f, 19f), "재생 중",
                HorizontalAlignment.Left, 200f, 13, new Color(0.55f, 0.72f, 0.60f));
            DrawString(font, StatusBar.Position + new Vector2(StatusBar.Size.X - 232f, 19f),
                "CODEC: NSP-ARCHIVE   720x480   4:3", HorizontalAlignment.Left, 230f, 12,
                new Color(0.45f, 0.50f, 0.55f));
        }

        private static string Clock(double seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    // --- 임시 이미지 자리 -------------------------------------------------
    private partial class PlaceholderPanel : Control
    {
        public string Note = "";
        public string Kind = "IMAGE";

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.07f, 0.08f, 0.09f, Kind == "FIGURE" ? 0.55f : 1f));
            DrawRect(box, new Color(0.34f, 0.40f, 0.42f, 0.7f), false, 1.5f);

            const float k = 18f;
            var c = new Color(0.5f, 0.58f, 0.6f, 0.85f);
            DrawLine(new Vector2(0, 0), new Vector2(k, 0), c, 2f);
            DrawLine(new Vector2(0, 0), new Vector2(0, k), c, 2f);
            DrawLine(new Vector2(Size.X, 0), new Vector2(Size.X - k, 0), c, 2f);
            DrawLine(new Vector2(Size.X, 0), new Vector2(Size.X, k), c, 2f);
            DrawLine(new Vector2(0, Size.Y), new Vector2(k, Size.Y), c, 2f);
            DrawLine(new Vector2(0, Size.Y), new Vector2(0, Size.Y - k), c, 2f);
            DrawLine(new Vector2(Size.X, Size.Y), new Vector2(Size.X - k, Size.Y), c, 2f);
            DrawLine(new Vector2(Size.X, Size.Y), new Vector2(Size.X, Size.Y - k), c, 2f);

            var font = ViewFont.Default;
            DrawString(font, new Vector2(0f, Size.Y * 0.40f), $"[ {Kind} ]",
                HorizontalAlignment.Center, Size.X, Kind == "FIGURE" ? 17 : 20,
                new Color(0.45f, 0.52f, 0.54f));
            if (!string.IsNullOrEmpty(Note))
                DrawMultilineString(font, new Vector2(14f, Size.Y * 0.40f + 30f), Note,
                    HorizontalAlignment.Center, Size.X - 28f, Kind == "FIGURE" ? 14 : 16, 4,
                    new Color(0.58f, 0.64f, 0.66f));
        }
    }

    // --- 노이즈 바(영상 영역 안에만) ----------------------------------------
    private partial class GlitchBars : Control
    {
        public float Amount;
        public Rect2 Area = new(0, 0, 800, 600);
        private readonly RandomNumberGenerator _rng = new();

        public override void _Draw()
        {
            if (Amount <= 0.01f) return;
            for (float y = Area.Position.Y; y < Area.Position.Y + Area.Size.Y; y += 3f)
                DrawRect(new Rect2(Area.Position.X, y, Area.Size.X, 1f), new Color(0f, 0f, 0f, 0.10f));

            int bands = Mathf.RoundToInt(Amount * 9f);
            for (int i = 0; i < bands; i++)
            {
                float y = _rng.RandfRange(Area.Position.Y, Area.Position.Y + Area.Size.Y);
                float h = _rng.RandfRange(2f, 16f);
                float a = _rng.RandfRange(0.05f, 0.22f) * Amount;
                DrawRect(new Rect2(Area.Position.X, y, Area.Size.X, h), new Color(0.75f, 0.85f, 0.95f, a));
            }
        }
    }
}
