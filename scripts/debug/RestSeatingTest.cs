using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 휴게실 좌석 배치 검증 — "관계대로 앉는가, 그러면서도 매번 달라지는가".
//
//   godot --headless --path . res://scenes/debug/RestSeatingTest.tscn
//
// 자리를 수백 번 뽑아 통계로 본다. 한 번만 보면 운이 좋았는지 규칙이 맞는지 알 수 없다.
public partial class RestSeatingTest : Node
{
    private const int Trials = 400;

    // room_rest.tscn 의 Seats/Seat1~6 과 같은 좌표(뒷줄 셋 · 앞줄 셋).
    private static readonly Vector3[] Seats =
    {
        new(-1.05f, 0f, -0.86f), new(0f, 0f, -0.86f), new(1.05f, 0f, -0.86f),
        new(-1.05f, 0f, 0.86f), new(0f, 0f, 0.86f), new(1.05f, 0f, 0.86f),
    };

    private int _pass, _fail;

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        RelationshipSystem.Load();
        var sim = FacilitySimulation.Instance;
        var roster = sim != null && sim.GetEmployeeIds().Count > 0
            ? sim.GetEmployeeIds().ToList()
            : new List<string> { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

        GD.Print("################ 휴게실 좌석 배치 검증 ################");
        GD.Print($"직원 {roster.Count}명 · 자리 {Seats.Length}개 · {Trials}회 뽑기\n");

        // 자리 사이 가까움 — 옆자리가 가장 크고, 같은 줄 양 끝이 가장 작아야 한다.
        float neighbour = RestSeating.Closeness(Seats[0], Seats[1]);
        float facing = RestSeating.Closeness(Seats[0], Seats[3]);
        float diagonal = RestSeating.Closeness(Seats[0], Seats[4]);
        float ends = RestSeating.Closeness(Seats[0], Seats[2]);
        GD.Print($"[A] 가까움 — 옆자리 {neighbour:0.00} · 마주봄 {facing:0.00} · " +
                 $"대각 {diagonal:0.00} · 같은 줄 끝 {ends:0.00}");
        Check("A 옆자리가 가장 가깝다", neighbour > facing && neighbour > diagonal && neighbour > ends);
        Check("A 마주 보는 자리가 같은 줄 양 끝보다 가깝다", facing > ends);

        // 400번 뽑아 "누가 누구 옆에 앉았는가" 를 센다.
        var rng = new Random(20261007);
        var adjacent = new Dictionary<string, int>();
        var seatOf = new Dictionary<string, Dictionary<int, int>>();
        var shapes = new HashSet<string>();
        foreach (string id in roster) seatOf[id] = new Dictionary<int, int>();

        for (int t = 0; t < Trials; t++)
        {
            var order = RestSeating.Arrange(roster, Seats, rng);
            shapes.Add(string.Join(",", order));
            for (int i = 0; i < order.Count; i++)
            {
                if (string.IsNullOrEmpty(order[i])) continue;
                seatOf[order[i]][i] = seatOf[order[i]].GetValueOrDefault(i) + 1;
                for (int j = i + 1; j < order.Count; j++)
                {
                    if (string.IsNullOrEmpty(order[j])) continue;
                    // '옆자리' = 같은 줄에서 바로 옆(거리가 가장 가까운 쌍).
                    if (RestSeating.Closeness(Seats[i], Seats[j]) < neighbour - 0.01f) continue;
                    adjacent[Key(order[i], order[j])] = adjacent.GetValueOrDefault(Key(order[i], order[j])) + 1;
                }
            }
        }

        // ── 친한 쌍은 자주 붙어 앉는다 ───────────────────────────────
        GD.Print("\n[B] 옆자리에 앉은 횟수 (관계 점수 순)");
        var pairs = new List<(string A, string B, int Score, int Count)>();
        for (int i = 0; i < roster.Count; i++)
        for (int j = i + 1; j < roster.Count; j++)
            pairs.Add((roster[i], roster[j], RelationshipSystem.PairScore(roster[i], roster[j]),
                       adjacent.GetValueOrDefault(Key(roster[i], roster[j]))));
        foreach (var p in pairs.OrderByDescending(x => x.Score))
            GD.Print($"    {p.A,-7}{p.B,-7} 관계 {p.Score,4} → 옆자리 {p.Count,4}회 ({p.Count * 100 / Trials,3}%)");

        var good = pairs.Where(p => p.Score >= 40).ToList();
        var bad = pairs.Where(p => p.Score <= -30).ToList();
        double goodAvg = good.Average(p => (double)p.Count);
        double badAvg = bad.Average(p => (double)p.Count);
        GD.Print($"    사이 좋은 쌍 평균 {goodAvg:0}회 · 사이 나쁜 쌍 평균 {badAvg:0}회");
        Check("B 사이 좋은 쌍이 나쁜 쌍보다 훨씬 자주 옆에 앉는다", goodAvg > badAvg * 3);
        Check("B 사이가 아주 나쁜 쌍은 옆자리가 드물다", bad.All(p => p.Count * 100 / Trials <= 15));
        Check("B 가장 친한 쌍(고양이·강아지)은 절반 넘게 옆에 앉는다",
            pairs.First(p => Key(p.A, p.B) == Key("cat", "dog")).Count * 100 / Trials >= 50);

        // ── 여우는 사이가 덜 나쁜 쪽에 앉는다 ────────────────────────
        GD.Print("\n[C] 여우의 옆자리");
        var foxPairs = pairs.Where(p => p.A == "fox" || p.B == "fox")
            .Select(p => (Who: p.A == "fox" ? p.B : p.A, p.Score, p.Count))
            .OrderByDescending(x => x.Count).ToList();
        foreach (var f in foxPairs)
            GD.Print($"    여우 ↔ {f.Who,-7} 관계 {f.Score,4} → {f.Count,4}회 ({f.Count * 100 / Trials,3}%)");
        int near = foxPairs.Where(f => f.Who is "rabbit" or "sheep" or "dog").Sum(f => f.Count);
        int far = foxPairs.Where(f => f.Who is "cat" or "wolf").Sum(f => f.Count);
        GD.Print($"    토끼·양·강아지 쪽 {near}회  ·  고양이·늑대 쪽 {far}회");
        Check("C 여우는 토끼 · 양 · 강아지 쪽에 훨씬 자주 앉는다", near > far * 3);

        // ── 매번 같은 그림이 아니다 ──────────────────────────────────
        GD.Print($"\n[D] 서로 다른 배치 {shapes.Count}가지 / {Trials}회");
        Check("D 자리가 고정이 아니다(여러 배치가 나온다)", shapes.Count >= 5);
        // 한 사람이 늘 같은 의자에 앉지도 않는다.
        foreach (string id in roster)
        {
            int most = seatOf[id].Values.DefaultIfEmpty(0).Max();
            Check($"D {id} 가 늘 같은 의자에 앉지는 않는다 (최다 {most * 100 / Trials}%)",
                most * 100 / Trials <= 90);
        }

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private static string Key(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

    private void Check(string label, bool ok)
    {
        if (ok) _pass++; else _fail++;
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
    }
}
