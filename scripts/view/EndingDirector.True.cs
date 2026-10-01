using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// TRUE — 코어 100% + 지목 정답. 안도, 그리고 쓸쓸함.
//
// 이 엔딩의 두 장면은 대사가 하나도 없다.
//   ② 재난 당일의 회상 — "누가 당신을 쳤는가" 의 답을 소리와 카메라만으로 준다.
//   ④ 마지막 통화    — 5일 내내 떠들던 것이 정체가 드러나자 연기를 그만두는 순간이다.
// 두 장면 모두 **대사를 넣지 마라.** 넣는 순간 설명이 되고, 설명이 되면 아무것도 아니게 된다.
public partial class EndingDirector
{
    private async Task TrueEnd()
    {
        await IdentityQueryFailed();
        await Flashback();
        await ArchiveRecord();
        await LastCall();
        await RecoveryComplete("격리 대상 ... 수용 완료", EndingMonitorView.Tone.Good);

        // ⑥ GUIDE-0 — 웃는 얼굴.
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
        bool proven = GameState.Instance?.WasProven ?? false;
        await GuideSpeak(right,
            "야간 근무가 종료되었습니다.",
            "관리자님, 수고하셨습니다.",
            proven ? "제출하신 근거는 전부 기록과 일치했습니다."
                   : "근거 자료는 부족했습니다. 다만 결과는 옳았습니다.");
        await Wait(2.2);

        // ⑦ 끝 — 아무도 보내지 않은 한 줄.
        HideGuide();
        await Wait(2.2);
        var pad = AdminPad3D.Instance;
        pad?.SetBadgeLed(PadBlue, 1.2f);
        PadOpen("");
        PadPush("※ 해당 사번의 본래 배정자는", EndingMonitorView.Tone.Dim, 24);
        PadPush("   끝내 확인되지 않았습니다.", EndingMonitorView.Tone.Dim, 24);
        await PadWait(3.0);

        _ctl?.LeanBackInChair(3.4f);
        Sfx.Instance?.Play("chair_creak", -16f);
        await Wait(1.0);
        await Fade(_black, 1f, 2.8f);
        Sfx.Instance?.StopLoop("vent_loop");
        await Wait(0.6);
        await Banner("TRUE END — 근무 종료", new Color(0.72f, 0.96f, 0.88f));
        EndingState.Record(EndingState.Kind.True);
    }

    // ── ① 신원 조회 — 패드 화면 ──────────────────────────────────────────
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
        await Wait(1.0);
    }

    // ── ② 회상 : 재난 당일 ──────────────────────────────────────────────
    //
    // 모니터로 보여주지 않는다. **방 자체가 그날로 돌아간다.**
    // 대사 한 줄 쓰지 마라 — 효과음과 카메라만으로 전달한다.
    private async Task Flashback()
    {
        // 1. 흰 섬광 + 짧은 이명.
        Sfx.Instance?.Play("tinnitus", -14f);
        await Flash(_white, 1f, 0.07f, 0.08f);

        // 2. 조명 전환 — 급하게.
        if (_ceiling != null) _ceiling.Visible = false;
        if (_emergency != null)
        {
            _emergency.Visible = true;
            _emergency.LightColor = new Color(0.95f, 0.12f, 0.08f);
            _emergency.LightEnergy = 1.6f;
        }
        _ctl?.SetScreenTint(new Color(1.15f, 0.72f, 0.68f));
        await Wait(0.4);

        // 3. 사이렌 — 아주 멀리서. 프롤로그의 그 사이렌이다.
        Sfx.Instance?.Loop("siren", -26f);

        // 4. 카메라가 강제로 문 쪽을 향한다. 게임 내내 볼 일 없던 방향이다.
        _ctl?.TurnToLookAt(_props?.DoorLookPoint ?? new Vector3(1.55f, 1.55f, -2.54f), 1.0f, DoorEyeOffset);
        await Wait(1.1);

        // 5. 문 창에 사람 형체가 **서 있다.** 움직이지 않는다.
        _props?.ShowStandingFigure(true);
        await Wait(2.4);

        // 6. 프롤로그에서 머리를 맞을 때와 **완전히 같은 효과음 조합**.
        Sfx.Instance?.Play("impact_blunt", -2f);
        _black.Color = _black.Color with { A = 1f };
        _props?.ShowStandingFigure(false);
        await Wait(0.25);
        Sfx.Instance?.Play("body_fall", -6f);

        // 7. 암전 상태의 이명.
        Sfx.Instance?.Play("tinnitus", -8f);
        await Wait(0.8);

        // 8. 서서히 복귀 — 문에는 아무도 없다.
        Sfx.Instance?.StopLoop("siren");
        if (_ceiling != null) _ceiling.Visible = true;
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
        _ctl?.SetScreenTint(Colors.White);
        var pad = AdminPad3D.Instance;
        if (pad?.BodyNode != null) _ctl?.FocusProp(pad.BodyNode, 1.2f);
        await Fade(_black, 0f, 1.2f);
        Sfx.Instance?.SetLoopVolume("machinery_loop", MachineryDb - 8f);
        Sfx.Instance?.SetLoopVolume("drone_loop", DroneDb - 8f);
        Sfx.Instance?.SetLoopVolume("vent_loop", VentDb - 8f);
        await Wait(0.6);
    }

    // ── ③ 기록 세 줄 ────────────────────────────────────────────────────
    // 세 번째 줄 뒤 3초 정적. 아무 소리도 없다.
    private async Task ArchiveRecord()
    {
        PadOpen("기록 조회");
        PadPush("▸ 최초 등록 ................. 재난 당일 23:47", EndingMonitorView.Tone.Normal, 26);
        await PadWait(1.0);
        PadPush("▸ 중앙제어실 출입 기록 ....... 1건", EndingMonitorView.Tone.Normal, 26);
        await PadWait(1.0);
        PadPush("▸ 총괄 관리자 생체 신호 소실 ... 23:47", EndingMonitorView.Tone.Bad, 26);
        await PadWait(3.0);
    }

    // ── ④ 마지막 통화 ───────────────────────────────────────────────────
    //
    // **아무 말도 하지 않는다.** 자막 없음, 타이핑 보이스 없음, 숨소리만 3초.
    private async Task LastCall()
    {
        Sfx.Instance?.Play("call_ring", -4f);
        var phone = Phone3D.Instance;
        if (phone != null) _ctl?.FocusProp(phone, 0.8f, 0.42f);
        await Wait(0.9);

        Sfx.Instance?.Play("phone_pickup", -6f);
        phone?.EndingLift();
        await Wait(1.3);

        string id = GameState.Instance?.FinalAccusedId ?? "";
        ShowStanding(id);
        Sfx.Instance?.Play("breath_faint", -10f);
        await Wait(3.0);

        Sfx.Instance?.Play("phone_hangup", -6f);
        phone?.EndingHangUp();
        _ctl?.SetScreenNoise(0.4f);
        HideStanding(0.7f);
        var n = CreateTween();
        n.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenNoise(v)), 0.4f, 0.02f, 0.8);
        await Wait(1.2);
    }

    // ── 스탠딩 일러스트(마지막 통화) ──────────────────────────────────────
    private TextureRect _standing;

    private void ShowStanding(string employeeId, string mood = "bad", string mouth = "closedmouth")
    {
        if (string.IsNullOrEmpty(employeeId)) return;
        string path = $"res://assets/characters/standing_v2/{employeeId}/{employeeId}_{mood}_{mouth}.png";
        var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path)
                                              : FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.StandingImage;
        if (tex == null) return;

        _standing?.QueueFree();
        _standing = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _standing.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _standing.AnchorLeft = 0.52f;
        _standing.OffsetLeft = 0f;
        _standing.OffsetTop = 0f;
        _standing.OffsetRight = 0f;
        _standing.OffsetBottom = 0f;
        _layer.AddChild(_standing);
        var t = CreateTween();
        t.TweenProperty(_standing, "modulate:a", 1f, 0.6);
    }

    private void HideStanding(float seconds)
    {
        if (_standing == null) return;
        var s = _standing;
        _standing = null;
        var t = CreateTween();
        t.TweenProperty(s, "modulate:a", 0f, Mathf.Max(0.05f, seconds));
        t.TweenCallback(Callable.From(() => { if (IsInstanceValid(s)) s.QueueFree(); }));
    }
}
