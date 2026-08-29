using Godot;

namespace NSP.View;

// 착석 고정 1인칭 시점. FPS 마우스룩은 없다(게임 조작은 모니터 위 마우스).
//  - 평상시: 사람이 앉아 숨 쉬는 정도의 미세 idle 모션.
//  - 모니터 클릭: FocusOnScreen()으로 카메라가 그 CRT 앞으로 0.3초 이동(가독성/드래그 편의).
//    ReturnToSeat()로 자리로 복귀.
//  - 상호작용(전화 등): FocusOn()으로 시선만 살짝 이동, ClearFocus()로 복귀.
//  - Shake()로 공포 충격 시 짧게 흔들림.
public partial class SeatedCameraRig : Node3D
{
    [Export] public bool IdleMotionEnabled = true;
    [Export] public float BreathBobMeters = 0.004f;
    [Export] public float BreathSwayDegrees = 0.15f;
    [Export] public float BreathPeriodSeconds = 5.5f;

    [Export] public float MaxFocusYawDegrees = 7f;
    [Export] public float MaxFocusPitchDegrees = 5f;

    private const float Rad2Deg = 57.29578f;

    private Camera3D _camera;
    private float _elapsed;

    private Vector3 _seatPos, _seatRotDeg;
    private Vector3 _basePos, _baseRotDeg;      // 현재 목표(자리 or CRT 앞), 트윈 대상
    private bool _zoomed;
    private Tween _baseTween;

    private Vector3 _focusDegrees;
    private float _focusWeight;
    private Tween _focusTween;

    private Vector3 _shakeOffset;
    private Tween _shakeTween;

    public bool IsZoomed => _zoomed;

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera3D>("Camera3D");
        _seatPos = _basePos = Position;
        _seatRotDeg = _baseRotDeg = RotationDegrees;
    }

    public override void _Process(double delta)
    {
        Vector3 pos = _basePos;
        Vector3 rot = _baseRotDeg;

        if (IdleMotionEnabled && !_zoomed)
        {
            _elapsed += (float)delta;
            float phase = _elapsed / Mathf.Max(0.1f, BreathPeriodSeconds) * Mathf.Tau;
            pos += new Vector3(Mathf.Sin(phase * 0.5f) * BreathBobMeters * 0.6f,
                               Mathf.Sin(phase) * BreathBobMeters, 0f);
            rot += new Vector3(Mathf.Sin(phase + 1.3f) * BreathSwayDegrees,
                               Mathf.Sin(phase * 0.37f) * BreathSwayDegrees * 0.7f, 0f);
        }

        rot += _focusDegrees * _focusWeight + _shakeOffset;
        Position = pos;
        RotationDegrees = rot;
    }

    // --- CRT 앞으로 확대 --------------------------------------------------

    public void FocusOnScreen(Vector3 screenCenterWorld, Vector3 screenNormalWorld, float distance, float dur = 0.32f)
    {
        if (_camera == null) return;
        Vector3 camPos = screenCenterWorld + screenNormalWorld.Normalized() * distance;
        var camWorld = new Transform3D(Basis.Identity, camPos).LookingAt(screenCenterWorld, Vector3.Up);
        Transform3D rigWorld = camWorld * _camera.Transform.AffineInverse();
        _zoomed = true;
        _focusWeight = 0f;
        TweenBaseTo(rigWorld.Origin, rigWorld.Basis.GetEuler() * Rad2Deg, dur);
    }

    public void ReturnToSeat(float dur = 0.3f)
    {
        _zoomed = false;
        TweenBaseTo(_seatPos, _seatRotDeg, dur);
    }

    private void TweenBaseTo(Vector3 pos, Vector3 rotDeg, float dur)
    {
        _baseTween?.Kill();
        _baseTween = CreateTween();
        _baseTween.SetParallel(true);
        _baseTween.TweenMethod(Callable.From<Vector3>(v => _basePos = v), _basePos, pos, dur)
            .SetTrans(Tween.TransitionType.Sine);
        _baseTween.TweenMethod(Callable.From<Vector3>(v => _baseRotDeg = v), _baseRotDeg, rotDeg, dur)
            .SetTrans(Tween.TransitionType.Sine);
    }

    // --- 시선만 살짝 (전화/공포 사전징후) -------------------------------

    public void FocusOn(Vector3 worldTarget, float durationSeconds = 0.35f)
    {
        if (_camera == null) return;
        Vector3 local = _camera.GlobalTransform.AffineInverse() * worldTarget;
        float depth = Mathf.Max(0.05f, -local.Z);
        float yaw = Mathf.RadToDeg(Mathf.Atan2(local.X, depth));
        float pitch = Mathf.RadToDeg(Mathf.Atan2(local.Y, depth));

        _focusDegrees = new Vector3(
            Mathf.Clamp(pitch, -MaxFocusPitchDegrees, MaxFocusPitchDegrees),
            Mathf.Clamp(-yaw, -MaxFocusYawDegrees, MaxFocusYawDegrees),
            0f);

        _focusTween?.Kill();
        _focusTween = CreateTween();
        _focusTween.TweenMethod(Callable.From<float>(v => _focusWeight = v), _focusWeight, 1f, durationSeconds)
            .SetTrans(Tween.TransitionType.Sine);
    }

    public void ClearFocus(float durationSeconds = 0.4f)
    {
        _focusTween?.Kill();
        _focusTween = CreateTween();
        _focusTween.TweenMethod(Callable.From<float>(v => _focusWeight = v), _focusWeight, 0f, durationSeconds)
            .SetTrans(Tween.TransitionType.Sine);
    }

    // --- 공포 충격 ------------------------------------------------------

    public void Shake(float strengthDegrees = 2.4f, float seconds = 0.5f)
    {
        _shakeTween?.Kill();
        _shakeTween = CreateTween();
        var rng = new RandomNumberGenerator();
        int steps = Mathf.Max(3, Mathf.RoundToInt(seconds / 0.05f));
        for (int i = 0; i < steps; i++)
        {
            float decay = 1f - (float)i / steps;
            Vector3 to = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-0.5f, 0.5f))
                         * strengthDegrees * decay;
            _shakeTween.TweenMethod(Callable.From<Vector3>(v => _shakeOffset = v), _shakeOffset, to, 0.05);
        }
        _shakeTween.TweenMethod(Callable.From<Vector3>(v => _shakeOffset = v), _shakeOffset, Vector3.Zero, 0.08);
    }
}
