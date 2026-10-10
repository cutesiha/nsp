using System.Threading.Tasks;
using Godot;

namespace NSP.View;

// 눈을 감았다 뜨는 연출. 위·아래에서 검정이 가운데로 모였다가, 다시 위·아래로 물러난다.
//
// 화면 전체를 덮는 단순한 암전과 다르다 — 암전은 "장면이 끊겼다", 이건 "이 사람이
// 눈을 감았다" 로 읽힌다. 가상 시뮬레이션이 끝나고 실제 근무가 시작되는 그 한 번에만 쓴다.
//
// 상태를 갖지 않는다. 부를 때 만들어 쓰고, 끝나면 스스로 사라진다.
public partial class EyelidOverlay : CanvasLayer
{
    private ColorRect _top, _bottom;

    // 감았다 뜨기. hold 동안 완전히 감긴 채로 있는다.
    public static async Task Blink(Node host, float close = 0.7f, float hold = 0.5f, float open = 0.9f)
    {
        var lid = Attach(host);
        if (lid == null) return;
        await lid.Close(close);
        if (hold > 0f) await lid.Hold(hold);
        await lid.Open(open);
        lid.Done();
    }

    // 감은 **동안** 다른 일을 해야 할 때는 이쪽을 쓴다.
    //   var lid = EyelidOverlay.Attach(this);
    //   await lid.Close(0.7f);   … 화면을 바꾸고 …   await lid.Open(0.9f);   lid.Done();
    public static EyelidOverlay Attach(Node host)
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return null;
        var lid = new EyelidOverlay();
        host.AddChild(lid);
        return lid;
    }

    // 눈꺼풀은 끝에서 빨리 닫히므로 뒤로 갈수록 빨라지게 둔다.
    public async Task Close(float seconds = 0.7f)
    {
        var size = GetViewport().GetVisibleRect().Size;
        Set(0f, size);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Sweep(0f, size.Y * 0.5f + 2f, seconds, Tween.TransitionType.Sine, Tween.EaseType.In, size);
    }

    public async Task Hold(float seconds)
    {
        if (seconds <= 0f) return;
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    // 뜰 때는 천천히.
    public async Task Open(float seconds = 0.9f)
    {
        var size = GetViewport().GetVisibleRect().Size;
        await Sweep(size.Y * 0.5f + 2f, 0f, seconds, Tween.TransitionType.Sine, Tween.EaseType.Out, size);
    }

    public void Done()
    {
        if (GodotObject.IsInstanceValid(this)) QueueFree();
    }

    public override void _Ready()
    {
        // 암전(TitleOverlay) 과 배너보다 위. 눈꺼풀은 무엇보다 앞이다.
        Layer = 128;
        ProcessMode = ProcessModeEnum.Always;

        _top = MakeLid();
        _bottom = MakeLid();
        AddChild(_top);
        AddChild(_bottom);
    }

    private static ColorRect MakeLid() => new()
    {
        Color = Colors.Black,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        AnchorRight = 1f,
    };

    private async Task Sweep(float from, float to, float seconds,
        Tween.TransitionType trans, Tween.EaseType ease, Vector2 size)
    {
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(v => Set(v, size)), from, to, seconds)
            .SetTrans(trans).SetEase(ease);
        await ToSignal(t, Tween.SignalName.Finished);
    }

    // amount = 위·아래에서 각각 덮은 높이(픽셀).
    private void Set(float amount, Vector2 size)
    {
        if (_top == null || _bottom == null) return;
        _top.Position = new Vector2(0f, 0f);
        _top.Size = new Vector2(size.X, amount);
        _bottom.Position = new Vector2(0f, size.Y - amount);
        _bottom.Size = new Vector2(size.X, amount);
    }
}
