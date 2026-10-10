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
//   책상 장비  = 전화기 / 관리자 패드 / 전력 패널 → 같은 명령의 지름길
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
    // 설정 메뉴가 붙은 책상 장비 — 예전 경고 단말기 자리의 관리자 패드(거치대).
    [Export] public NodePath SensorPath = "../ControlRoom/AdminPad";
    [Export] public NodePath PowerPanelPath = "../ControlRoom/PowerSwitchPanel";
    // 천장광 비활성 상태 — Lights 그룹이 숨겨져 있어 이 밝기 조절은 화면에 효과가 없다.
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingLight";
    [Export] public NodePath FillLightPath = "../ControlRoom/Lights/FillLight";

    // 책상 장비를 가리켰는지 판정하는 반경(m). 모델이 바뀌면 여기만 조정한다.
    [Export] public float PhoneRadius = 0.13f;
    [Export] public float SensorRadius = 0.15f;
    [Export] public float PowerRadius = 0.13f;

    public event Action StartRequested;
    public bool IsRunning { get; private set; }

    // 메뉴 조작을 실제로 받는 상태인가(전원 투입 연출이 끝났는가).
    // 캡처 도구가 "지금 키를 눌러도 되는가"를 묻는 데 쓴다 — 연출 중에 누르면 무시된다.
    public bool AtMenu => _phase == Phase.Menu;

    // 결말의 흔적 — 마지막으로 본 엔딩(EndingState)에 따라 시작 화면의 방 분위기가 다르다.
    //   Bad  : 붉은 CRT · 아주 느리게 붉게 점멸하는 비상등 · BGM 대신 낮은 기계음/경고음 · 가끔 혼자 울리는 전화
    //   Late : 영구 봉쇄 뒤. Bad 와 같은 어두운 방이되 경보는 울리지 않는다.
    //   True : 아주 약한 아침빛 · 정상 색 · CRT 노이즈 감소 · 같은 곡의 조용한 버전
    //   Loose: True 와 똑같이 밝다. 20초에 한 번 0.2초짜리 노이즈가 튀는 것만 다르다 —
    //          조용한데 가끔 어긋난다.
    public static float EndingScreenNoise => EndingState.Last switch
    {
        EndingState.Kind.Bad => 0.06f,
        EndingState.Kind.Late => 0.045f,
        EndingState.Kind.Loose => 0.03f,
        EndingState.Kind.True => 0.008f,
        _ => 0.018f,
    };
    private SpotLight3D _m1, _m2, _deskFill;
    private OmniLight3D _emergency, _morning;
    private float _m1E, _m2E, _deskE;
    private Color _m1C, _m2C, _deskC;
    private bool _endingLook;
    private double _nextRing = 24.0, _nextBeep = 9.0;
    private double _looseGlitch = 14.0, _looseGlitchUntil;
    private float _pulseT;

    // 화면 오른쪽 아래에 늘 떠 있는 조작 안내. 이 한 줄만 남긴다.
    private const string MenuHint = "↑ ↓ 이동   ENTER 확인";
    // 확인 창(예/아니오)은 선택지가 가로로 놓인다 — 안내도 좌우로 바꾼다.
    private const string ConfirmHint = "← → 선택   ENTER 확인   ESC 취소";

    // 책상 장비 ↔ 명령 연결. 라벨은 화면 아래 힌트 줄에 뜬다.
    private static readonly (string Id, string Hint)[] PropHints =
    {
        ("archive", "전화기  —  ARCHIVE  ·  도전과제 기록"),
        ("config", "관리자 패드  —  SYSTEM CONFIG  ·  환경 설정"),
        ("quit", "전력 패널  —  SHUT DOWN  ·  시스템 종료"),
    };

    private ControlRoom3DController _ctl;
    private Camera3D _camera;
    private Node3D _phone, _sensor, _power;
    private OmniLight3D _ceiling, _fill;
    private float _ceilBase = 1.1f, _fillBase = 0.4f;
    private SettingsPanel _settings;

    private TitleHintHud _hint;

    // Archive   = 도전과제 기록실을 보는 중. 메뉴 입력과 완전히 분리해 두어
    //             기록을 넘기다가 「근무 개시」가 실수로 실행되지 않게 한다.
    // ModeSelect = 근무 유형(대회용 / 기본 / 하드)을 고르는 중. 마찬가지로 분리한다.
    private enum Phase { Off, Standby, PoweringOn, Menu, Busy, Archive, ModeSelect, Done }
    private Phase _phase = Phase.Off;

    // 기록실 · 근무 유형 선택을 보는 동안의 조작 안내.
    private const string ArchiveHint = "↑ ↓ 이동   휠 스크롤   ESC 뒤로";
    private const string ModeHint = "↑ ↓ 이동   ENTER 확정   ESC 취소";
    // 휠 한 칸에 굴러가는 거리(논리 px). 한 줄(78px)보다 조금 작게 — 줄이 툭툭 끊기지 않는다.
    private const float ArchiveWheelStep = 62f;

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
        // 「업무를 시작합니다」 — 실제 타이틀 화면에 들어선 지점.
        // 개발 도구로 타이틀을 건너뛰고 들어오면 이 줄을 지나가지 않는다.
        AchievementManager.Instance?.NoteTitleReached();
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

        // 2) **화면을 축소하지 않는다.** 시작 화면은 모니터1 확대 화면 그대로다 —
        //    표제와 메뉴가 화면을 꽉 채운 채로 선택까지 간다. 제어실 전체는
        //    근무가 시작될 때 처음 보인다.
        await Wait(0.30);

        // 3) 그 사이 나머지 장비가 하나씩 켜진다(소리로 들린다).
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
        Sfx.Instance?.Play("relay_click", -12f);        // 관리자 패드 ON

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
            case "start": ShowModeSelect(); break;
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

    // 기록 열람 — 도전과제 기록실. 단말기가 목록을 불러오는 두 줄을 찍고,
    // 그 위에 기록실 화면이 켜진다. 글자를 읽을 수 있도록 왼쪽 CRT 를 확대해 둔다.
    private async Task ArchiveAsync()
    {
        _phase = Phase.Busy;
        var term = TitleTerminalView.Instance;
        var view = AchievementArchiveView.Instance;
        Sfx.Instance?.Play("phone_pickup", -16f);   // 전화기 쪽이 살아난다
        term.BeginReport("ACHIEVEMENT ARCHIVE");
        term.PushLine("> archive --achievements", 3);
        await Wait(0.40);
        int done = AchievementManager.Instance?.GetUnlockedCount() ?? 0;
        term.PushLine($"RECORDS  {done:00} / {Achievements.Total:00}", 1);
        await Wait(0.35);
        if (view == null)
        {
            // 기록실 화면을 못 찾았다 — 숫자만 알리고 메뉴로 돌아간다(게임은 멈추지 않는다).
            term.PushLine("ARCHIVE VIEW NOT FOUND", 2);
            _hint.SetSub("ESC 또는 ‘뒤로’");
            _phase = Phase.Menu;
            return;
        }
        view.Open();
        Sfx.Instance?.Play("window_open", -8f);
        _ctl?.FocusMonitor(1, 0.5f);   // 1 = 왼쪽 CRT(단말기) — 글자를 읽을 크기로 당긴다
        _hint.SetLine("");
        _hint.SetSub(ArchiveHint);
        _phase = Phase.Archive;
    }

    // 기록실을 닫고 타이틀 메뉴로 돌아간다.
    private void CloseArchive()
    {
        AchievementArchiveView.Instance?.Close();
        Sfx.Instance?.Play("relay_click", -8f);
        // 확대는 풀지 않는다 — 타이틀은 처음부터 끝까지 모니터1 확대 화면이다.
        TitleTerminalView.Instance?.ShowMenu();
        _hint.SetSub(MenuHint);
        _phase = Phase.Menu;
    }

    // 기록실의 키 입력. 메뉴 입력과 섞이지 않는다.
    private void ArchiveKey(Key code)
    {
        var view = AchievementArchiveView.Instance;
        if (view == null) { CloseArchive(); return; }
        switch (code)
        {
            case Key.Up or Key.W:
                if (view.MoveCursor(-1)) Sfx.Instance?.Play("tick", -16f);
                return;
            case Key.Down or Key.S:
                if (view.MoveCursor(1)) Sfx.Instance?.Play("tick", -16f);
                return;
            // 한 화면씩 건너뛰기. A · D 를 쓰지 않는 이유는 D 가 프로젝트 입력맵의
            // '대화 기록 열기'(L 은 시설 로그)라 Day1HistoryOverlay 가 _Input 단계에서
            // 먼저 가져가기 때문이다 — 여기까지 오지 않는다.
            case Key.Pageup or Key.Home:
                if (view.MovePage(-1)) Sfx.Instance?.Play("tick", -13f);
                return;
            case Key.Pagedown or Key.End:
                if (view.MovePage(1)) Sfx.Instance?.Play("tick", -13f);
                return;
            case Key.Escape or Key.Backspace:
                CloseArchive();
                return;
        }
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

    // 근무 개시 — 예/아니오 확인 대신 **근무 유형을 고른다.** 고르는 행위가 곧 확인이고,
    // 취소는 ESC 다. 여기서는 아직 아무것도 초기화하지 않는다 — 실제 새 게임은
    // 유형을 확정한 뒤 StartShiftAsync → StartRequested 에서 시작된다.
    private void ShowModeSelect()
    {
        TitleTerminalView.Instance?.ShowModeSelect();
        _hint.SetLine("");
        _hint.SetSub(ModeHint);
        _phase = Phase.ModeSelect;
    }

    // 고른 유형으로 확정. 잠긴 유형은 거부음만 내고 아무 일도 일어나지 않는다.
    private void ConfirmMode()
    {
        var term = TitleTerminalView.Instance;
        if (term == null) return;
        var mode = term.SelectedMode;
        if (!GameModes.IsPlayable(mode))
        {
            Sfx.Instance?.Play("switch_fail", -7f);
            return;
        }
        GameModes.Select(mode);
        GD.Print($"[근무 유형: {GameModes.DisplayName(mode)} — {GameModes.MaxDays}일]");
        Sfx.Instance?.Play("relay_click", -5f);
        _ = StartShiftAsync();
    }

    private void CancelModeSelect()
    {
        Sfx.Instance?.Play("relay_click", -8f);
        TitleTerminalView.Instance?.ShowMenu();
        _hint.SetSub(MenuHint);
        _phase = Phase.Menu;
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

        // [예] 를 누른 뒤로는 **멈추지 않고 그대로 꺼진다.**
        // 예전에는 SYSTEM OFFLINE 을 1.1초 띄우고 화면 밝기만 0 으로 내린 뒤 종료했는데,
        // 3D 방 자체는 어둡기만 할 뿐 검지 않아서 창이 사라지는 순간 뒤의 바탕화면(과
        // 콘솔 창)이 번쩍 보였다. 지금은 BlinkOverlay 로 **화면 전체를 진짜 검게** 덮고,
        // 그 암전이 끝난 프레임에 종료한다.
        term.BeginReport("");
        term.PushLine("SYSTEM OFFLINE", 3);
        Sfx.Instance?.Play("power_down", -6f);

        var t2 = CreateTween();
        t2.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 1.0f, 0.0f, 0.45);

        var blink = NSP.Ui.BlinkOverlay.Instance;
        if (blink != null) await blink.Close(0.55);
        else await Wait(0.55);
        await Wait(0.06);   // 완전히 검어진 화면을 한 프레임 이상 보여 주고 끈다
        GetTree().Quit();
    }

    private void Finish()
    {
        AchievementArchiveView.Instance?.Close();
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
            // 기록실을 보는 중 — 이동 · 스크롤 · 뒤로만 받는다(ENTER 로 근무가 시작되지 않는다).
            if (_phase == Phase.Archive)
            {
                ArchiveKey(k.Keycode);
                GetViewport().SetInputAsHandled();
                return;
            }

            // 근무 유형을 고르는 중.
            if (_phase == Phase.ModeSelect)
            {
                switch (k.Keycode)
                {
                    case Key.Up or Key.W or Key.Left or Key.A:
                        if (TitleTerminalView.Instance.MoveCursor(-1)) Sfx.Instance?.Play("tick", -16f);
                        break;
                    case Key.Down or Key.S or Key.Right:
                        if (TitleTerminalView.Instance.MoveCursor(1)) Sfx.Instance?.Play("tick", -16f);
                        break;
                    case Key.Enter or Key.KpEnter or Key.Space:
                        ConfirmMode();
                        break;
                    case Key.Escape or Key.Backspace:
                        CancelModeSelect();
                        break;
                }
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

        // 기록실 — 휠로 목록을 굴린다(커서는 그대로, 보는 위치만 움직인다).
        if (_phase == Phase.Archive && e is InputEventMouseButton
            { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            float step = wheel.ButtonIndex == MouseButton.WheelUp ? -ArchiveWheelStep : ArchiveWheelStep;
            if (AchievementArchiveView.Instance?.ScrollBy(step) == true) Sfx.Instance?.Play("tick", -22f);
            GetViewport().SetInputAsHandled();
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
            // 기록실 — 아래쪽 [뒤로] 만 누를 수 있다.
            if (_phase == Phase.Archive)
            {
                if (ArchiveItemUnderMouse() == "back") { CloseArchive(); GetViewport().SetInputAsHandled(); }
                return;
            }

            // 근무 유형 선택 — 세 칸 중 가리킨 것을 확정한다(책상 장비는 먹지 않는다).
            if (_phase == Phase.ModeSelect)
            {
                if (!string.IsNullOrEmpty(TerminalItemUnderMouse()))
                {
                    ConfirmMode();
                    GetViewport().SetInputAsHandled();
                }
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
        if (_phase is not (Phase.Menu or Phase.Archive or Phase.ModeSelect)) return;
        if (_camera == null) _camera = GetViewport().GetCamera3D();
        if (_camera == null) return;

        // 근무 유형 선택 — 세 칸 위에 마우스를 올리면 커서가 따라간다.
        // 책상 장비 호버는 쉰다(여기서 전화기를 눌러 기록실이 열리면 안 된다).
        if (_phase == Phase.ModeSelect)
        {
            Vector2 mp = GetViewport().GetMousePosition();
            if (!mp.IsEqualApprox(_lastPolled)) { _lastPolled = mp; _mouse = mp; }
            string over = TerminalItemUnderMouse();
            if (!string.IsNullOrEmpty(over) && TitleTerminalView.Instance.HoverId(over))
                Sfx.Instance?.Play("tick", -16f);
            return;
        }

        // 기록실 — 목록 위에 마우스를 올리면 그 줄로 커서가 간다. 책상 장비 호버는 쉰다.
        if (_phase == Phase.Archive)
        {
            Vector2 p = GetViewport().GetMousePosition();
            if (!p.IsEqualApprox(_lastPolled)) { _lastPolled = p; _mouse = p; }
            var view = AchievementArchiveView.Instance;
            if (view != null)
            {
                Vector3 ao = _camera.ProjectRayOrigin(_mouse);
                Vector3 ad = _camera.ProjectRayNormal(_mouse);
                if (TryCanvasPos("01", ao, ad, out Vector2 lpos) && view.HoverAt(lpos))
                    Sfx.Instance?.Play("tick", -20f);
            }
            return;
        }

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

        // 복구하지 못한 두 엔딩(Bad · Late)은 같은 어두운 방을 쓴다 — 경보 소리만 다르다.
        if (!EndingState.Recovered(kind))
        {
            ControlRoom3DHorror.ExternalLightingOverride = true;
            _ctl.SetScreenTint(new Color(1.3f, 0.30f, 0.26f));
            var red = new Color(1f, 0.22f, 0.18f);
            if (_m1 != null) _m1.LightColor = red;
            if (_m2 != null) _m2.LightColor = red;
            if (_deskFill != null) { _deskFill.LightColor = new Color(0.8f, 0.35f, 0.32f); _deskFill.LightEnergy = _deskE * 0.6f; }
            if (_emergency != null) { _emergency.Visible = true; _emergency.LightColor = new Color(0.95f, 0.1f, 0.08f); }
            if (kind == EndingState.Kind.Late)
            {
                // 영구 봉쇄 — 붉은 경보가 아니라 흐린 주황 비상등 하나만 남아 있다.
                _ctl.SetScreenTint(new Color(1.0f, 0.86f, 0.74f));
                if (_emergency != null) _emergency.LightColor = new Color(1f, 0.72f, 0.45f);
            }
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
        if (!_endingLook) return;

        // LOOSE — 방은 복구된 그대로 밝다. 20초에 한 번, CRT 가 0.2초 어긋난다.
        if (EndingState.Last == EndingState.Kind.Loose)
        {
            _looseGlitch -= delta;
            if (_looseGlitch <= 0)
            {
                _looseGlitch = _rng.RandfRange(17f, 23f);
                _looseGlitchUntil = 0.2;
                _ctl?.SetScreenNoise(0.42f);
                Sfx.Instance?.Play("noise", -22f);
            }
            if (_looseGlitchUntil > 0)
            {
                _looseGlitchUntil -= delta;
                if (_looseGlitchUntil <= 0) _ctl?.SetScreenNoise(EndingScreenNoise);
            }
            return;
        }

        if (EndingState.Last != EndingState.Kind.Bad) return;
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

    // 기록실도 같은 왼쪽 CRT 캔버스를 쓴다 — 좌표를 넘겨 무엇을 가리켰는지 물어본다.
    private string ArchiveItemUnderMouse()
    {
        var view = AchievementArchiveView.Instance;
        if (_camera == null || view == null) return "";
        Vector3 o = _camera.ProjectRayOrigin(_mouse);
        Vector3 d = _camera.ProjectRayNormal(_mouse);
        return TryCanvasPos("01", o, d, out Vector2 p) ? view.ItemAt(p) : "";
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
