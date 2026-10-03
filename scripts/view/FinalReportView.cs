using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 왼쪽 CRT — DAY5 가 끝나고 제출하는 마지막 절차 「격리 대상 지정 보고서」.
//
// 5일 동안 모은 기록으로 한 명을 지목하는 화면이다. 제출하면 되돌릴 수 없고,
// 그 결과(GameState.WasCaught)가 엔딩을 가른다. 붙인 근거(GameState.WasProven)는
// 엔딩을 바꾸지 않는다 — 찍어서 맞춘 사람과 증명해서 맞춘 사람을 구분할 뿐이다.
//
// 이 화면은 제출 전까지 빠져나갈 수 없다(ControlRoom3DController.SetFocusLocked ·
// PauseMenu 의 ESC 차단). 판정은 GameState.SubmitFinalReport 가 한 곳에서 한다.
public partial class FinalReportView : Control
{
    public static FinalReportView Instance { get; private set; }

    // 보고서가 떠 있는 동안 — ESC · 뒤로가기 · 화면 확대 전환을 전부 막는다.
    public static bool IsOpen { get; private set; }

    public event Action Submitted;

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Bg = new(0.018f, 0.028f, 0.030f);
    private static readonly Color Ink = new(0.84f, 0.90f, 0.89f);
    private static readonly Color Mint = new(0.46f, 0.92f, 0.80f);
    private static readonly Color Dim = new(0.40f, 0.50f, 0.52f);
    private static readonly Color Err = new(0.98f, 0.30f, 0.24f);

    // 지목 카드 — 직원 6명 + "지목하지 않음" 한 장.
    private const string NoPick = "";
    private const float CardTop = 116f, CardW = 102f, CardH = 134f, CardGap = 9f, CardLeft = 16f;
    private const int MaxEvidence = 2;

    private sealed class Pick
    {
        public string Id = "";
        public string Label = "";
        public Texture2D Face;
        public Color Tint = Colors.White;
        public bool Alive = true;
        public bool IsNone;
    }

    private readonly List<Pick> _picks = new();
    private readonly List<ClueBoard.Entry> _clues = new();
    private readonly List<(int Day, string EvidenceId)> _chosen = new();

    private string _selected = NoPick;
    private bool _submitted;
    private Font _font;
    private int _hover = -1;

    private Control _clueList;
    private Button _submitBtn;
    private Control _confirm;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (IsOpen) IsOpen = false;
    }

    // --- 열기 ---------------------------------------------------------------

    // DAY5 가 끝난 시점의 명단 · 단서판을 읽어 화면을 세운다.
    public void Present()
    {
        BuildPicks();
        BuildClues();
        _chosen.Clear();
        _submitted = false;

        // 기본 선택 — 이미 격리해 둔 직원이 있으면 그 사람이 미리 지목돼 있다.
        var sim = FacilitySimulation.Instance;
        _selected = _picks.FirstOrDefault(p => !p.IsNone && p.Alive
                                               && (sim?.GetEmployeeState(p.Id)?.Isolated ?? false))?.Id ?? NoPick;

        BuildUi();
        Visible = true;
        IsOpen = true;
        SetProcess(true);
        QueueRedraw();
    }

    private void BuildPicks()
    {
        _picks.Clear();
        var sim = FacilitySimulation.Instance;
        if (sim != null)
        {
            foreach (string id in StaffIdCard.Sorted(sim.GetActiveEmployeeIds()))
            {
                var def = sim.GetEmployeeDef(id);
                if (def == null) continue;
                var st = sim.GetEmployeeState(id);
                _picks.Add(new Pick
                {
                    Id = id,
                    Label = string.IsNullOrEmpty(def.Codename) ? id : def.Codename,
                    Face = def.FacePortrait ?? def.IdPhoto,
                    Tint = def.IconColor,
                    Alive = st?.Alive ?? true,
                });
            }
        }
        _picks.Add(new Pick { Id = NoPick, Label = "지목 없음", IsNone = true });
    }

    private void BuildClues()
    {
        _clues.Clear();
        _clues.AddRange(ClueBoard.Entries.OrderBy(e => e.Day).ThenBy(e => e.Seq));
    }

    // --- UI -----------------------------------------------------------------

    private void BuildUi()
    {
        foreach (var c in GetChildren().OfType<Node>().ToList()) c.QueueFree();
        _confirm = null;

        _clueList = new Control
        {
            Position = new Vector2(30f, 292f),
            Size = new Vector2(Canvas.X - 60f, 196f),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_clueList);
        RebuildClueRows();

        _submitBtn = MonitorUi.Button("[ 제 출 ]", Mint, _font, OpenConfirm, ViewFont.FS(20));
        _submitBtn.Position = new Vector2((Canvas.X - 240f) * 0.5f, 522f);
        _submitBtn.Size = new Vector2(240f, 48f);
        AddChild(_submitBtn);
    }

    private void RebuildClueRows()
    {
        if (_clueList == null) return;
        foreach (var c in _clueList.GetChildren().OfType<Node>().ToList()) c.QueueFree();

        if (_clues.Count == 0)
        {
            var empty = Lbl("보관한 단서가 없습니다. 근거 없이 제출할 수 있습니다.", 14, Dim);
            empty.Position = new Vector2(6f, 10f);
            empty.Size = new Vector2(_clueList.Size.X - 12f, 24f);
            _clueList.AddChild(empty);
            return;
        }

        const float rowH = 46f, gap = 6f;
        float colW = (_clueList.Size.X - 10f) * 0.5f;
        for (int i = 0; i < _clues.Count && i < 8; i++)
        {
            var e = _clues[i];
            bool on = _chosen.Contains((e.Day, e.EvidenceId));
            int col = i % 2, row = i / 2;
            var btn = ClueButton(e, on);
            btn.Position = new Vector2(col * (colW + 10f), row * (rowH + gap));
            btn.Size = new Vector2(colW, rowH);
            _clueList.AddChild(btn);
        }
        if (_clues.Count > 8)
        {
            var more = Lbl($"… 외 {_clues.Count - 8}장 (최신 8장만 제시할 수 있습니다)", 12, Dim);
            more.Position = new Vector2(6f, 4f * (rowH + gap));
            more.Size = new Vector2(_clueList.Size.X - 12f, 20f);
            _clueList.AddChild(more);
        }
    }

    private Button ClueButton(ClueBoard.Entry e, bool on)
    {
        var ev = e.Evidence;
        string head = $"DAY{e.Day} · {ev?.Tag ?? ""} {ev?.TimeText ?? ""}".TrimEnd();
        string body = ev?.Body ?? "";
        var btn = new Button
        {
            Text = $"{(on ? "■" : "□")} {head}\n     {body}",
            ClipText = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        btn.AddThemeFontOverride("font", _font);
        btn.AddThemeFontSizeOverride("font_size", ViewFont.FS(13));
        btn.AddThemeColorOverride("font_color", on ? Mint : Ink);
        btn.AddThemeColorOverride("font_hover_color", Mint);
        btn.AddThemeStyleboxOverride("normal", Box(on ? Mint : Dim, on ? 0.14f : 0.05f));
        btn.AddThemeStyleboxOverride("hover", Box(Mint, 0.12f));
        btn.AddThemeStyleboxOverride("pressed", Box(Mint, 0.2f));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.Pressed += () => ToggleClue(e);
        return btn;
    }

    private static StyleBoxFlat Box(Color line, float fill) => new()
    {
        BgColor = line with { A = fill },
        BorderColor = line with { A = 0.55f },
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    private void ToggleClue(ClueBoard.Entry e)
    {
        if (_submitted) return;
        var key = (e.Day, e.EvidenceId);
        if (_chosen.Remove(key)) { Sfx.Instance?.Play("tick", -18f); }
        else
        {
            if (_chosen.Count >= MaxEvidence) { Sfx.Instance?.Play("alert_beep3", -24f); return; }
            _chosen.Add(key);
            Sfx.Instance?.Play("tick", -14f);
        }
        RebuildClueRows();
        QueueRedraw();
    }

    // --- 입력 ---------------------------------------------------------------

    public override void _GuiInput(InputEvent @event)
    {
        if (_submitted || _confirm != null) return;
        switch (@event)
        {
            case InputEventMouseMotion mm:
            {
                int h = CardAt(mm.Position);
                if (h != _hover) { _hover = h; QueueRedraw(); }
                break;
            }
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
            {
                int i = CardAt(mb.Position);
                if (i < 0) break;
                var p = _picks[i];
                if (!p.IsNone && !p.Alive) { Sfx.Instance?.Play("alert_beep3", -24f); break; }
                _selected = p.Id;
                Sfx.Instance?.Play("click", -12f);
                QueueRedraw();
                break;
            }
        }
    }

    private int CardAt(Vector2 p)
    {
        for (int i = 0; i < _picks.Count; i++)
            if (CardRect(i).HasPoint(p)) return i;
        return -1;
    }

    private static Rect2 CardRect(int i) =>
        new(CardLeft + i * (CardW + CardGap), CardTop, CardW, CardH);

    // --- 제출 ---------------------------------------------------------------

    private void OpenConfirm()
    {
        if (_submitted || _confirm != null) return;
        Sfx.Instance?.Play("click", -10f);

        _confirm = new Control { MouseFilter = MouseFilterEnum.Stop };
        _confirm.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_confirm);

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        _confirm.AddChild(scrim);

        var panel = new Panel { Position = new Vector2(150f, 214f), Size = new Vector2(500f, 172f) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.08f, 0.10f, 0.97f),
            BorderColor = Mint with { A = 0.6f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        _confirm.AddChild(panel);

        var who = _picks.FirstOrDefault(p => p.Id == _selected && !p.IsNone);
        var l1 = Lbl(who == null ? "지목 없이 제출합니다." : $"격리 대상 : {who.Label}", 20,
            who == null ? Ink : Mint);
        l1.Position = new Vector2(0f, 22f);
        l1.Size = new Vector2(panel.Size.X, 28f);
        l1.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(l1);

        var l2 = Lbl("제출 후에는 수정할 수 없습니다. 제출하시겠습니까?", 15, Ink);
        l2.Position = new Vector2(0f, 62f);
        l2.Size = new Vector2(panel.Size.X, 24f);
        l2.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(l2);

        var yes = MonitorUi.Button("[ 예 ]", Mint, _font, Submit, ViewFont.FS(17));
        yes.Position = new Vector2(70f, 108f);
        yes.Size = new Vector2(160f, 44f);
        panel.AddChild(yes);

        var no = MonitorUi.Button("[ 아니오 ]", Ink, _font, CloseConfirm, ViewFont.FS(17));
        no.Position = new Vector2(270f, 108f);
        no.Size = new Vector2(160f, 44f);
        panel.AddChild(no);
    }

    private void CloseConfirm()
    {
        Sfx.Instance?.Play("click", -14f);
        _confirm?.QueueFree();
        _confirm = null;
    }

    private void Submit()
    {
        if (_submitted) return;
        _submitted = true;
        IsOpen = false;
        _confirm?.QueueFree();
        _confirm = null;
        if (_submitBtn != null) _submitBtn.Disabled = true;

        GameState.Instance?.SubmitFinalReport(_selected, _chosen,
            (day, id) => ClueBoard.Involves(ClueBoard.Find(day, id), _selected));

        Sfx.Instance?.Play("task_done", -8f);
        QueueRedraw();
        Submitted?.Invoke();
    }

    // --- 그리기 --------------------------------------------------------------

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), Bg);
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.26f }, false, 1.4f);

        DrawString(_font, new Vector2(30f, 48f), "격리 대상 지정 보고서", HorizontalAlignment.Left,
            520f, ViewFont.S(24), Mint);
        DrawString(_font, new Vector2(32f, 70f), "FINAL ISOLATION REPORT", HorizontalAlignment.Left,
            520f, ViewFont.S(12), Dim);
        DrawString(_font, new Vector2(Canvas.X - 200f, 48f), "NSP-07 · DAY 5",
            HorizontalAlignment.Right, 170f, ViewFont.S(14), Dim);
        DrawRect(new Rect2(30f, 80f, Canvas.X - 60f, 1f), Mint with { A = 0.22f });

        DrawString(_font, new Vector2(30f, 106f), "[지목 대상]", HorizontalAlignment.Left,
            300f, ViewFont.S(15), Ink);
        for (int i = 0; i < _picks.Count; i++) DrawCard(i);

        DrawString(_font, new Vector2(30f, 278f), "[근거 자료]", HorizontalAlignment.Left,
            200f, ViewFont.S(15), Ink);
        DrawString(_font, new Vector2(140f, 278f),
            $"선택 사항 · 최대 {MaxEvidence}장   ({_chosen.Count}/{MaxEvidence})",
            HorizontalAlignment.Left, 420f, ViewFont.S(13), Dim);
        // 근거 칸은 비어 있어도 자리가 보이게 테두리를 둔다(단서를 한 장도 안 찍은 판).
        DrawRect(new Rect2(24f, 286f, Canvas.X - 48f, 208f), Dim with { A = 0.22f }, false, 1f);

        DrawRect(new Rect2(30f, 500f, Canvas.X - 60f, 1f), Mint with { A = 0.22f });

        if (_submitted)
            DrawString(_font, new Vector2(0f, 578f), "제출되었습니다.", HorizontalAlignment.Center,
                Canvas.X, ViewFont.S(14), Mint);

        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }

    private void DrawCard(int i)
    {
        var p = _picks[i];
        var r = CardRect(i);
        bool on = p.Id == _selected;
        bool dead = !p.IsNone && !p.Alive;
        bool hot = i == _hover && !dead;

        var line = dead ? Dim : on ? Mint : hot ? Ink : Dim;
        DrawRect(r, new Color(0.04f, 0.09f, 0.10f, dead ? 0.45f : 0.9f));
        if (on) DrawRect(r, Mint with { A = 0.10f });
        DrawRect(r, line with { A = dead ? 0.3f : on ? 0.95f : 0.5f }, false, on ? 2.2f : 1.2f);

        var box = new Rect2(r.Position.X + (r.Size.X - 60f) * 0.5f, r.Position.Y + 12f, 60f, 60f);
        if (p.IsNone)
        {
            DrawRect(box, new Color(0.02f, 0.05f, 0.06f, 0.9f));
            DrawString(_font, new Vector2(box.Position.X, box.Position.Y + 40f), "—",
                HorizontalAlignment.Center, box.Size.X, ViewFont.S(28), Dim);
        }
        else if (p.Face != null)
        {
            var src = p.Face.GetSize();
            if (src.X > 0f && src.Y > 0f)
            {
                float k = Mathf.Min(box.Size.X / src.X, box.Size.Y / src.Y);
                var dst = src * k;
                DrawTextureRect(p.Face, new Rect2(box.Position + (box.Size - dst) * 0.5f, dst), false,
                    dead ? new Color(0.45f, 0.45f, 0.45f) : Colors.White);
            }
        }
        else
        {
            DrawCircle(box.GetCenter(), 22f, dead ? Dim : p.Tint);
        }

        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 94f), p.Label,
            HorizontalAlignment.Center, r.Size.X, ViewFont.S(p.IsNone ? 13 : 16),
            dead ? Dim : on ? Mint : Ink);
        if (dead)
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 118f), "응답 없음",
                HorizontalAlignment.Center, r.Size.X, ViewFont.S(12), Err with { A = 0.75f });
        else if (on && !p.IsNone)
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 118f), "지목",
                HorizontalAlignment.Center, r.Size.X, ViewFont.S(12), Mint);
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }
}
