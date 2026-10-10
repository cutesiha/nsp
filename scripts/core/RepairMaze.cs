using System;
using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// 수리 승인 미로(G-2).
//
// 관리자가 수리를 승인하면 패드에 작은 미로가 뜬다. 방향키로 한 칸씩 S 에서 G 까지 간다.
// 한 번의 실수로 끝난다 — 벽으로 가거나, 왔던 칸으로 되돌아가거나, 시간이 다하면 실패다.
//
// 화면과 완전히 분리해 둔다. 여기에는 Godot 노드도 입력도 없고 규칙만 있다 —
// 그래야 생성 1,000회를 헤드리스로 돌려 "풀 수 있는가 · 시간이 넉넉한가" 를 검사할 수 있다.
public sealed class RepairMaze
{
    public enum Dir { Up, Down, Left, Right }

    public enum StepResult
    {
        Moved,        // 한 칸 갔다
        HitWall,      // 벽 쪽으로 눌렀다 — 실패
        Backtracked,  // 이미 지나온 칸으로 되돌아갔다 — 실패
        Reached,      // 도착
    }

    // 한 칸을 눌러 넘어가는 데 사람이 쓰는 시간(초). 제한시간은 이 값에서 나온다.
    public const float SecondsPerStep = 0.35f;
    // 해답 길이에 곱하는 여유. 1.5 배 아래로는 내려가지 않는다(지시서 기준).
    public const float TimeMargin = 1.5f;

    public int Size { get; private set; }
    public Vector2I Start { get; private set; }
    public Vector2I Goal { get; private set; }
    public Vector2I Cursor { get; private set; }
    // 해답 경로의 칸 수(시작 칸 포함). 제한시간 산출의 근거다.
    public int SolutionLength { get; private set; }
    public float TimeLimitSeconds { get; private set; }
    public bool Solved => Cursor == Goal;
    // 실패했다면 어느 칸에서 왜 실패했는가(화면이 0.5초 보여 준다).
    public StepResult? FailedAs { get; private set; }
    public Vector2I FailedAt { get; private set; }

    public IReadOnlyList<Vector2I> Trail => _trail;

    // _open[x, y] 의 각 방향 = 그 방향으로 지나갈 수 있는가.
    private bool[,] _up, _down, _left, _right;
    private readonly List<Vector2I> _trail = new();
    private readonly HashSet<Vector2I> _visited = new();

    // size 칸짜리 정사각 미로를 만든다. seed 를 주면 같은 미로가 다시 나온다(검사용).
    public static RepairMaze Generate(int size, float timeScale = 1f, ulong? seed = null)
    {
        size = Mathf.Clamp(size, 3, 15);
        var rng = new RandomNumberGenerator();
        if (seed.HasValue) rng.Seed = seed.Value;
        else rng.Randomize();

        var m = new RepairMaze { Size = size };
        m.Carve(rng);
        m.Start = new Vector2I(0, 0);
        m.Goal = new Vector2I(size - 1, size - 1);
        m.Cursor = m.Start;
        m._trail.Add(m.Start);
        m._visited.Add(m.Start);

        // 완전 미로라 두 칸 사이의 길은 반드시 하나뿐이다. 그 길이가 곧 해답 길이다.
        var path = m.ShortestPath(m.Start, m.Goal);
        m.SolutionLength = path.Count;
        // 첫 칸은 이미 서 있으므로 실제로 눌러야 하는 횟수는 (길이 - 1) 이다.
        float need = Mathf.Max(1, path.Count - 1) * SecondsPerStep * TimeMargin;
        m.TimeLimitSeconds = Mathf.Max(3f, need * Mathf.Max(0.2f, timeScale));
        return m;
    }

    // 깊이 우선 탐색으로 벽을 헐어 완전 미로를 만든다(고립된 칸도, 순환도 없다).
    private void Carve(RandomNumberGenerator rng)
    {
        _up = new bool[Size, Size];
        _down = new bool[Size, Size];
        _left = new bool[Size, Size];
        _right = new bool[Size, Size];

        var seen = new bool[Size, Size];
        var stack = new Stack<Vector2I>();
        var at = new Vector2I(0, 0);
        seen[0, 0] = true;
        stack.Push(at);

        while (stack.Count > 0)
        {
            at = stack.Peek();
            var options = new List<Dir>(4);
            foreach (Dir d in new[] { Dir.Up, Dir.Down, Dir.Left, Dir.Right })
            {
                var n = at + Delta(d);
                if (Inside(n) && !seen[n.X, n.Y]) options.Add(d);
            }
            if (options.Count == 0) { stack.Pop(); continue; }

            var pick = options[(int)(rng.Randi() % (uint)options.Count)];
            var next = at + Delta(pick);
            OpenBetween(at, pick);
            seen[next.X, next.Y] = true;
            stack.Push(next);
        }
    }

    private void OpenBetween(Vector2I at, Dir d)
    {
        var n = at + Delta(d);
        Set(d, at, true);
        Set(Opposite(d), n, true);
    }

    private void Set(Dir d, Vector2I at, bool open)
    {
        switch (d)
        {
            case Dir.Up: _up[at.X, at.Y] = open; break;
            case Dir.Down: _down[at.X, at.Y] = open; break;
            case Dir.Left: _left[at.X, at.Y] = open; break;
            case Dir.Right: _right[at.X, at.Y] = open; break;
        }
    }

    public bool IsOpen(Vector2I at, Dir d)
    {
        if (!Inside(at)) return false;
        return d switch
        {
            Dir.Up => _up[at.X, at.Y],
            Dir.Down => _down[at.X, at.Y],
            Dir.Left => _left[at.X, at.Y],
            Dir.Right => _right[at.X, at.Y],
            _ => false,
        };
    }

    // 방향키 한 번. 실패했으면 그 이유가 FailedAs 에 남고 더는 움직이지 않는다.
    public StepResult Step(Dir d)
    {
        // 이미 끝났으면(실패든 도착이든) 더 움직이지 않는다. 시간 만료도 끝난 것이다.
        if (Failed) return FailedAs ?? StepResult.HitWall;
        if (Solved) return StepResult.Reached;

        var next = Cursor + Delta(d);
        if (!Inside(next) || !IsOpen(Cursor, d))
        {
            FailedAs = StepResult.HitWall;
            FailedAt = Cursor;
            return StepResult.HitWall;
        }
        if (_visited.Contains(next))
        {
            FailedAs = StepResult.Backtracked;
            FailedAt = next;
            return StepResult.Backtracked;
        }

        Cursor = next;
        _visited.Add(next);
        _trail.Add(next);
        return Cursor == Goal ? StepResult.Reached : StepResult.Moved;
    }

    // 시간이 다했다 — 지금 칸에서 실패로 끝난다.
    public void TimeOut()
    {
        if (Failed || Solved) return;
        FailedAt = Cursor;
        TimedOut = true;
    }

    public bool TimedOut { get; private set; }
    public bool Failed => FailedAs.HasValue || TimedOut;

    // 두 칸 사이의 최단 경로(완전 미로라 유일하다). 못 가면 빈 목록.
    public List<Vector2I> ShortestPath(Vector2I from, Vector2I to)
    {
        var prev = new Dictionary<Vector2I, Vector2I>();
        var queue = new Queue<Vector2I>();
        var seen = new HashSet<Vector2I> { from };
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == to) break;
            foreach (Dir d in new[] { Dir.Up, Dir.Down, Dir.Left, Dir.Right })
            {
                if (!IsOpen(at, d)) continue;
                var n = at + Delta(d);
                if (!seen.Add(n)) continue;
                prev[n] = at;
                queue.Enqueue(n);
            }
        }

        var path = new List<Vector2I>();
        if (from != to && !prev.ContainsKey(to)) return path;
        for (var at = to; ; at = prev[at])
        {
            path.Add(at);
            if (at == from) break;
        }
        path.Reverse();
        return path;
    }

    public bool Inside(Vector2I p) => p.X >= 0 && p.Y >= 0 && p.X < Size && p.Y < Size;

    public static Vector2I Delta(Dir d) => d switch
    {
        Dir.Up => new Vector2I(0, -1),
        Dir.Down => new Vector2I(0, 1),
        Dir.Left => new Vector2I(-1, 0),
        _ => new Vector2I(1, 0),
    };

    private static Dir Opposite(Dir d) => d switch
    {
        Dir.Up => Dir.Down,
        Dir.Down => Dir.Up,
        Dir.Left => Dir.Right,
        _ => Dir.Left,
    };

    // 오늘의 미로 크기 · 제한시간 배율. DAY 가 갈수록 조금씩 조인다.
    public static int SizeForDay(int day)
    {
        var cfg = Config.Instance?.Data;
        int baseSize = cfg?.RepairMazeSize ?? 7;
        int grow = Mathf.FloorToInt(Mathf.Max(0, day - 1) * (cfg?.RepairMazeSizePerDay ?? 0.5f));
        return Mathf.Clamp(baseSize + grow, 3, 15);
    }

    public static float TimeScaleForDay(int day)
    {
        var cfg = Config.Instance?.Data;
        float scale = cfg?.RepairMazeTimeScale ?? 1f;
        float perDay = cfg?.RepairMazeTimeScalePerDay ?? 0.05f;
        return Mathf.Clamp(scale - perDay * Mathf.Max(0, day - 1), 0.5f, 2f);
    }
}
