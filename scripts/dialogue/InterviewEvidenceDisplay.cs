using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Dialogue;

// 「조사 노트」 화면 정리 전용(presentation only).
//
// 원본 InterviewEvidence 는 삭제하지도 합치지도 않는다. 여기서 만드는 카드/그룹은 화면에
// 늘어놓는 방법일 뿐이고, 질문 · 모순 추궁에는 언제나 카드 안의 원본 자료(Items)를 쓴다.
// 추리 판정(EvidenceContradiction)은 이 파일을 전혀 보지 않는다.
//
// 그리고 이 파일은 "무엇이 수상한가"를 판단하지 않는다 — 시간이 가까운 것끼리 모으고,
// 같은 장면이 반복된 기록을 한 장으로 접을 뿐이다.
public enum EvidenceCardKind
{
    Single,     // 자료 한 장
    CctvRange,  // 같은 직원이 같은 방에서 연속으로 잡힌 CCTV 기록 묶음
    MovePath,   // 같은 직원의 이어지는 이동 기록 묶음(정비실 → 저장고 → 경비실)
}

public sealed class EvidenceCard
{
    public EvidenceCardKind Kind = EvidenceCardKind.Single;
    public readonly List<InterviewEvidence> Items = new();

    public InterviewEvidence First => Items[0];
    public InterviewEvidence Last => Items[^1];
    public bool IsCluster => Items.Count > 1;
    // 펼침 상태 등을 기억하는 화면용 키. 같은 자료로 다시 만들면 같은 키가 나온다.
    public string Key => $"{Kind}:{First.Id}";
}

public sealed class EvidenceGroup
{
    public string Key = "";
    public string Title = "";
    // 이 묶음의 중심 사고. null 이면 「기타 기록」.
    public InterviewEvidence Incident;
    public readonly List<EvidenceCard> Cards = new();
}

public static class InterviewEvidenceDisplay
{
    // 연속 기록으로 보는 최대 간격(게임 안의 분). 이보다 벌어지면 따로 보여 준다.
    public const int ClusterGapMinutes = 30;

    private static float GapSeconds => ClusterGapMinutes * DialogueClock.SecondsPerMinute;
    private static float WindowSeconds => EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute;

    // --- 압축 -----------------------------------------------------------

    // 시간순 자료를 카드로 접는다. 사고 · 증언 · 본인 진술 · 기분은 절대 묶지 않는다.
    //
    // CCTV : 같은 직원 · 같은 작업실 기록이 그 직원의 다른 CCTV 나 이동 기록 없이 이어지면 한 장.
    //        (같은 방 · 같은 인원의 반복 장면은 PlayerKnownEvidence 가 기록 단계에서 이미 걸러 내므로,
    //         남는 반복은 "그 방에 계속 있었는데 드나드는 사람만 바뀐" 경우다 — 그것도 같은 장면으로 본다.)
    // 이동 : 같은 직원의 이동이 앞 이동의 도착 방에서 이어서 출발하면 한 장의 경로.
    public static List<EvidenceCard> Compress(IEnumerable<InterviewEvidence> evidence)
    {
        var cards = new List<EvidenceCard>();
        if (evidence == null) return cards;

        var openCctv = new Dictionary<string, EvidenceCard>();
        var openMove = new Dictionary<string, EvidenceCard>();

        foreach (var ev in Ordered(evidence))
        {
            string who = ev.SubjectEmployeeId ?? "";
            bool timed = ev.HasTime && !string.IsNullOrEmpty(who);

            if (timed && ev.Kind == EvidenceKind.Cctv)
            {
                if (openCctv.TryGetValue(who, out var run) && run.Last.SubjectRoomId == ev.SubjectRoomId
                    && ev.AnchorTime - run.Last.AnchorTime <= GapSeconds)
                {
                    run.Items.Add(ev);
                    run.Kind = EvidenceCardKind.CctvRange;
                    continue;
                }
                openCctv[who] = NewCard(cards, ev);
                continue;
            }

            if (timed && ev.Kind == EvidenceKind.Movement)
            {
                // 움직였다면 그 전의 "같은 방에서 계속 확인"은 끝난 것이다.
                openCctv.Remove(who);
                if (openMove.TryGetValue(who, out var path) && path.Last.ToRoomId == ev.FromRoomId
                    && ev.AnchorTime - path.Last.AnchorTime <= GapSeconds)
                {
                    path.Items.Add(ev);
                    path.Kind = EvidenceCardKind.MovePath;
                    continue;
                }
                openMove[who] = NewCard(cards, ev);
                continue;
            }

            NewCard(cards, ev);
        }
        return cards;
    }

    private static EvidenceCard NewCard(List<EvidenceCard> cards, InterviewEvidence ev)
    {
        var c = new EvidenceCard();
        c.Items.Add(ev);
        cards.Add(c);
        return c;
    }

    // Board 와 같은 순서(시간 없는 자료 먼저 → 시각 → Id).
    private static IEnumerable<InterviewEvidence> Ordered(IEnumerable<InterviewEvidence> list) => list
        .Where(e => e != null)
        .OrderBy(e => e.HasTime ? 1 : 0)
        .ThenBy(e => e.AnchorTime)
        .ThenBy(e => e.Id, System.StringComparer.Ordinal);

    // --- 사건별 묶음 -----------------------------------------------------

    // 오늘의 사고를 중심으로 자료를 모은다. 사고 시각 ±EvidenceContradiction.WindowMinutes 안의 자료가
    // 그 사고 밑에 들어가고, 여러 사고에 걸치면 가장 가까운 사고 하나에만 들어간다.
    // 어느 사고 근처에도 없는 자료는 「기타 기록」. 기분(Mood)은 이 보기에서 뺀다.
    public static List<EvidenceGroup> ByIncident(IEnumerable<InterviewEvidence> board)
    {
        var all = Ordered(board ?? Enumerable.Empty<InterviewEvidence>()).ToList();
        var incidents = all.Where(e => e.Kind == EvidenceKind.Incident && e.HasTime).ToList();

        var buckets = new Dictionary<InterviewEvidence, List<InterviewEvidence>>();
        foreach (var inc in incidents) buckets[inc] = new List<InterviewEvidence> { inc };
        var other = new List<InterviewEvidence>();

        foreach (var ev in all)
        {
            if (ev.Kind == EvidenceKind.Mood) continue;
            if (ev.Kind == EvidenceKind.Incident && ev.HasTime) continue;   // 이미 자기 묶음의 머리
            var near = NearestIncident(incidents, ev);
            if (near != null) buckets[near].Add(ev); else other.Add(ev);
        }

        var groups = new List<EvidenceGroup>();
        foreach (var inc in incidents)
        {
            var g = new EvidenceGroup
            {
                Key = "inc:" + inc.Id,
                Title = $"{DialogueClock.Text(inc.AnchorTime)} {InterviewEvidenceBoard.RoomName(inc.SubjectRoomId)} 이상",
                Incident = inc,
            };
            g.Cards.AddRange(Compress(buckets[inc]));
            groups.Add(g);
        }
        if (other.Count > 0)
        {
            var g = new EvidenceGroup { Key = "etc", Title = "기타 기록" };
            g.Cards.AddRange(Compress(other));
            groups.Add(g);
        }
        return groups;
    }

    // 시간 창 안의 가장 가까운 사고. 없으면 null. 거리가 같으면 먼저 난 사고.
    public static InterviewEvidence NearestIncident(IEnumerable<InterviewEvidence> incidents, InterviewEvidence ev)
    {
        if (ev == null || !ev.HasTime || incidents == null) return null;
        InterviewEvidence best = null;
        float bestGap = float.MaxValue;
        foreach (var inc in incidents)
        {
            float gap = Mathf.Abs(ev.AnchorTime - inc.AnchorTime);
            if (gap > WindowSeconds || gap >= bestGap) continue;
            best = inc;
            bestGap = gap;
        }
        return best;
    }

    // --- 카드 문장 -------------------------------------------------------

    // 카드 첫 줄 — "[CCTV]  새벽 12시 42분 ~ 새벽 12시 48분".
    public static string Title(EvidenceCard card)
    {
        if (card == null || card.Items.Count == 0) return "";
        string tag = card.Kind switch
        {
            EvidenceCardKind.CctvRange => "CCTV",
            EvidenceCardKind.MovePath => "이동 경로",
            _ => Tag(card.First),
        };
        string when = card.IsCluster
            ? DialogueClock.Range(card.First.AnchorTime, card.Last.AnchorTime)
            : card.First.TimeText;
        return string.IsNullOrEmpty(when) ? $"[{tag}]" : $"[{tag}]  {when}";
    }

    // 카드 둘째 줄 — 누가 · 어디서.
    public static string Body(EvidenceCard card)
    {
        if (card == null || card.Items.Count == 0) return "";
        var f = card.First;
        string who = InterviewEvidenceBoard.Codename(f.SubjectEmployeeId);
        switch (card.Kind)
        {
            case EvidenceCardKind.CctvRange:
                return $"{who} · {InterviewEvidenceBoard.RoomName(f.SubjectRoomId)}에서 지속 확인";
            case EvidenceCardKind.MovePath:
                var rooms = new List<string> { InterviewEvidenceBoard.RoomName(f.FromRoomId) };
                rooms.AddRange(card.Items.Select(e => InterviewEvidenceBoard.RoomName(e.ToRoomId)));
                return $"{who}   " + string.Join(" → ", rooms);
            default:
                return Body(f);
        }
    }

    // 자료 한 장의 둘째 줄.
    public static string Body(InterviewEvidence ev)
    {
        if (ev == null) return "";
        string who = InterviewEvidenceBoard.Codename(ev.SubjectEmployeeId);
        string room = InterviewEvidenceBoard.RoomName(ev.SubjectRoomId);
        return ev.Kind switch
        {
            EvidenceKind.Incident => ev.Body,
            EvidenceKind.Cctv => $"{who} · {room}"
                                 + (ev.RelatedEmployeeIds.Count > 0
                                     ? $"   (함께: {string.Join(", ", ev.RelatedEmployeeIds.Select(InterviewEvidenceBoard.Codename))})"
                                     : ""),
            EvidenceKind.Movement => $"{who} · {InterviewEvidenceBoard.RoomName(ev.FromRoomId)} → "
                                     + InterviewEvidenceBoard.RoomName(ev.ToRoomId)
                                     + (ev.PlayerOrdered ? "  (지시)" : ""),
            EvidenceKind.Testimony => $"{InterviewEvidenceBoard.Codename(ev.SpeakerEmployeeId)} → {who} · {room}에서 봤다",
            EvidenceKind.OwnStatement => $"{who} · \"{room}에 있었다\"",
            _ => $"{who} · {ev.Body}",
        };
    }

    // 펼친 상세 한 줄 — "새벽 1시 11분  여우 · 저장고".
    public static string DetailLine(InterviewEvidence ev) =>
        ev == null ? "" : (string.IsNullOrEmpty(ev.TimeText) ? "" : ev.TimeText + "   ") + Body(ev);

    // 아래 「자료 A / 자료 B」 칸에 넣는 한 줄.
    public static string SlotLine(InterviewEvidence ev) =>
        ev == null ? "" : $"[{Tag(ev)}]  " + DetailLine(ev);

    public static string Tag(InterviewEvidence ev) => ev?.Kind switch
    {
        EvidenceKind.Movement => "이동",
        EvidenceKind.Incident => "사고",
        EvidenceKind.Cctv => "CCTV",
        EvidenceKind.Testimony => "증언",
        EvidenceKind.OwnStatement => "진술",
        _ => "기분",
    };
}
