using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 기절한 동료를 의무실 침대까지 데려가는 전 과정의 CCTV 표현.
//
// 바닥에 쓰러진 환자 → 구조자가 몸을 낮춰 실제로 잡음 → 들어 올림 → 다리를 움직이며 이동
// → 의무실 진입 → 침대 바로 옆까지 이동 → 몸을 돌림 → 천천히 내려놓음 → 손을 놓음
// → 환자 lying_idle → 구조자가 일어나 환자를 확인 → 돌아서서 작업실로 복귀.
//
// 중간에 한 번도 순간이동하지 않는다. 그래서 여기서는
//   · 운반자를 FollowRoute 로 **실제로 걷게** 하고(발이 미끄러지지 않게 재생 속도를 맞춘다),
//   · 환자를 운반자의 Carry Anchor 에 붙여 매 프레임 따라가게 하고,
//   · 침대 인계는 환자 transform 을 앵커 → 침대로 **연속해서** 옮긴다.
//
// 판정(어느 단계인가 · 언제 회복이 시작되는가)은 전부 FaintRescueSystem 이 쥐고 있다.
// 여기서 시스템에 돌려주는 것은 "침대 옆에 실제로 닿았다" 한 가지뿐이다(NotifyAtBedside).
public sealed partial class RoomWorkVisualController
{
    // 운반 한 건의 표현 상태. 운반자 Actor 가 들고 있다.
    private sealed class CarryState
    {
        public string VictimId = "";
        public FaintPhase LastPhase = FaintPhase.None;
        public bool Turned;             // 픽업/인계 전에 몸을 다 돌렸는가
        public float RecoverT = -1f;    // 환자를 눕힌 뒤 허리를 펴는 동작의 경과 시간
        public Vector3 FloorPos;        // 들어 올리기 시작할 때 환자가 누워 있던 자리
        public bool FloorCaptured;
        public bool Notified;           // 침대 옆 도착을 시스템에 알렸는가
    }

    // 침대 옆에 설 자리를 못 찾았을 때 쓰는 기본 간격(침대 중심에서 옆으로).
    private const float BedSideOffset = 0.78f;
    // 들어 올리기 전에 운반자가 환자에게서 떨어져 서는 거리.
    private const float PickupStandDistance = 0.34f;

    // 운반자 한 명을 그린다. 환자 노드까지 여기서 같이 잡는다.
    // 반환값 = 이 프레임을 여기서 처리했는가(true 면 일반 업무 표현을 건너뛴다).
    private bool TickCarry(string carrierId, Node3D node, EmployeeCctvAnimator anim, Actor actor,
                           Node3D victimNode, EmployeeCctvAnimator victimAnim, EmployeeState victim,
                           bool female, Vector3 fallbackPos, bool leaving,
                           List<RoomWorkSpot> spots, float delta)
    {
        if (victim == null || victimNode == null) return false;
        var style = FaintRescueSystem.StyleOf(carrierId);
        var cs = actor.Carry ??= new CarryState();
        if (cs.VictimId != victim.EmployeeId)
        {
            cs.VictimId = victim.EmployeeId;
            cs.Turned = false;
            cs.FloorCaptured = false;
            cs.Notified = false;
        }
        if (cs.LastPhase != victim.Faint)
        {
            cs.LastPhase = victim.Faint;
            cs.Turned = false;
            actor.HasRoute = false;
            cs.Notified = false;
        }

        // 환자는 운반 시스템만이 자세를 정한다(§64) — 일반 업무/자리 배치가 끼어들지 못하게.
        victimAnim?.SetProceduralRoot(true);

        switch (victim.Faint)
        {
            case FaintPhase.TransportPickup:
                TickPickup(node, anim, actor, cs, victimNode, victimAnim, victim, style, female, delta);
                return true;

            case FaintPhase.Transporting:
                TickCarryWalk(node, anim, actor, victimNode, victimAnim, victim, style, female,
                              fallbackPos, leaving, delta);
                return true;

            case FaintPhase.BedApproach:
                TickBedApproach(node, anim, actor, cs, victimNode, victimAnim, victim, style, female,
                                spots, fallbackPos, delta);
                return true;

            case FaintPhase.InMedicalBed:
                TickBedHandoff(node, anim, actor, cs, victimNode, victimAnim, victim, style, female,
                               spots, delta);
                return true;
        }
        return false;
    }

    // ① 들어 올리기 — 환자 옆에 자리를 잡고, 몸을 낮춰 잡고, 실제로 들어 올린다.
    //
    // 환자는 바닥 자세에서 운반 자세로 **연속해서** 올라온다. 클립이 손을 환자 몸에 대는
    // 시점(대략 절반)까지는 거의 바닥에 있다가, 그 뒤에 상체 → 골반 순으로 들린다(§8).
    private void TickPickup(Node3D node, EmployeeCctvAnimator anim, Actor actor, CarryState cs,
                            Node3D victimNode, EmployeeCctvAnimator victimAnim, EmployeeState victim,
                            CarryStyle style, bool female, float delta)
    {
        if (!cs.FloorCaptured)
        {
            cs.FloorPos = Flat(victimNode.Position);
            cs.FloorCaptured = true;
        }

        float total = FaintRescueSystem.PickupSeconds(style);
        float t = Mathf.Clamp(victim.FaintPhaseTimer, 0f, total);

        // 자리 — 환자를 밟지 않게 조금 떨어져 선다. 업기는 등을 환자 쪽으로 돌린다(§10).
        var stand = cs.FloorPos + FaintVisuals.AwayFrom(cs.FloorPos, Flat(node.Position), PickupStandDistance);
        node.Position = node.Position.Lerp(stand, Mathf.Clamp(delta * 8f, 0f, 1f));
        float yaw = PickupYaw(style, cs.FloorPos, node.Position);
        node.Rotation = new Vector3(0f, Mathf.LerpAngle(node.Rotation.Y, yaw, 1f - Mathf.Exp(-12f * delta)), 0f);

        // 운반자 — 픽업 클립을 지금 단계 시각에 맞춰 튼다.
        // (CCTV 를 돌렸다 돌아와도 처음부터 다시 재생하지 않는다 — §61)
        PlayAt(anim, anim?.BodyClip(FaintVisuals.PickupClip(style), female), t);
        anim?.SetProceduralRoot(true);

        // 환자 — 바닥 → 운반 자세. 손이 몸에 닿기 전에는 거의 움직이지 않는다.
        float grab = 0.45f;   // 이 시점에 운반자의 손이 환자 몸에 닿는다
        float k = t <= grab * total ? 0f : Mathf.SmoothStep(0f, 1f, (t - grab * total) / (total - grab * total));
        PlayVictimClip(victimAnim, victim, style, female, k);
        var floor = FaintVisuals.FloorTransform(victimNode, cs.FloorPos);
        var carried = FaintVisuals.VictimTransform(node, victimNode, style);
        victimNode.GlobalTransform = LiftBlend(floor, carried, k);
        SetVictimLyingLift(victimNode, Mathf.Lerp(FaintVisuals.LyingLift, 0f, Mathf.Min(1f, k * 1.6f)));
        // 누워 있던 몸이 들리면서 같이 일어난다 — 상체가 먼저 서고 골반이 따라온다.
        SetVictimRigPitch(victimNode, Mathf.Lerp(FloorPitch, CarriedPitch(style), Mathf.SmoothStep(0f, 1f, k)));
    }

    // ② 운반 — 실제로 걷는다. 발이 미끄러지지 않게 걸음 재생 속도를 이동 속도에 맞춘다(§25).
    private void TickCarryWalk(Node3D node, EmployeeCctvAnimator anim, Actor actor,
                               Node3D victimNode, EmployeeCctvAnimator victimAnim, EmployeeState victim,
                               CarryStyle style, bool female, Vector3 fallbackPos, bool leaving, float delta)
    {
        string clip = anim?.BodyClip(FaintVisuals.CarryWalkClip(style), female);
        float speed = FaintRescueSystem.CarrySpeedScale(victim.TransporterId);
        var goal = leaving ? DoorPoint : Flat(fallbackPos);
        if (FollowRoute(node, anim, actor, goal, delta, speed, clip))
        {
            // 다 왔는데 아직 이송 중이다 — 환자를 안은 채 선다(팔은 그대로 둔다).
            anim?.PlayClip(clip);
            anim?.SetSpeedFactor(0.001f);
        }
        else SyncCarryWalkSpeed(anim, style, speed);
        anim?.SetProceduralRoot(true);
        AttachVictim(node, victimNode, victimAnim, victim, style, female);
    }

    // ③ 침대 접근 — 의무실에 들어왔다고 멈추지 않는다. 침대 옆까지 **계속 걸어간다**(§36).
    private void TickBedApproach(Node3D node, EmployeeCctvAnimator anim, Actor actor, CarryState cs,
                                 Node3D victimNode, EmployeeCctvAnimator victimAnim, EmployeeState victim,
                                 CarryStyle style, bool female, List<RoomWorkSpot> spots,
                                 Vector3 fallbackPos, float delta)
    {
        var bed = FindBed(spots, victim.MedicalBedSpotId);
        string clip = anim?.BodyClip(FaintVisuals.CarryWalkClip(style), female);
        float speed = FaintRescueSystem.CarrySpeedScale(victim.TransporterId);

        if (bed == null)
        {
            // 이 방에 그 침대가 없다(다른 방을 보고 있다) — 걷던 모습 그대로 둔다.
            TickCarryWalk(node, anim, actor, victimNode, victimAnim, victim, style, female,
                          fallbackPos, false, delta);
            return;
        }

        var (approach, faceYaw) = BedApproachPoint(bed);
        if (!FollowRoute(node, anim, actor, approach, delta, speed, clip))
        {
            SyncCarryWalkSpeed(anim, style, speed);
            anim?.SetProceduralRoot(true);
            AttachVictim(node, victimNode, victimAnim, victim, style, female);
            return;
        }

        // 침대 옆에 섰다 — 걸음을 멈추고 침대 쪽으로 몸을 돌린다(§37).
        node.Position = approach;
        anim?.SetSpeedFactor(1f);
        if (!cs.Turned)
        {
            if (!TurnTo(node, anim, faceYaw, delta, clip))
            {
                anim?.SetProceduralRoot(true);
                AttachVictim(node, victimNode, victimAnim, victim, style, female);
                return;
            }
            cs.Turned = true;
        }
        anim?.PlayClip(clip);
        anim?.SetSpeedFactor(0.001f);   // 양발을 멈춘 채 안고 있는 자세로 선다
        anim?.SetProceduralRoot(true);
        AttachVictim(node, victimNode, victimAnim, victim, style, female);

        // 다 돌았다 — 이제부터 내려놓기다. 판정 쪽에 한 번만 알린다.
        if (cs.Notified) return;
        cs.Notified = true;
        FacilitySimulation.Instance?.Rescue?.NotifyAtBedside(victim.EmployeeId);
    }

    // ④ 침대에 내려놓기 — 환자가 매트리스에 **실제로 닿은 뒤에야** 손을 놓는다(§42 · §43).
    private void TickBedHandoff(Node3D node, EmployeeCctvAnimator anim, Actor actor, CarryState cs,
                                Node3D victimNode, EmployeeCctvAnimator victimAnim, EmployeeState victim,
                                CarryStyle style, bool female, List<RoomWorkSpot> spots, float delta)
    {
        var bed = FindBed(spots, victim.MedicalBedSpotId);
        float total = FaintRescueSystem.BedHandoffSeconds;
        float t = Mathf.Clamp(victim.FaintPhaseTimer, 0f, total);

        if (bed != null)
        {
            var (approach, faceYaw) = BedApproachPoint(bed);
            node.Position = node.Position.Lerp(approach, Mathf.Clamp(delta * 8f, 0f, 1f));
            node.Rotation = new Vector3(0f,
                Mathf.LerpAngle(node.Rotation.Y, faceYaw, 1f - Mathf.Exp(-12f * delta)), 0f);
        }

        PlayAt(anim, anim?.BodyClip(FaintVisuals.BedPlaceClip(style), female), t);
        anim?.SetSpeedFactor(1f);
        anim?.SetProceduralRoot(true);

        var carried = FaintVisuals.VictimTransform(node, victimNode, style);
        if (bed == null)
        {
            // 침대가 안 보이는 방 — 적어도 안고 있는 자세는 유지한다.
            PlayVictimClip(victimAnim, victim, style, female, 1f);
            victimNode.GlobalTransform = carried;
            return;
        }

        // 운반자 동작과 같은 박자로 환자를 침대로 옮긴다(§45).
        //   0~0.22   몸을 낮추고 돈다(환자는 거의 그대로)
        //   0.22~0.6 환자가 침대 쪽으로 내려간다
        //   0.6~0.78 torso 가 매트리스에 닿는다
        //   0.78~1.0 머리·다리 정렬 → 손을 뗀다
        float u = t / total;
        float move = Mathf.Clamp((u - 0.22f) / 0.56f, 0f, 1f);
        float k = Mathf.SmoothStep(0f, 1f, move);

        var layTarget = BedLyingTransform(bed);
        victimNode.GlobalTransform = LayBlend(carried, layTarget, k);
        // 안겨 있던 몸이 내려가며 매트리스 위로 눕는다. 완전히 닿기 조금 전에 수평이 되게
        // 먼저 돌려 둬야, lying_idle 로 바뀌는 순간 몸이 또 한 번 꺾이지 않는다.
        SetVictimRigPitch(victimNode, Mathf.Lerp(CarriedPitch(style), FloorPitch,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k * 1.3f, 0f, 1f))));

        // 매트리스에 닿기 전에는 절대 lying_idle 로 바꾸지 않는다(§46) —
        // 바꾸는 순간 몸이 혼자 눕는 것처럼 보인다.
        bool touched = u >= 0.74f;
        if (touched)
        {
            victimAnim?.PlayClip("lying_idle");
            SetVictimLyingLift(victimNode, Mathf.Lerp(0f, bed.SeatHeight + LyingBackLift,
                Mathf.Clamp((u - 0.74f) / 0.18f, 0f, 1f)));
        }
        else
        {
            PlayVictimClip(victimAnim, victim, style, female, 1f);
            SetVictimLyingLift(victimNode, 0f);
        }
    }

    // ⑤ 환자를 눕힌 뒤 — 손을 빼고 허리를 펴고 환자를 한 번 본다(§49 · §50).
    // 운반자가 CarryingVictimId 를 놓은 뒤에도 CarrierRecoverTimer 동안 여기로 들어온다.
    private bool TickCarrierRecover(Node3D node, EmployeeCctvAnimator anim, Actor actor,
                                    EmployeeState st, bool female, float delta)
    {
        if (st.CarrierRecoverTimer <= 0f)
        {
            if (actor.Carry != null) actor.Carry.RecoverT = -1f;
            return false;
        }
        var cs = actor.Carry ??= new CarryState();
        if (cs.RecoverT < 0f) cs.RecoverT = 0f;
        cs.RecoverT += delta;
        Release(actor);
        PlayAt(anim, anim?.BodyClip(FaintVisuals.CarrierRecoverClip, female), cs.RecoverT);
        anim?.SetSpeedFactor(1f);
        anim?.SetProceduralRoot(true);
        return true;
    }

    // ── 도우미 ────────────────────────────────────────────────────────

    // 환자를 운반 자세 그대로 붙여 둔다(걷는 동안 매 프레임).
    private static void AttachVictim(Node3D node, Node3D victimNode, EmployeeCctvAnimator victimAnim,
                                     EmployeeState victim, CarryStyle style, bool female)
    {
        PlayVictimClip(victimAnim, victim, style, female, 1f);
        victimNode.GlobalTransform = FaintVisuals.VictimTransform(node, victimNode, style);
        SetVictimLyingLift(victimNode, 0f);
        SetVictimRigPitch(victimNode, CarriedPitch(style));
    }

    // 환자 자세 — 들어 올리는 중에는 바닥 자세(lying_idle)에서 운반 자세로 넘어간다.
    private static void PlayVictimClip(EmployeeCctvAnimator victimAnim, EmployeeState victim,
                                       CarryStyle style, bool female, float lifted)
    {
        if (victimAnim == null) return;
        // 아직 바닥에 있는 동안은 누운 자세 그대로. 들리기 시작하면 운반 자세로 블렌드된다
        // (AnimationPlayer 의 기본 블렌드가 그 사이를 메운다).
        string clip = lifted <= 0.02f
            ? "lying_idle"
            : victimAnim.BodyClip(FaintVisuals.VictimClip(style), female);
        victimAnim.PlayClip(clip, 0.35);
    }

    // 환자 VisualRoot 높이 — 바닥/침대에 얹을 때만 쓴다(운반 중에는 0, 클립이 정한다).
    private static void SetVictimLyingLift(Node3D victimNode, float y)
    {
        var vr = victimNode?.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return;
        vr.Position = vr.Position with { Y = y };
    }

    // 몸 전체를 눕히는 각도(RigRoot). 0 = 선 몸, 90 = 누운 몸.
    //
    // 이게 왜 필요한가: lying_idle 은 RigRoot 를 90° 돌려 몸을 눕힌다. 업기·부축 자세 클립에는
    // RigRoot 트랙이 없어서, 바닥에서 일으켜도 **90° 가 그대로 남아** 환자가 누운 채 공중에 뜬다.
    // 평소라면 RelaxOrphans 가 되돌려 주지만, 운반 중에는 VisualRoot 를 직접 쓰느라
    // SetProceduralRoot(true) 로 그 복원을 꺼 두었다. 그래서 여기서 직접 돌려 준다.
    private static void SetVictimRigPitch(Node3D victimNode, float degrees)
    {
        var rig = victimNode?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot");
        if (rig == null) return;
        rig.Rotation = new Vector3(Mathf.DegToRad(degrees), rig.Rotation.Y, rig.Rotation.Z);
    }

    // 그 운반 방식에서 환자 몸이 최종적으로 누워 있는 각도.
    private static float CarriedPitch(CarryStyle style) =>
        style == NSP.Facility.CarryStyle.Bridal ? 90f : 0f;

    // 바닥에 누워 있는 몸의 각도.
    private const float FloorPitch = 90f;

    // 바닥 → 운반 자세. 몸이 한 덩어리로 떠오르되 상체가 먼저 들린다(§8).
    private static Transform3D LiftBlend(Transform3D floor, Transform3D carried, float k)
    {
        if (k <= 0f) return floor;
        if (k >= 1f) return carried;
        // 위치는 조금 늦게, 회전은 조금 먼저 — 상체부터 일어나는 것처럼 보인다.
        float posK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k * 1.15f - 0.15f, 0f, 1f));
        var origin = floor.Origin.Lerp(carried.Origin, posK);
        // 들리는 동안 살짝 위로 솟았다 제자리를 찾는다(끌어 올리는 느낌).
        origin.Y += Mathf.Sin(k * Mathf.Pi) * 0.05f;
        var basis = floor.Basis.Slerp(carried.Basis, Mathf.SmoothStep(0f, 1f, k));
        return new Transform3D(basis, origin);
    }

    // 운반 자세 → 침대. 가로로 미끄러져 날아가지 않게 아래로 내려놓는 느낌을 준다(§44).
    private static Transform3D LayBlend(Transform3D carried, Transform3D bed, float k)
    {
        if (k <= 0f) return carried;
        if (k >= 1f) return bed;
        float posK = Mathf.SmoothStep(0f, 1f, k);
        var origin = carried.Origin.Lerp(bed.Origin, posK);
        // 수평 이동이 먼저, 내려앉기가 나중 — 침대 위를 지나 내려놓는 궤적.
        origin.Y = Mathf.Lerp(carried.Origin.Y, bed.Origin.Y, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k * 1.4f - 0.4f, 0f, 1f)));
        var basis = carried.Basis.Slerp(bed.Basis, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k * 1.25f, 0f, 1f)));
        return new Transform3D(basis, origin);
    }

    // 환자가 최종적으로 눕는 자리(§35) — torso 가 매트리스 한가운데, 머리가 베개 쪽.
    // 씬에 PatientLayTarget 마커가 있으면 그것을, 없으면 침대 자리 자체를 쓴다.
    // (마커가 기본 위치면 MoveToLyingSpot 이 놓는 자리와 정확히 같다 — 인계가 끝난 뒤
    //  일반 표현으로 넘어갈 때 몸이 튀지 않는다.)
    private static Transform3D BedLyingTransform(RoomWorkSpot bed)
    {
        var marker = bed.GetNodeOrNull<Node3D>("PatientLayTarget");
        if (marker == null) return new Transform3D(Basis.FromEuler(bed.Rotation), Flat(bed.Position));
        var pos = Flat(bed.Position + bed.Basis * marker.Position);
        return new Transform3D(Basis.FromEuler(bed.Rotation + marker.Rotation), pos);
    }

    // 운반자가 환자를 내려놓기 좋은 자리 — 침대 중심이 아니라 **침대 옆 바닥**이다(§34).
    // 씬에 CarrierApproach 마커가 있으면 그것을, 없으면 침대 옆으로 계산해서 쓴다.
    private static (Vector3 Pos, float Yaw) BedApproachPoint(RoomWorkSpot bed)
    {
        var marker = bed.GetNodeOrNull<Node3D>("CarrierApproach");
        if (marker != null)
            return (Flat(bed.Position + bed.Basis * marker.Position), bed.Rotation.Y + marker.Rotation.Y);

        // 침대 자리의 로컬 +X 쪽(머리-발 축의 옆)으로 비켜선다.
        var side = bed.Basis * new Vector3(BedSideOffset, 0f, -0.55f);
        var pos = Flat(bed.Position + side);
        // 침대 쪽(-X)을 보도록 돈다.
        return (pos, bed.Rotation.Y - Mathf.Pi * 0.5f);
    }

    private static RoomWorkSpot FindBed(List<RoomWorkSpot> spots, string spotId)
    {
        if (spots == null || string.IsNullOrEmpty(spotId)) return null;
        foreach (var s in spots)
            if (s.SpotId == spotId || s.Name == spotId) return s;
        return null;
    }

    // 들어 올릴 때 서는 방향. 업기는 등을 환자 쪽으로, 부축은 환자를 왼쪽에 둔다.
    private static float PickupYaw(CarryStyle style, Vector3 victimPos, Vector3 carrierPos)
    {
        var d = Flat(carrierPos) - Flat(victimPos);
        if (d.LengthSquared() < 1e-4f) d = Vector3.Right;
        d = d.Normalized();
        return style switch
        {
            // 등을 환자 쪽으로 — 환자 반대쪽을 본다.
            NSP.Facility.CarryStyle.Piggyback => Mathf.Atan2(d.X, d.Z),
            // 환자를 자기 왼쪽에 둔다 — 오른쪽(+X)이 환자 반대 방향을 향하게.
            NSP.Facility.CarryStyle.Shoulder => Mathf.Atan2(-d.Z, d.X),
            // 환자를 마주 본다.
            _ => Mathf.Atan2(-d.X, -d.Z),
        };
    }

    // 걷기 재생 속도 = 실제 이동 속도 / 클립 한 바퀴가 나아가는 거리 (§25).
    private static void SyncCarryWalkSpeed(EmployeeCctvAnimator anim, CarryStyle style, float speedScale)
    {
        if (anim == null) return;
        float metersPerSecond = WalkSpeed * speedScale;
        float cycle = Mathf.Max(0.05f, anim.CurrentClipLength);
        float clipSpeed = FaintVisuals.CarryWalkStride(style) / cycle;   // 클립 기본 속도(m/s)
        anim.SetSpeedFactor(Mathf.Clamp(metersPerSecond / Mathf.Max(0.05f, clipSpeed), 0.25f, 2.5f));
    }

    // 클립을 "지금 이 시각"부터 튼다. CCTV 를 돌렸다 돌아와도 처음부터 다시 하지 않게(§61).
    private static void PlayAt(EmployeeCctvAnimator anim, string clip, float at)
    {
        if (anim == null || string.IsNullOrEmpty(clip)) return;
        if (anim.CurrentClip != clip) anim.PlayClip(clip, 0.12, Mathf.Max(0f, at));
    }
}
