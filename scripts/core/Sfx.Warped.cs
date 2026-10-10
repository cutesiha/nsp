using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// ── 기괴하게 변형한 재생 ──────────────────────────────────────────────────
//
// 프롤로그 재난 구간에서 쓰는 두 번째 재생 경로다. 같은 파일을 Warp 버스
// (저역통과 + 느린 페이저 + 깊은 잔향)로 흘리고, **재생 속도를 1 보다 낮춰**
// 길고 낮고 일그러진 소리로 바꾼다.
//
//   · 음원 파일은 건드리지 않는다 — 원본은 다른 자리에서 그대로 쓴다.
//   · 채널 이름이 있어 **같은 파일을 여러 겹** 쌓을 수 있다(유리가 깨지는
//     순간 한 겹, 끌리는 저음 한 겹). 한 채널에는 한 소리만 돈다.
//   · 프롤로그가 끝날 때 StopAllWarped() 한 번으로 전부 멈춘다 — 재난음이
//     게임 안까지 따라 들어오는 사고를 구조적으로 막는다.
//
// 기존 Play / Loop / 음악 / 공포음 경로는 전혀 지나가지 않는다.
public partial class Sfx
{
    private readonly Dictionary<string, AudioStreamPlayer> _warp = new();
    private readonly Dictionary<string, Tween> _warpFade = new();

    // 지연 재생 예약을 무효로 만드는 세대 번호. StopAllWarped 가 올리면 아직
    // 터지지 않은 예약은 조용히 버려진다(컷을 빨리 넘겼을 때 뒤늦게 울리는 사고 방지).
    private int _warpGen;

    // 채널별 일련번호. 한 채널만 끄거나 그 채널에 새 소리를 걸어도 **그 채널의**
    // 예약은 무효가 되어야 한다 — 아직 울리지 않은 예약은 StopWarped 로는
    // 멈출 수 없기 때문이다(멈출 플레이어가 아직 없다).
    private readonly Dictionary<string, int> _warpSeq = new();

    private int SeqOf(string channel) => _warpSeq.TryGetValue(channel, out int v) ? v : 0;

    private int BumpSeq(string channel)
    {
        int v = SeqOf(channel) + 1;
        _warpSeq[channel] = v;
        return v;
    }

    // 변형 재생. channel 을 비우면 key 를 채널 이름으로 쓴다.
    //
    //   pitch  1 보다 작으면 느리고 낮아진다 — 길이가 1/pitch 배로 늘어난다.
    //          0.5 면 두 배 길고 한 옥타브 낮다.
    //   loop   파일 길이보다 오래 깔아야 하는 소리(폭풍우)에만 쓴다.
    //   delay  겹겹이 쌓을 때 한 겹을 늦춘다.
    public void PlayWarped(string key, float volumeDb = -6f, float pitch = 0.8f,
        string channel = null, bool loop = false, float delaySeconds = 0f)
    {
        string ch = string.IsNullOrEmpty(channel) ? key : channel;
        if (delaySeconds <= 0f) { StartWarped(key, volumeDb, pitch, ch, loop); return; }

        int gen = _warpGen;
        int seq = BumpSeq(ch);
        var timer = GetTree()?.CreateTimer(delaySeconds);
        if (timer == null) return;
        timer.Timeout += () =>
        {
            if (gen != _warpGen || seq != SeqOf(ch) || !IsInstanceValid(this)) return;
            StartWarped(key, volumeDb, pitch, ch, loop);
        };
    }

    private void StartWarped(string key, float volumeDb, float pitch, string channel, bool loop)
    {
        BumpSeq(channel);
        var stream = Load(key);
        if (stream == null) return;
        GameSettings.EnsureWarpBus();

        // 루프는 파일을 복제해서 건다 — 캐시에 든 원본의 loop 를 켜 버리면
        // 그 파일을 쓰는 다른 자리(원샷)까지 끝없이 반복된다.
        if (loop)
        {
            stream = (AudioStream)stream.Duplicate();
            MakeLooping(stream);
        }

        if (!_warp.TryGetValue(channel, out var p) || !IsInstanceValid(p))
        {
            p = new AudioStreamPlayer { Bus = GameSettings.BusWarp };
            AddChild(p);
            _warp[channel] = p;
        }
        if (_warpFade.TryGetValue(channel, out var old) && old != null && old.IsValid()) old.Kill();
        _warpFade.Remove(channel);

        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.PitchScale = Mathf.Clamp(pitch, 0.1f, 4f);
        p.Play();
    }

    // 한 채널을 멈춘다. fadeSeconds > 0 이면 줄여서 끈다(폭풍우처럼 길게 깔린 소리).
    public void StopWarped(string channel, float fadeSeconds = 0f)
    {
        if (string.IsNullOrEmpty(channel)) return;
        // 아직 울리지 않은 예약도 같이 취소한다 — 안 그러면 컷이 끝난 뒤에 뒤늦게 터진다.
        BumpSeq(channel);
        if (!_warp.TryGetValue(channel, out var p) || !IsInstanceValid(p)) return;
        if (_warpFade.TryGetValue(channel, out var old) && old != null && old.IsValid()) old.Kill();
        _warpFade.Remove(channel);

        if (fadeSeconds <= 0f || !p.Playing) { p.Stop(); return; }
        var t = CreateTween();
        _warpFade[channel] = t;
        t.TweenProperty(p, "volume_db", -60f, fadeSeconds);
        t.TweenCallback(Callable.From(() => { if (IsInstanceValid(p)) p.Stop(); }));
    }

    // 변형 재생 전부 정지 + 예약 취소. 프롤로그 · 엔딩 컷씬이 끝날 때 한 번 부른다.
    public void StopAllWarped(float fadeSeconds = 0f)
    {
        _warpGen++;
        foreach (string ch in new List<string>(_warp.Keys)) StopWarped(ch, fadeSeconds);
    }

    public bool WarpedPlaying(string channel) =>
        _warp.TryGetValue(channel, out var p) && IsInstanceValid(p) && p.Playing;

    // 돌고 있는 변형음의 음량만 바꾼다.
    public void SetWarpedVolume(string channel, float volumeDb)
    {
        if (_warp.TryGetValue(channel, out var p) && IsInstanceValid(p)) p.VolumeDb = volumeDb;
    }

    // 돌고 있는 변형음의 음량을 **서서히** 바꾼다. 끄지는 않는다 —
    // 폭발 직전의 '정적' 한 박자처럼 깔려 있던 소리를 잠깐 재워 둘 때 쓴다.
    public void FadeWarped(string channel, float volumeDb, float seconds)
    {
        if (!_warp.TryGetValue(channel, out var p) || !IsInstanceValid(p)) return;
        if (_warpFade.TryGetValue(channel, out var old) && old != null && old.IsValid()) old.Kill();
        if (seconds <= 0f) { p.VolumeDb = volumeDb; _warpFade.Remove(channel); return; }
        var t = CreateTween();
        _warpFade[channel] = t;
        t.TweenProperty(p, "volume_db", volumeDb, seconds);
    }
}
