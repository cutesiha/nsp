using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 괴물 — 작업실 사고와 **완전히 분리된** 두 번째 사고 계통.
//
// 규칙은 하나뿐이다.
//   나타난 것을 게임이 알려주지 않는다. 관리자가 CCTV 를 직접 돌려 보고 찾아내야 하고,
//   찾아낸 뒤에도 GhostDispelSeconds 동안 **계속 보고 있어야** 사라진다.
//   끝내 못 찾으면 그 작업실에 사고가 터지고, 그 방에 있던 직원이 크게 무너진다.
//
// 이 장치가 있어야 CCTV 가 "가끔 확인하는 화면"에서 "계속 돌려야 하는 화면"이 된다.
// 경고 단말기도 미니맵도 괴물을 가리키지 않는 이유가 그것이다 — 가리키는 순간
// 관리자는 그 방만 보면 되고, 감시는 다시 알림 대기가 된다.
//
// 이 클래스는 **판정만** 한다. 화면에 어떻게 보이는지(등장·비명·소멸 연출)는
// 신호를 받은 FacilityCctvWorld 가 맡는다. 반대 방향은 없다.
public sealed class GhostHauntSystem
{
    // 지금 괴물이 있는 작업실. 비어 있으면 어디에도 없다.
    public string ActiveRoomId { get; private set; } = "";
    public bool Active => !string.IsNullOrEmpty(ActiveRoomId);
    // 나타난 뒤 흐른 시간 · 관리자가 **연속해서** 본 시간.
    public float AliveSeconds { get; private set; }
    public float WatchedSeconds { get; private set; }
    public int AppearedToday { get; private set; }
    public int DispelledToday { get; private set; }
    public int StruckToday { get; private set; }

    // 소멸까지 얼마나 봤는가(0~1). CCTV 화면의 게이지가 이 값을 읽는다.
    public float DispelRatio
    {
        get
        {
            float need = Mathf.Max(0.1f, Config.Instance?.Data?.GhostDispelSeconds ?? 5f);
            return Mathf.Clamp(WatchedSeconds / need, 0f, 1f);
        }
    }

    // --- 화면이 듣는 신호(표현 전용) ---------------------------------------
    public event Action<string> Appeared;
    public event Action<string> Screamed;
    public event Action<string> Dispelled;   // 관측으로 사라졌다
    public event Action<string> Struck;      // 방치되어 사고가 되었다

    private float _nextCheckAt;
    private float _cooldownUntil;
    private float _nextScreamAt;
    private readonly RandomNumberGenerator _rng = new();

    public void Reset()
    {
        ActiveRoomId = "";
        AliveSeconds = WatchedSeconds = 0f;
        AppearedToday = DispelledToday = StruckToday = 0;
        _nextCheckAt = 0f;
        _cooldownUntil = 0f;
        _nextScreamAt = 0f;
        _rng.Randomize();
    }

    // --- 진행 --------------------------------------------------------------

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (sim == null) return;
        var cfg = Config.Instance?.Data;
        if (cfg == null) return;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;

        if (Active) TickActive(delta, now, sim, cfg);
        else TickIdle(now, sim, cfg);
    }

    // 아직 없다 — 나타날 때가 되었는가.
    private void TickIdle(float now, FacilitySimulation sim, ConfigData cfg)
    {
        // 교육일(DAY0)에는 스스로 나오지 않는다. 튜토리얼이 정해진 시점에 직접 부른다.
        if (!DayFeatures.AutoIncidentsEnabled) return;
        // 오늘의 운영 규칙이 한도를 정해 두었으면 그것을 쓰고, 없을 때만 전역 기본값을 쓴다.
        int max = OpsProfile.Today?.GhostMaxPerDay ?? -1;
        if (max < 0) max = cfg.GhostMaxPerDay;
        if (AppearedToday >= max) return;
        if (now < cfg.GhostFirstAppearSeconds || now < _cooldownUntil) return;

        if (_nextCheckAt <= 0f) _nextCheckAt = now + cfg.GhostCheckSeconds;
        if (now < _nextCheckAt) return;
        _nextCheckAt = now + cfg.GhostCheckSeconds;

        if (_rng.Randf() >= cfg.GhostAppearChance) return;
        string room = PickRoom(sim);
        if (!string.IsNullOrEmpty(room)) Appear(room, sim);
    }

    // 나타날 방. 이미 고장 난 방은 고른다 해도 사고를 더할 수 없으니 제외한다.
    private string PickRoom(FacilitySimulation sim)
    {
        var pool = new List<string>();
        foreach (string roomId in sim.GetRoomIds())
        {
            if (roomId == FacilitySimulation.DeployOriginRoomId) continue;
            var def = sim.GetRoomDef(roomId);
            if (def == null || def.IsRestricted) continue;
            if (!sim.IsRoomActive(roomId)) continue;
            if (sim.HasRepairPending(roomId)) continue;
            pool.Add(roomId);
        }
        if (pool.Count == 0) return "";
        return pool[_rng.RandiRange(0, pool.Count - 1)];
    }

    // 튜토리얼(DAY0)이 정해진 방에 직접 부를 때. 일반 등장과 완전히 같은 경로를 쓴다.
    //
    // graceSeconds 를 주면 그동안은 사고로 번지지 않는다 — 교육에서 헤매다 설비가
    // 부서지면 배우기도 전에 벌부터 받는 꼴이 된다.
    public bool ForceAppear(string roomId, FacilitySimulation sim, float graceSeconds = -1f)
    {
        if (Active || sim == null || string.IsNullOrEmpty(roomId)) return false;
        _graceOverride = graceSeconds;
        Appear(roomId, sim);
        return true;
    }

    // 0 보다 크면 이번 등장에 한해 GhostGraceSeconds 대신 이 값을 쓴다.
    private float _graceOverride = -1f;

    private void Appear(string roomId, FacilitySimulation sim)
    {
        ActiveRoomId = roomId;
        AliveSeconds = WatchedSeconds = 0f;
        AppearedToday++;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        _nextScreamAt = now + (Config.Instance?.Data?.GhostScreamIntervalSeconds ?? 7f) * 0.5f;
        // 로그에도 알림에도 남기지 않는다 — 알려주는 순간 찾을 이유가 사라진다.
        Appeared?.Invoke(roomId);
    }

    private void TickActive(float delta, float now, FacilitySimulation sim, ConfigData cfg)
    {
        string room = ActiveRoomId;

        // 그 방이 없어졌거나(잠김) 이미 수리 중이면 조용히 물러난다.
        if (!sim.IsRoomActive(room)) { Clear(cfg, now); return; }

        AliveSeconds += delta;

        // ── 관측 ──────────────────────────────────────────────────────
        // "보고 있다" = 그 방을 CCTV 로 띄워 두었고, 전력이 살아 있고, 화면이 막히지 않았다.
        bool watching = sim.SurveillanceTargetRoomId == room
                        && (GameState.Instance?.IsCctvOperational() ?? false)
                        && !sim.IsRoomCctvBlocked(room);
        if (watching) WatchedSeconds += delta;
        // 눈을 떼면 되감긴다 — 여러 방을 번갈아 보며 조금씩 채울 수는 없다.
        else WatchedSeconds = Mathf.Max(0f, WatchedSeconds - delta * cfg.GhostWatchDecayPerSecond);

        // ── 비명 ──────────────────────────────────────────────────────
        if (now >= _nextScreamAt)
        {
            _nextScreamAt = now + cfg.GhostScreamIntervalSeconds;
            Screamed?.Invoke(room);
        }

        // ── 같은 방 직원 ───────────────────────────────────────────────
        // 눈앞에 있는 것을 보고 있으니 가만히 있어도 깎인다. 성격에 따라 폭이 다르다.
        foreach (string id in sim.OnDutyEmployeeIds(room))
            sim.AddStress(id, cfg.GhostPresenceStressPerSecond * delta * FearScale(id), "괴물 목격");

        // ── 소멸 / 사고 ────────────────────────────────────────────────
        if (WatchedSeconds >= cfg.GhostDispelSeconds) { Dispel(sim, cfg, now); return; }
        float grace = _graceOverride > 0f ? _graceOverride : cfg.GhostGraceSeconds;
        if (AliveSeconds >= grace) Strike(sim, cfg, now);
    }

    // 관리자가 끝까지 보고 있었다 — 괴물이 머리를 감싸 쥐고 가루처럼 흩어진다.
    private void Dispel(FacilitySimulation sim, ConfigData cfg, float now)
    {
        string room = ActiveRoomId;
        DispelledToday++;
        EventLog.Instance?.LogEvent(LogEventType.AnomalyDispelled, "", room,
            $"👁 {sim.RoomDisplayName(room)} — 관측으로 이상 개체 소멸");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"{sim.RoomDisplayName(room)}의 이상 개체가 소멸했습니다.", NSP.Ui.NoticeLevel.Info);
        Sfx.Instance?.Play("task_done", -4f);
        Dispelled?.Invoke(room);
        Clear(cfg, now);
    }

    // 끝내 아무도 보지 않았다 — 그 방의 설비가 부서지고, 그 방에 있던 사람이 무너진다.
    private void Strike(FacilitySimulation sim, ConfigData cfg, float now)
    {
        string room = ActiveRoomId;
        StruckToday++;

        var here = sim.OnDutyEmployeeIds(room).ToList();
        sim.TriggerGhostAccident(room);

        // 그 방에 있던 직원이 한 번에 크게 무너진다. 겁이 많을수록 더 크게 —
        // 양은 이 한 번으로 기절선을 넘도록 배율이 잡혀 있다(FearScale).
        foreach (string id in here)
            sim.AddStress(id, cfg.GhostIncidentStress * FearScale(id), "괴물 사고");

        Struck?.Invoke(room);
        Clear(cfg, now);
    }

    private void Clear(ConfigData cfg, float now)
    {
        _graceOverride = -1f;
        ActiveRoomId = "";
        AliveSeconds = WatchedSeconds = 0f;
        _cooldownUntil = now + cfg.GhostCooldownSeconds;
        _nextCheckAt = now + cfg.GhostCheckSeconds;
    }

    // 이 사람은 괴물 앞에서 얼마나 무너지는가. 성격 축 하나(AvoidsDanger)만 본다 —
    // 담담한 쪽은 덜 받고, 겁이 많은 쪽은 크게 받는다.
    //   늑대·고양이(0~1) 담담   ·   강아지·여우(2) 놀람   ·   양·토끼(3~) 공포
    public static float FearScale(string employeeId) =>
        EmployeeTraits.Get(employeeId).AvoidsDanger switch
        {
            <= 0 => 0.55f,   // 늑대 · 토끼 — 눈도 깜짝 안 한다
            1 => 0.8f,       // 고양이 · 강아지 · 여우 — 놀라지만 버틴다
            2 => 1.2f,
            _ => 1.8f,       // 양 — 이 한 번으로 기절선을 넘는다

        };

    // 이 직원이 지금 괴물을 보고 있는가 — CCTV 표현(겁먹은 동작)이 읽는다.
    public bool IsFacing(FacilitySimulation sim, string employeeId)
    {
        if (!Active || sim == null || string.IsNullOrEmpty(employeeId)) return false;
        var st = sim.GetEmployeeState(employeeId);
        return st is { Alive: true, Isolated: false } && st.CurrentRoomId == ActiveRoomId;
    }
}
