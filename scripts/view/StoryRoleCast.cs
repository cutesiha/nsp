using System.Collections.Generic;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 스토리 대본의 **배역**을 그날의 실제 인물로 바꾼다.
//
// 대본에 직원 id 를 박아 두면 "어제 그걸 본 사람" 같은 대사가 거짓말이 된다 —
// 괴물은 그 작업실에 있던 사람만 봤는데, 대본에는 양과 토끼가 봤다고 적혀 있으니
// 실제로 그 방에 없던 사람이 "저도 봤어요" 라고 말한다(DAY2 에서 실제로 그랬다).
//
// 그래서 그런 줄은 id 대신 배역으로 적는다.
//
//   @witness        어제 그 개체를 **직접 본** 사람
//   @heard1 … @heard5   보지 못한 사람(소리만 들은 사람). 1 부터 차례로 다른 사람이다
//
// 배역이 맡을 사람이 없으면 그 줄은 빠진다(say? 와 같은 취급). 그래서 한 명만 봤든
// 셋이 봤든 대화가 성립한다.
//
// 판정은 전부 **기록에서 읽기만** 한다 — 어제 로그의 이상 개체 줄에 적힌 목격자 명단이다
// (GhostHauntSystem 이 그 방에 서 있던 사람을 그대로 적어 둔다).
public sealed class StoryRoleCast
{
    public const string RolePrefix = "@";
    private const string WitnessRole = "@witness";
    private const string HeardPrefix = "@heard";

    private readonly List<string> _witnesses = new();
    private readonly List<string> _others = new();

    public static bool IsRole(string speakerId) =>
        !string.IsNullOrEmpty(speakerId) && speakerId[0] == '@';

    // 그 DAY 의 배역표. day 는 지금 시작하는 날이고, 보는 기록은 **어제** 것이다.
    public static StoryRoleCast ForDay(int day)
    {
        var cast = new StoryRoleCast();
        var log = EventLog.Instance;
        if (log != null && day > 1)
        {
            foreach (var e in log.GetAllEntries())
            {
                if (e.Day != day - 1) continue;
                if (e.EventType is not (LogEventType.AnomalyDispelled or LogEventType.AnomalyIncident)) continue;
                foreach (string id in e.WitnessEmployeeIds)
                    if (!cast._witnesses.Contains(id)) cast._witnesses.Add(id);
            }
        }
        // 목격자 가운데 지금 휴게실에 앉아 있지 않은 사람(사망 · 기절 · 격리)은 말할 수 없다.
        cast._witnesses.RemoveAll(id => !StoryCutinDirector.IsPresent(id));

        foreach (string id in StaffIdCard.Order)
            if (!cast._witnesses.Contains(id) && StoryCutinDirector.IsPresent(id))
                cast._others.Add(id);
        return cast;
    }

    // 배역 → 실제 직원 id. 배역이 아니면 그대로 돌려준다.
    // 맡을 사람이 없으면 빈 문자열 — 호출부가 그 줄을 건너뛴다.
    public string Resolve(string speakerId)
    {
        if (!IsRole(speakerId)) return speakerId ?? "";
        if (speakerId.StartsWith(WitnessRole))
            return Pick(_witnesses, Index(speakerId, WitnessRole.Length));
        if (speakerId.StartsWith(HeardPrefix))
            return Pick(_others, Index(speakerId, HeardPrefix.Length));
        return "";
    }

    // 배역이 바뀐 줄. 원본 StoryLine 은 StoryScript 가 들고 있는 **공유 대본**이라
    // 절대 고치지 않는다(한 번 고치면 그 판 내내 그대로 남는다).
    public StoryLine Apply(StoryLine line)
    {
        if (line == null || !IsRole(line.SpeakerEmployeeId)) return line;
        string who = Resolve(line.SpeakerEmployeeId);
        if (who.Length == 0) return null;   // 맡을 사람이 없다 — 이 줄은 빠진다
        return new StoryLine
        {
            SpeakerEmployeeId = who,
            Expression = line.Expression,
            Side = line.Side,
            Text = line.Text,
            HoldSeconds = line.HoldSeconds,
            ExitSide = line.ExitSide,
            Optional = line.Optional,
        };
    }

    // @heard3 → 3. 숫자가 없으면 1.
    private static int Index(string role, int prefixLength)
    {
        string tail = role[prefixLength..];
        return tail.Length > 0 && int.TryParse(tail, out int n) && n >= 1 ? n : 1;
    }

    private static string Pick(List<string> pool, int oneBased) =>
        oneBased <= pool.Count ? pool[oneBased - 1] : "";
}
