using Godot;
using NSP.Facility;

namespace NSP.View;

// 기절 흐름의 CCTV 표현 — 쓰러짐 · 바닥 · 다가감 · 상태 확인 · 운반.
//
// 판정은 전부 FaintRescueSystem 에 있다. 여기서는 그 단계(FaintPhase)를 읽어
// **그 사람의 3D 노드를 어떤 자세·어디에 둘지**만 정한다. 아무것도 바꾸지 않는다.
//
// 이 파일이 맡는 것
//   · 쓰러지는 1.25초 — 앞/뒤 두 방향. 모델을 그냥 눕히지 않고 상체 → 무릎 → 바닥 순서로 접는다.
//   · 바닥에 누운 채의 미세한 호흡
//   · 구조자가 환자 옆으로 다가와 무릎을 굽히고 확인하는 자세
//   · 운반 — 환자가 운반자의 **Carry Anchor** 에 붙어 따라간다(업기 / 안기 / 부축)
//
// 운반 동작의 순서(들어 올리기 · 걷기 · 침대 접근 · 내려놓기 · 복귀)는
// RoomWorkVisualController.Carry.cs 가 끌고 간다. 여기는 "어떤 클립을 쓰고,
// 환자 몸을 운반자의 어디에 붙이는가" 만 정한다.
//
// 환자는 절대 자기 애니메이션으로 걷지 않는다.
public static class FaintVisuals
{
    // 캐릭터별 발견 반응의 크기(§13). 0 = 거의 안 놀람, 1 = 크게 놀람.
    // 토끼·강아지·양이 크고, 늑대·고양이는 작고, 여우가 가장 작다.
    public static float StartleOf(string employeeId) => employeeId switch
    {
        "rabbit" => 1.00f,
        "dog" => 0.92f,
        "sheep" => 0.85f,   // 크게 놀라지만 뒤로 움찔한다(아래 Recoils)
        "wolf" => 0.34f,
        "cat" => 0.30f,
        _ => 0.12f,         // fox — 거의 놀라지 않는다
    };

    // 놀랄 때 뒤로 물러서는가(양만). 겁이 많아 먼저 몸이 뒤로 간 뒤에 다가간다.
    public static bool Recoils(string employeeId) => employeeId == "sheep";

    // ── 사람 운반 전용 클립 ────────────────────────────────────────────
    //
    // 박스 운반 클립(pickup_box · carry_box_* · place_box_*)은 여기서 절대 쓰지 않는다.
    // 팔 각도도 손 높이도 34cm 상자에 맞춰져 있어서, 사람에게 쓰면 손이 몸을 뚫거나 허공에 뜬다.
    // 아래 클립은 전부 tools/gen_carry_clips.gd 가 **사람 몸을 목표로 팔 IK 를 풀어** 만든 것이다.
    // 이름은 체형 접미사(_m / _f) 없는 기본형 — 부르는 쪽에서 BodyClip 으로 붙인다.

    public static string PickupClip(CarryStyle s) => s switch
    {
        CarryStyle.Piggyback => "faint_pickup_piggyback",
        CarryStyle.Bridal => "faint_pickup_bridal",
        _ => "faint_pickup_shoulder",
    };

    public static string CarryWalkClip(CarryStyle s) => s switch
    {
        CarryStyle.Piggyback => "faint_carry_walk_piggyback",
        CarryStyle.Bridal => "faint_carry_walk_bridal",
        _ => "faint_carry_walk_shoulder",
    };

    public static string BedPlaceClip(CarryStyle s) => s switch
    {
        CarryStyle.Piggyback => "faint_bed_place_piggyback",
        CarryStyle.Bridal => "faint_bed_place_bridal",
        _ => "faint_bed_place_shoulder",
    };

    // 실려 가는 동안의 환자 전용 자세. idle / lying_idle 을 쓰지 않는다 —
    // 의식 없는 사람은 제 힘으로 허리를 세우지 못한다(§29 · §30).
    public static string VictimClip(CarryStyle s) => s switch
    {
        CarryStyle.Piggyback => "faint_victim_piggyback",
        CarryStyle.Bridal => "faint_victim_bridal",
        _ => "faint_victim_shoulder",
    };

    public const string CarrierRecoverClip = "faint_carrier_recover";

    // 걷기 클립 한 바퀴가 실제로 나아가야 하는 거리(m). 이동 속도를 이 값으로 나눠 재생 속도를
    // 맞춰야 발이 바닥에서 미끄러지지 않는다(§24 · §25).
    public static float CarryWalkStride(CarryStyle s) => s switch
    {
        CarryStyle.Piggyback => 0.60f,   // 무릎을 굽힌 채 걸어 보폭이 짧다
        CarryStyle.Bridal => 0.72f,
        _ => 0.42f,                      // 부축 — 둘이 함께 절뚝여 보폭이 가장 짧다
    };

    // ── Carry Anchor ───────────────────────────────────────────────────
    //
    // 환자를 "운반자 노드 위치" 가 아니라 **운반자 리그의 관절** 에 붙인다. 그래야 운반자가
    // 걸을 때 몸이 흔들리고 숙여지는 만큼 환자도 같이 움직여, 둘이 하나의 덩어리로 보인다(§3·§4).
    //
    // 노드를 실제로 reparent 하지는 않는다 — 직원 dictionary 와 방 소유권을 깨지 않으려고,
    // 매 프레임 앵커의 전역 transform 을 읽어 환자 노드를 그 자리에 놓기만 한다(§5).

    private const string AnchorName = "CarryAnchor";

    private static readonly string[] AnchorParents =
    {
        "VisualRoot/RigRoot/Hips",
        "VisualRoot/RigRoot/Hips/Torso/Chest",
    };

    // 방식별로 붙는 관절과 그 관절 기준 위치가 다르다(§6). 같은 오프셋을 돌려 쓰지 않는다.
    private static (string Path, Vector3 Offset) AnchorSpec(CarryStyle style) => style switch
    {
        // 업기 — 골반 바로 위, 등 쪽(캐릭터는 -Z 를 보므로 +Z 가 등 뒤다).
        // 환자의 엉덩이가 여기 얹히고 상체가 운반자 등에 붙는다.
        CarryStyle.Piggyback => (AnchorParents[0], new Vector3(0f, 0.12f, 0.18f)),
        // 공주님 안기 — 가슴 앞, 조금 아래. 환자 몸의 한가운데가 여기 온다.
        CarryStyle.Bridal => (AnchorParents[1], new Vector3(0f, -0.11f, -0.23f)),
        // 어깨 부축 — 왼쪽 옆구리. 환자는 바닥에 발을 붙인 채 이쪽으로 기댄다.
        _ => (AnchorParents[1], new Vector3(-0.26f, -0.06f, 0.02f)),
    };

    // 운반자 리그에 표현용 앵커를 만들어 둔다(이미 있으면 그대로 쓴다).
    // 방식이 바뀌면 붙는 관절도 바뀌므로 예전 앵커는 떼어낸다.
    public static Node3D EnsureAnchor(Node3D carrierNode, CarryStyle style)
    {
        if (carrierNode == null) return null;
        var (path, offset) = AnchorSpec(style);
        var parent = carrierNode.GetNodeOrNull<Node3D>(path);
        if (parent == null) return null;

        foreach (string other in AnchorParents)
        {
            if (other == path) continue;
            carrierNode.GetNodeOrNull<Node3D>(other + "/" + AnchorName)?.QueueFree();
        }

        var anchor = parent.GetNodeOrNull<Node3D>(AnchorName);
        if (anchor == null)
        {
            anchor = new Node3D { Name = AnchorName };
            parent.AddChild(anchor);
        }
        anchor.Position = offset;
        return anchor;
    }

    // 체형 차이 보정(§7) — 조합을 하나하나 적지 않고, 그 체형의 골반 높이 하나만 쓴다.
    public static float HipHeight(Node3D node) =>
        node?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips")?.Position.Y ?? 0.9f;

    // 완전히 실려 있을 때 환자 노드가 있어야 할 전역 transform.
    // 앵커에서 출발하므로 운반자가 숙이고 흔들리는 대로 그대로 따라간다.
    public static Transform3D VictimTransform(Node3D carrierNode, Node3D victimNode, CarryStyle style)
    {
        if (carrierNode == null || victimNode == null) return Transform3D.Identity;
        var anchor = EnsureAnchor(carrierNode, style);
        if (anchor == null) return victimNode.GlobalTransform;
        var a = anchor.GlobalTransform;

        switch (style)
        {
            case CarryStyle.Piggyback:
            {
                // 환자는 운반자와 같은 쪽을 보고, 골반이 앵커에 얹힌다.
                var basis = CarryBasis(a.Basis, carrierNode);
                return new Transform3D(basis, a.Origin)
                       * new Transform3D(Basis.Identity, new Vector3(0f, -HipHeight(victimNode), 0f));
            }
            case CarryStyle.Bridal:
            {
                // 몸이 가로로 눕는다. 환자 클립이 RigRoot 를 90° 눕혀 두므로 몸은 자기 +Z 로 뻗는다 —
                // 머리가 운반자 왼쪽으로 가게 -90° 돌리고, 몸 한가운데가 앵커에 오도록 발 쪽으로 당긴다.
                var basis = CarryBasis(a.Basis, carrierNode);
                var rot = new Basis(Vector3.Up, Mathf.DegToRad(-90f));
                return new Transform3D(basis * rot, a.Origin)
                       * new Transform3D(Basis.Identity, new Vector3(0f, 0f, -BridalBodyCenter));
            }
            default:
            {
                // 부축 — 환자 발이 바닥에 닿아 있어야 한다(§18). 높이는 운반자가 선 바닥 높이를
                // 그대로 쓰고 좌우 위치만 옮긴다. 앵커의 상하 흔들림까지 따라가면 발이 땅을 뚫는다.
                var cb = carrierNode.GlobalTransform.Basis;
                var pos = carrierNode.GlobalPosition + cb * ShoulderSideOffset;
                pos.Y = carrierNode.GlobalPosition.Y;
                return new Transform3D(cb, pos);
            }
        }
    }

    // 안긴 몸의 발끝에서 몸 한가운데까지(m). 환자 루트는 발 쪽에 있다.
    private const float BridalBodyCenter = 0.78f;
    // 부축할 때 환자가 서는 자리(운반자 기준 왼쪽 옆).
    private static readonly Vector3 ShoulderSideOffset = new(-0.30f, 0f, 0.04f);

    // 앵커가 앞뒤로 숙은 것은 살리고 좌우 비틀림은 줄인 기울기.
    // 운반자가 걸으며 몸을 비트는 것까지 그대로 받으면 환자 다리가 운반자를 뚫는다.
    private static Basis CarryBasis(Basis anchor, Node3D carrierNode)
    {
        var yaw = new Basis(Vector3.Up, carrierNode.GlobalRotation.Y);
        float pitch = anchor.GetEuler().X;
        return yaw * new Basis(Vector3.Right, pitch * 0.6f);
    }

    // ── 환자 ──────────────────────────────────────────────────────────

    // 쓰러진 사람의 몸을 지금 단계에 맞게 놓는다.
    // 반환값 = 이 프레임에 이 노드를 직접 다뤘는가(true 면 일반 업무 표현을 건너뛴다).
    public static bool PoseVictim(Node3D node, EmployeeCctvAnimator anim, EmployeeState st, float delta)
    {
        if (node == null || st == null) return false;
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return false;

        switch (st.Faint)
        {
            case FaintPhase.Falling:
            {
                // 서 있는 몸이 발을 축으로 넘어간다. VisualRoot 의 원점이 발이라 여기를 돌리면
                // 그대로 "쓰러지는" 그림이 된다 — 높이를 따로 내리면 바닥을 뚫는다.
                float k = Mathf.Clamp(st.FaintPhaseTimer / FaintRescueSystem.FallSeconds, 0f, 1f);
                anim?.PlayClip("idle");
                ApplyCollapse(vr, k, st.FellForward);
                return true;
            }

            case FaintPhase.OnFloor:
            case FaintPhase.BeingChecked:
            case FaintPhase.AwaitingDecision:
            case FaintPhase.TransportDenied:
            {
                // lying_idle 은 **클립 자체가** 몸을 눕힌다(RigRoot 를 90도 돌린다).
                // 그래서 VisualRoot 는 돌리지 않고, 몸 두께의 절반만 띄워 바닥에 얹는다.
                anim?.PlayClip("lying_idle");
                float b = Mathf.Sin(Time.GetTicksMsec() / 1000f * 1.6f) * 0.008f;   // 호흡(§10)
                vr.RotationDegrees = new Vector3(0f, vr.RotationDegrees.Y, 0f);
                vr.Position = vr.Position with { Y = LyingLift + b };
                return true;
            }

            case FaintPhase.TransportPickup:
            case FaintPhase.Transporting:
            case FaintPhase.BedApproach:
            case FaintPhase.InMedicalBed:
                // 운반 중 — 위치도 자세도 전부 운반자 쪽(TickCarry)이 정한다.
                // 여기서 또 손대면 한 프레임에 두 곳이 환자를 움직여 서로 밀어낸다.
                //
                // 다만 VisualRoot 를 운반 쪽이 직접 쓰고 있다는 것만 알려 둔다 — 안 그러면
                // 매 프레임 기본 자세로 되돌리는 RelaxOrphans 가 골반 높이를 도로 끌어내린다.
                anim?.SetProceduralRoot(true);
                return true;

            default:
                return false;  // 회복은 기존 WorkSpot 표현(lying_idle)이 맡는다
        }
    }

    // 눕힌 몸을 바닥에 얹는 높이. 뼈가 몸 한가운데를 지나므로 두께 절반만큼 올린다
    // (RoomWorkVisualController 의 침대 처리와 같은 값).
    public const float LyingLift = 0.12f;

    // 무너지는 자세 — 발을 축으로 넘어간다.
    // 앞으로 쓰러지면 +피치, 뒤로 넘어지면 -피치. 처음에는 천천히 기울다가 끝에서 확 넘어간다.
    private static void ApplyCollapse(Node3D vr, float k, bool forward)
    {
        //  ~0.25 고개·상체가 조금 기운다 → ~0.6 무릎이 풀린다 → ~1.0 바닥에 닿는다
        float ease = k < 0.45f
            ? Mathf.SmoothStep(0f, 1f, k / 0.45f) * 0.28f      // 버티는 구간
            : 0.28f + Mathf.Pow((k - 0.45f) / 0.55f, 1.7f) * 0.72f;
        float pitch = (forward ? 1f : -1f) * ease * 88f;
        vr.RotationDegrees = new Vector3(pitch, vr.RotationDegrees.Y, 0f);
        // 무릎이 풀리며 몸이 조금 내려앉는다(바닥을 뚫지 않을 만큼만).
        vr.Position = vr.Position with { Y = Mathf.Lerp(0f, LyingLift, Mathf.SmoothStep(0.5f, 1f, k)) };
    }

    // 바닥에 누워 있는 환자의 transform(들어 올리기의 출발점).
    public static Transform3D FloorTransform(Node3D victimNode, Vector3 floorPos)
    {
        var b = new Basis(Vector3.Up, victimNode?.GlobalRotation.Y ?? 0f);
        return new Transform3D(b, floorPos);
    }

    // ── 구조자 ────────────────────────────────────────────────────────

    // 환자 옆으로 다가가 무릎을 굽히고 상태를 확인하는 자세(§12 · §14).
    // 손이 허공에 뜨지 않게 환자 몸 위치를 기준으로 자리를 잡는다.
    public static bool PoseResponder(Node3D node, EmployeeCctvAnimator anim, EmployeeState responder,
        EmployeeState victim, Vector3 victimPos, float delta)
    {
        if (node == null || victim == null) return false;
        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return false;

        float t = victim.FaintPhaseTimer;
        float startle = StartleOf(responder.EmployeeId);

        // ① 놀람 — 그 자리에서 몸이 움찔한다. 캐릭터마다 크기가 다르다.
        if (t < FaintRescueSystem.ApproachSeconds * 0.45f)
        {
            anim?.PlayClip(startle > 0.6f ? "suspicious" : "inspect");
            float j = Mathf.Sin(t * 26f) * 3.5f * startle;
            // 양은 먼저 뒤로 물러선다.
            float back = Recoils(responder.EmployeeId) ? -0.18f * startle : 0f;
            vr.RotationDegrees = new Vector3(-j, vr.RotationDegrees.Y, j * 0.4f);
            node.Position = node.Position.Lerp(victimPos + AwayFrom(victimPos, node.Position, 0.85f - back),
                Mathf.Clamp(delta * 4f, 0f, 1f));
            return true;
        }

        // ② 환자 옆으로 다가간다.
        Vector3 beside = victimPos + AwayFrom(victimPos, node.Position, 0.42f);
        node.Position = node.Position.Lerp(beside, Mathf.Clamp(delta * 3.4f, 0f, 1f));
        FaceTowards(node, victimPos);

        // ③ 무릎을 굽히고 상체를 숙여 얼굴·어깨 쪽을 확인한다.
        if (t >= FaintRescueSystem.ApproachSeconds)
        {
            anim?.PlayClip("repair");   // 무릎을 굽혀 손을 앞으로 내미는 자세
            float k = Mathf.Clamp((t - FaintRescueSystem.ApproachSeconds) / 0.4f, 0f, 1f);
            vr.RotationDegrees = new Vector3(Mathf.Lerp(0f, -26f, k), vr.RotationDegrees.Y, 0f);
            vr.Position = vr.Position with { Y = Mathf.Lerp(0f, -0.26f, k) };
        }
        else anim?.PlayClip("walk");
        return true;
    }

    // ── 잡동사니 ──────────────────────────────────────────────────────

    // target 에서 from 쪽으로 distance 만큼 떨어진 자리(환자를 밟고 서지 않게).
    public static Vector3 AwayFrom(Vector3 target, Vector3 from, float distance)
    {
        Vector3 d = from - target;
        d.Y = 0f;
        if (d.LengthSquared() < 1e-4f) d = Vector3.Right;
        return d.Normalized() * distance;
    }

    public static void FaceTowards(Node3D node, Vector3 target)
    {
        Vector3 d = target - node.Position;
        d.Y = 0f;
        if (d.LengthSquared() < 1e-4f) return;
        node.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)) + 180f, 0f);
    }
}
