using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 봉쇄 코어 복구 밸런스 — 난이도 3종 × 플레이 패턴 3종 자동 시뮬레이션.
//
//   godot --headless --path . res://scenes/debug/RecoveryBalanceTest.tscn --quit-after 200000
//
// 실제 FacilitySimulation 을 그대로 돌린다(배치만 패턴대로 흉내낸다).
// 판정은 하나도 바꾸지 않는다 — 읽고 세기만 한다.
public partial class RecoveryBalanceTest : Node
{
    private const float Step = 0.5f;
    private const int Runs = 100;

    private enum Style { Rookie, Average, Veteran }

    // 의무실이 빠져 있었다. 그러면 스트레스가 하루도 치료되지 않아 DAY4 에 6명 중 5명이
    // 기절해 버린다 — 실제 플레이어는 기절자를 의무실로 보낸다(그게 의무실의 역할이다).
    private static readonly string[] SideRooms =
        { "power_room", "maintenance_room", "guard_room", "vent_room", "medical_room", "storage_room" };

    private FacilitySimulation _sim;
    private readonly RandomNumberGenerator _rng = new();
    private int _matMade, _matUsed, _matDiscard;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("\n\n################ 코어 복구 밸런스 ################");
        _sim = FacilitySimulation.Instance;
        _rng.Seed = 20261008;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        foreach (int cycle in new[] { 1, 2, 3 })
        {
            var prof = RecoveryProfile.ForCycle(cycle);
            GD.Print($"\n══════ {cycle}차 — {prof.DisplayName} " +
                     $"(1명 ×{prof.CoreStaffMultiplier(1):0.00} · 2명 ×{prof.CoreStaffMultiplier(2):0.00} · " +
                     $"자재 ×{prof.CoreMaterialCostMultiplier:0.00} · 방해 ×{prof.SabotageCoreDamageMultiplier:0.00} · " +
                     $"보정 {(prof.CatchupEnabled ? "O" : "X")}) ══════");
            GD.Print("  패턴      DAY1    DAY2    DAY3    DAY4    DAY5   100%달성");
            foreach (Style style in new[] { Style.Rookie, Style.Average, Style.Veteran })
            {
                var r = Simulate(cycle, style, Runs);
                GD.Print($"  {Name(style),-8} {r.Day[0],5:0.0}% {r.Day[1],6:0.0}% {r.Day[2],6:0.0}% " +
                         $"{r.Day[3],6:0.0}% {r.Day[4],6:0.0}%   {r.SuccessRate,5:0.0}%");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        await LedgerBreakdown();
        await Calibrate();
        await Day3RecoveryCase();

        GD.Print("\n################ 끝 ################\n");
        GetTree().Quit();
    }

    private static new string Name(Style s) => s switch
    {
        Style.Rookie => "초보", Style.Average => "평균", _ => "숙련",
    };

    private sealed class Result
    {
        public readonly float[] Day = new float[5];
        public float SuccessRate;
    }

    private Result Simulate(int cycle, Style style, int runs)
    {
        var r = new Result();
        int cleared = 0;
        var dayTotals = new float[5];

        for (int run = 0; run < runs; run++)
        {
            GameState.Instance.ResetRun(1);
            _sim.ResetRun();
            GameState.Instance.SetRecoveryCycle(cycle);
            EventLog.Instance?.ClearAll();

            // 그 판의 DAY 별 종료 복구율. 중간에 100% 가 되면 남은 DAY 도 100% 로 둔다.
            var perDay = new float[5];
            int lastPlayed = -1;
            for (int day = 1; day <= 5; day++)
            {
                if (day > 1) GameState.Instance.GoToNextDay();
                GameState.Instance.SetRecoveryCycle(cycle);
                RunShift(style);
                perDay[day - 1] = GameState.Instance.CoreProgress;
                lastPlayed = day - 1;
                if (GameState.Instance.CoreProgress >= 100f) break;
            }
            for (int d = lastPlayed + 1; d < 5; d++) perDay[d] = perDay[Mathf.Max(0, lastPlayed)];
            for (int d = 0; d < 5; d++) dayTotals[d] += perDay[d];

            if (GameState.Instance.CoreProgress >= 100f) cleared++;
        }

        for (int d = 0; d < 5; d++) r.Day[d] = dayTotals[d] / runs;
        r.SuccessRate = 100f * cleared / runs;
        return r;
    }

    // 한 DAY 근무를 끝까지 돌린다. 패턴마다 코어실 인원 · 사고 대응 속도가 다르다.
    private void RunShift(Style style)
    {
        var roster = _sim.GetActiveEmployeeIds().ToList();
        if (roster.Count == 0) return;

        int coreStaff = style switch
        {
            Style.Rookie => 1,
            Style.Average => _rng.Randf() < 0.5f ? 1 : 2,
            _ => 2,
        };

        // 배치표에 들어가는 순간 기절자는 의무실 자리를 차지한다(실제 게임과 같은 경로).
        _sim.PlaceFaintedInMedical();
        Deploy(roster, coreStaff);
        GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);

        float length = DayObjectives.MaxShiftSeconds;
        float nextReact = 0f;

        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            // 수리 승인 — 이걸 하지 않으면 수리가 영영 끝나지 않는다.
            // (DAY4~5 에 코어실이 고장난 채로 남아 복구가 0 이 되던 원인이었다.)
            HandleRepairApproval(style);

            if (t < nextReact) continue;
            // 사고 대응 — 패턴마다 반응 간격이 다르다. 코어실에서 사람을 빼서 보낸다.
            float interval = style switch { Style.Rookie => 28f, Style.Average => 16f, _ => 8f };
            nextReact = t + interval;
            RespondToIncidents(roster, style, coreStaff);
        }
    }

    // 스트레스가 높은 직원을 의무실로. 패턴마다 "언제부터 위험하다고 보는가" 가 다르다.
    private void SendStressedToMedical(List<string> roster, Style style)
    {
        var cfg = Config.Instance.Data;
        // 기절선(StressFaintFrom) 대비 어느 지점에서 치료를 보내는가.
        float trigger = style switch
        {
            Style.Rookie => cfg.StressFaintFrom * 0.92f,   // 거의 쓰러지기 직전에야 알아챈다
            Style.Average => cfg.StressFaintFrom * 0.75f,
            _ => cfg.StressFaintFrom * 0.62f,              // 숙련자는 선제적으로 보낸다
        };
        // 의무실은 한 번에 둘까지만 — 근무 인력이 통째로 빠지면 그것대로 무너진다.
        int inMedical = roster.Count(x => _sim.GetEmployeeState(x)?.AssignedRoomId == "medical_room");

        foreach (string id in roster)
        {
            if (inMedical >= 2) break;
            var st = _sim.GetEmployeeState(id);
            if (st == null || !st.Alive || st.Isolated || st.Incapacitated) continue;
            if (st.AssignedRoomId == "medical_room") continue;
            if (st.Stress < trigger) continue;
            if (_sim.AssignToRoom(id, "medical_room")) inMedical++;
        }

        // 충분히 회복했으면 다시 근무로 돌려보낸다. 비어 있는 핵심 작업실부터 메운다 —
        // 정비실(자재) · 환기실(스트레스)이 비면 그날 경제가 통째로 무너진다.
        foreach (string id in roster)
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null || st.AssignedRoomId != "medical_room" || st.Incapacitated) continue;
            if (st.Stress > cfg.StressFaintFrom * 0.45f) continue;
            string need = Priority.FirstOrDefault(r => _sim.OnDutyCount(r) == 0) ?? "core_room";
            _sim.AssignToRoom(id, need);
        }
    }

    // 관리자가 수리 승인 요청에 답한다. 미로는 BFS 로 푼다 —
    // 초보는 가끔 거절하고(수리 시간 증가), 숙련은 늘 승인하고 푼다.
    private void HandleRepairApproval(Style style)
    {
        if (RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking)
        {
            bool approve = style switch
            {
                Style.Rookie => _rng.Randf() < 0.55f,
                Style.Average => _rng.Randf() < 0.85f,
                _ => true,
            };
            if (approve) RepairApprovalSystem.Approve(); else RepairApprovalSystem.Decline();
            return;
        }

        if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Maze) return;
        var maze = RepairApprovalSystem.Maze;
        if (maze == null || maze.Failed) return;

        // 초보는 미로에서 자주 틀린다.
        float slip = style switch { Style.Rookie => 0.35f, Style.Average => 0.12f, _ => 0.03f };
        var dir = SolveStep(maze);
        if (dir == null) return;
        if (_rng.Randf() < slip)
            dir = (RepairMaze.Dir)((int)(dir.Value + 1) % 4);
        RepairApprovalSystem.MazeInput(dir.Value);
    }

    // 지금 칸에서 목표로 가는 첫 걸음(BFS).
    private static RepairMaze.Dir? SolveStep(RepairMaze m)
    {
        var dirs = new[] { RepairMaze.Dir.Up, RepairMaze.Dir.Down, RepairMaze.Dir.Left, RepairMaze.Dir.Right };
        var delta = new Dictionary<RepairMaze.Dir, Vector2I>
        {
            [RepairMaze.Dir.Up] = new(0, -1), [RepairMaze.Dir.Down] = new(0, 1),
            [RepairMaze.Dir.Left] = new(-1, 0), [RepairMaze.Dir.Right] = new(1, 0),
        };
        var prev = new Dictionary<Vector2I, (Vector2I From, RepairMaze.Dir Dir)>();
        var seen = new HashSet<Vector2I> { m.Cursor };
        var q = new Queue<Vector2I>();
        q.Enqueue(m.Cursor);

        while (q.Count > 0)
        {
            var at = q.Dequeue();
            if (at == m.Goal)
            {
                var cur = at;
                while (prev[cur].From != m.Cursor) cur = prev[cur].From;
                return prev[cur].Dir;
            }
            foreach (var d in dirs)
            {
                if (!m.IsOpen(at, d)) continue;
                var nx = at + delta[d];
                if (!seen.Add(nx)) continue;
                prev[nx] = (at, d);
                q.Enqueue(nx);
            }
        }
        return null;
    }

    // 6명으로 7개 방을 다 채울 수 없다 — 실제 플레이어처럼 **우선순위**로 배치한다.
    // 정비실(자재)과 환기실(스트레스)을 비우면 그날 경제가 무너진다는 것을 측정으로 확인했다.
    private static readonly string[] Priority =
        { "maintenance_room", "vent_room", "power_room", "guard_room", "storage_room" };

    private void Deploy(List<string> roster, int coreStaff)
    {
        var free = roster.Where(id => _sim.GetEmployeeState(id) is { Alive: true, Isolated: false }
                                      && !_sim.MustStayInMedical(id)).ToList();
        int i = 0;
        for (; i < coreStaff && i < free.Count; i++) _sim.AssignToRoom(free[i], "core_room");
        for (int k = 0; i < free.Count; i++, k++)
            _sim.AssignToRoom(free[i], Priority[k % Priority.Length]);
    }

    // 고장 난 방에 사람을 보낸다. 초보는 코어실에서 빼 오고 제때 돌려놓지 못한다.
    private void RespondToIncidents(List<string> roster, Style style, int coreStaff)
    {
        var broken = SideRooms.Concat(new[] { "core_room" })
            .Where(rm => _sim.HasRepairPending(rm)).ToList();

        // 스트레스가 높은 직원을 의무실로 선제적으로 보낸다. 실제 플레이어가 하는 일이고,
        // 이걸 하지 않으면 DAY4 에 전원이 쓰러진다(측정으로 확인).
        SendStressedToMedical(roster, style);

        if (broken.Count == 0)
        {
            // 숙련 · 평균은 비는 대로 코어실 인원을 되돌린다.
            if (style == Style.Rookie) return;
            int inCore = _sim.OnDutyCount("core_room");
            if (inCore >= coreStaff) return;
            // 이미 코어실로 배치된 사람을 다시 배치하면 가던 길이 초기화되어 영영 도착하지
            // 못한다(이 하네스가 처음에 그 버그로 "평균이 초보보다 못한" 결과를 냈다).
            int heading = roster.Count(x => _sim.GetEmployeeState(x)?.AssignedRoomId == "core_room");
            foreach (string id in roster)
            {
                if (heading >= coreStaff) break;
                var st = _sim.GetEmployeeState(id);
                if (st == null || st.AssignedRoomId == "core_room" || st.Incapacitated || st.Isolated) continue;
                if (_sim.AssignToRoom(id, "core_room")) heading++;
            }
            return;
        }

        foreach (string room in broken)
        {
            int need = RoomStaffing.RepairMinWorkers(room, _sim.GetRoomDef(room));
            // 이미 그 방으로 가고 있는 사람까지 센다 — 도착 전에 또 보내면 길이 초기화된다.
            int sent = roster.Count(x => _sim.GetEmployeeState(x)?.AssignedRoomId == room);
            if (sent >= need) continue;
            foreach (string id in roster)
            {
                if (sent >= need) break;
                var st = _sim.GetEmployeeState(id);
                if (st == null || st.Incapacitated || st.Isolated) continue;
                if (st.AssignedRoomId == room) continue;
                // 숙련자는 코어실 인원을 마지막에만 뺀다.
                if (style == Style.Veteran && st.AssignedRoomId == "core_room"
                    && _sim.OnDutyCount("core_room") <= 1) continue;
                if (_sim.AssignToRoom(id, room)) sent++;
            }
        }
    }
    // ── 코어 증감 내역 — 시도량 · 반영량 · 막힌 사유를 DAY 별로 ──────────────
    //
    // 추측으로 고치지 않기 위한 계측이다. "복구가 사라진다" 가 정말 사고 때문인지,
    // 아니면 이미 100% 에 닿아 잘린 것인지(Clamp) 부터 가린다.
    private async System.Threading.Tasks.Task LedgerBreakdown()
    {
        GD.Print("\n══════ 코어 획득 분해 (1차 · 평균 패턴 · 20판) ══════");

        const int runs = 20;
        var attempted = new float[5];     // 복구가 성립했다면 들어왔을 양
        var applied = new float[5];       // 실제로 코어에 더해진 양
        var clamped = new float[5];       // 이미 100% 라 잘린 양
        var lost = new Dictionary<string, float[]>();
        var blocked = new Dictionary<string, float[]>();
        float[] Bucket(Dictionary<string, float[]> d, string k)
        {
            if (!d.TryGetValue(k, out var a)) d[k] = a = new float[5];
            return a;
        }

        int dayIdx = 0;
        GameState.CoreLedger = (delta, reason) =>
        {
            float before = GameState.Instance.CoreProgress;
            float after = Mathf.Clamp(before + delta, 0f, 100f);
            float real = after - before;
            if (delta >= 0)
            {
                attempted[dayIdx] += delta;
                applied[dayIdx] += real;
                clamped[dayIdx] += delta - real;
            }
            else Bucket(lost, reason)[dayIdx] += -real;
        };
        FacilitySimulation.CoreGainProbe = (amount, reason) =>
        {
            attempted[dayIdx] += amount;
            Bucket(blocked, reason)[dayIdx] += amount;
        };

        for (int run = 0; run < runs; run++)
        {
            GameState.Instance.ResetRun(1);
            _sim.ResetRun();
            GameState.Instance.SetRecoveryCycle(1);
            EventLog.Instance?.ClearAll();
            for (int day = 1; day <= 5; day++)
            {
                if (day > 1) GameState.Instance.GoToNextDay();
                GameState.Instance.SetRecoveryCycle(1);
                dayIdx = day - 1;
                RunShift(Style.Average);
            }
        }
        GameState.CoreLedger = null;
        FacilitySimulation.CoreGainProbe = null;

        for (int d = 0; d < 5; d++)
        {
            GD.Print($"  DAY{d + 1}  시도 +{attempted[d] / runs,6:0.0}   반영 +{applied[d] / runs,6:0.0}" +
                     $"   100%초과로잘림 {clamped[d] / runs,5:0.0}");
            foreach (var kv in blocked.OrderByDescending(k => k.Value[d]))
                if (kv.Value[d] > 0.05f) GD.Print($"          막힘 {kv.Key,-16} {kv.Value[d] / runs,6:0.0}");
            foreach (var kv in lost.OrderByDescending(k => k.Value[d]))
                if (kv.Value[d] > 0.05f) GD.Print($"          손실 {kv.Key,-16} -{kv.Value[d] / runs,5:0.0}");
        }
        GD.Print($"  합계  시도 +{attempted.Sum() / runs:0.0}  반영 +{applied.Sum() / runs:0.0}" +
                 $"  잘림 {clamped.Sum() / runs:0.0}" +
                 $"  막힘 {blocked.Values.Sum(a => a.Sum()) / runs:0.0}" +
                 $"  손실 -{lost.Values.Sum(a => a.Sum()) / runs:0.0}");

        // DAY 별로 코어가 왜 안 도는지 — 시도량이 DAY4~5 에 0 에 가까워지는 이유.
        GD.Print("\n  DAY 별 코어실 시간 배분 (한 판)");
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetRecoveryCycle(1);
        EventLog.Instance?.ClearAll();
        for (int day = 1; day <= 5; day++)
        {
            if (day > 1) GameState.Instance.GoToNextDay();
            GameState.Instance.SetRecoveryCycle(1);

            var roster = _sim.GetActiveEmployeeIds().ToList();
            Deploy(roster, 2);
            GameState.Instance.AssignRandomSaboteur(roster);
            _sim.ResetForNewShift();
            GameState.Instance.SetPhase(GamePhase.Live);

            int work = 0, noStaff = 0, noMat = 0, brokenT = 0, unstable = 0, total = 0, ventUp = 0;
            _matMade = _matUsed = _matDiscard = 0;
            FacilitySimulation.MaterialProbe = (n, kind) =>
            {
                if (kind == "produced") _matMade += n; else _matUsed += n;
            };
            GameState.MaterialFlowProbe = (n, kind) =>
            {
                if (kind == "cap_discard") _matDiscard += n;
            };
            float len = DayObjectives.MaxShiftSeconds;
            for (float t = 0f; t < len; t += Step)
            {
                GameState.Instance.AdvanceDayTime(Step);
                _sim.Tick(Step);
                HandleRepairApproval(Style.Average);
                if (Mathf.Abs(t % 16f) < Step * 0.5f) RespondToIncidents(roster, Style.Average, 2);

                total++;
                if (_sim.OnDutyCount("vent_room") > 0 && !_sim.HasRepairPending("vent_room")) ventUp++;
                if (GameState.Instance.CoreOutputUnstable) unstable++;
                else if (_sim.HasRepairPending("core_room")) brokenT++;
                else if (_sim.OnDutyCount("core_room") <= 0) noStaff++;
                else if (_sim.IsRoomBlockedByMaterials("core_room")) noMat++;
                else work++;
            }
            int alive = roster.Count(x => _sim.GetEmployeeState(x) is { Alive: true, Incapacitated: false, Isolated: false });
            int dead = roster.Count(x => _sim.GetEmployeeState(x)?.Alive == false);
            int faint = roster.Count(x => _sim.GetEmployeeState(x) is { Alive: true, Incapacitated: true });
            int iso = roster.Count(x => _sim.GetEmployeeState(x) is { Alive: true, Isolated: true });
            GD.Print($"    DAY{day}  복구 {100f * work / total,5:0.0}%  인력없음 {100f * noStaff / total,5:0.0}%" +
                     $"  자재없음 {100f * noMat / total,5:0.0}%  코어사고 {100f * brokenT / total,5:0.0}%" +
                     $"  출력불안정 {100f * unstable / total,5:0.0}%");
            GD.Print($"           가용 {alive}/{roster.Count}  (사망 {dead} · 기절 {faint} · 격리 {iso})" +
                     $"  자재 생산 {_matMade} · 소비 {_matUsed} · 한도폐기 {_matDiscard} · 남음 {GameState.Instance.Materials}/{GameState.Instance.MaterialsCap}" +
                     $"  환기가동 {100f * ventUp / total,4:0}%");
            _matMade = _matUsed = _matDiscard = 0;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ── DAY 별 배율 자동 탐색 ───────────────────────────────────────────────
    //
    // 5차원 격자를 전부 도는 대신 **앞 DAY 부터 차례로** 맞춘다. DAY N 의 배율은
    // DAY N 이후의 누적에만 영향을 주므로, 앞을 확정하고 다음으로 넘어가면 된다.
    // 하루 상한(cap)은 만들지 않는다 — 속도만 조절해 곡선을 맞춘다.
    private async System.Threading.Tasks.Task Calibrate()
    {
        // 평균 플레이 기준 목표(각 DAY 종료 누적). 과제의 '평균' 구간 한가운데.
        float[] target = { 18f, 40f, 61f, 86f, 100f };
        float[] found = { 1f, 1f, 1f, 1f, 1f };
        const int runs = 14;

        var prof = RecoveryProfile.ForCycle(1);
        GD.Print("\n══════ DAY 배율 자동 탐색 (1차 · 평균 기준) ══════");

        for (int day = 1; day <= 5; day++)
        {
            float lo = 0.05f, hi = 2.0f, best = 1f, bestErr = float.MaxValue;
            // 이분 탐색 — 그 DAY 종료 누적이 목표에 닿는 배율.
            for (int iter = 0; iter < 7; iter++)
            {
                float mid = (lo + hi) * 0.5f;
                found[day - 1] = mid;
                prof.DayRecoveryMultiplier = (float[])found.Clone();

                float avg = AverageAt(day, runs);
                float err = Mathf.Abs(avg - target[day - 1]);
                if (err < bestErr) { bestErr = err; best = mid; }
                if (avg > target[day - 1]) hi = mid; else lo = mid;
            }
            found[day - 1] = best;
            prof.DayRecoveryMultiplier = (float[])found.Clone();
            GD.Print($"  DAY{day} ×{best:0.00}  (목표 {target[day - 1]:0} · 실측 {AverageAt(day, runs):0.0})");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GD.Print($"\n  → DayRecoveryMultiplier = PackedFloat32Array(" +
                 $"{found[0]:0.00}, {found[1]:0.00}, {found[2]:0.00}, {found[3]:0.00}, {found[4]:0.00})");
    }

    // 평균 패턴으로 DAY1~day 를 돌렸을 때 그 DAY 종료 시점의 평균 코어.
    private float AverageAt(int day, int runs)
    {
        float sum = 0f;
        for (int run = 0; run < runs; run++)
        {
            GameState.Instance.ResetRun(1);
            _sim.ResetRun();
            GameState.Instance.SetRecoveryCycle(1);
            EventLog.Instance?.ClearAll();
            for (int d = 1; d <= day; d++)
            {
                if (d > 1) GameState.Instance.GoToNextDay();
                GameState.Instance.SetRecoveryCycle(1);
                RunShift(Style.Average);
            }
            sum += GameState.Instance.CoreProgress;
        }
        return sum / runs;
    }

    // ── DAY3 에 8% 로 크게 뒤처진 판이 DAY5 까지 회복 가능한가 ──────────────
    private async System.Threading.Tasks.Task Day3RecoveryCase()
    {
        GD.Print("\n══════ DAY3 8% 케이스 — 아직 회복 가능한가 ══════");

        foreach (int cycle in new[] { 1, 2, 3 })
        {
            int cleared = 0, runs = 50;
            float sum = 0f;
            for (int run = 0; run < runs; run++)
            {
                GameState.Instance.ResetRun(1);
                _sim.ResetRun();
                GameState.Instance.SetRecoveryCycle(cycle);
                EventLog.Instance?.ClearAll();

                // DAY3 시작 · 코어 8% 인 상태를 직접 만든다.
                GameState.Instance.GoToNextDay();
                GameState.Instance.GoToNextDay();
                GameState.Instance.SetRecoveryCycle(cycle);
                GameState.Instance.AddCoreProgress(8f - GameState.Instance.CoreProgress, "테스트 시작값");

                for (int day = 3; day <= 5; day++)
                {
                    if (day > 3) GameState.Instance.GoToNextDay();
                    GameState.Instance.SetRecoveryCycle(cycle);
                    // 이 시점부터는 "이해하고 제대로 하는" 플레이로 가정한다.
                    RunShift(Style.Average);
                    if (GameState.Instance.CoreProgress >= 100f) break;
                }
                sum += GameState.Instance.CoreProgress;
                if (GameState.Instance.CoreProgress >= 100f) cleared++;
            }
            var p = RecoveryProfile.ForCycle(cycle);
            GD.Print($"  {cycle}차 {p.DisplayName,-14} 평균 최종 {sum / runs,5:0.0}%   100% 달성 {100f * cleared / runs,5:0.0}%");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
