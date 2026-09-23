using Godot;

namespace NSP.Dialogue;

// 자료 두 장이 실제로 충돌하는가를 판정한다.
//
// 게임은 "무엇과 무엇이 모순인지" 를 먼저 알려주지 않는다. 플레이어가 두 장을 골라야만
// 이 판정이 돌고, 말이 되지 않는 조합은 여기서 막힌다. 판정은 문장 비교가 아니라
// 자료 구조의 (직원 · 시각 · 작업실) 세 값으로만 한다.
// 추궁이 어떤 규칙으로 성립했는가. 규칙은 셋뿐이고, 위에서 아래로 검사해 처음 걸리는 것을 쓴다.
//
//   Presence — 사고가 난 그 방에 있었다                (사고 기록 + 그 사람의 위치 자료)
//   Behavior — 사고 직전 그 방에서 이상 행동이 목격됐다 (행동이 실린 자료 + 사고 기록)
//   Location — 두 자료가 같은 시각에 다른 방을 가리킨다 (예전부터 있던 규칙)
//
// None 은 "성립하지 않음"이지 "물을 수 없음"이 아니다 — 틀린 조합을 들이미는 것도 추리다.
public enum ConfrontKind { None, Presence, Behavior, Location }

public static class EvidenceContradiction
{
    // 같은 순간으로 볼 수 있는 시간 폭(게임 안의 분). 이 값 하나만 조정하면 된다.
    public const int WindowMinutes = 10;

    private static float WindowSeconds => WindowMinutes * DialogueClock.SecondsPerMinute;

    public sealed class Result
    {
        public bool IsContradiction;
        // 어떤 규칙으로 성립했는가. None 이면 성립하지 않은 것이고, 그래도 질문은 할 수 있다.
        public ConfrontKind Kind = ConfrontKind.None;
        // 성립하지 않을 때 화면에 띄울 안내.
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
        var fail = new Result { IsContradiction = false };

        if (a == null || b == null || a == b || a.Id == b.Id)
        {
            fail.Notice = "서로 다른 자료 두 개를 선택해 주십시오.";
            return fail;
        }

        // 두 자료 모두 이 직원의 위치를 말하고 있어야 한다.
        if (a.SubjectEmployeeId != targetEmployeeId || b.SubjectEmployeeId != targetEmployeeId)
        {
            fail.Notice = "이 직원의 위치를 확인할 수 있는 자료가 아닙니다.";
            return fail;
        }
        if (!a.CanAnchorPosition || !b.CanAnchorPosition)
        {
            fail.Notice = "시각과 위치가 함께 기록된 자료여야 비교할 수 있습니다.";
            return fail;
        }

        // 둘 중 늦은 쪽의 시각을 기준으로 양쪽이 각각 어느 방을 주장하는지 본다.
        var (earlier, later) = a.AnchorTime <= b.AnchorTime ? (a, b) : (b, a);

        string roomFromEarlier = RoomClaimedAt(earlier, later.AnchorTime);
        string roomFromLater = RoomClaimedAt(later, later.AnchorTime);

        if (string.IsNullOrEmpty(roomFromEarlier) || string.IsNullOrEmpty(roomFromLater))
        {
            fail.Notice = "두 자료 사이에서 직접적인 모순을 확인할 수 없습니다.";
            return fail;
        }
        if (roomFromEarlier == roomFromLater)
        {
            fail.Notice = "두 자료는 서로 어긋나지 않습니다.";
            return fail;
        }

        return new Result
        {
            IsContradiction = true,
            Earlier = earlier,
            Later = later,
            RoomA = roomFromEarlier,
            RoomB = roomFromLater,
            AnchorTime = later.AnchorTime,
            QuestionText = Confront(earlier, later),
        };
    }

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

    private static string Confront(InterviewEvidence earlier, InterviewEvidence later)
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
            _ => $"{when}경 {room} 기록이 있습니다.",
        };
    }
}
