using System.Collections.Generic;
using System.Linq;

namespace NSP.Dialogue;

// 휴게시간 심문 한 건의 진행 상태.
//
// UI(PhoneCallHud)는 여기에만 말을 건다. 조사 자료를 고르고, 질문을 고르고, 두 장을
// 비교하는 흐름이 전부 이 클래스 안에서 끝나므로 화면 코드에는 추리 규칙이 없다.
public sealed class InterviewSession
{
    public string EmployeeId { get; }
    public List<InterviewEvidence> Board { get; private set; } = new();

    // 플레이어가 지금 고른 자료(최대 2장). 순서는 고른 순서.
    private readonly List<string> _selected = new();
    // 이미 물어본 질문 — 같은 걸 또 묻지 않게 목록에서 뺀다.
    private readonly HashSet<string> _asked = new();

    public InterviewSession(string employeeId)
    {
        EmployeeId = employeeId ?? "";
        Refresh();
    }

    // 답변이 새 진술을 남기면 자료가 늘어난다 — 한 턴이 끝날 때마다 다시 만든다.
    public void Refresh() => Board = InterviewEvidenceBoard.Build(EmployeeId);

    public string Greeting() => LocalDialogueGenerator.InterviewGreeting(EmployeeId);

    // --- 자료 선택 -------------------------------------------------------

    public IReadOnlyList<string> Selected => _selected;

    public bool IsSelected(string evidenceId) => _selected.Contains(evidenceId);

    // 선택 토글. 세 번째를 고르면 가장 먼저 고른 것이 빠진다.
    public void Toggle(string evidenceId)
    {
        if (string.IsNullOrEmpty(evidenceId)) return;
        if (_selected.Remove(evidenceId)) return;
        _selected.Add(evidenceId);
        while (_selected.Count > 2) _selected.RemoveAt(0);
    }

    public void ClearSelection() => _selected.Clear();

    public InterviewEvidence EvidenceAt(int index) =>
        index >= 0 && index < _selected.Count ? InterviewEvidenceBoard.Find(Board, _selected[index]) : null;

    // --- 질문 목록 -------------------------------------------------------

    // 증거 없이 물을 수 있는 기본 질문. 인터뷰의 도입부일 뿐 중심이 아니다.
    public List<InterviewQuestion> BasicQuestions() =>
        InterviewQuestionFactory.BasicQuestions(EmployeeId).Where(q => !_asked.Contains(q.Key)).ToList();

    // 지금 고른 자료 한 장으로 물을 수 있는 것들.
    public List<InterviewQuestion> QuestionsForSelection()
    {
        var ev = EvidenceAt(_selected.Count - 1);
        if (ev == null) return new List<InterviewQuestion>();
        return InterviewQuestionFactory.For(EmployeeId, ev).Where(q => !_asked.Contains(q.Key)).ToList();
    }

    // --- 모순 추궁 -------------------------------------------------------

    public bool CanTryConfront => _selected.Count == 2;

    // 두 자료가 실제로 충돌하는지 본다. 아니면 Notice 에 이유가 담겨 돌아온다.
    public EvidenceContradiction.Result CheckContradiction() =>
        EvidenceContradiction.Check(EmployeeId, EvidenceAt(0), EvidenceAt(1));

    // --- 한 턴 -----------------------------------------------------------

    public sealed class Turn
    {
        public string QuestionText = "";
        public string Answer = "";
        // 답변을 듣고 한 번 더 물을 수 있는 중립 질문(0~2개). 추궁은 여기 없다.
        public List<InterviewQuestion> FollowUps = new();
    }

    public Turn Ask(InterviewQuestion q)
    {
        var turn = new Turn();
        if (q == null) return turn;
        _asked.Add(q.Key);
        turn.QuestionText = q.Text;

        // 기본 질문은 기존 파이프라인이 그대로 답한다 — 잘 돌고 있는 길을 건드리지 않는다.
        turn.Answer = IsBasic(q.Intent)
            ? LocalDialogueGenerator.InterviewAnswer(EmployeeId, BasicQuestionId(q.Intent))
            : InterviewReplyPlanner.Answer(q);

        turn.FollowUps = NeutralFollowUps(q);
        Refresh();
        return turn;
    }

    public Turn Confront(EvidenceContradiction.Result result)
    {
        var turn = new Turn();
        if (result == null || !result.IsContradiction) return turn;
        _asked.Add($"Confront|{result.Earlier?.Id}|{result.Later?.Id}");
        turn.QuestionText = result.QuestionText;
        turn.Answer = InterviewReplyPlanner.ConfrontAnswer(EmployeeId, result);
        Refresh();
        return turn;
    }

    // --- 중립 꼬리질문 ---------------------------------------------------

    // 방금 답변에서 자연스럽게 이어지는 것만 만든다.
    // "기록과 다른데요?" 같은 추궁은 절대 여기서 나오지 않는다 — 그건 플레이어의 몫이다.
    private List<InterviewQuestion> NeutralFollowUps(InterviewQuestion asked)
    {
        var result = new List<InterviewQuestion>();
        if (asked == null || IsBasic(asked.Intent) || string.IsNullOrEmpty(asked.EvidenceId))
            return result;

        var ev = InterviewEvidenceBoard.Find(Board, asked.EvidenceId);
        if (ev == null) return result;

        // 방금 물은 것과 같은 자료에서, 아직 안 물어본 중립 질문을 최대 둘.
        var pool = new List<InterviewIntent>();
        switch (asked.Intent)
        {
            case InterviewIntent.AskMoveReason:
                pool.Add(InterviewIntent.FollowExactTime);
                pool.Add(InterviewIntent.AskWhoWasPresent);
                pool.Add(InterviewIntent.AskActionAtDestination);
                break;
            case InterviewIntent.AskPresenceReason:
                pool.Add(InterviewIntent.AskWhoWasPresent);
                pool.Add(InterviewIntent.AskRouteAround);
                break;
            case InterviewIntent.AskActionAtDestination:
                pool.Add(InterviewIntent.AskNextLocation);
                pool.Add(InterviewIntent.AskWhoWasPresent);
                break;
            case InterviewIntent.AskWhereAtIncident:
                pool.Add(InterviewIntent.AskWhoWasPresent);
                pool.Add(InterviewIntent.AskBeforeIncident);
                break;
            case InterviewIntent.AskIncidentKnown:
                pool.Add(InterviewIntent.AskWhereAtIncident);
                pool.Add(InterviewIntent.FollowExactTime);
                break;
            case InterviewIntent.AskConfirmTestimony:
                pool.Add(InterviewIntent.AskWhoWasPresent);
                pool.Add(InterviewIntent.FollowExactTime);
                break;
            case InterviewIntent.AskWhoWasPresent:
                pool.Add(InterviewIntent.AskNextLocation);
                break;
        }

        foreach (var intent in pool)
        {
            if (result.Count >= 2) break;
            var q = InterviewQuestionFactory.Make(EmployeeId, ev, intent);
            if (string.IsNullOrEmpty(q.Text) || _asked.Contains(q.Key)) continue;
            result.Add(q);
        }
        return result;
    }

    // --- 기본 질문은 기존 파이프라인으로 -----------------------------------

    private static bool IsBasic(InterviewIntent intent) => intent
        is InterviewIntent.BasicShift or InterviewIntent.BasicAnomaly or InterviewIntent.BasicSuspicious;

    private static string BasicQuestionId(InterviewIntent intent) => intent switch
    {
        InterviewIntent.BasicShift => DialogueQuestions.GeneralStatus,
        InterviewIntent.BasicSuspicious => DialogueQuestions.Suspicious,
        _ => DialogueQuestions.Anomaly,
    };
}
