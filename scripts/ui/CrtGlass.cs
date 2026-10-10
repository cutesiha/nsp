using Godot;

namespace NSP.Ui;

// 켜진 CRT 의 "빛나는 유리" 바탕. 화면 뷰가 _Draw 맨 처음에 바탕 대신 이걸 그리면
// 그 뒤에 그리는 패널 · 선 · 글자가 전부 이 빛 위에 얹힌다(모니터 셰이더는 UI 를 건드리지 않는다).
// 가운데가 밝고 가장자리로 갈수록 어두워지는 원형 그라데이션 한 장을 모든 화면이 같이 쓴다.
public static class CrtGlass
{
    // 가운데(형광이 가장 센 곳) / 가장자리 색. 패널 색(0.05~0.13)보다 가운데가 조금 밝아야
    // 패널이 "빛나는 유리 위의 어두운 카드"로 읽힌다.
    public static readonly Color Center = new(0.10f, 0.17f, 0.17f);
    public static readonly Color Edge = new(0.030f, 0.048f, 0.050f);

    private static GradientTexture2D _tex;

    public static void Draw(CanvasItem ci, Rect2 rect)
    {
        _tex ??= Build();
        ci.DrawTextureRect(_tex, rect, false);
    }

    private static GradientTexture2D Build()
    {
        var g = new Gradient();
        g.SetColor(0, Center);
        g.SetColor(1, Edge);
        return new GradientTexture2D
        {
            Gradient = g,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.25f, 0.5f),   // 반지름 0.75 — 모서리가 거의 가장자리 색
            Width = 256,
            Height = 192,
        };
    }
}
