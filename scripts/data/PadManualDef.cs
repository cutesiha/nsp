using Godot;

namespace NSP.Data;

// 관리자 패드 「지침」 앱의 문서 — 그림책처럼 챕터 → 페이지로 넘겨 읽는다.
// 문구 · 그림은 전부 data/pad/manual.tres 에서 편집한다(코드에 문장을 두지 않는다).
//
//   Chapter { Title, IconPath, Pages[] }
//   Page    { ImagePath, Title, Lines(최대 4줄, 줄당 40자 이내) }
//
// 한 페이지에 다 들어가지 않는 글은 페이지를 쪼갠다 — 스크롤하지 않는다.
[GlobalClass]
public partial class PadManualDef : Resource
{
    public const string DefaultPath = "res://data/pad/manual.tres";
    public const int MaxLinesPerPage = 4;
    public const int MaxCharsPerLine = 40;

    [Export] public string Title = "관리자 지침";
    // PadManualChapterDef 목록. (에디터 호환을 위해 Resource 배열로 둔다.)
    [Export] public Godot.Collections.Array<Resource> Chapters = new();

    public System.Collections.Generic.List<PadManualChapterDef> ChapterList()
    {
        var list = new System.Collections.Generic.List<PadManualChapterDef>();
        foreach (var r in Chapters)
            if (r is PadManualChapterDef c) list.Add(c);
        return list;
    }
}
