using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

// 엔딩 연출 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/EndingShot.tscn -- <저장 폴더> true|bad|title_true|title_bad
//
//   true / bad             : DAY5 · 코어 100% / 83.7% 로 맞춘 뒤 엔딩을 시작해 몇 초마다 화면을 저장한다.
//   title_true / title_bad : 그 엔딩을 본 뒤 [타이틀로] 를 누른 것처럼 시작 화면(눈을 뜨는 연출 포함)을 저장한다.
public partial class EndingShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        string mode = args.Length > 1 ? args[1] : "true";

        if (mode.StartsWith("title"))
        {
            var kind = mode == "title_bad" ? EndingState.Kind.Bad : EndingState.Kind.True;
            EndingState.Record(kind);
            EndingState.PendingWake = kind;
            AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
            foreach (var (at, name) in new[] { (1.5, "a_wake"), (5.5, "b_room"), (9.0, "c_room") })
            {
                await Until(at);
                Save(dir, $"{mode}_{name}.png");
            }
            // 아무 키 → 장비 점등 → 메뉴(새 근무 시작 / 복구 실패 기록).
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
            for (double at = 10.0; at <= 14.0; at += 1.0)
            {
                await Until(at);
                GD.Print($"[t={at}] reveal={TitleTerminalView.Instance?.MenuRevealDone} staffOn={TitleStaffIdView.Instance?.PoweredOn}");
            }
            Save(dir, $"{mode}_d_menu.png");
            EndingState.Clear();
            GetTree().Quit();
            return;
        }

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(20);

        var gs = GameState.Instance;
        for (int d = gs.CurrentDay; d < 5; d++) gs.GoToNextDay();
        float target = mode is "bad" or "report_bad" ? 83.7f : 100f;
        gs.AddCoreProgress(target - gs.CoreProgress, "캡처");
        gs.AddShiftTotals(2, 6);
        GD.Print($"DAY{gs.CurrentDay} · CORE {gs.CoreProgress:0.0}%");

        var flow = GetTree().Root.FindChild("ShiftFlowController", true, false);
        if (mode.StartsWith("report"))
        {
            // DAY5 근무가 끝난 것처럼 — FINAL SHIFT REPORT 를 왼쪽 CRT 에 띄우고 확대해서 찍는다.
            var stageField = typeof(ShiftFlowController).GetField("_stage", BindingFlags.NonPublic | BindingFlags.Instance);
            stageField?.SetValue(flow, System.Enum.Parse(stageField.FieldType, "Ending"));
            typeof(ShiftFlowController).GetMethod("EndShiftSequence", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(flow, null);
            await Until(3.5);
            GetTree().Root.FindChild("MainScene3D_Test", true, false)?.Call("FocusMonitor", 1, 0.01f);
            await Until(5.0);
            Save(dir, $"{mode}.png");
            GetTree().Quit();
            return;
        }
        typeof(ShiftFlowController).GetMethod("StartEnding", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(flow, null);
        _start = Time.GetTicksMsec() / 1000.0;

        var shots = mode == "bad"
            ? new[] { (6.5, "1_seq"), (10.0, "2_stall"), (13.0, "3_failed"), (18.5, "4_unstable"), (22.3, "5_bang"), (27.5, "6_banner"), (36.0, "7_record") }
            : new[] { (8.0, "1_seq"), (12.5, "2_stable"), (21.0, "3_thanks"), (27.0, "4_lean"), (33.0, "5_banner"), (41.0, "6_record") };
        foreach (var (at, name) in shots)
        {
            await Until(at);
            Save(dir, $"{mode}_{name}.png");
        }
        EndingState.Clear();
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private double _start = -1;

    private async System.Threading.Tasks.Task Until(double seconds)
    {
        if (_start < 0) _start = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - _start < seconds) await Frame();
    }

    private void Save(string dir, string file) => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
