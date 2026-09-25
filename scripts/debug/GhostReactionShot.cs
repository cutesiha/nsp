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
        foreach (double at in new[] { 0.25, 0.6, 1.0, 1.6, 3.0, 6.0 })
        {
            await Until(t1 + at);
            Shot(cctv, $"소멸 +{at:0.0}s");
        }
        Log($"소멸 +6s 클립: {Clips(world)}");
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
