using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// DAY 시작에 재생할 스토리 비트를 골라 차례로 돌린다(NSP_MAIN_STORY_DAY1_DAY5.md §5 · §6).
//
// 판정은 하나도 만들지 않는다 — 조건은 전부 기존 시스템의 값을 **읽기만** 한다.
//   어제 무슨 일이 있었나 → EventLog (LogEntry.Day 로 전날만 추려 본다)
//   성적이 어떤가       → GameState.CoreProgress · TotalKills · DayObjectives 의 그날 목표
//   누가 일할 수 있나    → EmployeeState.Alive / Incapacitated / Isolated
//
// 스토리가 실패해도 게임이 잠기면 안 된다(STORY_TUTORIAL_REWORK §38). 이 클래스에서
// 나가는 모든 길은 "그냥 스토리 없이 배치 화면으로" 로 끝난다.
public static class StoryBeatSelector
{
    // 한 비트가 이 시간을 넘겨도 끝나지 않으면 강제로 걷는다. 넘기는 속도는 플레이어가
    // 정하므로 넉넉히 둔다 — 느리게 읽는 사람을 끊으려는 값이 아니라, 컷인이 끝나지
    // 못하는 버그가 났을 때 배치 화면으로 돌아갈 길을 열어 두려는 값이다.
    private const double BeatTimeoutSeconds = 300.0;

    // 한 판에서 이미 재생한 비트. 같은 비트는 두 번 나오지 않는다(§6).
    private static readonly HashSet<string> _played = new();
    private static readonly Random _rng = new();

    public static void ResetRun() => _played.Clear();

    // 이미 재생한 비트. 같은 판에서 다시 뽑히지 않는다.
    public static IReadOnlyCollection<string> PlayedIds => _played;

    public static void MarkPlayed(string beatId)
    {
        if (!string.IsNullOrEmpty(beatId)) _played.Add(beatId);
    }

    // 그 DAY 에 재생할 큐. 화면에 띄우지 않고 목록만 만든다(검사에서 그대로 쓴다).
    public static List<StoryBeatEntry> BuildQueue(int day)
    {
        var queue = new List<StoryBeatEntry>();
        if (day <= 0) return queue;   // DAY0 교육에는 메인 스토리가 없다

        bool Usable(StoryBeatEntry e) =>
            !_played.Contains(e.Id) && (e.Day == 0 || e.Day == day)
            && IsPlayable(e) && Matches(e.When, day);

        // ① 어제 일에 대한 반응 — 맨 앞에 하나만(문서 §16).
        var reacts = StoryScript.Entries.Where(e => e.Kind == StoryBeatKind.React && Usable(e)).ToList();
        if (reacts.Count > 0) queue.Add(reacts[_rng.Next(reacts.Count)]);

        // ② 그 DAY 의 집단 대화 — 문서에 적힌 순서대로 전부. 관리자 선택(@choice)은
        //    바로 앞 비트 안에 한 걸음으로 들어가 있으므로 여기서 따로 뽑지 않는다.
        queue.AddRange(StoryScript.Entries.Where(e => e.Kind == StoryBeatKind.Core && Usable(e)));

        // 예전에는 여기서 2인 대화(pool) 비트를 두셋 더 무작위로 끼워 넣었다.
        // 그러면 "A·B 네 줄 → C·D 네 줄" 이 이어 붙어 한 테이블의 대화로 읽히지 않고,
        // DAY 시작이 매번 길어진다(문서 §16 · §25). 그래서 뽑지 않는다.
        return queue;
    }

    // DAY 시작에 한 번. 재생할 것이 없으면 아무 일도 하지 않고 곧바로 돌아온다(에러 아님).
    public static async Task PlayDayStart(Node ctx)
    {
        try
        {
            int day = GameState.Instance?.CurrentDay ?? 0;
            if (day <= 0 || ctx == null || !GodotObject.IsInstanceValid(ctx)) return;

            var queue = BuildQueue(day);
            if (queue.Count == 0) return;

            // 그 DAY 의 스토리 전체가 **모니터2 안**에서 돈다(연출 규칙 §1 · §19).
            // 비트마다 확대를 넣었다 풀지 않는다 — 한 번 들어가서 끝까지 보고 나온다.
            StoryCutinDirector.Instance?.EnterMonitor();
            foreach (var entry in queue)
            {
                var dir = StoryCutinDirector.Instance;
                if (dir == null || !GodotObject.IsInstanceValid(dir)) return;
                if (!dir.CanPlay) return;   // 통화 중 등 — 남은 비트는 다음 날로 넘기지 않고 접는다

                MarkPlayed(entry.Id);
                // 메인 스토리는 **모니터2 영상 안**에서 돈다 — 제어실 화면 위에 스탠딩이
                // 직접 뜨지 않는다(연출 규칙 §3). 카메라 확대 · 복귀는 디렉터가 맡는다.
                if (!await PlayWithTimeout(ctx, dir, entry.Beat)) return;
                if (!GodotObject.IsInstanceValid(ctx)) return;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"StoryBeatSelector: 스토리를 건너뜁니다 — {e.Message}");
            StoryCutinDirector.Instance?.Abort();
        }
        finally
        {
            // 어떻게 끝났든 모니터 확대는 반드시 풀린다 — 남아 있으면 그 뒤로
            // 배치도 근무도 할 수 없다(§17 · §18).
            StoryCutinDirector.Instance?.ExitMonitor();
        }
    }

    // 컷인이 끝나지 못하는 상황에서도 반드시 돌아온다(§10.5 fail-safe).
    private static async Task<bool> PlayWithTimeout(Node ctx, StoryCutinDirector dir, StoryBeat beat)
    {
        var play = dir.Play(beat);
        double left = BeatTimeoutSeconds;
        while (!play.IsCompleted && left > 0.0 && GodotObject.IsInstanceValid(ctx))
        {
            await ctx.ToSignal(ctx.GetTree(), SceneTree.SignalName.ProcessFrame);
            left -= ctx.GetProcessDeltaTime();
        }
        if (play.IsCompleted) return true;

        GD.PushWarning($"StoryBeatSelector: 컷인이 끝나지 않아 강제로 닫습니다 (beat={beat.BeatId}).");
        dir.Abort();
        return false;
    }

    // ── 재생 가능 판정 ───────────────────────────────────────────────
    //
    // need 에 적힌 직원이 전부 살아 있고, 기절도 격리도 아니어야 한다.
    // 대사를 한 줄씩 빼지 않고 비트를 통째로 건너뛴다 — 남은 줄이 허공에 대답하게 된다(§3).
    public static bool IsPlayable(StoryBeatEntry entry)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return false;

        // 대본에 없는 직원이 적혀 있으면 그 비트만 버린다(§10.5).
        foreach (var line in AllLines(entry.Beat))
            if (line.SpeakerEmployeeId.Length > 0 && sim.GetEmployeeDef(line.SpeakerEmployeeId) == null)
                return false;

        foreach (string id in entry.Need)
            if (!StoryCutinDirector.IsPresent(id)) return false;

        // ── 집단 대화(group: true) ────────────────────────────────────
        //
        // 한 테이블에 둘러앉아 같은 주제로 나누는 대화라, 한 명이 빠졌다고 통째로 버리지
        // 않는다. 대신 두 가지를 본다.
        //   · say  로 적힌 사람은 그 대화의 기둥이다 — 자리에 없으면 비트를 접는다
        //     (그 줄만 빼면 남은 줄이 허공에 대답하게 되고, 그렇다고 읽히면 죽은 사람이 말한다)
        //   · say? 로 적힌 사람은 빠져도 된다 — 그 줄만 건너뛴다(StoryCutinDirector.Speaks)
        // 그렇게 추리고 남은 사람이 min 보다 적으면 대화 자체가 성립하지 않는다.
        if (!entry.Beat.Group) return true;

        var present = new HashSet<string>();
        foreach (var line in AllLines(entry.Beat))
        {
            string who = line.SpeakerEmployeeId;
            if (who.Length == 0) continue;
            if (StoryCutinDirector.IsPresent(who)) present.Add(who);
            else if (!line.Optional) return false;
        }
        return present.Count >= entry.Beat.MinParticipants;
    }

    // 본 대화 + 선택지 분기에 든 모든 대사 줄.
    private static IEnumerable<StoryLine> AllLines(StoryBeat beat)
    {
        foreach (var step in beat.Steps)
        {
            if (step.Line != null) yield return step.Line;
            if (step.Choice == null) continue;
            foreach (var opt in step.Choice.Options)
            foreach (var l in opt.Branch) yield return l;
        }
    }

    // ── when 조건 ────────────────────────────────────────────────────
    public static bool Matches(string when, int day) => when switch
    {
        "" or "always" => true,
        "ghost_seen" => Yesterday(day, e => e.EventType is LogEventType.AnomalyDispelled
                                                       or LogEventType.AnomalyIncident),
        "death_yesterday" => Yesterday(day, e => e.EventType == LogEventType.Death),
        "faint_yesterday" => Yesterday(day, e => e.Detail == LogDetail.Fainted),
        "isolated_yesterday" => Yesterday(day, e => e.EventType == LogEventType.Isolation),
        "core_behind" => CoreBehind(day),
        "core_ok" => !CoreBehind(day),
        "record_bad" => RecordBad(day),
        "record_good" => !RecordBad(day),
        // 모르는 조건은 그 비트만 버린다 — 문서 오타가 엉뚱한 날에 대사를 띄우지 않게.
        _ => false,
    };

    // 어제(= day - 1) 기록에 그런 줄이 있었는가.
    private static bool Yesterday(int day, Func<LogEntry, bool> match)
    {
        var log = EventLog.Instance;
        if (log == null || day <= 1) return false;
        foreach (var e in log.GetAllEntries())
            if (e.Day == day - 1 && match(e)) return true;
        return false;
    }

    // 어제까지 끝냈어야 할 코어 복구율에 못 미치는가. 첫날은 견줄 어제가 없다.
    private static bool CoreBehind(int day)
    {
        if (day <= 1) return false;
        var plan = DayObjectives.For(day - 1);
        if (plan == null) return false;
        float target = 0f;
        foreach (var obj in plan.Objectives)
            if (obj != null && obj.Type == DayObjectiveType.CoreProgress)
                target = Mathf.Max(target, obj.TargetValue);
        if (target <= 0f) return false;
        return (GameState.Instance?.CoreProgress ?? 0f) < target - 0.0001f;
    }

    // 관리자의 성적. record_good 은 이것의 정확한 반대다 — 둘이 같이 뜨는 일은 없다(§11.5).
    private static bool RecordBad(int day) =>
        (GameState.Instance?.TotalKills ?? 0) > 0 || CoreBehind(day);
}
