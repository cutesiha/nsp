using Godot;

namespace NSP.Tools;

// 헤어를 머리통에 **자동으로 올려놓는** 계산.
//
// 원본 헤어팩은 저마다 좌표계도 크기도 다르다(남성 FBX 는 100배 스케일 + Z-up,
// 여성 OBJ 는 센티미터 격자). 그래서 분리할 때 전부 미터 단위로 내리고 원점을 AABB
// 중심으로 옮겨 두었다. 여기서는 그 메시를 실측한 두개골 크기에 맞춘다.
//
// 어디까지나 **출발점**이다. 지시서 §5 가 요구한 대로 사람이 바로 미세 조정할 수 있게
// 위치·회전·크기 슬라이더가 이 값을 초기값으로 받는다. "위치 초기화" 는 여기로 돌린다.
public static class HairFit
{
    // 머리카락 윗부분의 폭을 **귀 위쪽 머리통** 폭의 몇 배로 맞출지. 머리카락은 두피를
    // 조금 넘겨 덮으므로 1 보다 약간 크다. 화면으로 확인하며 고른 값이다.
    public const float CapWidthFactor = 1.10f;

    // 게임 직원 모델은 머리가 **사람 두상이 아니라 지름 0.26m 짜리 큰 구**다. 그 구는
    // 실제 머리통보다 크게 그려져 있어서, 머리카락을 구 폭에 그대로 맞추면 과하게 커진다.
    // 1 보다 작은 값이 되는 이유가 그것이다(헤어팩 두상은 1.10, 여기는 0.95).
    public const float ModelCapWidthFactor = 0.95f;

    // 정수리 위로 띄우는 높이(m). 0 이면 머리카락 끝이 두피에 딱 붙어 비어 보인다.
    public const float Lift = 0.004f;

    public const float MinScale = 0.25f, MaxScale = 4f;

    // 머리 본 기준 좌표계에서의 초기 변환. 스튜디오의 BoneAttachment3D 아래에 그대로 넣는다.
    // yaw 는 팩 단위로 정한 방향이다(HairCatalog.PackYaw).
    public static void Auto(HairEntry e, HairBaseEntry b, Transform3D headRest, float yaw,
                            out Vector3 pos, out Vector3 rotDeg, out float scale,
                            float widthFactor = CapWidthFactor)
    {
        rotDeg = new Vector3(0f, yaw, 0f);
        scale = FitScale(e, b, widthFactor);
        pos = Place(e, b, headRest, yaw, scale);
    }

    // 머리카락 윗부분의 폭을 귀 위쪽 머리통 폭에 맞춘다.
    public static float FitScale(HairEntry e, HairBaseEntry b, float widthFactor = CapWidthFactor)
        => e == null || b == null || e.CapSize.X <= 0.0001f
            ? 1f
            : Mathf.Clamp(b.CraniumSize.X * widthFactor / e.CapSize.X, MinScale, MaxScale);

    // 좌우·앞뒤는 귀 위쪽 중심에, 꼭대기는 정수리에 맞춘다. 크기가 달라지면 위치도
    // 달라지므로(원점이 AABB 중심이다) 둘을 따로 구할 수 있게 떼어 놓았다.
    public static Vector3 Place(HairEntry e, HairBaseEntry b, Transform3D headRest,
                                float yaw, float scale)
    {
        if (e == null || b == null) return Vector3.Zero;
        var rot = new Basis(Vector3.Up, Mathf.DegToRad(yaw));
        Vector3 cap = rot * (e.CapCenter * scale);
        // AABB 꼭대기가 아니라 **정수리 뚜껑의 꼭대기**다. 묶은 머리가 솟아 있어도
        // 머리카락이 얼굴까지 내려앉지 않는다.
        float topLocal = (e.CapCenter.Y + e.CapSize.Y * 0.5f) * scale;

        var inSkeleton = new Vector3(
            b.CraniumCenter.X - cap.X,
            b.SkullCenter.Y + b.SkullSize.Y * 0.5f + Lift - topLocal,
            b.CraniumCenter.Z - cap.Z);

        return headRest.Inverse() * inSkeleton;
    }

    // 사람이 조정한 값으로 만드는 최종 변환.
    public static Transform3D Compose(Vector3 pos, Vector3 rotDeg, Vector3 scale)
    {
        var basis = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(rotDeg.X), Mathf.DegToRad(rotDeg.Y), Mathf.DegToRad(rotDeg.Z)));
        return new Transform3D(basis.Scaled(scale), pos);
    }
}
