using System.Linq;
using Godot;
using NSP.Core;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 도전과제 화면을 **실제 타이틀에서** 열어 보고 그림을 남긴다.
//
//   godot --path . res://scenes/debug/AchievementShot.tscn -- <저장 폴더>
//
// 헤드리스로는 그림이 나오지 않는다 — 창 모드로 돌려야 한다.
//
// 연출을 직접 그리지 않는다. 타이틀이 평소대로 뜨기를 기다렸다가 **키보드를 눌러**
// 「기록 열람」을 고른다(Space → ↓ → Enter). 그래야 실제로 플레이할 때와 같은 그림이 나온다.
//
// 저장 경로는 user://achievements_shot.cfg 로 돌려 둔다 — 실제 기록은 건드리지 않는다.
public partial class AchievementShot : Node
{
    private const string ShotStore = "user://achievements_shot.cfg";

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
        // 실제 기록 파일 대신 전용 파일을 쓴다. 그 위에 "몇 개는 달성, 몇 개는 미달성,
        // 숨김 하나는 공개" 상태를 만들어 세 가지 표시를 한 화면에서 볼 수 있게 한다.
        var m = AchievementManager.Instance;
        AchievementManager.Sandbox = false;
        AchievementManager.StorePath = ShotStore;
        m?.ClearForTest();
        foreach (string id in new[]
                 {
                     Achievements.FirstLaunch, Achievements.TutorialComplete, Achievements.FirstShift,
                     Achievements.OutgoingCall, Achievements.FirstLog, Achievements.MazeFailOnce,
                     Achievements.FirstFaint, Achievements.TutorialCore100,
                 })
            m?.Unlock(id);
        AchievementToast.Instance?.ClearForTest();

        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        GD.Print("\n\n################ 도전과제 기록실 · 달성 팝업 ################");

        // ① 타이틀 대기 화면 → 아무 키.
        for (int i = 0; i < 1200 && TitleTerminalView.Instance?.CurrentMode
                 != TitleTerminalView.Mode.Standby; i++) await Frame();
        await Seconds(0.8);
        Press(Key.Space);

        // ② 전원 투입 연출이 끝나 메뉴 조작을 받을 때까지 기다린다.
        // (표제가 다 뜬 것만 보고 키를 누르면 아직 Phase.PoweringOn 이라 그대로 무시된다.)
        for (int i = 0; i < 2400 && TitleRoomDirector.Instance?.AtMenu != true; i++) await Frame();
        await Seconds(0.6);
        Shot("01_타이틀_메뉴");

        // ③ ↓ 한 번 = 「기록 열람」, Enter.
        Press(Key.S);          // ↓ 와 같다. 방향키는 포커스된 Control 이 먼저 먹을 수 있어 글자 키를 쓴다
        await Seconds(0.3);
        GD.Print($"   커서 = {TitleTerminalView.Instance?.SelectedId}");
        Press(Key.Space);

        var view = AchievementArchiveView.Instance;
        for (int i = 0; i < 900 && view?.IsOpen != true; i++) await Frame();
        await Seconds(1.6);   // 왼쪽 CRT 확대가 끝나기를 기다린다
        Ok(view?.IsOpen == true, "「기록 열람」 → 도전과제 기록실이 열렸다");
        GD.Print($"   달성 {m?.GetUnlockedCount()} / {Achievements.Total}   페이지 {view?.Page + 1} / {view?.PageCount}");
        Shot("02_기록실_1페이지");
        ShotCrt("02_기록실_1페이지_CRT");

        // ④ 페이지를 넘겨 본다 — 숨김 미달성(???)이 있는 쪽까지.
        for (int p = 2; p <= (view?.PageCount ?? 1); p++)
        {
            Press(Key.Right);
            await Seconds(0.5);
            ShotCrt($"03_기록실_{p}페이지_CRT");
        }
        Ok(view?.Page == (view?.PageCount ?? 1) - 1, "마지막 페이지까지 넘어갔다");

        // ⑤ ESC → 타이틀 메뉴로 복귀.
        Press(Key.Escape);
        for (int i = 0; i < 600 && view?.IsOpen != false; i++) await Frame();
        await Seconds(1.4);
        Ok(view?.IsOpen == false, "ESC 로 타이틀 메뉴로 돌아왔다");
        Ok(TitleTerminalView.Instance?.CurrentMode == TitleTerminalView.Mode.Menu, "메뉴 화면이 다시 떴다");
        Shot("04_복귀");

        // ⑥ 달성 팝업 — 오른쪽 아래.
        m?.Unlock(Achievements.GhostDispelled);
        for (int i = 0; i < 300 && AchievementToast.Instance?.IsShowing != true; i++) await Frame();
        await Seconds(0.45);
        var toast = AchievementToast.Instance;
        Ok(toast?.CardVisible == true, $"팝업이 떴다 — “{toast?.ShownTitle}”");
        Shot("05_달성팝업");
        await Seconds(0.5);
        Shot("06_달성팝업_유지");

        // 사라지는지도 본다(3.4초 유지 + 0.3초 페이드).
        await Seconds(4.0);
        Ok(toast?.IsShowing == false, "팝업이 저절로 사라졌다");
        Shot("07_팝업_사라짐");

        GD.Print($"\nsaved → {_dir}");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ShotStore));
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
        img.SavePng($"{_dir}/ach_{_shot:00}_{name}.png");
        _shot++;
    }

    // 왼쪽 CRT 안쪽만 — 3D 화면에서는 작아서 글자 크기를 볼 수 없다.
    private void ShotCrt(string name)
    {
        var tex = ControlRoom3DController.Instance?.TitleTerminalViewport?.GetTexture();
        tex?.GetImage()?.SavePng($"{_dir}/ach_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
