using System.Linq;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

public enum ConcealKind
{
    None,
    ProtectOther,   // 소중한 사람에게 불리한 목격을 먼저 말하지 않는다
    OwnMistake,     // 자기 실수를 축소한다
    Unauthorized,   // 지시 없는 이동 · 업무 태만을 가볍게 넘긴다
    Sabotage,       // 방해공작을 은폐한다 (결번 개체)
}

// 직원이 **무언가를 숨길 만한 실제 이유**가 있는가.
//
// 이 파일에서 가장 중요한 것은 하지 않는 일이다.
//
//   · 거짓말을 강제로 만들지 않는다. 숨길 **실제 사건**이 있을 때만 성립한다 —
//     토끼가 사고를 낸 적도 없는데 실수를 숨기는 대사를 만들면 안 된다(기획안 §1).
//   · 관계 수치만으로 없는 목격을 만들지 않는다. 목격 자체는 늘 기록에서 온다.
//   · 숨김은 **말하는 방식**만 바꾼다. 사실은 그대로 남아 있고, 다른 질문으로
//     반드시 되찾을 수 있어야 한다("그곳에 있던 직원을 모두 말해 주십시오").
//
// 그래서 "거짓말의 유무"와 "범인 여부"가 절대 같아지지 않는다. 정상 직원도 숨기고,
// 결번도 평범하게 답한다. 플레이어가 실제 행동과 증거를 맞대어 판단해야 한다.
public sealed class Concealment
{
    public ConcealKind Kind = ConcealKind.None;
    // 보호하려는 상대(ProtectOther 일 때만).
    public string AboutEmployeeId = "";
    // 왜 성립했는가 — 검증과 디버그용이다. **화면에 띄우지 않는다.**
    // 숨기는 동기를 플레이어에게 알려 주면 추리가 사라진다(기획안 §1).
    public string Why = "";

    public bool Any => Kind != ConcealKind.None;

    public static readonly Concealment No = new();
}

public static class ConcealmentMotive
{
    // 이 이상 감싸면 "불리한 목격을 먼저 말하지 않는다". 연인(0.9)·짝사랑(0.8)·
    // 무서워하는 상대(+0.25)가 여기 걸리고, 평범한 동료(0.3~0.4)는 걸리지 않는다.
    public const float ProtectThreshold = 0.6f;

    // 이 직원이 **저 사람에 대한 불리한 목격**을 먼저 꺼내지 않는가.
    //
    // 실제로 그 목격이 있을 때만 의미가 있다 — 이 함수는 성향만 본다.
    public static bool ShieldsFrom(string employeeId, string aboutId)
    {
        if (string.IsNullOrEmpty(employeeId) || string.IsNullOrEmpty(aboutId)
            || employeeId == aboutId) return false;
        return RelationshipSystem.ReportBias(employeeId, aboutId) >= ProtectThreshold;
    }

    // 지금 이 대화에서 이 직원이 숨길 만한 것이 실제로 있는가.
    public static Concealment For(DialogueContext ctx)
    {
        if (ctx == null || string.IsNullOrEmpty(ctx.EmployeeId)) return Concealment.No;
        string id = ctx.EmployeeId;
        int day = ctx.SubjectDay > 0 ? ctx.SubjectDay : ctx.CurrentDay;

        // ① 방해공작 — 결번 개체. 기존 판정을 그대로 쓴다.
        if (ctx.IsSaboteur && ctx.IsSubjectActor)
            return new Concealment { Kind = ConcealKind.Sabotage, Why = "방해공작 당사자" };

        // ② 소중한 사람을 감싼다 — **그 사람의 불리한 행동을 실제로 봤을 때만.**
        string seen = ctx.KnownSuspiciousActorId;
        if (!string.IsNullOrEmpty(seen) && ShieldsFrom(id, seen))
            return new Concealment
            {
                Kind = ConcealKind.ProtectOther,
                AboutEmployeeId = seen,
                Why = $"{seen} 의 수상한 행동을 봤고 감싸는 사이",
            };

        // ③ 자기 실수 — 오늘 이 직원이 실제로 낸 사고가 있을 때만.
        if (CausedIncident(id, day))
            return new Concealment { Kind = ConcealKind.OwnMistake, Why = "본인이 낸 사고가 있다" };

        // ④ 무단 이동 · 업무 태만 — 지시 없는 이동 기록이 실제로 있을 때만.
        if (MovedWithoutOrder(id, day))
            return new Concealment { Kind = ConcealKind.Unauthorized, Why = "지시 없는 이동이 있다" };

        return Concealment.No;
    }

    // 오늘 이 직원이 **행위자로 기록된** 사고가 있는가.
    private static bool CausedIncident(string id, int day) =>
        EventLog.Instance?.GetAllEntries().Any(e => e.Day == day
            && e.ActorEmployeeId == id
            && e.EventType is LogEventType.TaskFailed or LogEventType.Sabotage) ?? false;

    // 관리자 지시 없이 옮긴 이동이 **시설 로그 화면에 떴는가**.
    // 플레이어가 보지 못한 이동으로 은폐 동기를 만들지 않는다.
    private static bool MovedWithoutOrder(string id, int day) =>
        PlayerKnownEvidence.VisibleMoves(id, day).Any(m => !m.PlayerOrdered);
}
