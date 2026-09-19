using System;
using System.Collections.Generic;
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
// 정지 이미지가 슬라이드쇼처럼 보이지 않도록, 영상 영역에는 항상 다음이 걸려 있다.
//   - 느린 줌/팬(켄 번스)  ken: off 로 끌 수 있다
//   - CRT 주사선 + 미세 필름 노이즈
// 컷이 바뀌는 순간의 연출(fx: cut)은 매번 무작위로 하나만 고른다 — 다 넣으면 난잡해진다.
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
    // 영상 안 글씨는 흰색으로 통일한다(경보만 빨강).
    private static readonly Color Amber = new(0.94f, 0.96f, 0.96f);
    private static readonly Color AlertRed = new(1f, 0.42f, 0.32f);
    private static readonly Color Chrome = new(0.105f, 0.115f, 0.135f);

    // --- 영상 창 레이아웃 ---------------------------------------------------
    private static readonly Rect2 TitleBar = new(0f, 0f, 800f, 30f);
    private static readonly Rect2 VideoArea = new(10f, 36f, 780f, 448f);
    private static readonly Rect2 ControlBar = new(10f, 490f, 780f, 46f);
    private static readonly Rect2 StatusBar = new(10f, 542f, 780f, 28f);
    private static readonly Rect2 FigureBox = new(462f, 80f, 300f, 360f);
    private static readonly Rect2 SubtitleBox = new(26f, 382f, 748f, 96f);
    // 무전 수신 상태 HUD — 자막 띠 바로 위 왼쪽.
    private static readonly Rect2 RadioBox = new(26f, 286f, 316f, 88f);
    // 코어 출력 게이지 — 영상 한가운데 아래쪽.
    private static readonly Rect2 GaugeBox = new(70f, 232f, 660f, 168f);
    // 비상 경보창 — 실제 시설 경보 패널처럼 영상 한가운데 크게 뜬다.
    private static readonly Rect2 AlertBox = new(118f, 78f, 564f, 362f);

    private Font _font;
    private Control _frame;          // 흔들림이 걸리는 영상 내용물
    private Control _videoClip;      // 줌/팬이 영상 영역 밖으로 새지 않게 가두는 틀
    private TextureRect _image;
    private PlaceholderPanel _placeholder;
    private TextureRect _figure;
    private PlaceholderPanel _figurePlaceholder;
    private Label _title;
    private Label _recDot;
    private Label _overlay;
    private Label _sub;
    private Panel _subtitleBox;
    private Label _speaker;
    private Label _text;
    private Label _clickHint;
    private ColorRect _tint;
    private ColorRect _fade;
    private GlitchBars _glitch;
    private PlayerChrome _chrome;
    private RadioHud _radioHud;
    private GaugePanel _gauge;
    private AlertBoard _alertBoard;
    private WarpOverlay _warp;

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
    // 자막이 한 글자씩 드러날 때마다 화자 보이스를 울리기 위한 진행도.
    private int _spokenChars;
    private bool _radioOpen;
    // sfxafter / cutoff 는 타이핑이 끝나는 순간 한 번만 터진다.
    private bool _afterFired;

    // 재생바용 — 컷씬 전체 길이와 지금까지 흐른 시간(진짜 영상처럼 보이게).
    private double _totalSeconds;
    private double _elapsedBeforeSlide;

    // --- 컷 전환 연출(무작위) ------------------------------------------------
    private enum CutStyle { Punch, Shake, Flash, Pan, Glitch }
    private CutStyle _cutStyle;
    private CutStyle _lastCutStyle = CutStyle.Glitch;
    private readonly RandomNumberGenerator _rng = new();

    // --- 켄 번스(느린 줌/팬) -------------------------------------------------
    private float _kenFrom = 1.03f, _kenTo = 1.09f;
    private Vector2 _panFrom, _panTo;
    private double _kenSeconds = 3.0;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        _rng.Randomize();
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
        _glitch.Active = true;

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
        _sub.Text = "";
        _subtitleBox.Visible = false;
        _clickHint.Visible = false;
        _radioHud.Visible = false;
        _gauge.Visible = false;
        _alertBoard.Visible = false;
        _warp.Visible = false;
        _warp.Amount = 0f;
        _tint.Color = _tint.Color with { A = 0f };
        _fade.Color = _fade.Color with { A = 0f };
        _glitch.Amount = 0f;
        _glitch.Active = false;
        _glitch.QueueRedraw();
        _frame.Position = Vector2.Zero;
        _videoClip.Scale = Vector2.One;
        _videoClip.Modulate = Colors.White;
        _image.Scale = Vector2.One;
        _image.Position = Vector2.Zero;
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
        _spokenChars = _text.Text.Length;
        Sfx.Instance?.StopVoiceBlip();
        FireAfterCues();
        CloseRadio();
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
            _glitch.Active = false;
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
        _afterFired = false;
        _joltUntil = s.Jolt > 0f ? s.Jolt : 0.0;
        if (_fx == PrologueScript.SlideFx.Cut) _cutStyle = PickCutStyle();

        // 배경 이미지: 최종 파일이 있으면 그걸, 없으면 같은 자리에 임시 패널.
        // 일그러짐 연출 중에는 원본 대신 WarpOverlay 가 같은 그림을 조각내어 그린다.
        bool warping = _fx is PrologueScript.SlideFx.Warp or PrologueScript.SlideFx.WarpHold;
        var tex = LoadImage(s.ImagePath);
        _image.Texture = tex;
        _image.Visible = tex != null && !warping;
        _warp.Texture = tex;
        _warp.Visible = warping && tex != null;
        _warp.Amount = _fx == PrologueScript.SlideFx.WarpHold ? 0.9f : 0f;
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

        // 경고 문구도 자막과 같은 속도로 타이핑된다. 경보 카드는 붉게 띄운다.
        _overlay.Text = s.Overlay ?? "";
        _overlay.VisibleRatio = 0f;
        _overlay.AddThemeColorOverride("font_color",
            _fx == PrologueScript.SlideFx.Alert ? AlertRed : Amber);
        _sub.Text = s.Sub ?? "";
        _sub.Visible = !string.IsNullOrEmpty(s.Sub);

        bool hasText = !string.IsNullOrEmpty(s.Text);
        _subtitleBox.Visible = hasText;
        _speaker.Text = s.Speaker ?? "";
        _speaker.Visible = !string.IsNullOrEmpty(s.Speaker);
        _text.Text = s.Text ?? "";
        _text.VisibleRatio = hasText ? 0f : 1f;

        _typeSeconds = Math.Max(
            hasText ? PrologueTextStyle.TypeSeconds(s.Text) : 0f,
            _overlay.Text.Length > 0 ? PrologueTextStyle.TypeSeconds(s.Overlay) : 0f);

        SetupKenBurns(s);
        SetupRadioHud(s);
        SetupGauge(s);
        SetupAlertBoard(s);

        // 화자 보이스 — 기존 직원 보이스 파일을 그대로 쓰고, 무전이면 Radio 버스로 흘린다.
        _spokenChars = 0;
        Sfx.Instance?.StopVoiceBlip();
        CloseRadio();
        if (hasText && s.Radio && !string.IsNullOrEmpty(s.VoiceId)) OpenRadio();

        // 대사가 있는 슬라이드는 절대 저절로 넘어가지 않는다(hold: 값은 무시된다).
        // 대사가 없는 컷(몽타주·경고 문구)은 타이핑이 끝난 뒤 hold: 만큼 더 보여주고 넘어가되,
        // hold: 0 으로 적으면 그 컷도 입력을 기다린다.
        IsWaitingForInput = hasText || s.Hold <= 0f;
        _clickHint.Visible = false;

        _tint.Color = TintFor(_fx) with { A = 0f };
        _videoClip.Scale = Vector2.One;
        _videoClip.Modulate = Colors.White;
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

    // 컷마다 하나만 — 같은 연출이 두 번 연속 나오지 않게 한 번 다시 뽑는다.
    private CutStyle PickCutStyle()
    {
        var pick = (CutStyle)_rng.RandiRange(0, 4);
        if (pick == _lastCutStyle) pick = (CutStyle)(((int)pick + 1 + _rng.RandiRange(0, 3)) % 5);
        _lastCutStyle = pick;
        return pick;
    }

    // 정지 이미지를 아주 느리게 확대하며 좌우로 흘린다 — '슬라이드'가 아니라 '영상'처럼 보이게.
    private void SetupKenBurns(PrologueScript.Slide s)
    {
        _kenSeconds = Math.Max(1.2, NominalSeconds(s) + 1.2);
        if (!s.KenBurns || _image.Texture == null)
        {
            _kenFrom = _kenTo = 1f;
            _panFrom = _panTo = Vector2.Zero;
            ApplyKenBurns(0f);
            return;
        }
        bool zoomIn = _rng.Randf() > 0.25f;   // 대체로 천천히 밀고 들어간다
        _kenFrom = zoomIn ? 1.02f : 1.09f;
        _kenTo = zoomIn ? 1.09f : 1.02f;
        float drift = _rng.RandfRange(8f, 20f) * (_rng.Randf() > 0.5f ? 1f : -1f);
        _panFrom = new Vector2(-drift * 0.5f, _rng.RandfRange(-4f, 4f));
        _panTo = new Vector2(drift * 0.5f, _rng.RandfRange(-4f, 4f));
        ApplyKenBurns(0f);
    }

    private void ApplyKenBurns(float t)
    {
        float k = Mathf.Lerp(_kenFrom, _kenTo, t);
        _image.Scale = new Vector2(k, k);
        _image.Position = _panFrom.Lerp(_panTo, t);
    }

    private void SetupRadioHud(PrologueScript.Slide s)
    {
        bool on = s.Radio && s.Signal > 0;
        _radioHud.Visible = on;
        if (!on) return;
        _radioHud.Signal = s.Signal;
        _radioHud.CallSign = string.IsNullOrEmpty(s.VoiceId) ? "UNKNOWN" : s.VoiceId.ToUpperInvariant();
        _radioHud.Cut = false;
    }

    // 비상 경보창 — 제목/부제/항목들/맨 아랫줄. 항목은 한 줄씩 차례로 뜬다.
    private void SetupAlertBoard(PrologueScript.Slide s)
    {
        bool on = !string.IsNullOrEmpty(s.AlertTitle);
        _alertBoard.Visible = on;
        if (!on) return;
        _alertBoard.Title = s.AlertTitle;
        _alertBoard.Sub = s.AlertSub;
        _alertBoard.Foot = s.AlertFoot;
        _alertBoard.Rows = s.AlertRows;
        _alertBoard.Reset();
    }

    private void SetupGauge(PrologueScript.Slide s)
    {
        bool on = s.GaugeSteps.Count > 0;
        _gauge.Visible = on;
        if (!on) return;
        _gauge.Title = s.GaugeTitle;
        _gauge.SubText = s.GaugeSub;
        _gauge.AlertText = s.GaugeAlert;
        _gauge.Steps = s.GaugeSteps;
        _gauge.Duration = Math.Max(1.2, s.Hold);
        _gauge.Reset();
    }

    private string _activeLoop = "";

    // 무전 개시/종료 "치직" + 통신 중 약한 잡음. 잡음은 아주 낮게 깔아 보이스를 덮지 않는다.
    private void OpenRadio()
    {
        _radioOpen = true;
        Sfx.Instance?.Play("radio_click_on", -8f);
        Sfx.Instance?.Loop("radio_static", -22f);
    }

    private void CloseRadio()
    {
        if (!_radioOpen) return;
        _radioOpen = false;
        Sfx.Instance?.StopLoop("radio_static");
        Sfx.Instance?.Play("radio_click_off", -10f);
    }

    // 자막이 다 찍힌 직후에 한 번만 — 스위치 조작음, 통신 두절 치직.
    private void FireAfterCues()
    {
        if (_afterFired || _slide == null) return;
        _afterFired = true;
        if (!string.IsNullOrEmpty(_slide.SfxAfter)) Sfx.Instance?.Play(_slide.SfxAfter, -4f);
        if (_slide.Cutoff)
        {
            Sfx.Instance?.Play("radio_cut", -3f);
            _glitch.Amount = 1f;
            if (_radioHud.Visible) { _radioHud.Cut = true; }
        }
    }

    private void StopAllLoops()
    {
        CloseRadio();
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
        PrologueScript.SlideFx.Alert => new Color(0.92f, 0.10f, 0.08f),
        PrologueScript.SlideFx.Crt => new Color(1f, 1f, 1f),
        _ => new Color(0f, 0f, 0f),
    };

    public override void _Process(double delta)
    {
        if (_cutscene == null) return;

        _slideElapsed += delta;
        _fxTime += delta;

        // 자막/경고 타이핑 — GUIDE-0 와 완전히 같은 속도(PrologueTextStyle).
        if (_subtitleBox.Visible && _text.VisibleRatio < 1f)
        {
            _text.VisibleRatio = PrologueTextStyle.Ratio(_text.Text, _slideElapsed);
            SpeakRevealed();
        }
        else if (_radioOpen && _text.VisibleRatio >= 1f)
        {
            CloseRadio();   // 문장이 다 떴으면 통신 종료 치직
        }
        if (_overlay.Text.Length > 0 && _overlay.VisibleRatio < 1f)
            _overlay.VisibleRatio = PrologueTextStyle.Ratio(_overlay.Text, _slideElapsed);

        if (!IsTyping()) FireAfterCues();

        ApplyKenBurns((float)Mathf.Clamp(_slideElapsed / _kenSeconds, 0.0, 1.0));
        if (_radioHud.Visible) _radioHud.Tick((float)delta);
        if (_gauge.Visible) _gauge.Tick(_slideElapsed);
        if (_alertBoard.Visible) _alertBoard.Tick(_slideElapsed);

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

    // 새로 드러난 글자만큼 화자 보이스를 울린다(기존 직원 보이스 시스템을 그대로 호출).
    private void SpeakRevealed()
    {
        string voice = _slide?.VoiceId ?? "";
        if (string.IsNullOrEmpty(voice)) return;
        int shown = Mathf.RoundToInt(_text.VisibleRatio * _text.Text.Length);
        while (_spokenChars < shown)
        {
            char c = _text.Text[_spokenChars];
            _spokenChars++;
            Sfx.Instance?.PlayVoiceBlip(voice, c, _slide.Radio);
        }
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

        // 무전 신호가 약할수록 화면 노이즈가 늘어난다.
        float radioNoise = _radioHud.Visible ? (1f - _radioHud.Signal / 100f) * 0.35f : 0f;

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

            // 컷이 바뀔 때마다 무작위로 하나 — 전부 다 걸면 난잡해진다.
            case PrologueScript.SlideFx.Cut:
                ApplyCutStyle(t, ref offset);
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

            // 긴급 경보 — 화면 전체가 붉게 점멸한다.
            case PrologueScript.SlideFx.Alert:
                _tint.Color = _tint.Color with { A = 0.10f + 0.24f * Mathf.Max(0f, Mathf.Sin(t * 8.4f)) };
                _glitch.Amount = 0.12f;
                offset.X += Mathf.Sin(t * 33f) * 1.6f;
                break;

            // 조명이 나가려는 듯 밝기가 튄다.
            case PrologueScript.SlideFx.Flicker:
            {
                float f = Mathf.Sin(t * 21f) * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 43f);
                float lum = Mathf.Clamp(0.86f + f * 0.55f, 0.30f, 1.25f);
                _videoClip.Modulate = new Color(lum, lum * 0.98f, lum * 0.96f);
                _glitch.Amount = 0.18f;
                break;
            }

            // 브라운관이 켜지는 순간 — 가로선이 위아래로 펼쳐지며 노이즈가 걷힌다.
            case PrologueScript.SlideFx.Crt:
            {
                float open = Mathf.Clamp(t / 0.30f, 0.02f, 1f);
                _videoClip.Scale = new Vector2(1f, Mathf.Min(1f, open * open));
                _tint.Color = _tint.Color with { A = Mathf.Max(0f, 0.75f - t * 3.4f) };
                _glitch.Amount = Mathf.Max(0.08f, 1f - t * 1.4f);
                break;
            }

            // 직전 컷이 그 자리에서 기괴하게 일그러진다(새 그림을 띄우지 않는다).
            case PrologueScript.SlideFx.Warp:
            {
                float a = Mathf.Clamp(t / 1.5f, 0f, 1f);
                _warp.Amount = a;
                _warp.Phase = t * 9f;
                _warp.QueueRedraw();
                _glitch.Amount = 0.3f + 0.7f * a;
                _tint.Color = new Color(0.85f, 0.10f, 0.08f,
                    0.06f + 0.26f * a * (0.5f + 0.5f * Mathf.Sin(t * 19f)));
                offset += new Vector2(Mathf.Sin(t * 61f), Mathf.Cos(t * 47f)) * 9f * a;
                // 끝으로 갈수록 어두워진다 — 그대로 신호 두절 컷으로 이어진다.
                _fade.Color = _fade.Color with { A = Mathf.Max(0f, (a - 0.70f) / 0.30f) * 0.55f };
                break;
            }

            // 일그러진 그 상태로 멈춘다(신호 두절 문구가 올라앉는 화면).
            case PrologueScript.SlideFx.WarpHold:
            {
                _warp.Amount = 0.85f + 0.15f * Mathf.Sin(t * 13f);
                _warp.Phase = t * 9f;
                _warp.QueueRedraw();
                _glitch.Amount = 1f;
                _tint.Color = new Color(0.85f, 0.10f, 0.08f, 0.12f);
                _fade.Color = _fade.Color with { A = 0.55f };
                offset.X += Mathf.Sin(t * 73f) * 5f;
                break;
            }

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
                        // 시점 자체가 책상으로 빠르게 고꾸라진다(고개가 떨어지듯).
                        ControlRoom3DController.Instance?.CollapseCameraOntoDesk(0.34f);
                    }
                    float fall = Mathf.Clamp((t - 0.45f) / 0.55f, 0f, 1f);
                    offset.Y += fall * fall * 120f;
                    _fade.Color = _fade.Color with { A = fall };
                }
                break;

            default:
                _glitch.Amount = 0.05f;
                _tint.Color = _tint.Color with { A = 0f };
                break;
        }

        _glitch.Amount = Mathf.Max(_glitch.Amount, radioNoise);
        _glitch.QueueRedraw();
        _frame.Position = offset;
    }

    private void ApplyCutStyle(float t, ref Vector2 offset)
    {
        switch (_cutStyle)
        {
            case CutStyle.Punch:
                // 확 밀고 들어왔다가 제자리로.
                _videoClip.Scale = Vector2.One * Mathf.Lerp(1.14f, 1f, Mathf.Clamp(t / 0.34f, 0f, 1f));
                _glitch.Amount = 0.08f;
                _tint.Color = _tint.Color with { A = Mathf.Max(0f, 0.35f - t * 3.5f) };
                break;

            case CutStyle.Shake:
                offset += new Vector2(Mathf.Sin(t * 64f), Mathf.Cos(t * 51f) * 0.7f) * 9f * Mathf.Max(0f, 1f - t * 2.6f);
                _glitch.Amount = 0.10f;
                break;

            case CutStyle.Flash:
                _tint.Color = new Color(0.95f, 0.16f, 0.12f, Mathf.Max(0f, 0.55f - t * 3.0f));
                _glitch.Amount = 0.10f;
                break;

            case CutStyle.Pan:
                // 켄 번스보다 훨씬 빠른 횡이동.
                offset.X += Mathf.Lerp(26f, 0f, Mathf.Clamp(t / 0.55f, 0f, 1f));
                _glitch.Amount = 0.08f;
                _tint.Color = _tint.Color with { A = Mathf.Max(0f, 0.28f - t * 3.2f) };
                break;

            default:
                _glitch.Amount = Mathf.Max(0.12f, 0.9f - t * 3.2f);
                offset.X += Mathf.Sin(t * 70f) * 4f * Mathf.Max(0f, 1f - t * 3f);
                _tint.Color = new Color(0.75f, 0.85f, 1f, Mathf.Max(0f, 0.3f - t * 2.6f));
                break;
        }
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

        // 줌/팬이 영상 영역 밖으로 새지 않게 가두는 틀.
        _videoClip = new Control
        {
            Position = VideoArea.Position,
            Size = VideoArea.Size,
            PivotOffset = VideoArea.Size * 0.5f,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _frame.AddChild(_videoClip);

        _placeholder = new PlaceholderPanel
        {
            Position = new Vector2(16f, 14f),
            Size = VideoArea.Size - new Vector2(32f, 28f),
            Kind = "IMAGE",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _videoClip.AddChild(_placeholder);

        _image = new TextureRect
        {
            Position = Vector2.Zero,
            Size = VideoArea.Size,
            PivotOffset = VideoArea.Size * 0.5f,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _videoClip.AddChild(_image);

        // 같은 그림을 가로로 잘라 어긋나게 그리는 일그러짐 레이어.
        _warp = new WarpOverlay
        {
            Position = Vector2.Zero,
            Size = VideoArea.Size,
            Area = new Rect2(Vector2.Zero, VideoArea.Size),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _videoClip.AddChild(_warp);

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

        _title = MakeLabel("", 16, Dim, VideoArea.Position + new Vector2(100f, 13f));
        _title.Size = new Vector2(VideoArea.Size.X - 116f, 22f);
        _frame.AddChild(_title);

        // 한가운데 크게 뜨는 한글 주 문구 — 여러 줄(\n)을 받는다.
        _overlay = MakeLabel("", 34, Amber, new Vector2(VideoArea.Position.X, 140f));
        _overlay.Size = new Vector2(VideoArea.Size.X, 196f);
        _overlay.HorizontalAlignment = HorizontalAlignment.Center;
        _overlay.VerticalAlignment = VerticalAlignment.Center;
        _overlay.AddThemeColorOverride("font_outline_color", Colors.Black);
        _overlay.AddThemeConstantOverride("outline_size", 7);
        _overlay.AddThemeConstantOverride("line_spacing", 8);
        _frame.AddChild(_overlay);

        // 그 아래 작게 깔리는 영문 보조 문구.
        _sub = MakeLabel("", 15, new Color(0.72f, 0.76f, 0.78f), new Vector2(VideoArea.Position.X, 342f));
        _sub.Size = new Vector2(VideoArea.Size.X, 24f);
        _sub.HorizontalAlignment = HorizontalAlignment.Center;
        _sub.AddThemeColorOverride("font_outline_color", Colors.Black);
        _sub.AddThemeConstantOverride("outline_size", 5);
        _frame.AddChild(_sub);

        _gauge = new GaugePanel
        {
            Position = GaugeBox.Position,
            Size = GaugeBox.Size,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _frame.AddChild(_gauge);

        _alertBoard = new AlertBoard
        {
            Position = AlertBox.Position,
            Size = AlertBox.Size,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _frame.AddChild(_alertBoard);

        _radioHud = new RadioHud
        {
            Position = RadioBox.Position,
            Size = RadioBox.Size,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _frame.AddChild(_radioHud);

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

        _speaker = MakeLabel("", 13, Amber, new Vector2(16f, 6f));
        _subtitleBox.AddChild(_speaker);

        _text = MakeLabel("", 18, Ink, new Vector2(16f, 28f));
        _text.Size = new Vector2(SubtitleBox.Size.X - 32f, 60f);
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

        _clickHint = MakeLabel("▶  SPACE / ENTER / 클릭", 13, new Color(0.62f, 0.72f, 0.72f),
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
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
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
                HorizontalAlignment.Left, 600f, ViewFont.S(15), new Color(0.72f, 0.78f, 0.82f));
            DrawString(font, new Vector2(Size.X - 96f, 21f), "—   □   ✕",
                HorizontalAlignment.Left, 90f, ViewFont.S(15), new Color(0.55f, 0.60f, 0.65f));

            // 영상 영역(검은 바탕) + 얇은 테두리
            DrawRect(VideoArea, Colors.Black);
            DrawRect(VideoArea, new Color(0.22f, 0.26f, 0.30f), false, 1f);

            // 재생 컨트롤 바
            DrawRect(ControlBar, Chrome);
            DrawString(font, ControlBar.Position + new Vector2(14f, 30f), "▶",
                HorizontalAlignment.Left, 24f, ViewFont.S(18), new Color(0.80f, 0.86f, 0.90f));
            DrawString(font, ControlBar.Position + new Vector2(Size.X - 84f, 30f), "🔊",
                HorizontalAlignment.Left, 30f, ViewFont.S(15), new Color(0.60f, 0.66f, 0.70f));

            var track = new Rect2(ControlBar.Position + new Vector2(44f, 20f), new Vector2(ControlBar.Size.X - 216f, 6f));
            DrawRect(track, new Color(0.20f, 0.23f, 0.27f));
            DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * Mathf.Clamp(Progress, 0f, 1f), track.Size.Y)),
                new Color(0.42f, 0.78f, 0.92f));
            // 재생 헤드
            DrawCircle(track.Position + new Vector2(track.Size.X * Mathf.Clamp(Progress, 0f, 1f), 3f), 4.5f,
                new Color(0.78f, 0.92f, 1f));

            DrawString(font, ControlBar.Position + new Vector2(ControlBar.Size.X - 168f, 26f),
                $"{Clock(Elapsed)} / {Clock(Total)}", HorizontalAlignment.Left, 120f, ViewFont.S(14),
                new Color(0.70f, 0.76f, 0.80f));

            // 상태 표시줄
            DrawRect(StatusBar, new Color(0.085f, 0.095f, 0.115f));
            DrawString(font, StatusBar.Position + new Vector2(12f, 19f), "재생 중",
                HorizontalAlignment.Left, 200f, ViewFont.S(13), new Color(0.55f, 0.72f, 0.60f));
            DrawString(font, StatusBar.Position + new Vector2(StatusBar.Size.X - 238f, 19f),
                "CODEC: NSP-ARCHIVE   720x480   4:3", HorizontalAlignment.Left, 236f, ViewFont.S(12),
                new Color(0.45f, 0.50f, 0.55f));
        }

        private static string Clock(double seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt((float)seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    // --- 무전 수신 상태 HUD -------------------------------------------------
    // 누가 · 신호가 얼마나 남았는지 · 파형이 얼마나 흔들리는지를 한눈에 보여준다.
    private partial class RadioHud : Control
    {
        public int Signal = 100;
        public string CallSign = "";
        public bool Cut;

        private float _t;
        private readonly float[] _wave = new float[46];
        private readonly RandomNumberGenerator _rng = new();

        public void Tick(float delta)
        {
            _t += delta;
            // 파형을 왼쪽으로 흘리고 오른쪽 끝에 새 값을 밀어 넣는다.
            for (int i = 0; i < _wave.Length - 1; i++) _wave[i] = _wave[i + 1];
            float agitation = Cut ? 0.05f : Mathf.Lerp(1.0f, 0.25f, Signal / 100f);
            float carrier = Mathf.Sin(_t * 17f) * 0.35f + Mathf.Sin(_t * 41f) * 0.2f;
            _wave[^1] = Cut ? _rng.RandfRange(-0.05f, 0.05f)
                            : Mathf.Clamp(carrier + _rng.RandfRange(-agitation, agitation), -1f, 1f);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = ViewFont.Default;
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.02f, 0.05f, 0.05f, 0.72f));
            DrawRect(box, new Color(0.35f, 0.75f, 0.70f, 0.55f), false, 1.2f);

            var head = Cut ? new Color(0.85f, 0.30f, 0.25f) : new Color(0.55f, 0.95f, 0.85f);
            DrawString(font, new Vector2(12f, 20f), Cut ? "SIGNAL LOST" : "INCOMING RADIO",
                HorizontalAlignment.Left, Size.X - 24f, ViewFont.S(12), head with { A = 0.9f });
            DrawString(font, new Vector2(12f, 42f), $"{CallSign}  //  SIGNAL {Signal}%",
                HorizontalAlignment.Left, Size.X - 24f, ViewFont.S(17), head);

            // 파형.
            var area = new Rect2(12f, 52f, Size.X - 24f, 28f);
            DrawRect(area, new Color(0f, 0f, 0f, 0.35f));
            float step = area.Size.X / (_wave.Length - 1);
            var col = head with { A = 0.85f };
            for (int i = 0; i < _wave.Length - 1; i++)
            {
                var a = new Vector2(area.Position.X + i * step, area.Position.Y + area.Size.Y * 0.5f - _wave[i] * area.Size.Y * 0.45f);
                var b = new Vector2(area.Position.X + (i + 1) * step, area.Position.Y + area.Size.Y * 0.5f - _wave[i + 1] * area.Size.Y * 0.45f);
                DrawLine(a, b, col, 1.4f);
            }
        }
    }

    // --- 코어 출력 게이지 ---------------------------------------------------
    // 영어를 못 읽어도 "100% 였던 게 3% 가 됐다"가 바로 보여야 한다 — 숫자와 막대가
    // 실제로 내려가고, 주 정보는 전부 한글이다.
    private partial class GaugePanel : Control
    {
        public string Title = "";
        public string SubText = "";
        public string AlertText = "";
        public IReadOnlyList<float> Steps = Array.Empty<float>();
        public double Duration = 3.0;

        private const int Cells = 20;
        private const float Lead = 0.45f;   // 첫 값을 잠깐 보여주고 나서 내려가기 시작한다

        private float _value;
        private int _stepIndex = -1;
        private bool _alertFired;
        private double _alertAt = -1;

        public void Reset()
        {
            _value = Steps.Count > 0 ? Steps[0] : 0f;
            _stepIndex = -1;
            _alertFired = false;
            _alertAt = -1;
            QueueRedraw();
        }

        public void Tick(double elapsed)
        {
            if (Steps.Count == 0) return;
            int n = Steps.Count;
            float per = (float)Math.Max(0.35, (Duration - Lead) / Math.Max(1, n - 1));

            int idx;
            if (elapsed < Lead)
            {
                _value = Steps[0];
                idx = 0;
            }
            else
            {
                float k = (float)(elapsed - Lead) / per;
                int i = Mathf.Clamp((int)k, 0, n - 1);
                if (i >= n - 1)
                {
                    _value = Steps[n - 1];
                    idx = n - 1;
                }
                else
                {
                    // 앞부분은 빠르게 떨어지고 끝에서 살짝 붙잡힌다.
                    float f = Mathf.Clamp(k - i, 0f, 1f);
                    _value = Mathf.Lerp(Steps[i], Steps[i + 1], 1f - (1f - f) * (1f - f));
                    idx = i;
                }
            }

            if (idx != _stepIndex)
            {
                _stepIndex = idx;
                if (idx > 0) Sfx.Instance?.Play("gauge_tick", -8f);
            }
            if (!_alertFired && !string.IsNullOrEmpty(AlertText) && idx >= n - 1)
            {
                _alertFired = true;
                _alertAt = elapsed;
                Sfx.Instance?.Play("alarm", -4f);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = ViewFont.Default;
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.02f, 0.03f, 0.04f, 0.70f));
            DrawRect(box, new Color(0.62f, 0.66f, 0.68f, 0.5f), false, 1.4f);

            float pct = Mathf.Clamp(_value, 0f, 100f);
            // 40% 아래부터 주황 → 빨강.
            var col = pct > 60f ? new Color(0.55f, 0.92f, 0.70f)
                : pct > 30f ? new Color(0.98f, 0.72f, 0.25f)
                : new Color(1f, 0.32f, 0.24f);

            DrawString(font, new Vector2(20f, 34f), Title, HorizontalAlignment.Left, Size.X - 40f,
                ViewFont.S(22), new Color(0.92f, 0.90f, 0.82f));

            // 칸 막대 — ████░░░░ 를 실제 사각형으로 그린다.
            const float barX = 20f, barY = 52f, barH = 30f;
            float barW = Size.X - 40f - 118f;
            float cellW = barW / Cells;
            int filled = Mathf.RoundToInt(pct / 100f * Cells);
            for (int i = 0; i < Cells; i++)
            {
                var r = new Rect2(barX + i * cellW + 1.5f, barY, cellW - 3f, barH);
                DrawRect(r, i < filled ? col : new Color(col.R, col.G, col.B, 0.13f));
            }
            DrawRect(new Rect2(barX, barY, barW, barH), new Color(0.55f, 0.55f, 0.50f, 0.45f), false, 1.2f);

            // 숫자 — 게이지에서 제일 크게 읽혀야 한다.
            DrawString(font, new Vector2(barX + barW + 14f, barY + barH - 3f), $"{Mathf.RoundToInt(pct)}%",
                HorizontalAlignment.Right, 104f, ViewFont.S(34), col);

            if (!string.IsNullOrEmpty(SubText))
                DrawString(font, new Vector2(20f, barY + barH + 26f), SubText, HorizontalAlignment.Left,
                    Size.X - 40f, ViewFont.S(13), new Color(0.62f, 0.66f, 0.68f));

            if (_alertFired && !string.IsNullOrEmpty(AlertText))
            {
                // 경고는 깜빡인다 — 그래야 마지막 값이 '사고'로 읽힌다.
                float blink = 0.55f + 0.45f * Mathf.Sin((float)(_alertAt >= 0 ? Time.GetTicksMsec() / 1000.0 : 0) * 12f);
                DrawString(font, new Vector2(20f, Size.Y - 14f), AlertText, HorizontalAlignment.Left,
                    Size.X - 40f, ViewFont.S(25), new Color(1f, 0.30f, 0.24f, blink));
            }
        }
    }

    // --- 비상 경보창 ---------------------------------------------------------
    // "경고 문구 한 줄"이 아니라 실제 시설 경보 패널처럼 보이게 한다.
    // 제목(한글) · 부제(영문) · 항목 목록 · 맨 아랫줄 지시. 항목은 한 줄씩 차례로 켜진다.
    private partial class AlertBoard : Control
    {
        public string Title = "";
        public string Sub = "";
        public string Foot = "";
        public IReadOnlyList<(string Label, string Value)> Rows = Array.Empty<(string, string)>();

        private const float Lead = 0.30f, RowGap = 0.22f;
        private static readonly Color Deep = new(0.14f, 0.015f, 0.015f, 0.93f);
        private static readonly Color Line = new(1f, 0.22f, 0.18f);
        private static readonly Color Soft = new(1f, 0.46f, 0.40f);

        private int _shown;
        private bool _footShown;
        private float _t;

        public void Reset()
        {
            _shown = 0;
            _footShown = false;
            _t = 0f;
            QueueRedraw();
        }

        public void Tick(double elapsed)
        {
            _t = (float)elapsed;
            int want = elapsed < Lead ? 0 : Mathf.Min(Rows.Count, (int)((elapsed - Lead) / RowGap) + 1);
            if (want != _shown)
            {
                _shown = want;
                Sfx.Instance?.Play("tick", -14f);
            }
            if (!_footShown && Rows.Count > 0 && _shown >= Rows.Count
                && elapsed > Lead + Rows.Count * RowGap + 0.15)
            {
                _footShown = true;
                Sfx.Instance?.Play("sensor_beep", -10f);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = ViewFont.Default;
            var box = new Rect2(Vector2.Zero, Size);
            float pulse = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_t * 7.5f));

            // 패널 — 짙은 적색 바탕 + 두꺼운 경보 테두리.
            DrawRect(box.Grow(6f), new Color(0.35f, 0.03f, 0.03f, 0.35f * pulse));
            DrawRect(box, Deep);
            DrawRect(box, Line with { A = 0.95f * pulse }, false, 3f);
            DrawRect(box.Grow(-6f), Line with { A = 0.35f }, false, 1f);
            for (float y = 0; y < Size.Y; y += 3f)
                DrawRect(new Rect2(0, y, Size.X, 1f), new Color(0f, 0f, 0f, 0.16f));

            // 경고 삼각형.
            var c = new Vector2(Size.X * 0.5f, 60f);
            var pts = new[]
            {
                c + new Vector2(0f, -30f), c + new Vector2(30f, 22f), c + new Vector2(-30f, 22f),
            };
            DrawColoredPolygon(pts, Line with { A = 0.92f * pulse });
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, new Color(1f, 0.75f, 0.70f, 0.9f), 2.2f);
            DrawString(font, new Vector2(c.X - 12f, c.Y + 16f), "!", HorizontalAlignment.Center, 24f,
                ViewFont.S(24), Deep);

            // 제목 / 부제.
            DrawString(font, new Vector2(0f, 128f), Title, HorizontalAlignment.Center, Size.X,
                ViewFont.S(36), new Color(1f, 0.30f, 0.24f));
            if (!string.IsNullOrEmpty(Sub))
                DrawString(font, new Vector2(0f, 156f), Sub, HorizontalAlignment.Center, Size.X,
                    ViewFont.S(17), Soft);

            DrawRect(new Rect2(34f, 178f, Size.X - 68f, 1.6f), Line with { A = 0.55f });

            // 항목 — 왼쪽 라벨 / 오른쪽 값.
            float y2 = 212f;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (i >= _shown) break;
                var (label, value) = Rows[i];
                DrawString(font, new Vector2(40f, y2), label, HorizontalAlignment.Left,
                    Size.X - 200f, ViewFont.S(19), Soft);
                // 마지막에 켜진 항목은 잠깐 밝게 깜빡인다.
                bool fresh = i == _shown - 1;
                DrawString(font, new Vector2(Size.X - 200f, y2), value, HorizontalAlignment.Right,
                    160f, ViewFont.S(19), fresh ? new Color(1f, 0.85f, 0.80f, pulse) : new Color(1f, 0.32f, 0.26f));
                y2 += 32f;
            }

            if (_footShown && !string.IsNullOrEmpty(Foot))
                DrawString(font, new Vector2(0f, Size.Y - 30f), Foot, HorizontalAlignment.Center,
                    Size.X, ViewFont.S(24), new Color(1f, 0.36f, 0.30f, 0.55f + 0.45f * pulse));
        }
    }

    // --- 일그러짐(화면이 찢어지며 뒤틀리는 연출) -------------------------------
    // 새 그림을 띄우는 게 아니라, 직전 컷의 같은 이미지를 가로로 잘라 어긋나게 그린다.
    private partial class WarpOverlay : Control
    {
        public Texture2D Texture;
        public Rect2 Area;
        public float Amount;
        public float Phase;

        private const int Slices = 30;
        private readonly RandomNumberGenerator _rng = new();

        public override void _Draw()
        {
            if (Texture == null || Amount <= 0.01f) return;
            var src = Texture.GetSize();
            if (src.X <= 0f || src.Y <= 0f) return;

            // 원본과 같은 자리(비율 유지, 가운데 정렬)에 놓고 조각을 흩뜨린다.
            float k = Mathf.Min(Area.Size.X / src.X, Area.Size.Y / src.Y);
            var dst = src * k;
            var at = Area.Position + (Area.Size - dst) * 0.5f;

            float sliceH = dst.Y / Slices;
            float srcH = src.Y / Slices;
            _rng.Seed = 990013;   // 매 프레임 같은 패턴 → 지직거리되 완전 난장판은 아니게

            for (int i = 0; i < Slices; i++)
            {
                float t = i / (float)Slices;
                float wob = Mathf.Sin(t * 17f + Phase) + Mathf.Sin(t * 5.3f - Phase * 0.7f);
                float off = (wob * 0.5f + _rng.RandfRange(-1f, 1f) * 0.8f) * Amount * 96f * (0.3f + t);
                float sy = Mathf.Sin(t * 11f + Phase * 1.3f) * Amount * 22f;
                float sx = 1f + Mathf.Sin(t * 8f + Phase) * Amount * 0.22f;

                var srcRect = new Rect2(0f, i * srcH, src.X, srcH + 1f);
                var dstRect = new Rect2(at.X + off - dst.X * (sx - 1f) * 0.5f, at.Y + i * sliceH + sy,
                    dst.X * sx, sliceH + 1.5f);

                // 일그러질수록 어두워지고 붉게 물든다.
                float dark = 1f - 0.45f * Amount;
                DrawTextureRectRegion(Texture, dstRect, srcRect,
                    new Color(dark, dark * (1f - 0.35f * Amount), dark * (1f - 0.35f * Amount)));

                // 채널이 어긋난 듯한 잔상.
                if (Amount > 0.35f && i % 3 == 0)
                {
                    DrawTextureRectRegion(Texture, dstRect with { Position = dstRect.Position + new Vector2(7f * Amount, 0f) },
                        srcRect, new Color(1f, 0.15f, 0.15f, 0.30f * Amount));
                    DrawTextureRectRegion(Texture, dstRect with { Position = dstRect.Position - new Vector2(7f * Amount, 0f) },
                        srcRect, new Color(0.2f, 0.9f, 1f, 0.24f * Amount));
                }
            }
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
                HorizontalAlignment.Center, Size.X, ViewFont.S(Kind == "FIGURE" ? 17 : 20),
                new Color(0.45f, 0.52f, 0.54f));
            if (!string.IsNullOrEmpty(Note))
                DrawMultilineString(font, new Vector2(14f, Size.Y * 0.40f + 30f), Note,
                    HorizontalAlignment.Center, Size.X - 28f, ViewFont.S(Kind == "FIGURE" ? 14 : 16), 4,
                    new Color(0.58f, 0.64f, 0.66f));
        }
    }

    // --- 주사선 · 필름 노이즈 · 글리치 바(영상 영역 안에만) ---------------------
    // Active 인 동안에는 Amount 가 0 이어도 주사선과 미세 노이즈를 항상 깐다 —
    // 정지 이미지가 '종이 슬라이드'처럼 보이지 않게 하는 핵심이다.
    private partial class GlitchBars : Control
    {
        public float Amount;
        public bool Active;
        public Rect2 Area = new(0, 0, 800, 600);
        private readonly RandomNumberGenerator _rng = new();

        public override void _Draw()
        {
            if (!Active && Amount <= 0.01f) return;

            // CRT 주사선.
            for (float y = Area.Position.Y; y < Area.Position.Y + Area.Size.Y; y += 3f)
                DrawRect(new Rect2(Area.Position.X, y, Area.Size.X, 1f), new Color(0f, 0f, 0f, 0.12f));

            // 미세 필름 노이즈(항상 아주 옅게).
            if (Active)
            {
                for (int i = 0; i < 90; i++)
                {
                    float x = _rng.RandfRange(Area.Position.X, Area.Position.X + Area.Size.X);
                    float y = _rng.RandfRange(Area.Position.Y, Area.Position.Y + Area.Size.Y);
                    DrawRect(new Rect2(x, y, 2f, 2f),
                        new Color(1f, 1f, 1f, _rng.RandfRange(0.015f, 0.06f)));
                }
                // 세로로 천천히 흐르는 옅은 띠 하나(필름이 돌아가는 느낌).
                float band = Area.Position.Y + Mathf.PosMod(Time.GetTicksMsec() / 26f, Area.Size.Y);
                DrawRect(new Rect2(Area.Position.X, band, Area.Size.X, 26f), new Color(1f, 1f, 1f, 0.018f));
            }

            if (Amount <= 0.01f) return;
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
