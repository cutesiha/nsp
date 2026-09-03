using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 오른쪽 CRT — 휴게시간. 왼쪽에서 고른 직원을 휴게실 CCTV로 비춘다(2D 일러스트 + CCTV 프레임).
// 실제 대화는 여기서 텍스트로 굴리지 않는다 — 전화기를 들면 기존 PhoneCallHud 가 뜬다.
// 이 화면은 "누구를 보고 있는가"만 보여준다.
public partial class InterviewCCTVView : Control
{
    private static readonly Rect2 Frame = new(32, 60, 736, 452);

    private Font _font;
    private TextureRect _portrait;
    private Label _stateLabel;
    private Label _recLabel;
    private Label _clock;
    private TextureRect _noise;
    private ImageTexture[] _noiseFrames;
    private float _noiseSwap;
    private int _noiseIdx;
    private float _recBlink;
    private string _lastEmployee = "\0";

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var roomBg = new ColorRect { Position = Frame.Position, Size = Frame.Size, Color = new Color(0.10f, 0.10f, 0.12f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(roomBg);

        var portraitBox = new Control
        {
            // 스탠딩 원화의 발끝이 CCTV 화면 아래에 붙도록 프레임 전체 높이를 쓴다.
            // 세로 비율은 KeepAspectCentered가 유지하고, 가로 여백만 남긴다.
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

        _stateLabel = Lbl("왼쪽에서 직원을 선택하세요", 20, new Color(0.8f, 0.85f, 0.8f));
        _stateLabel.Position = new Vector2(Frame.Position.X, Frame.Position.Y + Frame.Size.Y / 2f - 16f);
        _stateLabel.Size = new Vector2(Frame.Size.X, 32f);
        _stateLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_stateLabel);

        _noiseFrames = new ImageTexture[6];
        for (int i = 0; i < 6; i++) _noiseFrames[i] = BuildNoise();
        _noise = new TextureRect
        {
            Texture = _noiseFrames[0], StretchMode = TextureRect.StretchModeEnum.Tile,
            Position = Frame.Position, Size = Frame.Size, MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.05f),
        };
        AddChild(_noise);

        _recLabel = Lbl("● REC", 18, new Color(0.95f, 0.25f, 0.2f));
        _recLabel.Position = new Vector2(44, 24);
        AddChild(_recLabel);

        _clock = Lbl("--:--", 18, new Color(0.75f, 0.85f, 0.8f));
        _clock.Position = new Vector2(600, 24);
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
        if (_noiseSwap > 0.06f)
        {
            _noiseSwap = 0f;
            _noiseIdx = (_noiseIdx + 1) % _noiseFrames.Length;
            _noise.Texture = _noiseFrames[_noiseIdx];
        }

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
            _stateLabel.Text = "왼쪽에서 직원을 선택하세요";
            return;
        }

        _stateLabel.Visible = false;
        _portrait.Texture = def.StandingImage ?? def.FacePortrait;
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
