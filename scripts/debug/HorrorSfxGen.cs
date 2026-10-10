using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 공포 사운드 패스 · 스토리 전환에 쓰는 효과음을 직접 합성해 assets/audio/sfx/ 에 쓴다.
//
//   godot --headless --path . res://scenes/debug/HorrorSfxGen.tscn --quit-after 40000
//
// 프로젝트의 기존 효과음 규격과 같다: 22050Hz · 16bit · 모노(VentSfxGen 과 동일).
// 모노로 만들고 좌우는 재생할 때 패닝으로 준다 — 스테레오 파일로 구우면 pan 을 바꿀 수 없다.
//
// ── 속삭임을 어떻게 만드는가 ─────────────────────────────────────────
// 백색소음에 음절 엔벨로프만 씌우면 "치익 치익" 하는 바람 소리로 들린다. 사람 목소리로
// 들리게 하는 것은 **포먼트**다 — 성도(聲道)의 공명 두 개(F1·F2)를 공진 필터로 흉내낸다.
// 음절마다 F1/F2 를 모음처럼 옮기면 "뭐라고 하는지는 모르겠는데 사람 말이다" 가 된다.
// 유성음 성분(톱니파)은 아주 조금만 섞는다 — 많이 섞으면 알아들을 수 있게 되어 버린다.
public partial class HorrorSfxGen : Node
{
    private const int Rate = 22050;

    private readonly Random _rng = new(20261008);

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 공포 · 스토리 효과음 합성 ################");

        // ── 속삭임 ───────────────────────────────────────────────────
        Save(dir, "whisper_short", Whisper(2, 0.95, false), 0.52);
        Save(dir, "whisper_long", Whisper(5, 1.85, false), 0.50);
        Save(dir, "whisper_call", WhisperCall(), 0.55);
        Save(dir, "whisper_reverse", Reverse(Whisper(4, 1.25, true)), 0.50);

        // ── 아주 가까운 소리 ──────────────────────────────────────────
        Save(dir, "breath_close", CloseInhale(), 0.58);
        Save(dir, "cloth_rustle", ClothRustle(), 0.50);

        // ── 문 ───────────────────────────────────────────────────────
        Save(dir, "door_knock_soft", SoftKnock(), 0.62);
        Save(dir, "door_scratch", DoorScratch(), 0.54);
        Save(dir, "door_handle", DoorHandle(), 0.50);

        // ── 환풍구 · 천장 ─────────────────────────────────────────────
        Save(dir, "duct_crawl", DuctCrawl(), 0.48);
        Save(dir, "duct_scrape", DuctScrape(), 0.50);

        // ── 관리자 ───────────────────────────────────────────────────
        Save(dir, "admin_exhale", AdminExhale(), 0.56);
        Save(dir, "admin_gasp", AdminGasp(), 0.62);

        // ── 휴게실 생활음 ─────────────────────────────────────────────
        Save(dir, "breakroom_murmur", Murmur(4.0), 0.44);
        Save(dir, "cup_set", CupSet(), 0.58);
        Save(dir, "chair_pull", ChairPull(), 0.58);

        GD.Print("\n################ 완료 ################");
        GetTree().Quit();
    }

    // ── 속삭임 ────────────────────────────────────────────────────────

    // 음절 n 개짜리 속삭임. voiced=true 면 유성음을 조금 더 섞는다(거꾸로 돌릴 용도).
    private double[] Whisper(int syllables, double seconds, bool voiced)
    {
        int n = Len(seconds);
        var buf = new double[n];
        // 음절을 시간축에 고르지 않게 흩어 놓는다 — 정박자면 기계가 말하는 것처럼 들린다.
        double at = seconds * 0.06;
        var f1 = new SvFilter();
        var f2 = new SvFilter();
        double phase = 0;

        for (int s = 0; s < syllables; s++)
        {
            double dur = Rand(0.11, 0.21);
            if (at + dur > seconds - 0.05) break;
            // 모음 포먼트 — /a/ /e/ /i/ /o/ /u/ 근처를 무작위로 집는다.
            var (a, b) = Vowel();
            double level = Rand(0.55, 1.0);
            int from = (int)(at * Rate), to = (int)((at + dur) * Rate);
            for (int i = from; i < to && i < n; i++)
            {
                double u = (double)(i - from) / (to - from);
                double env = Math.Sin(u * Math.PI);
                env *= env;                                  // 자음처럼 앞뒤가 빠르게 닫힌다
                double air = _rng.NextDouble() * 2 - 1;
                // 유성음 — 아주 약하게. 많으면 말이 알아들려 버린다(§11 A).
                phase += (voiced ? 128.0 : 104.0) / Rate;
                if (phase > 1) phase -= 1;
                double voice = (phase * 2 - 1) * (voiced ? 0.16 : 0.07);
                double src = air * 0.85 + voice;
                double v = f1.Run(src, a, 7.5) * 1.0 + f2.Run(src, b, 5.0) * 0.55;
                buf[i] += v * env * level;
            }
            at += dur + Rand(0.035, 0.12);
        }

        // 숨이 전체에 깔린다 — 음절 사이가 완전한 무음이면 사람 숨이 아니다.
        var hp = new OnePole();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double bed = Math.Sin(t * Math.PI);
            buf[i] += hp.HighPass(_rng.NextDouble() * 2 - 1, 0.35) * bed * 0.055;
        }
        return Fade(buf, 0.03, 0.12);
    }

    // "관리자님..." 의 가락만 흉내낸 속삭임 — 4음절, 끝이 내려가며 흐려진다.
    // 뜻은 들리지 않는다. 들리는 것은 **부르는 억양**뿐이다.
    private double[] WhisperCall()
    {
        const double seconds = 1.35;
        int n = Len(seconds);
        var buf = new double[n];
        // 관·리·자·님 — 앞 세 음절은 짧고 고르게, 마지막이 길게 끌리며 내려간다.
        double[] starts = { 0.10, 0.27, 0.43, 0.60 };
        double[] durs = { 0.14, 0.13, 0.15, 0.42 };
        double[] scale = { 1.00, 0.96, 0.90, 0.72 };   // 포먼트가 함께 내려간다 = 말끝이 처진다
        var f1 = new SvFilter();
        var f2 = new SvFilter();
        double phase = 0;

        for (int s = 0; s < 4; s++)
        {
            var (a, b) = Vowel();
            int from = (int)(starts[s] * Rate), to = (int)((starts[s] + durs[s]) * Rate);
            for (int i = from; i < to && i < n; i++)
            {
                double u = (double)(i - from) / (to - from);
                // 마지막 음절은 꼬리가 길게 빠진다.
                double env = s == 3 ? Math.Sin(u * Math.PI * 0.5) * (1 - u * 0.75) : Math.Sin(u * Math.PI);
                double air = _rng.NextDouble() * 2 - 1;
                phase += 112.0 * scale[s] / Rate;
                if (phase > 1) phase -= 1;
                double src = air * 0.85 + (phase * 2 - 1) * 0.09;
                double v = f1.Run(src, a * scale[s], 8.5) + f2.Run(src, b * scale[s], 5.5) * 0.5;
                buf[i] += v * env * (s == 3 ? 0.8 : 1.0);
            }
        }
        var hp = new OnePole();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            buf[i] += hp.HighPass(_rng.NextDouble() * 2 - 1, 0.33) * Math.Sin(t * Math.PI) * 0.05;
        }
        return Fade(buf, 0.03, 0.2);
    }

    // 모음 하나의 포먼트 두 개(F1, F2). 실제 한국어 모음의 대략치.
    private (double, double) Vowel() => _rng.Next(5) switch
    {
        0 => (730, 1090),   // ㅏ
        1 => (530, 1840),   // ㅔ
        2 => (270, 2290),   // ㅣ
        3 => (570, 840),    // ㅗ
        _ => (300, 870),    // ㅜ
    };

    // ── 아주 가까운 소리 ──────────────────────────────────────────────

    // 바로 옆에서 숨을 들이마신다. 입이 가까워서 저역이 실린다(근접 효과).
    private double[] CloseInhale()
    {
        int n = Len(1.15);
        var buf = new double[n];
        var lp = new OnePole();
        var bp = new SvFilter();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            // 0.1~0.62초에 빠르게 들이마시고, 그 뒤 아주 약하게 멈춘 숨.
            double env = Bump(t, 0.10, 0.62);
            env = Math.Pow(env, 0.7);
            double hold = Bump(t, 0.70, 1.05) * 0.18;
            double air = _rng.NextDouble() * 2 - 1;
            // 들이마시는 동안 컷오프가 열린다 — 숨이 차오르는 소리.
            double k = 0.035 + 0.05 * env;
            double body = lp.LowPass(air, k);
            double throat = bp.Run(air, 420, 3.2) * 0.35;   // 목 공명
            buf[i] = (body + throat) * (env + hold);
        }
        return Fade(buf, 0.02, 0.12);
    }

    // 뒤에서 옷이 스친다. 짧은 마찰 몇 번.
    private double[] ClothRustle()
    {
        int n = Len(0.78);
        var buf = new double[n];
        var hp = new OnePole();
        var bp = new SvFilter();
        // 스침 3~4번이 겹친다.
        int strokes = _rng.Next(3, 5);
        for (int s = 0; s < strokes; s++)
        {
            double at = Rand(0.02, 0.5);
            double dur = Rand(0.07, 0.17);
            int from = (int)(at * Rate), to = (int)((at + dur) * Rate);
            double level = Rand(0.5, 1.0);
            for (int i = from; i < to && i < n; i++)
            {
                double u = (double)(i - from) / (to - from);
                double env = Math.Sin(u * Math.PI);
                double air = _rng.NextDouble() * 2 - 1;
                double v = hp.HighPass(air, 0.45) * 0.7 + bp.Run(air, 2600, 1.6) * 0.5;
                buf[i] += v * env * level;
            }
        }
        return Fade(buf, 0.01, 0.1);
    }

    // ── 문 ────────────────────────────────────────────────────────────

    // 금속 문을 손등으로 아주 약하게 두 번. 큰 소리로 속이지 않는다(§11 B).
    private double[] SoftKnock()
    {
        int n = Len(1.05);
        var buf = new double[n];
        double[] at = { 0.08, 0.34 };
        for (int k = 0; k < at.Length; k++)
        {
            int from = (int)(at[k] * Rate);
            double level = k == 0 ? 1.0 : 0.78;
            // 문짝의 공명 — 낮은 모드 셋이 같이 울린다.
            double[] modes = { 92, 164, 231 };
            double[] decay = { 11, 17, 26 };
            for (int i = from; i < n; i++)
            {
                double t = (double)(i - from) / Rate;
                double v = 0;
                for (int m = 0; m < modes.Length; m++)
                    v += Math.Sin(Math.Tau * modes[m] * t) * Math.Exp(-decay[m] * t) / (m + 1.4);
                // 때리는 순간의 짧은 타격음.
                v += (_rng.NextDouble() * 2 - 1) * Math.Exp(-180 * t) * 0.5;
                buf[i] += v * level * 0.5;
            }
        }
        return Fade(buf, 0.002, 0.15);
    }

    // 금속 문을 길게 긁는다. 긁힘은 "불규칙한 마찰 펄스의 연속"이다.
    private double[] DoorScratch()
    {
        int n = Len(1.35);
        var buf = new double[n];
        var bp = new SvFilter();
        var bp2 = new SvFilter();
        double nextPulse = 0;
        double pulse = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Bump(t, 0.05, 1.22);
            // 마찰 펄스 — 평균 1.8ms 간격으로 불규칙하게 튄다.
            if (t >= nextPulse)
            {
                nextPulse = t + Rand(0.0008, 0.0030);
                pulse = _rng.NextDouble() * 2 - 1;
            }
            pulse *= 0.86;
            double v = bp.Run(pulse, 1750, 2.2) + bp2.Run(pulse, 3900, 1.5) * 0.6;
            buf[i] = v * env;
        }
        return Fade(buf, 0.03, 0.18);
    }

    // 문 손잡이가 움직인다 — 작은 금속 딸깍 둘에 경첩 끼익 하나.
    private double[] DoorHandle()
    {
        int n = Len(0.95);
        var buf = new double[n];
        // 딸깍.
        foreach (double at in new[] { 0.06, 0.13 })
        {
            int from = (int)(at * Rate);
            for (int i = from; i < n; i++)
            {
                double t = (double)(i - from) / Rate;
                double v = Math.Sin(Math.Tau * 1850 * t) * Math.Exp(-120 * t)
                         + Math.Sin(Math.Tau * 3100 * t) * Math.Exp(-200 * t) * 0.6;
                buf[i] += v * 0.3;
            }
        }
        // 경첩 끼익 — 느리게 올라가는 공진.
        var sv = new SvFilter();
        double nextPulse = 0, pulse = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Bump(t, 0.17, 0.78);
            if (t >= nextPulse) { nextPulse = t + Rand(0.004, 0.011); pulse = _rng.NextDouble() * 2 - 1; }
            pulse *= 0.9;
            double f = 620 + 480 * (t - 0.17) / 0.61;
            buf[i] += sv.Run(pulse, Math.Clamp(f, 200, 4000), 9.0) * env * 0.8;
        }
        return Fade(buf, 0.002, 0.12);
    }

    // ── 환풍구 ────────────────────────────────────────────────────────

    // 덕트 안에서 멀리 기어간다. 금속판이 눌릴 때마다 둔탁하게 "둥" 하고 울린다.
    private double[] DuctCrawl()
    {
        int n = Len(2.3);
        var buf = new double[n];
        var lp = new OnePole();
        // 눌리는 지점 5~7개.
        int steps = _rng.Next(5, 8);
        for (int s = 0; s < steps; s++)
        {
            double at = 0.1 + s * (2.0 / steps) + Rand(-0.06, 0.06);
            int from = (int)(at * Rate);
            if (from >= n) break;
            double level = Rand(0.45, 1.0);
            double f = Rand(58, 96);
            for (int i = from; i < n; i++)
            {
                double t = (double)(i - from) / Rate;
                if (t > 0.5) break;
                double v = Math.Sin(Math.Tau * f * t) * Math.Exp(-9 * t)
                         + Math.Sin(Math.Tau * f * 2.7 * t) * Math.Exp(-19 * t) * 0.4;
                buf[i] += v * level * 0.42;
            }
        }
        // 멀어서 고역이 깎인다 + 덕트 안의 바람.
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            buf[i] = lp.LowPass(buf[i], 0.12);
            buf[i] += lp.LowPass(_rng.NextDouble() * 2 - 1, 0.03) * Math.Sin(t * Math.PI) * 0.1;
        }
        return Fade(buf, 0.05, 0.3);
    }

    // 덕트 벽을 짧게 긁는다. DoorScratch 보다 어둡고 짧다(멀리 있다).
    private double[] DuctScrape()
    {
        int n = Len(1.0);
        var buf = new double[n];
        var bp = new SvFilter();
        var lp = new OnePole();
        double nextPulse = 0, pulse = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Bump(t, 0.04, 0.72);
            if (t >= nextPulse) { nextPulse = t + Rand(0.0012, 0.0042); pulse = _rng.NextDouble() * 2 - 1; }
            pulse *= 0.88;
            buf[i] = lp.LowPass(bp.Run(pulse, 1150, 2.6), 0.3) * env;
        }
        return Fade(buf, 0.02, 0.2);
    }

    // ── 관리자 ────────────────────────────────────────────────────────

    // "후우..." — 피곤한 날숨 하나. 스토리 진입 직전에 한 번 쓴다(§3).
    // 공포 효과가 아니다. 관리자가 실제로 그 자리에 앉아 있다는 감각이다.
    private double[] AdminExhale()
    {
        int n = Len(1.7);
        var buf = new double[n];
        var lp = new OnePole();
        var bp = new SvFilter();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            // 아주 짧게 들이마시고(0.05~0.22) 길게 내뱉는다(0.26~1.45).
            double inhale = Bump(t, 0.05, 0.22) * 0.3;
            double exhale = Bump(t, 0.26, 1.45);
            exhale = Math.Pow(exhale, 0.55);          // 앞이 세고 뒤가 길게 빠진다
            double air = _rng.NextDouble() * 2 - 1;
            double body = lp.LowPass(air, 0.028);     // 날숨은 어둡다
            double mouth = bp.Run(air, 520, 2.4) * 0.3;
            buf[i] = (body + mouth) * (inhale + exhale);
        }
        return Fade(buf, 0.02, 0.2);
    }

    // 숨이 멎었다가 급하게 들이마신다 — 큰 충격 직후(§15).
    private double[] AdminGasp()
    {
        int n = Len(1.5);
        var buf = new double[n];
        var lp = new OnePole();
        var bp = new SvFilter();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            // 0.0~0.30 무음(숨이 멎음) → 0.30~0.55 급한 들숨 → 0.62~1.3 가쁜 날숨 둘.
            double gasp = Bump(t, 0.30, 0.55);
            gasp = Math.Pow(gasp, 0.45);
            double out1 = Bump(t, 0.64, 0.92) * 0.55;
            double out2 = Bump(t, 0.98, 1.30) * 0.42;
            double air = _rng.NextDouble() * 2 - 1;
            double v = lp.LowPass(air, 0.045 + 0.05 * gasp) + bp.Run(air, 640, 2.8) * 0.4;
            buf[i] = v * (gasp + out1 + out2);
        }
        return Fade(buf, 0.01, 0.15);
    }

    // ── 휴게실 생활음 ─────────────────────────────────────────────────

    // 사람 몇이 멀리서 웅성거린다. 루프로 쓴다 — 암전 중에 먼저 깔린다(§4).
    // 말이 들리면 안 된다. 들리는 것은 "사람이 있다" 뿐이다.
    private double[] Murmur(double seconds)
    {
        int n = Len(seconds);
        var buf = new double[n];
        // 네 사람이 서로 다른 박자로 웅얼거린다.
        for (int v = 0; v < 4; v++)
        {
            var f1 = new SvFilter();
            var f2 = new SvFilter();
            double at = Rand(0, seconds);
            double pitch = Rand(0.82, 1.18);
            double level = Rand(0.5, 1.0);
            double phase = 0;
            while (true)
            {
                double dur = Rand(0.10, 0.22);
                var (a, b) = Vowel();
                int from = (int)(at % seconds * Rate);
                for (int k = 0; k < (int)(dur * Rate); k++)
                {
                    int i = (from + k) % n;              // 감싸 돌려 루프 이음매를 없앤다
                    double u = (double)k / (dur * Rate);
                    double env = Math.Sin(u * Math.PI);
                    double air = _rng.NextDouble() * 2 - 1;
                    phase += 118.0 * pitch / Rate;
                    if (phase > 1) phase -= 1;
                    double src = air * 0.6 + (phase * 2 - 1) * 0.22;
                    buf[i] += (f1.Run(src, a * pitch, 6.0) + f2.Run(src, b * pitch, 4.0) * 0.5)
                              * env * level * 0.4;
                }
                at += dur + Rand(0.12, 0.45);
                if (at > seconds * 2) break;
            }
        }
        // 멀리 있다 — 고역을 깎고 방의 잔향 느낌으로 살짝 번지게 한다.
        var lp = new OnePole();
        for (int i = 0; i < n; i++) buf[i] = lp.LowPass(buf[i], 0.085);
        int d1 = (int)(0.037 * Rate), d2 = (int)(0.071 * Rate);
        var wet = new double[n];
        for (int i = 0; i < n; i++)
            wet[i] = buf[i] + buf[(i - d1 + n) % n] * 0.3 + buf[(i - d2 + n) % n] * 0.18;
        return wet;   // 루프라 페이드를 넣지 않는다
    }

    // 컵을 테이블에 내려놓는다 — 바닥 닿는 "톡" 과 안에 든 액체의 짧은 출렁임.
    private double[] CupSet()
    {
        int n = Len(0.55);
        var buf = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double v = Math.Sin(Math.Tau * 410 * t) * Math.Exp(-38 * t)
                     + Math.Sin(Math.Tau * 1230 * t) * Math.Exp(-62 * t) * 0.45
                     + Math.Sin(Math.Tau * 2460 * t) * Math.Exp(-110 * t) * 0.2;
            v += (_rng.NextDouble() * 2 - 1) * Math.Exp(-300 * t) * 0.35;
            buf[i] = v;
        }
        return Fade(buf, 0.001, 0.1);
    }

    // 의자를 끌어당긴다 — 바닥 마찰이 길게 이어지고 끝에 다리가 닿는다.
    private double[] ChairPull()
    {
        int n = Len(1.05);
        var buf = new double[n];
        var bp = new SvFilter();
        var lp = new OnePole();
        double nextPulse = 0, pulse = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Bump(t, 0.03, 0.62);
            if (t >= nextPulse) { nextPulse = t + Rand(0.0015, 0.0060); pulse = _rng.NextDouble() * 2 - 1; }
            pulse *= 0.9;
            buf[i] = (bp.Run(pulse, 280, 3.4) + lp.LowPass(pulse, 0.2) * 0.6) * env;
        }
        // 끝에 의자 다리가 바닥에 닿는 소리.
        int from = (int)(0.60 * Rate);
        for (int i = from; i < n; i++)
        {
            double t = (double)(i - from) / Rate;
            buf[i] += (Math.Sin(Math.Tau * 128 * t) * Math.Exp(-26 * t)
                       + (_rng.NextDouble() * 2 - 1) * Math.Exp(-160 * t) * 0.5) * 0.55;
        }
        return Fade(buf, 0.005, 0.12);
    }

    // ── 합성 도구 ─────────────────────────────────────────────────────

    // 2극 상태변수 필터. 포먼트(공명) 하나를 흉내낸다. q 가 크면 더 "울린다".
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

    // 1극 저역/고역 통과. k 가 크면 고역이 더 남는다.
    private sealed class OnePole
    {
        private double _z;

        public double LowPass(double x, double k)
        {
            _z += k * (x - _z);
            return _z;
        }

        public double HighPass(double x, double k)
        {
            _z += k * (x - _z);
            return x - _z;
        }
    }

    private static double Bump(double t, double a, double b)
    {
        if (t <= a || t >= b) return 0;
        return Math.Sin((t - a) / (b - a) * Math.PI);
    }

    private double Rand(double a, double b) => a + _rng.NextDouble() * (b - a);

    private static int Len(double seconds) => (int)(Rate * seconds);

    private static double[] Fade(double[] s, double inSec, double outSec)
    {
        int fi = Math.Max(1, (int)(inSec * Rate));
        int fo = Math.Max(1, (int)(outSec * Rate));
        for (int i = 0; i < fi && i < s.Length; i++) s[i] *= (double)i / fi;
        for (int i = 0; i < fo && i < s.Length; i++) s[s.Length - 1 - i] *= (double)i / fo;
        return s;
    }

    private static double[] Reverse(double[] s)
    {
        Array.Reverse(s);
        return s;
    }

    // 피크를 맞춰 16bit 모노 WAV 로 쓴다(VentSfxGen.Write 와 같은 규격).
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
