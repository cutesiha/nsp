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
        _titleLabel.Text = $"{roomDef?.DisplayName ?? _roomId} — 발생 업무";

        foreach (Node child in _list.GetChildren())
            child.QueueFree();

        var tasks = sim.GetActiveTasksForRoom(_roomId);
        if (tasks.Count == 0)
        {
            _list.AddChild(new Label { Text = "현재 이 구역에 발생한 업무가 없습니다." });
            return;
        }

        foreach (var st in tasks)
        {
            var task = sim.GetTaskDef(st.TaskId);

            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", RowStyle());

            var rowBox = new VBoxContainer();
            rowBox.AddThemeConstantOverride("separation", 2);

            var topRow = new HBoxContainer();
            topRow.AddThemeConstantOverride("separation", 8);
            topRow.AddChild(new Label
            {
                Text = task?.DisplayName ?? st.TaskId,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            topRow.AddChild(new Label { Text = StatusText(st) });
            rowBox.AddChild(topRow);

            rowBox.AddChild(new ProgressBar
            {
                MinValue = 0,
                MaxValue = st.GaugeRequired <= 0f ? 1f : st.GaugeRequired,
                Value = st.Gauge,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 14),
            });

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

    private static string StatusText(SpawnedTask st) => st.Status switch
    {
        SpawnedTaskStatus.Completed => "✓ 완료",
        SpawnedTaskStatus.Failed => "🚨 실패",
        _ => st.Recurring ? "상시" : $"⏱ {Mathf.CeilToInt(st.Remaining)}초",
    };

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

    private static string EffectDescription(NSP.Data.TaskDef task) => task == null ? "" : task.EffectType switch
    {
        NSP.Data.TaskEffectType.AddCoreProgress => $"완료 시 코어 진행도 +{task.EffectAmount:0}%, 자재 소모",
        NSP.Data.TaskEffectType.AddMaterials => $"완료 시 공용 자재 +{task.EffectAmount:0}",
        NSP.Data.TaskEffectType.ReduceStress => $"완료 시 이 방 인원 스트레스 -{task.EffectAmount:0}",
        NSP.Data.TaskEffectType.BoostPowerCapacity => "완료 시 발전 사고 상태라면 최대 전력 정상 복구",
        _ => "",
    };
}
