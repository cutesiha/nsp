using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 휴게시간 심문 창(PhoneCallHud) 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/InterviewHudShot.tscn -- <저장 폴더>
//
// 사고 · 이동 · CCTV 반복 · 진술을 깔아 둔 뒤 여우를 심문한다.
//   1) 처음 화면(인사 + 기본 질문 세 개 — **자동으로 넘어가지 않는다**)
//   2) 기본 질문 하나를 고른 화면(답변 + 그 진술의 꼬리질문 + 직전 문답)
//   3) 자료 두 장을 고른 화면(A/B 배지 · 두 자료 제시 선택지)
//   4) 접은 화면(이름 한 줄만 남고 통화는 유지)
//
// 화면을 찍는 김에 §5 수용 기준 11~13(자동 재생 없음 · 접기 · 종료 경로)도 같이 확인해
// PASS/FAIL 로 찍는다. 그림 검사는 사람이 보지만, 상태 검사는 여기서 걸린다.
public partial class InterviewHudShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        Setup();

        // MONITOR 01 = 휴게실 화면(심문 콘솔이 그 위에 덮인다). 게임과 같은 800×600 논리 캔버스.
        _mon1 = new SubViewport { Size = new Vector2I(800, 600), RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = true, Disable3D = true };
        AddChild(_mon1);
        _mon1.AddChild(new RestRosterView());
        var hud = new PhoneCallHud();
        AddChild(hud);
        await Frames(3);
        hud.Open("fox", LocalInterviewDialogue.EventDay1Interview);
        await Seconds(2.5f);
        Save(dir, "interview_1_open.png");

        var session = (InterviewSession)typeof(PhoneCallHud)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);

        // ── §5-11 자동 재생 없음 ─────────────────────────────────────
        // 인사가 끝난 뒤 아무것도 누르지 않고 5초를 둔다. 화면이 스스로 바뀌면 안 된다.
        string before = MessageText(hud);
        await Seconds(5f);
        Check(MessageText(hud) == before, "§5-11 인사 뒤 5초, 아무 입력 없이 대사가 바뀌지 않는다");
        GD.Print($"      대사: {Trim(before)}");

        // 기본 질문 하나를 고른다 — 이제 답변은 이렇게 해야만 나온다.
        var pick = session.Openings.FirstOrDefault(o => o.FollowUps.Count > 0) ?? session.Openings.FirstOrDefault();
        if (pick != null) Call(hud, "PlayOpening", pick);
        await Typed(hud);
        Check(MessageText(hud) != before, "§5-11 기본 질문을 고르면 그때 답변이 나온다");
        Save(dir, "interview_2_statement.png");

        // 같은 질문을 다시 골라도 같은 문장이 나온다(다시 들을 수 있어야 한다).
        if (pick != null)
        {
            string first = MessageText(hud);
            Call(hud, "PlayOpening", pick);
            await Typed(hud);
            Check(MessageText(hud) == first, "같은 기본 질문을 다시 고르면 같은 진술을 다시 들려준다");
        }

        // CCTV 묶음 펼치기 + 원본 한 장 · 본인 진술 한 장 선택.
        var card = InterviewEvidenceDisplay.Compress(session.Board).FirstOrDefault(c => c.Kind == EvidenceCardKind.CctvRange);
        if (card != null)
        {
            var expanded = (HashSet<string>)typeof(PhoneCallHud)
                .GetField("_expandedCards", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
            expanded.Add(card.Key);
            session.Toggle(card.Items[0].Id);
        }
        var claim = session.Board.FirstOrDefault(e => e.Kind == EvidenceKind.OwnStatement && e.SubjectEmployeeId == "fox");
        if (claim != null) session.Toggle(claim.Id);
        Call(hud, "RefreshEvidence");
        Call(hud, "BuildInterviewMenu");
        // MON01 은 이제 조사 노트 한 장뿐이다(페이지 전환이 없다).
        await Frames(4);
        Save(dir, "interview_3_notes.png");

        // ── §5-12 접기 ───────────────────────────────────────────────
        int choicesBefore = ChoiceCount(hud);
        hud.SetCollapsed(true);
        await Frames(4);
        Check(hud.IsOpen, "§5-12 접어도 통화는 열려 있다(IsOpen)");
        Check(Session(hud) != null, "§5-12 접어도 세션이 살아 있다");
        Check(hud.Collapsed, "§5-12 접힘 상태가 표시된다");
        Save(dir, "interview_4_collapsed.png");

        hud.SetCollapsed(false);
        await Frames(4);
        Check(ChoiceCount(hud) == choicesBefore,
            $"§5-12 펼치면 접기 전 선택지가 그대로다 ({choicesBefore}개)");

        // ── §5-13 종료 경로 ──────────────────────────────────────────
        // 콘솔에는 종료 버튼이 없고, 보내는 신호는 "펼쳐 달라" 하나다.
        var con = RestInterviewConsole.Instance;
        var events = con?.GetType().GetEvents(BindingFlags.Public | BindingFlags.Instance);
        bool hasEnd = events != null && events.Any(e => e.Name == "EndPressed");
        bool hasExpand = events != null && events.Any(e => e.Name == "ExpandRequested");
        Check(!hasEnd, "§5-13 콘솔에 EndPressed(통화 종료)가 없다");
        Check(hasExpand, "§5-13 콘솔은 ExpandRequested(대화창 펼치기)만 보낸다");

        hud.SetCollapsed(true);
        con?.EmitExpandForTest();
        await Frames(4);
        Check(!hud.Collapsed && Session(hud) != null,
            "§5-13 「대화창 펼치기」는 창을 펼 뿐 세션을 끝내지 않는다");

        // 3D 전화기 클릭과 같은 경로 — 이건 실제로 끝낸다.
        hud.RequestClose();
        await Frames(4);
        Check(Session(hud) == null && !hud.IsOpen, "§5-13 전화기 클릭(RequestClose)은 세션을 끝낸다");

        GD.Print($"\n################ UI 수용 기준: {_pass} PASS / {_fail} FAIL ################");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private static void Call(object o, string method) =>
        o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(o, null);

    private static void Call(object o, string method, object arg) =>
        o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(o, new[] { arg });

    private static T Field<T>(object o, string name) => (T)o.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

    private static string MessageText(PhoneCallHud hud) => Field<RichTextLabel>(hud, "_message").Text;
    private static InterviewSession Session(PhoneCallHud hud) => Field<InterviewSession>(hud, "_session");

    private static int ChoiceCount(PhoneCallHud hud) =>
        Field<VBoxContainer>(hud, "_choices").GetChildCount() + Field<VBoxContainer>(hud, "_tail").GetChildCount();

    private static string Trim(string t) => t.Length <= 46 ? t : t[..46] + "…";

    // 타이핑이 끝날 때까지(최대 8초). 고정 시간으로 기다리면 답변이 길 때 어긋난다.
    private async System.Threading.Tasks.Task Typed(PhoneCallHud hud)
    {
        for (int i = 0; i < 480 && Field<bool>(hud, "_typing"); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Frames(2);
    }

    private static int _pass, _fail;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private static float At(int m) => m * DialogueClock.SecondsPerMinute + 0.01f;

    private static void Setup()
    {
        GameState.Instance.ResetRun(1);
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("fox");
        DialogueClaimState.ResetAll();
        var sim = FacilitySimulation.Instance;
        sim.RollDailyMoods();
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = "storage_room", ["dog"] = "guard_room", ["wolf"] = "maintenance_room",
            ["rabbit"] = "maintenance_room", ["sheep"] = "power_room", ["fox"] = "maintenance_room",
        };
        foreach (var kv in rooms)
        {
            var st = sim.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value; st.CurrentRoomId = kv.Value; st.Alive = true; st.Isolated = false;
            Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
        }
        // 새벽 12시 45분 정비실 사고, 여우의 이동, 저장고 CCTV 반복, 늑대의 증언.
        Log(LogEventType.RoomExit, "fox", "maintenance_room", At(163));
        Log(LogEventType.RoomEnter, "fox", "storage_room", At(164));
        Log(LogEventType.TaskFailed, "", "maintenance_room", At(165));
        for (int i = 0; i < 4; i++)
            PlayerKnownEvidence.RecordCctvObservation("storage_room", At(191 + i * 6),
                i % 2 == 0 ? new[] { "fox" } : new[] { "fox", "cat" });
        PlayerKnownEvidence.RecordSighting("wolf", "fox", "maintenance_room", At(166));
        PlayerKnownEvidence.RecordCctvObservation("guard_room", At(60), new[] { "dog" });
    }

    private static void Log(LogEventType type, string actor, string roomId, float at) =>
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = roomId,
            Description = $"(캡처 {type} {roomId})", WitnessEmployeeIds = new List<string>(),
        });

    private SubViewport _mon1;

    // 모니터 1 화면 + 실제 화면(아래 자막 띠) 두 장.
    private void Save(string dir, string file)
    {
        _mon1.GetTexture().GetImage().SavePng(dir + "/mon1_" + file);
        GetViewport().GetTexture().GetImage().SavePng(dir + "/screen_" + file);
    }

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private async System.Threading.Tasks.Task Seconds(float s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
