using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 3D 중앙제어실의 "사람이 장비를 조작한다" 계층. 게임 판정과 분리 — 손/팔/카메라/램프/
// 책상 경고등 연출만 한다. Phone3D / EventLog / FacilitySimulation 신호를 구독.
public partial class ControlRoomInteraction : Node
{
    [Export] public NodePath ArmsPath = "../ControlRoom/PlayerCharacter";
    [Export] public NodePath CameraRigPath = "../PlayerSeatRig";
    [Export] public NodePath PhonePath = "../ControlRoom/Telephone";
    [Export] public NodePath WarnLightPath = "../ControlRoom/ControlPanel/Panel_Btn1";
    [Export] public NodePath ConsoleButtonPath = "../ControlRoom/ControlPanel/Panel_Btn2";

    private PlayerCharacter _arms;
    private SeatedCameraRig _rig;
    private Node3D _phone;
    private StandardMaterial3D _warnMat;
    private StandardMaterial3D _consoleBtnMat;
    private Color _warnBase;

    private bool _phoneWired, _logWired;
    private string _lastSurveillanceRoom = "";
    private double _lastTypingSec;
    private double _warnUntil;
    private float _warnPhase;

    public override void _Ready()
    {
        _arms = GetNodeOrNull<PlayerCharacter>(ArmsPath);
        _rig = GetNodeOrNull<SeatedCameraRig>(CameraRigPath);
        _phone = GetNodeOrNull<Node3D>(PhonePath);
        _warnMat = CloneMat(GetNodeOrNull<MeshInstance3D>(WarnLightPath));
        _consoleBtnMat = CloneMat(GetNodeOrNull<MeshInstance3D>(ConsoleButtonPath));
        if (_warnMat != null) _warnBase = _warnMat.Emission;
    }

    private static StandardMaterial3D CloneMat(MeshInstance3D mi)
    {
        if (mi?.GetActiveMaterial(0) is not StandardMaterial3D src) return null;
        var m = (StandardMaterial3D)src.Duplicate();
        mi.MaterialOverride = m;
        return m;
    }

    public override void _Process(double delta)
    {
        WireSignals();
        TickWarnLight((float)delta);
        TickSurveillanceButton();
    }

    private void WireSignals()
    {
        if (!_phoneWired && Phone3D.Instance != null)
        {
            Phone3D.Instance.RingStarted += OnRingStarted;
            Phone3D.Instance.PickedUp += OnPickedUp;
            Phone3D.Instance.HungUp += OnHungUp;
            _phoneWired = true;
        }
        if (!_logWired && EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged += OnEntryLogged;
            _logWired = true;
        }
    }

    // --- 전화 (손 애니메이션은 Phone3D 가 PlayerCharacter 를 직접 구동) --------

    private void OnRingStarted()
    {
        if (_phone != null) _rig?.FocusOn(_phone.GlobalPosition, 0.3f);
    }

    private void OnPickedUp()
    {
        _rig?.PhonePosture(true);       // 고개를 수화기 쪽으로 기울인다
        Sfx.Instance?.Play("phone_pickup", -4f);
    }

    private void OnHungUp()
    {
        _rig?.ClearFocus(0.4f);
        _rig?.PhonePosture(false);
        Sfx.Instance?.Play("phone_hangup", -5f);
    }

    // --- 명령 입력 → 타건 -------------------------------------------

    private void OnEntryLogged()
    {
        // 근무 배치 단계의 배치 로그에는 반응하지 않는다(타건/경고등/전화벨은 근무 중·휴게시간에만).
        if (GameState.Instance?.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return;

        var list = EventLog.Instance?.GetAllEntries();
        if (list == null || list.Count == 0) return;
        var last = list[^1];

        if (last.EventType is LogEventType.Relocation or LogEventType.Isolation)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _lastTypingSec >= 1.6)
            {
                _lastTypingSec = now;
                _arms?.PlayTyping(0.8f);
                Sfx.Instance?.Play("key_type", -9f);
            }
        }

        if (last.EventType is LogEventType.TabooViolation or LogEventType.TaskFailed or LogEventType.PowerOutage
            or LogEventType.CctvDisconnect or LogEventType.Sabotage or LogEventType.Death)
        {
            _warnUntil = Time.GetTicksMsec() / 1000.0 + 4.0;
            Sfx.Instance?.Play("alarm", -6f, 0.8f);
        }

        // 사고가 나면 근처 직원이 전화로 보고 — 벨이 울린다(40초 쿨타임).
        if (last.EventType is LogEventType.TaskFailed or LogEventType.Sabotage
            && Phone3D.Instance is { IsBusy: false }
            && Time.GetTicksMsec() / 1000.0 - _lastIncomingCall > 40.0)
        {
            var sim = FacilitySimulation.Instance;
            string caller = sim?.GetRoomState(last.RoomId)?.OccupantEmployeeIds
                .FirstOrDefault(id => sim.GetEmployeeState(id) is { Alive: true, Isolated: false });
            if (!string.IsNullOrEmpty(caller))
            {
                _lastIncomingCall = Time.GetTicksMsec() / 1000.0;
                Phone3D.Instance.RingIncoming(caller);
            }
        }
    }

    private double _lastIncomingCall = -100;

    // 책상 경고등: 사고 시 붉게 명멸.
    private void TickWarnLight(float delta)
    {
        if (_warnMat == null) return;
        bool on = Time.GetTicksMsec() / 1000.0 < _warnUntil;
        if (on)
        {
            _warnPhase += delta * 12f;
            float k = 0.5f + 0.5f * Mathf.Sin(_warnPhase);
            _warnMat.Emission = new Color(0.95f, 0.1f, 0.08f);
            _warnMat.EmissionEnergyMultiplier = 1f + 3f * k;
        }
        else
        {
            _warnMat.Emission = _warnBase;
            _warnMat.EmissionEnergyMultiplier = 2f;
        }
    }

    // --- CCTV 전환 → 콘솔 버튼 -------------------------------------

    private void TickSurveillanceButton()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        string cur = sim.SurveillanceTargetRoomId ?? "";
        if (cur == _lastSurveillanceRoom) return;
        _lastSurveillanceRoom = cur;
        if (string.IsNullOrEmpty(cur)) return;

        _arms?.PlayButtonPress();
        Sfx.Instance?.Play("switch", -8f);
        if (_consoleBtnMat != null)
        {
            var t = CreateTween();
            t.TweenMethod(Callable.From<float>(v => _consoleBtnMat.EmissionEnergyMultiplier = v), 5f, 2f, 0.35);
        }
    }

    public override void _ExitTree()
    {
        if (_phoneWired && Phone3D.Instance != null)
        {
            Phone3D.Instance.RingStarted -= OnRingStarted;
            Phone3D.Instance.PickedUp -= OnPickedUp;
            Phone3D.Instance.HungUp -= OnHungUp;
        }
        if (_logWired && EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntryLogged;
    }
}
