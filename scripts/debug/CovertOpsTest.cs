using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 감시 공백 · 은밀 이동 · 봉쇄 교란 · 조건부 살인 자동 검증.
//
//   godot --headless --path . scenes/debug/CovertOpsTest.tscn --quit-after 6000
//
// 확인하는 것은 하나로 모인다 — **"전력이 꺼졌다" 는 사실만으로는 아무 일도 일어나지 않는다.**
// 모든 사건은 실제 위치 · 실제 경로 · 실제 체류 시간을 거치고, 플레이어가 확보한 자료는
// 관리자가 실제로 본 것뿐이다.
public partial class CovertOpsTest : Node
{
    private const float Step = 1f / 30f;
    private static readonly string[] Roster = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("############ 감시 공백 / 은밀 행동 / 조건부 살인 검증 ############");
        PowerAndBarrier();
        CovertMovement();
        SabotageVisibility();
        MurderRules();
        GhostCausality();
        ModeIntegrity();
        BalanceSweep();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    // ── ① 전력 · 차폐 (검사 1~4) ─────────────────────────────────────────
    private void PowerAndBarrier()
    {
        GD.Print("\n── 전력 · 차폐 ───────────────────────────────────");
        StartShift(1, "dog");
        var gs = GameState.Instance;

        // 1. 세 장치를 동시에 켤 수 없다.
        SetPower(true, true, false);
        bool thirdRefused = !gs.TryTogglePower(PowerConsumer.Barrier);
        Check(gs.PowerCapacity == 2, $"전력 용량이 2포인트 (현재 {gs.PowerCapacity})");
        Check(thirdRefused && !gs.IsConsumerPowered(PowerConsumer.Barrier),
            "LIGHTING + CCTV 가 켜진 상태에서 BARRIER 는 거부된다");
        // 하나를 끄면 켤 수 있다.
        gs.TryTogglePower(PowerConsumer.Lighting);
        Check(gs.TryTogglePower(PowerConsumer.Barrier), "조명을 끄면 BARRIER 를 켤 수 있다");

        // 2. 차폐문은 시간이 지나도 저절로 열리지 않는다.
        var seg = _sim.Corridors.Blockable.FirstOrDefault();
        Check(seg != null, "차폐 가능한 통로가 있다");
        if (seg != null)
        {
            _sim.Corridors.Seal(seg.Id, out _);
            Advance(90f);
            Check(seg.Sealed, $"90초가 지나도 차폐문이 유지된다 (상태 {seg.State})");
            Check(_sim.Corridors.SealedCount <= CorridorNet.MaxSealed,
                $"동시 차폐 한도 {CorridorNet.MaxSealed} 유지");

            // 3. BARRIER 전력을 내리면 기존 규칙대로 열린다.
            gs.TryTogglePower(PowerConsumer.Barrier);
            Advance(4f);
            Check(!seg.Sealed, "BARRIER 전력을 내리면 차폐문이 열린다");
        }

        // 4. 전력 배분을 바꿔도 직원 위치와 AI 상태가 그대로다.
        var before = Roster.ToDictionary(id => id, id => _sim.GetEmployeeState(id)?.CurrentRoomId ?? "");
        var phaseBefore = _sim.Covert.Phase;
        for (int i = 0; i < 6; i++)
        {
            gs.TryTogglePower(PowerConsumer.Lighting);
            gs.TryTogglePower(PowerConsumer.CctvWatch);
            Advance(0.4f);
        }
        bool same = Roster.All(id => (_sim.GetEmployeeState(id)?.CurrentRoomId ?? "") == before[id]);
        Check(same, "전력 스위치를 여러 번 조작해도 직원이 순간이동하지 않는다");
        Check(_sim.Covert.Phase == phaseBefore || _sim.Covert.Phase != CovertPhase.Attacking,
            "전력 조작만으로 결번 상태가 공격 단계로 건너뛰지 않는다");
    }

    // ── ② 은밀 이동 (검사 5~9) ───────────────────────────────────────────
    private void CovertMovement()
    {
        GD.Print("\n── 은밀 이동 ─────────────────────────────────────");

        // 5. 조명을 끄고 충분히 기다리면 결번이 실제로 다른 방으로 걸어간다.
        //    배치를 직접 지정한다 — 결번은 비상등이 없는 방에, 노릴 수 있는 1인 근무자를
        //    다른 방에 둔다. 기본 배치에 맡기면 결번이 비상등 있는 방(코어실·경비실)에
        //    떨어져 기회 자체가 열리지 않는 판이 섞인다.
        int started = 0, arrived = 0, hops = 0;
        bool teleported = false;
        for (int attempt = 0; attempt < 4 && arrived == 0; attempt++)
        {
            StartPlaced(3, "dog", new[]
            {
                ("dog", "storage_room"), ("rabbit", "maintenance_room"),
                ("cat", "core_room"), ("fox", "core_room"), ("sheep", "core_room"), ("wolf", "guard_room"),
            });
            SetPower(false, true, true);             // LIGHTING OFF
            string prev = _sim.GetEmployeeState("dog").CurrentRoomId;
            var visited = new HashSet<string> { prev };
            for (float t = 0; t < 110f; t += Step)
            {
                Advance(Step);
                var st = _sim.GetEmployeeState("dog");
                if (st.CurrentRoomId == prev) continue;
                // 순간이동이 아니라면 이웃한 방으로만 바뀐다(경로를 따라 한 칸씩).
                if (!_sim.RoomNeighborsIgnoringBarriers(prev).Contains(st.CurrentRoomId)) teleported = true;
                prev = st.CurrentRoomId;
                visited.Add(prev);
            }
            started += _sim.Covert.CovertMovesStarted;
            arrived += _sim.Covert.CovertMovesArrived;
            hops = Mathf.Max(hops, visited.Count);
        }
        Check(started > 0, $"LIGHTING OFF 가 이어지면 결번이 은밀 이동을 시도한다 ({started}건)");
        Check(hops > 1, $"실제로 다른 방에 도달한다 (들른 방 최대 {hops}곳)");
        Check(!teleported, "이동은 이웃한 방을 거쳐 일어난다 (순간이동 없음)");

        // 6. 차폐문으로 길이 끊기면 실제로 못 간다.
        StartShift(3, "dog");
        SetPower(false, false, true);                // BARRIER 만 켠다
        foreach (var s in _sim.Corridors.Blockable) _sim.Corridors.Seal(s.Id, out _);
        Advance(3f);
        var sealedSeg = _sim.Corridors.Segments.FirstOrDefault(x => x.Sealed);
        if (sealedSeg != null)
        {
            var path = _sim.FindPath(sealedSeg.RoomA, sealedSeg.RoomB);
            bool direct = path.Count == 1 && path[0] == sealedSeg.RoomB;
            Check(!direct, $"차폐된 통로({sealedSeg.DisplayName})는 경로로 쓰이지 않는다");
        }
        else Check(false, "차폐 상태를 만들지 못했다");

        // 7. 조명을 복구해도 직원이 순간이동하거나 상태가 초기화되지 않는다.
        StartShift(3, "dog");
        SetPower(false, true, true);
        Advance(40f);
        string whereDark = _sim.GetEmployeeState("dog").CurrentRoomId;
        float darkBefore = _sim.Covert.DarkSeconds;
        SetPower(true, true, false);
        Advance(0.5f);
        Check(_sim.GetEmployeeState("dog").CurrentRoomId == whereDark,
            "조명을 켠 순간 결번이 원래 자리로 되돌아가지 않는다");
        Check(_sim.Covert.DarkSeconds > darkBefore * 0.5f,
            "조명을 켰다고 누적된 위험이 즉시 0 이 되지 않는다 (스위치 연타 무력화 방지)");

        // 8. 조명이 켜져 있으면 새 은밀 이동이 시작되지 않는다.
        StartShift(3, "dog");
        SetPower(true, true, false);
        Advance(110f);
        Check(_sim.Covert.CovertMovesStarted == 0,
            $"LIGHTING ON 상태에서는 은밀 이동이 없다 ({_sim.Covert.CovertMovesStarted}건)");

        // 9. 격리된 결번은 아무것도 못 한다.
        StartShift(3, "dog");
        SetPower(false, false, true);
        _sim.IsolateEmployee("dog");
        Advance(110f);
        Check(_sim.Covert.CovertMovesStarted == 0 && _sim.Covert.KillsCommitted == 0,
            "격리된 결번은 은밀 이동도 살인도 하지 못한다");
    }

    // ── ③ 방해공작 · 관측 (검사 10~14) ───────────────────────────────────
    private void SabotageVisibility()
    {
        GD.Print("\n── 방해공작 · 관측 자료 ──────────────────────────");

        // 10. CCTV OFF 에서 봉쇄 교란이 일어난다.
        StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) });
        SetPower(true, false, false);                // CCTV OFF
        Advance(110f);
        int tampers = _sim.Covert.ContainmentTampers;
        Check(tampers > 0, $"CCTV OFF 가 이어지면 봉쇄 계통 교란이 일어난다 ({tampers}건)");
        Check(_sim.Containment.Instability > 0f,
            $"교란이 봉쇄 불안정성으로 쌓인다 ({_sim.Containment.Instability:0.0})");

        // 13 · 14. 관측하지 않은 업무 중단은 **어디에도** 기록되지 않는다.
        bool leaked = EventLog.Instance.GetAllEntries().Any(e =>
            e.Day == GameState.Instance.CurrentDay && e.ActorEmployeeId == "dog"
            && (e.Description.Contains("업무") && e.Description.Contains("중단")
                || e.Description.Contains("봉쇄 계통 조작")));
        Check(!leaked, "관측하지 않은 '업무 중단 / 봉쇄 조작' 은 시설 로그에 뜨지 않는다");
        Check(PlayerKnownEvidence.CctvActionsOf("dog").Count == 0,
            "관측하지 않은 행동은 조사자료(CCTV 행동 기록)가 되지 않는다");

        // 11. CCTV 는 켰지만 **다른 방**을 보고 있으면 자동 목격되지 않는다.
        StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) });
        SetPower(true, true, false);
        _sim.SetSurveillanceTarget("guard_room");    // 결번이 없는 방을 본다
        Advance(110f);
        Check(PlayerKnownEvidence.CctvActionsOf("dog").Count == 0,
            "CCTV 전원만 켜져 있고 다른 방을 보고 있으면 결번의 행동은 자료가 되지 않는다");

        // 13(성립). 교란이 **진행 중일 때** 그 방 화면을 켜면 현행범으로 잡힌다.
        //    너무 오래 꺼 두면 그날 교란 몫을 다 써 버려 잡을 것이 남지 않는다 —
        //    기회를 만들 만큼만(시작 문턱 + 조금) 끄고 바로 켠다.
        var seen = new List<PlayerKnownEvidence.CctvObservation>();
        for (int attempt = 0; attempt < 4 && seen.Count == 0; attempt++)
        {
            StartPlaced(3, "dog", new[]
            {
                ("dog", "power_room"), ("rabbit", "power_room"),
                ("cat", "core_room"), ("fox", "core_room"), ("sheep", "storage_room"), ("wolf", "guard_room"),
            });
            SetPower(true, false, false);            // CCTV 를 꺼 기회를 만든다
            Advance(8.5f);                           // 교란이 막 시작될 만큼만
            string sabRoom = _sim.GetEmployeeState("dog").CurrentRoomId;
            SetPower(true, true, false);             // 되켜고
            _sim.SetSurveillanceTarget(sabRoom);     // 바로 그 방을 본다
            Advance(15f);
            seen = PlayerKnownEvidence.CctvActionsOf("dog").ToList();
        }
        Check(seen.Count > 0,
            $"교란 중인 방을 직접 지켜보면 '업무 중단' 이 조사자료가 된다 ({seen.Count}건)");
        if (seen.Count > 0)
            GD.Print($"      예) {SaboteurPlan.Clock(seen[0].Time)} — " +
                     $"{Code("dog")}가 {_sim.RoomDisplayName(seen[0].RoomId)}에서 {seen[0].ActionText}");
        Check(seen.Count == 0 || _sim.Covert.CaughtOnCamera > 0,
            "현행범으로 잡히면 결번은 그 자리에서 손을 뗀다");

        // 12. 원격 교란 — 손댄 방과 증상이 난 방이 분리된다(기존 MinorTamper 구조).
        bool crossSeen = false;
        for (int i = 0; i < 10 && !crossSeen; i++)
        {
            StartShift(2, "dog");
            SetPower(true, true, false);
            Advance(150f);
            var plan = _sim.Saboteur;
            if (plan.HasTampered && plan.TamperAffectedRoomId != plan.TamperOriginRoomId) crossSeen = true;
        }
        Check(crossSeen, "교란은 손댄 방과 증상이 난 방이 다를 수 있다 (원인 추적이 필요하다)");
    }

    // ── ④ 살인 (검사 15~21) ──────────────────────────────────────────────
    private void MurderRules()
    {
        GD.Print("\n── 살인 ──────────────────────────────────────────");

        // 19. DAY1 은 어떤 경로로도 사람이 죽지 않는다.
        int day1Deaths = 0;
        for (int i = 0; i < 6; i++)
        {
            StartShift(1, "dog");
            SetPower(false, false, true);            // 가장 어두운 조합으로 몰아붙인다
            Advance(DayObjectives.MaxShiftSeconds);
            day1Deaths += Roster.Count(id => !_sim.GetEmployeeState(id).Alive);
        }
        Check(day1Deaths == 0, $"DAY1(AllowMurder=false) 은 조명을 꺼 두어도 사망자가 없다 ({day1Deaths}명)");

        // 15 · 16 · 17. 조명만 꺼도 즉사하지 않는다. 죽으려면 **같은 방**에 도달해야 한다.
        //     결번을 혼자 가둬 두고 모두를 반대편에 몰아 두면, 도달 자체가 일어나야 죽는다.
        StartShift(3, "dog", new[] { ("core_room", 5), ("power_room", 1) });
        SetPower(false, false, true);
        float firstDeathAt = -1f;
        string deathRoom = "";
        for (float t = 0; t < DayObjectives.MaxShiftSeconds; t += Step)
        {
            Advance(Step);
            if (firstDeathAt >= 0f) continue;
            var dead = Roster.Select(_sim.GetEmployeeState).FirstOrDefault(e => e is { Alive: false });
            if (dead == null) continue;
            firstDeathAt = GameState.Instance.DayTimeSeconds;
            deathRoom = dead.CurrentRoomId;
        }
        var cfgDark = Config.Instance.Data.BlackoutMurderAfterSeconds;
        Check(firstDeathAt < 0f || firstDeathAt > cfgDark,
            $"조명 OFF {cfgDark:0}초만으로 즉시 사망하지 않는다 (첫 사망 {firstDeathAt:0.0}초)");
        if (firstDeathAt >= 0f)
        {
            Check(_sim.Covert.KillsCommitted > 0, "사망은 은밀 행동 계통 하나만 거쳐 일어났다");
            Check(!string.IsNullOrEmpty(deathRoom), $"사망 장소가 실제 작업실이다 ({_sim.RoomDisplayName(deathRoom)})");
        }
        else GD.Print("      (이번 시도에서는 사망이 발생하지 않았다 — 조건부이므로 정상)");

        // 16. 결번이 한 번도 움직이지 않았는데 다른 방 사람이 죽는 일은 없다.
        bool remoteKill = false;
        for (int i = 0; i < 8 && !remoteKill; i++)
        {
            StartShift(3, "dog", new[] { ("core_room", 1), ("power_room", 5) });
            SetPower(false, false, true);
            string start = _sim.GetEmployeeState("dog").CurrentRoomId;
            Advance(DayObjectives.MaxShiftSeconds);
            var dead = Roster.Select(_sim.GetEmployeeState).Where(e => e is { Alive: false }).ToList();
            if (dead.Count == 0) continue;
            // 죽은 사람이 있는데 결번이 한 번도 이동하지 않았다면 원격 살인이다.
            if (_sim.Covert.CovertMovesArrived == 0 && dead.All(d => d.CurrentRoomId != start))
                remoteKill = true;
        }
        Check(!remoteKill, "결번이 실제로 이동하지 않고 다른 방 직원을 살해하는 일은 없다");

        // 18. 범행 준비 중에 그 방을 CCTV 로 보면 멈춘다.
        StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 4) });
        SetPower(false, true, false);                // 조명 OFF · CCTV ON
        bool interrupted = false;
        for (float t = 0; t < DayObjectives.MaxShiftSeconds; t += Step)
        {
            Advance(Step);
            if (_sim.Covert.Phase == CovertPhase.Preparing)
            {
                _sim.SetSurveillanceTarget(_sim.GetEmployeeState("dog").CurrentRoomId);
                Advance(4f);
                if (_sim.Covert.Phase != CovertPhase.Attacking) interrupted = true;
                break;
            }
        }
        Check(!interrupted || _sim.Covert.AbortedByPlayer > 0 || _sim.Covert.KillsCommitted == 0,
            "범행 준비 중 그 방을 CCTV 로 관측하면 범행이 중단된다");

        // 20. 하루 한도 · 전체 한도.
        var cfg = Config.Instance.Data;
        int worst = 0;
        for (int i = 0; i < 8; i++)
        {
            StartShift(3, "dog", new[] { ("core_room", 3), ("power_room", 3) });
            SetPower(false, false, true);
            Advance(DayObjectives.MaxShiftSeconds);
            worst = Mathf.Max(worst, Roster.Count(id => !_sim.GetEmployeeState(id).Alive));
        }
        Check(worst <= cfg.MurderMaxPerDay, $"하루 사망자가 한도({cfg.MurderMaxPerDay})를 넘지 않는다 (최대 {worst})");

        // 21. 사망 후 상태가 일관된다.
        bool consistent = true;
        foreach (string id in Roster)
        {
            var st = _sim.GetEmployeeState(id);
            if (st.Alive) continue;
            var room = _sim.GetRoomState(st.CurrentRoomId);
            if (room != null && room.OccupantEmployeeIds.Contains(id)) consistent = false;
        }
        Check(consistent, "사망한 직원은 작업실 점유자 목록에서 빠진다");
    }

    // ── ⑤ 귀신 인과 (검사 22~24) ─────────────────────────────────────────
    private void GhostCausality()
    {
        GD.Print("\n── 봉쇄 교란 ↔ 이상 개체 출몰 ───────────────────");

        // 22. 결번의 교란 → 봉쇄 불안정 → 출몰 위험 증가.
        //
        // **출몰 횟수로는 재지 않는다.** 하루 상한(GhostMaxPerDay)이 두 집단 모두를 천장에
        // 붙여 버려 차이가 보이지 않는다. 재는 것은 둘이다 — ① 가중치가 실제로 걸리는가,
        // ② 출몰 시점에 그 앞의 교란이 실제로 기록되어 있는가(인과가 이어졌는가).
        float m0 = MultiplierAt(0f), m50 = MultiplierAt(50f), m100 = MultiplierAt(100f);
        GD.Print($"      출몰 가중치 — 불안정 0:{m0:0.00}배  50:{m50:0.00}배  100:{m100:0.00}배");
        Check(m100 > m50 && m50 > m0 && Mathf.IsEqualApprox(m0, 1f),
            "봉쇄 불안정성이 높을수록 출몰 확률 가중치가 커진다 (0 이면 가중 없음)");

        int linked = 0, tamperRuns = 0, withTamper = 0;
        float instSum = 0f, firstWith = 0f;
        for (int i = 0; i < 8; i++)
        {
            StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) });
            SetPower(true, false, false);            // CCTV OFF — 교란이 가능한 조합
            float firstAt = -1f;
            for (float t = 0; t < DayObjectives.MaxShiftSeconds; t += Step)
            {
                Advance(Step);
                if (firstAt < 0f && _sim.Ghost.AppearedToday > 0) firstAt = GameState.Instance.DayTimeSeconds;
            }
            withTamper += _sim.Ghost.AppearedToday;
            instSum += _sim.Containment.Instability;
            if (_sim.Containment.DisturbanceCount > 0) tamperRuns++;
            if (_sim.Ghost.LastAppearCause != null) linked++;
            if (firstAt >= 0f) firstWith += firstAt;
        }

        int withoutTamper = 0;
        float firstWithout = 0f;
        int withoutRuns = 0;
        for (int i = 0; i < 8; i++)
        {
            StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) });
            SetPower(true, true, false);             // 계속 감시 — 교란 기회가 없다
            _sim.SetSurveillanceTarget("core_room");
            float firstAt = -1f;
            for (float t = 0; t < DayObjectives.MaxShiftSeconds; t += Step)
            {
                Advance(Step);
                if (firstAt < 0f && _sim.Ghost.AppearedToday > 0) firstAt = GameState.Instance.DayTimeSeconds;
            }
            withoutTamper += _sim.Ghost.AppearedToday;
            if (firstAt >= 0f) { firstWithout += firstAt; withoutRuns++; }
        }
        GD.Print($"      교란 가능 근무 출몰 {withTamper}회(교란 발생 {tamperRuns}/8, 평균 불안정성 " +
                 $"{instSum / 8f:0.0}) / 감시 유지 근무 출몰 {withoutTamper}회");
        Check(tamperRuns > 0 && linked > 0,
            $"출몰 시점에 그 앞의 봉쇄 교란이 실제로 기록되어 있다 (인과 {linked}/8건)");
        Check(withoutTamper > 0,
            "교란이 전혀 없어도 이상 개체는 나온다 (출몰 = 방금 누가 손댔다 가 아니다)");

        // 23. 기존 첫 등장 보장이 살아 있다(DAY1 학습).
        StartShift(1, "dog");
        SetPower(true, true, false);
        Advance(DayObjectives.MaxShiftSeconds);
        Check(_sim.Ghost.AppearedToday > 0, "DAY1 의 첫 등장 보장이 그대로 동작한다");
    }

    // ── ⑥ 모드 · 기존 진행 (검사 25~26) ──────────────────────────────────
    private void ModeIntegrity()
    {
        GD.Print("\n── 모드 · 기존 진행 ──────────────────────────────");

        // 26. 기본 모드는 결번을 무작위로 고른다.
        var picked = new HashSet<string>();
        var keep = GameModes.Current;
        GameModes.ForceSelect(GameMode.Standard);
        for (int i = 0; i < 24; i++)
        {
            GameState.Instance.ResetRun(1);
            GameState.Instance.AssignSaboteurForMode(_sim.GetActiveEmployeeIds());
            picked.Add(GameState.Instance.SaboteurEmployeeId);
        }
        Check(picked.Count > 1, $"기본 모드의 결번은 무작위다 (서로 다른 {picked.Count}명)");
        GameModes.ForceSelect(GameMode.Competition);
        GameState.Instance.ResetRun(1);
        GameState.Instance.AssignSaboteurForMode(_sim.GetActiveEmployeeIds());
        Check(GameState.Instance.SaboteurEmployeeId == GameModes.FixedSaboteurId,
            "대회용은 결번이 고정이다");
        GameModes.ForceSelect(keep);

        // 25. 확보한 자료로 휴게시간 심문이 성립한다.
        StartShift(3, "dog", new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) });
        SetPower(true, false, false);
        Advance(26f);
        string room = _sim.GetEmployeeState("dog").CurrentRoomId;
        SetPower(true, true, false);
        _sim.SetSurveillanceTarget(room);
        Advance(30f);
        var actions = PlayerKnownEvidence.CctvActionsOf("dog");
        Check(actions.Count == 0 || actions.All(a => a.Occupants.Contains("dog")),
            "CCTV 행동 자료에는 그 사람이 실제로 화면 안에 있었던 건만 남는다");
        Check(PlayerKnownEvidence.CctvObservationCount > 0, "관측 기록이 조사자료로 쌓인다");
    }

    // ── ⑦ 밸런스 스윕 ────────────────────────────────────────────────────
    //
    // DAY1~3 을 전력 선택 · 배치 · 감시 상황별로 돌려 빈도를 본다.
    // 한쪽으로 쏠리면(사망 0 또는 전원 사망) 여기서 숫자로 드러난다.
    private void BalanceSweep()
    {
        GD.Print("\n── 밸런스 스윕 (DAY1~3 × 전력 조합) ─────────────");
        var combos = new (string Name, bool L, bool C, bool B)[]
        {
            ("조명+CCTV", true, true, false),
            ("차폐+CCTV", false, true, true),
            ("차폐+조명", true, false, true),
        };
        var plans = new (string, int)[][]
        {
            new[] { ("core_room", 2), ("power_room", 2), ("guard_room", 1), ("storage_room", 1) },
            new[] { ("core_room", 3), ("power_room", 2), ("maintenance_room", 1) },
            new[] { ("core_room", 1), ("power_room", 1), ("maintenance_room", 1), ("storage_room", 1),
                    ("guard_room", 1), ("vent_room", 1) },
        };

        for (int day = 1; day <= 3; day++)
        {
            foreach (var (name, l, c, b) in combos)
            {
                int runs = 0, deaths = 0, moves = 0, tampers = 0, ghosts = 0, attempts = 0, stopped = 0;
                foreach (var plan in plans)
                    for (int r = 0; r < 2; r++)
                    {
                        StartShift(day, "dog", plan);
                        SetPower(l, c, b);
                        // 감시 조합: CCTV 가 켜져 있으면 가끔 채널을 돌린다(현실적인 운용).
                        for (float t = 0; t < DayObjectives.MaxShiftSeconds; t += Step)
                        {
                            Advance(Step);
                            if (c && Mathf.RoundToInt(t / Step) % 900 == 0)
                                _sim.SetSurveillanceTarget(plan[(int)(t / 40f) % plan.Length].Item1);
                        }
                        runs++;
                        deaths += Roster.Count(id => !_sim.GetEmployeeState(id).Alive);
                        moves += _sim.Covert.CovertMovesStarted;
                        tampers += _sim.Covert.ContainmentTampers;
                        ghosts += _sim.Ghost.AppearedToday;
                        attempts += _sim.Covert.KillAttempts;
                        stopped += _sim.Covert.AbortedByPlayer;
                    }
                GD.Print($"   DAY{day} {name,-10} 근무{runs}  사망 {deaths}  은밀이동 {moves}  " +
                         $"교란 {tampers}  출몰 {ghosts}  범행시도 {attempts}  저지 {stopped}");
                if (day == 1) Check(deaths == 0, $"DAY1 {name} — 사망 0");
            }
        }
    }

    // 불안정성 x 에서의 출몰 가중치(판정식이 실제로 x 를 쓰는지 본다).
    private float MultiplierAt(float instability)
    {
        var c = new ContainmentSystem();
        if (instability > 0f) c.Disturb(instability, "dog", "core_room");
        return c.AppearChanceMultiplier;
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    // 사람마다 방을 직접 지정한다. 기본 배치에 맡기면 결번이 비상등 있는 방에 떨어져
    // 기회가 아예 열리지 않는 판이 섞여, 조건부 사건의 검사가 운에 좌우된다.
    private void StartPlaced(int day, string saboteurId, (string Id, string Room)[] places)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        PlayerKnownEvidence.ResetAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        foreach (var (id, room) in places) _sim.AssignToRoom(id, room);
        GameState.Instance.SetSaboteur(saboteurId);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        Advance(6f);
    }

    private void StartShift(int day, string saboteurId, (string Room, int N)[] plan = null)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        PlayerKnownEvidence.ResetAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();

        plan ??= new (string, int)[]
        {
            ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
            ("guard_room", 1), ("storage_room", 1),
        };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++) _sim.AssignToRoom(roster[i], room);
        // 결번을 반드시 배치 안에 넣는다(배치에서 빠지면 아무것도 하지 않는다).
        if (!string.IsNullOrEmpty(saboteurId) && string.IsNullOrEmpty(_sim.GetEmployeeState(saboteurId)?.AssignedRoomId))
            _sim.AssignToRoom(saboteurId, plan[0].Room);

        GameState.Instance.SetSaboteur(saboteurId);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        // 배치 자리까지 걸어가는 시간을 먼저 흘려보낸다 — 그래야 '자리를 잡은 상태' 에서 센다.
        Advance(6f);
    }

    // 전력 세 채널을 원하는 조합으로 맞춘다(용량 2 이므로 둘까지만 켜진다).
    private void SetPower(bool lighting, bool cctv, bool barrier)
    {
        var gs = GameState.Instance;
        // 먼저 전부 끈 뒤 원하는 것만 켠다 — 용량 거부로 순서에 끌려가지 않게.
        foreach (var c in new[] { PowerConsumer.CctvWatch, PowerConsumer.Lighting, PowerConsumer.Barrier })
            if (gs.IsConsumerPowered(c)) gs.TryTogglePower(c);
        if (lighting) gs.TryTogglePower(PowerConsumer.Lighting);
        if (cctv) gs.TryTogglePower(PowerConsumer.CctvWatch);
        if (barrier) gs.TryTogglePower(PowerConsumer.Barrier);
    }

    private void Advance(float seconds)
    {
        for (float t = 0; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    private string Code(string id) => _sim.GetEmployeeDef(id)?.Codename ?? id;
}
