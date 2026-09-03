using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// 책상 위 배치표 종이의 실제 내용. 오래된 시설 공문서 느낌(누런 아이보리/얼룩/붉은 도장) —
// 흰 A4 메뉴 종이가 아니다. 방/직원 클릭은 화면 한쪽의 작은 정보 패널만 갱신한다(큰 팝업 없음).
// 배치 데이터는 전적으로 FacilitySimulation 을 통해 변경하고, 능력치/설명은 전부 기존
// TaskDef.RequiredStat / RoomDetailCard.Descriptions / EmployeeDef 값을 그대로 읽어온다.
public partial class ScheduleBoardUI : Control
{
    public Vector2I CanvasSize = new(768, 560);
    public Action StartPressed;

    // 종이(잉크) 톤 — 흰색이 아니라 누렇게 바랜 아이보리.
    private static readonly Color Ink = new(0.18f, 0.14f, 0.09f);
    private static readonly Color InkDim = new(0.42f, 0.35f, 0.24f);
    private static readonly Color InkRed = new(0.55f, 0.14f, 0.10f);
    private static readonly Color HeadcountBlue = new(0.10f, 0.24f, 0.48f);
    private static readonly Color HeadcountBrown = new(0.38f, 0.20f, 0.07f);
    private static readonly Color SlotFill = new(0.80f, 0.75f, 0.60f, 0.55f);
    private static readonly Color SelectFill = new(0.72f, 0.58f, 0.28f, 0.55f);
    private static readonly Color DockBg = new(0.79f, 0.76f, 0.66f, 0.9f);

    private const float DocLeft = 24f, DocRight = 460f;
    private const float DockLeft = 500f, DockRight = 744f;

    private Font _serif, _body;
    private Control _form;
    private Control _info;

    private string _selectedEmp = "";   // 배치 대상으로 선택된 직원
    private string _justWrote = "";
    private string _focusRoom = "";     // 마지막으로 클릭해 정보 패널에 띄운 방
    private string _focusEmp = "";      // 마지막으로 클릭해 정보 패널에 띄운 직원
    private string _hoverRoom = "";     // 직원 선택 중 마우스가 올라가 있는 방(비교 표시용)

    // --- 수동 드래그 앤 드롭 (SubViewport 안에서 Godot 기본 DnD가 안 먹어 직접 구현) ---
    private string _dragEmp = "";
    private Vector2 _pressPos, _lastDragPos;
    private bool _dragging;
    private Control _dragPreview;
    private readonly List<RoomRow> _rows = new();

    public override void _Ready()
    {
        _serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ViewFont.Default;
        _body = ViewFont.Default;

        Size = CanvasSize;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(new PaperTexture { Size = CanvasSize, MouseFilter = MouseFilterEnum.Ignore });

        _form = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _form.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_form);

        _info = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _info.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_info);


        SetProcessInput(true);
        Rebuild();
    }

    // 카드에서 마우스를 눌러 끌기 시작 → 여기서 추적한다(root._Input 이 이후 모션/떼기를 받는다).
    private void BeginDrag(string empId, Vector2 canvasPos)
    {
        _dragEmp = empId;
        _pressPos = canvasPos;
        _lastDragPos = canvasPos;
        _dragging = false;
    }

    public override void _Input(InputEvent e)
    {
        if (string.IsNullOrEmpty(_dragEmp)) return;

        // 이 뷰는 스케일 프레임(AddScaledView) 안에 있다 → _Input 은 뷰포트(확대) 좌표로 들어온다.
        // 행/드롭 판정은 논리 좌표(_form 로컬)이므로 여기서 로컬로 변환해 좌표계를 맞춘다.
        e = MakeInputLocal(e);

        if (e is InputEventMouseMotion mm)
        {
            _lastDragPos = mm.Position;
            if (!_dragging && mm.Position.DistanceTo(_pressPos) > 6f) StartDragVisual();
            if (_dragging) UpdateDrag(mm.Position);
        }
        else if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            if (_dragging) DropAt(_lastDragPos);
            EndDrag();
        }
    }

    private void StartDragVisual()
    {
        _dragging = true;
        var sim = FacilitySimulation.Instance;
        string name = sim?.GetEmployeeDef(_dragEmp)?.Codename ?? _dragEmp;

        _dragPreview = new Panel { Size = new Vector2(132, 30), MouseFilter = MouseFilterEnum.Ignore, ZIndex = 100 };
        _dragPreview.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.84f, 0.6f, 0.97f),
            BorderColor = new Color(0.3f, 0.22f, 0.12f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        });
        var l = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        l.SetAnchorsPreset(LayoutPreset.FullRect);
        l.AddThemeFontOverride("font", _serif);
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color", new Color(0.15f, 0.11f, 0.07f));
        _dragPreview.AddChild(l);
        AddChild(_dragPreview);
    }

    private void UpdateDrag(Vector2 canvasPos)
    {
        if (_dragPreview != null) _dragPreview.Position = canvasPos - _dragPreview.Size * new Vector2(0.5f, 0.5f);
        var hit = RowAt(canvasPos);
        foreach (var r in _rows)
        {
            bool h = r == hit;
            if (r.ManualHover != h) { r.ManualHover = h; r.QueueRedraw(); }
        }
    }

    private void DropAt(Vector2 canvasPos)
    {
        var hit = RowAt(canvasPos);
        if (hit != null) TryDropAssign(_dragEmp, hit.RoomId);
    }

    private RoomRow RowAt(Vector2 canvasPos)
    {
        foreach (var r in _rows)
            if (IsInstanceValid(r) && new Rect2(r.Position, r.Size).HasPoint(canvasPos))
                return r;
        return null;
    }

    private void EndDrag()
    {
        _dragEmp = "";
        _dragging = false;
        if (_dragPreview != null) { _dragPreview.QueueFree(); _dragPreview = null; }
        foreach (var r in _rows) if (IsInstanceValid(r) && r.ManualHover) { r.ManualHover = false; r.QueueRedraw(); }
    }

    public void Rebuild()
    {
        _hoverRoom = "";
        RebuildForm();
        RefreshInfoPanel();
    }

    // --- 문서 본문(방/직원 목록, 헤더, 시작 버튼) ---------------------------

    private void RebuildForm()
    {
        if (_form == null) return;
        foreach (Node c in _form.GetChildren()) c.QueueFree();
        _rows.Clear();

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        int day = GameState.Instance?.CurrentDay ?? 1;

        AddLabel(_form, $"DOC NO. NSP-04-{day:00}   FACILITY CONTROL DEPT.", new Vector2(DocLeft, 4), 11, InkDim, _body);
        AddLabel(_form, $"DAY {day:00}", new Vector2(DocLeft, 17), 32, Ink, _serif);
        AddLabel(_form, "N I G H T   S H I F T   A S S I G N M E N T", new Vector2(DocLeft, 60), 12, InkDim, _body);

        AddLabel(_form, "오늘의 금기", new Vector2(DocLeft, 94), 16, InkRed, _serif);
        var taboos = TabooRuleSystem.Instance?.GetActiveTaboos().ToList();
        string tabooText = taboos == null || taboos.Count == 0 ? "특이사항 없음" : "⚠ " + string.Join("   ⚠ ", taboos.Select(t => t.Description));
        var tabooLbl = AddLabel(_form, tabooText, new Vector2(DocLeft, 116), 19, InkRed, _body);
        // 금기 문구는 왼쪽 단 안에서 두 줄까지 접힌다 — 오른쪽 서류받침을 침범하지 않게.
        tabooLbl.CustomMinimumSize = new Vector2(DocRight - DocLeft, 46);
        tabooLbl.Size = new Vector2(DocRight - DocLeft, 46);
        tabooLbl.ClipText = true;
        tabooLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var rooms = sim.GetRoomIds()
            .Where(id =>
            {
                var d = sim.GetRoomDef(id);
                return d != null && !d.IsRestricted && sim.GetRoomTasksInPriorityOrder(id).Count > 0;
            })
            .ToList();

        AddLabel(_form, "작업실  ·  직원 카드를 끌어다 놓거나 카드를 고른 뒤 방을 누르세요", new Vector2(DocLeft, 170), 12, InkDim, _body);

        float y = 194f;
        foreach (var roomId in rooms)
        {
            var here = sim.GetEmployeeIds()
                .Select(sim.GetEmployeeState)
                .Where(s => s != null && s.AssignedRoomId == roomId)
                .Select(s => s.EmployeeId)
                .ToList();

            var row = new RoomRow(sim.GetRoomDef(roomId), _serif, _body)
            {
                Position = new Vector2(DocLeft, y),
                Size = new Vector2(DocRight - DocLeft - 8, 32),
                RoomId = roomId,
                Occupants = here,
                CodenameOf = id => sim.GetEmployeeDef(id)?.Codename ?? id,
                JustWrote = _justWrote,
                OnNameClick = OnRoomClicked,
                OnClearOcc = OnClearOccupant,
                OnHover = OnRoomHover,
                TryDropAssign = TryDropAssign,
            };
            _form.AddChild(row);
            _rows.Add(row);
            y += 35f;
        }

        // --- 오른쪽 대기 인원(직원 카드) ---
        AddLabel(_form, "대기 인원", new Vector2(DockLeft + 12, 16), 15, InkDim, _body);
        float ey = 42f;
        foreach (var empId in sim.GetEmployeeIds())
        {
            var edef = sim.GetEmployeeDef(empId);
            var est = sim.GetEmployeeState(empId);
            if (edef == null || est == null) continue;

            var card = new EmpCard(edef, _serif, _body)
            {
                Position = new Vector2(DockLeft + 10, ey),
                Size = new Vector2(DockRight - DockLeft - 20, 48),
                EmpId = empId,
                Selected = empId == _selectedEmp,
                AssignedRoomName = string.IsNullOrEmpty(est.AssignedRoomId)
                    ? "" : sim.GetRoomDef(est.AssignedRoomId)?.DisplayName ?? "",
                OnClick = OnEmployeeClicked,
                OnPressStart = BeginDrag,
            };
            _form.AddChild(card);
            ey += 52f;
        }

        // --- 하단 상태/버튼 ---
        int total = sim.GetEmployeeIds().Count;
        int placed = sim.GetEmployeeIds().Count(id => !string.IsNullOrEmpty(sim.GetEmployeeState(id)?.AssignedRoomId));
        int missing = total - placed;
        // 배치 단계에서는 직원이 아직 방으로 이동하지 않았으므로(시뮬레이션 미가동)
        // 물리적 점유(OccupantEmployeeIds)가 아니라 배치 지정(AssignedRoomId)으로 판정한다.
        bool coreStaffed = sim.GetEmployeeIds().Any(id => sim.GetEmployeeState(id)?.AssignedRoomId == "core_room");

        string statusText = !coreStaffed
            ? "⚠ 코어실에 최소 1명의 직원을 배치해야 합니다."
            : missing > 0 ? $"{placed} / {total} 배치  ·  미배치 {missing}명" : $"{placed} / {total} 배치 완료";
        var status = AddLabel(_form, statusText,
            new Vector2(DocLeft, CanvasSize.Y - 52), 17, !coreStaffed || missing > 0 ? InkRed : Ink, _body);
        status.Size = new Vector2(360, 30);

        var start = new Button
        {
            Text = "근무 시작 ▶",
            Position = new Vector2(CanvasSize.X - 214, CanvasSize.Y - 58),
            Size = new Vector2(190, 46),
            Disabled = !coreStaffed,
        };
        StyleDoc(start, coreStaffed ? new Color(0.95f, 0.92f, 0.83f) : InkDim,
            coreStaffed ? new Color(0.14f, 0.11f, 0.07f) : new Color(0.5f, 0.46f, 0.36f, 0.4f));
        start.AddThemeFontSizeOverride("font_size", 21);
        start.Pressed += () => StartPressed?.Invoke();
        _form.AddChild(start);
    }

    // --- 정보 패널(화면 오른쪽 하단 — 방/직원 하나만, 클릭 시 내용만 교체) -----

    private void RefreshInfoPanel()
    {
        if (_info == null) return;
        foreach (Node c in _info.GetChildren()) c.QueueFree();

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        // 직원 카드 6장(42 + 6×52 = 354)이 끝난 아래로 내려 겹치지 않게 한다.
        const float px = DockLeft + 12, pw = DockRight - DockLeft - 24;
        const float py = 366f;

        if (!string.IsNullOrEmpty(_selectedEmp) && !string.IsNullOrEmpty(_hoverRoom))
        {
            DrawCompare(sim, _selectedEmp, _hoverRoom, px, py, pw);
            return;
        }

        if (!string.IsNullOrEmpty(_focusEmp))
        {
            DrawEmployeeInfo(sim, _focusEmp, px, py, pw);
            return;
        }

        if (!string.IsNullOrEmpty(_focusRoom))
        {
            DrawRoomInfo(sim, _focusRoom, px, py, pw);
            return;
        }

        var hint = AddLabel(_info, "직원 또는 작업실을\n클릭하면 정보가\n표시됩니다.", new Vector2(px, py + 8), 15, InkDim, _body);
        hint.Size = new Vector2(pw, 90);
    }

    private void DrawRoomInfo(FacilitySimulation sim, string roomId, float px, float py, float pw)
    {
        var def = sim.GetRoomDef(roomId);
        if (def == null) return;

        AddLabel(_info, def.DisplayName, new Vector2(px, py), 20, Ink, _serif);

        var stats = RoomRequiredStats(sim, roomId);
        string statLine = stats.Count == 0 ? "" : string.Join("  ·  ", stats.Select(s => $"{StatIcon(s)} {StatLabel(s)}"));
        int headcount = sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RecommendedHeadcount).DefaultIfEmpty(1).Max();
        AddLabel(_info, $"요구 능력  {statLine}", new Vector2(px, py + 32), 15, InkRed, _body);
        Color headcountColor = headcount >= 2 ? HeadcountBlue : HeadcountBrown;
        var headcountLabel = AddLabel(_info, $"권장 인원  {headcount}명", new Vector2(px, py + 56), 15, headcountColor, _body);
        headcountLabel.AddThemeColorOverride("font_outline_color", headcountColor.Darkened(0.18f));
        headcountLabel.AddThemeConstantOverride("outline_size", 1);

        string desc = FirstSentence(RoomDetailCard.Descriptions.GetValueOrDefault(roomId, ""));
        var d = AddLabel(_info, desc, new Vector2(px, py + 84), 14, Ink, _body);
        d.Size = new Vector2(pw, 84);
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    private void DrawEmployeeInfo(FacilitySimulation sim, string employeeId, float px, float py, float pw)
    {
        var def = sim.GetEmployeeDef(employeeId);
        if (def == null) return;

        if (def.FacePortrait != null)
            _info.AddChild(MakeClippedPortrait(def.FacePortrait, new Vector2(px, py), new Vector2(52, 52)));
        AddLabel(_info, def.Codename, new Vector2(px + 60, py + 4), 21, Ink, _serif);

        float sy = py + 58;
        AddLabel(_info, $"기술   {Bar(def.Tech)}  {def.Tech}", new Vector2(px, sy), 15, Ink, _body);
        AddLabel(_info, $"담력   {Bar(def.Courage)}  {def.Courage}", new Vector2(px, sy + 22), 15, Ink, _body);
        AddLabel(_info, $"관찰   {Bar(def.Observation)}  {def.Observation}", new Vector2(px, sy + 44), 15, Ink, _body);
    }

    private void DrawCompare(FacilitySimulation sim, string employeeId, string roomId, float px, float py, float pw)
    {
        var edef = sim.GetEmployeeDef(employeeId);
        var rdef = sim.GetRoomDef(roomId);
        if (edef == null || rdef == null) return;

        var stats = RoomRequiredStats(sim, roomId);
        var primary = stats.Count > 0 ? stats[0] : StatType.Tech;
        int value = edef.GetStat(primary);

        AddLabel(_info, $"{edef.Codename}  ·  {StatIcon(primary)} {StatLabel(primary)} {value}", new Vector2(px, py), 16, Ink, _body);
        AddLabel(_info, $"→ {rdef.DisplayName} 요구 능력: {StatLabel(primary)}", new Vector2(px, py + 28), 14, InkDim, _body);

        var (fitText, fitCol, fitNote) = FitTier(value);
        var l = AddLabel(_info, fitText, new Vector2(px, py + 62), 19, fitCol, _body);
        l.Size = new Vector2(pw, 28);
        AddLabel(_info, fitNote, new Vector2(px, py + 88), 13, InkDim, _body);
    }

    // --- interaction --------------------------------------------------

    // 방 이름을 클릭 — 선택된 직원이 있으면 그 방에 배치, 없으면 방 정보만 표시.
    private void OnRoomClicked(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim != null && !string.IsNullOrEmpty(_selectedEmp))
        {
            AssignEmp(_selectedEmp, roomId);
            _selectedEmp = "";
            _focusRoom = roomId;
            _focusEmp = "";
            _hoverRoom = "";
            RebuildForm();
            RefreshInfoPanel();
            return;
        }
        _focusRoom = roomId;
        _focusEmp = "";
        RefreshInfoPanel();
    }

    private void OnEmployeeClicked(string employeeId)
    {
        _selectedEmp = _selectedEmp == employeeId ? "" : employeeId;
        _focusEmp = employeeId;
        _focusRoom = "";
        RebuildForm();
        RefreshInfoPanel();
    }

    private void OnRoomHover(string roomId, bool entering)
    {
        if (string.IsNullOrEmpty(_selectedEmp)) return;
        if (entering) _hoverRoom = roomId;
        else if (_hoverRoom == roomId) _hoverRoom = "";
        RefreshInfoPanel();
    }

    private void OnClearOccupant(string occupantId)
    {
        FacilitySimulation.Instance?.ClearAssignment(occupantId);
        _justWrote = "";
        RebuildForm();
        RefreshInfoPanel();
    }

    // 드래그 앤 드롭으로 직원 카드를 방에 놓음.
    private bool TryDropAssign(string employeeId, string roomId)
    {
        bool ok = AssignEmp(employeeId, roomId);
        _selectedEmp = "";
        _focusRoom = roomId;
        _focusEmp = "";
        _hoverRoom = "";
        RebuildForm();
        RefreshInfoPanel();
        return ok;
    }

    private bool AssignEmp(string employeeId, string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return false;
        var st = sim.GetEmployeeState(employeeId);
        if (st != null && st.AssignedRoomId == roomId) return true; // 이미 그 방
        sim.ClearAssignment(employeeId);
        if (sim.AssignToRoom(employeeId, roomId)) { _justWrote = employeeId; return true; }
        return false;
    }

    // --- helpers --------------------------------------------------

    private static List<StatType> RoomRequiredStats(FacilitySimulation sim, string roomId) =>
        sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RequiredStat).Distinct().ToList();

    // 업무 적합도 3단계 — FacilitySimulation.StatWorkRate 의 배율 구간과 같은 기준.
    private static (string Text, Color Col, string Note) FitTier(int value) => value switch
    {
        >= 3 => ("✓ 적합", new Color(0.16f, 0.42f, 0.18f), "업무 속도 조금 빠름"),
        2 => ("○ 보통", new Color(0.85f, 0.47f, 0.06f), "기준 속도"),
        _ => ("△ 비효율", InkRed, "업무 속도 크게 느림"),
    };

    private static string StatIcon(StatType s) => s switch
    {
        StatType.Tech => "🔧",
        StatType.Courage => "🛡",
        StatType.Observation => "🔍",
        _ => "",
    };

    private static string StatLabel(StatType s) => s switch
    {
        StatType.Tech => "기술",
        StatType.Courage => "담력",
        StatType.Observation => "관찰",
        _ => s.ToString(),
    };

    private static string Bar(int v, int max = 3)
    {
        v = Mathf.Clamp(v, 0, max);
        return new string('■', v) + new string('□', max - v);
    }

    // 기존 RoomDetailCard.Descriptions 는 2문장짜리도 있다 — 배치표 정보 패널은
    // 한 문장 이하만 보여준다는 지침에 맞춰 첫 문장만 잘라 쓴다(새 문장을 짓지 않는다).
    private static string FirstSentence(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int idx = s.IndexOf(". ", StringComparison.Ordinal);
        string first = idx >= 0 ? s[..idx] : s.TrimEnd('.', ' ');
        return first.EndsWith('.') ? first : first + ".";
    }

    private void StyleDoc(Button b, Color fg, Color bgFill)
    {
        b.AddThemeFontOverride("font", _body);
        b.AddThemeColorOverride("font_color", fg);
        b.AddThemeColorOverride("font_hover_color", fg);
        b.AddThemeColorOverride("font_pressed_color", fg);
        var normal = new StyleBoxFlat
        {
            BgColor = bgFill,
            BorderColor = new Color(0.4f, 0.32f, 0.2f, 0.5f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.65f, 0.52f, 0.24f, 0.4f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    // TextureRect 에 Position/Size 를 직접 주면(앵커 없이) 텍스처 원본 해상도로 그려지는
    // 경우가 있어, 고정 크기 클립 박스 안에 FullRect 앵커로 채워 넣어 확실히 가둔다.
    public static Control MakeClippedPortrait(Texture2D tex, Vector2 pos, Vector2 size)
    {
        var box = new Control { Position = pos, Size = size, ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        var img = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        img.SetAnchorsPreset(LayoutPreset.FullRect);
        box.AddChild(img);
        return box;
    }

    private Label AddLabel(Control parent, string text, Vector2 pos, int size, Color col, Font font)
    {
        var l = new Label { Text = text, Position = pos, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        parent.AddChild(l);
        return l;
    }

    // --- 직원 카드(드래그 소스) --------------------------------------------

    private partial class EmpCard : Control
    {
        public string EmpId = "";
        public bool Selected;
        public string AssignedRoomName = "";
        public Action<string> OnClick;
        public Action<string, Vector2> OnPressStart;

        private readonly EmployeeDef _def;
        private readonly Font _serif, _body;

        private bool _hover;

        public EmpCard(EmployeeDef def, Font serif, Font body)
        {
            _def = def; _serif = serif; _body = body;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            // 배치된 직원 카드는 짙은 황토색으로 눌러둔다(배치 해제하면 원래 종이색으로 돌아온다).
            // 호버 하이라이트와 같은 계열이되 더 어둡게 — 배치됨/호버가 헷갈리지 않는다.
            bool assigned = !string.IsNullOrEmpty(AssignedRoomName);
            Color bg = Selected ? new Color(0.78f, 0.64f, 0.34f, 0.95f)
                : assigned ? new Color(0.55f, 0.43f, 0.18f, 0.96f)
                : new Color(0.87f, 0.83f, 0.71f, 0.96f);
            if (_hover && !Selected) bg = bg.Lerp(new Color(0.97f, 0.90f, 0.66f), 0.42f);

            DrawRect(new Rect2(Vector2.Zero, Size), bg);
            var border = _hover ? new Color(0.55f, 0.42f, 0.18f) : new Color(0.35f, 0.27f, 0.16f, 0.7f);
            DrawRect(new Rect2(Vector2.Zero, Size), border, false, Selected || _hover ? 2.4f : 1.3f);

            // 짙은 회색 위에서는 글자를 밝게 뒤집는다.
            bool darkBg = assigned && !Selected && !_hover;
            var ink = darkBg ? new Color(0.94f, 0.92f, 0.86f) : new Color(0.16f, 0.12f, 0.08f);
            var dim = darkBg ? new Color(0.80f, 0.78f, 0.72f) : new Color(0.42f, 0.35f, 0.24f);

            DrawString(_serif, new Vector2(8, 20), (Selected ? "▶ " : "") + _def.Codename,
                HorizontalAlignment.Left, -1, 19, ink);

            // 윗줄 오른쪽은 배치처(있으면) 아니면 특성 — 둘을 겹쳐 그리지 않는다.
            string right = assigned ? "→ " + AssignedRoomName : _def.Trait;
            if (!string.IsNullOrEmpty(right))
                DrawString(_body, new Vector2(Size.X - 124, 19), right,
                    HorizontalAlignment.Right, 116, 13, dim);

            DrawMiniStat("기", _def.Tech, 8, 32, ink);
            DrawMiniStat("담", _def.Courage, 66, 32, ink);
            DrawMiniStat("관", _def.Observation, 124, 32, ink);
        }

        private void DrawMiniStat(string label, int v, float x, float y, Color ink)
        {
            DrawString(_body, new Vector2(x, y + 10), label, HorizontalAlignment.Left, -1, 12, ink);
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect2(x + 16 + i * 10, y, 8, 10);
                DrawRect(r, i < v ? ink : new Color(ink.R, ink.G, ink.B, 0.18f));
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                // 논리 좌표(_form 로컬)로 넘긴다 — root._Input 은 MakeInputLocal 로 같은 좌표계를 쓴다.
                OnPressStart?.Invoke(EmpId, Position + mb.Position);
                OnClick?.Invoke(EmpId);
            }
        }
    }

    // --- 작업실 행(클릭 대상 + 드롭 대상) ---------------------------------

    private partial class RoomRow : Control
    {
        public string RoomId = "";
        public List<string> Occupants = new();
        public string JustWrote = "";
        public Func<string, string> CodenameOf;
        public Action<string> OnNameClick;
        public Action<string> OnClearOcc;
        public Action<string, bool> OnHover;
        public Func<string, string, bool> TryDropAssign;
        public bool ManualHover;     // ScheduleBoardUI 의 수동 드래그가 이 행 위에 있을 때

        private readonly RoomDef _def;
        private readonly Font _serif, _body;
        private bool _hover;         // 마우스가 이 행 위에 있을 때(작업실 글자 색 변경)

        private const float NameW = 128f, SlotW = 142f, SlotGap = 8f;

        public RoomRow(RoomDef def, Font serif, Font body)
        {
            _def = def; _serif = serif; _body = body;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); OnHover?.Invoke(RoomId, true); };
            MouseExited += () => { _hover = false; QueueRedraw(); OnHover?.Invoke(RoomId, false); };
        }

        public override void _Draw()
        {
            var ink = new Color(0.17f, 0.13f, 0.09f);
            var dim = new Color(0.42f, 0.35f, 0.24f);
            bool dh = ManualHover;

            // 마우스를 올리면 작업실 이름이 붉게 밝아지고 밑줄이 그어진다.
            bool nameHot = _hover || dh;
            var nameCol = nameHot ? new Color(0.62f, 0.16f, 0.10f) : ink;
            DrawString(_serif, new Vector2(0, 22), _def.DisplayName, HorizontalAlignment.Left, NameW, 19, nameCol);
            if (nameHot)
            {
                float w = _serif.GetStringSize(_def.DisplayName, HorizontalAlignment.Left, NameW, 19).X;
                DrawLine(new Vector2(0, 26), new Vector2(Mathf.Min(w, NameW - 6), 26), nameCol with { A = 0.75f }, 1.4f);
            }

            for (int s = 0; s < 2; s++)
            {
                float x = NameW + s * (SlotW + SlotGap);
                var r = new Rect2(x, 2, SlotW, Size.Y - 4);
                DrawRect(r, new Color(0.80f, 0.75f, 0.60f, dh ? 0.8f : _hover ? 0.6f : 0.45f));
                DrawRect(r, dh ? new Color(0.55f, 0.42f, 0.18f) : new Color(0.4f, 0.32f, 0.2f, 0.55f), false, dh ? 2f : 1.1f);

                string occ = s < Occupants.Count ? Occupants[s] : "";
                if (!string.IsNullOrEmpty(occ))
                    DrawString(_body, new Vector2(x + 10, 22), "[ " + (CodenameOf?.Invoke(occ) ?? occ) + " ]",
                        HorizontalAlignment.Left, SlotW - 16, 15, ink);
                else
                    DrawString(_body, new Vector2(x + 10, 22), "[          ]",
                        HorizontalAlignment.Left, SlotW - 16, 15, dim);
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            float lx = mb.Position.X;
            if (lx < NameW) { OnNameClick?.Invoke(RoomId); return; }

            int slot = lx < NameW + SlotW + SlotGap * 0.5f ? 0 : 1;
            string occ = slot < Occupants.Count ? Occupants[slot] : "";
            if (!string.IsNullOrEmpty(occ)) OnClearOcc?.Invoke(occ);
            else OnNameClick?.Invoke(RoomId); // 빈 슬롯 클릭 = 방 클릭(선택 직원 배치)
        }

    }

    // --- 낡은 문서 질감(고정 시드로 한 번만 그림 — 클릭할 때마다 얼룩이 안 바뀐다) ------

    private partial class PaperTexture : Control
    {
        public override void _Draw()
        {
            var baseA = new Color(0.85f, 0.79f, 0.63f);
            var baseB = new Color(0.82f, 0.76f, 0.59f);
            DrawRect(new Rect2(Vector2.Zero, Size), baseA);

            var rng = new RandomNumberGenerator { Seed = 20940182 };

            // 오른쪽 서류받침(대기 인원/정보 패널) 카드 — 살짝 다른 톤.
            DrawRect(new Rect2(500, 12, 244, Size.Y - 24), new Color(0.80f, 0.78f, 0.70f, 0.85f));
            DrawRect(new Rect2(500, 12, 244, Size.Y - 24), new Color(0, 0, 0, 0.12f), false, 1.2f);

            // 큰 얼룩.
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                float r = rng.RandfRange(14f, 50f);
                DrawCircle(p, r, new Color(0.42f, 0.33f, 0.18f, rng.RandfRange(0.025f, 0.07f)));
            }
            // 미세 섬유/얼룩 반점.
            for (int i = 0; i < 220; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                bool light = rng.Randf() > 0.5f;
                var c = light ? new Color(0.95f, 0.91f, 0.78f, 0.05f) : new Color(0.32f, 0.25f, 0.14f, 0.05f);
                DrawRect(new Rect2(p, new Vector2(1.4f, 1.4f)), c);
            }

            // 가장자리 닳음.
            var edge = new Color(0.28f, 0.21f, 0.11f, 0.18f);
            float b = 16f;
            DrawRect(new Rect2(0, 0, Size.X, b), edge);
            DrawRect(new Rect2(0, Size.Y - b, Size.X, b), edge);
            DrawRect(new Rect2(0, 0, b, Size.Y), edge);
            DrawRect(new Rect2(Size.X - b, 0, b, Size.Y), edge);
            DrawCircle(Vector2.Zero, 46f, new Color(0.22f, 0.16f, 0.08f, 0.12f));
            DrawCircle(new Vector2(Size.X, Size.Y), 54f, new Color(0.22f, 0.16f, 0.08f, 0.12f));
            DrawCircle(new Vector2(Size.X, 0), 34f, new Color(0.22f, 0.16f, 0.08f, 0.09f));
            DrawCircle(new Vector2(0, Size.Y), 38f, new Color(0.22f, 0.16f, 0.08f, 0.09f));

            // 표 구분선 — 완전히 곧지 않게 짧은 세그먼트로 약간씩 어긋나게.
            // (본문 레이아웃 y 값과 짝을 이룬다 — 한쪽만 바꾸면 어긋난다.)
            DrawRoughLine(new Vector2(24, 88), new Vector2(460, 88), rng);
            DrawRoughLine(new Vector2(24, 166), new Vector2(460, 166), rng);
            DrawRoughLine(new Vector2(24, 446), new Vector2(460, 446), rng);

            // 하단 좌측 부서명.
            var font = ViewFont.Default;
            DrawString(font, new Vector2(24, 468), "FACILITY CONTROL DEPT.", HorizontalAlignment.Left, -1, 13, new Color(0.35f, 0.28f, 0.18f));

            // 붉은 승인 도장 — 본문(작업실 표)과 겹치지 않게 하단 우측 여백에.
            DrawSetTransform(new Vector2(430, 480), Mathf.DegToRad(-11f), Vector2.One);
            var stampCol = new Color(0.62f, 0.10f, 0.08f, 0.6f);
            DrawArc(Vector2.Zero, 33f, 0f, Mathf.Tau, 40, stampCol, 2.2f);
            DrawArc(Vector2.Zero, 26f, 0f, Mathf.Tau, 36, stampCol, 1.5f);
            DrawString(font, new Vector2(-22f, 7f), "승인", HorizontalAlignment.Left, -1, 20, stampCol);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        private void DrawRoughLine(Vector2 from, Vector2 to, RandomNumberGenerator rng)
        {
            var col = new Color(0.3f, 0.24f, 0.14f, 0.5f);
            int segs = 26;
            Vector2 prev = from;
            for (int i = 1; i <= segs; i++)
            {
                float t = (float)i / segs;
                var p = from.Lerp(to, t);
                p.Y += rng.RandfRange(-0.5f, 0.5f);
                DrawLine(prev, p, col, 1.2f);
                prev = p;
            }
        }
    }
}
