using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// DAY1 근무 배치 진입 — 패드가 저절로 올라오고 그 위에 안내가 떴다 사라지는지 본다.
// 스트레스 해금 여부도 함께 찍는다.
//
//   godot --path . res://scenes/debug/Day1PadHintShot.tscn -- <저장 폴더>
public partial class Day1PadHintShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        GD.Print($"DAY {GameState.Instance?.CurrentDay} · 단계 {GameState.Instance?.CurrentPhase}");
        GD.Print($"스트레스 해금 = {DayFeatures.StressEnabled} (StressUnlockDay={Config.Instance?.Data?.StressUnlockDay})");
        GD.Print($"능력치 {DayFeatures.StatsEnabled} · 금기 {DayFeatures.TaboosEnabled}");

        // 배치 진입 0.8초 뒤 패드가 올라오고, 그 0.35초 뒤 안내가 뜬다.
        double[] at = { 0.6, 1.1, 1.5, 2.2, 3.4, 4.2, 5.0 };
        double now = 0;
        var pad = AdminPad3D.Instance;
        var bubble = PadHintBubble.Instance;
        for (int i = 0; i < at.Length; i++)
        {
            await Seconds(at[i] - now);
            now = at[i];
            GD.Print($"   t={at[i]:0.0}s 패드열림={pad?.IsOpen} 든상태={pad?.IsHeld} 안내={bubble?.Visible}");
            GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/hint_{i}_{at[i]:0.0}s.png");
        }

        // 스트레스 구간이 카드에 보이는지 — 세 명을 서로 다른 구간에 세워 둔다(F-2).
        var simX = FacilitySimulation.Instance;
        if (simX != null)
        {
            var ids = simX.GetActiveEmployeeIds().ToList();
            if (ids.Count >= 3)
            {
                simX.GetEmployeeState(ids[0]).Stress = 20f;   // 주의
                simX.GetEmployeeState(ids[1]).Stress = 38f;   // 위험
                simX.GetEmployeeState(ids[2]).Stress = 47f;   // 기절
                simX.GetEmployeeState(ids[2]).Incapacitated = true;
                if (ids.Count >= 4) simX.GetEmployeeState(ids[3]).Isolated = true;
            }
        }

        // 모니터2 — 직원 블록 6개(증명사진)와 블록 하나를 고른 상세(3:4 상반신).
        var vp = ControlRoom3DController.Instance?.ScheduleStaffViewport;
        var staff = vp?.GetChildren().OfType<Control>()
            .SelectMany(c => c.GetChildren().OfType<NSP.Ui.ScheduleStaffView>().Prepend(c as NSP.Ui.ScheduleStaffView))
            .FirstOrDefault(c => c != null);
        if (staff != null)
        {
            await Seconds(0.4);
            vp.GetTexture()?.GetImage()?.SavePng($"{_dir}/monitor2_roster.png");
            var field = typeof(NSP.Ui.ScheduleStaffView)
                .GetField("_detailEmp", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (string who in new[] { "cat", "wolf" })
            {
                field?.SetValue(staff, who);
                staff.QueueRedraw();
                await Seconds(0.5);
                vp.GetTexture()?.GetImage()?.SavePng($"{_dir}/monitor2_detail_{who}.png");
            }
        }
        else GD.PrintErr("배치 화면(ScheduleStaffView)을 찾지 못했다");

        GD.Print($"배치 중 패드 사용 가능(DAY1) = {AdminPad3D.InService}");
        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(Mathf.Max(0.02, s)), SceneTreeTimer.SignalName.Timeout);
}
