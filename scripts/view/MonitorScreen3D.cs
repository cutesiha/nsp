using Godot;

namespace NSP.View;

// 3D CRT 모니터의 화면(QuadMesh). 자기 전용 SubViewport 하나(FacilityMonitorView 또는
// CCTVMonitorView)를 CRT 셰이더로 표시하고, 마우스 레이 → 화면 UV → 그 뷰포트의 2D
// 좌표 변환을 담당한다.
public partial class MonitorScreen3D : MeshInstance3D, IProjectionSurface
{
    // QuadMesh UV 세로가 뒤집혀 보이면 에디터에서 켠다.
    [Export] public bool FlipV = false;

    public ShaderMaterial ScreenMaterial { get; private set; }
    public SubViewport TargetViewport { get; set; }
    public Vector2 CanvasSize { get; set; } = new(800, 600);

    public void Configure(SubViewport viewport)
    {
        TargetViewport = viewport;
        CanvasSize = viewport.Size;

        var shader = GD.Load<Shader>("res://shaders/crt_screen.gdshader");
        ScreenMaterial = new ShaderMaterial { Shader = shader };
        ScreenMaterial.SetShaderParameter("screen_tex", viewport.GetTexture());
        ScreenMaterial.SetShaderParameter("region_min", Vector2.Zero);
        ScreenMaterial.SetShaderParameter("region_max", Vector2.One);
        MaterialOverride = ScreenMaterial;
    }

    // 카메라에서 쏜 월드 레이가 이 화면 앞면과 만나면 뷰포트 2D 픽셀 좌표를 out 한다.
    // clamp=true면 화면을 벗어나도 가장자리로 눌러 붙인다(드래그 중 유지용).
    public bool TryProjectRay(Vector3 rayOrigin, Vector3 rayDir, bool clamp, out Vector2 canvasPos)
    {
        canvasPos = Vector2.Zero;

        Transform3D inv = GlobalTransform.AffineInverse();
        Vector3 localOrigin = inv * rayOrigin;
        Vector3 localDir = inv.Basis * rayDir;

        if (Mathf.Abs(localDir.Z) < 1e-6f) return false;
        float t = -localOrigin.Z / localDir.Z;
        if (t < 0f) return false;

        Vector3 hit = localOrigin + localDir * t;
        float u = hit.X + 0.5f;
        float v = FlipV ? hit.Y + 0.5f : 0.5f - hit.Y;

        bool inside = u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        if (!inside && !clamp) return false;

        u = Mathf.Clamp(u, 0f, 1f);
        v = Mathf.Clamp(v, 0f, 1f);
        canvasPos = new Vector2(u * CanvasSize.X, v * CanvasSize.Y);
        return inside || clamp;
    }
}
