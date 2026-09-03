using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

// 공포 연출 디렉터 — LEVEL 2 / LEVEL 3.
// 기본 공포 연출은 실제 게임 상태/사건 로그에만 반응한다. 단, 금기 이상현상을 본 뒤에는
// 그 사건의 후속 효과로 근무 중 무작위 시각에 한 번만 초근접 점프스케어가 발생한다.
//
//  LEVEL 2 (자주): 금기 위반 임박, 발전기/정전/CCTV 단절, 사보타주, 방치 사고
//    → 화면 흔들림 + 순간 확대 + CCTV 노이즈 폭증 + 밝기 급변 + 스팅어
//
//  LEVEL 3 (근무당 1~4회): 실제 금기 위반 또는 사망
//    → 스팅어 → 3..2..1 → 화면 낙하 → 0.6초 암전 → CCTV 노이즈 → 붉은 섬광
//    귀신/실루엣 형체는 쓰지 않는다. "OOO 발생" 큰 글씨는 AlertBanner가 담당.
public partial class HorrorDirector : Node
{
    public static HorrorDirector Instance { get; private set; }

    // 전용 연출 이벤트(예: PowerRoomTabooEvent)가 화면을 장악하는 동안 일반 L2/L3 자동 발동을 멈춘다.
    public bool CustomEventActive { get; set; }

    // 3D 중앙제어실 레이어가 "공간 표현"(조명/카메라/CRT/손 반응)을 붙이는 신호.
    // 판정은 여전히 HorrorDirector가 소유 — 3D는 표현만 받는다.
    [Signal] public delegate void Level2StartedEventHandler();
    [Signal] public delegate void Level3StartedEventHandler(bool taboo);
    [Signal] public delegate void ImpactMomentEventHandler();

    private const int MaxLevel3PerShift = 4;
    private const double Level3CooldownMsec = 15000.0;
    private const double Level2CooldownMsec = 3500.0;

    private CanvasLayer _uiLayer;
    private CanvasLayer _overlay;
    private ColorRect _black;
    private ColorRect _red;
    private ColorRect _deathRed;
    private TextureRect _noise;
    private Label _bigLabel;
    private Node3D _faceEntity;
    private OmniLight3D _faceLight;
    private bool _faceJumpscarePlaying;
    private int _deathFeedbackSerial;
    private readonly RandomNumberGenerator _rng = new();
    private bool _postTabooJumpscareScheduled;
    private bool _postTabooJumpscarePlayed;
    private double _postTabooJumpscareAt = -1;
    private GamePhase _lastPhase = GamePhase.Prep;

    private int _level3Count;
    private double _lastLevel3Msec = -1_000_000;
    private double _lastLevel2Msec = -1_000_000;
    private bool _playing;
    private FacilitySimulation _wiredSimulation;

    public override void _Ready()
    {
        Instance = this;
        _uiLayer = GetParent()?.GetNodeOrNull<CanvasLayer>("UILayer");

        _overlay = new CanvasLayer { Layer = 128 };
        AddChild(_overlay);

        _black = FullRect(new Color(0f, 0f, 0f, 0f));
        _overlay.AddChild(_black);

        _noise = new TextureRect
        {
            Texture = BuildNoiseTexture(),
            StretchMode = TextureRect.StretchModeEnum.Tile,
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _noise.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(_noise);

        _red = FullRect(new Color(0.5f, 0f, 0f, 0f));
        _overlay.AddChild(_red);

        // 사망 비명용 페이드는 일반 공포 연출의 붉은 섬광과 분리한다.
        // 따라서 LEVEL 3 트윈이 돌아가도 비명 길이만큼 부드럽게 유지된다.
        _deathRed = FullRect(new Color(0.62f, 0.015f, 0.01f, 0f));
        _deathRed.Name = "DeathScreamRedFade";
        _overlay.AddChild(_deathRed);

        _bigLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bigLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bigLabel.AddThemeFontSizeOverride("font_size", ViewFont.FS(92));
        _bigLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.1f, 0.1f));
        _bigLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _bigLabel.AddThemeConstantOverride("outline_size", 12);
        _overlay.AddChild(_bigLabel);

        BuildEntityFaceJumpscare();

        _wiredSimulation = FacilitySimulation.Instance;
        if (_wiredSimulation != null)
            _wiredSimulation.EmployeeKilled += OnEmployeeKilled;

        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged += OnEntryLogged;
    }

    public override void _ExitTree()
    {
        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntryLogged;
        if (_wiredSimulation != null)
            _wiredSimulation.EmployeeKilled -= OnEmployeeKilled;
        if (Instance == this) Instance = null;
    }

    private static ColorRect FullRect(Color c)
    {
        var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore };
        r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return r;
    }

    private static ImageTexture BuildNoiseTexture()
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

    private void BuildEntityFaceJumpscare()
    {
        var camera = GetParent()?.GetNodeOrNull<Camera3D>("PlayerSeatRig/Camera3D");
        var scene = GD.Load<PackedScene>("res://scenes/props/entity.tscn");
        if (camera == null || scene == null) return;

        _faceEntity = scene.Instantiate<Node3D>();
        _faceEntity.Name = "EntityFaceJumpscare";
        _faceEntity.Visible = false;
        _faceEntity.Position = new Vector3(0f, -0.26f, -0.34f);
        _faceEntity.RotationDegrees = new Vector3(0f, 180f, 0f);
        _faceEntity.Scale = Vector3.One * 1.25f;
        camera.AddChild(_faceEntity);

        _faceLight = new OmniLight3D
        {
            Position = new Vector3(0f, 0.24f, 0.24f),
            LightColor = new Color(1f, 0.18f, 0.14f),
            LightEnergy = 0f,
            OmniRange = 1.8f,
            ShadowEnabled = false,
        };
        _faceEntity.AddChild(_faceLight);
    }

    // 결번자를 카메라 바로 앞에 한 프레임 만에 켠다. 접근 트윈을 쓰지 않아 예고 없이 튀어나오며,
    // 노출 시간도 약 0.1초로 제한해 형체를 오래 감상하는 연출이 되지 않게 한다.
    public async void PlayEntityFaceJumpscare()
    {
        if (_faceJumpscarePlaying || _faceEntity == null) return;
        _faceJumpscarePlaying = true;

        _faceEntity.Position = new Vector3(0f, -0.26f, -0.14f);
        _faceEntity.RotationDegrees = new Vector3(4f, 180f, 9f);
        _faceEntity.Scale = Vector3.One * 1.72f;
        _faceEntity.Visible = true;
        if (_faceLight != null) _faceLight.LightEnergy = 11f;

        Sfx.Instance?.Play("noise", 4f, 0.68f);
        Sfx.Instance?.PlayJumpscareTone(2f);
        AmbientOverlay.Instance?.PulseNoise(0.82f);
        GetParent()?.GetNodeOrNull<SeatedCameraRig>("PlayerSeatRig")?.Shake(4.8f, 0.14f);
        _noise.Modulate = new Color(1f, 1f, 1f, 1f);
        _red.Color = new Color(0.72f, 0f, 0f, 0.56f);

        await Wait(0.105);
        _faceEntity.Visible = false;
        if (_faceLight != null) _faceLight.LightEnergy = 0f;

        var clear = CreateTween().SetParallel(true);
        clear.TweenProperty(_noise, "modulate:a", 0f, 0.07);
        clear.TweenProperty(_red, "color:a", 0f, 0.07);
        await Wait(0.08);
        _faceJumpscarePlaying = false;
    }

    public void SchedulePostTabooJumpscare()
    {
        if (_postTabooJumpscareScheduled || _postTabooJumpscarePlayed
            || GameState.Instance?.CurrentPhase != GamePhase.Live)
            return;

        float shiftLength = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        float elapsed = GameState.Instance?.DayTimeSeconds ?? 0f;
        float remaining = Mathf.Max(0.4f, shiftLength - elapsed);
        float maxDelay = Mathf.Clamp(remaining - 0.2f, 0.35f, 18f);
        float minDelay = Mathf.Min(4f, maxDelay);
        float delay = _rng.RandfRange(minDelay, maxDelay);
        _postTabooJumpscareAt = Time.GetTicksMsec() / 1000.0 + delay;
        _postTabooJumpscareScheduled = true;
    }

    // --- 트리거 -------------------------------------------------------------

    private void OnEntryLogged()
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        var e = EventLog.Instance.GetAllEntries().LastOrDefault();
        if (e == null) return;

        if (CustomEventActive) return;

        switch (e.EventType)
        {
            case LogEventType.TabooViolation:
                // 전용 연출(PowerRoomTabooEvent 등)이 처리하는 금기는 일반 L3를 건너뛴다.
                if (IsHandledByDedicatedEvent(e.RoomId)) break;
                RequestLevel3(taboo: true);
                break;
            case LogEventType.Death:
                RequestLevel3(taboo: false);
                break;
            case LogEventType.PowerOutage:
            case LogEventType.CctvDisconnect:
            case LogEventType.Sabotage:
            case LogEventType.TaskFailed:
                PlayLevel2();
                break;
        }
    }

    private void OnEmployeeKilled(string employeeId)
    {
        if (GameState.Instance?.CurrentPhase == GamePhase.Live)
            PlayDeathFeedback(employeeId);
    }

    private async void PlayDeathFeedback(string employeeId)
    {
        int serial = ++_deathFeedbackSerial;
        Sfx.Instance?.PlayScream(employeeId, -1.5f);

        var fadeIn = CreateTween();
        fadeIn.TweenProperty(_deathRed, "color:a", 0.42f, 0.16)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        await Wait(0.68);
        if (serial != _deathFeedbackSerial || !IsInstanceValid(_deathRed)) return;

        var fadeOut = CreateTween();
        fadeOut.TweenProperty(_deathRed, "color:a", 0f, 0.32)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private static bool IsHandledByDedicatedEvent(string roomId)
    {
        var taboos = NSP.Taboo.TabooRuleSystem.Instance?.GetActiveTaboos();
        if (taboos == null) return false;
        foreach (var t in taboos)
        {
            if (!t.ConditionParams.TryGetValue("defer_consequence", out var defer) || !defer.AsBool()) continue;
            if (t.ConditionParams.TryGetValue("room_id", out var rid) && rid.AsString() == roomId) return true;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        var phase = GameState.Instance?.CurrentPhase ?? GamePhase.Prep;
        if (phase == GamePhase.Live && _lastPhase != GamePhase.Live)
        {
            _postTabooJumpscareScheduled = false;
            _postTabooJumpscarePlayed = false;
            _postTabooJumpscareAt = -1;
        }
        _lastPhase = phase;

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        if (phase == GamePhase.Live && _postTabooJumpscareScheduled && !_postTabooJumpscarePlayed
            && !CustomEventActive && !_playing && nowSeconds >= _postTabooJumpscareAt)
        {
            _postTabooJumpscareScheduled = false;
            _postTabooJumpscarePlayed = true;
            PlayEntityFaceJumpscare();
        }

        if (_playing || CustomEventActive || phase != GamePhase.Live) return;
        if (Time.GetTicksMsec() - _lastLevel2Msec < Level2CooldownMsec) return;

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        // "터지기 직전" 상태(제한시간 임박 / 금기 홀드 진행) → LEVEL 2 사전 징후.
        bool imminent = sim.GetRoomIds().Any(r =>
            RoomStatusText.GetDangerTier(r) is RoomDangerTier.Unstable or RoomDangerTier.Delayed);
        if (imminent) PlayLevel2();
    }

    private async void RequestLevel3(bool taboo)
    {
        if (_playing) return;
        double now = Time.GetTicksMsec();
        if (_level3Count >= MaxLevel3PerShift || now - _lastLevel3Msec < Level3CooldownMsec)
        {
            PlayLevel2();
            return;
        }
        _level3Count++;
        _lastLevel3Msec = now;
        await PlayLevel3(taboo);
    }

    // --- 연출 -------------------------------------------------------------

    private void PlayLevel2()
    {
        if (_playing) return;
        if (Time.GetTicksMsec() - _lastLevel2Msec < Level2CooldownMsec) return;
        _lastLevel2Msec = Time.GetTicksMsec();
        EmitSignal(SignalName.Level2Started);

        AmbientOverlay.Instance?.PulseNoise(0.24f);
        AmbientOverlay.Instance?.Flash(0.4f);

        if (_uiLayer != null)
        {
            var rng = new RandomNumberGenerator();
            var shake = CreateTween();
            for (int i = 0; i < 8; i++)
                shake.TweenProperty(_uiLayer, "offset",
                    new Vector2(rng.RandfRange(-9f, 9f), rng.RandfRange(-7f, 7f)), 0.04);
            shake.TweenProperty(_uiLayer, "offset", Vector2.Zero, 0.05);

            var punch = CreateTween();
            punch.TweenProperty(_uiLayer, "scale", new Vector2(1.02f, 1.02f), 0.05);
            punch.TweenProperty(_uiLayer, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Sine);
        }

        var n = CreateTween();
        n.TweenProperty(_noise, "modulate:a", 0.28f, 0.05);
        n.TweenProperty(_noise, "modulate:a", 0.0f, 0.4);

        var b = CreateTween();
        b.TweenProperty(_black, "color:a", 0.32f, 0.04);
        b.TweenProperty(_black, "color:a", 0.0f, 0.4);
    }

    private async Task PlayLevel3(bool taboo)
    {
        _playing = true;
        EmitSignal(SignalName.Level3Started, taboo);
        try
        {
            if (taboo) Sfx.Instance?.Play("taboo_break", 0f);
            AmbientOverlay.Instance?.PulseNoise(0.35f);

            _bigLabel.Modulate = Colors.White;
            for (int n = 3; n >= 1; n--)
            {
                _bigLabel.Text = $"{n}";
                Sfx.Instance?.Play("tick", -2f);
                AmbientOverlay.Instance?.Flash(0.35f);
                await Wait(0.45);
            }
            _bigLabel.Modulate = new Color(1f, 1f, 1f, 0f);
            _bigLabel.Text = "";

            // 화면이 아래로 내려앉음 (소리 없이 — 점프스케어용 "둥" 제거)
            EmitSignal(SignalName.ImpactMoment);
            if (_uiLayer != null)
            {
                var drop = CreateTween();
                drop.TweenProperty(_uiLayer, "offset", new Vector2(0f, 34f), 0.06)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                drop.TweenProperty(_uiLayer, "offset", new Vector2(0f, -10f), 0.08);
                drop.TweenProperty(_uiLayer, "offset", new Vector2(0f, 16f), 0.07);
                drop.TweenProperty(_uiLayer, "offset", Vector2.Zero, 0.14);
            }
            await Wait(0.1);

            // 암전 0.6초
            var bk = CreateTween();
            bk.TweenProperty(_black, "color:a", 1.0f, 0.04);
            await Wait(0.6);
            var bk2 = CreateTween();
            bk2.TweenProperty(_black, "color:a", 0.0f, 0.18);

            // CCTV 노이즈 폭증
            Sfx.Instance?.Play("noise", -1f);
            _noise.Modulate = new Color(1f, 1f, 1f, 0.62f);

            var flicker = CreateTween();
            for (int i = 0; i < 5; i++)
            {
                flicker.TweenProperty(_noise, "modulate:a", 0.25f, 0.05);
                flicker.TweenProperty(_noise, "modulate:a", 0.6f, 0.05);
            }
            flicker.TweenProperty(_noise, "modulate:a", 0.0f, 0.3);

            // 붉은 섬광
            var redT = CreateTween();
            redT.TweenInterval(0.15);
            redT.TweenProperty(_red, "color:a", 0.45f, 0.05);
            redT.TweenProperty(_red, "color:a", 0.0f, 0.35);

            await Wait(1.35);
        }
        finally
        {
            ResetVisuals();
            _playing = false;
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private void ResetVisuals()
    {
        if (_uiLayer != null && IsInstanceValid(_uiLayer)) { _uiLayer.Offset = Vector2.Zero; _uiLayer.Scale = Vector2.One; }
        if (!IsInstanceValid(this) || _black == null || !IsInstanceValid(_black)) return;
        _black.Color = new Color(0f, 0f, 0f, 0f);
        _red.Color = new Color(0.5f, 0f, 0f, 0f);
        _noise.Modulate = new Color(1f, 1f, 1f, 0f);
        _bigLabel.Modulate = new Color(1f, 1f, 1f, 0f);
        _bigLabel.Text = "";
    }
}
