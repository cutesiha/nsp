using System;
using System.Collections.Generic;
using Godot;
using NSP.Data;

namespace NSP.Core;

// 수리 승인 절차(G-2).
//
// 고장 수리가 걸리면 관리자 패드에 승인 요청이 뜬다.
//   ① "수리를 승인하시겠습니까?" — 제한 6초. 거절하거나 답하지 않으면 그 수리가 느려진다.
//   ② 승인하면 곧바로 미로가 뜬다. 성공하면 수리는 정상 진행, 실패하면 역시 느려진다.
// 기회는 한 번뿐이고, 요청이 겹치면 줄을 세워 하나씩 처리한다.
//
// 게임 시간은 멈추지 않는다 — 미로에 붙잡혀 CCTV 를 못 보는 것 자체가 대가다.
//
// 화면을 모른다. 패드(PadView)가 이 상태를 읽어 그리고, 결과만 여기로 돌려준다.
public static class RepairApprovalSystem
{
    public enum Phase { Idle, Asking, Maze, Result }

    public sealed class Request
    {
        public string RoomId = "";
        public string RoomName = "";
        // 느려질 대상. 벌점은 이 업무의 필요 게이지에 걸린다.
        public NSP.Facility.SpawnedTask Task;
    }

    public static Phase Current { get; private set; } = Phase.Idle;
    public static Request Active { get; private set; }
    public static RepairMaze Maze { get; private set; }
    // 남은 응답 시간(Asking) / 남은 미로 시간(Maze). 화면이 그대로 그린다.
    public static float SecondsLeft { get; private set; }
    public static float SecondsTotal { get; private set; }
    // 미로는 첫 방향키를 누른 순간부터 시간이 간다(뜨자마자 죽지 않게).
    public static bool MazeStarted { get; private set; }
    // 결과를 잠깐 보여 주는 동안 남은 시간.
    public static float ResultSeconds { get; private set; }
    public static bool LastApproved { get; private set; }
    public static bool LastSucceeded { get; private set; }

    // 결과가 정해진 순간(연출 · 효과음이 듣는다). (성공했는가, 방 id)
    public static event Action<bool, string> Resolved;

    private static readonly Queue<Request> _queue = new();
    private const float ResultHoldSeconds = 0.5f;

    public static int Pending => _queue.Count;
    public static bool Busy => Current != Phase.Idle;

    public static void ResetAll()
    {
        _queue.Clear();
        Current = Phase.Idle;
        Active = null;
        Maze = null;
        SecondsLeft = SecondsTotal = ResultSeconds = 0f;
        MazeStarted = false;
    }

    // 수리가 걸렸다 — 승인 요청을 줄에 세운다. 결번 개체가 수리 중이어도 똑같이 뜬다.
    public static void Enqueue(string roomId, string roomName, NSP.Facility.SpawnedTask task)
    {
        if (task == null || string.IsNullOrEmpty(roomId)) return;
        if (Active?.Task == task) return;
        foreach (var q in _queue) if (q.Task == task) return;
        _queue.Enqueue(new Request { RoomId = roomId, RoomName = roomName, Task = task });
    }

    public static void Tick(float delta)
    {
        switch (Current)
        {
            case Phase.Idle:
                if (_queue.Count > 0) BeginAsking(_queue.Dequeue());
                break;

            case Phase.Asking:
                SecondsLeft -= delta;
                if (SecondsLeft <= 0f) Finish(approved: false, succeeded: false);   // 무응답 = 거절
                break;

            case Phase.Maze:
                if (!MazeStarted) break;              // 첫 입력 전에는 시간이 가지 않는다
                SecondsLeft -= delta;
                if (SecondsLeft > 0f) break;
                Maze?.TimeOut();
                Finish(approved: true, succeeded: false);
                break;

            case Phase.Result:
                ResultSeconds -= delta;
                if (ResultSeconds <= 0f)
                {
                    Current = Phase.Idle;
                    Active = null;
                    Maze = null;
                }
                break;
        }
    }

    private static void BeginAsking(Request r)
    {
        Active = r;
        Current = Phase.Asking;
        SecondsTotal = SecondsLeft = Mathf.Max(1f, Config.Instance?.Data?.RepairApproveSeconds ?? 6f);
        Maze = null;
        MazeStarted = false;
    }

    // 관리자가 [예] 를 눌렀다 — 곧바로 미로.
    public static void Approve()
    {
        if (Current != Phase.Asking) return;
        int day = GameState.Instance?.CurrentDay ?? 1;
        Maze = RepairMaze.Generate(RepairMaze.SizeForDay(day), RepairMaze.TimeScaleForDay(day));
        SecondsTotal = SecondsLeft = Maze.TimeLimitSeconds;
        MazeStarted = false;
        Current = Phase.Maze;
    }

    // [아니오] — 그 수리가 느려진다.
    public static void Decline()
    {
        if (Current != Phase.Asking) return;
        Finish(approved: false, succeeded: false);
    }

    // 미로 입력 한 번. 첫 입력에서 시간이 흐르기 시작한다.
    public static void MazeInput(RepairMaze.Dir dir)
    {
        if (Current != Phase.Maze || Maze == null || Maze.Failed) return;
        MazeStarted = true;
        var r = Maze.Step(dir);
        if (r == RepairMaze.StepResult.Reached) Finish(approved: true, succeeded: true);
        else if (r is RepairMaze.StepResult.HitWall or RepairMaze.StepResult.Backtracked)
            Finish(approved: true, succeeded: false);
    }

    // 실패 · 성공이 정해졌다. 실패면 그 수리에 시간이 더 걸린다.
    private static void Finish(bool approved, bool succeeded)
    {
        LastApproved = approved;
        LastSucceeded = succeeded;
        string room = Active?.RoomId ?? "";

        if (!succeeded && Active?.Task != null)
        {
            float rate = Mathf.Max(0f, Config.Instance?.Data?.RepairDenyPenaltyRate ?? 0.5f);
            Active.Task.GaugeRequired *= 1f + rate;
            EventLog.Instance?.LogEvent(LogEventType.Neglect, "", room,
                approved ? $"⚠ {Active.RoomName} 수리 승인 절차 실패 — 수리 시간 증가"
                         : $"⚠ {Active.RoomName} 수리 미승인 — 수리 시간 증가");
        }

        Current = Phase.Result;
        ResultSeconds = ResultHoldSeconds;
        Resolved?.Invoke(succeeded, room);
    }
}
