using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 베이스 몸체 조사기 — 의상을 **어떻게 만들 수 있는지** 판단하기 위한 실측.
//
//   godot --headless --path . res://scenes/tools/HairSourceInspector.tscn --quit-after 400
//
// 핵심 질문: 몸 메시가 본 가중치를 들고 있는가. 들고 있다면 그 가중치를 그대로 물려받는
// "껍질" 을 떠서 옷을 만들 수 있다 — Blender 없이도 몸을 정확히 따라 움직이고, 애초에
// 몸 바깥에 있으므로 뚫릴 일이 없다.
public partial class HairSourceInspector : Node
{
    public override void _Ready()
    {
        foreach (string p in new[] { HairSourceSplitter.MaleBaseGltf, HairSourceSplitter.FemaleBaseGltf })
            Probe(p);
        GD.Print("\n#### 조사 끝");
    }

    private void Probe(string path)
    {
        GD.Print($"\n################ {path.GetFile()}");
        var root = ResourceLoader.Load<PackedScene>(path).Instantiate<Node3D>();
        Skeleton3D skel = HairSourceSplitter.FindSkeleton(root);

        MeshInstance3D body = skel.GetChildren().OfType<MeshInstance3D>()
            .Where(m => m.Mesh != null)
            .OrderByDescending(m => m.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].As<Vector3[]>().Length)
            .First();
        GD.Print($"  몸 메시 '{body.Name}' · skin={(body.Skin != null ? "있음" : "없음")} " +
                 $"· skeleton='{body.Skeleton}'");

        var arr = body.Mesh.SurfaceGetArrays(0);
        var vs = arr[(int)Mesh.ArrayType.Vertex].As<Vector3[]>();
        var bones = arr[(int)Mesh.ArrayType.Bones].As<int[]>();
        var weights = arr[(int)Mesh.ArrayType.Weights].As<float[]>();
        var norms = arr[(int)Mesh.ArrayType.Normal].As<Vector3[]>();
        var uvs = arr[(int)Mesh.ArrayType.TexUV].As<Vector2[]>();
        var idx = arr[(int)Mesh.ArrayType.Index].As<int[]>();
        GD.Print($"  정점 {vs.Length} · 삼각 {idx.Length / 3} · 법선 {(norms != null ? "O" : "X")} " +
                 $"· UV {(uvs != null ? "O" : "X")}");
        GD.Print($"  BONES {(bones == null ? "없음" : $"{bones.Length}개 ({bones.Length / vs.Length}/정점)")} " +
                 $"· WEIGHTS {(weights == null ? "없음" : $"{weights.Length}개")}");
        if (bones == null) { GD.Print("  !! 가중치가 없다 — 껍질 방식 불가"); root.QueueFree(); return; }

        // 어느 본이 어디를 지배하는가. 옷 영역을 본 이름으로 정의하기 위한 자료다.
        int per = bones.Length / vs.Length;
        var owned = new Dictionary<int, (int n, float lo, float hi)>();
        for (int i = 0; i < vs.Length; i++)
        {
            int best = bones[i * per]; float bw = weights[i * per];
            for (int k = 1; k < per; k++)
                if (weights[i * per + k] > bw) { bw = weights[i * per + k]; best = bones[i * per + k]; }
            if (!owned.TryGetValue(best, out var e)) e = (0, 99f, -99f);
            owned[best] = (e.n + 1, Mathf.Min(e.lo, vs[i].Y), Mathf.Max(e.hi, vs[i].Y));
        }
        GD.Print("  주 지배 본 (정점수 많은 순):");
        foreach (var kv in owned.OrderByDescending(k => k.Value.n).Take(16))
            GD.Print($"    {skel.GetBoneName(kv.Key),-14} 정점 {kv.Value.n,5}  y {kv.Value.lo:F3}~{kv.Value.hi:F3}");
        GD.Print($"  (가중치가 실린 본 {owned.Count}개 / 전체 {skel.GetBoneCount()}개)");

        root.QueueFree();
    }
}
