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

    // SFX 연결용 placeholder — 에셋을 아직 안 넣었으면 null로 두면 된다(재생 시 자동 스킵).
    // 지금은 방 하나에 채널 하나뿐이라 나중에 실제 사운드가 들어오면 우선순위/채널 분리를
    // 다시 검토해야 할 수 있다.
    [Export] public AudioStream WorkingLoopSfx;
    [Export] public AudioStream WarningSfx;
    [Export] public AudioStream FailureSfx;

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
    private AudioStreamPlayer _sfx;
    private CpuParticles2D _particles;
    private RoomDangerTier _lastTier = RoomDangerTier.None;
    private bool _wasActive;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        _normalColor = Color;
        MouseFilter = MouseFilterEnum.Stop;

        _sfx = new AudioStreamPlayer();
        AddChild(_sfx);

        _statusPopup = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-30f, -30f),
            Size = new Vector2(Size.X + 60f, 26f),
        };
        _statusPopup.AddThemeFontSizeOverride("font_size", 12);
        _statusPopup.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        _statusPopup.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 1f));
        _statusPopup.AddThemeConstantOverride("outline_size", 5);
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
                sim.ClearAssignment(relocatingId);
                sim.AssignToRoom(relocatingId, RoomId);
                sim.CancelRelocating();
                EmployeeDetailCard.Instance?.Refresh();
            }
            return;
        }

        sim.SetSurveillanceTarget(RoomId);
        EmployeeDetailCard.Instance?.HideCard();
        RoomDetailCard.Instance?.Show(RoomId);
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
        UpdateSfx(tier, activity);

        if (_particles != null)
            _particles.Emitting = tier == RoomDangerTier.Failure;

        _lastTier = tier;
        _wasActive = !string.IsNullOrEmpty(activity);
    }

    private void UpdateBoxColor(RoomState state, RoomDangerTier tier)
    {
        var sim = FacilitySimulation.Instance;
        if (!string.IsNullOrEmpty(sim.RelocatingEmployeeId) && IsValidRelocateTarget(sim, sim.RelocatingEmployeeId))
        {
            Color = RelocateHighlight;
        }
        else if (tier == RoomDangerTier.Failure)
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)(Time.GetTicksMsec() / 150.0));
            Color = new Color(0.5f * pulse, 0.08f, 0.08f);
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

        string line = RoomStatusText.GetDangerLine(tier);
        if (string.IsNullOrEmpty(line))
        {
            line = activity;
            if (string.IsNullOrEmpty(line) && TabooRuleSystem.Instance != null && TabooRuleSystem.Instance.IsRoomAtTabooRisk(RoomId))
                line = "⚠ 금기 주의";
        }

        if (def.IsCoreRoom)
        {
            string bar = RoomStatusText.BuildCoreBar(GameState.Instance.CoreProgress);
            line = string.IsNullOrEmpty(line) ? bar : $"{line}\n{bar}";
        }

        _statusPopup.Text = line;
    }

    private void UpdateSfx(RoomDangerTier tier, string activity)
    {
        bool isActive = !string.IsNullOrEmpty(activity);

        if (tier == RoomDangerTier.Failure && _lastTier != RoomDangerTier.Failure)
            PlaySfx(FailureSfx);
        else if (tier is RoomDangerTier.Delayed or RoomDangerTier.Unstable && _lastTier == RoomDangerTier.None)
            PlaySfx(WarningSfx);
        else if (isActive && !_wasActive)
            PlaySfx(WorkingLoopSfx);
        else if (!isActive && _wasActive)
            _sfx?.Stop();
    }

    private void PlaySfx(AudioStream stream)
    {
        if (stream == null || _sfx == null) return;
        _sfx.Stream = stream;
        _sfx.Play();
    }

    private bool IsValidRelocateTarget(FacilitySimulation sim, string relocatingEmployeeId)
    {
        if (!sim.CanAssignToRoom(RoomId)) return false;
        var state = sim.GetEmployeeState(relocatingEmployeeId);
        return state != null && state.CurrentRoomId != RoomId;
    }
}
