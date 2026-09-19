using System.Collections.Generic;

namespace NSP.Dialogue;

// 「자료 한 장 → 물어볼 수 있는 것들 → 질문 문장」.
//
// 질문 문장은 반드시 그 자료의 값(시각·작업실·상대)을 그대로 인용한다.
// 문장을 조립할 때 다른 사건의 값을 끌어오지 않는다 — 옛 꼬리질문이 엉뚱한 방 이름을
// 말하던 원인이 그것이었다.
public static class InterviewQuestionFactory
{
    // 증거 없이도 언제든 물을 수 있는 기본 질문. 인터뷰의 중심이 아니라 도입부다.
    public static List<InterviewQuestion> BasicQuestions(string targetEmployeeId)
    {
        var list = new List<InterviewQuestion>();
        void Add(InterviewIntent intent, string text) => list.Add(new InterviewQuestion
        {
            TargetEmployeeId = targetEmployeeId, Intent = intent, Text = text,
        });

        Add(InterviewIntent.BasicShift, "오늘 근무는 어땠습니까?");
        Add(InterviewIntent.BasicAnomaly, "오늘 이상한 점을 느꼈습니까?");
        Add(InterviewIntent.BasicSuspicious, "수상한 행동을 한 사람을 봤습니까?");
        return list;
    }

    // 이 자료로 물어볼 수 있는 것들.
    public static List<InterviewQuestion> For(string targetEmployeeId, InterviewEvidence ev)
    {
        var list = new List<InterviewQuestion>();
        if (ev == null || string.IsNullOrEmpty(targetEmployeeId)) return list;

        // 다른 직원의 자료로는 이 사람에게 물을 수 없다(사고 기록은 주인이 없으므로 예외).
        if (!string.IsNullOrEmpty(ev.SubjectEmployeeId) && ev.SubjectEmployeeId != targetEmployeeId)
            return list;

        switch (ev.Kind)
        {
            case EvidenceKind.Movement:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoveReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskNextLocation);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskActionAtDestination);
                break;

            case EvidenceKind.Incident:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskIncidentKnown);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhereAtIncident);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskBeforeIncident);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoSeenNear);
                break;

            case EvidenceKind.Cctv:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskPresenceReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskActionAtDestination);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskRouteAround);
                break;

            case EvidenceKind.Testimony:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskConfirmTestimony);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskPresenceReason);
                break;

            case EvidenceKind.OwnStatement:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskRestate);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                break;

            case EvidenceKind.Mood:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodBefore);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodRelated);
                break;
        }
        return list;
    }

    private static void Add(List<InterviewQuestion> list, string target, InterviewEvidence ev,
        InterviewIntent intent)
    {
        var q = Make(target, ev, intent);
        if (!string.IsNullOrEmpty(q.Text)) list.Add(q);
    }

    // 자료의 값을 그대로 옮겨 담은 질문 하나.
    public static InterviewQuestion Make(string target, InterviewEvidence ev, InterviewIntent intent)
    {
        var q = new InterviewQuestion
        {
            TargetEmployeeId = target,
            Intent = intent,
            EvidenceId = ev?.Id ?? "",
            IncidentKey = ev?.IncidentKey ?? "",
            IncidentType = ev?.IncidentType ?? default,
            AnchorTime = ev?.AnchorTime ?? 0f,
            HasAnchorTime = ev?.HasTime ?? false,
            FromRoomId = ev?.FromRoomId ?? "",
            ToRoomId = ev?.ToRoomId ?? "",
            SubjectRoomId = ev?.SubjectRoomId ?? "",
            OtherEmployeeId = ev?.SpeakerEmployeeId == target ? "" : ev?.SpeakerEmployeeId ?? "",
            PlayerOrderedMove = ev?.PlayerOrdered ?? false,
            MoodText = ev?.MoodText ?? "",
        };
        q.Text = KoreanParticle.Resolve(Text(q, ev));
        return q;
    }

    // 질문 문장. 시각은 자료에 시각이 있을 때만 말한다.
    private static string Text(InterviewQuestion q, InterviewEvidence ev)
    {
        string at = q.HasAnchorTime ? DialogueClock.Spoken(q.AnchorTime) + "경 " : "";
        string from = InterviewEvidenceBoard.RoomName(q.FromRoomId);
        string to = InterviewEvidenceBoard.RoomName(q.ToRoomId);
        string here = InterviewEvidenceBoard.RoomName(q.SubjectRoomId);
        string who = InterviewEvidenceBoard.Codename(q.OtherEmployeeId);

        return q.Intent switch
        {
            InterviewIntent.AskMoveReason =>
                $"{at}{from}에서 {to}으로/로 이동한 이유는 무엇입니까?",
            InterviewIntent.AskWhoWasPresent =>
                $"{at}함께 있던 직원이 있었습니까?",
            InterviewIntent.AskNextLocation =>
                $"{to}을/를 나온 뒤에는 어디로 이동했습니까?",
            InterviewIntent.AskActionAtDestination =>
                $"{here}에서는 무엇을 했습니까?",

            InterviewIntent.AskIncidentKnown =>
                string.IsNullOrEmpty(here)
                    ? $"{at}이 사고를 알고 있었습니까?"
                    : $"{at}{here}에서 난 이 사고를 알고 있었습니까?",
            InterviewIntent.AskWhereAtIncident =>
                $"{at}당신은 어디에 있었습니까?",
            InterviewIntent.AskBeforeIncident =>
                $"이 사고 직전에는 무엇을 하고 있었습니까?",
            InterviewIntent.AskWhoSeenNear =>
                $"{at}그 근처에서 누구를 봤습니까?",

            InterviewIntent.AskPresenceReason =>
                $"{at}{here}에 있었던 이유는 무엇입니까?",
            InterviewIntent.AskRouteAround =>
                $"{at}전후로 어떤 경로로 움직였습니까?",

            InterviewIntent.AskConfirmTestimony =>
                $"{who} 직원은 {at}{here}에서 당신을 봤다고 진술했습니다. 사실입니까?",
            InterviewIntent.AskRestate =>
                $"{at}{here}에 있었다고 하셨습니다. 다시 설명해 주십시오.",

            InterviewIntent.AskMoodReason =>
                $"오늘 기분을 '{q.MoodText}'이라고/라고 적으셨습니다. 이유가 무엇입니까?",
            InterviewIntent.AskMoodBefore =>
                "근무 전부터 그런 상태였습니까?",
            InterviewIntent.AskMoodRelated =>
                "특정 직원이나 사건과 관련이 있습니까?",

            InterviewIntent.FollowExactTime =>
                "정확히 몇 시였습니까?",

            _ => "",
        };
    }
}
