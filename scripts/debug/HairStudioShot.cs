using System.Collections.Generic;
using Godot;
using NSP.Tools;

namespace NSP.Debug;

// 스튜디오를 **눈으로** 확인한다. 머리카락이 두피를 뚫거나 공중에 뜨거나 앞뒤가
// 뒤집히는 문제는 수치만 봐서는 안 잡힌다(지시서 §5).
//
//   godot --path . res://scenes/debug/HairStudioShot.tscn -- <저장폴더>
//
// 창 모드로만 돌아간다 — 헤드리스는 그림을 그리지 않는다.
public partial class HairStudioShot : Node
{
    // 지금 **그 캐릭터가 고른 머리카락**을, 그 캐릭터의 실제 모델 위에서 찍는다.
    // 헤어 id 는 저장본에서 읽는다 — 고른 것이 바뀌면 캡처도 따라 바뀐다.
    // Scale 0 = 자동 맞춤 그대로. 0 이 아니면 그 값으로 덮어써서 비교한다.
    private readonly (string Char, bool CloseUp, float Yaw, float Scale)[] _shots =
    {
        ("rabbit", true, 0f, 0f),
        ("rabbit", false, 18f, 0f),
        ("cat", true, 20f, 0f),
        ("fox", true, 0f, 0f),
        ("sheep", true, 20f, 0f),
        ("wolf", true, 0f, 0f),
        ("dog", true, 20f, 0f),
        ("admin", true, 0f, 0f),
    };

    private CharacterHairStudio _studio;
    private string _dir = "user://hairshot";
    private int _at = -1, _wait;
    private readonly List<string> _saved = new();

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length > 0) _dir = args[0];
        DirAccess.MakeDirRecursiveAbsolute(_dir);

        var packed = ResourceLoader.Load<PackedScene>("res://scenes/tools/CharacterHairStudio.tscn");
        _studio = packed.Instantiate<CharacterHairStudio>();
        AddChild(_studio);
        GD.Print($"################ 스튜디오 캡처 → {_dir}");
        _wait = 20;
    }

    public override void _Process(double delta)
    {
        if (_wait-- > 0) return;

        if (_at >= 0 && _at < _shots.Length) Grab(_at);

        _at++;
        if (_at >= _shots.Length)
        {
            GD.Print($"################ 캡처 끝 — {_saved.Count}장");
            foreach (string s in _saved) GD.Print("  " + s);
            GetTree().Quit();
            return;
        }

        (string ch, bool close, float yaw, float sc) = _shots[_at];
        HairAssignment a = _studio.Store.Get(ch);
        string body = a.Body;
        string hair = a.HairId != "" ? a.HairId : _studio.Catalog.Entries[0].Id;
        _studio.PickForTest(ch, body, hair);
        _studio.CloseUpForTest(close);
        // 게임 모델은 얼굴이 -Z 라 반대편에서 봐야 얼굴이 나온다.
        _studio.OrbitForTest(yaw + (_studio.UsesGameModel ? 180f : 0f), close ? -2f : -8f);
        if (sc > 0f) _studio.ScaleForTest(sc);
        GD.Print($"  {_at + 1}. {ch}/{body}/{hair} close={close} yaw={yaw} → " +
                 $"pos{_studio.CurrentPos.Snapped(Vector3.One * 0.001f)} " +
                 $"rot{_studio.CurrentRot.Snapped(Vector3.One * 0.1f)} scale {_studio.CurrentScale:F3}");
        _wait = 26;
    }

    private void Grab(int i)
    {
        Image img = GetViewport().GetTexture()?.GetImage();
        if (img == null) { GD.Print("  !! 화면을 읽지 못했다"); return; }
        (string ch, bool close, _, _) = _shots[i];
        string hair = _studio.Store.Get(ch).HairId;
        string p = $"{_dir}/{i + 1:00}_{ch}_{hair}{(close ? "_face" : "")}.png";
        Error e = img.SavePng(p);
        if (e == Error.Ok) _saved.Add(p);
        else GD.Print($"  !! 저장 실패 {p}: {e}");
    }
}
