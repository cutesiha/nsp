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
        // 3차 — 통화 기록 · 이상 개체 조우 · 알리바이 따지기(질문 확장).
        TestIdleCallNeglect();
        TestGhostEncounter();
        TestAlibiQuestions();
        // 4차 — 사고 인지는 "그 순간 위치"가 아니라 "근무 중 겪은 것"이다(C).
        TestLearnedLater();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── 4차 : 사고가 난 뒤 그 방에 가 본 직원은 "몰랐다"고 하지 않는다 ──────────
    //
    // 토끼 경비실 배치 → 12분 발전실 고장(그 순간 토끼는 발전실을 알 길이 없다)
    // → 30분 관리자가 발전실로 재배치, 토끼가 수리 → 60분 경비실 복귀.
    //
    // 예전에는 "그 순간 어느 방에 있었나" 하나로만 인지를 판단해서, 직접 가서 고친 사람이
    // "그런 일이 있었어요? 처음 들어요!" 라고 답했다. 동선 답변도 하루 전체가 아니라
    // 그 순간만 보면 "쭉 경비실에 있었다"가 되어 재배치 기록과 어긋났다.
    private static void TestLearnedLater()
    {
        Head("4차", "나중에 가서 고친 사고 — 알고는 있다 · 그때는 다른 방에 있었다");
        Reset();
        Deploy(new() { ["rabbit"] = Guard, ["cat"] = Storage, ["dog"] = Core,
                       ["fox"] = Medical, ["sheep"] = Maintenance, ["wolf"] = Vent });
        Incident(LogEventType.TaskFailed, Power, At(12));          // 그 순간 토끼는 경비실
        Log(LogEventType.Relocation, "rabbit", Power, At(29));     // 관리자 지시
        Move("rabbit", Guard, Power, At(30));
        Log(LogEventType.TaskStart, "rabbit", Power, At(31));      // 🔧 로 시작해야 수리로 읽힌다
        Move("rabbit", Power, Guard, At(60));                      // 다시 경비실로
        MarkRepair(At(31));
        ShiftMemory.Invalidate();

        string guard = InterviewEvidenceBoard.RoomName(Guard);
        string power = InterviewEvidenceBoard.RoomName(Power);

        // ① 사고 인지 — 그 순간엔 몰랐지만 그 뒤에 가서 고쳤다.
        var level = DialogueContextBuilder.KnowledgeOf("rabbit",
            DialogueContextBuilder.FindByKey(1, IncidentKeyAt(Power, At(12))));
        Check(level == KnowledgeLevel.Later, $"인지수준 = Later ({level})");

        var board = InterviewEvidenceBoard.Build("rabbit");
        var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(incident != null, "사고 기록이 자료로 뜬다")) return;

        var qKnown = InterviewQuestionFactory.Make("rabbit", incident, InterviewIntent.AskIncidentKnown);
        var fKnown = InterviewReplyPlanner.FrameFor(qKnown);
        string aKnown = InterviewReplyPlanner.Answer(qKnown);
        GD.Print($"   Q: {qKnown.Text}\n   A: {aKnown}");
        Check(fKnown.Variant == "later", $"IncidentKnown = later ({fKnown.Variant})");
        Check(aKnown.Contains(guard) && aKnown.Contains(power),
            $"답에 '{guard}'(그때 있던 곳) 과 '{power}'(사고 난 곳) 이 함께 나온다");

        // ①-2 기본 질문("이상한 일 있었나")도 같은 자리를 짚는다 — 사고를 말하되 장면·소리는 말하지 않는다.
        var planQ1 = DialogueResponsePlanner.Plan(DialogueContextBuilder.Build("rabbit",
            DialogueConversationKind.Interview, DialogueQuestions.Anomaly, "", null));
        string aQ1 = LocalDialogueGenerator.InterviewAnswer("rabbit", DialogueQuestions.Anomaly);
        GD.Print($"   A(이상한 일): {aQ1}");
        Check(planQ1.Core == CoreKind.IncidentLater, $"Q1 핵심 = IncidentLater ({planQ1.Core})");
        Check(aQ1.Contains(guard) && aQ1.Contains(power), "Q1 답에도 두 작업실이 함께 나온다");

        // ② 이동 이유 — 관리자가 보낸 것이므로 "지시" 다.
        var move = board.Where(e => e.Kind == EvidenceKind.Movement && e.ToRoomId == Power)
            .OrderBy(e => e.AnchorTime).FirstOrDefault();
        if (!Check(move != null && move.PlayerOrdered, "발전실行 이동이 '지시' 로 기록된다")) return;
        var qMove = InterviewQuestionFactory.Make("rabbit", move, InterviewIntent.AskMoveReason);
        var fMove = InterviewReplyPlanner.FrameFor(qMove);
        GD.Print($"   Q: {qMove.Text}\n   A: {InterviewReplyPlanner.Answer(qMove)}");
        Check(fMove.Variant == "ordered", $"MoveReason = ordered ({fMove.Variant})");

        // ③ 동선 — 하루 전체의 재배치 기록을 본다. "쭉 경비실" 이 나오면 안 된다.
        var qRoute = InterviewQuestionFactory.Make("rabbit", move, InterviewIntent.AskRouteAround);
        var fRoute = InterviewReplyPlanner.FrameFor(qRoute);
        string aRoute = InterviewReplyPlanner.Answer(qRoute);
        GD.Print($"   Q: {qRoute.Text}\n   A: {aRoute}");
        Check(fRoute.Variant == "full", $"RouteAround = full ({fRoute.Variant})");

        var qNext = InterviewQuestionFactory.Make("rabbit", move, InterviewIntent.AskNextLocation);
        var fNext = InterviewReplyPlanner.FrameFor(qNext);
        string aNext = InterviewReplyPlanner.Answer(qNext);
        GD.Print($"   Q: {qNext.Text}\n   A: {aNext}");
        Check(fNext.Variant == "moved", $"NextLocation = moved ({fNext.Variant})");

        var qAgain = InterviewQuestionFactory.Make("rabbit", incident, InterviewIntent.AskRestate);
        string aAgain = InterviewReplyPlanner.Answer(qAgain);
        GD.Print($"   A(다시): {aAgain}");

        // "쭉 · 계속 경비실에 있었다" 는 어느 답에도 없어야 한다.
        // NextLocation.stayed / RouteAround.short 의 문구들이다 — 재배치 기록이 있는 사람에게
        // 이 말이 나오면 시설 로그와 정면으로 어긋난다.
        // ("그대로입니다"(Restate.same)는 '진술이 그대로'라는 뜻이라 여기 해당하지 않는다.)
        string[] all = { aKnown, aRoute, aNext, aAgain };
        string[] stay = { "쭉", "계속", "내내", "안 움직", "움직이지 않", "에만 있" };
        var slip = all.FirstOrDefault(a => stay.Any(a.Contains));
        Check(slip == null, $"어떤 답에도 '쭉 · 계속 {guard}' 가 없다{(slip == null ? "" : " → " + slip)}");
    }

    // 그 시각 그 방 사고의 주장 키.
    private static string IncidentKeyAt(string roomId, float at) => ClaimKeyOf(roomId, at);

    // 방금 남긴 업무 시작 기록을 수리(🔧)로 바꾼다 — FacilityLogFormatter 와 같은 규약이다.
    private static void MarkRepair(float at)
    {
        var e = EventLog.Instance.GetAllEntries()
            .LastOrDefault(x => x.EventType == LogEventType.TaskStart && Mathf.IsEqualApprox(x.GameTimeSeconds, at));
        if (e != null) e.Description = "🔧 " + e.Description + " / 설비 고장 수리 시작";
    }

    // ── 3차-A : 놀러 가고 싶다는 전화가 잦으면 근무 태만을 따질 수 있다 ──────────
    private static void TestIdleCallNeglect()
    {
        Head("3차-A", "여우가 놀러 가고 싶다고 두 번 전화 → 통화 자료 두 장 · 근무 태만 추궁");
        Reset();
        Deploy(new() { ["fox"] = Guard, ["cat"] = Storage, ["dog"] = Core,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["wolf"] = Vent });
        CallMemoryLog.RecordAt(1, At(12), "fox", CallRecordKind.Reported, Storage, DialogueRepository.EventIdleVisit);
        CallMemoryLog.RecordAt(1, At(30), "fox", CallRecordKind.Reported, Core, DialogueRepository.EventIdleVisit);

        var board = InterviewEvidenceBoard.Build("fox");
        var calls = board.Where(e => e.Kind == EvidenceKind.Call).OrderBy(e => e.AnchorTime).ToList();
        if (!Check(calls.Count == 2, $"통화 자료가 두 장 뜬다 ({calls.Count})")) return;
        foreach (var c in calls) GD.Print($"   자료: [{c.Header}] {c.OneLine}");
        Check(calls[1].Body.Contains("2번째"), "두 번째 통화에는 횟수가 적힌다");
        Check(calls[0].SubjectRoomId == Guard && calls[0].CanAnchorPosition, "전화한 순간 있던 방(경비실)이 재석 근거가 된다");
        Check(InterviewEvidenceBoard.Build("cat").All(e => e.Kind != EvidenceKind.Call), "다른 직원의 노트에는 여우의 통화가 없다");

        var qs = InterviewQuestionFactory.For("fox", calls[1]);
        foreach (var q in qs) GD.Print($"   Q: {q.Text}  ({q.Intent}{(q.IsConfront ? " · 따짐" : "")})");
        var neglect = qs.FirstOrDefault(q => q.Intent == InterviewIntent.ConfrontNeglect);
        Check(neglect != null && neglect.Text.Contains("2번") && neglect.IsConfront, "근무 태만 추궁이 열리고 횟수를 말한다");
        Check(qs.Any(q => q.Intent == InterviewIntent.AskCallReason && q.Text.Contains("코어실")), "전화 이유 질문이 그 통화가 말한 작업실을 인용한다");
        Check(qs.All(q => q.Intent != InterviewIntent.FollowExactTime), "몇 시였냐는 질문은 없다");
        if (neglect == null) return;

        string a = InterviewReplyPlanner.Answer(neglect);
        GD.Print($"   A(태만): {a}");
        Check(!string.IsNullOrEmpty(a) && a != "…", "추궁에 답이 돌아온다");
        Check(InterviewReplyPlanner.FrameFor(neglect).Variant == "justify",
            $"자리를 비운 기록이 없으면 항변한다 ({InterviewReplyPlanner.FrameFor(neglect).Variant})");
        foreach (var q in qs.Where(q => q.Intent != InterviewIntent.ConfrontNeglect))
        {
            string ans = InterviewReplyPlanner.Answer(q);
            GD.Print($"   A({q.Intent}): {ans}");
            Check(!string.IsNullOrEmpty(ans) && ans != "…", $"{q.Intent} 에 답이 있다");
        }

        // 지시 없이 실제로 옮긴 기록이 화면에 떴으면 발뺌하지 못한다.
        Move("fox", Guard, Storage, At(14));
        InterviewReplyPlanner.Reset();
        var neglect2 = InterviewQuestionFactory.Make("fox", calls[1], InterviewIntent.ConfrontNeglect);
        GD.Print($"   A(태만·이탈 기록 있음): {InterviewReplyPlanner.Answer(neglect2)}");
        Check(InterviewReplyPlanner.FrameFor(neglect2).Variant == "caught", "지시 없는 이동 기록이 있으면 인정한다");

        // 한 번뿐이면 태만 추궁은 없다.
        Reset();
        Deploy(new() { ["fox"] = Guard, ["cat"] = Storage });
        CallMemoryLog.RecordAt(1, At(12), "fox", CallRecordKind.Reported, Storage, DialogueRepository.EventIdleVisit);
        var one = InterviewEvidenceBoard.Build("fox").First(e => e.Kind == EvidenceKind.Call);
        Check(InterviewQuestionFactory.For("fox", one).All(q => q.Intent != InterviewIntent.ConfrontNeglect),
            "한 번 물어본 것으로는 태만을 따지지 않는다");
    }

    // ── 3차-B : 이상 개체를 마주친 직원에게 안부 · 생김새 · 행동 · 동료를 묻는다 ────
    private static void TestGhostEncounter()
    {
        Head("3차-B", "코어실 개체 관측 소멸 · 양과 늑대가 그 방에 있었다 → 개체 자료 · 질문 · 증언");
        Reset();
        Deploy(new() { ["sheep"] = Core, ["wolf"] = Core, ["cat"] = Storage, ["dog"] = Guard,
                       ["rabbit"] = Maintenance, ["fox"] = Vent });
        Log(LogEventType.AnomalyDispelled, "", Core, At(20));

        var sheep = InterviewEvidenceBoard.Build("sheep").FirstOrDefault(e => e.Kind == EvidenceKind.Anomaly);
        if (!Check(sheep != null, "양의 조사 노트에 개체 조우 자료가 뜬다")) return;
        GD.Print($"   자료: [{sheep.Header}] {sheep.OneLine}");
        Check(sheep.RelatedEmployeeIds.Contains("wolf"), "같이 있던 늑대가 자료에 실린다");
        Check(sheep.SubjectRoomId == Core && sheep.CanAnchorPosition, "코어실 · 그 시각의 재석 근거다");
        Check(InterviewEvidenceBoard.Build("cat").All(e => e.Kind != EvidenceKind.Anomaly), "다른 방에 있던 고양이에게는 없다");

        var qs = InterviewQuestionFactory.For("sheep", sheep);
        foreach (var q in qs) GD.Print($"   Q: {q.Text}  ({q.Intent})");
        Check(qs.Count == 4, $"괜찮은가 · 생김새 · 무엇을 했나 · 같이 있던 사람 — 네 질문 ({qs.Count})");
        foreach (var q in qs)
        {
            string a = InterviewReplyPlanner.Answer(q);
            GD.Print($"   A({q.Intent}): {a}");
            Check(!string.IsNullOrEmpty(a) && a != "…", $"{q.Intent} 에 답이 있다");
        }
        var well = qs.FirstOrDefault(q => q.Intent == InterviewIntent.AskGhostWellbeing);
        Check(well != null && InterviewReplyPlanner.FrameFor(well).Variant == "shaken", "겁 많은 양은 아직 떨고 있다");
        var others = qs.FirstOrDefault(q => q.Intent == InterviewIntent.AskGhostOthers);
        Check(others != null && others.Text.Contains("늑대"), "같이 있던 사람 질문이 늑대를 부른다");
        var say = InterviewEvidenceBoard.Build("wolf")
            .FirstOrDefault(e => e.Kind == EvidenceKind.Testimony && e.SpeakerEmployeeId == "sheep");
        Check(say != null && !string.IsNullOrEmpty(say.BehaviorDetail), $"양의 답이 늑대에 대한 증언 카드가 된다 — {say?.Body}");

        var wolfEv = InterviewEvidenceBoard.Build("wolf").FirstOrDefault(e => e.Kind == EvidenceKind.Anomaly);
        var wolfQ = InterviewQuestionFactory.Make("wolf", wolfEv, InterviewIntent.AskGhostWellbeing);
        GD.Print($"   A(늑대·안부): {InterviewReplyPlanner.Answer(wolfQ)}");
        Check(InterviewReplyPlanner.FrameFor(wolfQ).Variant == "ok", "늑대는 담담하다");
    }

    // ── 3차-C : 사고 기록에는 알리바이를 증명하라 · 무관 근거를 대라 · 지목하라 · 재석을 따진다 ──
    private static void TestAlibiQuestions()
    {
        Head("3차-C", "사고 기록 → 알리바이 증명 · 무관 근거 · 지목 · 기록상 재석 따지기");
        Reset();
        Deploy(new() { ["cat"] = Power, ["dog"] = Power, ["wolf"] = Vent,
                       ["rabbit"] = Maintenance, ["sheep"] = Medical, ["fox"] = Core });
        Incident(LogEventType.TaskFailed, Power, At(40));

        var inc = InterviewEvidenceBoard.Build("cat").First(e => e.Kind == EvidenceKind.Incident);
        var qs = InterviewQuestionFactory.For("cat", inc);
        foreach (var q in qs) GD.Print($"   Q: {q.Text}  ({q.Intent}{(q.IsConfront ? " · 따짐" : "")})");
        Check(qs.Any(q => q.Intent == InterviewIntent.AskAlibiProof), "알리바이 증명 요구가 있다");
        Check(qs.Any(q => q.Intent == InterviewIntent.AskProveInnocence && q.IsConfront), "무관하다는 근거를 대라는 추궁이 있다");
        Check(qs.Any(q => q.Intent == InterviewIntent.AskSuspectOpinion), "누가 그랬다고 보느냐는 질문이 있다");
        Check(qs.Any(q => q.Intent == InterviewIntent.PressPresence), "기록상 발전실에 있던 고양이에게는 재석 추궁이 열린다");
        Check(qs.All(q => q.Intent != InterviewIntent.FollowExactTime && !q.Text.Contains("몇 시")), "몇 시였냐는 질문은 없다");
        Check(InterviewQuestionFactory.For("wolf", inc).All(q => q.Intent != InterviewIntent.PressPresence),
            "환기실에 있던 늑대에게는 재석 추궁이 없다");

        var alibi = qs.First(q => q.Intent == InterviewIntent.AskAlibiProof);
        string a1 = InterviewReplyPlanner.Answer(alibi);
        GD.Print($"   A(증명): {a1}");
        Check(InterviewReplyPlanner.FrameFor(alibi).Variant == "witness" && a1.Contains("강아지"), "같이 있던 강아지를 증인으로 댄다");
        Check(PlayerKnownEvidence.SightingsOf("dog").Any(s => s.SpeakerId == "cat" && s.RoomId == Power),
            "그 말이 강아지에 대한 목격 증언으로 남는다");

        var press = qs.First(q => q.Intent == InterviewIntent.PressPresence);
        string a2 = InterviewReplyPlanner.Answer(press);
        GD.Print($"   A(재석): {a2}");
        Check(InterviewReplyPlanner.FrameFor(press).Variant == "admit", "결백한 직원은 거기 있었음을 인정한다");

        var suspect = qs.First(q => q.Intent == InterviewIntent.AskSuspectOpinion);
        string a4 = InterviewReplyPlanner.Answer(suspect);
        GD.Print($"   A(지목): {a4}");
        Check(InterviewReplyPlanner.FrameFor(suspect).Variant == "none", "본 것이 없으면 아무도 지목하지 않는다");

        // 결번자 — 무관 근거를 대라고 하면 부정하거나 흐린다.
        GameState.Instance.SetSaboteur("cat");
        InterviewReplyPlanner.Reset();
        DialogueClaimState.ResetAll();
        var prove = InterviewQuestionFactory.Make("cat", inc, InterviewIntent.AskProveInnocence);
        string a3 = InterviewReplyPlanner.Answer(prove);
        GD.Print($"   A(결번자·무관 근거): {a3}");
        var fr = InterviewReplyPlanner.FrameFor(prove);
        Check(fr.Variant is "deny" or "evasive", $"결번자는 부정하거나 흐린다 ({fr.Variant})");
        // 지시 없는 이동은 근무지 이탈로 따질 수 있다.
        Move("cat", Power, Storage, At(50));
        var mv = InterviewEvidenceBoard.Build("cat").First(e => e.Kind == EvidenceKind.Movement);
        var mq = InterviewQuestionFactory.For("cat", mv);
        foreach (var q in mq) GD.Print($"   Q(이동): {q.Text}  ({q.Intent}{(q.IsConfront ? " · 따짐" : "")})");
        Check(mq.Any(q => q.Intent == InterviewIntent.ConfrontUnorderedMove && q.Text.Contains("근무지 이탈")),
            "지시 없는 이동에는 근무지 이탈 추궁이 열린다");
        Check(mq.All(q => q.Intent != InterviewIntent.AskNextLocation), "그 뒤 어디로 갔냐는 질문은 기본 목록에서 빠졌다(꼬리질문으로만)");
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
        string expected = level switch
        {
            KnowledgeLevel.Direct => "direct",
            KnowledgeLevel.Indirect => "indirect",
            KnowledgeLevel.Later => "later",
            _ => "none",
        };
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
        CallMemoryLog.ResetAll();
        InterviewReplyPlanner.Reset();
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
