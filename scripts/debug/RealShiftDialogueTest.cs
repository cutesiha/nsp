using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 실제 게임 경로로 대화 규칙을 본다.
//
//   godot --headless --path . res://scenes/debug/RealShiftDialogueTest.tscn
//
// DialogueSampleDump 는 로그를 손으로 만들어 넣는다. 여기서는 손대지 않는다 —
//   메인 씬 → 배치 콘솔 → 근무 시작(ControlRoom3DController.BeginShift) → 근무(시뮬레이션 그대로)
//   → 근무 정산 → 휴게 → PhoneCallHud.Open → InterviewSession
// 을 그대로 지난 뒤 여섯 명에게 기본 질문 3개(최초 진술) + 위치 질문 2개를 던진다.
//
// 핵심은 ShiftStartRoomId — BeginShift 가 배치표 로그를 지운 뒤에도 근무 시작 위치가 남는가.
// 배치: 늑대·강아지=경비실 · 토끼·고양이=정비실 · 여우=코어실(혼자) · 양=배치 안 함.
// 근무 40% 지점에 여우를 저장고로 재배치한다.
public partial class RealShiftDialogueTest : Node
{
    private const string Guard = "guard_room", Maint = "maintenance_room", Core = "core_room", Storage = "storage_room";
    private const string Idle = "sheep", Moved = "fox";
    // 근무 120초를 빨리 돌린다. 시뮬레이션은 _Process 의 delta 로만 움직이므로 결과는 같은 규칙을 탄다.
    private const float TimeScale = 4f;

    private static readonly (string Id, string Room)[] Plan =
    {
        ("wolf", Guard), ("dog", Guard), ("rabbit", Maint), ("cat", Maint), (Moved, Core),
    };
    private static readonly string[] Ids = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private sealed class Said
    {
        public string Speaker = "", Question = "", Answer = "", Trace = "", Anchor = "?";
    }

    private readonly List<Said> _said = new();
    private int _pass, _fail;
    private FacilitySimulation _sim;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("################ 실게임 경로 — 근무 → 휴게 심문 ################");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        var scene = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(scene);

        for (int i = 0; i < 240 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        _sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (!Check(GameState.Instance?.CurrentPhase == GamePhase.Schedule && map != null && _sim != null && ctl != null,
                "배치 단계로 들어간다")) { Done(); return; }
        await Frames(10);
        int day = GameState.Instance.CurrentDay;

        // ── 배치 콘솔에서 배치하고 "근무 시작"을 누른다 ─────────────────────────
        foreach (var p in Plan) Check(_sim.AssignToRoom(p.Id, p.Room), $"{Nm(p.Id)} → {RoomName(p.Room)} 배치");
        await Frames(3);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 400 && GameState.Instance.CurrentPhase != GamePhase.Live; i++) await Frame();
        if (!Check(GameState.Instance.CurrentPhase == GamePhase.Live, "근무 시작 → 근무(Live)")) { Done(); return; }

        // ── 핵심: BeginShift 가 근무 시작 배치를 적어 두었는가 ─────────────────────
        GD.Print("\n===== 근무 시작 배치 =====");
        foreach (string id in Ids)
        {
            var st = _sim.GetEmployeeState(id);
            GD.Print($"   {Nm(id)}: ShiftStartDay={st.ShiftStartDay} ShiftStartRoomId='{st.ShiftStartRoomId}'");
        }
        Check(Plan.All(p => _sim.GetEmployeeState(p.Id).ShiftStartDay == day
                            && _sim.GetEmployeeState(p.Id).ShiftStartRoomId == p.Room),
            "BeginShift 가 배치된 다섯 명의 ShiftStartRoomId 를 채운다");
        Check(_sim.GetEmployeeState(Idle).ShiftStartRoomId == "", "배치되지 않은 양의 ShiftStartRoomId 는 빈 값");
        Check(!EventLog.Instance.GetAllEntries().Any(e => e.EventType == LogEventType.Relocation
                                                         && !string.IsNullOrEmpty(e.ActorEmployeeId)),
            "배치표 Relocation 로그는 근무 시작 때 지워졌다(그래서 기록값이 필요하다)");
        Check(DialogueContextBuilder.RoomAt(Moved, day, 0.1f) == Core, "근무 시작 직후 여우의 시간표 위치 = 코어실");
        Check(DialogueContextBuilder.RoomAt(Idle, day, 0.1f) == "", "양은 시간표에 없다");

        // ── 근무: 시뮬레이션을 그대로 굴린다. 40% 지점에 여우를 저장고로 재배치 ───────
        Engine.TimeScale = TimeScale;
        float max = DayObjectives.MaxShiftSeconds;
        float relocatedAt = -1f;
        for (int i = 0; i < 20000 && GameState.Instance.CurrentPhase == GamePhase.Live; i++)
        {
            if (relocatedAt < 0f && GameState.Instance.DayTimeSeconds >= max * 0.4f)
            {
                relocatedAt = GameState.Instance.DayTimeSeconds;
                Check(_sim.AssignToRoom(Moved, Storage), $"근무 중 여우 → 저장고 재배치 ({DialogueClock.Text(relocatedAt)})");
            }
            await Frame();
        }
        Engine.TimeScale = 1f;
        if (!Check(GameState.Instance.CurrentPhase == GamePhase.Settlement, "근무 시간이 다 되면 근무 정산으로 넘어간다"))
        { Done(); return; }

        // 근무 정산 화면이 뜬 뒤 [계속] — 버튼 이벤트가 부르는 것과 같은 메서드를 부른다.
        await Frames(120);
        var flow = FindOfType<ShiftFlowController>(scene);
        typeof(ShiftFlowController).GetMethod("RequestRestFromReport", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(flow, null);
        for (int i = 0; i < 300 && GameState.Instance.CurrentPhase != GamePhase.Rest; i++) await Frame();
        if (!Check(GameState.Instance.CurrentPhase == GamePhase.Rest, "근무 정산 → 휴게시간")) { Done(); return; }
        await Frames(30);

        var incidents = EventLog.Instance.GetAllEntries().Count(e => e.Day == day && DialogueContextBuilder.IsIncident(e.EventType));
        GD.Print($"\n   오늘 로그 {EventLog.Instance.GetAllEntries().Count}줄 · 사고 {incidents}건 · 결번자 {GameState.Instance.SaboteurEmployeeId}");
        if (relocatedAt > 0f)
            Check(DialogueContextBuilder.RoomAt(Moved, day, relocatedAt - 1f) == Core,
                "재배치 직전 여우의 시간표 위치는 여전히 코어실(지금 배치인 저장고가 아니다)");

        // ── 휴게시간 심문: PhoneCallHud 가 여는 InterviewSession 을 그대로 쓴다 ────────
        var hud = PhoneCallHud.Instance;
        var sessionField = typeof(PhoneCallHud).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (string id in Ids)
        {
            hud.Open(id, LocalInterviewDialogue.EventDay1Interview);
            await Frames(2);
            var session = sessionField?.GetValue(hud) as InterviewSession;
            if (!Check(session != null && session.EmployeeId == id, $"{Nm(id)} — PhoneCallHud 가 심문 세션을 연다"))
                continue;
            GD.Print($"\n--- {Nm(id)} ---");
            foreach (var o in session.Openings)
                Record(id, $"(최초 진술 · {o.QuestionId})", o.Text, o.Trace, "?");
            foreach (var q in LocationQuestions(session))
            {
                string a = session.Ask(q).Answer;
                Record(id, q.Text, a, DialogueComposer.LastTrace,
                    q.HasAnchorTime ? Bucket(q.AnchorTime).ToString() : "?");
            }
            hud.RequestClose();
            await Frames(2);
        }

        CheckRules();
        Done();
    }

    // 위치 질문 두 개 — 사고 자료가 있으면 "그때 어디" · "그 직전", 없으면 자기 이동 자료의 질문.
    private static List<InterviewQuestion> LocationQuestions(InterviewSession s)
    {
        var incident = s.Board.FirstOrDefault(e => e.Kind == EvidenceKind.Incident && s.CanUse(e));
        if (incident != null)
            return new[] { InterviewIntent.AskWhereAtIncident, InterviewIntent.AskBeforeIncident }
                .Select(i => InterviewQuestionFactory.Make(s.EmployeeId, incident, i))
                .Where(q => !string.IsNullOrEmpty(q.Text)).ToList();
        var own = s.Board.FirstOrDefault(e => s.CanUse(e) && InterviewQuestionFactory.For(s.EmployeeId, e).Count > 0);
        return own == null ? new List<InterviewQuestion>() : InterviewQuestionFactory.For(s.EmployeeId, own).Take(2).ToList();
    }

    // ── (a)(b)(c) ─────────────────────────────────────────────────────
    private void CheckRules()
    {
        GD.Print("\n===== 실게임 경로 검사 =====");
        Check(_said.Count >= 18, $"여섯 명의 답변이 모였다 ({_said.Count}개)");

        string name = Nm(Idle);
        var named = new Regex($@"(?<![가-힣]){Regex.Escape(name)}(?= 씨| 직원|이랑|하고|도 |이 )");
        var leaks = _said.Where(x => x.Speaker != Idle && named.IsMatch(x.Answer)).ToList();
        foreach (var x in leaks.Take(5)) GD.Print($"   미배치 누설: [{Nm(x.Speaker)}] {x.Answer}");
        Check(leaks.Count == 0, $"(a) 배치되지 않은 직원({name}) 이름이 어떤 답변에도 나오지 않는다 ({leaks.Count}건)");

        var over = _said.Select(x => (x, n: CompanionMemories(x.Trace))).Where(p => p.n > 0)
            .GroupBy(p => $"{Nm(p.x.Speaker)} | {p.x.Anchor}")
            .Where(g => g.Sum(p => p.n) > 1).ToList();
        foreach (var g in over.Take(5))
            GD.Print($"   동료 반복: {g.Key} — " + string.Join(" / ", g.Select(p => p.x.Answer)));
        Check(over.Count == 0, $"(b) 세션당 동료 언급 1회 이하 ({over.Count}개 세션 초과)");
        Check(_said.Where(x => x.Question.StartsWith("(최초 진술")).All(x => CompanionMemories(x.Trace) == 0),
            "최초 진술에는 동료 기억이 붙지 않는다");

        var roomNames = _sim.GetRoomIds().Select(RoomName).Where(n => n.Length > 0).Distinct().ToList();
        var twice = _said.Where(x => roomNames.Any(n => Count(x.Answer, n) >= 2)).ToList();
        foreach (var x in twice.Take(5)) GD.Print($"   방 반복: [{Nm(x.Speaker)}] {x.Answer}");
        Check(twice.Count == 0, $"(c) 한 답 안에 같은 작업실을 두 번 말한 답 0개 ({twice.Count}개)");
    }

    private static int CompanionMemories(string trace)
    {
        var m = Regex.Match(trace ?? "", @"기억\[(.*)\]");
        if (!m.Success) return 0;
        return m.Groups[1].Value.Split(", ")
            .Count(s => (s.StartsWith("mem.with") || s.StartsWith("mem.alone")) && !s.EndsWith("(잘림)"));
    }

    private static int Count(string text, string word)
    {
        int n = 0;
        for (int i = text.IndexOf(word, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(word, i + word.Length, System.StringComparison.Ordinal)) n++;
        return n;
    }

    private static int Bucket(float t) =>
        Mathf.FloorToInt(t / (ShiftMemory.SpokenBucketMinutes * DialogueClock.SecondsPerMinute));

    private void Record(string id, string question, string answer, string trace, string anchor)
    {
        _said.Add(new Said { Speaker = id, Question = question, Answer = answer ?? "", Trace = trace ?? "", Anchor = anchor });
        GD.Print($"  Q: {question}\n     → {answer}\n       [{trace}]");
    }

    private static T FindOfType<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (FindOfType<T>(c) is { } found) return found;
        return null;
    }

    private string Nm(string id) => _sim?.GetEmployeeDef(id)?.Codename ?? id;
    private string RoomName(string id) => _sim?.GetRoomDef(id)?.DisplayName ?? id;

    private void Done()
    {
        Engine.TimeScale = 1f;
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }
}
