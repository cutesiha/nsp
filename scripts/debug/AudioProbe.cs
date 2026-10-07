using Godot;
using NSP.Core;
using NSP.Ui;

namespace NSP.Debug;

// 헤드폰으로 직접 들어 보는 도구(지시서 §23 · §24).
//
//   godot --path . res://scenes/debug/AudioProbe.tscn
//
// 창 모드로 띄워 숫자키를 누르면 그 소리가 즉시 난다. 좌우가 실제로 구분되는지,
// 너무 큰지, 전화벨 · 경고음과 헷갈리는지를 귀로 비교하려고 만든 것이다.
// 대형 디버그 시스템이 아니다 — 목록 하나와 키 입력뿐이다.
public partial class AudioProbe : Node
{
    private sealed class Entry
    {
        public string Label = "";
        public string Key = "";
        public float Pan;
        public float Db = -20f;
        public bool Compare;   // 게임 정보음 — 공포음과 헷갈리지 않는지 견주는 용도
    }

    private static readonly Entry[] List =
    {
        new() { Label = "속삭임 — 오른쪽",      Key = "whisper_short",   Pan = 0.82f, Db = -26f },
        new() { Label = "속삭임 — 왼쪽",        Key = "whisper_short",   Pan = -0.82f, Db = -26f },
        new() { Label = "속삭임 — 긴 것(왼쪽)", Key = "whisper_long",    Pan = -0.72f, Db = -27f },
        new() { Label = "속삭임 — 부르는 억양", Key = "whisper_call",    Pan = 0.7f,  Db = -25f },
        new() { Label = "속삭임 — 거꾸로",      Key = "whisper_reverse", Pan = -0.7f, Db = -27f },
        new() { Label = "환풍구 — 기어가는 소리", Key = "duct_crawl",    Pan = -0.4f, Db = -24f },
        new() { Label = "환풍구 — 긁힘",        Key = "duct_scrape",     Pan = 0.4f,  Db = -26f },
        new() { Label = "문 — 노크",            Key = "door_knock_soft", Pan = 0f,    Db = -20f },
        new() { Label = "문 — 긁힘",            Key = "door_scratch",    Pan = 0.1f,  Db = -24f },
        new() { Label = "문 — 손잡이",          Key = "door_handle",     Pan = -0.1f, Db = -24f },
        new() { Label = "가까이 — 들이마심(오른쪽)", Key = "breath_close", Pan = 0.85f, Db = -23f },
        new() { Label = "가까이 — 옷 스침(왼쪽)",   Key = "cloth_rustle", Pan = -0.85f, Db = -25f },
        new() { Label = "관리자 — 날숨",        Key = "admin_exhale",    Pan = 0f,    Db = -22f },
        new() { Label = "관리자 — 숨 멎었다 들이마심", Key = "admin_gasp", Pan = 0f,  Db = -17f },
        new() { Label = "휴게실 — 웅성거림",    Key = "breakroom_murmur", Pan = 0f,   Db = -27f },
        new() { Label = "휴게실 — 의자 끌기",   Key = "chair_pull",      Pan = 0f,    Db = -22f },
        new() { Label = "휴게실 — 컵 내려놓기", Key = "cup_set",         Pan = 0f,    Db = -25f },
        // 아래 둘은 공포음이 아니다. 헷갈리지 않는지 바로 견주려고 같은 목록에 둔다(§13).
        new() { Label = "[게임음] 전화벨",      Key = "call_ring",       Pan = 0f,    Db = -8f, Compare = true },
        new() { Label = "[게임음] 경고",        Key = "alert_beep3",     Pan = 0f,    Db = -8f, Compare = true },
    };

    private Label _log;

    public override void _Ready()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.03f, 0.04f, 0.05f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        box.Position = new Vector2(40f, 28f);
        box.AddThemeConstantOverride("separation", 4);
        layer.AddChild(box);

        Add(box, "─── 공포 · 스토리 사운드 확인 (헤드폰 권장) ───", 26, new Color(0.55f, 0.95f, 1f));
        Add(box, "숫자 · 알파벳 키를 누르면 그 소리가 납니다.", 18, new Color(0.6f, 0.7f, 0.74f));
        Add(box, "", 14, Colors.White);

        for (int i = 0; i < List.Length; i++)
        {
            var e = List[i];
            string side = Mathf.Abs(e.Pan) < 0.1f ? "중앙" : e.Pan > 0 ? $"오른쪽 {e.Pan:0.00}" : $"왼쪽 {e.Pan:0.00}";
            var color = e.Compare ? new Color(1f, 0.8f, 0.36f) : new Color(0.86f, 0.92f, 0.94f);
            Add(box, $"  [{KeyLabel(i)}]  {e.Label,-28}  {side,-14}  {e.Db:0}dB", 19, color);
        }

        Add(box, "", 14, Colors.White);
        Add(box, "  [,]  스토리 전환 — 눈 감기 · 날숨 · 생활음 · 눈 뜨기", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "  [.]  BGM 더킹 한 번 (-4dB)", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "  [/]  시설 상시음 침묵 1.2초 (§18)", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "", 14, Colors.White);
        _log = Add(box, "준비됨.", 20, new Color(1f, 0.9f, 0.5f));
    }

    private static string KeyLabel(int i) => i < 9 ? (i + 1).ToString()
        : i == 9 ? "0" : ((char)('A' + i - 10)).ToString();

    private static Label Add(Node parent, string text, int size, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        parent.AddChild(l);
        return l;
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } k) return;

        int idx = IndexOf(k.Keycode);
        if (idx >= 0 && idx < List.Length)
        {
            var e = List[idx];
            if (e.Compare) Sfx.Instance?.Play(e.Key, e.Db);
            else Sfx.Instance?.PlayHorror(e.Key, e.Db, e.Pan);
            _log.Text = $"▶ {e.Label}  ({e.Key} · pan {e.Pan:0.00} · {e.Db:0}dB)";
            return;
        }

        switch (k.Keycode)
        {
            case Key.Comma:
                _log.Text = "▶ 스토리 전환 (모니터2 가 없는 씬이라 소리와 암전만)";
                _ = NSP.View.StoryTransition.Enter(this);
                break;
            case Key.Period:
                Sfx.Instance?.DuckMusic(-4f, 0.7f);
                _log.Text = "▶ BGM 더킹 -4dB";
                break;
            case Key.Slash:
                NSP.View.ControlRoomAtmosphere.Instance?.Hush(1.2f);
                _log.Text = NSP.View.ControlRoomAtmosphere.Instance == null
                    ? "시설 환경음이 없는 씬입니다(중앙제어실에서만 들립니다)."
                    : "▶ 상시음 침묵 1.2초";
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
    }

    private static int IndexOf(Key code)
    {
        if (code >= Key.Key1 && code <= Key.Key9) return (int)(code - Key.Key1);
        if (code == Key.Key0) return 9;
        if (code >= Key.A && code <= Key.Z) return 10 + (int)(code - Key.A);
        return -1;
    }
}
