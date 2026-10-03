using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;

namespace NSP.Facility;

// 격리 명령을 받은 직원이 **격리실로 출발하기까지**의 처리.
//
// 침대 앞 정지 · 앉기 · 눕기 · 압박밴드 체결 · 발버둥 · 해제 순서는 이미
// RoomWorkVisualController.Isolation.cs 가 CCTV 쪽에서 전부 가지고 있다(IsoPhase).
// 여기서 그것을 다시 만들지 않는다. 여기가 맡는 것은 그 앞의 세 가지다.
//
//   ① 명령을 받은 순간 그 자리에서 캐릭터별로 반응한다 — 바로 걷지 않는다(§6~§12)
//   ② 반응이 끝나면 **본인이** 격리실로 걸어간다(기절과 달리 아무도 끌고 가지 않는다)
//   ③ 아직 격리실에 닿기 전이면 취소할 수 있고, 도중에 쓰러지면 격리가 취소된다(§34 · §35)
//
// 캐릭터별 발버둥 강도(§25)도 여기서 정하고 CCTV 가 읽어 간다.
public enum IsolationPhase
{
    None,
    React,     // 명령을 듣고 그 자리에서 반응하는 중 — 아직 한 발도 떼지 않았다
    Walking,   // 스스로 격리실로 걸어가는 중
    InRoom,    // 격리실에 들어섰다 — 침대 절차는 CCTV(IsoPhase)가 이어받는다
}

public sealed class IsolationSystem
{
    // 명령을 듣고 움직이기 시작할 때까지의 시간(§6 — 0.8~2.0초). 캐릭터마다 다르다.
    //   양이 가장 오래 머뭇거리고(§7), 여우가 가장 태연하게 바로 움직인다(§12).
    public static float ReactSeconds(string employeeId) => employeeId switch
    {
        "sheep" => 1.9f,    // 굳었다가 한 발 가려다 멈춘다
        "dog" => 1.5f,      // 풀이 죽어 잠깐 망설인다
        "rabbit" => 1.35f,  // 크게 당황하고 뒤를 한 번 확인한다
        "cat" => 1.1f,      // 짜증 섞인 한숨 한 번
        "wolf" => 0.9f,     // 고개를 한 번 끄덕이고 바로
        _ => 0.8f,          // fox — 어깨 한 번 으쓱하고 만다
    };

    // 구속된 뒤 얼마나 발버둥치는가(§25). 0 = 거의 가만히, 1 = 계속 몸부림.
    public static float StruggleOf(string employeeId) => employeeId switch
    {
        "sheep" => 1.00f,   // 쉽게 진정되지 않는다
        "rabbit" => 0.85f,  // 처음 크게, 뒤로 갈수록 잦아든다
        "dog" => 0.45f,
        "wolf" => 0.40f,    // 한 번 세게 힘줘 보고 그 뒤 침착
        "cat" => 0.35f,
        _ => 0.20f,         // fox — 스트랩을 한 번 확인하고 거의 누워 있다
    };

    // 격리실까지 걸어가는 걸음걸이(§13) — 1.0 이 평소 속도다.
    //   양은 발이 잘 떨어지지 않고, 늑대는 평소와 다르지 않게 성큼성큼 간다.
    public static float WalkSpeedScale(string employeeId) => employeeId switch
    {
        "sheep" => 0.72f,
        "dog" => 0.82f,
        "rabbit" => 0.90f,
        "cat" => 0.95f,
        "wolf" => 1.05f,
        _ => 1.00f,     // fox
    };

    // 걸어가는 동안의 떨림(§14). 겁이 많은 쪽만 몸이 떨린다.
    public static float WalkTremble(string employeeId) => employeeId switch
    {
        "sheep" => 0.55f,
        "rabbit" => 0.30f,
        "dog" => 0.15f,
        _ => 0f,
    };

    // 처음에 크게 몰아치고 잦아드는 쪽인가. 양은 반대로 근무 내내 불안하다.
    public static bool SettlesDown(string employeeId) => employeeId is "rabbit" or "wolf" or "dog";

    // 화면이 듣는 신호 — 명령을 받은 순간(캐릭터별 반응 동작을 여기서 시작한다).
    public event Action<string> Ordered;

    private FacilitySimulation _sim;

    public void Attach(FacilitySimulation sim) => _sim = sim;

    public void Reset()
    {
        foreach (string id in _sim?.GetEmployeeIds() ?? new List<string>())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null) continue;
            st.Isolation = IsolationPhase.None;
            st.IsolationPhaseTimer = 0f;
        }
    }

    // ── 명령 ──────────────────────────────────────────────────────────

    // FacilitySimulation.IsolateEmployee 가 부른다. **여기서 걷게 하지 않는다.**
    public void OnOrdered(EmployeeState emp)
    {
        emp.Isolation = IsolationPhase.React;
        emp.IsolationPhaseTimer = 0f;
        // 하던 일을 그 자리에서 멈춘다. 걷기는 반응이 끝난 뒤다.
        emp.IsMoving = false;
        emp.PathQueue.Clear();
        emp.TargetRoomId = emp.CurrentRoomId;
        emp.ElbowWaypoint = null;
        Ordered?.Invoke(emp.EmployeeId);
    }

    // ── 매 틱 ─────────────────────────────────────────────────────────

    public void Tick(float delta)
    {
        if (_sim == null) return;
        foreach (string id in _sim.GetEmployeeIds())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null || st.Isolation == IsolationPhase.None) continue;

            // 기절이 격리보다 우선이다(§35). 걸어가던 중에 쓰러지면 격리는 없던 일이 된다.
            // 이미 격리실에 들어간 뒤라면 그대로 둔다 — 제 발로 의무실에 가게 하지 않는다.
            if (st.Incapacitated && st.Isolation != IsolationPhase.InRoom)
            {
                st.Isolation = IsolationPhase.None;
                st.IsolationPhaseTimer = 0f;
                _sim.AbortIsolation(st);
                continue;
            }

            st.IsolationPhaseTimer += delta;
            switch (st.Isolation)
            {
                case IsolationPhase.React:
                    // 반응이 끝나면 비로소 스스로 걸어간다.
                    if (st.IsolationPhaseTimer < ReactSeconds(id)) break;
                    st.Isolation = IsolationPhase.Walking;
                    st.IsolationPhaseTimer = 0f;
                    // 길을 못 찾는 경우(격리실이 없는 테스트 씬 등)에는 걷는 단계에 갇히지
                    // 않게 바로 수용된 것으로 본다. 걷지도 못하는 채로 남겨 두면 화면에서
                    // 원래 작업실에 계속 서 있게 된다.
                    if (!_sim.SendToIsolationRoom(id)) Arrive(st);
                    break;

                case IsolationPhase.Walking:
                    // 제 발로 들어섰다 — 침대 앞 정지 · 앉기 · 눕기 · 밴드 체결은 CCTV 가 이어받는다.
                    if (!st.IsMoving && st.CurrentRoomId == FacilitySimulation.IsolationRoomIdPublic) Arrive(st);
                    break;
            }
        }
    }

    private void Arrive(EmployeeState st)
    {
        st.Isolation = IsolationPhase.InRoom;
        st.IsolationPhaseTimer = 0f;
        EventLog.Instance?.LogEvent(NSP.Data.LogEventType.Isolation, st.EmployeeId,
            FacilitySimulation.IsolationRoomIdPublic, $"{Name(st.EmployeeId)} - 격리실 수용");
    }

    // 격리가 풀렸다 — 단계를 지운다(침대에서 일어나는 표현은 CCTV 가 마저 그린다).
    public void OnReleased(EmployeeState emp)
    {
        emp.Isolation = IsolationPhase.None;
        emp.IsolationPhaseTimer = 0f;
    }

    // ── 조회 ──────────────────────────────────────────────────────────

    // 아직 격리실에 닿기 전인가 — 이 구간은 취소해도 안전하다(§34).
    public static bool EnRoute(EmployeeState st) =>
        st != null && st.Isolation is IsolationPhase.React or IsolationPhase.Walking;

    // 그 자리에서 반응하는 중인가(아직 걷지 않는다).
    public static bool Reacting(EmployeeState st) => st is { Isolation: IsolationPhase.React };

    private string Name(string id) => _sim?.GetEmployeeDef(id)?.Codename ?? id;
}
