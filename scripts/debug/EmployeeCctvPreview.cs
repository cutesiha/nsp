using Godot;

namespace NSP.Debug;

// CCTV 직원 캐릭터 작업용 미리보기. 게임 로직과는 완전히 분리되어 있다.
// 씬 안의 6명은 실제 variant 씬(FoxEmployee3D.tscn 등)을 instance 한 것이므로,
// 그 씬을 고치면 여기 보이는 모습도 같이 바뀐다.
//
//   1  6명 전신 비교      2  CCTV 거리/각도      3  가면·머리 가까이
//   스페이스  test_pose 전원 재생/정지 — 관절 pivot 확인용
//   R        기본 자세로 되돌리기
//   L        이름표 켜기/끄기
public partial class EmployeeCctvPreview : Node3D
{
    [Export] public NodePath FullBodyCameraPath = "FullBodyCamera";
    [Export] public NodePath CctvCameraPath = "CctvCamera";
    [Export] public NodePath CloseCameraPath = "CloseCamera";

    private Camera3D _full, _cctv, _close;
    private readonly System.Collections.Generic.List<AnimationPlayer> _anims = new();
    private readonly System.Collections.Generic.List<Label3D> _labels = new();
    private Label _hint;

    public override void _Ready()
    {
        _full = GetNodeOrNull<Camera3D>(FullBodyCameraPath);
        _cctv = GetNodeOrNull<Camera3D>(CctvCameraPath);
        _close = GetNodeOrNull<Camera3D>(CloseCameraPath);
        if (_full != null) _full.Current = true;

        Collect(this);

        var layer = new CanvasLayer();
        AddChild(layer);
        _hint = new Label
        {
            Position = new Vector2(20, 16),
            Text = "1 전신 비교   2 CCTV 시점   3 가까이   SPACE test_pose   R 기본 자세   L 이름표\n"
                 + "왼쪽부터  여우 · 강아지 · 고양이 · 양 · 토끼 · 늑대",
        };
        _hint.AddThemeFontSizeOverride("font_size", 18);
        _hint.AddThemeColorOverride("font_color", new Color(0.9f, 0.93f, 0.96f));
        layer.AddChild(_hint);
    }

    private void Collect(Node n)
    {
        if (n is AnimationPlayer a) _anims.Add(a);
        if (n is Label3D l) _labels.Add(l);
        foreach (var c in n.GetChildren()) Collect(c);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Key1: if (_full != null) _full.Current = true; break;
            case Key.Key2: if (_cctv != null) _cctv.Current = true; break;
            case Key.Key3: if (_close != null) _close.Current = true; break;
            case Key.Space:
                bool play = _anims.Count > 0 && !_anims[0].IsPlaying();
                foreach (var a in _anims) { if (play) a.Play("test_pose"); else a.Pause(); }
                break;
            case Key.R:
                foreach (var a in _anims) { a.Stop(); a.Play("test_pose"); a.Seek(0, true); a.Pause(); }
                break;
            case Key.L:
                foreach (var l in _labels) l.Visible = !l.Visible;
                break;
        }
    }
}
