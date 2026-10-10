using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class RoomDetailCard : PanelContainer
{
    public static RoomDetailCard Instance { get; private set; }

    private Label _nameLabel;
    private Label _descLabel;
    private Button _taskListButton;
    private Button _lockButton;

    private string _roomId = "";

    public override void _Ready()
    {
        Instance = this;
        Visible = false;

        _nameLabel = GetNode<Label>("VBox/NameLabel");
        _descLabel = GetNode<Label>("VBox/DescPanel/DescLabel");
        _taskListButton = GetNode<Button>("VBox/ButtonRow/TaskListButton");
        _lockButton = GetNode<Button>("VBox/ButtonRow/LockButton");

        DangerButtonStyle.Apply(_lockButton);

        _taskListButton.Pressed += () => TaskPriorityPopup.Instance?.Show(_roomId);
        _lockButton.Pressed += OnLockPressed;
    }

    public void Show(string roomId)
    {
        _roomId = roomId;
        Visible = true;
        Refresh();
    }

    public void HideCard()
    {
        Visible = false;
        _roomId = "";
    }

    private void Refresh()
    {
        if (!Visible || string.IsNullOrEmpty(_roomId)) return;

        var sim = FacilitySimulation.Instance;
        var def = sim.GetRoomDef(_roomId);
        var state = sim.GetRoomState(_roomId);
        if (def == null || state == null) { HideCard(); return; }

        _nameLabel.Text = def.DisplayName;

        if (def.IsRestricted)
        {
            _descLabel.Text = "출입이 제한된 구역입니다. 배치·업무 조정 대상이 아닙니다.";
            _taskListButton.Visible = false;
            _lockButton.Visible = false;
            return;
        }

        _taskListButton.Visible = true;
        _lockButton.Visible = true;

        // 방 설명은 data/rooms/*.tres 한 곳에만 있다(RoomEffectText 창구로 읽는다).
        string desc = RoomEffectText.Summary(_roomId);
        string brief = string.Join("\n", RoomEffectText.Brief(_roomId));
        if (brief.Length > 0) desc = desc.Length > 0 ? desc + "\n" + brief : brief;
        // 능력치가 잠긴 날에는 "요구 능력" 줄을 싣지 않는다.
        string statsLine = "";
        if (NSP.Core.DayFeatures.StatsEnabled)
        {
            var requiredStats = sim.GetRoomTasksInPriorityOrder(_roomId)
                .Select(t => t.RequiredStat)
                .Distinct()
                .Select(StatLabel);
            statsLine = $"요구 능력: {string.Join(", ", requiredStats)}\n";
        }
        _descLabel.Text = $"{desc}\n{statsLine}인원: {sim.GetAssignedCount(_roomId)}/{NSP.Facility.FacilitySimulation.RoomSlotCapacity}";

        _lockButton.Text = state.Locked ? "봉쇄 해제" : "구역 봉쇄";
    }

    private void OnLockPressed()
    {
        var sim = FacilitySimulation.Instance;
        var state = sim.GetRoomState(_roomId);
        if (state == null) return;

        sim.SetRoomLocked(_roomId, !state.Locked);
        Refresh();
    }

    private static string StatLabel(NSP.Data.StatType stat) => stat switch
    {
        NSP.Data.StatType.Tech => "기술",
        NSP.Data.StatType.Courage => "담력",
        NSP.Data.StatType.Observation => "관찰",
        _ => stat.ToString(),
    };
}
