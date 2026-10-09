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
        GD.Print($"   달성 {m?.GetUnlockedCount()} / {Achievements.Total}   " +
                 $"스크롤 {view?.Scroll:0} / {AchievementArchiveView.MaxScroll:0}");
        Shot("02_기록실_맨위");
        ShotCrt("02_기록실_맨위_CRT");

        // ④ ↓ 로 끝까지 내려가며 몇 장 남긴다 — 숨김 미달성(???)이 있는 쪽까지.
        for (int step = 1; step <= 6; step++)
        {
            for (int i = 0; i < 5; i++) { Press(Key.S); await Frame(); }
            await Seconds(0.35);
            ShotCrt($"03_기록실_스크롤{step}_CRT");
        }
        Ok(view?.Cursor == Achievements.Total - 1, $"↓ 만으로 마지막 줄까지 내려갔다 (커서 {view?.Cursor})");
        Ok(view != null && Mathf.IsEqualApprox(view.Scroll, AchievementArchiveView.MaxScroll),
            "목록이 끝까지 스크롤됐다");

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
        var toast = AchievementToast.Instance;

        // 올라오는 동안을 촘촘히 찍는다 — 제자리보다 한 번 더 솟았다가 내려앉는
        // "뽀잉" 은 정지 화면 한 장으로는 확인할 수 없다.
        GD.Print($"   제자리 높이 {toast?.CardRestTop:0.0}");
        for (int i = 0; i < 9; i++)
        {
            GD.Print($"   올라옴 {i} — 높이 {toast?.CardTop,7:0.0}");
            Shot($"05_올라옴_{i}");
            await Seconds(0.05);
        }
        await Seconds(0.3);
        Ok(toast?.CardVisible == true, $"팝업이 떴다 — “{toast?.ShownTitle}”");
        Shot("06_달성팝업_제자리");

        // 퇴장 — 위로 살짝 튕긴 뒤 아래로 빠지는지 본다.
        //
        // **화면 저장을 섞지 않는다.** 1920×1080 PNG 한 장이 0.2초쯤 걸려서, 0.13초짜리
        // 튕김이 두 장 사이로 빠져나간다(실제로 그래서 놓쳤다). 매 프레임 높이만 읽어
        // 그동안의 **가장 높은 지점**을 적는다 — 제자리보다 위로 올라갔으면 튕긴 것이다.
        float top = toast?.CardTop ?? 0f;
        float peak = top, bottom = top;
        for (int i = 0; i < 260 && toast?.IsShowing == true; i++)
        {
            await Frame();
            top = toast?.CardTop ?? top;
            peak = Mathf.Min(peak, top);     // 값이 작을수록 위
            bottom = Mathf.Max(bottom, top);
        }
        GD.Print($"   퇴장 — 제자리 {toast?.CardRestTop:0.0} · 가장 높이 {peak:0.0} · 가장 아래 {bottom:0.0}");
        Ok(peak < (toast?.CardRestTop ?? 0f) - 2f, $"내려가기 전에 위로 튕긴다 ({peak:0.0})");
        Ok(bottom > (toast?.CardRestTop ?? 0f) + 20f, $"그 뒤 아래로 빠진다 ({bottom:0.0})");
        Shot("07_퇴장후");

        Ok(toast?.IsShowing == false, "팝업이 저절로 사라졌다");
        Shot("08_팝업_사라짐");

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
