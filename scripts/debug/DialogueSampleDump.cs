using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// Local Dialogue V2 — 사람이 읽는 대사 샘플.
//
//   godot --headless --path . res://scenes/debug/DialogueSampleDump.tscn
//
// 통계 검사로는 "사람이 이렇게 말하는가"를 알 수 없다. 그래서 근무 하루를 로그로 재현하고
// 6명에게 모든 종류의 질문을 던진 뒤, 답변을 캐릭터별로 tools/dialogue_samples/<id>.md 에 적는다.
//
// 장면 4개(혼자 배치 · 둘이 배치 · 재배치 · 미배치 포함)를 각각 SamplesPerScene 번 처음부터 다시 만들고,
// 매번 여섯 명 모두에게 같은 질문 묶음을 실제 순서대로(근무 중 전화 → 휴게시간 심문) 던진다.
// 그래서 한 장면 · 한 캐릭터 · 한 질문 종류마다 서로 독립인 답이 SamplesPerScene 개 모인다.
// 결번자 장면(거짓 알리바이)은 따로 한 번씩 돈다.
//
// 기계로 잡을 수 있는 것(빈칸 누출 · 조사 표기 누출 · 한 답 안의 같은 말 반복 · 거짓말 중
// 실제 위치 누설 · 대사 뱅크 조사 오류 · 미배치 직원 이름 · 세션당 동료 언급 · 방 두 번)은
// PASS/FAIL 로도 보고, 결과는 tools/dialogue_samples/_개요.md 에도 적는다.
public partial class DialogueSampleDump : Node
{
    private const string OutDir = "res://tools/dialogue_samples";
    private const int SamplesPerScene = 10;

    private const string Core = "core_room";
    private const string Guard = "guard_room";
    private const string Power = "power_room";
    private const string Maint = "maintenance_room";
    private const string Storage = "storage_room";
    private const string Medical = "medical_room";

    private static readonly string[] Ids = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private FacilitySimulation _sim;
    private int _pass, _fail;
    private readonly List<string> _checkLines = new();

    // 답변 한 줄 — 파일 출력과 기계 검사가 함께 쓴다.
    private sealed class Said
    {
        public string Scene = "";
        public int Sample;
        public string Speaker = "";
        public string Kind = "";      // 질문 종류(파일의 소제목)
        public string Question = "";
        public string Answer = "";
        public string Trace = "";
        public string Anchor = "?";   // 질문 기준 시각(10분 단위), 모르면 "?"
    }

    private readonly List<Said> _said = new();
    // 장면 이름 → 상황 설명(파일 머리에 적는다). 순서를 지킨다.
    private readonly List<(string Title, string Setup)> _scenes = new();
    private string _scene = "";
    private int _sample;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ Local Dialogue V2 샘플 ################");
        CheckBank();
        DutyByLogDetail();

        RunScene("장면 1 — 혼자 배치", SoloDay, Power);
        RunScene("장면 2 — 둘이 배치", PairDay, Power);
        // 근무 기억 검사는 질문 전에 — 질문을 받고 나면 그 기억은 "이미 한 이야기"가 되어 다시 떠올리지 않는다.
        RunScene("장면 3 — 재배치", RelocationDay, Power,
            before: first => { if (first) CheckMemoryOfRelocationDay(); },
            after: _ => IncomingCalls());
        RunScene("장면 4 — 미배치 포함", UnassignedDay, Storage, before: first =>
        {
            if (first) CheckUnassignedTimeline();
        });
        foreach (string sab in new[] { "sheep", "wolf", "dog" }) SaboteurDay(sab);

        CheckVoiceMarks();
        MachineChecks();
        WriteFiles();

        var answers = _said.Select(x => x.Answer).ToList();
        GD.Print($"\n샘플 {answers.Count}개 → {ProjectSettings.GlobalizePath(OutDir)}/<캐릭터>.md");
        GD.Print($"'씨' 가 들어간 답변 {answers.Count(a => a.Contains("씨"))} / {answers.Count}");
        GD.Print($"################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 대사 뱅크 자체 ───────────────────────────────────────────────
    private void CheckBank()
    {
        var problems = DialogueLineBank.Problems;
        foreach (string p in problems.Take(10)) GD.Print("   뱅크: " + p);
        Check(problems.Count == 0, $"대사 뱅크 조사·형식 오류 없음 ({problems.Count}건)");
        foreach (string id in Ids)
            Check(DialogueLineBank.Has(id, "mem.with", DialogueVoices.Get(id).Formal)
                  && DialogueLineBank.Keys.Any(k => k == $"{id}|mem.worked"),
                $"{id} 근무 기억 슬롯이 캐릭터 파일에 있다");
    }

    // ── 근무 구간은 로그 문구가 아니라 세부 종류(LogDetail)로 가린다 ──────────────────
    // 문구를 일부러 "기절" · "근무 복귀" · "배치 해제" 없이 적는다 — 그래도 판정이 같아야 한다.
    private void DutyByLogDetail()
    {
        NewDay("");
        foreach (string id in new[] { "rabbit", "cat" })
        {
            var st = _sim.GetEmployeeState(id);
            st.AssignedRoomId = Maint; st.CurrentRoomId = Maint; st.Alive = true; st.Isolated = false;
            Log(LogEventType.Relocation, id, Maint, 0f);
        }
        Log(LogEventType.Neglect, "cat", Maint, At(20), "(문구 없음)", detail: LogDetail.Fainted);
        Log(LogEventType.Neglect, "cat", Maint, At(40), "(문구 없음)", detail: LogDetail.Recovered);
        Log(LogEventType.TaskEnd, "cat", Maint, At(60), "(문구 없음)", detail: LogDetail.Unassigned);
        // 반대로 문구만 있고 종류가 없으면 근무 상태는 바뀌지 않는다.
        Log(LogEventType.Neglect, "rabbit", Maint, At(20), "토끼 — 기절, 의무실로 이송");

        bool With(float m) => DialogueContextBuilder.OccupantsAt(Maint, 1, At(m), "rabbit").Contains("cat");
        GD.Print("\n===== 근무 구간(LogDetail) =====");
        Check(With(10) && !With(30) && With(50) && !With(70),
            "기절 · 회복 · 배치 해제는 LogDetail 로 가린다 (22:10 있음 · 22:30 기절 · 22:50 복귀 · 23:10 해제)");
        Check(DialogueContextBuilder.OnDutyAt("rabbit", 1, At(30)), "문구에 '기절'만 있고 종류가 없으면 근무 중으로 본다");
    }

    // ── 장면 하나를 SamplesPerScene 번 ──────────────────────────────────
    // setup 은 NewDay 부터 사건 로그까지 전부 다시 만든다(샘플끼리 주장 · 기억이 섞이지 않게).
    private void RunScene(string title, System.Func<string> setup, string mainIncidentRoom,
        System.Action<bool> before = null, System.Action<bool> after = null)
    {
        GD.Print($"\n===== {title} =====");
        _scene = title;
        for (_sample = 1; _sample <= SamplesPerScene; _sample++)
        {
            string desc = setup();
            if (_sample == 1) _scenes.Add((title, desc));
            before?.Invoke(_sample == 1);
            foreach (string id in Ids) AskEveryKind(id, mainIncidentRoom);
            after?.Invoke(_sample == 1);
        }
    }

    // 한 직원에게 질문 묶음 전부 — 실제 순서대로. 근무 중 전화(23:30) → 휴게시간 심문.
    // 자료가 없어 물을 수 없는 질문(이동 기록이 없는 직원의 이동 이유 등)은 건너뛴다.
    private void AskEveryKind(string id, string mainIncidentRoom)
    {
        Rec(id, "근무 중 전화 · 작업 상태", "작업은 잘 되어가나요?", LocalDialogueGenerator.GeneralAnswer(id, 0));
        // 이 전화는 이 직원이 아는 가장 최근 사고를 기준으로 답한다(LocalDialogueGenerator.GeneralAnswer 와 같은 기준).
        var known = DialogueContextBuilder.MostRecentKnownIncident(id, 1);
        Rec(id, "근무 중 전화 · 이상현상", "주변에 이상현상은 없었나요?", LocalDialogueGenerator.GeneralAnswer(id, 2),
            anchor: known?.GameTimeSeconds ?? -1f);

        var s = new InterviewSession(id);
        // 최초 진술 — 심문을 열면 직원이 먼저 하는 말(기본 질문 세 개의 첫 답). 카드가 되는 문장이다.
        foreach (var o in s.Openings)
            Rec(id, "최초 진술 · " + OpeningName(o.QuestionId), "(심문을 열면 먼저 하는 말)", o.Text, o.Trace);

        var incident = s.Board.Where(e => e.Kind == EvidenceKind.Incident && s.CanUse(e))
            .OrderByDescending(e => e.SubjectRoomId == mainIncidentRoom).FirstOrDefault();
        if (incident != null)
        {
            AskIntent(s, incident, InterviewIntent.AskIncidentKnown, "사고 · 알았나");
            AskIntent(s, incident, InterviewIntent.AskWhereAtIncident, "사고 · 그때 어디");
            AskIntent(s, incident, InterviewIntent.AskWhoWasPresent, "사고 · 같이 있던 사람");
            AskIntent(s, incident, InterviewIntent.AskBeforeIncident, "사고 · 직전");
            // 방금 한 위치 진술이 자료가 됐다 — 그걸 들이밀며 다시 묻는다.
            var own = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement && s.CanUse(e));
            if (own != null) AskIntent(s, own, InterviewIntent.AskRestate, "진술 재확인");
        }
        var move = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Movement && s.CanUse(e));
        if (move != null)
        {
            AskIntent(s, move, InterviewIntent.AskMoveReason, "이동 · 이유");
            AskIntent(s, move, InterviewIntent.AskActionAtDestination, "이동 · 거기서 한 일");
        }
        var mood = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Mood && s.CanUse(e));
        if (mood != null)
        {
            AskIntent(s, mood, InterviewIntent.AskMoodReason, "기분 · 이유");
            AskIntent(s, mood, InterviewIntent.AskMoodBefore, "기분 · 근무 전부터");
        }
        // 의심 질문은 오늘의 대표 사고(가장 무거운 · 가장 최근)를 기준으로 답한다.
        Rec(id, "의심받을 때", "당신을 의심하고 있습니다.",
            LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Accuse),
            anchor: DialogueContextBuilder.SelectSubjectIncident(1)?.GameTimeSeconds ?? -1f);
        var again = s.BasicQuestions()[1];
        Rec(id, "기본 질문 다시(이상한 점)", again.Text + " (다시)", s.Ask(again).Answer);
    }

    private void AskIntent(InterviewSession s, InterviewEvidence ev, InterviewIntent intent, string kind)
    {
        var q = InterviewQuestionFactory.Make(s.EmployeeId, ev, intent);
        if (string.IsNullOrEmpty(q.Text)) return;
        string a = s.Ask(q).Answer;
        Rec(s.EmployeeId, kind, q.Text, a, anchor: q.HasAnchorTime ? q.AnchorTime : -1f);
    }

    private static string OpeningName(string questionId) => questionId switch
    {
        DialogueQuestions.ShiftReview => "근무 소감",
        DialogueQuestions.Anomaly => "이상한 점",
        DialogueQuestions.Suspicious => "수상한 사람",
        _ => questionId,
    };

    // ── 장면 1: 혼자 배치 — 여섯 명이 모두 다른 방 ──────────────────────────
    // 실제 게임처럼 배치표 로그 없이(BeginShift 가 지운다) 근무 시작 배치만 적어 두고 시작한다.
    private string SoloDay()
    {
        NewDay("");
        StartShift(new[]
        {
            ("rabbit", Maint), ("cat", Storage), ("fox", Core), ("sheep", Medical), ("wolf", Guard), ("dog", Power),
        });
        CallMemoryLog.RecordAt(1, At(30), "rabbit", CallRecordKind.ManagerCalled, Maint);
        Log(LogEventType.TaskFailed, "", Power, At(40), witnesses: new[] { "dog" });
        CallMemoryLog.RecordAt(1, At(41), "dog", CallRecordKind.Reported, Power, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(42), "dog", CallRecordKind.OrderedStay, Power, DialogueRepository.EventAccidentNearby);
        Log(LogEventType.TaskFailed, "", Storage, At(70), witnesses: new[] { "cat" });
        GameState.Instance.AdvanceDayTime(At(90));
        return "배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)\n" +
               "배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화\n" +
               "22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)\n" +
               "사고 질문은 22:40 발전실 건 기준";
    }

    // ── 장면 2: 둘이 배치 — 세 쌍 ─────────────────────────────────────
    private string PairDay()
    {
        NewDay("");
        StartShift(new[]
        {
            ("cat", Power), ("dog", Power), ("fox", Storage), ("sheep", Storage), ("rabbit", Maint), ("wolf", Maint),
        });
        CallMemoryLog.RecordAt(1, At(30), "wolf", CallRecordKind.ManagerCalled, Maint);
        Log(LogEventType.TaskFailed, "", Power, At(40), witnesses: new[] { "cat", "dog" });
        CallMemoryLog.RecordAt(1, At(41), "dog", CallRecordKind.Reported, Power, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(42), "dog", CallRecordKind.OrderedStay, Power, DialogueRepository.EventAccidentNearby);
        Log(LogEventType.TaskFailed, "", Storage, At(70), witnesses: new[] { "fox", "sheep" });
        CallMemoryLog.RecordAt(1, At(71), "sheep", CallRecordKind.Missed, Storage, DialogueRepository.EventAccidentNearby);
        GameState.Instance.AdvanceDayTime(At(90));
        return "배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실\n" +
               "배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화\n" +
               "22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재\n" +
               "사고 질문은 22:40 발전실 건 기준";
    }

    // ── 장면 3: 재배치 — 사고 2건 · 재배치 2번 · 전화 지시 · 놓친 전화 · 수리 완료 ────────
    // 배치표 로그(Relocation 0초)가 남아 있는 경로도 여기서 본다.
    private string RelocationDay()
    {
        NewDay("");
        Deploy();
        string repairGen = TaskName("repair_generator", "발전기 수리");

        Relocate("dog", Guard, Power, At(25), TaskName("power_generator_check", "발전기 점검"));
        CallMemoryLog.RecordAt(1, At(30), "rabbit", CallRecordKind.ManagerCalled, Maint);
        Log(LogEventType.TaskFailed, "", Power, At(40), witnesses: new[] { "dog" });
        CallMemoryLog.RecordAt(1, At(41), "wolf", CallRecordKind.Reported, Power, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(42), "wolf", CallRecordKind.OrderedGo, Power, DialogueRepository.EventAccidentNearby);
        Move("wolf", Guard, Power, At(45));
        Log(LogEventType.TaskStart, "wolf", Power, At(46), $"🔧 늑대 발전실 도착 / {repairGen} 시작", new[] { "dog" });
        Log(LogEventType.TaskComplete, "", Power, At(60), $"✓ 발전실 — '{repairGen}' 수리 완료 · 기능 복구");
        Log(LogEventType.TaskFailed, "", Maint, At(70), witnesses: new[] { "rabbit", "cat" });
        CallMemoryLog.RecordAt(1, At(71), "sheep", CallRecordKind.Missed, Maint, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(72), "cat", CallRecordKind.Reported, Maint, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(72.5f), "cat", CallRecordKind.OrderedStay, Maint, DialogueRepository.EventAccidentNearby);
        Relocate("rabbit", Maint, Storage, At(80), TaskName("inventory_sorting", "재고 정리"));
        GameState.Instance.AdvanceDayTime(At(90));
        return "배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)\n" +
               "22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)\n" +
               "22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)\n" +
               "23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치\n" +
               "사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다";
    }

    // ── 장면 4: 미배치 포함 — 오늘 근무하지 않은 직원과 근무 중에 옮겨진 직원 ─────────
    //   양   : 배치되지 않았다. 기본 시작실(저장고)에 서 있을 뿐 오늘 근무자가 아니다.
    //   여우 : 코어실에서 근무를 시작해 22:20 저장고로 옮겨졌다.
    private const string Idle = "sheep", Moved = "fox";

    private string UnassignedDay()
    {
        NewDay("");
        StartShift(new[] { ("rabbit", Maint), ("cat", Maint), (Moved, Core), ("wolf", Guard), ("dog", Guard) },
            unassigned: Idle);
        Log(LogEventType.TaskFailed, "", Core, At(10), witnesses: new[] { Moved });
        Relocate(Moved, Core, Storage, At(20), TaskName("inventory_sorting", "재고 정리"));
        Log(LogEventType.TaskFailed, "", Storage, At(40), witnesses: new[] { Moved });
        Log(LogEventType.TaskFailed, "", Maint, At(60), witnesses: new[] { "rabbit", "cat" });
        GameState.Instance.AdvanceDayTime(At(90));
        return "배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)\n" +
               "배치표 로그 없음(근무 시작 배치만 기록)\n" +
               "22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)\n" +
               "23:00 정비실 설비 고장(토끼·고양이 목격)\n" +
               "사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)";
    }

    private void CheckUnassignedTimeline()
    {
        Check(!DialogueContextBuilder.OccupantsAt(Storage, 1, At(40), Moved).Contains(Idle),
            "미배치 — 22:40 저장고 동석자에 근무하지 않은 양이 없다");
        Check(DialogueContextBuilder.RoomAt(Idle, 1, At(40)) == "",
            "미배치 — 근무하지 않은 양에게는 동선이 없다");
        Check(DialogueContextBuilder.RoomAt(Moved, 1, At(10)) == Core,
            $"재배치 — 여우는 옮겨지기 전(22:10)에 코어실에 있었다 ({RoomName(DialogueContextBuilder.RoomAt(Moved, 1, At(10)))})");
    }

    // 직원이 먼저 거는 발전실 사고 신고 — 장면 3 에서 그 사고를 아는 사람만.
    private void IncomingCalls()
    {
        foreach (string id in new[] { "wolf", "sheep", "dog" })
        {
            var call = LocalDialogueGenerator.BuildIncomingCall(id, DialogueRepository.EventAccidentNearby, Power);
            if (call == null) continue;
            // 첫 대사와 두 대답은 전화가 오는 순간 한꺼번에 만들어진다 — 틀 이름은 슬롯 규약으로 적는다.
            Rec(id, "수신 전화 · 사고 신고", "(직원이 전화를 건다)", call.Opening,
                "call.prefix / report.direct · report.indirect");
            for (int i = 0; i < call.Choices.Count; i++)
                Rec(id, i == 0 ? "수신 전화 · 가라는 지시에" : "수신 전화 · 대기 지시에",
                    call.Choices[i].Text, call.Choices[i].Reply, i == 0 ? "accept" : "decline");
        }
    }

    // ── 근무 기억 자체의 정확성(장면 3) ───────────────────────────────────
    private void CheckMemoryOfRelocationDay()
    {
        GD.Print("\n----- 근무 기억 -----");
        bool Has(string id, MemoryKind k, string room) =>
            ShiftMemory.Of(id, 1).Any(m => m.Kind == k && (room == null || m.RoomId == room));
        foreach (string id in Ids)
            GD.Print($"   {Nm(id)}: " + string.Join(" · ", ShiftMemory.Of(id, 1)
                .Select(m => $"{DialogueClock.Text(m.Time)} {m.Kind}{(m.RoomId.Length > 0 ? "@" + RoomName(m.RoomId) : "")}")));
        Check(Has("wolf", MemoryKind.Dispatched, Power), "늑대 — 전화 지시로 발전실 출동을 기억한다");
        Check(Has("wolf", MemoryKind.IncidentHeard, Power), "늑대 — 옆방(발전실) 사고를 소리로 기억한다");
        Check(Has("wolf", MemoryKind.RepairDone, Power), "늑대 — 자기가 있던 발전실 복구를 기억한다");
        Check(Has("sheep", MemoryKind.CallMissed, null), "양 — 관리자가 안 받은 전화를 기억한다");
        Check(Has("dog", MemoryKind.Relocated, Power) && Has("dog", MemoryKind.IncidentHere, Power),
            "강아지 — 재배치와 그 방의 사고를 기억한다");
        Check(Has("cat", MemoryKind.CallStay, null), "고양이 — 대기 지시를 기억한다");
        Check(!Has("fox", MemoryKind.IncidentHeard, Power) && !Has("fox", MemoryKind.IncidentHere, Power),
            "여우 — 먼 방(발전실) 사고는 기억에 없다");

        // 질문 시각(발전실 사고) 기준으로 강아지가 떠올리는 사람은 그 방 사람이어야 한다.
        var withNames = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            var r = ShiftMemory.Recall(new RecallRequest
            {
                EmployeeId = "dog", Day = 1, Topic = RecallTopic.Location, AnchorTime = At(47), AnchorRoom = Power,
            });
            foreach (var a in r.Addenda)
                if (a.Vars.TryGetValue("who", out var w)) withNames.Add(w);
        }
        Check(withNames.All(n => n == Nm("wolf")), $"강아지 — 22:47 발전실에서 같이 있던 사람은 늑대뿐 ({string.Join(",", withNames)})");
    }

    // ── 결번자가 거짓 알리바이를 대는 하루(장면마다 한 번) ──────────────────────
    private void SaboteurDay(string sab)
    {
        NewDay(sab);
        Deploy();
        string assigned = _sim.GetEmployeeState(sab).AssignedRoomId;
        string real = assigned == Storage ? Core : Storage;
        // 실제로는 배치 자리를 벗어나 다른 방에서 방해공작.
        Move(sab, assigned, real, At(30));
        Log(LogEventType.Sabotage, sab, real, At(33));
        Move(sab, real, assigned, At(38));
        Log(LogEventType.TaskFailed, "", real, At(42));
        GameState.Instance.AdvanceDayTime(At(90));

        string title = $"결번자 장면 — {Nm(sab)}";
        _scene = title; _sample = 1;
        _scenes.Add((title, KoreanParticle.Resolve(
            $"{Nm(sab)}은/는 {RoomName(assigned)} 배치 → 22:30 몰래 {RoomName(real)} → 22:33 방해공작 → 22:38 복귀 → 22:42 {RoomName(real)} 고장.\n" +
            "결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.")));
        GD.Print($"\n===== {title} =====");

        CheckLieMemory(sab, assigned, real);
        var session = new InterviewSession(sab);
        foreach (var o in session.Openings)
            Rec(sab, "최초 진술 · " + OpeningName(o.QuestionId), "(심문을 열면 먼저 하는 말)", o.Text, o.Trace);
        var incident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (incident != null)
        {
            foreach (var (intent, kind) in new[]
                     {
                         (InterviewIntent.AskWhereAtIncident, "사고 · 그때 어디"), (InterviewIntent.AskBeforeIncident, "사고 · 직전"),
                         (InterviewIntent.AskWhoWasPresent, "사고 · 같이 있던 사람"), (InterviewIntent.AskIncidentKnown, "사고 · 알았나"),
                     })
            {
                var q = InterviewQuestionFactory.Make(sab, incident, intent);
                string a = session.Ask(q).Answer;
                Rec(sab, kind, q.Text, a, anchor: q.HasAnchorTime ? q.AnchorTime : -1f);
                if (intent == InterviewIntent.AskWhereAtIncident)
                {
                    // 결번자 전략이 '축소·합리화'면 위치 자체는 인정한다 — 그때는 누설 검사가 아니다.
                    string claimed = InterviewReplyPlanner.FrameFor(q)?.Vars.GetValueOrDefault("room", "") ?? "";
                    if (claimed == RoomName(real))
                        GD.Print($"   ({sab} 은/는 이번엔 위치를 인정하는 전략 — 축소/합리화)");
                    else
                        Check(!a.Contains(RoomName(real)), $"[{sab}] 거짓 알리바이({claimed}) 중 실제 위치({RoomName(real)})를 말하지 않는다");
                }
            }
        }
        Rec(sab, "의심받을 때", "당신을 의심하고 있습니다.",
            LocalDialogueGenerator.InterviewAnswer(sab, DialogueQuestions.Accuse));

        // 같은 방 동료(정상)의 답과 나란히 본다 — 그 동료의 파일에 들어간다.
        string mate = Ids.FirstOrDefault(id => id != sab && _sim.GetEmployeeState(id).AssignedRoomId == assigned);
        if (!string.IsNullOrEmpty(mate) && incident != null)
        {
            var ms = new InterviewSession(mate);
            var q = InterviewQuestionFactory.Make(mate, incident, InterviewIntent.AskWhoWasPresent);
            Rec(mate, $"결번자({Nm(sab)})와 같은 배치 · 같이 있던 사람", q.Text, ms.Ask(q).Answer,
                anchor: q.HasAnchorTime ? q.AnchorTime : -1f);
        }
    }

    private void CheckLieMemory(string sab, string claimed, string real)
    {
        string realName = RoomName(real);
        int leaks = 0, total = 0;
        for (int i = 0; i < 60; i++)
        {
            var r = ShiftMemory.Recall(new RecallRequest
            {
                EmployeeId = sab, Day = 1, Topic = RecallTopic.Location, AnchorTime = At(33),
                AnchorRoom = claimed, Lying = true, IsSaboteur = true,
            });
            foreach (var a in r.Addenda)
            {
                total++;
                if (a.Vars.Values.Any(v => v == realName)) leaks++;
            }
        }
        Check(leaks == 0, $"[{sab}] 거짓말 중 근무 기억이 실제 방({realName})을 흘리지 않는다 (덧붙임 {total}개 중 {leaks})");
    }

    // ── 말버릇 — 장면 2(둘이 배치)의 위치 · 동석자 · 의심 답만 본다 ──────────────
    private void CheckVoiceMarks()
    {
        GD.Print("\n===== 말버릇 =====");
        var kinds = new[] { "사고 · 그때 어디", "사고 · 같이 있던 사람", "의심받을 때" };
        List<string> Of(string id) => _said
            .Where(x => x.Scene.StartsWith("장면 2") && x.Speaker == id && kinds.Contains(x.Kind))
            .Select(x => x.Answer).ToList();
        Check(Of("sheep").All(DialogueVoiceTics.HasStutter), $"양 — 모든 답변에 더듬기가 있다 ({Of("sheep").Count(DialogueVoiceTics.HasStutter)}/{Of("sheep").Count})");
        Check(Of("fox").Count(a => a.Contains('~')) * 2 >= Of("fox").Count, $"여우 — 절반 이상의 답변에 '~' ({Of("fox").Count(a => a.Contains('~'))}/{Of("fox").Count})");
        Check(Of("rabbit").Count(a => a.Contains('!')) * 10 >= Of("rabbit").Count * 8, $"토끼 — 대부분의 답변에 '!' ({Of("rabbit").Count(a => a.Contains('!'))}/{Of("rabbit").Count})");
        Check(Of("wolf").All(a => !a.Contains('!') && !a.Contains('~')), "늑대 — 느낌표 · 물결 없음");
        Check(Of("wolf").All(a => a.Contains("니다")), "늑대 — 합쇼체");
        Check(Of("cat").All(a => !a.Contains('!')), "고양이 — 느낌표 없음");
    }

    // ── 기계 검사 ─────────────────────────────────────────────────────
    private static readonly Regex Leak = new(@"[{}]|(은/는|이/가|을/를|으로/로|이에요/예요|이요/요|이었/였)");

    private void MachineChecks()
    {
        GD.Print("\n===== 기계 검사 =====");
        var answers = _said.Select(x => x.Answer).ToList();
        var leaks = answers.Where(a => Leak.IsMatch(a)).ToList();
        foreach (var a in leaks.Take(5)) GD.Print("   누출: " + a);
        Check(leaks.Count == 0, $"변수·조사 표기가 새지 않는다 ({leaks.Count}건)");

        var empty = answers.Where(a => string.IsNullOrWhiteSpace(a) || a == "…").ToList();
        Check(empty.Count == 0, $"빈 답변 없음 ({empty.Count}건)");

        var dup = answers.Where(HasRepeatedSentence).ToList();
        foreach (var a in dup.Take(5)) GD.Print("   반복: " + a);
        Check(dup.Count == 0, $"한 답변 안에서 같은 말을 두 번 하지 않는다 ({dup.Count}건)");

        Check(answers.Count >= 1000, $"샘플 1000개 이상 ({answers.Count})");

        // (a) 근무하지 않은 직원의 이름("양 씨", "양 직원", "양이랑" …)이 누구의 답에도 나오지 않는다.
        string name = Nm(Idle);
        var named = new Regex($@"(?<![가-힣]){Regex.Escape(name)}(?= 씨| 직원|이랑|하고|도 |이 )");
        var unassigned = _said.Where(x => x.Scene.StartsWith("장면 4") && x.Speaker != Idle && named.IsMatch(x.Answer)).ToList();
        foreach (var x in unassigned.Take(5)) GD.Print($"   미배치 누설: [{Nm(x.Speaker)}] {x.Answer}");
        Check(unassigned.Count == 0,
            $"(a) 배치되지 않은 직원({name}) 이름이 어떤 답변에도 나오지 않는다 ({unassigned.Count}건)");

        // (b) 같은 세션(장면 · 샘플 · 직원 · 질문 시각)에서 동료 이야기(같이 있던 사람 · 혼자였다)는 한 번까지.
        var over = _said
            .Select(x => (x, n: CompanionMemories(x.Trace)))
            .Where(p => p.n > 0)
            .GroupBy(p => $"{p.x.Scene} #{p.x.Sample} | {Nm(p.x.Speaker)} | {p.x.Anchor}")
            .Where(g => g.Sum(p => p.n) > 1)
            .ToList();
        foreach (var g in over.Take(5))
            GD.Print($"   동료 반복: {g.Key} ×{g.Sum(p => p.n)} — " + string.Join(" / ", g.Select(p => p.x.Answer)));
        Check(over.Count == 0, $"(b) 같은 세션에서 동료 언급이 한 번을 넘지 않는다 ({over.Count}개 세션)");

        // 최초 진술(카드가 되는 문장)에는 동료 기억이 붙지 않는다.
        var openings = _said.Where(x => x.Kind.StartsWith("최초 진술")).ToList();
        var chatty = openings.Where(x => CompanionMemories(x.Trace) > 0).ToList();
        foreach (var x in chatty.Take(5)) GD.Print($"   최초 진술 동료: [{Nm(x.Speaker)}] {x.Answer}");
        Check(openings.Count > 0 && chatty.Count == 0,
            $"최초 진술에 동료 기억이 붙지 않는다 ({chatty.Count}/{openings.Count})");

        // (c) 한 답 안에서 같은 작업실 이름을 두 번 말하지 않는다.
        var roomNames = _sim.GetRoomIds().Select(RoomName).Where(n => n.Length > 0).Distinct().ToList();
        var twice = answers.Where(a => roomNames.Any(n => Count(a, n) >= 2)).ToList();
        foreach (var a in twice.Take(5)) GD.Print("   방 반복: " + a);
        Check(twice.Count == 0, $"(c) 한 답 안에 같은 작업실을 두 번 말한 답 0개 ({twice.Count}개)");
    }

    // 트레이스에서 실제로 답에 남은 동료 기억 줄(mem.with* · mem.alone)의 수.
    private static int CompanionMemories(string trace)
    {
        var m = Regex.Match(trace ?? "", @"기억\[(.*)\]");
        if (!m.Success) return 0;
        return m.Groups[1].Value.Split(", ")
            .Count(s => (s.StartsWith("mem.with") || s.StartsWith("mem.alone")) && !s.EndsWith("(잘림)"));
    }

    private static int Count(string text, string word)
    {
        int n = 0;
        for (int i = text.IndexOf(word, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(word, i + word.Length, System.StringComparison.Ordinal)) n++;
        return n;
    }

    private static bool HasRepeatedSentence(string a)
    {
        var parts = Regex.Split(a, @"(?<=[.!?])\s+").Where(p => p.Length > 0).ToList();
        for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
                if (DialogueNaturalnessFilter.Repeats(parts[i], parts[j])) return true;
        return false;
    }

    // ── 출력 ─────────────────────────────────────────────────────────
    // 캐릭터마다 한 파일: 장면 → 질문 종류 → 샘플 답변(틀 · 기억 포함).
    private void WriteFiles()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
        foreach (string id in Ids)
        {
            var md = new StringBuilder();
            md.AppendLine($"# {Nm(id)} — 대사 샘플");
            md.AppendLine();
            md.AppendLine("`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.");
            md.AppendLine($"장면 1~4 는 장면을 {SamplesPerScene}번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다" +
                          "(질문 종류마다 답 " + SamplesPerScene + "개). 결번자 장면은 한 번씩입니다.");
            md.AppendLine("각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.");
            md.AppendLine($"어색한 줄을 찾으면 `data/dialogue/lines/{id}.txt` 의 그 슬롯을 고치면 됩니다.");
            md.AppendLine();
            foreach (var (title, setup) in _scenes)
            {
                var mine = _said.Where(x => x.Scene == title && x.Speaker == id).ToList();
                if (mine.Count == 0) continue;
                md.AppendLine($"## {title}");
                md.AppendLine();
                foreach (string line in setup.Split('\n')) md.AppendLine($"- {KoreanParticle.Resolve(line)}");
                md.AppendLine();
                foreach (var kind in mine.Select(x => x.Kind).Distinct())
                {
                    var group = mine.Where(x => x.Kind == kind).ToList();
                    md.AppendLine($"### {kind}");
                    md.AppendLine();
                    foreach (var q in group.Select(x => x.Question).Distinct()) md.AppendLine($"Q: {q}  ");
                    md.AppendLine();
                    foreach (var x in group)
                        md.AppendLine($"- {x.Answer} <sub>틀: {x.Trace}</sub>");
                    md.AppendLine();
                }
            }
            Save($"{OutDir}/{id}.md", md.ToString());
        }

        var o = new StringBuilder();
        o.AppendLine("# 대사 샘플 — 개요");
        o.AppendLine();
        o.AppendLine("캐릭터별 답변은 같은 폴더의 `<캐릭터>.md` 에 있습니다: " +
                     string.Join(" · ", Ids.Select(i => $"[{Nm(i)}]({i}.md)")));
        o.AppendLine();
        var answers = _said.Select(x => x.Answer).ToList();
        o.AppendLine($"- 답변 {answers.Count}개 · '씨' 가 들어간 답변 {answers.Count(a => a.Contains("씨"))}개");
        o.AppendLine($"- 기계 검사 {_pass} PASS / {_fail} FAIL");
        o.AppendLine();
        o.AppendLine("## 장면");
        o.AppendLine();
        foreach (var (title, setup) in _scenes)
        {
            o.AppendLine($"### {title}");
            o.AppendLine();
            foreach (string line in setup.Split('\n')) o.AppendLine($"- {KoreanParticle.Resolve(line)}");
            o.AppendLine();
        }
        o.AppendLine("## 기계 검사");
        o.AppendLine();
        foreach (string line in _checkLines) o.AppendLine($"- {line}");
        Save($"{OutDir}/_개요.md", o.ToString());
    }

    private static void Save(string path, string text)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f?.StoreString(text);
    }

    private void Rec(string id, string kind, string question, string answer, string traceOverride = null,
        float anchor = -1f)
    {
        _said.Add(new Said
        {
            Scene = _scene, Sample = _sample, Speaker = id, Kind = kind, Question = question,
            Answer = answer ?? "", Trace = traceOverride ?? DialogueComposer.LastTrace,
            Anchor = anchor >= 0f ? ((int)(anchor / At(10))).ToString() : "?",
        });
    }

    // ── 장면 만들기 ───────────────────────────────────────────────────
    private void NewDay(string saboteur)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        DialogueClaimState.ResetAll();   // 통화 기록 · 근무 기억 · 진술도 함께 비운다
        GameState.Instance.SetSaboteur(saboteur);
        _sim.RollDailyMoods();
    }

    // 실제 근무 시작과 같은 경로 — 배치만 해 두고(배치표 로그는 BeginShift 가 지운다) 근무 시작 배치를 적는다.
    // 배치된 사람은 근무 시작 직후 그 방에서 업무를 시작한다(같은 방 사람이 곧 같이 일한 사람).
    private void StartShift((string Id, string Room)[] plan, string unassigned = null)
    {
        foreach (var p in plan)
        {
            var st = _sim.GetEmployeeState(p.Id);
            st.AssignedRoomId = p.Room; st.CurrentRoomId = p.Room; st.Alive = true; st.Isolated = false;
        }
        if (!string.IsNullOrEmpty(unassigned))
        {
            var st = _sim.GetEmployeeState(unassigned);
            st.AssignedRoomId = "";
            st.CurrentRoomId = _sim.GetEmployeeDef(unassigned)?.StartRoomId ?? Storage;
        }
        _sim.RecordShiftStart();
        foreach (var p in plan)
        {
            var mates = plan.Where(o => o.Room == p.Room && o.Id != p.Id).Select(o => o.Id).ToArray();
            Log(LogEventType.TaskStart, p.Id, p.Room, 1f, $"{Nm(p.Id)} {RoomName(p.Room)} 도착 / {RoomTask(p.Room)} 시작", mates);
        }
    }

    // 그 방의 평소 업무(근무 기억 "…하고 있었어요" 에 이름이 나온다).
    private string RoomTask(string room) => room switch
    {
        Maint => TaskName("materials_production", "자재 생산"),
        Storage => TaskName("inventory_sorting", "재고 정리"),
        Core => TaskName("core_direct_repair", "코어 직접 수리"),
        Guard => TaskName("lockdown_gear_check", "봉쇄 장치 점검"),
        Power => TaskName("power_generator_check", "발전기 점검"),
        Medical => TaskName("medicine_sorting", "의약품 정리"),
        _ => "",
    };

    private void Deploy()
    {
        var plan = new (string Id, string Room, string Task)[]
        {
            ("rabbit", Maint, TaskName("materials_production", "자재 생산")),
            ("cat", Maint, TaskName("materials_production", "자재 생산")),
            ("fox", Core, TaskName("core_direct_repair", "코어 직접 수리")),
            ("sheep", Storage, TaskName("inventory_sorting", "재고 정리")),
            ("wolf", Guard, TaskName("lockdown_gear_check", "봉쇄 장치 점검")),
            ("dog", Guard, TaskName("lockdown_gear_check", "봉쇄 장치 점검")),
        };
        foreach (var p in plan)
        {
            var st = _sim.GetEmployeeState(p.Id);
            st.AssignedRoomId = p.Room;
            st.CurrentRoomId = p.Room;
            st.Alive = true;
            st.Isolated = false;
            Log(LogEventType.Relocation, p.Id, p.Room, 0f, $"{Nm(p.Id)} → {RoomName(p.Room)} 배치");
        }
        foreach (var p in plan)
        {
            var mates = plan.Where(o => o.Room == p.Room && o.Id != p.Id).Select(o => o.Id).ToArray();
            Log(LogEventType.TaskStart, p.Id, p.Room, 1f, $"{Nm(p.Id)} {RoomName(p.Room)} 도착 / {p.Task} 시작", mates);
        }
    }

    private void Relocate(string id, string from, string to, float at, string task)
    {
        Log(LogEventType.Relocation, id, to, at, $"{Nm(id)} → {RoomName(to)} 배치");
        Move(id, from, to, at + 2f * DialogueClock.SecondsPerMinute);
        // 같이 일을 시작하는 사람 = 그 방에서 근무 중인 사람(게임의 TaskStart 도 근무자만 적는다).
        var mates = DialogueContextBuilder.OccupantsAt(to, 1, at, id).ToArray();
        Log(LogEventType.TaskStart, id, to, at + 3f * DialogueClock.SecondsPerMinute,
            $"{Nm(id)} {RoomName(to)} 도착 / {task} 시작", mates);
        var st = _sim.GetEmployeeState(id);
        st.AssignedRoomId = to;
        st.CurrentRoomId = to;
    }

    private static void Move(string id, string from, string to, float arriveAt)
    {
        Log(LogEventType.RoomExit, id, from, Mathf.Max(0f, arriveAt - 1f));
        Log(LogEventType.RoomEnter, id, to, arriveAt);
    }

    private static void Log(LogEventType type, string actor, string room, float at, string desc = null,
        IEnumerable<string> witnesses = null, LogDetail detail = LogDetail.None)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = room,
            Description = desc ?? $"(샘플 {type} {room})", Detail = detail,
            WitnessEmployeeIds = witnesses != null ? new List<string>(witnesses) : new List<string>(),
        });
    }

    private static float At(float gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private string TaskName(string taskId, string fallback) => _sim.GetTaskDef(taskId)?.DisplayName ?? fallback;
    private string Nm(string id) => _sim.GetEmployeeDef(id)?.Codename ?? id;
    private string RoomName(string id) => _sim.GetRoomDef(id)?.DisplayName ?? id;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        _checkLines.Add($"{(ok ? "PASS" : "**FAIL**")} {what}");
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
