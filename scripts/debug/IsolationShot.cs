using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 격리 절차 자세 캡처 — 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/IsolationShot.tscn -- <저장 폴더>
//
// 시뮬레이션을 돌리지 않는다. 실제 방 씬 + 실제 직원 캐릭터에 RoomWorkVisualController 를
// 그대로 먹여 "그 자세가 화면에서 어떻게 보이는가"만 확인한다. 특히 볼 것:
//   · 명령 직후 그 자리에서의 반응(작업실에서 — 아직 격리실로 걷지 않는다)
//   · 빈 침대의 압박밴드가 보이지 않는가(§21 — 밴드를 뚫고 눕는 그림 방지)
//   · 침대 옆 → 앉기 → 눕기 → 밴드 체결 → 발버둥 순서가 끊기지 않는가
//   · 누운 몸이 침대 면을 파고들지 않는가(골반 높이를 숫자로도 남긴다)
public partial class IsolationShot : Node3D
{
    private static Dictionary<string, string> RoomScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("RoomScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private static Dictionary<string, string> EmployeeScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("EmployeeScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private static Vector3[] Slots =>
        (Vector3[])typeof(FacilityCctvWorld)
            .GetField("Slots", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new[] { Vector3.Zero };

    private const float Dt = 1f / 60f;

    private FacilitySimulation _sim;
    private Camera3D _cam;
    private Node3D _room;
    private RoomWorkVisualController _visual;
    private readonly List<(string Id, Node3D Node, EmployeeCctvAnimator Anim, CctvEmployeeAction Action, Vector3 FallbackPos)> _visible = new();

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        _sim = FacilitySimulation.Instance;
        GameState.Instance?.ResetRun(2);
        _sim?.ResetRun();
        _sim?.ResetForNewShift();

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

        // ① 명령 직후의 반응 — 작업실에서.
        await ShootReactions(dir);

        // ② 격리실 — 빈 침대 · 걷기 · 앉기 · 눕기 · 체결 · 발버둥 · 해제.
        await ShootIsolationRoom(dir);

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // ── ① 반응(§6~§12) ──────────────────────────────────────────────
    private async System.Threading.Tasks.Task ShootReactions(string dir)
    {
        LoadRoom("core_room");
        // 반응 자세만 보는 캡처다 — 방에 막 들어온 것으로 치면 출입구로 옮겨져 화면 밖으로 나간다.
        _ago = 999f;
        foreach (string id in new[] { "sheep", "rabbit", "cat", "wolf", "dog", "fox" })
        {
            ClearActors();
            var st = _sim.GetEmployeeState(id);
            st.Isolated = true;
            st.Isolation = IsolationPhase.React;
            st.CurrentRoomId = "core_room";
            st.IsMoving = false;
            // 설비에 가리지 않는 빈 바닥에 세운다(반응은 서 있던 자리에서 일어난다).
            var spot = new Vector3(0.4f, 0f, 0.9f);
            Add(id, CctvEmployeeAction.Idle, spot);
            // 실제 CCTV 각도로는 인물이 화면 아래로 잘린다 — 검사용으로만 가까이 붙인다.
            _cam.Position = spot + new Vector3(1.5f, 0.9f, 1.8f);
            _cam.LookAt(spot + new Vector3(0f, 0.85f, 0f), Vector3.Up);
            await Frames(2);

            // 반응 중반(가장 크게 나오는 지점)에서 한 장.
            float peak = IsolationSystem.ReactSeconds(id) * 0.45f;
            st.IsolationPhaseTimer = 0f;
            for (float t = 0f; t < peak; t += Dt)
            {
                st.IsolationPhaseTimer = t;
                Step();
                await Frame();
            }
            Save(dir, $"iso_react_{id}.png");
            GD.Print($"   반응 {id} — {IsolationSystem.ReactSeconds(id):0.00}초, 이 순간 {peak:0.00}초");
            st.Isolated = false;
            st.Isolation = IsolationPhase.None;
        }
    }

    // ── ② 격리실 절차(§16~§31) ───────────────────────────────────────
    private async System.Threading.Tasks.Task ShootIsolationRoom(string dir)
    {
        LoadRoom("isolation_room");
        // 실제 CCTV 각도로는 침대가 너무 멀다 — 검사용으로만 가까이 붙인다.
        _cam.Position = new Vector3(1.9f, 1.9f, 1.7f);
        _cam.LookAt(new Vector3(-0.6f, 0.55f, -0.5f), Vector3.Up);

        // 빈 침대 — 밴드가 보이면 안 된다(§21).
        ClearActors();
        for (int i = 0; i < 6; i++) { Step(); await Frame(); }
        Save(dir, "iso_bed_empty.png");
        GD.Print($"   빈 침대 — 보이는 밴드 {StrapReport()} (0이어야 한다)");

        // 두 사람을 동시에 수용한다 — 침대와 밴드가 서로 섞이지 않아야 한다.
        var a = _sim.GetEmployeeState("sheep");
        var b = _sim.GetEmployeeState("fox");
        foreach (var st in new[] { a, b })
        {
            st.Isolated = true;
            st.Isolation = IsolationPhase.InRoom;
            st.CurrentRoomId = "isolation_room";
            st.IsMoving = false;
        }
        Add("sheep", CctvEmployeeAction.Isolated, Slots[0]);
        Add("fox", CctvEmployeeAction.Isolated, Slots[1]);
        await Frames(2);

        // 단계가 끝날 때마다 한 장씩. 시간은 실제 단계 길이에 맞춘 것이다.
        var marks = new (float At, string Name)[]
        {
            (0.8f, "walk_to_bed"),
            (3.4f, "sit_edge"),
            (4.6f, "lying"),
            (5.6f, "strap_half"),
            (7.0f, "strapped"),
            (12.0f, "struggle"),
            (24.0f, "settled"),
        };
        float now = 0f;
        foreach (var (at, name) in marks)
        {
            for (; now < at; now += Dt) { Step(); await Frame(); }
            Save(dir, $"iso_{name}.png");
            GD.Print($"   {name} ({at:0.0}s) — 밴드 {StrapReport()} · {LieReport("sheep")} · {LieReport("fox")}");
        }

        // 침대별 · 캐릭터별로 따로 한 장씩 — 자세가 침대를 따라가는지 캐릭터를 따라가는지 본다.
        foreach (var (who, only) in new[] { ("fox", true), ("sheep", true) })
        {
            ClearActors();
            _ago = 0.2f;
            if (!only) continue;
            Add(who, CctvEmployeeAction.Isolated, Slots[0]);
            for (float t = 0f; t < 14f; t += Dt) { Step(); await Frame(); }
            Save(dir, $"iso_solo_{who}_bed1.png");
            GD.Print($"   혼자 {who} — 밴드 {StrapReport()}, 골반높이 {HipHeight(who):0.000}m · {LieReport(who)}");
        }
        // 두 번째 침대만 쓰게 한다(첫 번째 침대를 다른 사람이 차지한 경우).
        ClearActors();
        Add("sheep", CctvEmployeeAction.Isolated, Slots[0]);
        Add("fox", CctvEmployeeAction.Isolated, Slots[1]);
        for (float t = 0f; t < 14f; t += Dt) { Step(); await Frame(); }
        Save(dir, "iso_two_beds.png");
        GD.Print($"   두 침대 — 밴드 {StrapReport()}, 양 {HipHeight("sheep"):0.000}m / 여우 {HipHeight("fox"):0.000}m");

        // CCTV 를 한참 뒤에 돌려 본 경우 — 걷는 것부터 다시 보여주지 않고 이미 묶여 있다.
        ClearActors();
        _ago = 999f;
        Add("sheep", CctvEmployeeAction.Isolated, Slots[0]);
        await Frames(2);
        for (int i = 0; i < 20; i++) { Step(); await Frame(); }
        Save(dir, "iso_late_view.png");
        GD.Print($"   나중에 본 격리실 — 밴드 {StrapReport()} (바로 묶여 있어야 한다)");

        // 다시 정상 진행으로 돌려놓고, 여우만 해제한다.
        ClearActors();
        _ago = 0.2f;
        Add("sheep", CctvEmployeeAction.Isolated, Slots[0]);
        Add("fox", CctvEmployeeAction.Isolated, Slots[1]);
        for (float t = 0f; t < 8f; t += Dt) { Step(); await Frame(); }

        // 여우만 해제 — 밴드가 풀리고 상체를 일으켜 일어난다. 양은 그대로 묶여 있어야 한다.
        for (int i = 0; i < _visible.Count; i++)
            if (_visible[i].Id == "fox")
                _visible[i] = _visible[i] with { Action = CctvEmployeeAction.Walking };
        b.Isolated = false;
        b.Isolation = IsolationPhase.None;
        foreach (var (at, name) in new (float, string)[] { (0.5f, "unstrap"), (1.6f, "get_up"), (2.8f, "stand_up") })
        {
            for (float t = 0f; t < at; t += Dt) { Step(); await Frame(); }
            Save(dir, $"iso_release_{name}.png");
            GD.Print($"   해제 {name} — 밴드 {StrapReport()} (여우 침대만 0이 되어야 한다)");
        }
    }

    // ── 장면 ─────────────────────────────────────────────────────────

    private void LoadRoom(string roomId)
    {
        _room?.QueueFree();
        ClearActors();
        _room = GD.Load<PackedScene>(RoomScenes[roomId]).Instantiate<Node3D>();
        AddChild(_room);
        _visual = new RoomWorkVisualController();
        _roomId = roomId;
    }

    private string _roomId = "";

    private void Add(string id, CctvEmployeeAction action, Vector3 fallback)
    {
        var n = GD.Load<PackedScene>(EmployeeScenes[id]).Instantiate<Node3D>();
        _room.AddChild(n);
        n.Position = fallback;
        n.RotationDegrees = new Vector3(0, 135, 0);
        var ap = n.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        var anim = ap != null ? new EmployeeCctvAnimator(ap, _visible.Count + 1) : null;
        _visible.Add((id, n, anim, action, fallback));
    }

    private void ClearActors()
    {
        foreach (var v in _visible) v.Node.QueueFree();
        _visible.Clear();
    }

    // arrivedAgo 는 "이 방에 들어온 지 몇 초인가". 방금 걸어 들어온 상황(0.2초)에서는
    // 침대까지 걸어가는 전체 절차가 재생되고, 오래 지난 값(999초)이면 CCTV 를 나중에 돌려
    // 본 것으로 보고 이미 묶여 있는 모습부터 보여준다(SnapIsolation).
    private float _ago = 0.2f;

    private void Step()
    {
        _visual.Update(_sim, _room, _roomId, _visible, Dt, null, null, _ => _ago);
        foreach (var v in _visible) v.Anim?.Update(Dt);
    }

    // ── 측정 ─────────────────────────────────────────────────────────

    // 침대 n 의 밴드·버클 중 보이는 것(침대 뼈대는 세지 않는다). 빈 침대는 0 이어야 한다(§21).
    private int VisibleStraps(int bed)
    {
        int n = 0;
        foreach (var child in _room.GetChildren())
            if (child is MeshInstance3D m && m.Visible)
            {
                string name = m.Name.ToString();
                if (name.StartsWith($"RestraintBed{bed}") && (name.Contains("Strap") || name.Contains("Buckle"))) n++;
            }
        return n;
    }

    // 누운 몸이 침대 축을 따라 있는가 — 머리·발을 잇는 선과 침대 긴 축의 각도(도).
    // 0 에 가까워야 한다. 90 에 가까우면 침대를 가로질러 누운 것이다.
    private string LieReport(string id)
    {
        foreach (var v in _visible)
        {
            if (v.Id != id) continue;
            var head = v.Node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/Neck/Head");
            var foot = v.Node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/UpperLegL/LowerLegL/FootL");
            if (head == null || foot == null) return "리그 없음";
            var body = head.GlobalPosition - foot.GlobalPosition;
            body.Y = 0f;
            if (body.LengthSquared() < 0.0004f) return "몸 축 없음";
            // 침대 긴 축(머리 방향).
            float yaw = v.Node.Rotation.Y;
            var bedAxis = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            float deg = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(
                body.Normalized().Dot(bedAxis.Normalized()), -1f, 1f)));
            return $"{id} 몸-침대 각 {Mathf.Min(deg, 180f - deg):0}도, 머리 {head.GlobalPosition.Y:0.00}m / 발 {foot.GlobalPosition.Y:0.00}m";
        }
        return "없음";
    }

    private string StrapReport() => $"침대1 {VisibleStraps(1)}/7 · 침대2 {VisibleStraps(2)}/7";

    private float HipHeight(string id)
    {
        foreach (var v in _visible)
        {
            if (v.Id != id) continue;
            var hips = v.Node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
            if (hips != null) return hips.GlobalPosition.Y;
            // 리그 이름이 다르면 VisualRoot 높이로 대신한다.
            var vr = v.Node.GetNodeOrNull<Node3D>("VisualRoot");
            return vr?.GlobalPosition.Y ?? -1f;
        }
        return -1f;
    }

    private void Save(string dir, string file) =>
        GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
