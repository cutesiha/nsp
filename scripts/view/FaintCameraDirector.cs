using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 기절 · 구조가 일어나는 **그 방을 CCTV 가 계속 보여 준다.**
//
// 이 흐름은 관리자가 조작해야 하는 일인데(사람을 보내고 · 옮기게 하고), 정작 그것이
// 어디서 일어나는지는 미니맵의 아이콘 하나로만 알려 줬다. 보지 못한 사고를 수습하라는
// 꼴이라, 처음 겪는 사람은 무슨 일이 난 것인지도 모른 채 시간만 보냈다.
//
// 그래서 **처음부터 끝까지 붙잡는다**(1013 QA 13번 — "왜 시간 지나니까 풀리냐").
//
//   ① 쓰러진다            → 그 작업실에 고정          (쓰러지는 것을 본다)
//   ② 구조자가 도착한다    → 같은 작업실 그대로        (일으키는 것을 본다)
//   ③ 들고 의무실로 간다   → 의무실에 고정             (눕히는 것을 본다)
//   ④ 전화가 울린다       → 강제 해제                 (화면을 돌려준다)
//
// 예전에는 ①②③ 이 각각 3~4초짜리 강제였고 그 뒤에는 저절로 풀렸다. 그래서 사람을
// 보내는 동안 화면이 제멋대로 돌아가 "구조가 어디까지 갔는지" 를 놓쳤다. 고정은
// 사람을 보내는 조작을 막지 않는다 — 구조 요청은 **전화**로 받고 배치는 배치표에서
// 하므로, CCTV 채널이 묶여 있어도 할 수 있는 일은 그대로다.
//
// 풀리는 조건은 전화다. 교육에서는 "이제 원래 자리로 돌아갈까요?" 가 울리는 그 순간이고,
// 실제 근무에서는 다음 통화가 울릴 때다. 전화가 끝내 오지 않는 경우를 대비해
// 침대에 눕힌 뒤 BedReleaseSeconds 가 지나면 안전하게 풀어 준다.
public partial class FaintCameraDirector : Node
{
    // 강제 고정을 한 번에 거는 길이(초). 매 프레임 다시 걸어 '계속 고정' 을 만든다 —
    // 이 값은 "연출기가 죽어도 이만큼 뒤에는 반드시 풀린다" 는 안전장치다.
    private const float HoldRenew = 2.5f;
    // 침대에 눕힌 뒤 전화가 오지 않아도 풀어 주는 시간(초).
    private const float BedReleaseSeconds = 20f;

    private FaintRescueSystem _rescue;
    private bool _wired;

    // 지금 이 환자의 이송을 따라가고 있는가.
    private string _following = "";
    private bool _medicalShown;
    // 침대에 눕힌 시각(초, Godot 시계). 0 이면 아직 아니다.
    private double _bedAt;
    // 고정을 건 방. 비어 있으면 아무것도 붙잡고 있지 않다.
    private string _held = "";

    public override void _Process(double delta)
    {
        if (!_wired) Wire();
        if (!_wired) return;
        TickTransport();
        TickHold();
    }

    public override void _ExitTree() => Unwire();

    private void Wire()
    {
        _rescue = FacilitySimulation.Instance?.Rescue;
        if (_rescue == null) return;
        _wired = true;
        _rescue.Fainted += OnFainted;
        _rescue.Lifting += OnLifting;
        _rescue.BedReached += OnBedReached;
    }

    private void Unwire()
    {
        if (!_wired || _rescue == null) return;
        _wired = false;
        _rescue.Fainted -= OnFainted;
        _rescue.Lifting -= OnLifting;
        _rescue.BedReached -= OnBedReached;
    }

    // ① 쓰러진 순간 — 그 작업실을 붙잡는다.
    private void OnFainted(string victimId)
    {
        _following = victimId ?? "";
        _medicalShown = false;
        _bedAt = 0;
        Hold(RoomOf(victimId));
    }

    // ② 구조자가 도착해 들어 올리기 시작 — 같은 작업실 그대로.
    //    (환자가 그 사이 옮겨졌을 수도 있으니 방은 다시 읽는다.)
    private void OnLifting(string carrierId, string victimId)
    {
        _following = victimId ?? "";
        _medicalShown = false;
        _bedAt = 0;
        Hold(RoomOf(victimId));
    }

    // ③ 실제로 **옮기기 시작하면** 의무실로. 들어 올리는 장면을 먼저 보여 주고 나서다.
    private void TickTransport()
    {
        if (_medicalShown || string.IsNullOrEmpty(_following)) return;
        var st = FacilitySimulation.Instance?.GetEmployeeState(_following);
        if (st == null) { _following = ""; return; }
        if (st.Faint is not (FaintPhase.Transporting or FaintPhase.BedApproach)) return;
        _medicalShown = true;
        Hold(FacilitySimulation.MedicalRoomIdPublic);
    }

    // ④ 침대에 눕혔다 — 전화가 울릴 때까지는 의무실을 그대로 보여 준다.
    private void OnBedReached(string victimId)
    {
        if (victimId != _following) return;
        _bedAt = Time.GetTicksMsec() / 1000.0;
    }

    // 고정을 유지하고, 풀 때를 본다.
    private void TickHold()
    {
        if (string.IsNullOrEmpty(_held)) return;

        // 근무가 아니면(휴게시간 · 정산 · 엔딩) 즉시 놓는다 — 그 화면들이 채널을 쓴다.
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) { Release(); return; }

        // 침대에 눕힌 뒤 전화가 울리면 푼다 — "이제 원래 자리로 돌아갈까요?" 가 그 신호다.
        // **눕히기 전의 전화는 풀지 않는다.** 그건 "데려올까요?" 를 묻는 구조 요청이고,
        // 그 통화 중에도 쓰러진 사람을 계속 보여 줘야 한다.
        if (_bedAt > 0)
        {
            if (Phone3D.Instance?.IsRinging == true) { Release(); return; }
            // 전화가 끝내 오지 않는 경우의 안전장치.
            if (Time.GetTicksMsec() / 1000.0 - _bedAt >= BedReleaseSeconds) { Release(); return; }
        }

        // 플레이어가 직접 다른 카메라를 골랐으면 강제가 풀려 있다 — 손을 뗀다.
        // 그러지 않으면 매 프레임 다시 붙들어서 영영 다른 곳을 볼 수 없다.
        if (FacilitySimulation.Instance?.SurveillanceForced != true) { Release(); return; }
        FacilitySimulation.Instance?.ForceSurveillanceTarget(_held, HoldRenew);
    }

    private void Hold(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        _held = roomId;
        FacilitySimulation.Instance?.ForceSurveillanceTarget(roomId, HoldRenew);
    }

    private void Release()
    {
        _held = "";
        _following = "";
        _medicalShown = false;
        _bedAt = 0;
        FacilitySimulation.Instance?.ReleaseForcedSurveillance();
    }

    private static string RoomOf(string id) =>
        FacilitySimulation.Instance?.GetEmployeeState(id)?.CurrentRoomId ?? "";
}
