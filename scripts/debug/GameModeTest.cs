using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 게임 모드(대회용 3일 / 기본 5일 / 하드 잠김) 자동 검증.
//
//   godot --headless --path . res://scenes/debug/GameModeTest.tscn --quit-after 1200
//
// 보는 것은 네 가지다.
//   ① 모드가 바꾸는 것이 **진행 일수와 결번 배정 둘뿐**인가
//   ② 마지막 날 판정이 모드를 따라가는가 (대회용 DAY3 = 기본 DAY5)
//   ③ '복구 완료' 기준이 그 모드의 마지막 날 목표치인가 — 기본 모드는 예전과 똑같이 100%
//   ④ 모드를 바꿔 새 게임을 시작하면 이전 모드의 규칙이 따라오지 않는가
public partial class GameModeTest : Node
{
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 게임 모드 검증 ################");
        var keep = GameModes.Current;

        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        SectionF();

        GameModes.ForceSelect(keep);
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 모드 정의 ────────────────────────────────────────────────

    private void SectionA()
    {
        Head("A", "모드 정의와 잠금");
        Ok(GameModes.IsPlayable(GameMode.Competition), "대회용 VER 는 고를 수 있다");
        Ok(GameModes.IsPlayable(GameMode.Standard) == GameModes.StandardUnlocked,
            $"기본 모드 선택 가능 여부가 플래그를 따른다 (StandardUnlocked={GameModes.StandardUnlocked})");
        Ok(!GameModes.IsPlayable(GameMode.Hard), "하드 모드는 잠겨 있다");
        Ok(!GameModes.HardUnlocked, "하드 모드 해금 플래그는 꺼져 있다");

        GameModes.ForceSelect(GameMode.Standard);
        GameModes.Select(GameMode.Hard);
        Ok(GameModes.Current == GameMode.Standard, "잠긴 모드는 Select 해도 선택되지 않는다");

        Ok(GameModes.DaysFor(GameMode.Competition) == 3, $"대회용 {GameModes.DaysFor(GameMode.Competition)}일");
        Ok(GameModes.DaysFor(GameMode.Standard) == 5, $"기본 모드 {GameModes.DaysFor(GameMode.Standard)}일");
        Ok(Config.Instance.Data.CompetitionDays == 3 && Config.Instance.Data.MaxDays == 5,
            "일수는 코드가 아니라 data/config.tres 에서 온다");
    }

    // ── B. 마지막 날 판정 ───────────────────────────────────────────

    private void SectionB()
    {
        Head("B", "마지막 날이 모드를 따라간다");

        GameModes.ForceSelect(GameMode.Competition);
        Ok(GameModes.MaxDays == 3, "대회용 MaxDays = 3");
        Ok(!IsFinal(2) && IsFinal(3), "대회용은 DAY2 가 아니라 DAY3 이 마지막");
        Ok(IsFinal(4), "DAY4 로 넘어갈 일 자체가 없다(이미 마지막을 지났다)");
        Ok(ShiftCardView.ShieldHoursLeft(1) == 48, $"DAY1 남은 봉쇄 시간 {ShiftCardView.ShieldHoursLeft(1)}시간");

        GameModes.ForceSelect(GameMode.Standard);
        Ok(GameModes.MaxDays == 5, "기본 모드 MaxDays = 5");
        Ok(!IsFinal(3) && !IsFinal(4) && IsFinal(5), "기본 모드는 DAY5 가 마지막 — DAY3 에서 끝나지 않는다");
        Ok(ShiftCardView.ShieldHoursLeft(1) == 96, $"DAY1 남은 봉쇄 시간 {ShiftCardView.ShieldHoursLeft(1)}시간");

        // 마지막 날 휴게시간 — 대회용만 한 번 더 거친다.
        GameModes.ForceSelect(GameMode.Standard);
        Ok(!GameModes.FinalDayRest && SkipsRest(5),
            "기본 모드 DAY5 는 예전처럼 보고서 → 최종 보고서 (휴게시간 없음)");
        Ok(!SkipsRest(4), "기본 모드 DAY4 는 당연히 휴게시간을 거친다");

        GameModes.ForceSelect(GameMode.Competition);
        Ok(GameModes.FinalDayRest && !SkipsRest(3),
            "대회용 DAY3 은 휴게시간을 거친 뒤 최종 보고서로 간다");
        Ok(GoesToVerdictFromRest(3) && !GoesToVerdictFromRest(2),
            "그 휴게시간의 [최종 결과 확인 ▶] 이 최종 보고서로 잇는다");
    }

    // ShiftFlowController 의 두 분기와 같은 식.
    private static bool SkipsRest(int day) => day >= GameModes.MaxDays && !GameModes.FinalDayRest;
    private static bool GoesToVerdictFromRest(int day) => day >= GameModes.MaxDays;

    // ShiftFlowController 가 쓰는 것과 같은 식.
    private static bool IsFinal(int day) => day >= GameModes.MaxDays;

    // ── C. 결번 배정 ────────────────────────────────────────────────

    private void SectionC()
    {
        Head("C", "결번 — 대회용 고정 · 기본 모드 무작위");
        var gs = GameState.Instance;
        var roster = _sim.GetActiveEmployeeIds().ToList();
        Ok(roster.Contains(GameModes.FixedSaboteurId), $"명단에 고정 대상이 있다 (직원 {roster.Count}명)");

        GameModes.ForceSelect(GameMode.Competition);
        bool allFixed = true;
        for (int i = 0; i < 20; i++)
        {
            gs.SetSaboteur("");
            gs.AssignSaboteurForMode(roster);
            if (gs.SaboteurEmployeeId != GameModes.FixedSaboteurId) allFixed = false;
        }
        Ok(allFixed, "대회용은 20번 돌려도 매번 같은 직원으로 고정된다");

        GameModes.ForceSelect(GameMode.Standard);
        var seen = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            gs.SetSaboteur("");
            gs.AssignSaboteurForMode(roster);
            seen.Add(gs.SaboteurEmployeeId);
        }
        Ok(seen.Count > 1, $"기본 모드는 무작위로 돌아온다 (60회에 {seen.Count}명 등장)");
        Ok(seen.All(roster.Contains), "뽑힌 결번은 전부 실제 명단 안의 직원이다");

        // 고정 대상이 이번 근무 명단에 없으면 무작위로 물러난다(결번이 빈 판을 만들지 않는다).
        GameModes.ForceSelect(GameMode.Competition);
        var without = roster.Where(id => id != GameModes.FixedSaboteurId).ToList();
        gs.SetSaboteur("");
        gs.AssignSaboteurForMode(without);
        Ok(!string.IsNullOrEmpty(gs.SaboteurEmployeeId) && without.Contains(gs.SaboteurEmployeeId),
            "고정 대상이 명단에 없으면 남은 인원 중에서 뽑는다");
    }

    // ── D. '복구 완료' 기준 ─────────────────────────────────────────

    private void SectionD()
    {
        Head("D", "복구 완료 기준 = 그 모드 마지막 날 목표치");

        GameModes.ForceSelect(GameMode.Standard);
        float std = DayObjectives.FinalCoreTarget;
        Ok(Mathf.IsEqualApprox(std, 100f), $"기본 모드 기준 {std:0}% — 예전과 같다");
        Ok(DayObjectives.CoreRecovered(100f) && !DayObjectives.CoreRecovered(99.9f),
            "기본 모드는 100% 에서만 복구 완료");

        Ok(CoreTargetOf(1) == 18f && CoreTargetOf(2) == 38f && CoreTargetOf(3) == 58f,
            "기본 모드 목표 곡선 18 / 38 / 58 — 손대지 않았다");

        GameModes.ForceSelect(GameMode.Competition);
        Ok(CoreTargetOf(1) == 30f && CoreTargetOf(2) == 65f && CoreTargetOf(3) == 100f,
            "대회용 목표 곡선 30 / 65 / 100 (전용 폴더에서 읽는다)");
        float comp = DayObjectives.FinalCoreTarget;
        Ok(Mathf.IsEqualApprox(comp, 100f), $"대회용 기준 {comp:0}% = DAY3 필수 목표");
        Ok(DayObjectives.CoreRecovered(comp) && !DayObjectives.CoreRecovered(comp - 1f),
            "대회용은 그 목표치에서 복구 완료");
        Ok(DayObjectives.For(4) == null || DayObjectives.For(4).Day == 4,
            "대회용에 DAY4 전용 목표는 없다(쓰이지도 않는다)");

        // 평가 등급 — 계산 규칙(선택 업무 점수 구간)은 그대로다. 기준점만 모드를 따른다.
        GameModes.ForceSelect(GameMode.Standard);
        Ok(DayObjectives.Grade(100f, 5) == "S" && DayObjectives.Grade(100f, 3) == "A"
           && DayObjectives.Grade(100f, 0) == "C" && DayObjectives.Grade(92f, 5) == "C"
           && DayObjectives.Grade(92f, 0) == "D",
            "기본 모드 등급표가 예전과 한 칸도 다르지 않다 (S/A/C/C/D)");

        GameModes.ForceSelect(GameMode.Competition);
        Ok(DayObjectives.Grade(comp, 5) == "S" && DayObjectives.Grade(comp, 3) == "A",
            "대회용도 목표를 채우면 S · A 가 나온다");
        Ok(DayObjectives.Grade(comp - 1f, 5) == "C", "목표에 못 미치면 예전처럼 C 아래로");

        MeasureCompetitionCore();
    }

    // 대회용 3일을 실제로 돌려 코어가 어디까지 가는지 **재어 본다.**
    //
    // 판정이 아니라 보고다(난수가 섞여 판마다 흔들린다). 목표 곡선을 30/65/100 으로
    // 세워 두었는데 수율은 5일짜리 그대로이므로, 사흘 안에 100% 에 닿는지 아닌지를
    // 숫자로 남겨 두어야 다음 밸런스 단계에서 얼마나 올릴지 정할 수 있다.
    private void MeasureCompetitionCore()
    {
        const float step = 1f / 30f;
        GameModes.ForceSelect(GameMode.Competition);
        var plans = new (string Label, Dictionary<string, int> Crew)[]
        {
            ("코어 집중 (코어3 발전1 정비1 저장1)", Crew(core: 3, power: 1, maint: 1, guard: 0, storage: 1)),
            ("안정형   (코어2 발전1 정비1 경비1 저장1)", Crew(core: 2, power: 1, maint: 1, guard: 1, storage: 1)),
        };

        GD.Print("   ── 대회용 3일 실제 복구량(각 3판 평균) ──────────────");
        foreach (var (label, crew) in plans)
        {
            float sum = 0f;
            const int runs = 3;
            for (int r = 0; r < runs; r++)
            {
                GameState.Instance.ResetRun(1);
                _sim.ResetRun();
                EventLog.Instance.ClearAll();
                IncidentTracker.Reset();
                for (int day = 1; day <= GameModes.MaxDays; day++)
                {
                    GameState.Instance.SetPhase(GamePhase.Schedule);
                    var roster = _sim.GetActiveEmployeeIds().ToList();
                    int idx = 0;
                    foreach (var (room, n) in crew)
                        for (int i = 0; i < n && idx < roster.Count; i++, idx++)
                            _sim.AssignToRoom(roster[idx], room);
                    GameState.Instance.AssignSaboteurForMode(roster);
                    _sim.ResetForNewShift();
                    GameState.Instance.SetPhase(GamePhase.Live);

                    float length = DayObjectives.MaxShiftSeconds;
                    for (float t = 0f; t < length; t += step)
                    {
                        GameState.Instance.AdvanceDayTime(step);
                        _sim.Tick(step);
                    }
                    if (day < GameModes.MaxDays) GameState.Instance.GoToNextDay();
                }
                sum += GameState.Instance.CoreProgress;
            }
            float avg = sum / runs;
            GD.Print($"      {label,-34} 3일 뒤 코어 {avg,5:0.0}%  (목표 100%{(avg >= 100f ? "" : $" · {100f - avg:0.0}%p 모자람")})");
        }
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
    }

    private static Dictionary<string, int> Crew(int core, int power, int maint, int guard, int storage) =>
        new()
        {
            ["core_room"] = core, ["power_room"] = power, ["maintenance_room"] = maint,
            ["guard_room"] = guard, ["storage_room"] = storage,
        };

    private static float CoreTargetOf(int day)
    {
        var plan = DayObjectives.For(day);
        if (plan == null) return -1f;
        foreach (var def in plan.Objectives)
            if (def is { Type: DayObjectiveType.CoreProgress, TargetValue: > 0f }) return def.TargetValue;
        return -1f;
    }

    // ── E. 엔딩 네 갈래가 두 모드 모두에서 열리는가 ──────────────────

    private void SectionE()
    {
        Head("E", "엔딩 4분기");
        foreach (var mode in new[] { GameMode.Standard, GameMode.Competition })
        {
            GameModes.ForceSelect(mode);
            float goal = DayObjectives.FinalCoreTarget;
            var kinds = new List<EndingState.Kind>();
            foreach (float core in new[] { goal, goal - 5f })
                foreach (bool caught in new[] { true, false })
                {
                    // EndingDirector.RunAsync 와 같은 식.
                    bool recovered = DayObjectives.CoreRecovered(core);
                    kinds.Add(recovered
                        ? (caught ? EndingState.Kind.True : EndingState.Kind.Loose)
                        : (caught ? EndingState.Kind.Late : EndingState.Kind.Bad));
                }
            Ok(kinds.Distinct().Count() == 4,
                $"{GameModes.DisplayName(mode)}({goal:0}% 기준) — True · Loose · Late · Bad 네 갈래가 전부 나온다");
        }
    }

    // ── F. 모드를 바꿔 새 게임을 시작해도 섞이지 않는가 ──────────────

    private void SectionF()
    {
        Head("F", "모드 전환 시 이전 판이 따라오지 않는다");
        var gs = GameState.Instance;
        var roster = _sim.GetActiveEmployeeIds().ToList();

        // ① 대회용으로 한 판 — 결번 고정 · 3일.
        GameModes.ForceSelect(GameMode.Competition);
        gs.ResetRun(1);
        _sim.ResetRun();
        gs.AssignSaboteurForMode(_sim.GetActiveEmployeeIds());
        gs.AddCoreProgress(40f, "검사");
        gs.AddEvaluation(2);
        Ok(gs.SaboteurEmployeeId == GameModes.FixedSaboteurId && GameModes.MaxDays == 3,
            "대회용 한 판 — 결번 고정 · 3일");

        // ② 타이틀로 나가 기본 모드로 새 게임.
        GameModes.ForceSelect(GameMode.Standard);
        gs.ResetRun(1);
        _sim.ResetRun();
        Ok(GameModes.MaxDays == 5, "기본 모드로 바꾸면 다시 5일");
        Ok(Mathf.IsZeroApprox(gs.CoreProgress) && gs.EvaluationScore == 0 && gs.SaboteurEmployeeId == "",
            "이전 판의 코어 · 업무평가 · 결번이 남지 않는다");

        var seen = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            gs.SetSaboteur("");
            gs.AssignSaboteurForMode(roster);
            seen.Add(gs.SaboteurEmployeeId);
        }
        Ok(seen.Count > 1, "대회용을 하고 왔어도 기본 모드 결번은 다시 무작위다");

        // ③ 반대 방향 — 기본 모드 뒤에 대회용.
        GameModes.ForceSelect(GameMode.Competition);
        gs.ResetRun(1);
        gs.SetSaboteur("");
        gs.AssignSaboteurForMode(roster);
        Ok(gs.SaboteurEmployeeId == GameModes.FixedSaboteurId && GameModes.MaxDays == 3,
            "기본 모드를 하고 와도 대회용은 다시 고정 · 3일");

        // ④ 타이틀 단말기 목록.
        Head("F", "타이틀 근무 유형 화면");
        var items = TitleTerminalView.ModeItems;
        Ok(items.Length == 3, $"선택지 3개 ({items.Length})");
        Ok(items[0].Label == "대회용 VER" && items[1].Label == "기본 모드" && items[2].Label == "하드 모드",
            "대회용 VER / 기본 모드 / 하드 모드 순서");
        Ok(items[0].Sub.StartsWith("3 DAYS") && items[1].Sub.StartsWith("5 DAYS")
           && items[2].Sub == "ACCESS DENIED",
            "보조 문구에 실제 일수가 붙는다 (3 DAYS / 5 DAYS / ACCESS DENIED)");

        var term = new TitleTerminalView();
        AddChild(term);
        term.ShowModeSelect();
        Ok(term.CurrentMode == TitleTerminalView.Mode.ModeSelect, "근무 유형 선택 화면으로 전환된다");
        Ok(GameModes.IsPlayable(term.SelectedMode), "커서는 고를 수 있는 항목에서 시작한다");
        Ok(term.MoveCursor(1) && term.MoveCursor(1) && term.SelectedMode == GameMode.Hard,
            "↓ 로 하드 모드까지 내려갈 수는 있다");
        Ok(!GameModes.IsPlayable(term.SelectedMode), "다만 하드 모드는 실행 불가로 판정된다");
        Ok(term.ItemAt(new Vector2(300f, 300f)) == "mode_comp", "첫 칸을 마우스로 집을 수 있다");
        term.ShowMenu();
        Ok(term.CurrentMode == TitleTerminalView.Mode.Menu, "ESC 복귀 = 기존 메뉴 화면");
        Ok(TitleTerminalView.MenuItems.Length == 4 && TitleTerminalView.MenuItems[0].Id == "start",
            "기존 타이틀 메뉴(근무 개시 · 기록 열람 · 환경 설정 · 시스템 종료)는 그대로다");
        term.QueueFree();
    }

    // ── 도구 ────────────────────────────────────────────────────────

    private void Head(string tag, string title) => GD.Print($"\n===== [{tag}] {title} =====");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
