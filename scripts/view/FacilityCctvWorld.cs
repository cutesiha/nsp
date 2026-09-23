using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// CCTV 오른쪽 CRT 에 실제 3D 공간이 보이도록, 각 작업실 씬 + 직원 플레이스홀더를 한 격리된
// SubViewport 월드 안에 모아두고, 왼쪽 모니터에서 고른 방(SurveillanceTargetRoomId)만 보여준다.
//  - 모든 방 씬은 원점에 겹쳐 배치하고, 선택된 방 하나만 Visible=true (나머지 조명/메시는 꺼짐).
//  - 카메라는 단 하나. 방마다 각도가 달라지지 않도록 고정 구도(코너 위에서 대각선 아래)를 공유한다.
//  - 직원 6명은 색만 다른 임시 모델(employee_placeholder.tscn). 현재 방에 있는 직원만 표시.
//  - 결번자(entity.tscn)는 평소 숨김. HorrorDirector L3 때 잠깐 등장(연출 훅만, 튜닝은 이후).
public partial class FacilityCctvWorld : Node3D
{
    private const float EntityScale = 3.2f;
    // entity.glb의 실제 Y 범위는 약 -0.499~+0.495m로 원점이 몸 중앙에 있다.
    // 이 값을 올려야 발이 바닥에 닿고 모델 절반이 지면 아래로 묻히지 않는다.
    private const float EntityFloorOriginY = 0.499f * EntityScale;

    public static FacilityCctvWorld Instance { get; private set; }

    [Export] public Vector3 CameraPosition = new(3.5f, 3.05f, 3.5f);
    [Export] public Vector3 CameraLookAt = new(-0.4f, 0.5f, -0.5f);
    [Export] public float CameraFov = 58f;

    // 휴게시간 인터뷰용 사이드뷰. 근무 CCTV 가 코너에서 내려다보는 부감이라면, 이쪽은
    // 사람 눈높이에서 옆으로 비스듬히 보는 구도다 — 같은 3D 방을 카메라만 바꿔 재사용한다.
    [Export] public string InterviewRoomId = "medical_room";
    [Export] public Vector3 InterviewCameraPosition = new(4.3f, 1.62f, 1.15f);
    [Export] public Vector3 InterviewCameraLookAt = new(-0.3f, 1.15f, -0.55f);
    [Export] public float InterviewCameraFov = 46f;

    private bool _interviewMode;

    // roomId -> 방 씬 경로. central_office(중앙제어실)는 CCTV 대상 아님.
    private static readonly Dictionary<string, string> RoomScenes = new()
    {
        ["power_room"] = "res://scenes/rooms/room_power.tscn",
        ["vent_room"] = "res://scenes/rooms/room_vent.tscn",
        ["maintenance_room"] = "res://scenes/rooms/room_maintenance.tscn",
        ["medical_room"] = "res://scenes/rooms/room_medical.tscn",
        ["guard_room"] = "res://scenes/rooms/room_guard.tscn",
        ["core_room"] = "res://scenes/rooms/room_core.tscn",
        ["storage_room"] = "res://scenes/rooms/room_storage.tscn",
        ["isolation_room"] = "res://scenes/rooms/nsp_isolation_room.tscn",
    };

    // 방 바닥(원점 기준) 위 직원 배치 슬롯. 카메라가 +X/+Z 코너에 있으므로 안쪽으로 몰아둔다.
    private static readonly Vector3[] Slots =
    {
        new(-0.3f, 0f, -0.6f), new(0.9f, 0f, -1.2f), new(-1.3f, 0f, 0.2f),
        new(0.4f, 0f, 0.9f), new(1.4f, 0f, 0.1f), new(-1.0f, 0f, 1.3f),
    };

    // 직원 ID → CCTV 3D 캐릭터 씬. 표시 전용이며 시뮬레이션 좌표/판정에는 관여하지 않는다.
    // 없는 ID 는 예전 원통 플레이스홀더로 대체된다(fallback).
    private static readonly Dictionary<string, string> EmployeeScenes = new()
    {
        ["fox"] = "res://scenes/cctv_characters/employees/FoxEmployee3D.tscn",
        ["dog"] = "res://scenes/cctv_characters/employees/DogEmployee3D.tscn",
        ["cat"] = "res://scenes/cctv_characters/employees/CatEmployee3D.tscn",
        ["sheep"] = "res://scenes/cctv_characters/employees/SheepEmployee3D.tscn",
        ["rabbit"] = "res://scenes/cctv_characters/employees/RabbitEmployee3D.tscn",
        ["wolf"] = "res://scenes/cctv_characters/employees/WolfEmployee3D.tscn",
    };

    private readonly Dictionary<string, Node3D> _rooms = new();
    private readonly Dictionary<string, Node3D> _employees = new();
    private readonly Dictionary<string, EmployeeCctvAnimator> _animators = new();
    // 방 안 작업 자리 배치(표현 전용).
    private readonly RoomWorkVisualController _workVisual = new();
    private Node3D _entity;
    private Camera3D _camera;

    private string _shownRoom = "\0";
    private bool _horrorWired;
    private bool _employeesBuilt;
    private double _entityHideAt = -1;

    private Vector3 _camBaseRot;
    private double _camShakeUntil = -1;
    private float _camShakeStrength;
    private bool _hauntActive;
    private readonly List<(Light3D light, float energy)> _dimmedLights = new();

    public override void _Ready()
    {
        Instance = this;

        _camera = new Camera3D { Fov = CameraFov, Current = true };
        AddChild(_camera);
        _camera.GlobalPosition = CameraPosition;
        _camera.LookAt(CameraLookAt, Vector3.Up);
        _camBaseRot = _camera.Rotation;

        foreach (var (roomId, path) in RoomScenes)
        {
            var ps = GD.Load<PackedScene>(path);
            if (ps == null) { GD.PushWarning($"FacilityCctvWorld: 방 씬 로드 실패 — {path}"); continue; }
            var inst = ps.Instantiate<Node3D>();
            inst.Visible = false;
            AddChild(inst);
            _rooms[roomId] = inst;
        }

        BuildEntity();
    }

    public override void _ExitTree()
    {
        if (_horrorWired && HorrorDirector.Instance != null)
            HorrorDirector.Instance.Level3Started -= OnHorrorLevel3;
        if (Instance == this) Instance = null;
    }

    // FacilitySimulation 이 직원 상태를 다 만든 뒤에야 id 목록이 나온다 — _Process 에서 준비될
    // 때까지 재시도한다(_Ready 순서에 의존하지 않는다).
    private void BuildEmployees()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        var ids = sim.GetEmployeeIds();
        if (ids.Count == 0) return;

        var fallback = GD.Load<PackedScene>("res://scenes/props/employee_placeholder.tscn");

        int seed = 0;
        foreach (var id in ids)
        {
            Node3D actor = null;
            if (EmployeeScenes.TryGetValue(id, out var path))
            {
                var ps = GD.Load<PackedScene>(path);
                if (ps != null) actor = ps.Instantiate<Node3D>();
                else GD.PushWarning($"FacilityCctvWorld: 직원 3D 씬 로드 실패 — {path} (플레이스홀더로 대체)");
            }
            // 씬이 없거나 로드에 실패해도 게임이 멈추지 않게 예전 도형으로 대체한다.
            if (actor == null && fallback != null)
            {
                var ph = fallback.Instantiate<EmployeePlaceholder>();
                ph.SetColor(sim.GetEmployeeDef(id)?.IconColor ?? new Color(0.7f, 0.7f, 0.72f));
                actor = ph;
            }
            if (actor == null) continue;

            actor.Visible = false;
            AddChild(actor);
            _employees[id] = actor;

            var anim = actor.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (anim != null) _animators[id] = new EmployeeCctvAnimator(anim, ++seed);
        }
        _employeesBuilt = true;
    }

    private void BuildEntity()
    {
        var ps = GD.Load<PackedScene>("res://scenes/props/entity.tscn");
        if (ps == null) return;
        _entity = ps.Instantiate<Node3D>();
        _entity.Visible = false;
        // 모델 원본 높이가 작아 어두운 발전실에서 직원/설비 뒤로 묻혔다. CCTV 공격
        // 전용 존재는 화면을 확실히 채우도록 사람보다 훨씬 큰 비율을 사용한다.
        _entity.Scale = Vector3.One * EntityScale;
        AddChild(_entity);
    }

    public override void _Process(double delta)
    {
        _lastDelta = delta;
        WireHorror();
        if (!_employeesBuilt) BuildEmployees();

        var sim = FacilitySimulation.Instance;

        // 휴게시간에는 같은 월드를 '인터뷰 사이드뷰'로 쓴다(오른쪽 CRT = InterviewCCTVView).
        // 방 선택과 카메라만 바뀌며 시뮬레이션 상태는 건드리지 않는다.
        bool interview = GameState.Instance?.CurrentPhase == GamePhase.Rest;
        if (interview != _interviewMode) ApplyCameraMode(interview);

        // 시뮬레이션이 없으면(F6 단독 프리뷰) 방 하나는 보여준다.
        string target = interview ? InterviewRoomId
            : sim == null ? "core_room" : sim.SurveillanceTargetRoomId ?? "";

        if (target != _shownRoom)
        {
            _shownRoom = target;
            foreach (var (roomId, node) in _rooms)
                node.Visible = roomId == target;
        }

        // 인터뷰 화면에서는 직원을 2D 스탠딩 원화로 보여주므로 3D 플레이스홀더는 감춘다.
        if (interview) HideAllActors();
        else
        {
            UpdateEmployees(sim, target);
            if (!_hauntActive) UpdateEntity(target);
        }

        // 카메라 흔들림(카메라 공격 연출).
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _camShakeUntil)
        {
            var r = new Vector3(
                Mathf.DegToRad((float)GD.RandRange(-_camShakeStrength, _camShakeStrength)),
                Mathf.DegToRad((float)GD.RandRange(-_camShakeStrength, _camShakeStrength)),
                Mathf.DegToRad((float)GD.RandRange(-_camShakeStrength, _camShakeStrength) * 0.5f));
            _camera.Rotation = _camBaseRot + r;
        }
        else if (_camera.Rotation != _camBaseRot)
        {
            _camShakeStrength = Mathf.MoveToward(_camShakeStrength, 0f, (float)delta * 40f);
            _camera.Rotation = _camera.Rotation.Lerp(_camBaseRot, Mathf.Clamp((float)delta * 12f, 0f, 1f));
        }
    }

    // 근무 CCTV 부감 ↔ 휴게시간 인터뷰 사이드뷰 전환. 카메라만 옮긴다.
    private void ApplyCameraMode(bool interview)
    {
        _interviewMode = interview;
        if (_camera == null) return;

        _camera.Fov = interview ? InterviewCameraFov : CameraFov;
        _camera.GlobalPosition = interview ? InterviewCameraPosition : CameraPosition;
        _camera.LookAt(interview ? InterviewCameraLookAt : CameraLookAt, Vector3.Up);
        _camBaseRot = _camera.Rotation;   // 흔들림 연출의 기준 자세도 같이 갱신
    }

    private void HideAllActors()
    {
        foreach (var (_, ph) in _employees)
            if (ph.Visible) ph.Visible = false;
        if (_entity != null && _entity.Visible && !_hauntActive) _entity.Visible = false;
    }

    // ── 발전실 금기 이벤트 연출 훅 (PowerRoomTabooEvent 가 순서대로 호출) ──────────

    public void ShakeCamera(float degrees, float seconds)
    {
        _camShakeStrength = Mathf.Max(_camShakeStrength, degrees);
        _camShakeUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    // 결번자가 직원들 뒤(카메라에서 먼 코너)에 소리 없이 나타난다. 방 조명도 확 낮춘다.
    public void HauntSpawn(string roomId)
    {
        _hauntActive = true;
        if (_entity == null) return;
        _entity.Visible = true;
        _entity.Position = new Vector3(-1.6f, EntityFloorOriginY, -1.7f);
        _entity.LookAt(_entity.GlobalPosition + new Vector3(1f, 0f, 1f), Vector3.Up); // 카메라 반대쪽(방 안쪽)을 봄
        DimRoomLights(roomId, 0.25f);
    }

    // 천천히 CCTV 카메라를 바라본다.
    public void HauntLookAtCamera(float seconds)
    {
        if (_entity == null) return;
        var target = new Vector3(_camera.GlobalPosition.X, _entity.GlobalPosition.Y, _camera.GlobalPosition.Z);
        var look = _entity.GlobalTransform.LookingAt(target, Vector3.Up);
        var t = CreateTween();
        t.TweenProperty(_entity, "quaternion", look.Basis.GetRotationQuaternion(), seconds)
            .SetTrans(Tween.TransitionType.Sine);
    }

    // 카메라 바로 앞으로 다가온다(화면을 가득 채움).
    public void HauntChargeCamera(float seconds)
    {
        if (_entity == null || _camera == null) return;

        // 카메라가 놓인 바닥 투영점으로 정확히 돌진한다. 이전 구현은 카메라 시선 벡터의
        // 임의 지점을 로컬 position으로 사용해 옆으로 빗나가 보일 수 있었다.
        Vector3 cameraFloor = new(_camera.GlobalPosition.X, EntityFloorOriginY, _camera.GlobalPosition.Z);
        Vector3 away = _entity.GlobalPosition - cameraFloor;
        away.Y = 0f;
        if (away.LengthSquared() < 0.001f) away = Vector3.Back;
        away = away.Normalized();
        Vector3 near = cameraFloor + away * 1.25f;
        Vector3 extremelyClose = cameraFloor + away * 0.28f;

        var t = CreateTween();
        t.TweenProperty(_entity, "global_position", near, seconds * 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        t.TweenProperty(_entity, "global_position", extremelyClose, seconds * 0.55f)
            .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.In);

        // 원본 GLB에는 AnimationPlayer가 없으므로 접근 중 짧은 좌우 체중 이동을 준다.
        // 위치 트윈과 다른 축(rotation z)만 건드려 CCTV로 향하는 직선 경로는 유지한다.
        var run = CreateTween();
        int strides = Mathf.Max(6, Mathf.CeilToInt(seconds / 0.055f));
        for (int i = 0; i < strides; i++)
            run.TweenProperty(_entity, "rotation_degrees:z", i % 2 == 0 ? -11f : 11f, seconds / strides);
        run.TweenProperty(_entity, "rotation_degrees:z", 0f, 0.05f);

        var distort = CreateTween();
        int jerks = Mathf.Max(4, Mathf.CeilToInt(seconds / 0.09f));
        for (int i = 0; i < jerks; i++)
        {
            Vector3 scale = Vector3.One * EntityScale;
            scale.X *= i % 2 == 0 ? 0.88f : 1.08f;
            scale.Y *= i % 2 == 0 ? 1.10f : 0.94f;
            distort.TweenProperty(_entity, "scale", scale, seconds / jerks);
        }
        distort.TweenProperty(_entity, "scale", Vector3.One * EntityScale, 0.04f);
    }

    // 카메라를 내려친다 — 순간 앞으로 확 튀었다 돌아온다 + 카메라 흔들림.
    public void HauntLunge()
    {
        if (_entity == null) return;
        Vector3 p = _entity.GlobalPosition;
        Vector3 towardCamera = (_camera.GlobalPosition - p).Normalized();
        var t = CreateTween();
        t.TweenProperty(_entity, "global_position", p + towardCamera * 0.24f, 0.05);
        t.TweenProperty(_entity, "global_position", p, 0.12);
        ShakeCamera(3.5f, 0.28f);
    }

    public void HauntEnd()
    {
        _hauntActive = false;
        _entityHideAt = -1;
        if (_entity != null) _entity.Visible = false;
        RestoreRoomLights();
    }

    private void DimRoomLights(string roomId, float factor)
    {
        RestoreRoomLights();
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        foreach (var l in room.FindChildren("*", "Light3D", true, false))
            if (l is Light3D light)
            {
                _dimmedLights.Add((light, light.LightEnergy));
                light.LightEnergy *= factor;
            }
    }

    private void RestoreRoomLights()
    {
        foreach (var (light, energy) in _dimmedLights)
            if (IsInstanceValid(light)) light.LightEnergy = energy;
        _dimmedLights.Clear();
    }

    // 화면에 보이는 자리(슬롯)를 기억해 둔다 — 대화 상대를 바라보게 할 때만 쓴다.
    private readonly Dictionary<string, Vector3> _shownSlots = new();

    private void UpdateEmployees(FacilitySimulation sim, string target)
    {
        if (sim == null) return;

        // 지금 이 방에서 누가 말하고 있는지(자막이 재생 중일 때만 값이 있다).
        var caption = CctvOverheardCaption.Instance;
        var talk = caption?.Current;
        string talkA = "", talkB = "";
        if (talk != null && talk.RoomId == target && !string.IsNullOrEmpty(caption.CurrentSpeaker))
        { talkA = talk.A; talkB = talk.B; }

        _shownSlots.Clear();
        int slot = 0;
        foreach (var (id, actor) in _employees)
        {
            var st = sim.GetEmployeeState(id);
            string room = st == null ? "" : st.Isolated ? "isolation_room" : st.CurrentRoomId;
            // 오늘 배치되지 않은 직원은 근무 인원이 아니다 — 미니맵과 마찬가지로 CCTV 에도 안 잡힌다.
            bool onShift = st != null && (st.Isolated || sim.IsOnDuty(id));
            bool show = st is { Alive: true } && onShift && room == target && _rooms.ContainsKey(target);

            var pos = Slots[slot % Slots.Length];
            if (actor.Visible != show)
            {
                actor.Visible = show;
                if (show)
                {
                    _animators.GetValueOrDefault(id)?.Restart();
                    // 방에 막 들어온 순간에만 기본 슬롯에 세운다. 그 뒤 위치는
                    // RoomWorkVisualController 가 작업 자리까지 걸어가며 직접 옮긴다.
                    actor.Position = pos;
                }
            }
            if (!show) continue;

            _shownSlots[id] = pos;
            slot++;

            var animator = _animators.GetValueOrDefault(id);
            if (animator == null) { actor.RotationDegrees = new Vector3(0, 135, 0); continue; }

            // 표현 동작만 정하고, 실제 위치·회전·애니메이션 재생은 아래 컨트롤러 한 곳이 소유한다.
            // (두 곳에서 같이 만지면 매 프레임 서로 덮어써서 동작이 멈춘 것처럼 보인다.)
            var action = CctvActionResolver.Resolve(sim, st, id, target, talkA, talkB);
            _workQueue.Add((id, actor, animator, action, pos));
        }

        // 작업 자리 배치는 위치/회전을 덮어쓰므로 마지막에 한 번만 돌린다.
        _rooms.TryGetValue(target, out var roomNode);
        _workVisual.Update(sim, roomNode, target, _workQueue, (float)_lastDelta);
        _workQueue.Clear();
    }

    private readonly List<(string, Node3D, EmployeeCctvAnimator, CctvEmployeeAction, Vector3)> _workQueue = new();
    private double _lastDelta;

    // 행동에 따라 바라보는 방향만 바꾼다. 새 내비게이션은 만들지 않는다.
    //   카메라는 +X/+Z 코너에 있고, 캐릭터는 자기 -Z 를 본다.
    //   (작업 자리에 붙어 있을 때의 방향은 RoomWorkVisualController 가 WorkSpot 회전으로 정한다.)
    public float FacingDegrees(CctvEmployeeAction action, string id, string talkA, string talkB, Vector3 pos)
    {
        switch (action)
        {
            case CctvEmployeeAction.Walking:
                return 315f;                 // 방 안쪽(출입구 방향)으로 걸어가는 것처럼
            case CctvEmployeeAction.Working:
            case CctvEmployeeAction.Repairing:
            case CctvEmployeeAction.Suspicious:
                return 200f;                 // 방 안쪽 설비 쪽
            case CctvEmployeeAction.Talking:
            {
                string other = id == talkA ? talkB : talkA;
                if (!string.IsNullOrEmpty(other) && _shownSlots.TryGetValue(other, out var op))
                {
                    var d = op - pos;
                    if (d.LengthSquared() > 0.0001f)
                        return Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z));
                }
                return 135f;
            }
            default:
                return 135f;                 // 평소 — 예전 플레이스홀더와 같은 방향
        }
    }

    private void UpdateEntity(string target)
    {
        if (_entity == null) return;
        if (_entityHideAt > 0 && Time.GetTicksMsec() / 1000.0 >= _entityHideAt)
        {
            _entityHideAt = -1;
            _entity.Visible = false;
        }
        if (_entity.Visible && !string.IsNullOrEmpty(target) && _rooms.ContainsKey(target))
            _entity.Position = new Vector3(-2.0f, EntityFloorOriginY, -2.0f);
    }

    private void WireHorror()
    {
        if (_horrorWired || HorrorDirector.Instance == null) return;
        HorrorDirector.Instance.Level3Started += OnHorrorLevel3;
        _horrorWired = true;
    }

    private void OnHorrorLevel3(bool taboo) => FlashEntity();

    // "결번자 등장" — 현재 보고 있는 방 구석에 잠깐 나타났다 사라진다.
    public void FlashEntity(float seconds = 1.1f)
    {
        if (_entity == null) return;
        _entity.Visible = true;
        _entity.Position = new Vector3(-2.0f, EntityFloorOriginY, -2.0f);
        _entityHideAt = Time.GetTicksMsec() / 1000.0 + seconds;
    }
}
