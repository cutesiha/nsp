using System;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 왼쪽 CRT — 휴게시간. 근무 기록 대신 직원 명단만 담백하게 보여준다. 직원을 고르면
// 오른쪽 CCTV(InterviewCCTVView)가 그 직원으로 전환되고, 전화로 실제 대화한다
// (기존 Phone3D/PhoneCallHud/CallBubble 그대로 재사용 — 여기서는 선택 상태만 갖는다).
public partial class RestRosterView : Control
{
    public static RestRosterView Instance { get; private set; }
    public event Action NextRequested;

    public string SelectedEmployeeId { get; private set; } = "";

    private static readonly Color Bg = new(0.035f, 0.045f, 0.05f);
    private static readonly Color Ink = new(0.75f, 0.82f, 0.9f);
    private static readonly Color Dim = new(0.5f, 0.56f, 0.62f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);

    private Font _font;
    private VBoxContainer _rosterBox;
    private RichTextLabel _detail;
    private Button _isolateBtn;
    private Button _nextBtn;
    private bool _finalDay;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg));

        var title = MakeLabel("BREAK ROOM · 직원 명단", 20, Ink);
        title.Position = new Vector2(16, 14);
        AddChild(title);

        var rosterPanel = new Panel { Position = new Vector2(16, 56), Size = new Vector2(360, 480) };
        rosterPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(rosterPanel);

        var scroll = new ScrollContainer { Position = new Vector2(6, 6), Size = new Vector2(348, 468) };
        rosterPanel.AddChild(scroll);
        _rosterBox = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        _rosterBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rosterBox);

        var detailPanel = new Panel { Position = new Vector2(392, 56), Size = new Vector2(392, 480) };
        detailPanel.AddThemeStyleboxOverride("panel", Panelbox());
        AddChild(detailPanel);

        _detail = new RichTextLabel
        {
            Position = new Vector2(12, 10),
            Size = new Vector2(368, 380),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _detail.AddThemeFontOverride("normal_font", _font);
        _detail.AddThemeFontSizeOverride("normal_font_size", 15);
        _detail.AddThemeColorOverride("default_color", Ink);
        _detail.Text = "[color=#556]왼쪽에서 직원을 선택하세요.[/color]";
        detailPanel.AddChild(_detail);

        _isolateBtn = new Button { Position = new Vector2(12, 398), Size = new Vector2(368, 36), Text = "격리", Visible = false };
        _isolateBtn.Pressed += OnIsolatePressed;
        detailPanel.AddChild(_isolateBtn);

        _nextBtn = new Button { Position = new Vector2(544, 546), Size = new Vector2(232, 40), Text = "다음 날 근무 배치 ▶" };
        _nextBtn.AddThemeFontSizeOverride("font_size", 16);
        _nextBtn.Pressed += () => NextRequested?.Invoke();
        AddChild(_nextBtn);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Present(bool finalDay)
    {
        _finalDay = finalDay;
        _nextBtn.Text = finalDay ? "최종 결과 확인 ▶" : "다음 날 근무 배치 ▶";
        SelectedEmployeeId = "";
        _detail.Text = "[color=#556]왼쪽에서 직원을 선택하세요.[/color]";
        _isolateBtn.Visible = false;
        RebuildRoster();
    }

    private void RebuildRoster()
    {
        foreach (Node c in _rosterBox.GetChildren()) c.QueueFree();
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        foreach (var id in sim.GetEmployeeIds())
        {
            var def = sim.GetEmployeeDef(id);
            var st = sim.GetEmployeeState(id);
            if (def == null || st == null) continue;

            string suffix = !st.Alive ? "  [사망]" : st.Isolated ? "  [격리됨]" : "";
            var b = new Button
            {
                Text = def.Codename + suffix,
                CustomMinimumSize = new Vector2(330, 40),
                Alignment = HorizontalAlignment.Left,
            };
            b.AddThemeFontSizeOverride("font_size", 16);
            string cap = id;
            b.Pressed += () => Select(cap);
            _rosterBox.AddChild(b);
        }
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
            _detail.Text = $"[font_size=18]{def.Codename}[/font_size]\n\n[color=#ff6a55](응답 없음)[/color]";
            _isolateBtn.Visible = false;
            return;
        }

        string status = st.Isolated ? "[color=#dd88dd]격리됨[/color]" : "정상";
        _detail.Text =
            $"[font_size=18]{def.Codename}[/font_size]\n\n" +
            $"기술 {def.Tech}  담력 {def.Courage}  관찰 {def.Observation}\n" +
            $"스트레스 : {st.Stress:0}%\n" +
            $"상태 : {status}\n\n" +
            "[color=#8899aa]전화기를 들어 대화하세요.[/color]";
        _isolateBtn.Visible = true;
        _isolateBtn.Text = st.Isolated ? "격리 취소" : "격리";
    }

    private void OnIsolatePressed()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(SelectedEmployeeId)) return;
        var st = sim.GetEmployeeState(SelectedEmployeeId);
        if (st == null) return;
        if (st.Isolated) sim.CancelIsolation(SelectedEmployeeId);
        else sim.IsolateEmployee(SelectedEmployeeId);
        Select(SelectedEmployeeId);
        RebuildRoster();
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
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }
}
