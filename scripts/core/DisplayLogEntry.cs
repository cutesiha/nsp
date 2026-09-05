using NSP.Data;

namespace NSP.Core;

// 화면(시설 로그 창)에 보이는 한 줄. EventLog 의 원본 기록과 별개이며,
// FacilityLogFormatter 가 원본을 해석해 만들어 낸다. 저장되지 않는 표시 전용 데이터다.
public enum DisplayLogSeverity
{
    Normal,   // 운영 기록 — 재배치, 격리, 상황 안내
    Warning,  // 주의 — 사고 예고, 설비 이상, 자재 부족, 목격
    Critical, // 위험 — 금기 위반, 전력 손실, 사망, 업무 불능
    Recovery, // 복구 완료
}

public sealed class DisplayLogEntry
{
    public float Timestamp;
    public string Text = "";
    public DisplayLogSeverity Severity = DisplayLogSeverity.Normal;
    // 이 줄이 특정 직원의 행동인 경우에만 채워진다. 채워져 있으면 그 직원의 고유색으로 그린다.
    public string RelatedEmployeeId = "";
    public LogEventType SourceEventType;
}
