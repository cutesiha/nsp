using System;
using Godot;

namespace NSP.View;

// 휴게시간 MONITOR 01 — 심문 중에 휴게실(직원 선택) 화면 위에 덮이는 **조사 노트**.
//
//   직원 선택(RestRosterView)  →  [전화기를 든다]  →  조사 노트
//
// 세 면의 역할이 갈라져 있다(docs/NSP_INTERVIEW_REWORK.md §3-6).
//   MON02   직원 얼굴 · 입 모양 · 긴장 발광          (InterviewCCTVView)
//   자막 띠 대화 전부 — 대사 · 질문 · 선택지          (PhoneCallHud)
//   MON01   조사 노트만 — 이 화면                    (여기)
//
// 예전에는 이 화면 한 장에 진술 블록 · 꼬리질문 · 자료 목록 · 슬롯 · 버튼 넷이 다 들어가
// 기울어진 CRT 에서 읽을 양이 아니었다. 대화는 전부 자막 띠로 옮겼고 여기에는 자료만 남는다.
//
// 이 콘솔은 자리(컨테이너)와 표시만 갖는다. 내용물(카드 · 슬롯)은 PhoneCallHud 가
// InterviewSession 을 들고 채운다 — 추리 규칙은 여기 없다.
public partial class RestInterviewConsole : Control
{
    public static RestInterviewConsole Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Bg = new(0.03f, 0.045f, 0.055f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.5f, 0.6f, 0.66f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);

    public event Action EndPressed;

    // PhoneCallHud 가 채우는 자리.
    public HFlowContainer NoteTabs { get; private set; }
    public VBoxContainer EvidenceList { get; private set; }
    public HBoxContainer SlotRow { get; private set; }

    private Font _font;
    private Label _name;
    private Label _goal;
    private Label _detail;
    private Panel _listFrame;
    private Button _endBtn;
    private ScrollContainer _listScroll;
    // 카드가 새로 들어온 순간 목록 테두리가 한 번 밝아진다(§3-6 "말한 것이 자료가 된다").
    private float _flash;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        Position = Vector2.Zero;
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;   // 아래 휴게실 화면으로 클릭이 새지 않게
        Visible = false;
        SetProcess(true);

        var bg = new ColorRect { Color = Bg, MouseFilter = MouseFilterEnum.Ignore, Size = Canvas };
        AddChild(bg);

        var title = Lbl("조사 노트", 22, Cyan);
        title.Position = new Vector2(18, 10);
        AddChild(title);

        _name = Lbl("", 22, Colors.White);
        _name.Position = new Vector2(150, 10);
        _name.Size = new Vector2(300, 30);
        AddChild(_name);

        _endBtn = MonitorUi.Button("통화 종료", new Color(1f, 0.55f, 0.45f), _font, () => EndPressed?.Invoke(), ViewFont.S(15));
        _endBtn.Position = new Vector2(648, 12);
        _endBtn.Size = new Vector2(136, 38);
        AddChild(_endBtn);

        // ── 조사 목표 — 오늘 무엇을 밝혀야 하는가 한 줄 ────────────────
        // 로그에 이미 공개된 사실만 쓴다(범인 이름은 어디에도 없다).
        _goal = Lbl("", 14, Amber);
        _goal.Position = new Vector2(20, 46);
        _goal.Size = new Vector2(764, 22);
        AddChild(_goal);

        BuildNotes();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 조사 노트 — 탭 · 카드 목록 · 상세칸 · 자료 A/B.
    // [이 자료로 질문] [두 자료 비교] 버튼은 없다. 그건 자막 띠의 선택지가 맡는다.
    private void BuildNotes()
    {
        NoteTabs = new HFlowContainer { Position = new Vector2(16, 72), Size = new Vector2(768, 34) };
        NoteTabs.AddThemeConstantOverride("h_separation", 6);
        AddChild(NoteTabs);

        _listFrame = FramePanel(new Vector2(16, 110), new Vector2(768, 306));
        AddChild(_listFrame);
        _listScroll = new ScrollContainer
        {
            Position = new Vector2(24, 116), Size = new Vector2(752, 294),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(_listScroll);
        EvidenceList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EvidenceList.AddThemeConstantOverride("separation", 5);
        _listScroll.AddChild(EvidenceList);

        // ── 상세칸 — 카드 한 줄로는 다 못 싣는 전문을 여기 펼친다 ──────
        var detailFrame = FramePanel(new Vector2(16, 424), new Vector2(768, 78));
        AddChild(detailFrame);
        _detail = Lbl("자료를 고르면 전문이 여기에 표시됩니다.", 13, Dim);
        _detail.Position = new Vector2(28, 432);
        _detail.Size = new Vector2(744, 62);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_detail);

        SlotRow = new HBoxContainer { Position = new Vector2(16, 510), Size = new Vector2(768, 80) };
        SlotRow.AddThemeConstantOverride("separation", 8);
        AddChild(SlotRow);
    }

    // 심문 시작 — 휴게실 화면 위를 덮는다.
    public void Open(string codename, Color accent)
    {
        _name.Text = codename;
        _name.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.25f));
        _listScroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
        SetDetail("");
        Visible = true;
    }

    public void Close() => Visible = false;

    // 오늘 무엇을 밝혀야 하는가. PhoneCallHud 가 시설 로그 화면에 뜬 사건에서 만들어 넘긴다.
    public void SetGoal(string text) => _goal.Text = string.IsNullOrEmpty(text) ? "" : "조사 목표  ·  " + text;

    public void SetDetail(string text)
    {
        bool has = !string.IsNullOrEmpty(text);
        _detail.Text = has ? text : "자료를 고르면 전문이 여기에 표시됩니다.";
        _detail.AddThemeColorOverride("font_color", has ? new Color(0.86f, 0.92f, 0.94f) : Dim);
    }

    // 새 자료가 들어왔다 — 목록 테두리를 한 번 밝히고 짧은 소리를 낸다.
    public void FlashNotes()
    {
        _flash = 1f;
        NSP.Core.Sfx.Instance?.Play("relay_click", -14f);
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0f || _listFrame == null) return;
        _flash = Mathf.Max(0f, _flash - (float)delta * 2.2f);
        var box = _listFrame.GetThemeStylebox("panel") as StyleBoxFlat;
        if (box != null) box.BorderColor = Cyan with { A = 0.3f + 0.7f * _flash };
    }

    private Panel FramePanel(Vector2 pos, Vector2 size)
    {
        var p = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.07f, 0.09f, 0.9f),
            BorderColor = Cyan with { A = 0.3f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        return p;
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }
}
