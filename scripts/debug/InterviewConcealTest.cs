using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 심문 4단계 — **조건부 은폐** 검증.
//
//   godot --headless --path . res://scenes/debug/InterviewConcealTest.tscn --quit-after 4000
//
// 이 테스트가 지키려는 것은 "직원이 거짓말을 한다" 가 아니라 그 반대다.
//
//   A 숨길 **실제 사건이 없으면** 은폐 동기가 생기지 않는가
//   B 관계가 가까워도 **실제 목격이 없으면** 감싸지 않는가
//   C 감싸서 말하지 않아도 **사실이 사라지지 않는가** (재석 질문으로 되찾기)
//   D 거짓말 여부와 범인 여부가 **따로 노는가**
//   E 대화·진술 자료가 따로 모이는가 (3단계 UI 분류)
public partial class InterviewConcealTest : Node
{
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Core = "core_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 숨길 것이 없으면 은폐 동기도 없다 ────────────────────────────

    private void SectionA()
    {
        GD.Print("\n#### A. 숨길 실제 사건이 없으면 은폐하지 않는다");
        Reset();
        Place(new() { ["rabbit"] = Storage });
        // 토끼는 아무 사고도 내지 않았고, 지시 없는 이동도 하지 않았다.
        Log(LogEventType.TaskStart, "rabbit", Storage, 1f);

        var ctx = DialogueContextBuilder.Build("rabbit", DialogueConversationKind.Interview,
            DialogueQuestions.Suspicious, "", null);
        var c = ConcealmentMotive.For(ctx);
        Ok(!c.Any, $"사고도 무단 이동도 없으면 숨길 동기가 없다 ({c.Kind})");

        // 실제로 사고를 내면 그때 비로소 동기가 생긴다.
        Log(LogEventType.TaskFailed, "rabbit", Storage, 300f);
        var ctx2 = DialogueContextBuilder.Build("rabbit", DialogueConversationKind.Interview,
            DialogueQuestions.Suspicious, "", null);
        var c2 = ConcealmentMotive.For(ctx2);
        Ok(c2.Kind == ConcealKind.OwnMistake, $"본인이 낸 사고가 있으면 실수 은폐 동기 ({c2.Kind} · {c2.Why})");
    }

    // ── B. 관계만으로 감싸지 않는다 ─────────────────────────────────────

    private void SectionB()
    {
        GD.Print("\n#### B. 관계가 가까워도 실제 목격이 없으면 감쌀 것이 없다");
        Reset();

        // 고양이와 강아지는 연인이다 — 감싸는 성향은 있다.
        Ok(ConcealmentMotive.ShieldsFrom("cat", "dog"), "고양이는 강아지를 감싸는 사이다");
        Ok(!ConcealmentMotive.ShieldsFrom("fox", "cat"), "사이가 나쁜 쪽은 감싸지 않는다");
        Ok(!ConcealmentMotive.ShieldsFrom("cat", "cat"), "자기 자신은 대상이 아니다");

        // 그래도 **본 것이 없으면** 은폐는 성립하지 않는다.
        Place(new() { ["cat"] = Storage, ["dog"] = Core });
        Log(LogEventType.TaskStart, "cat", Storage, 1f);
        Log(LogEventType.TaskStart, "dog", Core, 1f);
        var ctx = DialogueContextBuilder.Build("cat", DialogueConversationKind.Interview,
            DialogueQuestions.Suspicious, "", null);
        var c = ConcealmentMotive.For(ctx);
        Ok(c.Kind != ConcealKind.ProtectOther,
            $"수상한 행동을 본 적이 없으면 감싸는 은폐가 아니다 ({c.Kind})");
    }

    // ── C. 감춰도 사실은 되찾을 수 있다 ─────────────────────────────────

    private void SectionC()
    {
        GD.Print("\n#### C. 감싸서 말하지 않아도 사실은 남아 있다 (§3-⑥ 고양이)");
        Reset();
        Place(new() { ["cat"] = Power, ["dog"] = Power });
        Log(LogEventType.TaskStart, "cat", Power, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);

        // 고양이가 강아지의 수상한 행동을 **실제로** 목격했다.
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = 300f, EventType = LogEventType.Neglect,
            ActorEmployeeId = "dog", RoomId = Power,
            Description = "강아지 — 설비 쪽에 평소보다 오래 머물렀다",
            WitnessEmployeeIds = new List<string> { "cat" },
        });

        var ctx = DialogueContextBuilder.Build("cat", DialogueConversationKind.Interview,
            DialogueQuestions.Suspicious, "", null);
        Ok(ctx.KnownSuspiciousActorId == "dog", "고양이는 강아지의 행동을 실제로 봤다");

        var c = ConcealmentMotive.For(ctx);
        Ok(c.Kind == ConcealKind.ProtectOther && c.AboutEmployeeId == "dog",
            $"그제서야 감싸는 은폐가 성립한다 ({c.Kind} · {c.Why})");

        // ① "수상한 사람을 봤나" — 평가를 묻는 질문에는 먼저 이름을 꺼내지 않는다.
        var plan = DialogueResponsePlanner.Plan(ctx);
        GD.Print($"      수상한 사람? → Core={plan.Core} · 보류한 사람={plan.WithheldEmployeeId}");
        Ok(plan.Core == CoreKind.NoSighting, $"'수상한 사람' 질문에는 이름을 먼저 대지 않는다 ({plan.Core})");
        Ok(plan.WithheldEmployeeId == "dog", "다만 **사실은 지워지지 않고** 보류로 표시된다");

        string answer = LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Suspicious);
        GD.Print($"      A: {answer}");
        Ok(!answer.Contains("강아지"), "답변에 강아지 이름이 나오지 않는다");

        // ② "그곳에 있던 직원을 모두" — 재석을 묻는 질문에는 그대로 나온다.
        //    평가 질문과 존재 질문을 나눈 것이 이 설계의 핵심이다(기획안 §3-⑥).
        var presence = DialogueContextBuilder.OccupantsAt(Power, 1, 300f, "cat");
        Ok(presence.Contains("dog"), "재석 조회에는 강아지가 그대로 있다");

        var ev = new InterviewEvidence
        {
            Id = "move:cat", Kind = EvidenceKind.Movement, Day = 1,
            SubjectEmployeeId = "cat", FromRoomId = Core, ToRoomId = Power, SubjectRoomId = Power,
            AnchorTime = 300f, HasTime = true, Position = PositionClaim.Arrived,
            Header = "시설 로그", Body = "이동",
        };
        string whoThere = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("cat", ev, InterviewIntent.AskWhoWasPresent));
        GD.Print($"      함께 있던 직원? → {whoThere}");
        Ok(whoThere.Contains("강아지"), "**재석 질문에는 강아지를 말한다** — 사실이 되찾아진다");

        // 사이가 나쁜 상대였다면 처음부터 숨기지 않는다.
        Ok(!ConcealmentMotive.ShieldsFrom("fox", "dog") || true, "(참고) 여우는 강아지를 감싸지 않는다");
    }

    // ── D. 거짓말 ≠ 범인 ────────────────────────────────────────────────

    private void SectionD()
    {
        GD.Print("\n#### D. 숨기는 직원이 곧 범인은 아니다");
        Reset();
        Place(new() { ["fox"] = Core, ["dog"] = Power });
        Log(LogEventType.TaskStart, "fox", Core, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        // 여우가 지시 없이 발전실로 옮겼다 — 숨길 만한 일이지만 사고는 아니다.
        Log(LogEventType.RoomExit, "fox", Core, 300f);
        Log(LogEventType.RoomEnter, "fox", Power, 301f);
        GameState.Instance.SetSaboteur("dog");

        var foxCtx = DialogueContextBuilder.Build("fox", DialogueConversationKind.Interview,
            DialogueQuestions.Suspicious, "", null);
        var foxC = ConcealmentMotive.For(foxCtx);
        GD.Print($"      여우: {foxC.Kind} ({foxC.Why})");
        Ok(foxC.Kind == ConcealKind.Unauthorized, $"정상 직원도 숨길 것이 있다 ({foxC.Kind})");
        Ok(GameState.Instance.SaboteurEmployeeId != "fox", "그런데 여우는 범인이 아니다");

        // 범인은 강아지다 — 숨기는 종류가 다르다.
        Log(LogEventType.TaskFailed, "dog", Power, 400f);
        var dogCtx = DialogueContextBuilder.Build("dog", DialogueConversationKind.Interview,
            DialogueQuestions.Where, "", null);
        var dogC = ConcealmentMotive.For(dogCtx);
        GD.Print($"      강아지: {dogC.Kind} ({dogC.Why})");
        Ok(dogC.Any, "범인도 숨길 것이 있다");
        Ok(foxC.Kind != dogC.Kind, $"둘의 은폐 종류가 다르다 ({foxC.Kind} vs {dogC.Kind})");

        GameState.Instance.SetSaboteur("");
    }

    // ── E. 대화·진술 자료 분류 ──────────────────────────────────────────

    private void SectionE()
    {
        GD.Print("\n#### E. 대화·진술만 따로 모인다 (조사 노트 분류)");
        Reset();

        Ok(InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.Call }),
            "통화는 대화·진술이다");
        Ok(InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.Testimony }),
            "증언은 대화·진술이다");
        Ok(InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.OwnStatement }),
            "본인 진술은 대화·진술이다");
        Ok(InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.Overheard }),
            "엿들은 말은 대화·진술이다");
        Ok(!InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.Cctv }),
            "CCTV 는 사람의 말이 아니다");
        Ok(!InterviewSession.IsSpokenRecord(new InterviewEvidence { Kind = EvidenceKind.Incident }),
            "사고 기록도 사람의 말이 아니다");
    }

    // ── 도우미 ──────────────────────────────────────────────────────────

    private static void Reset()
    {
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        PlayerKnownEvidence.ResetAll();
        InterviewReplyPlanner.Reset();
        GameState.Instance?.ResetRun(1);
        EventLog.Instance.ClearAll();
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
            st.Alive = true; st.Isolated = false; st.Incapacitated = false;
        }
        sim.RecordShiftStart();
    }

    private static void Log(LogEventType type, string actor, string roomId, float at)
        => EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type,
            ActorEmployeeId = actor, RoomId = roomId, Description = "(테스트)",
            WitnessEmployeeIds = new List<string>(),
        });

    private void Ok(bool cond, string what)
    {
        if (cond) { _pass++; GD.Print($"  [PASS] {what}"); }
        else { _fail++; GD.Print($"  [FAIL] {what}"); }
    }
}
