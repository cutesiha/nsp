using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 다층 공포 이벤트 자동 검증 (지시서 §6).
//
//   godot --headless --path . res://scenes/debug/HorrorEventTest.tscn --quit-after 14000
//
// 보는 것은 **연출이 판정을 흉내 내지 않는가** 다.
//   ① 순수 공포 이벤트가 코어 · 직원 · 사고 · 증거를 몰래 바꾸지 않는가
//   ② 진짜 위협(복도 괴물)이 돌고 있을 때 연출이 끼어들지 않는가
//   ③ 전화 · 스토리 · 수리 미로처럼 입력을 쥐는 순간에 비켜서는가
//   ④ 강한 연출이 연달아 두 번 터지지 않는가
//   ⑤ 비정상 전화가 통화 기록 · 증거 · 도전과제 집계에 남지 않는가
//   ⑥ DAY0(가상 시뮬레이션)에는 하나도 나오지 않는가
public partial class HorrorEventTest : Node
{
    private int _pass, _fail;
    private FacilitySimulation _sim;
    private HorrorEventDirector _dir;

    public override void _Ready()
    {
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(0.8);

        _sim = FacilitySimulation.Instance;
        _dir = HorrorEventDirector.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 3);
        GameModes.ForceSelect(GameMode.Competition);
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(0.8);

        var ids = _sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) _sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(1.2);

        GD.Print("\n\n################ 다층 공포 이벤트 검증 ################");
        // 자동 발동을 끄고 전부 손으로 돌린다 — 그래야 매번 같은 결과가 나온다.
        _dir.AutoDisabled = true;
        _dir.ResetShift();

        SectionA();
        await SectionB();
        await SectionC();
        await SectionD();
        SectionE();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 구성 ─────────────────────────────────────────────────────

    private void SectionA()
    {
        Head("A", "이벤트 표");
        var kinds = HorrorEventDirector.AllKinds.ToList();
        Ok(kinds.Count == 7, $"이벤트 {kinds.Count}종");
        Ok(kinds.Contains(HorrorEventDirector.Kind.CctvScare), "CCTV 점프스케어가 있다");
        Ok(kinds.Contains(HorrorEventDirector.Kind.CallGhost)
           && kinds.Contains(HorrorEventDirector.Kind.CallSilent)
           && kinds.Contains(HorrorEventDirector.Kind.CallWarp), "전화 3종이 있다");
        Ok(kinds.Count(HorrorEventDirector.IsStrong) == 2, "강한 연출은 둘뿐이다");

        // 하루에 같은 그림을 두 번 쓰지 않는다(§6-4 "한 영상 이미지를 재탕하지 않는다").
        Ok(HorrorEventDirector.MaxPerShiftOf(HorrorEventDirector.Kind.CctvScare) == 1,
            "CCTV 점프스케어는 한 근무에 한 번뿐이다");
        Ok(HorrorEventDirector.FromDayOf(HorrorEventDirector.Kind.CctvScare) >= 2,
            "첫날에는 강한 점프스케어가 없다");
        foreach (var k in kinds)
            GD.Print($"      {k,-12} DAY{HorrorEventDirector.FromDayOf(k)}~  " +
                     $"하루 {HorrorEventDirector.MaxPerShiftOf(k)}회  " +
                     $"{(HorrorEventDirector.IsStrong(k) ? "강함" : "")}");
    }

    // ── B. 차단 규칙 ────────────────────────────────────────────────

    private async System.Threading.Tasks.Task SectionB()
    {
        Head("B", "비켜서야 할 때");
        Ok(_dir.Safe(), "평소 근무 중에는 발동 가능하다");

        // 진짜 위협이 돌고 있으면 전부 입을 닫는다.
        var t = _sim.Threats.ForceSpawn("absentee", "corridor_north");
        Ok(t != null, "복도에 괴물을 띄웠다");
        Ok(!_dir.Safe(), "**괴물이 접근하는 동안에는 연출이 끼어들지 않는다**");
        _sim.Threats.Reset();
        await Seconds(0.2);
        Ok(_dir.Safe(), "위협이 끝나면 다시 가능하다");

        // 근무가 아니면 발동하지 않는다.
        var phase = GameState.Instance.CurrentPhase;
        GameState.Instance.SetPhase(GamePhase.Rest);
        Ok(!_dir.Safe(), "휴게시간에는 발동하지 않는다");
        GameState.Instance.SetPhase(phase);
        Ok(_dir.Safe(), "근무로 돌아오면 다시 가능하다");
    }

    // ── C. CCTV 점프스케어 ──────────────────────────────────────────

    private async System.Threading.Tasks.Task SectionC()
    {
        Head("C", "CCTV 점프스케어는 죽이지 않는다");

        float coreBefore = GameState.Instance.CoreProgress;
        int dayBefore = GameState.Instance.CurrentDay;
        int aliveBefore = _sim.GetEmployeeIds().Count(id => _sim.GetEmployeeState(id)?.Alive == true);
        int logBefore = EventLog.Instance?.GetAllEntries().Count ?? 0;
        var scareWords = MonsterThreatSystem.Monsters.Select(m => m.DisplayName)
            .Concat(new[] { "괴물", "침입", "점프" }).ToList();

        bool fired = _dir.ForceFire(HorrorEventDirector.Kind.CctvScare);
        Ok(fired, "점프스케어를 돌렸다");

        // 떠 있는 동안.
        await Seconds(0.18);
        var world = FacilityCctvWorld.Instance;
        Ok(world?.ScareForTest != null, "전용 배우가 만들어졌다(복도 개체와 별개)");
        Ok(_sim.Threats.Current == null, "**위협은 하나도 생기지 않았다**");

        // 끝날 때까지.
        await Until(() => !_dir.BusyForTest, 8000);
        Ok(!_dir.BusyForTest, "연출이 끝났다");
        Ok(world?.ScareForTest?.Root?.Visible != true, "끝난 뒤에는 화면에서 사라진다");

        Ok(Mathf.IsEqualApprox(GameState.Instance.CoreProgress, coreBefore)
           || Mathf.Abs(GameState.Instance.CoreProgress - coreBefore) < 5f, "코어가 연출 때문에 변하지 않았다");
        Ok(GameState.Instance.CurrentDay == dayBefore, "날짜가 그대로다");
        Ok(_sim.GetEmployeeIds().Count(id => _sim.GetEmployeeState(id)?.Alive == true) == aliveBefore,
            "직원이 다치지 않았다");
        // 연출이 도는 동안에도 평소 근무 기록(업무 발생 등)은 계속 쌓인다.
        // 그중 **공포 연출을 가리키는 줄이 하나도 없어야** 한다.
        var added = (EventLog.Instance?.GetAllEntries() ?? new System.Collections.Generic.List<LogEntry>())
            .Skip(logBefore).ToList();
        var leaked = added.Where(e => scareWords.Any(w => (e.Description ?? "").Contains(w))).ToList();
        foreach (var e in leaked) GD.Print($"      샜다: {e.Description}");
        Ok(leaked.Count == 0, $"시설 로그에 공포 연출이 한 줄도 남지 않았다(평소 기록 +{added.Count}건)");
        Ok(_dir.FiredCountForTest(HorrorEventDirector.Kind.CctvScare) == 1, "하루 집계에 한 번으로 올랐다");

        // §6-5 상호 배제 — 큰 것 뒤에는 다른 공포 계통도 함께 입을 닫는다.
        Ok(HorrorEventDirector.QuietNow, "강한 연출 뒤에 전역 정숙 구간이 걸린다");
        Ok(!PresenceDirector.Allowed(), "**그동안 무해한 형체 연출도 비켜선다**");
        Ok((HorrorAudioDirector.Instance?.BlockedReason() ?? "").Length > 0, "랜덤 공포음도 비켜선다");
        Ok(!_dir.Safe(), "자기 자신도 연달아 터지지 않는다");
        HorrorEventDirector.ClearHushForTest();
        Ok(PresenceDirector.Allowed(), "정숙 구간이 풀리면 원래대로 돌아온다");
        Ok(_dir.PickForTest() != HorrorEventDirector.Kind.CctvScare, "같은 근무에 두 번 뽑히지 않는다");
    }

    // ── D. 비정상 전화 ──────────────────────────────────────────────

    private async System.Threading.Tasks.Task SectionD()
    {
        Head("D", "받으면 안 되는 전화");
        var phone = Phone3D.Instance;
        if (phone == null) { Ok(false, "Phone3D 를 찾지 못했다"); return; }

        int historyBefore = DialogueHistory.Instance?.GetAllEntries().Count ?? 0;

        Ok(_dir.ForceFire(HorrorEventDirector.Kind.CallSilent), "무응답 전화를 울렸다");
        await Seconds(0.3);
        Ok(phone.IsRinging, "벨이 울린다");
        Ok(phone.AnomalyActive, "발신자가 없는 전화다");
        Ok(!_dir.Safe(), "전화가 울리는 동안에는 다른 연출이 끼어들지 않는다");

        // 받지 않으면 저 혼자 끊는다. 직원 전화와 달리 큐에 남지 않는다.
        await Until(() => !phone.IsBusy, 12000);
        Ok(!phone.IsBusy, "받지 않으면 스스로 끊는다");
        Ok(!phone.AnomalyActive, "끊은 뒤 전화기가 완전히 비었다");

        // 받았을 때의 연출 — 수화기를 드는 손 동작 없이 연출만 직접 돌린다.
        var acd = AnomalyCallDirector.Instance;
        Ok(acd != null, "전용 연출기가 있다");
        if (acd != null)
        {
            Ok(phone.RingAnomaly(AnomalyCallDirector.KindGhost), "발신자 미상 전화를 울렸다");
            typeof(Phone3D).GetMethod("PickUp", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(phone, null);
            await Until(() => acd.Running, 3000);
            Ok(acd.Running, "받으면 전용 연출이 돈다(대사 엔진을 타지 않는다)");
            Ok(PhoneCallHud.Instance?.IsOpen != true, "**통화 UI 가 열리지 않는다**");

            await Until(() => !acd.Running, 20000);
            Ok(!acd.Running, "연출이 스스로 끝난다");
            await Until(() => !phone.IsBusy, 6000);
            Ok(!phone.IsBusy, "끝나면 전화기가 정상으로 돌아온다(진짜 전화를 막지 않는다)");
        }

        int historyAfter = DialogueHistory.Instance?.GetAllEntries().Count ?? 0;
        Ok(historyAfter == historyBefore, "**통화 기록에 한 줄도 남지 않았다**");
    }

    // ── E. 통화 신호 왜곡 ───────────────────────────────────────────

    private void SectionE()
    {
        Head("E", "신호 왜곡은 소리만 바꾼다");
        Ok(Mathf.IsZeroApprox(Sfx.Instance?.VoiceWarp ?? 0f), "평소에는 왜곡이 없다");
        Ok(Mathf.IsZeroApprox(_dir.WarpForTest), "통화가 없으면 올라가지 않는다");
        _dir.ForceFire(HorrorEventDirector.Kind.CallWarp);
        Ok(Mathf.IsZeroApprox(_dir.WarpForTest), "통화 중이 아니면 걸어 놔도 0 이다");
        _dir.ResetShift();
        Ok(Mathf.IsZeroApprox(Sfx.Instance?.VoiceWarp ?? 1f), "근무가 바뀌면 깨끗이 지워진다");
    }

    // ── 보조 ────────────────────────────────────────────────────────

    private static void Head(string tag, string what) => GD.Print($"\n── {tag}. {what}");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    // 헤드리스는 프레임이 제멋대로 빨라서 "몇 프레임" 으로는 기다릴 수 없다 — 실제 시계로 센다.
    private async System.Threading.Tasks.Task Until(System.Func<bool> done, double timeoutMs)
    {
        double deadline = Time.GetTicksMsec() + timeoutMs;
        while (!done() && Time.GetTicksMsec() < deadline) await Frame();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
