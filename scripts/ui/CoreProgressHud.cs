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

        // 초보자가 가장 먼저 알아야 하는 것은 "왜 안 차는가" 다 — 숫자 옆에 바로 붙인다.
        string text = $"봉쇄 코어 복구율: {shown:0.0}%   {CoreStatusText()}";
        _labelWhite.Text = text;
        _labelBlack.Text = text;

        _clipBox.Position = Vector2.Zero;
        _clipBox.Size = new Vector2(fillWidth, Size.Y);
    }

    // 코어가 지금 차고 있는가, 아니면 왜 멈췄는가. 개발용 수치는 내보내지 않는다 —
    // 「중단 · 느림 · 복구 중 · 가속」 이 먼저 읽히고, 배율은 작게 덧붙기만 한다.
    private static string CoreStatusText()
    {
        var sim = NSP.Facility.FacilitySimulation.Instance;
        if (sim == null || GameState.Instance?.CurrentPhase != NSP.Data.GamePhase.Live) return "";

        string core = NSP.Facility.FacilitySimulation.CoreRoomIdPublic;

        if (sim.HasRepairPending(core)) return "· 복구 중단 — 코어실 사고";
        if (sim.IsRoomBlockedByMaterials(core)) return "· 복구 중단 — 자재 부족";

        int here = sim.OnDutyCount(core);
        if (here <= 0) return "· 복구 중단 — 코어실 인력 없음";

        float mult = NSP.Core.RecoveryProfile.CoreStaffMultiplier(here);
        if (mult <= 0f) return "· 복구 중단";
        if (mult < 1f) return $"· 복구 느림 ×{mult:0.00}";
        if (mult > 1.05f) return $"· 복구 가속 ×{mult:0.00}";
        return "· 복구 중";
    }
}
