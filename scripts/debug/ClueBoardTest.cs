using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 관리자 패드 단서(ClueBoard) · 근무 중 핀 검사.
//
//   godot --headless --path . res://scenes/debug/ClueBoardTest.tscn
//
// 보는 것:
//   ① 근무 중 로그 줄에서 찍은 자료가 휴게시간 조사 노트의 **같은 자료**(같은 Id)다.
//   ② 자료 Id 가 목록 순번이 아니라 내용으로 정해진다 — 기록이 늘거나 잘려도 흔들리지 않는다.
//   ③ 저장소는 하나다 — 심문 ★ 가 곧 ClueBoard 다.
//   ④ 단서는 날짜를 넘어 남는다(동결 사본) · (Day, Id) 로 갈린다 · 새 게임 / 교육일 종료에 비워진다.
//   ⑤ 엿들은 대화가 조사 자료가 되고, 자막 · 단축키로 찍힌다.
//   ⑥ 로그 창 ☆ 를 누르면 찍힌다 · 찍는 순간 토스트가 뜬다.
//   ⑦ 관리자 패드 — Tab 으로 꺼내 든 동안에도 시간은 흐른다(PauseWhileHeld 옵션) · 기록 창은 든 채로 열린다 · 단서 / 직원 / 지침 화면 · CCTV 스냅샷.
public partial class ClueBoardTest : Node
{
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(Start));
    }

    private async void Start()
    {
        GD.Print("################ 관리자 패드 단서 검사 ################");
        TestLogRowSameAsBoard();
        TestIdStability();
        TestClueBoardBasics();
        TestInterviewStarIsClueBoard();
        TestAcrossDays();
        TestResets();
        TestOverheardEvidence();
        await TestCaptionPin();
        await TestLogWindowStar();
        await TestToast();
        await TestPad();
        await TestPadView();
        TestSnapshot();
        TestFinalReport();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── ① 로그 줄 → 자료 : 조사 노트와 같은 Id ─────────────────────────
    private void TestLogRowSameAsBoard()
    {
        Head("A", "로그 줄에서 만든 자료 = 휴게시간 조사 노트의 자료");
        Reset();
        Deploy();
        Move("cat", Storage, Power, At(30));
        Incident(LogEventType.TaskFailed, Power, At(40));
        Log(LogEventType.Argument, "wolf", Maintenance, At(50));   // 자료가 되지 않는 줄

        var rows = Rows();
        var fromRows = rows.Select(InterviewEvidenceBoard.FromLogRow).Where(e => e != null).ToList();
        var move = fromRows.FirstOrDefault(e => e.Kind == EvidenceKind.Movement);
        var incident = fromRows.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        Check(move != null && move.SubjectEmployeeId == "cat", "이동 줄 → 고양이의 이동 자료");
        Check(incident != null && incident.SubjectRoomId == Power, "사고 줄 → 발전실 사고 자료");
        Check(fromRows.Count == 2, $"자료가 되는 줄은 이동 · 사고 둘뿐이다(배치 · 다툼 줄은 아니다) — {fromRows.Count}장");

        var board = InterviewEvidenceBoard.Build("cat");
        Check(move != null && board.Any(e => e.Id == move.Id), $"이동 자료 Id 가 조사 노트와 같다 ({move?.Id})");
        Check(incident != null && board.Any(e => e.Id == incident.Id), $"사고 자료 Id 가 조사 노트와 같다 ({incident?.Id})");
        Check(fromRows.All(e => e.Day == 1), "자료에 오늘 날짜가 붙는다");
    }

    // ── ② Id 는 내용으로 — 기록이 늘거나 잘려도 같다 ───────────────────
    private void TestIdStability()
    {
        Head("B", "자료 Id 안정성 — 로그가 늘어도 · 시청 기록이 잘려도");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(40));
        Move("cat", Storage, Power, At(45));
        var before = InterviewEvidenceBoard.Build("cat").Select(e => e.Id).ToList();

        // 앞쪽에 줄이 끼어드는 경우까지 — 근무 시작 전 배치 한 줄이 새로 생긴다.
        Log(LogEventType.TaskStart, "sheep", Power, 0.5f);
        Incident(LogEventType.PowerOutage, Core, At(60));
        Move("cat", Power, Storage, At(70));
        var after = InterviewEvidenceBoard.Build("cat").Select(e => e.Id).ToList();
        Check(before.All(after.Contains), $"먼저 있던 자료 {before.Count}장의 Id 가 그대로다");

        // CCTV 시청 기록은 200건 상한에서 앞부터 지워진다 — 남은 기록의 Id 는 그대로여야 한다.
        DialogueClaimState.ResetAll();
        for (int i = 0; i < 190; i++) Watch(i);
        var cctvBefore = InterviewEvidenceBoard.Build("cat").Where(e => e.Kind == EvidenceKind.Cctv)
            .ToDictionary(e => e.AnchorTime, e => e.Id);
        for (int i = 190; i < 215; i++) Watch(i);
        var cctvAfter = InterviewEvidenceBoard.Build("cat").Where(e => e.Kind == EvidenceKind.Cctv).ToList();
        int survived = 0, same = 0;
        foreach (var e in cctvAfter)
        {
            if (!cctvBefore.TryGetValue(e.AnchorTime, out var id)) continue;
            survived++;
            if (id == e.Id) same++;
        }
        Check(PlayerKnownEvidence.CctvObservationCount == 200, $"시청 기록은 상한 200건에서 잘렸다 ({PlayerKnownEvidence.CctvObservationCount})");
        Check(survived > 100 && same == survived, $"잘리고 남은 시청 기록 {survived}장의 Id 가 그대로다 ({same}/{survived})");
        Check(cctvAfter.Select(e => e.Id).Distinct().Count() == cctvAfter.Count, "시청 자료 Id 가 서로 겹치지 않는다");

        void Watch(int i) => PlayerKnownEvidence.RecordCctvObservation(
            i % 2 == 0 ? Storage : Guard, 10f + i * 0.5f,
            (i / 2) % 2 == 0 ? new[] { "cat" } : new[] { "cat", "dog" });
    }

    // ── ClueBoard 자체 ────────────────────────────────────────────────
    private void TestClueBoardBasics()
    {
        Head("C", "찍기 · 풀기 · 중복 · 알림 · 뱃지");
        Reset();
        Deploy();
        Move("cat", Storage, Power, At(30));
        Incident(LogEventType.TaskFailed, Power, At(40));
        var ev = Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e?.Kind == EvidenceKind.Incident);
        var mv = Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e?.Kind == EvidenceKind.Movement);

        int pins = 0, unpins = 0;
        void OnChanged(ClueBoard.Entry _, bool on) { if (on) pins++; else unpins++; }
        ClueBoard.Changed += OnChanged;

        Check(ClueBoard.Count == 0 && ClueBoard.UnseenCount == 0, "새 게임은 비어 있다");
        Check(ClueBoard.Pin(ev) && ClueBoard.IsPinned(ev), "찍었다");
        Check(!ClueBoard.Pin(ev) && ClueBoard.Count == 1, "같은 자료를 두 번 찍어도 한 장이다");
        Check(ClueBoard.Toggle(mv) && ClueBoard.Count == 2, "Toggle 로 두 번째를 찍었다");
        Check(ClueBoard.UnseenCount == 2, $"패드 뱃지 +2 ({ClueBoard.UnseenCount})");
        Check(ClueBoard.CountFor("cat") == 1, "고양이가 등장하는 단서 1장(이동)");
        ClueBoard.MarkSeen();
        Check(ClueBoard.UnseenCount == 0, "패드를 열면 뱃지가 비워진다");
        Check(!ClueBoard.Toggle(mv) && !ClueBoard.IsPinned(mv) && ClueBoard.Count == 1, "Toggle 로 풀었다");
        Check(pins == 2 && unpins == 1, $"알림: 찍기 {pins}번 · 풀기 {unpins}번");

        // 사본이다 — 원본을 고쳐도 단서는 그대로.
        string body = ev.Body;
        ev.Body = "변조";
        ev.RelatedEmployeeIds.Add("fox");
        var kept = ClueBoard.Find(ev.Day, ev.Id).Evidence;
        Check(kept.Body == body && !kept.RelatedEmployeeIds.Contains("fox"), "찍은 순간의 사본을 들고 있다(원본과 분리)");

        ClueBoard.Changed -= OnChanged;
    }

    // ── ③ 심문 ★ = ClueBoard ───────────────────────────────────────────
    private void TestInterviewStarIsClueBoard()
    {
        Head("D", "저장소는 하나 — 근무 중 로그 ☆ 와 심문 ★ 가 같은 단서다");
        Reset();
        Deploy();
        Move("cat", Storage, Power, At(30));
        Incident(LogEventType.TaskFailed, Power, At(40));

        // 근무 중 로그 창에서 이동 줄을 찍었다.
        var mv = Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e?.Kind == EvidenceKind.Movement);
        ClueBoard.Pin(mv);

        var session = new InterviewSession("cat");
        Check(session.IsStarred(mv.Id), "근무 중에 찍은 줄이 심문 조사 노트에서 ★ 로 보인다");

        // 심문에서 사고 카드에 ★ 를 찍었다.
        var inc = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        session.ToggleStar(inc.Id);
        Check(ClueBoard.IsPinned(1, inc.Id), "심문 ★ 가 ClueBoard 에 들어간다");
        session.Filter = EvidenceFilter.Starred;
        var starred = session.Visible().Select(e => e.Id).ToList();
        Check(starred.Count == 2 && starred.Contains(mv.Id) && starred.Contains(inc.Id), $"★ 보기에 두 장 ({starred.Count})");
        session.ToggleStar(inc.Id);
        Check(!ClueBoard.IsPinned(1, inc.Id), "심문에서 ★ 를 풀면 패드에서도 빠진다");
    }

    // ── ④ 날짜를 넘어 ─────────────────────────────────────────────────
    private void TestAcrossDays()
    {
        Head("E", "근무가 바뀌어도 단서는 남는다 · (Day, Id) 로 갈린다");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(40));
        var day1 = Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e?.Kind == EvidenceKind.Incident);
        ClueBoard.Pin(day1);

        // 다음 날 — 원천 자료는 전부 비워진다.
        GameState.Instance.GoToNextDay();
        EventLog.Instance.ClearAll();
        DialogueClaimState.ResetAll();
        Check(GameState.Instance.CurrentDay == 2, "DAY2");
        Check(InterviewEvidenceBoard.BuildAll("cat").All(e => e.Id != day1.Id), "DAY1 사고는 오늘 조사 노트에 없다(원천이 비워졌다)");
        var kept = ClueBoard.Find(1, day1.Id);
        Check(kept != null && kept.Evidence.Body == day1.Body && kept.Evidence.TimeText == day1.TimeText,
            "DAY1 단서는 패드에 그대로 남아 있다(동결 사본)");

        // 같은 시각 · 같은 방 · 같은 종류의 사고가 DAY2 에도 났다 → Id 는 같지만 다른 단서다.
        Deploy(2);
        Incident(LogEventType.TaskFailed, Power, At(40), 2);
        var day2 = Rows(2).Select(InterviewEvidenceBoard.FromLogRow).First(e => e?.Kind == EvidenceKind.Incident);
        Check(day2.Id == day1.Id && day2.Day == 2, "DAY2 사고의 Id 는 DAY1 과 같은 문자열이다");
        Check(!ClueBoard.IsPinned(day2), "그래도 DAY2 사고는 찍힌 것이 아니다");
        ClueBoard.Pin(day2);
        Check(ClueBoard.Count == 2 && ClueBoard.IsPinned(1, day1.Id) && ClueBoard.IsPinned(2, day2.Id), "두 날의 단서가 따로 남는다");
        Check(new InterviewSession("cat").IsStarred(day2.Id), "DAY2 심문에서는 DAY2 단서만 ★ 로 본다");
    }

    private void TestResets()
    {
        Head("F", "비워지는 때 — 새 게임 · 교육일(DAY0) 종료");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(40));
        ClueBoard.Pin(Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e != null));
        GameState.Instance.GoToNextDay();
        Check(ClueBoard.Count == 1, "DAY1 → DAY2 에는 남는다");
        GameState.Instance.ResetRun(1);
        Check(ClueBoard.Count == 0, "새 게임(ResetRun)에 비워진다");

        GameState.Instance.ResetRun(0);
        EventLog.Instance.ClearAll();
        Deploy(0);
        Incident(LogEventType.TaskFailed, Power, At(40), 0);
        ClueBoard.Pin(Rows(0).Select(InterviewEvidenceBoard.FromLogRow).First(e => e != null));
        Check(ClueBoard.Count == 1, "교육일(DAY0)에도 찍을 수 있다");
        GameState.Instance.GoToNextDay();
        Check(GameState.Instance.CurrentDay == 1 && ClueBoard.Count == 0, "교육이 끝나고 DAY1 이 되면 비워진다");
    }

    // ── ⑤ 엿들은 대화 → 조사 자료 ─────────────────────────────────────
    private void TestOverheardEvidence()
    {
        Head("G", "엿들은 대화 — 두 사람 모두의 조사 자료 · 질문 · 둘째 줄 갱신");
        Reset();
        var rec = PlayerKnownEvidence.RecordOverheard(Guard, "fox", "wolf", "오늘 좀 이상하지 않아?", "", At(20));
        var fox = InterviewEvidenceBoard.Build("fox").FirstOrDefault(e => e.Kind == EvidenceKind.Overheard);
        var wolf = InterviewEvidenceBoard.Build("wolf").FirstOrDefault(e => e.Kind == EvidenceKind.Overheard);
        Check(fox != null && wolf != null && fox.Id == wolf.Id, $"여우 · 늑대 조사 노트에 같은 Id 로 들어간다 ({fox?.Id})");
        Check(fox?.SubjectEmployeeId == "fox" && wolf?.SubjectEmployeeId == "wolf", "각자 자기 자료로 들어간다(누구에게든 물을 수 있다)");
        Check(fox?.RelatedEmployeeIds.Contains("wolf") == true, "함께 있던 사람으로 상대가 올라간다");
        Check(fox?.SubjectRoomId == Guard && fox.HasTime && fox.CanAnchorPosition, "그 방 · 그 시각의 재석 근거다");
        Check(InterviewEvidenceBoard.Build("cat").All(e => e.Kind != EvidenceKind.Overheard), "대화에 없던 고양이의 노트에는 없다");
        Check(InterviewEvidenceBoard.BuildAll("fox").Count(e => e.Kind == EvidenceKind.Overheard) == 1, "전원 보기에서는 한 장이다");
        var qs = fox == null ? new List<InterviewQuestion>() : InterviewQuestionFactory.For("fox", fox);
        Check(qs.Count > 0 && qs.All(q => !string.IsNullOrEmpty(q.Text)), $"이 자료로 여우에게 물을 수 있다 — {qs.FirstOrDefault()?.Text}");
        Check(fox == null || InterviewQuestionFactory.For("cat", fox).Count == 0, "다른 직원(고양이)에게는 물을 수 없다");
        Check(InterviewEvidenceDisplay.Tag(fox) == "대화" && fox?.Tag == "대화", "태그는 「대화」");

        // A 의 줄만 들었을 때 찍고, B 의 줄이 들리면 단서 사본도 채워진다.
        ClueBoard.Pin(InterviewEvidenceBoard.FromOverheard(rec));
        PlayerKnownEvidence.RecordOverheard(Guard, "fox", "wolf", "", "글쎄, 난 모르겠는데.", At(20));
        ClueBoard.Refresh(InterviewEvidenceBoard.FromOverheard(rec));
        var entry = ClueBoard.Find(1, fox?.Id);
        Check(entry != null && entry.Evidence.Body.Contains("글쎄"), $"둘째 줄까지 단서에 채워졌다 — {entry?.Evidence.Body}");
        Check(PlayerKnownEvidence.OverheardCount == 1, "같은 대화는 한 건이다(줄이 들어올 때마다 늘지 않는다)");

        ClueBoard.Refresh(InterviewEvidenceBoard.FromOverheard(
            PlayerKnownEvidence.RecordOverheard(Core, "cat", "dog", "안녕", "", At(30))));
        Check(ClueBoard.Count == 1, "찍지 않은 대화는 Refresh 로 찍히지 않는다");

        GameState.Instance.GoToNextDay();
        Check(InterviewEvidenceBoard.Build("fox").All(e => e.Kind != EvidenceKind.Overheard), "다음 날 조사 노트에는 어제 대화가 없다");
        Check(ClueBoard.IsPinned(1, fox?.Id), "찍어 둔 대화는 패드에 남는다");
    }

    // ── ⑤ 자막 · 단축키로 찍기 ────────────────────────────────────────
    private async System.Threading.Tasks.Task TestCaptionPin()
    {
        Head("H", "CCTV 대화 자막 — 뜬 줄이 기록되고 · 누르면 찍힌다 · 끝난 뒤 잠깐은 찍을 수 있다");
        LiveSetup(new Dictionary<string, string> { ["fox"] = Guard, ["wolf"] = Guard, ["cat"] = Core });
        var cap = new CctvOverheardCaption { Size = new Vector2(736f, 50f) };
        AddChild(cap);
        await Frames(1);
        var t = OverheardDialogue.Table;

        Check(!cap.CanPin && !cap.TogglePin(), "아무것도 들리지 않았으면 찍을 수 없다");
        Advance(cap, Guard, t.FirstDelaySeconds + 0.2f);
        Check(cap.Visible && cap.Current != null, $"A 가 말한다 — {cap.CurrentLine}");
        Check(PlayerKnownEvidence.OverheardCount == 1, "첫 줄이 뜨자 관리자가 들은 대화로 기록된다");
        Check(cap.CanPin && cap.PinnableEvidence?.Kind == EvidenceKind.Overheard, "지금 들리는 대화를 찍을 수 있다");

        // 자막을 누른다(실제 입력 경로).
        cap._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        var id = cap.PinnableEvidence?.Id;
        Check(ClueBoard.IsPinned(1, id), "자막을 누르면 단서로 찍힌다");
        string a = cap.Current.A, lineB = cap.Current.LineB;

        Advance(cap, Guard, cap.CurrentLine.Length / t.CharsPerSecond + t.LineHoldSeconds + 0.1f);
        Check(cap.Visible && cap.CurrentSpeaker != a, "B 가 받는다");
        Check(ClueBoard.Find(1, id)?.Evidence.Body.Contains(lineB) == true, "찍어 둔 단서에 B 의 줄까지 채워졌다");

        Advance(cap, Guard, cap.CurrentLine.Length / t.CharsPerSecond + t.LineHoldSeconds + 0.1f);
        Check(!cap.Visible && cap.CanPin, "자막이 사라진 직후에도 잠깐은 찍을 수 있다(단축키용)");
        Check(cap.TogglePin() && !ClueBoard.IsPinned(1, id), "다시 누르면 풀린다");
        await Frames(1);
        var until = Time.GetTicksMsec() + 3300;
        while (Time.GetTicksMsec() < until) await Frames(1);
        Check(!cap.CanPin, "유예 시간이 지나면 찍을 수 없다");

        // 단축키(F) — 근무 중에만.
        var hud = new ClueHud();
        AddChild(hud);
        await Frames(1);
        for (float s = 0f; s < t.CooldownMaxSeconds + 10f && !cap.Visible; s += 0.05f) Advance(cap, Guard, 0.05f);
        Check(cap.Visible && cap.CanPin, "다음 대화가 들린다");
        hud._Input(new InputEventKey { Keycode = Key.F, Pressed = true });
        Check(ClueBoard.IsPinned(1, cap.PinnableEvidence?.Id), "F 키로 찍힌다");
        hud.QueueFree();
        cap.QueueFree();
        await Frames(1);
    }

    // ── ⑥ 로그 창 ☆ ─────────────────────────────────────────────────
    private async System.Threading.Tasks.Task TestLogWindowStar()
    {
        Head("I", "시설 로그 창 — 이동 · 사고 줄에만 ☆ · 누르면 찍힌다");
        Reset();
        GameState.Instance.SetPhase(GamePhase.Live);
        Deploy();
        Move("cat", Storage, Power, At(30));
        Incident(LogEventType.TaskFailed, Power, At(40));
        Log(LogEventType.Argument, "wolf", Maintenance, At(50));

        var overlay = new Day1HistoryOverlay();
        AddChild(overlay);
        await Frames(1);
        overlay.OpenLog();
        await Frames(2);
        var stars = Buttons(overlay).Where(b => b.TooltipText.StartsWith("단서로 기록")).ToList();
        Check(stars.Count == 2, $"☆ 는 이동 · 사고 두 줄에만 달린다 ({stars.Count}개 / 로그 {Rows().Count}줄)");
        Check(stars.All(s => s.Modulate.A < 0.01f), "평소에는 숨어 있다(마우스를 올린 줄만 보인다)");
        if (stars.Count > 0)
        {
            stars[0].EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Check(ClueBoard.Count == 1, "☆ 를 누르면 단서로 찍힌다");
            Check(stars[0].Text == "★" && stars[0].Modulate.A > 0.99f, "찍힌 줄은 늘 ★ 로 보인다");
            var session = new InterviewSession("cat");
            Check(session.Board.Any(e => session.IsStarred(e.Id)), "휴게시간 조사 노트에서도 ★ 다");
        }
        overlay.QueueFree();
        await Frames(1);
    }

    private async System.Threading.Tasks.Task TestToast()
    {
        Head("J", "찍는 순간 — 토스트 · 뱃지");
        Reset();
        var hud = new ClueHud();
        AddChild(hud);
        await Frames(1);
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(40));
        ClueBoard.Pin(Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e != null));
        await Frames(2);
        var labels = Labels(hud).Where(l => l.IsVisibleInTree()).Select(l => l.Text).ToList();
        Check(labels.Any(s => s.Contains("단서 기록됨")), "「단서 기록됨」 토스트가 뜬다");
        Check(labels.Any(s => s.Contains("패드 +1")), "패드 뱃지 +1 이 함께 뜬다");
        hud.QueueFree();
        await Frames(1);
    }

    // ── 3D 패드 — 꺼내기 · 일시정지 · 내려놓기 ─────────────────────────
    private async System.Threading.Tasks.Task TestPad()
    {
        Head("K", "관리자 패드 — Tab 으로 꺼내고 · 든 동안에도 시간은 흐른다 · 기록 창은 열린다 · 내려놓기");
        Reset();
        GameState.Instance.SetPhase(GamePhase.Live);
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(40));
        ClueBoard.Pin(Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e != null));

        var pad = new AdminPad3D();
        AddChild(pad);
        var hud = new ClueHud();
        AddChild(hud);
        await Frames(1);
        Check(!pad.IsOpen && !AdminPad3D.PausesGame, "처음에는 책상 위에 놓여 있다");

        hud._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        Check(pad.IsOpen && !AdminPad3D.PausesGame, "Tab — 꺼내도 근무 시간은 그대로 흐른다(PauseWhileHeld 기본 끔)");
        pad.PauseWhileHeld = true;
        Check(AdminPad3D.PausesGame, "PauseWhileHeld 를 켜면 든 동안 멈춘다(옵션)");
        pad.PauseWhileHeld = false;
        Check(ClueBoard.UnseenCount == 1, "홈 화면에서는 새 단서 뱃지(+1)가 남아 있다");
        await Until(() => pad.IsHeld, 3000);
        Check(pad.IsHeld, "들어 올려 손에 쥐었다");
        Check(pad.HoldFlatToCamera && Mathf.IsZeroApprox(pad.HoldRollDeg), "일자 파지 — 화면이 카메라와 평행하고 갸웃 기울기가 없다");

        // 든 채로도 로그 · 대화 기록 · 업무 창은 열린다(게임 밖 UI). 창이 떠 있는 동안 우클릭은 창을 닫는 데 쓰인다.
        var overlay = new Day1HistoryOverlay();
        AddChild(overlay);
        await Frames(1);
        overlay._Input(new InputEventKey { Keycode = Key.L, Pressed = true });
        Check(overlay.IsLogOpen && pad.IsHeld, "든 채로 L — 시설 로그 창이 열리고 패드는 그대로 손에 있다");
        pad._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Check(overlay.IsLogOpen && pad.IsHeld && pad.View.Current == PadView.Tab.Home, "창이 떠 있는 동안 우클릭은 패드를 뒤로 보내지 않는다");
        overlay._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Check(!overlay.IsWindowOpen && pad.IsHeld, "우클릭은 창을 닫고, 패드는 여전히 손에 있다");
        overlay._Input(new InputEventKey { Keycode = Key.D, Pressed = true });
        Check(overlay.IsDialogueOpen, "든 채로 D — 대화 기록 창");
        overlay._Input(new InputEventKey { Keycode = Key.T, Pressed = true });
        Check(overlay.IsObjectivesOpen, "든 채로 T — 오늘의 업무 창");
        overlay.CloseWindow();
        var logBtn = overlay.FindChild("LogHistoryButton", true, false) as Control;
        Check(logBtn != null && overlay.IsOverIcons(logBtn.GetGlobalRect().GetCenter())
              && !overlay.IsOverIcons(Vector2.Zero), "오른쪽 아래 버튼 위의 클릭은 패드 화면으로 넘기지 않는다");
        overlay.QueueFree();
        await Frames(1);
        Check(pad.View.Current == PadView.Tab.Home, "열면 언제나 홈 화면부터");
        pad.View.OpenApp(PadView.Tab.Clues, fade: false);
        Check(pad.View.Current == PadView.Tab.Clues && pad.View.VisibleClues().Count == 1, "단서 앱을 열면 단서 1장이 보인다");
        Check(ClueBoard.UnseenCount == 0, "단서 앱을 열면 새 단서 뱃지가 비워진다");
        Check(pad.TargetViewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Always, "든 동안 화면을 그린다");

        // 우클릭 = 한 단계 뒤로. 상세 → 목록 → 홈, 홈에서 한 번 더 누르면 내려놓는다.
        pad.View.OpenApp(PadView.Tab.Staff, fade: false);
        pad.View.OpenStaffDetail("cat");
        Check(pad.View.Current == PadView.Tab.Staff && pad.View.StaffDetailId == "cat", "직원 상세 화면을 열었다");
        pad._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Check(pad.View.Current == PadView.Tab.Staff && pad.View.StaffDetailId == "", "우클릭 ① 직원 상세 → 직원 목록");
        pad._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Check(pad.View.Current == PadView.Tab.Home && pad.IsHeld, "우클릭 ② 직원 목록 → 홈(아직 들고 있다)");
        pad._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Check(pad.IsOpen && !pad.IsHeld, "우클릭 ③ 홈 → 내려놓기");
        await Until(() => !pad.IsOpen, 3000);
        Check(!pad.IsOpen && !AdminPad3D.PausesGame, "책상에 내려놓았다");
        Check(!pad.View.IsLocked && pad.View.Current == PadView.Tab.Home,
            "근무 중 거치대 위 화면은 잠금 화면이 아니라 홈(지침 · 단서 · 직원)이다");
        Check(pad.TargetViewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Always,
            "근무 중에는 거치대 위에서도 화면을 계속 그린다(시각 · 배지 · 예고)");

        // Tab 은 어느 화면에서든 곧바로 내려놓는다(뒤로 가지 않는다).
        hud._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        await Until(() => pad.IsHeld, 3000);
        pad.View.OpenApp(PadView.Tab.Clues, fade: false);
        hud._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        Check(!pad.IsHeld && pad.IsOpen, "Tab — 앱 화면에서도 한 단계 뒤로가 아니라 곧바로 내려놓는다");
        await Until(() => !pad.IsOpen, 3000);
        Check(pad.View.Current == PadView.Tab.Home && !pad.View.IsLocked, "앱 화면에서 내려놓아도 거치대 위 화면은 홈이다");

        // 거치대 위 홈의 앱 아이콘 — 누른 자리(논리 좌표)의 앱을 연 채로 집어 든다.
        Check(pad.View.AppAt(new Vector2(560, 345)) == PadView.Tab.Clues
              && pad.View.AppAt(new Vector2(282, 345)) == PadView.Tab.Manual
              && pad.View.AppAt(new Vector2(20, 20)) == null, "거치대 위 홈 — 아이콘 자리를 가려낸다");
        pad.Open(PadView.Tab.Staff);
        await Until(() => pad.IsHeld, 3000);
        Check(pad.IsHeld && pad.View.Current == PadView.Tab.Staff, "아이콘을 누르면 그 앱이 열린 채로 들어 올린다");
        pad.Close();
        await Until(() => !pad.IsOpen, 3000);

        // 근무 배치 — DAY1 만 예외다. 교육이 끝나고 처음 앉는 날이라 방금 배운 지침을 다시 펴 볼 수 있다.
        GameState.Instance.SetPhase(GamePhase.Schedule);
        await Frames(1);
        Check(GameState.Instance.CurrentDay == 1, "지금은 DAY1");
        Check(pad.CanOpen(), "DAY1 근무 배치에서는 꺼낼 수 있다");
        Check(!pad.View.IsLocked, "DAY1 근무 배치에는 거치대 위 화면이 홈이다");

        GameState.Instance.GoToNextDay();
        await Frames(1);
        Check(!pad.CanOpen(), "DAY2 부터는 근무 배치에서 꺼낼 수 없다");
        Check(pad.View.IsLocked, "DAY2 근무 배치에는 잠금 화면으로 바뀐다");

        GameState.Instance.SetPhase(GamePhase.Rest);
        await Frames(1);
        Check(pad.CanOpen(), "휴게시간에는 꺼낼 수 있다");
        Check(!pad.View.IsLocked, "휴게시간에는 다시 홈이 켜진다");

        pad.QueueFree();
        hud.QueueFree();
        await Frames(1);
    }

    // ── 패드 화면 — 단서 · 직원 · 지침 ────────────────────────────────
    private async System.Threading.Tasks.Task TestPadView()
    {
        Head("L", "패드 화면 — 페이지 · 정렬 · 직원 필터 · 상세 · 관련 진술 · 직원 탭 · 지침");
        Reset();
        GameState.Instance.SetPhase(GamePhase.Rest);
        Deploy();
        // 단서 8장 — 한 페이지(6장)를 넘긴다.
        for (int i = 0; i < 4; i++) Incident(LogEventType.TaskFailed, Power, At(20 + i * 10));
        Move("cat", Storage, Power, At(25));
        Move("dog", Guard, Core, At(35));
        PlayerKnownEvidence.RecordCctvObservation(Power, At(26), new[] { "cat", "sheep" });
        PlayerKnownEvidence.RecordOverheard(Guard, "fox", "wolf", "조용하네요.", "그러게요.", At(50));
        foreach (var ev in InterviewEvidenceBoard.BuildAll("cat")
                     .Where(e => e.Kind is EvidenceKind.Incident or EvidenceKind.Movement or EvidenceKind.Cctv or EvidenceKind.Overheard))
            ClueBoard.Pin(ev);
        foreach (var ev in InterviewEvidenceBoard.Build("dog").Where(e => e.Kind == EvidenceKind.Movement)) ClueBoard.Pin(ev);
        foreach (var ev in InterviewEvidenceBoard.Build("fox").Where(e => e.Kind == EvidenceKind.Overheard)) ClueBoard.Pin(ev);
        int total = ClueBoard.Count;

        var view = new PadView();
        AddChild(view);
        await Frames(1);
        view.OnOpened();
        view.OpenApp(PadView.Tab.Clues, fade: false);
        Check(total >= 7 && view.VisibleClues().Count == total, $"단서 {total}장이 모두 보인다");
        Check(view.PageCount == (total + 5) / 6, $"한 페이지 6장 — {view.PageCount}쪽");
        view.SetPage(1);
        Check(view.Page == 1, "다음 쪽으로 넘어간다");
        view.SetPage(99);
        Check(view.Page == view.PageCount - 1, "마지막 쪽을 넘지 않는다");

        var times = view.VisibleClues().Select(e => e.Evidence.AnchorTime).ToList();
        Check(times.SequenceEqual(times.OrderBy(t => t)), "기본은 시간순");
        view.SetGroupByEmployee(true);
        var firsts = view.VisibleClues().Select(e => e.Evidence.SubjectEmployeeId).Where(s => s != "").ToList();
        Check(firsts.Count > 0 && firsts.SequenceEqual(firsts.OrderBy(s => _sim.GetEmployeeIds().ToList().IndexOf(s))), "직원별로 묶인다");
        view.SetGroupByEmployee(false);

        view.ShowCluesFor("cat");
        Check(view.EmployeeFilter == "cat" && view.VisibleClues().All(e => ClueBoard.Involves(e, "cat")) && view.VisibleClues().Count >= 2,
            $"직원 필터 — 고양이가 등장하는 단서만 {view.VisibleClues().Count}장");
        view.ClearEmployeeFilter();

        // 상세 — 관련 직원 · 관련 진술.
        var cctv = ClueBoard.Entries.First(e => e.Evidence.Kind == EvidenceKind.Cctv);
        var related = PadView.RelatedStaff(cctv);
        Check(related.Contains("cat") && related.Contains("sheep"), $"관련 직원에 함께 있던 사람까지 — {string.Join(",", related)}");
        var incident = ClueBoard.Entries.First(e => e.Evidence.Kind == EvidenceKind.Incident);
        Check(PadView.RelatedStatements(incident).Count == 0, "아직 아무도 그 자료로 묻지 않았다 — 진술 없음");
        DialogueHistory.Instance.AddEntry("manager", "관리자", DialogueEntryType.PlayerChoice, "이 사고를 알고 있었습니까?",
            DialogueConversationType.Interview, "cat", new[] { incident.EvidenceId });
        DialogueHistory.Instance.AddEntry("cat", "고양이", DialogueEntryType.NpcResponse, "몰랐어요.",
            DialogueConversationType.Interview, "cat", new[] { incident.EvidenceId });
        DialogueHistory.Instance.AddEntry("dog", "강아지", DialogueEntryType.NpcResponse, "다른 이야기예요.",
            DialogueConversationType.Interview, "dog");
        var talks = PadView.RelatedStatements(incident);
        Check(talks.Count == 1 && talks[0].Employee == "cat" && talks[0].Lines.Count == 2,
            "그 자료로 물은 질문과 대답만 — 고양이 한 사람, 두 줄");
        view.OpenDetail(incident);
        Check(view.DetailEntry == incident, "카드를 누르면 상세가 열린다");
        ClueBoard.Unpin(incident.Day, incident.EvidenceId);
        Check(view.DetailEntry == null && ClueBoard.Count == total - 1, "상세에서 핀을 풀면 상세가 닫히고 단서가 빠진다");

        // 직원 탭.
        view.SwitchTab(PadView.Tab.Staff);
        view.OpenStaffDetail("cat");
        Check(view.StaffDetailId == "cat", "직원 상세가 열린다");
        var (good, bad) = PadView.Relations("cat");
        Check(good.All(o => RelationshipSystem.Band("cat", o) is PairBand.Close or PairBand.Friendly)
              && bad.All(o => RelationshipSystem.Band("cat", o) is PairBand.Uneasy or PairBand.Refuse)
              && good.Count + bad.Count > 0, $"관계는 Band 로 — 좋음 {string.Join(",", good)} / 나쁨 {string.Join(",", bad)}");
        _sim.GetEmployeeState("wolf").Isolated = true;
        Check(PadView.StatusOf("wolf").Text == "격리", "격리된 직원에 뱃지");
        _sim.GetEmployeeState("wolf").Isolated = false;
        bool filled = _sim.GetEmployeeIds().All(id => _sim.GetEmployeeDef(id) is { } d
            && d.Gender != "" && d.ShortProfileLine != "" && d.SelfIntroLine != "" && d.FavoriteFood != "" && d.DislikedFood != "");
        Check(filled, "6명 모두 성별 · 한 줄 성격 · 자기소개 · 음식 기록이 있다");
        view.ShowCluesFor("dog");
        Check(view.Current == PadView.Tab.Clues && view.EmployeeFilter == "dog", "직원 → 단서 보기는 그 직원 필터로 단서 탭을 연다");

        view.SwitchTab(PadView.Tab.Manual);
        var manual = GD.Load<PadManualDef>(PadManualDef.DefaultPath);
        var chapters = manual?.ChapterList() ?? new System.Collections.Generic.List<PadManualChapterDef>();
        Check(chapters.Count == 6, $"지침 챕터 6장(data/pad/manual.tres) — {chapters.Count}장");
        Check(chapters.All(c => c.PageList().Count is >= 2 and <= 4), "챕터마다 2~4쪽");
        var pages = chapters.SelectMany(c => c.PageList()).ToList();
        Check(pages.All(p => p.Lines.Length <= PadManualDef.MaxLinesPerPage && p.Lines.All(l => l.Length <= PadManualDef.MaxCharsPerLine)),
            $"모든 쪽이 4줄 · 줄당 40자 이내 — 가장 긴 줄 {pages.SelectMany(p => p.Lines).Max(l => l.Length)}자");
        Check(pages.All(p => !string.IsNullOrEmpty(p.ImagePath) && !string.IsNullOrEmpty(p.Title)), "모든 쪽에 그림 경로와 제목이 있다");
        view.OpenChapter(2);
        view.SetManualPage(1);
        Check(view.ManualChapter == 2 && view.ManualPage == 1, "챕터를 열고 쪽을 넘긴다");
        view.SetManualPage(99);
        Check(view.ManualPage == chapters[2].PageList().Count - 1, "마지막 쪽을 넘지 않는다");
        view.OpenChapter(-1);
        Check(view.ManualChapter == -1, "◀ 목록으로 챕터 카드로 돌아간다");
        view.QueueFree();
        await Frames(1);
    }

    // ── 최종 격리 보고서 — 지목과 근거 판정 ────────────────────────────
    //
    // 엔딩을 가르는 값이라 여기서 못 박는다.
    //   WasCaught = 지목 == 실제 결번  → 엔딩 축
    //   WasProven = 맞혔고 + 붙인 근거 중 한 장이 그 직원이 등장하는 자료
    //               (엔딩을 바꾸지 않는다 — 성적표와 GUIDE-0 한 줄에만 쓴다)
    private void TestFinalReport()
    {
        Head("N", "최종 격리 보고서 — 지목 정답 · 근거 0장 / 무관 1장 / 유효 1장");
        Reset();
        Deploy();
        GameState.Instance.SetSaboteur("cat");

        // 고양이의 이동 자료(유효 근거)와 늑대의 이동 자료(무관 근거)를 한 장씩 찍어 둔다.
        Move("cat", Storage, Power, At(30));
        Move("wolf", Maintenance, Guard, At(40));
        var evidence = Rows().Select(InterviewEvidenceBoard.FromLogRow).Where(e => e != null).ToList();
        var catEv = evidence.FirstOrDefault(e => e.SubjectEmployeeId == "cat");
        var wolfEv = evidence.FirstOrDefault(e => e.SubjectEmployeeId == "wolf");
        if (!Check(catEv != null && wolfEv != null, "검사용 자료 두 장을 만들었다")) return;
        ClueBoard.Pin(catEv);
        ClueBoard.Pin(wolfEv);

        bool Involves(int day, string id) =>
            ClueBoard.Involves(ClueBoard.Find(day, id), GameState.Instance.FinalAccusedId);
        var none = new List<(int Day, string EvidenceId)>();
        var wolfOnly = new List<(int Day, string EvidenceId)> { (wolfEv.Day, wolfEv.Id) };
        var catOnly = new List<(int Day, string EvidenceId)> { (catEv.Day, catEv.Id) };

        GameState.Instance.SubmitFinalReport("cat", none, Involves);
        Check(GameState.Instance.WasCaught && !GameState.Instance.WasProven,
            "정답 + 근거 0장 → 맞혔지만 증명하지 못했다");

        GameState.Instance.SubmitFinalReport("cat", wolfOnly, Involves);
        Check(GameState.Instance.WasCaught && !GameState.Instance.WasProven,
            "정답 + 무관한 근거 1장 → 증명으로 치지 않는다");

        GameState.Instance.SubmitFinalReport("cat", catOnly, Involves);
        Check(GameState.Instance.WasCaught && GameState.Instance.WasProven,
            "정답 + 그 직원이 등장하는 근거 1장 → 증명");

        GameState.Instance.SubmitFinalReport("wolf", catOnly, Involves);
        Check(!GameState.Instance.WasCaught && !GameState.Instance.WasProven,
            "오판이면 근거가 무엇이든 증명이 아니다");

        GameState.Instance.SubmitFinalReport("", catOnly, Involves);
        Check(!GameState.Instance.WasCaught, "미지목은 오판으로 친다");

        Reset();
    }

    // ── 스냅샷 연결 ───────────────────────────────────────────────────
    private void TestSnapshot()
    {
        Head("M", "CCTV 스냅샷 — 사고 순간의 한 컷이 그 사고 자료에 붙는다 · 보던 방만");
        Reset();
        Deploy();
        Incident(LogEventType.Sabotage, Power, At(40));
        var ev = Rows().Select(InterviewEvidenceBoard.FromLogRow).First(e => e != null);
        var log = EventLog.Instance.GetAllEntries().Last();
        string idAtEvent = InterviewEvidenceBoard.IncidentIdOf(
            InterviewEvidenceBoard.IncidentKeyOf(log.EventType, log.RoomId, log.GameTimeSeconds));
        Check(idAtEvent == ev.Id, "사건이 난 순간 만든 Id = 조사 자료 Id");

        var img = Image.CreateEmpty(CctvSnapshotRecorder.SnapshotSize.X, CctvSnapshotRecorder.SnapshotSize.Y, false, Image.Format.Rgb8);
        var tex = ImageTexture.CreateFromImage(img);
        ClueBoard.OfferSnapshot(1, idAtEvent, tex);
        Check(ClueBoard.SnapshotOf(1, ev.Id) == tex && ClueBoard.Count == 0, "찍기 전에도 한 컷은 보관된다(단서는 아직 아니다)");
        ClueBoard.Pin(ev);
        Check(ClueBoard.Find(1, ev.Id).Snapshot == tex, "사고를 찍으면 그 한 컷이 따라 붙는다");

        // 같은 순간 · 같은 방에서 겹친 사고 줄(#2)도 같은 한 컷을 쓴다.
        var twin = ev.Clone();
        twin.Id = ev.Id + "#2";
        Check(ClueBoard.SnapshotOf(1, twin.Id) == tex, "겹친 사고 줄(#2)도 같은 한 컷을 찾는다");
        ClueBoard.Pin(twin);
        var tex2 = ImageTexture.CreateFromImage(img);
        ClueBoard.OfferSnapshot(1, idAtEvent, tex2);
        Check(ClueBoard.Find(1, twin.Id).Snapshot == tex2 && ClueBoard.Find(1, ev.Id).Snapshot == tex2,
            "나중에 들어온 한 컷은 이미 찍힌 겹친 줄 모두에 붙는다");

        GameState.Instance.SetPhase(GamePhase.Live);
        Check(!CctvSnapshotRecorder.CanSee(Power) || _sim.SurveillanceTargetRoomId == Power,
            "보고 있지 않은 방은 찍지 않는다");
        GameState.Instance.SetPhase(GamePhase.Rest);
        Check(!CctvSnapshotRecorder.CanSee(_sim.SurveillanceTargetRoomId), "근무 중이 아니면 찍지 않는다");
    }

    private async System.Threading.Tasks.Task Until(System.Func<bool> cond, int timeoutMs)
    {
        ulong until = Time.GetTicksMsec() + (ulong)timeoutMs;
        while (!cond() && Time.GetTicksMsec() < until) await Frames(1);
    }

    // --- 도우미 -----------------------------------------------------------

    private static float At(int gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }

    private static void Reset()
    {
        GameState.Instance.ResetRun(1);
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
    }

    private static List<DisplayLogEntry> Rows(int day = 1) =>
        FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), day);

    private static void Deploy(int day = 1)
    {
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Maintenance,
            ["rabbit"] = Maintenance, ["fox"] = Core,
        };
        var sim = FacilitySimulation.Instance;
        foreach (var kv in rooms)
        {
            var st = sim.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value;
            st.CurrentRoomId = kv.Value;
            st.Alive = true;
            st.Isolated = false;
        }
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f, day);
    }

    private static void Move(string employeeId, string from, string to, float arriveAt)
    {
        Log(LogEventType.RoomExit, employeeId, from, Mathf.Max(0f, arriveAt - 1f));
        Log(LogEventType.RoomEnter, employeeId, to, arriveAt);
    }

    private static void Incident(LogEventType type, string roomId, float at, int day = 1) => Log(type, "", roomId, at, day);

    private static void Log(LogEventType type, string actor, string roomId, float at, int day = 1)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = day,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = roomId,
            Description = $"(테스트 {type} {roomId} {at:0.0})",
            WitnessEmployeeIds = new List<string>(),
        });
    }

    private void LiveSetup(Dictionary<string, string> plan)
    {
        EventLog.Instance?.ClearAll();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        foreach (var (emp, room) in plan) _sim.AssignToRoom(emp, room);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        for (float t = 0f; t < 60f && _sim.OnDutyEmployeeIds(Guard).Count < 2; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private static void Advance(CctvOverheardCaption cap, string room, float seconds)
    {
        for (float s = 0f; s < seconds; s += 0.05f)
        {
            GameState.Instance.AdvanceDayTime(0.05f);
            cap.Tick(0.05f, room, true);
        }
    }

    private static IEnumerable<Button> Buttons(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is Button b) yield return b;
            foreach (var d in Buttons(c)) yield return d;
        }
    }

    private static IEnumerable<Label> Labels(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is Label l) yield return l;
            foreach (var d in Labels(c)) yield return d;
        }
    }

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
