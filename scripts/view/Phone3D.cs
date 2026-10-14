using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
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
    // 관리자가 시간 안에 받지 않아 직원이 전화를 끊음. IncomingCallQueue 가 다음 전화를 잇는다.
    [Signal] public delegate void CallMissedEventHandler(string employeeId, string dialogueEvent);

    [Export] public NodePath HandsetPath = "Handset";
    [Export] public NodePath LampPath = "Phone_Lamp";
    [Export] public NodePath RingPlayerPath = "RingPlayer";
    [Export] public NodePath ClickAreaPath = "ClickArea";
    [Export] public NodePath HudPath = "../PhoneCallHud";
    [Export] public NodePath PlayerPath = "../PlayerCharacter";
    // 수화기의 손잡이 지점 / 받침대 지점 마커(씬에 Marker3D 로 두고 에디터에서 위치·회전 조정).
    [Export] public NodePath ReceiverGripPath = "Handset/ReceiverGripPoint";
    [Export] public NodePath ReceiverRestPath = "ReceiverRestPoint";
    // 손 소켓 기준 수화기가 자리잡는 오프셋(에디터에서 미세조정).
    [Export] public Vector3 GripOffsetPos = new(0f, 0f, 0.01f);
    [Export] public Vector3 GripOffsetRotDeg = new(0f, 0f, 0f);

    public static Phone3D Instance { get; private set; }

    private enum PhoneState { Idle, Ringing, Connecting, OnCall, Disconnecting }
    private PhoneState _state = PhoneState.Idle;
    private string _caller = "";
    // 이번 통화에 사용할 대사 이벤트(사고/비명/정전/목격/인터뷰/일반). PhoneCallHud 로 넘긴다.
    private string _dialogueEvent = DialogueRepository.EventGeneralCall;
    private string _incidentRoomId = "";
    // 수신 전화인지(true) 관리자 발신인지(false). 수신 전화만 patience 타이머가 돈다.
    private bool _isIncoming;
    // 수신 전화에서 이 시각(초)을 넘기면 직원이 포기하고 끊는다. -1 = 비활성.
    private double _patienceUntil = -1;

    private Node3D _handset;
    private Marker3D _receiverGrip, _receiverRest;
    private MeshInstance3D _lamp;
    private AudioStreamPlayer3D _ring;
    private Area3D _area;
    private PhoneCallHud _hud;
    private PlayerCharacter _player;
    private StandardMaterial3D _lampMat;

    // 다이얼 자리의 원형 발광 링 — 평소 회색, 착신/통화 중 해당 직원 고유색.
    private StandardMaterial3D _dialMat;
    private static readonly Color DialIdle = new(0.20f, 0.21f, 0.23f);

    private Node _handsetRestParent;
    private Transform3D _handsetRestXform;
    private float _lampPhase;
    private float _bodyShakePhase;
    private Vector3 _phoneRestPosition;
    private Vector3 _phoneRestRotation;
    private double _autoPickupAt = -1;

    public bool IsBusy => _state != PhoneState.Idle;
    // 지금 벨이 울리는 중인가. 대사 진행 입력이 수화기 클릭을 가로채지 않게 하는 데 쓴다.
    public bool IsRinging => _state == PhoneState.Ringing;

    public override void _Ready()
    {
        Instance = this;
        _phoneRestPosition = Position;
        _phoneRestRotation = RotationDegrees;
        _handset = GetNodeOrNull<Node3D>(HandsetPath);
        _receiverGrip = GetNodeOrNull<Marker3D>(ReceiverGripPath);
        _receiverRest = GetNodeOrNull<Marker3D>(ReceiverRestPath);
        _lamp = GetNodeOrNull<MeshInstance3D>(LampPath);
        _ring = GetNodeOrNull<AudioStreamPlayer3D>(RingPlayerPath);
        _area = GetNodeOrNull<Area3D>(ClickAreaPath);
        _hud = GetNodeOrNull<PhoneCallHud>(HudPath);
        _player = GetNodeOrNull<PlayerCharacter>(PlayerPath);
        if (_player != null)
        {
            _player.PhoneGripped += CompletePickup;
            _player.PhoneReleased += FinishHangUp;
        }

        if (_handset != null)
        {
            _handsetRestParent = _handset.GetParent();
            _handsetRestXform = _handset.Transform;
        }
        if (_lamp?.GetActiveMaterial(0) is StandardMaterial3D m)
        {
            _lampMat = (StandardMaterial3D)m.Duplicate();
            _lamp.MaterialOverride = _lampMat;
        }
        SetLamp(0.12f);

        BuildDialLed();

        if (_area != null) _area.InputEvent += OnAreaInput;
        CacheClickShape();
        ApplyClickRange();
        if (_hud != null) _hud.Closed += HangUp;
        // 벨이 울리는 동안 「전화 끊기」를 누르면 받지 않고 끊는다.
        if (_hud != null) _hud.RejectRequested += RejectIncoming;

        // 구식 전화벨 — "따르릉 따르릉" 이 벨이 울리는 동안 계속 반복되도록 재생이 끝나면 다시 건다.
        if (_ring != null) _ring.Finished += () =>
        {
            if (_state == PhoneState.Ringing) _ring.Play();
        };
    }

    // 다이얼 자리의 원형 발광 링 — 형상은 씬(Phone_DialRing)에 있고, 여기서는 색을
    // 바꿀 수 있게 머티리얼만 복제해서 잡는다. 씬에 노드가 없으면 코드로 만든다(F6 대비).
    private void BuildDialLed()
    {
        var ring = GetNodeOrNull<MeshInstance3D>("Phone_DialRing");
        if (ring == null)
        {
            ring = new MeshInstance3D
            {
                Name = "Phone_DialRing",
                Mesh = new TorusMesh { InnerRadius = 0.018f, OuterRadius = 0.028f, Rings = 24, RingSegments = 10 },
                Position = new Vector3(0f, 0.031f, 0.02f),
            };
            AddChild(ring);
        }

        _dialMat = ring.GetActiveMaterial(0) is StandardMaterial3D src
            ? (StandardMaterial3D)src.Duplicate()
            : new StandardMaterial3D { EmissionEnabled = true };
        _dialMat.EmissionEnabled = true;
        _dialMat.Emission = DialIdle;
        _dialMat.EmissionEnergyMultiplier = 0.6f;
        ring.MaterialOverride = _dialMat;
    }

    // ── 비정상 신호 전화(지시서 §6-2 ② ③) ───────────────────────────
    //
    // 발신자가 **없는** 전화다. 직원 전화와 같은 벨 · 수화기 · 손 동작을 그대로 쓰되,
    // 받으면 대사 엔진(PhoneCallHud)이 아니라 AnomalyCallDirector 가 받는다.
    //   · 통화 기록 · 증거 · 도전과제 집계 어디에도 남지 않는다.
    //   · 전화기가 비어 있을 때만 울린다 — 진짜 전화를 절대 밀어내지 않는다.
    private string _anomaly = "";

    public bool AnomalyActive => _anomaly != "";
    private static readonly Color UnknownAccent = new(0.52f, 0.56f, 0.58f);

    public bool RingAnomaly(string kind)
    {
        if (_state != PhoneState.Idle || string.IsNullOrEmpty(kind)) return false;
        if (NSP.Taboo.TabooRuleSystem.Instance?.IsPhoneLocked == true) return false;

        _anomaly = kind;
        _caller = "";
        _dialogueEvent = DialogueRepository.EventGeneralCall;
        _incidentRoomId = "";
        _isIncoming = true;
        _state = PhoneState.Ringing;
        _autoPickupAt = -1;
        // 받지 않으면 저 혼자 끊는다. 직원 전화와 달리 재발신도 없다.
        _patienceUntil = Time.GetTicksMsec() / 1000.0 + 7.0;

        _ring?.Play();
        LastCallRejectedByPlayer = false;
        _hud?.ShowIncoming(UnknownAccent);
        EmitSignal(SignalName.RingStarted);
        return true;
    }

    // AnomalyCallDirector 가 연출을 마치면 부른다. 수화기를 내려놓는 동작까지 같다.
    public void EndAnomalyCall()
    {
        if (_anomaly == "") return;
        HangUp();
    }

    private Color CallerColor()
    {
        if (_anomaly != "") return UnknownAccent;
        var def = FacilitySimulation.Instance?.GetEmployeeDef(_caller);
        return def?.IconColor ?? new Color(0.7f, 0.7f, 0.75f);
    }

    private void SetDial(Color color, float energy)
    {
        if (_dialMat == null) return;
        _dialMat.Emission = color;
        _dialMat.EmissionEnergyMultiplier = energy;
    }

    public override void _Process(double delta)
    {
        // 날짜가 바뀌면 클릭 범위도 바뀐다(DAY0 교육만 좁게). 값이 같으면 아무 일도 하지 않는다.
        ApplyClickRange();
        if (_state == PhoneState.Ringing)
        {
            _lampPhase += (float)delta * 9f;
            // 수신 중에는 실제 전화기 본체가 좌우로 짧게 흔들린다. HUD 효과가 아니라
            // 공급된 3D 모델까지 함께 움직여서 책상 위에서 바로 알아볼 수 있게 한다.
            _bodyShakePhase += (float)delta * 25f;
            float wobble = Mathf.Sin(_bodyShakePhase) + 0.35f * Mathf.Sin(_bodyShakePhase * 2.2f);
            Position = _phoneRestPosition + new Vector3(wobble * 0.008f, 0f, 0f);
            RotationDegrees = _phoneRestRotation + new Vector3(0f, 0f, wobble * 3.8f);
            float k = 0.5f + 0.5f * Mathf.Sin(_lampPhase);
            SetLamp(0.3f + 2.4f * k);
            SetDial(CallerColor(), 0.4f + 3.2f * k); // 해당 직원 고유색으로 점멸
            if (_autoPickupAt > 0 && Time.GetTicksMsec() / 1000.0 >= _autoPickupAt)
            {
                _autoPickupAt = -1;
                PickUp();
            }
            // 관리자 패드를 보는 동안에는 근무 시간이 멈춘다 — 직원이 기다리는 시간도 늘어난다.
            if (AdminPad3D.PausesGame && _patienceUntil > 0) _patienceUntil += delta;
            // 캐릭터별 대기시간이 지나면 직원이 전화를 포기한다.
            if (_isIncoming && _patienceUntil > 0 && Time.GetTicksMsec() / 1000.0 >= _patienceUntil)
                GiveUp();
        }
        else if (_state is PhoneState.OnCall or PhoneState.Connecting or PhoneState.Disconnecting)
        {
            _lampPhase += (float)delta * 2.4f;
            SetDial(CallerColor(), 1.6f + 0.9f * (0.5f + 0.5f * Mathf.Sin(_lampPhase)));
        }
        else
        {
            Position = _phoneRestPosition;
            RotationDegrees = _phoneRestRotation;
        }

        // 수화기가 손 소켓을 따라간다(쥔 순간~내려놓는 순간). 스케일 영향 없이 월드에서 직접 맞춘다.
        if (_handsetFollowsHand && _handset != null && _player?.HandSocket != null)
        {
            var sx = _player.HandSocket.GlobalTransform;
            var frame = new Transform3D(sx.Basis.Orthonormalized(), sx.Origin);
            var offset = new Transform3D(
                new Basis(Quaternion.FromEuler(new Vector3(
                    Mathf.DegToRad(GripOffsetRotDeg.X), Mathf.DegToRad(GripOffsetRotDeg.Y), Mathf.DegToRad(GripOffsetRotDeg.Z)))),
                GripOffsetPos);
            _handset.GlobalTransform = _handset.GlobalTransform.InterpolateWith(frame * offset,
                Mathf.Clamp((float)delta * 22f, 0f, 1f));
        }
    }
    private bool _handsetFollowsHand;


    // ── 클릭 범위 (DAY1~5 는 넓게) ────────────────────────────────────
    //
    // 기본 판정 상자는 수화기 언저리만 덮는다. 실제 근무에서는 전화를 급히 집어야 하는데
    // 받침(본체)을 눌러도 안 집혀서 벨이 끊길 때까지 몇 번씩 헛클릭하게 된다.
    // 그래서 DAY1~5 에서는 본체까지 덮도록 넓힌다.
    //
    // **가상 시뮬(DAY0)은 기존 범위 그대로다.** 교육은 "수화기를 든다" 를 가르치는 자리라,
    // 아무 데나 눌러도 집히면 무엇을 눌렀는지가 안 남는다.
    [Export] public Vector3 WideClickSize = new(0.46f, 0.26f, 0.48f);
    // 넓힌 상자는 받침까지 닿도록 조금 내려 앉힌다(위쪽은 수화기를 그대로 덮는다).
    [Export] public float WideClickDrop = 0.02f;

    private CollisionShape3D _clickShape;
    private BoxShape3D _clickBox;
    private Vector3 _clickSizeNarrow;
    private Vector3 _clickPosNarrow;
    private bool? _wideNow;

    private void CacheClickShape()
    {
        if (_area == null) return;
        foreach (var child in _area.GetChildren())
        {
            if (child is not CollisionShape3D cs || cs.Shape is not BoxShape3D box) continue;
            _clickShape = cs;
            // 상자는 씬에 박힌 SubResource 라 그대로 고치면 다음 실행까지 남는다. 복제해서 쓴다.
            _clickBox = (BoxShape3D)box.Duplicate();
            cs.Shape = _clickBox;
            _clickSizeNarrow = _clickBox.Size;
            _clickPosNarrow = cs.Position;
            return;
        }
    }

    // 오늘 넓은 범위를 쓰는가. 교육일(DAY0)만 좁은 기본값을 쓴다.
    private void ApplyClickRange()
    {
        if (_clickBox == null || _clickShape == null) return;
        bool wide = !NSP.Core.DayFeatures.IsTutorialDay;
        if (_wideNow == wide) return;
        // 수화기를 들고 있는 동안에는 재지 않는다 — 수화기가 손에 가 있으면 부피가
        // 책상 절반으로 번진다. 내려놓은 다음 프레임에 다시 잰다.
        if (wide && _handsetFollowsHand) return;
        _wideNow = wide;
        if (!wide)
        {
            _clickBox.Size = _clickSizeNarrow;
            _clickShape.Position = _clickPosNarrow;
            return;
        }
        // 넓은 범위는 **실제 전화기 메시가 차지하는 부피**와 예전 고정 상자를 **둘 다 덮는다.**
        // 고정값만 쓰면 모델이 바뀔 때 받침 한쪽이 판정 밖으로 빠지고(헛클릭),
        // 메시만 쓰면 예전보다 좁아지는 자리가 생긴다. 좁아지는 쪽은 없어야 한다.
        var box = new Aabb(_clickPosNarrow - new Vector3(0f, WideClickDrop, 0f) - WideClickSize * 0.5f,
            WideClickSize);
        if (ModelBounds(out Aabb model)) box = box.Merge(model);

        // 옆에 놓인 관리자 패드까지 먹지 않도록 좌우 폭만 묶는다 —
        // 패드와 전화기는 책상 위에서 0.39 쯤 떨어져 있다.
        float halfX = Mathf.Min(box.Size.X * 0.5f, ClickHalfWidthMax);
        var center = box.Position + box.Size * 0.5f;
        _clickBox.Size = new Vector3(halfX * 2f, box.Size.Y, box.Size.Z);
        _clickShape.Position = center;
    }

    // 전화기 메시(본체 + 수화기) 전부를 감싸는 상자. 전화기 자신의 좌표로 돌려준다.
    private bool ModelBounds(out Aabb bounds)
    {
        bounds = default;
        Aabb? box = null;
        var toLocal = GlobalTransform.AffineInverse();
        foreach (var mesh in Meshes(this))
        {
            var here = toLocal * mesh.GlobalTransform * mesh.GetAabb();
            box = box.HasValue ? box.Value.Merge(here) : here;
        }
        if (!box.HasValue || box.Value.Size.Length() < 0.01f) return false;
        bounds = box.Value.Grow(ClickMargin);
        return true;
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi && mi.Mesh != null) yield return mi;
            foreach (var deeper in Meshes(child)) yield return deeper;
        }
    }

    // 메시 바깥으로 더 두는 여유. 모서리를 눌러도 집히게 한다.
    private const float ClickMargin = 0.03f;
    // 좌우로 이만큼까지만 넓힌다(관리자 패드 침범 방지).
    private const float ClickHalfWidthMax = 0.23f;

    // 지금 클릭 상자 크기(검사용).
    public Vector3 ClickBoxSize => _clickBox?.Size ?? Vector3.Zero;
    public Vector3 NarrowClickSize => _clickSizeNarrow;
    private void OnAreaInput(Node camera, InputEvent @event, Vector3 pos, Vector3 normal, long shapeIdx)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        // 근무 중(Live) 또는 휴게시간(Rest)에만 조작 가능 — 시작 화면 / 근무 배치 단계에서는 무시한다.
        if (_state == PhoneState.Idle && GameState.Instance?.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return;
        // 제어실 입력이 잠긴 동안(단계 전환 연출 · 교육 마무리)에는 수화기도 집지 않는다.
        if (ControlRoom3DController.Instance?.IsInputLocked == true) return;
        // 관리자 패드를 든 동안에는 수화기를 집지 않는다 — 패드를 내려놓고 받는다.
        if (AdminPad3D.Instance?.IsOpen == true) return;

        if (_state == PhoneState.Idle) StartOutgoing();
        else if (_state == PhoneState.Ringing) PickUp();
        else if (_state == PhoneState.OnCall)
        {
            if (_hud != null) _hud.RequestClose(); // Closed 시그널 → HangUp
            else HangUp();
        }
        GetViewport()?.SetInputAsHandled();
    }

    // 게임 이벤트(사고 보고 등)에서 걸려오는 전화. dialogueEvent = DialogueRepository.Event* 중 하나.
    // IncomingCallDirector 만 호출한다(한 번에 한 통화 보장은 그쪽 큐가 담당).
    public void RingIncoming(string employeeId, string dialogueEvent = DialogueRepository.EventGeneralCall,
        string incidentRoomId = "")
    {
        if (_state != PhoneState.Idle || string.IsNullOrEmpty(employeeId)) return;
        _caller = employeeId;
        _dialogueEvent = string.IsNullOrEmpty(dialogueEvent) ? DialogueRepository.EventGeneralCall : dialogueEvent;
        // 어느 작업실 사건인지 — 대사 생성기가 실제 방 이름/사고 종류를 쓰기 위해 필요하다.
        _incidentRoomId = incidentRoomId ?? "";
        _isIncoming = true;
        _state = PhoneState.Ringing;
        _autoPickupAt = -1;

        // 가상 시뮬레이션(DAY0)에서는 직원이 포기하지 않는다 — 받을 때까지 계속 울린다.
        // 교육은 전화를 받아야 다음 단계로 넘어가는데, 직원이 먼저 끊어 버리면 안내가
        // 멈춘 채로 "다시 걸고 기다리는" 공백만 반복된다(TutorialDirector 가 재발신한다).
        if (NSP.Core.DayFeatures.IsTutorialDay) _patienceUntil = -1;
        else
        {
            float patience = FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.IncomingCallPatienceSeconds ?? 5f;
            _patienceUntil = Time.GetTicksMsec() / 1000.0 + Mathf.Max(1f, patience);
        }

        _ring?.Play();
        LastCallRejectedByPlayer = false;
        _hud?.ShowIncoming(CallerColor());
        EmitSignal(SignalName.RingStarted);
    }

    // 관리자가 **직접** 받지 않고 끊었다. 직원이 기다리다 포기한 것과 결과는 같다 —
    // 그 상황의 다음 전화는 IncomingCallDirector 의 큐가 그대로 잇는다.
    public void RejectIncoming()
    {
        if (_state != PhoneState.Ringing || !_isIncoming) return;
        // 관리자가 **분명히** 거절했다 — 그냥 못 받은 것과 뜻이 다른 상황이 있다.
        // (기절한 동료 이송: 안 받으면 직원이 알아서 옥기지만, 거절하면 옥기지 않는다.)
        LastCallRejectedByPlayer = true;
        GiveUp();
    }

    // 방금 끝난 수신 전화를 관리자가 직접 거절했는가(시간 초과가 아니라).
    public bool LastCallRejectedByPlayer { get; private set; }

    // 관리자가 시간 안에 받지 않음 → 직원이 끊는다. 벨을 바로 끊고 "뚝" 소리, LED 회색 복귀.
    private void GiveUp()
    {
        if (_state != PhoneState.Ringing) return;
        _state = PhoneState.Idle;
        _patienceUntil = -1;
        _isIncoming = false;
        _ring?.Stop();
        Sfx.Instance?.Play("phone_hangup", -3f); // 상대가 끊은 "뚝"
        SetLamp(0.12f);
        SetDial(DialIdle, 0.6f);
        _hud?.HideIncoming();

        // 받지 않은 비정상 신호는 아무 일도 아니다. 「무심한 관리자」로도 세지 않고
        // IncomingCallDirector 의 큐에도 알리지 않는다 — 애초에 직원이 건 전화가 아니다.
        if (_anomaly != "")
        {
            _anomaly = "";
            _caller = "";
            return;
        }

        string who = _caller;
        string ev = _dialogueEvent;
        _caller = "";
        // 「무심한 관리자」·「차가운 관리자」 — 한 통당 한 번만 센다. 거절과 무응답은
        // 결과가 같으므로 구분하지 않는다(어느 쪽이든 직원은 혼자 남는다).
        NSP.Core.AchievementManager.Instance?.NoteCallMissed();
        EmitSignal(SignalName.CallMissed, who, ev);
    }

    private void StartOutgoing()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        // 금기 페널티로 전화기가 잠겨 있으면 발신 자체가 되지 않는다.
        if (NSP.Taboo.TabooRuleSystem.Instance?.IsPhoneLocked == true) return;

        bool resting = GameState.Instance?.CurrentPhase == GamePhase.Rest;
        string target = resting
            ? RestRosterView.Instance?.SelectedEmployeeId ?? ""
            : FacilityMonitorView.Instance?.SelectedEmployeeId ?? "";
        // 근무 중에는 오늘 근무표에 올라간 직원에게만 전화가 간다. 휴게시간에는 배치가
        // 의미 없으므로(전원이 쉬는 중) 생존·비격리만 본다.
        // 다만 **아직 깨어나지 않은 직원은 심문할 수 없다** — 의무실에서 회복이 끝나지
        // 않은 채로 근무가 끝났으면 휴게시간에도 의식이 없다.
        bool Callable(string id) => resting
            ? sim.GetEmployeeState(id) is { Alive: true, Isolated: false, Incapacitated: false }
            : sim.IsOnDuty(id) && sim.GetEmployeeState(id)?.Incapacitated != true;

        if (string.IsNullOrEmpty(target) || !Callable(target))
        {
            // 휴게시간에는 대상을 직접 골라야 한다 — 무작위로 아무나 걸지 않는다.
            if (resting) return;
            target = sim.GetEmployeeIds()
                .Where(Callable)
                .OrderBy(_ => GD.Randf())
                .FirstOrDefault();
        }
        if (string.IsNullOrEmpty(target)) return;

        _caller = target;
        // 금기 ④(조명 꺼진 직원 호출) · ⑤(같은 직원 두 번 통화) 판정 지점.
        if (!resting) NSP.Taboo.TabooRuleSystem.Instance?.NotifyCallPlaced(target);
        // 휴게시간의 모든 발신은 실제 로그를 읽는 DAY1 로컬 인터뷰로 연결한다.
        // 방해자 역할은 여기서 정하지 않고 LocalInterviewDialogue가 GameState의 런타임 값만 읽는다.
        _dialogueEvent = resting
            ? LocalInterviewDialogue.EventDay1Interview
            : DialogueRepository.EventGeneralCall;
        _incidentRoomId = "";
        _isIncoming = false;
        _patienceUntil = -1;

        // 관리자가 거는 전화는 수신 전화가 아니다. 벨/진동/자동수신 대기 없이
        // 즉시 수화기를 집는 동작으로 이어진다.
        _state = PhoneState.Ringing;
        _autoPickupAt = -1;
        PickUp();
    }

    // 클릭 → 손이 뻗어 수화기를 쥐러 간다. HUD·통화 상태는 손이 실제로 쥔 뒤에.
    private void PickUp()
    {
        if (_state != PhoneState.Ringing) return;
        _state = PhoneState.Connecting;
        _patienceUntil = -1;
        _ring?.Stop();
        _hud?.HideIncoming();
        SetLamp(2.2f);

        if (_player != null && _handset != null)
        {
            Vector3 gripW = _receiverGrip?.GlobalPosition ?? _handset.GlobalPosition;
            _player.PlayPhonePickup(gripW);
        }
        else
        {
            CompletePickup(); // 플레이어 캐릭터가 없으면(F6 등) 즉시 연결
        }
    }

    // PlayerCharacter.PhoneGripped 신호 — 손가락이 수화기를 다 감은 순간.
    // 이때만 수화기를 손 소켓에 붙인다(손이 닿기 전엔 받침대에 그대로 있다).
    private void CompletePickup()
    {
        if (_state != PhoneState.Connecting) return;
        _state = PhoneState.OnCall;

        _handsetFollowsHand = _handset != null && _player?.HandSocket != null;

        SetDial(CallerColor(), 2.0f);

        // 비정상 신호는 대사 엔진을 타지 않는다. 통화 기록 · 증거 · 도전과제 집계
        // 어디에도 들어가지 않아야 해서, 여기서 갈라져 전용 연출로 넘어간다.
        if (_anomaly != "")
        {
            EmitSignal(SignalName.PickedUp);
            AnomalyCallDirector.Instance?.Begin(_anomaly);
            return;
        }

        if (_dialogueEvent == DialogueRepository.EventInterviewSuspected)
            RestRosterView.Instance?.DisarmInterrogate();
        // 통화가 실제로 연결된 지점 — 「상냥한 관리자」(직접 발신)와
        // 「여섯 명의 이야기」(회차 안에서 만난 사람)를 여기서 센다.
        // 수화기를 집기만 하고 손이 닿지 않았으면 여기까지 오지 않는다.
        NSP.Core.AchievementManager.Instance?.NoteCallConnected(_caller, !_isIncoming);
        EmitSignal(SignalName.PickedUp);
        _hud?.Open(_caller, _dialogueEvent, _incidentRoomId);
    }

    // ── 엔딩 전용 ────────────────────────────────────────────────────────
    // 통화 상태도 HUD 도 만들지 않고 수화기만 든다(TRUE 엔딩의 마지막 통화).
    // 상대는 아무 말도 하지 않는다 — 이 연출에는 대사가 없다.
    public void EndingLift()
    {
        if (_handset == null) return;
        if (_player == null) { _handsetFollowsHand = false; return; }
        _player.PhoneGripped += OnEndingGripped;
        _player.PlayPhonePickup(_receiverGrip?.GlobalPosition ?? _handset.GlobalPosition);
    }

    private void OnEndingGripped()
    {
        if (_player != null) _player.PhoneGripped -= OnEndingGripped;
        _handsetFollowsHand = _handset != null && _player?.HandSocket != null;
    }

    public void EndingHangUp()
    {
        if (_handset == null) return;
        if (_player != null)
        {
            _player.PhoneReleased += OnEndingReleased;
            _player.PlayPhoneHangup(_receiverRest?.GlobalPosition ?? _handsetCradleWorld());
        }
        else OnEndingReleased();
    }

    private void OnEndingReleased()
    {
        if (_player != null) _player.PhoneReleased -= OnEndingReleased;
        _handsetFollowsHand = false;
        var t = CreateTween();
        t.SetParallel(true);
        t.TweenProperty(_handset, "position", _handsetRestXform.Origin, 0.18).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_handset, "quaternion", _handsetRestXform.Basis.GetRotationQuaternion(), 0.18);
    }

    private void HangUp()
    {
        if (_state != PhoneState.OnCall) return;
        _state = PhoneState.Disconnecting;

        if (_player != null && _handset != null)
        {
            Vector3 restW = _receiverRest?.GlobalPosition ?? _handsetCradleWorld();
            _player.PlayPhoneHangup(restW);
        }
        else
        {
            FinishHangUp();
        }
    }

    // PlayerCharacter.PhoneReleased 신호 — 손이 수화기를 받침대에 내려놓은 순간.
    private void FinishHangUp()
    {
        _handsetFollowsHand = false;
        _state = PhoneState.Idle;
        _isIncoming = false;
        _anomaly = "";
        _patienceUntil = -1;
        SetLamp(0.12f);
        SetDial(DialIdle, 0.6f);
        if (_handset != null)
        {
            var t = CreateTween();
            t.SetParallel(true);
            t.TweenProperty(_handset, "position", _handsetRestXform.Origin, 0.18).SetTrans(Tween.TransitionType.Sine);
            t.TweenProperty(_handset, "quaternion", _handsetRestXform.Basis.GetRotationQuaternion(), 0.18);
        }
        EmitSignal(SignalName.HungUp);
    }

    // 수화기가 받침대에 놓인 자리(월드) — 마커가 없을 때 폴백.
    private Vector3 _handsetCradleWorld()
    {
        if (_handsetRestParent is Node3D p) return p.ToGlobal(_handsetRestXform.Origin);
        return _handset?.GlobalPosition ?? GlobalPosition;
    }

    private void SetLamp(float e)
    {
        if (_lampMat != null) _lampMat.EmissionEnergyMultiplier = e;
    }
}
