using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 원본 팩 → 헤어 하나하나의 메시 파일 + 카탈로그. **한 번만** 돌리면 되는 변환기다.
//
//   godot --headless --path . res://scenes/tools/HairAssetBuilder.tscn --quit-after 100000
//
// 결과물
//   res://assets/characters/hair/male/M-01.res …      남성 헤어
//   res://assets/characters/hair/female/F-001.res …   여성 헤어
//   res://assets/characters/hair/beard/MB-01.res      팩에 섞여 있던 수염(목록에서는 숨긴다)
//   res://assets/characters/hair/hair_catalog.json    스튜디오가 읽는 명세
//
// 원본 파일은 읽기만 한다.
public partial class HairAssetBuilder : Node
{
    public override void _Ready()
    {
        var sw = Stopwatch.StartNew();
        var notes = new List<string>();
        var cat = new HairCatalog();

        GD.Print("################ 헤어 에셋 분리 시작");

        HairBaseEntry m = HairSourceSplitter.MeasureBase("male", HairSourceSplitter.MaleBaseGltf, notes, Log);
        HairBaseEntry f = HairSourceSplitter.MeasureBase("female", HairSourceSplitter.FemaleBaseGltf, notes, Log);
        if (m != null) cat.Bases.Add(m);
        if (f != null) cat.Bases.Add(f);

        GD.Print("\n-- 남성 헤어팩 (hair.fbx)");
        cat.Entries.AddRange(HairSourceSplitter.SplitMalePack(notes, Log));

        GD.Print("\n-- 여성 헤어팩 (nv+toufa.obj)");
        cat.Entries.AddRange(HairSourceSplitter.SplitFemalePack(notes, Log));

        notes.Add("Blender 가 이 환경에 설치돼 있지 않아 GLB 변환은 하지 않았다. " +
                  "분리본은 Godot 네이티브 메시 리소스(.res, 압축)로 저장했다");

        foreach (string n in notes) cat.Notes.Add(n);
        Error err = cat.Save();

        GD.Print($"\n################ 카탈로그 저장 {HairCatalog.Path} → {err}");
        GD.Print($"  남성 헤어 {cat.Hairs("male").Count()}개 · 여성 헤어 {cat.Hairs("female").Count()}개 · " +
                 $"수염 {cat.Entries.Count(e => e.Kind == "beard")}개");
        GD.Print($"  팩이 바라보는 방향 — 남성 {HairCatalog.PackYaw(HairSourceSplitter.MaleFbx)}도 · " +
                 $"여성 {HairCatalog.PackYaw(HairSourceSplitter.FemaleObj)}도");
        foreach (string n in notes) GD.Print("  · " + n);
        GD.Print($"  걸린 시간 {sw.Elapsed.TotalSeconds:F1}초");

        GetTree().Quit();
    }

    private static void Log(string s) => GD.Print("  " + s);
}
