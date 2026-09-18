using Godot;

namespace NSP.Ui;

public partial class Phone : Control
{
    public static Phone Instance { get; private set; }

    [Signal]
    public delegate void RingFinishedEventHandler();

    // 3D 중앙제어실 레이어가 전화기 램프 점멸 / 팔 뻗기 연출을 시작하는 신호.
    [Signal]
    public delegate void RingStartedEventHandler();

    private AudioStreamPlayer _player;
    private Vector2 _restPosition;

    public override void _Ready()
    {
        Instance = this;
        _player = GetNode<AudioStreamPlayer>("RingPlayer");
        _restPosition = Position;
    }

    public void Ring()
    {
        Position = _restPosition;
        EmitSignal(SignalName.RingStarted);
        _player.Play();

        var rng = new RandomNumberGenerator();
        var tween = CreateTween();
        for (int i = 0; i < 12; i++)
        {
            Vector2 jitter = _restPosition + new Vector2(rng.RandfRange(-5f, 5f), rng.RandfRange(-4f, 4f));
            tween.TweenProperty(this, "position", jitter, 0.06);
        }
        tween.TweenProperty(this, "position", _restPosition, 0.06);
        tween.TweenCallback(Callable.From(() => EmitSignal(SignalName.RingFinished)));
    }
}
