using System.Collections.Generic;
using Godot;
using NSP.Core;
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
    public static FacilityCctvWorld Instance { get; private set; }

    [Export] public Vector3 CameraPosition = new(3.5f, 3.05f, 3.5f);
    [Export] public Vector3 CameraLookAt = new(-0.4f, 0.5f, -0.5f);
    [Export] public float CameraFov = 58f;

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

    private readonly Dictionary<string, Node3D> _rooms = new();
    private readonly Dictionary<string, EmployeePlaceholder> _employees = new();
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

        var ps = GD.Load<PackedScene>("res://scenes/props/employee_placeholder.tscn");
        if (ps == null) { _employeesBuilt = true; return; }

        foreach (var id in ids)
        {
            var ph = ps.Instantiate<EmployeePlaceholder>();
            ph.Visible = false;
            AddChild(ph);
            ph.SetColor(sim.GetEmployeeDef(id)?.IconColor ?? new Color(0.7f, 0.7f, 0.72f));
            _employees[id] = ph;
        }
        _employeesBuilt = true;
    }

    private void BuildEntity()
    {
        var ps = GD.Load<PackedScene>("res://scenes/props/entity.tscn");
        if (ps == null) return;
        _entity = ps.Instantiate<Node3D>();
        _entity.Visible = false;
        _entity.Scale = new Vector3(1.85f, 1.85f, 1.85f); // 모델 로컬 높이 ~1m → 사람 키
        AddChild(_entity);
    }

    public override void _Process(double delta)
    {
        WireHorror();
        if (!_employeesBuilt) BuildEmployees();

        var sim = FacilitySimulation.Instance;
        // 시뮬레이션이 없으면(F6 단독 프리뷰) 방 하나는 보여준다.
        string target = sim == null ? "core_room" : sim.SurveillanceTargetRoomId ?? "";

        if (target != _shownRoom)
        {
            _shownRoom = target;
            foreach (var (roomId, node) in _rooms)
                node.Visible = roomId == target;
        }

        UpdateEmployees(sim, target);
        if (!_hauntActive) UpdateEntity(target);

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
        _entity.Position = new Vector3(-2.3f, 0f, -2.3f);
        _entity.LookAt(_entity.GlobalPosition + new Vector3(1f, 0f, 1f), Vector3.Up); // 카메라 반대쪽(방 안쪽)을 봄
        DimRoomLights(roomId, 0.12f);
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
        if (_entity == null) return;
        Vector3 front = _camera.GlobalPosition + (CameraLookAt - _camera.GlobalPosition).Normalized() * 0.9f;
        front.Y = 0f;
        var t = CreateTween();
        t.TweenProperty(_entity, "position", front, seconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
    }

    // 카메라를 내려친다 — 순간 앞으로 확 튀었다 돌아온다 + 카메라 흔들림.
    public void HauntLunge()
    {
        if (_entity == null) return;
        Vector3 p = _entity.Position;
        var t = CreateTween();
        t.TweenProperty(_entity, "position", p + new Vector3(0.15f, 0.1f, 0.15f), 0.05);
        t.TweenProperty(_entity, "position", p, 0.12);
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

    private void UpdateEmployees(FacilitySimulation sim, string target)
    {
        if (sim == null) return;
        int slot = 0;
        foreach (var (id, ph) in _employees)
        {
            var st = sim.GetEmployeeState(id);
            string room = st == null ? "" : st.Isolated ? "isolation_room" : st.CurrentRoomId;
            bool show = st is { Alive: true } && room == target && _rooms.ContainsKey(target);

            ph.Visible = show;
            if (!show) continue;

            ph.Position = Slots[slot % Slots.Length];
            slot++;
            ph.RotationDegrees = new Vector3(0, 135, 0); // 카메라(+X/+Z 코너) 쪽을 대충 바라봄
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
            _entity.Position = new Vector3(-2.0f, 0f, -2.0f);
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
        _entity.Position = new Vector3(-2.0f, 0f, -2.0f);
        _entityHideAt = Time.GetTicksMsec() / 1000.0 + seconds;
    }
}
