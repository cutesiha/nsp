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
                     "tut_approval", "tut_approval_maze", "ops_cross_signal",
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

        // ── 작은 교란(Cross-Room Tamper)을 이해시키는 두 줄 ─────────────────
        //
        // 플레이어가 외워야 하는 규칙은 없다. 쉬운 문장 둘이면 된다 —
        //   ① 기계들은 서로 연결되어 있다(프롤로그 시설 안내 끝).
        //   ② 문제가 보인 곳과 원인이 시작된 곳이 다를 수도 있다(첫 교란 직후).
        // 둘 다 **한 판에 한 번만** 떠야 한다. 반복하면 설명이 아니라 잔소리가 된다.
        string tourEnd = Lines("tut_facility_end");
        Check(tourEnd.Contains("서로 연결되어 있습니다"), $"K 프롤로그에 기계 연결 안내가 있다 — {tourEnd}");
        Check(CountGuidesContaining("서로 연결되어 있습니다") == 1,
            "K-2 그 안내는 대본 전체에서 한 블록에만 있다(두 번 뜨지 않는다)");

        string cross = Lines("ops_cross_signal");
        Check(cross.Contains("원인이 시작된 곳"), $"J 첫 교란 안내가 원인과 증상을 갈라 말한다 — {cross}");
        // GUIDE-0 는 범인을 모른다 — 단정하는 낱말이 들어가면 그 자리에서 추리가 끝난다.
        Check(!cross.Contains("누군가") && !cross.Contains("조작") && !cross.Contains("방해"),
            $"J-2 첫 교란 안내가 범인을 단정하지 않는다 — {cross}");
        Check(!cross.Contains("동기화") && !cross.Contains("진단망") && !cross.Contains("노드"),
            $"J-3 첫 교란 안내에 어려운 용어가 없다 — {cross}");

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
        string.Join(" ", PrologueScript.GetGuide(id)?.Beats.Select(b => b.Value)
                         ?? System.Array.Empty<string>());

    // 대본 전체에서 그 문구가 든 @guide 블록이 몇 개인가(같은 안내가 여러 번 뜨지 않게).
    private static int CountGuidesContaining(string needle) =>
        PrologueScript.GuideIds.Count(id => Lines(id).Contains(needle));

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
