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

    // 사건 유형별 효과음(Sfx 전역 재생기). 에셋이 아직 임포트 안 됐으면 조용히 스킵된다.
    private static readonly Dictionary<LogEventType, (string Key, float Db)> Cues = new()
    {
        [LogEventType.TaskSpawned] = ("task_spawn", -3f),
        [LogEventType.TaskComplete] = ("task_done", -7f),
        [LogEventType.Neglect] = ("task_fail", -9f),
        [LogEventType.TaskFailed] = ("task_fail", -1f),
        [LogEventType.Sabotage] = ("task_fail", -1f),
        [LogEventType.PowerOutage] = ("power_down", 0f),
        [LogEventType.CctvDisconnect] = ("cctv_cut", 0f),
        // TabooViolation은 HorrorDirector가 '치직·펑'(taboo_break)으로 처리 → 여기선 소리 안 냄.
    };

    private const int MaxConcurrent = 12;
    private const float LifetimeSeconds = 1.5f;
    private const float RiseDistance = 40f;

    private static readonly HashSet<LogEventType> AllowList = new()
    {
        LogEventType.TaskSpawned,
        LogEventType.TaskComplete,
        LogEventType.TabooViolation,
        LogEventType.Neglect,
        LogEventType.TaskFailed,
        LogEventType.Sabotage,
        LogEventType.PowerOutage,
        LogEventType.CctvDisconnect,
    };

    // TaskComplete는 EventLog에 이미 짧은 배지 문구로 남기 때문에(FacilitySimulation.
    // ApplyTaskEffect) 여기선 나머지 타입만 짧은 배지로 매핑한다 — 상세 문장은 LogPanel에만.
    private static readonly Dictionary<LogEventType, string> Badges = new()
    {
        [LogEventType.TabooViolation] = "❗ 금기 위반",
        [LogEventType.Neglect] = "⚠ 자리 이탈",
        [LogEventType.TaskFailed] = "🚨 작업 실패",
        [LogEventType.Sabotage] = "🚨 이상 발생",
        [LogEventType.PowerOutage] = "⚡ 전력 이상",
        [LogEventType.CctvDisconnect] = "📹 CCTV 끊김",
    };

    private readonly List<Label> _active = new();

    public override void _Ready()
    {
        Instance = this;
        MouseFilter = MouseFilterEnum.Ignore;

        EventLog.Instance.EntryLogged += OnEntryLogged;
    }

    private void OnEntryLogged()
    {
        var entry = EventLog.Instance.GetAllEntries().LastOrDefault();
        if (entry == null || !AllowList.Contains(entry.EventType)) return;

        string text;
        float lifetime = LifetimeSeconds;

        if (entry.EventType == LogEventType.TaskSpawned)
        {
            text = BuildTaskSpawnedText(entry.RoomId);
            lifetime = 2.8f;
        }
        else if (entry.EventType == LogEventType.TaskComplete)
        {
            text = entry.Description;
        }
        else
        {
            text = Badges.GetValueOrDefault(entry.EventType, entry.Description);
        }

        Vector2 pos = FacilitySimulation.Instance?.GetRoomVisualPosition(entry.RoomId) ?? Vector2.Zero;
        Spawn(pos, text, lifetime);

        if (Cues.TryGetValue(entry.EventType, out var cue))
            NSP.Core.Sfx.Instance?.Play(cue.Key, cue.Db);
    }

    // "⚠ 새 작업 발생 / 🔧 발전기 점검 / 0:20" 형태의 방 위 팝업.
    private static string BuildTaskSpawnedText(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var st = sim?.GetPrimarySpawnedTask(roomId);
        var task = st != null ? sim.GetTaskDef(st.TaskId) : null;
        if (task == null) return "⚠ 새 작업 발생";

        string icon = RoomStatusText.WorkIcon(sim.GetRoomDef(roomId)?.ManagedResource ?? RoomResourceType.None);
        if (st.Recurring)
            return $"⚙ 상시 업무\n{icon} {task.DisplayName}";

        int s = Mathf.CeilToInt(st.Remaining);
        return $"⚠ 새 작업 발생\n{icon} {task.DisplayName}\n{s / 60:0}:{s % 60:00}";
    }

    public void Spawn(Vector2 worldPos, string text, float lifetime = LifetimeSeconds)
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
            Position = worldPos + new Vector2(-120f, -40f),
            Size = new Vector2(240f, 24f),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 7);
        AddChild(label);
        _active.Add(label);

        var tween = CreateTween();
        tween.TweenProperty(label, "position:y", label.Position.Y - RiseDistance, lifetime);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, lifetime);
        tween.TweenCallback(Callable.From(() =>
        {
            _active.Remove(label);
            label.QueueFree();
        }));
    }
}
