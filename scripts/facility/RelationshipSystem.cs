using System;
using System.Collections.Generic;
using Godot;
using NSP.Data;

namespace NSP.Facility;

public enum PairBand { Refuse, Uneasy, Neutral, Friendly, Close }

// 관계값 판정을 한 곳에서 답한다. RoomStaffing 과 같은 정적 창구 패턴.
//
// 판정 결과를 가져다 쓰는 곳:
//   - ScheduleScreen(배치): CanCoAssign / Band 로 거부·경고
//   - FacilitySimulation·RoomStaffing(운영): Band 로 소프트 페널티
//   - InterviewSession·DialogueComposer(심문): ReportBias 로 증언 편향
//   - CctvView(감시): Band + TypeOf 로 2인 대사 색 결정
//
// 게임 시작 시(또는 GameState 초기화 시) 한 번 Load() 를 호출한다.
public static class RelationshipSystem
{
    private static readonly Dictionary<(string From, string To), int> Aff = new();
    private static readonly Dictionary<(string From, string To), RelationType> Kind = new();
    private static readonly HashSet<(string Who, string Of)> FearOf = new();
    private static readonly HashSet<(string Who, string On)> LooksDownOn = new();
    private static RelationshipTableDef _table;

    public static void Load(string path = "res://data/relationships/relationships.tres")
    {
        Aff.Clear(); Kind.Clear(); FearOf.Clear(); LooksDownOn.Clear();

        _table = GD.Load<RelationshipTableDef>(path);
        if (_table == null)
        {
            GD.PushWarning($"RelationshipSystem: 테이블을 찾지 못했습니다: {path}");
            return;
        }

        foreach (string raw in _table.Seed)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            string[] p = line.Split(',');
            if (p.Length < 4)
            {
                GD.PushWarning($"RelationshipSystem: 잘못된 시드 줄(필드 4개 필요): {raw}");
                continue;
            }

            string from = p[0].Trim();
            string to = p[1].Trim();
            if (!int.TryParse(p[2].Trim(), out int affinity))
            {
                GD.PushWarning($"RelationshipSystem: affinity 파싱 실패: {raw}");
                continue;
            }
            Enum.TryParse(p[3].Trim(), true, out RelationType type);

            Aff[(from, to)] = Mathf.Clamp(affinity, -100, 100);
            Kind[(from, to)] = type;

            if (p.Length >= 5)
            {
                foreach (string flag in p[4].Split('|'))
                {
                    switch (flag.Trim().ToLowerInvariant())
                    {
                        case "fear": FearOf.Add((from, to)); break;
                        case "looksdown": LooksDownOn.Add((from, to)); break;
                        case "admire": break; // 현재는 대사 색용. 필요 시 별도 Set 추가.
                    }
                }
            }
        }
    }

    // ---- 조회 -------------------------------------------------------------

    public static int Affinity(string from, string to)
        => Aff.GetValueOrDefault((from, to), 0);

    public static RelationType TypeOf(string from, string to)
        => Kind.GetValueOrDefault((from, to), RelationType.Neutral);

    public static bool Fears(string who, string of) => FearOf.Contains((who, of));
    public static bool LooksDown(string who, string on) => LooksDownOn.Contains((who, on));

    // 배치·페널티는 두 방향을 합친 평균값으로 본다.
    public static int PairScore(string a, string b)
        => (Affinity(a, b) + Affinity(b, a)) / 2;

    public static PairBand Band(string a, string b)
    {
        int s = PairScore(a, b);
        int refuse = _table?.RefuseAtOrBelow ?? -70;
        int uneasy = _table?.UneasyAtOrBelow ?? -30;
        int friendly = _table?.FriendlyAtOrAbove ?? 30;
        int close = _table?.CloseAtOrAbove ?? 70;

        if (s <= refuse) return PairBand.Refuse;
        if (s <= uneasy) return PairBand.Uneasy;
        if (s >= close) return PairBand.Close;
        if (s >= friendly) return PairBand.Friendly;
        return PairBand.Neutral;
    }

    // ---- 배치 판정 --------------------------------------------------------

    // 두 명을 같은 방에 둘 수 있는가? (극혐 쌍은 false)
    public static bool CanCoAssign(string a, string b)
        => Band(a, b) != PairBand.Refuse;

    // 한 방의 인원 전체가 동실 가능한가? refused 에 거부 쌍을 담아준다.
    public static bool CanCoAssignAll(IReadOnlyList<string> roomMembers,
                                      out List<(string A, string B)> refused)
    {
        refused = new List<(string, string)>();
        for (int i = 0; i < roomMembers.Count; i++)
            for (int j = i + 1; j < roomMembers.Count; j++)
                if (!CanCoAssign(roomMembers[i], roomMembers[j]))
                    refused.Add((roomMembers[i], roomMembers[j]));
        return refused.Count == 0;
    }

    // ---- 런타임 변화 (Phase 3) --------------------------------------------

    // symmetric=true 면 반대 방향도 같은 양만큼 움직인다.
    public static void Apply(string from, string to, int delta, bool symmetric = false)
    {
        Aff[(from, to)] = Mathf.Clamp(Affinity(from, to) + delta, -100, 100);
        if (symmetric)
            Aff[(to, from)] = Mathf.Clamp(Affinity(to, from) + delta, -100, 100);
    }

    // ---- 증언 편향 (Phase 3) ----------------------------------------------

    // observer 가 about 에 대해 얼마나 감싸거나(+) 몰아세우는지(-).
    //  -1.0(적극 지목) ~ +1.0(적극 은폐)
    public static float ReportBias(string observer, string about)
    {
        float bias = Mathf.Clamp(Affinity(observer, about) / 100f, -1f, 1f);
        if (Fears(observer, about)) bias += 0.25f;     // 무서워서 보고를 꺼림
        if (LooksDown(observer, about)) bias -= 0.15f; // 깔봐서 증언을 진지하게 안 함
        return Mathf.Clamp(bias, -1f, 1f);
    }
}
