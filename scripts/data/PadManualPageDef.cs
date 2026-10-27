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

    // 스트레스가 있는 근무에서만 싣는 페이지(스트레스 · 기절 · 환기실 · 의무실).
    // 단기 근무에는 스트레스도 그 두 방도 없다(GameModes.StressSystemEnabled).
    [Export] public bool StressOnly;
    // 반대로 스트레스가 없는 근무에서만 싣는 페이지 — 같은 내용을 스트레스 언급 없이 다시 쓴 것.
    [Export] public bool NoStressOnly;

    // 이번 판의 근무 유형에서 이 페이지를 보여 주는가.
    public bool ShownNow()
    {
        bool stress = NSP.Core.GameModes.StressSystemEnabled;
        return stress ? !NoStressOnly : !StressOnly;
    }
}
