using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 중앙제어실로 접근하는 괴물이 지금 어느 단계인가.
//
//   Dormant  → 아직 없다
//   Outer    → 출현 구역에서 접근 복도 입구까지. **카메라에 안 잡힌다**(소리만)
//   Approach → 접근 복도를 따라 문 쪽으로. 이 구간이 CCTV 감시 구간이다
//   AtDoor   → 차폐문 앞 도착. 여기서 문 상태를 본다
//   Pounding → 문이 닫혀 있었다. 두드리고 긁는다
//   Retreat  → 물러난다
//   Breach   → 문이 열려 있었다. 넘어왔다 → 관리자 사망
public enum ThreatPhase { Dormant, Outer, Approach, AtDoor, Pounding, Retreat, Breach, Finished }

// 지금 돌고 있는 위협 하나.
public sealed class MonsterThreat
{
    public MonsterDef Def;
    public ThreatZoneDef Zone;
    // 이 개체가 **어느 작업실에서 걸어 나왔는가**(대회용). 비어 있으면 전용 출현 구역에서
    // 나온 것이다. 작업실 개체와 복도 개체가 같은 하나임을 잇는 끈이다(지시서 §3).
    public string OriginRoomId = "";
    // 들어오는 접근 복도(CorridorSegment.Id). 괴물은 **이 길 하나만** 쓴다 —
    // 그래서 서로 다른 카메라에 같은 개체가 동시에 보이는 일이 없다.
    public string SegmentId = "";

    public ThreatPhase Phase = ThreatPhase.Dormant;
    public float PhaseSeconds;
    // 출현 지점 → 접근 복도 입구까지 걸리는 시간. 0 이하면 출현 구역의 기본값을 쓴다.
    // 작업실에서 걸어 나온 개체는 그 방의 거리만큼 늦게 닿는다.
    public float OuterSeconds;
    // 복도 입구(0) → 차폐문 앞(1). 카메라에 보이는 거리가 이 값이다.
    public float Progress;
    // 지금 멈춰 서 있는가(숨 고르기). 표현이 이 값을 읽어 걷기/서기를 가른다.
    public bool Paused;
    public float PauseTimer;
    // 문 앞에서 몇 번째 시도인가. 같은 개체가 무한히 돌아오지 않게 한다.
    public int Attempts;

    public bool Active => Phase is not (ThreatPhase.Dormant or ThreatPhase.Finished);
    // 지금 CCTV 로 볼 수 있는 단계인가(복도 안에 있는가).
    public bool InCorridor => Phase is ThreatPhase.Approach or ThreatPhase.AtDoor
        or ThreatPhase.Pounding or ThreatPhase.Retreat;

    // 문까지 남은 거리(0~1). 소리 세기와 관리자 긴장도가 이것만 본다.
    public float Closeness => Phase switch
    {
        ThreatPhase.Outer => 0f,
        ThreatPhase.Approach => Mathf.Clamp(Progress, 0f, 1f),
        ThreatPhase.Retreat => Mathf.Clamp(Progress, 0f, 1f),
        ThreatPhase.Dormant or ThreatPhase.Finished => 0f,
        _ => 1f,
    };
}

// 괴물의 **판정**만 한다. 모델도 소리도 화면도 모른다 —
// 그쪽은 이 상태를 읽어 보여 주기만 한다(FacilityCctvWorld · MonsterActor · AdminFear).
//
// 규칙 하나만 지킨다: **예고 없는 죽음은 없다.**
//   출현 구역 → 외곽(소리) → 접근 복도(CCTV) → 문 앞(유예) → 판정
// 어느 단계도 건너뛰지 않으므로, 플레이어는 최소 한 번 볼 기회와 문을 내릴 시간을 갖는다.
public sealed class MonsterThreatSystem
{
    private const string ZoneFolder = "res://data/threats";

    // 동시에 도는 위협 수. 셋이 한꺼번에 오면 문이 하나뿐이라 막을 방법이 없다.
    public const int MaxActive = 1;

    private static List<MonsterDef> _monsters;
    private static List<ThreatZoneDef> _zones;

    private readonly List<MonsterThreat> _active = new();
    private readonly RandomNumberGenerator _rng = new();
    private FacilitySimulation _sim;

    private float _nextSpawnAt = -1f;
    private int _spawnedToday;
    private string _lastSegmentId = "";

    public IReadOnlyList<MonsterThreat> Active => _active;
    public MonsterThreat Current => _active.FirstOrDefault(t => t.Active);
    public int SpawnedToday => _spawnedToday;

    // ── 화면 · 소리가 듣는 신호 ──────────────────────────────────────
    public event Action<MonsterThreat> Spawned;        // 출현 구역에서 나왔다(먼 기척)
    public event Action<MonsterThreat> EnteredCorridor;// 접근 복도에 들어섰다(여기부터 CCTV)
    public event Action<MonsterThreat> ReachedDoor;    // 차폐문 앞 도착
    public event Action<MonsterThreat> Pounded;        // 문을 한 번 두드렸다
    public event Action<MonsterThreat> Retreated;      // 물러났다
    public event Action<MonsterThreat> Breached;       // 넘어왔다 → 관리자 사망

    // 개발 도구가 끌 수 있게 — 검사 중에 괴물이 끼어들면 다른 판정이 흔들린다.
    public bool Suppressed { get; set; }

    public void Attach(FacilitySimulation sim)
    {
        _sim = sim;
        _rng.Randomize();
        LoadDefs();
    }

    private static void LoadDefs()
    {
        if (_monsters != null) return;
        _monsters = new List<MonsterDef>();
        _zones = new List<ThreatZoneDef>();
        foreach (string path in ResourceDir.ListFiles(ZoneFolder, ".tres"))
        {
            var res = GD.Load<Resource>(path);
            if (res is MonsterDef m) _monsters.Add(m);
            else if (res is ThreatZoneDef z) _zones.Add(z);
        }
        _monsters.Sort((a, b) => string.CompareOrdinal(a.MonsterId, b.MonsterId));
        _zones.Sort((a, b) => string.CompareOrdinal(a.ZoneId, b.ZoneId));
    }

    public static void ReloadDefs() => _monsters = null;

    public static IReadOnlyList<MonsterDef> Monsters { get { LoadDefs(); return _monsters; } }
    public static IReadOnlyList<ThreatZoneDef> Zones { get { LoadDefs(); return _zones; } }

    public static ThreatZoneDef Zone(string id) => Zones.FirstOrDefault(z => z.ZoneId == id);

    // ── 하루 일정 ───────────────────────────────────────────────────
    //
    // 날짜별 등장 횟수. DAY1 은 "있다는 걸 배우는 날" 이라 한 번, 뒤로 갈수록 는다.
    // 숫자를 데이터로 빼지 않은 이유: 여기 셋뿐이고 모드와 날짜에만 달려 있어서,
    // 파일로 나누면 찾는 데 더 오래 걸린다.
    private static (int Count, float First, float Gap) Schedule(int day) => day switch
    {
        <= 1 => (1, 50f, 999f),
        2 => (2, 34f, 40f),
        _ => (2, 26f, 34f),
    };

    public void Reset()
    {
        _active.Clear();
        _spawnedToday = 0;
        _nextSpawnAt = -1f;
        _breached = false;
        _lastSegmentId = "";
    }

    // ── 진행 ────────────────────────────────────────────────────────

    public void Tick(float delta)
    {
        if (_sim == null) return;
        // 근무 중에만 돈다. 보고서 · 휴게시간 · 타이틀에서는 이전 괴물이 남지 않는다.
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) { if (_active.Count > 0) Reset(); return; }
        if (Suppressed || !GameModes.CorridorThreatsEnabled) return;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            TickThreat(_active[i], delta);
            if (_active[i].Phase == ThreatPhase.Finished) _active.RemoveAt(i);
        }
        TickSpawn();
    }

    // 이번 근무에 이미 침입당했는가. 당한 뒤로는 아무것도 더 보내지 않는다 —
    // 사망 연출이 도는 동안 다음 개체가 뒤에서 걸어오고 있으면 안 된다.
    private bool _breached;

    public bool Breaching => _breached;

    // 교육(DAY0) 중에는 켠다 — 문 앞까지는 오되 **넘어오지는 않는다.**
    // 조작을 배우는 자리에서 실패 한 번에 죽으면 배울 기회가 사라진다(지시서 §7).
    public bool NoBreach { get; set; }

    private void TickSpawn()
    {
        // 대회용에서 복도 개체는 **작업실에 나타난 이상 개체가 걸어 나온 것**이다.
        // 전용 출현 구역에서 따로 또 내보내면 작업실 개체와 복도 개체가 서로 무관한
        // 두 사건이 되어 버린다(지시서 §3). 그래서 그쪽 일정은 돌리지 않는다.
        if (GameModes.GhostWalksToControlRoom) return;
        if (_breached || _active.Count >= MaxActive) return;
        int day = GameState.Instance?.CurrentDay ?? 1;
        var (count, first, gap) = Schedule(day);
        if (_spawnedToday >= count) return;

        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        if (_nextSpawnAt < 0f) _nextSpawnAt = first + _rng.RandfRange(-4f, 6f);
        if (now < _nextSpawnAt) return;

        // 근무가 거의 끝나가면 띄우지 않는다 — 대응할 시간이 없는 위협은 공정하지 않다.
        float left = DayObjectives.RemainingSeconds;
        var pick = Choose(day);
        if (pick.Def == null) { _nextSpawnAt = now + 10f; return; }
        float needed = (pick.Zone?.OuterTravelSeconds ?? 8f) + pick.Def.ApproachSeconds
                       + pick.Def.BreachDelaySeconds + 4f;
        if (left < needed) { _spawnedToday = count; return; }

        Spawn(pick.Def, pick.Zone);
        _spawnedToday++;
        _nextSpawnAt = now + gap + _rng.RandfRange(-5f, 8f);
    }

    private (MonsterDef Def, ThreatZoneDef Zone) Choose(int day)
    {
        var pool = Monsters.Where(m => m.FirstDay <= day && !string.IsNullOrEmpty(m.ModelPath)).ToList();
        if (pool.Count == 0) return (null, null);
        float total = pool.Sum(m => Mathf.Max(0.01f, m.Weight));
        float roll = _rng.RandfRange(0f, total);
        foreach (var m in pool)
        {
            roll -= Mathf.Max(0.01f, m.Weight);
            if (roll > 0f) continue;
            return (m, ZoneFor(m));
        }
        return (pool[^1], ZoneFor(pool[^1]));
    }

    // 그 괴물이 쓰는 출현 구역. 데이터에서 구역을 직접 지정하지 않고 종류로 잇는다 —
    // 구역을 늘리거나 바꿔도 괴물 쪽 데이터를 고칠 일이 없다.
    private static ThreatZoneDef ZoneFor(MonsterDef m) => m.MonsterId switch
    {
        "infant" => Zone("zone_vent"),
        "spider" => Zone("zone_cable"),
        _ => Zone("zone_evacuation"),
    };

    // ── 작업실에서 걸어 나온다(대회용 · 지시서 §3) ───────────────────
    //
    // GhostHauntSystem 이 "그 방을 떠났다" 고 알리면 여기로 이어진다. 접근 복도는
    // **실제 방 연결 그래프에서 중앙제어실에 가장 가까운 쪽**으로 고르므로,
    // CCTV 에 잡히는 방향과 시뮬레이션상의 경로가 어긋나지 않는다.
    //
    // 돌려주는 값이 null 이면 띄우지 못한 것이다(이미 하나 돌고 있거나, 근무가 얼마
    // 남지 않았거나, 모델 데이터가 없거나). 작업실 쪽은 그래도 정상 종료된다.
    public MonsterThreat SpawnFromRoom(string originRoomId, string monsterId = "")
    {
        if (_sim == null || !GameModes.CorridorThreatsEnabled) return null;
        if (Suppressed || _breached) return null;

        var def = string.IsNullOrEmpty(monsterId)
            ? Choose(GameState.Instance?.CurrentDay ?? 1).Def
            : Monsters.FirstOrDefault(m => m.MonsterId == monsterId);
        if (def == null) return null;

        string segment = ApproachFrom(originRoomId);
        if (string.IsNullOrEmpty(segment)) return null;

        // 대응할 시간이 없는 위협은 공정하지 않다 — 근무가 얼마 남지 않았으면 보내지 않는다.
        float outer = OuterSecondsFrom(originRoomId, segment);
        float left = DayObjectives.RemainingSeconds;
        if (left > 0f && left < outer + def.ApproachSeconds + def.BreachDelaySeconds + 4f) return null;

        var t = Spawn(def, ZoneFor(def), segment);
        if (t == null) return null;
        t.OriginRoomId = originRoomId;
        t.OuterSeconds = outer;
        _spawnedToday++;
        return t;
    }

    // 그 작업실에서 중앙제어실로 갈 때 **마지막으로 지나는** 접근 복도.
    // 방 그래프에서 각 접근 복도의 바깥쪽 방까지의 홉 수를 재어 가장 가까운 것을 쓴다.
    public string ApproachFrom(string originRoomId)
    {
        var net = _sim?.Corridors;
        if (net == null) return "";
        string hub = FacilitySimulation.DeployOriginRoomId;

        string best = "";
        int bestHops = int.MaxValue;
        foreach (var seg in net.Blockable)
        {
            string outer = seg.RoomA == hub ? seg.RoomB : seg.RoomA;
            int hops = Hops(originRoomId, outer);
            if (hops < 0 || hops >= bestHops) continue;
            bestHops = hops;
            best = seg.Id;
        }
        return best;
    }

    // 작업실 → 접근 복도 입구까지 걸리는 시간. 멀리 있는 방에서 나왔으면 그만큼 늦게 닿는다.
    private float OuterSecondsFrom(string originRoomId, string segmentId)
    {
        var seg = _sim?.Corridors?.ById(segmentId);
        if (seg == null) return 8f;
        string hub = FacilitySimulation.DeployOriginRoomId;
        string outer = seg.RoomA == hub ? seg.RoomB : seg.RoomA;
        int hops = Mathf.Max(0, Hops(originRoomId, outer));
        return Mathf.Clamp(5f + hops * 4.5f, 5f, 18f);
    }

    // 방 그래프 위의 최단 홉 수. 닿지 못하면 -1.
    // **차폐문 상태를 보지 않는다** — 문을 내렸다고 괴물이 다른 방에서 생겨나지 않는다.
    private int Hops(string from, string to)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return -1;
        if (from == to) return 0;
        var seen = new HashSet<string> { from };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((from, 0));
        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            foreach (string next in _sim.RoomNeighborsIgnoringBarriers(id))
            {
                if (!seen.Add(next)) continue;
                if (next == to) return depth + 1;
                queue.Enqueue((next, depth + 1));
            }
        }
        return -1;
    }

    // 개발 도구 · 검사용 — 지금 당장 띄운다.
    public MonsterThreat ForceSpawn(string monsterId, string segmentId = "")
    {
        var def = Monsters.FirstOrDefault(m => m.MonsterId == monsterId);
        if (def == null) return null;
        var t = Spawn(def, ZoneFor(def), segmentId);
        if (t != null) _spawnedToday++;
        return t;
    }

    private MonsterThreat Spawn(MonsterDef def, ThreatZoneDef zone, string forceSegment = "")
    {
        // 동시 한도는 **여기서** 지킨다. 일정(TickSpawn)에서만 막으면 개발 도구의
        // 강제 소환으로 둘이 한꺼번에 서고, 문이 하나뿐이라 막을 방법이 없어진다.
        if (_active.Count(t => t.Active) >= MaxActive) return null;

        string segment = forceSegment;
        if (string.IsNullOrEmpty(segment))
        {
            // 그 구역이 닿을 수 있는 접근 복도 중 하나. 직전과 같은 길은 피한다 —
            // 늘 같은 문으로만 오면 두 번째부터는 사건이 아니라 절차가 된다.
            var lanes = (zone?.ApproachSegmentIds ?? Array.Empty<string>())
                .Where(id => _sim.Corridors.ById(id) is { IsBlockable: true })
                .ToList();
            if (lanes.Count == 0) return null;
            var fresh = lanes.Where(id => id != _lastSegmentId).ToList();
            if (fresh.Count > 0) lanes = fresh;
            segment = lanes[_rng.RandiRange(0, lanes.Count - 1)];
        }
        if (_sim.Corridors.ById(segment) == null) return null;

        _lastSegmentId = segment;
        var t = new MonsterThreat
        {
            Def = def, Zone = zone, SegmentId = segment,
            Phase = ThreatPhase.Outer, PhaseSeconds = 0f, Progress = 0f,
        };
        _active.Add(t);
        Spawned?.Invoke(t);
        return t;
    }

    private void TickThreat(MonsterThreat t, float delta)
    {
        t.PhaseSeconds += delta;
        var seg = _sim.Corridors.ById(t.SegmentId);
        if (seg == null) { Finish(t); return; }

        switch (t.Phase)
        {
            case ThreatPhase.Outer:
                // 출현 구역 → 복도 입구. 아직 아무 카메라에도 없다.
                if (t.PhaseSeconds < (t.OuterSeconds > 0f ? t.OuterSeconds : t.Zone?.OuterTravelSeconds ?? 8f)) break;
                Enter(t, ThreatPhase.Approach);
                EnteredCorridor?.Invoke(t);
                break;

            case ThreatPhase.Approach:
                TickApproach(t, delta);
                if (t.Progress < 1f) break;
                t.Attempts++;
                Enter(t, ThreatPhase.AtDoor);
                ReachedDoor?.Invoke(t);
                break;

            case ThreatPhase.AtDoor:
                // 문 앞. **지금 문 상태를 본다** — 닫혀 있으면 두드리고, 열려 있으면
                // 마지막 유예가 끝난 뒤 넘어온다. 그 유예가 플레이어의 마지막 기회다.
                if (seg.Sealed) { Enter(t, ThreatPhase.Pounding); break; }
                if (t.PhaseSeconds < t.Def.BreachDelaySeconds) break;
                // 교육 중에는 넘어오지 않는다. 문 앞에서 계속 기다리다가, 플레이어가
                // 문을 내리면 그때 두드리기로 넘어간다(지시서 §7 "DAY 0 즉사 금지").
                if (NoBreach) { t.PhaseSeconds = t.Def.BreachDelaySeconds; break; }
                Enter(t, ThreatPhase.Breach);
                _breached = true;
                Breached?.Invoke(t);
                break;

            case ThreatPhase.Pounding:
                // 두드리는 도중에 문이 열리면 다시 문 앞 판정으로 돌아간다.
                // 유예가 처음부터 다시 흐르므로 "열자마자 즉사" 가 되지 않는다.
                if (!seg.Sealed) { Enter(t, ThreatPhase.AtDoor); break; }
                TickPound(t);
                if (t.PhaseSeconds < t.Def.PoundSeconds) break;
                Enter(t, ThreatPhase.Retreat);
                Retreated?.Invoke(t);
                break;

            case ThreatPhase.Retreat:
                // 왔던 길로 물러난다. 화면에서도 멀어진다.
                t.Progress = Mathf.Max(0f, 1f - t.PhaseSeconds / Mathf.Max(0.3f, t.Def.RetreatSeconds));
                if (t.PhaseSeconds < t.Def.RetreatSeconds) break;
                Finish(t);
                break;

            case ThreatPhase.Breach:
                // 신호를 보낸 순간 사망 연출이 받아 갔다. 판정 쪽에 남길 일은 없으므로
                // 잠깐 뒤 스스로 접는다 — 받는 쪽이 없는 환경(검사 · 창 없는 실행)에서
                // 이 위협이 영원히 남아 다음 일정을 막는 일이 없게 한다.
                if (t.PhaseSeconds > 2f) Finish(t);
                break;
        }
    }

    private void TickApproach(MonsterThreat t, float delta)
    {
        // 숨 고르기 — 쉬지 않고 걸어오면 거리가 그냥 타이머가 된다.
        if (t.Paused)
        {
            t.PauseTimer -= delta;
            if (t.PauseTimer <= 0f) t.Paused = false;
            return;
        }
        float per = Mathf.Max(0.5f, t.Def.ApproachSeconds);
        t.Progress = Mathf.Min(1f, t.Progress + delta / per);

        if (t.Def.PauseEverySeconds <= 0f || t.Progress >= 0.96f) return;
        if (t.PhaseSeconds % t.Def.PauseEverySeconds >= delta) return;
        t.Paused = true;
        t.PauseTimer = t.Def.PauseLengthSeconds;
    }

    // 두드림은 일정 간격으로 한 번씩 신호를 낸다(소리 · 화면 진동이 이걸 받는다).
    private const float PoundInterval = 1.15f;
    private readonly Dictionary<MonsterThreat, float> _nextPound = new();

    private void TickPound(MonsterThreat t)
    {
        float next = _nextPound.GetValueOrDefault(t, 0f);
        if (t.PhaseSeconds < next) return;
        _nextPound[t] = t.PhaseSeconds + PoundInterval * _rng.RandfRange(0.82f, 1.25f);
        Pounded?.Invoke(t);
    }

    private void Enter(MonsterThreat t, ThreatPhase phase)
    {
        t.Phase = phase;
        t.PhaseSeconds = 0f;
        t.Paused = false;
        _nextPound.Remove(t);
        if (phase == ThreatPhase.AtDoor) t.Progress = 1f;
    }

    private void Finish(MonsterThreat t)
    {
        t.Phase = ThreatPhase.Finished;
        _nextPound.Remove(t);
    }
}
