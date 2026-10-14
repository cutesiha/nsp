using Godot;

namespace NSP.View;

// 의자에 앉고 일어나기 · 방 안에서 걷기. **표현 전용이다.**
//
// 앉기:   의자 앞(SitApproach)까지 걸어와 몸을 돌린 뒤 chair_sit — 골반이 좌판 높이까지 실제로 내려간다.
// 일어나기: 하던 동작을 멈추고 chair_stand — 골반이 올라오며 의자 앞으로 선 다음에야 걷기 시작한다.
// 골반 높이·앞뒤는 클립이 아니라 여기서 움직인다(체형마다 다리 길이와 좌판 높이가 다르기 때문) —
// 박자는 클립과 같은 곡선(WorkClipReach.SitCurve / StandCurve)을 쓴다.
public sealed partial class RoomWorkVisualController
{
    private const float SitSeconds = 1.0f;     // chair_sit 길이
    private const float StandSeconds = 0.9f;   // chair_stand 길이

    private void TickSitting(Node3D node, EmployeeCctvAnimator anim, Actor actor, string workClip)
    {
        var seat = actor.Seat;
        actor.SeatT += (float)(node.GetProcessDeltaTime());
        float u = Mathf.Clamp(actor.SeatT / SitSeconds, 0f, 1f);
        node.Position = seat.Pos;
        node.Rotation = new Vector3(0f, seat.Yaw, 0f);
        float f = WorkClipReach.SitCurve(u);
        SetPelvis(node, anim, f * (seat.Height - HipY(node)), -(1f - f) * RoomWorkSpot.SitApproach);
        anim?.PlayClip("chair_sit", 0.12);
        if (!actor.CreakPlayed && u > 0.62f) { actor.CreakPlayed = true; NSP.Core.Sfx.Instance?.Play("chair_creak", -14f, 1.05f); }
        if (u >= 1f)
        {
            actor.SeatState = SeatPhase.Seated;
            anim?.PlayClip(workClip, 0.25);
        }
    }

    // 앉아 있으면 먼저 일어난다. 이미 서 있으면 true(바로 다음 동작으로).
    private bool StandUpFirst(Node3D node, EmployeeCctvAnimator anim, Actor actor, bool female, float delta,
                              float speed = 1f)
    {
        if (actor.SeatState == SeatPhase.None || actor.Seat == null) { actor.SeatState = SeatPhase.None; return true; }
        if (actor.SeatState != SeatPhase.Standing)
        {
            // 앉는 도중이었으면 내려간 만큼에서부터 일어난다.
            float from = actor.SeatState == SeatPhase.Sitting
                ? WorkClipReach.SitCurve(Mathf.Clamp(actor.SeatT / SitSeconds, 0f, 1f)) : 1f;
            actor.SeatState = SeatPhase.Standing;
            actor.SeatT = (1f - from) * StandSeconds * 0.6f;
            actor.StandSpeed = speed;
            ShowProps(actor, "");
        }
        var seat = actor.Seat;
        actor.SeatT += delta * actor.StandSpeed;
        float u = Mathf.Clamp(actor.SeatT / StandSeconds, 0f, 1f);
        node.Position = seat.Pos;
        node.Rotation = new Vector3(0f, seat.Yaw, 0f);
        float f = WorkClipReach.StandCurve(u);
        SetPelvis(node, anim, f * (seat.Height - HipY(node)), -(1f - f) * RoomWorkSpot.SitApproach);
        anim?.SetSpeedFactor(actor.StandSpeed);
        anim?.PlayClip("chair_stand", 0.1);
        if (u < 1f) return false;

        // 다 섰다 — 의자 앞에 선 자세 그대로 이어서 걷는다.
        node.Position = seat.StandPoint;
        SetPelvis(node, anim, 0f, 0f);
        actor.SeatState = SeatPhase.None;
        actor.Arrived = false;
        actor.HasRoute = false;
        return true;
    }

    // 골반(VisualRoot) 높이·앞뒤 — 이번 프레임은 컨트롤러가 직접 정한다.
    private static void SetPelvis(Node3D node, EmployeeCctvAnimator anim, float y, float z)
    {
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return;
        vr.Position = new Vector3(0f, y, z);
        anim?.SetProceduralRoot(true);
    }

    // 목표까지 방 안을 걷는다 — 설비를 뚫지 않게 길목을 거쳐 꺾는다. 도착하면 true.
    private bool FollowRoute(Node3D node, EmployeeCctvAnimator anim, Actor actor, Vector3 goal, float delta,
                             float speedScale = 1f, string walkClip = null)
    {
        goal = Flat(goal);
        var here = Flat(node.Position);
        if (!actor.HasRoute || actor.RouteGoal.DistanceTo(goal) > 0.05f)
        {
            actor.Route.Clear();
            var path = _roomClear != null ? PathTo(here, goal, out _) : null;
            if (path != null) actor.Route.AddRange(path);
            else actor.Route.Add(goal);
            actor.RouteGoal = goal;
            actor.HasRoute = true;
        }
        while (actor.Route.Count > 0)
        {
            var next = actor.Route[0];
            var d = next - here;
            float dist = d.Length();
            if (dist <= ArriveDistance) { actor.Route.RemoveAt(0); continue; }
            var dir = d / dist;
            node.Position = here + dir * Mathf.Min(WalkSpeed * speedScale * delta, dist);
            float yaw = Mathf.Atan2(-dir.X, -dir.Z);
            node.Rotation = new Vector3(0f, Mathf.LerpAngle(node.Rotation.Y, yaw, 1f - Mathf.Exp(-14f * delta)), 0f);
            ClearVisualOffset(node);
            if (walkClip == null)
            {
                anim?.SetWalkSpeedScale(speedScale);
                anim?.SetAction(CctvEmployeeAction.Walking);
            }
            else
            {
                anim?.PlayClip(walkClip, 0.15);
                anim?.SetSpeedFactor(speedScale);
            }
            return false;
        }
        actor.HasRoute = false;
        node.Position = goal;
        return true;
    }
}
