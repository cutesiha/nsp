using Godot;

namespace NSP.View;

// entity.glb(귀신 "존재") 인스턴스에 유령 머티리얼을 입힌다.
// glb 안의 메시(geometry_0)에 material_override 를 걸어 회색빛 흰색 + 매트/노이즈 질감,
// 손끝·발끝 검붉은색(entity_ghost.gdshader)을 적용한다. [Tool] — 에디터에서도 바로 보인다.
[Tool]
public partial class EntityGhost : Node3D
{
    [Export] public Material GhostMaterial;

    public override void _Ready()
    {
        var mat = GhostMaterial ?? GD.Load<Material>("res://assets/materials/entity_ghost.tres");
        if (mat == null) return;

        foreach (var mi in FindChildren("*", "MeshInstance3D", true, false))
            if (mi is MeshInstance3D m)
                m.MaterialOverride = mat;
    }
}
