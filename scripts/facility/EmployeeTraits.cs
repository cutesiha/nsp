using System.Collections.Generic;

namespace NSP.Facility;

// 직원 한 명의 "평소 행동 방식".
//
// 능력치가 아니다. 플레이어에게 숫자로 공개하지 않으며, 성공/실패 판정에도 쓰지 않는다.
// 오직 두 가지에만 쓴다.
//   ① 근무 중 이 직원이 자리를 뜰 만한가 / 사건 현장으로 갈까 피할까 (EmployeeBehaviorSystem)
//   ② 옆 방에서 벌어진 일을 알아차리는가 / 본 것을 얼마나 정확히 말하는가 (대사 계층)
//
// 말투·정보량은 이미 DialogueVoiceProfiles 가 쥐고 있다. 여기 있는 것은 "행동" 쪽이며,
// 두 표가 같은 사람을 서로 다르게 그리지 않도록 StatementPrecision 만 겹쳐 둔다.
//
// 0 = 거의 그러지 않는다 · 1 = 가끔 · 2 = 자주 · 3 = 뚜렷하게 그렇다
public sealed class EmployeeTraitProfile
{
    public string EmployeeId = "";

    // 배치된 자리를 얼마나 쉽게 뜨는가. 로그에 이동 기록이 남는 빈도로 나타난다.
    public int MovementTendency;
    // 이상한 일이 생기면 직접 확인하러 가는가.
    public int Curiosity;
    // 본 것을 관리자에게 알리려 하는가(전화·보고 성향).
    public int ReportsAnomaly;
    // 위험해 보이는 장소에서 물러나는가.
    public int AvoidsDanger;
    // 같은 방이 아니어도 무슨 일이 났는지 알아차리는가.
    // 이 값이 낮으면 옆 방 사건을 "몰랐다"고 답한다 — 없는 목격을 만들지 않기 위한 문지기다.
    public int ObservationalAwareness;
    // 용건 없이 다른 직원 쪽으로 움직이는가.
    public int SocialMovement;
    // 시각·위치를 얼마나 정확히 진술하는가(DialogueVoiceProfiles 의 시간 표현과 같은 방향).
    public int StatementPrecision;

    public string Note = "";
}

public static class EmployeeTraits
{
    // 옆 방의 사건을 알아차리려면 이 이상이어야 한다.
    public const int AwarenessForIndirect = 2;
    // 같은 방에 있었다고 해서 모두가 설비 쪽을 보고 있는 것은 아니다.
    public const int AwarenessForWitness = 2;

    private static readonly Dictionary<string, EmployeeTraitProfile> _profiles = Build();

    public static EmployeeTraitProfile Get(string employeeId) =>
        _profiles.GetValueOrDefault(employeeId) ?? _fallback;

    public static IReadOnlyDictionary<string, EmployeeTraitProfile> All => _profiles;

    private static readonly EmployeeTraitProfile _fallback = new()
    {
        EmployeeId = "",
        MovementTendency = 1,
        Curiosity = 1,
        ReportsAnomaly = 1,
        AvoidsDanger = 1,
        ObservationalAwareness = 2,
        SocialMovement = 1,
        StatementPrecision = 2,
        Note = "정의되지 않은 직원",
    };

    private static Dictionary<string, EmployeeTraitProfile> Build() => new()
    {
        // 원칙적·책임감. 맡은 자리를 쉽게 뜨지 않고, 본 것은 보고한다.
        ["owl"] = new EmployeeTraitProfile
        {
            EmployeeId = "owl",
            MovementTendency = 0,
            Curiosity = 1,
            ReportsAnomaly = 3,
            AvoidsDanger = 1,
            ObservationalAwareness = 2,
            SocialMovement = 1,
            StatementPrecision = 3,
            Note = "규칙대로 대응한다 · 본 것과 추측을 구분해 말한다",
        },

        // 예민·효율. 불필요한 이동을 싫어하지만 설비 이상은 누구보다 빨리 알아챈다.
        ["cat"] = new EmployeeTraitProfile
        {
            EmployeeId = "cat",
            MovementTendency = 0,
            Curiosity = 2,
            ReportsAnomaly = 2,
            AvoidsDanger = 1,
            ObservationalAwareness = 3,
            SocialMovement = 0,
            StatementPrecision = 2,
            Note = "이동을 아낀다 · 설비 이상 감지가 빠르다 · 필요한 말만 한다",
        },

        // 소심·과민. 위험한 곳에서 먼저 물러나고, 분위기와 소리를 잘 알아차린다.
        ["jellyfish"] = new EmployeeTraitProfile
        {
            EmployeeId = "jellyfish",
            MovementTendency = 1,
            Curiosity = 0,
            ReportsAnomaly = 1,
            AvoidsDanger = 3,
            ObservationalAwareness = 3,
            SocialMovement = 0,
            StatementPrecision = 1,
            Note = "사고 현장에서 멀어진다 — 그 모습이 도망처럼 보일 수 있다",
        },

        // 활발·즉흥. 이상한 일이 있으면 직접 보러 간다 — 그래서 자주 현장 근처에 있다.
        ["rabbit"] = new EmployeeTraitProfile
        {
            EmployeeId = "rabbit",
            MovementTendency = 3,
            Curiosity = 3,
            ReportsAnomaly = 2,
            AvoidsDanger = 0,
            ObservationalAwareness = 1,
            SocialMovement = 2,
            StatementPrecision = 0,
            Note = "사건 현장으로 직접 간다 · 이동 기록이 가장 많다",
        },

        // 무뚝뚝·침착. 거의 움직이지 않는 대신 주변을 계속 본다.
        ["crow"] = new EmployeeTraitProfile
        {
            EmployeeId = "crow",
            MovementTendency = 0,
            Curiosity = 1,
            ReportsAnomaly = 2,
            AvoidsDanger = 1,
            ObservationalAwareness = 3,
            SocialMovement = 0,
            StatementPrecision = 3,
            Note = "움직이지 않는다 · 시각과 위치를 정확히 기억한다",
        },

        // 능글·사교적. 용건 없이 다른 직원 쪽으로 가는 일이 잦다.
        ["fox"] = new EmployeeTraitProfile
        {
            EmployeeId = "fox",
            MovementTendency = 2,
            Curiosity = 2,
            ReportsAnomaly = 1,
            AvoidsDanger = 1,
            ObservationalAwareness = 2,
            SocialMovement = 3,
            StatementPrecision = 1,
            Note = "여러 방을 오간다 — 사건 근처에 있었던 적이 많아 보인다",
        },
    };
}
