using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 의자에 앉은 관리자 캐릭터. 팔·손·손가락은 씬(MainScene3D_Test)의 Skeleton3D "Rig" 밑에
// 마디별 BoneAttachment3D + MeshInstance3D 노드로 깔려 있고(A_*/M_*), 손 모양·굵기·팔 셔츠
// 머티리얼을 에디터에서 바로 편집한다.
//
// 애니메이션은 "관절별 FK 포즈" 다 — Shoulder / UpperArm / Forearm / Hand / Fingers 를
// 각각 독립 회전한다. 팔 전체 Position 을 목표까지 통째로 옮기지 않는다. 접촉 정확도는
// 팔꿈치(Forearm)+손목(Hand)만 살짝 보정(AimForearm)해서 맞춘다 — 어깨/상완은 포즈 그대로.
// [Tool] — 에디터 뷰포트에서 PosePreview 로 각 자세를 눈으로 확인/튜닝할 수 있다.
// 게임 판정과 완전히 분리 — Phone3D / PowerSwitchPanel 신호에 포즈만 바꾼다.
[Tool]
public partial class PlayerCharacter : Node3D
{
    [Export] public Material ShirtMaterial;
    [Export] public Material SkinMaterial;
    [Export] public Material CuffMaterial;

    private bool _rebuild;
    // 인스펙터에서 체크하면 스킨 메시를 지우고 현재 Rig 기준으로 다시 만든다(에디터 전용).
    [Export] public bool RebuildSkin
    {
        get => _rebuild;
        set { _rebuild = false; if (Engine.IsEditorHint()) RegenSkin(); }
    }

    // ── 에디터 포즈 미리보기 ──────────────────────────────────────
    public enum PosePreviewKind { None, SeatedIdle, PhoneReach, PhoneGrip, PhoneCall, SwitchReady, SwitchOff, SwitchOn }
    private PosePreviewKind _preview;
    [Export] public PosePreviewKind PosePreview
    {
        get => _preview;
        set { _preview = value; if (Engine.IsEditorHint()) ApplyPreview(); }
    }

    // ── 포즈 튜닝값 (인스펙터에서 조정하고 PosePreview 로 확인) ────
    // 회전 규약: 뼈 rest 는 전부 -Y(아래). 로컬 X 회전 +값 = 뼈 끝이 앞(-Z)/위로 스윙.
    // 즉 UpperArm +40 = 상완을 앞으로, Forearm +140 = 팔꿈치 완전히 접힘(손이 얼굴로).
    [ExportGroup("팔 포즈 튜닝")]
    [Export] public float IdleElbowDeg = 58f;
    [Export] public float ReachElbowDeg = 34f;            // reach 는 팔꿈치가 펴진다(작은 값)
    [Export] public float CallElbowDeg = 66f;             // call 팔꿈치 굽힘 바이어스(나머지는 IK)
    [Export] public float SwitchElbowDeg = 40f;
    [Export] public float UpperArmForwardDeg = 26f;       // reach 때 상완이 앞으로 스윙하는 각(작게=팔꿈치가 몸 옆·아래)
    [Export] public float CallUpperArmDeg = -18f;          // call 때 상완이 얼굴 쪽(뒤)으로 살짝 스윙
    [Export] public Vector3 ReachShoulderShift = new(0f, -0.01f, -0.05f);
    [Export] public Vector3 SwitchShoulderShift = new(0.02f, -0.01f, -0.04f);
    // IK의 간이 2관절 계산과 실제 손가락 계층 사이의 오차를 보정해 검지 끝을 레버에 붙인다.
    [Export] public Vector3 SwitchAimCorrectionWorld = new(-0.025f, 0.040f, 0.001f);
    // call 때 어깨(팔 고정축)를 몸 쪽으로 크게 당긴다 — 수화기를 귀로 가져오는 건 팔+어깨가 같이 후퇴.
    [Export] public Vector3 CallShoulderShift = new(0f, -0.015f, 0.04f);
    [Export] public Vector3 ReachWrist = new(-12f, -6f, 0f);
    // 기존 전화 포즈의 손목 각도를 유지한다. 이 방향에서는 오른손의 손바닥이
    // 카메라 쪽을 향하고, 수화기를 쥘 때 손등이 먼저 보이지 않는다.
    [Export] public Vector3 GripWrist = new(-24f, 0f, 0f);
    [Export] public Vector3 CallWrist = new(-46f, 24f, 16f);
    [Export] public float GripCurlIndex = 40f;
    [Export] public float GripCurlMiddle = 66f;
    [Export] public float GripCurlRing = 68f;
    [Export] public float GripCurlPinky = 54f;
    [Export] public float GripThumbCurl = 34f;
    [Export] public float GripThumbOpp = 30f;
    [Export] public Vector3 PalmGripOffset = new(0f, -0.05f, 0.01f);   // Hand_R 로컬 손바닥 그립점
    [Export] public float UpperArmGiveDeg = 90f;          // IK 가 상완을 틀 수 있는 전역 상한(스텝별로 더 좁힘)
    [Export] public float MaxAimDeg = 175f;               // IK 가 팔꿈치를 굽힐 수 있는 전역 상한
    [Export] public bool DebugMarkers;

    private Skeleton3D _skel;
    private readonly Dictionary<string, int> _bone = new();
    private readonly Dictionary<int, Vector3> _globalPos = new();
    private Quaternion[] _restLocalRot;

    public Node3D HandSocket { get; private set; }        // Rig/A_Hand_R/HandSocket (수화기 부착점)
    public Node3D HandSocketL { get; private set; }       // Rig/A_Hand_L/HandSocketL (관리자 패드 부착점)

    public override void _Ready()
    {
        _skel = GetNodeOrNull<Skeleton3D>("Rig");
        if (_skel == null)
        {
            GD.PushWarning("PlayerCharacter: 자식 Skeleton3D 'Rig' 를 찾지 못함 — 씬에 추가되어 있어야 함.");
            return;
        }

        BindBones();
        HandSocket = _skel.GetNodeOrNull<Node3D>("A_Hand_R/HandSocket");
        if (HandSocket == null && _skel.GetNodeOrNull<Node3D>("A_Hand_R") is { } aHand)
        {
            HandSocket = new Marker3D { Name = "HandSocket", Position = new Vector3(0f, -0.05f, 0.01f) };
            aHand.AddChild(HandSocket);
        }
        // 왼손 소켓(관리자 패드) — 오른손 소켓을 좌우로 뒤집은 자리. 씬에 있으면 그것을 쓴다.
        HandSocketL = _skel.GetNodeOrNull<Node3D>("A_Hand_L/HandSocketL");
        if (HandSocketL == null && _skel.GetNodeOrNull<Node3D>("A_Hand_L") is { } aHandL)
        {
            var src = HandSocket?.Transform ?? new Transform3D(Basis.Identity, PalmGripOffset);
            HandSocketL = new Marker3D { Name = "HandSocketL", Position = MirrorX(src.Origin) };
            aHandL.AddChild(HandSocketL);
        }
        // 씬에 마디 노드(BoneAttachment3D)가 하나도 없는 구버전에서만 코드로 스킨을 만든다.
        if (!HasAuthoredLimbs() && SkinCount() == 0) BuildSkinMesh();

        // 마디 노드를 왼팔/오른팔로 나눠 둔다 — 두 팔이 따로 나타나고 사라진다.
        CollectSideNodes();

        InitPose(_armR);
        InitPose(_armL);
        // 손은 평소 절대 안 보인다 — 전화/스위치/관리자 패드 상호작용 때만 나온다.
        SetArmVisible(_armR, false);
        SetArmVisible(_armL, false);
    }

    private readonly Dictionary<string, List<Node3D>> _sideNodes = new() { ["R"] = new(), ["L"] = new() };

    private void CollectSideNodes()
    {
        _sideNodes["R"].Clear();
        _sideNodes["L"].Clear();
        foreach (var n in _skel.GetChildren())
        {
            if (n is not Node3D n3) continue;
            string name = n.Name.ToString();
            if (name.EndsWith("_L") || name.Contains("_L_")) _sideNodes["L"].Add(n3);
            else if (name.EndsWith("_R") || name.Contains("_R_")) _sideNodes["R"].Add(n3);
        }
    }

    private void RegenSkin()
    {
        _skel ??= GetNodeOrNull<Skeleton3D>("Rig");
        if (_skel == null) return;
        if (HasAuthoredLimbs())
        {
            GD.PushWarning("PlayerCharacter: 씬에 마디 노드(A_*)가 있어 코드 스킨 생성을 건너뜀. " +
                           "손 모양은 M_* MeshInstance3D 의 mesh 를 인스펙터에서 수정하세요.");
            return;
        }
        foreach (var c in _skel.GetChildren())
            if (c is MeshInstance3D) c.QueueFree();
        BindBones();
        BuildSkinMesh();
    }

    private int SkinCount()
    {
        int c = 0;
        foreach (var n in _skel.GetChildren()) if (n is MeshInstance3D) c++;
        return c;
    }

    // 씬에 마디별 BoneAttachment3D(A_*) 가 깔려 있으면 코드 생성은 건너뛴다.
    private bool HasAuthoredLimbs()
    {
        foreach (var n in _skel.GetChildren()) if (n is BoneAttachment3D) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Rig(씬의 Skeleton3D) 바인딩
    // ─────────────────────────────────────────────────────────────
    private void BindBones()
    {
        _skel.ResetBonePoses();
        _bone.Clear();
        _globalPos.Clear();

        int n = _skel.GetBoneCount();
        _restLocalRot = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            _bone[_skel.GetBoneName(i)] = i;
            _restLocalRot[i] = _skel.GetBoneRest(i).Basis.GetRotationQuaternion();
            _globalPos[i] = BoneGlobalRestPos(i);
        }
    }

    private Vector3 BoneGlobalRestPos(int i)
    {
        var p = Vector3.Zero;
        while (i >= 0) { p += _skel.GetBoneRest(i).Origin; i = _skel.GetBoneParent(i); }
        return p;
    }

    // ─────────────────────────────────────────────────────────────
    //  스킨 메시 (구버전 폴백) — Rig 뼈 위치 기준으로 강체 바인딩
    // ─────────────────────────────────────────────────────────────

    private static readonly (string name, float x, float[] seg, float w)[] Fingers =
    {
        ("Index",  0.032f, new[] { 0.034f, 0.023f, 0.018f }, 0.0135f),
        ("Middle", 0.010f, new[] { 0.038f, 0.027f, 0.020f }, 0.0140f),
        ("Ring",  -0.012f, new[] { 0.034f, 0.025f, 0.018f }, 0.0130f),
        ("Pinky", -0.033f, new[] { 0.026f, 0.019f, 0.015f }, 0.0110f),
    };

    private enum Mk { Shirt, Skin, Cuff }

    private void BuildSkinMesh()
    {
        var shirt = ShirtMaterial ?? Mat(new Color(0.34f, 0.37f, 0.45f), 0.85f);
        var skin = SkinMaterial ?? Mat(new Color(0.74f, 0.57f, 0.48f), 0.6f);
        var cuff = CuffMaterial ?? Mat(new Color(0.26f, 0.29f, 0.37f), 0.8f);
        Material MatOf(Mk m) => m == Mk.Shirt ? shirt : m == Mk.Cuff ? cuff : skin;

        var seg = CollectSegments();

        var stByMat = new Dictionary<Mk, SurfaceTool>();
        foreach (var g in seg)
        {
            if (!stByMat.TryGetValue(g.mk, out var st))
            {
                st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                stByMat[g.mk] = st;
            }
            AddTaperedBox(st, _bone.GetValueOrDefault(g.bone, 0), g.a, g.b, g.w0, g.t0, g.w1, g.t1);
        }

        var skinRes = _skel.CreateSkinFromRestTransforms();
        foreach (var (mk, st) in stByMat)
        {
            st.GenerateNormals();
            var mi = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = MatOf(mk), Skin = skinRes };
            _skel.AddChild(mi);
            mi.Skeleton = mi.GetPathTo(_skel);
        }
    }

    private static StandardMaterial3D Mat(Color c, float rough) =>
        new() { AlbedoColor = c, Roughness = rough, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    private Vector3 P(string bone) => _globalPos.GetValueOrDefault(_bone.GetValueOrDefault(bone, -1));

    private List<(string bone, Vector3 a, Vector3 b, float w0, float t0, float w1, float t1, Mk mk)> CollectSegments()
    {
        var seg = new List<(string, Vector3, Vector3, float, float, float, float, Mk)>();

        for (int s = -1; s <= 1; s += 2)
        {
            string sd = s < 0 ? "L" : "R";
            Vector3 sh = P($"Shoulder_{sd}"), el = P($"Forearm_{sd}"), wr = P($"Hand_{sd}");

            seg.Add(($"UpperArm_{sd}", sh, el, 0.05f, 0.05f, 0.042f, 0.042f, Mk.Shirt));
            seg.Add(($"Forearm_{sd}", el, wr, 0.042f, 0.042f, 0.032f, 0.030f, Mk.Shirt));
            seg.Add(($"Forearm_{sd}", el.Lerp(wr, 0.82f), wr, 0.046f, 0.044f, 0.044f, 0.042f, Mk.Cuff));

            var knuckle = wr + new Vector3(0.006f * s, -0.052f, 0.006f);
            seg.Add(($"Hand_{sd}", wr, knuckle, 0.062f, 0.026f, 0.084f, 0.024f, Mk.Skin));

            foreach (var (fn, _, fs, fw) in Fingers)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = P($"{fn}_{sd}_{k + 1}");
                    Vector3 b = a + new Vector3(0f, -fs[k], 0f);
                    float w0 = fw * (1f - 0.10f * k), w1 = fw * (1f - 0.10f * (k + 1));
                    seg.Add(($"{fn}_{sd}_{k + 1}", a, b, w0, w0 * 0.85f, w1, w1 * 0.85f, Mk.Skin));
                }

            Vector3 tb = P($"Thumb_{sd}_1"), tt = P($"Thumb_{sd}_2");
            seg.Add(($"Thumb_{sd}_1", tb, tt, 0.016f, 0.015f, 0.014f, 0.013f, Mk.Skin));
            seg.Add(($"Thumb_{sd}_2", tt, tt + new Vector3(0.016f * s, -0.020f, -0.004f), 0.013f, 0.012f, 0.010f, 0.010f, Mk.Skin));
        }
        return seg;
    }

    private static void AddTaperedBox(SurfaceTool st, int bone, Vector3 a, Vector3 b, float w0, float t0, float w1, float t1)
    {
        Vector3 axis = (b - a);
        float len = axis.Length();
        if (len < 1e-5f) return;
        axis /= len;
        Vector3 side = axis.Cross(Vector3.Forward);
        if (side.Length() < 1e-4f) side = axis.Cross(Vector3.Right);
        side = side.Normalized();
        Vector3 fwd = axis.Cross(side).Normalized();

        Vector3[] ring0 =
        {
            a + side * (w0 * 0.5f) + fwd * (t0 * 0.5f),
            a - side * (w0 * 0.5f) + fwd * (t0 * 0.5f),
            a - side * (w0 * 0.5f) - fwd * (t0 * 0.5f),
            a + side * (w0 * 0.5f) - fwd * (t0 * 0.5f),
        };
        Vector3[] ring1 =
        {
            b + side * (w1 * 0.5f) + fwd * (t1 * 0.5f),
            b - side * (w1 * 0.5f) + fwd * (t1 * 0.5f),
            b - side * (w1 * 0.5f) - fwd * (t1 * 0.5f),
            b + side * (w1 * 0.5f) - fwd * (t1 * 0.5f),
        };

        void V(Vector3 p)
        {
            st.SetBones(new[] { bone, 0, 0, 0 });
            st.SetWeights(new[] { 1f, 0f, 0f, 0f });
            st.AddVertex(p);
        }
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            V(p0); V(p1); V(p2);
            V(p0); V(p2); V(p3);
        }

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Quad(ring0[i], ring0[j], ring1[j], ring1[i]);
        }
        Quad(ring0[3], ring0[2], ring0[1], ring0[0]);
        Quad(ring1[0], ring1[1], ring1[2], ring1[3]);
    }

    // ═════════════════════════════════════════════════════════════
    //  팔/손 애니메이션 — 관절별 FK 포즈 + 시퀀스
    // ═════════════════════════════════════════════════════════════

    // 오른팔에서 포즈로 제어하는 뼈(부모→자식 순서).
    private static readonly string[] ArmBones =
    {
        "Shoulder_R", "UpperArm_R", "Forearm_R", "Hand_R",
        "Thumb_R_1", "Thumb_R_2",
        "Index_R_1", "Index_R_2", "Index_R_3",
        "Middle_R_1", "Middle_R_2", "Middle_R_3",
        "Ring_R_1", "Ring_R_2", "Ring_R_3",
        "Pinky_R_1", "Pinky_R_2", "Pinky_R_3",
    };

    private class ArmPose
    {
        public readonly Dictionary<string, Vector3> Rot = new();   // 뼈 로컬 회전(도)
        public Vector3 ShoulderShift;                              // Shoulder_R 위치 미세이동(m)
        public ArmPose S(string bone, float x, float y, float z) { Rot[bone] = new Vector3(x, y, z); return this; }
        public ArmPose Sh(Vector3 v) { ShoulderShift = v; return this; }
    }

    private static Quaternion QDeg(Vector3 d) => Quaternion.FromEuler(
        new Vector3(Mathf.DegToRad(d.X), Mathf.DegToRad(d.Y), Mathf.DegToRad(d.Z)));

    // ── 손 모양 헬퍼 ─────────────────────────────────────────────
    private static void RelaxHand(ArmPose p, float c = 11f)
    {
        foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
            p.S($"{f}_R_1", c, 0, 0).S($"{f}_R_2", c * 1.25f, 0, 0).S($"{f}_R_3", c * 0.8f, 0, 0);
        p.S("Thumb_R_1", -6, 0, -10).S("Thumb_R_2", -8, 0, 0);
    }

    private void GripHand(ArmPose p)
    {
        void C(string f, float a) => p.S($"{f}_R_1", a, 0, 0).S($"{f}_R_2", a * 1.15f, 0, 0).S($"{f}_R_3", a * 0.8f, 0, 0);
        C("Index", GripCurlIndex);
        C("Middle", GripCurlMiddle);
        C("Ring", GripCurlRing);
        C("Pinky", GripCurlPinky);
        p.S("Thumb_R_1", -6, 0, -GripThumbOpp).S("Thumb_R_2", -GripThumbCurl, 0, 0);
    }

    // 가볍게 주먹 + 검지만 편 손(스위치 조작용).
    private static void PointHand(ArmPose p)
    {
        void C(string f, float a) => p.S($"{f}_R_1", a, 0, 0).S($"{f}_R_2", a * 1.1f, 0, 0).S($"{f}_R_3", a * 0.85f, 0, 0);
        C("Middle", 78); C("Ring", 80); C("Pinky", 74);
        p.S("Thumb_R_1", -10, 0, 6).S("Thumb_R_2", -36, 0, 0);
        p.S("Index_R_1", 5, 0, 0).S("Index_R_2", 6, 0, 0).S("Index_R_3", 4, 0, 0);
    }

    // ── 5개 기본 포즈 ───────────────────────────────────────────
    private ArmPose PoseIdle()
    {
        var p = new ArmPose()
            .S("Shoulder_R", 0, 0, 0)
            .S("UpperArm_R", 8, 0, 5)               // 상완 거의 수직(몸 옆), 아주 살짝 앞
            .S("Forearm_R", IdleElbowDeg, 0, -3)    // 팔꿈치 굽힘 — 전완이 앞으로(무릎 위)
            .S("Hand_R", 5, 0, 0);
        RelaxHand(p);
        return p;
    }

    private ArmPose PoseReach()
    {
        var p = new ArmPose()
            .Sh(ReachShoulderShift)
            .S("UpperArm_R", UpperArmForwardDeg, -7, 3)   // 상완이 앞으로 스윙(팔꿈치가 앞으로 옴)
            .S("Forearm_R", ReachElbowDeg, 0, -5)         // 팔꿈치는 펴진 상태 — 전완이 전화기로
            .S("Hand_R", ReachWrist.X, ReachWrist.Y, ReachWrist.Z);
        RelaxHand(p);
        return p;
    }

    private ArmPose PoseGrip()
    {
        var p = PoseReach();
        p.S("Hand_R", GripWrist.X, GripWrist.Y, GripWrist.Z);
        GripHand(p);
        return p;
    }

    private ArmPose PoseCall()
    {
        // 상완은 IK 가 자유롭게 접어 얼굴로 가져온다. FK 는 팔꿈치가 "바깥·아래"로 벌어지도록
        // 바이어스만 주고(전화 자세), 손목으로 수화기를 귀·입 방향으로 돌린다.
        var p = new ArmPose()
            .Sh(CallShoulderShift)                       // 팔 고정축을 몸 쪽으로 당김
            .S("UpperArm_R", CallUpperArmDeg, 4, 22)     // 살짝 바깥(팔꿈치가 벌어짐)
            .S("Forearm_R", CallElbowDeg, 0, -14)
            .S("Hand_R", CallWrist.X, CallWrist.Y, CallWrist.Z);
        GripHand(p);
        return p;
    }

    // 수화기 든 채 받침대로 (전화받기 역재생이 아님 — 손목 정렬이 다르다).
    private ArmPose PoseHangReach()
    {
        var p = PoseReach();
        p.S("Forearm_R", ReachElbowDeg + 2, 0, -3)
         .S("Hand_R", -8, -2, 0);
        GripHand(p);
        return p;
    }

    private ArmPose PoseSwitchReady(bool fromBelow)
    {
        var p = new ArmPose()
            .Sh(SwitchShoulderShift)
            .S("UpperArm_R", 34, -11, 2)                  // 상완이 앞으로(팔꿈치가 앞으로), 스위치는 오른쪽이라 살짝 바깥
            .S("Forearm_R", SwitchElbowDeg, -8, -3)
            .S("Hand_R", fromBelow ? -4 : -14, -12, 0);
        PointHand(p);
        return p;
    }

    private ArmPose PoseSwitchOff()   // 검지 끝마디만 아래로 (손목/전완 거의 고정)
    {
        var p = PoseSwitchReady(false);
        p.S("Index_R_1", 12, 0, 0).S("Index_R_2", 44, 0, 0).S("Index_R_3", 48, 0, 0);
        return p;
    }

    private ArmPose PoseSwitchOn()    // 검지를 위로 튕김
    {
        var p = PoseSwitchReady(true);
        p.S("Index_R_1", -16, 0, 0).S("Index_R_2", -4, 0, 0).S("Index_R_3", 0, 0, 0);
        return p;
    }

    // ── 시퀀스 ──────────────────────────────────────────────────
    private class Step
    {
        public ArmPose Pose;
        public float In = 0.4f;                 // 이 포즈로 블렌드하는 시간
        public float Hold;                      // 도달 후 유지
        public bool Stagger;                    // 손가락을 순차로 감/폄
        public System.Func<Vector3> Aim;        // 팔꿈치만 살짝 보정할 월드 목표(null이면 순수 FK)
        public Vector3 AimTip;                  // 손목 로컬 접촉점(손바닥/검지끝)
        public float AimMaxDeg = 170f;          // 이 스텝에서 팔꿈치가 굽을 수 있는 최대각
        public float UpperGiveDeg = 80f;        // 이 스텝에서 상완이 FK 에서 틀 수 있는 최대각(작게=어깨 더 고정)
        public System.Action OnArrive;          // In 끝나는 순간 1회 (내부 콜백)
        // 손(= 손 소켓)의 월드 방향 목표. 있으면 팔 IK 뒤에 손목을 돌려 이 방향에 정확히 맞춘다.
        // 물건을 "그 방향으로" 쥐어야 할 때(관리자 패드의 가장자리) 쓴다. null 이면 FK 손목 그대로.
        public System.Func<Basis> AimBasis;
    }

    // 팔 하나의 재생 상태. 오른팔(전화 · 스위치 · 패드 누르기)과 왼팔(패드 들기)은 따로 돈다 —
    // 왼손이 패드를 든 채 오른손 검지로 화면을 누를 수 있어야 한다.
    private sealed class ArmChannel
    {
        public readonly string Side;           // "R" / "L"
        public readonly string[] Bones;
        public List<Step> Seq;
        public int StepIdx;
        public float StepT;
        public bool StepArrived;
        public readonly Dictionary<string, Quaternion> PoseCur = new();
        public readonly Dictionary<string, Quaternion> PoseFrom = new();
        public Vector3 ShiftCur, ShiftFrom;
        public bool Visible;

        public ArmChannel(string side)
        {
            Side = side;
            Bones = side == "R" ? ArmBones : System.Array.ConvertAll(ArmBones, ToLeft);
        }

        // 오른팔 기준 뼈 이름 → 이 팔의 뼈 이름.
        public string B(string rightBone) => Side == "R" ? rightBone : ToLeft(rightBone);
    }

    private static string ToLeft(string rightBone) => rightBone.Replace("_R", "_L");

    private readonly ArmChannel _armR = new("R");
    private readonly ArmChannel _armL = new("L");

    // 도달한 뒤 계속 그 자세로 목표를 따라가는 스텝(패드를 들고 있는 동안).
    private const float HoldForever = 1e9f;

    private static readonly (string prefix, float lead)[] FingerLead =
    {
        ("Thumb", 0.02f), ("Index", 0f), ("Middle", 0.06f), ("Ring", 0.12f), ("Pinky", 0.18f),
    };

    // 오른팔 포즈 → 왼팔 포즈. 왼팔 뼈대는 오른팔을 X 로 뒤집은 것(rest 도 좌우 대칭)이라
    // 각 관절 회전은 Y · Z 성분만, 어깨 이동은 X 성분만 부호가 바뀐다.
    private static ArmPose Mirror(ArmPose p)
    {
        var m = new ArmPose { ShoulderShift = MirrorX(p.ShoulderShift) };
        foreach (var (bone, d) in p.Rot) m.Rot[ToLeft(bone)] = new Vector3(d.X, -d.Y, -d.Z);
        return m;
    }

    private static Vector3 MirrorX(Vector3 v) => new(-v.X, v.Y, v.Z);

    private void InitPose(ArmChannel ch)
    {
        var idle = ch.Side == "R" ? PoseIdle() : Mirror(PoseIdle());
        ch.PoseCur.Clear();
        foreach (var b in ch.Bones)
            ch.PoseCur[b] = idle.Rot.TryGetValue(b, out var d) ? QDeg(d) : Quaternion.Identity;
        ch.ShiftCur = Vector3.Zero;
    }

    private void StartSequence(List<Step> steps) => StartSequence(_armR, steps);

    // steps 는 언제나 오른팔 기준으로 쓴다. 왼팔이면 여기서 뒤집는다.
    private void StartSequence(ArmChannel ch, List<Step> steps)
    {
        if (_skel == null) return;
        if (ch.Side == "L")
            foreach (var s in steps)
            {
                s.Pose = Mirror(s.Pose);
                s.AimTip = MirrorX(s.AimTip);
            }
        if (!ch.Visible) InitPose(ch);          // 숨은 상태에서 시작하면 idle 부터
        SetArmVisible(ch, true);
        ch.Seq = steps;
        ch.StepIdx = 0;
        ch.StepT = 0f;
        ch.StepArrived = false;
        SnapshotFrom(ch);
    }

    private static void SnapshotFrom(ArmChannel ch)
    {
        ch.PoseFrom.Clear();
        foreach (var kv in ch.PoseCur) ch.PoseFrom[kv.Key] = kv.Value;
        ch.ShiftFrom = ch.ShiftCur;
    }

    // 왼팔 메시만 따로 숨겨 두는 스위치(관리자 패드를 든 동안). 포즈 · 시퀀스는 그대로 돈다 —
    // 숨긴 사이에도 손은 패드를 계속 "들고" 있고, 다시 보이면 이어서 내려놓는 동작이 나온다.
    private bool _leftMeshHidden;

    public void SetLeftArmMeshHidden(bool hidden)
    {
        _leftMeshHidden = hidden;
        if (_skel == null) return;
        bool editor = Engine.IsEditorHint();
        foreach (var n in _sideNodes["L"])
            if (IsInstanceValid(n)) n.Visible = (_armL.Visible && !hidden) || editor;
    }

    private void SetArmVisible(ArmChannel ch, bool v)
    {
        ch.Visible = v;
        if (_skel == null) return;
        bool editor = Engine.IsEditorHint();
        bool show = v && !(ch.Side == "L" && _leftMeshHidden);
        foreach (var n in _sideNodes[ch.Side])
            if (IsInstanceValid(n)) n.Visible = show || editor;
        _skel.Visible = _armR.Visible || _armL.Visible || editor;
    }

    private static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

    private void TickSequence(ArmChannel ch, float d)
    {
        if (ch.Seq == null) return;
        var step = ch.Seq[ch.StepIdx];
        ch.StepT += d;

        float baseF = step.In <= 0f ? 1f : Mathf.Clamp(ch.StepT / step.In, 0f, 1f);

        foreach (var b in ch.Bones)
        {
            Quaternion tq = step.Pose.Rot.TryGetValue(b, out var deg) ? QDeg(deg) : Quaternion.Identity;
            float f = baseF;
            if (step.Stagger && IsFinger(b))
            {
                float lead = FingerLeadOf(b);
                float span = Mathf.Max(0.06f, step.In - 0.20f);
                f = Mathf.Clamp((ch.StepT - lead) / span, 0f, 1f);
            }
            ch.PoseCur[b] = ch.PoseFrom.GetValueOrDefault(b, Quaternion.Identity).Slerp(tq, Smooth(f)).Normalized();
        }
        ch.ShiftCur = ch.ShiftFrom.Lerp(step.Pose.ShoulderShift, Smooth(baseF));

        // 접촉 보정 — 어깨(고정축)에서 상완은 조금, 팔꿈치가 주로 굽어 손이 목표에 닿는다.
        // 손목 방향까지 정해진 스텝이면, 접촉점(AimTip)이 목표에 오도록 **손목 자리**를 먼저 역산해
        // 팔은 손목을 거기로 보내고 손목이 방향을 맞춘다. (손목을 돌리면 손바닥 접촉점이 손목에서
        // 10cm 가까이 떨어진 채 같이 돌아가므로, 접촉점을 그대로 겨누면 한참 빗나간다.)
        Basis? aimBasis = step.AimBasis?.Invoke();
        if (step.Aim != null)
        {
            Vector3 target = step.Aim();
            Vector3 tip = step.AimTip;
            if (aimBasis is { } ab)
            {
                float sc = _skel.GlobalTransform.Basis.Scale.X;
                target -= ab.Orthonormalized() * (step.AimTip * sc);
                tip = Vector3.Zero;
            }
            SolveArm(ch, target, tip, Smooth(baseF),
                     Mathf.Min(UpperArmGiveDeg, step.UpperGiveDeg), Mathf.Min(MaxAimDeg, step.AimMaxDeg));
        }
        // 손목 방향 — 팔이 자리를 잡은 뒤, 손이 목표 방향을 향하도록 손목만 돌린다.
        if (aimBasis is { } basis)
            SolveWrist(ch, basis, Smooth(baseF));

        if (!ch.StepArrived && baseF >= 1f)
        {
            ch.StepArrived = true;
            step.OnArrive?.Invoke();
        }

        // OnArrive 안에서 같은 팔의 새 시퀀스가 시작됐으면 여기서 끝낸다.
        if (ch.Seq == null || ch.StepIdx >= ch.Seq.Count || ch.Seq[ch.StepIdx] != step) return;

        if (ch.StepArrived && ch.StepT >= step.In + step.Hold)
        {
            if (ch.StepIdx + 1 < ch.Seq.Count)
            {
                ch.StepIdx++;
                ch.StepT = 0f;
                ch.StepArrived = false;
                SnapshotFrom(ch);
            }
            else ch.Seq = null;   // 마지막 포즈에서 정지
        }
    }

    private static bool IsFinger(string b) =>
        b.StartsWith("Thumb") || b.StartsWith("Index") || b.StartsWith("Middle") || b.StartsWith("Ring") || b.StartsWith("Pinky");

    private static float FingerLeadOf(string b)
    {
        foreach (var (prefix, lead) in FingerLead) if (b.StartsWith(prefix)) return lead;
        return 0f;
    }

    // 제약 2본 IK. 어깨 위치는 고정축. FK 포즈가 팔의 "스타일"(reach=앞, call=뒤)을 정하고,
    // 여기서 상완은 UpperArmGiveDeg 안에서만 살짝 틀고 팔꿈치(Forearm)가 주로 굽어 손이 목표에 닿는다.
    // rest 회전 전부 항등 + skel basis 항등(ControlRoom/PlayerCharacter 회전 없음) 가정.
    private void SolveArm(ArmChannel ch, Vector3 target, Vector3 tipLocal, float weight, float maxUpperDev, float maxForeDev)
    {
        if (weight <= 0.001f || _skel == null) return;
        string sh = ch.B("Shoulder_R"), up = ch.B("UpperArm_R"), fore = ch.B("Forearm_R"), hand = ch.B("Hand_R");
        if (!_bone.ContainsKey(fore) || !_bone.ContainsKey("Chest") || !_bone.ContainsKey(hand)) return;

        Vector3 chest = _skel.GetBoneRest(_bone["Chest"]).Origin;
        Vector3 shOff = _skel.GetBoneRest(_bone[sh]).Origin + ch.ShiftCur;
        Vector3 shoulderW = _skel.GlobalTransform * (chest + shOff);
        float sc = _skel.GlobalTransform.Basis.Scale.X;   // 리그 스케일(균일 가정) — 팔 길이에 반영

        Quaternion qU = ch.PoseCur.GetValueOrDefault(up, Quaternion.Identity);
        Quaternion qF = ch.PoseCur.GetValueOrDefault(fore, Quaternion.Identity);

        Vector3 elbowOff = _skel.GetBoneRest(_bone[fore]).Origin;               // 상완 벡터(뼈공간)
        Vector3 handOff = _skel.GetBoneRest(_bone[hand]).Origin + tipLocal;     // 전완+팁 벡터
        float l1 = elbowOff.Length() * sc;
        float l2 = handOff.Length() * sc;

        Vector3 fkUpper = (new Basis(qU) * elbowOff).Normalized();
        Vector3 fkFore = (new Basis(qU * qF) * handOff).Normalized();

        Vector3 toT = target - shoulderW;
        float dist = Mathf.Clamp(toT.Length(), Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.004f);
        Vector3 dirT = toT.LengthSquared() > 1e-8f ? toT.Normalized() : fkUpper;

        float cosS = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
        float shoulderAng = Mathf.Acos(cosS);

        Vector3 bendAxis = dirT.Cross(fkUpper);
        if (bendAxis.LengthSquared() < 1e-6f) bendAxis = dirT.Cross(Vector3.Right);
        bendAxis = bendAxis.Normalized();

        Vector3 idealUpper = dirT.Rotated(bendAxis, shoulderAng);
        Vector3 solvedUpper = LimitDir(fkUpper, idealUpper, maxUpperDev);

        Vector3 elbowW = shoulderW + solvedUpper * l1;
        Vector3 idealFore = (target - elbowW).Normalized();
        // solvedUpper 로 상완이 돌아간 만큼 전완 FK 방향도 같이 돌려 기준을 잡는다
        Vector3 foreRef = (new Basis(new Quaternion(fkUpper, solvedUpper)) * fkFore).Normalized();
        Vector3 solvedFore = LimitDir(foreRef, idealFore, maxForeDev);

        Quaternion wU = new Quaternion(Vector3.Down, solvedUpper);
        Quaternion wF = new Quaternion(Vector3.Down, solvedFore);
        ch.PoseCur[up] = qU.Slerp(wU, weight).Normalized();
        ch.PoseCur[fore] = qF.Slerp((ch.PoseCur[up].Inverse() * wF).Normalized(), weight).Normalized();
    }

    // 손목 방향 IK — 손 뼈의 월드 회전이 target 이 되도록 손목(Hand) 로컬 회전만 정한다.
    // rest 회전이 전부 항등이라 월드 회전 = 리그 회전 × 가슴 × 어깨 × 상완 × 전완 × 손.
    private void SolveWrist(ArmChannel ch, Basis target, float weight)
    {
        if (weight <= 0.001f || _skel == null) return;
        string hand = ch.B("Hand_R");
        if (!_bone.ContainsKey(hand)) return;

        Quaternion parent = _skel.GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion();
        if (_bone.TryGetValue("Chest", out int chest)) parent *= _skel.GetBonePoseRotation(chest);
        foreach (string b in new[] { "Shoulder_R", "UpperArm_R", "Forearm_R" })
            parent *= ch.PoseCur.GetValueOrDefault(ch.B(b), Quaternion.Identity);

        Quaternion want = (parent.Inverse() * target.Orthonormalized().GetRotationQuaternion()).Normalized();
        ch.PoseCur[hand] = ch.PoseCur.GetValueOrDefault(hand, Quaternion.Identity).Slerp(want, weight).Normalized();
    }

    private static Vector3 LimitDir(Vector3 from, Vector3 to, float maxDeg)
    {
        from = from.Normalized();
        to = to.Normalized();
        float ang = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(from.Dot(to), -1f, 1f)));
        if (ang <= maxDeg || ang < 0.01f) return to;
        return from.Slerp(to, maxDeg / ang).Normalized();
    }

    // ─────────────────────────────────────────────────────────────
    //  _Process
    // ─────────────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        if (_skel == null || Engine.IsEditorHint()) return;

        TickSequence(_armR, (float)delta);
        TickSequence(_armL, (float)delta);
        PushArm(_armR);
        PushArm(_armL);
        TickDebug();
    }

    private void PushArm(ArmChannel ch)
    {
        foreach (var b in ch.Bones)
        {
            if (!_bone.TryGetValue(b, out int idx)) continue;
            _skel.SetBonePoseRotation(idx, ch.PoseCur.GetValueOrDefault(b, Quaternion.Identity).Normalized());
        }
        if (_bone.TryGetValue(ch.B("Shoulder_R"), out int sh))
            _skel.SetBonePosePosition(sh, _skel.GetBoneRest(sh).Origin + ch.ShiftCur);
    }

    private MeshInstance3D _palmDot;
    private void TickDebug()
    {
        if (!DebugMarkers)
        {
            if (_palmDot != null) _palmDot.Visible = false;
            return;
        }
        if (_palmDot == null)
        {
            _palmDot = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.008f, Height = 0.016f, RadialSegments = 6, Rings = 4 },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.2f, 1f), EmissionEnabled = true, Emission = new Color(1f, 0.2f, 1f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
                TopLevel = true,
            };
            AddChild(_palmDot);
        }
        _palmDot.Visible = _armR.Visible;
        _palmDot.GlobalPosition = PalmGripGlobal().Origin;
    }

    // ─────────────────────────────────────────────────────────────
    //  에디터 포즈 미리보기
    // ─────────────────────────────────────────────────────────────
    private void ApplyPreview()
    {
        _skel ??= GetNodeOrNull<Skeleton3D>("Rig");
        if (_skel == null) return;
        if (_bone.Count == 0) BindBones();
        _skel.ResetBonePoses();

        ArmPose p = _preview switch
        {
            PosePreviewKind.SeatedIdle => PoseIdle(),
            PosePreviewKind.PhoneReach => PoseReach(),
            PosePreviewKind.PhoneGrip => PoseGrip(),
            PosePreviewKind.PhoneCall => PoseCall(),
            PosePreviewKind.SwitchReady => PoseSwitchReady(false),
            PosePreviewKind.SwitchOff => PoseSwitchOff(),
            PosePreviewKind.SwitchOn => PoseSwitchOn(),
            _ => null,
        };
        if (p == null) return;

        foreach (var (b, deg) in p.Rot)
            if (_bone.TryGetValue(b, out int i)) _skel.SetBonePoseRotation(i, QDeg(deg));
        if (_bone.TryGetValue("Shoulder_R", out int sh))
            _skel.SetBonePosePosition(sh, _skel.GetBoneRest(sh).Origin + p.ShoulderShift);
    }

    // ─────────────────────────────────────────────────────────────
    //  공개 API
    // ─────────────────────────────────────────────────────────────
    [Signal] public delegate void PhoneGrippedEventHandler();     // 손가락이 수화기를 다 감은 순간
    [Signal] public delegate void PhoneReleasedEventHandler();    // 수화기를 받침대에 놓은 순간

    public bool IsHoldingPhone { get; private set; }

    // Hand_R 본의 현재 월드 트랜스폼.
    public Transform3D HandGripGlobal()
    {
        int h = _bone.GetValueOrDefault("Hand_R", -1);
        if (h < 0 || _skel == null) return GlobalTransform;
        return _skel.GlobalTransform * _skel.GetBoneGlobalPose(h);
    }

    // 손바닥 그립점(수화기를 여기에 맞춘다). HandSocket 노드가 있으면 그걸 우선.
    public Transform3D PalmGripGlobal() =>
        HandSocket != null ? HandSocket.GlobalTransform : HandGripGlobal() * new Transform3D(Basis.Identity, PalmGripOffset);

    private Vector3 _phoneAim, _switchAim;
    private static readonly Vector3 IndexTipLocal = new(0.032f, -0.135f, 0.004f);
    private static readonly Vector3 EarpieceLocal = new(0f, 0.03f, 0f);        // 손목 살짝 위(수화부 쪽)

    // 카메라(=플레이어 눈) 기준, 수화기를 쥔 손이 실제로 오는 자리(턱·볼 아래 오른쪽).
    // 귀에 딱 붙이지 않는다 — 손은 귀보다 낮고, 고개를 살짝 기울여(PhonePosture) 간격을 메운다.
    private Vector3 EarTargetWorld()
    {
        var c = GetViewport()?.GetCamera3D();
        if (c == null) return (_skel?.GlobalTransform.Origin ?? GlobalTransform.Origin) + new Vector3(0.12f, 0.55f, 0.14f);
        // 카메라(눈) 기준 상대 위치 — 오른쪽·아래·앞. 오른쪽 귀 위치까지 충분히
        // 벌려 손과 수화기가 화면 중앙을 넘어 왼손처럼 보이지 않게 한다.
        var b = c.GlobalTransform.Basis;
        return c.GlobalPosition + b.X * 0.16f - b.Y * 0.17f - b.Z * 0.10f;
    }

    // 전화 받기: 뻗기(팔꿈치 폄) → 손가락 순차로 감아 쥐기 → (쥔 순간 신호) → 팔꿈치 접어 귀로.
    public void PlayPhonePickup(Vector3 receiverGripWorld)
    {
        _phoneAim = receiverGripWorld;
        IsHoldingPhone = false;
        StartSequence(new List<Step>
        {
            new() { Pose = PoseReach(), In = 0.45f, Hold = 0.04f, UpperGiveDeg = 60f,
                    Aim = () => _phoneAim, AimTip = PalmGripOffset },
            new() { Pose = PoseGrip(), In = 0.40f, Hold = 0.06f, Stagger = true, UpperGiveDeg = 60f,
                    Aim = () => _phoneAim, AimTip = PalmGripOffset,
                    OnArrive = () => { IsHoldingPhone = true; EmitSignal(SignalName.PhoneGripped); } },
            new() { Pose = PoseCall(), In = 0.60f, Hold = 0f,   // 통화는 팔이 자유롭게 접혀 얼굴로 온다
                    Aim = EarTargetWorld, AimTip = Vector3.Zero, AimMaxDeg = 175f },
        });
    }

    // 전화 끊기: 팔꿈치 펴며 수화기를 받침대로 → (놓은 순간 신호) → 손가락 펴고 팔 내리고 숨김.
    public void PlayPhoneHangup(Vector3 receiverRestWorld)
    {
        _phoneAim = receiverRestWorld;
        StartSequence(new List<Step>
        {
            new() { Pose = PoseHangReach(), In = 0.55f, Hold = 0.06f, UpperGiveDeg = 60f,
                    Aim = () => _phoneAim, AimTip = PalmGripOffset,
                    OnArrive = () => { IsHoldingPhone = false; EmitSignal(SignalName.PhoneReleased); } },
            new() { Pose = PoseIdle(), In = 0.45f, Hold = 0f, Stagger = true,
                    OnArrive = () => SetArmVisible(_armR, false) },
        });
    }
    public void PlayPhoneRelease(Vector3 w) => PlayPhoneHangup(w);   // 예전 이름 호환

    // 스위치: 가볍게 주먹 + 검지만 편 손을 레버로 → 검지 끝마디로 툭(접촉 순간 onContact) → 복귀.
    public void PlaySwitchFlip(bool turningOn, Vector3 leverTipWorld, System.Action onContact)
    {
        _switchAim = leverTipWorld + SwitchAimCorrectionWorld;
        var contact = turningOn ? PoseSwitchOn() : PoseSwitchOff();
        StartSequence(new List<Step>
        {
            new() { Pose = PoseSwitchReady(turningOn), In = 0.34f, Hold = 0.04f, Stagger = true, UpperGiveDeg = 105f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal, AimMaxDeg = 150f },
            new() { Pose = contact, In = 0.13f, Hold = 0.05f, UpperGiveDeg = 105f, AimMaxDeg = 150f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal,
                    OnArrive = onContact },
            new() { Pose = PoseSwitchReady(turningOn), In = 0.16f, Hold = 0.02f, UpperGiveDeg = 105f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal },
            new() { Pose = PoseIdle(), In = 0.40f, Hold = 0f, Stagger = true,
                    OnArrive = () => SetArmVisible(_armR, false) },
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  관리자 패드 — 왼손이 받쳐 든다(누르기 연출은 없다)
    // ─────────────────────────────────────────────────────────────
    // 손은 패드의 그립 마커(위치 + 방향)를 따라간다. 위치는 팔 IK(Aim), 방향은 손목 IK(AimBasis)가
    // 맞추고, 패드는 왼손 소켓(HandSocketL)에 붙어 따라온다(Phone3D 수화기와 같은 방식).
    // 포즈는 오른팔 기준으로 쓰고 왼팔 채널이 뒤집는다(Mirror) — 여기 값은 "손 모양"만 정한다.
    //
    // 파지 모양: 「뒷면 받침」 — 손바닥과 다섯 손가락 전부가 패드 뒷면 쪽에 있다.
    // 앞면(화면이 있는 면)에는 손의 어떤 부분도 오지 않는다. 엄지도 뒤에 둔다.
    // 손가락은 손바닥 쪽(= 패드 쪽)으로 굽으므로 굽힘값이 크면 본체를 뚫는다.
    // 화면을 눌러도 이 포즈는 절대 바뀌지 않는다 — 클릭 피드백은 PadView(UI) 안에서만 준다.
    [ExportGroup("관리자 패드 손")]
    [Export] public Vector3 PadShoulderShift = new(-0.04f, -0.01f, -0.05f);
    [Export] public float PadUpperArmDeg = 18f;
    [Export] public float PadElbowDeg = 96f;
    // 뒷면을 받치는 네 손가락의 굽힘(도). 손바닥 쪽 = 패드 쪽이라, 크면 본체를 뚫는다.
    [Export] public float PadFingerCurl = 6f;
    // 검지 → 새끼로 갈수록 더해지는 굽힘(손끝이 한 줄로 서지 않게).
    [Export] public float PadFingerCurlStep = 0f;
    // 엄지 — 뒷면 받침에서는 엄지도 뒤에 둔다. 세 값은 순서대로 먹는다(Euler YXZ = Z → X → Y).
    //   Opp  손바닥 쪽으로 모아 엄지를 손가락과 나란히 세운다
    //   Lift 손바닥이 보는 쪽(= 패드 쪽) 으로 들어 올린다 — 0 이 기본, 키우면 앞면으로 넘어간다
    //   Curl 끝마디 굽힘
    [Export] public float PadThumbOpp = 40f;
    [Export] public float PadThumbLift = 0f;
    [Export] public float PadThumbCurl = 20f;

    private ArmPose PosePadHold()
    {
        var p = new ArmPose()
            .Sh(PadShoulderShift)
            .S("UpperArm_R", PadUpperArmDeg, -4, 6)
            .S("Forearm_R", PadElbowDeg, 0, -8)
            .S("Hand_R", 0, 0, 0);   // 손목 방향은 SolveWrist 가 그립 마커에 맞춘다
        void C(string f, float a) => p.S($"{f}_R_1", a, 0, 0).S($"{f}_R_2", a * 0.8f, 0, 0).S($"{f}_R_3", a * 0.5f, 0, 0);
        float step = PadFingerCurlStep;
        C("Index", PadFingerCurl); C("Middle", PadFingerCurl + step);
        C("Ring", PadFingerCurl + step * 2f); C("Pinky", PadFingerCurl + step * 3f);
        p.S("Thumb_R_1", PadThumbLift - 6f, 0, -PadThumbOpp).S("Thumb_R_2", -PadThumbCurl, 0, 0);
        return p;
    }

    public bool IsLeftArmActive => _armL.Visible;

    // 손바닥 그립점의 월드 위치(왼손/오른손) — 패드 자세 튜닝 · 캡처 검사용.
    public Vector3 PalmWorld(bool left)
    {
        if (_skel == null || !_bone.TryGetValue(left ? "Hand_L" : "Hand_R", out int h)) return GlobalPosition;
        var hand = _skel.GlobalTransform * _skel.GetBoneGlobalPose(h);
        return hand * (left ? MirrorX(PalmGripOffset) : PalmGripOffset);
    }

    // 스텝 하나가 그립 마커(월드 트랜스폼)를 위치 · 방향 모두 따라가게 한다.
    private static Step PadStep(ArmPose pose, float inSec, float hold, System.Func<Transform3D> grip,
        System.Action onArrive = null, bool stagger = false) => new()
    {
        Pose = pose, In = inSec, Hold = hold, Stagger = stagger, UpperGiveDeg = 170f, AimMaxDeg = 175f,
        Aim = () => grip().Origin, AimBasis = () => grip().Basis, AimTip = new Vector3(0f, -0.05f, 0.01f),
        OnArrive = onArrive,
    };

    // 거치대의 패드를 왼손으로 집어 든다:
    //   뻗기 → 쥐기(쥔 순간 onGripped — 이때부터 패드가 손을 따라온다) → 들어 올리기(onLifted) → 든 채로 유지.
    // cradle / hold 는 그립 마커의 월드 트랜스폼(거치대 위 · 들었을 때). 매 프레임 다시 읽는다.
    public void PlayPadPickup(System.Func<Transform3D> cradle, System.Func<Transform3D> hold, float liftSeconds,
        System.Action onGripped, System.Action onLifted)
    {
        float lift = Mathf.Max(0.05f, liftSeconds);
        StartSequence(_armL, new List<Step>
        {
            PadStep(PoseReach(), lift * 0.66f, 0.02f, cradle),
            PadStep(PosePadHold(), lift * 0.36f, 0.02f, cradle, onGripped, stagger: true),
            PadStep(PosePadHold(), lift, 0f, hold, onLifted),
            PadStep(PosePadHold(), 0.01f, HoldForever, hold),
        });
    }

    // 패드를 거치대로 돌려놓는다: 든 채로 거치대 그립 자리까지 내려간다(seconds — 도착 onPlaced,
    // 이때 패드가 손에서 떨어져 거치대에 놓인다) → 손을 펴고 → 팔을 거둔다.
    public void PlayPadPutDown(System.Func<Transform3D> cradle, float seconds, System.Action onPlaced)
    {
        float lower = Mathf.Max(0.05f, seconds);
        StartSequence(_armL, new List<Step>
        {
            PadStep(PosePadHold(), lower, 0f, cradle, onPlaced),
            PadStep(PoseReach(), lower * 0.31f, 0f, cradle, stagger: true),
            new() { Pose = PoseIdle(), In = lower * 0.78f, Hold = 0f, Stagger = true,
                    OnArrive = () => SetArmVisible(_armL, false) },
        });
    }

    // 호환용 스텁 — 이제 손은 상호작용 때만 나온다.
    public void PlayIdle() { }
    public void PlayTyping(float seconds = 0.9f) { }
    public void PlayDeskBrace() { }
    public void PlayButtonPress() { }
}
