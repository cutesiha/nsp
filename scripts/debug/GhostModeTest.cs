using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 모드별 이상 개체 동작 검증 (지시서 §1 ~ §4, §9).
//
//   godot --headless --path . res://scenes/debug/GhostModeTest.tscn --quit-after 12000
//
// 보는 것은 **두 모드가 서로를 망가뜨리지 않는가** 다.
//   ① 기본 모드 — 보고 있으면 소멸한다. 못 찾으면 사고가 난다. 복도로 나가지 않는다
//   ② 대회용   — 아무리 봐도 소멸하지 않는다. 떠나서 **복도로 걸어 나간다**
//   ③ 작업실을 떠난 개체가 그 방 CCTV 에 남아 있지 않은가
//   ④ 들어오는 복도가 실제 방 연결 그래프와 맞는가(서쪽 방에서 나오면 서측으로)
//   ⑤ 사고가 매번 나지는 않는가 — 그래도 목격 기록은 반드시 남는가
//   ⑥ 소멸 업적이 대회용에서 잘못 달성되지 않는가
public partial class GhostModeTest : Node
{
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 모드별 이상 개체 검증 ################");
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        SectionF();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 모드 플래그 ──────────────────────────────────────────────

    private void SectionA()
    {
        Head("A", "모드 플래그는 서로 반대다");
        GameModes.ForceSelect(GameMode.Standard);
        Ok(GameModes.GhostDispelEnabled, "기본 모드 — 관측 소멸 가능");
        Ok(!GameModes.GhostWalksToControlRoom, "기본 모드 — 복도로 걸어오지 않는다");
        Ok(!GameModes.CorridorThreatsEnabled, "기본 모드 — 복도 위협 자체가 없다");

        GameModes.ForceSelect(GameMode.Competition);
        Ok(!GameModes.GhostDispelEnabled, "대회용 — **관측해도 소멸하지 않는다**");
        Ok(GameModes.GhostWalksToControlRoom, "대회용 — 중앙제어실로 걸어온다");
        Ok(GameModes.CorridorThreatsEnabled, "대회용 — 복도 위협이 돈다");
    }

    // ── B. 기본 모드 — 기존 동작 보존 ───────────────────────────────

    private void SectionB()
    {
        Head("B", "기본 모드는 예전 그대로다");
        GameModes.ForceSelect(GameMode.Standard);
        var g = StartShift();

        string room = StaffedRoom();
        Ok(g.ForceAppear(room, _sim), $"{_sim.RoomDisplayName(room)} 에 개체를 띄웠다");

        // 계속 지켜본다 → 소멸해야 한다.
        _sim.SetSurveillanceTarget(room);
        int dispelled = 0;
        g.Dispelled += _ => dispelled++;
        Ok(g.DispelRatio >= 0f, "관측 게이지가 존재한다");
        Advance(g, 12f, watchRoom: room);
        Ok(dispelled == 1, "지켜보면 소멸한다");
        Ok(!g.Active, "소멸 뒤에는 방에 남지 않는다");
        Ok(_sim.Threats.Current == null, "**기본 모드에서는 복도로 나가지 않는다**");
    }

    // ── C. 대회용 — 소멸하지 않는다 ─────────────────────────────────

    private void SectionC()
    {
        Head("C", "대회용은 보고 있어도 사라지지 않는다");
        GameModes.ForceSelect(GameMode.Competition);
        var g = StartShift();

        string room = StaffedRoom();
        g.ForceAppear(room, _sim);
        _sim.SetSurveillanceTarget(room);

        int dispelled = 0;
        g.Dispelled += _ => dispelled++;
        // 소멸 시간(기본 5초)의 세 배를 본다. 체류 시간(20초)보다는 짧게 —
        // 그보다 오래 보면 "소멸" 이 아니라 "체류가 끝나 떠난 것" 과 섞인다.
        Advance(g, 16f, watchRoom: room);
        Ok(dispelled == 0, "**16초를 지켜봐도 소멸하지 않는다**");
        Ok(Mathf.IsZeroApprox(g.DispelRatio), "관측 게이지가 0 으로 잠겨 있다");
        Ok(g.Active, "개체는 아직 그 방에 있다");
    }

    // ── D. 작업실 → 복도 ────────────────────────────────────────────

    private void SectionD()
    {
        Head("D", "같은 개체가 복도로 걸어 나간다");
        GameModes.ForceSelect(GameMode.Competition);
        var g = StartShift();

        // 발전실은 서측 접근 복도 바로 바깥이다 — 거기서 나오면 서측으로 들어와야 한다.
        const string origin = "power_room";
        Ok(_sim.Threats.ApproachFrom(origin) == "corridor_west",
            "발전실에서 나오면 **서측** 접근 복도로 들어온다");
        Ok(_sim.Threats.ApproachFrom("core_room") == "corridor_north",
            "코어실에서 나오면 북측으로 들어온다");
        Ok(_sim.Threats.ApproachFrom("storage_room") == "corridor_east",
            "저장고에서 나오면 동측으로 들어온다");

        g.ForceAppear(origin, _sim);
        _sim.SetSurveillanceTarget(origin);
        int departed = 0;
        g.Departed += _ => departed++;
        // 복도로 나오는 **그 순간**의 단계를 잡아 둔다. 나중에 보면 이미 걸어 들어온 뒤라
        // "처음부터 문 앞에 서 있었는지" 를 가릴 수 없다.
        var spawnPhase = ThreatPhase.Finished;
        bool spawnInCorridor = true;
        _sim.Threats.Spawned += t0 => { spawnPhase = t0.Phase; spawnInCorridor = t0.InCorridor; };

        // 체류 시간이 끝날 때까지 — 기본 34초.
        Advance(g, 40f, watchRoom: origin);
        Ok(departed == 1, "체류가 끝나면 작업실을 떠난다");
        Ok(!g.Active, "**떠난 뒤 작업실 CCTV 에 남지 않는다**");

        var t = _sim.Threats.Current;
        Ok(t != null, "복도 위협으로 이어졌다");
        if (t == null) return;
        Ok(t.OriginRoomId == origin, $"어느 방에서 나왔는지 기억한다({t.OriginRoomId})");
        Ok(t.SegmentId == "corridor_west", $"서측 복도로 들어온다({t.SegmentId})");
        Ok(spawnPhase == ThreatPhase.Outer, "나온 직후에는 복도 밖이다 — 바로 문 앞에 서지 않는다");
        Ok(!spawnInCorridor, "그 단계에서는 복도 CCTV 에도 잡히지 않는다");

        // 실제로 복도에 들어서는지.
        for (int i = 0; i < 60 * 30 && t.Phase == ThreatPhase.Outer; i++) _sim.Threats.Tick(Step);
        Ok(t.Phase == ThreatPhase.Approach, "접근 복도에 들어섰다");
        Ok(t.InCorridor, "이제부터 복도 CCTV 로 보인다");
    }

    // ── E. 사고 · 기록 · 교육 보호 ──────────────────────────────────

    private void SectionE()
    {
        Head("E", "사고는 가끔, 목격 기록은 언제나");
        GameModes.ForceSelect(GameMode.Competition);

        int runs = 16, struck = 0, sighting = 0, staffed = 0;
        for (int r = 0; r < runs; r++)
        {
            var g = StartShift();
            string room = StaffedRoom();
            if (_sim.OnDutyCount(room) > 0) staffed++;
            int before = EventLog.Instance?.GetAllEntries().Count ?? 0;
            g.ForceAppear(room, _sim);
            int hit = 0;
            g.Struck += _ => hit++;
            Advance(g, 40f);
            struck += hit;
            var added = (EventLog.Instance?.GetAllEntries() ?? new List<LogEntry>()).Skip(before);
            if (added.Any(e => e.EventType is LogEventType.AnomalySighting or LogEventType.AnomalyIncident))
                sighting++;
        }
        GD.Print($"      {runs}회 중 사고 {struck}회 · 기록 {sighting}회 · 사람 있던 방 {staffed}회");
        Ok(struck > 0, "사고가 아예 안 나지는 않는다");
        Ok(struck < runs, "**매번 사고가 나지는 않는다**");
        Ok(sighting == runs, "사고가 없어도 목격 기록은 항상 남는다");

        // 교육일 보호 — 넘어오지 못하게 잠글 수 있는가.
        var th = _sim.Threats;
        th.Reset();
        th.NoBreach = true;
        var t = th.ForceSpawn("absentee", "corridor_west");
        Ok(t != null, "교육용 개체를 띄웠다");
        if (t != null)
        {
            t.Phase = ThreatPhase.AtDoor;
            t.PhaseSeconds = 0f;
            _sim.Corridors.Unseal("corridor_west");
            for (int i = 0; i < 60 * 30; i++) th.Tick(Step);
            Ok(t.Phase != ThreatPhase.Breach, "**문이 열려 있어도 교육 중에는 넘어오지 않는다**");
        }
        th.NoBreach = false;
        th.Reset();
        _sim.Corridors.ResetAll();
        GameModes.ForceSelect(GameMode.Standard);
    }

    // ── F. 프롤로그 선택지 · 교육 대본 ──────────────────────────────

    private void SectionF()
    {
        Head("F", "프롤로그와 DAY 0 대본");
        var baseMenu = NSP.Prologue.PrologueScript.GetMenu("g_main");
        var compMenu = NSP.Prologue.PrologueScript.GetMenu("g_main_comp");

        Ok(baseMenu != null && baseMenu.Options.Count == 3, "기본 모드 질문은 세 개 그대로다");
        Ok(compMenu != null && compMenu.Options.Count == 4, "대회용 질문은 네 개다");
        if (baseMenu == null || compMenu == null) return;

        Ok(compMenu.RequireAll, "대회용도 네 질문을 전부 확인해야 넘어간다");
        // 앞 세 개는 **같은 답변 블록**을 가리켜야 한다 — 복제하면 한쪽만 고쳐질 수 있다.
        var shared = baseMenu.Options.Select(o => o.GuideId).ToList();
        Ok(shared.All(id => compMenu.Options.Any(o => o.GuideId == id)),
            "앞 세 질문은 같은 답변 블록을 공유한다(중복 대사 없음)");

        var extra = compMenu.Options.Select(o => o.GuideId).Except(shared).ToList();
        Ok(extra.Count == 1 && extra[0] == "g_why_me", "추가되는 질문은 하나뿐이다");
        var why = NSP.Prologue.PrologueScript.GetGuide("g_why_me");
        Ok(why != null && why.Beats.Count > 0, "새 답변 대사가 실제로 있다");
        Ok(compMenu.Options.Any(o => o.Label.Contains("왜 나를 노리지")), "질문 문구가 지시서와 같다");

        // 결번 추리의 답(특정 직원)을 흘리지 않아야 한다.
        string body = why == null ? "" : string.Join(" ", why.Beats.Select(b => b.Value));
        var names = FacilitySimulation.Instance.GetEmployeeIds()
            .Select(id => FacilitySimulation.Instance.GetEmployeeDef(id)?.Codename ?? "")
            .Where(n => n.Length > 0).ToList();
        Ok(!names.Any(n => body.Contains(n)), "**답변이 특정 직원을 지목하지 않는다**");

        // 모드별 제한 시간 — 기본 5일 120시간 / 대회용 3일 72시간.
        var mission = NSP.Prologue.PrologueScript.GetGuide("g_mission");
        string missionBody = mission == null ? "" : string.Join(" ", mission.Beats.Select(b => b.Value));
        Ok(missionBody.Contains("{HOURS}"), "제한 시간은 모드에 따라 치환된다");
        Ok(!missionBody.Contains("120시간"), "120시간이 하드코딩되어 있지 않다");

        // DAY 0 차폐 교육 대본이 전부 있는가.
        string[] blocks =
        {
            "tut_barrier_intro", "tut_barrier_found", "tut_barrier_corridor", "tut_barrier_close",
            "tut_barrier_wrong", "tut_barrier_power", "tut_barrier_hold", "tut_barrier_retreat",
            "tut_barrier_done",
        };
        var missing = blocks.Where(b => NSP.Prologue.PrologueScript.GetGuide(b) == null).ToList();
        Ok(missing.Count == 0, $"차폐 교육 대사 {blocks.Length}개가 모두 있다{(missing.Count > 0 ? " — 없음: " + string.Join(", ", missing) : "")}");
        // 기본 모드의 관측 교육도 그대로 남아 있어야 한다.
        Ok(NSP.Prologue.PrologueScript.GetGuide("tut_anomaly_watch") != null,
            "기본 모드의 관측 소멸 교육 대사가 남아 있다");
    }

    // ── 보조 ────────────────────────────────────────────────────────

    private GhostHauntSystem StartShift()
    {
        GameState.Instance.SetPhase(GamePhase.Live);
        // 근무 시계를 0 으로 돌린다. 안 그러면 검사가 쌓일수록 "근무가 거의 끝나서
        // 위협을 보내지 않는다" 는 정상 동작에 걸려 뒤쪽 항목이 통째로 막힌다.
        GameState.Instance.ResetDayClock();
        // 검사를 여러 번 이어 돌리면 앞 회차의 기절 · 사고 · 격리가 쌓여 사람이 자리에
        // 서지 못한다. 매번 멀쩡한 근무로 되돌린 뒤 시작한다.
        _sim.ResetRun();
        _sim.Threats.Reset();
        _sim.Corridors.ResetAll();
        _sim.Ghost.Reset();
        var ids = _sim.GetEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "storage_room", "maintenance_room", "guard_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) _sim.AssignToRoom(ids[i], rooms[i]);

        // 배치만으로는 아무도 방에 없다 — 걸어가는 데 시간이 걸린다. 사람이 자리에
        // 설 때까지 돌리고, 그 사이 저절로 나온 개체는 지운다(검사는 부른 개체만 본다).
        for (int i = 0; i < Mathf.CeilToInt(45f / Step); i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            if (i % 30 == 0 && StaffedRoomCount() >= 3) break;
        }
        _sim.Ghost.Reset();
        _sim.Threats.Reset();
        return _sim.Ghost;
    }

    // 사람이 서 있는 작업실의 수(배치 완료 판정용).
    private int StaffedRoomCount() =>
        new[] { "power_room", "core_room", "storage_room", "maintenance_room", "guard_room" }
            .Count(r => _sim.OnDutyCount(r) > 0);

    // 지금 **실제로 사람이 서 있는** 작업실. 배치표가 아니라 현재 위치를 본다 —
    // 배치만 보고 고르면 아직 걸어가는 중인 방을 집어 "목격자 0명" 이 된다.
    private string StaffedRoom()
    {
        foreach (string id in _sim.GetEmployeeIds())
        {
            var st = _sim.GetEmployeeState(id);
            string room = st?.CurrentRoomId ?? "";
            if (st is not { Alive: true, Isolated: false } || string.IsNullOrEmpty(room)) continue;
            if (room == FacilitySimulation.DeployOriginRoomId) continue;
            if (_sim.OnDutyCount(room) > 0) return room;
        }
        return "power_room";
    }

    // 개체 쪽 시간만 흘린다. watchRoom 을 주면 그 방을 계속 보고 있는 상태로 센다.
    private void Advance(GhostHauntSystem g, float seconds, string watchRoom = "")
    {
        int steps = Mathf.CeilToInt(seconds / Step);
        for (int i = 0; i < steps; i++)
        {
            if (!string.IsNullOrEmpty(watchRoom)) _sim.SetSurveillanceTarget(watchRoom);
            GameState.Instance.AdvanceDayTime(Step);
            // 시뮬레이션 전체를 돌린다 — 개체도 복도도 **직원 이동도** 그 안에서 돈다.
            // 개체만 따로 돌리면 직원이 방에 도착하지 못해 "목격자 0명" 이 되어 버린다.
            _sim.Tick(Step);
        }
    }

    private static void Head(string tag, string what) => GD.Print($"\n── {tag}. {what}");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
