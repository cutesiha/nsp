using Godot;

namespace NSP.View;

// 직원이 손에 드는 스마트폰 소품. **표현 전용이다.**
//
// 메시와 머티리얼은 **한 벌만 만들어 여섯 명이 공유한다**(저사양 노트북 — 지시서 §15).
// 캐릭터마다 MeshInstance3D 하나를 손 노드에 붙여 두고, 쓸 때만 Visible 을 켠다.
// 매 프레임 새로 만들지 않고, 손이 바뀌면(왼손 ↔ 오른손) 부모만 옮긴다.
//
// 손 노드 기준 방향: 손가락이 -Y, 손바닥이 -Z(클립보드·렌치와 같은 기준).
// 그래서 휴대폰은 손가락 쪽으로 조금 내려가고 손바닥 앞(-Z)에 놓인다 — 화면이 얼굴을 향한다.
public static class EmployeePhoneProp
{
    private const float BodyW = 0.070f, BodyH = 0.142f, BodyD = 0.009f;

    private static BoxMesh _body;
    private static BoxMesh _screen;
    private static StandardMaterial3D _bodyMat;
    private static StandardMaterial3D _screenMat;

    private static readonly Vector3 HandOffset = new(0.004f, -0.072f, -0.030f);
    private static readonly Vector3 HandRotation = new(-8f, 0f, 0f);

    private static void EnsureShared()
    {
        _body ??= new BoxMesh { Size = new Vector3(BodyW, BodyH, BodyD) };
        _screen ??= new BoxMesh { Size = new Vector3(BodyW - 0.009f, BodyH - 0.014f, 0.002f) };
        _bodyMat ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.11f, 0.13f), Metallic = 0.35f, Roughness = 0.45f,
        };
        // 약하게 빛나는 화면 — CCTV 의 어두운 화면에서도 "폰을 보고 있다" 가 읽혀야 한다.
        _screenMat ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.58f, 0.70f, 0.82f),
            EmissionEnabled = true,
            Emission = new Color(0.60f, 0.76f, 0.95f),
            EmissionEnergyMultiplier = 2.6f,
            Roughness = 0.2f,
        };
    }

    // 그 직원의 휴대폰을 원하는 손에 붙여 돌려준다(처음 한 번만 만든다). 손이 없으면 null.
    public static MeshInstance3D Ensure(Node3D owner, ref MeshInstance3D cached, string side)
    {
        var hand = Hand(owner, side);
        if (hand == null) return null;
        if (cached != null && GodotObject.IsInstanceValid(cached))
        {
            if (cached.GetParent() != hand)
            {
                cached.GetParent()?.RemoveChild(cached);
                hand.AddChild(cached);
                Place(cached, side);
            }
            return cached;
        }

        EnsureShared();
        var phone = new MeshInstance3D { Name = "Phone", Mesh = _body, MaterialOverride = _bodyMat };
        var screen = new MeshInstance3D { Mesh = _screen, MaterialOverride = _screenMat };
        // 손바닥 쪽(-Z) 면이 화면이다.
        screen.Position = new Vector3(0f, 0f, -BodyD / 2f - 0.0011f);
        phone.AddChild(screen);
        hand.AddChild(phone);
        Place(phone, side);
        cached = phone;
        return phone;
    }

    private static void Place(Node3D phone, string side)
    {
        float mirror = side == "L" ? -1f : 1f;
        phone.Position = HandOffset with { X = HandOffset.X * mirror };
        phone.RotationDegrees = HandRotation with { Z = HandRotation.Z * mirror };
    }

    public static void Show(MeshInstance3D phone, bool on)
    {
        if (phone != null && GodotObject.IsInstanceValid(phone) && phone.Visible != on) phone.Visible = on;
    }

    private static Node3D Hand(Node3D owner, string side) =>
        owner?.GetNodeOrNull<Node3D>(side == "L"
            ? "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderL/UpperArmL/LowerArmL/HandL"
            : "VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderR/UpperArmR/LowerArmR/HandR");
}
