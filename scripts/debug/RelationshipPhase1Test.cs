using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.Debug;

// 관계 시스템 Phase 1 — 시드 로드 · 배치 화면 거부/경고/아이콘 · 불편 동실 소프트 페널티 검증.
//
//   godot --headless --path . res://scenes/debug/RelationshipPhase1Test.tscn
//
// 배치 화면은 ScheduleConsoleTest 와 같이 실제 입력(끌어 놓기 · 마우스 이동 · 클릭)을 밀어 넣는다.
// 페널티는 시뮬레이션을 직접 돌려(AdvanceDayTime + Tick) 본다. 게임 흐름에서는 로드되지 않는다.
public partial class RelationshipPhase1Test : Node
{
    private const float Step = 0.25f;
    private FacilitySimulation _sim;
    private SubViewport _vp;
    private ScheduleMapView _map;
    private int _pass, _fail;
    private int _startPressed;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        _ = RunAll();
    }

    private async System.Threading.Tasks.Task RunAll()
    {
        GD.Print("################ 관계 시스템 Phase 1 검증 ################");
        Data();

        NewGame(1);
        _vp = new SubViewport
        {
            Size = new Vector2I(800, 600), HandleInputLocally = true, GuiDisableInput = false, Disable3D = true,
        };
        AddChild(_vp);
        _map = new ScheduleMapView();
        _vp.AddChild(_map);
        _map.StartPressed += () => _startPressed++;
        await Frames(3);

        await RefuseBlocksStart();
        await UneasyWarns();
        await FriendlyIcons();
        await DragPreview();

        _vp.QueueFree();
        Penalty();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 데이터 ───────────────────────────────────────────────────────────
    private void Data()
    {
        Head("A", "관계 시드 로드 (config RelationshipTablePath → RelationshipSystem.Load)");
        string path = Config.Instance.Data.RelationshipTablePath;
        Check(ResourceLoader.Exists(path), $"config 경로의 테이블이 있다 — {path}");
        Check(GD.Load<RelationshipTableDef>(path) != null, "RelationshipTableDef(scripts/data/RelationshipTableDef.cs)로 읽힌다");

        Check(RelationshipSystem.Affinity("cat", "fox") == -90 && RelationshipSystem.Affinity("fox", "cat") == -60,
            "방향성 감정: 고양이→여우 -90, 여우→고양이 -60");
        Check(RelationshipSystem.Band("cat", "fox") == PairBand.Refuse, "고양이–여우 = 거부(Refuse)");
        foreach (var (a, b) in new[] { ("cat", "sheep"), ("fox", "sheep"), ("fox", "wolf") })
            Check(RelationshipSystem.Band(a, b) == PairBand.Uneasy, $"{a}–{b} = 불편(Uneasy) ({RelationshipSystem.PairScore(a, b)})");
        Check(RelationshipSystem.Band("cat", "dog") == PairBand.Close, "고양이–강아지 = 밀접(Close)");
        Check(RelationshipSystem.Band("rabbit", "dog") == PairBand.Friendly, "토끼–강아지 = 우호(Friendly)");
        Check(RelationshipSystem.Band("dog", "fox") == PairBand.Neutral, "강아지–여우 = 중립(배치 제약 없음)");
        Check(RelationshipSystem.TypeOf("cat", "dog") == RelationType.Lover && RelationshipSystem.Fears("sheep", "wolf"),
            "관계 타입 · 플래그(연인, 양→늑대 fear)");

        // 판 간 초기화 — Phase 3 에서 움직인 값이 새 판에 남지 않는다.
        RelationshipSystem.Apply("cat", "fox", 100, true);
        _sim.ResetRun();
        Check(RelationshipSystem.Affinity("cat", "fox") == -90, "처음부터 다시 시작(ResetRun)하면 시드 값으로 돌아간다");

        int pairs = 0, refuse = 0, uneasy = 0;
        var ids = new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };
        for (int i = 0; i < ids.Length; i++)
            for (int j = i + 1; j < ids.Length; j++)
            {
                pairs++;
                var band = RelationshipSystem.Band(ids[i], ids[j]);
                if (band == PairBand.Refuse) refuse++;
                if (band == PairBand.Uneasy) uneasy++;
            }
        Check(pairs == 15 && refuse == 1 && uneasy == 3, $"15쌍 중 거부 {refuse} · 불편 {uneasy} (설계 문서 3.1: 1 · 3)");
    }

    // ── 배치 화면 ────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task RefuseBlocksStart()
    {
        Head("B", "동실 거부 — 근무 시작 차단 · 스파크 · 말풍선");
        await DragFromTo(_map.RosterCardOf("wolf").GetCenter(), _map.CellOf("core_room").GetCenter());
        await Frames(2);
        Check(_map.StartEnabled, "코어실에 늑대 → 근무 시작 활성");

        await DragFromTo(_map.RosterCardOf("cat").GetCenter(), _map.CellOf("power_room").GetCenter());
        await DragFromTo(_map.RosterCardOf("fox").GetCenter(), _map.CellOf("power_room").GetCenter());
        await Frames(2);
        Check(Assigned("cat") == "power_room" && Assigned("fox") == "power_room", "거부 쌍도 배치 자체는 된다(시작만 막힘)");
        var refused = ScheduleMapView.RefusedPairs(_sim);
        Check(refused.Count == 1 && refused[0].RoomId == "power_room", "발전실의 거부 쌍 1건을 찾는다");
        Check(!_map.StartEnabled, "거부 쌍이 있으면 근무 시작 비활성");
        await Click(_map.StartButtonRect.GetCenter());
        Check(_startPressed == 0, "비활성 상태에서 눌러도 시작하지 않는다");
        Check(ScheduleMapView.RoomBand(_sim, "power_room") == PairBand.Refuse, "발전실 아이콘 = 거부 스파크");
        string text = ScheduleMapView.PairText(_sim, "cat", "fox", PairBand.Refuse);
        Check(text == "고양이와 여우는 같은 방 근무를 거부합니다", $"안내 문구 — \"{text}\"");

        var ca = _map.ChipOf("cat"); var cf = _map.ChipOf("fox");
        Check(ca.Size != Vector2.Zero && cf.Size != Vector2.Zero, "두 칩이 방 칸 안에 그려진다(스파크 자리)");
        await Hover(_map.RelationSlotOf("power_room").GetCenter());
        var tip = _map.TooltipTexts();
        Check(tip.Contains(text), "관계 아이콘에 마우스 → 거부 말풍선");
        var mid = new Vector2((ca.GetCenter().X + cf.GetCenter().X) * 0.5f, Mathf.Min(ca.Position.Y, cf.Position.Y) - 7f);
        await Hover(mid);
        Check(_map.TooltipTexts().Contains(text), "두 칩 사이 스파크에 마우스 → 거부 말풍선");
        await Hover(new Vector2(700f, 560f));
        Check(_map.TooltipTexts().Count == 0, "아무것도 없는 곳 → 말풍선 없음");

        // 여우를 저장고로 옮기면 풀린다.
        await DragFromTo(_map.ChipOf("fox").GetCenter(), _map.CellOf("storage_room").GetCenter());
        await Frames(2);
        Check(ScheduleMapView.RefusedPairs(_sim).Count == 0 && _map.StartEnabled, "거부 쌍을 떼어 놓으면 다시 시작 가능");
        await Click(_map.StartButtonRect.GetCenter());
        Check(_startPressed == 1, "근무 시작 버튼 → StartPressed 한 번");
    }

    private async System.Threading.Tasks.Task UneasyWarns()
    {
        Head("C", "불편 — 주황 경고, 배치·시작은 허용");
        await DragFromTo(_map.RosterCardOf("sheep").GetCenter(), _map.CellOf("storage_room").GetCenter());
        await Frames(2);
        Check(ScheduleMapView.RoomBand(_sim, "storage_room") == PairBand.Uneasy, "저장고(여우+양) 아이콘 = 불편 경고");
        Check(_map.StartEnabled, "불편 쌍은 근무 시작을 막지 않는다");
        await Hover(_map.RelationSlotOf("storage_room").GetCenter());
        var tip = _map.TooltipTexts();
        Check(tip.Any(t => t.StartsWith("여우와 양은 사이가 불편합니다")), "불편 말풍선 — " + string.Join(" / ", tip));
    }

    private async System.Threading.Tasks.Task FriendlyIcons()
    {
        Head("D", "밀접/우호 아이콘");
        await DragFromTo(_map.RosterCardOf("dog").GetCenter(), _map.CellOf("power_room").GetCenter());
        await Frames(2);
        Check(ScheduleMapView.RoomBand(_sim, "power_room") == PairBand.Close, "발전실(고양이+강아지) = 밀접 하트");
        await DragFromTo(_map.RosterCardOf("rabbit").GetCenter(), _map.CellOf("core_room").GetCenter());
        await Frames(2);
        Check(ScheduleMapView.RoomBand(_sim, "core_room") == PairBand.Friendly, "코어실(늑대+토끼) = 우호 고리");
        Check(_map.StartEnabled, "밀접/우호는 시작에 영향 없음");
    }

    private async System.Threading.Tasks.Task DragPreview()
    {
        Head("E", "끌고 있는 동안 — 놓을 방의 관계를 미리 보여 준다");
        var from = _map.ChipOf("cat").GetCenter();
        var to = _map.CellOf("storage_room").GetCenter();
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
        await Frames(1);
        for (int i = 1; i <= 6; i++)
        {
            var p = from.Lerp(to, i / 6f);
            Push(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left });
        }
        await Frames(2);
        var tip = _map.TooltipTexts();
        Check(tip.Contains("고양이와 여우는 같은 방 근무를 거부합니다") && tip.Any(t => t.StartsWith("고양이와 양은 사이가 불편합니다")),
            "고양이를 저장고(여우·양) 위로 끌면 거부 · 불편 미리보기 — " + string.Join(" / ", tip));
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _map.RosterArea.GetCenter(), GlobalPosition = _map.RosterArea.GetCenter() });
        await Frames(2);
    }

    // ── 근무 중 소프트 페널티 ────────────────────────────────────────────
    private void Penalty()
    {
        Head("F", "불편 동실 소프트 페널티 (RoomStaffing · FacilitySimulation)");
        var cfg = Config.Instance.Data;
        float savedChance = cfg.UneasyArgumentChance, savedCheck = cfg.UneasyArgumentCheckSeconds;

        // DAY1 — 스트레스는 잠겨 있다. 효율 페널티만.
        LiveSetup(1, new() { ["fox"] = "guard_room", ["wolf"] = "guard_room", ["rabbit"] = "core_room", ["dog"] = "core_room" });
        WaitArrive("guard_room", 2);
        WaitArrive("core_room", 2);
        var ops = OpsProfile.Room("guard_room");
        float baseEff = OpsProfile.Curve(ops.Efficiency, 2, 0f);
        Check(RoomStaffing.TensePairs("guard_room").Count == 1, "경비실(여우+늑대) 불편 쌍 1");
        Check(Mathf.IsEqualApprox(RoomStaffing.Efficiency("guard_room"), baseEff * cfg.UneasyEfficiencyMultiplier),
            $"경비실 효율 {baseEff:0.00} × {cfg.UneasyEfficiencyMultiplier} = {RoomStaffing.Efficiency("guard_room"):0.00}");
        float coreBase = OpsProfile.Curve(OpsProfile.Room("core_room").Efficiency, 2, 0f);
        Check(Mathf.IsEqualApprox(RoomStaffing.Efficiency("core_room"), coreBase), "코어실(토끼+강아지, 우호) 효율은 그대로");

        float s0 = Stress("fox");
        cfg.UneasyArgumentChance = 1f; cfg.UneasyArgumentCheckSeconds = 5f;
        Run(12f);
        // 경영 리워크: 스트레스가 DAY1 부터 켜져 있다 — 불편 쌍은 바로 스트레스를 받는다.
        Check(Stress("fox") > s0, "DAY1 부터 긴장·언쟁 스트레스가 쌓인다");
        var args = EventLog.Instance.GetAllEntries().Where(e => e.EventType == LogEventType.Argument).ToList();
        Check(args.Count > 0 && args.All(e => e.RoomId == "guard_room"), $"언쟁이 경비실에서만 로그로 남는다({args.Count}건)");
        var rows = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1);
        var row = rows.FirstOrDefault(r => r.SourceEventType == LogEventType.Argument);
        Check(row != null && row.Text.Contains("언쟁") && row.Severity == DisplayLogSeverity.Warning,
            "시설 로그(L) 표시 — " + (row?.Text ?? "(없음)"));

        // 스트레스가 켜진 날 — 불편 쌍은 긴장 + 언쟁 스트레스를 받는다.
        // (현재 config 는 StressUnlockDay = 99 로 스트레스를 끈 상태라 이 구간만 잠시 DAY2 해금으로 본다.)
        int savedUnlock = cfg.StressUnlockDay;
        cfg.StressUnlockDay = 2;
        LiveSetup(2, new() { ["fox"] = "guard_room", ["wolf"] = "guard_room", ["rabbit"] = "core_room", ["dog"] = "core_room" });
        WaitArrive("guard_room", 2);
        WaitArrive("core_room", 2);
        GD.Print($"   DAY{GameState.Instance.CurrentDay} 스트레스 {(DayFeatures.StressEnabled ? "켜짐" : "잠김")} · 경비실 근무 [{string.Join(",", _sim.OnDutyEmployeeIds("guard_room"))}] · 코어실 [{string.Join(",", _sim.OnDutyEmployeeIds("core_room"))}]");
        float fox0 = Stress("fox"), wolf0 = Stress("wolf"), rab0 = Stress("rabbit"), dog0 = Stress("dog");
        cfg.UneasyArgumentChance = 0f;
        Run(cfg.UneasyStressIntervalSeconds + 1f);
        float foxD = Stress("fox") - fox0, wolfD = Stress("wolf") - wolf0;
        float rabD = Stress("rabbit") - rab0, dogD = Stress("dog") - dog0;
        GD.Print($"   Δ스트레스 여우 {foxD:0.##} 늑대 {wolfD:0.##} | 토끼 {rabD:0.##} 강아지 {dogD:0.##}");
        Check(foxD > rabD && wolfD > dogD, "불편 쌍(여우·늑대)이 우호 쌍(토끼·강아지)보다 스트레스가 더 오른다");

        float foxBefore = Stress("fox");
        cfg.UneasyArgumentChance = 1f; cfg.UneasyArgumentCheckSeconds = 5f;
        int before = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Argument);
        Run(5.5f);
        int after = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Argument);
        Check(after > before && Stress("fox") > foxBefore, "언쟁 → 로그 + 두 사람 스트레스");

        // 떨어뜨리면 페널티가 사라진다.
        _sim.AssignToRoom("fox", "storage_room");
        for (float t = 0f; t < 60f && _sim.OnDutyEmployeeIds("guard_room").Contains("fox"); t += Step) Tick();
        Check(RoomStaffing.TensePairs("guard_room").Count == 0
              && Mathf.IsEqualApprox(RoomStaffing.RelationEfficiency("guard_room"), 1f), "여우가 나가면 경비실 페널티 해제");

        cfg.UneasyArgumentChance = savedChance; cfg.UneasyArgumentCheckSeconds = savedCheck;
        cfg.StressUnlockDay = savedUnlock;
    }

    // ── 도우미 ───────────────────────────────────────────────────────────
    private void NewGame(int day)
    {
        EventLog.Instance?.ClearAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
    }

    // 방해자 없이 근무를 시작한다(방해공작이 결과를 흔들지 않게).
    private void LiveSetup(int day, Dictionary<string, string> plan)
    {
        NewGame(day);
        foreach (var (emp, room) in plan) _sim.AssignToRoom(emp, room);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void WaitArrive(string room, int n)
    {
        for (float t = 0f; t < 60f && _sim.OnDutyEmployeeIds(room).Count < n; t += Step) Tick();
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step) Tick();
    }

    private void Tick()
    {
        GameState.Instance.AdvanceDayTime(Step);
        _sim.Tick(Step);
    }

    private float Stress(string id) => _sim.GetEmployeeState(id)?.Stress ?? 0f;
    private string Assigned(string id) => _sim.GetEmployeeState(id)?.AssignedRoomId ?? "";

    private async System.Threading.Tasks.Task DragFromTo(Vector2 from, Vector2 to)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
        await Frames(1);
        for (int i = 1; i <= 6; i++)
        {
            var p = from.Lerp(to, i / 6f);
            Push(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left });
        }
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
        await Frames(2);
    }

    private async System.Threading.Tasks.Task Hover(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async System.Threading.Tasks.Task Click(Vector2 at)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private void Push(InputEvent e) => _vp.PushInput(e, true);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
