using Godot;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그/튜토리얼 대사 넘기기 입력.
//
// 대사는 절대 자동으로 넘어가지 않는다. 스페이스 / 엔터 / 마우스 왼쪽 클릭으로만 진행한다.
// 이 노드는 씬 루트(ControlRoom3DController)보다 먼저 _Input 을 받으므로, 배치표가 모달로
// 입력을 가져가는 단계에서도 대사를 넘길 수 있다.
//
// 대사가 떠 있는 동안에는 화면 아무 곳이나 클릭해도 넘어간다. 대사 묶음이 끝나면
// IsWaitingForInput 이 false 가 되므로, 플레이어가 지도·배치표·전화기를 클릭하는 데는
// 전혀 방해가 되지 않는다.
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

        // 벨이 울리는 동안에는 클릭을 먹지 않는다. 그러지 않으면 대사가 아직 흐르는 중에
        // 수화기를 눌러도 "대사 넘기기"로만 먹혀 전화를 받을 수 없다(키로는 그대로 넘어간다).
        if (byClick && Phone3D.Instance?.IsRinging == true) return;

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
