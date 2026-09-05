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
    // 책상 위 기기(센서 단말기 / 전력 스위치) 확대용 — 화면보다 더 가까이, 살짝 위에서.
    [Export] public NodePath SensorPath = "ControlRoom/AlertTerminal";
    [Export] public NodePath PowerPanelPath = "ControlRoom/PowerSwitchPanel";
    [Export] public float DeskPropFocusDistance = 0.46f;
    [Export] public Vector3 DeskPropFocusOffset = new(0f, 0.10f, 0f);

    // 1920x1080 화면에 맞춰 CRT/배치표/단말기의 모든 텍스트·UI를 같은 배율로 키운다.
    // 레이아웃 코드는 손대지 않고, View 를 이 배율로 스케일한 프레임 안에 넣어 통째로 확대한다.
    public const float UiScale = 1.3f;

    // 성능: SubViewport 의 '렌더 해상도만' 낮추는 배율(레이아웃/글자 크기는 그대로).
    // 뷰포트 크기와 스케일 프레임에 똑같이 곱하므로 화면에 보이는 결과는 동일하고
    // 텍셀 수만 줄어든다(입력 좌표 매핑도 vp.Size 기준이라 그대로 맞는다).
    private static float _renderScale = -1f;
    public static float RenderScale
    {
        get
        {
            if (_renderScale > 0f) return _renderScale;
            // [Tool] 스크립트가 에디터에서 이걸 읽을 때 설정을 로드하면 에디터 창 모드까지
            // 건드리게 된다 — 에디터에서는 항상 원래 해상도로 둔다.
            if (Engine.IsEditorHint()) return 1f;
            GameSettings.Load();
            _renderScale = GameSettings.GraphicsQuality switch
            {
                GameSettings.Quality.Low => 0.70f,
                GameSettings.Quality.Medium => 0.85f,
                _ => 1.0f,
            };
            return _renderScale;
        }
    }

    // 논리 캔버스 크기 → 실제 SubViewport 렌더 해상도.
    public static Vector2I ViewportSize(Vector2I logicalSize)
    {
        float k = UiScale * RenderScale;
        return new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(logicalSize.X * k)),
            Mathf.Max(1, Mathf.RoundToInt(logicalSize.Y * k)));
    }

    public static void AddScaledView(SubViewport vp, Control view, Vector2I logicalSize)
    {
        var frame = new Control { Size = logicalSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        float k = UiScale * RenderScale;
        frame.Scale = new Vector2(k, k);
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
    private MonitorScreen3D _focusedScreen;   // 확대 중인 대상이 모니터일 때만 채워진다
    private Node3D _focusedNode;              // 확대 중인 대상(모니터/센서/전력 기기)
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
        if ((GameState.Instance?.CurrentDay ?? 1) == 1)
            DialogueHistory.Instance?.ClearAll();
        if (string.IsNullOrEmpty(GameState.Instance?.SaboteurEmployeeId))
        {
            var sim = FacilitySimulation.Instance;
            GameState.Instance?.AssignRandomSaboteur(sim?.GetEmployeeIds() ?? System.Array.Empty<string>());
            string id = GameState.Instance?.SaboteurEmployeeId ?? "";
            if (!string.IsNullOrEmpty(id))
            {
                string name = sim?.GetEmployeeDef(id)?.Codename ?? id;
                GD.Print($"[방해자가 배정 되었습니다: {name}]");
            }
        }

        AutoStaff();
        SetScreenBrightness(1f);
        SetScreenNoise(0.020f);
    }

    // ShiftFlowController 훅.
    public void SetModalSurface(IProjectionSurface surface)
    {
        _modal = surface;
        _focusedScreen = null;
        _focusedNode = null;
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
        // 이 뷰포트는 '두 번째 3D 렌더 패스'라 가장 비싸다. 오른쪽 CRT 가 실제로 CCTV
        // 화면을 띄우고 있을 때만 갱신한다(UpdateActiveViewports).
        _facilityCctvVp = new SubViewport
        {
            Size = new Vector2I(
                Mathf.RoundToInt(640 * RenderScale), Mathf.RoundToInt(480 * RenderScale)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
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
        UpdateActiveViewports();
    }

    // 성능: CRT 프로그램은 5개(시설/CCTV/보고서/휴게명단/인터뷰)지만 한 번에 화면에
    // 붙어 있는 건 최대 2개다. 나머지는 매 프레임 render target 을 새로 그릴 이유가 없다.
    // 지금 어느 화면에도 안 붙은 뷰포트는 Disabled 로 내려 GPU/CPU 를 통째로 아낀다.
    // (Disabled 여도 안의 Control 은 _Process/_Input 을 그대로 받으므로 로직은 동일하다.)
    private void UpdateActiveViewports()
    {
        bool cctvOnScreen = false, interviewOnScreen = false;
        foreach (var vp in new[] { _facilityVp, _cctvVp, _reportVp, _restRosterVp, _interviewVp })
        {
            if (vp == null) continue;
            bool bound = false;
            foreach (var s in _screens)
                if (s.TargetViewport == vp) { bound = true; break; }

            // CRT 가 꺼져 있는 단계(시작 화면 / 근무 배치)에서는 화면이 사실상 검게
            // 눌려 있으므로 한 프레임만 그려두고 멈춘다(Once → 엔진이 알아서 Disabled).
            var want = !bound ? SubViewport.UpdateMode.Disabled
                : _brightness > 0.1f ? SubViewport.UpdateMode.Always
                : SubViewport.UpdateMode.Once;
            if (vp.RenderTargetUpdateMode != want) vp.RenderTargetUpdateMode = want;

            if (bound && vp == _cctvVp) cctvOnScreen = true;
            if (bound && vp == _interviewVp) interviewOnScreen = true;
        }

        _cctvOnScreen = cctvOnScreen;
        _interviewOnScreen = interviewOnScreen;
        UpdateCctvWorldViewport();
    }

    private bool _cctvOnScreen;
    private bool _interviewOnScreen;

    // 두 번째 3D 렌더 패스(작업실 월드)는 오른쪽 CRT 가 CCTV 를 띄우고 있고, 그 화면이
    // 실제로 켜져 있을 때만 돌린다. 시작 화면/근무 배치처럼 CRT 가 꺼져 있는 동안에는
    // 어차피 보이지 않으므로 통째로 멈춘다.
    private void UpdateCctvWorldViewport()
    {
        if (_facilityCctvVp == null) return;
        // 근무 CCTV : 화면에 붙어 있고 + CRT 가 켜져 있고 + 실제로 피드가 나오는 중일 때만.
        // (NO SIGNAL / CCTV 전력 OFF 동안에는 어차피 노이즈만 보이므로 3D 를 멈춘다.)
        // 휴게시간 인터뷰 : 같은 월드를 사이드뷰로 쓰므로 인터뷰 화면이 떠 있으면 켠다.
        bool feedLive = CCTVMonitorView.Instance?.FeedVisible ?? true;
        bool needed = (_cctvOnScreen && feedLive) || _interviewOnScreen;
        var want = needed && _brightness > 0.1f
            ? SubViewport.UpdateMode.Always
            : SubViewport.UpdateMode.Disabled;
        if (_facilityCctvVp.RenderTargetUpdateMode != want)
            _facilityCctvVp.RenderTargetUpdateMode = want;
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
            Size = ViewportSize(MonitorCanvasSize),
            // 어느 CRT 에도 안 붙은 동안은 그리지 않는다 — UpdateActiveViewports 가 켜준다.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
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

        UpdateActiveViewports();

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
        // CCTV 피드 상태는 매 프레임 바뀔 수 있으므로 여기서 계속 반영한다(값이 같으면 no-op).
        UpdateCctvWorldViewport();

        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;

        GameState.Instance.AdvanceDayTime((float)delta);
        FacilitySimulation.Instance?.Tick(delta);

    }

    // --- 입력 : CRT 레이캐스트 → 해당 View 로 전달 + 클릭 시 카메라 확대 ------

    public override void _Input(InputEvent @event)
    {
        if (_camera == null) return;

        // CanvasLayer 통화 HUD가 마우스 입력을 받는 동안에는 그 입력을 3D CRT로
        // 재투사하지 않는다. 그렇지 않으면 "통화를 종료한다" 클릭이 뒤쪽 휴게화면의
        // 다음 날 배치 버튼까지 동시에 눌릴 수 있다.
        if (PhoneCallHud.Instance?.IsOpen == true || Day1HistoryOverlay.Instance?.IsWindowOpen == true) return;

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

        // 화면 확대는 숫자키로만 한다 — 좌클릭은 순수하게 그 기기의 기능 조작에 쓴다.
        // 어떤 키가 무엇을 확대하는지는 설정(GameSettings)에서 바꿀 수 있다.
        // 같은 키를 다시 누르거나 ESC = 원래 자리로 복귀.
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            // ESC 는 PauseMenu 가 먼저 가져간다(확대 중이면 그쪽에서 UnzoomIfFocused 를 부른다).
            var target = GameSettings.TargetForKey(NormalizeNumpad(key.Keycode));
            if (target.HasValue)
            {
                ToggleFocusTarget(target.Value);
                GetViewport().SetInputAsHandled();
                return;
            }
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

        if (mb.Pressed && mb.ButtonIndex == MouseButton.Right && _focusedNode != null)
        {
            Unfocus();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 전력 기기를 확대했을 때에는 뒤쪽 CRT 평면으로 입력을 전달하지 않는다.
        // 입력을 처리하지 않은 채 반환해야 Area3D의 레버 클릭 판정이 정상적으로 받는다.
        if (IsFocusedPowerPanel())
        {
            _dragScreen = null;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left
                && ResolveTarget(GameSettings.ZoomTarget.PowerPanel) is PowerSwitchPanel panel
                && panel.TryInteractRay(origin, dir))
                GetViewport().SetInputAsHandled();
            return;
        }

        if (mb.Pressed)
        {
            var hit = PickScreen(origin, dir, out Vector2 canvasPos);
            if (hit == null) return;

            // 좌클릭은 확대하지 않는다(숫자키 1/2 담당) — 화면 안 UI 조작만 전달한다.
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
        if (IsFocusedPowerPanel()) return;

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

    // 숫자패드 1~9 도 같은 숫자로 취급한다.
    private static Key NormalizeNumpad(Key k) =>
        k is >= Key.Kp0 and <= Key.Kp9 ? Key.Key0 + (k - Key.Kp0) : k;

    // 확대 대상(모니터 2개 + 책상 위 기기 2개)을 실제 노드로 찾는다.
    private Node3D ResolveTarget(GameSettings.ZoomTarget t) => t switch
    {
        GameSettings.ZoomTarget.Monitor1 => _screens.FirstOrDefault(s => s.Name.ToString().Contains("01")),
        GameSettings.ZoomTarget.Monitor2 => _screens.FirstOrDefault(s => s.Name.ToString().Contains("02")),
        GameSettings.ZoomTarget.Sensor => GetNodeOrNull<Node3D>(SensorPath),
        GameSettings.ZoomTarget.PowerPanel => GetNodeOrNull<Node3D>(PowerPanelPath),
        _ => null,
    };

    private bool IsFocusedPowerPanel() =>
        _focusedNode != null && _focusedNode == ResolveTarget(GameSettings.ZoomTarget.PowerPanel);

    private void ToggleFocusTarget(GameSettings.ZoomTarget t)
    {
        var node = ResolveTarget(t);
        if (node == null) return;
        if (_focusedNode == node) { Unfocus(); return; }

        _focusedNode = node;
        _focusedScreen = node as MonitorScreen3D;

        // 모니터는 화면 앞으로, 책상 위 기기는 살짝 위에서 내려다보는 거리로 붙는다.
        bool isScreen = _focusedScreen != null;
        Vector3 center = node.GlobalPosition + (isScreen ? Vector3.Zero : DeskPropFocusOffset);
        Vector3 normal = node.GlobalTransform.Basis.Z.Normalized();
        _rig?.FocusOnScreen(center, normal, isScreen ? FocusDistance : DeskPropFocusDistance);
    }

    // 확대 중이면 풀고 true. PauseMenu 가 ESC 를 받았을 때 "메뉴 열기"보다 먼저 시도한다.
    public bool UnzoomIfFocused()
    {
        if (_focusedNode == null) return false;
        Unfocus();
        return true;
    }

    private void Unfocus()
    {
        if (_focusedNode == null) return;
        _focusedNode = null;
        _focusedScreen = null;
        _rig?.ReturnToSeat();
    }

    // --- PHASE 6 훅 ---------------------------------------------------

    public void SetScreenBrightness(float v)
    {
        _brightness = v;
        foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("brightness", v);
        UpdateActiveViewports();
    }
    public void SetScreenDistortion(float v) { _distortion = v; foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("h_distortion", v); }
    public void SetScreenNoise(float v) { _noise = v; foreach (var s in _screens) s.ScreenMaterial?.SetShaderParameter("noise_strength", v); }
}
