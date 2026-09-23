using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 휴게시간 조사 자료 자동 검증 (Test A~J).
//
//   godot --headless --path . scenes/debug/RestEvidenceTest.tscn --quit-after 900
//
// 보는 것은 하나다 — "근무 중 플레이어가 본 것만 자료가 되고, 그 자료로 실제 심문이
// 되는가". 문장이 예쁜지는 보지 않는다.
public partial class RestEvidenceTest : Node
{
    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";
    private const string Vent = "vent_room";
    private const string Medical = "medical_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        if (FacilitySimulation.Instance == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 휴게시간 조사 자료 검증 ################");
        TestA();
        TestB();
        TestC();
        TestD();
        TestE();
        TestF();
        TestG();
        TestH();
        TestI();
        TestJ();
        // 심문 개편(최초 진술 → 진술 꼬리질문 → 조사 노트 → 자료 비교) 검증.
        TestNewA();
        TestNewB();
        TestNewC();
        TestNewD();
        TestNewE();
        TestNewF();
        TestNewG();
        TestNewH();
        // 심문 리워크 1차(docs/NSP_INTERVIEW_REWORK.md §5) — 추궁 규칙 셋 · 카드 내용 · 최초 진술.
        TestNewI();
        TestNewJ();
        TestNewK();
        TestNewL();
        TestNewM();
        // 심문 리워크 2차(§5-14) — 띠 시간표.
        TestNewN();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── 신규 A : 인터뷰를 만들면 최초 진술 블록이 생긴다 ──────────────────
    private void TestNewA()
    {
        Head("신규 A", "인터뷰 생성 → 최초 진술 블록");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Storage, At(30));   // 고양이가 근무하던 방

        var session = new InterviewSession("cat");
        foreach (var o in session.Openings) GD.Print($"   [근무 진술 {o.Index + 1}] ({o.QuestionId}) {o.Text}  · 꼬리질문 {o.FollowUps.Count}");
        Check(session.Openings.Count >= 2 && session.Openings.Count <= 4, $"진술 블록 2~4개 ({session.Openings.Count}개)");
        Check(session.Openings.All(o => !string.IsNullOrWhiteSpace(o.Text)), "빈 진술 블록이 없다");
        Check(session.Openings.Select(o => o.Text).Distinct().Count() == session.Openings.Count,
            "같은 문장이 두 블록으로 나오지 않는다");
    }

    // ── 신규 B : Refresh 해도 최초 진술은 그대로 ───────────────────────────
    private void TestNewB()
    {
        Head("신규 B", "Refresh · 탭 전환 후에도 최초 진술 유지");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Storage, At(30));

        var session = new InterviewSession("cat");
        var texts = session.Openings.Select(o => o.Text).ToList();
        var lists = session.Openings.Select(o => o.FollowUps).ToList();
        session.Refresh();
        session.SetTab(NoteTab.ByIncident);
        session.Refresh();
        session.SetTab(NoteTab.CurrentEmployee);
        var incident = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        session.Ask(InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident));

        Check(session.Openings.Select(o => o.Text).SequenceEqual(texts), "진술 문장이 다시 생성되거나 바뀌지 않는다");
        Check(session.Openings.Select(o => o.FollowUps).Zip(lists, ReferenceEquals).All(x => x),
            "진술의 꼬리질문 목록도 그대로다");
    }

    // ── 신규 C : 진술을 고르면 그 진술의 꼬리질문만 ────────────────────────
    private void TestNewC()
    {
        Head("신규 C", "진술 선택 → 그 진술에서 나온 꼬리질문만");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Storage, At(30));

        var session = new InterviewSession("cat");
        var withFu = session.Openings.FirstOrDefault(o => o.FollowUps.Count > 0);
        Check(withFu != null, "꼬리질문이 달린 진술이 있다(직접 겪은 사고)");
        if (withFu == null) return;

        Check(session.OpeningFollowUps().Count == 0, "진술을 고르기 전에는 꼬리질문이 없다");
        session.SelectOpening(withFu.Index);
        var shown = session.OpeningFollowUps();
        GD.Print($"   진술 {withFu.Index + 1} 꼬리질문: {string.Join(" / ", shown.Select(q => q.Text))}");
        Check(ReferenceEquals(shown, withFu.FollowUps), "고른 진술의 FollowUps 가 그대로 뜬다");
        Check(shown.All(q => q.BaseQuestionId == withFu.QuestionId), "다른 진술의 질문이 섞이지 않는다");

        var other = session.Openings.FirstOrDefault(o => o != withFu);
        if (other != null)
        {
            session.SelectOpening(other.Index);
            Check(ReferenceEquals(session.OpeningFollowUps(), other.FollowUps), "다른 진술을 고르면 그 진술의 질문으로 바뀐다");
        }
        session.SelectOpening(withFu.Index);
        var turn = session.AskFollowUp(shown[0]);
        GD.Print($"   Q: {turn.QuestionText}\n   A: {turn.Answer}");
        Check(!string.IsNullOrWhiteSpace(turn.Answer), "진술 꼬리질문에 기존 로컬 답변이 돌아온다");
        Check(session.WasAskedFollowUp(shown[0]), "물어본 꼬리질문은 체크 표시된다");
    }

    // ── 신규 D : CCTV 반복 기록 5개 → 화면에선 한 장, 원본은 5장 ─────────────
    private void TestNewD()
    {
        Head("신규 D", "CCTV 반복 5건 → 범위 카드 1장 · 원본 5장 유지");
        Reset();
        Deploy();
        // 여우가 저장고에 계속 있고, 고양이가 드나든다(같은 인원 반복은 기록 단계에서 이미 걸러진다).
        for (int i = 0; i < 5; i++)
            PlayerKnownEvidence.RecordCctvObservation(Storage, At(191 + i * 6),
                i % 2 == 0 ? new[] { "fox" } : new[] { "fox", "cat" });

        var board = InterviewEvidenceBoard.Build("fox");
        var cctv = board.Where(e => e.Kind == EvidenceKind.Cctv).ToList();
        var cards = InterviewEvidenceDisplay.Compress(board);
        var range = cards.Where(c => c.Kind == EvidenceCardKind.CctvRange).ToList();
        if (range.Count > 0)
            GD.Print($"   카드: {InterviewEvidenceDisplay.Title(range[0])} / {InterviewEvidenceDisplay.Body(range[0])}");
        Check(cctv.Count == 5, $"원본 CCTV 자료 5장 ({cctv.Count}장)");
        Check(range.Count == 1 && range[0].Items.Count == 5, "표시 레이어에서는 CCTV 범위 카드 하나");
        Check(InterviewEvidenceBoard.Build("fox").Count(e => e.Kind == EvidenceKind.Cctv) == 5,
            "압축한 뒤에도 원본 InterviewEvidence 는 그대로 5장");
    }

    // ── 신규 E : 펼친 묶음의 원본 자료로 모순 추궁이 된다 ──────────────────
    private void TestNewE()
    {
        Head("신규 E", "묶음 카드 펼침 → 원본 자료 선택 → EvidenceContradiction");
        Reset();
        Deploy();
        for (int i = 0; i < 3; i++)
            PlayerKnownEvidence.RecordCctvObservation(Storage, At(191 + i * 6),
                i % 2 == 0 ? new[] { "fox" } : new[] { "fox", "cat" });
        PlayerKnownEvidence.RecordLocationStatement("fox", "t:191", Maintenance, true, At(191));

        var session = new InterviewSession("fox");
        var card = InterviewEvidenceDisplay.Compress(session.Board).First(c => c.Kind == EvidenceCardKind.CctvRange);
        var detail = card.Items[0];
        var claim = session.Board.First(e => e.Kind == EvidenceKind.OwnStatement && e.SubjectRoomId == Maintenance);
        session.Toggle(detail.Id);
        session.Toggle(claim.Id);
        var r = session.CheckContradiction();
        GD.Print($"   Q: {r.QuestionText}");
        Check(session.IsSelected(detail.Id), "펼친 상세의 원본 자료가 선택된다");
        Check(r.IsContradiction, "원본 자료가 그대로 모순 판정에 전달된다");
    }

    // ── 신규 F : 사고 · 증언 · 본인 진술은 자동 병합되지 않는다 ──────────────
    private void TestNewF()
    {
        Head("신규 F", "Incident / Testimony / OwnStatement 는 묶지 않는다");
        var list = new List<InterviewEvidence>();
        for (int i = 0; i < 3; i++)
        {
            float t = At(60 + i);
            list.Add(new InterviewEvidence { Id = $"i{i}", Kind = EvidenceKind.Incident, HasTime = true, AnchorTime = t, SubjectRoomId = Power });
            list.Add(new InterviewEvidence { Id = $"s{i}", Kind = EvidenceKind.Testimony, HasTime = true, AnchorTime = t,
                SubjectEmployeeId = "fox", SpeakerEmployeeId = "cat", SubjectRoomId = Power, Position = PositionClaim.AtRoom });
            list.Add(new InterviewEvidence { Id = $"c{i}", Kind = EvidenceKind.OwnStatement, HasTime = true, AnchorTime = t,
                SubjectEmployeeId = "fox", SpeakerEmployeeId = "fox", SubjectRoomId = Power, Position = PositionClaim.AtRoom });
        }
        var cards = InterviewEvidenceDisplay.Compress(list);
        Check(cards.Count == list.Count && cards.All(c => c.Kind == EvidenceCardKind.Single),
            $"9장 모두 개별 카드 ({cards.Count}장)");

        var groups = InterviewEvidenceDisplay.ByIncident(list);
        Check(groups.Count(g => g.Incident != null) == 3, "사고마다 사건 묶음이 하나씩 생긴다");
        Check(groups.Sum(g => g.Cards.Count) == list.Count, "각 자료는 한 묶음에만 들어간다");
    }

    // ── 신규 G : 플레이어에게 보이는 시각은 한글 시간대 표기 ─────────────────
    private void TestNewG()
    {
        Head("신규 G", "시각 표기 — 밤 · 새벽");
        var cases = new (int h, int m, string want)[]
        {
            (21, 20, "밤 9시 20분"), (22, 12, "밤 10시 12분"), (23, 15, "밤 11시 15분"),
            (0, 0, "새벽 12시"), (0, 45, "새벽 12시 45분"), (1, 10, "새벽 1시 10분"),
            (2, 30, "새벽 2시 30분"), (3, 13, "새벽 3시 13분"),
        };
        foreach (var (h, m, want) in cases)
            Check(DialogueClock.Format(h, m) == want, $"{h:00}:{m:00} → {DialogueClock.Format(h, m)}");

        // 근무 시계(22:00 = 0초)에서도 같은 표기가 나온다.
        Check(DialogueClock.Text(At(12) + 0.01f) == "밤 10시 12분", "근무 12분 → 밤 10시 12분");
        Check(DialogueClock.Text(At(165) + 0.01f) == "새벽 12시 45분", "근무 165분 → 새벽 12시 45분");
        Check(DialogueClock.Range(At(162) + 0.01f, At(168) + 0.01f) == "새벽 12시 42분 ~ 새벽 12시 48분", "시간 범위도 같은 규칙");

        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(165) + 0.01f, new[] { "cat" });
        var cctv = InterviewEvidenceBoard.Build("cat").First(e => e.Kind == EvidenceKind.Cctv);
        var q = InterviewQuestionFactory.Make("cat", cctv, InterviewIntent.AskPresenceReason);
        GD.Print($"   자료: {cctv.OneLine}\n   Q: {q.Text}");
        var digits = new System.Text.RegularExpressions.Regex(@"\d{1,2}:\d{2}");
        Check(cctv.TimeText == "새벽 12시 45분" && !digits.IsMatch(cctv.OneLine), "조사 자료 카드가 한글 시간대로 뜬다");
        Check(q.Text.Contains("새벽 12시 45분경") && !digits.IsMatch(q.Text), "질문 문장도 같은 표기를 쓴다");
    }

    // ── 신규 H : 표기가 바뀌어도 판정은 숫자 그대로 ─────────────────────────
    private void TestNewH()
    {
        Head("신규 H", "표기 변경 후에도 AnchorTime · 모순 판정은 그대로");
        Reset();
        Deploy();
        // 자정을 사이에 둔 2분 차이(밤 11시 59분 / 새벽 12시 1분) — 같은 순간으로 본다.
        PlayerKnownEvidence.RecordLocationStatement("cat", "t:119", Storage, true, At(119));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(121), new[] { "cat" });
        var s1 = new InterviewSession("cat");
        var claim = s1.Board.First(e => e.Kind == EvidenceKind.OwnStatement);
        var cam = s1.Board.First(e => e.Kind == EvidenceKind.Cctv);
        Check(Mathf.IsEqualApprox(claim.AnchorTime, At(119)) && Mathf.IsEqualApprox(cam.AnchorTime, At(121)),
            "AnchorTime 은 초 단위 숫자 그대로");
        s1.Toggle(claim.Id);
        s1.Toggle(cam.Id);
        var near = s1.CheckContradiction();
        GD.Print($"   {claim.TimeText} vs {cam.TimeText} → {(near.IsContradiction ? "모순" : near.Notice)}");
        Check(near.IsContradiction && Mathf.IsEqualApprox(near.AnchorTime, At(121)), "자정을 넘는 가까운 두 시각 → 모순");

        Reset();
        Deploy();
        PlayerKnownEvidence.RecordLocationStatement("cat", "t:100", Storage, true, At(100));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(140), new[] { "cat" });
        var s2 = new InterviewSession("cat");
        s2.Toggle(s2.Board.First(e => e.Kind == EvidenceKind.OwnStatement).Id);
        s2.Toggle(s2.Board.First(e => e.Kind == EvidenceKind.Cctv).Id);
        Check(!s2.CheckContradiction().IsContradiction, $"{EvidenceContradiction.WindowMinutes}분보다 먼 두 시각 → 모순 아님");
    }

    // ── A : 근무 중 화면에 뜬 이동 기록이 조사 자료가 된다 ────────────────
    private void TestA()
    {
        Head("A", "화면에 뜬 이동 기록 → 조사 자료");
        Reset();
        Deploy();
        Move("cat", Storage, Maintenance, At(13));

        var shown = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1)
            .Any(r => r.RelatedEmployeeId == "cat" && r.ToRoomId == Maintenance);
        var board = InterviewEvidenceBoard.Build("cat");
        var move = board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement);

        Check(shown, "이 이동이 시설 로그 화면에 실제로 떴다");
        Check(move != null, "같은 이동이 조사 자료로 들어온다");
        if (move == null) return;
        GD.Print($"   자료: {move.OneLine}");
        Check(move.FromRoomId == Storage && move.ToRoomId == Maintenance, "출발·도착 작업실이 그대로 실린다");
        Check(move.HasTime && Mathf.IsEqualApprox(move.AnchorTime, At(13)), "시각이 그대로 실린다");
        Check(move.PlayerObserved && move.Day == 1, "플레이어가 본 오늘 자료로 기록된다");
    }

    // ── B : 플레이어가 보지 못한 내부 기록은 자료가 되지 않는다 ────────────
    private void TestB()
    {
        Head("B", "통과 기록 · 목격자 없는 방해공작 → 자료 아님");
        Reset();
        Deploy();

        // ① 지나가기만 한 방(화면 로그에 뜨지 않는다)
        Log(LogEventType.RoomExit, "wolf", Guard, At(9), passing: true);
        Log(LogEventType.RoomEnter, "wolf", Core, At(10), passing: true);
        // ② 아무도 보지 못한 결번자의 행동 — EventLog 에는 남지만 화면에는 없다
        GameState.Instance.SetSaboteur("fox");
        Log(LogEventType.RoomEnter, "fox", Power, At(20), passing: true);

        var wolf = InterviewEvidenceBoard.Build("wolf");
        var fox = InterviewEvidenceBoard.Build("fox");
        GD.Print($"   늑대 자료 {wolf.Count}장 · 여우 자료 {fox.Count}장 (기분 제외 " +
                 $"{wolf.Count(e => e.Kind != EvidenceKind.Mood)}/{fox.Count(e => e.Kind != EvidenceKind.Mood)})");

        Check(!wolf.Any(e => e.Kind == EvidenceKind.Movement), "통과만 한 이동은 자료가 되지 않는다");
        Check(!fox.Any(e => e.Kind == EvidenceKind.Movement), "결번자의 숨은 이동도 자료가 되지 않는다");
        // 내부 진실은 여전히 알고 있지만, 그것이 자료로 새어 나오지 않아야 한다.
        Check(DialogueContextBuilder.RoomAt("fox", 1, At(25)) == Power,
            "내부 기록(SystemTruth)에는 그대로 남아 있다");
        Check(fox.All(e => e.PlayerObserved), "조사 자료에 들어온 것은 전부 플레이어가 본 것이다");
    }

    // ── C : CCTV 로 본 장면이 자료가 된다 ────────────────────────────────
    private void TestC()
    {
        Head("C", "CCTV 관찰 → CCTV 자료");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat", "rabbit" });

        var board = InterviewEvidenceBoard.Build("cat");
        var cctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        Check(cctv != null, "CCTV 자료가 생긴다");
        if (cctv == null) return;
        GD.Print($"   자료: {cctv.OneLine}  (같이 잡힌 인원 {cctv.RelatedEmployeeIds.Count}명)");
        Check(cctv.SubjectRoomId == Maintenance && cctv.Position == PositionClaim.AtRoom,
            "그 시각 그 방에 있었다는 위치 자료다");
        Check(cctv.RelatedEmployeeIds.Contains("rabbit"), "같이 찍힌 직원이 함께 기록된다");
        Check(!InterviewEvidenceBoard.Build("dog").Any(e => e.Kind == EvidenceKind.Cctv),
            "찍히지 않은 직원의 자료에는 뜨지 않는다");
    }

    // ── D : 인터뷰에서 얻은 위치 진술이 자료가 된다 ───────────────────────
    private void TestD()
    {
        Head("D", "인터뷰 답변 → 이 직원의 진술 자료");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(30));

        var session = new InterviewSession("cat");
        int before = session.Board.Count(e => e.Kind == EvidenceKind.OwnStatement);

        var incident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        Check(incident != null, "사고 기록이 자료로 뜬다");
        if (incident == null) return;

        var q = InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident);
        var turn = session.Ask(q);
        GD.Print($"   Q: {turn.QuestionText}\n   A: {turn.Answer}");

        var claims = session.Board.Where(e => e.Kind == EvidenceKind.OwnStatement).ToList();
        Check(claims.Count == before + 1, "답변이 진술 자료 한 장으로 남는다");
        if (claims.Count == 0) return;
        GD.Print($"   자료: {claims[0].OneLine}");
        Check(claims[0].HasTime && claims[0].CanAnchorPosition, "그 진술에 시각과 위치가 함께 붙는다");
    }

    // ── E : 한 직원의 증언을 다른 직원 심문에 쓴다 ────────────────────────
    private void TestE()
    {
        Head("E", "늑대의 목격 증언 → 고양이 심문에서 사용");
        Reset();
        Deploy();
        GameState.Instance.SetSaboteur("cat");
        // 늑대가 실제로 같은 방에서 고양이의 행동을 봤다.
        Log(LogEventType.Sabotage, "cat", Maintenance, At(35), witnesses: new[] { "wolf" });

        var wolfSession = new InterviewSession("wolf");
        var q = wolfSession.BasicQuestions().First(x => x.Intent == InterviewIntent.BasicSuspicious);
        var turn = wolfSession.Ask(q);
        GD.Print($"   Q(늑대): {turn.QuestionText}\n   A: {turn.Answer}");

        var catBoard = InterviewEvidenceBoard.Build("cat");
        var said = catBoard.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        Check(said != null, "늑대의 증언이 고양이의 조사 자료로 넘어간다");
        if (said == null) return;
        GD.Print($"   자료: [{said.Header}] {said.OneLine}");
        Check(said.SpeakerEmployeeId == "wolf" && said.SubjectEmployeeId == "cat",
            "누가 누구에 대해 말했는지가 남는다");

        var follow = InterviewQuestionFactory.For("cat", said);
        Check(follow.Any(x => x.Intent == InterviewIntent.AskConfirmTestimony),
            "그 증언으로 고양이에게 확인 질문을 만들 수 있다");
        if (follow.Count > 0) GD.Print($"   Q(고양이): {follow[0].Text}");
    }

    // ── F : 같은 시각 다른 방 → 모순 추궁 가능 ────────────────────────────
    private void TestF()
    {
        Head("F", "22:16 저장고 진술 vs 22:16 정비실 CCTV");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordLocationStatement("cat", "t:16", Storage, true, At(16));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat" });

        var session = new InterviewSession("cat");
        var claim = session.Board.First(e => e.Kind == EvidenceKind.OwnStatement);
        var cctv = session.Board.First(e => e.Kind == EvidenceKind.Cctv);
        session.Toggle(claim.Id);
        session.Toggle(cctv.Id);

        Check(session.CanTryConfront, "자료 두 장이 선택된다");
        var r = session.CheckContradiction();
        Check(r.IsContradiction, "모순으로 판정된다");
        if (!r.IsContradiction) { GD.Print($"   안내: {r.Notice}"); return; }
        GD.Print($"   Q: {r.QuestionText}");
        GD.Print($"   A: {session.Confront(r).Answer}");
        Check(r.QuestionText.Contains("저장고") && r.QuestionText.Contains("정비실"),
            "추궁 문장이 두 자료를 모두 인용한다");
    }

    // ── G : 시간이 벌어진 두 기록 → 모순 아님 ─────────────────────────────
    private void TestG()
    {
        Head("G", "21:50 저장고 / 22:30 정비실");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Storage, At(5), new[] { "cat" });
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(45), new[] { "cat" });

        var session = new InterviewSession("cat");
        var a = session.Board.First(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Storage);
        var b = session.Board.First(e => e.Kind == EvidenceKind.Cctv && e.SubjectRoomId == Maintenance);
        session.Toggle(a.Id);
        session.Toggle(b.Id);
        var r = session.CheckContradiction();
        GD.Print($"   안내: {r.Notice}");
        Check(!r.IsContradiction, "모순으로 판정되지 않는다");
        Check(!string.IsNullOrEmpty(r.Notice), "왜 안 되는지 화면에 설명이 나간다");
    }

    // ── H : 결번자의 거짓 알리바이는 반복 질문에도 유지된다 ────────────────
    private void TestH()
    {
        Head("H", "결번자의 진술 자료 ↔ DialogueClaimState");
        Reset();
        Deploy();
        GameState.Instance.SetSaboteur("cat");
        // 고양이는 실제로 정비실로 갔고, 거기서 사고가 났다.
        Move("cat", Storage, Maintenance, At(20));
        Incident(LogEventType.TaskFailed, Maintenance, At(30));

        var session = new InterviewSession("cat");
        var incident = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        var q = InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident);

        string first = session.Ask(q).Answer;
        var claimEv = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        string claimedRoom = claimEv?.SubjectRoomId ?? "";

        // 같은 질문을 다시. 새 알리바이를 지어내면 안 된다.
        string second = session.Ask(q).Answer;
        var claimAfter = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement);
        var state = DialogueClaimState.Get("cat", 1, incident.IncidentKey);

        GD.Print($"   1차: {first}\n   2차: {second}");
        GD.Print($"   진술 자료 = {InterviewEvidenceBoard.RoomName(claimedRoom)} / " +
                 $"ClaimState = {InterviewEvidenceBoard.RoomName(state.ClaimedRoomId)} " +
                 $"(진실 {state.ClaimTruthful})");
        Check(claimEv != null, "결번자의 진술도 자료로 남는다");
        Check(claimAfter?.SubjectRoomId == claimedRoom, "다시 물어도 진술 자료가 바뀌지 않는다");
        Check(claimedRoom == state.ClaimedRoomId, "진술 자료가 DialogueClaimState 와 같은 방을 가리킨다");
        Check(session.Board.Count(e => e.Kind == EvidenceKind.OwnStatement) == 1,
            "같은 사건에 대한 진술 자료가 여러 장으로 불어나지 않는다");
    }

    // ── I : 아무 대사나 자료가 되지는 않는다 ──────────────────────────────
    private void TestI()
    {
        Head("I", "기분·소감·되묻기는 자료로 저장하지 않는다");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Power, At(30));

        var session = new InterviewSession("dog");
        int before = session.Board.Count;

        // 기분 질문 3종 + 기본 질문 3종 + 정확한 시각 되묻기.
        var mood = session.Board.First(e => e.Kind == EvidenceKind.Mood);
        foreach (var intent in new[] { InterviewIntent.AskMoodReason, InterviewIntent.AskMoodBefore,
                     InterviewIntent.AskMoodRelated })
            session.Ask(InterviewQuestionFactory.Make("dog", mood, intent));
        foreach (var q in session.BasicQuestions()) session.Ask(q);
        var incident = session.Board.First(e => e.Kind == EvidenceKind.Incident);
        session.Ask(InterviewQuestionFactory.Make("dog", incident, InterviewIntent.FollowExactTime));

        int after = session.Board.Count;
        GD.Print($"   질문 7개 → 자료 {before}장 → {after}장");
        Check(after == before, "추리에 쓸 수 없는 답변은 자료를 늘리지 않는다");

        // 반대로 위치를 묻는 질문의 답은 자료로 남는다.
        //
        // 장수로 세지 않는다 — 심문 리워크(§3-4) 이후로는 최초 진술("이상한 점")이 이미
        // 같은 사건의 위치 진술을 남기므로, 같은 사건을 다시 물어도 그 카드가 갱신될 뿐
        // 새로 불어나지 않는다(한 사건당 진술 한 장은 예전부터의 규칙이다 — Test H).
        session.Ask(InterviewQuestionFactory.Make("dog", incident, InterviewIntent.AskWhereAtIncident));
        var claimCard = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement
                                                          && e.SubjectEmployeeId == "dog");
        GD.Print($"   위치 질문 1개 → 자료 {session.Board.Count}장 · 진술 카드 {claimCard?.OneLine ?? "없음"}");
        Check(claimCard != null, "위치 진술은 자료로 남는다");
        Check(claimCard != null && claimCard.CanAnchorPosition, "그 진술 카드는 시각·위치를 함께 가진다");
        Check(session.Board.Count >= before, "자료가 줄어들지 않는다");
    }

    // ── J : 심문 대상을 바꿔도 오늘의 자료는 남는다 ───────────────────────
    private void TestJ()
    {
        Head("J", "직원을 바꿔도 조사 자료 유지 · DAY 가 바뀌면 분리");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(16), new[] { "cat" });
        Incident(LogEventType.TaskFailed, Power, At(30));

        var catSession = new InterviewSession("cat");
        var incident = catSession.Board.First(e => e.Kind == EvidenceKind.Incident);
        catSession.Ask(InterviewQuestionFactory.Make("cat", incident, InterviewIntent.AskWhereAtIncident));
        int catEvidence = catSession.Board.Count;

        // 다른 직원을 심문하고 다시 돌아온다.
        var dogSession = new InterviewSession("dog");
        dogSession.Ask(InterviewQuestionFactory.Make("dog", incident, InterviewIntent.AskWhereAtIncident));
        var back = new InterviewSession("cat");

        GD.Print($"   고양이 {catEvidence}장 → 강아지 심문 후 {back.Board.Count}장");
        Check(back.Board.Count >= catEvidence, "먼저 확보한 자료가 사라지지 않는다");
        Check(back.Board.Any(e => e.Kind == EvidenceKind.OwnStatement), "고양이의 진술이 그대로 남아 있다");

        // 전원 보기에서는 강아지의 진술까지 함께 읽을 수 있다.
        back.SetScope(true);
        Check(back.Board.Any(e => e.SubjectEmployeeId == "dog"), "전원 보기에서 다른 직원 자료도 읽힌다");
        Check(!back.CanUse(back.Board.First(e => e.SubjectEmployeeId == "dog")),
            "다른 직원의 자료로는 이 사람에게 질문할 수 없다");
        back.SetScope(false);

        // DAY 가 넘어가면 오늘 자료는 섞이지 않는다.
        GameState.Instance.GoToNextDay();
        var day2 = InterviewEvidenceBoard.Build("cat");
        GD.Print($"   DAY{GameState.Instance.CurrentDay} 조사 자료 {day2.Count}장 " +
                 $"(진술 {day2.Count(e => e.Kind == EvidenceKind.OwnStatement)}장)");
        Check(!day2.Any(e => e.Kind == EvidenceKind.OwnStatement),
            "지난 DAY 의 진술이 다음 DAY 자료에 섞이지 않는다");
        Check(!day2.Any(e => e.Kind == EvidenceKind.Cctv),
            "지난 DAY 의 CCTV 기록도 섞이지 않는다");
    }


    // ── 신규 I : 사고 기록 + 그 방에 있었다는 자료 → 재석 추궁(Presence) ───
    //
    // 예전에는 사고 기록이 "이 직원의 위치를 확인할 수 있는 자료가 아닙니다" 로 막혀
    // 플레이어가 가장 먼저 집는 카드로 아무것도 못 물었다(NSP_INTERVIEW_REWORK §1-1).
    private void TestNewI()
    {
        Head("신규 I", "사고 기록 + CCTV → 재석 추궁 성립 / 다른 방이면 불성립");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Maintenance, At(40));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(38), new[] { "cat" });

        var s = new InterviewSession("cat");
        var inc = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        var cam = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        if (!Check(inc != null && cam != null, "사고 기록과 CCTV 가 모두 자료로 뜬다")) return;

        var r = EvidenceContradiction.Check("cat", inc, cam);
        GD.Print($"   자료1: {inc.OneLine}\n   자료2: {cam.OneLine}\n   Kind: {r.Kind}\n   Q: {r.QuestionText}");
        Check(r.Kind == ConfrontKind.Presence, "재석 추궁(Presence)으로 성립한다");
        Check(r.QuestionText.Contains("정비실"), "질문문에 사고가 난 작업실이 들어간다");
        Check(r.QuestionText.Contains(DialogueClock.Spoken(At(40))), "질문문에 사고 시각이 들어간다");

        // 고른 순서가 반대여도 같은 판정이 나와야 한다.
        Check(EvidenceContradiction.Check("cat", cam, inc).Kind == ConfrontKind.Presence,
            "자료를 고른 순서와 무관하게 성립한다");

        // ── 다른 방이면 성립하지 않는다. 그래도 물을 수는 있다(거절하지 않는다).
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Maintenance, At(40));
        PlayerKnownEvidence.RecordCctvObservation(Storage, At(38), new[] { "cat" });

        var s2 = new InterviewSession("cat");
        var inc2 = s2.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        var cam2 = s2.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
        if (!Check(inc2 != null && cam2 != null, "두 자료가 모두 뜬다")) return;

        var r2 = EvidenceContradiction.Check("cat", inc2, cam2);
        Check(r2.Kind == ConfrontKind.None, "다른 방이면 성립하지 않는다");

        s2.Toggle(inc2.Id);
        s2.Toggle(cam2.Id);
        var turn = s2.Confront(r2);
        GD.Print($"   Q: {turn.QuestionText}\n   A: {turn.Answer}");
        Check(!string.IsNullOrWhiteSpace(turn.QuestionText), "성립하지 않아도 질문 문장이 나온다");
        Check(!string.IsNullOrWhiteSpace(turn.Answer) && turn.Answer != "…",
            "성립하지 않아도 빈 턴이 아니라 중립 답변이 돌아온다");
    }

    // ── 신규 J : 행동이 실린 증언 + 사고 기록 → 행동 추궁(Behavior) ────────
    private void TestNewJ()
    {
        Head("신규 J", "설비 쪽 목격 증언 + 사고 기록 → 행동 추궁");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordSighting("wolf", "cat", Maintenance, At(30), Odd);
        Incident(LogEventType.TaskFailed, Maintenance, At(40));

        var s = new InterviewSession("cat");
        var say = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Testimony);
        var inc = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (!Check(say != null && inc != null, "증언과 사고 기록이 모두 자료로 뜬다")) return;

        GD.Print($"   증언 카드: {say.OneLine}");
        // §5-4 — 증언 카드에 "무엇을 봤는지"가 남아야 한다(예전에는 방 이름만 남았다).
        Check(say.Body == $"{"늑대"} · {Odd}", $"증언 카드 본문이 '늑대 · {Odd}' 다");
        Check(say.BehaviorDetail == Odd, "그 내용이 BehaviorDetail 로도 실린다");

        var r = EvidenceContradiction.Check("cat", say, inc);
        GD.Print($"   Kind: {r.Kind}\n   Q: {r.QuestionText}");
        Check(r.Kind == ConfrontKind.Behavior, "행동 추궁(Behavior)으로 성립한다");
        Check(r.QuestionText.Contains(Odd), "질문문에 목격된 행동이 그대로 들어간다");
    }

    // ── 신규 K : 최초 진술이 그 자리에서 자료가 된다 ──────────────────────
    //
    // 화면에 크게 뜨는 "이상한 점" 답변이 카드가 되지 않아 쓸 수 없었다(§1-4).
    private void TestNewK()
    {
        Head("신규 K", "최초 진술(사고 목격) → 시각이 붙은 진술 자료");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Storage, At(30));   // 고양이가 근무하던 방

        var turn = LocalDialogueGenerator.Interview("cat", DialogueQuestions.Anomaly);
        GD.Print($"   A: {turn.Answer}");
        var said = PlayerKnownEvidence.StatementsBy("cat");
        foreach (var st in said)
            GD.Print($"   진술: {RoomNameOf(st.RoomId)} · {(st.HasTime ? DialogueClock.Text(st.AnchorTime) : "시각 없음")}");
        Check(said.Count >= 1, "진술이 플레이어가 아는 자료로 남는다");
        Check(said.Any(x => x.HasTime), "그 진술에 시각이 붙는다");

        var board = InterviewEvidenceBoard.Build("cat");
        var card = board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement && e.CanAnchorPosition);
        Check(card != null, "그 진술이 위치를 말하는 카드로 조사 자료에 올라온다");
    }

    // ── 신규 L : 예전 위치 모순 규칙은 그대로 살아 있다 ────────────────────
    private void TestNewL()
    {
        Head("신규 L", "같은 시각 다른 방 → 위치 추궁(Location) 유지");
        Reset();
        Deploy();
        PlayerKnownEvidence.RecordLocationStatement("cat", "t:119", Storage, true, At(119));
        PlayerKnownEvidence.RecordCctvObservation(Maintenance, At(121), new[] { "cat" });

        var s = new InterviewSession("cat");
        var claim = s.Board.First(e => e.Kind == EvidenceKind.OwnStatement);
        var cam = s.Board.First(e => e.Kind == EvidenceKind.Cctv);
        var r = EvidenceContradiction.Check("cat", claim, cam);
        GD.Print($"   Kind: {r.Kind}\n   Q: {r.QuestionText}");
        Check(r.IsContradiction, "모순으로 판정된다(기존 동작)");
        Check(r.Kind == ConfrontKind.Location, "위치 추궁(Location)으로 분류된다");
    }

    // ── 신규 M : DAY0 교육이 듣는 Asked 이벤트가 그대로 돈다 ───────────────
    private void TestNewM()
    {
        Head("신규 M", "InterviewSession.Asked — 교육 진행 조건");
        Reset();
        Deploy();
        Incident(LogEventType.TaskFailed, Storage, At(30));

        int fired = 0;
        string who = "";
        void OnAsked(string id, InterviewQuestion q) { fired++; who = id; }
        InterviewSession.Asked += OnAsked;
        try
        {
            var s = new InterviewSession("rabbit");
            var basic = s.BasicQuestions().FirstOrDefault();
            if (Check(basic != null, "기본 질문이 있다")) s.Ask(basic);

            var follow = s.Openings.SelectMany(o => o.FollowUps).FirstOrDefault();
            if (follow != null) s.AskFollowUp(follow);
        }
        finally { InterviewSession.Asked -= OnAsked; }

        GD.Print($"   Asked {fired}회 · 대상 {who}");
        Check(fired >= 1, "질문할 때마다 Asked 가 발신된다");
        Check(who == "rabbit", "발신에 심문 대상 직원이 실린다");
    }

    // ── 신규 N : 띠 시간표 구간 계산 (§5-14) ──────────────────────────────
    //
    // 여우가 22:00 정비실에 배치되고 23:10 저장고로 옮기면, 여우의 띠는
    // [22:00, 23:10) 정비실 · [23:10, 04:00) 저장고 두 구간이어야 한다.
    // 23:47 방해공작은 그 시각에 세로선이 되고, 경고 줄은 띠에 나타나지 않는다.
    //
    // 부르는 것은 StaffTimelineView 의 static 함수뿐이다 — 화면 없이 확인하기 위해서다.
    // 넘기는 자료도 FacilityLogFormatter.Build() 결과뿐이다. EventLog 원본을 넘기면
    // 플레이어가 보지 못한 이동이 띠에 서게 되고, 그 순간 이 검사는 의미가 없어진다.
    private void TestNewN()
    {
        Head("신규 N", "띠 시간표 — 구간 계산과 사고 세로선 (§5-14)");
        Reset();

        var rooms = new Dictionary<string, string>
        {
            ["fox"] = Maintenance, ["cat"] = Storage, ["dog"] = Guard,
            ["wolf"] = Core, ["rabbit"] = Power, ["sheep"] = Medical,
        };
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
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);

        Move("fox", Maintenance, Storage, At(70));             // 23:10 이동
        Incident(LogEventType.CctvDisconnect, Core, At(90));   // 23:30 경고 — 띠에 뜨면 안 된다
        Log(LogEventType.Sabotage, "fox", Maintenance, At(107)); // 23:47 방해공작

        var rows = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1);
        float len = Mathf.Max(1f, Config.Instance?.Data?.DayLengthSeconds ?? 120f);
        var bands = StaffTimelineView.BuildSegments(rooms.Keys, rows, len);

        // --- 여우: 두 구간 ---
        var fox = bands.GetValueOrDefault("fox");
        if (!Check(fox != null && fox.Count == 2, $"여우 띠가 두 구간이다 (실제 {fox?.Count ?? 0})"))
        {
            foreach (var g in fox ?? new List<StaffTimelineView.Segment>())
                GD.Print($"   구간: {RoomNameOf(g.RoomId)} {g.Start:0.0}~{g.End:0.0}");
            return;
        }
        GD.Print($"   구간1: {RoomNameOf(fox[0].RoomId)} {fox[0].Start:0.0}~{fox[0].End:0.0}");
        GD.Print($"   구간2: {RoomNameOf(fox[1].RoomId)} {fox[1].Start:0.0}~{fox[1].End:0.0}");
        Check(fox[0].RoomId == Maintenance, "첫 구간은 배치받은 정비실이다");
        Check(Mathf.IsZeroApprox(fox[0].Start), "첫 구간은 근무 시작(22:00)에서 열린다");
        Check(Mathf.Abs(fox[0].End - At(70)) < 0.5f, "첫 구간은 이동 시각(23:10)에 닫힌다");
        Check(fox[1].RoomId == Storage, "둘째 구간은 옮겨 간 저장고다");
        Check(Mathf.Abs(fox[1].Start - At(70)) < 0.5f, "둘째 구간은 이동 시각에 열린다");
        Check(Mathf.Abs(fox[1].End - len) < 0.5f, "둘째 구간은 근무 끝(04:00)까지 이어진다");

        // --- 움직이지 않은 직원: 한 구간이 하루를 덮는다 ---
        var cat = bands.GetValueOrDefault("cat");
        Check(cat != null && cat.Count == 1 && cat[0].RoomId == Storage
              && Mathf.IsZeroApprox(cat[0].Start) && Mathf.Abs(cat[0].End - len) < 0.5f,
            "이동이 없던 직원은 배치받은 방 한 구간뿐이다");

        // --- 사고 세로선 ---
        var lines = StaffTimelineView.IncidentRowIndices(rows);
        foreach (int i in lines)
            GD.Print($"   세로선: {rows[i].Severity} {rows[i].Timestamp:0.0} {rows[i].Text}");
        if (Check(lines.Count == 1, $"세로선은 사고 하나뿐이다 (실제 {lines.Count})"))
        {
            var line = rows[lines[0]];
            Check(line.Severity == DisplayLogSeverity.Sabotage, "그 줄은 방해공작이다");
            Check(Mathf.Abs(line.Timestamp - At(107)) < 0.5f, "23:47 그 시각에 선다");
        }
        Check(rows.Any(r => r.Severity == DisplayLogSeverity.Warning),
            "경고 줄은 로그 목록에는 남아 있다");
        Check(!lines.Any(i => rows[i].Severity == DisplayLogSeverity.Warning),
            "경고 줄은 띠에 나타나지 않는다");

        // --- 띠는 화면에 뜬 줄만 본다 ---
        // 구간을 만드는 근거는 "도착한 방이 적힌 줄"이다. 여우에게 그런 줄은
        // 최초 배치 한 줄과 23:10 이동 한 줄, 화면에 실제로 뜬 둘뿐이어야 한다.
        Check(rows.Count(r => r.RelatedEmployeeId == "fox" && !string.IsNullOrEmpty(r.ToRoomId)) == 2,
            "여우의 방 이동 줄은 시설 로그 화면에 뜬 두 줄뿐이다");
    }

    // 목격 증언에 실리는 행동 — SaboteurPlan.TickPrecursors 가 남기는 문구 그대로.
    private const string Odd = "설비 쪽에 평소보다 오래 머물렀다";

    private static string RoomNameOf(string roomId) => InterviewEvidenceBoard.RoomName(roomId);

    // --- 도우미 -----------------------------------------------------------

    private static float At(int gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }

    private static void Reset()
    {
        GameState.Instance.ResetRun(1);
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        FacilitySimulation.Instance.RollDailyMoods();
    }

    // 배치 + 근무 시작. TaskStart 가 있어야 이후 이동이 시설 로그 화면에 뜬다.
    private static void Deploy()
    {
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = Storage, ["dog"] = Guard, ["wolf"] = Maintenance,
            ["rabbit"] = Maintenance, ["sheep"] = Power, ["fox"] = Core,
        };
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
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
    }

    private static void Move(string employeeId, string from, string to, float arriveAt)
    {
        Log(LogEventType.RoomExit, employeeId, from, Mathf.Max(0f, arriveAt - 1f));
        Log(LogEventType.RoomEnter, employeeId, to, arriveAt);
    }

    private static void Incident(LogEventType type, string roomId, float at) => Log(type, "", roomId, at);

    private static void Log(LogEventType type, string actor, string roomId, float at,
        IEnumerable<string> witnesses = null, bool passing = false)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = roomId,
            PassingThrough = passing,
            Description = $"(테스트 {type} {roomId} {at:0.0})",
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }
}
