using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 오른쪽 CRT 전용. CCTV만. 왼쪽에서 선택한 방(SurveillanceTargetRoomId)을 보여준다.
// 배경 / 직원 / 이펙트를 각각 다른 Layer로 분리한다 — 나중에 Blender 렌더 이미지를
// res://assets/cctv/{name}.png 로 넣으면 배경 레이어만 교체된다.
public partial class CCTVMonitorView : Control
{
    public static CCTVMonitorView Instance { get; private set; }

    private static readonly Rect2 Frame = new(32, 60, 736, 452);

    private static readonly Dictionary<string, string> TexName = new()
    {
        ["power_room"] = "power", ["vent_room"] = "vent", ["maintenance_room"] = "maintenance",
        ["medical_room"] = "medical", ["guard_room"] = "guard", ["storage_room"] = "storage",
        ["core_room"] = "core", ["isolation_room"] = "isolation", ["central_office"] = "central",
    };

    private Font _font;
    private TextureRect _bgTex;
    private CctvPlaceholder _bgPlaceholder;
    private Control _employeeLayer;
    private ColorRect _tint;
    private Label _stateLabel;
    private Label _recLabel;
    private Label _camLabel;
    private Label _clock;
    private TextureRect _noise;
    private ImageTexture[] _noiseFrames;
    private float _noiseSwap;
    private int _noiseIdx;
    private float _recBlink;
    private float _glitch;
    private string _lastRoom = "?";

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
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
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

        _clock = Lbl("--:--", 18, new Color(0.75f, 0.85f, 0.8f));
        _clock.Position = new Vector2(600, 24);
        _clock.Size = new Vector2(156, 24);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
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
        float d = (float)delta;
        var sim = FacilitySimulation.Instance;
        string roomId = sim?.SurveillanceTargetRoomId ?? "";

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

        if (string.IsNullOrEmpty(roomId) || sim == null)
        {
            ShowState("MONITOR 01에서 방을 선택하세요", darken: 1f);
            _camLabel.Text = "";
            _noise.Modulate = new Color(1, 1, 1, 0.05f + _glitch * 0.5f);
            return;
        }

        var def = sim.GetRoomDef(roomId);
        var state = sim.GetRoomState(roomId);
        _camLabel.Text = $"CAM · {def?.DisplayName ?? roomId}";

        bool powered = GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch);
        bool disconnected = state?.CctvDisconnected == true;

        if (disconnected)
        {
            ShowState("── SIGNAL LOST ──", darken: 0.92f);
            _noise.Modulate = new Color(1, 1, 1, 0.5f + _glitch * 0.4f);
            return;
        }
        if (!powered)
        {
            ShowState("NO SIGNAL\nPOWER OFFLINE", darken: 0.9f);
            _noise.Modulate = new Color(1, 1, 1, 0.16f);
            return;
        }

        // 정상 피드
        _stateLabel.Visible = false;
        UpdateBackground(roomId);
        _bgPlaceholder.Visible = !_bgTex.Visible;

        bool red = state?.RedAlertLighting == true;
        _tint.Color = red ? new Color(0.5f, 0.03f, 0.03f, 0.35f) : new Color(0, 0, 0, 0.12f);
        _noise.Modulate = new Color(1, 1, 1, (red ? 0.14f : 0.06f) + _glitch * 0.55f);

        RebuildEmployees(sim, state);
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

    private void UpdateBackground(string roomId)
    {
        if (roomId == _lastRoom) return;
        _lastRoom = roomId;

        string path = $"res://assets/cctv/{TexName.GetValueOrDefault(roomId, roomId)}.png";
        if (ResourceLoader.Exists(path))
        {
            _bgTex.Texture = GD.Load<Texture2D>(path);
            _bgTex.Visible = true;
        }
        else
        {
            _bgTex.Visible = false;
            _bgPlaceholder.RoomId = roomId;
            _bgPlaceholder.QueueRedraw();
        }
    }

    private void RebuildEmployees(FacilitySimulation sim, RoomState state)
    {
        foreach (var c in _employeeLayer.GetChildren()) c.QueueFree();
        if (state == null) return;

        int i = 0;
        foreach (var id in state.OccupantEmployeeIds)
        {
            var def = sim.GetEmployeeDef(id);
            var est = sim.GetEmployeeState(id);
            if (def == null || est == null || !est.Alive) continue;

            float x = 90 + (i % 3) * 200;
            float y = 150 + (i / 3) * 150;

            var dot = new ColorRect { Position = new Vector2(x, y), Size = new Vector2(30, 30), Color = def.IconColor, MouseFilter = MouseFilterEnum.Ignore };
            _employeeLayer.AddChild(dot);
            var name = Lbl(def.Codename, 15, new Color(0.95f, 0.95f, 0.85f));
            name.Position = new Vector2(x - 20, y + 32);
            name.Size = new Vector2(70, 20);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            _employeeLayer.AddChild(name);
            i++;
        }

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

    private static string FacilityClock(float t)
    {
        int totalMin = 22 * 60 + Mathf.FloorToInt(t * (480f / 300f));
        int h = (totalMin / 60) % 24;
        int m = totalMin % 60;
        return $"{h:00}:{m:00}";
    }

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
