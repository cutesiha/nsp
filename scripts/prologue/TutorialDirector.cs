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
    [Export] public float SituationWaitSeconds = 12f;
    // 교육용 사고가 나는 작업실. 배치한 방과 달라야 토끼가 실제로 옮겨 간 기록이 남고,
    // 그 기록이 STEP 6 의 모순 추리 재료가 된다.
    // 발전실은 수리에 두 명이 필요한 방이라 "한 명을 더 보내야 고쳐진다"가 자연스럽다.
    [Export] public string AccidentRoomId = "power_room";
    // 교육용 이상 개체가 나타나는 작업실. 사고가 난 방(발전실)과 달라야 한다 —
    // 같은 방이면 "고장 난 곳만 보면 된다"로 배우게 된다.
    // DAY0 에 처음부터 열려 있는 작업실은 코어실 · 경비실 · 발전실 셋이다(RoomDef.UnlockDay).
    [Export] public string AnomalyRoomId = "guard_room";
    // 근무 시작 후 교육용 사고가 나기까지의 시간.
    [Export] public float IncidentDelaySeconds = 8f;

    public bool IsRunning { get; private set; }

    private ShiftFlowController _flow;
    private GuideHologramView _guide;

    // STEP 9 의 모순을 만들기 위해 STEP 5 에서 토끼가 "원래 있던" 작업실을 기억해 둔다.
    private string _rabbitOriginRoomName = "";
    // 플레이어가 토끼에게 '조사 자료로' 질문했는가(이동 기록이면 더 좋다).
    private bool _rabbitAskedWithEvidence;
    private bool _rabbitAskedAnything;
    private bool _rabbitAnsweredWhere;

    public override void _Ready()
    {
        Instance = this;
        _flow = GetNodeOrNull<ShiftFlowController>(ShiftFlowPath);
    }

    public override void _ExitTree()
    {
        // 교육 도중 씬이 내려가도(개발 허브 복귀 등) 정적 후크가 다음 씬에 남지 않게 한다.
        IsRunning = false;
        InterviewSession.Asked -= OnInterviewAsked;
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

        // STEP 9 의 교육용 진술. 실제 로그와 어긋나는 문장을 이 후크로만 돌려준다.
        LocalDialogueGenerator.ScriptedAnswerOverride = ScriptedAnswer;
        // 심문에서 무엇을 물었는지 듣는다 — 진행 조건 판정에만 쓴다.
        InterviewSession.Asked += OnInterviewAsked;
        if (PhoneCallHud.Instance != null)
            PhoneCallHud.Instance.EventChoiceMade += OnTutorialCallChoice;

        // ── STEP 1 : 직원 확인 ────────────────────────────────────────
        TutorialTelemetry.Step("1 직원 확인");
        await Say("tut_intro");
        await Say("tut_mood");

        // ── STEP 2 : 배치 ────────────────────────────────────────────
        TutorialTelemetry.Step("2 첫 배치");
        await Say("tut_assign", RoomVars(AssignRoomId));
        await WaitForTutorialAssign(sim);
        Sfx.Instance?.Play("assign", -6f);
        // 배치가 맞았다. 그 방 카드의 "지금 무엇을 만들고 있는가" 줄을 한 번 짚어 준다.
        NSP.View.FacilityMonitorView.HighlightRoomNumber(AssignRoomId);
        await Say("tut_assign_rest");
        await Until(() => GameState.Instance?.CurrentPhase == GamePhase.Live);

        // 근무 화면 전환은 ShiftFlowController.EnterShift 가 이미 끝냈다(왼쪽=시설 / 오른쪽=GUIDE-0).
        await Say("tut_shift_start");

        // ── STEP 3 : 자재 · 정비실 ───────────────────────────────────
        TutorialTelemetry.Step("3 자재·정비실");
        await RunMaterialsLesson(sim);

        // ── STEP 4 : 보관 한도 · 저장고 ──────────────────────────────
        TutorialTelemetry.Step("4 저장고");
        await RunStorageLesson(sim);

        // ── STEP 5 : 사고 ────────────────────────────────────────────
        TutorialTelemetry.Step("5 사고·재배치");
        await Wait(IncidentDelaySeconds);
        // STEP 9 의 모순을 만들려면 토끼가 실제로 방을 옮긴 기록이 남아야 한다. 그래서 수리
        // 최소 인원을 "지금 그 방에 있는 인원 + 1" 로 잡아, 한 명을 더 보내야만 고쳐지게 한다.
        string originRoomId = sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId ?? "";
        // **최소 두 명**이어야 한다. 한 명으로 고쳐지면 "혼자 보냈는데 왜 게이지가 안 차지"
        // 를 배울 자리가 없고, 승인 요청이 사람을 보내자마자 떠 버린다. 이미 둘 이상이
        // 있는 방이면 거기에 한 명을 더 — 토끼가 실제로 옮겨 간 기록이 남아야
        // STEP 9 의 모순 추리 재료가 된다.
        int need = Mathf.Max(2, (sim?.OnDutyCount(AccidentRoomId) ?? 0) + 1);
        // 사고와 함께 승인 요청이 줄에 선다. 다만 **띄우지는 않는다** —
        // 아직 아무도 보내지 않았는데 "수리를 승인하시겠습니까" 부터 뜨면 순서가 거꾸로다.
        // 토끼가 그 방에 도착한 뒤에 풀어 준다(RunApprovalLesson).
        RepairApprovalSystem.Held = true;
        RepairApprovalSystem.Paused = true;
        sim?.TriggerTutorialAccident(AccidentRoomId, need);
        await Say("tut_incident");
        // 발전실 사고는 전력 용량을 깎는다(RoomDef.AccidentConsequence = PowerCapacityLoss).
        // 그러면 조명 · CCTV · 패드 중 하나가 **저절로 꺼진다** — 그 일이 실제로 일어났을
        // 때만 설명한다(문서 §16). 사고 방을 바꿔 전력이 멀쩡하면 이 줄은 뜨지 않는다.
        if (GameState.Instance is { } gsPower
            && gsPower.PowerCapacity < (Config.Instance?.Data?.PowerCapacityMax ?? 3))
            await Say("tut_power_short");
        await Say("tut_relocate", new System.Collections.Generic.Dictionary<string, string>
        {
            ["ROOM"] = RoomName(AccidentRoomId),
        });
        // 플레이어가 토끼가 아닌 다른 직원을 보내 수리를 끝내 버릴 수도 있다 —
        // 그 경우에도 교육이 멈추지 않도록 "토끼가 왔거나, 어쨌든 수리가 끝났거나" 로 기다린다.
        await Until(() => sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId == AccidentRoomId
                          || sim?.HasRepairPending(AccidentRoomId) == false);
        if (sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId == AccidentRoomId)
            _rabbitOriginRoomName = RoomName(originRoomId);

        // ── STEP 5-A : 수리 승인 요청 · 미로 ──────────────────────────
        // 사람을 보낸 직후, 수리가 끝나기 전에 가르친다 — 실제 근무에서 겪는 순서 그대로다.
        await RunApprovalLesson();

        await Until(() => sim?.HasRepairPending(AccidentRoomId) == false);
        await Say("tut_repair_done");
        // 실제 근무의 방해공작을 설명만 한다 — DAY0 에는 방해자를 만들지 않는다.
        await Say("tut_sabotage_intro");

        // ── STEP 6 : 이상 개체 (CCTV) ──────────────────────────────────────
        TutorialTelemetry.Step("6 이상 개체");
        // 설명만 하고 넘어가면 근무 중에 화면을 돌려 볼 이유가 생기지 않는다.
        // 그래서 한 번 실제로 나타나게 하고, 직접 찾아 직접 지켜보게 한다.
        await RunAnomalyLesson(sim);

        // ── STEP 7 : 기절 · 의무실 이송 ───────────────────────────────
        TutorialTelemetry.Step("7 기절·의무실");
        await RunFaintLesson(sim);

        // ── STEP 8 : 복귀 전화 ──────────────────────────────────────
        TutorialTelemetry.Step("8 복귀 전화");
        // 교육의 두 번째 통화. 거는 사람은 방금 수리를 끝내고 동료를 의무실에 눕히고 온 토끼다.
        // "수화기를 든다" 는 조작은 STEP 3 의 첫 통화에서 이미 배웠으므로 다시 말하지 않는다.
        // 여기서 고른 선택지가 토끼를 실제로 움직이고, 그 이동 기록이 STEP 9 의 모순 재료가 된다.
        RingTutorialCall(TutorialEmployeeId, DialogueRepository.EventTutorialRepairDone, AccidentRoomId);
        await Wait(TutorialCallRingLeadSeconds);
        await WaitForIncomingCallAnswered(TutorialEmployeeId,
            DialogueRepository.EventTutorialRepairDone, AccidentRoomId);
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);
        await Say("tut_call_done");

        // ── STEP 9 : 휴게시간 · 기록 비교 ────────────────────────────────────────
        TutorialTelemetry.Step("9 휴게시간 심문");
        await Say("tut_endshift");
        await Until(() => GameState.Instance?.CurrentPhase == GamePhase.Rest);
        // 교육이 끝나기 전에는 다음 날로 넘어갈 수 없다.
        RestRosterView.Instance?.SetNextEnabled(false, "교육 진행 중...");
        await Say("tut_rest");
        await Until(() => PhoneCallHud.Instance?.IsOpen == true
                          && PhoneCallHud.Instance.CurrentEmployeeId == TutorialEmployeeId);
        // 심문 대화창이 화면 아래 절반을 차지한다(일반 통화와 같은 자리) —
        // 지시문도 얼굴창도 위로 올라가야 가리지 않는다. 대화창을 접어도 이 자리는 그대로 둔다.
        GuideSubtitleHud.Instance?.SetTopAligned(true);
        GuideCornerFace.SetLifted(true);

        // 이동 기록으로 물으면 통과. 옮긴 기록이 아예 없는 판이면 아무 질문이나 하면 된다.
        await SayThen("tut_ask_where", () => _rabbitAskedWithEvidence
                          || (string.IsNullOrEmpty(_rabbitOriginRoomName) && _rabbitAskedAnything));
        // 토끼가 실제로 방을 옮긴 기록이 있을 때만 "기록과 진술이 다르다"고 말한다.
        // (플레이어가 지시와 다르게 움직여 재배치 기록이 없으면 그 단계는 건너뛴다.)
        if (!string.IsNullOrEmpty(_rabbitOriginRoomName))
            await SayThen("tut_contradiction", () => Day1HistoryOverlay.Instance?.IsLogOpen == true);
        // D 를 눌러 대화 기록을 **실제로 연 뒤에만** "통화를 종료하라"는 다음 줄이 뜬다.
        await SayThen("tut_dialogue_log", () => Day1HistoryOverlay.Instance?.IsDialogueOpen == true);
        await SayThen("tut_end_call", () => PhoneCallHud.Instance?.IsOpen != true);

        // ── STEP 10 : 교육 종료 ────────────────────────────────────────
        TutorialTelemetry.Step("10 마무리");
        // 대화 기록까지 확인한 뒤 통화를 끊어야 교육이 끝난다.
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);
        GuideSubtitleHud.Instance?.SetTopAligned(false);   // 마무리 대사는 원래 자리로
        GuideCornerFace.SetLifted(false);
        // 마무리 멘트는 제어실 전체가 보이는 자리에서 한다. 모니터를 확대해 둔 채로 끝나면
        // 화면 하나만 꽉 찬 상태에서 교육이 닫혀, 이어지는 전환이 보이지 않는다.
        ControlRoom3DController.Instance?.ClearFocus(0.45f);
        await Wait(0.5);
        // 여기서부터 DAY1 배치 화면이 켜질 때까지 책상 위 기기를 잠근다 — 마무리 안내가 흐르는 동안
        // 전화기가 눌려 통화가 열린 채로 DAY1 에 들어가는 일이 있었다. 배치 단계(EnterSchedule)가 푼다.
        ControlRoom3DController.Instance?.SetInputLocked(true);
        await Say("tut_complete");
        await Wait(0.6);

        _reachedEnd = true;
        Finish();
        _flow?.AdvanceFromTutorial();
    }

    // 토끼가 정비실에 놓일 때까지 기다린다. 엉뚱하게 놓으면 무엇이 틀렸는지 짚어 준다.
    // 같은 잘못을 반복해도 잔소리가 쌓이지 않게, 상태가 바뀔 때만 한 번씩 말한다.
    private async Task WaitForTutorialAssign(FacilitySimulation sim)
    {
        string said = "";
        while (IsRunning)
        {
            string rabbitRoom = sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId ?? "";
            if (rabbitRoom == AssignRoomId) return;

            // 정비실에 엉뚱한 직원이 들어갔는가 / 토끼가 엉뚱한 방에 갔는가.
            bool intruder = !string.IsNullOrEmpty(OtherAssignedTo(sim, AssignRoomId));
            string want = intruder ? "tut_assign_wrong_person"
                : !string.IsNullOrEmpty(rabbitRoom) ? "tut_assign_wrong_room"
                : "";

            if (want.Length == 0) { said = ""; await NextFrame(); continue; }
            if (want == said) { await NextFrame(); continue; }

            said = want;
            TutorialTelemetry.Count("배치 실수");
            await Say(want, RoomVars(AssignRoomId));
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

    // 안내가 뜨기 전에 벨을 먼저 울리는 시간.
    private const double TutorialCallRingLeadSeconds = 1.3;

    private void RingTutorialCall(string caller, string dialogueEvent, string roomId)
    {
        if (string.IsNullOrEmpty(caller)) return;
        if (Phone3D.Instance is { IsBusy: false })
            Phone3D.Instance.RingIncoming(caller, dialogueEvent, roomId);
    }

    // "그렇게 하십시오" 를 고르면 토끼는 말한 대로 정비실로 돌아간다.
    // (말만 하고 실제로는 그대로 서 있으면 다음 단계의 기록 비교가 거짓말이 된다.)
    private void OnTutorialCallChoice(string employeeId, string dialogueEvent, int choiceIndex)
    {
        if (dialogueEvent != DialogueRepository.EventTutorialRepairDone) return;
        if (employeeId != TutorialEmployeeId || choiceIndex != 0) return;
        FacilitySimulation.Instance?.AssignToRoom(TutorialEmployeeId, AssignRoomId);
    }

    // 못 받으면(직원이 끊으면) 다시 건다 — 교육이 멈추지 않게.
    private async Task WaitForIncomingCallAnswered(string caller, string dialogueEvent, string roomId)
    {
        if (string.IsNullOrEmpty(caller)) return;

        bool firstRing = true;
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

    // 심문에서 플레이어가 던진 질문. 교육 진행 조건만 본다(대사에는 관여하지 않는다).
    private void OnInterviewAsked(string employeeId, InterviewQuestion q)
    {
        if (employeeId != TutorialEmployeeId || q == null) return;
        _rabbitAskedAnything = true;
        // 조사 자료를 근거로 던진 질문이어야 "기록으로 물었다"고 본다.
        if (!string.IsNullOrEmpty(q.EvidenceId)) _rabbitAskedWithEvidence = true;
    }

    // STEP 9 교육용 고정 진술. 토끼의 "사고 당시 어디에 있었나" 답변만 가로채고,
    // 나머지 직원·질문은 평소대로 로그 기반 대사를 쓴다.
    private string ScriptedAnswer(string employeeId, string questionId)
    {
        if (employeeId != TutorialEmployeeId) return null;

        // "그러고 어딜 갔습니까?" — 교육에서 토끼는 발전실 수리 뒤 **기절한 동료를 의무실로
        // 옮겼다.** 그 사실을 빼고 답하면 바로 앞에서 본 이송과 어긋나, 플레이어가 배운
        // 모순 찾기가 거꾸로 이 대사를 가리키게 된다.
        if (_carriedToMedical
            && questionId == DialogueQuestions.FollowUpPrefix + NSP.Dialogue.FollowUpIntent.AskNextAction)
        {
            string after = PrologueScript.GetScripted("tut_rabbit_then");
            if (!string.IsNullOrEmpty(after)) return after;
        }

        if (questionId != DialogueQuestions.Where) return null;
        _rabbitAnsweredWhere = true;
        // 옮긴 기록이 없으면 지어낼 모순도 없다 — 평소대로 로그 기반 대사를 쓰게 둔다.
        if (string.IsNullOrEmpty(_rabbitOriginRoomName)) return null;
        string text = PrologueScript.GetScripted("tut_rabbit_where");
        return string.IsNullOrEmpty(text) ? null : text.Replace("{FROM_ROOM}", _rabbitOriginRoomName);
    }

    // 교육에서 토끼가 실제로 기절한 동료를 의무실로 옮겼는가(심문 답변이 이 사실을 쓴다).
    private bool _carriedToMedical;

    // 이상 개체가 나타난 방에 혼자 있던 직원 — 바로 다음 단계에서 이 사람이 쓰러진다.
    private string _anomalyVictim = "";

    // ── 수리 승인 · 미로 교육 ─────────────────────────────────────────
    //
    // 승인 요청은 사고와 함께 이미 떠 있다(제한 시간은 Paused 로 멈춰 있다).
    // 안내를 한 줄 읽히고 나서야 시계가 돌기 시작한다 — 설명을 읽는 동안 제한 시간이
    // 지나가 버리면 "배우는 중에 벌점"이 된다.
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
            await Say("tut_approval");
            // 여기서부터 응답 제한 시간이 흐른다.
            RepairApprovalSystem.Paused = false;
            // [예] 를 누르면 미로로 넘어간다. 거절하거나 시간을 넘겨도 교육은 이어진다.
            await Until(() => RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking);

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
        }

        // 패드를 손에 든 채로 다음 안내가 흐르면 화면이 패드에 가린다 — 내려놓을 때까지 기다린다.
        if (AdminPad3D.Instance?.IsOpen == true)
        {
            PadHintBubble.Show("패드를 내려놓으십시오. (Tab · 또는 패드 바깥을 클릭)", 3f);
            await Until(() => AdminPad3D.Instance?.IsOpen != true);
        }
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
    //   ⑤ 정비실이 열린다 → 플레이어가 직접 배치한다 → 자재가 늘고 코어가 다시 돈다
    //
    // 교육 시뮬레이션은 보관 한도를 작게 잡아 두었으므로(TutorialMaterialsCap) ② 는
    // 대개 저절로 온다. 그래도 오지 않으면(코어실에 아무도 없는 등) 기다리다 직접 비운다 —
    // 교육이 여기서 영영 멈춰 서면 안 된다.
    private async Task RunMaterialsLesson(FacilitySimulation sim)
    {
        if (sim == null) return;
        var gs = GameState.Instance;
        if (gs == null) return;

        int cost = Mathf.Max(1, Config.Instance?.Data?.MaterialsPerCoreGauge ?? 2);
        await UntilOrAfter(() => gs.Materials < cost, SituationWaitSeconds);
        if (gs.Materials >= cost) gs.AddMaterials(-gs.Materials);

        // 전화를 거는 사람은 그 멈춘 자리에 있는 직원이다(없으면 교육 대상인 토끼).
        string caller = FirstOnDuty(sim, AssignRoomId);
        RingTutorialCall(caller, DialogueRepository.EventTutorialMaterialShort, AssignRoomId);
        await Wait(TutorialCallRingLeadSeconds);
        // 전화를 받는 조작은 여기서 처음 배운다.
        await SayThen("tut_call", () => PhoneCallHud.Instance?.IsOpen == true);
        await WaitForIncomingCallAnswered(caller, DialogueRepository.EventTutorialMaterialShort, AssignRoomId);
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);

        // 이제야 설명한다. 그리고 그 방을 **지금** 연다 — 말과 화면이 같은 순간에 바뀐다.
        sim.OpenRoomMidShift(MaterialsRoomId);
        NSP.View.FacilityMonitorView.HighlightRoomNumber(MaterialsRoomId);
        // "자재가 필요합니다" 를 말하는 동안 화면 위 자재 숫자를 하늘색 테두리로 가리킨다.
        // 둘째 줄("정비실을 열었습니다")이 뜨면 꺼진다 — 그때부터 봐야 할 곳은 작업실이다.
        HintMaterialsFor(2);
        await Say("tut_materials");

        // 열어 줬다고 끝이 아니다 — **어떻게** 배치하는지를 말해 준다.
        // 드래그로 옮기는 조작이라는 것을 모른 채 멈춰 서 있던 자리다.
        await SayThen("tut_room_assign", () => AnyoneAssignedTo(sim, MaterialsRoomId));

        // 사람이 들어가 자재가 실제로 들어올 때까지. 플레이어가 다른 방법으로
        // 자재를 채웠더라도(그럴 일은 없지만) 숫자가 오르면 통과시킨다.
        await Until(() => gs.Materials >= cost);
        await Say("tut_materials_done");
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
        await Say("tut_storage");
        await SayThen("tut_room_assign", () => AnyoneAssignedTo(sim, StorageRoomId));

        // 재고 정리는 한 바퀴가 길다(inventory_sorting). 한도가 실제로 오를 때까지
        // 기다리면 교육이 1분 가까이 멈춰 선다 — 사람을 넣은 것까지만 확인한다.
        await Until(() => sim.OnDutyCount(StorageRoomId) > 0);
        await Say("tut_storage_done");
    }

    // 그 방에서 지금 근무 중인 첫 직원. 아무도 없으면 교육 대상(토끼)이 건다.
    private string FirstOnDuty(FacilitySimulation sim, string roomId)
    {
        var ids = sim?.OnDutyEmployeeIds(roomId);
        return ids is { Count: > 0 } ? ids[0] : TutorialEmployeeId;
    }

    // ── 기절 · 의무실 이송 교육 ───────────────────────────────────────
    //
    // 교육일에는 스트레스가 잠겨 있어 저절로 쓰러지는 일이 없다. 그래서 한 명을 직접
    // 쓰러뜨리고, **실제 구조 절차(FaintRescueSystem)를 그대로** 밟게 한다.
    // 의무실은 DAY1 에 열리는 방이라 이 교육 동안만 연다.
    private async Task RunFaintLesson(FacilitySimulation sim)
    {
        if (sim == null) return;

        // 방금 개체가 나타난 방에 혼자 있던 그 직원이 쓰러진다(RunAnomalyLesson 이 골라 둔다).
        // 그 사이에 플레이어가 그 사람을 옮겼으면 조건이 깨지므로 다시 고른다.
        string victim = Eligible(sim, _anomalyVictim) ? _anomalyVictim : PickFaintVictim(sim);
        if (string.IsNullOrEmpty(victim)) return;

        DayFeatures.ForceRoomOpen(FacilitySimulation.MedicalRoomIdPublic, true);
        if (!sim.TriggerTutorialFaint(victim))
        {
            DayFeatures.ForceRoomOpen(FacilitySimulation.MedicalRoomIdPublic, false);
            return;
        }

        var st = sim.GetEmployeeState(victim);
        // ① 지도에서 그 아이콘을 직접 눌러 보게 한다.
        // 아이콘을 누르지 않고 곧바로 사람을 보내 버려도 교육이 멈추지 않게, 구조가 시작되면 넘어간다.
        await SayThen("tut_faint", () => NSP.View.FacilityMonitorView.Instance?.SelectedEmployeeId == victim
                                         || st.Faint != FaintPhase.OnFloor);
        // ② 다른 직원을 그 방으로 보내면 구조가 시작된다(WasDispatchedForRescue → 전화 없이 바로 이송).
        await SayThen("tut_faint_carry",
            () => st.Faint is FaintPhase.TransportPickup or FaintPhase.Transporting or FaintPhase.BedApproach
                            or FaintPhase.InMedicalBed or FaintPhase.Recovering);
        // ③ 침대에 눕을 때까지. 깨어나는 것은 기다리지 않는다 —
        //    회복에는 실제 근무와 같은 시간(StressFaintRecoverySeconds)이 걸리고,
        //    그동안 교육이 멈춰 서 있을 이유가 없다.
        await Until(() => st.Faint is FaintPhase.Recovering or FaintPhase.None);
        // 심문 단계에서 토끼가 이 일을 말한다(ScriptedAnswer) — 실제로 옮겼을 때만.
        _carriedToMedical = sim.GetEmployeeState(TutorialEmployeeId)?.CurrentRoomId
                            == FacilitySimulation.MedicalRoomIdPublic
                            || st.TransporterId == TutorialEmployeeId;
        await Say("tut_faint_done");
        // ④ 옮긴 직원이 제자리로 돌아올 때까지 기다렸다가 다음 단계로.
        await Until(() => sim.GetActiveEmployeeIds()
            .All(id => string.IsNullOrEmpty(sim.GetEmployeeState(id)?.CarryingVictimId)));
    }

    // 쓰러질 직원. 교육 대사가 이름을 부르므로(tut_faint_carry · 전화) 고양이로 고정한다.
    // 고양이를 쓸 수 없는 상황이면 혼자 근무 중인 다른 직원으로 떨어진다.
    [Export] public string FaintEmployeeId = "cat";

    private string PickFaintVictim(FacilitySimulation sim)
    {
        if (Eligible(sim, FaintEmployeeId)) return FaintEmployeeId;
        foreach (string id in sim.GetActiveEmployeeIds())
            if (Eligible(sim, id)) return id;
        return "";
    }

    // 혼자 근무 중이어야 한다 — 같은 방 동료가 있으면 알아서 구조에 들어가,
    // "다른 직원을 불러온다" 를 배울 자리가 없어진다. 토끼는 뒤 단계에서 쓰므로 제외한다.
    private bool Eligible(FacilitySimulation sim, string id)
    {
        if (string.IsNullOrEmpty(id) || id == TutorialEmployeeId) return false;
        var st = sim.GetEmployeeState(id);
        if (st == null || !st.Alive || st.Incapacitated || st.Isolated || st.IsMoving) return false;
        if (string.IsNullOrEmpty(st.CurrentRoomId)) return false;
        if (sim.OnDutyCount(st.CurrentRoomId) != 1) return false;
        // 수리가 걸린 방은 피한다 — 거기서 쓰러지면 수리까지 같이 멈춘다.
        return !sim.HasRepairPending(st.CurrentRoomId);
    }

    private void Finish()
    {
        // 블라인드 테스트 기록은 교육이 어떻게 끝났든 한 번만 남는다(중단 포함).
        TutorialTelemetry.End(_reachedEnd);
        IsRunning = false;
        _materialsHintLines = 0;
        NSP.View.FacilityMonitorView.HighlightMaterials(false);
        RepairApprovalSystem.Paused = false;
        RepairApprovalSystem.Held = false;
        DayFeatures.ForceRoomOpen(FacilitySimulation.MedicalRoomIdPublic, false);
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
        if (PhoneCallHud.Instance != null)
            PhoneCallHud.Instance.EventChoiceMade -= OnTutorialCallChoice;
        if (_guide != null) _guide.LineShown -= OnGuideLine;
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
        NSP.View.FacilityMonitorView.HighlightMaterials(true);
    }

    private void OnGuideLine(string text)
    {
        GuideSubtitleHud.Instance?.SetLine(text);
        if (_materialsHintLines > 0 && --_materialsHintLines == 0)
            NSP.View.FacilityMonitorView.HighlightMaterials(false);
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

    // ── 이상 개체 교육 ───────────────────────────────────────────────
    //
    // 순서가 중요하다. 설명(tut_anomaly_intro)을 먼저 듣고, "찾아보라"(tut_anomaly_find)고
    // 말하는 **그 순간에** 나타난다. 예전에는 설명보다 먼저 불러 두었는데, 관리자가 마침
    // 코어실 CCTV 를 보고 있으면 설명을 듣는 동안 관측 게이지가 차서 개체가 소리 없이
    // 소멸해 버렸다 — 찾으라는 말도 듣기 전에 끝나 있었다.
    //
    // 교육 중에는 사고로 번지지 않는다(유예 9999초). 헤매도 벌은 없고, 배우기만 하면 된다.
    private async Task RunAnomalyLesson(FacilitySimulation sim)
    {
        if (sim?.Ghost == null) return;

        // 개체는 **혼자 근무 중인 직원의 방**에 나타난다. 그래야 바로 다음 단계에서
        // 그 사람이 쓰러지는 것이 "개체를 혼자 마주한 결과" 로 읽힌다 — 두 사건을
        // 따로 보여 주면 CCTV 를 지켜볼 이유가 설명으로만 남는다.
        _anomalyVictim = PickFaintVictim(sim);
        string room = string.IsNullOrEmpty(_anomalyVictim) ? AnomalyRoomId
            : sim.GetEmployeeState(_anomalyVictim)?.CurrentRoomId ?? AnomalyRoomId;

        await Say("tut_anomaly_intro");
        if (!sim.Ghost.ForceAppear(room, sim, 9999f)) return;
        // 찾을 때까지 — 그 방을 CCTV 로 띄우는 순간이 "찾았다" 다.
        await SayThen("tut_anomaly_find", () => sim.SurveillanceTargetRoomId == room
                                                || !sim.Ghost.Active);
        if (!sim.Ghost.Active) return;

        // 지켜보는 동안 — 화면을 돌리면 게이지가 되감기는 것도 여기서 배운다.
        await SayThen("tut_anomaly_watch", () => !sim.Ghost.Active);
        await Say("tut_anomaly_done");
    }

    // --- await 헬퍼 -------------------------------------------------------

    // DAY0 교육에는 직원 스탠딩 컷인이 없다. 가르치는 자리에 직원들 대화가 끼면
    // 지금 무엇을 해야 하는지가 묻힌다 — 안내는 GUIDE-0 의 자막 띠 하나로만 간다.
    // (대본 @beat day0_* 는 NSP_PROLOGUE_RUNTIME.md 에 그대로 남아 있고, 부르지 않을 뿐이다.)
    private Task Say(string guideId, System.Collections.Generic.Dictionary<string, string> replacements = null)
    {
        ThrowIfAborted();
        // 교육 중에는 어떤 화면도 빼앗지 않는다. 지시는 화면 아래 자막 띠가 전하고,
        // GUIDE-0 의 얼굴은 CCTV 화면 오른쪽 아래 구석의 작은 창으로만 뜬다.
        GuideCornerFace.ShowAll(true);
        return PrologueDirector.ShowGuide(_guide, guideId, replacements);
    }

    // 안내를 다 읽기 전에 플레이어가 먼저 행동해도 그대로 다음으로 넘어간다.
    // ("L키를 눌러 보십시오" 라고 해 놓고 정작 그 안내 중의 L키를 무시하면 안 된다.)
    private async Task SayThen(string guideId, Func<bool> done,
        System.Collections.Generic.Dictionary<string, string> replacements = null)
    {
        var say = Say(guideId, replacements);
        while (IsRunning && IsInstanceValid(this) && !done() && !say.IsCompleted)
            await NextFrame();
        // 행동이 먼저였다면 남은 안내는 마지막 줄만 남기고 여기서 닫는다.
        if (!say.IsCompleted) _guide?.FinishGuideNow();
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
