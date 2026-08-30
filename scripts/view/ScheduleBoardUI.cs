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

        Rebuild();
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

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        int day = GameState.Instance?.CurrentDay ?? 1;

        AddLabel(_form, $"DOC NO. NSP-04-{day:00}   FACILITY CONTROL DEPT.", new Vector2(DocLeft, 6), 10, InkDim, _body);
        AddLabel(_form, $"DAY {day:00}", new Vector2(DocLeft, 22), 27, Ink, _serif);
        AddLabel(_form, "N I G H T   S H I F T   A S S I G N M E N T", new Vector2(DocLeft, 58), 11, InkDim, _body);

        AddLabel(_form, "TODAY'S PROTOCOL", new Vector2(DocLeft, 92), 11, InkRed, _body);
        var taboos = TabooRuleSystem.Instance?.GetActiveTaboos().ToList();
        string tabooText = taboos == null || taboos.Count == 0 ? "특이사항 없음" : "⚠ " + string.Join("   ⚠ ", taboos.Select(t => t.Description));
        var tabooLbl = AddLabel(_form, tabooText, new Vector2(DocLeft, 108), 13, InkRed, _body);
        tabooLbl.Size = new Vector2(DocRight - DocLeft, 30);
        tabooLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var rooms = sim.GetRoomIds()
            .Where(id =>
            {
                var d = sim.GetRoomDef(id);
                return d != null && !d.IsRestricted && sim.GetRoomTasksInPriorityOrder(id).Count > 0;
            })
            .ToList();

        float y = 162f;
        foreach (var roomId in rooms)
        {
            var def = sim.GetRoomDef(roomId);

            var nameBtn = new Button
            {
                Text = def.DisplayName,
                Position = new Vector2(DocLeft, y),
                Size = new Vector2(122, 30),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
            };
            StyleDoc(nameBtn, Ink, new Color(0, 0, 0, 0));
            string capRoom = roomId;
            nameBtn.Pressed += () => OnRoomClicked(capRoom);
            nameBtn.MouseEntered += () => OnRoomHover(capRoom, true);
            nameBtn.MouseExited += () => OnRoomHover(capRoom, false);
            _form.AddChild(nameBtn);

            var here = sim.GetEmployeeIds()
                .Select(sim.GetEmployeeState)
                .Where(s => s != null && s.AssignedRoomId == roomId)
                .Select(s => s.EmployeeId)
                .ToList();

            for (int slot = 0; slot < 2; slot++)
            {
                string occ = slot < here.Count ? here[slot] : "";
                var b = SlotButton(occ, sim);
                b.Position = new Vector2(150 + slot * 156, y);
                string capturedOcc = occ, capRoom2 = roomId;
                b.Pressed += () => OnSlot(capRoom2, capturedOcc);
                b.MouseEntered += () => OnRoomHover(capRoom2, true);
                b.MouseExited += () => OnRoomHover(capRoom2, false);
                _form.AddChild(b);
            }
            y += 32f;
        }

        // --- 오른쪽 대기 인원 ---
        AddLabel(_form, "대기 인원", new Vector2(DockLeft + 12, 22), 13, InkDim, _body);
        float ey = 46f;
        foreach (var empId in sim.GetEmployeeIds())
        {
            var edef = sim.GetEmployeeDef(empId);
            var est = sim.GetEmployeeState(empId);
            if (edef == null || est == null) continue;

            bool assigned = !string.IsNullOrEmpty(est.AssignedRoomId);
            bool selected = empId == _selectedEmp;

            var b = new Button
            {
                Text = (selected ? "▶ " : "") + edef.Codename + (assigned ? "  ✓" : ""),
                Position = new Vector2(DockLeft + 12, ey),
                Size = new Vector2(DockRight - DockLeft - 24, 30),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
            };
            StyleDoc(b, selected ? Ink : (assigned ? InkDim : Ink), selected ? SelectFill : new Color(0, 0, 0, 0));
            string cap = empId;
            b.Pressed += () => OnEmployeeClicked(cap);
            _form.AddChild(b);
            ey += 32f;
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
            new Vector2(DocLeft, CanvasSize.Y - 46), 14, !coreStaffed || missing > 0 ? InkRed : Ink, _body);
        status.Size = new Vector2(360, 26);

        var start = new Button
        {
            Text = "근무 시작 ▶",
            Position = new Vector2(CanvasSize.X - 202, CanvasSize.Y - 52),
            Size = new Vector2(178, 40),
            Disabled = !coreStaffed,
        };
        StyleDoc(start, coreStaffed ? new Color(0.95f, 0.92f, 0.83f) : InkDim,
            coreStaffed ? new Color(0.14f, 0.11f, 0.07f) : new Color(0.5f, 0.46f, 0.36f, 0.4f));
        start.AddThemeFontSizeOverride("font_size", 17);
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

        const float px = DockLeft + 12, pw = DockRight - DockLeft - 24;
        const float py = 300f;

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

        var hint = AddLabel(_info, "직원 또는 작업실을\n클릭하면 정보가\n표시됩니다.", new Vector2(px, py + 8), 13, InkDim, _body);
        hint.Size = new Vector2(pw, 80);
    }

    private void DrawRoomInfo(FacilitySimulation sim, string roomId, float px, float py, float pw)
    {
        var def = sim.GetRoomDef(roomId);
        if (def == null) return;

        AddLabel(_info, def.DisplayName, new Vector2(px, py), 17, Ink, _serif);

        var stats = RoomRequiredStats(sim, roomId);
        string statLine = stats.Count == 0 ? "" : string.Join("  ·  ", stats.Select(s => $"{StatIcon(s)} {StatLabel(s)}"));
        int headcount = sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RecommendedHeadcount).DefaultIfEmpty(1).Max();
        AddLabel(_info, $"{statLine}   ·   권장 인원 {headcount}명", new Vector2(px, py + 28), 13, InkDim, _body);

        string desc = FirstSentence(RoomDetailCard.Descriptions.GetValueOrDefault(roomId, ""));
        var d = AddLabel(_info, desc, new Vector2(px, py + 54), 13, Ink, _body);
        d.Size = new Vector2(pw, 60);
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    private void DrawEmployeeInfo(FacilitySimulation sim, string employeeId, float px, float py, float pw)
    {
        var def = sim.GetEmployeeDef(employeeId);
        if (def == null) return;

        if (def.FacePortrait != null)
            _info.AddChild(MakeClippedPortrait(def.FacePortrait, new Vector2(px, py), new Vector2(56, 56)));
        AddLabel(_info, def.Codename, new Vector2(px + 64, py + 4), 18, Ink, _serif);

        float sy = py + 64;
        AddLabel(_info, $"기술   {Bar(def.Tech)}  {def.Tech}", new Vector2(px, sy), 13, Ink, _body);
        AddLabel(_info, $"담력   {Bar(def.Courage)}  {def.Courage}", new Vector2(px, sy + 20), 13, Ink, _body);
        AddLabel(_info, $"관찰   {Bar(def.Observation)}  {def.Observation}", new Vector2(px, sy + 40), 13, Ink, _body);

        if (!string.IsNullOrEmpty(def.Trait))
        {
            AddLabel(_info, "특성", new Vector2(px, sy + 70), 11, InkDim, _body);
            AddLabel(_info, def.Trait, new Vector2(px, sy + 88), 14, Ink, _body);
        }
    }

    private void DrawCompare(FacilitySimulation sim, string employeeId, string roomId, float px, float py, float pw)
    {
        var edef = sim.GetEmployeeDef(employeeId);
        var rdef = sim.GetRoomDef(roomId);
        if (edef == null || rdef == null) return;

        var stats = RoomRequiredStats(sim, roomId);
        var primary = stats.Count > 0 ? stats[0] : StatType.Tech;
        int value = edef.GetStat(primary);

        AddLabel(_info, $"{edef.Codename}  ·  {StatIcon(primary)} {StatLabel(primary)} {value}", new Vector2(px, py), 14, Ink, _body);
        AddLabel(_info, $"→ {rdef.DisplayName} 요구 능력: {StatLabel(primary)}", new Vector2(px, py + 24), 13, InkDim, _body);

        bool fit = value >= 2;
        var l = AddLabel(_info, fit ? "✓ 적합" : "△ 비효율", new Vector2(px, py + 56), 16, fit ? new Color(0.16f, 0.42f, 0.18f) : InkRed, _body);
        l.Size = new Vector2(pw, 24);
    }

    // --- interaction --------------------------------------------------

    private void OnRoomClicked(string roomId)
    {
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

    private void OnSlot(string roomId, string occupantId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        if (!string.IsNullOrEmpty(occupantId))
        {
            sim.ClearAssignment(occupantId);
            _justWrote = "";
        }
        else if (!string.IsNullOrEmpty(_selectedEmp))
        {
            sim.ClearAssignment(_selectedEmp);
            if (sim.AssignToRoom(_selectedEmp, roomId))
                _justWrote = _selectedEmp;
            _selectedEmp = "";
        }

        _focusRoom = roomId;
        _focusEmp = "";
        _hoverRoom = "";
        RebuildForm();
        RefreshInfoPanel();
    }

    // --- helpers --------------------------------------------------

    private static List<StatType> RoomRequiredStats(FacilitySimulation sim, string roomId) =>
        sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RequiredStat).Distinct().ToList();

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

    private Button SlotButton(string occ, FacilitySimulation sim)
    {
        bool filled = !string.IsNullOrEmpty(occ);
        string code = filled ? $"[ {sim.GetEmployeeDef(occ)?.Codename ?? occ} ]" : "[            ]";
        var b = new Button
        {
            Text = code,
            Size = new Vector2(150, 30),
            Flat = true,
        };
        StyleDoc(b, filled ? Ink : InkDim, SlotFill);
        b.AddThemeFontSizeOverride("font_size", 13);
        if (filled && occ == _justWrote)
        {
            b.Modulate = new Color(1, 1, 1, 0);
            var t = b.CreateTween();
            t.TweenProperty(b, "modulate:a", 1f, 0.28);
        }
        return b;
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
            DrawRoughLine(new Vector2(24, 88), new Vector2(460, 88), rng);
            DrawRoughLine(new Vector2(24, 150), new Vector2(460, 150), rng);
            DrawRoughLine(new Vector2(24, 394), new Vector2(460, 394), rng);

            // 하단 좌측 부서명.
            var font = ViewFont.Default;
            DrawString(font, new Vector2(24, 414), "FACILITY CONTROL DEPT.", HorizontalAlignment.Left, -1, 12, new Color(0.35f, 0.28f, 0.18f));

            // 붉은 승인 도장.
            DrawSetTransform(new Vector2(378, 452), Mathf.DegToRad(-11f), Vector2.One);
            var stampCol = new Color(0.62f, 0.10f, 0.08f, 0.6f);
            DrawArc(Vector2.Zero, 42f, 0f, Mathf.Tau, 40, stampCol, 2.4f);
            DrawArc(Vector2.Zero, 33f, 0f, Mathf.Tau, 36, stampCol, 1.6f);
            DrawString(font, new Vector2(-26f, 7f), "승인", HorizontalAlignment.Left, -1, 22, stampCol);
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
