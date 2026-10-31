using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// 엔딩 → 근무 기록 → [타이틀로] 뒤의 시작 화면을 재현해 찍는다(창 모드).
// EndingDirector 의 [타이틀로] 와 같은 값(Last · PendingWake)을 세우고 메인 씬을 띄운다.
//   godot --path . res://scenes/debug/TitleWakeProbe.tscn -- <저장 폴더> [true|loose|late|bad]
public partial class TitleWakeProbe : Node
{
    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        var kind = (args.Length > 1 ? args[1] : "true") switch
        {
            "bad" => EndingState.Kind.Bad,
            "late" => EndingState.Kind.Late,
            "loose" => EndingState.Kind.Loose,
            _ => EndingState.Kind.True,
        };
        // 이번 실행이 처음 켠 것이 아니라 "엔딩을 본 뒤" 라는 것 — 경고는 이미 지나갔다.
        typeof(BootWarning).GetField("_shown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, true);
        EndingState.Record(kind);
        EndingState.PendingWake = kind;
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        if (args.Length > 2 && args[2] == "noglow")
        {
            var ov = NSP.Ui.AmbientOverlay.Instance;
            var f = typeof(NSP.Ui.AmbientOverlay).GetField("_centerGlow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f?.GetValue(ov) is CanvasItem g) g.Visible = false;
            GD.Print("[probe] centre glow off");
        }
        foreach (double at in new[] { 1.0, 3.0, 6.0, 10.0 })
        {
            await ToSignal(GetTree().CreateTimer(at == 1.0 ? 1.0 : at - Prev(at)), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"wake_{kind}_{at:0}s.png"));
            GD.Print($"[probe] {at:0}s blink={NSP.Ui.BlinkOverlay.Instance?.IsClosed}");
        }
        GetTree().Quit();
    }

    private static double Prev(double at) => at switch { 3.0 => 1.0, 6.0 => 3.0, 10.0 => 6.0, _ => 0.0 };
}
