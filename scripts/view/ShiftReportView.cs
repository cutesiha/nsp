using System;
using System.Linq;
using System.Text;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 왼쪽 CRT — 근무 종료 시 SettlementScreen(2D) 을 대신해 여기서 보고서를 보여준다.
// 계산 로직은 SettlementScreen 이 하던 집계를 그대로 옮겨왔다(EventLog / GameState 재사용).
// 텍스트 대신 숫자 위주로, 핵심 사건 몇 줄만.
public partial class ShiftReportView : Control
{
    public static ShiftReportView Instance { get; private set; }
    public event Action ContinueRequested;

    private static readonly Color Bg = new(0.035f, 0.05f, 0.045f);
    private static readonly Color Ink = new(0.7f, 0.9f, 0.78f);
    private static readonly Color Dim = new(0.45f, 0.6f, 0.55f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);
    private static readonly Color Alert = new(1f, 0.4f, 0.32f);

    private Font _font;
    private RichTextLabel _body;
    private RichTextLabel _events;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(Rect(Bg));

        var title = MakeLabel("SHIFT REPORT", 22, Ink);
        title.Position = new Vector2(24, 20);
        AddChild(title);

        _body = new RichTextLabel
        {
            Position = new Vector2(24, 70),
            Size = new Vector2(752, 220),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _body.AddThemeFontOverride("normal_font", _font);
        _body.AddThemeFontSizeOverride("normal_font_size", 22);
        _body.AddThemeColorOverride("default_color", Ink);
        AddChild(_body);

        var evHead = MakeLabel("주요 사건", 14, Dim);
        evHead.Position = new Vector2(24, 300);
        AddChild(evHead);

        _events = new RichTextLabel
        {
            Position = new Vector2(24, 326),
            Size = new Vector2(752, 130),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _events.AddThemeFontOverride("normal_font", _font);
        _events.AddThemeFontSizeOverride("normal_font_size", 15);
        _events.AddThemeColorOverride("default_color", Dim);
        AddChild(_events);

        var btn = MonitorUi.Button("계속 ▶", Ink, _font, () => ContinueRequested?.Invoke(), 17);
        btn.Position = new Vector2(600, 542);
        btn.Size = new Vector2(176, 44);
        AddChild(btn);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 근무 시작 시점의 코어/자재 값을 받아 증감을 보여준다.
    public void Present(float coreAtStart, int materialsAtStart)
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        if (sim == null || gs == null) return;

        var entries = EventLog.Instance?.GetAllEntries() ?? new System.Collections.Generic.List<LogEntry>();
        int taboo = entries.Count(e => e.EventType == LogEventType.TabooViolation);
        int sabotage = entries.Count(e => e.EventType == LogEventType.Sabotage);
        int incidents = entries.Count(e => e.EventType is LogEventType.TaskFailed or LogEventType.PowerOutage or LogEventType.CctvDisconnect) + sabotage;
        int isolated = sim.GetEmployeeIds().Count(id => sim.GetEmployeeState(id)?.Isolated == true);
        int aliveCount = sim.GetEmployeeIds().Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        int total = sim.GetEmployeeIds().Count;

        float coreDelta = gs.CoreProgress - coreAtStart;
        int materialsDelta = gs.Materials - materialsAtStart;

        var sb = new StringBuilder();
        sb.AppendLine($"[font_size=26]DAY {gs.CurrentDay} — SHIFT COMPLETE[/font_size]\n");
        sb.AppendLine($"CORE        {Signed(coreDelta):0.0}%   [color=#8899aa](현재 {gs.CoreProgress:0.0}%)[/color]");
        sb.AppendLine($"MATERIAL    {Signed(materialsDelta)}   [color=#8899aa](현재 {gs.Materials})[/color]\n");
        sb.AppendLine($"사고          {incidents}");
        sb.AppendLine($"금기 위반     {(taboo > 0 ? $"[color=#ff6a55]{taboo}[/color]" : "0")}");
        sb.AppendLine($"격리          {isolated}");
        sb.AppendLine($"생존          {(aliveCount < total ? "[color=#ff6a55]" : "")}{aliveCount} / {total}{(aliveCount < total ? "[/color]" : "")}");
        _body.Text = sb.ToString();

        var evsb = new StringBuilder();
        var notable = entries
            .Where(e => e.EventType is LogEventType.TabooViolation or LogEventType.Death or LogEventType.Sabotage
                or LogEventType.TaskFailed or LogEventType.PowerOutage or LogEventType.CctvDisconnect)
            .TakeLast(3);
        bool any = false;
        foreach (var e in notable)
        {
            any = true;
            evsb.AppendLine($"{Clock(e.GameTimeSeconds)}  {Strip(e.Description)}");
        }
        _events.Text = any ? evsb.ToString() : "- 특이 사건 없음";
    }

    private static string Signed(float v) => v >= 0 ? $"+{v:0.0}" : v.ToString("0.0");
    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
    private static string Strip(string s) => s.Replace("⚠", "").Replace("🚨", "").Trim();

    private static string Clock(float s)
    {
        int totalMin = 22 * 60 + Mathf.FloorToInt(s * (480f / 300f));
        int h = (totalMin / 60) % 24;
        int m = totalMin % 60;
        return $"{h:00}:{m:00}";
    }

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
