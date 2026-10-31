using Godot;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 차폐문을 닫은 복도에 괴물을 **문 앞 단계**로 세워 놓고, 몸이 문 평면을 넘는지 **수치로** 잰다.
//
//   godot --headless --path . res://scenes/debug/BarrierBlockShot.tscn
//
// 보는 것은 하나다 — 괴물이 차폐문을 뚫고 나와 있지 않은가(1013 QA 23번).
// 눈으로 보려면 복도 조명이 필요해 캡처가 까다롭다. 대신 그려지는 메시의 월드 AABB 를
// 직접 재서, 중앙제어실 쪽으로 가장 많이 나온 지점이 문 평면(DoorZ)을 넘었는지 본다.
//
//   통과(PASS) = 가장 앞선 지점이 문보다 바깥(작업실 쪽)에 있다.
public partial class BarrierBlockShot : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var world = new Node3D();
        AddChild(world);

        var seg = new CorridorSegment
        {
            Id = "corridor_west", DisplayName = "서측 접근 복도", Side = CorridorSide.West,
            RoomA = "central_office", RoomB = "발전실",
            Kind = CorridorKind.ControlAccess, IsBlockable = true,
            CctvCameraId = "CAM-W01", ThreatLane = true,
            State = BarrierState.Sealed, Shut = 1f,
        };

        var vis = new CorridorVisual();
        var root = vis.Build(seg, "중앙제어실", "발전실", controlRoomIsA: true);
        world.AddChild(root);
        vis.Sync(seg, 0f);
        await Frames(2);

        // 복도의 바깥 방향(중앙제어실 → 작업실). 이 부호의 반대쪽이 '문을 넘은' 쪽이다.
        float outward = Mathf.Sign(vis.ApproachZ(1f) - vis.DoorZ);
        GD.Print($"################ 차폐문 막힘 검사 ################");
        GD.Print($"문 평면 DoorZ = {vis.DoorZ:0.00}, 바깥 방향 = {outward:+0;-0}");

        foreach (var def in MonsterThreatSystem.Monsters)
        {
            if (def == null || string.IsNullOrEmpty(def.ModelPath)) continue;
            var actor = new MonsterActor();
            actor.Attach(world);
            if (!actor.Use(def)) { actor.Root.QueueFree(); continue; }

            foreach (var phase in new[] { ThreatPhase.AtDoor, ThreatPhase.Pounding, ThreatPhase.Breach })
            {
                var threat = new MonsterThreat { Def = def, SegmentId = seg.Id, Phase = phase, Progress = 1f };
                // 두드리는 동작은 앞뒤로 움직인다 — 두 주기쯤 돌려 **가장 앞선 순간**을 잡는다.
                float nose = outward > 0f ? float.MaxValue : float.MinValue;
                for (int i = 0; i < 90; i++)
                {
                    actor.Place(threat, vis, 1f / 60f);
                    await Frames(1);
                    var box = WorldBounds(actor.Root);
                    if (box.Size == Vector3.Zero) continue;
                    float front = outward > 0f ? box.Position.Z : box.Position.Z + box.Size.Z;
                    nose = outward > 0f ? Mathf.Min(nose, front) : Mathf.Max(nose, front);
                }
                float margin = (nose - vis.DoorZ) * outward;   // 양수 = 문 바깥에 머물렀다
                bool ok = margin >= 0f;
                if (ok) _pass++; else _fail++;
                GD.Print($"   {(ok ? "PASS" : "FAIL")}  {def.MonsterId,-9} {phase,-9} " +
                         $"가장 앞선 지점 {nose:0.00}, 문까지 {margin:+0.00;-0.00} m");
            }
            actor.Hide();
            actor.Root.QueueFree();
            await Frames(2);
        }

        GD.Print($"################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    // 그려지는 메시 전부를 감싸는 월드 공간 AABB.
    private static Aabb WorldBounds(Node3D root)
    {
        var box = new Aabb();
        bool got = false;
        void Walk(Node n)
        {
            if (n is MeshInstance3D mi && mi.Visible && mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0)
            {
                var a = mi.GlobalTransform * mi.GetAabb();
                box = got ? box.Merge(a) : a;
                got = true;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        return got ? box : new Aabb();
    }

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
