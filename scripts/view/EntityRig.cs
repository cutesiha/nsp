using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 괴물(entity.glb)을 팔·다리가 따로 움직이는 관절 구조로 쪼갠다.
//
// 원본 모델에는 뼈도 AnimationPlayer 도 없다. 그래서 통째로 기울이거나 늘리는 것 말고는
// 할 수 있는 게 없었고, 걷는 것도 미끄러지는 것처럼 보이고 "머리를 감싸 쥐는" 동작도
// 실루엣을 찌그러뜨리는 흉내에 그쳤다.
//
// 여기서 하는 일은 **메시를 잘라 관절 밑으로 옮기는 것**뿐이다. 직원 CCTV 캐릭터와
// 같은 방식이다 — 살에 스킨 웨이트가 없고, 부위마다 부모 관절(Node3D)의 회전을 따라간다.
//
//   Root
//    ├ Body            (몸통 + 머리 — 움직이지 않는다)
//    ├ HipL → KneeL    (왼 허벅지 → 왼 정강이)
//    ├ HipR → KneeR
//    ├ ShoulderL → ElbowL  (왼 위팔 → 왼 아래팔·손)
//    └ ShoulderR → ElbowR
//
// 이 모델은 팔을 좌우로 **펼치고** 있다(EntityGhost 의 손끝 판정과 같은 전제).
// 그래서 팔을 드는 것은 어깨의 z 회전이고, 팔꿈치를 접는 것도 z 회전이다.
public sealed class EntityRig
{
    // 부위를 가르는 경계(모델 AABB 를 0~1 로 정규화한 좌표).
    // 원화가 바뀌면 이 여섯 값만 만지면 된다.
    // (실측: 이 원화는 발이 0~10%, 다리가 10~50%, 골반이 50~60%, 그 위가 몸통·팔이다.
    //  발은 좌우로 넓게 퍼져 있어 "중심축 가까이" 로 거르면 몸통에 남는다 — 그래서
    //  다리 판정에는 좌우 폭을 쓰지 않는다.)
    private const float LegTopY = 0.50f;      // 이 아래가 다리(발 포함)
    private const float KneeY = 0.30f;        // 이 아래가 정강이 + 발
    private const float ArmBottomY = 0.50f;   // 이 위가 팔
    private const float ArmInner = 0.36f;     // 중심축에서 이만큼 벌어지면 팔
    private const float ElbowLateral = 0.66f; // 이보다 더 바깥이면 아래팔·손

    // 웅크릴 때 몸이 내려앉는 높이(모델 단위 — 쓰는 쪽에서 Scale 을 곱한다).
    // 허벅지·정강이 길이에서 계산한 값이다: 서 있을 때 0.50, 접었을 때 약 0.33.
    public const float CrouchDrop = 0.165f;

    private Node3D _hipL, _hipR, _kneeL, _kneeR;
    private Node3D _shoulderL, _shoulderR, _elbowL, _elbowR;
    private readonly List<Node3D> _joints = new();

    public bool Valid => _hipL != null && _shoulderL != null;

    // 모델을 쪼갠다. 실패하면 원본을 그대로 두고 Valid = false 로 남는다.
    public static EntityRig Build(Node3D root)
    {
        var rig = new EntityRig();
        if (root == null) return rig;

        MeshInstance3D source = null;
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0) { source = mi; break; }
        if (source == null) return rig;

        var aabb = source.Mesh.GetAabb();
        if (aabb.Size.Y < 0.001f) return rig;
        rig.Split(root, source, aabb);
        return rig;
    }

    // --- 자르기 -----------------------------------------------------------

    private enum Part { Body, UpperLegL, LowerLegL, UpperLegR, LowerLegR, UpperArmL, ForeArmL, UpperArmR, ForeArmR }

    private void Split(Node3D root, MeshInstance3D source, Aabb aabb)
    {
        Vector3 mn = aabb.Position, sz = aabb.Size;
        Vector3 inv = new(1f / Mathf.Max(1e-4f, sz.X), 1f / Mathf.Max(1e-4f, sz.Y), 1f / Mathf.Max(1e-4f, sz.Z));
        float cz = mn.Z + sz.Z * 0.5f;

        // 관절 위치(모델 좌표). 왼쪽은 -X, 오른쪽은 +X.
        float midX = mn.X + sz.X * 0.5f;
        Vector3 HipAt(float s) => new(midX + s * sz.X * 0.075f, mn.Y + sz.Y * LegTopY, cz);
        Vector3 KneeAt(float s) => new(midX + s * sz.X * 0.075f, mn.Y + sz.Y * KneeY, cz);
        float armY = mn.Y + sz.Y * 0.74f;
        Vector3 ShoulderAt(float s) => new(midX + s * sz.X * (ArmInner * 0.5f), armY, cz);
        Vector3 ElbowAt(float s) => new(midX + s * sz.X * (ElbowLateral * 0.5f), armY, cz);

        var origin = new Dictionary<Part, Vector3>
        {
            [Part.Body] = Vector3.Zero,
            [Part.UpperLegL] = HipAt(-1f), [Part.LowerLegL] = KneeAt(-1f),
            [Part.UpperLegR] = HipAt(+1f), [Part.LowerLegR] = KneeAt(+1f),
            [Part.UpperArmL] = ShoulderAt(-1f), [Part.ForeArmL] = ElbowAt(-1f),
            [Part.UpperArmR] = ShoulderAt(+1f), [Part.ForeArmR] = ElbowAt(+1f),
        };

        var builders = new Dictionary<Part, SurfaceTool>();
        foreach (Part p in System.Enum.GetValues<Part>())
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            builders[p] = st;
        }

        var arrays = source.Mesh.SurfaceGetArrays(0);
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int triCount = (index.Length > 0 ? index.Length : verts.Length) / 3;
        if (triCount == 0) return;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = index.Length > 0 ? index[t * 3] : t * 3;
            int i1 = index.Length > 0 ? index[t * 3 + 1] : t * 3 + 1;
            int i2 = index.Length > 0 ? index[t * 3 + 2] : t * 3 + 2;
            // 삼각형 하나는 통째로 한 부위에 들어간다 — 정점마다 나누면 면이 찢어진다.
            var center = (verts[i0] + verts[i1] + verts[i2]) / 3f;
            var part = Classify((center - mn) * inv);
            var st = builders[part];
            Vector3 o = origin[part];
            foreach (int i in stackalloc[] { i0, i1, i2 })
            {
                if (normals.Length > i) st.SetNormal(normals[i]);
                if (colors.Length > i) st.SetColor(colors[i]);
                st.AddVertex(verts[i] - o);   // 관절 기준 좌표로 옮긴다
            }
        }

        var material = source.MaterialOverride ?? source.Mesh.SurfaceGetMaterial(0);
        source.Visible = false;

        Node3D Make(Part part, Node3D parent, Vector3 worldOrigin)
        {
            var joint = new Node3D { Name = part.ToString(), Position = worldOrigin - PositionOf(parent, origin) };
            parent.AddChild(joint);
            var mesh = builders[part].Commit();
            if (mesh != null && mesh.GetSurfaceCount() > 0)
            {
                var mi = new MeshInstance3D { Name = part + "_Mesh", Mesh = mesh };
                if (material != null) mi.MaterialOverride = material;
                joint.AddChild(mi);
            }
            return joint;
        }

        // 몸통은 관절이 아니다 — 원점에 그대로 둔다.
        Make(Part.Body, root, Vector3.Zero);

        _hipL = Make(Part.UpperLegL, root, origin[Part.UpperLegL]);
        _kneeL = Make(Part.LowerLegL, _hipL, origin[Part.LowerLegL]);
        _hipR = Make(Part.UpperLegR, root, origin[Part.UpperLegR]);
        _kneeR = Make(Part.LowerLegR, _hipR, origin[Part.LowerLegR]);
        _shoulderL = Make(Part.UpperArmL, root, origin[Part.UpperArmL]);
        _elbowL = Make(Part.ForeArmL, _shoulderL, origin[Part.ForeArmL]);
        _shoulderR = Make(Part.UpperArmR, root, origin[Part.UpperArmR]);
        _elbowR = Make(Part.ForeArmR, _shoulderR, origin[Part.ForeArmR]);

        _joints.Clear();
        _joints.AddRange(new[] { _hipL, _kneeL, _hipR, _kneeR, _shoulderL, _elbowL, _shoulderR, _elbowR });
    }

    // 부모 관절의 모델 좌표(자식 관절의 상대 위치를 구하려고).
    private static Vector3 PositionOf(Node3D parent, Dictionary<Part, Vector3> origin)
    {
        foreach (var (part, pos) in origin)
            if (parent.Name == part.ToString()) return pos;
        return Vector3.Zero;   // root
    }

    private static Part Classify(Vector3 n)
    {
        float lateral = Mathf.Abs(n.X - 0.5f) * 2f;
        bool left = n.X < 0.5f;

        // 팔 — 위쪽에서 좌우로 뻗어 나간 부분.
        if (n.Y > ArmBottomY && lateral > ArmInner)
        {
            if (lateral > ElbowLateral) return left ? Part.ForeArmL : Part.ForeArmR;
            return left ? Part.UpperArmL : Part.UpperArmR;
        }
        // 다리 — 골반 아래는 전부. 발이 좌우로 퍼져 있어도 다리와 함께 움직여야 한다.
        if (n.Y < LegTopY)
        {
            if (n.Y < KneeY) return left ? Part.LowerLegL : Part.LowerLegR;
            return left ? Part.UpperLegL : Part.UpperLegR;
        }
        return Part.Body;
    }

    // --- 자세 -------------------------------------------------------------

    public void Reset()
    {
        foreach (var j in _joints) if (j != null) j.Rotation = Vector3.Zero;
    }

    // 걷기. phase 는 계속 증가하는 값(초 단위 × 속도), amount 0~1 로 보폭을 줄인다.
    // 다리는 엇갈려 앞뒤로 흔들리고, 뒤로 간 다리만 무릎이 접힌다. 팔도 반대로 조금 흔든다.
    public void SetWalk(float phase, float amount)
    {
        if (!Valid) return;
        amount = Mathf.Clamp(amount, 0f, 1f);
        float swing = Mathf.Sin(phase) * 0.55f * amount;
        float swingBack = -swing;

        _hipL.Rotation = new Vector3(swing, 0f, 0f);
        _hipR.Rotation = new Vector3(swingBack, 0f, 0f);
        // 무릎은 다리가 뒤로 갈 때만 접힌다(앞으로 뻗는 다리는 곧게).
        // 이 원화는 다리가 가늘어서 크게 접으면 정강이와 허벅지 사이가 벌어져 보인다 —
        // 걸을 때는 얕게만 접는다(웅크릴 때는 SetCrouch 가 깊게 접는다).
        _kneeL.Rotation = new Vector3(-Mathf.Max(0f, -swing) * 0.85f, 0f, 0f);
        _kneeR.Rotation = new Vector3(-Mathf.Max(0f, -swingBack) * 0.85f, 0f, 0f);

        // 팔은 다리와 반대로, 훨씬 작게. 펼친 팔이라 앞뒤(x)로만 흔든다.
        _shoulderL.Rotation = new Vector3(swingBack * 0.35f, 0f, 0f);
        _shoulderR.Rotation = new Vector3(swing * 0.35f, 0f, 0f);
        _elbowL.Rotation = _elbowR.Rotation = Vector3.Zero;
    }

    // 가만히 서 있을 때의 숨. 관절을 아주 조금만 쓴다.
    public void SetIdle(float phase)
    {
        if (!Valid) return;
        float s = Mathf.Sin(phase) * 0.05f;
        _hipL.Rotation = new Vector3(s, 0f, 0f);
        _hipR.Rotation = new Vector3(-s, 0f, 0f);
        _kneeL.Rotation = _kneeR.Rotation = Vector3.Zero;
        _shoulderL.Rotation = new Vector3(0f, 0f, -s * 0.6f);
        _shoulderR.Rotation = new Vector3(0f, 0f, s * 0.6f);
        _elbowL.Rotation = _elbowR.Rotation = Vector3.Zero;
    }

    // 웅크린 자세 — 무릎을 접고 팔을 몸 쪽으로 모은다.
    public void SetCrouch(float amount)
    {
        if (!Valid) return;
        amount = Mathf.Clamp(amount, 0f, 1f);
        _hipL.Rotation = new Vector3(0.9f * amount, 0f, 0f);
        _hipR.Rotation = new Vector3(0.9f * amount, 0f, 0f);
        _kneeL.Rotation = new Vector3(-1.7f * amount, 0f, 0f);
        _kneeR.Rotation = new Vector3(-1.7f * amount, 0f, 0f);
        // 펼친 팔을 몸 쪽으로 내린다(왼팔 +z, 오른팔 -z 가 아래 방향).
        _shoulderL.Rotation = new Vector3(0f, 0f, 0.7f * amount);
        _shoulderR.Rotation = new Vector3(0f, 0f, -0.7f * amount);
        _elbowL.Rotation = new Vector3(0f, 0f, 0.5f * amount);
        _elbowR.Rotation = new Vector3(0f, 0f, -0.5f * amount);
    }

    // 머리를 감싸 쥔다 — 어깨를 들어 팔을 위로 세우고, 팔꿈치를 접어 손을 머리로 가져간다.
    // amount 0 = 평소, 1 = 두 손이 머리를 완전히 감싼 자세.
    public void SetHeadGrab(float amount)
    {
        if (!Valid) return;
        amount = Mathf.Clamp(amount, 0f, 1f);
        // 펼친 팔이므로 어깨의 z 회전이 곧 "든다" 다. 왼팔은 -z, 오른팔은 +z 가 위쪽.
        _shoulderL.Rotation = new Vector3(0.25f * amount, 0f, -1.15f * amount);
        _shoulderR.Rotation = new Vector3(0.25f * amount, 0f, 1.15f * amount);
        // 팔꿈치를 더 접어 손이 머리 옆으로 온다.
        _elbowL.Rotation = new Vector3(0f, 0f, -1.25f * amount);
        _elbowR.Rotation = new Vector3(0f, 0f, 1.25f * amount);
        // 다리는 버티느라 조금 굽는다.
        _hipL.Rotation = new Vector3(0.35f * amount, 0f, 0f);
        _hipR.Rotation = new Vector3(0.35f * amount, 0f, 0f);
        _kneeL.Rotation = new Vector3(-0.6f * amount, 0f, 0f);
        _kneeR.Rotation = new Vector3(-0.6f * amount, 0f, 0f);
    }

    // 떨림 — 지금 자세 위에 잔진동만 얹는다(SetHeadGrab 뒤에 부른다).
    public void AddTremble(float phase, float amount)
    {
        if (!Valid || amount <= 0f) return;
        float a = Mathf.Sin(phase * 37f) * 0.10f * amount;
        float b = Mathf.Sin(phase * 29f + 1.3f) * 0.10f * amount;
        _shoulderL.Rotation += new Vector3(a, b, 0f);
        _shoulderR.Rotation += new Vector3(b, a, 0f);
        _elbowL.Rotation += new Vector3(b, 0f, a);
        _elbowR.Rotation += new Vector3(a, 0f, b);
        _kneeL.Rotation += new Vector3(a * 0.5f, 0f, 0f);
        _kneeR.Rotation += new Vector3(b * 0.5f, 0f, 0f);
    }
}
