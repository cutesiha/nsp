using Godot;

namespace NSP.Ui;

public partial class CorridorLine : Line2D
{
    [Export] public NodePath RoomANodePath;
    [Export] public NodePath RoomBNodePath;
    // 더 이상 필요 없음(두 방이 같은 행/열이 아니면 자동으로 꺾는다). .tscn 호환용으로만 남김.
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

        bool axisAligned = Mathf.IsEqualApprox(a.X, b.X) || Mathf.IsEqualApprox(a.Y, b.Y);
        if (axisAligned)
        {
            Points = new[] { a, b };
            return;
        }

        // FacilitySimulation.ComputeElbowWaypoint와 동일 규칙: 세로 구간은 더 위쪽 방의 X,
        // 가로 구간은 더 아래쪽 방의 Y. A/B 선언 순서와 무관하게 같은 모서리가 나온다.
        Vector2 upper = a.Y <= b.Y ? a : b;
        Vector2 lower = a.Y <= b.Y ? b : a;
        Points = new[] { a, new Vector2(upper.X, lower.Y), b };
    }
}
