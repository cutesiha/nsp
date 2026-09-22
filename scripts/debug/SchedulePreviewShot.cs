using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// Phase 0 화면 캡처(레이아웃 확인용). 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/SchedulePreviewShot.tscn -- <저장 폴더>
//
// 배치 콘솔 두 화면(직원 몇 명을 배치해 둔 상태)과 환경 설정 창을 PNG 로 저장하고 종료한다.
public partial class SchedulePreviewShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        var sim = FacilitySimulation.Instance;
        GameState.Instance.ResetRun(1);
        sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        sim.RollDailyMoods();
        sim.AssignToRoom("rabbit", "maintenance_room");
        sim.AssignToRoom("cat", "maintenance_room");
        sim.AssignToRoom("wolf", "core_room");
        sim.AssignToRoom("dog", "guard_room");

        var mapVp = Vp(out var map, new ScheduleMapView());
        var staffVp = Vp(out _, new ScheduleStaffView());
        await Frames(4);
        Save(mapVp, dir, "schedule_map.png");
        Save(staffVp, dir, "schedule_staff_grid.png");

        // 직원 선택 / 작업실 선택 상태는 입력으로 만든다.
        var m = (ScheduleMapView)map;
        m.ComputeLayout(sim);
        Click(mapVp, m.RosterCardOf("sheep").GetCenter());
        await Frames(3);
        Save(mapVp, dir, "schedule_map_selected.png");
        Save(staffVp, dir, "schedule_staff_employee.png");
        Click(mapVp, m.CellOf("power_room").GetCenter());   // 선택 중이던 양이 발전실로 배치된다
        await Frames(3);
        Click(mapVp, m.CellOf("storage_room").GetCenter());
        await Frames(3);
        Save(staffVp, dir, "schedule_staff_room.png");

        // 모니터 2 — X 로 목록 복귀 → 직원 블록 클릭 → 상세 → X 로 목록.
        Click(staffVp, new Vector2(736f, 56f));
        await Frames(3);
        Save(staffVp, dir, "schedule_staff_after_close.png");
        Click(staffVp, new Vector2(186f, 216f));
        await Frames(3);
        Save(staffVp, dir, "schedule_staff_detail.png");
        Click(staffVp, new Vector2(736f, 56f));
        await Frames(3);

        // 실시간 운영 모니터 1 — 작업실 선택 + 아래 한 줄 알림.
        GameState.Instance.SetPhase(GamePhase.Live);
        var monVp = Vp(out var mon, new FacilityMonitorView());
        await Frames(3);
        typeof(FacilityMonitorView).GetMethod("SelectRoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(mon, new object[] { "maintenance_room" });
        NSP.Ui.FacilityAlertHud.Instance?.Notify("⚠ 발전실 사고 위험 — 근무자 부재", NSP.Ui.NoticeLevel.Warning);
        await Frames(4);
        Save(monVp, dir, "monitor1_room.png");
        monVp.QueueFree();
        GameState.Instance.SetPhase(GamePhase.Schedule);

        // 관계 Phase 1 — 동실 거부(고양이+여우) · 우호(늑대+토끼) · 불편(고양이+양).
        foreach (var id in sim.GetActiveEmployeeIds()) sim.ClearAssignment(id);
        sim.AssignToRoom("cat", "storage_room");
        sim.AssignToRoom("fox", "storage_room");
        sim.AssignToRoom("wolf", "core_room");
        sim.AssignToRoom("rabbit", "core_room");
        sim.AssignToRoom("dog", "power_room");
        sim.AssignToRoom("sheep", "guard_room");
        await Frames(2);
        m.ComputeLayout(sim);
        Hover(mapVp, m.RelationSlotOf("storage_room").GetCenter());
        await Frames(3);
        Save(mapVp, dir, "schedule_map_refuse.png");

        sim.AssignToRoom("fox", "guard_room");
        sim.AssignToRoom("sheep", "storage_room");
        await Frames(2);
        m.ComputeLayout(sim);
        Hover(mapVp, m.RelationSlotOf("storage_room").GetCenter());
        await Frames(3);
        Save(mapVp, dir, "schedule_map_uneasy.png");

        var panel = new SettingsPanel();
        AddChild(panel);
        await Frames(2);
        panel.Open();
        await Frames(4);
        GetViewport().GetTexture().GetImage().SavePng(dir + "/settings.png");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private SubViewport Vp(out Control view, Control v)
    {
        var vp = new SubViewport
        {
            Size = new Vector2I(800, 600), RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = true, Disable3D = true,
        };
        AddChild(vp);
        vp.AddChild(v);
        view = v;
        return vp;
    }

    private static void Click(SubViewport vp, Vector2 at)
    {
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }, true);
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
    }

    private static void Hover(SubViewport vp, Vector2 at) =>
        vp.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);

    private static void Save(SubViewport vp, string dir, string name) =>
        vp.GetTexture().GetImage().SavePng(dir + "/" + name);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
