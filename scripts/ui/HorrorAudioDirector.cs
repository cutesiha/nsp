using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

// 공포 사운드 패스(지시서 PART B).
//
// 화면에 무언가를 더 보여주지 않는다. 새 귀신 모델도, 얼굴도, 글리치도 없다 — 그건
// 이미 HorrorDirector 가 한다. 여기서 만드는 것은 하나뿐이다:
//
//   **아무것도 없는데 내 옆이나 뒤에서 무언가 들린 것 같은 기분.**
//
// 지키는 규칙 넷.
//   ① 게임 정보음이 아니다(§13). 전화벨 · 경고음 · 승인음과 섞이지 않게 전용 버스
//      (GameSettings.BusHorror)로만 나가고, 그 소리들이 울리는 동안에는 입을 닫는다.
//      플레이어가 "게임 정보를 놓쳤다" 고 착각하면 공포가 아니라 UX 오류다.
//   ② 규칙적이면 실패다(§12). 쿨다운이 길고 매번 흔들리며, 하루 횟수에 상한이 있고
//      같은 소리를 연속으로 쓰지 않는다.
//   ③ 좌우가 들려야 한다(§14). 전부 모노 파일이고 패닝은 버스에서 준다.
//   ④ 스토리 중에는 울리지 않는다(§20). 휴게시간도 빈도를 낮춘다(§21).
public partial class HorrorAudioDirector : Node
{
    public static HorrorAudioDirector Instance { get; private set; }

    // ── 소리 묶음 ─────────────────────────────────────────────────────

    // 공포음 한 종류. pan 은 -1(왼쪽) ~ +1(오른쪽).
    private sealed class Cue
    {
        public string Key = "";
        public string Group = "";       // 같은 묶음은 연달아 뽑지 않는다
        public float Db;
        public float PanMin, PanMax;
        public float PitchMin = 0.95f, PitchMax = 1.05f;
        public int FromDay = 1;         // 이 DAY 부터 나온다
        public float Weight = 1f;
        public bool Close;              // 가까운 소리 — 가장 무서운 카드(§11 D)
    }

    // 전부 아주 작다. 가장 큰 것(문 노크)도 -20dB 다 — "뭐였지?" 정도여야 한다(§11 B).
    private static readonly Cue[] Cues =
    {
        // A. 좌우 속삭임 — 뜻이 들리지 않는다. 게임 정보를 말하지 않는다(§11 A).
        new() { Key = "whisper_short", Group = "whisper", Db = -26f, PanMin = 0.62f, PanMax = 0.90f, Weight = 1.2f },
        new() { Key = "whisper_short", Group = "whisper", Db = -26f, PanMin = -0.90f, PanMax = -0.62f, Weight = 1.2f },
        new() { Key = "whisper_long", Group = "whisper", Db = -27f, PanMin = -0.88f, PanMax = -0.58f, FromDay = 2 },
        new() { Key = "whisper_reverse", Group = "whisper", Db = -27f, PanMin = -0.85f, PanMax = -0.55f, FromDay = 3,
                PitchMin = 0.88f, PitchMax = 0.98f },
        // 부르는 억양만 남은 속삭임. DAY3 부터, 오른쪽에서만 — 왼쪽 오른쪽 다 쓰면 흔해진다.
        new() { Key = "whisper_call", Group = "whisper", Db = -25f, PanMin = 0.55f, PanMax = 0.85f, FromDay = 3,
                Weight = 0.8f },

        // B. 뒤쪽 · 문 방향 — 중앙에 가깝게 두고 멀게 들린다(§11 B).
        new() { Key = "door_knock_soft", Group = "door", Db = -20f, PanMin = -0.18f, PanMax = 0.18f,
                PitchMin = 0.92f, PitchMax = 1.0f },
        new() { Key = "door_scratch", Group = "door", Db = -24f, PanMin = -0.3f, PanMax = 0.3f },
        new() { Key = "door_handle", Group = "door", Db = -24f, PanMin = -0.22f, PanMax = 0.22f, FromDay = 2 },

        // C. 환풍구 · 천장 — 좌우가 바뀐다(§11 C).
        new() { Key = "duct_crawl", Group = "duct", Db = -24f, PanMin = -0.55f, PanMax = -0.25f, Weight = 1.3f },
        new() { Key = "duct_crawl", Group = "duct", Db = -24f, PanMin = 0.25f, PanMax = 0.55f, Weight = 1.3f },
        new() { Key = "duct_scrape", Group = "duct", Db = -26f, PanMin = -0.5f, PanMax = 0.5f, Weight = 1.1f },
        new() { Key = "pipe_knock", Group = "duct", Db = -25f, PanMin = -0.45f, PanMax = 0.45f,
                PitchMin = 0.8f, PitchMax = 0.95f },

        // D. 아주 가까운 소리 — 빈도를 매우 낮게. 남발하면 가장 먼저 지겨워진다(§11 D · §19).
        new() { Key = "breath_close", Group = "close", Db = -23f, PanMin = 0.72f, PanMax = 0.92f,
                FromDay = 3, Weight = 0.5f, Close = true },
        new() { Key = "cloth_rustle", Group = "close", Db = -25f, PanMin = -0.88f, PanMax = -0.7f,
                FromDay = 3, Weight = 0.45f, Close = true },
        new() { Key = "whisper_short", Group = "close", Db = -22f, PanMin = -0.93f, PanMax = -0.82f,
                FromDay = 4, Weight = 0.35f, Close = true, PitchMin = 1.0f, PitchMax = 1.1f },
    };

    // ── 하루 횟수 (§12) ──────────────────────────────────────────────
    //
    // DAY1 은 초보자가 처음 혼자 앉는 날이다 — 0~1회, 가까운 소리는 아예 없다(§19).
    private static readonly (int Min, int Max)[] PerDay =
    {
        (0, 1),   // DAY1
        (0, 2),   // DAY2
        (1, 2),   // DAY3
        (1, 3),   // DAY4
        (1, 3),   // DAY5
    };

    // 쿨다운. 짧으면 "30초마다 공포음" 이 되어 규칙이 들킨다(§12).
    private const float CooldownMinSeconds = 34f;
    private const float CooldownMaxSeconds = 72f;
    // 근무 시작 직후에는 울리지 않는다 — 배치를 막 끝낸 플레이어가 조작을 익히는 구간이다.
    private const float FirstEarliestSeconds = 26f;
    // 휴게시간은 추리 구간이다 — 빈도를 반으로 줄인다(§21).
    private const float RestCooldownScale = 2.0f;

    private readonly RandomNumberGenerator _rng = new();
    private float _nextAt = -1f;
    private int _playedToday;
    private int _quotaToday;
    private string _lastGroup = "";
    private string _lastKey = "";
    private GamePhase _lastPhase = GamePhase.Prep;
    private int _lastDay = -1;

    // 검사가 읽는 값. 게임 판정에는 쓰지 않는다.
    public int PlayedToday => _playedToday;
    public int QuotaToday => _quotaToday;
    public string LastKey => _lastKey;

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 새 근무가 시작될 때마다 하루 몫을 다시 뽑는다.
    private void BeginDay(int day)
    {
        _lastDay = day;
        _playedToday = 0;
        _lastGroup = "";
        _lastKey = "";
        var (lo, hi) = PerDay[Mathf.Clamp(day - 1, 0, PerDay.Length - 1)];
        _quotaToday = _rng.RandiRange(lo, hi);
        // 첫 건의 시각도 매번 흔들린다. 같은 초에 울리면 두 판만 돌려도 들킨다.
        _nextAt = FirstEarliestSeconds + _rng.RandfRange(0f, 30f);
    }

    // ── 울려도 되는 상황인가 ──────────────────────────────────────────
    //
    // 하나라도 걸리면 그 차례를 **건너뛰지 않고 미룬다** — 조건이 풀릴 때 울린다.
    // 건너뛰면 조용한 판에서는 하루 몫이 그대로 남아 아무 일도 일어나지 않는다.
    public string BlockedReason()
    {
        // ① DAY 스토리 중에는 울리지 않는다(§20). 대사를 듣다가 whisper 가 끼면 분위기가 깨진다.
        if (NSP.View.StoryTransition.Active) return "스토리 전환 중";
        if (NSP.View.StoryCutinDirector.Instance is { IsPlaying: true }) return "스토리 중";

        // ② 게임 정보음과 겹치지 않는다(§13). 전화벨 · 경고 · 승인 요청이 울리는 동안은 입을 닫는다.
        if (NSP.View.Phone3D.Instance is { IsBusy: true }) return "통화 중";
        if (NSP.View.PhoneCallHud.Instance is { IsOpen: true }) return "통화창 열림";
        if (RepairApprovalSystem.Busy) return "수리 승인 절차 중";

        var sim = FacilitySimulation.Instance;
        if (sim == null) return "시뮬레이션 없음";
        if (sim.Warnings.Active.Count > 0) return "경고 중";
        if (IncidentTracker.ActiveCount > 0) return "사고 중";

        // ③ 괴물이 떠 있는 동안에는 울리지 않는다 — 괴물 소리와 헷갈리면 게임 신호가 흐려진다(§13).
        if (sim.Ghost is { Active: true }) return "괴물 등장 중";

        // ④ 다른 공포 연출이 화면을 잡고 있으면 비킨다.
        if (HorrorDirector.Instance is { CustomEventActive: true }) return "전용 공포 연출 중";
        if (Sfx.Instance is { HorrorPlaying: true }) return "앞 공포음이 아직 울리는 중";

        return "";
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        var phase = gs.CurrentPhase;

        // 근무 · 휴게시간에만 돈다. 배치 · 정산 · 타이틀에서는 아예 쉰다.
        bool live = phase == GamePhase.Live;
        bool rest = phase == GamePhase.Rest;
        if (!live && !rest)
        {
            _lastPhase = phase;
            return;
        }

        // 교육일(DAY0)에는 울리지 않는다 — 배우는 중에 놀래키지 않는다.
        if (!DayFeatures.AutoIncidentsEnabled) { _lastPhase = phase; return; }

        int day = gs.CurrentDay;
        if (day != _lastDay || (live && _lastPhase != GamePhase.Live)) BeginDay(day);
        _lastPhase = phase;

        if (_playedToday >= _quotaToday) return;

        float now = gs.DayTimeSeconds;
        if (now < _nextAt) return;
        if (BlockedReason().Length > 0) return;

        Play(day, rest);
    }

    // 한 건 울린다.
    private void Play(int day, bool rest)
    {
        var cue = Pick(day, rest);
        if (cue == null) return;

        float pan = _rng.RandfRange(cue.PanMin, cue.PanMax);
        float pitch = _rng.RandfRange(cue.PitchMin, cue.PitchMax);
        // 휴게시간은 추리 구간이라 더 작게 깐다(§21).
        float db = cue.Db - (rest ? 3f : 0f);

        // 침묵을 먼저 만든다(§18). 시설 상시음만 아주 잠깐 비워 — 전체 Audio 를 mute 하지 않는다.
        // 그 빈 자리에 소리가 들어가야 "방금 뭐였지" 가 된다.
        var atmos = NSP.View.ControlRoomAtmosphere.Instance;
        if (atmos != null && cue.Close) atmos.Hush(1.2f);

        // 가까운 소리와 노크만 BGM 을 아주 잠깐 누른다(§17).
        // 작은 소리마다 누르면 BGM 이 출렁여서 그게 더 거슬린다.
        if (cue.Close) Sfx.Instance?.DuckMusic(-4f, 0.7f);
        else if (cue.Group == "door") Sfx.Instance?.DuckMusic(-2.5f, 0.5f);

        Sfx.Instance?.PlayHorror(cue.Key, db, pan, pitch);

        _playedToday++;
        _lastGroup = cue.Group;
        _lastKey = cue.Key;

        float scale = rest ? RestCooldownScale : 1f;
        _nextAt = (GameState.Instance?.DayTimeSeconds ?? 0f)
                  + _rng.RandfRange(CooldownMinSeconds, CooldownMaxSeconds) * scale;
    }

    // 오늘 쓸 수 있는 소리 중 하나. 같은 묶음 · 같은 파일을 연달아 쓰지 않는다(§12).
    private Cue Pick(int day, bool rest)
    {
        var pool = new List<Cue>();
        float total = 0f;
        foreach (var c in Cues)
        {
            if (c.FromDay > day) continue;
            // 휴게시간에는 가까운 소리를 쓰지 않는다 — 로그를 읽는 중에 귀 옆에서 숨소리가
            // 나면 추리가 아니라 방해가 된다(§21).
            if (rest && c.Close) continue;
            if (c.Group == _lastGroup) continue;
            if (c.Key == _lastKey) continue;
            pool.Add(c);
            total += c.Weight;
        }
        // 전부 걸러졌다면(소리 종류가 적은 DAY1) 묶음 제한만 풀고 다시 고른다.
        if (pool.Count == 0)
        {
            foreach (var c in Cues)
            {
                if (c.FromDay > day || (rest && c.Close) || c.Key == _lastKey) continue;
                pool.Add(c);
                total += c.Weight;
            }
        }
        if (pool.Count == 0) return null;

        float roll = _rng.RandfRange(0f, total);
        foreach (var c in pool)
        {
            roll -= c.Weight;
            if (roll <= 0f) return c;
        }
        return pool[^1];
    }

    // ── 디버그 전용 ───────────────────────────────────────────────────

    // 조건 · 쿨다운 · 하루 몫을 모두 무시하고 그 소리만 울린다(AudioProbe 가 쓴다).
    public void DebugPlay(string key, float pan, float db = -20f)
        => Sfx.Instance?.PlayHorror(key, db, pan);

    // 오늘 그 DAY 에 쓸 수 있는 소리 키 목록(검사용).
    public static List<string> KeysForDay(int day, bool rest)
    {
        var list = new List<string>();
        foreach (var c in Cues)
        {
            if (c.FromDay > day || (rest && c.Close)) continue;
            if (!list.Contains(c.Key)) list.Add(c.Key);
        }
        return list;
    }

    // 하루 횟수 범위(검사용).
    public static (int Min, int Max) QuotaRange(int day) =>
        PerDay[Mathf.Clamp(day - 1, 0, PerDay.Length - 1)];

    // 이 소리는 "가까운 소리" 인가(검사용 — DAY1·2 에 나오면 안 된다).
    public static bool IsCloseCue(string key, int day)
    {
        foreach (var c in Cues)
            if (c.Key == key && c.Close && c.FromDay <= day) return true;
        return false;
    }

    // 공포음이 쓰는 키 전체. 게임 정보음과 한 글자도 겹쳐서는 안 된다(§13 검사).
    public static List<string> AllKeys()
    {
        var list = new List<string>();
        foreach (var c in Cues) if (!list.Contains(c.Key)) list.Add(c.Key);
        return list;
    }
}
