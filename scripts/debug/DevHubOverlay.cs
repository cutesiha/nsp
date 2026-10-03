using Godot;

namespace NSP.Debug;

// 실행 중인 게임 화면 위에 얹히는 개발용 복귀 버튼.
// DeveloperHub 를 통해 들어왔을 때만 만들어지므로, 평소 플레이에는 존재하지 않는다.
public partial class DevHubOverlay : CanvasLayer
{
    public event System.Action ReturnRequested;

    public override void _Ready()
    {
        Layer = 210;   // 자막 띠(112) · 건너뛰기 버튼(125) 보다 위
        var b = new Button
        {
            Text = "◀ DEV HUB  (F10)",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        b.AnchorLeft = 0f; b.AnchorTop = 0f;
        b.OffsetLeft = 16; b.OffsetTop = 16; b.OffsetRight = 176; b.OffsetBottom = 48;
        b.Modulate = new Color(1f, 1f, 1f, 0.75f);
        b.Pressed += () => ReturnRequested?.Invoke();
        AddChild(b);
    }

    // 개발 중 창을 그냥 닫는 경우에도 user:// 진행 기록은 되돌려 둔다.
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) DebugEntryPoint.RestoreEndingState();
    }
}
