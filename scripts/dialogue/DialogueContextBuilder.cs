using System;
using System.Collections.Generic;
using System.Linq;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 게임의 사실(EventLog / FacilitySimulation / GameState)에서 "이 직원이 알고 있는 것"만
// 뽑아내는 곳. 대사 문장은 여기서 만들지 않는다.
//
// 핵심 규칙
//   · 위치는 항상 로그 재생으로 계산한다. "그날 마지막 위치"를 쓰지 않는다.
//   · 목격 여부는 WitnessEmployeeIds 또는 사건 시각의 실제 위치로만 판단한다.
//   · 인접 작업실(ConnectedRoomIds)에 있었으면 간접 인지까지만 인정한다.
public static class DialogueContextBuilder
{
    public const string PlayerOnlyRoomId = "central_office";

    // --- 위치 타임라인 --------------------------------------------------
    // 로그를 한 번만 훑어 직원별 (시각, 작업실) 구간을 만든다. 방이 비어 있으면 이동 중.
    //
    // 시간표에는 그날 실제로 근무한 직원만 들어간다. 배치되지 않은 직원은 시작실에 서 있어도
    // 근무자가 아니다 — 그 사람을 "같이 있던 사람"으로 대면 없는 사실을 만드는 셈이다.
    private sealed class Timeline
    {
        public int Day;
        public int EntryCount;
        public int ShiftVersion;
        public readonly Dictionary<string, List<(float Time, string Room)>> Moves = new();
        // 근무 중인 구간. 격리 · 사망 · 기절 · 배치 해제로 끊기고, 재배치 · 회복으로 다시 이어진다.
        public readonly Dictionary<string, List<(float Time, bool OnDuty)>> Duty = new();
    }

    private static Timeline _timeline;

    // 이 시각 이전의 배치(Relocation)는 근무 시작 전 배치표다(ShiftMemory 의 Assigned 와 같은 기준).
    public const float ShiftStartSeconds = 0.5f;

    private static Timeline GetTimeline(int day)
    {
        var log = EventLog.Instance;
        var sim = FacilitySimulation.Instance;
        int count = log?.GetAllEntries().Count ?? 0;
        int version = FacilitySimulation.ShiftStartVersion;
        if (_timeline != null && _timeline.Day == day && _timeline.EntryCount == count
            && _timeline.ShiftVersion == version) return _timeline;

        var t = new Timeline { Day = day, EntryCount = count, ShiftVersion = version };
        var today = log?.GetAllEntries().Where(e => e.Day == day).ToList() ?? new List<LogEntry>();
        if (sim != null)
        {
            foreach (string id in sim.GetEmployeeIds())
            {
                // 0초 위치 = 근무 시작 때의 배치. 지금의 배치(재배치 뒤일 수 있다)도,
                // 캐릭터의 기본 시작실도 쓰지 않는다. 모르면 빈 값.
                string seed = ShiftStartRoom(id, day, sim, today);
                // 시작 배치가 없어도 근무 중에 배치됐으면(재배치 · 업무 시작) 그때부터 근무자다.
                bool worked = seed.Length > 0 || today.Any(e => e.ActorEmployeeId == id && StartsDuty(e));
                if (!worked) continue;
                t.Moves[id] = new List<(float, string)> { (0f, seed) };
                t.Duty[id] = new List<(float, bool)> { (0f, seed.Length > 0) };
            }
        }

        foreach (var e in today)
        {
            if (string.IsNullOrEmpty(e.ActorEmployeeId)) continue;
            if (!t.Moves.TryGetValue(e.ActorEmployeeId, out var list)) continue;
            var duty = t.Duty[e.ActorEmployeeId];

            switch (e.EventType)
            {
                // Relocation 은 이동 "명령" 이라 실제 도착이 아니다 — 위치 증거로 쓰지 않는다.
                // 다만 "배치됐다"는 사실이므로 근무는 그때부터다.
                case LogEventType.Relocation:
                    if (!string.IsNullOrEmpty(e.RoomId)) duty.Add((e.GameTimeSeconds, true));
                    break;
                case LogEventType.RoomEnter:
                case LogEventType.TaskStart:
                    if (!string.IsNullOrEmpty(e.RoomId) && e.RoomId != PlayerOnlyRoomId)
                        list.Add((e.GameTimeSeconds, e.RoomId));
                    if (e.EventType == LogEventType.TaskStart) duty.Add((e.GameTimeSeconds, true));
                    break;
                case LogEventType.RoomExit:
                    list.Add((e.GameTimeSeconds, ""));
                    break;
                case LogEventType.Isolation:
                case LogEventType.Death:
                    list.Add((e.GameTimeSeconds, e.RoomId ?? ""));
                    duty.Add((e.GameTimeSeconds, false));
                    break;
                // 기절 · 회복 · 배치 해제는 따로 사건 종류가 없다 — FacilitySimulation 이 남기는 문구로 가린다.
                case LogEventType.Neglect when (e.Description ?? "").Contains("기절"):
                    duty.Add((e.GameTimeSeconds, false));
                    break;
                case LogEventType.Neglect when (e.Description ?? "").Contains("근무 복귀"):
                    duty.Add((e.GameTimeSeconds, true));
                    break;
                case LogEventType.TaskEnd when (e.Description ?? "").Contains("배치 해제"):
                    duty.Add((e.GameTimeSeconds, false));
                    break;
            }
        }

        _timeline = t;
        return t;
    }

    // 근무 시작 때의 배치. 배치표 로그(근무 시작 전 Relocation)가 남아 있으면 그것,
    // 없으면 근무 시작 순간 FacilitySimulation 이 적어 둔 값. 둘 다 없으면 빈 값 — 배치되지 않았다.
    private static string ShiftStartRoom(string id, int day, FacilitySimulation sim, List<LogEntry> today)
    {
        var placed = today.FirstOrDefault(e => e.EventType == LogEventType.Relocation && e.ActorEmployeeId == id
                                               && !string.IsNullOrEmpty(e.RoomId)
                                               && e.GameTimeSeconds <= ShiftStartSeconds);
        if (placed != null) return placed.RoomId;
        var st = sim.GetEmployeeState(id);
        return st != null && st.ShiftStartDay == day ? st.ShiftStartRoomId ?? "" : "";
    }

    private static bool StartsDuty(LogEntry e) =>
        (e.EventType == LogEventType.Relocation && !string.IsNullOrEmpty(e.RoomId))
        || e.EventType == LogEventType.TaskStart;

    // 그 시각에 근무 중이었는가(오늘 근무자이고, 격리 · 사망 · 기절 · 배치 해제 상태가 아니다).
    public static bool OnDutyAt(string employeeId, int day, float timeSeconds)
    {
        var t = GetTimeline(day);
        if (!t.Duty.TryGetValue(employeeId, out var list) || list.Count == 0) return false;
        bool on = list[0].OnDuty;
        foreach (var (time, d) in list)
        {
            if (time > timeSeconds) break;
            on = d;
        }
        return on;
    }

    // 해당 시각에 그 직원이 실제로 있던 작업실. 빈 문자열이면 통로 이동 중이거나 알 수 없음.
    public static string RoomAt(string employeeId, int day, float timeSeconds)
    {
        var t = GetTimeline(day);
        if (!t.Moves.TryGetValue(employeeId, out var list) || list.Count == 0) return "";
        string room = list[0].Room;
        foreach (var (time, r) in list)
        {
            if (time > timeSeconds) break;
            room = r;
        }
        return room;
    }

    // 그 시각 직전에 있던 작업실(현재 방으로 들어오기 전). 없으면 빈 값.
    public static string RoomBefore(string employeeId, int day, float timeSeconds)
    {
        var t = GetTimeline(day);
        if (!t.Moves.TryGetValue(employeeId, out var list) || list.Count == 0) return "";
        string previous = "", current = list[0].Room;
        foreach (var (time, r) in list)
        {
            if (time > timeSeconds) break;
            if (r != current) { previous = current; current = r; }
        }
        return previous;
    }

    // 그 시각 이후 처음으로 옮겨 간 작업실. 끝까지 그 자리에 있었으면 빈 값.
    public static string RoomAfter(string employeeId, int day, float timeSeconds)
    {
        var t = GetTimeline(day);
        if (!t.Moves.TryGetValue(employeeId, out var list) || list.Count == 0) return "";
        string at = RoomAt(employeeId, day, timeSeconds);
        foreach (var (time, r) in list)
            if (time > timeSeconds && !string.IsNullOrEmpty(r) && r != at) return r;
        return "";
    }

    // 그 시각에 해당 작업실에 함께 있던 다른 직원들 — 그때 근무 중이던 사람만
    // (격리 · 기절 · 미배치인 사람은 그 방에 서 있어도 "같이 일한 사람"이 아니다).
    public static List<string> OccupantsAt(string roomId, int day, float timeSeconds, string exceptId)
    {
        var sim = FacilitySimulation.Instance;
        var result = new List<string>();
        if (sim == null || string.IsNullOrEmpty(roomId)) return result;
        foreach (string id in sim.GetEmployeeIds())
            if (id != exceptId && RoomAt(id, day, timeSeconds) == roomId && OnDutyAt(id, day, timeSeconds))
                result.Add(id);
        return result;
    }

    public static string RoomAtIncident(string employeeId, DialogueFact fact) =>
        fact == null ? "" : RoomAt(employeeId, fact.Entry?.Day ?? Day(), fact.TimeSeconds);

    public static int Day() => GameState.Instance?.CurrentDay ?? 1;

    // --- 인접 판정 ------------------------------------------------------
    public static bool IsAdjacent(string roomA, string roomB)
    {
        if (string.IsNullOrEmpty(roomA) || string.IsNullOrEmpty(roomB) || roomA == roomB) return false;
        if (roomA == PlayerOnlyRoomId || roomB == PlayerOnlyRoomId) return false;
        var sim = FacilitySimulation.Instance;
        var a = sim?.GetRoomDef(roomA);
        var b = sim?.GetRoomDef(roomB);
        // "옆방"은 통로 연결과 다르다 — 모든 방이 중앙 제어실로만 이어져 있어도
        // 벽을 맞댄 방끼리는 소리가 넘어간다. 옆방 목록이 비어 있으면 통로로 판단한다.
        return Near(a, roomB) || Near(b, roomA);

        static bool Near(NSP.Data.RoomDef def, string other)
        {
            if (def == null) return false;
            return def.AdjacentRoomIds.Count > 0
                ? def.AdjacentRoomIds.Contains(other)
                : def.ConnectedRoomIds.Contains(other);
        }
    }

    // --- 인지 수준 ------------------------------------------------------
    public static KnowledgeLevel KnowledgeOf(string employeeId, LogEntry e)
    {
        if (e == null) return KnowledgeLevel.None;
        if (e.WitnessEmployeeIds.Contains(employeeId) || e.ActorEmployeeId == employeeId)
            return KnowledgeLevel.Direct;
        string where = RoomAt(employeeId, e.Day, e.GameTimeSeconds);
        if (!string.IsNullOrEmpty(where) && where == e.RoomId) return KnowledgeLevel.Direct;
        // 벽 너머의 일은 아무나 알아차리지 못한다 — 관찰력이 낮으면 "몰랐다"가 사실이다.
        // (없는 목격을 만들지 않기 위한 문지기. 캐릭터 차이가 증언의 양을 가른다.)
        if (!IsAdjacent(where, e.RoomId)) return KnowledgeLevel.None;
        return NSP.Facility.EmployeeTraits.Get(employeeId).ObservationalAwareness
               >= NSP.Facility.EmployeeTraits.AwarenessForIndirect
            ? KnowledgeLevel.Indirect
            : KnowledgeLevel.None;
    }

    // --- 사건 분류 ------------------------------------------------------
    public static bool IsIncident(LogEventType t) => t is LogEventType.Sabotage
        or LogEventType.TaskFailed
        or LogEventType.TabooViolation
        or LogEventType.PowerOutage
        or LogEventType.CctvDisconnect
        or LogEventType.Death;

    public static bool IsSuspiciousAction(LogEventType t) => t is LogEventType.Sabotage
        or LogEventType.Neglect
        or LogEventType.FalseOrderFollowed;

    // 인터뷰 전체가 기준으로 삼을 "그 사건". 가장 무거운 사건, 같으면 가장 최근 것.
    private static int Severity(LogEventType t) => t switch
    {
        LogEventType.Death => 5,
        LogEventType.TaskFailed => 4,
        LogEventType.PowerOutage => 4,
        LogEventType.Sabotage => 3,
        LogEventType.TabooViolation => 3,
        LogEventType.CctvDisconnect => 2,
        _ => 0,
    };

    public static LogEntry SelectSubjectIncident(int day)
    {
        var log = EventLog.Instance;
        if (log == null) return null;
        return log.GetAllEntries()
            .Where(e => e.Day == day && IsIncident(e.EventType))
            .OrderByDescending(e => Severity(e.EventType))
            .ThenByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    // 이 직원이 실제로 알고 있는 사건 중 가장 최근 것(일반 통화 "이상현상" 질문용).
    // excludeOwnActs: 자기가 한 방해공작은 "겪은 사고"로 꺼내지 않는다(결번자가 스스로 화제에 올리지 않게).
    public static LogEntry MostRecentKnownIncident(string employeeId, int day, bool excludeOwnActs = false)
    {
        var log = EventLog.Instance;
        if (log == null) return null;
        return log.GetAllEntries()
            .Where(e => e.Day == day && IsIncident(e.EventType)
                        && !(excludeOwnActs && e.ActorEmployeeId == employeeId)
                        && KnowledgeOf(employeeId, e) != KnowledgeLevel.None)
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    // 이 직원이 직접 목격한 다른 직원의 수상한 행동. 목격 기록이 없으면 null —
    // 여기서 null 이면 어떤 대사도 특정 직원을 지목할 수 없다.
    public static LogEntry FindKnownSuspicious(string employeeId, int day)
    {
        var log = EventLog.Instance;
        if (log == null) return null;
        return log.GetAllEntries()
            .Where(e => e.Day == day
                        && IsSuspiciousAction(e.EventType)
                        && !string.IsNullOrEmpty(e.ActorEmployeeId)
                        && e.ActorEmployeeId != employeeId
                        && e.WitnessEmployeeIds.Contains(employeeId))
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    // RecordOddBehaviour 가 남기는 문구는 "{코드네임} — {무엇을 했는가}" 형식이다.
    // 앞의 이름은 증언 카드가 따로 붙이므로 뒤쪽만 잘라 쓴다. 형식이 다르면 빈 값(= 내용 없음).
    private static string OddBehaviourDetail(string description)
    {
        if (string.IsNullOrEmpty(description)) return "";
        int i = description.IndexOf(" — ", System.StringComparison.Ordinal);
        return i < 0 ? "" : description[(i + 3)..].Trim();
    }

    // 오늘 이 직원과 같은 작업실에 있었던(= 실제로 얼굴을 본) 다른 직원들.
    private static List<string> SeenEmployees(string employeeId, int day)
    {
        var sim = FacilitySimulation.Instance;
        var result = new List<string>();
        if (sim == null) return result;
        var t = GetTimeline(day);
        if (!t.Moves.TryGetValue(employeeId, out var mine)) return result;

        // 내 위치가 바뀌는 시점마다 그 방에서 근무 중이던 사람을 모은다.
        foreach (var (time, room) in mine)
        {
            if (string.IsNullOrEmpty(room) || !OnDutyAt(employeeId, day, time)) continue;
            foreach (string other in OccupantsAt(room, day, time, employeeId))
                if (!result.Contains(other)) result.Add(other);
        }
        return result;
    }

    // 사건 키로 실제 로그 한 줄을 되찾는다. 없으면 null — 없는 사건을 지어내지 않는다.
    public static LogEntry FindByKey(int day, string incidentKey)
    {
        if (string.IsNullOrEmpty(incidentKey)) return null;
        return EventLog.Instance?.GetAllEntries()
            .FirstOrDefault(e => e.Day == day && DialogueFact.From(e, KnowledgeLevel.None).Key == incidentKey);
    }

    // --- 컨텍스트 조립 --------------------------------------------------

    // 기준 시각을 명시해 만드는 경로.
    //
    // 기본 Build 는 사건을 주지 않으면 "오늘 가장 무거운 사건"을 스스로 골라 기준으로 삼는다.
    // 이동 기록이나 CCTV 처럼 사건이 아닌 순간을 물을 때 그 기본값이 끼어들면, 질문은
    // 22:13 이동을 묻는데 답변은 22:40 사고를 기준으로 계산되는 사고가 난다.
    // 그래서 증거 기반 심문은 반드시 이쪽으로 들어온다.
    public static DialogueContext BuildAt(string employeeId, DialogueConversationKind kind,
        string questionId, LogEntry subject, float anchorTime, string claimKey)
    {
        var ctx = Build(employeeId, kind, questionId, "", subject, autoSelectSubject: false);
        if (anchorTime >= 0f)
        {
            ctx.SubjectTime = anchorTime;
            ctx.HasSubjectTime = true;
            // 사건이 따로 없으면 그 시각의 실제 위치를 여기서 다시 잡는다.
            if (subject == null)
            {
                string at = RoomAt(employeeId, ctx.CurrentDay, anchorTime);
                ctx.RoomAtSubject = string.IsNullOrEmpty(at) ? ctx.AssignedRoomId : at;
            }
        }
        if (!string.IsNullOrEmpty(claimKey)) ctx.ClaimKey = claimKey;
        return ctx;
    }

    public static DialogueContext Build(string employeeId, DialogueConversationKind kind,
        string questionId, string eventId, LogEntry subjectOverride, bool autoSelectSubject = true)
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        int day = Day();
        var st = sim?.GetEmployeeState(employeeId);

        var ctx = new DialogueContext
        {
            EmployeeId = employeeId,
            IsSaboteur = gs?.SaboteurEmployeeId == employeeId,
            Conversation = kind,
            QuestionId = questionId ?? "",
            EventId = eventId ?? "",
            CurrentDay = day,
            CurrentGameTime = gs?.DayTimeSeconds ?? 0f,
            CurrentRoomId = st?.CurrentRoomId ?? "",
            AssignedRoomId = st?.AssignedRoomId ?? "",
            Stress = st?.Stress ?? 1f,
            StressBand = st != null && sim != null ? sim.StressBandName(st) : "",
            DailyMood = st?.DailyMood ?? "",
            Incapacitated = st?.Incapacitated ?? false,
            Isolated = st?.Isolated ?? false,
            IsMoving = st?.IsMoving ?? false,
            FacilityBlackout = gs != null && gs.PowerCapacity == 0,
        };

        var subject = subjectOverride ?? (autoSelectSubject ? SelectSubjectIncident(day) : null);
        if (subject != null)
        {
            var knowledge = KnowledgeOf(employeeId, subject);
            ctx.Subject = DialogueFact.From(subject, knowledge);
            ctx.SubjectKnowledge = knowledge;
            ctx.RoomAtSubject = RoomAt(employeeId, subject.Day, subject.GameTimeSeconds);
            ctx.IsSubjectActor = subject.ActorEmployeeId == employeeId;
            ctx.SubjectTime = subject.GameTimeSeconds;
            ctx.HasSubjectTime = true;
            ctx.ClaimKey = ctx.Subject.Key;
        }
        if (string.IsNullOrEmpty(ctx.RoomAtSubject))
            ctx.RoomAtSubject = ctx.AssignedRoomId;

        var suspicious = FindKnownSuspicious(employeeId, day);
        if (suspicious != null)
        {
            ctx.KnownSuspicious = DialogueFact.From(suspicious, KnowledgeLevel.Direct);
            ctx.KnownSuspiciousActorId = suspicious.ActorEmployeeId;
            ctx.KnownSuspiciousDetail = OddBehaviourDetail(suspicious.Description);
        }
        ctx.SeenEmployeeIds.AddRange(SeenEmployees(employeeId, day));

        // 현재 근무 상태(일반 통화에서 지금 상황을 답할 때만 쓴다).
        string room = string.IsNullOrEmpty(ctx.CurrentRoomId) ? ctx.AssignedRoomId : ctx.CurrentRoomId;
        if (sim != null && !string.IsNullOrEmpty(room))
        {
            var task = sim.GetPrimarySpawnedTask(room);
            ctx.HasActiveTask = task != null;
            ctx.CurrentTaskName = task != null ? sim.GetTaskDef(task.TaskId)?.DisplayName ?? "" : "";
            ctx.RoomUnderRepair = task is { IsRepair: true };
            ctx.RoomBlockedByMaterials = sim.IsRoomBlockedByMaterials(room);
            ctx.RoomCctvBlocked = sim.IsRoomCctvBlocked(room);
        }

        ctx.EvidenceAgainstCount = CountEvidence(employeeId, day, ctx);
        return ctx;
    }

    // 이 직원에게 실제로 불리하게 남은 기록의 수. 방해자가 얼마나 위험한지 판단하는 근거이며,
    // 없는 증거를 상상해 겁먹거나 있는 증거를 무시하지 않게 한다.
    private static int CountEvidence(string employeeId, int day, DialogueContext ctx)
    {
        var log = EventLog.Instance;
        if (log == null) return 0;
        int n = log.GetAllEntries().Count(e => e.Day == day
            && e.ActorEmployeeId == employeeId
            && IsSuspiciousAction(e.EventType)
            && e.WitnessEmployeeIds.Any(w => w != employeeId));

        // 배정지를 벗어난 상태를 다른 직원이 같은 방에서 볼 수 있었는가.
        if (ctx.Subject != null && !string.IsNullOrEmpty(ctx.RoomAtSubject)
            && !string.IsNullOrEmpty(ctx.AssignedRoomId)
            && ctx.RoomAtSubject != ctx.AssignedRoomId)
        {
            if (OccupantsAt(ctx.RoomAtSubject, day, ctx.Subject.TimeSeconds, employeeId).Count > 0)
                n++;
        }
        return n;
    }

    public static void Invalidate() => _timeline = null;
}
