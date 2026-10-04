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
    [Export] public string AssignRoomId = "guard_room";
    // 교육용 사고가 나는 작업실. 배치한 방과 달라야 토끼가 실제로 옮겨 간 기록이 남고,
    // 그 기록이 STEP 6 의 모순 추리 재료가 된다.
    // 발전실은 수리에 두 명이 필요한 방이라 "한 명을 더 보내야 고쳐진다"가 자연스럽다.
    [Export] public string AccidentRoomId = "power_room";
    // 교육용 이상 개체가 나타나는 작업실. 사고가 난 방(storage_room)과 달라야 한다 —
    // 같은 방이면 "고장 난 곳만 보면 된다"로 배우게 된다.
    // DAY0 에 열려 있는 작업실은 코어실 · 정비실 · 저장고 셋뿐이다(RoomDef.UnlockDay).
    [Export] public string AnomalyRoomId = "core_room";
    // 근무 시작 후 교육용 사고가 나기까지의 시간.
    [Export] public float IncidentDelaySeconds = 8f;

    public bool IsRunning { get; private set; }

    private ShiftFlowController _flow;
    private GuideHologramView _guide;

    // STEP 6 의 모순을 만들기 위해 STEP 3 에서 토끼가 "원래 있던" 작업실을 기억해 둔다.
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
        _ = RunGuarded();
    }

    // 교육을 도중에 끝낸다(개발 허브에서 DAY0 근무로 곧장 들어갈 때). 남은 안내 · 사건 ·
    // DAY1 전환은 하나도 실행하지 않고, 교육이 켜 둔 화면(자막 띠 · 얼굴창 · 강제 CCTV)만 걷는다.
    public void Abort()
    {
        if (!IsRunning) return;
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

        // STEP 6 의 교육용 진술. 실제 로그와 어긋나는 문장을 이 후크로만 돌려준다.
        LocalDialogueGenerator.ScriptedAnswerOverride = ScriptedAnswer;
        // 심문에서 무엇을 물었는지 듣는다 — 진행 조건 판정에만 쓴다.
        InterviewSession.Asked += OnInterviewAsked;
        if (PhoneCallHud.Instance != null)
            PhoneCallHud.Instance.EventChoiceMade += OnTutorialCallChoice;

        // ── STEP 1 : 직원 확인 ────────────────────────────────────────
        await Say("tut_intro");
        await Say("tut_mood");

        // ── STEP 1.5 : 시설 CCTV 투어 ────────────────────────────────
        await RunFacilityTour();

        // ── STEP 2 : 배치 ────────────────────────────────────────────
        await Say("tut_assign", RoomVars(AssignRoomId));
        await WaitForTutorialAssign(sim);
        Sfx.Instance?.Play("assign", -6f);
        // 배치가 맞았다. 그 방 카드의 "지금 무엇을 만들고 있는가" 줄을 한 번 짚어 준다.
        NSP.View.FacilityMonitorView.HighlightRoomNumber(AssignRoomId);
        await Say("tut_assign_rest");
        await Until(() => GameState.Instance?.CurrentPhase == GamePhase.Live);

        // 근무 화면 전환은 ShiftFlowController.EnterShift 가 이미 끝냈다(왼쪽=시설 / 오른쪽=GUIDE-0).
        await Say("tut_shift_start");

        // ── STEP 3 : 사고 ────────────────────────────────────────────
        await Wait(IncidentDelaySeconds);
        // STEP 6 의 모순을 만들려면 토끼가 실제로 방을 옮긴 기록이 남아야 한다. 그래서 수리
        // 최소 인원을 "지금 그 방에 있는 인원 + 1" 로 잡아, 한 명을 더 보내야만 고쳐지게 한다.
        string originRoomId = sim?.GetEmployeeState(TutorialEmployeeId)?.AssignedRoomId ?? "";
        int need = (sim?.OnDutyCount(AccidentRoomId) ?? 0) + 1;
        // 사고와 함께 승인 요청이 줄에 선다. 다만 **띄우지는 않는다** —
        // 아직 아무도 보내지 않았는데 "수리를 승인하시겠습니까" 부터 뜨면 순서가 거꾸로다.
        // 토끼가 그 방에 도착한 뒤에 풀어 준다(RunApprovalLesson).
        RepairApprovalSystem.Held = true;
        RepairApprovalSystem.Paused = true;
        sim?.TriggerTutorialAccident(AccidentRoomId, need);
        await Say("tut_incident");
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

        // ── STEP 3-A : 수리 승인 요청 · 미로 ──────────────────────────
        // 사람을 보낸 직후, 수리가 끝나기 전에 가르친다 — 실제 근무에서 겪는 순서 그대로다.
        await RunApprovalLesson();

        await Until(() => sim?.HasRepairPending(AccidentRoomId) == false);
        await Say("tut_repair_done");
        // 실제 근무의 방해공작을 설명만 한다 — DAY0 에는 방해자를 만들지 않는다.
        await Say("tut_sabotage_intro");

        // ── STEP 3-B : 이상 개체 ──────────────────────────────────────
        // 설명만 하고 넘어가면 근무 중에 화면을 돌려 볼 이유가 생기지 않는다.
        // 그래서 한 번 실제로 나타나게 하고, 직접 찾아 직접 지켜보게 한다.
        await RunAnomalyLesson(sim);

        // ── STEP 4 : 기절 · 의무실 이송 ───────────────────────────────
        await RunFaintLesson(sim);

        // ── STEP 5 : 전화 / 대화 ──────────────────────────────────────
        // 벨이 먼저 울리고, 그 소리를 들은 뒤에 안내가 뜬다.
        // (안내를 읽고 나서야 전화가 오면 순서가 거꾸로다.)
        // 거는 사람은 방금 수리를 끝낸 토끼다 — 교육에서 유일하게 정해진 통화.
        RingTutorialCall(TutorialEmployeeId);
        await Wait(TutorialCallRingLeadSeconds);
        // 안내가 흐르는 도중에 수화기를 들어도 곧바로 통화로 넘어간다.
        await SayThen("tut_call", () => PhoneCallHud.Instance?.IsOpen == true);
        await WaitForIncomingCallAnswered(TutorialEmployeeId);
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);
        await Say("tut_call_done");

        // ── STEP 6 : 기록 비교 ────────────────────────────────────────
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

        // ── STEP 7 : 교육 종료 ────────────────────────────────────────
        // 대화 기록까지 확인한 뒤 통화를 끊어야 교육이 끝난다.
        await Until(() => PhoneCallHud.Instance?.IsOpen != true);
        // 여기서부터 DAY1 배치 화면이 켜질 때까지 책상 위 기기를 잠근다 — 마무리 안내가 흐르는 동안
        // 전화기가 눌려 통화가 열린 채로 DAY1 에 들어가는 일이 있었다. 배치 단계(EnterSchedule)가 푼다.
        ControlRoom3DController.Instance?.SetInputLocked(true);
        GuideSubtitleHud.Instance?.SetTopAligned(false);   // 마무리 대사는 원래 자리로
        GuideCornerFace.SetLifted(false);
        // 마무리 멘트는 제어실 전체가 보이는 자리에서 한다. 모니터를 확대해 둔 채로 끝나면
        // 화면 하나만 꽉 찬 상태에서 교육이 닫혀, 이어지는 전환이 보이지 않는다.
        ControlRoom3DController.Instance?.ClearFocus(0.45f);
        await Wait(0.5);
        await Say("tut_complete");
        await Wait(0.6);

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

    private void RingTutorialCall(string caller)
    {
        if (string.IsNullOrEmpty(caller)) return;
        if (Phone3D.Instance is { IsBusy: false })
            Phone3D.Instance.RingIncoming(caller, DialogueRepository.EventTutorialRepairDone, AccidentRoomId);
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
    private async Task WaitForIncomingCallAnswered(string caller)
    {
        if (string.IsNullOrEmpty(caller)) return;

        while (PhoneCallHud.Instance?.IsOpen != true)
        {
            RingTutorialCall(caller);
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

    // STEP 6 교육용 고정 진술. 토끼의 "사고 당시 어디에 있었나" 답변만 가로채고,
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

    // ── 수리 승인 · 미로 교육 ─────────────────────────────────────────
    //
    // 승인 요청은 사고와 함께 이미 떠 있다(제한 시간은 Paused 로 멈춰 있다).
    // 안내를 한 줄 읽히고 나서야 시계가 돌기 시작한다 — 설명을 읽는 동안 제한 시간이
    // 지나가 버리면 "배우는 중에 벌점"이 된다.
    private async Task RunApprovalLesson()
    {
        // 사람을 보냈으니 이제 요청을 띄운다(제한 시간은 아직 멈춰 있다).
        RepairApprovalSystem.Held = false;
        // 줄에서 꺼내 화면에 올라오기까지 한 틱 — 떠 있는 것을 확인하고 말한다.
        for (int i = 0; i < 120 && RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking; i++)
            await NextFrame();

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

    // ── 기절 · 의무실 이송 교육 ───────────────────────────────────────
    //
    // 교육일에는 스트레스가 잠겨 있어 저절로 쓰러지는 일이 없다. 그래서 한 명을 직접
    // 쓰러뜨리고, **실제 구조 절차(FaintRescueSystem)를 그대로** 밟게 한다.
    // 의무실은 DAY1 에 열리는 방이라 이 교육 동안만 연다.
    private async Task RunFaintLesson(FacilitySimulation sim)
    {
        if (sim == null) return;

        string victim = PickFaintVictim(sim);
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
        IsRunning = false;
        RepairApprovalSystem.Paused = false;
        RepairApprovalSystem.Held = false;
        DayFeatures.ForceRoomOpen(FacilitySimulation.MedicalRoomIdPublic, false);
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

    private void OnGuideLine(string text) => GuideSubtitleHud.Instance?.SetLine(text);

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

        await Say("tut_anomaly_intro");
        if (!sim.Ghost.ForceAppear(AnomalyRoomId, sim, 9999f)) return;
        // 찾을 때까지 — 그 방을 CCTV 로 띄우는 순간이 "찾았다" 다.
        await SayThen("tut_anomaly_find", () => sim.SurveillanceTargetRoomId == AnomalyRoomId
                                                || !sim.Ghost.Active);
        if (!sim.Ghost.Active) return;

        // 지켜보는 동안 — 화면을 돌리면 게이지가 되감기는 것도 여기서 배운다.
        await SayThen("tut_anomaly_watch", () => !sim.Ghost.Active);
        await Say("tut_anomaly_done");
    }

    // --- await 헬퍼 -------------------------------------------------------

    // ── 시설 CCTV 투어 ──────────────────────────────────────────────────
    // 배치 단계의 MONITOR 02(직원 정보)를 잠시 기존 CCTV 화면으로 바꿔 방을 하나씩 비춘다.
    // 새 UI 를 만들지 않고 ControlRoom3DController.CctvViewport + FacilitySimulation 의
    // 감시 대상 전환을 그대로 쓴다. 끝나면 반드시 원래 직원 화면으로 되돌린다.
    // 눌러 보면 설명이 나오는 작업실. 순서는 정해져 있지 않다 — 어느 것부터 눌러도 된다.
    private static readonly (string RoomId, string GuideId)[] TourRooms =
    {
        ("core_room", "tut_room_core"),
        ("power_room", "tut_room_power"),
        ("guard_room", "tut_room_guard"),
        ("maintenance_room", "tut_room_maintenance"),
        ("storage_room", "tut_room_storage"),
        ("vent_room", "tut_room_vent"),
        ("medical_room", "tut_room_medical"),
        ("isolation_room", "tut_room_isolation"),
    };

    private async Task RunFacilityTour()
    {
        var ctl = ControlRoom3DController.Instance;
        var sim = FacilitySimulation.Instance;
        if (ctl == null || sim == null) return;

        await Say("tut_facility_intro");

        // MONITOR 02 만 CCTV 로. MONITOR 01 의 시설 지도는 그대로 둔다.
        ctl.SetRightScreen(ctl.CctvViewport);

        // 예전에는 여덟 방을 알아서 차례로 비추며 설명만 흘렸다. 그러면 관리자는
        // 읽기만 하다 끝나고, 정작 "왼쪽 지도를 눌러 오른쪽 화면을 바꾼다"는 이 게임의
        // 기본 조작을 한 번도 해보지 않은 채 근무에 들어간다.
        // 지금은 **직접 눌러야** 그 방이 뜨고 설명이 나온다.
        var left = new System.Collections.Generic.List<(string RoomId, string GuideId)>(TourRooms);
        string lastSeen = NSP.Ui.ScheduleMapView.Instance?.FocusRoomId ?? "";
        while (left.Count > 0 && IsRunning && IsInstanceValid(this))
        {
            // 남은 방 중 하나를 누를 때까지 기다린다.
            int picked = -1;
            // 조건은 여러 번 평가된다(SayThen 이 안내 중에도, 끝난 뒤에도 묻는다) —
            // 한 번 찾았으면 그대로 붙잡아 둔다. 그러지 않으면 두 번째 질문에서 false 가 되어
            // 영영 다음으로 넘어가지 못한다.
            // 첫 방은 tut_facility_intro 가 이미 "눌러 보십시오" 라고 했다 — 바로 기다린다.
            // 두 번째부터만 "다른 작업실도" 를 덧붙인다.
            Func<bool> picker = () =>
            {
                if (picked >= 0) return true;
                string now = NSP.Ui.ScheduleMapView.Instance?.FocusRoomId ?? "";
                if (now == lastSeen) return false;
                lastSeen = now;
                int hit = left.FindIndex(t => t.RoomId == now);
                if (hit < 0) return false;   // 이미 설명한 방을 다시 눌렀다 — 계속 기다린다
                picked = hit;
                return true;
            };
            // 재촉은 한 번이면 된다. 여덟 번 반복하면 읽지 않게 되고 지겹기만 하다.
            if (left.Count == TourRooms.Length - 1) await SayThen("tut_facility_pick", picker);
            else await Until(picker);
            if (picked < 0 || picked >= left.Count) break;

            var (roomId, guideId) = left[picked];
            left.RemoveAt(picked);
            sim.ForceSurveillanceTarget(roomId, 600f);   // 누른 방을 오른쪽 CRT 에 띄운다
            await Say(guideId);
        }
        sim.ReleaseForcedSurveillance();

        await Say("tut_facility_end");
        // 배치 화면으로 복귀 — 이후 기존 토끼 배치 교육이 그대로 이어진다.
        //
        // 지도의 선택도 같이 푼다. ScheduleStaffView 는 왼쪽 지도에서 방이 골라져 있으면
        // 직원 블록 대신 그 방의 설명을 그린다 — 투어에서 마지막으로 누른 방이 그대로
        // 남아 있어, 정작 "토끼를 배치하십시오" 단계에서 작업실 설명이 떠 있었다.
        NSP.Ui.ScheduleMapView.Instance?.ClearFocus();
        ctl.SetRightScreen(ctl.ScheduleStaffViewport);
    }

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

    // 조건이 참이 될 때까지 매 프레임 확인한다. 씬이 사라지거나 교육이 중단되면 빠져나온다.
    private async Task Until(Func<bool> condition)
    {
        while (IsRunning && IsInstanceValid(this) && !condition())
            await NextFrame();
        ThrowIfAborted();
    }
}
