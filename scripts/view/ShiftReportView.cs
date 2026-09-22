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
            Size = new Vector2(752, 440),
            BbcodeEnabled = true,
            ScrollActive = false,
        };
        _body.AddThemeFontOverride("normal_font", _font);
        _body.AddThemeFontSizeOverride("normal_font_size", ViewFont.S(22));
        _body.AddThemeColorOverride("default_color", Ink);
        AddChild(_body);

        // '주요 사건' 목록은 없앴다 — 같은 내용이 L 키 시설 로그에 그대로 남아 있고,
        // 여기서는 숫자만 보고 넘어가는 편이 읽기 쉽다.

        var btn = MonitorUi.Button("계속 ▶", Ink, _font, () => ContinueRequested?.Invoke(), ViewFont.S(23));
        btn.Position = new Vector2(548, 522);
        btn.Size = new Vector2(228, 60);
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
        // 실제로 '발생한' 사고 건수. 사고의 파생 결과나 주기적 손실 로그는 세지 않는다.
        int incidents = IncidentTracker.OpenedCount;
        var roster = sim.GetActiveEmployeeIds();
        int isolated = roster.Count(id => sim.GetEmployeeState(id)?.Isolated == true);
        int aliveCount = roster.Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        int total = roster.Count;

        float coreDelta = gs.CoreProgress - coreAtStart;
        int materialsDelta = gs.Materials - materialsAtStart;

        var sb = new StringBuilder();
        // 필수 업무를 못 끝냈으면 머리글부터 다르다 — "끝났다"와 "못 끝냈다"는 다른 결과다.
        int missed = gs.LastShiftMissedRequired;
        sb.AppendLine(missed > 0
            ? $"[font_size=26][color=#ff6b5a]{DayFeatures.DayLabel(gs.CurrentDay)} — 필수 업무 미달성[/color][/font_size]"
            : $"[font_size=26]{DayFeatures.DayLabel(gs.CurrentDay)} — SHIFT COMPLETE[/font_size]");
        if (missed > 0)
            sb.AppendLine($"[color=#ff9a8a]근무 시간이 끝났습니다. 끝내지 못한 필수 업무 {missed}건.[/color]");
        sb.AppendLine("");
        sb.AppendLine($"CORE        {Signed(coreDelta)}%   [color=#8899aa](현재 {gs.CoreProgress:0.0}%)[/color]");
        sb.AppendLine($"MATERIAL    {Signed(materialsDelta)}   [color=#8899aa](현재 {gs.Materials})[/color]\n");
        // 이번 근무의 경고 대응 성적 — 배치 판단이 실제로 통했는지가 여기서 드러난다.
        var warn = FacilitySimulation.Instance?.Warnings;
        if (warn != null && warn.Raised > 0)
            sb.AppendLine($"경고          {warn.Raised}건 " +
                          $"[color=#88ddaa]막음 {warn.Prevented}[/color] · " +
                          $"{(warn.Failed > 0 ? $"[color=#ff6a55]놓침 {warn.Failed}[/color]" : "놓침 0")}");
        sb.AppendLine($"사고          {incidents}");
        sb.AppendLine($"금기 위반     {(taboo > 0 ? $"[color=#ff6a55]{taboo}[/color]" : "0")}");
        sb.AppendLine($"격리          {isolated}");
        sb.AppendLine($"생존          {(aliveCount < total ? "[color=#ff6a55]" : "")}{aliveCount} / {total}{(aliveCount < total ? "[/color]" : "")}");

        // 오늘의 업무 결과. 못 끝낸 업무가 있어도 다음 날로 넘어간다 — 다만
        // 계속 놓치면 DAY5 까지 코어 100% 를 못 채운다는 게 그대로 보인다.
        var lines = DayObjectives.Lines();
        if (lines.Count > 0)
        {
            sb.AppendLine("\n[color=#8899aa]오늘의 업무[/color]");
            foreach (var l in lines)
            {
                string mark = l.Required ? (l.Done ? "☑" : "□") : (l.Done ? "★" : "☆");
                string state = l.Done
                    ? "[color=#88ddaa]완료[/color]"
                    : (l.Required ? "[color=#ffb347]미달성[/color]" : "[color=#8899aa]미달성[/color]");
                sb.AppendLine($"{mark} {l.Def.DisplayText}   [color=#8899aa]{l.ProgressText}[/color]   {state}");
            }
            int earned = lines.Count(l => !l.Required && l.Done);
            if (earned > 0)
                sb.AppendLine($"[color=#88ddaa]업무평가 +{earned}[/color]" +
                              $"   [color=#8899aa](누적 {gs.EvaluationScore})[/color]");
        }
        _body.Text = sb.ToString();
        FitBody();
    }

    // 내용이 길면(업무 줄이 많은 날) 아래쪽이 잘리지 않게 글자를 줄이고, 그래도 넘치면 스크롤을 켠다.
    private async void FitBody()
    {
        int size = ViewFont.S(22), min = ViewFont.S(15);
        _body.ScrollActive = false;
        _body.AddThemeFontSizeOverride("normal_font_size", size);
        for (int guard = 0; guard < 16; guard++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(_body)) return;
            if (_body.GetContentHeight() <= _body.Size.Y || size <= min) break;
            size--;
            _body.AddThemeFontSizeOverride("normal_font_size", size);
        }
        if (IsInstanceValid(_body) && _body.GetContentHeight() > _body.Size.Y) _body.ScrollActive = true;
    }

    private static string Signed(float v) => v >= 0 ? $"+{v:0.0}" : v.ToString("0.0");
    private static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();
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
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }
}
