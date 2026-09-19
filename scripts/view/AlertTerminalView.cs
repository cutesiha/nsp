using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 책상 위 경고 단말기(SENSOR 전용 화면).
//
// 역할은 "왜 / 언제 / 방치하면 어떻게 되는가" 다. 어디가 위험한지는 미니맵, 현장 모습은
// CCTV, 직원의 주관적 보고는 전화, 시간순 기록은 시설 로그가 담당한다.
//
// 판정은 전혀 하지 않는다 — IncidentBoard 가 정리해 준 목록을 페이지로 넘겨 보여줄 뿐이다.
//   PAGE 1      : 현재 상태 요약(정상/주의/경고/사고 + 개수 + 최우선 항목)
//   PAGE 2..N   : 사고/위험 카드 한 건씩 (원인 · 결과 · 조치)
//   마지막 PAGE : 최근 해결 기록
//
// SENSOR 전원이 꺼지면 사전 경고·카운트다운·원인 분석이 끊긴다(사고 자체는 그대로 발생).
// [Tool] — 에디터 뷰포트에서도 화면이 보이게 한다(에디터엔 autoload가 없어 "전원 차단").
[Tool]
public partial class AlertTerminalView : Control
{
    // AlertTerminalProp 이 이 뷰를 560x300 논리 캔버스에 올린다.
    private const float CanvasW = 560f;
    private const float CanvasH = 300f;
    private const float FlashSeconds = 3.0f;

    private static readonly Color Ok = new(0.40f, 0.95f, 0.50f);
    private static readonly Color Caution = new(0.95f, 0.80f, 0.25f);
    private static readonly Color Warn = new(1f, 0.62f, 0.15f);
    private static readonly Color Crit = new(1f, 0.40f, 0.20f);
    private static readonly Color Dim = new(0.42f, 0.60f, 0.48f);
    private static readonly Color Body = new(0.72f, 0.88f, 0.78f);

    private Label _head, _status, _title, _sub, _body, _page;
    private Button _prev, _next;
    private bool _logWired;
    private double _failureFlashUntil = -1;
    private double _nextBeepAt;
    private bool _wasLive;
    private int _pageIndex;
    private int _pageCount = 1;
    private string _lastTopIncidentId = "";
    private AlertSeverity _lastSeverity = AlertSeverity.Notice;
    private List<IncidentDisplayData> _items = new();

    public AlertSeverity CurrentSeverity { get; private set; } = AlertSeverity.Notice;
    public bool InFailureFlash { get; private set; }
    // 이번 근무에 사망자가 나왔는가 — 경광등이 검정(소등)으로 바뀐다.
    public bool DeathSeen { get; private set; }

    public override void _Ready()
    {
        EnsureBuilt();
    }

    // 화면 위젯을 만든다. 노드가 이미 있으면(예: 이 스크립트의 C# 인스턴스만 새로 만들어져
    // 참조가 날아간 경우) 이름으로 다시 찾아 붙인다.
    //
    // _Ready 에서 한 번만 대입하면 안 되는 이유: Godot 이 어떤 이유로든 스크립트의 관리
    // 인스턴스를 다시 만들면 _Ready 는 다시 불리지 않고 _Process 만 계속 돈다. 그러면
    // 아래 필드들이 전부 null 인 채로 Show()/SetNav() 가 불려 매 프레임
    // NullReferenceException 이 쏟아진다(화면은 멈추고 콘솔만 수천 줄로 찬다).
    // 그래서 _Process 도 맨 앞에서 이 함수를 부른다.
    private void EnsureBuilt()
    {
        if (_head != null && IsInstanceValid(_head)) return;

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;

        var font = ViewFont.Default;
        const float x = 16f, w = CanvasW - x * 2f;

        var bg = Adopt("Bg", () =>
        {
            var c = new ColorRect { Color = new Color(0.015f, 0.02f, 0.015f), MouseFilter = MouseFilterEnum.Ignore };
            c.SetAnchorsPreset(LayoutPreset.FullRect);
            return c;
        });
        MoveChild(bg, 0); // 배경은 항상 맨 뒤(먼저 그려짐)

        _head = Adopt("Head", () => Lbl("경고 단말기", 20, Dim, font, new Vector2(x, 4), 280, 26));

        _status = Adopt("Status", () =>
        {
            var l = Lbl("", 23, Ok, font, new Vector2(CanvasW - 210f, 3), 194, 32);
            l.HorizontalAlignment = HorizontalAlignment.Right;
            return l;
        });

        _title = Adopt("Title", () => Lbl("정상 가동 중", 34, Ok, font, new Vector2(x, 30), w, 46));

        _sub = Adopt("Sub", () => Lbl("", 26, Dim, font, new Vector2(x, 76), w, 34));

        // 본문은 원인/결과/조치 3줄로 줄이고 글자를 크게 잡는다(멀리서도 읽혀야 한다).
        // 페이지 화살표(y 258) 위에서 끊는다.
        _body = Adopt("Body", () => Lbl("", 25, Body, font, new Vector2(x, 112), w, 142));

        _page = Adopt("Page", () =>
        {
            var l = Lbl("", 19, Dim, font, new Vector2(CanvasW - 178f, CanvasH - 40f), 96, 30);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            return l;
        });

        _prev = Adopt("Prev", () => NavButton("◀", font, new Vector2(CanvasW - 224f, CanvasH - 42f)));
        _prev.Pressed += () => Turn(-1);

        _next = Adopt("Next", () => NavButton("▶", font, new Vector2(CanvasW - 76f, CanvasH - 42f)));
        _next.Pressed += () => Turn(+1);
    }

    // 같은 이름의 자식이 이미 있으면 그것을 쓰고, 없을 때만 만들어 붙인다.
    private T Adopt<T>(string name, System.Func<T> create) where T : Control
    {
        var existing = GetNodeOrNull<T>(name);
        if (existing != null && IsInstanceValid(existing)) return existing;

        T made = create();
        made.Name = name;
        AddChild(made);
        return made;
    }

    private void Turn(int step)
    {
        if (_pageCount <= 1) return;
        _pageIndex = (_pageIndex + step + _pageCount) % _pageCount;
    }

    private static Label Lbl(string t, int size, Color c, Font font, Vector2 pos, float w, float h)
    {
        var l = new Label
        {
            Text = t,
            Position = pos,
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, 0),
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
        };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    private static Button NavButton(string text, Font font, Vector2 pos)
    {
        var b = new Button
        {
            Text = text,
            Position = pos,
            Size = new Vector2(46, 34),
            CustomMinimumSize = new Vector2(46, 34),
            MouseFilter = MouseFilterEnum.Stop,
        };
        b.AddThemeFontOverride("font", font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.S(20));
        b.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.65f));
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.12f, 0.08f),
            BorderColor = new Color(0.3f, 0.55f, 0.38f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.14f, 0.3f, 0.18f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    public override void _Process(double delta)
    {
        // _Ready 가 돌지 않았거나 참조가 날아간 상태로 들어와도 아래에서 null 을 밟지 않게 한다.
        EnsureBuilt();
        WireLog();

        bool live = GameState.Instance?.CurrentPhase == GamePhase.Live;
        if (live && !_wasLive) DeathSeen = false;
        _wasLive = live;

        double now = Time.GetTicksMsec() / 1000.0;
        InFailureFlash = now < _failureFlashUntil;

        if (GameState.Instance == null || FacilitySimulation.Instance == null)
        {
            Show("경고 단말기", "", "전원 차단", "", "사고 예측 불가", Dim, AlertSeverity.Notice);
            SetNav(1);
            return;
        }

        // 근무가 끝나면 단말기는 대기 상태다 — 경고음도 여기서 멈춘다.
        if (!live)
        {
            CurrentSeverity = AlertSeverity.Notice;
            _lastSeverity = AlertSeverity.Notice;
            InFailureFlash = false;
            _failureFlashUntil = -1;
            _pageIndex = 0;
            _lastTopIncidentId = "";
            Show("경고 단말기", "대기", "대기 중", "", "근무가 시작되면 감시를 재개합니다.",
                Dim, AlertSeverity.Notice);
            SetNav(1);
            return;
        }

        // SENSOR 전원이 없으면 사전 경고·카운트다운·원인 분석이 끊긴다.
        if (!GameState.Instance.IsConsumerPowered(PowerConsumer.Sensor))
        {
            CurrentSeverity = AlertSeverity.Notice;
            Show("경고 단말기", "OFFLINE", "전원 차단", "",
                "사고 예측 · 원인 분석 불가\n전력을 배분하면 현재 상태를 다시 확인할 수 있습니다.",
                Dim, AlertSeverity.Notice);
            SetNav(1);
            return;
        }

        _items = IncidentBoard.Build();
        var recent = IncidentTracker.Recent;
        // 1페이지(요약) + 항목별 카드 + 최근 기록(있을 때만).
        SetNav(1 + _items.Count + (recent.Count > 0 ? 1 : 0));

        // 새 사고가 열리면 그 카드를 잠깐 강제로 띄운다.
        var topActive = _items.FirstOrDefault(i => i.State == IncidentState.Active);
        if (topActive != null && topActive.IncidentId != _lastTopIncidentId)
        {
            _lastTopIncidentId = topActive.IncidentId;
            _failureFlashUntil = now + FlashSeconds;
            _pageIndex = 1 + _items.IndexOf(topActive);
            Sfx.Instance?.Play("sensor_beep", -4f, 0.8f);
        }
        if (topActive == null) _lastTopIncidentId = "";

        if (_pageIndex == 0) DrawSummary();
        else if (_pageIndex <= _items.Count) DrawCard(_items[_pageIndex - 1]);
        else DrawRecent(recent);

        TickBeep(now);
    }

    // --- 페이지 ---------------------------------------------------------

    private void DrawSummary()
    {
        int active = _items.Count(i => i.State == IncidentState.Active);
        int risk = _items.Count(i => i.State is IncidentState.Warning or IncidentState.Caution && !i.IsOperational);
        var top = _items.FirstOrDefault(i => !i.IsOperational);

        var severity = active > 0 ? AlertSeverity.Critical
            : _items.Any(i => i.State == IncidentState.Warning) ? AlertSeverity.Warning
            : AlertSeverity.Notice;

        string statusWord = active > 0 ? "사고"
            : _items.Any(i => i.State == IncidentState.Warning) ? "경고"
            : risk > 0 || _items.Count > 0 ? "주의" : "정상";

        string body = top == null
            ? "특이사항 없음"
            : $"최우선\n{RoomName(top.RoomId)} / {top.Title}"
              + (top.WarningRemainingSeconds > 0.05f ? $"\n사고까지 {Clock(top.WarningRemainingSeconds)}"
                  : top.WarningRemainingSeconds >= 0f ? "\n사고까지 발생 대기" : "");

        Show("경고 단말기", statusWord, statusWord == "정상" ? "정상 가동 중" : $"시설 상태 : {statusWord}",
            $"활성 사고 {active}   ·   사고 위험 {risk}", body, SeverityColor(severity), severity);
    }

    private void DrawCard(IncidentDisplayData d)
    {
        string stateWord = d.IsOperational ? "운영 상태"
            : d.IsProtocol ? "금기 위험"
            : d.State switch
            {
                IncidentState.Active => "사고 발생",
                IncidentState.Warning => "사고 임박",
                IncidentState.Caution => "주의",
                _ => "해결됨",
            };

        // 본문은 세 줄까지만. 남은 시간은 부제로 올려 크게 보이게 한다.
        var lines = new List<string> { $"원인 · {d.CauseText}" };
        if (d.ConsequenceLines.Count > 0)
            lines.Add((d.State == IncidentState.Active ? "결과 · " : "예상 · ")
                      + string.Join(" · ", d.ConsequenceLines.Take(2))
                      + (d.ConsequenceLines.Count > 2 ? $" 외 {d.ConsequenceLines.Count - 2}건" : ""));
        string action = d.ActionHint;
        if (d.State == IncidentState.Active && d.RepairWorkers > 1)
            action += $" · {d.RepairWorkers}명";
        if (!string.IsNullOrEmpty(action)) lines.Add($"조치 · {action}");

        string sub = RoomName(d.RoomId);
        if (d.WarningRemainingSeconds > 0.05f) sub += $"   사고까지 {Clock(d.WarningRemainingSeconds)}";
        else if (d.WarningRemainingSeconds >= 0f) sub += "   발생 대기";

        var severity = d.IsOperational ? AlertSeverity.Notice : d.Severity;
        Show(InFailureFlash && d.State == IncidentState.Active ? "⚠ 사고 발생" : "경고 단말기",
            stateWord, d.Title, sub, string.Join("\n", lines),
            SeverityColor(severity), severity);
    }

    private void DrawRecent(IReadOnlyList<IncidentDisplayData> recent)
    {
        string body = recent.Count == 0
            ? "기록 없음"
            : string.Join("\n", recent.Take(3).Select(r =>
                $"{Clock24(r.StartedAt)} {RoomName(r.RoomId)} · {r.Title} → {Clock24(r.ResolvedAt)} 해결"));
        Show("경고 단말기", "기록", "최근 해결 기록", $"{recent.Count}건", body, Dim, AlertSeverity.Notice);
    }

    // --- 그리기 ---------------------------------------------------------

    private void Show(string head, string status, string title, string sub, string body,
        Color color, AlertSeverity severity)
    {
        CurrentSeverity = severity;
        _head.Text = head;
        _head.AddThemeColorOverride("font_color", head.StartsWith("⚠") ? Crit : Dim);
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", color);
        _title.Text = title;
        _title.AddThemeColorOverride("font_color", color);
        _sub.Text = sub;
        _body.Text = body;
    }

    private void SetNav(int pageCount)
    {
        _pageCount = Mathf.Max(1, pageCount);
        if (_pageIndex >= _pageCount) _pageIndex = 0;
        bool many = _pageCount > 1;
        _prev.Visible = many;
        _next.Visible = many;
        _page.Text = many ? $"{_pageIndex + 1} / {_pageCount}" : "";
    }

    private static Color SeverityColor(AlertSeverity s) => s switch
    {
        AlertSeverity.Critical => Crit,
        AlertSeverity.Warning => Caution,
        _ => Ok,
    };

    // 상태별 경고음 — 사고가 계속 크게 울려 피곤해지지 않도록 간격을 벌린다.
    private void TickBeep(double now)
    {
        if (CurrentSeverity != _lastSeverity)
        {
            _lastSeverity = CurrentSeverity;
            if (CurrentSeverity == AlertSeverity.Warning) Sfx.Instance?.Play("sensor_beep", -12f, 0.95f);
            _nextBeepAt = now + 1.0;
            return;
        }
        if (CurrentSeverity == AlertSeverity.Notice) return;
        if (now < _nextBeepAt) return;
        _nextBeepAt = now + (CurrentSeverity == AlertSeverity.Critical ? 1.6 : 3.2);
        Sfx.Instance?.Play("sensor_beep", CurrentSeverity == AlertSeverity.Critical ? -8f : -13f,
            CurrentSeverity == AlertSeverity.Critical ? 1.1f : 0.9f);
    }

    // 사망 발생만 로그에서 듣는다(경광등 소등용).
    private void WireLog()
    {
        if (_logWired || EventLog.Instance == null) return;
        _logWired = true;
        EventLog.Instance.EntryLogged += OnLogged;
    }

    public override void _ExitTree()
    {
        if (_logWired && EventLog.Instance != null) EventLog.Instance.EntryLogged -= OnLogged;
    }

    private void OnLogged()
    {
        var e = EventLog.Instance?.GetAllEntries().LastOrDefault();
        if (e?.EventType == LogEventType.Death) DeathSeen = true;
    }

    private static string RoomName(string roomId) =>
        string.IsNullOrEmpty(roomId) ? "시설 전체"
            : FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:00}:{t % 60:00}";
    }

    // 근무 시계(0초 = 22:00)를 실제 시각 표기로.
    private static string Clock24(float seconds)
    {
        float length = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int total = 22 * 60 + Mathf.FloorToInt(Mathf.Max(0f, seconds) * (360f / Mathf.Max(1f, length)));
        return $"{(total / 60) % 24:00}:{total % 60:00}";
    }
}
