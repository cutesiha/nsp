using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.Debug;

// DAY3 플레이테스트 수정 최종 검수.
//
//   godot --headless --path . res://scenes/debug/PlaytestAuditTest.tscn --quit-after 20000
//
// 지시서의 검수 항목을 그대로 본다.
//   파트 1 — ① 스트레스 구간 알림 ② 기절 표시 ③ 격리 위치 ④ ×n 배지 없음
//   파트 2 — 관리자 입력이 필요한 이벤트가 하루당 몇 번인가
public partial class PlaytestAuditTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;
    private int _pass, _fail;
    private readonly List<(string Text, NoticeLevel Level)> _notices = new();

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 최종 검수 : DAY3 플레이테스트 수정 ################");
        FacilityAlertHud.Noticed += OnNotice;

        CheckStressAlert();
        CheckFaintShown();
        CheckIsolatedPlace();
        CheckNoConfrontBadges();
        CountManagerInputs();

        FacilityAlertHud.Noticed -= OnNotice;
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private void OnNotice(string text, NoticeLevel level) => _notices.Add((text, level));

    // ── ① 스트레스 구간 알림이 최소 1회 ────────────────────────────────
    private void CheckStressAlert()
    {
        Head("①", "근무 중 스트레스 구간 알림이 최소 1회 뜬다");
        Reset(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        Check(DayFeatures.StressEnabled, "DAY2 에 스트레스가 켜져 있다");

        _notices.Clear();
        // 한 판을 그대로 돌린다 — 야간 피로만으로도 구간을 넘는 사람이 나온다.
        var rooms = _sim.GetRoomIds().Where(r => _sim.GetRoomDef(r)?.IsRestricted == false).ToList();
        int i = 0;
        foreach (string id in _sim.GetActiveEmployeeIds()) _sim.AssignToRoom(id, rooms[i++ % rooms.Count]);
        for (float t = 0f; t < Config.Instance.Data.DayLengthSeconds; t += Step)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
        }

        var stress = _notices.Where(n => n.Text.Contains("스트레스") || n.Text.Contains("기절 임박")).ToList();
        foreach (var n in stress.Take(4)) GD.Print($"   알림: [{n.Level}] {n.Text}");
        Check(stress.Count >= 1, $"스트레스 관련 알림 {stress.Count}회");
    }

    // ── ② 기절하면 배치 화면이 읽는 값에 뜬다 ──────────────────────────
    private void CheckFaintShown()
    {
        Head("②", "기절하면 배치 화면이 '기절 · 근무 불가' 로 읽는다");
        Reset(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        var st = _sim.GetEmployeeState("cat");
        st.Stress = Config.Instance.Data.StressFaintFrom - 0.5f;
        _sim.AddStress("cat", 1f);

        Check(st.Incapacitated, "Incapacitated 가 선다");
        Check(_sim.StressBandName(st) == "기절", $"구간 이름 = 기절 ({_sim.StressBandName(st)})");
        // 배치 화면은 이 두 값으로 카드를 그린다(ScheduleStaffView / ScheduleMapView).
        Check(Mathf.IsZeroApprox(_sim.StressWorkRate(st)), "작업 효율 0");
    }

    // ── ③ 격리 직원은 다음 날에도 격리실 ───────────────────────────────
    private void CheckIsolatedPlace()
    {
        Head("③", "격리 직원의 위치가 격리실이다");
        Reset(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        _sim.AssignToRoom("sheep", "generator_room");
        for (int i = 0; i < 3000 && _sim.GetEmployeeState("sheep").CurrentRoomId != "generator_room"; i++)
            _sim.Tick(Step);

        _sim.IsolateEmployee("sheep");
        var st = _sim.GetEmployeeState("sheep");
        for (int i = 0; i < 3000 && st.Isolation != IsolationPhase.InRoom; i++) _sim.Tick(Step);
        Check(st.Isolation == IsolationPhase.InRoom, "오늘 격리실에 수용됐다");

        GameState.Instance.GoToNextDay();
        _sim.ResetForNewShift();
        Check(st.Isolated && st.CurrentRoomId == "isolation_room",
            $"다음 날 위치 = 격리실 ({st.CurrentRoomId})");
    }

    // ── ④ 휴게 화면에 ×n 배지가 없다 ───────────────────────────────────
    private void CheckNoConfrontBadges()
    {
        Head("④", "휴게 화면에 '진술 흔들림 ×n' 배지가 없다");
        // 그리는 코드 자체가 사라졌는지 본다 — 문구를 찾는 것보다 확실하다.
        var view = typeof(NSP.View.RestRosterView);
        var all = view.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Concat(new[] { view }).ToList();
        bool draws = all.Any(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public
                                               | BindingFlags.Instance | BindingFlags.Static)
            .Any(m => m.Name is "DrawConfrontMarks" or "MarkLine"));
        Check(!draws, "배지를 그리는 코드가 없다");
        // 세는 것 자체는 남아 있다(다른 곳에서 쓸 수 있게).
        NSP.View.ConfrontMarks.Clear();
        Check(NSP.View.ConfrontMarks.Shaken("cat") == 0, "집계는 그대로 남아 있다");
    }

    // ── 파트 2 : 관리자 입력이 필요한 이벤트가 하루에 몇 번인가 ─────────
    private void CountManagerInputs()
    {
        Head("파트2", "관리자 입력이 필요한 이벤트 — 하루당 몇 회인가");
        Reset(3);
        GameState.Instance.SetPhase(GamePhase.Live);

        int approvals = 0, rotations = 0, stays = 0;
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();

        var rooms = _sim.GetRoomIds().Where(r => _sim.GetRoomDef(r)?.IsRestricted == false).ToList();
        // 절반만 배치한다 — 빈 방이 생겨야 무인 경고(체류 대기)가 실제로 걸린다.
        var ids = _sim.GetActiveEmployeeIds().Take(3).ToList();
        for (int i = 0; i < ids.Count; i++) _sim.AssignToRoom(ids[i], rooms[i]);

        var seenApproval = new HashSet<string>();
        var seenStay = new HashSet<string>();
        bool rotationSeen = false;

        for (float t = 0f; t < Config.Instance.Data.DayLengthSeconds; t += Step)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);

            // 승인 요청 — 뜬 것만 센다(답은 하지 않는다 = 무응답으로 흘러간다).
            if (RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking
                && RepairApprovalSystem.Active != null
                && seenApproval.Add(RepairApprovalSystem.Active.RoomId)) approvals++;

            // 순환 배치 — 하루 한 번.
            if (!rotationSeen && RotationOrderSystem.Current == RotationOrderSystem.Phase.Waiting)
            { rotationSeen = true; rotations++; }

            // 체류 대기 — 무인 경고가 걸린 방(사람을 보내 머물게 해야 풀린다).
            foreach (string r in rooms)
            {
                var rs = _sim.GetRoomState(r);
                if (rs != null && rs.UnstaffedTimer > 1f) seenStay.Add(r);
            }
        }
        stays = seenStay.Count;

        int total = approvals + rotations + stays;
        GD.Print($"   수리 승인 {approvals}회 · 순환 배치 {rotations}회 · 체류 대기 {stays}건 = 하루 {total}회");
        Check(total >= 4, $"관리자 입력이 필요한 이벤트가 하루 4회 이상 ({total}회)");
    }

    // --- 도우미 ---------------------------------------------------------

    private void Reset(int day)
    {
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        _notices.Clear();
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
