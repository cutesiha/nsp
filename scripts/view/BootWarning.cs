using Godot;
using NSP.Core;

namespace NSP.View;

// 게임을 켤 때 한 번 뜨는 경고 화면 — 광과민성 발작 · 공포 · 유혈 묘사.
//
// 공개 대회 · 출시 빌드에는 사실상 필수다. 붉은 점멸(직원 사망) · 화면 섬광 · 흔들림이 있고,
// 그 세기를 줄이는 설정(깜빡임 줄이기)이 있다는 것까지 여기서 알린다.
//
// 2026-10-31 사용자 요청으로 정한 모양:
//   · **게임을 새로 켰을 때만** 뜬다. 엔딩 뒤 · 시작화면으로 돌아왔을 때는 뜨지 않는다(한 번 켠 동안 한 번).
//   · 검은 화면 위로 글이 서서히 떠오르고(페이드 인), 4초 머문 뒤 저절로 사라진다(글 → 검은 막 순서로 페이드 아웃).
//     키를 누를 필요가 없다 — 사라지면 타이틀의 모니터1 [PRESS ANY KEY] 가 기다린다.
//   · 떠 있는 동안에는 소리가 하나도 나지 않는다(Master 음소거). 배경음은 경고가 걷힐 때 처음부터 시작한다
//     (ShiftFlowController 가 Closing 을 받아 곡을 튼다).
//   · 떠 있는 동안 들어온 입력은 여기서 삼킨다 — 그 키가 뒤의 타이틀까지 넘어가지 않게.
public partial class BootWarning : CanvasLayer
{
    private const double TextFadeIn = 1.0;     // 글이 떠오르는 시간
    private const double HoldSeconds = 4.0;    // 다 떠오른 뒤 머무는 시간(2026-10-31 사용자 요청 2초 → 4초)
    private const double TextFadeOut = 0.6;    // 글이 사라지는 시간
    private const double CurtainFadeOut = 0.8; // 그 뒤 검은 막이 걷히는 시간
    private static bool _shown;

    // 지금 경고가 떠 있는가. 타이틀(TitleRoomDirector)은 이 동안 [PRESS ANY KEY] 를 받지 않는다.
    public static bool Active { get; private set; }
    // 이번에 켠 게임에서 경고가 아직 안 떴는가(= 이번 타이틀에서 뜰 차례인가).
    public static bool WillShow => !_shown;
    // 검은 막이 걷히기 시작하는 순간. 타이틀이 [PRESS ANY KEY] 대기로 들어가고, 배경음이 처음부터 시작한다.
    public static event System.Action Closing;

    // 경고 없이 지나간 첫 부팅(개발용 바로 시작 등)도 "켤 때 한 번" 을 쓴 것으로 친다 —
    // 그러지 않으면 그 판의 엔딩 뒤 타이틀에서 처음으로 경고가 떠 버린다.
    public static void MarkShown() => _shown = true;

    private Control _root;
    private ColorRect _curtain;
    private VBoxContainer _box;
    private bool _done;

    // 다 걷히기 전에 장면째 사라지면(드묾) 소리를 되돌리고, 타이틀이 영영 기다리지 않게 신호를 보낸다.
    public override void _ExitTree() => Finish();

    public static void ShowOnce(Node parent)
    {
        if (_shown || parent == null) return;
        _shown = true;
        Active = true;
        SetMuted(true);
        parent.AddChild(new BootWarning());
    }

    // 떠 있는 동안 모든 소리를 끈다. 음량 설정(버스 볼륨)은 건드리지 않고 음소거 깃발만 쓴다.
    private static void SetMuted(bool on)
    {
        int master = AudioServer.GetBusIndex(GameSettings.BusMaster);
        if (master >= 0) AudioServer.SetBusMute(master, on);
    }

    public override void _Ready()
    {
        Layer = 200;   // 필터(119) · 설정 창(130) · 공포 오버레이 위 — 무엇보다 먼저 보인다
        ProcessMode = ProcessModeEnum.Always;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        // 검은 막은 처음부터 완전히 덮는다 — 뒤의 타이틀이 비치면 안 된다. 글만 서서히 떠오른다.
        _curtain = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_curtain);

        _box = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _box.AddThemeConstantOverride("separation", 22);
        _root.AddChild(_box);

        _box.AddChild(Line("경 고", 46, new Color(0.92f, 0.20f, 0.18f), ViewFont.DayTitle));
        _box.AddChild(Line("이 게임에는 붉게 점멸하는 조명, 번쩍이는 화면, 흔들리는 화면 연출이 있습니다.\n" +
                           "빛에 민감하거나 광과민성 발작을 겪은 적이 있다면 플레이에 주의하십시오.",
                           22, new Color(0.88f, 0.90f, 0.90f)));
        _box.AddChild(Line("환경 설정의 「깜빡임 줄이기」를 켜면 이 연출들이 약해집니다.", 20, new Color(0.46f, 0.90f, 0.80f)));
        _box.AddChild(Line("이 게임은 공포, 유혈, 사망 묘사를 포함하고 있습니다.", 18, new Color(0.55f, 0.60f, 0.62f)));

        // 떠오르고 → 머물고 → 글이 사라지고 → (여기서 소리 · 타이틀이 돌아온다) → 검은 막이 걷힌다.
        var tw = CreateTween();
        tw.TweenInterval(0.3);
        tw.TweenProperty(_box, "modulate:a", 1f, TextFadeIn).SetTrans(Tween.TransitionType.Sine);
        tw.TweenInterval(HoldSeconds);
        tw.TweenProperty(_box, "modulate:a", 0f, TextFadeOut).SetTrans(Tween.TransitionType.Sine);
        tw.TweenCallback(Callable.From(Finish));
        tw.TweenProperty(_curtain, "color:a", 0f, CurtainFadeOut).SetTrans(Tween.TransitionType.Sine);
        tw.TweenCallback(Callable.From(QueueFree));
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

    // 떠 있는 동안 키 · 클릭은 여기서 삼킨다(넘기는 기능은 없다 — 4초 뒤 저절로 걷힌다).
    public override void _Input(InputEvent e)
    {
        if (_done) return;
        if (e is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        Active = false;
        SetMuted(false);
        Closing?.Invoke();
    }
}
