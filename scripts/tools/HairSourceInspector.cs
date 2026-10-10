using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 원본 에셋 구조 조사기 — 스튜디오를 만들기 전에 **실제로 무엇이 들어 있는지** 본다.
//
//   godot --headless --path . res://scenes/tools/HairSourceInspector.tscn --quit-after 400
//
// 지시서는 "FBX 내부 노드와 메시 구조를 조사하여 실제 개별 헤어스타일 단위로 분리" 하라고
// 했다. 파일 이름만 보고 짐작하지 않기 위해, 가져온 씬을 직접 펼쳐 표면 단위로 센다.
// 표면 개수가 같은 쌍은 "같은 메시가 두 번 들어 있는 것" 일 수 있으므로 실제로 비교한다.
public partial class HairSourceInspector : Node
{
    private const string MaleHair = "res://assets/characters/source/male_hair/hair.fbx";

    private sealed class Surf
    {
        public string Label = "";
        public Vector3[] V;
        public int Idx;
        public Aabb Box;
    }

    public override void _Ready()
    {
        GD.Print($"\n################ 남성 헤어팩 표면 단위 — {MaleHair}");
        var packed = ResourceLoader.Load<PackedScene>(MaleHair);
        var root = packed.Instantiate<Node3D>();
        var surfs = new List<Surf>();

        foreach (Node n in root.GetChildren())
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            Transform3D t = mi.Transform;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arr = mi.Mesh.SurfaceGetArrays(s);
                var vs = arr[(int)Mesh.ArrayType.Vertex].As<Vector3[]>();
                var idx = arr[(int)Mesh.ArrayType.Index].As<int[]>();
                var baked = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) baked[i] = t * vs[i];
                var box = new Aabb(baked[0], Vector3.Zero);
                foreach (Vector3 v in baked) box = box.Expand(v);
                string mat = mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "(무)";
                surfs.Add(new Surf
                {
                    Label = $"노드{mi.Name}.surf{s} '{mat}'",
                    V = baked, Idx = idx?.Length ?? vs.Length, Box = box,
                });
                GD.Print($"- {surfs[^1].Label}: 정점 {vs.Length} 삼각 {(idx?.Length ?? vs.Length) / 3} " +
                         $"크기{box.Size.Snapped(Vector3.One * 0.0001f)} 중심{box.GetCenter().Snapped(Vector3.One * 0.0001f)}");
            }
        }
        root.QueueFree();

        GD.Print("\n-- 정점·삼각형 수가 같은 쌍을 실제로 비교한다");
        for (int i = 0; i < surfs.Count; i++)
            for (int j = i + 1; j < surfs.Count; j++)
            {
                Surf a = surfs[i], b = surfs[j];
                if (a.V.Length != b.V.Length || a.Idx != b.Idx) continue;

                float dSize = (a.Box.Size - b.Box.Size).Length();
                Vector3 ca = a.Box.GetCenter(), cb = b.Box.GetCenter();
                float maxInOrder = 0f;
                for (int k = 0; k < a.V.Length; k++)
                    maxInOrder = Mathf.Max(maxInOrder, (a.V[k] - ca).DistanceTo(b.V[k] - cb));

                // 정점 순서가 달라졌을 수도 있다 — 정렬해서도 재 본다.
                var sa = a.V.Select(v => v - ca).OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
                var sb = b.V.Select(v => v - cb).OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
                float maxSorted = 0f;
                for (int k = 0; k < sa.Length; k++) maxSorted = Mathf.Max(maxSorted, sa[k].DistanceTo(sb[k]));

                GD.Print($"  {a.Label} ↔ {b.Label}: 크기차 {dSize:F6}m · " +
                         $"순서대로 최대 {maxInOrder:F6}m · 정렬 후 최대 {maxSorted:F6}m");
            }

        GD.Print("\n#### 조사 끝");
    }
}
