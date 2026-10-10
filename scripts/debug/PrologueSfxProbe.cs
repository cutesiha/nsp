using Godot;

namespace NSP.Debug;

// 프롤로그 재난 연출에 쓰는 음원의 **실제 길이**를 잰다.
//
//   godot --headless --path . res://scenes/debug/PrologueSfxProbe.tscn --quit-after 20
//
// 길이를 모르고 겹겹이 쌓으면(유리 폭발 · 재난음) 레이어가 서로 어긋나거나
// 한 겹만 먼저 끝나 버린다. 연출 수치를 정하기 전에 여기서 먼저 재 둔다.
public partial class PrologueSfxProbe : Node
{
    private static readonly string[] Keys =
    {
        "glass_bomb", "disaster", "storm", "female_run_shouting", "male_run_shouting",
        "siren", "boom", "metal_clang", "rubble_collapse", "glass_shatter",
    };

    public override void _Ready()
    {
        GD.Print("\n\n################ 프롤로그 음원 길이 ################");
        foreach (string key in Keys)
        {
            string found = "";
            foreach (string ext in new[] { ".wav", ".ogg", ".mp3" })
            {
                string path = $"res://assets/audio/sfx/{key}{ext}";
                if (ResourceLoader.Exists(path)) { found = path; break; }
            }
            if (found.Length == 0) { GD.Print($"  {key,-22} 없음"); continue; }
            var s = GD.Load<AudioStream>(found);
            GD.Print($"  {key,-22} {s?.GetLength() ?? 0,6:0.00}s  {s?.GetType().Name}  {found.GetExtension()}");
        }
        GetTree().Quit();
    }
}
