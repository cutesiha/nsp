using System.Collections.Generic;

namespace NSP.View;

// 스토리 컷인 한 묶음의 데이터. 풀스크린 비주얼 노벨 장면이 아니라
// "3D 관제 화면 위에 스탠딩 한두 명 + 이름 + 대사" 를 올리는 연출 단위다.
//
// 여기에는 판정이 하나도 없다 — 게임 규칙은 전부 기존 FacilitySimulation 쪽에 있고,
// 이 데이터는 무엇을 화면에 보여 줄지만 담는다(문서 §38).
//
// 대본 파일(.tres / 텍스트 스크립트)로 읽어 들이는 일은 이 Phase 범위가 아니다.
// 지금은 호출부가 코드로 채운다. 나중에 로더가 붙어도 이 모양은 그대로 쓸 수 있다.

public enum CutinSide { Left, Right }

// 대사 한 줄.
public sealed class StoryLine
{
    // 말하는 직원. 비우면 스탠딩 없이 자막만 띄운다(시설 안내 · 나레이션).
    public string SpeakerEmployeeId = "";
    // "smile" / "bad". 비우면 대사 분위기로 자동 판정(EmployeeExpression).
    // 리소스가 없는 이름을 주면 그 직원의 평소 표정으로 떨어진다.
    public string Expression = "";
    // 스탠딩이 설 자리. 같은 쪽을 쓰는 직원이 바뀌면 그 자리의 인물이 교체된다.
    public CutinSide Side = CutinSide.Left;
    public string Text = "";
    // 0 보다 크면 그 시간이 지나면 입력 없이도 다음 줄로 넘어간다(짧은 반응 컷).
    // 0 이면 플레이어가 넘길 때까지 기다린다(기본).
    public double HoldSeconds = 0;

    // 이 줄을 띄우기 전에 퇴장시킬 자리. 그 쪽 스탠딩이 페이드로 사라지고,
    // 남은 한 명은 화면 가운데로 부드럽게 돌아온다.
    public CutinSide? ExitSide = null;
}

public sealed class StoryBeat
{
    public string BeatId = "";
    // 새 규칙을 처음 가르치는 순간에는 켠다 — 대사를 읽는 동안 근무 시간이 흘러
    // 불이익을 받지 않게 한다(문서 §27).
    public bool PauseGameplay = true;
    // 컷인이 말하는 동안 CCTV 엿들은 대화 자막을 멈춘다(문서 §28).
    public bool SuppressAmbientDialogue = true;
    public readonly List<StoryLine> Lines = new();

    public StoryBeat Add(string speaker, string text, CutinSide side = CutinSide.Left,
        string expression = "", double holdSeconds = 0, CutinSide? exitSide = null)
    {
        Lines.Add(new StoryLine
        {
            SpeakerEmployeeId = speaker ?? "",
            Text = text ?? "",
            Side = side,
            Expression = expression ?? "",
            HoldSeconds = holdSeconds,
            ExitSide = exitSide,
        });
        return this;
    }
}
