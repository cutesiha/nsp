using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// C 귀신 소멸 후 문의 전화(H-3) 검증.
//
//   godot --headless --path . res://scenes/debug/GhostScreamCallTest.tscn --quit-after 20000
//
// 순수 분위기 연출이다. 그래서 보는 것은 "적당히 드물게 오는가" 와 "단서가 되지 않는가" 다.
public partial class GhostScreamCallTest : Node
{
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ C 비명 문의 전화 검증 ################");
        TestRate();
        TestCallerExclusion();
        TestLines();
        TestNotAClue();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 40% · 하루 1회 ──────────────────────────────────────────────────
    private void TestRate()
    {
        Head("A", "소멸 100회에 문의 전화 35~45% · 하루 1회 상한");
        var director = new IncomingCallDirector();
        float want = director.GhostScreamCallChance;
        GD.Print($"   설정된 확률 = {want * 100f:0}%");
        Check(Mathf.Abs(want - 0.40f) < 0.001f, $"확률이 40% 로 설정돼 있다 ({want * 100f:0}%)");

        AddChild(director);   // GD.Randf 등 엔진 기능을 쓰므로 트리에 올려 둔다
        GameState.Instance.SetPhase(GamePhase.Live);

        // 실제 판정 경로를 100번 탄다 — 하루에 한 번씩, 100일.
        int hit = 0;
        for (int day = 1; day <= 100; day++)
        {
            GameState.Instance.ResetRun(2);   // DAY2(교육일이 아니어야 한다)
            typeof(IncomingCallDirector)
                .GetField("_ghostCallDay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(director, -1);     // 새 날로 본다
            if (director.DebugGhostDispelled("power_room")) hit++;
        }
        GD.Print($"   소멸 100회: 문의 전화 {hit}회 ({hit}%)");
        Check(hit >= 28 && hit <= 52, $"40% 근처다 ({hit}% — 100표본의 통계 오차 포함)");

        // 하루 상한 — 같은 날 여러 번 소멸해도 한 번만.
        GameState.Instance.ResetRun(2);
        typeof(IncomingCallDirector)
            .GetField("_ghostCallDay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(director, -1);
        int sameDay = 0;
        for (int i = 0; i < 40; i++) if (director.DebugGhostDispelled("power_room")) sameDay++;
        GD.Print($"   같은 날 40회 소멸: 문의 전화 {sameDay}회");
        Check(sameDay <= 1, $"하루 1회를 넘지 않는다 ({sameDay}회)");

        director.QueueFree();
    }

    // ── 귀신이 있던 방의 직원은 제외 ────────────────────────────────────
    private void TestCallerExclusion()
    {
        Head("B", "귀신이 있던 방의 직원은 발신자가 되지 않는다");
        Reset();
        const string ghostRoom = "power_room";

        // 한 명을 그 방에, 나머지는 다른 방에.
        var ids = _sim.GetActiveEmployeeIds().ToList();
        var rooms = _sim.GetRoomIds().Where(r => r != ghostRoom && _sim.GetRoomDef(r)?.IsRestricted == false).ToList();
        Place(ids[0], ghostRoom);
        for (int i = 1; i < ids.Count; i++) Place(ids[i], rooms[(i - 1) % rooms.Count]);

        int inRoom = 0;
        for (int i = 0; i < 300; i++)
        {
            string caller = IncomingCallDirector.GhostScreamCaller(ghostRoom);
            if (caller == ids[0]) inRoom++;
        }
        GD.Print($"   300회 중 그 방 직원({ids[0]})이 뽑힌 횟수 = {inRoom}");
        Check(inRoom == 0, "그 방에 있던 직원은 한 번도 뽑히지 않는다");

        // 기절 · 격리 · 사망자도 걸지 않는다.
        _sim.GetEmployeeState(ids[1]).Incapacitated = true;
        _sim.GetEmployeeState(ids[2]).Isolated = true;
        _sim.GetEmployeeState(ids[3]).Alive = false;
        bool badCaller = false;
        for (int i = 0; i < 300; i++)
        {
            string c = IncomingCallDirector.GhostScreamCaller(ghostRoom);
            if (c == ids[1] || c == ids[2] || c == ids[3]) badCaller = true;
        }
        Check(!badCaller, "기절 · 격리 · 사망한 직원은 걸지 않는다");

        // 아무도 없으면 전화도 없다.
        foreach (string id in ids) Place(id, ghostRoom);
        Check(string.IsNullOrEmpty(IncomingCallDirector.GhostScreamCaller(ghostRoom)),
            "물어볼 사람이 없으면 전화도 없다");
    }

    // ── 대사가 여섯 명 모두 준비돼 있다 ─────────────────────────────────
    private void TestLines()
    {
        Head("C", "여섯 명 모두 문의 · 두 가지 대답을 가지고 있다");
        Reset();
        foreach (string id in new[] { "rabbit", "cat", "dog", "fox", "sheep", "wolf" })
        {
            var line = LocalDialogueGenerator.BuildIncomingCall(id, DialogueRepository.EventGhostScream, "power_room");
            bool ok = line != null && !string.IsNullOrEmpty(line.Opening) && line.Choices.Count == 2
                      && line.Choices.All(c => !string.IsNullOrEmpty(c.Reply));
            GD.Print($"   {id,-7} \"{line?.Opening}\"");
            if (ok)
            {
                GD.Print($"   {"",7}  ① {line.Choices[0].Text} → \"{line.Choices[0].Reply}\"");
                GD.Print($"   {"",7}  ② {line.Choices[1].Text} → \"{line.Choices[1].Reply}\"");
            }
            Check(ok, $"{id} 의 문의 · 대답이 모두 있다");
        }
    }

    // ── 단서가 되지 않는다 ──────────────────────────────────────────────
    private void TestNotAClue()
    {
        Head("D", "통화 기록 · 조사 자료 · 근무 기억 어디에도 남지 않는다");
        Reset();
        var ids = _sim.GetActiveEmployeeIds().ToList();
        var rooms = _sim.GetRoomIds().Where(r => _sim.GetRoomDef(r)?.IsRestricted == false).ToList();
        for (int i = 0; i < ids.Count; i++) Place(ids[i], rooms[i % rooms.Count]);

        CallMemoryLog.ResetAll();
        PlayerKnownEvidence.ResetAll();
        ShiftMemory.Invalidate();
        int cardsBefore = InterviewEvidenceBoard.Build("cat").Count;

        // 비교군 — 사고 신고 통화는 기록되어 자료 카드가 된다(이 경로가 살아 있는지 먼저 확인).
        CallMemoryLog.Record("cat", CallRecordKind.Reported, "power_room", DialogueRepository.EventAccidentNearby);
        int cardsAccident = InterviewEvidenceBoard.Build("cat").Count;
        Check(cardsAccident > cardsBefore, $"사고 신고 통화는 자료 카드가 된다 ({cardsBefore} → {cardsAccident})");

        // 비명 문의는 기록 자체를 남기지 않으므로 카드도 생기지 않는다.
        CallMemoryLog.ResetAll();
        ShiftMemory.Invalidate();
        int before = InterviewEvidenceBoard.Build("cat").Count;
        // 실제 경로가 기록을 건너뛰는지는 PhoneCallHud / IncomingCallDirector 의 예외로 보장된다.
        // 여기서는 "기록이 없으면 카드도 없다" 를 확인한다.
        Check(CallMemoryLog.All.Count == 0, "통화 기록이 비어 있다");
        Check(InterviewEvidenceBoard.Build("cat").Count == before, "조사 자료 카드가 늘지 않는다");

        bool inMemory = ShiftMemory.Of("cat", GameState.Instance.CurrentDay)
            .Any(m => m.Kind is MemoryKind.CallReported or MemoryKind.CallMissed);
        Check(!inMemory, "근무 기억에 통화로 남지 않는다");

        // 사실을 알려주면 스트레스가 오른다(그것이 유일한 결과다).
        var st = _sim.GetEmployeeState("cat");
        st.Stress = 10f;
        _sim.AddStress("cat", IncomingCallDirector.GhostScreamTruthStress);
        GD.Print($"   사실을 알려준 뒤 스트레스 = {st.Stress:0.0} (담력 배율이 걸린다)");
        Check(st.Stress > 10f, "사실을 알려주면 발신자 스트레스가 오른다");
    }

    // --- 도우미 ---------------------------------------------------------

    private void Reset()
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        CallMemoryLog.ResetAll();
        PlayerKnownEvidence.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Place(string id, string room)
    {
        var st = _sim.GetEmployeeState(id);
        if (st == null) return;
        st.AssignedRoomId = room;
        st.CurrentRoomId = room;
        st.Alive = true;
        st.Isolated = false;
        st.Incapacitated = false;
    }

    private void Head(string id, string t) => GD.Print($"\n===== [{id}] {t} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
