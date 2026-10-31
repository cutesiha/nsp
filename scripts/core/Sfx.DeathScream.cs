using Godot;

namespace NSP.Core;

// ── 직원 사망 비명 (2026-10-31 사용자 요청) ─────────────────────────────────
//
// 남자 직원이면 scream/boy_scream, 여자 직원이면 scream/girl_scream.
// 녹음 하나를 그대로 틀면 "비명 효과음" 으로 들린다 — 세 겹으로 일그러뜨려 사람이 아니게 만든다.
//
//   ① 원음     — 울리는 비명 버스(Scream: 긴 잔향 · 두꺼운 저역 · 리미터)로 크게, 살짝 낮게.
//   ② 끌리는 울음 — 같은 소리를 Warp 버스(저역통과 · 페이저 · 깊은 잔향)로 한참 낮춰 깐다.
//                  끝으로 갈수록 더 늘어지고 낮아진다 — 숨이 꺼지면서 목소리가 녹아내린다.
//   ③ 찢어지는 겹 — 조금 늦게, 높게, 작게. 목소리가 둘로 갈라진 것처럼 들린다.
//
// 세 겹이 **다 멎을 때까지**가 "비명이 울리는 동안" 이다(DeathScreamPlaying) —
// 화면 떨림 · 천장의 붉은 점멸(View.DeathScreamFx)이 이 값을 보고 함께 끝난다.
public partial class Sfx
{
    private const string BoyScreamPath = "res://assets/audio/scream/boy_scream.mp3";
    private const string GirlScreamPath = "res://assets/audio/scream/girl_scream.mp3";

    private AudioStreamPlayer[] _deathLayers;
    private Tween _deathBend;
    private int _deathSerial;

    public bool DeathScreamPlaying
    {
        get
        {
            if (_deathLayers == null) return false;
            foreach (var p in _deathLayers)
                if (IsInstanceValid(p) && p.Playing) return true;
            return false;
        }
    }

    // 비명을 튼다. 음원을 못 찾으면 false — 부르는 쪽이 예전 합성 비명으로 대신한다.
    // pan: 비명이 난 방이 중앙제어실의 왼쪽(-) / 오른쪽(+) 어디인가. 원음 겹만 그쪽에서 들린다.
    public bool PlayDeathScream(bool female, float pan = 0f)
    {
        string path = female ? GirlScreamPath : BoyScreamPath;
        var stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        if (stream == null) return false;

        GameSettings.EnsureWarpBus();
        if (_deathLayers == null)
        {
            _deathLayers = new[]
            {
                new AudioStreamPlayer { Name = "DeathScreamMain", Bus = GameSettings.BusScreamPan },
                new AudioStreamPlayer { Name = "DeathScreamLow", Bus = GameSettings.BusWarp },
                new AudioStreamPlayer { Name = "DeathScreamHigh", Bus = GameSettings.BusSfx },
            };
            foreach (var p in _deathLayers) AddChild(p);
        }
        int serial = ++_deathSerial;
        GameSettings.SetScreamPan(pan);
        float len = (float)stream.GetLength();
        if (len <= 0f) len = 3f;

        // 비명이 도는 동안은 시설 소음 · 음악을 깊이 누른다 — 그 방에서 난 소리만 남게.
        DuckAmbience(-16f, len * 1.5f);
        DuckMusic(-10f, len * 1.4f, 0.06f);

        var main = _deathLayers[0];
        main.Stream = stream;
        // girl_scream 원본이 boy_scream 보다 2dB 작다(LoudnessProbe) - 그만큼 올려 남녀 크기를 맞춘다.
        // 큰 소리는 의도지만, 예전 +4dB 는 비명 버스 리미터를 너무 세게 눌러 소리가 뭉개졌다 - 조금 내렸다.
        float even = female ? 2f : 0f;
        main.VolumeDb = 1f + even;
        main.PitchScale = 0.92f;
        main.Play();

        const float LowFrom = 0.68f, LowTo = 0.46f;
        var low = _deathLayers[1];
        low.Stream = stream;
        low.VolumeDb = -1f + even;
        low.PitchScale = LowFrom;
        low.Play();
        // 낮은 겹은 재생하는 내내 더 느려진다. 길이는 평균 배속으로 잡는다(끝까지 늘어지게).
        _deathBend?.Kill();
        _deathBend = CreateTween();
        _deathBend.TweenProperty(low, "pitch_scale", LowTo, len / ((LowFrom + LowTo) * 0.5f))
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);

        var high = _deathLayers[2];
        high.Stop();
        var timer = GetTree()?.CreateTimer(0.09);
        if (timer != null)
            timer.Timeout += () =>
            {
                if (serial != _deathSerial || !IsInstanceValid(high)) return;
                high.Stream = stream;
                high.VolumeDb = -9f + even;
                high.PitchScale = 1.17f;
                high.Play();
            };
        return true;
    }

    public void StopDeathScream()
    {
        _deathSerial++;
        _deathBend?.Kill();
        if (_deathLayers == null) return;
        foreach (var p in _deathLayers)
            if (IsInstanceValid(p)) p.Stop();
    }
}
