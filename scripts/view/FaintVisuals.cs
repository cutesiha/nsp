using System.Collections.Generic;
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
//   · 운반 — 환자 노드가 운반자에게 **붙어서** 따라간다(업기 / 부축 / 안기)
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

            case FaintPhase.Transporting:
                // 운반자에게 붙어 간다 — 위치·자세 모두 운반자 기준으로 잡는다.
                return true;   // 실제 배치는 PoseCarry 가 한다

            default:
                return false;  // 침대·회복은 기존 WorkSpot 표현(lying_idle)이 맡는다
        }
    }

    // 눕힌 몸을 바닥에 얹는 높이. 뼈가 몸 한가운데를 지나므로 두께 절반만큼 올린다
    // (RoomWorkVisualController 의 침대 처리와 같은 값).
    private const float LyingLift = 0.12f;

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

    // ── 운반 ──────────────────────────────────────────────────────────

    // 환자를 운반자 몸에 붙인다. 자세는 캐릭터마다 다르다(§26 · §29 · §32).
    // 붙이는 기준이 운반자의 VisualRoot 라서 둘이 떨어져 뜨는 일이 없다.
    public static void PoseCarry(Node3D carrierNode, EmployeeCctvAnimator carrierAnim,
        Node3D victimNode, EmployeeCctvAnimator victimAnim,
        EmployeeState victim, string carrierId, float delta)
    {
        if (carrierNode == null || victimNode == null) return;
        var style = FaintRescueSystem.StyleOf(carrierId);
        float lift = Mathf.Clamp(victim.FaintPhaseTimer / FaintRescueSystem.LiftSeconds, 0f, 1f);
        float k = Mathf.SmoothStep(0f, 1f, lift);

        // 운반자 — 무게에 따라 자세가 다르다.
        carrierAnim?.PlayClip(lift < 1f ? "pickup_box"
            : style == CarryStyle.Shoulder ? "carry_box_heavy" : "carry_box_normal");
        var cvr = carrierNode.GetNodeOrNull<Node3D>("VisualRoot");
        if (cvr != null)
        {
            // 부축(양·토끼)은 환자 반대쪽으로 몸이 기운다. 업기·안기는 뒤로 조금 젖힌다.
            float lean = style switch
            {
                CarryStyle.Shoulder => 9f,
                CarryStyle.Piggyback => -5f,
                _ => -3f,
            };
            cvr.RotationDegrees = new Vector3(style == CarryStyle.Shoulder ? 4f : lean,
                cvr.RotationDegrees.Y, style == CarryStyle.Shoulder ? lean : 0f);
        }

        // 환자 — 운반자 위치에 붙고, 자세만 방식별로 다르게 얹는다.
        //
        // 업기·부축은 몸이 서 있는 방향이라 idle 을, 안기는 가로로 눕는 자세라 lying_idle 을 쓴다.
        // (lying_idle 은 클립 자체가 몸을 눕히므로 거기에 피치를 또 주면 안 된다.)
        victimAnim?.PlayClip(style == CarryStyle.Bridal ? "lying_idle" : "idle");
        victimNode.Position = carrierNode.Position;
        victimNode.RotationDegrees = carrierNode.RotationDegrees;

        var vvr = victimNode.GetNodeOrNull<Node3D>("VisualRoot");
        if (vvr == null) return;

        // 운반자 기준 로컬 오프셋과 기울기. 캐릭터는 자기 -Z 를 보므로 **등 뒤는 +Z** 다.
        (Vector3 Offset, Vector3 Rot) pose = style switch
        {
            // 업기 — 등에 업힌다. 몸은 거의 선 채로 운반자 등에 얹혀 앞으로 축 처진다.
            CarryStyle.Piggyback => (new Vector3(0f, 0.34f, 0.26f), new Vector3(-16f, 0f, 0f)),
            // 어깨 부축 — 옆에 서서 한 팔을 어깨에 건다. 발이 거의 바닥에 닿는다.
            CarryStyle.Shoulder => (new Vector3(0.34f, 0.04f, 0.02f), new Vector3(-8f, 0f, -20f)),
            // 공주님 안기 — 가슴 앞에 가로로 안긴다. 눕는 클립이라 방향만 90도 돌린다.
            _ => (new Vector3(0f, 0.56f, -0.22f), new Vector3(0f, 90f, 0f)),
        };
        vvr.Position = vvr.Position.Lerp(pose.Offset * k, Mathf.Clamp(delta * 6f, 0f, 1f));
        vvr.RotationDegrees = new Vector3(pose.Rot.X * k, pose.Rot.Y, pose.Rot.Z * k);
    }

    // ── 잡동사니 ──────────────────────────────────────────────────────

    // target 에서 from 쪽으로 distance 만큼 떨어진 자리(환자를 밟고 서지 않게).
    private static Vector3 AwayFrom(Vector3 target, Vector3 from, float distance)
    {
        Vector3 d = from - target;
        d.Y = 0f;
        if (d.LengthSquared() < 1e-4f) d = Vector3.Right;
        return d.Normalized() * distance;
    }

    private static void FaceTowards(Node3D node, Vector3 target)
    {
        Vector3 d = target - node.Position;
        d.Y = 0f;
        if (d.LengthSquared() < 1e-4f) return;
        node.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)) + 180f, 0f);
    }
}
