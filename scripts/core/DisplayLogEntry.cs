using NSP.Data;

namespace NSP.Core;

// 화면(시설 로그 창)에 보이는 한 줄. EventLog 의 원본 기록과 별개이며,
// FacilityLogFormatter 가 원본을 해석해 만들어 낸다. 저장되지 않는 표시 전용 데이터다.
public enum DisplayLogSeverity
{
    Normal,   // 운영 기록 — 최초 배치, 격리, 순찰, 상황 안내
    Move,     // 직원 이동 — 어디서 어디로 갔는가(추리의 뼈대)
    Warning,  // 주의 — 지금 대응하면 막을 수 있는 경고, 자재 부족
    Critical, // 위험 — 실제 고장, 금기 위반, 전력 손실, 사망
    Sabotage, // 방해공작 — 다른 무엇보다 먼저 눈에 들어와야 한다
    Recovery, // 복구 / 안정화 완료
    // 작업실이 제 일을 해낸 순간 — 그 방의 RoomDef.MapColor 로 쓴다.
    // 경고(주황)·사고(빨강)와 색이 겹치지 않아야 눈으로 갈라진다.
    RoomEffect,
}

public sealed class DisplayLogEntry
{
    public float Timestamp;
    public string Text = "";
    public DisplayLogSeverity Severity = DisplayLogSeverity.Normal;
    // 이 줄이 특정 직원의 행동인 경우에만 채워진다. 채워져 있으면 그 직원의 고유색으로 그린다.
    public string RelatedEmployeeId = "";
    public LogEventType SourceEventType;

    // 이 줄이 직원 이동이면 채워진다. 꼬리질문 시스템이 "플레이어가 실제로 본 이동"을
    // 문장 해석 없이 알아내기 위한 값이다.
    public string FromRoomId = "";
    public string ToRoomId = "";
    // 관리자가 지시한 이동인가(false = 직원이 스스로 움직인 것으로 보인다).
    public bool PlayerOrdered;

    // 이 줄이 특정 작업실의 효과면 그 방 id. 화면이 RoomDef.MapColor 를 찾는 데만 쓴다.
    public string RoomId = "";
}
