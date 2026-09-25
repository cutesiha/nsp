using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 작업실 안에서 "어느 직원을 어느 자리로 보내 어떤 모습으로 보여줄지"만 정한다.
//
// **표현 전용이다.** 업무 게이지·효과·배치 판정은 전부 FacilitySimulation 이 이미 끝냈고,
// 여기서는 그 결과(현재 방 · 현재 업무 · 표현 동작)를 읽어 위치와 애니메이션만 고른다.
// 저장고 수레 역시 화면 연출일 뿐, 자재 수치는 기존 inventory_sorting 효과가 담당한다.
public sealed partial class RoomWorkVisualController
{
    // 방 안에서 자리로 걸어가는 속도(표현 전용). 시뮬레이션 이동과 무관하다.
    private const float WalkSpeed = 1.15f;
    private const float ArriveDistance = 0.10f;

    // 격리 표현 — 발버둥 → 탈진 전환 시간(초). 게임 격리 판정/시간과는 무관하다.
    public const float IsolationStruggleSeconds = 10f;

    // 저장고 수레 연출 값.
    private const int CartCapacity = 5;
    private const float CartOutSeconds = 1.6f;
    private const float PickSeconds = 1.0f;
    private const float PlaceSeconds = 0.9f;

    private sealed class Actor
    {
        public RoomWorkSpot Spot;
        public bool Arrived;
        public Node3D BoxProp;          // 상자를 안고 있을 때만 보이는 소품
        public int CartPhase;           // 0 집으러 · 1 집는 중 · 2 나르는 중 · 3 내려놓는 중
        public float CartTimer;
        public string CartId = "";
        public float IsolatedFor = -1f;   // 격리 시작부터의 표현 전용 경과 시간
        public GhostVisual Ghost;         // 괴물 반응 중의 같은 방 안 표현 위치(아래 GhostVisual)
        public bool SnapToSpot;           // 다시 보일 때 걸어가지 않고 바로 자리에 붙인다(여우 — 계속 일하던 중)
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
        _cartRoom = "";
    }

    // 지금 보이는 직원들을 방의 작업 자리에 배치한다.
    //   visible : (직원 ID, 캐릭터 노드, 애니메이터, 표현 동작, 기본 슬롯 위치)
    public void Update(FacilitySimulation sim, Node3D room, string roomId,
                       List<(string Id, Node3D Node, EmployeeCctvAnimator Anim,
                             CctvEmployeeAction Action, Vector3 FallbackPos)> visible,
                       float delta, GhostReactionTracker reactions = null, Vector3? ghostPos = null)
    {
        if (room == null) return;
        var spots = GetSpots(room, roomId);
        string taskId = sim?.GetPrimarySpawnedTask(roomId)?.TaskId ?? "";

        TickCarts(room, roomId, delta);
        BeginGhostFrame(room, roomId, visible);

        var used = new Dictionary<RoomWorkSpot, int>();
        foreach (var v in visible)
        {
            var actor = _actors.TryGetValue(v.Id, out var a) ? a : _actors[v.Id] = new Actor();
            // 시선·재생 속도는 괴물 반응이 있을 때만 바꾼다 — 매 프레임 기본값부터.
            v.Anim?.SetLook(0f, 0f, 8f);
            v.Anim?.SetSpeedFactor(1f);

            // 격리 — 다른 무엇보다 먼저. 전용 침대에 눕히고 격리 전용 클립만 재생한다.
            if (v.Action == CctvEmployeeAction.Isolated)
            {
                var bed = PickSpot(spots, "__isolation__", actor.Spot, used);
                if (bed != null)
                {
                    used[bed] = used.GetValueOrDefault(bed) + 1;
                    if (actor.Spot != bed) { actor.Spot = bed; actor.Arrived = false; }
                    if (actor.IsolatedFor < 0f) actor.IsolatedFor = 0f;
                    actor.IsolatedFor += delta;
                    string clip = actor.IsolatedFor < IsolationStruggleSeconds
                        ? "isolated_struggle" : "isolated_exhausted";
                    MoveToSpot(v.Node, v.Anim, actor, bed, IsFemale(v.Node), delta, clip);
                }
                continue;
            }
            // 격리가 풀리면 표현 타이머도 초기화한다(다시 격리되면 0초부터).
            actor.IsolatedFor = -1f;

            // 기절한 직원은 '환자'다 — 의무실 침대에 눕힌다.
            // 반대로 멀쩡히 일하는 직원은 눕는 자리를 절대 쓰지 않는다(아래 PickSpot).
            if (sim?.GetEmployeeState(v.Id)?.Incapacitated == true)
            {
                var bed = PickSpot(spots, "__patient__", actor.Spot, used);
                if (bed != null)
                {
                    used[bed] = used.GetValueOrDefault(bed) + 1;
                    if (actor.Spot != bed) { actor.Spot = bed; actor.Arrived = false; }
                    MoveToSpot(v.Node, v.Anim, actor, bed, IsFemale(v.Node), delta, "lying_idle");
                    continue;
                }
                // 침대가 없는 방에서 기절했으면 그 자리에 그대로 둔다(기존 동작).
            }

            // 같은 방의 괴물 — 이동·방해공작·일반 업무보다 먼저 처리한다.
            // (격리·기절·실제 이동·장기 공황은 CctvActionResolver/GhostReactionTracker 가 이미 앞에 두었다.)
            var gs = reactions?.Get(v.Id);
            var prof = gs is { Stage: not GhostReactionTracker.Stage.None } ? GhostReactionProfiles.Get(gs.Action) : null;
            if (prof != null && !prof.KeepsWorking)
            {
                TickGhostReaction(v.Id, v.Node, v.Anim, v.FallbackPos, actor, gs, prof, ghostPos,
                                  spots, taskId, used, delta);
                continue;
            }
            if (prof != null) FoxOverlay(v.Id, v.Node, v.Anim, actor, gs, ghostPos);
            else actor.Ghost = null;

            // 대화·이동·방해공작 중에는 자리를 잡지 않는다(기존 연출을 그대로 둔다).
            bool freeAction = v.Action is CctvEmployeeAction.Walking or CctvEmployeeAction.Talking
                                        or CctvEmployeeAction.Suspicious or CctvEmployeeAction.Handoff;
            if (freeAction)
            {
                Release(actor);
                v.Node.Position = v.FallbackPos;
                ClearVisualOffset(v.Node);
                v.Anim?.SetAction(v.Action);
                v.Anim?.Tick();
                if (_facing != null) v.Node.RotationDegrees = new Vector3(0, _facing(v.Action, v.Id, v.FallbackPos), 0);
                continue;
            }

            // 저장고 재고 정리는 자리에 서 있는 게 아니라 상자를 나르는 왕복이다.
            if (roomId == "storage_room" && taskId == "inventory_sorting" && _carts.Count > 0)
            {
                TickCarrier(v.Id, v.Node, v.Anim, actor, spots, delta);
                continue;
            }

            var spot = PickSpot(spots, taskId, actor.Spot, used);
            if (spot == null)
            {
                Release(actor);
                v.Node.Position = v.FallbackPos;
                ClearVisualOffset(v.Node);
                v.Anim?.SetAction(v.Action);
                v.Anim?.Tick();
                if (_facing != null) v.Node.RotationDegrees = new Vector3(0, _facing(v.Action, v.Id, v.FallbackPos), 0);
                continue;
            }

            used[spot] = used.GetValueOrDefault(spot) + 1;
            if (actor.Spot != spot) { actor.Spot = spot; actor.Arrived = false; }
            if (actor.SnapToSpot) { actor.SnapToSpot = false; actor.Arrived = true; }
            MoveToSpot(v.Node, v.Anim, actor, spot, IsFemale(v.Node), delta);
        }

        // 화면에서 사라진 직원은 자리를 놓아 준다.
        foreach (var (id, actor) in _actors)
        {
            bool still = false;
            foreach (var v in visible) if (v.Id == id) { still = true; break; }
            if (!still)
            {
                Release(actor);
                // 다시 보일 때는 반응을 처음부터 하지 않고, 그동안 가 있었을 자리에 바로 둔다.
                if (actor.Ghost != null) actor.Ghost.NeedsPlace = true;
            }
        }

        // 클립·위치를 다 정한 뒤 — 쓰지 않는 관절 되돌리기 · 시선.
        foreach (var v in visible) v.Anim?.Update(delta);
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
        bool wantIsolation = taskId == "__isolation__";
        if (wantIsolation)
        {
            if (current != null && current.AnimationName.StartsWith("isolated_")
                && used.GetValueOrDefault(current) < current.Capacity) return current;
            foreach (var s in spots)
                if (s.AnimationName.StartsWith("isolated_") && used.GetValueOrDefault(s) < s.Capacity)
                    return s;
            return null;
        }
        // 일반 업무는 격리 침대를 쓰지 않는다.

        // 이미 쓰던 자리가 아직 유효하면 그대로 유지한다(자리 사이를 왔다갔다 하지 않게).
        if (current != null && !IsLyingSpot(current)
            && current.Supports(taskId)
            && used.GetValueOrDefault(current) < current.Capacity)
            return current;

        foreach (var s in spots)
        {
            if (IsLyingSpot(s)) continue;          // 눕는 자리는 환자·격리 전용이다
            if (!s.Supports(taskId)) continue;
            if (!s.SharedSpot && used.GetValueOrDefault(s) >= s.Capacity) continue;
            return s;
        }
        return null;
    }

    private static void Release(Actor a)
    {
        a.Spot = null;
        a.Arrived = false;
        if (a.BoxProp != null) a.BoxProp.Visible = false;
        a.CartPhase = 0;
        a.CartTimer = 0f;
        a.IsolatedFor = -1f;
    }

    // ── 자리로 이동 → 도착하면 작업 애니메이션 ─────────────────────────────

    private static void MoveToSpot(Node3D node, EmployeeCctvAnimator anim, Actor actor,
                                   RoomWorkSpot spot, bool female, float delta, string clipOverride = null)
    {
        var goal = spot.Position;
        var here = node.Position;
        // 앉거나 눕는 자리는 높이 보정을 따로 하므로 XZ 거리만 본다.
        float dist = new Vector2(goal.X - here.X, goal.Z - here.Z).Length();

        if (!actor.Arrived && dist > ArriveDistance)
        {
            var dir = new Vector3(goal.X - here.X, 0, goal.Z - here.Z).Normalized();
            node.Position = here + dir * Mathf.Min(WalkSpeed * delta, dist);
            node.Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
            ClearVisualOffset(node);
            anim?.SetAction(CctvEmployeeAction.Walking);
            return;
        }

        actor.Arrived = true;
        node.Position = new Vector3(goal.X, 0, goal.Z);
        node.Rotation = spot.Rotation;
        ApplySeatOffset(node, spot, clipOverride);
        anim?.PlayClip(clipOverride ?? spot.ClipFor(female));
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
        var hips = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
        float hipY = hips?.Position.Y ?? 0.9f;
        vr.Position = vr.Position with { Y = spot.SeatHeight - hipY };
    }

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

    // ── 저장고 : 상자 ↔ 수레 왕복 ────────────────────────────────────────

    private void TickCarts(Node3D room, string roomId, float delta)
    {
        if (roomId != "storage_room") { if (_cartRoom == "storage_room") { _carts.Clear(); _cartRoom = ""; } return; }
        if (_cartRoom != roomId)
        {
            _carts.Clear();
            _cartRoom = roomId;
            foreach (string name in new[] { "Cart1", "Cart2" })
            {
                var n = room.GetNodeOrNull<Node3D>(name);
                if (n == null) continue;
                var load = n.GetNodeOrNull<Node3D>($"{name}Load");
                _carts[name] = new Cart { Node = n, Load = load, Home = n.Position };
                SetCartBoxes(_carts[name], 0);
            }
        }

        foreach (var (_, cart) in _carts)
        {
            if (cart.OutTimer < 0f) continue;
            cart.OutTimer -= delta;
            // 가득 차면 출입구 쪽으로 나갔다가 빈 수레로 돌아온다(연출뿐, 수치와 무관).
            float t = 1f - Mathf.Clamp(cart.OutTimer / CartOutSeconds, 0f, 1f);
            float away = Mathf.Sin(t * Mathf.Pi) * 4.2f;
            cart.Node.Position = cart.Home + new Vector3(away, 0, away * 0.35f);
            if (t > 0.5f && cart.Boxes > 0) SetCartBoxes(cart, 0);
            if (cart.OutTimer <= 0f) { cart.OutTimer = -1f; cart.Node.Position = cart.Home; }
        }
    }

    private static void SetCartBoxes(Cart cart, int count)
    {
        cart.Boxes = count;
        if (cart.Load == null) return;
        for (int i = 0; i < cart.Load.GetChildCount(); i++)
            if (cart.Load.GetChild(i) is Node3D b) b.Visible = i < count;
    }

    private Cart FreeCart()
    {
        foreach (var (_, c) in _carts)
            if (c.OutTimer < 0f && c.Boxes < CartCapacity) return c;
        return null;
    }

    private void TickCarrier(string id, Node3D node, EmployeeCctvAnimator anim, Actor actor,
                             List<RoomWorkSpot> spots, float delta)
    {
        var pick = spots.Find(s => s.Id == "SupplyPickSpot");
        var drop = spots.Find(s => s.Id == "CartDropSpotA");
        if (pick == null || drop == null) return;

        EnsureBoxProp(node, actor);
        bool female = IsFemale(node);

        switch (actor.CartPhase)
        {
            case 0:   // 상자 더미로 이동
                if (Step(node, anim, pick.Position, delta)) { actor.CartPhase = 1; actor.CartTimer = PickSeconds; }
                break;

            case 1:   // 집는 중
                node.Rotation = pick.Rotation;
                anim?.PlayClip("pickup_box");
                actor.CartTimer -= delta;
                if (actor.CartTimer <= 0f)
                {
                    if (actor.BoxProp != null) actor.BoxProp.Visible = true;
                    var cart = FreeCart();
                    actor.CartId = cart != null ? cart.Node.Name.ToString() : "";
                    actor.CartPhase = 2;
                }
                break;

            case 2:   // 수레까지 나르기 — 남/여 carry 연출이 다르다(수치 영향 없음)
            {
                var goal = ResolveDropPosition(drop, actor);
                if (Step(node, anim, goal, delta, female ? "carry_box_heavy" : "carry_box_normal"))
                { actor.CartPhase = 3; actor.CartTimer = PlaceSeconds; }
                break;
            }

            case 3:   // 내려놓기
                anim?.PlayClip("place_box");
                actor.CartTimer -= delta;
                if (actor.CartTimer <= 0f)
                {
                    if (actor.BoxProp != null) actor.BoxProp.Visible = false;
                    if (_carts.TryGetValue(actor.CartId, out var c) && c.OutTimer < 0f)
                    {
                        SetCartBoxes(c, Mathf.Min(c.Boxes + 1, CartCapacity));
                        if (c.Boxes >= CartCapacity) c.OutTimer = CartOutSeconds;
                    }
                    actor.CartPhase = 0;
                }
                break;
        }
    }

    private Vector3 ResolveDropPosition(RoomWorkSpot drop, Actor actor)
    {
        if (_carts.TryGetValue(actor.CartId, out var c) && c.OutTimer < 0f)
            return c.Home + new Vector3(0, 0, 0.62f);
        return drop.Position;
    }

    // 목표 지점까지 한 걸음. 도착하면 true.
    private static bool Step(Node3D node, EmployeeCctvAnimator anim, Vector3 goal, float delta,
                             string walkClip = null)
    {
        var here = node.Position;
        var flat = new Vector3(goal.X - here.X, 0, goal.Z - here.Z);
        float d = flat.Length();
        if (d <= ArriveDistance) return true;

        var dir = flat / d;
        node.Position = here + dir * Mathf.Min(WalkSpeed * delta, d);
        node.Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
        if (walkClip == null) anim?.SetAction(CctvEmployeeAction.Walking);
        else anim?.PlayClip(walkClip);
        return false;
    }

    // 상자를 안고 있을 때만 보이는 소품. 캐릭터 가슴 앞에 붙인다.
    private static void EnsureBoxProp(Node3D node, Actor actor)
    {
        if (actor.BoxProp != null && GodotObject.IsInstanceValid(actor.BoxProp)) return;
        var anchor = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest");
        if (anchor == null) return;
        var mi = new MeshInstance3D
        {
            Name = "CarriedBox",
            Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.26f, 0.28f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.42f, 0.28f), Roughness = 0.9f },
            Visible = false,
        };
        anchor.AddChild(mi);
        mi.Position = new Vector3(0, 0.02f, -0.30f);
        actor.BoxProp = mi;
    }
}
