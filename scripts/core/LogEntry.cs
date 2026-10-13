using System.Collections.Generic;
using NSP.Data;

namespace NSP.Core;

public class LogEntry
{
    public int Day;
    public float GameTimeSeconds;
    public LogEventType EventType;
    public string ActorEmployeeId = "";
    public string RoomId = "";
    public string Description = "";
    // RoomEnter/RoomExit 전용. 목적지로 가는 길에 잠시 지나친 방인가.
    // 원본 기록에는 그대로 남지만, 플레이어용 시설 로그는 이 줄을 이동으로 세지 않는다
    // (경유지를 "그 방에 있었다"로 읽으면 심문 근거가 통째로 틀어진다).
    public bool PassingThrough;
    // 그 순간 관리자가 **관측할 수 없었던** 기록인가(조명이 꺼져 위치 추적이 끊긴 동안의 이동 등).
    //
    // 원본 기록에는 그대로 남는다 — 내부 시뮬레이션의 진실이고, 같은 방 직원의 기억과
    // 휴게시간 증언은 이것을 근거로 삼는다. 다만 **플레이어용 시설 로그에는 줄이 뜨지 않는다**:
    // 관리자는 그 시간 동안 아무것도 못 봤으므로, 자동으로 알게 되면 안 된다(지시서 §2 · §9-5).
    public bool Covert;
    // 세부 종류(기절 · 회복 · 배치 해제). 판정은 Description 문구가 아니라 이 값으로 한다.
    public LogDetail Detail = LogDetail.None;
    public List<string> WitnessEmployeeIds = new();
}
