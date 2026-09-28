using Godot;

namespace NSP.Data;

// 관리자 패드 「지침」 탭의 문서. 문구는 전부 data/pad/manual.tres 에 있다.
//
// Lines 한 줄이 한 문단이다. "# " 로 시작하는 줄은 섹션 제목이고, 그 아래 줄들이 본문이다.
// 문장 안 강조는 쓰지 않는다 — 패드 화면이 작아 색이 많으면 오히려 읽히지 않는다.
[GlobalClass]
public partial class PadManualDef : Resource
{
    [Export] public string Title = "관리자 지침";
    [Export] public string[] Lines = System.Array.Empty<string>();

    public const string DefaultPath = "res://data/pad/manual.tres";

    public sealed class Section
    {
        public string Title = "";
        public System.Collections.Generic.List<string> Paragraphs = new();
    }

    public System.Collections.Generic.List<Section> Sections()
    {
        var list = new System.Collections.Generic.List<Section>();
        Section cur = null;
        foreach (string raw in Lines ?? System.Array.Empty<string>())
        {
            string line = (raw ?? "").Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("# "))
            {
                cur = new Section { Title = line[2..].Trim() };
                list.Add(cur);
                continue;
            }
            if (cur == null) { cur = new Section(); list.Add(cur); }
            cur.Paragraphs.Add(line);
        }
        return list;
    }
}
