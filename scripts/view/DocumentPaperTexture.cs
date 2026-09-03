using Godot;

namespace NSP.View;

// SettingsPanel의 종이 얼룩을 공용화한 문서 배경. 배치표와 같은 고정 시드/낡은 종이 결이다.
public partial class DocumentPaperTexture : Control
{
    public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

    public override void _Draw()
    {
        var rng = new RandomNumberGenerator { Seed = 771144 };
        for (int i = 0; i < 22; i++)
        {
            var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
            DrawCircle(p, rng.RandfRange(24f, 84f),
                new Color(0.42f, 0.33f, 0.18f, rng.RandfRange(0.03f, 0.075f)));
        }
        for (int i = 0; i < 420; i++)
        {
            var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
            bool light = rng.Randf() > 0.5f;
            DrawRect(new Rect2(p, new Vector2(1.6f, 1.6f)),
                light ? new Color(0.96f, 0.92f, 0.79f, 0.07f) : new Color(0.32f, 0.25f, 0.14f, 0.07f));
        }

        Color edge = new(0.28f, 0.21f, 0.11f, 0.20f);
        const float border = 18f;
        DrawRect(new Rect2(0, 0, Size.X, border), edge);
        DrawRect(new Rect2(0, Size.Y - border, Size.X, border), edge);
        DrawRect(new Rect2(0, 0, border, Size.Y), edge);
        DrawRect(new Rect2(Size.X - border, 0, border, Size.Y), edge);
    }
}
