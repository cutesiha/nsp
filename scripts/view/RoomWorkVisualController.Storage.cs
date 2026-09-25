using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 저장고 재고 정리 — 상자 더미 ↔ 수레 왕복. **표현 전용이다** (자재 수치는 inventory_sorting 효과가 담당).
//
// 운반조는 두 명까지(더미 앞 · 옆에서 각자 집어 수레 1 · 2 로). 나머지 인원은 선반 작업 자리로 간다.
// 상자는 매 프레임 두 손바닥 사이에 놓는다 — 팔 간격과 상자 폭(34cm)이 맞게 클립을 만들었으므로
// 손이 상자 옆면을 받치고, 상자는 가슴·배에 붙는다. 내려놓을 때는 손을 놓는 순간 수레 칸으로 옮겨 앉는다.
//
// 들고 나르는 모습은 사람마다 다르다(능력치가 아니라 CCTV 표현만):
//   남성(늑대·강아지·여우) 수월하게 · 여성(토끼·고양이) 조금 힘겹게 · 양 가장 힘겹게(느리고, 다시 끌어올리고, 내려놓고 어깨가 푹).
public sealed partial class RoomWorkVisualController
{
    private const int CartCapacity = 5;
    private const float CartOutSeconds = 1.6f;
    private const int MaxCarriers = 2;
    private const float PickReach = 0.44f;          // 상자 더미 맨 윗상자까지 앞 거리(pickup_box_* 와 같은 값)
    private const float DropReach = 0.62f;          // 수레 중심에서 내려놓는 사람까지
    private const float BoxSettleSeconds = 0.15f;   // 손을 놓은 상자가 수레 칸에 앉는 시간
    private const float BoxAboveGrip = 0.26f * 0.28f;   // 손바닥(옆면 아래쪽)에서 상자 중심까지

    private static string CarryStyle(string id) => id switch
    {
        "sheep" => "sheep",
        "rabbit" or "cat" => "female",
        _ => "male",
    };

    // 상자를 든 걸음의 빠르기(표현만). 양이 가장 느리다.
    private static float CarrySpeed(string id) => id switch
    {
        "sheep" => 0.62f, "rabbit" => 0.94f, "cat" => 0.9f, "dog" => 0.95f, "fox" => 1.05f, _ => 1f,
    };

    private void AssignCarriers(string roomId, string taskId,
                                List<(string Id, Node3D Node, EmployeeCctvAnimator Anim, CctvEmployeeAction Action, Vector3 FallbackPos)> visible,
                                GhostReactionTracker reactions)
    {
        bool sorting = roomId == "storage_room" && taskId == "inventory_sorting" && _carts.Count > 0;
        var eligible = new List<string>();
        foreach (var v in visible)
        {
            bool ok = sorting
                      && v.Action is not (CctvEmployeeAction.Isolated or CctvEmployeeAction.Walking or CctvEmployeeAction.Leaving
                          or CctvEmployeeAction.Suspicious or CctvEmployeeAction.Handoff)
                      && !GhostReactionProfiles.IsGhostReaction(v.Action)
                      && reactions?.Get(v.Id) is not { Stage: not GhostReactionTracker.Stage.None };
            if (ok) eligible.Add(v.Id);
        }
        // 이미 나르던 사람은 계속 나른다(맡은 줄도 그대로).
        var taken = new HashSet<int>();
        foreach (var v in visible)
        {
            if (!_actors.TryGetValue(v.Id, out var a)) continue;
            if (a.Carrier >= 0 && eligible.Contains(v.Id) && taken.Add(a.Carrier)) continue;
            a.Carrier = -1;
        }
        foreach (string id in eligible)
        {
            if (taken.Count >= MaxCarriers) break;
            if (!_actors.TryGetValue(id, out var a) || a.Carrier >= 0) continue;
            for (int i = 0; i < MaxCarriers; i++)
                if (taken.Add(i)) { a.Carrier = i; a.CartPhase = 0; break; }
        }
    }

    // ── 수레 ──

    private void TickCarts(Node3D room, string roomId, float delta)
    {
        if (roomId != "storage_room") { if (_cartRoom == "storage_room") { _carts.Clear(); _cartRoom = ""; } return; }
        _storageRoom = room;
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

    // ── 운반조 한 명 ──

    // 집는 자리: 0번은 더미 앞에서, 1번은 더미 옆에서(서로 부딪히지 않게). 맨 윗상자 바로 앞에 선다.
    private (Vector3 Pos, float Yaw) PickPoint(int carrier)
    {
        var room = _storageRoom;
        var top0 = room?.GetNodeOrNull<Node3D>("SupplyStack2")?.Position ?? new Vector3(-1.95f, 0.46f, 1.35f);
        var top1 = room?.GetNodeOrNull<Node3D>("SupplyStack3")?.Position ?? new Vector3(-1.45f, 0.46f, 1.35f);
        return carrier == 0
            ? (Flat(top0) + new Vector3(0f, 0f, -PickReach), Mathf.Pi)          // +Z 를 본다
            : (Flat(top1) + new Vector3(PickReach, 0f, 0f), Mathf.Pi * 0.5f);   // -X 를 본다
    }

    // 내려놓는 자리: 수레 1 은 앞(+Z)에서, 수레 2 는 옆(-X)에서.
    private static (Vector3 Pos, float Yaw) DropPoint(Cart cart, int carrier) =>
        carrier == 0
            ? (Flat(cart.Home) + new Vector3(0f, 0f, DropReach), 0f)
            : (Flat(cart.Home) + new Vector3(-DropReach, 0f, 0f), -Mathf.Pi * 0.5f);

    private Node3D _storageRoom;

    private void TickCarrier(string id, Node3D node, EmployeeCctvAnimator anim, Actor actor,
                             List<RoomWorkSpot> spots, bool female, float delta)
    {
        string style = CarryStyle(id);
        string pickClip = $"pickup_box_{style}", carryClip = $"carry_box_{style}", placeClip = $"place_box_{style}";
        var cart = _carts.GetValueOrDefault(actor.Carrier == 0 ? "Cart1" : "Cart2") ?? FirstCart();
        if (cart == null) return;
        actor.CartId = cart.Node.Name.ToString();

        switch (actor.CartPhase)
        {
            case 0:   // 상자 더미로 걸어가 맨 윗상자 앞에서 몸을 돌린다
            {
                var (p, yaw) = PickPoint(actor.Carrier);
                if (!FollowRoute(node, anim, actor, p, delta)) return;
                node.Position = p;
                if (!TurnTo(node, anim, yaw, delta)) return;
                actor.CartPhase = 1; actor.CartTimer = 0f;
                anim?.PlayClip(pickClip, 0.15, 0f);
                break;
            }
            case 1:   // 집기 — 두 손이 옆면에 닿는 순간부터 상자는 손 사이에 있다
            {
                anim?.PlayClip(pickClip, 0.15, 0f);
                var (at, length) = WorkClipReach.Box(pickClip);
                actor.CartTimer += delta;
                if (actor.CartTimer >= at) actor.BoxInHands = true;
                if (actor.CartTimer >= length) { actor.CartPhase = 2; actor.HasRoute = false; }
                break;
            }
            case 2:   // 나르기
            {
                var (p, yaw) = DropPoint(cart, actor.Carrier);
                if (!FollowRoute(node, anim, actor, p, delta, CarrySpeed(id), carryClip)) return;
                node.Position = p;
                if (!TurnTo(node, anim, yaw, delta)) { anim?.PlayClip(carryClip); return; }
                // 수레가 비우러 나가 있으면 상자를 든 채 기다린다.
                if (cart.OutTimer >= 0f) { anim?.PlayClip(carryClip); anim?.SetSpeedFactor(0.15f); return; }
                actor.CartPhase = 3; actor.CartTimer = 0f;
                anim?.PlayClip(placeClip, 0.12, 0f);
                break;
            }
            case 3:   // 내려놓기 — 손을 놓는 순간 상자는 수레 칸으로
            {
                anim?.PlayClip(placeClip, 0.12, 0f);
                var (at, length) = WorkClipReach.Box(placeClip);
                actor.CartTimer += delta;
                if (actor.BoxInHands && actor.CartTimer >= at && actor.BoxProp != null)
                {
                    actor.BoxInHands = false;
                    actor.ReleaseT = 0f;
                    actor.ReleaseFrom = actor.BoxProp.GlobalTransform;
                }
                if (actor.CartTimer >= length) { actor.CartPhase = 0; actor.HasRoute = false; }
                break;
            }
        }
    }

    private Cart FirstCart()
    {
        foreach (var (_, c) in _carts) return c;
        return null;
    }

    // 들고 있는 상자 — 두 손바닥 사이(옆면 아래쪽을 받친 위치)에, 가슴 방향 그대로.
    // 놓은 뒤에는 짧게 수레 칸으로 내려앉고, 그 순간 수레 쪽 상자가 켜진다(상자가 순간이동하지 않게).
    private void UpdateHeldBox(Node3D node, Actor actor, float delta)
    {
        if (actor.ReleaseT >= 0f)
        {
            actor.ReleaseT += delta;
            var cart = _carts.GetValueOrDefault(actor.CartId);
            var slot = cart?.Load != null && cart.Boxes < cart.Load.GetChildCount()
                ? (cart.Load.GetChild(cart.Boxes) as Node3D)?.GlobalTransform : null;
            if (actor.BoxProp != null && slot != null)
                actor.BoxProp.GlobalTransform = actor.ReleaseFrom.InterpolateWith(slot.Value,
                    Mathf.Clamp(actor.ReleaseT / BoxSettleSeconds, 0f, 1f));
            if (actor.ReleaseT >= BoxSettleSeconds)
            {
                actor.ReleaseT = -1f;
                if (actor.BoxProp != null) actor.BoxProp.Visible = false;
                if (cart != null && cart.OutTimer < 0f)
                {
                    SetCartBoxes(cart, Mathf.Min(cart.Boxes + 1, CartCapacity));
                    if (cart.Boxes >= CartCapacity) cart.OutTimer = CartOutSeconds;
                }
            }
            return;
        }
        if (!actor.BoxInHands) { if (actor.BoxProp != null) actor.BoxProp.Visible = false; return; }

        EnsureBoxProp(node, actor);
        var hl = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderL/UpperArmL/LowerArmL/HandL");
        var hr = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderR/UpperArmR/LowerArmR/HandR");
        var chest = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest");
        if (hl == null || hr == null || chest == null || actor.BoxProp == null) return;
        var pl = hl.GlobalTransform * new Vector3(0f, -0.06f, 0f);
        var pr = hr.GlobalTransform * new Vector3(0f, -0.06f, 0f);
        var basis = chest.GlobalTransform.Basis.Orthonormalized();
        actor.BoxProp.GlobalTransform = new Transform3D(basis, (pl + pr) * 0.5f + basis.Y * BoxAboveGrip);
        actor.BoxProp.Visible = true;
    }

    private static void HideBox(Actor a)
    {
        a.BoxInHands = false;
        a.ReleaseT = -1f;
        if (a.BoxProp != null && GodotObject.IsInstanceValid(a.BoxProp)) a.BoxProp.Visible = false;
    }

    // 상자 소품(34×26×28cm) — 캐릭터 노드 밑에 두고 매 프레임 손 위치로 옮긴다.
    private static void EnsureBoxProp(Node3D node, Actor actor)
    {
        if (actor.BoxProp != null && GodotObject.IsInstanceValid(actor.BoxProp)) return;
        var mi = new MeshInstance3D
        {
            Name = "CarriedBox",
            Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.26f, 0.28f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.42f, 0.28f), Roughness = 0.9f },
            Visible = false,
        };
        node.AddChild(mi);
        actor.BoxProp = mi;
    }
}
