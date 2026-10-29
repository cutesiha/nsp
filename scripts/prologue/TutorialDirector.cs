using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Prologue;

// DAY 0 — GUIDE-0 가 진행하는 관리자 교육.
//
// 설명창을 읽히는 방식이 아니라 "말한다 → 플레이어가 직접 한다 → 확인하고 다음으로" 구조다.
// 그래서 각 단계는 실제 게임 상태(배치·사고·수리·로그 창·통화)를 폴링해서 넘어간다.
// 새 게임 규칙을 만들지 않는다 — 전부 기존 FacilitySimulation / Phone3D / 로그 창을 쓴다.
//
// GUIDE-0 의 문구는 docs/NSP_PROLOGUE_RUNTIME.md 의 @guide tut_* 블록에 있다.
//
// ── 대사를 넘기는 두 가지 방식 ─────────────────────────────────────────
//
//   Say    : 설명. 마지막 줄까지 클릭으로 넘긴다.
//   SayAct : 지시. 마지막 줄이 **다 찍히는 순간** 묶음이 끝나고, 그 줄은 지시문으로 남은 채
//            플레이어가 그 행동을 할 때까지 기다린다. 클릭을 기다리지 않는다.
//
// 지시 줄을 클릭으로 넘기게 두면 안 된다 — 대사가 클릭을 기다리는 동안에는 화면 어디를 눌러도
// "대사 넘기기" 로 먹힌다(PrologueAdvanceInput). 직원을 끌려고 누른 첫 클릭이 사라져서
// "한 번 더 눌러야 넘어간다" 가 되던 자리다.
//
// ── 대사 줄에 걸린 연출(cue) ──────────────────────────────────────────
//
// 대본의 `cue: 이름` 이 바로 다음 줄에 연출을 건다(GuideHologramView.CueStarted/CueEnded).
// 확대 · 레버 시연 · 화살표가 **어느 줄에서** 켜지는지를 대본이 정하고, 무엇을 하는지는 여기
// OnCueStarted / OnCueEnded 가 정한다. 대본의 문구를 고쳐도 연출 자리는 cue 줄과 함께 움직인다.
public partial class TutorialDirector : Node
{
    public static TutorialDirector Instance { get; private set; }

    [Export] public NodePath ShiftFlowPath = "../ShiftFlowController";

    // 교육에 쓰는 고정 배역/장소. 데이터(UnlockDay)와 짝을 이룬다.
    [Export] public string TutorialEmployeeId = "rabbit";
    // 교육에서 토끼를 놓아 보게 할 작업실. DAY0 에 열려 있는 방이어야 한다.
    // 코어실이다 — 이 게임의 목표가 봉쇄 코어 복구라는 것을 첫 조작으로 보여 주고,
    // 그 자리에서 자재를 쓰기 시작해야 다음 단계(자재 부족)가 저절로 온다.
    [Export] public string AssignRoomId = "core_room";
    // 상황이 생긴 뒤에 열어 보여 줄 작업실. DAY0 에는 잠겨 있다가 교육이 직접 연다
    // (FacilitySimulation.OpenRoomMidShift) — 설명보다 상황이 먼저 와야 한다.
    [Export] public string MaterialsRoomId = "maintenance_room";
    [Export] public string StorageRoomId = "storage_room";
    // 교육 시뮬레이션의 자재 보관 한도. 실제 근무(30)보다 작게 잡아야
    // 코어가 자재를 다 쓰고 멈추는 상황과, 보관함이 차는 상황이 근무 안에서 일어난다.
    [Export] public int TutorialMaterialsCap = 6;
    // 상황이 저절로 오지 않을 때 기다려 주는 시간. 넘기면 교육이 직접 그 상황을 만든다.
    [Export] public float SituationWaitSeconds = 5f;
    // 근무 시작 안내가 다 찍힌 뒤 첫 전화(자재 부족)가 울리기까지 기다리는 최대 시간.
    // 짧게 둔다 — "직원들이 복구 작업을 하고 있습니다" 다음에 한참 아무 일도 없으면 늘어진다.
    // 안내("직원들이 시설 복구 작업을 하고 있습니다")가 다 찍힌 뒤 정확히 이만큼 지나 벨이 울린다.
    // 그때까지 자재가 저절로 바닥나지 않았으면 교육이 바닥낸다(2026-10-27 — 1.5초로 고정).
    [Export] public float FirstCallDelaySeconds = 1.5f;
    // 정비실에 사람을 넣은 뒤 자재가 실제로 들어오기까지 기다려 주는 최대 시간.
    [Export] public float MaterialsArriveWaitSeconds = 3f;
    // 교육용 사고가 나는 작업실. 배치한 방과 달라야 토끼가 실제로 옮겨 간 기록이 남고,
    // 그 기록이 휴게시간 모순 추리 재료가 된다.
    // 발전실은 수리에 두 명이 필요한 방이라 "한 명을 더 보내야 고쳐진다"가 자연스럽다.
    [Export] public string AccidentRoomId = "power_room";
    // 교육용 이상 개체가 나타나는 작업실. 사고가 난 방(발전실)과 달라야 한다 —
    // 같은 방이면 "고장 난 곳만 보면 된다"로 배우게 된다.
    // DAY0 에 처음부터 열려 있는 작업실은 코어실 · 경비실 · 발전실 셋이다(RoomDef.UnlockDay).
    [Export] public string AnomalyRoomId = "guard_room";
    // 저장고 안내를 넘긴 뒤 교육용 사고가 나기까지의 시간. 길면 "아무 일도 안 일어난다" 로 읽힌다
    // (8초였을 때 화면을 눌러 봐야 사고가 나는 것처럼 보였다 — 2026-10-27).
    [Export] public float IncidentDelaySeconds = 1.5f;
    // 한 명만 보내면 되는 방(정비실 · 저장고)에서 화살표로 짚을 직원. 그 직원을 옮길 수 없으면
    // 코어실 밖의 다른 직원을 짚는다(PickOneMover).
    [Export] public string MaterialsMoverId = "fox";
    [Export] public string StorageMoverId = "cat";
    // 정비실이 자재를 만들 때마다 화면 위 자재 숫자를 하늘색 테두리로 잠깐 짚는 시간.
    [Export] public float MaterialsFlashSeconds = 1.2f;
    // 개체가 나타날 방은 "혼자 근무 중인 직원의 방" 을 먼저 고른다 — 이 직원이 먼저 후보다.
    [Export] public string LoneWorkerPreferredId = "cat";

    public bool IsRunning { get; private set; }

    // 교육이 지금 "수화기를 들라" 고 하고 있는가. 그동안만 전화기가 자막 띠 위의 클릭도 받고
    // 판정 상자를 넓힌다(Phone3D) — 평소에는 띠를 누른 클릭이 전화기로 새지 않는다.
    public bool PhoneInvited => IsRunning && _phoneInvited;
    private bool _phoneInvited;

    // 교육 중에는 GUIDE-0 가 근무 종료를 지시한 뒤에만 근무를 끝낼 수 있다. 업무 창 「근무 종료」 ·
    // 모니터1 「근무 종료」 둘 다 이 값을 본다 — 교육 도중에 끝나면 남은 단계가 통째로 끊긴다.
    public static bool BlocksEndShift => Instance is { IsRunning: true, _endShiftInvited: false };
    private bool _endShiftInvited;

    private ShiftFlowController _flow;
    private GuideHologramView _guide;

    // 휴게시간 모순을 만들기 위해 사고 때 토끼가 "원래 있던" 작업실을 기억해 둔다.
    private string _rabbitOriginRoomName = "";
    // 플레이어가 토끼에게 '조사 자료로' 질문했는가(이동 기록이면 더 좋다).
    private bool _rabbitAskedWithEvidence;
    private bool _rabbitAskedAnything;

    public override void _Ready()
    {
        Instance = this;
        _flow = GetNodeOrNull<ShiftFlowController>(ShiftFlowPath);
        // 교육 화살표는 이 노드가 데리고 다닌다(교육이 없는 씬에는 뜨지 않는다).
        if (TutorialArrow.Instance == null) AddChild(new TutorialArrow { Name = "TutorialArrow" });
    }

    public override void _ExitTree()
    {
        // 교육 도중 씬이 내려가도(개발 허브 복귀 등) 정적 후크가 다음 씬에 남지 않게 한다.
        IsRunning = false;
        InterviewSession.Asked -= OnInterviewAsked;
        RoomEffectStats.MaterialsGained -= OnMaterialsGained;
        LocalDialogueGenerator.ScriptedAnswerOverride = null;
        if (Instance == this) Instance = null;
    }

    // ShiftFlowController 가 DAY0 배치 단계에 들어온 직후 한 번 부른다.
    public void BeginDay0()
    {
        if (IsRunning) return;
        IsRunning = true;
        // 교육 시뮬레이션은 작게 돌린다 — 보관 한도를 줄여 두어야 자재가 떨어지는 일도,
        // 보관함이 차는 일도 한 번의 근무 안에 실제로 일어난다. 끝나면 되돌린다(Finish).
        TutorialTelemetry.Begin();
        var gs = GameState.Instance;
        if (gs != null)
        {
            _savedMaterialsCap = gs.MaterialsCap;
            gs.AddMaterialsCap(TutorialMaterialsCap - gs.MaterialsCap);
        }
        _ = RunGuarded();
    }

    // 교육이 줄여 둔 자재 보관 한도의 원래 값(-1 = 건드리지 않았다).
    private int _savedMaterialsCap = -1;
    // 마지막 안내까지 읽고 끝났는가(중단과 구분해 기록에 남긴다).
    private bool _reachedEnd;

    // 교육을 도중에 끝낸다(개발 허브에서 DAY0 근무로 곧장 들어갈 때). 남은 안내 · 사건 ·
    // DAY1 전환은 하나도 실행하지 않고, 교육이 켜 둔 화면(자막 띠 · 얼굴창 · 강제 CCTV)만 걷는다.
    public void Abort()
    {
        if (!IsRunning) return;
        NSP.View.StoryCutinDirector.Instance?.Abort();
        _guide?.FinishGuideNow();
        FacilitySimulation.Instance?.ReleaseForcedSurveillance();
        Finish();
    }

    private async Task RunGuarded()
    {
        try { await RunAsync(); }
        catch (OperationCanceledException) { /* Abort — 여기서 조용히 끝난다 */ }
    }

    // 중단되었으면 대기 중이던 단계에서 곧바로 빠져나온다(다음 단계로 넘어가지 않는다).
    private void ThrowIfAborted()
    {
        if (!IsRunning || !IsInstanceValid(this)) throw new OperationCanceledException();
    }

    private ControlRoom3DController Ctl => ControlRoom3DController.Instance;

    private async Task RunAsync()
    {
        _guide = GuideHologramView.Instance;
        for (int i = 0; i < 120 && _guide == null; i++) { await NextFrame(); _guide = GuideHologramView.Instance; }
        if (_guide == null)
        {
            GD.PushWarning("TutorialDirector: GUIDE-0 화면을 찾지 못해 DAY0 교육을 건너뜁니다.");
            Finish();
            return;
        }

        var sim = FacilitySimulation.Instance;
        // 근무 배치 단계에서는 책상을 내려다보느라 오른쪽 CRT 가 화면에 없다 — 자막 띠를 켠다.
        GuideSubtitleHud.Instance?.SetActive(true);
        _guide.LineShown += OnGuideLine;
        _guide.CueStarted += OnCueStarted;
        _guide.CueEnded += OnCueEnded;

        // 휴게시간 교육용 진술. 실제 로그와 어긋나는 문장을 이 후크로만 돌려준다.
        LocalDialogueGenerator.ScriptedAnswerOverride = ScriptedAnswer;
        // 심문에서 무엇을 물었는지 듣는다 — 진행 조건 판정에만 쓴다.
        InterviewSession.Asked += OnInterviewAsked;
        // 정비실이 자재를 만들 때마다 화면 위 자재 숫자를 잠깐 짚는다.
        RoomEffectStats.MaterialsGained += OnMaterialsGained;

        // ── STEP 1 : 직원 확인 ────────────────────────────────────────
        TutorialTelemetry.Step("1 직원 확인");
        await Say("tut_intro");

        // ── STEP 2 : 배치 ────────────────────────────────────────────
        TutorialTelemetry.Step("2 첫 배치");
        // 토끼 카드 옆 화살표 → 토끼를 집어 들면 코어실 옆으로 옮겨 간다 → 놓으면 사라진다.
        ArmArrow(AssignArrowAim);
        await SayTyped("tut_assign", RoomVars(AssignRoomId), RabbitInAssignRoom);
        ShowArmedArrow();   // 대본에 cue: arrow 가 없었으면 여기서 띄운다
        await WaitForTutorialAssign(sim);
        HideArrow();
        Sfx.Instance?.Play("assign", -6f);
        // 배치가 맞았다. 그 방 카드의 "지금 무엇을 만들고 있는가" 줄을 한 번 짚어 준다.
        NSP.View.FacilityMonitorView.HighlightRoomNumber(AssignRoomId);
        // "남은 직원도 배치한 뒤 근무 시작" — 지시다. 클릭을 기다리지 않는다.
        // 남은 직원(고양이 · 여우) 카드 옆마다 화살표 — 그 직원을 놓는 순간 그 화살표만 사라진다.
        PointAtUnplacedRoster();
        await SayAct("tut_assign_rest", () => GameState.Instance?.CurrentPhase == GamePhase.Live);
        await Until(() => GameState.Instance?.CurrentPhase == GamePhase.Live);
        HideArrow();

        // 근무 화면 전환은 ShiftFlowController.EnterShift 가 이미 끝냈다(왼쪽=시설 / 오른쪽=CCTV).
        // "직원들이 시설 복구 작업을 하고 있습니다" — 다 찍히면 클릭 없이 곧바로 다음(전화)으로 간다.
        await SayTyped("tut_shift_start");

        // ── STEP 3 : 자재 · 정비실 ───────────────────────────────────
        TutorialTelemetry.Step("3 자재·정비실");
        await RunMaterialsLesson(sim);

        // ── STEP 4 : 보관 한도 · 저장고 ──────────────────────────────
        TutorialTelemetry.Step("4 저장고");
        await RunStorageLesson(sim);

        // ── STEP 5 : 사고 ────────────────────────────────────────────
        TutorialTelemetry.Step("5 사고·재배치");
        await RunAccidentLesson(sim);

        // ── STEP 6 : 이상 개체 (CCTV · 차폐) ──────────────────────────
        TutorialTelemetry.Step("6 이상 개체");
        // 설명만 하고 넘어가면 근무 중에 화면을 돌려 볼 이유가 생기지 않는다.
        // 그래서 한 번 실제로 나타나게 하고, 직접 찾아 직접 막게 한다.
        // (기절 · 의무실 이송 교육과 복귀 전화는 템포 때문에 뺐다 — 2026-10-22.)
        await RunBarrierLesson(sim);

        // ── STEP 7 : 휴게시간 · 기록 비교 ────────────────────────────────────────
        TutorialTelemetry.Step("7 휴게시간 심문");
        await RunRestLesson();

        // ── STEP 8 : 교육 종료 ────────────────────────────────────────
        TutorialTelemetry.Step("8 마무리");
        // 대화 기록까지 확인한 뒤 통화를 끊어야 교육이 끝난다.
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);
        GuideSubtitleHud.Instance?.SetTopAligned(false);   // 마무리 대사는 원래 자리로
        GuideCornerFace.SetLifted(false);
        // 마무리 멘트는 제어실 전체가 보이는 자리에서 한다. 모니터를 확대해 둔 채로 끝나면
        // 화면 하나만 꽉 찬 상태에서 교육이 닫혀, 이어지는 전환이 보이지 않는다.
        Ctl?.ClearFocus(0.45f);
        await Wait(0.5);
        // 여기서부터 DAY1 배치 화면이 켜질 때까지 책상 위 기기를 잠근다 — 마무리 안내가 흐르는 동안
        // 전화기가 눌려 통화가 열린 채로 DAY1 에 들어가는 일이 있었다. 배치 단계(EnterSchedule)가 푼다.
        Ctl?.SetInputLocked(true);
        await Say("tut_complete");
        await Wait(0.6);

        _reachedEnd = true;
        // 「초급 관리자」 — 마지막 안내까지 읽고 끝난 경우만이다.
        // Abort()(개발 허브의 DAY0 직행)는 이 줄을 지나가지 않는다.
        NSP.Core.AchievementManager.Instance?.NoteTutorialCompleted();
        Finish();
        _flow?.AdvanceFromTutorial();
    }

    private bool RabbitInAssignRoom() =>
        FacilitySimulation.Instance?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId == AssignRoomId;

    // 토끼가 코어실에 놓일 때까지 기다린다. 엉뚱하게 놓으면 무엇이 틀렸는지 짚어 준다.
    // 같은 잘못을 반복해도 잔소리가 쌓이지 않게, 상태가 바뀔 때만 한 번씩 말한다.
    private async Task WaitForTutorialAssign(FacilitySimulation sim)
    {
        string said = "";
        while (IsRunning)
        {
            string rabbitRoom = sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId ?? "";
            if (rabbitRoom == AssignRoomId) return;

            // 코어실에 엉뚱한 직원이 들어갔는가 / 토끼가 엉뚱한 방에 갔는가.
            bool intruder = !string.IsNullOrEmpty(OtherAssignedTo(sim, AssignRoomId));
            string want = intruder ? "tut_assign_wrong_person"
                : !string.IsNullOrEmpty(rabbitRoom) ? "tut_assign_wrong_room"
                : "";

            if (want.Length == 0) { said = ""; await NextFrame(); continue; }
            if (want == said) { await NextFrame(); continue; }

            said = want;
            TutorialTelemetry.Count("배치 실수");
            // 되짚는 말도 지시다 — 클릭을 기다리지 않고 바로 다시 놓아 볼 수 있게 한다.
            await SayTyped(want, RoomVars(AssignRoomId), RabbitInAssignRoom);
        }
        ThrowIfAborted();   // 중단으로 빠져나왔으면 배치 성공 연출로 넘어가지 않는다
    }

    // 대사에 끼울 작업실 이름. 코드의 AssignRoomId / AccidentRoomId 를 바꿔도
    // 대사가 따라오게 한다 — 예전에는 "정비실" 이 대사 파일에 박혀 있었다.
    private System.Collections.Generic.Dictionary<string, string> RoomVars(string roomId) =>
        new() { ["ROOM"] = RoomName(roomId) };

    // 그 작업실에 배치된 '교육 대상이 아닌' 직원. 없으면 빈 값.
    private string OtherAssignedTo(FacilitySimulation sim, string roomId)
    {
        if (sim == null || string.IsNullOrEmpty(roomId)) return "";
        foreach (string id in sim.GetEmployeeIds())
        {
            if (id == TutorialEmployeeId) continue;
            if (sim.GetEmployeeState(id)?.AssignedRoomId == roomId) return id;
        }
        return "";
    }

    // ── 교육 전용 통화 ──────────────────────────────────────────────

    // 안내가 뜨기 전에 벨을 먼저 울리는 시간.
    private const double TutorialCallRingLeadSeconds = 1.3;

    private static void RingTutorialCall(string caller, string dialogueEvent, string roomId)
    {
        if (string.IsNullOrEmpty(caller)) return;
        if (Phone3D.Instance is { IsBusy: false })
            Phone3D.Instance.RingIncoming(caller, dialogueEvent, roomId);
    }

    // 못 받으면(직원이 끊으면) 다시 건다 — 교육이 멈추지 않게.
    private async Task WaitForIncomingCallAnswered(string caller, string dialogueEvent, string roomId)
    {
        if (string.IsNullOrEmpty(caller)) return;

        bool firstRing = true;
        // 다시 걸기 전의 빈틈에도 "받으라" 는 지시는 이어진다(벨이 잠깐 멈춰도 띠 위 클릭을 받는다).
        _phoneInvited = true;
        try
        {
            while (PhoneCallHud.Instance?.IsOpen != true)
            {
                if (!firstRing) TutorialTelemetry.Count("전화 재발신");
                firstRing = false;
                RingTutorialCall(caller, dialogueEvent, roomId);
                // 벨이 울리는 동안(또는 다시 걸기 전) 잠깐 기다린다.
                await Wait(1.0);
                ThrowIfAborted();
            }
        }
        finally { _phoneInvited = false; }
    }

    // 심문에서 플레이어가 던진 질문. 교육 진행 조건만 본다(대사에는 관여하지 않는다).
    private void OnInterviewAsked(string employeeId, InterviewQuestion q)
    {
        if (employeeId != TutorialEmployeeId || q == null) return;
        _rabbitAskedAnything = true;
        // 조사 자료를 근거로 던진 질문이어야 "기록으로 물었다"고 본다.
        if (!string.IsNullOrEmpty(q.EvidenceId)) _rabbitAskedWithEvidence = true;
    }

    // 휴게시간 교육용 고정 진술. 토끼의 "사고 당시 어디에 있었나" 답변만 가로채고,
    // 나머지 직원·질문은 평소대로 로그 기반 대사를 쓴다.
    private string ScriptedAnswer(string employeeId, string questionId)
    {
        if (employeeId != TutorialEmployeeId) return null;
        if (questionId != DialogueQuestions.Where) return null;
        // 옮긴 기록이 없으면 지어낼 모순도 없다 — 평소대로 로그 기반 대사를 쓰게 둔다.
        if (string.IsNullOrEmpty(_rabbitOriginRoomName)) return null;
        string text = PrologueScript.GetScripted("tut_rabbit_where");
        return string.IsNullOrEmpty(text) ? null : text.Replace("{FROM_ROOM}", _rabbitOriginRoomName);
    }

    // ── 자재 · 정비실 교육 ───────────────────────────────────────────
    //
    // 순서가 전부다. 설명이 먼저 오면 "읽고 외우는 튜토리얼"이 되고,
    // 상황이 먼저 오면 "왜 저 방이 필요한지"가 저절로 읽힌다.
    //
    //   ① 코어실에 배치된 토끼가 자재를 쓰며 복구한다
    //   ② 자재가 떨어져 복구가 멈춘다 (방 카드에 "자재 없음 — 복구 정지 중"이 뜬다)
    //   ③ 토끼가 **전화로** 먼저 알린다 — 관리자와 직원의 직접 대화는 전화뿐이다
    //   ④ GUIDE-0 는 원인과 해결만 한 줄씩 말한다
    //   ⑤ 정비실이 열린다 → 정비실 옆에 화살표 → 플레이어가 직접 배치한다 → 곧바로 다음 대사
    private async Task RunMaterialsLesson(FacilitySimulation sim)
    {
        if (sim == null) return;
        var gs = GameState.Instance;
        if (gs == null) return;

        int cost = Mathf.Max(1, Config.Instance?.Data?.MaterialsPerCoreGauge ?? 2);
        // 근무 시작 안내가 다 찍힌 뒤 1.5초 — 그때 벨이 울리고, 그 다음 대사가 이어진다.
        await Wait(FirstCallDelaySeconds);
        if (gs.Materials >= cost) gs.AddMaterials(-gs.Materials);

        // 전화를 거는 사람은 그 멈춘 자리에 있는 직원이다(없으면 교육 대상인 토끼).
        string caller = FirstOnDuty(sim, AssignRoomId);
        RingTutorialCall(caller, DialogueRepository.EventTutorialMaterialShort, AssignRoomId);
        await Wait(TutorialCallRingLeadSeconds);
        // 전화를 받는 조작은 여기서 처음 배운다. 벨이 그쳐도(못 받았다) 여기서 멈추지 않는다 —
        // 바로 아래에서 다시 건다(예전에는 받을 때까지만 기다려서 놓치면 교육이 멈췄다).
        await SayAct("tut_call", () => PhoneCallHud.Instance?.IsOpen == true
                                       || Phone3D.Instance?.IsRinging != true);
        await WaitForIncomingCallAnswered(caller, DialogueRepository.EventTutorialMaterialShort, AssignRoomId);
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);

        // 이제야 설명한다. 그리고 그 방을 **지금** 연다 — 말과 화면이 같은 순간에 바뀐다.
        sim.OpenRoomMidShift(MaterialsRoomId);
        NSP.View.FacilityMonitorView.HighlightRoomNumber(MaterialsRoomId);
        // "자재가 필요합니다" 를 말하는 동안 화면 위 자재 숫자를 하늘색 테두리로 가리킨다.
        // 둘째 줄("정비실을 열었습니다")이 뜨면 꺼진다 — 그때부터 봐야 할 곳은 작업실이다.
        HintMaterialsFor(2);
        // 둘째 줄(배치 지시)이 뜨면 정비실 옆과 옮길 직원 옆에 화살표가 선다(cue: arrow). 배치하는 순간 다음으로.
        ArmArrow(() => RoomArrowAim(MaterialsRoomId), () => PointAtOneMover(sim, MaterialsRoomId, MaterialsMoverId));
        await SayAct("tut_materials", () => AnyoneAssignedTo(sim, MaterialsRoomId));
        EndMaterialsHint();   // 둘째 줄을 읽기 전에 배치했으면 강조가 남아 있다
        HideArrow();

        // 사람이 들어가 자재가 실제로 들어오기를 잠깐만 기다린다. 걸어가서 한 바퀴 돌리는 데
        // 시간이 걸리므로, 그 사이를 교육이 채운다(늘어지지 않게 — 사용자 지시 2026-10-22).
        await UntilOrAfter(() => gs.Materials >= cost, MaterialsArriveWaitSeconds);
        if (gs.Materials < cost) gs.AddMaterials(cost - gs.Materials);
        // "자재가 들어왔습니다" — 말하는 동안 자재 숫자를 다시 짚는다(한 줄 묶음이라 끝나면 직접 끈다).
        HintMaterialsFor(2);
        await Say("tut_materials_done");
        EndMaterialsHint();
    }

    private void EndMaterialsHint()
    {
        _materialsHintLines = 0;
        HighlightMaterials(false);
    }

    // ── 보관 한도 · 저장고 교육 ──────────────────────────────────────
    //
    // 정비실이 돌기 시작하면 작은 보관함은 금방 찬다. 그때 "더 만들어도 둘 데가 없다"는
    // 전화가 오고, 저장고가 열린다. 여기서도 설명은 상황 뒤에 온다.
    private async Task RunStorageLesson(FacilitySimulation sim)
    {
        if (sim == null) return;
        var gs = GameState.Instance;
        if (gs == null) return;

        await UntilOrAfter(() => gs.Materials >= gs.MaterialsCap, SituationWaitSeconds);
        // 생산이 한도까지 따라오지 못했으면 보유량을 한도까지 채운다.
        // (한도를 내려서 맞추면 그 뒤 근무 내내 코어가 자재 부족으로 멈춰 선다.)
        if (gs.Materials < gs.MaterialsCap) gs.AddMaterials(gs.MaterialsCap - gs.Materials);

        string caller = FirstOnDuty(sim, MaterialsRoomId);
        RingTutorialCall(caller, DialogueRepository.EventTutorialStorageFull, MaterialsRoomId);
        await Wait(TutorialCallRingLeadSeconds);
        await WaitForIncomingCallAnswered(caller, DialogueRepository.EventTutorialStorageFull, MaterialsRoomId);
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);

        sim.OpenRoomMidShift(StorageRoomId);
        NSP.View.FacilityMonitorView.HighlightRoomNumber(StorageRoomId);
        // 첫 줄("자재 보관 한도가 차서…")이 뜬 동안 자재 숫자를 짚는다 — 둘째 줄에서 꺼진다.
        HintMaterialsFor(2);
        // "직원을 배치해 보십시오" 에서 기다린다 — 저장고에 사람을 넣는 순간 다음 대사.
        ArmArrow(() => RoomArrowAim(StorageRoomId), () => PointAtOneMover(sim, StorageRoomId, StorageMoverId, MaterialsRoomId));
        await SayAct("tut_storage", () => AnyoneAssignedTo(sim, StorageRoomId));
        EndMaterialsHint();
        HideArrow();
        await Say("tut_storage_done");
    }

    // 그 방에서 지금 근무 중인 첫 직원. 아무도 없으면 교육 대상(토끼)이 건다.
    private string FirstOnDuty(FacilitySimulation sim, string roomId)
    {
        var ids = sim?.OnDutyEmployeeIds(roomId);
        return ids is { Count: > 0 } ? ids[0] : TutorialEmployeeId;
    }

    // ── 사고 · 재배치 · 수리 승인 교육 ──────────────────────────────
    private async Task RunAccidentLesson(FacilitySimulation sim)
    {
        await Wait(IncidentDelaySeconds);
        // 휴게시간 모순을 만들려면 토끼가 실제로 방을 옮긴 기록이 남아야 한다.
        // 사고 직전에 토끼가 **실제로 서 있던** 방. 배치표에 적힌 방(AssignedRoomId)이 아니다 —
        // 아직 도착하지 않았으면 가 본 적 없는 방이 진술에 들어가고, 교육이 가르치려는
        // "기록과 진술을 맞대어 본다"가 거짓 자료 위에서 이루어진다.
        string originRoomId = NSP.Dialogue.DialogueContextBuilder.RoomAtOrLast(
            TutorialEmployeeId, GameState.Instance?.CurrentDay ?? 0,
            GameState.Instance?.DayTimeSeconds ?? 0f);
        if (string.IsNullOrEmpty(originRoomId))
            originRoomId = sim?.GetEmployeeState(TutorialEmployeeId)?.CurrentRoomId ?? "";
        // **최소 두 명**이어야 한다. 한 명으로 고쳐지면 "혼자 보냈는데 왜 게이지가 안 차지"
        // 를 배울 자리가 없고, 승인 요청이 사람을 보내자마자 떠 버린다. 이미 둘 이상이
        // 있는 방이면 거기에 한 명을 더.
        int need = Mathf.Max(2, (sim?.OnDutyCount(AccidentRoomId) ?? 0) + 1);
        // 사고와 함께 승인 요청이 줄에 선다. 다만 **띄우지는 않는다** —
        // 아직 아무도 보내지 않았는데 "수리를 승인하시겠습니까" 부터 뜨면 순서가 거꾸로다.
        // (단기 근무에는 승인 절차 자체가 없다 — GameModes.RepairApprovalEnabled.)
        bool approval = GameModes.RepairApprovalEnabled;
        if (approval)
        {
            RepairApprovalSystem.Held = true;
            RepairApprovalSystem.Paused = true;
        }
        sim?.TriggerTutorialAccident(AccidentRoomId, need);
        // 발전실 사고는 전력 용량을 깎는다(3 → 2). 그 일이 실제로 일어났을 때만 설명한다.
        // 이 묶음의 줄마다 cue 가 걸려 있다 — 스위치 기기 확대 · 조명 · CCTV 레버 시연 · 차폐 시연.
        if (GameState.Instance is { } gsPower
            && gsPower.PowerCapacity < (Config.Instance?.Data?.PowerCapacityMax ?? 3))
            await Say("tut_power_short");
        // 시연이 무엇을 바꿔 두었든 여기서 원래대로(줄을 넘기며 이미 돌려놓았지만 한 번 더).
        StopLeverDemos();

        // "직원 2명을 발전실로 옮겨 수리하십시오" — 발전실 옆 + 옮길 직원 옆 화살표, 필요한 인원을 보내면 다음으로.
        // 플레이어가 토끼가 아닌 다른 직원을 보내도 교육이 멈추지 않는다.
        ArmArrow(() => RoomArrowAim(AccidentRoomId), () => PointAtMovers(sim, AccidentRoomId, need));
        await SayAct("tut_relocate",
            () => AssignedCount(sim, AccidentRoomId) >= need || sim?.HasRepairPending(AccidentRoomId) == false,
            RoomVars(AccidentRoomId));
        HideArrow();
        if (sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId == AccidentRoomId)
            _rabbitOriginRoomName = RoomName(originRoomId);

        // ── 수리 승인 요청 · 미로 ──────────────────────────────────────
        // 사람을 보낸 직후, 수리가 끝나기 전에 가르친다 — 실제 근무에서 겪는 순서 그대로다.
        // 승인 절차가 없는 근무(단기 근무)에서는 가르칠 것도 없다 — 수리가 끝나기를 기다릴 뿐이다.
        if (approval) await RunApprovalLesson();

        await Until(() => sim?.HasRepairPending(AccidentRoomId) == false);
        await Say("tut_repair_done");
        // 실제 근무의 방해공작을 설명한다 — "방금처럼요" 앞에서 발전실 방해공작 경보를 한 번
        // 보여 준다(cue: demo_sabotage). DAY0 에는 방해자를 만들지 않는다 — 보여 주기용이다.
        await Say("tut_sabotage_intro");
        StopSabotageDemo();
    }

    private static int AssignedCount(FacilitySimulation sim, string roomId)
    {
        if (sim == null) return 0;
        int n = 0;
        foreach (string id in sim.GetActiveEmployeeIds())
            if (sim.GetEmployeeState(id)?.AssignedRoomId == roomId) n++;
        return n;
    }

    // ── 수리 승인 · 미로 교육 ─────────────────────────────────────────
    //
    // 승인 요청은 사고와 함께 이미 줄에 서 있다(제한 시간은 Paused 로 멈춰 있다).
    // "'예' 를 눌러 보십시오" 가 뜨는 순간 패드를 들어 올려 화면 가득 보여 주고(cue: zoom_pad),
    // [예] 를 누르면 곧바로 다음 대사(미로 설명)로 간다. 미로가 끝나면(성공이든 실패든) 내려놓는다.
    private async Task RunApprovalLesson()
    {
        // 사람을 보냈으니 이제 요청을 띄울 수 있다(제한 시간은 아직 멈춰 있다).
        RepairApprovalSystem.Held = false;
        // 요청은 **필요한 인원이 실제로 그 방에 도착한 뒤** 에 뜬다 — 걸어가는 시간이
        // 있으므로 틱 수로 세지 않고, 요청이 뜨거나 수리가 끝날 때까지 기다린다.
        await Until(() => RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking
                          || FacilitySimulation.Instance?.HasRepairPending(AccidentRoomId) == false);

        // 요청이 실제로 떠 있을 때만 가르친다(수리가 먼저 끝났거나 요청이 이미 닫혔으면 건너뛴다).
        if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking)
        {
            RepairApprovalSystem.Paused = false;
            return;
        }

        try
        {
            // 교육에서는 거절해도 벌점이 없다 — 대신 다시 띄우고 한 번 더 시킨다.
            RepairApprovalSystem.RetryOnDecline = true;
            // 지시가 다 찍히면 응답 제한 시간이 흐르기 시작한다. [예] 를 누르는 순간 다음으로.
            await SayAct("tut_approval", () => RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking,
                beforeWait: () => RepairApprovalSystem.Paused = false);
            RepairApprovalSystem.Paused = false;
            // cue 가 빠진 대본이어도 패드는 들어 올린다.
            OpenPadForApproval();

            // [예] 를 누를 때까지 반복한다. [아니오] · 무응답이면 GUIDE-0 가 한마디 하고
            // 같은 요청을 다시 띄운다(1013 QA 11번). 그동안 수리는 진행되지 않는다.
            while (true)
            {
                await Until(() => RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking);
                // 거절은 Finish 가 요청을 다시 줄에 세우고 Held 를 켜 둔다 — 그 상태로만 되돌아온다.
                if (!RepairApprovalSystem.Held) break;
                RepairApprovalSystem.Paused = true;
                await Say("tut_approval_retry");
                RepairApprovalSystem.Paused = false;
                RepairApprovalSystem.Held = false;            // 여기서 다시 뜬다
                await Until(() => RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking
                                  || FacilitySimulation.Instance?.HasRepairPending(AccidentRoomId) == false);
                if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking) break;
                OpenPadForApproval();
            }
            RepairApprovalSystem.RetryOnDecline = false;

            if (RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Maze)
            {
                // 미로 시계는 첫 방향키부터 흐르지만, 설명 중에 방향키를 눌러 시작해 버리는 것도 막는다.
                RepairApprovalSystem.Paused = true;
                await Say("tut_approval_maze");
                RepairApprovalSystem.Paused = false;
                // 풀든 실패하든 끝까지 지켜본 뒤 교육을 이어 간다.
                await Until(() => RepairApprovalSystem.Current is RepairApprovalSystem.Phase.Idle
                                      or RepairApprovalSystem.Phase.Result);
            }
        }
        finally
        {
            RepairApprovalSystem.Paused = false;
            RepairApprovalSystem.Held = false;
            RepairApprovalSystem.RetryOnDecline = false;
        }

        // 절차가 끝났다 — 결과를 잠깐 보여 주고 패드를 내려놓는다(확대 해제).
        if (AdminPad3D.Instance?.IsOpen == true)
        {
            await Wait(0.9);
            AdminPad3D.Instance?.Close();
            await Until(() => AdminPad3D.Instance?.IsOpen != true);
        }
    }

    // 패드를 들어 올려 승인 요청을 화면 가득 보여 준다(이미 들고 있으면 그대로).
    private static void OpenPadForApproval()
    {
        var pad = AdminPad3D.Instance;
        if (pad == null || pad.IsOpen) return;
        if (pad.CanOpen()) pad.Open();
    }

    // 쓰러질 사람이 아니라 "혼자 근무 중인 직원" 이다. 개체가 그 방에 나타나야
    // "혼자 개체를 마주한 직원" 이 휴게시간 증언에 남는다.
    private string PickLoneWorker(FacilitySimulation sim)
    {
        if (IsLoneWorker(sim, LoneWorkerPreferredId)) return LoneWorkerPreferredId;
        foreach (string id in sim.GetActiveEmployeeIds())
            if (IsLoneWorker(sim, id)) return id;
        return "";
    }

    // 혼자 근무 중이어야 한다. 토끼는 휴게시간 심문에서 쓰므로 제외한다.
    private bool IsLoneWorker(FacilitySimulation sim, string id)
    {
        if (string.IsNullOrEmpty(id) || id == TutorialEmployeeId) return false;
        var st = sim.GetEmployeeState(id);
        if (st == null || !st.Alive || st.Incapacitated || st.Isolated || st.IsMoving) return false;
        if (string.IsNullOrEmpty(st.CurrentRoomId)) return false;
        if (sim.OnDutyCount(st.CurrentRoomId) != 1) return false;
        // 수리가 걸린 방은 피한다 — 거기서 개체까지 겹치면 무엇을 봐야 하는지 흐려진다.
        return !sim.HasRepairPending(st.CurrentRoomId);
    }

    private void Finish()
    {
        // 블라인드 테스트 기록은 교육이 어떻게 끝났든 한 번만 남는다(중단 포함).
        TutorialTelemetry.End(_reachedEnd);
        IsRunning = false;
        _materialsHintLines = 0;
        _phoneInvited = false;
        _endShiftInvited = false;
        _materialsFlashGen++;
        RoomEffectStats.MaterialsGained -= OnMaterialsGained;
        HideArrow();
        StopLeverDemos();
        StopSabotageDemo();
        StopContainmentDemo();
        HighlightMaterials(false);
        NSP.View.FacilityMonitorView.HighlightEndShift(false);
        RepairApprovalSystem.Paused = false;
        RepairApprovalSystem.Held = false;
        RepairApprovalSystem.RetryOnDecline = false;
        // 교육이 상황에 맞춰 연 작업실과 줄여 둔 보관 한도를 되돌린다.
        // (자재 보유량 자체는 DAY1 로 넘어갈 때 GameState.GoToNextDay 가 처음 값으로 되돌린다.)
        DayFeatures.ForceRoomOpen(MaterialsRoomId, false);
        DayFeatures.ForceRoomOpen(StorageRoomId, false);
        if (_savedMaterialsCap >= 0 && GameState.Instance != null)
        {
            GameState.Instance.AddMaterialsCap(_savedMaterialsCap - GameState.Instance.MaterialsCap);
            _savedMaterialsCap = -1;
        }
        LocalDialogueGenerator.ScriptedAnswerOverride = null;
        InterviewSession.Asked -= OnInterviewAsked;
        if (_guide != null)
        {
            _guide.LineShown -= OnGuideLine;
            _guide.CueStarted -= OnCueStarted;
            _guide.CueEnded -= OnCueEnded;
        }
        GuideCornerFace.ShowAll(false);
        GuideCornerFace.SetLifted(false);
        _guide?.HideHologram();
        GuideSubtitleHud.Instance?.SetTopAligned(false);
        GuideSubtitleHud.Instance?.SetActive(false);
        GuideSubtitleHud.Instance?.Clear();
        RestRosterView.Instance?.SetNextEnabled(true);
    }

    // 자재 강조를 끌 때까지 남은 안내 줄 수. 0 이 되는 순간 테두리가 꺼진다.
    // (GUIDE-0 의 한 안내가 여러 줄이라 "몇 번째 줄에서" 를 이 수로 센다.)
    private int _materialsHintLines;

    private void HintMaterialsFor(int lines)
    {
        _materialsHintLines = Mathf.Max(1, lines);
        HighlightMaterials(true);
    }

    // 모니터1 위 「자재 N / M」 — 하늘색 테두리와 함께 그 옆에 화살표를 세운다(자재가 모자라거나
    // 들어오는 순간). 테두리만으로는 화면 어디를 봐야 하는지 눈에 잘 안 들어왔다.
    private const string MaterialsArrowKey = "materials";

    private static void HighlightMaterials(bool on)
    {
        NSP.View.FacilityMonitorView.HighlightMaterials(on);
        if (on) TutorialArrow.PointAlso(MaterialsArrowKey, MaterialsArrowAim);
        else TutorialArrow.ClearAlso(MaterialsArrowKey);
    }

    // 「자재 N / M」 왼쪽에서 그 숫자를 가리킨다(오른쪽은 모니터 가장자리다).
    private static TutorialArrow.Aim? MaterialsArrowAim()
    {
        var view = FacilityMonitorView.Instance;
        return view == null ? null : TutorialArrow.BesideOnMonitor(view, view.MaterialsRect, TutorialArrow.Side.Left);
    }

    // 정비실이 자재를 만들었다 — 화면 위 「자재 N / M」 을 하늘색 테두리로 잠깐 짚었다가 끈다.
    // 안내 줄이 테두리를 켜 둔 동안(HintMaterialsFor)에는 그쪽이 끈다 — 여기서는 건드리지 않는다.
    //
    // 교육은 5배속이라 자재가 0.6초마다 들어온다 — 들어올 때마다 켜면 테두리가 꺼질 틈이 없다.
    // 한 번 짚으면 MaterialsFlashGapSeconds 동안은 다시 켜지 않는다. 보관함이 가득 차 숫자가
    // 그대로인 동안에는 짚지 않는다(넘치는 자재는 버려지고 있다).
    private const double MaterialsFlashGapSeconds = 4.0;
    private int _materialsFlashGen;
    private double _materialsFlashNext;

    private void OnMaterialsGained()
    {
        if (!IsRunning || _materialsHintLines > 0) return;
        var gs = GameState.Instance;
        if (gs == null || gs.Materials >= gs.MaterialsCap) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _materialsFlashNext) return;
        _materialsFlashNext = now + MaterialsFlashGapSeconds;
        HighlightMaterials(true);
        _ = EndMaterialsFlash(++_materialsFlashGen);
    }

    private async Task EndMaterialsFlash(int gen)
    {
        try { await Wait(MaterialsFlashSeconds); }
        catch (OperationCanceledException) { return; }
        if (gen == _materialsFlashGen && _materialsHintLines == 0)
            HighlightMaterials(false);
    }

    private void OnGuideLine(string text)
    {
        GuideSubtitleHud.Instance?.SetLine(text);
        if (_materialsHintLines > 0 && --_materialsHintLines == 0)
            HighlightMaterials(false);
    }

    // 그 작업실에 **배치된** 직원이 하나라도 있는가. 도착(OnDutyCount)이 아니라 배치로 본다 —
    // 끌어다 놓은 그 순간에 "했다"고 읽혀야 안내가 멈추지 않는다.
    private static bool AnyoneAssignedTo(FacilitySimulation sim, string roomId)
    {
        if (sim == null || string.IsNullOrEmpty(roomId)) return false;
        foreach (string id in sim.GetActiveEmployeeIds())
            if (sim.GetEmployeeState(id)?.AssignedRoomId == roomId) return true;
        return false;
    }

    private static string RoomName(string roomId) =>
        FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId ?? "";

    // ── 이상 개체 · 차폐 교육 ─────────────────────────────────────────
    //
    //   ① "괴물이 개체들을 탈출시키고 있습니다" → "지금처럼 이상한 소리가 들린다면…"
    //      그 줄에서 개체가 작업실에 나타나고 비명 · 벽 두드리는 소리가 난다(cue: ghost_noise).
    //      CCTV 로 그 방을 찾을 때까지 기다린다.
    //   ② 찾았다 → 개체가 복도로 나와 관리자에게 걸어온다 → 복도 CCTV 확인
    //   ③ 접근 — 모니터2 를 확대해 보여 주고(cue: zoom_monitor2), 넘기면 스위치 기기로
    //      옮겨 간다(cue: zoom_switch_hold). 차폐가 닫히는 순간 확대를 푼다.
    //   ④ 두드림 → 후퇴 → 차폐 해제
    //
    // 교육 중에는 **절대 죽지 않는다**(MonsterThreatSystem.NoBreach). 자동으로 문을 닫아 주지도
    // 않는다 — 플레이어가 직접 올려야 다음으로 간다.
    private const string BarrierLessonSegment = "corridor_west";
    // 교육용 개체가 복도를 걸어오는 시간 배율(데이터 10초 × 2 = 20초) — 다가오는 것이 화면에 보이게.
    private const float BarrierLessonApproachScale = 2f;

    // 개체가 나타날 작업실 · 나타났는가(cue: ghost_noise 가 띄운다).
    private string _ghostRoom = "";
    private bool _ghostShown;

    private async Task RunBarrierLesson(FacilitySimulation sim)
    {
        if (sim?.Ghost == null || sim.Threats == null) return;
        var net = sim.Corridors;
        var west = net?.ById(BarrierLessonSegment);

        // ── 6-1. 작업실 출현 ───────────────────────────────────────────
        string lone = PickLoneWorker(sim);
        string room = string.IsNullOrEmpty(lone) ? AnomalyRoomId
            : sim.GetEmployeeState(lone)?.CurrentRoomId ?? AnomalyRoomId;
        // 작업실마다 **들어오는 접근 복도가 정해져 있다**(1013 QA 12번). 이 교육의 안내는
        // "서측" 으로 못 박혀 있으므로, 서측으로 이어지지 않는 방이 뽑혔으면 서측 방
        // (경비실)으로 되돌린다 — 말과 화면이 어긋나는 쪽이 훨씬 나쁘다.
        if (sim.Threats.ApproachFrom(room) != BarrierLessonSegment) room = AnomalyRoomId;
        _ghostRoom = room;
        _ghostShown = false;
        sim.Threats.NoBreach = true;

        // "지금처럼 이상한 소리가 들린다면 CCTV 를 돌려 찾으셔야 합니다" — 그 방을 띄우는 순간이
        // "찾았다" 다. 설명을 다 듣기 전에 먼저 찾아도 그대로 다음으로 넘어간다.
        await SayAct("tut_anomaly_intro",
            () => _ghostShown && (sim.SurveillanceTargetRoomId == room || !sim.Ghost.Active),
            beforeWait: AppearGhost);
        if (!sim.Ghost.Active || west == null) { CleanUpBarrierLesson(sim); return; }

        // ── 6-2. 관측해도 소멸하지 않음 ────────────────────────────────
        await Say("tut_barrier_found");
        // 작업실에서 지우고(= 작업실 CCTV 에서도 사라진다) 복도로 내보낸다.
        // 들어올 방향은 교육이므로 서측으로 고정한다. 목격 기록은 그대로 남는다.
        sim.Ghost.ForceDepart(sim, notify: false);
        var threat = sim.Threats.ForceSpawn("absentee", BarrierLessonSegment);
        if (threat == null) { CleanUpBarrierLesson(sim); return; }
        threat.OriginRoomId = room;
        // "서측 복도로 진입했습니다" 가 곧바로 사실이어야 한다 — 출현 구역에서 복도 입구까지 걷는
        // 시간(10초)을 건너뛰고 지금 복도에 들어선다. 걸음은 늦춰서, 복도 CCTV 를 띄우고
        // "개체가 접근 중입니다" 를 읽는 동안 실제로 다가오는 것이 보이게 한다(2026-10-27).
        threat.OuterSeconds = 0.05f;
        threat.ApproachScale = BarrierLessonApproachScale;

        // ── 6-3. 복도 CCTV 확인 ────────────────────────────────────────
        // 지도의 서측 복도 네모 위에 화살표 — 그 복도 CCTV 를 띄우는 순간 걷는다.
        ArmArrow(() => CorridorArrowAim(BarrierLessonSegment));
        await SayAct("tut_barrier_corridor", () => sim.SurveillanceCorridorId == BarrierLessonSegment);
        HideArrow();

        // ── 6-4~6-5. 접근 · 차폐 조작 ──────────────────────────────────
        // 전력이 없으면 아무리 설명해도 문이 내려가지 않는다. 그럴 때만 한 번 짚어 준다.
        if (!BarrierPowerReady()) await Say("tut_barrier_power");
        // 레버가 닫는 통로는 지도에서 고른 통로다. 방금 서측 복도 CCTV 를 띄웠으니 대개 이미
        // 서측이지만, 스위치 기기를 확대한 채로는 지도를 누를 수 없으므로 여기서 맞춰 둔다.
        net.Select(BarrierLessonSegment);
        // 줄 1 = 모니터2 확대 / 줄 2 = 스위치 기기 확대(cue). 차폐가 닫히는 순간 다음으로.
        await SayTyped("tut_barrier_close", null, () => west.Sealed);

        // 올바른 문을 내릴 때까지. 다른 문을 내렸으면 방향을 다시 알려 준다.
        bool warned = false;
        while (IsRunning && IsInstanceValid(this) && !west.Sealed)
        {
            if (!warned && net.Blockable.Any(s => s.Id != BarrierLessonSegment && s.Sealed))
            {
                warned = true;
                await SayTyped("tut_barrier_wrong", null, () => west.Sealed);
            }
            await NextFrame();
        }
        // 차폐 성공 — 레버 화살표를 걷고 확대를 풀어 제어실 전체로 돌아온다.
        TutorialArrow.ClearAlso(CueArrowKey("zoom_switch_hold"));
        _holdSwitchZoom = false;
        if (Ctl?.IsPowerPanelFocused == true || Ctl?.IsMonitorFocused(2) == true) Ctl?.ClearFocus(0.45f);

        // ── 6-6. 차폐문 공격 ───────────────────────────────────────────
        await Until(() => threat.Phase is ThreatPhase.Pounding or ThreatPhase.Retreat
                          or ThreatPhase.Finished);
        await Say("tut_barrier_hold");
        await Until(() => threat.Phase is ThreatPhase.Retreat or ThreatPhase.Finished);

        // ── 6-7. 차폐 해제 ─────────────────────────────────────────────
        // "이제 차폐를 해제하십시오" — 세 번째(BARRIER) 레버 위에 화살표, 문이 다 열리면 걷는다.
        ArmArrow(() => LeverAim(PowerConsumer.Barrier));
        await SayAct("tut_barrier_retreat", () => !west.Sealed && !west.Moving);
        HideArrow();
        await Say("tut_barrier_done");

        // 교육이 남긴 것을 전부 지운다 — 임시 괴물 · 차폐 · 공포 상태가 DAY1 로 넘어가면 안 된다.
        CleanUpBarrierLesson(sim);
    }

    // 개체를 작업실에 띄운다 — 비명과 벽을 두드리는 소리가 같이 난다(어느 방인지는 알려 주지 않는다).
    private void AppearGhost()
    {
        if (_ghostShown || string.IsNullOrEmpty(_ghostRoom)) return;
        var sim = FacilitySimulation.Instance;
        if (sim?.Ghost == null) return;
        // 교육 중에는 사고로 번지지 않는다(유예 9999초).
        if (!sim.Ghost.ForceAppear(_ghostRoom, sim, 9999f) && !sim.Ghost.Active) return;
        _ghostShown = true;
        _ = PlayGhostNoise();
    }

    // 벽 너머의 비명 + 쿵 · 쿵 · 쿵. 크게 지르지 않는다 — "어디선가" 까지만 들려야 찾을 이유가 생긴다.
    private async Task PlayGhostNoise()
    {
        try
        {
            Sfx.Instance?.PlayGhostScreamDistant(-5f);
            double[] beats = { 0.55, 0.42, 0.62, 0.36 };
            foreach (double gap in beats)
            {
                await Wait(gap);
                Sfx.Instance?.Play("impact_blunt", -7f, (float)GD.RandRange(0.62, 0.74));
                Sfx.Instance?.Play("door_knock_soft", -3f, (float)GD.RandRange(0.82, 0.95));
                Ctl?.ShakeCamera(0.28f, 0.16f);
            }
        }
        catch (OperationCanceledException) { }
    }

    // 차폐 장치에 전력이 갈 수 있는 상태인가(이미 켜져 있거나, 남은 용량이 있거나).
    private static bool BarrierPowerReady()
    {
        var gs = GameState.Instance;
        if (gs == null) return false;
        return gs.IsConsumerPowered(PowerConsumer.Barrier) || gs.GetPowerRemaining() > 0;
    }

    private void CleanUpBarrierLesson(FacilitySimulation sim)
    {
        StopContainmentDemo();
        _holdSwitchZoom = false;
        TutorialArrow.ClearAlso(CueArrowKey("zoom_switch_hold"));
        _ghostRoom = "";
        if (sim == null) return;
        if (sim.Ghost?.Active == true) sim.Ghost.ForceDepart(sim, notify: false);
        if (sim.Threats != null)
        {
            sim.Threats.NoBreach = false;
            sim.Threats.Reset();
        }
        sim.Corridors?.ResetAll();
        NSP.Ui.AdminFearDirector.Instance?.ResetAll();
        sim.SetCorridorSurveillance("");
    }

    // ── 휴게시간 · 기록 비교 교육 ───────────────────────────────────────
    private async Task RunRestLesson()
    {
        // 근무는 실제 근무와 같은 자리 — 오른쪽 아래 「T 업무」 창에서 끝낸다(2026-10-27).
        // 「T 업무」 버튼 위에 화살표 → 창을 열면 그 안의 「근무 종료」 옆으로 옮겨 간다 →
        // 누르는 순간 사라진다. 이 단계 전에는 근무 종료가 막혀 있다(BlocksEndShift).
        _endShiftInvited = true;
        // 화면 아래 자막 띠가 「T 업무」 버튼 윗부분을 덮는다 — 이 단계 동안만 띠를 위로 올린다.
        GuideSubtitleHud.Instance?.SetTopAligned(true);
        bool endRequested = false;
        void OnEndRequested() => endRequested = true;
        var overlay = Day1HistoryOverlay.Instance;
        if (overlay != null) overlay.EndShiftRequested += OnEndRequested;
        try
        {
            ArmArrow(EndShiftArrowAim);
            await SayAct("tut_endshift", () => endRequested
                                               || FacilityMonitorView.Instance?.EndShiftConfirmOpen == true
                                               || GameState.Instance?.CurrentPhase != GamePhase.Live);
        }
        finally
        {
            if (overlay != null && IsInstanceValid(overlay)) overlay.EndShiftRequested -= OnEndRequested;
        }
        HideArrow();
        GuideSubtitleHud.Instance?.SetTopAligned(false);
        await Until(() => GameState.Instance?.CurrentPhase == GamePhase.Rest);
        // 교육이 끝나기 전에는 다음 날로 넘어갈 수 없다.
        RestRosterView.Instance?.SetNextEnabled(false, "교육 진행 중...");

        // "토끼 직원을 선택한 뒤, 수화기를 들어 통화해 보십시오" — 토끼 아이콘 옆 화살표,
        // 토끼를 고르면 전화기 위에서 아래를 가리킨다. 수화기를 드는 순간 사라진다.
        ArmArrow(RestCallArrowAim);
        _phoneInvited = true;
        try
        {
            await SayAct("tut_rest", () => PhoneCallHud.Instance?.IsOpen == true
                                           && PhoneCallHud.Instance.CurrentEmployeeId == TutorialEmployeeId);
        }
        finally { _phoneInvited = false; }
        HideArrow();
        // 심문 대화창이 화면 아래 절반을 차지한다(일반 통화와 같은 자리) —
        // 지시문도 얼굴창도 위로 올라가야 가리지 않는다. 대화창을 접어도 이 자리는 그대로 둔다.
        GuideSubtitleHud.Instance?.SetTopAligned(true);
        GuideCornerFace.SetLifted(true);

        // "숫자 키 '1'" 줄에서 모니터1 을 확대한다(cue: zoom_monitor1).
        // 이동 기록으로 물으면 통과. 옮긴 기록이 아예 없는 판이면 아무 질문이나 하면 된다.
        // 지시 줄이 뜨면 조사 노트 목록 위 → 자료를 고르면 대화창의 「[자료]로 묻는다.」 위에 화살표.
        ArmArrow(AskEvidenceArrowAim);
        await SayAct("tut_ask_where", () => _rabbitAskedWithEvidence
                          || (string.IsNullOrEmpty(_rabbitOriginRoomName) && _rabbitAskedAnything));
        HideArrow();
        // L 로 시설 기록을 여는 법은 **언제나** 가르친다. 예전에는 토끼가 실제로 방을 옮긴 판에서만
        // 띄웠는데, 사고 수리에 다른 직원을 보내면(화살표도 코어실 밖 직원부터 가리킨다) 토끼가
        // 옮기지 않아 이 안내가 통째로 빠졌다. 안내 문구는 모순을 말하지 않는다 — 기록을 여는 법뿐이다.
        await SayAct("tut_contradiction", () => Day1HistoryOverlay.Instance?.IsLogOpen == true);
        // D 를 눌러 대화 기록을 **실제로 연 뒤에만** "통화를 종료하라"는 다음 줄이 뜬다.
        await SayAct("tut_dialogue_log", () => Day1HistoryOverlay.Instance?.IsDialogueOpen == true);
        // "통화를 종료해보십시오" 가 뜨는 순간 확대를 푼다(cue: zoom_clear).
        // 「통화를 종료한다.」 옆 화살표 — 자료 질문 목록이 떠 있으면 먼저 「…다른 질문을 한다.」 옆.
        // 통화가 끝날 때까지 따라다니다 끊는 순간 사라진다.
        ArmArrow(EndCallArrowAim);
        await SayAct("tut_end_call", () => PhoneCallHud.Instance?.IsOpen != true);
        HideArrow();
        if (Ctl?.IsMonitorFocused(1) == true) Ctl?.ClearFocus(0.45f);
    }

    // ── 대사 줄 연출(cue) ────────────────────────────────────────────

    // 대본의 cue 이름 → 연출. 모르는 이름은 조용히 넘어간다(대본 오타로 교육이 멈추면 안 된다).
    private void OnCueStarted(string name)
    {
        if (!IsRunning) return;
        switch (name)
        {
            // 이 단계의 화살표를 지금 띄운다(무엇을 가리킬지는 단계가 ArmArrow 로 정해 둔다).
            case "arrow": ShowArmedArrow(); break;

            // 스위치 기기 확대 — 다음 줄로 넘어가면 풀린다. 「POWER n / m」 옆에 화살표.
            case "zoom_switch":
                Ctl?.FocusPowerPanel(0.55f);
                TutorialArrow.PointAlso(CueArrowKey(name), PowerLabelAim);
                break;
            // 확대를 한 번 풀고 스위치 기기로 다시 붙는다. 차폐가 닫힐 때까지 유지한다.
            // 세 번째(BARRIER) 레버 위에 화살표 — 차폐가 닫히면 걷는다(RunBarrierLesson).
            case "zoom_switch_hold":
                _holdSwitchZoom = true;
                _guide?.HoldTyping(0.95);
                _ = ZoomSwitchAfterUnfocus();
                TutorialArrow.PointAlso(CueArrowKey(name), () => LeverAim(PowerConsumer.Barrier));
                break;
            case "zoom_monitor1": Ctl?.FocusMonitor(1, 0.45f); break;
            // 모니터2 를 먼저 당긴 뒤 글자가 찍힌다.
            case "zoom_monitor2":
                Ctl?.FocusMonitor(2, 0.45f);
                _guide?.HoldTyping(0.5);
                break;
            case "zoom_pad": OpenPadForApproval(); break;
            case "zoom_clear": Ctl?.ClearFocus(0.45f); break;

            // 스위치 기기 시연 — 레버를 실제로 내렸다 올린다(두 번). 그 줄의 레버 위에만 화살표.
            case "flick_lighting":
                StartLeverFlick(PowerConsumer.Lighting);
                TutorialArrow.PointAlso(CueArrowKey(name), () => LeverAim(PowerConsumer.Lighting));
                break;
            case "flick_cctv":
                StartLeverFlick(PowerConsumer.CctvWatch);
                TutorialArrow.PointAlso(CueArrowKey(name), () => LeverAim(PowerConsumer.CctvWatch));
                break;
            // 복도 CCTV 를 띄우고 차폐 레버를 올려 문이 닫히는 것을 보여 준다.
            case "demo_barrier":
                StartBarrierDemo();
                TutorialArrow.PointAlso(CueArrowKey(name), () => LeverAim(PowerConsumer.Barrier));
                break;
            // 발전실 방해공작 경보 + CCTV 오류 화면. 경보가 먼저 뜨고 글자가 따라온다.
            case "demo_sabotage":
                StartSabotageDemo();
                _guide?.HoldTyping(1.5);
                break;

            // 개체 출현 — 비명 · 벽 두드리는 소리.
            case "ghost_noise": AppearGhost(); break;

            // 화면 위 「봉쇄 불안정」 게이지를 띄워 차오르게 하고, 그 옆에 화살표를 세운다.
            // 화살표는 불안정성 설명이 끝날 때(containment_settle 줄이 넘어갈 때)까지 남는다.
            case "containment":
                StartContainmentDemo(ContainmentDemoPeak, 3.2);
                TutorialArrow.PointAlso(ContainmentArrowKey, ContainmentGaugeAim);
                break;
            // 괴물을 격리하면 가라앉는다 — 게이지가 다시 내려간다.
            case "containment_settle":
                StartContainmentDemo(0f, 2.4);
                TutorialArrow.PointAlso(ContainmentArrowKey, ContainmentGaugeAim);
                break;
        }
    }

    // 줄이 넘어가거나 묶음이 닫혔다. 그 줄에서만 유지되는 연출만 여기서 걷는다.
    // (화살표 · 패드 · 차폐용 확대는 그 단계가 행동을 확인한 뒤에 직접 걷는다.)
    private void OnCueEnded(string name)
    {
        // 그 줄에만 서는 화살표(POWER 글자 · 레버 하나). 차폐용 레버 화살표는 문이 닫힐 때 걷는다.
        if (name != "zoom_switch_hold") TutorialArrow.ClearAlso(CueArrowKey(name));
        switch (name)
        {
            case "zoom_switch":
                if (Ctl?.IsPowerPanelFocused == true && !_holdSwitchZoom) Ctl.ClearFocus(0.45f);
                break;
            case "zoom_monitor2":
                if (Ctl?.IsMonitorFocused(2) == true) Ctl.ClearFocus(0.4f);
                break;
            case "flick_lighting":
            case "flick_cctv":
                StopLeverFlick();
                break;
            case "demo_barrier": StopBarrierDemo(); break;
            case "demo_sabotage": StopSabotageDemo(); break;
            // 차오르던 게이지는 다음 줄(더 많은 개체가 나온다)에서도 조금 더 오른다 — 눈금을 넘어 붉어진다.
            case "containment": StartContainmentDemo(ContainmentDemoTop, 3.6); break;
            case "containment_settle": StopContainmentDemo(); break;
        }
    }

    // ── 봉쇄 불안정 시연 ─────────────────────────────────────────────
    //
    // 가상 시뮬레이션에는 결번이 없어 봉쇄 불안정이 실제로 오르지 않는다(ShiftHud 도 숨긴다).
    // "괴물이 있는 한 오른다 → 많이 오르면 개체가 더 나온다 → 격리하면 가라앉는다" 를 말하는 동안만
    // 화면의 그 게이지를 시연 값으로 띄운다. 시뮬레이션의 불안정성은 건드리지 않는다.
    private const string ContainmentArrowKey = "containment_gauge";
    // 실제 근무처럼 50% 에서 시작해 오르고(눈금 75% 를 넘으면 붉어진다), 격리 줄에서 0 까지 내려간다.
    private const float ContainmentDemoStart = 50f;
    private const float ContainmentDemoPeak = 68f;
    private const float ContainmentDemoTop = 88f;
    private int _containmentDemoGen;

    private void StartContainmentDemo(float to, double seconds)
    {
        int gen = ++_containmentDemoGen;
        float from = ShiftHud.DemoContainment ?? ContainmentDemoStart;
        ShiftHud.DemoContainment = from;
        _ = RunContainmentDemo(gen, from, to, seconds);
    }

    private async Task RunContainmentDemo(int gen, float from, float to, double seconds)
    {
        try
        {
            double t = 0;
            while (gen == _containmentDemoGen && t < seconds)
            {
                await NextFrame();
                t += GetProcessDeltaTime();
                float k = Mathf.Clamp((float)(t / Mathf.Max(0.05, seconds)), 0f, 1f);
                if (gen == _containmentDemoGen)
                    ShiftHud.DemoContainment = Mathf.Lerp(from, to, 1f - (1f - k) * (1f - k));
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopContainmentDemo()
    {
        _containmentDemoGen++;
        ShiftHud.DemoContainment = null;
        TutorialArrow.ClearAlso(ContainmentArrowKey);
    }

    // 화면 위 「봉쇄 불안정 N%」 막대 오른쪽에서 그 막대를 가리킨다.
    private static TutorialArrow.Aim? ContainmentGaugeAim()
    {
        var gauge = ShiftHud.Instance?.ContainmentGaugeControl;
        return gauge == null ? null
            : TutorialArrow.BesideOnScreen(gauge, new Rect2(Vector2.Zero, gauge.Size), TutorialArrow.Side.Right);
    }

    // 차폐 단계의 스위치 확대 — 다음 줄로 넘어가도 풀지 않는다(차폐가 닫힐 때 직접 푼다).
    private bool _holdSwitchZoom;

    // 모니터2 확대를 풀고(자리로 돌아오는 것이 보이게 잠깐 둔 뒤) 스위치 기기로 붙는다.
    // 글자는 그동안 HoldTyping 으로 멈춰 있다 — 기기가 화면에 들어온 뒤에 찍힌다.
    private async Task ZoomSwitchAfterUnfocus()
    {
        try
        {
            if (Ctl?.IsPowerPanelFocused == true) return;
            Ctl?.ClearFocus(0.35f);
            await Wait(0.4);
            if (!_holdSwitchZoom) return;
            Ctl?.FocusPowerPanel(0.55f);
        }
        catch (OperationCanceledException) { }
    }

    // ── 레버 시연 ────────────────────────────────────────────────────

    // 시연이 돌고 있는 동안 원래 상태를 기억해 둔다(끝나면 그대로 돌려놓는다).
    private int _flickGen;
    private PowerConsumer? _flickChannel;
    private bool _flickWasOn;

    // 내림(1초) → 올림(1초) → 내림(1초) → 올림. 줄이 넘어가면 그 자리에서 멈추고 되돌린다.
    private void StartLeverFlick(PowerConsumer channel)
    {
        StopLeverFlick();
        var gs = GameState.Instance;
        if (gs == null) return;
        _flickChannel = channel;
        _flickWasOn = gs.IsConsumerPowered(channel);
        _ = RunLeverFlick(channel, ++_flickGen);
    }

    private async Task RunLeverFlick(PowerConsumer channel, int gen)
    {
        try
        {
            for (int i = 0; i < 2; i++)
            {
                if (gen != _flickGen) return;
                PowerSwitchPanel.DemoSet(channel, false);
                await Wait(1.0);
                if (gen != _flickGen) return;
                PowerSwitchPanel.DemoSet(channel, true);
                await Wait(1.0);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopLeverFlick()
    {
        _flickGen++;
        if (_flickChannel is { } ch) PowerSwitchPanel.DemoSet(ch, _flickWasOn);
        _flickChannel = null;
    }

    // 차폐 시연 상태.
    private bool _barrierDemoOn;
    private bool _barrierDemoWasPowered;
    private string _barrierDemoPrevRoom = "";
    private string _barrierDemoPrevCorridor = "";

    // 복도 아무 곳의 CCTV 를 띄우고 세 번째(차폐) 레버를 올린다 — 그 복도 문이 내려오는 것이 보인다.
    // 발전실 사고로 전력이 2/2 인 때라 용량을 보지 않고 올린다(보여 주기용). 다음 줄에서 내린다.
    private void StartBarrierDemo()
    {
        var sim = FacilitySimulation.Instance;
        var net = sim?.Corridors;
        var gs = GameState.Instance;
        if (net == null || gs == null) return;
        var seg = net.ById(BarrierLessonSegment) ?? net.Blockable.FirstOrDefault();
        if (seg == null) return;

        _barrierDemoOn = true;
        _barrierDemoWasPowered = gs.IsConsumerPowered(PowerConsumer.Barrier);
        _barrierDemoPrevRoom = sim.SurveillanceTargetRoomId ?? "";
        _barrierDemoPrevCorridor = sim.SurveillanceCorridorId ?? "";
        sim.SetCorridorSurveillance(seg.Id);
        net.Select(seg.Id);
        PowerSwitchPanel.DemoSet(PowerConsumer.Barrier, true);
        net.Seal(seg.Id, out _);
    }

    private void StopBarrierDemo()
    {
        if (!_barrierDemoOn) return;
        _barrierDemoOn = false;
        var sim = FacilitySimulation.Instance;
        // 차폐 전원을 내리면 닫혔던 문은 저절로 열린다(CorridorNet.Tick — 전력 없으면 연다).
        PowerSwitchPanel.DemoSet(PowerConsumer.Barrier, _barrierDemoWasPowered);
        if (sim == null) return;
        if (_barrierDemoPrevCorridor.Length > 0) sim.SetCorridorSurveillance(_barrierDemoPrevCorridor);
        else if (_barrierDemoPrevRoom.Length > 0) sim.SetSurveillanceTarget(_barrierDemoPrevRoom);
    }

    private void StopLeverDemos()
    {
        StopLeverFlick();
        StopBarrierDemo();
    }

    // ── 방해공작 시연 ────────────────────────────────────────────────

    private bool _sabotageDemoOn;

    // 실제 근무에서 발전실 방해공작이 터질 때와 같은 경보(붉은 점멸 + 상단 배너)를 띄우고,
    // 가상 시뮬레이션에서만 CCTV 화면을 노이즈 + ERROR! 로 망가뜨린다. 전력은 건드리지 않는다.
    private void StartSabotageDemo()
    {
        if (_sabotageDemoOn) return;
        _sabotageDemoOn = true;
        NSP.Ui.FacilityAlertHud.Instance?.ShowCriticalAlert("▲ 발전 계통에서 비정상적인 손상이 감지되었습니다!");
        CCTVMonitorView.Instance?.SetDemoError(true);
    }

    private void StopSabotageDemo()
    {
        if (!_sabotageDemoOn) return;
        _sabotageDemoOn = false;
        CCTVMonitorView.Instance?.SetDemoError(false);
    }

    // ── 안내 화살표 ──────────────────────────────────────────────────

    // 이 단계에서 띄울 화살표. 대본의 cue: arrow 줄이 뜨는 순간 실제로 뜬다 — cue 가 없는
    // 대본이면 지시 줄이 다 찍혀 행동을 기다리기 시작할 때 뜬다(SayAct).
    private Func<TutorialArrow.Aim?> _armedArrow;
    // 주 화살과 같이 띄울 곁 화살(옮길 직원 옆 등). 주 화살이 뜨는 순간 함께 뜬다.
    private Action _armedExtras;

    private void ArmArrow(Func<TutorialArrow.Aim?> aim, Action extras = null)
    {
        _armedArrow = aim;
        _armedExtras = extras;
    }

    private void ShowArmedArrow()
    {
        if (_armedArrow != null) TutorialArrow.PointAt(_armedArrow);
        _armedExtras?.Invoke();
        _armedArrow = null;
        _armedExtras = null;
    }

    private void HideArrow()
    {
        _armedArrow = null;
        _armedExtras = null;
        TutorialArrow.Clear();
    }

    // ── 곁 화살 — 옮길 직원 ──────────────────────────────────────────

    private static string MoverKey(string employeeId) => "emp:" + employeeId;

    // 배치 화면 — 아직 배치하지 않은 직원(토끼 제외 — 고양이 · 여우) 카드 옆마다.
    // 카드는 배치한 뒤에도 명단에 남아 있으므로(아래 줄만 "→ 방" 으로 바뀐다) 배치 여부로 거른다 —
    // 놓는 순간 그 직원의 화살표만 사라진다. 전원을 놓으면 「근무 시작」 옆에 화살표가 선다.
    private void PointAtUnplacedRoster()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == TutorialEmployeeId) continue;
            string captured = id;
            TutorialArrow.PointAlso(MoverKey(captured), () =>
            {
                var map = NSP.Ui.ScheduleMapView.Instance;
                if (map == null || IsPlaced(sim, captured)) return null;
                return TutorialArrow.BesideOnMonitor(map, map.RosterCardOf(captured));
            });
        }
        TutorialArrow.PointAlso("start", () =>
        {
            var map = NSP.Ui.ScheduleMapView.Instance;
            if (map == null || !sim.GetActiveEmployeeIds().All(id => IsPlaced(sim, id))) return null;
            return TutorialArrow.BesideOnMonitor(map, map.StartButtonRect);
        });
    }

    private static bool IsPlaced(FacilitySimulation sim, string employeeId) =>
        !string.IsNullOrEmpty(sim?.GetEmployeeState(employeeId)?.AssignedRoomId);

    // 근무 중 지도 — 한 명만 보내면 되는 방(정비실 · 저장고). 정해 둔 직원(preferredId) 아이콘 위에
    // 하나만 세운다. 그 직원을 옮길 수 없으면(이미 그 방 · 코어실 · 방금 다른 교육 방에 보낸 직원)
    // 코어실 밖의 다른 직원을 짚는다. 그 방에 누구든 놓는 순간 단계가 끝나 화살표가 걷힌다.
    private void PointAtOneMover(FacilitySimulation sim, string roomId, string preferredId, params string[] avoidRoomIds)
    {
        if (sim == null) return;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            string captured = id;
            TutorialArrow.PointAlso(MoverKey(captured), () =>
            {
                if (PickOneMover(sim, roomId, preferredId, avoidRoomIds) != captured) return null;
                var map = FacilityMonitorView.Instance?.Minimap;
                return map == null ? null
                    : TutorialArrow.BesideOnMonitor(map, map.EmployeeIconOf(captured), TutorialArrow.Side.Above);
            });
        }
    }

    private string PickOneMover(FacilitySimulation sim, string roomId, string preferredId, string[] avoidRoomIds)
    {
        if (AssignedCount(sim, roomId) > 0) return "";
        bool Free(string id, bool allowAvoided)
        {
            var st = sim.GetEmployeeState(id);
            if (st == null || !st.Alive || st.Incapacitated || st.Isolated) return false;
            if (string.IsNullOrEmpty(st.AssignedRoomId) || st.AssignedRoomId == roomId) return false;
            if (st.AssignedRoomId == AssignRoomId) return false;
            return allowAvoided || !avoidRoomIds.Contains(st.AssignedRoomId);
        }
        foreach (bool allowAvoided in new[] { false, true })
        {
            if (!string.IsNullOrEmpty(preferredId) && Free(preferredId, allowAvoided)) return preferredId;
            foreach (string id in sim.GetActiveEmployeeIds())
                if (Free(id, allowAvoided)) return id;
        }
        // 코어실 밖에 아무도 없으면 코어실에서 한 명(남는 사람이 있을 때만).
        return PickMovers(sim, roomId, 1).FirstOrDefault() ?? "";
    }

    // 근무 중 지도 — 그 작업실로 옮길 직원 아이콘 위에서 아래를 가리킨다(작업실 옆 화살표와 함께).
    // 누구를 가리킬지는 매 프레임 다시 고른다 — 한 명을 옮기면 그 화살표는 사라지고 남은 몫만 남는다.
    private void PointAtMovers(FacilitySimulation sim, string roomId, int need)
    {
        if (sim == null) return;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            string captured = id;
            TutorialArrow.PointAlso(MoverKey(captured), () =>
            {
                if (!PickMovers(sim, roomId, need).Contains(captured)) return null;
                var map = FacilityMonitorView.Instance?.Minimap;
                return map == null ? null
                    : TutorialArrow.BesideOnMonitor(map, map.EmployeeIconOf(captured), TutorialArrow.Side.Above);
            });
        }
    }

    // 그 작업실로 옮기라고 가리킬 직원들. 코어실 인원은 남겨 두고 나머지를 가리킨다.
    // 나머지로 모자라면 코어실에 한 명은 남긴 채 거기서 빼 오고, 그래도 모자라면(전원이 코어실)
    // 코어실 직원을 가리킨다. 코어실에서 빼 올 때는 토끼가 먼저다 — 휴게시간 교육의
    // "옮긴 기록" 이 토끼에게 남는다.
    private System.Collections.Generic.List<string> PickMovers(FacilitySimulation sim, string roomId, int need)
    {
        var picks = new System.Collections.Generic.List<string>();
        int missing = need - AssignedCount(sim, roomId);
        if (sim == null || missing <= 0) return picks;

        var core = new System.Collections.Generic.List<string>();
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null || !st.Alive || st.Incapacitated || st.Isolated) continue;
            if (string.IsNullOrEmpty(st.AssignedRoomId) || st.AssignedRoomId == roomId) continue;
            if (st.AssignedRoomId == AssignRoomId) core.Add(id);
            else picks.Add(id);
        }
        if (picks.Count >= missing) return picks;

        core.Sort((a, b) => (a == TutorialEmployeeId ? 0 : 1).CompareTo(b == TutorialEmployeeId ? 0 : 1));
        int fromCore = Mathf.Min(missing - picks.Count, Mathf.Max(core.Count - 1, 0));
        if (picks.Count + fromCore < missing) fromCore = Mathf.Min(missing - picks.Count, core.Count);
        picks.AddRange(core.Take(fromCore));
        return picks;
    }

    // 휴게시간 심문 — 자료를 아직 안 골랐으면 조사 노트 목록 위(아래를 가리킨다).
    // 고르면 대화창의 「[자료]로 묻는다.」 위, 그걸 눌러 질문 목록이 뜨면 첫 질문 위.
    private static TutorialArrow.Aim? AskEvidenceArrowAim()
    {
        var hud = PhoneCallHud.Instance;
        if (hud?.IsOpen != true) return null;
        var choice = hud.FirstIntentChoice ?? hud.EvidenceActionChoice;
        var onChoice = choice == null ? null
            : TutorialArrow.BesideOnScreen(choice, new Rect2(Vector2.Zero, choice.Size), TutorialArrow.Side.Above);
        if (onChoice != null) return onChoice;
        var console = RestInterviewConsole.Instance;
        if (console == null || !console.Visible) return null;
        return TutorialArrow.BesideOnMonitor(console, console.EvidenceListRect, TutorialArrow.Side.Above);
    }

    // 통화 끝내기 — 대화창 맨 아래 한 줄(「통화를 종료한다.」, 자료 질문 중이면 「…다른 질문을 한다.」)의
    // 왼쪽에서 가리킨다. 글자가 왼쪽 정렬이라 왼쪽에 서야 글자 바로 옆이다.
    private static TutorialArrow.Aim? EndCallArrowAim()
    {
        var hud = PhoneCallHud.Instance;
        if (hud?.IsOpen != true) return null;
        var tail = hud.TailChoice;
        return tail == null ? null
            : TutorialArrow.BesideOnScreen(tail, new Rect2(Vector2.Zero, tail.Size), TutorialArrow.Side.Left);
    }

    // 배치 화면 — 토끼 카드 옆 → 토끼를 누르거나 끌기 시작하면 코어실 옆.
    private TutorialArrow.Aim? AssignArrowAim()
    {
        var map = NSP.Ui.ScheduleMapView.Instance;
        if (map == null || RabbitInAssignRoom()) return null;
        bool held = map.HeldEmployeeId == TutorialEmployeeId;
        var rect = held ? map.CellOf(AssignRoomId) : map.RosterCardOf(TutorialEmployeeId);
        return TutorialArrow.BesideOnMonitor(map, rect);
    }

    // 근무 중 지도 — 그 작업실 상자 오른쪽에서 가리킨다.
    private static TutorialArrow.Aim? RoomArrowAim(string roomId)
    {
        var map = FacilityMonitorView.Instance?.Minimap;
        if (map == null) return null;
        return TutorialArrow.BesideOnMonitor(map, map.RoomBoxOf(roomId));
    }

    // 근무 종료 — 업무 창이 닫혀 있으면 오른쪽 아래 「T 업무」 버튼 위(아래를 가리킨다),
    // 열려 있으면 그 창의 「근무 종료」 버튼 왼쪽에서 가리킨다.
    private static TutorialArrow.Aim? EndShiftArrowAim()
    {
        var overlay = Day1HistoryOverlay.Instance;
        if (overlay == null) return null;
        if (overlay.IsObjectivesOpen && overlay.ObjectivesEndButton is { } end)
            return TutorialArrow.BesideOnScreen(end, new Rect2(Vector2.Zero, end.Size), TutorialArrow.Side.Left);
        var btn = overlay.ObjectivesButton;
        return btn == null ? null
            : TutorialArrow.BesideOnScreen(btn, new Rect2(Vector2.Zero, btn.Size), TutorialArrow.Side.Above);
    }

    // 대사 줄 연출(cue)마다 따로 세우는 곁 화살의 이름 — 앞 줄 화살이 다음 줄 화살을 지우지 않게.
    private static string CueArrowKey(string cue) => "cue:" + cue;

    // 스위치 기기의 그 레버 꼭대기 바로 위에서 아래를 가리킨다.
    private static TutorialArrow.Aim? LeverAim(PowerConsumer channel)
    {
        var top = PowerSwitchPanel.Instance?.LeverTopGlobal(channel);
        return top == null ? null : TutorialArrow.AtWorld(top.Value, TutorialArrow.Side.Above, gapPx: 2f);
    }

    // 「POWER n / m」 글자 왼쪽에서 그 글자를 가리킨다.
    private static TutorialArrow.Aim? PowerLabelAim()
    {
        var left = PowerSwitchPanel.Instance?.CapacityLabelLeftGlobal();
        return left == null ? null : TutorialArrow.AtWorld(left.Value, TutorialArrow.Side.Left);
    }

    // 근무 중 지도 — 그 복도 구간의 하늘색 네모 위에서 아래를 가리킨다.
    private static TutorialArrow.Aim? CorridorArrowAim(string segmentId)
    {
        var map = FacilityMonitorView.Instance?.Minimap;
        return map == null ? null
            : TutorialArrow.BesideOnMonitor(map, map.CorridorBoxOf(segmentId), TutorialArrow.Side.Above);
    }

    // 휴게시간 — 토끼를 아직 안 골랐으면 토끼 아이콘, 골랐으면 책상 위 전화기(위에서 아래로).
    private TutorialArrow.Aim? RestCallArrowAim()
    {
        if (PhoneCallHud.Instance?.IsOpen == true) return null;
        var rest = RestRosterView.Instance;
        if (rest == null) return null;
        if (rest.SelectedEmployeeId != TutorialEmployeeId)
        {
            var icon = rest.EmployeeIconOf(TutorialEmployeeId);
            if (icon == null) return null;
            var (side, rect) = ClearSideForRestIcon(rest, icon);
            return TutorialArrow.BesideOnMonitor(icon, rect, side);
        }
        var phone = Phone3D.Instance;
        if (phone == null) return null;
        return TutorialArrow.AtWorld(phone.GlobalPosition + Vector3.Up * PhoneArrowLift, TutorialArrow.Side.Above);
    }

    // 전화기 원점에서 화살촉까지 올리는 높이(m) — 수화기 위쪽에 닿지 않게.
    private const float PhoneArrowLift = 0.16f;

    // 휴게실 아이콘은 두 줄로 붙어 앉아 있어, 옆에 화살을 세우면 옆자리 직원 위에 걸치기 쉽다
    // (자리는 관계도로 판마다 바뀐다). 오른쪽 → 왼쪽 → 위 → 아래 순으로 다른 아이콘과 겹치지
    // 않는 쪽을 고른다. 돌려주는 사각형은 아이콘 로컬 좌표다(동그라미 또는 이름까지 포함).
    private (TutorialArrow.Side, Rect2) ClearSideForRestIcon(RestRosterView rest, Control icon)
    {
        var circle = rest.EmployeeIconCircleOf(TutorialEmployeeId);
        if (circle.Size == Vector2.Zero) circle = new Rect2(Vector2.Zero, new Vector2(icon.Size.X, icon.Size.X));
        var whole = new Rect2(Vector2.Zero, icon.Size);
        const float Len = 100f, Half = 16f;   // 화살 길이(촉 + 몸통 + 둥둥 폭)를 화면 논리 단위로
        Vector2 c = circle.GetCenter();
        // 이름이 붙은 쪽(뒷줄 = 위, 앞줄 = 아래)으로 세울 때는 이름까지 넘어서 선다.
        bool nameAbove = circle.Position.Y > 0.5f;
        var top = nameAbove ? whole : circle;
        var bottom = nameAbove ? circle : whole;
        var options = new (TutorialArrow.Side Side, Rect2 Target, Rect2 Swept)[]
        {
            (TutorialArrow.Side.Right, circle, new Rect2(circle.End.X, c.Y - Half, Len, Half * 2f)),
            (TutorialArrow.Side.Left, circle, new Rect2(circle.Position.X - Len, c.Y - Half, Len, Half * 2f)),
            (TutorialArrow.Side.Above, top, new Rect2(c.X - Half, top.Position.Y - Len, Half * 2f, Len)),
            (TutorialArrow.Side.Below, bottom, new Rect2(c.X - Half, bottom.End.Y, Half * 2f, Len)),
        };
        var sim = FacilitySimulation.Instance;
        var others = new System.Collections.Generic.List<Rect2>();
        if (sim != null)
            foreach (string id in sim.GetActiveEmployeeIds())
            {
                if (id == TutorialEmployeeId) continue;
                if (rest.EmployeeIconOf(id) is { } o && o.GetParent() == icon.GetParent())
                    others.Add(new Rect2(o.Position, o.Size));
            }
        foreach (var opt in options)
        {
            var swept = new Rect2(icon.Position + opt.Swept.Position, opt.Swept.Size);
            if (!others.Any(r => r.Intersects(swept))) return (opt.Side, opt.Target);
        }
        return (TutorialArrow.Side.Right, circle);
    }

    // --- await 헬퍼 -------------------------------------------------------

    // DAY0 교육에는 직원 스탠딩 컷인이 없다. 가르치는 자리에 직원들 대화가 끼면
    // 지금 무엇을 해야 하는지가 묻힌다 — 안내는 GUIDE-0 의 자막 띠 하나로만 간다.
    // (대본 @beat day0_* 는 NSP_PROLOGUE_RUNTIME.md 에 그대로 남아 있고, 부르지 않을 뿐이다.)
    //
    // 대본에서 묶음을 통째로 지워도 멈추지 않는다 — 없는 묶음은 GuideHologramView 가 곧바로
    // 완료로 돌려준다(경고 로그만 남는다). 이어지는 조건 대기는 그대로 돈다.
    private Task Say(string guideId, System.Collections.Generic.Dictionary<string, string> replacements = null,
        bool completeWhenTyped = false)
    {
        ThrowIfAborted();
        // 교육 중에는 어떤 화면도 빼앗지 않는다. 지시는 화면 아래 자막 띠가 전하고,
        // GUIDE-0 의 얼굴은 CCTV 화면 오른쪽 아래 구석의 작은 창으로만 뜬다.
        GuideCornerFace.ShowAll(true);
        // 앞 묶음이 아직 떠 있으면(다 찍히고 기다리던 지시 줄) 그것부터 닫는다.
        if (_guide.IsGuideActive) _guide.FinishGuideNow();
        return PrologueDirector.ShowGuide(_guide, guideId, replacements, completeWhenTyped);
    }

    // 다 찍히는 순간 끝난다 — 클릭을 기다리지 않는다. stopEarly 가 참이 되면 그 자리에서 닫는다.
    private async Task SayTyped(string guideId,
        System.Collections.Generic.Dictionary<string, string> replacements = null, Func<bool> stopEarly = null)
    {
        var say = Say(guideId, replacements, completeWhenTyped: true);
        while (IsRunning && IsInstanceValid(this) && !say.IsCompleted && stopEarly?.Invoke() != true)
            await NextFrame();
        if (!say.IsCompleted) _guide?.FinishGuideNow();
        ThrowIfAborted();
    }

    // 지시 — 마지막 줄이 다 찍히면 그 줄을 지시문으로 남긴 채 done 이 참이 될 때까지 기다린다.
    // 앞 줄들(설명)은 평소처럼 클릭으로 넘긴다. 플레이어가 먼저 해 버리면 남은 안내는 닫는다.
    // beforeWait — 지시 줄까지 다 뜬 뒤 기다리기 시작하는 순간 한 번(화살표 · 개체 출현의 대비책).
    private async Task SayAct(string guideId, Func<bool> done,
        System.Collections.Generic.Dictionary<string, string> replacements = null, Action beforeWait = null)
    {
        var say = Say(guideId, replacements, completeWhenTyped: true);
        while (IsRunning && IsInstanceValid(this) && !done() && !say.IsCompleted)
            await NextFrame();
        // 행동이 먼저였다면 남은 안내는 마지막 줄만 남기고 여기서 닫는다.
        if (!say.IsCompleted) _guide?.FinishGuideNow();
        ThrowIfAborted();
        if (!done())
        {
            // 대본에 cue: arrow 가 없었으면 여기서 띄운다.
            ShowArmedArrow();
            beforeWait?.Invoke();
        }
        await Until(done);
    }

    private async Task NextFrame()
    {
        ThrowIfAborted();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ThrowIfAborted();
    }

    private async Task Wait(double seconds)
    {
        ThrowIfAborted();
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        ThrowIfAborted();
    }

    // 조건이 참이 되거나 제한 시간이 지날 때까지. 상황이 저절로 오기를 기다리되,
    // 오지 않아도 교육이 멈춰 서지 않게 하는 데 쓴다(부르는 쪽이 직접 상황을 만든다).
    private async Task UntilOrAfter(Func<bool> condition, double seconds)
    {
        double left = seconds;
        while (IsRunning && IsInstanceValid(this) && !condition() && left > 0.0)
        {
            left -= GetProcessDeltaTime();
            await NextFrame();
        }
        ThrowIfAborted();
    }

    // 조건이 참이 될 때까지 매 프레임 확인한다. 씬이 사라지거나 교육이 중단되면 빠져나온다.
    private async Task Until(Func<bool> condition)
    {
        while (IsRunning && IsInstanceValid(this) && !condition())
            await NextFrame();
        ThrowIfAborted();
    }
}
