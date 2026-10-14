using Godot;

namespace NSP.Debug;

// 개발용 복귀 경로. DeveloperHub 를 통해 들어왔을 때만 올라오므로 평소 플레이에는 없다.
//
// **버튼은 기본적으로 그리지 않는다.** 화면 왼쪽 위에 흰 버튼이 떠 있으면 게임 화면을
// 그대로 보고 확인할 수가 없다(스크린샷마다 끼어든다). 허브로 돌아가는 길은 F10 하나로
// 충분하다 — DeveloperHub 가 직접 키를 받으므로 이 오버레이와 무관하게 동작한다.
// 굳이 버튼이 필요하면 실행 인자에 --devbutton 을 붙인다.
public partial class DevHubOverlay : CanvasLayer
{
    public event System.Action ReturnRequested;

    public override void _Ready()
    {
        Layer = 210;   // 자막 띠(112) · 건너뛰기 버튼(125) 보다 위
        if (!System.Array.Exists(OS.GetCmdlineArgs(), a => a == "--devbutton")) return;

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
