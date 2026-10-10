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
    //
    // 올라올 때와 내려갈 때 모두 **제자리를 한 번 지나친다**(뽀잉).
    //   등장 : 아래에서 올라와 제자리보다 조금 더 위로 솟았다가 내려앉는다
    //   퇴장 : 제자리에서 살짝 위로 튕겼다가 아래로 빠진다
    // Godot 의 Back 이징이 바로 그 움직임이다 — Out 은 지나쳤다 돌아오고,
    // In 은 반대쪽으로 먼저 당겼다가 간다. 직접 키프레임을 짜지 않는다.
    private const float RiseSeconds = 0.20f;      // 아래에서 솟아오르는 시간
    private const float SettleSeconds = 0.17f;    // 솟은 자리에서 제자리로 내려앉는 시간
    private const float HoldSeconds = 3.4f;       // 머무는 시간
    private const float HopSeconds = 0.13f;       // 퇴장 직전 위로 튕기는 시간
    private const float FallSeconds = 0.30f;      // 아래로 빠지며 사라지는 시간
    private const float RiseDistance = 34f;       // 올라오기 시작하는 깊이(논리 px)
    private const float Overshoot = 12f;          // 제자리보다 이만큼 더 솟았다가 내려앉는다
    private const float ExitHop = 9f;             // 내려가기 전에 이만큼 위로 튕긴다
    private const float FallDistance = 44f;       // 내려가며 빠지는 깊이(논리 px)
    private const float GapSeconds = 0.22f;       // 다음 알림까지의 틈

    // 아래 수치는 **논리 단위**다. 실제 픽셀은 Scale 배. 글자 크기(ViewFont.FS)도 같은 배율을
    // 쓰므로, 해상도나 UI 배율이 바뀌어도 글자가 칸을 넘지 않는다.
    // (FS 만 TextScale 을 곱하고 칸은 안 곱하던 때, 설명 두 줄이 카드 밖으로 흘러나왔다.)
    private static float LayoutScale => ControlRoom3DController.UiScale * ViewFont.TextScale;

    private const float MarginRight = 22f;
    private const float MarginBottom = 22f;
    private const float CardWidth = 322f;
    private const float CardHeight = 86f;
    private const float IconSize = 52f;
    private const float PadLeft = 12f;
    private const float IconGap = 12f;

    // ── 색 ──────────────────────────────────────────────────────────
    //
    // 예전에는 테두리 · 제목 · 아이콘이 전부 누런 크림색이었다. 이 게임의 다른 글자
    // (대사창 · 단말기 버튼)는 전부 밝은 하늘색 계열이라 이 창만 혼자 떠 보였다.
    // 대사창과 같은 청록으로 맞춘다.
    private static readonly Color Back = new(0.043f, 0.059f, 0.086f, 0.94f);
    private static readonly Color Edge = new(0.42f, 0.70f, 0.80f, 0.60f);
    private static readonly Color EdgeInner = new(0.55f, 0.95f, 1f, 0.38f);
    private static readonly Color Title = new(0.82f, 0.96f, 1f);
    private static readonly Color Body = new(0.66f, 0.80f, 0.86f);
    private static readonly Color IconInk = new(0.55f, 0.95f, 1f);
    // 달성음. assets/audio/sfx/achievement_sfx.mp3
    private const string UnlockSfx = "achievement_sfx";

    private Control _root;
    private Panel _card;
    private Label _title, _body;
    private AchievementIconView _icon;
    private Tween _tween;
    // 투명도는 움직임과 따로 간다(단계 길이가 서로 다르다).
    private Tween _fade;

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
        float scale = LayoutScale;

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

        // 왼쪽 아이콘 칸 — 도전과제마다 다른 그림을 직접 그린다(AchievementIcons).
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

        _icon = new AchievementIconView { MouseFilter = Control.MouseFilterEnum.Ignore, Ink = IconInk };
        _icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconBox.AddChild(_icon);

        float textLeft = (PadLeft + IconSize + IconGap) * scale;
        float textWidth = (CardWidth - PadLeft - IconSize - IconGap - PadLeft) * scale;

        // "ACHIEVEMENT UNLOCKED" 머리글은 뺐다 — 창이 뜨는 것 자체가 그 뜻이고,
        // 그 자리를 비워야 이름과 설명을 키울 수 있다.
        _title = Text("", ViewFont.FS(17), Title);
        _title.Position = new Vector2(textLeft, 16f * scale);
        _title.Size = new Vector2(textWidth, 24f * scale);
        _card.AddChild(_title);

        // 설명은 두 줄까지 접힌다 — 조건 문구가 한 줄에 안 들어가는 것이 보통이다.
        _body = Text("", ViewFont.FS(12), Body);
        _body.Position = new Vector2(textLeft, 42f * scale);
        _body.Size = new Vector2(textWidth, 36f * scale);
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
        _icon.Show(def);
        _title.Text = def.Name;
        _body.Text = def.Condition;
        _card.Visible = true;

        float scale = LayoutScale;
        float restTop = -(CardHeight + MarginBottom) * scale;
        float restBottom = -MarginBottom * scale;

        _card.OffsetTop = restTop + RiseDistance * scale;
        _card.OffsetBottom = restBottom + RiseDistance * scale;
        _card.Modulate = new Color(1f, 1f, 1f, 0f);

        Sfx.Instance?.Play(UnlockSfx, -9f);

        float fallTop = restTop + FallDistance * scale;
        float fallBottom = restBottom + FallDistance * scale;

        float overTop = restTop - Overshoot * scale;
        float overBottom = restBottom - Overshoot * scale;
        float hopTop = restTop - ExitHop * scale;
        float hopBottom = restBottom - ExitHop * scale;

        // 움직임은 **순차 트윈 하나**로 짠다. 위치를 offset_top / offset_bottom 두 속성으로
        // 나눠 tween 하면서 parallel 과 chain 을 섞으면 어느 단계가 어느 단계 뒤에 붙는지가
        // 흐려진다(실제로 퇴장 튕김 단계가 통째로 건너뛰어졌다).
        // 높이 하나(카드 윗선)만 tween 하고, 아랫선은 그 값에서 계산한다.
        _cardHeightPx = CardHeight * scale;
        float startTop = restTop + RiseDistance * scale;

        _tween?.Kill();
        _tween = CreateTween();
        // ① 솟아오른다 — 제자리를 지나쳐 조금 더 위까지.
        Hop(startTop, overTop, RiseSeconds, Tween.TransitionType.Sine, Tween.EaseType.Out);
        // ② 내려앉는다 — 지나친 만큼 제자리로. 여기까지가 등장 "뽀잉".
        Hop(overTop, restTop, SettleSeconds, Tween.TransitionType.Back, Tween.EaseType.Out);
        _tween.TweenInterval(HoldSeconds);
        // ③ 내려가기 전에 위로 한 번 더 튕긴다.
        Hop(restTop, hopTop, HopSeconds, Tween.TransitionType.Sine, Tween.EaseType.Out);
        // ④ 그대로 아래로 빠진다.
        Hop(hopTop, fallTop, FallSeconds, Tween.TransitionType.Cubic, Tween.EaseType.In);
        _tween.TweenCallback(Callable.From(() =>
        {
            _card.Visible = false;
            _showing = false;
            _gap = GapSeconds;
        }));

        // 투명도는 따로 간다 — 또렷해지는 건 자리를 잡기 전에 끝나고(투명한 채로 튀어오르면
        // 유령처럼 보인다), 흐려지는 건 퇴장 튕김이 **보인 뒤에** 시작한다.
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(_card, "modulate:a", 1f, RiseSeconds * 0.8f);
        _fade.TweenInterval(RiseSeconds * 0.2f + SettleSeconds + HoldSeconds + HopSeconds
                            + FallSeconds * 0.28f);
        _fade.TweenProperty(_card, "modulate:a", 0f, FallSeconds * 0.72f);
    }

    // 카드 윗선을 from → to 로 옮기는 한 단계. 아랫선은 그 값에서 따라간다
    // (둘을 따로 tween 하면 단계가 어긋나며 카드가 늘어난다).
    private float _cardHeightPx;

    private void Hop(float from, float to, float seconds,
                     Tween.TransitionType trans, Tween.EaseType ease)
    {
        _tween.TweenMethod(Callable.From<float>(SetCardTop), from, to, seconds)
            .SetTrans(trans).SetEase(ease);
    }

    private void SetCardTop(float top)
    {
        if (_card == null) return;
        _card.OffsetTop = top;
        _card.OffsetBottom = top + _cardHeightPx;
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
        _fade?.Kill();
        _fade = null;
        _showing = false;
        _gap = 0;
        if (_card != null) _card.Visible = false;
    }
    public string ShownTitle => _title?.Text ?? "";
    public bool CardVisible => _card?.Visible ?? false;
    // 카드가 지금 서 있는 높이(논리 px, 작을수록 위). 뽀잉이 실제로 제자리를 지나치는지
    // 눈이 아니라 숫자로 확인하는 자리다.
    public float CardTop
    {
        get
        {
            float s = LayoutScale;
            return (_card?.OffsetTop ?? 0f) / Mathf.Max(0.01f, s);
        }
    }
    public float CardRestTop => -(CardHeight + MarginBottom);
    public static bool CanShowNow => SafeToShow();
}
