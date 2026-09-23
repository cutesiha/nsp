using Godot;

namespace NSP.Dialogue;

// 추궁이 어떤 규칙으로 성립했는가. 규칙은 셋뿐이고, 위에서 아래로 검사해 처음 걸리는 것을 쓴다.
//
//   Presence — 사고가 난 그 방에 있었다                (사고 기록 + 그 사람의 위치 자료)
//   Behavior — 그 방에서의 행동이 문제가 된다
//              ㉠ 본인의 행동 주장 + 그와 어긋나는 동료의 목격 증언
//              ㉡ 행동이 실린 자료 + 같은 방의 사고 기록
//   Location — 두 자료가 같은 시각에 다른 방을 가리킨다 (예전부터 있던 규칙)
//
// None 은 "성립하지 않음"이지 "물을 수 없음"이 아니다 — 틀린 조합을 들이미는 것도 추리다.
public enum ConfrontKind { None, Presence, Behavior, Location }

// 자료 두 장으로 무엇을 물을 수 있는가를 판정한다.
//
// 게임은 "무엇과 무엇이 모순인지" 를 먼저 알려주지 않는다. 플레이어가 두 장을 골라야만
// 이 판정이 돌고, 판정은 문장 비교가 아니라 자료 구조의 (직원 · 시각 · 작업실 · 행동)
// 값으로만 한다.
//
// 이 클래스는 **거절하지 않는다.** 예전에는 "이 직원의 위치를 확인할 수 있는 자료가
// 아닙니다" 로 막았는데, 플레이어가 가장 먼저 집는 사고 기록이 늘 그 벽에 걸렸다.
// 이제 성립하지 않는 조합은 Kind = None 으로 돌아가고, 질문 자체는 그대로 할 수 있다
// (답은 InterviewReplyPlanner 의 Confront.neutral).
public static class EvidenceContradiction
{
    // 같은 순간으로 볼 수 있는 시간 폭(게임 안의 분). 이 값 하나만 조정하면 된다.
    public const int WindowMinutes = 10;

    private static float WindowSeconds => WindowMinutes * DialogueClock.SecondsPerMinute;
    // 전조(설비 접근 · 오래 머무름)는 사고보다 앞서 나온다 — 행동 추궁은 폭을 두 배로 본다.
    private static float BehaviorWindowSeconds => WindowSeconds * 2f;

    public sealed class Result
    {
        public bool IsContradiction;
        // 어떤 규칙으로 성립했는가. None 이면 성립하지 않은 것이고, 그래도 질문은 할 수 있다.
        public ConfrontKind Kind = ConfrontKind.None;
        // 성립하지 않을 때 화면에 띄울 안내. UI 는 이것으로 거절하지 않는다 — 곁들이는 설명일 뿐이다.
        public string Notice = "";

        // 시간 순으로 정렬한 두 자료.
        public InterviewEvidence Earlier;
        public InterviewEvidence Later;
        // 충돌하는 두 작업실과 기준 시각.
        public string RoomA = "";
        public string RoomB = "";
        public float AnchorTime;

        public string QuestionText = "";
    }

    public static Result Check(string targetEmployeeId, InterviewEvidence a, InterviewEvidence b)
    {
        var fail = new Result { IsContradiction = false, Kind = ConfrontKind.None };

        if (a == null || b == null || a == b || a.Id == b.Id)
        {
            fail.Notice = "서로 다른 자료 두 개를 선택해 주십시오.";
            return fail;
        }

        // 시간 순으로 세워 두면 아래 규칙들이 "먼저 있었던 일 → 나중 일" 순서로 문장을 만든다.
        var (earlier, later) = a.AnchorTime <= b.AnchorTime ? (a, b) : (b, a);

        return Presence(targetEmployeeId, a, b, earlier, later)
               ?? Behavior(targetEmployeeId, a, b, earlier, later)
               ?? Location(targetEmployeeId, a, b, earlier, later)
               ?? NoRule(earlier, later);
    }

    // ── ① 재석 추궁 — 사고가 난 그 방에 있었다 ────────────────────────────
    //
    // 사고 기록은 주인이 없는 자료다(누구에게나 물을 수 있다). 그 사고가 난 방에
    // 이 직원이 있었다는 자료가 한 장이라도 있으면, 그것만으로 물을 거리가 된다.
    // 거짓말을 잡는 규칙이 아니라 "그 자리에 있었던 사람에게 묻는" 규칙이다 —
    // 결번자는 제자리에서 범행하므로 위치 모순은 거의 생기지 않는다(§1-2).
    private static Result Presence(string target, InterviewEvidence a, InterviewEvidence b,
                                   InterviewEvidence earlier, InterviewEvidence later)
    {
        var incident = a.Kind == EvidenceKind.Incident ? a : b.Kind == EvidenceKind.Incident ? b : null;
        if (incident == null || !incident.HasTime || string.IsNullOrEmpty(incident.SubjectRoomId)) return null;

        var other = incident == a ? b : a;
        if (other.Kind == EvidenceKind.Incident) return null;             // 사고 기록 두 장은 재석이 아니다
        if (other.SubjectEmployeeId != target || !other.CanAnchorPosition) return null;

        // 그 자료에 따르면 사고 시각에 이 직원은 어느 방에 있었는가.
        if (RoomClaimedAt(other, incident.AnchorTime) != incident.SubjectRoomId) return null;

        return new Result
        {
            IsContradiction = true,
            Kind = ConfrontKind.Presence,
            Earlier = earlier,
            Later = later,
            RoomA = incident.SubjectRoomId,
            RoomB = incident.SubjectRoomId,
            AnchorTime = incident.AnchorTime,
            QuestionText = PresenceQuestion(incident),
        };
    }

    // ── ② 행동 추궁 — 그 방에서의 행동이 문제가 된다 ───────────────────────
    //
    // 이 게임의 추리는 "그 방에 있던 사람 중 누가 이상 행동을 했는가" 다. 두 갈래로 선다.
    //   ㉠ 본인이 "그런 행동은 안 했다"고 한 말 + 동료가 "하고 있었다"고 한 증언
    //   ㉡ 행동이 실린 자료(동료 증언 · 설비 접근 CCTV) + 같은 방의 사고 기록
    // 결백한 직원도 같은 행동을 할 수 있으므로(가짜 단서), 성립했다는 것이 곧 범인이라는
    // 뜻은 아니다 — 결백한 직원은 ㉠ 을 만들지 않지만 ㉡ 에는 얼마든지 걸린다.
    private static Result Behavior(string target, InterviewEvidence a, InterviewEvidence b,
                                   InterviewEvidence earlier, InterviewEvidence later)
    {
        // ② - ㉠ 본인이 "그런 행동은 하지 않았다"고 한 말 + 동료가 "하고 있었다"고 한 증언.
        //
        // 결번자에게서 잡을 수 있는 **유일한 정면 충돌**이다(§3-2). 사고 기록이 필요 없다 —
        // 두 사람의 말이 같은 방·같은 시간대에서 서로를 부정하는 것 자체가 물을 거리다.
        var own = Claim(a, target) ?? Claim(b, target);
        if (own != null)
        {
            var witness = own == a ? b : a;
            if (witness.Kind == EvidenceKind.Testimony
                && witness.SubjectEmployeeId == target
                && !string.IsNullOrEmpty(witness.BehaviorDetail)
                && witness.SubjectRoomId == own.SubjectRoomId
                && witness.HasTime && own.HasTime
                && Mathf.Abs(witness.AnchorTime - own.AnchorTime) <= BehaviorWindowSeconds)
            {
                return new Result
                {
                    IsContradiction = true,
                    Kind = ConfrontKind.Behavior,
                    Earlier = earlier,
                    Later = later,
                    RoomA = own.SubjectRoomId,
                    RoomB = witness.SubjectRoomId,
                    AnchorTime = witness.AnchorTime,
                    QuestionText = DenialQuestion(own, witness),
                };
            }
        }

        // ② - ㉡ 행동이 실린 자료 + 같은 방의 사고 기록.
        var incident = a.Kind == EvidenceKind.Incident ? a : b.Kind == EvidenceKind.Incident ? b : null;
        if (incident == null || !incident.HasTime || string.IsNullOrEmpty(incident.SubjectRoomId)) return null;

        var act = incident == a ? b : a;
        if (act.SubjectEmployeeId != target || string.IsNullOrEmpty(act.BehaviorDetail)) return null;
        if (act.SubjectRoomId != incident.SubjectRoomId) return null;
        if (!act.HasTime || Mathf.Abs(incident.AnchorTime - act.AnchorTime) > BehaviorWindowSeconds) return null;

        return new Result
        {
            IsContradiction = true,
            Kind = ConfrontKind.Behavior,
            Earlier = earlier,
            Later = later,
            RoomA = act.SubjectRoomId,
            RoomB = incident.SubjectRoomId,
            AnchorTime = act.AnchorTime,
            QuestionText = BehaviorQuestion(act),
        };
    }

    // 본인이 자기 행동에 대해 한 주장("설비 근처에 가지 않았다"). 아니면 null.
    private static InterviewEvidence Claim(InterviewEvidence ev, string target) =>
        ev.Kind == EvidenceKind.OwnStatement && ev.SubjectEmployeeId == target
        && !string.IsNullOrEmpty(ev.BehaviorDetail) ? ev : null;

    // ── ③ 위치 추궁 — 같은 시각에 두 자료가 다른 방을 가리킨다 ─────────────
    //
    // V1 부터 있던 규칙. 플레이어가 사고 뒤 직원을 다른 방으로 옮겼을 때처럼
    // 실제로 진술과 기록이 어긋나는 경우에만 성립한다.
    private static Result Location(string target, InterviewEvidence a, InterviewEvidence b,
                                   InterviewEvidence earlier, InterviewEvidence later)
    {
        if (a.SubjectEmployeeId != target || b.SubjectEmployeeId != target) return null;
        if (!a.CanAnchorPosition || !b.CanAnchorPosition) return null;

        // 둘 중 늦은 쪽의 시각을 기준으로 양쪽이 각각 어느 방을 주장하는지 본다.
        string roomFromEarlier = RoomClaimedAt(earlier, later.AnchorTime);
        string roomFromLater = RoomClaimedAt(later, later.AnchorTime);
        if (string.IsNullOrEmpty(roomFromEarlier) || string.IsNullOrEmpty(roomFromLater)) return null;
        if (roomFromEarlier == roomFromLater) return null;

        return new Result
        {
            IsContradiction = true,
            Kind = ConfrontKind.Location,
            Earlier = earlier,
            Later = later,
            RoomA = roomFromEarlier,
            RoomB = roomFromLater,
            AnchorTime = later.AnchorTime,
            QuestionText = LocationQuestion(earlier, later),
        };
    }

    // ── 성립하지 않는 조합 ────────────────────────────────────────────────
    //
    // 거절하지 않는다. 두 자료를 그대로 들이미는 질문을 만들어 주고, 답은 중립이다.
    private static Result NoRule(InterviewEvidence earlier, InterviewEvidence later) => new()
    {
        IsContradiction = false,
        Kind = ConfrontKind.None,
        Earlier = earlier,
        Later = later,
        AnchorTime = later.HasTime ? later.AnchorTime : earlier.AnchorTime,
        Notice = "두 자료 사이에서 직접적인 모순을 확인할 수 없습니다.",
        QuestionText = KoreanParticle.Resolve($"{Describe(earlier)} 그리고 {Describe(later)} 이 둘을 함께 보면 어떻습니까?"),
    };

    // 그 자료에 따르면 이 직원은 해당 시각에 어느 방에 있었는가. 말할 수 없으면 빈 값.
    private static string RoomClaimedAt(InterviewEvidence ev, float time)
    {
        float gap = time - ev.AnchorTime;
        if (Mathf.Abs(gap) > WindowSeconds) return "";

        // 도착 기록은 그 순간을 경계로 앞뒤의 방이 다르다.
        if (ev.Position == PositionClaim.Arrived)
            return gap >= 0f ? ev.ToRoomId : ev.FromRoomId;

        return ev.SubjectRoomId;
    }

    // --- 추궁 문장 ------------------------------------------------------

    private static string PresenceQuestion(InterviewEvidence incident)
    {
        string when = DialogueClock.Spoken(incident.AnchorTime);
        string room = InterviewEvidenceBoard.RoomName(incident.SubjectRoomId);
        return KoreanParticle.Resolve(
            $"{when}경 {room}에서 사고가 났을 때 그 방에 계셨습니다. 무엇을 하고 있었습니까?");
    }

    // 본인의 말과 동료의 말이 정면으로 부딪힐 때. 둘 다 그대로 인용한다 —
    // 어느 쪽이 거짓인지는 화면이 정하지 않는다.
    private static string DenialQuestion(InterviewEvidence own, InterviewEvidence witness)
    {
        string room = InterviewEvidenceBoard.RoomName(own.SubjectRoomId);
        string who = InterviewEvidenceBoard.Codename(witness.SpeakerEmployeeId);
        return KoreanParticle.Resolve(
            $"{room}에서 {own.BehaviorDetail}고 하셨습니다. 하지만 {who} 직원은 "
            + $"{witness.BehaviorDetail}고 진술했습니다. 설명해 주시죠.");
    }

    private static string BehaviorQuestion(InterviewEvidence act)
    {
        string room = InterviewEvidenceBoard.RoomName(act.SubjectRoomId);
        return KoreanParticle.Resolve(
            $"사고 직전 {room}에서 {act.BehaviorDetail}는 증언이 있습니다. 설명해 주시죠.");
    }

    private static string LocationQuestion(InterviewEvidence earlier, InterviewEvidence later)
    {
        // 본인의 진술이 있으면 그것을 먼저 들이민다 — "이렇게 말했는데, 기록은 다르다".
        bool earlierIsClaim = earlier.Kind == EvidenceKind.OwnStatement;
        bool laterIsClaim = later.Kind == EvidenceKind.OwnStatement;
        var (first, second) = (!earlierIsClaim && laterIsClaim) ? (later, earlier) : (earlier, later);

        string line1 = Describe(first);
        string line2 = Describe(second);
        return KoreanParticle.Resolve($"{line1} 하지만 {line2} 설명해 주시죠.");
    }

    private static string Describe(InterviewEvidence ev)
    {
        string when = DialogueClock.Spoken(ev.AnchorTime);
        string room = InterviewEvidenceBoard.RoomName(ev.SubjectRoomId);

        return ev.Kind switch
        {
            EvidenceKind.OwnStatement =>
                $"{when}에는 {room}에 있었다고 하셨습니다.",
            EvidenceKind.Testimony =>
                $"{InterviewEvidenceBoard.Codename(ev.SpeakerEmployeeId)} 직원은 {when}경 "
                + $"{room}에서 당신을 봤다고 진술했습니다.",
            EvidenceKind.Cctv =>
                $"같은 시각 {room} CCTV에 당신이 기록되어 있습니다.",
            EvidenceKind.Movement =>
                $"시설 로그에는 {when}에 {InterviewEvidenceBoard.RoomName(ev.FromRoomId)}에서 "
                + $"{room}으로/로 이동한 기록이 있습니다.",
            EvidenceKind.Incident =>
                $"{when}경 {room}에서 사고가 있었습니다.",
            EvidenceKind.Mood =>
                $"오늘 근무 전에는 '{ev.MoodText}' 이라고 적어 내셨습니다.",
            _ => $"{when}경 {room} 기록이 있습니다.",
        };
    }
}
