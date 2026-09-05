using Godot;

namespace NSP.Core;

// 게임 설정(음량 / 전체화면 / 그래픽 품질 / 조작키). 시작 화면의 설정 창이 이걸 읽고 쓴다.
// user://nsp_settings.cfg 에 저장되어 다음 실행에도 유지된다.
//
// 오디오는 Master 아래에 BGM / SFX 두 버스를 런타임에 만들어 쓴다(프로젝트에 버스
// 레이아웃 파일이 없어도 동작). 음악은 BGM, 나머지 소리는 SFX 로 보낸다.
public static class GameSettings
{
    public const string BusMaster = "Master";
    public const string BusBgm = "BGM";
    public const string BusSfx = "SFX";

    private const string ConfigPath = "user://nsp_settings.cfg";

    // 그래픽 품질은 Godot 렌더러까지 함께 바꾼다. 렌더러는 실행 중에 교체할 수 없으므로
    // 선택한 뒤 게임을 재시작해 적용한다. 해상도 배율은 같은 렌더러 안의 보조 절감 옵션이다.
    public enum Quality { High, Medium, Low }

    public static readonly (Quality Q, string Label, string RenderingMethod, string RenderingDriver, float Scale)[] QualityLevels =
    {
        (Quality.High,   "높음", "forward_plus",     "d3d12",   1.00f),
        (Quality.Medium, "보통", "mobile",           "d3d12",   0.82f),
        // 0.65는 팔/손가락처럼 작은 스킨 메시가 심하게 뭉개져 전화 모션이 달라 보였다.
        // 0.72여도 렌더 픽셀 수는 최고 품질의 약 52%라 저사양 모드는 충분히 가볍다.
        (Quality.Low,    "낮음", "gl_compatibility", "opengl3", 0.72f),
    };

    // 숫자키로 확대할 대상들.
    public enum ZoomTarget { Monitor1, Monitor2, Sensor, PowerPanel }

    public static readonly (ZoomTarget Target, string Label)[] ZoomTargets =
    {
        (ZoomTarget.Monitor1, "모니터 1 확대"),
        (ZoomTarget.Monitor2, "모니터 2 확대"),
        (ZoomTarget.Sensor, "경고 단말기 확대"),
        (ZoomTarget.PowerPanel, "전력 기기 확대"),
    };

    private static readonly Key[] DefaultKeys = { Key.Key1, Key.Key2, Key.Key3, Key.Key4 };
    private static readonly Key[] Keys = { Key.Key1, Key.Key2, Key.Key3, Key.Key4 };

    private static float _master = 0.85f, _bgm = 0.8f, _sfx = 0.9f;
    private static bool _fullscreen;
    private static Quality _quality = Quality.High;
    private static bool _loaded;

    public static float MasterVolume { get => _master; set { _master = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static float BgmVolume { get => _bgm; set { _bgm = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static float SfxVolume { get => _sfx; set { _sfx = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static bool Fullscreen { get => _fullscreen; set { _fullscreen = value; ApplyFullscreen(); } }
    public static Quality GraphicsQuality { get => _quality; set { _quality = value; ApplyQuality(); } }
    public static string QualityLabel => QualityLevels[(int)_quality].Label;
    public static string RenderingMethod => QualityLevels[(int)_quality].RenderingMethod;

    public static Key GetKey(ZoomTarget t) => Keys[(int)t];

    // 같은 키가 두 기능에 겹치면 원래 그 키를 쓰던 쪽과 자리를 바꾼다.
    public static void SetKey(ZoomTarget t, Key key)
    {
        int idx = (int)t;
        for (int i = 0; i < Keys.Length; i++)
            if (i != idx && Keys[i] == key) Keys[i] = Keys[idx];
        Keys[idx] = key;
    }

    public static ZoomTarget? TargetForKey(Key key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (Keys[i] == key) return (ZoomTarget)i;
        return null;
    }

    // --- 적용 -------------------------------------------------------------

    // Master 아래 BGM / SFX 버스를 보장한다. 오디오 노드가 만들어지기 전에 불려야 한다.
    public static void EnsureBuses()
    {
        foreach (string name in new[] { BusBgm, BusSfx })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            int idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, BusMaster);
        }
    }

    private static void ApplyAudio()
    {
        EnsureBuses();
        SetBusVolume(BusMaster, _master);
        SetBusVolume(BusBgm, _bgm);
        SetBusVolume(BusSfx, _sfx);
    }

    private static void SetBusVolume(string bus, float linear)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, linear <= 0.0005f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(linear, 0.0005f)));
    }

    // 전체화면은 해상도를 늘리는 게 아니라 '화면 전체를 쓰는 창'으로 바꾸는 것.
    // 콘텐츠 비율은 project.godot 의 stretch 설정(canvas_items / keep)이 유지해 준다.
    private static void ApplyFullscreen()
    {
        var want = _fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        DisplayServer.WindowSetMode(want);

        // 에디터에서 F5 로 실행하면 게임 창이 에디터 안에 '임베드' 되는데, 임베드된 창은
        // 전체화면 전환이 무시된다(엔진 제한). 조용히 아무 일도 안 일어난 것처럼 보이므로
        // 왜 안 먹었는지 로그로 남긴다.
        if (DisplayServer.WindowGetMode() == want) return;
        GD.PushWarning(
            $"GameSettings: 전체화면 전환이 적용되지 않았습니다(요청={want}, 실제={DisplayServer.WindowGetMode()}). " +
            "에디터에서 실행 중이라면 에디터 설정 → 실행 → Window Placement → Game Embed Mode 를 " +
            "'Disabled' 로 바꾸거나, 내보낸 실행 파일에서 확인하세요.");
    }

    // 3D 렌더 배율만 낮춘다. 창 크기·UI 해상도는 그대로라 글자는 선명하게 유지된다.
    private static void ApplyQuality()
    {
        if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
            tree.Root.Scaling3DScale = QualityLevels[(int)_quality].Scale;
    }

    // 렌더러는 초기화 전에만 고를 수 있다. 내보낸 게임에서는 선택 직후 재시작한다.
    // F5 실행은 Godot 자체가 재시작을 지원하지 않으므로 창을 닫거나 project.godot를
    // 수정하지 않는다. 이 경우에는 같은 렌더러 안에서 해상도 배율만 즉시 반영된다.
    public static bool RestartForGraphicsQuality()
    {
        var level = QualityLevels[(int)_quality];
        Save();

        // 에디터에서 F5로 실행 중이면 project.godot를 쓰거나 게임을 종료하지 않는다.
        if (OS.HasFeature("editor")) return false;
        if (Engine.GetMainLoop() is not SceneTree tree) return false;
        OS.SetRestartOnExit(true, new[]
        {
            "--rendering-method", level.RenderingMethod,
            "--rendering-driver", level.RenderingDriver,
        });
        tree.Quit();
        return true;
    }

    // --- 저장 / 불러오기 ---------------------------------------------------

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) == Error.Ok)
        {
            _master = (float)cfg.GetValue("audio", "master", _master);
            _bgm = (float)cfg.GetValue("audio", "bgm", _bgm);
            _sfx = (float)cfg.GetValue("audio", "sfx", _sfx);
            _fullscreen = (bool)cfg.GetValue("video", "fullscreen", _fullscreen);
            _quality = (Quality)Mathf.Clamp((int)cfg.GetValue("video", "quality", (int)_quality), 0, QualityLevels.Length - 1);
            for (int i = 0; i < Keys.Length; i++)
                Keys[i] = (Key)(int)cfg.GetValue("keys", ((ZoomTarget)i).ToString(), (int)DefaultKeys[i]);
        }

        ApplyAudio();
        ApplyFullscreen();
        ApplyQuality();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", _master);
        cfg.SetValue("audio", "bgm", _bgm);
        cfg.SetValue("audio", "sfx", _sfx);
        cfg.SetValue("video", "fullscreen", _fullscreen);
        cfg.SetValue("video", "quality", (int)_quality);
        for (int i = 0; i < Keys.Length; i++)
            cfg.SetValue("keys", ((ZoomTarget)i).ToString(), (int)Keys[i]);
        cfg.Save(ConfigPath);
    }

    public static void ResetKeysToDefault()
    {
        for (int i = 0; i < Keys.Length; i++) Keys[i] = DefaultKeys[i];
    }

    // 키 이름을 사람이 읽는 형태로.
    public static string KeyName(Key k) => k switch
    {
        >= Key.Key0 and <= Key.Key9 => ((int)k - (int)Key.Key0).ToString(),
        >= Key.Kp0 and <= Key.Kp9 => "숫자패드 " + ((int)k - (int)Key.Kp0),
        _ => OS.GetKeycodeString(k),
    };
}
