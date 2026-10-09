using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// DAY1~5 메인 스토리 검증 — NSP_MAIN_STORY_DAY1_DAY5.md §24 검수표를 코드로 옮긴 것.
//
//   godot --headless --path . res://scenes/debug/MainStoryTest.tscn
//
// 컷인을 실제로 그려 보지는 않는다(그쪽은 StoryCutinTest 가 104항목으로 지킨다).
// 여기서 보는 것은 "대본이 규칙대로 읽히고, 누가 언제 뽑히는가" 다.
public partial class MainStoryTest : Node
{
    private int _pass, _fail;
    private FacilitySimulation _sim;

    private static readonly string[] Roster = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null || GameState.Instance == null)
        { GD.PrintErr("오토로드를 찾지 못했습니다."); GetTree().Quit(1); return; }

        GD.Print("################ DAY1~5 메인 스토리 검증 ################");
        ParsedScript();
        Day1Opening();
        Choices();
        GroupFeel();
        MissingStaff();
        GhostWitness();
        DesignRules();
        NoGameplayEffect();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ── 대본이 읽혔는가 ──────────────────────────────────────────────
    private void ParsedScript()
    {
        var all = StoryScript.Entries;
        GD.Print($"\n[A] 비트 {all.Count}개 — " +
                 $"core {all.Count(e => e.Kind == StoryBeatKind.Core)} · " +
                 $"react {all.Count(e => e.Kind == StoryBeatKind.React)} · " +
                 $"pool {all.Count(e => e.Kind == StoryBeatKind.Pool)}");
        foreach (var e in all)
            GD.Print($"    {e.Id,-22} day {(e.Day == 0 ? "any" : e.Day.ToString()),-3} {e.Kind,-5} " +
                     $"{(e.Beat.Group ? "group" : "pair"),-5} min {e.Beat.MinParticipants} · " +
                     $"{e.Beat.Lines.Count}줄" +
                     (e.Beat.Steps.Any(st => st.IsChoice) ? " · 선택 있음" : ""));

        Check("A 비트를 읽었다", all.Count >= 10);
        Check("A 산문 속 형식 예시가 비트로 섞이지 않았다",
            all.All(e => e.Id.Length > 0 && !e.Id.Contains('<')));
        Check("A 모든 비트가 집단 대화(group)다", all.All(e => e.Beat.Group));
        // §16 — 2인 pool 비트를 더 이상 쓰지 않는다.
        Check("A pool 비트가 남아 있지 않다", all.All(e => e.Kind != StoryBeatKind.Pool));
        Check("A DAY1 은 min 6 이다",
            all.Where(e => e.Day == 1).All(e => e.Beat.MinParticipants == 6));
        Check("A DAY2~5 · 반응 비트는 min 2 다",
            all.Where(e => e.Day != 1).All(e => e.Beat.MinParticipants == 2));
        // 배역(@witness 등)으로 적은 줄은 say(필수)로 둘 수 있다 — 맡을 사람이 없으면
        // 비트 자체가 성립하지 않는다("본 사람" 없이 목격담을 시작할 수는 없다).
        Check("A 직원 이름으로 적은 대사는 전부 say? 다(결원이면 그 줄만 빠진다)",
            all.SelectMany(e => AllLines(e.Beat))
               .All(l => l.Optional || StoryRoleCast.IsRole(l.SpeakerEmployeeId)));
    }

    // ── §24-1 · 2 · 3 · 6 : DAY1 의 시작 ─────────────────────────────
    private void Day1Opening()
    {
        GD.Print("");
        ResetWorld();
        SetDay(1);
        var q = StoryBeatSelector.BuildQueue(1);
        GD.Print($"[B] DAY1 재생 순서 : {string.Join(" → ", q.Select(e => e.Id))}");

        Check("B DAY1 첫 장면이 재난 직후다(자기소개가 아니다)",
            q.Count > 0 && q[0].Id == "d1_after_disaster");
        Check("B 자기소개는 그 뒤에 온다",
            q.Count > 1 && q[^1].Id == "d1_introductions");

        var opening = q.FirstOrDefault(e => e.Id == "d1_after_disaster");
        string text = opening == null ? "" : string.Join(" ", AllLines(opening.Beat).Select(l => l.Text));
        Check("B 상실 · 공포가 먼저 나온다",
            text.Contains("살아 있는 사람은 없는") || text.Contains("아무 소리도 안 나요"));
        // §24-3 — "재난 이후 새로 배정받았다" 는 말이 없어야 한다.
        var all = StoryScript.Entries.SelectMany(e => AllLines(e.Beat)).Select(l => l.Text).ToList();
        var banned = new[] { "배정", "새로 왔", "전출", "발령" };
        var hit = banned.Where(b => all.Any(t => t.Contains(b))).ToList();
        Check($"B 누구도 '새로 배정받았다'고 말하지 않는다{(hit.Count == 0 ? "" : " — " + string.Join(",", hit))}",
            hit.Count == 0);

        var intro = StoryScript.Entries.FirstOrDefault(e => e.Id == "d1_introductions");
        var speakers = intro == null ? new List<string>()
            : AllLines(intro.Beat).Select(l => l.SpeakerEmployeeId).Distinct().ToList();
        GD.Print($"[B] 자기소개 화자 {speakers.Count}명 : {string.Join(", ", speakers)}");
        Check("B 자기소개에 여섯 명이 전부 나온다", Roster.All(speakers.Contains));
    }

    // ── §24-4 · 5 · 9 · 10 : 관리자 선택지 ───────────────────────────
    private void Choices()
    {
        GD.Print("");
        var choices = StoryScript.Entries
            .SelectMany(e => e.Beat.Steps.Where(st => st.IsChoice).Select(st => (Owner: e, st.Choice)))
            .ToList();
        foreach (var (owner, c) in choices)
            GD.Print($"[C] {c.ChoiceId,-22} (day {owner.Day}) 선택지 {c.Options.Count}개 · " +
                     $"분기 {string.Join("/", c.Options.Select(o => o.Branch.Count))}줄");

        Check("C 선택지가 다섯 개(DAY1~5) 있다", choices.Count >= 5);
        Check("C 모든 선택에 선택지가 셋이다", choices.All(c => c.Choice.Options.Count == 3));
        Check("C 모든 선택지에 분기 반응이 붙어 있다",
            choices.All(c => c.Choice.Options.All(o => o.Branch.Count > 0)));
        Check("C 모든 선택지에 branch_id 가 읽혔다",
            choices.All(c => c.Choice.Options.All(o => o.BranchId.Length > 0)));

        for (int day = 1; day <= 5; day++)
        {
            int d = day;
            Check($"C DAY{day} 에 관리자 선택이 있다", choices.Any(c => c.Owner.Day == d));
        }

        var lead = choices.FirstOrDefault(c => c.Choice.ChoiceId == "d1_leadership").Choice;
        Check("C 첫 선택지에 그 문장이 그대로 있다",
            lead != null && lead.Options[0].Text == "제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다.");
        Check("C 첫 선택의 prompt 가 읽혔다", lead != null && lead.Prompt.Contains("기다리고 있습니다"));
        // §24-5 — 고른 뒤 여섯이 각자 반응한다.
        foreach (var o in lead?.Options ?? new List<StoryChoiceOption>())
        {
            var who = o.Branch.Select(l => l.SpeakerEmployeeId).Distinct().ToList();
            Check($"C '{o.BranchId}' 반응에 여섯 명이 다 나온다 ({who.Count}명)", Roster.All(who.Contains));
        }
    }

    // ── §24-7 · 8 : 한 테이블의 대화인가 ─────────────────────────────
    private void GroupFeel()
    {
        GD.Print("");
        foreach (var e in StoryScript.Entries.Where(e => e.Kind == StoryBeatKind.Core && e.Day >= 2))
        {
            var lines = e.Beat.Steps.Where(st => st.Line != null).Select(st => st.Line).ToList();
            int who = lines.Select(l => l.SpeakerEmployeeId).Distinct().Count();
            // 2인 대화를 이어 붙인 모양이면 "같은 두 사람이 네 줄 내리" 가 보인다.
            int longestPair = LongestTwoPersonRun(lines);
            GD.Print($"[D] {e.Id,-22} 화자 {who}명 · 같은 두 사람만 이어진 최대 {longestPair}줄");
            Check($"D {e.Id} 에 네 명 이상이 참여한다", who >= 4);
            Check($"D {e.Id} 가 2인 대화를 이어 붙인 모양이 아니다", longestPair <= 5);
        }
    }

    // 같은 두 사람만 주고받는 구간의 최대 길이.
    private static int LongestTwoPersonRun(List<StoryLine> lines)
    {
        int best = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var seen = new HashSet<string>();
            for (int j = i; j < lines.Count; j++)
            {
                seen.Add(lines[j].SpeakerEmployeeId);
                if (seen.Count > 2) break;
                best = Mathf.Max(best, j - i + 1);
            }
        }
        return best;
    }

    // ── §24-11 · 12 : 결원 ───────────────────────────────────────────
    private void MissingStaff()
    {
        GD.Print("");
        var main = StoryScript.Entries.First(e => e.Id == "d2_main_group");
        var cases = new (string Label, System.Action Apply, System.Action Undo)[]
        {
            ("사망", () => _sim.GetEmployeeState("sheep").Alive = false,
                     () => _sim.GetEmployeeState("sheep").Alive = true),
            ("기절", () => _sim.GetEmployeeState("sheep").Incapacitated = true,
                     () => _sim.GetEmployeeState("sheep").Incapacitated = false),
            ("격리", () => _sim.GetEmployeeState("sheep").Isolated = true,
                     () => _sim.GetEmployeeState("sheep").Isolated = false),
        };

        foreach (var (label, apply, undo) in cases)
        {
            ResetWorld();
            SetDay(2);
            apply();
            var lines = AllLines(main.Beat).Where(StoryCutinDirector.Speaks).ToList();
            bool sheepGone = lines.All(l => l.SpeakerEmployeeId != "sheep");
            bool othersStay = lines.Count(l => l.SpeakerEmployeeId == "cat") > 0;
            GD.Print($"[E] 양 {label} — 남은 줄 {lines.Count} · 양 줄 {(sheepGone ? "없음" : "남음")}");
            Check($"E 양 {label} 시 그 줄만 빠진다", sheepGone);
            Check($"E 양 {label} 이어도 나머지 대화는 이어진다", othersStay && lines.Count >= 5);
            Check($"E 양 {label} 이어도 비트는 재생된다(min 2)", StoryBeatSelector.IsPlayable(main));
            undo();
        }

        // 남은 사람이 둘 미만이면 대화 자체가 성립하지 않는다.
        ResetWorld();
        SetDay(2);
        foreach (string id in Roster.Take(5)) _sim.GetEmployeeState(id).Alive = false;
        Check("E 한 명만 남으면 비트가 통째로 생략된다", !StoryBeatSelector.IsPlayable(main));
        ResetWorld();
    }

    // ── 이상 개체 목격담 : 본 사람만 봤다고 말한다 ───────────────────
    //
    // 괴물은 그 작업실에 있던 사람만 본다. 어제 기록(목격자 명단)이 그대로 배역이 되어야
    // 하고, 보지 못한 사람은 "소리만 들었다" 쪽 줄을 맡아야 한다.
    private void GhostWitness()
    {
        GD.Print("");
        // DAY1 에 기록을 남기고 DAY2 로 넘어간다 — 비트가 보는 것은 **어제** 기록이다.
        ResetWorld();
        var log = EventLog.Instance;
        log.LogEvent(NSP.Data.LogEventType.AnomalyDispelled, "", "power_room",
            "👁 발전실 — 관측으로 이상 개체 소멸", witnesses: new[] { "cat" });   // 고양이만 그 방에 있었다
        SetDay(2);

        var cast = StoryRoleCast.ForDay(2);
        Check("H 본 사람이 @witness 가 된다", cast.Resolve("@witness") == "cat");
        var heard = new[] { "@heard1", "@heard2", "@heard3", "@heard4", "@heard5" }
            .Select(cast.Resolve).Where(s => s.Length > 0).ToList();
        Check("H 못 본 사람만 @heard 가 된다", heard.Count == 5 && !heard.Contains("cat"));
        Check("H @heard 는 서로 다른 사람이다", heard.Distinct().Count() == heard.Count);

        var beat = StoryScript.Entries.First(e => e.Id == "d2_ghost_group");
        Check("H 목격자가 있으면 목격담 비트가 재생된다", StoryBeatSelector.IsPlayable(beat, 2));
        var spoken = AllLines(beat.Beat).Select(l => cast.Apply(l))
            .Where(l => l != null && StoryCutinDirector.Speaks(l)).ToList();
        Check("H 봤다고 말하는 줄은 전부 그 사람 것이다",
            spoken.Where(l => l.Text.Contains("봤") || l.Text.Contains("눈앞"))
                  .All(l => l.SpeakerEmployeeId == "cat"));
        Check("H 소리만 들었다는 줄은 본 사람이 말하지 않는다",
            spoken.Where(l => l.Text.Contains("들었"))
                  .All(l => l.SpeakerEmployeeId != "cat"));

        // 본 사람이 아무도 없으면(기록에 목격자가 없으면) 목격담은 뜨지 않는다.
        ResetWorld();
        EventLog.Instance.LogEvent(NSP.Data.LogEventType.AnomalyDispelled, "", "power_room",
            "👁 발전실 — 관측으로 이상 개체 소멸");
        SetDay(2);
        Check("H 본 사람이 없으면 목격담 비트가 생략된다", !StoryBeatSelector.IsPlayable(beat, 2));
        ResetWorld();
    }

    // ── §24-14 : 결번 분기 없음 ──────────────────────────────────────
    private void DesignRules()
    {
        GD.Print("");
        var all = StoryScript.Entries;
        Check("F 결번 조건으로 갈리는 비트가 없다",
            all.All(e => !e.When.Contains("saboteur") && !e.When.Contains("결번")));
        Check("F 대사가 결번을 입에 올리지 않는다",
            all.SelectMany(e => AllLines(e.Beat)).All(l => !l.Text.Contains("결번")));
        var roster = new HashSet<string>(_sim.GetEmployeeIds());
        // 배역(@witness · @heard1 …)은 재생할 때 실제 직원으로 바뀐다 — 명부에 없는 것이 맞다.
        var strangers = all.SelectMany(e => AllLines(e.Beat)).Select(l => l.SpeakerEmployeeId)
            .Where(id => !roster.Contains(id) && !StoryRoleCast.IsRole(id)).Distinct().ToList();
        Check($"F 현 직원 6인만 말한다{(strangers.Count == 0 ? "" : " — " + string.Join(",", strangers))}",
            strangers.Count == 0);
        Check("F 모든 비트가 근무를 멈춘다", all.All(e => e.Beat.PauseGameplay));
        Check("F 모든 비트가 엿들은 대화를 멈춘다", all.All(e => e.Beat.SuppressAmbientDialogue));
        // record_bad / record_good 은 서로의 정확한 반대다.
        for (int day = 2; day <= 5; day++)
            Check($"F DAY{day} record_bad 와 record_good 가 동시에 참이 아니다",
                StoryBeatSelector.Matches("record_bad", day) != StoryBeatSelector.Matches("record_good", day));
    }

    // ── §24-13 : 선택이 수치를 바꾸지 않는다 ─────────────────────────
    private void NoGameplayEffect()
    {
        GD.Print("");
        ResetWorld();
        SetDay(1);
        var gs = GameState.Instance;
        float core = gs.CoreProgress;
        int kills = gs.TotalKills;
        float stress = _sim.GetEmployeeState("rabbit").Stress;
        int rel = RelationshipSystem.PairScore("cat", "dog");

        StoryChoiceHistory.ResetRun();
        StoryChoiceHistory.Record(1, "d1_leadership", 0, "제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다.");

        Check("G 고른 답이 기록된다", StoryChoiceHistory.ChosenIndex("d1_leadership") == 0);
        Check("G 코어 복구율이 그대로다", Mathf.IsEqualApprox(gs.CoreProgress, core));
        Check("G 사망 수가 그대로다", gs.TotalKills == kills);
        Check("G 스트레스가 그대로다", Mathf.IsEqualApprox(_sim.GetEmployeeState("rabbit").Stress, stress));
        Check("G 관계도가 그대로다", RelationshipSystem.PairScore("cat", "dog") == rel);
        StoryChoiceHistory.ResetRun();
        Check("G 새 판에서 기록이 비워진다", StoryChoiceHistory.Entries.Count == 0);
    }

    // ── 공통 ─────────────────────────────────────────────────────────

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

    private void ResetWorld()
    {
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        EventLog.Instance?.ClearAll();
        StoryBeatSelector.ResetRun();
    }

    private void SetDay(int day)
    {
        while (GameState.Instance.CurrentDay < day) GameState.Instance.GoToNextDay();
    }

    private void Check(string label, bool ok)
    {
        if (ok) _pass++; else _fail++;
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
    }
}
