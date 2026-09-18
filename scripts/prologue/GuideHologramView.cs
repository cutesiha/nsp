using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Prologue;

// 오른쪽 CRT 전용 — 관리자 권한 승계 콘솔과 GUIDE-0 안내창.
// GUIDE-0 는 단순 텍스트 출력이 아니라 콘솔 화면 위에 떠오르는 '홀로그램 창'이다.
// 창 위쪽의 정사각형 칸이 GUIDE-0 의 얼굴 자리이며, 지금은 임시 초상(placeholder)이 뜬다.
//
// ★ 얼굴 교체:  res://assets/ui/guide0/guide0_<표정키>.png  파일을 넣기만 하면 된다.
//   표정키는 데이터 파일(NSP_PROLOGUE_RUNTIME.md)의 portrait: 값 — 현재 normal / smile.
//   파일이 없으면 자동으로 "GUIDE-0 PORTRAIT" 임시 박스가 대신 그려진다.
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
        foreach (var opt in menu.Options)
        {
            var captured = opt;
            bool answered = _answeredOptions.Contains(captured.GuideId);
            // 이미 확인한 질문은 체크 표시 + 어두운 색으로 눌러 두고 다시 고를 수 없게 한다.
            var accent = answered ? new Color(0.30f, 0.44f, 0.48f) : Cyan;
            string label = (answered ? "✓  " : "") + captured.Label;
            var b = MonitorUi.Button(label, accent, _font, () =>
            {
                if (answered) return;
                ClearChoices();
                Sfx.Instance?.Play("click", -10f);
                _answeredOptions.Add(captured.GuideId);
                var cb = _menuPick;
                _menuPick = null;
                cb?.Invoke(captured);
            }, 16);
            b.Disabled = answered;
            b.CustomMinimumSize = new Vector2(0f, 34f);
            _choices.AddChild(b);
        }
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
                case PrologueScript.GuideBeatKind.Noise:
                    _noiseUntil = Time.GetTicksMsec() / 1000.0 + 0.7;
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
        // 타이핑 속도는 프롤로그 자막과 같은 값(PrologueTextStyle)을 쓴다.
        _lineDuration = PrologueTextStyle.TypeSeconds(text);
        _hint.Visible = true;
    }

    private void SetPortrait(string key)
    {
        _portrait.Expression = string.IsNullOrEmpty(key) ? "normal" : key;
        _portrait.Texture = LoadPortrait(_portrait.Expression);
        _portrait.QueueRedraw();
    }

    private static Texture2D LoadPortrait(string expression)
    {
        // 최종 도트 초상화를 넣으면 자동으로 임시 박스를 대체한다.
        foreach (string ext in new[] { ".png", ".webp", ".jpg" })
        {
            string path = $"{PortraitDir}guide0_{expression}{ext}";
            if (ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        }
        return null;
    }

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

        // 대사는 절대 저절로 넘어가지 않는다 — 스페이스/엔터/클릭을 기다린다.
        if (_guide != null && _line.VisibleRatio < 1f)
        {
            _lineElapsed += delta;
            _line.VisibleRatio = PrologueTextStyle.Ratio(_line.Text, _lineElapsed);
            SpeakRevealed();
            if (_line.VisibleRatio >= 1f) OnLineTypedOut();
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
        if (string.IsNullOrEmpty(voice)) return;
        int shown = Mathf.RoundToInt(_line.VisibleRatio * _line.Text.Length);
        while (_spokenChars < shown)
        {
            char c = _line.Text[_spokenChars];
            _spokenChars++;
            Sfx.Instance?.PlayVoiceBlip(voice, c);
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
        _console.AddThemeFontSizeOverride("normal_font_size", 17);
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
        _portrait = new PortraitBox
        {
            Position = new Vector2(312f, 74f),
            Size = new Vector2(176f, 176f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _holoRoot.AddChild(_portrait);

        _name = MakeLabel("GUIDE-0", 18, Cyan, new Vector2(0f, 258f));
        _name.Size = new Vector2(Canvas.X, 24f);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_name);

        _line = MakeLabel("", 20, new Color(0.88f, 0.98f, 1f), new Vector2(140f, 292f));
        _line.Size = new Vector2(520f, 76f);
        _line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _line.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_line);

        _icons = new HBoxContainer
        {
            Position = new Vector2(140f, 372f),
            Size = new Vector2(520f, 56f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _icons.AddThemeConstantOverride("separation", 10);
        _holoRoot.AddChild(_icons);

        _choices = new VBoxContainer
        {
            Position = new Vector2(160f, 392f),
            Size = new Vector2(480f, 150f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        _choices.AddThemeConstantOverride("separation", 8);
        _holoRoot.AddChild(_choices);

        _hint = MakeLabel("클릭하여 계속", 13, new Color(0.45f, 0.62f, 0.66f), new Vector2(0f, 528f));
        _hint.Size = new Vector2(Canvas.X, 20f);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _holoRoot.AddChild(_hint);
    }

    private Label MakeLabel(string text, int size, Color col, Vector2 pos)
    {
        var l = new Label { Text = text, Position = pos, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
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
                    DrawTextureRect(Texture, new Rect2(box.Position + (Size - dst) * 0.5f, dst), false);
                }
            }
            else
            {
                // 임시 초상 — 최종 도트 얼굴이 들어오면 이 블록은 그려지지 않는다.
                var font = ViewFont.Default;
                DrawString(font, new Vector2(0f, Size.Y * 0.44f), "GUIDE-0",
                    HorizontalAlignment.Center, Size.X, 22, Cyan with { A = 0.9f });
                DrawString(font, new Vector2(0f, Size.Y * 0.58f), "PORTRAIT",
                    HorizontalAlignment.Center, Size.X, 15, Cyan with { A = 0.55f });
                DrawString(font, new Vector2(0f, Size.Y * 0.74f), $"[{Expression}]",
                    HorizontalAlignment.Center, Size.X, 13, Cyan with { A = 0.35f });
            }

            DrawRect(box, Cyan with { A = 0.9f }, false, 2f);
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
