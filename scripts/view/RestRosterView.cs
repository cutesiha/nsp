using System;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 왼쪽 CRT — DAY1 휴게실 평면도. 명단을 세로로 나열하지 않고, 실제로 전화를 걸
// 직원을 휴게실 안의 고유색 아이콘으로 고른다. 오른쪽 InterviewCCTVView는 이 선택값을
// 그대로 읽어 서 있는 모습을 보여준다.
public partial class RestRosterView : Control
{
    public static RestRosterView Instance { get; private set; }
    public event Action NextRequested;

    public string SelectedEmployeeId { get; private set; } = "";

    // 이전 저장/씬 연결과의 호환용이다. DAY1은 전화 후 질문 목록에서 Q5를 직접 선택하므로
    // 별도의 "의심 추궁" 스위치는 더 이상 사용하지 않는다.
    public bool InterrogateArmed => false;
    public void DisarmInterrogate() { }

    private static readonly Color Bg = new(0.035f, 0.045f, 0.05f);
    private static readonly Color Ink = new(0.75f, 0.82f, 0.9f);
    private static readonly Color Dim = new(0.5f, 0.56f, 0.62f);

    private Font _font;
    private BreakRoomMap _map;
    private Label _selected;
    private Label _instruction;
    private Button _isolateBtn;
    private Button _nextBtn;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg));

        var title = MakeLabel("BREAK ROOM · 인터뷰 대상 선택", 20, Ink);
        title.Position = new Vector2(16, 14);
        AddChild(title);

        var mapPanel = new Panel { Position = new Vector2(16, 54), Size = new Vector2(768, 402) };
        mapPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(mapPanel);

        _map = new BreakRoomMap { Position = new Vector2(7, 7), Size = new Vector2(754, 388) };
        _map.EmployeeSelected += Select;
        mapPanel.AddChild(_map);

        var infoPanel = new Panel { Position = new Vector2(16, 470), Size = new Vector2(500, 116) };
        infoPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(infoPanel);
        _selected = MakeLabel("휴게실 안의 직원 아이콘을 선택하세요.", 14, Dim);
        _selected.Position = new Vector2(14, 13);
        _selected.Size = new Vector2(472, 30);
        _selected.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoPanel.AddChild(_selected);

        _instruction = MakeLabel("책상 위 전화기를 들어 심문을 시작하세요.", 17, Ink);
        _instruction.Position = new Vector2(14, 57);
        _instruction.Size = new Vector2(472, 30);
        _instruction.HorizontalAlignment = HorizontalAlignment.Center;
        infoPanel.AddChild(_instruction);

        _isolateBtn = new Button { Position = new Vector2(532, 476), Size = new Vector2(244, 38), Text = "격리" };
        _isolateBtn.AddThemeFontOverride("font", _font);
        _isolateBtn.AddThemeFontSizeOverride("font_size", ViewFont.FS(15));
        _isolateBtn.Pressed += OnIsolatePressed;
        _isolateBtn.Visible = false;
        AddChild(_isolateBtn);

        _nextBtn = new Button { Position = new Vector2(532, 534), Size = new Vector2(244, 44), Text = "다음 날 근무 배치 ▶" };
        _nextBtn.AddThemeFontOverride("font", _font);
        _nextBtn.AddThemeFontSizeOverride("font_size", ViewFont.FS(16));
        _nextBtn.Pressed += () => NextRequested?.Invoke();
        AddChild(_nextBtn);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Present(bool finalDay)
    {
        _nextBtn.Text = finalDay ? "최종 결과 확인 ▶" : "다음 날 근무 배치 ▶";
        SelectedEmployeeId = "";
        _selected.Text = "휴게실 안의 직원 아이콘을 선택하세요.";
        _isolateBtn.Visible = false;
        RebuildRoster();
    }

    private void RebuildRoster()
    {
        _map?.Populate(FacilitySimulation.Instance, SelectedEmployeeId);
    }

    private void Select(string employeeId)
    {
        SelectedEmployeeId = employeeId;
        var sim = FacilitySimulation.Instance;
        var def = sim?.GetEmployeeDef(employeeId);
        var st = sim?.GetEmployeeState(employeeId);
        if (def == null || st == null) return;

        if (!st.Alive)
        {
            _selected.Text = $"{def.Codename} · 응답 없음";
            _isolateBtn.Visible = false;
        }
        else
        {
            string status = st.Isolated ? "격리됨" : "휴게 중";
            _selected.Text = $"{def.Codename} · {status}";
            _isolateBtn.Visible = true;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
        }
        RebuildRoster();
    }

    private void OnIsolatePressed()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(SelectedEmployeeId)) return;
        var st = sim.GetEmployeeState(SelectedEmployeeId);
        if (st == null || !st.Alive) return;
        if (st.Isolated) sim.CancelIsolation(SelectedEmployeeId);
        else sim.IsolateEmployee(SelectedEmployeeId);
        Select(SelectedEmployeeId);
    }

    private StyleBoxFlat Panelbox() => new()
    {
        BgColor = new Color(0.05f, 0.06f, 0.08f),
        BorderColor = new Color(0.3f, 0.35f, 0.42f),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    private static ColorRect Rect(Color c)
    {
        var r = new ColorRect { Color = c, MouseFilter = MouseFilterEnum.Ignore };
        r.SetAnchorsPreset(LayoutPreset.FullRect);
        return r;
    }

    private Label MakeLabel(string text, int size, Color col)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // 배치가 매번 바뀌어도 아이콘 자체는 직원을 따라간다. 이 좌표는 휴게실의 의자 위치이며,
    // 근무 중 실제 작업실 좌표나 방해자 역할을 표현하지 않는다.
    private partial class BreakRoomMap : Control
    {
        public event Action<string> EmployeeSelected;

        private static readonly Vector2[] Seats =
        {
            new(112, 110), new(328, 110), new(544, 110),
            new(112, 260), new(328, 260), new(544, 260),
        };

        public void Populate(FacilitySimulation sim, string selectedId)
        {
            foreach (Node child in GetChildren()) child.QueueFree();
            if (sim == null) return;

            int seat = 0;
            foreach (string id in sim.GetEmployeeIds())
            {
                if (seat >= Seats.Length) break;
                var def = sim.GetEmployeeDef(id);
                var state = sim.GetEmployeeState(id);
                if (def == null || state == null) continue;

                Color accent = state.Alive ? def.IconColor : new Color(0.28f, 0.30f, 0.32f);
                var b = new Button
                {
                    Position = Seats[seat++],
                    Size = new Vector2(128, 62),
                    Text = $"●  {def.Codename}",
                    TooltipText = state.Alive ? $"{def.Codename} 선택" : $"{def.Codename} · 응답 없음",
                    Disabled = !state.Alive,
                };
                b.AddThemeFontOverride("font", ViewFont.Default);
                b.AddThemeFontSizeOverride("font_size", ViewFont.FS(16));
                b.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.35f));
                b.AddThemeStyleboxOverride("normal", IconBox(accent, id == selectedId));
                b.AddThemeStyleboxOverride("hover", IconBox(accent.Lerp(Colors.White, 0.25f), true));
                b.AddThemeStyleboxOverride("pressed", IconBox(accent, true));
                string captured = id;
                b.Pressed += () => EmployeeSelected?.Invoke(captured);
                AddChild(b);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.025f, 0.04f, 0.045f));
            DrawRect(new Rect2(8, 8, Size.X - 16, Size.Y - 16), new Color(0.07f, 0.095f, 0.10f), false, 2f);

            // 중앙 휴게 테이블과 여섯 의자: 2D 평면도에서 지금 선택하는 사람이 어디에 앉아
            // 있는지 직관적으로 보이도록 하는 장식이며 게임 좌표를 변경하지 않는다.
            DrawRect(new Rect2(247, 84, 260, 210), new Color(0.19f, 0.16f, 0.12f));
            DrawRect(new Rect2(255, 92, 244, 194), new Color(0.28f, 0.23f, 0.16f), false, 2f);
            foreach (Vector2 seat in Seats)
            {
                Vector2 chair = seat + new Vector2(20, 70);
                DrawRect(new Rect2(chair, new Vector2(88, 22)), new Color(0.16f, 0.21f, 0.22f));
            }

            DrawRect(new Rect2(28, 42, 156, 36), new Color(0.10f, 0.20f, 0.22f));
            DrawString(ViewFont.Default, new Vector2(42, 66), "COFFEE / MED", HorizontalAlignment.Left, 132, ViewFont.FS(13), new Color(0.62f, 0.82f, 0.82f));
            DrawRect(new Rect2(Size.X - 184, 42, 156, 36), new Color(0.17f, 0.13f, 0.20f));
            DrawString(ViewFont.Default, new Vector2(Size.X - 169, 66), "LOCKER / REST", HorizontalAlignment.Left, 132, ViewFont.FS(13), new Color(0.78f, 0.68f, 0.9f));
            DrawString(ViewFont.Default, new Vector2(24, Size.Y - 20), "아이콘 선택 → 책상 위 전화기", HorizontalAlignment.Left, 340, ViewFont.FS(14), new Color(0.52f, 0.67f, 0.70f));
        }

        private static StyleBoxFlat IconBox(Color accent, bool selected) => new()
        {
            BgColor = new Color(accent.R, accent.G, accent.B, selected ? 0.34f : 0.18f),
            BorderColor = accent.Lerp(Colors.White, selected ? 0.45f : 0.1f),
            BorderWidthLeft = selected ? 3 : 1,
            BorderWidthTop = selected ? 3 : 1,
            BorderWidthRight = selected ? 3 : 1,
            BorderWidthBottom = selected ? 3 : 1,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        };
    }
}
