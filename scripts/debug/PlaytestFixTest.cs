using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 플레이테스트에서 나온 치명적 문제 5건의 회귀 방지 테스트.
//
//   godot --headless --path . scenes/debug/PlaytestFixTest.tscn --quit-after 900
//
//   ① 휴게시간에 "오늘 근무는 어땠습니까?" 가 현재 상태(이동 중)를 답하지 않는다
//   ② 같은 질문을 두 번 해도 주장이 바뀌지 않는다
//   ③ 기분과 그 이유가 의미적으로 어긋나지 않는다
//   ④ 같은 방에 있었다고 범인을 자동으로 특정하지 않는다
//   ⑤ 필수 업무 미달성이 상태로 남는다
public partial class PlaytestFixTest : Node
{
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        if (FacilitySimulation.Instance == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 플레이테스트 수정 검증 ################");
        TestShiftReview();
        TestRepeatSameClaim();
        TestMoodConsistency();
        TestWitnessIdentification();
        TestMissedObjectives();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── ① 휴게시간 회고가 현재 상태를 답하지 않는다 ────────────────────
    private void TestShiftReview()
    {
        GD.Print("\n===== ① 휴게시간 '오늘 근무는 어땠습니까?' =====");
        Setup();
        // 일부러 이동 중 상태로 만들어 둔다 — 예전에는 여기서 "가고 있어요" 가 나왔다.
        foreach (string id in new[] { "cat", "fox" })
        {
            var st = FacilitySimulation.Instance.GetEmployeeState(id);
            st.IsMoving = true;
            st.TargetRoomId = Power;
        }
        GameState.Instance.SetPhase(GamePhase.Rest);

        bool moving = false;
        foreach (string id in new[] { "cat", "fox", "owl", "crow" })
        {
            string a = Answer(id, DialogueQuestions.ShiftReview);
            GD.Print($"   {Code(id),-4} : {a}");
            if (a.Contains("가고 있") || a.Contains("이동 중") || a.Contains("가는 중")) moving = true;
        }
        Check(!moving, "① 휴게시간 회고가 '가고 있어요' 로 답하지 않는다");

        // 실시간 통화(GEN_STATUS)는 지금 상태를 그대로 답해야 한다 — 그쪽은 건드리지 않았다.
        GameState.Instance.SetPhase(GamePhase.Live);
        string live = Answer("cat", DialogueQuestions.GeneralStatus);
        GD.Print($"   (실시간 통화) 고양이 : {live}");
        Check(!string.IsNullOrEmpty(live), "① 실시간 통화 답변은 그대로 동작한다");
    }

    // ── ② 반복 질문 — 표현만 달라지고 주장은 같다 ──────────────────────
    private void TestRepeatSameClaim()
    {
        GD.Print("\n===== ② 같은 질문을 두 번 =====");
        Setup();
        Incident(LogEventType.TaskFailed, Power, At(40));
        GameState.Instance.SetPhase(GamePhase.Rest);

        bool ok = true;
        foreach (string id in new[] { "cat", "owl", "rabbit" })
        {
            var ctx1 = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview,
                DialogueQuestions.Where, "", null);
            var p1 = DialogueResponsePlanner.Plan(ctx1);
            string a1 = KoreanDialogueComposer.Compose(ctx1, p1);

            var ctx2 = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview,
                DialogueQuestions.Where, "", null);
            ctx2.IsRepeat = true;
            var p2 = DialogueResponsePlanner.Plan(ctx2);
            string a2 = KoreanDialogueComposer.Compose(ctx2, p2);

            GD.Print($"   {Code(id),-4} : {a1}\n        ↳ {a2}");
            // 핵심 주장(어느 방에 있었다)이 같아야 한다.
            if (p1.RoomId != p2.RoomId) ok = false;
        }
        Check(ok, "② 반복 질문에서도 주장하는 위치가 같다");
    }

    // ── ③ 기분과 이유가 어긋나지 않는다 ────────────────────────────────
    private void TestMoodConsistency()
    {
        GD.Print("\n===== ③ 오늘의 기분 ↔ 이유 =====");
        Setup();
        Incident(LogEventType.TaskFailed, Storage, At(40));
        GameState.Instance.SetPhase(GamePhase.Rest);

        Check(MoodTones.Of("여유로움") == MoodTone.Calm && MoodTones.Of("불안함") == MoodTone.Uneasy
              && MoodTones.Of("그냥 그럼") == MoodTone.Neutral, "③ 기분 분류가 맞다");

        // 긍정 기분인데 사고 탓을 하면 안 된다.
        var sim = FacilitySimulation.Instance;
        bool calmOk = true, uneasyLinked = false;
        foreach (string id in new[] { "fox", "cat", "owl" })
        {
            sim.GetEmployeeState(id).DailyMood = "여유로움";
            string a = MoodAnswer(id, "여유로움");
            GD.Print($"   {Code(id),-4} (여유로움) : {a}");
            if (a.Contains("사고") || a.Contains("저장고") || a.Contains("신경 쓰여")) calmOk = false;
        }
        Check(calmOk, "③ 긍정 기분은 사고를 이유로 대지 않는다");

        foreach (string id in new[] { "jellyfish", "cat" })
        {
            sim.GetEmployeeState(id).DailyMood = "불안함";
            string a = MoodAnswer(id, "불안함");
            GD.Print($"   {Code(id),-4} (불안함) : {a}");
            if (a.Contains("저장고") || a.Contains("사고") || a.Contains("시설")) uneasyLinked = true;
        }
        Check(uneasyLinked, "③ 부정 기분은 실제로 아는 사고와 이어 말할 수 있다");
    }

    // ── ④ 목격 ≠ 범인 특정 ─────────────────────────────────────────────
    private void TestWitnessIdentification()
    {
        GD.Print("\n===== ④ 같은 방 목격자가 범인을 자동으로 알지 않는다 =====");
        Setup();
        GameState.Instance.SetSaboteur("fox");

        // 여우와 고양이(관찰력 3)가 같은 코어실에 있고, 여우가 방해공작을 했다.
        // 준비 단계의 이상 행동은 아무도 못 봤다고 가정한다.
        Place("fox", Core);
        Place("cat", Core);
        Log(LogEventType.Sabotage, "fox", Core, At(50), new[] { "cat" });
        DialogueContextBuilder.Invalidate();

        // 여기서는 EventLog 에 직접 넣었으므로 목격자 목록을 그대로 쓴다 —
        // 실제 경로(LogSabotage)의 분리는 아래 시뮬레이션에서 확인한다.
        GD.Print("   (직접 기록) 고양이가 목격자로 등록된 경우 — Q3 지목 가능 여부 확인");
        string named = Answer("cat", DialogueQuestions.Suspicious);
        GD.Print($"   고양이 : {named}");

        // 실제 근무를 돌려 LogSabotage 경로를 탄다.
        var sim = FacilitySimulation.Instance;
        int identified = 0, noticedOnly = 0, happened = 0, runs = 8;
        for (int i = 0; i < runs; i++)
        {
            StartShift("fox");
            for (float t = 0f; t < DayObjectives.MaxShiftSeconds; t += 1f / 30f)
            {
                GameState.Instance.AdvanceDayTime(1f / 30f);
                sim.Tick(1f / 30f);
            }
            var sab = EventLog.Instance.GetAllEntries()
                .FirstOrDefault(e => e.Day == 1 && e.EventType == LogEventType.Sabotage);
            if (sab == null) continue;
            happened++;
            if (sab.WitnessEmployeeIds.Count > 0) identified++;
            if (EventLog.Instance.GetAllEntries().Any(e => e.EventType == LogEventType.Neglect
                    && string.IsNullOrEmpty(e.ActorEmployeeId) && e.WitnessEmployeeIds.Count > 0))
                noticedOnly++;
        }
        GD.Print($"   방해공작 {happened}/{runs}회 · 범인을 특정한 근무 {identified}회 · " +
                 $"이상만 눈치챈 근무 {noticedOnly}회");
        Check(happened >= runs / 2, "④ (전제) 방해공작은 여전히 발생한다");
        Check(identified <= happened / 2, "④ 범인 특정은 드물게만 일어난다");

        // 관찰력이 낮은 직원은 애초에 눈치도 못 챈다.
        Check(EmployeeTraits.Get("rabbit").ObservationalAwareness < EmployeeTraits.AwarenessForWitness,
            "④ 관찰력이 낮은 직원은 이상 자체를 놓칠 수 있다");

        // 도착 직후 즉시 방해공작하지 않는다.
        var ops = OpsProfile.For(1);
        float minGap = ops.SabotageSettleSeconds + ops.SabotagePrepareMinSeconds;
        GD.Print($"   배치 → 방해공작 최소 간격 {minGap:0}초 (정착 {ops.SabotageSettleSeconds:0} + 준비 {ops.SabotagePrepareMinSeconds:0})");
        Check(minGap >= 15f, "④ 방에 도착하자마자 방해공작하지 않는다");
    }

    // ── ⑤ 필수 업무 미달성이 상태로 남는다 ─────────────────────────────
    private void TestMissedObjectives()
    {
        GD.Print("\n===== ⑤ 필수 업무 미달성 기록 =====");
        Setup();
        GameState.Instance.SetPhase(GamePhase.Live);

        int total = DayObjectives.RequiredTotal;
        int done = DayObjectives.RequiredDone;
        GameState.Instance.RecordShiftObjectives(total - done);
        GD.Print($"   필수 {done}/{total} · 기록된 미달성 {GameState.Instance.LastShiftMissedRequired}건 · " +
                 $"누적 {GameState.Instance.MissedRequiredDays}일");
        Check(GameState.Instance.LastShiftMissedRequired == total - done, "⑤ 미달성 수가 그대로 기록된다");
        Check(GameState.Instance.MissedRequiredDays == 1, "⑤ 미달성 근무가 누적으로 남는다");

        // 전부 달성한 근무는 기록을 늘리지 않는다.
        GameState.Instance.RecordShiftObjectives(0);
        Check(GameState.Instance.LastShiftMissedRequired == 0
              && GameState.Instance.MissedRequiredDays == 1, "⑤ 전부 달성한 근무는 실패로 세지 않는다");
    }

    // --- 도우미 -----------------------------------------------------------

    private static float At(int gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private static string Answer(string id, string questionId)
    {
        var ctx = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview, questionId, "", null);
        var plan = DialogueResponsePlanner.Plan(ctx);
        return KoreanDialogueComposer.Compose(ctx, plan);
    }

    // 기분 질문은 증거 기반 심문 경로를 탄다.
    private static string MoodAnswer(string id, string mood)
    {
        var board = InterviewEvidenceBoard.Build(id);
        var card = board.FirstOrDefault(e => e.Kind == EvidenceKind.Mood);
        if (card == null) return "(기분 자료 없음)";
        var q = InterviewQuestionFactory.Make(id, card, InterviewIntent.AskMoodReason);
        InterviewReplyPlanner.Reset();
        return InterviewReplyPlanner.Answer(q);
    }

    private static void Setup()
    {
        GameState.Instance.ResetRun(1);
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        FacilitySimulation.Instance.RollDailyMoods();

        var rooms = new Dictionary<string, string>
        {
            ["owl"] = Guard, ["cat"] = Storage, ["crow"] = Maintenance,
            ["rabbit"] = Maintenance, ["jellyfish"] = Power, ["fox"] = Core,
        };
        foreach (var kv in rooms) Place(kv.Key, kv.Value);
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
        DialogueContextBuilder.Invalidate();
    }

    private static void Place(string id, string room)
    {
        var st = FacilitySimulation.Instance.GetEmployeeState(id);
        if (st == null) return;
        st.AssignedRoomId = room;
        st.CurrentRoomId = room;
        st.Alive = true;
        st.Isolated = false;
        st.IsMoving = false;
    }

    private void StartShift(string saboteurId)
    {
        var sim = FacilitySimulation.Instance;
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(1);
        sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        sim.RollDailyMoods();
        // 결번자와 관찰력 높은 직원을 같은 방에 둔다 — 가장 들키기 쉬운 배치.
        sim.AssignToRoom("fox", Core);
        sim.AssignToRoom("cat", Core);
        sim.AssignToRoom("owl", Power);
        sim.AssignToRoom("crow", Maintenance);
        sim.AssignToRoom("jellyfish", Guard);
        sim.AssignToRoom("rabbit", Storage);
        GameState.Instance.SetSaboteur(saboteurId);
        sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private static void Incident(LogEventType type, string roomId, float at) => Log(type, "", roomId, at);

    private static void Log(LogEventType type, string actor, string room, float at,
        IEnumerable<string> witnesses = null)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = room,
            Description = $"(테스트 {type} {room} {at:0.0})",
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    private static string Code(string id) =>
        FacilitySimulation.Instance?.GetEmployeeDef(id)?.Codename ?? id;
}
