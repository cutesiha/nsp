using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

// EventLog가 이미 쏘고 있는 신호 하나만 구독해서 방 위에 짧은 팝업을 띄운다 — 새 판정 로직
// 없음, 이미 결정된 사건을 화면에 노출만 한다. RoomEnter/RoomExit/TaskStart 같이 잦은
// 이벤트는 허용 목록에서 빠져있어 도배되지 않는다(메인 화면=즉시 인지, 상세는 LogPanel).
public partial class FloatingPopupLayer : Control
{
    public static FloatingPopupLayer Instance { get; private set; }

    // SFX 연결용 placeholder — 에셋 없으면 null로 두면 자동 스킵.
    [Export] public AudioStream WarningCue;
    [Export] public AudioStream FailureCue;

    private const int MaxConcurrent = 12;
    private const float LifetimeSeconds = 1.5f;
    private const float RiseDistance = 40f;

    private static readonly HashSet<LogEventType> AllowList = new()
    {
        LogEventType.TaskComplete,
        LogEventType.TabooViolation,
        LogEventType.Neglect,
        LogEventType.Sabotage,
        LogEventType.PowerOutage,
        LogEventType.CctvDisconnect,
    };

    // TaskComplete는 EventLog에 이미 짧은 배지 문구로 남기 때문에(FacilitySimulation.
    // ApplyTaskEffect) 여기선 나머지 타입만 짧은 배지로 매핑한다 — 상세 문장은 LogPanel에만.
    private static readonly Dictionary<LogEventType, string> Badges = new()
    {
        [LogEventType.TabooViolation] = "❗ 금기 위반",
        [LogEventType.Neglect] = "⚠ 방치 사고",
        [LogEventType.Sabotage] = "🚨 이상 발생",
        [LogEventType.PowerOutage] = "⚡ 전력 이상",
        [LogEventType.CctvDisconnect] = "📹 CCTV 끊김",
    };

    private static readonly HashSet<LogEventType> SevereTypes = new()
    {
        LogEventType.Sabotage, LogEventType.PowerOutage, LogEventType.CctvDisconnect,
    };

    private readonly List<Label> _active = new();
    private AudioStreamPlayer _sfx;

    public override void _Ready()
    {
        Instance = this;
        MouseFilter = MouseFilterEnum.Ignore;

        _sfx = new AudioStreamPlayer();
        AddChild(_sfx);

        EventLog.Instance.EntryLogged += OnEntryLogged;
    }

    private void OnEntryLogged()
    {
        var entry = EventLog.Instance.GetAllEntries().LastOrDefault();
        if (entry == null || !AllowList.Contains(entry.EventType)) return;

        string text = entry.EventType == LogEventType.TaskComplete
            ? entry.Description
            : Badges.GetValueOrDefault(entry.EventType, entry.Description);

        Vector2 pos = FacilitySimulation.Instance?.GetRoomVisualPosition(entry.RoomId) ?? Vector2.Zero;
        Spawn(pos, text);

        if (SevereTypes.Contains(entry.EventType))
            PlayCue(FailureCue);
        else if (entry.EventType is LogEventType.TabooViolation or LogEventType.Neglect)
            PlayCue(WarningCue);
    }

    public void Spawn(Vector2 worldPos, string text)
    {
        if (_active.Count >= MaxConcurrent)
        {
            var oldest = _active[0];
            _active.RemoveAt(0);
            oldest.QueueFree();
        }

        var label = new Label
        {
            Text = text,
            Position = worldPos + new Vector2(-60f, -20f),
            Size = new Vector2(120f, 20f),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 5);
        AddChild(label);
        _active.Add(label);

        var tween = CreateTween();
        tween.TweenProperty(label, "position:y", label.Position.Y - RiseDistance, LifetimeSeconds);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, LifetimeSeconds);
        tween.TweenCallback(Callable.From(() =>
        {
            _active.Remove(label);
            label.QueueFree();
        }));
    }

    private void PlayCue(AudioStream stream)
    {
        if (stream == null || _sfx == null) return;
        _sfx.Stream = stream;
        _sfx.Play();
    }
}
