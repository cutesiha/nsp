using Godot;

namespace NSP.View;

// 3D 화면(CRT, 책상 위 서류 등) 위로 마우스 레이를 쏴서 그 화면 전용 SubViewport 의
// 2D 픽셀 좌표를 얻기 위한 공통 인터페이스. ControlRoom3DController 가 입력을 전달할 때 쓴다.
public interface IProjectionSurface
{
    SubViewport TargetViewport { get; }
    bool TryProjectRay(Vector3 rayOrigin, Vector3 rayDir, bool clamp, out Vector2 canvasPos);
}
