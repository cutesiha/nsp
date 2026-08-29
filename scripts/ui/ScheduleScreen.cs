using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Ui;

public partial class ScheduleScreen : Control
{
    // DAY1 권장 최소 구현 세트(NSP_DAY1_EVENTS.md) 검증 범위: TABOO-01만 활성화한다.
    private static readonly string[] DailyTabooIds = { "taboo_power_headcount_limit" };

    private const int StatBarMax = 10;

    private VBoxContainer _roomList;
    private VBoxContainer _tabooList;
    private HBoxContainer _employeeRoster;
    private Control _manualPage;
    private Control _tabooPage;
    private Button _startButton;
    private ConfirmationDialog _confirmDialog;
    private Font _tabooFont;

    private PanelContainer _employeeCard;
    private TextureRect _cardPortrait;
    private Label _cardName;
    private VBoxContainer _cardStats;
    private string _cardEmployeeId = "";

    public override void _Ready()
    {
        _roomList = GetNode<VBoxContainer>("Root/DocumentPanel/DocumentVBox/DocumentScroll/RoomListContainer");
        _tabooList = GetNode<VBoxContainer>("Root/NotebookPanel/NotebookStack/TabooPage/TabooListContainer");
        _employeeRoster = GetNode<HBoxContainer>("Root/DocumentPanel/DocumentVBox/EmployeeRoster");
        _manualPage = GetNode<Control>("Root/NotebookPanel/NotebookStack/ManualPage");
        _tabooPage = GetNode<Control>("Root/NotebookPanel/NotebookStack/TabooPage");
        _startButton = GetNode<Button>("StartButton");
        _confirmDialog = GetNode<ConfirmationDialog>("ConfirmDialog");
        _tabooFont = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf");

        _employeeCard = GetNode<PanelContainer>("Root/NotebookPanel/NotebookStack/EmployeeCard");
        _cardPortrait = GetNode<TextureRect>("Root/NotebookPanel/NotebookStack/EmployeeCard/CardVBox/Portrait");
        _cardName = GetNode<Label>("Root/NotebookPanel/NotebookStack/EmployeeCard/CardVBox/NameLabel");
        _cardStats = GetNode<VBoxContainer>("Root/NotebookPanel/NotebookStack/EmployeeCard/CardVBox/StatsBox");

        GetNode<Button>("Root/NotebookPanel/NotebookStack/ManualPage/ManualNextRow/ManualNextButton").Pressed += ShowTabooPage;
        GetNode<Button>("Root/NotebookPanel/NotebookStack/TabooPage/TabooPrevRow/TabooPrevButton").Pressed += ShowManualPage;

        _startButton.Pressed += OnStartPressed;
        _confirmDialog.Confirmed += ProceedToLive;
        _confirmDialog.GetOkButton().Text = "예";
        _confirmDialog.GetCancelButton().Text = "아니오";

        TabooRuleSystem.Instance.ActivateDailyTaboos(DailyTabooIds);
        GameState.Instance.SetPhase(GamePhase.Schedule);

        Refresh();
    }

    private void ShowTabooPage()
    {
        _manualPage.Visible = false;
        _tabooPage.Visible = true;
    }

    private void ShowManualPage()
    {
        _tabooPage.Visible = false;
        _manualPage.Visible = true;
    }

    private void OnStartPressed()
    {
        var sim = FacilitySimulation.Instance;
        bool allAssigned = sim.GetEmployeeIds().All(id => !string.IsNullOrEmpty(sim.GetEmployeeState(id)?.AssignedRoomId));

        if (allAssigned)
            ProceedToLive();
        else
            _confirmDialog.PopupCentered();
    }

    private void ProceedToLive()
    {
        GameState.Instance.SetPhase(GamePhase.Live);
        GetTree().ChangeSceneToFile("res://scenes/main/MainScene.tscn");
    }

    private void Refresh()
    {
        BuildTabooPage();
        BuildDocument();

        var sim = FacilitySimulation.Instance;
        bool anyAssigned = sim.GetEmployeeIds().Any(id => !string.IsNullOrEmpty(sim.GetEmployeeState(id)?.AssignedRoomId));
        _startButton.Visible = anyAssigned;
    }

    private void BuildTabooPage()
    {
        foreach (Node child in _tabooList.GetChildren())
            child.QueueFree();

        foreach (var taboo in TabooRuleSystem.Instance.GetActiveTaboos())
        {
            var label = new Label { Text = $"- {taboo.Description}" };
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.AddThemeFontOverride("font", _tabooFont);
            label.AddThemeFontSizeOverride("font_size", 22);
            _tabooList.AddChild(label);
        }
    }

    private void BuildDocument()
    {
        foreach (Node child in _roomList.GetChildren())
            child.QueueFree();
        foreach (Node child in _employeeRoster.GetChildren())
            child.QueueFree();

        var sim = FacilitySimulation.Instance;

        foreach (var roomId in sim.GetRoomIds())
        {
            var roomDef = sim.GetRoomDef(roomId);
            if (roomDef == null || roomDef.IsRestricted) continue;
            if (sim.GetRoomTasksInPriorityOrder(roomId).Count == 0) continue;

            var assignedHere = sim.GetEmployeeIds()
                .Select(id => sim.GetEmployeeState(id))
                .Where(s => s != null && s.AssignedRoomId == roomId)
                .Select(s => s.EmployeeId)
                .ToList();

            bool atRisk = TabooRuleSystem.Instance.IsRoomAtTabooRisk(roomId);

            var row = new VBoxContainer();
            var roomNameLabel = new Label { Text = $"{(atRisk ? "⚠ " : "")}{roomDef.DisplayName}" };
            roomNameLabel.AddThemeFontSizeOverride("font_size", 19);
            row.AddChild(roomNameLabel);

            var slotRow = new HBoxContainer();
            slotRow.AddThemeConstantOverride("separation", 12);

            for (int i = 0; i < 2; i++)
            {
                string assignedId = i < assignedHere.Count ? assignedHere[i] : "";
                var slot = new RoomSlot
                {
                    RoomId = roomId,
                    AssignedEmployeeId = assignedId,
                    CustomMinimumSize = new Vector2(148f, 52f),
                    Text = assignedId != "" ? sim.GetEmployeeDef(assignedId)?.Codename ?? assignedId : "( 빈 슬롯 )",
                    OnEmployeeDropped = (employeeId, targetRoomId) =>
                    {
                        FacilitySimulation.Instance.ClearAssignment(employeeId);
                        FacilitySimulation.Instance.AssignToRoom(employeeId, targetRoomId);
                        Refresh();
                    },
                };
                if (assignedId != "")
                {
                    string capturedAssignedId = assignedId;
                    slot.Pressed += () =>
                    {
                        FacilitySimulation.Instance.ClearAssignment(capturedAssignedId);
                        Refresh();
                    };
                }
                slot.AddThemeFontSizeOverride("font_size", 16);
                slotRow.AddChild(slot);
            }

            var fnLabel = new Label
            {
                Text = FunctionLabel(roomDef.ManagedResource),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            fnLabel.AddThemeFontSizeOverride("font_size", 16);
            slotRow.AddChild(fnLabel);

            row.AddChild(slotRow);
            _roomList.AddChild(row);
            _roomList.AddChild(new HSeparator());
        }

        foreach (var employeeId in sim.GetEmployeeIds())
        {
            var def = sim.GetEmployeeDef(employeeId);
            var state = sim.GetEmployeeState(employeeId);
            if (def == null || state == null || !string.IsNullOrEmpty(state.AssignedRoomId)) continue;

            var chip = new EmployeeChip
            {
                EmployeeId = employeeId,
                CustomMinimumSize = new Vector2(132f, 58f),
                Text = $"{def.Codename}\n기{def.Tech} 담{def.Courage} 관{def.Observation}",
            };
            chip.AddThemeFontSizeOverride("font_size", 16);
            chip.Pressed += () => ToggleEmployeeCard(employeeId);
            _employeeRoster.AddChild(chip);
        }
    }

    private void ToggleEmployeeCard(string employeeId)
    {
        if (_cardEmployeeId == employeeId)
        {
            _employeeCard.Visible = false;
            _cardEmployeeId = "";
            return;
        }

        var def = FacilitySimulation.Instance.GetEmployeeDef(employeeId);
        if (def == null) return;

        _cardEmployeeId = employeeId;
        _cardPortrait.Texture = def.FacePortrait;
        _cardName.Text = def.Codename;

        foreach (Node child in _cardStats.GetChildren())
            child.QueueFree();

        AddStatRow("기술", def.Tech);
        AddStatRow("담력", def.Courage);
        AddStatRow("관찰", def.Observation);

        _employeeCard.Visible = true;
    }

    private void AddStatRow(string label, int value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var nameLbl = new Label { Text = label, CustomMinimumSize = new Vector2(48, 0) };
        nameLbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(nameLbl);
        row.AddChild(new ProgressBar
        {
            MinValue = 0,
            MaxValue = StatBarMax,
            Value = value,
            ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 18),
        });
        var valLbl = new Label { Text = value.ToString(), CustomMinimumSize = new Vector2(22, 0) };
        valLbl.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(valLbl);
        _cardStats.AddChild(row);
    }

    private static string FunctionLabel(RoomResourceType type) => type switch
    {
        RoomResourceType.Power => "전력 공급원",
        RoomResourceType.Survival => "생존 유지",
        RoomResourceType.Materials => "자재 생산",
        RoomResourceType.Stress => "스트레스 회복",
        RoomResourceType.Surveillance => "감시·봉쇄 강화",
        RoomResourceType.CoreRepair => "코어 직접 수리",
        RoomResourceType.Storage => "자재 저장 상한 관리",
        RoomResourceType.Isolation => "격리 수용 (배치 불가)",
        _ => "",
    };
}
