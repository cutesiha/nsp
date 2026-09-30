using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 책상 위 "관리자 패드" — 산업용 태블릿. 지침 · 단서 · 직원 앱이 들어 있다(PadView).
// 평소에는 책상 왼쪽 거치대에 비스듬히 기대어 있다(예전 경고 단말기 자리).
//
//   꺼내기    패드 클릭 / Tab → 왼손이 거치대에서 집어 화면 앞으로 들어 올리고, 시선이 내려간다.
//   조작      화면 클릭은 ControlRoom3DController 가 이 표면(모달)으로 전달한다.
//             클릭에 손 · 패드 · 카메라는 절대 움직이지 않는다 — 반응은 화면(UI) 안에서만 준다.
//   내려놓기  우클릭(홈에서) / Tab → 거치대로 돌아간다. 앱 안에서 우클릭은 한 단계 뒤로.
//
// 손과 패드: 왼손은 패드의 그립 마커(GripPoint — 위치 + 방향)를 따라간다. 패드는 손을 쥔 순간부터
// 왼손 소켓에 붙어 따라온다(Phone3D 수화기와 같은 lerp). 그래서 손가락과 패드의 관계는 마커 하나로
// 정해지고, 손이 패드를 뚫거나 반대로 쥐지 않는다. 파지는 「뒷면 받침」 — 손 전체가 화면 평면 뒤다
// (GripLocal 주석 · AdminPadShot 의 「파지 규칙」 검사 참고).
//
// 전력: 전력 패널의 세 번째 채널(PowerConsumer.Sensor — 표시는 「패드」)이 이 기기의 전원이다.
// 꺼지면 화면이 글리치 뒤 꺼지고, 들고 있었다면 내려놓는다. 다시 켜지면 부팅 화면을 거친다.
//
// 든 동안에도 근무 시간은 그대로 흐른다(PauseWhileHeld 를 켜면 멈춘다 — 플레이테스트에서
// "너무 사기"라 껐다). 통화 중에는 꺼낼 수 없고, 든 동안에는 전화기를 집을 수 없다 —
// 왼손 · 오른손이 서로의 물건을 동시에 쥐지 않게. 로그 · 대화 기록 · 오늘의 업무(L · D · T)는
// 게임 밖 UI 라 든 채로도 열린다.
// 판정은 하지 않는다. 자료는 ClueBoard · InterviewEvidenceBoard 가 쥐고 있다.
//
// 거치대 위 화면: 근무 · 휴게시간(InService)에는 홈(지침 · 단서 · 직원)이 켜져 있다 — 아이콘을 누르면
// 그 앱을 연 채로 집어 든다. 그 밖의 시간(배치 · 타이틀 등)에는 어두운 잠금 화면이다.
//
// [Tool] — 형상을 코드로 만들지만 에디터 뷰포트에도 보이게 한다(AlertTerminalProp · PowerSwitchPanel 과 같다).
// 씬에서 AdminPad 노드를 옮기면 거치대와 패드가 함께 움직이고, 크기 · 기울기 Export 를 바꾸면 곧바로
// 다시 그린다. 에디터에서는 화면 UI(PadView)를 띄우지 않고 켜진 화면 색만 칠한다.
// EditorPreviewHeld 를 켜면 들고 있을 자리(HoldOffset · 기울기)를 좌석 카메라 기준으로 보여 준다.
[Tool]
public partial class AdminPad3D : Node3D, IProjectionSurface, ISurfacePressListener
{
    [Export] public NodePath PlayerPath = "../PlayerCharacter";
    [Export] public NodePath RigPath = "../../PlayerSeatRig";
    // 화면 UI 의 논리 캔버스(PadView.Layout 과 같다 — PadView 가 이 좌표로 짠다).
    [Export] public Vector2I CanvasSize = new(1120, 700);
    // 본체 크기(m) — 가로 · 두께 · 세로(화면을 위로 눕힌 상태 기준).
    [Export] public Vector3 BodySize = new(0.30f, 0.012f, 0.21f);
    // 화면이 앞면에서 차지하는 크기(m). 나머지는 베젤(아래쪽 베젤이 조금 두껍다).
    [Export] public Vector2 ScreenSize = new(0.272f, 0.170f);
    // 들고 있을 때의 자리 — 자리에 앉은 카메라 기준(오른쪽 +X · 위 +Y · 앞 -Z).
    // 화면은 카메라 화면과 평행하다(HoldFlatToCamera) — 눈보다 아래에 들어도 기울지 않는다.
    // 눈에서 약 28cm(파지 밀림 8cm 를 빼면 약 20cm) — 1920 화면에서 본체 가로가 약 69% 를 차지한다.
    // (패드는 왼손 소켓을 따라가므로 실제 자리는 여기서 파지 깊이만큼 눈 쪽으로 당겨진다 —
    //  HoldDriftMeters 로 그 양을 잰다. 파지를 바꾸면 이 값도 같이 다시 맞춰야 크기가 유지된다.)
    [Export] public Vector3 HoldOffset = new(0.04f, -0.105f, -0.285f);
    // 켜면 화면이 카메라 화면과 평행하다(시점과 각도가 어긋나지 않는 "일자" 파지 — 기본).
    // 끄면 화면 법선이 눈을 향한다 — 눈보다 아래에 들고 있으므로 자연히 뒤로 기운다(약 25°).
    [Export] public bool HoldFlatToCamera = true;
    // 위 기준에서 더 뒤로(+) / 앞으로(-) 기울이는 각(도).
    [Export] public float HoldExtraTiltDeg = 0f;
    // 화면 안에서 패드를 갸웃 기울이는 각(도). 양수 = 오른쪽이 아래로. 0 = 카메라 수평과 나란히.
    // (눈을 향하는 파지(HoldFlatToCamera=false)에서는 원근 때문에 오른쪽이 3~4° 내려가 보이므로
    //  그때는 -1.4 정도가 화면상 +2° 였다 — AdminPadShot pose 의 「기울기」 출력으로 잰다.)
    [Export] public float HoldRollDeg = 0f;
    // 왼손 그립 마커 — 본체 로컬 위치 · 방향. 손 소켓(손바닥 한가운데)이 이 마커에 정확히 겹친다.
    //
    // 「뒷면 받침 파지」 — 손은 통째로 화면 평면 **뒤**에 있다. 앞면(화면 쪽)에는 손목도 엄지도
    // 오지 않는다. 플레이어에게 보이는 것은 패드와, 패드 실루엣 왼쪽 아래로 조금 나오는
    // 손목 · 팔뚝뿐이다.
    //   위치: 왼쪽(-X) · 화면 아래쪽(+Z, 본체 -Z 가 화면 위쪽이다) · 뒷면 쪽(-Y).
    //         손목은 패드 아래 모서리 바깥으로 빠지고 팔뚝은 그대로 아래로 내려간다.
    //   방향: X 회전 73° = (90 - 손바닥 눕힘 17°) — 손바닥이 뒷면(본체 +Y)을 보고, 손가락이
    //         본체 -Z(화면 위쪽)로 뻗어 뒷면을 받친다. 눕힘을 줄이면 손목이 앞으로 들리고,
    //         키우면 팔이 못 닿아 패드가 손에 끌려온다 — 17° 가 둘 사이의 자리다.
    //   (AdminPadShot pose 의 「파지 규칙」 검사 — 왼손 본 15개 + 손 메시가 전부 화면 평면
    //    5mm 뒤 — 를 통과하는 값이다. 값을 바꾸면 반드시 그 검사를 다시 돌릴 것.)
    [Export] public Vector3 GripLocal = new(-0.101f, -0.056f, 0.092f);
    [Export] public Vector3 GripRotDeg = new(73f, 0f, 0f);
    // 든 동안 왼팔 메시를 숨긴다(비상용 스위치). 평소에는 끈다 — 지금 파지는 손이 보여도
    // 화면을 1픽셀도 가리지 않는다(AdminPadShot pose 의 「파지 규칙」 검사).
    // 파지를 손보다가 손이 화면을 덮게 되면 이걸 켜서 급한 불을 끌 수 있다.
    // 집는 동작 · 내려놓는 동작은 켜도 그대로 보이고, 든 상태에서만 감춘다.
    [Export] public bool HideLeftArmWhileHolding = false;
    // 거치대에 기댄 각도 — 책상 면에서 잰 각(도).
    [Export] public float CradleLeanDeg = 58f;
    // 집어 들고 · 내려놓는 데 걸리는 시간. 뻗기 · 쥐기 · 손 펴기 시간도 여기에 비례한다
    // (PlayerCharacter.PlayPadPickup / PlayPadPutDown) — 이 값 하나로 전체 속도를 조절한다.
    [Export] public float LiftSeconds = 0.26f;
    // 손을 따라가는 부드러움(Phone3D 수화기와 같은 값).
    [Export] public float HandFollowSharpness = 22f;
    // 근무 · 휴게시간에 거치대 위 화면(홈)의 밝기. 들었을 때가 1 이다.
    [Export(PropertyHint.Range, "0,1,0.01")] public float StowedBrightness = 0.85f;

    // 에디터 전용 — 켜면 패드를 거치대 대신 들고 있을 자리에 띄운다(HoldOffset 등을 맞출 때).
    // 좌석 카메라(PlayerSeatRig/Camera3D) 기준이다. 게임 실행에는 영향이 없다.
    [ExportGroup("에디터 미리보기")]
    [Export] public bool EditorPreviewHeld = false;

    public static AdminPad3D Instance { get; private set; }

    // 패드를 쓰는 시간 — 근무 · 휴게시간. 이 동안은 거치대에 놓여 있어도 홈 화면이 켜져 있다.
    public static bool InService => GameState.Instance?.CurrentPhase is GamePhase.Live or GamePhase.Rest;

    // 패드가 거치대에 놓여 있지 않은 동안(집으러 가는 중 · 들어 올리는 중 · 들고 있음 · 내려놓는 중)
    // 근무 시간을 멈출 것인가. 기본은 끔 — 든 채로 시간이 멈추면 감시 · 통화 압박이 사라져 너무 쉬웠다.
    [Export] public bool PauseWhileHeld = false;
    public static bool PausesGame =>
        Instance != null && Instance.PauseWhileHeld && Instance._state != PadState.Stowed;

    // 패드 전원 — 근무 중에는 전력 패널의 패드 채널을 따른다. 근무 밖(휴게시간 등)은 늘 켜져 있다.
    public static bool PadPowered =>
        GameState.Instance == null || GameState.Instance.CurrentPhase != GamePhase.Live
        || GameState.Instance.IsConsumerPowered(PowerConsumer.Sensor);

    private enum PadState { Stowed, Reaching, Lifting, Held, Lowering }
    private PadState _state = PadState.Stowed;

    public bool IsOpen => _state != PadState.Stowed;
    public bool IsHeld => _state == PadState.Held;
    public bool IsScreenOn => _powered;

    public SubViewport TargetViewport => _vp;
    public PadView View => _view;

    private SubViewport _vp;
    private PadView _view;
    private Node3D _body;
    private MeshInstance3D _screen;
    private ShaderMaterial _screenMat;
    private StandardMaterial3D _ledMat;
    private Marker3D _grip;
    private PlayerCharacter _player;
    private SeatedCameraRig _rig;
    private Tween _moveTween, _brightTween;
    private Transform3D _hold;
    private bool _attached;       // 패드가 왼손 소켓을 따라가는 중
    private bool _powered = true;
    private double _awakeUntil;   // 거치 상태에서 부팅 화면 등을 그리는 동안
    private bool _stowedAlert;    // 거치 중 사고 예고가 떠 있는가(잠금 화면 점멸)
    private bool _inService;      // 지금 근무 · 휴게시간인가(거치대 위 화면이 홈인가)
    private float _ledPhase;

    public Vector3 GripWorld => _grip?.GlobalPosition ?? GlobalPosition;

    // 들고 있는 동안 패드가 설계된 자리(HoldOffset)에서 얼마나 밀렸는가(m).
    // 패드는 왼손 소켓을 따라가므로, 팔 IK 가 그립 마커까지 못 닿으면 패드가 손에 끌려간다.
    // 그러면 눈에서의 거리 = 화면에서 보이는 크기가 파지 값에 따라 흔들린다. 0 이어야 한다.
    public float HoldDriftMeters => _state == PadState.Held && _body != null
        ? _body.GlobalPosition.DistanceTo(_hold.Origin) : 0f;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) { BuildEditorPreview(); return; }
        Instance = this;
        _player = GetNodeOrNull<PlayerCharacter>(PlayerPath);
        _rig = GetNodeOrNull<SeatedCameraRig>(RigPath);

        _vp = new SubViewport
        {
            // 손에 들고 코앞에서 읽는 화면이다 — 배치표처럼 CRT 보다 높은 해상도로 그린다.
            Size = ControlRoom3DController.ViewportSize(CanvasSize,
                ControlRoom3DController.DocumentSupersample, ControlRoom3DController.DocumentMinRenderScale),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            HandleInputLocally = true,
            GuiDisableInput = false,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(_vp);
        // 화면은 캔버스와 같은 1120×700 좌표로 짠다(PadView.Layout).
        _view = new PadView();
        ControlRoom3DController.AddScaledView(_vp, _view, CanvasSize,
            ControlRoom3DController.DocumentSupersample, ControlRoom3DController.DocumentMinRenderScale);

        if (HasAuthoredParts()) AdoptAuthoredParts(_vp.GetTexture());
        else { BuildCradle(); BuildBody(_vp.GetTexture()); }
        _body.GlobalTransform = RestTransform();

        _powered = PadPowered;
        _inService = InService;
        _view.SetStowed(true, locked: !_inService);
        SetScreenAwake(false);
        if (!_powered) _view.PowerOff(glitch: false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── 에디터 미리보기 ──────────────────────────────────────────────────

    private string _editorShape = "";

    // 거치대 · 본체를 (다시) 만든다. 스크립트를 다시 빌드하면 C# 필드가 비워지므로 이름으로 옛것을 치운다.
    // 여기서 만든 노드는 Owner 가 없어 씬 파일에 저장되지 않는다.
    // 씬에 PadCradle · PadBody 가 이미 author 되어 있으면(메인 씬이 그렇다) 만들지 않고 그것을 쓴다.
    private void BuildEditorPreview()
    {
        if (HasAuthoredParts()) { AdoptAuthoredParts(null); _editorShape = EditorShape(); PlaceEditorPreview(); return; }
        foreach (string n in new[] { "PadCradle", "PadBody" })
            if (GetNodeOrNull(n) is { } old) { RemoveChild(old); old.QueueFree(); }
        BuildCradle();
        BuildBody(null);
        _editorShape = EditorShape();
        PlaceEditorPreview();
    }

    // ── 씬에 들어 있는 기기 ───────────────────────────────────────────────
    //
    // 다른 책상 기기(경고 단말기 · 전력 패널)처럼 패드도 메인 씬 안에 실제 노드로 들어 있다.
    // 그래야 에디터에서 보이고, 고르고, 옮기고, 재질을 바꿀 수 있다.
    // 여기서는 그 노드를 그대로 집어 쓰기만 한다 — 모양을 코드로 다시 만들지 않는다.
    // (씬에 없으면 예전처럼 코드가 만든다 — 검사용 빈 씬에서도 패드가 뜨도록.)
    private bool HasAuthoredParts() =>
        GetNodeOrNull<Node3D>("PadBody") != null && GetNodeOrNull<Node3D>("PadCradle") != null;

    private void AdoptAuthoredParts(Texture2D screenTex)
    {
        _body = GetNode<Node3D>("PadBody");
        _body.TopLevel = true;                      // 들면 손을 따라가야 하므로 부모와 끊는다
        _screen = _body.GetNode<MeshInstance3D>("Screen");
        _grip = _body.GetNode<Marker3D>("GripPoint");
        _grip.Position = GripLocal;
        _grip.RotationDegrees = GripRotDeg;

        // 표시등 재질은 매 프레임 밝기를 바꾼다 — 씬에 저장된 원본을 건드리지 않게 복제해서 쓴다.
        _ledMat = DuplicateMaterial(_body.GetNodeOrNull<MeshInstance3D>("BadgeLed"));
        _cradleLed = DuplicateMaterial(GetNodeOrNull<MeshInstance3D>("PadCradle/Led"));

        if (screenTex != null)
        {
            _screenMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crt_screen.gdshader") };
            _screenMat.SetShaderParameter("screen_tex", screenTex);
            _screenMat.SetShaderParameter("region_min", Vector2.Zero);
            _screenMat.SetShaderParameter("region_max", Vector2.One);
            _screenMat.SetShaderParameter("noise_strength", 0.012f);
            _screen.MaterialOverride = _screenMat;
        }

        if (Engine.IsEditorHint()) return;
        if (_body.GetNodeOrNull<Area3D>("ClickArea") is { } area)
        {
            area.InputRayPickable = true;
            area.InputEvent += OnAreaInput;
        }
    }

    private static StandardMaterial3D DuplicateMaterial(MeshInstance3D mi)
    {
        if (mi?.MaterialOverride is not StandardMaterial3D m) return null;
        var copy = (StandardMaterial3D)m.Duplicate();
        mi.MaterialOverride = copy;
        return copy;
    }

    private string EditorShape() => $"{BodySize}|{ScreenSize}|{CradleLeanDeg}";

    private void PlaceEditorPreview()
    {
        _hold = HoldTransform();
        _body.GlobalTransform = EditorPreviewHeld ? _hold : RestTransform();
    }

    private void TickEditorPreview()
    {
        if (_body == null || !IsInstanceValid(_body) || EditorShape() != _editorShape) BuildEditorPreview();
        else PlaceEditorPreview();
    }

    // ── 모양 ─────────────────────────────────────────────────────────────

    private static StandardMaterial3D Metal(Color c, float rough = 0.45f) =>
        new() { AlbedoColor = c, Metallic = 0.55f, Roughness = rough };

    // 거치대 — 받침판 + 앞 턱 + 뒤로 기운 등받이. 패드 아래 가장자리가 턱에 걸린다.
    private void BuildCradle()
    {
        var mat = Metal(new Color(0.08f, 0.09f, 0.10f));
        var cradle = new Node3D { Name = "PadCradle" };
        AddChild(cradle);
        float w = BodySize.X * 0.86f;

        cradle.AddChild(new MeshInstance3D
        {
            Name = "Base", Mesh = new BoxMesh { Size = new Vector3(w, 0.012f, 0.13f) },
            Position = new Vector3(0f, 0.006f, 0.0f), MaterialOverride = mat,
        });
        cradle.AddChild(new MeshInstance3D
        {
            Name = "Lip", Mesh = new BoxMesh { Size = new Vector3(w, 0.022f, 0.010f) },
            Position = new Vector3(0f, 0.016f, LipZ + 0.008f), MaterialOverride = mat,
        });
        // 등받이 — 패드 뒷면에 붙어 같은 각도로 기운다.
        var rest = RestLocal();
        var back = new MeshInstance3D
        {
            Name = "Back", Mesh = new BoxMesh { Size = new Vector3(w * 0.8f, 0.008f, BodySize.Z * 0.62f) },
            MaterialOverride = mat,
        };
        back.Transform = new Transform3D(rest.Basis,
            rest.Origin - rest.Basis.Y * (BodySize.Y * 0.5f + 0.005f) - rest.Basis.Z * (BodySize.Z * 0.12f));
        cradle.AddChild(back);
        // 받침판의 작은 상태등 — 패드 전원이 들어와 있으면 청록.
        _cradleLed = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.08f, 0.08f), EmissionEnabled = true,
            Emission = new Color(0.3f, 0.95f, 1f), EmissionEnergyMultiplier = 1.4f,
        };
        cradle.AddChild(new MeshInstance3D
        {
            Name = "Led", Mesh = new BoxMesh { Size = new Vector3(0.018f, 0.003f, 0.004f) },
            Position = new Vector3(w * 0.38f, 0.0135f, LipZ + 0.016f), MaterialOverride = _cradleLed,
        });
    }

    private StandardMaterial3D _cradleLed;
    private const float LipZ = 0.045f;      // 앞 턱의 자리(거치대 로컬 +Z = 관리자 쪽)
    private const float LipTop = 0.024f;

    // screenTex 가 null 이면(에디터) 화면 UI 대신 켜진 화면 색만 칠한다.
    private void BuildBody(Texture2D screenTex)
    {
        // 본체는 거치대와 따로 움직인다 — 들어 올리면 손을 따라간다.
        _body = new Node3D { Name = "PadBody", TopLevel = true };
        AddChild(_body);

        _body.AddChild(new MeshInstance3D
        {
            Name = "Shell", Mesh = new BoxMesh { Size = BodySize },
            MaterialOverride = Metal(new Color(0.10f, 0.115f, 0.125f)),
        });

        // 화면 테두리 안쪽의 검은 유리 — 화면이 꺼져 있을 때도 "화면"으로 보이게.
        float screenZ = 0.004f;   // 아래 베젤이 조금 두껍다
        _body.AddChild(new MeshInstance3D
        {
            Name = "Glass",
            Mesh = new QuadMesh { Size = ScreenSize + new Vector2(0.008f, 0.008f) },
            Position = new Vector3(0f, BodySize.Y * 0.5f + 0.0004f, screenZ),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.01f, 0.015f, 0.02f), Roughness = 0.15f, Metallic = 0.2f,
            },
        });

        Material screenMat;
        if (screenTex != null)
        {
            _screenMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crt_screen.gdshader") };
            _screenMat.SetShaderParameter("screen_tex", screenTex);
            _screenMat.SetShaderParameter("region_min", Vector2.Zero);
            _screenMat.SetShaderParameter("region_max", Vector2.One);
            _screenMat.SetShaderParameter("noise_strength", 0.012f);
            screenMat = _screenMat;
        }
        else
        {
            screenMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.03f, 0.1f, 0.12f), EmissionEnabled = true,
                Emission = new Color(0.1f, 0.42f, 0.5f), EmissionEnergyMultiplier = 0.6f,
            };
        }
        _screen = new MeshInstance3D
        {
            Name = "Screen",
            Mesh = new QuadMesh { Size = Vector2.One },
            // QuadMesh 는 +Z 를 본다 → X 로 -90° 눕혀 본체 윗면(+Y)을 보게 한다. 화면 위쪽 = 본체 -Z.
            Scale = new Vector3(ScreenSize.X, ScreenSize.Y, 1f),
            Position = new Vector3(0f, BodySize.Y * 0.5f + 0.0008f, screenZ),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            MaterialOverride = screenMat,
        };
        _body.AddChild(_screen);

        // 새 단서가 있으면 깜빡이는 작은 호박색 표시등 — 패드의 뱃지.
        _ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.15f, 0.05f), EmissionEnabled = true,
            Emission = new Color(1f, 0.72f, 0.25f), EmissionEnergyMultiplier = 0f,
        };
        _body.AddChild(new MeshInstance3D
        {
            Name = "BadgeLed",
            Mesh = new BoxMesh { Size = new Vector3(0.014f, 0.002f, 0.005f) },
            Position = new Vector3(BodySize.X * 0.5f - 0.024f, BodySize.Y * 0.5f + 0.0006f, -BodySize.Z * 0.5f + 0.007f),
            MaterialOverride = _ledMat,
        });

        _grip = new Marker3D { Name = "GripPoint", Position = GripLocal, RotationDegrees = GripRotDeg };
        _body.AddChild(_grip);

        if (Engine.IsEditorHint()) return;
        // 거치대의 패드를 누르면 꺼낸다.
        var area = new Area3D { Name = "ClickArea", InputRayPickable = true };
        area.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = BodySize + new Vector3(0.02f, 0.03f, 0.02f) },
        });
        area.InputEvent += OnAreaInput;
        _body.AddChild(area);
    }

    // 거치대에 기댄 자리(이 노드 로컬). 화면이 관리자 쪽(+Z)으로 CradleLeanDeg 만큼 일어선다.
    // 패드 아래 가장자리가 앞 턱 위에 걸린다.
    private Transform3D RestLocal()
    {
        var basis = new Basis(Vector3.Right, Mathf.DegToRad(CradleLeanDeg));
        Vector3 bottomEdge = basis * new Vector3(0f, -BodySize.Y * 0.5f, BodySize.Z * 0.5f);
        var lip = new Vector3(0f, LipTop, LipZ);
        return new Transform3D(basis, lip - bottomEdge);
    }

    private Transform3D RestTransform() => GlobalTransform * RestLocal();

    // 그립 마커의 본체 로컬 트랜스폼(Export 값을 바꾸면 곧바로 반영).
    private Transform3D GripLocalTransform()
    {
        if (_grip != null)
        {
            _grip.Position = GripLocal;
            _grip.RotationDegrees = GripRotDeg;
            return _grip.Transform;
        }
        return new Transform3D(Basis.FromEuler(GripRotDeg * (Mathf.Pi / 180f)), GripLocal);
    }

    // 들고 있을 자리 — 자리에 똑바로 앉은 카메라 앞 아래.
    // HoldFlatToCamera: 화면 법선 = 카메라 뒤(+Z) — 화면이 카메라 화면과 평행한 "일자" 파지.
    // 아니면 화면 법선이 눈을 향한다(눈보다 아래에 드므로 뒤로 기운다).
    private Transform3D HoldTransform()
    {
        Transform3D cam = SeatCamera();
        Vector3 origin = cam * HoldOffset;
        Vector3 up = HoldFlatToCamera
            ? cam.Basis.Z.Normalized()                                    // 본체 +Y(화면) ∥ 시선 축
            : (cam.Origin - origin).Normalized();                         // 본체 +Y(화면) → 눈
        // 화면의 위아래를 카메라의 위아래에 맞춘다 — 기준을 가로축에서 뽑으면 패드를 눈 축에서
        // 옆으로 비켜 든 만큼(HoldOffset.X) 화면이 저절로 돌아간다(예전엔 왼쪽이 4° 내려갔다).
        Vector3 camDown = -cam.Basis.Y;
        Vector3 back = (camDown - up * camDown.Dot(up)).Normalized();    // 본체 +Z(화면 아래쪽)
        Vector3 right = up.Cross(back).Normalized();
        var basis = new Basis(right, up, back);
        // 손으로 든 물건이 수평으로 딱 맞을 리 없다 — 기울기는 여기서 의도한 만큼만 준다.
        if (!Mathf.IsZeroApprox(HoldRollDeg))
            basis = new Basis(up, Mathf.DegToRad(-HoldRollDeg)) * basis;
        if (!Mathf.IsZeroApprox(HoldExtraTiltDeg))
            basis = new Basis(right, Mathf.DegToRad(HoldExtraTiltDeg)) * basis;
        return new Transform3D(basis, origin);
    }

    // 자리에 똑바로 앉은 카메라. 에디터에서는 리그 스크립트가 돌지 않으므로 리그의 카메라를 그대로 쓴다
    // (실행 중 SeatedCameraGlobal 도 리그 자리 × 카메라 처음 자세라 같은 값이다).
    private Transform3D SeatCamera()
    {
        if (_rig != null) return _rig.SeatedCameraGlobal();
        var cam = GetNodeOrNull<Node3D>(RigPath)?.GetNodeOrNull<Node3D>("Camera3D");
        if (cam != null) return cam.GlobalTransform;
        return GetViewport()?.GetCamera3D()?.GlobalTransform ?? GlobalTransform;
    }

    // 그립 마커의 월드 트랜스폼 — 거치대에 놓였을 때 / 들고 있을 때.
    private Transform3D CradleGrip() => RestTransform() * GripLocalTransform();
    private Transform3D HoldGrip() => _hold * GripLocalTransform();

    // ── 꺼내기 / 내려놓기 ──────────────────────────────────────────────────

    // 지금 꺼낼 수 있는가. 근무 중 · 휴게시간에만, 전원이 있을 때, 통화 중이 아닐 때.
    public bool CanOpen()
    {
        if (_state != PadState.Stowed) return false;
        if (!InService) return false;
        if (!_powered) return false;
        var ctl = ControlRoom3DController.Instance;
        if (ctl != null && (ctl.IsInputLocked || ctl.ModalSurface != null)) return false;
        // 벨이 울리는 중에는 꺼낼 수 있다(급한 상황에도 패드는 열린다). 수화기를 든 뒤에는 안 된다.
        if (Phone3D.Instance is { IsBusy: true, IsRinging: false }) return false;
        if (PauseMenu.Instance?.IsOpen == true) return false;
        return true;
    }

    public void Toggle()
    {
        if (_state == PadState.Stowed) Open();
        else Close();
    }

    // app — 거치대 위 화면에서 누른 앱 아이콘. 그 앱을 연 채로 집어 든다(없으면 홈).
    public void Open(PadView.Tab? app = null)
    {
        if (!CanOpen())
        {
            // 전원이 꺼진 패드를 누르면 둔한 딸깍 소리만 난다.
            if (!_powered && GameState.Instance?.CurrentPhase == GamePhase.Live)
                Sfx.Instance?.Play("switch_fail", -12f);
            return;
        }
        // 기록 창(오늘의 업무 · 시설 로그 · 대화 기록)이 떠 있으면 닫고 꺼낸다 —
        // 근무가 시작되면 「오늘의 업무」 창이 저절로 뜨므로, 막으면 Tab 이 먹지 않는 것처럼 보인다.
        if (Day1HistoryOverlay.Instance?.IsWindowOpen == true) Day1HistoryOverlay.Instance.CloseWindow();
        var ctl = ControlRoom3DController.Instance;
        ctl?.UnzoomIfFocused();
        _state = PadState.Reaching;
        _hold = HoldTransform();

        SetScreenAwake(true);
        _view.OnOpened();
        if (app is { } a && a != PadView.Tab.Home) _view.OpenApp(a);
        ctl?.SetModalSurface(this);
        _rig?.PadPosture(true, LiftSeconds + 0.14f);
        Sfx.Instance?.Play("relay_click", -12f, 0.9f);

        if (_player?.HandSocketL != null)
            _player.PlayPadPickup(CradleGrip, HoldGrip, LiftSeconds, OnGripped, () => SetState(PadState.Held));
        else
        {
            // 손이 없는 장면(F6 · 검사)에서는 패드만 들어 올린다.
            _state = PadState.Lifting;
            MoveBody(_body.GlobalTransform, _hold, LiftSeconds, () => SetState(PadState.Held));
        }
    }

    // 손가락이 패드를 다 감은 순간 — 이제 패드가 손과 함께 올라온다.
    private void OnGripped()
    {
        if (_state != PadState.Reaching) return;
        _state = PadState.Lifting;
        _attached = true;
        Sfx.Instance?.Play("crt_on", -16f, 1.4f);
    }

    private void SetState(PadState s)
    {
        if (_state is PadState.Lifting or PadState.Reaching) _state = s;
        // 다 들어 올린 순간부터 왼팔을 감춘다(HideLeftArmWhileHolding).
        if (_state == PadState.Held && HideLeftArmWhileHolding) _player?.SetLeftArmMeshHidden(true);
    }

    public void Close()
    {
        if (_state is PadState.Stowed or PadState.Lowering) return;
        bool wasReaching = _state == PadState.Reaching;
        _state = PadState.Lowering;
        _player?.SetLeftArmMeshHidden(false);   // 내려놓는 동작은 손이 보이는 채로

        ControlRoom3DController.Instance?.SetModalSurface(null);
        _rig?.PadPosture(false, LiftSeconds + 0.06f);
        Sfx.Instance?.Play("relay_click", -14f, 0.75f);

        if (_player?.HandSocketL != null)
        {
            // 아직 쥐기 전이었다면 패드는 그대로 거치대에 있다 — 손만 거둔다.
            if (wasReaching && !_attached) { _player.PlayPadPutDown(CradleGrip, 0.12f, Placed); return; }
            _player.PlayPadPutDown(CradleGrip, LiftSeconds, Placed);
        }
        else MoveBody(_body.GlobalTransform, RestTransform(), LiftSeconds, Placed);
    }

    // 패드가 거치대에 닿았다 — 손에서 떼어 제자리에 딱 맞춘다.
    private void Placed()
    {
        _attached = false;
        MoveBody(_body.GlobalTransform, RestTransform(), 0.08f, () =>
        {
            _state = PadState.Stowed;
            _view.OnClosed(locked: !_inService);
            SetScreenAwake(false);
        });
    }

    private void MoveBody(Transform3D from, Transform3D to, float seconds, System.Action done)
    {
        _moveTween?.Kill();
        _moveTween = CreateTween();
        _moveTween.TweenMethod(Callable.From<float>(t =>
        {
            var x = from.InterpolateWith(to, t);
            x.Origin += Vector3.Up * Mathf.Sin(t * Mathf.Pi) * 0.02f;
            _body.GlobalTransform = x;
        }), 0f, 1f, Mathf.Max(0.02f, seconds)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _moveTween.TweenCallback(Callable.From(done));
    }

    // 화면을 그리고 밝힐 것인가. 들고 있는 동안은 늘. 거치 중에는 근무 · 휴게시간이면 홈을 켜 두고,
    // 그 밖에는 잠금 화면을 한 장만 그려 어둡게 둔다(사고 예고가 뜨면 점멸하도록 계속 그린다).
    private void SetScreenAwake(bool awake)
    {
        bool draw = awake || StowedLive || Time.GetTicksMsec() / 1000.0 < _awakeUntil;
        _vp.RenderTargetUpdateMode = draw ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;
        SetBrightness(awake ? (_powered ? 1f : 0f) : StowedLevel(), 0.15f);
    }

    // 거치대 위에서 홈 화면이 켜져 있는가.
    private bool StowedLive => _inService && _powered;

    // 거치 중 화면 밝기 — 꺼짐 0 · 근무/휴게시간(홈) StowedBrightness · 잠금 화면 0.22(예고가 뜨면 0.6).
    private float StowedLevel() => !_powered ? 0f : _inService ? StowedBrightness : _stowedAlert ? 0.6f : 0.22f;

    private void SetBrightness(float to, float seconds)
    {
        if (_screenMat == null) return;
        _brightTween?.Kill();
        float from = _screenMat.GetShaderParameter("brightness").AsSingle();
        _brightTween = CreateTween();
        _brightTween.TweenMethod(Callable.From<float>(v => _screenMat.SetShaderParameter("brightness", v)),
            from, to, Mathf.Max(0.01f, seconds));
    }

    // ── 전원 ─────────────────────────────────────────────────────────────

    // 패드 채널이 꺼졌다(직접 내렸거나 정전으로 용량이 깎였다) — 글리치 0.2초 → 검은 화면.
    private void PowerCut()
    {
        _view.PowerOff(glitch: true);
        Sfx.Instance?.Play("power_down", -14f, 1.3f);
        _awakeUntil = Time.GetTicksMsec() / 1000.0 + 0.4;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        if (IsOpen) Close();
        var t = CreateTween();
        t.TweenInterval(0.22);
        t.TweenCallback(Callable.From(() => SetBrightness(0f, 0.05f)));
    }

    // 다시 켜졌다 — 검은 화면 → 로고 + 부팅음(0.8초) → 첫 화면.
    private void PowerRestore()
    {
        _view.PowerOn();
        Sfx.Instance?.Play("crt_on", -10f);
        _awakeUntil = Time.GetTicksMsec() / 1000.0 + PadView.BootSeconds + 0.3;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        SetBrightness(IsOpen ? 1f : StowedLevel(), 0.2f);
    }

    // ── 입력 ─────────────────────────────────────────────────────────────

    private void OnAreaInput(Node camera, InputEvent e, Vector3 pos, Vector3 normal, long shapeIdx)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        if (_state != PadState.Stowed) return;
        // 켜진 홈 화면의 앱 아이콘을 눌렀으면 그 앱을 연 채로 들어 올린다.
        PadView.Tab? app = null;
        if (StowedLive && camera is Node3D cam
            && ProjectToScreen(cam.GlobalPosition, (pos - cam.GlobalPosition).Normalized(), out Vector2 uv, out bool inside)
            && inside)
            app = _view.AppAt(uv * (Vector2)CanvasSize);
        Open(app);
        GetViewport()?.SetInputAsHandled();
    }

    public override void _Input(InputEvent e)
    {
        if (Engine.IsEditorHint()) return;
        // 들고 있는 동안 우클릭 = 한 단계 뒤로(상세 → 목록 → 홈). 홈에서 한 번 더 누르면 내려놓기.
        // Tab 은 어느 화면에서든 곧바로 내려놓는다(ClueHud). (ControlRoom3DController 보다 먼저 받는다.)
        // 기록 창(로그 · 대화 기록 · 업무)이 패드 위에 떠 있으면 우클릭은 그 창을 닫는 데 쓴다(Day1HistoryOverlay).
        if (_state is PadState.Held or PadState.Lifting
            && Day1HistoryOverlay.Instance?.IsWindowOpen != true
            && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            if (_state != PadState.Held || _view?.TryGoBack() != true) Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) { TickEditorPreview(); return; }

        // 근무 · 휴게시간이 아니게 되면(엔딩 · 타이틀 등) 내려놓는다.
        bool inService = InService;
        if (_state != PadState.Stowed && !inService) Close();
        // 거치대 위 화면 — 근무 · 휴게시간이 시작되면 홈을 켜고, 끝나면 잠금 화면으로.
        if (inService != _inService)
        {
            _inService = inService;
            if (_state == PadState.Stowed)
            {
                _view.SetStowed(true, locked: !inService);
                SetScreenAwake(false);
            }
        }

        // 전원 — 패드 채널 / 정전.
        bool powered = PadPowered;
        if (powered != _powered)
        {
            _powered = powered;
            if (powered) PowerRestore();
            else PowerCut();
        }
        // 거치 중 — 근무 · 휴게시간에는 홈을 계속 그린다(시각 · 배지 · 예고 배너 점멸).
        // 그 밖(잠금 화면)에는 사고 예고가 뜬 동안만 계속 그리고, 아니면 한 장만 그려 두고 멈춘다.
        if (!IsOpen)
        {
            bool alert = _powered && _view.HasAlert;
            if (alert != _stowedAlert)
            {
                _stowedAlert = alert;
                if (_state == PadState.Stowed) SetBrightness(StowedLevel(), 0.2f);
            }
            bool draw = StowedLive || alert || Time.GetTicksMsec() / 1000.0 < _awakeUntil;
            var want = draw ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;
            if (_vp.RenderTargetUpdateMode != want && (draw || _vp.RenderTargetUpdateMode == SubViewport.UpdateMode.Always))
                _vp.RenderTargetUpdateMode = want;
        }

        // 쥔 순간부터 내려놓을 때까지 패드는 왼손 소켓을 따라간다(Phone3D 수화기와 같은 lerp).
        if (_attached && _player?.HandSocketL != null)
        {
            var sx = _player.HandSocketL.GlobalTransform;
            var socket = new Transform3D(sx.Basis.Orthonormalized(), sx.Origin);
            var target = socket * GripLocalTransform().AffineInverse();
            _body.GlobalTransform = _body.GlobalTransform.InterpolateWith(target,
                Mathf.Clamp((float)delta * HandFollowSharpness, 0f, 1f));
        }

        // 표시등 — 새 단서(패드 모서리) · 전원(거치대).
        _ledPhase += (float)delta * 5f;
        if (_ledMat != null)
        {
            bool unseen = _powered && ClueBoard.UnseenCount > 0 && _state == PadState.Stowed;
            _ledMat.EmissionEnergyMultiplier = unseen ? 1.2f + 1.8f * (0.5f + 0.5f * Mathf.Sin(_ledPhase)) : 0f;
        }
        if (_cradleLed != null) _cradleLed.EmissionEnergyMultiplier = _powered ? 1.4f : 0f;
    }

    public bool TryProjectRay(Vector3 rayOrigin, Vector3 rayDir, bool clamp, out Vector2 canvasPos)
    {
        canvasPos = Vector2.Zero;
        if (_state != PadState.Held) return false;
        if (!ProjectToScreen(rayOrigin, rayDir, out Vector2 uv, out bool inside)) return false;
        if (!inside && !clamp) return false;
        canvasPos = new Vector2(Mathf.Clamp(uv.X, 0f, 1f) * _vp.Size.X, Mathf.Clamp(uv.Y, 0f, 1f) * _vp.Size.Y);
        return true;
    }

    // 광선이 화면 평면에 닿는 자리(0~1, 왼쪽 위 원점). 평면과 나란하거나 뒤쪽이면 false.
    private bool ProjectToScreen(Vector3 rayOrigin, Vector3 rayDir, out Vector2 uv, out bool inside)
    {
        uv = Vector2.Zero;
        inside = false;
        if (_screen == null) return false;
        Transform3D inv = _screen.GlobalTransform.AffineInverse();
        Vector3 lo = inv * rayOrigin;
        Vector3 ld = inv.Basis * rayDir;
        if (Mathf.Abs(ld.Z) < 1e-6f) return false;
        float t = -lo.Z / ld.Z;
        if (t < 0f) return false;
        Vector3 hit = lo + ld * t;
        uv = new Vector2(hit.X + 0.5f, 0.5f - hit.Y);
        inside = uv.X is >= 0f and <= 1f && uv.Y is >= 0f and <= 1f;
        return true;
    }

    // 화면의 한 점(뷰포트 픽셀) → 월드 좌표.
    public Vector3 ScreenPointWorld(Vector2 canvasPos)
    {
        float u = canvasPos.X / Mathf.Max(1f, _vp.Size.X);
        float v = canvasPos.Y / Mathf.Max(1f, _vp.Size.Y);
        return _screen.GlobalTransform * new Vector3(u - 0.5f, 0.5f - v, 0f);
    }

    // 화면을 눌렀다 — 반응은 화면(UI) 안에서만 한다.
    // 패드 트랜스폼 · 왼팔 파지 포즈 · 카메라는 클릭에 절대 반응하지 않는다.
    // (예전에는 누른 자리에 따라 왼손 엄지 / 오른팔 전체를 움직였다 — 클릭마다 화면이 휘저어져 어지러웠다.)
    public void OnSurfacePressed(Vector2 canvasPos)
    {
        if (_state != PadState.Held) return;
        Sfx.Instance?.Play("key_single", -20f, 1.3f);
        _view?.FlashTouch(canvasPos / ViewScale);
    }

    // 뷰포트 픽셀 ↔ PadView 논리 좌표(1120×700) 배율.
    private float ViewScale => _vp == null ? 1f : _vp.Size.X / Mathf.Max(1f, (float)CanvasSize.X);
}
