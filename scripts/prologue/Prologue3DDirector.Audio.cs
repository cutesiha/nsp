using System.Collections.Generic;
using Godot;
using NSP.Core;

namespace NSP.Prologue;

// 프롤로그 3D 컷씬의 소리 관리.
//
// ── 왜 따로 있나 ─────────────────────────────────────────────────────────
// 컷씬 연출기는 장면마다 사이렌 · 경보 · 기계음 루프를 켠다. 그런데 루프는
// **켠 쪽이 꺼야 한다**. 예전에는 그 책임이 흩어져 있어서, 마지막 장면
// (director_last)이 켠 경보 루프가 프롤로그가 끝난 뒤에도 계속 울렸다 —
// 게임을 시작해도 경보음 하나가 끝없이 깔려 있었다.
//
// 그래서 이 파일에 등록소를 둔다.
//   SceneLoop  장면이 켠 루프를 적어 둔다 → StopSceneLoops 한 번에 전부 끈다
//   CutWarp    컷 하나짜리 변형음(비명 · 유리)을 적어 둔다 → 다음 컷에서 사라진다
// 컷씬 데이터(`sfxloop:`)가 이미 쥐고 있는 루프는 등록하지 않는다. 그건
// CutscenePlayer 의 몫이고, 둘이 같이 끄면 사이렌이 중간에 끊긴다.
public partial class Prologue3DDirector
{
    // ── 장면이 켠 반복음 ────────────────────────────────────────────────
    private readonly HashSet<string> _sceneLoops = new();

    // 장면용 루프. **이 연출기가 직접 켠 것만** 등록한다.
    private void SceneLoop(string key, float volumeDb)
    {
        var sfx = Sfx.Instance;
        if (sfx == null) return;
        // 이미 돌고 있다면 컷씬 데이터가 켠 것이다(사이렌). 소유권을 빼앗지 않는다.
        if (!sfx.IsLooping(key)) _sceneLoops.Add(key);
        sfx.Loop(key, volumeDb);
    }

    private void StopSceneLoop(string key)
    {
        if (!_sceneLoops.Remove(key)) return;
        Sfx.Instance?.StopLoop(key);
    }

    private void StopSceneLoops()
    {
        foreach (string key in _sceneLoops) Sfx.Instance?.StopLoop(key);
        _sceneLoops.Clear();
    }

    // 소유권을 따지지 않고 통째로 끈다. 폭발 직전의 '정적' 한 박자에만 쓴다 —
    // 거기서는 컷씬이 켠 사이렌까지 **같이** 꺼져야 그 침묵이 성립한다.
    private void SilenceLoops(params string[] keys)
    {
        foreach (string key in keys)
        {
            _sceneLoops.Remove(key);
            Sfx.Instance?.StopLoop(key);
        }
    }

    // ── 컷 하나짜리 변형음 ──────────────────────────────────────────────
    //
    // 받아 온 음원을 Warp 버스로 일그러뜨려 내보낸다(Sfx.PlayWarped). 컷이 바뀌면
    // 짧게 줄여 끈다 — 플레이어가 컷을 빨리 넘겼을 때 비명이 다음 장면까지
    // 따라 들어오지 않게.
    private readonly HashSet<string> _cutWarps = new();

    private void CutWarp(string key, float volumeDb, float pitch,
        string channel = null, bool loop = false, float delaySeconds = 0f)
    {
        var sfx = Sfx.Instance;
        if (sfx == null) return;
        _cutWarps.Add(string.IsNullOrEmpty(channel) ? key : channel);
        sfx.PlayWarped(key, volumeDb, pitch, channel, loop, delaySeconds);
    }

    private const float CutWarpFade = 0.45f;

    private void StopCutWarps()
    {
        foreach (string ch in _cutWarps) Sfx.Instance?.StopWarped(ch, CutWarpFade);
        _cutWarps.Clear();
    }

    // ── 대재난의 폭풍우 ────────────────────────────────────────────────
    //
    // 대재난이 시작되는 순간부터 비상 차폐가 내려갈 때까지 한 번도 끊기지 않고
    // 깔린다. 컷이 바뀌어도 이어져야 하므로 컷 등록소에 넣지 않고 따로 관리한다.
    // 두 겹 — 들리는 폭풍우 한 겹, 그 밑에 반 옥타브 낮춘 지하의 울림 한 겹.
    private const string StormCh = "storm";
    private const string StormLowCh = "storm_low";

    private void StormBegin()
    {
        var sfx = Sfx.Instance;
        if (sfx == null || sfx.WarpedPlaying(StormCh)) return;
        sfx.PlayWarped("storm", StormDb, 0.82f, StormCh, loop: true);
        sfx.PlayWarped("storm", StormLowDb, 0.50f, StormLowCh, loop: true);
    }

    // 대사 · 무전이 그 위에서 들려야 한다. 깔려 있는 줄 알겠지만 말을 덮지는 않는 크기.
    private const float StormDb = -17f;
    private const float StormLowDb = -24f;

    private void StormEnd(float fade = 1.6f)
    {
        Sfx.Instance?.StopWarped(StormCh, fade);
        Sfx.Instance?.StopWarped(StormLowCh, fade);
    }

    // 폭발 직전의 침묵 — 폭풍우를 **끊지 않고** 재운다. 끊어 버리면 비상 차폐까지
    // 이어져야 하는 소리가 중간에 사라지고, 다시 켤 때 처음부터 다시 돈다.
    private void StormSilence(float seconds = 0.3f)
    {
        Sfx.Instance?.FadeWarped(StormCh, -52f, seconds);
        Sfx.Instance?.FadeWarped(StormLowCh, -52f, seconds);
    }

    private void StormResume(float seconds = 1.4f)
    {
        Sfx.Instance?.FadeWarped(StormCh, StormDb, seconds);
        Sfx.Instance?.FadeWarped(StormLowCh, StormLowDb, seconds);
    }

    // ── 유리가 깨지는 소리 ──────────────────────────────────────────────
    //
    // glass_bomb 한 파일을 세 속도로 겹친다. 그대로 틀면 1.6초짜리 "창문 깨짐" 이지만,
    // 느리게 겹쳐 깔면 **거대한 무언가가 길게 갈라지는 소리**가 된다.
    //   huge  : 봉쇄 코어가 갈라지는 순간(세 겹 · 6초)
    //   !huge : 연구실 유리 칸막이(두 겹 · 3초)
    private void GlassBurst(bool huge, float db = 0f)
    {
        CutWarp("glass_bomb", db - (huge ? 2f : 5f), huge ? 0.92f : 1.00f, "glass_crack");
        CutWarp("glass_bomb", db - (huge ? 1f : 8f), huge ? 0.46f : 0.52f, "glass_low",
            delaySeconds: huge ? 0.08f : 0.10f);
        if (!huge) return;
        // 갈라진 뒤 한참 끌리는 꼬리. 이 한 겹이 "길고 기괴하게" 를 만든다.
        CutWarp("glass_bomb", db - 7f, 0.30f, "glass_tail", delaySeconds: 0.55f);
    }

    // ── 직원들의 비명 ──────────────────────────────────────────────────
    //
    // 여성 · 남성 녹음을 **섞어** 쓴다. 한 쪽만 틀면 한 사람이 지르는 소리로 들리고,
    // 원본 그대로면 너무 또렷해서 "녹음" 처럼 들린다 — 속도를 내리고 Warp 버스로
    // 흘려 복도 저편에서 울리는 소리로 만든다. 남성 쪽은 반 박자 늦게 깔린다.
    private void ShoutingMix()
    {
        CutWarp("female_run_shouting", -8f, 0.86f, "shout_f");
        CutWarp("male_run_shouting", -10f, 0.90f, "shout_m", delaySeconds: 0.35f);
    }

    // ── 지상의 재난음 ──────────────────────────────────────────────────
    //
    // 지상 컷 두 개 동안 계속 깔린다. 장면 길이보다 짧을 수 있어 반복으로 건다.
    private const string SurfaceDisasterCh = "surface_disaster";

    private void SurfaceDisasterBegin()
    {
        var sfx = Sfx.Instance;
        if (sfx == null || sfx.WarpedPlaying(SurfaceDisasterCh)) return;
        sfx.PlayWarped("disaster", -9f, 0.78f, SurfaceDisasterCh, loop: true);
    }

    private void SurfaceDisasterEnd() => Sfx.Instance?.StopWarped(SurfaceDisasterCh, 0.9f);

    // ── 차폐문 쿠웅 ────────────────────────────────────────────────────
    //
    // 거대한 금속 차폐문이 바닥에 닿는 소리. 가까이서 닫히면 그 자리에서,
    // 멀리서 잠기면 낮추고 줄여서 — 같은 파일 하나로 거리를 만든다.
    private static void Slam(bool near)
    {
        if (near)
        {
            Sfx.Instance?.Play("bulkhead_slam", -1f);
            return;
        }
        Sfx.Instance?.Play("bulkhead_slam", -11f, 0.82f);
    }
}
