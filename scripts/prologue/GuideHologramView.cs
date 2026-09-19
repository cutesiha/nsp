using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Prologue;

// 오른쪽 CRT 전용 — 관리자 권한 승계 콘솔 / 승계 완료 시스템 창 / GUIDE-0 안내창.
// GUIDE-0 는 단순 텍스트 출력이 아니라 콘솔 화면 위에 떠오르는 '홀로그램 창'이다.
// 창 위쪽의 정사각형 칸이 GUIDE-0 의 얼굴 자리이며, 지금은 임시 초상(placeholder)이 뜬다.
//
// ★ 얼굴 교체:  res://assets/ui/guide0/guide0_<표정키>.png  파일을 넣기만 하면 된다.
//   표정키는 데이터 파일(NSP_PROLOGUE_RUNTIME.md)의 portrait: 값 — 현재 normal / smile.
//   파일이 없으면 자동으로 "GUIDE-0 PORTRAIT" 임시 박스가 대신 그려진다.
//
// GUIDE-0 가 말하는 동안 창 오른쪽의 보조 정보판(InfoPanel)이 같이 움직인다 — 얼굴만
// 15초 보고 있지 않도록. 어떤 정보판을 띄울지는 데이터 파일의 panel: 이 정한다.
public partial class GuideHologramView : Control
{
    public static GuideHologramView Instance { get; private set; }

    // 초상 파일이 놓일 폴더. 표정키마다 guide0_<키>.png 로 넣는다.
    public const string PortraitDir = "res://assets/ui/guide0/";

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color ConsoleInk = new(0.62f, 0.92f, 0.78f);
    private static readonly Color ConsoleOk = new(0.75f, 1f, 0.6f);

    private Font _font;
    private RichTextLabel _console;
    private HoloFrame _holo;
    private Control _holoRoot;
    private PortraitBox _portrait;
    private Label _name;
    private Label _line;
    private HBoxContainer _icons;
    private VBoxContainer _choices;
    private Label _hint;
    private InfoPanel _panel;
    private SystemWindow _window;
    // 압축 모드 — 얼굴/이름/대사를 이 창에서 빼고(왼쪽 CRT 와 자막 띠가 맡는다)
    // 보조 정보판과 선택지만 크게 보여준다.
    private bool _compact;

    // 진행 상태
    private readonly List<string> _consoleLines = new();
    private PrologueScript.ConsoleBlock _consoleBlock;
    private int _consoleStep;
    private double _consoleWait;
    private Action _consoleDone;
    // 지금 한 글자씩 찍히는 중인 줄.
    private string _consoleTyping = "";
    private int _consoleTypedChars;
    private double _consoleTypeClock;
    private bool _consoleTypingIsOk;

    // 콘솔이 끝난 뒤 잠깐 떴다 닫히는 시스템 창(권한 승계 완료).
    private PrologueScript.WindowBlock _windowBlock;
    private double _windowLeft;
    private Action _windowDone;

    private PrologueScript.GuideBlock _guide;
    private int _beat;
    private double _lineElapsed;
    private double _lineDuration;
    private Action _guideDone;
    // true 면 마지막 대사의 타이핑이 끝나는 순간을 완료로 본다(뒤에 선택지가 이어질 때).
    private bool _completeWhenTyped;
    private Action<PrologueScript.MenuOption> _menuPick;
    // 이미 확인한 선택지(mode: all 메뉴의 체크 표시).
    private readonly HashSet<string> _answeredOptions = new();

    private double _noiseUntil;
    // GUIDE-0 대사가 한 글자씩 드러날 때마다 전용 보이스를 울리기 위한 진행도.
    private int _spokenChars;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;
        BuildUi();
        SetProcess(true);
        HideHologram();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // --- 콘솔 -------------------------------------------------------------

    public void ClearConsole()
    {
        _consoleLines.Clear();
        _consoleTyping = "";
        _consoleTypedChars = 0;
        _console.Text = "";
        _console.Visible = true;
    }

    // cmd 처럼 한 줄씩 출력한다. 마지막 ok: 줄에서 띠롱 효과음이 난다.
    public void PlayConsole(string consoleId, Action onDone)
    {
        ClearConsole();
        _consoleBlock = PrologueScript.GetConsole(consoleId);
        _consoleStep = 0;
        _consoleWait = 0.35;
        _consoleDone = onDone;
        if (_consoleBlock == null)
        {
            _consoleBlock = null;
            onDone?.Invoke();
        }
    }

    // --- 승계 완료 시스템 창 ------------------------------------------------

    // 콘솔이 사라지고, 화면 한가운데 창이 떴다가 hold 초 뒤에 닫힌다.
    // 권한 승계와 GUIDE-0 등장을 별개의 사건으로 느끼게 하는 사이 박자다.
    public void PlayWindow(string windowId, Action onDone)
    {
        var block = PrologueScript.GetWindow(windowId);
        if (block == null)
        {
            GD.PushWarning($"GuideHologramView: 시스템 창 '{windowId}' 를 찾지 못했습니다.");
            onDone?.Invoke();
            return;
        }
        _console.Visible = false;
        _windowBlock = block;
        _windowLeft = block.Hold;
        _windowDone = onDone;
        _window.Title = block.Title;
        _window.Big = block.Big;
        _window.Lines = block.Lines;
        _window.Visible = true;
        _window.Modulate = new Color(1f, 1f, 1f, 0f);
        _window.Scale = new Vector2(0.92f, 0.92f);
        _window.PivotOffset = _window.Size * 0.5f;
        _window.QueueRedraw();
        Sfx.Instance?.Play("window_open", -6f);
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_window, "modulate:a", 1f, 0.18);
        t.TweenProperty(_window, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back);
    }

    private void TickWindow(double delta)
    {
        if (_windowBlock == null) return;
        _windowLeft -= delta;
        if (_windowLeft > 0) return;
        _windowBlock = null;
        _window.Visible = false;
        var cb = _windowDone;
        _windowDone = null;
        cb?.Invoke();
    }

    // --- GUIDE-0 홀로그램 --------------------------------------------------

    public void ShowHologram()
    {
        if (_holoRoot.Visible) return;
        _holoRoot.Visible = true;
        _holoRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _holoRoot.Scale = new Vector2(0.86f, 0.86f);
        _holoRoot.PivotOffset = Canvas * 0.5f;
        Sfx.Instance?.Play("sensor_beep", -8f);
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_holoRoot, "modulate:a", 1f, 0.35);
        t.TweenProperty(_holoRoot, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back);
    }

    public void HideHologram()
    {
        if (!_holoRoot.Visible) { ResetGuideState(); return; }
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_holoRoot, "modulate:a", 0f, 0.3);
        t.TweenProperty(_holoRoot, "scale", new Vector2(0.9f, 0.9f), 0.3);
        t.Chain().TweenCallback(Callable.From(() =>
        {
            _holoRoot.Visible = false;
            ResetGuideState();
        }));
    }

    private void ResetGuideState()
    {
        _guide = null;
        _guideDone = null;
        _completeWhenTyped = false;
        _menuPick = null;
        ClearChoices();
        _icons.Visible = false;
        _hint.Visible = false;
        SetPanel("none");
    }

    // 프롤로그에서 화면을 셋으로 나눌 때 켠다.
    //   왼쪽 CRT = GuideFaceView(얼굴)  /  이 창 = 그림·도형  /  화면 아래 = 대사
    public void SetCompact(bool on)
    {
        _compact = on;
        _portrait.Visible = !on;
        _name.Visible = !on;
        _line.Visible = !on;
        _hint.Visible = _hint.Visible && !on;

        // 얼굴이 빠진 만큼 정보판을 크게 키워 가운데에 놓는다.
        _panel.Position = on ? new Vector2(178f, 104f) : new Vector2(388f, 88f);
        _panel.Scale = on ? new Vector2(1.5f, 1.5f) : Vector2.One;
        _icons.Position = on ? new Vector2(126f, 368f) : new Vector2(126f, 380f);
        _choices.Position = on ? new Vector2(148f, 376f) : new Vector2(148f, 382f);
    }

    // 자막 띠가 같은 속도로 글자를 드러내기 위해 읽어 가는 값.
    public string CurrentLineText => _line?.Text ?? "";
    public float CurrentLineRatio => _line?.VisibleRatio ?? 1f;

    // 창 오른쪽 보조 정보판 — none / alert / authority / mission.
    private void SetPanel(string kind)
    {
        string k = string.IsNullOrEmpty(kind) ? "none" : kind.Trim().ToLowerInvariant();
        // 압축 모드에서는 창에 대사가 없으므로, 보여줄 정보판이 없을 때도 빈 창으로 두지 않는다.
        if (_compact && k == "none") k = "idle";
        _panel.Kind = k;
        _panel.Visible = k != "none";
        _panel.QueueRedraw();
    }

    // 대사 묶음을 순서대로 보여준다. 다 끝나면 마지막 줄을 화면에 남긴 채 onDone 을 부른다
    // (튜토리얼 중에는 그 줄이 그대로 지시문 역할을 한다).
    public void ShowGuide(string guideId, Action onDone = null, bool completeWhenTyped = false)
    {
        var block = PrologueScript.GetGuide(guideId);
        if (block == null)
        {
            GD.PushWarning($"GuideHologramView: GUIDE-0 대사 '{guideId}' 를 찾지 못했습니다.");
            onDone?.Invoke();
            return;
        }
        ShowHologram();
        ClearChoices();
        _icons.Visible = false;
        _guide = block;
        _guideDone = onDone;
        _completeWhenTyped = completeWhenTyped;
        _beat = -1;
        SetPortrait(block.StartPortrait);
        SetPanel(block.StartPanel);
        NextBeat();
    }

    // 치환자가 있는 대사(예: tut_relocate 의 {ROOM})를 쓸 때.
    public void ShowGuide(string guideId, Dictionary<string, string> replacements, Action onDone = null,
        bool completeWhenTyped = false)
    {
        _replacements = replacements;
        ShowGuide(guideId, onDone, completeWhenTyped);
    }

    private Dictionary<string, string> _replacements;

    // 새 메뉴를 처음 열 때 호출 — 확인 표시를 초기화한다.
    public void ResetMenuProgress() => _answeredOptions.Clear();

    public void ShowMenu(string menuId, Action<PrologueScript.MenuOption> onPick)
    {
        var menu = PrologueScript.GetMenu(menuId);
        ClearChoices();
        if (menu == null || menu.Options.Count == 0) { onPick?.Invoke(null); return; }

        ShowHologram();
        _menuPick = onPick;
        _hint.Visible = false;

        // 화면 아래 자막 블록이 떠 있으면 선택지도 그 안에 둔다 — 대사와 같은 자리에서 고른다.
        var hud = GuideSubtitleHud.Instance;
        bool useHud = hud is { IsActive: true };
        var hudList = useHud ? new List<GuideSubtitleHud.Choice>() : null;

        foreach (var opt in menu.Options)
        {
            var captured = opt;
            bool answered = _answeredOptions.Contains(captured.GuideId);
            // 이미 확인한 질문은 체크 표시 + 어두운 색으로 눌러 두고 다시 고를 수 없게 한다.
            var accent = answered ? new Color(0.30f, 0.44f, 0.48f) : Cyan;
            string label = (answered ? "✓  " : "") + captured.Label;

            void Pick()
            {
                if (answered) return;
                ClearChoices();
                Sfx.Instance?.Play("click", -10f);
                _answeredOptions.Add(captured.GuideId);
                var cb = _menuPick;
                _menuPick = null;
                cb?.Invoke(captured);
            }

            if (useHud)
            {
                hudList.Add(new GuideSubtitleHud.Choice { Text = label, Disabled = answered, OnPick = Pick });
                continue;
            }

            var b = MonitorUi.Button(label, accent, _font, Pick, ViewFont.S(16));
            b.Disabled = answered;
            b.CustomMinimumSize = new Vector2(0f, 40f);
            _choices.AddChild(b);
        }

        if (useHud) hud.ShowChoices(hudList);
    }

    // mode: all 메뉴에서 모든 항목을 한 번씩 확인했는가.
    public bool AllOptionsAnswered(string menuId)
    {
        var menu = PrologueScript.GetMenu(menuId);
        if (menu == null) return true;
        foreach (var o in menu.Options)
            if (!_answeredOptions.Contains(o.GuideId)) return false;
        return true;
    }

    private void ClearChoices()
    {
        foreach (Node c in _choices.GetChildren()) c.QueueFree();
        GuideSubtitleHud.Instance?.ClearChoices();
    }

    private void NextBeat()
    {
        if (_guide == null) return;
        _beat++;
        while (_beat < _guide.Beats.Count)
        {
            var b = _guide.Beats[_beat];
            switch (b.Kind)
            {
                case PrologueScript.GuideBeatKind.Portrait:
                    SetPortrait(b.Value);
                    _beat++;
                    continue;
                case PrologueScript.GuideBeatKind.Icons:
                    BuildEmployeeIcons();
                    _beat++;
                    continue;
                case PrologueScript.GuideBeatKind.Panel:
                    SetPanel(b.Value);
                    _beat++;
                    continue;
                case PrologueScript.GuideBeatKind.Noise:
                    _noiseUntil = Time.GetTicksMsec() / 1000.0 + 0.7;
                    GuideFaceView.Instance?.Flash();
                    Sfx.Instance?.Play("noise", -10f);
                    _beat++;
                    continue;
                default:
                    StartLine(Substitute(b.Value));
                    return;
            }
        }

        // 모든 대사 끝 — 마지막 줄은 그대로 남긴다(튜토리얼에서는 그게 지시문이다).
        _hint.Visible = false;
        _guide = null;
        var done = _guideDone;
        _guideDone = null;
        done?.Invoke();
    }

    private string Substitute(string text)
    {
        if (_replacements == null || string.IsNullOrEmpty(text)) return text;
        foreach (var kv in _replacements) text = text.Replace("{" + kv.Key + "}", kv.Value);
        return text;
    }

    // 지금 화면에 뜬 GUIDE-0 대사. 오른쪽 CRT 가 보이지 않는 단계에서는
    // GuideSubtitleHud 가 이 이벤트를 받아 같은 문장을 화면 아래에 띄운다.
    public event Action<string> LineShown;

    private void StartLine(string text)
    {
        LineShown?.Invoke(text);
        _line.Text = text;
        _line.VisibleRatio = 0f;
        _lineElapsed = 0;
        _spokenChars = 0;
        Sfx.Instance?.StopVoiceBlip();
        // 글자가 찍히기 시작하면 입도 같이 움직이기 시작한다.
        GuideMouthAnimator.StartTalking();
        // 타이핑 속도는 프롤로그 자막과 같은 값(PrologueTextStyle)을 쓴다.
        _lineDuration = PrologueTextStyle.TypeSeconds(text);
        _hint.Visible = !_compact;
    }

    private void SetPortrait(string key)
    {
        _portrait.Expression = string.IsNullOrEmpty(key) ? "normal" : key;
        _portrait.Texture = GuideArt.Portrait(_portrait.Expression, out bool mouthless);
        _portrait.Mouthless = mouthless;
        _portrait.QueueRedraw();
        // 왼쪽 CRT 의 얼굴 화면도 같은 표정으로 맞춘다.
        GuideFaceView.Instance?.SetPortrait(_portrait.Expression, _portrait.Texture, mouthless);
    }

    // 초상 탐색 규칙은 GuideArt 한 곳에만 둔다(얼굴 화면도 같은 규칙을 쓴다).
    private static Texture2D LoadPortrait(string expression) =>
        GuideArt.Portrait(expression, out _);

    private void BuildEmployeeIcons()
    {
        foreach (Node c in _icons.GetChildren()) c.QueueFree();
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        foreach (var id in sim.GetEmployeeIds())
        {
            var def = sim.GetEmployeeDef(id);
            if (def == null) continue;
            _icons.AddChild(new EmployeeIcon
            {
                CustomMinimumSize = new Vector2(52f, 52f),
                Tex = def.FacePortrait,
                Tint = def.IconColor,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        _icons.Visible = true;
    }

    // --- tick -------------------------------------------------------------

    public override void _Process(double delta)
    {
        TickConsole(delta);
        TickWindow(delta);
        if (_panel.Visible) _panel.Tick(delta);

        // 대사는 절대 저절로 넘어가지 않는다 — 스페이스/엔터/클릭을 기다린다.
        if (_guide != null && _line.VisibleRatio < 1f)
        {
            _lineElapsed += delta;
            _line.VisibleRatio = PrologueTextStyle.Ratio(_line.Text, _lineElapsed);
            SpeakRevealed();
            if (_line.VisibleRatio >= 1f)
            {
                GuideMouthAnimator.StopTalking();
                OnLineTypedOut();
            }
        }

        // 대사 묶음이 끝났으면 어떤 경로로 끝났든 입을 다문다.
        if (_guide == null && GuideMouthAnimator.Talking) GuideMouthAnimator.StopTalking();
        // 다 읽고도 넘기지 않는 동안에만 얼굴이 가끔 일그러진다.
        GuideMouthAnimator.SetIdleWaiting(_guide != null && _line.VisibleRatio >= 1f);
        if (GuideMouthAnimator.Tick(delta))
        {
            _portrait.QueueRedraw();
            GuideFaceView.Instance?.NotifyMouthChanged();
        }

        float noise = Time.GetTicksMsec() / 1000.0 < _noiseUntil ? 1f : 0f;
        if (!Mathf.IsEqualApprox(_holo.Noise, noise)) { _holo.Noise = noise; }
        _holo.QueueRedraw();
    }

    // cmd 처럼 한 글자씩 찍는다. 글자가 찍힐 때마다 타자 소리가 난다(언더테일식 블립).
    private void TickConsole(double delta)
    {
        if (_consoleBlock == null) return;

        // 1) 찍는 중인 줄이 있으면 먼저 그 줄을 마저 찍는다.
        if (_consoleTypedChars < _consoleTyping.Length)
        {
            _consoleTypeClock += delta;
            while (_consoleTypedChars < _consoleTyping.Length
                   && _consoleTypeClock >= PrologueTextStyle.ConsoleSecondsPerChar)
            {
                _consoleTypeClock -= PrologueTextStyle.ConsoleSecondsPerChar;
                _consoleTypedChars++;
                char c = _consoleTyping[_consoleTypedChars - 1];
                if (c != ' ' && _consoleTypedChars % PrologueTextStyle.BlipEveryChars == 0)
                    Sfx.Instance?.Play("key_single", -16f, (float)GD.RandRange(0.92, 1.12));
            }
            RenderConsole();
            if (_consoleTypedChars >= _consoleTyping.Length)
            {
                if (_consoleTypingIsOk) Sfx.Instance?.Play("task_done", -6f);
                _consoleWait = _consoleTypingIsOk ? PrologueTextStyle.ConsoleOkGap : PrologueTextStyle.ConsoleLineGap;
                _consoleTyping = "";
                _consoleTypedChars = 0;
            }
            return;
        }

        _consoleWait -= delta;
        if (_consoleWait > 0) return;

        if (_consoleStep >= _consoleBlock.Steps.Count)
        {
            _consoleBlock = null;
            var cb = _consoleDone;
            _consoleDone = null;
            cb?.Invoke();
            return;
        }

        var step = _consoleBlock.Steps[_consoleStep++];
        if (step.IsWaitOnly) { _consoleWait = step.Wait; return; }

        if (string.IsNullOrEmpty(step.Text))
        {
            _consoleLines.Add("");
            RenderConsole();
            _consoleWait = PrologueTextStyle.ConsoleLineGap;
            return;
        }

        _consoleLines.Add("");                 // 이 줄을 채워 나간다
        _consoleTyping = step.Text;
        _consoleTypedChars = 0;
        _consoleTypeClock = 0;
        _consoleTypingIsOk = step.IsOk;
        RenderConsole();
    }

    // 마지막 줄만 "지금까지 찍힌 만큼" 보여주고, 그 뒤에 깜빡이는 커서를 붙인다.
    private void RenderConsole()
    {
        if (_consoleLines.Count > 0 && _consoleTyping.Length > 0)
        {
            string shown = _consoleTyping[.._consoleTypedChars];
            _consoleLines[^1] = _consoleTypingIsOk ? $"[color=#bbff99]{shown}[/color]" : shown;
        }
        bool caret = (Time.GetTicksMsec() / 380) % 2 == 0;
        _console.Text = string.Join("\n", _consoleLines) + (caret ? " _" : "");
    }

    // 스페이스/엔터/클릭으로만 넘어간다(자동 진행 없음). 콘솔이 돌아가는 동안에도 받는다.
    public bool IsWaitingForInput => _guide != null || _consoleBlock != null;

    public void RequestAdvance()
    {
        // 승계 콘솔 — 찍는 중이면 그 줄을 즉시 완성하고, 아니면 곧바로 다음 줄로 넘긴다.
        if (_consoleBlock != null)
        {
            if (_consoleTypedChars < _consoleTyping.Length)
            {
                _consoleTypedChars = _consoleTyping.Length;
                RenderConsole();
                if (_consoleTypingIsOk) Sfx.Instance?.Play("task_done", -6f);
                _consoleTyping = "";
                _consoleTypedChars = 0;
            }
            _consoleWait = 0;
            return;
        }

        if (_guide == null) return;
        if (_line.VisibleRatio < 1f)
        {
            _line.VisibleRatio = 1f;
            _lineElapsed = _lineDuration;
            _spokenChars = _line.Text.Length;
            Sfx.Instance?.StopVoiceBlip();
            // 즉시 출력 완료 — 입도 그 자리에서 멈춘다.
            GuideMouthAnimator.StopTalking();
            return;
        }
        NextBeat();
    }

    // 마지막 대사의 타이핑이 끝난 순간 — completeWhenTyped 모드면 여기서 바로 완료 처리한다.
    // 대사는 화면에 그대로 남고, 호출부가 곧바로 선택지를 띄운다.
    private void OnLineTypedOut()
    {
        if (!_completeWhenTyped || _guide == null) return;
        if (_beat < _guide.Beats.Count - 1) return;   // 아직 남은 대사가 있다
        _hint.Visible = false;
        _guide = null;
        _completeWhenTyped = false;
        var done = _guideDone;
        _guideDone = null;
        done?.Invoke();
    }

    // 새로 드러난 글자만큼 GUIDE-0 보이스를 울린다(직원 보이스와 같은 재생 경로, 다른 Voice ID).
    private void SpeakRevealed()
    {
        string voice = _guide?.VoiceId ?? "";
        int shown = Mathf.RoundToInt(_line.VisibleRatio * _line.Text.Length);
        while (_spokenChars < shown)
        {
            char c = _line.Text[_spokenChars];
            _spokenChars++;
            // 입은 글자마다 모양을 바꾸지 않는다 — 문장부호에서 잠깐 다물 때만 쓴다.
            GuideMouthAnimator.NoticeCharacter(c);
            if (!string.IsNullOrEmpty(voice)) Sfx.Instance?.PlayVoiceBlip(voice, c);
        }
    }

    // CRT 를 직접 클릭해도 넘어간다(화면 아무 곳이나 누르는 경로는 PrologueAdvanceInput).
    public override void _GuiInput(InputEvent e)
    {
        if (_guide == null) return;
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        RequestAdvance();
        AcceptEvent();
    }

    // --- UI ---------------------------------------------------------------

    private void BuildUi()
    {
        var bg = new ColorRect { Color = new Color(0.015f, 0.03f, 0.028f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _console = new RichTextLabel
        {
            BbcodeEnabled = true,
            Position = new Vector2(28f, 26f),
            Size = new Vector2(744f, 548f),
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _console.AddThemeFontOverride("normal_font", _font);
        _console.AddThemeFontSizeOverride("normal_font_size", ViewFont.S(17));
        _console.AddThemeColorOverride("default_color", ConsoleInk);
        AddChild(_console);

        _holoRoot = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = Canvas };
        AddChild(_holoRoot);

        _holo = new HoloFrame
        {
            Position = new Vector2(96f, 44f),
            Size = new Vector2(608f, 512f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _holoRoot.AddChild(_holo);

        // 정사각형 얼굴 창 — 여기에 최종 도트 초상화가 들어간다.
        // (창 위쪽 28px 는 GUIDE-0.exe 제목 표시줄이 쓴다.)
        _portrait = new PortraitBox
        {
            Position = new Vector2(150f, 88f),
            Size = new Vector2(164f, 164f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _holoRoot.AddChild(_portrait);

        // 말하는 동안 같이 움직이는 보조 정보판(재난 현황 / 권한 계층도 / 목표).
        _panel = new InfoPanel
        {
            Position = new Vector2(388f, 88f),
            Size = new Vector2(296f, 164f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _holoRoot.AddChild(_panel);

        _name = MakeLabel("GUIDE-0", 18, Cyan, new Vector2(150f, 256f));
        _name.Size = new Vector2(164f, 26f);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_name);

        _line = MakeLabel("", 20, new Color(0.88f, 0.98f, 1f), new Vector2(126f, 292f));
        _line.Size = new Vector2(548f, 84f);
        _line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _line.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_line);

        _icons = new HBoxContainer
        {
            Position = new Vector2(126f, 380f),
            Size = new Vector2(548f, 56f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _icons.AddThemeConstantOverride("separation", 10);
        _holoRoot.AddChild(_icons);

        _choices = new VBoxContainer
        {
            Position = new Vector2(148f, 382f),
            Size = new Vector2(504f, 150f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        _choices.AddThemeConstantOverride("separation", 7);
        _holoRoot.AddChild(_choices);

        _hint = MakeLabel("클릭하여 계속", 13, new Color(0.45f, 0.62f, 0.66f), new Vector2(0f, 534f));
        _hint.Size = new Vector2(Canvas.X, 20f);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_hint);

        // 승계 완료 창은 홀로그램과 별개로 화면 한가운데 뜬다.
        _window = new SystemWindow
        {
            Position = new Vector2(196f, 188f),
            Size = new Vector2(408f, 216f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_window);
    }

    private Label MakeLabel(string text, int size, Color col, Vector2 pos)
    {
        var l = new Label { Text = text, Position = pos, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // --- 홀로그램 창 테두리 -------------------------------------------------
    private partial class HoloFrame : Control
    {
        public float Noise;
        private readonly RandomNumberGenerator _rng = new();

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.06f, 0.20f, 0.24f, 0.72f));
            DrawRect(box, Cyan with { A = 0.85f }, false, 2f);
            DrawRect(box.Grow(4f), Cyan with { A = 0.25f }, false, 1f);

            // 제목 표시줄 — "GUIDE-0.exe 라는 작은 프로그램이 떴다"는 인상을 준다.
            var bar = new Rect2(0f, 0f, Size.X, 28f);
            DrawRect(bar, new Color(0.10f, 0.32f, 0.36f, 0.92f));
            DrawRect(bar, Cyan with { A = 0.45f }, false, 1f);
            var font = ViewFont.Default;
            DrawString(font, new Vector2(12f, 20f), "GUIDE-0.exe", HorizontalAlignment.Left,
                Size.X - 100f, ViewFont.S(14), Cyan with { A = 0.95f });
            DrawString(font, new Vector2(Size.X - 84f, 20f), "—  □  ✕", HorizontalAlignment.Left,
                80f, ViewFont.S(14), Cyan with { A = 0.55f });

            // 홀로그램 스캔라인.
            for (float y = 0; y < Size.Y; y += 4f)
                DrawRect(new Rect2(0, y, Size.X, 1f), new Color(0.55f, 0.95f, 1f, 0.05f));

            // 모서리 브래킷.
            const float k = 22f;
            var c = Cyan;
            DrawLine(new Vector2(0, 0), new Vector2(k, 0), c, 3f);
            DrawLine(new Vector2(0, 0), new Vector2(0, k), c, 3f);
            DrawLine(new Vector2(Size.X, 0), new Vector2(Size.X - k, 0), c, 3f);
            DrawLine(new Vector2(Size.X, 0), new Vector2(Size.X, k), c, 3f);
            DrawLine(new Vector2(0, Size.Y), new Vector2(k, Size.Y), c, 3f);
            DrawLine(new Vector2(0, Size.Y), new Vector2(0, Size.Y - k), c, 3f);
            DrawLine(new Vector2(Size.X, Size.Y), new Vector2(Size.X - k, Size.Y), c, 3f);
            DrawLine(new Vector2(Size.X, Size.Y), new Vector2(Size.X, Size.Y - k), c, 3f);

            if (Noise <= 0.01f) return;
            for (int i = 0; i < 12; i++)
            {
                float y = _rng.RandfRange(0f, Size.Y);
                DrawRect(new Rect2(0, y, Size.X, _rng.RandfRange(2f, 10f)),
                    new Color(0.8f, 1f, 1f, _rng.RandfRange(0.06f, 0.24f)));
            }
        }
    }

    // --- 얼굴 자리 --------------------------------------------------------
    private partial class PortraitBox : Control
    {
        public Texture2D Texture;
        public string Expression = "normal";
        // 얼굴 그림에 입이 없어 Overlay 를 얹어야 하는가.
        public bool Mouthless;

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.03f, 0.12f, 0.15f, 0.9f));

            if (Texture != null)
            {
                var src = Texture.GetSize();
                if (src.X > 0f && src.Y > 0f)
                {
                    float k = Mathf.Min(Size.X / src.X, Size.Y / src.Y);
                    var dst = src * k;
                    var at = box.Position + (Size - dst) * 0.5f
                             + new Vector2(0f, GuideMouthAnimator.BobOffset);
                    GuideFacePaint.Draw(this, Texture, Mouthless, new Rect2(at, dst), Cyan);
                }
            }
            else
            {
                // 임시 초상 — 최종 도트 얼굴이 들어오면 이 블록은 그려지지 않는다.
                var font = ViewFont.Default;
                DrawString(font, new Vector2(0f, Size.Y * 0.44f), "GUIDE-0",
                    HorizontalAlignment.Center, Size.X, ViewFont.S(22), Cyan with { A = 0.9f });
                DrawString(font, new Vector2(0f, Size.Y * 0.58f), "PORTRAIT",
                    HorizontalAlignment.Center, Size.X, ViewFont.S(15), Cyan with { A = 0.55f });
                DrawString(font, new Vector2(0f, Size.Y * 0.74f), $"[{Expression}]",
                    HorizontalAlignment.Center, Size.X, ViewFont.S(13), Cyan with { A = 0.35f });
            }

            DrawRect(box, Cyan with { A = 0.9f }, false, 2f);
        }
    }

    // --- 승계 완료 시스템 창 -------------------------------------------------
    private partial class SystemWindow : Control
    {
        public string Title = "";
        public string Big = "";
        public System.Collections.Generic.IReadOnlyList<string> Lines = System.Array.Empty<string>();

        public override void _Draw()
        {
            var font = ViewFont.Default;
            var box = new Rect2(Vector2.Zero, Size);

            DrawRect(box.Grow(3f), new Color(0f, 0f, 0f, 0.45f));
            DrawRect(box, new Color(0.05f, 0.12f, 0.14f, 0.97f));
            DrawRect(box, Cyan with { A = 0.9f }, false, 2f);

            var bar = new Rect2(0f, 0f, Size.X, 30f);
            DrawRect(bar, new Color(0.10f, 0.32f, 0.36f, 0.95f));
            DrawString(font, new Vector2(14f, 21f), Title, HorizontalAlignment.Left,
                Size.X - 70f, ViewFont.S(16), Cyan);
            DrawString(font, new Vector2(Size.X - 30f, 21f), "✕", HorizontalAlignment.Left,
                26f, ViewFont.S(14), Cyan with { A = 0.5f });

            DrawString(font, new Vector2(0f, 108f), Big, HorizontalAlignment.Center,
                Size.X, ViewFont.S(42), new Color(0.80f, 1f, 0.92f));

            float y = 150f;
            foreach (string l in Lines)
            {
                DrawString(font, new Vector2(0f, y), l, HorizontalAlignment.Center,
                    Size.X, ViewFont.S(14), Cyan with { A = 0.72f });
                y += 24f;
            }
        }
    }

    // --- 보조 정보판 --------------------------------------------------------
    // GUIDE-0 가 말하는 동안 옆에서 같이 움직이는 판. 대사 내용과 짝을 이룬다.
    private partial class InfoPanel : Control
    {
        public string Kind = "none";
        private float _t;

        public void Tick(double delta)
        {
            _t += (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = ViewFont.Default;
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.03f, 0.14f, 0.17f, 0.80f));
            DrawRect(box, Cyan with { A = 0.55f }, false, 1.4f);
            for (float y = 0; y < Size.Y; y += 4f)
                DrawRect(new Rect2(0, y, Size.X, 1f), new Color(0.55f, 0.95f, 1f, 0.04f));

            switch (Kind)
            {
                case "idle": DrawIdle(font); break;
                case "alert": DrawAlert(font); break;
                case "authority": DrawAuthority(font); break;
                case "mission": DrawMission(font); break;
            }
        }

        // 보여줄 정보가 없는 동안의 대기 화면 — 창이 살아 있다는 느낌만 준다.
        private void DrawIdle(Font font)
        {
            DrawString(font, new Vector2(16f, 30f), "GUIDANCE UNIT", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(14), Cyan with { A = 0.8f });
            DrawString(font, new Vector2(16f, 54f), "ONLINE", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(20), new Color(0.80f, 1f, 0.92f));

            DrawLine(new Vector2(16f, 70f), new Vector2(Size.X - 16f, 70f), Cyan with { A = 0.3f }, 1f);

            // 천천히 흐르는 진단 막대 몇 줄.
            for (int i = 0; i < 4; i++)
            {
                float y = 88f + i * 18f;
                var bar = new Rect2(16f, y, Size.X - 32f, 9f);
                DrawRect(bar, new Color(0f, 0f, 0f, 0.35f));
                float w = 0.35f + 0.6f * (0.5f + 0.5f * Mathf.Sin(_t * (0.7f + i * 0.31f) + i));
                DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * w, bar.Size.Y)),
                    Cyan with { A = 0.35f + 0.2f * i * 0.25f });
            }

            // 좌우로 흐르는 스캔 표시.
            float sweep = Mathf.PosMod(_t * 0.4f, 1f);
            DrawRect(new Rect2(16f + (Size.X - 32f) * sweep, 160f, 3f, 12f), Cyan with { A = 0.7f });
            DrawString(font, new Vector2(16f, Size.Y - 10f), "SYS DIAGNOSTIC  ·  NOMINAL",
                HorizontalAlignment.Left, Size.X - 32f, ViewFont.S(12), Cyan with { A = 0.55f });
        }

        // 무슨 일이 벌어진 거지? — 재난 / 격리 붕괴 / 신원 오류.
        private void DrawAlert(Font font)
        {
            var red = new Color(1f, 0.38f, 0.30f);
            float blink = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_t * 3.2f));

            // 경고 삼각형.
            var c = new Vector2(34f, 40f);
            var pts = new[] { c + new Vector2(0f, -17f), c + new Vector2(16f, 12f), c + new Vector2(-16f, 12f) };
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, red with { A = blink }, 2.2f);
            DrawString(font, new Vector2(c.X - 4f, c.Y + 9f), "!", HorizontalAlignment.Left, 20f,
                ViewFont.S(16), red with { A = blink });

            DrawString(font, new Vector2(62f, 32f), "외부 대규모 재난", HorizontalAlignment.Left,
                Size.X - 74f, ViewFont.S(16), new Color(0.94f, 0.96f, 0.98f));
            DrawString(font, new Vector2(62f, 52f), "격리 시스템 붕괴", HorizontalAlignment.Left,
                Size.X - 74f, ViewFont.S(16), new Color(0.94f, 0.96f, 0.98f));

            DrawLine(new Vector2(16f, 70f), new Vector2(Size.X - 16f, 70f), Cyan with { A = 0.3f }, 1f);

            // 직원 여섯 명 실루엣 — 하나만 붉게 깜빡인다.
            DrawString(font, new Vector2(16f, 90f), "현장 인원 6", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(13), Cyan with { A = 0.8f });
            int odd = (int)(_t * 0.7f) % 6;
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector2(20f + i * 30f, 98f);
                bool bad = i == odd;
                var col = bad ? red with { A = blink } : Cyan with { A = 0.75f };
                DrawCircle(p + new Vector2(10f, 7f), 5f, col);
                DrawRect(new Rect2(p.X + 4.5f, p.Y + 14f, 11f, 14f), col);
            }

            DrawString(font, new Vector2(16f, Size.Y - 10f), "신원 기록 불일치  1 / 6",
                HorizontalAlignment.Left, Size.X - 32f, ViewFont.S(14), red with { A = blink });
        }

        // 나는 누구지? — 관제 담당에서 총괄 관리자로 권한이 올라간다.
        private void DrawAuthority(Font font)
        {
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_t * 2.4f));
            DrawString(font, new Vector2(16f, 26f), "관리 권한 계층", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(13), Cyan with { A = 0.75f });

            var dead = new Color(0.55f, 0.60f, 0.62f);
            var upper = new Rect2(24f, 40f, Size.X - 48f, 34f);
            DrawRect(upper, new Color(0.08f, 0.16f, 0.18f, 0.7f));
            DrawRect(upper, dead with { A = 0.6f }, false, 1.2f);
            DrawString(font, new Vector2(upper.Position.X + 12f, upper.Position.Y + 23f),
                "전임 총괄 관리자", HorizontalAlignment.Left, upper.Size.X - 20f, ViewFont.S(15), dead);
            DrawLine(new Vector2(upper.Position.X + 8f, upper.Position.Y + 17f),
                new Vector2(upper.End.X - 8f, upper.Position.Y + 17f), new Color(0.85f, 0.3f, 0.25f, 0.8f), 1.6f);

            // 아래에서 위로 올라가는 화살표.
            float ax = Size.X * 0.5f;
            float ay = 92f + Mathf.Sin(_t * 3.4f) * 2.5f;
            DrawLine(new Vector2(ax, ay + 12f), new Vector2(ax, ay - 8f), Cyan with { A = pulse }, 2f);
            DrawPolyline(new[]
            {
                new Vector2(ax - 6f, ay - 2f), new Vector2(ax, ay - 10f), new Vector2(ax + 6f, ay - 2f),
            }, Cyan with { A = pulse }, 2f);

            var lower = new Rect2(24f, 108f, Size.X - 48f, 46f);
            DrawRect(lower, new Color(0.10f, 0.30f, 0.34f, 0.85f * pulse + 0.1f));
            DrawRect(lower, Cyan with { A = 0.9f }, false, 1.8f);
            DrawString(font, new Vector2(lower.Position.X + 12f, lower.Position.Y + 16f),
                "관제 담당", HorizontalAlignment.Left, lower.Size.X - 20f, ViewFont.S(11),
                Cyan with { A = 0.6f });
            DrawString(font, new Vector2(lower.Position.X + 12f, lower.Position.Y + 39f),
                "▶ 시설 총괄 관리자", HorizontalAlignment.Left, lower.Size.X - 20f, ViewFont.S(15),
                new Color(0.88f, 1f, 0.96f));
        }

        // 내가 해야 할 일은? — 남은 시간과 코어 목표.
        private void DrawMission(Font font)
        {
            DrawString(font, new Vector2(16f, 26f), "비상 차폐 잔여 시간", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(13), Cyan with { A = 0.78f });

            // 120시간에서 아주 천천히 줄어드는 타이머(실제 게임 시간과는 무관한 연출용).
            double total = 120 * 3600 - _t * 3.0;
            int h = Mathf.Max(0, (int)(total / 3600));
            int m = Mathf.Max(0, (int)(total / 60) % 60);
            int sec = Mathf.Max(0, (int)total % 60);
            var warn = new Color(1f, 0.72f, 0.30f);
            DrawString(font, new Vector2(16f, 58f), $"{h:000}:{m:00}:{sec:00}", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(30), warn);

            DrawLine(new Vector2(16f, 74f), new Vector2(Size.X - 16f, 74f), Cyan with { A = 0.3f }, 1f);

            DrawString(font, new Vector2(16f, 98f), "봉쇄 코어 복구", HorizontalAlignment.Left,
                Size.X - 32f, ViewFont.S(13), Cyan with { A = 0.78f });

            // 0% → 목표 100%.
            var bar = new Rect2(16f, 108f, Size.X - 32f, 20f);
            DrawRect(bar, new Color(0f, 0f, 0f, 0.4f));
            // 목표선이 왼쪽에서 오른쪽으로 흐른다.
            float sweep = Mathf.PosMod(_t * 0.35f, 1f);
            DrawRect(new Rect2(bar.Position.X + bar.Size.X * sweep, bar.Position.Y, 2f, bar.Size.Y),
                new Color(0.55f, 1f, 0.75f, 0.55f));
            DrawRect(bar, Cyan with { A = 0.55f }, false, 1.2f);

            DrawString(font, new Vector2(16f, Size.Y - 14f), "현재 0%   ▶   목표 100%",
                HorizontalAlignment.Left, Size.X - 32f, ViewFont.S(16), new Color(0.80f, 1f, 0.88f));
        }
    }

    // --- 직원 아이콘 -------------------------------------------------------
    private partial class EmployeeIcon : Control
    {
        public Texture2D Tex;
        public Color Tint = Colors.White;

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            DrawRect(box, new Color(0.04f, 0.14f, 0.17f, 0.9f));
            if (Tex != null)
            {
                var src = Tex.GetSize();
                if (src.X > 0f && src.Y > 0f)
                {
                    float k = Mathf.Min(Size.X / src.X, Size.Y / src.Y);
                    var dst = src * k;
                    DrawTextureRect(Tex, new Rect2((Size - dst) * 0.5f, dst), false);
                }
            }
            else
            {
                DrawCircle(Size * 0.5f, Size.X * 0.3f, Tint);
            }
            DrawRect(box, Tint with { A = 0.85f }, false, 1.5f);
        }
    }
}
