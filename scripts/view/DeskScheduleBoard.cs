using System;
using Godot;

namespace NSP.View;

// 책상 위 "근무 배치표". 별도 2D Schedule 씬을 열지 않고, 제어실 안에서 카메라가
// 책상을 내려다보면 이 종이 위에서 직원을 배치한다. 내부 데이터/배치는 전부 기존
// FacilitySimulation(AssignToRoom / ClearAssignment) 을 그대로 쓴다.
public partial class DeskScheduleBoard : Node3D, IProjectionSurface
{
    [Export] public Vector2I CanvasSize = new(768, 560);

    public event Action StartRequested;
    public SubViewport TargetViewport => _vp;
    public Vector3 SurfaceCenterWorld => _surface != null ? _surface.GlobalPosition : GlobalPosition;
    public Vector3 SurfaceNormalWorld => _surface != null ? _surface.GlobalTransform.Basis.Z.Normalized() : Vector3.Up;

    private SubViewport _vp;
    private MeshInstance3D _surface;
    private ScheduleBoardUI _ui;
    private Node3D _pen;

    public override void _Ready()
    {
        _vp = new SubViewport
        {
            Size = CanvasSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            HandleInputLocally = true,
            GuiDisableInput = false,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(_vp);

        _ui = new ScheduleBoardUI { CanvasSize = CanvasSize };
        _ui.StartPressed = () => StartRequested?.Invoke();
        _vp.AddChild(_ui);

        _surface = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One },
            Scale = new Vector3(0.56f, 0.40f, 1f),
            RotationDegrees = new Vector3(-68f, 0f, 0f),
            Position = new Vector3(0f, 0.10f, 0f),
        };
        _surface.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = _vp.GetTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(_surface);

        BuildPen();
        Visible = false;
    }

    // 배치표 옆에 자연스럽게 놓인 펜 — 근무 배치 단계에서만 보인다.
    private void BuildPen()
    {
        _pen = new Node3D
        {
            Position = new Vector3(0.10f, -0.006f, 0.185f),
            RotationDegrees = new Vector3(0f, 18f, 90f),
        };

        var bodyMat = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.06f, 0.09f), Roughness = 0.35f, Metallic = 0.15f };
        var metalMat = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.73f, 0.76f), Metallic = 0.75f, Roughness = 0.3f };

        var body = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0045f, BottomRadius = 0.0055f, Height = 0.135f, RadialSegments = 10 },
            MaterialOverride = bodyMat,
        };
        _pen.AddChild(body);

        var clip = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.0018f, 0.032f, 0.005f) },
            Position = new Vector3(0.0065f, 0.035f, 0f),
            MaterialOverride = metalMat,
        };
        _pen.AddChild(clip);

        var tip = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0008f, BottomRadius = 0.0045f, Height = 0.016f, RadialSegments = 10 },
            Position = new Vector3(0f, -0.0755f, 0f),
            MaterialOverride = metalMat,
        };
        _pen.AddChild(tip);

        AddChild(_pen);
    }

    public void SetActive(bool on)
    {
        Visible = on;
        if (_pen != null) _pen.Visible = on;
        if (on)
        {
            if (_surface != null)
            {
                _surface.Scale = new Vector3(0.56f, 0.40f, 1f);
                _surface.Position = new Vector3(0f, 0.10f, 0f);
                _surface.RotationDegrees = new Vector3(-68f, 0f, 0f);
            }
            _ui?.Rebuild();
        }
    }

    public void Refresh() => _ui?.Rebuild();

    // 근무 확정 시 종이를 한쪽으로 치우는 연출.
    public void PlayDismiss()
    {
        if (_surface == null) { Visible = false; return; }
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_surface, "position:x", 0.42f, 0.32);
        t.TweenProperty(_surface, "rotation_degrees:z", -14f, 0.32);
        t.Chain().TweenCallback(Callable.From(() =>
        {
            Visible = false;
            if (_pen != null) _pen.Visible = false;
        }));
    }

    public bool TryProjectRay(Vector3 rayOrigin, Vector3 rayDir, bool clamp, out Vector2 canvasPos)
    {
        canvasPos = Vector2.Zero;
        if (_surface == null) return false;

        Transform3D inv = _surface.GlobalTransform.AffineInverse();
        Vector3 lo = inv * rayOrigin;
        Vector3 ld = inv.Basis * rayDir;
        if (Mathf.Abs(ld.Z) < 1e-6f) return false;

        float t = -lo.Z / ld.Z;
        if (t < 0f) return false;

        Vector3 hit = lo + ld * t;
        float u = hit.X + 0.5f;
        float v = 0.5f - hit.Y;

        bool inside = u is >= 0f and <= 1f && v is >= 0f and <= 1f;
        if (!inside && !clamp) return false;

        u = Mathf.Clamp(u, 0f, 1f);
        v = Mathf.Clamp(v, 0f, 1f);
        canvasPos = new Vector2(u * CanvasSize.X, v * CanvasSize.Y);
        return inside || clamp;
    }
}
