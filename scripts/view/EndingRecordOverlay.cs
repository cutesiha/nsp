using System;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 엔딩 뒤 「N일간의 근무 기록」 — 성적표. 엔딩은 코어 복구 × 최종 보고서의 지목으로 갈리고,
// 생존자 · 사고 · 업무 기록은 여기에만 남는다. 등급 규칙은 DayObjectives.Grade 하나다.
// (예전 PROTOTYPE RESULT 화면을 대신한다. 숫자는 GameState 의 5일 누적을 읽기만 한다.)
public partial class EndingRecordOverlay : Control
{
    public event Action TitleRequested;

    // 어떤 엔딩으로 끝났는가 — 맨 윗줄의 이름에만 쓴다.
    public EndingState.Kind Kind { get; set; } = EndingState.Kind.None;

    // 이 성적표가 떠 있는가. 엔딩 연출은 여기서 끝난 셈이라, 미뤄 두었던
    // 도전과제 팝업(AchievementToast)이 이 화면부터 뜬다.
    public static bool IsShown { get; private set; }

    private static string EndingName(EndingState.Kind k) => k switch
    {
        EndingState.Kind.True => "근무 종료",
        EndingState.Kind.Loose => "복구 완료, 대상 미확인",
        EndingState.Kind.Late => "대상 확인, 복구 실패",
        EndingState.Kind.Bad => "복구 실패",
        _ => "종료 기록",
    };

    private static readonly Color Ink = new(0.84f, 0.92f, 0.90f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.50f, 0.60f, 0.64f);
    private static readonly Color Warn = new(1f, 0.55f, 0.45f);
    // F 등급 성적표 — 화면 전체가 붉은 결로 바뀐다.
    private static readonly Color FailRed = new(1f, 0.30f, 0.26f);
    private static readonly Color FailDim = new(0.66f, 0.36f, 0.34f);

    public override void _ExitTree() => IsShown = false;

    public override void _Ready()
    {
        IsShown = true;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;
        MouseFilter = MouseFilterEnum.Stop;
        Modulate = new Color(1, 1, 1, 0);

        var bg = new ColorRect { Color = new Color(0.01f, 0.015f, 0.02f, 1f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 화면 한가운데 — CenterContainer 가 창 크기를 따라 패널을 가운데에 둔다.
        // 등급을 먼저 정한다 — F 면 성적표 전체가 붉어진다.
        string grade = DayObjectives.FinalGrade(Kind);
        bool failed = grade == "F";
        // 강조색 하나로 화면 전체의 결을 바꾼다. 실패 기록은 청록 대신 붉은 계열이다.
        var accent = failed ? FailRed : Cyan;

        if (failed) bg.Color = new Color(0.035f, 0.006f, 0.008f, 1f);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = failed ? new Color(0.13f, 0.025f, 0.03f, 0.94f) : new Color(0.03f, 0.08f, 0.10f, 0.92f),
            BorderColor = accent with { A = failed ? 0.85f : 0.55f },
            BorderWidthLeft = failed ? 2 : 1, BorderWidthTop = failed ? 2 : 1,
            BorderWidthRight = failed ? 2 : 1, BorderWidthBottom = failed ? 2 : 1,
            ContentMarginLeft = 48, ContentMarginRight = 48, ContentMarginTop = 36, ContentMarginBottom = 32,
        });
        center.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);

        col.AddChild(Lbl($"{GameModes.MaxDays}일간의 근무 기록", 26, accent, HorizontalAlignment.Center));
        col.AddChild(Lbl($"NIGHT SHIFT RECORD / DAY 1-{GameModes.MaxDays}", 13, failed ? FailDim : Dim,
            HorizontalAlignment.Center));
        col.AddChild(new HSeparator());

        var gs = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        var roster = sim?.GetActiveEmployeeIds().ToList() ?? new System.Collections.Generic.List<string>();
        int alive = roster.Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        float core = Mathf.Min(gs?.CoreProgress ?? 0f, 100f);
        int score = gs?.EvaluationScore ?? 0;
        int missed = gs?.MissedRequiredDays ?? 0;
        bool caught = gs?.WasCaught ?? false;
        string accused = gs?.FinalAccusedId ?? "";
        string accusedName = string.IsNullOrEmpty(accused)
            ? "지목 없음"
            : sim?.GetEmployeeDef(accused)?.Codename ?? accused;

        Row(col, "봉쇄 코어", $"{core:0.0}%", DayObjectives.CoreRecovered(core) ? accent : Warn);
        Row(col, "생존 직원", $"{alive} / {roster.Count}", alive < roster.Count ? Warn : Ink);
        // 금기 위반 줄은 없앴다 — 금기 계통 자체가 게임에서 빠졌다.
        Row(col, "발생 사고", $"{gs?.TotalIncidents ?? 0}", Ink);
        Row(col, "지목 대상", accusedName, caught ? accent : Warn);
        // 맞혔는가 · 틀렸는가 둘 뿐이다. 근거가 맞았는지는 따로 적지 않는다.
        Row(col, "지목 결과", caught ? "정확" : "틀림", caught ? accent : Warn);
        Row(col, "필수 업무 미달성", $"{missed}일", missed > 0 ? Warn : Ink);
        Row(col, "업무 평가", $"{score}점", Ink);
        col.AddChild(new HSeparator());
        Row(col, "종료 기록", EndingName(Kind), failed ? FailDim : Dim, 20);
        Row(col, "관리자 평가", grade, accent, 30);

        // 엔딩 수집 — 네 엔딩이 "코어 복구 × 지목" 두 축으로 갈린다는 것을 여기서 보여 준다.
        // 이번 엔딩은 강조, 본 엔딩은 이름표, 못 본 엔딩은 「? ? ?」(조건은 타이틀 모니터2 엔딩 칸에서 볼 수 있다).
        col.AddChild(new HSeparator());
        Row(col, "엔딩 수집", $"{EndingState.SeenCount} / {EndingState.Catalog.Length}", Ink, 18);
        var slots = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        slots.AddThemeConstantOverride("separation", 14);
        foreach (var e in EndingState.Catalog)
        {
            bool now = e.Kind == Kind;
            bool got = EndingState.Seen(e.Kind);
            var l = Lbl(got ? e.Tag : "? ? ?", now ? 18 : 15, now ? accent : got ? Ink : Dim, HorizontalAlignment.Center);
            l.CustomMinimumSize = new Vector2(130, 0);
            l.TooltipText = got ? $"{e.Tag} END: {e.Name}\n{e.Rule}" : $"아직 보지 못한 엔딩\n{e.Rule}";
            l.MouseFilter = MouseFilterEnum.Pass;
            slots.AddChild(l);
        }
        col.AddChild(slots);
        var rule = EndingState.Catalog.FirstOrDefault(e => e.Kind == Kind);
        if (rule != null) col.AddChild(Lbl($"이번 엔딩의 조건: {rule.Rule}", 13, failed ? FailDim : Dim, HorizontalAlignment.Center));

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        var btn = MonitorUi.Button("[ 타이틀로 ]", accent, ViewFont.Default, () => TitleRequested?.Invoke(), ViewFont.FS(18));
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
