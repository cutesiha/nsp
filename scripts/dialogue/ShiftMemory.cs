using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 근무 기억 한 줄의 종류.
public enum MemoryKind
{
    Assigned,       // 근무 시작 배치
    Relocated,      // 근무 중 관리자가 재배치했다
    Dispatched,     // 전화로 "확인하러 가라"는 지시를 받았다
    Worked,         // 그 방에서 업무를 시작했다(같이 한 사람 포함)
    RepairDone,     // 내가 있던 방의 고장이 복구됐다
    IncidentHere,   // 내가 있던 방에서 사고가 났다(직접 봤다)
    IncidentHeard,  // 옆방 사고를 소리·진동으로 알았다
    CallReported,   // 내가 먼저 관리자에게 전화했다
    CallStay,       // 전화로 "대기하라"는 지시를 받았다
    CallMissed,     // 전화했는데 관리자가 안 받았다
    ManagerCalled,  // 관리자가 나에게 전화했다

    // ── 기억에서 바로 계산하는 것(로그 한 줄이 아니다) ──────────────────
    Companion,      // 그 시각 같은 방에 있던 사람 / 혼자였다
    DayCompanion,   // 오늘 가장 오래 같이 있던 사람
    Summary,        // 하루 요약(재배치 횟수 · 수리 횟수 · 통화 횟수)
    Slip,           // 결번자 흉내의 어긋남
}

public sealed class MemoryItem
{
    public float Time;
    public MemoryKind Kind;
    public string RoomId = "";
    public string TaskName = "";
    public bool IsRepair;
    public LogEventType IncidentType;
    public readonly List<string> With = new();
}

// 어떤 질문에 덧붙일 기억을 고르는가.
public enum RecallTopic
{
    None,
    Location,      // 그 시각 어디 있었나 / 진술 재확인 / 증언 확인
    Anomaly,       // 이상한 일 / 사고를 알았나
    Suspicious,    // 수상한 사람(목격 없음)
    ShiftReview,   // 오늘 근무는 어땠나
    Status,        // (근무 중 통화) 지금 어떤가
    Movement,      // 왜 이동했나
    Presence,      // 거기 있던 이유 / 거기서 한 일 / 사고 직전
    Accuse,        // 당신을 의심한다
}

public sealed class RecallRequest
{
    public string EmployeeId = "";
    public int Day = 1;
    public RecallTopic Topic;
    // 답변이 가리키는 시각. 음수면 하루 전체를 본다.
    public float AnchorTime = -1f;
    // 답변이 가리키는 방(실제 위치 또는 결번자가 주장한 위치).
    public string AnchorRoom = "";
    // 결번자가 거짓 알리바이를 대는 중인가 — 그렇다면 AnchorRoom 이 주장한 방이다.
    public bool Lying;
    public bool IsSaboteur;
    public bool IsRepeat;
    // 핵심 답변이 이미 말한 기억 종류(같은 정보를 두 번 말하지 않는다).
    public readonly HashSet<MemoryKind> Covered = new();
    // 핵심 답변이 다루는 사건(그 사건 자체는 기억으로 다시 꺼내지 않는다).
    public string SubjectIncidentKey = "";
}

public sealed class RecallResult
{
    public readonly List<ReplyAddendum> Addenda = new();
    // 결번자 흉내의 어긋남: 겁먹어야 할 상황인데 놀란 기색을 빼 버린다.
    public bool SuppressFear;
}

// 직원 한 명의 "오늘 근무 기억".
//
// EventLog(배치 · 재배치 · 업무 · 수리 · 사고)와 CallMemoryLog(통화)에서 그 직원이 실제로 겪은
// 일만 시간순으로 모은다. 답변할 때는 질문 시각 근처에서 관련 있는 기억 1~2개를 골라
// 핵심 답변 뒤에 덧붙인다 — "환기실입니다." 가 "환기실입니다. 22시쯤 관리자님 전화 받고
// 계속 필터 쪽에 있었습니다." 가 되는 곳이 여기다.
//
// 지켜야 할 것
//   · 없는 일을 만들지 않는다. 기억은 로그와 통화 기록에서만 나온다.
//   · 결번자는 자기 방해공작을 기억으로 꺼내지 않는다.
//   · 결번자가 거짓 알리바이를 대는 동안에는 그 시간대의 "진짜 동선"을 꺼내지 않는다.
//     같이 있던 사람도 주장한 방 기준으로 댄다(그 사람이 실제로 그 방에 있었으니 확인하면 들통난다).
//   · 몇 개를, 무엇을 먼저 꺼내는지는 캐릭터 말투(DialogueVoiceDef)가 정한다.
public static class ShiftMemory
{
    // 질문 시각에서 이만큼(게임 분) 안의 기억만 "그때 일"로 꺼낸다.
    public const float RecallWindowMinutes = 20f;
    // 결번자가 거짓 알리바이를 대는 동안 진짜 동선을 숨기는 폭(게임 분).
    public const float LieWindowMinutes = 30f;
    // 흉내 어긋남이 한 답변에서 새어 나올 확률.
    public const float ImpostorSlipChance = 0.35f;

    // ── 기억 만들기 ──────────────────────────────────────────────────

    private sealed class Cached
    {
        public int LogCount;
        public int CallVersion;
        public List<MemoryItem> Items;
    }

    private static readonly Dictionary<string, Cached> _cache = new();

    public static void Invalidate() => _cache.Clear();

    public static List<MemoryItem> Of(string employeeId, int day)
    {
        var log = EventLog.Instance;
        int count = log?.GetAllEntries().Count ?? 0;
        string key = employeeId + "|" + day;
        if (_cache.TryGetValue(key, out var c) && c.LogCount == count && c.CallVersion == CallMemoryLog.Version)
            return c.Items;

        var items = Build(employeeId, day);
        _cache[key] = new Cached { LogCount = count, CallVersion = CallMemoryLog.Version, Items = items };
        return items;
    }

    private static List<MemoryItem> Build(string id, int day)
    {
        var list = new List<MemoryItem>();
        var log = EventLog.Instance;
        if (log != null)
        {
            foreach (var e in log.GetAllEntries())
            {
                if (e.Day != day) continue;
                bool mine = e.ActorEmployeeId == id;
                switch (e.EventType)
                {
                    case LogEventType.Relocation when mine && !string.IsNullOrEmpty(e.RoomId):
                        // 근무가 시작되기 전(배치표)의 배정과 근무 중 재배치를 가른다.
                        list.Add(new MemoryItem
                        {
                            Time = e.GameTimeSeconds, RoomId = e.RoomId,
                            Kind = e.GameTimeSeconds <= DialogueContextBuilder.ShiftStartSeconds
                                ? MemoryKind.Assigned : MemoryKind.Relocated,
                        });
                        break;

                    case LogEventType.TaskStart when mine:
                    {
                        var item = new MemoryItem
                        {
                            Time = e.GameTimeSeconds, RoomId = e.RoomId, Kind = MemoryKind.Worked,
                            IsRepair = (e.Description ?? "").StartsWith("🔧"),
                            TaskName = TaskNameFrom(e.Description),
                        };
                        item.With.AddRange(e.WitnessEmployeeIds.Where(w => w != id));
                        list.Add(item);
                        break;
                    }

                    case LogEventType.TaskComplete when (e.Description ?? "").Contains("수리 완료")
                                                        && DialogueContextBuilder.RoomAt(id, day, e.GameTimeSeconds) == e.RoomId:
                        list.Add(new MemoryItem { Time = e.GameTimeSeconds, RoomId = e.RoomId, Kind = MemoryKind.RepairDone });
                        break;
                }

                // 사고. 결번자는 자기가 한 방해공작을 "기억"으로 꺼내지 않는다.
                if (DialogueContextBuilder.IsIncident(e.EventType) && !(mine && e.EventType == LogEventType.Sabotage))
                {
                    var level = DialogueContextBuilder.KnowledgeOf(id, e);
                    if (level == KnowledgeLevel.None) continue;
                    list.Add(new MemoryItem
                    {
                        Time = e.GameTimeSeconds, RoomId = e.RoomId ?? "", IncidentType = e.EventType,
                        Kind = level == KnowledgeLevel.Direct ? MemoryKind.IncidentHere : MemoryKind.IncidentHeard,
                    });
                }
            }
        }

        foreach (var r in CallMemoryLog.For(id, day))
        {
            var kind = r.Kind switch
            {
                CallRecordKind.Reported => MemoryKind.CallReported,
                CallRecordKind.OrderedGo => MemoryKind.Dispatched,
                CallRecordKind.OrderedStay => MemoryKind.CallStay,
                CallRecordKind.Missed => MemoryKind.CallMissed,
                _ => MemoryKind.ManagerCalled,
            };
            list.Add(new MemoryItem { Time = r.Time, RoomId = r.RoomId, Kind = kind });
        }

        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    // 로그 원문에서 업무 이름을 집어낸다. 실제 TaskDef 이름과 먼저 대조한다.
    public static string TaskNameFrom(string description)
    {
        string d = (description ?? "").Replace("⚙", "").Replace("🔧", "").Trim();
        if (d.Length == 0) return "";
        var sim = FacilitySimulation.Instance;
        if (sim != null)
            foreach (var def in sim.GetTaskDefs())
                if (!string.IsNullOrEmpty(def?.DisplayName) && d.Contains(def.DisplayName))
                    return def.DisplayName;
        int slash = d.LastIndexOf(" / ", System.StringComparison.Ordinal);
        if (slash < 0) return "";
        d = d[(slash + 3)..].Trim();
        foreach (string tail in new[] { " 시작", " 수행", " 진행" })
            if (d.EndsWith(tail)) d = d[..^tail.Length].Trim();
        return d;
    }

    // ── 기억 꺼내기 ──────────────────────────────────────────────────

    private sealed class Candidate
    {
        public MemoryKind Group;
        public float Score;
        public ReplyAddendum Line;
    }

    public static RecallResult Recall(RecallRequest r)
    {
        var result = new RecallResult();
        if (r == null || string.IsNullOrEmpty(r.EmployeeId) || r.Topic == RecallTopic.None) return result;

        var voice = DialogueVoices.Get(r.EmployeeId);
        bool formal = voice.Formal;
        float spm = DialogueClock.SecondsPerMinute;
        float window = RecallWindowMinutes * spm;
        float lieWindow = LieWindowMinutes * spm;
        bool anchored = r.AnchorTime >= 0f;

        var items = Of(r.EmployeeId, r.Day).Where(m => Visible(m, r, lieWindow)).ToList();
        var cands = new List<Candidate>();

        void Add(MemoryKind group, float score, ReplyAddendum line)
        {
            if (r.Covered.Contains(group) || score <= 0f || line == null) return;
            line.Kind = group;
            cands.Add(new Candidate { Group = group, Score = score, Line = line });
        }

        // 질문 시각과 가까울수록 떠올리기 쉽다. 시각이 없는 질문(하루 전체)은 거리를 보지 않는다.
        float Near(float t)
        {
            if (!anchored) return 1f;
            float d = Mathf.Abs(t - r.AnchorTime);
            return d > window ? 0f : Mathf.Lerp(1f, 0.3f, d / window);
        }

        // ── 1) 그 시각 같은 방에 있던 사람(모든 시각 질문에 공통) ─────────
        bool companionTopic = r.Topic is RecallTopic.Location or RecallTopic.Presence or RecallTopic.Anomaly
            or RecallTopic.Accuse or RecallTopic.Movement;
        if (anchored && companionTopic && !string.IsNullOrEmpty(r.AnchorRoom))
        {
            var others = DialogueContextBuilder.OccupantsAt(r.AnchorRoom, r.Day, r.AnchorTime, r.EmployeeId);
            // 사고가 난 바로 그 방이면, 같이 있던 사람의 반응까지 이야기할 수 있다.
            bool incidentHere = items.Any(m => m.Kind == MemoryKind.IncidentHere && m.RoomId == r.AnchorRoom
                                               && Near(m.Time) > 0.6f);
            // "이상한 일" 이야기 뒤의 동석자는 그 사고가 난 바로 그 방일 때만 말이 된다.
            // 옆방 소리 이야기 뒤에 "고양이 씨도 거기 있었어요" 가 붙으면 사고 현장에 있었다는 말로 들린다.
            if (r.Topic == RecallTopic.Anomaly && !incidentHere) others = null;
            ReplyAddendum line = null;
            if (others == null) { }
            else if (others.Count >= 2)
                line = new ReplyAddendum { Slot = "mem.with.two" }.Set("who", Name(others[0])).Set("who2", Name(others[1]));
            else if (others.Count == 1)
                line = new ReplyAddendum { Slot = incidentHere ? "mem.with.incident" : "mem.with" }.Set("who", Name(others[0]));
            else
                line = new ReplyAddendum { Slot = "mem.alone" };
            // 이 세션(같은 시간대)에서 같은 방 · 같은 동료 이야기를 이미 했으면 다시 꺼내지 않는다.
            if (line != null)
            {
                line.SpokenKey = SpokenKey(r.EmployeeId, r.Day, Bucket(r.AnchorTime), MemoryKind.Companion,
                    r.AnchorRoom, others.Take(2));
                if (WasSaid(line)) line = null;
            }
            if (line != null)
            {
                line.Set("room", RoomName(r.AnchorRoom));
                // 의심받을 때 "같이 있던 사람"은 가장 중요한 해명이다.
                float boost = r.Topic == RecallTopic.Accuse ? 1.8f : 1f;
                Add(MemoryKind.Companion, boost * OthersWeight(voice) * (others.Count == 0 ? 0.6f : 1f), line);
            }
        }

        // ── 2) 로그 · 통화 기억 ────────────────────────────────────────
        foreach (var m in items)
        {
            float near = Near(m.Time);
            if (near <= 0f) continue;
            float relevance = Relevance(r.Topic, m.Kind);
            if (relevance <= 0f) continue;

            switch (m.Kind)
            {
                case MemoryKind.Worked:
                    // 핵심 답변이 말하는 방에서 한 일만 — 다른 방 이야기는 앞말과 어긋난다.
                    if (!string.IsNullOrEmpty(r.AnchorRoom) && m.RoomId != r.AnchorRoom) break;
                    if (string.IsNullOrEmpty(m.TaskName)) break;
                    Add(MemoryKind.Worked, relevance * near * WorkWeight(voice),
                        new ReplyAddendum { Slot = m.IsRepair ? "mem.worked.repair" : "mem.worked" }
                            .Set("task", m.TaskName).Set("room", RoomName(m.RoomId)));
                    break;

                case MemoryKind.Relocated:
                    if (anchored && m.Time > r.AnchorTime) break;   // 그 뒤에 옮긴 건 "그때" 이야기가 아니다
                    if (!string.IsNullOrEmpty(r.AnchorRoom) && m.RoomId != r.AnchorRoom) break;
                    Add(MemoryKind.Relocated, relevance * near * CallWeight(voice),
                        new ReplyAddendum { Slot = "mem.relocated" }
                            .Set("room", RoomName(m.RoomId)).Set("time", KoreanParticle.TimePhrase(m.Time, formal)));
                    break;

                case MemoryKind.Dispatched:
                    Add(MemoryKind.Dispatched, relevance * near * CallWeight(voice),
                        new ReplyAddendum { Slot = "mem.dispatched" }.Set("iroom", RoomName(m.RoomId)));
                    break;

                case MemoryKind.CallStay:
                    Add(MemoryKind.CallStay, relevance * near * CallWeight(voice),
                        new ReplyAddendum { Slot = "mem.call.stay" }.Set("iroom", RoomName(m.RoomId)));
                    break;

                case MemoryKind.CallReported:
                    Add(MemoryKind.CallReported, relevance * near * CallWeight(voice),
                        new ReplyAddendum { Slot = "mem.call.reported" }.Set("iroom", RoomName(m.RoomId)));
                    break;

                case MemoryKind.CallMissed:
                    Add(MemoryKind.CallMissed, relevance * near * CallWeight(voice) * 1.2f,
                        new ReplyAddendum { Slot = "mem.call.missed" }.Set("iroom", RoomName(m.RoomId)));
                    break;

                case MemoryKind.ManagerCalled:
                    Add(MemoryKind.ManagerCalled, relevance * near * CallWeight(voice) * 0.7f,
                        new ReplyAddendum { Slot = "mem.called" }
                            .Set("room", RoomName(string.IsNullOrEmpty(r.AnchorRoom) ? m.RoomId : r.AnchorRoom)));
                    break;

                case MemoryKind.RepairDone:
                    if (!string.IsNullOrEmpty(r.AnchorRoom) && m.RoomId != r.AnchorRoom) break;
                    Add(MemoryKind.RepairDone, relevance * near * WorkWeight(voice),
                        new ReplyAddendum { Slot = "mem.repair" }.Set("room", RoomName(m.RoomId)));
                    break;

                case MemoryKind.IncidentHeard:
                    if (IsSubject(m, r)) break;
                    Add(MemoryKind.IncidentHeard, relevance * near * HeardWeight(r.EmployeeId),
                        new ReplyAddendum { Slot = "mem.heard" }.Set("iroom", RoomName(m.RoomId)));
                    break;

                case MemoryKind.IncidentHere:
                    if (IsSubject(m, r)) break;
                    Add(MemoryKind.IncidentHere, relevance * near,
                        new ReplyAddendum { Slot = "mem.incident.here" }.Set("room", RoomName(m.RoomId)));
                    break;
            }
        }

        // ── 3) 하루 전체를 돌아보는 질문 ────────────────────────────────
        if (r.Topic is RecallTopic.ShiftReview or RecallTopic.Suspicious)
            AddDayLevel(r, items, voice, Add);

        // 떠올릴 게 동료 이야기 하나뿐이면 그대로 뽑혀 버린다(가중치는 후보끼리의 비율일 뿐이다).
        // 남 이야기를 꺼내는 성향(MentionOthersChance)을 한 번 더 거친다.
        if (cands.Count == 1 && cands[0].Group is MemoryKind.Companion or MemoryKind.DayCompanion
            && GD.Randf() >= voice.MentionOthersChance)
            cands.Clear();

        // ── 4) 몇 개를 꺼낼까 — 말투가 정한다 ────────────────────────────
        int budget = r.IsRepeat ? 0 : Budget(voice);
        var picked = PickWeighted(cands, budget);

        // ── 5) 결번자 흉내의 어긋남 ────────────────────────────────────
        if (r.IsSaboteur && r.Lying && GD.Randf() < ImpostorSlipChance)
            ApplyImpostorTell(r, voice, picked, result);

        foreach (var c in picked) result.Addenda.Add(c.Line);
        return result;
    }

    // 결번자가 거짓 알리바이를 대는 동안에는 그 시간대의 진짜 동선을 꺼내지 않는다.
    private static bool Visible(MemoryItem m, RecallRequest r, float lieWindow)
    {
        if (!r.Lying || r.AnchorTime < 0f) return true;
        if (Mathf.Abs(m.Time - r.AnchorTime) > lieWindow) return true;
        return m.Kind switch
        {
            // 통화는 관리자도 아는 일이라 숨길 이유가 없다 — 다만 방 이름은 주장과 맞아야 한다.
            MemoryKind.CallReported or MemoryKind.CallMissed or MemoryKind.ManagerCalled
                or MemoryKind.CallStay => true,
            MemoryKind.IncidentHeard or MemoryKind.IncidentHere => false,
            _ => m.RoomId == r.AnchorRoom,
        };
    }

    private static bool IsSubject(MemoryItem m, RecallRequest r) =>
        !string.IsNullOrEmpty(r.SubjectIncidentKey)
        && $"{m.IncidentType}:{m.RoomId}:{m.Time:0.0}" == r.SubjectIncidentKey;

    // 질문 주제별로 어떤 기억이 자연스럽게 따라 나오는가(0 이면 꺼내지 않는다).
    private static float Relevance(RecallTopic topic, MemoryKind kind) => topic switch
    {
        RecallTopic.Location => kind switch
        {
            MemoryKind.Worked => 1.2f, MemoryKind.Relocated => 1f, MemoryKind.Dispatched => 1f,
            MemoryKind.ManagerCalled => 0.8f, MemoryKind.CallReported => 0.8f, MemoryKind.CallStay => 0.8f,
            MemoryKind.IncidentHeard => 0.6f, MemoryKind.RepairDone => 0.7f,
            _ => 0f,
        },
        RecallTopic.Anomaly => kind switch
        {
            MemoryKind.CallReported => 1.3f, MemoryKind.Dispatched => 1.3f, MemoryKind.CallStay => 1.1f,
            MemoryKind.CallMissed => 1.4f, MemoryKind.RepairDone => 1f, MemoryKind.IncidentHeard => 0.7f,
            MemoryKind.IncidentHere => 0.7f,
            _ => 0f,
        },
        RecallTopic.Presence => kind switch
        {
            MemoryKind.Worked => 1.1f, MemoryKind.Relocated => 1f, MemoryKind.Dispatched => 1f,
            MemoryKind.ManagerCalled => 0.7f, MemoryKind.RepairDone => 0.8f, MemoryKind.IncidentHeard => 0.5f,
            _ => 0f,
        },
        RecallTopic.Movement => kind switch
        {
            MemoryKind.Worked => 1f, MemoryKind.RepairDone => 0.8f, MemoryKind.ManagerCalled => 0.5f,
            _ => 0f,
        },
        RecallTopic.Status => kind switch
        {
            MemoryKind.Relocated => 1.2f, MemoryKind.Dispatched => 1.2f, MemoryKind.RepairDone => 1f,
            MemoryKind.Worked => 0.6f,
            _ => 0f,
        },
        RecallTopic.Accuse => kind switch
        {
            MemoryKind.Worked => 0.8f, MemoryKind.Relocated => 0.8f, MemoryKind.Dispatched => 0.8f,
            MemoryKind.ManagerCalled => 0.6f,
            _ => 0f,
        },
        _ => 0f,
    };

    private static void AddDayLevel(RecallRequest r, List<MemoryItem> items, DialogueVoiceDef voice,
        System.Action<MemoryKind, float, ReplyAddendum> add)
    {
        // 오늘 가장 자주 같이 일한 사람.
        var partner = items.Where(m => m.Kind == MemoryKind.Worked)
            .SelectMany(m => m.With)
            .GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        if (!string.IsNullOrEmpty(partner))
        {
            var line = new ReplyAddendum
            {
                Slot = "mem.with.day",
                SpokenKey = SpokenKey(r.EmployeeId, r.Day, DayBucket, MemoryKind.DayCompanion, "", new[] { partner }),
            };
            if (!WasSaid(line)) add(MemoryKind.DayCompanion, OthersWeight(voice) * 0.9f, line.Set("who", Name(partner)));
        }

        if (r.Topic != RecallTopic.ShiftReview) return;

        int moves = items.Count(m => m.Kind is MemoryKind.Relocated or MemoryKind.Dispatched);
        if (moves >= 2)
            add(MemoryKind.Summary, 1.1f * CallWeight(voice),
                new ReplyAddendum { Slot = "mem.sum.moves" }.Set("n", KoreanParticle.Count(moves)));

        var repair = items.LastOrDefault(m => m.Kind == MemoryKind.RepairDone);
        if (repair != null)
            add(MemoryKind.RepairDone, 1f * WorkWeight(voice),
                new ReplyAddendum { Slot = "mem.repair" }.Set("room", RoomName(repair.RoomId)));

        int reported = items.Count(m => m.Kind == MemoryKind.CallReported);
        int missed = items.Count(m => m.Kind == MemoryKind.CallMissed);
        if (missed > 0)
            add(MemoryKind.CallMissed, 1.2f * CallWeight(voice), new ReplyAddendum { Slot = "mem.call.missed" });
        else if (reported >= 2)
            add(MemoryKind.CallReported, 1f * CallWeight(voice),
                new ReplyAddendum { Slot = "mem.sum.calls" }.Set("n", KoreanParticle.Count(reported)));
    }

    // ── 이미 한 이야기 ────────────────────────────────────────────────
    //
    // 동료 기억은 한 세션(직원 · 날 · 주제 시각)에 한 번만 한다. "주제 시각"은 SpokenBucketMinutes
    // 단위로 묶고, 바로 옆 구간까지 같은 시간대로 본다(22:39 와 22:41 이 다른 시간대가 되지 않게).
    // 실제로 답에 들어간 줄만 DialogueComposer 가 MarkSaid 로 적는다.
    public const float SpokenBucketMinutes = 10f;
    private const int DayBucket = int.MinValue;   // 하루 전체를 돌아보는 이야기(시각 없음)

    private static int Bucket(float time) =>
        time < 0f ? DayBucket : Mathf.FloorToInt(time / (SpokenBucketMinutes * DialogueClock.SecondsPerMinute));

    private static string SpokenKey(string employeeId, int day, int bucket, MemoryKind kind, string room,
        IEnumerable<string> who) =>
        $"{employeeId}|{day}|{bucket}|{kind}|{room}|{string.Join("+", who.OrderBy(x => x))}";

    // 같은 이야기를 이 시간대(앞뒤 구간 포함)에 이미 했는가.
    public static bool WasSaid(ReplyAddendum a)
    {
        if (a == null || string.IsNullOrEmpty(a.SpokenKey)) return false;
        var p = a.SpokenKey.Split('|');
        if (p.Length < 3 || !int.TryParse(p[2], out int bucket)) return DialogueClaimState.WasSpoken(a.SpokenKey);
        if (bucket == DayBucket) return DialogueClaimState.WasSpoken(a.SpokenKey);
        // 그 시간대의 "같이 있던 사람"을 플레이어가 직접 물었으면 그것도 이미 한 이야기다.
        if (p.Length > 3 && p[3] == nameof(MemoryKind.Companion)
            && int.TryParse(p[1], out int day) && CompanionAsked(p[0], day, bucket))
            return true;
        for (int d = -1; d <= 1; d++)
        {
            p[2] = (bucket + d).ToString();
            if (DialogueClaimState.WasSpoken(string.Join("|", p))) return true;
        }
        return false;
    }

    public static void MarkSaid(ReplyAddendum a)
    {
        if (a != null) DialogueClaimState.MarkSpoken(a.SpokenKey);
    }

    // 플레이어가 "그때 같이 있던 사람"을 직접 물었다 — 그 시간대의 다른 답에는 동료 이야기를 덧붙이지 않는다.
    public static void MarkCompanionAsked(string employeeId, int day, float time) =>
        DialogueClaimState.MarkSpoken($"{employeeId}|{day}|{Bucket(time)}|asked");

    public static bool CompanionAsked(string employeeId, int day, float time) =>
        CompanionAsked(employeeId, day, Bucket(time));

    private static bool CompanionAsked(string employeeId, int day, int bucket)
    {
        if (bucket == DayBucket) return false;
        for (int d = -1; d <= 1; d++)
            if (DialogueClaimState.WasSpoken($"{employeeId}|{day}|{bucket + d}|asked")) return true;
        return false;
    }

    // ── 말투에 따른 가중치 ──────────────────────────────────────────────

    // 한 답변에 덧붙이는 기억의 수: 늑대·고양이는 0~1, 양·여우는 대개 1, 토끼·강아지는 1~2.
    private static int Budget(DialogueVoiceDef v)
    {
        float p1 = Mathf.Clamp(0.25f + v.VolunteerInfoChance * 0.6f + v.Talkativeness * 0.4f, 0f, 0.95f);
        float p2 = Mathf.Clamp((v.Talkativeness - 0.35f) * 1.2f, 0f, 0.6f);
        if (GD.Randf() >= p1) return 0;
        return GD.Randf() < p2 ? 2 : 1;
    }

    private static float OthersWeight(DialogueVoiceDef v) => 0.4f + v.MentionOthersChance * 1.6f;
    private static float CallWeight(DialogueVoiceDef v) => 0.6f + v.Talkativeness * 0.8f;
    private static float WorkWeight(DialogueVoiceDef v) => 0.5f + v.Directness * 0.8f + v.OfferActionChance * 0.3f;

    // 옆방 소리는 예민한 사람일수록 먼저 떠올린다.
    private static float HeardWeight(string employeeId) =>
        0.3f + EmployeeTraits.Get(employeeId).ObservationalAwareness * 0.3f;

    // 점수에 비례해 뽑되, 같은 종류는 한 번만.
    private static List<Candidate> PickWeighted(List<Candidate> cands, int budget)
    {
        var picked = new List<Candidate>();
        var pool = cands.ToList();
        while (picked.Count < budget && pool.Count > 0)
        {
            float total = pool.Sum(c => c.Score);
            float roll = GD.Randf() * total;
            Candidate chosen = pool[^1];
            foreach (var c in pool)
            {
                roll -= c.Score;
                if (roll <= 0f) { chosen = c; break; }
            }
            picked.Add(chosen);
            pool.RemoveAll(c => c.Group == chosen.Group);
        }
        return picked;
    }

    // 결번자가 이 직원 행세를 하다 새는 미세한 어긋남(DialogueVoiceDef.ImpostorTells).
    // 한 번에 하나만, 정상 성격의 연장선에서 살짝 틀어진 정도로만.
    private static void ApplyImpostorTell(RecallRequest r, DialogueVoiceDef v, List<Candidate> picked, RecallResult result)
    {
        var tells = v.ImpostorTells;
        if (tells == ImpostorTell.None) return;

        // 사람에 대한 기억이 비어 있다 — 누가 있었는지 물으면 뭉뚱그린다.
        if (tells.HasFlag(ImpostorTell.VagueAboutPeople))
        {
            int at = picked.FindIndex(c => c.Group is MemoryKind.Companion or MemoryKind.DayCompanion);
            if (at >= 0)
            {
                picked[at] = Slip("mem.with.vague");
                return;
            }
        }
        // 걱정은 하는데 구체적인 관찰이 없다.
        if (tells.HasFlag(ImpostorTell.ConcernWithoutDetail) && r.Topic is RecallTopic.Anomaly or RecallTopic.ShiftReview)
        {
            picked.RemoveAll(c => c.Group is MemoryKind.Companion or MemoryKind.DayCompanion);
            picked.Add(Slip("slip.concern.vague"));
            return;
        }
        // 평소보다 지나치게 단정한다.
        if (tells.HasFlag(ImpostorTell.OverAssertive) && r.Topic is RecallTopic.Location or RecallTopic.Accuse)
        {
            picked.Add(Slip("slip.assert"));
            return;
        }
        // 겁먹어야 할 상황인데 침착하다.
        if (tells.HasFlag(ImpostorTell.CalmWhereShouldFear))
            result.SuppressFear = true;

        static Candidate Slip(string slot) => new()
        {
            Group = MemoryKind.Slip, Score = 1f,
            Line = new ReplyAddendum { Slot = slot, Kind = MemoryKind.Slip },
        };
    }

    private static string RoomName(string roomId) => InterviewEvidenceBoard.RoomName(roomId);
    private static string Name(string employeeId) => InterviewEvidenceBoard.Codename(employeeId);
}
