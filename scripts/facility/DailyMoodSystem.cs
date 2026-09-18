using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 전날 겪은 사건의 강도. DAY1 은 이전 사건이 없으므로 항상 None 이다.
// 이후 DAY 에서 사고/목격/사망 기록을 읽어 Light / Strong 을 채우는 자리다.
public enum MoodEventLevel
{
    None,
    Light,
    Strong,
}

// 직원별 "오늘의 기분상태"를 하루에 한 번 고른다.
//
// 규칙(docs/NSP_V3_REWORK_REVISED.md §5·§8·§9·§12):
//  · 완전 랜덤 금지 — 캐릭터별 표현 풀(MoodPoolDef) 안에서만 고른다.
//  · 방해자 여부는 선택에 전혀 쓰지 않는다. 이 클래스는 SaboteurEmployeeId 를 읽지 않는다.
//  · 모호한 표현("잘 모르겠음" 등)은 한 DAY 에 MoodVagueMaxPerDay 명까지만.
//  · 같은 직원이 어제와 똑같은 표현을 반복하지 않는다.
//
// 감정 수치 / 정신력 / 호감도 같은 새 게이지는 만들지 않는다 — 결과는 표시용 문구 하나뿐이다.
public sealed class DailyMoodSystem
{
    private readonly Dictionary<string, MoodPoolDef> _pools = new();
    private readonly Random _rng = new();

    public DailyMoodSystem()
    {
        foreach (string path in ResourceDir.ListFiles("res://data/moods/", ".tres"))
        {
            var res = GD.Load<MoodPoolDef>(path);
            if (res != null && !string.IsNullOrEmpty(res.EmployeeId))
                _pools[res.EmployeeId] = res;
        }
    }

    public MoodPoolDef GetPool(string employeeId) => _pools.GetValueOrDefault(employeeId);

    // 하루치 기분을 전원에게 새로 배정한다. levelOf 가 null 이면 전원 None(= DAY1).
    public void RollForDay(IEnumerable<EmployeeState> states, Func<string, MoodEventLevel> levelOf = null)
    {
        int vagueLeft = Math.Max(0, Config.Instance?.Data?.MoodVagueMaxPerDay ?? 2);

        // 앞 직원이 모호 표현을 먼저 채가는 편향이 생기지 않게 매번 순서를 섞는다.
        foreach (var st in states.Where(s => s != null).OrderBy(_ => _rng.Next()))
        {
            var level = levelOf?.Invoke(st.EmployeeId) ?? MoodEventLevel.None;
            st.PreviousMood = st.DailyMood;
            st.DailyMood = Pick(st.EmployeeId, level, st.PreviousMood, ref vagueLeft);
        }
    }

    private string Pick(string employeeId, MoodEventLevel level, string previousMood, ref int vagueLeft)
    {
        var pool = _pools.GetValueOrDefault(employeeId);
        if (pool == null) return "";

        var candidates = new List<string>(BaseMoods(pool, level));

        // 모호한 표현은 후보에 "섞일 뿐"이라 반드시 나오지는 않는다. 오늘 배정 한도가 남아
        // 있을 때만 후보에 올리므로, 하루에 최대 MoodVagueMaxPerDay 명까지만 쓰게 된다.
        var vague = pool.VagueMoods;
        if (vagueLeft > 0 && vague != null) candidates.AddRange(vague);

        if (candidates.Count == 0) return "";

        // 어제와 같은 표현은 뺀다 — 단, 후보가 그것뿐이면 그대로 쓴다.
        if (!string.IsNullOrEmpty(previousMood) && candidates.Count > 1)
            candidates.RemoveAll(m => m == previousMood);

        string picked = candidates[_rng.Next(candidates.Count)];
        if (vague != null && vague.Contains(picked)) vagueLeft--;
        return picked;
    }

    // 사건 강도에 맞는 풀. 해당 풀이 비어 있으면 한 단계 약한 쪽으로 내려간다.
    private static IEnumerable<string> BaseMoods(MoodPoolDef pool, MoodEventLevel level)
    {
        if (level == MoodEventLevel.Strong && pool.StrongEventMoods?.Count > 0) return pool.StrongEventMoods;
        if (level >= MoodEventLevel.Light && pool.LightEventMoods?.Count > 0) return pool.LightEventMoods;
        return pool.CalmMoods ?? new Godot.Collections.Array<string>();
    }
}
