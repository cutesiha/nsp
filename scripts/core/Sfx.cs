using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// 전역 효과음 재생기(autoload). 씬마다 AudioStreamPlayer를 새로 만들지 않고 여기 한 곳을 쓴다.
//  - Play(key, db, pitch) : 원샷. assets/audio/sfx_{key}.wav 를 지연 로드해 풀에서 재생.
//  - Loop(key, db)        : 이름 있는 루프 채널(주변 공포음, 수리음 등). StopLoop로 정지.
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

        // 프로젝트 전역 버튼 클릭음.
        // This autoload persists across scene transitions, keeping the electrical
        // background present from the title screen until the game is closed.
        Loop("electrical_background", -17f);

        GetTree().NodeAdded += OnNodeAdded;

        // 항상 깔리는 저음 공포 앰비언스.
        Loop("ambient", -20f);
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
            : $"res://assets/audio/sfx_{key}.wav";
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
            string path = $"res://assets/audio/sfx_voice_{employeeId}_{i:00}.wav";
            if (!ResourceLoader.Exists(path)) break;
            var stream = GD.Load<AudioStream>(path);
            if (stream != null) list.Add(stream);
        }

        if (list.Count == 0)
        {
            var single = Load($"voice_{employeeId}");
            if (single != null) list.Add(single);
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
