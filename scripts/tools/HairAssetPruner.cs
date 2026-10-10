using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 고르지 않은 헤어를 지운다. 8명에게 실제로 올라간 것만 남긴다.
//
//   godot --headless --path . res://scenes/tools/HairAssetPruner.tscn --quit-after 300
//
// 지우는 것
//   · assets/characters/hair/{male,female,beard}/*.res 중 쓰이지 않는 것
//   · 그에 딸린 썸네일 png 와 .import
//   · 카탈로그에서 그 항목들
//
// 남는 것은 hair_assignments.json 이 가리키는 메시뿐이다. 원본 팩은 건드리지 않으므로
// 나중에 다른 머리로 바꾸고 싶으면 HairAssetBuilder 를 다시 돌리면 된다.
//
// **선택이 하나도 없으면 아무것도 지우지 않는다** — 빈 파일로 전부 날리는 사고를 막는다.
public partial class HairAssetPruner : Node
{
    public override void _Ready()
    {
        HairCatalog cat = HairCatalog.Load();
        var store = HairAssignmentStore.Load();

        var keep = new HashSet<string>();
        foreach ((string id, _, _) in HairAssignmentStore.Characters)
        {
            if (!store.HasChoice(id)) continue;
            keep.Add(store.Get(id).HairId);
        }

        GD.Print($"################ 헤어 정리 — 카탈로그 {cat.Entries.Count}개 중 {keep.Count}개를 남긴다");
        foreach ((string id, _, _) in HairAssignmentStore.Characters)
            GD.Print($"  {HairAssignmentStore.Label(id),-12} {(store.HasChoice(id) ? store.Get(id).HairId : "(미선택)")}");

        if (keep.Count == 0)
        {
            GD.Print("  !! 고른 헤어가 하나도 없다. 아무것도 지우지 않는다");
            GetTree().Quit();
            return;
        }
        int unchosen = HairAssignmentStore.Characters.Count(c => !store.HasChoice(c.Id));
        if (unchosen > 0)
            GD.Print($"  !! 아직 고르지 않은 캐릭터가 {unchosen}명 있다 — 그래도 진행한다");

        long freed = 0;
        int meshes = 0, thumbs = 0;
        var dropped = new List<HairEntry>();

        foreach (HairEntry e in cat.Entries.ToList())
        {
            if (keep.Contains(e.Id)) continue;
            dropped.Add(e);
            cat.Entries.Remove(e);
            freed += Kill(e.MeshPath, ref meshes);
            freed += Kill($"{HairThumbnailBaker.OutDir}/{e.Id}.png", ref thumbs);
            Kill($"{HairThumbnailBaker.OutDir}/{e.Id}.png.import", ref thumbs);
            Kill($"{e.MeshPath}.import", ref meshes);
        }

        cat.Notes.Add($"고르지 않은 헤어 {dropped.Count}개를 지웠다. 남은 것은 " +
                      string.Join(", ", cat.Entries.Select(e => e.Id)) + " 뿐이고, " +
                      "다른 머리로 바꾸려면 원본 팩으로 HairAssetBuilder 를 다시 돌려야 한다");
        Error err = cat.Save();

        GD.Print($"\n  메시 {meshes}개 · 썸네일 {thumbs}개 삭제 — {freed / 1024 / 1024}MB 회수");
        GD.Print($"  카탈로그 저장 {err} — 남은 항목 {cat.Entries.Count}개: " +
                 string.Join(", ", cat.Entries.Select(e => e.Id)));
        GetTree().Quit();
    }

    private static long Kill(string resPath, ref int count)
    {
        if (!FileAccess.FileExists(resPath)) return 0;
        long size;
        using (var f = FileAccess.Open(resPath, FileAccess.ModeFlags.Read))
            size = f?.GetLength() is { } l ? (long)l : 0L;
        Error e = DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(resPath));
        if (e != Error.Ok) { GD.Print($"  !! {resPath} 삭제 실패: {e}"); return 0; }
        count++;
        return size;
    }
}
