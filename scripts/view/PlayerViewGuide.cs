using Godot;

namespace NSP.View;

// 메인 씬 편집기에서만 보이는 1인칭 신체/카메라 기준 가이드.
// 런타임에는 즉시 숨겨 실제 화면이나 충돌에 전혀 관여하지 않는다.
[Tool]
public partial class PlayerViewGuide : Node3D
{
    [Export] public NodePath CameraPath = "../PlayerSeatRig/Camera3D";

    private Camera3D _camera;
    private MeshInstance3D _body;
    private MeshInstance3D _head;
    private MeshInstance3D _leftEye;
    private MeshInstance3D _rightEye;
    private MeshInstance3D _cameraPoint;
    private MeshInstance3D _viewRay;
    private Label3D _label;

    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
        {
            Visible = false;
            SetProcess(false);
            return;
        }

        _camera = GetNodeOrNull<Camera3D>(CameraPath);
        BuildGuide();
        SyncToCamera();
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) SyncToCamera();
    }

    private void BuildGuide()
    {
        if (_body != null) return;

        var bodyMat = GuideMaterial(new Color(0.25f, 0.62f, 0.95f, 0.18f));
        var faceMat = GuideMaterial(new Color(0.35f, 0.78f, 1f, 0.22f));
        var eyeMat = GuideMaterial(new Color(1f, 0.24f, 0.38f, 0.95f), true);
        var cameraMat = GuideMaterial(new Color(1f, 0.85f, 0.15f, 0.95f), true);

        _body = new MeshInstance3D
        {
            Name = "Body_Box",
            Mesh = new BoxMesh { Size = new Vector3(0.48f, 0.68f, 0.24f) },
            MaterialOverride = bodyMat,
        };
        AddChild(_body);

        _head = new MeshInstance3D
        {
            Name = "Face_Circle",
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 20, Rings = 10 },
            MaterialOverride = faceMat,
        };
        AddChild(_head);

        _leftEye = Eye("Left_Eye", eyeMat);
        _rightEye = Eye("Right_Eye", eyeMat);
        _cameraPoint = new MeshInstance3D
        {
            Name = "MAIN_CAMERA_EYE_POINT",
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = cameraMat,
        };
        AddChild(_cameraPoint);

        _viewRay = new MeshInstance3D
        {
            Name = "MAIN_CAMERA_VIEW_DIRECTION",
            Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.012f, 0.72f) },
            MaterialOverride = cameraMat,
        };
        AddChild(_viewRay);

        _label = new Label3D
        {
            Name = "Guide_Label",
            Text = "PLAYER EYES / MAIN CAMERA\nRIGHT ARM = +X",
            FontSize = 24,
            PixelSize = 0.0015f,
            Modulate = new Color(1f, 0.9f, 0.35f),
            NoDepthTest = true,
        };
        AddChild(_label);
    }

    private MeshInstance3D Eye(string name, Material material)
    {
        var eye = new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh { Radius = 0.018f, Height = 0.036f, RadialSegments = 10, Rings = 5 },
            MaterialOverride = material,
        };
        AddChild(eye);
        return eye;
    }

    private static StandardMaterial3D GuideMaterial(Color color, bool glow = false) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = glow,
        Emission = color,
        EmissionEnergyMultiplier = glow ? 2f : 0f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private void SyncToCamera()
    {
        _camera ??= GetNodeOrNull<Camera3D>(CameraPath);
        if (_camera == null || _body == null) return;

        Basis basis = _camera.GlobalTransform.Basis.Orthonormalized();
        Vector3 eye = _camera.GlobalPosition;
        Vector3 right = basis.X;
        Vector3 up = basis.Y;
        Vector3 forward = -basis.Z;

        _head.GlobalTransform = new Transform3D(basis, eye - up * 0.02f + basis.Z * 0.08f);
        _body.GlobalTransform = new Transform3D(basis, eye - up * 0.45f + basis.Z * 0.17f);
        _leftEye.GlobalPosition = eye - right * 0.045f + forward * 0.065f;
        _rightEye.GlobalPosition = eye + right * 0.045f + forward * 0.065f;
        _cameraPoint.GlobalPosition = eye;
        _viewRay.GlobalTransform = new Transform3D(basis, eye + forward * 0.36f);
        _label.GlobalPosition = eye + right * 0.19f + up * 0.15f;
    }
}
