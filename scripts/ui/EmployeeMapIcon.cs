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
        if (_label != null)
            _label.MouseFilter = MouseFilterEnum.Ignore;
        _normalColor = Color;

        var def = FacilitySimulation.Instance?.GetEmployeeDef(EmployeeId);
        if (def != null && _label != null)
            _label.Text = def.Codename;

        // 이동 중일 때만 보이는 작은 상태 라벨 — 씬 파일을 4번 손대지 않기 위해 코드로 생성.
        _statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-60f, Size.Y + 3f),
            Size = new Vector2(Size.X + 120f, 18f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.75f, 1f));
        _statusLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 1f));
        _statusLabel.AddThemeConstantOverride("outline_size", 4);
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

    // 직원 아이콘을 잡아서 필요한 작업실로 옮긴다 — 이번 리워크의 핵심 조작.
    // 짧게 클릭만 하면 드래그가 시작되지 않아 위 _GuiInput(상세 카드 열기)이 그대로 동작한다.
    public override Variant _GetDragData(Vector2 atPosition)
    {
        var sim = FacilitySimulation.Instance;
        var state = sim?.GetEmployeeState(EmployeeId);
        if (state == null || !state.Alive || state.Isolated)
            return default;

        var preview = new ColorRect { Color = _normalColor, Size = new Vector2(22f, 22f) };
        preview.AddChild(new Label
        {
            Text = sim.GetEmployeeDef(EmployeeId)?.Codename ?? EmployeeId,
            Position = new Vector2(-9f, 22f),
        });
        SetDragPreview(preview);

        EmployeeDetailCard.Instance?.Show(EmployeeId);
        return EmployeeId;
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
