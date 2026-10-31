using System.Reflection;
using System.Threading.Tasks;
using Godot;
using NSP.View;

namespace NSP.Debug;

// 타이틀 흐름 확인(창 모드) — 2026-10-31 요청 네 가지.
//   ① 게임을 막 켰을 때만 광과민성 경고가 뜬다(다시 타이틀로 오면 안 뜬다).
//   ② 경고를 넘기면 [PRESS ANY KEY] 없이 곧바로 표제가 찍힌다.
//   ③ 다시 타이틀로 오면(엔딩 뒤 · 시작화면으로) [PRESS ANY KEY] 에서 기다린다.
//   ④ 확대 화면에서는 메뉴가 다 찍히면 바로 고를 수 있다(장비가 다 켜질 때까지 기다리지 않는다).
//   godot --path . res://scenes/debug/TitleFlowProbe.tscn
public partial class TitleFlowProbe : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        var main = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(main);
        await Seconds(0.15);
        Check(BoxAlpha() < 0.3f, $"경고 글이 서서히 떠오른다(페이드 인 시작 알파 {BoxAlpha():0.00})");
        await Seconds(0.85);
        Check(BootWarning.Active, "① 처음 켜면 광과민성 경고가 떠 있다");
        Check(Muted(), "경고가 떠 있는 동안은 소리가 꺼져 있다(Master 음소거)");
        Check(string.IsNullOrEmpty(NSP.Core.Sfx.Instance?.CurrentMusic), $"경고가 떠 있는 동안 배경음은 시작하지 않았다 ('{NSP.Core.Sfx.Instance?.CurrentMusic}')");
        await Seconds(1.0);
        Check(BoxAlpha() > 0.95f, $"경고 글이 다 떠올랐다 (알파 {BoxAlpha():0.00})");
        await Seconds(7.0);
        Check(BootWarning.Active, "아무 키도 누르지 않으면 9초가 지나도 닫히지 않는다");
        Check(Phase() == "Off", $"② 경고가 떠 있는 동안 타이틀은 [PRESS ANY KEY] 로 기다리지 않는다 (phase {Phase()})");

        Key(Godot.Key.Space);                               // 경고 넘기기 — 이 한 번뿐
        double t0 = Time.GetTicksMsec() / 1000.0;
        await Seconds(0.25);
        Check(BootWarning.Active && BoxAlpha() < 0.95f, "넘기면 글부터 서서히 사라진다(페이드 아웃)");
        await Seconds(0.6);
        Check(!Muted(), "경고가 걷히면 소리가 돌아온다");
        Check(NSP.Core.Sfx.Instance?.CurrentMusic == "rest_time", "경고가 걷힐 때 배경음이 시작된다");
        Check(!BootWarning.Active, "경고가 닫혔다");
        Check(TitleTerminalView.Instance?.CurrentMode == TitleTerminalView.Mode.Menu,
            $"② 키를 더 누르지 않아도 표제 · 메뉴가 찍히기 시작했다 (mode {TitleTerminalView.Instance?.CurrentMode})");
        while (Phase() != "Menu" && Time.GetTicksMsec() / 1000.0 - t0 < 10.0) await Seconds(0.05);
        double ready = Time.GetTicksMsec() / 1000.0 - t0;
        Check(Phase() == "Menu" && ready < 3.0,
            $"④ 경고를 넘긴 뒤 {ready:0.00}초 만에 메뉴를 고를 수 있다(장비 연출을 기다리지 않는다)");

        // 다시 타이틀로 — 엔딩 뒤 [타이틀로] · 일시정지의 「시작화면으로」 와 같다(씬을 새로 만든다).
        main.QueueFree();
        await Seconds(0.3);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        await Seconds(2.0);
        Check(!BootWarning.Active, "① 다시 타이틀로 오면 광과민성 경고가 뜨지 않는다");
        Check(Phase() == "Standby", $"③ 다시 타이틀로 오면 [PRESS ANY KEY] 에서 기다린다 (phase {Phase()})");
        Check(TitleTerminalView.Instance?.CurrentMode == TitleTerminalView.Mode.Standby, "③ 단말기에 대기 화면이 떠 있다");
        await Seconds(1.5);
        Check(Phase() == "Standby", "③ 키를 누르기 전에는 계속 기다린다");
        Key(Godot.Key.Space);
        await Seconds(0.2);
        Check(Phase() == "PoweringOn" || Phase() == "Menu", $"③ 키를 누르면 전원이 켜진다 (phase {Phase()})");
        Key(Godot.Key.Enter);                               // 부팅 중 건너뛰기(예전엔 닿지 않던 블록)
        await Seconds(0.2);
        Check(Phase() == "Menu", $"부팅 중에 누르면 남은 연출을 건너뛰고 바로 메뉴 (phase {Phase()})");

        GD.Print($"\n[probe] 결과: {_pass} PASS / {_fail} FAIL");
        GetTree().Quit();
    }

    private static bool Muted() => AudioServer.IsBusMute(AudioServer.GetBusIndex("Master"));

    private float BoxAlpha()
    {
        var w = Find<BootWarning>(GetTree().Root);
        var box = w == null ? null : typeof(BootWarning).GetField("_box", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(w) as Control;
        return box?.Modulate.A ?? -1f;
    }

    private static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var r = Find<T>(c); if (r != null) return r; }
        return null;
    }

    private static string Phase()
    {
        var d = TitleRoomDirector.Instance;
        if (d == null) return "(없음)";
        return typeof(TitleRoomDirector).GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(d)?.ToString() ?? "?";
    }

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private static void Key(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    private async Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
