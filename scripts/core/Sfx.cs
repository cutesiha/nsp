using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// 전역 효과음 재생기(autoload). 씬마다 AudioStreamPlayer를 새로 만들지 않고 여기 한 곳을 쓴다.
//  - Play(key, db, pitch) : 원샷. assets/audio/sfx/{key}.wav 를 지연 로드해 풀에서 재생.
//  - Loop(key, db)        : 이름 있는 루프 채널(수리음 등). StopLoop로 정지.
//  - CrossfadeMusic(name, fade, loop, startSec, targetDb, restartIfSame) : assets/audio/bgm/{name}.wav
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

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            AddChild(p);
            _pool.Add(p);
        }

        _voicePlayer = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_voicePlayer);

        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            _music[i] = new AudioStreamPlayer { Bus = "Master", VolumeDb = MusicSilentDb };
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

        string path = $"res://assets/audio/bgm/{name}.wav";
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

    // 이름 있는 루프 채널. 같은 key로 다시 부르면 아무것도 하지 않는다(이미 재생 중).
    public void Loop(string key, float volumeDb = 0f)
    {
        if (_loops.ContainsKey(key)) return;
        var stream = Load(key);
        if (stream == null) return;

        var p = new AudioStreamPlayer { Bus = "Master", Stream = stream, VolumeDb = volumeDb };
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
