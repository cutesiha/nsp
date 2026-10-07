using System.Threading.Tasks;
using Godot;

namespace NSP.Ui;

// "관리자가 잠시 눈을 감았다 뜬다" — 화면 가장자리부터 어두워져 중앙까지 덮고, 역순으로 열린다.
//
// 눈꺼풀 그림을 위아래에서 내리지 않는다(지시서 §2). 그런 연출은 1인칭 관제실 화면에
// 얹으면 만화가 된다. 여기서 하는 일은 하나뿐이다 — **비네트를 중앙까지 조여 들어간다.**
//
// 구현은 비네트 텍스처 한 장의 scale 이다. 어두운 고리가 바깥에 있을 때는 화면 밖이라
// 보이지 않고, scale 을 줄이면 그 고리가 중앙으로 밀려 들어오며 시야가 좁아진다.
// 마지막 구간만 검은 판으로 완전히 덮는다 — 고리만으로는 중앙에 작은 구멍이 남는다.
//
// **반드시 열린다.** 예외 · 씬 전환 · 강제 종료 어느 쪽으로 빠져나가도 OpenNow() 를
// 지나가거나 _ExitTree 가 치운다(§10 fail-safe). 닫힌 채로 남으면 그 뒤로 아무것도 안 보인다.
public partial class BlinkOverlay : CanvasLayer
{
    public static BlinkOverlay Instance { get; private set; }

    // 지금 눈이 감겨 있는가(검사 · fail-safe 판정용).
    public bool IsClosed { get; private set; }

    // 고리가 화면 밖에 있어 아무것도 가리지 않는 배율. 2.6 이면 1920 폭에서도 안 보인다.
    private const float OpenScale = 2.6f;
    // 고리가 중앙까지 조여든 배율. 이보다 더 줄이면 텍스처 보간이 지저분해진다.
    private const float ShutScale = 0.22f;

    private TextureRect _ring;
    private ColorRect _black;
    private Tween _tween;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        // 공포 연출 오버레이(HorrorDirector = 128)보다 위. 눈을 감으면 그 위의 노이즈도 덮인다.
        Layer = 160;

        _ring = new TextureRect
        {
            Texture = BuildRing(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Scale = Vector2.One * OpenScale,
        };
        _ring.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_ring);

        _black = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_black);

        Recenter();
        GetViewport().SizeChanged += Recenter;
    }

    public override void _ExitTree()
    {
        var vp = GetViewport();
        if (vp != null) vp.SizeChanged -= Recenter;
        if (Instance == this) Instance = null;
    }

    // scale 은 PivotOffset 을 기준으로 돈다. 가운데를 기준으로 조여야 하므로 매번 맞춘다.
    private void Recenter()
    {
        if (_ring == null || !IsInstanceValid(_ring)) return;
        var size = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        if (size == Vector2.Zero) return;
        _ring.Size = size;
        _ring.Position = Vector2.Zero;
        _ring.PivotOffset = size * 0.5f;
    }

    // 눈을 감는다. 끝나면 화면은 완전한 검정이다.
    public async Task Close(double seconds = 0.45)
    {
        Recenter();
        IsClosed = true;
        _tween?.Kill();
        _tween = CreateTween().SetParallel(true);
        // 고리가 중앙으로 조여든다 — 가장자리부터 어두워지는 것이 이 트윈이다.
        _tween.TweenProperty(_ring, "scale", Vector2.One * ShutScale, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        // 검은 판은 뒤쪽 60% 구간에서만 올라온다. 처음부터 올리면 가장자리 연출이 묻힌다.
        _tween.TweenProperty(_black, "color:a", 1f, seconds * 0.62)
            .SetDelay(seconds * 0.38).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(_tween, Tween.SignalName.Finished);
    }

    // 눈을 뜬다.
    public async Task Open(double seconds = 0.45)
    {
        Recenter();
        _tween?.Kill();
        _tween = CreateTween().SetParallel(true);
        _tween.TweenProperty(_ring, "scale", Vector2.One * OpenScale, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_black, "color:a", 0f, seconds * 0.55)
            .SetTrans(Tween.TransitionType.Sine);
        await ToSignal(_tween, Tween.SignalName.Finished);
        IsClosed = false;
    }

    // 트윈을 기다리지 않고 즉시 전부 걷는다. fail-safe 전용 — 연출이 아니다.
    public void OpenNow()
    {
        _tween?.Kill();
        _tween = null;
        IsClosed = false;
        if (_ring != null && IsInstanceValid(_ring)) _ring.Scale = Vector2.One * OpenScale;
        if (_black != null && IsInstanceValid(_black)) _black.Color = new Color(0f, 0f, 0f, 0f);
    }

    // 중앙이 비고 바깥이 검은 고리. AmbientOverlay 의 비네트보다 훨씬 급하게 짙어진다 —
    // 이쪽은 분위기가 아니라 시야를 **닫는** 용도다.
    private static ImageTexture BuildRing()
    {
        const int w = 512, h = 512;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float c = w / 2f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            // 0.30 안쪽은 완전 투명, 0.78 바깥은 완전 검정. 사이는 부드럽게.
            float a = Mathf.Clamp((d - 0.30f) / 0.48f, 0f, 1f);
            a = a * a * (3f - 2f * a);
            img.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
