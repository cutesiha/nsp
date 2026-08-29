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

    public SpawnedTaskStatus Status = SpawnedTaskStatus.Active;
    // Completed / Failed 이후 방 카드에 "✓ 완료" · "🚨 실패" 를 잠깐 더 보여주기 위한 잔여 표시 시간.
    public float ResolveDisplayTimer;

    // TaskStart 로그(직원이 실제로 업무 수행 시작)를 1인당 1회만 남기기 위한 집합.
    public readonly HashSet<string> StartedWorkerIds = new();

    public float Remaining => System.Math.Max(0f, TimeLimitSeconds - Elapsed);
    public float Ratio => GaugeRequired > 0f ? Gauge / GaugeRequired : 0f;
}
