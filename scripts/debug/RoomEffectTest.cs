using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 작업실 일곱 개의 효과가 화면에 실제로 드러나는지 검사한다.
//
//   godot --headless --path . res://scenes/debug/RoomEffectTest.tscn
//
// 보는 것은 세 가지뿐이다.
//   (a) 방마다 "보이는 숫자 한 줄"이 나온다.
//   (b) 효과가 발생하는 순간 로그에 그 방 전용 색의 줄이 남는다.
//   (c) 방을 비우면 "무엇을 잃고 있는지" 붉은 한 줄이 나온다.
//
// 시뮬레이션 수치는 보지 않는다 — 이 검사는 표시가 붙어 있는지만 본다.
public partial class RoomEffectTest : Node
{
    private const float Step = 1f / 30f;

    private static readonly string[] Rooms =
    {
        "core_room", "power_room", "maintenance_room", "storage_room",
        "vent_room", "guard_room", "medical_room",
    };

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 작업실 효과 표시 검사 ################");

        CheckHeadlines();
        CheckIdleLines();
        CheckLogColors();
        CheckTutorialDay();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── (a) 보이는 숫자 ──────────────────────────────────────────────
    private void CheckHeadlines()
    {
        GD.Print("\n---------------- (a) 방 카드 첫 줄 ----------------");
        StartShift(2, staffEveryRoom: true);
        Run(30f);

        foreach (string roomId in Rooms)
        {
            string line = RoomEffectText.Headline(roomId);
            GD.Print($"   {RoomName(roomId),-8} {line}");
            Check(!string.IsNullOrWhiteSpace(line), $"{RoomName(roomId)} 에 보이는 숫자 한 줄이 있다");
        }

        // 숫자가 실제로 움직이는가 — 코어·자재·경비 기록은 근무가 돌면 늘어야 한다.
        Check(RoomEffectStats.CoreUpToday > 0f, $"코어 복구량이 집계된다 ({RoomEffectStats.CoreUpToday:0.#}%)");
        Check(RoomEffectStats.MaterialsToday > 0, $"자재 생산량이 집계된다 ({RoomEffectStats.MaterialsToday}개)");
        Check(RoomEffectStats.GuardRecordsToday > 0, $"경비 기록이 집계된다 ({RoomEffectStats.GuardRecordsToday}건)");
    }

    // ── (c) 비웠을 때의 붉은 줄 ──────────────────────────────────────
    private void CheckIdleLines()
    {
        GD.Print("\n---------------- (c) 비웠을 때 ----------------");
        // 아무도 배치하지 않은 근무. 일곱 방 모두가 "무엇을 잃고 있는지" 말해야 한다.
        StartShift(2, staffEveryRoom: false);
        Run(30f);

        // 의무실은 기절자가 있어야 말이 된다 — 한 명을 실제로 기절시킨다.
        string victim = _sim.GetActiveEmployeeIds().FirstOrDefault();
        if (!string.IsNullOrEmpty(victim)) _sim.AddStress(victim, 60f, "검사");
        Run(2f);

        foreach (string roomId in Rooms)
        {
            string line = RoomEffectText.Idle(roomId);
            GD.Print($"   {RoomName(roomId),-8} {line.Replace("\n", " / ")}");
            Check(!string.IsNullOrWhiteSpace(line), $"{RoomName(roomId)} 을 비우면 무엇을 잃는지 적힌다");
        }

        // 사람이 있으면 그 줄은 사라져야 한다(붉은 줄이 상시 표시되면 의미가 없다).
        StartShift(2, staffEveryRoom: true);
        Run(10f);
        var still = Rooms.Where(r => !string.IsNullOrWhiteSpace(RoomEffectText.Idle(r))).ToList();
        Check(still.Count == 0, $"사람을 채우면 붉은 줄이 사라진다 (남은 방: {(still.Count == 0 ? "없음" : string.Join(", ", still.Select(RoomName)))})");
    }

    // ── (b) 로그 줄이 색으로 갈라지는가 ──────────────────────────────
    private void CheckLogColors()
    {
        GD.Print("\n---------------- (b) 로그 줄 ----------------");
        StartShift(2, staffEveryRoom: true);
        Run(20f);

        // 발전실을 비운다 → 출력이 90% 아래로 떨어지는 순간이 잡혀야 한다.
        Clear("power_room");
        Run(3f);
        // 저장고를 비운다 → 코어 1%당 자재 소모가 바뀌는 순간.
        Clear("storage_room");
        Run(3f);
        // 환기실을 비웠다가 다시 채운다 → 공기 순환 재개.
        Clear("vent_room");
        Run(3f);
        Assign("vent_room", 1);
        Run(3f);
        // 기절자 한 명 → 의무실 이송.
        string victim = _sim.GetActiveEmployeeIds().FirstOrDefault(id =>
            _sim.GetEmployeeState(id) is { Alive: true, Incapacitated: false });
        if (!string.IsNullOrEmpty(victim)) _sim.AddStress(victim, 60f, "검사");
        // 모아 둔 코어·자재 줄이 나가도록 충분히 돌린다(모음 주기 10초).
        Run(14f);

        var rows = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), GameState.Instance.CurrentDay);
        var effects = rows.Where(r => r.Severity == DisplayLogSeverity.RoomEffect).ToList();

        foreach (var r in effects.Take(20))
            GD.Print($"   {r.Timestamp,5:0}초  #{Day1HistoryOverlay.BodyColor(r).ToHtml(false)}  {r.Text}");

        Check(effects.Count > 0, $"방 효과 줄이 로그에 남는다 ({effects.Count}줄)");

        // 어느 방들이 말을 했는가. 일곱 중 다섯 이상은 나와야 "보인다"고 할 수 있다.
        var spoke = effects.Select(r => r.RoomId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        GD.Print("   말한 방 : " + string.Join(", ", spoke.Select(RoomName)));
        Check(spoke.Count >= 5, $"일곱 방 중 다섯 이상이 로그에 말한다 ({spoke.Count}개)");

        // 색이 실제로 갈라지는가 — 경고·사고 색과 겹치면 "무슨 일이 났다"로 잘못 읽힌다.
        // 이것이 지시가 요구한 조건이다.
        var alarm = rows.Where(r => r.Severity is DisplayLogSeverity.Warning or DisplayLogSeverity.Critical
                                     or DisplayLogSeverity.Sabotage)
            .Select(Day1HistoryOverlay.BodyColor).ToList();
        int clashes = effects.Count(e => alarm.Any(o => Near(o, Day1HistoryOverlay.BodyColor(e))));
        Check(clashes == 0, $"방 효과 색이 경고·사고 색과 겹치지 않는다 (겹친 줄 {clashes})");

        // 직원 고유색과 가까운 방이 있는가. 실패로 보지 않는다 — 직원 줄은 "이름 | ..." 이고
        // 방 줄은 "작업실 | ..." 에 ◆ 표식이 붙어 글로 먼저 갈라지기 때문이다.
        // 다만 알아 둘 값이라 남긴다(RoomDef.MapColor 와 EmployeeDef.IconColor 는 따로 정해졌다).
        foreach (string roomId in spoke)
        {
            var rc = Day1HistoryOverlay.BodyColor(new DisplayLogEntry
                { Severity = DisplayLogSeverity.RoomEffect, RoomId = roomId });
            foreach (string empId in _sim.GetEmployeeIds())
            {
                var d = _sim.GetEmployeeDef(empId);
                if (d == null) continue;
                var ec = Day1HistoryOverlay.BodyColor(new DisplayLogEntry { RelatedEmployeeId = empId });
                if (Near(rc, ec))
                    GD.Print($"      참고: {RoomName(roomId)} #{rc.ToHtml(false)} 가 "
                             + $"{d.Codename} #{ec.ToHtml(false)} 와 가깝다");
            }
        }

        // 방마다 다른 색인가.
        var colors = spoke.Select(id => _sim.GetRoomDef(id)?.MapColor ?? Colors.White).ToList();
        int dup = 0;
        for (int i = 0; i < colors.Count; i++)
            for (int j = i + 1; j < colors.Count; j++)
                if (Near(colors[i], colors[j])) dup++;
        Check(dup == 0, $"방마다 색이 다르다 (겹친 쌍 {dup})");
    }

    // ── (d) DAY0 는 스트레스가 잠겨 있다 ────────────────────────────
    private void CheckTutorialDay()
    {
        GD.Print("\n---------------- (d) 교육일(DAY0) ----------------");
        StartShift(0, staffEveryRoom: true);
        Run(5f);
        string vent = RoomEffectText.Headline("vent_room");
        GD.Print($"   환기실   {vent}");
        Check(vent.Contains("영향 없음"), "DAY0 환기실 카드는 '오늘은 영향 없음'");
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    private void StartShift(int day, bool staffEveryRoom)
    {
        GameState.Instance.ResetRun(day);
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);

        if (!staffEveryRoom) return;
        // 일곱 방을 한 명씩. 인원이 모자라면 앞쪽 방부터 채운다.
        var roster = _sim.GetActiveEmployeeIds().ToList();
        for (int i = 0; i < Rooms.Length && i < roster.Count; i++)
            _sim.AssignToRoom(roster[i], Rooms[i]);
    }

    private void Assign(string roomId, int n)
    {
        int placed = 0;
        foreach (string id in _sim.GetActiveEmployeeIds())
        {
            if (placed >= n) break;
            var st = _sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false, Incapacitated: false }) continue;
            if (st.AssignedRoomId == roomId) { placed++; continue; }
            _sim.AssignToRoom(id, roomId);
            placed++;
        }
    }

    // 그 방 인원을 전부 중앙 사무실로 물린다(다른 방의 인원 구성을 건드리지 않는다).
    private void Clear(string roomId)
    {
        foreach (string id in _sim.GetActiveEmployeeIds().ToList())
            if (_sim.GetEmployeeState(id)?.AssignedRoomId == roomId)
                _sim.AssignToRoom(id, "central_office");
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) < 0.06f && Mathf.Abs(a.G - b.G) < 0.06f && Mathf.Abs(a.B - b.B) < 0.06f;

    private string RoomName(string roomId) => _sim.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
