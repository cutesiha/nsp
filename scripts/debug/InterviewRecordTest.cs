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
//   E 한 발언을 두 주장으로 나눠 조사할 수 있는가 (§4 조작 흐름)
//   F 그 흐름이 **실제 조작 경로**(Toggle → QuestionsForSelection)를 통과하는가
//   G 요약하지 않은 원문 발언을 골라 범위를 물을 수 있는가
//   H 어제 한 발언과 정정 전 발언을 각각 고를 수 있는가
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
        SectionE();
        SectionF();
        SectionG();
        SectionH();
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

    // ── E. 발언을 골라 그 주장으로 묻는다 (기획안 §4 조작 흐름) ─────────
    //
    //   "강아지가 발전실에 있었어요. 기계를 만지는 건 못 봤지만요."
    //     → 주장 A: 강아지가 발전실에 있는 모습을 봤다
    //     → 주장 B: 조작하는 장면은 보지 못했다
    //   A 와 B 에서 나오는 질문이 다르고, B 로는 아무것도 추궁할 수 없어야 한다.

    private void SectionE()
    {
        GD.Print("\n#### E. 한 발언을 두 주장으로 나눠 조사한다");
        Reset();
        Place(new() { ["sheep"] = Power, ["dog"] = Power });
        Log(LogEventType.TaskStart, "sheep", Power, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);

        // 양이 관리자에게 한 말 — 두 주장이 한 번에 들어온다.
        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "기계 앞에 서 있었다", 1);
        PlayerKnownEvidence.RecordNotObserved("sheep", "dog", Power, "설비를 조작하는 장면", 300f, 1);

        // ① 말한 본인(양)의 조사 노트에 두 주장이 **따로** 올라온다.
        var sheepBoard = InterviewEvidenceBoard.Build("sheep");
        var saidCards = sheepBoard.Where(e => e.Kind == EvidenceKind.Testimony
                                              && e.SpeakerEmployeeId == "sheep").ToList();
        GD.Print("      양의 노트: " + string.Join(" / ", saidCards.Select(c => c.Body)));
        Ok(saidCards.Count == 2, $"① 한 발언이 주장 두 장으로 올라온다 ({saidCards.Count})");
        Ok(saidCards.Count(c => c.NotObserved) == 1, "   그중 한 장은 '못 봤다' 주장이다");

        var positive = saidCards.FirstOrDefault(c => !c.NotObserved);
        var negative = saidCards.FirstOrDefault(c => c.NotObserved);
        if (positive == null || negative == null) return;

        // ② 주장 A 로는 말한 본인에게 되물을 수 있다 — 무엇을 하고 있었나 / 얼마나 봤나.
        var qa = InterviewQuestionFactory.For("sheep", positive).Select(q => q.Intent).ToList();
        GD.Print($"      A 주장 → {string.Join(",", qa)}");
        Ok(qa.Contains(InterviewIntent.AskSeenPersonAction), "② '강아지는 뭘 하고 있었나' 를 되물을 수 있다");
        Ok(qa.Contains(InterviewIntent.AskObservationWindow), "   '얼마나 지켜봤나' 를 되물을 수 있다");

        // ③ 주장 B 로는 관찰 범위만 되물을 수 있다.
        var qb = InterviewQuestionFactory.For("sheep", negative).Select(q => q.Intent).ToList();
        GD.Print($"      B 주장 → {string.Join(",", qb)}");
        Ok(qb.Contains(InterviewIntent.AskNotObservedScope), "③ '어느 순간까지 보셨나' 를 되물을 수 있다");
        Ok(!qb.Contains(InterviewIntent.AskSeenPersonAction),
            "   '뭘 하고 있었나' 는 열리지 않는다 — 못 본 것이므로");

        // ④ 주장 A 는 강아지에게 들이밀 수 있고, 주장 B 는 들이밀 수 없다.
        var toDogA = InterviewQuestionFactory.For("dog", positive).Select(q => q.Intent).ToList();
        var toDogB = InterviewQuestionFactory.For("dog", negative).Select(q => q.Intent).ToList();
        GD.Print($"      강아지에게 A → {string.Join(",", toDogA)} / B → {string.Join(",", toDogB)}");
        Ok(toDogA.Contains(InterviewIntent.AskAboutTestimony), "④ 본 것은 상대에게 들이밀 수 있다");
        Ok(toDogB.Count == 0, "   '못 봤다' 로는 상대를 추궁할 수 없다");

        // ⑤ '못 봤다' 는 무엇과 맞대어도 모순이 되지 않는다.
        var cctv = Card(EvidenceKind.Cctv, "dog", Power, 300f, position: true);
        var r = EvidenceContradiction.Check("dog", negative, cctv);
        GD.Print($"      ⑤ {r.Kind} / {r.Verdict} — {r.VerdictText}");
        Ok(!r.IsContradiction, "⑤ '못 봤다' 와 CCTV 를 맞대도 모순이 아니다");
        Ok(r.VerdictText.Contains("근거는 되지 않"), "   이유를 밝힌다");
    }

    // ── F. 실제 조작 경로 ───────────────────────────────────────────────
    //
    // 여기까지의 검사는 질문 생성기(InterviewQuestionFactory)를 직접 불렀다. 그런데
    // 플레이어는 생성기를 부르지 않는다 — **카드를 누르고(Toggle) 질문 버튼을 누른다
    // (QuestionsForSelection).** 그 사이에 InterviewSession.CanUse 라는 문이 하나 더 있고,
    // 그 문이 생성기와 다른 규칙을 들고 있던 동안 §4 의 흐름은 화면에서 막혀 있었다.
    // 이 절은 **세션을 통과하는지**만 본다.

    private void SectionF()
    {
        GD.Print("\n#### F. 세션(Toggle → QuestionsForSelection)을 실제로 통과한다");
        Reset();
        Place(new() { ["sheep"] = Power, ["dog"] = Power, ["wolf"] = Guard });
        Log(LogEventType.TaskStart, "sheep", Power, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);

        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "기계 앞에 서 있었다", 1);
        PlayerKnownEvidence.RecordNotObserved("sheep", "dog", Power, "설비를 조작하는 장면", 300f, 1);

        // ① 양에게 **양 자신의 증언**을 들고 물을 수 있는가 — 세션까지 통과해야 한다.
        var sheep = new InterviewSession("sheep");
        var own = sheep.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony
                                                  && e.SpeakerEmployeeId == "sheep" && !e.NotObserved);
        Ok(own != null, "① 양의 노트에 양이 한 증언이 있다");
        if (own == null) return;

        Ok(sheep.CanUse(own), "   그 카드를 쓸 수 있다고 판정한다");
        sheep.Toggle(own.Id);
        Ok(sheep.Selected.Count == 1, "   카드를 눌러 **실제로 선택된다**");
        var qs = sheep.QuestionsForSelection().Select(q => q.Intent).ToList();
        GD.Print($"      세션 질문 → {string.Join(",", qs)}");
        Ok(qs.Contains(InterviewIntent.AskSeenPersonAction), "   선택한 자료로 질문이 뜬다");

        // ② '못 봤다' 카드는 목격 대상(강아지)에게 추궁 수단이 되지 않는다.
        var dog = new InterviewSession("dog");
        var notSeen = dog.Board.FirstOrDefault(e => e.NotObserved);
        Ok(notSeen != null, "② 강아지의 노트에 '못 봤다' 증언이 올라온다");
        if (notSeen != null)
        {
            dog.Toggle(notSeen.Id);
            Ok(dog.QuestionsForSelection().Count == 0,
                "   그 카드로는 강아지에게 물을 질문이 하나도 없다");
        }

        // ③ 남의 자료로 아무나 붙잡고 묻는 것은 여전히 막혀 있다.
        var wolf = new InterviewSession("wolf");
        wolf.SetScope(true);                       // [전원] 보기로 남의 자료까지 읽는다
        var others = wolf.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony
                                                     && e.SpeakerEmployeeId == "sheep"
                                                     && e.SubjectEmployeeId == "dog");
        Ok(others != null, "③ [전원] 보기에서는 남의 증언도 읽을 수 있다");
        if (others != null)
        {
            Ok(!wolf.CanUse(others), "   그러나 늑대에게 그 자료로 물을 수는 없다");
            wolf.Toggle(others.Id);
            Ok(wolf.Selected.Count == 0, "   눌러도 선택되지 않는다(읽기 전용)");
        }

        // ④ 기절 기록 — 쓰러진 사람이 아닌 **같이 있던 직원**도 고를 수 있어야 한다.
        //    (여기도 CanUse 가 막고 있어서 §3-④ 질문 트리가 화면에서 닫혀 있었다.)
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = 400f, EventType = LogEventType.Neglect,
            Detail = LogDetail.Fainted, ActorEmployeeId = "dog", RoomId = Power,
            Description = "(테스트) 기절", WitnessEmployeeIds = new List<string>(),
        });
        var sheep2 = new InterviewSession("sheep");
        var faint = sheep2.Board.FirstOrDefault(e => e.Id.StartsWith("faint:"));
        Ok(faint != null, "④ 같은 방에 있던 양의 노트에 기절 기록이 올라온다");
        if (faint != null)
        {
            sheep2.Toggle(faint.Id);
            var fq = sheep2.QuestionsForSelection().Select(q => q.Intent).ToList();
            GD.Print($"      기절 기록[{faint.Id}] 대상 {faint.SubjectEmployeeId}·{faint.SubjectRoomId}"
                     + $"·{faint.AnchorTime} / 현장 인원 "
                     + string.Join(",", DialogueContextBuilder.OccupantsAt(
                         faint.SubjectRoomId, faint.SubjectDay, faint.AnchorTime, faint.SubjectEmployeeId))
                     + $" / 양 위치 {DialogueContextBuilder.RoomAt("sheep", 1, faint.AnchorTime)}");
            GD.Print($"      기절 기록 → {string.Join(",", fq)}");
            Ok(sheep2.Selected.Count == 1, "   발견자가 그 기록을 고를 수 있다");
            Ok(fq.Contains(InterviewIntent.AskFoundWhere), "   '어디서 발견했나' 가 뜬다");
        }
    }

    // ── G. 원문 발언 ────────────────────────────────────────────────────
    //
    // "오늘은 별일 없었습니다" 는 어느 요약 카드에도 올라오지 않던 말이다.
    // 요약할 위치도 행동도 없기 때문이다 — 그래서 그 말을 쥐고 범위를 캐물을 수 없었다.

    private void SectionG()
    {
        GD.Print("\n#### G. 요약하지 않은 원문 발언을 골라 범위를 묻는다");
        Reset();
        DialogueHistory.Instance?.ClearAll();
        Place(new() { ["cat"] = Storage });
        Log(LogEventType.TaskStart, "cat", Storage, 1f);

        // 실제 대화창이 저장하는 그 경로로 넣는다(자료에 묶이지 않은 답 = 뭉뚱그린 말).
        DialogueHistory.Instance?.AddEntry("manager", "관리자", DialogueEntryType.PlayerChoice,
            "오늘 근무는 어땠습니까?", DialogueConversationType.Interview, "cat");
        DialogueHistory.Instance?.AddEntry("cat", "고양이", DialogueEntryType.NpcResponse,
            "오늘은 별일 없었습니다. 평소대로 했어요.", DialogueConversationType.Interview, "cat");
        // 자료를 들고 한 질문의 답은 이미 요약 카드에 붙으므로 원문 카드가 되지 않는다.
        DialogueHistory.Instance?.AddEntry("cat", "고양이", DialogueEntryType.NpcResponse,
            "그 시각에는 저장고에 있었습니다.", DialogueConversationType.Interview, "cat",
            new[] { "incident:TaskFailed:storage_room:200.0" });

        var session = new InterviewSession("cat");
        var spoken = session.Board.Where(e => e.SpokenLine).ToList();
        GD.Print("      원문 카드: " + string.Join(" / ", spoken.Select(s => s.Body)));
        Ok(spoken.Count == 1, $"① 자료에 묶이지 않은 발언만 원문 카드가 된다 ({spoken.Count})");
        if (spoken.Count == 0) return;

        var line = spoken[0];
        Ok(line.Body.Contains("별일 없었"), "   카드에 **원문 그대로** 실린다");
        Ok(line.Position == PositionClaim.None, "   위치 주장이 아니다");

        // ② 「대화·진술」 탭에 올라오고, 골라서 질문할 수 있다.
        session.SetTab(NoteTab.Spoken);
        Ok(session.SpokenRecords().Any(e => e.Id == line.Id), "② 대화·진술 탭에 올라온다");
        session.Toggle(line.Id);
        Ok(session.Selected.Count == 1, "   눌러서 선택된다");
        var q = session.QuestionsForSelection();
        GD.Print($"      질문 → {string.Join(" / ", q.Select(x => x.Text))}");
        Ok(q.Any(x => x.Intent == InterviewIntent.AskStatementScope), "   범위를 묻는 질문이 뜬다");
        Ok(q.Any(x => x.Text.Contains("별일 없었")), "   질문이 그 발언을 인용한다");

        // ③ 답이 실제로 나온다(빈 문장이 아니다).
        var turn = session.Ask(q.First(x => x.Intent == InterviewIntent.AskStatementScope));
        GD.Print($"      답 → \"{turn.Answer}\"");
        Ok(turn.Answer.Length > 0, "③ 답변이 비어 있지 않다");
        Ok(!turn.Answer.Contains("{") && !turn.Answer.Contains("은/는"),
            "   치환되지 않은 자리표시자가 남지 않는다");

        // ④ 범위를 말하지 않은 발언으로는 모순을 세울 수 없다.
        var incident = Card(EvidenceKind.Incident, "", Storage, 200f);
        var r = EvidenceContradiction.Check("cat", line, incident);
        GD.Print($"      ④ {r.Verdict} — {r.VerdictText}");
        Ok(!r.IsContradiction, "④ 원문 발언만으로는 모순이 성립하지 않는다");
        Ok(r.VerdictText.Contains("어디까지"), "   먼저 범위를 물어야 한다고 알려 준다");
    }

    // ── H. 이전 날짜 · 정정 전 발언 ──────────────────────────────────────

    private void SectionH()
    {
        GD.Print("\n#### H. 어제 한 발언과 정정 전 발언을 각각 고를 수 있다");
        Reset();
        Place(new() { ["fox"] = Core });
        const string key = "TaskFailed:core_room:300.0";

        // DAY1 — 핵심제어실이라고 말했다가 저장고로 고쳤다.
        PlayerKnownEvidence.RecordLocationStatement("fox", key, Core, true, 300f, 1);
        PlayerKnownEvidence.RecordLocationStatement("fox", key, Storage, true, 300f, 1);

        var day1 = new InterviewSession("fox");
        var claims1 = day1.Board.Where(e => e.Kind == EvidenceKind.OwnStatement && !e.SpokenLine).ToList();
        GD.Print("      DAY1 진술 카드: " + string.Join(" / ", claims1.Select(c => c.Body)));
        Ok(claims1.Count == 2, $"① 정정 전 발언과 지금 발언이 **각각** 카드가 된다 ({claims1.Count})");
        Ok(claims1.Count(c => c.Superseded) == 1, "   그중 한 장이 정정 전 발언이다");
        foreach (var c in claims1)
        {
            day1.ClearSelection();
            day1.Toggle(c.Id);
            Ok(day1.Selected.Count == 1, $"   고를 수 있다 — {c.Body}");
        }

        // DAY3 로 넘어간다. 진술 기록은 판이 끝날 때까지 남는다.
        GameState.Instance.GoToNextDay();
        GameState.Instance.GoToNextDay();
        Ok(DialogueContextBuilder.Day() == 3, $"② DAY{DialogueContextBuilder.Day()} 로 넘어왔다");

        var day3 = new InterviewSession("fox");
        var claims3 = day3.Board.Where(e => e.Kind == EvidenceKind.OwnStatement && !e.SpokenLine).ToList();
        GD.Print("      DAY3 노트: " + string.Join(" / ",
            claims3.Select(c => $"DAY{c.Day}·대상DAY{c.SubjectDay} {c.Body}")));
        Ok(claims3.Count == 2, $"③ DAY1 에 한 위치 진술을 DAY3 에도 찾을 수 있다 ({claims3.Count})");
        Ok(claims3.All(c => c.Day == 1), "   말한 날이 DAY1 로 남아 있다(오늘 날짜로 덮이지 않는다)");
        Ok(claims3.All(c => c.SubjectDay == 1), "   가리키는 날도 DAY1 이다");

        var old = claims3.FirstOrDefault(c => c.Superseded);
        if (old != null)
        {
            day3.Toggle(old.Id);
            Ok(day3.Selected.Count == 1, "④ DAY3 에서 DAY1 의 정정 전 발언을 고를 수 있다");
            Ok(day3.QuestionsForSelection().Count > 0, "   그 발언으로 물을 질문이 있다");
        }

        // ⑤ DAY1 진술과 DAY3 기록을 맞대면 날짜가 다르다고 막힌다(1단계에서 세운 규칙).
        var todayRecord = Card(EvidenceKind.Cctv, "fox", Core, 300f, position: true);
        todayRecord.Day = 3;
        var r = EvidenceContradiction.Check("fox", old ?? claims3[0], todayRecord);
        GD.Print($"      ⑤ {r.Verdict} — {r.VerdictText}");
        Ok(!r.IsContradiction, "⑤ 어제 발언과 오늘 기록은 모순으로 묶이지 않는다");
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
