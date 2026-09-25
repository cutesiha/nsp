using Godot;

namespace NSP.View;

// 작업실 안에서 직원이 실제로 일하는 자리. **표현 전용이다.**
//
// 이 노드는 업무 판정을 하지 않는다. 기존 게임이 "이 방에서 지금 이 업무가 진행 중"이라고
// 정한 뒤, RoomWorkVisualController 가 그 TaskId 를 지원하는 이 자리를 찾아
// 직원을 옮기고 애니메이션을 고를 뿐이다.
//
// 이 노드의 Transform 이 곧 직원이 설 위치이자 바라볼 방향이다(캐릭터는 자기 -Z 를 본다).
[Tool]
public partial class RoomWorkSpot : Node3D
{
    // 디버그/로그용 이름. 비워 두면 노드 이름을 쓴다.
    [Export] public string SpotId = "";

    // 이 자리가 담당하는 업무. 비어 있으면 "아무 업무나"(fallback 작업 자리)로 쓴다.
    [Export] public string[] SupportedTaskIds = System.Array.Empty<string>();

    // 이 자리에서 재생할 공용 애니메이션 이름.
    [Export] public string AnimationName = "work";
    // 체형별로 다르게 보이고 싶을 때만 채운다(비어 있으면 AnimationName 을 쓴다).
    [Export] public string MaleAnimationOverride = "";
    [Export] public string FemaleAnimationOverride = "";

    // 같은 업무를 지원하는 자리가 여럿이면 큰 값이 먼저 채워진다.
    [Export] public int Priority = 0;

    // 앉거나 눕는 자리. 0 이면 서 있는 자리, 그 외에는 엉덩이(또는 등)가 놓일 높이(m).
    // 직원 체형마다 골반 높이가 달라서, 실제 보정은 컨트롤러가 런타임에 계산한다.
    [Export] public bool IsSeated = false;
    [Export] public float SeatHeight = 0f;

    [Export] public int Capacity = 1;

    // 저장고 수레처럼 "여러 명이 번갈아 쓰는" 자리는 점유 검사를 하지 않는다.
    [Export] public bool SharedSpot = false;

    // 이 자리에서 손에 드는 소품(표현 전용): "" · "clipboard" · "wrench".
    [Export] public string HandProp = "";

    // 앉는 자리 — 의자 앞 이만큼(m)에 서서 몸을 돌린 뒤 앉는다(chair_sit). 일어날 때도 여기로 선다.
    public const float SitApproach = 0.2f;

    // 손이 실제로 닿아야 하는 점(자식 Marker3D "InteractionTarget"). 있으면 컨트롤러가
    // 작업 클립의 팔 길이(WorkClipReach)만큼 떨어진 곳에 직원을 세운다 — 체형이 달라도 손이 그 점에 간다.
    // 없으면 이 노드 위치에 그대로 선다.
    public Vector3? TargetInParent()
    {
        var t = GetNodeOrNull<Node3D>("InteractionTarget");
        return t == null ? null : Transform * t.Position;
    }

    public string Id => string.IsNullOrEmpty(SpotId) ? Name.ToString() : SpotId;

    public bool Supports(string taskId)
    {
        if (SupportedTaskIds.Length == 0) return true;   // 아무 업무나 받는 예비 자리
        foreach (var t in SupportedTaskIds)
            if (t == taskId) return true;
        return false;
    }

    // 체형에 따라 다른 애니메이션을 쓰고 싶을 때(예: 상자 나르기).
    public string ClipFor(bool female)
    {
        string over = female ? FemaleAnimationOverride : MaleAnimationOverride;
        return string.IsNullOrEmpty(over) ? AnimationName : over;
    }
}
