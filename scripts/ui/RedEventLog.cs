using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

// 메인 화면 오른쪽 아래(갈색 책상 빈 공간)에, 큼지막한 붉은 글씨로 항상 떠 있는 사건 패널.
// 노이즈/비네트 위에 확실히 보이도록 자체 CanvasLayer(layer 110)로 띄운다.
//
//  ▮ 이상 현황
//   ▶ 금기 위반 발생            ← 순간 알림, 8초 뒤 사라짐
//   ⚡ 최대 전력 감소 — 발전기 점검 필요   ← 미해결 상태. 해결(발전기 점검 완료)될 때까지 안 사라짐
//   📹 CCTV 전력 차단
//   🔒 정비실 봉쇄 중            ← 봉쇄 해제하면 사라짐
//   ⏳ 발전실 사고까지 8초        ← 카운트다운. 완료/실패 시 사라짐
public partial class RedEventLog : CanvasLayer
{
    private const int MaxFlash = 5;
    private const double FlashTtlSeconds = 8.0;
    private const float CountdownShowUnder = 12f;

    private static readonly Dictionary<LogEventType, string> FlashMsg = new()
    {
        [LogEventType.TabooViolation] = "금기 위반 발생",
        [LogEventType.Death] = "직원 활동 중단",
        [LogEventType.Sabotage] = "설비 파손 감지",
    };

    private VBoxContainer _flash;
    private VBoxContainer _issues;
    private VBoxContainer _countdowns;
    private Label _idleLabel;

    private readonly List<(Label Label, double DieAt)> _flashEntries = new();
    private readonly Dictionary<string, Label> _issueLabels = new();
    private readonly Dictionary<string, Label> _cdLabels = new();

    public override void _Ready()
    {
        Layer = 110;

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AnchorLeft = 1; panel.AnchorTop = 1; panel.AnchorRight = 1; panel.AnchorBottom = 1;
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.OffsetLeft = -18; panel.OffsetTop = -18; panel.OffsetRight = -18; panel.OffsetBottom = -18;
        panel.AddThemeStyleboxOverride("panel", BgStyle());
        AddChild(panel);

        var vbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.CustomMinimumSize = new Vector2(370f, 0f);
        panel.AddChild(vbox);

        var header = MakeLabel("▮ 이상 현황", new Color(0.95f, 0.55f, 0.5f), 20);
        header.HorizontalAlignment = HorizontalAlignment.Right;
        vbox.AddChild(header);

        _flash = MakeColumn();
        _issues = MakeColumn();
        _countdowns = MakeColumn();
        vbox.AddChild(_flash);
        vbox.AddChild(_issues);
        vbox.AddChild(_countdowns);

        _idleLabel = MakeLabel("· 정상 운영 중", new Color(0.7f, 0.7f, 0.7f), 20);
        _idleLabel.HorizontalAlignment = HorizontalAlignment.Right;
        vbox.AddChild(_idleLabel);

        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged += OnEntry;
    }

    public override void _ExitTree()
    {
        if (EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntry;
    }

    private static StyleBoxFlat BgStyle() => new()
    {
        BgColor = new Color(0.06f, 0.02f, 0.02f, 0.62f),
        BorderColor = new Color(0.7f, 0.12f, 0.12f, 0.9f),
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 14, ContentMarginTop = 10, ContentMarginRight = 14, ContentMarginBottom = 10,
    };

    private static VBoxContainer MakeColumn()
    {
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 4);
        return v;
    }

    private static Label MakeLabel(string text, Color color, int size = 25)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }

    private void OnEntry()
    {
        var e = EventLog.Instance.GetAllEntries().LastOrDefault();
        if (e == null || !FlashMsg.TryGetValue(e.EventType, out var msg)) return;

        var lbl = MakeLabel($"▶ {msg}", new Color(1f, 0.15f, 0.15f));
        _flash.AddChild(lbl);
        _flashEntries.Add((lbl, Now() + FlashTtlSeconds));
        while (_flashEntries.Count > MaxFlash)
        {
            var oldest = _flashEntries[0];
            _flashEntries.RemoveAt(0);
            if (IsInstanceValid(oldest.Label)) oldest.Label.QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        TickFlash();

        var sim = FacilitySimulation.Instance;
        bool liveState = sim != null && GameState.Instance?.CurrentPhase == GamePhase.Live;

        SyncSet(_issueLabels, _issues,
            liveState ? ScanStandingIssues(sim) : Enumerable.Empty<(string, string, Color)>());
        SyncCountdowns(liveState ? sim : null);

        if (_idleLabel != null)
            _idleLabel.Visible = _flashEntries.Count == 0 && _issueLabels.Count == 0 && _cdLabels.Count == 0;
    }

    private void TickFlash()
    {
        double now = Now();
        for (int i = _flashEntries.Count - 1; i >= 0; i--)
        {
            var (label, dieAt) = _flashEntries[i];
            if (!IsInstanceValid(label)) { _flashEntries.RemoveAt(i); continue; }
            double left = dieAt - now;
            if (left <= 0) { label.QueueFree(); _flashEntries.RemoveAt(i); continue; }
            label.Modulate = new Color(1f, 1f, 1f, left < 1.2 ? (float)(left / 1.2) : 1f);
        }
    }

    // 지금 이 순간 미해결인 사고들. 조건이 해소되면 다음 프레임에 라벨도 사라진다.
    private static IEnumerable<(string Key, string Text, Color Color)> ScanStandingIssues(FacilitySimulation sim)
    {
        var gs = GameState.Instance;
        var warn = new Color(1f, 0.3f, 0.16f);

        if (gs.IsPowerAccidentActive())
            yield return ("pwr_cap", "⚡ 최대 전력 감소 — 발전기 점검 필요", warn);
        if (!gs.IsConsumerPowered(PowerConsumer.CctvWatch))
            yield return ("pwr_cctv", "📹 CCTV 전력 차단", warn);
        if (!gs.IsConsumerPowered(PowerConsumer.Lighting))
            yield return ("pwr_light", "💡 조명 전력 차단", warn);

        foreach (var rid in sim.GetRoomIds())
        {
            var rd = sim.GetRoomDef(rid);
            var rs = sim.GetRoomState(rid);
            if (rd == null || rs == null || rd.IsRestricted) continue;
            string name = rd.DisplayName;

            if (rs.Locked)
                yield return ($"lock:{rid}", $"🔒 {name} 봉쇄 중", warn);
            if (!rs.PowerOn)
                yield return ($"pout:{rid}", $"⚡ {name} 정전", warn);
            if (rs.CctvDisconnected)
                yield return ($"ccut:{rid}", $"📹 {name} CCTV 단절", warn);

            var st = sim.GetPrimarySpawnedTask(rid);
            if (st is { Status: SpawnedTaskStatus.Failed })
            {
                string taskName = sim.GetTaskDef(st.TaskId)?.DisplayName ?? st.TaskId;
                yield return ($"fail:{rid}", $"🚨 {name} '{taskName}' 처리 실패", new Color(1f, 0.15f, 0.15f));
            }
        }
    }

    private void SyncSet(Dictionary<string, Label> labels, VBoxContainer parent,
        IEnumerable<(string Key, string Text, Color Color)> want)
    {
        var wanted = new Dictionary<string, (string Text, Color Color)>();
        foreach (var (key, text, color) in want)
            wanted[key] = (text, color);

        foreach (var key in labels.Keys.ToList())
        {
            if (wanted.ContainsKey(key)) continue;
            if (IsInstanceValid(labels[key])) labels[key].QueueFree();
            labels.Remove(key);
        }

        foreach (var (key, v) in wanted)
        {
            if (!labels.TryGetValue(key, out var lbl))
            {
                lbl = MakeLabel(v.Text, v.Color);
                parent.AddChild(lbl);
                labels[key] = lbl;
            }
            lbl.Text = v.Text;
            lbl.AddThemeColorOverride("font_color", v.Color);
            lbl.Modulate = Colors.White;
        }
    }

    private void SyncCountdowns(FacilitySimulation sim)
    {
        if (sim == null) { ClearDict(_cdLabels); return; }

        var live = new HashSet<string>();
        double now = Now();

        foreach (var rid in sim.GetRoomIds())
        foreach (var st in sim.GetActiveTasksForRoom(rid))
        {
            if (st.Status != SpawnedTaskStatus.Active || st.Recurring) continue;
            if (st.Remaining > CountdownShowUnder || st.Ratio >= 0.92f) continue;

            live.Add(st.TaskId);
            string room = sim.GetRoomDef(st.RoomId)?.DisplayName ?? st.RoomId;
            int secs = Mathf.CeilToInt(st.Remaining);
            string txt = $"⏳ {room} 사고까지 {secs}초";

            if (!_cdLabels.TryGetValue(st.TaskId, out var lbl))
            {
                lbl = MakeLabel(txt, new Color(1f, 0.5f, 0.14f));
                _countdowns.AddChild(lbl);
                _cdLabels[st.TaskId] = lbl;
            }
            lbl.Text = txt;
            float u = Mathf.Clamp(st.Remaining / CountdownShowUnder, 0f, 1f);
            lbl.AddThemeColorOverride("font_color", new Color(1f, 0.12f + 0.4f * u, 0.10f));
            lbl.Modulate = st.Remaining < 5f
                ? new Color(1f, 1f, 1f, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin((float)(now * 8.0))))
                : Colors.White;
        }

        foreach (var key in _cdLabels.Keys.ToList())
        {
            if (live.Contains(key)) continue;
            if (IsInstanceValid(_cdLabels[key])) _cdLabels[key].QueueFree();
            _cdLabels.Remove(key);
        }
    }

    private static void ClearDict(Dictionary<string, Label> d)
    {
        foreach (var l in d.Values)
            if (IsInstanceValid(l)) l.QueueFree();
        d.Clear();
    }

    private static double Now() => Time.GetTicksMsec() / 1000.0;
}
