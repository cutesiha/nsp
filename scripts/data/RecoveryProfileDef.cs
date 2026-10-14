using Godot;

namespace NSP.Data;

// 봉쇄 코어 복구 난이도 한 벌. "몇 번째 5일인가"(RecoveryCycle)로 고른다.
//
//   1차 — 스토리 캠페인. 시스템을 배우면서 첫 엔딩까지 갈 수 있어야 한다.
//   2차 — 1차에서 배운 운영을 실제로 요구한다.
//   3차 — 운영 최적화와 추리를 동시에 요구한다(기존의 빡센 밸런스가 여기로 왔다).
//
// 숫자는 전부 여기(data/recovery/*.tres)에 있다. 코드에 DAY 별 분기를 넣지 않는다.
[GlobalClass]
public partial class RecoveryProfileDef : Resource
{
    [Export] public string ProfileId = "";
    [Export] public string DisplayName = "";

    // --- 코어실 인원별 복구 배율 -------------------------------------------
    // index = 그 방 근무 인원(0명부터). 표 끝을 넘는 인원은 마지막 값을 쓴다.
    // 이 값이 있으면 data/ops/*.tres 의 코어실 Efficiency 보다 **우선**한다.
    [Export] public float[] CoreStaffCurve = { 0f, 1.0f, 1.45f, 1.70f, 1.78f };

    // 코어 복구 속도 자체에 걸리는 배율(인원 배율과 별개로 곱해진다).
    //
    // 근무는 120초다. 인원 배율만 만지면 2명 풀가동으로도 하루 22% 가 한계라
    // 5일 총획득이 110% — 손실을 빼면 완벽한 플레이만 겨우 100% 에 닿는다.
    // 초보자가 들어갈 자리를 만들려면 이 값이 함께 움직여야 한다.
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float CoreRateMultiplier = 1f;

    // DAY 별 복구 배율(index 0 = DAY1). 하나의 전역 배율로 5일을 맞출 수 없어서 넣었다 —
    // DAY1 은 사고가 적어 코어에 사람을 오래 둘 수 있고, DAY4~5 는 경고 · 기절 · 방해로
    // 실작업 시간이 줄어든다. 비어 있으면 전부 1.0 으로 본다.
    [Export] public float[] DayRecoveryMultiplier = System.Array.Empty<float>();

    public float DayMultiplier(int day)
    {
        if (DayRecoveryMultiplier == null || DayRecoveryMultiplier.Length == 0) return 1f;
        int i = Mathf.Clamp(day - 1, 0, DayRecoveryMultiplier.Length - 1);
        return DayRecoveryMultiplier[i];
    }

    // --- 자재 ---------------------------------------------------------------
    // 코어 복구 1회가 먹는 자재에 곱한다. 0.7 = 기존보다 30% 덜 먹는다.
    [Export(PropertyHint.Range, "0.1,2,0.05")] public float CoreMaterialCostMultiplier = 1f;

    // 자재 1개가 사 오는 복구량 배율. 자재 소모는 정수라 1개 밑으로 못 내려가므로
    // "자재 부담 완화" 는 실질적으로 이 수율로 조절한다(1차 2.0 = 자재 1개에 2%).
    [Export(PropertyHint.Range, "0.5,4,0.1")] public float CoreYieldMultiplier = 1f;

    // --- 기반 경제(스트레스 · 자재) ------------------------------------------
    //
    // 첫 5일이 "시설 운영을 배우는" 캠페인이 되려면, 정상 운용만으로 5일을 버틸 수 있어야
    // 한다. 측정에서는 DAY2 근무의 87% 가 자재 대기였고 DAY4 에 6명 전원이 기절했다 —
    // 그 상태에서는 코어 배율을 아무리 만져도 의미가 없다.

    // 직원이 받는 스트레스 **증가량** 배율(감소에는 걸지 않는다).
    [Export(PropertyHint.Range, "0.2,2,0.05")] public float StressGainMultiplier = 1f;
    // 의무실 치료 1회가 내리는 스트레스 배율.
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float MedicalStressRecoveryMultiplier = 1f;
    // 정비실 자재 생산량 배율.
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float MaterialProductionMultiplier = 1f;

    // 자재 보관 한도의 **하한**. 저장고 사고가 겹치면 한도가 0 까지 내려가는데, 0 이 되면
    // 생산한 자재가 전부 즉시 폐기되어 코어가 영영 돌지 못한다(측정에서 DAY2 에 그렇게 됐다).
    // 저장고를 비우면 여전히 손해지만, 판이 되돌릴 수 없게 죽지는 않게 하는 바닥이다.
    // 0 이면 바닥 없음(기존 동작 그대로).
    [Export] public int MaterialsCapFloor;

    // --- 방해공작 -----------------------------------------------------------
    // 결번 개체가 복구 작업을 망쳤을 때 깎이는 코어 %에 곱한다.
    [Export(PropertyHint.Range, "0,2,0.05")] public float SabotageCoreDamageMultiplier = 1f;

    // 코어실 경고(출력 불안정)를 놓쳤을 때 깎이는 코어 %에 곱한다.
    // 방해공작과 함께 5일 손실의 절반을 차지한다 — 초보자에게는 이쪽이 더 아프다.
    [Export(PropertyHint.Range, "0,2,0.05")] public float CoreInstabilityLossMultiplier = 1f;

    // --- 뒤처짐 보정(Catch-up) ----------------------------------------------
    // 화면에 "초보자 보정" 같은 문구를 띄우지 않는다. 내부 보정이다.
    [Export] public bool CatchupEnabled;
    // 권장치보다 이만큼(%p) 뒤처지면 그 단계의 배율이 걸린다.
    [Export] public float CatchupMidThreshold = 10f;
    [Export] public float CatchupHighThreshold = 20f;
    [Export] public float CatchupMidMultiplier = 1.15f;
    [Export] public float CatchupHighMultiplier = 1.30f;

    // --- DAY 별 권장 복구율 --------------------------------------------------
    // index 0 = DAY1 종료 시점의 권장치. 실패 조건이 아니라 "일정이 밀렸는가"의 기준이다.
    [Export] public float[] RecommendedByDay = { 18f, 36f, 56f, 80f, 100f };

    // 그 DAY 종료 시점의 권장 복구율(표 밖이면 마지막 값).
    public float RecommendedAt(int day)
    {
        if (RecommendedByDay == null || RecommendedByDay.Length == 0) return 0f;
        int i = Mathf.Clamp(day - 1, 0, RecommendedByDay.Length - 1);
        return RecommendedByDay[i];
    }

    // 인원수에 해당하는 코어 복구 배율.
    public float CoreStaffMultiplier(int headcount)
    {
        if (CoreStaffCurve == null || CoreStaffCurve.Length == 0) return headcount > 0 ? 1f : 0f;
        int i = Mathf.Clamp(headcount, 0, CoreStaffCurve.Length - 1);
        return CoreStaffCurve[i];
    }
}
