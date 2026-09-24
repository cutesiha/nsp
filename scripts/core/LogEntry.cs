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
    // 세부 종류(기절 · 회복 · 배치 해제). 판정은 Description 문구가 아니라 이 값으로 한다.
    public LogDetail Detail = LogDetail.None;
    public List<string> WitnessEmployeeIds = new();
}
