using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using NSP.Core;

namespace NSP.View;

// 트루엔딩 처분실의 결번자.
//
// **팔이 검게 변한 것이 아니다.** 양팔은 평소의 직원 3D 모델 그대로이고, 그 위를
// 정체불명의 검은 구속재가 여러 겹 감고 있다(문서 §4). 구속재에는 굵은 쇠사슬이
// 이어져 있고, 사슬 끝에는 사람이 끌 수 없는 크기의 쇠 무게추가 바닥에 놓여 있다(§5).
//
// 자세와 몸부림은 **클립을 쓰지 않고 직접 만든다.** 이 게임의 구속 클립(isolated_*)은
// 격리실 구속 침대에 **누운** 자세라, 무릎 꿇고 양팔이 좌우로 당겨진 이 장면과 맞지 않는다.
// 뼈대가 평범한 Node3D 계층이라 직접 돌리는 편이 정확하고, 문서 §6 의 몸부림 순서를
// 그대로 짤 수 있다.
//
// 구속재는 팔 마디의 자식으로 붙어 자세를 그대로 따라가고, 사슬은 매 프레임 손목과
// 무게추를 잇는다 — 그래서 몸부림치면 사슬이 실제로 팽팽해진다.
public partial class EndingOffender : Node3D
{
    private static readonly Dictionary<string, string> Scenes = new()
    {
        ["fox"] = "res://scenes/cctv_characters/employees/FoxEmployee3D.tscn",
        ["dog"] = "res://scenes/cctv_characters/employees/DogEmployee3D.tscn",
        ["cat"] = "res://scenes/cctv_characters/employees/CatEmployee3D.tscn",
        ["sheep"] = "res://scenes/cctv_characters/employees/SheepEmployee3D.tscn",
        ["rabbit"] = "res://scenes/cctv_characters/employees/RabbitEmployee3D.tscn",
        ["wolf"] = "res://scenes/cctv_characters/employees/WolfEmployee3D.tscn",
    };

    private const string Rig = "VisualRoot/RigRoot";
    private const string HipsPath = Rig + "/Hips";
    private const string TorsoPath = HipsPath + "/Torso";
    private const string ChestPath = TorsoPath + "/Chest";
    private const string HeadPath = ChestPath + "/Neck/Head";
    private const string ShoulderL = ChestPath + "/ShoulderL";
    private const string ShoulderR = ChestPath + "/ShoulderR";

    private Node3D _actor, _visual, _hips, _torso, _chest, _head;
    private Node3D _shL, _shR, _upL, _upR, _loL, _loR;
    private AnimationPlayer _player;

    private readonly List<MeshInstance3D> _chain = new();
    private readonly List<Node3D> _wrists = new();
    private readonly List<Vector3> _anchors = new();
    private Node3D _faceOverlay;

    private const int Links = 16;
    private const float ChainRest = 1.72f;

    // ── 자세 변수 ───────────────────────────────────────────────────────
    // 매 프레임 이 값들로 뼈대를 다시 세운다. 연출은 이 값만 움직인다.
    private float _headDown = 1f;     // 1 = 푹 숙임, 0 = 들어 올림
    private float _pullR, _pullL;     // 팔을 빼내려 당기는 정도
    private float _tremble;           // 어깨 떨림
    private float _twist;             // 상체 비틀기
    private float _slack = 1f;        // 1 = 축 늘어짐
    private float _t;

    public void Spawn(string employeeId)
    {
        string path = Scenes.GetValueOrDefault(employeeId ?? "", "");
        if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
            _actor = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
        if (_actor == null)
            foreach (var p in Scenes.Values)
            {
                if (!ResourceLoader.Exists(p)) continue;
                _actor = GD.Load<PackedScene>(p)?.Instantiate<Node3D>();
                if (_actor != null) break;
            }
        if (_actor == null) return;

        _actor.Position = Vector3.Zero;
        // 카메라(+Z 쪽)를 향해 앉는다. 캐릭터는 자기 -Z 를 본다.
        _actor.Rotation = new Vector3(0f, Mathf.Pi, 0f);
        AddChild(_actor);

        // 클립이 돌면 매 프레임 자세를 덮어쓴다 — 아예 멈춰 둔다.
        _player = _actor.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        _player?.Stop();

        _visual = _actor.GetNodeOrNull<Node3D>("VisualRoot");
        _hips = _actor.GetNodeOrNull<Node3D>(HipsPath);
        _torso = _actor.GetNodeOrNull<Node3D>(TorsoPath);
        _chest = _actor.GetNodeOrNull<Node3D>(ChestPath);
        _head = _actor.GetNodeOrNull<Node3D>(HeadPath);
        _shL = _actor.GetNodeOrNull<Node3D>(ShoulderL);
        _shR = _actor.GetNodeOrNull<Node3D>(ShoulderR);
        _upL = _actor.GetNodeOrNull<Node3D>(ShoulderL + "/UpperArmL");
        _upR = _actor.GetNodeOrNull<Node3D>(ShoulderR + "/UpperArmR");
        _loL = _actor.GetNodeOrNull<Node3D>(ShoulderL + "/UpperArmL/LowerArmL");
        _loR = _actor.GetNodeOrNull<Node3D>(ShoulderR + "/UpperArmR/LowerArmR");

        // 어깨 장식(견장)은 뼈대가 아니라 배우 루트에 붙어 있다 — 몸을 내려 앉히면
        // 그것만 제자리에 남아 머리 위에 떠 버린다. 이 장면에서는 떼어 둔다.
        var accessory = _actor.GetNodeOrNull<Node3D>("AccessoryAnchor");
        if (accessory != null) accessory.Visible = false;

        KneelLegs();
        BuildRestraints();
        BuildWeightsAndChains();
        ApplyPose();
    }

    // 무릎을 꿇린다. 다리는 연출 내내 움직이지 않으므로 한 번만 잡아 둔다.
    private void KneelLegs()
    {
        foreach (string s in new[] { "L", "R" })
        {
            var upper = _actor.GetNodeOrNull<Node3D>($"{HipsPath}/UpperLeg{s}");
            var lower = _actor.GetNodeOrNull<Node3D>($"{HipsPath}/UpperLeg{s}/LowerLeg{s}");
            var foot = _actor.GetNodeOrNull<Node3D>($"{HipsPath}/UpperLeg{s}/LowerLeg{s}/Foot{s}");
            float side = s == "L" ? 1f : -1f;
            if (upper != null) upper.RotationDegrees = new Vector3(6f, 0f, side * 5f);
            if (lower != null) lower.RotationDegrees = new Vector3(-104f, 0f, 0f);
            if (foot != null) foot.RotationDegrees = new Vector3(14f, 0f, 0f);
        }
        // 정강이가 바닥에 닿도록 몸 전체를 내린다.
        if (_visual != null) _visual.Position = new Vector3(0f, -0.40f, 0.10f);
    }

    // ── 검은 구속재 ──────────────────────────────────────────────────────
    //
    // 팔 둘레를 여러 겹 감는다. 팔 메시를 통째로 덮지 않는다 — 띠 사이로 원래 팔 색이
    // 보여야 "팔이 검은 것"이 아니라 "팔이 묶인 것"으로 읽힌다(§4).
    private void BuildRestraints()
    {
        var band = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.030f, 0.028f, 0.036f),
            Roughness = 0.40f,
            Metallic = 0.18f,
        };
        Wrap(_upL, band, 0.085f, new[] { -0.07f, -0.19f });
        Wrap(_upR, band, 0.085f, new[] { -0.07f, -0.19f });
        Wrap(_loL, band, 0.077f, new[] { -0.04f, -0.14f, -0.24f });
        Wrap(_loR, band, 0.077f, new[] { -0.04f, -0.14f, -0.24f });
    }

    private static void Wrap(Node3D bone, Material mat, float radius, float[] offsets)
    {
        if (bone == null) return;
        foreach (float y in offsets)
            bone.AddChild(new MeshInstance3D
            {
                Name = "Restraint",
                Mesh = new TorusMesh
                {
                    InnerRadius = radius * 0.74f, OuterRadius = radius, Rings = 14, RingSegments = 8,
                },
                Position = new Vector3(0f, y, 0f),
                RotationDegrees = new Vector3(90f, 0f, 0f),
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
    }

    // ── 쇠사슬 · 무게추 ─────────────────────────────────────────────────
    private void BuildWeightsAndChains()
    {
        var iron = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.19f, 0.19f, 0.20f), Roughness = 0.5f, Metallic = 0.95f,
        };

        int side = 0;
        foreach (var lower in new[] { _loL, _loR })
        {
            var wrist = lower?.GetNodeOrNull<Node3D>(side == 0 ? "HandL" : "HandR");
            if (wrist == null) { side++; continue; }
            _wrists.Add(wrist);

            float x = side == 0 ? 1.45f : -1.45f;   // 캐릭터가 180° 돌아 서 있다
            var weight = new Node3D { Name = $"Weight{side}", Position = new Vector3(x, 0f, 0.25f) };
            AddChild(weight);
            weight.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.88f, 0.70f, 0.88f) },
                Position = new Vector3(0f, 0.35f, 0f),
                MaterialOverride = iron,
            });
            weight.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.08f, OuterRadius = 0.12f, Rings = 10, RingSegments = 8 },
                Position = new Vector3(0f, 0.74f, 0f),
                RotationDegrees = new Vector3(0f, 90f, 0f),
                MaterialOverride = iron,
            });
            _anchors.Add(weight.Position + new Vector3(0f, 0.76f, 0f));

            for (int i = 0; i < Links; i++)
            {
                var link = new MeshInstance3D
                {
                    Name = $"Link{side}_{i}",
                    Mesh = new TorusMesh
                    {
                        InnerRadius = 0.032f, OuterRadius = 0.058f, Rings = 8, RingSegments = 6,
                    },
                    MaterialOverride = iron,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(link);
                _chain.Add(link);
            }
            side++;
        }
    }

    // ── 매 프레임 ───────────────────────────────────────────────────────

    public float Tautness { get; private set; }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        ApplyPose();
        UpdateChains();
    }

    private void ApplyPose()
    {
        if (_hips == null) return;

        // 숨이 거칠다 — 가슴이 눈에 띄게 들썩인다.
        float breath = Mathf.Sin(_t * 2.6f) * (1.4f + _slack * 1.2f);
        // 떨림은 빠르고 작다.
        float shake = _tremble * Mathf.Sin(_t * 26f) * 3.2f;

        // 상체는 **앞으로** 숙인다. 이 뼈대에서 허리·가슴의 +X 는 뒤로 젖히는 쪽이라
        // 부호가 음수다(고개는 반대로 +X 가 숙이는 쪽이다 — 아래 _head 참고).
        _hips.RotationDegrees = new Vector3(-9f - _twist * 2f, _twist * 10f, _twist * 5f);
        if (_torso != null)
            _torso.RotationDegrees = new Vector3(-15f - breath * 0.4f + _pullR * 3f, _twist * 13f, -_twist * 6f);
        if (_chest != null)
            _chest.RotationDegrees = new Vector3(-8f - breath, _twist * 8f, shake * 0.4f);

        // 고개 — 푹 숙였다가 아주 천천히 든다. 목도 +X 가 뒤로 젖히는 쪽이라 음수다.
        if (_head != null)
            _head.RotationDegrees = new Vector3(Mathf.Lerp(-3f, -34f, _headDown), _twist * -6f, shake * 0.5f);

        // 양팔은 사슬에 끌려 좌우로 벌어져 있다. 당기면 그만큼 더 벌어지고 위로 들린다.
        Arm(_shL, _upL, _loL, +1f, _pullL, shake);
        Arm(_shR, _upR, _loR, -1f, _pullR, shake);
    }

    // side = +1 이면 캐릭터의 왼팔. 팔은 로컬 -Y 로 뻗어 있으므로 Z 회전이 좌우 벌림이다.
    private static void Arm(Node3D shoulder, Node3D upper, Node3D lower, float side, float pull, float shake)
    {
        if (shoulder == null) return;
        // 기본 68° — 팔이 거의 수평으로 당겨져 있다. 당기면 78° 까지 올라간다.
        float spread = Mathf.Lerp(68f, 78f, pull);
        shoulder.RotationDegrees = new Vector3(shake * 0.6f, 0f, side * spread);
        if (upper != null) upper.RotationDegrees = new Vector3(-12f + pull * 10f, 0f, side * (4f + pull * 6f));
        // 팔꿈치는 구속재 때문에 거의 펴진 채다 — 당겨도 조금밖에 굽지 않는다.
        if (lower != null) lower.RotationDegrees = new Vector3(-16f + pull * 12f, 0f, 0f);
    }

    private void UpdateChains()
    {
        if (_wrists.Count == 0) return;
        float maxT = 0f;
        for (int s = 0; s < _wrists.Count && s < _anchors.Count; s++)
        {
            Vector3 a = ToLocal(_wrists[s].GlobalPosition);
            Vector3 b = _anchors[s];
            float dist = a.DistanceTo(b);
            float taut = Mathf.Clamp(dist / ChainRest, 0f, 1f);
            maxT = Mathf.Max(maxT, taut);
            float sag = (1f - taut) * 0.42f;
            var dir = (b - a).Normalized();

            for (int i = 0; i < Links; i++)
            {
                int idx = s * Links + i;
                if (idx >= _chain.Count) break;
                float t = (i + 0.5f) / Links;
                var p = a.Lerp(b, t);
                p.Y -= Mathf.Sin(t * Mathf.Pi) * sag;
                _chain[idx].Position = p;
                _chain[idx].LookAtFromPosition(p, p + dir, Vector3.Up);
                // 링이 한 칸씩 엇갈려 꿰인다 — 진짜 사슬처럼 보이게.
                if (i % 2 == 1) _chain[idx].RotateObjectLocal(Vector3.Forward, Mathf.Pi / 2f);
            }
        }
        Tautness = maxT;
    }

    // ── 연기 (문서 §6 의 순서 그대로) ────────────────────────────────────

    // 처음 상태 — 고개를 푹 숙이고 축 늘어져 있다.
    public void Slump()
    {
        _headDown = 1f; _pullL = _pullR = 0f; _tremble = 0f; _twist = 0f; _slack = 1f;
    }

    // 짧은 컷씬처럼 한 번만 돈다. 기계적인 반복 루프가 아니다.
    public async Task StruggleSequence()
    {
        // ① 오른팔을 한 번 강하게 당긴다.
        _slack = 0.3f;
        await Ramp(v => _pullR = v, 0f, 1f, 0.42);
        Sfx.Instance?.Play("metal_clang", -9f, 0.78f);
        await Hold(0.22);
        // ② 실패 — 다시 돌아온다.
        await Ramp(v => _pullR = v, 1f, 0.15f, 0.55);
        await Hold(0.35);

        // ③ 양쪽 어깨가 떨린다.
        await Ramp(v => _tremble = v, 0f, 1f, 0.5);
        await Hold(0.7);
        await Ramp(v => _tremble = v, 1f, 0.25f, 0.4);

        // ④ 몸 전체를 뒤틀며 구속을 풀려고 한다.
        _ = Ramp(v => _pullL = v, 0.1f, 0.95f, 0.8);
        await Ramp(v => _twist = v, 0f, 1f, 0.8);
        Sfx.Instance?.Play("metal_clang", -6f, 0.62f);
        // ⑤ 사슬이 팽팽해지며 금속음. 무게추는 거의 움직이지 않는다.
        Sfx.Instance?.Play("rubble_collapse", -14f, 0.75f);
        await Hold(0.5);
        await Ramp(v => _twist = v, 1f, -0.7f, 0.65);
        Sfx.Instance?.Play("metal_clang", -11f, 0.9f);
        await Hold(0.3);

        // ⑥ 다시 축 늘어진다.
        _ = Ramp(v => _pullL = v, 0.95f, 0f, 0.9);
        _ = Ramp(v => _pullR = v, 0.15f, 0f, 0.9);
        await Ramp(v => _twist = v, -0.7f, 0f, 0.9);
        _tremble = 0f;
        _slack = 1f;
        await Hold(0.5);
    }

    // 고개를 **천천히** 든다. 한 번에 들지 않고 중간에 멈칫한다.
    public async Task LiftHead(double seconds)
    {
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            float e = k < 0.55f ? Mathf.Pow(k / 0.55f, 2f) * 0.58f
                                : 0.58f + Mathf.Pow((k - 0.55f) / 0.45f, 0.75f) * 0.42f;
            _headDown = 1f - e;
        }
        _headDown = 0f;
    }

    private async Task Ramp(System.Action<float> set, float from, float to, double seconds)
    {
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            set(Mathf.Lerp(from, to, k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) / 2f));
        }
        set(to);
    }

    private async Task Hold(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // ── 섬뜩한 얼굴 ──────────────────────────────────────────────────────
    //
    // 새 모델을 만들지 않는다(§7). 가면을 핏기 없이 눌러 두고 그 위에 **어긋난 눈과 입**을
    // 얹는다 — 눈 높이가 좌우로 어긋나 있고 입이 얼굴 폭을 거의 다 쓴다.
    // 귀여운 몬스터 얼굴도, 화면을 덮는 점프스케어도 만들지 않는다.
    public void RevealFace()
    {
        if (_head == null || _faceOverlay != null) return;

        var ink = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.015f, 0.013f, 0.017f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            // 가면의 눈·코 메시와 겹치는 자리다 — 깊이 비교를 끄고 무조건 위에 그린다.
            NoDepthTest = true,
            RenderPriority = 8,
        };

        // 가면 평면(MaskAnchor)에 바로 얹는다 — 머리에 달면 가면 뒤로 묻힌다.
        var anchor = _head.GetNodeOrNull<Node3D>("MaskAnchor") ?? _head;
        _faceOverlay = new Node3D { Name = "OffenderFace", Position = new Vector3(0f, 0.008f, -0.055f) };
        anchor.AddChild(_faceOverlay);

        Quad(_faceOverlay, new Vector2(0.052f, 0.072f), new Vector3(-0.055f, 0.030f, 0f), ink, -7f);
        Quad(_faceOverlay, new Vector2(0.045f, 0.088f), new Vector3(0.060f, 0.011f, 0f), ink, 10f);
        Quad(_faceOverlay, new Vector2(0.180f, 0.026f), new Vector3(0.005f, -0.060f, 0f), ink, 3f);
        Quad(_faceOverlay, new Vector2(0.052f, 0.020f), new Vector3(-0.083f, -0.044f, 0.002f), ink, 40f);
        Quad(_faceOverlay, new Vector2(0.052f, 0.020f), new Vector3(0.090f, -0.046f, 0.002f), ink, -42f);

        // 가면 자체도 한 단계 핏기가 가신다.
        foreach (var mi in FindMeshes(_head))
            mi.MaterialOverlay = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.22f, 0.05f, 0.07f, 0.42f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
    }

    private static void Quad(Node3D parent, Vector2 size, Vector3 pos, Material mat, float rollDeg) =>
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size },
            Position = pos,
            RotationDegrees = new Vector3(0f, 180f, rollDeg),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

    private static IEnumerable<MeshInstance3D> FindMeshes(Node root)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is MeshInstance3D mi) yield return mi;
            foreach (var g in FindMeshes(c)) yield return g;
        }
    }
}
