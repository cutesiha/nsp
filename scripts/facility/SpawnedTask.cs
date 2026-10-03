using System.Collections.Generic;

namespace NSP.Facility;

public enum SpawnedTaskStatus
{
    Active,
    Completed,
    Failed,
}

// TaskSpawnDef 로 실제 발생한 "업무 인스턴스". 방마다 0~여러 개가 존재할 수 있고,
// FacilitySimulation 이 소유한다(SSoT). UI 는 읽기만 한다.
public class SpawnedTask
{
    public string TaskId = "";
    public string RoomId = "";
    public bool Recurring;

    public float TimeLimitSeconds;
    public float GaugeRequired;

    public float Elapsed;   // 발생 후 경과 시간
    public float Gauge;     // 업무 진행도

    // 사고 업무를 제한시간 안에 못 막아 고장이 난 뒤, 같은 업무가 "수리" 로 전환된 상태.
    // 수리 업무는 제한시간이 없고, 완료하면 걸려 있던 시설 페널티가 풀린다.
    public bool IsRepair;
    // 이 업무 인스턴스가 요구하는 최소 인원. 0 이면 TaskDef.MinWorkersToProgress 를 쓴다.
    // (무인 방치 사고의 수리는 RoomDef.RepairMinWorkers 로 결정된다 — 발전실·코어실은 2명.)
    public int MinWorkersOverride;

    public SpawnedTaskStatus Status = SpawnedTaskStatus.Active;
    // Completed / Failed 이후 방 카드에 "✓ 완료" · "🚨 실패" 를 잠깐 더 보여주기 위한 잔여 표시 시간.
    public float ResolveDisplayTimer;

    // TaskStart 로그(직원이 실제로 업무 수행 시작)를 1인당 1회만 남기기 위한 집합.
    public readonly HashSet<string> StartedWorkerIds = new();
    // 자재가 없어 멈춰 있다고 이미 기록했는가. 상태가 바뀔 때만 한 번씩 로그를 남긴다.
    public bool MaterialsBlockedLogged;

    // 이번 틱에 실제로 게이지가 찼는가(최소 인원을 채웠고 자재도 막히지 않음).
    // 화면 표시 전용 — 시뮬레이션 판정에는 쓰지 않는다.
    public bool Progressing;

    // 마지막으로 계산된 초당 게이지 증가량. 표시 전용이다 — 방 카드가
    // "다음 생산까지 몇 초" 를 지금 속도 기준으로 환산하는 데만 쓴다.
    public float LastRate;

    public float Remaining => System.Math.Max(0f, TimeLimitSeconds - Elapsed);
    public float Ratio => GaugeRequired > 0f ? Gauge / GaugeRequired : 0f;
}
