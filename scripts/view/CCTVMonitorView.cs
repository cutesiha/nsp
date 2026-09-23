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
    // 비명 자막과 "얼마나 더 봐야 사라지는가" 게이지. 둘 다 **지금 보고 있는 방**에만 뜬다 —
    // 다른 방에 있는 괴물은 이 화면 어디에도 나타나지 않는다.
    private Label _screamLabel;
    private float _screamLeft;
    private ColorRect _dispelBack, _dispelFill;
    private Label _dispelLabel;
    private bool _ghostWired;
    private ImageTexture[] _noiseFrames;
    private float _noiseSwap;
    private int _noiseIdx;
    private float _recBlink;
    private float _glitch;

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

        _recLabel = Lbl("● REC", 18, new Color(0.95f, 0.25f, 0.2f));
        _recLabel.Position = new Vector2(44, 24);
        AddChild(_recLabel);

        _camLabel = Lbl("", 15, new Color(0.75f, 0.85f, 0.8f));
        _camLabel.Position = new Vector2(44, 512);
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

    // 괴물이 지르는 비명과 소멸 게이지.
    private void BuildGhostOverlay()
    {
        _screamLabel = Lbl("크와아아악!!!!!!!!!", 54, new Color(1f, 0.20f, 0.18f));
        _screamLabel.Position = new Vector2(Frame.Position.X, Frame.Position.Y + 120f);
        _screamLabel.Size = new Vector2(Frame.Size.X, 80f);
        _screamLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _screamLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _screamLabel.AddThemeConstantOverride("outline_size", 10);
        _screamLabel.Visible = false;
        AddChild(_screamLabel);

        // 게이지는 화면 아래쪽 — 방 안을 가리지 않는 자리에 둔다.
        float gy = Frame.Position.Y + Frame.Size.Y - 44f;
        _dispelBack = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            Position = new Vector2(Frame.Position.X + 150f, gy),
            Size = new Vector2(Frame.Size.X - 300f, 14f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_dispelBack);
        _dispelFill = new ColorRect
        {
            Color = new Color(0.62f, 0.95f, 0.78f, 0.92f),
            Position = _dispelBack.Position + new Vector2(2f, 2f),
            Size = new Vector2(0f, 10f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_dispelFill);
        _dispelLabel = Lbl("관측 유지 — 시선을 떼지 마십시오", 15, new Color(0.72f, 0.96f, 0.84f));
        _dispelLabel.Position = new Vector2(Frame.Position.X, gy - 26f);
        _dispelLabel.Size = new Vector2(Frame.Size.X, 22f);
        _dispelLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _dispelLabel.Visible = false;
        AddChild(_dispelLabel);
    }

    // 지금 보고 있는 방에 괴물이 있는 동안에만 게이지가 차오른다.
    private void TickGhostOverlay(float d, FacilitySimulation sim, string roomId, bool feed)
    {
        var ghost = sim?.Ghost;
        if (ghost != null && !_ghostWired)
        {
            ghost.Screamed += OnGhostScream;
            _ghostWired = true;
        }

        if (_screamLeft > 0f)
        {
            _screamLeft -= d;
            // 글자가 떨린다 — 읽을 수 없을 정도는 아니다.
            _screamLabel.Position = new Vector2(
                Frame.Position.X + _rng.RandfRange(-5f, 5f),
                Frame.Position.Y + 120f + _rng.RandfRange(-4f, 4f));
            if (_screamLeft <= 0f) _screamLabel.Visible = false;
        }

        bool show = feed && ghost is { Active: true } && ghost.ActiveRoomId == roomId;
        if (_dispelBack.Visible != show)
        {
            _dispelBack.Visible = _dispelFill.Visible = _dispelLabel.Visible = show;
            if (!show && _screamLabel != null) { _screamLabel.Visible = false; _screamLeft = 0f; }
        }
        if (!show) return;

        float w = (_dispelBack.Size.X - 4f) * ghost.DispelRatio;
        _dispelFill.Size = new Vector2(w, 10f);
        _dispelLabel.Text = ghost.DispelRatio >= 0.999f
            ? "관측 완료"
            : $"관측 유지 — 시선을 떼지 마십시오  ({ghost.DispelRatio * 100f:0}%)";
    }

    private void OnGhostScream(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || sim.SurveillanceTargetRoomId != roomId) return;
        _screamLabel.Visible = true;
        _screamLeft = 1.15f;
        Shake(6f, 0.5f);
        FlashGlitch(0.9f);
        Sfx.Instance?.Play("alert_beep3", 2f);
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

    public override void _Process(double delta)
    {
        FeedVisible = false;
        float d = (float)delta;
        var sim = FacilitySimulation.Instance;
        string roomId = sim?.SurveillanceTargetRoomId ?? "";
        bool feed = UpdateFeed(d, sim, roomId);
        TickGhostOverlay(d, sim, roomId, feed);
        // 엿들은 대화 — 정상 피드가 나오고 근무 중일 때만 들린다.
        _overheard?.Tick(d, roomId, feed && GameState.Instance?.CurrentPhase == GamePhase.Live);
    }

    // 화면 상태를 갱신하고, 방 영상이 정상적으로 나오고 있으면 true.
    private bool UpdateFeed(float d, FacilitySimulation sim, string roomId)
    {

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

    private void RebuildEmployees(FacilitySimulation sim, RoomState state)
    {
        // 직원은 이제 3D 월드(FacilityCctvWorld)에서 실제 플레이스홀더로 보인다 — 여기선 방 상태 텍스트만.
        if (state == null) return;

        var block = RoomStatusText.BuildRoomStatusBlock(state.RoomId);
        // 상태 블록은 프레임 하단 중앙에.
        var existing = _employeeLayer.GetNodeOrNull<Label>("Status");
        var status = existing ?? Lbl("", 15, new Color(0.85f, 0.9f, 0.85f));
        status.Name = "Status";
        status.Text = string.IsNullOrEmpty(block) ? "정상 근무 중" : block.Replace("\n", "   ");
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
