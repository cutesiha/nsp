using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 저장고 상자 운반 동작이 매 프레임 클립을 갈아타지 않는지 잰다.
//
// ※ 사람(기절한 동료) 운반 연출은 여기서 보지 않는다 — RescueCarryTest 가 따로 본다.
//   godot --path . res://scenes/debug/RescueCarryTest.tscn -- <운반자> <환자>
//   화면으로 볼 때는 RescueCarryShot(단계별 스틸, slow 인자로 0.5배속).
//
//   godot --headless --path . res://scenes/debug/CarryAnimTest.tscn
//
// 증상: 상자를 나르는 동안 팔이 앞뒤로 크게 휘저어졌다(토끼 · 고양이 · 양).
// 원인: 클립의 위팔 키프레임이 6.0028rad 처럼 2π 를 넘겨 적혀 있었다. −0.28rad 과 같은
//       자세지만 Godot 은 오일러 트랙을 각도가 아니라 숫자로 선형 보간하므로,
//       −0.27 ↔ 6.00 사이를 오가며 팔이 매 주기 한 바퀴(약 359°) 돌았다.
//
// 그래서 실제 위팔 각도가 한 클립 안에서 얼마나 움직이는지를 직접 잰다.
// 정상 운반은 몇 도 수준이고, 2π 가 섞이면 6rad(≈344°) 가 나온다.
public partial class CarryAnimTest : Node
{
    private const float WatchSeconds = 16f;
    private const float MaxArmSwingRad = 2.0f;   // 정상 집기/놓기도 이보다는 훨씬 작다
    private const int MaxChangesPerSecond = 6;   // 국면 전환은 넉넉잡아 이 정도

    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("################ 상자 운반 동작 검사 ################");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || ctl == null) { Fail("씬이 뜨지 않았다"); return; }

        // 저장고에 두 명 — 운반은 두 명이 수레 하나씩 맡는다.
        var roster = sim.GetActiveEmployeeIds().ToList();
        for (int i = 0; i < 2 && i < roster.Count; i++) sim.AssignToRoom(roster[i], "storage_room");
        for (int i = 2; i < roster.Count; i++) sim.AssignToRoom(roster[i], "guard_room");
        ctl.BeginShift();
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();

        // 저장고를 CCTV 로 지켜봐야 작업실 3D 가 살아나고 운반 동작이 돈다.
        sim.SetSurveillanceTarget("storage_room");
        await Seconds(1.5);

        // 상자 정리 업무가 떠야 운반이 시작된다.
        for (int i = 0; i < 200 && sim.GetActiveTasksForRoom("storage_room").All(t => t.TaskId != "inventory_sorting"); i++)
            await Seconds(0.25);
        bool sorting = sim.GetActiveTasksForRoom("storage_room").Any(t => t.TaskId == "inventory_sorting");
        Check(sorting, $"저장고에 상자 정리 업무가 떴다 ({string.Join(",", sim.GetActiveTasksForRoom("storage_room").Select(t => t.TaskId))})");
        if (!sorting) { Done(); return; }

        // ── 계측 ──────────────────────────────────────────────────────
        var players = new List<AnimationPlayer>();
        Collect(FacilityCctvWorld.Instance, players);
        Check(players.Count > 0, $"CCTV 월드에서 직원 AnimationPlayer {players.Count}개를 찾았다");
        if (players.Count == 0) { Done(); return; }

        var last = new Dictionary<AnimationPlayer, string>();
        var changes = new Dictionary<AnimationPlayer, List<float>>();
        var seen = new Dictionary<AnimationPlayer, List<string>>();
        // 클립별 위팔 각도 범위(최댓값 - 최솟값). 한 바퀴 도는 팔이 여기서 드러난다.
        var swing = new Dictionary<string, (float Min, float Max)>();
        foreach (var p in players) { last[p] = ""; changes[p] = new(); seen[p] = new(); }

        const string ArmL = "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderL/UpperArmL";
        const string ArmR = "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderR/UpperArmR";

        float t = 0f;
        while (t < WatchSeconds)
        {
            await Frame();
            t += (float)GetProcessDeltaTime();
            foreach (var p in players)
            {
                if (!IsInstanceValid(p)) continue;
                string now = p.CurrentAnimation;
                if (now != last[p])
                {
                    last[p] = now;
                    changes[p].Add(t);
                    if (seen[p].Count < 14) seen[p].Add(string.IsNullOrEmpty(now) ? "(없음)" : now);
                }
                if (!now.Contains("_box")) continue;
                var body = p.GetParent();
                foreach (string arm in new[] { ArmL, ArmR })
                {
                    if (body?.GetNodeOrNull<Node3D>(arm) is not { } j) continue;
                    string key = $"{now} · {(arm == ArmL ? "왼팔" : "오른팔")}";
                    float x = j.Rotation.X;
                    swing[key] = swing.TryGetValue(key, out var r)
                        ? (Mathf.Min(r.Min, x), Mathf.Max(r.Max, x)) : (x, x);
                }
            }
        }

        int worst = 0;
        AnimationPlayer worstPlayer = null;
        foreach (var p in players)
        {
            // 1초 창을 훑어 가장 많이 바뀐 구간을 찾는다.
            var ts = changes[p];
            for (int i = 0; i < ts.Count; i++)
            {
                int n = ts.Count(x => x >= ts[i] && x < ts[i] + 1f);
                if (n > worst) { worst = n; worstPlayer = p; }
            }
        }

        foreach (var p in players)
            if (changes[p].Count > 0)
                GD.Print($"   {p.GetParent()?.Name}: 클립 {changes[p].Count}회 전환 — {string.Join(" → ", seen[p])}");

        Check(worst <= MaxChangesPerSecond,
              $"1초 안에 클립이 가장 많이 바뀐 횟수 {worst}회 (허용 {MaxChangesPerSecond}회)"
              + (worstPlayer != null && worst > MaxChangesPerSecond ? $" — {worstPlayer.GetParent()?.Name}" : ""));

        // 위팔이 한 클립 안에서 얼마나 움직이는가 — 이게 "팔 휘젓기" 의 실체다.
        var over = new List<string>();
        foreach (var (key, r) in swing.OrderBy(k => k.Key))
        {
            float range = r.Max - r.Min;
            GD.Print($"   {key}: 위팔 각도 {Mathf.RadToDeg(r.Min):0.0}° ~ {Mathf.RadToDeg(r.Max):0.0}° (폭 {Mathf.RadToDeg(range):0.0}°)");
            if (range > MaxArmSwingRad) over.Add($"{key} 폭 {Mathf.RadToDeg(range):0}°");
        }
        Check(swing.Count > 0, $"상자 클립에서 위팔 각도를 {swing.Count}건 쟀다");
        Check(over.Count == 0, over.Count == 0
            ? $"어떤 상자 클립도 위팔이 {Mathf.RadToDeg(MaxArmSwingRad):0}° 넘게 휘돌지 않는다"
            : $"팔이 휘도는 클립 {over.Count}건 — {string.Join(" · ", over)}");

        Done();
    }

    private static void Collect(Node root, List<AnimationPlayer> into)
    {
        if (root == null) return;
        if (root is AnimationPlayer ap && ap.HasAnimation("carry_box_male")) into.Add(ap);
        foreach (var c in root.GetChildren()) Collect(c, into);
    }

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }

    private void Fail(string label) { Check(false, label); Done(); }

    private void Done()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
