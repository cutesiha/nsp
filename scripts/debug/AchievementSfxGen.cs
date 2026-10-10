using System;
using System.IO;
using Godot;

namespace NSP.Debug;

// 도전과제 달성음 achievement.wav 를 합성해 assets/audio/sfx/ 에 쓴다.
//
//   godot --headless --path . res://scenes/debug/AchievementSfxGen.tscn --quit-after 20000
//   godot --headless --path . --import            (새 wav 를 엔진에 등록)
//
// 규격은 프로젝트의 다른 합성음과 같다: 22050Hz · 16bit · 모노(VentSfxGen · HorrorSfxGen).
//
// ── 왜 ding.wav 를 쓰지 않는가 ────────────────────────────────────────
// ding.wav 는 이미 **수리 완료**를 알리는 소리다(FacilitySimulation). 업적에 같은 소리를
// 쓰면 근무 중에 "뭘 고쳤나?" 하고 화면을 찾게 된다 — 게임 신호와 메타 알림이 섞이면
// 둘 다 못 믿게 된다. 그리고 한 음짜리라서 '띠롱' 두 음이 되지 못한다.
//
// ── 어떤 소리인가 ────────────────────────────────────────────────────
// 밝고 귀여운 게임 효과음이 아니라 **낡은 전자 단말기의 알림음**이다.
//   · 두 음(A5 → D6, 4도 위) — 첫 음은 짧고 또렷하게, 둘째 음이 조금 높게 받는다
//   · 사인파에 홀수 배음을 아주 조금 섞는다(소형 피에조 스피커의 쇳소리)
//   · 꼬리는 짧은 콤 딜레이 두 번 — 방이 아니라 기계 안에서 울리는 정도
//   · 전체 0.66초, 피크를 낮게 잡는다(알림이 대사를 덮으면 안 된다)
public partial class AchievementSfxGen : Node
{
    private const int Rate = 22050;
    private const double Total = 0.66;

    // A5 → D6. 반음이 아니라 4도를 띄워야 '띠-롱' 으로 들린다.
    private const double Note1 = 880.0;
    private const double Note2 = 1174.66;

    public override void _Ready()
    {
        string dir = ProjectSettings.GlobalizePath("res://assets/audio/sfx");
        GD.Print("\n\n################ 도전과제 달성음 합성 ################");
        Save(dir, "achievement", Build(), 0.46);
        GD.Print("\n################ 완료 ################");
        GD.Print("   엔진 등록:  godot --headless --path . --import");
        GetTree().Quit();
    }

    private static double[] Build()
    {
        int n = (int)(Total * Rate);
        var dry = new double[n];

        // 첫 음 — 짧고 또렷하다. 어택이 거의 없다(단말기의 딸깍 같은 시작).
        Tone(dry, at: 0.000, dur: 0.115, hz: Note1, level: 0.85, decay: 26.0);
        // 둘째 음 — 조금 높게, 조금 길게 받는다.
        Tone(dry, at: 0.125, dur: 0.300, hz: Note2, level: 1.00, decay: 11.0);

        // 꼬리 — 짧은 콤 딜레이 두 번. 공간이 아니라 기계 안쪽의 울림이다.
        var wet = (double[])dry.Clone();
        Echo(wet, dry, 0.047, 0.30);
        Echo(wet, dry, 0.091, 0.14);

        // 끝을 확실히 0 으로 내린다 — 잘린 끝은 '툭' 하고 들린다.
        int fade = (int)(0.05 * Rate);
        for (int i = 0; i < fade; i++)
        {
            double k = 1.0 - (double)i / fade;
            wet[n - 1 - i] *= k * k;
        }
        return wet;
    }

    // 사인파 + 홀수 배음 약간. decay 가 클수록 짧게 끊긴다.
    private static void Tone(double[] buf, double at, double dur, double hz, double level, double decay)
    {
        int from = (int)(at * Rate);
        int to = Math.Min(buf.Length, (int)((at + dur) * Rate));
        for (int i = from; i < to; i++)
        {
            double t = (double)(i - from) / Rate;
            double env = Math.Exp(-t * decay);
            // 맨 앞 1ms 만 올려 준다 — 그러지 않으면 시작에서 딱 소리가 난다.
            env *= Math.Min(1.0, t / 0.001);
            double s = Math.Sin(2 * Math.PI * hz * t)
                       + 0.14 * Math.Sin(2 * Math.PI * hz * 3 * t)
                       + 0.05 * Math.Sin(2 * Math.PI * hz * 5 * t);
            buf[i] += s * env * level;
        }
    }

    private static void Echo(double[] into, double[] src, double delaySeconds, double gain)
    {
        int d = (int)(delaySeconds * Rate);
        for (int i = d; i < into.Length; i++) into[i] += src[i - d] * gain;
    }

    // 피크를 맞춰 16bit 모노 WAV 로 쓴다(VentSfxGen.Write · HorrorSfxGen.Save 와 같은 규격).
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
