using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 플레이테스트에서 올라온 결함 7건이 실제로 고쳐졌는지 본다.
//
//   godot --headless --path . res://scenes/debug/QaFixTest.tscn --quit-after 120000
//
// 전부 "고쳤다"고 보고했다가 안 고쳐져 있던 적이 있는 항목들이다. 그래서 설명이 아니라
// 실행으로 확인한다 — 각 검사는 그 결함을 실제로 재현할 수 있는 상태를 만들고 측정한다.
//
//   ① 빨간 사고 수리 승인 요청은 **필요 인원이 도착한 뒤** 에 뜬다
//   ② 늑대는 반말을 쓰지 않는다
//   ③ 기절이 조사 자료가 되고, 의무실 이동 사유가 "실려 갔다"로 나온다
//   ④ 아직 깨어나지 않은 직원은 심문 대상이 아니다
//   ⑤ 근무가 끝나면 떠 있던 승인 요청·미로가 닫힌다
//   ⑥ 미로 한 칸 · 결과에 효과음이 붙는다(키가 실재하는지)
//   ⑦ 환기실 디버프는 **사고가 난 뒤에만** 걸린다
public partial class QaFixTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ QA 결함 7건 재검증 ################");
        TestApprovalWaitsForCrew();
        TestWolfSpeaksFormally();
        TestFaintEvidenceAndCarriedAnswer();
        TestFaintedCannotBeInterviewed();
        TestMazeClosesOnShiftEnd();
        TestMazeSoundsExist();
        TestVentDebuffNeedsAccident();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── ① 승인 요청은 인원이 도착한 뒤에 뜬다 ──────────────────────────
    //
    // 발전실은 수리에 2명이 필요하다. 1명만 보낸 동안에는 요청이 뜨면 안 되고,
    // 2명째가 들어선 뒤에 떠야 한다.
    private void TestApprovalWaitsForCrew()
    {
        Head("①", "수리 승인 요청은 필요 인원이 도착한 뒤에 뜬다");
        FreshShift();

        const string room = "power_room";
        var ids = _sim.GetActiveEmployeeIds();
        // 전원 저장고로 — 발전실은 비워 둔 채로 사고를 낸다(아무도 현장에 없는 상태).
        foreach (string id in ids) _sim.AssignToRoom(id, "storage_room");
        Settle();

        _sim.TriggerTutorialAccident(room, 2);
        Check(_sim.HasRepairPending(room), "발전실에 사고 수리가 걸렸다");
        Advance(2f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Idle,
            "아무도 안 보냈는데 요청이 뜨지 않는다");

        // 한 명만 보낸다 — 2명이 필요하므로 아직이다.
        _sim.AssignToRoom(ids[0], room);
        WaitArrive(ids[0], room);
        Advance(2f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Idle,
            $"1명만 도착했을 때도 뜨지 않는다 (현재 {_sim.OnDutyCount(room)}명)");

        // 두 번째가 도착하면 그제서야 뜬다.
        _sim.AssignToRoom(ids[1], room);
        WaitArrive(ids[1], room);
        Advance(1.5f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking,
            $"2명이 다 도착하면 요청이 뜬다 (현재 {_sim.OnDutyCount(room)}명)");
    }

    // ── ② 늑대 말투 ───────────────────────────────────────────────────
    //
    // 늑대는 DialogueVoiceProfiles 에서 격식체(Formal)로 등록돼 있다. 손으로 쓴 대사가
    // 거기서 벗어나면 "day1 부터 갑자기 반말" 로 보인다.
    private void TestWolfSpeaksFormally()
    {
        Head("②", "늑대는 반말을 쓰지 않는다");

        var bad = new List<string>();
        foreach (string path in new[] { "res://docs/NSP_DIALOGUE_RUNTIME.md", "res://docs/NSP_PROLOGUE_RUNTIME.md" })
            ScanWolfLines(path, bad);

        foreach (string line in bad) GD.Print($"      반말: {line}");
        Check(bad.Count == 0, $"대사 원본에 늑대 반말이 없다 ({bad.Count}건)");

        // 실제로 뽑히는 전화 대사도 본다 — 코드에 박힌 문장이 따로 있다.
        FreshShift();
        var ids = _sim.GetActiveEmployeeIds();
        if (!ids.Contains("wolf") || ids.Count < 2) { Check(false, "늑대와 동료가 근무 중이다"); return; }
        string victim = ids.First(x => x != "wolf");
        foreach (string id in ids) _sim.AssignToRoom(id, "storage_room");
        Settle();
        _sim.TriggerTutorialFaint(victim);
        // 쓰러지는 연출이 끝나고 동료가 다가와 상태를 살필 때까지 — 그래야 전화가 성립한다.
        for (int i = 0; i < 600 && _sim.Rescue?.FindUnmovedVictimIn("storage_room") == null; i++) Advance(Step);

        var call = LocalDialogueGenerator.BuildIncomingCall(
            "wolf", DialogueRepository.EventFaintTransportRequest, "storage_room");
        if (call == null) { Check(false, "늑대의 이송 요청 통화가 만들어진다"); return; }
        var said = new List<string> { call.Opening };
        said.AddRange(call.Choices.Select(c => c.Reply));
        var informal = said.Where(IsInformal).ToList();
        foreach (string s in informal) GD.Print($"      반말: {s}");
        Check(informal.Count == 0, $"늑대의 기절 이송 통화가 전부 존댓말이다 ({informal.Count}건 반말)");
    }

    // ── ③ 기절 조사 자료 · 의무실 이동 사유 ─────────────────────────────
    private void TestFaintEvidenceAndCarriedAnswer()
    {
        Head("③", "기절이 조사 자료가 되고, 의무실 이동 사유가 '실려 갔다'가 된다");
        FreshShift();

        var ids = _sim.GetActiveEmployeeIds();
        string victim = ids[0];
        foreach (string id in ids) _sim.AssignToRoom(id, "storage_room");
        Settle();

        _sim.TriggerTutorialFaint(victim);
        Check(_sim.GetEmployeeState(victim).Incapacitated, $"{Name(victim)} 이/가 기절했다");
        // 동료가 다가와 관리자에게 전화를 건다. 이 검사에는 사람이 없으니 "받지 않았다" 로 둔다 —
        // 그러면 구조자가 알아서 옮긴다(FaintRescueSystem.NoAnswer).
        float t = 0f;
        while (t < 20f && _sim.GetEmployeeState(victim).Faint != FaintPhase.AwaitingDecision)
        { Advance(Step); t += Step; }
        _sim.Rescue?.NoAnswer(victim);
        while (t < 120f && _sim.GetEmployeeState(victim).CurrentRoomId != FacilitySimulation.MedicalRoomIdPublic)
        { Advance(Step); t += Step; }
        Check(_sim.GetEmployeeState(victim).CurrentRoomId == FacilitySimulation.MedicalRoomIdPublic,
            $"{Name(victim)} 이/가 의무실로 옮겨졌다 ({t:0.0}초)");

        // 관리자가 의무실 CCTV 를 봤다 — 누워 있는 그를 화면으로 확인한 것이다.
        // 이게 있어야 "의무실에 왜 있었습니까" 를 물을 자료가 생긴다.
        PlayerKnownEvidence.RecordCctvObservation(
            FacilitySimulation.MedicalRoomIdPublic, GameState.Instance.DayTimeSeconds, new[] { victim });

        var board = InterviewEvidenceBoard.Build(victim);
        var faint = board.FirstOrDefault(e => e.Id.StartsWith("faint:"));
        Check(faint != null, "조사 자료에 기절 기록이 들어간다");
        if (faint != null) GD.Print($"      → [{faint.Header}] {faint.Body}");

        // 의무실에 관한 질문이 실제로 어떻게 답하는지 본다.
        //   · "그 뒤에 어디로 갔습니까"      ← 기절 기록에 붙는 꼬리질문
        //   · "의무실에 왜 있었습니까"       ← 의무실 CCTV 자료
        // 기절해서 실려 간 사람이 제 발로 간 것처럼 답하면 안 된다.
        var asked = new List<(string Q, string A)>();
        void Ask(InterviewEvidence ev, InterviewIntent intent)
        {
            if (ev == null) return;
            var iq = InterviewQuestionFactory.Make(victim, ev, intent);
            if (iq != null) asked.Add((iq.Text, InterviewReplyPlanner.Answer(iq)));
        }
        Ask(faint, InterviewIntent.AskNextLocation);
        var medCctv = board.FirstOrDefault(e => e.Kind == EvidenceKind.Cctv
                                                && e.SubjectRoomId == FacilitySimulation.MedicalRoomIdPublic);
        Check(medCctv != null, "의무실 CCTV 가 조사 자료가 된다");
        Ask(medCctv, InterviewIntent.AskPresenceReason);

        foreach (var (qt, at) in asked) GD.Print($"      Q: {qt}\n      A: {at}");
        Check(asked.Count > 0, $"의무실에 대해 물을 수 있는 질문이 있다 ({asked.Count}개)");

        // 제 발로 점검하러 간 것처럼 말하는 답이 하나라도 있으면 실패다.
        var pretend = asked.Where(x => x.A.Contains("상태를 확인") || x.A.Contains("상태 보")
                                       || x.A.Contains("들렀") || x.A.Contains("보러")
                                       || x.A.Contains("이동했습니다")).ToList();
        foreach (var (_, at) in pretend) GD.Print($"      ✗ {at}");
        Check(pretend.Count == 0, $"'점검하러 갔다' 식의 답이 없다 ({pretend.Count}건)");

        // 의무실 관련 질문은 하나도 빠짐없이 "실려 갔다"를 말해야 한다 —
        // 하나라도 제 발로 간 것처럼 답하면 그 한 줄이 그대로 거짓 알리바이가 된다.
        bool Carried(string a) => a.Contains("쓰러") || a.Contains("실려")
                                  || a.Contains("옮겨") || a.Contains("의식");
        Check(asked.All(x => Carried(x.A)), "의무실에 관한 답이 전부 '실려 갔다'를 말한다");
    }

    // ── ④ 기절 중에는 심문할 수 없다 ────────────────────────────────────
    private void TestFaintedCannotBeInterviewed()
    {
        Head("④", "회복되지 않은 기절 직원은 심문 대상이 아니다");
        FreshShift();

        var ids = _sim.GetActiveEmployeeIds();
        string victim = ids[0];
        foreach (string id in ids) _sim.AssignToRoom(id, "storage_room");
        Settle();
        _sim.TriggerTutorialFaint(victim);
        Advance(1f);

        GameState.Instance.SetPhase(GamePhase.Rest);
        var st = _sim.GetEmployeeState(victim);
        // Phone3D 의 휴게시간 통화 조건과 **같은 식** 이다.
        bool callable = st is { Alive: true, Isolated: false, Incapacitated: false };
        Check(!callable, $"{Name(victim)} 은/는 기절 상태라 전화가 가지 않는다");

        string other = ids.First(x => x != victim);
        var ost = _sim.GetEmployeeState(other);
        Check(ost is { Alive: true, Isolated: false, Incapacitated: false },
            $"{Name(other)} 은/는 평소대로 심문할 수 있다");

        // 기절은 근무가 바뀌면 풀린다 — 다음 날까지 심문 불가로 남으면 안 된다.
        _sim.ResetForNewShift();
        Check(_sim.GetEmployeeState(victim)?.Incapacitated == false, "새 근무가 시작되면 기절이 풀린다");
    }

    // ── ⑤ 근무 종료 시 미로가 닫힌다 ────────────────────────────────────
    private void TestMazeClosesOnShiftEnd()
    {
        Head("⑤", "미로를 푸는 중에 근무가 끝나도 패드가 멈추지 않는다");
        RepairApprovalSystem.ResetAll();

        var task = new SpawnedTask { RoomId = "core_room", IsRepair = true, GaugeRequired = 10f };
        RepairApprovalSystem.Enqueue("core_room", "코어실", task);
        RepairApprovalSystem.Tick(Step);
        RepairApprovalSystem.Approve();
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Maze, "미로가 떠 있다");

        // 여기서 근무가 끝난다 — 미로를 풀지도, 틀리지도 않은 채로.
        RepairApprovalSystem.AbortForShiftEnd();
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Idle, "근무 종료와 함께 닫힌다");
        Check(!RepairApprovalSystem.Busy, "패드 화면이 더는 미로를 그리지 않는다");
        Check(Mathf.Abs(task.GaugeRequired - 10f) < 0.01f, "끝난 근무의 수리에 벌점을 매기지 않는다");

        // 줄에 서 있던 다음 요청도 같이 비워야 한다 — 안 그러면 다음 근무 첫 틱에 튀어나온다.
        RepairApprovalSystem.ResetAll();
        RepairApprovalSystem.Enqueue("core_room", "코어실", new SpawnedTask { RoomId = "core_room", IsRepair = true });
        RepairApprovalSystem.Enqueue("power_room", "발전실", new SpawnedTask { RoomId = "power_room", IsRepair = true });
        RepairApprovalSystem.Tick(Step);
        RepairApprovalSystem.AbortForShiftEnd();
        Check(RepairApprovalSystem.Pending == 0, "줄에서 기다리던 요청도 같이 사라진다");
    }

    // ── ⑥ 미로 효과음 ─────────────────────────────────────────────────
    //
    // 소리 자체는 헤드리스에서 들을 수 없다. 대신 코드가 부르는 키가 실재하는지 본다 —
    // 없는 키를 부르면 Sfx.Play 가 조용히 아무것도 하지 않아 "효과음이 안 난다"가 된다.
    private void TestMazeSoundsExist()
    {
        Head("⑥", "미로 · 승인 버튼 효과음 키가 실재한다");
        foreach (string key in new[] { "tick", "task_done", "switch_fail", "power_down", "relay_click", "switch" })
            Check(Sfx.Instance?.Has(key) == true, $"효과음 '{key}' 를 찾을 수 있다");
    }

    // ── ⑦ 환기실 디버프는 사고 뒤에만 ──────────────────────────────────
    //
    // 근무 중에는 누구나 가만히 있어도 스트레스가 조금씩 오른다(ShiftStressAmount).
    // 그래서 "안 올랐는가" 가 아니라 **환기실을 비운 판과 채운 판이 같은가** 를 본다.
    private void TestVentDebuffNeedsAccident()
    {
        Head("⑦", "환기실을 비웠다는 이유만으로 스트레스가 오르지 않는다");

        float empty = StressOver30s(ventStaffed: false);
        float staffed = StressOver30s(ventStaffed: true);
        Check(!GameState.Instance.VentilationDown, "30초 안에는 아직 환기 고장이 나지 않는다");
        GD.Print($"      환기실 비움 +{empty:0.00}  /  환기실 배치 +{staffed:0.00}");
        // 예전 동작이 살아 있으면 30초에 VentUnstaffedStressAmount(1.5) 가 세 번 들어가 +4.5 차이가 난다.
        float oldTick = Config.Instance.Data.VentUnstaffedStressAmount;
        Check(empty - staffed < oldTick,
            $"비운 판이 더 오르지 않는다 (차이 {empty - staffed:+0.00;-0.00} · 옛 동작이면 +{oldTick * 3f:0.0})");

        // 사고를 내면 그때부터 오른다 — 디버프 자체가 사라진 것은 아니어야 한다.
        var ids = _sim.GetActiveEmployeeIds();
        _sim.TriggerTutorialAccident("vent_room");
        Check(GameState.Instance.VentilationDown, "환기 고장이 걸렸다");
        var mid = ids.ToDictionary(x => x, x => _sim.GetEmployeeState(x).Stress);
        float faultSpan = Config.Instance.Data.VentFaultStressIntervalSeconds * 2f + 1f;
        Advance(faultSpan);
        float after = ids.Max(x => _sim.GetEmployeeState(x).Stress - mid[x]);
        // 같은 시간 동안의 평소 상승분보다 확실히 커야 "환기 때문" 이라고 말할 수 있다.
        float baseline = staffed * faultSpan / 30f;
        Check(after > baseline + 1f,
            $"사고가 난 뒤에는 실제로 오른다 (+{after:0.00} · 평소 +{baseline:0.00})");
    }

    // 30초 동안 오른 최대 스트레스. 환기실만 비우거나 채우고 나머지 조건은 같게 맞춘다.
    private float StressOver30s(bool ventStaffed)
    {
        FreshShift();
        var ids = _sim.GetActiveEmployeeIds();
        // 작업실 5개는 두 판 모두 똑같이 채운다 — 빈 작업실에서 사고가 나면 "사고 현장"
        // 스트레스가 섞여 들어와 환기 탓인지 가릴 수 없다. 남는 한 명만 환기실 또는 의무실로
        // 보내, 두 판의 차이가 **환기실에 사람이 있는가** 하나만 되게 한다.
        string[] ops = { "power_room", "core_room", "maintenance_room", "guard_room", "storage_room" };
        for (int i = 0; i < ops.Length && i < ids.Count; i++) _sim.AssignToRoom(ids[i], ops[i]);
        for (int i = ops.Length; i < ids.Count; i++)
            _sim.AssignToRoom(ids[i], ventStaffed ? "vent_room" : FacilitySimulation.MedicalRoomIdPublic);
        Settle();
        Check(_sim.OnDutyCount("vent_room") == (ventStaffed ? 1 : 0),
            ventStaffed ? "환기실에 1명을 넣었다" : "환기실이 비어 있다");

        var before = ids.ToDictionary(x => x, x => _sim.GetEmployeeState(x).Stress);
        Advance(30f);
        return ids.Max(x => _sim.GetEmployeeState(x).Stress - before[x]);
    }

    // ── 공통 ──────────────────────────────────────────────────────────

    private void FreshShift()
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    // 배치 직후 전원이 제자리에 설 때까지. 게임 시간은 흘리지 않는다.
    private void Settle()
    {
        var ids = _sim.GetActiveEmployeeIds();
        for (int i = 0; i < 4000 && ids.Any(x => _sim.GetEmployeeState(x).IsMoving); i++) _sim.Tick(Step);
    }

    private void WaitArrive(string employeeId, string roomId)
    {
        for (int i = 0; i < 4000; i++)
        {
            var st = _sim.GetEmployeeState(employeeId);
            if (st != null && !st.IsMoving && st.CurrentRoomId == roomId) return;
            Advance(Step);
        }
    }

    private void Advance(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
        }
    }

    // 늑대 블록의 대사 줄을 모아 반말을 골라낸다.
    private static void ScanWolfLines(string path, List<string> bad)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        bool inWolf = false;
        while (!f.EofReached())
        {
            string line = f.GetLine().Trim();
            if (line.StartsWith("@char ")) inWolf = line["@char ".Length..].Trim() == "wolf";
            if (!inWolf) continue;
            foreach (string tag in new[] { "opening:", "reply:", "greeting:", "a:" })
            {
                if (!line.StartsWith(tag)) continue;
                string said = line[tag.Length..].Trim();
                if (IsInformal(said)) bad.Add(said);
            }
        }
    }

    // 문장 끝이 반말인가. 늑대는 전부 '…습니다 / …십시오' 로 끝난다.
    private static bool IsInformal(string said)
    {
        if (string.IsNullOrWhiteSpace(said)) return false;
        foreach (string s in said.Split('.', '!', '?'))
        {
            string t = s.Trim().TrimEnd('…', ' ');
            if (t.Length < 2) continue;
            foreach (string end in new[] { "했다", "한다", "있다", "없다", "겠다", "간다", "온다", "지", "야", "라", "자", "네", "군" })
                if (t.EndsWith(end) && !t.EndsWith("니다") && !t.EndsWith("세요") && !t.EndsWith("시오"))
                    return true;
        }
        return false;
    }

    private static new string Name(string employeeId) =>
        FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
