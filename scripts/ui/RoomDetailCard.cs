using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class RoomDetailCard : PanelContainer
{
    public static RoomDetailCard Instance { get; private set; }

    private static readonly System.Collections.Generic.Dictionary<string, string> Descriptions = new()
    {
        ["power_room"] = "시설 전력망의 주 공급원입니다. 이 방의 업무 처리량이 전체 전력 여유를 좌우합니다.",
        ["vent_room"] = "환기 및 생존 유지 설비를 관리합니다. 방치 시 전 직원 스트레스가 상승합니다.",
        ["maintenance_room"] = "자재를 생산하고 설비 고장을 수리합니다. 코어 수리에 쓸 자재를 여기서 확보해야 합니다.",
        ["medical_room"] = "직원의 스트레스를 회복시키는 유일한 공간입니다.",
        ["guard_room"] = "CCTV 감시와 구역 봉쇄 효율을 담당합니다. 인력 배치 시 사보타주 확률이 감소합니다.",
        ["core_room"] = "봉쇄 코어를 직접 수리합니다. 자재를 소모하며 진행도를 높입니다.",
        ["storage_room"] = "공용 자재 풀의 저장 상한을 관리합니다.",
        ["isolation_room"] = "격리된 직원이 수용되는 공간입니다. 직접 배치할 수 없습니다.",
    };

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

        var requiredStats = sim.GetRoomTasksInPriorityOrder(_roomId)
            .Select(t => t.RequiredStat)
            .Distinct()
            .Select(StatLabel);
        string statsLine = string.Join(", ", requiredStats);

        string desc = Descriptions.GetValueOrDefault(_roomId, "");
        _descLabel.Text = $"{desc}\n요구 능력: {statsLine}\n인원: {sim.GetAssignedCount(_roomId)}/2";

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
