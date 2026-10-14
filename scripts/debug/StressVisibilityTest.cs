using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.Debug;

// F-2 스트레스 가시성 검증.
//
//   godot --headless --path . res://scenes/debug/StressVisibilityTest.tscn --quit-after 4000
//
// 화면에 무엇이 그려지는지가 아니라, 화면들이 공통으로 읽는 값(구간 이름 · 구간 색 ·
// 기절 임박 · 구간 전환 알림)이 규칙대로 나오는지를 본다.
public partial class StressVisibilityTest : Node
{
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
        GD.Print("\n\n################ F-2 스트레스 가시성 검증 ################");
        FacilityAlertHud.Noticed += OnNotice;

        TestBands();
        TestFaintImminent();
        TestBandAlerts();
        TestBenchedFlags();

        FacilityAlertHud.Noticed -= OnNotice;
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private void OnNotice(string text, NoticeLevel level) => _notices.Add((text, level));

    // ── 구간 이름과 색은 한 곳에서만 정해진다 ────────────────────────────
    private void TestBands()
    {
        Head("A", "구간 이름 · 색 — 모든 화면이 같은 값을 읽는다");
        Reset(1);
        var st = _sim.GetEmployeeState("cat");
        var cfg = Config.Instance.Data;

        foreach (var (stress, want) in new[]
                 { (1f, "정상"), (10f, "정상"), (cfg.StressCautionFrom, "주의"), (30f, "주의"),
                   (cfg.StressDangerFrom, "위험"), (45f, "위험"), (cfg.StressFaintFrom, "기절") })
        {
            st.Stress = stress;
            st.Incapacitated = false;
            Check(_sim.StressBandName(st) == want, $"스트레스 {stress:0} = {want} ({_sim.StressBandName(st)})");
        }

        Check(FacilitySimulation.StressBandColor("위험") != FacilitySimulation.StressBandColor("주의")
              && FacilitySimulation.StressBandColor("기절") != FacilitySimulation.StressBandColor("위험"),
            "구간마다 색이 다르다");
        Check(FacilitySimulation.StressBandHex("위험") == "#ff9933", "위험 색은 기존 상세창과 같다");

        // 잠긴 날에는 구간이 늘 정상이다 — 화면도 아무것도 그리지 않는다.
        Reset(0);
        st = _sim.GetEmployeeState("cat");
        st.Stress = 40f;
        Check(!DayFeatures.StressEnabled && _sim.StressBandName(st) == "정상",
            "스트레스가 잠긴 날에는 구간이 뜨지 않는다");
    }

    private void TestFaintImminent()
    {
        Head("B", "기절 임박 — 기절 문턱 바로 아래");
        Reset(1);
        var st = _sim.GetEmployeeState("cat");
        var cfg = Config.Instance.Data;

        st.Stress = cfg.StressFaintFrom - cfg.StressFaintSoonMargin - 0.1f;
        Check(!_sim.IsFaintImminent(st), "아직 임박이 아니다");
        st.Stress = cfg.StressFaintFrom - cfg.StressFaintSoonMargin;
        Check(_sim.IsFaintImminent(st), "문턱 바로 아래 = 임박");
        st.Stress = cfg.StressFaintFrom;
        Check(!_sim.IsFaintImminent(st), "이미 기절했으면 임박이 아니다");
    }

    // ── 구간이 나빠지는 순간에만 한 번 알린다 ────────────────────────────
    private void TestBandAlerts()
    {
        Head("C", "구간 전환 알림 — 위험 진입 · 기절 임박에 한 번씩");
        Reset(1);
        GameState.Instance.SetPhase(GamePhase.Live);
        var st = _sim.GetEmployeeState("cat");
        var cfg = Config.Instance.Data;
        string name = _sim.GetEmployeeDef("cat")?.Codename ?? "cat";

        // AddStress 는 담력 배율을 먹으므로 "얼마를 더하면 몇이 된다" 로 계산하지 않는다.
        // 문턱 바로 아래에 직접 세워 두고 조금만 밀어 넘긴다.
        // 정상 → 주의: 알리지 않는다(아직 일은 거의 그대로 돌아간다).
        _notices.Clear();
        st.Stress = cfg.StressCautionFrom - 0.5f;
        _sim.AddStress("cat", 1f);
        Check(_sim.StressBandName(st) == "주의" && _notices.Count == 0,
            $"주의 진입에는 알림이 없다 ({_sim.StressBandName(st)} · {_notices.Count}건)");

        // 주의 → 위험: 경고 한 번, 작업 효율까지 적는다.
        _notices.Clear();
        st.Stress = cfg.StressDangerFrom - 0.5f;
        _sim.AddStress("cat", 1f);
        int rate = Mathf.RoundToInt(_sim.StressWorkRate(st) * 100f);
        bool one = _notices.Count == 1 && _notices[0].Level == NoticeLevel.Warning;
        Check(one, $"위험 진입에 경고 1건 ({_notices.Count}건)");
        if (one)
        {
            GD.Print($"      → {_notices[0].Text}");
            Check(_notices[0].Text.Contains(name) && _notices[0].Text.Contains($"{rate}%"),
                "문구에 이름과 작업 효율이 들어간다");
        }

        // 같은 구간 안에서 더 올라도 다시 알리지 않는다.
        _notices.Clear();
        _sim.AddStress("cat", 1f);
        Check(_notices.Count == 0, $"같은 구간에서는 다시 알리지 않는다 ({_notices.Count}건)");

        // 기절 임박: 붉은 알림 한 번.
        _notices.Clear();
        st.Stress = cfg.StressFaintFrom - cfg.StressFaintSoonMargin - 0.5f;
        _sim.AddStress("cat", 1f);
        bool crit = _notices.Count == 1 && _notices[0].Level == NoticeLevel.Critical;
        Check(crit, $"기절 임박에 붉은 알림 1건 ({_notices.Count}건)");
        if (crit) GD.Print($"      → {_notices[0].Text}");

        // 근무가 아니면 알리지 않는다(배치 · 휴게 중에는 조용하다).
        Reset(1);
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _notices.Clear();
        _sim.GetEmployeeState("dog").Stress = cfg.StressDangerFrom - 0.5f;
        _sim.AddStress("dog", 1f);
        Check(_notices.Count == 0, $"근무 중이 아니면 알리지 않는다 ({_notices.Count}건)");
    }

    // 배치 화면이 "배치 불가" 로 그릴 근거 — 기절 · 격리 플래그.
    private void TestBenchedFlags()
    {
        Head("D", "기절 · 격리 — 배치 화면이 읽는 플래그");
        Reset(1);
        GameState.Instance.SetPhase(GamePhase.Live);
        var st = _sim.GetEmployeeState("cat");
        st.Stress = Config.Instance.Data.StressFaintFrom - 0.5f;
        _sim.AddStress("cat", 1f);
        Check(st.Incapacitated, "기절 문턱을 넘으면 Incapacitated 가 선다");
        Check(_sim.StressBandName(st) == "기절", "구간 이름도 기절");
        Check(Mathf.IsZeroApprox(_sim.StressWorkRate(st)), "기절하면 작업 효율 0");

        var dog = _sim.GetEmployeeState("dog");
        dog.Isolated = true;
        Check(dog.Isolated && !dog.Incapacitated, "격리는 기절과 따로 표시된다");
        dog.Isolated = false;
    }

    // --- 도우미 ---------------------------------------------------------

    private void Reset(int day)
    {
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        EventLog.Instance?.ClearAll();
        foreach (string id in _sim.GetActiveEmployeeIds())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null) continue;
            st.Stress = 1f;
            st.Incapacitated = false;
            st.Isolated = false;
            st.Alive = true;
        }
        _notices.Clear();
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private bool Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
        return ok;
    }
}
