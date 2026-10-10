using Godot;

namespace NSP.Data;

// 한 작업실의 "인원수에 따라 무엇이 달라지는가".
//
// V3 의 DAY1 은 스트레스·능력치·금기 없이 돌아간다. 그래서 배치가 의미를 가지려면
// "몇 명을 넣었는가" 자체가 결과를 바꿔야 한다. 그 표가 여기 있다.
//
// 인원수별 배열(Efficiency 등)은 index = 그 방의 근무 인원이다.
//   index 0 = 아무도 없을 때, 1 = 한 명, 2 = 두 명 …
// 배열 끝을 넘는 인원은 마지막 값을 그대로 쓴다 — 그래서 3명째부터 증가폭을 줄이려면
// 마지막 두 칸을 비슷하게 적으면 된다.
//
// 이 리소스는 OpsProfileDef(하루치 운영 규칙) 안에 담긴다. DAY2~5 를 만들 때는
// 새 OpsProfileDef 에 같은 방의 다른 표를 넣으면 된다 — 코드는 그대로다.
[GlobalClass]
public partial class RoomOpsDef : Resource
{
    [Export] public string RoomId = "";
    [Export(PropertyHint.MultilineText)] public string RoleNote = "";

    // ── 인원수별 값 ────────────────────────────────────────────────────
    // 이 방 업무의 진행 속도 배율. 0명이면 0 — 아무 일도 진행되지 않는다.
    [Export] public float[] Efficiency = { 0f, 1f, 1.3f, 1.4f };

    // 이 방의 인원이 시설 '전체' 업무 속도에 거는 배율(발전실 전용).
    // 발전이 불안정하면 모든 방이 느려진다.
    [Export] public float[] FacilityOutput = { 1f, 1f, 1f, 1f };

    // 이 방이 고장 난 동안 시설 전체에 걸리는 배율(발전실 전용).
    [Export] public float OutputWhileBroken = 1f;

    // 이 방의 인원이 시설 '전체' 경고 발생 확률에 거는 배율(정비실 전용).
    // 정비 인력이 많을수록 다른 방의 이상 징후를 미리 잡아 준다.
    [Export] public float[] FacilityWarningChance = { 1f, 1f, 1f, 1f };

    // 이 방의 인원이 방해공작 성공 확률에 거는 배율(경비실 전용).
    [Export] public float[] SabotageChance = { 1f, 1f, 1f, 1f };

    // 이 방의 인원이 코어 복구 1회당 자재 소모량에 거는 배율(저장고 전용).
    [Export] public float[] MaterialCost = { 1f, 1f, 1f, 1f };

    // 이 방의 인원이 순찰 기록을 남기는 주기(초). 0 이면 기록하지 않는다(경비실 전용).
    // 기록은 "그 시각 그 방에 누가 있었나"일 뿐 범인을 지목하지 않는다.
    [Export] public float[] PatrolIntervalSeconds = { 0f, 0f, 0f, 0f };

    // ── 이 방에서 뜨는 경고 ────────────────────────────────────────────
    // 경고는 "지금 대응하면 사고를 막을 수 있는 시간"이다. 제목이 비어 있으면 경고가 없다.
    [Export] public string WarningTitle = "";
    [Export] public string WarningCause = "";
    [Export] public string WarningConsequence = "";
    // 점검 주기마다 굴리는 발생 확률(index = 인원수). 전부 0 이면 저절로는 뜨지 않는다
    // (그래도 OpsProfileDef.ScheduledWarnings 로 정해진 시각에 띄울 수 있다).
    [Export] public float[] WarningChance = { 0f, 0f, 0f, 0f };
    [Export] public float WarningCheckSeconds = 22f;
    // 경고가 뜬 뒤 사고가 나기까지의 대응 시간(초).
    [Export] public float WarningResponseSeconds = 10f;
    // 이 인원 이상이 그 방에 도착하면 안정화 성공. 이동 중인 직원은 아직 인원이 아니다.
    [Export] public int WarningRequiredStaff = 2;
    // 한 번 처리한 뒤 이 방에 다시 경고가 뜨기까지의 최소 간격(초).
    [Export] public float WarningCooldownSeconds = 20f;

    // 경고를 풀기 위해 **실제로 붙어서 작업해야 하는 시간**(초).
    // 사람이 도착하는 순간 경고가 사라지면 "보내기만 하면 끝"이라 배치가 선택이 되지 않는다.
    // 실제 고장 수리(RepairSeconds)보다는 반드시 짧다 — 경고 단계에서 잡는 쪽이 늘 싸야 한다.
    // 0 이면 RepairSeconds 의 WarningStabilizeRatio 배로 자동 계산한다.
    [Export] public float WarningStabilizeSeconds = 0f;

    // ── 무인 방치 / 수리 ───────────────────────────────────────────────
    // 0 이하 = 비워 둬도 사고가 나지 않는다(대신 경고가 압박을 준다).
    // 0 보다 크면 RoomDef.UnstaffedAccidentSeconds 대신 이 값을 쓴다.
    [Export] public float UnstaffedAccidentSeconds = 0f;

    // 0 이하면 RoomDef 의 값을 그대로 쓴다.
    [Export] public float RepairSeconds = 0f;
    [Export] public int RepairMinWorkers = 0;
}
