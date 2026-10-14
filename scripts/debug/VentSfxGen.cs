using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace NSP.Debug;

// 환풍기 효과음 3종을 직접 합성해 assets/audio/sfx/ 에 덮어쓴다.
//
//   godot --headless --path . res://scenes/debug/VentSfxGen.tscn --quit-after 20000
//
// 프로젝트의 기존 효과음 규격과 같다: 22050Hz · 16bit · 모노.
// (tools/*.py 생성기들과 같은 역할이지만 이 컴퓨터에 파이썬이 없어 C# 으로 썼다.
//  알고리즘은 아래 주석에 그대로 적어 두었으므로 파이썬으로 옮기기 쉽다.)
//
// 밋밋한 백색소음과 갈리는 핵심은 "블레이드 통과음" 이다 — 팬 날개가 덕트를 스치며
// 만드는 약 20Hz 의 진폭 변조. 이것이 있어야 "후우우웅" 하고 도는 기계로 들린다.
public partial class VentSfxGen : Node
{
    private const int Rate = 22050;
    // 블레이드 통과 주파수(Hz). 4초 루프에 정확히 80주기가 들어가 이음매가 생기지 않는다.
    private const double BladeHz = 20.0;
    // 모터 기본 하모닉(Hz). 4초에 480주기 — 역시 정수배.
    private const double MotorHz = 120.0;

    private readonly Random _rng = new(20260101);

    public override void _Ready()
    {
        string outDir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 환풍기 효과음 합성 ################");

        var loop = MakeLoop(4.0);
        var stop = MakeStop(3.5);
        var restart = MakeRestart(2.8);

        Write(Path.Combine(outDir, "vent_loop.wav"), loop, 0.72);
        Write(Path.Combine(outDir, "vent_stop.wav"), stop, 0.80);
        Write(Path.Combine(outDir, "vent_restart.wav"), restart, 0.80);

        GD.Print("\n---------------- 검증 ----------------");
        Report("vent_loop", loop, true);
        Report("vent_stop", stop, false);
        Report("vent_restart", restart, false);
        BladeCheck("vent_loop", loop);

        GD.Print("\n################ 끝 ################");
        GetTree().Quit();
    }

    // ── 정상 회전 루프 ────────────────────────────────────────────────
    //
    // 겹치는 층:
    //   ① 저역 러블   40~90Hz 대역 노이즈 — 덕트가 떠는 소리
    //   ② 블레이드    20Hz 진폭 변조 — "부-웅 부-웅"
    //   ③ 중역 공기   400~2000Hz 대역 노이즈, 아주 낮게
    //   ④ 모터        120Hz + 2·3배음의 약한 사인
    //   ⑤ 흔들림      아주 느린 주기 합으로 전체 볼륨을 ±1dB — 기계적 반복감을 없앤다
    //
    // 이음매: 0.15초를 더 만든 뒤 그 꼬리를 머리에 크로스페이드로 접어 넣는다.
    private double[] MakeLoop(double seconds)
    {
        int n = (int)(Rate * seconds);
        int tail = (int)(Rate * 0.15);
        int total = n + tail;

        var rumble = Band(Noise(total), 40, 90);
        var air = Band(Noise(total), 400, 2000);
        var outBuf = new double[total];

        for (int i = 0; i < total; i++)
        {
            double t = i / (double)Rate;
            // ② 블레이드 — 기본 20Hz 에 2배음을 살짝 섞어 날개 끝이 스치는 결을 준다.
            double blade = 1.0
                + 0.62 * Math.Sin(2 * Math.PI * BladeHz * t)
                + 0.18 * Math.Sin(4 * Math.PI * BladeHz * t + 0.7);
            // ④ 모터 하모닉
            double motor = 0.055 * Math.Sin(2 * Math.PI * MotorHz * t)
                         + 0.026 * Math.Sin(2 * Math.PI * MotorHz * 2 * t + 1.1)
                         + 0.013 * Math.Sin(2 * Math.PI * MotorHz * 3 * t + 2.3);
            // ⑤ 아주 느린 흔들림(±1dB ≈ ±0.12). 루프 길이의 정수배 주기만 써서 이음매를 지킨다.
            double wob = 1.0
                + 0.07 * Math.Sin(2 * Math.PI * (1.0 / seconds) * t + 0.3)
                + 0.04 * Math.Sin(2 * Math.PI * (2.0 / seconds) * t + 1.9)
                + 0.03 * Math.Sin(2 * Math.PI * (3.0 / seconds) * t + 4.1);

            outBuf[i] = (rumble[i] * 0.95 * blade + air[i] * 0.10 + motor) * wob;
        }

        return FoldLoop(outBuf, n, tail);
    }

    // ── 관성으로 돌다 멎는 소리 ───────────────────────────────────────
    //
    // 속도 s(t) 는 지수적으로 떨어진다 — 처음엔 천천히, 끝에서 급격히.
    // 블레이드 주기와 모터 하모닉이 s 를 따라 같이 느려지고, 저역 필터도 함께 닫힌다.
    // 마지막 0.4초에 금속이 한 번 끼익 하고 멎는 소리 + 완전한 무음.
    private double[] MakeStop(double seconds)
    {
        int n = (int)(Rate * seconds);
        var rumble = Band(Noise(n), 40, 110);
        var air = Band(Noise(n), 400, 2000);
        var outBuf = new double[n];

        double bladePhase = 0, motorPhase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double u = t / seconds;
            // 처음엔 완만, 끝에서 급락.
            double s = Math.Exp(-3.4 * Math.Pow(u, 2.2));

            bladePhase += BladeHz * s / Rate;
            motorPhase += MotorHz * s / Rate;
            double blade = 1.0 + 0.62 * Math.Sin(2 * Math.PI * bladePhase);
            double motor = (0.055 * Math.Sin(2 * Math.PI * motorPhase)
                          + 0.026 * Math.Sin(4 * Math.PI * motorPhase)) * s;

            // 소리도 같이 잦아든다. 마지막 0.35초는 거의 사라진다.
            double amp = Math.Pow(s, 0.75);
            outBuf[i] = (rumble[i] * 0.95 * blade + air[i] * 0.10 * s + motor) * amp;
        }

        // 금속 마찰 — 멎기 직전 한 번. 2.2kHz 근처의 떨리는 사인 + 마른 노이즈.
        int screechAt = (int)(Rate * (seconds - 0.40));
        int screechLen = (int)(Rate * 0.26);
        var friction = Band(Noise(screechLen), 1800, 5200);
        for (int i = 0; i < screechLen; i++)
        {
            int at = screechAt + i;
            if (at >= n) break;
            double u = i / (double)screechLen;
            double env = Math.Sin(Math.PI * u);                  // 부드럽게 들어왔다 나간다
            double f = 2200 * (1.0 - 0.35 * u);                  // 끝으로 갈수록 낮아진다
            double vib = 1.0 + 0.04 * Math.Sin(2 * Math.PI * 34 * i / Rate);
            outBuf[at] += env * (0.30 * Math.Sin(2 * Math.PI * f * vib * i / Rate) + 0.18 * friction[i]);
        }

        // 마지막 0.10초는 완전한 무음 — 이 침묵이 연출의 핵심이다.
        int silence = (int)(Rate * 0.10);
        for (int i = n - silence; i < n; i++)
        {
            double k = (n - 1 - i) / (double)silence;            // 남은 소리를 매끄럽게 0 으로
            outBuf[i] *= k;
        }
        return outBuf;
    }

    // ── 다시 걸리는 소리 ──────────────────────────────────────────────
    //
    // 모터가 걸리는 "털컥" 한 번 → 속도가 S 자로 올라 정상 회전에 도달.
    // 마지막 0.2초는 vent_loop 의 시작과 같은 스펙트럼·같은 위상으로 맞춘다.
    private double[] MakeRestart(double seconds)
    {
        int n = (int)(Rate * seconds);
        var rumble = Band(Noise(n), 40, 90);
        var air = Band(Noise(n), 400, 2000);
        var outBuf = new double[n];

        // 끝에서 blade · motor 위상이 0(= loop 의 시작 위상)이 되도록 뒤에서부터 적분한다.
        var sOf = new double[n];
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            // 0.18 에서 1.0 까지 S 자로. 마지막 구간은 1.0 에 붙어 있다.
            double s = 0.18 + 0.82 * (u <= 0.82 ? 0.5 - 0.5 * Math.Cos(Math.PI * (u / 0.82)) : 1.0);
            sOf[i] = s;
        }
        var bladeP = new double[n];
        var motorP = new double[n];
        for (int i = n - 2; i >= 0; i--)
        {
            bladeP[i] = bladeP[i + 1] - BladeHz * sOf[i + 1] / Rate;
            motorP[i] = motorP[i + 1] - MotorHz * sOf[i + 1] / Rate;
        }

        for (int i = 0; i < n; i++)
        {
            double s = sOf[i];
            double blade = 1.0 + 0.62 * Math.Sin(2 * Math.PI * bladeP[i])
                               + 0.18 * Math.Sin(4 * Math.PI * bladeP[i] + 0.7);
            double motor = (0.055 * Math.Sin(2 * Math.PI * motorP[i])
                          + 0.026 * Math.Sin(4 * Math.PI * motorP[i] + 1.1)
                          + 0.013 * Math.Sin(6 * Math.PI * motorP[i] + 2.3)) * s;
            double amp = Math.Pow(s, 0.8);
            outBuf[i] = (rumble[i] * 0.95 * blade + air[i] * 0.10 * s + motor) * amp;
        }

        // 모터가 걸리는 털컥 — 맨 앞 한 번.
        int clunk = (int)(Rate * 0.16);
        var thudNoise = Band(Noise(clunk), 60, 900);
        for (int i = 0; i < clunk && i < n; i++)
        {
            double env = Math.Exp(-i / (Rate * 0.030));
            outBuf[i] += env * (0.55 * Math.Sin(2 * Math.PI * 74 * i / Rate) + 0.35 * thudNoise[i]);
        }
        return outBuf;
    }

    // ── 도우미 ────────────────────────────────────────────────────────

    private double[] Noise(int n)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = _rng.NextDouble() * 2.0 - 1.0;
        return a;
    }

    private static double[] LowPass(double[] src, double cutoff)
    {
        double a = Math.Exp(-2.0 * Math.PI * cutoff / Rate);
        var outBuf = new double[src.Length];
        double prev = 0;
        for (int i = 0; i < src.Length; i++)
        {
            prev = src[i] * (1 - a) + prev * a;
            outBuf[i] = prev;
        }
        return outBuf;
    }

    // 1차 필터를 두 번 걸어 대역만 남긴다(저역 통과 - 더 낮은 저역 통과).
    private static double[] Band(double[] src, double lo, double hi)
    {
        var low = LowPass(src, hi);
        var below = LowPass(src, lo);
        var outBuf = new double[src.Length];
        for (int i = 0; i < src.Length; i++) outBuf[i] = low[i] - below[i];
        return outBuf;
    }

    // 꼬리를 머리에 크로스페이드로 접어 넣어 이음매를 없앤다.
    private static double[] FoldLoop(double[] src, int n, int tail)
    {
        var outBuf = new double[n];
        Array.Copy(src, outBuf, n);
        for (int i = 0; i < tail; i++)
        {
            double k = i / (double)tail;              // 0 → 1
            outBuf[i] = outBuf[i] * k + src[n + i] * (1 - k);
        }
        return outBuf;
    }

    private static void Write(string path, double[] samples, double peak)
    {
        double hi = 1e-9;
        foreach (double s in samples) hi = Math.Max(hi, Math.Abs(s));
        double k = peak / hi;

        using var f = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        using var w = new BinaryWriter(f);
        int dataBytes = samples.Length * 2;
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
        foreach (double s in samples)
            w.Write((short)(Math.Clamp(s * k, -1.0, 1.0) * 32767));
    }

    // 길이 · 피크 · RMS · (루프면) 이음매 오차를 수치로 찍는다.
    private static void Report(string name, double[] s, bool loop)
    {
        double hi = 0, sum = 0;
        foreach (double v in s) { hi = Math.Max(hi, Math.Abs(v)); sum += v * v; }
        double norm = 0.72 / Math.Max(1e-9, hi);   // 저장 시 정규화된 값 기준으로 환산
        double rms = Math.Sqrt(sum / s.Length) * norm;
        GD.Print($"   {name,-14} {s.Length / (double)Rate,5:0.00}초  피크 {hi * norm:0.000}  RMS {rms:0.000}");

        if (!loop) return;
        // 이음매: 끝에서 머리로 넘어갈 때의 단차와, 정상 구간의 평균 단차를 견준다.
        double seam = Math.Abs(s[0] - s[^1]);
        double avgStep = 0;
        for (int i = 1; i < s.Length; i++) avgStep += Math.Abs(s[i] - s[i - 1]);
        avgStep /= s.Length - 1;
        // 머리 0.15초와 꼬리 0.15초의 RMS 차이(스펙트럼이 이어지는가).
        int w = (int)(Rate * 0.15);
        double headRms = 0, tailRms = 0;
        for (int i = 0; i < w; i++) { headRms += s[i] * s[i]; tailRms += s[^(i + 1)] * s[^(i + 1)]; }
        headRms = Math.Sqrt(headRms / w) * norm;
        tailRms = Math.Sqrt(tailRms / w) * norm;
        GD.Print($"   {"",14} 이음매 단차 {seam * norm:0.0000} (보통 샘플 간 단차 {avgStep * norm:0.0000}, "
                 + $"비율 {seam / Math.Max(1e-9, avgStep):0.00}배)");
        GD.Print($"   {"",14} 머리/꼬리 RMS {headRms:0.000} / {tailRms:0.000} "
                 + $"(차이 {Math.Abs(headRms - tailRms) / Math.Max(1e-9, headRms) * 100:0.0}%)");
    }

    // 블레이드 통과음이 실제로 들어 있는지 — 자기상관에서 1/20초 지연의 봉우리를 확인한다.
    private static void BladeCheck(string name, double[] s)
    {
        // 포락선(진폭)만 뽑아 본다.
        int win = 64;
        var env = new List<double>();
        for (int i = 0; i + win < s.Length; i += win)
        {
            double a = 0;
            for (int k = 0; k < win; k++) a += Math.Abs(s[i + k]);
            env.Add(a / win);
        }
        double mean = 0;
        foreach (double v in env) mean += v;
        mean /= env.Count;

        // 포락선의 주파수 성분을 직접 본다 — 자기상관은 지연이 칸 단위라 20Hz 를 정확히 짚지 못한다.
        // (그래서 쓰던 자기상관 함수는 지웠다.)
        // 20Hz(날개 통과)가 그 주변 대역보다 몇 배나 솟아 있는가로 판정한다.
        double envRate = Rate / (double)win;
        double Mag(double hz)
        {
            double re = 0, im = 0;
            for (int i = 0; i < env.Count; i++)
            {
                double a = 2 * Math.PI * hz * i / envRate;
                double v = env[i] - mean;
                re += v * Math.Cos(a);
                im += v * Math.Sin(a);
            }
            return Math.Sqrt(re * re + im * im) / env.Count;
        }

        double atBlade = Mag(BladeHz);
        // 주변 대역(날개 주파수와 그 배음을 뺀 5~40Hz)의 평균 — 잡음 바닥이다.
        double floorSum = 0; int floorN = 0;
        for (double hz = 5; hz <= 40; hz += 0.5)
        {
            if (Math.Abs(hz - BladeHz) < 2.5 || Math.Abs(hz - BladeHz * 2) < 2.5) continue;
            floorSum += Mag(hz);
            floorN++;
        }
        double noiseFloor = floorSum / Math.Max(1, floorN);
        double ratio = atBlade / Math.Max(1e-12, noiseFloor);
        GD.Print($"   {name,-14} 포락선 스펙트럼: {BladeHz:0}Hz 성분 {atBlade:0.0000} / "
                 + $"주변 잡음 바닥 {noiseFloor:0.0000} = {ratio:0.0}배");
        GD.Print($"   {"",14} → 날개 통과음이 잡음 위로 솟아 있다 [{(ratio >= 4.0 ? "확인" : "약함")}]");
    }
}
