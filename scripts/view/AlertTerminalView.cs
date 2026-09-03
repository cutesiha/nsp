using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 책상 위 경고 단말기(SENSOR 전용 화면)의 실제 내용. 판정은 전혀 하지 않는다 —
// AlertSystem이 계산한 목록과 GameState/EventLog를 읽기만 한다. 문구는 전부 한글.
//   SENSOR ON  : AlertSystem이 계산한 가장 급한 예고를 그대로 보여준다(카운트다운 포함).
//   SENSOR OFF : 평소엔 "센서 전원 차단"만, 실제 사고 로그 순간만 최소 사후 경보를 잠깐 띄운다.
// CurrentSeverity / InFailureFlash / DeathSeen 는 AlertTerminalProp 의 회전 경광등이 색·속도를 정하는 데 쓴다.
// [Tool] — 에디터 뷰포트에서도 화면이 보이게 한다(에디터엔 autoload가 없어 "센서 전원 차단").
[Tool]
public partial class AlertTerminalView : Control
{
    private static readonly HashSet<LogEventType> FailureTypes = new()
    {
        LogEventType.TaskFailed, LogEventType.PowerOutage, LogEventType.Sabotage,
        LogEventType.Death, LogEventType.CctvDisconnect,
    };

    private Label _line1, _line2, _line3;
    private bool _logWired;
    private double _failureFlashUntil = -1;
    private string _failureHeadline = "";
    private string _failureRoom = "";
    private double _nextBeepAt;
    private bool _wasLive;

    public AlertSeverity CurrentSeverity { get; private set; } = AlertSeverity.Notice;
    public bool InFailureFlash { get; private set; }
    // 이번 근무에 사망자가 나왔는가 — 경광등이 검정(소등)으로 바뀐다.
    public bool DeathSeen { get; private set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.015f, 0.02f, 0.015f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // 책상 위 작은 단말기라 멀리서도 읽혀야 한다 — 글자를 크게 잡는다.
        // 폭은 반드시 명시한다(컨테이너에 맡기면 폭이 0으로 잡혀 한 글자씩 세로로 접힌다).
        var font = ViewFont.Default;
        const float x = 14f, w = CanvasW - x * 2f;

        var head = Lbl("경고 단말기", 21, new Color(0.4f, 0.6f, 0.46f), font, new Vector2(x, 6), w, 26);
        AddChild(head);

        _line1 = Lbl("정상 가동 중", 46, new Color(0.4f, 0.95f, 0.5f), font, new Vector2(x, 36), w, 112);
        AddChild(_line1);

        _line2 = Lbl("경고 없음", 34, new Color(0.4f, 0.75f, 0.48f), font, new Vector2(x, 152), w, 78);
        AddChild(_line2);

        _line3 = Lbl("", 34, new Color(0.95f, 0.65f, 0.25f), font, new Vector2(x, 234), w, 60);
        AddChild(_line3);
    }

    // AlertTerminalProp 이 이 뷰를 384x300 논리 캔버스에 올린다.
    private const float CanvasW = 384f;

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
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public override void _Process(double delta)
    {
        WireLog();

        // 근무가 새로 시작되면 사망 상태를 초기화한다.
        bool live = GameState.Instance?.CurrentPhase == GamePhase.Live;
        if (live && !_wasLive) DeathSeen = false;
        _wasLive = live;

        double now = Time.GetTicksMsec() / 1000.0;
        InFailureFlash = now < _failureFlashUntil;

        if (InFailureFlash)
        {
            CurrentSeverity = AlertSeverity.Critical;
            Show3(_failureHeadline, _failureRoom, "", new Color(1f, 0.35f, 0.28f));
            return;
        }

        bool sensorOn = GameState.Instance?.IsConsumerPowered(PowerConsumer.Sensor) ?? false;
        if (!sensorOn)
        {
            CurrentSeverity = AlertSeverity.Notice;
            Show3("센서 전원 차단", "사고 예측 불가", "", new Color(0.55f, 0.55f, 0.55f));
            return;
        }

        var alerts = AlertSystem.Instance?.GetActiveAlerts() ?? new List<Alert>();
        if (alerts.Count == 0)
        {
            CurrentSeverity = AlertSeverity.Notice;
            Show3("정상 가동 중", "경고 없음", "", new Color(0.4f, 0.95f, 0.5f));
            return;
        }

        var a = alerts[0];
        CurrentSeverity = a.Severity;
        Color col = a.Severity == AlertSeverity.Critical ? new Color(1f, 0.4f, 0.2f) : new Color(0.95f, 0.8f, 0.25f);
        Show3($"⚠ {a.Headline}", a.SubLabel, a.Countdown, col);

        TickBeep(now, a);
    }

    private void TickBeep(double now, Alert a)
    {
        if (now < _nextBeepAt) return;
        float interval = a.Severity == AlertSeverity.Critical ? 0.8f : 2.4f;
        _nextBeepAt = now + interval;
        Sfx.Instance?.Play("sensor_beep", -9f, a.Severity == AlertSeverity.Critical ? 1.1f : 0.9f);
    }

    private void Show3(string l1, string l2, string l3, Color color)
    {
        _line1.Text = l1;
        _line1.AddThemeColorOverride("font_color", color);
        _line2.Text = l2;
        _line3.Text = l3;
    }

    private void WireLog()
    {
        if (_logWired || EventLog.Instance == null) return;
        EventLog.Instance.EntryLogged += OnLog;
        _logWired = true;
    }

    private void OnLog()
    {
        var entries = EventLog.Instance.GetAllEntries();
        if (entries.Count == 0) return;
        var last = entries[^1];

        if (last.EventType == LogEventType.Death) DeathSeen = true;
        if (!FailureTypes.Contains(last.EventType)) return;

        _failureHeadline = last.EventType == LogEventType.Death ? "⚠ 사망자 발생" : "⚠ 시설 고장 발생";
        _failureRoom = FacilitySimulation.Instance?.GetRoomDef(last.RoomId)?.DisplayName ?? "";
        _failureFlashUntil = Time.GetTicksMsec() / 1000.0 + 6.0;
    }

    public override void _ExitTree()
    {
        if (_logWired && EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnLog;
    }
}
