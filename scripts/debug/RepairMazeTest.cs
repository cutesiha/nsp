using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.Debug;

// G-2 수리 승인 미로 검증.
//
//   godot --headless --path . res://scenes/debug/RepairMazeTest.tscn --quit-after 8000
//
// 미로가 억울하지 않은지를 본다 — 반드시 풀 수 있고, 제한시간이 해답보다 충분히 길고,
// 실패는 세 가지 경우에만 난다.
public partial class RepairMazeTest : Node
{
    private int _pass, _fail;

    public override void _Ready()
    {
        GD.Print("\n\n################ G-2 수리 미로 검증 ################");
        TestGeneration();
        TestFailWall();
        TestFailBacktrack();
        TestFailTimeout();
        TestDayScaling();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 1,000회 생성 — 해답 보장 · 시간 여유 ────────────────────────────
    private void TestGeneration()
    {
        Head("A", "1,000회 생성 — 반드시 풀 수 있고 시간이 넉넉하다");
        const int runs = 1000;
        int unsolvable = 0, tooTight = 0, minLen = int.MaxValue, maxLen = 0;
        float minSlack = float.MaxValue;

        for (int i = 0; i < runs; i++)
        {
            var m = RepairMaze.Generate(7, 1f, (ulong)(i + 1));
            var path = m.ShortestPath(m.Start, m.Goal);
            if (path.Count == 0 || path[0] != m.Start || path[^1] != m.Goal) { unsolvable++; continue; }

            // 해답을 그대로 따라가면 실제로 도착하는가(규칙 위반 없이).
            var walk = RepairMaze.Generate(7, 1f, (ulong)(i + 1));
            bool ok = true;
            for (int k = 1; k < path.Count && ok; k++)
            {
                var step = path[k] - path[k - 1];
                var dir = step == new Vector2I(0, -1) ? RepairMaze.Dir.Up
                    : step == new Vector2I(0, 1) ? RepairMaze.Dir.Down
                    : step == new Vector2I(-1, 0) ? RepairMaze.Dir.Left : RepairMaze.Dir.Right;
                var r = walk.Step(dir);
                if (r is RepairMaze.StepResult.HitWall or RepairMaze.StepResult.Backtracked) ok = false;
            }
            if (!ok || !walk.Solved) { unsolvable++; continue; }

            minLen = Mathf.Min(minLen, path.Count);
            maxLen = Mathf.Max(maxLen, path.Count);

            // 제한시간이 "해답 길이 × 입력 시간" 의 1.5배 이상인가.
            float need = (path.Count - 1) * RepairMaze.SecondsPerStep;
            float slack = m.TimeLimitSeconds / Mathf.Max(0.001f, need);
            minSlack = Mathf.Min(minSlack, slack);
            if (slack < RepairMaze.TimeMargin - 0.001f) tooTight++;
        }

        GD.Print($"   해답 길이 {minLen}~{maxLen}칸 · 최소 시간 여유 {minSlack:0.00}배");
        Check(unsolvable == 0, $"{runs}회 모두 풀 수 있다 (불가 {unsolvable}건)");
        Check(tooTight == 0, $"{runs}회 모두 시간 여유 {RepairMaze.TimeMargin:0.0}배 이상 (모자람 {tooTight}건)");
        Check(minSlack >= RepairMaze.TimeMargin - 0.001f, "가장 빠듯한 미로도 기준을 넘는다");
    }

    // ── 실패 ① 벽 ───────────────────────────────────────────────────────
    private void TestFailWall()
    {
        Head("B", "실패 ① 벽 쪽으로 눌렀다");
        var m = RepairMaze.Generate(7, 1f, 42);
        // 시작 칸에서 막혀 있는 방향을 찾는다(바깥으로 나가는 방향은 항상 벽이다).
        var blocked = new[] { RepairMaze.Dir.Up, RepairMaze.Dir.Left }
            .First(d => !m.IsOpen(m.Start, d));
        var r = m.Step(blocked);
        Check(r == RepairMaze.StepResult.HitWall, $"벽으로 가면 즉시 실패 ({r})");
        Check(m.Failed && m.FailedAs == RepairMaze.StepResult.HitWall, "실패 이유가 남는다");
        Check(m.FailedAt == m.Start, "어느 칸에서 실패했는지 남는다");
        Check(m.Step(RepairMaze.Dir.Right) == RepairMaze.StepResult.HitWall, "실패 뒤에는 더 움직이지 않는다");
    }

    // ── 실패 ② 왔던 칸 ──────────────────────────────────────────────────
    private void TestFailBacktrack()
    {
        Head("C", "실패 ② 이미 지나온 칸으로 되돌아갔다");
        var m = RepairMaze.Generate(7, 1f, 7);
        // 갈 수 있는 첫 방향으로 한 칸 간 뒤, 곧바로 되돌아간다.
        var open = new[] { RepairMaze.Dir.Right, RepairMaze.Dir.Down }.First(d => m.IsOpen(m.Start, d));
        Check(m.Step(open) == RepairMaze.StepResult.Moved, "한 칸 갔다");
        var back = open == RepairMaze.Dir.Right ? RepairMaze.Dir.Left : RepairMaze.Dir.Up;
        var r = m.Step(back);
        Check(r == RepairMaze.StepResult.Backtracked, $"왔던 칸으로 가면 즉시 실패 ({r})");
        Check(m.FailedAt == m.Start, "되돌아가려 한 칸이 남는다");
    }

    // ── 실패 ③ 시간 ─────────────────────────────────────────────────────
    private void TestFailTimeout()
    {
        Head("D", "실패 ③ 시간이 다했다");
        var m = RepairMaze.Generate(7, 1f, 11);
        Check(!m.Failed, "아직 실패가 아니다");
        m.TimeOut();
        Check(m.TimedOut && m.Failed, "시간이 다하면 실패다");
        Check(m.Step(RepairMaze.Dir.Right) != RepairMaze.StepResult.Moved, "그 뒤에는 움직이지 않는다");
    }

    // ── DAY 가 갈수록 조인다 ────────────────────────────────────────────
    private void TestDayScaling()
    {
        Head("E", "DAY 가 갈수록 미로가 커지고 시간이 줄어든다");
        int d1 = RepairMaze.SizeForDay(1), d5 = RepairMaze.SizeForDay(5);
        float t1 = RepairMaze.TimeScaleForDay(1), t5 = RepairMaze.TimeScaleForDay(5);
        GD.Print($"   DAY1 {d1}칸 ×{t1:0.00} / DAY5 {d5}칸 ×{t5:0.00}");
        Check(d5 >= d1, "미로가 작아지지는 않는다");
        Check(t5 <= t1, "시간 배율이 늘어나지는 않는다");

        // 조여도 풀 수 있어야 한다.
        int tight = 0;
        for (int i = 0; i < 200; i++)
        {
            var m = RepairMaze.Generate(d5, t5, (ulong)(i + 500));
            var path = m.ShortestPath(m.Start, m.Goal);
            if (path.Count == 0) { tight++; continue; }
            float need = (path.Count - 1) * RepairMaze.SecondsPerStep;
            if (m.TimeLimitSeconds < need) tight++;
        }
        Check(tight == 0, $"가장 조인 날에도 제한시간이 해답보다 길다 (모자람 {tight}건)");
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
