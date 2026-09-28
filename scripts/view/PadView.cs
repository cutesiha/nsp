using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 관리자 패드 화면 — 상단 탭 [지침] [단서] [직원].
//
//   지침  data/pad/manual.tres(PadManualDef)를 섹션 목록으로 읽는다.
//   단서  ClueBoard 의 단서를 카드(2행 × 3열)로. 시간순 / 직원별, 직원 필터, 페이지.
//         카드를 누르면 오른쪽 절반에 상세가 밀려 나온다(시각 · 설명 · 관련 직원 · 관련 진술 · 핀 해제).
//   직원  6명의 인사 기록. 관계(좋음/나쁨)는 RelationshipSystem 의 Band 로 판정한다.
//
// 이 화면은 보여 주기만 한다. 자료를 만들지 않는다 — 단서는 ClueBoard, 자료 내용은
// InterviewEvidenceBoard, 진술은 DialogueHistory 에서 읽는다.
public partial class PadView : Control
{
    public enum Tab { Manual, Clues, Staff }

    public Vector2I CanvasSize = new(800, 500);
    public event Action CloseRequested;

    public const int CardsPerPage = 6;
    private const float HeaderH = 44f;

    private static readonly Color Bg = new(0.025f, 0.045f, 0.055f);
    private static readonly Color PanelBg = new(0.04f, 0.075f, 0.09f, 0.95f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.50f, 0.62f, 0.66f);
    private static readonly Color Ink = new(0.86f, 0.92f, 0.94f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);
    private static readonly Color Red = new(1f, 0.42f, 0.34f);

    public Tab Current { get; private set; } = Tab.Clues;

    // ── 단서 탭 상태 ──
    public bool GroupByEmployee { get; private set; }
    public string EmployeeFilter { get; private set; } = "";
    public int Page { get; private set; }
    public ClueBoard.Entry DetailEntry { get; private set; }
    private readonly HashSet<string> _openStatements = new();

    // ── 직원 탭 상태 ──
    public string StaffDetailId { get; private set; } = "";

    private Font _font;
    private Label _clock;
    private readonly Dictionary<Tab, Button> _tabButtons = new();
    private Control _manualPage, _cluesPage, _staffPage;
    private Panel _detail;
    private Tween _fade, _slide;
    private PadManualDef _manual;
    private bool _manualBuilt;
    private float _clockTick;

    public override void _Ready()
    {
        _font = ViewFont.Default;
        Size = CanvasSize;
        MouseFilter = MouseFilterEnum.Stop;
        _manual = GD.Load<PadManualDef>(PadManualDef.DefaultPath);

        AddChild(new ColorRect { Color = Bg, Size = CanvasSize, MouseFilter = MouseFilterEnum.Ignore });
        BuildHeader();

        _manualPage = Page0();
        _cluesPage = Page0();
        _staffPage = Page0();

        _detail = new Panel
        {
            Position = new Vector2(CanvasSize.X, HeaderH), Size = new Vector2(CanvasSize.X * 0.5f, CanvasSize.Y - HeaderH),
            MouseFilter = MouseFilterEnum.Stop, Visible = false,
        };
        _detail.AddThemeStyleboxOverride("panel", Box(PanelBg, Cyan with { A = 0.5f }));
        AddChild(_detail);

        ClueBoard.Changed += OnClueChanged;
        SwitchTab(Tab.Clues, fade: false);
    }

    public override void _ExitTree() => ClueBoard.Changed -= OnClueChanged;

    private Control Page0()
    {
        var c = new Control
        {
            Position = new Vector2(0, HeaderH), Size = new Vector2(CanvasSize.X, CanvasSize.Y - HeaderH),
            MouseFilter = MouseFilterEnum.Ignore, Visible = false,
        };
        AddChild(c);
        return c;
    }

    public override void _Process(double delta)
    {
        _clockTick -= (float)delta;
        if (_clockTick > 0f || _clock == null) return;
        _clockTick = 0.5f;
        var gs = GameState.Instance;
        int day = gs?.CurrentDay ?? 1;
        _clock.Text = gs?.CurrentPhase == GamePhase.Rest
            ? $"DAY {day} · 휴게시간"
            : $"DAY {day} · {DialogueClock.Text(gs?.DayTimeSeconds ?? 0f)} · 정지";
    }

    // 패드를 들었다 / 내려놓았다(AdminPad3D).
    public void OnOpened()
    {
        ClueBoard.MarkSeen();
        _clockTick = 0f;
        RefreshHeader();
        RebuildCurrent();
    }

    public void OnClosed() => CloseDetail(instant: true);

    private void OnClueChanged(ClueBoard.Entry entry, bool pinned)
    {
        if (!IsInsideTree()) return;
        if (!pinned && DetailEntry == entry) CloseDetail(instant: true);
        RefreshHeader();
        if (Current == Tab.Clues) RebuildClues();
        if (Current == Tab.Staff) RebuildStaff();
    }

    // ── 머리줄(탭) ────────────────────────────────────────────────────────

    private void BuildHeader()
    {
        var bar = new ColorRect { Color = new Color(0.03f, 0.08f, 0.10f), Size = new Vector2(CanvasSize.X, HeaderH),
                                  MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bar);
        AddChild(new ColorRect { Color = Cyan with { A = 0.35f }, Position = new Vector2(0, HeaderH - 1),
                                 Size = new Vector2(CanvasSize.X, 1), MouseFilter = MouseFilterEnum.Ignore });

        var brand = Lbl("◆ ADMIN PAD", 13, Cyan with { A = 0.8f });
        brand.Position = new Vector2(14, 12);
        AddChild(brand);

        float x = 150f;
        foreach (var (tab, label) in new[] { (Tab.Manual, "지침"), (Tab.Clues, "단서"), (Tab.Staff, "직원") })
        {
            var t = tab;
            var b = Btn(label, Cyan, () => SwitchTab(t), 15);
            b.Position = new Vector2(x, 5);
            b.Size = new Vector2(104, 34);
            AddChild(b);
            _tabButtons[tab] = b;
            x += 110f;
        }

        _clock = Lbl("", 12, Dim);
        _clock.Position = new Vector2(486, 13);
        _clock.Size = new Vector2(196, 20);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_clock);

        var close = Btn("내려놓기", Dim, () => CloseRequested?.Invoke(), 12);
        close.Position = new Vector2(692, 7);
        close.Size = new Vector2(96, 30);
        AddChild(close);
    }

    private void RefreshHeader()
    {
        if (_tabButtons.TryGetValue(Tab.Clues, out var clues)) clues.Text = $"단서 {ClueBoard.Count}";
        foreach (var (tab, b) in _tabButtons) Paint(b, tab == Current, Cyan);
    }

    // ── 탭 전환 ──────────────────────────────────────────────────────────

    public void SwitchTab(Tab tab, bool fade = true)
    {
        Current = tab;
        CloseDetail(instant: true);
        _manualPage.Visible = tab == Tab.Manual;
        _cluesPage.Visible = tab == Tab.Clues;
        _staffPage.Visible = tab == Tab.Staff;
        RefreshHeader();
        RebuildCurrent();

        // 앱이 열리듯 짧은 페이드.
        var page = PageOf(tab);
        _fade?.Kill();
        if (!fade || !IsInsideTree()) { page.Modulate = Colors.White; return; }
        page.Modulate = Colors.White with { A = 0f };
        _fade = CreateTween();
        _fade.TweenProperty(page, "modulate:a", 1f, 0.14);
    }

    private Control PageOf(Tab tab) => tab switch
    {
        Tab.Manual => _manualPage,
        Tab.Staff => _staffPage,
        _ => _cluesPage,
    };

    private void RebuildCurrent()
    {
        switch (Current)
        {
            case Tab.Manual: BuildManual(); break;
            case Tab.Clues: RebuildClues(); break;
            case Tab.Staff: RebuildStaff(); break;
        }
    }

    // 직원 탭에서 "이 직원의 단서" → 단서 탭을 그 직원으로 걸러 연다.
    public void ShowCluesFor(string employeeId)
    {
        EmployeeFilter = employeeId ?? "";
        Page = 0;
        SwitchTab(Tab.Clues);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  지침
    // ═════════════════════════════════════════════════════════════════════

    private void BuildManual()
    {
        if (_manualBuilt) return;
        _manualBuilt = true;

        var scroll = new ScrollContainer
        {
            Position = new Vector2(14, 8), Size = new Vector2(CanvasSize.X - 28, CanvasSize.Y - HeaderH - 16),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _manualPage.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        col.AddChild(Lbl(_manual?.Title ?? "관리자 지침", 18, Cyan));
        var sections = _manual?.Sections() ?? new List<PadManualDef.Section>();
        if (sections.Count == 0) col.AddChild(Lbl("지침 문서를 불러오지 못했습니다.", 13, Dim));
        foreach (var s in sections)
        {
            col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
            if (!string.IsNullOrEmpty(s.Title)) col.AddChild(Lbl("▍" + s.Title, 15, Amber));
            foreach (string p in s.Paragraphs) col.AddChild(Wrap(Lbl(p, 13, Ink)));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  단서
    // ═════════════════════════════════════════════════════════════════════

    // 지금 보이는 단서(필터 · 정렬 적용, 페이지 나누기 전).
    public List<ClueBoard.Entry> VisibleClues()
    {
        IEnumerable<ClueBoard.Entry> list = ClueBoard.Entries;
        if (!string.IsNullOrEmpty(EmployeeFilter)) list = list.Where(e => ClueBoard.Involves(e, EmployeeFilter));

        var byTime = list
            .OrderBy(e => e.Day)
            .ThenBy(e => e.Evidence.HasTime ? 1 : 0)
            .ThenBy(e => e.Evidence.AnchorTime)
            .ThenBy(e => e.Seq);
        if (!GroupByEmployee) return byTime.ToList();

        var order = EmployeeOrder();
        return byTime.OrderBy(e => order.IndexOf(PrimaryEmployee(e.Evidence)) is var i && i >= 0 ? i : 99).ToList();
    }

    public int PageCount => Math.Max(1, (VisibleClues().Count + CardsPerPage - 1) / CardsPerPage);

    public void SetPage(int page)
    {
        Page = Math.Clamp(page, 0, PageCount - 1);
        RebuildClues();
    }

    public void SetGroupByEmployee(bool on)
    {
        GroupByEmployee = on;
        Page = 0;
        RebuildClues();
    }

    public void ClearEmployeeFilter()
    {
        EmployeeFilter = "";
        Page = 0;
        RebuildClues();
    }

    private void RebuildClues()
    {
        Clear(_cluesPage);
        var all = VisibleClues();
        Page = Math.Clamp(Page, 0, Math.Max(0, (all.Count + CardsPerPage - 1) / CardsPerPage - 1));

        // ── 도구줄: 정렬 · 필터 · 페이지 ──
        var sort = Btn(GroupByEmployee ? "정렬: 직원별" : "정렬: 시간순", Cyan,
            () => SetGroupByEmployee(!GroupByEmployee), 12);
        sort.Position = new Vector2(14, 6);
        sort.Size = new Vector2(120, 28);
        _cluesPage.AddChild(sort);

        if (!string.IsNullOrEmpty(EmployeeFilter))
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(EmployeeFilter);
            var chip = Btn($"{def?.Codename ?? EmployeeFilter}  ✕", Readable(def?.IconColor ?? Cyan), ClearEmployeeFilter, 12);
            chip.Position = new Vector2(142, 6);
            chip.Size = new Vector2(110, 28);
            _cluesPage.AddChild(chip);
        }

        int pages = Math.Max(1, (all.Count + CardsPerPage - 1) / CardsPerPage);
        var prev = Btn("◀", Cyan, () => SetPage(Page - 1), 13);
        prev.Position = new Vector2(620, 6);
        prev.Size = new Vector2(40, 28);
        prev.Disabled = Page <= 0;
        _cluesPage.AddChild(prev);
        var pageLbl = Lbl($"{Page + 1} / {pages}", 12, Dim);
        pageLbl.Position = new Vector2(664, 10);
        pageLbl.Size = new Vector2(76, 20);
        pageLbl.HorizontalAlignment = HorizontalAlignment.Center;
        _cluesPage.AddChild(pageLbl);
        var next = Btn("▶", Cyan, () => SetPage(Page + 1), 13);
        next.Position = new Vector2(744, 6);
        next.Size = new Vector2(40, 28);
        next.Disabled = Page >= pages - 1;
        _cluesPage.AddChild(next);

        if (all.Count == 0)
        {
            string empty = string.IsNullOrEmpty(EmployeeFilter)
                ? "핀으로 기록한 단서가 없습니다.\n시설 로그의 ☆를 눌러 수상한 기록을 보관하세요."
                : $"{Codename(EmployeeFilter)} 직원이 등장하는 단서가 없습니다.";
            var l = Wrap(Lbl(empty, 14, Dim));
            l.Position = new Vector2(60, 150);
            l.Size = new Vector2(CanvasSize.X - 120, 120);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            _cluesPage.AddChild(l);
            return;
        }

        var grid = new GridContainer { Columns = 3, Position = new Vector2(14, 42) };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        _cluesPage.AddChild(grid);
        foreach (var entry in all.Skip(Page * CardsPerPage).Take(CardsPerPage))
            grid.AddChild(ClueCard(entry));
    }

    private Control ClueCard(ClueBoard.Entry entry)
    {
        var ev = entry.Evidence;
        var tag = PhoneCallHud.TagColor(ev);
        var card = new Button { CustomMinimumSize = new Vector2(250, 196), FocusMode = FocusModeEnum.None, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", Box(PanelBg, tag with { A = 0.35f }));
        card.AddThemeStyleboxOverride("hover", Box(PanelBg, tag));
        card.AddThemeStyleboxOverride("pressed", Box(PanelBg, tag));
        card.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var captured = entry;
        card.Pressed += () => OpenDetail(captured);

        var thumb = Thumbnail(entry, new Vector2(234, 112));
        thumb.Position = new Vector2(8, 8);
        card.AddChild(thumb);

        var tagLbl = Lbl($" {InterviewEvidenceDisplay.Tag(ev)} ", 11, Colors.Black);
        tagLbl.Position = new Vector2(12, 12);
        var tagBg = new ColorRect { Color = tag, Position = new Vector2(10, 11), Size = new Vector2(46, 20),
                                    MouseFilter = MouseFilterEnum.Ignore };
        card.AddChild(tagBg);
        card.AddChild(tagLbl);

        var title = Lbl(InterviewEvidenceDisplay.Body(ev), 13, Ink);
        title.Position = new Vector2(10, 124);
        title.Size = new Vector2(230, 22);
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        card.AddChild(title);

        var when = Lbl(WhenText(entry), 11, Dim);
        when.Position = new Vector2(10, 148);
        when.Size = new Vector2(230, 18);
        when.ClipText = true;
        card.AddChild(when);

        float dx = 10f;
        foreach (string id in StaffOf(ev))
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
            if (def == null) continue;
            card.AddChild(new ColorRect
            {
                Color = def.IconColor, Position = new Vector2(dx, 174), Size = new Vector2(12, 12),
                MouseFilter = MouseFilterEnum.Ignore,
            });
            dx += 16f;
        }
        return card;
    }

    // 썸네일 — 사건 순간의 CCTV 한 컷. 없으면 사고는 NO SIGNAL, 그 밖의 자료는 종류 글자.
    private Control Thumbnail(ClueBoard.Entry entry, Vector2 size)
    {
        var box = new Control { Size = size, CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        box.AddChild(new ColorRect { Color = new Color(0.01f, 0.02f, 0.025f), Size = size, MouseFilter = MouseFilterEnum.Ignore });

        var tex = entry.Snapshot ?? ClueBoard.SnapshotOf(entry.Day, entry.EvidenceId);
        if (tex != null)
        {
            box.AddChild(new TextureRect
            {
                // 확장 모드를 먼저 정한다 — 텍스처보다 늦게 정하면 원본 이미지 크기로 늘어난다.
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Texture = tex, Size = size, MouseFilter = MouseFilterEnum.Ignore,
            });
            return box;
        }

        bool incident = entry.Evidence.Kind == EvidenceKind.Incident;
        var l = Lbl(incident ? "NO SIGNAL" : InterviewEvidenceDisplay.Tag(entry.Evidence), incident ? 16 : 22,
            incident ? Dim with { A = 0.7f } : PhoneCallHud.TagColor(entry.Evidence) with { A = 0.45f });
        l.Size = size;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(l);
        if (incident)
        {
            // 신호 없는 화면의 가로 줄무늬 — 텍스처 없이 선 몇 개로.
            for (int i = 0; i < 6; i++)
                box.AddChild(new ColorRect
                {
                    Color = Dim with { A = 0.08f + 0.05f * (i % 2) },
                    Position = new Vector2(0, 10 + i * (size.Y - 20) / 5f), Size = new Vector2(size.X, 2),
                    MouseFilter = MouseFilterEnum.Ignore,
                });
        }
        return box;
    }

    // ── 상세 ─────────────────────────────────────────────────────────────

    public void OpenDetail(ClueBoard.Entry entry)
    {
        if (entry == null) return;
        bool same = DetailEntry == entry;
        DetailEntry = entry;
        if (!same) _openStatements.Clear();
        BuildDetail();

        _detail.Visible = true;
        _slide?.Kill();
        if (!IsInsideTree()) { _detail.Position = _detail.Position with { X = CanvasSize.X * 0.5f }; return; }
        if (!same) _detail.Position = _detail.Position with { X = CanvasSize.X };
        _slide = CreateTween();
        _slide.TweenProperty(_detail, "position:x", CanvasSize.X * 0.5f, 0.18)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    public void CloseDetail(bool instant = false)
    {
        if (_detail == null) return;
        DetailEntry = null;
        _slide?.Kill();
        if (instant || !IsInsideTree())
        {
            _detail.Visible = false;
            _detail.Position = _detail.Position with { X = CanvasSize.X };
            return;
        }
        _slide = CreateTween();
        _slide.TweenProperty(_detail, "position:x", (float)CanvasSize.X, 0.14);
        _slide.TweenCallback(Callable.From(() => _detail.Visible = false));
    }

    private void BuildDetail()
    {
        Clear(_detail);
        var entry = DetailEntry;
        if (entry == null) return;
        var ev = entry.Evidence;
        var tag = PhoneCallHud.TagColor(ev);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(12, 10), Size = _detail.Size - new Vector2(24, 20),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _detail.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        var head = new HBoxContainer();
        var title = Lbl($"[{InterviewEvidenceDisplay.Tag(ev)}]  {ev.Header}", 15, tag);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var x = Btn("✕", Dim, () => CloseDetail(), 13);
        x.CustomMinimumSize = new Vector2(32, 28);
        head.AddChild(x);
        col.AddChild(head);

        col.AddChild(Thumbnail(entry, new Vector2(370, 150)));
        col.AddChild(Lbl(WhenText(entry), 13, Cyan));
        col.AddChild(Wrap(Lbl(ev.Body, 13, Ink)));

        // 관련 직원 — 자료에 나오는 사람 + (오늘 자료라면) 같은 시간대 같은 방 기록에 있던 사람.
        col.AddChild(Section("관련 직원"));
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 4);
        var staff = RelatedStaff(entry);
        foreach (string id in staff) chips.AddChild(StaffChip(id));
        if (staff.Count == 0) chips.AddChild(Lbl("—", 12, Dim));
        col.AddChild(chips);

        // 관련 진술 — 이 자료를 들고 한 질문과 그 대답. 직원별로 접어 둔다.
        col.AddChild(Section("관련 진술"));
        var talks = RelatedStatements(entry);
        if (talks.Count == 0)
            col.AddChild(Wrap(Lbl("진술 없음 — 휴게시간에 물어볼 수 있습니다.", 12, Dim)));
        foreach (var (who, lines) in talks)
        {
            string key = who;
            bool open = _openStatements.Contains(key);
            int answers = lines.Count(l => l.EntryType != DialogueEntryType.PlayerChoice);
            var toggle = Btn($"{(open ? "▾" : "▸")} {Codename(who)}의 진술 ({answers})",
                Readable(FacilitySimulation.Instance?.GetEmployeeDef(who)?.IconColor ?? Cyan), () =>
                {
                    if (!_openStatements.Remove(key)) _openStatements.Add(key);
                    BuildDetail();
                }, 12);
            toggle.Alignment = HorizontalAlignment.Left;
            toggle.CustomMinimumSize = new Vector2(0, 28);
            col.AddChild(toggle);
            if (!open) continue;
            foreach (var l in lines)
            {
                bool mine = l.EntryType == DialogueEntryType.PlayerChoice;
                col.AddChild(Wrap(Lbl((mine ? "   관리자: " : $"   {l.SpeakerDisplayName}: ") + l.Text, 12, mine ? Dim : Ink)));
            }
        }

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var unpin = Btn("★ 핀 해제", Amber, () => ClueBoard.Unpin(entry.Day, entry.EvidenceId), 13);
        unpin.CustomMinimumSize = new Vector2(0, 32);
        col.AddChild(unpin);
    }

    // 이 단서의 관련 직원. 오늘 단서면 조사 자료판의 "같은 시간대" 창(SameWindow)을 그대로 써서
    // 같은 방 기록에 잡힌 사람까지 더한다. 지난 날 단서는 원천이 비워졌으므로 단서 자체만 본다.
    public static List<string> RelatedStaff(ClueBoard.Entry entry)
    {
        var ev = entry?.Evidence;
        var ids = new List<string>();
        if (ev == null) return ids;
        foreach (string id in StaffOf(ev)) if (!ids.Contains(id)) ids.Add(id);

        int today = GameState.Instance?.CurrentDay ?? 1;
        if (entry.Day != today || !ev.HasTime || string.IsNullOrEmpty(ev.SubjectRoomId)) return ids;

        var board = InterviewEvidenceBoard.BuildAll("");
        foreach (string otherId in InterviewEvidenceBoard.SameWindow(board, ev))
        {
            var other = InterviewEvidenceBoard.Find(board, otherId);
            if (other == null || other.SubjectRoomId != ev.SubjectRoomId || other.Position == PositionClaim.None) continue;
            foreach (string id in StaffOf(other)) if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    // 이 단서를 들고 한 질문 · 대답(그 단서의 날). 심문한 직원별로 묶는다.
    public static List<(string Employee, List<DialogueHistoryEntry> Lines)> RelatedStatements(ClueBoard.Entry entry)
    {
        var result = new List<(string, List<DialogueHistoryEntry>)>();
        var all = DialogueHistory.Instance?.GetAllEntries();
        if (entry == null || all == null) return result;
        foreach (var g in all.Where(e => e.Day == entry.Day && e.Mentions(entry.EvidenceId)
                                         && !string.IsNullOrEmpty(e.CounterpartId))
                     .GroupBy(e => e.CounterpartId))
            result.Add((g.Key, g.OrderBy(e => e.Seq).ToList()));
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  직원
    // ═════════════════════════════════════════════════════════════════════

    public void OpenStaffDetail(string employeeId)
    {
        StaffDetailId = employeeId ?? "";
        RebuildStaff();
    }

    private void RebuildStaff()
    {
        Clear(_staffPage);
        if (!string.IsNullOrEmpty(StaffDetailId)) { BuildStaffDetail(StaffDetailId); return; }

        var grid = new GridContainer { Columns = 3, Position = new Vector2(14, 12) };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        _staffPage.AddChild(grid);
        foreach (string id in EmployeeOrder()) grid.AddChild(StaffCard(id));
    }

    private Control StaffCard(string id)
    {
        var sim = FacilitySimulation.Instance;
        var def = sim?.GetEmployeeDef(id);
        var color = def?.IconColor ?? Cyan;
        var card = new Button { CustomMinimumSize = new Vector2(250, 208), FocusMode = FocusModeEnum.None, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", Box(PanelBg, color with { A = 0.4f }));
        card.AddThemeStyleboxOverride("hover", Box(PanelBg, color));
        card.AddThemeStyleboxOverride("pressed", Box(PanelBg, color));
        card.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        string captured = id;
        card.Pressed += () => OpenStaffDetail(captured);

        var face = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = def?.FacePortrait ?? def?.StandingImage, Position = new Vector2(65, 10), Size = new Vector2(120, 120),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        card.AddChild(face);

        var name = Lbl(def?.Codename ?? id, 16, Readable(color));
        name.Position = new Vector2(10, 136);
        name.Size = new Vector2(230, 24);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(name);

        var (badge, badgeColor) = StatusOf(id);
        if (!string.IsNullOrEmpty(badge))
        {
            var b = Lbl($" {badge} ", 11, Colors.Black);
            var bg = new ColorRect { Color = badgeColor, Position = new Vector2(180, 12), Size = new Vector2(58, 20),
                                     MouseFilter = MouseFilterEnum.Ignore };
            b.Position = new Vector2(184, 13);
            card.AddChild(bg);
            card.AddChild(b);
        }

        int clues = ClueBoard.CountFor(id);
        var count = Lbl(clues > 0 ? $"단서 {clues}건" : "단서 없음", 11, clues > 0 ? Amber : Dim);
        count.Position = new Vector2(10, 166);
        count.Size = new Vector2(230, 18);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        card.AddChild(count);
        return card;
    }

    // 사망 / 격리 / 아직 합류 전.
    public static (string Text, Color Color) StatusOf(string id)
    {
        var sim = FacilitySimulation.Instance;
        var st = sim?.GetEmployeeState(id);
        var def = sim?.GetEmployeeDef(id);
        if (st != null && !st.Alive) return ("사망", Red);
        if (st is { Isolated: true }) return ("격리", Amber);
        if (def != null && def.UnlockDay > (GameState.Instance?.CurrentDay ?? 1)) return ("미합류", Dim);
        return ("", Dim);
    }

    private void BuildStaffDetail(string id)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
        var color = Readable(def?.IconColor ?? Cyan);

        var back = Btn("◀ 직원 목록", Cyan, () => OpenStaffDetail(""), 12);
        back.Position = new Vector2(14, 8);
        back.Size = new Vector2(110, 28);
        _staffPage.AddChild(back);

        var art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = def?.StandingImage ?? def?.FacePortrait, Position = new Vector2(14, 42), Size = new Vector2(250, 404),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _staffPage.AddChild(art);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(280, 12), Size = new Vector2(506, CanvasSize.Y - HeaderH - 24),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _staffPage.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 12);
        nameRow.AddChild(Lbl(def?.Codename ?? id, 22, color));
        if (!string.IsNullOrEmpty(def?.Gender)) nameRow.AddChild(Lbl(def.Gender, 12, Dim));
        var (badge, badgeColor) = StatusOf(id);
        if (!string.IsNullOrEmpty(badge)) nameRow.AddChild(Lbl($"[{badge}]", 12, badgeColor));
        col.AddChild(nameRow);

        if (!string.IsNullOrEmpty(def?.ShortProfileLine)) col.AddChild(Wrap(Lbl(def.ShortProfileLine, 13, Ink)));

        col.AddChild(Section("자기소개"));
        col.AddChild(Wrap(Lbl(string.IsNullOrEmpty(def?.SelfIntroLine) ? "—" : $"“{def.SelfIntroLine}”", 13, color)));

        var (good, bad) = Relations(id);
        col.AddChild(Section("사이 좋은 직원"));
        col.AddChild(ChipRow(good));
        col.AddChild(Section("사이 안 좋은 직원"));
        col.AddChild(ChipRow(bad));

        col.AddChild(Section("TMI"));
        col.AddChild(Wrap(Lbl($"좋아하는 음식 · {Or(def?.FavoriteFood)}", 12, Ink)));
        col.AddChild(Wrap(Lbl($"싫어하는 음식 · {Or(def?.DislikedFood)}", 12, Ink)));

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        int clues = ClueBoard.CountFor(id);
        var link = Btn($"이 직원의 단서 {clues}건 보기  ▶", Amber, () => ShowCluesFor(id), 13);
        link.CustomMinimumSize = new Vector2(0, 32);
        col.AddChild(link);
    }

    // 관계는 따로 적어 두지 않는다 — RelationshipSystem 의 Band 로 그때그때 판정한다.
    public static (List<string> Good, List<string> Bad) Relations(string id)
    {
        var good = new List<string>();
        var bad = new List<string>();
        foreach (string other in EmployeeOrder())
        {
            if (other == id) continue;
            var band = RelationshipSystem.Band(id, other);
            if (band is PairBand.Close or PairBand.Friendly) good.Add(other);
            else if (band is PairBand.Uneasy or PairBand.Refuse) bad.Add(other);
        }
        return (good, bad);
    }

    private Control ChipRow(List<string> ids)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        if (ids.Count == 0) row.AddChild(Lbl("—", 12, Dim));
        foreach (string id in ids) row.AddChild(StaffChip(id));
        return row;
    }

    private Control StaffChip(string id)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
        var c = def?.IconColor ?? Cyan;
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = c with { A = 0.22f }, BorderColor = c,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 1, ContentMarginBottom = 1,
        });
        chip.AddChild(Lbl(def?.Codename ?? id, 12, Readable(c)));
        return chip;
    }

    // ── 공통 ─────────────────────────────────────────────────────────────

    // 자료에 나오는 직원(그 사람 · 말한 사람 · 함께 있던 사람).
    private static List<string> StaffOf(InterviewEvidence ev)
    {
        var ids = new List<string>();
        void Add(string id) { if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id); }
        Add(ev?.SubjectEmployeeId);
        Add(ev?.SpeakerEmployeeId);
        if (ev != null) foreach (string id in ev.RelatedEmployeeIds) Add(id);
        return ids;
    }

    private static string PrimaryEmployee(InterviewEvidence ev) => StaffOf(ev).FirstOrDefault() ?? "";

    private static List<string> EmployeeOrder() =>
        FacilitySimulation.Instance?.GetEmployeeIds().ToList() ?? new List<string>();

    private static string WhenText(ClueBoard.Entry entry)
    {
        string t = entry.Evidence.TimeText;
        return string.IsNullOrEmpty(t) ? $"DAY {entry.Day} · 근무 전" : $"DAY {entry.Day} · {t}";
    }

    private static string Codename(string id) => InterviewEvidenceBoard.Codename(id);

    private static string Or(string s) => string.IsNullOrEmpty(s) ? "—" : s;

    // 어두운 고유색(늑대 등)이 어두운 화면에서 안 읽히지 않게 최소 밝기만 끌어올린다.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.6f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }

    private static void Clear(Node n)
    {
        foreach (Node c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    private Label Section(string text)
    {
        var l = Lbl(text, 12, Cyan with { A = 0.75f });
        l.CustomMinimumSize = new Vector2(0, 22);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return l;
    }

    private Label Lbl(string text, int size, Color color)
    {
        var l = new Label { Text = text ?? "", MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private Button Btn(string text, Color accent, Action onPressed, int size)
    {
        var b = MonitorUi.Button(text, accent, _font ?? ViewFont.Default, onPressed, ViewFont.S(size));
        b.FocusMode = FocusModeEnum.None;
        Paint(b, false, accent);
        return b;
    }

    private static void Paint(Button b, bool on, Color accent)
    {
        var normal = Box(on ? accent with { A = 0.22f } : new Color(0.04f, 0.08f, 0.10f, 0.7f),
            on ? accent : accent with { A = 0.35f });
        var hover = Box(accent with { A = 0.18f }, accent);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.03f, 0.05f, 0.06f, 0.5f), accent with { A = 0.12f }));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", on ? Colors.White : accent);
    }

    private static StyleBoxFlat Box(Color bg, Color border) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2,
    };
}
