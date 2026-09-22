using System;
using Godot;

namespace NSP.View;

// 휴게시간 MONITOR 01 — 심문 중에 휴게실(직원 선택) 화면 위에 덮이는 심문 콘솔.
//
//   직원 선택(RestRosterView)  →  [전화기를 든다]  →  ① 심문   ⇄   ② 조사 노트
//
//   ① 심문      : 직원의 근무 진술 블록 · 고른 진술의 꼬리질문 · 자료 질문 목록 · [조사 노트 ▶]
//   ② 조사 노트 : [사건별][현재 직원][★ 중요] · 카드 목록 · 자료 A / B · 질문 · 비교 · [← 심문으로]
//
// 이 콘솔은 자리(컨테이너)와 화면 전환만 갖는다. 내용물(진술 · 질문 · 카드 · 버튼)은
// PhoneCallHud 가 InterviewSession 을 들고 채운다 — 추리 규칙은 여기 없다.
// MONITOR 02(InterviewCCTVView, 직원 스탠딩 · 입 모양)는 이 전환과 무관하게 계속 보인다.
public partial class RestInterviewConsole : Control
{
    public static RestInterviewConsole Instance { get; private set; }

    public enum Page { Interview, Notes }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Bg = new(0.03f, 0.045f, 0.055f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.5f, 0.6f, 0.66f);

    public event Action EndPressed;

    // PhoneCallHud 가 채우는 자리.
    public Label StatementsHead { get; private set; }
    public VBoxContainer Statements { get; private set; }
    public VBoxContainer Choices { get; private set; }
    public VBoxContainer Tail { get; private set; }
    public HFlowContainer NoteTabs { get; private set; }
    public VBoxContainer EvidenceList { get; private set; }
    public HBoxContainer SlotRow { get; private set; }
    public HBoxContainer ActionRow { get; private set; }

    public Page Current { get; private set; } = Page.Interview;

    private Font _font;
    private Label _name;
    private Label _pageTitle;
    private Control _interviewPage;
    private Control _notesPage;
    private Button _endBtn;
    private ScrollContainer _interviewScroll;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        Position = Vector2.Zero;
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;   // 아래 휴게실 화면으로 클릭이 새지 않게
        Visible = false;

        var bg = new ColorRect { Color = Bg, MouseFilter = MouseFilterEnum.Ignore, Size = Canvas };
        AddChild(bg);

        var title = Lbl("INTERVIEW", 22, Cyan);
        title.Position = new Vector2(18, 10);
        AddChild(title);

        _name = Lbl("", 22, Colors.White);
        _name.Position = new Vector2(196, 10);
        _name.Size = new Vector2(300, 30);
        AddChild(_name);

        _pageTitle = Lbl("", 13, Dim);
        _pageTitle.Position = new Vector2(20, 38);
        _pageTitle.Size = new Vector2(600, 20);
        AddChild(_pageTitle);

        _endBtn = MonitorUi.Button("통화 종료", new Color(1f, 0.55f, 0.45f), _font, () => EndPressed?.Invoke(), ViewFont.S(15));
        _endBtn.Position = new Vector2(648, 12);
        _endBtn.Size = new Vector2(136, 38);
        AddChild(_endBtn);

        BuildInterviewPage();
        BuildNotesPage();
        ShowPage(Page.Interview);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ① 심문 — 진술 블록 · 꼬리질문 · 자료 질문 목록(스크롤) + 아래 [조사 노트 ▶].
    private void BuildInterviewPage()
    {
        _interviewPage = new Control { Position = new Vector2(0, 60), Size = new Vector2(800, 540), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_interviewPage);

        var frame = FramePanel(new Vector2(16, 4), new Vector2(768, 462));
        _interviewPage.AddChild(frame);

        _interviewScroll = new ScrollContainer
        {
            Position = new Vector2(26, 12), Size = new Vector2(748, 446),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _interviewPage.AddChild(_interviewScroll);

        var inner = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 7);
        _interviewScroll.AddChild(inner);

        StatementsHead = Lbl("근무 진술", 14, Cyan with { A = 0.8f });
        inner.AddChild(StatementsHead);
        Statements = new VBoxContainer();
        Statements.AddThemeConstantOverride("separation", 6);
        inner.AddChild(Statements);

        var gap = new Control { CustomMinimumSize = new Vector2(0, 6) };
        inner.AddChild(gap);

        Choices = new VBoxContainer();
        Choices.AddThemeConstantOverride("separation", 6);
        inner.AddChild(Choices);
        Tail = new VBoxContainer();
        inner.AddChild(Tail);

        var toNotes = MonitorUi.Button("조사 노트  ▶", Cyan, _font, () => ShowPage(Page.Notes), ViewFont.S(16));
        toNotes.Position = new Vector2(560, 476);
        toNotes.Size = new Vector2(224, 44);
        _interviewPage.AddChild(toNotes);

        var hint = Lbl("진술을 누르면 그 내용으로 캐물을 수 있습니다.", 12, Dim);
        hint.Position = new Vector2(20, 488);
        hint.Size = new Vector2(530, 20);
        _interviewPage.AddChild(hint);
    }

    // ② 조사 노트 — 탭 · 카드 목록 · 자료 A/B · 질문/비교 · [← 심문으로].
    private void BuildNotesPage()
    {
        _notesPage = new Control { Position = new Vector2(0, 60), Size = new Vector2(800, 540), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_notesPage);

        NoteTabs = new HFlowContainer { Position = new Vector2(16, 0), Size = new Vector2(768, 34) };
        NoteTabs.AddThemeConstantOverride("h_separation", 6);
        _notesPage.AddChild(NoteTabs);

        var frame = FramePanel(new Vector2(16, 38), new Vector2(768, 300));
        _notesPage.AddChild(frame);
        var scroll = new ScrollContainer
        {
            Position = new Vector2(24, 44), Size = new Vector2(752, 288),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _notesPage.AddChild(scroll);
        EvidenceList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EvidenceList.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(EvidenceList);

        SlotRow = new HBoxContainer { Position = new Vector2(16, 346), Size = new Vector2(768, 92) };
        SlotRow.AddThemeConstantOverride("separation", 8);
        _notesPage.AddChild(SlotRow);

        ActionRow = new HBoxContainer { Position = new Vector2(16, 446), Size = new Vector2(540, 76) };
        ActionRow.AddThemeConstantOverride("separation", 8);
        _notesPage.AddChild(ActionRow);

        var back = MonitorUi.Button("←  심문으로", Cyan, _font, () => ShowPage(Page.Interview), ViewFont.S(16));
        back.Position = new Vector2(566, 452);
        back.Size = new Vector2(218, 64);
        _notesPage.AddChild(back);
    }

    public void ShowPage(Page page)
    {
        Current = page;
        if (_interviewPage == null) return;
        _interviewPage.Visible = page == Page.Interview;
        _notesPage.Visible = page == Page.Notes;
        _pageTitle.Text = page == Page.Interview
            ? "근무 진술 · 꼬리질문 · 자료 질문"
            : "조사 노트 — 자료를 한 장 고르면 질문, 두 장 고르면 비교";
    }

    // 심문 시작 — 휴게실 화면 위를 덮는다. 처음에는 늘 ① 심문 화면.
    public void Open(string codename, Color accent)
    {
        _name.Text = codename;
        _name.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.25f));
        ShowPage(Page.Interview);
        _interviewScroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
        Visible = true;
    }

    public void Close() => Visible = false;

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
