using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// 직원의 이동 진술이 **실제 이동 기록과 반드시 일치하는가**.
//
//   godot --headless --path . res://scenes/debug/MovementTruthTest.tscn --quit-after 20000
//
// 버그: 목적지로 가는 길에 잠시 지나친 방(PassingThrough)을 진술 타임라인이 체류로 셌다.
// 플레이어가 보는 시설 로그는 그 줄을 빼고 그리므로(FacilityLogFormatter), 같은 사실을
// 두 자료가 다르게 말했다 — 환기실 → (경비실 통과) → 발전실 인 직원이
// "경비실에 있다가 발전실로 갔다"고 답했다.
public partial class MovementTruthTest : Node
{
    private const float Step = 0.25f;
    private int _pass, _fail;

    // 강제 이동 경로 — 과제에 적힌 순서 그대로.
    private static readonly string[] Route = { "core_room", "vent_room", "power_room", "storage_room" };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("\n\n################ 이동 진술 Truth ################");
        var sim = FacilitySimulation.Instance;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await RouteTest(sim);
        await RandomShiftTest(sim);

        GD.Print($"\n################ 통과 {_pass} · 실패 {_fail} ################\n");
        if (_fail > 0) GD.PushError($"MovementTruthTest: {_fail}건 실패");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    // ── ① 6명 전부, 정해진 경로로 옮기며 매 이동 후 확인 ──────────────
    private async System.Threading.Tasks.Task RouteTest(FacilitySimulation sim)
    {
        GD.Print("\n── ① core → vent → power → storage (6인 전원) ──");

        foreach (string id in sim.GetActiveEmployeeIds().ToList())
        {
            GameState.Instance.ResetRun(1);
            sim.ResetRun();
            foreach (string other in sim.GetActiveEmployeeIds()) sim.AssignToRoom(other, "core_room");
            sim.ResetForNewShift();
            GameState.Instance.SetPhase(GamePhase.Live);

            // 첫 배치 도착까지
            await Settle(sim, 30f);

            var visited = new List<string> { "core_room" };
            foreach (string room in Route.Skip(1))
            {
                sim.AssignToRoom(id, room);
                await Settle(sim, 40f, () => sim.GetEmployeeState(id)?.CurrentRoomId == room
                                             && sim.GetEmployeeState(id)?.IsMoving != true);
                visited.Add(room);

                float now = GameState.Instance.DayTimeSeconds;
                int day = GameState.Instance.CurrentDay;

                string at = DialogueContextBuilder.RoomAt(id, day, now);
                string before = DialogueContextBuilder.RoomBefore(id, day, now);
                string expectedBefore = visited[^2];

                bool okAt = at == room;
                bool okBefore = before == expectedBefore;
                if (!okAt || !okBefore)
                {
                    GD.Print($"   ! {id} → {room}: RoomAt={at}(기대 {room}) RoomBefore={before}(기대 {expectedBefore})");
                    GD.Print("     [원본 이동 기록]");
                    foreach (var e in EventLog.Instance.GetAllEntries()
                                 .Where(e => e.ActorEmployeeId == id && e.Day == day
                                             && e.EventType is LogEventType.RoomEnter or LogEventType.RoomExit
                                                 or LogEventType.Relocation or LogEventType.TaskStart))
                        GD.Print($"       {e.GameTimeSeconds,6:0.0}s {e.EventType,-12} {e.RoomId,-18} passing={e.PassingThrough}");
                }
                Check(okAt && okBefore, $"{id}: {expectedBefore} → {room} 진술이 실제와 같다");

                // 진술에 **방문하지 않은 방**이 섞이면 안 된다.
                Check(string.IsNullOrEmpty(before) || visited.Contains(before),
                    $"{id}: 직전 방 '{before}' 은 실제로 들른 방이다");
            }
        }
    }

    // ── ② DAY1~5 무작위 근무를 돌려 "안 간 방" 진술이 한 번이라도 나오는지 ──
    private async System.Threading.Tasks.Task RandomShiftTest(FacilitySimulation sim)
    {
        GD.Print("\n── ② DAY1~5 반복 — 가지 않은 방이 진술에 나오는가 ──");
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "vent_room" };
        int bad = 0, checks = 0;

        for (int day = 1; day <= 5; day++)
        {
            GameState.Instance.ResetRun(1);
            sim.ResetRun();
            for (int d = 1; d < day; d++) GameState.Instance.GoToNextDay();

            var roster = sim.GetActiveEmployeeIds().ToList();
            foreach (string id in roster) sim.AssignToRoom(id, rooms[rng.RandiRange(0, rooms.Length - 1)]);
            GameState.Instance.AssignRandomSaboteur(roster);
            sim.ResetForNewShift();
            GameState.Instance.SetPhase(GamePhase.Live);

            // 실제로 들른 방을 직접 센다(진술과 비교할 기준).
            var realVisits = roster.ToDictionary(id => id, _ => new HashSet<string>());

            for (float t = 0f; t < 150f; t += Step)
            {
                GameState.Instance.AdvanceDayTime(Step);
                sim.Tick(Step);

                // 가끔 재배치해 동선을 섞는다.
                if (t > 20f && Mathf.Abs(t % 25f) < Step * 0.5f)
                {
                    string who = roster[rng.RandiRange(0, roster.Count - 1)];
                    sim.AssignToRoom(who, rooms[rng.RandiRange(0, rooms.Length - 1)]);
                }
            }

            // "실제로 들른 방" 의 기준은 원본 입장 기록이다 — 통과(PassingThrough)는 뺀다.
            // 시설 로그가 플레이어에게 보여 주는 것과 같은 기준이다(FacilityLogFormatter).
            foreach (var e in EventLog.Instance.GetAllEntries()
                         .Where(e => e.Day == day && e.EventType == LogEventType.RoomEnter
                                     && !e.PassingThrough && !string.IsNullOrEmpty(e.RoomId)))
                if (realVisits.ContainsKey(e.ActorEmployeeId)) realVisits[e.ActorEmployeeId].Add(e.RoomId);

            float now = GameState.Instance.DayTimeSeconds;
            foreach (string id in roster)
            {
                // 결번자의 거짓말은 별개다 — 여기서는 정상 직원만 본다.
                if (id == GameState.Instance.SaboteurEmployeeId) continue;

                string at = DialogueContextBuilder.RoomAt(id, day, now);
                string before = DialogueContextBuilder.RoomBefore(id, day, now);
                foreach (var (label, room) in new[] { ("RoomAt", at), ("RoomBefore", before) })
                {
                    if (string.IsNullOrEmpty(room)) continue;
                    checks++;
                    if (realVisits[id].Contains(room)) continue;
                    bad++;
                    GD.Print($"   ! DAY{day} {id} {label}={room} — 실제로 들른 적 없음 " +
                             $"(실제: {string.Join(", ", realVisits[id])})");
                }
            }
        }

        GD.Print($"   검사 {checks}건 중 어긋남 {bad}건");
        Check(bad == 0, $"정상 직원 진술에 방문하지 않은 방이 없다 ({bad}건)");
    }

    // ── 헬퍼 ───────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Settle(FacilitySimulation sim, float maxSeconds,
        System.Func<bool> until = null)
    {
        for (float t = 0f; t < maxSeconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            sim.Tick(Step);
            if (until != null && until()) return;
            if (Mathf.Abs(t % 5f) < Step * 0.5f) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Check(bool ok, string label)
    {
        if (ok) { _pass++; }
        else { _fail++; GD.Print($"  FAIL {label}"); }
    }
}
