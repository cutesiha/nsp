using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 지금 대응하면 막을 수 있는 이상 징후 한 건.
public sealed class FacilityWarning
{
    public string RoomId = "";
    public string Title = "";
    public string Cause = "";
    public string Consequence = "";
    public int RequiredStaff = 2;
    public float Remaining;
    public float Total;
    public bool Scheduled;     // 정해진 시각에 띄운 경고인가(학습용)

    public float Ratio => Total <= 0f ? 0f : Mathf.Clamp(Remaining / Total, 0f, 1f);
}

// 경고 → 대응 → (성공) 안정화 / (실패) 고장 의 흐름을 담당한다.
//
// 기존 구조를 새로 만들지 않는다. 실패했을 때의 사고는 FacilitySimulation 이 이미 쓰는
// TriggerRoomAccident 경로를 그대로 타므로 수리 업무 · 시설 로그 · 경고 단말기 · 미니맵이
// 전부 지금까지와 똑같이 동작한다. 이 시스템이 새로 만드는 것은 "사고 전에 주는 시간"뿐이다.
//
// 수치는 전부 data/ops/*.tres (OpsProfileDef / RoomOpsDef) 에 있다.
public sealed class FacilityWarningSystem
{
    private readonly List<FacilityWarning> _active = new();
    private readonly Dictionary<string, float> _nextCheckAt = new();
    private readonly Dictionary<string, float> _cooldownUntil = new();
    private readonly HashSet<ScheduledWarningDef> _firedSchedule = new();
    private readonly Dictionary<ScheduledWarningDef, float> _scheduleAt = new();
    private readonly Random _rng = new();
    private float _gapUntil;

    public IReadOnlyList<FacilityWarning> Active => _active;
    public bool HasActive(string roomId) => _active.Any(w => w.RoomId == roomId);
    public FacilityWarning ForRoom(string roomId) => _active.FirstOrDefault(w => w.RoomId == roomId);

    // 이번 근무에서 실제로 뜬 / 막은 / 놓친 경고 수 — 정산 표시와 테스트에 쓴다.
    public int Raised { get; private set; }
    public int Prevented { get; private set; }
    public int Failed { get; private set; }

    public void Reset()
    {
        _active.Clear();
        _nextCheckAt.Clear();
        _cooldownUntil.Clear();
        _firedSchedule.Clear();
        _scheduleAt.Clear();
        _gapUntil = 0f;
        Raised = Prevented = Failed = 0;
    }

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (sim == null) return;
        // DAY0 교육은 튜토리얼이 직접 사고를 내므로 여기서는 아무것도 하지 않는다.
        if (!DayFeatures.AutoIncidentsEnabled) return;
        var profile = OpsProfile.Today;
        if (profile == null) return;

        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        TickActive(delta, now, sim);
        TickScheduled(now, profile, sim);
        TickRandom(now, profile, sim);
    }

    // --- 떠 있는 경고 ------------------------------------------------------

    private void TickActive(float delta, float now, FacilitySimulation sim)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var w = _active[i];

            // 이미 그 방이 고장 나 있으면 경고는 의미가 없다(수리가 우선).
            if (sim.HasRepairPending(w.RoomId))
            {
                _active.RemoveAt(i);
                continue;
            }

            // 대응 성공 — 필요한 인원이 실제로 그 방에 도착했다.
            // 이동 중인 직원은 아직 인원이 아니다(OnDutyCount 규약) — 그래서 거리가 곧 비용이다.
            if (sim.OnDutyCount(w.RoomId) >= w.RequiredStaff)
            {
                Prevented++;
                Close(w, now, sim);
                EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", w.RoomId,
                    $"✓ {sim.RoomDisplayName(w.RoomId)} — {w.Title} 안정화 성공");
                NSP.Ui.FacilityAlertHud.Instance?.Notify("✓ " +
                    NSP.Dialogue.KoreanParticle.Subject(sim.RoomDisplayName(w.RoomId)) + " 안정화되었습니다.",
                    NSP.Ui.NoticeLevel.Info);
                Sfx.Instance?.Play("task_done", -6f);
                continue;
            }

            w.Remaining -= delta;
            if (w.Remaining > 0f) continue;

            // 대응 실패 — 실제 고장으로 넘어간다(기존 사고 경로 그대로).
            Failed++;
            Close(w, now, sim);
            sim.TriggerWarningFailure(w.RoomId, w.Title);
        }
    }

    private void Close(FacilityWarning w, float now, FacilitySimulation sim)
    {
        _active.Remove(w);
        var ops = OpsProfile.Room(w.RoomId);
        _cooldownUntil[w.RoomId] = now + (ops?.WarningCooldownSeconds ?? 20f);
        _gapUntil = now + (OpsProfile.Today?.WarningGapSeconds ?? 14f);
    }

    // --- 정해진 시각의 경고(학습용) -----------------------------------------

    private void TickScheduled(float now, OpsProfileDef profile, FacilitySimulation sim)
    {
        foreach (var s in profile.ScheduledWarnings)
        {
            if (s == null || _firedSchedule.Contains(s)) continue;
            // 같은 초에 매번 뜨면 대본처럼 보인다 — 사건은 보장하되 시각만 흔든다.
            if (!_scheduleAt.TryGetValue(s, out float at))
            {
                at = s.AtSeconds + (s.JitterSeconds > 0f
                    ? (float)(_rng.NextDouble() * 2.0 - 1.0) * s.JitterSeconds
                    : 0f);
                _scheduleAt[s] = at = Mathf.Max(1f, at);
            }
            if (now < at) continue;
            _firedSchedule.Add(s);

            var ops = OpsProfile.Room(s.RoomId);
            if (ops == null || string.IsNullOrEmpty(ops.WarningTitle)) continue;
            if (sim.HasRepairPending(s.RoomId) || HasActive(s.RoomId)) continue;

            Raise(ops, sim,
                s.RequiredStaff > 0 ? s.RequiredStaff : ops.WarningRequiredStaff,
                s.ResponseSeconds > 0f ? s.ResponseSeconds : ops.WarningResponseSeconds,
                scheduled: true);
        }
    }

    // --- 인원 상태에서 나오는 경고 ------------------------------------------

    private void TickRandom(float now, OpsProfileDef profile, FacilitySimulation sim)
    {
        float dayLength = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        if (now < profile.WarningGraceSeconds) return;
        if (now > dayLength - profile.WarningCutoffSeconds) return;
        if (_active.Count >= Mathf.Max(1, profile.MaxConcurrentWarnings)) return;
        if (now < _gapUntil) return;

        float facilityMult = RoomStaffing.WarningChanceMultiplier();

        foreach (var ops in OpsProfile.AllRooms())
        {
            if (string.IsNullOrEmpty(ops.WarningTitle)) continue;
            if (HasActive(ops.RoomId) || sim.HasRepairPending(ops.RoomId)) continue;
            if (now < _cooldownUntil.GetValueOrDefault(ops.RoomId, 0f)) continue;

            float period = Mathf.Max(2f, ops.WarningCheckSeconds);
            if (!_nextCheckAt.TryGetValue(ops.RoomId, out float at))
            {
                _nextCheckAt[ops.RoomId] = now + period;
                continue;
            }
            if (now < at) continue;
            _nextCheckAt[ops.RoomId] = now + period;

            float chance = OpsProfile.Curve(ops.WarningChance, sim.OnDutyCount(ops.RoomId), 0f) * facilityMult;
            if (chance <= 0f || _rng.NextDouble() >= chance) continue;

            Raise(ops, sim, ops.WarningRequiredStaff, ops.WarningResponseSeconds, scheduled: false);
            return;   // 한 번에 하나만 띄운다
        }
    }

    private void Raise(RoomOpsDef ops, FacilitySimulation sim, int requiredStaff, float seconds, bool scheduled)
    {
        var w = new FacilityWarning
        {
            RoomId = ops.RoomId,
            Title = ops.WarningTitle,
            Cause = string.IsNullOrEmpty(ops.WarningCause) ? "설비 부하 상승" : ops.WarningCause,
            Consequence = string.IsNullOrEmpty(ops.WarningConsequence) ? "설비 고장" : ops.WarningConsequence,
            RequiredStaff = Mathf.Max(1, requiredStaff),
            Remaining = Mathf.Max(1f, seconds),
            Total = Mathf.Max(1f, seconds),
            Scheduled = scheduled,
        };
        _active.Add(w);
        Raised++;

        EventLog.Instance?.LogEvent(LogEventType.TaskSpawned, "", w.RoomId,
            $"⚠ {sim.RoomDisplayName(w.RoomId)} — {w.Title} · {w.Total:0}초 안에 {w.RequiredStaff}명 필요");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"⚠ {sim.RoomDisplayName(w.RoomId)}에서 " +
            NSP.Dialogue.KoreanParticle.Subject(w.Title) + " 감지되었습니다.", NSP.Ui.NoticeLevel.Warning);
        Sfx.Instance?.Play("alert_beep3", -8f);
        // 경고 단계에서는 직원이 스스로 움직이지 않는다 — 누구를 보낼지는 관리자가 정한다.
    }
}
