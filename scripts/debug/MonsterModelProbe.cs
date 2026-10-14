using System.Collections.Generic;
using Godot;

namespace NSP.Debug;

// 괴물 FBX 3종이 Godot 에서 **실제로 어떻게 들어왔는지** 찍어 본다.
//
//   godot --headless --path . res://scenes/debug/MonsterModelProbe.tscn --quit-after 600
//
// 추측으로 애니메이션 이름을 부르지 않기 위한 사전 조사다(지시서 §6).
// 노드 트리 · Skeleton3D · AnimationPlayer 의 클립 목록 · 메시 크기 · 머티리얼을 전부 적는다.
public partial class MonsterModelProbe : Node
{
    private static readonly string[] Paths =
    {
        "res://assets/models/ghosts/MonsterPSX.fbx",
        "res://assets/models/ghosts/CreepyDoll.fbx",
        "res://assets/models/ghosts/Huntsman_spider(Unrigged).fbx",
    };

    public override void _Ready()
    {
        GD.Print("\n\n################ 괴물 모델 조사 ################");
        foreach (string path in Paths) Inspect(path);
        GD.Print("\n################ 끝 ################");
        GetTree().Quit();
    }

    private void Inspect(string path)
    {
        GD.Print($"\n===== {path} =====");
        if (!ResourceLoader.Exists(path)) { GD.Print("   파일을 찾지 못했다"); return; }
        var ps = GD.Load<PackedScene>(path);
        if (ps == null) { GD.Print("   PackedScene 으로 읽히지 않는다"); return; }

        var root = ps.Instantiate<Node3D>();
        AddChild(root);
        GD.Print($"   루트: {root.Name} ({root.GetType().Name})  자식 {root.GetChildCount()}");

        var meshes = new List<MeshInstance3D>();
        var skeletons = new List<Skeleton3D>();
        var players = new List<AnimationPlayer>();
        Walk(root, 1, meshes, skeletons, players);

        GD.Print($"   ── 메시 {meshes.Count} · 스켈레톤 {skeletons.Count} · AnimationPlayer {players.Count}");

        var box = new Aabb();
        bool first = true;
        foreach (var m in meshes)
        {
            // 루트 기준 크기. **부모 사슬 전체**를 거쳐야 한다 —
            // 스킨 메시는 Skeleton3D 아래에 있고 그 노드에 축척이 걸려 있는 경우가 많아,
            // 바로 위 Transform 만 곱하면 터무니없이 작은 값이 나온다.
            var a = m.GetAabb();
            a = (root.GlobalTransform.AffineInverse() * m.GlobalTransform) * a;
            if (first) { box = a; first = false; }
            else box = box.Merge(a);
            GD.Print($"      메시 {m.Name,-24} 표면 {m.Mesh?.GetSurfaceCount()}  " +
                     $"크기 {a.Size}  재질 {(m.Mesh?.SurfaceGetMaterial(0)?.ResourceName ?? "-")}");
        }
        if (!first)
            GD.Print($"   ── 전체 크기 {box.Size}   바닥 y {box.Position.Y:0.000}   중심 {box.GetCenter()}");

        foreach (var sk in skeletons)
            GD.Print($"   ── 스켈레톤 {sk.Name} 뼈 {sk.GetBoneCount()}개  " +
                     $"루트뼈 {(sk.GetBoneCount() > 0 ? sk.GetBoneName(0) : "-")}");

        foreach (var ap in players)
        {
            var list = ap.GetAnimationList();
            GD.Print($"   ── AnimationPlayer {ap.Name} 클립 {list.Length}개");
            foreach (string clip in list)
            {
                var anim = ap.GetAnimation(clip);
                GD.Print($"      · \"{clip}\"  길이 {anim?.Length:0.00}초  루프 {anim?.LoopMode}  트랙 {anim?.GetTrackCount()}");
            }
        }
        if (players.Count == 0) GD.Print("   ── 애니메이션 없음 (정지 메시)");

        root.QueueFree();
    }

    private static void Walk(Node n, int depth, List<MeshInstance3D> meshes,
        List<Skeleton3D> skeletons, List<AnimationPlayer> players)
    {
        if (n is MeshInstance3D mi) meshes.Add(mi);
        if (n is Skeleton3D sk) skeletons.Add(sk);
        if (n is AnimationPlayer ap) players.Add(ap);
        if (depth <= 3)
            GD.Print($"   {new string(' ', depth * 3)}{n.Name} <{n.GetType().Name}>");
        foreach (var c in n.GetChildren()) Walk(c, depth + 1, meshes, skeletons, players);
    }
}
