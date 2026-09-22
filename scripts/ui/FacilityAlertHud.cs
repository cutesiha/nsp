using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Ui;

// "지금 중요한 문제가 터졌다"를 화면 어디를 보고 있든 알리는 경보 연출.
//
// 방해공작처럼 로그만 남기면 놓치기 쉬운 사건에 쓴다. 게임 로직은 전혀 건드리지 않고
// 화면 위에만 얹히므로, 연출이 도는 동안에도 시뮬레이션은 그대로 흐른다.
//
// 다른 이벤트에서도 그대로 쓸 수 있다:
//     FacilityAlertHud.Instance?.ShowCriticalAlert("⚠ ...");
//     FacilityAlertHud.Instance?.ShowCoreLoss(2.5f);
// 시스템 알림의 중요도. 색과 표시 시간만 달라진다.
public enum NoticeLevel { Info, Warning, Critical }

public partial class FacilityAlertHud : CanvasLayer
{
    public static FacilityAlertHud Instance { get; private set; }
    // 왼쪽 아래 알림이 한 줄 뜰 때마다(중복 제외) — 모니터 1 아래 한 줄 알림이 같은 내용을 받는다.
    public static event System.Action<string, NoticeLevel> Noticed;

    // 화면 왼쪽 아래에 쌓이는 짧은 시스템 알림.
    // Facility Log 와 역할이 다르다 — 여기는 "지금 무슨 일이 났는지"만 알리고,
    // 시각·위치·직원 이동 같은 추리 근거는 Facility Log 가 맡는다.
    private const int NoticeKeep = 4;
    private const double NoticeHold = 4.0;
    private const double NoticeHoldCritical = 6.5;
    private const float NoticeFade = 1.2f;

    private VBoxContainer _notices;
    private readonly System.Collections.Generic.Dictionary<string, double> _noticeSeenAt = new();

    // 붉은 점멸 — 짧고 강하게 세 번. 길게 덮으면 플레이를 방해한다.
    private const float FlashPeak = 0.34f;
    private const double FlashOn = 0.08;
    private const double FlashOff = 0.08;
    private const int FlashCount = 3;

    // 상단 배너 — 위에서 내려와 잠시 머물다 사라진다.
    // 화면 맨 위는 코어 게이지(ShiftHud)와 감소량 표시가 쓰므로 그 아래에 멈춘다.
    // 하필 "복구율이 깎였다"고 알리면서 그 숫자를 가리면 안 된다.
    private const float BannerHeight = 70f;
    private const float BannerTop = 98f;
    private const double BannerHold = 2.4;

    private static readonly Color AlertRed = new(0.95f, 0.16f, 0.12f);

    private ColorRect _flash;
    private Panel _banner;
    private Label _bannerText;
    private Label _coreLoss;

    public override void _Ready()
    {
        Instance = this;
        // HUD(110)·기록창(115) 위, 공포 연출(128) 아래.
        Layer = 122;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // --- 외부에서 부르는 것 -------------------------------------------------

    // 짧은 시스템 알림 한 줄. 같은 문장이 연달아 들어오면 무시한다
    // (발전실 과열 점검이 반복돼도 진행 중인 사건 하나당 한 번만 뜬다).
    public void Notify(string text, NoticeLevel level = NoticeLevel.Info)
    {
        if (_notices == null || string.IsNullOrWhiteSpace(text)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_noticeSeenAt.TryGetValue(text, out double at) && now - at < 8.0) return;
        _noticeSeenAt[text] = now;
        Noticed?.Invoke(text, level);

        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        label.AddThemeFontOverride("font", ViewFont.Default);
        label.AddThemeFontSizeOverride("font_size", ViewFont.FS(16));
        label.AddThemeColorOverride("font_color", level switch
        {
            NoticeLevel.Critical => new Color(1f, 0.34f, 0.30f),
            NoticeLevel.Warning => new Color(1f, 0.74f, 0.28f),
            _ => new Color(0.72f, 0.95f, 0.92f),
        });
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 5);
        _notices.AddChild(label);

        while (_notices.GetChildCount() > NoticeKeep)
        {
            var oldest = _notices.GetChild(0);
            _notices.RemoveChild(oldest);
            oldest.QueueFree();
        }

        var t = CreateTween();
        t.TweenInterval(level == NoticeLevel.Critical ? NoticeHoldCritical : NoticeHold);
        t.TweenProperty(label, "modulate:a", 0f, NoticeFade);
        t.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label)) label.QueueFree();
        }));
    }

    // DAY 가 끝나면 이번 근무의 알림은 지운다(지난 사건은 Facility Log 에서 본다).
    public void ClearNotices()
    {
        _noticeSeenAt.Clear();
        if (_notices == null) return;
        foreach (Node c in _notices.GetChildren()) { _notices.RemoveChild(c); c.QueueFree(); }
    }

    public override void _Process(double delta)
    {
        // 근무 중에만 남겨 둔다.
        if (GameState.Instance?.CurrentPhase != GamePhase.Live && _notices?.GetChildCount() > 0)
            ClearNotices();
    }

    // 중요 사건 경보. 붉은 점멸 + 상단 배너 + 경보음이 한 번에 나간다.
    // 시스템 알림도 같이 띄운다 — 알림이 기본 전달 수단이고 연출은 추가다.
    public void ShowCriticalAlert(string message)
    {
        Notify(message, NoticeLevel.Critical);
        RedFlash();
        Banner(message);
        // 새 오디오 파일을 만들지 않는다 — 기존 경보음을 짧게 겹쳐 쓴다.
        Sfx.Instance?.Play("alert_beep3", -4f);
        Sfx.Instance?.Play("noise", -14f);
    }

    // 코어 복구율이 깎인 순간 그 감소분을 잠깐 띄운다.
    public void ShowCoreLoss(float amount)
    {
        if (_coreLoss == null || amount <= 0.01f) return;
        _coreLoss.Text = $"-{amount:0.#}%";
        _coreLoss.Visible = true;
        _coreLoss.Modulate = new Color(1f, 1f, 1f, 1f);
        _coreLoss.Position = new Vector2(_coreLoss.Position.X, CoreLossTop);

        var t = CreateTween();
        t.SetParallel(true);
        // 숫자가 게이지에서 떨어져 나가듯 살짝 내려가며 사라진다.
        t.TweenProperty(_coreLoss, "position:y", CoreLossTop + 22f, 1.5)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_coreLoss, "modulate:a", 0f, 1.5).SetDelay(0.6);
        t.Chain().TweenCallback(Callable.From(() => _coreLoss.Visible = false));
    }

    // --- 연출 ---------------------------------------------------------------

    private void RedFlash()
    {
        if (_flash == null) return;
        _flash.Visible = true;
        _flash.Color = AlertRed with { A = 0f };

        var t = CreateTween();
        for (int i = 0; i < FlashCount; i++)
        {
            t.TweenProperty(_flash, "color:a", FlashPeak, FlashOn * 0.35);
            t.TweenProperty(_flash, "color:a", 0f, FlashOff).SetDelay(FlashOn * 0.65);
        }
        // 마지막 한 번은 천천히 빠지며 여운을 남긴다.
        t.TweenProperty(_flash, "color:a", FlashPeak * 0.8f, 0.06);
        t.TweenProperty(_flash, "color:a", 0f, 0.45);
        t.TweenCallback(Callable.From(() => _flash.Visible = false));

        // 카메라는 아주 약하게만 — 크게 흔들면 지도를 읽을 수 없다.
        ControlRoom3DController.Instance?.ShakeCamera(0.22f, 0.35f);
    }

    private void Banner(string message)
    {
        if (_banner == null || string.IsNullOrWhiteSpace(message)) return;
        _bannerText.Text = message;
        _banner.Visible = true;
        _banner.OffsetTop = -BannerHeight;
        _banner.OffsetBottom = 0f;
        _banner.Modulate = new Color(1f, 1f, 1f, 1f);

        var t = CreateTween();
        // 위에서 내려온다 → 머문다 → 서서히 사라진다.
        t.TweenProperty(_banner, "offset_top", BannerTop, 0.26).SetTrans(Tween.TransitionType.Back);
        t.Parallel().TweenProperty(_banner, "offset_bottom", BannerTop + BannerHeight, 0.26)
            .SetTrans(Tween.TransitionType.Back);
        t.TweenInterval(BannerHold);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.55);
        t.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // --- UI -----------------------------------------------------------------

    private const float CoreLossTop = 20f;

    private void BuildUi()
    {
        _flash = new ColorRect
        {
            Color = AlertRed with { A = 0f },
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);

        _banner = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetTop = -BannerHeight, OffsetBottom = 0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _banner.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.02f, 0.02f, 0.93f),
            BorderColor = AlertRed,
            BorderWidthTop = 2, BorderWidthBottom = 2,
        });
        AddChild(_banner);

        _bannerText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        _bannerText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bannerText.AddThemeFontOverride("font", ViewFont.Default);
        _bannerText.AddThemeFontSizeOverride("font_size", ViewFont.FS(26));
        _bannerText.AddThemeColorOverride("font_color", new Color(1f, 0.90f, 0.86f));
        _bannerText.AddThemeColorOverride("font_outline_color", new Color(0.15f, 0f, 0f));
        _bannerText.AddThemeConstantOverride("outline_size", 5);
        _banner.AddChild(_bannerText);

        // 코어 게이지(ShiftHud) 바로 오른쪽 — 게이지와 같은 줄에 붙여 둔다.
        // 아래에 두면 잠시 뒤 내려오는 배너와 겹친다.
        _coreLoss = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = 296f, OffsetRight = 456f,
            Position = new Vector2(296f, CoreLossTop),
            Visible = false,
        };
        _coreLoss.AddThemeFontOverride("font", ViewFont.Default);
        _coreLoss.AddThemeFontSizeOverride("font_size", ViewFont.FS(28));
        _coreLoss.AddThemeColorOverride("font_color", new Color(1f, 0.32f, 0.26f));
        _coreLoss.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0f, 0f));
        _coreLoss.AddThemeConstantOverride("outline_size", 6);
        AddChild(_coreLoss);

        // 왼쪽 아래 — 기록창 버튼(오른쪽 아래)과 겹치지 않는 자리.
        _notices = new VBoxContainer
        {
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 26f, OffsetRight = 660f,
            OffsetTop = -186f, OffsetBottom = -26f,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _notices.AddThemeConstantOverride("separation", 4);
        AddChild(_notices);
    }
}
