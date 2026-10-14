using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 기절한 동료를 의무실 침대까지 데려가는 **화면 연출**을 실제로 재서 검사한다.
//
//   godot --headless --path . res://scenes/debug/RescueCarryTest.tscn
//
// 판정(단계 전환 · 회복 시작 시점)은 FaintRescueTest 가 본다. 여기서 보는 것은
// "CCTV 에서 실제로 그렇게 보이는가" — 어떤 클립이 돌았는지, 다리가 움직였는지,
// 두 몸이 붙어 있었는지, 환자가 침대로 연속해서 옮겨졌는지다.
//
// 예전 증상(이번 작업의 대상)
//   · 사람을 드는데 박스용 pickup_box / carry_box_* 가 재생됐다
//   · 이동 중 다리가 멈춘 채 모델만 미끄러졌다
//   · 의무실 방에 들어온 순간 바로 회복으로 넘어가 환자가 침대에 순간이동했다
public partial class RescueCarryTest : Node
{
    private const float Step = 1f / 60f;
    private const string Room = "maintenance_room";
    // 기본은 늑대(업기)가 양을 옮기는 경우. 인자로 다른 조합을 넘길 수 있다.
    //   godot ... res://scenes/debug/RescueCarryTest.tscn -- <운반자> <환자>
    private string VictimId = "sheep";
    private string CarrierId = "wolf";

    // 다리 관절이 "실제로 걸었다"고 볼 최소 각도 폭(도).
    private const float MinLegSwingDeg = 12f;
    // 운반 중 두 몸(루트)이 떨어져 있어도 되는 최대 거리(m).
    private const float MaxPairDistance = 0.95f;
    // 침대로 옮기는 동안 한 프레임에 환자가 튈 수 있는 최대 거리(m) — 이보다 크면 순간이동이다.
    private const float MaxFrameJump = 0.30f;

    private int _pass, _fail;

    private static readonly string[] BoxClips =
    {
        "pickup_box", "carry_box_normal", "carry_box_heavy",
        "carry_box_male", "carry_box_female", "carry_box_sheep",
        "pickup_box_male", "pickup_box_female", "pickup_box_sheep",
        "place_box_male", "place_box_female", "place_box_sheep",
    };

    private const string ChestPath = "VisualRoot/RigRoot/Hips/Torso/Chest";
    private const string LegPath = "VisualRoot/RigRoot/Hips/UpperLegL";
    private const string ShinPath = "VisualRoot/RigRoot/Hips/UpperLegL/LowerLegL";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var argv = OS.GetCmdlineUserArgs();
        if (argv.Length > 1) { CarrierId = argv[0]; VictimId = argv[1]; }
        var style = FaintRescueSystem.StyleOf(CarrierId);
        GD.Print($"################ 사람 이송 연출 검사 — {CarrierId}({style}) 가 {VictimId} 를 옮긴다 ################");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || ctl == null) { Fail("씬이 뜨지 않았다"); return; }

        // 환자와 구조자를 한 방에, 나머지는 멀리 떨어뜨린다.
        foreach (string id in sim.GetActiveEmployeeIds())
            sim.AssignToRoom(id, id == VictimId || id == CarrierId ? Room : "guard_room");
        ctl.BeginShift();
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(1.0);

        // 그 방을 CCTV 로 봐야 3D 가 살아나고 연출이 돈다.
        sim.SetSurveillanceTarget(Room);
        await Seconds(1.5);
        for (int i = 0; i < 600 && sim.GetEmployeeState(CarrierId).IsMoving; i++) await Frame();
        await Seconds(0.8);

        var v = sim.GetEmployeeState(VictimId);
        var c = sim.GetEmployeeState(CarrierId);
        Check(v.CurrentRoomId == Room && c.CurrentRoomId == Room,
            $"환자와 구조자가 같은 방에 있다 ({v.CurrentRoomId} / {c.CurrentRoomId})");

        sim.AddStress(VictimId, 80f, "검수");
        for (int i = 0; i < 1200 && v.Faint != FaintPhase.AwaitingDecision; i++) await Frame();
        Check(v.Faint == FaintPhase.AwaitingDecision, $"동료가 발견해 전화를 걸었다 ({v.Faint})");
        sim.Rescue.Approve(VictimId);

        // ── 관찰 ──────────────────────────────────────────────────────
        var world = FacilityCctvWorld.Instance;
        var phasesSeen = new List<FaintPhase>();
        var carrierClips = new HashSet<string>();
        var victimClips = new HashSet<string>();
        var clipByPhase = new Dictionary<FaintPhase, HashSet<string>>();
        float legMin = 999f, legMax = -999f, shinMin = 999f, shinMax = -999f;
        float maxPair = 0f;
        float maxJump = 0f, handoffTravel = 0f;
        bool lyingBeforeBed = false;
        bool detachedEarly = false;
        bool recoverClipSeen = false;
        float minHipsY = 99f, maxAnchorGap = 0f, finalHipsY = -1f;
        var maxPairPhase = FaintPhase.None;
        float maxPairAfterSwitch = -1f, lastSwitchT = -99f;
        float recoverSeenAt = 0f, recoverDeadline = 0f;
        bool walkClipWhileMoving = false;
        Vector3 lastVictimPos = Vector3.Zero;
        bool havePos = false;
        FaintPhase prev = FaintPhase.None;

        string watching = Room;
        for (float t = 0f; t < 90f; t += Step)
        {
            await Frame();
            // CCTV 는 한 번에 한 방만 그린다 — 운반자를 따라가야 복도 건너편 연출까지 볼 수 있다.
            // (안 따라가면 의무실 장면이 아예 렌더되지 않아 "안 보인다"가 "안 한다"로 보인다.)
            if (c.CurrentRoomId != watching && !string.IsNullOrEmpty(c.CurrentRoomId))
            {
                watching = c.CurrentRoomId;
                lastSwitchT = t;
                sim.SetSurveillanceTarget(watching);
            }
            var cn = world?.EmployeeNode(CarrierId);
            var vn = world?.EmployeeNode(VictimId);
            var ca = world?.EmployeeAnimator(CarrierId);
            var va = world?.EmployeeAnimator(VictimId);
            var phase = v.Faint;
            if (phase != prev) { phasesSeen.Add(phase); prev = phase; }

            string cclip = ca?.CurrentClip ?? "";
            string vclip = va?.CurrentClip ?? "";
            bool carrying = phase is FaintPhase.TransportPickup or FaintPhase.Transporting
                or FaintPhase.BedApproach or FaintPhase.InMedicalBed;

            if (carrying)
            {
                if (!string.IsNullOrEmpty(cclip)) carrierClips.Add(cclip);
                if (!string.IsNullOrEmpty(vclip)) victimClips.Add(vclip);
                if (!clipByPhase.TryGetValue(phase, out var set))
                    clipByPhase[phase] = set = new HashSet<string>();
                if (!string.IsNullOrEmpty(cclip)) set.Add(cclip);

                // 손을 놓지 않았는가(§42).
                if (phase != FaintPhase.InMedicalBed && c.CarryingVictimId != VictimId) detachedEarly = true;

                // 루트(발끝)끼리 재면 안 된다 — 안기 자세는 환자 루트가 몸 옆으로 비켜 있어
                // 붙어 있어도 1.4m 가 나온다. 두 몸이 닿았는지는 가슴끼리 재는 것이 맞다.
                // 다 들어 올린 뒤부터 잰다 — 픽업 처음에는 아직 걸어가는 중이라 당연히 떨어져 있다.
                if (phase is FaintPhase.Transporting or FaintPhase.BedApproach)
                {
                    var cc = cn?.GetNodeOrNull<Node3D>(ChestPath);
                    var vc = vn?.GetNodeOrNull<Node3D>(ChestPath);
                    if (cc != null && vc != null)
                    {
                        float gap = cc.GlobalPosition.DistanceTo(vc.GlobalPosition);
                        if (gap > maxPair) { maxPair = gap; maxPairPhase = phase; maxPairAfterSwitch = t - lastSwitchT; }
                    }
                }

                // 업고 가는 동안 환자 골반이 바닥 가까이 내려오면 "끌고 가는" 그림이 된다.
                // 또 운반자 앵커에서 멀어지면 몸이 떠서 따라오는 것처럼 보인다.
                var vh = vn?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
                var anc = cn?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/CarryAnchor")
                          ?? cn?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/CarryAnchor");
                if (phase == FaintPhase.Transporting && vh != null)
                {
                    minHipsY = Mathf.Min(minHipsY, vh.GlobalPosition.Y);
                    if (anc != null) maxAnchorGap = Mathf.Max(maxAnchorGap,
                        anc.GlobalPosition.DistanceTo(vh.GlobalPosition));
                }

                // 매트리스에 닿기 전에 lying_idle 로 바뀌면 몸이 혼자 눕는다(§46).
                if (phase != FaintPhase.InMedicalBed && vclip == "lying_idle"
                    && phase != FaintPhase.TransportPickup)
                    lyingBeforeBed = true;
            }

            // 이동 중 다리가 실제로 움직이는가(§22 · §24).
            if (phase == FaintPhase.Transporting && cn != null)
            {
                if (cclip.StartsWith("faint_carry_walk")) walkClipWhileMoving = true;
                if (cn.GetNodeOrNull<Node3D>(LegPath) is { } leg)
                {
                    legMin = Mathf.Min(legMin, leg.Rotation.X);
                    legMax = Mathf.Max(legMax, leg.Rotation.X);
                }
                if (cn.GetNodeOrNull<Node3D>(ShinPath) is { } shin)
                {
                    shinMin = Mathf.Min(shinMin, shin.Rotation.X);
                    shinMax = Mathf.Max(shinMax, shin.Rotation.X);
                }
            }

            // 침대 인계 — 환자가 연속해서 옮겨지는가(§43).
            if (phase == FaintPhase.InMedicalBed && vn != null)
            {
                var p = vn.GlobalPosition;
                if (havePos)
                {
                    float d = p.DistanceTo(lastVictimPos);
                    maxJump = Mathf.Max(maxJump, d);
                    handoffTravel += d;
                }
                lastVictimPos = p;
                havePos = true;
            }
            else havePos = false;

            if (phase == FaintPhase.Recovering && vn != null)
                finalHipsY = vn.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips")?.GlobalPosition.Y ?? finalHipsY;
            if (cclip.StartsWith("faint_carrier_recover")) { recoverClipSeen = true; if (recoverSeenAt <= 0f) recoverSeenAt = t; }
            if (phase == FaintPhase.Recovering && recoverSeenAt <= 0f && t > 1f) recoverDeadline += Step;
            if (phase == FaintPhase.Recovering && ((recoverSeenAt > 0f && t > recoverSeenAt + 1.5f) || recoverDeadline > 4f)) break;
        }

        // ── 판정 ──────────────────────────────────────────────────────
        var boxUsed = carrierClips.Where(x => BoxClips.Contains(x)).ToList();

        Check(!clipByPhase.GetValueOrDefault(FaintPhase.TransportPickup, new()).Any(x => x.StartsWith("pickup_box")),
            $"1. 들어 올릴 때 pickup_box 를 쓰지 않는다 — {Join(clipByPhase.GetValueOrDefault(FaintPhase.TransportPickup, new()))}");

        Check(boxUsed.Count == 0,
            boxUsed.Count == 0 ? "2. 운반 내내 박스 클립이 한 번도 안 쓰였다"
                               : $"2. 박스 클립이 쓰였다 — {Join(boxUsed)}");

        Check(walkClipWhileMoving,
            $"3. 이동 중 사람 운반 걷기 클립이 실제로 재생됐다 — {Join(clipByPhase.GetValueOrDefault(FaintPhase.Transporting, new()))}");

        float legSwing = Mathf.RadToDeg(legMax - legMin), shinSwing = Mathf.RadToDeg(shinMax - shinMin);
        Check(legSwing >= MinLegSwingDeg && shinSwing >= MinLegSwingDeg,
            $"4. 이동 중 다리가 실제로 움직인다 (허벅지 {legSwing:0.0}° · 정강이 {shinSwing:0.0}°)");

        Check(maxPair > 0f && maxPair <= MaxPairDistance,
            $"5. 운반 내내 두 몸통이 붙어 있다 (가슴 사이 최대 {maxPair:0.00}m @ {maxPairPhase} · 방전환 +{maxPairAfterSwitch:0.00}s / 허용 {MaxPairDistance:0.00}m)");

        int roomIdx = phasesSeen.IndexOf(FaintPhase.BedApproach);
        int recIdx = phasesSeen.IndexOf(FaintPhase.Recovering);
        Check(roomIdx >= 0 && recIdx > roomIdx,
            $"6. 의무실에 들어온 즉시 회복으로 넘어가지 않는다 — {Join(phasesSeen.Select(p => p.ToString()))}");

        Check(phasesSeen.Contains(FaintPhase.BedApproach), "7. 침대 접근(BedApproach) 단계가 실제로 지나간다");

        Check(handoffTravel > 0.25f && maxJump <= MaxFrameJump,
            $"8. 인계 동안 환자가 연속해서 옮겨진다 (이동 {handoffTravel:0.00}m · 최대 한 프레임 {maxJump:0.00}m)");

        Check(!detachedEarly, "9. 침대에 닿기 전에 손을 놓지 않는다");

        Check(!lyingBeforeBed, "10. 침대에 눕기 전에는 lying_idle 로 바뀌지 않는다");

        Check(recIdx > phasesSeen.IndexOf(FaintPhase.InMedicalBed) && recIdx >= 0,
            "11. 완전히 눕힌 뒤에만 회복이 시작된다");

        Check(recoverClipSeen, "12. 환자를 눕힌 뒤 구조자가 일어나는 동작(faint_carrier_recover)을 재생한다");

        // 부축(Shoulder)은 환자 발이 바닥에 닿아 있는 것이 정상이다(§18) — 기준이 다르다.
        float minHips = style == CarryStyle.Shoulder ? 0.45f : 0.80f;
        Check(minHipsY > minHips,
            $"13. 옮기는 동안 환자가 바닥에 끌리지 않는다 (골반 최저 {minHipsY:0.00}m / 기준 {minHips:0.00}m)");

        Check(maxAnchorGap > 0f && maxAnchorGap < 0.35f,
            $"14. 이동 내내 환자 골반이 운반 앵커에 붙어 있다 (최대 {maxAnchorGap:0.00}m)");

        Check(finalHipsY > 0.55f && finalHipsY < 1.05f,
            $"15. 환자가 침대 높이에 눕는다 (골반 {finalHipsY:0.00}m)");

        GD.Print($"   [정보] 구조자 클립: {Join(carrierClips)}");
        GD.Print($"   [정보] 환자 클립: {Join(victimClips)}");
        GD.Print($"   [정보] 단계 순서: {Join(phasesSeen.Select(p => p.ToString()))}");
        Done();
    }

    private static string Join(IEnumerable<string> xs) => string.Join(" · ", xs);

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
