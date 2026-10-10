using Godot;
using Godot.Collections;

namespace NSP.Data;

// 두 직원 사이 감정의 종류. 배치 규칙보다는 대사·연출의 색을 정한다.
public enum RelationType
{
    Neutral,
    Friend,       // 우호
    Trust,        // 담백한 신뢰·상호 존중
    Care,         // 일방적으로 챙김 (고양이→토끼, 강아지→양, 늑대→토끼)
    Crush,        // 일방적 호감·짝사랑 (토끼→여우)
    MutualCrush,  // 서로 호감이지만 티 안 냄 (양↔늑대)
    Lover,        // 연인 (고양이↔강아지)
    Wary,         // 경계·거리감
    Friction,     // 마찰·티격태격
    Hatred,       // 극혐
}

// 관계 테이블 전체를 담는 데이터 리소스.
// 위치: res://data/relationships/relationships.tres
// 파일 이름 = 클래스 이름이어야 Godot 가 .tres 의 스크립트를 찾는다(RelationshipDef.cs → RelationshipTableDef.cs).
//
// 한 줄이 "방향 하나"의 감정을 뜻한다. 대칭 관계도 두 줄로 각각 적는다.
// 방향을 나누는 이유: 짝사랑, 편향된 증언, "무서워하면서 좋아함" 같은
// 비대칭을 표현하기 위해서다.
[GlobalClass]
public partial class RelationshipTableDef : Resource
{
    // "from,to,affinity,type[,flags]"
    //   from,to  : EmployeeId  (cat / dog / fox / rabbit / sheep / wolf)
    //   affinity : -100 ~ 100  — from 이 to 에게 느끼는 초기 감정
    //   type     : RelationType 이름
    //   flags    : fear | admire | looksdown  (여러 개는 | 로, 생략 가능)
    [Export] public Array<string> Seed = new();

    // 두 방향 감정의 평균(PairScore)으로 판정하는 동실 밴드 임계값.
    // 데이터에서 바로 튜닝할 수 있게 export 로 노출한다.
    [Export] public int RefuseAtOrBelow = -70;   // 이하 → 동실 거부(하드 차단)
    [Export] public int UneasyAtOrBelow = -30;   // 이하 → 불편(소프트 페널티)
    [Export] public int FriendlyAtOrAbove = 30;  // 이상 → 우호
    [Export] public int CloseAtOrAbove = 70;     // 이상 → 밀접
}
