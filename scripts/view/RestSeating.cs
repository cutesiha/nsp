using System;
using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 휴게실 좌석 배치 — "누가 누구 옆에 앉는가"만 정한다. **표현 전용이다.**
//
// 고정 좌석도 아니고 완전 무작위도 아니다. 관계도(RelationshipSystem.PairScore)를 그대로
// 읽어 **가까이 앉을수록 점수가 크게 반영되는** 배치를 고른다. 그래서
//   · 사이가 좋은 둘은 옆자리에 자주 앉고
//   · 사이가 나쁜 둘은 테이블 반대쪽 끝으로 밀려난다
// 결과가 저절로 나온다. 이름을 코드에 박지 않았으므로 relationships.tres 를 고치면
// 자리도 따라 바뀐다(여우가 토끼·강아지 쪽에 자주 앉는 것도 그 표에서 나오는 결과다).
//
// 매번 같은 그림이 되지 않도록, 최적해 하나만 쓰지 않고 "충분히 좋은" 배치 여럿 중에서
// 무작위로 하나를 고른다.
public static class RestSeating
{
    // 자리 사이의 가까움. 거리가 멀수록 빠르게 작아진다 —
    // 옆자리(1.05m) 0.48 · 마주봄(1.72m) 0.25 · 대각(2.01m) 0.20 · 같은 줄 끝(2.10m) 0.18.
    // 그래서 "옆에 앉혔는가" 가 배치를 가장 크게 좌우한다.
    public static float Closeness(Vector3 a, Vector3 b)
    {
        float d = new Vector2(a.X - b.X, a.Z - b.Z).Length();
        return 1f / (1f + d * d);
    }

    // 이 배치가 얼마나 "관계대로" 인가. 클수록 좋다.
    public static float Score(IReadOnlyList<string> order, IReadOnlyList<Vector3> seats)
    {
        float total = 0f;
        for (int i = 0; i < order.Count; i++)
        for (int j = i + 1; j < order.Count; j++)
        {
            if (string.IsNullOrEmpty(order[i]) || string.IsNullOrEmpty(order[j])) continue;
            total += Closeness(seats[i], seats[j]) * RelationshipSystem.PairScore(order[i], order[j]);
        }
        return total;
    }

    // 좌석 순서를 만든다. 반환값[i] = i 번 자리에 앉을 직원 id(남는 자리는 빈 문자열).
    //
    //   ① 무작위로 섞어 놓고 → 두 사람을 바꿔 점수가 오르면 받아들이는 식으로 다듬는다(언덕오르기)
    //   ② 이것을 여러 번 되풀이해 후보를 모은다
    //   ③ 가장 좋은 점수에서 Tolerance 안쪽에 드는 후보들 중 하나를 무작위로 고른다
    //
    // ③ 덕분에 "관계는 지키되 매번 조금씩 다른" 자리가 나온다.
    public static List<string> Arrange(IReadOnlyList<string> employees, IReadOnlyList<Vector3> seats,
                                       Random rng, int tries = 12)
    {
        int n = seats.Count;
        var best = new List<List<string>>();
        float bestScore = float.NegativeInfinity;

        for (int t = 0; t < Math.Max(1, tries); t++)
        {
            var order = new List<string>(n);
            for (int i = 0; i < n; i++) order.Add(i < employees.Count ? employees[i] : "");
            Shuffle(order, rng);
            HillClimb(order, seats);

            float s = Score(order, seats);
            if (s > bestScore + 0.001f) { bestScore = s; best.Clear(); best.Add(order); }
            else if (s >= bestScore - Tolerance) best.Add(order);
        }
        return best.Count == 0 ? new List<string>() : best[rng.Next(best.Count)];
    }

    // 최고 점수에서 이만큼까지는 "똑같이 괜찮은 배치"로 보고 후보에 넣는다.
    //
    // 자리가 달라지는 것은 주로 이 값 때문이 아니라, 언덕오르기가 **서로 다른 꼭대기**에
    // 올라서기 때문이다(여섯 명 기준 열두 가지쯤 나온다). 값을 크게 올려도 눈에 띄게
    // 늘지 않고, 대신 사이 나쁜 둘이 옆에 앉는 일이 생기기 시작한다.
    private const float Tolerance = 18f;

    private static void Shuffle(List<string> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = rng.Next(i + 1);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }

    // 두 자리를 바꿔 점수가 오르면 받아들인다. 더 오를 데가 없으면 멈춘다.
    private static void HillClimb(List<string> order, IReadOnlyList<Vector3> seats)
    {
        float current = Score(order, seats);
        for (int pass = 0; pass < 40; pass++)
        {
            bool improved = false;
            for (int i = 0; i < order.Count; i++)
            for (int j = i + 1; j < order.Count; j++)
            {
                (order[i], order[j]) = (order[j], order[i]);
                float s = Score(order, seats);
                if (s > current + 0.001f) { current = s; improved = true; }
                else (order[i], order[j]) = (order[j], order[i]);
            }
            if (!improved) return;
        }
    }
}
