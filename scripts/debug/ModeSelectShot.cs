using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// 타이틀의 「근무 유형 선택」과 일시정지 창의 「도전과제」를 실제로 띄워 보고 그림을 남긴다.
//
//   godot --path . res://scenes/debug/ModeSelectShot.tscn -- <저장 폴더>
//
// 헤드리스로는 그림이 나오지 않는다 — 창 모드로 돌려야 한다.
// 연출을 직접 그리지 않고 **키보드를 눌러** 실제 플레이와 같은 길로 들어간다.
public partial class ModeSelectShot : Node
{
    private string _dir = "";
    private int _shot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        // 캡처가 실제 도전과제 기록 파일을 건드리지 않게 한다(읽기는 이미 끝났고, 쓰기만 막는다).
        // 그 위에 몇 개를 메모리로만 해금해 달성/미달성이 같이 보이는 화면을 만든다.
        AchievementManager.Sandbox = true;
        foreach (string id in new[]
                 {
                     Achievements.FirstLaunch, Achievements.TutorialComplete,
                     Achievements.FirstShift, Achievements.RepairRequestIgnored,
                 })
            AchievementManager.Instance?.Unlock(id);

        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        GD.Print("\n\n################ 근무 유형 선택 · 일시정지 도전과제 ################");

        // ① 타이틀 대기 → 아무 키 → 메뉴 조작 가능해질 때까지.
        for (int i = 0; i < 1200 && TitleTerminalView.Instance?.CurrentMode
                 != TitleTerminalView.Mode.Standby; i++) await Frame();
        await Seconds(0.8);
        Press(Key.Space);
        for (int i = 0; i < 2400 && TitleRoomDirector.Instance?.AtMenu != true; i++) await Frame();
        await Seconds(0.6);

        // ② 「근무 개시」(커서 0번)에서 바로 ENTER.
        GD.Print($"   커서 = {TitleTerminalView.Instance?.SelectedId}");
        Press(Key.Space);
        await Seconds(0.8);
        var term = TitleTerminalView.Instance;
        Ok(term?.CurrentMode == TitleTerminalView.Mode.ModeSelect, "근무 개시 → 근무 유형 선택 화면");
        GD.Print($"   선택 = {term?.SelectedMode}   ({GameModes.DisplayName(term?.SelectedMode ?? GameMode.Standard)})");
        ShotCrt("01_모드선택_대회용");

        // ③ ↓ 로 기본 모드 → 하드 모드.
        Press(Key.S);
        await Seconds(0.4);
        Ok(term?.SelectedMode == GameMode.Standard, "↓ 한 번 = 기본 모드");
        ShotCrt("02_모드선택_기본");

        Press(Key.S);
        await Seconds(0.4);
        Ok(term?.SelectedMode == GameMode.Hard, "↓ 두 번 = 하드 모드(잠김)");
        ShotCrt("03_모드선택_하드잠김");

        // ④ 잠긴 항목에서 ENTER — 아무 일도 일어나지 않아야 한다.
        Press(Key.Space);
        await Seconds(0.8);
        Ok(term?.CurrentMode == TitleTerminalView.Mode.ModeSelect,
            "하드 모드는 ENTER 를 눌러도 시작되지 않는다");
        Ok(GameState.Instance?.CurrentPhase == NSP.Data.GamePhase.Prep,
            "게임 상태도 그대로다 (선택 화면이 떴다고 초기화되지 않는다)");

        // ⑤ ESC → 타이틀 메뉴 복귀.
        Press(Key.Escape);
        await Seconds(0.8);
        Ok(term?.CurrentMode == TitleTerminalView.Mode.Menu, "ESC 로 타이틀 메뉴 복귀");
        Ok(TitleRoomDirector.Instance?.AtMenu == true, "메뉴 조작이 다시 살아 있다");
        Shot("04_타이틀_복귀");

        // ⑥ 일시정지 창 — 카드 목록과 도전과제 창.
        var pause = new PauseMenu();
        AddChild(pause);
        await Frame();
        pause.Open();
        await Seconds(0.5);
        Shot("05_일시정지_카드");
        GetTree().Paused = false;   // 캡처가 멈추지 않게 바로 푼다

        var panel = new AchievementPanel();
        AddChild(panel);
        await Frame();
        panel.Open();
        await Seconds(0.6);
        Shot("06_일시정지_도전과제");
        for (int i = 0; i < 8; i++) { Press(Key.S); await Frame(); }
        await Seconds(0.5);
        Shot("07_일시정지_도전과제_스크롤");
        panel.Close();
        pause.Close();

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private static void Press(Key code)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = false });
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        img.SavePng($"{_dir}/mode_{_shot:00}_{name}.png");
        _shot++;
    }

    // 왼쪽 CRT 안쪽만 — 3D 화면에서는 작아서 글자를 볼 수 없다.
    private void ShotCrt(string name)
    {
        var tex = ControlRoom3DController.Instance?.TitleTerminalViewport?.GetTexture();
        tex?.GetImage()?.SavePng($"{_dir}/mode_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
