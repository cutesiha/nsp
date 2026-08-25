using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class TaskPriorityPopup : Control
{
    public static TaskPriorityPopup Instance { get; private set; }

    private Label _titleLabel;
    private VBoxContainer _list;
    private Button _closeButton;

    private string _roomId = "";

    public override void _Ready()
    {
        Instance = this;
        Visible = false;

        _titleLabel = GetNode<Label>("Panel/VBox/TitleRow/TitleLabel");
        _list = GetNode<VBoxContainer>("Panel/VBox/List");
        _closeButton = GetNode<Button>("Panel/VBox/TitleRow/CloseButton");

        _closeButton.Pressed += ClosePopup;
    }

    public void Show(string roomId)
    {
        _roomId = roomId;
        Visible = true;
        Rebuild();
    }

    private void ClosePopup()
    {
        Visible = false;
        _roomId = "";
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Rebuild();
    }

    private void Rebuild()
    {
        if (string.IsNullOrEmpty(_roomId)) return;

        var sim = FacilitySimulation.Instance;
        var roomDef = sim.GetRoomDef(_roomId);
        _titleLabel.Text = $"{roomDef?.DisplayName ?? _roomId} — 업무 우선순위";

        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        var tasks = sim.GetRoomTasksInPriorityOrder(_roomId);
        for (int i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            bool isActive = i == 0;
            float gauge = sim.GetTaskGauge(_roomId, task.TaskId);

            var row = new TaskRow { TaskId = task.TaskId };
            row.AddThemeStyleboxOverride("panel", RowStyle());
            row.OnDroppedOnto = (droppedId, targetId) =>
            {
                int targetIndex = sim.GetRoomTasksInPriorityOrder(_roomId).FindIndex(t => t.TaskId == targetId);
                if (targetIndex >= 0)
                    sim.MoveTaskToIndex(_roomId, droppedId, targetIndex);
                Rebuild();
            };

            var rowBox = new VBoxContainer();
            rowBox.AddThemeConstantOverride("separation", 2);

            var topRow = new HBoxContainer();
            topRow.AddThemeConstantOverride("separation", 8);
            topRow.AddChild(new Label { Text = "≡", CustomMinimumSize = new Vector2(16, 0) });
            topRow.AddChild(new Label
            {
                Text = task.DisplayName,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            if (isActive)
                topRow.AddChild(new Label { Text = $"작업중 {gauge / task.GaugeRequired * 100f:0}%" });

            var upButton = new Button { Text = "▲", Disabled = i == 0, CustomMinimumSize = new Vector2(28, 0) };
            string capturedId = task.TaskId;
            upButton.Pressed += () => { sim.ReorderRoomTask(_roomId, capturedId, true); Rebuild(); };
            topRow.AddChild(upButton);

            var downButton = new Button { Text = "▼", Disabled = i == tasks.Count - 1, CustomMinimumSize = new Vector2(28, 0) };
            downButton.Pressed += () => { sim.ReorderRoomTask(_roomId, capturedId, false); Rebuild(); };
            topRow.AddChild(downButton);

            rowBox.AddChild(topRow);

            var gaugeBar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = task.GaugeRequired,
                Value = gauge,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 14),
            };
            rowBox.AddChild(gaugeBar);

            string effect = EffectDescription(task);
            if (!string.IsNullOrEmpty(effect))
            {
                var effectLabel = new Label { Text = effect };
                effectLabel.AddThemeFontSizeOverride("font_size", 12);
                rowBox.AddChild(effectLabel);
            }

            row.AddChild(rowBox);
            _list.AddChild(row);
        }
    }

    private static StyleBoxFlat RowStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.99f, 0.98f, 0.94f, 1f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            BorderColor = new Color(0.3f, 0.3f, 0.28f, 1f),
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 8, ContentMarginTop = 6, ContentMarginRight = 8, ContentMarginBottom = 6,
        };
    }

    private static string EffectDescription(NSP.Data.TaskDef task) => task.EffectType switch
    {
        NSP.Data.TaskEffectType.AddCoreProgress => $"완료 시 코어 진행도 +{task.EffectAmount:0}%, 자재 소모",
        NSP.Data.TaskEffectType.AddMaterials => $"완료 시 공용 자재 +{task.EffectAmount:0}",
        NSP.Data.TaskEffectType.ReduceStress => $"완료 시 이 방 인원 스트레스 -{task.EffectAmount:0}",
        NSP.Data.TaskEffectType.BoostPowerCapacity => "완료 시 발전 사고 상태라면 최대 전력 정상 복구",
        _ => "",
    };
}
