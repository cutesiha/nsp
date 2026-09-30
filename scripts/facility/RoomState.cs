using System.Collections.Generic;

namespace NSP.Facility;

public class RoomState
{
    public string RoomId;
    public bool PowerOn = true;
    public bool Locked = false;
    public bool RedAlertLighting = false;
    public bool CctvDisconnected = false;
    // 방해공작으로 일시 차단된 CCTV. 근무 시계(DayTimeSeconds) 기준의 해제 시각이며,
    // 설비 고장(CctvDisconnected)과 달리 시간이 지나면 저절로 풀린다.
    public float CctvBlockedUntil = 0f;
    // 설비 수치가 잠깐 흔들리는 구간(근무 시계 기준 해제 시각). 고장이 아니라 "이상 징후"다 —
    // 진행도가 잠시 멈추거나 출력이 살짝 내려앉는 정도이며 로그에도 남지 않는다.
    public float MicroFaultUntil = 0f;
    // 그 방에서 누군가 설비 쪽으로 다가간 순간(근무 시계 기준). CCTV 화면에서만 보인다.
    public float SuspiciousActionUntil = 0f;
    public string SuspiciousActorId = "";
    public bool InfoDistorted = false;
    public List<string> OccupantEmployeeIds = new();

    public List<string> TaskPriorityOrder = new();
    public Dictionary<string, float> TaskGauges = new();
    public float NeglectTimer = 0f;
    // 근무자가 한 명도 없는 상태가 이어진 시간(초). RoomDef 의 사고 발생 시간에 도달하면
    // 그 방의 사고가 터지고 0으로 돌아간다.
    public float UnstaffedTimer = 0f;
    // 근무자가 다시 들어와 머문 시간(초). Config.UnstaffedClearSeconds 를 채워야 경고가 풀린다 —
    // 문만 열고 지나가는 것으로는 해제되지 않는다(G-1). 방이 다시 비면 0으로 돌아간다.
    public float UnstaffedClearTimer = 0f;
    public Dictionary<string, float> TabooHoldTimers = new();
    // 금기 판정이 "구성이 바뀌었는지"를 보기 위해 들고 있는 비교용 키(예: 방 인원 목록).
    public Dictionary<string, string> TabooWatchKeys = new();
}
