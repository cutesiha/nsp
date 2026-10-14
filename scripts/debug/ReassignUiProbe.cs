using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 임시 탐침 — **직원이 죽은 뒤** 근무 중 지도와 이튿날 배치 화면이 어떻게 반응하는가.
//
//   godot --path . scenes/debug/ReassignUiProbe.tscn -- <저장 폴더>
//
// 보는 것 넷.
//   ① 근무 중 지도에서 죽은 직원을 끌 수 없다(그리고 왜 안 되는지 말한다).
//   ② 산 직원은 사고 난 방으로 그대로 보낼 수 있다(기존 조작이 멀쩡한가).
//   ③ 이튿날 배치 지도에서 죽은 직원은 집히지 않고 거부 이유가 뜬다.
//   ④ 모니터2(신원 화면)에 사망 표시가 뜬다 — 그림으로 확인한다.
public partial class ReassignUiProbe : Node
{
    private string _dir = "";
    private int _pass, _fail;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    { for (int i = 0; i < n; i++) await Frame(); }

    private async System.Threading.Tasks.Task Seconds(double s)
    {
        double t = 0;
        while (t < s) { await Frame(); t += GetProcessDeltaTime(); }
    }

    private void Check(bool ok, string what)
    {
        if (ok) { _pass++; GD.Print($"  PASS  {what}"); }
        else { _fail++; GD.PrintErr($"  FAIL  {what}"); }
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance?.GoToNextDay();                  // DAY2 — 여기서부터 사람이 죽는다
        DebugEntryPoint.Call(flow, "ClearAllAssignments");
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Frames(10);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1800 && DebugEntryPoint.Stage(flow) != "Shift"; i++) await Frame();
        await Seconds(5);

        var ctl = ControlRoom3DController.Instance;
        var sim = FacilitySimulation.Instance;
        GD.Print($"\n################ DAY{GameState.Instance?.CurrentDay} 근무 중 ################");

        var map = FindNode<FacilityMonitorView>(ctl.FacilityViewport)?.MinimapForTest;
        if (map == null) { GD.PrintErr("미니맵을 찾지 못했다"); GetTree().Quit(1); return; }

        // 사고를 하나 내고, 그 방 사람을 죽인다(실제 살인 경로 — CommitCovertKill).
        const string acc = "power_room";
        sim.TriggerWarningFailure(acc, "발전기 점검");
        await Seconds(1);
        string victim = sim.GetRoomState(acc)?.OccupantEmployeeIds.FirstOrDefault() ?? "";
        if (victim.Length == 0)
        {
            victim = sim.GetEmployeeIds().First(id => sim.GetEmployeeState(id) is { Alive: true });
            sim.AssignToRoom(victim, acc);
            await Seconds(22);
        }
        bool killed = sim.CommitCovertKill(victim, sim.GetEmployeeState(victim).CurrentRoomId);
        await Seconds(1);
        var vst = sim.GetEmployeeState(victim);
        GD.Print($"[사망] {victim} 살해={killed} 생존={vst?.Alive} 발견={vst?.DiscoveredDead} 방={vst?.CurrentRoomId}");
        Check(killed && vst?.Alive == false, "DAY2 에서 직원이 실제로 사망한다");

        // ① 죽은 직원을 끌어 본다.
        int popupsBefore = PopupCount(map);
        await Drag(ctl, map, victim, "guard_room");
        vst = sim.GetEmployeeState(victim);
        Check(vst?.AssignedRoomId != "guard_room", "① 죽은 직원은 끌어다 놓아도 재배치되지 않는다");
        Check(PopupCount(map) > popupsBefore, "① 거부 이유가 지도에 뜬다(조용히 실패하지 않는다)");

        // ② 산 직원은 사고 난 방으로 그대로 보낼 수 있다.
        string helper = sim.GetEmployeeIds().FirstOrDefault(id =>
            sim.GetEmployeeState(id) is { Alive: true, Isolated: false, Incapacitated: false }
            && sim.GetEmployeeState(id).CurrentRoomId != acc
            && !string.IsNullOrEmpty(sim.GetEmployeeState(id).AssignedRoomId));
        await Drag(ctl, map, helper, acc);
        await Seconds(22);
        Check(sim.GetEmployeeState(helper)?.CurrentRoomId == acc, "② 산 직원은 사고 난 방으로 이동한다");
        Shot(ctl.FacilityViewport, "shift_map_dead");

        // ── 이튿날 배치 ───────────────────────────────────────────────
        GameState.Instance?.GoToNextDay();
        DebugEntryPoint.Call(flow, "ClearAllAssignments");
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(2);
        GD.Print($"\n################ DAY{GameState.Instance?.CurrentDay} 근무 배치 ################");

        var smap = ScheduleMapView.Instance;
        Check(smap != null, "배치 지도가 떠 있다");
        if (smap != null)
        {
            // 죽은 직원의 대기 인원 카드를 누른다.
            var card = smap.RosterCardOf(victim);
            if (card.Size.X > 1f)
            {
                var xf = smap.GetGlobalTransformWithCanvas();
                Vector2 at = xf * card.GetCenter();
                var vp = ctl.ScheduleMapViewport;
                vp.PushInput(new InputEventMouseButton
                { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }, true);
                await Frames(2);
                string refusal = Field<string>(smap, "_refusal");
                string drag = Field<string>(smap, "_dragEmp");
                GD.Print($"  죽은 직원 카드 누름 → 집은직원='{drag}' 거부='{refusal}'");
                Check(string.IsNullOrEmpty(drag), "③ 죽은 직원은 배치 화면에서 집히지 않는다");
                Check(!string.IsNullOrEmpty(refusal), "③ 거부 이유가 상태 줄에 뜬다");
                vp.PushInput(new InputEventMouseButton
                { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
                await Frames(2);
            }
            else Check(false, "죽은 직원의 대기 인원 카드를 찾았다");

            // 그래도 코드로 직접 배치를 시도하면 거부돼야 한다.
            Check(!sim.AssignToRoom(victim, "core_room"), "③ 사망자는 배치 자체가 거부된다");
        }
        await Seconds(1.2);
        Shot(ctl.ScheduleStaffViewport, "schedule_staff_dead");
        Shot(ctl.ScheduleMapViewport, "schedule_map_dead");

        // ④ 모니터2 에서 죽은 직원 블록을 눌러 상세를 연다.
        var staff = FindNode<ScheduleStaffView>(ctl.ScheduleStaffViewport);
        var cards = Field<System.Collections.Generic.List<(Rect2 Rect, string Id)>>(staff, "_cards");
        var dead = cards?.FirstOrDefault(c => c.Id == victim) ?? default;
        if (staff != null && dead.Id == victim)
        {
            var xf2 = staff.GetGlobalTransformWithCanvas();
            Vector2 at2 = xf2 * dead.Rect.GetCenter();
            var svp = ctl.ScheduleStaffViewport;
            svp.PushInput(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Pressed = true, Position = at2, GlobalPosition = at2 }, true);
            await Frames(2);
            svp.PushInput(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Pressed = false, Position = at2, GlobalPosition = at2 }, true);
            await Seconds(1.0);
            Check(Field<string>(staff, "_detailEmp") == victim, "④ 모니터2 에서 사망자 상세가 열린다");
            Shot(ctl.ScheduleStaffViewport, "schedule_staff_detail_dead");
        }
        else Check(false, "④ 모니터2 의 사망자 블록을 찾았다");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Drag(
        ControlRoom3DController ctl, FacilityMinimap map, string employeeId, string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var icon = (Vector2)typeof(FacilityMinimap)
            .GetMethod("IconPos", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(map, new object[] { sim, employeeId });
        var center = (Vector2)typeof(FacilityMinimap)
            .GetMethod("CenterOf", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Invoke(map, new object[] { roomId });

        var xf = map.GetGlobalTransformWithCanvas();
        Vector2 from = xf * icon, to = xf * center;
        var vp = ctl.FacilityViewport;
        vp.PushInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from }, true);
        await Frames(2);
        for (int i = 1; i <= 6; i++)
        {
            Vector2 p = from.Lerp(to, i / 6f);
            vp.PushInput(new InputEventMouseMotion
            { Position = p, GlobalPosition = p, Relative = (to - from) / 6f, ButtonMask = MouseButtonMask.Left }, true);
            await Frame();
        }
        vp.PushInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to }, true);
        await Frames(2);
        var st = sim.GetEmployeeState(employeeId);
        GD.Print($"  끌기 {employeeId} → {roomId} : 배치방={st?.AssignedRoomId} 이동중={st?.IsMoving}");
    }

    private static int PopupCount(FacilityMinimap map)
    {
        var f = typeof(FacilityMinimap).GetField("_popups", BindingFlags.NonPublic | BindingFlags.Instance);
        return f?.GetValue(map) is System.Collections.ICollection c ? c.Count : 0;
    }

    private void Shot(SubViewport vp, string name)
    {
        var img = vp?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"  (그림 없음: {name})"); return; }
        string path = $"{_dir}/{name}.png";
        img.SavePng(path);
        GD.Print($"  그림 저장: {path}");
    }

    private static T Field<T>(object o, string name)
    {
        var f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) return default;
        var v = f.GetValue(o);
        return v is T t ? t : default;
    }

    private static T FindNode<T>(Node root) where T : Node
    {
        if (root == null) return null;
        if (root is T hit) return hit;
        foreach (var child in root.GetChildren())
            if (FindNode<T>(child) is T found) return found;
        return null;
    }
}
