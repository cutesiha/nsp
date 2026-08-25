using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.Ui;

public partial class LogPanel : Control
{
    public static LogPanel Instance { get; private set; }

    private const int MaxDisplayedEntries = 40;

    private RichTextLabel _output;

    public override void _Ready()
    {
        Instance = this;
        _output = GetNode<RichTextLabel>("Output");
        EventLog.Instance.EntryLogged += OnEntryLogged;
        Refresh();
    }

    public void Toggle() => Visible = !Visible;

    private void OnEntryLogged()
    {
        Refresh();
    }

    private void Refresh()
    {
        var entries = EventLog.Instance.GetAllEntries().TakeLast(MaxDisplayedEntries);
        _output.Text = string.Join("\n", entries.Select(FormatEntry));
        _output.ScrollToLine(_output.GetLineCount());
    }

    private static string FormatEntry(LogEntry e)
    {
        int totalSeconds = Mathf.RoundToInt(e.GameTimeSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00} {e.Description}";
    }
}
