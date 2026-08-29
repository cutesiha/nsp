using System.Linq;
using System.Text;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// 왼쪽 CRT 전용 프로그램. 기존 2D MainScene 팝업/UI를 하나도 재사용하지 않는다.
// FacilitySimulation / GameState / EventLog / TabooRuleSystem 를 구독해 표시만 하고,
// 배치/격리 명령만 되돌려 보낸다. 방 선택 = SurveillanceTarget 설정 → 오른쪽 CCTV 연동.
public partial class FacilityMonitorView : Control
{
    private static readonly Color Bg = new(0.035f, 0.05f, 0.045f);
    private static readonly Color Ink = new(0.7f, 0.9f, 0.78f);
    private static readonly Color Dim = new(0.45f, 0.6f, 0.55f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);
    private static readonly Color Alert = new(1f, 0.35f, 0.28f);

    private Font _font;
    private FacilityMinimap _minimap;
    private Label _clock;
    private Label _protocol;
    private Label _alertLine;
    private RichTextLabel _inspector;
    private Button _isolateBtn;
    private RichTextLabel _log;

    private string _selRoom = "";
    private string _selEmp = "";
    private double _alertUntil;

    public static FacilityMonitorView Instance { get; private set; }
    public string SelectedEmployeeId => _selEmp;
    public string SelectedRoomId => _selRoom;

    public override void _Ready()
    {
        Instance = this;
        _font = GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg, LayoutPreset.FullRect));
        BuildHeader();
        BuildBody();
        BuildLog();

        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged += OnLog;
        RebuildLog();
    }

    public override void _ExitTree()
    {
        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnLog;
        if (Instance == this) Instance = null;
    }

    // --- build -------------------------------------------------------

    private static ColorRect Rect(Color c, LayoutPreset preset)
    {
        var r = new ColorRect { Color = c, MouseFilter = MouseFilterEnum.Ignore };
        r.SetAnchorsPreset(preset);
        return r;
    }

    private Label MakeLabel(string text, int size, Color col)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private void BuildHeader()
    {
        var bar = new Panel { Position = new Vector2(0, 0), Size = new Vector2(800, 44) };
        bar.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.07f, 0.11f, 0.10f)));
        AddChild(bar);

        var title = MakeLabel("  FACILITY MONITOR", 20, Ink);
        title.Position = new Vector2(0, 8);
        bar.AddChild(title);

        _clock = MakeLabel("--:--", 20, Amber);
        _clock.Position = new Vector2(640, 8);
        _clock.Size = new Vector2(150, 28);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        bar.AddChild(_clock);

        _alertLine = MakeLabel("", 15, Alert);
        _alertLine.Position = new Vector2(12, 46);
        _alertLine.Size = new Vector2(776, 20);
        AddChild(_alertLine);

        _protocol = MakeLabel("TODAY'S PROTOCOL", 13, Amber);
        _protocol.Position = new Vector2(12, 66);
        _protocol.Size = new Vector2(776, 24);
        _protocol.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_protocol);
    }

    private void BuildBody()
    {
        var mapPanel = new Panel { Position = new Vector2(8, 96), Size = new Vector2(452, 372) };
        mapPanel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(mapPanel);

        var mapHead = MakeLabel(" FACILITY MAP", 12, Dim);
        mapHead.Position = new Vector2(6, 4);
        mapPanel.AddChild(mapHead);

        _minimap = new FacilityMinimap { Position = new Vector2(10, 24), Size = new Vector2(432, 340) };
        _minimap.OnRoomSelected = SelectRoom;
        _minimap.OnEmployeeSelected = SelectEmployee;
        mapPanel.AddChild(_minimap);

        var insPanel = new Panel { Position = new Vector2(468, 96), Size = new Vector2(324, 372) };
        insPanel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(insPanel);

        var insHead = MakeLabel(" SELECTED", 13, Amber);
        insHead.Position = new Vector2(8, 6);
        insPanel.AddChild(insHead);

        _inspector = new RichTextLabel
        {
            Position = new Vector2(10, 30),
            Size = new Vector2(304, 280),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _inspector.AddThemeFontOverride("normal_font", _font);
        _inspector.AddThemeFontSizeOverride("normal_font_size", 14);
        _inspector.AddThemeColorOverride("default_color", Ink);
        insPanel.AddChild(_inspector);

        _isolateBtn = new Button { Position = new Vector2(10, 320), Size = new Vector2(304, 38), Text = "격리", Visible = false };
        _isolateBtn.AddThemeFontSizeOverride("font_size", 15);
        _isolateBtn.Pressed += OnIsolatePressed;
        insPanel.AddChild(_isolateBtn);
    }

    private void BuildLog()
    {
        var panel = new Panel { Position = new Vector2(8, 476), Size = new Vector2(784, 118) };
        panel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(panel);

        var head = MakeLabel(" LOG", 12, Dim);
        head.Position = new Vector2(6, 2);
        panel.AddChild(head);

        _log = new RichTextLabel
        {
            Position = new Vector2(10, 20),
            Size = new Vector2(764, 92),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _log.AddThemeFontOverride("normal_font", _font);
        _log.AddThemeFontSizeOverride("normal_font_size", 12);
        _log.AddThemeColorOverride("default_color", Dim);
        panel.AddChild(_log);
    }

    private StyleBoxFlat Panelbox(Color bg)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(0.25f, 0.4f, 0.35f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
    }

    // --- selection --------------------------------------------------

    private void SelectRoom(string roomId)
    {
        _selRoom = roomId;
        _selEmp = "";
        _minimap.SelectedRoomId = roomId;
        _minimap.SelectedEmployeeId = "";
        FacilitySimulation.Instance?.SetSurveillanceTarget(roomId);
    }

    private void SelectEmployee(string empId)
    {
        _selEmp = empId;
        var st = FacilitySimulation.Instance?.GetEmployeeState(empId);
        _selRoom = st?.CurrentRoomId ?? "";
        _minimap.SelectedEmployeeId = empId;
        _minimap.SelectedRoomId = _selRoom;
        if (!string.IsNullOrEmpty(_selRoom))
            FacilitySimulation.Instance?.SetSurveillanceTarget(_selRoom);
    }

    private void OnIsolatePressed()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(_selEmp)) return;
        var st = sim.GetEmployeeState(_selEmp);
        if (st == null) return;
        if (st.Isolated) sim.CancelIsolation(_selEmp);
        else sim.IsolateEmployee(_selEmp);
    }

    // --- per-frame -------------------------------------------------

    public override void _Process(double delta)
    {
        _clock.Text = ShiftClock(GameState.Instance?.DayTimeSeconds ?? 0f);
        UpdateProtocol();
        UpdateInspector();

        if (Time.GetTicksMsec() / 1000.0 > _alertUntil)
            _alertLine.Text = "";
        else
            _alertLine.Modulate = new Color(1, 1, 1, 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.GetTicksMsec() / 120f)));
    }

    private void UpdateProtocol()
    {
        var sb = new StringBuilder("TODAY'S PROTOCOL   ");
        var taboos = TabooRuleSystem.Instance?.GetActiveTaboos().ToList();
        if (taboos == null || taboos.Count == 0) sb.Append("—");
        else sb.Append(string.Join("    ", taboos.Select(t => "⚠ " + t.Description)));
        _protocol.Text = sb.ToString();
    }

    private void UpdateInspector()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) { _inspector.Text = ""; return; }

        if (!string.IsNullOrEmpty(_selEmp))
        {
            var def = sim.GetEmployeeDef(_selEmp);
            var st = sim.GetEmployeeState(_selEmp);
            if (def == null || st == null) { _inspector.Text = "—"; _isolateBtn.Visible = false; return; }

            string room = sim.GetRoomDef(st.CurrentRoomId)?.DisplayName ?? "—";
            var task = sim.GetActiveTaskForRoom(st.CurrentRoomId);
            string status = !st.Alive ? "[color=#ff5555]활동 중단[/color]"
                : st.Isolated ? "[color=#dd88dd]격리됨[/color]"
                : st.IsMoving ? "이동 중"
                : st.Stress > 70 ? "[color=#ffaa44]과부하[/color]" : "정상";

            _inspector.Text =
                $"[color=#ffc040]EMPLOYEE[/color]\n[font_size=18]{def.Codename}[/font_size]\n\n" +
                $"현재 위치 : {room}\n" +
                $"현재 작업 : {(st.IsMoving ? "이동" : task?.DisplayName ?? "대기")}\n" +
                $"기술 {def.Tech}  담력 {def.Courage}  관찰 {def.Observation}\n" +
                $"스트레스 : {st.Stress:0}%\n" +
                $"상태 : {status}";

            _isolateBtn.Visible = st.Alive;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
            return;
        }

        _isolateBtn.Visible = false;

        if (!string.IsNullOrEmpty(_selRoom))
        {
            var def = sim.GetRoomDef(_selRoom);
            var state = sim.GetRoomState(_selRoom);
            if (def == null || state == null) { _inspector.Text = "—"; return; }

            var st = sim.GetPrimarySpawnedTask(_selRoom);
            var tier = RoomStatusText.GetDangerTier(_selRoom);
            string dangerTxt = tier switch
            {
                RoomDangerTier.Failure => "[color=#ff4444]FAILURE[/color]",
                RoomDangerTier.Unstable => "[color=#ff9933]UNSTABLE[/color]",
                RoomDangerTier.Delayed => "[color=#dddd55]DELAYED[/color]",
                _ => "정상",
            };
            var occ = state.OccupantEmployeeIds
                .Select(id => sim.GetEmployeeDef(id)?.Codename).Where(c => c != null);

            var sb = new StringBuilder($"[color=#ffc040]ROOM[/color]\n[font_size=18]{def.DisplayName}[/font_size]\n\n");
            if (st != null)
            {
                var tdef = sim.GetTaskDef(st.TaskId);
                sb.Append($"현재 작업 : {tdef?.DisplayName ?? st.TaskId}\n");
                if (!st.Recurring && st.Status == SpawnedTaskStatus.Active)
                    sb.Append($"남은 시간 : {Clock(st.Remaining)}\n");
                sb.Append($"진행도 : {Mathf.Clamp(st.Ratio, 0f, 1f) * 100f:0}%\n");
            }
            else sb.Append("현재 작업 : 없음\n");
            sb.Append($"배치 직원 : {(occ.Any() ? string.Join(", ", occ) : "없음")}\n");
            sb.Append($"상태 : {dangerTxt}");
            if (state.Locked) sb.Append("  [color=#ff9933][봉쇄][/color]");
            if (TabooRuleSystem.Instance?.IsRoomAtTabooRisk(_selRoom) == true)
                sb.Append("\n[color=#ffcc33]⚠ 금기 대상 구역[/color]");
            _inspector.Text = sb.ToString();
            return;
        }

        _inspector.Text = "[color=#556]지도에서 방 또는 직원을 선택하세요.[/color]";
    }

    private void OnLog()
    {
        var e = EventLog.Instance?.GetAllEntries();
        if (e == null || e.Count == 0) return;
        var last = e[^1];
        if (last.EventType is LogEventType.TabooViolation or LogEventType.TaskFailed or LogEventType.PowerOutage
            or LogEventType.CctvDisconnect or LogEventType.Sabotage or LogEventType.Death)
        {
            _alertLine.Text = "!! " + last.Description;
            _alertUntil = Time.GetTicksMsec() / 1000.0 + 6.0;
        }
        RebuildLog();
    }

    private void RebuildLog()
    {
        var entries = EventLog.Instance?.GetAllEntries();
        if (entries == null) return;
        var sb = new StringBuilder();
        foreach (var en in entries.TakeLast(6))
            sb.AppendLine($"{Clock(en.GameTimeSeconds)}  {Strip(en.Description)}");
        _log.Text = sb.ToString();
    }

    private static string Strip(string s) => s.Replace("⚠", "").Replace("🚨", "").Trim();

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:0}:{t % 60:00}";
    }

    private static string ShiftClock(float t)
    {
        int totalMin = 22 * 60 + Mathf.FloorToInt(t * (480f / 300f));
        int h = (totalMin / 60) % 24;
        int m = totalMin % 60;
        return $"{h:00}:{m:00}";
    }
}
