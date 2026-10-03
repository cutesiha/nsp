using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

// 엔딩 연출 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/EndingShot.tscn -- <저장 폴더> <모드>
//
//   true / loose / late / bad : 코어 복구(100% / 83.7%) × 최종 보고서의 지목(정답 / 미지목) 네 조합을
//                               그대로 맞춘 뒤 엔딩을 시작해 몇 초마다 화면을 저장한다.
//   verdict                   : DAY5 최종 격리 보고서 화면(제출 전)을 저장한다.
//   report_true / report_bad  : DAY5 FINAL SHIFT REPORT 화면.
//   title_*                   : 그 엔딩을 본 뒤 [타이틀로] 를 누른 것처럼 시작 화면(눈을 뜨는 연출 포함).
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
            var kind = mode switch
            {
                "title_bad" => EndingState.Kind.Bad,
                "title_loose" => EndingState.Kind.Loose,
                "title_late" => EndingState.Kind.Late,
                _ => EndingState.Kind.True,
            };
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
        bool recovered = mode is "true" or "loose" or "report_true" or "verdict";
        gs.AddCoreProgress((recovered ? 100f : 83.7f) - gs.CoreProgress, "캡처");
        gs.AddShiftTotals(2, 6);
        // 근무를 거치지 않으므로 결번이 비어 있다 — 뽑아 두고 지목까지 맞춘다.
        if (string.IsNullOrEmpty(gs.SaboteurEmployeeId))
            gs.AssignRandomSaboteur(NSP.Facility.FacilitySimulation.Instance?.GetActiveEmployeeIds()
                                    ?? new System.Collections.Generic.List<string>());
        bool caught = mode is "true" or "late";
        if (mode is "true" or "loose" or "late" or "bad")
            gs.ForceFinalVerdict(caught ? gs.SaboteurEmployeeId : "", caught);
        GD.Print($"DAY{gs.CurrentDay} · CORE {gs.CoreProgress:0.0}%");

        var flow = GetTree().Root.FindChild("ShiftFlowController", true, false);
        if (mode == "verdict")
        {
            // DAY5 보고서 [계속] 과 같은 경로 — GUIDE-0 안내가 끝나면 왼쪽 CRT 에 보고서가 뜬다.
            DebugEntryPoint.SetStage(flow, "Report");
            DebugEntryPoint.Call(flow, "RequestRestFromReport");
            // GUIDE-0 안내는 플레이어가 눌러 넘긴다 — 캡처에서는 대신 몇 번 눌러 준다.
            for (int i = 0; i < 10; i++) { await Until(2.0 + i * 1.4); Press(); }
            await Until(18.0);
            Save(dir, "verdict.png");
            GetTree().Quit();
            return;
        }
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

        // 네 엔딩 모두 공통 도입(모니터 소등 → 방 → 패드)으로 시작한다. 길이가 서로 달라
        // 모드마다 눈여겨볼 지점의 시각을 따로 둔다.
        var shots = mode switch
        {
            "loose" => new[] { (9.0, "1_pad"), (20.0, "2_recovered"), (30.0, "3_guide"), (38.0, "4_cards"), (44.0, "5_broken"), (52.0, "6_white"), (62.0, "7_record") },
            "late" => new[] { (10.0, "1_pad"), (22.0, "2_countdown"), (34.0, "3_guide"), (46.0, "4_switch"), (52.0, "5_seal"), (62.0, "6_dark"), (74.0, "7_record") },
            "bad" => new[] { (3.0, "0_intro"), (8.0, "1_pad"), (13.0, "2_warning"), (18.0, "3_guide"), (22.0, "4_bulkhead"), (28.0, "5_door"), (38.0, "6_banner") },
            _ => new[] { (10.0, "1_pad"), (16.6, "2_flashback"), (24.0, "3_record"), (31.0, "4_call"), (42.0, "5_recovered"), (47.0, "6_guide"), (60.0, "7_banner") },
        };
        // 인자에 sweep 을 더하면 4초마다 통째로 찍는다(연출 길이를 맞출 때 쓴다).
        if (args.Length > 2 && args[2] == "sweep")
        {
            for (double at = 6.0; at <= 96.0; at += 4.0)
            {
                await Until(at);
                Save(dir, $"{mode}_t{at:00}.png");
            }
        }
        else
        {
            foreach (var (at, name) in shots)
            {
                await Until(at);
                Save(dir, $"{mode}_{name}.png");
            }
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

    private static void Press()
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
