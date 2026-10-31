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
        // 창 제목. 프로젝트 이름(config/name = "NSP")은 저장 폴더(app_userdata/NSP) 이름이기도 해서
        // 바꾸면 설정 · 기록이 새 폴더로 갈라진다 — 그래서 이름은 두고 창 제목만 게임 제목으로 건다.
        DisplayServer.WindowSetTitle("야간근무지침");
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
