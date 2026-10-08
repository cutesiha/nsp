using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// ── 사이렌 직후의 지상 ───────────────────────────────────────────────────
//
// 지하 시설 안만 보여 주면 "여기만 사고가 났구나" 로 읽힌다. 사이렌이 울린 직후 한 번,
// 밖을 보여 준다 — 그리고 밖은 이미 끝나 있다.
//
// 세 컷으로 끊는다(전부 합쳐 11초 안팎) :
//   ① 소리부터  — 거의 암전인 채로 붕괴음 · 바람 · 먼 폭발이 먼저 온다
//   ② 와이드    — 붉은 하늘 아래 무너진 스카이라인을 천천히 훑는다. 한 동이 실제로 무너진다
//   ③ 도로 · 끝 — 잔해가 쌓인 길을 지나 먼지 속으로 시야가 흐려지며 끊긴다
//
// 음악은 쓰지 않는다. 환경음과 효과음만으로 "재난 현장" 이 되어야 한다.
public partial class Prologue3DDirector
{
    private void SurfaceSetup()
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Surface);
    }

    // 바깥의 기본 소리 — 들어갈 때 한 번 깔고 나갈 때 전부 내린다.
    private void SurfaceAmbience()
    {
        Sfx.Instance?.Loop("vent_loop", -9f);              // 거센 바람
        Sfx.Instance?.Loop("drone_loop", -15f);            // 먼 곳의 낮은 울림
        Sfx.Instance?.Loop("electric_crackle_loop", -22f); // 불타는 소리
        Sfx.Instance?.Loop("siren", -15f);                 // 사이렌의 잔향 — 멀리
    }

    public void SurfaceAmbienceStop()
    {
        foreach (var k in new[] { "vent_loop", "drone_loop", "electric_crackle_loop" })
            Sfx.Instance?.StopLoop(k);
    }

    // ── ① 소리부터 · 와이드 ─────────────────────────────────────────────
    private async Task SurfaceWide(int gen)
    {
        SurfaceSetup();
        _stage.SurfaceCollapse(0f);
        SurfaceAmbience();

        // 소리가 먼저 오되 **화면을 끄지는 않는다.** 앞서 잠깐 암전되면 재생이 끊긴
        // 것처럼 보인다 — 기록영상은 끊기지 않고 계속 돌아가야 한다.
        Sfx.Instance?.Play("rubble_collapse", -3f, 0.72f);
        Sfx.Instance?.Play("metal_clang", -7f, 0.55f);
        _stage.SetCam(new Vector3(-16f, 7.5f, 26f), new Vector3(6f, 9f, -40f), 62f);
        await Wait(0.75);
        if (!Alive(gen)) return;

        Sfx.Instance?.Play("boom", -9f, 0.48f);            // 먼 폭발
        Shake(0.9f, 0.7f);

        // 부유하듯 아주 천천히 훑는다 — 고정하면 정지 그림처럼 보인다.
        _ = _stage.MoveCamCut(new Vector3(14f, 9.2f, 22f), new Vector3(-10f, 7f, -52f), 6.2, 58f);

        await Wait(1.5);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("metal_clang", -12f, 0.7f);

        // 멀리서 건물 한 동이 실제로 무너진다.
        await Wait(0.6);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("rubble_collapse", -6f, 0.85f);
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(k => _stage.SurfaceCollapse(k)), 0f, 1f, 2.3)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await Wait(1.6);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("impact_blunt", -5f, 0.6f);
        Shake(2.2f, 1.1f);
        Sfx.Instance?.Play("boom", -13f, 0.4f);
    }

    // ── ② 무너진 도로 ───────────────────────────────────────────────────
    private async Task SurfaceRoad(int gen)
    {
        SurfaceSetup();
        SurfaceAmbience();
        // 사람 눈높이로 내려온다 — 스케일이 바뀌면서 "여기 서 있었다면" 이 된다.
        _stage.SetCam(new Vector3(-2.2f, 1.7f, 9.5f), new Vector3(3f, 2.2f, -16f), 54f);
        _ = _stage.MoveCamCut(new Vector3(1.6f, 1.55f, 3.2f), new Vector3(6f, 2.6f, -20f), 3.6);

        Sfx.Instance?.Play("steam_hiss", -11f, 0.8f);
        await Wait(0.8);
        if (!Alive(gen)) return;

        // 구조물 틈에서 스파크가 튄다.
        var root = _stage.SurfaceRoot;
        if (root != null)
        {
            var sp = _stage.Sparks(root, new Vector3(4.2f, 1.1f, -9f), 1.5f, 60);
            sp.Emitting = true;
        }
        Sfx.Instance?.Play("electric_arc", -6f);
        await Wait(0.9);
        if (!Alive(gen)) return;

        Sfx.Instance?.Play("metal_clang", -9f, 0.52f);     // 철골이 휘는 소리
        Shake(0.7f, 1.0f);
        await Wait(1.0);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("rubble_collapse", -14f, 1.1f);
    }

    // ── ③ 끝 ────────────────────────────────────────────────────────────
    private async Task SurfaceLast(int gen)
    {
        SurfaceSetup();
        SurfaceAmbience();
        // 폐허를 바라보다 먼지 속으로 들어간다.
        _stage.SetCam(new Vector3(3.4f, 2.0f, 2.0f), new Vector3(-6f, 4.5f, -34f), 50f);
        _ = _stage.MoveCamCut(new Vector3(2.0f, 2.4f, -3.5f), new Vector3(-9f, 5.5f, -40f), 3.4, 44f);

        var root = _stage.SurfaceRoot;
        if (root != null)
        {
            // 카메라 바로 앞으로 재가 몰려와 시야를 덮는다.
            _stage.SurfaceDust(new Vector3(1.4f, 2.3f, -3.0f));
        }

        await Wait(1.3);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("boom", -16f, 0.42f);
        await Wait(0.9);
        if (!Alive(gen)) return;

        // 신호가 끊긴다 — 단, 화면을 끄지는 않는다. 암전하면 영상이 멈춘 것처럼 보인다.
        // 대신 기록영상의 잡음 · 흔들림을 확 키워 "회선이 죽는 중" 으로 보이게 한다.
        Sfx.Instance?.Play("noise", -6f, 0.9f);
        CutscenePlayer.Instance?.FilmSurge(0.95f);
        await Wait(0.9);
        if (!Alive(gen)) return;
        SurfaceAmbienceStop();
        Sfx.Instance?.Play("radio_cut", -8f);
    }
}
