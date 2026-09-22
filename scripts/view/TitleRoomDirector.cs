using System;
using System.Threading.Tasks;
using Godot;
using NSP.Core;

namespace NSP.View;

// 타이틀 화면 2 — "중앙제어실 전체가 타이틀".
//
// 화면 위에 메뉴 패널을 얹지 않는다(그건 기존 TitleOverlay 의 방식이고 지금은 꺼 둔다).
// 대신 이미 있는 제어실 장비를 그대로 쓴다.
//   왼쪽 CRT(모니터 1)  = TitleTerminalView (표제 + 명령 선택지) — 여기서 게임이 시작된다
//   오른쪽 CRT(모니터 2) = TitleStaffIdView  (직원 여섯 명 신원 확인 + 무작위 오류 연출)
//   책상 장비  = 전화기 / 센서 단말 / 전력 패널 → 같은 명령의 지름길
//
// 게임을 켜면 오른쪽 CRT 를 확대한 화면에서 시작한다. 아무 키나 누르면 그 화면에서
// 프로그램이 실행되듯 표제가 한 글자씩 찍히고 메뉴가 한 줄씩 뜬 뒤, 그제서야 화면이
// 천천히 축소되며 제어실 전체와 책상 장비가 드러난다.
//
// 이 노드가 제어실 입력을 직접 레이캐스트한다. 근무 중 입력(ControlRoom3DController)은
// 타이틀 동안 잠겨 있으므로 서로 간섭하지 않는다.
public partial class TitleRoomDirector : Node
{
    public static TitleRoomDirector Instance { get; private set; }

    [Export] public NodePath ControllerPath = "..";
    [Export] public NodePath PhonePath = "../ControlRoom/Telephone";
    [Export] public NodePath SensorPath = "../ControlRoom/AlertTerminal";
    [Export] public NodePath PowerPanelPath = "../ControlRoom/PowerSwitchPanel";
    // 천장광 비활성 상태 — Lights 그룹이 숨겨져 있어 이 밝기 조절은 화면에 효과가 없다.
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingLight";
    [Export] public NodePath FillLightPath = "../ControlRoom/Lights/FillLight";

    // 책상 장비를 가리켰는지 판정하는 반경(m). 모델이 바뀌면 여기만 조정한다.
    [Export] public float PhoneRadius = 0.13f;
    [Export] public float SensorRadius = 0.12f;
    [Export] public float PowerRadius = 0.13f;

    public event Action StartRequested;
    public bool IsRunning { get; private set; }

    // 결말의 흔적 — 마지막으로 본 엔딩(EndingState)에 따라 시작 화면의 방 분위기가 다르다.
    //   실패 : 붉은 CRT · 아주 느리게 붉게 점멸하는 비상등 · BGM 대신 낮은 기계음/경고음 · 가끔 혼자 울리는 전화
    //   복구 : 아주 약한 아침빛 · 정상 색 · CRT 노이즈 감소 · 같은 곡의 조용한 버전
    public static float EndingScreenNoise => EndingState.Last switch
    {
        EndingState.Kind.Bad => 0.06f,
        EndingState.Kind.True => 0.008f,
        _ => 0.018f,
    };
    private SpotLight3D _m1, _m2, _deskFill;
    private OmniLight3D _emergency, _morning;
    private float _m1E, _m2E, _deskE;
    private Color _m1C, _m2C, _deskC;
    private bool _endingLook;
    private double _nextRing = 24.0, _nextBeep = 9.0;
    private float _pulseT;

    // 화면 오른쪽 아래에 늘 떠 있는 조작 안내. 이 한 줄만 남긴다.
    private const string MenuHint = "↑ ↓ 이동   ENTER 확인";
    // 확인 창(예/아니오)은 선택지가 가로로 놓인다 — 안내도 좌우로 바꾼다.
    private const string ConfirmHint = "← → 선택   ENTER 확인   ESC 취소";

    // 책상 장비 ↔ 명령 연결. 라벨은 화면 아래 힌트 줄에 뜬다.
    private static readonly (string Id, string Hint)[] PropHints =
    {
        ("archive", "전화기  —  ARCHIVE  ·  통신 기록"),
        ("config", "센서 단말  —  SYSTEM CONFIG  ·  환경 설정"),
        ("quit", "전력 패널  —  SHUT DOWN  ·  시스템 종료"),
    };

    private ControlRoom3DController _ctl;
    private Camera3D _camera;
    private Node3D _phone, _sensor, _power;
    private OmniLight3D _ceiling, _fill;
    private float _ceilBase = 1.1f, _fillBase = 0.4f;
    private SettingsPanel _settings;

    private TitleHintHud _hint;

    private enum Phase { Off, Standby, PoweringOn, Menu, Busy, Done }
    private Phase _phase = Phase.Off;

    private string _hoverProp = "";
    // 마우스 위치 — 실제 커서 폴링과 모션 이벤트 중 최근 것을 쓴다.
    private Vector2 _mouse;
    private Vector2 _lastPolled = new(-9999f, -9999f);
    // 이번 프레임에 마우스가 실제로 움직였는가(호버가 커서를 가져갈 자격).
    private bool _mouseMoved;
    private double _nextFlicker = 7.0, _nextDistant = 11.0;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
        _ctl = GetNodeOrNull<ControlRoom3DController>(ControllerPath);
        _phone = GetNodeOrNull<Node3D>(PhonePath);
        _sensor = GetNodeOrNull<Node3D>(SensorPath);
        _power = GetNodeOrNull<Node3D>(PowerPanelPath);
        _ceiling = GetNodeOrNull<OmniLight3D>(CeilingLightPath);
        _fill = GetNodeOrNull<OmniLight3D>(FillLightPath);

        _hint = new TitleHintHud();
        AddChild(_hint);

        SetProcess(false);
        SetProcessUnhandledInput(false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ShiftFlowController 가 부팅 직후 부른다.
    public void Begin()
    {
        if (IsRunning) return;
        IsRunning = true;
        _ = BootAsync();
    }

    private async Task BootAsync()
    {
        // 컨트롤러의 AfterReady(CallDeferred)가 CRT 를 초기 상태로 돌려놓은 뒤에 시작한다.
        await NextFrame();
        await NextFrame();

        _camera = GetViewport().GetCamera3D();
        if (_ctl == null || TitleTerminalView.Instance == null || TitleStaffIdView.Instance == null)
        {
            GD.PushWarning("TitleRoomDirector: 타이틀 화면을 찾지 못해 건너뜁니다.");
            IsRunning = false;
            StartRequested?.Invoke();
            return;
        }

        if (_ceiling != null) _ceilBase = _ceiling.LightEnergy / 0.26f;
        if (_fill != null) _fillBase = _fill.LightEnergy / 0.3f;

        _ctl.SetLeftScreen(_ctl.TitleTerminalViewport);
        _ctl.SetRightScreen(_ctl.TitleStaffViewport);

        // 뷰포트가 매 프레임 갱신되도록 전체 밝기는 올려 두고, 오른쪽 CRT 만 꺼 둔다.
        _ctl.SetScreenBrightness(1f);
        // 오른쪽 CRT(직원 신원)는 아직 꺼져 있다 — 약한 노이즈만 보일 정도로.
        _ctl.SetScreenBrightnessFor("02", 0.13f);
        // 타이틀 동안에는 화면 노이즈를 조금 낮춘다(표제와 메뉴가 첫인상이다).
        _ctl.SetScreenNoise(EndingScreenNoise);
        ApplyEndingLook();

        TitleStaffIdView.Instance.PowerOff();
        TitleTerminalView.Instance.ShowStandby();

        // 게임 시작 순간부터 왼쪽 CRT 확대 화면이다(카메라를 즉시 그 자리에 둔다).
        // 엔딩 뒤에 다시 눈을 뜬 경우만 예외 — 방 전체(붉은 CRT · 비상등 / 아침빛)가 먼저 보여야 한다.
        if (EndingState.Last == EndingState.Kind.None) _ctl.FocusMonitor(1, 0.01f);

        // 대기 화면의 안내는 단말기 자체에 "[ PRESS ANY KEY ]" 로 떠 있다 — 겹쳐 쓰지 않는다.
        _hint.SetLine("");
        _hint.SetSub("");
        _hint.ShowHud();

        _phase = Phase.Standby;
        SetProcess(true);
        SetProcessUnhandledInput(true);
    }

    // --- 전원 투입 ------------------------------------------------------------

    private async Task PowerOnAsync()
    {
        _phase = Phase.PoweringOn;
        _hint.SetSub("");
        Sfx.Instance?.Play("sensor_beep", -8f);       // 삑

        // 1) 확대된 화면에서 표제가 한 글자씩 찍히고 메뉴가 한 줄씩 뜬다.
        var term = TitleTerminalView.Instance;
        term?.ShowMenu(true);
        await Wait(0.18);
        while (term != null && !term.MenuRevealDone) await NextFrame();
        await Wait(0.30);

        // 2) 화면이 축소되며 제어실 전체가 드러난다.
        _ctl?.ClearFocus(0.70f);
        await Wait(0.30);

        // 3) 축소되는 동안 나머지 장비가 하나씩 켜진다.
        Sfx.Instance?.Play("relay_click", -7f);        // 오른쪽 모니터(직원 신원) ON
        var t1 = CreateTween();
        t1.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightnessFor("02", v)), 0.13f, 1.0f, 0.5)
          .SetTrans(Tween.TransitionType.Sine);
        TitleStaffIdView.Instance?.PowerOn();

        await Wait(0.45);
        Sfx.Instance?.Play("switch", -10f);             // 책상 조명 ON
        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilBase * 0.34f, 0.7);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", _fillBase * 0.45f, 0.7);

        await Wait(0.40);
        Sfx.Instance?.Play("relay_click", -12f);        // 센서 단말 ON

        await Wait(0.35);
        _hint.SetSub(MenuHint);
        _phase = Phase.Menu;
    }

    // --- 명령 --------------------------------------------------------------

    private void Activate(string id)
    {
        if (_phase != Phase.Menu) return;
        Sfx.Instance?.Play("relay_click", -6f);
        switch (id)
        {
            case "start": ShowStartConfirm(); break;
            case "archive": _ = ArchiveAsync(); break;
            case "config": OpenSettings(); break;
            case "quit": ShowShutdownConfirm(); break;
            case "back": TitleTerminalView.Instance?.ShowMenu(); break;
        }
    }

    // 근무 개시 — 인증 연출을 거쳐 그대로 프롤로그로 이어진다.
    private async Task StartShiftAsync()
    {
        _phase = Phase.Busy;
        _hint.SetSub("");
        var term = TitleTerminalView.Instance;
        var staff = TitleStaffIdView.Instance;

        term.BeginReport("ADMINISTRATOR ACCESS");
        term.PushLine("> auth --administrator", 3);
        await Wait(0.55);
        term.PushLine("SCANNING STAFF IDENTIFICATION...");

        await Wait(0.35);
        var tcs = new TaskCompletionSource();
        staff.RunScan(() => tcs.TrySetResult());
        await tcs.Task;

        await Wait(0.30);
        term.PushLine("ACCESS GRANTED", 1);
        Sfx.Instance?.Play("task_done", -5f);
        await Wait(1.10);

        // 화면을 한 번 내려 두고 넘긴다 — 프롤로그가 다시 켜는 연출로 자연히 이어진다.
        ClearEndingLook();
        _ctl?.SetScreenNoise(0.035f);   // 게임 화면의 기본 노이즈로 되돌린다
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 1.0f, 0.02f, 0.45)
         .SetTrans(Tween.TransitionType.Sine);
        await Wait(0.55);

        Finish();
        StartRequested?.Invoke();
    }

    // 기록 열람 — 아직 저장/기록 시스템이 없으므로 "기록 없음"만 알린다.
    private async Task ArchiveAsync()
    {
        _phase = Phase.Busy;
        var term = TitleTerminalView.Instance;
        Sfx.Instance?.Play("phone_pickup", -16f);   // 전화기 쪽이 살아난다
        term.BeginReport("ARCHIVE  /  통신 기록", ("back", "뒤로"));
        term.PushLine("> archive --list", 3);
        await Wait(0.45);
        term.PushLine("저장된 근무 기록이 없습니다.");
        await Wait(0.25);
        term.PushLine("NO RECORD FOUND", 3);
        _hint.SetSub("ESC 또는 ‘뒤로’");
        _phase = Phase.Menu;
    }

    private void OpenSettings()
    {
        if (_settings == null || !IsInstanceValid(_settings))
        {
            _settings = new SettingsPanel();
            AddChild(_settings);
        }
        _settings.Open();
    }

    // 근무 개시 — 시스템 종료와 같은 확인 창을 한 번 거친다.
    private void ShowStartConfirm()
    {
        var term = TitleTerminalView.Instance;
        term.BeginReport("BEGIN NIGHT SHIFT?", ("go", "예"), ("no", "아니오"));
        term.PushLine("근무를 개시하시겠습니까?", 1);
        _hint.SetSub(ConfirmHint);
    }

    private void ShowShutdownConfirm()
    {
        var term = TitleTerminalView.Instance;
        term.BeginReport("SHUT DOWN SYSTEM?", ("yes", "예"), ("no", "아니오"));
        term.PushLine("시스템을 종료하시겠습니까?", 2);
        _hint.SetSub(ConfirmHint);
    }

    // 종료 — 장비가 하나씩 꺼지고 SYSTEM OFFLINE.
    private async Task ShutdownAsync()
    {
        _phase = Phase.Busy;
        ClearEndingLook();
        _hint.SetLine("");
        _hint.SetSub("");
        var term = TitleTerminalView.Instance;

        TitleStaffIdView.Instance?.PowerOff();
        Sfx.Instance?.Play("relay_click", -6f);
        var t1 = CreateTween();
        t1.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightnessFor("01", v)), 1.0f, 0.02f, 0.4);

        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", 0.02f, 1.0);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", 0.02f, 1.0);

        await Wait(0.55);
        term.BeginReport("");
        term.PushLine("SYSTEM OFFLINE", 3);
        Sfx.Instance?.Play("power_down", -6f);

        await Wait(1.10);
        var t2 = CreateTween();
        t2.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 1.0f, 0.0f, 0.7);
        await Wait(0.9);
        GetTree().Quit();
    }

    private void Finish()
    {
        _phase = Phase.Done;
        IsRunning = false;
        SetProcess(false);
        SetProcessUnhandledInput(false);
        _hint.HideHud();
    }

    // --- 입력 ---------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (_phase is Phase.Off or Phase.Done or Phase.PoweringOn) return;
        if (_settings != null && IsInstanceValid(_settings) && _settings.Visible) return;

        if (e is InputEventMouseMotion mm) { _mouse = mm.Position; _mouseMoved = true; return; }

        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (_phase == Phase.Standby)
            {
                _ = PowerOnAsync();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (_phase != Phase.Menu) return;

            switch (k.Keycode)
            {
                case Key.Up or Key.W or Key.Left or Key.A:
                    if (TitleTerminalView.Instance.MoveCursor(-1)) Sfx.Instance?.Play("tick", -16f);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Down or Key.S or Key.Right or Key.D:
                    if (TitleTerminalView.Instance.MoveCursor(1)) Sfx.Instance?.Play("tick", -16f);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Enter or Key.KpEnter or Key.Space:
                    Select();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Escape:
                    if (TitleTerminalView.Instance.CurrentMode == TitleTerminalView.Mode.Report)
                    {
                        TitleTerminalView.Instance.ShowMenu();
                        _hint.SetSub(MenuHint);
                        GetViewport().SetInputAsHandled();
                    }
                    return;
            }
            return;
        }

        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            if (_phase == Phase.Standby)
            {
                _ = PowerOnAsync();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (_phase != Phase.Menu) return;

            // 장비를 눌렀으면 그 장비에 걸린 명령, 아니면 단말기에서 가리킨 항목.
            string id = !string.IsNullOrEmpty(_hoverProp) ? _hoverProp : TerminalItemUnderMouse();
            if (!string.IsNullOrEmpty(id)) { ActivateResolved(id); GetViewport().SetInputAsHandled(); }
        }
    }

    private void Select() => ActivateResolved(TitleTerminalView.Instance.SelectedId);

    // 종료 확인 창의 예/아니오는 일반 명령과 다르게 처리한다.
    private void ActivateResolved(string id)
    {
        switch (id)
        {
            case "go": _ = StartShiftAsync(); return;
            case "yes": _ = ShutdownAsync(); return;
            case "no":
            case "back":
                Sfx.Instance?.Play("relay_click", -8f);
                TitleTerminalView.Instance.ShowMenu();
                _hint.SetSub(MenuHint);
                return;
            default:
                Activate(id);
                return;
        }
    }

    // --- tick : 마우스 호버 + 잔잔한 생활감 --------------------------------------

    public override void _Process(double delta)
    {
        TickAmbience(delta);
        if (_phase != Phase.Menu) return;
        if (_camera == null) _camera = GetViewport().GetCamera3D();
        if (_camera == null) return;

        Vector2 polled = GetViewport().GetMousePosition();
        if (!polled.IsEqualApprox(_lastPolled)) { _lastPolled = polled; _mouse = polled; _mouseMoved = true; }
        // 마우스가 가만히 있으면 호버가 커서를 가져가지 않는다. 그러지 않으면 방향키로
        // 옮긴 커서를 멈춰 있는 마우스가 매 프레임 도로 끌어와 방향키가 먹통이 된다.
        bool takeCursor = _mouseMoved;
        _mouseMoved = false;
        Vector3 origin = _camera.ProjectRayOrigin(_mouse);
        Vector3 dir = _camera.ProjectRayNormal(_mouse);

        // 1) 책상 장비.
        string prop = PropUnder(origin, dir);
        if (prop != _hoverProp)
        {
            _hoverProp = prop;
            if (!string.IsNullOrEmpty(prop))
            {
                Sfx.Instance?.Play("tick", -16f);
                foreach (var (id, hintText) in PropHints)
                    if (id == prop) _hint.SetLine(hintText);
                if (takeCursor) TitleTerminalView.Instance.HoverId(prop);
            }
            else _hint.SetLine("");
        }
        if (!string.IsNullOrEmpty(prop))
        {
            TitleStaffIdView.Instance.SetHover(-1);
            return;
        }

        // 2) 왼쪽 CRT 의 명령 선택지. 마우스를 올리면 커서가 따라오고 클릭하면 실행된다.
        string item = TerminalItemUnderMouse();
        if (!string.IsNullOrEmpty(item))
        {
            if (takeCursor && TitleTerminalView.Instance.HoverId(item)) Sfx.Instance?.Play("tick", -16f);
            TitleStaffIdView.Instance.SetHover(-1);
            _hint.SetLine("");
            return;
        }

        // 3) 오른쪽 CRT 의 직원 카드 — 마우스를 올리면 신원 한 줄이 뜬다.
        int card = -1;
        if (TryCanvasPos("02", origin, dir, out Vector2 lp))
            card = TitleStaffIdView.Instance.IndexAt(lp);
        if (TitleStaffIdView.Instance.SetHover(card)) Sfx.Instance?.Play("tick", -20f);
        _hint.SetLine(card >= 0 ? TitleStaffIdView.Instance.HoverLine : "");
    }

    private void TickAmbience(double delta)
    {
        if (_phase is Phase.Off or Phase.Done) return;
        TickEndingLook(delta);

        // 형광등이 가끔 한 번 깜빡인다.
        _nextFlicker -= delta;
        if (_nextFlicker <= 0 && _ceiling != null && _phase != Phase.Standby)
        {
            _nextFlicker = _rng.RandfRange(7f, 15f);
            float keep = _ceiling.LightEnergy;
            var t = CreateTween();
            t.TweenProperty(_ceiling, "light_energy", keep * 0.25f, 0.05);
            t.TweenProperty(_ceiling, "light_energy", keep, 0.10);
            Sfx.Instance?.Play("flicker", -26f);
        }

        // 멀리서 금속 부딪히는 소리.
        _nextDistant -= delta;
        if (_nextDistant <= 0)
        {
            _nextDistant = _rng.RandfRange(10f, 20f);
            Sfx.Instance?.Play(_rng.Randf() > 0.5f ? "metal_clang" : "pipe_knock", -28f);
        }
    }

    // --- 결말의 흔적 -----------------------------------------------------------

    private void ApplyEndingLook()
    {
        var kind = EndingState.Last;
        if (kind == EndingState.Kind.None || _ctl == null) return;
        _endingLook = true;
        _m1 = _ctl.GetNodeOrNull<SpotLight3D>("ControlRoom/Monitor01/M01_ScreenLight");
        _m2 = _ctl.GetNodeOrNull<SpotLight3D>("ControlRoom/Monitor02/M02_ScreenLight");
        _deskFill = _ctl.GetNodeOrNull<SpotLight3D>("ControlRoom/DeskFillLight");
        _emergency = _ctl.GetNodeOrNull<OmniLight3D>("ControlRoom/EmergencyLight");
        if (_m1 != null) { _m1E = _m1.LightEnergy; _m1C = _m1.LightColor; }
        if (_m2 != null) { _m2E = _m2.LightEnergy; _m2C = _m2.LightColor; }
        if (_deskFill != null) { _deskE = _deskFill.LightEnergy; _deskC = _deskFill.LightColor; }

        if (kind == EndingState.Kind.Bad)
        {
            ControlRoom3DHorror.ExternalLightingOverride = true;
            _ctl.SetScreenTint(new Color(1.3f, 0.30f, 0.26f));
            var red = new Color(1f, 0.22f, 0.18f);
            if (_m1 != null) _m1.LightColor = red;
            if (_m2 != null) _m2.LightColor = red;
            if (_deskFill != null) { _deskFill.LightColor = new Color(0.8f, 0.35f, 0.32f); _deskFill.LightEnergy = _deskE * 0.6f; }
            if (_emergency != null) { _emergency.Visible = true; _emergency.LightColor = new Color(0.95f, 0.1f, 0.08f); }
            Sfx.Instance?.FadeOutMusic(1.0f);
            Sfx.Instance?.Loop("drone_loop", -18f);
            Sfx.Instance?.Loop("machinery_loop", -27f);
            return;
        }

        // 복구 — 아주 약한 아침빛. 붉거나 불안정했던 표시 없이 정상 색에 따뜻함만 조금.
        _ctl.SetScreenTint(new Color(1.03f, 1.0f, 0.95f));
        var warm = new Color(1f, 0.86f, 0.70f);
        if (_deskFill != null) { _deskFill.LightColor = _deskC.Lerp(warm, 0.6f); _deskFill.LightEnergy = _deskE * 1.35f; }
        _morning = new OmniLight3D
        {
            Name = "TitleMorningLight",
            LightColor = new Color(1f, 0.82f, 0.62f),
            LightEnergy = 0.55f,
            OmniRange = 4.8f,
            ShadowEnabled = false,
            Position = new Vector3(-1.8f, 2.3f, -0.6f),
        };
        _ctl.GetNodeOrNull<Node3D>("ControlRoom")?.AddChild(_morning);
        Sfx.Instance?.CrossfadeMusic("rest_time", 1.5f, loop: true, targetDb: -15f, restartIfSame: true);
    }

    private void TickEndingLook(double delta)
    {
        if (!_endingLook || EndingState.Last != EndingState.Kind.Bad) return;
        // 방 전체 비상등이 아주 느리게 붉게 점멸한다.
        _pulseT += (float)delta;
        if (_emergency != null)
            _emergency.LightEnergy = 0.15f + 1.4f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(_pulseT * 1.05f), 2f);
        // 가끔 전화기가 혼자 울리거나 잡음이 난다.
        _nextRing -= delta;
        if (_nextRing <= 0)
        {
            _nextRing = _rng.RandfRange(22f, 40f);
            Sfx.Instance?.Play(_rng.Randf() < 0.6f ? "call_ring" : "radio_static", -20f);
        }
        _nextBeep -= delta;
        if (_nextBeep <= 0)
        {
            _nextBeep = _rng.RandfRange(8f, 16f);
            Sfx.Instance?.Play("alert_beep3", -26f);
        }
    }

    // 새 근무를 시작하면(또는 시스템 종료) 방을 원래대로 돌린다.
    private void ClearEndingLook()
    {
        if (!_endingLook) return;
        _endingLook = false;
        _ctl?.SetScreenTint(Colors.White);
        if (_m1 != null) { _m1.LightColor = _m1C; _m1.LightEnergy = _m1E; }
        if (_m2 != null) { _m2.LightColor = _m2C; _m2.LightEnergy = _m2E; }
        if (_deskFill != null) { _deskFill.LightColor = _deskC; _deskFill.LightEnergy = _deskE; }
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
        if (_morning != null && IsInstanceValid(_morning)) _morning.QueueFree();
        _morning = null;
        Sfx.Instance?.StopLoop("drone_loop");
        Sfx.Instance?.StopLoop("machinery_loop");
        ControlRoom3DHorror.ExternalLightingOverride = false;
    }

    // --- 레이캐스트 헬퍼 --------------------------------------------------------

    private string TerminalItemUnderMouse()
    {
        if (_camera == null) return "";
        Vector3 o = _camera.ProjectRayOrigin(_mouse);
        Vector3 d = _camera.ProjectRayNormal(_mouse);
        return TryCanvasPos("01", o, d, out Vector2 p) ? TitleTerminalView.Instance.ItemAt(p) : "";
    }

    // CRT 평면을 맞췄으면 그 화면의 '논리 캔버스' 좌표를 돌려준다.
    // (TryProjectRay 는 실제 뷰포트 해상도 좌표를 주므로 스케일로 나눈다.)
    private bool TryCanvasPos(string token, Vector3 origin, Vector3 dir, out Vector2 logical)
    {
        logical = Vector2.Zero;
        if (_ctl == null) return false;
        foreach (var s in _ctl.Screens)
        {
            if (!s.Name.ToString().Contains(token)) continue;
            if (!s.TryProjectRay(origin, dir, clamp: false, out Vector2 vp)) return false;
            logical = vp / Mathf.Max(0.01f, ControlRoom3DController.SurfaceScale());
            return true;
        }
        return false;
    }

    private string PropUnder(Vector3 origin, Vector3 dir)
    {
        if (_phone != null && RayHitsSphere(origin, dir, _phone.GlobalPosition, PhoneRadius)) return "archive";
        if (_sensor != null && RayHitsSphere(origin, dir, _sensor.GlobalPosition, SensorRadius)) return "config";
        if (_power != null && RayHitsSphere(origin, dir, _power.GlobalPosition, PowerRadius)) return "quit";
        return "";
    }

    private static bool RayHitsSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius)
    {
        Vector3 oc = origin - center;
        float b = oc.Dot(dir);
        float c = oc.LengthSquared() - radius * radius;
        float disc = b * b - c;
        return disc >= 0f && -b + Mathf.Sqrt(disc) > 0f;
    }

    private async Task NextFrame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // --- 화면 아래 어두운 공간의 힌트 줄 ------------------------------------------
    private partial class TitleHintHud : CanvasLayer
    {
        private Label _line, _sub;

        public override void _Ready()
        {
            Layer = 78;   // 시작 화면(80) 아래
            var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            // 마우스를 올린 장비/직원의 한 줄 설명 — 가운데 아래.
            _line = Make(ViewFont.FS(17), new Color(0.72f, 0.86f, 0.84f), -96f);
            root.AddChild(_line);

            // 조작 안내는 오른쪽 아래 구석에 작게만 둔다.
            _sub = Make(ViewFont.FS(13), new Color(0.40f, 0.48f, 0.50f), -58f);
            _sub.HorizontalAlignment = HorizontalAlignment.Right;
            _sub.AnchorLeft = 1f;
            _sub.OffsetLeft = -640f;
            _sub.OffsetRight = -28f;
            root.AddChild(_sub);
            Visible = false;
        }

        private static Label Make(int size, Color col, float bottomOffset)
        {
            var l = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
                OffsetTop = bottomOffset, OffsetBottom = bottomOffset + 34f,
            };
            l.AddThemeFontOverride("font", ViewFont.Default);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            return l;
        }

        public void SetLine(string t) { if (_line != null) _line.Text = t ?? ""; }
        public void SetSub(string t) { if (_sub != null) _sub.Text = t ?? ""; }
        public void ShowHud() => Visible = true;
        public void HideHud() => Visible = false;
    }
}
