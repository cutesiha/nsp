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

        // 교육이 차례로 부르는 GUIDE-0 블록 — 하나라도 없으면 그 단계에서 멈춘다.
        foreach (string id in new[]
                 {
                     "tut_intro", "tut_assign", "tut_incident", "tut_anomaly_intro", "tut_anomaly_find",
                     "tut_anomaly_watch", "tut_anomaly_done", "tut_call", "tut_rest", "tut_ask_where",
                     "tut_contradiction", "tut_dialogue_log", "tut_end_call", "tut_complete",
                     "tut_approval", "tut_approval_maze",
                     "tut_faint", "tut_faint_carry", "tut_faint_done",
                 })
        {
            var g = PrologueScript.GetGuide(id);
            Check(g != null && g.Beats.Count > 0, $"@guide {id} 가 있다");
        }

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

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
