using Godot;
using NSP.Data;

namespace NSP.Core;

public partial class Config : Node
{
    public static Config Instance { get; private set; }

    [Export] public ConfigData Data;

    private const string DefaultConfigPath = "res://data/config.tres";

    public override void _EnterTree()
    {
        Instance = this;
        // 대본의 표정 기호(☺︎ ☹︎)가 컬러 이모지로 바뀌지 않게 — 어떤 글자보다 먼저 건다.
        NSP.View.ViewFont.InstallSymbolFallback();
        if (Data == null)
        {
            Data = ResourceLoader.Exists(DefaultConfigPath)
                ? GD.Load<ConfigData>(DefaultConfigPath)
                : new ConfigData();
        }
    }
}
