using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// B '존재' 연출 검증.
//
//   godot --path . res://scenes/debug/PresenceTest.tscn --quit-after 40000
//
// 이 연출의 규칙은 "보이되 남지 않는다" 이다. 그래서 보는 것도 두 가지다.
//   ① 하루에 2~3회, 40초 간격, 근무 중에만 · 교육일에는 나오지 않는다
//   ② 로그 · 증거 카드 · 인터뷰 답변 어디에도 흔적이 없다
public partial class PresenceTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && PresenceDirector.Instance == null; i++) await Frame();
        await Seconds(0.8);

        var d = PresenceDirector.Instance;
        if (d == null) { GD.PrintErr("PresenceDirector 없음"); GetTree().Quit(); return; }

        GD.Print("\n\n################ B '존재' 연출 검증 ################");
        TestBudget(d);
        TestGating(d);
        await TestLeavesNoTrace(d);

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 하루 상한과 간격 ────────────────────────────────────────────────
    private void TestBudget(PresenceDirector d)
    {
        Head("A", "하루 2~3회 · 서로 40초 이상");
        var gs = GameState.Instance;
        gs.ResetRun(2);
        gs.SetPhase(GamePhase.Live);

        int lo = 99, hi = 0;
        for (int run = 0; run < 40; run++)
        {
            gs.ResetRun(2);
            gs.SetPhase(GamePhase.Live);
            d.ResetForTest(2, 0);          // 목표는 아래에서 다시 뽑게 한다
            // 하루를 통째로 흘려 본다.
            var fires = SimulateDay(d);
            lo = Mathf.Min(lo, fires.Count);
            hi = Mathf.Max(hi, fires.Count);

            for (int i = 1; i < fires.Count; i++)
                if (fires[i] - fires[i - 1] < 40f - 0.1f)
                {
                    Check(false, $"간격이 40초 미만이다 ({fires[i] - fires[i - 1]:0.0}초)");
                    return;
                }
        }
        GD.Print($"   40일치 시뮬레이션: 하루 {lo}~{hi}회");
        Check(lo >= 2 && hi <= 3, $"하루 2~3회 안에 들어온다 ({lo}~{hi})");
        Check(true, "모든 연출이 40초 이상 떨어져 있다");
    }

    // 하루를 흘리며 연출이 난 시각을 모은다(화면 없이 판정만).
    private System.Collections.Generic.List<float> SimulateDay(PresenceDirector d)
    {
        var gs = GameState.Instance;
        var at = new System.Collections.Generic.List<float>();
        void OnFire(PresenceDirector.Kind k) => at.Add(gs.DayTimeSeconds);
        d.Fired += OnFire;

        d.ResetForTestRandomTarget(2);
        float len = Config.Instance.Data.DayLengthSeconds;
        const float step = 0.25f;
        for (float t = 0; t < len; t += step)
        {
            gs.AdvanceDayTime(step);
            d.TickForTest(step);
        }
        d.Fired -= OnFire;
        return at;
    }

    // ── 언제 나오지 않는가 ──────────────────────────────────────────────
    private void TestGating(PresenceDirector d)
    {
        Head("B", "근무 중에만 · 교육일(DAY0)에는 나오지 않는다");
        var gs = GameState.Instance;

        gs.ResetRun(0);
        gs.SetPhase(GamePhase.Live);
        Check(!PresenceDirector.Allowed(), "교육일에는 나오지 않는다");

        gs.ResetRun(2);
        gs.SetPhase(GamePhase.Schedule);
        Check(!PresenceDirector.Allowed(), "근무 배치 중에는 나오지 않는다");
        gs.SetPhase(GamePhase.Rest);
        Check(!PresenceDirector.Allowed(), "휴게시간에는 나오지 않는다");
        gs.SetPhase(GamePhase.Live);
        Check(PresenceDirector.Allowed(), "근무 중에는 나온다");
    }

    // ── 흔적이 남지 않는다 ──────────────────────────────────────────────
    private async System.Threading.Tasks.Task TestLeavesNoTrace(PresenceDirector d)
    {
        Head("C", "로그 · 증거 카드 · 인터뷰 어디에도 남지 않는다");
        var gs = GameState.Instance;
        gs.ResetRun(2);
        gs.SetPhase(GamePhase.Live);
        var sim = FacilitySimulation.Instance;
        sim.ResetRun();
        sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        PlayerKnownEvidence.ResetAll();
        CallMemoryLog.ResetAll();
        ShiftMemory.Invalidate();

        // 직원을 움직이지 않는다 — 걷기만 해도 입퇴실 기록이 쌓여서 "연출이 남긴 줄" 과 섞인다.
        foreach (string id in sim.GetActiveEmployeeIds()) sim.ClearAssignment(id);
        await Seconds(0.6);
        EventLog.Instance.ClearAll();

        int logBefore = EventLog.Instance.GetAllEntries().Count;
        int cardsBefore = InterviewEvidenceBoard.Build("cat").Count;

        // 네 가지를 전부 한 번씩 실제로 띄운다.
        foreach (PresenceDirector.Kind k in System.Enum.GetValues<PresenceDirector.Kind>())
        {
            d.Fire(k);
            await Seconds(0.7);
        }
        GD.Print($"   네 연출을 모두 띄웠다: {string.Join(" · ", d.FiredKinds)}");

        var added = EventLog.Instance.GetAllEntries().Skip(logBefore).ToList();
        foreach (var e in added) GD.Print($"   [로그에 새로 생긴 줄] {e.EventType} {e.RoomId} {e.Description}");
        int logAfter = EventLog.Instance.GetAllEntries().Count;
        Check(logAfter == logBefore, $"EventLog 에 한 줄도 남지 않는다 ({logBefore} → {logAfter})");
        Check(PlayerKnownEvidence.SightingsBy("cat").Count() == 0, "목격 증언으로 남지 않는다");
        Check(CallMemoryLog.All.Count == 0, "통화 기록으로 남지 않는다");

        int cardsAfter = InterviewEvidenceBoard.Build("cat").Count;
        Check(cardsAfter == cardsBefore, $"조사 자료 카드가 늘지 않는다 ({cardsBefore} → {cardsAfter})");

        // 근무 기억 · 인터뷰 답변에 등장하지 않는다.
        ShiftMemory.Invalidate();
        bool inMemory = ShiftMemory.Of("cat", 2).Any(m =>
            (m.TaskName ?? "").Contains("존재") || (m.RoomId ?? "").Contains("존재"));
        Check(!inMemory, "근무 기억에 남지 않는다");

        string answer = LocalDialogueGenerator.InterviewAnswer("cat", DialogueQuestions.Anomaly);
        GD.Print($"   '이상한 점' 답변: {answer}");
        string[] words = { "실루엣", "형체", "그림자", "누군가", "존재" };
        Check(!words.Any(answer.Contains), "인터뷰 답변에 등장하지 않는다");
    }

    private void Head(string id, string t) => GD.Print($"\n===== [{id}] {t} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
