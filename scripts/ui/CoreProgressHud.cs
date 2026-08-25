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

        float progress = Mathf.Clamp(GameState.Instance.CoreProgress / 100f, 0f, 1f);
        float fillWidth = Size.X * progress;
        _fill.Size = new Vector2(fillWidth, _background.Size.Y);

        string text = $"봉쇄 코어 복구율: {GameState.Instance.CoreProgress:0.0}%";
        _labelWhite.Text = text;
        _labelBlack.Text = text;

        _clipBox.Position = Vector2.Zero;
        _clipBox.Size = new Vector2(fillWidth, Size.Y);
    }
}
