using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class EmployeeDetailCard : PanelContainer
{
    public static EmployeeDetailCard Instance { get; private set; }

    private TextureRect _portrait;
    private Label _nameLabel;
    private Label _statsLabel;
    private Button _callButton;
    private Button _relocateButton;
    private Button _taskPriorityButton;
    private Button _isolateButton;

    private string _employeeId = "";

    public override void _Ready()
    {
        Instance = this;
        Visible = false;

        _portrait = GetNode<TextureRect>("VBox/InfoRow/Portrait");
        _nameLabel = GetNode<Label>("VBox/InfoRow/InfoVBox/NameLabel");
        _statsLabel = GetNode<Label>("VBox/InfoRow/InfoVBox/StatsLabel");
        _callButton = GetNode<Button>("VBox/ButtonGrid/CallButton");
        _relocateButton = GetNode<Button>("VBox/ButtonGrid/RelocateButton");
        _taskPriorityButton = GetNode<Button>("VBox/ButtonGrid/TaskPriorityButton");
        _isolateButton = GetNode<Button>("VBox/ButtonGrid/IsolateButton");

        DangerButtonStyle.Apply(_isolateButton);

        _callButton.Pressed += OnCallPressed;
        _relocateButton.Pressed += OnRelocatePressed;
        _taskPriorityButton.Pressed += OnTaskPriorityPressed;
        _isolateButton.Pressed += OnIsolatePressed;
    }

    public void Show(string employeeId)
    {
        _employeeId = employeeId;
        Visible = true;
        Refresh();
    }

    public void HideCard()
    {
        Visible = false;
        _employeeId = "";
    }

    public void Refresh()
    {
        if (!Visible || string.IsNullOrEmpty(_employeeId)) return;

        var sim = FacilitySimulation.Instance;
        var def = sim.GetEmployeeDef(_employeeId);
        var state = sim.GetEmployeeState(_employeeId);
        if (def == null || state == null) { HideCard(); return; }

        _portrait.Texture = def.FacePortrait;
        _nameLabel.Text = def.Codename;
        _statsLabel.Text = $"기술: {def.Tech}\n담력: {def.Courage}\n관찰: {def.Observation}\n스트레스: {state.Stress:0}";

        bool actionable = state.Alive && !state.Isolated;
        _callButton.Disabled = !actionable;
        _relocateButton.Disabled = !actionable;
        _taskPriorityButton.Disabled = !actionable;

        _relocateButton.Text = sim.RelocatingEmployeeId == _employeeId ? "재배치 취소" : "재배치";
        _isolateButton.Text = state.Isolated ? "격리 취소" : "격리";
        _isolateButton.Disabled = !state.Alive;
    }

    public override void _Process(double delta)
    {
        if (Visible)
            Refresh();
    }

    private void OnCallPressed()
    {
        string id = _employeeId;
        if (string.IsNullOrEmpty(id) || Phone.Instance == null) return;

        var icon = EmployeeMapIcon.Registry.GetValueOrDefault(id);
        Phone.Instance.Connect(Phone.SignalName.RingFinished,
            Callable.From(() => CallBubble.Instance?.StartCall(id, icon)),
            (uint)ConnectFlags.OneShot);
        Phone.Instance.Ring();
    }

    private void OnRelocatePressed()
    {
        var sim = FacilitySimulation.Instance;
        if (sim.RelocatingEmployeeId == _employeeId)
            sim.CancelRelocating();
        else
            sim.StartRelocating(_employeeId);
        Refresh();
    }

    private void OnTaskPriorityPressed()
    {
        var state = FacilitySimulation.Instance.GetEmployeeState(_employeeId);
        if (state == null || string.IsNullOrEmpty(state.CurrentRoomId)) return;
        TaskPriorityPopup.Instance?.Show(state.CurrentRoomId);
    }

    private void OnIsolatePressed()
    {
        var sim = FacilitySimulation.Instance;
        var state = sim.GetEmployeeState(_employeeId);
        if (state == null) return;

        if (state.Isolated)
            sim.CancelIsolation(_employeeId);
        else
            sim.IsolateEmployee(_employeeId);

        Refresh();
    }
}
