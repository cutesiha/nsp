using Godot;

namespace NSP.Debug;

// 실제 작업실 안에 새 3D 직원을 세워 크기만 눈으로 확인하는 씬.
// 카메라 위치 · 시선 · FOV 와 직원이 서는 자리는 FacilityCctvWorld 가 게임에서 쓰는 값을 그대로 복사했다
// (그쪽 코드는 건드리지 않는다 — 값이 바뀌면 여기 상수도 같이 고쳐야 한다).
//
//   1  CCTV 시점      2  인터뷰 시점
//   3  기존 원통 플레이스홀더 켜기/끄기 (크기 비교용)
//   SPACE  test_pose  ·  R  기본 자세
public partial class EmployeeInRoomPreview : Node3D
{
    // FacilityCctvWorld 의 기본값과 같아야 한다.
    private static readonly Vector3 CctvPos = new(3.5f, 3.05f, 3.5f);
    private static readonly Vector3 CctvLook = new(-0.4f, 0.5f, -0.5f);
    private static readonly Vector3 InterviewPos = new(4.3f, 1.62f, 1.15f);
    private static readonly Vector3 InterviewLook = new(-0.3f, 1.15f, -0.55f);
    // FacilityCctvWorld.Slots[1] — 비교용 플레이스홀더를 세울 자리.
    private static readonly Vector3 PlaceholderSlot = new(0.9f, 0f, -1.2f);

    private Camera3D _cctv, _interview;
    private Node3D _placeholder;
    private AnimationPlayer _animM, _animF;
    private Label _hint;

    public override void _Ready()
    {
        _cctv = GetNodeOrNull<Camera3D>("CctvCamera");
        _interview = GetNodeOrNull<Camera3D>("InterviewCamera");
        // 씬에 적힌 회전값 대신 게임과 똑같이 LookAt 으로 다시 맞춘다.
        Aim(_cctv, CctvPos, CctvLook);
        Aim(_interview, InterviewPos, InterviewLook);
        if (_cctv != null) _cctv.Current = true;

        _animM = GetNodeOrNull<AnimationPlayer>("EmployeeCctvBase_M/AnimationPlayer");
        _animF = GetNodeOrNull<AnimationPlayer>("EmployeeCctvBase_F/AnimationPlayer");

        // 지금 게임에 들어가 있는 임시 원통 직원 — 새 모델이 얼마나 커졌는지 옆에 놓고 본다.
        var ps = GD.Load<PackedScene>("res://scenes/props/employee_placeholder.tscn");
        if (ps != null)
        {
            _placeholder = ps.Instantiate<Node3D>();
            AddChild(_placeholder);
            _placeholder.Position = PlaceholderSlot;
            _placeholder.RotationDegrees = new Vector3(0, 135, 0);
        }

        var layer = new CanvasLayer();
        AddChild(layer);
        _hint = new Label { Position = new Vector2(20, 16) };
        _hint.AddThemeFontSizeOverride("font_size", 18);
        _hint.AddThemeColorOverride("font_color", new Color(0.9f, 0.93f, 0.96f));
        layer.AddChild(_hint);
        UpdateHint();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Key1: if (_cctv != null) _cctv.Current = true; break;
            case Key.Key2: if (_interview != null) _interview.Current = true; break;
            case Key.Key3:
                if (_placeholder != null) _placeholder.Visible = !_placeholder.Visible;
                UpdateHint();
                break;
            case Key.Space:
                bool play = !(_animM?.IsPlaying() ?? false);
                foreach (var a in new[] { _animM, _animF })
                {
                    if (a == null) continue;
                    if (play) a.Play("test_pose");
                    else a.Pause();
                }
                break;
            case Key.R:
                foreach (var a in new[] { _animM, _animF })
                {
                    a?.Stop();
                    a?.Play("test_pose");
                    a?.Seek(0, true);
                    a?.Pause();
                }
                break;
        }
    }

    private void UpdateHint()
    {
        string ph = _placeholder == null ? "없음" : (_placeholder.Visible ? "켬" : "끔");
        _hint.Text = $"1 CCTV 시점   2 인터뷰 시점   3 기존 플레이스홀더({ph})   SPACE test_pose   R 기본 자세\n"
                   + $"천장 높이 3.25m · 방 6×6m · M 1.95m / F 1.68m";
    }

    private static void Aim(Camera3D cam, Vector3 pos, Vector3 look)
    {
        if (cam == null) return;
        cam.GlobalPosition = pos;
        cam.LookAt(look, Vector3.Up);
    }
}
