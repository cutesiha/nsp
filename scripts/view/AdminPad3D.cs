using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 책상 위 "관리자 패드" — 얇은 산업용 태블릿. 지침 · 단서 · 직원 세 앱이 들어 있다(PadView).
//
//   꺼내기    패드 클릭 / Tab → 왼손이 집어 화면 앞 아래로 들어 올리고, 시선이 내려간다.
//   조작      화면 클릭은 ControlRoom3DController 가 이 표면(모달)으로 전달한다.
//             누른 자리가 화면 왼쪽이면 왼손 엄지, 오른쪽이면 오른손 검지가 누르는 연출만 한다
//             (클릭 판정은 손과 무관하다 — 전화기와 같은 원칙).
//   내려놓기  우클릭 / Tab.
//
// 든 동안에는 근무 시간이 멈춘다(PausesGame). 통화 중에는 꺼낼 수 없고, 든 동안에는 전화기를
// 집을 수 없다 — 왼손 · 오른손이 서로의 물건을 동시에 쥐지 않게.
// 판정은 하지 않는다. 자료는 ClueBoard · InterviewEvidenceBoard 가 쥐고 있다.
public partial class AdminPad3D : Node3D, IProjectionSurface, ISurfacePressListener
{
    [Export] public NodePath PlayerPath = "../PlayerCharacter";
    [Export] public NodePath RigPath = "../../PlayerSeatRig";
    // 화면 UI 의 논리 캔버스(PadView 가 이 좌표로 짠다).
    [Export] public Vector2I CanvasSize = new(800, 500);
    // 본체 크기(m) — 가로 · 두께 · 세로(책상에 눕혀 둔 상태 기준).
    [Export] public Vector3 BodySize = new(0.23f, 0.011f, 0.15f);
    // 화면이 앞면에서 차지하는 크기(m). 나머지는 베젤.
    [Export] public Vector2 ScreenSize = new(0.206f, 0.127f);
    // 들고 있을 때의 자리 — 자리에 앉은 카메라 기준(오른쪽 +X · 위 +Y · 앞 -Z).
    [Export] public Vector3 HoldOffset = new(-0.01f, -0.165f, -0.30f);
    // 왼손이 쥐는 자리 — 본체 로컬. 왼쪽 아래 모서리 안쪽의 뒷면 — 손바닥이 패드 뒤를 받치고
    // 엄지만 앞 테두리로 넘어온다(손이 화면 옆 허공을 쥔 것처럼 보이지 않게).
    [Export] public Vector3 GripLocal = new(-0.09f, -0.055f, 0.045f);
    [Export] public float LiftSeconds = 0.42f;

    public static AdminPad3D Instance { get; private set; }

    // 패드가 책상 위에 놓여 있지 않은 동안(집으러 가는 중 · 들어 올리는 중 · 들고 있음 · 내려놓는 중)
    // 근무 시간은 흐르지 않는다.
    public static bool PausesGame => Instance != null && Instance._state != PadState.Stowed;

    private enum PadState { Stowed, Reaching, Lifting, Held, Lowering }
    private PadState _state = PadState.Stowed;

    public bool IsOpen => _state != PadState.Stowed;
    public bool IsHeld => _state == PadState.Held;

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
    private Tween _moveTween;
    private Transform3D _hold;
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
            // 책상 위에 놓여 있는 동안에는 그리지 않는다(마지막 한 장이 대기 화면으로 남는다).
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            HandleInputLocally = true,
            GuiDisableInput = false,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(_vp);
        _view = new PadView { CanvasSize = CanvasSize };
        _view.CloseRequested += Close;
        ControlRoom3DController.AddScaledView(_vp, _view, CanvasSize,
            ControlRoom3DController.DocumentSupersample, ControlRoom3DController.DocumentMinRenderScale);

        BuildBody();
        _body.GlobalTransform = RestTransform();
        SetScreenAwake(false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── 모양 ─────────────────────────────────────────────────────────────

    private void BuildBody()
    {
        // 본체는 자기 부모(책상 위 자리)와 따로 움직인다 — 들어 올리면 카메라 앞으로 간다.
        _body = new Node3D { Name = "PadBody", TopLevel = true };
        AddChild(_body);

        var shell = new MeshInstance3D
        {
            Name = "Shell",
            Mesh = new BoxMesh { Size = BodySize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.115f, 0.125f), Metallic = 0.55f, Roughness = 0.45f,
            },
        };
        _body.AddChild(shell);

        // 화면 테두리 안쪽의 검은 유리 — 화면이 꺼져 있을 때도 "화면"으로 보이게.
        var glass = new MeshInstance3D
        {
            Name = "Glass",
            Mesh = new QuadMesh { Size = ScreenSize + new Vector2(0.008f, 0.008f) },
            Position = new Vector3(0f, BodySize.Y * 0.5f + 0.0004f, 0.004f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.01f, 0.015f, 0.02f), Roughness = 0.15f, Metallic = 0.2f,
            },
        };
        _body.AddChild(glass);

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
            Position = new Vector3(0f, BodySize.Y * 0.5f + 0.0008f, 0.004f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            MaterialOverride = _screenMat,
        };
        _body.AddChild(_screen);

        // 새 단서가 있으면 깜빡이는 작은 호박색 표시등 — 책상 위에 놓인 패드의 뱃지.
        _ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.15f, 0.05f), EmissionEnabled = true,
            Emission = new Color(1f, 0.72f, 0.25f), EmissionEnergyMultiplier = 0f,
        };
        _body.AddChild(new MeshInstance3D
        {
            Name = "BadgeLed",
            Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.002f, 0.004f) },
            Position = new Vector3(BodySize.X * 0.5f - 0.02f, BodySize.Y * 0.5f + 0.0006f, -BodySize.Z * 0.5f + 0.006f),
            MaterialOverride = _ledMat,
        });

        _grip = new Marker3D { Name = "GripPoint", Position = GripLocal };
        _body.AddChild(_grip);

        // 책상 위 패드를 누르면 꺼낸다.
        var area = new Area3D { Name = "ClickArea", InputRayPickable = true };
        area.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = BodySize + new Vector3(0.02f, 0.03f, 0.02f) },
        });
        area.InputEvent += OnAreaInput;
        _body.AddChild(area);
    }

    // 책상 위에 놓인 자리. 이 노드(씬에서 위치를 잡는 자리)가 곧 받침이다.
    private Transform3D RestTransform() =>
        GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0f, BodySize.Y * 0.5f, 0f));

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
        return new Transform3D(new Basis(right, up, back), origin);
    }

    // ── 꺼내기 / 내려놓기 ──────────────────────────────────────────────────

    // 지금 꺼낼 수 있는가. 근무 중 · 휴게시간에만, 통화 중이 아닐 때, 다른 창이 열려 있지 않을 때.
    public bool CanOpen()
    {
        if (_state != PadState.Stowed) return false;
        if (GameState.Instance?.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return false;
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
        if (!CanOpen()) return;
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

        if (_player != null) _player.PlayPadPickup(() => GripWorld, StartLift);
        else StartLift();   // 손이 없는 장면(F6 등)에서는 바로 들어 올린다
    }

    // 손가락이 패드를 다 감은 순간 — 이제 패드가 손과 함께 올라온다.
    private void StartLift()
    {
        if (_state != PadState.Reaching) return;
        _state = PadState.Lifting;
        Sfx.Instance?.Play("crt_on", -16f, 1.4f);
        MoveBody(_body.GlobalTransform, _hold, LiftSeconds, () => _state = PadState.Held);
    }

    public void Close()
    {
        if (_state is PadState.Stowed or PadState.Lowering) return;
        _state = PadState.Lowering;

        ControlRoom3DController.Instance?.SetModalSurface(null);
        _rig?.PadPosture(false, LiftSeconds + 0.1f);
        _player?.PlayPadPutDown(() => GripWorld, LiftSeconds, null);
        Sfx.Instance?.Play("relay_click", -14f, 0.75f);

        MoveBody(_body.GlobalTransform, RestTransform(), LiftSeconds, () =>
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
            // 올라오는 동안 살짝 몸 쪽으로 호를 그린다(직선으로 떠오르지 않게).
            var x = from.InterpolateWith(to, t);
            x.Origin += Vector3.Up * Mathf.Sin(t * Mathf.Pi) * 0.025f;
            _body.GlobalTransform = x;
        }), 0f, 1f, Mathf.Max(0.05f, seconds)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _moveTween.TweenCallback(Callable.From(done));
    }

    // 화면이 켜져 있는가. 꺼지면 뷰포트를 멈추고 화면을 어둡게 둔다(대기 화면).
    private void SetScreenAwake(bool awake)
    {
        _vp.RenderTargetUpdateMode = awake ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Once;
        _screenMat?.SetShaderParameter("brightness", awake ? 1f : 0.22f);
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

        // 새 단서 표시등 — 놓여 있을 때만 깜빡인다.
        if (_ledMat != null)
        {
            _ledPhase += (float)delta * 5f;
            bool unseen = ClueBoard.UnseenCount > 0 && _state == PadState.Stowed;
            _ledMat.EmissionEnergyMultiplier = unseen ? 1.2f + 1.8f * (0.5f + 0.5f * Mathf.Sin(_ledPhase)) : 0f;
        }
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
            _player.PlayPadThumbTap(() => GripWorld);
            return;
        }
        Vector3 at = ScreenPointWorld(canvasPos);
        Vector3 normal = _screen.GlobalTransform.Basis.Z.Normalized();
        _player.PlayPadFingerTap(at, at + normal * 0.035f);
    }
}
