using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 복도 · 차폐 시스템 자동 검증 (중앙제어실 3방향 구조).
//
//   godot --headless --path . res://scenes/debug/CorridorTest.tscn --quit-after 8000
//
// 보는 것은 "그려 놓은 차폐" 가 아니라 **실제로 길이 끊기는가** 다.
//   ① 중앙제어실로 들어오는 길이 정확히 셋인가 (지도만이 아니라 길찾기에서도)
//   ② 남측 영구 봉쇄가 정말로 통과 불가인가 — 그리고 경비실↔정비실은 멀쩡한가
//   ③ 격리실 · 의무실이 고립되지 않았는가
//   ④ 차폐문 상태 기계가 어떤 순서로 눌러도 꼬이지 않는가
//   ⑤ 차폐가 **실제로 지나가는 직원에게만** 값을 매기는가
//   ⑥ 전력이 끊기거나 근무가 끝나면 반드시 열리는가(영구 봉쇄는 예외)
public partial class CorridorTest : Node
{
    private const float Step = 1f / 30f;
    private const string Hub = FacilitySimulation.DeployOriginRoomId;

    private FacilitySimulation _sim;
    private CorridorNet _net;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        _net = _sim.Corridors;
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 복도 · 차폐 검증 (중앙제어실 3방향) ################");
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        SectionF();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 중앙제어실 접근 경로 ─────────────────────────────────────

    private void SectionA()
    {
        Head("A", "중앙제어실로 들어오는 길은 정확히 셋");

        // 데이터가 아니라 **길찾기가 쓰는 이웃 목록**으로 센다.
        // 화면은 막혔는데 실제로는 통과하는 버그를 여기서 잡는다.
        var hubNeighbors = WalkNeighbors(Hub).OrderBy(x => x).ToList();
        GD.Print($"      중앙제어실 이웃: {string.Join(", ", hubNeighbors)}");
        Ok(hubNeighbors.Count == 3, $"직접 연결 {hubNeighbors.Count}곳");
        Ok(hubNeighbors.SequenceEqual(new[] { "core_room", "power_room", "storage_room" }),
            "코어실 · 발전실 · 저장고 셋뿐이다");

        foreach (string gone in new[] { "guard_room", "maintenance_room", "isolation_room", "medical_room" })
        {
            Ok(!hubNeighbors.Contains(gone), $"{gone} 직통 연결 없음");
            // 반대 방향 데이터도 본다 — 한쪽에만 적혀 있어도 Neighbors 는 이어 준다.
            Ok(!WalkNeighbors(gone).Contains(Hub), $"{gone} 쪽 데이터에도 중앙제어실이 없다");
        }

        Head("A", "차폐문 셋 + 남측 영구 봉쇄");
        var doors = _net.Blockable.OrderBy(s => s.Side).ToList();
        Ok(doors.Count == 3, $"차폐 가능 구간 {doors.Count}개");
        foreach (var s in doors)
            GD.Print($"      {s.Side,-6} {s.Id,-16} {s.DisplayName,-12} {s.RoomA} ↔ {s.RoomB}  cam={s.CctvCameraId}");

        Ok(doors.Any(s => s is { Side: CorridorSide.North, RoomB: "core_room", CctvCameraId: "CAM-N01" }),
            "북 — 코어실 방향 · CAM-N01");
        Ok(doors.Any(s => s is { Side: CorridorSide.West, RoomB: "power_room", CctvCameraId: "CAM-W01" }),
            "서 — 발전실 방향 · CAM-W01");
        Ok(doors.Any(s => s is { Side: CorridorSide.East, RoomB: "storage_room", CctvCameraId: "CAM-E01" }),
            "동 — 저장고 방향 · CAM-E01");
        Ok(doors.All(s => s.RoomA == Hub), "셋 다 중앙제어실 바로 앞 복도다(작업실 출입문이 아니다)");
        Ok(doors.All(s => s.ThreatLane), "셋 다 괴물 접근 레인으로 등록돼 있다");

        var seal = _net.Segments.FirstOrDefault(s => s.PermanentSeal);
        Ok(seal != null, "남측 영구 봉쇄 격벽이 등록돼 있다");
        Ok(seal is { Side: CorridorSide.South, RoomB: "isolation_room" }, "남 — 격리실 방향의 옛 통로");
        Ok(seal is { IsBlockable: false, Sealed: true }, "조작 불가 · 항상 봉쇄");
        Ok(!_net.IsPassable(Hub, "isolation_room"), "중앙제어실 → 격리실 직통은 통과 불가");
        Ok(_sim.FindPath(Hub, "isolation_room").All(r => r != Hub)
           && !_sim.FindPath(Hub, "isolation_room").Contains("isolation_room0"),
            "길찾기도 그 길을 쓰지 않는다");
    }

    // 길찾기가 실제로 쓰는 이웃(차폐 상태까지 반영된 것).
    private List<string> WalkNeighbors(string roomId) =>
        _sim.GetRoomIds().Where(other => other != roomId && _sim.FindPath(roomId, other).Count == 1).ToList();

    // ── B. 고립 없음 ────────────────────────────────────────────────

    private void SectionB()
    {
        Head("B", "아무 방도 고립되지 않았다");
        Reset();

        // 경비실↔정비실 — 남측 격벽이 **막아서는 안 되는** 정상 통로.
        var gm = _sim.FindPath("guard_room", "maintenance_room");
        Ok(gm.Count == 1 && gm[0] == "maintenance_room",
            $"경비실 → 정비실 한 걸음 ({string.Join(" → ", gm)})");
        Ok(_net.IsPassable("guard_room", "maintenance_room"), "그 통로는 열려 있다");

        // 격리실 — 이제 의무실을 거쳐 간다.
        var iso = _sim.FindPath(Hub, "isolation_room");
        Ok(iso.Count > 0, $"중앙제어실 → 격리실 도달 가능 ({string.Join(" → ", iso)})");
        Ok(iso.Contains("medical_room"), "의무실을 거쳐 간다");
        Ok(!iso.Contains(Hub), "중앙제어실을 경유지로 쓰지 않는다");
        var back = _sim.FindPath("isolation_room", "core_room");
        Ok(back.Count > 0, $"격리 해제 후 작업실 복귀도 가능 ({string.Join(" → ", back)})");

        // 모든 방 쌍이 서로 닿는가.
        var rooms = _sim.GetRoomIds().Where(r => _sim.GetRoomDef(r) != null).ToList();
        var unreachable = new List<string>();
        foreach (string a in rooms)
            foreach (string b in rooms)
            {
                if (a == b) continue;
                // 제한 구역(중앙제어실 · 격리실)은 경유지로 못 쓰므로 출발지로는 세지 않는다.
                if (_sim.GetRoomDef(a).IsRestricted && a != Hub) continue;
                if (_sim.FindPath(a, b).Count == 0) unreachable.Add($"{a}→{b}");
            }
        foreach (string u in unreachable.Take(6)) GD.Print($"      닿지 않음: {u}");
        Ok(unreachable.Count == 0, $"닿지 않는 방 쌍 {unreachable.Count}건");
    }

    // ── C. 차폐문 상태 기계 ─────────────────────────────────────────

    private void SectionC()
    {
        Head("C", "차폐문 상태 기계");
        Reset();
        var seg = _net.ById("corridor_east");

        SetBarrierPower(false);
        Ok(!_net.CanSeal(seg, out string why) && why.Contains("전력"), $"차폐 전력이 없으면 거부 — \"{why}\"");
        SetBarrierPower(true);

        Ok(_net.Seal(seg.Id, out _) && seg.State == BarrierState.Closing, "닫기 지시 → CLOSING");
        Ok(seg.Passable, "닫히는 중에는 아직 지나갈 수 있다 (문 밑으로 뛰어들 수 있다)");
        Tick(0.3f);
        Ok(seg.State == BarrierState.Closing && seg.Shut is > 0.2f and < 0.8f, $"구동 중 {seg.Shut:0.00}");
        Tick(0.7f);
        Ok(seg.State == BarrierState.Sealed && Mathf.IsEqualApprox(seg.Shut, 1f), "완전히 닫히면 SEALED");
        Ok(!seg.Passable && !_net.IsPassable(Hub, "storage_room"), "그제서야 길이 끊긴다");

        var other = _net.ById("corridor_north");
        Ok(!_net.CanSeal(other, out string why2) && why2.Contains("한도"), $"동시 차폐 한도 — \"{why2}\"");
        Ok(other.State == BarrierState.Open, "거부당한 쪽 문은 저절로 움직이지 않는다");

        Ok(_net.Select("corridor_west") && _net.SelectedId == "corridor_west", "제어 대상 변경");
        Ok(seg.State == BarrierState.Sealed && other.State == BarrierState.Open,
            "대상을 바꿨다고 문이 저절로 여닫히지 않는다");
        Ok(!_net.Select("corridor_south_seal") && _net.SelectedId == "corridor_west",
            "봉쇄된 남측 격벽은 제어 대상이 될 수 없다");

        Ok(_net.Unseal(seg.Id) && seg.State == BarrierState.Opening, "열기 지시 → OPENING");
        Tick(1.0f);
        Ok(seg.State == BarrierState.Open && _net.IsPassable(Hub, "storage_room"), "열리면 길이 돌아온다");
        Ok(!_net.CanSeal(seg, out string why3) && why3.Contains("냉각"), $"재작동 냉각 — \"{why3}\"");
        Tick(seg.ReengageCooldownSeconds + 0.1f);
        Ok(_net.CanSeal(seg, out _), "냉각이 끝나면 다시 닫을 수 있다");

        // 유지 상한.
        Reset();
        _net.Seal(seg.Id, out _);
        Tick(seg.DriveSeconds + 0.1f);
        Tick(seg.MaxSealSeconds + 0.2f);
        Ok(!seg.Sealed, $"유지 상한 {seg.MaxSealSeconds:0}초가 지나면 저절로 열린다");

        // 전력 상실 → 안전측 개방.
        Reset();
        _net.Seal(seg.Id, out _);
        Tick(seg.DriveSeconds + 0.1f);
        SetBarrierPower(false);
        Tick(seg.DriveSeconds + 0.2f);
        Ok(seg.State == BarrierState.Open, "차폐 전력이 끊기면 문이 열린다(직원 고립 방지)");
        SetBarrierPower(true);

        // 연타.
        Reset();
        for (int i = 0; i < 20; i++) { _net.Toggle(seg.Id, out _); Tick(0.05f); }
        Tick(3f);
        Ok(seg.State is BarrierState.Open or BarrierState.Sealed && seg.Shut is 0f or 1f,
            $"20번 연타해도 중간에 끼지 않는다 (지금 {seg.StatusText})");

        // BARRIER 레버.
        Head("C", "BARRIER 레버");
        Reset();
        var gs = GameState.Instance;
        if (gs.IsConsumerPowered(PowerConsumer.Barrier)) gs.TryTogglePower(PowerConsumer.Barrier);
        _net.Select("corridor_east");
        Ok(_net.LeverToggle(out _) && gs.IsConsumerPowered(PowerConsumer.Barrier), "레버 올리기 → 전력 슬롯 점유");
        Tick(seg.DriveSeconds + 0.1f);
        Ok(seg.Sealed, "고른 통로의 문이 실제로 내려왔다");
        Ok(_net.LeverToggle(out _), "레버 내리기");
        Tick(seg.DriveSeconds + 0.1f);
        Ok(!seg.Sealed && !gs.IsConsumerPowered(PowerConsumer.Barrier), "문이 열리고 슬롯도 반납된다");

        Reset();
        if (gs.IsConsumerPowered(PowerConsumer.Barrier)) gs.TryTogglePower(PowerConsumer.Barrier);
        foreach (var s in _net.Segments) s.CooldownLeft = 0f;
        gs.TriggerPowerAccident(2);
        Ok(!_net.LeverToggle(out string lw) && lw.Contains("용량"), $"용량이 없으면 거부 — \"{lw}\"");
        Ok(!gs.IsConsumerPowered(PowerConsumer.Barrier) && _net.Segments.All(x => !x.Moving),
            "실패하면 슬롯을 반납하고 문도 안 움직인다");
        gs.RepairPowerAccident();
        Reset();
    }

    // ── D. 차폐가 직원에게 매기는 값 ────────────────────────────────

    private void SectionD()
    {
        Head("D", "차폐는 **그 길을 실제로 지나는 직원에게만** 값을 매긴다");
        StartShift();
        var east = _net.ById("corridor_east");
        east.MaxSealSeconds = 0f;

        // ① 지나가지 않는 직원은 아무 영향이 없다.
        //    경비실 ↔ 정비실은 중앙제어실 근처에도 가지 않는다.
        string walker = _sim.GetActiveEmployeeIds().First(id =>
            _sim.GetEmployeeState(id)?.CurrentRoomId == "guard_room");
        _net.Seal(east.Id, out _);
        Tick(east.DriveSeconds + 0.1f);
        Ok(east.Sealed, "동측 차폐");
        _sim.AssignToRoom(walker, "maintenance_room");
        float t1 = RunUntil(() => _sim.GetEmployeeState(walker).CurrentRoomId == "maintenance_room", 40f);
        Ok(_sim.GetEmployeeState(walker).CurrentRoomId == "maintenance_room",
            $"경비실 → 정비실 이동은 그대로다 ({t1:0.0}초)");
        Ok(!_sim.GetEmployeeState(walker).BlockedByBarrier, "'차폐로 이동 불가' 가 붙지 않았다");

        // ② 실제로 그 길을 쓰는 이동은 우회한다 — 중앙제어실에서 저장고로 나가는 배치.
        string rookie = _sim.GetActiveEmployeeIds().First(id =>
            _sim.GetEmployeeState(id)?.CurrentRoomId == Hub);
        var detour = _sim.FindPath(Hub, "storage_room");
        Ok(detour.Count > 1 && detour.Last() == "storage_room",
            $"중앙제어실 → 저장고가 {detour.Count}걸음으로 늘었다 ({string.Join(" → ", detour)})");
        _sim.AssignToRoom(rookie, "storage_room");
        float t2 = RunUntil(() => _sim.GetEmployeeState(rookie).CurrentRoomId == "storage_room", 90f);
        Ok(_sim.GetEmployeeState(rookie).CurrentRoomId == "storage_room", $"우회해서 도착했다 ({t2:0.0}초)");
        Ok(!AnyoneInsideSealed(), "아무도 닫힌 문 안에 끼어 있지 않다");

        east.MaxSealSeconds = 9f;
        Reset();
    }

    // ── E. 영구 봉쇄는 무슨 일이 있어도 열리지 않는다 ───────────────

    private void SectionE()
    {
        Head("E", "남측 영구 봉쇄 불변");
        var seal = _net.Segments.First(s => s.PermanentSeal);
        var gs = GameState.Instance;

        Ok(!_net.Seal(seal.Id, out string r1) && r1.Contains("차폐 불가"), $"닫기 지시 거부 — \"{r1}\"");
        Ok(!_net.Unseal(seal.Id) && seal.Sealed, "열기 지시도 먹지 않는다");
        Ok(!_net.Toggle(seal.Id, out _) && seal.Sealed, "토글도 먹지 않는다");

        SetBarrierPower(false);
        Tick(2f);
        Ok(seal.Sealed && !_net.IsPassable(Hub, "isolation_room"), "전력이 없어도 봉쇄된 채다");
        gs.TriggerPowerAccident(3);
        Tick(2f);
        Ok(seal.Sealed, "전력 사고가 나도 열리지 않는다");
        gs.RepairPowerAccident();
        SetBarrierPower(true);

        _net.ResetAll();
        Ok(seal.Sealed && Mathf.IsEqualApprox(seal.Shut, 1f), "근무가 바뀌어도 봉쇄된 채다");
        _sim.ResetForNewShift();
        Ok(seal.Sealed, "새 근무에서도 그대로");
        _sim.ResetRun();
        Ok(seal.Sealed, "새 게임에서도 그대로");
        Ok(_net.Segments.Where(s => !s.PermanentSeal).All(s => s.State == BarrierState.Open),
            "반면 일반 차폐문은 전부 열린 채로 시작한다");

        // PAD 전원 유지.
        Head("E", "PAD 전원 유지");
        Ok(gs.IsConsumerPowered(PowerConsumer.Sensor), "패드는 켜져 있다");
        Ok(!gs.TryTogglePower(PowerConsumer.Sensor), "전력 패널 레버로 끌 수 없다(상시 전원)");
        gs.TryTogglePower(PowerConsumer.Lighting);
        gs.TryTogglePower(PowerConsumer.CctvWatch);
        Ok(gs.IsConsumerPowered(PowerConsumer.Sensor), "조명과 CCTV 를 둘 다 꺼도 패드는 그대로다");
        gs.TryTogglePower(PowerConsumer.Lighting);
        gs.TryTogglePower(PowerConsumer.CctvWatch);
        Reset();
    }

    // ── F. 기절 이송 · 격리 · 교육일 ────────────────────────────────

    private void SectionF()
    {
        Head("F", "기절 이송과 격리");
        StartShift();

        // 의무실까지의 길이 길어졌다(중앙제어실 직통이 없어졌으므로).
        var toMed = _sim.FindPath("storage_room", FacilitySimulation.MedicalRoomIdPublic);
        Ok(toMed.Count > 0, $"저장고 → 의무실 {toMed.Count}걸음 ({string.Join(" → ", toMed)})");

        var ids = _sim.GetActiveEmployeeIds();
        _sim.AssignToRoom(ids[0], "storage_room");
        _sim.AssignToRoom(ids[1], "storage_room");
        RunUntil(() => _sim.GetEmployeeState(ids[1]).CurrentRoomId == "storage_room", 30f);

        string victim = ids[0];
        var v = _sim.GetEmployeeState(victim);
        _sim.AddStress(victim, 200f, "검사");
        RunUntil(() => v.Incapacitated, 20f);
        Ok(v.Incapacitated, $"{victim} 저장고에서 기절");

        RunUntil(() => v.Faint == FaintPhase.AwaitingDecision, 30f);
        _sim.Rescue.NoAnswer(victim);
        float t = RunUntil(() => v.CurrentRoomId == FacilitySimulation.MedicalRoomIdPublic, 240f);
        Ok(v.CurrentRoomId == FacilitySimulation.MedicalRoomIdPublic,
            $"의무실까지 옮겨졌다 ({t:0.0}초 · 지금 {v.CurrentRoomId})");

        // 격리 — 중앙제어실 직통이 사라졌으므로 의무실을 거쳐 걸어가야 한다.
        string who = ids.First(id => id != victim
            && _sim.GetEmployeeState(id) is { Alive: true, Incapacitated: false });
        Ok(_sim.IsolateEmployee(who), $"{who} 격리 명령");
        RunUntil(() => _sim.GetEmployeeState(who).Isolation == IsolationPhase.InRoom, 180f);
        var w = _sim.GetEmployeeState(who);
        Ok(w.Isolation == IsolationPhase.InRoom, "격리실 수용까지 도달");
        Ok(w.CurrentRoomId == FacilitySimulation.IsolationRoomIdPublic,
            $"제 발로 격리실까지 걸어갔다 (지금 {w.CurrentRoomId})");
        Ok(!AnyoneInsideSealed(), "벽을 뚫지 않았다");
        Reset();

        // 교육일 회귀.
        Head("F", "DAY0 교육일 회귀");
        GameState.Instance.ResetRun(0);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        _sim.AssignToRoom(roster[0], "core_room");
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        Ok(_net.Segments.Where(s => !s.PermanentSeal).All(s => s.State == BarrierState.Open),
            "교육일은 모든 차폐문이 열린 채로 시작한다");
        Tick(8f);
        Ok(_sim.GetEmployeeState(roster[0]).CurrentRoomId == "core_room",
            "배치 이동이 평소처럼 끝난다");
        Reset();
    }

    private bool AnyoneInsideSealed() =>
        _sim.GetEmployeeIds().Select(id => _sim.GetEmployeeState(id)).Any(st =>
            st != null && st.IsMoving &&
            _net.Segments.Any(s => s.Sealed && s.Touches(st.CurrentRoomId)
                                   && s.Other(st.CurrentRoomId) == st.TargetRoomId));

    // ── 도구 ────────────────────────────────────────────────────────

    private void Reset()
    {
        _net.ResetAll();
        SetBarrierPower(true);
        foreach (var s in _net.Segments) s.CooldownLeft = 0f;
    }

    private static void SetBarrierPower(bool on)
    {
        var gs = GameState.Instance;
        if (gs.IsConsumerPowered(PowerConsumer.Barrier) != on) gs.TryTogglePower(PowerConsumer.Barrier);
    }

    private void Tick(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private float RunUntil(System.Func<bool> done, float limitSeconds)
    {
        float t = 0f;
        while (t < limitSeconds && !done())
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            t += Step;
        }
        return t;
    }

    // 직원 다섯은 작업실에, 한 명은 중앙제어실에 남겨 둔다
    // (배치 이동이 차폐를 만나는 상황을 만들려면 아직 안 나간 사람이 필요하다).
    private void StartShift()
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        RepairApprovalSystem.ResetAll();
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "storage_room", "power_room", "maintenance_room", "guard_room", "core_room" };
        for (int i = 0; i < roster.Count && i < rooms.Length; i++) _sim.AssignToRoom(roster[i], rooms[i]);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        SetBarrierPower(true);
        Tick(14f);
    }

    private void Head(string tag, string title) => GD.Print($"\n===== [{tag}] {title} =====");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
