using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 의자에 앉은 관리자 캐릭터. 몸통/다리/머리는 씬(MainScene3D_Test)에 저폴리 MeshInstance3D
// 노드로 두고(에디터에서 자유 편집), 팔·손·손가락은 씬의 Skeleton3D "Rig" 를 읽어 스킨 메시를
// 코드로 생성한다(뼈 웨이트가 있어 손으로 못 짜는 부분). [Tool] 이라 에디터 뷰포트에서도 보인다.
// Rig 뼈 rest 나 아래 손 파라미터를 바꾼 뒤 인스펙터의 RebuildSkin 토글을 누르면 스킨이 다시 만들어진다.
// 게임 판정과 완전히 분리 — Phone3D / PowerSwitchPanel / ControlRoomInteraction 신호에 포즈만 바꾼다.
[Tool]
public partial class PlayerCharacter : Node3D
{
    [Export] public float PoseLerp = 7f; // 포즈 전환 속도
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

    private Skeleton3D _skel;
    private readonly Dictionary<string, int> _bone = new();
    private readonly Dictionary<int, Vector3> _globalPos = new();
    private Quaternion[] _restLocalRot;
    private Quaternion[] _deltaCur;      // 현재 적용 중인 회전 델타(레스트 기준)
    private Quaternion[] _deltaTgt;      // 목표 델타

    private double _typingUntil;
    private float _typePhase;
    private double _holdUntil;

    public override void _Ready()
    {
        _skel = GetNodeOrNull<Skeleton3D>("Rig");
        if (_skel == null)
        {
            GD.PushWarning("PlayerCharacter: 자식 Skeleton3D 'Rig' 를 찾지 못함 — 씬에 추가되어 있어야 함.");
            return;
        }

        BindBones();
        if (SkinCount() == 0) BuildSkinMesh();
        ApplyPose(PoseIdle(), instant: true);
    }

    private void RegenSkin()
    {
        _skel ??= GetNodeOrNull<Skeleton3D>("Rig");
        if (_skel == null) return;
        foreach (var c in _skel.GetChildren()) c.QueueFree();
        BindBones();
        BuildSkinMesh();
        ApplyPose(PoseIdle(), instant: true);
    }

    private int SkinCount()
    {
        int c = 0;
        foreach (var n in _skel.GetChildren()) if (n is MeshInstance3D) c++;
        return c;
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
        _deltaCur = new Quaternion[n];
        _deltaTgt = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            _bone[_skel.GetBoneName(i)] = i;
            _restLocalRot[i] = _skel.GetBoneRest(i).Basis.GetRotationQuaternion();
            _deltaCur[i] = Quaternion.Identity;
            _deltaTgt[i] = Quaternion.Identity;
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
    //  스킨 메시 (팔 / 손 / 손가락) — Rig 뼈 위치 기준으로 강체 바인딩
    // ─────────────────────────────────────────────────────────────

    // 각 손가락: (밑동 x, 3마디 길이, 굵기). gen_skeleton.py 와 동일해야 뼈와 맞는다.
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

    // 뼈 로컬 +Y가 뼈를 따라간다고 가정하지 않고, 스켈레톤 공간에서 직접 상자를 만든다.
    // a→b 방향으로 길쭉한 상자, 단면은 a에서 (w0×t0), b에서 (w1×t1). 정점 전부 bone에 100% 바인딩.
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
            Quad(ring0[i], ring0[j], ring1[j], ring1[i]); // 옆면
        }
        Quad(ring0[3], ring0[2], ring0[1], ring0[0]); // 밑면
        Quad(ring1[0], ring1[1], ring1[2], ring1[3]); // 윗면(끝)
    }

    // ─────────────────────────────────────────────────────────────
    //  포즈 (뼈 이름 → 로컬 회전 델타, 오일러 도)
    // ─────────────────────────────────────────────────────────────
    private static Dictionary<string, Vector3> Relaxed()
    {
        var d = new Dictionary<string, Vector3>();
        // 편 상태에서도 살짝 굽은 Relaxed 손가락.
        foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
            foreach (var sd in new[] { "L", "R" })
            {
                d[$"{f}_{sd}_1"] = new Vector3(12, 0, 0);
                d[$"{f}_{sd}_2"] = new Vector3(16, 0, 0);
                d[$"{f}_{sd}_3"] = new Vector3(10, 0, 0);
            }
        foreach (var sd in new[] { "L", "R" })
        {
            d[$"Thumb_{sd}_1"] = new Vector3(-6, 0, sd == "R" ? -14 : 14);
            d[$"Thumb_{sd}_2"] = new Vector3(-8, 0, 0);
        }
        return d;
    }

    // 뼈 로컬 +X 축 회전이 양수면 아래로 뻗은 팔을 앞(-Z)으로 스윙시킨다.
    // 상완+전완 각도 합이 ~90 이면 전완이 수평(앞).
    private Dictionary<string, Vector3> PoseIdle()
    {
        var d = Relaxed();
        // 전완을 책상 위에 얹은 자세(손은 화면 아래쪽 가장자리).
        d["UpperArm_R"] = new Vector3(44, -16, 0);
        d["Forearm_R"] = new Vector3(50, 4, 0);
        d["Hand_R"] = new Vector3(-6, 0, 0);
        d["UpperArm_L"] = new Vector3(44, 16, 0);
        d["Forearm_L"] = new Vector3(50, -4, 0);
        d["Hand_L"] = new Vector3(-6, 0, 0);
        return d;
    }

    private Dictionary<string, Vector3> PosePhoneReach()
    {
        var d = PoseIdle();
        // 오른팔을 앞으로 들어 뻗고 손가락을 벌린다(수화기를 잡으러 가는 중).
        d["UpperArm_R"] = new Vector3(30, -18, 0);
        d["Forearm_R"] = new Vector3(64, 8, 0);
        d["Hand_R"] = new Vector3(-24, 0, 0);
        SpreadFingers(d, "R", 0.5f);
        return d;
    }

    private Dictionary<string, Vector3> PosePhoneHold()
    {
        var d = PoseIdle();
        // 수화기를 감싸 쥐고 팔꿈치를 크게 접어 귀 쪽으로(손이 얼굴 높이로 올라옴).
        d["UpperArm_R"] = new Vector3(24, 8, 0);
        d["Forearm_R"] = new Vector3(128, 12, 0);
        d["Hand_R"] = new Vector3(-46, 24, 16);
        GripFingers(d, "R", 0.7f);
        return d;
    }

    private Dictionary<string, Vector3> PoseSwitch()
    {
        var d = PoseIdle();
        // 오른팔을 오른쪽 앞으로 들어 검지만 펴서 스위치를 민다.
        d["UpperArm_R"] = new Vector3(30, -40, 0);
        d["Forearm_R"] = new Vector3(58, -6, 0);
        d["Hand_R"] = new Vector3(-20, -10, 0);
        GripFingers(d, "R", 0.85f);
        d["Index_R_1"] = new Vector3(6, 0, 0);
        d["Index_R_2"] = new Vector3(8, 0, 0);
        d["Index_R_3"] = new Vector3(4, 0, 0);
        return d;
    }

    private Dictionary<string, Vector3> PoseType()
    {
        var d = PoseIdle();
        d["UpperArm_R"] = new Vector3(40, -10, 0);
        d["Forearm_R"] = new Vector3(58, 0, 0);
        d["UpperArm_L"] = new Vector3(40, 10, 0);
        d["Forearm_L"] = new Vector3(58, 0, 0);
        foreach (var sd in new[] { "L", "R" })
            foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                d[$"{f}_{sd}_1"] = new Vector3(30, 0, 0);
                d[$"{f}_{sd}_2"] = new Vector3(26, 0, 0);
            }
        return d;
    }

    private Dictionary<string, Vector3> PoseDeskBrace()
    {
        var d = PoseIdle();
        d["UpperArm_R"] = new Vector3(48, -8, 0);
        d["Forearm_R"] = new Vector3(52, 0, 0);
        d["UpperArm_L"] = new Vector3(48, 8, 0);
        d["Forearm_L"] = new Vector3(52, 0, 0);
        GripFingers(d, "R", 0.5f);
        GripFingers(d, "L", 0.5f);
        return d;
    }

    private static void SpreadFingers(Dictionary<string, Vector3> d, string sd, float amt)
    {
        d[$"Index_{sd}_1"] = new Vector3(4, 0, sd == "R" ? -10 * amt : 10 * amt);
        d[$"Middle_{sd}_1"] = new Vector3(4, 0, 0);
        d[$"Ring_{sd}_1"] = new Vector3(4, 0, sd == "R" ? 8 * amt : -8 * amt);
        d[$"Pinky_{sd}_1"] = new Vector3(4, 0, sd == "R" ? 16 * amt : -16 * amt);
    }

    private static void GripFingers(Dictionary<string, Vector3> d, string sd, float amt)
    {
        foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
        {
            d[$"{f}_{sd}_1"] = new Vector3(55 * amt, 0, 0);
            d[$"{f}_{sd}_2"] = new Vector3(60 * amt, 0, 0);
            d[$"{f}_{sd}_3"] = new Vector3(40 * amt, 0, 0);
        }
        d[$"Thumb_{sd}_1"] = new Vector3(-10, 0, sd == "R" ? -34 : 34);
        d[$"Thumb_{sd}_2"] = new Vector3(-30 * amt, 0, 0);
    }

    // ─────────────────────────────────────────────────────────────
    //  적용 / 보간
    // ─────────────────────────────────────────────────────────────
    private void ApplyPose(Dictionary<string, Vector3> pose, bool instant = false)
    {
        for (int i = 0; i < _deltaTgt.Length; i++)
            _deltaTgt[i] = Quaternion.Identity;

        foreach (var (name, euler) in pose)
        {
            if (!_bone.TryGetValue(name, out int idx)) continue;
            _deltaTgt[idx] = Quaternion.FromEuler(new Vector3(
                Mathf.DegToRad(euler.X), Mathf.DegToRad(euler.Y), Mathf.DegToRad(euler.Z)));
        }

        if (instant)
        {
            for (int i = 0; i < _deltaCur.Length; i++) _deltaCur[i] = _deltaTgt[i];
            PushBones();
        }
    }

    private void PushBones()
    {
        for (int i = 0; i < _deltaCur.Length; i++)
            _skel.SetBonePoseRotation(i, (_restLocalRot[i] * _deltaCur[i]).Normalized());
    }

    public override void _Process(double delta)
    {
        if (_skel == null || Engine.IsEditorHint()) return; // 에디터에선 _Ready 의 Idle 포즈만 유지
        double now = Time.GetTicksMsec() / 1000.0;

        if (_typingUntil > 0 && now >= _typingUntil) { _typingUntil = 0; ApplyPose(PoseIdle()); }
        if (_holdUntil > 0 && now >= _holdUntil) { _holdUntil = 0; ApplyPose(PoseIdle()); }

        float t = Mathf.Clamp((float)delta * PoseLerp, 0f, 1f);
        for (int i = 0; i < _deltaCur.Length; i++)
            _deltaCur[i] = _deltaCur[i].Slerp(_deltaTgt[i], t).Normalized();

        // 타건 시 손끝 위아래 톡톡.
        if (_typingUntil > 0)
        {
            _typePhase += (float)delta * 22f;
            float bob = 6f + 6f * Mathf.Sin(_typePhase);
            foreach (var sd in new[] { "L", "R" })
                foreach (var f in new[] { "Index", "Middle", "Ring" })
                    if (_bone.TryGetValue($"{f}_{sd}_1", out int bi))
                        _deltaCur[bi] = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(28f + bob), 0, 0));
        }

        PushBones();
    }

    // ─────────────────────────────────────────────────────────────
    //  공개 제스처 (기존 PlayerArms 와 동일한 이름)
    // ─────────────────────────────────────────────────────────────
    public void PlayIdle() { _typingUntil = 0; _holdUntil = 0; ApplyPose(PoseIdle()); }
    public void PlayPhoneReach() => ApplyPose(PosePhoneReach());
    public void PlayPhoneHold() => ApplyPose(PosePhoneHold());

    public void PlayButtonPress()
    {
        ApplyPose(PoseSwitch());
        _holdUntil = Time.GetTicksMsec() / 1000.0 + 0.5;
    }

    public void PlaySwitchFlip(int side = 0)
    {
        ApplyPose(PoseSwitch());
        _holdUntil = Time.GetTicksMsec() / 1000.0 + 0.55;
    }

    public void PlayTyping(float seconds = 0.9f)
    {
        _typingUntil = Time.GetTicksMsec() / 1000.0 + seconds;
        ApplyPose(PoseType());
    }

    public void PlayDeskBrace()
    {
        ApplyPose(PoseDeskBrace());
        _holdUntil = Time.GetTicksMsec() / 1000.0 + 0.3;
    }
}
