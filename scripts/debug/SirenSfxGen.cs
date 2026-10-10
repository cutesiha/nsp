using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 시설 경보 사이렌을 직접 합성해 assets/audio/sfx/siren.wav 로 쓴다.
//
//   godot --headless --path . res://scenes/debug/SirenSfxGen.tscn --quit-after 600
//
// 프로젝트 규격 그대로 : 22050Hz · 16bit · 모노(VentSfxGen 과 동일).
//
// ── 왜 이 값인가 ─────────────────────────────────────────────────────────
// 참고로 받아 왔던 음원을 SirenProbe 로 **실측**한 결과를 그대로 옮긴 것이다 :
//
//     주기        8.0 초
//     음높이      403 Hz(바닥) → 558 Hz(꼭대기)
//     모양        2.0초 상승 → 3.0초 유지 → 3.0초 하강
//     세기        거의 일정하고 꼭대기에서 조금 커진다
//     RMS         0.14
//
// ── 소리의 정체 ──────────────────────────────────────────────────────────
// 전동 사이렌은 사인파가 아니다. 회전자가 공기를 토막 내며 내는 소리라서 배음이
// 촘촘하게 쌓인 **거친 톤**이고, 그 배음 구조가 "경보" 로 들리게 만든다.
// 사인파 하나만 쓸어 올리면 테스트 톤처럼 들린다.
//
// 이음매 : 길이를 정확히 한 주기(8초)로 잡고, 8초 동안 쌓인 위상이 2π의 정수배가
// 되도록 주파수 전체를 아주 조금(0.1% 미만) 늘여 맞춘다. 그래야 반복 재생할 때
// 경계에서 딱 소리가 나지 않는다.
public partial class SirenSfxGen : Node
{
    private const int Rate = 22050;

    // 한 주기가 8초였을 때는 쓸어 올리는 소리가 너무 더뎌서 "경보" 로 들리지 않았다.
    // 상승 2.5초 · 하강 1.0초로 줄이고, 꼭대기에 머무는 1.5초는 그대로 두었다 —
    // 그래서 주기가 5초다. 훑는 속도가 두 배 가까이 빨라져 다급하게 들린다.
    private const double Period = 5.0;    // 한 주기(초)
    private const double LoHz = 403.0;    // 바닥 음높이
    private const double HiHz = 558.0;    // 꼭대기 음높이
    // 일부러 실제 민방위 경보의 박자(5초 상승 / 3초 하강)를 쓰지 않는다. 녹음을 베끼지
    // 않더라도 실제 경보 신호와 같은 패턴을 그대로 내보내는 것은 피한다.
    private const double RiseSec = 2.5;   // 올라가는 데 걸리는 시간
    private const double FallSec = 1.0;   // 내려오는 데 걸리는 시간
    private const double HoldSec = Period - RiseSec - FallSec;   // 꼭대기에서 머무는 시간
    // 시설 전체에 울리는 경보다. 작게 깔리면 안 된다.
    private const double TargetRms = 0.36;

    private readonly Random _rng = new(20261008);

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 사이렌 합성 ################");
        var buf = Make(Period);
        Write(Path.Combine(dir, "siren.wav"), buf);
        Report("siren.wav", buf);
        GetTree().Quit();
    }

    // 한 주기 동안의 음높이(Hz). 상승 → 유지 → 하강.
    private static double FreqAt(double t)
    {
        double fall = FallSec;
        if (t < RiseSec)
        {
            // 기계가 돌기 시작하는 느낌 — 처음엔 더디게 붙었다가 끝에서 빨리 올라간다.
            double k = t / RiseSec;
            return Mathf.Lerp((float)LoHz, (float)HiHz, (float)(k * k * (3 - 2 * k)));
        }
        if (t < RiseSec + HoldSec) return HiHz;
        double j = (t - RiseSec - HoldSec) / fall;
        // 관성 때문에 내려올 때가 더 길고 완만하다.
        return Mathf.Lerp((float)HiHz, (float)LoHz, (float)(j * j * (3 - 2 * j)));
    }

    private double[] Make(double seconds)
    {
        int n = (int)(seconds * Rate);
        var outBuf = new double[n];

        // ── 이음매 맞추기 ────────────────────────────────────────────────
        // 8초 동안 쌓이는 위상을 먼저 재고, 그것이 2π 의 정수배가 되도록 주파수를
        // 통째로 아주 조금 보정한다. 보정폭은 0.1% 미만이라 귀로는 같은 소리다.
        double raw = 0;
        for (int i = 0; i < n; i++) raw += FreqAt(i / (double)Rate) / Rate;
        double tune = Math.Round(raw) / raw;
        GD.Print($"위상 보정 {(tune - 1) * 100:0.000}%  (한 주기 {raw:0.0} → {raw * tune:0.0} 파)");

        // 전동 사이렌의 배음 구조. 홀수 배음이 조금 더 서 있어야 '쇳소리' 가 난다.
        double[] harm = { 1.00, 0.62, 0.78, 0.34, 0.45, 0.20, 0.24, 0.11, 0.13 };

        double phase = 0, noiseLp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double f = FreqAt(t) * tune;
            phase += Mathf.Tau * f / Rate;

            double v = 0;
            for (int h = 0; h < harm.Length; h++)
            {
                // 높은 배음은 음이 올라갈수록 조금씩 죽는다(실제 혼이 그렇다).
                double roll = 1.0 - 0.30 * (f - LoHz) / (HiHz - LoHz) * h / harm.Length;
                v += harm[h] * Math.Sin(phase * (h + 1)) * Math.Max(0.0, roll);
            }
            v /= 3.0;

            // 회전자가 공기를 치는 숨소리 — 아주 약하게 섞어야 '기계' 가 된다.
            // 잡음은 되풀이되지 않으므로 양끝 10ms 에서 재워 둔다. 안 그러면 반복
            // 재생할 때 그 불연속이 '딱' 소리로 들린다.
            noiseLp += ((_rng.NextDouble() * 2 - 1) - noiseLp) * 0.035;
            int edge = Rate / 100;
            double nf = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)edge);
            v += noiseLp * 0.07 * nf;

            // 세기 : 거의 일정하고 꼭대기에서 조금 커진다(실측과 같다).
            double up = (FreqAt(t) - LoHz) / (HiHz - LoHz);
            double amp = 0.86 + 0.14 * up;
            // 회전 때문에 생기는 느린 울렁임.
            amp *= 1.0 + 0.05 * Math.Sin(Mathf.Tau * 3.0 * t);

            outBuf[i] = v * amp;
        }
        return outBuf;
    }

    private static void Write(string path, double[] s)
    {
        // 피크가 아니라 RMS 를 맞춘다 — 기존 사이렌과 같은 크기로 들려야
        // 프롤로그 · 엔딩에 적어 둔 볼륨(dB) 값을 그대로 쓸 수 있다.
        double sum = 0;
        foreach (double v in s) sum += v * v;
        double rms = Math.Sqrt(sum / s.Length);
        double k = TargetRms / Math.Max(1e-9, rms);
        double hi = 0;
        foreach (double v in s) hi = Math.Max(hi, Math.Abs(v * k));
        if (hi > 0.97) k *= 0.97 / hi;   // 클리핑 방지

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

    // 길이 · 피크 · RMS · 이음매 오차를 수치로 남긴다.
    private static void Report(string name, double[] s)
    {
        double hi = 0, sum = 0;
        foreach (double v in s) { hi = Math.Max(hi, Math.Abs(v)); sum += v * v; }
        double rms = Math.Sqrt(sum / s.Length);
        double k = TargetRms / Math.Max(1e-9, rms);
        double seam = Math.Abs(s[0] - s[^1]) * k;
        // 이음매가 '큰지' 는 평소 샘플 간 변화량과 견줘야 안다. 비슷하면 안 들린다.
        double step = 0, stepMax = 0;
        for (int i = 1; i < s.Length; i++)
        {
            double d = Math.Abs(s[i] - s[i - 1]) * k;
            step += d;
            stepMax = Math.Max(stepMax, d);
        }
        step /= s.Length - 1;
        GD.Print($"{name}: {s.Length / (double)Rate:0.00}s  피크 {hi * k:0.00}  RMS {rms * k:0.000}");
        GD.Print($"  이음매 {seam:0.0000}  (평소 샘플간 변화 평균 {step:0.0000} · 최대 {stepMax:0.0000})");
        GD.Print(seam <= stepMax ? "  → 이음매가 평소 변화 범위 안이다. 딱 소리 없음."
                                 : "  !! 이음매가 평소보다 크다 — 반복 재생에서 딱 소리가 난다.");
    }
}
