using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
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
    [Export] public NodePath FillLightPath = "../ControlRoom/Lights/FillLight";
    [Export] public NodePath CeilingFixturePath = "../ControlRoom/Lights/CeilingFixture";
    [Export] public NodePath EmergencyLightPath = "../ControlRoom/Lights/EmergencyLight";
    [Export] public NodePath EmergencyMeshPath = "../ControlRoom/Lights/EmergencyLight_Mesh";
    [Export] public NodePath CameraRigPath = "../PlayerSeatRig";
    [Export] public NodePath ArmsPath = "../ControlRoom/PlayerCharacter";
    [Export] public NodePath AtmospherePath = "../Atmosphere";

    private OmniLight3D _ceiling;
    private OmniLight3D _fill;
    private OmniLight3D _emergency;
    private MeshInstance3D _fixture;
    private StandardMaterial3D _fixtureMat;
    private SeatedCameraRig _rig;
    private PlayerCharacter _arms;
    private ControlRoomAtmosphere _atmos;

    private float _ceilingBase = 1.1f;
    private float _fillBase = 0.55f;
    private bool _wired;
    private bool _lightsOff;         // 조명 스위치 OFF / 정전으로 실내등이 꺼진 상태
    private double _impactBlackUntil; // [쿵] 순간 강제 소등

    public override void _Ready()
    {
        _ceiling = GetNodeOrNull<OmniLight3D>(CeilingLightPath);
        _fill = GetNodeOrNull<OmniLight3D>(FillLightPath);
        _emergency = GetNodeOrNull<OmniLight3D>(EmergencyLightPath);
        _fixture = GetNodeOrNull<MeshInstance3D>(CeilingFixturePath);
        _rig = GetNodeOrNull<SeatedCameraRig>(CameraRigPath);
        _arms = GetNodeOrNull<PlayerCharacter>(ArmsPath);
        _atmos = GetNodeOrNull<ControlRoomAtmosphere>(AtmospherePath);

        if (_ceiling != null) _ceilingBase = _ceiling.LightEnergy;
        if (_fill != null) _fillBase = _fill.LightEnergy;
        if (_fixture?.GetActiveMaterial(0) is StandardMaterial3D fm)
        {
            _fixtureMat = (StandardMaterial3D)fm.Duplicate();
            _fixture.MaterialOverride = _fixtureMat;
        }
    }

    private static readonly Color LightBlue = new(0.5f, 0.65f, 1f);
    private static readonly Color LightRed = new(1f, 0.34f, 0.30f);

    public override void _Process(double delta)
    {
        if (!_wired && HorrorDirector.Instance != null)
        {
            var d = HorrorDirector.Instance;
            d.Level2Started += OnLevel2;
            d.Level3Started += OnLevel3;
            d.ImpactMoment += OnImpact;
            _wired = true;
        }

        TickRoomLighting((float)delta);
    }

    // 실내 조명 통합 제어: 조명 스위치 OFF / 정전 → 방이 어두워지고 비상등이 켜진다.
    // 정상일 때만 사고 수(2+)에 따라 파랑/빨강.
    private void TickRoomLighting(float delta)
    {
        if (_ceiling == null) return;
        var gs = GameState.Instance;
        bool live = gs?.CurrentPhase == GamePhase.Live;
        double now = Time.GetTicksMsec() / 1000.0;

        bool blackout = live && gs.PowerCapacity == 0;
        bool lightingCut = live && !gs.IsConsumerPowered(PowerConsumer.Lighting);
        bool impactBlack = now < _impactBlackUntil;
        _lightsOff = blackout || lightingCut || impactBlack;

        float ceilTarget, fillTarget, fixTarget, emgTarget;
        Color colTarget;

        if (_lightsOff)
        {
            // 코어실·격리실은 별도 시스템이라 여기서 건드리지 않는다(중앙제어실 + 일반 작업실만).
            ceilTarget = 0f;
            fillTarget = 0f;
            fixTarget = 0.02f;
            emgTarget = blackout || impactBlack ? 1.3f : 0.7f;
            colTarget = LightBlue;
        }
        else
        {
            float mul = 1f;
            int acc = ActiveAccidentCount();
            colTarget = acc >= 2 ? LightRed : LightBlue;
            ceilTarget = _ceilingBase * mul;
            fillTarget = _fillBase * mul;
            fixTarget = 1.5f;
            emgTarget = 0f;
        }

        float k = Mathf.Clamp(delta * (_lightsOff ? 9f : 4f), 0f, 1f);
        _ceiling.LightEnergy = Mathf.Lerp(_ceiling.LightEnergy, ceilTarget, k);
        _ceiling.LightColor = _ceiling.LightColor.Lerp(colTarget, Mathf.Clamp(delta * 1.8f, 0f, 1f));
        if (_fill != null) _fill.LightEnergy = Mathf.Lerp(_fill.LightEnergy, fillTarget, k);
        SetFixtureGlow(Mathf.Lerp(_fixtureMat?.EmissionEnergyMultiplier ?? 1.5f, fixTarget, k));

        if (_emergency != null)
        {
            if (emgTarget > 0.01f && !_emergency.Visible) _emergency.Visible = true;
            _emergency.LightEnergy = Mathf.Lerp(_emergency.LightEnergy, emgTarget, Mathf.Clamp(delta * 4f, 0f, 1f));
            if (emgTarget < 0.01f && _emergency.LightEnergy < 0.03f) _emergency.Visible = false;
        }
    }

    private static int ActiveAccidentCount()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return 0;
        int n = 0;
        foreach (var id in sim.GetRoomIds())
        {
            if (sim.GetRoomDef(id)?.IsRestricted == true) continue;
            if (NSP.Ui.RoomStatusText.GetDangerTier(id) == NSP.Ui.RoomDangerTier.Failure) n++;
        }
        return n;
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
        // 짧은 깜빡임 — TickRoomLighting 을 잠깐 흔든다.
        _impactBlackUntil = System.Math.Max(_impactBlackUntil, Time.GetTicksMsec() / 1000.0 + 0.14);
    }

    private void OnLevel3(bool taboo)
    {
        _impactBlackUntil = System.Math.Max(_impactBlackUntil, Time.GetTicksMsec() / 1000.0 + 0.5);
    }

    private async void OnImpact()
    {
        _rig?.Shake(3.0f, 0.5f);
        _arms?.PlayDeskBrace();
        _atmos?.CreakChair();
        CCTVMonitorView.Instance?.FlashGlitch(1f);

        _impactBlackUntil = Time.GetTicksMsec() / 1000.0 + 2.25;   // 강제 소등 → 비상등 → 복귀는 TickRoomLighting
        ControlRoom3DController.Instance?.SetScreenBrightness(0.18f);
        ControlRoom3DController.Instance?.SetScreenDistortion(0.6f);

        await Wait(2.25);
        ControlRoom3DController.Instance?.SetScreenBrightness(1f);
        ControlRoom3DController.Instance?.SetScreenDistortion(0f);
    }

    // --- 도우미 -------------------------------------------------------

    private void SetFixtureGlow(float e)
    {
        if (_fixtureMat != null) _fixtureMat.EmissionEnergyMultiplier = e;
    }

    private async System.Threading.Tasks.Task Wait(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
