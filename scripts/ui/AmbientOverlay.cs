using Godot;
using NSP.Core;

namespace NSP.Ui;

// 모든 씬 위에 항상 깔리는 분위기 오버레이(autoload CanvasLayer).
//  - 화면 가장자리 비네트(살짝 어둡게)
//  - 미세한 필름 노이즈(계속 지글거림)
//  - 아주 가끔 화면이 반짝(전기 불량처럼) + 짧은 지직 소리
// HorrorDirector가 공포 이벤트 때 Flash() / PulseNoise()로 강하게 끌어올릴 수 있다.
public partial class AmbientOverlay : CanvasLayer
{
    public static AmbientOverlay Instance { get; private set; }

    private const int NoiseFrames = 8;
    // 모든 씬에서 확실히 보이도록 — 밝은 배경(타이틀/스케줄)에서도 지글거림이 남는 수치.
    private const float BaseNoiseAlpha = 0.12f;

    private TextureRect _vignetteRect;
    private TextureRect _centerGlow;
    private TextureRect _noiseRect;
    private ColorRect _flash;
    private ColorRect _shutdownDim;
    private Label _shutdownLabel;
    private bool _shutdown;
    private float _shutdownT;
    private ImageTexture[] _noiseTex;

    private float _noiseSwap;
    private int _noiseIdx;
    private float _extraNoise;
    private float _nextFlicker;
    private float _clock;
    private float _sceneIntensity = 1f;

    public override void _EnterTree() => Instance = this;

    // 3D 씬처럼 자체 노이즈/CRT 효과가 이미 있는 화면에서 전역 오버레이를 줄인다.
    // 2D 씬은 1.0 그대로. 씬이 바뀌면 그 씬에서 다시 설정한다.
    public void SetSceneIntensity(float mult) => _sceneIntensity = Mathf.Clamp(mult, 0f, 1f);

    public override void _Ready()
    {
        Layer = 100;

        _vignetteRect = new TextureRect
        {
            Texture = BuildVignette(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0.9f),
        };
        _vignetteRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_vignetteRect);

        // 가운데는 좀 더 밝게(가산 블렌드).
        _centerGlow = new TextureRect
        {
            Texture = BuildCenterGlow(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _centerGlow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _centerGlow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_centerGlow);

        _noiseTex = new ImageTexture[NoiseFrames];
        for (int i = 0; i < NoiseFrames; i++)
            _noiseTex[i] = BuildNoise();

        _noiseRect = new TextureRect
        {
            Texture = _noiseTex[0],
            StretchMode = TextureRect.StretchModeEnum.Tile,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, BaseNoiseAlpha),
        };
        _noiseRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_noiseRect);

        _flash = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);

        // SHUT DOWN — 전력 0. 화면이 확 어두워지고 붉은 대형 문구.
        _shutdownDim = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _shutdownDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_shutdownDim);

        _shutdownLabel = new Label
        {
            Text = "SHUT DOWN",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 0.25f, 0.2f, 0f),
        };
        _shutdownLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _shutdownLabel.AddThemeFontOverride("font", NSP.View.ViewFont.Default);
        _shutdownLabel.AddThemeFontSizeOverride("font_size", 84);
        _shutdownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.22f, 0.16f));
        _shutdownLabel.AddThemeConstantOverride("outline_size", 8);
        _shutdownLabel.AddThemeColorOverride("font_outline_color", new Color(0.1f, 0f, 0f));
        AddChild(_shutdownLabel);

        _nextFlicker = (float)GD.RandRange(4.0, 9.0);
    }

    // 전력 0 → SHUT DOWN 표시 on/off.
    public void SetShutdown(bool on) => _shutdown = on;

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _clock += d;

        // 노이즈 프레임 스왑 (빠르게 지글)
        _noiseSwap += d;
        if (_noiseSwap >= 0.05f)
        {
            _noiseSwap = 0f;
            _noiseIdx = (_noiseIdx + 1) % NoiseFrames;
            _noiseRect.Texture = _noiseTex[_noiseIdx];
        }

        _extraNoise = Mathf.Max(0f, _extraNoise - d * 0.9f);
        _noiseRect.Modulate = new Color(1f, 1f, 1f, (BaseNoiseAlpha + _extraNoise) * _sceneIntensity);
        _vignetteRect.Modulate = new Color(1f, 1f, 1f, 0.9f * Mathf.Lerp(0.5f, 1f, _sceneIntensity));
        _centerGlow.Modulate = new Color(1f, 1f, 1f, 0.18f * _sceneIntensity);

        // 가끔 반짝
        _nextFlicker -= d;
        if (_nextFlicker <= 0f)
        {
            _nextFlicker = (float)GD.RandRange(4.0, 10.0);
            Flash(0.55f);
        }

        // SHUT DOWN 페이드 + 깜빡이는 문구
        _shutdownT = Mathf.Clamp(_shutdownT + (_shutdown ? d * 2.5f : -d * 3f), 0f, 1f);
        _shutdownDim.Color = new Color(0f, 0f, 0f, 0.72f * _shutdownT);
        float blink = _shutdown ? 0.55f + 0.45f * Mathf.Sin(_clock * 6f) : 1f;
        _shutdownLabel.Modulate = new Color(1f, 1f, 1f, _shutdownT * blink);
        if (_shutdownT > 0.01f && _shutdown && Mathf.PosMod(_clock, 0.9f) < d) _extraNoise = Mathf.Max(_extraNoise, 0.3f);
    }

    // 화면 반짝(전기 불량). strength 0~1.
    public void Flash(float strength = 0.6f)
    {
        strength = Mathf.Clamp(strength, 0.05f, 1f);
        var t = CreateTween();
        t.TweenProperty(_flash, "color:a", strength, 0.03);
        t.TweenProperty(_flash, "color:a", 0f, 0.12 + 0.16 * strength);
        _extraNoise = Mathf.Max(_extraNoise, 0.2f * strength);
        Sfx.Instance?.Play("flicker", -12f + 8f * strength);
    }

    // 노이즈를 잠깐 확 끌어올린다(공포 이벤트 중).
    public void PulseNoise(float amount = 0.25f)
    {
        _extraNoise = Mathf.Max(_extraNoise, Mathf.Clamp(amount, 0f, 0.6f));
    }

    private static ImageTexture BuildNoise()
    {
        var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator();
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 128; x++)
        {
            // 어두운 쪽으로 살짝 치우친 그레인 — 밝은 배경(타이틀/스케줄)에서도 지지직이 보인다.
            float v = Mathf.Pow(rng.Randf(), 1.5f);
            img.SetPixel(x, y, new Color(v, v, v));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildVignette()
    {
        const int w = 320, h = 200;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = w / 2f, cy = h / 2f;
        float maxD = Mathf.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x - cx) / maxD;
            float dy = (y - cy) / maxD;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            // 중앙은 투명, 바깥·모서리로 갈수록 부드럽게 짙어짐(블러 느낌).
            float a = Mathf.Clamp((d - 0.42f) / 0.5f, 0f, 1f);
            a = Mathf.Pow(a, 1.7f) * 0.9f;
            img.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // 화면 가운데를 살짝 밝히는 부드러운 원형 하이라이트(가산 블렌드로 얹는다).
    private static ImageTexture BuildCenterGlow()
    {
        const int w = 256, h = 160;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = w / 2f, cy = h / 2f;
        float maxD = Mathf.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x - cx) / maxD, dy = (y - cy) / maxD;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp(1f - d / 0.7f, 0f, 1f);
            a = Mathf.Pow(a, 2.2f);
            img.SetPixel(x, y, new Color(0.55f, 0.65f, 0.82f, a)); // 살짝 파란 빛
        }
        return ImageTexture.CreateFromImage(img);
    }
}
