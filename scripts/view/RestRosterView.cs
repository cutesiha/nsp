using System;
using System.Collections.Generic;
using Godot;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 추궁 표식 — **플레이어의 메모다.**
//
// 오늘 누구를 몇 번 흔들었고 몇 번 해명을 들었는지, 심문하다 보면 금방 뒤섞인다.
// 아이콘 옆에 그 횟수를 남겨 두는 것뿐이고, 그 이상은 아무 뜻이 없다.
//
// **어떤 판정도 이 값을 읽지 않는다** — 격리 판정도, 엔딩 판정도, 결번 AI 도.
// 읽는 순간 "흔들린 사람이 범인"이 되어, 플레이어가 자료를 맞춰 볼 이유가 사라진다.
// 자백을 받아 내는 게임이 아니라 기록을 맞춰 보는 게임이기 때문이다(§3-5).
//
// 다음 날로 넘어가면 지운다(ShiftFlowController 의 Rest → DayTransition).
public static class ConfrontMarks
{
    // evasive / deny — 말을 돌리거나 부인했다.
    private static readonly Dictionary<string, int> _shaken = new();
    // honest — 순순히 인정하거나 설명했다.
    private static readonly Dictionary<string, int> _explained = new();

    public static int Shaken(string employeeId) => _shaken.GetValueOrDefault(employeeId, 0);
    public static int Explained(string employeeId) => _explained.GetValueOrDefault(employeeId, 0);

    // neutral 은 아무 표식도 남기지 않는다 — 추궁이 성립하지 않아 되물은 것뿐이다.
    public static void Note(string employeeId, string variant)
    {
        if (string.IsNullOrEmpty(employeeId)) return;
        switch (variant)
        {
            case "evasive":
            case "deny":
                _shaken[employeeId] = Shaken(employeeId) + 1;
                break;
            case "honest":
                _explained[employeeId] = Explained(employeeId) + 1;
                break;
        }
    }

    public static void Clear()
    {
        _shaken.Clear();
        _explained.Clear();
    }
}

// 왼쪽 CRT — 휴게시간. 휴게실(BREAK ROOM)을 위에서 내려다본 2D 도식 화면이다.
// 직원은 동물 얼굴 아이콘(EmployeeDef.FacePortrait)으로 크게 표시되고, 클릭하면
// 화면 오른쪽의 인터뷰 자리로 걸어간다. 오른쪽 CRT(InterviewCCTVView)는 여기서 고른
// 직원을 3D 방 사이드뷰 위에 스탠딩으로 띄운다.
//
// 이 화면은 '2D 탑뷰' 전용이다 — 3D 렌더를 쓰지 않는다.
// 게임 판정(격리 / 다음 날 진행)은 기존과 동일하게 FacilitySimulation 에 그대로 위임한다.
public partial class RestRosterView : Control
{
    public static RestRosterView Instance { get; private set; }
    public event Action NextRequested;

    public string SelectedEmployeeId { get; private set; } = "";

    // 이전 저장/씬 연결과의 호환용이다. DAY1은 전화 후 질문 목록에서 Q5를 직접 선택하므로
    // 별도의 "의심 추궁" 스위치는 더 이상 사용하지 않는다.
    public bool InterrogateArmed => false;
    public void DisarmInterrogate() { }

    private static readonly Color Bg = new(0.035f, 0.045f, 0.05f);
    private static readonly Color Ink = new(0.75f, 0.82f, 0.9f);
    private static readonly Color Dim = new(0.5f, 0.56f, 0.62f);
    // 단말기 버튼 색 — 다른 모니터 화면(통화 · 패드)과 같은 청록, 격리만 붉은색.
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Red = new(1f, 0.46f, 0.42f);

    private Font _font;
    private BreakRoomTopView _map;
    private Label _selected;
    private Label _instruction;
    private Label _summary;
    private Tween _summaryFade;
    private Button _isolateBtn;
    private Button _nextBtn;

    public override void _Ready()
    {
        Instance = this;
        InterviewSession.Confronted += OnConfronted;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg));

        var title = MakeLabel("BREAK ROOM", 22, Ink);
        title.Position = new Vector2(18, 10);
        AddChild(title);

        var sub = MakeLabel("휴게실 상단 감시 / 인터뷰 대상 선택", 13, Dim);
        sub.Position = new Vector2(20, 36);
        AddChild(sub);

        // 제목 줄 오른쪽 — 방금 끝난 근무에서 작업실들이 실제로 만들어 낸 것.
        // 휴게에 들어온 직후 5초만 떠 있다가 조용히 사라진다.
        _summary = MakeLabel("", 15, new Color(0.62f, 0.9f, 0.82f));
        _summary.Position = new Vector2(300, 14);
        _summary.Size = new Vector2(484, 24);
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        // 길어도 칸 밖(모니터 오른쪽 끝)으로 삐져나가지 않는다 — 글자를 줄이고(ShowShiftSummary), 그래도 넘치면 자른다.
        _summary.ClipText = true;
        _summary.Visible = false;
        AddChild(_summary);

        var mapPanel = new Panel { Position = new Vector2(16, 58), Size = new Vector2(768, 400) };
        mapPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(mapPanel);

        _map = new BreakRoomTopView { Position = new Vector2(6, 6), Size = new Vector2(756, 388) };
        _map.EmployeeSelected += Select;
        mapPanel.AddChild(_map);

        var infoPanel = new Panel { Position = new Vector2(16, 470), Size = new Vector2(500, 116) };
        infoPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(infoPanel);
        _selected = MakeLabel("휴게실 안의 직원 아이콘을 선택하세요.", 14, Dim);
        _selected.Position = new Vector2(14, 13);
        _selected.Size = new Vector2(472, 30);
        _selected.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoPanel.AddChild(_selected);

        _instruction = MakeLabel("책상 위 전화기를 들어 심문을 시작하세요.", 17, Ink);
        _instruction.Position = new Vector2(14, 57);
        _instruction.Size = new Vector2(472, 30);
        _instruction.HorizontalAlignment = HorizontalAlignment.Center;
        infoPanel.AddChild(_instruction);

        // 두 버튼은 다른 모니터 UI 와 같은 단말기 버튼이다(MonitorUi) — 엔진 기본 버튼을
        // 쓰면 어두운 CRT 화면 위에 흰 덩어리 두 개가 떠 있다.
        // 격리는 되돌리기 어려운 조작이라 붉은색, 다음 날 진행은 다른 화면과 같은 청록색.
        _isolateBtn = MonitorUi.Button("격리", Red, _font, OnIsolatePressed, ViewFont.S(15));
        _isolateBtn.Position = new Vector2(532, 476);
        _isolateBtn.Size = new Vector2(244, 38);
        _isolateBtn.Disabled = true;
        AddChild(_isolateBtn);

        _nextBtn = MonitorUi.Button("다음 날 근무 배치 ▶", Cyan, _font,
            () => NextRequested?.Invoke(), ViewFont.S(16));
        _nextBtn.Position = new Vector2(532, 534);
        _nextBtn.Size = new Vector2(244, 44);
        AddChild(_nextBtn);

        // 심문 중에는 이 화면 위에 심문 콘솔(진술 · 조사 노트)이 덮인다. 통화를 끊으면 다시 휴게실.
        AddChild(new RestInterviewConsole());
    }

    public override void _ExitTree()
    {
        InterviewSession.Confronted -= OnConfronted;
        if (Instance == this) Instance = null;
    }

    // 추궁했다 — 그 직원 아이콘 옆 횟수를 하나 올린다. 그뿐이다(ConfrontMarks 주석 참조).
    private void OnConfronted(string employeeId, ConfrontKind kind, string variant)
    {
        if (kind == ConfrontKind.None) return;
        ConfrontMarks.Note(employeeId, variant);
        _map?.Refresh();
    }

    // DAY0 교육 중에는 다음 날로 넘어가지 못하게 잠근다(TutorialDirector 가 제어).
    // 휴게실 지도 위 그 직원의 동그란 아이콘(교육 화살표가 가리킨다). 아이콘 원은 위쪽 정사각형이다.
    public Control EmployeeIconOf(string employeeId) => _map?.IconOf(employeeId);
    // 그 아이콘의 얼굴 동그라미(아이콘 로컬 좌표). 뒷줄은 이름이 위에 붙어 동그라미가 아래로 비켜 있다.
    public Rect2 EmployeeIconCircleOf(string employeeId) => _map?.CircleRectOf(employeeId) ?? new Rect2();

    public void SetNextEnabled(bool enabled, string lockedText = "")
    {
        if (_nextBtn == null) return;
        _nextBtn.Disabled = !enabled;
        if (!enabled && !string.IsNullOrEmpty(lockedText)) _nextBtn.Text = lockedText;
    }

    public void Present(bool finalDay)
    {
        _nextBtn.Disabled = false;
        _nextBtn.Text = finalDay ? "최종 결과 확인 ▶" : "다음 날 근무 배치 ▶";
        SelectedEmployeeId = "";
        _selected.Text = "휴게실 안의 직원 아이콘을 선택하세요.";
        // 직원을 아직 안 골랐으면 격리는 누를 수 없다(버튼은 그대로 보인다).
        _isolateBtn.Disabled = true;
        _isolateBtn.Text = "격리";
        _map?.Populate(FacilitySimulation.Instance);
        _map?.SetSelected("");
        ShowShiftSummary();
    }

    // 오늘 방별 성과 한 줄. 근무 중에는 방 카드로만 보이던 숫자들을 한 번에 모아 보여
    // 준다 — "어느 방을 비워 뒀는지"가 여기서 결과로 드러난다.
    private void ShowShiftSummary()
    {
        if (_summary == null) return;
        var parts = new System.Collections.Generic.List<string>
        {
            $"코어 +{RoomEffectStats.CoreUpToday:0.#}%",
            $"자재 +{RoomEffectStats.MaterialsToday}",
            $"경비 기록 {RoomEffectStats.GuardRecordsToday}건",
        };
        // 스트레스가 잠긴 날에는 아예 움직이지 않는 수치라 적지 않는다.
        if (NSP.Core.DayFeatures.StressEnabled)
            parts.Add($"스트레스 최고 {RoomEffectStats.StressPeakToday:0}");

        _summaryFade?.Kill();
        string text = "오늘 방별 성과 ▶ " + string.Join("  /  ", parts);
        int fs = ViewFont.S(15);
        while (fs > ViewFont.S(10) && _font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X > _summary.Size.X) fs--;
        _summary.AddThemeFontSizeOverride("font_size", fs);
        _summary.Text = text;
        _summary.Visible = true;
        _summary.Modulate = new Color(1f, 1f, 1f, 1f);
        _summaryFade = CreateTween();
        _summaryFade.TweenInterval(4.2);
        _summaryFade.TweenProperty(_summary, "modulate:a", 0f, 0.8);
        _summaryFade.TweenCallback(Callable.From(() => _summary.Visible = false));
    }

    private void Select(string employeeId) => Select(employeeId, toggle: true);

    // toggle — 같은 아이콘을 다시 누르면 선택을 푸다(1013 QA 24번).
    // 격리 버튼처럼 "지금 고른 사람을 다시 그리기" 만 하려는 쪽은 toggle: false 로 부른다.
    private void Select(string employeeId, bool toggle)
    {
        // 심문 통화가 붙어 있는 동안에는 풀지 않는다 — 오른쪽 CRT 와 조사 노트가
        // 이 선택을 보고 그리므로, 통화 중에 비우면 화면이 통째로 비어 버린다.
        if (toggle && !string.IsNullOrEmpty(employeeId) && employeeId == SelectedEmployeeId
            && PhoneCallHud.Instance?.IsOpen != true)
        {
            Deselect();
            return;
        }
        SelectedEmployeeId = employeeId;
        var sim = FacilitySimulation.Instance;
        var def = sim?.GetEmployeeDef(employeeId);
        var st = sim?.GetEmployeeState(employeeId);
        if (def == null || st == null) return;

        if (!st.Alive)
        {
            _selected.Text = $"{def.Codename} · 응답 없음";
            _isolateBtn.Disabled = true;
        }
        else if (st.Incapacitated)
        {
            // 아직 깨어나지 않았다 — 심문할 수 없다. 묻고 싶으면 근무 중에 의무실을 돌려 놨어야 한다.
            _selected.Text = $"{def.Codename} · 기절 — 심문 불가";
            _isolateBtn.Disabled = false;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
        }
        else
        {
            string status = st.Isolated ? "격리됨" : "휴게 중";
            _selected.Text = $"{def.Codename} · {status}";
            _isolateBtn.Disabled = false;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
        }

        // 선택한 직원이 인터뷰 자리로 걸어가는 연출. 오른쪽 CRT 는 SelectedEmployeeId 만 보므로
        // 이 연출이 게임 판정에 영향을 주지 않는다.
        _map?.SetSelected(employeeId);
    }

    // 선택을 푸고 그 직원을 제자리로 돌려보낸다. SetSelected("") 가 아이콘을
    // 전부 자기 자리(HomeSeat)로 돌려놓는다.
    private void Deselect()
    {
        SelectedEmployeeId = "";
        _selected.Text = "휴게실 안의 직원 아이콘을 선택하세요.";
        _isolateBtn.Disabled = true;
        _isolateBtn.Text = "격리";
        _map?.SetSelected("");
        NSP.Core.Sfx.Instance?.Play("relay_click", -10f);
    }

    private void OnIsolatePressed()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(SelectedEmployeeId)) return;
        var st = sim.GetEmployeeState(SelectedEmployeeId);
        if (st == null || !st.Alive) return;
        if (st.Isolated) sim.CancelIsolation(SelectedEmployeeId);
        else sim.IsolateEmployee(SelectedEmployeeId);
        Select(SelectedEmployeeId, toggle: false);
        _map?.Refresh();
    }

    private StyleBoxFlat Panelbox() => new()
    {
        BgColor = new Color(0.05f, 0.06f, 0.08f),
        BorderColor = new Color(0.3f, 0.35f, 0.42f),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    private static ColorRect Rect(Color c)
    {
        var r = new ColorRect { Color = c, MouseFilter = MouseFilterEnum.Ignore };
        r.SetAnchorsPreset(LayoutPreset.FullRect);
        return r;
    }

    private Label MakeLabel(string text, int size, Color col)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // ────────────────────────────────────────────────────────────────
    //  휴게실 탑뷰 (2D)
    // ────────────────────────────────────────────────────────────────
    // 위에서 내려다본 방 한 칸. 사방 벽 두께가 보이고 그 안쪽이 바닥이다.
    // 오른쪽 벽에 인터뷰실로 통하는 문이 있고, 선택된 직원이 그 앞으로 걸어간다.
    private partial class BreakRoomTopView : Control
    {
        public event Action<string> EmployeeSelected;

        private const float WallThickness = 16f;
        private const float IconSize = 84f;
        private const float NameHeight = 18f;

        private readonly List<EmployeeIcon> _icons = new();
        private string _selectedId = "";

        // ── 오른쪽 CRT 의 3D 휴게실(room_rest.tscn)을 위에서 내려다본 축척 ──
        //
        // 두 화면이 "같은 방" 으로 읽혀야 한다. 그래서 가구 · 자리를 3D 좌표에서 그대로 옮긴다.
        //   가로 1m = 99px, 세로 1m = 58px. 3D 원점(테이블 가운데)이 Origin 이다.
        //   방 안쪽은 x 16~612(6m), 그 오른쪽 칸막이 너머가 인터뷰 대기칸이다.
        private static readonly Vector2 Origin = new(314f, 200f);
        private const float PxPerMeterX = 99f, PxPerMeterZ = 58f;
        private const float RoomRight = 612f;      // 방 오른쪽 벽(칸막이) 안쪽 면
        private const float PartitionWidth = 16f;
        private const float DoorHalf = 38f;        // 칸막이 문 폭의 절반
        private static Vector2 P(float x, float z) => Origin + new Vector2(x * PxPerMeterX, z * PxPerMeterZ);

        // 긴 테이블 3.2m × 1.15m(room_rest.tscn 의 TableTop).
        private static Rect2 TableRect => new(P(-1.6f, -0.575f), new Vector2(3.2f * PxPerMeterX, 1.15f * PxPerMeterZ));

        // 자리(아이콘 동그라미 중심). 3D 의 Seat1~6 과 같은 순서 — 0~2 = 뒷줄(위), 3~5 = 앞줄(아래), 왼쪽부터.
        // x 는 3D 의 -1.05 / 0 / 1.05m, y 는 테이블 위 · 아래 변에 붙여 앉힌다.
        private static Vector2 SeatCenter(int seat)
        {
            float x = P((seat % 3 - 1) * 1.05f, 0f).X;
            var t = TableRect;
            float y = seat < 3 ? t.Position.Y - 4f - IconSize * 0.5f : t.End.Y + 4f + IconSize * 0.5f;
            return new Vector2(x, y);
        }

        // 칸막이 문 너머 = 인터뷰 대기 위치.
        private Vector2 InterviewSpot => new((RoomRight + PartitionWidth + Size.X - WallThickness) * 0.5f, Origin.Y);

        public void Populate(FacilitySimulation sim)
        {
            foreach (var icon in _icons) icon.QueueFree();
            _icons.Clear();
            if (sim == null) return;

            // 이 지도는 휴게실을 위에서 본 그림이다 — 오른쪽 CRT 의 3D 휴게실과 자리가
            // 어긋나면 "같은 방"으로 읽히지 않는다. 그래서 3D 쪽이 관계도로 정한 자리를
            // 그대로 가져온다(자리 번호 0~2 = 뒷줄, 3~5 = 앞줄 · 왼쪽부터).
            // 3D 월드가 아직 없으면(단독 프리뷰) 예전처럼 명단 순서대로 앉힌다.
            var rest = FacilityCctvWorld.Instance?.RestRoom;
            rest?.EnsureSeating(sim);
            var taken = new System.Collections.Generic.HashSet<int>();
            int next = 0;
            foreach (string id in sim.GetActiveEmployeeIds())
            {
                var def = sim.GetEmployeeDef(id);
                if (def == null) continue;

                const int seatCount = 6;
                int seat = rest?.SeatOf(id) ?? -1;
                if (seat < 0 || seat >= seatCount || !taken.Add(seat))
                {
                    while (next < seatCount && taken.Contains(next)) next++;
                    if (next >= seatCount) break;
                    seat = next;
                    taken.Add(seat);
                }

                var icon = new EmployeeIcon
                {
                    EmployeeId = id,
                    Size = new Vector2(IconSize, IconSize + NameHeight),
                    // 뒷줄은 이름을 동그라미 위에 둔다 — 아래에 두면 이름이 테이블 위로 올라앉는다.
                    NameAbove = seat < 3,
                    HomeSeat = SeatCenter(seat),
                };
                icon.Position = icon.HomeSeat - icon.CircleCenter;
                icon.Clicked += id2 => EmployeeSelected?.Invoke(id2);
                AddChild(icon);
                _icons.Add(icon);
            }
            QueueRedraw();
        }

        public void Refresh()
        {
            foreach (var icon in _icons) icon.QueueRedraw();
        }

        public Control IconOf(string employeeId)
        {
            foreach (var icon in _icons)
                if (icon.EmployeeId == employeeId && IsInstanceValid(icon)) return icon;
            return null;
        }

        // 그 아이콘의 얼굴 동그라미 자리(아이콘 로컬). 이름은 뒷줄이면 위, 앞줄이면 아래에 붙는다.
        public Rect2 CircleRectOf(string employeeId)
        {
            foreach (var icon in _icons)
                if (icon.EmployeeId == employeeId && IsInstanceValid(icon))
                    return new Rect2(icon.CircleCenter - new Vector2(icon.Size.X, icon.Size.X) * 0.5f,
                        new Vector2(icon.Size.X, icon.Size.X));
            return new Rect2();
        }

        public void SetSelected(string employeeId)
        {
            _selectedId = employeeId;
            foreach (var icon in _icons)
            {
                bool sel = icon.EmployeeId == employeeId;
                icon.Selected = sel;
                icon.MoveTo(sel ? InterviewSpot : icon.HomeSeat);
                icon.QueueRedraw();
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            float w = Size.X, h = Size.Y;

            // 벽(바깥 테두리 두께) → 그 안쪽이 바닥. 평면 네모가 아니라 방 안처럼 보이게 한다.
            DrawRect(new Rect2(0, 0, w, h), new Color(0.115f, 0.125f, 0.145f));           // 벽 몸통
            DrawRect(new Rect2(0, 0, w, h), new Color(0.30f, 0.35f, 0.40f), false, 2f);   // 바깥선

            // 방 바닥(칸막이 왼쪽)과 인터뷰 대기칸(칸막이 오른쪽).
            var floor = new Rect2(WallThickness, WallThickness, RoomRight - WallThickness, h - WallThickness * 2f);
            var booth = new Rect2(RoomRight + PartitionWidth, WallThickness,
                w - WallThickness - (RoomRight + PartitionWidth), h - WallThickness * 2f);
            DrawRect(floor, new Color(0.055f, 0.075f, 0.082f));
            DrawRect(booth, new Color(0.045f, 0.060f, 0.068f));

            // 바닥 타일 격자 — 위에서 내려다본 느낌을 준다(방 안에만).
            var grid = new Color(1f, 1f, 1f, 0.030f);
            for (float x = floor.Position.X + 54f; x < floor.End.X; x += 54f)
                DrawLine(new Vector2(x, floor.Position.Y), new Vector2(x, floor.End.Y), grid, 1f);
            for (float y = floor.Position.Y + 54f; y < floor.End.Y; y += 54f)
                DrawLine(new Vector2(floor.Position.X, y), new Vector2(floor.End.X, y), grid, 1f);
            DrawRect(floor, new Color(0.22f, 0.28f, 0.31f), false, 1.5f);                  // 걸레받이

            var labelFont = ViewFont.Default;
            int labelSize = ViewFont.S(11);
            var steelEdge = new Color(0.40f, 0.42f, 0.46f);

            // ── 뒷벽(위) : 왼쪽 사물함 · 오른쪽 조리대 ──
            // 사물함 3D x -2.6 ~ -1.0m, 조리대 0.25 ~ 1.55m.
            var lockers = new Rect2(new Vector2(P(-2.6f, 0f).X, WallThickness), new Vector2(1.6f * PxPerMeterX, 34f));
            DrawRect(lockers, new Color(0.15f, 0.15f, 0.17f));
            for (int i = 1; i < 4; i++)
            {
                float x = lockers.Position.X + lockers.Size.X * i / 4f;
                DrawLine(new Vector2(x, lockers.Position.Y + 3f), new Vector2(x, lockers.End.Y - 3f), steelEdge with { A = 0.5f }, 1f);
            }
            DrawRect(lockers, steelEdge, false, 1.5f);
            DrawLabel("LOCKER", lockers, labelFont, labelSize, new Color(0.68f, 0.70f, 0.74f));

            var counter = new Rect2(new Vector2(P(0.25f, 0f).X, WallThickness), new Vector2(1.3f * PxPerMeterX, 38f));
            DrawRect(counter, new Color(0.20f, 0.17f, 0.13f));
            DrawRect(counter, new Color(0.40f, 0.33f, 0.24f), false, 1.5f);
            // 정수통(오른쪽 끝) — 동그라미 하나.
            DrawCircle(new Vector2(counter.End.X - 14f, counter.Position.Y + counter.Size.Y * 0.5f), 7f,
                new Color(0.40f, 0.55f, 0.62f));
            DrawLabel("COUNTER", new Rect2(counter.Position, counter.Size - new Vector2(26f, 0f)), labelFont, labelSize,
                new Color(0.72f, 0.62f, 0.48f));

            // ── 왼쪽 벽 : 공지판 · 시계 ──
            var notice = new Rect2(new Vector2(WallThickness, P(0f, 0.1f).Y), new Vector2(7f, 1.0f * PxPerMeterZ));
            DrawRect(notice, new Color(0.30f, 0.25f, 0.19f));
            for (int i = 0; i < 4; i++)
                DrawRect(new Rect2(notice.Position + new Vector2(1.5f, 6f + i * 13f), new Vector2(4f, 8f)),
                    new Color(0.70f, 0.68f, 0.62f));
            DrawCircle(new Vector2(WallThickness + 5f, P(0f, -1.2f).Y), 6f, new Color(0.50f, 0.52f, 0.54f));

            // ── 오른쪽 : 휴지통 · 간이 수납 ──
            DrawCircle(P(2.35f, -1.3f), 11f, new Color(0.30f, 0.31f, 0.32f));
            DrawArc(P(2.35f, -1.3f), 11f, 0f, Mathf.Tau, 24, steelEdge, 1.2f);
            var side = new Rect2(P(2.05f, 1.11f), new Vector2(0.5f * PxPerMeterX, 0.38f * PxPerMeterZ));
            DrawRect(side, new Color(0.20f, 0.17f, 0.13f));
            DrawRect(side, new Color(0.40f, 0.33f, 0.24f), false, 1.2f);

            // ── 가운데 긴 테이블(3.2m × 1.15m) + 위에 놓인 컵 · 종이(가운데 TABLE 글자를 피해 양 끝에) ──
            var table = TableRect;
            DrawRect(table, new Color(0.20f, 0.17f, 0.13f));
            DrawRect(table, new Color(0.34f, 0.28f, 0.19f), false, 2f);
            foreach (var (x, z, r) in new[] { (-1.15f, -0.2f, 4.5f), (0.95f, 0.22f, 3.5f), (1.3f, -0.25f, 4f) })
                DrawCircle(P(x, z), r, new Color(0.58f, 0.56f, 0.52f));
            DrawRect(new Rect2(P(-0.85f, 0.12f), new Vector2(0.2f * PxPerMeterX, 0.14f * PxPerMeterZ)),
                new Color(0.62f, 0.60f, 0.55f));
            DrawLabel("TABLE", table, labelFont, ViewFont.S(12), new Color(0.45f, 0.40f, 0.32f));

            // ── 칸막이 + 문 : 고른 직원이 이 문을 지나 인터뷰 대기칸으로 간다 ──
            bool open = !string.IsNullOrEmpty(_selectedId);
            var wallCol = new Color(0.115f, 0.125f, 0.145f);
            float doorTop = Origin.Y - DoorHalf, doorBottom = Origin.Y + DoorHalf;
            DrawRect(new Rect2(RoomRight, WallThickness, PartitionWidth, doorTop - WallThickness), wallCol);
            DrawRect(new Rect2(RoomRight, doorBottom, PartitionWidth, h - WallThickness - doorBottom), wallCol);
            var doorway = new Rect2(RoomRight, doorTop, PartitionWidth, doorBottom - doorTop);
            DrawRect(doorway, open ? new Color(0.16f, 0.34f, 0.30f) : new Color(0.08f, 0.10f, 0.12f));
            DrawRect(doorway, open ? new Color(0.45f, 0.95f, 0.80f) : new Color(0.34f, 0.40f, 0.44f), false, 1.5f);
            DrawRect(booth, new Color(0.22f, 0.28f, 0.31f), false, 1.5f);

            var boothInk = open ? new Color(0.55f, 1f, 0.88f) : new Color(0.45f, 0.52f, 0.56f);
            DrawLabel("INTERVIEW", new Rect2(booth.Position + new Vector2(0f, 8f), new Vector2(booth.Size.X, 20f)),
                labelFont, labelSize, boothInk);

            // 문 앞 대기 표시.
            DrawArc(InterviewSpot, IconSize * 0.5f + 4f, 0f, Mathf.Tau, 40,
                open ? new Color(0.45f, 0.95f, 0.80f, 0.55f) : new Color(0.35f, 0.42f, 0.46f, 0.30f), 1.5f);
        }

        // 그 칸 한가운데에 가로로 한 줄. 칸보다 길면 글자 크기를 줄여 칸 안에 넣는다 —
        // 예전에는 세로로 늘어놓은 글자가 칸 밖으로 튀어나왔다.
        private void DrawLabel(string text, Rect2 box, Font font, int size, Color col)
        {
            int fs = size;
            while (fs > 8 && font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X > box.Size.X - 8f) fs--;
            float ascent = font.GetAscent(fs), height = font.GetHeight(fs);
            var baseline = new Vector2(box.Position.X, box.Position.Y + (box.Size.Y - height) * 0.5f + ascent);
            DrawString(font, baseline, text, HorizontalAlignment.Center, box.Size.X, fs, col);
        }

        // 동물 얼굴 아이콘 하나. 기존 미니맵의 고유색(IconColor)을 테두리로 유지하면서,
        // EmployeeDef.FacePortrait(동물 얼굴 원화)를 크게 얹는다. 원화가 아직 없는 직원은
        // 고유색 원 + 코드네임 첫 글자로 대체한다(화면이 비지 않게).
        private partial class EmployeeIcon : Control
        {
            public event Action<string> Clicked;

            public string EmployeeId = "";
            // 자리 = 얼굴 동그라미의 중심(지도 좌표).
            public Vector2 HomeSeat;
            public bool Selected;
            // 이름을 동그라미 위에 쓴다(테이블 뒷줄). 앞줄은 아래.
            public bool NameAbove;

            // 아이콘 안에서 얼굴 동그라미 중심. 이름 줄만큼 위아래로 비켜 있다.
            public Vector2 CircleCenter => new(Size.X * 0.5f, (NameAbove ? Size.Y - Size.X : 0f) + Size.X * 0.5f);

            private bool _hover;
            private Tween _move;

            public override void _Ready()
            {
                MouseFilter = MouseFilterEnum.Stop;
                MouseEntered += () => { _hover = true; QueueRedraw(); };
                MouseExited += () => { _hover = false; QueueRedraw(); };
            }

            public void MoveTo(Vector2 center)
            {
                Vector2 target = center - CircleCenter;
                if (Position.DistanceTo(target) < 0.5f) return;
                _move?.Kill();
                _move = CreateTween();
                _move.TweenProperty(this, "position", target, 0.45)
                     .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            }

            public override void _GuiInput(InputEvent e)
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                var st = FacilitySimulation.Instance?.GetEmployeeState(EmployeeId);
                if (st is not { Alive: true }) return;   // 사망자는 인터뷰 대상이 아니다
                Clicked?.Invoke(EmployeeId);
                AcceptEvent();
            }

            public override void _Draw()
            {
                var sim = FacilitySimulation.Instance;
                var def = sim?.GetEmployeeDef(EmployeeId);
                var st = sim?.GetEmployeeState(EmployeeId);
                if (def == null) return;

                bool alive = st?.Alive ?? false;
                float d = Size.X;
                Vector2 c = CircleCenter;
                float r = d * 0.5f - 4f;
                // 이름 줄의 글자 기준선 · 상태 줄(응답 없음 등)의 기준선.
                float nameBase = NameAbove ? c.Y - d * 0.5f - 4f : d + 13f;
                float statusBase = c.Y + d * 0.5f + 1f;

                Color accent = alive ? def.IconColor : new Color(0.30f, 0.32f, 0.34f);

                // 바닥 그림자 — 탑뷰에서 아이콘이 바닥 위에 떠 있는 느낌.
                DrawCircle(c + new Vector2(0, 4f), r, new Color(0f, 0f, 0f, 0.35f));

                // 선택/호버 링.
                if (Selected)
                    DrawArc(c, r + 5f, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.92f), 3.5f);
                else if (_hover && alive)
                    DrawArc(c, r + 4f, 0f, Mathf.Tau, 48, accent.Lerp(Colors.White, 0.5f), 2f);

                // 얼굴 판.
                DrawCircle(c, r, new Color(0.09f, 0.10f, 0.12f));
                DrawArc(c, r, 0f, Mathf.Tau, 48, accent.Lerp(Colors.White, Selected ? 0.45f : 0.12f),
                    Selected ? 3f : 2f);

                var face = def.FacePortrait;
                if (face != null)
                {
                    float s = r * 1.72f;
                    var box = new Rect2(c - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s));
                    DrawTextureRect(face, box, false,
                        alive ? Colors.White : new Color(0.45f, 0.45f, 0.48f));
                }
                else
                {
                    // 얼굴 원화가 없는 직원의 대체 표시.
                    DrawCircle(c, r * 0.72f, new Color(accent.R, accent.G, accent.B, alive ? 0.55f : 0.25f));
                    string initial = string.IsNullOrEmpty(def.Codename) ? "?" : def.Codename.Substring(0, 1);
                    DrawString(ViewFont.Default, c + new Vector2(-r, 10f), initial,
                        HorizontalAlignment.Center, r * 2f, ViewFont.S(30),
                        alive ? Colors.White : new Color(0.6f, 0.6f, 0.6f));
                }

                // 코드네임 + 상태.
                var nameCol = alive ? new Color(0.95f, 0.95f, 0.86f) : new Color(0.55f, 0.55f, 0.58f);
                DrawString(ViewFont.Default, new Vector2(-14f, nameBase), def.Codename,
                    HorizontalAlignment.Center, d + 28f, ViewFont.S(17), nameCol);

                if (st is { Alive: false })
                    DrawString(ViewFont.Default, new Vector2(-14f, statusBase), "응답 없음",
                        HorizontalAlignment.Center, d + 28f, ViewFont.S(11), new Color(0.9f, 0.35f, 0.35f));
                else if (st is { Isolated: true })
                    DrawString(ViewFont.Default, new Vector2(-14f, statusBase), "[격리]",
                        HorizontalAlignment.Center, d + 28f, ViewFont.S(11), new Color(0.88f, 0.52f, 0.9f));
                else if (st is { Incapacitated: true })
                    DrawString(ViewFont.Default, new Vector2(-14f, statusBase), "[기절]",
                        HorizontalAlignment.Center, d + 28f, ViewFont.S(11), new Color(0.95f, 0.62f, 0.3f));

                // "진술 흔들림 ×n" · "해명 ×n" 배지는 그리지 않는다(F-5).
                //
                // 화면이 세어 주는 순간 그 숫자가 곧 범인 지목표가 됐다 — 관리자가 기록을 읽고
                // 판단하는 대신 점이 많은 사람을 골랐다. 세는 것(ConfrontMarks)은 그대로 두되
                // 결론은 사람이 내린다.
            }
        }
    }
}
