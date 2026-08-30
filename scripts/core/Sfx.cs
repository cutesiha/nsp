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

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            AddChild(p);
            _pool.Add(p);
        }

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
