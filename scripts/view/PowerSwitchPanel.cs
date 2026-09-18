using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Ui;

namespace NSP.View;

// 전력 배분용 물리 토글 스위치 박스 — LIGHTING / CCTV / SENSOR 3개의 레버 스위치.
// 레버를 클릭하면 손이 나와 검지로 튕기고, 그 순간 "딱!" 소리 + 레버가 위/아래로 넘어가며
// 해당 기기 전원이 on/off 된다. 전력 포인트가 깎이면 스위치 기기에서 지지직 스파크가 튀고,
// 전력이 0이 되면 SHUT DOWN — 방 조명/센서/CCTV 전부 꺼지고 계속 파지직거린다.
// GameState 를 읽고 TryTogglePower 만 호출한다(전력 상태를 여기서 들고 있지 않는다).
[Tool]
public partial class PowerSwitchPanel : Node3D
{
    // X 는 switch.glb 모델 로컬 좌표의 레버 중심 (메시를 실측해서 얻은 값).
    private static readonly (PowerConsumer Channel, string Label, float ModelX)[] Switches =
    {
        (PowerConsumer.Lighting, "LIGHTING", -0.288f),
        (PowerConsumer.CctvWatch, "CCTV", 0.000f),
        (PowerConsumer.Sensor, "SENSOR", 0.288f),
    };

    // switch.glb 는 본체와 레버가 하나의 메시로 붙어 있다. 아래 상자 안에 드는 삼각형을
    // 레버로 떼어내 각자 회전축(Pivot)에 매단다. 값은 메시 실측 기준:
    //   레버 3개 : x = ±0.288 / 0, y 0.435~0.711, z 0.235~0.307
    private const float LeverHalfWidth = 0.052f;  // 레버 중심에서 좌우로 이 만큼
    private const float LeverMinY = 0.42f;        // 이 높이 위쪽만 레버
    private const float LeverMinZ = 0.222f;       // 이만큼 앞으로 튀어나온 것만 레버
    private const float LeverPivotY = 0.452f;     // 회전축(레버 밑동)
    private const float LeverPivotZ = 0.248f;

    private const float LeverOn = 0f;     // 모델이 만들어진 그대로 = 올라간 상태(ON)
    private const float LeverOff = 42f;   // 앞으로 넘겨 아래로 내린 상태(OFF)

    private readonly Dictionary<PowerConsumer, Node3D> _levers = new();
    private readonly Dictionary<PowerConsumer, Node3D> _tips = new();
    private readonly Dictionary<PowerConsumer, StandardMaterial3D> _ledMats = new();
    private readonly Dictionary<PowerConsumer, double> _rejectUntil = new();
    private readonly Dictionary<PowerConsumer, bool> _shownOn = new();
    private readonly Dictionary<PowerConsumer, double> _flipUntil = new();
    // 채널이 외부 요인으로 꺼졌을 때의 고장 연출(회색 연기 + 그 레버 위 스파크).
    private readonly Dictionary<PowerConsumer, CpuParticles3D> _smoke = new();
    private readonly Dictionary<PowerConsumer, StandardMaterial3D> _arcMats = new();
    private readonly Dictionary<PowerConsumer, OmniLight3D> _arcLights = new();
    private readonly Dictionary<PowerConsumer, double> _arcUntil = new();
    private AudioStreamPlayer3D _hiss;
    private Label3D _capacityLabel;
    private PlayerCharacter _arms;

    private AudioStreamPlayer3D _zap, _crackle;
    private MeshInstance3D _spark;
    private StandardMaterial3D _sparkMat;
    private int _lastCapacity = -1;
    private double _zapSparkUntil;
    private bool _shutdown;
    private bool _built;

    public override void _Ready()
    {
        if (_built) return;
        _built = true;
        if (!Engine.IsEditorHint())
            _arms = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;

        // switch.glb 의 통짜 메시에서 레버 3개를 실제로 떼어내 각자 회전축에 매단다.
        SplitLeversFromModel();

        // LED / 라벨 / 클릭 영역은 떼어낸 레버 위치에 맞춰 붙인다.
        foreach (var (channel, label, x) in Switches)
            BuildSwitch(channel, label, x);

        // 스파크 — 스위치들 위에서 번쩍이는 가산 발광구(평소 꺼짐).
        _sparkMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.85f, 1f), EmissionEnabled = true,
            Emission = new Color(0.75f, 0.9f, 1f), EmissionEnergyMultiplier = 0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        float faceY = LeverPivotY * _modelScale, faceZ = LeverPivotZ * _modelScale;
        _spark = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 6, Rings = 4 },
            Position = new Vector3(0f, faceY + 0.06f, faceZ), MaterialOverride = _sparkMat,
        };
        AddChild(_spark);
        var sparkLight = new OmniLight3D
        {
            Position = new Vector3(0f, 0.02f, 0.01f), LightColor = new Color(0.7f, 0.85f, 1f),
            LightEnergy = 0f, OmniRange = 0.8f, Name = "SparkLight",
        };
        _spark.AddChild(sparkLight);

        if (!Engine.IsEditorHint())
        {
            _zap = MakePlayer("electric_arc", loop: false, db: -3f);
            _crackle = MakePlayer("electric_crackle_loop", loop: true, db: -8f);
            _hiss = MakePlayer("steam_hiss", loop: false, db: -5f);   // 푸슈숙
            foreach (var (channel, _, _) in Switches)
            {
                bool on = GameState.Instance?.IsConsumerPowered(channel) ?? true;
                _shownOn[channel] = on;
                if (_levers.TryGetValue(channel, out var lv))
                    lv.RotationDegrees = lv.RotationDegrees with { X = on ? LeverOn : LeverOff };
            }
            _lastCapacity = GameState.Instance?.PowerCapacity ?? -1;
        }

        _capacityLabel = new Label3D
        {
            Text = "POWER 3 / 3",
            Position = new Vector3(0f, faceY + 0.115f, faceZ - 0.020f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            PixelSize = 0.00042f, FontSize = 40, OutlineSize = 0,
            Modulate = new Color(0.55f, 0.85f, 0.65f),
        };
        AddChild(_capacityLabel);
    }

    private AudioStreamPlayer3D MakePlayer(string key, bool loop, float db)
    {
        string path = $"res://assets/audio/sfx/{key}.wav";
        var p = new AudioStreamPlayer3D { VolumeDb = db, UnitSize = 2.2f, MaxDistance = 10f, Bus = GameSettings.BusSfx };
        if (ResourceLoader.Exists(path)) p.Stream = GD.Load<AudioStream>(path);
        AddChild(p);
        if (loop) p.Finished += () => { if (IsInstanceValid(p) && p.Stream != null && _wantCrackle) p.Play(); };
        return p;
    }
    private bool _wantCrackle;

    // 모델(SwitchModel) 로컬 좌표 → PowerSwitchPanel 로컬 좌표.
    private float _modelScale = 1f;
    private float PanelX(float modelX) => modelX * _modelScale;

    private void BuildSwitch(PowerConsumer channel, string label, float modelX)
    {
        float x = PanelX(modelX);
        float faceY = LeverPivotY * _modelScale;
        float faceZ = LeverPivotZ * _modelScale;

        var ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.04f, 0.04f), EmissionEnabled = true,
            Emission = new Color(0.15f, 0.95f, 0.3f), EmissionEnergyMultiplier = 2.2f,
        };
        _ledMats[channel] = ledMat;
        // 작은 구형 LED — 어느 각도에서 봐도 점으로 보인다(원통은 옆에서 납작한 막대로 보였다).
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.0055f, Height = 0.011f, RadialSegments = 8, Rings = 5 },
            Position = new Vector3(x + 0.0335f, faceY + 0.006f, faceZ + 0.004f),
            MaterialOverride = ledMat,
        });

        // 채널 라벨(LIGHTING/CCTV/SENSOR)은 switch.glb 면판에 이미 새겨져 있어 따로 그리지 않는다.

        BuildBreakdownFx(channel, x, faceY, faceZ);

        // 클릭 영역 — 떼어낸 레버가 실제로 있는 자리를 덮는다.
        var area = new Area3D { InputRayPickable = true, Position = new Vector3(x, faceY + 0.030f, faceZ + 0.010f) };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.048f, 0.10f, 0.060f) } });
        area.InputEvent += (camera, ev, pos, normal, idx) => OnAreaInput(channel, ev);
        AddChild(area);
    }

    // switch.glb 는 본체와 레버가 한 덩어리 메시다. 삼각형 하나하나를 위치로 분류해서
    // 레버 3개를 각각 독립 MeshInstance3D 로 떼어내고, 밑동에 회전축을 세워 매단다.
    // 재질/UV/노멀은 원본 그대로 옮기므로 겉보기는 전혀 달라지지 않는다.
    private void SplitLeversFromModel()
    {
        var model = GetNodeOrNull<Node3D>("SwitchModel");
        var src = model == null ? null : FindMesh(model);
        if (src?.Mesh == null || src.Mesh.GetSurfaceCount() == 0)
        {
            GD.PushWarning("PowerSwitchPanel: SwitchModel 메시를 찾지 못해 레버를 분리하지 못했습니다.");
            return;
        }
        _modelScale = model.Scale.X;

        var arrays = src.Mesh.SurfaceGetArrays(0);
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (verts.Length == 0 || index.Length < 3) return;

        // 삼각형 무게중심이 어느 레버 상자에 드는지 → 0 = 본체, 1..3 = 레버.
        int Bucket(Vector3 c)
        {
            if (c.Y < LeverMinY || c.Z < LeverMinZ) return 0;
            for (int s = 0; s < Switches.Length; s++)
                if (Mathf.Abs(c.X - Switches[s].ModelX) <= LeverHalfWidth) return s + 1;
            return 0;
        }

        int bucketCount = Switches.Length + 1;
        var tris = new List<int>[bucketCount];
        for (int b = 0; b < bucketCount; b++) tris[b] = new List<int>();

        for (int i = 0; i + 2 < index.Length; i += 3)
        {
            var c = (verts[index[i]] + verts[index[i + 1]] + verts[index[i + 2]]) / 3f;
            var list = tris[Bucket(c)];
            list.Add(index[i]); list.Add(index[i + 1]); list.Add(index[i + 2]);
        }

        var material = src.Mesh.SurfaceGetMaterial(0);

        // 본체 — 원본 노드의 메시를 레버가 빠진 것으로 교체한다.
        var bodyMesh = BuildSubMesh(arrays, tris[0], Vector3.Zero);
        if (bodyMesh != null)
        {
            src.Mesh = bodyMesh;
            src.SetSurfaceOverrideMaterial(0, material);
        }

        // 레버 3개 — 밑동을 원점으로 옮겨 회전축 아래에 붙인다.
        for (int s = 0; s < Switches.Length; s++)
        {
            var (channel, _, modelX) = Switches[s];
            var pivotPos = new Vector3(modelX, LeverPivotY, LeverPivotZ);
            var leverMesh = BuildSubMesh(arrays, tris[s + 1], pivotPos);
            if (leverMesh == null) continue;

            var pivot = new Node3D { Position = pivotPos, RotationDegrees = new Vector3(LeverOn, 0f, 0f) };
            model.AddChild(pivot);
            var leverMi = new MeshInstance3D { Mesh = leverMesh };
            pivot.AddChild(leverMi);
            leverMi.SetSurfaceOverrideMaterial(0, material);

            _levers[channel] = pivot;
            // 손 애니메이션이 겨냥할 레버 끝(모델 로컬 기준 레버 꼭대기).
            var tip = new Node3D { Position = new Vector3(0f, 0.711f - LeverPivotY, 0.02f) };
            pivot.AddChild(tip);
            _tips[channel] = tip;
        }
    }

    private static MeshInstance3D FindMesh(Node n)
    {
        if (n is MeshInstance3D mi) return mi;
        foreach (var c in n.GetChildren())
            if (FindMesh(c) is { } found) return found;
        return null;
    }

    // 지정한 삼각형만 뽑아 새 메시로. 정점 속성(노멀/UV/탄젠트/색)은 있는 것만 그대로 옮긴다.
    private static ArrayMesh BuildSubMesh(Godot.Collections.Array srcArrays, List<int> triIndices, Vector3 originShift)
    {
        if (triIndices.Count < 3) return null;

        var srcVerts = srcArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var srcNorms = srcArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var srcTans = srcArrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
        var srcUvs = srcArrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var srcUv2 = srcArrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var srcCols = srcArrays[(int)Mesh.ArrayType.Color].AsColorArray();

        var remap = new Dictionary<int, int>(triIndices.Count);
        var verts = new List<Vector3>();
        var norms = srcNorms.Length > 0 ? new List<Vector3>() : null;
        var tans = srcTans.Length > 0 ? new List<float>() : null;
        var uvs = srcUvs.Length > 0 ? new List<Vector2>() : null;
        var uv2 = srcUv2.Length > 0 ? new List<Vector2>() : null;
        var cols = srcCols.Length > 0 ? new List<Color>() : null;
        var idx = new List<int>(triIndices.Count);

        foreach (int oi in triIndices)
        {
            if (!remap.TryGetValue(oi, out int ni))
            {
                ni = verts.Count;
                remap[oi] = ni;
                verts.Add(srcVerts[oi] - originShift);
                norms?.Add(srcNorms[oi]);
                if (tans != null)
                    for (int k = 0; k < 4; k++) tans.Add(srcTans[oi * 4 + k]);
                uvs?.Add(srcUvs[oi]);
                uv2?.Add(srcUv2[oi]);
                cols?.Add(srcCols[oi]);
            }
            idx.Add(ni);
        }

        var outArrays = new Godot.Collections.Array();
        outArrays.Resize((int)Mesh.ArrayType.Max);
        outArrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        if (norms != null) outArrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        if (tans != null) outArrays[(int)Mesh.ArrayType.Tangent] = tans.ToArray();
        if (uvs != null) outArrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        if (uv2 != null) outArrays[(int)Mesh.ArrayType.TexUV2] = uv2.ToArray();
        if (cols != null) outArrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        outArrays[(int)Mesh.ArrayType.Index] = idx.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outArrays);
        return mesh;
    }

    // 연기 알갱이용 텍스처 — 가운데가 진하고 가장자리로 갈수록 투명해지는 둥근 얼룩.
    // (텍스처 없이 단색 QuadMesh 를 쓰면 네모난 판때기가 그대로 보인다.)
    private static ImageTexture _smokeTex;
    private static ImageTexture SmokePuffTexture()
    {
        if (_smokeTex != null) return _smokeTex;

        const int n = 64;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 90210 };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;          // 0(중심) ~ 1(가장자리)
            float a = Mathf.Clamp(1f - d, 0f, 1f);
            a = a * a * (3f - 2f * a);                              // smoothstep — 경계가 부드럽게
            a *= 0.75f + 0.25f * rng.Randf();                       // 뭉게뭉게한 얼룩감
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        _smokeTex = ImageTexture.CreateFromImage(img);
        return _smokeTex;
    }

    // 레버가 외부 요인으로 내려갈 때 그 자리에서 피어오르는 회색 연기와 튀는 스파크.
    // 평소엔 완전히 꺼져 있고 BreakdownAt() 이 호출될 때만 한 번 터진다.
    private void BuildBreakdownFx(PowerConsumer channel, float x, float faceY, float faceZ)
    {
        var puffPos = new Vector3(x, faceY + 0.055f, faceZ + 0.012f);

        // 사각형 판때기로 보이지 않게, 가장자리가 부드럽게 사라지는 원형 알파 텍스처를 씌운다.
        var smokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.66f, 0.66f, 0.69f, 1f),
            AlbedoTexture = SmokePuffTexture(),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            ParticlesAnimHFrames = 1,
            ParticlesAnimVFrames = 1,
            DisableReceiveShadows = true,
        };
        // 피어오르며 옅어진다 — 처음엔 진하고, 끝에서 완전히 사라진다.
        var ramp = new Gradient();
        ramp.SetOffset(0, 0f);
        ramp.SetColor(0, new Color(0.60f, 0.60f, 0.63f, 0f));
        ramp.AddPoint(0.2f, new Color(0.56f, 0.56f, 0.59f, 0.26f));
        ramp.AddPoint(0.55f, new Color(0.48f, 0.48f, 0.51f, 0.14f));
        ramp.SetOffset(1, 1f);
        ramp.SetColor(1, new Color(0.42f, 0.42f, 0.45f, 0f));

        // 김이 펄펄 올라오는 느낌: 오래 살고, 천천히 위로, 계속 커지면서 흩어진다.
        var scaleCurve = new Curve();
        scaleCurve.AddPoint(new Vector2(0f, 0.35f));
        scaleCurve.AddPoint(new Vector2(0.45f, 0.85f));
        scaleCurve.AddPoint(new Vector2(1f, 1.4f));

        var smoke = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = 34,
            Lifetime = 2.2,
            Explosiveness = 0.12f,     // 한 번에 뻥 터지지 않고 계속 뿜어져 나온다
            Randomness = 0.55f,
            Position = puffPos,
            Mesh = new QuadMesh { Size = new Vector2(0.028f, 0.028f) },
            MaterialOverride = smokeMat,
            Direction = Vector3.Up,
            Spread = 18f,
            InitialVelocityMin = 0.06f,
            InitialVelocityMax = 0.16f,
            Gravity = new Vector3(0f, 0.02f, 0f),
            DampingMin = 0.05f,
            DampingMax = 0.2f,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.6f,
            ScaleAmountCurve = scaleCurve,
            AngleMin = -180f,
            AngleMax = 180f,
            AngularVelocityMin = -22f,
            AngularVelocityMax = 22f,
            ColorRamp = ramp,
        };
        AddChild(smoke);
        _smoke[channel] = smoke;

        var arcMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.8f, 0.9f, 1f, 0f), EmissionEnabled = true,
            Emission = new Color(0.8f, 0.92f, 1f), EmissionEnergyMultiplier = 0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.017f, Height = 0.034f, RadialSegments = 6, Rings = 4 },
            Position = puffPos, MaterialOverride = arcMat,
        });
        _arcMats[channel] = arcMat;

        var arcLight = new OmniLight3D
        {
            Position = puffPos + new Vector3(0f, 0.01f, 0f),
            LightColor = new Color(0.72f, 0.86f, 1f), LightEnergy = 0f, OmniRange = 0.6f,
        };
        AddChild(arcLight);
        _arcLights[channel] = arcLight;
    }

    // 전력이 모자라 이 채널이 강제로 꺼졌다 — 레버가 내려간 자리에서 연기 + 스파크 + 푸슈숙/치직.
    private void BreakdownAt(PowerConsumer channel)
    {
        if (Engine.IsEditorHint()) return;

        if (_smoke.TryGetValue(channel, out var smoke))
        {
            smoke.Emitting = false;
            smoke.Restart();
            smoke.Emitting = true;
        }
        _arcUntil[channel] = Time.GetTicksMsec() / 1000.0 + 0.75;
        _rejectUntil[channel] = Time.GetTicksMsec() / 1000.0 + 0.9;   // LED 붉게 명멸

        _hiss?.Play();
        _zap?.Play();
        _zapSparkUntil = Time.GetTicksMsec() / 1000.0 + 0.45;
        Sfx.Instance?.Play("relay_click", -6f, 0.75f);
    }

    private void OnAreaInput(PowerConsumer channel, InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        BeginToggle(channel);
    }

    // 확대 상태에서는 메인 카메라 광선을 패널 평면에 직접 투영한다. 작은 Area3D가
    // 프레임 입력 순서에 밀리더라도 화면에 보이는 레버를 누르면 확실하게 반응한다.
    public bool TryInteractRay(Vector3 rayOriginWorld, Vector3 rayDirectionWorld)
    {
        Transform3D inv = GlobalTransform.AffineInverse();
        Vector3 origin = inv * rayOriginWorld;
        Vector3 direction = (inv.Basis * rayDirectionWorld).Normalized();
        float faceZ = LeverPivotZ * _modelScale + 0.01f;
        if (Mathf.Abs(direction.Z) < 0.0001f) return false;
        float distance = (faceZ - origin.Z) / direction.Z;
        if (distance <= 0f) return false;

        Vector3 hit = origin + direction * distance;
        float faceY = LeverPivotY * _modelScale;
        if (Mathf.Abs(hit.Y - faceY) > 0.16f) return false;

        PowerConsumer nearest = Switches[0].Channel;
        float nearestX = float.MaxValue;
        foreach (var (candidate, _, modelX) in Switches)
        {
            float dx = Mathf.Abs(hit.X - PanelX(modelX));
            if (dx < nearestX) { nearestX = dx; nearest = candidate; }
        }
        if (nearestX > 0.075f) return false;

        BeginToggle(nearest);
        return true;
    }

    private void BeginToggle(PowerConsumer channel)
    {
        if (GameState.Instance == null) return;
        if (GameState.Instance.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return;
        if (_flipUntil.GetValueOrDefault(channel) > Time.GetTicksMsec() / 1000.0) return; // 연타 방지

        bool turningOn = !GameState.Instance.IsConsumerPowered(channel);
        Vector3 tipW = _tips.TryGetValue(channel, out var tip) ? tip.GlobalPosition : GlobalPosition;
        _flipUntil[channel] = Time.GetTicksMsec() / 1000.0 + 1.0;

        // 실제 토글을 고정 타이머가 아니라 오른손 검지가 레버에 닿은 프레임에 실행한다.
        // 팔 리치 시간이 달라져도 손보다 레버가 먼저 움직이는 어색함이 생기지 않는다.
        if (_arms != null)
            _arms.PlaySwitchFlip(turningOn, tipW, () => DoToggle(channel));
        else
            DoToggle(channel);
    }

    // 검지가 레버에 닿는 순간.
    private void DoToggle(PowerConsumer channel)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        bool ok = gs.TryTogglePower(channel);
        if (ok)
        {
            bool on = gs.IsConsumerPowered(channel);
            AnimateLever(channel, on);
            Sfx.Instance?.Play("relay_click", -3f);           // 딱!
            Sfx.Instance?.Play("switch", -8f, on ? 1.05f : 0.9f);
        }
        else
        {
            // 용량 부족 — 레버를 올리려다 중간에 걸려 도로 내려앉는다. LED가 붉게 깜빡, 지지직.
            Sfx.Instance?.Play("switch_fail", -4f);
            _zap?.Play();
            _zapSparkUntil = Time.GetTicksMsec() / 1000.0 + 0.35;
            _rejectUntil[channel] = Time.GetTicksMsec() / 1000.0 + 0.8;
            if (_levers.TryGetValue(channel, out var lever))
            {
                // 시도한 방향(ON)으로 40%만 올라갔다가 실패해서 원위치로 되돌아온다.
                float rest = _shownOn.GetValueOrDefault(channel, true) ? LeverOn : LeverOff;
                float target = gs.IsConsumerPowered(channel) ? LeverOff : LeverOn;
                float strain = Mathf.Lerp(rest, target, 0.4f);
                var t = CreateTween();
                t.TweenProperty(lever, "rotation_degrees:x", strain, 0.10).SetTrans(Tween.TransitionType.Quad);
                t.TweenProperty(lever, "rotation_degrees:x", Mathf.Lerp(rest, target, 0.26f), 0.06);
                t.TweenProperty(lever, "rotation_degrees:x", strain, 0.06);   // 덜컥덜컥 걸림
                t.TweenProperty(lever, "rotation_degrees:x", rest, 0.18).SetTrans(Tween.TransitionType.Back);
                Sfx.Instance?.Play("metal_clang", -12f, 1.35f);
            }
        }
    }

    private void AnimateLever(PowerConsumer channel, bool on)
    {
        _shownOn[channel] = on;
        if (!_levers.TryGetValue(channel, out var lever)) return;
        var t = CreateTween();
        t.TweenProperty(lever, "rotation_degrees:x", on ? LeverOn : LeverOff, 0.1)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        var gs = GameState.Instance;
        if (gs == null) return;
        double now = Time.GetTicksMsec() / 1000.0;

        // ── 전력 포인트 차감 감지 → 지지직 스파크 + 하강음 ──
        int cap = gs.PowerCapacity;
        if (_lastCapacity >= 0 && cap < _lastCapacity)
        {
            _zap?.Play();
            _zapSparkUntil = now + 0.5;
            Sfx.Instance?.Play("power_point_lost", -3f);
            AmbientOverlay.Instance?.Flash(0.35f);
        }
        _lastCapacity = cap;

        // ── SHUT DOWN (전력 0) — 계속 파지직 ──
        bool blackout = cap == 0 && gs.CurrentPhase == GamePhase.Live;
        if (blackout != _shutdown)
        {
            _shutdown = blackout;
            AmbientOverlay.Instance?.SetShutdown(blackout);
            if (blackout) Sfx.Instance?.Play("power_down", -2f);
        }
        _wantCrackle = blackout;
        if (blackout && _crackle != null && !_crackle.Playing && _crackle.Stream != null) _crackle.Play();
        if (!blackout && _crackle != null && _crackle.Playing) _crackle.Stop();

        // ── 스파크 발광 ──
        float sparkE;
        if (blackout)
            sparkE = 0.4f + 2.2f * Mathf.Abs(Mathf.Sin((float)(now * 17.0)) * (0.4f + 0.6f * GD.Randf()));
        else if (now < _zapSparkUntil)
            sparkE = 1.5f + 4f * GD.Randf();
        else
            sparkE = Mathf.Lerp(_sparkMat.EmissionEnergyMultiplier, 0f, (float)delta * 12f);
        _sparkMat.EmissionEnergyMultiplier = sparkE;
        _sparkMat.AlbedoColor = _sparkMat.AlbedoColor with { A = Mathf.Clamp(sparkE * 0.25f, 0f, 0.9f) };
        if (_spark.GetNodeOrNull<OmniLight3D>("SparkLight") is { } sl) sl.LightEnergy = Mathf.Min(sparkE, 4f);

        // ── 레버 자동 반영(외부 차단) + LED ──
        foreach (var (channel, _, _) in Switches)
        {
            bool on = gs.IsConsumerPowered(channel);
            // 플레이어 조작이 아닌데 상태가 바뀐 = 전력이 모자라 강제로 차단된 것.
            // 레버가 저절로 내려가고, 그 자리에서 연기와 스파크가 터진다.
            if (now >= _flipUntil.GetValueOrDefault(channel) && _shownOn.GetValueOrDefault(channel, on) != on)
            {
                AnimateLever(channel, on);
                if (!on) BreakdownAt(channel);
            }

            // 채널별 아크 섬광(고장 직후 짧게).
            float arc = _arcUntil.GetValueOrDefault(channel) > now ? 1.2f + 4.5f * GD.Randf() : 0f;
            if (_arcMats.TryGetValue(channel, out var am))
            {
                am.EmissionEnergyMultiplier = Mathf.Lerp(am.EmissionEnergyMultiplier, arc, arc > 0f ? 1f : (float)delta * 10f);
                am.AlbedoColor = am.AlbedoColor with { A = Mathf.Clamp(am.EmissionEnergyMultiplier * 0.2f, 0f, 0.85f) };
            }
            if (_arcLights.TryGetValue(channel, out var al))
                al.LightEnergy = Mathf.Min(_arcMats[channel].EmissionEnergyMultiplier, 3.5f);

            var mat = _ledMats[channel];
            bool rejecting = _rejectUntil.GetValueOrDefault(channel) > now;
            if (rejecting)
            {
                float k = 0.5f + 0.5f * Mathf.Sin((float)(now * 40.0));
                mat.Emission = new Color(1f, 0.1f, 0.05f);
                mat.EmissionEnergyMultiplier = 1f + 3f * k;
            }
            else
            {
                mat.Emission = on ? new Color(0.15f, 0.95f, 0.3f) : new Color(0.45f, 0.05f, 0.03f);
                mat.EmissionEnergyMultiplier = on ? 2.4f : 0.7f;
            }
        }

        int max = Config.Instance.Data.PowerCapacityMax;
        _capacityLabel.Text = blackout ? "SHUT DOWN" : $"POWER {cap} / {max}";
        _capacityLabel.Modulate = cap >= max ? new Color(0.55f, 0.85f, 0.65f)
            : cap == 0 ? new Color(0.95f, 0.2f, 0.16f)
            : new Color(0.95f, 0.7f, 0.25f);
    }
}
