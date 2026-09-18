using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.Core;

// 개발용 사고 UX 검증. 게임 흐름에서는 로드되지 않는다.
//   godot --headless --path . scenes/debug/SensorScenarioTest.tscn
public partial class SensorScenarioTest : Node
{
    private FacilitySimulation _sim;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;

        TestA();
        TestB();
        TestC();
        TestD();
        TestE();
        TestF();
        TestG();
        GD.Print("\n=== 센서 시나리오 검증 종료 ===");
        GetTree().Quit();
    }

    // TEST A — 환기실을 비우면 사고 전에 원인과 남은 시간이 보인다.
    private void TestA()
    {
        Setup(skip: "vent_room");
        Run(6f);
        Print("TEST A : 환기실 무인 6초 경과");
    }

    // TEST B — 위험 시간 전에 배치하면 경고가 사라진다.
    private void TestB()
    {
        Setup(skip: "vent_room");
        Run(10f);
        GD.Print("\n[TEST B] 10초 시점 — 아래 위험이 떠 있어야 한다");
        PrintRows();
        _sim.AssignToRoom("crow", "vent_room");
        Run(1f);
        Print("TEST B : 환기실에 직원 배치 직후 (위험 해소)");
    }

    // TEST C — 무시하면 사고가 나고 해결 전까지 ACTIVE 로 남는다.
    private void TestC()
    {
        Setup(skip: "vent_room");
        Run(40f);
        Print("TEST C : 경고 무시 → 사고 발생");
        Run(20f);
        GD.Print("  20초 더 지난 뒤에도 목록에 남아 있는가 →");
        PrintRows();
    }

    // TEST D — 발전실 사고 하나에 파생 결과가 묶인다.
    private void TestD()
    {
        Setup(skip: "power_room");
        Run(40f);
        Print("TEST D : 발전실 사고 (파생 결과 묶임)");
    }

    // TEST E — 사고/위험이 여러 건이면 페이지가 늘어난다.
    private void TestE()
    {
        Setup(skip: "");
        // 두 방을 동시에 비운다.
        _sim.ClearAssignment("crow");
        _sim.ClearAssignment("cat");
        Run(14f);
        Print("TEST E : 두 곳 동시 위험");
        GD.Print($"  센서 페이지 수 = 1(요약) + {IncidentBoard.Build().Count}(카드)"
                 + $" + {(IncidentTracker.Recent.Count > 0 ? 1 : 0)}(최근)");
    }

    // TEST F — 방해공작 흔적에 범인 이름이 없어야 한다.
    private void TestF()
    {
        Setup(skip: "");
        IncidentTracker.Anomaly("core_room", "비정상 조작 흔적 감지", "코어 복구율 -3%");
        Print("TEST F : 방해공작 흔적");
        string names = string.Join("", _sim.GetEmployeeIds().Select(id => _sim.GetEmployeeDef(id).Codename));
        var rows = IncidentBoard.Build();
        bool leak = rows.Any(r => names.Length > 0 && _sim.GetEmployeeIds()
            .Any(id => r.CauseText.Contains(_sim.GetEmployeeDef(id).Codename)
                       || r.Title.Contains(_sim.GetEmployeeDef(id).Codename)));
        GD.Print($"  범인 이름 노출 = {(leak ? "있음(문제)" : "없음")}");
    }

    // TEST G — SENSOR 전원을 끄면 사전 경고가 끊긴다.
    private void TestG()
    {
        Setup(skip: "vent_room");
        Run(10f);
        GD.Print("\n[TEST G] SENSOR ON");
        PrintRows();
        GameState.Instance.TryTogglePower(PowerConsumer.Sensor);
        GD.Print($"  SENSOR OFF (전원 = {GameState.Instance.IsConsumerPowered(PowerConsumer.Sensor)})");
        PrintRows();
        Run(40f);
        GD.Print("  OFF 상태로 사고 발생까지 진행 →");
        PrintRows();
        GameState.Instance.TryTogglePower(PowerConsumer.Sensor);
        GD.Print("  SENSOR 다시 ON → 현재 활성 사고는 확인 가능해야 한다");
        PrintRows();
    }

    // --- 도우미 ---------------------------------------------------------

    private void Setup(string skip)
    {
        EventLog.Instance.ClearAll();
        _sim.ResetRun();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);

        var plan = new (string Emp, string Room)[]
        {
            ("owl", "guard_room"), ("cat", "maintenance_room"), ("crow", "vent_room"),
            ("rabbit", "core_room"), ("jellyfish", "storage_room"), ("fox", "power_room"),
        };
        foreach (var (emp, room) in plan)
            if (room != skip) _sim.AssignToRoom(emp, room);

        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        if (!GameState.Instance.IsConsumerPowered(PowerConsumer.Sensor))
            GameState.Instance.TryTogglePower(PowerConsumer.Sensor);
    }

    private void Run(float seconds)
    {
        const float step = 0.25f;
        for (float t = 0f; t < seconds; t += step)
        {
            GameState.Instance.AdvanceDayTime(step);
            _sim.Tick(step);
        }
    }

    private void Print(string label)
    {
        GD.Print($"\n===== {label} =====");
        PrintRows();
    }

    private static void PrintRows()
    {
        var rows = IncidentBoard.Build();
        if (rows.Count == 0) { GD.Print("  (표시할 항목 없음 — 정상)"); return; }
        foreach (var r in rows)
        {
            string kind = r.IsOperational ? "운영" : r.IsProtocol ? "금기" : r.State.ToString();
            string room = FacilitySimulation.Instance?.GetRoomDef(r.RoomId)?.DisplayName ?? r.RoomId;
            string time = r.WarningRemainingSeconds >= 0f ? $" / {r.WarningRemainingSeconds:00.0}초" : "";
            GD.Print($"  [{kind,-8}] {room} · {r.Title}{time}");
            GD.Print($"             원인 {r.CauseText} / 결과 {string.Join(" + ", r.ConsequenceLines)} / 조치 {r.ActionHint}");
        }
    }
}
