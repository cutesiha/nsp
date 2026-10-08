using Godot;

namespace NSP.View;

// 작업실 씬에 하나씩 달려 있는 마감 노드.
//
// 하는 일은 한 줄이다 — 부모(방 루트)에 RoomDressing 을 붙인다. 그런데 **[Tool]** 이라서
// 게임이 돌 때뿐 아니라 **고도 에디터에서 씬을 열었을 때도** 돈다. 그래서 에디터 뷰포트에서
// 바로 마감된 방이 보인다(이게 없으면 에디터에서는 영영 예전 상자만 보인다).
//
// 중요 — RoomDressing 이 만드는 노드에는 **owner 를 주지 않는다.** 고도는 owner 가 없는
// 노드를 씬에 저장하지 않으므로, 에디터에서 이 방을 열어 두고 저장해도 .tscn 에는
// 마감 노드가 한 개도 끼어들지 않는다. 열 때마다 새로 그려지고, 저장할 때는 사라진다.
[Tool]
public partial class RoomDresserNode : Node3D
{
    // 어느 방의 마감을 쓸지. FacilityCctvWorld.RoomScenes 의 키와 같은 값이다.
    [Export] public string RoomId { get; set; } = "";

    public override void _Ready()
    {
        // **한 프레임 미룬다.** _Ready 는 자식부터 도는데, 그 시점에 부모(방 루트)는 아직
        // 자기 자식들을 세우는 중이라 AddChild 가 거부된다("parent is busy setting up
        // children"). 재질 덮어쓰기는 먹고 새 노드만 조용히 사라져서, 벽 색만 바뀌고
        // 소품이 하나도 안 붙는 상태가 됐었다.
        CallDeferred(nameof(Dress));
    }

    private void Dress()
    {
        var room = GetParent<Node3D>();
        if (room == null || string.IsNullOrEmpty(RoomId)) return;
        RoomDressing.Apply(RoomId, room);
    }
}
