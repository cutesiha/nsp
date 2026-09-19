using Godot;
using NSP.View;

namespace NSP.Prologue;

// GUIDE-0 얼굴 전용 화면 — 프롤로그에서 왼쪽 CRT(모니터 1)에 뜬다.
//
// 오른쪽 CRT 는 같은 시간 동안 '그림·도형이 도는 창'(GuideHologramView 의 압축 모드)을 맡고,
// 대사는 화면 아래 자막 띠가 맡는다. 셋이 한 장면을 나눠 갖는 구조다.
//
// ★ 얼굴 교체: assets/ui/guide0/guide0_<표정키>.png 를 넣기만 하면 된다.
//   원본은 흰색(또는 밝은 회색) 도트로 찍으면 된다 — 여기서 홀로그램 하늘색으로 물들여 그린다.
public partial class GuideFaceView : Control
{
    public static GuideFaceView Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);

    // 얼굴 칸 — 정사각형. 최종 도트 초상화가 이 안에 비율 그대로 들어간다.
    private static readonly Rect2 FaceBox = new(236f, 128f, 328f, 328f);

    private Font _font;
    private Texture2D _texture;
    private bool _mouthless;
    private string _expression = "normal";
    private float _t;
    private double _noiseUntil;
    private bool _shown;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // GuideHologramView 가 표정을 바꿀 때 같이 불러 준다(초상 파일 탐색 규칙도 그쪽과 공유).
    // mouthless = 입이 없는 얼굴이라 입 Overlay 를 따로 얹어야 한다.
    public void SetPortrait(string expression, Texture2D texture, bool mouthless = false)
    {
        _expression = string.IsNullOrEmpty(expression) ? "normal" : expression;
        _texture = texture;
        _mouthless = mouthless;
        QueueRedraw();
    }

    // 입 모양이 바뀐 프레임에만 다시 그린다(GuideMouthAnimator 가 알려 준다).
    public void NotifyMouthChanged()
    {
        if (_shown) QueueRedraw();
    }

    public void Flash() => _noiseUntil = Time.GetTicksMsec() / 1000.0 + 0.7;

    public void SetShown(bool shown)
    {
        _shown = shown;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // 홀로그램은 늘 아주 미세하게 흔들린다. 매 프레임 다시 그릴 필요는 없다.
        if (Mathf.PosMod(_t, 0.12f) < delta || Time.GetTicksMsec() / 1000.0 < _noiseUntil) QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.015f, 0.03f, 0.033f));
        if (!_shown) { Scanlines(); return; }

        bool noisy = Time.GetTicksMsec() / 1000.0 < _noiseUntil;

        // 창 테두리 + 제목 표시줄(오른쪽 창과 같은 규격).
        var frame = new Rect2(96f, 44f, 608f, 512f);
        DrawRect(frame, new Color(0.06f, 0.20f, 0.24f, 0.72f));
        DrawRect(frame, Cyan with { A = 0.85f }, false, 2f);
        DrawRect(frame.Grow(4f), Cyan with { A = 0.25f }, false, 1f);

        var bar = new Rect2(frame.Position.X, frame.Position.Y, frame.Size.X, 28f);
        DrawRect(bar, new Color(0.10f, 0.32f, 0.36f, 0.92f));
        DrawRect(bar, Cyan with { A = 0.45f }, false, 1f);
        DrawString(_font, bar.Position + new Vector2(12f, 20f), "GUIDE-0.exe",
            HorizontalAlignment.Left, frame.Size.X - 100f, ViewFont.S(14), Cyan with { A = 0.95f });
        DrawString(_font, bar.Position + new Vector2(frame.Size.X - 84f, 20f), "—  □  ✕",
            HorizontalAlignment.Left, 80f, ViewFont.S(14), Cyan with { A = 0.55f });

        // 얼굴.
        DrawRect(FaceBox, new Color(0.03f, 0.12f, 0.15f, 0.92f));
        if (_texture != null)
        {
            var src = _texture.GetSize();
            if (src.X > 0f && src.Y > 0f)
            {
                float k = Mathf.Min(FaceBox.Size.X / src.X, FaceBox.Size.Y / src.Y);
                var dst = src * k;
                var at = FaceBox.Position + (FaceBox.Size - dst) * 0.5f
                         + new Vector2(0f, GuideMouthAnimator.BobOffset);
                // 흰색 도트 원본을 홀로그램 하늘색으로 물들여 그린다(+ 입 Overlay).
                GuideFacePaint.Draw(this, _texture, _mouthless, new Rect2(at, dst), Cyan);
            }
        }
        else
        {
            // 임시 초상 — 최종 도트 얼굴을 넣으면 이 블록은 그려지지 않는다.
            DrawString(_font, new Vector2(FaceBox.Position.X, FaceBox.Position.Y + FaceBox.Size.Y * 0.44f),
                "GUIDE-0", HorizontalAlignment.Center, FaceBox.Size.X, ViewFont.S(34), Cyan with { A = 0.9f });
            DrawString(_font, new Vector2(FaceBox.Position.X, FaceBox.Position.Y + FaceBox.Size.Y * 0.58f),
                "PORTRAIT", HorizontalAlignment.Center, FaceBox.Size.X, ViewFont.S(20), Cyan with { A = 0.55f });
            DrawString(_font, new Vector2(FaceBox.Position.X, FaceBox.Position.Y + FaceBox.Size.Y * 0.72f),
                $"[{_expression}]", HorizontalAlignment.Center, FaceBox.Size.X, ViewFont.S(15), Cyan with { A = 0.35f });
        }
        DrawRect(FaceBox, Cyan with { A = 0.9f }, false, 2f);

        DrawString(_font, new Vector2(frame.Position.X, 506f), "GUIDE-0",
            HorizontalAlignment.Center, frame.Size.X, ViewFont.S(22), Cyan);

        // 홀로그램 스캔라인 + 노이즈.
        for (float y = frame.Position.Y; y < frame.End.Y; y += 4f)
            DrawRect(new Rect2(frame.Position.X, y, frame.Size.X, 1f), new Color(0.55f, 0.95f, 1f, 0.05f));
        if (noisy)
        {
            var rng = new RandomNumberGenerator();
            for (int i = 0; i < 14; i++)
            {
                float y = rng.RandfRange(frame.Position.Y, frame.End.Y);
                DrawRect(new Rect2(frame.Position.X, y, frame.Size.X, rng.RandfRange(2f, 12f)),
                    new Color(0.8f, 1f, 1f, rng.RandfRange(0.06f, 0.26f)));
            }
        }
        Scanlines();
    }

    private void Scanlines()
    {
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.12f));
    }
}
