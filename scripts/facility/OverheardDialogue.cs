using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;

namespace NSP.Facility;

public enum OverheardSituation { Normal, Incident, Sabotage }

// 같은 방 두 사람이 CCTV 너머로 주고받는 짧은 대화 한 번.
public sealed class OverheardExchange
{
    public string RoomId = "";
    public string A = "", B = "";
    public string LineA = "", LineB = "";
    public PairBand Band;
    public OverheardSituation Situation;
    public string Key = "";   // 고른 줄의 key (밴드 이름 또는 type:…)
}

// CCTV 2인 대사(관계 시스템 Phase 2) — "(밴드, 상황) → 미리 쓴 짧은 대화"를 한 곳에서 고른다.
// RoomStaffing · RelationshipSystem 과 같은 정적 창구. 표시는 CctvOverheardCaption 이 맡는다.
//
//   · 누가: 그 방에서 지금 근무 중인 두 사람(FacilitySimulation.OnDutyEmployeeIds)
//   · 관계: RelationshipSystem.Band — 동실 거부(Refuse)가 근무 중 재배치로 모였다면 불편(Uneasy) 줄을 쓴다.
//   · 상황: 최근 그 방의 방해공작 기록 → 방해 정황, 사고(업무 실패)·수리 대기·적색 경보 → 사고 직후, 그 외 평상.
//   · 대사: data/relationships/overheard_lines.tres (OverheardLineTableDef). 코드에는 문장이 없다.
public static class OverheardDialogue
{
    private sealed class Entry
    {
        public string Key = "";
        public OverheardSituation Situation;
        public string A = "", B = "";
    }

    private static readonly List<Entry> Entries = new();
    private static readonly Random Rng = new();
    // 같은 방에서 방금 나온 줄은 한동안 다시 고르지 않는다.
    private static readonly Dictionary<string, Queue<Entry>> Recent = new();
    private const int RecentPerRoom = 4;

    public static OverheardLineTableDef Table { get; private set; }

    public static void Load(string path = "res://data/relationships/overheard_lines.tres")
    {
        Entries.Clear();
        Recent.Clear();
        Table = ResourceLoader.Exists(path) ? GD.Load<OverheardLineTableDef>(path) : null;
        if (Table == null)
        {
            GD.PushWarning($"OverheardDialogue: 대사 테이블을 찾지 못했습니다: {path}");
            return;
        }

        foreach (string raw in Table.Lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] p = line.Split('|');
            if (p.Length < 4 || !TryParseSituation(p[1].Trim(), out var sit))
            {
                GD.PushWarning($"OverheardDialogue: 잘못된 줄(key|situation|A|B): {raw}");
                continue;
            }
            Entries.Add(new Entry { Key = p[0].Trim(), Situation = sit, A = p[2].Trim(), B = p[3].Trim() });
        }
    }

    public static int Count => Entries.Count;

    // 검증용 — 읽어 들인 줄 전부(key, 상황, A 틀, B 틀).
    public static IEnumerable<(string Key, OverheardSituation Situation, string A, string B)> AllTemplates() =>
        Entries.Select(e => (e.Key, e.Situation, e.A, e.B));

    // 한 key · 상황에 들어 있는 줄 수(검증용).
    public static int CountOf(string key, OverheardSituation situation) =>
        Entries.Count(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && e.Situation == situation);

    private static bool TryParseSituation(string s, out OverheardSituation sit)
    {
        switch (s.ToLowerInvariant())
        {
            case "normal": sit = OverheardSituation.Normal; return true;
            case "incident": sit = OverheardSituation.Incident; return true;
            case "sabotage": sit = OverheardSituation.Sabotage; return true;
            default: sit = OverheardSituation.Normal; return false;
        }
    }

    // ── 상황 판정 ─────────────────────────────────────────────────────────

    public static OverheardSituation SituationOf(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        if (sim == null || gs == null) return OverheardSituation.Normal;

        float now = gs.DayTimeSeconds;
        int day = gs.CurrentDay;
        float sabWin = Table?.SabotageWindowSeconds ?? 45f;
        float incWin = Table?.IncidentWindowSeconds ?? 35f;
        bool sabotage = false, incident = false;
        var entries = EventLog.Instance?.GetAllEntries();
        if (entries != null)
        {
            // 최근 기록만 보면 되므로 뒤에서부터 훑는다.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.Day != day) break;
                float age = now - e.GameTimeSeconds;
                if (age > Mathf.Max(sabWin, incWin)) break;
                if (e.RoomId != roomId) continue;
                if (e.EventType == LogEventType.Sabotage && age <= sabWin) sabotage = true;
                if (e.EventType == LogEventType.TaskFailed && age <= incWin) incident = true;
            }
        }
        if (sabotage) return OverheardSituation.Sabotage;

        var st = sim.GetRoomState(roomId);
        if (incident || sim.HasRepairPending(roomId) || st?.RedAlertLighting == true)
            return OverheardSituation.Incident;
        return OverheardSituation.Normal;
    }

    // ── 고르기 ───────────────────────────────────────────────────────────

    // 그 방에서 지금 들릴 대화 한 번. 두 사람 이상이 근무 중이 아니면 false.
    public static bool TryPick(string roomId, out OverheardExchange ex)
    {
        ex = null;
        var sim = FacilitySimulation.Instance;
        if (sim == null || Entries.Count == 0) return false;
        var here = sim.OnDutyEmployeeIds(roomId);
        if (here.Count < 2) return false;

        // 두 사람을 고른다 — 관계가 뚜렷한(중립이 아닌) 쌍을 조금 더 자주.
        var pairs = new List<(string, string)>();
        for (int i = 0; i < here.Count; i++)
            for (int j = i + 1; j < here.Count; j++)
            {
                pairs.Add((here[i], here[j]));
                if (RelationshipSystem.Band(here[i], here[j]) != PairBand.Neutral) pairs.Add((here[i], here[j]));
            }
        var (x, y) = pairs[Rng.Next(pairs.Count)];
        return TryPickFor(roomId, x, y, SituationOf(roomId), out ex);
    }

    // 정해진 두 사람 · 상황으로 고른다(검증 도구도 쓴다).
    public static bool TryPickFor(string roomId, string x, string y, OverheardSituation situation, out OverheardExchange ex)
    {
        ex = null;
        if (Entries.Count == 0) return false;
        var band = RelationshipSystem.Band(x, y);

        // ① 관계 타입 전용 줄 — A 가 B 에게 그 감정을 가진 쪽이어야 한다.
        var typed = new List<(Entry E, string A, string B)>();
        foreach (var (a, b) in new[] { (x, y), (y, x) })
        {
            string key = "type:" + RelationshipSystem.TypeOf(a, b);
            foreach (var e in Candidates(roomId, key, situation)) typed.Add((e, a, b));
        }
        if (typed.Count > 0 && Rng.NextDouble() < (Table?.TypeLineChance ?? 0.5f))
        {
            var t = typed[Rng.Next(typed.Count)];
            ex = Build(roomId, t.E, t.A, t.B, band, situation);
            return true;
        }

        // ② 밴드 줄 — 밀접 줄이 없으면 우호 줄, 거부는 불편 줄로 대신한다.
        foreach (string key in BandKeys(band))
        {
            var list = Candidates(roomId, key, situation).ToList();
            if (list.Count == 0) continue;
            var e = list[Rng.Next(list.Count)];
            bool swap = Rng.Next(2) == 0;
            ex = Build(roomId, e, swap ? y : x, swap ? x : y, band, situation);
            return true;
        }

        // ③ 그 상황 줄이 하나도 없으면 평상 줄로.
        return situation != OverheardSituation.Normal
               && TryPickFor(roomId, x, y, OverheardSituation.Normal, out ex);
    }

    private static IEnumerable<string> BandKeys(PairBand band) => band switch
    {
        PairBand.Close => new[] { "Close", "Friendly" },
        PairBand.Friendly => new[] { "Friendly" },
        PairBand.Refuse => new[] { "Uneasy" },
        PairBand.Uneasy => new[] { "Uneasy" },
        _ => new[] { "Neutral" },
    };

    private static IEnumerable<Entry> Candidates(string roomId, string key, OverheardSituation situation)
    {
        var all = Entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && e.Situation == situation).ToList();
        if (Recent.TryGetValue(roomId, out var q))
        {
            var fresh = all.Where(e => !q.Contains(e)).ToList();
            if (fresh.Count > 0) return fresh;
        }
        return all;
    }

    private static OverheardExchange Build(string roomId, Entry e, string a, string b, PairBand band, OverheardSituation sit)
    {
        if (!Recent.TryGetValue(roomId, out var q)) Recent[roomId] = q = new Queue<Entry>();
        q.Enqueue(e);
        while (q.Count > RecentPerRoom) q.Dequeue();

        return new OverheardExchange
        {
            RoomId = roomId, A = a, B = b, Band = band, Situation = sit, Key = e.Key,
            LineA = Fill(e.A, a, b, a),
            LineB = Fill(e.B, a, b, b),
        };
    }

    // {a}/{b} 를 코드네임으로 채우고 조사를 맞춘 뒤, 말하는 사람의 말버릇(양의 더듬기 · 여우의 "~")을 입힌다.
    private static string Fill(string template, string a, string b, string speaker)
    {
        var sim = FacilitySimulation.Instance;
        string na = sim?.GetEmployeeDef(a)?.Codename ?? a;
        string nb = sim?.GetEmployeeDef(b)?.Codename ?? b;
        string text = KoreanParticle.Resolve(template.Replace("{a}", na).Replace("{b}", nb));
        return DialogueVoiceTics.Apply(text, DialogueVoices.Get(speaker));
    }
}
