using System.Linq;
using Godot;
using NSP.Prologue;

namespace NSP.Debug;

// 프롤로그 · 교육 데이터(docs/NSP_PROLOGUE_RUNTIME.md)가 코드가 기대하는 대로 읽히는지 본다.
//
//   godot --headless --path . res://scenes/debug/PrologueScriptTest.tscn
//
// 문서를 손으로 고치다 블록 이름이 어긋나면 교육이 그 자리에서 멈춘다 — 그걸 실행 전에 잡는다.
public partial class PrologueScriptTest : Node
{
    private int _pass, _fail;

    public override void _Ready()
    {
        GD.Print("################ 프롤로그 스크립트 검사 ################");

        // 교육이 차례로 부르는 GUIDE-0 블록. 없어도 교육은 멈추지 않지만(그 묶음만 건너뛴다)
        // 안내가 통째로 빠지므로 대본에 남아 있는지 본다.
        foreach (string id in new[]
                 {
                     "tut_intro", "tut_assign", "tut_assign_wrong_room", "tut_assign_wrong_person",
                     "tut_assign_rest", "tut_shift_start", "tut_call", "tut_materials", "tut_materials_done",
                     "tut_storage", "tut_storage_done", "tut_power_short", "tut_relocate", "tut_repair_done",
                     "tut_sabotage_intro", "tut_approval", "tut_approval_retry", "tut_approval_maze",
                     "tut_anomaly_intro", "tut_barrier_found", "tut_barrier_corridor", "tut_barrier_close",
                     "tut_barrier_hold", "tut_barrier_retreat", "tut_barrier_done",
                     "tut_endshift", "tut_rest", "tut_ask_where", "tut_contradiction", "tut_dialogue_log",
                     "tut_end_call", "tut_complete",
                 })
        {
            var g = PrologueScript.GetGuide(id);
            Check(g != null && g.Beats.Count > 0, $"@guide {id} 가 있다");
        }

        // ── 대사 줄 연출(cue) ─────────────────────────────────────────────
        // cue 는 **바로 다음 줄**에 걸린다 — 그 줄 앞에 있어야 한다.
        CheckCueBefore("tut_power_short", "zoom_switch | 사고가 나면", "사고가 나면");
        CheckCueBefore("tut_power_short", "flick_lighting", "조명");
        CheckCueBefore("tut_power_short", "flick_cctv", "CCTV:");
        CheckCueBefore("tut_power_short", "demo_barrier", "Barrier");
        CheckCueBefore("tut_sabotage_intro", "demo_sabotage", "방금처럼요");
        CheckCueBefore("tut_anomaly_intro", "ghost_noise", "이상한 소리");
        CheckCueBefore("tut_barrier_close", "zoom_monitor2", "접근 중");
        CheckCueBefore("tut_barrier_close", "zoom_switch_hold", "BARRIER");
        CheckCueBefore("tut_ask_where", "zoom_monitor1", "숫자 키");
        CheckCueBefore("tut_end_call", "zoom_clear", "통화를 종료");
        CheckCueBefore("tut_approval", "zoom_pad", "‘예’");
        foreach (string id in new[] { "tut_assign", "tut_materials", "tut_storage", "tut_relocate", "tut_endshift", "tut_rest" })
            Check(Cues(id).Contains("arrow"), $"{id} 에 안내 화살표(cue: arrow)가 걸려 있다");
        // cue 는 대사로 읽히면 안 된다.
        Check(!Lines("tut_power_short").Contains("zoom_switch"), "cue 이름이 대사 줄에 섞이지 않는다");

        // ── 줄바꿈 /n ─────────────────────────────────────────────────────
        Check(NSP.View.GuideTextMarkup.PlainText("가나 /n다라") == "가나 \n다라", "/n 은 줄바꿈이 된다");
        Check(NSP.View.GuideTextMarkup.PlainText("/1빨강/n계속/0") == "빨강\n계속", "/n 은 색 명령과 섞여도 줄바꿈이다");
        Check(NSP.View.GuideTextMarkup.RichText("가 /n나").Contains("\n"), "화면 표시에도 줄바꿈이 들어간다");

        // ── 가상 시뮬레이션이 끝난 뒤에는 GUIDE-0 가 근무에 끼어들지 않는다 ──
        foreach (string id in new[] { "ops_cross_signal", "ops_vent_down", "ops_first_sabotage" })
            Check(PrologueScript.GetGuide(id) == null, $"근무 중 안내 {id} 는 대본에서 빠졌다");

        // D 키 단계 — "확인할 수 있습니다" 와 "통화를 종료" 가 서로 다른 블록이어야 D 를 누른 뒤에만 둘째 줄이 뜬다.
        var dlog = PrologueScript.GetGuide("tut_dialogue_log");
        var end = PrologueScript.GetGuide("tut_end_call");
        string dlogText = string.Join(" ", dlog?.Beats.Select(b => b.Value) ?? System.Array.Empty<string>());
        string endText = string.Join(" ", end?.Beats.Select(b => b.Value) ?? System.Array.Empty<string>());
        Check(dlogText.Contains("D키") && !dlogText.Contains("종료"), $"tut_dialogue_log 는 D 키 안내만 한다 — {dlogText}");
        Check(endText.Contains("종료"), $"tut_end_call 이 통화 종료를 안내한다 — {endText}");


        // 대재난 컷씬 — 사이렌이 시작되는 첫 컷에 점프스케어(scare:)가 붙어 있다.
        var cut = PrologueScript.GetCutscene("prologue_disaster");
        Check(cut != null && cut.Slides.Count > 0, "@cutscene prologue_disaster 가 있다");
        var siren = cut?.Slides.FirstOrDefault(s => s.SfxLoopStart == "siren");
        Check(siren != null && siren.Scare > 0.99f, $"사이렌 컷에 scare 가 붙어 있다 ({siren?.Scare})");
        Check(cut != null && cut.Slides.Count(s => s.Scare > 0f) == 1, "점프스케어는 한 컷에서만 터진다");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // 그 @guide 블록의 모든 줄을 하나로 이어 붙인다.
    private static string Lines(string id) =>
        string.Join(" ", PrologueScript.GetGuide(id)?.Beats
                             .Where(b => b.Kind == PrologueScript.GuideBeatKind.Line).Select(b => b.Value)
                         ?? System.Array.Empty<string>());

    // 그 블록에 걸린 cue 이름들(| 뒤의 글자 조건은 뺀다).
    private static System.Collections.Generic.List<string> Cues(string id) =>
        PrologueScript.GetGuide(id)?.Beats
            .Where(b => b.Kind == PrologueScript.GuideBeatKind.Cue)
            .Select(b => b.Value.Split('|')[0].Trim()).ToList()
        ?? new System.Collections.Generic.List<string>();

    // cue 가 그 블록에 있고, 바로 뒤의 첫 대사 줄에 lineContains 가 들어 있는가.
    private void CheckCueBefore(string id, string cueValue, string lineContains)
    {
        var beats = PrologueScript.GetGuide(id)?.Beats;
        bool ok = false;
        if (beats != null)
        {
            for (int i = 0; i < beats.Count; i++)
            {
                if (beats[i].Kind != PrologueScript.GuideBeatKind.Cue) continue;
                if (beats[i].Value.Replace(" ", "") != cueValue.Replace(" ", "")) continue;
                var next = beats.Skip(i + 1).FirstOrDefault(b => b.Kind == PrologueScript.GuideBeatKind.Line);
                ok = next != null && next.Value.Contains(lineContains);
                break;
            }
        }
        Check(ok, $"{id} — cue: {cueValue} 가 \"{lineContains}\" 줄 앞에 있다");
    }

    // 대본 전체에서 그 문구가 든 @guide 블록이 몇 개인가(같은 안내가 여러 번 뜨지 않게).
    private static int CountGuidesContaining(string needle) =>
        PrologueScript.GuideIds.Count(id => Lines(id).Contains(needle));

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
