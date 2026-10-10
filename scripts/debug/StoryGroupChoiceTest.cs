using System.Linq;
using Godot;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 집단 대화(group) · 선택 가능한 줄(say?) · 관리자 선택(@choice/@branch) 런타임 검증.
//
//   godot --headless --path . res://scenes/debug/StoryGroupChoiceTest.tscn
//
// 대본은 검사 안에서 글자로 만들어 쓴다 — 저장소에 가짜 대본 파일을 두지 않는다.
// 여기서 보는 것은 **문법이 규칙대로 해석되는가** 뿐이고, 실제 DAY1~5 대사는
// docs/NSP_MAIN_STORY_DAY1_DAY5.md 가 가진다.
public partial class StoryGroupChoiceTest : Node
{
    private int _pass, _fail;
    private FacilitySimulation _sim;

    // 집단 대화 한 덩어리 — 여섯이 같은 주제로 끼어들고, 중간에 관리자에게 답을 묻는다.
    private const string Script = @"
@beat t_group
day: 1
need:
when: always
kind: core
group: true
min: 6
say:  rabbit | bad    | L | 이제 어떻게 되는 거예요?
say?: sheep  | bad    | R | 저, 저는 잘 모르겠어요...
say:  cat    | normal | R | 묻는다고 답이 나오나요.
say?: dog    | smile  | L | 일단 다들 앉아서 얘기해요.
@choice t_lead
prompt: 관리자님, 뭐라고 답하시겠습니까?
option: 제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다.
option: 아직 아무것도 약속할 수 없습니다.
option: 여러분이 직접 정하십시오.
@branch
say:  wolf   | normal | L | 알겠습니다.
say?: fox    | smile  | R | 든든하네요~
@endbranch
@branch
say:  cat    | bad    | L | 그 말이 제일 정직하긴 하네요.
@endbranch
@branch
say:  fox    | normal | L | 저희더러 정하라고요~?
@endbranch
say:  wolf   | normal | R | 그럼 움직이겠습니다.

@beat t_small
day: 2
need:
when: always
kind: core
group: true
min: 2
say?: rabbit | normal | L | 둘만 남았네요.
say?: cat    | normal | R | 그러게요.
";

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); GetTree().Quit(1); return; }
        NSP.Core.GameState.Instance?.ResetRun(1);
        _sim.ResetRun();

        var beats = StoryScript.ParseText(Script);
        GD.Print("################ 집단 대화 · 선택지 검증 ################");
        GD.Print($"\n[A] 비트 {beats.Count}개");
        Check("A 비트 두 개를 읽었다", beats.Count == 2);
        if (beats.Count < 2) { Done(); return; }

        var group = beats[0];
        // ── 문법 ────────────────────────────────────────────────────
        Check("A group: true 가 읽혔다", group.Beat.Group);
        Check("A min: 6 이 읽혔다", group.Beat.MinParticipants == 6);
        var lines = group.Beat.Lines;
        GD.Print($"[A] 본 대화 {lines.Count}줄 · 차례 {group.Beat.Steps.Count}걸음");
        Check("A 본 대화가 다섯 줄이다", lines.Count == 5);
        Check("A say? 줄만 Optional 로 표시된다",
            lines.Count(l => l.Optional) == 2
            && lines.Where(l => l.Optional).All(l => l.SpeakerEmployeeId is "sheep" or "dog"));
        Check("A 선택이 대사 사이에 끼어 있다",
            group.Beat.Steps.Count(st => st.IsChoice) == 1 && group.Beat.Steps[4].IsChoice);

        var choice = group.Beat.Steps[4].Choice;
        GD.Print($"[A] 선택 '{choice.ChoiceId}' — 선택지 {choice.Options.Count}개");
        Check("A @choice 의 id 가 읽혔다", choice.ChoiceId == "t_lead");
        Check("A prompt 가 읽혔다", choice.Prompt.Contains("뭐라고 답하시겠습니까"));
        Check("A 선택지가 셋이다", choice.Options.Count == 3);
        Check("A 첫 선택지가 대본 그대로다",
            choice.Options[0].Text == "제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다.");
        Check("A 번호 없는 @branch 가 차례대로 붙는다",
            choice.Options[0].Branch.Count == 2 && choice.Options[1].Branch.Count == 1
            && choice.Options[2].Branch.Count == 1);
        Check("A 분기 안의 say? 도 Optional 이다",
            choice.Options[0].Branch[1] is { SpeakerEmployeeId: "fox", Optional: true });
        Check("A 선택 뒤에도 본 대화가 이어진다",
            group.Beat.Steps[^1].Line is { SpeakerEmployeeId: "wolf" });

        // ── 결원 처리 ────────────────────────────────────────────────
        GD.Print("\n[B] 결원");
        Check("B 여섯 다 있으면 재생된다", StoryBeatSelector.IsPlayable(group));

        // say? 화자(양)가 빠져도 비트는 살아 있고 그 줄만 빠진다 — 단 min 6 은 못 채운다.
        foreach (var (label, apply, undo) in Cases())
        {
            apply();
            bool playable = StoryBeatSelector.IsPlayable(group);
            bool sheepSpeaks = StoryCutinDirector.Speaks(lines.First(l => l.SpeakerEmployeeId == "sheep"));
            bool catSpeaks = StoryCutinDirector.Speaks(lines.First(l => l.SpeakerEmployeeId == "cat"));
            GD.Print($"    양 {label} — min6 비트 재생 {playable} · 양 줄 {sheepSpeaks} · 고양이 줄 {catSpeaks}");
            Check($"B 양 {label} 시 그 say? 줄이 빠진다", !sheepSpeaks);
            Check($"B 양 {label} 이어도 다른 줄은 그대로다", catSpeaks);
            Check($"B 양 {label} 이면 min 6 을 못 채워 비트가 생략된다", !playable);
            undo();
        }

        // min 2 짜리는 한 명이 빠져도 남은 한 명으로는 모자라고, 둘 다 있으면 재생된다.
        var small = beats[1];
        Check("B min 2 비트는 둘 다 있으면 재생된다", StoryBeatSelector.IsPlayable(small));
        _sim.GetEmployeeState("cat").Alive = false;
        Check("B min 2 비트는 한 명만 남으면 생략된다", !StoryBeatSelector.IsPlayable(small));
        _sim.GetEmployeeState("cat").Alive = true;

        // say(필수) 화자가 빠지면 비트를 접는다 — 죽은 직원이 말하는 일이 없어야 한다.
        _sim.GetEmployeeState("wolf").Alive = false;
        Check("B say(필수) 화자가 죽으면 비트 전체가 생략된다", !StoryBeatSelector.IsPlayable(group));
        _sim.GetEmployeeState("wolf").Alive = true;

        // ── 선택 기록 ────────────────────────────────────────────────
        GD.Print("\n[C] 선택 기록");
        StoryChoiceHistory.ResetRun();
        float coreBefore = NSP.Core.GameState.Instance.CoreProgress;
        StoryChoiceHistory.Record(1, choice.ChoiceId, 0, choice.Options[0].Text);
        Check("C 고른 답이 기록된다", StoryChoiceHistory.ChosenIndex("t_lead") == 0);
        Check("C 기록에 그 문장이 남는다",
            StoryChoiceHistory.Entries[^1].OptionText.StartsWith("제가 당신들을"));
        Check("C 선택이 코어 복구율을 바꾸지 않는다",
            Mathf.IsEqualApprox(NSP.Core.GameState.Instance.CoreProgress, coreBefore));
        StoryChoiceHistory.ResetRun();
        Check("C 새 판에서 기록이 비워진다", StoryChoiceHistory.Entries.Count == 0);

        Done();
    }

    private (string, System.Action, System.Action)[] Cases() => new (string, System.Action, System.Action)[]
    {
        ("사망", () => _sim.GetEmployeeState("sheep").Alive = false,
                 () => _sim.GetEmployeeState("sheep").Alive = true),
        ("기절", () => _sim.GetEmployeeState("sheep").Incapacitated = true,
                 () => _sim.GetEmployeeState("sheep").Incapacitated = false),
        ("격리", () => _sim.GetEmployeeState("sheep").Isolated = true,
                 () => _sim.GetEmployeeState("sheep").Isolated = false),
    };

    private void Done()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string label, bool ok)
    {
        if (ok) _pass++; else _fail++;
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
    }
}
