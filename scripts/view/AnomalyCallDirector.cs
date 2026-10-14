using System.Threading.Tasks;
using Godot;
using NSP.Core;

namespace NSP.View;

// 받으면 안 되는 전화(지시서 §6-2 ② ③).
//
// 직원 전화와 **같은 전화기**를 쓴다 — 벨도, 수화기를 드는 손 동작도, 끊는 동작도
// 그대로다. 다른 것은 받은 뒤다. 대사 엔진(PhoneCallHud)을 타지 않으므로
//
//   · 통화 기록(DialogueHistory)에 남지 않는다
//   · 증거 카드 · 단서판에 올라가지 않는다
//   · 도전과제 집계(「여섯 명의 이야기」 · 「무심한 관리자」)에 들어가지 않는다
//   · 직원 상태 · 코어 · 사고 어디에도 손대지 않는다
//
// 즉 **추리에 아무 영향이 없다.** 무서울 뿐이다.
public partial class AnomalyCallDirector : Node
{
    public static AnomalyCallDirector Instance { get; private set; }

    public const string KindGhost = "ghost";     // ② 정체불명의 귀신 전화
    public const string KindSilent = "silent";   // ③ 무응답 전화 / 수화기 속 속삭임

    // 검사용 — 마지막으로 돌린 연출과 지금 돌고 있는지.
    public string LastKind { get; private set; } = "";
    public bool Running { get; private set; }

    private CanvasLayer _layer;
    private Panel _panel;
    private Label _who, _line;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Build();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void Build()
    {
        _layer = new CanvasLayer { Layer = 128, Visible = false };
        AddChild(_layer);

        _panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -230f, OffsetRight = 230f, OffsetTop = -156f, OffsetBottom = -56f,
        };
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.040f, 0.042f, 0.93f),
            BorderColor = new Color(0.32f, 0.34f, 0.34f),
            ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        box.SetBorderWidthAll(1);
        _panel.AddThemeStyleboxOverride("panel", box);
        _layer.AddChild(_panel);

        var col = new VBoxContainer();
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(col);

        _who = Lbl("발신자 미상", 15, new Color(0.62f, 0.66f, 0.66f));
        col.AddChild(_who);
        _line = Lbl("", 18, new Color(0.80f, 0.82f, 0.80f));
        _line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _line.VerticalAlignment = VerticalAlignment.Center;
        _line.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(_line);
    }

    private static Label Lbl(string text, int size, Color c)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontOverride("font", ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    // ── 연출 ────────────────────────────────────────────────────────

    public void Begin(string kind)
    {
        if (Running) return;
        LastKind = kind;
        _ = Run(kind);
    }

    private async Task Run(string kind)
    {
        Running = true;
        _layer.Visible = true;
        _who.Text = kind == KindSilent ? "발신자 미상 — 무응답" : "발신자 미상";
        _line.Text = "";

        if (kind == KindSilent) await RunSilent();
        else await RunGhost();

        _layer.Visible = false;
        Running = false;
        // 수화기를 내려놓는 동작까지 평소 전화와 똑같이 끝난다. 여기서 끝내지 않으면
        // 전화기가 통화 중인 채로 남아 진짜 전화가 들어오지 못한다.
        Phone3D.Instance?.EndAnomalyCall();
    }

    // ② 정체불명의 귀신 전화 — 짧은 단어 몇 마디와 가까운 숨소리.
    // 직원 이름도, 방 이름도, 결번 이야기도 나오지 않는다. 쓸 수 있는 정보가 없어야
    // 플레이어가 이 전화를 증거로 삼지 않는다.
    private async Task RunGhost()
    {
        Sfx.Instance?.Play("man_breath", -10f, 0.86f);
        await Say("…………", 1.1);
        await Say("거기…… 아직 있어요?", 1.5);
        Sfx.Instance?.PlayHorror("whisper_short", -16f, -0.55f, 0.88f);
        await Say("문은…… 닫았나요.", 1.6);
        Sfx.Instance?.Play("man_breath", -7f, 0.78f);
        await Say("…그럼 됐어요.", 1.3);
        await Say("", 0.5);
        Sfx.Instance?.Play("phone_hangup", -4f);
        await Wait(0.4);
    }

    // ③ 무응답 전화 — 침묵, 먼 금속 긁힘, 한마디 속삭임, 끊김.
    private async Task RunSilent()
    {
        await Say("…", 2.3);
        Sfx.Instance?.PlayHorror("duct_scrape", -19f, 0.6f, 0.84f);
        await Say("…………", 1.7);
        Sfx.Instance?.PlayHorror("whisper_short", -14f, -0.7f, 0.92f);
        await Say("― 뒤.", 1.2);
        Sfx.Instance?.Play("phone_hangup", -3f);
        await Wait(0.5);
    }

    private async Task Say(string text, double seconds)
    {
        if (_line != null) _line.Text = text;
        await Wait(seconds);
    }

    private async Task Wait(double seconds)
        => await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
}
