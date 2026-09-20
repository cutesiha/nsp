using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 휴게시간 조사 자료 자동 검증 (Test A~J).
//
//   godot --headless --path . scenes/debug/RestEvidenceTest.tscn --quit-after 900
//
// 보는 것은 하나다 — "근무 중 플레이어가 본 것만 자료가 되고, 그 자료로 실제 심문이
// 되는가". 문장이 예쁜지는 보지 않는다.
public partial class RestEvidenceTest : Node
{
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";
    private const string Vent = "vent_room";
    private const string Medical = "medical_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        if (FacilitySimulation.Instance == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 휴게시간 조사 자료 검증 ################");
        TestA();
        TestB();
        TestC();
        TestD();
        TestE();
        TestF();
        TestG();
        TestH();
        TestI();
        TestJ();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── A : 근무 중 화면에 뜬 이동 기록이 조사 자료가 된다 ────────────────
    private void TestA()
    {
        Head("A", "화면에 뜬 이동 기록 → 조사 자료");
        Reset();
        Deploy();
        Move("cat", Storage, Maintenance, At(13));

        var shown = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1)
            .Any(r => r.RelatedEmployeeId == "cat" && r.ToRoomId == Maintenance);
        var board = InterviewEvidenceBoard.Build("cat");
        var move = board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement);

        Check(shown, "이 이동이 시설 로그 화면에 실제로 떴다");
        Check(move != null, "같은 이동이 조사 자료로 들어온다");
        if (move == null) return;
        GD.Print($"   자료: {move.OneLine}");
        Check(move.FromRoomId == Storage && move.ToRoomId == Maintenance, "출발·도착 작업실이 그대로 실린다");
        Check(move.HasTime && Mathf.IsEqualApprox(move.AnchorTime, At(13)), "시각이 그대로 실린다");
        Check(move.PlayerObserved && move.Day == 1, "플레이어가 본 오늘 자료로 기록된다");
    }

    // ── B : 플레이어가 보지 못한 내부 기록은 자료가 되지 않는다 ────────────
    private void TestB()
    {
        Head("B", "통과 기록 · 목격자 없는 방해공작 → 자료 아님");
        Reset();
        Deploy();

        // ① 지나가기만 한 방(화면 로그에 뜨지 않는다)
        Log(LogEventType.RoomExit, "crow", Guard, At(9), passing: true);
        Log(LogEventType.RoomEnter, "crow", Core, At(10), passing: true);
        // ② 아무도 보지 못한 결번자의 행동 — EventLog 에는 남지만 화면에는 없다
        GameState.Instance.SetSaboteur("fox");
        Log(LogEventType.RoomEnter, "fox", Power, At(20), passing: true);

        var crow = InterviewEvidenceBoard.Build("crow");
        var fox = InterviewEvidenceBoard.Build("fox");
        GD.Print($"   까마귀 자료 {crow.Count}장 · 여우 자료 {fox.Count}장 (기분 제외 " +
                 $"{crow.Count(e => e.Kind != EvidenceKind.Mood)}/{fox.Count(e => e.Kind != EvidenceKind.Mood)})");

        Check(!crow.Any(e => e.Kind == EvidenceKind.Movement), "통과만 한 이동은 자료가 되지 않는다");
        Check(!fox.Any(e => e.Kind == EvidenceKind.Movement), "결번자의 숨은 이동도 자료가 되지 않는다");
        // 내부 진실은 여전히 알고 있지만, 그것이 자료로 새어 나오지 않아야 한다.
        Check(DialogueContextBuilder.RoomAt("fox", 1, At(25)) == Power,
            "내부 기록(SystemTruth)에는 그대로 남아 있다");
        Check(fox.All(e => e.PlayerObserved), "조사 자료에 들어온 것은 전부 플레이어가 본 것이다");
    }

    // ── C : CCTV 로 본 장면이 자료가 된다 ────────────────────────────────
    private void TestC()
    {
        Head("C", "CCTV 관찰 → CCTV 자료");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat", "rabbit" });

        var board = InterviewEvidenceBoard.Build("cat");
        var cctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        Check(cctv != null, "CCTV 자료가 생긴다");
        if (cctv == null) return;
        GD.Print($"   자료: {cctv.OneLine}  (같이 잡힌 인원 {cctv.RelatedEmployeeIds.Count}명)");
        Check(cctv.SubjectRoomId == Maintenance && cctv.Position == PositionClaim.AtRoom,
            "그 시각 그 방에 있었다는 위치 자료다");
        Check(cctv.RelatedEmployeeIds.Contains("rabbit"), "같이 찍힌 직원이 함께 기록된다");
        Check(!InterviewEvidenceBoard.Build("owl").Any(e => e.Kind == EvidenceKind.Cctv),
            "찍히지 않은 직원의 자료에는 뜨지 않는다");
    }

    // ── D : 인터뷰에서 얻은 위치 진술이 자료가 된다 ───────────────────────
    private void TestD()
    {
        Head("D", "인터뷰 답변 → 이 직원의 진술 자료");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(30));

        var session = new InterviewSession("cat");
        int before = session.Board.Count(e => e.Kind == EvidenceKind.OwnStatement);

        var incident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        Check(incident != null, "사고 기록이 자료로 뜬다");
        if (incident == null) return;

        var q = InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident);
        var turn = session.Ask(q);
        GD.Print($"   Q: {turn.QuestionText}\n   A: {turn.Answer}");

        var claims = session.Board.Where(e => e.Kind == EvidenceKind.OwnStatement).ToList();
        Check(claims.Count == before + 1, "답변이 진술 자료 한 장으로 남는다");
        if (claims.Count == 0) return;
        GD.Print($"   자료: {claims[0].OneLine}");
        Check(claims[0].HasTime && claims[0].CanAnchorPosition, "그 진술에 시각과 위치가 함께 붙는다");
    }

    // ── E : 한 직원의 증언을 다른 직원 심문에 쓴다 ────────────────────────
    private void TestE()
    {
        Head("E", "까마귀의 목격 증언 → 고양이 심문에서 사용");
        Reset();
        Deploy();
        GameState.Instance.SetSaboteur("cat");
        // 까마귀가 실제로 같은 방에서 고양이의 행동을 봤다.
        Log(LogEventType.Sabotage, "cat", Maintenance, At(35), witnesses: new[] { "crow" });

        var crowSession = new InterviewSession("crow");
        var q = crowSession.BasicQuestions().First(x => x.Intent == InterviewIntent.BasicSuspicious);
        var turn = crowSession.Ask(q);
        GD.Print($"   Q(까마귀): {turn.QuestionText}\n   A: {turn.Answer}");

        var catBoard = InterviewEvidenceBoard.Build("cat");
        var said = catBoard.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        Check(said != null, "까마귀의 증언이 고양이의 조사 자료로 넘어간다");
        if (said == null) return;
        GD.Print($"   자료: [{said.Header}] {said.OneLine}");
        Check(said.SpeakerEmployeeId == "crow" && said.SubjectEmployeeId == "cat",
            "누가 누구에 대해 말했는지가 남는다");

        var follow = InterviewQuestionFactory.For("cat", said);
        Check(follow.Any(x => x.Intent == InterviewIntent.AskConfirmTestimony),
            "그 증언으로 고양이에게 확인 질문을 만들 수 있다");
        if (follow.Count > 0) GD.Print($"   Q(고양이): {follow[0].Text}");
    }

    // ── F : 같은 시각 다른 방 → 모순 추궁 가능 ────────────────────────────
    private void TestF()
    {
        Head("F", "22:16 저장고 진술 vs 22:16 정비실 CCTV");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordLocationStatement("cat", "t:16", Storage, true, At(16));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat" });

        var session = new InterviewSession("cat");
        var claim = session.Board.First(e => e.Kind == EvidenceKind.OwnStatement);
        var cctv = session.Board.First(e => e.Kind == EvidenceKind.Cctv);
        session.Toggle(claim.Id);
        session.Toggle(cctv.Id);

        Check(session.CanTryConfront, "자료 두 장이 선택된다");
        var r = session.CheckContradiction();
        Check(r.IsContradiction, "모순으로 판정된다");
        if (!r.IsContradiction) { GD.Print($"   안내: {r.Notice}"); return; }
        GD.Print($"   Q: {r.QuestionText}");
        GD.Print($"   A: {session.Confront(r).Answer}");
        Check(r.QuestionText.Contains("저장고") && r.QuestionText.Contains("정비실"),
            "추궁 문장이 두 자료를 모두 인용한다");
    }

    // ── G : 시간이 벌어진 두 기록 → 모순 아님 ─────────────────────────────
    private void TestG()
    {
        Head("G", "21:50 저장고 / 22:30 정비실");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Storage, At(5), new[] { "cat" });
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(45), new[] { "cat" });

        var session = new InterviewSession("cat");
        var a = session.Board.First(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Storage);
        var b = session.Board.First(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Maintenance);
        session.Toggle(a.Id);
        session.Toggle(b.Id);
        var r = session.CheckContradiction();
        GD.Print($"   안내: {r.Notice}");
        Check(!r.IsContradiction, "모순으로 판정되지 않는다");
        Check(!string.IsNullOrEmpty(r.Notice), "왜 안 되는지 화면에 설명이 나간다");
    }

    // ── H : 결번자의 거짓 알리바이는 반복 질문에도 유지된다 ────────────────
    private void TestH()
    {
        Head("H", "결번자의 진술 자료 ↔ DialogueClaimState");
        Reset();
        Deploy();
        GameState.Instance.SetSaboteur("cat");
        // 고양이는 실제로 정비실로 갔고, 거기서 사고가 났다.
        Move("cat", Storage, Maintenance, At(20));
        Incident(LogEventType.TaskFailed, Maintenance, At(30));

        var session = new InterviewSession("cat");
        var incident = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        var q = InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident);

        string first = session.Ask(q).Answer;
        var claimEv = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        string claimedRoom = claimEv?.SubjectRoomId ?? "";

        // 같은 질문을 다시. 새 알리바이를 지어내면 안 된다.
        string second = session.Ask(q).Answer;
        var claimAfter = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        var state = DialogueClaimState.Get("cat", 1, incident.IncidentKey);

        GD.Print($"   1차: {first}\n   2차: {second}");
        GD.Print($"   진술 자료 = {InterviewEvidenceBoard.RoomName(claimedRoom)} / " +
                 $"ClaimState = {InterviewEvidenceBoard.RoomName(state.ClaimedRoomId)} " +
                 $"(진실 {state.ClaimTruthful})");
        Check(claimEv != null, "결번자의 진술도 자료로 남는다");
        Check(claimAfter?.SubjectRoomId == claimedRoom, "다시 물어도 진술 자료가 바뀌지 않는다");
        Check(claimedRoom == state.ClaimedRoomId, "진술 자료가 DialogueClaimState 와 같은 방을 가리킨다");
        Check(session.Board.Count(e => e.Kind == EvidenceKind.OwnStatement) == 1,
            "같은 사건에 대한 진술 자료가 여러 장으로 불어나지 않는다");
    }

    // ── I : 아무 대사나 자료가 되지는 않는다 ──────────────────────────────
    private void TestI()
    {
        Head("I", "기분·소감·되묻기는 자료로 저장하지 않는다");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(30));

        var session = new InterviewSession("owl");
        int before = session.Board.Count;

        // 기분 질문 3종 + 기본 질문 3종 + 정확한 시각 되묻기.
        var mood = session.Board.First(e => e.Kind == EvidenceKind.Mood);
        foreach (var intent in new[] { InterviewIntent.AskMoodReason, InterviewIntent.AskMoodBefore,
                     InterviewIntent.AskMoodRelated })
            session.Ask(InterviewQuestionFactory.Make("owl", mood, intent));
        foreach (var q in session.BasicQuestions()) session.Ask(q);
        var incident = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        session.Ask(InterviewQuestionFactory.Make("owl", incident, InterviewIntent.FollowExactTime));

        int after = session.Board.Count;
        GD.Print($"   질문 7개 → 자료 {before}장 → {after}장");
        Check(after == before, "추리에 쓸 수 없는 답변은 자료를 늘리지 않는다");

        // 반대로 위치를 묻는 질문은 자료를 늘린다.
        session.Ask(InterviewQuestionFactory.Make("owl", incident, InterviewIntent.AskWhereAtIncident));
        GD.Print($"   위치 질문 1개 → 자료 {session.Board.Count}장");
        Check(session.Board.Count == before + 1, "위치 진술은 자료로 남는다");
    }

    // ── J : 심문 대상을 바꿔도 오늘의 자료는 남는다 ───────────────────────
    private void TestJ()
    {
        Head("J", "직원을 바꿔도 조사 자료 유지 · DAY 가 바뀌면 분리");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat" });
        Incident(LogEventType.TaskFailed, Power, At(30));

        var catSession = new InterviewSession("cat");
        var incident = catSession.Board.First(e => e.Kind == EvidenceKind.Incident);
        catSession.Ask(InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident));
        int catEvidence = catSession.Board.Count;

        // 다른 직원을 심문하고 다시 돌아온다.
        var owlSession = new InterviewSession("owl");
        owlSession.Ask(InterviewQuestionFactory.Make("owl", incident, InterviewIntent.AskWhereAtIncident));
        var back = new InterviewSession("cat");

        GD.Print($"   고양이 {catEvidence}장 → 올빼미 심문 후 {back.Board.Count}장");
        Check(back.Board.Count >= catEvidence, "먼저 확보한 자료가 사라지지 않는다");
        Check(back.Board.Any(e => e.Kind == EvidenceKind.OwnStatement), "고양이의 진술이 그대로 남아 있다");

        // 전원 보기에서는 올빼미의 진술까지 함께 읽을 수 있다.
        back.SetScope(true);
        Check(back.Board.Any(e => e.SubjectEmployeeId == "owl"), "전원 보기에서 다른 직원 자료도 읽힌다");
        Check(!back.CanUse(back.Board.First(e => e.SubjectEmployeeId == "owl")),
            "다른 직원의 자료로는 이 사람에게 질문할 수 없다");
        back.SetScope(false);

        // DAY 가 넘어가면 오늘 자료는 섞이지 않는다.
        GameState.Instance.GoToNextDay();
        var day2 = InterviewEvidenceBoard.Build("cat");
        GD.Print($"   DAY{GameState.Instance.CurrentDay} 조사 자료 {day2.Count}장 " +
                 $"(진술 {day2.Count(e => e.Kind == EvidenceKind.OwnStatement)}장)");
        Check(!day2.Any(e => e.Kind == EvidenceKind.OwnStatement),
            "지난 DAY 의 진술이 다음 DAY 자료에 섞이지 않는다");
        Check(!day2.Any(e => e.Kind == EvidenceKind.Cctv),
            "지난 DAY 의 CCTV 기록도 섞이지 않는다");
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
        FacilitySimulation.Instance.RollDailyMoods();
    }

    // 배치 + 근무 시작. TaskStart 가 있어야 이후 이동이 시설 로그 화면에 뜬다.
    private static void Deploy()
    {
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Maintenance,
            ["rabbit"] = Maintenance, ["jellyfish"] = Power, ["fox"] = Core,
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
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
    }

    private static void Move(string employeeId, string from, string to, float arriveAt)
    {
        Log(LogEventType.RoomExit, employeeId, from, Mathf.Max(0f, arriveAt - 1f));
        Log(LogEventType.RoomEnter, employeeId, to, arriveAt);
    }

    private static void Incident(LogEventType type, string roomId, float at) => Log(type, "", roomId, at);

    private static void Log(LogEventType type, string actor, string roomId, float at,
        IEnumerable<string> witnesses = null, bool passing = false)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = roomId,
            PassingThrough = passing,
            Description = $"(테스트 {type} {roomId} {at:0.0})",
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }
}
