using NSP.Data;

namespace NSP.Dialogue;

// 휴게시간 심문에서 플레이어가 손에 쥐고 있는 "조사 자료" 한 장.
//
// 이 자료는 전부 PlayerKnownEvidence / 시설 로그 화면(FacilityLogFormatter) 에서만 나온다.
// EventLog 원본(SystemTruth)을 여기로 들여오면 안 된다 — 플레이어가 보지 못한 사실을
// 질문으로 만들어 주는 순간 추리가 사라진다.
public enum EvidenceKind
{
    Movement,       // 시설 로그: 이 직원이 어디서 어디로 이동했다
    Incident,       // 시설 로그: 사고가 났다
    Cctv,           // 관리자가 CCTV 로 직접 본 장면
    Testimony,      // 다른 직원이 이 직원에 대해 한 진술
    OwnStatement,   // 이 직원이 관리자에게 스스로 한 진술
    Mood,           // 근무 전 자가보고 — 오늘의 기분
}

// 자료 한 장이 "그 직원이 언제 어디 있었는가"를 주장하는가.
// 모순 판정은 이 세 값(직원 · 시각 · 작업실)만 비교한다. 문자열 비교를 하지 않는다.
public enum PositionClaim
{
    None,       // 위치를 말하지 않는 자료(기분 등)
    AtRoom,     // 그 시각 그 방에 있었다
    Arrived,    // 그 시각 그 방에 도착했다(그 전에는 다른 방)
}

public sealed class InterviewEvidence
{
    // 화면/질문/테스트가 자료를 가리키는 고유 키. 같은 자료는 같은 Id 로 다시 만들어진다.
    public string Id = "";
    public EvidenceKind Kind;
    // 어느 근무의 자료인가. 조사 자료는 항상 오늘 것만 모은다.
    public int Day = 1;

    // 이 자료가 플레이어에게 실제로 보인 적이 있는가.
    // 조사 자료에 들어오는 순간 항상 true 다 — false 인 자료를 만들지 않는 것이
    // 이 시스템의 유일한 규칙이고, 값은 그 규칙을 테스트가 확인할 수 있게 남겨 둔다.
    public bool PlayerObserved = true;

    // 이 자료에 함께 등장하는 직원들(같은 방에 있던 인원, 증언자 등).
    public readonly System.Collections.Generic.List<string> RelatedEmployeeIds = new();

    // --- 카드 표시 ---------------------------------------------------
    public string Header = "";      // "시설 로그" / "CCTV" / "늑대의 증언"
    public string TimeText = "";    // "22:13" (없으면 빈 값)
    public string Body = "";        // "저장고 → 정비실"

    // --- 의미 --------------------------------------------------------
    // 이 자료가 다루는 직원. 인터뷰 대상과 같을 때만 그 사람에게 물을 수 있다.
    public string SubjectEmployeeId = "";
    // 증언 자료에서 그 말을 한 사람.
    public string SpeakerEmployeeId = "";

    // 기준 시각(근무 시작 = 0초). HasTime 이 false 면 시간을 근거로 쓸 수 없다.
    public float AnchorTime;
    public bool HasTime;

    public PositionClaim Position = PositionClaim.None;
    public string FromRoomId = "";
    public string ToRoomId = "";
    // 이 자료가 가리키는 작업실(위치 주장의 대상). Movement 면 ToRoomId 와 같다.
    public string SubjectRoomId = "";

    // 사고 자료일 때만.
    public string IncidentKey = "";
    public LogEventType IncidentType;

    // 이동이 관리자의 지시였는가(시설 로그 화면에 그렇게 떴는가).
    public bool PlayerOrdered;

    // 기분 자료일 때의 문구.
    public string MoodText = "";

    // 모순 판정에 쓸 수 있는 자료인가 — 직원과 시각과 위치가 모두 있어야 한다.
    public bool CanAnchorPosition =>
        Position != PositionClaim.None && HasTime && !string.IsNullOrEmpty(SubjectRoomId);

    // 카드에 찍히는 한 줄. 종류를 두 글자로 앞에 달아 훑어보기 쉽게 한다.
    //   기록  22:13  저장고 → 정비실
    //   증언  22:16  늑대 · 정비실에서 봤다
    public string Tag => Kind switch
    {
        EvidenceKind.Movement => "기록",
        EvidenceKind.Incident => "사고",
        EvidenceKind.Cctv => "CCTV",
        EvidenceKind.Testimony => "증언",
        EvidenceKind.OwnStatement => "진술",
        _ => "기분",
    };

    public string OneLine =>
        $"{Tag}  {(string.IsNullOrEmpty(TimeText) ? "" : TimeText + "  ")}{Body}";
}
