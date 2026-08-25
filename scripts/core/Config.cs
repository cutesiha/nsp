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
        if (Data == null)
        {
            Data = ResourceLoader.Exists(DefaultConfigPath)
                ? GD.Load<ConfigData>(DefaultConfigPath)
                : new ConfigData();
        }
    }
}
