using Godot;

namespace NSP.Data;

// 관리자 패드 지침의 한 챕터(PadManualDef 참고).
[GlobalClass]
public partial class PadManualChapterDef : Resource
{
    [Export] public string Title = "";
    // 챕터 카드의 썸네일(res:// 경로). 비어 있으면 첫 페이지 그림을 쓴다.
    [Export] public string IconPath = "";
    // 스트레스가 없는 근무(단기 근무)에서 쓰는 썸네일. 비어 있으면 IconPath 그대로.
    [Export] public string IconPathNoStress = "";
    // PadManualPageDef 목록.
    [Export] public Godot.Collections.Array<Resource> Pages = new();

    // 이번 판에 보여 줄 페이지만(PadManualPageDef.ShownNow).
    public System.Collections.Generic.List<PadManualPageDef> PageList()
    {
        var list = new System.Collections.Generic.List<PadManualPageDef>();
        foreach (var r in Pages)
            if (r is PadManualPageDef p && p.ShownNow()) list.Add(p);
        return list;
    }

    public string ThumbnailPath()
    {
        if (!NSP.Core.GameModes.StressSystemEnabled && !string.IsNullOrEmpty(IconPathNoStress)) return IconPathNoStress;
        if (!string.IsNullOrEmpty(IconPath)) return IconPath;
        foreach (var p in PageList())
            if (!string.IsNullOrEmpty(p.ImagePath)) return p.ImagePath;
        return "";
    }
}
