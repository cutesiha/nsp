using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 관리자 심장박동 admin_heartbeat.wav 를 합성해 assets/audio/sfx/ 에 쓴다.
//
//   godot --headless --path . res://scenes/debug/HeartbeatSfxGen.tscn --quit-after 300
//   godot --headless --path . --import
//
// 규격은 프로젝트의 다른 합성음과 같다: 22050Hz · 16bit · 모노.
//
// ── 왜 한 박자만 굽는가 ──────────────────────────────────────────────
// BPM 을 PitchScale 로 바꾸면 소리까지 같이 높아져 우스워진다(지시서 금지 사항).
// 그래서 **"둥-둥" 한 쌍만** 담고, 쉬는 구간은 파일에 넣지 않는다.
// 재생 간격 60/BPM 은 AdminFearDirector 가 타이머로 잡는다 — 그러면 BPM 이 바뀌어도
// 소리 자체는 한 번도 변형되지 않는다.
//
// ── 어떻게 만드는가 ──────────────────────────────────────────────────
// 심장 소리는 두 번의 밸브 닫힘이다.
//   S1 "둥"  낮고 길다(약 52Hz)   — 더 세게
//   S2 "둥"  조금 높고 짧다(약 64Hz) — 0.16초 뒤, 약하게
// 각각 사인파에 빠른 감쇠를 걸고, 작은 스피커에서도 들리도록 중역대 타격 성분
// (노이즈 한 조각을 밴드패스로 깎은 것)을 아주 조금 얹는다.
// 서브베이스가 과하면 클리핑이 나므로 피크를 0.5 아래로 잡는다.
public partial class HeartbeatSfxGen : Node
{
    private const int Rate = 22050;
    private const double Total = 0.46;

    private readonly Random _rng = new(20261010);

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 관리자 심장박동 합성 ################");
        Save(dir, "admin_heartbeat", Build(), 0.48);
        GD.Print("\n################ 완료 ################");
        GD.Print("   엔진 등록:  godot --headless --path . --import");
        GetTree().Quit();
    }

    private double[] Build()
    {
        int n = (int)(Total * Rate);
        var buf = new double[n];

        // S1 — 낮고 묵직하다.
        Thump(buf, at: 0.000, hz: 52.0, drop: 14.0, decay: 17.0, level: 1.00, click: 0.16);
        // S2 — 0.16초 뒤, 조금 높고 짧다.
        Thump(buf, at: 0.160, hz: 64.0, drop: 18.0, decay: 24.0, level: 0.66, click: 0.12);

        // 끝을 0 으로 닫는다 — 잘린 끝은 '툭' 하고 들린다.
        int fade = (int)(0.04 * Rate);
        for (int i = 0; i < fade; i++)
        {
            double k = 1.0 - (double)i / fade;
            buf[n - 1 - i] *= k * k;
        }
        return buf;
    }

    // 한 번의 밸브 닫힘. 사인파의 주파수가 시작 직후 살짝 떨어진다(실제 심음의 특징).
    // click 은 중역대 타격 성분 — 작은 스피커에서 저음이 안 날 때 이것만 남는다.
    private void Thump(double[] buf, double at, double hz, double drop, double decay,
        double level, double click)
    {
        int from = (int)(at * Rate);
        var lp = new OnePole(0.35);
        var hp = new OnePole(0.06);
        double phase = 0;
        for (int i = from; i < buf.Length; i++)
        {
            double t = (double)(i - from) / Rate;
            double env = Math.Exp(-t * decay);
            if (env < 0.0008) break;
            // 어택을 아주 짧게 올려 준다 — 그러지 않으면 시작에서 딱 소리가 난다.
            env *= Math.Min(1.0, t / 0.0025);

            double f = hz - drop * (1.0 - Math.Exp(-t * 26.0));
            phase += f / Rate;
            double body = Math.Sin(2 * Math.PI * phase);
            // 2배음을 아주 조금 — 완전한 사인은 심장보다 신호음처럼 들린다.
            body += 0.18 * Math.Sin(4 * Math.PI * phase);

            // 타격 성분: 노이즈를 저역통과 → 고역통과로 깎아 200~700Hz 근처만 남긴다.
            double noise = _rng.NextDouble() * 2 - 1;
            double knock = hp.High(lp.Low(noise)) * Math.Exp(-t * 90.0);

            buf[i] += (body * 0.9 + knock * click * 3.2) * env * level;
        }
    }

    // 1차 필터 — VentSfxGen · HorrorSfxGen 과 같은 규약.
    private sealed class OnePole
    {
        private readonly double _a;
        private double _z;
        public OnePole(double a) => _a = a;
        public double Low(double x) { _z += _a * (x - _z); return _z; }
        public double High(double x) { _z += _a * (x - _z); return x - _z; }
    }

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
