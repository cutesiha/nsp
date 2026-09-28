using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 책상 위 "관리자 패드" — 산업용 태블릿. 지침 · 단서 · 직원 앱이 들어 있다(PadView).
// 평소에는 책상 왼쪽 거치대에 비스듬히 기대어 있다(예전 경고 단말기 자리).
//
//   꺼내기    패드 클릭 / Tab → 왼손이 거치대에서 집어 화면 앞으로 들어 올리고, 시선이 내려간다.
//   조작      화면 클릭은 ControlRoom3DController 가 이 표면(모달)으로 전달한다.
//             누른 자리가 화면 왼쪽이면 왼손 엄지, 오른쪽이면 오른손 검지가 누르는 연출만 한다
//             (클릭 판정은 손과 무관하다 — 전화기와 같은 원칙).
//   내려놓기  우클릭 / Tab → 거치대로 돌아간다.
//
// 손과 패드: 왼손은 패드의 그립 마커(GripPoint — 위치 + 방향)를 따라간다. 패드는 손을 쥔 순간부터
// 왼손 소켓에 붙어 따라온다(Phone3D 수화기와 같은 lerp). 그래서 손가락과 패드의 관계는 마커 하나로
// 정해지고, 손이 패드를 뚫거나 반대로 쥐지 않는다.
//
// 전력: 전력 패널의 세 번째 채널(PowerConsumer.Sensor — 표시는 「패드」)이 이 기기의 전원이다.
// 꺼지면 화면이 글리치 뒤 꺼지고, 들고 있었다면 내려놓는다. 다시 켜지면 부팅 화면을 거친다.
//
// 든 동안에는 근무 시간이 멈춘다(PausesGame). 통화 중에는 꺼낼 수 없고, 든 동안에는 전화기를
// 집을 수 없다 — 왼손 · 오른손이 서로의 물건을 동시에 쥐지 않게.
// 판정은 하지 않는다. 자료는 ClueBoard · InterviewEvidenceBoard 가 쥐고 있다.
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
    // 화면은 눈을 향한다 — 눈보다 아래에 들고 있으므로 자연히 뒤로 기운다(약 25°).
    [Export] public Vector3 HoldOffset = new(0.035f, -0.12f, -0.26f);
    // 눈을 향한 기울기에서 더 뒤로(+) / 앞으로(-) 기울이는 각(도).
    [Export] public float HoldExtraTiltDeg = 0f;
    // 왼손 그립 마커 — 본체 로컬 위치 · 방향. 손 소켓이 이 마커에 정확히 겹친다.
    //   위치: 왼쪽 변 바로 바깥, 두께 가운데보다 살짝 뒤(손바닥이 변에 닿는다).
    //   방향: 손바닥이 패드 쪽(본체 +X)을 향하고, 손가락은 뒤 · 위로(뒷면을 감싸 가운데 쪽으로 굽는다),
    //         엄지 쪽은 앞 · 위 — 엄지가 앞 베젤 위에 놓인다. (AdminPadShot grips 로 후보를 비교해 골랐다)
    [Export] public Vector3 GripLocal = new(-0.168f, -0.012f, 0.03f);
    [Export] public Vector3 GripRotDeg = new(0f, -90f, -55f);
    // 거치대에 기댄 각도 — 책상 면에서 잰 각(도).
    [Export] public float CradleLeanDeg = 58f;
    [Export] public float LiftSeconds = 0.45f;
    // 손을 따라가는 부드러움(Phone3D 수화기와 같은 값).
    [Export] public float HandFollowSharpness = 22f;

    public static AdminPad3D Instance { get; private set; }

    // 패드가 거치대에 놓여 있지 않은 동안(집으러 가는 중 · 들어 올리는 중 · 들고 있음 · 내려놓는 중)
    // 근무 시간은 흐르지 않는다.
    public static bool PausesGame => Instance != null && Instance._state != PadState.Stowed;

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
    private float _ledPhase;

    public Vector3 GripWorld => _grip?.GlobalPosition ?? GlobalPosition;

    public override void _Ready()
    {
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
        _view.CloseRequested += Close;
        ControlRoom3DController.AddScaledView(_vp, _view, CanvasSize,
            ControlRoom3DController.DocumentSupersample, ControlRoom3DController.DocumentMinRenderScale);

        BuildCradle();
        BuildBody();
        _body.GlobalTransform = RestTransform();

        _powered = PadPowered;
        SetScreenAwake(false);
        if (!_powered) _view.PowerOff(glitch: false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
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

    private void BuildBody()
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

        _screenMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crt_screen.gdshader") };
        _screenMat.SetShaderParameter("screen_tex", _vp.GetTexture());
        _screenMat.SetShaderParameter("region_min", Vector2.Zero);
        _screenMat.SetShaderParameter("region_max", Vector2.One);
        _screenMat.SetShaderParameter("noise_strength", 0.012f);
        _screen = new MeshInstance3D
        {
            Name = "Screen",
            Mesh = new QuadMesh { Size = Vector2.One },
            // QuadMesh 는 +Z 를 본다 → X 로 -90° 눕혀 본체 윗면(+Y)을 보게 한다. 화면 위쪽 = 본체 -Z.
            Scale = new Vector3(ScreenSize.X, ScreenSize.Y, 1f),
            Position = new Vector3(0f, BodySize.Y * 0.5f + 0.0008f, screenZ),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            MaterialOverride = _screenMat,
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

    // 들고 있을 자리 — 자리에 똑바로 앉은 카메라 앞 아래. 화면은 눈을 향한다.
    private Transform3D HoldTransform()
    {
        Transform3D cam = _rig?.SeatedCameraGlobal()
                          ?? GetViewport()?.GetCamera3D()?.GlobalTransform
                          ?? GlobalTransform;
        Vector3 origin = cam * HoldOffset;
        Vector3 up = (cam.Origin - origin).Normalized();                 // 본체 +Y(화면) → 눈
        Vector3 right = (cam.Basis.X - up * cam.Basis.X.Dot(up)).Normalized();
        Vector3 back = right.Cross(up).Normalized();                     // 본체 +Z(화면 아래쪽)
        var basis = new Basis(right, up, back);
        if (!Mathf.IsZeroApprox(HoldExtraTiltDeg))
            basis = new Basis(right, Mathf.DegToRad(HoldExtraTiltDeg)) * basis;
        return new Transform3D(basis, origin);
    }

    // 그립 마커의 월드 트랜스폼 — 거치대에 놓였을 때 / 들고 있을 때.
    private Transform3D CradleGrip() => RestTransform() * GripLocalTransform();
    private Transform3D HoldGrip() => _hold * GripLocalTransform();

    // ── 꺼내기 / 내려놓기 ──────────────────────────────────────────────────

    // 지금 꺼낼 수 있는가. 근무 중 · 휴게시간에만, 전원이 있을 때, 통화 중이 아닐 때.
    public bool CanOpen()
    {
        if (_state != PadState.Stowed) return false;
        if (GameState.Instance?.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return false;
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

    public void Open()
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
        ctl?.SetModalSurface(this);
        _rig?.PadPosture(true, LiftSeconds + 0.25f);
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
    }

    public void Close()
    {
        if (_state is PadState.Stowed or PadState.Lowering) return;
        bool wasReaching = _state == PadState.Reaching;
        _state = PadState.Lowering;

        ControlRoom3DController.Instance?.SetModalSurface(null);
        _rig?.PadPosture(false, LiftSeconds + 0.1f);
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
            SetScreenAwake(false);
            _view.OnClosed();
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

    // 화면이 켜져 있는가(들고 있는 동안). 거치 중에는 뷰포트를 멈추고 어둡게 둔다(대기 화면).
    private void SetScreenAwake(bool awake)
    {
        bool draw = awake || Time.GetTicksMsec() / 1000.0 < _awakeUntil;
        _vp.RenderTargetUpdateMode = draw ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;
        SetBrightness(!_powered ? 0f : awake ? 1f : _stowedAlert ? 0.6f : 0.22f, 0.15f);
    }

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
        SetBrightness(IsOpen ? 1f : 0.22f, 0.2f);
    }

    // ── 입력 ─────────────────────────────────────────────────────────────

    private void OnAreaInput(Node camera, InputEvent e, Vector3 pos, Vector3 normal, long shapeIdx)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        if (_state != PadState.Stowed) return;
        Open();
        GetViewport()?.SetInputAsHandled();
    }

    public override void _Input(InputEvent e)
    {
        // 들고 있는 동안 우클릭 = 내려놓기. (ControlRoom3DController 보다 먼저 받는다.)
        if (_state is PadState.Held or PadState.Lifting
            && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // 근무 · 휴게시간이 아니게 되면(엔딩 · 타이틀 등) 내려놓는다.
        if (_state != PadState.Stowed && GameState.Instance?.CurrentPhase is not (GamePhase.Live or GamePhase.Rest))
            Close();

        // 전원 — 패드 채널 / 정전.
        bool powered = PadPowered;
        if (powered != _powered)
        {
            _powered = powered;
            if (powered) PowerRestore();
            else PowerCut();
        }
        // 거치 중 사고 예고 — 잠금 화면의 경고가 점멸하도록 화면을 계속 그리고 조금 밝힌다.
        // 예고가 없으면 한 장만 그려 두고 멈춘다(평소 거치 상태의 비용 0).
        if (!IsOpen)
        {
            bool alert = _powered && _view.HasAlert;
            if (alert != _stowedAlert)
            {
                _stowedAlert = alert;
                if (_state == PadState.Stowed) SetBrightness(alert ? 0.6f : 0.22f, 0.2f);
            }
            bool draw = alert || Time.GetTicksMsec() / 1000.0 < _awakeUntil;
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
        if (_screen == null || _state != PadState.Held) return false;

        Transform3D inv = _screen.GlobalTransform.AffineInverse();
        Vector3 lo = inv * rayOrigin;
        Vector3 ld = inv.Basis * rayDir;
        if (Mathf.Abs(ld.Z) < 1e-6f) return false;
        float t = -lo.Z / ld.Z;
        if (t < 0f) return false;

        Vector3 hit = lo + ld * t;
        float u = hit.X + 0.5f;
        float v = 0.5f - hit.Y;
        bool inside = u is >= 0f and <= 1f && v is >= 0f and <= 1f;
        if (!inside && !clamp) return false;

        canvasPos = new Vector2(Mathf.Clamp(u, 0f, 1f) * _vp.Size.X, Mathf.Clamp(v, 0f, 1f) * _vp.Size.Y);
        return inside || clamp;
    }

    // 들고 있는 손 모양 · 그립을 지금 Export 값으로 다시 맞춘다(인스펙터 · 캡처 튜닝용).
    public void RefreshHandPose()
    {
        if (_state == PadState.Held) _player?.PlayPadHold(HoldGrip);
    }

    // 화면의 한 점(뷰포트 픽셀) → 월드 좌표.
    public Vector3 ScreenPointWorld(Vector2 canvasPos)
    {
        float u = canvasPos.X / Mathf.Max(1f, _vp.Size.X);
        float v = canvasPos.Y / Mathf.Max(1f, _vp.Size.Y);
        return _screen.GlobalTransform * new Vector3(u - 0.5f, 0.5f - v, 0f);
    }

    // 화면을 눌렀다 — 왼쪽 절반은 들고 있는 왼손 엄지, 오른쪽 절반은 오른손 검지.
    public void OnSurfacePressed(Vector2 canvasPos)
    {
        if (_state != PadState.Held || _player == null) return;
        Sfx.Instance?.Play("key_single", -20f, 1.3f);
        bool left = canvasPos.X < _vp.Size.X * 0.5f;
        if (left)
        {
            _player.PlayPadThumbTap(HoldGrip);
            return;
        }
        Vector3 at = ScreenPointWorld(canvasPos);
        Vector3 normal = _screen.GlobalTransform.Basis.Z.Normalized();
        _player.PlayPadFingerTap(at, at + normal * 0.035f);
    }
}
