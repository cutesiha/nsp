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
//   1) 처음 화면(근무 진술 블록 + 사건별 조사 노트)
//   2) 진술 하나를 고른 화면(그 진술의 꼬리질문)
//   3) CCTV 묶음을 펼치고 자료 두 장을 고른 화면(자료 A / B)
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
        var withFu = session.Openings.FirstOrDefault(o => o.FollowUps.Count > 0) ?? session.Openings.FirstOrDefault();
        if (withFu != null)
        {
            session.SelectOpening(withFu.Index);
            Call(hud, "BuildInterviewMenu");
        }
        await Frames(4);
        Save(dir, "interview_2_statement.png");

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
        // MON01 은 이제 조사 노트 한 장뿐이다(페이지 전환이 없다).
        await Frames(4);
        Save(dir, "interview_3_notes.png");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private static void Call(object o, string method) =>
        o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(o, null);

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
