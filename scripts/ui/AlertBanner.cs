using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Ui;

// 코어 복구율 게이지 바로 아래에, 사건이 터지면 아주 큰 붉은 글씨로 "OOO 발생"을 띄우고
// 반짝반짝하다가 ~3초 뒤 사라진다. 판정은 안 하고 EventLog만 읽는다.
public partial class AlertBanner : Label
{
    private static readonly Dictionary<LogEventType, string> Messages = new()
    {
        [LogEventType.TabooViolation] = "금기 위반 발생",
        [LogEventType.Sabotage] = "설비 파손 발생",
        [LogEventType.PowerOutage] = "전력 계통 이상 발생",
        [LogEventType.CctvDisconnect] = "감시 신호 두절",
        [LogEventType.TaskFailed] = "작업 처리 실패",
        [LogEventType.Death] = "직원 활동 중단",
    };

    private Tween _tween;

    public override void _Ready()
    {
        Modulate = new Color(1f, 1f, 1f, 0f);
        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged += OnEntryLogged;
    }

    public override void _ExitTree()
    {
        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntryLogged;
    }

    private void OnEntryLogged()
    {
        var e = EventLog.Instance.GetAllEntries().LastOrDefault();
        if (e == null || !Messages.TryGetValue(e.EventType, out var msg)) return;
        Sfx.Instance?.PlayScaryWarning(-5f);
        Flash($"⚠ {msg} ⚠");
    }

    public void Flash(string message)
    {
        Text = message;
        _tween?.Kill();
        _tween = CreateTween();

        // 3초간 5회 깜빡 → 사라짐
        Modulate = new Color(1f, 1f, 1f, 1f);
        for (int i = 0; i < 5; i++)
        {
            _tween.TweenProperty(this, "modulate:a", 0.25f, 0.16);
            _tween.TweenProperty(this, "modulate:a", 1.0f, 0.16);
        }
        _tween.TweenInterval(0.4);
        _tween.TweenProperty(this, "modulate:a", 0f, 0.4);
        _tween.TweenCallback(Callable.From(() => Text = ""));
    }
}
