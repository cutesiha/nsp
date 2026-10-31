using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// 엔딩 → (크레딧) → 근무 기록 → [타이틀로] → 시작 화면까지 실제 흐름 그대로 돌려 찍는다(창 모드).
// 장면이 바뀌어도 살아 있도록 검사 본체는 루트(Root) 밑에 붙는다.
//   godot --path . res://scenes/debug/EndingFlowProbe.tscn -- <저장 폴더> [true|loose|late|bad] [배속]
public partial class EndingFlowProbe : Node
{
    public override void _Ready()
    {
        var runner = new Runner { Name = "EndingFlowProbeRunner" };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, runner);
    }

    private partial class Runner : Node
    {
        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            _ = Run();
        }

        private async Task Run()
        {
            var args = OS.GetCmdlineUserArgs();
            string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
            string which = args.Length > 1 ? args[1] : "bad";
            float speed = args.Length > 2 && float.TryParse(args[2], out float s) ? s : 3f;
            double every = args.Length > 3 && double.TryParse(args[3], out double ev) ? ev : 2.0;

            DebugEntryPoint.SkipTitleOnNextBoot();
            GameModes.ForceSelect(GameMode.Competition);
            GetTree().CurrentScene.AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
            for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != NSP.Data.GamePhase.Schedule; i++) await Frame();
            await Seconds(1.0);

            var gs = GameState.Instance;
            bool recovered = which is "true" or "loose";
            bool caught = which is "true" or "late";
            gs.GetType().GetProperty("CoreProgress")?.SetValue(gs, recovered ? 100f : 20f);
            if (caught)
            {
                gs.GetType().GetProperty("FinalAccusedId")?.SetValue(gs, gs.SaboteurEmployeeId);
            }
            GD.Print($"[probe] 엔딩 시작 {which}  core={gs.CoreProgress} caught={gs.WasCaught}");
            var flow = DebugEntryPoint.FindFlow(GetTree());
            flow.GetType().GetMethod("StartEnding", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(flow, null);
            Engine.TimeScale = speed;

            // 근무 기록 화면이 뜰 때까지(크레딧은 ESC 로 넘긴다).
            int shot = 0;
            double t0 = Time.GetTicksMsec() / 1000.0, next = every, scareNext = 0;
            while (!EndingRecordOverlay.IsShown && Time.GetTicksMsec() / 1000.0 - t0 < 240)
            {
                await Frame();
                double t = Time.GetTicksMsec() / 1000.0 - t0;
                if (t >= next && shot < 120) { Shot(dir, $"e_{which}_{shot++:000}"); next += every; }
                // 들이닥침(BAD) 은 1초 남짓이라 그 동안만 촘촘히 찍는다.
                if (t >= scareNext && Find<AdminDeathDirector>(GetTree().Root)?.FindChild("Monster", true, false) is Node3D { Visible: true }) { Shot(dir, $"s_{which}_{t:000.00}"); scareNext = t + 0.12; }
                if (Find<CreditsRoll>(GetTree().Root) != null) Key(Godot.Key.Escape);
            }
            Engine.TimeScale = 1f;
            GD.Print($"[probe] 근무 기록 화면 {(EndingRecordOverlay.IsShown ? "뜸" : "안 뜸")}  {Time.GetTicksMsec() / 1000.0 - t0:0.0}s");
            await Seconds(1.5);
            Shot(dir, $"r_{which}_record");

            // [타이틀로]
            var rec = Find<EndingRecordOverlay>(GetTree().Root);
            var btn = rec == null ? null : FindAll<BaseButton>(rec).FirstOrDefault();
            GD.Print($"[probe] [타이틀로] 버튼 {(btn != null ? "있음" : "없음")}");
            btn?.EmitSignal(BaseButton.SignalName.Pressed);
            for (int i = 0; i < 300 && GetTree().CurrentScene?.Name != "MainScene3D_Test"; i++) await Frame();

            // 크레딧이 기록 뒤에 오는 경우 — 넘긴다.
            for (int i = 0; i < 600; i++)
            {
                if (Find<CreditsRoll>(GetTree().Root) != null) Key(Godot.Key.Escape);
                if (TitleRoomDirector.Instance != null) break;
                await Frame();
            }
            foreach (double at in new[] { 1.0, 4.0, 9.0 })
            {
                await Seconds(at == 1.0 ? 1.0 : at == 4.0 ? 3.0 : 5.0);
                Shot(dir, $"t_{which}_{at:0}s");
                var win = GetTree().Root;
                GD.Print($"[probe] 타이틀 {at:0}s  scene={GetTree().CurrentScene?.Name} 3D끔={win.Disable3D} " +
                         $"WorldCovered={ControlRoom3DController.WorldCovered} blink={NSP.Ui.BlinkOverlay.Instance?.IsClosed} " +
                         $"title={(TitleRoomDirector.Instance != null)}");
            }
            GetTree().Quit();
        }

        private void Shot(string dir, string name) =>
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name + ".png"));

        private static void Key(Key k)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
        }

        private static T Find<T>(Node n) where T : Node
        {
            if (n is T t) return t;
            foreach (var c in n.GetChildren()) { var r = Find<T>(c); if (r != null) return r; }
            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> FindAll<T>(Node n) where T : Node
        {
            if (n is T t) yield return t;
            foreach (var c in n.GetChildren())
                foreach (var r in FindAll<T>(c)) yield return r;
        }

        private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        private async Task Seconds(double s) =>
            await ToSignal(GetTree().CreateTimer(s, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }
}
