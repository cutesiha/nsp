using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 단서를 찍는 순간의 반응 — 짧은 소리 + 「단서 기록됨」 토스트 + 패드 뱃지 수.
// 단서를 **어디서** 찍었는지(로그 창 ☆ · CCTV 대화 자막 · 심문 ★ · 패드)는 상관없다.
// ClueBoard.Changed 하나만 듣는다.
//
// 근무 중 단축키도 여기서 받는다.
//   F    지금(또는 방금) CCTV 로 들은 대화를 단서로 찍는다(자막 클릭과 같다)
//   Tab  관리자 패드를 꺼낸다 / 내려놓는다(AdminPad3D)
//
// 입력 액션을 새로 만들지 않고 키를 직접 본다(Day1HistoryOverlay 의 T 키와 같은 방식).
public partial class ClueHud : CanvasLayer
{
    public static ClueHud Instance { get; private set; }

    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.50f, 0.62f, 0.66f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);
    private static readonly Color Bg = new(0.02f, 0.05f, 0.06f, 0.92f);

    private const double ToastHoldSeconds = 1.8;

    private Font _font;
    private Control _root;

    private PanelContainer _toast;
    private Label _toastHead, _toastBody, _toastBadge;
    private Tween _toastTween;

    public override void _Ready()
    {
        Instance = this;
        Layer = 116;   // 시설 로그 창(115) 위 — 로그에서 ☆ 를 눌러도 토스트가 가려지지 않게. ESC 메뉴(120) 아래.
        ProcessMode = ProcessModeEnum.Always;
        _font = ViewFont.Default;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildToast();
        ClueBoard.Changed += OnClueChanged;
    }

    public override void _ExitTree()
    {
        ClueBoard.Changed -= OnClueChanged;
        if (Instance == this) Instance = null;
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (PauseMenu.Instance?.IsOpen == true) return;
        var phase = GameState.Instance?.CurrentPhase;

        if (key.Keycode == Key.F && phase == GamePhase.Live && AdminPad3D.Instance?.IsOpen != true)
        {
            // 들은 대화가 없으면 아무 일도 없다 — 키를 삼키지 않는다.
            if (CctvOverheardCaption.Instance?.TogglePin() == true) GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Tab && AdminPad3D.Instance is { } pad)
        {
            // Tab 은 UI 포커스 이동 키이기도 하다 — 패드를 다루는 순간에는 여기서 삼킨다.
            if (pad.IsOpen) pad.Close();
            else if (pad.CanOpen()) pad.Open();
            else return;
            GetViewport().SetInputAsHandled();
        }
    }

    // ── 토스트 ───────────────────────────────────────────────────────────

    private void BuildToast()
    {
        _toast = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _toast.AddThemeStyleboxOverride("panel", Box(Cyan with { A = 0.7f }, 14, 8));
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.OffsetTop = 28;
        _root.AddChild(_toast);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        _toast.AddChild(col);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 18);
        col.AddChild(head);
        _toastHead = Lbl("", 17, Cyan);
        _toastHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_toastHead);
        _toastBadge = Lbl("", 13, Amber);
        _toastBadge.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(_toastBadge);

        _toastBody = Lbl("", 13, Dim);
        _toastBody.CustomMinimumSize = new Vector2(ViewFont.FS(300), 0);
        col.AddChild(_toastBody);
    }

    private void OnClueChanged(ClueBoard.Entry entry, bool pinned)
    {
        if (pinned) Sfx.Instance?.Play("relay_click", -7f, 1.3f);
        else Sfx.Instance?.Play("relay_click", -12f, 0.8f);

        var ev = entry?.Evidence;
        _toastHead.Text = pinned ? "◆ 단서 기록됨" : "◇ 단서 기록 해제";
        _toastHead.AddThemeColorOverride("font_color", pinned ? Cyan : Dim);
        _toastBody.Text = ev == null ? "" : Short(ev.OneLine, 34);
        // 패드를 들고 있으면 그 자리에서 보이므로 "+N" 대신 전체 수만.
        bool padOpen = AdminPad3D.Instance?.IsOpen == true;
        _toastBadge.Text = !padOpen && ClueBoard.UnseenCount > 0 ? $"패드 +{ClueBoard.UnseenCount}" : $"패드 {ClueBoard.Count}";

        _toastTween?.Kill();
        _toast.Visible = true;
        _toast.Modulate = Colors.White with { A = 0f };
        _toastTween = CreateTween();
        _toastTween.TweenProperty(_toast, "modulate:a", 1f, 0.12);
        _toastTween.TweenInterval(ToastHoldSeconds);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.4);
        _toastTween.TweenCallback(Callable.From(() => _toast.Visible = false));
    }

    // ── 공통 ─────────────────────────────────────────────────────────────

    private static StyleBoxFlat Box(Color border, int padX, int padY) => new()
    {
        BgColor = Bg,
        BorderColor = border,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
    };

    private Label Lbl(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static string Short(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s[..max] + "…";
}
