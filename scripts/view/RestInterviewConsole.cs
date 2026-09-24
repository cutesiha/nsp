using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Dialogue;

namespace NSP.View;

// 휴게시간 MONITOR 01 — 심문 중에 휴게실(직원 선택) 화면 위에 덮이는 **조사 노트**.
//
//   직원 선택(RestRosterView)  →  [전화기를 든다]  →  조사 노트
//
// 세 면의 역할이 갈라져 있다(docs/NSP_INTERVIEW_REWORK.md §3-6).
//   MON02   직원 얼굴 · 입 모양 · 긴장 발광          (InterviewCCTVView)
//   자막 띠 대화 전부 — 대사 · 질문 · 선택지          (PhoneCallHud)
//   MON01   조사 노트만 — 이 화면                    (여기)
//
// 예전에는 이 화면 한 장에 진술 블록 · 꼬리질문 · 자료 목록 · 슬롯 · 버튼 넷이 다 들어가
// 기울어진 CRT 에서 읽을 양이 아니었다. 대화는 전부 자막 띠로 옮겼고 여기에는 자료만 남는다.
//
// 이 콘솔은 자리(컨테이너)와 표시만 갖는다. 내용물(카드 · 슬롯)은 PhoneCallHud 가
// InterviewSession 을 들고 채운다 — 추리 규칙은 여기 없다.
//
// **통화를 끊는 버튼은 여기 없다.** 세션을 끝내는 길은 대화창 선택지의 「통화를 종료한다.」
// 와 3D 전화기 클릭 둘뿐이다. 이 화면이 대화창에 보내는 신호는 "펼쳐 달라" 하나다.
public partial class RestInterviewConsole : Control
{
    public static RestInterviewConsole Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Bg = new(0.03f, 0.045f, 0.055f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.5f, 0.6f, 0.66f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);

    // 대화창을 접어 둔 채로 이 화면을 보다가 다시 펼치고 싶을 때. 통화를 끊지 않는다.
    public event Action ExpandRequested;
    // 자료 A/B 가 바뀌었다 — 대화창의 선택지가 달라지므로 접혀 있으면 펼쳐야 한다.
    public event Action SlotsChanged;
    // 띠에 꽂힌 핀(또는 사고 세로선)을 눌렀다. 받는 쪽(PhoneCallHud)은 목록에서 같은
    // 카드를 누른 것과 **똑같이** 처리한다 — 띠는 카드를 가리키는 또 하나의 손가락일 뿐,
    // 따로 고르는 방법이 아니다.
    public event Action<string> EvidencePinPressed;

    // PhoneCallHud 가 채우는 자리.
    public HFlowContainer NoteTabs { get; private set; }
    public VBoxContainer EvidenceList { get; private set; }
    public HBoxContainer SlotRow { get; private set; }

    private Font _font;
    private Label _name;
    private Label _goal;
    private Label _guardRecords;
    private Label _detail;
    private Panel _listFrame;
    private StaffTimelineView _band;
    // 사고 세로선을 눌렀을 때 어느 카드를 누른 셈으로 칠지 찾기 위해 들고 있는 자료판.
    private IReadOnlyList<InterviewEvidence> _board = new List<InterviewEvidence>();
    private List<DisplayLogEntry> _rows = new();
    private Button _expandBtn;
    private ScrollContainer _listScroll;
    // 카드가 새로 들어온 순간 목록 테두리가 한 번 밝아진다(§3-6 "말한 것이 자료가 된다").
    private float _flash;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        Position = Vector2.Zero;
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;   // 아래 휴게실 화면으로 클릭이 새지 않게
        Visible = false;
        SetProcess(true);

        var bg = new ColorRect { Color = Bg, MouseFilter = MouseFilterEnum.Ignore, Size = Canvas };
        AddChild(bg);

        var title = Lbl("조사 노트", 22, Cyan);
        title.Position = new Vector2(18, 10);
        AddChild(title);

        _name = Lbl("", 22, Colors.White);
        _name.Position = new Vector2(150, 10);
        _name.Size = new Vector2(300, 30);
        AddChild(_name);

        _expandBtn = MonitorUi.Button("대화창 펼치기", Cyan, _font, () => ExpandRequested?.Invoke(), ViewFont.S(15));
        _expandBtn.Position = new Vector2(624, 12);
        _expandBtn.Size = new Vector2(160, 38);
        AddChild(_expandBtn);

        // ── 조사 목표 — 오늘 무엇을 밝혀야 하는가 한 줄 ────────────────
        // 로그에 이미 공개된 사실만 쓴다(범인 이름은 어디에도 없다).
        _goal = Lbl("", 14, Amber);
        _goal.Position = new Vector2(20, 46);
        _goal.Size = new Vector2(764, 22);
        AddChild(_goal);

        // 같은 줄 오른쪽 끝 — 오늘 경비실이 남긴 재석 기록 수.
        // 이 숫자가 곧 "대조할 수 있는 자료가 얼마나 있는가" 다.
        _guardRecords = Lbl("", 13, Dim);
        _guardRecords.Position = new Vector2(20, 47);
        _guardRecords.Size = new Vector2(764, 22);
        _guardRecords.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_guardRecords);

        BuildNotes();
    }

    // 띠 높이 · 그 아래 첫 줄. 조사 목표(46~68) 바로 아래에서 시작한다.
    private const float BandY = 72f;
    private const float BandH = 130f;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 조사 노트 — 탭 · 카드 목록 · 상세칸 · 자료 A/B.
    // [이 자료로 질문] [두 자료 비교] 버튼은 없다. 그건 자막 띠의 선택지가 맡는다.
    private void BuildNotes()
    {
        // ── 띠 시간표 — 카드 목록이 "언제 · 어디"로 읽히게 하는 줄 ──────
        // 심문 중인 직원의 자료 카드가 그 시각 위에 핀으로 꽂힌다. 핀을 누르면
        // 목록에서 같은 카드를 누른 것과 같다 — 고르는 길이 둘로 갈라지지 않는다.
        _band = new StaffTimelineView
        {
            Position = new Vector2(16, BandY), Size = new Vector2(768, BandH),
        };
        _band.PinPressed = id => EvidencePinPressed?.Invoke(id);
        _band.IncidentPressed = OnIncidentLinePressed;
        _band.SegmentPressed = OnSegmentPressed;
        AddChild(_band);

        NoteTabs = new HFlowContainer
        {
            Position = new Vector2(16, BandY + BandH + 6), Size = new Vector2(768, 34),
        };
        NoteTabs.AddThemeConstantOverride("h_separation", 6);
        AddChild(NoteTabs);

        // 카드 목록은 그대로 남기고 스크롤 높이만 줄인다(띠가 목록을 대신하지 않는다).
        _listFrame = FramePanel(new Vector2(16, BandY + BandH + 44), new Vector2(768, 170));
        AddChild(_listFrame);
        _listScroll = new ScrollContainer
        {
            Position = new Vector2(24, BandY + BandH + 50), Size = new Vector2(752, 158),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(_listScroll);
        EvidenceList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EvidenceList.AddThemeConstantOverride("separation", 5);
        _listScroll.AddChild(EvidenceList);

        // ── 상세칸 — 카드 한 줄로는 다 못 싣는 전문을 여기 펼친다 ──────
        var detailFrame = FramePanel(new Vector2(16, 424), new Vector2(768, 78));
        AddChild(detailFrame);
        _detail = Lbl("자료를 고르면 전문이 여기에 표시됩니다.", 13, Dim);
        _detail.Position = new Vector2(28, 432);
        _detail.Size = new Vector2(744, 62);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_detail);

        SlotRow = new HBoxContainer { Position = new Vector2(16, 510), Size = new Vector2(768, 80) };
        SlotRow.AddThemeConstantOverride("separation", 8);
        AddChild(SlotRow);
    }

    // 심문 시작 — 휴게실 화면 위를 덮는다.
    public void Open(string codename, Color accent)
    {
        _name.Text = codename;
        _name.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.25f));
        _listScroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
        SetDetail("");
        Visible = true;
    }

    public void Close() => Visible = false;

    // 오늘 무엇을 밝혀야 하는가. PhoneCallHud 가 시설 로그 화면에 뜬 사건에서 만들어 넘긴다.
    public void SetGoal(string text)
    {
        _goal.Text = string.IsNullOrEmpty(text) ? "" : "조사 목표: " + text;

        // 경비실이 오늘 무엇을 남겼는가. 없으면 왜 없는지까지 적는다 —
        // 자료가 비는 이유가 "경비실을 비웠기 때문"이라는 것을 여기서 알게 한다.
        int n = NSP.Facility.RoomEffectStats.GuardRecordsToday;
        _guardRecords.Text = n > 0 ? $"경비 기록 {n}건" : "경비 기록 없음 — 오늘 경비실이 비어 있었습니다";
        _guardRecords.AddThemeColorOverride("font_color", n > 0 ? Dim : new Color(1f, 0.46f, 0.40f));
    }

    public void SetDetail(string text)
    {
        bool has = !string.IsNullOrEmpty(text);
        _detail.Text = has ? text : "자료를 고르면 전문이 여기에 표시됩니다.";
        _detail.AddThemeColorOverride("font_color", has ? new Color(0.86f, 0.92f, 0.94f) : Dim);
    }

    // 띠에 그릴 것을 받는다. PhoneCallHud 가 자료판을 다시 만들 때마다 부른다.
    //
    // rows 는 시설 로그 **화면**의 줄(FacilityLogFormatter 결과)이다. EventLog 원본을
    // 넘기면 플레이어가 보지 못한 이동이 띠에 서고, 그 순간 이 화면은 추리를 대신 해 준다.
    //
    // 핀이 되는 자료는 네 가지 — CCTV 로 직접 본 장면 · 동료의 증언 · 본인의 진술 ·
    // 근무 전 기분이다. 시각이 없는 기분은 띠 왼쪽 끝에 선다. 시설 로그에서 온 카드
    // (이동 · 사고)는 핀이 되지 않는다 — 그건 이미 띠와 세로선 그 자체다.
    public void SetTimeline(IEnumerable<string> employeeIds, List<DisplayLogEntry> rows,
        IReadOnlyList<InterviewEvidence> board, string subjectId, IEnumerable<string> selectedIds)
    {
        if (_band == null) return;
        _rows = rows ?? new List<DisplayLogEntry>();
        _board = board ?? new List<InterviewEvidence>();
        var pins = _board.Where(e => e.SubjectEmployeeId == subjectId
            && e.Kind is EvidenceKind.Cctv or EvidenceKind.Testimony
                       or EvidenceKind.OwnStatement or EvidenceKind.Mood);
        _band.SetData(employeeIds, _rows, pins);
        _band.SetSelected(selectedIds);
    }

    // 띠의 구간을 눌렀다 = 그 시각 그 직원의 자료 카드를 누른 것으로 친다.
    //
    // 구간 하나는 곧 "이 직원이 이 시각에 이 방으로 옮겼다" 는 이동 기록이고,
    // 조사 노트에는 그 기록이 카드로 들어와 있다. 띠에서 눈으로 찾은 것을 목록에서
    // 다시 찾게 하지 않는다.
    private void OnSegmentPressed(string employeeId, float time)
    {
        InterviewEvidence best = null;
        float bestGap = float.MaxValue;
        foreach (var e in _board)
        {
            if (e.SubjectEmployeeId != employeeId || !e.HasTime) continue;
            float gap = Mathf.Abs(e.AnchorTime - time);
            // 같은 시각이면 이동 기록을 먼저 고른다 — 구간을 만든 것이 그 줄이다.
            if (gap > bestGap || (Mathf.IsEqualApprox(gap, bestGap) && e.Kind != EvidenceKind.Movement)) continue;
            bestGap = gap;
            best = e;
        }
        // 근무 6시간을 120초로 환산하므로 2초는 대략 6분이다 — 그보다 멀면 그 구간의 자료가 아니다.
        if (best != null && bestGap <= 2f) EvidencePinPressed?.Invoke(best.Id);
    }

    // 사고 세로선을 눌렀다 = 그 사고 카드를 누른 것으로 친다.
    // 같은 시각의 사고 카드를 자료판에서 찾아 그 id 를 그대로 올려보낸다.
    private void OnIncidentLinePressed(int logIndex)
    {
        if (logIndex < 0 || logIndex >= _rows.Count) return;
        float at = _rows[logIndex].Timestamp;
        var card = _board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident
            && e.HasTime && Mathf.Abs(e.AnchorTime - at) < 0.5f);
        if (card != null) EvidencePinPressed?.Invoke(card.Id);
    }

    // 캡처/검증 씬 전용 — 버튼을 실제로 누르지 않고 같은 신호만 보낸다.
    public void EmitExpandForTest() => ExpandRequested?.Invoke();

    // 자료 A/B 가 바뀌었다고 알린다(PhoneCallHud 가 슬롯을 다시 그린 뒤 부른다).
    public void NotifySlotsChanged() => SlotsChanged?.Invoke();

    // 새 자료가 들어왔다 — 목록 테두리를 한 번 밝히고 짧은 소리를 낸다.
    public void FlashNotes()
    {
        _flash = 1f;
        NSP.Core.Sfx.Instance?.Play("relay_click", -14f);
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0f || _listFrame == null) return;
        _flash = Mathf.Max(0f, _flash - (float)delta * 2.2f);
        var box = _listFrame.GetThemeStylebox("panel") as StyleBoxFlat;
        if (box != null) box.BorderColor = Cyan with { A = 0.3f + 0.7f * _flash };
    }

    private Panel FramePanel(Vector2 pos, Vector2 size)
    {
        var p = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.07f, 0.09f, 0.9f),
            BorderColor = Cyan with { A = 0.3f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        return p;
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }
}
