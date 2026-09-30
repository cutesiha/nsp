using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Dialogue;

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
        // 증거 기반 심문(조사 자료 → 질문 → 모순 추궁) 검증.
        InterviewScenarioTest.RunAll();
        // 조립 규칙(문장 수 상한 · 닫힌 틀 · 되받기 중복) 검증.
        ComposerRuleChecks();
        GD.Print("=== 시나리오 검증 종료 ===");
        GetTree().Quit();
    }

    // --- 조립 규칙 (CLAUDE_CODE_TASK_dialogue_AB A-1 ~ A-3) ------------------
    //
    // 문장이 아니라 "무엇을 붙이고 무엇을 안 붙이는가"를 본다. 대사 뱅크가 바뀌어도 이 규칙은 그대로다.
    private static int _rulePass, _ruleFail;

    private static void RuleCheck(bool ok, string what)
    {
        if (ok) _rulePass++; else _ruleFail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private static void ComposerRuleChecks()
    {
        GD.Print("\n################ 조립 규칙 검증 ################");
        _rulePass = _ruleFail = 0;

        // 문장 수 — "~" 뒤 공백도 경계다.
        RuleCheck(DialogueComposer.SentenceCount("코어실에 있었어요~ 관리자님은 그때 어디 계셨어요? 그때는 혼자였어요~") == 3, "문장 수: ~ · ? 경계로 3문장");
        RuleCheck(DialogueComposer.SentenceCount("혼자였습니다.") == 1, "문장 수: 한 문장");

        // 닫힌 틀 — 두 문장 이상 · 되묻기로 끝남 · 덧붙임 어미로 끝남.
        RuleCheck(DialogueComposer.IsClosed("코어실에 있었어요~ 관리자님은 그때 어디 계셨어요?"), "닫힘: 두 문장");
        RuleCheck(DialogueComposer.IsClosed("제가 그렇게 수상해 보여요~?"), "닫힘: ~? 로 끝남");
        RuleCheck(DialogueComposer.IsClosed("로그 보면 나오잖아요~"), "닫힘: 잖아요~ 로 끝남");
        RuleCheck(DialogueComposer.IsClosed("자재만 오면 금방 할 텐데."), "닫힘: 텐데. 로 끝남");
        RuleCheck(!DialogueComposer.IsClosed("저장고에 있었습니다."), "열림: 평서문 한 문장");
        RuleCheck(!DialogueComposer.IsClosed("혼자였어요~"), "열림: ~ 로 끝나는 한 문장은 닫힌 틀이 아니다");

        // 되받기 중복 — 짧은 첫 문장이 ? · ~ 로 끝나거나 되받기 주제어로 시작.
        RuleCheck(DialogueComposer.AlreadyEchoes("수상한 사람이라~ 딱히요."), "되받음: 짧은 첫 문장이 ~ 로 끝남");
        RuleCheck(DialogueComposer.AlreadyEchoes("저요? 코어실에 있었어요."), "되받음: 저요? 로 시작");
        RuleCheck(DialogueComposer.AlreadyEchoes("이상현상이요~? 없었어요."), "되받음: 이상 으로 시작");
        RuleCheck(!DialogueComposer.AlreadyEchoes("코어실에서 하던 일을 계속하고 있었습니다."), "안 되받음: 긴 평서문");

        // 실제 조립 — 닫힌 틀에는 기억 · 되묻기가 붙지 않고, 보정은 앞에 온다.
        var f = new ReplyFrame { EmployeeId = "fox", CustomSlot = "incident.indirect", MaxSentences = 3 };
        f.Set("iroom", "발전실").Set("sound", "쿵 하는 소리가 들렸어요.");
        f.Caveats.Add("caveat.indirect");
        f.BackSlot = "closer.back";
        f.Addenda.Add(new ReplyAddendum { Slot = "mem.alone" }.Set("room", "코어실"));
        int closedRuns = 0, okRuns = 0;
        for (int i = 0; i < 12; i++)
        {
            string a = DialogueComposer.Compose(f);
            string tr = DialogueComposer.LastTrace;
            if (!tr.Contains("(닫힘)")) continue;
            closedRuns++;
            bool noTail = !tr.Contains("되묻기") && !tr.Contains("덧붙임") && !tr.Contains("인상")
                          && !System.Text.RegularExpressions.Regex.IsMatch(tr, @"기억\[[^\]]*mem\.alone\]");
            if (noTail) okRuns++;
            if (i < 3) GD.Print($"   예: {a}  ({tr})");
        }
        RuleCheck(closedRuns > 0, $"여우 간접 목격 틀 중 닫힌 틀이 뽑혔다 ({closedRuns}/12)");
        RuleCheck(closedRuns == okRuns, $"닫힌 틀 뒤에는 기억 · 되묻기 · 덧붙임이 붙지 않는다 ({okRuns}/{closedRuns})");

        // 문장 수 상한 — 여우(2) 에게 기억 두 줄을 주어도 두 문장을 넘지 않는다(핵심이 한 문장일 때).
        var g = new ReplyFrame { EmployeeId = "wolf", CustomSlot = "Companion.alone", MaxSentences = 2 };
        g.Set("room", "경비실");
        g.Addenda.Add(new ReplyAddendum { Slot = "mem.worked" }.Set("task", "순찰").Set("room", "경비실"));
        g.Addenda.Add(new ReplyAddendum { Slot = "mem.repair" }.Set("room", "경비실"));
        g.ExtraSlot = "support.nothing";
        int over = 0;
        for (int i = 0; i < 12; i++)
        {
            string a = DialogueComposer.Compose(g);
            if (DialogueComposer.SentenceCount(a) > 2) { over++; GD.Print($"   초과: {a}  ({DialogueComposer.LastTrace})"); }
        }
        RuleCheck(over == 0, $"늑대(상한 2) — 기억 두 줄 · 덧붙임을 줘도 두 문장을 넘지 않는다 (초과 {over}/12)");

        GD.Print($"\n################ 결과: {_rulePass} PASS / {_ruleFail} FAIL ################");
    }

    // --- 시나리오 A : 늑대가 인접 작업실에서 소리만 들었다 -----------------
    private void ScenarioA()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO A : 발전실 사고 / 늑대는 환기실(인접) =====");
        Ask("wolf", DialogueQuestions.Anomaly);
        Ask("wolf", DialogueQuestions.Where);
        Ask("wolf", DialogueQuestions.Suspicious);
    }

    // --- 시나리오 B : 늑대가 방해자. 실제는 저장고, 배정은 환기실 -----------
    private void ScenarioB()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core,
        });
        GameState.Instance.SetSaboteur("wolf");

        // 실제 이동 기록: 환기실 → 저장고
        Move("wolf", Vent, Storage, 4f);
        // 저장고에서의 방해공작(목격자 없음)
        Log(LogEventType.Sabotage, "wolf", Storage, 7f);
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO B : 방해자 늑대 / 실제 저장고, 배정 환기실 =====");
        var ctx = DialogueContextBuilder.Build("wolf", DialogueConversationKind.Interview,
            DialogueQuestions.Where, "", null);
        GD.Print($"  [내부] 실제 위치={ctx.RoomAtSubject} / 배정={ctx.AssignedRoomId} / 불리한 기록={ctx.EvidenceAgainstCount}");
        Ask("wolf", DialogueQuestions.Where);
        Ask("wolf", DialogueQuestions.Where);   // 같은 질문 반복 — 주장이 바뀌면 안 된다
        Ask("wolf", DialogueQuestions.Anomaly);
        Ask("wolf", DialogueQuestions.Accuse);
        var claim = DialogueClaimState.Get("wolf", 1,
            DialogueContextBuilder.SelectSubjectIncident(1) is { } e
                ? DialogueFact.From(e, KnowledgeLevel.None).Key : "no_incident");
        GD.Print($"  [내부] 전략={claim.Mode} / 주장 위치={claim.ClaimedRoomId} / 진실여부={claim.ClaimTruthful}");
    }

    // --- 시나리오 C : 양이 인접 방에서 소리만 들었다 ---------------------
    private void ScenarioC()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["sheep"] = Vent, ["wolf"] = Guard, ["dog"] = Medical,
            ["rabbit"] = Maintenance, ["cat"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO C : 양이 환기실(인접)에서 소리만 들음 =====");
        for (int i = 0; i < 3; i++) Ask("sheep", DialogueQuestions.Anomaly);
    }

    // --- 시나리오 D : 같은 사실, 6명 비교 -----------------------------------
    private void ScenarioD()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["rabbit"] = Vent, ["dog"] = Vent,
            ["cat"] = Vent, ["sheep"] = Vent, ["fox"] = Vent,
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
            ["wolf"] = Power, ["rabbit"] = Power, ["dog"] = Power,
            ["cat"] = Power, ["sheep"] = Power, ["fox"] = Power,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);

        GD.Print("\n===== SCENARIO E : 발전실 안에서 직접 목격 =====");
        GD.Print(LocalDialogueGenerator.DebugCompare(DialogueQuestions.Anomaly));
    }

    // --- 시나리오 F : 6명이 각각 방해자일 때의 전략 --------------------------
    private void ScenarioF()
    {
        GD.Print("\n===== SCENARIO F : 방해자별 전략과 알리바이 =====");
        foreach (string id in new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" })
        {
            Reset();
            Place(new Dictionary<string, string>
            {
                ["wolf"] = Vent, ["dog"] = Vent, ["cat"] = Vent,
                ["rabbit"] = Vent, ["sheep"] = Vent, ["fox"] = Vent,
            });
            GameState.Instance.SetSaboteur(id);
            // 방해자만 저장고로 이탈했고, 다른 직원이 그 장면을 목격했다.
            Move(id, Vent, Storage, 4f);
            Log(LogEventType.Sabotage, id, Storage, 7f, new[] { id == "dog" ? "cat" : "dog" });
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
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Power,
        });

        GD.Print("\n===== SCENARIO G : 일반 통화(정상) =====");
        foreach (string id in new[] { "cat", "wolf", "sheep" })
            GD.Print($"  [{id}] 작업진행 → {LocalDialogueGenerator.GeneralAnswer(id, 0)}");

        // 고양이만 스트레스를 위험 구간으로 올린다.
        FacilitySimulation.Instance.GetEmployeeState("cat").Stress = 38f;
        GD.Print("  -- 고양이 스트레스 38 --");
        GD.Print($"  [cat] 작업진행 → {LocalDialogueGenerator.GeneralAnswer("cat", 0)}");
        GD.Print($"  [cat] 집중지시 → {LocalDialogueGenerator.GeneralAnswer("cat", 1)}");

        Incident(LogEventType.TaskFailed, Power, 8.5f);
        GD.Print("  -- 발전실 사고 발생 후 --");
        foreach (string id in new[] { "wolf", "sheep", "rabbit" })
            GD.Print($"  [{id}] 이상현상 → {LocalDialogueGenerator.GeneralAnswer(id, 2)}");
        FacilitySimulation.Instance.GetEmployeeState("cat").Stress = 1f;
    }

    // --- 시나리오 H : 발전실이 아닌 작업실 사고도 신고되는가 -------------------
    private void ScenarioH()
    {
        GD.Print("\n===== SCENARIO H : 수신 전화(작업실별) =====");
        foreach (var (room, caller) in new[]
        {
            (Maintenance, "rabbit"), (Maintenance, "wolf"),
            (Vent, "dog"), (Medical, "fox"), (Storage, "sheep"),
        })
        {
            Reset();
            Place(new Dictionary<string, string>
            {
                ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
                ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Power,
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

        // F1 : 일반 늑대 / 인접 작업실에서 소리만 들음
        SetupBasic();
        Turn("wolf", DialogueQuestions.Anomaly, "F1 일반 늑대 · Q1(간접 목격)");

        // F2 : 일반 늑대 / Q2 — 플레이어 증거 없음 → 추궁 후보가 나오면 안 된다
        SetupBasic();
        Turn("wolf", DialogueQuestions.Where, "F2 일반 늑대 · Q2(증거 없음)");

        // F3 : 방해자 늑대 / 실제 저장고, 주장 환기실 — 증거 없음
        SetupSaboteur();
        Turn("wolf", DialogueQuestions.Where, "F3 방해자 늑대 · Q2(증거 없음)");

        // F4 : 같은 상황 + 양에게서 목격 증언을 먼저 확보
        SetupSaboteur();
        Log(LogEventType.Sabotage, "wolf", Storage, 7f, new[] { "sheep" });
        GD.Print("\n--- 먼저 양을 인터뷰해 목격 증언을 확보한다 ---");
        Turn("sheep", DialogueQuestions.Suspicious, "F4-1 양 · Q3");
        Turn("wolf", DialogueQuestions.Where, "F4-2 방해자 늑대 · Q2(목격 증언 확보 후)");

        // F5 : 고양이가 여우의 행동을 목격 → Q3 세부 추궁
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Core,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core,
        });
        Log(LogEventType.Sabotage, "fox", Core, 6f, new[] { "cat" });
        Incident(LogEventType.TaskFailed, Power, 8.5f);
        Turn("cat", DialogueQuestions.Suspicious, "F5 고양이 · Q3(실제 목격)");

        // F6 : 답할 것이 없으면 꼬리질문 0개
        SetupBasic();
        Turn("dog", DialogueQuestions.Suspicious, "F6 강아지 · Q3(목격 없음)");

        // F7 : 6명이 같은 꼬리질문에 어떻게 다르게 답하는가
        SetupBasic();
        GD.Print("\n===== F7 : 같은 꼬리질문(그 전에는 어디에?) 6명 비교 =====");
        foreach (string id in new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" })
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
        GD.Print("\n--- 늑대의 이동이 시설 로그 화면에 떴다고 가정 ---");
        Turn("wolf", DialogueQuestions.Accuse, "F8 방해자 늑대 · Q5");
    }

    private void SetupBasic()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core,
        });
        Incident(LogEventType.TaskFailed, Power, 8.5f);
    }

    private void SetupSaboteur()
    {
        Reset();
        Place(new Dictionary<string, string>
        {
            ["wolf"] = Vent, ["dog"] = Guard, ["cat"] = Medical,
            ["rabbit"] = Maintenance, ["sheep"] = Storage, ["fox"] = Core,
        });
        GameState.Instance.SetSaboteur("wolf");
        // 근무 시작 배치가 끝난 상태로 만든다 — 이후의 이동이 시설 로그 화면에 뜬다.
        foreach (var id in new[] { "wolf", "dog", "cat", "rabbit", "sheep", "fox" })
            Log(LogEventType.TaskStart, id,
                FacilitySimulation.Instance.GetEmployeeState(id).AssignedRoomId, 1f);
        Move("wolf", Vent, Storage, 4f);
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
        // 실제 근무 시작(BeginShift)처럼 배치를 근무 시작 위치로 적어 둔다 — 동선 시간표의 0초 위치.
        sim.RecordShiftStart();
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
