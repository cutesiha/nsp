using System;
using System.Collections.Generic;
using Godot;
using NSP.Data;

namespace NSP.Core;

// 순환 배치 규정(G-3).
//
// 근무 중 한 번, 하루 절반쯤에 "순환 배치 시간" 이 걸린다. 제한 시간 안에 최소 두 명을
// 지금과 다른 작업실로 옮겨야 한다. 못 지키면 그날 남은 시간 동안 야간 근무 피로가 배로 는다.
//
// 목적은 두 가지다. 관리자에게 근무 중에 할 일을 하나 더 주고, 동선을 섞어 목격 정보를
// 흐린다(F-5 와 함께 간다 — 모두가 제자리에 있으면 이동 기록 자체가 범인 표가 된다).
//
// 새 화면을 만들지 않는다. 알림과 판정만 하고, 재배치는 평소 쓰던 지도에서 그대로 한다.
public static class RotationOrderSystem
{
    public enum Phase { Idle, Waiting, Done }

    public static Phase Current { get; private set; } = Phase.Idle;
    // 남은 제한 시간(초).
    public static float SecondsLeft { get; private set; }
    public static int Moved { get; private set; }
    public static int Needed { get; private set; }
    // 지키지 못했는가 — 그날 남은 시간 동안 피로가 빨라진다.
    public static bool Failed { get; private set; }

    // 규정이 걸린 순간 / 끝난 순간(성공 여부). 화면 알림이 듣는다.
    public static event Action Started;
    public static event Action<bool> Finished;

    // 규정이 걸릴 때 각자 있던 작업실. 여기서 바뀌어야 "옮겼다" 로 센다.
    private static readonly Dictionary<string, string> _roomAtOrder = new();
    private static bool _firedToday;

    public static void ResetAll()
    {
        Current = Phase.Idle;
        SecondsLeft = 0f;
        Moved = 0;
        Needed = 0;
        Failed = false;
        _firedToday = false;
        _roomAtOrder.Clear();
    }

    // 못 지킨 날의 피로 배율. 지켰거나 아직 걸리지 않았으면 1배.
    public static float ShiftStressMultiplier()
    {
        if (!Failed) return 1f;
        return Mathf.Max(1f, Config.Instance?.Data?.RotationMissPenaltyRate ?? 2f);
    }

    public static void Tick(float delta, NSP.Facility.FacilitySimulation sim)
    {
        var gs = GameState.Instance;
        var cfg = Config.Instance?.Data;
        if (gs == null || cfg == null || sim == null) return;
        if (gs.CurrentPhase != GamePhase.Live || !DayFeatures.AutoIncidentsEnabled) return;

        switch (Current)
        {
            case Phase.Idle:
                if (_firedToday) break;
                // 하루 절반쯤에 한 번 건다.
                if (gs.DayTimeSeconds < cfg.DayLengthSeconds * Mathf.Clamp(cfg.RotationAtDayRatio, 0.1f, 0.9f)) break;
                Begin(sim, cfg);
                break;

            case Phase.Waiting:
                SecondsLeft -= delta;
                Moved = CountMoved(sim);
                if (Moved >= Needed) { End(true); break; }
                if (SecondsLeft <= 0f) End(false);
                break;
        }
    }

    private static void Begin(NSP.Facility.FacilitySimulation sim, ConfigData cfg)
    {
        _roomAtOrder.Clear();
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false, Incapacitated: false }) continue;
            if (string.IsNullOrEmpty(st.AssignedRoomId)) continue;
            _roomAtOrder[id] = st.AssignedRoomId;
        }

        _firedToday = true;
        Current = Phase.Waiting;
        Needed = Mathf.Max(1, cfg.RotationMinMoves);
        // 옮길 사람이 애초에 모자라면 요구치를 줄인다(지킬 수 없는 규정을 걸지 않는다).
        Needed = Mathf.Min(Needed, Mathf.Max(1, _roomAtOrder.Count));
        SecondsLeft = Mathf.Max(5f, cfg.RotationWindowSeconds);
        Moved = 0;

        EventLog.Instance?.LogEvent(LogEventType.Relocation, "", "",
            $"📋 순환 배치 시간 — {Mathf.CeilToInt(SecondsLeft)}초 안에 {Needed}명을 다른 작업실로");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"순환 배치 시간 — {Needed}명을 다른 작업실로 ({Mathf.CeilToInt(SecondsLeft)}초)",
            NSP.Ui.NoticeLevel.Warning);
        Started?.Invoke();
    }

    // 규정이 걸릴 때와 다른 방에 배치된 사람 수.
    private static int CountMoved(NSP.Facility.FacilitySimulation sim)
    {
        int n = 0;
        foreach (var (id, was) in _roomAtOrder)
        {
            var st = sim.GetEmployeeState(id);
            if (st == null || string.IsNullOrEmpty(st.AssignedRoomId)) continue;
            if (st.AssignedRoomId != was) n++;
        }
        return n;
    }

    private static void End(bool ok)
    {
        Current = Phase.Done;
        Failed = !ok;
        SecondsLeft = 0f;

        if (ok)
        {
            EventLog.Instance?.LogEvent(LogEventType.Relocation, "", "", "✓ 순환 배치 완료");
            NSP.Ui.FacilityAlertHud.Instance?.Notify("순환 배치 완료", NSP.Ui.NoticeLevel.Info);
        }
        else
        {
            float rate = Config.Instance?.Data?.RotationMissPenaltyRate ?? 2f;
            EventLog.Instance?.LogEvent(LogEventType.Neglect, "", "",
                $"⚠ 순환 배치 미이행 — 남은 근무 동안 피로 누적 {rate:0.#}배");
            NSP.Ui.FacilityAlertHud.Instance?.Notify(
                $"순환 배치 미이행 — 피로 누적 {rate:0.#}배", NSP.Ui.NoticeLevel.Critical);
        }
        Finished?.Invoke(ok);
    }
}
