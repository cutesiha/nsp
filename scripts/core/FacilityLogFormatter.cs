using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NSP.Data;
using NSP.Facility;

namespace NSP.Core;

// EventLog(전체 원본 기록) → 플레이어용 시설 로그(DisplayLogEntry).
//
// EventLog 는 절대 건드리지 않는다. 로컬 대화 생성·사고 당시 위치 판정·목격 판정이
// 그 상세 기록을 그대로 필요로 하기 때문이다. 여기서는 "관리자가 실제로 알 수 있는
// 시설 관리 기록"만 골라 다시 문장으로 만든다.
//
// 화면에서 빼는 것
//   · 초기 배치를 위한 퇴장/입장, 단순 RoomEnter/RoomExit
//   · 정상적인 업무 시작(TaskStart) / 배치 해제(TaskEnd)
//   · 상시 업무 발생과 그 완료(자재 생산 등 반복 기록)
//   · 스트레스 증감 원본 기록(구간이 위험으로 넘어갈 때만 한 줄)
//
// 화면에 남기는 것
//   · 재배치/이동은 "도착한 순간" 한 줄로 요약
//   · 사고, 금기, 전력·CCTV 이상, 코어/자재 손실, 격리, 업무 불능, 사망
//   · 목격 기록이 있는 방해 행동(목격자가 실제로 본 것만)
public static class FacilityLogFormatter
{
    // 같은 문장이 연속으로 반복되는 것을 막는 검사 범위.
    private const int DuplicateWindow = 6;
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
            Text = $"DAY {day:00} 근무 시작 / 초기 배치 완료",
            Severity = DisplayLogSeverity.Normal,
        });

        var state = new BuildState();
        foreach (var e in today)
        {
            var row = Convert(e, state);
            if (row == null) continue;
            if (IsRecentDuplicate(rows, row)) continue;
            rows.Add(row);
        }
        return rows;
    }

    // 표시용 요약에 필요한 최소 상태. EventLog 를 바꾸지 않고 여기서만 들고 있는다.
    private sealed class BuildState
    {
        // 화면 기준으로 그 직원이 마지막에 있다고 알려진 작업실.
        public readonly Dictionary<string, string> LastRoom = new();
        // 관리자가 내린 재배치 지시의 목적지(도착하면 소비된다).
        public readonly Dictionary<string, string> PendingOrder = new();
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
        LogEventType.TaskFailed => Row(e, Strip(e.Description), DisplayLogSeverity.Critical),
        LogEventType.Neglect => Neglect(e, s),
        LogEventType.Sabotage => Sabotage(e),
        LogEventType.TabooViolation => Row(e, Strip(e.Description), DisplayLogSeverity.Critical),
        LogEventType.PowerOutage => Row(e, Strip(e.Description), DisplayLogSeverity.Critical),
        LogEventType.CctvDisconnect => Row(e, Strip(e.Description), DisplayLogSeverity.Warning),
        LogEventType.Death => Death(e, s),
        LogEventType.Isolation => Isolation(e, s),
        LogEventType.FalseOrderFollowed => Row(e, Strip(e.Description), DisplayLogSeverity.Warning, e.ActorEmployeeId),
        _ => null,
    };

    // --- 이동 요약 ------------------------------------------------------

    // 배치 지시. 아직 초기 배치 전이면 그 방을 "원래 자리"로 삼고 아무것도 표시하지 않는다.
    private static DisplayLogEntry Relocation(LogEntry e, BuildState s)
    {
        // 행위자가 없는 Relocation 은 구역 봉쇄 같은 시설 조치다.
        if (string.IsNullOrEmpty(e.ActorEmployeeId))
            return Row(e, Strip(e.Description), DisplayLogSeverity.Warning);

        if (!s.Deployed.Contains(e.ActorEmployeeId))
            s.LastRoom[e.ActorEmployeeId] = e.RoomId;
        else
            s.PendingOrder[e.ActorEmployeeId] = e.RoomId;
        return null;
    }

    // 실제 도착. 초기 배치 도착은 숨기고, 그 뒤의 자리 이동만 한 줄로 요약한다.
    private static DisplayLogEntry Arrival(LogEntry e, BuildState s)
    {
        string id = e.ActorEmployeeId;
        if (string.IsNullOrEmpty(id)) return null;

        if (!s.Deployed.Contains(id))
        {
            s.Deployed.Add(id);
            s.LastRoom[id] = e.RoomId;
            return null;
        }

        string from = s.LastRoom.GetValueOrDefault(id, "");
        s.LastRoom[id] = e.RoomId;
        if (from == e.RoomId || string.IsNullOrEmpty(from)) return null;

        // 관리자가 지시한 목적지에 도착했으면 "재배치", 그 밖의 이동은 "이동".
        bool ordered = s.PendingOrder.GetValueOrDefault(id, "") == e.RoomId;
        s.PendingOrder.Remove(id);
        string verb = ordered ? "재배치" : "이동";
        return Row(e, $"{Codename(id)} | {RoomName(from)} → {RoomName(e.RoomId)} {verb}",
            DisplayLogSeverity.Normal, id);
    }

    // 업무를 시작했다는 것은 배치가 끝났다는 뜻이다(이동 없이 제자리 근무한 직원 포함).
    // 평상시 업무 시작은 숨기고, 사고 복구 작업(🔧)만 운영 기록으로 남긴다.
    private static DisplayLogEntry MarkDeployed(LogEntry e, BuildState s)
    {
        string id = e.ActorEmployeeId;
        if (!string.IsNullOrEmpty(id))
        {
            s.Deployed.Add(id);
            if (!s.LastRoom.ContainsKey(id)) s.LastRoom[id] = e.RoomId;
        }
        if (!(e.Description ?? "").StartsWith("🔧")) return null;
        return Row(e, $"{Codename(id)} | {RoomName(e.RoomId)} 수리 시작", DisplayLogSeverity.Normal, id);
    }

    // --- 업무/사고 ------------------------------------------------------

    // 상시 업무(⚙)는 계속 도는 기록이라 숨기고, 제한시간이 붙은 사고 예고만 남긴다.
    private static DisplayLogEntry TaskSpawned(LogEntry e)
    {
        string d = e.Description ?? "";
        if (d.StartsWith("⚙")) return null;
        return Row(e, Strip(d), DisplayLogSeverity.Warning);
    }

    // 반복 업무 완료(자재 +5 등)는 숨긴다. 수리 완료·전력 복구·효과가 막힌 경우만 남긴다.
    private static DisplayLogEntry TaskComplete(LogEntry e)
    {
        string d = e.Description ?? "";
        if (d.Contains("기능 복구") || d.Contains("⚡"))
            return Row(e, Strip(d), DisplayLogSeverity.Recovery);
        if (d.Contains("⚠"))
            return Row(e, Strip(d), DisplayLogSeverity.Warning);
        return null;
    }

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
            return Row(e, Strip(d), DisplayLogSeverity.Warning, id);

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

    // 방해 행동. 실행자 id 는 EventLog 에 남아 있지만 화면에는 절대 쓰지 않는다.
    // 같은 방에 있던 직원이 실제로 목격했을 때만 "누가 무엇을 했는지"가 드러난다.
    private static DisplayLogEntry Sabotage(LogEntry e)
    {
        string witness = e.WitnessEmployeeIds.FirstOrDefault(w => w != e.ActorEmployeeId);
        if (!string.IsNullOrEmpty(witness) && !string.IsNullOrEmpty(e.ActorEmployeeId))
            return Row(e, $"{Codename(witness)} | {Codename(e.ActorEmployeeId)}의 비정상 행동 목격",
                DisplayLogSeverity.Warning, witness);

        // 목격자가 없으면 결과만 보인다 — 원문에도 이름이 들어 있지 않다.
        return Row(e, Strip(e.Description), DisplayLogSeverity.Warning);
    }

    private static DisplayLogEntry Death(LogEntry e, BuildState s)
    {
        if (!string.IsNullOrEmpty(e.ActorEmployeeId))
        {
            s.Deployed.Remove(e.ActorEmployeeId);
            s.LastRoom.Remove(e.ActorEmployeeId);
        }
        return Row(e, Strip(e.Description), DisplayLogSeverity.Critical, e.ActorEmployeeId);
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

    // 원문의 상태 표식은 화면에서 중요도 아이콘으로 다시 붙이므로 여기서 떼어낸다.
    private static string Strip(string value) =>
        (value ?? "").Replace("⚠", "").Replace("🚨", "").Replace("✓", "").Replace("⚙", "").Replace("🔧", "").Trim();

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
