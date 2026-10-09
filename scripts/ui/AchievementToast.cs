using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Ui;

// 도전과제 달성 알림 — 화면 오른쪽 아래에 조용히 올라오는 작은 창.
//
// 게임을 멈추지 않고, 입력도 가로채지 않는다(MouseFilter.Ignore). 여러 개가 한꺼번에
// 해금되면 줄을 세워 하나씩 보여 준다.
//
// **중요한 연출 도중에는 뜨지 않는다.** 프롤로그 컷신 · 엔딩 · 암전 전환 · DAY 스토리가
// 도는 동안은 붙잡아 두고, 화면이 안전해진 뒤에 띄운다. 트루엔딩 장면 한복판에
// 「드디어 퇴근입니다」가 튀어나오면 그 엔딩이 그걸로 끝나 버린다.
public partial class AchievementToast : CanvasLayer
{
    public static AchievementToast Instance { get; private set; }

    // ── 연출 수치 ───────────────────────────────────────────────────
    private const float RiseSeconds = 0.25f;   // 아래에서 올라오며 서서히 나타난다
    private const float HoldSeconds = 3.4f;    // 머무는 시간
    private const float FadeSeconds = 0.30f;   // 사라지는 시간
    private const float RiseDistance = 26f;    // 올라오는 거리(논리 px)
    private const float GapSeconds = 0.22f;    // 다음 알림까지의 틈

    // 아래 수치는 **논리 단위**다. 실제 픽셀은 Scale 배. 글자 크기(ViewFont.FS)도 같은 배율을
    // 쓰므로, 해상도나 UI 배율이 바뀌어도 글자가 칸을 넘지 않는다.
    // (FS 만 TextScale 을 곱하고 칸은 안 곱하던 때, 설명 두 줄이 카드 밖으로 흘러나왔다.)
    private static float Scale => ControlRoom3DController.UiScale * ViewFont.TextScale;

    private const float MarginRight = 22f;
    private const float MarginBottom = 22f;
    private const float CardWidth = 322f;
    private const float CardHeight = 86f;
    private const float IconSize = 52f;
    private const float PadLeft = 12f;
    private const float IconGap = 12f;

    // ── 색 (어두운 남먹색 바탕 · 얇은 금속 테두리 · 낮은 채도의 크림색 제목) ──
    private static readonly Color Back = new(0.043f, 0.059f, 0.086f, 0.94f);
    private static readonly Color Edge = new(0.60f, 0.62f, 0.58f, 0.55f);
    private static readonly Color EdgeInner = new(0.86f, 0.80f, 0.60f, 0.30f);
    private static readonly Color Tag = new(0.76f, 0.70f, 0.50f, 0.85f);
    private static readonly Color Title = new(0.95f, 0.91f, 0.78f);
    private static readonly Color Body = new(0.70f, 0.74f, 0.74f);
    private static readonly Color IconInk = new(0.88f, 0.84f, 0.66f);

    private const string TagText = "ACHIEVEMENT UNLOCKED";

    private Control _root;
    private Panel _card;
    private Label _tag, _title, _body;
    private Label _icon;
    private Tween _tween;

    private readonly Queue<AchievementDefinition> _queue = new();
    private bool _showing;
    private double _gap;

    public override void _Ready()
    {
        Instance = this;
        // 경보 HUD(122) · 공포 연출(128) 위, 암전(BlinkOverlay 160) 아래.
        Layer = 150;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        AchievementManager.AchievementUnlocked += OnUnlocked;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        AchievementManager.AchievementUnlocked -= OnUnlocked;
        if (Instance == this) Instance = null;
    }

    // ── 화면 ────────────────────────────────────────────────────────

    private void BuildUi()
    {
        float scale = Scale;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _card = new Panel
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(CardWidth * scale, CardHeight * scale),
            Size = new Vector2(CardWidth * scale, CardHeight * scale),
            Modulate = new Color(1f, 1f, 1f, 0f),
            Visible = false,
        };
        // 오른쪽 아래 고정 — 해상도가 바뀌어도 여백이 그대로 유지된다.
        _card.AnchorLeft = _card.AnchorRight = 1f;
        _card.AnchorTop = _card.AnchorBottom = 1f;
        _card.OffsetLeft = -(CardWidth + MarginRight) * scale;
        _card.OffsetRight = -MarginRight * scale;
        _card.OffsetTop = -(CardHeight + MarginBottom) * scale;
        _card.OffsetBottom = -MarginBottom * scale;
        _card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Back,
            BorderColor = Edge,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        });
        _root.AddChild(_card);

        // 왼쪽 아이콘 칸 — 별도 그림을 준비하지 않았으므로 분류별 픽토그램 한 글자를 쓴다.
        var iconBox = new Panel
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(PadLeft * scale, (CardHeight - IconSize) * 0.5f * scale),
            Size = new Vector2(IconSize * scale, IconSize * scale),
        };
        iconBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.15f, 0.95f),
            BorderColor = EdgeInner,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        _card.AddChild(iconBox);

        _icon = Text("◆", ViewFont.FS(22), IconInk);
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconBox.AddChild(_icon);

        float textLeft = (PadLeft + IconSize + IconGap) * scale;
        float textWidth = (CardWidth - PadLeft - IconSize - IconGap - PadLeft) * scale;

        _tag = Text(TagText, ViewFont.FS(9), Tag);
        _tag.Position = new Vector2(textLeft, 10f * scale);
        _tag.Size = new Vector2(textWidth, 12f * scale);
        _card.AddChild(_tag);

        _title = Text("", ViewFont.FS(15), Title);
        _title.Position = new Vector2(textLeft, 24f * scale);
        _title.Size = new Vector2(textWidth, 20f * scale);
        _card.AddChild(_title);

        // 설명은 두 줄까지 접힌다 — 조건 문구가 한 줄에 안 들어가는 것이 보통이다.
        _body = Text("", ViewFont.FS(9), Body);
        _body.Position = new Vector2(textLeft, 47f * scale);
        _body.Size = new Vector2(textWidth, 30f * scale);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _card.AddChild(_body);
    }

    private static Label Text(string s, int px, Color c)
    {
        var l = new Label
        {
            Text = s,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipText = false,
        };
        l.AddThemeFontOverride("font", ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    // ── 큐 ──────────────────────────────────────────────────────────

    private void OnUnlocked(AchievementDefinition def)
    {
        if (def == null) return;
        _queue.Enqueue(def);
    }

    // 지금 알림을 띄워도 괜찮은 화면인가. 아니면 큐에 그대로 둔다.
    //
    // 이 판정은 "무엇을 막는가"가 아니라 "무엇이 지금 화면의 주인인가"를 본다.
    private static bool SafeToShow()
    {
        // 프롤로그 컷신 · 프롤로그 안내.
        if (NSP.Prologue.CutscenePlayer.Instance?.IsPlaying == true) return false;
        if (NSP.Prologue.PrologueDirector.Instance?.IsRunning == true) return false;
        // 엔딩 연출 — 마지막 「5일간의 근무 기록」 화면부터는 괜찮다.
        if (EndingDirector.IsPlaying && !EndingRecordOverlay.IsShown) return false;
        // 눈을 감은 전환(암전) · DAY 스토리.
        if (BlinkOverlay.Instance?.IsClosed == true) return false;
        if (StoryTransition.Active) return false;
        if (StoryCutinDirector.PausesGameplay) return false;
        // 수리 승인 미로를 푸는 중 — 손이 바쁜 순간에 끼어들지 않는다.
        if (RepairApprovalSystem.Busy) return false;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_showing) return;
        if (_gap > 0) { _gap -= delta; return; }
        if (_queue.Count == 0 || !SafeToShow()) return;
        Present(_queue.Dequeue());
    }

    private void Present(AchievementDefinition def)
    {
        _showing = true;
        _icon.Text = def.Glyph;
        _title.Text = def.Name;
        _body.Text = def.Condition;
        _card.Visible = true;

        float scale = Scale;
        float restTop = -(CardHeight + MarginBottom) * scale;
        float restBottom = -MarginBottom * scale;

        _card.OffsetTop = restTop + RiseDistance * scale;
        _card.OffsetBottom = restBottom + RiseDistance * scale;
        _card.Modulate = new Color(1f, 1f, 1f, 0f);

        Sfx.Instance?.Play("achievement", -9f);

        _tween?.Kill();
        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(_card, "offset_top", restTop, RiseSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_card, "offset_bottom", restBottom, RiseSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_card, "modulate:a", 1f, RiseSeconds);
        _tween.Chain().TweenInterval(HoldSeconds);
        _tween.Chain().TweenProperty(_card, "modulate:a", 0f, FadeSeconds);
        _tween.Chain().TweenCallback(Callable.From(() =>
        {
            _card.Visible = false;
            _showing = false;
            _gap = GapSeconds;
        }));
    }

    // ── 검사용 ──────────────────────────────────────────────────────

    public bool IsShowing => _showing;
    public int Pending => _queue.Count;

    // 검사 전용 — 아직 띄우지 않은 큐를 비운다(검사 구간마다 깨끗하게 시작하기 위해).
    public void ClearForTest()
    {
        _queue.Clear();
        _tween?.Kill();
        _tween = null;
        _showing = false;
        _gap = 0;
        if (_card != null) _card.Visible = false;
    }
    public string ShownTitle => _title?.Text ?? "";
    public bool CardVisible => _card?.Visible ?? false;
    public static bool CanShowNow => SafeToShow();
}
