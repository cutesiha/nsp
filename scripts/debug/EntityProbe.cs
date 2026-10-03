using Godot;

namespace NSP.Debug;

// entity.glb 의 실제 비율을 잰다(리그를 자르는 경계값을 손으로 정하기 위한 일회용 측정).
//   godot --headless --path . res://scenes/debug/EntityProbe.tscn
public partial class EntityProbe : Node
{
    public override void _Ready()
    {
        var ps = GD.Load<PackedScene>("res://scenes/props/entity.tscn");
        if (ps == null) { GD.PrintErr("entity.tscn 없음"); GetTree().Quit(); return; }
        var root = ps.Instantiate<Node3D>();
        AddChild(root);

        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null || mi.Mesh.GetSurfaceCount() == 0) continue;
            var aabb = mi.Mesh.GetAabb();
            GD.Print($"mesh={mi.Name}  aabb pos={aabb.Position}  size={aabb.Size}");

            // 높이 구간별 삼각형 수 — 어디서 다리/몸통/목/머리가 갈리는지 본다.
            var arrays = mi.Mesh.SurfaceGetArrays(0);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            int tri = (index.Length > 0 ? index.Length : verts.Length) / 3;
            GD.Print($"  triangles={tri}");

            const int Bins = 20;
            var count = new int[Bins];
            var widthMax = new float[Bins];
            var depthMax = new float[Bins];
            for (int t = 0; t < tri; t++)
            {
                int i0 = index.Length > 0 ? index[t * 3] : t * 3;
                int i1 = index.Length > 0 ? index[t * 3 + 1] : t * 3 + 1;
                int i2 = index.Length > 0 ? index[t * 3 + 2] : t * 3 + 2;
                var c = (verts[i0] + verts[i1] + verts[i2]) / 3f;
                float ny = (c.Y - aabb.Position.Y) / Mathf.Max(1e-4f, aabb.Size.Y);
                int b = Mathf.Clamp((int)(ny * Bins), 0, Bins - 1);
                count[b]++;
                float lateral = Mathf.Abs((c.X - aabb.Position.X) / aabb.Size.X - 0.5f) * 2f;
                if (lateral > widthMax[b]) widthMax[b] = lateral;
                float dz = Mathf.Abs((c.Z - aabb.Position.Z) / aabb.Size.Z - 0.5f) * 2f;
                if (dz > depthMax[b]) depthMax[b] = dz;
            }
            for (int b = 0; b < Bins; b++)
                GD.Print($"  y {b / (float)Bins:0.00}~{(b + 1) / (float)Bins:0.00}  tri={count[b],5}" +
                         $"  maxLateral={widthMax[b]:0.00}  maxDepth={depthMax[b]:0.00}");
        }
        GetTree().Quit();
    }
}
