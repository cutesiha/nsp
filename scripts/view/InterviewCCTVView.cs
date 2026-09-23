using Godot;
using NSP.Core;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 오른쪽 CRT — 휴게시간 인터뷰 화면.
// 근무 중 CCTV(CCTVMonitorView)와 달리 감시화면이 아니라 "기록용 인터뷰 단말"로 보이게 한다.
//   · 배경 : FacilityCctvWorld 의 3D 방을 사이드뷰(측면 시점)로 렌더한 텍스처
//            (근무 CCTV 는 같은 월드를 코너 부감으로 본다 — 카메라만 다르다)
//   · 인물 : 왼쪽 BREAK ROOM 탑뷰에서 고른 직원의 스탠딩 원화
//   · 노이즈는 아주 옅게만 — 읽기가 우선이다.
// 실제 대화는 여기서 굴리지 않는다. 기존대로 책상 위 전화기를 들면 PhoneCallHud 가 뜬다.
public partial class InterviewCCTVView : Control
{
    private static readonly Rect2 Frame = new(28, 56, 744, 460);

    private Font _font;
    private TextureRect _roomFeed;      // 3D 사이드뷰 배경
    private ColorRect _roomFallback;    // 3D 준비 전 대체 배경
    private TextureRect _portrait;
    private Control _portraitBox;
    private Label _stateLabel;
    private Label _recLabel;
    private Label _clock;
    private Label _nameLabel;
    private Label _statusLabel;
    private ColorRect _namePlate;
    private TextureRect _noise;
    private ImageTexture[] _noiseFrames;
    private float _noiseSwap;
    private int _noiseIdx;
    private float _recBlink;
    private string _lastEmployee = "\0";
    private bool _feedBound;

    // 스탠딩 일러 CRT 셰이더(nsp_crt_glow_standing) — 청록 톤 + 어둡게 + 중앙 발광.
    // 머티리얼은 하나만 두고, 직원이 바뀔 때만 그 직원의 발광 값(EmployeeDef.StandingGlow*)을 넣는다
    // (입 모양 프레임이 바뀌어도 값은 그대로 유지된다).
    private ShaderMaterial _standingMat;
    private float _baseDarkness = 0.84f;
    private float _baseGlowAmt = 0.9f;
    private float _defaultGlowAmt = 0.9f;   // 셰이더 기본값 — 긴장 발광의 비율 기준
    private Tween _tensionTween;

    public static InterviewCCTVView Instance { get; private set; }

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 3D 방이 아직 준비되지 않았을 때만 보이는 단색 배경.
        _roomFallback = new ColorRect
        {
            Position = Frame.Position, Size = Frame.Size,
            Color = new Color(0.10f, 0.10f, 0.12f), MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_roomFallback);

        _roomFeed = new TextureRect
        {
            Position = Frame.Position, Size = Frame.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_roomFeed);

        // 배경을 살짝 눌러 스탠딩 원화가 앞으로 떠 보이게 한다.
        var tint = new ColorRect
        {
            Position = Frame.Position, Size = Frame.Size,
            Color = new Color(0.02f, 0.03f, 0.05f, 0.28f), MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(tint);

        _portraitBox = new Control
        {
            // 스탠딩 원화의 발끝이 화면 아래에 붙도록 프레임 전체 높이를 쓴다.
            Position = Frame.Position,
            Size = Frame.Size,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_portraitBox);

        // 크기는 ApplyPortrait 가 직접 계산한다. 여기서 화면에 맞춰 늘리면(KeepAspect)
        // 원화마다 잘라낸 여백이 달라서 키가 제각각으로 보인다.
        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _portraitBox.AddChild(_portrait);
        BuildStandingMaterial();

        _stateLabel = Lbl("왼쪽 BREAK ROOM 에서 직원을 선택하세요", 20, new Color(0.8f, 0.85f, 0.8f));
        _stateLabel.Position = new Vector2(Frame.Position.X, Frame.Position.Y + Frame.Size.Y / 2f - 16f);
        _stateLabel.Size = new Vector2(Frame.Size.X, 32f);
        _stateLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_stateLabel);

        // 인터뷰 화면은 감시화면이 아니다 — 노이즈는 존재감만 남긴다.
        _noiseFrames = new ImageTexture[6];
        for (int i = 0; i < 6; i++) _noiseFrames[i] = BuildNoise();
        _noise = new TextureRect
        {
            Texture = _noiseFrames[0], StretchMode = TextureRect.StretchModeEnum.Tile,
            Position = Frame.Position, Size = Frame.Size, MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.028f),
        };
        AddChild(_noise);

        // 하단 명패 — 이름 / 상태.
        _namePlate = new ColorRect
        {
            Position = new Vector2(Frame.Position.X, Frame.End.Y - 62f),
            Size = new Vector2(Frame.Size.X, 62f),
            Color = new Color(0.03f, 0.05f, 0.06f, 0.82f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_namePlate);

        _nameLabel = Lbl("", 24, new Color(0.92f, 0.94f, 0.9f));
        _nameLabel.Position = new Vector2(Frame.Position.X + 22f, Frame.End.Y - 54f);
        _nameLabel.Size = new Vector2(Frame.Size.X - 44f, 30f);
        AddChild(_nameLabel);

        _statusLabel = Lbl("", 14, new Color(0.62f, 0.72f, 0.76f));
        _statusLabel.Position = new Vector2(Frame.Position.X + 22f, Frame.End.Y - 26f);
        _statusLabel.Size = new Vector2(Frame.Size.X - 44f, 22f);
        AddChild(_statusLabel);

        var title = Lbl("INTERVIEW", 17, new Color(0.72f, 0.86f, 0.9f));
        title.Position = new Vector2(Frame.Position.X + 4f, 24f);
        AddChild(title);

        _recLabel = Lbl("● REC", 16, new Color(0.95f, 0.35f, 0.28f));
        _recLabel.Position = new Vector2(Frame.Position.X + 132f, 25f);
        AddChild(_recLabel);

        _clock = Lbl("--:--", 16, new Color(0.75f, 0.85f, 0.8f));
        _clock.Position = new Vector2(Frame.End.X - 224f, 25f);
        _clock.Size = new Vector2(220, 24);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 3);
        l.MouseFilter = MouseFilterEnum.Ignore;
        return l;
    }

    public override void _EnterTree()
    {
        Instance = this;
        InterviewSession.Confronted += OnConfronted;
    }

    public override void _ExitTree()
    {
        InterviewSession.Confronted -= OnConfronted;
        if (Instance == this) Instance = null;
    }

    // --- 스탠딩 일러 CRT 셰이더 ---------------------------------------------

    private void BuildStandingMaterial()
    {
        var cfg = Config.Instance?.Data;
        string path = cfg?.StandingShaderPath ?? "";
        // 경로가 비어 있으면 셰이더 · 발광 · 긴장 연출을 모두 끈다(원화 그대로 표시).
        if (string.IsNullOrEmpty(path)) return;
        var shader = GD.Load<Shader>(path);
        if (shader == null)
        {
            GD.PushWarning($"InterviewCCTVView: 스탠딩 일러 셰이더를 찾지 못했습니다: {path}");
            return;
        }
        _standingMat = new ShaderMaterial { Shader = shader };
        // 스캔라인 · 그레인은 모니터 셰이더(crt_screen)가 이미 그린다 — 일러에서는 끈다.
        _standingMat.SetShaderParameter("scan_amt", cfg?.StandingScanAmt ?? 0f);
        _standingMat.SetShaderParameter("grain_amt", cfg?.StandingGrainAmt ?? 0f);
        // 긴장 연출이 끝나면 돌아올 밝기 = 셰이더에 적힌 기본값.
        var dv = RenderingServer.ShaderGetParameterDefault(shader.GetRid(), "darkness");
        if (dv.VariantType != Variant.Type.Nil) _baseDarkness = dv.AsSingle();
        var gv = RenderingServer.ShaderGetParameterDefault(shader.GetRid(), "glow_amt");
        if (gv.VariantType != Variant.Type.Nil && gv.AsSingle() > 0f) _defaultGlowAmt = gv.AsSingle();
        _portrait.Material = _standingMat;
    }

    // 직원 한 명의 발광 값. 흰 옷처럼 밝은 원화는 EmployeeDef 에서 낮춰 둔다.
    private void ApplyStandingGlow(NSP.Data.EmployeeDef def)
    {
        if (_standingMat == null || def == null) return;
        StopTension();
        _baseGlowAmt = def.StandingGlowAmt;
        _standingMat.SetShaderParameter("glow_amt", def.StandingGlowAmt);
        _standingMat.SetShaderParameter("glow_center", def.StandingGlowCenter);
        _standingMat.SetShaderParameter("darkness", _baseDarkness);
    }

    // 외부에서 부르는 강도 조절 — 더 어둡게(darkness↓) + 빛 번짐(glowAmt↑).
    public void SetCrtIntensity(float darkness, float glowAmt)
    {
        if (_standingMat == null) return;
        StopTension();
        _standingMat.SetShaderParameter("darkness", darkness);
        _standingMat.SetShaderParameter("glow_amt", glowAmt);
    }

    // 지금 직원의 평소 값으로 되돌린다.
    public void ResetCrtIntensity() => SetCrtIntensity(_baseDarkness, _baseGlowAmt);

    // 긴장 순간 — 확 어두워지며 빛이 번졌다가, 잠시 뒤 평소 값으로 서서히 돌아온다.
    public void PulseTension()
    {
        if (_standingMat == null) return;
        var cfg = Config.Instance?.Data;
        float dark = cfg?.StandingTensionDarkness ?? 0.5f;
        // 발광은 직원 비율대로 — 기본 직원은 설정값 그대로, 발광을 낮춰 둔 흰 옷 직원은 그만큼 덜 번진다.
        float glow = (cfg?.StandingTensionGlowAmt ?? 2.2f) * (_baseGlowAmt / _defaultGlowAmt);
        float hold = cfg?.StandingTensionHoldSeconds ?? 2.5f;
        float fade = cfg?.StandingTensionFadeSeconds ?? 1.2f;

        StopTension();
        _standingMat.SetShaderParameter("darkness", dark);
        _standingMat.SetShaderParameter("glow_amt", glow);
        _tensionTween = CreateTween();
        _tensionTween.TweenInterval(hold);
        _tensionTween.SetParallel(true);
        _tensionTween.TweenMethod(Callable.From<float>(v => _standingMat.SetShaderParameter("darkness", v)),
            dark, _baseDarkness, fade);
        _tensionTween.TweenMethod(Callable.From<float>(v => _standingMat.SetShaderParameter("glow_amt", v)),
            glow, _baseGlowAmt, fade);
    }

    private void StopTension()
    {
        if (_tensionTween != null && _tensionTween.IsValid()) _tensionTween.Kill();
        _tensionTween = null;
    }

    // 심문 중 모순 추궁이 성립한 순간 — 지금 화면에 떠 있는 그 직원이면 긴장 연출.
    // 자료 두 장을 함께 들이민 순간. 성립하지 않은 조합(None)에는 긴장 연출을 넣지 않는다 —
    // 화면이 "지금 뭔가 맞았다"고 알려 주면 추리가 사라진다.
    // (kind/variant 별 세기 차등은 2차 작업 §4 19번이다.)
    private void OnConfronted(string employeeId, NSP.Dialogue.ConfrontKind kind, string variant)
    {
        if (employeeId == _lastEmployee && kind != NSP.Dialogue.ConfrontKind.None) PulseTension();
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _clock.Text = FacilityClock(GameState.Instance?.DayTimeSeconds ?? 0f);

        _recBlink += d;
        _recLabel.Visible = (_recBlink % 1.4f) < 1.0f;

        _noiseSwap += d;
        if (_noiseSwap > 0.09f)
        {
            _noiseSwap = 0f;
            _noiseIdx = (_noiseIdx + 1) % _noiseFrames.Length;
            _noise.Texture = _noiseFrames[_noiseIdx];
        }

        BindRoomFeed();

        string empId = RestRosterView.Instance?.SelectedEmployeeId ?? "";
        // 말하는 입 모양 · 표정이 있는 직원(standing_v2)은 매 프레임 원화를 갈아 끼운다.
        if (empId == _lastEmployee)
        {
            var talking = EmployeeMouthAnimator.PortraitFor(empId);
            if (talking != null && talking != _portrait.Texture) ApplyPortrait(talking);
            return;
        }
        _lastEmployee = empId;

        var sim = FacilitySimulation.Instance;
        var def = string.IsNullOrEmpty(empId) ? null : sim?.GetEmployeeDef(empId);
        var st = string.IsNullOrEmpty(empId) ? null : sim?.GetEmployeeState(empId);

        if (def == null || st == null)
        {
            _portrait.Texture = null;
            _stateLabel.Visible = true;
            _stateLabel.Text = "왼쪽 BREAK ROOM 에서 직원을 선택하세요";
            _namePlate.Visible = false;
            _nameLabel.Text = "";
            _statusLabel.Text = "";
            return;
        }

        _stateLabel.Visible = false;
        ApplyPortrait(EmployeeMouthAnimator.PortraitFor(empId) ?? def.StandingImage ?? def.FacePortrait);
        ApplyStandingGlow(def);
        _namePlate.Visible = true;
        _nameLabel.Text = def.Codename;
        _statusLabel.Text = !st.Alive ? "응답 없음 · 기록 종료"
            : st.Isolated ? "격리됨 · 인터뷰 가능"
            : "휴게 중 · 전화 연결 대기";
    }

    // --- 스탠딩 원화 배치 ------------------------------------------------
    //
    // 원화는 캐릭터마다 따로 잘려 있어 캔버스 크기가 제각각이다. 화면에 "맞춰" 그리면
    // 키가 작은 캐릭터가 여우만큼 커 보인다. 그래서 모든 원화에 같은 배율을 적용하고
    // 발끝을 화면 아래에 붙인다 — 원화 안의 실제 그림 높이가 곧 키가 된다.
    //
    // 배율 기준은 가장 큰 원화가 표시 영역에 딱 들어가는 값이며,
    // 나머지는 그 비율대로 자동으로 작아진다. (원화를 교체하면 이 기준도 다시 확인할 것)

    // 원화 위쪽 여백 — 제일 큰 캐릭터의 머리가 프레임 위선에 닿지 않게 한다.
    private const float PortraitTopMargin = 12f;
    // 얼굴이 잘 보이도록 전원에게 같은 배율로 키운다. 키가 가장 큰 직원의 머리가 위로 잘리지 않게
    // 여섯 명 모두 같은 만큼(아래 PortraitDrop) 내린다 — 대신 다리 쪽이 화면 아래로 잘린다.
    private const float PortraitZoom = 1.45f;

    private static readonly System.Collections.Generic.Dictionary<ulong, Rect2I> _contentBoxes = new();
    private static float _portraitUnit = -1f;

    private void ApplyPortrait(Texture2D tex)
    {
        _portrait.Texture = tex;
        if (tex == null) return;

        Rect2I box = ContentBox(tex);
        float avail = _portraitBox.Size.Y - PortraitTopMargin;
        float unit = PortraitUnit(avail) * PortraitZoom;
        // 확대로 늘어난 만큼 전원 똑같이 내린다 → 가장 큰 직원의 머리가 원래 자리(위 여백)에 머문다.
        float drop = avail * (PortraitZoom - 1f);

        _portrait.Size = new Vector2(tex.GetWidth() * unit, tex.GetHeight() * unit);
        _portrait.Position = new Vector2(
            // 가로는 그림의 중심을 표시 영역 중앙에.
            _portraitBox.Size.X / 2f - (box.Position.X + box.Size.X / 2f) * unit,
            // 세로는 발끝을 바닥에 맞춘 자리에서 공통 drop 만큼 아래로.
            _portraitBox.Size.Y - (box.Position.Y + box.Size.Y) * unit + drop
                - (FacilitySimulation.Instance?.GetEmployeeDef(_lastEmployee)?.InterviewPortraitLift ?? 0f));
    }

    // 여섯 명 중 가장 큰 원화가 표시 높이에 맞도록 하는 공통 배율.
    private static float PortraitUnit(float availableHeight)
    {
        if (_portraitUnit > 0f) return _portraitUnit;

        float tallest = 1f;
        var sim = FacilitySimulation.Instance;
        if (sim != null)
        {
            foreach (string id in sim.GetEmployeeIds())
            {
                var t = sim.GetEmployeeDef(id)?.StandingImage;
                if (t != null) tallest = Mathf.Max(tallest, ContentBox(t).Size.Y);
            }
        }
        _portraitUnit = availableHeight / Mathf.Max(1f, tallest);
        return _portraitUnit;
    }

    // 원화에서 실제로 그림이 그려진 영역(투명 여백 제외). 원화를 교체해도 자동으로 다시 잡힌다.
    public static Rect2I ContentBox(Texture2D tex)
    {
        ulong key = tex.GetInstanceId();
        if (_contentBoxes.TryGetValue(key, out var cached)) return cached;

        Rect2I box = Measure(tex.GetImage()) ?? new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight());
        _contentBoxes[key] = box;
        return box;
    }

    // 알파가 충분히 진한 픽셀만 그림으로 본다. Image.GetUsedRect() 는 알파가 1이라도
    // 포함해서, 원화 위쪽에 남은 아주 옅은 선까지 키로 계산되어 비율이 어긋난다.
    private static Rect2I? Measure(Image img)
    {
        if (img == null) return null;
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);

        byte[] data = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        if (data == null || data.Length < w * h * 4) return null;

        const int AlphaThreshold = 24;
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (data[row + x * 4 + 3] <= AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        if (maxX < 0) return null;
        return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // 3D 작업실 월드(FacilityCctvWorld)의 SubViewport 텍스처를 배경으로 한 번만 연결한다.
    // 휴게시간에는 그 월드가 인터뷰용 사이드뷰 카메라로 전환된다(FacilityCctvWorld 참조).
    private void BindRoomFeed()
    {
        if (_feedBound) return;
        var tex = ControlRoom3DController.Instance?.FacilityCctvViewport?.GetTexture();
        if (tex == null) return;
        _roomFeed.Texture = tex;
        _roomFeed.Visible = true;
        _roomFallback.Visible = false;
        _feedBound = true;
    }

    private static ImageTexture BuildNoise()
    {
        var img = Image.CreateEmpty(96, 96, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator();
        for (int y = 0; y < 96; y++)
        for (int x = 0; x < 96; x++)
        {
            float v = rng.Randf();
            img.SetPixel(x, y, new Color(v, v, v));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // 플레이어에게 보이는 시각 — 한글 시간대 표기(밤/새벽). 환산 · 표기는 DialogueClock 한 곳에서만.
    private static string FacilityClock(float t) => NSP.Dialogue.DialogueClock.Text(t);
}
