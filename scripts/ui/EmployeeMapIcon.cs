using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class EmployeeMapIcon : ColorRect
{
    [Export] public string EmployeeId = "";

    public static readonly Dictionary<string, EmployeeMapIcon> Registry = new();

    private const float StackOffsetRadius = 20f;

    private Label _label;
    private Label _statusLabel;
    private Color _normalColor;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        _normalColor = Color;

        var def = FacilitySimulation.Instance?.GetEmployeeDef(EmployeeId);
        if (def != null && _label != null)
            _label.Text = def.Codename;

        // 이동 중일 때만 보이는 작은 상태 라벨 — 씬 파일을 4번 손대지 않기 위해 코드로 생성.
        _statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-40f, Size.Y + 2f),
            Size = new Vector2(Size.X + 80f, 14f),
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 9);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.7f, 1f));
        AddChild(_statusLabel);

        Registry[EmployeeId] = this;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _ExitTree()
    {
        if (Registry.TryGetValue(EmployeeId, out var self) && self == this)
            Registry.Remove(EmployeeId);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            RoomDetailCard.Instance?.HideCard();
            EmployeeDetailCard.Instance?.Show(EmployeeId);
        }
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        var state = sim?.GetEmployeeState(EmployeeId);
        if (state == null) return;

        Color = state.Alive ? _normalColor : new Color(0.3f, 0.3f, 0.3f);

        Vector2 pos = state.Position;
        if (!state.IsMoving)
            pos += GetStackOffset(sim, state);

        Position = pos - Size / 2f;

        if (_statusLabel != null)
        {
            _statusLabel.Visible = state.IsMoving;
            if (state.IsMoving)
                _statusLabel.Text = $"→ {sim.GetRoomDef(state.TargetRoomId)?.DisplayName ?? state.TargetRoomId} 이동 중";
        }
    }

    private Vector2 GetStackOffset(FacilitySimulation sim, EmployeeState state)
    {
        var room = sim.GetRoomState(state.CurrentRoomId);
        if (room == null) return Vector2.Zero;

        int idx = room.OccupantEmployeeIds.IndexOf(EmployeeId);
        if (idx <= 0) return Vector2.Zero;

        float angle = idx * Mathf.Tau / 6f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * StackOffsetRadius;
    }
}
