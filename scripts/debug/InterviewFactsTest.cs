using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 심문 1단계 — **사실·진술 일치성** 검증.
//
//   godot --headless --path . res://scenes/debug/InterviewFactsTest.tscn --quit-after 4000
//
// 보는 것은 "직원이 거짓말을 하느냐" 가 아니라, **시스템이 엉뚱한 사실을 끌어오느냐** 다.
// 의도적인 거짓말은 동기와 일관성이 있어야 하고, 자료 참조 오류는 아예 없어야 한다.
//
//   A 어제 일을 오늘 물어도 **어제 동선**으로 답하는가
//   B 끌어낼 기록이 없을 때 배치표의 방을 지어내지 않는가
//   C 기절·격리로 의식이 없던 직원을 목격자로 세우지 않는가
//   D 동석자·목격은 그 날 그 방에 실제로 있던 사람만인가
//   E 추궁 질문이 열리는 근거(재석)도 그 날짜로 판정하는가
public partial class InterviewFactsTest : Node
{
    private const string Vent = "vent_room";
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";
    private const string Medical = "medical_room";
    private const string Maintenance = "maintenance_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        SectionF();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 어제 일은 어제 동선으로 ──────────────────────────────────────
    //
    // 여우가 DAY1 에는 발전실에서 일했고 DAY2 에는 경비실로 옮겼다. DAY2 에 앉아
    // DAY1 사고를 물으면 **발전실** 이 나와야 한다. 여기서 경비실이 나오면 그건
    // 거짓말이 아니라 자료 참조 오류다(기획안 §6 첫째 규칙).

    private void SectionA()
    {
        GD.Print("\n#### A. 어제 일을 오늘 물어도 어제 동선으로 답하는가");
        Reset();

        // DAY1 — 여우는 발전실, 양은 저장고.
        Place(new() { ["fox"] = Power, ["sheep"] = Storage, ["dog"] = Core });
        Log(1, LogEventType.TaskStart, "fox", Power, 1f);
        Log(1, LogEventType.TaskStart, "sheep", Storage, 1f);
        Log(1, LogEventType.TaskStart, "dog", Core, 1f);
        Log(1, LogEventType.TaskFailed, "", Power, 600f);

        // DAY2 — 여우가 경비실로 옮겨 갔다. 오늘 배치는 경비실이다.
        NextDay();
        Place(new() { ["fox"] = Guard, ["sheep"] = Storage, ["dog"] = Core });
        Log(2, LogEventType.TaskStart, "fox", Guard, 1f);

        string day1 = DialogueContextBuilder.RoomAt("fox", 1, 600f);
        string day2 = DialogueContextBuilder.RoomAt("fox", 2, 600f);
        Ok(day1 == Power, $"DAY1 동선은 발전실이다 (실제 {RoomOf(day1)})");
        Ok(day2 == Guard, $"DAY2 동선은 경비실이다 (실제 {RoomOf(day2)})");

        // 어제 사고 자료로 만든 질문 — 날짜가 실려 있어야 한다.
        var ev = Evidence(1, EvidenceKind.Incident, "fox", Power, 600f);
        var qs = InterviewQuestionFactory.For("fox", ev);
        var where = qs.FirstOrDefault(q => q.Intent == InterviewIntent.AskWhereAtIncident);
        Ok(where != null, "어제 사고 자료로 '그때 어디 있었나' 질문이 열린다");
        Ok(where != null && where.AnchorDay == 1, $"질문이 DAY1 을 들고 있다 (실제 {where?.AnchorDay})");

        if (where != null)
        {
            string answer = InterviewReplyPlanner.Answer(where);
            GD.Print($"      답변: {answer}");
            Ok(answer.Contains(RoomOf(Power)), $"답변이 발전실을 말한다");
            Ok(!answer.Contains(RoomOf(Guard)), "오늘 배치된 경비실을 말하지 않는다");
        }

    }

    // ── B. 기록이 없으면 방을 지어내지 않는다 ───────────────────────────

    private void SectionB()
    {
        GD.Print("\n#### B. 끌어낼 기록이 없으면 배치표의 방을 대지 않는다");
        Reset();

        // 늑대는 배치만 되어 있고 그 방에 **도착한 기록이 없다**. 사고는 다른 방에서 났다.
        Place(new() { ["wolf"] = Vent, ["cat"] = Storage });
        Log(1, LogEventType.TaskStart, "cat", Storage, 1f);
        // 늑대의 입장·업무 기록을 일부러 남기지 않는다. 대신 다른 방으로 이동만 찍힌다.
        Log(1, LogEventType.RoomEnter, "wolf", Maintenance, 300f);
        Log(1, LogEventType.TaskFailed, "", Storage, 100f);

        // 100초 시점 — 늑대는 어디에도 도착한 적이 없다.
        string at = DialogueContextBuilder.RoomAt("wolf", 1, 100f);
        Ok(string.IsNullOrEmpty(at), $"그 시각 늑대의 위치 기록이 없다 (실제 '{at}')");

        var ctx = DialogueContextBuilder.Build("wolf", DialogueConversationKind.Interview,
            DialogueQuestions.Where, "", null);
        var plan = DialogueResponsePlanner.Plan(ctx);
        bool namesAssigned = plan.RoomId == Vent;
        Ok(!namesAssigned, $"배치표의 환기실을 위치로 내놓지 않는다 (plan.RoomId='{plan.RoomId}')");

        var ev = Evidence(1, EvidenceKind.Incident, "wolf", Storage, 100f);
        var q = InterviewQuestionFactory.Make("wolf", ev, InterviewIntent.AskWhereAtIncident);
        string answer = InterviewReplyPlanner.Answer(q);
        GD.Print($"      답변: {answer}");
        Ok(!answer.Contains(RoomOf(Vent)), "답변에도 환기실이 나오지 않는다");
        Ok(!answer.Contains("통로"), "빈 값을 '통로'로 둘러대지 않는다");
        Ok(answer.Length > 2 && answer != "…", "그래도 대답은 한다 (모르겠다는 취지)");
    }

    // ── C. 의식이 없던 직원은 목격자가 아니다 ───────────────────────────

    private void SectionC()
    {
        GD.Print("\n#### C. 기절·격리 중이던 직원을 목격자로 세우지 않는다");
        Reset();
        Place(new() { ["sheep"] = Power, ["wolf"] = Power, ["fox"] = Core });
        Log(1, LogEventType.TaskStart, "sheep", Power, 1f);
        Log(1, LogEventType.TaskStart, "wolf", Power, 1f);
        Log(1, LogEventType.TaskStart, "fox", Core, 1f);

        // 양이 발전실에서 쓰러진다. 그 뒤 같은 방에서 사고가 난다.
        var faint = new LogEntry
        {
            Day = 1, GameTimeSeconds = 200f, EventType = LogEventType.Neglect,
            Detail = LogDetail.Fainted, ActorEmployeeId = "sheep", RoomId = Power,
            Description = "(테스트) 기절",
        };
        EventLog.Instance.Log(faint);
        var incident = new LogEntry
        {
            Day = 1, GameTimeSeconds = 400f, EventType = LogEventType.TaskFailed,
            RoomId = Power, Description = "(테스트) 설비 사고",
        };
        EventLog.Instance.Log(incident);

        Ok(!DialogueContextBuilder.OnDutyAt("sheep", 1, 400f), "쓰러진 양은 그 시각 근무 중이 아니다");
        Ok(DialogueContextBuilder.OnDutyAt("wolf", 1, 400f), "같은 방의 늑대는 근무 중이다");

        var sheepKnows = DialogueContextBuilder.KnowledgeOf("sheep", incident);
        var wolfKnows = DialogueContextBuilder.KnowledgeOf("wolf", incident);
        Ok(sheepKnows != KnowledgeLevel.Direct,
            $"의식이 없던 양은 사고를 직접 본 것으로 치지 않는다 (실제 {sheepKnows})");
        Ok(wolfKnows == KnowledgeLevel.Direct, $"깨어 있던 늑대는 직접 목격이다 (실제 {wolfKnows})");

        // 로그가 목격자로 명시했다면 그건 존중한다 — 지어낸 목격이 아니라 기록이다.
        var named = new LogEntry
        {
            Day = 1, GameTimeSeconds = 410f, EventType = LogEventType.TaskFailed,
            RoomId = Power, Description = "(테스트) 목격자 명시",
            WitnessEmployeeIds = new List<string> { "sheep" },
        };
        EventLog.Instance.Log(named);
        Ok(DialogueContextBuilder.KnowledgeOf("sheep", named) == KnowledgeLevel.Direct,
            "로그가 목격자로 적어 둔 경우는 그대로 인정한다");
    }

    // ── D. 동석자는 그 날 그 방 사람만 ──────────────────────────────────

    private void SectionD()
    {
        GD.Print("\n#### D. 동석자·목격은 그 날 그 방에 실제로 있던 사람만");
        Reset();

        // DAY1 저장고에는 고양이만. DAY2 저장고에는 강아지만.
        Place(new() { ["cat"] = Storage, ["dog"] = Core });
        Log(1, LogEventType.TaskStart, "cat", Storage, 1f);
        Log(1, LogEventType.TaskStart, "dog", Core, 1f);
        NextDay();
        Place(new() { ["cat"] = Core, ["dog"] = Storage });
        Log(2, LogEventType.TaskStart, "cat", Core, 1f);
        Log(2, LogEventType.TaskStart, "dog", Storage, 1f);

        var day1 = DialogueContextBuilder.OccupantsAt(Storage, 1, 500f, "fox");
        var day2 = DialogueContextBuilder.OccupantsAt(Storage, 2, 500f, "fox");
        Ok(day1.Contains("cat") && !day1.Contains("dog"),
            $"DAY1 저장고에는 고양이뿐 ({string.Join(",", day1)})");
        Ok(day2.Contains("dog") && !day2.Contains("cat"),
            $"DAY2 저장고에는 강아지뿐 ({string.Join(",", day2)})");

        // 근무하지 않은 직원은 아무 방에서도 동석자가 되지 않는다.
        var ghost = DialogueContextBuilder.OccupantsAt(Vent, 1, 500f, "");
        Ok(ghost.Count == 0, $"아무도 없던 방에 사람을 세우지 않는다 ({ghost.Count}명)");

    }

    // ── E. 추궁 근거도 날짜를 지킨다 ────────────────────────────────────

    private void SectionE()
    {
        GD.Print("\n#### E. '기록상 거기 있었다' 판정도 그 날짜로 한다");
        Reset();

        // DAY1 토끼는 정비실, DAY2 토끼는 발전실. DAY1 발전실 사고를 묻는다.
        Place(new() { ["rabbit"] = Maintenance });
        Log(1, LogEventType.TaskStart, "rabbit", Maintenance, 1f);
        NextDay();
        Place(new() { ["rabbit"] = Power });
        Log(2, LogEventType.TaskStart, "rabbit", Power, 1f);

        var day1Incident = Evidence(1, EvidenceKind.Incident, "", Power, 500f);
        bool knows = InterviewQuestionFactory.PlayerKnowsPresence("rabbit", day1Incident);
        Ok(!knows, "DAY1 발전실 사고에 대해 '토끼가 거기 있었다'로 판정하지 않는다");

        var day2Incident = Evidence(2, EvidenceKind.Incident, "", Power, 500f);
        Ok(InterviewQuestionFactory.PlayerKnowsPresence("rabbit", day2Incident),
            "DAY2 같은 방 사고에 대해서는 재석으로 판정한다");

    }

    // ── F. 모순 판정의 세 날짜 ──────────────────────────────────────────
    //
    // 날짜가 셋 등장한다. 섞으면 안 된다.
    //   발언 날짜    — 직원이 실제로 그 말을 한 근무일
    //   대상 사건 날짜 — 그 말이 가리키는 근무일
    //   증거 사건 날짜 — 제시한 증거가 실제로 일어난 근무일
    // 모순은 **대상 사건 날짜 ↔ 증거 사건 날짜** 로만 따진다. 언제 말했는지는 상관없다.

    private void SectionF()
    {
        GD.Print("\n#### F. 모순 판정 — 대상 사건 날짜와 증거 날짜를 맞댄다");
        Reset();

        // ① 당일 발언 ↔ 당일 증거 — 평소대로 성립한다.
        var claimToday = Claim("fox", day: 1, subjectDay: 1, Guard, 500f);
        var cctvToday = Cctv("fox", day: 1, Power, 500f);
        var r1 = EvidenceContradiction.Check("fox", claimToday, cctvToday);
        Ok(r1.IsContradiction && r1.Kind == ConfrontKind.Location,
            $"① 당일 발언 ↔ 당일 증거: 모순 성립 ({r1.Kind})");
        Ok(r1.SubjectDay == 1, $"   판정이 DAY1 의 일로 기록된다 (실제 {r1.SubjectDay})");

        // ② 어제 사건에 대한 **오늘** 발언 ↔ 어제 증거 — 발언 날짜가 달라도 성립해야 한다.
        NextDay();
        var claimMadeToday = Claim("fox", day: 2, subjectDay: 1, Guard, 500f);
        var r2 = EvidenceContradiction.Check("fox", claimMadeToday, cctvToday);
        Ok(r2.IsContradiction,
            $"② DAY2 에 한 'DAY1 에는 경비실' 진술 ↔ DAY1 CCTV: 모순 성립 ({r2.Kind})");
        Ok(r2.SubjectDay == 1, $"   말한 날(2)이 아니라 가리키는 날(1)로 기록된다 (실제 {r2.SubjectDay})");

        // ③ DAY3 에서 DAY1 에 대한 DAY2 발언을 추궁한다 — 세 날짜가 모두 다른 경우.
        NextDay();
        Ok(GameState.Instance.CurrentDay == 3, "지금은 DAY3");
        var r3 = EvidenceContradiction.Check("fox", claimMadeToday, cctvToday);
        Ok(r3.IsContradiction, $"③ DAY3 에서 추궁해도 성립한다 ({r3.Kind})");
        Ok(r3.SubjectDay == 1, $"   여전히 DAY1 의 일로 판정한다 (실제 {r3.SubjectDay})");
        string answer = InterviewReplyPlanner.ConfrontAnswer("fox", r3, out string variant);
        GD.Print($"      답변({variant}): {answer}");
        Ok(!string.IsNullOrEmpty(answer) && answer != "…", "   추궁에 답변이 나온다");

        // ④ 날짜만 다른 동일 시각·동일 방 사건은 서로 다른 사건이다.
        //    사건 키에 날짜가 없으므로(Type:Room:Time) 여기서 걸러 내지 못하면 한 사건이 된다.
        var incidentDay1 = Incident(1, Power, 500f);
        var incidentDay2 = Incident(2, Power, 500f);
        Ok(incidentDay1.IncidentKey == incidentDay2.IncidentKey,
            "④ 사건 키는 두 날이 같다 (Type:Room:Time 에 날짜가 없다)");
        Ok(incidentDay1.SubjectDay != incidentDay2.SubjectDay,
            "   그래도 가리키는 날이 다르다");
        var r4 = EvidenceContradiction.Check("fox", claimToday, incidentDay2);
        Ok(!r4.IsContradiction,
            $"   DAY1 진술 ↔ DAY2 사고는 모순이 아니다 ({r4.Kind})");
        Ok(r4.Notice.Contains("다른 근무"), $"   이유를 밝힌다: {r4.Notice}");

        // 같은 날끼리는 그대로 성립한다 — ④ 때문에 멀쩡한 판정까지 막히면 안 된다.
        var r4b = EvidenceContradiction.Check("fox", claimToday, incidentDay1);
        Ok(r4b.IsContradiction || r4b.Kind != ConfrontKind.None || r4b.Notice.Contains("직접적인"),
            "   같은 DAY1 끼리는 날짜를 이유로 막지 않는다");

        // ⑤ 서로 다른 날의 자료로 잘못된 모순이 생기지 않는다.
        //    DAY1 경비실 / DAY2 발전실 — 같은 시각이지만 다른 날이므로 충돌이 아니다.
        var day1Guard = Cctv("fox", day: 1, Guard, 500f);
        var day2Power = Cctv("fox", day: 2, Power, 500f);
        var r5 = EvidenceContradiction.Check("fox", day1Guard, day2Power);
        Ok(!r5.IsContradiction, $"⑤ 날이 다른 두 위치 기록은 모순이 아니다 ({r5.Kind})");

        // 반대로 같은 날의 서로 다른 방은 모순이다.
        var day1Power = Cctv("fox", day: 1, Power, 500f);
        var r5b = EvidenceContradiction.Check("fox", day1Guard, day1Power);
        Ok(r5b.IsContradiction && r5b.Kind == ConfrontKind.Location,
            $"   같은 날 다른 방이면 모순이다 ({r5b.Kind})");
    }

    // 진술 카드 — day 는 말한 날, subjectDay 는 그 말이 가리키는 날.
    private static InterviewEvidence Claim(string who, int day, int subjectDay, string room, float at)
        => new()
        {
            Id = $"claim:{who}:{subjectDay}:{at}", Kind = EvidenceKind.OwnStatement,
            Day = day, SubjectDay = subjectDay,
            SubjectEmployeeId = who, SpeakerEmployeeId = who,
            SubjectRoomId = room, Position = PositionClaim.AtRoom,
            AnchorTime = at, HasTime = true,
            Header = "진술", Body = $"본인 · {InterviewEvidenceBoard.RoomName(room)}에 있었다",
        };

    // CCTV 기록 — 기록은 만들어진 날과 가리키는 날이 같다.
    private static InterviewEvidence Cctv(string who, int day, string room, float at)
        => new()
        {
            Id = $"cctv:{who}:{day}:{room}:{at}", Kind = EvidenceKind.Cctv,
            Day = day,
            SubjectEmployeeId = who, SubjectRoomId = room, Position = PositionClaim.AtRoom,
            AnchorTime = at, HasTime = true,
            Header = "CCTV", Body = InterviewEvidenceBoard.RoomName(room),
        };

    private static InterviewEvidence Incident(int day, string room, float at)
        => new()
        {
            Id = $"incident:{day}:{room}:{at}", Kind = EvidenceKind.Incident,
            Day = day, SubjectRoomId = room,
            AnchorTime = at, HasTime = true,
            IncidentKey = $"TaskFailed:{room}:{at:0.0}",
            IncidentType = LogEventType.TaskFailed,
            Header = "시설 로그", Body = "설비 사고",
        };

    // ── 도우미 ──────────────────────────────────────────────────────────

    private static void Reset()
    {
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        GameState.Instance?.ResetRun(1);
        EventLog.Instance.ClearAll();
    }

    // DAY1 → DAY2 로 넘기면 시설 로그는 지워지지 않는다(DAY0 를 벗어날 때만 비운다).
    // 그래서 어제 자료를 오늘 다시 묻는 상황이 실제로 성립한다.
    private static void NextDay() => GameState.Instance?.GoToNextDay();

    private static void Place(Dictionary<string, string> rooms)
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
            st.Incapacitated = false;
        }
        sim.RecordShiftStart();
    }

    private static void Log(int day, LogEventType type, string actor, string roomId, float at)
        => EventLog.Instance.Log(new LogEntry
        {
            Day = day, GameTimeSeconds = at, EventType = type,
            ActorEmployeeId = actor, RoomId = roomId, Description = "(테스트)",
            WitnessEmployeeIds = new List<string>(),
        });

    private static InterviewEvidence Evidence(int day, EvidenceKind kind, string subject,
                                              string roomId, float at)
        => new()
        {
            Id = $"test:{kind}:{day}:{at}", Kind = kind, Day = day,
            SubjectEmployeeId = subject, SubjectRoomId = roomId,
            AnchorTime = at, HasTime = true,
            IncidentKey = kind == EvidenceKind.Incident ? $"incident:TaskFailed:{roomId}:{at / 60f:0.0}" : "",
            IncidentType = LogEventType.TaskFailed,
            Header = "시설 로그", Body = "(테스트)",
        };

    private static string RoomOf(string roomId) => InterviewEvidenceBoard.RoomName(roomId);

    private void Ok(bool cond, string what)
    {
        if (cond) { _pass++; GD.Print($"  [PASS] {what}"); }
        else { _fail++; GD.Print($"  [FAIL] {what}"); }
    }
}
