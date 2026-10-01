using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.View;

// BAD — 코어 미달 + 지목 실패. 절망.
//
// 여유를 주지 않는 엔딩이다. 조회가 끝나기 무섭게 경보가 울리고, 카운트다운도 없다 —
// 이미 늦었다. 마지막은 격벽이 **아래에서 위로** 열리는 목록 하나다.
// D-3 → D-2 → D-1 → 지상 출구. 지상으로 향한다는 뜻이고, 그래서 더 말할 것이 없다.
//
// 프롤로그의 "격리 구역에서 개체들이 빠져나왔어요" 가 복수형이었던 이유가 여기서 회수된다.
public partial class EndingDirector
{
    private async Task BadEnd()
    {
        string accused = GameState.Instance?.FinalAccusedId ?? "";

        // ① 조회 — 끝나자마자 **즉시** 경보. 여유를 주지 않는다.
        PadOpen("신원 조회 중");
        for (int i = 0; i < 2; i++) { Sfx.Instance?.Play("key_type", -18f); await Wait(0.16); }
        PadPush(string.IsNullOrEmpty(accused) ? "▸ 지정 없음 · 격리 미집행" : "▸ 정상 등록 인원입니다",
            string.IsNullOrEmpty(accused) ? EndingMonitorView.Tone.Normal : EndingMonitorView.Tone.Good);
        await PadWait();
        Sfx.Instance?.Play("alert_beep3", -2f);
        AdminPad3D.Instance?.FlashBadgeLed(PadRed, 3.0f, 0.3f, 1.2f);
        await Wait(0.5);

        // ② 차폐 붕괴 — 두 모니터가 **동시에** 켜지며 붉어진다.
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);
        left?.SetAlarm(true);
        right?.SetAlarm(true);
        _ctl?.ClearFocus(0.6f);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenTint(new Color(1f, 0.52f, 0.46f));
        Sfx.Instance?.Play("crt_on", -5f);
        Sfx.Instance?.Loop("siren", -6f);
        if (_emergency != null)
        {
            _emergency.Visible = true;
            _emergency.LightColor = new Color(0.95f, 0.1f, 0.08f);
            _emergency.LightEnergy = 1.6f;
        }
        SetLights(0.85f, 0.5f);
        await Wait(0.6);

        right?.Clear("SYSTEM WARNING");
        foreach (var (text, _) in new[]
                 {
                     ("CORE ......... CRITICAL", 0), ("CONTAINMENT .. FAILING", 0),
                     ("SEAL ......... NOT ENGAGED", 0), ("격리 대상 .... 미확인", 0),
                 })
        {
            right?.Push(text, EndingMonitorView.Tone.Bad);
            await Wait(0.25);
        }
        _ctl?.ShakeCamera(1.8f, 0.6f);
        await FlickerRoom();
        await Wait(0.4);
        _ctl?.ShakeCamera(1.8f, 0.6f);
        await FlickerRoom();
        await Wait(0.3);
        await FlickerRoom();
        await Flash(_red, 0.7f, 0.18f, 0.32f);
        Sfx.Instance?.Play("boom", -2f);
        await AwaitTyping(right, 0.8);

        // ③ GUIDE-0 — horrorface 의 자리.
        left?.Clear();
        Sfx.Instance?.Play("window_open", -10f);
        GuideCornerFace.SetAlarmTint(true);
        ShowGuide("sneer");
        await Wait(1.0);
        await GuideSpeak(left, "최종 복구에 실패했습니다.", "야간 근무 기록을 종료합니다.");

        // 두 번째 줄이 끝나는 순간 — 노이즈가 치솟고 그 1프레임에 얼굴이 바뀐다.
        _ctl?.SetScreenNoise(0.9f);
        SwapGuideFace("horrorface");
        NSP.Ui.AmbientOverlay.Instance?.PulseNoise(0.6f);
        await NextFrame();
        await NextFrame();
        Sfx.Instance?.Play("cctv_cut", -4f);
        _ctl?.SetScreenBrightness(0f);
        _ctl?.SetScreenNoise(0f);
        HideGuide();
        // 책상 위 패드도 함께 죽는다 — 이 방에서 켜져 있는 것이 하나도 남지 않아야 한다.
        EndingPadConsole.Instance?.Close();
        AdminPad3D.Instance?.SetBadgeLed(PadRed, 0f);
        AdminPad3D.Instance?.SetEndingMode(false);
        await Wait(0.6);

        // ④ 격벽 개방 — 오른쪽 모니터에만. 아래에서 위로 올라간다 = 지상으로 향한다.
        SetLights(0f, 0f);
        if (_ceiling != null) _ceiling.LightEnergy = 0f;
        if (_roomFill != null) _roomFill.LightEnergy = 0f;
        await Wait(0.6);

        // 오른쪽 CRT 만 켠다 — 경보창은 그 화면 안에 뜬다.
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenBrightnessFor("01", 0f);
        _ctl?.SetScreenTint(new Color(1f, 0.6f, 0.55f));
        EndingMonitorView.Right?.Clear();
        EndingMonitorView.Right?.SetAlarm(true);
        ShowBulkheadAlert();
        var rows = new List<(string, string)>();
        foreach (var (label, value) in new[]
                 {
                     ("▸ D-3 구역", "개방"), ("▸ D-2 구역", "개방"),
                     ("▸ D-1 구역", "개방"), ("▸ 지상 출구", "개방"),
                 })
        {
            rows.Add((label, value));
            SetBulkheadRows(rows);
            Sfx.Instance?.Play("metal_clang", -14f);
            await Wait(0.9);
        }
        await Wait(0.6);

        // 사이렌을 **뚝 끊는다.** 페이드 아웃이 아니다.
        Sfx.Instance?.StopLoop("siren");
        foreach (var k in new[] { "machinery_loop", "drone_loop", "vent_loop" }) Sfx.Instance?.StopLoop(k);
        await Wait(1.5);
        HideBulkheadAlert();

        // ⑤ 끝 — 문. 완전한 어둠. 두 화면도 비상등도 전부 끈다.
        _ctl?.SetScreenBrightness(0f);
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
        SetEmergencyMesh(0f);
        _ctl?.SetScreenTint(Colors.White);
        _ctl?.TurnToLookAt(_props?.DoorLookPoint ?? new Vector3(1.55f, 1.55f, -2.54f), 1.4f, DoorEyeOffset);
        await Wait(1.5);

        // 복도 쪽에서 빛이 들어온다. 그림자가 지나간다 — 하나, 둘, 셋. 발소리는 없다.
        _props?.SetCorridorLight(true);
        await Wait(1.0);
        if (_props != null)
        {
            await _props.PassShadow(0.6f, this);
            await Wait(0.4);
            await _props.PassShadow(0.6f, this);
            await Wait(0.4);
            await _props.PassShadow(0.6f, this);
        }
        await Wait(1.2);

        // 문 손잡이가 한 번 돌아간다.
        Sfx.Instance?.Play("metal_clang", -20f);
        if (_props != null) await _props.TurnHandle(this);

        // 즉시 암전(페이드 없음).
        _black.Color = _black.Color with { A = 1f };
        _props?.AllOff();
        await Wait(1.0);
        await Banner("BAD END — 복구 실패", new Color(1f, 0.45f, 0.38f));
        EndingState.Record(EndingState.Kind.Bad);
    }

    // ── 격벽 경보창 — 프롤로그 컷씬의 alert: 양식 그대로 ────────────────────
    private AlertBoard _bulkhead;

    private void ShowBulkheadAlert()
    {
        _bulkhead?.QueueFree();
        _bulkhead = new AlertBoard
        {
            Title = "지상 연결 통로",
            Sub = "격벽 상태",
            Foot = "",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(150f, 90f),
            Size = new Vector2(500f, 420f),
        };
        // **오른쪽 CRT 안에** 띄운다 — 화면 위에 떠 있는 판이 아니라 모니터가 뱉은 출력이다.
        var host = _ctl?.EndingRightViewport?.GetChild<Control>(0);
        (host ?? (Node)_layer).AddChild(_bulkhead);
        _bulkhead.Reset();
        _bulkheadT = 0;
    }

    private double _bulkheadT;

    private void SetBulkheadRows(List<(string Label, string Value)> rows)
    {
        if (_bulkhead == null || !IsInstanceValid(_bulkhead)) return;
        _bulkhead.Rows = rows.ToArray();
        // AlertBoard 는 흐른 시간으로 항목을 하나씩 켠다 — 줄을 넣은 만큼만 켜지게 맞춘다.
        _bulkheadT = 0.31 + (rows.Count - 1) * 0.22;
        _bulkhead.Tick(_bulkheadT);
    }

    private void HideBulkheadAlert()
    {
        _bulkhead?.QueueFree();
        _bulkhead = null;
    }
}
