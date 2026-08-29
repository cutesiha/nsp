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
    [Export] public Vector2I MonitorCanvasSize = new(800, 600);
    [Export] public float FocusDistance = 0.62f;

    [Export] public string[] AutoStaffRooms = { "core_room", "power_room", "vent_room", "maintenance_room" };
    private static readonly string[] DailyTabooIds = { "taboo_power_headcount_limit" };

    private Camera3D _camera;
    private SeatedCameraRig _rig;
    private readonly List<MonitorScreen3D> _screens = new();
    private SubViewport _facilityVp, _cctvVp;

    private MonitorScreen3D _dragScreen;
    private MonitorScreen3D _focusedScreen;
    private Vector2 _lastCanvasPos;

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
        BootShift();
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

    private void BootShift()
    {
        TabooRuleSystem.Instance?.ActivateDailyTaboos(DailyTabooIds);
        GameState.Instance?.SetPhase(GamePhase.Live);
        FacilitySimulation.Instance?.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        if (string.IsNullOrEmpty(GameState.Instance?.SaboteurEmployeeId))
            GameState.Instance?.AssignRandomSaboteur(FacilitySimulation.Instance?.GetEmployeeIds() ?? System.Array.Empty<string>());
    }

    private void BuildViewports()
    {
        _facilityVp = MakeViewport();
        _facilityVp.AddChild(new FacilityMonitorView());

        _cctvVp = MakeViewport();
        _cctvVp.AddChild(new CCTVMonitorView());
    }

    private SubViewport MakeViewport()
    {
        var vp = new SubViewport
        {
            Size = MonitorCanvasSize,
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

        // View 들이 방 좌표를 등록한 뒤에 자동 배치(F6 단독 테스트용).
        GetTree().CreateTimer(0.15).Timeout += AutoStaff;
    }

    private void AutoStaff()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        var employees = sim.GetEmployeeIds().ToList();
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

    public void SetScreenBrightness(float v) { foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("brightness", v); }
    public void SetScreenDistortion(float v) { foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("h_distortion", v); }
    public void SetScreenNoise(float v) { foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("noise_strength", v); }
}
