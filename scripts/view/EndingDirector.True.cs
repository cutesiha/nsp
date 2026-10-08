using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Prologue;

namespace NSP.View;

// TRUE — 코어 100% + 지목 정답.
//
// 감정 흐름은 하나로 이어져 있다 —
//   성공 → 섬뜩함 → 압도감 → 시설 재가동 → 안도감 → 여운
//
//   ① 코어가 마지막 몇 %를 채우고 100.0 에 닿는다        (성공)
//   ② 지목한 사번을 조회한다 → **결번**                   (성공의 뒷맛)
//   ③ 처분실 — 묶인 결번자, 몸부림, 그리고 드는 고개      (섬뜩함)
//   ④ 철창 밖으로 빠지는 카메라 → 암전 → 비명
//   ⑤ 거대한 봉쇄 코어가 다시 물린다                      (압도감)
//   ⑥ 시설이 하나씩 되살아난다(격벽 · 복도 · 패널 · 기계)  (재가동)
//   ⑦ 중앙제어실에 처음으로 정상 조명이 들어온다          (안도감)
//   ⑧ 모니터2 — 살아남은 사람들의 마지막 대화
//   ⑨ GUIDE-0 의 마지막 인사 → 여운                        (여운)
//
// ③~⑥ 은 중앙제어실이 아닌 다른 장소다 — EndingCutsceneStage 가 자체 World3D 에 짓고
// 화면 전체에 깐다. ⑦ 에서 그 화면을 걷으면 그 아래 중앙제어실이 그대로 드러난다.
//
// **중앙제어실의 밝은 조명은 ⑦ 이전에 절대 켜지지 않는다**(연출 문서 §18).
public partial class EndingDirector
{
    private EndingCutsceneStage _stage;

    private async Task TrueEnd()
    {
        await CoreFinalCount();
        await IdentityQueryFailed();
        await RestraintRoom();
        await CoreHall();
        await FacilityMontage();
        await ControlRoomRestored();
        await LastRestStory();
        await GuideFarewell();

        await Banner("TRUE END — 근무 종료", new Color(0.72f, 0.96f, 0.88f));
        EndingState.Record(EndingState.Kind.True);
    }

    // ── ① 코어 100% ─────────────────────────────────────────────────────
    //
    // 지목 화면으로 바로 넘기지 않는다. 5일 내내 끌어올린 그 숫자가 100.0 에 닿는 것을
    // 먼저 보여 준다 — 화려한 승리 연출이 아니라, 하던 일이 끝났다는 확인이다(§1).
    private async Task CoreFinalCount()
    {
        var left = EndingMonitorView.Left;
        HookTyping(left);

        // 왼쪽 화면만 켠다. 방은 어두운 채로 둔다 — 조명은 아직이다.
        _ctl?.ClearFocus(0.8f);
        await Wait(0.4);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenBrightnessFor("02", 0f);
        _ctl?.SetScreenNoise(0.05f);
        Sfx.Instance?.Play("crt_on", -8f);
        SetLights(0.55f, 0.25f);
        await Wait(0.6);

        left?.Clear("CONTAINMENT CORE", "FINAL RECOVERY SEQUENCE");
        left?.SetBar(97.1f);
        await Wait(0.5);
        // 마지막 수치가 올라갈수록 게이지 소리가 촘촘해진다.
        foreach (var (v, gap) in new[] { (97.1f, 0.62), (98.3f, 0.52), (99.2f, 0.44), (99.8f, 0.36), (100f, 0.2) })
        {
            left?.SetBar(v);
            Sfx.Instance?.Play("gauge_tick", -10f);
            await Wait(gap);
        }

        // 100% — 짧고 묵직한 완료음. 화면이 아주 약하게 흔들린다.
        Sfx.Instance?.Play("task_done", -5f);
        Sfx.Instance?.Play("relay_click", -10f, 0.7f);
        _ctl?.ShakeMonitor("01", 0.5f, 0.3f);
        await Wait(0.7);
        left?.Push("");
        left?.Push("복구 완료", EndingMonitorView.Tone.Good, 32);
        await AwaitTyping(left, 1.4);

        // 5일 내내 울리던 기계음이 한 단계 가라앉는다.
        Sfx.Instance?.SetLoopVolume("machinery_loop", MachineryDb - 16f);
        Sfx.Instance?.SetLoopVolume("drone_loop", DroneDb - 16f);
        await Wait(0.6);

        // 다시 화면을 끄고 패드로 돌아간다 — 조회는 손 앞에서 한다.
        _ctl?.SetScreenBrightnessFor("01", 0f);
        Sfx.Instance?.Play("power_down", -14f);
        SetLights(0f, 0f);
        var pad = AdminPad3D.Instance;
        if (pad?.BodyNode != null) _ctl?.FocusProp(pad.BodyNode, 1.1f);
        await Wait(1.0);
    }

    // ── ② 신원 조회 — 패드 화면 ──────────────────────────────────────────
    // LATE 도 같은 조회를 쓴다(그쪽은 그 뒤에 경보가 바로 울린다).
    private async Task IdentityQueryFailed()
    {
        PadOpen("신원 조회 중");
        for (int i = 0; i < 3; i++) { Sfx.Instance?.Play("key_type", -18f); await Wait(0.18); }
        PadPush("▸ 사번 대조 ...", EndingMonitorView.Tone.Dim);
        await PadWait(0.8);
        PadPush("▸ 일치하는 인사 기록 없음", EndingMonitorView.Tone.Bad);
        await PadWait();

        // 1.2초 정적 — 모든 앰비언트가 한순간 멀어진다.
        Sfx.Instance?.SetLoopVolume("machinery_loop", MachineryDb - 20f);
        Sfx.Instance?.SetLoopVolume("drone_loop", DroneDb - 20f);
        Sfx.Instance?.SetLoopVolume("vent_loop", VentDb - 20f);
        await Wait(1.2);

        // 한 글자씩 느리게.
        PadPush("▸ 해당 사번은 결번입니다", EndingMonitorView.Tone.Bad, 34, charTime: 0.11);
        await PadWait();
        AdminPad3D.Instance?.FlashBadgeLed(PadRed, 3.4f, 0.55f);
        Sfx.Instance?.Play("sensor_beep", -6f);
        await Wait(1.4);
    }

    // ── ③ 처분실 ────────────────────────────────────────────────────────
    private async Task RestraintRoom()
    {
        // 조회가 끝나면 곧바로 암전한다(§2).
        await Fade(_black, 1f, 1.1f);
        EndingPadConsole.Instance?.Close();
        AdminPad3D.Instance?.SetBadgeLed(PadRed, 0f);
        _ctl?.SetScreenNoise(0f);
        Sfx.Instance?.StopLoop("machinery_loop");
        Sfx.Instance?.StopLoop("drone_loop");
        Sfx.Instance?.SetLoopVolume("vent_loop", VentDb - 26f);
        await Wait(0.9);

        // 무대를 세우고 화면 전체에 깐다. 암전 밑에서 준비하므로 전환이 보이지 않는다.
        _stage = new EndingCutsceneStage();
        AddChild(_stage);
        _stage.Attach(_layer);
        _stage.ShowSet(EndingCutsceneStage.Set.Restraint);
        _stage.Offender?.Spawn(GameState.Instance?.FinalAccusedId ?? "");
        _stage.Offender?.Slump();
        // 멀리서 시작한다 — 처음에는 그것이 무엇인지 잘 보이지 않는다.
        _stage.SetCam(new Vector3(0f, 1.32f, 2.95f), new Vector3(0f, 0.80f, 0f), 52f);
        _stage.Screen.Modulate = Colors.White;
        await Wait(0.3);

        // 암전을 걷으면 붉은 방이 드러난다.
        Sfx.Instance?.Loop("drone_loop", DroneDb - 10f);
        await Fade(_black, 0f, 1.6f);
        await Wait(1.0);

        // 숨소리 — 이 방에 들리는 유일한 소리다.
        Sfx.Instance?.Play("breath_faint", -9f);
        // 천천히 다가간다.
        await _stage.MoveCam(new Vector3(0f, 1.12f, 1.85f), new Vector3(0f, 0.80f, 0f), 3.2);
        await Wait(0.6);

        // 몸부림 — 짧은 컷씬처럼 한 번(§6). 사슬 금속음은 결번자 쪽에서 같이 난다.
        await _stage.Offender.StruggleSequence();
        Sfx.Instance?.Play("breath_faint", -7f, 0.9f);
        await Wait(0.8);

        // 고개를 든다. 그 얼굴은 사람을 흉내 내고 있던 무언가다(§7).
        _stage.Offender.RevealFace();
        await _stage.MoveCam(new Vector3(0f, 1.24f, 1.06f), new Vector3(0f, 1.16f, 0f), 1.9, 45f);
        await _stage.Offender.LiftHead(1.7);
        Sfx.Instance?.Play("tinnitus", -17f);
        await Wait(1.6);

        // ── ④ 철창 밖으로 빠지는 카메라(§8) ──────────────────────────────
        // FOV 줌이 아니라 **실제로 뒤로 물러난다**. 창살 사이 틈(x = 0)으로 빠져나간다.
        await _stage.MoveCam(new Vector3(0f, 1.38f, 7.6f), new Vector3(0f, 0.82f, 0f), 5.4, 55f);
        await Wait(1.0);

        // 암전 → 짧은 정적 → 비명. 화면 효과는 쓰지 않는다 — 비명이 중심이다(§9).
        await Fade(_black, 1f, 1.3f);
        await Wait(1.1);
        Sfx.Instance?.PlayGhostScream(6f);
        await Wait(3.2);
    }

    // ── ⑤ 거대한 봉쇄 코어실 ─────────────────────────────────────────────
    //
    // "코어가 충전됐다"가 아니라 **"봉쇄 시스템이 다시 물렸다"**로 읽혀야 한다(§11).
    private async Task CoreHall()
    {
        _stage.ShowSet(EndingCutsceneStage.Set.CoreHall);
        _stage.CoreReset();
        // 바닥에서 올려다본다 — 공간이 얼마나 큰지는 올려다볼 때만 읽힌다.
        _stage.SetCam(new Vector3(0f, 1.6f, 15.5f), new Vector3(0f, 7.6f, -3f), 62f);
        await Fade(_black, 0f, 1.8f);
        Sfx.Instance?.Loop("machinery_loop", MachineryDb - 14f);
        await Wait(1.4);

        // ① 바닥 조명이 하나씩 켜진다.
        for (int i = 0; i < 10; i++)
        {
            _stage.CoreFloorLamp(i);
            Sfx.Instance?.Play("relay_click", -19f, 0.9f + i * 0.02f);
            await Wait(0.14);
        }
        await Wait(0.5);

        // ② 링이 제자리로 정렬된다. ③ 외곽 구조가 잠금 위치로 내려온다.
        _ = _stage.MoveCam(new Vector3(0f, 3.4f, 11.0f), new Vector3(0f, 8.2f, -3f), 6.0, 60f);
        _stage.CoreAlignRings(2.6);
        Sfx.Instance?.Loop("drone_loop", DroneDb - 6f);
        Sfx.Instance?.Play("steam_hiss", -12f);
        await Wait(2.8);

        _stage.CoreClamp(1.1);
        Sfx.Instance?.Play("machinery_loop", -10f);
        await Wait(1.0);
        // ④ 마지막에 `쾅` 하고 물린다.
        Sfx.Instance?.Play("metal_clang", -3f, 0.55f);
        Sfx.Instance?.Play("boom", -9f, 0.8f);
        await Wait(0.5);

        // ⑤ 중심부 빛이 차오른다 — 저음의 충전음이 같이 올라간다.
        _stage.CoreCharge(5.8f, 2.4);
        Sfx.Instance?.Play("intro_machine", -8f);
        await Wait(2.6);
        Sfx.Instance?.Play("task_done", -8f);
        Sfx.Instance?.SetLoopVolume("drone_loop", DroneDb - 14f);
        await Wait(1.6);
    }

    // ── ⑥ 시설 재가동 몽타주 ─────────────────────────────────────────────
    //
    // 한 장면을 오래 끌지 않는다. 1~2초짜리 샷이 빠르게 이어진다(§12 · §17).
    private async Task FacilityMontage()
    {
        _stage.ShowSet(EndingCutsceneStage.Set.Corridor);
        _stage.CorridorReset();

        // A — 격벽이 실제로 내려와 닫힌다(§13).
        _stage.SetCam(new Vector3(0.6f, 1.75f, -16.5f), new Vector3(0f, 2.6f, -24f), 58f);
        await Cut(0.25);
        Sfx.Instance?.Play("vent_stop", -8f);
        _stage.BulkheadClose(1.1);
        await Wait(1.15);
        Sfx.Instance?.Play("metal_clang", -6f, 0.7f);
        _stage.BulkheadLocked();
        Sfx.Instance?.Play("relay_click", -12f);
        await Wait(0.75);

        // B — 복도 문 조명이 **하나씩** 켜진다(§14). 한꺼번에 켜지면 안 된다.
        await Cut(0.3);
        _stage.SetCam(new Vector3(0.3f, 1.72f, 7.5f), new Vector3(-0.4f, 1.9f, -20f), 56f);
        await Wait(0.45);
        for (int i = 0; i < 4; i++)
        {
            _stage.DoorLamp(i);
            Sfx.Instance?.Play("relay_click", -11f, 1.0f - i * 0.04f);
            await Wait(0.42);
        }
        await Wait(0.55);

        // C — 꺼져 있던 패널이 부팅되고, 그 화면 안에 GUIDE-0 이 뜬다(§15).
        await Cut(0.3);
        _stage.SetCam(new Vector3(0.35f, 1.92f, 6.1f), new Vector3(2.4f, 1.9f, 4f), 48f);
        await Wait(0.4);
        Sfx.Instance?.Play("crt_on", -9f);
        _stage.PanelPower(0.5f);
        await Wait(0.18);
        _stage.PanelPower(0f);           // 한 번 깜빡
        await Wait(0.14);
        _stage.PanelPower(2.6f);
        Sfx.Instance?.Play("noise", -17f);
        await Wait(0.5);
        _stage.PanelShowGuide(GuideArt.Portrait("smile", out _));
        Sfx.Instance?.Play("sensor_beep", -14f);
        await Wait(1.3);

        // D — 멈춰 있던 작업 기계가 저절로 돌기 시작한다(§16).
        await Cut(0.3);
        _stage.SetCam(new Vector3(1.55f, 2.15f, 15.4f), new Vector3(-0.2f, 1.15f, 11f), 54f);
        await Wait(0.4);
        _stage.MachinePower();
        Sfx.Instance?.Play("relay_click", -10f);
        await Wait(0.3);
        Sfx.Instance?.Play("metal_clang", -13f, 1.25f);   // 한 번 덜컹
        _stage.MachineRun(true);
        Sfx.Instance?.Loop("machinery_loop", MachineryDb - 4f);
        await Wait(1.6);
    }

    // 몽타주의 컷 전환 — 아주 짧은 암전으로 끊어 준다.
    private async Task Cut(double seconds)
    {
        await Fade(_black, 1f, (float)seconds);
        await Fade(_black, 0f, (float)seconds);
    }

    // ── ⑦ 중앙제어실 복귀 ────────────────────────────────────────────────
    //
    // 여기까지 중앙제어실은 계속 어두웠다. 이 순간이 시설 복구의 마지막 시각적 보상이다(§18).
    private async Task ControlRoomRestored()
    {
        await Fade(_black, 1f, 1.0f);
        _stage.MachineRun(false);
        _stage.ShowSet(EndingCutsceneStage.Set.None);
        _stage.Screen.Modulate = new Color(1, 1, 1, 0);   // 컷씬 화면을 걷는다
        _ctl?.ClearFocus(0.6f);
        await Wait(0.6);

        // 아직 어두운 중앙제어실.
        await Fade(_black, 0f, 1.5f);
        Sfx.Instance?.SetLoopVolume("machinery_loop", MachineryDb - 10f);
        Sfx.Instance?.Loop("vent_loop", VentDb - 6f);
        await Wait(1.6);

        // 딸깍 — 정상 전력이 돌아온다.
        Sfx.Instance?.Play("relay_click", -4f);
        await Wait(0.12);
        BrightRoom(1.6f);
        Sfx.Instance?.Play("crt_on", -12f);
        await Wait(2.0);
    }

    // 시설의 정상 조명. 아침 햇살도, 노란 가정집 조명도 아니다 —
    // "이 시설에 처음으로 정상 전력이 돌아왔다" 는 백색에 가까운 빛이다(§19).
    private void BrightRoom(float seconds)
    {
        var neutral = new Color(0.98f, 0.96f, 0.92f);
        var t = CreateTween().SetParallel(true);
        if (_ceiling != null)
        {
            t.TweenProperty(_ceiling, "light_color", _ceilC.Lerp(neutral, 0.75f), seconds);
            t.TweenProperty(_ceiling, "light_energy", _ceilE * 0.95f, seconds);
        }
        if (_roomFill != null)
        {
            t.TweenProperty(_roomFill, "light_color", _roomFillC.Lerp(neutral, 0.75f), seconds);
            t.TweenProperty(_roomFill, "light_energy", _roomFillE * 1.8f, seconds);
        }
        if (_fill != null) t.TweenProperty(_fill, "light_energy", _fillE * 0.9f, seconds);
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
        SetEmergencyMesh(0f);
    }

    // ── ⑧ 모니터2 — 마지막 휴게실 ────────────────────────────────────────
    //
    // DAY 스토리와 **같은 화면 문법**을 그대로 쓴다(§20 · §22) — 3D 휴게실도, 2D 스탠딩도,
    // 이름과 대사도 전부 모니터2 안에서만 그려진다. 중앙제어실 화면 위에 일러를 띄우지 않는다.
    private async Task LastRestStory()
    {
        var beat = PrologueScript.GetBeat("ending_true_rest");
        var dir = StoryCutinDirector.Instance;
        if (beat == null || dir == null) { await Wait(0.5); return; }

        // 지목된 직원은 이 자리에 없다 — 수용되어 시설을 떠났다.
        // (휴게실 인원 판정은 FacilitySimulation 의 상태를 그대로 읽는다.)
        string accused = GameState.Instance?.FinalAccusedId ?? "";
        var st = FacilitySimulation.Instance?.GetEmployeeState(accused);
        if (st != null) st.Isolated = true;

        // 모니터2 확대는 스토리 연출기가 한다 — 그동안만 시점 잠금을 푼다.
        _ctl?.SetFocusLocked(false);
        _ctl?.SetScreenBrightness(1f);
        await dir.PlayOnMonitor(beat);
        await Wait(0.9);

        // 모니터2 확대를 풀고 제어실 기본 위치로 돌아온다(§24).
        _ctl?.ClearFocus(0.9f);
        _ctl?.SetFocusLocked(true);
        await Wait(1.2);
    }

    // ── ⑨ GUIDE-0 의 마지막 인사 ─────────────────────────────────────────
    private async Task GuideFarewell()
    {
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);

        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenNoise(0.03f);
        Sfx.Instance?.Play("crt_on", -10f);
        SetLights(0.9f, 0.6f);
        await Wait(0.8);

        // 왼쪽 — 시설 상태. 오른쪽 — GUIDE-0.
        left?.Clear("SYSTEM STATUS");
        left?.Push("CORE ........ STABLE", EndingMonitorView.Tone.Good);
        left?.Push("POWER ....... NORMAL", EndingMonitorView.Tone.Good);
        left?.Push("CONTAINMENT . RESTORED", EndingMonitorView.Tone.Good);
        left?.Push("격리 대상 ... 수용 완료", EndingMonitorView.Tone.Good);
        for (int i = 0; i < 4; i++)
        {
            await Wait(0.42);
            Sfx.Instance?.Play("relay_click", -13f - i * 2f);
        }
        await AwaitTyping(left, 0.6);

        Sfx.Instance?.Play("window_open", -11f);
        ShowGuide("smile", onRight: true);
        _ctl?.SetScreenNoise(0.012f);
        await Wait(0.9);

        bool proven = GameState.Instance?.WasProven ?? false;
        await GuideSpeak(right,
            "봉쇄 코어 복구가 완료되었습니다.",
            "제7지하시설의 봉쇄 상태가 정상화되었습니다.",
            "관리자님. 5일간의 야간 근무, 수고하셨습니다.",
            proven ? "제출하신 근거는 전부 기록과 일치했습니다."
                   : "근거 자료는 부족했습니다. 다만 결과는 옳았습니다.");
        await Wait(2.0);
        HideGuide();

        // ── 여운 — 바로 결과 화면으로 튀지 않는다(§26).
        // 기계가 정상적으로 돌아가는 소리만 남은 밝은 방을 잠깐 그대로 둔다.
        await Wait(1.8);
        _ctl?.LeanBackInChair(3.2f);
        Sfx.Instance?.Play("chair_creak", -16f);
        await Wait(1.4);
        await Fade(_black, 1f, 2.4f);
        Sfx.Instance?.StopLoop("machinery_loop");
        Sfx.Instance?.StopLoop("drone_loop");
        Sfx.Instance?.StopLoop("vent_loop");
        await Wait(0.8);
    }
}
