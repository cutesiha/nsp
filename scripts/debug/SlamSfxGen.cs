using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 차폐문이 닫히는 **쿠웅** 소리를 합성해 assets/audio/sfx/bulkhead_slam.wav 로 쓴다.
//
//   godot --headless --path . res://scenes/debug/SlamSfxGen.tscn --quit-after 600
//
// 프로젝트 규격 그대로 : 22050Hz · 16bit · 모노(SirenSfxGen · VentSfxGen 과 동일).
//
// ── 어떤 소리인가 ────────────────────────────────────────────────────────
// 수십 톤짜리 금속 차폐문이 콘크리트 바닥에 닿는 소리다. 기존 metal_clang 은
// 1.4초짜리 **쇳소리**라서 '쨍' 하고 끝난다 — 무게가 실리지 않는다.
// 무거운 충돌음은 네 겹으로 되어 있다.
//
//   ① 접지 순간    아주 짧은(20ms) 둔탁한 타격. 높은 쪽은 깎아 둔다 — 날카로우면
//                  금속판을 때린 소리가 되고, 문이 '내려앉은' 느낌이 사라진다.
//   ② 저음 덩어리  62Hz → 34Hz 로 떨어지는 사인. 이게 가슴을 치는 '쿠-' 다.
//   ③ 문의 울림    88 · 131 · 178Hz 의 낮은 배음이 1초 남짓 둔하게 운다. 살짝
//                  어긋나게 두어(디튠) 한 음처럼 들리지 않게 한다.
//   ④ 꼬리         저역만 남은 잡음이 2초 넘게 깔린다 — 바닥과 벽을 타고 가는
//                  울림과 떨어지는 부스러기. 이 꼬리가 '규모' 를 만든다.
//
// 합을 tanh 로 눌러 둔다. 피크를 그냥 자르면 저음이 '퍼석' 하고 찌그러진다.
public partial class SlamSfxGen : Node
{
    private const int Rate = 22050;
    private const double Seconds = 2.7;

    // 가장 큰 소리 중 하나다(프롤로그에서 폭발 다음). 다만 폭발보다 조금 아래.
    private const double TargetPeak = 0.97;

    private readonly Random _rng = new(20261010);

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 차폐문 쿠웅 합성 ################");
        var buf = Make();
        Write(Path.Combine(dir, "bulkhead_slam.wav"), buf);
        Report("bulkhead_slam.wav", buf);
        GetTree().Quit();
    }

    // 한 극(pole) 저역통과. a 는 차단주파수에서 구한다.
    private static double Coef(double hz) => 1.0 - Math.Exp(-Mathf.Tau * hz / Rate);

    private double[] Make()
    {
        int n = (int)(Seconds * Rate);
        var buf = new double[n];

        double hitLp = 0, tailLp1 = 0, tailLp2 = 0;
        double aHit = Coef(1100), aTail1 = Coef(320), aTail2 = Coef(170);

        // ③ 문의 울림 — 위상을 미리 잡아 둔다.
        double[] ringHz = { 88.0, 131.5, 178.0, 241.0 };
        double[] ringAmp = { 0.34, 0.22, 0.13, 0.06 };
        double[] ringTau = { 0.62, 0.44, 0.30, 0.18 };
        var ringPh = new double[ringHz.Length];
        for (int k = 0; k < ringPh.Length; k++) ringPh[k] = _rng.NextDouble() * Mathf.Tau;

        double subPh = 0;

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double v = 0;

            // ① 접지 순간 : 20ms 안에 사그라드는 둔탁한 타격.
            double rawHit = _rng.NextDouble() * 2 - 1;
            hitLp += (rawHit - hitLp) * aHit;
            v += hitLp * 0.95 * Math.Exp(-t / 0.018);

            // ② 저음 덩어리 : 62Hz 에서 34Hz 로 내려앉는다.
            double subHz = 34.0 + 28.0 * Math.Exp(-t / 0.22);
            subPh += Mathf.Tau * subHz / Rate;
            v += Math.Sin(subPh) * 1.15 * Math.Exp(-t / 0.42);
            // 한 옥타브 위를 아주 조금 섞어 작은 스피커에서도 '쿠웅' 이 남게 한다.
            v += Math.Sin(subPh * 2.0) * 0.20 * Math.Exp(-t / 0.26);

            // ③ 문의 울림.
            for (int k = 0; k < ringHz.Length; k++)
            {
                ringPh[k] += Mathf.Tau * ringHz[k] / Rate;
                v += Math.Sin(ringPh[k]) * ringAmp[k] * Math.Exp(-t / ringTau[k]);
            }

            // ④ 꼬리 : 저역만 남은 잡음. 느리게 울렁이며 2초 넘게 깔린다.
            double rawTail = _rng.NextDouble() * 2 - 1;
            tailLp1 += (rawTail - tailLp1) * aTail1;
            tailLp2 += (tailLp1 - tailLp2) * aTail2;
            double wob = 1.0 + 0.35 * Math.Sin(Mathf.Tau * 2.3 * t + 1.1);
            v += tailLp2 * 2.2 * Math.Exp(-t / 0.95) * wob;

            // 맨 끝 30ms 는 재워 둔다 — 잘린 끝이 '딱' 으로 들리지 않게.
            double outFade = Math.Min(1.0, (n - 1 - i) / (Rate * 0.03));
            buf[i] = Math.Tanh(v * 0.92) * outFade;
        }
        return buf;
    }

    private static void Write(string path, double[] s)
    {
        double hi = 0;
        foreach (double v in s) hi = Math.Max(hi, Math.Abs(v));
        double k = TargetPeak / Math.Max(1e-9, hi);

        using var f = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        using var w = new BinaryWriter(f);
        int dataBytes = s.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (double v in s) w.Write((short)(Math.Clamp(v * k, -1.0, 1.0) * 32767));
    }

    // 길이 · 피크 · RMS · 저역 비중을 수치로 남긴다. 저역이 얇으면 '쿠웅' 이 아니다.
    private static void Report(string name, double[] s)
    {
        double hi = 0, sum = 0;
        foreach (double v in s) { hi = Math.Max(hi, Math.Abs(v)); sum += v * v; }
        double k = TargetPeak / Math.Max(1e-9, hi);
        double rms = Math.Sqrt(sum / s.Length) * k;

        // 120Hz 저역통과를 한 번 더 걸어 저음이 전체의 얼마인지 본다.
        double a = 1.0 - Math.Exp(-Mathf.Tau * 120.0 / Rate), lp = 0, lowSum = 0;
        foreach (double v in s) { lp += (v - lp) * a; lowSum += lp * lp; }
        double lowRms = Math.Sqrt(lowSum / s.Length) * k;

        GD.Print($"{name}: {s.Length / (double)Rate:0.00}s  피크 {hi * k:0.00}  RMS {rms:0.000}");
        GD.Print($"  120Hz 이하 RMS {lowRms:0.000}  (전체의 {lowRms / Math.Max(1e-9, rms) * 100:0}%)");
        GD.Print(lowRms / Math.Max(1e-9, rms) > 0.5
            ? "  → 저음이 소리의 절반 이상이다. 둔탁한 쿠웅."
            : "  !! 저음이 얇다 — 금속 때리는 소리로 들린다.");
    }
}
