using Godot;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그/튜토리얼 대사 넘기기 입력.
//
// 대사는 절대 자동으로 넘어가지 않는다. 스페이스 / 엔터 / 마우스 왼쪽 클릭으로만 진행한다.
// 이 노드는 씬 루트(ControlRoom3DController)보다 먼저 _Input 을 받으므로, 배치표가 모달로
// 입력을 가져가는 단계에서도 대사를 넘길 수 있다.
//
// 마우스 클릭을 "화면 아무 곳이나"로 받는 것은 프롤로그 동안만이다. DAY0 교육 중에는
// 플레이어가 지도·배치표·전화기를 계속 클릭해야 하므로, 키(스페이스/엔터)로 넘기거나
// GUIDE-0 모니터를 직접 클릭하게 둔다.
public partial class PrologueAdvanceInput : Node
{
    public override void _Ready() => SetProcessInput(true);

    public override void _Input(InputEvent e)
    {
        bool byKey = e is InputEventKey { Pressed: true, Echo: false } k
                     && k.Keycode is Key.Space or Key.Enter or Key.KpEnter;
        bool byClick = e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
        if (!byKey && !byClick) return;

        // 통화 HUD / 로그 창이 떠 있으면 그쪽 입력이 우선이다.
        if (PhoneCallHud.Instance?.IsOpen == true || Day1HistoryOverlay.Instance?.IsWindowOpen == true) return;

        bool prologueRunning = PrologueDirector.Instance?.IsRunning == true;
        if (byClick && !prologueRunning) return;

        if (CutscenePlayer.Instance is { IsWaitingForInput: true } cut)
        {
            cut.RequestAdvance();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (GuideHologramView.Instance is { IsWaitingForInput: true } guide)
        {
            guide.RequestAdvance();
            GetViewport().SetInputAsHandled();
        }
    }
}
