using Godot;
using NSP.Core;

namespace NSP.View;

// MONITOR 02 안에서 돌아가는 DAY1~5 메인 스토리 화면.
//
// 플레이어가 느껴야 하는 것은 하나다 —
// **"중앙제어실에 앉아 모니터2 를 확대해 휴게실 CCTV 를 보고 있다."**
// 그래서 스토리는 제어실 전체를 덮는 별도 화면이 아니라, 이 모니터 영상 안에서 합성된다.
//
//   배경 : FacilityCctvWorld 의 휴게실 3D (InterviewCCTVView 와 같은 SubViewport 텍스처)
//   인물 : 그 위에 얹히는 2D 스탠딩 — StoryCutinHud 의 내용물을 여기로 옮겨 붙인다
//   자막 : 이름 · 대사도 이 영역 안에서만 그린다(모니터 밖으로 나가지 않는다)
//
// 스탠딩 · 자막을 그리는 일 자체는 전부 기존 StoryCutinHud 가 한다. 이 화면은 그것이
// 설 **자리**와 CCTV 배경만 제공한다(StoryCutinHud.SetRenderTarget).
public partial class StoryMonitorView : Control
{
    public static StoryMonitorView Instance { get; private set; }

    // 모니터 테두리 안쪽 영상 영역. 인터뷰 화면(InterviewCCTVView)과 같은 자리를 쓴다.
    private static readonly Rect2 Frame = new(28, 56, 744, 460);

    private Font _font;
    private TextureRect _roomFeed;
    private ColorRect _roomFallback;
    private Label _title, _rec, _clock;
    private float _recBlink;
    private bool _feedBound;

    // 스탠딩 · 대사창이 들어와 앉는 자리. StoryCutinHud 가 이 Control 안으로 옮겨 온다.
    public Control StoryLayer { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

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

        _title = Lbl("BREAK ROOM", 17, new Color(0.72f, 0.86f, 0.9f));
        _title.Position = new Vector2(Frame.Position.X + 4f, 24f);
        AddChild(_title);

        _rec = Lbl("● REC", 16, new Color(0.95f, 0.35f, 0.28f));
        _rec.Position = new Vector2(Frame.Position.X + 152f, 25f);
        AddChild(_rec);

        _clock = Lbl("--:--", 16, new Color(0.75f, 0.85f, 0.8f));
        _clock.Position = new Vector2(Frame.End.X - 224f, 25f);
        _clock.Size = new Vector2(220, 24);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);

        // 스탠딩 · 대사창이 들어올 자리 — 영상 영역에 딱 맞춘다.
        // 여기 들어온 것은 이 사각형 밖으로 나가지 않는다(ClipContents).
        StoryLayer = new Control
        {
            Position = Frame.Position, Size = Frame.Size,
            ClipContents = true, MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(StoryLayer);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        BindRoomFeed();
        _clock.Text = FacilityClock(GameState.Instance?.DayTimeSeconds ?? 0f);
        _recBlink += (float)delta;
        _rec.Visible = (_recBlink % 1.4f) < 1.0f;
    }

    // 3D 휴게실 월드(FacilityCctvWorld)의 SubViewport 텍스처를 배경으로 한 번만 연결한다.
    // 인터뷰 화면과 같은 텍스처다 — 카메라와 보여 줄 방은 FacilityCctvWorld 가 정한다.
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

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 3);
        return l;
    }

    // 다른 CRT 화면과 같은 시각 표기.
    private static string FacilityClock(float daySeconds)
    {
        var cfg = Config.Instance?.Data;
        float total = cfg?.DayLengthSeconds ?? 180f;
        float t = total <= 0f ? 0f : Mathf.Clamp(daySeconds / total, 0f, 1f);
        int minutes = Mathf.RoundToInt(t * 8f * 60f);
        int hh = (22 + minutes / 60) % 24;
        int mm = minutes % 60;
        return $"{hh:00}:{mm:00}";
    }
}
