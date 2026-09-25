using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 격리실 — 침대에 실제로 눕고, 다 누운 뒤에야 압박 밴드가 채워지고, 그 뒤에야 발버둥친다. **표현 전용이다.**
//
//   도착 → 침대 옆까지 걷기 → 침대 쪽을 등지고 돌기 → 가장자리에 앉기(chair_sit)
//   → 등을 대고 눕기(bed_lie_down — 몸을 돌리며 다리를 올린다) → 잠깐 정지
//   → X 밴드 둘 · 허리 · 다리 밴드 · 버클 체결 → isolated_struggle(밴드에 막힌 발버둥) → 지쳐 늘어짐
// 해제는 역순: 밴드 풀림 → 상체를 일으켜 앉기(bed_get_up) → 일어나기(chair_stand) → 출입구로.
//
// 밴드는 방 씬에 이미 있는 RestraintBed{n}StrapX0/X1/Waist/Leg · Buckle0~2 를 그대로 쓴다.
// 비어 있는 침대에서는 밴드가 보이지 않는다. 채워질 때는 누운 사람의 체형에 맞춰 몸 위에 얹는다.
public sealed partial class RoomWorkVisualController
{
    private enum IsoPhase { WalkSide, Turn, Sit, Lie, Hold, Strap, Struggle, Unstrap, GetUp, StandUp, Done }

    private sealed class IsoState
    {
        public bool Active = true;
        public IsoPhase Phase;
        public float T;               // 이번 단계 경과
        public float Struggled;       // 발버둥 누적(탈진 전환)
        public RoomWorkSpot Bed;
        public Seat Edge;             // 침대 가장자리(앉는 자리)
        public int ClicksPlayed;
    }

    private const float LieSeconds = 1.6f;      // bed_lie_down
    private const float GetUpSeconds = 1.5f;    // bed_get_up
    private const float HoldSeconds = 0.25f;    // 다 누운 뒤 밴드 전 정지
    private const float StrapSeconds = 1.0f;
    private const float UnstrapSeconds = 0.7f;
    private const float EdgeInset = 0.30f;      // 침대 가운데에서 가장자리 좌판까지

    // ── 침대 한 개의 밴드 ──
    private sealed class BedStraps
    {
        public readonly List<(MeshInstance3D Node, Transform3D Orig, int Group)> Parts = new();
        public float Closed;          // 0 풀림(안 보임) ~ 1 체결
        public bool Claimed;          // 이번 프레임에 누가 이 침대를 쓰는가
        public Vector3 BodyHips;      // 체결 위치 기준(누운 사람 골반)
        public Vector3 HeadDir, Side;
        public bool Female;
    }

    private readonly Dictionary<RoomWorkSpot, BedStraps> _straps = new();
    private Node3D _strapRoom;

    private void BeginStrapFrame(Node3D room, string roomId)
    {
        _strapRoom = roomId == "isolation_room" ? room : null;
        // 격리실의 모든 침대를 등록해 둔다 — 아무도 없는 침대의 밴드도 걷어야 하므로.
        if (_strapRoom != null)
            foreach (var s in GetSpots(room, roomId))
                if (IsIsolationSpot(s)) StrapsOf(s);
        foreach (var (_, b) in _straps) b.Claimed = false;
    }

    // 아무도 없는 침대는 밴드를 걷어 둔다.
    private void EndStrapFrame()
    {
        foreach (var (_, b) in _straps)
        {
            if (!b.Claimed) b.Closed = 0f;
            ApplyStraps(b);
        }
    }

    private BedStraps StrapsOf(RoomWorkSpot bed)
    {
        if (_straps.TryGetValue(bed, out var b)) return b;
        b = new BedStraps();
        _straps[bed] = b;
        if (_strapRoom == null) return b;
        string n = bed.Id[^1..];   // IsolationBedSpot1 → "1"
        void Take(string name, int group)
        {
            if (_strapRoom.GetNodeOrNull<MeshInstance3D>($"RestraintBed{n}{name}") is { } m) b.Parts.Add((m, m.Transform, group));
        }
        Take("StrapX0", 0); Take("StrapX1", 0); Take("StrapWaist", 1); Take("StrapLeg", 2);
        Take("Buckle0", 3); Take("Buckle1", 3); Take("Buckle2", 3);
        return b;
    }

    // 체결 정도(0~1)에 따라 밴드를 침대 옆에서 몸 위로 끌어온다. 버클은 끝에서 딸깍.
    private static void ApplyStraps(BedStraps b)
    {
        foreach (var (node, rest, group) in b.Parts)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            // 그룹마다 시작이 다르다: X 밴드 먼저 → 허리 → 다리 → 버클.
            float from = group switch { 0 => 0f, 1 => 0.35f, 2 => 0.55f, _ => 0.86f };
            float to = group switch { 0 => 0.45f, 1 => 0.7f, 2 => 0.88f, _ => 1f };
            float k = Mathf.Clamp((b.Closed - from) / (to - from), 0f, 1f);
            k = k * k * (3f - 2f * k);
            node.Visible = k > 0.001f;
            if (!node.Visible) continue;
            var fastened = FastenedTransform(b, rest, group, node.Name.ToString());
            // 풀린 자리: 침대 옆 아래로 접혀 있다.
            var open = fastened;
            open.Origin = fastened.Origin + b.Side * 0.55f + Vector3.Down * 0.3f;
            open.Basis = fastened.Basis.Scaled(new Vector3(0.25f, 1f, 1f));
            node.Transform = open.InterpolateWith(fastened, k);
        }
    }

    // 누운 사람 몸 위에 얹는다(원래 씬의 회전·폭은 그대로, 높이·앞뒤만 체형에 맞춘다).
    private static Transform3D FastenedTransform(BedStraps b, Transform3D rest, int group, string name)
    {
        if (b.BodyHips == Vector3.Zero) return rest;
        bool f = b.Female;
        // 골반 기준 머리 쪽 거리 · 몸 위 높이(가슴 · 허리 · 넓적다리).
        (float along, float up) = group switch
        {
            0 => (f ? 0.23f : 0.29f, f ? 0.10f : 0.115f),
            1 => (0.02f, f ? 0.085f : 0.095f),
            2 => (f ? -0.30f : -0.38f, f ? 0.065f : 0.075f),
            _ => (name.EndsWith("0") ? (f ? 0.23f : 0.29f) : name.EndsWith("1") ? 0.02f : (f ? -0.30f : -0.38f),
                  (name.EndsWith("0") ? (f ? 0.10f : 0.115f) : name.EndsWith("1") ? (f ? 0.085f : 0.095f) : (f ? 0.065f : 0.075f)) + 0.03f),
        };
        var t = rest;
        var p = b.BodyHips + b.HeadDir * along;
        t.Origin = new Vector3(p.X, b.BodyHips.Y + up, p.Z);
        return t;
    }

    // ── 격리 한 프레임. 처리했으면 true(다른 동작으로 넘어가지 않는다) ──
    private bool TickIsolation(Node3D node, EmployeeCctvAnimator anim, Actor actor, List<RoomWorkSpot> spots,
                               Dictionary<RoomWorkSpot, int> used, bool female, bool releasing, float delta)
    {
        // 격리실이 아닌 방이면(떠나는 중) 여기서 할 일이 없다.
        if (_strapRoom == null) { actor.Iso = null; return false; }

        var iso = actor.Iso;
        if (iso == null)
        {
            if (releasing) return false;
            var bed = PickSpot(spots, "__isolation__", actor.Spot, used);
            if (bed == null) return false;
            iso = actor.Iso = new IsoState { Bed = bed, Phase = IsoPhase.WalkSide };
            actor.Spot = bed;
            actor.HasRoute = false;
        }
        if (iso.Bed == null)
        {
            // 다시 본 격리실 — 이미 누워 있던 침대를 잡는다.
            iso.Bed = PickSpot(spots, "__isolation__", actor.Spot, used);
            if (iso.Bed == null) { actor.Iso = null; return false; }
            actor.Spot = iso.Bed;
        }
        var b = iso.Bed;
        used[b] = used.GetValueOrDefault(b) + 1;
        var straps = StrapsOf(b);
        straps.Claimed = true;
        straps.Female = female;

        // 침대 기하 — 머리 방향 · 가장자리 · 누운 골반 위치.
        float hipY = HipY(node);
        float lieYaw = b.Rotation.Y;
        var headDir = new Vector3(Mathf.Sin(lieYaw), 0f, Mathf.Cos(lieYaw));
        var side = headDir.Cross(Vector3.Up).Normalized();
        var lieHips = Flat(b.Position) + headDir * hipY + new Vector3(0f, b.SeatHeight + LyingBackLift, 0f);
        iso.Edge ??= new Seat
        {
            Pos = Flat(lieHips) + side * EdgeInset, Yaw = Mathf.Atan2(-side.X, -side.Z), Height = b.SeatHeight,
        };
        straps.BodyHips = lieHips;
        straps.HeadDir = headDir;
        straps.Side = side;

        // 풀려나는 중 — 단계에 따라 역순으로.
        if (releasing && iso.Phase < IsoPhase.Unstrap)
        {
            switch (iso.Phase)
            {
                case IsoPhase.WalkSide or IsoPhase.Turn:
                    actor.Iso = null; actor.Spot = null; return false;
                case IsoPhase.Sit:
                    iso.Phase = IsoPhase.StandUp; break;
                case IsoPhase.Lie:
                    iso.Phase = IsoPhase.GetUp; iso.T = (1f - Mathf.Clamp(iso.T / LieSeconds, 0f, 1f)) * GetUpSeconds; break;
                default:
                    iso.Phase = IsoPhase.Unstrap; iso.T = (1f - straps.Closed) * UnstrapSeconds; break;
            }
        }

        iso.T += delta;
        switch (iso.Phase)
        {
            case IsoPhase.WalkSide:
                straps.Closed = 0f;
                if (!FollowRoute(node, anim, actor, iso.Edge.StandPoint, delta)) return true;
                iso.Phase = IsoPhase.Turn; iso.T = 0f;
                return true;

            case IsoPhase.Turn:
                node.Position = iso.Edge.StandPoint;
                if (!TurnTo(node, anim, iso.Edge.Yaw, delta)) return true;
                iso.Phase = IsoPhase.Sit; iso.T = 0f;
                actor.Seat = iso.Edge; actor.SeatState = SeatPhase.Sitting; actor.SeatT = 0f; actor.CreakPlayed = false;
                return true;

            case IsoPhase.Sit:
                TickSitting(node, anim, actor, "chair_sit");
                if (actor.SeatState == SeatPhase.Seated) { iso.Phase = IsoPhase.Lie; iso.T = 0f; }
                return true;

            case IsoPhase.Lie:
            {
                float u = Mathf.Clamp(iso.T / LieSeconds, 0f, 1f);
                PoseLying(node, anim, iso, lieHips, lieYaw, hipY, Smooth(u));
                anim?.PlayClip("bed_lie_down", 0.1);
                if (u >= 1f) { iso.Phase = IsoPhase.Hold; iso.T = 0f; }
                return true;
            }

            case IsoPhase.Hold:
            case IsoPhase.Strap:
            case IsoPhase.Struggle:
            {
                PoseLying(node, anim, iso, lieHips, lieYaw, hipY, 1f);
                if (iso.Phase == IsoPhase.Hold)
                {
                    anim?.PlayClip("lying_idle", 0.15);
                    if (iso.T >= HoldSeconds) { iso.Phase = IsoPhase.Strap; iso.T = 0f; iso.ClicksPlayed = 0; }
                }
                else if (iso.Phase == IsoPhase.Strap)
                {
                    anim?.PlayClip("lying_idle", 0.15);
                    straps.Closed = Mathf.Clamp(iso.T / StrapSeconds, 0f, 1f);
                    // 밴드마다 한 번씩 체결음.
                    int clicks = straps.Closed >= 0.88f ? 3 : straps.Closed >= 0.7f ? 2 : straps.Closed >= 0.45f ? 1 : 0;
                    while (iso.ClicksPlayed < clicks) { iso.ClicksPlayed++; NSP.Core.Sfx.Instance?.Play("relay_click", -10f, 0.8f + 0.1f * iso.ClicksPlayed); }
                    if (iso.T >= StrapSeconds) { iso.Phase = IsoPhase.Struggle; iso.T = 0f; }
                }
                else
                {
                    straps.Closed = 1f;
                    iso.Struggled += delta;
                    anim?.PlayClip(iso.Struggled < IsolationStruggleSeconds ? "isolated_struggle" : "isolated_exhausted", 0.3);
                }
                return true;
            }

            case IsoPhase.Unstrap:
                PoseLying(node, anim, iso, lieHips, lieYaw, hipY, 1f);
                anim?.PlayClip("lying_idle", 0.3);
                straps.Closed = Mathf.Clamp(1f - iso.T / UnstrapSeconds, 0f, 1f);
                if (iso.T >= UnstrapSeconds) { iso.Phase = IsoPhase.GetUp; iso.T = 0f; }
                return true;

            case IsoPhase.GetUp:
            {
                straps.Closed = 0f;
                float u = Mathf.Clamp(iso.T / GetUpSeconds, 0f, 1f);
                PoseLying(node, anim, iso, lieHips, lieYaw, hipY, Smooth(1f - u));
                anim?.PlayClip("bed_get_up", 0.1);
                if (u >= 1f)
                {
                    iso.Phase = IsoPhase.StandUp; iso.T = 0f;
                    actor.Seat = iso.Edge; actor.SeatState = SeatPhase.Seated;
                    var rr = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot");
                    if (rr != null) rr.Rotation = Vector3.Zero;
                }
                return true;
            }

            case IsoPhase.StandUp:
                straps.Closed = 0f;
                actor.Seat = iso.Edge;
                if (actor.SeatState == SeatPhase.None) actor.SeatState = SeatPhase.Seated;
                if (!StandUpFirst(node, anim, actor, female, delta)) return true;
                actor.Iso = null;
                actor.Spot = null;
                return false;   // 이제 걸어 나간다(떠나는 처리가 이어받는다)
        }
        return true;
    }

    // 앉은 자세(e=0) ↔ 누운 자세(e=1) 사이. 골반을 가장자리에서 침대 가운데로 옮기면서
    // 몸을 눕히고(RigRoot) 머리가 베개 쪽으로 가게 돌린다. 골반이 정해진 길을 따라가도록
    // RigRoot 회전 중심(발 밑)을 보정해 노드 위치 · VisualRoot 높이를 계산한다.
    private static void PoseLying(Node3D node, EmployeeCctvAnimator anim, IsoState iso, Vector3 lieHips,
                                  float lieYaw, float hipY, float e)
    {
        var edge = iso.Edge;
        var seatHips = edge.Pos + new Vector3(0f, edge.Height, 0f);
        var hips = seatHips.Lerp(lieHips, e);
        float r = Mathf.Pi * 0.5f * e;
        float yaw = Mathf.LerpAngle(edge.Yaw, lieYaw, Mathf.Clamp(e * 1.15f, 0f, 1f));
        // 골반 = 노드 + Yaw·(VisualRoot + RigRoot 회전된 (0, hipY, 0))
        var back = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (hipY * Mathf.Sin(r));
        node.Position = Flat(hips - back);
        node.Rotation = new Vector3(0f, yaw, 0f);
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr != null) vr.Position = new Vector3(0f, hips.Y - hipY * Mathf.Cos(r), 0f);
        var rr = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot");
        if (rr != null) rr.Rotation = new Vector3(r, 0f, 0f);
        anim?.SetProceduralRoot(true);
    }

    private static float Smooth(float u) => u * u * (3f - 2f * u);

    // CCTV 를 돌려 격리실을 다시 봤다 — 그동안 이미 누워 묶여 있었을 수 있다.
    private void SnapIsolation(Actor actor, float ago)
    {
        if (ago < 7f) return;   // 아직 들어오는 중이었다면 처음부터(걷기부터) 보여준다
        actor.Iso = new IsoState
        {
            Phase = IsoPhase.Struggle,
            Struggled = Mathf.Max(0f, ago - 7f),
        };
    }
}
