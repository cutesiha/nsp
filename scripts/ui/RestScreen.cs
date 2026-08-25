using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

public partial class RestScreen : Control
{
    private HBoxContainer _employeeRow;
    private TextureRect _portrait;
    private Label _speakerLabel;
    private Label _messageLabel;
    private Button _isolateButton;
    private string _selectedEmployeeId = "";

    public override void _Ready()
    {
        GameState.Instance.SetPhase(GamePhase.Rest);

        _employeeRow = GetNode<HBoxContainer>("Root/EmployeeRow");
        _portrait = GetNode<TextureRect>("Root/DialogueBox/Portrait");
        _speakerLabel = GetNode<Label>("Root/DialogueBox/TextColumn/SpeakerLabel");
        _messageLabel = GetNode<Label>("Root/DialogueBox/TextColumn/MessageLabel");
        _isolateButton = GetNode<Button>("Root/DialogueBox/TextColumn/IsolateButton");
        _isolateButton.Pressed += OnIsolatePressed;
        _isolateButton.Disabled = true;

        GetNode<Button>("Root/ResultButton").Pressed += OnResultPressed;

        BuildEmployeeRow();
    }

    private void BuildEmployeeRow()
    {
        foreach (Node child in _employeeRow.GetChildren())
            child.QueueFree();

        foreach (var employeeId in FacilitySimulation.Instance.GetEmployeeIds())
        {
            var def = FacilitySimulation.Instance.GetEmployeeDef(employeeId);
            var state = FacilitySimulation.Instance.GetEmployeeState(employeeId);
            if (def == null || state == null) continue;

            string suffix = state.Isolated ? " [격리됨]" : state.Alive ? "" : " [사망]";
            var button = new Button { Text = def.Codename + suffix };
            string capturedId = employeeId;
            button.Pressed += () => OnEmployeeSelected(capturedId);
            _employeeRow.AddChild(button);
        }
    }

    private void OnEmployeeSelected(string employeeId)
    {
        var def = FacilitySimulation.Instance.GetEmployeeDef(employeeId);
        var state = FacilitySimulation.Instance.GetEmployeeState(employeeId);
        if (def == null || state == null) return;

        _selectedEmployeeId = employeeId;
        _portrait.Texture = def.StandingImage;

        if (!state.Alive)
        {
            _speakerLabel.Text = def.Codename;
            _messageLabel.Text = "(응답 없음)";
            _isolateButton.Disabled = true;
            return;
        }

        _speakerLabel.Text = def.Codename;
        _messageLabel.Text = $"\"{def.PersonalityLine1}\"\n\n(실제 대화는 PHASE 10에서 Claude API로 연결됩니다. 지금은 성격 설정만 미리보기로 표시합니다.)";
        _isolateButton.Disabled = state.Isolated;
    }

    private void OnIsolatePressed()
    {
        if (string.IsNullOrEmpty(_selectedEmployeeId)) return;

        bool ok = FacilitySimulation.Instance.IsolateEmployee(_selectedEmployeeId);
        if (ok)
        {
            _messageLabel.Text += "\n\n(격리되었습니다.)";
            _isolateButton.Disabled = true;
            BuildEmployeeRow();
        }
        else
        {
            _messageLabel.Text += "\n\n(격리 인원이 이미 가득 찼습니다.)";
        }
    }

    private void OnResultPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/result/ResultScreen.tscn");
    }
}
