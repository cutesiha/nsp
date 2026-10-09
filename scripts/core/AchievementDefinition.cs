using System.Collections.Generic;
using System.Linq;

namespace NSP.Core;

// 도전과제 한 건의 정의. **여기에 판정 규칙은 없다** — 이름 · 설명 · 숨김 여부뿐이다.
// 실제 해금은 AchievementManager 가 게임의 기존 이벤트를 듣고 한 번씩 내린다.
//
// Id 는 저장 파일(user://achievements_v1.cfg)에 그대로 들어간다 — **절대 바꾸지 않는다.**
// 표시 번호(No)는 기록실 정렬용이라 바꿔도 저장 데이터와 무관하다.
public sealed class AchievementDefinition
{
    public string Id = "";
    public int No;
    public string Name = "";
    // 기록실과 달성 팝업에 같이 뜨는 한 줄. "…하세요." 로 끝나는 조건문이다.
    public string Condition = "";
    // 달성 전에는 이름과 설명을 감춘다(???). 달성하면 보통 도전과제와 똑같이 보인다.
    public bool Hidden;
    // 아이콘 칸에 그릴 한 글자. 분류마다 다르다(A 기본 ◇ · B 실수 △ · C 직원 ○ · D 평가 ★ · E 특수 ◆).
    public string Glyph = "◇";
}

// 30개의 목록. 이 파일이 유일한 출처다 — 화면도 저장도 전부 여기를 읽는다.
public static class Achievements
{
    // ── A. 기본 플레이 / 관리자 업무 ─────────────────────────────────
    public const string FirstLaunch = "first_launch";
    public const string TutorialComplete = "tutorial_complete";
    public const string FirstShift = "first_shift";
    public const string OutgoingCall = "outgoing_call";
    public const string FirstLog = "first_log";
    public const string FirstInterview = "first_interview";

    // ── B. 실수 / 시설 관리 ──────────────────────────────────────────
    public const string MazeFailOnce = "maze_fail_once";
    public const string MazeFailThree = "maze_fail_three";
    public const string RepairRequestIgnored = "repair_request_ignored";
    public const string FirstFaint = "first_faint";
    public const string WarningNeglected = "warning_neglected";
    public const string PowerRestored = "power_restored";
    public const string FirstFacilityRepair = "first_facility_repair";
    public const string SafeShift = "safe_shift";

    // ── C. 직원 / 이상현상 / 추리 ────────────────────────────────────
    public const string MissedCallOnce = "missed_call_once";
    public const string MissedCallThree = "missed_call_three";
    public const string GhostIgnored = "ghost_ignored";
    public const string GhostDispelled = "ghost_dispelled";
    public const string SuspiciousCctv = "suspicious_cctv";
    public const string FirstEvidence = "first_evidence";
    public const string FirstContradiction = "first_contradiction";
    public const string FirstIsolation = "first_isolation";
    public const string InnocentIsolation = "innocent_isolation";
    public const string TalkToSix = "talk_to_six";

    // ── D. 근무 평가 ────────────────────────────────────────────────
    public const string GradeS = "grade_s";
    public const string GradeA = "grade_a";
    public const string GradeLowest = "grade_lowest";

    // ── E. 특수 / 엔딩 ──────────────────────────────────────────────
    public const string TutorialCore100 = "tutorial_core_100";
    public const string TrueEnding = "true_ending";
    public const string BadEnding = "bad_ending";

    // ── 누적 카운터(영구 저장) ───────────────────────────────────────
    public const string CounterMazeFail = "maze_fail";
    public const string CounterMissedCall = "missed_call";

    public const int MazeFailTarget = 3;
    public const int MissedCallTarget = 3;
    // 한 회차에서 이만큼의 서로 다른 직원과 이야기하면 「여섯 명의 이야기」.
    public const int TalkTarget = 6;

    private static readonly List<AchievementDefinition> _all = new();

    public static IReadOnlyList<AchievementDefinition> All
    {
        get { Build(); return _all; }
    }

    public static int Total => All.Count;

    public static AchievementDefinition Get(string id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(a => a.Id == id);

    private static void Build()
    {
        if (_all.Count > 0) return;

        void Add(string id, string name, string condition, string glyph, bool hidden = false) =>
            _all.Add(new AchievementDefinition
            {
                Id = id, No = _all.Count + 1, Name = name,
                Condition = condition, Glyph = glyph, Hidden = hidden,
            });

        // A. 기본 플레이 / 관리자 업무
        Add(FirstLaunch, "업무를 시작합니다", "시설 관리 단말기를 처음 켜세요.", "◇");
        Add(TutorialComplete, "초급 관리자", "가상 시뮬레이션 교육을 끝까지 마치세요.", "◇");
        Add(FirstShift, "첫 야근", "DAY 1 근무를 정상적으로 종료하세요.", "◇");
        Add(OutgoingCall, "상냥한 관리자", "직원에게 직접 전화를 걸어 통화하세요.", "◇");
        Add(FirstLog, "기록은 남는다", "시설 로그를 열람하세요.", "◇");
        Add(FirstInterview, "취조를 시작하지", "휴게시간에 직원을 심문하세요.", "◇");

        // B. 실수 / 시설 관리
        Add(MazeFailOnce, "하찮은 관리자!", "수리 승인 퍼즐에 실패하세요.", "△");
        Add(MazeFailThree, "부러진 손가락", "수리 승인 퍼즐에 누적 3회 실패하세요.", "△");
        Add(RepairRequestIgnored, "모른 척하기도 업무입니다", "수리 승인 요청을 거절하거나 방치하세요.", "△");
        Add(FirstFaint, "부주의한 관리자", "근무 중 직원을 기절까지 몰아붙이세요.", "△");
        Add(WarningNeglected, "경고는 장식인가요?", "사전 경고를 방치해 설비를 고장 내세요.", "△");
        Add(PowerRestored, "다시 켜진 불빛", "발전기 사고로 줄어든 전력을 수리로 복구하세요.", "△");
        Add(FirstFacilityRepair, "고쳐 쓰면 그만", "고장 난 작업실 설비를 수리 완료하세요.", "△");
        Add(SafeShift, "오늘은 무사히", "사망도 기절도 없이 하루 근무를 마치세요.", "△");

        // C. 직원 / 이상현상 / 추리
        Add(MissedCallOnce, "무심한 관리자", "직원의 전화를 받지 않거나 거절하세요.", "○");
        Add(MissedCallThree, "차가운 관리자", "직원의 전화를 누적 3회 놓치세요.", "○");
        Add(GhostIgnored, "시크한 관리자", "이상 개체를 끝까지 관측하지 않아 사고를 내세요.", "○");
        Add(GhostDispelled, "눈 마주쳤다", "CCTV로 이상 개체를 끝까지 관측해 소멸시키세요.", "○");
        Add(SuspiciousCctv, "누군가의 흔적", "CCTV로 직원의 수상한 행동을 직접 포착하세요.", "○", hidden: true);
        Add(FirstEvidence, "증거는 말한다", "조사 자료를 근거로 심문 질문을 하세요.", "○");
        Add(FirstContradiction, "말이 안 맞는데?", "자료 두 장으로 유효한 모순을 성립시키세요.", "○");
        Add(FirstIsolation, "격리 조치", "직원을 격리실에 수용하세요.", "○");
        Add(InnocentIsolation, "억울한 격리", "결백한 직원을 격리하세요.", "○", hidden: true);
        Add(TalkToSix, "여섯 명의 이야기", "한 회차에서 여섯 직원 모두와 대화하세요.", "○");

        // D. 근무 평가
        Add(GradeS, "완벽한 근무!", "최종 관리자 평가 S등급을 받으세요.", "★");
        Add(GradeA, "이 구역의 에이스", "최종 관리자 평가 A등급을 받으세요.", "★");
        Add(GradeLowest, "쓸모없는 관리자", "최종 관리자 평가 D등급을 받으세요.", "★", hidden: true);

        // E. 특수 / 엔딩
        Add(TutorialCore100, "이게 연습이라고?", "가상 시뮬레이션에서 봉쇄 코어를 100% 복구하세요.", "◆", hidden: true);
        Add(TrueEnding, "드디어 퇴근입니다", "트루 엔딩에 도달하세요.", "◆", hidden: true);
        Add(BadEnding, "잘못된 판단", "배드 엔딩에 도달하세요.", "◆", hidden: true);
    }
}
