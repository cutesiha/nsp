using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 기절·구조·운반 자세 캡처 — 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/FaintShot.tscn -- <저장 폴더>
//
// 시뮬레이션을 돌리지 않는다. 실제 작업실 씬 + 실제 직원 캐릭터에 FaintVisuals 를 직접 먹여
// "그 자세가 화면에서 어떻게 보이는가"만 확인한다. 특히 볼 것:
//   · 쓰러지는 도중과 바닥에 누운 모습
//   · 구조자가 환자 옆에 무릎을 굽히고 있는 거리(손이 허공에 뜨지 않는가)
//   · 업기 / 어깨 부축 / 공주님 안기에서 두 모델이 떨어져 뜨지 않는가(§30 S)
public partial class FaintShot : Node3D
{
    private static Dictionary<string, string> RoomScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("RoomScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private static Dictionary<string, string> EmployeeScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("EmployeeScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private Camera3D _cam;
    private Node3D _room;
    private readonly List<Node3D> _spawned = new();

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        var probe = new FacilityCctvWorld();
        _cam = new Camera3D { Fov = probe.CameraFov, Current = true };
        AddChild(_cam);
        _cam.Position = probe.CameraPosition;
        _cam.LookAt(probe.CameraLookAt, Vector3.Up);
        probe.Free();
        _cam.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.03f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.82f, 0.86f, 0.92f),
            AmbientLightEnergy = 1.15f,
        };
        var sun = new DirectionalLight3D { LightEnergy = 0.9f };
        AddChild(sun);
        sun.GlobalPosition = new Vector3(3, 4, 3);
        sun.LookAt(new Vector3(-1, 0, -1), Vector3.Up);

        _room = GD.Load<PackedScene>(RoomScenes["maintenance_room"]).Instantiate<Node3D>();
        AddChild(_room);

        // 쓰러지는 도중 / 바닥
        await ShootVictim(dir, "sheep", forward: true, at: 0.45f, name: "fall_front_mid");
        await ShootVictim(dir, "sheep", forward: true, at: 1.25f, name: "fall_front_done");
        await ShootVictim(dir, "sheep", forward: false, at: 1.25f, name: "fall_back_done");

        // 구조자가 옆에 앉아 확인
        await ShootResponder(dir, "sheep", "wolf");

        // 운반 세 가지
        await ShootCarry(dir, "sheep", "wolf", "carry_piggyback");
        await ShootCarry(dir, "sheep", "rabbit", "carry_shoulder");
        await ShootCarry(dir, "sheep", "dog", "carry_bridal");

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // ── 장면 만들기 ──────────────────────────────────────────────────

    private Node3D Spawn(string employeeId, Vector3 pos)
    {
        var n = GD.Load<PackedScene>(EmployeeScenes[employeeId]).Instantiate<Node3D>();
        _room.AddChild(n);
        n.Position = pos;
        n.RotationDegrees = new Vector3(0, 135, 0);
        _spawned.Add(n);
        return n;
    }

    // FaintVisuals 는 EmployeeCctvAnimator 를 받지만 여기엔 그게 없다 —
    // 클립은 AnimationPlayer 를 직접 돌려 맞춘다(RoomCctvPreview 와 같은 방식).
    private static void PlayClip(Node3D node, string clip)
    {
        var ap = node?.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (ap != null && ap.HasAnimation(clip)) ap.Play(clip);
    }

    private void ClearScene()
    {
        foreach (var n in _spawned) n.QueueFree();
        _spawned.Clear();
    }

    private static EmployeeState Victim(string id, FaintPhase phase, bool forward, float timer) => new()
    {
        EmployeeId = id, Incapacitated = true, Faint = phase,
        FellForward = forward, FaintPhaseTimer = timer, CurrentRoomId = "maintenance_room",
    };

    private async System.Threading.Tasks.Task ShootVictim(string dir, string id, bool forward, float at, string name)
    {
        ClearScene();
        var node = Spawn(id, new Vector3(0.2f, 0f, 0.2f));
        await Frames(2);
        var st = Victim(id, at >= FaintRescueSystem.FallSeconds ? FaintPhase.OnFloor : FaintPhase.Falling,
            forward, at);
        PlayClip(node, st.Faint == FaintPhase.Falling ? "idle" : "lying_idle");
        for (int i = 0; i < 20; i++) { FaintVisuals.PoseVictim(node, null, st, 1f / 60f); await Frame(); }
        Save(dir, $"faint_{name}.png");
        GD.Print($"   {name}");
    }

    private async System.Threading.Tasks.Task ShootResponder(string dir, string victimId, string responderId)
    {
        ClearScene();
        var vNode = Spawn(victimId, new Vector3(0.2f, 0f, 0.2f));
        var rNode = Spawn(responderId, new Vector3(1.2f, 0f, 0.9f));
        await Frames(2);

        var vSt = Victim(victimId, FaintPhase.BeingChecked, true,
            FaintRescueSystem.ApproachSeconds + FaintRescueSystem.CheckSeconds * 0.6f);
        var rSt = new EmployeeState { EmployeeId = responderId, CurrentRoomId = "maintenance_room" };
        PlayClip(vNode, "lying_idle");
        PlayClip(rNode, "repair");
        for (int i = 0; i < 90; i++)
        {
            FaintVisuals.PoseVictim(vNode, null, vSt, 1f / 60f);
            FaintVisuals.PoseResponder(rNode, null, rSt, vSt, vNode.Position, 1f / 60f);
            await Frame();
        }
        Save(dir, "faint_responder_check.png");
        GD.Print($"   responder {responderId} — 환자와의 거리 {vNode.Position.DistanceTo(rNode.Position):0.00}m");
    }

    private async System.Threading.Tasks.Task ShootCarry(string dir, string victimId, string carrierId, string name)
    {
        ClearScene();
        var cNode = Spawn(carrierId, new Vector3(0.4f, 0f, 0.4f));
        var vNode = Spawn(victimId, new Vector3(0.4f, 0f, 0.4f));
        await Frames(2);

        var vSt = Victim(victimId, FaintPhase.Transporting, true, FaintRescueSystem.LiftSeconds + 1f);
        var style = FaintRescueSystem.StyleOf(carrierId);
        PlayClip(cNode, style == CarryStyle.Shoulder ? "carry_box_heavy" : "carry_box_normal");
        PlayClip(vNode, style == CarryStyle.Bridal ? "lying_idle" : "idle");
        for (int i = 0; i < 60; i++)
        {
            FaintVisuals.PoseCarry(cNode, null, vNode, null, vSt, carrierId, 1f / 60f);
            await Frame();
        }
        Save(dir, $"faint_{name}.png");
        // 두 모델이 떨어져 뜨지 않는지 숫자로도 남긴다(§30 S).
        var cvr = cNode.GetNodeOrNull<Node3D>("VisualRoot");
        var vvr = vNode.GetNodeOrNull<Node3D>("VisualRoot");
        float gap = cvr != null && vvr != null ? cvr.GlobalPosition.DistanceTo(vvr.GlobalPosition) : -1f;
        GD.Print($"   {name} ({carrierId}) — 두 몸 사이 {gap:0.00}m");
    }

    // ── 캡처 ─────────────────────────────────────────────────────────

    private void Save(string dir, string file) =>
        GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
