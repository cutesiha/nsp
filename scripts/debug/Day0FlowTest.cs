using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Prologue;

namespace NSP.Debug;

// DAY0(가상 교육 시뮬레이션) 구조 검증 — 작업실 순회 강의를 걷어내고
// 상황 기반 단계로 바꾼 뒤(NSP_STORY_TUTORIAL_REWORK PHASE 4) 깨지기 쉬운 곳만 본다.
//
//   godot --headless --path . res://scenes/debug/Day0FlowTest.tscn
//
// 교육 자체를 처음부터 끝까지 돌리지는 않는다 — 그 단계들은 전부 플레이어의 조작을
// 기다리므로 자동으로 흉내 낼 수 없다. 대신 교육이 **기대는 전제**를 검사한다.
public partial class Day0FlowTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => CallDeferred(nameof(RunAll));

    private void RunAll()
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        if (sim == null || gs == null) { GD.PrintErr("오토로드를 찾지 못했습니다."); GetTree().Quit(1); return; }

        GD.Print("################ DAY0 교육 구조 검증 ################");

        // ── A : DAY0 에 처음부터 열려 있는 작업실 ─────────────────────
        gs.ResetRun(0);
        sim.ResetRun();
        var open = sim.GetRoomIds()
            .Where(r => sim.GetRoomDef(r)?.IsRestricted == false && sim.IsRoomActive(r))
            .OrderBy(r => r).ToList();
        GD.Print($"\n[A] DAY0 배치 가능 작업실 : {string.Join(" · ", open)}");
        Check("A 코어실이 열려 있다(첫 배치 대상)", open.Contains("core_room"));
        Check("A 정비실은 잠겨 있다(상황이 생긴 뒤에 열린다)", !open.Contains("maintenance_room"));
        Check("A 저장고는 잠겨 있다(상황이 생긴 뒤에 열린다)", !open.Contains("storage_room"));

        // 교육이 쓰는 방들이 실제로 쓸 수 있는 상태인지 — 설정이 어긋나면 교육이 멈춰 선다.
        var dir = new TutorialDirector();
        Check("A 첫 배치 작업실이 DAY0 에 열려 있다", open.Contains(dir.AssignRoomId));
        Check("A 사고 작업실이 DAY0 에 열려 있다", open.Contains(dir.AccidentRoomId));
        Check("A 이상 개체 작업실이 DAY0 에 열려 있고 사고 작업실과 다르다",
            open.Contains(dir.AnomalyRoomId) && dir.AnomalyRoomId != dir.AccidentRoomId);
        dir.Free();

        // ── B : 근무 중에 작업실을 연다 ───────────────────────────────
        gs.SetPhase(GamePhase.Live);
        sim.OpenRoomMidShift("maintenance_room");
        var task = sim.GetPrimarySpawnedTask("maintenance_room");
        GD.Print($"\n[B] 정비실 열기 → 활성 {sim.IsRoomActive("maintenance_room")} · 업무 {task?.TaskId ?? "없음"}");
        Check("B 정비실이 열렸다", sim.IsRoomActive("maintenance_room"));
        Check("B 자재 생산 업무가 함께 떴다", task?.TaskId == "materials_production");
        Check("B 열린 뒤에는 배치할 수 있다", sim.AssignToRoom("rabbit", "maintenance_room"));

        // 두 번 불러도 같은 업무가 겹치지 않는다.
        sim.OpenRoomMidShift("maintenance_room");
        int dup = CountActive(sim, "maintenance_room", "materials_production");
        GD.Print($"[B] 다시 열기 → 자재 생산 업무 {dup}개");
        Check("B 같은 상시 업무가 중복으로 뜨지 않는다", dup == 1);

        // ── C : 교육 전용 전화 — 여섯 명 모두 자기 대사가 있다 ────────
        GD.Print("");
        foreach (string ev in new[]
        {
            DialogueRepository.EventTutorialMaterialShort,
            DialogueRepository.EventTutorialStorageFull,
        })
        {
            var missing = new[] { "rabbit", "cat", "dog", "fox", "sheep", "wolf" }
                .Where(id => string.IsNullOrEmpty(DialogueRepository.GetEvent(ev, id)?.Opening)).ToList();
            GD.Print($"[C] {ev} — 대사 없는 직원 {(missing.Count == 0 ? "없음" : string.Join(", ", missing))}");
            Check($"C {ev} 는 직원 여섯 명 모두에게 대사가 있다", missing.Count == 0);
        }
        // 복귀 전화 대사에 이름 · 방이 박혀 있으면 상황이 바뀔 때마다 거짓말이 된다
        // (1013 QA 14번 — 여우가 쓰러졌는데 "고양이 직원", 코어실로 가면서 "경비실").
        // 답변(reply)까지 본다 — 방 이름이 박혀 있던 자리가 거기였다.
        var ev14 = DialogueRepository.GetEvent(DialogueRepository.EventTutorialRepairDone, "rabbit");
        string back = ev14?.Opening ?? "";
        string replies = string.Join(" ", (ev14?.Choices ?? new System.Collections.Generic.List<DialogueRepository.Choice>())
            .Select(c => c.Reply ?? ""));
        string[] rooms = { "경비실", "정비실", "코어실", "발전실", "저장고", "환기실" };
        string[] names = { "고양이", "강아지", "여우", "토끼", "양 ", "늑대" };
        Check("C 복귀 전화 대사에 작업실 이름이 박혀 있지 않다",
            back.Length > 0 && !rooms.Any(r => back.Contains(r) || replies.Contains(r)));
        Check("C 복귀 전화 대사에 직원 이름이 박혀 있지 않다",
            back.Length > 0 && !names.Any(n => back.Contains(n)));
        Check("C 복귀 전화 대사가 치환 토큰을 쓴다",
            back.Contains("{VICTIM}") && replies.Contains("{BACK_ROOM}"));

        // ── D : 안내 대본 ─────────────────────────────────────────────
        GD.Print("");
        Check("D 작업실 순회 안내가 사라졌다", PrologueScript.GetGuide("tut_room_core") == null
                                               && PrologueScript.GetGuide("tut_facility_intro") == null);
        foreach (string id in new[] { "tut_materials", "tut_materials_done", "tut_storage", "tut_storage_done" })
            Check($"D @guide {id} 가 있다", PrologueScript.GetGuide(id) != null);

        // ── E : 교육이 남긴 자재는 실제 근무로 넘어가지 않는다 ────────
        gs.AddMaterialsCap(6 - gs.MaterialsCap);     // 교육이 한도를 줄인 상태
        gs.AddMaterials(-999);                        // 다 써 버린 상태
        GD.Print($"\n[E] 교육 종료 시점 : 자재 {gs.Materials} / {gs.MaterialsCap}");
        gs.GoToNextDay();
        var cfg = Config.Instance.Data;
        GD.Print($"[E] DAY{gs.CurrentDay} 시작 : 자재 {gs.Materials} / {gs.MaterialsCap}");
        Check("E DAY1 자재가 처음 값으로 돌아온다", gs.Materials == cfg.MaterialsStart);
        Check("E DAY1 보관 한도가 처음 값으로 돌아온다", gs.MaterialsCap == cfg.MaterialsCapBase);

        // ── F : 스토리 컷인 대본(@beat) ───────────────────────────────
        GD.Print("");
        var roster = new System.Collections.Generic.HashSet<string>(sim.GetEmployeeIds());
        foreach (string id in new[] { "day0_start", "day0_incident_done", "day0_rest", "day0_end" })
        {
            var beat = NSP.Prologue.PrologueScript.GetBeat(id);
            Check($"F @beat {id} 에 대사가 있다", beat is { Lines.Count: > 0 });
            if (beat == null) continue;
            // 구 캐릭터(owl · crow · jellyfish)가 섞이면 안 된다(문서 §5).
            var strangers = beat.Lines.Select(l => l.SpeakerEmployeeId)
                .Where(x => !string.IsNullOrEmpty(x) && !roster.Contains(x)).Distinct().ToList();
            Check($"F {id} 는 현 직원 6인만 말한다{(strangers.Count == 0 ? "" : " — " + string.Join(",", strangers))}",
                strangers.Count == 0);
            Check($"F {id} 의 모든 줄에 대사 글이 있다", beat.Lines.All(l => l.Text.Length > 0));
            GD.Print($"[F] {id} — {beat.Lines.Count}줄 · " +
                     string.Join(" → ", beat.Lines.Select(l => l.SpeakerEmployeeId)));
        }

        // ── G : 교육 안내 ─────────────────────────────────────────────
        GD.Print("");
        Check("G §17 수리 인원을 작업실로 보내라는 안내가 있다",
            GuideText("tut_relocate").Contains("{ROOM}"));
        Check("G §16 전력 감소 안내가 있다", GuideText("tut_power_short").Contains("전력"));
        // 2026-10-22 — 가상 시뮬레이션이 끝난 뒤에는 GUIDE-0 가 근무에 끼어들지 않는다
        // (첫 방해공작 · 환기 정지 · 작은 교란 안내를 대본에서 뺐다).
        Check("G 근무 중 방해공작 안내는 없다", NSP.Prologue.PrologueScript.GetGuide("ops_first_sabotage") == null);
        Check("G 근무 중 환기 정지 안내는 없다", NSP.Prologue.PrologueScript.GetGuide("ops_vent_down") == null);

        // 전력 총량은 3, 발전실 사고 한 번이면 2 가 된다(가상 시뮬레이션도 같다).
        int cap = Config.Instance?.Data?.PowerCapacityMax ?? 0;
        Check($"G 전력 총량은 3 이다({cap})", cap == 3);
        gs.RepairPowerAccident();
        Check($"G 근무 시작 전력 {gs.PowerCapacity} / {cap} — 처음부터 다 차 있다", gs.PowerCapacity == cap);
        Check("G 시작할 때 켜진 것은 조명 · CCTV 뿐이다",
            gs.IsConsumerPowered(PowerConsumer.Lighting) && gs.IsConsumerPowered(PowerConsumer.CctvWatch)
            && !gs.IsConsumerPowered(PowerConsumer.Barrier));
        float powerLoss = sim.GetRoomDef("power_room")?.AccidentAmount ?? 0f;
        gs.TriggerPowerAccident(Mathf.RoundToInt(powerLoss));
        Check($"G 발전실 사고 한 번이면 전력이 2 가 된다({gs.PowerCapacity})", gs.PowerCapacity == 2);
        gs.RepairPowerAccident();

        // ── H : 블라인드 테스트 기록 ──────────────────────────────────
        GD.Print("");
        NSP.Prologue.TutorialTelemetry.Begin();
        Check("H 기록이 시작된다", NSP.Prologue.TutorialTelemetry.IsRecording);
        NSP.Prologue.TutorialTelemetry.Step("테스트 단계");
        NSP.Prologue.TutorialTelemetry.Count("배치 실수");
        NSP.Prologue.TutorialTelemetry.Count("배치 실수");
        NSP.Prologue.TutorialTelemetry.End(completed: true);
        Check("H 기록이 닫힌다", !NSP.Prologue.TutorialTelemetry.IsRecording);
        Check("H 기록 파일이 남는다", Godot.FileAccess.FileExists(NSP.Prologue.TutorialTelemetry.LogPath));

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // @guide 블록의 대사를 한 줄로 이어 붙인다(검사용).
    private static string GuideText(string id)
    {
        var g = NSP.Prologue.PrologueScript.GetGuide(id);
        if (g == null) return "";
        return string.Join(" ", g.Beats
            .Where(b => b.Kind == NSP.Prologue.PrologueScript.GuideBeatKind.Line)
            .Select(b => b.Value));
    }

    private static int CountActive(FacilitySimulation sim, string roomId, string taskId) =>
        sim.GetActiveTasksForRoom(roomId).Count(t => t.TaskId == taskId
                                                     && t.Status == SpawnedTaskStatus.Active);

    private void Check(string label, bool ok)
    {
        if (ok) _pass++; else _fail++;
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
    }
}
