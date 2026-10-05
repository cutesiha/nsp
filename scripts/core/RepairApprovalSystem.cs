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

    // 요청이 새로 뜬 순간(연출 · 효과음 · 확대 해제가 듣는다). (방 id, 방 이름)
    //
    // 이 신호가 없으면 요청은 책상 위 패드에만 조용히 뜬다. 모니터를 확대해 두고 있으면
    // 패드가 화면 밖이라 제한 시간이 그대로 흘러가 버린다 — 그래서 요청은 반드시 알린다.
    public static event Action<string, string> Opened;

    // 결과가 정해진 순간(연출 · 효과음이 듣는다). (성공했는가, 방 id)
    public static event Action<bool, string> Resolved;

    // 시간만 멈춘다(요청은 그대로 떠 있고, [예]·방향키도 그대로 받는다).
    // DAY0 교육이 "이게 승인 요청입니다" 를 설명하는 동안 제한 시간이 흐르지 않게 하려고 둔다.
    // 교육 밖에서는 아무도 켜지 않는다.
    public static bool Paused { get; set; }

    // 줄에 세워만 두고 **띄우지 않는다**. DAY0 교육이 "사고 → 사람을 보낸다 → 그제서야 승인 요청"
    // 순서를 만들려고 쓴다. 교육 밖에서는 아무도 켜지 않는다.
    public static bool Held { get; set; }

    // 이 수리가 아직 승인 절차를 기다리는 중인가. 그동안은 수리 게이지가 차지 않는다 —
    // 승인하기도 전에 직원들이 알아서 고쳐 버리면 이 절차가 아무 의미가 없다.
    public static bool IsAwaiting(NSP.Facility.SpawnedTask task)
    {
        if (task == null) return false;
        if (Active?.Task == task && Current is Phase.Asking or Phase.Maze) return true;
        foreach (var q in _queue) if (q.Task == task) return true;
        return false;
    }

    private static readonly Queue<Request> _queue = new();
    private const float ResultHoldSeconds = 0.5f;

    public static int Pending => _queue.Count;
    public static bool Busy => Current != Phase.Idle;

    public static void ResetAll()
    {
        _queue.Clear();
        Paused = false;
        Held = false;
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
                if (!Held && _queue.Count > 0) BeginAsking(_queue.Dequeue());
                break;

            case Phase.Asking:
                if (Paused) break;
                SecondsLeft -= delta;
                if (SecondsLeft <= 0f) Finish(approved: false, succeeded: false);   // 무응답 = 거절
                break;

            case Phase.Maze:
                if (!MazeStarted || Paused) break;     // 첫 입력 전에는 시간이 가지 않는다
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
        Opened?.Invoke(r.RoomId, r.RoomName);
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
        if (Current != Phase.Maze || Maze == null || Maze.Failed || Paused) return;
        MazeStarted = true;
        var r = Maze.Step(dir);
        // 한 칸 움직일 때마다 짧은 소리 — 눌렸는지 아닌지 손끝으로 알 수 있어야 한다.
        // 칸마다 음을 조금씩 올려 "앞으로 가고 있다"가 귀로도 들리게 한다.
        if (r == RepairMaze.StepResult.Moved)
        {
            float climb = 0.92f + Mathf.Min(0.26f, Maze.Trail.Count * 0.02f);
            Sfx.Instance?.Play("tick", -11f, climb);
        }
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
        // 결과를 귀로도 알린다. 미로는 패드 화면 안에서 끝나므로, 소리가 없으면
        // 풀었는지 틀렸는지 글자를 읽어야만 안다.
        if (succeeded) Sfx.Instance?.Play("task_done", -5f);
        else if (!approved) Sfx.Instance?.Play("switch_fail", -6f);          // [아니오] · 무응답
        else Sfx.Instance?.Play("power_down", -7f, 1.15f);                   // 승인했지만 미로 실패

        Current = Phase.Result;
        ResultSeconds = ResultHoldSeconds;
        Resolved?.Invoke(succeeded, room);
    }

    // 근무가 끝났다 — 떠 있던 요청과 미로를 닫는다.
    //
    // 이게 없으면 미로를 푸는 도중에 근무가 자동 종료될 때 패드가 미로 화면에 멈춰 선다:
    // 시뮬레이션이 멈춰 Tick 이 돌지 않으니 제한 시간도 흐르지 않고, 화면은 Busy 라 안 닫힌다.
    // 벌점은 매기지 않는다 — 수리 자체가 근무와 함께 사라지므로 때릴 대상이 없다.
    public static void AbortForShiftEnd()
    {
        if (Current == Phase.Idle && _queue.Count == 0) return;
        _queue.Clear();
        Current = Phase.Idle;
        Active = null;
        Maze = null;
        SecondsLeft = SecondsTotal = ResultSeconds = 0f;
        MazeStarted = false;
    }
}
