using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 개발용 시나리오 검증. 게임 흐름에서는 절대 로드되지 않으며
// scenes/debug/DialogueScenarioTest.tscn 을 직접 실행할 때만 동작한다.
//
//   godot --headless --path . scenes/debug/DialogueScenarioTest.tscn
//
// 목적: 같은 사실을 넣었을 때 6명의 출력이 실제로 다른지, 간접 목격자가 직접 본 것처럼
// 말하지 않는지, 방해자의 알리바이가 반복 질문에서도 유지되는지를 눈으로 확인한다.
public partial class DialogueScenarioTest : Node
{
    private const string Vent = "vent_room";
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Medical = "medical_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";

    public override void _Ready()
    {
        ScenarioA();
        ScenarioB();
        ScenarioC();
        ScenarioD();
        ScenarioE();
        ScenarioF();
        ScenarioG();
        ScenarioH();
        GD.Print("=== 시나리오 검증 종료 ===");
        GetTree().Quit();
    }

    // --- 시나리오 A : 까마귀가 인접 작업실에서 소리만 들었다 -----------------
    private void ScenarioA()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO A : 발전실 사고 / 까마귀는 환기실(인접) =====");
        Ask("crow", DialogueQuestions.Anomaly);
        Ask("crow", DialogueQuestions.Where);
        Ask("crow", DialogueQuestions.Suspicious);
    }

    // --- 시나리오 B : 까마귀가 방해자. 실제는 저장고, 배정은 환기실 -----------
    private void ScenarioB()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core,
        });
        GameState.Instance.SetSaboteur("crow");

        // 실제 이동 기록: 환기실 → 저장고
        Move("crow", Vent, Storage, 4f);
        // 저장고에서의 방해공작(목격자 없음)
        Log(LogEventType.Sabotage, "crow", Storage, 7f);
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO B : 방해자 까마귀 / 실제 저장고, 배정 환기실 =====");
        var ctx = DialogueContextBuilder.Build("crow", DialogueConversationKind.Interview,
            DialogueQuestions.Where, "", null);
        GD.Print($"  [내부] 실제 위치={ctx.RoomAtSubject} / 배정={ctx.AssignedRoomId} / 불리한 기록={ctx.EvidenceAgainstCount}");
        Ask("crow", DialogueQuestions.Where);
        Ask("crow", DialogueQuestions.Where);   // 같은 질문 반복 — 주장이 바뀌면 안 된다
        Ask("crow", DialogueQuestions.Anomaly);
        Ask("crow", DialogueQuestions.Accuse);
        var claim = DialogueClaimState.Get("crow", 1,
            DialogueContextBuilder.SelectSubjectIncident(1) is { } e
                ? DialogueFact.From(e, KnowledgeLevel.None).Key : "no_incident");
        GD.Print($"  [내부] 전략={claim.Mode} / 주장 위치={claim.ClaimedRoomId} / 진실여부={claim.ClaimTruthful}");
    }

    // --- 시나리오 C : 해파리가 인접 방에서 소리만 들었다 ---------------------
    private void ScenarioC()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["jellyfish"] = Vent, ["crow"] = Guard, ["owl"] = Medical,
            ["rabbit"] = Maintenance, ["cat"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO C : 해파리가 환기실(인접)에서 소리만 들음 =====");
        for (int i = 0; i < 3; i++) Ask("jellyfish", DialogueQuestions.Anomaly);
    }

    // --- 시나리오 D : 같은 사실, 6명 비교 -----------------------------------
    private void ScenarioD()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["rabbit"] = Vent, ["owl"] = Vent,
            ["cat"] = Vent, ["jellyfish"] = Vent, ["fox"] = Vent,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO D : 완전히 같은 사실 / 6명 비교 =====");
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Where));
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Anomaly));
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Suspicious));
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Opinion));
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Accuse));
    }

    // --- 시나리오 E : 사고가 난 방 안에 있었다(직접 목격) --------------------
    private void ScenarioE()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Power, ["rabbit"] = Power, ["owl"] = Power,
            ["cat"] = Power, ["jellyfish"] = Power, ["fox"] = Power,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO E : 발전실 안에서 직접 목격 =====");
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Anomaly));
    }

    // --- 시나리오 F : 6명이 각각 방해자일 때의 전략 --------------------------
    private void ScenarioF()
    {
        GD.Print("\n===== SCENARIO F : 방해자별 전략과 알리바이 =====");
        foreach (string id in new[] { "owl", "cat", "jellyfish", "rabbit", "crow", "fox" })
        {
            Reset();
            Place(new Dictionary<string, string>
            {
                ["crow"] = Vent, ["owl"] = Vent, ["cat"] = Vent,
                ["rabbit"] = Vent, ["jellyfish"] = Vent, ["fox"] = Vent,
            });
            GameState.Instance.SetSaboteur(id);
            // 방해자만 저장고로 이탈했고, 다른 직원이 그 장면을 목격했다.
            Move(id, Vent, Storage, 4f);
            Log(LogEventType.Sabotage, id, Storage, 7f, new[] { id == "owl" ? "cat" : "owl" });
            Incident(LogEventType.TaskFailed, Power, 8.5f);

            string where = LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Where);
            string again = LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Where);
            string accuse = LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Accuse);
            var claim = DialogueClaimState.Get(id, 1, SubjectKey());
            GD.Print($"  [{id}] 전략={claim.Mode} 주장위치={claim.ClaimedRoomId} (실제=storage_room)");
            GD.Print($"      Q2 → {where}");
            GD.Print($"      Q2 재질문 → {again}");
            GD.Print($"      Q5 → {accuse}");
        }
    }

    // --- 시나리오 G : 일반 통화가 현재 상태를 반영하는가 ----------------------
    private void ScenarioG()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Power,
        });

        GD.Print("\n===== SCENARIO G : 일반 통화(정상) =====");
        foreach (string id in new[] { "cat", "crow", "jellyfish" })
            GD.Print($"  [{id}] 작업진행 → {LocalDialogueGenerator.GeneralAnswer(id, 0)}");

        // 고양이만 스트레스를 위험 구간으로 올린다.
        FacilitySimulation.Instance.GetEmployeeState("cat").Stress = 38f;
        GD.Print("  -- 고양이 스트레스 38 --");
        GD.Print($"  [cat] 작업진행 → {LocalDialogueGenerator.GeneralAnswer("cat", 0)}");
        GD.Print($"  [cat] 집중지시 → {LocalDialogueGenerator.GeneralAnswer("cat", 1)}");

        Incident(LogEventType.TaskFailed, Power, 8.5f);
        GD.Print("  -- 발전실 사고 발생 후 --");
        foreach (string id in new[] { "crow", "jellyfish", "rabbit" })
            GD.Print($"  [{id}] 이상현상 → {LocalDialogueGenerator.GeneralAnswer(id, 2)}");
        FacilitySimulation.Instance.GetEmployeeState("cat").Stress = 1f;
    }

    // --- 시나리오 H : 발전실이 아닌 작업실 사고도 신고되는가 -------------------
    private void ScenarioH()
    {
        GD.Print("\n===== SCENARIO H : 수신 전화(작업실별) =====");
        foreach (var (room, caller) in new[]
        {
            (Maintenance, "rabbit"), (Maintenance, "crow"),
            (Vent, "owl"), (Medical, "fox"), (Storage, "jellyfish"),
        })
        {
            Reset();
            Place(new Dictionary<string, string>
            {
                ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
                ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Power,
            });
            Incident(LogEventType.TaskFailed, room, 8.5f);
            var line = LocalDialogueGenerator.BuildIncomingCall(caller,
                DialogueRepository.EventAccidentNearby, room);
            if (line == null) { GD.Print($"  [{caller}/{room}] 생성 불가 → 폴백"); continue; }
            GD.Print($"  [{caller}/{room}] {line.Opening}");
            foreach (var c in line.Choices) GD.Print($"      · {c.Text} → {c.Reply}");
        }
    }

    private static string SubjectKey()
    {
        var e = DialogueContextBuilder.SelectSubjectIncident(1);
        return e == null ? "no_incident" : DialogueFact.From(e, KnowledgeLevel.None).Key;
    }

    // --- 도우미 ---------------------------------------------------------

    private static void Reset()
    {
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
    }

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
        }
    }

    private static void Move(string employeeId, string from, string to, float at)
    {
        Log(LogEventType.RoomExit, employeeId, from, at);
        Log(LogEventType.RoomEnter, employeeId, to, at + 1f);
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
            Description = "(테스트)",
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }

    private static void Ask(string employeeId, string questionId)
    {
        string q = LocalInterviewDialogue.GetQuestionText(employeeId, questionId);
        string a = LocalDialogueGenerator.InterviewAnswer(employeeId, questionId);
        GD.Print($"  Q({questionId}) {q}\n    → {a}");
    }
}
