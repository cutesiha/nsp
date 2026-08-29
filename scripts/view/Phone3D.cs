using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 책상 위 실제 3D 전화기. 모니터 버튼이나 2D 팝업이 아니라 공간 속 장비다.
//  - 수신: 벨 + 램프 점멸 → 클릭 → 수화기가 올라옴 → 통화 HUD
//  - 발신: 클릭 → 왼쪽 CRT에서 선택한 직원(없으면 근무 중 무작위)에게 통화
// 손 애니메이션은 ControlRoomInteraction 이 신호를 받아 별도로 처리한다(게임 판정과 분리).
public partial class Phone3D : Node3D
{
    [Signal] public delegate void RingStartedEventHandler();
    [Signal] public delegate void PickedUpEventHandler();
    [Signal] public delegate void HungUpEventHandler();

    [Export] public NodePath HandsetPath = "Handset";
    [Export] public NodePath LampPath = "Phone_Lamp";
    [Export] public NodePath RingPlayerPath = "RingPlayer";
    [Export] public NodePath ClickAreaPath = "ClickArea";
    [Export] public NodePath HudPath = "../PhoneCallHud";

    public static Phone3D Instance { get; private set; }

    private enum PhoneState { Idle, Ringing, OnCall }
    private PhoneState _state = PhoneState.Idle;
    private string _caller = "";

    private Node3D _handset;
    private MeshInstance3D _lamp;
    private AudioStreamPlayer3D _ring;
    private Area3D _area;
    private PhoneCallHud _hud;
    private StandardMaterial3D _lampMat;

    private Vector3 _handsetRestPos, _handsetRestRot;
    private float _lampPhase;
    private double _autoPickupAt = -1;

    public bool IsBusy => _state != PhoneState.Idle;

    public override void _Ready()
    {
        Instance = this;
        _handset = GetNodeOrNull<Node3D>(HandsetPath);
        _lamp = GetNodeOrNull<MeshInstance3D>(LampPath);
        _ring = GetNodeOrNull<AudioStreamPlayer3D>(RingPlayerPath);
        _area = GetNodeOrNull<Area3D>(ClickAreaPath);
        _hud = GetNodeOrNull<PhoneCallHud>(HudPath);

        if (_handset != null)
        {
            _handsetRestPos = _handset.Position;
            _handsetRestRot = _handset.RotationDegrees;
        }
        if (_lamp?.GetActiveMaterial(0) is StandardMaterial3D m)
        {
            _lampMat = (StandardMaterial3D)m.Duplicate();
            _lamp.MaterialOverride = _lampMat;
        }
        SetLamp(0.12f);

        if (_area != null) _area.InputEvent += OnAreaInput;
        if (_hud != null) _hud.Closed += HangUp;
    }

    public override void _Process(double delta)
    {
        if (_state == PhoneState.Ringing)
        {
            _lampPhase += (float)delta * 9f;
            SetLamp(0.3f + 2.4f * (0.5f + 0.5f * Mathf.Sin(_lampPhase)));
            if (_autoPickupAt > 0 && Time.GetTicksMsec() / 1000.0 >= _autoPickupAt)
            {
                _autoPickupAt = -1;
                PickUp();
            }
        }
    }

    private void OnAreaInput(Node camera, InputEvent @event, Vector3 pos, Vector3 normal, long shapeIdx)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        if (_state == PhoneState.Idle) StartOutgoing();
        else if (_state == PhoneState.Ringing) PickUp();
        GetViewport().SetInputAsHandled();
    }

    // 게임 이벤트(사고 보고 등)에서 걸려오는 전화.
    public void RingIncoming(string employeeId)
    {
        if (_state != PhoneState.Idle || string.IsNullOrEmpty(employeeId)) return;
        _caller = employeeId;
        _state = PhoneState.Ringing;
        _autoPickupAt = -1;
        _ring?.Play();
        EmitSignal(SignalName.RingStarted);
    }

    private void StartOutgoing()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        string target = FacilityMonitorView.Instance?.SelectedEmployeeId ?? "";
        if (string.IsNullOrEmpty(target) || sim.GetEmployeeState(target) is not { Alive: true, Isolated: false })
        {
            target = sim.GetEmployeeIds()
                .Where(id => sim.GetEmployeeState(id) is { Alive: true, Isolated: false })
                .OrderBy(_ => GD.Randf())
                .FirstOrDefault();
        }
        if (string.IsNullOrEmpty(target)) return;

        _caller = target;
        _state = PhoneState.Ringing;
        _ring?.Play();
        _autoPickupAt = Time.GetTicksMsec() / 1000.0 + 0.9;
        EmitSignal(SignalName.RingStarted);
    }

    private void PickUp()
    {
        _state = PhoneState.OnCall;
        _ring?.Stop();
        SetLamp(2.2f);
        LiftHandset(true);
        EmitSignal(SignalName.PickedUp);
        _hud?.Open(_caller);
    }

    private void HangUp()
    {
        if (_state != PhoneState.OnCall) return;
        _state = PhoneState.Idle;
        SetLamp(0.12f);
        LiftHandset(false);
        EmitSignal(SignalName.HungUp);
    }

    private void LiftHandset(bool up)
    {
        if (_handset == null) return;
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_handset, "position",
            up ? _handsetRestPos + new Vector3(0.02f, 0.14f, 0.03f) : _handsetRestPos, 0.28)
            .SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_handset, "rotation_degrees",
            up ? _handsetRestRot + new Vector3(0f, 0f, 22f) : _handsetRestRot, 0.28);
    }

    private void SetLamp(float e)
    {
        if (_lampMat != null) _lampMat.EmissionEnergyMultiplier = e;
    }
}
