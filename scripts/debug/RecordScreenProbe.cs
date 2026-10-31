using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// 엔딩 뒤 근무 기록 화면(EndingRecordOverlay)을 찍어 본다 — 엔딩 수집 줄이 제대로 뜨는가(창 모드).
// BAD 와 TRUE 를 본 것으로 기록한 뒤 TRUE 의 기록 화면을 띄운다. 에디터 실행이라 기록 파일은 다음 F5 에 비워진다.
//   godot --path . res://scenes/debug/RecordScreenProbe.tscn -- <저장 폴더>
public partial class RecordScreenProbe : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        EndingState.Record(EndingState.Kind.Bad);
        EndingState.Record(EndingState.Kind.True);

        var layer = new CanvasLayer { Layer = 130 };
        AddChild(layer);
        layer.AddChild(new EndingRecordOverlay { Kind = EndingState.Kind.True });
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "record_screen.png"));
        GD.Print($"[probe] seen {EndingState.SeenCount}/4");
        GetTree().Quit();
    }
}
