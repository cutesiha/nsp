using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 오른쪽 CRT 전용. CCTV만. 왼쪽에서 선택한 방(SurveillanceTargetRoomId)을 보여준다.
// 배경은 실제 3D 작업실 월드(FacilityCctvWorld) 의 SubViewport 텍스처를 그대로 깔고,
// 그 위에 노이즈 / 스캔라인 / REC / 상태(신호 없음·손실·설비 고장) 오버레이만 얹는다.
public partial class CCTVMonitorView : Control
{
    public static CCTVMonitorView Instance { get; private set; }

    private static readonly Rect2 Frame = new(32, 60, 736, 452);

    private Font _font;
    private TextureRect _bgTex;
    private CctvPlaceholder _bgPlaceholder;
    private bool _feedBound;

    // 성능 훅: 지금 이 화면이 실제 3D 작업실 피드를 보여주고 있는가.
    // ControlRoom3DController 가 이걸 보고 CCTV 3D 월드 SubViewport 를 켜고 끈다
    // (NO SIGNAL / 전력 OFF 동안에는 두 번째 3D 렌더 패스를 통째로 멈춘다).
    public bool FeedVisible { get; private set; }
    private Control _employeeLayer;
    private ColorRect _tint;
    private Label _stateLabel;
    private Label _recLabel;
    private Label _camLabel;
    private Label _clock;
    private CctvOverheardCaption _overheard;
    private TextureRect _noise;

    // ── 괴물 ───────────────────────────────────────────────────────────
    // "얼마나 더 봐야 사라지는가" 게이지. **지금 보고 있는 방**에만 뜬다 —
    // 다른 방에 있는 괴물은 이 화면 어디에도 나타나지 않는다.
    // (비명에는 자막을 달지 않는다. 소리와 화면 흔들림만으로 충분하고,
    //  큰 글자가 뜨면 정작 방 안에서 무슨 일이 벌어지는지 가린다.)
    private ColorRect _dispelBack, _dispelFill, _dispelLabelBg;
    private Label _dispelLabel;
    private bool _ghostWired;
    private ImageTexture[] _noiseFrames;
    private float _noiseSwap;
    // 괴물을 보고 있는 동안 **내 화면 전체**에 끼는 회색 노이즈(0~1)와 그 최대 두께.
    // 0.34 는 모니터 안에 끼우던 때의 값이다 — 화면 전체에 그만큼 끼우면 아무것도
    // 안 보인다. 눈치챌 만큼만, "살짝".
    private float _ghostNoise;
    private const float GhostNoiseAdd = 0.20f;
    private int _noiseIdx;
    private float _recBlink;
    private float _glitch;

    // ── 아날로그 질감 · 채널 전환 ──────────────────────────────────────
    //
    // 영상 사각형(_bgTex)에만 거는 셰이더다. 곡률 · 잔상 · 색 번짐 · 트래킹 오류를
    // 여기서 내고, 스캔라인은 예전처럼 따로 깐 레이어가 맡는다(두 겹이면 모아레가 생긴다).
    private ShaderMaterial _feedMat;
    // 지금 보고 있는 채널. 방이든 복도든 한 문자열로 묶어 둔다 — 복도에서 방으로
    // 건너뛰는 것도 "채널이 바뀐 것" 이라 똑같이 신호가 한 번 끊겨야 한다.
    private string _lastChannel = "";
    private bool _channelSeen;
    private float _switchT;      // 1 → 0. 떨어지는 동안 화면이 흐르고 신호를 새로 잡는다
    private float _trackingT;    // 신호 불안정 세기(화면이 이미 흔들릴 때만 올라간다)
    // 이 방 · 통로에서 실제로 무슨 일이 벌어지고 있는가(붉은 경보 · 차폐 중).
    // **화면 왜곡은 여기서만 세진다** — 평상시 영상은 끝까지 또렷해야 한다(지시서 §3).
    private bool _feedUnstable;
    private const float SwitchSeconds = 0.26f;

    // 금기 이벤트 연출 훅.
    private double _forceFeedUntil = -1;   // 전력/고장과 무관하게 3D 피드를 강제로 보여줌
    private double _forceLostUntil = -1;   // SIGNAL LOST 강제
    private double _shakeUntil = -1;
    private float _shakeStrength;
    private readonly RandomNumberGenerator _rng = new();

    // PowerRoomTabooEvent 가 부른다.
    public void ForceFeed(float seconds) => _forceFeedUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    public void ForceSignalLost(float seconds) => _forceLostUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    public void Shake(float strength, float seconds = 0.35f)
    {
        _shakeStrength = Mathf.Max(_shakeStrength, strength);
        _shakeUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _bgPlaceholder = new CctvPlaceholder { Position = Frame.Position, Size = Frame.Size };
        AddChild(_bgPlaceholder);

        _bgTex = new TextureRect
        {
            Position = Frame.Position, Size = Frame.Size,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Visible = false, MouseFilter = MouseFilterEnum.Ignore,
        };
        _feedMat = BuildFeedMaterial();
        if (_feedMat != null) _bgTex.Material = _feedMat;
        AddChild(_bgTex);

        _employeeLayer = new Control { Position = Frame.Position, Size = Frame.Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_employeeLayer);

        _tint = new ColorRect { Position = Frame.Position, Size = Frame.Size, Color = new Color(0, 0, 0, 0), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_tint);

        _noiseFrames = new ImageTexture[6];
        for (int i = 0; i < 6; i++) _noiseFrames[i] = BuildNoise();
        _noise = new TextureRect
        {
            Texture = _noiseFrames[0], StretchMode = TextureRect.StretchModeEnum.Tile,
            Position = Frame.Position, Size = Frame.Size, MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.06f),
        };
        AddChild(_noise);
        AddChild(BuildScanlines());

        _stateLabel = Lbl("MONITOR 01에서 방을 선택하세요", 20, new Color(0.8f, 0.85f, 0.8f));
        _stateLabel.Position = new Vector2(Frame.Position.X, Frame.Position.Y + Frame.Size.Y / 2 - 16);
        _stateLabel.Size = new Vector2(Frame.Size.X, 32);
        _stateLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_stateLabel);

        // 교육 시연용 오류 표시(SetDemoError). 평소에는 꺼져 있다.
        _errorLabel = Lbl("ERROR!\nSIGNAL CORRUPTED", 48, new Color(1f, 0.22f, 0.18f));
        _errorLabel.Position = new Vector2(Frame.Position.X, Frame.Position.Y + Frame.Size.Y / 2 - 70);
        _errorLabel.Size = new Vector2(Frame.Size.X, 140);
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorLabel.VerticalAlignment = VerticalAlignment.Center;
        _errorLabel.AddThemeConstantOverride("outline_size", 8);
        _errorLabel.Visible = false;
        AddChild(_errorLabel);

        _recLabel = Lbl("● REC", 18, new Color(0.95f, 0.25f, 0.2f));
        _recLabel.Position = new Vector2(44, 24);
        AddChild(_recLabel);

        _camLabel = Lbl("", 22, new Color(0.78f, 0.90f, 0.84f));
        _camLabel.Position = new Vector2(44, 504);
        AddChild(_camLabel);

        // 같은 방 두 사람의 대화 자막(관계 시스템 Phase 2) — 상태 줄 바로 위.
        _overheard = new CctvOverheardCaption
        {
            Position = new Vector2(Frame.Position.X, Frame.Position.Y + Frame.Size.Y - 112f),
            Size = new Vector2(Frame.Size.X, 50f),
        };
        AddChild(_overheard);

        _clock = Lbl("--:--", 18, new Color(0.75f, 0.85f, 0.8f));
        _clock.Position = new Vector2(536, 24);
        _clock.Size = new Vector2(220, 24);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);

        BuildGhostOverlay();
    }

    // 소멸 게이지.
    //
    // **화면 위쪽에 둔다.** 예전에는 아래쪽이었는데, 거기는 그 방의 업무 진행바와
    // 엿들은 대화 자막이 이미 쓰는 자리다 — DAY0 교육에는 업무도 대화도 없어서 잘 보였지만,
    // 실제 근무에서는 진행바가 게이지 위에 그대로 겹쳐 그려져 "게이지가 안 뜬다" 가 됐다.
    // 위쪽은 방 천장이라 가릴 것이 없고, 괴물이 떠 있는 동안 가장 먼저 봐야 할 것이기도 하다.
    private void BuildGhostOverlay()
    {
        float ly = Frame.Position.Y + 10f;
        float gy = ly + 26f;
        // 글자 받침. 방 천장 조명이 밝아서 외곽선만으로는 글자가 묻힌다
        // (코어실 천장이 흰색이라 "관측 유지" 가 안 읽혔다).
        _dispelLabelBg = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.6f),
            Position = new Vector2(Frame.Position.X, ly - 2f),
            Size = new Vector2(Frame.Size.X, 28f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_dispelLabelBg);

        _dispelLabel = Lbl("관측 유지 — 시선을 떼지 마십시오", 16, new Color(0.72f, 0.96f, 0.84f));
        _dispelLabel.Position = new Vector2(Frame.Position.X, ly);
        _dispelLabel.Size = new Vector2(Frame.Size.X, 24f);
        _dispelLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _dispelLabel.Visible = false;
        AddChild(_dispelLabel);

        _dispelBack = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            Position = new Vector2(Frame.Position.X + 150f, gy),
            Size = new Vector2(Frame.Size.X - 300f, 18f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_dispelBack);
        _dispelFill = new ColorRect
        {
            Color = new Color(0.62f, 0.95f, 0.78f, 0.95f),
            Position = _dispelBack.Position + new Vector2(2f, 2f),
            Size = new Vector2(0f, 14f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_dispelFill);
    }

    // 소멸 게이지가 지금 화면에 떠 있는가(검사 전용 — 판정에는 쓰지 않는다).
    public bool DispelGaugeVisible => _dispelBack?.Visible ?? false;
    // 지금 보고 있는 방에 괴물이 있는 동안에만 게이지가 차오른다.
    private void TickGhostOverlay(float d, FacilitySimulation sim, string roomId, bool feed)
    {
        var ghost = sim?.Ghost;
        if (ghost != null && !_ghostWired)
        {
            ghost.Screamed += OnGhostScream;
            _ghostWired = true;
        }

        bool onScreen = feed && ghost is { Active: true } && ghost.ActiveRoomId == roomId;
        // 대회용에서는 관측해도 소멸하지 않으므로 **게이지만** 띄우지 않는다. 0% 인
        // 막대를 남겨 두면 "더 보면 채워지겠지" 하고 계속 쳐다보게 된다(지시서 §1).
        // 보고 있는 동안의 공포 반응은 두 모드 모두 그대로다.
        bool show = onScreen && GameModes.GhostDispelEnabled;
        if (_dispelBack.Visible != show)
        {
            _dispelBack.Visible = _dispelFill.Visible = _dispelLabel.Visible = _dispelLabelBg.Visible = show;
        }
        // 화면에 **실제로 떠 있는 동안에만** 관리자가 반응한다. 지도에서 방만 골라 두고
        // CCTV 가 꺼져 있으면 아무 일도 없다(지시서 §7-6).
        // 붉은 기운이 바깥에서 중앙으로 천천히 조여 온다 — 깜빡이지 않는다.
        NSP.Ui.AdminFearDirector.Instance?.GhostWatch(onScreen ? 0.35f + ghost.DispelRatio * 0.65f : 0f);

        // 그것을 보고 있는 동안 회색 노이즈가 낀다 — **모니터 안이 아니라 내 화면 전체에.**
        //
        // 처음에는 이 뷰의 노이즈 레이어를 두껍게 했었다. 그러면 "CCTV 기계가 지직거린다"
        // 로 읽힌다. 보여야 하는 것은 그게 아니라 **그것을 보고 있는 사람 쪽이 이상해지는
        // 것**이라, 화면 전체(AmbientOverlay)에 아주 살짝 끼우는 쪽으로 옮겼다.
        // 사라지면 붉은 비네팅 · 숨소리와 **같은 속도로** 빠진다(뚝 끊기지 않는다).
        _ghostNoise = onScreen
            ? Mathf.Min(1f, _ghostNoise + d * 2.4f)
            : Mathf.Max(0f, _ghostNoise - d * 0.8f);
        NSP.Ui.AmbientOverlay.Instance?.SetGhostNoise(_ghostNoise * GhostNoiseAdd);

        if (!show) return;

        float w = (_dispelBack.Size.X - 4f) * ghost.DispelRatio;
        _dispelFill.Size = new Vector2(w, 14f);
        _dispelLabel.Text = ghost.DispelRatio >= 0.999f
            ? "관측 완료"
            : $"관측 유지 — 시선을 떼지 마십시오  ({ghost.DispelRatio * 100f:0}%)";
    }

    // 벽 너머의 기척. 괴물의 비명과, 그 방에 있는 직원의 비명이 겹쳐 아주 작게 난다.
    // 직원 비명은 그 직원의 목소리로 만들어지므로(Sfx.PlayScream), 귀가 밝은 플레이어는
    // "누구 목소리인지"까지 짐작할 수 있다.
    private const float DistantGhostDb = -17f;
    private const float DistantScreamDb = -21f;

    private void PlayDistantHaunt(FacilitySimulation sim, string roomId)
    {
        Sfx.Instance?.PlayGhostScreamDistant(DistantGhostDb);

        // 그 방에 사람이 있으면 그 사람의 비명이 함께 샌다. 비어 있으면 괴물 소리만.
        var here = sim.OnDutyEmployeeIds(roomId).ToList();
        if (here.Count == 0) return;
        Sfx.Instance?.PlayScream(here[(int)(GD.Randi() % (uint)here.Count)], DistantScreamDb);
    }

    private void OnGhostScream(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        // 다른 방을 보고 있다 — 그래도 소리는 들린다.
        //
        // 괴물은 로그에도 알림에도 뜨지 않는다. 화면을 돌려 찾아내는 것이 이 계통의
        // 전부인데, 아무 기척이 없으면 찾을 이유 자체가 생기지 않는다.
        // 벽 너머로 새어 나오는 정도의 소리만 흘려 "어디선가 났다"까지만 알린다 —
        // 어느 방인지는 알려 주지 않으므로, 결국 CCTV 를 돌려 봐야 한다.
        if (sim.SurveillanceTargetRoomId != roomId)
        {
            PlayDistantHaunt(sim, roomId);
            return;
        }
        Shake(9f, 0.6f);
        FlashGlitch(1f);
        // 보고 있는 동안에도 **크게 지르지 않는다.** 전체 음량의 비명은 소멸하는 순간 한 번뿐이다
        // (GhostHauntSystem.Dispel) — 그래야 그 소리가 "다 봤다" 의 신호가 된다.
        // 여기서는 화면이 흔들리고 기척이 조금 커지는 정도로만 둔다.
        Sfx.Instance?.PlayGhostScreamDistant(-9f);
        // 소리가 닿아 모니터 기기 자체가 흔들린다 — 화면 안이 아니라 책상 위가 흔들려야
        // "저 안에서 난 소리" 가 이쪽으로 건너온 것처럼 읽힌다.
        NSP.View.ControlRoom3DController.Instance?.ShakeMonitor("02", 1.8f, 0.7f);
    }

    public override void _ExitTree()
    {
        // 괴물 시스템은 시뮬레이션(오토로드)과 함께 살아 있다 — 이 화면이 사라질 때
        // 손을 떼지 않으면 해제된 노드로 신호가 들어온다.
        if (_ghostWired && FacilitySimulation.Instance?.Ghost != null)
        {
            FacilitySimulation.Instance.Ghost.Screamed -= OnGhostScream;
            _ghostWired = false;
        }
        if (Instance == this) Instance = null;
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

    // 공포/이상현상 훅 — ControlRoom3DHorror 가 호출.
    public void FlashGlitch(float amount = 1f) => _glitch = Mathf.Max(_glitch, Mathf.Clamp(amount, 0f, 1f));

    // ── 교육 시연 : 방해공작으로 망가진 화면 ──────────────────────────────
    //
    // 가상 시뮬레이션에서 GUIDE-0 가 "괴물이 시설을 방해하는 경우도 있습니다. 방금처럼요." 를
    // 말하는 동안만 켠다. 실제 근무의 방해공작은 이렇게 화면을 망가뜨리지 않는다 — 보여 주기용이다.
    // 영상 위에 굵은 노이즈가 치직거리고 붉은 ERROR! 가 깜빡인다. 끄면 그 자리에서 원래대로.
    private bool _demoError;
    private Label _errorLabel;
    private double _nextDemoCrackle;

    public bool DemoErrorOn => _demoError;

    public void SetDemoError(bool on)
    {
        if (_demoError == on) return;
        _demoError = on;
        if (_errorLabel != null) _errorLabel.Visible = on;
        if (!on) return;
        _glitch = 1f;
        Shake(5f, 0.45f);
        _nextDemoCrackle = 0;
        Sfx.Instance?.Play("cctv_cut", -6f, 0.8f);
        Sfx.Instance?.Play("noise", -8f);
    }

    private void TickDemoError(float d)
    {
        if (!_demoError) return;
        double now = Time.GetTicksMsec() / 1000.0;
        // 노이즈는 늘 두껍게, 그 위에 가끔 크게 튄다.
        _glitch = Mathf.Max(_glitch, 0.7f + 0.3f * _rng.Randf());
        _noise.Modulate = new Color(1, 1, 1, 0.55f + 0.35f * _rng.Randf());
        _tint.Color = new Color(0.38f, 0.02f, 0.02f, 0.30f + 0.15f * _rng.Randf());
        if (_errorLabel != null)
            _errorLabel.Modulate = new Color(1, 1, 1, _rng.Randf() < 0.12f ? 0.25f : 1f);
        if (now >= _nextDemoCrackle)
        {
            _nextDemoCrackle = now + _rng.RandfRange(0.35f, 0.8f);
            Shake(_rng.RandfRange(2f, 5f), 0.18f);
            Sfx.Instance?.Play("cctv_cut", -14f, _rng.RandfRange(0.7f, 1.2f));
        }
    }

    // ── 아날로그 질감 ──────────────────────────────────────────────────

    // 셰이더가 없으면(파일 누락 · 임포트 전) 그냥 머티리얼 없이 돈다 — 예전 화면 그대로다.
    private static ShaderMaterial BuildFeedMaterial()
    {
        const string path = "res://shaders/cctv_feed.gdshader";
        if (!ResourceLoader.Exists(path)) return null;
        var shader = GD.Load<Shader>(path);
        if (shader == null) return null;

        var m = new ShaderMaterial { Shader = shader };
        // 표본을 더 뽑는 두 항목(잔상 · 색 번짐)은 낮은 품질에서 끈다. 나머지(곡률 ·
        // 비네팅 · 흔들림 · 잡음)는 표본이 늘지 않으므로 어느 기기에서나 그대로 둔다.
        bool lite = GameSettings.GraphicsQuality is GameSettings.Quality.Low
                                                 or GameSettings.Quality.VeryLow;
        m.SetShaderParameter("ghosting", lite ? 0f : 0.20f);
        m.SetShaderParameter("bleed", lite ? 0f : 0.32f);
        return m;
    }

    // 지금 보고 있는 채널 하나의 이름. 복도와 작업실을 같은 자리에서 비교해야
    // 둘 사이를 건너뛸 때도 신호가 한 번 끊긴다.
    private static string ChannelKey(FacilitySimulation sim)
    {
        if (sim == null) return "";
        string corridor = sim.SurveillanceCorridorId ?? "";
        return corridor.Length > 0 ? "C:" + corridor : "R:" + (sim.SurveillanceTargetRoomId ?? "");
    }

    // 채널이 바뀌는 순간 — 낡은 영상 장비가 신호를 놓쳤다가 새로 잡는다.
    //
    // 소리는 셋 다 아주 작다. 지도를 훑으며 방을 연달아 눌러 보는 것이 이 게임의
    // 기본 동작이라, 한 번 누를 때마다 크게 울리면 금방 피로해진다.
    private void BeginChannelSwitch()
    {
        _switchT = 1f;
        Sfx.Instance?.Play("relay_click", -19f, 1.1f);   // 기계식 채널 노브
        Sfx.Instance?.Play("cctv_cut", -21f, 1.35f);     // 신호가 끊겼다 잡히는 치직
    }

    // 전환 진행도와 신호 상태를 셰이더에 넘긴다.
    private void TickFeedShader(float d, bool feed, bool unstable)
    {
        _switchT = Mathf.Max(0f, _switchT - d / SwitchSeconds);
        if (_feedMat == null) return;

        // 트래킹 오류는 **이미 무슨 일이 벌어지고 있을 때만** 올라간다(지시서 §3).
        // 평상시에는 0 이고, 셰이더가 깔아 둔 아주 약한 띠만 지나간다.
        float want = Mathf.Clamp(_glitch * 0.8f + (unstable ? 0.22f : 0f), 0f, 1f);
        _trackingT = Mathf.MoveToward(_trackingT, want, d * (want > _trackingT ? 6f : 1.2f));

        _feedMat.SetShaderParameter("tracking", _trackingT);
        _feedMat.SetShaderParameter("switch_t", _switchT);
        _feedMat.SetShaderParameter("signal", feed ? 1f : 0f);
    }

    public override void _Process(double delta)
    {
        using var _perf = NSP.Debug.PerfStats.Scope(NSP.Debug.PerfStats.Span.CctvMonitor);
        FeedVisible = false;
        float d = (float)delta;
        var sim = FacilitySimulation.Instance;
        string roomId = sim?.SurveillanceTargetRoomId ?? "";

        // 채널이 바뀌었는가. 화면에 처음 잡히는 채널에는 전환 연출을 넣지 않는다 —
        // 근무를 시작하자마자 신호가 끊기면 고장난 것처럼 보인다.
        string channel = ChannelKey(sim);
        if (channel != _lastChannel)
        {
            if (_channelSeen && channel.Length > 2) BeginChannelSwitch();
            _lastChannel = channel;
            _channelSeen = true;
        }

        bool feed = UpdateFeed(d, sim, roomId);
        TickDemoError(d);
        TickFeedShader(d, feed, _feedUnstable || _demoError);
        // 전환하는 짧은 순간에는 노이즈 레이어도 같이 두꺼워진다. 상태(신호 없음 ·
        // 손실 · 정상)마다 값이 다르므로 각자 정한 값 **위에** 얹는다.
        if (_switchT > 0f)
            _noise.Modulate = new Color(1, 1, 1, Mathf.Min(0.95f, _noise.Modulate.A + _switchT * 0.45f));
        // 이상 개체 소멸 판정이 "화면이 실제로 켜져 있었는가"를 여기서 받아 간다.
        sim?.ReportCctvFeedLive(feed && FeedVisible);
        TickGhostOverlay(d, sim, roomId, feed);
        // 엿들은 대화 — 정상 피드가 나오고 근무 중일 때만 들린다.
        // 관리자 패드를 보는 동안에는 근무 시간이 멈추므로 대화도 그 자리에 멈춘다.
        // 스토리 컷인이 말하는 동안에도 멈춘다 — 두 대사가 동시에 나오지 않게(문서 §28).
        // 컷인이 끝나면 다시 첫 대기부터 정상으로 돌아간다(기존 Tick 의 복귀 경로).
        _overheard?.Tick(AdminPad3D.PausesGame ? 0f : d, roomId,
            feed && GameState.Instance?.CurrentPhase == GamePhase.Live
                 && !StoryCutinDirector.SuppressesAmbientDialogue);
    }

    // 화면 상태를 갱신하고, 방 영상이 정상적으로 나오고 있으면 true.
    private bool UpdateFeed(float d, FacilitySimulation sim, string roomId)
    {
        // 기본은 "안정" 이다. 아래에서 붉은 경보 · 차폐를 만났을 때만 다시 켠다.
        _feedUnstable = false;

        _clock.Text = FacilityClock(GameState.Instance?.DayTimeSeconds ?? 0f);

        _recBlink += d;
        _recLabel.Visible = (_recBlink % 1.4f) < 1.0f;

        _glitch = Mathf.Max(0f, _glitch - d * 1.6f);
        _noiseSwap += d;
        if (_noiseSwap > 0.05f)
        {
            _noiseSwap = 0f;
            _noiseIdx = (_noiseIdx + 1) % _noiseFrames.Length;
            _noise.Texture = _noiseFrames[_noiseIdx];
        }

        double now = Time.GetTicksMsec() / 1000.0;
        Position = now < _shakeUntil
            ? new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * _shakeStrength
            : Vector2.Zero;
        if (now >= _shakeUntil) _shakeStrength = Mathf.MoveToward(_shakeStrength, 0f, d * 60f);

        // 금기 이벤트: CCTV 강제 차단.
        if (now < _forceLostUntil)
        {
            ShowState("── SIGNAL LOST ──", darken: 0.94f);
            _noise.Modulate = new Color(1, 1, 1, 0.62f + _glitch * 0.3f);
            return false;
        }
        bool forceFeed = now < _forceFeedUntil;

        // 복도 카메라 — 작업실이 아니라 통로를 비춘다.
        // 전력 · 감시 설비 고장 규칙은 작업실과 **똑같이** 적용된다(복도만 계속 보이면 안 된다).
        string corridorId = sim?.SurveillanceCorridorId ?? "";
        if (!string.IsNullOrEmpty(corridorId)) return UpdateCorridorFeed(sim, corridorId, forceFeed);

        if (string.IsNullOrEmpty(roomId) || sim == null)
        {
            ShowState("MONITOR 01에서 방을 선택하세요", darken: 1f);
            _camLabel.Text = "";
            _noise.Modulate = new Color(1, 1, 1, 0.05f + _glitch * 0.5f);
            return false;
        }

        var def = sim.GetRoomDef(roomId);
        var state = sim.GetRoomState(roomId);
        // 금기 페널티(채널 혼선) 중에는 방 이름과 화면이 일부러 어긋난다.
        string camName = def?.DisplayName ?? roomId;
        if (NSP.Taboo.TabooRuleSystem.Instance?.IsCctvScrambled == true)
        {
            var ids = sim.GetRoomIds().ToList();
            if (ids.Count > 0)
            {
                int idx = Mathf.Abs(roomId.GetHashCode() + (int)(Time.GetTicksMsec() / 3000)) % ids.Count;
                camName = sim.GetRoomDef(ids[idx])?.DisplayName ?? camName;
            }
        }
        _camLabel.Text = $"CAM · {camName}";

        bool powered = GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch);
        bool systemFault = GameState.Instance.CctvSystemOffline;
        bool disconnected = sim.IsRoomCctvBlocked(roomId);

        if (!forceFeed && systemFault)
        {
            // FAIL-04: 경비실 감시 설비 고장 — 전력을 줘도 수리 전까지 신호 없음.
            ShowState("SIGNAL FAILURE\nSURVEILLANCE SYSTEM DOWN", darken: 0.92f);
            _noise.Modulate = new Color(1, 1, 1, 0.5f + _glitch * 0.4f);
            return false;
        }
        if (!forceFeed && disconnected)
        {
            ShowState("── SIGNAL LOST ──", darken: 0.92f);
            _noise.Modulate = new Color(1, 1, 1, 0.5f + _glitch * 0.4f);
            return false;
        }
        if (!forceFeed && !powered)
        {
            ShowState("NO SIGNAL\nCCTV POWER OFF", darken: 0.9f);
            _noise.Modulate = new Color(1, 1, 1, 0.16f);
            return false;
        }

        // 정상 피드 — 실제 3D 작업실 월드 텍스처를 그대로 깐다. 아직 준비 전이면 2D 폴백.
        _stateLabel.Visible = false;
        BindFacilityFeed();
        bool hasFeed = _bgTex.Texture != null;
        FeedVisible = hasFeed;
        _bgTex.Visible = hasFeed;
        _bgPlaceholder.Visible = !hasFeed;
        if (!hasFeed && _bgPlaceholder.RoomId != roomId)
        {
            _bgPlaceholder.RoomId = roomId;
            _bgPlaceholder.QueueRedraw();
        }

        bool red = state?.RedAlertLighting == true;
        _feedUnstable = red;
        _tint.Color = red ? new Color(0.5f, 0.03f, 0.03f, 0.35f) : new Color(0, 0, 0, 0.12f);
        _noise.Modulate = new Color(1, 1, 1, (red ? 0.14f : 0.06f) + _glitch * 0.55f);

        RebuildEmployees(sim, state);
    
        return true;
    }

    private void ShowState(string text, float darken)
    {
        _stateLabel.Visible = true;
        _stateLabel.Text = text;
        _bgTex.Visible = false;
        _bgPlaceholder.Visible = false;
        _tint.Color = new Color(0, 0, 0, darken);
        foreach (var c in _employeeLayer.GetChildren()) c.QueueFree();
    }

    // 3D 작업실 월드 SubViewport 텍스처를 배경으로 한 번만 연결한다.
    private void BindFacilityFeed()
    {
        if (_feedBound) return;
        var vp = ControlRoom3DController.Instance?.FacilityCctvViewport;
        var tex = vp?.GetTexture();
        if (tex == null) return;
        _bgTex.Texture = tex;
        _feedBound = true;
    }

    // 복도 영상. 작업실 피드와 같은 3D 뷰포트를 쓰고(FacilityCctvWorld 가 복도로 전환해 둔다)
    // 차폐문 상태만 화면 아래에 한 줄로 덧붙인다.
    private bool UpdateCorridorFeed(FacilitySimulation sim, string corridorId, bool forceFeed)
    {
        var seg = sim.Corridors.ById(corridorId);
        if (seg == null)
        {
            ShowState("MONITOR 01에서 방을 선택하세요", darken: 1f);
            _camLabel.Text = "";
            return false;
        }

        _camLabel.Text = $"CAM · {seg.CctvCameraId} · {seg.DisplayName}";

        bool powered = GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch);
        if (!forceFeed && GameState.Instance.CctvSystemOffline)
        {
            ShowState("SIGNAL FAILURE\nSURVEILLANCE SYSTEM DOWN", darken: 0.92f);
            _noise.Modulate = new Color(1, 1, 1, 0.5f + _glitch * 0.4f);
            return false;
        }
        if (!forceFeed && !powered)
        {
            ShowState("NO SIGNAL\nCCTV POWER OFF", darken: 0.9f);
            _noise.Modulate = new Color(1, 1, 1, 0.16f);
            return false;
        }

        _stateLabel.Visible = false;
        BindFacilityFeed();
        bool hasFeed = _bgTex.Texture != null;
        FeedVisible = hasFeed;
        _bgTex.Visible = hasFeed;
        _bgPlaceholder.Visible = !hasFeed;

        _feedUnstable = seg.Sealed || seg.Moving;
        _tint.Color = seg.Sealed ? new Color(0.42f, 0.05f, 0.04f, 0.15f) : new Color(0, 0, 0, 0.12f);
        _noise.Modulate = new Color(1, 1, 1, 0.06f + _glitch * 0.55f);

        // 화면 아래 한 줄 — 이 문이 지금 어떤 상태인가.
        var status = _employeeLayer.GetNodeOrNull<Label>("Status") ?? Lbl("", 15, new Color(0.85f, 0.9f, 0.85f));
        if (status.GetParent() == null) { status.Name = "Status"; _employeeLayer.AddChild(status); }
        status.Text = seg.IsBlockable
            ? $"차폐문 {seg.StatusText}" + (seg.Sealed ? $"   유지 {seg.SealedSeconds:0}s / {seg.MaxSealSeconds:0}s" : "")
            : "차폐 설비 없음";
        status.AddThemeColorOverride("font_color", seg.Sealed
            ? new Color(1f, 0.46f, 0.38f)
            : seg.Moving ? new Color(1f, 0.82f, 0.42f) : new Color(0.72f, 0.82f, 0.78f));
        status.Position = new Vector2(0, Frame.Size.Y - 60);
        status.Size = new Vector2(Frame.Size.X, 40);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        return true;
    }

    private void RebuildEmployees(FacilitySimulation sim, RoomState state)
    {
        // 직원은 이제 3D 월드(FacilityCctvWorld)에서 실제 플레이스홀더로 보인다 — 여기선 방 상태 텍스트만.
        if (state == null) return;

        var block = RoomStatusText.BuildRoomStatusBlock(state.RoomId);
        // 상태 블록은 프레임 하단 중앙에.
        var existing = _employeeLayer.GetNodeOrNull<Label>("Status");
        var status = existing ?? Lbl("", 15, new Color(0.85f, 0.9f, 0.85f));
        status.Name = "Status";
        // 이상이 있을 때만 쓴다. "정상 근무 중" 은 아무것도 알려주지 않으면서 화면 아래를
        // 늘 차지해, 정작 문제가 떴을 때 그 줄이 눈에 덜 띄게 만들고 있었다.
        status.Text = block?.Replace("\n", "   ") ?? "";
        status.Position = new Vector2(0, Frame.Size.Y - 60);
        status.Size = new Vector2(Frame.Size.X, 40);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        if (existing == null) _employeeLayer.AddChild(status);
    }

    private TextureRect BuildScanlines()
    {
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        for (int y = 0; y < 4; y++)
        {
            var c = y % 2 == 0 ? new Color(0, 0, 0, 0.16f) : new Color(0, 0, 0, 0f);
            for (int x = 0; x < 4; x++) img.SetPixel(x, y, c);
        }
        return new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(img),
            StretchMode = TextureRect.StretchModeEnum.Tile,
            Position = Frame.Position, Size = Frame.Size, MouseFilter = MouseFilterEnum.Ignore,
        };
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

    // 배경 placeholder — 방별 가구 배치(CctvView.RoomFurniture 재사용)를 그린다.
    private partial class CctvPlaceholder : Control
    {
        public string RoomId = "";

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.13f, 0.14f));
            if (!CctvView.RoomFurniture.TryGetValue(RoomId, out var pieces)) return;

            float sx = Size.X / 330f, sy = Size.Y / 200f;
            foreach (var (rect, color) in pieces)
                DrawRect(new Rect2(rect.Position * new Vector2(sx, sy), rect.Size * new Vector2(sx, sy)),
                    color.Lerp(new Color(0.1f, 0.1f, 0.1f), 0.15f));
        }
    }
}
