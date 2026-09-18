using Godot;

namespace NSP.View;

// 직원 임시 3D 플레이스홀더. 저폴리 블록형 인간 실루엣(약 1.8m, 발바닥이 원점).
// 색만 다르게 해서 CCTV 에서 누가 누구인지 구분한다. 나중에 정식 모델로 교체할 때
// 이 씬만 갈아끼우면 되도록 색/식별 로직을 여기 한곳에 둔다.
[Tool]
public partial class EmployeePlaceholder : Node3D
{
    [Export] public Color BodyColor = new(0.7f, 0.7f, 0.72f);

    private StandardMaterial3D _bodyMat;
    private StandardMaterial3D _headMat;

    public override void _Ready() => Apply();

    // FacilityCctvWorld 가 직원별 IconColor 로 호출.
    public void SetColor(Color c)
    {
        BodyColor = c;
        Apply();
    }

    private void Apply()
    {
        _bodyMat ??= new StandardMaterial3D { Roughness = 0.85f, Metallic = 0f };
        _headMat ??= new StandardMaterial3D { Roughness = 0.8f, Metallic = 0f };
        _bodyMat.AlbedoColor = BodyColor;
        _headMat.AlbedoColor = BodyColor.Lerp(Colors.White, 0.35f);

        foreach (var node in FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi) continue;
            mi.MaterialOverride = mi.Name.ToString().Contains("Head") ? _headMat : _bodyMat;
        }
    }
}
