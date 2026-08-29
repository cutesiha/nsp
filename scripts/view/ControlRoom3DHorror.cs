using Godot;
using NSP.Ui;

namespace NSP.View;

// HorrorDirector(판정)의 신호를 받아 3D 중앙제어실의 "공간 표현"만 담당한다.
// 랜덤 점프스케어 없음 — 실제 금기 위반/사고에만 반응한다.
//  L2  : 형광등 깜빡 + CRT 노이즈 + 미세 카메라 흔들림
//  L3  : 카운트다운 동안 조명 다운
//  [쿵]: 카메라 강타 + 손이 책상을 짚음 + 천장등 OFF + CRT 암전 → 비상등 → 복귀
public partial class ControlRoom3DHorror : Node
{
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingLight";
    [Export] public NodePath CeilingFixturePath = "../ControlRoom/Lights/CeilingFixture";
    [Export] public NodePath EmergencyLightPath = "../ControlRoom/Lights/EmergencyLight";
    [Export] public NodePath EmergencyMeshPath = "../ControlRoom/Lights/EmergencyLight_Mesh";
    [Export] public NodePath CameraRigPath = "../PlayerSeatRig";
    [Export] public NodePath ArmsPath = "../PlayerSeatRig/Camera3D/PlayerArms";
    [Export] public NodePath AtmospherePath = "../Atmosphere";

    private OmniLight3D _ceiling;
    private OmniLight3D _emergency;
    private MeshInstance3D _fixture;
    private StandardMaterial3D _fixtureMat;
    private SeatedCameraRig _rig;
    private PlayerArms _arms;
    private ControlRoomAtmosphere _atmos;

    private float _ceilingBase = 1.1f;
    private bool _wired;

    public override void _Ready()
    {
        _ceiling = GetNodeOrNull<OmniLight3D>(CeilingLightPath);
        _emergency = GetNodeOrNull<OmniLight3D>(EmergencyLightPath);
        _fixture = GetNodeOrNull<MeshInstance3D>(CeilingFixturePath);
        _rig = GetNodeOrNull<SeatedCameraRig>(CameraRigPath);
        _arms = GetNodeOrNull<PlayerArms>(ArmsPath);
        _atmos = GetNodeOrNull<ControlRoomAtmosphere>(AtmospherePath);

        if (_ceiling != null) _ceilingBase = _ceiling.LightEnergy;
        if (_fixture?.GetActiveMaterial(0) is StandardMaterial3D fm)
        {
            _fixtureMat = (StandardMaterial3D)fm.Duplicate();
            _fixture.MaterialOverride = _fixtureMat;
        }
    }

    public override void _Process(double _)
    {
        if (_wired || HorrorDirector.Instance == null) return;
        var d = HorrorDirector.Instance;
        d.Level2Started += OnLevel2;
        d.Level3Started += OnLevel3;
        d.ImpactMoment += OnImpact;
        _wired = true;
    }

    public override void _ExitTree()
    {
        if (!_wired || HorrorDirector.Instance == null) return;
        var d = HorrorDirector.Instance;
        d.Level2Started -= OnLevel2;
        d.Level3Started -= OnLevel3;
        d.ImpactMoment -= OnImpact;
    }

    // --- 핸들러 --------------------------------------------------------

    private void OnLevel2()
    {
        _rig?.Shake(0.8f, 0.22f);
        CCTVMonitorView.Instance?.FlashGlitch(0.5f);
        ControlRoom3DController.Instance?.SetScreenNoise(0.14f);
        var restore = CreateTween();
        restore.TweenInterval(0.5);
        restore.TweenCallback(Callable.From(() => ControlRoom3DController.Instance?.SetScreenNoise(0.035f)));
        FlickerCeiling(2);
    }

    private void OnLevel3(bool taboo)
    {
        if (_ceiling == null) return;
        var t = CreateTween();
        t.TweenProperty(_ceiling, "light_energy", _ceilingBase * 0.35f, 0.4);
    }

    private async void OnImpact()
    {
        _rig?.Shake(3.0f, 0.5f);
        _arms?.PlayDeskBrace();
        _atmos?.CreakChair();
        CCTVMonitorView.Instance?.FlashGlitch(1f);

        SetCeiling(0f);
        SetFixtureGlow(0.05f);
        ControlRoom3DController.Instance?.SetScreenBrightness(0.18f);
        ControlRoom3DController.Instance?.SetScreenDistortion(0.6f);

        await Wait(0.35);
        EmergencyOn();

        await Wait(1.9);
        ControlRoom3DController.Instance?.SetScreenBrightness(1f);
        ControlRoom3DController.Instance?.SetScreenDistortion(0f);
        var t = CreateTween();
        t.TweenProperty(_ceiling, "light_energy", _ceilingBase, 0.6);
        SetFixtureGlow(1.5f);
        EmergencyOff();
    }

    // --- 도우미 -------------------------------------------------------

    private void FlickerCeiling(int times)
    {
        if (_ceiling == null) return;
        var t = CreateTween();
        for (int i = 0; i < times; i++)
        {
            t.TweenProperty(_ceiling, "light_energy", _ceilingBase * 0.15f, 0.04);
            t.TweenProperty(_ceiling, "light_energy", _ceilingBase, 0.09);
        }
    }

    private void SetCeiling(float e)
    {
        if (_ceiling != null) _ceiling.LightEnergy = e;
    }

    private void SetFixtureGlow(float e)
    {
        if (_fixtureMat != null) _fixtureMat.EmissionEnergyMultiplier = e;
    }

    private void EmergencyOn()
    {
        if (_emergency == null) return;
        _emergency.Visible = true;
        var t = CreateTween();
        t.TweenProperty(_emergency, "light_energy", 1.3f, 0.25);
    }

    private void EmergencyOff()
    {
        if (_emergency == null) return;
        var t = CreateTween();
        t.TweenProperty(_emergency, "light_energy", 0f, 0.5);
        t.TweenCallback(Callable.From(() => { if (_emergency != null) _emergency.Visible = false; }));
    }

    private async System.Threading.Tasks.Task Wait(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
