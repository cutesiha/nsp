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
    // call 때 어깨(팔 고정축)를 몸 쪽으로 크게 당긴다 — 수화기를 귀로 가져오는 건 팔+어깨가 같이 후퇴.
    [Export] public Vector3 CallShoulderShift = new(0f, 0f, 0.06f);
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
        // 씬에 마디 노드(BoneAttachment3D)가 하나도 없는 구버전에서만 코드로 스킨을 만든다.
        if (!HasAuthoredLimbs() && SkinCount() == 0) BuildSkinMesh();

        // 왼팔 마디는 애니메이션이 없고 카메라에 걸릴 수 있어 런타임에 항상 숨긴다(오른손만 연출).
        if (!Engine.IsEditorHint())
            foreach (var n in _skel.GetChildren())
                if (n is Node3D n3 && (n.Name.ToString().EndsWith("_L") || n.Name.ToString().Contains("_L_")))
                    n3.Visible = false;

        InitPose();
        // 손은 평소 절대 안 보인다 — 전화/스위치 상호작용 때만 나온다.
        SetArmVisible(false);
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
        public Callable? OnArriveCallable;      // In 끝나는 순간 1회 (외부에서 전달)
    }

    private List<Step> _seq;
    private int _stepIdx;
    private float _stepT;
    private bool _stepArrived;
    private readonly Dictionary<string, Quaternion> _poseCur = new();
    private readonly Dictionary<string, Quaternion> _poseFrom = new();
    private Vector3 _shiftCur, _shiftFrom;
    private bool _armVisible;

    private static readonly (string prefix, float lead)[] FingerLead =
    {
        ("Thumb", 0.02f), ("Index", 0f), ("Middle", 0.06f), ("Ring", 0.12f), ("Pinky", 0.18f),
    };

    private void InitPose()
    {
        var idle = PoseIdle();
        _poseCur.Clear();
        foreach (var b in ArmBones)
            _poseCur[b] = idle.Rot.TryGetValue(b, out var d) ? QDeg(d) : Quaternion.Identity;
        _shiftCur = Vector3.Zero;
    }

    private void StartSequence(List<Step> steps)
    {
        if (_skel == null) return;
        if (!_armVisible) InitPose();          // 숨은 상태에서 시작하면 idle 부터
        SetArmVisible(true);
        _seq = steps;
        _stepIdx = 0;
        _stepT = 0f;
        _stepArrived = false;
        SnapshotFrom();
    }

    private void SnapshotFrom()
    {
        _poseFrom.Clear();
        foreach (var kv in _poseCur) _poseFrom[kv.Key] = kv.Value;
        _shiftFrom = _shiftCur;
    }

    private void SetArmVisible(bool v)
    {
        _armVisible = v;
        if (_skel != null) _skel.Visible = v || Engine.IsEditorHint();
    }

    private static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

    private void TickSequence(float d)
    {
        if (_seq == null) return;
        var step = _seq[_stepIdx];
        _stepT += d;

        float baseF = step.In <= 0f ? 1f : Mathf.Clamp(_stepT / step.In, 0f, 1f);

        foreach (var b in ArmBones)
        {
            Quaternion tq = step.Pose.Rot.TryGetValue(b, out var deg) ? QDeg(deg) : Quaternion.Identity;
            float f = baseF;
            if (step.Stagger && IsFinger(b))
            {
                float lead = FingerLeadOf(b);
                float span = Mathf.Max(0.06f, step.In - 0.20f);
                f = Mathf.Clamp((_stepT - lead) / span, 0f, 1f);
            }
            _poseCur[b] = _poseFrom.GetValueOrDefault(b, Quaternion.Identity).Slerp(tq, Smooth(f)).Normalized();
        }
        _shiftCur = _shiftFrom.Lerp(step.Pose.ShoulderShift, Smooth(baseF));

        // 접촉 보정 — 어깨(고정축)에서 상완은 조금, 팔꿈치가 주로 굽어 손이 목표에 닿는다.
        if (step.Aim != null)
            SolveArm(step.Aim(), step.AimTip, Smooth(baseF),
                     Mathf.Min(UpperArmGiveDeg, step.UpperGiveDeg), Mathf.Min(MaxAimDeg, step.AimMaxDeg));

        if (!_stepArrived && baseF >= 1f)
        {
            _stepArrived = true;
            step.OnArrive?.Invoke();
            if (step.OnArriveCallable is { } cb && cb.Target != null) cb.Call();
        }

        if (_stepArrived && _stepT >= step.In + step.Hold)
        {
            if (_stepIdx + 1 < _seq.Count)
            {
                _stepIdx++;
                _stepT = 0f;
                _stepArrived = false;
                SnapshotFrom();
            }
            else _seq = null;   // 마지막 포즈에서 정지
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
    private void SolveArm(Vector3 target, Vector3 tipLocal, float weight, float maxUpperDev, float maxForeDev)
    {
        if (weight <= 0.001f || _skel == null) return;
        if (!_bone.ContainsKey("Forearm_R") || !_bone.ContainsKey("Chest") || !_bone.ContainsKey("Hand_R")) return;

        Vector3 chest = _skel.GetBoneRest(_bone["Chest"]).Origin;
        Vector3 shOff = _skel.GetBoneRest(_bone["Shoulder_R"]).Origin + _shiftCur;
        Vector3 shoulderW = _skel.GlobalTransform * (chest + shOff);
        float sc = _skel.GlobalTransform.Basis.Scale.X;   // 리그 스케일(균일 가정) — 팔 길이에 반영

        Quaternion qU = _poseCur.GetValueOrDefault("UpperArm_R", Quaternion.Identity);
        Quaternion qF = _poseCur.GetValueOrDefault("Forearm_R", Quaternion.Identity);

        Vector3 elbowOff = _skel.GetBoneRest(_bone["Forearm_R"]).Origin;               // 상완 벡터(뼈공간)
        Vector3 handOff = _skel.GetBoneRest(_bone["Hand_R"]).Origin + tipLocal;        // 전완+팁 벡터
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
        _poseCur["UpperArm_R"] = qU.Slerp(wU, weight).Normalized();
        _poseCur["Forearm_R"] = qF.Slerp((_poseCur["UpperArm_R"].Inverse() * wF).Normalized(), weight).Normalized();
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

        TickSequence((float)delta);
        PushArm();
        TickDebug();
    }

    private void PushArm()
    {
        foreach (var b in ArmBones)
        {
            if (!_bone.TryGetValue(b, out int idx)) continue;
            _skel.SetBonePoseRotation(idx, _poseCur.GetValueOrDefault(b, Quaternion.Identity).Normalized());
        }
        if (_bone.TryGetValue("Shoulder_R", out int sh))
            _skel.SetBonePosePosition(sh, _skel.GetBoneRest(sh).Origin + _shiftCur);
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
        _palmDot.Visible = _armVisible;
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
        // 카메라(눈) 기준 상대 위치 — 오른쪽·아래·앞. 손+수화기가 화면 우하단에 오도록.
        var b = c.GlobalTransform.Basis;
        return c.GlobalPosition + b.X * 0.045f - b.Y * 0.15f - b.Z * 0.15f;
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
                    OnArrive = () => SetArmVisible(false) },
        });
    }
    public void PlayPhoneRelease(Vector3 w) => PlayPhoneHangup(w);   // 예전 이름 호환

    // 스위치: 가볍게 주먹 + 검지만 편 손을 레버로 → 검지 끝마디로 툭(접촉 순간 onContact) → 복귀.
    public void PlaySwitchFlip(bool turningOn, Vector3 leverTipWorld, Callable onContact)
    {
        _switchAim = leverTipWorld;
        var contact = turningOn ? PoseSwitchOn() : PoseSwitchOff();
        StartSequence(new List<Step>
        {
            new() { Pose = PoseSwitchReady(turningOn), In = 0.38f, Hold = 0.04f, Stagger = true, UpperGiveDeg = 60f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal },
            new() { Pose = contact, In = 0.13f, Hold = 0.05f, UpperGiveDeg = 60f, AimMaxDeg = 50f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal,
                    OnArriveCallable = onContact },
            new() { Pose = PoseSwitchReady(turningOn), In = 0.16f, Hold = 0.02f, UpperGiveDeg = 60f,
                    Aim = () => _switchAim, AimTip = IndexTipLocal },
            new() { Pose = PoseIdle(), In = 0.40f, Hold = 0f, Stagger = true,
                    OnArrive = () => SetArmVisible(false) },
        });
    }

    // 호환용 스텁 — 이제 손은 상호작용 때만 나온다.
    public void PlayIdle() { }
    public void PlayTyping(float seconds = 0.9f) { }
    public void PlayDeskBrace() { }
    public void PlayButtonPress() { }
}
