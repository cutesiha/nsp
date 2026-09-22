using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NSP.Data;
using NSP.Facility;

namespace NSP.Core;

// EventLog(전체 원본 기록) → 플레이어용 시설 로그(DisplayLogEntry).
//
// EventLog 는 절대 건드리지 않는다. 로컬 대화 생성·사고 당시 위치 판정·목격 판정이
// 그 상세 기록을 그대로 필요로 하기 때문이다. 여기서는 "관리자가 나중에 심문에서
// 근거로 쓸 사건"만 골라 다시 문장으로 만든다.
//
// 이 로그는 디버그 콘솔이 아니다. 한 줄을 보고 아래를 답할 수 있어야 한다.
//   · 이 직원은 처음 어디에 있었지?
//   · 왜 저 시간에 다른 방으로 옮겼지?
//   · 사고 직전에 누가 어디로 움직였지?
//   · 방해공작 시각에 그 방에 누가 있었지?
//
// 화면에 남기는 것
//   ① 근무 시작 시각의 최초 배치 (직원 수만큼 한 번)
//   ② 재배치 — 지시가 아니라 "실제로 도착한 순간"
//   ③ 경고 발생 / 안정화 성공 / 실제 고장
//   ④ 방해공작 (빨강. 범인 이름은 절대 쓰지 않는다)
//   ⑤ 경비 순찰 기록, 전력·CCTV 이상, 자재 중단/재개, 격리, 사망, 금기 위반
//
// 화면에서 빼는 것
//   · 평상시 TaskStart / TaskEnd / RoomExit / 초기 배치용 입퇴실
//   · 상시 업무 발생과 그 완료(자재 +1, 코어 +1% 같은 반복 tick)
//   · 스트레스 증감 원본(위험 구간 진입만 한 줄)
//   · 그 밖의 내부 계산용 이벤트
public static class FacilityLogFormatter
{
    // 같은 문장이 연속으로 반복되는 것을 막는 검사 범위.
    private const int DuplicateWindow = 6;
    // 근무 시계가 돌기 전에 내려진 배치 지시 = 최초 배치로 본다.
    private const float InitialDeployWindow = 1.0f;
    private static readonly Regex StressTail = new(@"→\s*(\d+)\s*$", RegexOptions.Compiled);

    public static List<DisplayLogEntry> Build(IReadOnlyList<LogEntry> entries, int day)
    {
        var rows = new List<DisplayLogEntry>();
        if (entries == null) return rows;

        var today = entries.Where(e => e.Day == day).ToList();
        if (today.Count == 0) return rows;

        rows.Add(new DisplayLogEntry
        {
            Timestamp = 0f,
            Text = $"{DayFeatures.DayLabel(day)} 근무 개시",
            Severity = DisplayLogSeverity.Normal,
        });

        var state = new BuildState();
        var body = new List<DisplayLogEntry>();
        foreach (var e in today)
        {
            var row = Convert(e, state);
            if (row == null) continue;
            if (IsRecentDuplicate(body, row)) continue;
            body.Add(row);
        }

        // ① 최초 배치는 근무 시작 줄 바로 아래에 모아 둔다(전부 22:00 이라 순서가 맞는다).
        //    지시 시점이 아니라 "이 직원이 오늘 어디서 시작했는가"를 알려 주는 줄이다.
        foreach (var (id, roomId) in state.InitialRoom)
            rows.Add(new DisplayLogEntry
            {
                Timestamp = 0f,
                Text = $"{Codename(id)} | {RoomName(roomId)} 배치",
                Severity = DisplayLogSeverity.Normal,
                RelatedEmployeeId = id,
                SourceEventType = LogEventType.Relocation,
            });

        rows.AddRange(body);
        return rows;
    }

    // 표시용 요약에 필요한 최소 상태. EventLog 를 바꾸지 않고 여기서만 들고 있는다.
    private sealed class BuildState
    {
        // 화면 기준으로 그 직원이 마지막에 있다고 알려진 작업실.
        public readonly Dictionary<string, string> LastRoom = new();
        // 관리자가 내린 재배치 지시의 목적지(도착하면 소비된다).
        public readonly Dictionary<string, string> PendingOrder = new();
        // 근무 시작 시각의 배치. 배치를 바꿔 가며 정해도 마지막 것만 남는다.
        public readonly Dictionary<string, string> InitialRoom = new();
        // 초기 배치가 끝난 직원. 여기 들어오기 전의 입퇴실은 전부 초기 배치로 본다.
        public readonly HashSet<string> Deployed = new();
        // 스트레스 위험 구간 진입을 한 번만 알린다.
        public readonly HashSet<string> StressWarned = new();
    }

    private static DisplayLogEntry Convert(LogEntry e, BuildState s) => e.EventType switch
    {
        LogEventType.Relocation => Relocation(e, s),
        LogEventType.RoomEnter => Arrival(e, s),
        LogEventType.TaskStart => MarkDeployed(e, s),
        LogEventType.RoomExit or LogEventType.TaskEnd => null,
        LogEventType.TaskSpawned => TaskSpawned(e),
        LogEventType.TaskComplete => TaskComplete(e),
        LogEventType.TaskFailed => Accident(e),
        LogEventType.Neglect => Neglect(e, s),
        LogEventType.Sabotage => Sabotage(e),
        LogEventType.TabooViolation => Row(e, Pipe(e.Description), DisplayLogSeverity.Critical),
        LogEventType.PowerOutage => Row(e, Pipe(e.Description), DisplayLogSeverity.Critical),
        // 전력 용량 변화는 전후 값이 들어 있다. 늘어난 경우는 복구로 본다.
        LogEventType.PowerCapacityChanged => Row(e, Pipe(e.Description),
            (e.Description ?? "").Contains("증가") ? DisplayLogSeverity.Recovery : DisplayLogSeverity.Critical),
        LogEventType.ResourceShortage => Row(e, Pipe(e.Description),
            (e.Description ?? "").Contains("재개") ? DisplayLogSeverity.Recovery : DisplayLogSeverity.Warning),
        LogEventType.CctvDisconnect => Row(e, Pipe(e.Description), DisplayLogSeverity.Warning),
        LogEventType.Argument => Row(e, Pipe(e.Description), DisplayLogSeverity.Warning, e.ActorEmployeeId),
        LogEventType.Death => Death(e, s),
        LogEventType.Isolation => Isolation(e, s),
        LogEventType.FalseOrderFollowed => Row(e, Pipe(e.Description), DisplayLogSeverity.Warning, e.ActorEmployeeId),
        _ => null,
    };

    // --- 이동 요약 ------------------------------------------------------

    // 배치 지시. 화면에는 지시 자체를 쓰지 않는다 —
    // 명령만 내리고 아직 도착하지 않은 직원이 "그 방에 있었다"로 읽히면 안 되기 때문이다.
    private static DisplayLogEntry Relocation(LogEntry e, BuildState s)
    {
        // 행위자가 없는 Relocation 은 구역 봉쇄 같은 시설 조치다.
        if (string.IsNullOrEmpty(e.ActorEmployeeId))
            return Row(e, Pipe(e.Description), DisplayLogSeverity.Warning);

        // 근무 시계가 돌기 전의 지시 = 최초 배치. 마지막으로 정한 방만 남는다.
        if (e.GameTimeSeconds <= InitialDeployWindow && !s.Deployed.Contains(e.ActorEmployeeId))
        {
            s.InitialRoom[e.ActorEmployeeId] = e.RoomId;
            s.LastRoom[e.ActorEmployeeId] = e.RoomId;
            return null;
        }

        if (!s.Deployed.Contains(e.ActorEmployeeId))
            s.LastRoom[e.ActorEmployeeId] = e.RoomId;
        else
            s.PendingOrder[e.ActorEmployeeId] = e.RoomId;
        return null;
    }

    // 실제 도착. 초기 배치 도착은 숨기고(위에서 이미 한 줄로 냈다),
    // 그 뒤의 자리 이동만 "출발 → 도착" 한 줄로 요약한다.
    private static DisplayLogEntry Arrival(LogEntry e, BuildState s)
    {
        string id = e.ActorEmployeeId;
        if (string.IsNullOrEmpty(id)) return null;
        // 목적지로 가는 길에 잠시 지나친 방은 "그 방에 있었다"가 아니다.
        if (e.PassingThrough) return null;

        if (!s.Deployed.Contains(id))
        {
            s.Deployed.Add(id);
            // 배치 자리에 도착한 것이면 초기 배치다(위에서 이미 한 줄로 냈다).
            // 배치 자리가 아닌 곳에 처음 도착했다면 그건 근무 중 이동이다 —
            // 제자리에서 계속 일하던 직원이 처음 자리를 뜨는 순간이 여기 걸린다.
            string initial = s.InitialRoom.GetValueOrDefault(id, "");
            if (string.IsNullOrEmpty(initial) || initial == e.RoomId)
            {
                if (!s.InitialRoom.ContainsKey(id)) s.InitialRoom[id] = e.RoomId;
                s.LastRoom[id] = e.RoomId;
                return null;
            }
        }

        string from = s.LastRoom.GetValueOrDefault(id, "");
        s.LastRoom[id] = e.RoomId;
        if (from == e.RoomId || string.IsNullOrEmpty(from)) return null;

        // 관리자가 지시한 목적지에 도착했는가(추리 시스템이 "지시" 여부를 구분한다).
        bool ordered = s.PendingOrder.GetValueOrDefault(id, "") == e.RoomId;
        s.PendingOrder.Remove(id);
        var row = Row(e, $"{Codename(id)} | {RoomName(from)} → {RoomName(e.RoomId)}",
            DisplayLogSeverity.Move, id);
        if (row != null)
        {
            row.FromRoomId = from;
            row.ToRoomId = e.RoomId;
            row.PlayerOrdered = ordered;
        }
        return row;
    }

    // 업무를 시작했다는 것은 배치가 끝났다는 뜻이다(이동 없이 제자리 근무한 직원 포함).
    // 평상시 업무 시작은 숨기고, 사고 복구 작업(🔧)만 운영 기록으로 남긴다.
    private static DisplayLogEntry MarkDeployed(LogEntry e, BuildState s)
    {
        string id = e.ActorEmployeeId;
        if (!string.IsNullOrEmpty(id))
        {
            if (!s.Deployed.Contains(id) && !s.InitialRoom.ContainsKey(id))
                s.InitialRoom[id] = e.RoomId;
            s.Deployed.Add(id);
            if (!s.LastRoom.ContainsKey(id)) s.LastRoom[id] = e.RoomId;
        }
        if (!(e.Description ?? "").StartsWith("🔧")) return null;
        return Row(e, $"{Codename(id)} | {RoomName(e.RoomId)} 수리 시작", DisplayLogSeverity.Normal, id);
    }

    // --- 경고 / 사고 ----------------------------------------------------

    // 상시 업무(⚙)는 계속 도는 기록이라 숨긴다. 남는 것은 "지금 대응하면 막을 수 있는" 경고뿐.
    private static DisplayLogEntry TaskSpawned(LogEntry e)
    {
        string d = e.Description ?? "";
        if (d.StartsWith("⚙")) return null;
        return Row(e, Pipe(d), DisplayLogSeverity.Warning);
    }

    // 반복 업무 완료(자재 +1 등)는 숨긴다.
    // 남기는 것: 경고 안정화 · 수리 완료 · 전력 복구 · 경비 순찰 기록 · 효과가 막힌 경우.
    private static DisplayLogEntry TaskComplete(LogEntry e)
    {
        string d = e.Description ?? "";
        if (d.Contains("안정화 성공"))
            return Row(e, Pipe(d).Replace("안정화 성공", "안정화 완료"), DisplayLogSeverity.Recovery);
        if (d.Contains("기능 복구") || d.Contains("⚡"))
            return Row(e, Pipe(d), DisplayLogSeverity.Recovery);
        // 경비 순찰 — "그 시각 그 방에 누가 있었나"는 심문에서 그대로 근거가 된다.
        if (d.Contains("순찰 기록"))
            return Row(e, Pipe(d), DisplayLogSeverity.Normal);
        if (d.Contains("⚠"))
            return Row(e, Pipe(d), DisplayLogSeverity.Warning);
        return null;
    }

    // 실제로 설비가 망가진 순간. 경고보다 한 단계 더 눈에 띈다.
    private static DisplayLogEntry Accident(LogEntry e) =>
        Row(e, Pipe(e.Description), DisplayLogSeverity.Critical);

    // Neglect 는 세 가지 용도로 쓰인다 — 스트레스 증감 / 기절 / 업무 미완료 이탈.
    private static DisplayLogEntry Neglect(LogEntry e, BuildState s)
    {
        string d = e.Description ?? "";
        string id = e.ActorEmployeeId;

        if (d.Contains("기절"))
        {
            // 의무실로 강제 이송된다 — 뒤따르는 입장 기록이 "이동"으로 잡히지 않게 초기화.
            if (!string.IsNullOrEmpty(id)) { s.Deployed.Remove(id); s.LastRoom.Remove(id); }
            return Row(e, $"{Codename(id)} | 업무 불능 / 의무실 이송", DisplayLogSeverity.Critical, id);
        }

        if (d.Contains("미완료"))
            return Row(e, Pipe(d), DisplayLogSeverity.Warning, id);

        // 스트레스 증감 원본은 너무 잦아 숨긴다. 위험 구간에 처음 들어간 순간만 한 번 알린다.
        var m = StressTail.Match(d);
        if (m.Success && !string.IsNullOrEmpty(id) && !s.StressWarned.Contains(id)
            && int.TryParse(m.Groups[1].Value, out int stress)
            && stress >= (Config.Instance?.Data?.StressDangerFrom ?? 31))
        {
            s.StressWarned.Add(id);
            return Row(e, $"{Codename(id)} | 스트레스 위험 ({stress})", DisplayLogSeverity.Warning, id);
        }
        return null;
    }

    // --- 추리 정보 ------------------------------------------------------

    // 방해공작. 실행자 id 는 EventLog 에 남아 있지만 화면에는 절대 쓰지 않는다 —
    // 로그가 범인을 알려 주면 심문도 CCTV 도 볼 이유가 없어진다.
    // 목격자가 있으면 "누군가의 비정상 행동을 봤다"까지만 알려 주고, 누구였는지는
    // 그 목격자를 직접 심문해서 알아내게 한다.
    private static DisplayLogEntry Sabotage(LogEntry e)
    {
        // 원본은 "작업실 — 결과" 형태다. 앞에 '방해공작' 을 세워 한눈에 구분되게 한다.
        string body = Strip(e.Description).Replace(" — ", " · ");
        string witness = e.WitnessEmployeeIds.FirstOrDefault(w => w != e.ActorEmployeeId);
        if (!string.IsNullOrEmpty(witness))
            return Row(e, $"방해공작 | {body}  ({Codename(witness)} 목격)", DisplayLogSeverity.Sabotage);
        return Row(e, $"방해공작 | {body}", DisplayLogSeverity.Sabotage);
    }

    private static DisplayLogEntry Death(LogEntry e, BuildState s)
    {
        if (!string.IsNullOrEmpty(e.ActorEmployeeId))
        {
            s.Deployed.Remove(e.ActorEmployeeId);
            s.LastRoom.Remove(e.ActorEmployeeId);
        }
        return Row(e, Pipe(e.Description), DisplayLogSeverity.Critical, e.ActorEmployeeId);
    }

    private static DisplayLogEntry Isolation(LogEntry e, BuildState s)
    {
        string id = e.ActorEmployeeId;
        bool released = (e.Description ?? "").Contains("해제");
        // 격리/해제 모두 강제 이동을 동반한다 — 뒤따르는 입장 기록을 이동으로 잡지 않는다.
        if (!string.IsNullOrEmpty(id)) { s.Deployed.Remove(id); s.LastRoom.Remove(id); }
        return Row(e, $"{Codename(id)} | {(released ? "격리 해제" : "격리 조치")}",
            DisplayLogSeverity.Normal, id);
    }

    // --- 공통 -----------------------------------------------------------

    private static DisplayLogEntry Row(LogEntry e, string text, DisplayLogSeverity severity,
        string employeeId = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return new DisplayLogEntry
        {
            Timestamp = e.GameTimeSeconds,
            Text = text,
            Severity = severity,
            RelatedEmployeeId = employeeId ?? "",
            SourceEventType = e.EventType,
        };
    }

    // 같은 사건이 여러 내부 이벤트로 남아도 화면에는 같은 문장을 반복하지 않는다.
    private static bool IsRecentDuplicate(List<DisplayLogEntry> rows, DisplayLogEntry row)
    {
        int from = System.Math.Max(0, rows.Count - DuplicateWindow);
        for (int i = rows.Count - 1; i >= from; i--)
            if (rows[i].Text == row.Text) return true;
        return false;
    }

    // 원본 문구를 "주체 | 내용" 한 형식으로 맞춘다. 원본은 '방 — 내용' 으로 적혀 있다.
    private static string Pipe(string value)
    {
        string t = Strip(value);
        int i = t.IndexOf(" — ", System.StringComparison.Ordinal);
        if (i > 0) return t[..i].Trim() + " | " + t[(i + 3)..].Trim();
        return t;
    }

    // 원문의 상태 표식은 화면에서 중요도 아이콘으로 다시 붙이므로 여기서 떼어낸다.
    private static string Strip(string value) =>
        (value ?? "").Replace("⚠", "").Replace("🚨", "").Replace("✓", "")
            .Replace("⚙", "").Replace("🔧", "").Replace("🛡", "").Trim();

    private static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "직원";
        return FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
    }

    private static string RoomName(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return "통로";
        return FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;
    }
}
