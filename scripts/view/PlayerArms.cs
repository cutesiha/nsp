using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 착석 관리자의 양팔/손. 카메라 자식으로 붙어 시점을 따라간다. 평소에는 화면 아래로
// 내려가 거의 안 보이고, 상호작용(전화/키보드/버튼/충격) 때만 올라온다.
// 상완·전완·손이 구분되는 인간 형태 — 막대기 두 개가 아니다.
//
// 포즈는 세 관절(어깨/팔꿈치/손목)의 각도로 정의하고 트윈으로 전환한다. 값은 첫 시안이라
// 에디터/코드에서 미세조정 대상이다. 게임 판정과 완전히 분리 — 여기서 게임 상태를 바꾸지 않는다.
public partial class PlayerArms : Node3D
{
    [Export] public float GestureSeconds = 0.32f;
    [Export] public Material SleeveMaterial;
    [Export] public Material SkinMaterial;

    private enum Pose { Idle, PhoneReach, PhoneHold, Typing, ButtonPress, DeskBrace }

    private readonly struct JointSet
    {
        public readonly Vector3 Shoulder, Elbow, Wrist;
        public JointSet(Vector3 s, Vector3 e, Vector3 w) { Shoulder = s; Elbow = e; Wrist = w; }
    }

    // (왼팔, 오른팔) 각각의 관절 각도(도). 오른팔이 전화/버튼을 담당한다.
    private static readonly Dictionary<Pose, (JointSet L, JointSet R)> Poses = new()
    {
        [Pose.Idle] = (
            new JointSet(new Vector3(-18, 10, 8), new Vector3(-35, 0, 0), new Vector3(0, 0, 0)),
            new JointSet(new Vector3(-18, -10, -8), new Vector3(-35, 0, 0), new Vector3(0, 0, 0))),
        [Pose.PhoneReach] = (
            new JointSet(new Vector3(-18, 10, 8), new Vector3(-35, 0, 0), new Vector3(0, 0, 0)),
            new JointSet(new Vector3(-72, 34, -6), new Vector3(-58, 0, 0), new Vector3(-10, 0, 0))),
        [Pose.PhoneHold] = (
            new JointSet(new Vector3(-18, 10, 8), new Vector3(-35, 0, 0), new Vector3(0, 0, 0)),
            new JointSet(new Vector3(-104, 8, -20), new Vector3(-96, 0, 0), new Vector3(-20, 20, 0))),
        [Pose.Typing] = (
            new JointSet(new Vector3(-62, 6, 6), new Vector3(-74, 0, 0), new Vector3(14, 0, 0)),
            new JointSet(new Vector3(-62, -6, -6), new Vector3(-74, 0, 0), new Vector3(14, 0, 0))),
        [Pose.ButtonPress] = (
            new JointSet(new Vector3(-18, 10, 8), new Vector3(-35, 0, 0), new Vector3(0, 0, 0)),
            new JointSet(new Vector3(-70, -22, -4), new Vector3(-52, 0, 0), new Vector3(20, 0, 0))),
        [Pose.DeskBrace] = (
            new JointSet(new Vector3(-78, 12, 10), new Vector3(-56, 0, 0), new Vector3(24, 0, 0)),
            new JointSet(new Vector3(-78, -12, -10), new Vector3(-56, 0, 0), new Vector3(24, 0, 0))),
    };

    private Node3D _lShoulder, _lElbow, _lWrist, _rShoulder, _rElbow, _rWrist;
    private Tween _tween;
    private double _typingUntil;
    private float _typeBob;

    public override void _Ready()
    {
        SleeveMaterial ??= MakeMat(new Color(0.09f, 0.09f, 0.11f), 0.7f);
        SkinMaterial ??= MakeMat(new Color(0.62f, 0.47f, 0.39f), 0.6f);

        (_lShoulder, _lElbow, _lWrist) = BuildArm(-1);
        (_rShoulder, _rElbow, _rWrist) = BuildArm(1);

        ApplyInstant(Pose.Idle);
    }

    private static StandardMaterial3D MakeMat(Color c, float rough) =>
        new() { AlbedoColor = c, Roughness = rough };

    // side: -1 왼팔 / +1 오른팔. 어깨는 카메라 로컬 기준 몸통 위치쯤.
    private (Node3D shoulder, Node3D elbow, Node3D wrist) BuildArm(int side)
    {
        var shoulder = new Node3D { Name = side < 0 ? "LeftArm" : "RightArm" };
        shoulder.Position = new Vector3(0.2f * side, -0.16f, -0.02f);
        AddChild(shoulder);

        var upper = Segment(0.24f, SleeveMaterial);
        shoulder.AddChild(upper);

        var elbow = new Node3D { Name = "Elbow", Position = new Vector3(0f, -0.24f, 0f) };
        shoulder.AddChild(elbow);

        var fore = Segment(0.22f, SleeveMaterial);
        elbow.AddChild(fore);

        var wrist = new Node3D { Name = "Wrist", Position = new Vector3(0f, -0.22f, 0f) };
        elbow.AddChild(wrist);

        BuildHand(wrist, side);
        return (shoulder, elbow, wrist);
    }

    // 관절(부모)의 원점이 위쪽 끝이 되도록 -Y로 내려 그린 캡슐.
    private MeshInstance3D Segment(float length, Material mat)
    {
        var mesh = new CapsuleMesh { Radius = 0.035f, Height = length, RadialSegments = 8, Rings = 4 };
        return new MeshInstance3D
        {
            Mesh = mesh,
            Position = new Vector3(0f, -length * 0.5f, 0f),
            MaterialOverride = mat,
        };
    }

    private void BuildHand(Node3D wrist, int side)
    {
        var palm = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.085f, 0.028f, 0.10f) },
            Position = new Vector3(0f, -0.05f, 0f),
            MaterialOverride = SkinMaterial,
        };
        wrist.AddChild(palm);

        for (int i = 0; i < 4; i++)
        {
            var finger = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.018f, 0.02f, 0.055f) },
                Position = new Vector3(-0.03f + i * 0.02f, -0.05f, -0.075f),
                MaterialOverride = SkinMaterial,
            };
            palm.AddChild(finger);
        }
        var thumb = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.02f, 0.02f, 0.045f) },
            Position = new Vector3(0.05f * side, -0.05f, -0.02f),
            RotationDegrees = new Vector3(0f, 35f * side, 0f),
            MaterialOverride = SkinMaterial,
        };
        palm.AddChild(thumb);
    }

    // --- 공개 제스처 ------------------------------------------------------

    public void PlayIdle() => TweenTo(Pose.Idle);
    public void PlayPhoneReach() => TweenTo(Pose.PhoneReach);
    public void PlayPhoneHold() => TweenTo(Pose.PhoneHold);
    public void PlayButtonPress()
    {
        TweenTo(Pose.ButtonPress);
        var t = CreateTween();
        t.TweenInterval(GestureSeconds + 0.12);
        t.TweenCallback(Callable.From(PlayIdle));
    }

    public void PlayTyping(float seconds = 0.9f)
    {
        _typingUntil = Time.GetTicksMsec() / 1000.0 + seconds;
        TweenTo(Pose.Typing);
    }

    public void PlayDeskBrace()
    {
        var t = CreateTween();
        t.SetParallel(false);
        SetPoseTween(t, Pose.DeskBrace, 0.06);
        t.TweenInterval(0.22);
        t.TweenCallback(Callable.From(PlayIdle));
    }

    // --- 내부 ------------------------------------------------------------

    private void TweenTo(Pose pose)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.SetParallel(true);
        SetPoseTween(_tween, pose, GestureSeconds);
    }

    private void SetPoseTween(Tween t, Pose pose, double dur)
    {
        var (l, r) = Poses[pose];
        t.TweenProperty(_lShoulder, "rotation_degrees", l.Shoulder, dur).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_lElbow, "rotation_degrees", l.Elbow, dur).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_lWrist, "rotation_degrees", l.Wrist, dur).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_rShoulder, "rotation_degrees", r.Shoulder, dur).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_rElbow, "rotation_degrees", r.Elbow, dur).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_rWrist, "rotation_degrees", r.Wrist, dur).SetTrans(Tween.TransitionType.Sine);
    }

    private void ApplyInstant(Pose pose)
    {
        var (l, r) = Poses[pose];
        _lShoulder.RotationDegrees = l.Shoulder; _lElbow.RotationDegrees = l.Elbow; _lWrist.RotationDegrees = l.Wrist;
        _rShoulder.RotationDegrees = r.Shoulder; _rElbow.RotationDegrees = r.Elbow; _rWrist.RotationDegrees = r.Wrist;
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_typingUntil > 0)
        {
            if (now >= _typingUntil)
            {
                _typingUntil = 0;
                PlayIdle();
            }
            else
            {
                // 손끝이 위아래로 톡톡 — 타건 느낌.
                _typeBob += (float)delta * 26f;
                float bob = Mathf.Sin(_typeBob) * 4f;
                _lWrist.RotationDegrees = new Vector3(14f + bob, 0f, 0f);
                _rWrist.RotationDegrees = new Vector3(14f - bob, 0f, 0f);
            }
        }
    }
}
