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
    // 시설 환경음(기계 · 환기) 전용. SFX 로 보내므로 효과음 볼륨 설정을 그대로 따르되,
    // 음악과는 완전히 분리된다 — BGM 을 내려도 시설 소음은 계속 돈다.
    public const string BusAmbience = "Ambience";
    // 무전/인터컴 전용. SFX 로 보내므로 효과음 볼륨 설정을 그대로 따른다.
    // 기존 직원 보이스를 그대로 통과시키되 대역만 좁혀 "무전기에서 나오는 소리"로 만든다.
    public const string BusRadio = "Radio";
    public const string BusScream = "Scream";
    // 공포 ambience 전용. SFX 로 보내므로 효과음 볼륨 설정을 그대로 따른다.
    //
    // 버스를 따로 두는 이유는 둘이다.
    //   ① 좌우 패닝(AudioEffectPanner)을 **이 소리들에만** 걸 수 있다
    //   ② 게임 정보음(전화벨 · 경고)과 섞이지 않게 통째로 눌러 둘 수 있다(§13 · §21)
    // 가까운 소리는 이 버스를 아주 살짝 어둡게 통과시킨다 — 방 밖에서 들리는 느낌.
    public const string BusHorror = "Horror";
    // 프롤로그 재난 연출 전용 — 받아 온 음원(유리 폭발 · 재난음 · 비명 · 폭풍우)을
    // 원본 그대로가 아니라 **기괴하게 일그러뜨려** 내보내는 길이다. SFX 로 보내므로
    // 효과음 볼륨 설정을 그대로 따른다. 이 버스는 처음 쓸 때 만들어진다 —
    // 실시간 근무 중에는 존재하지 않아 잔향 · 페이저 비용이 들지 않는다.
    public const string BusWarp = "Warp";

    private const string ConfigPath = "user://nsp_settings.cfg";

    // 그래픽 품질. **실행 중에 그 자리에서 바뀐다** — 게임을 껐다 켜지 않는다.
    //
    // 예전에는 품질마다 Godot 렌더러(forward_plus / mobile / gl_compatibility)까지 바꿨다.
    // 렌더러는 초기화 전에만 고를 수 있어서, 고를 때마다 게임이 재시작돼야 했고
    // 에디터 실행에서는 아예 적용되지 않았다. 지금은 **실행 중에 바꿀 수 있는 것만** 쓴다.
    //   · 3D 렌더 해상도 배율   (가장 큰 절감)
    //   · 빛 번짐(Glow)         (두 번째로 큰 절감)
    //   · 그림자 해상도 · 부드러움
    public enum Quality { High, Medium, Low }

    // Scale = 3D 렌더 해상도 배율 · Glow = 빛 번짐 · ShadowAtlas = 그림자 해상도
    // (0.65는 팔/손가락처럼 작은 메시가 심하게 뭉개져 전화 모션이 달라 보였다.
    //  0.72여도 렌더 픽셀 수는 최고 품질의 약 52%다.)
    public static readonly (Quality Q, string Label, float Scale, bool Glow, int ShadowAtlas)[] QualityLevels =
    {
        (Quality.High,   "높음", 1.00f, true,  4096),
        (Quality.Medium, "보통", 0.82f, true,  2048),
        // 낮음만 번짐을 끈다. 책상 기기의 라벨은 그대로 빛나되 번져 나가지는 않는다 —
        // 대신 모니터 뒤쪽 빛(M0x_BackGlow)은 실제 광원이라 품질과 무관하게 켜져 있어,
        // 번짐이 없어도 모니터 기기의 실루엣은 그대로 보인다.
        (Quality.Low,    "낮음", 0.72f, false, 1024),
    };

    // 숫자키로 확대할 대상들.
    public enum ZoomTarget { Monitor1, Monitor2, Sensor, PowerPanel }

    public static readonly (ZoomTarget Target, string Label)[] ZoomTargets =
    {
        (ZoomTarget.Monitor1, "모니터 1 확대"),
        (ZoomTarget.Monitor2, "모니터 2 확대"),
        (ZoomTarget.Sensor, "관리자 패드 꺼내기"),   // 예전 경고 단말기 자리 — 값 이름은 저장 호환으로 유지
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

    // Master 아래 BGM / SFX 버스와, SFX 아래 Radio 버스를 보장한다.
    // 오디오 노드가 만들어지기 전에 불려야 한다.
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
        EnsureRadioBus();
        EnsureScreamBus();
        EnsureHorrorBus();
        EnsureAmbienceBus();
    }

    // 시설 환경음(기계 · 환기) 전용 버스.
    //
    // **음악과 완전히 따로 둔다.** 근무가 시작되면 BGM 은 페이드아웃되지만 시설 소음은
    // 계속 돌아야 한다 — 같은 버스에 있으면 음악을 줄일 때 시설도 같이 죽는다.
    // SFX 로 보내므로 플레이어의 효과음 볼륨 설정은 그대로 따른다.
    //
    // 대사 · 전화 · 공포음이 울릴 때 이 버스만 잠깐 눌러(Sfx.DuckAmbience) 환경음이
    // 말을 덮지 않게 한다. 그러려면 환경음이 자기 버스에 혼자 있어야 한다.
    private static void EnsureAmbienceBus()
    {
        if (AudioServer.GetBusIndex(BusAmbience) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusAmbience);
        AudioServer.SetBusSend(idx, BusSfx);
    }

    // 공포 ambience 버스. 패너 하나와 아주 얕은 잔향만 둔다.
    //
    // 패너의 pan 값은 재생할 때마다 Sfx.PlayHorror 가 바꾼다 — 그래서 효과 인덱스를
    // 찾아 둘 수 있게 패너를 **맨 앞**에 넣는다(HorrorPannerIndex).
    // 잔향은 아주 얕다. 깊게 걸면 "동굴에서 나는 소리" 가 되어 내 옆에서 난 느낌이 사라진다.
    private static void EnsureHorrorBus()
    {
        if (AudioServer.GetBusIndex(BusHorror) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusHorror);
        AudioServer.SetBusSend(idx, BusSfx);

        AudioServer.AddBusEffect(idx, new AudioEffectPanner { Pan = 0f }, HorrorPannerIndex);
        AudioServer.AddBusEffect(idx, new AudioEffectReverb
        {
            RoomSize = 0.34f,
            Damping = 0.72f,
            Wet = 0.14f,
            Dry = 1f,
            Spread = 0.4f,
        });
    }

    // 공포 버스의 패너 효과 인덱스. 재생 직전에 pan 을 바꿔 좌우를 정한다.
    public const int HorrorPannerIndex = 0;

    // ── 기괴하게 변형하는 버스(프롤로그 재난) ─────────────────────────────
    //
    // 받아 온 음원을 그대로 틀면 "어디서 들어 본 효과음" 으로 들린다. 재난 구간에서는
    // 같은 파일을 **깊은 지하에서 울리는 정체 모를 소리**로 바꿔 내보낸다. 파일을
    // 고치지 않고 버스에서만 일그러뜨리므로, 원본은 다른 자리에서 그대로 쓸 수 있다.
    //
    //   저역통과   위쪽을 깎아 멀고 둔탁하게 — 새 효과음처럼 들리게 하는 가장 큰 몫
    //   페이저     아주 느린 위상 쓸림. "기계가 아닌 무언가" 의 울렁임을 만든다
    //   잔향       거대한 콘크리트 공간. Horror 버스보다 훨씬 깊게 건다
    //
    // 처음 쓸 때 만든다 — 프롤로그 · 엔딩 말고는 이 버스를 지나는 소리가 없으므로
    // 평소에는 버스 자체가 없어 효과 비용이 0 이다.
    public static void EnsureWarpBus()
    {
        if (AudioServer.GetBusIndex(BusWarp) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusWarp);
        AudioServer.SetBusSend(idx, BusSfx);

        AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter
        {
            CutoffHz = 5200f,
            Resonance = 0.38f,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectPhaser
        {
            RangeMinHz = 160f,
            RangeMaxHz = 2600f,
            RateHz = 0.28f,
            Feedback = 0.55f,
            Depth = 2.4f,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectReverb
        {
            PredelayMsec = 90f,
            RoomSize = 0.92f,
            Damping = 0.28f,
            Wet = 0.46f,
            Dry = 0.82f,
            Spread = 1f,
        });
    }

    // 공포 소리의 좌우 위치를 정한다. -1 = 완전 왼쪽, +1 = 완전 오른쪽.
    // 극단값(±1)은 쓰지 않는다 — 헤드폰에서 머리 밖에 붙어 버려 "옆" 이 아니게 된다(§14).
    public static void SetHorrorPan(float pan)
    {
        int idx = AudioServer.GetBusIndex(BusHorror);
        if (idx < 0) return;
        if (AudioServer.GetBusEffect(idx, HorrorPannerIndex) is AudioEffectPanner p)
            p.Pan = Mathf.Clamp(pan, -0.95f, 0.95f);
    }

    // 괴물의 비명 전용 버스. 좁은 지하 시설에서 울리는 느낌을 여기서만 만든다 —
    // 같은 잔향을 Sfx 전체에 걸면 클릭음까지 동굴에서 나는 소리가 된다.
    private static void EnsureScreamBus()
    {
        if (AudioServer.GetBusIndex(BusScream) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusScream);
        AudioServer.SetBusSend(idx, BusSfx);

        // 긴 잔향 — 복도 끝까지 울렸다가 돌아오는 소리.
        AudioServer.AddBusEffect(idx, new AudioEffectReverb
        {
            RoomSize = 0.92f,
            Damping = 0.28f,
            Spread = 1f,
            Wet = 0.62f,
            Dry = 0.85f,
            PredelayMsec = 40f,
            PredelayFeedback = 0.45f,
        });
        // 저역을 살려 몸으로 오는 소리로 만든다(EQ6 대역: 32 · 100 · 320 · 1k · 3.2k · 10kHz).
        var eq = new AudioEffectEQ6();
        eq.SetBandGainDb(0, 7f);
        eq.SetBandGainDb(1, 5f);
        eq.SetBandGainDb(2, 2f);
        AudioServer.AddBusEffect(idx, eq);
        // 최종 리미터 — 아주 크게 밀어 넣어도 찢어지지 않고 천장에 눌러 담는다.
        // PreGain 으로 신호를 천장까지 밀어 올린다(= 평균 음량이 올라가 더 크게 들린다).
        AudioServer.AddBusEffect(idx, new AudioEffectHardLimiter { CeilingDb = -0.3f, PreGainDb = 12f });
        AudioServer.SetBusVolumeDb(idx, 10f);
    }

    // 무전 질감은 이 버스의 필터로만 만든다 — 원본 보이스 파일은 전혀 건드리지 않는다.
    //   대역통과(중역만 남김) → 살짝 찌그러뜨림 → 고역 한 번 더 깎기
    // 목소리를 알아들을 수 있는 선에서 좁고 먹먹하게만 만드는 것이 목표다.
    private static void EnsureRadioBus()
    {
        if (AudioServer.GetBusIndex(BusRadio) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusRadio);
        AudioServer.SetBusSend(idx, BusSfx);

        AudioServer.AddBusEffect(idx, new AudioEffectBandPassFilter
        {
            CutoffHz = 1500f,   // 무전기 스피커 대역
            Resonance = 0.5f,
            Db = AudioEffectFilter.FilterDB.Filter12Db,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectDistortion
        {
            Mode = AudioEffectDistortion.ModeEnum.Clip,
            Drive = 0.18f,      // 아주 약하게 — 강하면 타이핑 보이스를 덮는다
            PostGain = -1f,
        });
        AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter { CutoffHz = 3200f });
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
    // 글로우(블룸)도 같이 끈다 — 내장 GPU 에서 단일 항목으로는 가장 비싼 후처리다
    // (측정: 1080p 기준 프레임의 약 1/4). 씬이 원래 글로우를 쓰는지 기억해 뒀다가
    // '높음' 으로 돌아오면 그대로 되살린다.
    private static bool? _sceneWantsGlow;

    private static void ApplyQuality()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;
        var level = QualityLevels[(int)_quality];

        tree.Root.Scaling3DScale = level.Scale;
        // 그림자 — 해상도와 부드러움을 같이 내린다. 이 방의 그림자는 모니터 스포트라이트
        // 둘이 전부라, 낮음에서도 완전히 끄지는 않는다(끄면 기기 앞뒤 구분이 사라진다).
        tree.Root.PositionalShadowAtlasSize = level.ShadowAtlas;
        var soft = _quality switch
        {
            Quality.High => RenderingServer.ShadowQuality.SoftMedium,
            Quality.Medium => RenderingServer.ShadowQuality.SoftLow,
            _ => RenderingServer.ShadowQuality.SoftVeryLow,
        };
        RenderingServer.PositionalSoftShadowFilterSetQuality(soft);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(soft);

        ApplyEnvironmentQuality(tree.Root.World3D?.Environment);
    }

    // 씬이 바뀌어 Environment 가 새로 올라온 뒤에도 한 번 불러 준다.
    //
    // **빛 번짐(Glow)은 품질과 상관없이 켠다.** 이 게임에서 번짐은 장식이 아니라 조명 그
    // 자체다 — 제어실에 있는 광원은 모니터 스포트라이트 둘뿐이고, 기기 라벨과 화면이
    // 번져 나가는 것으로 나머지 밝기를 만든다. 그래서 껐을 때와 켰을 때가 "조금 예쁘다"
    // 정도가 아니라 아예 다른 방이 된다(예전에는 높음에서만 켜져, 낮음으로 놓고 플레이하면
    // 에디터에서 보던 그림이 안 나왔다).
    //
    // 대신 **번짐의 폭**을 품질로 나눈다. 비용은 번지는 세기가 아니라 blur 단계 수에서
    // 나오므로, 낮은 품질에서는 좁은 단계만 써서 훨씬 싸게 같은 인상을 낸다.
    public static void ApplyEnvironmentQuality(Godot.Environment env)
    {
        if (env == null) return;
        _sceneWantsGlow ??= env.GlowEnabled;
        env.GlowEnabled = _sceneWantsGlow.Value && QualityLevels[(int)_quality].Glow;
        if (!env.GlowEnabled) return;

        // 1~7 단계. 숫자가 클수록 넓고 흐리게 퍼지며 그만큼 비싸다.
        bool wide = _quality == Quality.High;
        env.SetGlowLevel(1, 1f);
        env.SetGlowLevel(2, 1f);
        env.SetGlowLevel(3, 1f);
        env.SetGlowLevel(4, 0.85f);
        env.SetGlowLevel(5, wide ? 0.55f : 0f);
        env.SetGlowLevel(6, 0f);
        env.SetGlowLevel(7, 0f);
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
