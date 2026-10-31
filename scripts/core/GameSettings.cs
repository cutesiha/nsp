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
    // 작업실 인터폰 전용. 무전(Radio)과 **일부러 다르게** 들려야 한다 —
    // GUIDE-0 는 시설의 기계 비서라 좁고 차갑게, 직원 전화는 벽에 붙은 낡은 인터폰이라
    // 조금 더 넓고 먹먹하게. 같은 필터를 쓰면 누가 말하는지 귀로 구분되지 않는다.
    public const string BusIntercom = "Intercom";
    public const string BusScream = "Scream";
    // 직원 사망 비명의 방향 - 이 버스에서 좌우를 정하고 비명 버스(잔향 / 리미터)로 보낸다.
    public const string BusScreamPan = "ScreamPan";
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
    //   · 점 광원(Omni) 그림자  (내장 GPU 에서 특히 비싸다 — 아래 PointShadows)
    //
    // **새 항목은 반드시 뒤에 붙인다.** 저장 파일에는 숫자로 들어가 있어서, 가운데에
    // 끼워 넣으면 전에 "낮음" 으로 저장한 사람이 다른 품질로 켜진다.
    public enum Quality { High, Medium, Low, VeryLow }

    // Scale = 3D 렌더 해상도 배율 · Glow = 빛 번짐 · ShadowAtlas = 그림자 해상도
    // PointShadows = 점 광원(OmniLight3D)이 그림자를 드리우는가
    // (0.65는 팔/손가락처럼 작은 메시가 심하게 뭉개져 전화 모션이 달라 보였다.
    //  0.72여도 렌더 픽셀 수는 최고 품질의 약 52%다.)
    public static readonly (Quality Q, string Label, float Scale, bool Glow, int ShadowAtlas, bool PointShadows)[] QualityLevels =
    {
        (Quality.High,   "높음", 1.00f, true,  4096, true),
        (Quality.Medium, "보통", 0.82f, true,  2048, true),
        // 낮음만 번짐을 끈다. 책상 기기의 라벨은 그대로 빛나되 번져 나가지는 않는다 —
        // 대신 모니터 뒤쪽 빛(M0x_BackGlow)은 실제 광원이라 품질과 무관하게 켜져 있어,
        // 번짐이 없어도 모니터 기기의 실루엣은 그대로 보인다.
        (Quality.Low,    "낮음", 0.72f, false, 1024, true),
        // **매우 낮음** — i3 + 내장 그래픽 같은 기계를 위한 칸이다.
        //
        // "낮음" 을 더 내리지 않고 칸을 새로 만든 이유 : 0.72 라는 값은 그냥 고른 것이
        // 아니라, 0.65 에서 전화 받는 손가락이 뭉개져 모션이 달라 보여서 올려 둔 값이다.
        // 그 판단을 뒤집어 모두의 "낮음" 을 흐리게 만드는 대신, 더 필요한 사람만
        // 한 칸 더 내려갈 수 있게 한다.
        //
        // 실측(docs/perf/probe_paired.md, Intel UHD · i3-10110U · 1800×980)
        //   3D 스케일 0.72 → 0.60   프레임 −5.4 ms
        //   3D 스케일 0.72 → 0.52   프레임 −6.8 ms
        //   점 광원 그림자 끄기      프레임 −2.1 ms · draw call −338
        // 둘을 합치면 이 기계에서 25 FPS 대가 33 FPS 대가 된다.
        (Quality.VeryLow, "매우 낮음", 0.58f, false, 512, false),
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
    private static bool _reduceFlashing;
    private static Quality _quality = Quality.High;
    private static bool _loaded;

    public static float MasterVolume { get => _master; set { _master = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static float BgmVolume { get => _bgm; set { _bgm = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static float SfxVolume { get => _sfx; set { _sfx = Mathf.Clamp(value, 0f, 1f); ApplyAudio(); } }
    public static bool Fullscreen { get => _fullscreen; set { _fullscreen = value; ApplyFullscreen(); } }
    // 깜빡임 · 흔들림 줄이기(광과민성 배려). 켜면 붉은 점멸은 느린 맥박으로, 화면 섬광 · 카메라 흔들림은 약하게 바뀐다.
    public static bool ReduceFlashing { get => _reduceFlashing; set => _reduceFlashing = value; }
    // 섬광 · 흔들림에 곱하는 배율 — 줄이기를 켜면 0.3.
    public static float FlashScale => _reduceFlashing ? 0.3f : 1f;
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
        EnsureIntercomBus();
        EnsureScreamBus();
        EnsureScreamPanBus();
        EnsureAnalogSfxChain();
        EnsureHorrorBus();
        EnsureAmbienceBus();
        EnsureMasterLimiter();
    }

    // 마지막 안전장치 — 모든 소리가 모이는 Master 끝에 리미터 하나. 비명 버스처럼 일부러 크게 미는 소리와
    // 음악 · 효과음이 한꺼번에 겹쳐도 0dBFS 를 넘어 깨지지(클리핑) 않는다. 평소 크기는 건드리지 않는다
    // (끌어올리는 값 없이 -1dB 위의 순간 최고점만 누른다) — 헤드폰으로 듣는 사람의 귀를 지키는 자리다.
    // 비명이 난 방 쪽에서 들리게 - 좌우 패너 하나만 두고 비명 버스로 넘긴다(잔향 / 저역 / 리미터는 그쪽 몫).
    private static AudioEffectPanner _screamPanner;

    private static void EnsureScreamPanBus()
    {
        int idx = AudioServer.GetBusIndex(BusScreamPan);
        if (idx < 0)
        {
            idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, BusScreamPan);
            AudioServer.SetBusSend(idx, BusScream);
            AudioServer.AddBusEffect(idx, new AudioEffectPanner());
        }
        _screamPanner = AudioServer.GetBusEffect(idx, 0) as AudioEffectPanner;
    }

    // -1 왼쪽 ~ +1 오른쪽.
    public static void SetScreamPan(float pan)
    {
        EnsureScreamPanBus();
        if (_screamPanner != null) _screamPanner.Pan = Mathf.Clamp(pan, -1f, 1f);
    }

    private static void EnsureMasterLimiter()
    {
        int master = AudioServer.GetBusIndex(BusMaster);
        if (master < 0) return;
        for (int i = 0; i < AudioServer.GetBusEffectCount(master); i++)
            if (AudioServer.GetBusEffect(master, i) is AudioEffectHardLimiter) return;
        AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = -1f, PreGainDb = 0f, Release = 0.1f });
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

    // ── 시설 장비를 거쳐 나온 소리 (아날로그 호러) ───────────────────────
    //
    // 게임의 **모든 소리**가 1970~90년대 지하 연구시설의 장비를 통해 들린다.
    // 효과음 파일을 한 개도 바꾸지 않고, 그것들이 지나는 길에 장비의 성질을 입힌다
    // — 그래야 새 소리를 넣을 때마다 따로 손볼 필요가 없다.
    //
    // SFX 버스 하나에만 건다. 그 아래로 환경음 · 공포음 · 무전 · 인터폰 · 비명이
    // 전부 흘러들어오므로 한 번에 같은 성질을 받고, **음악(BGM)은 받지 않는다** —
    // 음악까지 좁히면 그냥 고장 난 스피커가 된다.
    //
    // 다섯 겹, 전부 약하게. 하나씩은 "들릴 듯 말 듯" 이지만 겹치면 소리의 출처가
    // 바뀐다 — 게임이 내는 소리가 아니라 **그 방의 장비가 내는 소리**가 된다.
    //
    //   ① 저역 깎기   작은 스피커는 바닥 울림을 못 낸다
    //   ② 테이프 흔들림 감은 테이프가 고르게 돌지 않는다 — 아날로그 호러의 핵심
    //   ③ 비트 깎기   낡은 디지털 · 자기 녹음의 거친 결
    //   ④ 고역 깎기   공기감 · 반짝임이 없다
    //   ⑤ 짧은 잔향   콘크리트 지하실
    //
    // 세기를 바꾸려면 아래 숫자만 고친다. 전부 0 으로 두면 예전 소리 그대로다.
    private const string AnalogChainMark = "nsp_analog_chain";

    private static void EnsureAnalogSfxChain()
    {
        int idx = AudioServer.GetBusIndex(BusSfx);
        if (idx < 0) return;
        // 두 번 걸지 않는다 — EnsureBuses 는 여러 번 불릴 수 있다.
        for (int i = 0; i < AudioServer.GetBusEffectCount(idx); i++)
            if (AudioServer.GetBusEffect(idx, i) is { } fx && fx.HasMeta(AnalogChainMark)) return;

        void Add(AudioEffect fx)
        {
            fx.SetMeta(AnalogChainMark, true);
            AudioServer.AddBusEffect(idx, fx);
        }

        // ① 작은 스피커 — 바닥이 없다.
        Add(new AudioEffectHighPassFilter { CutoffHz = 115f, Resonance = 0.4f });

        // ② 테이프 흔들림(wow). 느리고 아주 얕게 — 빠르거나 깊으면 "고장" 으로 들린다.
        var wow = new AudioEffectChorus { VoiceCount = 2, Wet = 0.12f, Dry = 1f };
        // 두 겹의 주기가 서로 안 맞아떨어져야 흔들림이 규칙으로 들리지 않는다.
        wow.SetVoiceDelayMs(0, 14f); wow.SetVoiceRateHz(0, 0.35f);
        wow.SetVoiceDepthMs(0, 1.4f); wow.SetVoiceLevelDb(0, -3f);
        wow.SetVoiceDelayMs(1, 21f); wow.SetVoiceRateHz(1, 0.21f);
        wow.SetVoiceDepthMs(1, 1.0f); wow.SetVoiceLevelDb(1, -6f);
        Add(wow);

        // ③ 비트를 깎는다 — 자기 녹음 · 낡은 디지털의 거친 결.
        //    Drive 를 올리면 금방 지저분해진다. 알아듣는 데 지장 없는 선까지만.
        Add(new AudioEffectDistortion
        {
            Mode = AudioEffectDistortion.ModeEnum.Lofi,
            Drive = 0.14f, PreGain = 0f, PostGain = -1f,
        });

        // ④ 공기감을 덜어낸다.
        Add(new AudioEffectLowPassFilter { CutoffHz = 8600f, Resonance = 0.3f });

        // ⑤ 콘크리트 지하실. 젖은 정도를 아주 낮게 둔다 — 높이면 경고음이 뭉개진다.
        Add(new AudioEffectReverb
        {
            RoomSize = 0.42f, Damping = 0.62f, Wet = 0.085f, Dry = 1f, Spread = 0.7f,
        });
    }

    // 작업실 인터폰 — 벽에 붙은 낡은 스피커.
    //
    //   저역 깎기(통 울림 제거) → 고역 깎기(좁은 스피커) → 아주 짧은 잔향(벽에 붙은 상자)
    // 무전(Radio)보다 **대역이 넓고 찌그러짐이 없다.** 진술을 알아듣는 데 방해가 되면
    // 안 되기 때문이다(지시서 §6) — 통화는 추리에 필요한 정보를 듣는 자리다.
    private static void EnsureIntercomBus()
    {
        if (AudioServer.GetBusIndex(BusIntercom) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, BusIntercom);
        AudioServer.SetBusSend(idx, BusSfx);

        AudioServer.AddBusEffect(idx, new AudioEffectHighPassFilter { CutoffHz = 420f });
        AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter { CutoffHz = 4200f });
        AudioServer.AddBusEffect(idx, new AudioEffectReverb
        {
            RoomSize = 0.18f, Damping = 0.75f, Wet = 0.12f, Dry = 1f, Spread = 0.2f,
        });
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

    // ── 화면 기준 1920x1080 ─────────────────────────────────────────────
    //
    // 이 게임의 모든 화면(HUD · 통화창 · 컷신 · 제어실 카메라 구도)은 1920x1080 한 벌로 짠다.
    // 창 크기는 컴퓨터마다 달라도 되지만, 그 안의 화면은 언제나 1920x1080 을 통째로 줄이거나 키운
    // 것이어야 한다(창 비율이 다르면 위아래나 양옆이 검게 빈다 — 잘리거나 늘어나지 않는다).
    //
    // 예전에는 노트북에서 창이 넘치면 project.godot 의 기준 해상도 자체를 바꿔 맞췄다
    // (1920x1080 → 1600x900 → 1600x800 → 1800x980 → 1700x880 …). 그러면 화면 **비율**이 같이 바뀌어
    // 모니터 확대 · 스토리 화면이 이상하게 확대되고 잘렸다(2026-10-30 제보 — 1720x900 · 1080x920).
    // 그래서 기준을 여기서 못 박는다. project.godot 의 viewport 크기를 바꾸면 **창 크기만** 바뀐다.
    public static readonly Vector2I DesignResolution = new(1920, 1080);

    private static void ApplyDesignResolution()
    {
        // 에디터 자체의 창을 건드리면 안 된다([Tool] 스크립트가 설정을 읽는 경우).
        if (Engine.IsEditorHint()) return;
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;
        var root = tree.Root;
        root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        root.ContentScaleSize = DesignResolution;
    }

    // 창 모드에서 창이 화면(작업 표시줄 제외)보다 크면 16:9 를 지킨 채 화면 안에 들어오게 줄이고 가운데 둔다.
    // 1920x1080 노트북은 제목 줄 · 작업 표시줄 때문에 1920x1080 창이 들어가지 않는다 — 기준을 바꾸지 말고
    // 창만 줄이면 된다. 이미 들어가는 창은 건드리지 않는다(검사 씬의 --resolution 도 그대로 둔다).
    private static void FitWindowToScreen()
    {
        if (_fullscreen || Engine.IsEditorHint() || Engine.IsEmbeddedInEditor()) return;
        if (DisplayServer.GetName() == "headless") return;
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;

        int screen = DisplayServer.WindowGetCurrentScreen();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        if (usable.Size.X <= 0 || usable.Size.Y <= 0) return;

        Vector2I inner = DisplayServer.WindowGetSize();
        Vector2I outer = DisplayServer.WindowGetSizeWithDecorations();
        var deco = new Vector2I(Mathf.Max(0, outer.X - inner.X), Mathf.Max(0, outer.Y - inner.Y));
        // 창이 아직 화면에 자리 잡기 전이면 테두리 크기를 모른다 — 윈도 기본 제목 줄 · 테두리로 어림한다.
        if (deco.Y <= 0)
        {
            float dpi = Mathf.Max(1f, DisplayServer.ScreenGetDpi(screen) / 96f);
            deco = new Vector2I(Mathf.RoundToInt(16 * dpi), Mathf.RoundToInt(39 * dpi));
        }
        int maxW = usable.Size.X - deco.X;
        int maxH = usable.Size.Y - deco.Y;
        if (maxW <= 0 || maxH <= 0 || (inner.X <= maxW && inner.Y <= maxH)) return;

        float aspect = (float)DesignResolution.X / DesignResolution.Y;
        int w = Mathf.Min(maxW, Mathf.FloorToInt(maxH * aspect));
        int h = Mathf.FloorToInt(w / aspect);
        DisplayServer.WindowSetSize(new Vector2I(w, h));
        // 위치는 창 안쪽(제목 줄 아래) 기준이다. 제목 줄까지 포함한 창 전체가 작업 영역 가운데 오게 놓는다
        // (아래 테두리는 옆 테두리와 같은 굵기로 본다).
        int title = deco.Y - deco.X / 2;
        DisplayServer.WindowSetPosition(usable.Position
            + new Vector2I((usable.Size.X - w) / 2, (usable.Size.Y - h - deco.Y) / 2 + title));
    }

    // 전체화면은 해상도를 늘리는 게 아니라 '화면 전체를 쓰는 창'으로 바꾸는 것.
    // 콘텐츠 비율은 위의 화면 기준(canvas_items / keep · 1920x1080)이 유지해 준다.
    private static void ApplyFullscreen()
    {
        var want = _fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        DisplayServer.WindowSetMode(want);
        // 전체화면에서 창 모드로 돌아오면 예전 창 크기로 돌아온다 — 그 크기가 화면을 넘을 수 있다.
        FitWindowToScreen();

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
        ApplyPointShadowPolicy();

        // CRT 화면들의 렌더 해상도도 같이 바꾼다.
        //
        // 여기까지는 **메인 3D 만** 바뀌었다. CRT 모니터와 CCTV 3D 월드는 각자
        // SubViewport 에 그려지고, 그 크기는 만들어질 때 ControlRoom3DController.RenderScale
        // 로 한 번 정해진 뒤 그대로였다 — 캐시된 static 값이라 무효화되지도 않았다.
        // 그래서 실행 중에 품질을 낮춰도 화면 안의 화면은 그대로 높은 해상도로 그려졌고,
        // 플레이어 입장에서는 "낮음으로 내려도 별로 안 빨라진다" 가 된다.
        NSP.View.ControlRoom3DController.OnQualityChanged();
    }

    // ── 점 광원 그림자 ──────────────────────────────────────────────
    //
    // **왜 점 광원만 따로 보는가.** 스포트라이트의 그림자는 그림자 맵 한 장이지만,
    // 점 광원(OmniLight3D)의 그림자는 **큐브맵 — 여섯 면**이다. 장면을 여섯 번 더
    // 그린다는 뜻이다. 실측에서 켜고 끄는 것만으로 draw call 이 799 → 461 로
    // 338 개 줄었고 프레임이 2.1 ms 빨라졌다(docs/perf/probe_paired.md ⑧).
    // 이 방에서 그림자를 켠 광원 열셋 중 열하나가 점 광원이다.
    //
    // **높음 · 보통은 건드리지 않는다.** 조명과 공포 분위기를 그대로 두라는 요구가
    // 있고, 실제로 천장 광원의 그림자가 빠지면 책상 위 기기들이 바닥에서 뜬 것처럼
    // 보인다. 끄는 것은 "매우 낮음" 뿐이다.
    //
    // 원래 그림자를 켜고 있던 광원만 기억해 두었다가 되돌린다 — 그렇지 않으면
    // 품질을 올렸을 때 **원래 꺼져 있던** 광원까지 그림자를 켜 장면이 달라진다.
    private static readonly System.Collections.Generic.HashSet<ulong> _pointShadowWasOn = new();

    public static void ApplyPointShadowPolicy()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return;
        Walk(tree.Root, QualityLevels[(int)_quality].PointShadows);

        static void Walk(Node n, bool on)
        {
            if (n is OmniLight3D l)
            {
                ulong id = l.GetInstanceId();
                if (!on)
                {
                    if (l.ShadowEnabled) { _pointShadowWasOn.Add(id); l.ShadowEnabled = false; }
                }
                else if (_pointShadowWasOn.Contains(id)) l.ShadowEnabled = true;
            }
            foreach (var c in n.GetChildren()) Walk(c, on);
        }
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

        // 접촉 그림자(SSAO) — 기기가 책상 · 서로 맞닿는 곳만 진하게 어두워진다. 높음 · 보통에서만 켠다.
        // 씬마다 원래 켰는지를 Environment 자체에 적어 두고, 품질을 다시 올리면 그대로 되살린다.
        if (!env.HasMeta("nsp_wants_ssao")) env.SetMeta("nsp_wants_ssao", env.SsaoEnabled);
        env.SsaoEnabled = (bool)env.GetMeta("nsp_wants_ssao")
            && (_quality == Quality.High || _quality == Quality.Medium);

        _sceneWantsGlow ??= env.GlowEnabled;
        env.GlowEnabled = _sceneWantsGlow.Value && QualityLevels[(int)_quality].Glow;
        if (!env.GlowEnabled) return;

        // 숫자가 클수록 넓고 흐리게 퍼지며 그만큼 비싸다.
        //
        // **인덱스는 0 부터다.** 인스펙터가 "Glow Levels 1~7" 로 보여 주는 것과 달리
        // SetGlowLevel 이 받는 번호는 0~6 이고, 7 을 넣으면 매 씬 전환마다
        // "p_level = 7 is out of bounds" 가 찍힌다. 아래 번호는 지금 화면에 실제로
        // 걸려 있던 단계 그대로 두고, 범위 밖이라 애초에 먹지 않던 한 줄만 걷어냈다
        // — 보정해 둔 색감이 바뀌면 안 되기 때문이다.
        bool wide = _quality == Quality.High;
        env.SetGlowLevel(1, 1f);
        env.SetGlowLevel(2, 1f);
        env.SetGlowLevel(3, 1f);
        env.SetGlowLevel(4, 0.85f);
        env.SetGlowLevel(5, wide ? 0.55f : 0f);
        env.SetGlowLevel(6, 0f);
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
            _reduceFlashing = (bool)cfg.GetValue("video", "reduce_flashing", _reduceFlashing);
            _quality = (Quality)Mathf.Clamp((int)cfg.GetValue("video", "quality", (int)_quality), 0, QualityLevels.Length - 1);
            for (int i = 0; i < Keys.Length; i++)
                Keys[i] = (Key)(int)cfg.GetValue("keys", ((ZoomTarget)i).ToString(), (int)DefaultKeys[i]);
        }
        else if (DisplayServer.GetName() != "headless")
        {
            // 처음 켰다 — 설정 파일이 없다. 그래픽 품질을 그래픽 장치에 맞춰 고르고 바로 저장한다
            // (2026-10-31). 내장 그래픽(노트북 · 사무용 PC)에서 '높음' 으로 시작하면 처음 몇 분이 끊긴다.
            // 플레이어는 환경 설정에서 언제든 바꿀 수 있다.
            _quality = AutoQuality();
            Save();
        }

        ApplyAudio();
        ApplyDesignResolution();
        ApplyFullscreen();
        ApplyQuality();
    }

    // 그래픽 장치 종류로 고른 첫 품질 — 외장 = 높음, 내장 = 낮음, 소프트웨어 · 가상 = 매우 낮음, 모르면 보통.
    private static Quality AutoQuality()
    {
        var type = RenderingServer.GetVideoAdapterType();
        Quality q = type switch
        {
            RenderingDevice.DeviceType.DiscreteGpu => Quality.High,
            RenderingDevice.DeviceType.IntegratedGpu => Quality.Low,
            RenderingDevice.DeviceType.Cpu or RenderingDevice.DeviceType.VirtualGpu => Quality.VeryLow,
            _ => Quality.Medium,
        };
        GD.Print($"[설정] 첫 실행: 그래픽 장치 '{RenderingServer.GetVideoAdapterName()}' ({type}) → 품질 {q}");
        return q;
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", _master);
        cfg.SetValue("audio", "bgm", _bgm);
        cfg.SetValue("audio", "sfx", _sfx);
        cfg.SetValue("video", "fullscreen", _fullscreen);
        cfg.SetValue("video", "reduce_flashing", _reduceFlashing);
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
