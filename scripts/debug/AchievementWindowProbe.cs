using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// 일시정지 창의 도전과제 창을 찍어 본다(창 모드). 과제 몇 개를 딴 상태로 연다.
public partial class AchievementWindowProbe : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var m = AchievementManager.Instance;
        foreach (string id in new[] { Achievements.FirstLaunch, Achievements.FirstShift, Achievements.GhostIgnored, Achievements.TrueEnding })
            m?.Unlock(id);
        var panel = new AchievementPanel();
        AddChild(panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        panel.Open();
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "achievement_window.png"));
        GetTree().Quit();
    }
}
