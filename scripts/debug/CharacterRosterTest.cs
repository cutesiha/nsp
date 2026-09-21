using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 직원 6인 교체(해파리→양, 올빼미→늑대, 까마귀→강아지) 검증.
//
//   godot --headless --path . res://scenes/debug/CharacterRosterTest.tscn
//
// 게임 흐름에서 로드되지 않는다. 결번자 자율 이동(Test H)의 긴 검증은 SaboteurClueTest 가
// 새 6명을 번갈아 결번자로 세워 12번 근무를 돌리며 따로 본다.
public partial class CharacterRosterTest : Node
{
    private static readonly string[] Final = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };
    private static readonly string[] NewThree = { "sheep", "wolf", "dog" };
    private static readonly string[] Retired = { "owl", "crow", "jellyfish" };
    private static readonly string[] RetiredNames = { "올빼미", "까마귀", "해파리" };

    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 직원 6인 교체 검증 ################");
        TestA_Roster();
        TestB_NoRetired();
        TestC_Assign();
        TestD_LogAndCctv();
        TestE_Interview();
        TestF_Evidence();
        TestG_SaboteurMimic();
        TestH_NoAutonomousMove();
        TestDay0();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ── A : 새 게임의 직원은 정확히 6명 ────────────────────────────────
    private void TestA_Roster()
    {
        Head("A", "새 게임 직원 = rabbit cat fox sheep wolf dog");
        NewGame(1);
        var ids = _sim.GetEmployeeIds().OrderBy(x => x).ToList();
        GD.Print($"   런타임 직원: {string.Join(", ", ids)}");
        Check(ids.SequenceEqual(Final.OrderBy(x => x)), "직원 id 가 정확히 6명");
        Check(_sim.GetActiveEmployeeIds().Count == 6, "DAY1 근무 인원 6명");
        foreach (string id in NewThree)
        {
            var def = _sim.GetEmployeeDef(id);
            Check(def != null && !string.IsNullOrEmpty(def.Codename) && def.StandingImage != null
                  && def.FacePortrait != null,
                $"{id} 데이터·스탠딩·얼굴 로드 ({def?.Codename})");
        }
        // 대사·성향 표도 같은 6명을 안다.
        Check(Final.All(id => DialogueVoices.Get(id).EmployeeId == id), "말투(voice .tres) 6명 전부 로드");
        Check(Final.All(id => EmployeeTraits.All.ContainsKey(id)) && EmployeeTraits.All.Count == 6,
            "행동 성향표 6명");
        Check(Final.All(id => DialogueVoiceProfiles.Ids.Contains(id)) && DialogueVoiceProfiles.Ids.Count == 6,
            "대화 계획 프로필 6명");
        _sim.RollDailyMoods();
        Check(Final.All(id => !string.IsNullOrEmpty(_sim.GetEmployeeState(id).DailyMood)),
            "6명 모두 오늘의 기분이 뽑힌다");
        foreach (string id in NewThree)
        {
            var v = DialogueVoices.Get(id);
            GD.Print($"   {id,-6} 축=[{v.VoiceAxis}] 시작={v.LeadWith} 확신={v.Confidence} " +
                     $"타인언급={v.MentionOthersChance} 행동제안={v.OfferActionChance} 흉내어긋남={v.ImpostorTells}");
            Check(!string.IsNullOrEmpty(v.VoiceAxis) && v.ImpostorTells != ImpostorTell.None,
                $"{id} V2 캐릭터 축·결번자 어긋남 데이터가 있다");
        }
    }

    // ── B : 옛 직원이 어디에도 생성되지 않는다 ──────────────────────────
    private void TestB_NoRetired()
    {
        Head("B", "owl / crow / jellyfish 런타임 생성 없음");
        foreach (string id in Retired)
        {
            Check(_sim.GetEmployeeDef(id) == null && _sim.GetEmployeeState(id) == null, $"{id} 직원 없음");
            Check(DialogueVoices.Get(id).EmployeeId != id, $"{id} 말투 데이터 없음");
            Check(!EmployeeTraits.All.ContainsKey(id), $"{id} 성향 없음");
        }
        // 화면에 뜨는 인사·코드네임에 옛 이름이 없다.
        var shown = Final.Select(id => _sim.GetEmployeeDef(id).Codename)
            .Concat(Final.Select(LocalDialogueGenerator.InterviewGreeting))
            .Concat(Final.Select(LocalDialogueGenerator.GeneralGreeting)).ToList();
        Check(!shown.Any(HasRetiredName), "코드네임·인사말에 옛 이름이 없다");
    }

    // ── C : 새 세 명 모두 배치 가능 ─────────────────────────────────────
    private void TestC_Assign()
    {
        Head("C", "양·늑대·강아지 작업실 배치");
        NewGame(1);
        var rooms = new[] { Power, Maintenance, Storage };
        for (int i = 0; i < NewThree.Length; i++)
        {
            bool ok = _sim.AssignToRoom(NewThree[i], rooms[i]);
            Check(ok && _sim.GetEmployeeState(NewThree[i]).AssignedRoomId == rooms[i],
                $"{NewThree[i]} → {rooms[i]} 배치");
        }
    }

    // ── D : CCTV / 시설 로그 표시 ──────────────────────────────────────
    private void TestD_LogAndCctv()
    {
        Head("D", "시설 로그 · CCTV 표시");
        NewGame(1);
        Deploy();
        foreach (string id in NewThree) Move(id, _sim.GetEmployeeState(id).CurrentRoomId, Core, At(20));
        var rows = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1);
        foreach (string id in NewThree)
        {
            var row = rows.FirstOrDefault(r => r.RelatedEmployeeId == id && r.ToRoomId == Core);
            string name = _sim.GetEmployeeDef(id).Codename;
            if (row != null) GD.Print($"   로그: {row.Text}");
            Check(row != null && row.Text.Contains(name), $"{name} 이동이 시설 로그에 코드네임으로 뜬다");
        }
        Check(!rows.Any(r => HasRetiredName(r.Text)), "시설 로그에 옛 이름이 없다");

        // CCTV 는 방 점유자 id → EmployeeDef(코드네임·고유색·원화)로 그린다.
        foreach (string id in NewThree)
        {
            var st = _sim.GetEmployeeState(id);
            st.CurrentRoomId = Core;
        }
        PlayerKnownEvidence.RecordCctvObservation(Core, At(21), NewThree);
        foreach (string id in NewThree)
            Check(PlayerKnownEvidence.CctvSightingsOf(id).Count > 0 && _sim.GetEmployeeDef(id).IconColor.A > 0,
                $"{id} CCTV 관측 기록 · 고유색");
    }

    // ── E : 휴게시간 인터뷰 ─────────────────────────────────────────────
    private void TestE_Interview()
    {
        Head("E", "양·늑대·강아지 휴게시간 인터뷰");
        NewGame(1);
        Deploy();
        Log(LogEventType.TaskFailed, "", Power, At(15));
        foreach (string id in NewThree)
        {
            var s = new InterviewSession(id);
            var qs = s.BasicQuestions();
            if (!Check(qs.Count > 0, $"{id} 기본 질문이 뜬다")) continue;
            GD.Print($"   [{_sim.GetEmployeeDef(id).Codename}] 인사: {s.Greeting()}");
            foreach (var q in qs.Take(3))
            {
                var turn = s.Ask(q);
                GD.Print($"     Q: {turn.QuestionText}\n     A: {turn.Answer}");
                Check(!string.IsNullOrWhiteSpace(turn.Answer) && turn.Answer != "…" && !HasRetiredName(turn.Answer),
                    $"{id} 답변 생성");
            }
            // 옛 조각 조립기(일반 통화 · 기본 질문)도 새 id 로 돈다.
            string general = LocalDialogueGenerator.Interview(id, DialogueQuestions.Anomaly)?.Answer ?? "";
            GD.Print($"     (조각 조립기) {general}");
            Check(!string.IsNullOrWhiteSpace(general) && general != "…", $"{id} 조각 조립기 답변");
        }
    }

    // ── F : PlayerKnownEvidence 의 id 로 쓰인다 ──────────────────────────
    private void TestF_Evidence()
    {
        Head("F", "PlayerKnownEvidence · 증언 자료");
        NewGame(1);
        Deploy();
        PlayerKnownEvidence.RecordSighting("sheep", "wolf", Maintenance, At(18));
        PlayerKnownEvidence.RecordSighting("wolf", "dog", Guard, At(19));
        PlayerKnownEvidence.RecordSighting("dog", "sheep", Power, At(20));
        PlayerKnownEvidence.RecordCctvObservation(Guard, At(22), new[] { "dog" });

        Check(InterviewEvidenceBoard.Build("wolf").Any(e => e.Kind == EvidenceKind.Testimony && e.SpeakerEmployeeId == "sheep"),
            "양의 증언이 늑대 자료가 된다");
        Check(InterviewEvidenceBoard.Build("dog").Any(e => e.Kind == EvidenceKind.Testimony && e.SpeakerEmployeeId == "wolf"),
            "늑대의 증언이 강아지 자료가 된다");
        Check(InterviewEvidenceBoard.Build("sheep").Any(e => e.Kind == EvidenceKind.Testimony && e.SpeakerEmployeeId == "dog"),
            "강아지의 증언이 양 자료가 된다");
        Check(InterviewEvidenceBoard.Build("dog").Any(e => e.Kind == EvidenceKind.Cctv), "강아지 CCTV 자료");
        var header = InterviewEvidenceBoard.Build("wolf").First(e => e.Kind == EvidenceKind.Testimony);
        GD.Print($"   자료: {header.OneLine}");
        Check(!HasRetiredName(header.OneLine), "자료 문구에 옛 이름이 없다");
    }

    // ── G : 결번자가 새 세 명 중 하나여도 주장·자료가 깨지지 않는다 ───────────
    private void TestG_SaboteurMimic()
    {
        Head("G", "결번자 = 양 / 늑대 / 강아지");
        foreach (string sab in NewThree)
        {
            NewGame(1);
            Deploy();
            string assigned = _sim.GetEmployeeState(sab).AssignedRoomId;
            GameState.Instance.SetSaboteur(sab);
            Move(sab, assigned, Storage, At(8));
            Log(LogEventType.Sabotage, sab, Storage, At(12));
            Log(LogEventType.TaskFailed, "", Power, At(16));

            var board = InterviewEvidenceBoard.Build(sab);
            var incident = board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident);
            if (!Check(incident != null, $"[{sab}] 사고 자료가 뜬다")) continue;

            var q1 = InterviewQuestionFactory.Make(sab, incident, InterviewIntent.AskWhereAtIncident);
            string a1 = InterviewReplyPlanner.Answer(q1);
            string claim1 = InterviewReplyPlanner.FrameFor(q1).Vars.GetValueOrDefault("room", "");
            var q2 = InterviewQuestionFactory.Make(sab, incident, InterviewIntent.AskWhereAtIncident);
            string claim2 = InterviewReplyPlanner.FrameFor(q2).Vars.GetValueOrDefault("room", "");
            GD.Print($"   [{sab}] Q: {q1.Text}\n   A: {a1}  (주장={claim1})");
            Check(!string.IsNullOrEmpty(a1) && claim1 == claim2, $"[{sab}] 다시 물어도 같은 위치를 주장한다");

            // 옛 조각 조립기 경로의 알리바이(DialogueClaimState)도 확인.
            LocalDialogueGenerator.Interview(sab, DialogueQuestions.Where);
            LocalDialogueGenerator.Interview(sab, DialogueQuestions.Where);
            bool anyClaim = true;
            try { DialogueClaimState.Get(sab, 1, incident.IncidentKey); }
            catch (System.Exception) { anyClaim = false; }
            Check(anyClaim, $"[{sab}] DialogueClaimState 조회가 깨지지 않는다");

            // 모순 판정도 새 id 로 돈다(성립 여부가 아니라 예외 없이 결과가 나오는지).
            PlayerKnownEvidence.RecordCctvObservation(Storage, At(12), new[] { sab });
            var cctv = InterviewEvidenceBoard.Build(sab).FirstOrDefault(e => e.Kind == EvidenceKind.Cctv);
            bool ran = true;
            try { if (cctv != null) EvidenceContradiction.Check(sab, incident, cctv); }
            catch (System.Exception) { ran = false; }
            Check(ran, $"[{sab}] 모순 판정이 예외 없이 돈다");
        }
    }

    // ── H : 직원은 스스로 방을 옮기지 않는다 ───────────────────────────
    private void TestH_NoAutonomousMove()
    {
        Head("H", "근무 60초 동안 자율 이동 없음(결번자 = 양·늑대·강아지 순서)");
        foreach (string sab in NewThree)
        {
            NewGame(1);
            GameState.Instance.SetPhase(GamePhase.Schedule);
            var plan = new Dictionary<string, string>
            {
                ["rabbit"] = Core, ["cat"] = Core, ["fox"] = Maintenance,
                ["sheep"] = Storage, ["wolf"] = Power, ["dog"] = Guard,
            };
            foreach (var kv in plan) _sim.AssignToRoom(kv.Key, kv.Value);
            GameState.Instance.SetSaboteur(sab);
            GameState.Instance.SetPhase(GamePhase.Live);

            int wrong = 0;
            const float step = 1f / 30f;
            for (float t = 0f; t < 60f; t += step)
            {
                GameState.Instance.AdvanceDayTime(step);
                _sim.Tick(step);
                if (t < 20f) continue;   // 첫 배치 이동이 끝날 때까지 기다린다
                foreach (var kv in plan)
                {
                    var st = _sim.GetEmployeeState(kv.Key);
                    if (st.Alive && !st.Isolated && st.AssignedRoomId != kv.Value) wrong++;
                }
            }
            Check(wrong == 0, $"[결번자 {sab}] 배치가 플레이어 명령 없이 바뀌지 않는다 (위반 {wrong})");
        }
    }

    // ── DAY0 : 교육 인원은 그대로 ─────────────────────────────────────
    private void TestDay0()
    {
        Head("DAY0", "교육 인원(토끼·고양이·여우) 유지");
        NewGame(0);
        var day0 = _sim.GetActiveEmployeeIds().OrderBy(x => x).ToList();
        GD.Print($"   DAY0 인원: {string.Join(", ", day0)}");
        Check(day0.SequenceEqual(new[] { "cat", "fox", "rabbit" }), "DAY0 = cat, fox, rabbit");
        NewGame(1);
    }

    // ── helpers ──────────────────────────────────────────────────────
    private void NewGame(int day)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        PlayerKnownEvidence.ResetAll();
        _sim.RollDailyMoods();
    }

    private void Deploy()
    {
        var rooms = new Dictionary<string, string>
        {
            ["rabbit"] = Maintenance, ["cat"] = Storage, ["fox"] = Core,
            ["sheep"] = Power, ["wolf"] = Maintenance, ["dog"] = Guard,
        };
        foreach (var kv in rooms)
        {
            var st = _sim.GetEmployeeState(kv.Key);
            st.AssignedRoomId = kv.Value;
            st.CurrentRoomId = kv.Value;
            st.Alive = true;
            st.Isolated = false;
        }
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
    }

    private static void Move(string employeeId, string from, string to, float arriveAt)
    {
        Log(LogEventType.RoomExit, employeeId, from, Mathf.Max(0f, arriveAt - 1f));
        Log(LogEventType.RoomEnter, employeeId, to, arriveAt);
    }

    private static void Log(LogEventType type, string actor, string roomId, float at)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1,
            GameTimeSeconds = at,
            EventType = type,
            ActorEmployeeId = actor,
            RoomId = roomId,
            Description = $"(테스트 {type} {roomId} {at:0.0})",
            WitnessEmployeeIds = new List<string>(),
        });
    }

    private static float At(int gameMinutes) => gameMinutes * DialogueClock.SecondsPerMinute;

    private static bool HasRetiredName(string text) =>
        !string.IsNullOrEmpty(text) && RetiredNames.Any(text.Contains);

    private static void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }
}
