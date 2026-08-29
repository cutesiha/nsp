using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

public partial class CctvView : Control
{
    private static readonly Dictionary<string, (Rect2 Rect, Color Color)[]> RoomFurniture = new()
    {
        ["power_room"] = new[]
        {
            (new Rect2(30, 30, 170, 110), new Color(0.30f, 0.24f, 0.10f)),
            (new Rect2(230, 60, 50, 70), new Color(0.34f, 0.28f, 0.12f)),
        },
        ["vent_room"] = new[]
        {
            (new Rect2(50, 20, 80, 80), new Color(0.14f, 0.28f, 0.28f)),
            (new Rect2(200, 20, 80, 80), new Color(0.14f, 0.28f, 0.28f)),
            (new Rect2(125, 130, 80, 30), new Color(0.16f, 0.24f, 0.24f)),
        },
        ["maintenance_room"] = new[]
        {
            (new Rect2(20, 110, 230, 40), new Color(0.26f, 0.22f, 0.10f)),
            (new Rect2(270, 40, 40, 40), new Color(0.30f, 0.26f, 0.12f)),
            (new Rect2(60, 30, 60, 60), new Color(0.22f, 0.22f, 0.24f)),
        },
        ["medical_room"] = new[]
        {
            (new Rect2(30, 60, 110, 50), new Color(0.20f, 0.24f, 0.28f)),
            (new Rect2(230, 30, 60, 60), new Color(0.22f, 0.26f, 0.30f)),
        },
        ["central_office"] = new[]
        {
            (new Rect2(40, 40, 240, 60), new Color(0.18f, 0.18f, 0.24f)),
            (new Rect2(40, 120, 100, 40), new Color(0.16f, 0.16f, 0.20f)),
        },
        ["guard_room"] = new[]
        {
            (new Rect2(30, 30, 140, 60), new Color(0.16f, 0.16f, 0.2f)),
            (new Rect2(200, 30, 60, 100), new Color(0.14f, 0.14f, 0.18f)),
        },
        ["core_room"] = new[]
        {
            (new Rect2(120, 15, 90, 160), new Color(0.3f, 0.17f, 0.15f)),
            (new Rect2(30, 130, 70, 40), new Color(0.22f, 0.18f, 0.14f)),
            (new Rect2(220, 40, 70, 110), new Color(0.32f, 0.16f, 0.16f)),
        },
        ["storage_room"] = new[]
        {
            (new Rect2(20, 20, 80, 150), new Color(0.19f, 0.21f, 0.16f)),
            (new Rect2(120, 20, 80, 150), new Color(0.19f, 0.21f, 0.16f)),
            (new Rect2(220, 20, 80, 150), new Color(0.19f, 0.21f, 0.16f)),
        },
        ["isolation_room"] = new[]
        {
            (new Rect2(110, 40, 80, 120), new Color(0.22f, 0.15f, 0.2f)),
        },
    };

    private ColorRect _background;
    private Label _recLabel;
    private Label _roomNameLabel;
    private Label _statusLabel;
    private Control _floorArea;

    private readonly List<Node> _furnitureNodes = new();
    private readonly List<Node> _occupantNodes = new();
    private string _lastFurnitureRoomId = "";

    public override void _Ready()
    {
        _background = GetNode<ColorRect>("Background");
        _recLabel = GetNode<Label>("RecLabel");
        _roomNameLabel = GetNode<Label>("RoomNameLabel");
        _statusLabel = GetNode<Label>("StatusLabel");
        _floorArea = GetNode<Control>("FloorArea");
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = sim?.SurveillanceTargetRoomId ?? "";

        if (string.IsNullOrEmpty(roomId))
        {
            ShowStatusOnly("MONITOR 01에서 방을 선택하세요", new Color(0.05f, 0.05f, 0.05f));
            return;
        }

        var def = sim.GetRoomDef(roomId);
        var state = sim.GetRoomState(roomId);
        _roomNameLabel.Text = def?.DisplayName ?? roomId;

        if (state != null && state.CctvDisconnected)
        {
            ShowStatusOnly("[CCTV 단절]", new Color(0.05f, 0.05f, 0.05f));
            return;
        }

        bool powered = GameState.Instance.GetPowerAllocated(PowerConsumer.CctvWatch) > 0;
        if (!powered)
        {
            ShowStatusOnly("전력 부족 — CCTV 신호 없음", new Color(0.05f, 0.05f, 0.05f));
            return;
        }

        _recLabel.Visible = true;
        string block = RoomStatusText.BuildRoomStatusBlock(roomId);
        _statusLabel.Text = string.IsNullOrEmpty(block) ? "정상 근무 중" : block;
        _floorArea.Visible = true;

        bool lit = state == null || !state.RedAlertLighting;
        Color roomColor = sim.GetRoomVisualColor(roomId);
        _background.Color = lit
            ? new Color(roomColor.R * 0.55f, roomColor.G * 0.55f, roomColor.B * 0.55f)
            : new Color(roomColor.R * 0.12f + 0.08f, roomColor.G * 0.06f, roomColor.B * 0.06f);

        RebuildFurniture(roomId);
        RebuildOccupants(sim, state);
    }

    private void ShowStatusOnly(string status, Color backgroundColor)
    {
        _roomNameLabel.Text = "";
        _recLabel.Visible = false;
        _statusLabel.Text = status;
        _background.Color = backgroundColor;
        _floorArea.Visible = false;
        _lastFurnitureRoomId = "";
        ClearNodes(_furnitureNodes);
        ClearNodes(_occupantNodes);
    }

    private void RebuildFurniture(string roomId)
    {
        if (roomId == _lastFurnitureRoomId) return;
        _lastFurnitureRoomId = roomId;

        ClearNodes(_furnitureNodes);
        if (!RoomFurniture.TryGetValue(roomId, out var pieces)) return;

        foreach (var (rect, color) in pieces)
        {
            var piece = new ColorRect { Position = rect.Position, Size = rect.Size, Color = color };
            _floorArea.AddChild(piece);
            _furnitureNodes.Add(piece);
        }
    }

    private void RebuildOccupants(FacilitySimulation sim, RoomState state)
    {
        ClearNodes(_occupantNodes);
        if (state == null) return;

        int i = 0;
        foreach (var employeeId in state.OccupantEmployeeIds)
        {
            var def = sim.GetEmployeeDef(employeeId);
            if (def == null) continue;

            float x = 40 + (i % 4) * 100;
            float y = 210 + (i / 4) * 50;

            var box = new VBoxContainer { Position = new Vector2(x, y) };
            var icon = new ColorRect { Color = def.IconColor, CustomMinimumSize = new Vector2(26f, 26f) };
            var label = new Label { Text = def.Codename, HorizontalAlignment = HorizontalAlignment.Center };
            box.AddChild(icon);
            box.AddChild(label);
            _floorArea.AddChild(box);
            _occupantNodes.Add(box);
            i++;
        }
    }

    private void ClearNodes(List<Node> nodes)
    {
        foreach (var node in nodes)
            node.QueueFree();
        nodes.Clear();
    }
}
