using Godot;

namespace NSP.View;

// 낡은 기록영상 질감을 한 겹 덮는 판.
//
// 프롤로그 · 엔딩 · 스토리 3D 컷씬은 깨끗한 실시간 화면이 아니라 "시설이 남긴 기록물"
// 이어야 한다. 아래에 무엇이 그려져 있든(3D 컷씬이든 정지 그림이든) 화면을 그대로 읽어
// 가공하므로, 장면마다 따로 손볼 필요가 없다.
//
// 일반 게임플레이에는 절대 붙이지 않는다 — 컷씬에서만 쓴다.
public partial class ArchiveFilm : ColorRect
{
    public enum Look
    {
        Archive,    // 안전교육 기록 — 화질은 낮지만 안정적이다
        Disaster,   // 재난 기록 — 노이즈 · 흔들림 · 신호 끊김이 심해진다
        Cinema,     // 엔딩 — 기본은 시네마틱, 그 위에 낡은 시설 영상 질감만 옅게
    }

    private ShaderMaterial _mat;
    private float _t;
    private float _instability, _instabilityWant;

    public override void _Ready()
    {
        Color = new Color(1, 1, 1, 1);
        MouseFilter = MouseFilterEnum.Ignore;
        var sh = GD.Load<Shader>("res://shaders/archive_film.gdshader");
        if (sh == null) { Visible = false; return; }
        _mat = new ShaderMaterial { Shader = sh };
        Material = _mat;
        SetLook(Look.Archive);
    }

    public void SetLook(Look look)
    {
        if (_mat == null) return;
        switch (look)
        {
            case Look.Disaster:
                Set2(0.075f, 0.10f, 0.40f, 0.0016f, 0.10f, 0.030f);
                _instabilityWant = 0.55f;
                break;
            case Look.Cinema:
                // 엔딩은 "완전히 망가진 화면" 이 아니다 — 낡은 아카이브 필름 정도.
                Set2(0.038f, 0.045f, 0.30f, 0.0007f, 0.08f, 0.022f);
                _instabilityWant = 0.0f;
                break;
            default:
                Set2(0.055f, 0.085f, 0.36f, 0.0011f, 0.16f, 0.028f);
                _instabilityWant = 0.04f;
                break;
        }
    }

    // 한 장면 안에서 신호가 더 무너지게 한다(0~1). SIGNAL LOST · 폭발 직후 등.
    public void Surge(float amount) => _instability = Mathf.Max(_instability, amount);

    private void Set2(float grain, float scan, float vig, float chroma, float desat, float lift)
    {
        _mat.SetShaderParameter("grain", grain);
        _mat.SetShaderParameter("scan", scan);
        _mat.SetShaderParameter("vignette", vig);
        _mat.SetShaderParameter("chroma", chroma);
        _mat.SetShaderParameter("desat", desat);
        _mat.SetShaderParameter("lift", lift);
    }

    public override void _Process(double delta)
    {
        if (_mat == null) return;
        _t += (float)delta;
        // 치솟은 불안정은 서서히 평소 값으로 돌아온다.
        _instability = Mathf.MoveToward(_instability, _instabilityWant, (float)delta * 0.8f);
        _mat.SetShaderParameter("t", _t);
        _mat.SetShaderParameter("instability", _instability);
    }
}
