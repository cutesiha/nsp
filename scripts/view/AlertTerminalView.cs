using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 책상 위 소형 경고 단말기(SENSOR 전용 화면)의 실제 내용. 판정은 전혀 하지 않는다 —
// AlertSystem이 계산한 목록과 GameState/EventLog를 읽기만 한다.
//   SENSOR ON  : AlertSystem이 계산한 가장 급한 예고를 그대로 보여준다(카운트다운 포함).
//   SENSOR OFF : 평소엔 "SENSOR OFFLINE"만 보이지만, 실제 사고 로그가 찍히는 순간만은
//                최소한의 사후 경보를 잠깐 띄운다(정보 없이 억울하게 당하지 않도록).
// CurrentSeverity/InFailureFlash는 AlertTerminalProp이 경고등 색·점멸 속도를 정하는 데 쓴다.
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
    private string _failureRoom = "";
    private double _nextBeepAt;

    public AlertSeverity CurrentSeverity { get; private set; } = AlertSeverity.Notice;
    public bool InFailureFlash { get; private set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var bg = new ColorRect { Color = new Color(0.015f, 0.02f, 0.015f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var font = ViewFont.Default;
        var head = Lbl("ALERT TERMINAL", 12, new Color(0.35f, 0.55f, 0.42f), font);
        head.Position = new Vector2(6, 4);
        AddChild(head);

        _line1 = Lbl("SYSTEM NORMAL", 17, new Color(0.4f, 0.95f, 0.5f), font);
        _line1.Position = new Vector2(6, 26);
        AddChild(_line1);

        _line2 = Lbl("NO ACTIVE ALERTS", 13, new Color(0.35f, 0.7f, 0.42f), font);
        _line2.Position = new Vector2(6, 52);
        AddChild(_line2);

        _line3 = Lbl("", 15, new Color(0.9f, 0.6f, 0.2f), font);
        _line3.Position = new Vector2(6, 78);
        AddChild(_line3);
    }

    private static Label Lbl(string t, int size, Color c, Font font)
    {
        var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public override void _Process(double delta)
    {
        WireLog();
        double now = Time.GetTicksMsec() / 1000.0;
        InFailureFlash = now < _failureFlashUntil;

        if (InFailureFlash)
        {
            CurrentSeverity = AlertSeverity.Critical;
            Show3("⚠ FACILITY FAILURE", _failureRoom, "", new Color(1f, 0.35f, 0.28f));
            return;
        }

        bool sensorOn = GameState.Instance?.IsConsumerPowered(PowerConsumer.Sensor) ?? false;
        if (!sensorOn)
        {
            CurrentSeverity = AlertSeverity.Notice;
            Show3("SENSOR OFFLINE", "", "", new Color(0.55f, 0.55f, 0.55f));
            return;
        }

        var alerts = AlertSystem.Instance?.GetActiveAlerts() ?? new List<Alert>();
        if (alerts.Count == 0)
        {
            CurrentSeverity = AlertSeverity.Notice;
            Show3("SYSTEM NORMAL", "NO ACTIVE ALERTS", "", new Color(0.4f, 0.95f, 0.5f));
            return;
        }

        var a = alerts[0];
        CurrentSeverity = a.Severity;
        string cd = a.TimeRemaining >= 0f ? Clock(a.TimeRemaining) : "";
        Color col = a.Severity == AlertSeverity.Critical ? new Color(1f, 0.4f, 0.2f) : new Color(0.95f, 0.8f, 0.25f);
        Show3($"⚠ {a.Headline}", a.Detail, cd, col);

        TickBeep(now, a);
    }

    private void TickBeep(double now, Alert a)
    {
        if (now < _nextBeepAt) return;
        float interval = a.Severity == AlertSeverity.Critical ? 0.9f : 2.4f;
        _nextBeepAt = now + interval;
        Sfx.Instance?.Play("tick", -10f, a.Severity == AlertSeverity.Critical ? 1.15f : 0.95f);
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
        if (!FailureTypes.Contains(last.EventType)) return;

        _failureRoom = FacilitySimulation.Instance?.GetRoomDef(last.RoomId)?.DisplayName ?? "";
        _failureFlashUntil = Time.GetTicksMsec() / 1000.0 + 6.0;
    }

    public override void _ExitTree()
    {
        if (_logWired && EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnLog;
    }

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:0}:{t % 60:00}";
    }
}
