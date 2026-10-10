using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 스토리 전환 연출 · 공포 사운드 패스 검증(지시서 PART A · PART B).
//
//   godot --headless --path . res://scenes/debug/StoryAudioTest.tscn --quit-after 120000
//
// 소리가 "무서운가" 는 기계가 못 잰다 — 그건 헤드폰으로 들어야 한다(§24).
// 여기서 재는 것은 들어 보기 전에 반드시 맞아야 하는 것들이다:
//   · 쓰는 음원 파일이 전부 실재하는가(없으면 Play 가 조용히 아무것도 안 한다)
//   · 공포음과 게임 정보음이 **한 파일도 겹치지 않는가**(§13)
//   · DAY1 에 가까운 소리가 안 나오는가(§19) · 하루 횟수 상한(§12)
//   · 스토리 중 · 통화 중 · 경고 중에는 울리지 않는가(§20 · §13 · §21)
//   · Shift Card 수치가 하드코딩이 아닌가(§9)
//   · fail-safe 가 암전 · 확대 · 스토리 BGM 을 전부 되돌리는가(§10)
public partial class StoryAudioTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        GD.Print("\n\n################ 스토리 전환 · 공포 사운드 검증 ################");
        TestAssetsExist();
        TestHorrorNeverCollidesWithGameplayCues();
        TestDayRamp();
        TestSuppression();
        TestShiftCard();
        TestFailSafe();
        TestBuses();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 음원이 실재하는가 ─────────────────────────────────────────────
    //
    // 없는 키로 Play 를 부르면 Sfx 가 조용히 지나간다 — "효과음이 안 난다" 의 1순위 원인이다.
    private void TestAssetsExist()
    {
        Head("A", "쓰는 음원이 전부 실재한다");

        foreach (string key in HorrorAudioDirector.AllKeys())
            Check(Sfx.Instance?.Has(key) == true, $"공포음 '{key}'");

        // 스토리 전환이 쓰는 것들.
        foreach (string key in new[] { "admin_exhale", "admin_gasp", "breakroom_murmur",
                                       "chair_pull", "chair_creak", "cup_set", "cloth_rustle" })
            Check(Sfx.Instance?.Has(key) == true, $"스토리 전환음 '{key}'");

        Check(Sfx.HasMusic(StoryTransition.StoryMusic),
            $"스토리 BGM '{StoryTransition.StoryMusic}' (mp3 도 찾는다)");
        Check(Sfx.HasMusic("rest_time"), "복귀용 기존 BGM 'rest_time'");
    }

    // ── §13 게임 정보음과 섞이지 않는가 ──────────────────────────────
    //
    // 플레이어가 "게임 정보를 놓쳤다" 고 착각하는 건 공포가 아니라 UX 오류다.
    // 그래서 공포음은 게임이 쓰는 소리를 **한 파일도** 재사용하지 않는다.
    private void TestHorrorNeverCollidesWithGameplayCues()
    {
        Head("B", "공포음이 게임 정보음과 겹치지 않는다");

        // 게임이 "확인해야 할 것" 을 알리는 데 쓰는 소리들.
        var gameplay = new HashSet<string>
        {
            "call_ring", "phone_pickup", "phone_hangup",      // 전화
            "alarm", "alert_beep3", "siren",                  // 경고 · 사고
            "sensor_beep", "task_spawn", "task_fail",          // 업무 · 센서
            "relay_click", "switch", "switch_fail",            // 승인 · 조작
            "task_done", "ding", "shift_complete", "assign",   // 완료 · 배치
            "power_down", "cctv_cut", "taboo_break",            // 결과 · 금기
        };

        var horror = HorrorAudioDirector.AllKeys();
        var clash = horror.Where(gameplay.Contains).ToList();
        foreach (string k in clash) GD.Print($"      ✗ '{k}' 는 게임 정보음이다");
        Check(clash.Count == 0, $"공포음 {horror.Count}종 중 게임 정보음과 같은 파일 {clash.Count}개");

        // 괴물 비명도 게임 신호다 — 공포 ambience 가 그 자리를 빌리면 안 된다.
        Check(!horror.Any(k => k.Contains("비명")), "괴물 비명 파일을 ambience 로 쓰지 않는다");

        // 공포음은 전용 버스로만 나간다 — 그래야 통째로 눌러 둘 수 있다.
        Check(AudioServer.GetBusIndex(GameSettings.BusHorror) >= 0,
            $"'{GameSettings.BusHorror}' 버스가 있다");
    }

    // ── §12 · §19 DAY 별 세기 ────────────────────────────────────────
    private void TestDayRamp()
    {
        Head("C", "DAY 가 올라갈수록만 세진다");

        GD.Print("   DAY  하루 횟수   쓸 수 있는 소리   가까운 소리");
        for (int day = 1; day <= 5; day++)
        {
            var (lo, hi) = HorrorAudioDirector.QuotaRange(day);
            var keys = HorrorAudioDirector.KeysForDay(day, rest: false);
            int close = keys.Count(k => HorrorAudioDirector.IsCloseCue(k, day));
            GD.Print($"   {day}    {lo}~{hi}회      {keys.Count,2}종            {close}종");
        }

        // DAY1 은 초보자가 처음 혼자 앉는 날 — 가까운 귀 속삭임이 없어야 한다(§19).
        var d1 = HorrorAudioDirector.KeysForDay(1, rest: false);
        Check(d1.All(k => !HorrorAudioDirector.IsCloseCue(k, 1)), "DAY1 에는 가까운 소리가 없다");
        Check(!HorrorAudioDirector.KeysForDay(2, rest: false)
            .Any(k => HorrorAudioDirector.IsCloseCue(k, 2)), "DAY2 에도 아직 없다");
        Check(HorrorAudioDirector.KeysForDay(3, rest: false)
            .Any(k => HorrorAudioDirector.IsCloseCue(k, 3)), "DAY3 부터 가까운 소리가 생긴다");

        // 하루 횟수는 올라가기만 한다. 중간에 줄면 체감이 거꾸로 간다.
        bool monotone = true;
        for (int day = 2; day <= 5; day++)
        {
            var (lo0, hi0) = HorrorAudioDirector.QuotaRange(day - 1);
            var (lo1, hi1) = HorrorAudioDirector.QuotaRange(day);
            if (lo1 < lo0 || hi1 < hi0) monotone = false;
        }
        Check(monotone, "하루 횟수가 DAY 가 가도 줄지 않는다");
        Check(HorrorAudioDirector.QuotaRange(1).Max <= 1, "DAY1 은 많아도 1회");
        Check(HorrorAudioDirector.QuotaRange(5).Max <= 3, "DAY5 도 3회를 넘지 않는다");

        // 휴게시간에는 가까운 소리를 쓰지 않는다 — 추리 구간이다(§21).
        // 같은 키가 먼 소리와 가까운 소리 양쪽에 있으므로 cue 단위로 본다.
        var rest5 = HorrorAudioDirector.PickablesForDay(5, rest: true);
        Check(rest5.Count > 0 && rest5.All(x => !x.Close),
            $"휴게시간에는 가까운 소리가 뽑히지 않는다 ({rest5.Count}개 중 0개)");
        var live5 = HorrorAudioDirector.PickablesForDay(5, rest: false);
        Check(live5.Any(x => x.Close),
            $"근무 중에는 뽑힌다 ({live5.Count(x => x.Close)}개 / {live5.Count}개)");
        var live1 = HorrorAudioDirector.PickablesForDay(1, rest: false);
        Check(live1.All(x => !x.Close), "DAY1 근무에도 가까운 소리가 뽑히지 않는다");
    }

    // ── §20 · §13 울리면 안 되는 상황 ────────────────────────────────
    private void TestSuppression()
    {
        Head("D", "울려서는 안 되는 상황에서 입을 닫는다");

        var dir = HorrorAudioDirector.Instance;
        if (dir == null) { Check(false, "HorrorAudioDirector 가 떠 있다"); return; }
        Check(true, "HorrorAudioDirector 가 떠 있다");

        var sim = FacilitySimulation.Instance;
        GameState.Instance?.ResetRun(2);
        sim?.ResetRun();
        sim?.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        GameState.Instance?.SetPhase(GamePhase.Live);

        // 아무 일도 없을 때는 열려 있어야 한다 — 전부 막혀 있으면 평생 안 울린다.
        string quiet = dir.BlockedReason();
        Check(quiet.Length == 0, $"조용한 근무에서는 막히지 않는다 ({(quiet.Length == 0 ? "열림" : quiet)})");

        // ① 수리 승인 절차 중 — 제한 시간을 재는 중이다. 여기서 whisper 가 끼면 UX 오류다.
        RepairApprovalSystem.Enqueue("core_room", "코어실",
            new NSP.Facility.SpawnedTask { RoomId = "core_room", IsRepair = true, GaugeRequired = 9f });
        RepairApprovalSystem.Tick(1f / 30f);
        Check(dir.BlockedReason().Length > 0, $"승인 절차 중에는 막힌다 ({dir.BlockedReason()})");
        RepairApprovalSystem.ResetAll();

        // ② 사고 중 — 경고음과 겹치면 안 된다.
        sim?.TriggerTutorialAccident("core_room");
        Check(dir.BlockedReason().Length > 0, $"사고 중에는 막힌다 ({dir.BlockedReason()})");

        // ③ 스토리 전환 중 — 대사를 듣다가 whisper 가 끼면 분위기가 깨진다(§20).
        //    StoryTransition.Active 는 private set 이라 실제 전환으로만 켜진다.
        //    여기서는 "스토리 중" 조건이 BlockedReason 에 들어 있는지 코드로 확인한다.
        Check(BlockedReasonMentions("스토리"), "BlockedReason 에 스토리 조건이 들어 있다");
        Check(BlockedReasonMentions("통화"), "BlockedReason 에 통화 조건이 들어 있다");
        Check(BlockedReasonMentions("괴물"), "BlockedReason 에 괴물 조건이 들어 있다");
    }

    // BlockedReason 이 그 조건을 실제로 보는지 — 소스에 그 문구가 있는지로 확인한다.
    // (헤드리스에서 통화 · 괴물 상태를 진짜로 만들려면 씬 전체가 필요하다.)
    private static bool BlockedReasonMentions(string word)
    {
        using var f = FileAccess.Open("res://scripts/ui/HorrorAudioDirector.cs", FileAccess.ModeFlags.Read);
        if (f == null) return false;
        string src = f.GetAsText();
        int from = src.IndexOf("public string BlockedReason()", System.StringComparison.Ordinal);
        if (from < 0) return false;
        int to = src.IndexOf("public override void _Process", from, System.StringComparison.Ordinal);
        if (to < 0) to = src.Length;
        return src[from..to].Contains(word);
    }

    // ── §9 Shift Card ────────────────────────────────────────────────
    private void TestShiftCard()
    {
        Head("E", "Shift Card 가 실제 수치를 쓴다");

        GameState.Instance?.ResetRun(3);
        GameState.Instance?.AddCoreProgress(44f, "검사");
        var lines = ShiftCardView.Lines(3);
        foreach (string l in lines) GD.Print($"      {l}");

        Check(lines[0] == "DAY 3", $"DAY 줄이 지금 날짜다 ({lines[0]})");
        Check(lines[1].Contains("44"), $"코어 줄이 실제 복구율이다 ({lines[1]})");
        Check(ShiftCardView.GoalPercent(3) > 0f, $"오늘 목표를 목표표에서 읽는다 ({lines[2]})");
        // 지시서 §9 의 예: DAY3 · 전체 5일이면 48시간.
        Check(ShiftCardView.ShieldHoursLeft(3) == 48, $"차폐 남은 시간이 (전체일-오늘)×24 다 ({lines[3]})");
        Check(ShiftCardView.ShieldHoursLeft(5) == 0, "마지막 날은 0시간");

        // 하드코딩이 아닌가 — DAY 가 바뀌면 전부 따라 바뀌어야 한다(§9).
        GameState.Instance?.ResetRun(2);
        GameState.Instance?.AddCoreProgress(11f, "검사");
        var l2 = ShiftCardView.Lines(2);
        Check(l2[0] == "DAY 2" && l2[1].Contains("11") && l2[3].Contains("72"),
            $"DAY2 에서는 전부 다른 값이 나온다 ({l2[0]} / {l2[1]} / {l2[3]})");
    }

    // ── §10 fail-safe ────────────────────────────────────────────────
    //
    // 암전이 남으면 그 뒤로 게임이 보이지 않는다. 가장 치명적인 항목이다.
    private void TestFailSafe()
    {
        Head("F", "어떻게 끝나도 전부 되돌아온다");

        var blink = BlinkOverlay.Instance;
        var card = ShiftCardView.Instance;
        if (blink == null || card == null)
        {
            Check(false, "BlinkOverlay · ShiftCardView 가 떠 있다");
            return;
        }
        Check(true, "BlinkOverlay · ShiftCardView 가 떠 있다");

        // 암전 · 카드 · 더킹 · 스토리 BGM 을 한꺼번에 걸어 놓고 Release 를 때린다.
        _ = blink.Close(0.4);
        Sfx.Instance?.DuckMusic(-5f, 5f);
        Sfx.Instance?.CrossfadeMusic(StoryTransition.StoryMusic, 0.1f, true, 0f, -9f, true);
        Sfx.Instance?.PlayHorror("whisper_short", -40f, 0.8f);

        StoryTransition.Release();

        Check(!blink.IsClosed, "암전이 풀렸다");
        Check(!card.IsShown, "Shift Card 가 사라졌다");
        Check(Mathf.Abs(Sfx.Instance?.DuckDb ?? 0f) < 0.01f, $"BGM 더킹이 0 으로 돌아왔다 ({Sfx.Instance?.DuckDb:0.00}dB)");
        Check(Sfx.Instance?.HorrorPlaying != true, "울리던 공포음이 끊겼다");
        Check(Sfx.Instance?.CurrentMusic != StoryTransition.StoryMusic,
            $"스토리 BGM 이 남아 있지 않다 (지금 '{Sfx.Instance?.CurrentMusic}')");
        Check(!StoryTransition.Active, "전환 상태 깃발이 내려갔다");
        // 몇 번 불러도 안전해야 한다 — 예외 경로가 겹쳐 들어올 수 있다.
        StoryTransition.Release();
        StoryTransition.Release();
        Check(!blink.IsClosed, "여러 번 불러도 괜찮다");
    }

    // ── §22 버스 구조 ────────────────────────────────────────────────
    private void TestBuses()
    {
        Head("G", "오디오 버스 구조를 갈아엎지 않았다");

        foreach (string bus in new[] { GameSettings.BusMaster, GameSettings.BusBgm, GameSettings.BusSfx,
                                       GameSettings.BusRadio, GameSettings.BusScream, GameSettings.BusHorror })
            Check(AudioServer.GetBusIndex(bus) >= 0, $"'{bus}' 버스");

        int horror = AudioServer.GetBusIndex(GameSettings.BusHorror);
        Check(AudioServer.GetBusSend(horror) == GameSettings.BusSfx,
            "Horror 는 SFX 로 보낸다 — 효과음 볼륨 설정을 그대로 따른다");
        Check(AudioServer.GetBusEffect(horror, GameSettings.HorrorPannerIndex) is AudioEffectPanner,
            "Horror 버스 맨 앞에 패너가 있다");

        // 패닝이 실제로 값을 받는가(§14).
        GameSettings.SetHorrorPan(0.8f);
        var p = AudioServer.GetBusEffect(horror, GameSettings.HorrorPannerIndex) as AudioEffectPanner;
        Check(p != null && Mathf.Abs(p.Pan - 0.8f) < 0.01f, $"pan 을 넣으면 반영된다 ({p?.Pan:0.00})");
        // 극단값은 잘라 낸다 — ±1 이면 헤드폰에서 머리 밖에 붙는다.
        GameSettings.SetHorrorPan(-5f);
        Check(p != null && p.Pan >= -0.96f, $"극단값은 잘린다 ({p?.Pan:0.00})");
        GameSettings.SetHorrorPan(0f);

        // BGM 더킹은 BGM 버스 앰프로만 한다 — 사용자 볼륨 설정과 섞이지 않게.
        int bgm = AudioServer.GetBusIndex(GameSettings.BusBgm);
        Check(AudioServer.GetBusEffect(bgm, 0) is AudioEffectAmplify, "BGM 버스 맨 앞에 더킹용 앰프가 있다");
    }

    // ── 공통 ──────────────────────────────────────────────────────────

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
