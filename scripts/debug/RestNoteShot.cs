using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 휴게시간 — **모니터1 을 확대한 상태에서 조사자료를 고른** 화면 캡처.
//
//   godot --path . res://scenes/debug/RestNoteShot.tscn -- <저장 폴더>
//
// 창 모드로 돌려야 한다(헤드리스는 그림을 그리지 않는다).
// InterviewHudShot 은 심문 창만 따로 띄워 찍는다 — 여기서는 **실제 중앙제어실**을
// 띄우고 휴게시간 상태로 바꾼 뒤, 게임에서 보는 그대로의 확대 화면을 찍는다.
public partial class RestNoteShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(60);

        SeedShift();

        // 휴게시간으로 바꾼다 — 실제 흐름(RequestRestFromReport)이 하는 것과 같은 상태.
        var ctl = ControlRoom3DController.Instance;
        GameState.Instance?.SetPhase(GamePhase.Rest);
        RestRosterView.Instance?.Present(false);
        ctl?.SetLeftScreen(ctl.RestRosterViewport);
        ctl?.SetRightScreen(ctl.InterviewViewport);
        await Frames(20);
        ctl?.FocusMonitor(1, 0.5f);      // 모니터1 확대
        await Frames(60);
        Save(dir, "rest_01_명단.png");

        // 여우를 심문하고 조사자료 두 장을 고른다.
        var hud = PhoneCallHud.Instance;
        if (hud == null) { GD.Print("  !! PhoneCallHud 를 찾지 못했다"); GetTree().Quit(); return; }
        hud.Open("fox", LocalInterviewDialogue.EventDay1Interview);
        await Frames(150);

        var session = (InterviewSession)typeof(PhoneCallHud)
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud);
        if (session != null)
        {
            var card = InterviewEvidenceDisplay.Compress(session.Board)
                .FirstOrDefault(c => c.Kind == EvidenceCardKind.CctvRange);
            if (card != null)
            {
                var expanded = (HashSet<string>)typeof(PhoneCallHud)
                    .GetField("_expandedCards", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud);
                expanded?.Add(card.Key);
                session.Toggle(card.Items[0].Id);
            }
            var claim = session.Board.FirstOrDefault(
                e => e.Kind == EvidenceKind.OwnStatement && e.SubjectEmployeeId == "fox");
            if (claim != null) session.Toggle(claim.Id);
            Call(hud, "RefreshEvidence");
            Call(hud, "BuildInterviewMenu");
        }
        await Frames(40);
        Save(dir, "rest_02_조사자료선택.png");

        // 창을 접어 조사 노트 전체가 보이는 화면도 한 장.
        hud.SetCollapsed(true);
        await Frames(40);
        Save(dir, "rest_03_노트전체.png");

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // 하루치 근무 기록을 깔아 둔다 — 조사 노트가 비어 있으면 고를 자료가 없다.
    private static void SeedShift()
    {
        GameState.Instance?.SetSaboteur("fox");
        DialogueClaimState.ResetAll();
        var sim = FacilitySimulation.Instance;
        sim?.RollDailyMoods();
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = "storage_room", ["dog"] = "guard_room", ["wolf"] = "maintenance_room",
            ["rabbit"] = "maintenance_room", ["sheep"] = "power_room", ["fox"] = "maintenance_room",
        };
        foreach (var kv in rooms)
        {
            var st = sim?.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value; st.CurrentRoomId = kv.Value; st.Alive = true; st.Isolated = false;
            Log(LogEventType.TaskStart, kv.Key, kv.Value, 1f);
        }
        Log(LogEventType.RoomExit, "fox", "maintenance_room", At(163));
        Log(LogEventType.RoomEnter, "fox", "storage_room", At(164));
        Log(LogEventType.TaskFailed, "", "maintenance_room", At(165));
        for (int i = 0; i < 4; i++)
            PlayerKnownEvidence.RecordCctvObservation("storage_room", At(191 + i * 6),
                i % 2 == 0 ? new[] { "fox" } : new[] { "fox", "cat" });
        PlayerKnownEvidence.RecordSighting("wolf", "fox", "maintenance_room", At(166));
        PlayerKnownEvidence.RecordCctvObservation("guard_room", At(60), new[] { "dog" });
    }

    private static float At(float minutes) => minutes;

    private static void Log(LogEventType type, string actor, string roomId, float at) =>
        EventLog.Instance.Log(new LogEntry
        {
            Day = 1, GameTimeSeconds = at, EventType = type, ActorEmployeeId = actor, RoomId = roomId,
            Description = $"(캡처 {type} {roomId})", WitnessEmployeeIds = new List<string>(),
        });

    private static void Call(object target, string method, params object[] a)
        => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, a);

    private void Save(string dir, string file)
        => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
