using System;
using System.Collections.Generic;
using Godot;

namespace NSP.Debug;

// 참고용 사이렌 음원이 **실제로 어떤 소리인지 측정**한다.
//
//   godot --path . res://scenes/debug/SirenProbe.tscn -- <재는 초>
//
// 귀로 듣고 흉내 내는 대신 숫자를 뽑는다 : 기본 주파수가 몇 Hz 에서 몇 Hz 까지,
// 몇 초 주기로 오르내리는지 · 소리 크기는 어떻게 출렁이는지.
// 그 값을 그대로 SirenSfxGen 에 넣으면 "비슷하게" 가 추측이 아니라 계산이 된다.
//
// 헤드리스로 돌리면 더미 오디오 드라이버라 아무것도 안 잡힌다 — 창 모드로 돌려야 한다.
public partial class SirenProbe : Node
{
    private const string Bus = "SirenProbe";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        double seconds = args.Length > 0 ? args[0].ToFloat() : 8.0;
        string path = args.Length > 1 ? args[1] : "res://assets/audio/sfx/siren.mp3";

        if (!ResourceLoader.Exists(path)) { GD.Print("없는 파일: " + path); GetTree().Quit(); return; }
        var stream = GD.Load<AudioStream>(path);
        GD.Print($"원본: {path}  {stream.GetLength():0.0}s  {stream.GetClass()}");

        // 전용 버스를 만들어 거기서만 캡처한다. 마스터는 죽여 둔다(스피커로 안 나가게).
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, Bus);
        AudioServer.SetBusSend(idx, "Master");
        var cap = new AudioEffectCapture { BufferLength = 2f };
        AudioServer.AddBusEffect(idx, cap);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), -60f);

        var p = new AudioStreamPlayer { Stream = stream, Bus = Bus, VolumeDb = 0f };
        AddChild(p);
        p.Play();

        int rate = (int)AudioServer.GetMixRate();
        var pcm = new List<float>();
        double t0 = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - t0 < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            int n = cap.GetFramesAvailable();
            if (n <= 0) continue;
            foreach (var v in cap.GetBuffer(n)) pcm.Add((v.X + v.Y) * 0.5f);
        }
        p.Stop();
        GD.Print($"캡처: {pcm.Count} 샘플 @ {rate}Hz  = {pcm.Count / (double)rate:0.00}s");
        if (pcm.Count < rate) { GD.Print("!! 소리가 안 잡혔다(오디오 드라이버 확인)"); GetTree().Quit(); return; }

        Analyse(pcm, rate);
        GetTree().Quit();
    }

    private static void Analyse(List<float> pcm, int rate)
    {
        // 46ms 창마다 세기와 기본 주파수를 잰다. 자기상관이 사이렌처럼 또렷한
        // 음높이를 가진 소리에는 가장 안정적이다.
        int win = 2048, hop = 1024;
        var env = new List<double>();
        var f0 = new List<double>();
        for (int s = 0; s + win < pcm.Count; s += hop)
        {
            double rms = 0;
            for (int i = 0; i < win; i++) rms += pcm[s + i] * pcm[s + i];
            rms = Math.Sqrt(rms / win);
            env.Add(rms);
            f0.Add(rms < 1e-4 ? 0 : Pitch(pcm, s, win, rate));
        }

        double peak = 0;
        foreach (var v in env) peak = Math.Max(peak, v);
        GD.Print($"최대 RMS {peak:0.000}");

        // 음높이가 잡힌 구간만 모아 범위를 본다.
        var voiced = new List<double>();
        for (int i = 0; i < f0.Count; i++)
            if (f0[i] > 60 && f0[i] < 4000 && env[i] > peak * 0.25) voiced.Add(f0[i]);
        voiced.Sort();
        if (voiced.Count > 8)
        {
            GD.Print($"기본 주파수  최저 {voiced[(int)(voiced.Count * 0.05)]:0} Hz" +
                     $"  중앙 {voiced[voiced.Count / 2]:0} Hz" +
                     $"  최고 {voiced[(int)(voiced.Count * 0.95)]:0} Hz");
        }

        // 음높이가 오르내리는 주기 — f0 수열의 자기상관.
        GD.Print($"음높이 스윕 주기  {Period(f0, hop / (double)rate):0.00} s");
        GD.Print($"소리 크기 출렁임 주기 {Period(env, hop / (double)rate):0.00} s");

        // 전 구간의 음높이 · 세기를 0.5초 간격으로 훑는다. 사이렌이 한 번
        // 올라갔다 내려오는지, 계속 올라가기만 하는지는 이걸로만 알 수 있다.
        double step = 0.5, dt = hop / (double)rate;
        GD.Print("  t      f0     RMS");
        for (double t = 0; t < f0.Count * dt; t += step)
        {
            int i = (int)(t / dt);
            if (i >= f0.Count) break;
            int bar = (int)Math.Round(env[i] / Math.Max(1e-6, peak) * 24);
            GD.Print($"{t,5:0.0}  {f0[i],6:0}  {new string('#', Math.Max(0, bar))}");
        }
    }

    // 자기상관으로 기본 주파수 하나.
    private static double Pitch(List<float> x, int off, int n, int rate)
    {
        int lo = rate / 2000, hi = Math.Min(n / 2, rate / 80);
        double best = 0; int bestLag = 0;
        for (int lag = lo; lag < hi; lag++)
        {
            double sum = 0;
            for (int i = 0; i < n - lag; i++) sum += x[off + i] * x[off + i + lag];
            if (sum > best) { best = sum; bestLag = lag; }
        }
        return bestLag > 0 ? rate / (double)bestLag : 0;
    }

    // 수열이 되풀이되는 주기(초). 평균을 뺀 뒤 자기상관의 첫 봉우리를 찾는다.
    private static double Period(List<double> v, double dt)
    {
        int n = v.Count;
        if (n < 16) return 0;
        double mean = 0;
        foreach (var a in v) mean += a;
        mean /= n;
        double best = 0; int bestLag = 0;
        for (int lag = 2; lag < n / 2; lag++)
        {
            double sum = 0;
            for (int i = 0; i < n - lag; i++) sum += (v[i] - mean) * (v[i + lag] - mean);
            if (sum > best) { best = sum; bestLag = lag; }
        }
        return bestLag * dt;
    }
}
