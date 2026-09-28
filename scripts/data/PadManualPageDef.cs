using Godot;

namespace NSP.Data;

// 관리자 패드 지침의 한 페이지 — 위쪽 그림 + 제목 한 줄 + 본문 최대 4줄(PadManualDef 참고).
[GlobalClass]
public partial class PadManualPageDef : Resource
{
    // 페이지 위쪽 그림(res:// 경로). 게임 화면 캡처(res://assets/pad/manual/)나 프롤로그 컷 그림.
    [Export] public string ImagePath = "";
    [Export] public string Title = "";
    [Export] public string[] Lines = System.Array.Empty<string>();
}
