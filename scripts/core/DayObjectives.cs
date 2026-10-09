using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.Core;

// "오늘 무엇을 해야 하는가"를 한 곳에서 답한다.
//
// 판정 규칙을 새로 만들지 않는다 — 이미 돌고 있는 값만 읽는다.
//   코어 복구율        GameState.CoreProgress   (DAY 가 바뀌어도 초기화되지 않는 누적값)
//   경고 안정화 횟수    FacilityWarningSystem.Prevented
//   고장 발생/해결 수   IncidentTracker.OpenedCount / ResolvedCount
//   작업실·직원 상태    FacilitySimulation
//
// 수치는 전부 data/objectives/dayN.tres 에 있다.
public static class DayObjectives
{
    private const string Folder = "res://data/objectives";
    // 대회용 3일 전용 목표. 기본 5일은 위 폴더를, 대회용은 이 폴더를 읽는다 —
    // 두 벌을 한 폴더에 섞으면 같은 Day 가 둘이 되어 어느 쪽이 뽑힐지 알 수 없다.
    // (ResourceDir.ListFiles 는 하위 폴더로 내려가지 않으므로 서로 간섭하지 않는다.)
    private const string CompetitionFolder = "res://data/objectives/competition";

    private static readonly List<DayPlanDef> _plans = new();
    private static readonly List<DayPlanDef> _compPlans = new();
    private static bool _loaded;

    // 업무 한 줄의 현재 상태(화면 표시용).
    public sealed class Line
    {
        public DayObjectiveDef Def;
        public float Current;
        public float Target;
        public bool Done;
        // 근무가 끝나야 확정되는 업무인가("고장 0회", "전원 생존"처럼 지금은 지키고 있을 뿐).
        // 화면에서 시작하자마자 달성한 것처럼 보이지 않게 하는 데 쓴다.
        public bool Provisional;
        public bool Required => Def?.Required ?? true;
        // "14.2 / 18%" 처럼 사람이 읽는 진행도. 셀 수 없는 업무는 빈 문자열.
        public string ProgressText = "";
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Load(Folder, _plans);
        Load(CompetitionFolder, _compPlans);
        if (_plans.Count == 0)
            GD.PushWarning($"DayObjectives: {Folder} 에서 업무 목록을 찾지 못했습니다.");
    }

    private static void Load(string folder, List<DayPlanDef> into)
    {
        foreach (string path in ResourceDir.ListFiles(folder, ".tres"))
        {
            var res = GD.Load<DayPlanDef>(path);
            if (res != null) into.Add(res);
        }
        into.Sort((a, b) => a.Day.CompareTo(b.Day));
    }

    public static DayPlanDef Today => For(GameState.Instance?.CurrentDay ?? 1);

    public static DayPlanDef For(int day)
    {
        EnsureLoaded();
        // 대회용은 전용 목록을 먼저 본다. 그 날짜가 없으면 기본 목록으로 물러난다 —
        // 전용 데이터가 빠져도 업무 목록이 통째로 비지는 않게.
        if (GameModes.Current == GameMode.Competition)
            foreach (var p in _compPlans)
                if (p.Day == day) return p;
        foreach (var p in _plans)
            if (p.Day == day) return p;
        return null;
    }

    // 이 날의 최대 근무시간(초). 업무 데이터에 없으면 전역 기본값.
    public static float MaxShiftSeconds =>
        Today is { MaxShiftSeconds: > 0f } plan
            ? plan.MaxShiftSeconds
            : Config.Instance?.Data?.DayLengthSeconds ?? 180f;

    public static float RemainingSeconds =>
        Mathf.Max(0f, MaxShiftSeconds - (GameState.Instance?.DayTimeSeconds ?? 0f));

    // --- 진행도 -----------------------------------------------------------

    public static List<Line> Lines()
    {
        var list = new List<Line>();
        var plan = Today;
        if (plan == null) return list;
        foreach (var def in plan.Objectives)
            if (def != null) list.Add(Evaluate(def));
        return list;
    }

    public static Line Evaluate(DayObjectiveDef def)
    {
        var line = new Line { Def = def, Target = def.TargetValue };
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;

        switch (def.Type)
        {
            case DayObjectiveType.CoreProgress:
                line.Current = gs?.CoreProgress ?? 0f;
                line.Done = line.Current >= def.TargetValue - 0.0001f;
                // 소수점은 보이지 않는다. 내림으로 적어서 "18 / 18%" 인데 미달성인 경우가 없게 한다.
                line.ProgressText = $"{Mathf.FloorToInt(line.Current)} / {def.TargetValue:0}%";
                break;

            case DayObjectiveType.WarningsResolved:
                line.Current = sim?.Warnings.Prevented ?? 0;
                line.Done = line.Current >= def.TargetValue;
                line.ProgressText = $"{line.Current:0} / {def.TargetValue:0}";
                break;

            case DayObjectiveType.IncidentsResolved:
                line.Current = (sim?.Warnings.Prevented ?? 0) + IncidentTracker.ResolvedCount;
                line.Done = line.Current >= def.TargetValue;
                line.ProgressText = $"{line.Current:0} / {def.TargetValue:0}";
                break;

            case DayObjectiveType.BreakdownsAtMost:
                // 적을수록 좋은 업무 — 지금까지 낸 고장 수가 한도 이하인가.
                line.Current = IncidentTracker.OpenedCount;
                line.Done = line.Current <= def.TargetValue;
                line.Provisional = true;
                line.ProgressText = $"{line.Current:0} / {def.TargetValue:0} 이하";
                break;

            case DayObjectiveType.RoomsOperational:
            {
                int ok = def.TargetRooms.Count(r => sim?.HasRepairPending(r) == false);
                line.Current = ok;
                line.Target = def.TargetRooms.Count;
                line.Done = ok >= def.TargetRooms.Count;
                line.Provisional = true;
                line.ProgressText = $"{ok} / {def.TargetRooms.Count}";
                break;
            }

            case DayObjectiveType.AllStaffAble:
            case DayObjectiveType.AllStaffAlive:
            {
                var roster = sim?.GetActiveEmployeeIds() ?? new List<string>();
                bool alive = def.Type == DayObjectiveType.AllStaffAlive;
                int ok = roster.Count(id =>
                {
                    var st = sim.GetEmployeeState(id);
                    if (st == null) return false;
                    return alive ? st.Alive : st is { Alive: true, Isolated: false, Incapacitated: false };
                });
                line.Current = ok;
                line.Target = roster.Count;
                line.Done = roster.Count > 0 && ok >= roster.Count;
                line.Provisional = true;
                line.ProgressText = $"{ok} / {roster.Count}";
                break;
            }
        }
        return line;
    }

    // --- 근무 종료 판정 ----------------------------------------------------

    public static int RequiredTotal => Lines().Count(l => l.Required);
    public static int RequiredDone => Lines().Count(l => l.Required && l.Done);

    // 필수 업무를 전부 끝냈는가. 업무가 아예 없는 날(교육 등)은 항상 끝낼 수 있다.
    public static bool CanEndShift
    {
        get
        {
            var lines = Lines();
            if (lines.Count == 0) return true;
            return lines.Where(l => l.Required).All(l => l.Done);
        }
    }

    // 닫혀 있는 버튼에 붙는 짧은 표시.
    public static string ShortStatus()
    {
        var lines = Lines();
        int total = lines.Count(l => l.Required);
        if (total == 0) return "업무목록";
        int done = lines.Count(l => l.Required && l.Done);
        return done >= total ? $"업무 {done}/{total} ✓" : $"업무 {done}/{total}";
    }

    // 이번 근무에서 달성한 선택 업무 수 = 업무평가 점수.
    public static int OptionalCompleted() => Lines().Count(l => !l.Required && l.Done);

    // ── "복구 완료" 의 기준 ────────────────────────────────────────────────
    //
    // 이번 모드의 **마지막 날 코어 목표치**다. 기본 5일은 DAY5 의 100%, 대회용 3일은
    // DAY3 의 58% — 즉 기본 모드에서는 예전과 숫자 하나 다르지 않다.
    //
    // 100% 를 상수로 박아 두면 3일짜리 모드에서는 코어 목표 곡선(18/38/58/80/100)상
    // 도달 자체가 불가능해, 엔딩 네 갈래 중 '복구 성공' 두 갈래가 영영 열리지 않는다.
    // 밸런스 수치는 그대로 두고 **기준만 그 모드의 마지막 날로** 옮긴다.
    public static float FinalCoreTarget
    {
        get
        {
            var plan = For(GameModes.MaxDays);
            if (plan == null) return 100f;
            foreach (var def in plan.Objectives)
                if (def is { Type: DayObjectiveType.CoreProgress, TargetValue: > 0f })
                    return def.TargetValue;
            return 100f;
        }
    }

    // 엔딩 · 최종 기록 · 평가 등급이 전부 이 하나를 본다.
    public static bool CoreRecovered(float coreProgress) => coreProgress >= FinalCoreTarget - 0.001f;

    // 근무를 전부 마친 뒤의 관리자 평가 등급.
    // 선택 업무는 성능 보상을 주지 않는다 — 오직 이 등급에만 반영된다.
    public static string Grade(float coreProgress, int evaluationScore)
    {
        if (!CoreRecovered(coreProgress)) return evaluationScore >= 3 ? "C" : "D";
        if (evaluationScore >= 5) return "S";
        if (evaluationScore >= 3) return "A";
        return evaluationScore >= 1 ? "B" : "C";
    }

    public static void Reload()
    {
        _plans.Clear();
        _compPlans.Clear();
        _loaded = false;
        EnsureLoaded();
    }
}
