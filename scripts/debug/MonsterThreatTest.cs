using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 괴물 접근 · 차폐 판정 자동 검증 (PHASE B).
//
//   godot --headless --path . res://scenes/debug/MonsterThreatTest.tscn --quit-after 9000
//
// 보는 것은 **공정한 위협인가** 다.
//   ① 정해진 출현 구역에서 시작하는가 (작업실에서 갑자기 생기지 않는가)
//   ② 외곽 → 접근 복도 → 문 앞 순서를 하나도 건너뛰지 않는가
//   ③ CCTV 감시 구간을 반드시 지나는가 (한 번도 볼 기회가 없는 죽음이 없는가)
//   ④ 문이 닫혀 있으면 통과하지 못하는가 — 두드리다 물러나는가
//   ⑤ 문이 열려 있을 때만 침입하는가 — 그것도 유예를 준 뒤인가
//   ⑥ 영구 봉쇄된 남측으로는 절대 오지 않는가
//   ⑦ 근무가 끝나거나 모드가 다르면 아예 돌지 않는가
public partial class MonsterThreatTest : Node
{
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;
    private MonsterThreatSystem _th;
    private CorridorNet _net;
    private int _pass, _fail;

    // 들어온 신호를 순서대로 적어 둔다 — 단계를 건너뛰면 여기서 드러난다.
    private readonly List<string> _log = new();

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        _th = _sim.Threats;
        _net = _sim.Corridors;
        _th.Spawned += t => _log.Add("spawn:" + t.Def.MonsterId);
        _th.EnteredCorridor += t => _log.Add("corridor:" + t.SegmentId);
        _th.ReachedDoor += _ => _log.Add("door");
        _th.Pounded += _ => _log.Add("pound");
        _th.Retreated += _ => _log.Add("retreat");
        _th.Breached += _ => _log.Add("breach");
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 괴물 접근 · 차폐 판정 검증 ################");
        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 데이터 ───────────────────────────────────────────────────

    private void SectionA()
    {
        Head("A", "괴물 3종 · 출현 구역 3곳");
        var ms = MonsterThreatSystem.Monsters;
        var zs = MonsterThreatSystem.Zones;
        Ok(ms.Count == 3, $"괴물 {ms.Count}종");
        Ok(zs.Count == 3, $"출현 구역 {zs.Count}곳");

        foreach (var m in ms)
            GD.Print($"      {m.MonsterId,-9} {m.DisplayName,-12} 키 {m.TargetHeight:0.00}m  " +
                     $"접근 {m.ApproachSeconds:0}초  유예 {m.BreachDelaySeconds:0.0}초  DAY{m.FirstDay}~");
        foreach (var z in zs)
            GD.Print($"      {z.ZoneId,-16} {z.CodeName,-11} → {string.Join(", ", z.ApproachSegmentIds)}");

        Ok(ms.All(m => ResourceLoader.Exists(m.ModelPath)), "세 모델 파일이 전부 있다");
        Ok(ms.All(m => m.BreachDelaySeconds > 0f), "**유예 시간이 0인 괴물은 없다** (경고 없는 즉사 금지)");
        Ok(ms.All(m => m.ApproachSeconds >= 8f), "접근에 최소 8초는 걸린다 (문을 내릴 시간)");

        // 출현 구역이 가리키는 접근 복도는 전부 실재하고 차폐 가능해야 한다.
        foreach (var z in zs)
            foreach (string id in z.ApproachSegmentIds)
                Ok(_net.ById(id) is { IsBlockable: true }, $"{z.ZoneId} → {id} 는 차폐 가능한 접근 복도다");

        // 남측 영구 봉쇄는 어떤 구역에서도 쓰지 않는다.
        Ok(zs.SelectMany(z => z.ApproachSegmentIds).All(id => id != "corridor_south_seal"),
            "영구 봉쇄된 남측으로 오는 경로는 없다");

        // 작업실 데이터에 섞여 들어가지 않았는가 — 출현 구역은 RoomDef 가 아니다.
        Ok(zs.All(z => _sim.GetRoomDef(z.ZoneId) == null),
            "출현 구역은 작업실로 등록되지 않았다 (배치표 · 길찾기에 없다)");

        // 애니메이션 이름은 FBX 에서 읽은 것 그대로여야 한다 — 없는 이름을 부르면 안 된다.
        Head("A", "애니메이션 매핑이 실제 클립과 맞는가");
        foreach (var m in ms) CheckClips(m);
    }

    private void CheckClips(MonsterDef m)
    {
        var ps = GD.Load<PackedScene>(m.ModelPath);
        var inst = ps?.Instantiate<Node3D>();
        if (inst == null) { Ok(false, $"{m.MonsterId} 모델을 열 수 없다"); return; }
        AddChild(inst);
        var ap = FindAnim(inst);
        var have = ap?.GetAnimationList() ?? System.Array.Empty<string>();

        var want = new[] { m.AnimIdle, m.AnimWalk, m.AnimRun, m.AnimAttackDoor, m.AnimJumpscare, m.AnimRetreat }
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var missing = want.Where(w => !have.Contains(w)).ToList();
        foreach (string x in missing) GD.Print($"      없는 클립: {m.MonsterId} → \"{x}\"");
        Ok(missing.Count == 0, $"{m.MonsterId}: 적어 둔 클립 {want.Count}개가 전부 실제로 있다 (보유 {have.Length}개)");

        // 측정값 — 모델을 바꿔도 키가 맞는지 확인할 수 있게 남긴다.
        var box = Bounds(inst, inst);
        GD.Print($"      {m.MonsterId} 원본 크기 {box.Size}  → 목표 {m.TargetHeight:0.00}m " +
                 $"(배율 {(box.Size.Y > 0 ? m.TargetHeight / box.Size.Y : 0f):0.000})");
        inst.QueueFree();
    }

    private static AnimationPlayer FindAnim(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren()) if (FindAnim(c) is { } f) return f;
        return null;
    }

    private static Aabb Bounds(Node3D root, Node n)
    {
        var box = new Aabb();
        bool got = false;
        void Walk(Node x)
        {
            if (x is MeshInstance3D mi && mi.Visible && mi.Mesh is { } mesh && mesh.GetSurfaceCount() > 0
                && !mi.Name.ToString().StartsWith("WGT_"))
            {
                var a = (root.GlobalTransform.AffineInverse() * mi.GlobalTransform) * mi.GetAabb();
                box = got ? box.Merge(a) : a;
                got = true;
            }
            foreach (var c in x.GetChildren()) Walk(c);
        }
        Walk(n);
        return box;
    }

    // ── B. 차폐 성공 ────────────────────────────────────────────────

    private void SectionB()
    {
        Head("B", "문이 닫혀 있으면 통과하지 못한다");
        StartShift(3);
        var t = _th.ForceSpawn("absentee", "corridor_north");
        Ok(t != null, "결번자 형태를 북측 복도로 띄웠다");
        Ok(t.Phase == ThreatPhase.Outer, "출현 구역(외곽)에서 시작한다 — 복도에 바로 생기지 않는다");
        Ok(t.Progress == 0f, "복도 진행도 0");

        // 외곽 구간 — 아직 CCTV 에 없다.
        Tick(t.Zone.OuterTravelSeconds * 0.5f);
        Ok(t.Phase == ThreatPhase.Outer && !t.InCorridor, "외곽 구간에서는 카메라에 잡히지 않는다");

        RunUntil(() => t.Phase == ThreatPhase.Approach, 30f);
        Ok(t.Phase == ThreatPhase.Approach && t.InCorridor, "접근 복도에 들어섰다 — 여기부터 CCTV");

        // 가까이 왔을 때 문을 내린다 — 플레이어가 실제로 하는 타이밍이다.
        // (너무 일찍 내리면 유지 상한이 먼저 끝나 문이 저절로 열린다. 그게 이 설계의 값이다.)
        RunUntil(() => t.Progress > 0.45f, 40f);
        float mid = t.Progress;
        Ok(mid is > 0.4f and < 0.95f, $"중간 지점에서 아직 오는 중 ({mid * 100f:0}%)");
        RunUntil(() => t.Progress > 0.82f, 40f);
        var seg = _net.ById("corridor_north");
        _net.Select(seg.Id);
        _net.Seal(seg.Id, out _);
        Tick(seg.DriveSeconds + 0.1f);
        Ok(seg.Sealed, "북측 차폐");

        RunUntil(() => t.Phase == ThreatPhase.AtDoor || t.Phase == ThreatPhase.Pounding, 60f);
        Ok(t.Phase is ThreatPhase.AtDoor or ThreatPhase.Pounding, "문 앞 도착");
        RunUntil(() => t.Phase == ThreatPhase.Pounding, 10f);
        Ok(t.Phase == ThreatPhase.Pounding, "닫힌 문을 두드린다");
        Ok(Mathf.IsEqualApprox(t.Progress, 1f), "문 앞에서 멈춰 섰다 (문을 뚫지 않는다)");

        int pounds = _log.Count(x => x == "pound");
        Tick(3f);
        Ok(_log.Count(x => x == "pound") > pounds, "두드리는 소리가 반복된다");

        RunUntil(() => t.Phase is ThreatPhase.Retreat or ThreatPhase.Finished, 30f);
        Ok(_log.Contains("retreat"), "일정 시간 뒤 물러난다 (근무 끝까지 긁지 않는다)");
        RunUntil(() => !t.Active, 20f);
        Ok(!t.Active, "사라졌다 — 관리자는 살아 있다");
        Ok(!_log.Contains("breach"), "침입은 일어나지 않았다");

        GD.Print($"      신호 순서: {string.Join(" → ", _log.Distinct())}");
        Ok(_log.IndexOf("corridor:corridor_north") > _log.IndexOf("spawn:absentee")
           && _log.IndexOf("door") > _log.IndexOf("corridor:corridor_north"),
            "출현 → 복도 → 문 순서를 지켰다 (어느 단계도 건너뛰지 않았다)");
    }

    // ── C. 차폐 실패 ────────────────────────────────────────────────

    private void SectionC()
    {
        Head("C", "문이 열려 있을 때만, 그것도 유예를 준 뒤에 침입한다");
        StartShift(3);
        var t = _th.ForceSpawn("absentee", "corridor_west");
        RunUntil(() => t.Phase == ThreatPhase.Approach, 30f);
        Ok(t.InCorridor, "서측 복도로 접근 중 — CCTV 로 볼 수 있는 단계를 지났다");

        // 문을 내리지 않고 그대로 둔다.
        RunUntil(() => t.Phase == ThreatPhase.AtDoor, 60f);
        Ok(t.Phase == ThreatPhase.AtDoor, "문 앞 도착 (문은 열려 있다)");
        Ok(!_log.Contains("breach"), "도착하자마자 죽이지 않는다");

        // 유예 동안은 아직 침입하지 않는다.
        Tick(t.Def.BreachDelaySeconds * 0.5f);
        Ok(t.Phase == ThreatPhase.AtDoor && !_log.Contains("breach"),
            $"유예 {t.Def.BreachDelaySeconds:0.0}초의 절반이 지나도 아직이다");

        // 그 유예 안에 문을 내리면 살아난다.
        var seg = _net.ById("corridor_west");
        _net.Select(seg.Id);
        _net.Seal(seg.Id, out _);
        Tick(seg.DriveSeconds + 0.2f);
        Ok(t.Phase == ThreatPhase.Pounding && !_log.Contains("breach"),
            "**마지막 순간에 문을 내리면 막힌다** — 두드리기로 바뀐다");

        // 다시 열면 유예가 처음부터 다시 흐른다(열자마자 즉사 금지).
        _net.Unseal(seg.Id);
        Tick(seg.DriveSeconds + 0.2f);
        Ok(t.Phase == ThreatPhase.AtDoor && !_log.Contains("breach"), "문을 열면 다시 문 앞 판정으로");
        Tick(t.Def.BreachDelaySeconds * 0.5f);
        Ok(!_log.Contains("breach"), "열자마자 죽지 않는다 — 유예가 다시 흐른다");

        RunUntil(() => _log.Contains("breach"), 20f);
        Ok(_log.Contains("breach"), "끝내 열어 두면 침입한다");
        Ok(t.Phase == ThreatPhase.Breach, "침입 단계에 들어갔다");
    }

    // ── D. 경로 규칙 ────────────────────────────────────────────────

    private void SectionD()
    {
        Head("D", "정해진 길로만 온다");
        StartShift(3);

        // 각 괴물이 자기 구역이 가리키는 복도로만 들어오는가.
        var used = new Dictionary<string, HashSet<string>>();
        for (int i = 0; i < 24; i++)
        {
            StartShift(3);
            foreach (string id in new[] { "absentee", "infant", "spider" })
            {
                var t = _th.ForceSpawn(id);
                if (t == null) continue;
                if (!used.TryGetValue(id, out var set)) used[id] = set = new HashSet<string>();
                set.Add(t.SegmentId);
                // 다음 괴물을 띄우려면 지금 것을 치운다.
                _th.Reset();
            }
        }
        foreach (var (id, set) in used)
        {
            var zone = MonsterThreatSystem.Zones.First(z =>
                z.ZoneId == (id == "infant" ? "zone_vent" : id == "spider" ? "zone_cable" : "zone_evacuation"));
            GD.Print($"      {id,-9} 사용한 복도: {string.Join(", ", set.OrderBy(x => x))}");
            Ok(set.All(s => zone.ApproachSegmentIds.Contains(s)),
                $"{id} 는 자기 구역이 가리키는 복도로만 들어온다");
        }
        Ok(used["absentee"].Count > 1, "복도가 둘 이상인 구역은 번갈아 쓴다 (같은 길만 반복하지 않는다)");

        // 동시에 하나만.
        StartShift(3);
        _th.ForceSpawn("absentee");
        int before = _th.Active.Count;
        _th.ForceSpawn("infant");
        Ok(_th.Active.Count(x => x.Active) <= MonsterThreatSystem.MaxActive,
            $"동시에 도는 위협은 {MonsterThreatSystem.MaxActive}개를 넘지 않는다 (지금 {_th.Active.Count})");
    }

    // ── E. 꺼져 있어야 할 때 꺼져 있는가 ────────────────────────────

    private void SectionE()
    {
        Head("E", "돌면 안 되는 상황");
        StartShift(3);
        _th.ForceSpawn("absentee");
        Ok(_th.Current != null, "근무 중에는 돈다");

        GameState.Instance.SetPhase(GamePhase.Rest);
        Tick(1f);
        Ok(_th.Current == null, "휴게시간에 들어가면 남아 있던 괴물이 치워진다");

        GameState.Instance.SetPhase(GamePhase.Live);
        _th.ForceSpawn("absentee");
        _sim.ResetForNewShift();
        Ok(_th.Current == null, "새 근무를 시작하면 전날 괴물이 남지 않는다");

        // 기본 모드에서는 아예 돌지 않는다.
        Head("E", "기본 5일 모드 회귀");
        StartShift(3, GameMode.Standard);
        Ok(!GameModes.CorridorThreatsEnabled, "기본 모드는 복도 위협이 꺼져 있다");
        Tick(120f);
        Ok(_th.Current == null && _th.SpawnedToday == 0,
            $"기본 모드로 120초를 돌려도 한 마리도 나오지 않는다 (등장 {_th.SpawnedToday})");
        Ok(_net.Segments.Where(s => !s.PermanentSeal).All(s => s.State == BarrierState.Open),
            "문도 저절로 움직이지 않는다");

        GameModes.ForceSelect(GameMode.Competition);
        Head("E", "대회용 하루 일정");
        for (int day = 1; day <= 3; day++)
        {
            StartShift(day);
            // 하루치를 통째로 **막아 가며** 돌린다 — 플레이어가 제때 문을 내리는 판.
            // (막지 않으면 첫 침입에서 판이 끝나므로 하루 일정을 볼 수 없다.)
            DefendWholeShift(DayObjectives.MaxShiftSeconds);
            GD.Print($"      DAY{day} 등장 {_th.SpawnedToday}회 · 침입 {(_th.Breaching ? "있음" : "없음")}");
            Ok(!_th.Breaching, $"DAY{day} — 제때 문을 내리면 한 번도 뚫리지 않는다");
            Ok(_th.SpawnedToday >= 1, $"DAY{day} 에 최소 한 번은 온다");
            Ok(_th.SpawnedToday <= 3, $"DAY{day} 에 과하게 몰리지 않는다");
        }
    }

    // ── 도구 ────────────────────────────────────────────────────────

    private void StartShift(int day, GameMode mode = GameMode.Competition)
    {
        _log.Clear();
        GameModes.ForceSelect(mode);
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        RepairApprovalSystem.ResetAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < roster.Count && i < rooms.Length; i++) _sim.AssignToRoom(roster[i], rooms[i]);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        var gs = GameState.Instance;
        if (!gs.IsConsumerPowered(PowerConsumer.Barrier)) gs.TryTogglePower(PowerConsumer.Barrier);
        foreach (var s in _net.Segments) s.CooldownLeft = 0f;
        _th.Reset();
    }

    // 괴물이 가까워지면 그쪽 문을 내리고, 물러나면 다시 연다 — 사람이 하는 그대로.
    // "제때 내리면 살아남는가" 를 한 판 길이로 확인하는 데 쓴다.
    private void DefendWholeShift(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            var th = _th.Current;
            if (th == null)
            {
                // 아무도 없으면 문을 열어 둔다(냉각이 흐르게).
                foreach (var s in _net.Segments.Where(x => x.Sealed && !x.PermanentSeal).ToList())
                    _net.Unseal(s.Id);
                continue;
            }
            var seg = _net.ById(th.SegmentId);
            if (seg == null) continue;

            bool wantShut = th.Phase is ThreatPhase.AtDoor or ThreatPhase.Pounding
                            || (th.Phase == ThreatPhase.Approach && th.Progress > 0.80f);
            if (wantShut && !seg.Sealed && _net.CanSeal(seg, out _))
            {
                _net.Select(seg.Id);
                _net.Seal(seg.Id, out _);
            }
            else if (!wantShut && seg.Sealed && th.Phase is ThreatPhase.Retreat or ThreatPhase.Finished)
                _net.Unseal(seg.Id);
        }
    }

    private void Tick(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private float RunUntil(System.Func<bool> done, float limit)
    {
        float t = 0f;
        while (t < limit && !done())
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            t += Step;
        }
        return t;
    }

    private void Head(string tag, string title) => GD.Print($"\n===== [{tag}] {title} =====");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
