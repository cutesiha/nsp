using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 괴물 반응 캡처 + 검사. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/GhostReactionShot.tscn -- <저장 폴더> [방] [직원,직원,...] [이름]
//     예) -- out maintenance_room sheep,rabbit,cat,dog,wolf,fox all6
//         -- out guard_room sheep,wolf,fox trio
//         -- out core_room fox solo_fox          (한 명만 — 개별 테스트)
//
// 실제 MainScene3D_Test 로 DAY1 근무를 띄우고, 고른 직원을 한 방에 모은 뒤 그 방에 괴물을
// ForceAppear 로 부른다(실제 등장 경로와 같다). 그 방 CCTV 를 보며 시점마다 사진을 찍고,
//   ① 다른 방으로 돌렸다가 돌아와 첫 반응이 다시 나오지 않는지
//   ② 끝까지 지켜봐 소멸시킨 뒤 원래 자리로 돌아가는지
//   ③ 그 사이 CurrentRoomId · AssignedRoomId · IsMoving · 배치 로그가 그대로인지
//   ④ 직원끼리 · 괴물과 겹치지 않는지
// 를 로그로 남긴다. (방 정원 2명 제한은 배치 화면 규칙이다 — 검사용으로 AssignToRoom 으로 직접 넣는다.)
public partial class GhostReactionShot : Node
{
    private string _dir, _room, _tag;
    private List<string> _ids;
    private readonly List<(string Label, Image Img)> _shots = new();
    private readonly StringBuilder _report = new();
    private int _fails;
    private bool _moveTest;
    private bool _fromHub;   // DeveloperHub 에서 열었으면 끝나도 창을 닫지 않는다(계속 지켜볼 수 있게)
    private bool _seatTest;
    private string _mode = "";
    private bool _again;   // 회복 도중 괴물이 다시 나타나는 경우   // "" 괴물 · "work" 6인 작업/착석/퇴장 · "iso" 격리/해제

    public override void _Ready()
    {
        // 게임 컨트롤러보다 뒤에 돈다 — 아래 _Process 가 CCTV 뷰포트 갱신 설정을 마지막에 정하게.
        ProcessPriority = 1000;
        _ = Run();
    }

    // 캡처용: CCTV 가 꺼진 날(DAY2 전력 배분 등)에도 3D 화면은 계속 그린다. 게임 판정(관측)과는 무관하다.
    private SubViewport _keepDrawing;
    public override void _Process(double delta)
    {
        if (_keepDrawing != null && IsInstanceValid(_keepDrawing))
            _keepDrawing.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _fromHub = args.Length == 0;
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _room = args.Length > 1 ? args[1] : "maintenance_room";
        _ids = (args.Length > 2 ? args[2] : "sheep,rabbit,cat,dog,wolf,fox").Split(',').ToList();
        _tag = args.Length > 3 ? args[3] : "ghost";
        _moveTest = args.Length > 4 && args[4].Contains("move");
        _seatTest = args.Length > 4 && args[4].Contains("seat");
        _again = args.Length > 4 && args[4].Contains("again");
        _mode = args.Length > 4 && args[4].Contains("work") ? "work" : args.Length > 4 && args[4].Contains("iso") ? "iso" : "";
        System.IO.File.WriteAllText($"{_dir}/{_tag}_progress.txt", "");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        Log($"phase={GameState.Instance?.CurrentPhase}");
        await Frames(60);

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        if (_seatTest)
        {
            // DeveloperHub 와 같은 실제 진행 경로로 DAY2 배치 화면까지 올린다.
            DebugEntryPoint.CallStatic("StartNewRun", 1);
            GameState.Instance?.GoToNextDay();
            DebugEntryPoint.Call(flow, "ClearAllAssignments");
            DebugEntryPoint.SetStage(flow, "DayTransition");
            DebugEntryPoint.Call(flow, "EnterSchedule");
            await Frames(10);
            Log($"DAY{GameState.Instance?.CurrentDay} phase={GameState.Instance?.CurrentPhase}");
        }
        if (sim == null || ctl == null) { Fail("씬이 뜨지 않았다"); Finish(); return; }

        foreach (string id in _ids)
            if (!sim.AssignToRoom(id, _room)) Fail($"배치 실패 {id}");
        await Frames(5);

        // 근무 시작 — DeveloperHub 와 같은 실제 진행 메서드를 부른다(배치는 위에서 끝냈다).
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        Log($"phase={GameState.Instance?.CurrentPhase}");

        // 작업 검사는 들어오는 모습부터 본다.
        if (_mode == "work") { sim.SetSurveillanceTarget(_room); await RunWork(sim, ctl); Finish(); return; }

        // 모두 그 방에 도착할 때까지.
        for (int i = 0; i < 60 * 60; i++)
        {
            if (_ids.All(id => sim.GetEmployeeState(id) is { IsMoving: false } st && st.CurrentRoomId == _room)) break;
            await Frame();
        }
        // 도중에 저절로 나온 괴물이 있으면 끝날 때까지 기다린다.
        for (int i = 0; i < 60 * 60 && sim.Ghost.Active; i++) await Frame();
        Log("도착: " + string.Join(" ", _ids.Select(id => $"{id}@{sim.GetEmployeeState(id).CurrentRoomId}")));
        sim.SetSurveillanceTarget(_room);
        await Seconds(4.5);

        var world = FacilityCctvWorld.Instance;
        var cctv = ctl.FacilityCctvViewport;
        if (world == null || cctv == null) { Fail("CCTV 월드 없음"); Finish(); return; }
        // 판독용으로만 크게 찍는다(게임 화면 비율 4:3 그대로).
        cctv.Size = new Vector2I(960, 720);
        _keepDrawing = cctv;

        if (_mode == "iso") { await RunIsolation(sim, world, cctv); Finish(); return; }

        // 앉아서 일하는 중에 괴물이 나오는 경우 — DAY2 정비실 설비 수리(조립 책상, 36초경 발생)를
        // 실제로 기다린다. 누군가 sit_* 클립을 틀면 시작한다.
        if (_seatTest)
        {
            for (int i = 0; i < 60 * 90 && !_ids.Any(id => ClipOf(world, id).StartsWith("sit_")); i++) await Frame();
            await Seconds(1.0);
            Log("앉은 자리: " + Clips(world));
        }

        var before = Snapshot(sim);
        int relocBefore = CountRelocationLogs();
        var workPos = Positions(world);
        var workClip = _ids.ToDictionary(id => id, id => ClipOf(world, id));
        Shot(cctv, "업무 중(괴물 없음)");
        Log("업무 중 클립: " + Clips(world));

        // ── 등장 ──
        for (int i = 0; i < 60 * 60 && sim.Ghost.Active; i++) await Frame();
        GD.Seed(20260926);
        // 여우의 "가끔 쳐다봄"을 몇 번 보려면 5초 소멸이 너무 짧다 — 이 검사 씬에서만 잠시 늘린다.
        var cfg = Config.Instance.Data;
        float dispelSeconds = cfg.GhostDispelSeconds;
        if (_ids.Contains("fox")) cfg.GhostDispelSeconds = 60f;
        if (!sim.Ghost.ForceAppear(_room, sim, 90f)) Fail("괴물 등장 실패");
        double t0 = Now();
        foreach (double at in new[] { 0.08, 0.2, 0.4, 0.7, 1.1, 1.7, 2.6, 3.6 })
        {
            await Until(t0 + at);
            Shot(cctv, $"+{at:0.00}s");
            Log($"  +{at:0.00}s {Necks(world)}");
            CheckMoving(sim);
        }
        Log($"등장 +3.6s 클립: {Clips(world)}");
        Log($"등장 +3.6s 위치: {PosText(world)}");
        CheckSpacing(world, sim);

        // 여우 — 가끔 쳐다보는가(고개 각도를 0.1초 간격으로 6초).
        if (_ids.Contains("fox"))
        {
            var sb = new StringBuilder("  fox 고개(°)/속도: ");
            for (int i = 0; i < 60; i++)
            {
                sb.Append(Necks(world, "fox")).Append(' ');
                await Seconds(0.1);
            }
            Log(sb.ToString());
        }

        // ── 다른 방으로 돌렸다 돌아오기 ──
        string other = sim.GetRoomIds().First(r => r != _room && r != FacilitySimulation.DeployOriginRoomId
                                                   && FacilityCctvWorldHasRoom(r));
        sim.SetSurveillanceTarget(other);
        await Seconds(3.0);
        CheckMoving(sim);
        sim.SetSurveillanceTarget(_room);
        await Frames(2);
        Shot(cctv, "다른 방 3초 후 복귀");
        Log($"복귀 직후 클립: {Clips(world)}");
        foreach (string id in _ids)
        {
            var s = world.GhostReactions.Get(id);
            string clip = ClipOf(world, id);
            var prof = s == null ? null : GhostReactionProfiles.Get(s.Action);
            if (prof != null && !prof.KeepsWorking && clip == prof.IntroClip)
                Fail($"{id}: 복귀 후 첫 반응({clip})을 다시 틀었다");
        }

        // ── 더 높은 상태가 괴물 연출에 막히지 않는가 — 반응 중인 한 명을 실제로 재배치 ──
        if (_moveTest && _ids.Count >= 2)
        {
            string mover = _ids[^1];
            if (!sim.AssignToRoom(mover, other)) Fail($"{mover}: 재배치 실패");
            await Frames(3);
            var s = world.GhostReactions.Get(mover);
            var act = CctvActionResolver.Resolve(sim, sim.GetEmployeeState(mover), mover, _room, "", "");
            Log($"재배치 직후 {mover}: 반응단계={s?.Stage} 표현={act} 클립={ClipOf(world, mover)}");
            if (s?.Stage != GhostReactionTracker.Stage.None) Fail($"{mover}: 재배치 후에도 괴물 연출이 남았다");
            if (act != CctvEmployeeAction.Walking) Fail($"{mover}: 이동 표현이 아니다({act})");
            Shot(cctv, $"{mover} 재배치 직후");
            _ids.Remove(mover);
            relocBefore = CountRelocationLogs();   // 방금 넣은 재배치 한 줄은 검사 대상이 아니다
        }

        cfg.GhostDispelSeconds = dispelSeconds;   // 원래 값으로

        // ── 끝까지 지켜봐 소멸 ──
        for (int i = 0; i < 60 * 12 && sim.Ghost.Active; i++) await Frame();
        if (sim.Ghost.Active) Fail("소멸하지 않았다");
        double t1 = Now();
        Log($"소멸 직후 클립: {Clips(world)}");
        if (_again)
        {
            // 회복하는 도중 괴물이 다시 나타난다 — 회복은 끊기고 새 반응이 먼저여야 한다.
            await Seconds(1.0);
            Log($"재등장 직전 클립: {Clips(world)}");
            if (!sim.Ghost.ForceAppear(_room, sim, 90f)) Fail("재등장 실패");
            await Seconds(0.6);
            Log($"재등장 +0.6s 클립: {Clips(world)}");
            foreach (string id in _ids)
            {
                var st2 = world.GhostReactions.Get(id);
                if (st2 == null || st2.Stage != GhostReactionTracker.Stage.Reacting) Fail($"{id}: 재등장했는데 반응하지 않는다");
                string c = ClipOf(world, id);
                if (c.Contains("recover") || c.Contains("relief") || c.Contains("floor")) Fail($"{id}: 회복 클립이 새 반응을 덮었다({c})");
            }
            Shot(cctv, "회복 중 재등장 +0.6s");
            for (int i = 0; i < 60 * 12 && sim.Ghost.Active; i++) await Frame();
            t1 = Now();
            Log($"두 번째 소멸 직후 클립: {Clips(world)}");
        }
        // 실제 업무 차단 — 괴물이 사라진 뒤 각자 몇 초 동안 업무 게이지에 기여하지 않는가.
        float day = GameState.Instance?.DayTimeSeconds ?? 0f;
        Log("업무 차단(남은 초): " + string.Join(" · ", _ids.Select(id => $"{id}={sim.GetEmployeeState(id).WorkBlockedUntil - day:0.0}")));
        var rseq = new Dictionary<string, List<string>>();
        var rjump = new Dictionary<string, float>();
        var rlast = new Dictionary<string, Vector3>();
        double[] rshots = { 0.25, 0.8, 1.6, 3.0, 4.5, 6.0, 8.0, 11.0 };
        int ri = 0;
        while (Now() - t1 < 12.0)
        {
            Track(world, rseq, rjump, rlast);
            if (ri < rshots.Length && Now() - t1 >= rshots[ri]) { Shot(cctv, $"소멸 +{rshots[ri]:0.0}s"); ri++; }
            await Frame();
        }
        foreach (string id in _ids)
            Log($"  회복 {id}: {string.Join(">", rseq.GetValueOrDefault(id) ?? new List<string>())}");
        Log($"양 후유증: {world.GhostReactions.HasAftershock("sheep")}");
        Log($"소멸 +12s 클립: {Clips(world)}");
        var back = Positions(world);
        foreach (string id in _ids)
            // 그사이 방 업무가 바뀌었으면 자리도 바뀐다 — 같은 업무일 때만 같은 자리인지 본다.
            if (workClip[id] == ClipOf(world, id)
                && workPos.TryGetValue(id, out var p0) && back.TryGetValue(id, out var p1) && p0.DistanceTo(p1) > 0.25f)
                Fail($"{id}: 원래 자리로 돌아오지 않았다 ({p0} → {p1})");

        // ── 게임 상태 그대로인가 ──
        var after = Snapshot(sim);
        foreach (string id in _ids)
            if (before[id] != after[id]) Fail($"{id}: 상태 변함 {before[id]} → {after[id]}");
        int relocAfter = CountRelocationLogs();
        if (relocAfter != relocBefore) Fail($"배치/이동 로그가 늘었다 {relocBefore} → {relocAfter}");
        Log($"소멸 {sim.Ghost.DispelledToday}회 · 사고 {sim.Ghost.StruckToday}회");

        Finish();
    }

    // ── 작업실 6인 · 착석 · 퇴장 ──────────────────────────────────────

    private async System.Threading.Tasks.Task RunWork(FacilitySimulation sim, ControlRoom3DController ctl)
    {
        var world = FacilityCctvWorld.Instance;
        var cctv = ctl.FacilityCctvViewport;
        cctv.Size = new Vector2I(960, 720);
        _keepDrawing = cctv;
        for (int i = 0; i < 60 * 60 && !Actors(world).Any(kv => _ids.Contains(kv.Key) && kv.Value.Visible); i++) await Frame();
        double t0 = Now();
        Log("입장 시작");
        var seq = new Dictionary<string, List<string>>();
        var jump = new Dictionary<string, float>();
        var last = new Dictionary<string, Vector3>();
        double[] shots = { 0.5, 2.0, 4.0, 7.0, 11.0, 16.0 };
        int si = 0;
        while (Now() - t0 < 18.0)
        {
            Track(world, seq, jump, last);
            if (si < shots.Length && Now() - t0 >= shots[si]) { Shot(cctv, $"입장 +{shots[si]:0.0}s"); si++; }
            await Frame();
        }
        foreach (string id in _ids)
            Log($"  {id}: {string.Join(">", seq.GetValueOrDefault(id) ?? new List<string>())}  (최대 한 프레임 이동 {jump.GetValueOrDefault(id):0.00}m)");
        Log("자리: " + SpotsText(world));
        Log("클립: " + Clips(world));
        Log("위치: " + PosText(world));
        CheckSpacing(world, sim);
        var spotsUsed = SpotIds(world);
        foreach (string id in _ids)
        {
            string c = ClipOf(world, id);
            // 운반조는 상자를 내려놓고 더미로 돌아가는 동안 걷는다 — 그건 일이다.
            if (spotsUsed.GetValueOrDefault(id, "").StartsWith("운반")) continue;
            if (c is "idle" or "inspect" or "walk") Fail($"{id}: 작업 자리에서 일하지 않는다({c})");
        }
        if (spotsUsed.Values.Distinct().Count() != spotsUsed.Count) Fail("같은 자리에 두 명");

        // 앉아 있는 사람을 다른 방으로 보낸다 — 일어난 뒤 걸어 나가야 한다(순간이동 금지).
        string seated = _ids.FirstOrDefault(id => ClipOf(world, id).StartsWith("sit_"));
        if (seated != null)
        {
            string other = sim.GetRoomIds().First(r => r != _room && FacilityCctvWorldHasRoom(r));
            sim.AssignToRoom(seated, other);
            seq.Clear(); jump.Clear(); last.Clear();
            double t1 = Now();
            double[] at = { 0.3, 0.8, 2.0 };
            int k = 0;
            while (Now() - t1 < 7.0)
            {
                Track(world, seq, jump, last, seated);
                if (k < at.Length && Now() - t1 >= at[k]) { Shot(cctv, $"{seated} 재배치 +{at[k]:0.0}s"); k++; }
                await Frame();
            }
            string s = string.Join(">", seq.GetValueOrDefault(seated) ?? new List<string>());
            Log($"재배치 {seated}: {s}  (최대 한 프레임 이동 {jump.GetValueOrDefault(seated):0.00}m)");
            if (!s.Contains("chair_stand")) Fail($"{seated}: 일어나는 동작 없이 떠났다");
            if (jump.GetValueOrDefault(seated) > 0.2f) Fail($"{seated}: 순간이동({jump[seated]:0.00}m)");
        }
        else Log("(앉은 사람 없음 — 착석 퇴장 검사 생략)");
    }

    // ── 격리 → 해제 ────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RunIsolation(FacilitySimulation sim, FacilityCctvWorld world, SubViewport cctv)
    {
        string who = _ids[0];
        sim.SetSurveillanceTarget("isolation_room");
        await Frames(5);
        if (!sim.IsolateEmployee(who)) { Fail("격리 실패"); return; }
        var seq = new Dictionary<string, List<string>>();
        var jump = new Dictionary<string, float>();
        var last = new Dictionary<string, Vector3>();
        double t0 = Now();
        double strapsFullAt = -1, struggleAt = -1, lyingAt = -1;
        double[] shots = { 1.0, 3.0, 5.0, 6.5, 7.5, 8.3, 9.5, 12.0 };
        int si = 0;
        while (Now() - t0 < 14.0)
        {
            Track(world, seq, jump, last, who);
            string c = ClipOf(world, who);
            int straps = VisibleStraps(world);
            if (lyingAt < 0 && c == "lying_idle") lyingAt = Now() - t0;
            if (strapsFullAt < 0 && straps >= 7) strapsFullAt = Now() - t0;
            if (struggleAt < 0 && c == "isolated_struggle") struggleAt = Now() - t0;
            if (si < shots.Length && Now() - t0 >= shots[si]) { Shot(cctv, $"격리 +{shots[si]:0.0}s (밴드 {straps})"); si++; }
            await Frame();
        }
        Log($"격리 {who}: {string.Join(">", seq.GetValueOrDefault(who) ?? new List<string>())}  (최대 한 프레임 이동 {jump.GetValueOrDefault(who):0.00}m)");
        Log($"  누움 {lyingAt:0.0}s · 밴드 다 채움 {strapsFullAt:0.0}s · 발버둥 시작 {struggleAt:0.0}s");
        if (lyingAt < 0 || strapsFullAt < 0 || struggleAt < 0) Fail("격리 순서가 끝까지 진행되지 않았다");
        else if (!(lyingAt < strapsFullAt && strapsFullAt <= struggleAt + 0.05)) Fail("누움 → 밴드 → 발버둥 순서가 아니다");

        sim.CancelIsolation(who);
        seq.Clear(); jump.Clear(); last.Clear();
        double t1 = Now();
        si = 0;
        double[] shots2 = { 0.4, 1.2, 2.2, 3.2, 4.5 };
        double freeAt = -1, getUpAt = -1;
        while (Now() - t1 < 9.0)
        {
            Track(world, seq, jump, last, who);
            if (freeAt < 0 && VisibleStraps(world) == 0) freeAt = Now() - t1;
            if (getUpAt < 0 && ClipOf(world, who) == "bed_get_up") getUpAt = Now() - t1;
            if (si < shots2.Length && Now() - t1 >= shots2[si]) { Shot(cctv, $"해제 +{shots2[si]:0.0}s (밴드 {VisibleStraps(world)})"); si++; }
            await Frame();
        }
        Log($"해제 {who}: {string.Join(">", seq.GetValueOrDefault(who) ?? new List<string>())}  (최대 한 프레임 이동 {jump.GetValueOrDefault(who):0.00}m)");
        Log($"  밴드 풀림 {freeAt:0.0}s · 일어나기 시작 {getUpAt:0.0}s");
        if (getUpAt < 0 || freeAt < 0 || freeAt > getUpAt + 0.05) Fail("밴드가 풀리기 전에 일어났다(또는 일어나지 않았다)");
        if (jump.GetValueOrDefault(who) > 0.2f) Fail($"{who}: 순간이동({jump[who]:0.00}m)");
    }

    private int VisibleStraps(FacilityCctvWorld world)
    {
        var rooms = (Dictionary<string, Node3D>)typeof(FacilityCctvWorld)
            .GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world);
        var iso = rooms?.GetValueOrDefault("isolation_room");
        if (iso == null) return 0;
        int n = 0;
        foreach (var c in iso.GetChildren())
        {
            if (c is not MeshInstance3D m) continue;
            string name = m.Name.ToString();
            if (name.StartsWith("RestraintBed") && (name.Contains("Strap") || name.Contains("Buckle")) && m.Visible) n++;
        }
        return n;
    }

    // 클립이 바뀔 때마다 기록 · 한 프레임에 가장 크게 움직인 거리(순간이동 감지).
    private void Track(FacilityCctvWorld world, Dictionary<string, List<string>> seq, Dictionary<string, float> jump,
                       Dictionary<string, Vector3> last, string only = null)
    {
        foreach (var (id, node) in Actors(world))
        {
            if (!_ids.Contains(id) || only != null && id != only || !node.Visible) { last.Remove(id); continue; }
            string c = ClipOf(world, id);
            var list = seq.TryGetValue(id, out var l) ? l : seq[id] = new List<string>();
            if (list.Count == 0 || list[^1] != c) list.Add(c);
            // 골반 위치로 잰다(노드는 의자·침대 전환 때 골반 보정으로 움직이므로).
            var hips = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
            var p = hips?.GlobalPosition ?? node.GlobalPosition;
            if (last.TryGetValue(id, out var q)) jump[id] = Mathf.Max(jump.GetValueOrDefault(id), p.DistanceTo(q));
            last[id] = p;
        }
    }

    private Dictionary<string, string> SpotIds(FacilityCctvWorld world)
    {
        var ctrl = typeof(FacilityCctvWorld).GetField("_workVisual", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world);
        var actors = ctrl?.GetType().GetField("_actors", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as System.Collections.IDictionary;
        var outd = new Dictionary<string, string>();
        if (actors == null) return outd;
        foreach (System.Collections.DictionaryEntry e in actors)
        {
            string id = (string)e.Key;
            if (!_ids.Contains(id)) continue;
            var spot = e.Value.GetType().GetField("Spot")?.GetValue(e.Value) as RoomWorkSpot;
            int carrier = (int)(e.Value.GetType().GetField("Carrier")?.GetValue(e.Value) ?? -1);
            outd[id] = carrier >= 0 ? $"운반{carrier}" : spot?.Id ?? "-";
        }
        return outd;
    }

    private string SpotsText(FacilityCctvWorld world) => string.Join(" · ", SpotIds(world).Select(kv => $"{kv.Key}={kv.Value}"));

    private static bool FacilityCctvWorldHasRoom(string roomId) =>
        roomId is "power_room" or "vent_room" or "maintenance_room" or "medical_room"
            or "guard_room" or "core_room" or "storage_room";

    // ── 검사 ──

    private Dictionary<string, string> Snapshot(FacilitySimulation sim) =>
        _ids.ToDictionary(id => id, id =>
        {
            var st = sim.GetEmployeeState(id);
            return $"cur={st.CurrentRoomId} asg={st.AssignedRoomId} moving={st.IsMoving}";
        });

    private void CheckMoving(FacilitySimulation sim)
    {
        foreach (string id in _ids)
        {
            var st = sim.GetEmployeeState(id);
            if (st.IsMoving || st.CurrentRoomId != _room) Fail($"{id}: 실제 이동 발생 ({st.CurrentRoomId}, moving={st.IsMoving})");
        }
    }

    private void CheckSpacing(FacilityCctvWorld world, FacilitySimulation sim)
    {
        var pos = Positions(world);
        var ids = pos.Keys.ToList();
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                float d = pos[ids[i]].DistanceTo(pos[ids[j]]);
                if (d < 0.45f) Fail($"겹침: {ids[i]}-{ids[j]} {d:0.00}m");
            }
        var ghost = world.GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Scale.X > 3f && n.Visible);
        if (ghost != null)
            foreach (var id in new[] { "cat", "dog", "wolf", "rabbit" })
                if (pos.TryGetValue(id, out var p))
                {
                    float d = new Vector2(p.X - ghost.Position.X, p.Z - ghost.Position.Z).Length();
                    Log($"  {id} ↔ 괴물 {d:0.00}m");
                }
    }

    // 이동·배치에 관한 시설 로그 줄 수(숨으러 뛰어가도 늘면 안 된다).
    private int CountRelocationLogs() =>
        EventLog.Instance?.GetAllEntries().Count(e => e.EventType is LogEventType.Relocation
                                                     || _ids.Contains(e.ActorEmployeeId) && e.Description.Contains("이동")) ?? -1;

    // ── 3D 표현 읽기 ──

    private Dictionary<string, Node3D> Actors(FacilityCctvWorld world) =>
        (Dictionary<string, Node3D>)typeof(FacilityCctvWorld)
            .GetField("_employees", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world);

    private Dictionary<string, EmployeeCctvAnimator> Animators(FacilityCctvWorld world) =>
        (Dictionary<string, EmployeeCctvAnimator>)typeof(FacilityCctvWorld)
            .GetField("_animators", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world);

    private Dictionary<string, Vector3> Positions(FacilityCctvWorld world) =>
        Actors(world).Where(kv => _ids.Contains(kv.Key) && kv.Value.Visible)
                     .ToDictionary(kv => kv.Key, kv => kv.Value.Position with { Y = 0 });

    private string ClipOf(FacilityCctvWorld world, string id) => Animators(world).GetValueOrDefault(id)?.CurrentClip ?? "-";

    private string Clips(FacilityCctvWorld world) => string.Join(" · ", _ids.Select(id => $"{id}={ClipOf(world, id)}"));

    // 고개(Neck) 좌우 각도와 AnimationPlayer 재생 속도 — 여우의 "힐끔"과 "손 멈춤"을 숫자로 본다.
    private string Necks(FacilityCctvWorld world, string only = null) =>
        string.Join(" ", Actors(world).Where(kv => (only == null ? _ids.Contains(kv.Key) : kv.Key == only) && kv.Value.Visible)
            .Select(kv =>
            {
                var neck = kv.Value.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/Neck");
                var ap = kv.Value.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
                string yaw = neck == null ? "-" : $"{Mathf.RadToDeg(neck.Rotation.Y):0}";
                return only != null ? $"{yaw}/{ap?.SpeedScale:0.0}" : $"{kv.Key}={yaw}°x{ap?.SpeedScale:0.0}";
            }));

    private string PosText(FacilityCctvWorld world) =>
        string.Join(" · ", Positions(world).Select(kv => $"{kv.Key}({kv.Value.X:0.00},{kv.Value.Z:0.00})"));

    // ── 사진 ──

    private void Shot(SubViewport vp, string label)
    {
        var img = vp.GetTexture().GetImage();
        img.Convert(Image.Format.Rgba8);
        _shots.Add((label, img));
    }

    private void Finish()
    {
        if (_shots.Count > 0)
        {
            // 한 장으로 모은다(4열). 각 칸 480×360.
            const int cw = 480, ch = 360, cols = 4;
            int rows = (_shots.Count + cols - 1) / cols;
            var sheet = Image.CreateEmpty(cw * cols, ch * rows, false, Image.Format.Rgba8);
            for (int i = 0; i < _shots.Count; i++)
            {
                var img = (Image)_shots[i].Img.Duplicate();
                img.Resize(cw, ch, Image.Interpolation.Bilinear);
                sheet.BlitRect(img, new Rect2I(0, 0, cw, ch), new Vector2I(i % cols * cw, i / cols * ch));
                _shots[i].Img.SavePng($"{_dir}/{_tag}_{i:00}.png");
            }
            sheet.SavePng($"{_dir}/{_tag}_sheet.png");
            for (int i = 0; i < _shots.Count; i++) Log($"  [{i:00}] {_shots[i].Label}");
        }
        Log(_fails == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_fails})");
        System.IO.File.WriteAllText($"{_dir}/{_tag}_report.txt", _report.ToString());
        if (!_fromHub) GetTree().Quit();
    }

    private void Log(string s)
    {
        GD.Print("[GHOST] " + s);
        _report.AppendLine(s);
        System.IO.File.AppendAllText($"{_dir}/{_tag}_progress.txt", s + System.Environment.NewLine);
    }
    private void Fail(string s) { _fails++; Log("!! " + s); }

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    private async System.Threading.Tasks.Task Until(double t)
    {
        while (Now() < t) await Frame();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
