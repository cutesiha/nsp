using System.Collections.Generic;

namespace NSP.Core;

// 사고 정보를 플레이어에게 보여주기 위한 표시용 구조. 시뮬레이션의 진실 데이터가 아니라
// 그것을 읽어 정리한 결과다(경고 단말기 / 미니맵이 같은 값을 본다).
public enum IncidentState
{
    Caution,   // 위험 조건이 쌓이기 시작함
    Warning,   // 사고 임박 — 카운트다운 진행
    Active,    // 실제로 사고가 발생해 아직 해결되지 않음
    Resolved,  // 해결됨(최근 기록)
}

public sealed class IncidentDisplayData
{
    public string IncidentId = "";
    public string RoomId = "";
    public string Title = "";
    public IncidentState State;

    // 왜 이렇게 됐는가. 방해공작은 원인을 "판별 불가"로만 표시한다.
    public string CauseText = "";
    // 사고까지 남은 시간(초). 음수면 카운트다운 없음.
    public float WarningRemainingSeconds = -1f;
    // 방치했을 때 / 이미 발생했을 때의 결과. Root 사고 하나에 파생 결과가 모인다.
    public readonly List<string> ConsequenceLines = new();
    // 지금 무엇을 할 수 있는가. 판단은 플레이어가 한다 — 누구를 보내라고 지시하지 않는다.
    public string ActionHint = "";

    public AlertSeverity Severity;
    // 이 항목이 다른 사고의 파생 결과라면 그 사고의 id.
    public string ParentIncidentId = "";
    public float StartedAt;
    public float ResolvedAt = -1f;
    // 수리에 필요한 인원(발전실·코어실은 2명).
    public int RepairWorkers = 1;

    // 사고가 아니라 "운영 상태"(무인 페널티)임을 구분한다.
    public bool IsOperational;
    // 사고가 아니라 "금기 위반 위험"임을 구분한다.
    public bool IsProtocol;
}
