using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// 전역 효과음 재생기(autoload). 씬마다 AudioStreamPlayer를 새로 만들지 않고 여기 한 곳을 쓴다.
//  - Play(key, db, pitch) : 원샷. assets/audio/sfx/{key}.wav 를 지연 로드해 풀에서 재생.
//  - Loop(key, db)        : 이름 있는 루프 채널(수리음 등). StopLoop로 정지.
//  - CrossfadeMusic(name, fade, loop, startSec, targetDb, restartIfSame) : assets/audio/bgm/{name}.ogg
//    를 2채널 핑퐁으로 크로스페이드. FadeOutMusic(fade) / StopMusic()(즉시). PlayMusic 은 호환용 래퍼.
//    근무화면(실제 메인 근무)에는 BGM을 켜지 않는다 — 거긴 ControlRoomAtmosphere의 환경음.
//  - 모든 BaseButton 클릭에 자동으로 "click" 효과음을 붙인다(명시 배선 불필요).
// 파일이 아직 Godot에 임포트되지 않았으면 조용히 스킵한다(에디터를 한 번 열면 임포트됨).
public partial class Sfx : Node
{
    public static Sfx Instance { get; private set; }

    private const int PoolSize = 10;
    private readonly Dictionary<string, AudioStream> _cache = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private readonly Dictionary<string, AudioStreamPlayer> _loops = new();
    private int _next;

    // BGM: 2채널 핑퐁으로 크로스페이드한다. 곡마다 루프/시작오프셋이 달라 채널별로 들고 있음.
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[2];
    private readonly bool[] _musicLoop = { true, true };
    private int _musicActive;
    private string _musicName = "";
    private Tween _musicFadeIn, _musicFadeOut;
    private const float MusicSilentDb = -50f;

    // 대사 타자 효과에 맞춰 나는 캐릭터별 짧은 보이스 블립. 전용 채널 하나만 써서
    // 여러 블립이 겹쳐 터지지 않게 하고(새로 Play()하면 이전 소리를 자연히 끊는다),
    // 최소 간격(Rate Limit)으로 너무 촘촘하게 울리지 않게 한다.
    private AudioStreamPlayer _voicePlayer;
    private readonly RandomNumberGenerator _voiceRng = new();
    private readonly Dictionary<string, List<AudioStream>> _voiceVariantCache = new();
    private double _lastVoiceBlipMsec = -10000;
    private const double VoiceBlipMinIntervalMsec = 55;
    private const float VoicePitchVariationSemitones = 1.5f;

    public override void _EnterTree()
    {
        Instance = this;
        // 오디오 노드가 만들어지기 전에 BGM/SFX 버스를 준비하고 저장된 설정을 적용한다.
        GameSettings.EnsureBuses();
        GameSettings.Load();
    }

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer { Bus = GameSettings.BusSfx };
            AddChild(p);
            _pool.Add(p);
        }

        _voicePlayer = new AudioStreamPlayer { Bus = GameSettings.BusSfx };
        AddChild(_voicePlayer);

        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            _music[i] = new AudioStreamPlayer { Bus = GameSettings.BusBgm, VolumeDb = MusicSilentDb };
            _music[i].Finished += () => OnMusicFinished(idx);
            AddChild(_music[i]);
        }

        GetTree().NodeAdded += OnNodeAdded;
    }

    private void OnMusicFinished(int idx)
    {
        // 활성 채널의 루프 곡만 다시 재생(비루프 곡은 그냥 끝난다).
        if (idx != _musicActive || !_musicLoop[idx]) return;
        var p = _music[idx];
        if (IsInstanceValid(p) && p.Stream != null) p.Play();
    }

    // --- BGM (bgm/ 폴더) — 크로스페이드 ------------------------------------
    // 기존 호출 호환: 즉시(짧은 페이드) 루프 재생.
    public void PlayMusic(string name, float volumeDb = -6f) => CrossfadeMusic(name, 0.15f, true, 0f, volumeDb);

    // 현재 곡을 페이드아웃하며 name 을 페이드인한다.
    //  fadeSeconds  : 페이드 길이(아웃/인 동시 = 크로스페이드)
    //  loop         : 곡이 끝나면 다시 재생할지
    //  startSeconds : 이 지점부터 재생 시작
    //  restartIfSame: 같은 곡이어도 처음부터 다시 페이드(씬 전환 체감용)
    public void CrossfadeMusic(string name, float fadeSeconds = 0.8f, bool loop = true,
                               float startSeconds = 0f, float targetDb = -6f, bool restartIfSame = false)
    {
        if (name == _musicName && !restartIfSame && _music[_musicActive].Playing) return;

        // BGM은 용량 때문에 .ogg 로 보관한다(.wav 는 이전 자산 호환용 폴백).
        string path = $"res://assets/audio/bgm/{name}.ogg";
        if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/bgm/{name}.wav";
        if (!ResourceLoader.Exists(path)) { FadeOutMusic(fadeSeconds); return; }
        float fade = Mathf.Max(0.02f, fadeSeconds);

        int inIdx = 1 - _musicActive;
        var outP = _music[_musicActive];
        var inP = _music[inIdx];

        _musicFadeOut?.Kill();
        if (outP.Playing)
        {
            _musicFadeOut = CreateTween();
            _musicFadeOut.TweenProperty(outP, "volume_db", MusicSilentDb, fade).SetTrans(Tween.TransitionType.Sine);
            _musicFadeOut.TweenCallback(Callable.From(() => { if (IsInstanceValid(outP)) outP.Stop(); }));
        }

        _musicFadeIn?.Kill();
        inP.Stream = GD.Load<AudioStream>(path);
        inP.VolumeDb = MusicSilentDb;
        inP.Play(startSeconds);
        _musicLoop[inIdx] = loop;
        _musicFadeIn = CreateTween();
        _musicFadeIn.TweenProperty(inP, "volume_db", targetDb, fade).SetTrans(Tween.TransitionType.Sine);

        _musicActive = inIdx;
        _musicName = name;
    }

    public void FadeOutMusic(float fadeSeconds = 0.8f)
    {
        _musicName = "";
        _musicFadeIn?.Kill();
        _musicFadeOut?.Kill();
        foreach (var p in _music)
        {
            if (!IsInstanceValid(p) || !p.Playing) continue;
            var pp = p;
            _musicFadeOut = CreateTween();
            _musicFadeOut.TweenProperty(pp, "volume_db", MusicSilentDb, Mathf.Max(0.02f, fadeSeconds)).SetTrans(Tween.TransitionType.Sine);
            _musicFadeOut.TweenCallback(Callable.From(() => { if (IsInstanceValid(pp)) pp.Stop(); }));
        }
    }

    public void StopMusic()
    {
        _musicName = "";
        _musicFadeIn?.Kill();
        _musicFadeOut?.Kill();
        foreach (var p in _music) if (IsInstanceValid(p)) p.Stop();
    }

    public override void _ExitTree()
    {
        var tree = GetTree();
        if (tree != null)
            tree.NodeAdded -= OnNodeAdded;
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton b)
            b.Pressed += () => Play("click", -8f);
    }

    private AudioStream Load(string key)
    {
        if (_cache.TryGetValue(key, out var s)) return s;
        string path = key == "electrical_background"
            ? "res://assets/audio/electrical_noise2_[cut_3sec].mp3"
            : $"res://assets/audio/sfx/{key}.wav";
        s = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        _cache[key] = s;
        return s;
    }

    public void Play(string key, float volumeDb = 0f, float pitch = 1f)
    {
        var stream = Load(key);
        if (stream == null) return;

        var p = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        p.Stream = stream;
        p.VolumeDb = volumeDb;
        p.PitchScale = Mathf.Clamp(pitch, 0.1f, 4f);
        p.Play();
    }

    // A lowered alarm, burst of static, and delayed detuned pulse make a warning
    // feel less like a UI notification and more like an equipment fault.
    public void PlayScaryWarning(float volumeDb = -4f)
    {
        Play("alarm", volumeDb, 0.72f);
        Play("noise", volumeDb - 13f, 0.62f);

        var delayedPulse = GetTree().CreateTimer(0.28);
        delayedPulse.Timeout += () =>
        {
            if (!IsInsideTree()) return;
            Play("alarm", volumeDb - 5f, 1.18f);
        };
    }

    // 대화창에서 글자가 한 글자씩 드러날 때마다 호출한다. 공백/문장부호는 울리지 않고,
    // employeeId 로 캐릭터별 보이스 블립을 찾아 재생한다(없는 직원은 조용히 스킵 —
    // 목소리 없는 캐릭터를 추가해도 에러가 나지 않는다).
    // res://assets/audio/sfx_voice_{employeeId}_01.wav, _02, _03... 처럼 variant가
    // 여러 개 있으면 매번 그중 하나를 무작위로 골라 같은 글자에도 완전히 같은 소리가
    // 반복되지 않게 한다(variant가 없는 캐릭터는 voice_{employeeId}.wav 단일 파일로 폴백).
    public void PlayVoiceBlip(string employeeId, char c)
    {
        if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) return;

        double now = Time.GetTicksMsec();
        if (now - _lastVoiceBlipMsec < VoiceBlipMinIntervalMsec) return;

        var variants = LoadVoiceVariants(employeeId);
        if (variants.Count == 0) return;

        _lastVoiceBlipMsec = now;
        _voicePlayer.Stream = variants[_voiceRng.RandiRange(0, variants.Count - 1)];
        float semitones = _voiceRng.RandfRange(-VoicePitchVariationSemitones, VoicePitchVariationSemitones);
        _voicePlayer.PitchScale = Mathf.Pow(2f, semitones / 12f);
        _voicePlayer.Play();
    }

    private List<AudioStream> LoadVoiceVariants(string employeeId)
    {
        if (_voiceVariantCache.TryGetValue(employeeId, out var cached)) return cached;

        var list = new List<AudioStream>();
        for (int i = 1; i <= 6; i++)
        {
            string path = $"res://assets/audio/voice/{employeeId}_{i:00}.wav";
            if (!ResourceLoader.Exists(path)) break;
            var stream = GD.Load<AudioStream>(path);
            if (stream != null) list.Add(stream);
        }

        if (list.Count == 0)
        {
            string single = $"res://assets/audio/voice/{employeeId}.wav";
            if (ResourceLoader.Exists(single))
                list.Add(GD.Load<AudioStream>(single));
        }

        _voiceVariantCache[employeeId] = list;
        return list;
    }

    // 대사 스킵/즉시 완성 시 트레일링 블립을 바로 끊는다.
    public void StopVoiceBlip() => _voicePlayer?.Stop();

    // --- 절차 생성 효과음(에셋 없음) — 직원 비명 / 결번자 웃음 -------------
    private readonly Dictionary<string, AudioStream> _employeeScreamStreams = new();
    private AudioStream _laughStream, _jumpscareToneStream;

    // 기존 보이스 블립의 캐릭터별 높낮이/질감을 따라 만든 짧은 비명.
    // 한 가지 소리를 무작위 피치로 돌려 쓰지 않고 직원마다 별도 파형을 캐시한다.
    public void PlayScream(string employeeId = "", float volumeDb = -5f)
    {
        string id = string.IsNullOrWhiteSpace(employeeId) ? "default" : employeeId.Trim().ToLowerInvariant();
        if (!_employeeScreamStreams.TryGetValue(id, out var scream))
        {
            scream = BuildEmployeeScream(id);
            _employeeScreamStreams[id] = scream;
        }
        PlayGenerated(scream, volumeDb, _voiceRng.RandfRange(0.97f, 1.03f));
    }

    // 결번자 웃음 — 낮은 기음의 하강하는 톤 버스트("허 허 허") + 서브하모닉 왜곡.
    public void PlayEntityLaugh(float volumeDb = -4f)
    {
        _laughStream ??= BuildLaugh();
        PlayGenerated(_laughStream, volumeDb, _voiceRng.RandfRange(0.94f, 1.03f));
    }

    // 결번자가 플레이어 시야를 덮을 때의 짧고 날카로운 전자음.
    public void PlayJumpscareTone(float volumeDb = -1f)
    {
        _jumpscareToneStream ??= BuildJumpscareTone();
        PlayGenerated(_jumpscareToneStream, volumeDb, 1f);
    }

    private void PlayGenerated(AudioStream s, float db, float pitch)
    {
        if (s == null) return;
        var p = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        p.Stream = s;
        p.VolumeDb = db;
        p.PitchScale = Mathf.Clamp(pitch, 0.1f, 4f);
        p.Play();
    }

    private static AudioStreamWav BuildJumpscareTone()
    {
        const int rate = 22050;
        const float duration = 0.26f;
        int n = (int)(rate * duration);
        var pcm = new byte[n * 2];
        var rng = new RandomNumberGenerator();
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float frequency = 1550f + 180f * Mathf.Sin(t * Mathf.Tau * 13f);
            phase += frequency / rate * Mathf.Tau;
            float envelope = Mathf.Min(1f, t * 35f) * Mathf.Pow(1f - t, 0.18f);
            float sample = (Mathf.Sin(phase) * 0.76f + rng.RandfRange(-0.24f, 0.24f)) * envelope;
            short value = (short)(Mathf.Clamp(sample, -1f, 1f) * 27000f);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = false,
            Data = pcm,
        };
    }

    private static AudioStreamWav BuildEmployeeScream(string employeeId)
    {
        const int rate = 22050;
        // 낮고 절제된 올빼미/까마귀, 날카로운 고양이, 떨리는 해파리,
        // 밝고 높은 토끼, 중간 톤의 여우로 기존 음성 인상을 유지한다.
        (float startHz, float peakHz, float duration, float rough, float vibrato, float fall) profile = employeeId switch
        {
            "owl" => (310f, 610f, 0.86f, 0.18f, 24f, 0.72f),
            "cat" => (510f, 970f, 0.68f, 0.25f, 38f, 0.60f),
            "jellyfish" => (560f, 1040f, 0.94f, 0.22f, 46f, 0.78f),
            "rabbit" => (590f, 1120f, 0.78f, 0.17f, 42f, 0.66f),
            "crow" => (250f, 540f, 0.82f, 0.34f, 28f, 0.70f),
            "fox" => (430f, 820f, 0.84f, 0.20f, 32f, 0.68f),
            _ => (460f, 860f, 0.78f, 0.25f, 36f, 0.66f),
        };

        int n = (int)(rate * profile.duration);
        var pcm = new byte[n * 2];
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)employeeId.GetHashCode() + 0x51CEul;
        float ph = 0f, breath = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            // 처음 40%에서 급히 치솟고 끝에서 목이 잠기듯 내려간다.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 0.40f));
            float tail = t < 0.62f ? 1f : Mathf.Lerp(1f, profile.fall, (t - 0.62f) / 0.38f);
            float freq = Mathf.Lerp(profile.startHz, profile.peakHz, rise) * tail;
            freq *= 1f + 0.035f * Mathf.Sin(t * profile.vibrato * Mathf.Tau)
                + 0.012f * Mathf.Sin(t * 91f);
            ph += freq / rate * Mathf.Tau;

            // 성대의 비대칭 파형과 배음, 숨소리를 섞어 순수 전자음처럼 들리지 않게 한다.
            float glottal = Mathf.Sin(ph) + 0.46f * Mathf.Sin(ph * 2.03f)
                + 0.20f * Mathf.Sin(ph * 3.01f) + 0.08f * Mathf.Sin(ph * 4.97f);
            breath = Mathf.Lerp(breath, rng.RandfRange(-1f, 1f), 0.34f);
            float tremble = 0.86f + 0.14f * Mathf.Sin(t * (employeeId == "jellyfish" ? 17f : 11f) * Mathf.Tau);
            float attack = Mathf.Min(1f, t * 34f);
            float release = Mathf.Pow(Mathf.Max(0f, 1f - t), 0.42f);
            float env = attack * release * tremble;
            float voiced = glottal * (0.54f - profile.rough * 0.18f);
            float airy = breath * (0.12f + profile.rough * 0.42f);
            float sample = Mathf.Tanh((voiced + airy) * (1.35f + profile.rough)) * env;
            short v = (short)(Mathf.Clamp(sample, -1f, 1f) * 28500f);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = pcm,
        };
    }

    private static AudioStreamWav BuildLaugh()
    {
        const int rate = 22050;
        int n = (int)(rate * 1.7f);
        var pcm = new byte[n * 2];
        var rng = new RandomNumberGenerator();
        float ph = 0f, phSub = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float baseFreq = Mathf.Lerp(150f, 95f, t);                 // 점점 낮게
            ph += baseFreq / rate * Mathf.Tau;
            phSub += baseFreq * 0.5f / rate * Mathf.Tau;
            // "허 허 허 허" — 8Hz 게이트로 톤을 끊는다.
            float gate = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * n / rate * 8f * Mathf.Pi)), 2.5f);
            float tone = Mathf.Sin(ph) + 0.6f * Mathf.Sin(phSub) + 0.2f * Mathf.Sin(ph * 3f);
            float noise = rng.RandfRange(-1f, 1f) * 0.12f;
            float env = Mathf.Min(1f, t * 8f) * Mathf.Pow(1f - t, 0.4f);
            float s = Mathf.Clamp((tone * gate + noise) * 1.3f, -1f, 1f) * env;
            short v = (short)(s * 24000f);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = pcm,
        };
    }

    // 이름 있는 루프 채널. 같은 key로 다시 부르면 아무것도 하지 않는다(이미 재생 중).
    public void Loop(string key, float volumeDb = 0f)
    {
        if (_loops.ContainsKey(key)) return;
        var stream = Load(key);
        if (stream == null) return;

        var p = new AudioStreamPlayer { Bus = GameSettings.BusSfx, Stream = stream, VolumeDb = volumeDb };
        AddChild(p);
        p.Finished += () => { if (IsInstanceValid(p)) p.Play(); };
        p.Play();
        _loops[key] = p;
    }

    public void StopLoop(string key)
    {
        if (!_loops.TryGetValue(key, out var p)) return;
        _loops.Remove(key);
        if (IsInstanceValid(p)) { p.Stop(); p.QueueFree(); }
    }
}
