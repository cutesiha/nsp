using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.Ui;

public partial class LogPanel : Control
{
    private const int MaxDisplayedEntries = 40;

    private RichTextLabel _output;

    public override void _Ready()
    {
        _output = GetNode<RichTextLabel>("Output");
        EventLog.Instance.EntryLogged += OnEntryLogged;
        Refresh();
    }

    private void OnEntryLogged()
    {
        Refresh();
    }

    private void Refresh()
    {
        var entries = EventLog.Instance.GetAllEntries().TakeLast(MaxDisplayedEntries);
        _output.Text = string.Join("\n", entries.Select(e => $"[DAY{e.Day} {e.GameTimeSeconds:0}s] {e.Description}"));
        _output.ScrollToLine(_output.GetLineCount());
    }
}
