using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Core;

// 도전과제 판정과 영구 저장. autoload 하나로 끝낸다.
//
// 설계 원칙 세 가지.
//   ① **게임 규칙을 바꾸지 않는다.** 여기서 하는 일은 이미 일어난 사건을 듣고 기록하는 것뿐이다.
//      사고 확률 · 코어 수율 · 평가 등급 계산 어디에도 손대지 않았다.
//   ② **매 프레임 조건을 훑지 않는다.** 전부 사건이 일어난 그 순간 한 번 판정한다
//      (기존 이벤트를 구독하거나, 그 지점에서 한 줄 알려 준다).
//   ③ **깨져도 게임이 멈추지 않는다.** 저장 실패 · 파일 손상 · 예외 전부 조용히 삼킨다.
//
// 저장은 user://achievements_v1.cfg 한 장. 해금 id 와 누적 카운터만 들어간다.
// 회차용 수치(이번 판 대화한 직원 · 오늘 기절 수 등)는 메모리에만 두고 저장하지 않는다.
public partial class AchievementManager : Node
{
    public static AchievementManager Instance { get; private set; }

    // 개발 도구로 들어온 실행 — 해금은 메모리에만 남기고 user:// 파일을 건드리지 않는다.
    // (DebugEntryPoint 가 켠다. 실제 플레이에서는 아무도 켜지 않는다.)
    public static bool Sandbox { get; set; }

    public const string DefaultSavePath = "user://achievements_v1.cfg";

    // 실제 저장 경로. 검사 씬만 다른 파일로 바꿔 쓴다 — 자동 검증이 돌 때마다
    // 플레이어의 진짜 기록이 지워지면 안 된다.
    public static string StorePath { get; set; } = DefaultSavePath;

    private const string SectionUnlocked = "unlocked";
    private const string SectionCounters = "counters";

    // 해금된 순간(팝업이 듣는다). 보류된 업적은 보류가 풀릴 때 여기로 나온다.
    public static event Action<AchievementDefinition> AchievementUnlocked;

    private readonly HashSet<string> _unlocked = new();
    private readonly Dictionary<string, int> _counters = new();
    private bool _loaded;

    // ── 회차용 (저장하지 않는다) ─────────────────────────────────────
    // 이번 판에서 실제로 대화한 직원.
    private readonly HashSet<string> _talked = new();
    // 이번 근무에서 새로 기절한 수 / 사망 발생 여부.
    private int _faintsThisShift;
    private bool _deathThisShift;
    // 팝업을 미뤄 둔 업적 — 지금 띄우면 추리의 정답을 알려주게 되는 것들.
    // 「억울한 격리」가 여기 들어간다: 격리한 순간에 뜨면 그 직원이 결번이 아니라는 사실이
    // 그대로 새어 나간다. 최종 보고서를 제출한 뒤에 비로소 띄운다.
    private readonly List<string> _deferred = new();

    public override void _EnterTree()
    {
        Instance = this;

        // **에디터에서 돌릴 때(F5 · F6)는 항상 0개부터 시작한다.**
        //
        // 도전과제를 확인하려면 "아직 안 깬 상태" 가 매번 필요한데, 기록이 파일에 남아
        // 있으면 한 번 깬 것은 두 번 다시 볼 수 없다. 그래서 에디터 실행에서는 읽지 않고
        // 아예 지우고 시작한다. 내보낸 빌드는 평소대로 읽는다 —
        // OS.HasFeature("editor") 는 거기서 false 다.
        if (OS.HasFeature("editor")) WipeAll();
        else Load();
    }

    public override void _Ready() => Wire();

    public override void _ExitTree()
    {
        Unwire();
        if (Instance == this) Instance = null;
    }

    // ── 공개 인터페이스 ──────────────────────────────────────────────

    public bool IsUnlocked(string achievementId) => _unlocked.Contains(achievementId ?? "");

    public int GetUnlockedCount() => Achievements.All.Count(a => _unlocked.Contains(a.Id));

    public int GetCounter(string counterId) => _counters.GetValueOrDefault(counterId ?? "", 0);

    // 보류 중인 팝업이 몇 개 남아 있는가(검사용).
    public int DeferredCount => _deferred.Count;

    // 이미 해금했으면 아무 일도 일어나지 않는다 — 중복 팝업도, 중복 저장도 없다.
    //
    // deferToast=true 면 기록만 남기고 팝업은 보류한다(FlushDeferred 가 나중에 띄운다).
    public bool Unlock(string achievementId, bool deferToast = false)
    {
        var def = Achievements.Get(achievementId);
        if (def == null || !_unlocked.Add(def.Id)) return false;
        Save();
        if (deferToast) _deferred.Add(def.Id);
        else Raise(def);
        return true;
    }

    // 누적 카운터를 1 올리고 새 값을 돌려준다. 값은 영구 저장된다.
    public int IncrementCounter(string counterId)
    {
        if (string.IsNullOrEmpty(counterId)) return 0;
        int v = _counters.GetValueOrDefault(counterId, 0) + 1;
        _counters[counterId] = v;
        Save();
        return v;
    }

    // 보류해 둔 팝업을 띄운다(최종 보고서 제출 뒤 등).
    public void FlushDeferred()
    {
        if (_deferred.Count == 0) return;
        var list = _deferred.ToList();
        _deferred.Clear();
        foreach (string id in list)
        {
            var def = Achievements.Get(id);
            if (def != null) Raise(def);
        }
    }

    private static void Raise(AchievementDefinition def)
    {
        try { AchievementUnlocked?.Invoke(def); }
        catch (Exception e) { GD.PushWarning($"AchievementManager: 팝업 통지 실패 - {e.Message}"); }
    }

    // ── 저장 ────────────────────────────────────────────────────────

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var cfg = new ConfigFile();
            // 파일이 없으면 그냥 빈 기록으로 시작한다(처음 해금할 때 새로 만들어진다).
            if (cfg.Load(StorePath) != Error.Ok) return;
            if (cfg.HasSection(SectionUnlocked))
                foreach (string key in cfg.GetSectionKeys(SectionUnlocked))
                    if ((bool)cfg.GetValue(SectionUnlocked, key, false) && Achievements.Get(key) != null)
                        _unlocked.Add(key);
            if (cfg.HasSection(SectionCounters))
                foreach (string key in cfg.GetSectionKeys(SectionCounters))
                    _counters[key] = Math.Max(0, (int)cfg.GetValue(SectionCounters, key, 0));
        }
        catch (Exception e)
        {
            // 손상된 파일 — 기록이 비는 것은 아쉽지만 게임이 멈추지는 않는다.
            _unlocked.Clear();
            _counters.Clear();
            GD.PushWarning($"AchievementManager: 도전과제 기록을 읽지 못했습니다 - {e.Message}");
        }
    }

    private void Save()
    {
        if (Sandbox) return;
        try
        {
            var cfg = new ConfigFile();
            foreach (string id in _unlocked) cfg.SetValue(SectionUnlocked, id, true);
            foreach (var pair in _counters) cfg.SetValue(SectionCounters, pair.Key, pair.Value);
            cfg.Save(StorePath);
        }
        catch (Exception e)
        {
            // 디스크에 못 썼다고 런타임이 멈추면 안 된다 — 이번 실행 동안은 메모리에 남아 있다.
            GD.PushWarning($"AchievementManager: 도전과제 저장 실패 - {e.Message}");
        }
    }

    // ── 기록 통째로 지우기 (개발용) ──────────────────────────────────
    //
    // 메모리와 **user:// 파일 둘 다** 지운다 — 처음 켠 것과 같은 상태가 된다.
    // ClearForTest 와 다르다: 저쪽은 메모리만 비우므로 다음 해금 때 예전 파일이
    // 그대로 다시 쓰이고, 게임을 껐다 켜면 기록이 되살아난다.
    //
    // 에디터에서 F5 · F6 로 돌릴 때마다 도전과제를 처음부터 확인하려고 만들었다.
    // 부르는 자리는 두 곳 — 개발자 허브의 버튼과, 에디터 실행 중의 F7(아래 _Input).
    public void WipeAll()
    {
        _unlocked.Clear();
        _counters.Clear();
        _deferred.Clear();
        OnRunReset();
        _loaded = true;                 // 다시 Load 해서 되살아나지 않게

        if (Sandbox) { GD.Print("[도전과제] 샌드박스 실행 - 메모리 기록만 비웠습니다."); return; }

        try
        {
            // 파일을 없앤다. 못 없애면 빈 파일로 덮어 같은 결과를 만든다.
            if (FileAccess.FileExists(StorePath))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(StorePath));
            if (FileAccess.FileExists(StorePath)) new ConfigFile().Save(StorePath);
            GD.Print($"[도전과제] 기록을 초기화했습니다 - {StorePath}");
        }
        catch (Exception e)
        {
            GD.PushWarning($"AchievementManager: 기록 파일을 지우지 못했습니다 - {e.Message}");
        }
    }

    // ── 에디터 실행 중의 초기화 키 ──────────────────────────────────
    //
    // **F7.** 내보낸 빌드에는 아예 없다(OS.HasFeature("editor") 로 막는다) —
    // 플레이어가 실수로 기록을 날리는 일이 없어야 한다.
    //
    // 개발자 허브(F6)에는 버튼이 따로 있지만, F5 로 곧장 타이틀을 띄웠을 때는
    // 그 버튼에 닿을 수 없다. 그 자리를 위한 키다.
    private const Key WipeKey = Key.F7;

    public override void _Input(InputEvent @event)
    {
        if (!OS.HasFeature("editor")) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: WipeKey }) return;
        WipeAll();
        ShowWipeNotice();
        GetViewport().SetInputAsHandled();
    }

    // 눌렀다는 것이 **화면에** 보여야 한다 — 에디터 출력창을 보고 있지 않을 때가 많다.
    private void ShowWipeNotice()
    {
        var layer = new CanvasLayer { Layer = 200 };
        AddChild(layer);

        var label = new Label
        {
            Text = "  도전과제 기록 초기화됨 (F7)  ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        label.Position = new Vector2(-150f, 28f);
        label.CustomMinimumSize = new Vector2(300f, 34f);
        label.AddThemeColorOverride("font_color", new Color(0.62f, 0.95f, 1f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        label.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(label);

        var tw = CreateTween();
        tw.TweenInterval(1.1);
        tw.TweenProperty(label, "modulate:a", 0f, 0.5);
        tw.TweenCallback(Callable.From(layer.QueueFree));
    }

    // 검사 전용 — 메모리 기록만 비운다(파일은 건드리지 않는다).
    public void ClearForTest()
    {
        _unlocked.Clear();
        _counters.Clear();
        _deferred.Clear();
        OnRunReset();
    }

    // 검사 전용 — "게임을 껐다 다시 켠" 상태를 만든다. 메모리를 비우고 파일에서 다시 읽는다.
    public void ReloadForTest()
    {
        ClearForTest();
        _loaded = false;
        Load();
    }

    // ── 회차 / 근무 경계 ─────────────────────────────────────────────

    // 새 근무(판)를 시작했다. **영구 해금 기록은 절대 건드리지 않는다.**
    public void OnRunReset()
    {
        _talked.Clear();
        _faintsThisShift = 0;
        _deathThisShift = false;
    }

    // 오늘 근무가 시작됐다 — 하루치 집계만 되돌린다.
    public void OnShiftStart()
    {
        _faintsThisShift = 0;
        _deathThisShift = false;
    }

    // ── 사건 보고 (게임 쪽에서 한 줄로 부른다) ─────────────────────────

    private static bool RealShift => (GameState.Instance?.CurrentDay ?? 1) >= 1;
    private static int Day => GameState.Instance?.CurrentDay ?? 1;

    // 실제 타이틀 화면에 들어섰다.
    public void NoteTitleReached() => Unlock(Achievements.FirstLaunch);

    // 가상 시뮬레이션 교육을 **끝까지** 마쳤다(중단 · 개발용 스킵은 여기로 오지 않는다).
    public void NoteTutorialCompleted() => Unlock(Achievements.TutorialComplete);

    // 시설 로그 창을 열었다. 근무 흐름 안에서 연 것만 센다 —
    // 로그 창(L)은 타이틀 화면에서도 열리는데, 거기서 연 빈 창까지 "열람"으로 칠 수는 없다.
    public void NoteFacilityLogOpened()
    {
        if (GameState.Instance?.CurrentPhase is GamePhase.Live or GamePhase.Rest or GamePhase.Settlement)
            Unlock(Achievements.FirstLog);
    }

    // 코어 복구율이 방금 100% 를 넘었다. 가상 시뮬레이션(DAY0)의 실제 근무 중일 때만 센다 —
    // DAY1 전환에서 진행도가 0% 로 돌아가도 이 기록은 이미 파일에 들어가 있다.
    public void NoteCoreProgress(float before, float after)
    {
        if (Day != 0 || GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        if (before >= 100f - 0.0001f || after < 100f - 0.0001f) return;
        Unlock(Achievements.TutorialCore100);
    }

    // 전화가 실제로 연결됐다. outgoing = 관리자가 직접 걸었다.
    public void NoteCallConnected(string employeeId, bool outgoing)
    {
        // 먼저 거는 것은 언제나 플레이어의 선택이다 — 교육일이라고 빼지 않는다.
        // (교육이 거는 전화는 전부 **수신**이라 여기로 들어오지 않는다.)
        if (outgoing) Unlock(Achievements.OutgoingCall);
        // 한 회차에서 서로 다른 여섯 명과 이야기했는가. 자동 재생 스토리 대사는 전화를
        // 거치지 않으므로 여기 들어오지 않는다.
        if (string.IsNullOrEmpty(employeeId) || !RealShift) return;
        _talked.Add(employeeId);
        if (_talked.Count >= Achievements.TalkTarget) Unlock(Achievements.TalkToSix);
    }

    // 직원의 수신 전화를 거절했거나 받지 않았다. 한 통당 한 번만 들어온다.
    public void NoteCallMissed()
    {
        // 교육의 강제 전화는 세지 않는다 — 플레이어의 선택이 아니다.
        if (!RealShift) return;
        Unlock(Achievements.MissedCallOnce);
        if (IncrementCounter(Achievements.CounterMissedCall) >= Achievements.MissedCallTarget)
            Unlock(Achievements.MissedCallThree);
    }

    // 고장 난 설비의 수리가 완료됐다.
    public void NoteFacilityRepaired()
    {
        if (RealShift) Unlock(Achievements.FirstFacilityRepair);
    }

    // 발전기 사고로 깎였던 전력 용량이 수리로 정상 복구됐다
    // (스위치를 껐다 켜는 것은 용량을 바꾸지 않으므로 여기 오지 않는다).
    public void NotePowerRestored()
    {
        if (RealShift) Unlock(Achievements.PowerRestored);
    }

    // CCTV 로 직원의 수상한 행동을 직접 포착해 유효한 관찰 기록이 남았다.
    public void NoteSuspiciousObserved()
    {
        if (RealShift) Unlock(Achievements.SuspiciousCctv);
    }

    // 직원이 격리실에 실제로 수용됐다(명령만 내리고 취소했으면 여기 오지 않는다).
    public void NoteIsolationAccepted(string employeeId)
    {
        if (!RealShift) return;
        Unlock(Achievements.FirstIsolation);
        // 결번이 아닌 직원을 격리했다 — **팝업은 미뤄 둔다.** 지금 띄우면 이 사람이
        // 범인이 아니라는 사실을 업적이 먼저 알려 주는 셈이 된다.
        string saboteur = GameState.Instance?.SaboteurEmployeeId ?? "";
        if (!string.IsNullOrEmpty(saboteur) && employeeId != saboteur)
            Unlock(Achievements.InnocentIsolation, deferToast: true);
    }

    // 최종 격리 보고서를 제출했다 — 미뤄 둔 팝업을 이제 띄운다.
    public void NoteFinalReportSubmitted() => FlushDeferred();

    // 오늘 근무가 끝났다.
    public void NoteShiftEnded()
    {
        if (!RealShift) return;
        if (Day == 1) Unlock(Achievements.FirstShift);
        // 하루 도중에 기절한 직원을 회복시켜도 무사고가 아니다 — 발생 자체를 센다.
        if (_faintsThisShift == 0 && !_deathThisShift) Unlock(Achievements.SafeShift);
    }

    // 엔딩 종류가 확정됐다. 등급도 이 시점에 함께 판정한다 — 평가 등급 계산 규칙은
    // DayObjectives.Grade 그대로 쓰고 여기서 새로 만들지 않는다.
    public void NoteEnding(EndingState.Kind kind)
    {
        string grade = DayObjectives.FinalGrade(kind);
        // S 를 받았다고 A 까지 주지 않는다 — 실제로 받은 등급 하나만.
        switch (grade)
        {
            case "S": Unlock(Achievements.GradeS); break;
            case "A": Unlock(Achievements.GradeA); break;
            case "F": Unlock(Achievements.GradeLowest); break;
        }
        // 다른 실패 엔딩(Late · Loose)을 Bad 로 뭉치지 않는다.
        if (kind == EndingState.Kind.True) Unlock(Achievements.TrueEnding);
        else if (kind == EndingState.Kind.Bad) Unlock(Achievements.BadEnding);
    }

    // ── 기존 이벤트 구독 ─────────────────────────────────────────────

    private FacilityHooks _hooks;

    private void Wire()
    {
        try
        {
            NSP.Dialogue.InterviewSession.Asked += OnInterviewAsked;
            NSP.Dialogue.InterviewSession.Confronted += OnConfronted;
            RepairApprovalSystem.Resolved += OnRepairResolved;
            _hooks = new FacilityHooks(this);
            AddChild(_hooks);
        }
        catch (Exception e) { GD.PushWarning($"AchievementManager: 이벤트 연결 실패 - {e.Message}"); }
    }

    private void Unwire()
    {
        try
        {
            NSP.Dialogue.InterviewSession.Asked -= OnInterviewAsked;
            NSP.Dialogue.InterviewSession.Confronted -= OnConfronted;
            RepairApprovalSystem.Resolved -= OnRepairResolved;
        }
        catch { /* 종료 중 — 무시 */ }
    }

    private void OnInterviewAsked(string employeeId, NSP.Dialogue.InterviewQuestion q)
    {
        if (!RealShift) return;
        // 휴게시간에 실제로 물은 것만 심문이다(교육의 진행 확인용 호출과 구분된다).
        if (GameState.Instance?.CurrentPhase == GamePhase.Rest) Unlock(Achievements.FirstInterview);
        // 조사 자료를 근거로 한 질문 — 자료를 고르기만 한 것으로는 여기 오지 않는다.
        if (!string.IsNullOrEmpty(q?.EvidenceId)) Unlock(Achievements.FirstEvidence);
    }

    private void OnConfronted(string employeeId, NSP.Dialogue.ConfrontKind kind, string variant)
    {
        // 성립하지 않은 조합(None)은 추궁이 아니다 — 판정은 EvidenceContradiction 이 이미 했다.
        if (RealShift && kind != NSP.Dialogue.ConfrontKind.None) Unlock(Achievements.FirstContradiction);
    }

    // 수리 승인 절차가 끝났다. 미로 실패와 거절/무응답을 여기서 가른다 —
    // 둘은 서로 배타적이므로 한 건이 양쪽에 집계되지 않는다.
    private void OnRepairResolved(bool succeeded, string roomId)
    {
        if (succeeded || !RealShift) return;
        if (RepairApprovalSystem.MazeFailed)
        {
            Unlock(Achievements.MazeFailOnce);
            if (IncrementCounter(Achievements.CounterMazeFail) >= Achievements.MazeFailTarget)
                Unlock(Achievements.MazeFailThree);
            return;
        }
        if (!RepairApprovalSystem.LastApproved) Unlock(Achievements.RepairRequestIgnored);
    }

    internal void OnFainted()
    {
        if (!RealShift) return;
        _faintsThisShift++;
        Unlock(Achievements.FirstFaint);
    }

    internal void OnDeath()
    {
        if (RealShift) _deathThisShift = true;
    }

    internal void OnWarningFailed()
    {
        if (RealShift) Unlock(Achievements.WarningNeglected);
    }

    // 이상 개체가 작업실을 떠났다 — 그 방 CCTV 를 띄워 두고 있었을 때만 "확인했다" 로 친다.
    internal void OnGhostDispelled(string roomId)
    {
        if (WatchingRoom(roomId)) Unlock(Achievements.GhostDispelled);
    }

    // 이상 개체가 그 방 설비를 부쉈다 — **그 순간을 CCTV 로 보고 있었을 때만** 「보기만 하는 관리자」.
    //
    // 예전에는 "끝까지 관측하지 않아 사고를 내세요" 였다. 관측 소멸이 폐지된 뒤로 개체는 보든 말든
    // 떠나며 확률로 사고를 내므로, 끝까지 지켜본 사람에게도 이 과제가 떴다(2026-10-31 사용자 제보).
    // 지금은 사고를 막지 못하고 지켜보기만 한 것을 센다 — 보지 않았으면 뜨지 않는다.
    internal void OnGhostStruck(string roomId)
    {
        if (RealShift && WatchingRoom(roomId)) Unlock(Achievements.GhostIgnored);
    }

    private static bool WatchingRoom(string roomId)
    {
        var sim = NSP.Facility.FacilitySimulation.Instance;
        return sim != null && !string.IsNullOrEmpty(roomId)
            && sim.IsRoomUnderActiveCctv(roomId) && !sim.IsRoomCctvBlocked(roomId);
    }

    // FacilitySimulation 안쪽 시스템들의 이벤트를 받아 넘기는 얇은 중계 노드.
    // 매니저가 직접 구독하지 않는 이유는 하나다 — 시뮬레이션 autoload 가 이 노드보다
    // 먼저 준비됐는지 보장할 수 없어, 준비될 때까지 한 프레임씩 기다려야 한다.
    private sealed partial class FacilityHooks : Node
    {
        private readonly AchievementManager _m;
        private NSP.Facility.FacilitySimulation _sim;
        private bool _bound;

        public FacilityHooks(AchievementManager m) => _m = m;

        public override void _Ready()
        {
            Name = "FacilityHooks";
            SetProcess(true);
            Bind();
        }

        public override void _Process(double delta) => Bind();

        private void Bind()
        {
            if (_bound) { SetProcess(false); return; }
            _sim = NSP.Facility.FacilitySimulation.Instance;
            if (_sim == null) return;
            _bound = true;
            SetProcess(false);
            try
            {
                _sim.Rescue.Fainted += OnFainted;
                _sim.Warnings.Neglected += OnWarningFailed;
                _sim.Isolation.Accepted += OnIsolated;
                if (_sim.Ghost != null)
                {
                    _sim.Ghost.Departed += OnDispelled;
                    _sim.Ghost.Struck += OnStruck;
                }
                _sim.EmployeeKilled += OnKilled;
            }
            catch (Exception e) { GD.PushWarning($"AchievementManager: 시뮬레이션 연결 실패 - {e.Message}"); }
        }

        public override void _ExitTree()
        {
            if (!_bound || _sim == null || !IsInstanceValid(_sim)) return;
            try
            {
                _sim.Rescue.Fainted -= OnFainted;
                _sim.Warnings.Neglected -= OnWarningFailed;
                _sim.Isolation.Accepted -= OnIsolated;
                if (_sim.Ghost != null)
                {
                    _sim.Ghost.Departed -= OnDispelled;
                    _sim.Ghost.Struck -= OnStruck;
                }
                _sim.EmployeeKilled -= OnKilled;
            }
            catch { /* 종료 중 — 무시 */ }
        }

        private void OnFainted(string id) => _m.OnFainted();
        private void OnKilled(string id) => _m.OnDeath();
        private void OnWarningFailed(string roomId, string title) => _m.OnWarningFailed();
        private void OnIsolated(string id) => _m.NoteIsolationAccepted(id);
        private void OnDispelled(string roomId) => _m.OnGhostDispelled(roomId);
        private void OnStruck(string roomId) => _m.OnGhostStruck(roomId);
    }
}
