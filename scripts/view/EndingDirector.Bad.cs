using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.View;

// BAD — 코어 미달 + 지목 실패. 절망.
//
// 순서(2026-10-31 사용자 지시):
//   ① 공통 도입 — 컴퓨터가 꺼지는 소리. **방 조명은 켜지 않는다.** 그 밑으로 사이렌이 얕게 깔려 끝까지 간다
//   ② 조회 — "해당 직원은 괴물이 아니었습니다"
//   ③ 격벽 개방 목록이 **먼저** 뜬다. D-3 → D-2 → D-1 → 지상 출구, 아래에서 위로 = 지상으로 향한다
//   ④ 그 다음에 중앙제어실이 새빨갛게 켜지고, GUIDE-0 이 코어 복구 실패를 알린다
//   ⑤ 문. 그림자는 없다. 밖에서 무언가가 쾅!! 쾅!! 문을 친다(문짝이 떤다)
//   ⑥ 뚝 그친다 … 아무도 없는데 문이 끼이이익 열린다. 문 밖은 새까맣다
//   ⑦ 그것이 눈앞에 들이닥친다 — 관리자 사망 → 암전 → 근무 기록
//
// 프롤로그의 "격리 구역에서 개체들이 빠져나왔어요" 가 복수형이었던 이유가 여기서 회수된다.
public partial class EndingDirector
{
    // 공통 도입부터 마지막까지 깔리는 사이렌. "얕게" — 경보라기보다 멀리서 계속 우는 소리다.
    private const float BadSirenDb = -21f;

    // 돌아볼 곳 — 문 한가운데. 문은 모니터와 같은 쪽(앞 벽)에 있어서 자리에서 보면 모니터2 가
    // 아래 절반을 가린다. 의자에서 일어나 오른쪽으로 비켜 선 눈높이로 본다 — 모니터2 오른쪽
    // 가장자리(x 0.88) 바깥으로 시선이 지나가 문짝이 바닥까지 다 보인다.
    private static readonly Vector3 BadDoorLook = new(1.55f, 1.0f, -2.54f);
    private static readonly Vector3 BadDoorEye = new(0.95f, 0.38f, 0.10f);

    // 공통 도입의 전원 차단과 함께 사이렌을 아주 작게 틀어 천천히 올린다.
    private void StartBadSiren()
    {
        Sfx.Instance?.Loop("siren", -42f);
        Sfx.Instance?.SetLoopPitch("siren", 0.9f);
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(db => Sfx.Instance?.SetLoopVolume("siren", db)), -42f, BadSirenDb, 3.0);
    }

    private async Task BadEnd()
    {
        string accused = GameState.Instance?.FinalAccusedId ?? "";
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;

        // ② 조회 — 어둠 속에서 모니터2 하나만 켜진다. 사이렌은 이미 얕게 돌고 있다.
        await QueryOpen("신원 조회 중");
        for (int i = 0; i < 2; i++) { Sfx.Instance?.Play("key_type", -18f); await Wait(0.16); }
        if (string.IsNullOrEmpty(accused))
            QueryPush("▸ 지정 없음, 격리 미집행", EndingMonitorView.Tone.Normal);
        else
            QueryPush("▸ 해당 직원은 괴물이 아니었습니다", EndingMonitorView.Tone.Good, 34);
        await QueryWait();
        Sfx.Instance?.Play("alert_beep3", -2f);
        await Wait(1.0);

        // ③ 격벽 개방 — 같은 모니터2 에 경보창이 뜬다(확대한 채 그대로 읽는다).
        _ctl?.SetScreenNoise(0.35f);
        Sfx.Instance?.Play("cctv_cut", -10f);
        await Wait(0.12);
        _ctl?.SetScreenNoise(0.05f);
        _ctl?.SetScreenTint(new Color(1f, 0.6f, 0.55f));
        if (_m2 != null) _m2.LightColor = new Color(1f, 0.42f, 0.38f);
        right?.Clear();
        right?.SetAlarm(true);
        ShowBulkheadAlert();
        Sfx.Instance?.Play("alert_beep3", -8f, 0.8f);
        await Wait(0.8);
        var rows = new List<(string, string)>();
        foreach (var (label, value) in new[]
                 {
                     ("▸ D-3 구역", "개방"), ("▸ D-2 구역", "개방"),
                     ("▸ D-1 구역", "개방"), ("▸ 지상 출구", "개방"),
                 })
        {
            rows.Add((label, value));
            SetBulkheadRows(rows);
            // 저 아래 어딘가에서 격벽이 하나씩 올라간다.
            Sfx.Instance?.Play("metal_clang", -14f);
            Sfx.Instance?.Play("bulkhead_slam", -22f, 0.7f);
            await Wait(1.0);
        }
        await Wait(1.2);

        // ④ 중앙제어실이 새빨갛게 켜진다 — 쾅 하고 한 번에. 그 다음에 GUIDE-0.
        HideBulkheadAlert();
        _ctl?.ClearFocus(0.8f);
        Sfx.Instance?.Play("switch", -4f, 0.62f);
        Sfx.Instance?.Play("boom", -4f, 0.72f);
        BadRoomRed();
        _ctl?.ShakeCamera(1.2f, 0.4f);
        await Wait(1.4);

        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);
        left?.SetAlarm(true);
        right?.SetAlarm(true);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenTint(new Color(1f, 0.52f, 0.46f));
        if (_m1 != null) { _m1.LightColor = new Color(1f, 0.42f, 0.38f); _m1.LightEnergy = _m1E * 0.5f; }
        if (_m2 != null) _m2.LightEnergy = _m2E * 0.5f;
        Sfx.Instance?.Play("crt_on", -6f);
        // 얼굴은 왼쪽(모니터1), 말은 오른쪽(모니터2). 같은 화면에 두면 얼굴창이 대사를 덮는다.
        Sfx.Instance?.Play("window_open", -10f);
        GuideCornerFace.SetAlarmTint(true);
        ShowGuide("sneer");
        await Wait(1.0);
        await GuideSpeak(right, "코어 복구에 실패했습니다.", "야간 근무 기록을 종료합니다.");

        // 두 번째 줄이 끝나는 순간 — 노이즈가 치솟고 그 1프레임에 얼굴이 바뀐다. 그리고 두 화면이 죽는다.
        _ctl?.SetScreenNoise(0.9f);
        SwapGuideFace("horrorface");
        NSP.Ui.AmbientOverlay.Instance?.PulseNoise(0.6f);
        await NextFrame();
        await NextFrame();
        Sfx.Instance?.Play("cctv_cut", -4f);
        _ctl?.SetScreenBrightness(0f);
        _ctl?.SetScreenNoise(0f);
        HideGuide();
        SetLights(0f, 0f);   // 화면빛이 사라지고 붉은 등만 남는다
        await Wait(1.2);

        // ⑤⑥ 문 — 쾅!! 쾅!! … 끼이이익.
        // 그것은 이때 미리 세워 둔다(모델을 읽는 끊김이 들이닥치는 순간에 오지 않게).
        var scare = AdminDeathDirector.PrepareScare(_ctl, "absentee");
        await DoorKnockAndOpen();

        // 문 밖은 새까맣다. 아무것도 나오지 않는다 — 그렇게 생각할 만큼만 기다린다.
        await Wait(1.9);

        // ⑦ 그것이 렌즈 앞에. 그 한 프레임에 비명 · 충격 · 흔들림이 같이 터진다.
        _badRed = false;   // 붉은 조명은 이제 들이닥침 연출이 잡는다
        if (scare != null) await scare.ScareAsync();
        else { Sfx.Instance?.PlayGhostScreamChorus(); await Wait(0.8); }

        // 관리자 사망 — 그 자리에서 암전(페이드 없음). 비명만 어둠 속에 남는다.
        _black.Color = _black.Color with { A = 1f };
        scare?.EndScare();
        BadLightsOff();
        Sfx.Instance?.StopLoop("siren");
        foreach (var k in new[] { "machinery_loop", "drone_loop", "vent_loop" }) Sfx.Instance?.StopLoop(k);
        Sfx.Instance?.Play("body_fall", -6f, 0.85f);
        await Wait(2.4);
        await Banner("BAD END - 복구 실패", new Color(1f, 0.45f, 0.38f));
        EndingState.Record(EndingState.Kind.Bad);
    }

    // ── 문 (BAD · LATE 공통) ────────────────────────────────────────────
    //
    // 문을 돌아본다. 그림자도 복도 빛도 없다 — 붉은 방 안에서 닫힌 문 하나만.
    // 밖에서 무언가가 쾅!! 쾅!! 친다 → 뚝 그친다 → 아무도 없는데 문이 끼이이익 열린다. 문 밖은 새까맣다.
    private async Task DoorKnockAndOpen()
    {
        _props?.PrepareDoor();
        Sfx.Instance?.Play("chair_creak", -14f, 0.9f);
        _ctl?.TurnToLookAt(BadDoorLook, 1.8f, BadDoorEye);
        await Wait(2.6);

        // 쾅!! 쾅!! — 둘씩 끊어 치다가 마지막엔 쉬지 않고 몰아친다.
        foreach (var (strength, gap) in new[]
                 {
                     (0.8f, 0.42), (0.9f, 1.6),
                     (1f, 0.40), (1f, 1.3),
                     (1f, 0.30), (1f, 0.28), (1f, 0.26), (1f, 0.30),
                 })
        {
            BangDoor(strength);
            await Wait(gap);
        }

        // 뚝. 사이렌만 남는다 … 그리고 아무도 없는데 문이 열린다.
        await Wait(2.6);
        Sfx.Instance?.Play("door_handle", -6f, 0.78f);
        if (_props != null) _ = _props.TurnHandle(this);
        await Wait(0.45);
        if (_props != null) await _props.SwingDoor(3f, 0.16f, this);   // 걸쇠가 풀리며 살짝 벌어진다
        await Wait(1.1);
        // 끼이이익 — 낮고 길게. 원음 한 겹 + 일그러진 한 겹.
        Sfx.Instance?.Play("door_scratch", -2f, 0.42f);
        Sfx.Instance?.PlayWarped("chair_creak", -5f, 0.38f, "bad_creak1", delaySeconds: 0.6f,
            bus: GameSettings.BusSfx);
        Sfx.Instance?.PlayWarped("door_scratch", -9f, 0.33f, "bad_creak2", delaySeconds: 1.2f);
        Sfx.Instance?.PlayWarped("chair_creak", -7f, 0.32f, "bad_creak3", delaySeconds: 2.3f,
            bus: GameSettings.BusSfx);
        if (_props != null) await _props.SwingDoor(78f, 3.8f, this);
        else await Wait(3.8);
    }

    // ── 붉은 방 ───────────────────────────────────────────────────────
    //
    // 평소 이 방의 빛은 두 모니터였고, 천장등 묶음은 숨겨져 있다. 여기서 천장등 · 보조광 ·
    // 비상등을 **전부 붉게** 켠다. 비상등은 문 바로 옆이 제자리라 열리는 문짝이 그 빛에 하얗게
    // 타 버린다 — 방 한가운데 천장 쪽으로 옮겨 방 전체와 문을 같이 적신다.
    // 문 옆 비상등 덩어리(발광 메시)는 숨긴다. 열리는 문 위 모서리를 뚫고 나온다.
    private static readonly Color BadRed = new(1f, 0.07f, 0.045f);
    private bool _badRed;
    private double _badRedT;
    private float _badDip = 1f;   // 쾅 할 때 잠깐 떨어졌다 돌아오는 밝기

    private void BadRoomRed()
    {
        _badRed = true;
        if (_lightGroup != null) _lightGroup.Visible = true;
        if (_emergencyMesh != null) _emergencyMesh.Visible = false;
        if (_ceilFixture is MeshInstance3D mi && mi.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            var red = (StandardMaterial3D)mat.Duplicate();
            red.Emission = BadRed;
            red.AlbedoColor = BadRed;
            red.EmissionEnergyMultiplier = 0.6f;
            mi.SetSurfaceOverrideMaterial(0, red);
        }
        if (_ceiling != null) { _ceiling.LightColor = BadRed; _ceiling.LightEnergy = 2.6f; }
        if (_roomFill != null) { _roomFill.LightColor = BadRed; _roomFill.LightEnergy = _roomFillE * 2.4f; }
        if (_fill != null) { _fill.LightColor = BadRed; _fill.LightEnergy = _fillE * 1.4f; }
        if (_emergency != null)
        {
            _emergency.Visible = true;
            _emergency.LightColor = BadRed;
            _emergency.Position = new Vector3(0.9f, 2.3f, -1.3f);
            _emergency.OmniRange = 7.6f;
            _emergency.LightEnergy = 3.0f;
        }
    }

    // 붉은 등은 숨을 쉰다 — 고정값이면 조명이 아니라 색필터처럼 보인다.
    private void TickBadRed(double delta)
    {
        if (!_badRed) return;
        _badRedT += delta;
        _badDip = Mathf.MoveToward(_badDip, 1f, (float)delta * 4f);
        float pulse = 0.80f + 0.20f * Mathf.Sin((float)_badRedT * 5.2f);
        if (_emergency != null) _emergency.LightEnergy = 3.0f * pulse * _badDip;
        if (_ceiling != null) _ceiling.LightEnergy = 2.6f * _badDip;
    }

    private void BadLightsOff()
    {
        _badRed = false;
        SetLights(0f, 0f);
        if (_ceiling != null) _ceiling.LightEnergy = 0f;
        if (_roomFill != null) _roomFill.LightEnergy = 0f;
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
    }

    // 밖에서 한 번 쾅 — 문짝이 튀고, 방이 흔들리고, 붉은 등이 한순간 꺼질 듯 떨어진다.
    private void BangDoor(float s)
    {
        _props?.BangDoor(s);
        Sfx.Instance?.Play("impact_blunt", 3f * s, _rng.RandfRange(0.50f, 0.58f));
        Sfx.Instance?.Play("bulkhead_slam", -4f + 4f * s, _rng.RandfRange(1.35f, 1.5f));
        Sfx.Instance?.Play("boom", -9f + 5f * s, _rng.RandfRange(0.58f, 0.66f));
        Sfx.Instance?.Play("metal_clang", -13f, _rng.RandfRange(0.55f, 0.65f));
        _ctl?.ShakeCamera(1.4f + 2.2f * s, 0.32f);
        NSP.Ui.AmbientOverlay.Instance?.PulseNoise(0.22f * s);
        _badDip = Mathf.Min(_badDip, 1f - 0.55f * s * GameSettings.FlashScale);
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
