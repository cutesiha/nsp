using Godot;

namespace NSP.Debug;

// 음원 크기 재기(헤드리스) — 재생하지 않고 디코딩만 해서 평균 크기(RMS dB)와 최대값을 낸다.
// 곡마다 음량 편차를 맞출 때(Sfx.MusicTrimDb) 기준이 된다.
//   godot --headless --path . res://scenes/debug/LoudnessProbe.tscn
public partial class LoudnessProbe : Node
{
    private static readonly string[] Files =
    {
        "res://assets/audio/bgm/rest_time.ogg", "res://assets/audio/bgm/breakroom.mp3",
        "res://assets/audio/bgm/nsp_mainbgm2.ogg", "res://assets/audio/bgm/startbgm1.ogg",
        "res://assets/audio/bgm/machine_bgm.mp3", "res://assets/audio/bgm/industrial_fan_bgm.mp3",
        "res://assets/audio/scream/boy_scream.mp3", "res://assets/audio/scream/girl_scream.mp3",
    };

    public override void _Ready()
    {
        foreach (string path in Files)
        {
            var stream = GD.Load<AudioStream>(path);
            if (stream == null) { GD.Print($"[loud] {path} 없음"); continue; }
            var pb = stream.InstantiatePlayback();
            pb.Start(0.0);
            double sum = 0; long n = 0; float peak = 0f;
            int maxFrames = (int)(stream.GetLength() * 44100.0) + 44100;
            // 짧은 구간(0.4초)마다 RMS 를 재서, 조용한 구간을 뺀 평균도 낸다(곡의 "들리는" 크기).
            var blocks = new System.Collections.Generic.List<double>();
            double bsum = 0; int bn = 0;
            while (n < maxFrames && pb.IsPlaying())
            {
                var buf = pb.MixAudio(1.0f, 4096);
                if (buf.Length == 0) break;
                foreach (var f in buf)
                {
                    double m = (f.X * f.X + f.Y * f.Y) * 0.5;
                    sum += m; bsum += m; bn++; n++;
                    peak = Mathf.Max(peak, Mathf.Max(Mathf.Abs(f.X), Mathf.Abs(f.Y)));
                    if (bn >= 17640) { blocks.Add(bsum / bn); bsum = 0; bn = 0; }
                }
            }
            double rms = n > 0 ? 10 * System.Math.Log10(sum / n + 1e-12) : -120;
            blocks.Sort();
            // 위쪽 70% 구간만(조용한 도입 · 끝을 뺀 "들리는" 크기).
            double gsum = 0; int gn = 0;
            for (int i = (int)(blocks.Count * 0.3); i < blocks.Count; i++) { gsum += blocks[i]; gn++; }
            double gated = gn > 0 ? 10 * System.Math.Log10(gsum / gn + 1e-12) : rms;
            GD.Print($"[loud] {System.IO.Path.GetFileName(path),-24} 길이 {stream.GetLength(),6:0.0}s  RMS {rms,6:0.0} dB  " +
                     $"들리는 크기 {gated,6:0.0} dB  최대 {20 * System.Math.Log10(peak + 1e-9),6:0.0} dBFS");
        }
        GetTree().Quit();
    }
}
