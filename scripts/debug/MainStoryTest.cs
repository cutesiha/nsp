using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// DAY1~5 메인 스토리 검증 — NSP_MAIN_STORY_DAY1_DAY5.md §11 검수표를 코드로 옮긴 것.
//
//   godot --headless --path . res://scenes/debug/MainStoryTest.tscn
//
// 컷인을 실제로 그려 보지는 않는다(그쪽은 StoryCutinTest 가 104항목으로 지킨다).
// 여기서 보는 것은 "무엇이 언제 뽑히는가" 와 "대본이 설계 원칙을 지키는가" 다.
public partial class MainStoryTest : Node
{
    private int _pass, _fail;
    private FacilitySimulation _sim;

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null || GameState.Instance == null)
        { GD.PrintErr("오토로드를 찾지 못했습니다."); GetTree().Quit(1); return; }

        GD.Print("################ DAY1~5 메인 스토리 검증 ################");
        ParsedScript();
        DesignRules();
        Voices();
        DayQueues();
        MissingStaff();
        NoRepeat();
        RecordSplit();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ── 대본이 읽혔는가 ──────────────────────────────────────────────
    private void ParsedScript()
    {
        var all = StoryScript.Entries;
        GD.Print($"\n[A] 비트 {all.Count}개 — " +
                 $"core {all.Count(e => e.Kind == StoryBeatKind.Core)} · " +
                 $"pool {all.Count(e => e.Kind == StoryBeatKind.Pool)} · " +
                 $"react {all.Count(e => e.Kind == StoryBeatKind.React)}");
        Check("A 비트를 읽었다", all.Count >= 20);
        // §4 의 형식 설명에 든 예시까지 비트로 읽으면 안 된다.
        Check("A 산문 속 예시가 비트로 섞이지 않았다",
            all.All(e => e.Id.Length > 0 && !e.Id.Contains('<')));
        Check("A 모든 비트에 대사가 있다", all.All(e => e.Beat.Lines.Count > 0));
        Check("A 모든 대사에 말하는 사람과 글이 있다",
            all.All(e => e.Beat.Lines.All(l => l.SpeakerEmployeeId.Length > 0 && l.Text.Length > 0)));
        Check("A DAY1 자기소개 세 비트가 전부 core 다",
            new[] { "d1_intro_a", "d1_intro_b", "d1_intro_c" }
                .All(id => all.Any(e => e.Id == id && e.Kind == StoryBeatKind.Core && e.Day == 1)));
        Check("A 반응 비트는 day:any 다",
            all.Where(e => e.Kind == StoryBeatKind.React).All(e => e.Day == 0));

        // 조건 오타는 그 비트를 영영 안 뜨게 만든다 — 전부 아는 값인지 본다.
        string[] known = { "always", "ghost_seen", "death_yesterday", "faint_yesterday",
                           "isolated_yesterday", "core_behind", "core_ok", "record_bad", "record_good" };
        var unknown = all.Where(e => !known.Contains(e.When)).Select(e => e.Id + ":" + e.When).ToList();
        Check("A 모든 when 조건이 아는 값이다" + Tail(unknown), unknown.Count == 0);
    }

    // ── §2 설계 원칙 ─────────────────────────────────────────────────
    private void DesignRules()
    {
        var all = StoryScript.Entries;
        var roster = new System.Collections.Generic.HashSet<string>(_sim.GetEmployeeIds());
        GD.Print("");

        // §2.1 — 결번으로 분기하지 않는다. 조건에도, 대사에도 그 말이 없어야 한다.
        Check("B §2.1 결번 조건으로 갈리는 비트가 없다",
            all.All(e => !e.When.Contains("saboteur") && !e.When.Contains("결번")));
        Check("B §2.1 대사가 결번을 입에 올리지 않는다",
            all.All(e => e.Beat.Lines.All(l => !l.Text.Contains("결번"))));

        // §2.3 — 직원끼리의 대화. 관리자는 엿듣는 쪽이라 화자가 될 수 없다.
        var strangers = all.SelectMany(e => e.Beat.Lines).Select(l => l.SpeakerEmployeeId)
            .Where(id => !roster.Contains(id)).Distinct().ToList();
        Check("B §2.3 현 직원 6인만 말한다" + Tail(strangers), strangers.Count == 0);

        // §6 — 두 가지는 반드시 켜져 있어야 한다.
        Check("B §6 모든 비트가 근무를 멈춘다(PauseGameplay)", all.All(e => e.Beat.PauseGameplay));
        Check("B §6 모든 비트가 엿들은 대화를 멈춘다", all.All(e => e.Beat.SuppressAmbientDialogue));

        // need 가 비어 있으면 결원이 나도 걸러지지 않는다.
        Check("B 모든 비트에 need 가 적혀 있다", all.All(e => e.Need.Length > 0));
        var mismatched = all
            .Where(e => e.Need.Any(n => e.Beat.Lines.All(l => l.SpeakerEmployeeId != n)))
            .Select(e => e.Id).ToList();
        Check("B need 에 적힌 사람이 그 비트에서 실제로 말한다" + Tail(mismatched), mismatched.Count == 0);
    }

    // ── §7 말투 ──────────────────────────────────────────────────────
    private void Voices()
    {
        GD.Print("");
        var lines = StoryScript.Entries.SelectMany(e => e.Beat.Lines).ToList();
        string[] Of(string id) => lines.Where(l => l.SpeakerEmployeeId == id).Select(l => l.Text).ToArray();

        Check("C 고양이는 느낌표를 쓰지 않는다", Of("cat").All(t => !t.Contains('!')));
        Check("C 늑대는 느낌표를 쓰지 않는다", Of("wolf").All(t => !t.Contains('!')));
        Check("C 늑대는 물결을 쓰지 않는다", Of("wolf").All(t => !t.Contains('~')));
        Check("C 토끼의 느낌표는 한 대사에 셋 이하",
            Of("rabbit").All(t => t.Count(c => c == '!') <= 3));
    }

    // ── §11-1 : DAY 별로 뜨는가 ──────────────────────────────────────
    private void DayQueues()
    {
        GD.Print("");
        ResetWorld();
        Check("D DAY0 에는 스토리가 없다", StoryBeatSelector.BuildQueue(0).Count == 0);

        for (int day = 1; day <= 5; day++)
        {
            ResetWorld();
            SetDay(day);
            var q = StoryBeatSelector.BuildQueue(day);
            GD.Print($"[D] DAY{day} — {q.Count}개 : {string.Join(", ", q.Select(e => e.Id))}");
            Check($"D DAY{day} 에 스토리가 뜬다", q.Count > 0);
        }

        // core 는 조건이 맞으면 전부 들어간다.
        ResetWorld();
        SetDay(1);
        var d1 = StoryBeatSelector.BuildQueue(1).Select(e => e.Id).ToList();
        Check("D DAY1 자기소개 세 비트가 모두 들어간다",
            new[] { "d1_intro_a", "d1_intro_b", "d1_intro_c" }.All(d1.Contains));
    }

    // ── §11-2 · 3 : 죽거나 기절·격리된 직원이 낀 비트는 통째로 빠진다 ──
    private void MissingStaff()
    {
        GD.Print("");
        var cases = new (string Label, System.Action<EmployeeState> Apply)[]
        {
            ("사망", st => st.Alive = false),
            ("기절", st => st.Incapacitated = true),
            ("격리", st => st.Isolated = true),
        };

        foreach (var (label, apply) in cases)
        {
            ResetWorld();
            SetDay(1);
            int before = StoryBeatSelector.BuildQueue(1).Count;

            apply(_sim.GetEmployeeState("rabbit"));
            StoryBeatSelector.ResetRun();
            var after = StoryBeatSelector.BuildQueue(1).ToList();
            var leaked = after.Where(e => e.Need.Contains("rabbit")).Select(e => e.Id).ToList();

            GD.Print($"[E] 토끼 {label} — 이전 {before}개 → 이후 {after.Count}개");
            Check($"E 토끼 {label} 시 토끼가 든 비트가 빠진다" + Tail(leaked), leaked.Count == 0);
            Check($"E 토끼 {label} 이후에도 남은 비트로 진행된다", after.Count > 0);
            // 한 줄씩 빼지 않고 비트를 통째로 건너뛴다(§3).
            Check($"E 토끼 {label} 시 토끼 대사가 한 줄도 남지 않는다",
                after.All(e => e.Beat.Lines.All(l => l.SpeakerEmployeeId != "rabbit")));
        }
        ResetWorld();
    }

    // ── §11-4 : 같은 비트가 한 판에서 두 번 나오지 않는다 ────────────
    private void NoRepeat()
    {
        GD.Print("");
        ResetWorld();
        var seen = new System.Collections.Generic.List<string>();
        for (int day = 1; day <= 5; day++)
        {
            SetDay(day);
            foreach (var e in StoryBeatSelector.BuildQueue(day))
            {
                seen.Add(e.Id);
                StoryBeatSelector.MarkPlayed(e.Id);
            }
        }
        var dup = seen.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        GD.Print($"[F] 한 판에서 재생한 비트 {seen.Count}개 · 중복 {dup.Count}개");
        Check("F 같은 비트가 두 번 나오지 않는다" + Tail(dup), dup.Count == 0);
    }

    // ── §11-5 : record_bad / record_good 는 서로의 정확한 반대 ───────
    private void RecordSplit()
    {
        GD.Print("");
        ResetWorld();
        for (int day = 2; day <= 5; day++)
            Check($"G DAY{day} record_bad 와 record_good 가 동시에 참이 아니다",
                StoryBeatSelector.Matches("record_bad", day) != StoryBeatSelector.Matches("record_good", day));

        // 코어가 뒤처진 상태 — DAY4 는 DAY3 목표(58%)를 기준으로 본다.
        ResetWorld();
        SetDay(4);
        bool behindBad = StoryBeatSelector.Matches("record_bad", 4);
        GD.Print($"[G] 코어 {GameState.Instance.CoreProgress:0}% · DAY4 record_bad = {behindBad}");
        Check("G 코어가 목표에 못 미치면 record_bad 다", behindBad);

        GameState.Instance.AddCoreProgress(100f, "테스트");
        Check("G 코어를 채우면 record_good 이다", StoryBeatSelector.Matches("record_good", 4));

        // 사망자가 하나라도 있으면 코어와 무관하게 record_bad 다.
        GameState.Instance.RegisterKill();
        Check("G 사망자가 있으면 record_bad 다", StoryBeatSelector.Matches("record_bad", 4));
        ResetWorld();
    }

    // ── 공통 ─────────────────────────────────────────────────────────

    private void ResetWorld()
    {
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        EventLog.Instance?.ClearAll();
        StoryBeatSelector.ResetRun();
    }

    // CurrentDay 는 GoToNextDay 로만 올라간다 — 실제 경로를 그대로 쓴다.
    private void SetDay(int day)
    {
        while (GameState.Instance.CurrentDay < day) GameState.Instance.GoToNextDay();
    }

    private static string Tail(System.Collections.Generic.List<string> bad) =>
        bad.Count == 0 ? "" : " — " + string.Join(", ", bad);

    private void Check(string label, bool ok)
    {
        if (ok) _pass++; else _fail++;
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
    }
}
