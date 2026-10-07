using System.Collections.Generic;

namespace NSP.View;

// 관리자에게 답을 요구하는 순간. **세계관상 이것은 전화 응답이다** —
// 휴게실 공용 전화가 연결돼 있고, 관리자가 수화기 너머로 답하는 것으로 본다
// (새 통신 수단을 만들지 않는다 — NSP_STORY_TUTORIAL_REWORK §2.1).
//
// 고른 답은 **직원들의 반응만** 바꾼다. 코어 복구율 · 스트레스 · 관계도 · 방해공작 확률 ·
// 결번 판정 · 엔딩 중 어느 것도 건드리지 않는다.
public sealed class StoryChoiceOption
{
    // 화면에 뜨는 선택지 글, 그리고 고른 뒤 "관리자가 한 말"로 잠깐 보여 줄 문장이기도 하다.
    public string Text = "";
    // 대본에서 이 선택지와 @branch 를 잇는 이름(option: … | <branch_id>).
    public string BranchId = "";
    // 이 답을 고르면 재생할 직원 반응.
    public readonly List<StoryLine> Branch = new();
}

public sealed class StoryChoice
{
    // 기록에 남길 이름. 비어 있으면 비트 id + 순번으로 채운다(StoryScript).
    public string ChoiceId = "";
    // 선택지 위에 한 줄로 뜨는 물음(비어 있어도 된다).
    public string Prompt = "";
    public readonly List<StoryChoiceOption> Options = new();
}

// 비트 안의 한 걸음 — 대사 한 줄이거나, 관리자 선택 하나다.
// (둘을 한 목록에 담아야 "말하다가 → 묻고 → 반응" 순서가 대본 그대로 유지된다.)
public sealed class StoryStep
{
    public StoryLine Line;
    public StoryChoice Choice;
    public bool IsChoice => Choice != null;
}

// 플레이어가 무엇을 골랐는가. 기록만 한다 — 어떤 판정도 이 값을 읽지 않는다.
public static class StoryChoiceHistory
{
    public sealed class Entry
    {
        public int Day;
        public string ChoiceId = "";
        public int OptionIndex;
        public string OptionText = "";
    }

    private static readonly List<Entry> _entries = new();

    public static IReadOnlyList<Entry> Entries => _entries;

    public static void Record(int day, string choiceId, int optionIndex, string optionText)
    {
        _entries.Add(new Entry
        {
            Day = day,
            ChoiceId = choiceId ?? "",
            OptionIndex = optionIndex,
            OptionText = optionText ?? "",
        });
    }

    // 그 선택에서 무엇을 골랐는가(-1 = 아직 고르지 않았다).
    public static int ChosenIndex(string choiceId)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (_entries[i].ChoiceId == choiceId) return _entries[i].OptionIndex;
        return -1;
    }

    public static void ResetRun() => _entries.Clear();
}
