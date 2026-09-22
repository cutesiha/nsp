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
// 통계 검사로는 "사람이 이렇게 말하는가"를 알 수 없다. 그래서 실제 근무 하루를 로그로
// 재현하고 6명에게 모든 종류의 질문을 던진 뒤, 답변 100여 개를 tools/dialogue_samples.md 에
// 상황 설명과 함께 적는다. 사람이 읽고 어색한 줄을 찾으면 된다.
//
// 기계로 잡을 수 있는 것(빈칸 누출 · 조사 표기 누출 · 한 답 안의 같은 말 반복 · 거짓말 중
// 실제 위치 누설 · 대사 뱅크 조사 오류)은 여기서 PASS/FAIL 로도 본다.
public partial class DialogueSampleDump : Node
{
    private const string OutPath = "res://tools/dialogue_samples.md";

    private const string Core = "core_room";
    private const string Guard = "guard_room";
    private const string Power = "power_room";
    private const string Maint = "maintenance_room";
    private const string Storage = "storage_room";

    private static readonly string[] Ids = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private FacilitySimulation _sim;
    private readonly StringBuilder _md = new();
    private readonly List<string> _answers = new();
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ Local Dialogue V2 샘플 ################");
        _md.AppendLine("# Local Dialogue V2 — 대사 샘플");
        _md.AppendLine();
        _md.AppendLine("`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.");
        _md.AppendLine("각 답변 아래 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯입니다.");
        _md.AppendLine("어색한 줄을 찾으면 `data/dialogue/lines/<캐릭터>.txt` 의 그 슬롯을 고치면 됩니다.");
        _md.AppendLine();

        CheckBank();
        VoiceCompare();
        NormalDay();
        foreach (string sab in new[] { "sheep", "wolf", "dog" }) SaboteurDay(sab);
        MachineChecks();

        string path = ProjectSettings.GlobalizePath(OutPath);
        using (var f = FileAccess.Open(OutPath, FileAccess.ModeFlags.Write))
            f?.StoreString(_md.ToString());
        GD.Print($"\n샘플 {_answers.Count}개 → {path}");
        GD.Print($"################ 결과: {_pass} PASS / {_fail} FAIL ################");
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

    // ── 평범한 하루: 사고 2건 · 재배치 · 전화 지시 · 놓친 전화 · 수리 완료 ────────
    private void NormalDay()
    {
        NewDay("");
        Deploy();
        string repairGen = TaskName("repair_generator", "발전기 수리");

        // 22시대 — 강아지를 발전실로 재배치.
        Relocate("dog", Guard, Power, At(25), TaskName("power_generator_check", "발전기 점검"));
        // 관리자가 토끼에게 전화.
        CallMemoryLog.RecordAt(1, At(30), "rabbit", CallRecordKind.ManagerCalled, Maint);
        // 발전실 사고 — 강아지는 그 방(직접), 늑대는 옆방 경비실(소리).
        Log(LogEventType.TaskFailed, "", Power, At(40), witnesses: new[] { "dog" });
        // 늑대가 전화로 신고 → "확인하러 가라" → 발전실로 가서 수리.
        CallMemoryLog.RecordAt(1, At(41), "wolf", CallRecordKind.Reported, Power, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(42), "wolf", CallRecordKind.OrderedGo, Power, DialogueRepository.EventAccidentNearby);
        Move("wolf", Guard, Power, At(45));
        Log(LogEventType.TaskStart, "wolf", Power, At(46), $"🔧 늑대 발전실 도착 / {repairGen} 시작", new[] { "dog" });
        Log(LogEventType.TaskComplete, "", Power, At(60), $"✓ 발전실 — '{repairGen}' 수리 완료 · 기능 복구");
        // 정비실 사고 — 토끼·고양이는 그 방, 양은 옆방 저장고. 양이 전화했지만 관리자가 못 받음.
        Log(LogEventType.TaskFailed, "", Maint, At(70), witnesses: new[] { "rabbit", "cat" });
        CallMemoryLog.RecordAt(1, At(71), "sheep", CallRecordKind.Missed, Maint, DialogueRepository.EventAccidentNearby);
        // 고양이가 전화로 대기 지시를 받음.
        CallMemoryLog.RecordAt(1, At(72), "cat", CallRecordKind.Reported, Maint, DialogueRepository.EventAccidentNearby);
        CallMemoryLog.RecordAt(1, At(72.5f), "cat", CallRecordKind.OrderedStay, Maint, DialogueRepository.EventAccidentNearby);
        // 토끼를 저장고로 재배치.
        Relocate("rabbit", Maint, Storage, At(80), TaskName("inventory_sorting", "재고 정리"));

        Section("장면 1 — 평범한 하루",
            "배치: 토끼·고양이=정비실(자재 생산) · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실\n" +
            "22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)\n" +
            "22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)\n" +
            "23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치");

        GameState.Instance.AdvanceDayTime(At(90));
        CheckMemoryOfNormalDay();
        foreach (string id in Ids) InterviewAll(id, maxEvidence: 4);

        Sub("근무 중 전화(관리자가 걸었다고 가정 — 23:30)");
        foreach (string id in Ids)
        {
            Answer(id, "작업은 잘 되어가나요?", LocalDialogueGenerator.GeneralAnswer(id, 0));
            Answer(id, "주변에 이상현상은 없었나요?", LocalDialogueGenerator.GeneralAnswer(id, 2));
        }

        Sub("직원이 먼저 거는 사고 신고(발전실)");
        foreach (string id in new[] { "wolf", "sheep", "dog" })
        {
            var call = LocalDialogueGenerator.BuildIncomingCall(id, DialogueRepository.EventAccidentNearby, Power);
            if (call == null) { _md.AppendLine($"- ({Nm(id)}: 발전실 사고를 모름 → 전화 없음)"); continue; }
            // 첫 대사와 두 대답은 전화가 오는 순간 한꺼번에 만들어진다 — 틀 이름은 슬롯 규약으로 적는다.
            Answer(id, "(전화가 온다)", call.Opening, "", "call.prefix / report.direct · report.indirect");
            for (int i = 0; i < call.Choices.Count; i++)
                Answer(id, call.Choices[i].Text, call.Choices[i].Reply, "", i == 0 ? "accept" : "decline");
        }

        Sub("같은 질문을 다시 받았을 때");
        foreach (string id in new[] { "rabbit", "wolf", "sheep" })
        {
            var s = new InterviewSession(id);
            var q = s.BasicQuestions()[1];
            Answer(id, q.Text + " (다시)", s.Ask(q).Answer);
        }
    }

    // ── 말투 비교: 같은 질문을 6명에게 여러 번 ─────────────────────────────
    // 캐릭터 말투가 실제로 갈리는지 한눈에 본다. 둘씩 같은 방에 둬서 "누구랑 있었나"에 모두 이름이 나오게 한다.
    private void VoiceCompare()
    {
        NewDay("");
        var pairs = new (string Id, string Room)[]
        {
            ("cat", Power), ("dog", Power), ("fox", Storage), ("sheep", Storage), ("rabbit", Maint), ("wolf", Maint),
        };
        foreach (var p in pairs)
        {
            var st = _sim.GetEmployeeState(p.Id);
            st.AssignedRoomId = p.Room; st.CurrentRoomId = p.Room; st.Alive = true; st.Isolated = false;
            Log(LogEventType.Relocation, p.Id, p.Room, 0f, $"{Nm(p.Id)} → {RoomName(p.Room)} 배치");
        }
        // 아무도 없는 코어실에서 사고 — 질문의 기준 시각만 만든다.
        Log(LogEventType.TaskFailed, "", Core, At(40));
        GameState.Instance.AdvanceDayTime(At(60));

        Section("말투 비교 — 같은 질문, 여섯 명",
            "배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실. 22:40 코어실 설비 고장(아무도 없음).\n" +
            "같은 질문을 세 번씩 — 매번 다시 뽑힌다.");

        foreach (string id in Ids)
        {
            Sub($"{Nm(id)}");
            var session = new InterviewSession(id);
            var incident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
            if (incident == null) { _md.AppendLine("- (사고 자료 없음)"); continue; }
            foreach (var intent in new[] { InterviewIntent.AskWhereAtIncident, InterviewIntent.AskWhoWasPresent })
            {
                var q = InterviewQuestionFactory.Make(id, incident, intent);
                for (int i = 0; i < 3; i++) Answer(id, q.Text, InterviewReplyPlanner.Answer(q));
            }
            Answer(id, "당신을 의심하고 있습니다.", LocalDialogueGenerator.InterviewAnswer(id, DialogueQuestions.Accuse));
        }
        CheckVoiceMarks();
    }

    // 작성자가 지정한 말버릇이 실제 출력에 남는가(말투 비교 구간의 답변만 본다).
    private void CheckVoiceMarks()
    {
        var by = new Dictionary<string, List<string>>();
        foreach (var line in _md.ToString().Split('\n'))
        {
            var m = Regex.Match(line, @"^\*\*(.+?)\*\* .*");
            if (m.Success) { _lastSpeaker = m.Groups[1].Value; continue; }
            if (line.StartsWith("A: ") && _lastSpeaker != null)
            {
                if (!by.ContainsKey(_lastSpeaker)) by[_lastSpeaker] = new List<string>();
                by[_lastSpeaker].Add(line[3..].Trim());
            }
        }
        List<string> Of(string id) => by.GetValueOrDefault(Nm(id)) ?? new List<string>();
        Check(Of("sheep").All(DialogueVoiceTics.HasStutter), $"양 — 모든 답변에 더듬기가 있다 ({Of("sheep").Count(DialogueVoiceTics.HasStutter)}/{Of("sheep").Count})");
        Check(Of("fox").Count(a => a.Contains('~')) * 2 >= Of("fox").Count, $"여우 — 절반 이상의 답변에 '~' ({Of("fox").Count(a => a.Contains('~'))}/{Of("fox").Count})");
        Check(Of("rabbit").Count(a => a.Contains('!')) * 10 >= Of("rabbit").Count * 8, $"토끼 — 대부분의 답변에 '!' ({Of("rabbit").Count(a => a.Contains('!'))}/{Of("rabbit").Count})");
        Check(Of("wolf").All(a => !a.Contains('!') && !a.Contains('~')), "늑대 — 느낌표 · 물결 없음");
        Check(Of("wolf").All(a => a.Contains("니다")), "늑대 — 합쇼체");
        Check(Of("cat").All(a => !a.Contains('!')), "고양이 — 느낌표 없음");
    }

    private string _lastSpeaker;

    // ── 결번자가 거짓 알리바이를 대는 하루 ─────────────────────────────────
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

        Section($"장면 — 결번자 = {Nm(sab)}",
            $"{Nm(sab)}은/는 {RoomName(assigned)} 배치 → 22:30 몰래 {RoomName(real)} → 22:33 방해공작 → 22:38 복귀 → 22:42 {RoomName(real)} 고장.\n" +
            "아래 답변에서 결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.");

        GameState.Instance.AdvanceDayTime(At(90));
        CheckLieMemory(sab, assigned, real);
        var session = new InterviewSession(sab);
        var incident = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
        if (incident != null)
        {
            foreach (var intent in new[] { InterviewIntent.AskWhereAtIncident, InterviewIntent.AskBeforeIncident,
                         InterviewIntent.AskWhoWasPresent, InterviewIntent.AskIncidentKnown })
            {
                var q = InterviewQuestionFactory.Make(sab, incident, intent);
                string a = session.Ask(q).Answer;
                Answer(sab, q.Text, a, "[결번자]");
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
        foreach (var q in session.BasicQuestions())
            Answer(sab, q.Text, session.Ask(q).Answer, "[결번자]");
        Answer(sab, "당신을 의심하고 있습니다.", LocalDialogueGenerator.InterviewAnswer(sab, DialogueQuestions.Accuse), "[결번자]");

        // 같은 방 동료(정상)의 답과 나란히 본다.
        string mate = Ids.FirstOrDefault(id => id != sab && _sim.GetEmployeeState(id).AssignedRoomId == assigned);
        if (!string.IsNullOrEmpty(mate) && incident != null)
        {
            var ms = new InterviewSession(mate);
            var q = InterviewQuestionFactory.Make(mate, incident, InterviewIntent.AskWhoWasPresent);
            Answer(mate, q.Text, ms.Ask(q).Answer, "(같은 배치의 정상 직원)");
        }
    }

    // ── 한 직원에게 기본 질문 + 자료 질문 ─────────────────────────────────
    private void InterviewAll(string id, int maxEvidence)
    {
        Sub($"{Nm(id)} — 휴게시간 심문 (오늘의 기분: {_sim.GetEmployeeState(id).DailyMood})");
        var s = new InterviewSession(id);
        _md.AppendLine($"> {s.Greeting()}");
        _md.AppendLine();
        foreach (var q in s.BasicQuestions())
            Answer(id, q.Text, s.Ask(q).Answer);

        int used = 0;
        foreach (var ev in s.Board.Where(s.CanUse).ToList())
        {
            if (used >= maxEvidence) break;
            var qs = InterviewQuestionFactory.For(id, ev);
            if (qs.Count == 0) continue;
            used++;
            _md.AppendLine($"*자료: {ev.OneLine}*");
            _md.AppendLine();
            foreach (var q in qs.Take(2))
                Answer(id, q.Text, s.Ask(q).Answer);
        }
    }

    // ── 근무 기억 자체의 정확성 ─────────────────────────────────────────
    private void CheckMemoryOfNormalDay()
    {
        GD.Print("\n===== 근무 기억 =====");
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

    // ── 기계 검사 ─────────────────────────────────────────────────────
    private static readonly Regex Leak = new(@"[{}]|(은/는|이/가|을/를|으로/로|이에요/예요|이요/요|이었/였)");

    private void MachineChecks()
    {
        GD.Print("\n===== 기계 검사 =====");
        var leaks = _answers.Where(a => Leak.IsMatch(a)).ToList();
        foreach (var a in leaks.Take(5)) GD.Print("   누출: " + a);
        Check(leaks.Count == 0, $"변수·조사 표기가 새지 않는다 ({leaks.Count}건)");

        var empty = _answers.Where(a => string.IsNullOrWhiteSpace(a) || a == "…").ToList();
        Check(empty.Count == 0, $"빈 답변 없음 ({empty.Count}건)");

        var dup = _answers.Where(HasRepeatedSentence).ToList();
        foreach (var a in dup.Take(5)) GD.Print("   반복: " + a);
        Check(dup.Count == 0, $"한 답변 안에서 같은 말을 두 번 하지 않는다 ({dup.Count}건)");

        Check(_answers.Count >= 100, $"샘플 100개 이상 ({_answers.Count})");
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
    private void Section(string title, string setup)
    {
        _md.AppendLine($"## {title}");
        _md.AppendLine();
        foreach (string line in setup.Split('\n')) _md.AppendLine($"- {KoreanParticle.Resolve(line)}");
        _md.AppendLine();
        GD.Print($"\n===== {title} =====");
    }

    private void Sub(string title)
    {
        _md.AppendLine($"### {title}");
        _md.AppendLine();
    }

    private void Answer(string id, string question, string answer, string tag = "", string traceOverride = null)
    {
        _answers.Add(answer ?? "");
        string trace = traceOverride ?? DialogueComposer.LastTrace;
        _md.AppendLine($"**{Nm(id)}** {tag} — Q: {question}  ");
        _md.AppendLine($"A: {answer}  ");
        _md.AppendLine($"<sub>틀: {trace}</sub>");
        _md.AppendLine();
        GD.Print($"  [{Nm(id)}] {question}\n     → {answer}");
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
        var mates = Ids.Where(o => o != id && DialogueContextBuilder.RoomAt(o, 1, at) == to).ToArray();
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
        IEnumerable<string> witnesses = null)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = room,
            Description = desc ?? $"(샘플 {type} {room})",
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
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
