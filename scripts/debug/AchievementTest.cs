using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 도전과제 시스템 자동 검증.
//
//   godot --headless --path . res://scenes/debug/AchievementTest.tscn --quit-after 2500
//
// 저장 경로를 user://achievements_test.cfg 로 돌려 두고 돌린다 — 실제 플레이 기록
// (achievements_v1.cfg)은 한 글자도 건드리지 않는다.
//
// 가능한 곳은 **실제 시스템을 돌려서** 확인한다.
//   수리 승인       RepairApprovalSystem 을 진짜로 열고 거절 · 미로 실패를 만든다
//   이상 개체       GhostHauntSystem.ForceAppear → 방치 → Strike
//   격리            FacilitySimulation.IsolateEmployee → 격리실 수용까지 Tick
//   코어 100%       GameState.AddCoreProgress (DAY0 / DAY1 양쪽)
// 화면(전화기 · 시설 로그 창)처럼 3D 씬이 있어야 하는 곳은 같은 보고 함수를 직접 부른다
// (그 함수를 어디서 부르는지는 코드에 한 줄로 붙어 있고 빌드가 지킨다).
public partial class AchievementTest : Node
{
    private const string TestStore = "user://achievements_test.cfg";
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;
    private AchievementManager _m;
    private int _pass, _fail;
    private readonly List<string> _toasts = new();

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        _m = AchievementManager.Instance;
        if (_sim == null || _m == null)
        {
            GD.PrintErr("FacilitySimulation / AchievementManager 를 찾지 못했습니다.");
            return;
        }
        AchievementManager.Sandbox = false;
        AchievementManager.StorePath = TestStore;
        AchievementManager.AchievementUnlocked += OnToast;
        CallDeferred(nameof(RunAll));
    }

    private void OnToast(AchievementDefinition def) => _toasts.Add(def.Id);

    private void RunAll()
    {
        GD.Print("\n\n################ 도전과제 시스템 검증 ################");
        Wipe();

        SectionA();
        SectionB();
        SectionC();
        SectionD();
        SectionE();
        SectionF();
        SectionG();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        AchievementManager.AchievementUnlocked -= OnToast;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestStore));
        GetTree().Quit();
    }

    // ── A. 목록 ─────────────────────────────────────────────────────

    private void SectionA()
    {
        Head("A", "도전과제 30개 정의");
        var all = Achievements.All;
        Ok(all.Count == 30, $"전체 30개 ({all.Count}개)");
        Ok(all.Count(a => a.Hidden) == 6, $"숨겨진 도전과제 6개 ({all.Count(a => a.Hidden)}개)");
        Ok(all.Select(a => a.Id).Distinct().Count() == all.Count, "영문 ID 가 전부 다르다");
        Ok(all.Select((a, i) => a.No == i + 1).All(x => x), "표시 번호가 1~30 연속이다");
        Ok(all.All(a => a.Name.Length > 0 && a.Condition.Length > 0), "이름과 조건 문구가 전부 채워져 있다");

        // 지시서가 정한 숨김 여부 — 이 여섯 개만 숨김이어야 한다.
        string[] hidden =
        {
            Achievements.SuspiciousCctv, Achievements.InnocentIsolation, Achievements.GradeLowest,
            Achievements.TutorialCore100, Achievements.TrueEnding, Achievements.BadEnding,
        };
        Ok(hidden.All(id => Achievements.Get(id)?.Hidden == true)
           && all.Where(a => a.Hidden).All(a => hidden.Contains(a.Id)),
            "숨김 목록이 지시서와 같다 (누군가의 흔적 · 억울한 격리 · 쓸모없는 관리자 · 이게 연습이라고? · 트루 · 배드)");

        Ok(Achievements.Get(Achievements.MazeFailThree)?.Name == "부러진 손가락"
           && Achievements.Get(Achievements.GradeLowest)?.Name == "쓸모없는 관리자",
            "이름이 지시서 그대로다 (부러진 손가락 · 쓸모없는 관리자)");
    }

    // ── B. 저장 / 불러오기 ───────────────────────────────────────────

    private void SectionB()
    {
        Head("B", "영구 저장 · 회차 초기화");
        Wipe();

        _m.Unlock(Achievements.FirstShift);
        _m.IncrementCounter(Achievements.CounterMazeFail);
        _m.IncrementCounter(Achievements.CounterMazeFail);
        Ok(_m.GetUnlockedCount() == 1 && _m.GetCounter(Achievements.CounterMazeFail) == 2,
            "해금 1개 · 누적 카운터 2");

        // 게임을 껐다 다시 켠다.
        _m.ReloadForTest();
        Ok(_m.IsUnlocked(Achievements.FirstShift), "재실행 후에도 해금이 남아 있다");
        Ok(_m.GetCounter(Achievements.CounterMazeFail) == 2, "재실행 후에도 누적 카운터가 남아 있다");

        // 새 판을 시작해도 영구 기록은 그대로, 회차 기록만 비워진다.
        for (int i = 0; i < 3; i++) _m.NoteCallConnected($"x{i}", false);
        GameState.Instance.ResetRun(1);
        Ok(_m.IsUnlocked(Achievements.FirstShift) && _m.GetCounter(Achievements.CounterMazeFail) == 2,
            "새 게임을 시작해도 영구 기록이 유지된다");
        for (int i = 0; i < 5; i++) _m.NoteCallConnected($"y{i}", false);
        Ok(!_m.IsUnlocked(Achievements.TalkToSix),
            "회차 대화 기록은 초기화됐다 (3+5명이어도 여섯 명 업적이 뜨지 않는다)");
        _m.NoteCallConnected("y5", false);
        Ok(_m.IsUnlocked(Achievements.TalkToSix), "같은 회차에서 여섯 명을 채우면 해금된다");

        // 손상된 파일.
        using (var f = FileAccess.Open(TestStore, FileAccess.ModeFlags.Write))
            f?.StoreString("이건 설정 파일이 아니다\n\0\x01rubbish");
        _m.ReloadForTest();
        Ok(_m.GetUnlockedCount() == 0, "손상된 저장 파일을 만나도 멈추지 않고 빈 기록으로 시작한다");

        // 파일이 아예 없을 때.
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestStore));
        _m.ReloadForTest();
        Ok(_m.GetUnlockedCount() == 0, "저장 파일이 없어도 안전하게 시작한다");
        _m.Unlock(Achievements.FirstLog);
        Ok(FileAccess.FileExists(TestStore), "첫 해금에서 저장 파일이 새로 만들어진다");
    }

    // ── C. 중복 방지 / 팝업 큐 ───────────────────────────────────────

    private void SectionC()
    {
        Head("C", "중복 방지 · 팝업 큐");
        Wipe();

        _toasts.Clear();
        Ok(_m.Unlock(Achievements.FirstShift), "첫 해금은 true 를 돌려준다");
        Ok(!_m.Unlock(Achievements.FirstShift), "두 번째 해금은 false (중복 처리 안 됨)");
        Ok(_toasts.Count == 1, $"팝업 통지도 한 번뿐이다 ({_toasts.Count}회)");

        _toasts.Clear();
        var toast = AchievementToast.Instance;
        toast?.ClearForTest();
        _m.Unlock(Achievements.FirstLog);
        _m.Unlock(Achievements.FirstInterview);
        _m.Unlock(Achievements.OutgoingCall);
        Ok(toast != null, "AchievementToast autoload 이 떠 있다");
        Ok(_toasts.Count == 3 && toast?.Pending == 3,
            $"세 개가 동시에 해금되면 큐에 세 개가 쌓인다 (큐 {toast?.Pending})");

        Ok(!_m.Unlock("존재하지_않는_업적"), "모르는 id 는 조용히 무시된다");
    }

    // ── D. 수리 승인 (실제 시스템) ───────────────────────────────────

    private void SectionD()
    {
        Head("D", "수리 승인 — 미로 실패와 거절을 가른다");
        Wipe();
        StartShift(1);

        // ① 거절 — 미로에 들어가지 않았다.
        OpenApproval();
        RepairApprovalSystem.Decline();
        Ok(_m.IsUnlocked(Achievements.RepairRequestIgnored), "[아니오] → 모른 척하기도 업무입니다");
        Ok(!_m.IsUnlocked(Achievements.MazeFailOnce), "거절은 미로 실패로 세지 않는다");
        Ok(_m.GetCounter(Achievements.CounterMazeFail) == 0, "거절은 미로 실패 누적에도 들어가지 않는다");

        // ② 무응답 — 제한 시간을 흘려보낸다.
        Settle();
        OpenApproval();
        for (int i = 0; i < 400 && RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking; i++)
            RepairApprovalSystem.Tick(0.1f);
        Ok(_m.GetCounter(Achievements.CounterMazeFail) == 0, "무응답도 미로 실패가 아니다");

        // ③ 미로 실패 세 번 — 1회와 3회가 따로 뜬다.
        for (int n = 1; n <= 3; n++)
        {
            Settle();
            FailMazeOnce();
            if (n == 1)
            {
                Ok(_m.IsUnlocked(Achievements.MazeFailOnce), "미로 1회 실패 → 하찮은 관리자!");
                Ok(!_m.IsUnlocked(Achievements.MazeFailThree), "1회로는 부러진 손가락이 뜨지 않는다");
            }
            if (n == 2) Ok(!_m.IsUnlocked(Achievements.MazeFailThree), "2회로도 뜨지 않는다");
        }
        Ok(_m.GetCounter(Achievements.CounterMazeFail) == 3, $"미로 실패 누적 3 ({_m.GetCounter(Achievements.CounterMazeFail)})");
        Ok(_m.IsUnlocked(Achievements.MazeFailThree), "누적 3회 → 부러진 손가락");
        Settle();
    }

    // 승인 요청 하나를 실제로 띄운다.
    private void OpenApproval()
    {
        var task = new SpawnedTask { TaskId = "t", RoomId = "core_room", IsRepair = true, GaugeRequired = 10f };
        RepairApprovalSystem.Enqueue("core_room", "코어실", task);
        for (int i = 0; i < 20 && RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking; i++)
            RepairApprovalSystem.Tick(Step);
    }

    // 승인하고 미로에서 틀린다. 첫 입력이 벽이면 그대로 실패, 아니면 되돌아가 실패한다 —
    // 어느 미로가 나와도 두 번 안에 끝난다.
    private void FailMazeOnce()
    {
        OpenApproval();
        RepairApprovalSystem.Approve();
        BreakMaze();
    }

    // 떠 있는 미로를 틀려서 끝낸다. 첫 입력이 벽이면 거기서, 아니면 되돌아가며 끝난다.
    // **반드시 끝내야 한다** — 미로는 첫 입력 전에는 제한 시간이 흐르지 않아서,
    // 열어 둔 채 방치하면 그 수리가 영원히 승인 대기에 묶인다.
    private static void BreakMaze()
    {
        foreach (var d in new[] { RepairMaze.Dir.Up, RepairMaze.Dir.Down, RepairMaze.Dir.Left })
        {
            if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Maze) return;
            RepairApprovalSystem.MazeInput(d);
        }
    }

    private static void Settle()
    {
        for (int i = 0; i < 80 && RepairApprovalSystem.Busy; i++) RepairApprovalSystem.Tick(0.1f);
    }

    // ── E. 이상 개체 · 기절 · 격리 (실제 시스템) ─────────────────────

    private void SectionE()
    {
        Head("E", "이상 개체 · 기절 · 격리");
        Wipe();
        StartShift(1);

        // 이상 개체를 띄우고 **보지 않는다** — 유예 시간이 지나면 사고가 된다.
        Ok(_sim.Ghost.ForceAppear("maintenance_room", _sim, 2f), "이상 개체가 정비실에 떴다");
        for (int i = 0; i < 300 && _sim.Ghost.Active; i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        Ok(_sim.Ghost.StruckToday > 0, "방치해서 사고가 됐다");
        Ok(_m.IsUnlocked(Achievements.GhostIgnored), "방치 → 시크한 관리자");
        Ok(!_m.IsUnlocked(Achievements.GhostDispelled), "소멸시키지 않았으니 눈 마주쳤다는 뜨지 않는다");

        // 이번에는 **계속 보고 있는다** — CCTV 로 그 방을 띄워 두면 소멸한다.
        Wipe();
        StartShift(1);
        _sim.Ghost.ForceAppear("guard_room", _sim, 9999f);
        _sim.SetSurveillanceTarget("guard_room");
        _sim.ReportCctvFeedLive(true);
        for (int i = 0; i < 900 && _sim.Ghost.Active; i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        Ok(_sim.Ghost.DispelledToday > 0, $"끝까지 봐서 소멸시켰다 (소멸 {_sim.Ghost.DispelledToday}건)");
        Ok(_m.IsUnlocked(Achievements.GhostDispelled), "관측 소멸 → 눈 마주쳤다");
        Ok(!_m.IsUnlocked(Achievements.GhostIgnored), "이번에는 시크한 관리자가 뜨지 않는다");

        // 경고 방치 — 실제 경고가 떠서 고장으로 넘어가는 것을 기다린다.
        // (여기서 보는 것은 FacilityWarningSystem.Neglected 구독이 실제로 이어져 있는가다.)
        Wipe();
        StartShift(1);
        float dayLength = Config.Instance.Data.DayLengthSeconds;
        for (float t = 0f; t < dayLength && _sim.Warnings.Failed == 0; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        if (_sim.Warnings.Failed > 0)
            Ok(_m.IsUnlocked(Achievements.WarningNeglected),
                $"경고를 방치해 고장이 났다 ({_sim.Warnings.Failed}건) → 경고는 장식인가요?");
        else
            GD.Print("   SKIP  이번 근무에서는 경고가 고장까지 가지 않았다(무작위)");

        // 설비 수리 완료 — 사고를 하나 내고, 사람을 붙여 실제로 고쳐 본다.
        Wipe();
        StartShift(1);
        _sim.TriggerWarningFailure("maintenance_room", "검사용 고장");
        Ok(_sim.HasRepairPending("maintenance_room"), "정비실에 수리가 걸렸다");
        foreach (string id in _sim.GetActiveEmployeeIds()) _sim.AssignToRoom(id, "maintenance_room");
        for (float t = 0f; t < dayLength * 4f && _sim.HasRepairPending("maintenance_room"); t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            // 승인 요청이 뜨면 승인하고 미로를 끝낸다(틀려도 수리는 느려질 뿐 진행된다).
            if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking) continue;
            RepairApprovalSystem.Approve();
            BreakMaze();
        }
        Ok(!_sim.HasRepairPending("maintenance_room"), "사람을 붙여 수리를 끝냈다");
        Ok(_m.IsUnlocked(Achievements.FirstFacilityRepair), "수리 완료 → 고쳐 쓰면 그만");

        // 전력 복구 — 발전기 사고로 용량이 깎인 뒤 발전실 점검을 끝내야 한다.
        Wipe();
        StartShift(1);
        Ok(!_m.IsUnlocked(Achievements.PowerRestored), "시작할 때는 전력 업적이 없다");
        int full = GameState.Instance.PowerCapacity;
        GameState.Instance.TriggerPowerAccident(2);
        Ok(GameState.Instance.PowerCapacity < full, $"발전기 사고로 전력 {full} → {GameState.Instance.PowerCapacity}");
        foreach (string id in _sim.GetActiveEmployeeIds()) _sim.AssignToRoom(id, "power_room");
        for (float t = 0f; t < dayLength * 4f && GameState.Instance.IsPowerAccidentActive(); t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            if (RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking) continue;
            RepairApprovalSystem.Approve();
            BreakMaze();
        }
        Ok(!GameState.Instance.IsPowerAccidentActive(), $"발전실 점검으로 전력 {GameState.Instance.PowerCapacity} 복구");
        Ok(_m.IsUnlocked(Achievements.PowerRestored), "전력 정상 복구 → 다시 켜진 불빛");

        // 스위치를 껐다 켜는 것은 용량을 바꾸지 않는다 — 업적도 뜨지 않는다.
        Wipe();
        StartShift(1);
        GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
        GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
        Ok(!_m.IsUnlocked(Achievements.PowerRestored), "스위치만 껐다 켜면 다시 켜진 불빛이 뜨지 않는다");

        // CCTV 로 수상한 행동을 포착 — 그 방을 보고 있을 때만 기록이 남는다.
        Wipe();
        StartShift(1);
        _sim.SetSurveillanceTarget("storage_room");
        _sim.ReportCctvFeedLive(true);
        _sim.Tick(Step);
        _sim.MarkSuspiciousAction("storage_room", _sim.GetActiveEmployeeIds().First());
        Ok(_m.IsUnlocked(Achievements.SuspiciousCctv), "보고 있던 방의 수상한 행동 → 누군가의 흔적");

        Wipe();
        StartShift(1);
        _sim.SetSurveillanceTarget("core_room");       // 다른 방을 보고 있다
        _sim.ReportCctvFeedLive(true);
        _sim.Tick(Step);
        _sim.MarkSuspiciousAction("storage_room", _sim.GetActiveEmployeeIds().First());
        Ok(!_m.IsUnlocked(Achievements.SuspiciousCctv), "안 보고 있던 방의 일은 포착한 것이 아니다");

        // 격리 — 명령만으로는 안 되고 격리실에 실제로 들어서야 한다.
        Wipe();
        StartShift(1);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        string saboteur = GameState.Instance.SaboteurEmployeeId;
        string innocent = roster.First(id => id != saboteur);
        Ok(_sim.IsolateEmployee(innocent), $"{innocent} 에게 격리 명령");
        Ok(!_m.IsUnlocked(Achievements.FirstIsolation), "명령만으로는 격리 조치가 뜨지 않는다");
        for (int i = 0; i < 2000 && !_m.IsUnlocked(Achievements.FirstIsolation); i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        Ok(_m.IsUnlocked(Achievements.FirstIsolation), "격리실에 수용되면 격리 조치");
        Ok(_m.IsUnlocked(Achievements.InnocentIsolation), "결백한 직원이었으므로 억울한 격리도 기록됐다");
        Ok(!_toasts.Contains(Achievements.InnocentIsolation) && _m.DeferredCount == 1,
            "다만 **팝업은 아직 뜨지 않는다** — 결번이 누군지 새어 나가지 않게 보류했다");

        // 최종 보고서를 제출하면 그때 뜬다.
        GameState.Instance.SubmitFinalReport(innocent, new List<(int, string)>(), null);
        Ok(_toasts.Contains(Achievements.InnocentIsolation) && _m.DeferredCount == 0,
            "최종 보고서를 제출한 뒤에 팝업이 뜬다");

        // 결번을 격리한 경우에는 억울한 격리가 뜨지 않는다.
        Wipe();
        StartShift(1);
        GameState.Instance.SetSaboteur(roster[0]);
        Ok(_sim.IsolateEmployee(roster[0]), "결번 개체에게 격리 명령");
        for (int i = 0; i < 2000 && !_m.IsUnlocked(Achievements.FirstIsolation); i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        Ok(_m.IsUnlocked(Achievements.FirstIsolation) && !_m.IsUnlocked(Achievements.InnocentIsolation),
            "실제 결번을 격리하면 억울한 격리는 뜨지 않는다");
    }

    // ── F. DAY0 특별 업적 ───────────────────────────────────────────

    private void SectionF()
    {
        Head("F", "「이게 연습이라고?」 — 가상 시뮬레이션 100%");
        Wipe();

        // DAY0, 근무 중, 99%.
        GameState.Instance.ResetRun(0);
        GameState.Instance.SetPhase(GamePhase.Live);
        GameState.Instance.AddCoreProgress(99f, "검사");
        Ok(!_m.IsUnlocked(Achievements.TutorialCore100), $"DAY0 코어 99% → 해금되지 않는다 ({GameState.Instance.CoreProgress:0}%)");
        GameState.Instance.AddCoreProgress(1f, "검사");
        Ok(_m.IsUnlocked(Achievements.TutorialCore100), "DAY0 코어 100% → 해금된다");

        // DAY1 로 넘어가며 진행도가 0% 로 돌아가도 기록은 남는다.
        GameState.Instance.GoToNextDay();
        Ok(GameState.Instance.CurrentDay == 1 && GameState.Instance.CoreProgress < 0.01f,
            "DAY1 전환에서 코어 진행도는 기존 규칙대로 0% 로 초기화된다");
        Ok(_m.IsUnlocked(Achievements.TutorialCore100), "그래도 해금 기록은 사라지지 않는다");
        _m.ReloadForTest();
        Ok(_m.IsUnlocked(Achievements.TutorialCore100), "게임을 껐다 켜도 남아 있다");

        // DAY1~5 에서 100% 를 찍어도 이 업적은 아니다.
        Wipe();
        GameState.Instance.ResetRun(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        GameState.Instance.AddCoreProgress(100f, "검사");
        Ok(!_m.IsUnlocked(Achievements.TutorialCore100), "DAY2 코어 100% → 이 업적은 해금되지 않는다");

        // 근무 중이 아닐 때의 강제 주입(개발 허브 · 캡처 도구)은 일반 플레이가 아니다.
        Wipe();
        GameState.Instance.ResetRun(0);
        GameState.Instance.SetPhase(GamePhase.Schedule);
        GameState.Instance.AddCoreProgress(100f, "캡처");
        Ok(!_m.IsUnlocked(Achievements.TutorialCore100), "근무 중이 아닌 강제 주입으로는 해금되지 않는다");
    }

    // ── G. 등급 · 엔딩 · 하루 무사고 ─────────────────────────────────

    private void SectionG()
    {
        Head("G", "평가 등급 · 엔딩 · 하루 무사고");

        // 등급 — 실제로 받은 등급 하나만. 계산은 DayObjectives.Grade 를 그대로 쓴다.
        (float core, int score, string grade, string id, string label)[] cases =
        {
            (100f, 5, "S", Achievements.GradeS, "S → 완벽한 근무!"),
            (100f, 3, "A", Achievements.GradeA, "A → 이 구역의 에이스"),
            (100f, 1, "B", "", "B → 해당 업적 없음"),
            (40f, 0, "D", Achievements.GradeLowest, "D → 쓸모없는 관리자"),
            (40f, 4, "C", "", "C → 해당 업적 없음"),
        };
        foreach (var c in cases)
        {
            Wipe();
            GameState.Instance.ResetRun(5);
            GameState.Instance.AddCoreProgress(c.core, "검사");
            GameState.Instance.AddEvaluation(c.score);
            Ok(DayObjectives.Grade(c.core, c.score) == c.grade, $"등급 계산 {c.core:0}%/{c.score}점 = {c.grade}");
            _m.NoteEnding(EndingState.Kind.Loose);
            int got = new[] { Achievements.GradeS, Achievements.GradeA, Achievements.GradeLowest }
                .Count(x => _m.IsUnlocked(x));
            bool want = !string.IsNullOrEmpty(c.id);
            Ok(got == (want ? 1 : 0) && (!want || _m.IsUnlocked(c.id)), c.label);
        }

        // S 를 받았다고 A 까지 주지 않는다.
        Wipe();
        GameState.Instance.ResetRun(5);
        GameState.Instance.AddCoreProgress(100f, "검사");
        GameState.Instance.AddEvaluation(5);
        _m.NoteEnding(EndingState.Kind.True);
        Ok(_m.IsUnlocked(Achievements.GradeS) && !_m.IsUnlocked(Achievements.GradeA),
            "S 를 받아도 A 업적은 따라 해금되지 않는다");
        Ok(_m.IsUnlocked(Achievements.TrueEnding) && !_m.IsUnlocked(Achievements.BadEnding),
            "트루엔딩 → 드디어 퇴근입니다 (배드는 아니다)");

        // 다른 실패 엔딩을 Bad 로 뭉치지 않는다.
        foreach (var k in new[] { EndingState.Kind.Loose, EndingState.Kind.Late })
        {
            Wipe();
            _m.NoteEnding(k);
            Ok(!_m.IsUnlocked(Achievements.BadEnding) && !_m.IsUnlocked(Achievements.TrueEnding),
                $"{k} 엔딩은 트루도 배드도 아니다");
        }
        Wipe();
        _m.NoteEnding(EndingState.Kind.Bad);
        Ok(_m.IsUnlocked(Achievements.BadEnding), "배드엔딩 → 잘못된 판단");

        // 하루 무사고 — 기절이 한 번이라도 있으면 뜨지 않는다(회복시켜도 마찬가지).
        Wipe();
        StartShift(1);
        _m.NoteShiftEnded();
        Ok(_m.IsUnlocked(Achievements.SafeShift) && _m.IsUnlocked(Achievements.FirstShift),
            "사고 없이 DAY1 종료 → 첫 야근 + 오늘은 무사히");

        Wipe();
        StartShift(1);
        FaintSomeone();
        Ok(_m.IsUnlocked(Achievements.FirstFaint), "근무 중 기절 → 부주의한 관리자");
        _m.NoteShiftEnded();
        Ok(!_m.IsUnlocked(Achievements.SafeShift), "기절이 있었으면 오늘은 무사히가 뜨지 않는다");

        // 교육일(DAY0)은 실수 업적을 주지 않는다.
        Wipe();
        StartShift(0);
        FaintSomeone();
        _m.NoteCallMissed();
        _m.OnWarningFailed();
        _m.NoteShiftEnded();
        Ok(!_m.IsUnlocked(Achievements.FirstFaint) && !_m.IsUnlocked(Achievements.MissedCallOnce)
           && !_m.IsUnlocked(Achievements.WarningNeglected) && !_m.IsUnlocked(Achievements.SafeShift),
            "DAY0 의 강제 연출로는 부주의 · 무심 · 경고 방치 · 오늘은 무사히가 뜨지 않는다");

        // 수신 전화 누적.
        Wipe();
        StartShift(1);
        _m.NoteCallMissed();
        Ok(_m.IsUnlocked(Achievements.MissedCallOnce) && !_m.IsUnlocked(Achievements.MissedCallThree),
            "전화 1회 놓침 → 무심한 관리자");
        _m.NoteCallMissed();
        _m.NoteCallMissed();
        Ok(_m.IsUnlocked(Achievements.MissedCallThree), "누적 3회 → 차가운 관리자");
        _m.ReloadForTest();
        Ok(_m.GetCounter(Achievements.CounterMissedCall) == 3, "전화 누적 횟수는 재실행 후에도 남는다");

        // 기록실 화면.
        Head("G", "기록실 화면");
        var view = new AchievementArchiveView();
        AddChild(view);
        view.Open();
        Ok(view.PageCount == 6 && AchievementArchiveView.PerPage == 5,
            $"30개를 5개씩 {view.PageCount} 페이지로 나눈다");
        Ok(view.MovePage(1) && view.Page == 1, "다음 페이지로 넘어간다");
        view.MovePage(99);
        Ok(view.Page == view.PageCount - 1, "마지막 페이지에서 더 넘어가지 않는다");
        view.MovePage(-99);
        Ok(view.Page == 0 && !view.MovePage(-1), "첫 페이지에서 더 돌아가지 않는다");
        view.MoveCursor(-1);
        Ok(view.Page == 0 && view.Cursor == 0, "첫 줄에서 위로 더 가지 않는다");
        for (int i = 0; i < 40; i++) view.MoveCursor(1);
        Ok(view.Page * AchievementArchiveView.PerPage + view.Cursor == 29,
            "↓ 만으로 30번째 업적까지 닿는다 (페이지가 따라 넘어간다)");
        Ok(view.ItemAt(new Vector2(70f, 556f)) == "prev"
           && view.ItemAt(new Vector2(220f, 556f)) == "next"
           && view.ItemAt(new Vector2(690f, 556f)) == "back",
            "[◀ 이전] · [다음 ▶] · [뒤로] 를 마우스로 집을 수 있다");
        view.Close();
        Ok(!view.IsOpen && !view.Visible, "ESC · [뒤로] 로 닫히면 화면에서 사라진다");
        view.QueueFree();

        // 팝업 보류 조건.
        Head("G", "팝업 보류");
        Ok(AchievementToast.CanShowNow, "평소에는 팝업을 띄울 수 있다");
        BlinkOverlay.Instance?.Close(0.01);
        Ok(!AchievementToast.CanShowNow, "암전(눈 감기) 중에는 띄우지 않는다");
        BlinkOverlay.Instance?.OpenNow();
        Ok(AchievementToast.CanShowNow, "암전이 풀리면 다시 띄운다");
        OpenApproval();
        Ok(!AchievementToast.CanShowNow, "수리 승인 절차가 떠 있는 동안에도 띄우지 않는다");
        RepairApprovalSystem.Decline();
        Settle();
        Ok(Sfx.Instance?.Has("achievement") == true, "달성음 achievement.wav 가 등록되어 있다");
    }

    // ── 도구 ────────────────────────────────────────────────────────

    // 아무 직원 하나를 실제로 기절까지 몰아붙인다(스트레스 주입 → CheckFaint).
    private void FaintSomeone()
    {
        string id = _sim.GetActiveEmployeeIds().FirstOrDefault();
        if (string.IsNullOrEmpty(id)) return;
        _sim.AddStress(id, 200f, "검사");
        for (int i = 0; i < 60 && _sim.GetEmployeeState(id)?.Incapacitated != true; i++)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    // DAY n 근무를 실제로 시작한다(Day1OpsTest 와 같은 순서).
    private void StartShift(int day)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        RepairApprovalSystem.ResetAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < roster.Count && i < rooms.Length; i++) _sim.AssignToRoom(roster[i], rooms[i]);
        if (day >= 1) GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        _m.OnShiftStart();
    }

    // 해금 기록을 비우고 저장 파일도 지운다 — 검사 구간마다 깨끗한 상태에서 시작한다.
    private void Wipe()
    {
        _toasts.Clear();
        AchievementToast.Instance?.ClearForTest();
        _m.ClearForTest();
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestStore));
    }

    private void Head(string tag, string title) => GD.Print($"\n===== [{tag}] {title} =====");

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
