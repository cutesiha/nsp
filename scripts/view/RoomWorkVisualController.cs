using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 작업실 안에서 "어느 직원을 어느 자리로 보내 어떤 모습으로 보여줄지"만 정한다.
//
// **표현 전용이다.** 업무 게이지·효과·배치 판정은 전부 FacilitySimulation 이 이미 끝냈고,
// 여기서는 그 결과(현재 방 · 현재 업무 · 표현 동작)를 읽어 위치와 애니메이션만 고른다.
// 저장고 수레 역시 화면 연출일 뿐, 자재 수치는 기존 inventory_sorting 효과가 담당한다.
//
// 직원 3D 노드의 위치·회전·클립은 **이 클래스 한 곳만** 정한다(FacilityCctvWorld 는 보일지 말지만).
// 한 프레임의 우선순위:
//   격리(침대 · 스트랩 · 해제) → 기절 → 괴물 반응 → 괴물 뒤 회복 → 방을 떠남/실제 이동
//   → 제자리 동작(방해공작) → 저장고 운반 → 작업 자리(의자 앉기/일어나기 포함)
// 방 안에서 걷는 것은 모두 표현용 이동이다 — RoomId · IsMoving 은 읽기만 한다.
public sealed partial class RoomWorkVisualController
{
    // 방 안에서 자리로 걸어가는 속도(표현 전용). 시뮬레이션 이동과 무관하다.
    private const float WalkSpeed = 1.15f;
    private const float ArriveDistance = 0.10f;
    private const float TurnRate = 7f;             // 자리에 도착해 몸을 돌리는 빠르기(rad/s)

    // 격리 표현 — 발버둥 → 탈진 전환 시간(초). 게임 격리 판정/시간과는 무관하다.
    public const float IsolationStruggleSeconds = 10f;

    // 방의 출입구. 카메라가 +X/+Z 코너에 있으므로 그 아래(화면 밖)로 나가고 들어온다.
    public static readonly Vector3 DoorPoint = new(2.62f, 0f, 2.62f);
    // 방에 "막" 들어온 것으로 보는 시간 — 이보다 오래됐으면 CCTV 를 돌려 다시 본 것이므로 자리에 바로 둔다.
    private const float JustArrivedSeconds = 2.5f;

    private enum SeatPhase { None, Sitting, Seated, Standing }

    // 앉는 자리(의자 좌판 · 격리 침대 가장자리). 골반이 놓일 곳과 바라보는 방향.
    private sealed class Seat
    {
        public Vector3 Pos;      // 좌판 중심(바닥 높이)
        public float Yaw;
        public float Height;     // 좌판 높이
        public RoomWorkSpot Spot;
        public Vector3 Forward => new(-Mathf.Sin(Yaw), 0f, -Mathf.Cos(Yaw));
        public Vector3 StandPoint => Pos + Forward * RoomWorkSpot.SitApproach;
    }

    private sealed class Actor
    {
        public RoomWorkSpot Spot;
        public bool Arrived;
        // 방 안 경로(설비를 피해 꺾는 점들)
        public readonly List<Vector3> Route = new();
        public Vector3 RouteGoal;
        public bool HasRoute;
        // 의자
        public SeatPhase SeatState;
        public float SeatT;
        public Seat Seat;
        public float StandSpeed = 1f;
        public bool CreakPlayed;
        // 소품
        public Node3D BoxProp;
        public MeshInstance3D Clipboard, Wrench;
        // 저장고
        public int CartPhase;           // 0 집으러 · 1 집는 중 · 2 나르는 중 · 3 내려놓는 중
        public float CartTimer;
        public string CartId = "";
        public int Carrier = -1;        // 몇 번째 운반조인가(-1 = 운반 안 함)
        public bool BoxInHands;
        public float ReleaseT = -1f;
        public Transform3D ReleaseFrom;
        // 격리
        public IsoState Iso;
        // 괴물
        public GhostVisual Ghost;
        public Vector3? LastGhostSeen;  // 그날 마지막으로 괴물이 있던 곳(양 후유증 시선)
        public float GlanceT;
        // 화면
        public bool SnapToSpot;
        public bool Hidden = true;
        public bool Exited;
        public Node3D OwnerNode;        // 이 직원의 3D 노드(소품을 손에 붙일 때)
        public float GhostShift;        // 앉아 있다 일어나느라 늦어진 괴물 반응 시간
        public int GhostShiftSerial = -1;
    }

    private sealed class Cart
    {
        public Node3D Node;
        public Node3D Load;
        public Vector3 Home;
        public int Boxes;
        public float OutTimer = -1f;
    }

    private readonly Dictionary<string, Actor> _actors = new();
    private readonly Dictionary<string, List<RoomWorkSpot>> _spotCache = new();
    private readonly Dictionary<string, Cart> _carts = new();
    private string _cartRoom = "";

    // 자리에 붙어 있지 않을 때의 시선 방향을 FacilityCctvWorld 에서 받아 쓴다.
    private System.Func<CctvEmployeeAction, string, Vector3, float> _facing;
    public void SetFacingResolver(System.Func<CctvEmployeeAction, string, Vector3, float> f) => _facing = f;

    public void Reset()
    {
        _actors.Clear();
        _spotCache.Clear();
        _clearance.Clear();
        _hideSpots.Clear();
        _carts.Clear();
        _straps.Clear();
        _cartRoom = "";
    }

    // 이 직원이 출입구까지 걸어 나갔는가 — FacilityCctvWorld 가 이때 화면에서 지운다.
    public bool HasExited(string id) => _actors.TryGetValue(id, out var a) && a.Exited;

    // 지금 보이는 직원들을 방의 작업 자리에 배치한다.
    //   visible    : (직원 ID, 캐릭터 노드, 애니메이터, 표현 동작, 기본 슬롯 위치)
    //   arrivedAgo : 그 직원이 이 방(화면상 방)에 들어온 지 몇 초인가
    public void Update(FacilitySimulation sim, Node3D room, string roomId,
                       List<(string Id, Node3D Node, EmployeeCctvAnimator Anim,
                             CctvEmployeeAction Action, Vector3 FallbackPos)> visible,
                       float delta, GhostReactionTracker reactions = null, Vector3? ghostPos = null,
                       System.Func<string, float> arrivedAgo = null)
    {
        if (room == null) return;
        var spots = GetSpots(room, roomId);
        string taskId = sim?.GetPrimarySpawnedTask(roomId)?.TaskId ?? "";

        TickCarts(room, roomId, delta);
        BeginGhostFrame(room, roomId, visible);
        BeginStrapFrame(room, roomId);
        AssignCarriers(roomId, taskId, visible, reactions);

        var used = new Dictionary<RoomWorkSpot, int>();
        foreach (var v in visible)
        {
            var actor = _actors.TryGetValue(v.Id, out var a) ? a : _actors[v.Id] = new Actor();
            var node = v.Node;
            var anim = v.Anim;
            bool female = IsFemale(node);
            var st = sim?.GetEmployeeState(v.Id);
            actor.OwnerNode = node;
            // 시선·재생 속도·루트 제어는 필요한 곳에서만 켠다 — 매 프레임 기본값부터.
            anim?.SetLook(0f, 0f, 8f);
            anim?.SetSpeedFactor(1f);
            anim?.SetProceduralRoot(false);
            anim?.SetAftershock(0f);
            if (v.Action != CctvEmployeeAction.Leaving) actor.Exited = false;

            // 화면에 막 나타났다 — 방에 들어온 참이면 출입구에서 걸어 들어오고, CCTV 를 돌려 다시 본 것이면 제자리에.
            if (actor.Hidden)
            {
                actor.Hidden = false;
                float ago = arrivedAgo?.Invoke(v.Id) ?? 999f;
                if (ago < JustArrivedSeconds && v.Action != CctvEmployeeAction.Leaving)
                {
                    node.Position = DoorPoint;
                    node.Rotation = new Vector3(0f, Mathf.Atan2(DoorPoint.X, DoorPoint.Z), 0f);
                    actor.Arrived = false;
                    actor.HasRoute = false;
                    actor.SeatState = SeatPhase.None;
                }
                else
                {
                    actor.SnapToSpot = true;
                    if (v.Action == CctvEmployeeAction.Isolated) SnapIsolation(actor, ago);
                }
            }

            // ① 격리 — 침대까지 걸어가 눕고, 스트랩이 채워진 뒤 발버둥. 풀리면 역순으로 일어난다.
            if (v.Action == CctvEmployeeAction.Isolated || actor.Iso is { Active: true })
            {
                if (TickIsolation(node, anim, actor, spots, used, female,
                                  releasing: v.Action != CctvEmployeeAction.Isolated, delta))
                    continue;
            }

            // ② 기절한 직원은 '환자'다 — 의무실 침대에 눕힌다.
            // 반대로 멀쩡히 일하는 직원은 눕는 자리를 절대 쓰지 않는다(아래 PickSpot).
            if (st?.Incapacitated == true)
            {
                var bed = PickSpot(spots, "__patient__", actor.Spot, used);
                if (bed != null)
                {
                    used[bed] = used.GetValueOrDefault(bed) + 1;
                    if (actor.Spot != bed) { actor.Spot = bed; actor.Arrived = false; }
                    actor.SeatState = SeatPhase.None;
                    MoveToLyingSpot(node, anim, actor, bed, delta, "lying_idle");
                    continue;
                }
                // 침대가 없는 방에서 기절했으면 그 자리에 그대로 둔다(기존 동작).
            }

            // ③ 같은 방의 괴물 — 이동·방해공작·일반 업무보다 먼저 처리한다.
            // (격리·기절·실제 이동·장기 공황은 CctvActionResolver/GhostReactionTracker 가 이미 앞에 두었다.)
            var gs = reactions?.Get(v.Id);
            var prof = gs is { Stage: not GhostReactionTracker.Stage.None } ? GhostReactionProfiles.Get(gs.Action) : null;
            if (prof != null && !prof.KeepsWorking)
            {
                // 앉아 있었으면 먼저 일어난다(빠르게) — 의자 위에서 곧바로 서 있는 모습으로 바뀌지 않게.
                // 그 사이의 시간만큼 반응을 늦춰 첫 반응(놀람)을 건너뛰지 않는다.
                if (actor.GhostShiftSerial != gs.Serial) { actor.GhostShiftSerial = gs.Serial; actor.GhostShift = 0f; }
                if (!StandUpFirst(node, anim, actor, female, delta, speed: 2.2f)) { actor.GhostShift += delta; continue; }
                TickGhostReaction(v.Id, node, anim, v.FallbackPos, actor, gs, prof, ghostPos,
                                  spots, taskId, used, delta);
                if (actor.Ghost is { HasGhost: true }) actor.LastGhostSeen = actor.Ghost.LastGhost;
                continue;
            }
            // ④ 여우 — 괴물이 사라진 뒤 "뭐였냐 저건." 한 번(자리·앉은 자세 그대로).
            if (prof != null && gs.Stage == GhostReactionTracker.Stage.Recovering && TickFoxShrug(node, anim, actor, gs, prof, female))
                continue;
            if (prof != null) FoxOverlay(v.Id, node, anim, actor, gs, ghostPos);
            else actor.Ghost = null;

            // 그날 괴물을 본 양 — 정상 작업하되 약하게 떨고, 가끔 괴물이 있던 쪽을 본다.
            if (reactions?.HasAftershock(v.Id) == true) TickAftershock(node, anim, actor, delta);

            // ⑤ 방을 떠난다(재배치 · 격리 · 다른 방으로 이동) — 앉아 있었으면 일어나서 출입구로 걸어 나간다.
            bool leaving = v.Action == CctvEmployeeAction.Leaving
                           || v.Action == CctvEmployeeAction.Walking && st != null
                              && !string.IsNullOrEmpty(st.TargetRoomId) && st.TargetRoomId != roomId;
            if (leaving)
            {
                if (!StandUpFirst(node, anim, actor, female, delta)) continue;
                LeaveSpot(actor);
                ShowProps(actor, "");
                if (FollowRoute(node, anim, actor, DoorPoint, delta)) actor.Exited = true;
                continue;
            }

            // ⑥ 제자리 동작(방해공작 · 건네주기) — 서 있는 그 자리에서.
            if (v.Action is CctvEmployeeAction.Suspicious or CctvEmployeeAction.Handoff or CctvEmployeeAction.Talking)
            {
                if (!StandUpFirst(node, anim, actor, female, delta)) continue;
                ShowProps(actor, "");
                ClearVisualOffset(node);
                anim?.SetAction(v.Action);
                anim?.Tick();
                continue;
            }

            // ⑦ 저장고 재고 정리 — 운반조는 상자 더미 ↔ 수레를 왕복한다(나머지는 선반 자리로).
            if (actor.Carrier >= 0 && _carts.Count > 0)
            {
                if (!StandUpFirst(node, anim, actor, female, delta)) continue;
                LeaveSpot(actor);
                ShowProps(actor, "");
                TickCarrier(v.Id, node, anim, actor, spots, female, delta);
                continue;
            }
            HideBox(actor);

            // ⑧ 작업 자리.
            var spot = PickSpot(spots, taskId, actor.Spot, used);
            if (spot == null)
            {
                // 자리가 모자란 경우(여섯 명 넘게) — 기본 슬롯에 서 있는다. 순간이동하지 않고 걸어간다.
                if (!StandUpFirst(node, anim, actor, female, delta)) continue;
                LeaveSpot(actor);
                ShowProps(actor, "");
                if (FollowRoute(node, anim, actor, Flat(v.FallbackPos), delta))
                {
                    ClearVisualOffset(node);
                    anim?.SetAction(CctvEmployeeAction.Idle);
                }
                continue;
            }

            // 다른 자리로 옮겨야 하는데 아직 앉아 있다 — 먼저 일어난다.
            if (actor.SeatState != SeatPhase.None && actor.Seat?.Spot != spot)
            {
                used[spot] = used.GetValueOrDefault(spot) + 1;
                StandUpFirst(node, anim, actor, female, delta);
                continue;
            }

            used[spot] = used.GetValueOrDefault(spot) + 1;
            if (actor.Spot != spot)
            {
                actor.Spot = spot; actor.Arrived = false; actor.HasRoute = false;
                if (actor.SeatState == SeatPhase.None) actor.Seat = null;   // 새 자리의 의자는 새로 잡는다
            }
            if (actor.SnapToSpot) { actor.SnapToSpot = false; SnapToSpot(node, anim, actor, spot, female); }
            MoveToSpot(node, anim, actor, spot, female, delta);
        }

        // 화면에서 사라진 직원은 자리를 놓아 준다.
        foreach (var (id, actor) in _actors)
        {
            bool still = false;
            foreach (var v in visible) if (v.Id == id) { still = true; break; }
            if (!still)
            {
                if (!actor.Hidden) Release(actor);
                actor.Hidden = true;
                // 다시 보일 때는 반응을 처음부터 하지 않고, 그동안 가 있었을 자리에 바로 둔다.
                if (actor.Ghost != null) actor.Ghost.NeedsPlace = true;
            }
        }

        EndStrapFrame();

        // 클립·위치를 다 정한 뒤 — 쓰지 않는 관절 되돌리기 · 시선 · 들고 있는 상자.
        foreach (var v in visible)
        {
            v.Anim?.Update(delta);
            if (_actors.TryGetValue(v.Id, out var a)) UpdateHeldBox(v.Node, a, delta);
        }
    }

    // ── 자리 선택 ────────────────────────────────────────────────────────

    private List<RoomWorkSpot> GetSpots(Node3D room, string roomId)
    {
        if (_spotCache.TryGetValue(roomId, out var cached)) return cached;
        var list = new List<RoomWorkSpot>();
        Collect(room, list);
        list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        _spotCache[roomId] = list;
        return list;
    }

    private static void Collect(Node n, List<RoomWorkSpot> o)
    {
        if (n is RoomWorkSpot s) o.Add(s);
        foreach (var c in n.GetChildren()) Collect(c, o);
    }

    // 저장고 운반 동선(상자 더미 · 수레 앞) — 서서 일하는 자리가 아니다.
    private static bool IsCarrierSpot(RoomWorkSpot s) =>
        s.AnimationName.StartsWith("pickup_box") || s.AnimationName.StartsWith("place_box");

    private static RoomWorkSpot PickSpot(List<RoomWorkSpot> spots, string taskId,
                                         RoomWorkSpot current, Dictionary<RoomWorkSpot, int> used)
    {
        // 기절한 직원(환자)은 눕는 자리만 쓴다 — 업무 목록과 무관하게 비어 있는 침대를 고른다.
        if (taskId == "__patient__")
        {
            if (current != null && IsLyingSpot(current) && !IsIsolationSpot(current)
                && used.GetValueOrDefault(current) < current.Capacity) return current;
            foreach (var s in spots)
                if (IsLyingSpot(s) && !IsIsolationSpot(s) && used.GetValueOrDefault(s) < s.Capacity)
                    return s;
            return null;
        }

        // 격리 직원은 격리 침대(AnimationName 이 isolated_ 로 시작하는 자리)만 쓴다.
        if (taskId == "__isolation__")
        {
            if (current != null && IsIsolationSpot(current) && used.GetValueOrDefault(current) < current.Capacity) return current;
            foreach (var s in spots)
                if (IsIsolationSpot(s) && used.GetValueOrDefault(s) < s.Capacity) return s;
            return null;
        }

        bool Free(RoomWorkSpot s) => !IsLyingSpot(s) && !IsCarrierSpot(s)
                                     && (s.SharedSpot || used.GetValueOrDefault(s) < s.Capacity);

        // 이미 쓰던 자리가 아직 비어 있으면 그대로(자리 사이를 왔다갔다 하지 않게).
        if (current != null && Free(current)) return current;

        // ① 지금 업무를 맡는 자리(SupportedTaskIds 에 있음)를 먼저, 우선순위 순으로.
        foreach (var s in spots)
            if (Free(s) && s.SupportedTaskIds.Length > 0 && s.Supports(taskId)) return s;
        // ② 나머지 자리 — 방마다 실제 설비에 붙은 작업 자리가 여섯 개 있다. 사람이 늘어도 누구도 빈손으로 서 있지 않다.
        foreach (var s in spots)
            if (Free(s)) return s;
        return null;
    }

    private static void Release(Actor a)
    {
        LeaveSpot(a);
        a.SeatState = SeatPhase.None;
        a.Seat = null;
        a.CartPhase = 0;
        a.CartTimer = 0f;
        a.BoxInHands = false;
        a.ReleaseT = -1f;
        HideBox(a);
        ShowProps(a, "");
        if (a.Iso != null && a.Iso.Phase < IsoPhase.Sit) a.Iso = null;
    }

    private static void LeaveSpot(Actor a)
    {
        a.Spot = null;
        a.Arrived = false;
        a.HasRoute = false;
    }

    // ── 자리로 이동 → (필요하면 앉기) → 작업 애니메이션 ──────────────────────

    // 서서 일하는 자리에서 실제로 설 곳 — 손이 InteractionTarget 에 닿도록 팔 길이만큼 뒤에.
    private static Vector3 StandPos(RoomWorkSpot spot, string clip, bool female)
    {
        var target = spot.TargetInParent();
        if (target == null || !WorkClipReach.TryGet(clip, female, out float reach, out _)) return Flat(spot.Position);
        var fwd = Flat(-spot.Transform.Basis.Z).Normalized();
        return Flat(target.Value) - fwd * reach;
    }

    private static Seat SeatOf(RoomWorkSpot spot) => new()
    {
        Pos = Flat(spot.Position), Yaw = spot.Rotation.Y, Height = spot.SeatHeight, Spot = spot,
    };

    private void MoveToSpot(Node3D node, EmployeeCctvAnimator anim, Actor actor, RoomWorkSpot spot, bool female, float delta)
    {
        if (IsLyingSpot(spot)) { MoveToLyingSpot(node, anim, actor, spot, delta, null); return; }
        string clip = anim?.BodyClip(spot.ClipFor(female), female) ?? spot.ClipFor(female);

        if (spot.IsSeated)
        {
            actor.Seat ??= SeatOf(spot);
            switch (actor.SeatState)
            {
                case SeatPhase.Seated:
                    node.Position = actor.Seat.Pos;
                    node.Rotation = new Vector3(0f, actor.Seat.Yaw, 0f);
                    ApplySeatOffset(node, spot);
                    anim?.PlayClip(clip);
                    ShowProps(actor, spot.HandProp);
                    return;
                case SeatPhase.Sitting:
                    TickSitting(node, anim, actor, clip);
                    return;
            }
            // 의자 앞까지 걸어가 → 몸을 돌리고 → 앉는다.
            if (!actor.Arrived)
            {
                if (!FollowRoute(node, anim, actor, actor.Seat.StandPoint, delta)) return;
                actor.Arrived = true;
            }
            node.Position = actor.Seat.StandPoint;
            ClearVisualOffset(node);
            if (!TurnTo(node, anim, actor.Seat.Yaw, delta)) return;
            actor.SeatState = SeatPhase.Sitting;
            actor.SeatT = 0f;
            actor.CreakPlayed = false;
            TickSitting(node, anim, actor, clip);
            return;
        }

        var goal = StandPos(spot, clip, female);
        if (!actor.Arrived)
        {
            if (!FollowRoute(node, anim, actor, goal, delta)) return;
            actor.Arrived = true;
        }
        node.Position = goal;
        ClearVisualOffset(node);
        // 설비 쪽으로 몸을 돌린 뒤 손을 댄다.
        if (!TurnTo(node, anim, spot.Rotation.Y, delta)) return;
        anim?.PlayClip(clip);
        ShowProps(actor, spot.HandProp);
    }

    // CCTV 를 돌려 다시 본 경우 — 그동안 이미 자리에서 일하고 있었다. 걸어오거나 다시 앉는 동작 없이 바로.
    private static void SnapToSpot(Node3D node, EmployeeCctvAnimator anim, Actor actor, RoomWorkSpot spot, bool female)
    {
        actor.Arrived = true;
        actor.HasRoute = false;
        if (IsLyingSpot(spot)) return;
        if (spot.IsSeated)
        {
            actor.Seat = SeatOf(spot);
            actor.SeatState = SeatPhase.Seated;
            node.Position = actor.Seat.Pos;
            node.Rotation = new Vector3(0f, actor.Seat.Yaw, 0f);
            return;
        }
        string clip = anim?.BodyClip(spot.ClipFor(female), female) ?? spot.ClipFor(female);
        node.Position = StandPos(spot, clip, female);
        node.Rotation = new Vector3(0f, spot.Rotation.Y, 0f);
    }

    // 의무실 병상(기절한 환자) — 예전 방식 그대로: 침대 발치까지 걸어가 누운 자세로.
    private void MoveToLyingSpot(Node3D node, EmployeeCctvAnimator anim, Actor actor, RoomWorkSpot spot,
                                 float delta, string clipOverride)
    {
        var goal = Flat(spot.Position);
        if (!actor.Arrived)
        {
            if (!FollowRoute(node, anim, actor, goal, delta)) return;
            actor.Arrived = true;
        }
        node.Position = goal;
        node.Rotation = spot.Rotation;
        ApplySeatOffset(node, spot, clipOverride);
        anim?.PlayClip(clipOverride ?? spot.AnimationName);
    }

    // 몸을 yaw 쪽으로 돌린다. 다 돌았으면 true.
    private static bool TurnTo(Node3D node, EmployeeCctvAnimator anim, float yaw, float delta)
    {
        float now = node.Rotation.Y;
        float diff = Mathf.AngleDifference(now, yaw);
        if (Mathf.Abs(diff) < 0.12f) { node.Rotation = new Vector3(0f, yaw, 0f); return true; }
        node.Rotation = new Vector3(0f, now + Mathf.Sign(diff) * Mathf.Min(Mathf.Abs(diff), TurnRate * delta), 0f);
        anim?.SetAction(CctvEmployeeAction.Idle);
        return false;
    }

    // 앉기/눕기 — 체형마다 골반 높이가 달라서 여기서 런타임에 보정한다.
    private static void ApplySeatOffset(Node3D node, RoomWorkSpot spot, string clipOverride = null)
    {
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return;
        if (!spot.IsSeated) { vr.Position = vr.Position with { Y = 0 }; return; }

        // 눕는 자리(lying_idle / isolated_*)는 RigRoot 를 90° 눕혀 몸이 수평이 된다.
        // 이때 골반이 이미 바닥 높이에 오므로 침대 높이를 그대로 쓴다.
        // 앉는 자리는 골반이 서 있는 높이 그대로라 그만큼 빼 준다.
        if (IsLyingClip(clipOverride ?? spot.AnimationName))
        {
            // 뼈는 몸 한가운데를 지난다 — 침대 높이를 그대로 쓰면 몸의 절반이 매트리스에 묻힌다.
            // 등이 매트리스 위에 놓이도록 몸 두께의 절반(가슴 깊이 0.205 의 절반)만큼 올린다.
            vr.Position = vr.Position with { Y = spot.SeatHeight + LyingBackLift };
            return;
        }
        vr.Position = vr.Position with { Y = spot.SeatHeight - HipY(node) };
    }

    private static float HipY(Node3D node) => node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips")?.Position.Y ?? 0.9f;

    // 누운 몸을 매트리스 위로 올리는 높이(몸 두께의 절반).
    private const float LyingBackLift = 0.12f;

    private static bool IsIsolationSpot(RoomWorkSpot s) =>
        s != null && s.AnimationName.StartsWith("isolated_");

    // 눕는 자리(의무실 병상 · 격리실 구속 침대). 평소 업무에는 쓰지 않는다.
    private static bool IsLyingSpot(RoomWorkSpot s) => s != null && IsLyingClip(s.AnimationName);

    private static bool IsLyingClip(string clip) =>
        !string.IsNullOrEmpty(clip) && (clip.StartsWith("lying") || clip.StartsWith("isolated_"));

    private static void ClearVisualOffset(Node3D node)
    {
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr != null && !Mathf.IsZeroApprox(vr.Position.Y)) vr.Position = vr.Position with { Y = 0 };
    }

    // 여성 베이스인지 — 이름이 아니라 실제 몸(가슴 파츠 유무)으로 판단한다.
    private static bool IsFemale(Node3D node) =>
        node.GetNodeOrNull("VisualRoot/RigRoot/Hips/Torso/Chest/BustMeshL") != null;

    // ── 양의 그날 후유증 ─────────────────────────────────────────────────

    private static void TickAftershock(Node3D node, EmployeeCctvAnimator anim, Actor actor, float delta)
    {
        if (anim == null) return;
        anim.SetAftershock(1f);
        // 몇 초에 한 번 괴물이 있던 쪽으로 고개가 돌아간다.
        actor.GlanceT += delta;
        float cycle = actor.GlanceT % 6.5f;
        if (actor.LastGhostSeen is { } g && cycle > 5.6f)
        {
            var d = g - Flat(node.Position);
            if (d.LengthSquared() > 0.01f)
                anim.SetLook(Mathf.AngleDifference(node.Rotation.Y, Mathf.Atan2(-d.X, -d.Z)), 0.9f, 10f);
        }
    }

    // ── 손에 드는 소품 ───────────────────────────────────────────────────

    private static void ShowProps(Actor a, string prop)
    {
        if (a.Clipboard != null && GodotObject.IsInstanceValid(a.Clipboard)) a.Clipboard.Visible = prop == "clipboard";
        if (a.Wrench != null && GodotObject.IsInstanceValid(a.Wrench)) a.Wrench.Visible = prop == "wrench";
        if (string.IsNullOrEmpty(prop)) return;
        // 처음 필요할 때 만든다.
        if (prop == "clipboard" && (a.Clipboard == null || !GodotObject.IsInstanceValid(a.Clipboard))) a.Clipboard = MakeClipboard(a);
        if (prop == "wrench" && (a.Wrench == null || !GodotObject.IsInstanceValid(a.Wrench))) a.Wrench = MakeWrench(a);
    }

    // 클립보드는 왼손(가슴 앞)에 — clipboard_check 클립이 왼손을 이 자리에 둔다.
    private static MeshInstance3D MakeClipboard(Actor a)
    {
        var hand = FindHand(a, "L");
        if (hand == null) return null;
        var board = new MeshInstance3D
        {
            Name = "Clipboard",
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.012f, 0.30f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.42f, 0.26f), Roughness = 0.85f },
        };
        var paper = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.19f, 0.004f, 0.25f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 0.86f), Roughness = 0.9f },
            Position = new Vector3(0f, 0.008f, 0.01f),
        };
        board.AddChild(paper);
        hand.AddChild(board);
        // 손바닥에 판의 한쪽 끝을 쥔다(손 노드 기준: 손가락 방향 -Y).
        board.Position = new Vector3(0.09f, -0.08f, -0.02f);
        board.RotationDegrees = new Vector3(90f, 0f, 0f);
        return board;
    }

    private static MeshInstance3D MakeWrench(Actor a)
    {
        var hand = FindHand(a, "R");
        if (hand == null) return null;
        var w = new MeshInstance3D
        {
            Name = "Wrench",
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.24f, 0.02f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.64f, 0.68f), Metallic = 0.7f, Roughness = 0.35f },
        };
        hand.AddChild(w);
        w.Position = new Vector3(0f, -0.14f, -0.03f);
        return w;
    }

    private static Node3D FindHand(Actor a, string side) =>
        a.OwnerNode?.GetNodeOrNull<Node3D>(side == "L"
            ? "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderL/UpperArmL/LowerArmL/HandL"
            : "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderR/UpperArmR/LowerArmR/HandR");
}
