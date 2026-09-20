using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 증거 기반 심문의 검증 시나리오 A~G.
//
//   godot --headless --path . scenes/debug/DialogueScenarioTest.tscn
//
// 문장이 예쁜지가 아니라 "질문과 답변이 올바른 사건·시각·작업실에 묶여 있는지" 를 본다.
// 옛 꼬리질문이 저지르던 사고(다른 사건의 방 이름으로 추궁하기)가 다시 나오면 여기서 걸린다.
public static class InterviewScenarioTest
{
    private const string Vent = "vent_room";
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Medical = "medical_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";

    private static int _pass, _fail;

    public static void RunAll()
    {
        _pass = _fail = 0;
        GD.Print("\n\n################ 증거 기반 심문 검증 ################");
        TestA();
        TestB();
        TestC();
        TestD();
        TestE();
        TestF();
        TestG();
        TestNoAutoChallenge();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── A : 이동 기록을 고르면 그 이동을 그대로 묻는다 ──────────────────
    private static void TestA()
    {
        Head("A", "22:13 고양이 저장고→정비실 기록으로 질문 생성");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Vent,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Medical, ["fox"] = Core });
        Move("cat", Storage, Maintenance, At(13));

        var board = InterviewEvidenceBoard.Build("cat");
        var move = board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement);
        if (!Check(move != null, "이동 자료가 조사 자료에 뜬다")) return;

        GD.Print($"   자료: [{move.Header}] {move.OneLine}");
        var q = InterviewQuestionFactory.Make("cat", move, InterviewIntent.AskMoveReason);
        GD.Print($"   Q: {q.Text}");
        GD.Print($"   A: {InterviewReplyPlanner.Answer(q)}");

        Check(q.Text.Contains("저장고") && q.Text.Contains("정비실"), "질문이 그 기록의 두 작업실을 그대로 인용한다");
        Check(!q.Text.Contains("발전실") && !q.Text.Contains("의무실") && !q.Text.Contains("경비실"),
            "엉뚱한 작업실 이름이 섞이지 않는다");
        Check(Mathf.IsEqualApprox(q.AnchorTime, move.AnchorTime), "질문의 기준 시각이 그 자료의 시각이다");
    }

    // ── B : 진술과 CCTV 가 같은 시각에 어긋나면 추궁이 열린다 ────────────
    private static void TestB()
    {
        Head("B", "22:16 저장고 진술 vs 22:16 정비실 CCTV → 모순 성립");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Vent,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Medical, ["fox"] = Core });

        PlayerKnownEvidence.RecordLocationStatement("cat", "TaskFailed:power_room:16.0", Storage, true, At(16));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat" });

        var board = InterviewEvidenceBoard.Build("cat");
        var claim = board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        var cctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        if (!Check(claim != null && cctv != null, "진술과 CCTV 가 모두 자료로 뜬다")) return;

        GD.Print($"   자료1: [{claim.Header}] {claim.OneLine}");
        GD.Print($"   자료2: [{cctv.Header}] {cctv.OneLine}");
        var r = EvidenceContradiction.Check("cat", claim, cctv);
        Check(r.IsContradiction, "모순으로 판정된다");
        if (!r.IsContradiction) { GD.Print($"   안내: {r.Notice}"); return; }

        GD.Print($"   Q: {r.QuestionText}");
        GD.Print($"   A: {InterviewReplyPlanner.ConfrontAnswer("cat", r)}");
        Check(r.QuestionText.Contains("저장고") && r.QuestionText.Contains("정비실"),
            "추궁 문장이 두 자료를 모두 인용한다");
    }

    // ── C : 시간이 벌어진 두 기록은 모순이 아니다 ───────────────────────
    private static void TestC()
    {
        Head("C", "22:05 저장고 / 22:45 정비실 → 모순 아님");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Vent,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Medical, ["fox"] = Core });

        PlayerKnownEvidence.RecordCctvObservation(Storage, At(5), new[] { "cat" });
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(45), new[] { "cat" });

        var board = InterviewEvidenceBoard.Build("cat");
        var a = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Storage);
        var b = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Maintenance);
        if (!Check(a != null && b != null, "두 CCTV 자료가 모두 뜬다")) return;

        var r = EvidenceContradiction.Check("cat", a, b);
        Check(!r.IsContradiction, "모순으로 판정되지 않는다");
        GD.Print($"   안내: {r.Notice}");
    }

    // ── D : 방해공작은 "사건"으로만 자료가 된다(범인은 알려 주지 않는다) ──
    private static void TestD()
    {
        Head("D", "목격자 없는 방해공작 — 사건은 남고 실행자는 새지 않는다");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Vent,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Medical, ["fox"] = Core });
        // 내부 기록에는 실행자가 남지만 아무도 그 장면을 보지 못했다.
        Log(LogEventType.Sabotage, "cat", Core, At(20));

        var board = InterviewEvidenceBoard.Build("cat");
        foreach (var e in board) GD.Print($"   자료: [{e.Header}] {e.OneLine}");

        var sab = board.FirstOrDefault(e => e.IncidentType == LogEventType.Sabotage);
        // 시설 로그 화면에 뜬 사건이므로 조사 자료가 된다 — 플레이어가 본 것이다.
        Check(sab != null, "방해공작 사건 자체는 사고 기록으로 뜬다");
        if (sab == null) return;

        Check(string.IsNullOrEmpty(sab.SubjectEmployeeId),
            "그 자료는 누구의 것도 아니다(특정 직원을 가리키지 않는다)");
        Check(sab.Position == PositionClaim.None,
            "위치 주장이 아니므로 그것만으로 누구의 알리바이도 깨지 않는다");
        Check(!board.Any(e => e.Kind == EvidenceKind.Movement || e.Kind == EvidenceKind.Cctv),
            "실행자의 이동이나 CCTV 기록은 여전히 자료가 아니다");

        var qs = board.SelectMany(e => InterviewQuestionFactory.For("cat", e)).ToList();
        Check(qs.All(q => !q.Text.Contains("고양이")), "질문 후보가 실행자를 지목하지 않는다");
    }

    // ── E : 거짓 알리바이는 후속 질문에도 유지된다 ──────────────────────
    private static void TestE()
    {
        Head("E", "방해자의 알리바이가 반복 질문에서 흔들리지 않는다");
        Reset();
        Deploy(new() { ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core });
        GameState.Instance.SetSaboteur("crow");
        Move("crow", Vent, Storage, At(8));          // 실제로는 저장고로 빠졌다
        Log(LogEventType.Sabotage, "crow", Storage, At(12));
        Incident(LogEventType.TaskFailed, Power, At(16));

        var board = InterviewEvidenceBoard.Build("crow");
        var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(incident != null, "사고 기록이 자료로 뜬다")) return;

        var qWhere = InterviewQuestionFactory.Make("crow", incident, InterviewIntent.AskWhereAtIncident);
        GD.Print($"   Q: {qWhere.Text}");
        GD.Print($"   A: {InterviewReplyPlanner.Answer(qWhere)}");
        var claimed = InterviewReplyPlanner.FrameFor(qWhere).Vars.GetValueOrDefault("room", "");
        string real = InterviewEvidenceBoard.RoomName(
            DialogueContextBuilder.RoomAt("crow", 1, incident.AnchorTime));
        GD.Print($"   [내부] 주장={claimed} / 실제={real}");

        // 같은 사건을 다른 각도로 두 번 더 캐묻는다.
        var qRestate = InterviewQuestionFactory.Make("crow", incident, InterviewIntent.AskWhereAtIncident);
        string again = InterviewReplyPlanner.FrameFor(qRestate).Vars.GetValueOrDefault("room", "");
        var qBefore = InterviewQuestionFactory.Make("crow", incident, InterviewIntent.AskBeforeIncident);
        var before = InterviewReplyPlanner.FrameFor(qBefore);
        GD.Print($"   A(직전): {InterviewReplyPlanner.Answer(qBefore)}");

        Check(claimed == again, "다시 물어도 같은 위치를 말한다");
        Check(before.Vars.GetValueOrDefault("room", "") == claimed, "직전 상황도 같은 알리바이 위에서 답한다");
        if (claimed != real)
            Check(before.Vars.GetValueOrDefault("prev", "") != real, "거짓 알리바이 중에는 실제 동선을 흘리지 않는다");
    }

    // ── F : 간접 인지는 직접 본 것처럼 말하지 않는다 ────────────────────
    private static void TestF()
    {
        Head("F", "옆방에서 소리만 들은 직원은 원인을 설명하지 않는다");
        Reset();
        Deploy(new() { ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core });
        Incident(LogEventType.TaskFailed, Power, At(16));

        var board = InterviewEvidenceBoard.Build("crow");
        var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(incident != null, "사고 기록이 자료로 뜬다")) return;

        var q = InterviewQuestionFactory.Make("crow", incident, InterviewIntent.AskIncidentKnown);
        var frame = InterviewReplyPlanner.FrameFor(q);
        GD.Print($"   Q: {q.Text}");
        GD.Print($"   A: {InterviewReplyPlanner.Answer(q)}");
        GD.Print($"   [내부] 인지수준 variant={frame.Variant}");

        var level = DialogueContextBuilder.KnowledgeOf("crow",
            DialogueContextBuilder.FindByKey(1, incident.IncidentKey));
        string expected = level == KnowledgeLevel.Direct ? "direct"
            : level == KnowledgeLevel.Indirect ? "indirect" : "none";
        Check(frame.Variant == expected, $"답변이 실제 인지수준({expected})과 일치한다");
    }

    // ── G : 서로 다른 사건의 값이 섞이지 않는다 ─────────────────────────
    private static void TestG()
    {
        Head("G", "사건이 둘이어도 각 질문은 자기 사건에만 묶인다");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["owl"] = Guard, ["crow"] = Vent,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Medical, ["fox"] = Core });
        Incident(LogEventType.CctvDisconnect, Medical, At(10));
        Incident(LogEventType.TaskFailed, Power, At(40));

        var board = InterviewEvidenceBoard.Build("cat");
        var incidents = board.Where(e => e.Kind == EvidenceKind.Incident)
            .OrderBy(e => e.AnchorTime).ToList();
        if (!Check(incidents.Count >= 2, "사고 자료가 두 건 모두 뜬다")) return;

        var q1 = InterviewQuestionFactory.Make("cat", incidents[0], InterviewIntent.AskWhereAtIncident);
        var q2 = InterviewQuestionFactory.Make("cat", incidents[1], InterviewIntent.AskWhereAtIncident);
        GD.Print($"   Q1: {q1.Text}");
        GD.Print($"   A1: {InterviewReplyPlanner.Answer(q1)}");
        GD.Print($"   Q2: {q2.Text}");
        GD.Print($"   A2: {InterviewReplyPlanner.Answer(q2)}");

        Check(!Mathf.IsEqualApprox(q1.AnchorTime, q2.AnchorTime), "두 질문의 기준 시각이 다르다");
        Check(q1.IncidentKey != q2.IncidentKey, "두 질문의 사건 키가 다르다");
        Check(q1.Text.Contains(DialogueClock.Spoken(incidents[0].AnchorTime)), "각 질문이 자기 사건의 시각을 말한다");
        Check(q2.Text.Contains(DialogueClock.Spoken(incidents[1].AnchorTime)), "두 번째 질문도 자기 시각을 말한다");
    }

    // ── 회귀 : 시스템이 대신 모순을 짚어 주지 않는다 ─────────────────────
    private static void TestNoAutoChallenge()
    {
        Head("H", "자동 꼬리질문에 추궁이 섞이지 않는다");
        Reset();
        Deploy(new() { ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core });
        GameState.Instance.SetSaboteur("crow");
        Move("crow", Vent, Storage, At(8));
        Incident(LogEventType.TaskFailed, Power, At(16));
        // 플레이어가 실제로 확보한 증거까지 잔뜩 깔아 둔다 — 그래도 자동 추궁은 없어야 한다.
        PlayerKnownEvidence.RecordCctvObservation(Storage, At(16), new[] { "crow" });
        PlayerKnownEvidence.RecordSighting("owl", "crow", Storage, At(16));

        bool any = false;
        foreach (string qid in new[] { DialogueQuestions.Anomaly, DialogueQuestions.Where,
                     DialogueQuestions.Suspicious, DialogueQuestions.Opinion, DialogueQuestions.Accuse })
        {
            var turn = LocalDialogueGenerator.Interview("crow", qid);
            foreach (var f in turn.FollowUps)
            {
                GD.Print($"   [꼬리질문] {f.Text}  ({f.Intent})");
                if (f.IsChallenge) any = true;
            }
        }
        Check(!any, "추궁형 꼬리질문이 하나도 자동 생성되지 않는다");

        // 대신 플레이어가 직접 고르면 열린다.
        var board = InterviewEvidenceBoard.Build("crow");
        var cctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        var say = board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        var claim2 = board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        GD.Print($"   자료 수: CCTV={(cctv != null ? 1 : 0)} 증언={(say != null ? 1 : 0)} 진술={(claim2 != null ? 1 : 0)}");
        if (cctv != null && claim2 != null)
        {
            var r = EvidenceContradiction.Check("crow", claim2, cctv);
            GD.Print($"   플레이어 제시 결과: {(r.IsContradiction ? r.QuestionText : r.Notice)}");
        }
    }

    // --- 도우미 ---------------------------------------------------------

    // 게임 안의 분 → 근무 시계 초.
    private static float At(int gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private static void Head(string id, string title)
    {
        GD.Print($"\n===== [증거심문 {id}] {title} =====");
    }

    private static bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }

    private static void Reset()
    {
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
    }

    // 배치 + 근무 시작. TaskStart 가 있어야 이후 이동이 시설 로그 화면에 뜬다.
    private static void Deploy(Dictionary<string, string> rooms)
    {
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
        IEnumerable<string> witnesses = null)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = roomId,
            // 시설 로그 화면은 같은 문장이 연달아 나오면 중복으로 보고 버린다.
            // 테스트 기록마다 서로 다른 문구를 줘야 실제와 같은 수의 줄이 뜬다.
            Description = $"(테스트 {type} {roomId} {at:0.0})",
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }
}
