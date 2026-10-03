using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// LOOSE — 코어 100% + 지목 실패. 이 게임에서 사람들이 얘기하고 다닐 엔딩이다.
//
// 원칙 하나로 전부 설명된다: **복구 완료 구간이 TRUE 와 한 프레임도 달라서는 안 된다.**
// 그래서 그 구간은 RecoveryComplete() 한 메서드를 공유하고, 다른 것은 SYSTEM STATUS 의
// 마지막 한 줄뿐이다("격리 대상 ... 미확인"). 플레이어는 이겼다고 믿은 채로 끝까지 간다.
//
// 그리고 명단 여섯 장이 전부 "확인" 으로 끝난 뒤, 완전한 무음 속에서 한 장이 깨진다.
// **복구되지 않는다.** 타이틀에서 늘 보던 0.1초짜리 글리치와 다른 점은 그것 하나뿐이다.
public partial class EndingDirector
{
    private async Task LooseEnd()
    {
        var gs = GameState.Instance;
        string accused = gs?.FinalAccusedId ?? "";

        // ① 조회 — 여기서 **아무 강조도 하지 않는다.** 평범한 확인음 하나로 지나간다.
        PadOpen("신원 조회 중");
        for (int i = 0; i < 3; i++) { Sfx.Instance?.Play("key_type", -18f); await Wait(0.18); }
        if (string.IsNullOrEmpty(accused))
        {
            PadPush("▸ 지정 없음 · 격리 미집행", EndingMonitorView.Tone.Normal);
            await PadWait();
        }
        else
        {
            PadPush("▸ 인사 기록 확인", EndingMonitorView.Tone.Dim);
            await PadWait(0.8);
            PadPush("▸ 정상 등록 인원입니다", EndingMonitorView.Tone.Good);
            await PadWait(0.3);
            PadPush("▸ 격리 이송 완료", EndingMonitorView.Tone.Normal);
            await PadWait();
        }
        AdminPad3D.Instance?.FlashBadgeLed(PadGreen, 2.4f, 0.8f, 0.6f);
        Sfx.Instance?.Play("sensor_beep", -16f);
        await Wait(1.4);

        // ② 복구 완료 — TRUE 와 같은 코드. 다른 것은 이 한 줄뿐이다.
        await RecoveryComplete("격리 대상 ... 미확인", EndingMonitorView.Tone.Warn);

        // ③ GUIDE-0 — 첫 균열. 세 번째 줄을 앞 두 줄과 똑같은 톤 · 속도로 말한다.
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        await Wait(1.6);
        left?.Clear();
        _ctl?.SetScreenNoise(0.06f);
        Sfx.Instance?.Play("window_open", -10f);
        ShowGuide("smile");
        await Wait(0.25);
        _ctl?.SetScreenNoise(0.012f);
        await Wait(0.9);

        right?.Clear();
        await GuideSpeak(right,
            "야간 근무가 종료되었습니다.",
            "관리자님, 수고하셨습니다.",
            "근무 인원 명단을 정리하겠습니다.");
        await Wait(1.4);

        // ④ 신원 카드 여섯 장 — 타이틀 화면의 그 명단이 왼쪽 CRT 에 뜬다.
        // GUIDE-0 은 사라지지 않는다. 오른쪽 CRT 로 옮겨 **계속 웃고 있다.**
        // 그 화면의 글자는 지운다 — 얼굴창 뒤로 글자가 비치면 지저분하다.
        right?.Clear();
        PlaceGuide(onRight: true);
        var cards = EndingStaffIdView.Instance;
        _ctl?.SetLeftScreen(_ctl.EndingStaffViewport);
        cards?.Present(accused);
        cards?.RevealAll();
        await Wait(0.8);

        for (int i = 0; i < (cards?.Count ?? 0); i++)
        {
            cards.Verify(i);
            Sfx.Instance?.Play("relay_click", -15f);
            await Wait(0.45);
        }
        cards?.SetFooter("ID STATUS : NORMAL");
        await Wait(0.9);

        // ⑤ 2초 정적 — 이 게임 최고의 순간.
        //    모든 소리를 끈다. 조명은 여전히 밝고 따뜻하다. 바꾸지 마라.
        foreach (var k in new[] { "machinery_loop", "drone_loop", "vent_loop", "alarm", "siren" })
            Sfx.Instance?.StopLoop(k);
        await Wait(2.0);

        // 남은 카드 중 한 장이 깨진다 — 실제 결번 개체. 복구되지 않는다.
        string real = gs?.SaboteurEmployeeId ?? "";
        var sim = FacilitySimulation.Instance;
        bool realAvailable = !string.IsNullOrEmpty(real) && real != accused
                             && (sim?.GetEmployeeState(real)?.Alive ?? true);
        cards?.Break(realAvailable ? real : "", 1.5f);
        cards?.SetFooter("");

        // 카메라가 그 카드 쪽으로 **아주 미세하게** 밀린다(확대가 아니다 — 시선만 옮긴다).
        var m1 = _ctl?.GetNodeOrNull<Node3D>("ControlRoom/Monitor01/M01_Screen");
        if (m1 != null) _ctl?.GazeAt(m1.GlobalPosition, 2.0f);
        await Wait(3.6);

        // ⑥ 끝 — 이명. 프롤로그에서 머리를 맞고 깨어날 때 들었던 그 소리다.
        _ctl?.LeanBackInChair(3.4f);
        Sfx.Instance?.Play("chair_creak", -16f);
        await Wait(1.2);

        Sfx.Instance?.Loop("tinnitus", -24f);
        var ring = CreateTween();
        ring.TweenMethod(Callable.From<float>(v => Sfx.Instance?.SetLoopVolume("tinnitus", v)), -24f, -6f, 2.0);

        // 암전이 아니라 하얗게 번진다. 조명도 같이 과노출된다.
        var over = CreateTween().SetParallel(true);
        over.TweenProperty(_white, "color:a", 1f, 2.5);
        if (_ceiling != null) over.TweenProperty(_ceiling, "light_energy", _ceilE * 1.6f, 2.5);
        await Wait(2.6);
        await Wait(1.5);
        Sfx.Instance?.StopLoop("tinnitus");

        await Banner("BAD END — 복구 완료 · 대상 미확인",
            new Color(0.92f, 0.92f, 0.88f), new Color(0.12f, 0.12f, 0.14f));
        EndingState.Record(EndingState.Kind.Loose);
        // 성적표는 흰 화면이 아니라 어둠 위에 뜬다.
        _black.Color = _black.Color with { A = 1f };
        _white.Color = _white.Color with { A = 0f };
    }
}
