using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 귀신이 **나타나고 날뛰는 동안** 걸리는 비용의 출처를 가른다.
//
//   godot --path . res://scenes/debug/GhostPerfProbe.tscn
//
// 반드시 창 모드로 돌린다(헤드리스는 그리지 않으므로 전부 거짓말이 된다).
//
// 귀신 하나가 오면 여러 가지가 한꺼번에 일어난다 — 등장 · 비명(덮침/머리박기) ·
// 소멸(가루) · 사고(설비 파괴). "렉이 걸린다" 에서 더 나아가려면 **어느 것이**
// 몇 ms 인지 따로 봐야 한다. 그래서 하나씩 일으키고 그 직후 프레임을 전부 적는다.
//
// 기계는 돌릴수록 느려지므로(PERFORMANCE_BASELINE.md G) 기준을 매번 바로 앞에서
// 다시 재고 그 짝과만 비교한다.
public partial class GhostPerfProbe : Node
{
    private const double Warm = 0.6;
    private const double Sample = 2.0;

    private readonly List<PerfStats.Result> _rows = new();
    private readonly List<string> _notes = new();

    public override void _Ready() => _ = Run();

    public override void _Process(double delta) => PerfStats.Sample(delta);

    private async System.Threading.Tasks.Task Run()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("GhostPerfProbe: 창 모드로 돌릴 것.");
            GetTree().Quit(1);
            return;
        }

        // V-Sync 를 끄고 재는 선택지 — 144Hz 에 막혀 있으면 진짜 작업량이 보이지 않는다.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "novsync") >= 0)
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 1800 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        var sim = FacilitySimulation.Instance;
        if (sim?.Ghost == null) { GD.PrintErr("시뮬레이션/귀신 계통을 찾지 못했다"); GetTree().Quit(1); return; }

        Deploy(sim);
        GameState.Instance?.SetPhase(GamePhase.Live);
        await Frames(90);
        sim.SetSurveillanceTarget("power_room");
        await Frames(60);

        // 실제 근무를 그대로 타고 가며 재는 모드 — 아무것도 강제하지 않는다.
        //   godot --path . res://scenes/debug/GhostPerfProbe.tscn -- ride [초]
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "ride") >= 0
            && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "come") < 0)
        {
            await Ride(sim);
            return;
        }

        // 문 앞 → 침입 → 사망 연출까지 — 가장 무거운 한 토막.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "death") >= 0)
        {
            GD.Print("\n######## 침입과 사망 연출 ########\n");
            var seg = sim.Corridors.ById("corridor_north");
            if (seg != null) sim.Corridors.Unseal(seg.Id);
            var th2 = sim.Threats.ForceSpawn("absentee", "corridor_north");
            if (th2 == null) { GD.PrintErr("위협을 만들지 못했다"); GetTree().Quit(1); return; }
            sim.SetCorridorSurveillance("corridor_north");
            th2.Phase = ThreatPhase.Approach;
            th2.Progress = 0.86f;
            await Measure(sim, "복도 접근 - 문 앞까지", keepGhost: true);
            for (int i = 0; i < 1800 && th2.Phase != ThreatPhase.AtDoor; i++) await Frame();
            await Burst(sim, "문 앞 유예", () => { }, frames: 120);
            for (int i = 0; i < 1800 && !AdminDeathDirector.IsPlaying; i++) await Frame();
            // 프레임 수로 끊으면 FPS 가 높을수록 구간이 짧아진다(250 FPS 에서 240프레임은 정적 1초뿐이었다).
            await Burst(sim, "침입 → 돌진 → 사망 화면", () => { }, frames: 2000);
            await Frames(2);
            GetTree().Quit();
            return;
        }

        // 날이 바뀌며 새로 나오는 개체까지 준비가 닿는가.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "warm") >= 0)
        {
            Say("\n######## 날짜별 준비(MonsterWarmup) ########\n");
            Say($"   DAY1 부팅 직후 준비된 개체 {MonsterWarmup.WarmedCount}종 (결번자만이면 1)");
            var ctl = ControlRoom3DController.Instance;
            foreach (int day in new[] { 2, 3 })
            {
                MonsterWarmup.Run(ctl, day);
                for (int i = 0; i < 90; i++) await Frame();
                Say($"   DAY{day} 준비 호출 뒤 {MonsterWarmup.WarmedCount}종");
            }
            // 준비가 끝난 뒤 거미를 처음 화면에 올려 본다.
            sim.SetSurveillanceTarget("power_room");
            await Frames(30);
            sim.Ghost.ForceAppear("power_room", sim, 120f);
            SetMonsterId(sim, "spider");
            sim.SetSurveillanceTarget("core_room");
            await Frames(6);
            await Burst(sim, "거미 - 준비 뒤 처음 화면에", () => sim.SetSurveillanceTarget("power_room"), frames: 40);
            WriteReport();
            await Frames(2);
            GetTree().Quit();
            return;
        }

        // 세 개체를 차례로 띄워 본다 — 준비(MonsterWarmup)가 닿지 않은 모델이
        // 처음 화면에 뜰 때 몇 ms 를 먹는가. DAY1 로 부팅하면 결번자만 준비된다.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "models") >= 0)
        {
            GD.Print("\n######## 개체별 - 처음 화면에 뜰 때 ########\n");
            GD.Print($"   오늘 = DAY {GameState.Instance?.CurrentDay}, 준비 완료 = {MonsterWarmup.Done}");
            sim.SetSurveillanceTarget("power_room");
            await Frames(40);
            foreach (string id in new[] { "absentee", "infant", "spider", "infant" })
            {
                sim.Ghost.ForceDepart(sim, notify: false);
                await Frames(20);
                sim.Ghost.ForceAppear("power_room", sim, 120f);
                SetMonsterId(sim, id);
                // 화면에서 한 번 내렸다 올려 그 모델로 다시 꽂게 한다.
                sim.SetSurveillanceTarget("core_room");
                await Frames(6);
                await Burst(sim, $"개체 {id} - 처음 화면에", () => sim.SetSurveillanceTarget("power_room"), frames: 40);
            }
            await Frames(2);
            GetTree().Quit();
            return;
        }

        // 귀신이 **나한테 걸어오는** 길을 그대로 탄다 — 작업실 → 복도 → 문 앞 → 침입.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "come") >= 0)
        {
            GD.Print("\n######## 귀신이 걸어온다 - 느린 프레임만 적는다 ########\n");
            sim.Ghost.ForceAppear("power_room", sim, 3f);
            await Frames(60);
            sim.Ghost.ForceDepart(sim, notify: true);   // 복도로 내보낸다
            await Frames(30);
            var th = sim.Threats?.Current;
            GD.Print(th != null
                ? $"   복도 위협 생성됨 - {th.SegmentId} / {th.Def?.MonsterId}"
                : "   복도 위협이 생기지 않았다(모드 설정 확인)");
            if (th != null) sim.SetCorridorSurveillance(th.SegmentId);
            await Ride(sim, watchCorridor: true);
            return;
        }

        GD.Print("\n######## 귀신 - 어디서 멈추는가 ########\n");

        await Measure(sim, "기준 A1 (귀신 없음)");

        // ── 등장 ────────────────────────────────────────────────────
        await Burst(sim, "등장(보이는 방), 최초", () => sim.Ghost.ForceAppear("power_room", sim, 60f));
        await Measure(sim, "기준 A2", keepGhost: true);

        // ── 비명 세 번 — 반응이 넷으로 갈리므로 여러 번 본다 ─────────
        for (int i = 1; i <= 4; i++)
            await Burst(sim, $"비명 {i}회", () => Raise(sim, "Screamed", "power_room"));

        // ── 소멸(가루) ──────────────────────────────────────────────
        await Burst(sim, "소멸 - 가루 입자", () => Raise(sim, "Dispelled", "power_room"), frames: 90);

        // ── 사고(설비 파괴) ─────────────────────────────────────────
        sim.Ghost.ForceDepart(sim, notify: false);
        await Frames(30);
        sim.Ghost.ForceAppear("power_room", sim, 60f);
        await Frames(40);
        await Burst(sim, "사고 - 설비 파괴(불꽃)", () => Raise(sim, "Struck", "power_room"), frames: 150);

        // ── 채널을 돌려 귀신을 발견하는 순간 ────────────────────────
        sim.Ghost.ForceDepart(sim, notify: false);
        await Frames(30);
        sim.Ghost.ForceAppear("core_room", sim, 60f);
        await Frames(30);
        await Burst(sim, "채널 전환으로 발견(코어실)", () => sim.SetSurveillanceTarget("core_room"));

        sim.Ghost.ForceDepart(sim, notify: false);
        sim.SetSurveillanceTarget("power_room");
        await Measure(sim, "기준 A3 (귀신 없음)");

        // ── 복도로 걸어 나온 개체 — 관리자 쪽으로 다가온다 ──────────
        var t = sim.Threats.ForceSpawn("absentee", "corridor_north");
        if (t != null)
        {
            sim.SetCorridorSurveillance("corridor_north");
            await Frames(30);
            await Measure(sim, "복도 접근(화면에 보임)", keepGhost: true);
            t.Phase = ThreatPhase.Approach;
            t.Progress = 0.86f;
            await Burst(sim, "문 앞까지", () => { }, frames: 120);
        }

        // ── 중앙제어실 점프스케어 ───────────────────────────────────
        var hd = NSP.Ui.HorrorDirector.Instance;
        if (hd != null) await Burst(sim, "점프스케어(얼굴)", () => hd.PlayEntityFaceJumpscare(), frames: 150);

        Say("\n" + PerfStats.Header);
        foreach (var r in _rows) Say(PerfStats.Row(r));
        Say("\n## 사건 직후 프레임(ms)\n");
        foreach (string n in _notes) Say(n);
        WriteReport();
        await Frames(2);
        GetTree().Quit();
    }

    // 사건 하나를 일으키고 그 직후 프레임을 한 장씩 적는다.
    private async System.Threading.Tasks.Task Burst(FacilitySimulation sim, string label,
        Action fire, int frames = 60)
    {
        await Seconds(Warm);
        PerfStats.Begin(label);
        ulong t0 = Time.GetTicksUsec();
        fire();
        double call = (Time.GetTicksUsec() - t0) / 1000.0;

        var ms = new List<double>();
        for (int i = 0; i < frames; i++)
        {
            ulong a = Time.GetTicksUsec();
            await Frame();
            ms.Add((Time.GetTicksUsec() - a) / 1000.0);
        }
        Keep(PerfStats.End());

        double worst = 0; int at = 0;
        for (int i = 0; i < ms.Count; i++) if (ms[i] > worst) { worst = ms[i]; at = i; }
        var sb = new System.Text.StringBuilder();
        sb.Append($"- **{label}** - 호출 {call:0.00} ms, 가장 느린 프레임 {worst:0.0} ms (+{at}프레임)\n  ");
        foreach (double x in ms) sb.Append($"{x:0} ");
        _notes.Add(sb.ToString());
        Say($"      호출 {call:0.00} ms, 가장 느린 프레임 {worst:0.0} ms (+{at})");
    }

    // 이번 등장의 개체를 바꿔 끼운다(검사 전용 — 판정 규칙은 건드리지 않는다).
    private static void SetMonsterId(FacilitySimulation sim, string id)
    {
        var f = typeof(GhostHauntSystem).GetField("<ActiveMonsterId>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(sim.Ghost, id);
        else GD.PrintErr("   개체 종류를 바꾸지 못했다");
    }

    // 표현 쪽만 따로 보기 위해 판정이 쏘는 신호를 그대로 쏜다(게임 규칙은 건드리지 않는다).
    private static void Raise(FacilitySimulation sim, string eventName, string roomId)
    {
        var f = typeof(GhostHauntSystem).GetField(eventName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (f?.GetValue(sim.Ghost) is Action<string> d) d(roomId);
        else GD.PrintErr($"   {eventName} 신호를 찾지 못했다");
    }

    private async System.Threading.Tasks.Task Measure(FacilitySimulation sim, string label, bool keepGhost = false)
    {
        if (!keepGhost) sim.Ghost?.ForceDepart(sim, notify: false);
        await Seconds(Warm);
        PerfStats.Begin(label);
        await Seconds(Sample);
        Keep(PerfStats.End());
    }

    // 내보낸 빌드에는 콘솔이 없다 — 찍은 것을 파일로도 남긴다(인자 out:<경로>).
    private readonly System.Text.StringBuilder _file = new();

    private void Say(string line)
    {
        GD.Print(line);
        _file.AppendLine(line);
    }

    private void WriteReport()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("out:")) continue;
            using var fh = FileAccess.Open(a[4..], FileAccess.ModeFlags.Write);
            if (fh != null) fh.StoreString(_file.ToString());
            return;
        }
    }

    private void Keep(PerfStats.Result r)
    {
        _rows.Add(r);
        Say($"   {r.Label} - 평균 {r.AvgFps:0.0} FPS, 1%low {r.LowOnePercentFps:0.0}, " +
                 $"최악 {r.WorstMs:0.0}ms, draw {r.DrawCalls}, 오브젝트 {r.RenderObjects}");
    }

    private void Deploy(FacilitySimulation sim)
    {
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = "storage_room", ["dog"] = "guard_room", ["wolf"] = "maintenance_room",
            ["rabbit"] = "core_room", ["sheep"] = "power_room", ["fox"] = "vent_room",
        };
        foreach (var kv in rooms)
        {
            var st = sim.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value;
            st.CurrentRoomId = kv.Value;
            st.Alive = true; st.Isolated = false; st.Incapacitated = false;
        }
    }

    // ── 근무를 그대로 타고 간다 ──────────────────────────────────────
    //
    // 강제로 일으킨 사건 하나하나는 전부 빨랐다. 그렇다면 남은 가능성은
    // "여러 가지가 겹쳐 돌아가는 실제 근무" 다. 그래서 아무것도 건드리지 않고
    // 끝까지 돌리며, 느린 프레임이 나오면 **그때 무슨 일이 벌어지고 있었는지**를
    // 함께 적는다. 관리자처럼 가끔 채널도 돌린다.
    private async System.Threading.Tasks.Task Ride(FacilitySimulation sim, bool watchCorridor = false)
    {
        var args = OS.GetCmdlineUserArgs();
        double limit = 180;
        for (int i = 0; i < args.Length - 1; i++)
            if ((args[i] == "ride" || args[i] == "come") && double.TryParse(args[i + 1], out double v)) limit = v;

        GD.Print($"\n######## 실제 근무 {limit:0}초 - 느린 프레임만 적는다 ########\n");

        string[] channels = { "power_room", "core_room", "storage_room", "guard_room",
                              "maintenance_room", "vent_room" };

        // 느린 프레임 옆에 "그 직전에 무슨 기록이 남았는가" 를 적는다.
        int seen = EventLog.Instance?.GetAllEntries().Count ?? 0;
        _log.Clear();
        void PullLog(double at)
        {
            var all = EventLog.Instance?.GetAllEntries();
            if (all == null) return;
            for (int i = seen; i < all.Count; i++) _log.Add((at, all[i].EventType + " " + all[i].RoomId + " " + all[i].Description));
            seen = all.Count;
        }
        var slow = new List<(double At, double Ms, string What)>();
        double t = 0, nextSwitch = 5;
        int ch = 0, frames = 0;
        double worstEver = 0;

        while (t < limit)
        {
            ulong a = Time.GetTicksUsec();
            await Frame();
            double ms = (Time.GetTicksUsec() - a) / 1000.0;
            t += GetProcessDeltaTime();
            frames++;
            PullLog(t);

            if (ms > 16.7)
            {
                slow.Add((t, ms, Context(sim) + Recent(t)));
                if (ms > worstEver) worstEver = ms;
            }

            // 관리자처럼 채널을 돌린다(복도를 지켜보는 중이면 그대로 둔다).
            if (!watchCorridor && t >= nextSwitch)
            {
                nextSwitch = t + 5;
                ch = (ch + 1) % channels.Length;
                sim.SetSurveillanceTarget(channels[ch]);
            }
            // 근무가 끝나면 거기서 멈춘다(사망 연출은 끝까지 본다).
            if (GameState.Instance?.CurrentPhase != GamePhase.Live && !AdminDeathDirector.IsPlaying) break;
        }

        GD.Print($"   {frames}프레임 / {t:0}초, 16.7ms 를 넘긴 프레임 {slow.Count}개, 가장 느린 {worstEver:0.0} ms\n");
        slow.Sort((x, y) => y.Ms.CompareTo(x.Ms));
        for (int i = 0; i < slow.Count && i < 25; i++)
            GD.Print($"   {slow[i].Ms,7:0.0} ms  @{slow[i].At,6:0.0}초   {slow[i].What}");
        await Frames(2);
        GetTree().Quit();
    }

    // 직전 1.5초 안에 남은 기록.
    private readonly List<(double At, string Text)> _log = new();

    private string Recent(double at)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = _log.Count - 1; i >= 0 && _log[i].At > at - 1.5; i--)
            sb.Append("  ◂ ").Append(_log[i].Text);
        return sb.ToString();
    }

    // 그 프레임에 무엇이 돌고 있었는가.
    private static string Context(FacilitySimulation sim)
    {
        var g = sim.Ghost;
        string ghost = g is { Active: true }
            ? $"귀신 {g.ActiveRoomId}({g.ActiveMonsterId}) {g.AliveSeconds:0}초"
            : "귀신 없음";
        var th = sim.Threats?.Current;
        string threat = th != null ? $", 복도 {th.SegmentId} {th.Phase} {th.Progress:0.00}" : "";
        string scare = AdminDeathDirector.IsPlaying ? ", 사망연출" : "";
        return ghost + threat + scare;
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
    {
        double t = 0;
        while (t < s) { await Frame(); t += GetProcessDeltaTime(); }
    }
}
