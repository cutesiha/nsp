using System;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Ui;
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

    [Export] public NodePath ControllerPath = "..";
    [Export] public NodePath ShiftFlowPath = "../ShiftFlowController";

    // 교육에 쓰는 고정 배역/장소. 데이터(UnlockDay)와 짝을 이룬다.
    [Export] public string TutorialEmployeeId = "rabbit";
    [Export] public string AssignRoomId = "maintenance_room";
    [Export] public string AccidentRoomId = "storage_room";
    // 근무 시작 후 교육용 사고가 나기까지의 시간.
    [Export] public float IncidentDelaySeconds = 8f;

    public bool IsRunning { get; private set; }

    private ControlRoom3DController _ctl;
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
        _ctl = GetNodeOrNull<ControlRoom3DController>(ControllerPath);
        _flow = GetNodeOrNull<ShiftFlowController>(ShiftFlowPath);
    }

    public override void _ExitTree()
    {
        LocalDialogueGenerator.ScriptedAnswerOverride = null;
        if (Instance == this) Instance = null;
    }

    // ShiftFlowController 가 DAY0 배치 단계에 들어온 직후 한 번 부른다.
    public void BeginDay0()
    {
        if (IsRunning) return;
        IsRunning = true;
        _ = RunAsync();
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

        // ── STEP 1 : 직원 확인 ────────────────────────────────────────
        await Say("tut_intro");
        await Say("tut_mood");

        // ── STEP 2 : 배치 ────────────────────────────────────────────
        await Say("tut_assign");
        await WaitForTutorialAssign(sim);
        Sfx.Instance?.Play("assign", -6f);
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
        await Until(() => sim?.HasRepairPending(AccidentRoomId) == false);
        await Say("tut_repair_done");

        // ── STEP 4 : 시설 로그 ────────────────────────────────────────
        await Say("tut_log");
        await Until(() => Day1HistoryOverlay.Instance?.IsLogOpen == true);
        await Say("tut_log_done");

        // ── STEP 5 : 전화 / 대화 ──────────────────────────────────────
        // 벨이 먼저 울리고, 그 소리를 들은 뒤에 안내가 뜬다.
        // (안내를 읽고 나서야 전화가 오면 순서가 거꾸로다.)
        string caller = PickCaller();
        RingTutorialCall(caller);
        await Wait(TutorialCallRingLeadSeconds);
        await Say("tut_call");
        await WaitForIncomingCallAnswered(caller);
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
        await Say("tut_ask_where");
        // 이동 기록으로 물으면 통과. 옮긴 기록이 아예 없는 판이면 아무 질문이나 하면 된다.
        await Until(() => _rabbitAskedWithEvidence
                          || (string.IsNullOrEmpty(_rabbitOriginRoomName) && _rabbitAskedAnything));
        // 토끼가 실제로 방을 옮긴 기록이 있을 때만 "기록과 진술이 다르다"고 말한다.
        // (플레이어가 지시와 다르게 움직여 재배치 기록이 없으면 그 단계는 건너뛴다.)
        if (!string.IsNullOrEmpty(_rabbitOriginRoomName))
        {
            await Say("tut_contradiction");
            await Until(() => Day1HistoryOverlay.Instance?.IsLogOpen == true);
        }
        await Say("tut_dialogue_log");
        await Until(() => Day1HistoryOverlay.Instance?.IsDialogueOpen == true);

        // ── STEP 7 : 교육 종료 + 결번자 영상 ──────────────────────────
        await Say("tut_complete");
        await PlayGhostVideo();
        await Say("tut_after_ghost");
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
            await Say(want);
        }
    }

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

    // 왼쪽 CRT 를 잠시 '영상' 재생기로 바꿔 결번자 컷씬을 틀고, 끝나면 휴게 명단으로 되돌린다.
    // 실제 CCTV 시스템과는 무관한 이벤트 영상이다.
    private async Task PlayGhostVideo()
    {
        var player = CutscenePlayer.Instance;
        if (player == null || _ctl == null) return;

        AmbientOverlay.Instance?.SetSceneIntensity(1f);
        _ctl.SetScreenNoise(0.35f);
        _ctl.SetLeftScreen(_ctl.CutsceneViewport);
        await Wait(0.2);

        var tcs = new TaskCompletionSource();
        void Handler() { player.Finished -= Handler; tcs.TrySetResult(); }
        player.Finished += Handler;
        player.Play("outage_ghost");
        await tcs.Task;

        await Wait(0.5);
        player.Clear();
        _ctl.SetScreenNoise(0.020f);
        _ctl.SetLeftScreen(_ctl.RestRosterViewport);
        AmbientOverlay.Instance?.SetSceneIntensity(0.1f);
    }

    // 안내가 뜨기 전에 벨을 먼저 울리는 시간.
    private const double TutorialCallRingLeadSeconds = 1.3;

    // 사고를 보고할 직원 한 명. 교육 대상(토끼)은 제외한다.
    private string PickCaller()
    {
        var sim = FacilitySimulation.Instance;
        foreach (var id in sim?.GetActiveEmployeeIds() ?? new System.Collections.Generic.List<string>())
        {
            if (id == TutorialEmployeeId) continue;
            if (sim.IsOnDuty(id)) return id;
        }
        return "";
    }

    private void RingTutorialCall(string caller)
    {
        if (string.IsNullOrEmpty(caller)) return;
        if (Phone3D.Instance is { IsBusy: false })
            Phone3D.Instance.RingIncoming(caller, DialogueRepository.EventAccidentNearby, AccidentRoomId);
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
            if (!IsRunning) return;
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
        if (employeeId != TutorialEmployeeId || questionId != DialogueQuestions.Where) return null;
        _rabbitAnsweredWhere = true;
        // 옮긴 기록이 없으면 지어낼 모순도 없다 — 평소대로 로그 기반 대사를 쓰게 둔다.
        if (string.IsNullOrEmpty(_rabbitOriginRoomName)) return null;
        string text = PrologueScript.GetScripted("tut_rabbit_where");
        return string.IsNullOrEmpty(text) ? null : text.Replace("{FROM_ROOM}", _rabbitOriginRoomName);
    }

    private void Finish()
    {
        IsRunning = false;
        LocalDialogueGenerator.ScriptedAnswerOverride = null;
        InterviewSession.Asked -= OnInterviewAsked;
        if (_guide != null) _guide.LineShown -= OnGuideLine;
        GuideCornerFace.Instance?.SetShown(false);
        _guide?.HideHologram();
        GuideSubtitleHud.Instance?.SetActive(false);
        GuideSubtitleHud.Instance?.Clear();
        RestRosterView.Instance?.SetNextEnabled(true);
    }

    private void OnGuideLine(string text) => GuideSubtitleHud.Instance?.SetLine(text);

    private static string RoomName(string roomId) =>
        FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId ?? "";

    // --- await 헬퍼 -------------------------------------------------------

    private Task Say(string guideId, System.Collections.Generic.Dictionary<string, string> replacements = null)
    {
        // 교육 중에는 어떤 화면도 빼앗지 않는다. 지시는 화면 아래 자막 띠가 전하고,
        // GUIDE-0 의 얼굴은 CCTV 화면 오른쪽 아래 구석의 작은 창으로만 뜬다.
        GuideCornerFace.Instance?.SetShown(true);
        return PrologueDirector.ShowGuide(_guide, guideId, replacements);
    }

    private async Task NextFrame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // 조건이 참이 될 때까지 매 프레임 확인한다. 씬이 사라지면 조용히 빠져나온다.
    private async Task Until(Func<bool> condition)
    {
        while (IsRunning && IsInstanceValid(this) && !condition())
            await NextFrame();
    }
}
