using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 기절 · 구조가 일어나는 **그 방을 CCTV 가 직접 보여 준다.**
//
// 이 흐름은 관리자가 조작해야 하는 일인데(사람을 보내고 · 옮기게 하고), 정작 그것이
// 어디서 일어나는지는 미니맵의 아이콘 하나로만 알려 줬다. 보지 못한 사고를 수습하라는
// 꼴이라, 처음 겪는 사람은 무슨 일이 난 것인지도 모른 채 시간만 보냈다.
//
// 그래서 **장면이 바뀌는 네 순간에만** 화면을 그쪽으로 돌린다.
//
//   ① 쓰러진다            → 그 작업실                (쓰러지는 것을 본다)
//   ② 구조자가 도착한다    → 그 작업실                (일으키는 것을 본다)
//   ③ 들고 의무실로 간다   → 의무실                   (눕히는 것을 본다)
//   ④ 침대에 눕혔다       → 강제 해제                 (화면을 돌려준다)
//
// **계속 붙잡아 두지 않는다.** ①과 ② 사이에는 관리자가 다른 직원을 눌러 그 방을 봐야
// 하기 때문이다(그게 사람을 보내는 조작이다). 그래서 각 전환은 몇 초짜리 강제이고,
// 그 뒤에는 원래 보던 채널로 저절로 돌아간다(FacilitySimulation.ForceSurveillanceTarget).
public partial class FaintCameraDirector : Node
{
    // 각 장면을 붙잡아 두는 시간(초). 짧으면 무슨 일인지 못 보고, 길면 조작을 막는다.
    private const float FaintHold = 3.2f;
    private const float PickupHold = 3.0f;
    private const float BedHold = 4.0f;

    private FaintRescueSystem _rescue;
    private bool _wired;

    // 지금 이 환자의 이송을 따라가고 있는가 — 의무실로 돌리는 순간을 한 번만 잡기 위한 값.
    private string _following = "";
    private bool _medicalShown;

    public override void _Process(double delta)
    {
        if (!_wired) Wire();
        if (!_wired) return;
        TickTransport();
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

    // ① 쓰러진 순간 — 그 작업실로.
    private void OnFainted(string victimId)
    {
        _following = victimId ?? "";
        _medicalShown = false;
        Look(RoomOf(victimId), FaintHold);
    }

    // ② 구조자가 도착해 들어 올리기 시작 — 다시 그 작업실로.
    //    (①에서 강제가 풀린 뒤 관리자가 다른 방을 보고 있을 수 있다. 그래서 다시 돌린다.)
    private void OnLifting(string carrierId, string victimId)
    {
        _following = victimId ?? "";
        _medicalShown = false;
        Look(RoomOf(victimId), PickupHold);
    }

    // ③ 실제로 **옮기기 시작하면** 의무실로. 들어 올리는 장면을 먼저 보여 주고 나서다.
    private void TickTransport()
    {
        if (_medicalShown || string.IsNullOrEmpty(_following)) return;
        var st = FacilitySimulation.Instance?.GetEmployeeState(_following);
        if (st == null) { _following = ""; return; }
        if (st.Faint is not (FaintPhase.Transporting or FaintPhase.BedApproach)) return;
        _medicalShown = true;
        Look(FacilitySimulation.MedicalRoomIdPublic, BedHold);
    }

    // ④ 침대에 눕혔다 — 화면을 관리자에게 돌려준다.
    private void OnBedReached(string victimId)
    {
        if (victimId != _following) return;
        _following = "";
        _medicalShown = false;
        FacilitySimulation.Instance?.ReleaseForcedSurveillance();
    }

    private static string RoomOf(string id) =>
        FacilitySimulation.Instance?.GetEmployeeState(id)?.CurrentRoomId ?? "";

    // 근무 중이고 CCTV 가 살아 있을 때만 돌린다. 휴게시간·정산 화면에서 채널을 빼앗으면
    // 그 화면이 통째로 어긋난다.
    private static void Look(string roomId, float seconds)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        FacilitySimulation.Instance?.ForceSurveillanceTarget(roomId, seconds);
    }
}
