using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Dialogue;

// 직원이 방금 한 답변을 보고 "한 번 더 캐물을 것"을 0~2개 만든다.
//
// 규칙
//   · 캐릭터별 고정 꼬리질문 목록은 만들지 않는다. 의도(FollowUpIntent)를 고르고
//     그 의도의 표현 풀에서 문장을 뽑는다 — 질문은 UI 선택지라 캐릭터별로 나누지 않는다.
//   · 강한 추궁(Challenge*)은 PlayerKnownEvidence 에 실제 근거가 있을 때만 후보가 된다.
//     내부 EventLog 의 진실만 알고 플레이어가 모르는 내용은 절대 질문이 되지 않는다.
//   · 깊이는 1단계다. 꼬리질문의 답변 뒤에는 다시 기본 질문 목록으로 돌아간다.
public static class FollowUpQuestionGenerator
{
    // 개발용. 후보가 어떻게 뽑히고 무엇이 왜 버려졌는지 출력한다.
    public static bool DebugLog;

    private const int MaxFollowUps = 2;

    public static List<FollowUpQuestion> Generate(DialogueContext ctx, DialogueResponsePlan plan)
    {
        var candidates = new List<FollowUpQuestion>();
        if (ctx == null || plan == null) return candidates;

        string incidentKey = ctx.Subject?.Key ?? "no_incident";
        switch (ctx.QuestionId)
        {
            case DialogueQuestions.Anomaly: Q1(ctx, plan, candidates); break;
            case DialogueQuestions.Where: Q2(ctx, plan, candidates); break;
            case DialogueQuestions.Suspicious: Q3(ctx, plan, candidates); break;
            case DialogueQuestions.Opinion: Q4(ctx, candidates); break;
            case DialogueQuestions.Accuse: Q5(ctx, plan, candidates); break;
            default: return candidates;
        }

        foreach (var c in candidates)
        {
            c.SubjectIncidentKey = incidentKey;
            c.BaseQuestionId = ctx.QuestionId;
        }
        return Select(ctx, candidates, ctx.QuestionId == DialogueQuestions.Opinion ? 1 : MaxFollowUps);
    }

    // --- 기본 질문별 후보 -------------------------------------------------

    // Q1: 직접 본 경우와 소리만 들은 경우에 물어볼 것이 다르다.
    private static void Q1(DialogueContext ctx, DialogueResponsePlan plan, List<FollowUpQuestion> outList)
    {
        switch (plan.Core)
        {
            case CoreKind.IncidentDirect:
                Add(outList, FollowUpIntent.AskDetails, 45, "직접 목격한 사건이 있다");
                Add(outList, FollowUpIntent.AskNextAction, 40, "사건 이후 행동을 확인할 수 있다");
                Add(outList, FollowUpIntent.AskCertainty, 24, "일반 확인");
                break;
            case CoreKind.IncidentIndirect:
                Add(outList, FollowUpIntent.AskWhatWasHeard, 50, "감각 정보만 있는 진술");
                Add(outList, FollowUpIntent.AskCertainty, 38, "간접 인지라 확신도를 물을 수 있다");
                Add(outList, FollowUpIntent.AskNextAction, 30, "사건 이후 행동을 확인할 수 있다");
                break;
            default:
                Reject("Q1", "아는 사건이 없어 캐물을 내용이 없다");
                break;
        }
    }

    // Q2: 알리바이 질문이라 우선도가 가장 높다.
    private static void Q2(DialogueContext ctx, DialogueResponsePlan plan, List<FollowUpQuestion> outList)
    {
        Add(outList, FollowUpIntent.AskPreviousLocation, 55, "위치 진술이 있으므로 직전 동선을 물을 수 있다");
        Add(outList, FollowUpIntent.AskWhoWasPresent, 50, "같은 방 인원 확인");
        Add(outList, FollowUpIntent.AskNextAction, 35, "이후 동선 확인");
        AddChallenges(ctx, plan, outList, bonus: 0);
    }

    // Q3: 실제로 목격한 행동에 대해서만 세부를 캐물을 수 있다.
    private static void Q3(DialogueContext ctx, DialogueResponsePlan plan, List<FollowUpQuestion> outList)
    {
        if (plan.Core != CoreKind.SuspiciousSighting)
        {
            // 화면 로그에는 이 직원이 목격자로 떴는데 본인은 못 봤다고 했다면 모순이다.
            var seen = VisibleWitnessRows(ctx.EmployeeId, ctx.CurrentDay);
            if (seen != null)
                Add(outList, FollowUpIntent.ChallengeContradiction, 118,
                    "시설 로그에 이 직원의 목격 기록이 떠 있는데 못 봤다고 답했다", challenge: true);
            else
                Reject("Q3", "목격 진술도 없고 플레이어가 아는 목격 기록도 없다");
            return;
        }

        Add(outList, FollowUpIntent.AskDetails, 50, "목격한 행동의 내용을 캐물을 수 있다");
        Add(outList, FollowUpIntent.AskExactLocation, 45, "목격 장소 확인");
        Add(outList, FollowUpIntent.AskReason, 40, "수상하다고 본 근거 확인");
        Add(outList, FollowUpIntent.AskCertainty, 30, "일반 확인");
        Add(outList, FollowUpIntent.AskNextAction, 24, "이후 관찰 여부");

        // 지목당한 직원이 이미 다른 위치를 주장했다면 증언끼리 충돌한다.
        string target = plan.SubjectEmployeeId;
        var theirClaim = PlayerKnownEvidence.OwnClaim(target, ctx.Subject?.Key ?? "no_incident");
        if (theirClaim != null && !string.IsNullOrEmpty(plan.IncidentRoomId)
            && theirClaim.RoomId != plan.IncidentRoomId)
        {
            var q = Add(outList, FollowUpIntent.ChallengeWitness, 110,
                "지목당한 직원이 관리자에게 다른 위치를 진술했다", challenge: true);
            q.TargetEmployeeId = target;
            q.EvidenceRoomId = theirClaim.RoomId;
        }
    }

    // Q4: 추리 비중이 낮아 최대 1개만.
    private static void Q4(DialogueContext ctx, List<FollowUpQuestion> outList)
    {
        Add(outList, FollowUpIntent.AskReason, 30, "평가 근거 확인");
        Add(outList, FollowUpIntent.AskTodayDifference, 28, "오늘의 변화 확인");
        Add(outList, FollowUpIntent.AskSuspicion, 25, "의심 여부 확인");
    }

    // Q5: 가장 강한 추궁이 나올 수 있는 질문.
    private static void Q5(DialogueContext ctx, DialogueResponsePlan plan, List<FollowUpQuestion> outList)
    {
        Add(outList, FollowUpIntent.AskDefense, 50, "의심에 대한 해명 요구");
        Add(outList, FollowUpIntent.AskWitness, 45, "위치를 확인해 줄 사람 확인");
        Add(outList, FollowUpIntent.AskRouteAgain, 40, "동선 재확인");

        var moves = PlayerKnownEvidence.VisibleMoves(ctx.EmployeeId, ctx.CurrentDay);
        if (moves.Any(m => !m.PlayerOrdered))
            Add(outList, FollowUpIntent.AskReasonForMovement, 60, "화면 로그에 지시 없는 이동이 있다");

        AddChallenges(ctx, plan, outList, bonus: 5);
    }

    // --- 추궁 후보(플레이어가 확보한 증거가 있을 때만) ----------------------

    private static void AddChallenges(DialogueContext ctx, DialogueResponsePlan plan,
        List<FollowUpQuestion> outList, int bonus)
    {
        string incidentKey = ctx.Subject?.Key ?? "no_incident";
        // 이 답변에서 직원이 주장한 위치. 여기와 어긋나는 "플레이어가 아는 사실"만 근거가 된다.
        string claimed = plan.Core == CoreKind.SelfLocation ? plan.RoomId : "";
        if (string.IsNullOrEmpty(claimed))
            claimed = PlayerKnownEvidence.OwnClaim(ctx.EmployeeId, incidentKey)?.RoomId ?? "";

        // ① 다른 직원의 증언 — 관리자가 직접 들은 것만 남아 있다.
        foreach (var sighting in PlayerKnownEvidence.SightingsOf(ctx.EmployeeId))
        {
            if (string.IsNullOrEmpty(sighting.RoomId) || sighting.RoomId == claimed) continue;
            var q = Add(outList, FollowUpIntent.ChallengeWitness, 120 + bonus,
                "다른 직원이 관리자에게 이 직원을 다른 곳에서 봤다고 진술했다", challenge: true);
            q.TargetEmployeeId = sighting.SpeakerId;
            q.EvidenceRoomId = sighting.RoomId;
            break;
        }

        // ② 관리자가 CCTV로 직접 본 것.
        float when = ctx.Subject?.TimeSeconds ?? ctx.CurrentGameTime;
        const float CctvWindow = 12f;
        string cctvRoom = PlayerKnownEvidence.CctvSeenRoomOf(ctx.EmployeeId, when, CctvWindow);
        if (!string.IsNullOrEmpty(cctvRoom) && !string.IsNullOrEmpty(claimed) && cctvRoom != claimed)
        {
            var q = Add(outList, FollowUpIntent.ChallengeLocation, 118 + bonus,
                "CCTV 로 이 직원을 진술과 다른 작업실에서 직접 봤다", challenge: true);
            q.EvidenceRoomId = cctvRoom;
        }
        else if (PlayerKnownEvidence.CctvWatchedWithout(ctx.EmployeeId, claimed, when, CctvWindow))
        {
            Add(outList, FollowUpIntent.ChallengeContradiction, 112 + bonus,
                "진술한 작업실을 CCTV로 지켜봤지만 그 직원이 없었다", challenge: true);
        }

        // ③ 시설 로그 화면에 실제로 떴던 이동.
        var moves = PlayerKnownEvidence.VisibleMoves(ctx.EmployeeId, ctx.CurrentDay);
        var offSite = moves.FirstOrDefault(m => !string.IsNullOrEmpty(claimed)
                                                && m.ToRoomId != claimed && m.FromRoomId != claimed);
        if (offSite != null)
        {
            var q = Add(outList, FollowUpIntent.ChallengeLocation, 115 + bonus,
                "화면 로그에 진술과 다른 작업실로 이동한 기록이 있다", challenge: true);
            q.EvidenceRoomId = offSite.ToRoomId;
        }

        var unordered = moves.FirstOrDefault(m => !m.PlayerOrdered);
        if (unordered != null)
        {
            var q = Add(outList, FollowUpIntent.ChallengeSuspiciousMovement, 100 + bonus,
                "화면 로그에 지시하지 않은 이동이 있다", challenge: true);
            q.EvidenceRoomId = unordered.ToRoomId;
        }

        // ④ 이동 기록은 떴는데 본인은 그 이동을 한 번도 말하지 않았다.
        if (moves.Count > 0 && !string.IsNullOrEmpty(claimed)
            && moves.All(m => m.ToRoomId != claimed))
            Add(outList, FollowUpIntent.ChallengeOmission, 95 + bonus,
                "화면에 뜬 이동을 진술에서 빠뜨렸다", challenge: true);

        // ⑤ 같은 사건에 대해 앞서 말한 위치와 지금 위치가 다르다.
        var previous = PlayerKnownEvidence.OwnClaim(ctx.EmployeeId, incidentKey);
        if (previous != null && !string.IsNullOrEmpty(claimed) && previous.RoomId != claimed)
            Add(outList, FollowUpIntent.ChallengeClaimConsistency, 110 + bonus,
                "앞서 진술한 위치와 지금 진술이 다르다", challenge: true);

        if (!outList.Any(c => c.IsChallenge))
            Reject("Challenge", "플레이어가 확보한 모순 증거가 없다");
    }

    // 시설 로그 화면에 "이 직원이 목격했다"고 뜬 줄이 있는가.
    private static NSP.Core.DisplayLogEntry VisibleWitnessRows(string employeeId, int day)
    {
        var rows = NSP.Core.FacilityLogFormatter.Build(NSP.Core.EventLog.Instance?.GetAllEntries(), day);
        return rows.FirstOrDefault(r => r.RelatedEmployeeId == employeeId
                                        && r.SourceEventType == NSP.Data.LogEventType.Sabotage);
    }

    // --- 후보 선정 --------------------------------------------------------

    private static List<FollowUpQuestion> Select(DialogueContext ctx, List<FollowUpQuestion> candidates, int max)
    {
        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.Subject?.Key ?? "no_incident");

        foreach (var c in candidates)
        {
            // 이미 물어본 의도는 뒤로 민다 — 같은 걸 계속 캐묻지 않게.
            int asked = claim.Asked(IntentKey(c.Intent));
            if (asked > 0) c.Priority -= 30 * asked;
            c.Text = QuestionText(c);
        }

        var picked = candidates
            .Where(c => c.Priority > 0 && !string.IsNullOrEmpty(c.Text))
            .OrderByDescending(c => c.Priority)
            .ThenBy(_ => GD.Randf())
            .Take(max)
            .ToList();

        if (DebugLog)
        {
            foreach (var c in candidates)
                GD.Print($"[FOLLOWUP] Employee={ctx.EmployeeId} BaseQuestion={ctx.QuestionId} " +
                         $"Candidate={c.Intent} Priority={c.Priority} " +
                         $"{(picked.Contains(c) ? "Selected" : "Dropped")} Reason={c.Reason}");
        }
        return picked;
    }

    private static FollowUpQuestion Add(List<FollowUpQuestion> outList, FollowUpIntent intent, int priority,
        string reason, bool challenge = false)
    {
        var q = new FollowUpQuestion { Intent = intent, Priority = priority, Reason = reason, IsChallenge = challenge };
        outList.Add(q);
        return q;
    }

    private static void Reject(string where, string reason)
    {
        if (DebugLog) GD.Print($"[FOLLOWUP] {where} Rejected Reason={reason}");
    }

    public static string IntentKey(FollowUpIntent intent) => "FU_" + intent;

    // --- 질문 문장 --------------------------------------------------------
    // 플레이어 질문은 UI 선택지다. 캐릭터별로 나누지 않고 의도마다 몇 가지 표현만 둔다.
    private static string QuestionText(FollowUpQuestion q)
    {
        string room = RoomName(q.EvidenceRoomId);
        string who = Codename(q.TargetEmployeeId);

        var pool = q.Intent switch
        {
            FollowUpIntent.AskExactLocation => new[] { "정확히 어디에서 봤습니까?", "어느 작업실이었습니까?" },
            FollowUpIntent.AskPreviousLocation => new[] { "그 전에는 어디에 있었습니까?", "사고 전에는 어디에 있었습니까?" },
            FollowUpIntent.AskNextAction => new[] { "그 뒤에는 어떻게 했습니까?", "그 다음에는 어디로 갔습니까?" },
            FollowUpIntent.AskWhoWasPresent => new[] { "당시 혼자 있었습니까?", "같이 있던 직원은 없었습니까?" },
            FollowUpIntent.AskWitness => new[] { "당신의 위치를 확인해 줄 사람이 있습니까?", "그때 함께 있던 사람이 있습니까?" },
            FollowUpIntent.AskDetails => new[] { "구체적으로 어떤 상황이었습니까?", "정확히 어떤 행동이었습니까?" },
            FollowUpIntent.AskCertainty => new[] { "확실합니까?", "그렇게 단정할 수 있습니까?" },
            FollowUpIntent.AskReason => new[] { "왜 그렇게 생각했습니까?", "그렇게 판단한 이유가 있습니까?" },
            FollowUpIntent.AskWhatWasHeard => new[] { "어떤 소리였습니까?", "무슨 소리를 들었는지 말해 주십시오." },
            FollowUpIntent.AskWhatWasSeen => new[] { "직접 본 겁니까?", "본 것과 들은 것을 구분해 주십시오." },
            FollowUpIntent.AskTodayDifference => new[] { "오늘도 평소와 같았습니까?" },
            FollowUpIntent.AskSuspicion => new[] { "수상하다고 느낀 적은 없습니까?" },
            FollowUpIntent.AskDefense => new[] { "의심을 풀 수 있는 설명이 있습니까?", "그렇다면 어떻게 설명하시겠습니까?" },
            FollowUpIntent.AskRouteAgain => new[] { "사고 당시 동선을 다시 설명해 주십시오." },
            FollowUpIntent.AskReasonForMovement => new[] { "그때 왜 그 방으로 이동했습니까?" },

            FollowUpIntent.ChallengeLocation => string.IsNullOrEmpty(room)
                ? null : new[] { $"{room}에 있었다는 기록이 있습니다. 설명해 주십시오." },
            FollowUpIntent.ChallengeWitness => string.IsNullOrEmpty(who) || string.IsNullOrEmpty(room)
                ? null : new[] { $"{who} 직원은 당신을 {room}에서 봤다고 했습니다." },
            FollowUpIntent.ChallengeOmission => new[] { "아까는 그 이동에 대해 말하지 않았습니다." },
            FollowUpIntent.ChallengeContradiction => new[] { "기록과 진술이 맞지 않습니다. 다시 설명해 주십시오." },
            FollowUpIntent.ChallengeSuspiciousMovement => new[] { "지시 없이 이동한 기록이 있습니다. 이유가 뭡니까?" },
            FollowUpIntent.ChallengeClaimConsistency => new[] { "아까 말씀한 위치와 지금 설명이 다릅니다." },
            FollowUpIntent.ChallengeTime => new[] { "말씀하신 시각이 기록과 맞지 않습니다." },
            _ => null,
        };

        if (pool == null || pool.Length == 0) return "";
        return pool[(int)(GD.Randi() % (uint)pool.Length)];
    }

    private static string RoomName(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return "";
        return NSP.Facility.FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? "";
    }

    private static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "";
        return NSP.Facility.FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? "";
    }
}
