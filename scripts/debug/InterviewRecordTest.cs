using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 심문 3단계 — **대화 기록으로 추궁하기** 검증.
//
//   godot --headless --path . res://scenes/debug/InterviewRecordTest.tscn --quit-after 4000
//
//   A 진술을 고쳐도 **이전 진술이 지워지지 않는가**
//   B "못 봤다" 가 부재의 증거로 쓰이지 않는가
//   C 모순 판정이 네 상태로 갈리는가 (빨간색 = 거짓말 이 아니다)
//   D 증언을 그 말이 가리키는 사람에게 들이밀 수 있는가 (목격 트리 5단계)
public partial class InterviewRecordTest : Node
{
    private const string Power = "power_room";
    private const string Guard = "guard_room";
    private const string Storage = "storage_room";
    private const string Core = "core_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 진술 정정 이력 ───────────────────────────────────────────────

    private void SectionA()
    {
        GD.Print("\n#### A. 진술을 고쳐도 이전 진술이 남는다");
        Reset();

        PlayerKnownEvidence.RecordLocationStatement("fox", "k1", Guard, true, 300f, 1);
        var st = PlayerKnownEvidence.StatementsBy("fox").First();
        Ok(st.RoomId == Guard, "처음에는 경비실이라고 말했다");
        Ok(!st.WasCorrected, "아직 정정한 적 없다");

        // 추궁 뒤 진술을 고친다.
        PlayerKnownEvidence.RecordLocationStatement("fox", "k1", Power, true, 300f, 1);
        st = PlayerKnownEvidence.StatementsBy("fox").First();
        Ok(st.RoomId == Power, "지금 주장은 발전실이다");
        Ok(st.WasCorrected, "정정한 적이 있다고 표시된다");
        Ok(st.FirstRoomId == Guard, $"**처음 한 말(경비실)이 지워지지 않았다** (실제 {InterviewEvidenceBoard.RoomName(st.FirstRoomId)})");
        Ok(st.RoomHistory.Count == 2, $"이력이 순서대로 쌓인다 ({string.Join("→", st.RoomHistory.Select(InterviewEvidenceBoard.RoomName))})");

        // 같은 말을 또 해도 이력이 늘지 않는다 — 반복은 정정이 아니다.
        PlayerKnownEvidence.RecordLocationStatement("fox", "k1", Power, true, 300f, 1);
        st = PlayerKnownEvidence.StatementsBy("fox").First();
        Ok(st.RoomHistory.Count == 2, "같은 말을 반복한 것은 정정으로 세지 않는다");
    }

    // ── B. "못 봤다" 는 부재의 증거가 아니다 ────────────────────────────

    private void SectionB()
    {
        GD.Print("\n#### B. '못 봤다' 와 '봤다' 를 섞지 않는다");
        Reset();

        // 양이 강아지를 봤다고 말했고, 조작 장면은 못 봤다고 말했다.
        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "기계 앞에 서 있었다", 1);
        PlayerKnownEvidence.RecordNotObserved("sheep", "dog", Power, "설비를 조작하는 장면", 300f, 1);

        var seen = PlayerKnownEvidence.SightingsOf("dog").ToList();
        var notSeen = PlayerKnownEvidence.NotObservedOf("dog").ToList();
        Ok(seen.Count == 1 && !seen[0].NotObserved,
            $"'봤다' 는 목격으로 한 건 ({seen.Count})");
        Ok(notSeen.Count == 1 && notSeen[0].NotObserved,
            $"'못 봤다' 는 따로 한 건 ({notSeen.Count})");
        Ok(seen.All(s => !s.NotObserved), "목격 조회에 '못 봤다' 가 섞이지 않는다");

        // 목격 상세를 읽을 때도 '못 봤다' 를 끌어오지 않는다.
        var detail = PlayerKnownEvidence.AllStatementsOfSighting("sheep", "dog", 1);
        Ok(detail != null && detail.Detail == "기계 앞에 서 있었다",
            $"목격 상세는 실제로 본 내용이다 ('{detail?.Detail}')");
        Ok(detail != null && !detail.NotObserved, "'못 봤다' 쪽이 상세로 잡히지 않는다");
    }

    // ── C. 모순은 네 상태로 갈린다 ──────────────────────────────────────

    private void SectionC()
    {
        GD.Print("\n#### C. 빨간색 = 거짓말 이 아니다 — 네 상태");
        Reset();
        Place(new() { ["dog"] = Power, ["sheep"] = Power });
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        Log(LogEventType.TaskStart, "sheep", Power, 1f);

        // ① 확인된 사실 충돌 — 기록(CCTV) ↔ 본인의 명시적 위치 주장
        var claim = Card(EvidenceKind.OwnStatement, "dog", Guard, 300f, position: true);
        var cctv = Card(EvidenceKind.Cctv, "dog", Power, 300f, position: true);
        var r1 = EvidenceContradiction.Check("dog", claim, cctv);
        GD.Print($"      ① {r1.Kind} / {r1.Verdict} — {r1.VerdictText}");
        Ok(r1.Verdict == ConfrontVerdict.FactConflict,
            $"① 기록 ↔ 진술은 '확인된 사실 충돌' ({r1.Verdict})");

        // ② 진술 대립 — 본인의 행동 주장 ↔ 동료의 목격 증언. 누가 맞는지는 모른다.
        var denial = Card(EvidenceKind.OwnStatement, "dog", Power, 300f, behavior: "설비 근처에 가지 않았다");
        var witness = Card(EvidenceKind.Testimony, "dog", Power, 300f, behavior: "패널에 손을 대고 있었다");
        witness.SpeakerEmployeeId = "sheep";
        var r2 = EvidenceContradiction.Check("dog", denial, witness);
        GD.Print($"      ② {r2.Kind} / {r2.Verdict} — {r2.VerdictText}");
        Ok(r2.Verdict == ConfrontVerdict.StatementClash,
            $"② 사람 말 ↔ 사람 말은 '진술 대립' ({r2.Verdict})");
        Ok(r2.VerdictText.Contains("어느 쪽이 맞는지"), "   누가 맞는지는 모른다고 적는다");

        // ③ 해명 필요 — 사고 난 방에 있었다는 것만으로는 충돌이 아니다.
        var incident = Card(EvidenceKind.Incident, "", Power, 300f);
        incident.IncidentKey = InterviewEvidenceBoard.IncidentKeyOf(LogEventType.TaskFailed, Power, 300f);
        incident.IncidentType = LogEventType.TaskFailed;
        var presence = Card(EvidenceKind.Cctv, "dog", Power, 300f, position: true);
        var r3 = EvidenceContradiction.Check("dog", incident, presence);
        GD.Print($"      ③ {r3.Kind} / {r3.Verdict} — {r3.VerdictText}");
        Ok(r3.Verdict == ConfrontVerdict.NeedsExplanation,
            $"③ 사고 난 방에 있었다는 것은 '해명 필요' ({r3.Verdict})");

        // ④ 모순 없음 — 같은 방을 가리키는 두 자료.
        var same = Card(EvidenceKind.Movement, "dog", Power, 300f, position: true);
        var r4 = EvidenceContradiction.Check("dog", presence, same);
        GD.Print($"      ④ {r4.Kind} / {r4.Verdict} — {r4.VerdictText}");
        Ok(r4.Verdict == ConfrontVerdict.None, $"④ 양립 가능하면 '모순 없음' ({r4.Verdict})");

        Ok(r1.Verdict != r2.Verdict && r2.Verdict != r3.Verdict,
            "네 상태가 실제로 서로 다르게 나온다");
    }

    // ── D. 증언을 그 말이 가리키는 사람에게 ─────────────────────────────

    private void SectionD()
    {
        GD.Print("\n#### D. 양의 증언을 강아지에게 들이민다 (목격 트리 5단계)");
        Reset();
        Place(new() { ["dog"] = Power, ["sheep"] = Power });
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        Log(LogEventType.TaskStart, "sheep", Power, 1f);

        // 양이 "강아지가 기계 앞에 있었다" 고 말해 둔 상태.
        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "기계 앞에 서 있었다", 1);

        var testimony = Card(EvidenceKind.Testimony, "dog", Power, 300f, behavior: "기계 앞에 서 있었다");
        testimony.SpeakerEmployeeId = "sheep";

        var qs = InterviewQuestionFactory.For("dog", testimony);
        var intents = qs.Select(q => q.Intent).ToList();
        GD.Print($"      강아지에게: {string.Join(",", intents)}");
        Ok(intents.Contains(InterviewIntent.AskAboutTestimony),
            "증언을 그 사람에게 들이미는 질문이 열린다");

        var ask = qs.First(q => q.Intent == InterviewIntent.AskAboutTestimony);
        GD.Print($"      Q: {ask.Text}");
        Ok(ask.Text.Contains("양"), "질문에 증언자 이름이 들어간다");
        Ok(ask.Text.Contains("발전실"), "질문에 그 방이 들어간다");

        string answer = InterviewReplyPlanner.Answer(ask);
        GD.Print($"      A: {answer}");
        Ok(!string.IsNullOrEmpty(answer) && answer != "…", "강아지가 답한다");

        // 결번이면 증언 하나로 무너지지 않는다 — 증언은 기록이 아니다.
        InterviewReplyPlanner.Reset();
        GameState.Instance.SetSaboteur("dog");
        Log(LogEventType.TaskFailed, "dog", Power, 300f);
        var ask2 = InterviewQuestionFactory.Make("dog", testimony, InterviewIntent.AskAboutTestimony, "sheep");
        string a2 = InterviewReplyPlanner.Answer(ask2);
        GD.Print($"      A(결번): {a2}");
        Ok(!a2.Contains("제가 했") && !a2.Contains("고의"), "증언만으로 자백하지 않는다");
        GameState.Instance.SetSaboteur("");

        // 증언자(양)에게는 관찰 범위를 되물을 수 있다.
        string window = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("sheep", testimony, InterviewIntent.AskObservationWindow, "dog"));
        GD.Print($"      A(양·관찰시간): {window}");
        Ok(!string.IsNullOrEmpty(window) && window != "…", "증언자에게 관찰 시간을 되물을 수 있다");
    }

    // ── 도우미 ──────────────────────────────────────────────────────────

    private static InterviewEvidence Card(EvidenceKind kind, string subject, string room, float at,
                                          bool position = false, string behavior = "")
        => new()
        {
            Id = $"{kind}:{subject}:{room}:{at}:{behavior}", Kind = kind, Day = 1,
            SubjectEmployeeId = subject, SubjectRoomId = room,
            AnchorTime = at, HasTime = true,
            Position = position ? PositionClaim.AtRoom : PositionClaim.None,
            BehaviorDetail = behavior,
            Header = kind.ToString(), Body = "(테스트)",
        };

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
