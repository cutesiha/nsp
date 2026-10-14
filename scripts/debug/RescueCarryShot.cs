using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 사람 이송 연출을 눈으로 검수하기 위한 연속 스틸. CCTV 화면 그대로 찍는다.
//
//   godot --path . res://scenes/debug/RescueCarryShot.tscn -- <저장 폴더> [운반자] [환자]
//
// 기본은 세 가지를 차례로 찍는다(§66).
//   A. 늑대가 양을 업는다      (Piggyback)
//   B. 강아지가 고양이를 안는다 (Bridal)
//   C. 토끼가 여우를 부축한다   (Shoulder)
//
// 단계마다 한 장씩 남겨, 바닥 → 픽업 → 운반 → 침대 접근 → 인계 → 복귀가 끊기지 않는지 본다.
public partial class RescueCarryShot : Node
{
    private const float Step = 1f / 60f;
    private const string Room = "maintenance_room";

    private string _dir = "";
    private FacilitySimulation _sim;
    private ControlRoom3DController _ctl;

    // (운반자, 환자, 이름) — 세 가지 운반 방식이 모두 나오게 고른 조합이다.
    private static readonly (string Carrier, string Victim, string Name)[] Cases =
    {
        ("wolf", "sheep", "A_piggyback_wolf"),
        ("dog", "cat", "B_bridal_dog"),
        ("rabbit", "fox", "C_shoulder_rabbit"),
    };

    // CCTV 3D 뷰포트는 모니터에 붙어 있을 때만 그려진다(컨트롤러가 매 프레임 끈다).
    // 검수 중에는 모니터를 안 보고 있어도 계속 그리게 눌러 둔다.
    private SubViewport _keepDrawing;

    public override void _Process(double delta)
    {
        if (_keepDrawing != null && IsInstanceValid(_keepDrawing))
            _keepDrawing.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://rescue");
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        // 느린 재생 검수(§67) — 손 위치·접촉·인계를 눈으로 따라가기 쉽게 한다.
        //   ... -- <폴더> <운반자> <환자> slow
        if (System.Array.IndexOf(args, "slow") >= 0) { Engine.TimeScale = 0.5f; GD.Print("   (0.5배속 검수)"); }

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        _sim = FacilitySimulation.Instance;
        _ctl = ControlRoom3DController.Instance;
        if (_sim == null || _ctl == null) { GD.PushError("씬이 뜨지 않았다"); GetTree().Quit(1); return; }

        // CCTV 해상도를 올려 손 위치·접촉을 눈으로 볼 수 있게 한다(검수 전용).
        var vp = _ctl.FacilityCctvViewport;
        if (vp != null) { vp.Size = new Vector2I(1280, 960); _keepDrawing = vp; }

        var only = args.Length > 2 ? new[] { (args[1], args[2], "custom") } : Cases;
        _ctl.BeginShift();
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();

        // 작업실 3D 패스는 오른쪽 CRT 가 실제로 CCTV 를 띄우고 화면이 켜져 있을 때만 돈다
        // (UpdateCctvWorldViewport). 검수용이니 확실히 켜 둔다.
        _ctl.SetRightScreen(_ctl.CctvViewport);
        _ctl.SetScreenBrightness(1.0f);
        await Seconds(0.6);

        foreach (var (carrier, victim, name) in only)
        {
            await Shoot(carrier, victim, name);
        }
        GD.Print("saved → " + _dir);
        GetTree().Quit(0);
    }

    private async System.Threading.Tasks.Task Shoot(string carrierId, string victimId, string name)
    {
        GD.Print($"\n──────── {name} : {carrierId} 가 {victimId} 를 옮긴다 ────────");

        // 둘만 한 방에, 나머지는 멀리.
        foreach (string id in _sim.GetActiveEmployeeIds())
            _sim.AssignToRoom(id, id == carrierId || id == victimId ? Room : "guard_room");
        _sim.SetSurveillanceTarget(Room);
        for (int i = 0; i < 900 && (_sim.GetEmployeeState(carrierId).IsMoving
            || _sim.GetEmployeeState(victimId).IsMoving); i++) await Frame();
        await Seconds(1.0);

        var v = _sim.GetEmployeeState(victimId);
        var c = _sim.GetEmployeeState(carrierId);
        _sim.AddStress(victimId, 80f, "검수");

        // ① 바닥에 쓰러져 있다
        for (int i = 0; i < 900 && v.Faint != FaintPhase.OnFloor && v.Faint != FaintPhase.BeingChecked; i++)
            await Frame();
        await Seconds(0.4);
        Save(name, "01_floor");

        // ② 구조자가 다가와 확인한다
        for (int i = 0; i < 1800 && v.Faint != FaintPhase.AwaitingDecision; i++) await Frame();
        Save(name, "02_check");
        _sim.Rescue.Approve(victimId);

        // ③ 들어 올리기 — 잡는 순간 · 다 든 순간
        float pick = FaintRescueSystem.PickupSeconds(FaintRescueSystem.StyleOf(carrierId));
        await Until(() => v.Faint != FaintPhase.TransportPickup || v.FaintPhaseTimer >= pick * 0.50f, 6f);
        Save(name, "03_pickup_grab");
        await SaveClose(name, "03_pickup_grab", carrierId);
        await Until(() => v.Faint != FaintPhase.TransportPickup || v.FaintPhaseTimer >= pick * 0.96f, 6f);
        Save(name, "04_pickup_done");
        await SaveClose(name, "04_pickup_done", carrierId);
        DumpPair(carrierId, victimId);

        // ④ 운반 — 다리를 움직이며 걷는 중
        await Until(() => v.Faint == FaintPhase.Transporting, 6f);
        await Seconds(0.7);
        Save(name, "05_carry_walk");
        await SaveClose(name, "05_carry_walk", carrierId);

        // ⑤ 의무실로 따라간다
        string watching = Room;
        bool approachShot = false, handoff1 = false, handoff2 = false;
        for (float t = 0f; t < 70f; t += Step)
        {
            await Frame();
            if (c.CurrentRoomId != watching && !string.IsNullOrEmpty(c.CurrentRoomId))
            {
                watching = c.CurrentRoomId;
                _sim.SetSurveillanceTarget(watching);
                await Seconds(0.3);
                if (watching == FacilitySimulation.MedicalRoomIdPublic) Save(name, "06_enter_medical");
            }
            if (!approachShot && v.Faint == FaintPhase.BedApproach && v.FaintPhaseTimer > 0.9f)
            { approachShot = true; Save(name, "07_bed_side"); }
            if (!handoff1 && v.Faint == FaintPhase.InMedicalBed
                && v.FaintPhaseTimer > FaintRescueSystem.BedHandoffSeconds * 0.55f)
            { handoff1 = true; Save(name, "08_lowering"); await SaveClose(name, "08_lowering", carrierId); }
            if (!handoff2 && v.Faint == FaintPhase.InMedicalBed
                && v.FaintPhaseTimer > FaintRescueSystem.BedHandoffSeconds * 0.92f)
            { handoff2 = true; Save(name, "09_touchdown"); await SaveClose(name, "09_touchdown", carrierId); }
            if (v.Faint == FaintPhase.Recovering && c.CarrierRecoverTimer > 0f
                && c.CarrierRecoverTimer < FaintRescueSystem.CarrierRecoverSeconds * 0.45f)
            { Save(name, "10_carrier_up"); await SaveClose(name, "10_carrier_up", carrierId); break; }
        }

        // ⑥ 구조자가 돌아간다
        await Seconds(1.2);
        Save(name, "11_return");

        // 다음 케이스를 위해 환자를 되살린다.
        v.FaintRecoverTimer = 0.01f;
        await Seconds(1.5);
    }

    // ── 캡처 ─────────────────────────────────────────────────────────

    private readonly List<string> _shots = new();

    // 두 몸이 실제로 어디 있는지 숫자로 찍는다(붙어 보이지 않을 때 원인을 가린다).
    private void DumpPair(string carrierId, string victimId)
    {
        var w = FacilityCctvWorld.Instance;
        var cn = w?.EmployeeNode(carrierId);
        var vn = w?.EmployeeNode(victimId);
        if (cn == null || vn == null) { GD.Print("   [덤프] 노드 없음"); return; }
        var anchor = cn.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/CarryAnchor")
                     ?? cn.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/CarryAnchor");
        var cHips = cn.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
        var vHips = vn.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
        GD.Print($"   [덤프] 운반자 root={cn.GlobalPosition} yaw={Mathf.RadToDeg(cn.GlobalRotation.Y):0.0}");
        GD.Print($"   [덤프] 앵커={(anchor != null ? anchor.GlobalPosition.ToString() : "없음")}");
        GD.Print($"   [덤프] 환자 root={vn.GlobalPosition} yaw={Mathf.RadToDeg(vn.GlobalRotation.Y):0.0} scale={vn.Scale}");
        GD.Print($"   [덤프] 운반자Hips={cHips?.GlobalPosition} 환자Hips={vHips?.GlobalPosition}");
        if (anchor != null && vHips != null)
            GD.Print($"   [덤프] 앵커↔환자Hips 거리={anchor.GlobalPosition.DistanceTo(vHips.GlobalPosition):0.000}m");
    }

    private void Save(string name, string step)
    {
        var vp = _ctl.FacilityCctvViewport;
        var img = vp?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   ! {name}/{step} — 화면을 못 읽었다"); return; }
        string file = $"{_dir}/{name}_{step}.png";
        img.SavePng(file);
        _shots.Add(file);
        GD.Print($"   {name}/{step}");
    }

    // 손 위치·접촉을 눈으로 보려면 CCTV 기본 화각으로는 너무 멀다.
    // 같은 월드에 검수 전용 카메라를 잠깐 세워 가까이서 한 장 더 찍는다.
    private Camera3D _closeCam;

    private async System.Threading.Tasks.Task SaveClose(string name, string step, string carrierId)
    {
        var vp = _ctl.FacilityCctvViewport;
        var cn = FacilityCctvWorld.Instance?.EmployeeNode(carrierId);
        if (vp == null || cn == null) { Save(name, step); return; }

        if (_closeCam == null)
        {
            _closeCam = new Camera3D { Name = "ShotCloseCam", Fov = 48f };
            vp.AddChild(_closeCam);
        }
        var world = FindWorldCamera(vp);
        var target = cn.GlobalPosition + new Vector3(0f, 0.95f, 0f);
        // 운반자의 오른쪽 앞 약간 위 — 업은 등도, 안은 가슴도 같이 보이는 각도.
        // 운반자의 앞쪽 비스듬한 자리에서 본다 — 업은 등도, 가슴에 안은 환자도 같이 보인다.
        // 그 자리가 방 밖이면 방 중심 쪽으로 물러선다(벽을 뚫고 찍지 않게).
        var b = cn.GlobalTransform.Basis;
        var want = target + (-b.Z * 1.45f + b.X * 1.35f) + Vector3.Up * 0.55f;
        if (Mathf.Abs(want.X) > 2.3f || Mathf.Abs(want.Z) > 2.3f)
        {
            var toCenter = new Vector3(-target.X, 0f, -target.Z);
            if (toCenter.Length() < 0.4f) toCenter = Vector3.Back;
            want = target + toCenter.Normalized() * 2.0f + Vector3.Up * 0.75f;
        }
        _closeCam.GlobalPosition = want;
        _closeCam.LookAt(target, Vector3.Up);
        _closeCam.Current = true;
        await Frame();
        await Frame();
        Save(name, step + "_close");
        world?.MakeCurrent();
        await Frame();
    }

    private Camera3D FindWorldCamera(Node root)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Camera3D cam && cam != _closeCam) return cam;
            if (c is Node n && FindWorldCamera(n) is { } found) return found;
        }
        return null;
    }

    private async System.Threading.Tasks.Task Until(System.Func<bool> cond, float maxSeconds)
    {
        for (float t = 0f; t < maxSeconds; t += Step)
        {
            if (cond()) return;
            await Frame();
        }
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
