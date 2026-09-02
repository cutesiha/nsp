using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// MainScene3D_Test 루트. 기존 2D MainScene 을 통째로 넣지 않는다. 대신 전용 View 두 개를
// 각자 SubViewport 에 만들어 두 CRT 에 표시하고, 게임 시뮬레이션(FacilitySimulation 등)을
// 여기서 tick 한다. 마우스는 레이캐스트로 해당 CRT 의 View 로 전달한다.
public partial class ControlRoom3DController : Node3D
{
    [Export] public NodePath CameraPath = "PlayerSeatRig/Camera3D";
    [Export] public NodePath RigPath = "PlayerSeatRig";
    // CRT 화면 UI를 짜는 논리 캔버스 크기(레이아웃 좌표계). 실제 렌더 해상도는 여기에 UiScale 을 곱한다.
    [Export] public Vector2I MonitorCanvasSize = new(800, 600);
    [Export] public float FocusDistance = 0.62f;

    // 1920x1080 화면에 맞춰 CRT/배치표/단말기의 모든 텍스트·UI를 같은 배율로 키운다.
    // 레이아웃 코드는 손대지 않고, View 를 이 배율로 스케일한 프레임 안에 넣어 통째로 확대한다.
    public const float UiScale = 1.3f;

    public static void AddScaledView(SubViewport vp, Control view, Vector2I logicalSize)
    {
        var frame = new Control { Size = logicalSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.Scale = new Vector2(UiScale, UiScale);
        vp.AddChild(frame);
        frame.AddChild(view);
    }

    [Export] public string[] AutoStaffRooms =
        { "core_room", "power_room", "vent_room", "maintenance_room", "guard_room", "medical_room" };
    public static readonly string[] DailyTabooIds = { "taboo_power_headcount_limit" };

    private Camera3D _camera;
    private SeatedCameraRig _rig;
    private readonly List<MonitorScreen3D> _screens = new();
    private SubViewport _facilityVp, _cctvVp, _reportVp, _restRosterVp, _interviewVp;
    // CCTV CRT 뒤에서 실제 3D 작업실을 렌더하는 격리된 월드. CCTVMonitorView 가 이 텍스처를
    // 배경으로 깔고 그 위에 노이즈/REC/신호상태 오버레이를 그린다.
    private SubViewport _facilityCctvVp;
    private float _brightness = 1f, _distortion = 0f, _noise = 0.035f;

    private MonitorScreen3D _dragScreen;
    private MonitorScreen3D _focusedScreen;
    private Vector2 _lastCanvasPos;

    // Title/Schedule 단계에서 ShiftFlowController 가 제어실 CRT 입력을 잠그거나(_inputLocked),
    // 책상 위 배치표 같은 다른 표면으로 입력을 돌린다(_modal).
    private IProjectionSurface _modal;
    private bool _inputLocked;

    public IReadOnlyList<MonitorScreen3D> Screens => _screens;
    public static ControlRoom3DController Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        _camera = GetNodeOrNull<Camera3D>(CameraPath);
        _rig = GetNodeOrNull<SeatedCameraRig>(RigPath);

        GetViewport().PhysicsObjectPicking = true;
        AmbientOverlay.Instance?.SetSceneIntensity(0.15f);
        CollectScreens(this);
        BuildViewports();

        CallDeferred(nameof(AfterReady));
    }

    public override void _ExitTree()
    {
        AmbientOverlay.Instance?.SetSceneIntensity(1f);
        if (Instance == this) Instance = null;
    }

    private void CollectScreens(Node n)
    {
        if (n is MonitorScreen3D s) _screens.Add(s);
        foreach (var c in n.GetChildren()) CollectScreens(c);
    }

    // 시작 화면 → 근무 배치 → 근무 로 이어지는 흐름은 ShiftFlowController 가 소유한다.
    // 배치 확정 시점에 이 메서드가 호출되어 시뮬레이션을 실제로 굴리기 시작한다.
    public void BeginShift()
    {
        TabooRuleSystem.Instance?.ActivateDailyTaboos(DailyTabooIds);
        GameState.Instance?.SetPhase(GamePhase.Live);
        FacilitySimulation.Instance?.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        if (string.IsNullOrEmpty(GameState.Instance?.SaboteurEmployeeId))
            GameState.Instance?.AssignRandomSaboteur(FacilitySimulation.Instance?.GetEmployeeIds() ?? System.Array.Empty<string>());

        AutoStaff();
        SetScreenBrightness(1f);
        SetScreenNoise(0.035f);
    }

    // ShiftFlowController 훅.
    public void SetModalSurface(IProjectionSurface surface)
    {
        _modal = surface;
        _focusedScreen = null;
        _dragScreen = null;
    }

    public void SetInputLocked(bool locked) => _inputLocked = locked;

    public SubViewport FacilityViewport => _facilityVp;
    public SubViewport CctvViewport => _cctvVp;
    public SubViewport ReportViewport => _reportVp;
    public SubViewport RestRosterViewport => _restRosterVp;
    public SubViewport InterviewViewport => _interviewVp;
    public SubViewport FacilityCctvViewport => _facilityCctvVp;

    private void BuildViewports()
    {
        // CCTV 3D 월드 — 자체 World3D 로 격리해서 중앙제어실 3D 와 섞이지 않게 한다.
        _facilityCctvVp = new SubViewport
        {
            Size = new Vector2I(640, 480),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
            Disable3D = false,
            GuiDisableInput = true,
            TransparentBg = false,
        };
        AddChild(_facilityCctvVp);
        var cctvWorld = GD.Load<PackedScene>("res://scenes/facility/facility_cctv_world.tscn");
        if (cctvWorld != null) _facilityCctvVp.AddChild(cctvWorld.Instantiate());

        _facilityVp = MakeViewport();
        AddScaledView(_facilityVp, new FacilityMonitorView(), MonitorCanvasSize);

        _cctvVp = MakeViewport();
        AddScaledView(_cctvVp, new CCTVMonitorView(), MonitorCanvasSize);

        _reportVp = MakeViewport();
        AddScaledView(_reportVp, new ShiftReportView(), MonitorCanvasSize);

        _restRosterVp = MakeViewport();
        AddScaledView(_restRosterVp, new RestRosterView(), MonitorCanvasSize);

        _interviewVp = MakeViewport();
        AddScaledView(_interviewVp, new InterviewCCTVView(), MonitorCanvasSize);
    }

    // ShiftFlowController 가 단계 전환마다 CRT 에 붙는 프로그램을 바꿔 끼운다
    // (왼쪽=시설/배치기록/보고서, 오른쪽=CCTV/인터뷰) — 씬 전환 없이 화면만 바뀐다.
    public void SetLeftScreen(SubViewport vp) => ConfigureNamed("01", vp);
    public void SetRightScreen(SubViewport vp) => ConfigureNamed("02", vp);

    private void ConfigureNamed(string token, SubViewport vp)
    {
        if (vp == null) return;
        foreach (var s in _screens)
            if (s.Name.ToString().Contains(token))
                s.Configure(vp);
        ApplyScreenParams();
    }

    private void ApplyScreenParams()
    {
        foreach (var s in _screens)
        {
            s.ScreenMaterial?.SetShaderParameter("brightness", _brightness);
            s.ScreenMaterial?.SetShaderParameter("h_distortion", _distortion);
            s.ScreenMaterial?.SetShaderParameter("noise_strength", _noise);
        }
    }

    private SubViewport MakeViewport()
    {
        var vp = new SubViewport
        {
            Size = new Vector2I(
                Mathf.RoundToInt(MonitorCanvasSize.X * UiScale),
                Mathf.RoundToInt(MonitorCanvasSize.Y * UiScale)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            HandleInputLocally = true,
            GuiDisableInput = false,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(vp);
        return vp;
    }

    private void AfterReady()
    {
        foreach (var s in _screens)
        {
            bool isFacility = s.Name.ToString().Contains("01");
            s.Configure(isFacility ? _facilityVp : _cctvVp);
        }

        // 근무 부팅 전까지 CRT 는 꺼진 상태로 둔다(시작 화면 / 배치 단계).
        SetScreenBrightness(0.02f);
    }

    private void AutoStaff()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        var employees = sim.GetEmployeeIds().ToList();

        // 스케줄 화면을 거쳐 들어온 경우 플레이어 배치를 존중한다.
        // 아무도 배치돼 있지 않을 때(F6 단독 실행)만 자동 배치한다.
        if (employees.Any(id => !string.IsNullOrEmpty(sim.GetEmployeeState(id)?.AssignedRoomId)))
            return;

        for (int i = 0; i < employees.Count && i < AutoStaffRooms.Length; i++)
            sim.AssignToRoom(employees[i], AutoStaffRooms[i]);
    }

    public override void _Process(double delta)
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;

        GameState.Instance.AdvanceDayTime((float)delta);
        FacilitySimulation.Instance?.Tick(delta);

    }

    // --- 입력 : CRT 레이캐스트 → 해당 View 로 전달 + 클릭 시 카메라 확대 ------

    public override void _Input(InputEvent @event)
    {
        if (_camera == null) return;

        if (_modal != null)
        {
            switch (@event)
            {
                case InputEventMouseButton mb: ForwardModal(mb); break;
                case InputEventMouseMotion mm: ForwardModal(mm); break;
            }
            return;
        }

        if (_inputLocked) return;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Unfocus();
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mb: HandleMouseButton(mb); break;
            case InputEventMouseMotion mm: HandleMouseMotion(mm); break;
        }
    }

    private void ForwardModal(InputEventMouseButton mb)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mb.Position);
        Vector3 dir = _camera.ProjectRayNormal(mb.Position);
        bool hit = _modal.TryProjectRay(origin, dir, clamp: !mb.Pressed, out Vector2 cp);
        if (!hit && mb.Pressed) return;
        _lastCanvasPos = cp;
        _modal.TargetViewport?.PushInput(MakeButton(mb, cp), inLocalCoords: true);
        GetViewport().SetInputAsHandled();
    }

    private void ForwardModal(InputEventMouseMotion mm)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mm.Position);
        Vector3 dir = _camera.ProjectRayNormal(mm.Position);
        if (!_modal.TryProjectRay(origin, dir, clamp: true, out Vector2 cp)) return;
        var ev = new InputEventMouseMotion
        {
            Position = cp, GlobalPosition = cp,
            Relative = cp - _lastCanvasPos, ButtonMask = mm.ButtonMask,
        };
        _lastCanvasPos = cp;
        _modal.TargetViewport?.PushInput(ev, inLocalCoords: true);
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mb.Position);
        Vector3 dir = _camera.ProjectRayNormal(mb.Position);

        if (mb.Pressed && mb.ButtonIndex == MouseButton.Right && _focusedScreen != null)
        {
            Unfocus();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mb.Pressed)
        {
            var hit = PickScreen(origin, dir, out Vector2 canvasPos);
            if (hit == null) return;

            if (_focusedScreen != hit)
                Focus(hit);

            _dragScreen = hit;
            _lastCanvasPos = canvasPos;
            Forward(hit, MakeButton(mb, canvasPos));
            GetViewport().SetInputAsHandled();
        }
        else
        {
            var target = _dragScreen ?? PickScreen(origin, dir, out _);
            if (target != null)
            {
                target.TryProjectRay(origin, dir, clamp: _dragScreen != null, out _lastCanvasPos);
                Forward(target, MakeButton(mb, _lastCanvasPos));
                GetViewport().SetInputAsHandled();
            }
            _dragScreen = null;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        Vector3 origin = _camera.ProjectRayOrigin(mm.Position);
        Vector3 dir = _camera.ProjectRayNormal(mm.Position);

        var target = _dragScreen;
        Vector2 canvasPos;
        if (target != null) target.TryProjectRay(origin, dir, clamp: true, out canvasPos);
        else target = PickScreen(origin, dir, out canvasPos);
        if (target == null) return;

        var ev = new InputEventMouseMotion
        {
            Position = canvasPos, GlobalPosition = canvasPos,
            Relative = canvasPos - _lastCanvasPos, Velocity = mm.Velocity, ButtonMask = mm.ButtonMask,
        };
        _lastCanvasPos = canvasPos;
        Forward(target, ev);
        if (_dragScreen != null) GetViewport().SetInputAsHandled();
    }

    private static InputEventMouseButton MakeButton(InputEventMouseButton src, Vector2 canvasPos) => new()
    {
        ButtonIndex = src.ButtonIndex, Pressed = src.Pressed, DoubleClick = src.DoubleClick,
        Position = canvasPos, GlobalPosition = canvasPos, ButtonMask = src.ButtonMask,
    };

    private void Forward(MonitorScreen3D screen, InputEvent ev)
    {
        screen.TargetViewport?.PushInput(ev, inLocalCoords: true);
    }

    private MonitorScreen3D PickScreen(Vector3 origin, Vector3 dir, out Vector2 canvasPos)
    {
        canvasPos = Vector2.Zero;
        float best = float.MaxValue;
        MonitorScreen3D bestScreen = null;
        foreach (var s in _screens)
        {
            if (!s.TryProjectRay(origin, dir, clamp: false, out Vector2 cp)) continue;
            float d = origin.DistanceSquaredTo(s.GlobalPosition);
            if (d < best) { best = d; bestScreen = s; canvasPos = cp; }
        }
        return bestScreen;
    }

    private void Focus(MonitorScreen3D screen)
    {
        _focusedScreen = screen;
        Vector3 center = screen.GlobalPosition;
        Vector3 normal = screen.GlobalTransform.Basis.Z.Normalized();
        _rig?.FocusOnScreen(center, normal, FocusDistance);
    }

    private void Unfocus()
    {
        if (_focusedScreen == null) return;
        _focusedScreen = null;
        _rig?.ReturnToSeat();
    }

    // --- PHASE 6 훅 ---------------------------------------------------

    public void SetScreenBrightness(float v) { _brightness = v; foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("brightness", v); }
    public void SetScreenDistortion(float v) { _distortion = v; foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("h_distortion", v); }
    public void SetScreenNoise(float v) { _noise = v; foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("noise_strength", v); }
}
