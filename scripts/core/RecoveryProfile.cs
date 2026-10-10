using Godot;
using NSP.Data;

namespace NSP.Core;

// 지금 판에 걸린 코어 복구 난이도. OpsProfile 과 같은 방식의 정적 창구다.
//
//   RecoveryCycle 1 → campaign1 (스토리)
//                 2 → standard2
//                 3 이상 → hard3
//
// 숫자는 전부 data/recovery/*.tres 에 있다. 여기에는 "어느 표를 쓰는가" 와
// "뒤처짐 보정을 얼마나 거는가" 만 있다.
public static class RecoveryProfile
{
    private const string Dir = "res://data/recovery/";
    private static readonly System.Collections.Generic.Dictionary<string, RecoveryProfileDef> _cache = new();

    // 몇 번째 5일인가. 1 = 첫 5일(스토리 캠페인).
    public static int Cycle => GameState.Instance?.RecoveryCycle ?? 1;

    public static RecoveryProfileDef Current => ForCycle(Cycle);

    public static RecoveryProfileDef ForCycle(int cycle)
    {
        string id = cycle <= 1 ? "campaign1" : cycle == 2 ? "standard2" : "hard3";
        if (_cache.TryGetValue(id, out var hit) && hit != null) return hit;

        string path = Dir + id + ".tres";
        var def = ResourceLoader.Exists(path) ? GD.Load<RecoveryProfileDef>(path) : null;
        if (def == null)
        {
            GD.PushWarning($"RecoveryProfile: 복구 난이도 표를 찾지 못했습니다: {path}");
            def = new RecoveryProfileDef();   // 코드 기본값(1차와 같은 값)으로 버틴다
        }
        _cache[id] = def;
        return def;
    }

    // --- 코어 복구 ----------------------------------------------------------

    // 코어실 인원수에 걸리는 복구 배율. ops 표의 Efficiency 를 대신한다.
    public static float CoreStaffMultiplier(int headcount) => Current.CoreStaffMultiplier(headcount);

    // 코어 복구 1회가 먹는 자재 배율.
    public static float MaterialCostMultiplier => Mathf.Max(0.05f, Current.CoreMaterialCostMultiplier);

    // 방해공작이 깎는 코어 % 배율.
    public static float SabotageDamageMultiplier => Mathf.Max(0f, Current.SabotageCoreDamageMultiplier);

    // 코어실 경고를 놓쳤을 때의 손실 배율.
    public static float InstabilityLossMultiplier => Mathf.Max(0f, Current.CoreInstabilityLossMultiplier);

    // 코어 복구 속도 자체에 걸리는 배율.
    // 속도 배율 × 그 DAY 의 배율. 하루 상한(cap)은 만들지 않는다 — 잘하면 앞설 수 있다.
    public static float RateMultiplier =>
        Mathf.Max(0.02f, Current.CoreRateMultiplier * Current.DayMultiplier(GameState.Instance?.CurrentDay ?? 1));

    // 자재 1개가 사 오는 복구량 배율.
    public static float YieldMultiplier => Mathf.Max(0.1f, Current.CoreYieldMultiplier);

    // --- 기반 경제 ----------------------------------------------------------

    // 스트레스 증가량 배율(감소에는 걸지 않는다 — 치료는 아래 값이 따로 맡는다).
    public static float StressGainMultiplier => Mathf.Max(0.05f, Current.StressGainMultiplier);
    // 의무실 치료량 배율.
    public static float MedicalRecoveryMultiplier => Mathf.Max(0.1f, Current.MedicalStressRecoveryMultiplier);
    // 정비실 자재 생산량 배율.
    public static float MaterialProductionMultiplier => Mathf.Max(0.1f, Current.MaterialProductionMultiplier);

    // 자재 보관 한도의 하한(0 = 바닥 없음).
    public static int MaterialsCapFloor => Mathf.Max(0, Current.MaterialsCapFloor);

    // --- 뒤처짐 보정 --------------------------------------------------------

    // 오늘 종료 시점의 권장 복구율(실패선이 아니다).
    public static float RecommendedToday => Current.RecommendedAt(GameState.Instance?.CurrentDay ?? 1);

    // 권장치보다 몇 %p 뒤처져 있는가(앞서 있으면 0).
    public static float BehindBy()
    {
        float have = GameState.Instance?.CoreProgress ?? 0f;
        return Mathf.Max(0f, RecommendedToday - have);
    }

    // 코어 복구 속도에만 곱하는 보정. **다른 어떤 판정에도 쓰지 않는다**
    // (직원 능력 · 스트레스 · 사고 확률 · 괴물 · 방해공작 발생률 · 추리 정보).
    //
    // 정상 범위로 돌아오면 단계가 내려가며 자연히 1.0 이 된다 — 한 번에 튀지 않는다.
    public static float CatchupMultiplier()
    {
        var p = Current;
        if (!p.CatchupEnabled) return 1f;

        float behind = BehindBy();
        if (behind >= p.CatchupHighThreshold) return p.CatchupHighMultiplier;
        if (behind >= p.CatchupMidThreshold) return p.CatchupMidMultiplier;
        return 1f;
    }

    // 화면이 "복구가 밀렸는가"를 말할 때 쓰는 판정(문구는 UI 가 고른다).
    public static bool BehindSchedule() => BehindBy() >= Current.CatchupMidThreshold;
}
