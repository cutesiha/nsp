using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 개발 전용 진입 정보. DeveloperHub 를 통해 들어왔을 때만 값이 찬다.
//
// 게임 코드는 이 클래스를 전혀 모른다 — 여기서 ShiftFlowController 의 기존 진행 메서드를
// 순서대로 불러 주기만 한다. Pending 이 비어 있으면(=평소 실행) 게임 흐름은 100% 그대로다.
//
// ★ 여기서 부르는 것은 전부 '실제 게임이 평소에 부르는 그 메서드'다.
//   가짜 화면을 만들지 않으며, 밸런스 데이터(Config/OpsProfile/Objective)는 건드리지 않는다.
public static class DebugEntryPoint
{
    public enum Phase
    {
        Schedule,       // 근무 배치
        Shift,          // 실시간 근무
        Report,         // 근무 종료 보고서
        Rest,           // 휴게 / 심문
        Verdict,        // DAY5 최종 격리 보고서
        Ending,         // 엔딩 연출(EndingDirector)
        Title,          // 타이틀(엔딩 이후 상태 포함)
    }

    public sealed class Request
    {
        public int Day = 1;
        public Phase Phase = Phase.Schedule;
        /// <summary>0 미만이면 코어 복구율을 건드리지 않는다.</summary>
        public float Core = -1f;
        /// <summary>직원 스트레스를 정상/주의/위험으로 섞어 넣는다(UI 확인용).</summary>
        public bool SeedStress = true;
        /// <summary>Phase.Title 에서 '마지막으로 본 엔딩'을 무엇으로 볼지.</summary>
        public EndingState.Kind TitleAfter = EndingState.Kind.None;
        /// <summary>엔딩을 바로 재생할 때 최종 보고서의 지목을 강제한다.
        /// null = 건드리지 않음 · "" = 지목 없음 · "!" = 실제 결번을 지목(정답).</summary>
        public string Accused;
        /// <summary>지목이 정답일 때 근거까지 맞았던 것으로 둘지(GUIDE-0 한 줄 · 등급에만 반영).</summary>
        public bool Proven;
        public string Label = "";
    }

    // 지금 Debug Hub 를 통해 들어와 있는가. [DEV HUB] 복귀 버튼을 띄우는 조건이기도 하다.
    public static Request Pending { get; set; }
    public static bool Active => Pending != null;

    // --- user:// 실제 진행 기록 보호 ---------------------------------------
    // 엔딩/타이틀 테스트는 EndingState.Record() 로 파일을 건드린다.
    // 들어갈 때 원래 값을 적어 두고, Hub 로 돌아올 때 그대로 되돌린다.
    private static EndingState.Kind? _endingBackup;

    public static void BackupEndingState()
    {
        _endingBackup ??= EndingState.Last;
    }

    public static void RestoreEndingState()
    {
        if (_endingBackup is not { } kind) return;
        _endingBackup = null;
        EndingState.Record(kind);
        EndingState.PendingWake = EndingState.Kind.None;
    }

    // --- ShiftFlowController 로 가는 다리 -----------------------------------
    // 진행 메서드는 전부 private 이다. 게임 코드에 개발용 훅을 새로 심지 않으려고
    // 리플렉션으로 부른다(기존 EndingShot.cs 가 쓰던 방식 그대로다).

    public static Node FindFlow(SceneTree tree) =>
        tree?.Root?.FindChild("ShiftFlowController", true, false);

    public static void Call(Node flow, string method) =>
        flow?.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(flow, null);

    public static void CallStatic(string method, params object[] args) =>
        typeof(ShiftFlowController).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, args);

    public static string Stage(Node flow) =>
        flow?.GetType().GetField("_stage", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(flow)?.ToString() ?? "";

    public static void SetStage(Node flow, string stage)
    {
        var f = flow?.GetType().GetField("_stage", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(flow, System.Enum.Parse(f.FieldType, stage));
    }

    // 타이틀 연출(중앙제어실 부팅)과 프롤로그를 건너뛰게 한다. 게임 화면에는 이 값을 켜는
    // 버튼이 없다 — 개발 허브와 검사 씬만 쓴다(ShiftFlowController._skipToDay1Pending).
    // 필드를 찾지 못하면 허브가 타이틀 위에서 진행을 억지로 밀어붙이게 되므로 크게 알린다.
    public static void SkipTitleOnNextBoot()
    {
        var f = typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) GD.PushError("DebugEntryPoint: ShiftFlowController._skipToDay1Pending 이 없다 — 타이틀을 건너뛸 수 없다.");
        f?.SetValue(null, true);
        // 그 첫 진입은 무조건 DAY1 로 열린다. 허브는 그 뒤에 원하는 날짜로 다시 들어가므로
        // 첫 진입의 '그 날의 안내'(DAY0 교육 시작 · DAY1 패드 안내 · 해금 안내)는 건너뛴다.
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
    }

    // 허브가 넣은 스트레스(표시 확인용) 때문에 GUIDE-0 의 "스트레스 주의" 첫 안내가 뜨지 않게 한다.
    // 그 안내는 한 판에 한 번 뜨는 실제 게임 연출이다 — 이미 본 것으로 표시만 한다.
    public static void SuppressStressHint(Node flow) =>
        flow?.GetType().GetField("_stressHintShown", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(flow, true);

    // 씬을 내려도 남는 정적 연출 상태를 걷는다 — GUIDE-0 얼굴창 · 입 모양 · 교육용 대사 후크 ·
    // 목소리. 허브에서 DAY0 교육을 보다가 F10 으로 나와 다른 단계로 들어가면 이것들이
    // 그대로 남아 엉뚱한 화면에 GUIDE-0 이 떠 있었다.
    public static void ResetPresentation()
    {
        NSP.Prologue.GuideCornerFace.ShowAll(false);
        NSP.Prologue.GuideCornerFace.SetLifted(false);
        NSP.Prologue.GuideMouthAnimator.Reset();
        EmployeeMouthAnimator.Reset();
        NSP.Dialogue.LocalDialogueGenerator.ScriptedAnswerOverride = null;
        Sfx.Instance?.StopVoiceBlip();
        GameState.Instance?.SetPhase(NSP.Data.GamePhase.Prep);
    }

    // --- 개발용 초기 상태 ---------------------------------------------------
    // 화면을 확인하기 좋은 값을 넣을 뿐이다. 실제 저장이나 밸런스 데이터에는 쓰지 않는다.
    public static void SeedState(Request req)
    {
        var gs = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        if (gs == null) return;

        if (req.Core >= 0f) gs.AddCoreProgress(req.Core - gs.CoreProgress, "디버그 진입");

        // 보고서 · 엔딩을 바로 재생할 때는 근무를 거치지 않아 결번이 비어 있다 — 여기서 뽑는다.
        if (req.Phase is Phase.Verdict or Phase.Ending && string.IsNullOrEmpty(gs.SaboteurEmployeeId))
            gs.AssignRandomSaboteur(sim?.GetActiveEmployeeIds()
                                    ?? new System.Collections.Generic.List<string>());

        if (req.Accused != null)
            gs.ForceFinalVerdict(req.Accused == "!" ? gs.SaboteurEmployeeId : req.Accused, req.Proven);

        // DAY1 은 한 판의 첫날이다 — 스트레스는 반드시 0 에서 시작한다.
        // (허브가 표시 확인용으로 섞어 넣으면 "첫날인데 주의 · 위험이 섞여 있다"가 된다.)
        if (req.Day <= 1) return;
        // 스트레스가 잠긴 날에는 넣어도 화면에 안 뜬다 — 해금된 날에만.
        if (!req.SeedStress || sim == null || !DayFeatures.StressEnabled) return;
        var cfg = Config.Instance?.Data;
        if (cfg == null) return;
        // 정상 / 주의 / 위험을 한 명씩 만들어 둔다(상태 표시 UI 확인용).
        float[] band = { 0f, cfg.StressCautionFrom + 1f, cfg.StressDangerFrom + 1f };
        int i = 0;
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null) continue;
            st.Stress = band[i % band.Length];
            i++;
        }
    }
}
