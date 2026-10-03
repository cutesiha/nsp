using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 캐릭터 화법 품질 검증 (Test A~I) + 6명 비교 샘플 출력.
//
//   godot --headless --path . scenes/debug/VoiceStyleTest.tscn --quit-after 900
//
// 사실이 맞는지는 DialogueScenarioTest 가 본다. 여기서 보는 것은 하나다 —
// "사람이 이렇게 말하는가, 그리고 여섯 명이 서로 다르게 말하는가".
public partial class VoiceStyleTest : Node
{
    private const int Samples = 20;
    private static readonly string[] Ids = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private const string Power = "power_room";
    private const string Storage = "storage_room";
    private const string Maintenance = "maintenance_room";
    private const string Guard = "guard_room";
    private const string Core = "core_room";

    private int _pass, _fail;

    public override void _Ready()
    {
        if (FacilitySimulation.Instance == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 캐릭터 화법 검증 ################");
        Setup();

        PrintCompare("이상현상은 없었습니까?", DialogueQuestions.Anomaly);
        PrintCompare("그때 어디에 있었습니까?", DialogueQuestions.Where);
        PrintCompare("수상한 행동을 한 사람을 봤습니까?", DialogueQuestions.Suspicious);
        PrintCompare("확실합니까?", DialogueQuestions.FollowUpPrefix + FollowUpIntent.AskCertainty);
        PrintCompare("당신을 의심하고 있습니다.", DialogueQuestions.Accuse);
        PrintCompare("그렇다면 어떻게 설명하시겠습니까?",
            DialogueQuestions.FollowUpPrefix + FollowUpIntent.AskDefense);
        PrintRepeat();

        var corpus = Collect();
        CheckA(corpus);
        CheckB(corpus);
        CheckC(corpus);
        CheckD(corpus);
        CheckE(corpus);
        CheckF(corpus);
        CheckG();
        CheckH(corpus);
        CheckI(corpus);

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // --- 샘플 출력 ---------------------------------------------------------

    private void PrintCompare(string title, string questionId)
    {
        GD.Print($"\n===== Q: {title} =====");
        foreach (string id in Ids)
        {
            var lines = new List<string>();
            for (int i = 0; i < 3; i++) lines.Add(Answer(id, questionId));
            GD.Print($"  {Codename(id),-4} : {lines[0]}");
            GD.Print($"       ↳ {lines[1]}");
            GD.Print($"       ↳ {lines[2]}");
        }
    }

    // 같은 질문을 다시 받았을 때.
    private void PrintRepeat()
    {
        GD.Print("\n===== 같은 질문을 다시 받았을 때 (Q: 이상현상은 없었습니까?) =====");
        foreach (string id in Ids)
        {
            Answer(id, DialogueQuestions.Anomaly);
            GD.Print($"  {Codename(id),-4} : {Answer(id, DialogueQuestions.Anomaly, repeat: true)}");
        }
    }

    // --- 표본 --------------------------------------------------------------

    private Dictionary<string, List<string>> Collect()
    {
        var corpus = new Dictionary<string, List<string>>();
        string[] questions =
        {
            DialogueQuestions.Anomaly, DialogueQuestions.Where, DialogueQuestions.Suspicious,
            DialogueQuestions.Opinion, DialogueQuestions.Accuse, DialogueQuestions.GeneralStatus,
        };
        foreach (string id in Ids)
        {
            var list = new List<string>();
            for (int i = 0; i < Samples; i++)
                list.Add(Answer(id, questions[i % questions.Length]));
            corpus[id] = list;
        }
        return corpus;
    }

    private string Answer(string id, string questionId, bool repeat = false)
    {
        var ctx = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview, questionId, "", null);
        ctx.IsRepeat = repeat;
        ctx.TargetEmployeeId = id == "cat" ? "dog" : "cat";
        var plan = DialogueResponsePlanner.Plan(ctx);
        return KoreanDialogueComposer.Compose(ctx, plan);
    }

    // --- Test A~I ----------------------------------------------------------

    private void CheckA(Dictionary<string, List<string>> corpus)
    {
        int yes = corpus["rabbit"].Count(t => t.StartsWith("네!") || t.StartsWith("네,") || t.StartsWith("네."));
        GD.Print($"\n[A] 토끼 {Samples}개 중 '네'로 시작 {yes}개");
        Check(yes <= Samples / 5, "A 토끼가 대부분 '네!'로 시작하지 않는다");
    }

    private void CheckB(Dictionary<string, List<string>> corpus)
    {
        int that = corpus["cat"].Count(t => t.StartsWith("그거요"));
        GD.Print($"[B] 고양이 {Samples}개 중 '그거요?' 시작 {that}개");
        Check(that <= 1, "B 고양이가 '그거요?'만 반복하지 않는다");
    }

    private void CheckC(Dictionary<string, List<string>> corpus)
    {
        float wolf = Avg(corpus["wolf"]), rabbit = Avg(corpus["rabbit"]), fox = Avg(corpus["fox"]);
        float dog = Avg(corpus["dog"]), cat = Avg(corpus["cat"]), sheep = Avg(corpus["sheep"]);
        GD.Print($"[C] 평균 길이 — 늑대 {wolf:0.0} / 고양이 {cat:0.0} / 양 {sheep:0.0} / " +
                 $"강아지 {dog:0.0} / 여우 {fox:0.0} / 토끼 {rabbit:0.0}");
        Check(wolf < rabbit && wolf < fox, "C 늑대가 토끼·여우보다 짧게 말한다");
    }

    private void CheckD(Dictionary<string, List<string>> corpus)
    {
        int sheep = corpus["sheep"].Count(Hedged);
        int wolf = corpus["wolf"].Count(Hedged);
        GD.Print($"[D] 확신 낮추는 표현 — 양 {sheep}개 / 늑대 {wolf}개");
        Check(sheep > wolf, "D 양이 늑대보다 자주 확신을 낮춘다");
    }

    private void CheckE(Dictionary<string, List<string>> corpus)
    {
        int fox = corpus["fox"].Count(t => t.Contains("?"));
        GD.Print($"[E] 여우 {Samples}개 중 되묻는 답변 {fox}개");
        // 여우의 핵심 문장 자체에 되묻는 말이 섞인 경우도 '?' 로 잡히므로 상한은 넉넉히 둔다.
        // 여기서 막고 싶은 것은 "모든 답변이 되묻기" 한 가지다.
        Check(fox >= 1 && fox < Samples, "E 여우는 가끔 되묻지만 매번은 아니다");
    }

    // 늑대는 직접 본 것은 단정하지만 확인하지 않은 것은 단정하지 않는다 — 벽 너머로 들은
    // 사건을 물었을 때 "봤습니다" 라고 말하면 안 되고, 직접 보지 못했다는 사실이 남아야 한다.
    private void CheckF(Dictionary<string, List<string>> corpus)
    {
        int scoped = 0, claimed = 0;
        for (int i = 0; i < 12; i++)
        {
            string a = Answer("wolf", DialogueQuestions.Anomaly);
            GD.Print($"     늑대: {a}");
            if (a.Contains("직접") || a.Contains("확인") || a.Contains("범위") || a.Contains("소리")
                || a.Contains("보고") || a.Contains("방향") || a.Contains("진동")) scoped++;
            if (a.Contains("봤습니다") && !a.Contains("직접 보")) claimed++;
        }
        GD.Print($"[F] 늑대 12개 중 감각/범위를 밝힌 답변 {scoped}개 · 단정한 답변 {claimed}개");
        Check(claimed == 0, "F 늑대가 확인하지 않은 것을 단정하지 않는다");
        Check(scoped >= 6, "F 늑대가 들은 것과 본 것을 구분한다");
    }

    // 강아지의 착함은 복종형 "네!" 로 표현하지 않는다.
    private void CheckI(Dictionary<string, List<string>> corpus)
    {
        int yes = corpus["dog"].Count(t => t.StartsWith("네") || t.StartsWith("예"));
        GD.Print($"[I] 강아지 {Samples}개 중 '네'로 시작 {yes}개");
        Check(yes <= Samples / 5, "I 강아지가 '네'로 시작하는 복종형 답변만 하지 않는다");
    }

    private void CheckG()
    {
        // 같은 질문을 연달아 5번 — 같은 문장/같은 시작이 반복되면 안 된다.
        // 위치 질문은 답이 곧 방 이름이라 시작이 같을 수밖에 없다 —
        // 표현이 갈릴 여지가 있는 질문으로 본다.
        foreach (string id in Ids)
        {
            var five = new List<string>();
            for (int i = 0; i < 5; i++) five.Add(Answer(id, DialogueQuestions.Anomaly));
            int distinct = five.Distinct().Count();
            int maxDup = five.GroupBy(t => t).Max(g => g.Count());
            GD.Print($"[G] {Codename(id),-4} 5연속 — 서로 다른 문장 {distinct}개, 같은 문장 최대 {maxDup}회");
            // 늑대처럼 문장 풀이 3개뿐인 캐릭터는 5연속에서 한 문장이 세 번 나올 수 있다.
            // 잡고 싶은 것은 "같은 문장만 반복"이므로 서로 다른 문장 수를 기준으로 본다.
            Check(distinct >= 3 && maxDup <= 3, $"G {Codename(id)} 가 같은 문장을 되풀이하지 않는다");
        }
    }

    private static readonly Regex DoubleYes = new(@"(네|예)[.!,]\s*(네|예)", RegexOptions.Compiled);

    private void CheckH(Dictionary<string, List<string>> corpus)
    {
        var bad = corpus.SelectMany(kv => kv.Value).Where(t => DoubleYes.IsMatch(t)).ToList();
        // 느낌표 상한은 캐릭터마다 다르다(토끼는 밝고 솔직해서 여럿) — 말투 설정의 MaxExclamations 를 넘지 않으면 된다.
        int tooMany = corpus.Sum(kv => kv.Value.Count(t => t.Count(c => c == '!') > DialogueVoices.Get(kv.Key).MaxExclamations));
        GD.Print($"[H] '네! 네!' {bad.Count}건 · 느낌표 상한 초과 {tooMany}건");
        foreach (string b in bad.Take(3)) GD.Print("     " + b);
        Check(bad.Count == 0, "H 한 답변에 같은 긍정이 두 번 나오지 않는다");
        Check(tooMany == 0, "H 한 답변의 느낌표가 그 캐릭터의 상한을 넘지 않는다");
    }

    // --- 도우미 ------------------------------------------------------------

    private static bool Hedged(string t) =>
        t.Contains("확실") || t.Contains("같아요") || t.Contains("잘 모르") || t.Contains("수도 있")
        || t.Contains("아마") || t.Contains("싶은데");

    private static float Avg(List<string> list) => list.Count == 0 ? 0f : (float)list.Average(t => t.Length);

    private static string Codename(string id) =>
        FacilitySimulation.Instance?.GetEmployeeDef(id)?.Codename ?? id;

    private void Check(string label, bool ok) => Check(ok, label);

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    // 여섯 명이 같은 사실을 갖도록 만든다 — 차이가 오직 말투에서만 나오게.
    private static void Setup()
    {
        GameState.Instance.ResetRun(1);
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        FacilitySimulation.Instance.RollDailyMoods();

        var rooms = new Dictionary<string, string>
        {
            // 늑대는 경비실 — 발전실 사고를 소리로만 알게 되는 자리(F 검사가 이 자리를 본다).
            ["wolf"] = Guard, ["cat"] = Storage, ["sheep"] = Maintenance,
            ["rabbit"] = Maintenance, ["dog"] = Core, ["fox"] = Core,
        };
        var sim = FacilitySimulation.Instance;
        foreach (var kv in rooms)
        {
            var st = sim.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value;
            st.CurrentRoomId = kv.Value;
            st.Alive = true;
            st.Isolated = false;
        }
        foreach (var kv in rooms) Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
        // 아무도 보지 못한 곳에서 사고가 하나 났다 — 전원이 같은 사실을 갖는다.
        Log(LogEventType.TaskFailed, "", Power, 40f);
    }

    private static void Log(LogEventType type, string actor, string room, float at)
    {
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = room,
            Description = $"(테스트 {type} {room} {at:0.0})",
            WitnessEmployeeIds = new List<string>(),
        });
    }
}
