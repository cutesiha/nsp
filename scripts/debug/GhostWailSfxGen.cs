using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 이상 개체가 작업실에 나와 있는 동안 계속 깔리는 「귀신 울음」 루프를 합성해 assets/audio/sfx/ 에 쓴다.
//
//   godot --headless --path . res://scenes/debug/GhostWailSfxGen.tscn --quit-after 40000
//   (새 파일이면 그 뒤에 godot --headless --path . --import 로 들여온다)
//
// 규격은 HorrorSfxGen 과 같다: 22050Hz · 16bit · 모노.
//
// ── 어떤 소리인가 ─────────────────────────────────────────────────────
// 예전 기척은 희미한 숨소리 하나라 "무슨 소리지?" 에서 멈췄다(2026-10-29 사용자 — "아직 헷갈림").
// 여기서는 **사람 목소리 같지만 사람이 아닌** 울음을 만든다.
//   · 목소리 셋이 "우우우—아아아—" 하고 길게 끌며 서로 어긋나게 오르내린다(포먼트 /u/ ↔ /a/).
//   · 음정은 반음 사이를 미끄러지고, 떨림(비브라토)이 일정하지 않다 — 노래가 아니라 흐느낌.
//   · 셋째 목소리에는 금속성 링 변조를 걸어 "목에서 나는 소리가 아니다" 를 남긴다.
//   · 바닥에 아주 낮은 웅웅거림과 숨 섞인 바람이 깔린다.
// 끝과 처음을 겹쳐(크로스페이드) 이어 붙여, 반복해도 이음매가 들리지 않게 한다.
public partial class GhostWailSfxGen : Node
{
    private const int Rate = 22050;
    private const double LoopSeconds = 9.0;
    private const double CrossSeconds = 1.5;

    private readonly Random _rng = new(20261029);

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 귀신 울음 루프 합성 ################");
        Save(dir, "ghost_wail_loop", Loopable(Wail(LoopSeconds + CrossSeconds)), 0.56);
        GD.Print("\n################ 완료 ################");
        GetTree().Quit();
    }

    private double[] Wail(double seconds)
    {
        int n = (int)(Rate * seconds);
        var buf = new double[n];

        // 목소리 셋 — (기준 음, 윤곽 위상, 금속성 정도, 크기).
        var voices = new (double F0, double Phase, double Metal, double Level)[]
        {
            (392.0, 0.0, 0.0, 1.00),
            (293.7, 2.1, 0.0, 0.80),
            (466.2, 4.4, 0.55, 0.55),
        };
        foreach (var v in voices) AddVoice(buf, v.F0, v.Phase, v.Metal, v.Level);

        // 숨 섞인 바람 — 목소리가 쉬는 사이도 완전히 비지 않게.
        var air = new SvFilter();
        var lo = new OnePole();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double swell = 0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 4.5 + 1.3);
            double noise = _rng.NextDouble() * 2 - 1;
            buf[i] += air.Run(noise, 1700 + 500 * Math.Sin(2 * Math.PI * t / 3.0), 1.4) * 0.10 * (0.4 + swell);
            // 아주 낮은 웅웅거림 — 두 음이 맞물려 천천히 일렁인다.
            buf[i] += (Math.Sin(2 * Math.PI * 49.0 * t) + Math.Sin(2 * Math.PI * 49.6 * t)) * 0.035;
            // 거친 고역은 덜어 낸다 — 벽 너머로 새어 나오는 질감.
            buf[i] = lo.LowPass(buf[i], 0.42);
        }
        return buf;
    }

    // 목소리 하나. 음정과 모음이 천천히 미끄러지고, 숨을 쉬듯 끊겼다 이어진다.
    private void AddVoice(double[] buf, double f0, double ph, double metal, double level)
    {
        const int Harmonics = 12;
        double phase = 0;
        double vibDrift = _rng.NextDouble() * 6.28;
        for (int i = 0; i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            // 음정 윤곽(반음) — 서로 다른 주기의 느린 사인을 겹쳐 "끌다가 치솟다 꺾인다".
            double contour = 4.0 * Math.Sin(2 * Math.PI * t / 4.5 + ph)
                           + 2.6 * Math.Sin(2 * Math.PI * t / 2.25 + ph * 1.7)
                           + 1.8 * Math.Sin(2 * Math.PI * t / 7.2 + ph * 0.6);
            // 떨림 — 빠르기와 깊이가 흔들린다(노래가 아니라 흐느낌).
            double vibRate = 5.2 + 1.4 * Math.Sin(2 * Math.PI * t / 3.3 + vibDrift);
            double vibDepth = 0.25 + 0.30 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 2.9 + ph));
            contour += vibDepth * Math.Sin(2 * Math.PI * vibRate * t);
            double f = f0 * Math.Pow(2, contour / 12.0);
            phase += 2 * Math.PI * f / Rate;
            if (phase > 2 * Math.PI * 1000) phase -= 2 * Math.PI * 1000;

            // 모음 — /u/(350, 800) ↔ /a/(760, 1180) 사이를 오간다.
            double vowel = 0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 3.8 + ph * 1.3);
            double fa = 350 + 410 * vowel, fb = 800 + 380 * vowel;

            double s = 0;
            for (int k = 1; k <= Harmonics; k++)
            {
                double hf = f * k;
                if (hf > Rate * 0.45) break;
                double formant = Math.Exp(-Math.Pow((hf - fa) / 170.0, 2))
                               + 0.7 * Math.Exp(-Math.Pow((hf - fb) / 240.0, 2)) + 0.08;
                s += Math.Sin(phase * k) * formant / Math.Pow(k, 1.1);
            }

            // 숨을 쉬듯 — 소리가 부풀었다가 거의 사라진다.
            double breath = Math.Max(0, Math.Sin(2 * Math.PI * t / 3.0 + ph * 2.3));
            double env = 0.15 + 0.85 * breath * breath;
            if (metal > 0) s *= 1 - metal + metal * Math.Sin(2 * Math.PI * 43.0 * t);
            buf[i] += s * env * level * 0.32;
        }
    }

    // 꼬리 CrossSeconds 를 머리에 겹쳐 LoopSeconds 길이의 이음매 없는 루프로 만든다.
    private static double[] Loopable(double[] raw)
    {
        int loop = (int)(Rate * LoopSeconds), cross = (int)(Rate * CrossSeconds);
        var outBuf = new double[loop];
        for (int i = 0; i < loop; i++)
        {
            if (i < cross && loop + i < raw.Length)
            {
                double k = (double)i / cross;
                outBuf[i] = raw[i] * k + raw[loop + i] * (1 - k);
            }
            else outBuf[i] = raw[i];
        }
        return outBuf;
    }

    // HorrorSfxGen 과 같은 상태 변수 필터(밴드 출력).
    private sealed class SvFilter
    {
        private double _low, _band;

        public double Run(double input, double freqHz, double q)
        {
            double f = 2 * Math.Sin(Math.PI * Math.Clamp(freqHz, 20, Rate * 0.45) / Rate);
            double damp = 1.0 / Math.Max(0.5, q);
            double high = input - _low - damp * _band;
            _band += f * high;
            _low += f * _band;
            return _band;
        }
    }

    private sealed class OnePole
    {
        private double _z;

        public double LowPass(double x, double k)
        {
            _z += k * (x - _z);
            return _z;
        }
    }

    // 피크를 맞춰 16bit 모노 WAV 로 쓴다(HorrorSfxGen.Save 와 같은 규격).
    private static void Save(string dir, string name, double[] samples, double peak)
    {
        double hi = 1e-9;
        foreach (double s in samples) hi = Math.Max(hi, Math.Abs(s));
        double k = peak / hi;
        double sum = 0;
        foreach (double s in samples) sum += s * s * k * k;
        double rms = Math.Sqrt(sum / Math.Max(1, samples.Length));

        string path = Path.Combine(dir, name + ".wav");
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

        GD.Print($"   {name,-20} {samples.Length / (double)Rate,5:0.00}초  피크 {peak:0.00}  RMS {rms:0.000}");
    }
}
