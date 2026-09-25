using System.Collections.Generic;

namespace NSP.Facility;

// 괴물이 사라진 뒤 "아직 일을 못 하는" 시간(초). CCTV 의 회복 연출과 같은 표를 쓴다.
//
// 괴물이 소멸하거나 사고를 내고 사라지는 순간, 그 방에 있던 직원마다
// EmployeeState.WorkBlockedUntil = 지금 + WorkBlockSeconds 가 걸린다. 그동안은 업무 게이지에 기여하지 않는다
// (기절·공황과 같은 취급 — 자리에는 있지만 일은 못 한다). 스트레스는 건드리지 않는다.
//
//   AnimSeconds      : CCTV 회복 연출 길이(양은 바닥에서 떠는 시간 + 일어나는 시간)
//   WorkBlockSeconds : 연출 + 원래 자리로 걸어 돌아가는 시간(대략)
// 여우는 괴물 앞에서도 일하던 사람이라 짧은 "뭐였냐" 동작만큼만 멈춘다.
public static class PostGhostRecovery
{
    // 양이 괴물이 사라진 뒤에도 바닥에 주저앉아 떠는 시간(3~5초 사이로 고정).
    public const float SheepFloorSeconds = 4.0f;
    public const float SheepStandSeconds = 2.4f;   // ghost_sheep_recover 길이

    private static readonly Dictionary<string, (float Anim, float Block)> _table = new()
    {
        ["sheep"] = (SheepFloorSeconds + SheepStandSeconds, SheepFloorSeconds + SheepStandSeconds + 2.2f),
        ["rabbit"] = (1.8f, 3.4f),   // ghost_rabbit_relief
        ["cat"] = (2.2f, 3.4f),      // ghost_cat_wipe_recover
        ["dog"] = (2.8f, 4.6f),      // ghost_dog_breathe_recover
        ["wolf"] = (3.4f, 4.6f),     // ghost_wolf_check_recover
        ["fox"] = (2.0f, 2.0f),      // ghost_fox_shrug_recover
    };

    public static float AnimSeconds(string employeeId) => _table.TryGetValue(employeeId, out var v) ? v.Anim : 1.2f;
    public static float WorkBlockSeconds(string employeeId) => _table.TryGetValue(employeeId, out var v) ? v.Block : 2f;
}
