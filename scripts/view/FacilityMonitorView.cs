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
    // ControlRoom3DController.MonitorCanvasSize와 동일한 모니터 1 논리 좌표계.
    // 확인 창은 이 크기에 직접 배치해 부모 앵커가 아직 계산되지 않은 프레임에도 흔들리지 않는다.
    private static readonly Vector2 MonitorCanvas = new(800f, 600f);
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
    private TextureRect _face;
    // 인스펙터 얼굴 썸네일 한 변(1:1).
    private const float FaceSize = 72f;
    private Button _isolateBtn;
    private Label _notice;
    private double _noticeUntil;
    private Control _roomDots;
    private readonly System.Collections.Generic.List<Color> _roomDotColors = new();

    // 위 두 블록(지도 · 설명) 높이와 아래 한 줄 알림 높이.
    private const float BodyHeight = 448f;
    private const float NoticeHeight = 40f;
    private const float EmpDotRadius = 10f;   // FacilityMinimap 의 직원 아이콘과 같은 크기
    private Control _endShiftConfirmation;

    private string _selRoom = "";
    private string _selEmp = "";
    private double _alertUntil;

    public static FacilityMonitorView Instance { get; private set; }
    public string SelectedEmployeeId => _selEmp;
    public string SelectedRoomId => _selRoom;
    public event System.Action EndShiftRequested;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg, LayoutPreset.FullRect));
        BuildHeader();
        BuildBody();
        BuildLog();
        BuildEndShiftConfirmation();

        if (EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged += OnLog;
        }
        FacilityAlertHud.Noticed += OnNotice;
    }

    public override void _ExitTree()
    {
        if (EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged -= OnLog;
        }
        FacilityAlertHud.Noticed -= OnNotice;
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
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
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
        _clock.Position = new Vector2(420, 8);
        _clock.Size = new Vector2(262, 28);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        bar.AddChild(_clock);

        _endBtn = MonitorUi.Button("근무 종료", Amber, _font, OnEndShiftPressed, ViewFont.S(14));
        _endBtn.Position = new Vector2(690, 6);
        _endBtn.Size = new Vector2(104, 32);
        _endBtn.TooltipText = "필수 업무를 모두 끝내야 누를 수 있습니다.";
        bar.AddChild(_endBtn);

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
        var mapPanel = new Panel { Position = new Vector2(8, 96), Size = new Vector2(452, BodyHeight) };
        mapPanel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(mapPanel);

        var mapHead = MakeLabel(" FACILITY MAP", 12, Dim);
        mapHead.Position = new Vector2(6, 4);
        mapPanel.AddChild(mapHead);

        _minimap = new FacilityMinimap { Position = new Vector2(10, 24), Size = new Vector2(432, BodyHeight - 32f) };
        _minimap.OnRoomSelected = SelectRoom;
        _minimap.OnEmployeeSelected = SelectEmployee;
        mapPanel.AddChild(_minimap);

        var insPanel = new Panel { Position = new Vector2(468, 96), Size = new Vector2(324, BodyHeight) };
        insPanel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(insPanel);

        var insHead = MakeLabel(" SELECTED", 15, Amber);
        insHead.Position = new Vector2(8, 6);
        insPanel.AddChild(insHead);

        // 직원을 고르면 설명 위에 얼굴(스탠딩 원화에서 머리만 잘라낸 FacePortrait)을 1:1 로 띄운다.
        // 방을 고르면 숨는다 — 그때는 설명이 이 자리까지 올라와 쓰던 높이를 그대로 쓴다.
        _face = new TextureRect
        {
            Position = new Vector2(10, 30),
            Size = new Vector2(FaceSize, FaceSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        insPanel.AddChild(_face);

        _inspector = new RichTextLabel
        {
            Position = new Vector2(10, 30),
            Size = new Vector2(304, 280),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _inspector.AddThemeFontOverride("normal_font", _font);
        _inspector.AddThemeFontSizeOverride("normal_font_size", ViewFont.S(19));
        _inspector.AddThemeFontOverride("bold_font", new FontVariation { BaseFont = _font, VariationEmbolden = 0.9f });
        _inspector.AddThemeColorOverride("default_color", Ink);
        insPanel.AddChild(_inspector);

        // 작업실을 고르면 "직원 :" 줄 옆에 미니맵과 같은 동그란 직원 아이콘만 늘어놓는다.
        _roomDots = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false, Size = new Vector2(300f, 30f) };
        _roomDots.Draw += DrawRoomDots;
        insPanel.AddChild(_roomDots);

        _isolateBtn = new Button { Position = new Vector2(10, BodyHeight - 44f), Size = new Vector2(304, 36), Text = "격리", Visible = false };
        _isolateBtn.AddThemeFontSizeOverride("font_size", ViewFont.S(15));
        _isolateBtn.Pressed += OnIsolatePressed;
        insPanel.AddChild(_isolateBtn);
    }

    // 아래 블록 = 한 줄 알림. 메인 화면 왼쪽 아래 알림(FacilityAlertHud)이 뜰 때만 그 한 줄을 띄운다.
    // (전체 기록은 L 키 시설 로그가 맡는다.)
    private void BuildLog()
    {
        var panel = new Panel { Position = new Vector2(8, 96 + BodyHeight + 6), Size = new Vector2(784, NoticeHeight) };
        panel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.05f, 0.08f, 0.07f)));
        AddChild(panel);

        _notice = MakeLabel("", 17, Ink);
        _notice.Position = new Vector2(12, 0);
        _notice.Size = new Vector2(760, NoticeHeight);
        _notice.VerticalAlignment = VerticalAlignment.Center;
        _notice.ClipText = true;
        panel.AddChild(_notice);
    }

    private void OnNotice(string text, NoticeLevel level)
    {
        if (_notice == null) return;
        _notice.Text = text;
        _notice.AddThemeColorOverride("font_color", level switch
        {
            NoticeLevel.Critical => Alert,
            NoticeLevel.Warning => Amber,
            _ => Ink,
        });
        _noticeUntil = Time.GetTicksMsec() / 1000.0 + (level == NoticeLevel.Critical ? 6.5 : 4.0);
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

    private Button _endBtn;

    private void OnEndShiftPressed()
    {
        // 필수 업무를 끝내기 전에는 근무를 끝낼 수 없다(오늘의 업무 창과 같은 규칙).
        if (!DayObjectives.CanEndShift) return;

        float limit = DayObjectives.MaxShiftSeconds;
        float elapsed = GameState.Instance?.DayTimeSeconds ?? 0f;
        if (elapsed < limit)
        {
            _endShiftConfirmation.Visible = true;
            return;
        }

        EndShiftRequested?.Invoke();
    }

    // 근무 종료 확인은 전역 UI가 아니라 이 Control(모니터 1의 SubViewport) 안에 그린다.
    private void BuildEndShiftConfirmation()
    {
        _endShiftConfirmation = new Control
        {
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
            Position = Vector2.Zero,
            Size = MonitorCanvas,
        };
        AddChild(_endShiftConfirmation);

        var shade = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.78f),
            MouseFilter = MouseFilterEnum.Stop,
            Position = Vector2.Zero,
            Size = MonitorCanvas,
        };
        _endShiftConfirmation.AddChild(shade);

        var panel = new Panel
        {
            // 800×600 모니터 화면 가운데의 500×220 패널.
            Position = new Vector2(150f, 190f),
            Size = new Vector2(500f, 220f),
        };
        panel.AddThemeStyleboxOverride("panel", Panelbox(new Color(0.055f, 0.085f, 0.075f)));
        _endShiftConfirmation.AddChild(panel);

        var title = MakeLabel("근무 종료 확인", 22, Amber);
        title.Position = new Vector2(20, 16);
        title.Size = new Vector2(460, 30);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(title);

        var message = MakeLabel("정말 근무를 종료하시겠습니까?\n근무를 종료하여 발생한 불이익은 책임지지 않습니다.", 17, Ink);
        message.Position = new Vector2(20, 56);
        message.Size = new Vector2(460, 62);
        message.HorizontalAlignment = HorizontalAlignment.Center;
        message.VerticalAlignment = VerticalAlignment.Center;
        panel.AddChild(message);

        var no = MonitorUi.Button("아니오", Dim, _font, () => _endShiftConfirmation.Visible = false, ViewFont.S(16));
        no.Position = new Vector2(125, 158);
        no.Size = new Vector2(110, 40);
        panel.AddChild(no);

        var yes = MonitorUi.Button("예", Amber, _font, () =>
        {
            _endShiftConfirmation.Visible = false;
            EndShiftRequested?.Invoke();
        }, 16);
        yes.Position = new Vector2(265, 158);
        yes.Size = new Vector2(110, 40);
        panel.AddChild(yes);
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
        string clock = ShiftClock(GameState.Instance?.DayTimeSeconds ?? 0f);
        if (_clock.Text != clock) _clock.Text = clock;

        // 필수 업무를 끝내기 전에는 근무를 끝낼 수 없다는 걸 버튼 모양으로 보여준다.
        if (_endBtn != null)
        {
            bool can = DayObjectives.CanEndShift;
            if (_endBtn.Disabled == can) _endBtn.Disabled = !can;
        }
        UpdateProtocol();
        UpdateInspector();
        if (_notice != null && _notice.Text != "" && Time.GetTicksMsec() / 1000.0 > _noticeUntil) _notice.Text = "";

        if (Time.GetTicksMsec() / 1000.0 > _alertUntil)
            _alertLine.Text = "";
        else
            _alertLine.Modulate = new Color(1, 1, 1, 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.GetTicksMsec() / 120f)));
    }

    // 성능: RichTextLabel 에 Text 를 대입하면 내용이 같아도 BBCode 를 다시 파싱하고
    // 레이아웃을 다시 잡는다. 매 프레임 그 비용을 내지 않도록 바뀔 때만 대입한다.
    private string _inspectorCache;
    private void SetInspector(string text)
    {
        if (_inspectorCache == text) return;
        _inspectorCache = text;
        _inspector.Text = text;
    }

    // 얼굴을 띄우면 설명 글이 그 아래에서 시작하도록 내려준다.
    private Texture2D _faceCache;
    private void SetFace(Texture2D tex)
    {
        if (_faceCache == tex) return;
        _faceCache = tex;
        _face.Texture = tex;
        _face.Visible = tex != null;

        // 방을 고르면 격리 버튼이 숨으므로 그 자리까지 설명이 내려올 수 있다.
        // (직원을 고르면 y 328 의 격리 버튼 위에서 끊어야 한다.)
        float top = tex != null ? 30f + FaceSize + 6f : 30f;
        float bottom = tex != null ? BodyHeight - 50f : BodyHeight - 10f;
        _inspector.Position = new Vector2(10, top);
        _inspector.Size = new Vector2(304, bottom - top);
    }

    private void UpdateProtocol()
    {
        var sb = new StringBuilder("TODAY'S PROTOCOL   ");
        var taboos = TabooRuleSystem.Instance?.GetActiveTaboos().ToList();
        if (taboos == null || taboos.Count == 0) sb.Append("—");
        else sb.Append(string.Join("    ", taboos.Select(t => "⚠ " + t.Description)));
        string protocol = sb.ToString();
        if (_protocol.Text != protocol) _protocol.Text = protocol;
    }

    private void UpdateInspector()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) { SetFace(null); SetInspector(""); return; }

        if (!string.IsNullOrEmpty(_selEmp))
        {
            var def = sim.GetEmployeeDef(_selEmp);
            var st = sim.GetEmployeeState(_selEmp);
            if (def == null || st == null) { SetFace(null); SetInspector("—"); _isolateBtn.Visible = false; return; }

            string room = sim.GetRoomDef(st.CurrentRoomId)?.DisplayName ?? "—";
            var task = sim.GetActiveTaskForRoom(st.CurrentRoomId);
            string status = !st.Alive ? "[color=#ff5555]활동 중단[/color]"
                : st.Isolated ? "[color=#dd88dd]격리됨[/color]"
                : st.Incapacitated ? "[color=#ff5555]기절 — 의무실에서 회복 중[/color]"
                : st.IsMoving ? "이동 중" : "정상";

            string doing = st.IsMoving ? "이동 중" : task?.DisplayName ?? "대기";
            bool abnormal = !st.Alive || st.Isolated || st.Incapacitated;

            // 능력치/스트레스가 잠긴 날에는 그 줄 자체를 싣지 않고,
            // 배치 판단에 실제로 쓰는 "오늘의 기분"을 대신 보여준다.
            string detail;
            if (DayFeatures.StatsEnabled || DayFeatures.StressEnabled)
            {
                var sb = new System.Text.StringBuilder();
                if (DayFeatures.StatsEnabled)
                {
                    sb.Append($"기술 {def.Tech} · 작업 {Mathf.RoundToInt(sim.TechWorkMultiplier(_selEmp) * 100f)}%\n");
                    sb.Append($"담력 {def.Courage} · 스트레스 {Mathf.RoundToInt(sim.CourageStressMultiplier(_selEmp) * 100f)}%\n");
                    sb.Append($"관찰 {def.Observation} · 단서 {Mathf.RoundToInt(sim.ObservationClueChance(_selEmp) * 100f)}%\n\n");
                }
                if (DayFeatures.StressEnabled)
                {
                    // 스트레스는 1~50 구간제. 구간 이름과 그 구간의 업무 속도를 같이 보여준다.
                    string band = sim.StressBandName(st);
                    string bandColor = band switch
                    {
                        "기절" => "#ff5555",
                        "위험" => "#ff9933",
                        "주의" => "#dddd55",
                        _ => "#88cc88",
                    };
                    float stressMax = Config.Instance?.Data?.StressMax ?? 50f;
                    sb.Append($"스트레스 {st.Stress:0} / {stressMax:0} · [color={bandColor}]{band}[/color]" +
                              $" · 작업 {Mathf.RoundToInt(sim.StressWorkRate(st) * 100f)}%");
                }
                detail = sb.ToString().TrimEnd('\n');
            }
            else
            {
                string mood = sim.GetDailyMood(_selEmp);
                // 경영 리워크: 특성(Trait)은 싣지 않는다.
                detail = $"오늘의 기분 · [color=#ffd479]{(string.IsNullOrEmpty(mood) ? "—" : mood)}[/color]";
            }

            SetFace(def.FacePortrait);
            SetInspector(
                $"[color=#ffc040]EMPLOYEE[/color]\n[font_size=23]{def.Codename}[/font_size]\n" +
                $"[color=#8a99a8]{room} · {doing}[/color]\n\n" +
                detail +
                (abnormal ? $"\n{status}" : ""));

            _isolateBtn.Visible = st.Alive;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
            HideRoomDots();
            return;
        }

        _isolateBtn.Visible = false;

        if (!string.IsNullOrEmpty(_selRoom))
        {
            var def = sim.GetRoomDef(_selRoom);
            var state = sim.GetRoomState(_selRoom);
            if (def == null || state == null) { SetFace(null); SetInspector("—"); HideRoomDots(); return; }

            var st = sim.GetPrimarySpawnedTask(_selRoom);
            var workers = state.OccupantEmployeeIds;
            bool broken = RoomStatusText.GetDangerTier(_selRoom) == RoomDangerTier.Failure;
            // 상태는 네 가지뿐 — 작업중 / 수리중 / 고장 / 비어있음.
            string statusLabel, statusCol;
            if (broken && st is { IsRepair: true } && workers.Count > 0) { statusLabel = "수리중"; statusCol = "#ffc040"; }
            else if (broken) { statusLabel = "고장"; statusCol = "#ff4444"; }
            else if (workers.Count == 0) { statusLabel = "비어있음"; statusCol = "#8a99a8"; }
            else { statusLabel = "작업중"; statusCol = "#9ee6c4"; }

            // 진행 = 10칸 블록. 상시 업무는 한 바퀴 차면 다시 비워지며 반복된다.
            int filled = st == null ? 0 : Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp(st.Ratio, 0f, 1f) * 10f + 0.0001f), 0, 10);
            string bar = new string('■', filled) + new string('□', 10 - filled);

            SetFace(null);            // 방 선택 — 얼굴 없음
            SetInspector(
                $"[font_size={ViewFont.S(27)}][b]{def.DisplayName}[/b][/font_size]\n" +
                "[color=#3f5f55]" + new string('─', 56) + "[/color]\n" +
                $"상태 : [color={statusCol}]{statusLabel}[/color]\n" +
                $"진행 : [color=#9ee6c4]{bar}[/color]\n" +
                "직원 :");
            ShowRoomDots(sim, workers);
            return;
        }

        HideRoomDots();
        SetFace(null);
        SetInspector("[color=#556]지도에서 방 또는 직원을 선택하세요.[/color]");
    }

    // "직원 :" 줄 옆 동그란 아이콘(미니맵과 같은 모양 · 색). 나중에 미니맵 아이콘 그림이 생기면 여기도 같이 바꾼다.
    private void ShowRoomDots(FacilitySimulation sim, System.Collections.Generic.IEnumerable<string> ids)
    {
        _roomDotColors.Clear();
        foreach (var id in ids)
        {
            var d = sim.GetEmployeeDef(id);
            var e = sim.GetEmployeeState(id);
            if (d != null) _roomDotColors.Add(e?.Alive == false ? new Color(0.35f, 0.35f, 0.35f) : d.IconColor);
        }
        // "직원 :" 은 설명의 마지막 줄 — 그 줄 높이 가운데, 글자 바로 오른쪽에 붙인다.
        int last = Mathf.Max(0, _inspector.GetLineCount() - 1);
        float lineY = _inspector.GetLineOffset(last);
        int fs = ViewFont.S(19);
        float labelW = _font.GetStringSize("직원 : ", HorizontalAlignment.Left, -1, fs).X;
        _roomDots.Position = _inspector.Position + new Vector2(labelW + 4f, lineY + fs * 0.62f - _roomDots.Size.Y / 2f);
        _roomDots.Visible = true;
        _roomDots.QueueRedraw();
    }

    private void HideRoomDots()
    {
        if (_roomDots != null && _roomDots.Visible) _roomDots.Visible = false;
    }

    private void DrawRoomDots()
    {
        float x = EmpDotRadius + 2f, y = _roomDots.Size.Y / 2f;
        if (_roomDotColors.Count == 0)
        {
            _roomDots.DrawString(_font, new Vector2(0f, y + 6f), "—", HorizontalAlignment.Left, -1, ViewFont.S(19), Dim);
            return;
        }
        foreach (var c in _roomDotColors)
        {
            _roomDots.DrawCircle(new Vector2(x, y), EmpDotRadius, c);
            _roomDots.DrawCircle(new Vector2(x, y), EmpDotRadius, new Color(0f, 0f, 0f, 0.7f), false, 1.6f);
            x += EmpDotRadius * 2f + 6f;
        }
    }

    // 고장 위험 / 고장 상태 블록. 위험도 고장도 없으면 아무것도 붙이지 않는다.
    private static string BuildRiskBlock(string roomId, SpawnedTask task, RoomDangerTier tier)
    {
        if (tier == RoomDangerTier.Failure)
        {
            string cause = RoomStatusText.GetFailureCause(roomId);
            var sb = new StringBuilder("\n\n[color=#ff4444]🚨 고장 발생[/color]");
            if (!string.IsNullOrEmpty(cause)) sb.Append($"\n[color=#ff8a8a]{cause} — 수리 전까지 복구되지 않습니다.[/color]");
            if (task is { IsRepair: true })
                sb.Append($"\n[color=#ff8a8a]수리 진행도 : {Mathf.Clamp(task.Ratio, 0f, 1f) * 100f:0}%[/color]");
            return sb.ToString();
        }

        // 아직 고장 전 — 제한시간이 도는 업무가 있으면 남은 시간을 위험 경고로 보여준다.
        if (task is { Recurring: false, Status: SpawnedTaskStatus.Active } && task.TimeLimitSeconds < float.MaxValue)
        {
            string col = tier == RoomDangerTier.Unstable ? "#ff9933" : "#dddd55";
            string head = tier == RoomDangerTier.Unstable ? "❗ 고장 임박" : "⚠ 고장 위험";
            return $"\n\n[color={col}]{head} — {Clock(task.Remaining)} 뒤 고장[/color]";
        }
        return "";
    }

    private void OnLog()
    {
        var e = EventLog.Instance?.GetAllEntries();
        if (e is { Count: > 0 })
        {
            var last = e[^1];
            if (last.EventType is LogEventType.TabooViolation or LogEventType.TaskFailed or LogEventType.PowerOutage
                or LogEventType.CctvDisconnect or LogEventType.Sabotage or LogEventType.Death)
            {
                _alertLine.Text = "!! " + last.Description;
                _alertUntil = Time.GetTicksMsec() / 1000.0 + 6.0;
            }
        }
    }

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:0}:{t % 60:00}";
    }

    // 플레이어에게 보이는 시각 — 한글 시간대 표기(밤/새벽). 환산 · 표기는 DialogueClock 한 곳에서만.
    private static string ShiftClock(float t) => NSP.Dialogue.DialogueClock.Text(t);
}
