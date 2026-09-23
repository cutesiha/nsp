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
        // 2차 — 결번자의 "설비 근처에 가지 않았다" 거짓말(§3-2).
        TestEquipmentDenial();
        TestInnocentAdmitsBehavior();
        TestDenialVersusWitness();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── A : 이동 기록을 고르면 그 이동을 그대로 묻는다 ──────────────────
    private static void TestA()
    {
        Head("A", "22:13 고양이 저장고→정비실 기록으로 질문 생성");
        Reset();
        Deploy(new() { ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });
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
        Deploy(new() { ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });

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
        Deploy(new() { ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });

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
        Deploy(new() { ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });
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
        Deploy(new() { ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core });
        GameState.Instance.SetSaboteur("wolf");
        Move("wolf", Vent, Storage, At(8));          // 실제로는 저장고로 빠졌다
        Log(LogEventType.Sabotage, "wolf", Storage, At(12));
        Incident(LogEventType.TaskFailed, Power, At(16));

        var board = InterviewEvidenceBoard.Build("wolf");
        var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(incident != null, "사고 기록이 자료로 뜬다")) return;

        var qWhere = InterviewQuestionFactory.Make("wolf", incident, InterviewIntent.AskWhereAtIncident);
        GD.Print($"   Q: {qWhere.Text}");
        GD.Print($"   A: {InterviewReplyPlanner.Answer(qWhere)}");
        var claimed = InterviewReplyPlanner.FrameFor(qWhere).Vars.GetValueOrDefault("room", "");
        string real = InterviewEvidenceBoard.RoomName(
            DialogueContextBuilder.RoomAt("wolf", 1, incident.AnchorTime));
        GD.Print($"   [내부] 주장={claimed} / 실제={real}");

        // 같은 사건을 다른 각도로 두 번 더 캐묻는다.
        var qRestate = InterviewQuestionFactory.Make("wolf", incident, InterviewIntent.AskWhereAtIncident);
        string again = InterviewReplyPlanner.FrameFor(qRestate).Vars.GetValueOrDefault("room", "");
        var qBefore = InterviewQuestionFactory.Make("wolf", incident, InterviewIntent.AskBeforeIncident);
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
        Deploy(new() { ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core });
        Incident(LogEventType.TaskFailed, Power, At(16));

        var board = InterviewEvidenceBoard.Build("wolf");
        var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(incident != null, "사고 기록이 자료로 뜬다")) return;

        var q = InterviewQuestionFactory.Make("wolf", incident, InterviewIntent.AskIncidentKnown);
        var frame = InterviewReplyPlanner.FrameFor(q);
        GD.Print($"   Q: {q.Text}");
        GD.Print($"   A: {InterviewReplyPlanner.Answer(q)}");
        GD.Print($"   [내부] 인지수준 variant={frame.Variant}");

        var level = DialogueContextBuilder.KnowledgeOf("wolf",
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
        Deploy(new() { ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });
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
        Deploy(new() { ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
                       ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core });
        GameState.Instance.SetSaboteur("wolf");
        Move("wolf", Vent, Storage, At(8));
        Incident(LogEventType.TaskFailed, Power, At(16));
        // 플레이어가 실제로 확보한 증거까지 잔뜩 깔아 둔다 — 그래도 자동 추궁은 없어야 한다.
        PlayerKnownEvidence.RecordCctvObservation(Storage, At(16), new[] { "wolf" });
        PlayerKnownEvidence.RecordSighting("dog", "wolf", Storage, At(16));

        bool any = false;
        foreach (string qid in new[] { DialogueQuestions.Anomaly, DialogueQuestions.Where,
                     DialogueQuestions.Suspicious, DialogueQuestions.Opinion, DialogueQuestions.Accuse })
        {
            var turn = LocalDialogueGenerator.Interview("wolf", qid);
            foreach (var f in turn.FollowUps)
            {
                GD.Print($"   [꼬리질문] {f.Text}  ({f.Intent})");
                if (f.IsChallenge) any = true;
            }
        }
        Check(!any, "추궁형 꼬리질문이 하나도 자동 생성되지 않는다");

        // 대신 플레이어가 직접 고르면 열린다.
        var board = InterviewEvidenceBoard.Build("wolf");
        var cctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        var say = board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        var claim2 = board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        GD.Print($"   자료 수: CCTV={(cctv != null ? 1 : 0)} 증언={(say != null ? 1 : 0)} 진술={(claim2 != null ? 1 : 0)}");
        if (cctv != null && claim2 != null)
        {
            var r = EvidenceContradiction.Check("wolf", claim2, cctv);
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

    // ── §5-6 : 결번자는 "설비 쪽엔 손도 안 댔다"고 한 번 정하면 끝까지 그 말을 한다 ──
    //
    // 알리바이(ClaimedRoomId)와 같은 규칙이다 — 물을 때마다 말이 달라지면 추리가 성립하지 않는다.
    private static void TestEquipmentDenial()
    {
        Head("2차-A", "결번자의 설비 접촉 부인 — 한 번 정하면 바뀌지 않는다");
        Reset();
        Deploy(new() { ["cat"] = Maintenance, ["dog"] = Guard, ["wolf"] = Maintenance,
                       ["rabbit"] = Storage, ["sheep"] = Medical, ["fox"] = Core });
        GameState.Instance.SetSaboteur("cat");
        // 고양이가 저지른 방해공작. SelectSubjectIncident 가 이걸 주제로 고른다.
        Log(LogEventType.Sabotage, "cat", Maintenance, At(40), new[] { "wolf" });

        string key = ClaimKeyOf(Maintenance, At(40));
        var claim = DialogueClaimState.Get("cat", 1, key);
        // 전략은 무작위로 정해지므로 검사에서는 Omit 으로 고정한다(§5-6 의 전제).
        claim.Mode = DeceptionMode.Omit;
        claim.ModeDecided = true;

        string first = LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Where);
        GD.Print($"   1차: {first}");
        Check(claim.EquipmentDenialDecided, "한 번 물으면 설비 접촉 여부가 정해진다");
        Check(claim.DeniesEquipmentContact, "Omit 전략이면 '설비 근처에 안 갔다'고 주장한다");
        // 어느 문장이 뽑힐지는 매번 다르므로 낱말이 아니라 **그 슬롯의 문장인지**로 본다.
        Check(EndsWithDenialLine("cat", first), "그 주장이 실제 문장으로 나간다");
        Check(PlayerKnownEvidence.BehaviorClaimsBy("cat")
                .Any(x => x.Text == KoreanDialogueComposer.EquipmentDenialText),
            "그 주장이 플레이어가 아는 자료로 남는다");

        // 두 번째 질문 — 값이 바뀌면 안 된다. 전략이 흔들려도 마찬가지다.
        claim.Mode = DeceptionMode.Minimize;
        string second = LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Where);
        GD.Print($"   2차: {second}");
        Check(claim.DeniesEquipmentContact, "두 번 물어도 주장이 뒤집히지 않는다");

        // 결백한 직원은 이 주장을 아예 하지 않는다.
        Reset();
        Deploy(new() { ["cat"] = Maintenance, ["dog"] = Guard, ["wolf"] = Maintenance,
                       ["rabbit"] = Storage, ["sheep"] = Medical, ["fox"] = Core });
        Log(LogEventType.TaskFailed, "", Maintenance, At(40));
        string innocent = LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Where);
        var clean = DialogueClaimState.Get("cat", 1, ClaimKeyOf(Maintenance, At(40)));
        GD.Print($"   결백: {innocent}");
        Check(!clean.DeniesEquipmentContact, "결백한 직원은 설비 접촉을 부인하지 않는다");
    }

    // ── §5-7 : 가짜 단서의 주인(결백한 직원)은 행동 추궁에 순순히 인정한다 ──
    //
    // EmployeeBehaviorSystem 이 만드는 가짜 단서 때문에 결백한 직원도 "설비 쪽에 오래
    // 머물렀다"는 증언의 대상이 된다. 그때 흐리면 결백한 사람이 범인처럼 보인다.
    private static void TestInnocentAdmitsBehavior()
    {
        Head("2차-B", "결백한 직원의 행동 추궁 — honest");
        Reset();
        Deploy(new() { ["cat"] = Maintenance, ["dog"] = Guard, ["wolf"] = Maintenance,
                       ["rabbit"] = Storage, ["sheep"] = Medical, ["fox"] = Core });
        // 결번자는 다른 사람이다. 고양이는 결백하지만 같은 행동이 목격됐다.
        GameState.Instance.SetSaboteur("fox");
        PlayerKnownEvidence.RecordSighting("wolf", "cat", Maintenance, At(30), Odd);
        Log(LogEventType.TaskFailed, "", Maintenance, At(40));

        var board = InterviewEvidenceBoard.Build("cat");
        var say = board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        var inc = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(say != null && inc != null, "증언과 사고 기록이 자료로 뜬다")) return;

        var r = EvidenceContradiction.Check("cat", say, inc);
        Check(r.Kind == ConfrontKind.Behavior, "행동 추궁이 성립한다");
        string answer = InterviewReplyPlanner.ConfrontAnswer("cat", r, out string variant);
        GD.Print($"   Q: {r.QuestionText}\n   A: ({variant}) {answer}");
        Check(variant == "honest", "결백한 직원은 인정한다(honest)");
    }

    // ── §3-2 마무리 : 부인 카드 + 동료 목격 증언 = 행동 추궁 ──────────────
    //
    // 결번자에게서 잡을 수 있는 유일한 거짓말이 실제로 잡히는지 끝까지 본다.
    private static void TestDenialVersusWitness()
    {
        Head("2차-C", "결번자의 부인 진술 + 동료 목격 증언 → 행동 추궁 · deny");
        Reset();
        Deploy(new() { ["cat"] = Maintenance, ["dog"] = Guard, ["wolf"] = Maintenance,
                       ["rabbit"] = Storage, ["sheep"] = Medical, ["fox"] = Core });
        GameState.Instance.SetSaboteur("cat");
        Log(LogEventType.Sabotage, "cat", Maintenance, At(40), new[] { "wolf" });

        var claim = DialogueClaimState.Get("cat", 1, ClaimKeyOf(Maintenance, At(40)));
        claim.Mode = DeceptionMode.Omit;
        claim.ModeDecided = true;

        // ① 고양이에게 물어 "설비 근처에 안 갔다" 를 받아 낸다.
        GD.Print("   A: " + LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Where));
        // ② 늑대에게 물어 "설비 쪽에 오래 머물렀다" 를 받아 낸다(여기서는 직접 넣는다).
        PlayerKnownEvidence.RecordSighting("wolf", "cat", Maintenance, At(36), Odd);

        var board = InterviewEvidenceBoard.Build("cat");
        var deny = board.FirstOrDefault(e => e.BehaviorDetail == KoreanDialogueComposer.EquipmentDenialText);
        var say = board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        if (!Check(deny != null, "부인 진술이 조사 자료 카드가 된다")) return;
        GD.Print($"   부인 카드: {deny.OneLine}");
        if (!Check(say != null, "동료 목격 증언도 자료로 있다")) return;

        var r = EvidenceContradiction.Check("cat", deny, say);
        Check(r.Kind == ConfrontKind.Behavior, "두 장을 맞대면 행동 추궁이 성립한다");
        Check(EvidenceContradiction.Check("cat", say, deny).Kind == ConfrontKind.Behavior,
            "고른 순서와 무관하다");

        string answer = InterviewReplyPlanner.ConfrontAnswer("cat", r, out string variant);
        GD.Print($"   Q: {r.QuestionText}\n   A: ({variant}) {answer}");
        Check(variant == "deny", "결번자는 물러서지 않는다(deny)");
    }

    // 목격 증언에 실리는 행동 — SaboteurPlan.TickPrecursors 가 남기는 문구 그대로.
    private const string Odd = "설비 쪽에 평소보다 오래 머물렀다";

    // 답변이 그 직원의 Denial.equipment 문장으로 끝나는가.
    private static bool EndsWithDenialLine(string employeeId, string answer)
    {
        bool formal = DialogueVoices.Get(employeeId).Formal;
        foreach (string line in DialogueLineBank.Get(employeeId, KoreanDialogueComposer.EquipmentDenialSlot, formal))
            if (answer.Contains(line)) return true;
        return false;
    }

    // 그 사건의 주장 키. DialogueContextBuilder 가 ctx.ClaimKey 로 쓰는 값과 같다.
    private static string ClaimKeyOf(string roomId, float at)
    {
        var e = EventLog.Instance.GetAllEntries()
            .FirstOrDefault(x => x.RoomId == roomId && Mathf.IsEqualApprox(x.GameTimeSeconds, at));
        return e == null ? "" : DialogueFact.From(e, KnowledgeLevel.None).Key;
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
