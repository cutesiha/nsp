using System;
using System.Collections.Generic;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 왼쪽 CRT — 휴게시간. 휴게실(BREAK ROOM)을 위에서 내려다본 2D 도식 화면이다.
// 직원은 동물 얼굴 아이콘(EmployeeDef.FacePortrait)으로 크게 표시되고, 클릭하면
// 화면 오른쪽의 인터뷰 자리로 걸어간다. 오른쪽 CRT(InterviewCCTVView)는 여기서 고른
// 직원을 3D 방 사이드뷰 위에 스탠딩으로 띄운다.
//
// 이 화면은 '2D 탑뷰' 전용이다 — 3D 렌더를 쓰지 않는다.
// 게임 판정(격리 / 다음 날 진행)은 기존과 동일하게 FacilitySimulation 에 그대로 위임한다.
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
    private BreakRoomTopView _map;
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

        var title = MakeLabel("BREAK ROOM", 22, Ink);
        title.Position = new Vector2(18, 10);
        AddChild(title);

        var sub = MakeLabel("휴게실 상단 감시 · 인터뷰 대상 선택", 13, Dim);
        sub.Position = new Vector2(20, 36);
        AddChild(sub);

        var mapPanel = new Panel { Position = new Vector2(16, 58), Size = new Vector2(768, 400) };
        mapPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(mapPanel);

        _map = new BreakRoomTopView { Position = new Vector2(6, 6), Size = new Vector2(756, 388) };
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
        _isolateBtn.Disabled = true;
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
        // 직원을 아직 안 골랐으면 격리는 누를 수 없다(버튼은 그대로 보인다).
        _isolateBtn.Disabled = true;
        _isolateBtn.Text = "격리";
        _map?.Populate(FacilitySimulation.Instance);
        _map?.SetSelected("");
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
            _isolateBtn.Disabled = true;
        }
        else
        {
            string status = st.Isolated ? "격리됨" : "휴게 중";
            _selected.Text = $"{def.Codename} · {status}";
            _isolateBtn.Disabled = false;
            _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
        }

        // 선택한 직원이 인터뷰 자리로 걸어가는 연출. 오른쪽 CRT 는 SelectedEmployeeId 만 보므로
        // 이 연출이 게임 판정에 영향을 주지 않는다.
        _map?.SetSelected(employeeId);
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
        _map?.Refresh();
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

    // ────────────────────────────────────────────────────────────────
    //  휴게실 탑뷰 (2D)
    // ────────────────────────────────────────────────────────────────
    // 위에서 내려다본 방 한 칸. 사방 벽 두께가 보이고 그 안쪽이 바닥이다.
    // 오른쪽 벽에 인터뷰실로 통하는 문이 있고, 선택된 직원이 그 앞으로 걸어간다.
    private partial class BreakRoomTopView : Control
    {
        public event Action<string> EmployeeSelected;

        private const float WallThickness = 16f;
        private const float IconSize = 84f;

        private readonly List<EmployeeIcon> _icons = new();
        private string _selectedId = "";

        // 휴게실 안 자리(아이콘 중심). 방 안에 흩어져 있는 느낌으로 배치한다.
        private static readonly Vector2[] Seats =
        {
            new(150, 108), new(330, 92),  new(500, 120),
            new(142, 260), new(322, 274), new(498, 250),
        };

        // 오른쪽 문 앞 = 인터뷰 대기 위치.
        private Vector2 InterviewSpot => new(Size.X - 108f, Size.Y * 0.5f);

        public void Populate(FacilitySimulation sim)
        {
            foreach (var icon in _icons) icon.QueueFree();
            _icons.Clear();
            if (sim == null) return;

            int seat = 0;
            foreach (string id in sim.GetEmployeeIds())
            {
                if (seat >= Seats.Length) break;
                var def = sim.GetEmployeeDef(id);
                if (def == null) continue;

                var icon = new EmployeeIcon
                {
                    EmployeeId = id,
                    Size = new Vector2(IconSize, IconSize + 18f),
                    HomeSeat = Seats[seat++],
                };
                icon.Position = icon.HomeSeat - icon.Size * 0.5f;
                icon.Clicked += id2 => EmployeeSelected?.Invoke(id2);
                AddChild(icon);
                _icons.Add(icon);
            }
            QueueRedraw();
        }

        public void Refresh()
        {
            foreach (var icon in _icons) icon.QueueRedraw();
        }

        public void SetSelected(string employeeId)
        {
            _selectedId = employeeId;
            foreach (var icon in _icons)
            {
                bool sel = icon.EmployeeId == employeeId;
                icon.Selected = sel;
                icon.MoveTo(sel ? InterviewSpot : icon.HomeSeat);
                icon.QueueRedraw();
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            float w = Size.X, h = Size.Y;

            // 벽(바깥 테두리 두께) → 그 안쪽이 바닥. 평면 네모가 아니라 방 안처럼 보이게 한다.
            DrawRect(new Rect2(0, 0, w, h), new Color(0.115f, 0.125f, 0.145f));           // 벽 몸통
            DrawRect(new Rect2(0, 0, w, h), new Color(0.30f, 0.35f, 0.40f), false, 2f);   // 바깥선

            var floor = new Rect2(WallThickness, WallThickness, w - WallThickness * 2f, h - WallThickness * 2f);
            DrawRect(floor, new Color(0.055f, 0.075f, 0.082f));                            // 바닥
            DrawRect(floor, new Color(0.22f, 0.28f, 0.31f), false, 1.5f);                  // 걸레받이

            // 바닥 타일 격자 — 위에서 내려다본 느낌을 준다.
            var grid = new Color(1f, 1f, 1f, 0.030f);
            for (float x = floor.Position.X + 54f; x < floor.End.X; x += 54f)
                DrawLine(new Vector2(x, floor.Position.Y), new Vector2(x, floor.End.Y), grid, 1f);
            for (float y = floor.Position.Y + 54f; y < floor.End.Y; y += 54f)
                DrawLine(new Vector2(floor.Position.X, y), new Vector2(floor.End.X, y), grid, 1f);

            // 중앙 휴게 테이블(윗면 + 테두리).
            var table = new Rect2(w * 0.5f - 96f, h * 0.5f - 46f, 192f, 92f);
            DrawRect(table, new Color(0.20f, 0.17f, 0.13f));
            DrawRect(table, new Color(0.34f, 0.28f, 0.19f), false, 2f);
            DrawString(ViewFont.Default, table.Position + new Vector2(0, table.Size.Y * 0.5f + 5f),
                "TABLE", HorizontalAlignment.Center, table.Size.X, ViewFont.FS(12),
                new Color(0.45f, 0.40f, 0.32f));

            // 왼쪽 벽 붙박이 — 커피/구급.
            var coffee = new Rect2(WallThickness + 2f, 44f, 30f, 108f);
            DrawRect(coffee, new Color(0.10f, 0.22f, 0.24f));
            DrawRect(coffee, new Color(0.30f, 0.55f, 0.58f), false, 1.5f);
            DrawStringRotated("COFFEE / MED", coffee.Position + new Vector2(21f, 12f), new Color(0.55f, 0.78f, 0.80f));

            // 아래 벽 붙박이 — 사물함.
            var locker = new Rect2(w * 0.5f - 84f, h - WallThickness - 30f, 168f, 28f);
            DrawRect(locker, new Color(0.17f, 0.13f, 0.20f));
            DrawRect(locker, new Color(0.45f, 0.36f, 0.55f), false, 1.5f);
            DrawString(ViewFont.Default, locker.Position + new Vector2(0, 19f), "LOCKER",
                HorizontalAlignment.Center, locker.Size.X, ViewFont.FS(12), new Color(0.68f, 0.58f, 0.80f));

            // 오른쪽 벽의 인터뷰실 출입문 — 선택한 직원이 이 앞으로 간다.
            float doorH = 120f;
            var door = new Rect2(w - WallThickness - 3f, h * 0.5f - doorH * 0.5f, WallThickness + 3f, doorH);
            bool open = !string.IsNullOrEmpty(_selectedId);
            DrawRect(door, open ? new Color(0.16f, 0.34f, 0.30f) : new Color(0.10f, 0.13f, 0.15f));
            DrawRect(door, open ? new Color(0.45f, 0.95f, 0.80f) : new Color(0.34f, 0.40f, 0.44f), false, 2f);
            DrawStringRotated("INTERVIEW", new Vector2(w - 30f, h * 0.5f - 44f),
                open ? new Color(0.55f, 1f, 0.88f) : new Color(0.45f, 0.52f, 0.56f));

            // 문 앞 대기 표시.
            Vector2 spot = InterviewSpot;
            DrawArc(spot, 44f, 0f, Mathf.Tau, 40,
                open ? new Color(0.45f, 0.95f, 0.80f, 0.55f) : new Color(0.35f, 0.42f, 0.46f, 0.30f), 1.5f);
        }

        // 세로쓰기 대신 글자 사이를 벌려 벽에 붙은 라벨처럼 보이게 한다.
        private void DrawStringRotated(string text, Vector2 at, Color col)
        {
            var f = ViewFont.Default;
            int fs = ViewFont.FS(11);
            float y = at.Y;
            foreach (char c in text)
            {
                if (c != ' ' && c != '/')
                    DrawString(f, new Vector2(at.X, y), c.ToString(), HorizontalAlignment.Center, 20f, fs, col);
                y += fs * 0.95f;
            }
        }

        // 동물 얼굴 아이콘 하나. 기존 미니맵의 고유색(IconColor)을 테두리로 유지하면서,
        // EmployeeDef.FacePortrait(동물 얼굴 원화)를 크게 얹는다. 원화가 아직 없는 직원은
        // 고유색 원 + 코드네임 첫 글자로 대체한다(화면이 비지 않게).
        private partial class EmployeeIcon : Control
        {
            public event Action<string> Clicked;

            public string EmployeeId = "";
            public Vector2 HomeSeat;
            public bool Selected;

            private bool _hover;
            private Tween _move;

            public override void _Ready()
            {
                MouseFilter = MouseFilterEnum.Stop;
                MouseEntered += () => { _hover = true; QueueRedraw(); };
                MouseExited += () => { _hover = false; QueueRedraw(); };
            }

            public void MoveTo(Vector2 center)
            {
                Vector2 target = center - Size * 0.5f;
                if (Position.DistanceTo(target) < 0.5f) return;
                _move?.Kill();
                _move = CreateTween();
                _move.TweenProperty(this, "position", target, 0.45)
                     .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            }

            public override void _GuiInput(InputEvent e)
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                var st = FacilitySimulation.Instance?.GetEmployeeState(EmployeeId);
                if (st is not { Alive: true }) return;   // 사망자는 인터뷰 대상이 아니다
                Clicked?.Invoke(EmployeeId);
                AcceptEvent();
            }

            public override void _Draw()
            {
                var sim = FacilitySimulation.Instance;
                var def = sim?.GetEmployeeDef(EmployeeId);
                var st = sim?.GetEmployeeState(EmployeeId);
                if (def == null) return;

                bool alive = st?.Alive ?? false;
                float d = Size.X;
                Vector2 c = new(d * 0.5f, d * 0.5f);
                float r = d * 0.5f - 4f;

                Color accent = alive ? def.IconColor : new Color(0.30f, 0.32f, 0.34f);

                // 바닥 그림자 — 탑뷰에서 아이콘이 바닥 위에 떠 있는 느낌.
                DrawCircle(c + new Vector2(0, 4f), r, new Color(0f, 0f, 0f, 0.35f));

                // 선택/호버 링.
                if (Selected)
                    DrawArc(c, r + 5f, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.92f), 3.5f);
                else if (_hover && alive)
                    DrawArc(c, r + 4f, 0f, Mathf.Tau, 48, accent.Lerp(Colors.White, 0.5f), 2f);

                // 얼굴 판.
                DrawCircle(c, r, new Color(0.09f, 0.10f, 0.12f));
                DrawArc(c, r, 0f, Mathf.Tau, 48, accent.Lerp(Colors.White, Selected ? 0.45f : 0.12f),
                    Selected ? 3f : 2f);

                var face = def.FacePortrait;
                if (face != null)
                {
                    float s = r * 1.72f;
                    var box = new Rect2(c - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s));
                    DrawTextureRect(face, box, false,
                        alive ? Colors.White : new Color(0.45f, 0.45f, 0.48f));
                }
                else
                {
                    // 원화 미제작 직원(해파리 / 올빼미) 대체 표시.
                    DrawCircle(c, r * 0.72f, new Color(accent.R, accent.G, accent.B, alive ? 0.55f : 0.25f));
                    string initial = string.IsNullOrEmpty(def.Codename) ? "?" : def.Codename.Substring(0, 1);
                    DrawString(ViewFont.Default, c + new Vector2(-r, 10f), initial,
                        HorizontalAlignment.Center, r * 2f, ViewFont.FS(30),
                        alive ? Colors.White : new Color(0.6f, 0.6f, 0.6f));
                }

                // 코드네임 + 상태.
                var nameCol = alive ? new Color(0.95f, 0.95f, 0.86f) : new Color(0.55f, 0.55f, 0.58f);
                DrawString(ViewFont.Default, new Vector2(-14f, d + 13f), def.Codename,
                    HorizontalAlignment.Center, d + 28f, ViewFont.FS(14), nameCol);

                if (st is { Alive: false })
                    DrawString(ViewFont.Default, new Vector2(-14f, d + 1f), "응답 없음",
                        HorizontalAlignment.Center, d + 28f, ViewFont.FS(11), new Color(0.9f, 0.35f, 0.35f));
                else if (st is { Isolated: true })
                    DrawString(ViewFont.Default, new Vector2(-14f, d + 1f), "[격리]",
                        HorizontalAlignment.Center, d + 28f, ViewFont.FS(11), new Color(0.88f, 0.52f, 0.9f));
            }
        }
    }
}
