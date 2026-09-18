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
        FollowUpScenarios();
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

    // ══ 꼬리질문 검증 ══════════════════════════════════════════════════
    private void FollowUpScenarios()
    {
        GD.Print("\n########## 꼬리질문 시스템 ##########");

        // F1 : 일반 까마귀 / 인접 작업실에서 소리만 들음
        SetupBasic();
        Turn("crow", DialogueQuestions.Anomaly, "F1 일반 까마귀 · Q1(간접 목격)");

        // F2 : 일반 까마귀 / Q2 — 플레이어 증거 없음 → 추궁 후보가 나오면 안 된다
        SetupBasic();
        Turn("crow", DialogueQuestions.Where, "F2 일반 까마귀 · Q2(증거 없음)");

        // F3 : 방해자 까마귀 / 실제 저장고, 주장 환기실 — 증거 없음
        SetupSaboteur();
        Turn("crow", DialogueQuestions.Where, "F3 방해자 까마귀 · Q2(증거 없음)");

        // F4 : 같은 상황 + 해파리에게서 목격 증언을 먼저 확보
        SetupSaboteur();
        Log(LogEventType.Sabotage, "crow", Storage, 7f, new[] { "jellyfish" });
        GD.Print("\n--- 먼저 해파리를 인터뷰해 목격 증언을 확보한다 ---");
        Turn("jellyfish", DialogueQuestions.Suspicious, "F4-1 해파리 · Q3");
        Turn("crow", DialogueQuestions.Where, "F4-2 방해자 까마귀 · Q2(목격 증언 확보 후)");

        // F5 : 고양이가 여우의 행동을 목격 → Q3 세부 추궁
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Core,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core,
        });
        Log(LogEventType.Sabotage, "fox", Core, 6f, new[] { "cat" });
        Incident(LogEventType.TaskFailed, Power, 8.5f);
        Turn("cat", DialogueQuestions.Suspicious, "F5 고양이 · Q3(실제 목격)");

        // F6 : 답할 것이 없으면 꼬리질문 0개
        SetupBasic();
        Turn("owl", DialogueQuestions.Suspicious, "F6 올빼미 · Q3(목격 없음)");

        // F7 : 6명이 같은 꼬리질문에 어떻게 다르게 답하는가
        SetupBasic();
        GD.Print("\n===== F7 : 같은 꼬리질문(그 전에는 어디에?) 6명 비교 =====");
        foreach (string id in new[] { "owl", "cat", "jellyfish", "rabbit", "crow", "fox" })
        {
            LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Where);
            var q = new FollowUpQuestion
            {
                Intent = FollowUpIntent.AskPreviousLocation,
                Text = "그 전에는 어디에 있었습니까?",
                SubjectIncidentKey = SubjectKey(),
            };
            GD.Print($"  {id,-10} → {LocalDialogueGenerator.FollowUpAnswer(id, q)}");
        }

        // F8 : Q5 추궁 — 지시 없는 이동이 화면 로그에 떠 있을 때
        SetupSaboteur();
        GD.Print("\n--- 까마귀의 이동이 시설 로그 화면에 떴다고 가정 ---");
        Turn("crow", DialogueQuestions.Accuse, "F8 방해자 까마귀 · Q5");
    }

    private void SetupBasic()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);
    }

    private void SetupSaboteur()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["crow"] = Vent, ["owl"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["jellyfish"] = Storage, ["fox"] = Core,
        });
        GameState.Instance.SetSaboteur("crow");
        // 근무 시작 배치가 끝난 상태로 만든다 — 이후의 이동이 시설 로그 화면에 뜬다.
        foreach (var id in new[] { "crow", "owl", "cat", "rabbit", "jellyfish", "fox" })
            Log(LogEventType.TaskStart, id,
                FacilitySimulation.Instance.GetEmployeeState(id).AssignedRoomId, 1f);
        Move("crow", Vent, Storage, 4f);
        Incident(LogEventType.TaskFailed, Power, 8.5f);
    }

    // 기본 질문 → 답변 → 꼬리질문 후보 → 첫 후보 선택 → 답변까지 한 번에 출력.
    private void Turn(string employeeId, string questionId, string label)
    {
        GD.Print($"\n===== {label} =====");
        FollowUpQuestionGenerator.DebugLog = true;
        var turn = LocalDialogueGenerator.Interview(employeeId, questionId);
        FollowUpQuestionGenerator.DebugLog = false;
        GD.Print($"  Q: {LocalInterviewDialogue.GetQuestionText(employeeId, questionId)}");
        GD.Print($"  A: {turn.Answer}");
        if (turn.FollowUps.Count == 0)
        {
            GD.Print("  꼬리질문 없음 → 기본 질문 목록으로 복귀");
            return;
        }
        foreach (var f in turn.FollowUps)
            GD.Print($"  [꼬리질문] {f.Text}   ({f.Intent}{(f.IsChallenge ? " · 추궁" : "")})");
        var pick = turn.FollowUps[0];
        GD.Print($"  → 선택: {pick.Text}");
        GD.Print($"  A: {LocalDialogueGenerator.FollowUpAnswer(employeeId, pick)}");
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
