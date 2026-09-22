using System;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 엔딩 뒤 「5일간의 근무 기록」 — 성적표. 엔딩은 코어 100% 하나로만 갈렸고,
// 생존자 · 금기 · 사고 · 방해자 격리 · 업무 점수는 여기 기록으로만 남는다.
// (예전 PROTOTYPE RESULT 화면을 대신한다. 숫자는 GameState 의 5일 누적을 읽기만 한다.)
public partial class EndingRecordOverlay : Control
{
    public event Action TitleRequested;

    private static readonly Color Ink = new(0.84f, 0.92f, 0.90f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.50f, 0.60f, 0.64f);
    private static readonly Color Warn = new(1f, 0.55f, 0.45f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;
        MouseFilter = MouseFilterEnum.Stop;
        Modulate = new Color(1, 1, 1, 0);

        var bg = new ColorRect { Color = new Color(0.01f, 0.015f, 0.02f, 1f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 화면 한가운데 — CenterContainer 가 창 크기를 따라 패널을 가운데에 둔다.
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.08f, 0.10f, 0.92f),
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 48, ContentMarginRight = 48, ContentMarginTop = 36, ContentMarginBottom = 32,
        });
        center.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);

        col.AddChild(Lbl("5일간의 근무 기록", 26, Cyan, HorizontalAlignment.Center));
        col.AddChild(Lbl("NIGHT SHIFT RECORD  ·  DAY 1 – 5", 13, Dim, HorizontalAlignment.Center));
        col.AddChild(new HSeparator());

        var gs = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        var roster = sim?.GetActiveEmployeeIds().ToList() ?? new System.Collections.Generic.List<string>();
        int alive = roster.Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        float core = Mathf.Min(gs?.CoreProgress ?? 0f, 100f);
        int score = gs?.EvaluationScore ?? 0;
        bool isolated = sim?.IsSaboteurIsolated() ?? false;
        int missed = gs?.MissedRequiredDays ?? 0;

        Row(col, "봉쇄 코어", $"{core:0.0}%", core >= 100f ? Cyan : Warn);
        Row(col, "생존 직원", $"{alive} / {roster.Count}", alive < roster.Count ? Warn : Ink);
        Row(col, "금기 위반", $"{gs?.TotalTabooViolations ?? 0}", Ink);
        Row(col, "발생 사고", $"{gs?.TotalIncidents ?? 0}", Ink);
        Row(col, "방해공작자", isolated ? "격리 완료" : "미확인", isolated ? Cyan : Warn);
        Row(col, "필수 업무 미달성", $"{missed}일", missed > 0 ? Warn : Ink);
        Row(col, "업무 평가", $"{score}점", Ink);
        col.AddChild(new HSeparator());
        Row(col, "관리자 평가", DayObjectives.Grade(gs?.CoreProgress ?? 0f, score), Cyan, 30);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        var btn = MonitorUi.Button("[ 타이틀로 ]", Cyan, ViewFont.Default, () => TitleRequested?.Invoke(), ViewFont.FS(18));
        btn.CustomMinimumSize = new Vector2(260, 52);
        btn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        col.AddChild(btn);

        var t = CreateTween();
        t.TweenProperty(this, "modulate:a", 1f, 0.9);
    }

    private static void Row(VBoxContainer col, string label, string value, Color valueCol, int size = 20)
    {
        var row = new HBoxContainer();
        var l = Lbl(label, size - 2, Dim, HorizontalAlignment.Left);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(l);
        row.AddChild(Lbl(value, size, valueCol, HorizontalAlignment.Right));
        col.AddChild(row);
    }

    private static Label Lbl(string t, int size, Color c, HorizontalAlignment align)
    {
        var l = new Label { Text = t, HorizontalAlignment = align };
        l.AddThemeFontOverride("font", ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }
}
