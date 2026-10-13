using System.Collections.Generic;

namespace NSP.Facility;

// 직원 한 명이 할 수 있는 "딴짓" 한 가지. 사람마다 두 가지씩, 모두 12종이다.
//
// 딴짓은 **방해공작이 아니다.** 정상 직원도 하고, 결번(강아지)도 평범한 딴짓을 한다.
// 이 목록은 발생 규칙과 시간만 가진다 — 어떤 클립을 어떻게 재생할지는 표현 계층
// (NSP.View.DistractionProfiles)이, 전화 대사는 NSP.Dialogue.DistractionDialogue 가 가진다.
public enum DistractionKind
{
    None,
    FoxPhone,       // FOX_01 기계 앞에서 휴대폰 보기
    FoxFloorVideo,  // FOX_02 바닥에 대자로 누워 영상 보기
    SheepDoze,      // SHEEP_01 작업 중 꾸벅꾸벅 졸기
    SheepHide,      // SHEEP_02 무서워서 다른 오브젝트 옆에 웅크리기
    CatCall,        // CAT_01 즐겁게 개인 통화(상대 = 강아지)
    CatWallNap,     // CAT_02 벽에 기대 잠깐 잠들기
    RabbitPeek,     // RABBIT_01 작업 기기를 호기심 있게 조사
    RabbitSelfie,   // RABBIT_02 기분 좋은 날 셀카
    WolfWallNap,    // WOLF_01 벽에 기대 잠깐 잠들기(절제된 쪽)
    WolfKick,       // WOLF_02 작업 기기를 발로 차고 손으로 두드리기
    DogCall,        // DOG_01 고양이와 즐겁게 통화
    DogProbe,       // DOG_02 다른 설비를 살펴보고 두드리기
}

// 딴짓을 하려면 작업실 안에 어떤 자리가 필요한가. 자리가 없는 방에서는 그 딴짓을 고르지 않는다
// (표현 계층이 방마다 미리 재서 DistractionPlaces 에 올려 둔다).
public enum DistractionPlace
{
    AtSpot,      // 지금 일하던 자리 그대로
    FreeFloor,   // 설비에서 떨어진 빈 바닥(눕는다)
    WallLean,    // 등을 댈 수 있는 벽면
    HideSpot,    // 설비 뒤·구석처럼 몸을 숨길 자리
    OtherSpot,   // 지금 자리가 아닌 다른 설비
}

// 딴짓 한 종류의 발생 규칙과 단계별 시간(초).
public sealed class DistractionDef
{
    public DistractionKind Kind;
    public string Code = "";          // 로그·기록용 식별자(FOX_01 …)
    public string EmployeeId = "";
    public DistractionPlace Place = DistractionPlace.AtSpot;

    // 강한 지적이 필요한 딴짓인가 — 전화 선택지 문구가 "지금 뭐하는 겁니까?" 가 된다.
    // 지시서 §11-1: FOX_02 와 WOLF_02 둘뿐이다.
    public bool Strong;

    // 그날 기분이 분명히 좋은 날에만 한다(토끼 셀카).
    public bool NeedsGoodMood;

    // 상대 직원과 **하나의 실제 통화 사건**을 공유한다(고양이 ↔ 강아지).
    public string PairedWith = "";

    // 단계별 시간. Enter 는 (자리에서 하는 준비 + 걸어가는 시간 + 자리에서 자세 잡기)를 넉넉히 잡은 값이다 —
    // 실제 걸음은 방 구조에 따라 달라지므로 표현 계층이 자기 박자로 걷고, 이 시간은 "딴짓이 시작된 때"만 정한다.
    public float EnterSeconds = 2f;
    public float DistractMinSeconds = 7f;    // 적발되지 않아도 최소 이만큼은 한다
    public float DistractMaxSeconds = 12f;   // 아무도 안 보면 이쯤에서 스스로 그만둔다
    public float NoticeSeconds = 1.6f;       // 적발 반응(클립 길이와 같게)
    public float RecoverSeconds = 2f;        // 행동을 정리하는 시간
    public float ReturnSeconds = 0.6f;       // 자리로 돌아가는 시간(걷는 쪽은 표현이 더 걸릴 수 있다)

    public float TotalMaxSeconds =>
        EnterSeconds + DistractMaxSeconds + NoticeSeconds + RecoverSeconds + ReturnSeconds;
}

public static class DistractionCatalog
{
    // 12종. 순서는 지시서 §4~§9 와 같다.
    private static readonly List<DistractionDef> _all = new()
    {
        new DistractionDef
        {
            Kind = DistractionKind.FoxPhone, Code = "FOX_01", EmployeeId = "fox",
            Place = DistractionPlace.AtSpot,
            EnterSeconds = 2.0f, DistractMinSeconds = 7f, DistractMaxSeconds = 12f,
            NoticeSeconds = 1.5f, RecoverSeconds = 1.9f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.FoxFloorVideo, Code = "FOX_02", EmployeeId = "fox",
            Place = DistractionPlace.FreeFloor, Strong = true,
            EnterSeconds = 5.6f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 1.7f, RecoverSeconds = 3.4f, ReturnSeconds = 3.0f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.SheepDoze, Code = "SHEEP_01", EmployeeId = "sheep",
            Place = DistractionPlace.AtSpot,
            EnterSeconds = 2.4f, DistractMinSeconds = 7f, DistractMaxSeconds = 12f,
            NoticeSeconds = 1.4f, RecoverSeconds = 1.3f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.SheepHide, Code = "SHEEP_02", EmployeeId = "sheep",
            Place = DistractionPlace.HideSpot,
            EnterSeconds = 5.8f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 1.7f, RecoverSeconds = 1.5f, ReturnSeconds = 3.0f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.CatCall, Code = "CAT_01", EmployeeId = "cat",
            Place = DistractionPlace.AtSpot, PairedWith = "dog",
            EnterSeconds = 2.0f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 2.1f, RecoverSeconds = 2.1f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.CatWallNap, Code = "CAT_02", EmployeeId = "cat",
            Place = DistractionPlace.WallLean,
            EnterSeconds = 5.0f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 2.2f, RecoverSeconds = 1.7f, ReturnSeconds = 3.0f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.RabbitPeek, Code = "RABBIT_01", EmployeeId = "rabbit",
            Place = DistractionPlace.AtSpot,
            EnterSeconds = 1.8f, DistractMinSeconds = 7f, DistractMaxSeconds = 12f,
            NoticeSeconds = 1.5f, RecoverSeconds = 1.7f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.RabbitSelfie, Code = "RABBIT_02", EmployeeId = "rabbit",
            Place = DistractionPlace.AtSpot, NeedsGoodMood = true,
            EnterSeconds = 1.9f, DistractMinSeconds = 7f, DistractMaxSeconds = 12f,
            NoticeSeconds = 1.6f, RecoverSeconds = 1.9f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.WolfWallNap, Code = "WOLF_01", EmployeeId = "wolf",
            Place = DistractionPlace.WallLean,
            EnterSeconds = 5.2f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 1.8f, RecoverSeconds = 1.9f, ReturnSeconds = 3.0f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.WolfKick, Code = "WOLF_02", EmployeeId = "wolf",
            Place = DistractionPlace.AtSpot, Strong = true,
            EnterSeconds = 1.9f, DistractMinSeconds = 6.5f, DistractMaxSeconds = 11f,
            NoticeSeconds = 1.9f, RecoverSeconds = 1.8f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.DogCall, Code = "DOG_01", EmployeeId = "dog",
            Place = DistractionPlace.AtSpot, PairedWith = "cat",
            EnterSeconds = 1.9f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 1.9f, RecoverSeconds = 1.9f, ReturnSeconds = 0.5f,
        },
        new DistractionDef
        {
            Kind = DistractionKind.DogProbe, Code = "DOG_02", EmployeeId = "dog",
            Place = DistractionPlace.OtherSpot,
            EnterSeconds = 4.8f, DistractMinSeconds = 8f, DistractMaxSeconds = 13f,
            NoticeSeconds = 1.6f, RecoverSeconds = 1.2f, ReturnSeconds = 3.0f,
        },
    };

    public static IReadOnlyList<DistractionDef> All => _all;

    public static DistractionDef Get(DistractionKind kind)
    {
        foreach (var d in _all)
            if (d.Kind == kind) return d;
        return null;
    }

    public static DistractionDef ByCode(string code)
    {
        foreach (var d in _all)
            if (d.Code == code) return d;
        return null;
    }

    // 그 직원의 딴짓 두 가지.
    public static List<DistractionDef> For(string employeeId)
    {
        var list = new List<DistractionDef>();
        foreach (var d in _all)
            if (d.EmployeeId == employeeId) list.Add(d);
        return list;
    }

    // 고양이·강아지의 개인 통화 짝인가.
    public static bool IsPersonalCall(DistractionKind kind) =>
        kind is DistractionKind.CatCall or DistractionKind.DogCall;
}
