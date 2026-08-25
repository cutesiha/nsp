using Godot;

namespace NSP.Ui;

public partial class CorridorLine : Line2D
{
    [Export] public NodePath RoomANodePath;
    [Export] public NodePath RoomBNodePath;
    [Export] public bool UseElbow = false;

    private Control _roomA;
    private Control _roomB;

    public override void _Ready()
    {
        _roomA = GetNodeOrNull<Control>(RoomANodePath);
        _roomB = GetNodeOrNull<Control>(RoomBNodePath);
    }

    public override void _Process(double delta)
    {
        if (_roomA == null || _roomB == null) return;

        Vector2 a = _roomA.Position + _roomA.Size / 2f;
        Vector2 b = _roomB.Position + _roomB.Size / 2f;

        Points = UseElbow ? new[] { a, new Vector2(a.X, b.Y), b } : new[] { a, b };
    }
}
