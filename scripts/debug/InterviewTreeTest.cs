using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 심문 2단계 — **이동 기록 · 직원 간 목격** 질문 트리 검증.
//
//   godot --headless --path . res://scenes/debug/InterviewTreeTest.tscn --quit-after 4000
//
//   A 이동 사유마다 첫 질문이 다른가 (배치 / 지시 / 무단 / 실려 감)
//   B 없는 만남을 전제한 질문을 만들지 않는가
//   C "누구를 봤다" 뒤에 "그 사람은 뭘 하고 있었나" 가 이어지는가  ← 기획안 §9 둘째 기준
//   D '봤다' / '만졌다' / '조작했다' 를 뭉개지 않는가
//   E 의심의 근거가 실제 사건일 때만 "수상했다" 고 말하는가
//   F 설비 사고 — 설정 변경 질문과 결번의 일관된 은폐
//   G 정전 — 직접 경험 / 경보만 / 사후 인지 구별
//   H 기절 — 당사자 / 발견자 / 구조자 구분
//   I 이상 개체 — 직접 본 사람만 모습·방향을 말한다
public partial class InterviewTreeTest : Node
{
    private const string Power = "power_room";
    private const string Guard = "guard_room";
    private const string Storage = "storage_room";
    private const string Core = "core_room";
    private const string Medical = "medical_room";

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
        SectionI();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 이동 사유별 첫 질문 ──────────────────────────────────────────

    private void SectionA()
    {
        GD.Print("\n#### A. 이동 사유에 따라 첫 질문이 달라진다");
        Reset();
        Place(new() { ["fox"] = Guard, ["dog"] = Power, ["sheep"] = Core });
        Log(LogEventType.TaskStart, "fox", Guard, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        Log(LogEventType.TaskStart, "sheep", Core, 1f);

        // ① 관리자가 배치한 이동 — "도착했을 때 어땠나" 가 열린다.
        var ordered = Move("fox", Guard, Power, 300f, playerOrdered: true);
        var qOrdered = InterviewQuestionFactory.For("fox", ordered).Select(q => q.Intent).ToList();
        Ok(qOrdered.Contains(InterviewIntent.AskArrivalState),
            "① 관리자 배치: '도착했을 때 어떤 상황이었나' 가 열린다");
        Ok(!qOrdered.Contains(InterviewIntent.ConfrontUnorderedMove),
            "   지시받은 이동을 근무지 이탈로 따지지 않는다");

        // ② 지시 없는 이동 — 근무지 이탈 추궁이 열린다.
        var unordered = Move("sheep", Core, Storage, 400f, playerOrdered: false);
        var qFree = InterviewQuestionFactory.For("sheep", unordered).Select(q => q.Intent).ToList();
        Ok(qFree.Contains(InterviewIntent.ConfrontUnorderedMove),
            "② 지시 없는 이동: 근무지 이탈을 따질 수 있다");
        Ok(!qFree.Contains(InterviewIntent.AskArrivalState),
            "   '도착했을 때' 질문은 열리지 않는다 (사고 대응이 아니다)");

        // ③ 쓰러져서 실려 간 이동 — 본인에게 이동 이유를 묻지 않는다.
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = 500f, EventType = LogEventType.Neglect,
            Detail = LogDetail.Fainted, ActorEmployeeId = "dog", RoomId = Power,
            Description = "(테스트) 기절",
        });
        var carried = Move("dog", Power, Medical, 540f, playerOrdered: false);
        var qCarried = InterviewQuestionFactory.For("dog", carried).Select(q => q.Intent).ToList();
        Ok(!qCarried.Contains(InterviewIntent.AskMoveReason),
            "③ 실려 간 사람에게 '왜 이동했나' 를 묻지 않는다");
        Ok(!qCarried.Contains(InterviewIntent.ConfrontUnorderedMove),
            "   근무지 이탈로도 따지지 않는다");

        GD.Print($"      배치 {string.Join(",", qOrdered)}");
        GD.Print($"      무단 {string.Join(",", qFree)}");
        GD.Print($"      이송 {string.Join(",", qCarried)}");
    }

    // ── B. 없는 만남을 전제하지 않는다 ──────────────────────────────────

    private void SectionB()
    {
        GD.Print("\n#### B. 도착한 방에 아무도 없으면 '만나러 갔나' 를 묻지 않는다");
        Reset();
        Place(new() { ["fox"] = Guard, ["dog"] = Power });
        Log(LogEventType.TaskStart, "fox", Guard, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);

        // 발전실에는 강아지가 있다 → 만남을 물을 수 있다.
        var toPower = Move("fox", Guard, Power, 300f, playerOrdered: false);
        Ok(InterviewQuestionFactory.ColleagueAt("fox", toPower) == "dog",
            "발전실에 실제로 강아지가 있었다");
        var withDog = InterviewQuestionFactory.For("fox", toPower).Select(q => q.Intent).ToList();
        Ok(withDog.Contains(InterviewIntent.AskVisitPurpose),
            "그 경우에만 '만나러 간 이유' 질문이 열린다");

        // 저장고에는 아무도 없다 → 묻지 않는다.
        var toEmpty = Move("fox", Power, Storage, 600f, playerOrdered: false);
        Ok(InterviewQuestionFactory.ColleagueAt("fox", toEmpty) == "",
            "저장고에는 아무도 없었다");
        Ok(!InterviewQuestionFactory.For("fox", toEmpty).Any(q => q.Intent == InterviewIntent.AskVisitPurpose),
            "아무도 없던 방에 대해서는 만남을 묻지 않는다");
    }

    // ── C. "누구를 봤다" → "그 사람은 뭘 하고 있었나" ────────────────────

    private void SectionC()
    {
        GD.Print("\n#### C. 이름이 나오면 그 사람을 파고들 수 있다");
        Reset();
        Place(new() { ["sheep"] = Core, ["dog"] = Power, ["fox"] = Guard });
        Log(LogEventType.TaskStart, "sheep", Core, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        Log(LogEventType.TaskStart, "fox", Guard, 1f);
        Move("sheep", Core, Power, 300f, playerOrdered: false);

        // 실제 조사 노트에 올라온 이동 자료로 묻는다 — 손으로 만든 카드가 아니다.
        var session = new InterviewSession("sheep");
        var ev = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement
                                                   && e.ToRoomId == Power);
        Ok(ev != null, "조사 노트에 저장고→발전실 이동 자료가 올라왔다");
        if (ev == null) return;

        var q = InterviewQuestionFactory.Make("sheep", ev, InterviewIntent.AskWhoWasPresent);
        var turn = session.Ask(q);
        GD.Print($"      Q: {q.Text}\n      A: {turn.Answer}");

        var frame = InterviewReplyPlanner.FrameFor(q);
        Ok(frame.MentionedEmployeeId == "dog",
            $"답변이 강아지를 입에 올렸다 (실제 '{frame.MentionedEmployeeId}')");

        var follow = turn.FollowUps.Select(x => x.Intent).ToList();
        Ok(follow.Contains(InterviewIntent.AskSeenPersonAction),
            $"'강아지는 뭘 하고 있었나' 가 이어진다 ({string.Join(",", follow)})");
        var action = turn.FollowUps.First(x => x.Intent == InterviewIntent.AskSeenPersonAction);
        Ok(action.OtherEmployeeId == "dog", "그 질문이 강아지를 가리킨다");
        Ok(action.Text.Contains("강아지"), $"질문 문장에 이름이 들어간다: {action.Text}");

        var answer2 = session.Ask(action);
        GD.Print($"      Q: {action.Text}\n      A: {answer2.Answer}");
        Ok(!string.IsNullOrEmpty(answer2.Answer) && answer2.Answer != "…", "그 질문에 답이 나온다");

        // 아무도 없었던 경우에는 그 사슬이 아예 열리지 않는다.
        Move("fox", Guard, Storage, 700f, playerOrdered: false);   // 저장고에는 아무도 없다
        var alone = new InterviewSession("fox");
        var evAlone = alone.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement
                                                      && e.ToRoomId == Storage);
        Ok(evAlone != null, "여우의 저장고 이동 자료도 올라왔다");
        if (evAlone == null) return;
        var tAlone = alone.Ask(InterviewQuestionFactory.Make("fox", evAlone, InterviewIntent.AskWhoWasPresent));
        GD.Print($"      A(혼자): {tAlone.Answer}");
        Ok(!tAlone.FollowUps.Any(x => x.Intent == InterviewIntent.AskSeenPersonAction),
            "혼자였다고 답하면 '그 사람은' 질문이 열리지 않는다");
    }

    // ── D. '봤다' 와 '조작했다' 를 뭉개지 않는다 ─────────────────────────

    private void SectionD()
    {
        GD.Print("\n#### D. 관찰 범위를 넘어서 말하지 않는다");
        Reset();
        Place(new() { ["sheep"] = Power, ["dog"] = Power });
        Log(LogEventType.TaskStart, "sheep", Power, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);

        var ev = Movement("sheep", Core, Power, 300f, false);

        // ① 방에 있는 것만 봤다고 기록된 경우.
        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "", 1);
        string onlyThere = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("sheep", ev, InterviewIntent.AskSeenPersonAction, "dog"));
        GD.Print($"      (상세 없음) {onlyThere}");
        Ok(onlyThere.Contains("못 봤") || onlyThere.Contains("모르겠"),
            "상세 기록이 없으면 '거기까지는 못 봤다' 고 답한다");

        string scopeNo = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("sheep", ev, InterviewIntent.AskSeenScope, "dog"));
        GD.Print($"      (조작 봤나) {scopeNo}");
        Ok(!scopeNo.Contains("네."), "조작 장면을 봤다고 말하지 않는다");

        // ② 설비를 만지는 것까지 봤다고 기록된 경우 — 그때만 "봤다" 가 된다.
        InterviewReplyPlanner.Reset();
        PlayerKnownEvidence.ResetAll();
        PlayerKnownEvidence.RecordSighting("sheep", "dog", Power, 300f, "설비 쪽에 손을 대고 있었다", 1);
        string scopeYes = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("sheep", ev, InterviewIntent.AskSeenScope, "dog"));
        GD.Print($"      (상세 있음) {scopeYes}");
        Ok(scopeYes.Contains("봤") && !scopeYes.Contains("못 봤"),
            "설비 접촉까지 기록돼 있을 때만 봤다고 답한다");
    }

    // ── E. 의심의 근거는 실제 사건이어야 한다 ───────────────────────────

    private void SectionE()
    {
        GD.Print("\n#### E. 근거 없이 '수상했다' 고 말하지 않는다");
        Reset();
        Place(new() { ["cat"] = Power, ["dog"] = Power });
        Log(LogEventType.TaskStart, "cat", Power, 1f);
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        PlayerKnownEvidence.RecordSighting("cat", "dog", Power, 300f, "기계 앞에 서 있었다", 1);

        var ev = Movement("cat", Core, Power, 300f, false);

        // ① 사고가 없었다 — 고양이는 수상했다고 단정하지 않는다(기획안 §3-⑥ 고양이 사례).
        string noReason = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("cat", ev, InterviewIntent.AskWhySuspicious, "dog"));
        GD.Print($"      (사고 없음) {noReason}");
        Ok(noReason.Contains("수상") && (noReason.Contains("아니") || noReason.Contains("없")),
            "사고가 없으면 '수상하다고 한 적 없다' 고 답한다");

        // ② 같은 방에서 실제로 사고가 났다 — 그때는 근거를 댄다.
        InterviewReplyPlanner.Reset();
        Log(LogEventType.TaskFailed, "", Power, 305f);   // 목격 직후(판정 폭 20분 = 실제 10초 안)
        string withReason = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("cat", ev, InterviewIntent.AskWhySuspicious, "dog"));
        GD.Print($"      (사고 있음) {withReason}");
        Ok(withReason.Contains("발전실") || withReason.Contains("고장") || withReason.Contains("사고"),
            "실제 사고를 근거로 댄다");
    }

    // ── F. 설비 사고 · 방해공작 ─────────────────────────────────────────
    //
    // 이 유형의 중심은 Q4 "설비에 손을 대거나 설정을 변경했습니까" 다.
    // 결번 개체의 은폐는 **질문을 다시 눌러도 흔들리지 않아야** 한다(기획안 §5).

    private void SectionF()
    {
        GD.Print("\n#### F. 설비 사고 — 설정 변경 질문과 일관된 은폐");
        Reset();
        Place(new() { ["dog"] = Power, ["cat"] = Storage });
        Log(LogEventType.TaskStart, "dog", Power, 1f);
        Log(LogEventType.TaskStart, "cat", Storage, 1f);
        // 방해공작으로 난 사고 — 행위자가 강아지로 남는다. 그래야 "설비는 건드리지
        // 않았다" 는 은폐 주장이 성립한다(행위자가 아니면 숨길 것이 없다).
        Log(LogEventType.TaskFailed, "dog", Power, 300f);
        GameState.Instance.SetSaboteur("dog");

        var incident = Incident(Power, 300f);

        // ① 설비 사고에만 설비 질문이 열린다.
        var qs = InterviewQuestionFactory.For("dog", incident).Select(q => q.Intent).ToList();
        GD.Print($"      강아지에게 열린 질문: {string.Join(",", qs)}");
        Ok(qs.Contains(InterviewIntent.AskEquipmentFault), "① '사고 전에 설비 이상이 있었나' 가 열린다");
        Ok(qs.Contains(InterviewIntent.AskEquipmentTouch),
            "   현장에 있던 직원에게 '설정을 변경했나' 가 열린다");

        // 설비와 무관한 사고에는 열리지 않는다.
        var death = Incident(Power, 300f);
        death.IncidentType = LogEventType.Death;
        Ok(!InterviewQuestionFactory.For("dog", death).Any(q => q.Intent == InterviewIntent.AskEquipmentTouch),
            "   설비와 무관한 사고에는 열리지 않는다");

        // ② 결번의 답은 한 번 정해지면 바뀌지 않는다.
        var qTouch = InterviewQuestionFactory.Make("dog", incident, InterviewIntent.AskEquipmentTouch);
        string a1 = InterviewReplyPlanner.Answer(qTouch);
        var frame = InterviewReplyPlanner.FrameFor(qTouch);
        GD.Print($"      Q: {qTouch.Text}\n      A: {a1}  [{frame.Variant}]");

        InterviewReplyPlanner.Reset();   // 기억(메모)만 비운다 — 주장은 남아 있어야 한다
        string a2 = InterviewReplyPlanner.Answer(qTouch);
        var frame2 = InterviewReplyPlanner.FrameFor(qTouch);
        Ok(frame.Variant == frame2.Variant,
            $"② 같은 질문을 다시 물어도 같은 전략이다 ({frame.Variant} → {frame2.Variant})");

        // ③ 부인했다면 그 주장이 자료로 남아야 한다 — 나중에 CCTV 와 맞댈 수 있어야 하므로.
        if (frame.Variant == "deny")
        {
            bool recorded = PlayerKnownEvidence.BehaviorClaimsBy("dog").Count > 0;
            Ok(recorded, "③ '설비는 건드리지 않았다' 가 조사 자료로 남는다");
        }
        else
        {
            Ok(true, $"③ 이번 판에서 강아지는 부인을 고르지 않았다 ({frame.Variant}) — 전략은 판마다 정해진다");
        }

        // ④ 답에 따라 꼬리질문이 갈라진다.
        var session = new InterviewSession("dog");
        var boardIncident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        Ok(boardIncident != null, "④ 조사 노트에 사고 자료가 올라왔다");
        if (boardIncident != null)
        {
            var turn = session.Ask(InterviewQuestionFactory.Make("dog", boardIncident,
                InterviewIntent.AskEquipmentTouch));
            var follow = turn.FollowUps.Select(x => x.Intent).ToList();
            GD.Print($"      A: {turn.Answer}\n      → {string.Join(",", follow)}");
            Ok(follow.Count > 0, "   답에서 이어지는 꼬리질문이 나온다");
            Ok(!follow.Contains(InterviewIntent.AskEquipmentTouch), "   같은 질문을 되풀이하지 않는다");
        }

        // ⑤ 사고를 몰랐던 직원에게는 "누구에게 알렸나" 를 묻지 않는다.
        var qsCat = InterviewQuestionFactory.For("cat", incident).Select(q => q.Intent).ToList();
        Ok(!qsCat.Contains(InterviewIntent.AskWhoReported),
            $"⑤ 사고를 모른 고양이에게는 '누구에게 알렸나' 를 묻지 않는다 ({string.Join(",", qsCat)})");

        GameState.Instance.SetSaboteur("");
    }

    // ── G. 정전 — 직접 겪은 것과 나중에 들은 것 ─────────────────────────

    private void SectionG()
    {
        GD.Print("\n#### G. 정전 — 인지 수준에 따라 답과 꼬리질문이 갈린다");
        Reset();
        // 양은 발전실(정전 현장), 여우는 멀리 떨어진 경비실.
        Place(new() { ["sheep"] = Power, ["fox"] = Guard });
        Log(LogEventType.TaskStart, "sheep", Power, 1f);
        Log(LogEventType.TaskStart, "fox", Guard, 1f);
        Log(LogEventType.PowerOutage, "", Power, 300f);

        var blackout = new InterviewEvidence
        {
            Id = "incident:blackout", Kind = EvidenceKind.Incident, Day = 1,
            SubjectRoomId = Power, AnchorTime = 300f, HasTime = true,
            IncidentKey = InterviewEvidenceBoard.IncidentKeyOf(LogEventType.PowerOutage, Power, 300f),
            IncidentType = LogEventType.PowerOutage,
            Header = "시설 로그", Body = "정전",
        };

        var qs = InterviewQuestionFactory.For("sheep", blackout).Select(q => q.Intent).ToList();
        GD.Print($"      정전 질문: {string.Join(",", qs)}");
        Ok(qs.Contains(InterviewIntent.AskBlackoutExperience), "정전 자료에 전용 질문이 열린다");
        Ok(!qs.Contains(InterviewIntent.AskEquipmentTouch) || qs.Contains(InterviewIntent.AskBlackoutSigns),
            "설비 질문 한 벌을 그대로 붙이지 않는다");

        // 현장에 있던 양 — 직접 겪었다.
        string inRoom = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("sheep", blackout, InterviewIntent.AskBlackoutExperience));
        var fSheep = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("sheep", blackout, InterviewIntent.AskBlackoutExperience));
        GD.Print($"      양(현장): {inRoom}  [{fSheep.Variant}]");
        Ok(fSheep.Variant == "sawit", $"현장에 있던 직원은 직접 겪었다고 답한다 ({fSheep.Variant})");

        // 멀리 있던 여우 — 직접 경험으로 처리하면 안 된다.
        var fFox = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("fox", blackout, InterviewIntent.AskBlackoutExperience));
        GD.Print($"      여우(경비실): [{fFox.Variant}]");
        Ok(fFox.Variant != "sawit", $"멀리 있던 직원이 직접 봤다고 하지 않는다 ({fFox.Variant})");

        // 징후는 그 방에서 실제로 앞선 고장이 있었을 때만.
        var fSigns = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("fox", blackout, InterviewIntent.AskBlackoutSigns));
        Ok(fSigns.Variant == "nothing", $"근거 없이 '이상한 소리를 들었다' 고 하지 않는다 ({fSigns.Variant})");
    }

    // ── H. 기절 · 구조 — 당사자 / 발견자 / 구조자 ───────────────────────

    private void SectionH()
    {
        GD.Print("\n#### H. 기절 — 쓰러진 사람 · 그 자리에 있던 사람 · 옮긴 사람");
        Reset();
        Place(new() { ["sheep"] = Power, ["wolf"] = Power, ["cat"] = Storage });
        Log(LogEventType.TaskStart, "sheep", Power, 1f);
        Log(LogEventType.TaskStart, "wolf", Power, 1f);
        Log(LogEventType.TaskStart, "cat", Storage, 1f);

        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = 300f, EventType = LogEventType.Neglect,
            Detail = LogDetail.Fainted, ActorEmployeeId = "sheep", RoomId = Power,
            Description = "(테스트) 기절",
        });
        // 늑대가 실제로 의무실로 옮겼다 — 구조 기록.
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = 310f, EventType = LogEventType.Neglect,
            ActorEmployeeId = "wolf", RoomId = Power,
            Description = "늑대가 양를 의무실로 이송",
        });

        var faint = new InterviewEvidence
        {
            Id = "faint:sheep:300.00", Kind = EvidenceKind.Incident, Day = 1,
            SubjectEmployeeId = "sheep", SubjectRoomId = Power,
            AnchorTime = 300f, HasTime = true, Position = PositionClaim.AtRoom,
            Header = "기절 기록", Body = "양 — 스트레스 한계",
        };
        Ok(InterviewQuestionFactory.IsFaintCard(faint), "기절 자료로 인식된다");

        // ① 당사자 — 의식이 없던 동안의 일은 묻지 않는다.
        var qVictim = InterviewQuestionFactory.For("sheep", faint).Select(q => q.Intent).ToList();
        GD.Print($"      당사자(양): {string.Join(",", qVictim)}");
        Ok(qVictim.Contains(InterviewIntent.AskLastMemory) && qVictim.Contains(InterviewIntent.AskWokeWhere),
            "① 당사자에게는 '마지막 기억' 과 '깨어난 곳' 을 묻는다");
        Ok(!qVictim.Contains(InterviewIntent.AskWhoSeenNear) && !qVictim.Contains(InterviewIntent.AskFoundWhere),
            "   의식 없던 동안 누구를 봤는지는 묻지 않는다");

        // ② 같은 방에 있던 늑대 — 발견 + 실제 이송 기록이 있으므로 구조도 묻는다.
        var qWolf = InterviewQuestionFactory.For("wolf", faint).Select(q => q.Intent).ToList();
        GD.Print($"      늑대(현장+이송): {string.Join(",", qWolf)}");
        Ok(qWolf.Contains(InterviewIntent.AskFoundWhere), "② 그 자리에 있던 직원에게 발견 경위를 묻는다");
        Ok(qWolf.Contains(InterviewIntent.AskRescueAction), "   이송 기록이 있으므로 구조도 묻는다");

        // ③ 다른 방에 있던 고양이 — 발견자도 구조자도 아니다.
        var qCat = InterviewQuestionFactory.For("cat", faint).Select(q => q.Intent).ToList();
        GD.Print($"      고양이(다른 방): {string.Join(",", qCat)}");
        Ok(!qCat.Contains(InterviewIntent.AskFoundWhere) && !qCat.Contains(InterviewIntent.AskRescueAction),
            "③ 다른 방에 있던 직원에게 발견·구조를 묻지 않는다");

        // ④ 늑대의 발견 답변은 '본 만큼' 이다.
        string found = InterviewReplyPlanner.Answer(
            InterviewQuestionFactory.Make("wolf", faint, InterviewIntent.AskFoundCondition, "sheep"));
        GD.Print($"      늑대: {found}");
        Ok(found.Contains("의식"), "④ 발견 당시 상태를 말한다");
    }

    // ── I. 이상 개체 — 본 것과 들은 것 ──────────────────────────────────

    private void SectionI()
    {
        GD.Print("\n#### I. 이상 개체 — 직접 본 사람만 모습과 방향을 말한다");
        Reset();
        Place(new() { ["rabbit"] = Storage, ["dog"] = Core });
        Log(LogEventType.TaskStart, "rabbit", Storage, 1f);
        Log(LogEventType.TaskStart, "dog", Core, 1f);
        Log(LogEventType.AnomalyIncident, "", Storage, 300f);

        var anomaly = new InterviewEvidence
        {
            Id = "anomaly:storage", Kind = EvidenceKind.Anomaly, Day = 1,
            SubjectRoomId = Storage, AnchorTime = 300f, HasTime = true,
            IncidentKey = InterviewEvidenceBoard.IncidentKeyOf(LogEventType.AnomalyIncident, Storage, 300f),
            IncidentType = LogEventType.AnomalyIncident,
            Header = "시설 로그", Body = "이상 현상",
        };

        var qs = InterviewQuestionFactory.For("rabbit", anomaly).Select(q => q.Intent).ToList();
        Ok(qs.Contains(InterviewIntent.AskAnomalySeenHow),
            $"'직접 보았는가' 가 생김새 질문보다 먼저 열린다 ({string.Join(",", qs)})");

        var fRabbit = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("rabbit", anomaly, InterviewIntent.AskAnomalySeenHow));
        GD.Print($"      토끼(현장): [{fRabbit.Variant}]");
        Ok(fRabbit.Variant == "saw", $"그 방에 있던 직원은 봤다고 답한다 ({fRabbit.Variant})");

        var fDog = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("dog", anomaly, InterviewIntent.AskAnomalySeenHow));
        GD.Print($"      강아지(코어실): [{fDog.Variant}]");
        Ok(fDog.Variant != "saw", $"다른 방 직원이 봤다고 하지 않는다 ({fDog.Variant})");

        // 직접 보지 못한 사람은 방향도 말할 수 없다.
        var fDir = InterviewReplyPlanner.FrameFor(
            InterviewQuestionFactory.Make("dog", anomaly, InterviewIntent.AskAnomalyDirection));
        Ok(fDir.Variant == "unknown", $"못 본 사람은 이동 방향도 말하지 않는다 ({fDir.Variant})");
    }

    private static InterviewEvidence Incident(string room, float at)
        => new()
        {
            Id = $"incident:{room}:{at}", Kind = EvidenceKind.Incident, Day = 1,
            SubjectRoomId = room, AnchorTime = at, HasTime = true,
            IncidentKey = InterviewEvidenceBoard.IncidentKeyOf(LogEventType.TaskFailed, room, at),
            IncidentType = LogEventType.TaskFailed,
            Header = "시설 로그", Body = "설비 사고",
        };

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

    // 실제 이동 로그를 남기고 그 이동 자료를 돌려준다.
    private static InterviewEvidence Move(string who, string from, string to, float at, bool playerOrdered)
    {
        Log(LogEventType.RoomExit, who, from, at);
        Log(LogEventType.RoomEnter, who, to, at + 1f);
        return Movement(who, from, to, at, playerOrdered);
    }

    private static InterviewEvidence Movement(string who, string from, string to, float at, bool ordered)
        => new()
        {
            Id = $"move:{who}:{at}", Kind = EvidenceKind.Movement, Day = 1,
            SubjectEmployeeId = who, FromRoomId = from, ToRoomId = to, SubjectRoomId = to,
            Position = PositionClaim.Arrived,
            AnchorTime = at, HasTime = true, PlayerOrdered = ordered,
            Header = "시설 로그", Body = "이동",
        };

    private void Ok(bool cond, string what)
    {
        if (cond) { _pass++; GD.Print($"  [PASS] {what}"); }
        else { _fail++; GD.Print($"  [FAIL] {what}"); }
    }
}
