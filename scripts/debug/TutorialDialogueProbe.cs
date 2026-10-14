using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// F-1 진단 — DAY0 교육 휴게시간에 토끼가 "이상한 점" · "수상한 사람" 질문에
// 위치 답변("발전실이요!")을 내놓는 이유를 찾는다.
//
//   godot --headless --path . res://scenes/debug/TutorialDialogueProbe.tscn
//
// 세 질문(Anomaly / Suspicious / Where)의 답과, 그 답을 만든 컨텍스트(ctx) · 슬롯을 함께 찍는다.
// 교육(DAY0)과 실전(DAY1)을 같은 로그로 나란히 돌려 어디서 갈리는지 본다.
public partial class TutorialDialogueProbe : Node
{
    private const string Rabbit = "rabbit";
    private const string Power = "power_room";
    private const string Guard = "guard_room";
    private const string Maintenance = "maintenance_room";

    public override void _Ready()
    {
        GD.Print("\n\n################ F-1 진단 : 교육 휴게시간 대사 ################");
        Probe(day: 0, "교육(DAY0)");
        Probe(day: 1, "실전(DAY1)");
        GD.Print("\n################ 진단 끝 ################");
        GetTree().Quit();
    }

    private void Probe(int day, string label)
    {
        var gs = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        gs.ResetRun(day);
        sim.ResetRun();
        EventLog.Instance.ClearAll();
        DialogueClaimState.ResetAll();
        CallMemoryLog.ResetAll();
        PlayerKnownEvidence.ResetAll();
        ShiftMemory.Invalidate();
        InterviewReplyPlanner.Reset();
        DialoguePatternMemory.ResetAll();

        GD.Print($"\n===== {label} — GameState.CurrentDay={gs.CurrentDay} / DialogueContextBuilder.Day()={DialogueContextBuilder.Day()} =====");

        // 교육과 같은 모양: 토끼는 경비실 배치(AssignRoomId) → 발전실 사고(AccidentRoomId)
        // → 관리자가 토끼를 발전실로 재배치해 수리. 경비실은 발전실의 옆방이다.
        Deploy(sim, Rabbit, Guard);
        Deploy(sim, "cat", Maintenance);
        Log(LogEventType.TaskStart, Rabbit, Guard, 1f, day);
        Log(LogEventType.TaskStart, "cat", Maintenance, 1f, day);
        Log(LogEventType.TaskFailed, "", Power, At(20), day);                 // 무인 사고
        Log(LogEventType.Relocation, Rabbit, Power, At(24), day);             // 관리자 지시
        Log(LogEventType.RoomExit, Rabbit, Guard, At(24.5f), day);
        Log(LogEventType.RoomEnter, Rabbit, Power, At(25), day);
        Log(LogEventType.TaskStart, Rabbit, Power, At(26), day, repair: true);
        Log(LogEventType.TaskComplete, Rabbit, Power, At(34), day, done: true);
        ShiftMemory.Invalidate();

        gs.SetPhase(GamePhase.Rest);

        var subject = DialogueContextBuilder.MostRecentKnownIncident(Rabbit, DialogueContextBuilder.Day(), true);
        GD.Print($"   [사고 조회] MostRecentKnownIncident = {(subject == null ? "없음" : $"{subject.EventType}@{subject.RoomId} t={subject.GameTimeSeconds:0.0} day={subject.Day}")}");
        GD.Print($"   [로그] 오늘 줄 수 = {EventLog.Instance.GetAllEntries().Count(e => e.Day == DialogueContextBuilder.Day())} / 전체 {EventLog.Instance.GetAllEntries().Count}");

        foreach (string q in new[] { DialogueQuestions.Anomaly, DialogueQuestions.Suspicious, DialogueQuestions.Where, DialogueQuestions.ShiftReview })
            Ask(q);
    }

    private void Ask(string questionId)
    {
        var subject = questionId == DialogueQuestions.Anomaly
            ? DialogueContextBuilder.MostRecentKnownIncident(Rabbit, DialogueContextBuilder.Day(), true)
            : null;
        var ctx = DialogueContextBuilder.Build(Rabbit, DialogueConversationKind.Interview, questionId, "", subject);
        var plan = DialogueResponsePlanner.Plan(ctx);
        string answer = LocalDialogueGenerator.InterviewAnswer(Rabbit, questionId);

        GD.Print($"\n   ── 질문 {questionId}");
        GD.Print($"      답      : {answer}");
        GD.Print($"      슬롯    : {DialogueComposer.LastTrace}");
        GD.Print($"      Core    : {plan.Core} / Knowledge={plan.Knowledge} / RoomId={plan.RoomId}");
        GD.Print($"      ctx     : Subject={(ctx.Subject == null ? "없음" : ctx.Subject.Key)} RoomAtSubject={ctx.RoomAtSubject} "
                 + $"Day={ctx.CurrentDay} Assigned={ctx.AssignedRoomId}");
        GD.Print($"      ctx2    : KnownSuspiciousActorId={(string.IsNullOrEmpty(ctx.KnownSuspiciousActorId) ? "없음" : ctx.KnownSuspiciousActorId)} "
                 + $"SubjectKnowledge={ctx.SubjectKnowledge} IsSaboteur={ctx.IsSaboteur}");
        GD.Print($"      덮어쓰기: ScriptedAnswerOverride={(LocalDialogueGenerator.ScriptedAnswerOverride == null ? "없음" : "설정됨")}");
    }

    // --- 도우미 ---------------------------------------------------------

    private static float At(float gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private static void Deploy(FacilitySimulation sim, string id, string room)
    {
        var st = sim.GetEmployeeState(id);
        if (st == null) return;
        st.AssignedRoomId = room;
        st.CurrentRoomId = room;
        st.Alive = true;
        st.Isolated = false;
    }

    private static void Log(LogEventType type, string actor, string room, float at, int day, bool repair = false, bool done = false)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = day,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = room,
            Description = (repair ? "🔧 " : "") + $"(진단 {type} {room} {at:0.0})" + (done ? " / 설비 수리 완료" : ""),
        });
    }
}
