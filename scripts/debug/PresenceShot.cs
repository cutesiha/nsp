using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

// '존재' 연출 캡처 — 반사 · 문창 · 조명 복귀.
//   godot --path . res://scenes/debug/PresenceShot.tscn -- <저장 폴더>
public partial class PresenceShot : Node
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
        for (int i = 0; i < 1500 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.5);
        GameState.Instance.SetPhase(GamePhase.Live);
        await Seconds(1.0);
        // 패드와 업무 창을 치워 제어실이 보이게 한다.
        AdminPad3D.Instance?.Close();
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(1.2);

        var d = PresenceDirector.Instance;
        Save("presence_0_before");

        d.Fire(PresenceDirector.Kind.Reflection);
        await Seconds(0.17);   // 0.35초짜리 연출의 한가운데(가장 진한 순간)
        Save("presence_1_reflection");
        await Seconds(0.6);

        d.Fire(PresenceDirector.Kind.DoorWindow);
        await Seconds(0.30);   // 0.6초짜리 연출의 한가운데
        Save("presence_2_door_mid");
        await Seconds(0.8);

        d.Fire(PresenceDirector.Kind.LightReturn);
        await Frames(1);
        Save("presence_3_lightreturn");

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    // 형체가 뜨는 자리(화면 오른쪽 모니터 위)의 평균 밝기. 눈으로 가늠하지 않고 숫자로 본다.
    private static readonly Rect2I Probe = new(1267, 324, 269, 324);
    private float _baseline = -1f;

    private void Save(string n)
    {
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng($"{_dir}/{n}.png");

        float mean = 0f;
        if (img != null)
        {
            double sum = 0;
            int count = 0;
            for (int y = Probe.Position.Y; y < Probe.End.Y && y < img.GetHeight(); y += 2)
            for (int x = Probe.Position.X; x < Probe.End.X && x < img.GetWidth(); x += 2)
            {
                var c = img.GetPixel(x, y);
                sum += (c.R + c.G + c.B) / 3.0;
                count++;
            }
            mean = count == 0 ? 0f : (float)(sum / count);
        }
        if (_baseline < 0f) _baseline = mean;
        float drop = _baseline <= 0f ? 0f : (1f - mean / _baseline) * 100f;
        GD.Print($"   {n,-26} 형체 자리 평균 밝기 {mean:0.0000}  (기준 대비 {drop:+0.0;-0.0}% 어두움)");
    }

    private async System.Threading.Tasks.Task Frames(int k)
    {
        for (int i = 0; i < k; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
