using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// G-2 수리 승인 절차 검증.
//
//   godot --headless --path . res://scenes/debug/RepairApprovalTest.tscn --quit-after 6000
//
// 승인 · 거절 · 무응답 · 미로 실패가 각각 수리에 어떻게 반영되는지, 요청이 겹칠 때
// 줄을 서는지를 본다. 미로 규칙 자체는 RepairMazeTest 가 따로 본다.
public partial class RepairApprovalTest : Node
{
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
        GD.Print("\n\n################ G-2 수리 승인 절차 검증 ################");
        TestAskAppears();
        TestDeclinePenalty();
        TestTimeoutPenalty();
        TestApproveThenSolve();
        TestApproveThenFail();
        TestQueue();
        TestMazeTimerStartsOnFirstInput();
        TestHeldAndGate();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 요청이 뜬다 ─────────────────────────────────────────────────────
    private void TestAskAppears()
    {
        Head("A", "수리가 걸리면 승인 요청이 뜬다");
        var task = NewRepair();
        Tick(0.1f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking,
            $"승인 요청 단계 ({RepairApprovalSystem.Current})");
        Check(RepairApprovalSystem.Active?.Task == task, "요청이 그 수리를 가리킨다");
        float want = Config.Instance.Data.RepairApproveSeconds;
        Check(Mathf.Abs(RepairApprovalSystem.SecondsTotal - want) < 0.01f,
            $"응답 제한 {want:0}초 ({RepairApprovalSystem.SecondsTotal:0.#})");
    }

    // ── 거절 ────────────────────────────────────────────────────────────
    private void TestDeclinePenalty()
    {
        Head("B", "거절하면 그 수리가 느려진다");
        var task = NewRepair();
        Tick(0.1f);
        float before = task.GaugeRequired;
        RepairApprovalSystem.Decline();
        float rate = Config.Instance.Data.RepairDenyPenaltyRate;
        Check(Mathf.Abs(task.GaugeRequired - before * (1f + rate)) < 0.01f,
            $"수리 시간 +{rate * 100f:0}% ({before:0.#} → {task.GaugeRequired:0.#})");
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Result, "결과를 잠깐 보여 준다");
        Tick(1f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Idle, "그 뒤 요청이 닫힌다");
    }

    // ── 무응답 ──────────────────────────────────────────────────────────
    private void TestTimeoutPenalty()
    {
        Head("C", "답하지 않아도 거절과 같다");
        var task = NewRepair();
        Tick(0.1f);
        float before = task.GaugeRequired;
        Tick(Config.Instance.Data.RepairApproveSeconds + 0.2f);
        float rate = Config.Instance.Data.RepairDenyPenaltyRate;
        Check(Mathf.Abs(task.GaugeRequired - before * (1f + rate)) < 0.01f,
            $"무응답도 +{rate * 100f:0}% ({task.GaugeRequired:0.#})");
        Check(!RepairApprovalSystem.LastApproved, "승인하지 않은 것으로 남는다");
    }

    // ── 승인 → 성공 ─────────────────────────────────────────────────────
    private void TestApproveThenSolve()
    {
        Head("D", "승인하고 미로를 풀면 수리가 그대로 진행된다");
        var task = NewRepair();
        Tick(0.1f);
        float before = task.GaugeRequired;
        RepairApprovalSystem.Approve();
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Maze, "곧바로 미로가 뜬다");

        var m = RepairApprovalSystem.Maze;
        Check(m != null, "미로가 만들어졌다");
        if (m == null) return;
        Check(RepairApprovalSystem.SecondsTotal > 0f, $"제한시간이 있다 ({RepairApprovalSystem.SecondsTotal:0.0}초)");

        WalkSolution(m);
        Check(RepairApprovalSystem.LastSucceeded, "성공으로 끝난다");
        Check(Mathf.Abs(task.GaugeRequired - before) < 0.01f, "수리 시간이 늘지 않는다");
    }

    // ── 승인 → 실패 ─────────────────────────────────────────────────────
    private void TestApproveThenFail()
    {
        Head("E", "승인했어도 미로에 실패하면 느려진다");
        var task = NewRepair();
        Tick(0.1f);
        float before = task.GaugeRequired;
        RepairApprovalSystem.Approve();
        var m = RepairApprovalSystem.Maze;
        if (m == null) { Check(false, "미로가 만들어졌다"); return; }

        // 벽 쪽으로 눌러 실패시킨다.
        var blocked = new[] { RepairMaze.Dir.Up, RepairMaze.Dir.Left }.First(d => !m.IsOpen(m.Start, d));
        RepairApprovalSystem.MazeInput(blocked);
        float rate = Config.Instance.Data.RepairDenyPenaltyRate;
        Check(!RepairApprovalSystem.LastSucceeded, "실패로 끝난다");
        Check(Mathf.Abs(task.GaugeRequired - before * (1f + rate)) < 0.01f,
            $"수리 시간 +{rate * 100f:0}% ({task.GaugeRequired:0.#})");
        Check(RepairApprovalSystem.LastApproved, "승인은 했던 것으로 남는다(거절과 구분)");
    }

    // ── 겹치면 줄을 선다 ────────────────────────────────────────────────
    private void TestQueue()
    {
        Head("F", "요청이 겹치면 하나씩 처리한다");
        Reset();
        var a = NewRepair("power_room", clean: false);
        var b = NewRepair("core_room", clean: false);
        Tick(0.1f);
        Check(RepairApprovalSystem.Active?.Task == a, "먼저 걸린 수리부터 묻는다");
        Check(RepairApprovalSystem.Pending == 1, $"나머지는 줄에서 기다린다 ({RepairApprovalSystem.Pending}건)");

        RepairApprovalSystem.Decline();
        Tick(1f);
        Check(RepairApprovalSystem.Active?.Task == b, "앞 건이 끝나면 다음 건을 묻는다");
        Check(RepairApprovalSystem.Pending == 0, "줄이 비었다");
    }

    // ── 뜨자마자 죽지 않는다 ────────────────────────────────────────────
    private void TestMazeTimerStartsOnFirstInput()
    {
        Head("G", "미로 시간은 첫 방향키부터 흐른다");
        var task = NewRepair();
        Tick(0.1f);
        RepairApprovalSystem.Approve();
        float total = RepairApprovalSystem.SecondsTotal;

        Tick(3f);   // 가만히 둔다
        Check(!RepairApprovalSystem.MazeStarted, "아직 시작하지 않았다");
        Check(Mathf.Abs(RepairApprovalSystem.SecondsLeft - total) < 0.01f,
            $"시간이 줄지 않는다 ({RepairApprovalSystem.SecondsLeft:0.0}/{total:0.0})");

        var m = RepairApprovalSystem.Maze;
        var open = new[] { RepairMaze.Dir.Right, RepairMaze.Dir.Down }.First(d => m.IsOpen(m.Start, d));
        RepairApprovalSystem.MazeInput(open);
        Check(RepairApprovalSystem.MazeStarted, "첫 입력에 시작한다");
        Tick(1f);
        Check(RepairApprovalSystem.SecondsLeft < total, "그때부터 시간이 흐른다");
    }

    // --- 도우미 ---------------------------------------------------------

    // 해답을 그대로 따라 걷는다.
    private void WalkSolution(RepairMaze m)
    {
        var path = m.ShortestPath(m.Start, m.Goal);
        for (int i = 1; i < path.Count; i++)
        {
            var step = path[i] - path[i - 1];
            var dir = step == new Vector2I(0, -1) ? RepairMaze.Dir.Up
                : step == new Vector2I(0, 1) ? RepairMaze.Dir.Down
                : step == new Vector2I(-1, 0) ? RepairMaze.Dir.Left : RepairMaze.Dir.Right;
            RepairApprovalSystem.MazeInput(dir);
        }
    }

    private SpawnedTask NewRepair(string roomId = "power_room", bool clean = true)
    {
        if (clean) Reset();
        var task = new SpawnedTask
        {
            TaskId = "repair", RoomId = roomId, IsRepair = true,
            Status = SpawnedTaskStatus.Active, GaugeRequired = 20f, TimeLimitSeconds = float.MaxValue,
        };
        RepairApprovalSystem.Enqueue(roomId, roomId, task);
        return task;
    }

    // ── 요청이 뜨기 전에는 수리가 진행되지 않는다 ───────────────────────
    private void TestHeldAndGate()
    {
        Head("H", "승인 전에는 수리가 한 톨도 진행되지 않는다");
        Reset();
        // Held = 줄에 세워만 두고 띄우지 않는다(DAY0 교육이 "사람을 보낸 뒤에" 띄우려고 쓴다).
        // NewRepair 가 Reset 을 한 번 더 돌리므로(clean) 그 뒤에 켠다 — ResetAll 이 Held 를 내린다.
        var task = NewRepair();
        RepairApprovalSystem.Held = true;
        Tick(2f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Idle, "붙잡아 두면 요청이 뜨지 않는다");
        Check(RepairApprovalSystem.IsAwaiting(task), "그동안에도 그 수리는 대기 상태다");

        RepairApprovalSystem.Held = false;
        Tick(0.1f);
        Check(RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking, "놓으면 그제서야 뜬다");
        Check(RepairApprovalSystem.IsAwaiting(task), "묻는 동안에는 수리가 멈춰 있다");

        RepairApprovalSystem.Approve();
        Check(RepairApprovalSystem.IsAwaiting(task), "미로를 푸는 동안에도 멈춰 있다");

        var m = RepairApprovalSystem.Maze;
        var path = m.ShortestPath(m.Cursor, m.Goal);
        for (int i = 1; i < path.Count; i++)
        {
            var d = path[i] - RepairApprovalSystem.Maze.Cursor;
            RepairApprovalSystem.MazeInput(d.Y < 0 ? RepairMaze.Dir.Up : d.Y > 0 ? RepairMaze.Dir.Down
                : d.X < 0 ? RepairMaze.Dir.Left : RepairMaze.Dir.Right);
        }
        Check(!RepairApprovalSystem.IsAwaiting(task), "절차가 끝나면 수리가 풀린다");
    }

    private void Reset()
    {
        GameState.Instance.ResetRun(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
    }

    private void Tick(float seconds)
    {
        const float step = 1f / 30f;
        for (float t = 0f; t < seconds; t += step) RepairApprovalSystem.Tick(step);
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
