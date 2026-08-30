using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Ui;

public partial class RoomView : ColorRect
{
    [Export] public string RoomId = "";

    private static readonly Color RelocateHighlight = new(0.85f, 0.25f, 0.25f, 0.9f);

    // 고장 시 방 종류별로 다른 파티클(발전실=전기 스파크, 그 외=연기/노이즈 느낌) — 실제
    // 텍스처 에셋 없이 순수 색상 점으로만 표현(placeholder 아님, 그냥 최소 표현).
    private static readonly Dictionary<RoomResourceType, Color> FailureParticleColor = new()
    {
        [RoomResourceType.Power] = new Color(1f, 0.95f, 0.35f, 1f),
        [RoomResourceType.Survival] = new Color(0.75f, 0.75f, 0.75f, 0.65f),
        [RoomResourceType.Materials] = new Color(0.9f, 0.55f, 0.2f, 0.85f),
        [RoomResourceType.Surveillance] = new Color(0.45f, 0.8f, 1f, 0.8f),
        [RoomResourceType.CoreRepair] = new Color(1f, 0.3f, 0.3f, 0.85f),
        [RoomResourceType.Stress] = new Color(0.8f, 0.85f, 0.95f, 0.6f),
        [RoomResourceType.Storage] = new Color(0.65f, 0.6f, 0.5f, 0.6f),
    };

    private Label _label;
    private Label _statusPopup;
    private Color _normalColor;
    private AudioStreamPlayer _workSfx;
    private CpuParticles2D _particles;
    private RoomDangerTier _lastTier = RoomDangerTier.None;
    private bool _wasActive;
    private bool _workLoop;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        if (_label != null)
            _label.MouseFilter = MouseFilterEnum.Ignore;
        _normalColor = Color;
        MouseFilter = MouseFilterEnum.Stop;

        // 이 방을 CCTV로 보고 있을 때만 도는 수리음(망치 소리) 루프.
        _workSfx = new AudioStreamPlayer { VolumeDb = -8f };
        const string repairPath = "res://assets/audio/sfx/repair.wav";
        if (ResourceLoader.Exists(repairPath))
            _workSfx.Stream = GD.Load<AudioStream>(repairPath);
        _workSfx.Finished += () => { if (_workLoop && _workSfx.Stream != null) _workSfx.Play(); };
        AddChild(_workSfx);

        _statusPopup = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-60f, -46f),
            Size = new Vector2(Size.X + 120f, 44f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _statusPopup.AddThemeFontSizeOverride("font_size", 16);
        _statusPopup.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        _statusPopup.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 1f));
        _statusPopup.AddThemeConstantOverride("outline_size", 6);
        AddChild(_statusPopup);

        _particles = BuildFailureParticles();
        AddChild(_particles);

        FacilitySimulation.Instance?.SetRoomVisualCenter(RoomId, Position + Size / 2f);
        FacilitySimulation.Instance?.SetRoomVisualColor(RoomId, _normalColor);
    }

    private CpuParticles2D BuildFailureParticles()
    {
        var roomDef = FacilitySimulation.Instance?.GetRoomDef(RoomId);
        Color color = FailureParticleColor.GetValueOrDefault(roomDef?.ManagedResource ?? RoomResourceType.None, new Color(0.8f, 0.8f, 0.8f, 0.7f));
        bool sparkStyle = roomDef?.ManagedResource is RoomResourceType.Power or RoomResourceType.CoreRepair;

        var p = new CpuParticles2D
        {
            Emitting = false,
            Amount = sparkStyle ? 20 : 14,
            Lifetime = sparkStyle ? 0.35 : 1.1,
            Position = Size / 2f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Mathf.Min(Size.X, Size.Y) * 0.35f,
            Direction = new Vector2(0f, sparkStyle ? 0f : -1f),
            Spread = sparkStyle ? 180f : 40f,
            Gravity = sparkStyle ? Vector2.Zero : new Vector2(0f, -18f),
            InitialVelocityMin = sparkStyle ? 30f : 6f,
            InitialVelocityMax = sparkStyle ? 90f : 18f,
            ScaleAmountMin = sparkStyle ? 1.5f : 3f,
            ScaleAmountMax = sparkStyle ? 3f : 5f,
            Color = color,
        };
        return p;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        var sim = FacilitySimulation.Instance;
        string relocatingId = sim.RelocatingEmployeeId;

        if (!string.IsNullOrEmpty(relocatingId))
        {
            if (IsValidRelocateTarget(sim, relocatingId))
            {
                // ClearAssignment 없이 바로 재배치 — 이동 중이면 향하던 방까지 마저 가고
                // 이어붙는다(BeginPathTo). 중간에 통로에서 튕겨나가지 않게.
                sim.AssignToRoom(relocatingId, RoomId);
                sim.CancelRelocating();
                NSP.Core.Sfx.Instance?.Play("assign", -6f);
                EmployeeDetailCard.Instance?.Refresh();
            }
            return;
        }

        sim.SetSurveillanceTarget(RoomId);
        EmployeeDetailCard.Instance?.HideCard();
        RoomDetailCard.Instance?.Show(RoomId);
    }

    // --- 직원 아이콘 → 방 드래그 앤 드롭 (이번 리워크의 핵심 조작) -----------------
    // ScheduleScene 의 EmployeeChip/RoomSlot 과 같은 패턴. 드롭 시 기존 AssignToRoom
    // (→ BeginPathTo → FindPath) 를 그대로 태워 실제로 이동하게 한다.

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.String) return false;
        var sim = FacilitySimulation.Instance;
        if (sim == null) return false;

        string employeeId = data.AsString();
        var emp = sim.GetEmployeeState(employeeId);
        if (emp == null || !emp.Alive || emp.Isolated) return false;
        if (emp.AssignedRoomId == RoomId) return false;

        return sim.CanAssignToRoom(RoomId);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var sim = FacilitySimulation.Instance;
        string employeeId = data.AsString();

        // ClearAssignment 생략 — AssignToRoom이 재배치까지 처리하고, 이동 중이면
        // 향하던 방까지 마저 걸어간 뒤 새 경로를 잇는다.
        sim.AssignToRoom(employeeId, RoomId);
        sim.CancelRelocating();
        NSP.Core.Sfx.Instance?.Play("assign", -6f);

        EmployeeDetailCard.Instance?.Refresh();
        RoomDetailCard.Instance?.HideCard();
    }

    private bool IsDragDropTarget()
    {
        var vp = GetViewport();
        if (vp == null || !vp.GuiIsDragging()) return false;
        var data = vp.GuiGetDragData();
        return data.VariantType == Variant.Type.String && _CanDropData(Vector2.Zero, data);
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(RoomId)) return;

        var def = sim.GetRoomDef(RoomId);
        var state = sim.GetRoomState(RoomId);
        if (def == null || state == null) return;

        var tier = def.IsRestricted ? RoomDangerTier.None : RoomStatusText.GetDangerTier(RoomId);
        string activity = def.IsRestricted ? "" : RoomStatusText.BuildActivityLine(RoomId);

        UpdateBoxColor(state, tier);
        UpdateNameLabel(def, state);
        UpdateStatusPopup(def, tier, activity);
        UpdateWorkLoop(sim, activity);

        if (_particles != null)
            _particles.Emitting = tier == RoomDangerTier.Failure;

        // 작업실이 완전히 고장난 순간 — "위잉 위잉" 경고음 2번.
        if (tier == RoomDangerTier.Failure && _lastTier != RoomDangerTier.Failure)
            NSP.Core.Sfx.Instance?.PlayScaryWarning(-3f);

        _lastTier = tier;
        _wasActive = !string.IsNullOrEmpty(activity);
    }

    private void UpdateBoxColor(RoomState state, RoomDangerTier tier)
    {
        var sim = FacilitySimulation.Instance;
        bool relocateHighlight = !string.IsNullOrEmpty(sim.RelocatingEmployeeId)
            && IsValidRelocateTarget(sim, sim.RelocatingEmployeeId);

        if (relocateHighlight || IsDragDropTarget())
        {
            Color = RelocateHighlight;
        }
        else if (tier == RoomDangerTier.Failure)
        {
            // 강하게 명멸하는 붉은색.
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)(Time.GetTicksMsec() / 110.0));
            Color = new Color(0.55f + 0.4f * pulse, 0.04f, 0.05f);
        }
        else
        {
            Color = state.PowerOn ? _normalColor : new Color(0.06f, 0.06f, 0.07f);
        }
    }

    // 방 이름 + 자주 안 바뀌는 상태(출입제한/봉쇄/적색등)만 박스 안 라벨에 남긴다.
    // "~중...", 위험 단계, 코어 진행바처럼 계속 바뀌는 건 전부 박스 위 팝업(_statusPopup)으로.
    private void UpdateNameLabel(RoomDef def, RoomState state)
    {
        if (_label == null) return;

        string text = def.DisplayName;
        if (def.IsRestricted) text += "\n[출입제한]";
        if (state.Locked) text += "\n[봉쇄]";
        if (state.RedAlertLighting) text += "\n[적색등]";
        _label.Text = text;
    }

    private void UpdateStatusPopup(RoomDef def, RoomDangerTier tier, string activity)
    {
        if (_statusPopup == null) return;

        if (def.IsRestricted)
        {
            _statusPopup.Text = "";
            return;
        }

        string line = RoomStatusText.BuildRoomStatusBlock(RoomId);

        if (def.IsCoreRoom)
        {
            string bar = RoomStatusText.BuildCoreBar(GameState.Instance.CoreProgress);
            line = string.IsNullOrEmpty(line) ? bar : $"{line}\n{bar}";
        }

        _statusPopup.Text = line;
    }

    // 지금 CCTV로 보고 있는 방에서 실제로 업무가 진행 중일 때만 망치 소리를 반복 재생한다.
    // (위험/고장 효과음은 FloatingPopupLayer가 사건 로그 기준으로 담당 — 여기서 중복 재생 안 함.)
    private void UpdateWorkLoop(FacilitySimulation sim, string activity)
    {
        if (_workSfx == null) return;
        bool working = sim.SurveillanceTargetRoomId == RoomId && !string.IsNullOrEmpty(activity);

        if (working && !_workLoop)
        {
            _workLoop = true;
            if (_workSfx.Stream != null && !_workSfx.Playing) _workSfx.Play();
        }
        else if (!working && _workLoop)
        {
            _workLoop = false;
            _workSfx.Stop();
        }
    }

    private bool IsValidRelocateTarget(FacilitySimulation sim, string relocatingEmployeeId)
    {
        if (!sim.CanAssignToRoom(RoomId)) return false;
        var state = sim.GetEmployeeState(relocatingEmployeeId);
        return state != null && state.CurrentRoomId != RoomId;
    }
}
