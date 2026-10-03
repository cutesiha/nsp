using Godot;
using NSP.Core;

namespace NSP.Ui;

public partial class CoreProgressHud : Control
{
    private ColorRect _background;
    private ColorRect _fill;
    private Label _labelWhite;
    private Control _clipBox;
    private Label _labelBlack;

    public override void _Ready()
    {
        _background = GetNode<ColorRect>("Background");
        _fill = GetNode<ColorRect>("Background/Fill");
        _labelWhite = GetNode<Label>("LabelWhite");
        _clipBox = GetNode<Control>("ClipBox");
        _labelBlack = GetNode<Label>("ClipBox/LabelBlack");
    }

    public override void _Process(double delta)
    {
        if (GameState.Instance == null) return;

        // 확정값이 아니라 "지금 차오르는 중"까지 포함해 보여준다 — 숫자가 계속 움직여야
        // 코어실에 사람을 더 넣은 효과가 즉시 눈에 보인다.
        float shown = NSP.Facility.FacilitySimulation.Instance?.CoreProgressPreview()
                      ?? GameState.Instance.CoreProgress;
        float progress = Mathf.Clamp(shown / 100f, 0f, 1f);
        float fillWidth = Size.X * progress;
        _fill.Size = new Vector2(fillWidth, _background.Size.Y);

        string text = $"봉쇄 코어 복구율: {shown:0.0}%";
        _labelWhite.Text = text;
        _labelBlack.Text = text;

        _clipBox.Position = Vector2.Zero;
        _clipBox.Size = new Vector2(fillWidth, Size.Y);
    }
}
