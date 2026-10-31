using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 딴짓 12종 검사 — 실제 MainScene3D_Test 로 근무를 띄우고, 실제 CCTV 조건으로 확인한다.
//
//   godot --path . res://scenes/debug/DistractionTest.tscn -- <저장 폴더> [data|act|play|all]
//     data : 12종 등록 · 클립 존재 · 대사 · 색 (근무 없이)
//     act  : 적발 조건(전원 OFF · 다른 방 · 채널 전환) · 우선순위 · 개인 통화 · 교차 질문
//     play : DAY1~3 을 실제로 돌리며 "CCTV 를 정상적으로 쓰는 플레이" 의 목격 횟수(2~5) 확인
//     shot : 12종을 차례로 일으켜 **실제 CCTV 구도**로 단계별 사진을 찍는다(창 모드 필요)
//
// **가짜 목격을 만들지 않는다.** 모든 목격은 실제 피드 조건(전력 · 채널 · 신호 · CRT 표시 ·
// 직원이 화면에 보이는 상태)을 그대로 통과한 것만 센다. CCTV 를 끈 채로 2회를 보장하지 않는다.
public partial class DistractionTest : Node
{
    private const string LibPath = "res://scenes/cctv_characters/anim/employee_common.tres";

    // 검사용 배치 — 한 방에 두 명씩 넣어 "한 명만 반응하는가" 까지 본다.
    private static readonly (string Room, string[] Ids)[] Layout =
    {
        ("maintenance_room", new[] { "fox", "sheep" }),
        ("guard_room", new[] { "rabbit", "wolf" }),
        ("core_room", new[] { "cat" }),
        ("power_room", new[] { "dog" }),
    };

    private string _dir = "";
    private string _mode = "all";
    private readonly StringBuilder _report = new();
    private int _fails;
    private bool _fromHub;
    private SubViewport _keepDrawing;

    public override void _Ready()
    {
        ProcessPriority = 1000;
        _ = Run();
    }

    public override void _Process(double delta)
    {
        if (_keepDrawing != null && IsInstanceValid(_keepDrawing))
            _keepDrawing.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _fromHub = args.Length == 0;
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _mode = args.Length > 1 ? args[1] : "all";

        if (_mode is "data" or "all") CheckData();

        if (_mode is "act" or "play" or "shot" or "all")
        {
            DebugEntryPoint.SkipTitleOnNextBoot();
            AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
            for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
            await Frames(30);
            var sim = FacilitySimulation.Instance;
            var ctl = ControlRoom3DController.Instance;
            if (sim == null || ctl == null) { Fail("씬이 뜨지 않았다"); Finish(); return; }
            _keepDrawing = ctl.FacilityCctvViewport;

            if (_mode is "act" or "all") await RunAct(sim);
            if (_mode is "shot") await RunShot(sim);
            if (_mode is "play" or "all") await RunPlay(sim);
        }

        Finish();
    }

    // ── ① 데이터 ────────────────────────────────────────────────────────

    private void CheckData()
    {
        Log("── 데이터 ──");
        var all = DistractionCatalog.All;
        Check(all.Count == 12, $"딴짓 12종 등록 ({all.Count})");
        foreach (string id in new[] { "fox", "sheep", "cat", "rabbit", "wolf", "dog" })
        {
            var two = DistractionCatalog.For(id);
            Check(two.Count == 2, $"{id} 딴짓 2종 ({two.Count}: {string.Join(",", two.Select(d => d.Code))})");
        }

        var strong = all.Where(d => d.Strong).Select(d => d.Code).OrderBy(c => c).ToList();
        Check(strong.SequenceEqual(new[] { "FOX_02", "WOLF_02" }),
            "강한 지적 문구는 FOX_02, WOLF_02 뿐 - " + string.Join(",", strong));
        var mood = all.Where(d => d.NeedsGoodMood).Select(d => d.Code).ToList();
        Check(mood.SequenceEqual(new[] { "RABBIT_02" }), "기분 좋은 날 전용은 RABBIT_02 뿐 - " + string.Join(",", mood));
        Check(DistractionMood.IsGood("기분 좋음") && DistractionMood.IsGood("활기참")
              && !DistractionMood.IsGood("나쁘지 않음") && !DistractionMood.IsGood("걱정됨"),
            "기분 판정 - 분명히 좋은 표현만 통과(스트레스가 낮다는 이유로는 안 된다)");

        var paired = all.Where(d => !string.IsNullOrEmpty(d.PairedWith)).ToList();
        Check(paired.Count == 2 && paired.All(d => DistractionCatalog.For(d.PairedWith)
                  .Any(o => DistractionCatalog.IsPersonalCall(o.Kind))),
            "개인 통화 짝 - 고양이 ↔ 강아지");

        // 클립이 실제로 라이브러리에 있는가.
        var lib = GD.Load<AnimationLibrary>(LibPath);
        if (lib == null) { Fail("애니메이션 라이브러리를 읽지 못했다"); return; }
        int clips = 0, missing = 0;
        var seen = new HashSet<string>();
        foreach (var def in all)
        {
            var p = DistractionProfiles.Get(def.Kind);
            if (p == null) { Fail($"{def.Code}: 표현 프로필 없음"); continue; }
            foreach (string c in new[] { p.PreClip, p.EnterClip, p.LoopClip, p.NoticeClip, p.RecoverClip })
            {
                if (string.IsNullOrEmpty(c)) continue;
                if (!seen.Add(c)) continue;
                clips++;
                if (!lib.HasAnimation(c)) { missing++; Fail($"{def.Code}: 클립 없음 - {c}"); }
            }
            Check(!string.IsNullOrEmpty(p.LoopClip) && !string.IsNullOrEmpty(p.NoticeClip)
                  && !string.IsNullOrEmpty(p.RecoverClip), $"{def.Code}: 반복/적발/복귀 클립이 다 있다");
        }
        Check(missing == 0, $"딴짓 클립 {clips}종 전부 라이브러리에 있다");

        // 적발 클립은 짧고(0.15~0.35초에 반응 시작) 반복 클립은 보일 만큼 길어야 한다.
        foreach (var def in all)
        {
            var p = DistractionProfiles.Get(def.Kind);
            if (p == null) continue;
            var loop = lib.GetAnimation(p.LoopClip);
            if (loop != null)
                Check(loop.LoopMode == Animation.LoopModeEnum.Linear && loop.Length >= 2.4f,
                    $"{def.Code}: 반복 클립 {loop.Length:0.0}s, loop={loop.LoopMode}");
        }

        // 설비를 만지는 딴짓은 손 거리 값이 있어야 한다(허공을 두드리지 않게).
        foreach (string c in new[] { "dis_rabbit_peek_loop", "dis_dog_probe_loop", "dis_wolf_kick_loop" })
            Check(DistractionClipReach.TryGet(c, out float f, out float h) && f > 0.2f,
                $"손 거리 {c} = {(DistractionClipReach.TryGet(c, out float ff, out float hh) ? $"{ff:0.00}m h{hh:0.00}" : "없음")}");

        // 대사 — 지정 문구 그대로인가.
        Check(DistractionDialogue.Question(Ev(DistractionKind.FoxFloorVideo)) == "지금 뭐하는 겁니까?",
            "FOX_02 질문 문구");
        Check(DistractionDialogue.Question(Ev(DistractionKind.WolfKick)) == "지금 뭐하는 겁니까?",
            "WOLF_02 질문 문구");
        int mild = all.Count(d => DistractionDialogue.Question(Ev(d.Kind)) == DistractionDialogue.AskMild);
        Check(mild == 10, $"나머지 10종은 \"뭐하고 계셨습니까?\" ({mild})");
        foreach (var def in all)
            Check(DistractionDialogue.Answer(Ev(def.Kind)).Length > 8, $"{def.Code} 대답 있음");
        Check(DistractionDialogue.Answer(Ev(DistractionKind.RabbitPeek, "정비실")).Contains("정비실"),
            "RABBIT_01 대답에 실제 작업실 이름이 들어간다");
        Check(DistractionDialogue.CrossQuestion("dog") == "고양이 직원과 통화했습니까?"
              && DistractionDialogue.CrossQuestion("cat") == "강아지 직원과 통화했습니까?",
            "교차 확인 질문 문구");
        Check(DistractionDialogue.Text.R8 == 0xF4 && DistractionDialogue.Text.G8 == 0xA4
              && DistractionDialogue.Hover.R8 == 0xFF && DistractionDialogue.Hover.G8 == 0x77,
            "선택지 색 #F4A4A4 / Hover #FF7777");
    }

    private static DistractionEvent Ev(DistractionKind kind, string room = "발전실") =>
        new() { Kind = kind, RoomName = room, EmployeeId = DistractionCatalog.Get(kind)?.EmployeeId ?? "" };

    // ── ② 적발 조건 · 우선순위 · 개인 통화 ───────────────────────────────

    private async System.Threading.Tasks.Task RunAct(FacilitySimulation sim)
    {
        Log("");
        Log("── 적발 조건 ──");
        await StartShift(sim);
        var ds = sim.Distractions;
        // 검사 중에는 정해진 딴짓 하나만 본다 — 스스로 만드는 기회는 ③ 에서 확인한다.
        ds.AutoSpawn = false;
        var world = FacilityCctvWorld.Instance;
        Log("방별 딴짓 자리: " + string.Join(", ", Layout.Select(l =>
            $"{l.Room}=[{string.Join(",", System.Enum.GetValues<DistractionPlace>().Where(x => DistractionPlaces.Has(l.Room, x)))}]")));

        await SceneWitness(sim, ds, world);
        await ScenePersonalCall(sim, ds);
        await SceneKeepState(sim, ds, world);
        await ScenePowerOff(sim, ds);
        await SceneOtherRoom(sim, ds);
        await ScenePriority(sim, ds, world);
        // 스스로 만드는 기회까지 돌려 보므로 맨 뒤에 둔다(앞 장면이 쓸 딴짓을 먼저 써 버리지 않게).
        await SceneNoStartWhileWatched(sim, ds);
        ds.AutoSpawn = true;

        Log("");
        var hist = DialogueHistory.Instance;
        Log($"실제 발생 {ds.StartedToday}회, 목격 {ds.WitnessedToday}회, 대화 기록 {hist?.GetAllEntries()?.Count ?? -1}줄");
        foreach (var e in ds.Events)
            Log($"  {e.EventId} {e.Code} {e.EmployeeId}@{e.RoomName} D{e.Day} {e.StartedAt:0.0}s "
                + $"목격={(e.HasBeenObserved ? $"{e.ObservedAt:0.0}s" : "-")} 질문={e.HasBeenAskedByManager} "
                + $"통화={e.LinkedPhoneCallEventId} 중단={(e.Aborted ? e.AbortReason : "-")}");
        foreach (var c in ds.PersonalCalls)
            Log($"  {c.EventId} D{c.Day} {c.AId}↔{c.BId} {c.StartedAt:0.0}~{c.EndedAt:0.0}s "
                + $"끝={c.EndReason} 실토=[{string.Join(",", c.Admitted)}] 교차질문=[{string.Join(",", c.CrossAsked)}]");
    }

    // ⓪ 보는 앞에서는 시작하지 않는다(2026-10-29).
    //    · 스스로 만드는 기회는 지금 띄워 둔 방을 고르지 않는다.
    //    · 자리를 잡는 사이에 그 방이 띄워지면 딴짓을 접고 하던 일로 돌아간다(목격 기록 없음).
    private async System.Threading.Tasks.Task SceneNoStartWhileWatched(FacilitySimulation sim, DistractionSystem ds)
    {
        Log("");
        await Ensure(sim, 40f);
        await Watch(sim, "maintenance_room");
        int startedBefore = ds.StartedToday;
        ds.AutoSpawn = true;
        for (int k = 0; k < 40; k++)
        {
            ds.RushNextChance();
            await Frames(2);
            var made = ds.Events.LastOrDefault(e => e.Day == GameState.Instance.CurrentDay);
            if (ds.StartedToday > startedBefore && made != null) break;
        }
        ds.AutoSpawn = false;
        var spawned = ds.Events.Where(e => e.Day == GameState.Instance.CurrentDay).Skip(startedBefore).ToList();
        Check(spawned.All(e => e.RoomId != "maintenance_room"),
            $"스스로 시작한 딴짓은 지금 보고 있는 방(정비실)에서 일어나지 않는다 - "
            + (spawned.Count == 0 ? "이번엔 발생 없음" : string.Join(",", spawned.Select(e => $"{e.Code}@{e.RoomName}"))));
        foreach (var e in spawned)
            for (int i = 0; i < 60 * 40 && e.Running; i++) await Frame();

        // 보고 있는 방에서 억지로 일으켜 본다 — 자리를 잡기도 전에 카메라를 의식하고 돌아간다.
        var ev = await Force(sim, DistractionKind.SheepDoze);
        Check(ev != null, "SHEEP 딴짓을 보고 있는 방에서 일으켜 본다");
        if (ev == null) return;
        for (int i = 0; i < 60 * 3 && ev.Phase == DistractionPhase.Entering; i++) await Frame();
        Check(ev.Aborted && ev.AbortReason == "관측 중" && ev.Phase is DistractionPhase.Returning or DistractionPhase.Done,
            $"보는 앞에서는 딴짓을 시작하지 않는다 (phase={ev.Phase} 중단={ev.AbortReason})");
        Check(!ev.HasBeenObserved, "시작하지 않은 딴짓은 목격으로 세지 않는다");
        for (int i = 0; i < 60 * 20 && ev.Running; i++) await Frame();
    }

    // ① 안 보는 사이에 시작된 딴짓을, 그 방을 띄워 잡는다 — 목격이 성립하고 3~4초 뒤 놀라며,
    //    전화에 전용 선택지가 뜬다. (보는 앞에서는 시작하지 않는다 — ⓪ SceneNoStartWhileWatched.)
    private async System.Threading.Tasks.Task SceneWitness(FacilitySimulation sim, DistractionSystem ds,
                                                           FacilityCctvWorld world)
    {
        await Ensure(sim, 40f);
        await Watch(sim, "core_room");
        var ev = await Force(sim, DistractionKind.FoxPhone);
        Check(ev != null, "FOX_01 발생(다른 방을 보는 사이)");
        if (ev == null) return;
        // 자리를 잡고 실제로 딴짓을 하는 중에 그 방을 띄운다.
        for (int i = 0; i < 60 * 20 && ev.Running && ev.Phase != DistractionPhase.Distracted; i++) await Frame();
        await Seconds(0.8);
        await Watch(sim, "maintenance_room", 0);
        float watchAt = GameState.Instance?.DayTimeSeconds ?? 0f;
        await Frames(6);   // 화면에 처음 잡히는 순간의 자리 맞춤(SnapToGoal)은 순간이동으로 세지 않는다

        var seq = new List<string>();
        var phases = new List<string>();
        float jump = 0f;
        Vector3? last = null;
        float noticedAt = -1f;
        for (int i = 0; i < 60 * 30 && ev.Running; i++)
        {
            Track(world, "fox", seq, ref jump, ref last);
            if (phases.Count == 0 || phases[^1] != ev.Phase.ToString())
            {
                phases.Add(ev.Phase.ToString());
                float now = GameState.Instance?.DayTimeSeconds ?? 0f;
                if (ev.Phase == DistractionPhase.Noticed) noticedAt = now;
            }
            await Frame();
        }
        Log($"FOX_01 단계: {string.Join(">", phases)}");
        Log($"FOX_01 클립: {string.Join(">", seq)}");
        Check(ev.HasBeenObserved, "그 방을 띄웠다 - 목격됨");
        Check(ev.HasReactedToCctv, "적발 반응 재생됨");
        // 보자마자 놀라지 않는다 — 그 방을 띄운 뒤 3~4초 지나서 알아챈다(2026-10-29).
        float react = noticedAt - watchAt;
        Check(noticedAt >= 0f && react >= 2.8f && react <= 5.0f, $"채널을 맞춘 뒤 3~4초 지나 놀란다({react:0.0}s)");
        Check(ds.WitnessedTodayOf("fox").Count == 1, "같은 사건을 한 번만 집계");
        Check(phases.Contains("Noticed") && phases.Contains("Recovering") && phases.Contains("Returning"),
            "적발 → 정리 → 복귀 단계가 모두 지나갔다");
        Check(seq.Contains("dis_fox_phone_loop")
              && seq.Contains("dis_fox_phone_notice") && seq.Contains("dis_fox_phone_recover"),
            "반복 → 적발 → 복귀 클립이 순서대로 재생됐다");
        Check(jump < 0.3f, $"순간이동 없음(한 프레임 최대 {jump:0.00}m)");
        Check(ds.ActiveOf("sheep") == null, "같은 방 양은 딴짓 중이 아니다");
        string sheepClip = Clip(world, "sheep");
        Check(!sheepClip.StartsWith("dis_"), $"같은 방 양은 딴짓 모션이 아니다 ({sheepClip})");
        await Seconds(4);
        string back = Clip(world, "fox");
        Check(!back.StartsWith("dis_") && back != "walk", $"원래 작업 모션 복귀 ({back})");

        // ── 전화 — 실제 통화창을 열어 선택지와 기록까지 확인한다 ──
        var hud = PhoneCallHud.Instance;
        if (hud == null) { Fail("통화창(PhoneCallHud)이 없다"); return; }
        hud.Open("fox");
        var btns = await WaitChoices(hud);
        Log("통화 선택지: " + string.Join(" | ", btns.Select(b => b.Text.Trim())));
        var red = btns.FirstOrDefault(b => b.Text.Contains(DistractionDialogue.AskMild));
        Check(red != null, "목격한 딴짓 질문이 통화 선택지에 뜬다");
        Check(red != null && btns.IndexOf(red) == 0, "그 질문이 맨 위에 있다");
        Check(red != null && red.GetThemeColor("font_color").R8 == DistractionDialogue.Text.R8
              && red.GetThemeColor("font_color").G8 == DistractionDialogue.Text.G8,
            "연한 붉은색(#F4A4A4)으로 표시된다");
        int before = DialogueHistory.Instance?.GetAllEntries()?.Count ?? 0;
        if (red != null) await Press(red);
        var after = await WaitChoices(hud);
        var log = DialogueHistory.Instance?.GetAllEntries();
        Check(log != null && log.Count >= before + 2, $"질문과 대답이 대화 기록에 남는다 ({before}→{log?.Count})");
        Check(log != null && log.Any(e => e.Text.Contains("잠깐 폰 좀 봤어요")), "FOX_01 지정 대사 그대로 답한다");
        Check(ds.LatestUnasked("fox") == null, "한 번 물은 사건은 다시 질문으로 뜨지 않는다");
        Check(!after.Any(b => b.Text.Contains(DistractionDialogue.AskMild)), "같은 질문이 목록에 남아 있지 않다");
        Check(after.Any(b => b.Text.Contains("작업은 잘 되어가나요")), "기존 일반 질문은 그대로 있다");
        Check(ds.WitnessedTodayOf("fox").Count == 1, "목격 기록 자체는 남는다(조사 연결용)");
        CloseHud(hud);
    }

    // ② 개인 통화 — 두 사람이 하나의 통화 사건을 공유하고, 교차 확인 질문이 한 번만 열린다.
    private async System.Threading.Tasks.Task ScenePersonalCall(FacilitySimulation sim, DistractionSystem ds)
    {
        Log("");
        await Ensure(sim, 45f);
        await Watch(sim, "storage_room");
        var call = await Force(sim, DistractionKind.CatCall);
        Check(call != null, "CAT_01 발생 - 개인 통화");
        if (call == null) return;
        for (int i = 0; i < 60 * 20 && call.Running && call.Phase != DistractionPhase.Distracted; i++) await Frame();
        await Watch(sim, call.RoomId, 0);

        var dogEv = ds.ActiveOf("dog");
        Check(dogEv != null && dogEv.Kind == DistractionKind.DogCall, "상대(강아지)도 같은 시각에 통화 중이다");
        var pc = ds.CallById(call.LinkedPhoneCallEventId);
        Check(pc != null && dogEv != null && dogEv.LinkedPhoneCallEventId == pc.EventId,
            "두 사람이 **하나의 통화 사건**을 공유한다");
        Check(pc != null && pc.Day == GameState.Instance.CurrentDay && pc.Involves("cat") && pc.Involves("dog"),
            $"통화 사건 - DAY{pc?.Day}, {pc?.AId}↔{pc?.BId}, 시작 {pc?.StartedAt:0.0}s");

        for (int i = 0; i < 60 * 25 && !call.HasBeenObserved && call.Running; i++) await Frame();
        Check(call.HasBeenObserved, "고양이 통화를 CCTV 로 목격");
        Check(ds.PendingCrossQuestion("dog") == null, "목격만으로는 교차 질문이 열리지 않는다");

        var hud = PhoneCallHud.Instance;
        if (hud == null) { Fail("통화창이 없다"); return; }

        // 고양이에게 — "뭐하고 계셨습니까?" → "누구와 통화했습니까?"
        hud.Open("cat");
        var btns = await WaitChoices(hud);
        var q1 = btns.FirstOrDefault(b => b.Text.Contains(DistractionDialogue.AskMild));
        Check(q1 != null && btns.IndexOf(q1) == 0, "고양이 - 목격 질문이 맨 위에");
        if (q1 != null) await Press(q1);
        btns = await WaitChoices(hud);
        var q2 = btns.FirstOrDefault(b => b.Text.Contains(DistractionDialogue.FollowUpAsk));
        Check(q2 != null && btns.IndexOf(q2) == 0, "꼬리질문 \"누구와 통화했습니까?\" 가 이어서 뜬다");
        if (q2 != null) await Press(q2);
        btns = await WaitChoices(hud);
        var log = DialogueHistory.Instance?.GetAllEntries();
        Check(log != null && log.Any(e => e.Text.Contains("강아지 직원이요")), "고양이가 상대를 말한다");
        Check(!btns.Any(b => b.Text.Contains(DistractionDialogue.FollowUpAsk)), "꼬리질문은 한 번만");
        CloseHud(hud);

        // 강아지에게 — 일회용 교차 확인 질문.
        var cross = ds.PendingCrossQuestion("dog");
        Check(cross != null, "고양이 진술 뒤 - 강아지에게 교차 질문이 열린다");
        hud.Open("dog");
        btns = await WaitChoices(hud);
        var q3 = btns.FirstOrDefault(b => b.Text.Contains("고양이 직원과 통화했습니까"));
        Check(q3 != null && btns.IndexOf(q3) == 0, "강아지 - 교차 질문이 일반 질문 목록 맨 위에");
        Check(q3 != null && q3.GetThemeColor("font_color").R8 == DistractionDialogue.Text.R8,
            "교차 질문도 연한 붉은색");
        if (q3 != null) await Press(q3);
        await WaitChoices(hud);
        log = DialogueHistory.Instance?.GetAllEntries();
        Check(log != null && log.Any(e => e.Text.Contains("어떻게 아셨어요")), "강아지가 인정한다");
        Check(ds.PendingCrossQuestion("dog") == null, "교차 질문은 그 사건에 한 번만");
        CloseHud(hud);
        hud.Open("dog");
        btns = await WaitChoices(hud);
        Check(!btns.Any(b => b.Text.Contains("고양이 직원과 통화했습니까")), "다시 걸어도 같은 교차 질문은 없다");
        CloseHud(hud);

        // 반대 방향 — 강아지가 먼저 말한 경우(같은 통화 사건 기준).
        if (dogEv != null)
        {
            ds.MarkAsked(dogEv);
            ds.MarkAdmitted(dogEv);
            var back = ds.PendingCrossQuestion("cat");
            Check(back != null, "강아지 진술 뒤 - 고양이에게 교차 질문이 열린다");
            if (back != null)
            {
                ds.MarkCrossAsked(back, "cat");
                Check(ds.PendingCrossQuestion("cat") == null, "반대 방향도 일회용");
            }
        }

        // 관리자 전화가 연결되면 개인 통화는 끊긴다.
        var still = ds.CallById(call.LinkedPhoneCallEventId);
        bool wasActive = still is { Active: true };
        ds.NotifyManagerCall("cat");
        Check(still != null && !still.Active, $"관리자 전화 - 개인 통화 종료 (끝={still?.EndReason})");
        if (wasActive) Check(still.EndReason == "manager_call", "끊긴 이유가 기록된다");
        for (int i = 0; i < 60 * 30 && (call.Running || dogEv is { Running: true }); i++) await Frame();
    }

    // ③ 채널을 돌렸다 돌아와도 같은 사건이 이어진다.
    private async System.Threading.Tasks.Task SceneKeepState(FacilitySimulation sim, DistractionSystem ds,
                                                             FacilityCctvWorld world)
    {
        Log("");
        await Ensure(sim, 35f);
        await Watch(sim, "core_room");
        var keep = await Force(sim, DistractionKind.RabbitPeek);
        Check(keep != null, "RABBIT_01 발생(경비실)");
        if (keep == null) return;
        for (int i = 0; i < 60 * 20 && keep.Phase != DistractionPhase.Distracted; i++) await Frame();
        await Watch(sim, "guard_room", 0);
        await Seconds(1.2);
        float elapsed = keep.Elapsed;
        int seenCount = ds.WitnessedTodayOf("rabbit").Count;
        sim.SetSurveillanceTarget("core_room");
        await Seconds(2.5);
        sim.SetSurveillanceTarget("guard_room");
        await Frames(4);
        Check(ds.ActiveOf("rabbit")?.EventId == keep.EventId, "돌아와도 같은 사건이다");
        Check(keep.Elapsed > elapsed + 2f, $"보지 않는 동안에도 진행됐다 ({keep.Elapsed - elapsed:0.0}s)");
        string now = Clip(world, "rabbit");
        Check(now != "dis_rabbit_peek_enter", $"복귀 후 진입 동작을 다시 틀지 않는다 ({now})");
        Check(ds.WitnessedTodayOf("rabbit").Count == seenCount, "같은 사건을 두 번 집계하지 않는다");
        for (int i = 0; i < 60 * 40 && keep.Running; i++) await Frame();
    }

    // ④ CCTV 전력 OFF — 딴짓은 일어나지만 목격 기록은 생기지 않는다.
    private async System.Threading.Tasks.Task ScenePowerOff(FacilitySimulation sim, DistractionSystem ds)
    {
        Log("");
        await Ensure(sim, 30f);
        int before = ds.WitnessedToday;
        if (GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch))
            GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
        Check(!GameState.Instance.IsCctvOperational(), "CCTV 전력 OFF");
        await Frames(10);
        var off = await Force(sim, DistractionKind.SheepDoze);
        Check(off != null, "전력 OFF 중에도 딴짓은 실제로 일어난다");
        for (int i = 0; i < 60 * 30 && off is { Phase: DistractionPhase.Entering or DistractionPhase.Distracted }; i++)
            await Frame();
        Check(off != null && !off.HasBeenObserved, "전력 OFF - 목격 기록 없음");
        Check(ds.WitnessedToday == before, $"목격 횟수 그대로 ({ds.WitnessedToday})");
        Check(ds.LatestUnasked("sheep") == null, "목격하지 않은 딴짓은 전화 질문이 없다");
        GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
        await Frames(10);
        Check(GameState.Instance.IsCctvOperational(), "CCTV 전력 복구");
    }

    // ⑤ 다른 방을 보고 있으면 목격되지 않는다.
    private async System.Threading.Tasks.Task SceneOtherRoom(FacilitySimulation sim, DistractionSystem ds)
    {
        Log("");
        await Ensure(sim, 30f);
        await Watch(sim, "core_room");
        int before = ds.WitnessedToday;
        var away = await Force(sim, DistractionKind.WolfWallNap);
        Check(away != null, "WOLF_01 발생(경비실)");
        for (int i = 0; i < 60 * 30 && away is { Phase: DistractionPhase.Entering or DistractionPhase.Distracted }; i++)
            await Frame();
        Check(away != null && !away.HasBeenObserved, "다른 방을 보고 있었다 - 목격 기록 없음");
        Check(ds.WitnessedToday == before, "목격 횟수 그대로");
    }

    // ⑥ 우선순위 — 괴물이 나타나면 딴짓은 즉시 끊긴다.
    private async System.Threading.Tasks.Task ScenePriority(FacilitySimulation sim, DistractionSystem ds,
                                                            FacilityCctvWorld world)
    {
        Log("");
        await Ensure(sim, 30f);
        await Watch(sim, "core_room");
        var cut = await Force(sim, DistractionKind.DogProbe);
        Check(cut != null, "DOG_02 발생(발전실)");
        if (cut == null) return;
        for (int i = 0; i < 60 * 20 && cut.Phase != DistractionPhase.Distracted; i++) await Frame();
        await Watch(sim, "power_room", 0);
        for (int i = 0; i < 60 * 30 && sim.Ghost.Active; i++) await Frame();   // 다른 방 괴물이 물러날 때까지
        bool called = sim.Ghost.ForceAppear("power_room", sim, 30f);
        Check(called && sim.Ghost.ActiveRoomId == "power_room", "발전실에 괴물 등장");
        await Seconds(0.6);
        Check(!cut.Running && cut.Aborted, $"괴물 등장 - 딴짓 즉시 중단 (phase={cut.Phase} aborted={cut.Aborted})");
        Check(ds.ActiveOf("dog") == null, "중단된 뒤에는 딴짓 상태가 남지 않는다");
        string gclip = Clip(world, "dog");
        Check(gclip.StartsWith("ghost_") || gclip is "walk" or "idle", $"괴물 반응이 딴짓을 덮었다 ({gclip})");
        // 괴물이 있는 동안에는 새 딴짓도 시작되지 않는다.
        Check(ds.ForceStart(sim, DistractionKind.DogCall) == null, "괴물이 있는 방에서는 딴짓을 시작하지 않는다");
        for (int i = 0; i < 60 * 60 && sim.Ghost.Active; i++) await Frame();
        await Seconds(2);
    }

    // 그 직원이 딴짓을 할 수 있는 상태가 될 때까지 잠깐 기다렸다가 일으킨다.
    // (경비실 담당은 순찰로 방을 비우는 때가 있어 그 순간에는 시작되지 않는다 — 정상이다.)
    private async System.Threading.Tasks.Task<DistractionEvent> Force(FacilitySimulation sim,
                                                                      DistractionKind kind, float wait = 12f)
    {
        var ds = sim.Distractions;
        double t0 = Now();
        while (Now() - t0 < wait)
        {
            var ev = ds.ForceStart(sim, kind);
            if (ev != null) return ev;
            await Frames(10);
        }
        Log("  (시작 못 함 - " + Why(sim, DistractionCatalog.Get(kind)?.EmployeeId ?? "") + ")");
        return null;
    }

    // 왜 딴짓을 시작하지 못했는가(검사용 상태 덤프).
    private static string Why(FacilitySimulation sim, string id)
    {
        var st = sim.GetEmployeeState(id);
        if (st == null) return id + ": 상태 없음";
        return $"{id} room={st.CurrentRoomId} moving={st.IsMoving} target={st.TargetRoomId} "
             + $"iso={st.Isolated}/{st.Isolation} faint={st.Faint} carry={st.CarryingVictimId} "
             + $"responder={st.ResponderId} blocked={st.BlockedByBarrier} "
             + $"workBlock={st.WorkBlockedUntil - GameState.Instance.DayTimeSeconds:0.0}s "
             + $"onDuty={sim.IsOnDuty(id)} panic={sim.IsPanicked(id)} "
             + $"ghost={(sim.Ghost.Active ? sim.Ghost.ActiveRoomId : "-")} "
             + $"victim={sim.Rescue?.FindUnmovedVictimIn(st.CurrentRoomId)?.EmployeeId ?? "-"} "
             + $"active={sim.Distractions.ActiveOf(id)?.Code ?? "-"}";
    }

    // 그 방을 실제로 비출 때까지 기다린다.
    //
    // 사고·괴물 연출이 CCTV 를 강제로 다른 화면(복도 등)으로 돌리는 때가 있다
    // (FacilitySimulation.IsSurveillanceForced). 그 동안에는 관리자가 그 방을 **보고 있지 않은 것**이
    // 맞으므로 목격도 성립하지 않는다 — 검사에서는 강제가 풀릴 때까지 기다렸다 본다.
    private async System.Threading.Tasks.Task Watch(FacilitySimulation sim, string room, double settle = 3.6)
    {
        for (int i = 0; i < 60 * 25; i++)
        {
            sim.SetSurveillanceTarget(room);
            if (sim.SurveillanceTargetRoomId == room) break;
            await Frame();
        }
        if (sim.SurveillanceTargetRoomId != room)
            Log($"  (CCTV 가 {room} 로 가지 않는다 - 지금 {sim.SurveillanceTargetRoomId}/{sim.SurveillanceCorridorId})");
        await Seconds(settle);
    }

    // 남은 근무 시간이 모자라면 다음 날로 넘겨 새 근무에서 이어 본다.
    private async System.Threading.Tasks.Task Ensure(FacilitySimulation sim, float need)
    {
        float left = DayObjectives.RemainingSeconds;
        if (GameState.Instance.CurrentPhase == GamePhase.Live && left > need) return;
        Log($"  (남은 근무 {left:0}s - 다음 날 근무에서 이어 본다)");
        await NextDay(sim);
        await StartShift(sim);
        sim.Distractions.AutoSpawn = false;
        float t0 = GameState.Instance.DayTimeSeconds;
        await Seconds(1.0);
        Log($"  (DAY{GameState.Instance.CurrentDay} 시계 {t0:0.0}→{GameState.Instance.DayTimeSeconds:0.0}s, "
            + $"컷인정지={StoryCutinDirector.PausesGameplay} 패드정지={AdminPad3D.PausesGame} 사망연출={AdminDeathDirector.IsPlaying})");
    }

    // ── 통화창 다루기(검사 전용) ─────────────────────────────────────────

    private static List<Button> ChoiceButtons(PhoneCallHud hud)
    {
        var box = typeof(PhoneCallHud)
            .GetField("_choices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(hud) as Control;
        var list = new List<Button>();
        if (box == null) return list;
        foreach (var c in box.GetChildren())
            if (c is Button b) list.Add(b);
        return list;
    }

    private async System.Threading.Tasks.Task<List<Button>> WaitChoices(PhoneCallHud hud)
    {
        // 대사가 다 찍힌 뒤에 선택지가 생긴다 — 비었다가 다시 찰 때까지 기다린다.
        for (int i = 0; i < 60 * 12; i++)
        {
            var list = ChoiceButtons(hud);
            if (list.Count > 0) return list;
            await Frame();
        }
        return ChoiceButtons(hud);
    }

    private async System.Threading.Tasks.Task Press(Button b)
    {
        b.EmitSignal(Button.SignalName.Pressed);
        // 선택지가 비워질 때까지(새 대사가 시작될 때까지) 한 프레임 이상 기다린다.
        for (int i = 0; i < 30 && ChoiceButtons((PhoneCallHud)b.Owner ?? PhoneCallHud.Instance).Count > 0; i++)
            await Frame();
    }

    private static void CloseHud(PhoneCallHud hud)
    {
        typeof(PhoneCallHud)
            .GetMethod("CloseCall", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(hud, null);
    }

    // ── ③ 일반적인 CCTV 플레이에서 목격 2~5회 ────────────────────────────

    private async System.Threading.Tasks.Task RunPlay(FacilitySimulation sim)
    {
        Log("");
        Log("── DAY1~3, CCTV 를 정상적으로 쓰는 플레이 ──");
        // 재현 가능한 감시 시나리오: 사람이 있는 방을 6초마다 차례로 본다(실제 조작과 같은 경로).
        var rooms = Layout.Select(l => l.Room).ToArray();
        for (int day = 1; day <= 3; day++)
        {
            if (GameState.Instance.CurrentPhase != GamePhase.Schedule) await ToSchedule(sim);
            await StartShift(sim);
            int idx = 0;
            double nextSwitch = 0;
            var ds = sim.Distractions;
            double t0 = Now();
            int frames = 0, feedOk = 0, powerOk = 0, onScreen = 0, forced = 0;
            bool wasOp = true;
            double nextPower = 0;
            while (GameState.Instance.CurrentPhase == GamePhase.Live && Now() - t0 < 200)
            {
                if (Now() >= nextSwitch)
                {
                    sim.SetSurveillanceTarget(rooms[idx % rooms.Length]);
                    idx++;
                    nextSwitch = Now() + 6.0;
                }
                // CCTV 전력이 내려가면 플레이어는 당연히 다시 켠다 — 그 행동까지가 "정상적인 CCTV 플레이" 다.
                // (전력이 모자라 못 켜면 그대로 둔다. 켜지지 않은 동안의 목격은 없다.)
                if (Now() >= nextPower && !GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch))
                {
                    GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
                    nextPower = Now() + 2.0;
                }
                // 관리자가 "지금 실제로 볼 수 있는 상태인가" 를 같이 센다(목격이 0 일 때의 원인 추적).
                frames++;
                bool op = GameState.Instance.IsCctvOperational();
                if (op != wasOp)
                {
                    wasOp = op;
                    Log($"    CCTV {(op ? "복구" : "꺼짐")} - {GameState.Instance.DayTimeSeconds:0}s "
                        + $"(전력 {GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch)}, 설비고장 {!op && GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch)})");
                }
                if (op) powerOk++;
                if (CCTVMonitorView.Instance?.FeedVisible == true) feedOk++;
                if (ControlRoom3DController.Instance?.CctvOnScreen == true) onScreen++;
                if (sim.SurveillanceTargetRoomId != rooms[(idx - 1) % rooms.Length]) forced++;
                await Frame();
            }
            Log($"  CCTV 상태 - 전력 {100f * powerOk / Mathf.Max(1, frames):0}%, 피드 {100f * feedOk / Mathf.Max(1, frames):0}%"
                + $", CRT {100f * onScreen / Mathf.Max(1, frames):0}%, 강제전환 {100f * forced / Mathf.Max(1, frames):0}%");
            int seen = ds.WitnessedToday;
            int made = ds.StartedToday;
            var people = ds.Events.Where(e => e.Day == GameState.Instance.CurrentDay && e.HasBeenObserved)
                .Select(e => e.Code).ToList();
            // 2회 이상은 **CCTV 를 실제로 쓸 수 있었던 근무**에서만 따진다.
            // 발전 사고로 전력 용량이 모자라 화면이 절반 넘게 꺼져 있던 근무까지 2회를 요구하면,
            // 보지도 않은 목격을 만들어 내라는 말이 된다(지시서 §2 — 그건 금지다).
            float uptime = (float)powerOk / Mathf.Max(1, frames);
            string head = $"DAY{GameState.Instance.CurrentDay}: 발생 {made}회, 목격 {seen}회 "
                          + $"({string.Join(",", people)}), CCTV 가동 {uptime * 100f:0}%";
            if (uptime >= 0.7f) Check(seen is >= 2 and <= 5, head);
            else
            {
                Check(seen <= 5, head + " - 화면이 꺼져 있던 시간이 길어 하한은 따지지 않는다");
                Log("    (전력 용량이 모자라 CCTV 를 다시 켤 수 없었다 - 발전 사고를 수리해야 하는 근무다)");
            }
            Check(made <= DistractionSystem.MaxEventsPerDay, $"DAY{GameState.Instance.CurrentDay}: 발생 상한 지킴");
            var dupes = ds.Events.Where(e => e.Day == GameState.Instance.CurrentDay)
                .GroupBy(e => e.EmployeeId + e.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check(dupes.Count == 0, "같은 직원이 같은 딴짓을 두 번 하지 않았다 " + string.Join(",", dupes));
            var many = ds.Events.Where(e => e.Day == GameState.Instance.CurrentDay)
                .GroupBy(e => e.EmployeeId).Where(g => g.Count() > DistractionSystem.MaxEventsPerEmployeePerDay)
                .Select(g => g.Key).ToList();
            Check(many.Count == 0, "한 직원이 하루 2회를 넘지 않았다 " + string.Join(",", many));

            if (day < 3) await NextDay(sim);
        }
    }

    // ── ④ 실제 CCTV 구도 사진 ────────────────────────────────────────────
    //
    // 12종을 차례로 일으켜 단계마다(진입 · 딴짓 · 적발 · 정리) 한 장씩 찍는다.
    // 창 모드로 실행해야 한다 — 헤드리스는 그림을 그리지 않는다.
    private readonly List<(string Label, Image Img)> _shots = new();

    private async System.Threading.Tasks.Task RunShot(FacilitySimulation sim)
    {
        Log("");
        Log("── 실제 CCTV 구도 사진 ──");
        await StartShift(sim);
        var ds = sim.Distractions;
        ds.AutoSpawn = false;
        // 진입 동작까지 찍으려면 그 방을 띄워 둔 채로 일으켜야 한다(게임에서는 보는 앞에서 시작하지 않는다).
        ds.AllowStartWhileWatched = true;
        var ctl = ControlRoom3DController.Instance;
        var cctv = ctl?.FacilityCctvViewport;
        if (cctv == null) { Fail("CCTV 뷰포트가 없다"); return; }
        cctv.Size = new Vector2I(960, 720);
        _keepDrawing = cctv;

        // 네 번째 인자로 코드를 주면 그것만 찍는다(예: FOX_01,CAT_01).
        var args = OS.GetCmdlineUserArgs();
        string only = args.Length > 2 ? args[2] : "";
        foreach (var def in DistractionCatalog.All)
        {
            if (only.Length > 0 && !only.Split(',').Contains(def.Code)) continue;
            await Ensure(sim, 34f);
            string room = sim.GetEmployeeState(def.EmployeeId)?.CurrentRoomId ?? "";
            await Watch(sim, room);                // 실제로 그 방이 보일 때까지 + 관측 인정 시간
            var ev = await Force(sim, def.Kind);
            if (ev == null) { Fail($"{def.Code} 발생 실패"); continue; }
            Log($"{def.Code} {def.EmployeeId}@{sim.RoomDisplayName(room)}");

            await ShotAt(cctv, ev, DistractionPhase.Entering, 0.5f, $"{def.Code} 진입");
            await ShotAt(cctv, ev, DistractionPhase.Distracted, 1.2f, $"{def.Code} 딴짓");
            await ShotAt(cctv, ev, DistractionPhase.Noticed, 0.3f, $"{def.Code} 적발");
            await ShotAt(cctv, ev, DistractionPhase.Recovering, 0.5f, $"{def.Code} 정리");
            for (int i = 0; i < 60 * 40 && ev.Running; i++) await Frame();
            await Seconds(1.5);
            Shot(cctv, $"{def.Code} 복귀");
            SaveSheet(def.Code, 5);
        }
        ds.AutoSpawn = true;
        ds.AllowStartWhileWatched = false;
    }

    private async System.Threading.Tasks.Task ShotAt(SubViewport vp, DistractionEvent ev,
                                                     DistractionPhase phase, float after, string label)
    {
        if (ev.Phase != phase)
            for (int i = 0; i < 60 * 40 && ev.Running && ev.Phase != phase; i++) await Frame();
        if (!ev.Running && ev.Phase != phase)
        {
            Log($"  (건너뜀 - {label}, phase={ev.Phase} 중단={ev.AbortReason} 목격={ev.HasBeenObserved} "
                + $"남은근무={DayObjectives.RemainingSeconds:0}s)");
            return;
        }
        await Seconds(after);
        Shot(vp, label);
    }

    private void Shot(SubViewport vp, string label)
    {
        var img = vp.GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) { Log($"  (사진 실패 - {label})"); return; }
        img.Convert(Image.Format.Rgba8);
        _shots.Add((label, img));
    }

    // 한 종류의 사진을 한 장으로 모아 저장한다(가로 한 줄).
    private void SaveSheet(string code, int cols)
    {
        if (_shots.Count == 0) return;
        const int cw = 480, ch = 360;
        int rows = (_shots.Count + cols - 1) / cols;
        var sheet = Image.CreateEmpty(cw * Mathf.Min(cols, _shots.Count), ch * rows, false, Image.Format.Rgba8);
        for (int i = 0; i < _shots.Count; i++)
        {
            var img = (Image)_shots[i].Img.Duplicate();
            img.Resize(cw, ch, Image.Interpolation.Bilinear);
            sheet.BlitRect(img, new Rect2I(0, 0, cw, ch), new Vector2I(i % cols * cw, i / cols * ch));
        }
        sheet.SavePng($"{_dir}/dis_{code}.png");
        Log("  사진: " + string.Join(" | ", _shots.ConvertAll(x => x.Label)));
        _shots.Clear();
    }

    // ── 진행 도우미 ─────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task StartShift(FacilitySimulation sim)
    {
        foreach (var (room, ids) in Layout)
            foreach (string id in ids)
            {
                if (sim.AssignToRoom(id, room)) continue;
                // 며칠을 아무도 돌보지 않고 돌리면 쓰러지거나 격리된 직원이 생긴다 — 검사의 잘못이 아니다.
                var st = sim.GetEmployeeState(id);
                string why = st == null ? "상태 없음"
                    : !st.Alive ? "사망" : st.Isolated ? "격리" : st.Incapacitated ? "기절" : "";
                if (why.Length == 0) Fail($"배치 실패 {id}→{room}");
                else Log($"  ({id} 배치 불가 - {why}. 이 날은 그 직원 없이 본다)");
            }
        await Frames(5);
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        // 검사는 차폐문을 닫지 않는다 — 복도로 걸어온 개체가 넘어오면 관리자 사망 연출이 근무 시계를
        // 통째로 세운다(AdminDeathDirector). DAY1 개체는 근무 초반(8~14초)에 나오므로 거의 매번 걸린다.
        // 개체 · 복도 위협 자체는 그대로 두고 넘어오지만 못하게 한다(교육과 같은 장치).
        if (sim.Threats != null) sim.Threats.NoBreach = true;
        // 둘째 날부터는 근무 시작에 메인 스토리가 돌며 근무 시간을 멈춰 둔다 — 검사에는 넘길 사람이
        // 없으니 끊는다. 안 끊으면 그날 일으킨 딴짓이 진입 단계에 멈춘 채 남는다.
        // 해금 안내가 패드를 꺼내 두면 근무 시간이 멈춘다(AdminPad3D.PausesGame) — 그것도 내려놓는다.
        for (int i = 0; i < 120; i++)
        {
            if (StoryCutinDirector.Instance is { IsPlaying: true } story) story.Abort();
            if (AdminPad3D.Instance is { IsOpen: true } pad) pad.Close();
            await Frame();
        }
        // 모두 자리에 도착할 때까지.
        for (int i = 0; i < 60 * 40; i++)
        {
            if (Layout.SelectMany(l => l.Ids).All(id =>
                sim.GetEmployeeState(id) is { IsMoving: false })) break;
            await Frame();
        }
        Log($"DAY{GameState.Instance.CurrentDay} 명부 - " + string.Join(" ", Layout.SelectMany(l => l.Ids)
            .Select(id => sim.GetEmployeeState(id) is { } s2
                ? $"{id}({(s2.Alive ? "" : "사망")}{(s2.Isolated ? "격리" : "")}{(s2.Incapacitated ? "기절" : "")}"
                  + $"스트레스{s2.Stress:0})" : $"{id}(?)")));
        Log($"DAY{GameState.Instance.CurrentDay} 근무 시작 - " + string.Join(" ",
            Layout.SelectMany(l => l.Ids).Select(id => $"{id}@{sim.GetEmployeeState(id).CurrentRoomId}")));
    }

    private async System.Threading.Tasks.Task NextDay(FacilitySimulation sim)
    {
        for (int i = 0; i < 60 * 30 && GameState.Instance.CurrentPhase == GamePhase.Live; i++) await Frame();
        await ToSchedule(sim);
    }

    private async System.Threading.Tasks.Task ToSchedule(FacilitySimulation sim)
    {
        var flow = DebugEntryPoint.FindFlow(GetTree());
        GameState.Instance?.GoToNextDay();
        DebugEntryPoint.Call(flow, "ClearAllAssignments");
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(10);
    }

    private static void Track(FacilityCctvWorld world, string id, List<string> seq, ref float jump,
                             ref Vector3? last)
    {
        string c = Clip(world, id);
        if (seq.Count == 0 || seq[^1] != c) seq.Add(c);
        var node = world?.EmployeeNode(id);
        if (node is not { Visible: true }) { last = null; return; }
        var hips = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips");
        var p = hips?.GlobalPosition ?? node.GlobalPosition;
        if (last.HasValue) jump = Mathf.Max(jump, p.DistanceTo(last.Value));
        last = p;
    }

    private static string Clip(FacilityCctvWorld world, string id) =>
        world?.EmployeeAnimator(id)?.CurrentClip ?? "-";

    // ── 보고 ────────────────────────────────────────────────────────────

    private void Check(bool ok, string what)
    {
        if (ok) Log("  ok  " + what);
        else Fail(what);
    }

    private void Log(string s)
    {
        GD.Print("[DIS] " + s);
        _report.AppendLine(s);
    }

    private void Fail(string s)
    {
        _fails++;
        Log("  !!  " + s);
    }

    private void Finish()
    {
        Log(_fails == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_fails})");
        try { System.IO.File.WriteAllText($"{_dir}/distraction_report.txt", _report.ToString()); }
        catch (System.Exception e) { GD.Print("[DIS] 보고서 저장 실패 - " + e.Message); }
        if (!_fromHub) GetTree().Quit();
    }

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
