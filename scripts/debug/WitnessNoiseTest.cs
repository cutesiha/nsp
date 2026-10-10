using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// F-5 목격 증언 잡음 검증.
//
//   godot --headless --path . res://scenes/debug/WitnessNoiseTest.tscn --quit-after 5000
//
// 확정 증언 한 건이 곧 범인 확정이 되면 추리가 아니라 정답 확인이 된다.
// 여기서 보는 것은 세 가지다.
//   ① 정상 직원의 실제 이동도 "수상한 목격" 후보가 된다(지어내지 않는다 — 로그와 일치).
//   ② 목격은 확률이다 — 자격이 되는 사람이 늘 보지는 않는다.
//   ③ 결번 개체도 남의 실제 이동을 목격해 보고할 수 있다.
public partial class WitnessNoiseTest : Node
{
    private const string Guard = "guard_room";
    private const string Power = "power_room";

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
        GD.Print("\n\n################ F-5 목격 증언 잡음 검증 ################");
        TestOrdinaryMovesAreSeen();
        TestWitnessIsProbabilistic();
        TestSaboteurCanReportOthers();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── ① 정상 직원의 이동도 증언 후보가 된다 ────────────────────────────
    private void TestOrdinaryMovesAreSeen()
    {
        Head("A", "정상 직원의 실제 이동도 '수상한 목격' 후보가 된다");
        var cfg = Config.Instance.Data;
        Check(cfg.OrdinaryMoveSightingChance > 0f,
            $"잡음 확률이 켜져 있다 ({cfg.OrdinaryMoveSightingChance:0.00})");

        int trials = 400, noted = 0, mismatched = 0;
        for (int i = 0; i < trials; i++)
        {
            Reset();
            // 관찰력이 높은 고양이가 발전실에 있고, 강아지가 지시를 받아 그 방으로 들어온다.
            Place("cat", Power);
            Place("dog", Guard);
            EventLog.Instance.ClearAll();          // 여기까지의 배치 이동은 보지 않는다
            Place("dog", Power);

            var note = OddNotes().FirstOrDefault(e => e.ActorEmployeeId == "dog");
            if (note == null) continue;
            noted++;
            // 지어낸 목격이 아니어야 한다 — 같은 방에 실제로 들어온 기록이 있어야 한다.
            bool real = EventLog.Instance.GetAllEntries().Any(e =>
                e.EventType == LogEventType.RoomEnter && e.ActorEmployeeId == "dog"
                && e.RoomId == note.RoomId);
            if (!real) mismatched++;
        }

        GD.Print($"   {trials}회 중 강아지의 이동이 증언으로 남은 횟수 = {noted}");
        Check(noted > 0, "정상 직원의 이동이 증언으로 남는 경우가 있다");
        Check(noted < trials, "늘 남지는 않는다(잡음이지 규칙이 아니다)");
        Check(mismatched == 0, $"증언은 전부 실제 이동 기록과 맞는다 (어긋남 {mismatched}건)");
    }

    // ── ② 목격은 확률이다 ────────────────────────────────────────────────
    private void TestWitnessIsProbabilistic()
    {
        Head("B", "자격이 되는 목격자도 늘 보지는 않는다");
        int trials = 300, seen = 0;
        for (int i = 0; i < trials; i++)
        {
            Reset();
            Place("cat", Power);
            Place("dog", Power);
            var got = _sim.RecordOddBehaviour("dog", Power, "설비 쪽에 평소보다 오래 머물렀다");
            if (got.Contains("cat")) seen++;
        }
        GD.Print($"   {trials}회 중 고양이가 목격한 횟수 = {seen}");
        Check(seen > 0, "가끔은 본다");
        Check(seen < trials, "늘 보지는 않는다 — 방해공작 한 번에 확정 증언이 반드시 생기지 않는다");
    }

    // ── ③ 결번 개체도 남의 실제 이동을 보고할 수 있다 ───────────────────────
    private void TestSaboteurCanReportOthers()
    {
        Head("C", "결번 개체도 남의 실제 이동을 목격해 보고할 수 있다");
        bool everPointed = false, pointedSelf = false;
        for (int i = 0; i < 200 && !everPointed; i++)
        {
            Reset();
            GameState.Instance.SetSaboteur("cat");
            Place("cat", Power);
            Place("dog", Guard);
            Place("dog", Power);

            var known = DialogueContextBuilder.FindKnownSuspicious("cat", GameState.Instance.CurrentDay);
            if (known == null) continue;
            everPointed = true;
            if (known.ActorEmployeeId == "cat") pointedSelf = true;
            GD.Print($"   결번(고양이)가 본 것: {known.ActorEmployeeId} @ {known.RoomId}");
        }
        Check(everPointed, "결번 개체에게도 목격 자료가 생긴다('아무것도 모른다' 일변도가 아니다)");
        Check(!pointedSelf, "자기 자신을 지목하지는 않는다");
    }

    // --- 도우미 ---------------------------------------------------------

    private List<LogEntry> OddNotes() => EventLog.Instance.GetAllEntries()
        .Where(e => e.EventType == LogEventType.Neglect && e.WitnessEmployeeIds.Count > 0)
        .ToList();

    private void Reset()
    {
        GameState.Instance.ResetRun(2);
        GameState.Instance.SetSaboteur("");
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    // 배치하고 실제로 그 방에 도착할 때까지 돌린다.
    // 방 점유자 목록은 도착(ArriveAtRoom)에서만 갱신되므로 위치를 손으로 옮기면 안 된다 —
    // 옮기면 같은 방에 있어도 목격자 후보에 들어가지 않는다.
    private void Place(string id, string room)
    {
        _sim.AssignToRoom(id, room);
        var st = _sim.GetEmployeeState(id);
        for (int i = 0; i < 2000 && st != null && st.CurrentRoomId != room; i++) Settle(1f / 30f);
    }

    private void Settle(float seconds)
    {
        const float step = 1f / 30f;
        for (float t = 0f; t < seconds; t += step)
        {
            _sim.Tick(step);
            GameState.Instance.AdvanceDayTime(step);
        }
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }
}
