using Godot;
using NSP.Core;

namespace NSP.View;

// 게임을 켤 때 한 번 뜨는 경고 화면 — 광과민성 발작 · 공포 · 유혈 묘사.
//
// 공개 대회 · 출시 빌드에는 사실상 필수다. 붉은 점멸(직원 사망) · 화면 섬광 · 흔들림이 있고,
// 그 세기를 줄이는 설정(깜빡임 줄이기)이 있다는 것까지 여기서 알린다.
// 엔딩 뒤 타이틀로 돌아올 때(씬을 다시 불러도)는 다시 뜨지 않는다 — 한 번 켠 동안 한 번.
// 떠 있는 동안 들어온 입력은 여기서 삼킨다 — 경고를 넘기려고 누른 키가 타이틀의 [PRESS ANY KEY] 까지 넘기지 않게.
public partial class BootWarning : CanvasLayer
{
    private const double MinSeconds = 2.2;    // 이만큼은 읽힌다(그 전에는 눌러도 넘어가지 않는다)
    private const double AutoSeconds = 7.0;   // 아무것도 안 눌러도 이만큼 뒤에는 넘어간다
    private static bool _shown;

    private Control _root;
    private double _t;
    private bool _closing;

    public static void ShowOnce(Node parent)
    {
        if (_shown || parent == null) return;
        _shown = true;
        parent.AddChild(new BootWarning());
    }

    public override void _Ready()
    {
        Layer = 200;   // 필터(119) · 설정 창(130) · 공포 오버레이 위 — 무엇보다 먼저 보인다
        ProcessMode = ProcessModeEnum.Always;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var bg = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(bg);

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 22);
        _root.AddChild(box);

        box.AddChild(Line("경 고", 46, new Color(0.92f, 0.20f, 0.18f), ViewFont.DayTitle));
        box.AddChild(Line("이 게임에는 붉게 점멸하는 조명, 번쩍이는 화면, 흔들리는 화면 연출이 있습니다.\n" +
                          "빛에 민감하거나 광과민성 발작을 겪은 적이 있다면 플레이에 주의하십시오.",
                          22, new Color(0.88f, 0.90f, 0.90f)));
        box.AddChild(Line("환경 설정의 「깜빡임 줄이기」를 켜면 이 연출들이 약해집니다.", 20, new Color(0.46f, 0.90f, 0.80f)));
        box.AddChild(Line("이 게임은 공포 · 유혈 · 사망 묘사를 포함하고 있습니다.", 18, new Color(0.55f, 0.60f, 0.62f)));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 26f) });
        var hint = Line("아무 키나 눌러 계속", 16, new Color(0.40f, 0.46f, 0.48f));
        hint.Name = "Hint";
        hint.Modulate = new Color(1, 1, 1, 0);
        box.AddChild(hint);
    }

    private static Label Line(string text, int size, Color color, Font font = null)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontOverride("font", font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t >= MinSeconds && _root.FindChild("Hint", true, false) is Label hint && hint.Modulate.A < 1f)
            hint.Modulate = new Color(1, 1, 1, Mathf.Min(1f, hint.Modulate.A + (float)delta * 2f));
        if (_t >= AutoSeconds) Close();
    }

    public override void _Input(InputEvent e)
    {
        if (_closing) { GetViewport().SetInputAsHandled(); return; }
        bool press = e is InputEventKey { Pressed: true, Echo: false } || e is InputEventMouseButton { Pressed: true };
        if (e is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
        if (press && _t >= MinSeconds) Close();
    }

    private void Close()
    {
        if (_closing) return;
        _closing = true;
        var tw = CreateTween();
        tw.TweenProperty(_root, "modulate:a", 0f, 0.6).SetTrans(Tween.TransitionType.Sine);
        tw.TweenCallback(Callable.From(QueueFree));
    }
}
