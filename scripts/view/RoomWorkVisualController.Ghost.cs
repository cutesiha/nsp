using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 괴물을 마주친 직원의 CCTV 반응 — 같은 방 **안에서의** 표현 위치와 클립만 정한다.
//
// 고양이·강아지가 구석으로 도망가도 실제로 다른 방에 간 것이 아니다. 여기서 옮기는 것은
// 3D 노드의 Position 뿐이며 CurrentRoomId · AssignedRoomId · IsMoving · 시설 로그는 읽지도 쓰지도 않는다.
// 괴물이 사라지면 원래 업무 자리(Actor.Spot)로 다시 걸어간다 — 기존 MoveToSpot 이 그대로 처리한다.
public sealed partial class RoomWorkVisualController
{
    private const float GhostBodyRadius = 0.3f;     // 설비에서 이만큼은 떨어져 선다
    private const float GhostPersonalSpace = 0.62f; // 다른 직원과 이만큼은 떨어진다
    private const float GhostKeepAway = 0.95f;      // 숨는 자리·물러선 자리는 괴물에서 이만큼 떨어진다
    private const float SeatStandoffSpeed = 2.2f;   // 의자에서 튕겨 나오는 빠르기
    private const float GhostTooClose = 0.95f;      // 괴물이 배회하다 이보다 가까이 오면 그만큼 비켜선다

    // 한 직원의 반응 연출 기억. 반응 1회(GhostReactionTracker.State.Serial)마다 새로 만든다.
    private sealed class GhostVisual
    {
        public int Serial;
        public bool Initialized;
        public bool NeedsPlace;              // 화면 밖에 있다가 다시 보였다
        public bool LateJoin;                // 반응이 시작된 뒤에 화면에 들어왔다(첫 반응은 다시 안 튼다)
        public RoomWorkSpot Spot;            // 끝나면 돌아갈 업무 자리
        public Vector3 Anchor;               // 반응을 시작한 바닥(앉아 있었으면 의자 옆)
        public Vector3 Goal;                 // 최종 위치(숨는 자리 · 반 걸음 · 제자리)
        public readonly List<Move> Moves = new();
        public int MoveIndex;
        public int HideIndex = -1;
        public float RepickCooldown;
        public Vector3 LastGhost;
        public bool HasGhost;
        public float Yaw;
        public bool YawSet;
    }

    private struct Move
    {
        public Vector3 To;
        public float NotBefore;   // 반응 시작 후 이 시점부터 움직인다
        public float Speed;
        public bool FaceTravel;   // 가는 방향을 본다(도망) / false = 괴물을 본 채 움직인다
    }

    // 방 설비의 바닥 점유(표현 전용). 메시 AABB 를 바닥에 투영한 사각형들.
    private sealed class RoomClearance
    {
        public readonly List<Rect2> Boxes = new();
        public const float Half = 2.7f;   // 방은 6×6m — 벽 안쪽만 쓴다

        public float At(Vector3 p)
        {
            if (Mathf.Abs(p.X) > Half || Mathf.Abs(p.Z) > Half) return 0f;
            float best = 9f;
            foreach (var b in Boxes)
            {
                float dx = Mathf.Max(0f, Mathf.Max(b.Position.X - p.X, p.X - b.End.X));
                float dz = Mathf.Max(0f, Mathf.Max(b.Position.Y - p.Z, p.Z - b.End.Y));
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        // a→b 직선이 설비를 뚫지 않는가. 출발점 근처(작업대에 붙어 서 있던 자리)는 건너뛴다.
        public bool Clear(Vector3 a, Vector3 b, float r)
        {
            var d = Flat(b - a);
            float len = d.Length();
            for (float s = 0.4f; s < len - 0.15f; s += 0.15f)
                if (At(a + d / len * s) < r) return false;
            return true;
        }
    }

    private readonly Dictionary<string, RoomClearance> _clearance = new();
    private readonly Dictionary<string, List<Vector3>> _hideSpots = new();
    private readonly List<(string Id, Vector3 P)> _occupied = new();
    private RoomClearance _roomClear;
    private List<Vector3> _roomHide;

    // 숨으러 갈 때 돌아가는 길목(설비를 피해 꺾는 점). 방 가운데 쪽 격자.
    private static readonly Vector3[] Waypoints =
    {
        new(-1.5f, 0, -1.5f), new(-0.5f, 0, -1.5f), new(0.5f, 0, -1.5f), new(1.5f, 0, -1.5f),
        new(-1.5f, 0, -0.5f), new(-0.5f, 0, -0.5f), new(0.5f, 0, -0.5f), new(1.5f, 0, -0.5f),
        new(-1.5f, 0, 0.5f), new(-0.5f, 0, 0.5f), new(0.5f, 0, 0.5f), new(1.5f, 0, 0.5f),
        new(-1.5f, 0, 1.5f), new(-0.5f, 0, 1.5f), new(0.5f, 0, 1.5f), new(1.5f, 0, 1.5f),
    };

    private void BeginGhostFrame(Node3D room, string roomId,
                                 List<(string Id, Node3D Node, EmployeeCctvAnimator Anim,
                                       CctvEmployeeAction Action, Vector3 FallbackPos)> visible)
    {
        _roomClear = GetClearance(room, roomId);
        _roomHide = GetHideSpots(room, roomId);
        // 지금 서 있는 자리와, 이미 정해 둔 반응 목표 — 서로 같은 좌표를 차지하지 않게.
        _occupied.Clear();
        foreach (var v in visible)
        {
            _occupied.Add((v.Id, Flat(v.Node.Position)));
            if (_actors.TryGetValue(v.Id, out var a) && a.Ghost is { Initialized: true })
                _occupied.Add((v.Id, a.Ghost.Goal));
        }
    }

    // ── 반응 한 프레임 ────────────────────────────────────────────────────

    private void TickGhostReaction(string id, Node3D node, EmployeeCctvAnimator anim, Vector3 fallbackPos,
                                   Actor actor, GhostReactionTracker.State gs, GhostReactionProfile prof,
                                   Vector3? ghostPos, List<RoomWorkSpot> spots, string taskId,
                                   Dictionary<RoomWorkSpot, int> used, float delta)
    {
        var g = actor.Ghost;
        if (g == null || g.Serial != gs.Serial) actor.Ghost = g = new GhostVisual { Serial = gs.Serial };
        if (ghostPos.HasValue) { g.LastGhost = Flat(ghostPos.Value); g.HasGhost = true; }
        Vector3 ghost = g.HasGhost ? g.LastGhost : Vector3.Zero;
        bool reacting = gs.Stage == GhostReactionTracker.Stage.Reacting;

        // 상자를 안고 있었다면 내려놓은 것으로 둔다(저장고 수레 연출과 겹치지 않게).
        if (actor.BoxProp != null) actor.BoxProp.Visible = false;
        actor.CartPhase = 0;

        if (!g.Initialized) InitGhostReaction(id, node, fallbackPos, actor, g, gs, prof, ghost, spots, taskId, used);

        // 돌아갈 업무 자리는 계속 잡아 둔다 — 반응이 끝나면 그 자리로 걸어간다.
        if (g.Spot != null) used[g.Spot] = used.GetValueOrDefault(g.Spot) + 1;
        actor.Spot = g.Spot;
        actor.Arrived = false;

        // 화면 밖에 있던 동안 이미 도착했을 자리로 바로 옮긴다.
        if (g.NeedsPlace)
        {
            g.NeedsPlace = false;
            g.LateJoin = true;
            node.Position = g.Goal;
            g.MoveIndex = g.Moves.Count;
            g.YawSet = false;
        }

        // ── 같은 방 안의 표현 이동 ──
        bool moving = false;
        var travel = Vector3.Zero;
        bool faceTravel = false;
        float moveTime = 0f;
        if (reacting && g.MoveIndex < g.Moves.Count)
        {
            var m = g.Moves[g.MoveIndex];
            if (gs.Elapsed >= m.NotBefore)
            {
                var here = Flat(node.Position);
                var d = m.To - here;
                float dist = d.Length();
                if (dist <= ArriveDistance) { node.Position = m.To; g.MoveIndex++; }
                else
                {
                    travel = d / dist;
                    node.Position = here + travel * Mathf.Min(m.Speed * delta, dist);
                    moving = true;
                    faceTravel = m.FaceTravel;
                    moveTime = gs.Elapsed - m.NotBefore;
                }
            }
        }
        bool fleeing = moving && faceTravel;

        // 숨은 자리 쪽으로 괴물이 다가오면 한 번 더 자리를 옮긴다(고양이·강아지).
        g.RepickCooldown -= delta;
        if (reacting && prof.Move == GhostMove.Hide && !moving && g.MoveIndex >= g.Moves.Count
            && g.HasGhost && g.RepickCooldown <= 0f && Flat(node.Position).DistanceTo(ghost) < 1.15f)
        {
            g.RepickCooldown = 4f;
            var here = Flat(node.Position);
            var path = PickHide(id, here, ghost, g.HideIndex, out int idx);
            if (path != null && path[^1].DistanceTo(ghost) > here.DistanceTo(ghost) + 0.8f)
            {
                g.HideIndex = idx;
                g.Goal = path[^1];
                _occupied.Add((id, g.Goal));
                foreach (var p in path)
                    g.Moves.Add(new Move { To = p, NotBefore = 0f, Speed = prof.MoveSpeed, FaceTravel = true });
            }
        }

        // 괴물이 배회하다 몸 위로 걸어 들어오면(괴물 이동은 괴물 쪽 연출이다) 겹치지 않게 조금씩 비켜선다.
        // 숨는 사람은 위에서 자리를 다시 고르고, 나머지(양·토끼·늑대)는 그 자리에서 뒤로 밀려난다.
        if (reacting && !moving && prof.Move != GhostMove.Hide && ghostPos.HasValue)
        {
            var here = Flat(node.Position);
            var d = here - ghost;
            float dist = d.Length();
            if (dist < GhostTooClose)
            {
                var away = dist > 1e-3f ? d / dist : new Vector3(0.7f, 0, 0.7f);
                float speed = prof.Move == GhostMove.InPlace ? 0.6f : 1.1f;   // 주저앉은 양은 느리게
                var p = here + away * Mathf.Min(speed * delta, GhostTooClose - dist);
                if (_roomClear.At(p) >= GhostBodyRadius * 0.8f && SpotOk(id, p, ghost, keepAway: 0f))
                {
                    node.Position = p;
                    g.Goal = p;
                }
            }
        }

        // ── 클립 ──
        if (!reacting)
            anim?.PlayClip(prof.RecoverClip, 0.2, gs.RecoverElapsed);
        else if (!g.LateJoin && gs.Elapsed < prof.IntroSeconds && !string.IsNullOrEmpty(prof.IntroClip))
            anim?.PlayClip(prof.IntroClip, prof.IntroBlend, gs.Elapsed);
        else if (fleeing && !string.IsNullOrEmpty(prof.MoveClip))
            anim?.PlayClip(prof.MoveClip, 0.1);
        else
            // 첫 반응에서 이어질 때는 처음부터(마지막 자세 = 반복 첫 자세), 늦게 봤으면 직원마다 다른 위상.
            anim?.PlayClip(prof.LoopClip, 0.14, g.LateJoin ? -1f : 0f);

        // ── 몸 방향 ──
        float toGhost = YawTo(Flat(node.Position), ghost, g.Yaw);
        if (!g.YawSet) { g.Yaw = g.LateJoin && prof.FaceRate > 0f ? toGhost : node.Rotation.Y; g.YawSet = true; }
        if (fleeing)
            g.Yaw = Mathf.LerpAngle(g.Yaw, Mathf.Atan2(-travel.X, -travel.Z), 1f - Mathf.Exp(-14f * delta));
        else if (reacting && prof.FaceRate > 0f)
            g.Yaw = Mathf.LerpAngle(g.Yaw, toGhost, 1f - Mathf.Exp(-prof.FaceRate * delta));
        // 회복 중에는 방향을 그대로 둔다 — 괴물이 있던 자리를 잠시 더 본다.
        node.Rotation = new Vector3(0f, g.Yaw, 0f);

        // ── 시선(Neck) — 고개로 괴물을 좇는다 ──
        float look = 0f;
        if (reacting)
        {
            if (fleeing)
                look = prof.MoveClip == "ghost_dog_flee"
                    // 강아지는 도망치다 두 번 뒤돌아본다. 고양이는 뛰면서도 눈을 떼지 않는다.
                    ? (moveTime is > 0.16f and < 0.42f or > 0.66f and < 0.9f ? 1f : 0f)
                    : 1f;
            else if (g.LateJoin || gs.Elapsed >= prof.IntroSeconds)
                look = prof.LookWeight;
        }
        anim?.SetLook(Mathf.AngleDifference(g.Yaw, toGhost), look, fleeing ? 16f : 9f);
    }

    private void InitGhostReaction(string id, Node3D node, Vector3 fallbackPos, Actor actor, GhostVisual g,
                                   GhostReactionTracker.State gs, GhostReactionProfile prof, Vector3 ghost,
                                   List<RoomWorkSpot> spots, string taskId, Dictionary<RoomWorkSpot, int> used)
    {
        g.Initialized = true;
        // 반응이 막 시작됐을 때 보고 있었는가 — 아니면 첫 반응은 건너뛰고 지금 상태부터 보여준다.
        bool fresh = gs.Stage == GhostReactionTracker.Stage.Reacting && gs.Elapsed < 0.3f;
        g.LateJoin = !fresh;

        g.Spot = actor.Spot ?? PickSpot(spots, taskId, null, used);
        Vector3 start;
        bool seated;
        if (fresh)
        {
            start = Flat(node.Position);
            seated = actor.Spot is { IsSeated: true } && actor.Arrived;
        }
        else
        {
            start = g.Spot != null ? Flat(g.Spot.Position) : Flat(fallbackPos);
            seated = g.Spot is { IsSeated: true };
        }

        // 앉아 있던 사람은 의자 옆 바닥으로 튕겨 나온 뒤 반응한다 — 의자·책상 속에서 주저앉지 않게.
        g.Anchor = seated ? Standoff(id, g.Spot, ghost) : start;
        if (fresh && seated)
            g.Moves.Add(new Move { To = g.Anchor, NotBefore = 0f, Speed = SeatStandoffSpeed });

        g.Goal = g.Anchor;
        switch (prof.Move)
        {
            case GhostMove.InPlace when prof.BackOffBelow > 0f:
            {
                // 괴물이 코앞에 나타났다 — 움찔하며 조금 물러난 자리에서 주저앉는다(양).
                var d = Flat(g.Anchor - ghost);
                float dist = d.Length();
                if (dist >= prof.BackOffBelow) break;
                var away = dist > 1e-3f ? d / dist : new Vector3(0.7f, 0, 0.7f);
                float back = Mathf.Min(0.6f, prof.BackOffBelow - dist);
                foreach (float k in new[] { 1f, 0.6f })
                {
                    var p = g.Anchor + away * back * k;
                    if (SpotOk(id, p, ghost, keepAway: 0.5f)) { g.Goal = p; break; }
                }
                break;
            }
            case GhostMove.StepBack:
            {
                var dir = Flat(g.Anchor - ghost);
                dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : new Vector3(0.7f, 0, 0.7f);
                foreach (float k in new[] { 1f, 0.55f })
                {
                    var p = g.Anchor + dir * prof.StepMeters * k;
                    if (SpotOk(id, p, ghost)) { g.Goal = p; break; }
                }
                break;
            }
            case GhostMove.StepToward:
            {
                var d = Flat(ghost - g.Anchor);
                float dist = d.Length();
                float step = Mathf.Min(prof.StepMeters, dist - prof.KeepFromGhost);
                if (dist > 1e-3f && step >= 0.12f)
                    foreach (float k in new[] { 1f, 0.55f })
                    {
                        var p = g.Anchor + d / dist * step * k;
                        if (SpotOk(id, p, ghost, keepAway: prof.KeepFromGhost - 0.05f)) { g.Goal = p; break; }
                    }
                break;
            }
            case GhostMove.Hide:
            {
                var path = PickHide(id, g.Anchor, ghost, -1, out int idx);
                if (path != null) { g.HideIndex = idx; g.Goal = path[^1]; }
                // 숨을 곳이 없으면(좁은 방) 그 자리에서 웅크린다.
                if (path != null)
                    foreach (var p in path)
                        g.Moves.Add(new Move { To = p, NotBefore = prof.MoveAt, Speed = prof.MoveSpeed, FaceTravel = true });
                break;
            }
        }
        if (prof.Move is GhostMove.StepBack or GhostMove.StepToward or GhostMove.InPlace && g.Goal != g.Anchor)
            g.Moves.Add(new Move
            {
                To = g.Goal, NotBefore = prof.MoveAt,
                Speed = g.Anchor.DistanceTo(g.Goal) / Mathf.Max(0.1f, prof.StepSeconds),
            });
        _occupied.Add((id, g.Goal));

        // 늦게 봤으면 이미 가 있었을 자리에 바로 둔다.
        if (g.LateJoin)
        {
            node.Position = g.Goal;
            g.MoveIndex = g.Moves.Count;
        }
    }

    // ── 여우 — 자리·클립은 그대로(기존 업무 처리), 손만 잠깐 멈추고 고개만 돌린다 ─────────

    private void FoxOverlay(string id, Node3D node, EmployeeCctvAnimator anim, Actor actor,
                            GhostReactionTracker.State gs, Vector3? ghostPos)
    {
        var g = actor.Ghost;
        if (g == null || g.Serial != gs.Serial) actor.Ghost = g = new GhostVisual { Serial = gs.Serial, Initialized = true };
        if (ghostPos.HasValue) { g.LastGhost = Flat(ghostPos.Value); g.HasGhost = true; }
        // 화면을 돌렸다 와도 여우는 그 사이 계속 일하고 있었다 — 입구 슬롯에서 다시 걸어오지 않는다.
        if (g.NeedsPlace) { g.NeedsPlace = false; actor.SnapToSpot = true; }
        if (!g.HasGhost || anim == null) return;

        float look = 0f, speed = 1f;
        float t = gs.Elapsed;
        if (gs.Stage == GhostReactionTracker.Stage.Reacting)
        {
            // "...뭐야 저건." — 손이 0.3초 멈추고 고개가 홱 돌아간다. 몸은 그대로.
            if (t >= GhostReactionProfiles.FoxFreezeFrom && t < GhostReactionProfiles.FoxFreezeUntil) speed = 0f;
            if (t < GhostReactionProfiles.FoxNoticeLookUntil) look = 1f;
            else if (FoxGlancing(gs.Serial, id, t)) { look = 1f; speed = GhostReactionProfiles.FoxGlanceWorkSpeed; }
        }
        else if (gs.RecoverElapsed is > 0.05f and < 0.45f) look = 1f;   // 사라진 자리를 한 번 확인

        // 걸어가는 중이면 다리를 멈추지 않는다(미끄러져 보인다).
        if (anim.CurrentClip != "walk") anim.SetSpeedFactor(speed);
        float yaw = node.Rotation.Y;
        anim.SetLook(Mathf.AngleDifference(yaw, YawTo(Flat(node.Position), g.LastGhost, yaw)), look, 18f);
    }

    // 1.5~2.5초마다 0.3~0.5초씩 — 간격은 반응마다 다르지만 같은 반응 안에서는 고정이다.
    private static bool FoxGlancing(int serial, string id, float t)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(serial * 7919 + id.GetHashCode() & 0x7fffffff) };
        float at = GhostReactionProfiles.FoxNoticeLookUntil;
        for (int i = 0; i < 64; i++)
        {
            at += rng.RandfRange(GhostReactionProfiles.FoxGlanceGapMin, GhostReactionProfiles.FoxGlanceGapMax);
            float dur = rng.RandfRange(GhostReactionProfiles.FoxGlanceMin, GhostReactionProfiles.FoxGlanceMax);
            if (t < at) return false;
            if (t < at + dur) return true;
            at += dur;
        }
        return false;
    }

    // ── 자리 고르기 ───────────────────────────────────────────────────────

    // 설비에서 떨어져 있고, 다른 직원·괴물과 겹치지 않는 바닥인가.
    private bool SpotOk(string id, Vector3 p, Vector3 ghost, float keepAway = GhostKeepAway)
    {
        if (_roomClear == null || _roomClear.At(p) < GhostBodyRadius) return false;
        if (p.DistanceTo(ghost) < keepAway) return false;
        foreach (var (other, q) in _occupied)
            if (other != id && p.DistanceTo(q) < GhostPersonalSpace) return false;
        return true;
    }

    // 괴물에서 가장 먼 숨는 자리까지의 길(마지막 점 = 숨는 자리). 없으면 null.
    private List<Vector3> PickHide(string id, Vector3 from, Vector3 ghost, int exclude, out int index)
    {
        index = -1;
        List<Vector3> best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < (_roomHide?.Count ?? 0); i++)
        {
            if (i == exclude) continue;
            var h = _roomHide[i];
            if (!SpotOk(id, h, ghost, keepAway: 1.6f)) continue;
            var path = PathTo(from, h, out float len);
            if (path == null) continue;
            float score = h.DistanceTo(ghost) - 0.2f * len;
            if (score > bestScore) { bestScore = score; best = path; index = i; }
        }
        return best;
    }

    // 직선으로 가면 설비를 뚫는 경우 길목 하나를 거쳐 꺾어 간다.
    // 작업대 사이처럼 좁은 자리에서는 먼저 빈 쪽으로 한 걸음 빠져나온 뒤 길을 찾는다.
    private List<Vector3> PathTo(Vector3 from, Vector3 to, out float len)
    {
        var path = PathVia(from, to, out len);
        if (path != null) return path;

        List<Vector3> best = null;
        float bestLen = float.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            var e = from + new Vector3(Mathf.Cos(k * Mathf.Pi / 4f), 0f, Mathf.Sin(k * Mathf.Pi / 4f)) * 0.55f;
            if (_roomClear.At(e) < 0.25f) continue;
            var rest = PathVia(e, to, out float l);
            if (rest == null || l + 0.55f >= bestLen) continue;
            bestLen = l + 0.55f;
            best = new List<Vector3> { e };
            best.AddRange(rest);
        }
        len = bestLen;
        return best;
    }

    private List<Vector3> PathVia(Vector3 from, Vector3 to, out float len)
    {
        len = from.DistanceTo(to);
        if (_roomClear.Clear(from, to, 0.2f)) return new List<Vector3> { to };
        List<Vector3> best = null;
        float bestLen = float.MaxValue;
        foreach (var w in Waypoints)
        {
            if (_roomClear.At(w) < 0.4f) continue;
            float l = from.DistanceTo(w) + w.DistanceTo(to);
            if (l >= bestLen || !_roomClear.Clear(from, w, 0.2f) || !_roomClear.Clear(w, to, 0.2f)) continue;
            bestLen = l;
            best = new List<Vector3> { w, to };
        }
        if (best != null) len = bestLen;
        return best;
    }

    // 앉아 있던 자리 옆의 빈 바닥 — 의자 뒤쪽을 먼저, 괴물 반대쪽을 조금 더 좋게 본다.
    private Vector3 Standoff(string id, RoomWorkSpot seat, Vector3 ghost)
    {
        var at = Flat(seat.Position);
        var back = Flat(seat.Transform.Basis.Z).Normalized();
        Vector3 best = at;
        float bestScore = float.MinValue;
        foreach (float r in new[] { 0.6f, 0.8f })
            for (int k = 0; k < 8; k++)
            {
                var dir = back.Rotated(Vector3.Up, k * Mathf.Pi / 4f);
                var p = at + dir * r;
                if (!SpotOk(id, p, ghost, keepAway: 0.7f)) continue;
                float score = dir.Dot(back) * 1.5f + p.DistanceTo(ghost) * 0.4f - r;
                if (score > bestScore) { bestScore = score; best = p; }
            }
        return best;
    }

    private RoomClearance GetClearance(Node3D room, string roomId)
    {
        if (_clearance.TryGetValue(roomId, out var c)) return c;
        c = new RoomClearance();
        var inv = room.GlobalTransform.AffineInverse();
        foreach (var n in room.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || !mi.Visible) continue;
            var box = (inv * mi.GlobalTransform) * mi.GetAabb();
            // 바닥·천장·조명과 방 껍데기(바닥판)는 제외 — 사람이 부딪히는 높이의 물건만.
            if (box.End.Y < 0.05f || box.Position.Y > 1.8f) continue;
            if (box.Size.X > 5.5f && box.Size.Z > 5.5f) continue;
            c.Boxes.Add(new Rect2(box.Position.X, box.Position.Z, box.Size.X, box.Size.Z));
        }
        _clearance[roomId] = c;
        return c;
    }

    // 각 방 씬의 GhostHideSpots 밑 마커(표현 전용). CCTV 에 보이는 빈 구석에 놓아 두었다.
    private List<Vector3> GetHideSpots(Node3D room, string roomId)
    {
        if (_hideSpots.TryGetValue(roomId, out var list)) return list;
        list = new List<Vector3>();
        var parent = room.GetNodeOrNull<Node3D>("GhostHideSpots");
        if (parent != null)
            foreach (var n in parent.GetChildren())
                if (n is Node3D m) list.Add(Flat(room.ToLocal(m.GlobalPosition)));
        _hideSpots[roomId] = list;
        return list;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    // from 에서 to 를 바라보는 몸 방향(캐릭터는 자기 -Z 를 본다). 같은 점이면 keep.
    private static float YawTo(Vector3 from, Vector3 to, float keep)
    {
        var d = to - from;
        return d.LengthSquared() < 1e-4f ? keep : Mathf.Atan2(-d.X, -d.Z);
    }
}
