using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.Debug;

// 비상 차폐 유지 시간이 **근무 유형에 따라** 바뀌는지 본다(대회용 72 / 기본 120).
//
//   godot --headless --path . res://scenes/debug/SealHoursTest.tscn
public partial class SealHoursTest : Node
{
    public override void _Ready()
    {
        int pass = 0, fail = 0;
        GD.Print("################ 비상 차폐 유지 시간 ################");

        // 대사 원본에 숫자가 박혀 있지 않아야 한다.
        string raw = Raw();
        Check(ref pass, ref fail, raw.Contains("{HOURS}"), $"컷씬 원본이 토큰을 쓴다 (winbig: {raw})");
        Check(ref pass, ref fail, !raw.Contains("120"), "컷씬 원본에 120 이 박혀 있지 않다");

        foreach (var (mode, want) in new[]
                 { (GameMode.Competition, "72:00:00"), (GameMode.Standard, "120:00:00") })
        {
            GameModes.ForceSelect(mode);
            string got = raw.Replace("{HOURS}", (GameModes.MaxDays * 24).ToString());
            Check(ref pass, ref fail, got == want,
                $"{GameModes.DisplayName(mode)}({GameModes.MaxDays}일) → {got}");
        }

        GD.Print($"################ 결과: {pass} PASS / {fail} FAIL ################");
        GetTree().Quit(fail > 0 ? 1 : 0);
    }

    // 컷씬 원본에서 winbig 한 줄을 그대로 읽는다.
    private static string Raw()
    {
        using var f = FileAccess.Open("res://docs/NSP_PROLOGUE_RUNTIME.md", FileAccess.ModeFlags.Read);
        if (f == null) return "";
        while (!f.EofReached())
        {
            string line = f.GetLine().Trim();
            if (line.StartsWith("winbig:")) return line[7..].Trim();
        }
        return "";
    }

    private static void Check(ref int pass, ref int fail, bool ok, string label)
    {
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {label}");
        if (ok) pass++; else fail++;
    }
}
