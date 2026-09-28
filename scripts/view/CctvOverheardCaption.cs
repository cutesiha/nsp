using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// CCTV 화면 아래쪽에 뜨는 "엿들은 대화" 자막(관계 시스템 Phase 2).
//
// CCTVMonitorView 가 매 프레임 Tick 으로 지금 보는 방과 피드 상태를 넘긴다. 그 방에 두 사람 이상이
// 근무 중이면 잠시 뒤 OverheardDialogue 가 고른 대화 한 번(A → B)을 한 글자씩 찍어 보여 주고,
// 쉬었다가 다음 대화를 고른다. 방을 바꾸면 처음부터 다시 기다린다. 신호가 없으면(전력 · 고장 · 차단)
// 들리지 않는다. 대사 · 간격은 전부 data/relationships/overheard_lines.tres.
//
// 화면에 뜬 대화는 "관리자가 들은 것"으로 PlayerKnownEvidence 에 남는다(휴게시간 조사 자료).
// 자막을 누르거나 단축키(F — ClueHud 가 받는다)를 누르면 그 대화를 관리자 패드의 단서로 찍는다.
// 자막이 사라진 뒤에도 잠깐(PinGraceSeconds)은 방금 들은 대화를 찍을 수 있다.
public partial class CctvOverheardCaption : Control
{
    // CCTV 3D 직원이 "지금 누가 대화 중인지"를 읽어 talk 애니메이션을 고를 때만 쓴다.
    // 자막 표시 로직 자체는 바뀌지 않는다.
    public static CctvOverheardCaption Instance { get; private set; }

    private enum Phase { Waiting, SpeakA, SpeakB }

    private static readonly Color Ink = new(0.84f, 0.92f, 0.88f);
    private static readonly Color Dim = new(0.46f, 0.62f, 0.58f);
    // 심문 조사 노트의 ★ 와 같은 색 — 찍힌 것은 호박색, 찍을 수 있는 것은 옅은 청록.
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private const float PinGraceSeconds = 3f;

    private RichTextLabel _text;
    private Label _mic;
    private Label _star;
    private ColorRect _bar;
    private bool _hover;

    // 방금 들은 대화(관리자가 들은 기록). 혼잣말이나 아직 아무것도 못 들었으면 null.
    private PlayerKnownEvidence.OverheardRecord _heard;
    private string _heardId = "";
    private double _pinGraceUntil = -1;

    private string _roomId = "";
    private Phase _phase = Phase.Waiting;
    private float _timer;
    private float _shown;
    private string _plain = "";
    private readonly RandomNumberGenerator _rng = new();

    // 검증용 — 지금 보여 주는 대화.
    public OverheardExchange Current { get; private set; }
    public string CurrentSpeaker => _phase switch
    {
        Phase.SpeakA => Current?.A ?? "",
        Phase.SpeakB => Current?.B ?? "",
        _ => "",
    };
    public string CurrentLine => _phase == Phase.Waiting ? "" : _plain;
    public int ExchangesPlayed { get; private set; }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        Instance = this;
        // 자막 띠를 누르면 그 대화를 단서로 찍는다(보이지 않는 동안에는 입력을 받지 않는다).
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => _hover = true;
        MouseExited += () => _hover = false;
        var font = ViewFont.Default;

        _bar = new ColorRect { Color = new Color(0.01f, 0.03f, 0.03f, 0.72f), MouseFilter = MouseFilterEnum.Ignore };
        _bar.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_bar);

        _mic = new Label { Text = "◉ AUDIO", MouseFilter = MouseFilterEnum.Ignore };
        _mic.AddThemeFontOverride("font", font);
        _mic.AddThemeFontSizeOverride("font_size", ViewFont.S(11));
        _mic.AddThemeColorOverride("font_color", Dim);
        _mic.Position = new Vector2(10f, 4f);
        AddChild(_mic);

        _text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = false, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _text.AddThemeFontOverride("normal_font", font);
        _text.AddThemeFontSizeOverride("normal_font_size", ViewFont.S(16));
        _text.AddThemeColorOverride("default_color", Ink);
        _text.AddThemeColorOverride("font_outline_color", Colors.Black);
        _text.AddThemeConstantOverride("outline_size", 3);
        _text.SetAnchorsPreset(LayoutPreset.FullRect);
        _text.OffsetLeft = 78f; _text.OffsetRight = -78f; _text.OffsetTop = 8f;
        AddChild(_text);

        // 오른쪽 끝의 ☆ — 심문 조사 노트의 ★ 와 같은 모양이다.
        _star = new Label
        {
            Text = "☆", MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _star.AddThemeFontOverride("font", font);
        _star.AddThemeFontSizeOverride("font_size", ViewFont.S(20));
        _star.SetAnchorsPreset(LayoutPreset.RightWide);
        _star.OffsetLeft = -64f; _star.OffsetRight = -14f;
        AddChild(_star);

        Visible = false;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        TogglePin();
        AcceptEvent();
    }

    public override void _Process(double delta) => PaintStar();

    // 지금(또는 방금) 들은 대화를 찍을 수 있는가.
    public bool CanPin => _heard != null
        && (Visible || Time.GetTicksMsec() / 1000.0 < _pinGraceUntil);

    // 지금 들은 대화의 조사 자료(찍을 수 없으면 null). 조사 노트와 같은 창구로 만든다.
    public InterviewEvidence PinnableEvidence => CanPin ? InterviewEvidenceBoard.FromOverheard(_heard) : null;

    // 들은 대화를 단서로 찍는다(이미 찍혀 있으면 푼다). 찍을 수 없으면 false.
    public bool TogglePin()
    {
        var ev = PinnableEvidence;
        if (ev == null) return false;
        ClueBoard.Toggle(ev);
        PaintStar();
        return true;
    }

    private void PaintStar()
    {
        if (_star == null || !Visible) return;
        bool pinnable = _heard != null;
        bool pinned = pinnable && ClueBoard.IsPinned(_heard.Day, _heardId);
        _star.Visible = pinnable;
        _star.Text = pinned ? "★" : "☆";
        _star.AddThemeColorOverride("font_color",
            pinned || _hover ? Amber : Cyan with { A = 0.5f });
    }

    // audible = 지금 이 방의 영상 · 소리가 실제로 들어오는가(신호 없음 · 차단 · 근무 외 시간이면 false).
    public void Tick(float delta, string roomId, bool audible)
    {
        var table = OverheardDialogue.Table;
        roomId ??= "";
        if (roomId != _roomId)
        {
            _roomId = roomId;
            Reset(table?.FirstDelaySeconds ?? 2.5f);
        }
        if (!audible || table == null || string.IsNullOrEmpty(roomId))
        {
            // 끊긴 동안에는 말하던 줄을 버리고, 다시 들리면 첫 대기부터.
            if (_phase != Phase.Waiting || Visible) Reset(table?.FirstDelaySeconds ?? 2.5f);
            return;
        }

        _timer -= delta;
        switch (_phase)
        {
            case Phase.Waiting:
                if (_timer > 0f) return;
                // 이 방에 무너진 직원이 있으면 대화 대신 그 사람의 혼잣말이 계속 들린다.
                // 옆에 누가 있어도 마찬가지다 — 아무도 대답하지 않는다.
                string broken = PanickedHere(roomId);
                if (!string.IsNullOrEmpty(broken))
                {
                    Current = null;
                    _heard = null;   // 혼잣말은 대화가 아니다 — 단서로 찍지 않는다
                    Speak(Phase.SpeakA, broken,
                        FacilitySimulation.PanicMutter(broken), table);
                    _soloMutter = true;
                    return;
                }
                _soloMutter = false;
                if (!OverheardDialogue.TryPick(roomId, out var ex))
                {
                    _timer = 1.5f;   // 아직 두 사람이 모이지 않았다 — 잠시 뒤 다시 본다
                    return;
                }
                Current = ex;
                ExchangesPlayed++;
                Speak(Phase.SpeakA, ex.A, ex.LineA, table);
                Heard(roomId, GameState.Instance?.DayTimeSeconds ?? 0f);
                break;

            case Phase.SpeakA:
            case Phase.SpeakB:
                _shown += delta * Mathf.Max(1f, table.CharsPerSecond);
                _text.VisibleCharacters = Mathf.Min(_text.GetTotalCharacterCount(), NamePrefixLength + (int)_shown);
                if (_timer > 0f) return;
                // 혼잣말은 상대가 없다 — 짧게 쉬었다 같은 말을 다시 한다.
                if (_soloMutter) { Reset(_rng.RandfRange(2.2f, 4.0f)); break; }
                // 두 사람 중 한 명이라도 자리를 뜨면(재배치 · 기절 · 사망) 대화는 거기서 끊긴다.
                var here = FacilitySimulation.Instance?.OnDutyEmployeeIds(roomId);
                bool stillBoth = here != null && here.Contains(Current.A) && here.Contains(Current.B);
                if (_phase == Phase.SpeakA && stillBoth)
                {
                    Speak(Phase.SpeakB, Current.B, Current.LineB, table);
                    Heard(roomId, _heard?.Time ?? 0f);
                }
                else Reset(_rng.RandfRange(table.CooldownMinSeconds, Mathf.Max(table.CooldownMinSeconds, table.CooldownMaxSeconds)));
                break;
        }
    }

    // 지금 나오는 것이 두 사람의 대화가 아니라 한 사람의 혼잣말인가.
    private bool _soloMutter;

    // 이 방에서 무너진 사람(여럿이면 첫 사람).
    private static string PanickedHere(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return "";
        foreach (string id in sim.OnDutyEmployeeIds(roomId))
            if (sim.IsPanicked(id)) return id;
        return "";
    }

    private int NamePrefixLength { get; set; }

    private void Speak(Phase phase, string speaker, string line, OverheardLineTableDef table)
    {
        _phase = phase;
        _plain = line;
        var def = FacilitySimulation.Instance?.GetEmployeeDef(speaker);
        string name = def?.Codename ?? speaker;
        string col = (def?.IconColor ?? Ink).Lightened(0.25f).ToHtml(false);
        _text.Text = $"[color=#{col}]{name}[/color]  {Escape(line)}";
        NamePrefixLength = name.Length + 2;
        _shown = 0f;
        _text.VisibleCharacters = NamePrefixLength;
        // 글자가 다 찍히는 시간 + 머무는 시간.
        _timer = line.Length / Mathf.Max(1f, table.CharsPerSecond) + table.LineHoldSeconds;
        Visible = true;
    }

    // 화면에 뜬 줄을 "관리자가 들은 것"으로 남긴다. A 의 줄이 뜰 때 한 번, B 의 줄이 뜰 때 한 번.
    // 둘째 줄이 들어오면 이미 찍어 둔 단서의 사본도 둘째 줄까지 채운다.
    private void Heard(string roomId, float startedAt)
    {
        if (Current == null) return;
        bool second = _phase == Phase.SpeakB;
        _heard = PlayerKnownEvidence.RecordOverheard(roomId, Current.A, Current.B,
            Current.LineA, second ? Current.LineB : "", startedAt);
        var ev = InterviewEvidenceBoard.FromOverheard(_heard);
        _heardId = ev?.Id ?? "";
        if (second) ClueBoard.Refresh(ev);
    }

    private void Reset(float wait)
    {
        // 자막이 사라진 직후에도 잠깐은 방금 들은 대화를 찍을 수 있다.
        if (_heard != null && _phase != Phase.Waiting)
            _pinGraceUntil = Time.GetTicksMsec() / 1000.0 + PinGraceSeconds;
        _phase = Phase.Waiting;
        _timer = wait;
        _plain = "";
        Visible = false;
        if (_text != null) _text.Text = "";
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
