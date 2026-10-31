using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.View;

// 화면 오른쪽 위의 「SKIP ≫」. 지금 흐르는 이야기를 **그 자리에서 끊고** 다음 할 일로 간다.
//
// 버튼은 책상 위 기기들과 같은 것을 쓴다(MonitorUi.Button) — 오른쪽 아래의
// 업무 · 로그 · 대화 기록과 같은 모양 · 같은 호버 · 같은 소리다. 게임 안의 화면에
// 엔진 기본 버튼이 혼자 다른 얼굴로 끼어들지 않게.
//
// **무엇을 하는가는 여기서 정하지 않는다.** ShiftFlowController.SkipStory() 가 단계마다
// 어디로 갈지 알고 있고(프롤로그 → DAY1 배치 · 교육/컷인 → 그 자리의 배치), 이 창은
// 그걸 부르고 버튼을 보이고 감추는 일만 한다. 도착점이 두 군데에 적히면 반드시 어긋난다.
public partial class StorySkipHud : CanvasLayer
{
    public static StorySkipHud Instance { get; private set; }

    private Button _button;

    // 화면 가장자리에서 띄우는 간격. ShiftHud 가 이 버튼 아래로 아이콘을 내릴 때도 쓴다.
    public const float EdgeMargin = 12f;
    public const float CornerHeight = EdgeMargin + 52f;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        // 자막(120) · 공포 연출(140) 위, 암전(160) 아래 — 암전 중에는 같이 덮인다.
        Layer = 150;
        ProcessMode = ProcessModeEnum.Always;   // 배치표 모달 · 일시정지 중에도 눌린다

        _button = MonitorUi.Button("SKIP  ≫", new Color(0.78f, 0.82f, 0.86f),
            ViewFont.Default, OnPressed, ViewFont.FS(17));
        _button.Name = "StorySkipButton";
        _button.TooltipText = "지금 나오는 이야기를 건너뜁니다";
        _button.CustomMinimumSize = new Vector2(132, 52);
        _button.FocusMode = Control.FocusModeEnum.None;
        _button.MouseFilter = Control.MouseFilterEnum.Stop;
        // 화면 오른쪽 위 모서리에 붙인다(2026-10-27 — 톱니 · Tab 아래에 떠 있던 자리가 애매했다).
        // 이 버튼이 떠 있는 동안에는 ShiftHud 가 톱니 · 패드 아이콘을 이 아래로 내린다.
        _button.AnchorLeft = 1f; _button.AnchorRight = 1f;
        _button.OffsetLeft = -(EdgeMargin + 132f); _button.OffsetRight = -EdgeMargin;
        _button.OffsetTop = EdgeMargin; _button.OffsetBottom = EdgeMargin + 52f;
        _button.Visible = false;
        AddChild(_button);
    }

    // 지금 끊을 이야기가 있는가.
    private static bool StoryOnScreen =>
        StoryCutinDirector.Instance is { IsPlaying: true }
        || TutorialDirector.Instance is { IsRunning: true }
        || PrologueDirector.Instance is { IsRunning: true };

    // 선택지가 떠 있으면 감춘다 — 고르는 것은 플레이어 몫이고, 건너뛰기가 대신
    // 누르면 이야기가 제멋대로 갈라진다.
    private static bool Choosing => StoryCutinHud.Instance is { IsChoosing: true };

    public bool ButtonVisible => _button?.Visible ?? false;

    // 창 좌표가 이 버튼 위인가.
    //
    // **PrologueAdvanceInput 이 반드시 물어봐야 한다.** 그쪽은 대사가 떠 있는 동안
    // 화면 아무 데나 누르면 넘기려고 _Input 단계에서 왼쪽 클릭을 통째로 먹는데,
    // _Input 은 GUI 보다 먼저 돈다 — 그래서 이 버튼은 **보이는 내내 눌리지 않았다.**
    // 버튼 위를 누른 것만 비켜 주면 된다.
    public bool ContainsPoint(Vector2 screenPos) =>
        _button is { Visible: true } && _button.GetGlobalRect().HasPoint(screenPos);

    private void OnPressed()
    {
        if (Choosing) return;
        var flow = GetTree()?.Root?.FindChild("ShiftFlowController", true, false) as ShiftFlowController
                   ?? FindFlow();
        if (flow?.SkipStory() == true) _button.Visible = false;
    }

    // 씬마다 이름이 다를 수 있어 타입으로도 한 번 찾는다.
    private ShiftFlowController FindFlow()
    {
        return Walk(GetTree()?.Root);

        static ShiftFlowController Walk(Node n)
        {
            if (n == null) return null;
            if (n is ShiftFlowController f) return f;
            foreach (var c in n.GetChildren())
                if (Walk(c) is { } found) return found;
            return null;
        }
    }

    public override void _Process(double delta)
    {
        if (_button == null) return;
        bool show = StoryOnScreen && !Choosing;
        if (_button.Visible != show) _button.Visible = show;
    }
}
