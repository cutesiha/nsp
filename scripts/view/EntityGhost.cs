using Godot;

namespace NSP.View;

// entity.glb(결번자 "존재") 인스턴스의 메시에 석고/플라스터 느낌 머티리얼을 입힌다.
//  - 몸통: 흰색~옅은 회색, 거칠고 무광(StandardMaterial3D, roughness 높음, metallic 0)
//  - 손끝/발끝: 검붉은색 — 메시 로컬 AABB 기준으로 "아래쪽(발) / 좌우로 뻗은 위쪽(손)" 정점에
//    버텍스 컬러를 구워 넣고, StandardMaterial3D 의 vertex_color_use_as_albedo 로 표현한다.
//    (텍스처 제작 없이 색/러프니스만으로 처리 — 요청 사항)
// [Tool] — 에디터에서도 바로 반영된다.
[Tool]
public partial class EntityGhost : Node3D
{
    [Export] public Color BodyColor = new(0.86f, 0.86f, 0.87f);
    [Export] public Color TipColor = new(0.22f, 0.03f, 0.035f);
    [Export(PropertyHint.Range, "0,1")] public float Roughness = 0.93f;
    // 발끝이 물드는 높이 범위(정규화 0~1). 클수록 발목 위까지 붉게 번진다.
    [Export(PropertyHint.Range, "0.02,0.5")] public float TipReach = 0.16f;
    // 손이 물드는 좌우 범위. 몸 중심축에서 좌우로 얼마나 뻗어야 붉어지기 시작하는지(0=중심, 1=제일 바깥).
    // 값을 낮출수록 팔뚝 안쪽까지 붉게 번진다.
    [Export(PropertyHint.Range, "0.1,0.9")] public float HandReach = 0.38f;

    public override void _Ready()
    {
        foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
            if (node is MeshInstance3D mi && mi.Mesh != null)
                BakeAndAssign(mi);
    }

    private void BakeAndAssign(MeshInstance3D mi)
    {
        var src = mi.Mesh;
        var aabb = src.GetAabb();
        Vector3 mn = aabb.Position;
        Vector3 sz = aabb.Size;
        Vector3 inv = new(
            sz.X > 1e-4f ? 1f / sz.X : 0f,
            sz.Y > 1e-4f ? 1f / sz.Y : 0f,
            sz.Z > 1e-4f ? 1f / sz.Z : 0f);

        var baked = new ArrayMesh();
        for (int s = 0; s < src.GetSurfaceCount(); s++)
        {
            var arrays = src.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (verts.Length == 0) continue;

            var colors = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = (verts[i] - mn) * inv;                       // 0..1 per axis
                float feet = Smooth(TipReach, 0f, n.Y);                  // 바닥 쪽 = 발끝

                // 손: 이 모델은 팔을 좌우로 펼치고 있으므로 "중심축에서 X 로 멀리 뻗은 위쪽"이 손이다.
                // Z(몸 두께)는 쓰지 않는다 — Z 범위가 곧 몸통 두께라, 섞으면 가슴/등까지 물든다.
                float lateral = Mathf.Abs(n.X - 0.5f) * 2f;              // 0=중심축, 1=제일 바깥
                // 높이 게이트는 '위로 갈수록 1'. 다리·발은 feet 가 이미 담당하므로 아래쪽은 뺀다.
                float upper = Smooth(0.45f, 0.62f, n.Y);
                float hands = Smooth(HandReach, HandReach + 0.18f, lateral) * upper;

                float tip = Mathf.Clamp(feet + hands, 0f, 1f);
                colors[i] = BodyColor.Lerp(TipColor, Mathf.Pow(tip, 1.1f));
            }
            arrays[(int)Mesh.ArrayType.Color] = colors;
            baked.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }

        if (baked.GetSurfaceCount() == 0) return;
        mi.Mesh = baked;
        mi.MaterialOverride = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            Roughness = Roughness,
            Metallic = 0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
