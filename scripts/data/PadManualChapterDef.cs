using Godot;

namespace NSP.Data;

// 관리자 패드 지침의 한 챕터(PadManualDef 참고).
[GlobalClass]
public partial class PadManualChapterDef : Resource
{
    [Export] public string Title = "";
    // 챕터 카드의 썸네일(res:// 경로). 비어 있으면 첫 페이지 그림을 쓴다.
    [Export] public string IconPath = "";
    // PadManualPageDef 목록.
    [Export] public Godot.Collections.Array<Resource> Pages = new();

    public System.Collections.Generic.List<PadManualPageDef> PageList()
    {
        var list = new System.Collections.Generic.List<PadManualPageDef>();
        foreach (var r in Pages)
            if (r is PadManualPageDef p) list.Add(p);
        return list;
    }

    public string ThumbnailPath()
    {
        if (!string.IsNullOrEmpty(IconPath)) return IconPath;
        foreach (var p in PageList())
            if (!string.IsNullOrEmpty(p.ImagePath)) return p.ImagePath;
        return "";
    }
}
