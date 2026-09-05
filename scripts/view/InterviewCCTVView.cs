using Godot;
using NSP.Core;
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

        var portraitBox = new Control
        {
            // 스탠딩 원화의 발끝이 화면 아래에 붙도록 프레임 전체 높이를 쓴다.
            Position = new Vector2(Frame.Position.X + Frame.Size.X / 2f - 190f, Frame.Position.Y),
            Size = new Vector2(380f, Frame.Size.Y),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(portraitBox);

        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        portraitBox.AddChild(_portrait);

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
        _clock.Position = new Vector2(Frame.End.X - 160f, 25f);
        _clock.Size = new Vector2(156, 24);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);
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
        if (empId == _lastEmployee) return;
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
        _portrait.Texture = def.StandingImage ?? def.FacePortrait;
        _namePlate.Visible = true;
        _nameLabel.Text = def.Codename;
        _statusLabel.Text = !st.Alive ? "응답 없음 · 기록 종료"
            : st.Isolated ? "격리됨 · 인터뷰 가능"
            : "휴게 중 · 전화 연결 대기";
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

    private static string FacilityClock(float t)
    {
        float shiftLength = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int totalMin = 22 * 60 + Mathf.FloorToInt(t * (360f / Mathf.Max(1f, shiftLength)));
        int h = (totalMin / 60) % 24;
        int m = totalMin % 60;
        return $"{h:00}:{m:00}";
    }
}
