using System.Collections.Generic;
using Godot;
using NSP.View;

namespace NSP.Debug;

// CCTV 직원 캐릭터 작업용 미리보기. 게임 로직과는 완전히 분리되어 있다.
// 씬 안의 6명은 실제 variant 씬(FoxEmployee3D.tscn 등)을 instance 한 것이므로,
// 그 씬을 고치면 여기 보이는 모습도 같이 바뀐다.
//
//   1~8      애니메이션 전환 (6명 동시)
//   Q/E      재생 속도 -/+      ·  W  속도 1.0 으로
//   SPACE    일시정지/재개      ·  R  현재 애니메이션 처음부터
//   Z/X/C    전신 / CCTV 거리 / 가까이 시점
//   L        이름표 켜기/끄기
//   G        괴물 반응 — 6명이 각자 자기 반응(첫 반응 → 반복)을 동시에 한다 · H 회복
public partial class EmployeeCctvPreview : Node3D
{
    // 공용 라이브러리(employee_common.tres)에 실제로 들어 있는 애니메이션 전부.
    // 앞의 여덟 개는 숫자키 1~8 로도 고를 수 있고, 나머지는 버튼으로 고른다.
    public static readonly string[] Clips =
    {
        "idle", "walk", "talk", "work", "repair", "inspect", "suspicious", "handoff",
        "sit_typing", "sit_assemble", "hammer_work", "lying_idle",
        "carry_box_normal", "carry_box_heavy", "pickup_box", "place_box",
        "isolated_struggle", "isolated_exhausted",
    };

    [Export] public NodePath FullBodyCameraPath = "FullBodyCamera";
    [Export] public NodePath CctvCameraPath = "CctvCamera";
    [Export] public NodePath CloseCameraPath = "CloseCamera";

    private Camera3D _full, _cctv, _close;
    private readonly List<AnimationPlayer> _anims = new();
    private readonly List<Label3D> _labels = new();
    private readonly List<Button> _buttons = new();
    private Label _hint;
    private int _clip;
    private float _speed = 1f;
    private bool _paused;

    public override void _Ready()
    {
        _full = GetNodeOrNull<Camera3D>(FullBodyCameraPath);
        _cctv = GetNodeOrNull<Camera3D>(CctvCameraPath);
        _close = GetNodeOrNull<Camera3D>(CloseCameraPath);
        if (_full != null) _full.Current = true;

        Collect(this);
        foreach (var a in _anims) a.PlaybackDefaultBlendTime = 0.15;

        BuildUi();
        Play(0);
    }

    private void Collect(Node n)
    {
        if (n is AnimationPlayer a) _anims.Add(a);
        if (n is Label3D l) _labels.Add(l);
        foreach (var c in n.GetChildren()) Collect(c);
    }

    // --- 재생 ---------------------------------------------------------------

    // ── 괴물 반응(각자) ──────────────────────────────────────────────
    // 게임과 같은 GhostReactionProfiles 표를 읽어 첫 반응 → 반복 → 회복 클립을 잇는다.
    // 위치 이동(숨기·반 걸음)은 게임의 RoomWorkVisualController 몫이라 여기서는 제자리다.
    private float _ghostT = -1f;
    private bool _ghostRecover;

    private static string IdOf(AnimationPlayer a)
    {
        string path = (a.GetParent()?.SceneFilePath ?? "").ToLowerInvariant();
        foreach (var id in new[] { "fox", "dog", "cat", "sheep", "rabbit", "wolf" })
            if (path.Contains(id)) return id;
        return "";
    }

    private void PlayGhost(bool recover)
    {
        _ghostT = 0f;
        _ghostRecover = recover;
        _paused = false;
        foreach (var a in _anims)
        {
            var prof = GhostReactionProfiles.Get(GhostReactionProfiles.ActionFor(IdOf(a)));
            if (prof == null) continue;
            a.SpeedScale = _speed;
            if (prof.KeepsWorking) { a.Play("work"); continue; }
            if (recover) a.Play(prof.RecoverClip, 0.2);
            else a.Play(prof.IntroClip, prof.IntroBlend);
            a.Seek(0, true);
        }
        _hint.Text = recover ? "[괴물 반응 — 회복]" : "[괴물 반응 — 각자]  G 다시  H 회복\n"
            + "양 주저앉음 · 토끼 비명 · 고양이 숨기 · 강아지 숨기 · 늑대 대치 · 여우 계속 일함(고개만)";
    }

    public override void _Process(double delta)
    {
        if (_ghostT < 0f || _paused) return;
        _ghostT += (float)delta * _speed;
        foreach (var a in _anims)
        {
            var prof = GhostReactionProfiles.Get(GhostReactionProfiles.ActionFor(IdOf(a)));
            if (prof == null) continue;
            if (prof.KeepsWorking)
            {
                // 여우 — 손을 0.3초 멈추고 고개만 돌렸다가, 2초마다 한 번씩 다시 본다.
                var neck = a.GetParent().GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/Neck");
                float t = _ghostT;
                bool look = !_ghostRecover && (t < 0.6f || t % 2.0f is > 1.2f and < 1.6f);
                a.SpeedScale = !_ghostRecover && t is > 0.03f and < 0.33f ? 0f : _speed;
                if (neck != null)
                    neck.Rotation = neck.Rotation with { Y = Mathf.Lerp(neck.Rotation.Y, look ? 0.9f : 0f, 0.25f) };
                continue;
            }
            if (!_ghostRecover && _ghostT >= prof.IntroSeconds && a.CurrentAnimation != prof.LoopClip)
                a.Play(prof.LoopClip, 0.14);
        }
    }

    private void Play(int index)
    {
        _ghostT = -1f;
        _clip = Mathf.Clamp(index, 0, Clips.Length - 1);
        _paused = false;
        foreach (var a in _anims)
        {
            a.SpeedScale = _speed;
            a.Play(Clips[_clip]);   // 블렌드는 PlaybackDefaultBlendTime 이 맡는다
        }
        Refresh();
    }

    private void Restart()
    {
        foreach (var a in _anims) { a.Seek(0, true); a.Play(Clips[_clip]); }
        _paused = false;
        Refresh();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        foreach (var a in _anims)
        {
            if (_paused) a.Pause();
            else a.Play(Clips[_clip]);
        }
        Refresh();
    }

    private void SetSpeed(float s)
    {
        _speed = Mathf.Clamp(s, 0.1f, 3f);
        foreach (var a in _anims) a.SpeedScale = _speed;
        Refresh();
    }

    // --- 입력 ---------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key8) { Play((int)(k.Keycode - Key.Key1)); return; }
        switch (k.Keycode)
        {
            case Key.Q: SetSpeed(_speed - 0.1f); break;
            case Key.E: SetSpeed(_speed + 0.1f); break;
            case Key.W: SetSpeed(1f); break;
            case Key.Space: TogglePause(); break;
            case Key.R: Restart(); break;
            case Key.Z: if (_full != null) _full.Current = true; break;
            case Key.X: if (_cctv != null) _cctv.Current = true; break;
            case Key.C: if (_close != null) _close.Current = true; break;
            case Key.L: foreach (var l in _labels) l.Visible = !l.Visible; break;
            case Key.G: PlayGhost(false); break;
            case Key.H: PlayGhost(true); break;
            case Key.F10: GetTree().ChangeSceneToFile("res://scenes/debug/DeveloperHub.tscn"); break;
        }
    }

    // --- UI -----------------------------------------------------------------

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        // 18개라 한 줄에 다 넣으면 화면을 넘는다 — 9개씩 두 줄.
        var row = new HBoxContainer { Position = new Vector2(20, 64) };
        row.AddThemeConstantOverride("separation", 6);
        layer.AddChild(row);
        var rowB = new HBoxContainer { Position = new Vector2(20, 102) };
        rowB.AddThemeConstantOverride("separation", 6);
        layer.AddChild(rowB);
        for (int i = 0; i < Clips.Length; i++)
        {
            int idx = i;
            string label = i < 8 ? $"{i + 1}. {Clips[i]}" : Clips[i];
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(112, 34) };
            b.Pressed += () => Play(idx);
            (i < 9 ? row : rowB).AddChild(b);
            _buttons.Add(b);
        }

        var row2 = new HBoxContainer { Position = new Vector2(20, 142) };
        row2.AddThemeConstantOverride("separation", 6);
        layer.AddChild(row2);
        foreach (var (text, act) in new (string, System.Action)[]
                 {
                     ("속도 -", () => SetSpeed(_speed - 0.1f)),
                     ("속도 +", () => SetSpeed(_speed + 0.1f)),
                     ("1.0x", () => SetSpeed(1f)),
                     ("일시정지", TogglePause),
                     ("처음부터", Restart),
                 })
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(88, 32) };
            b.Pressed += act;
            row2.AddChild(b);
        }

        // Developer Hub 로 복귀.
        var ghostBtn = new Button { Text = "괴물 반응(각자) G", Position = new Vector2(200, 180) };
        ghostBtn.Pressed += () => PlayGhost(false);
        layer.AddChild(ghostBtn);
        var recoverBtn = new Button { Text = "회복 H", Position = new Vector2(360, 180) };
        recoverBtn.Pressed += () => PlayGhost(true);
        layer.AddChild(recoverBtn);

        var back = new Button { Text = "◀ DEV HUB (F10)", Position = new Vector2(20, 180) };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/debug/DeveloperHub.tscn");
        layer.AddChild(back);

        _hint = new Label { Position = new Vector2(20, 16) };
        _hint.AddThemeFontSizeOverride("font_size", 17);
        _hint.AddThemeColorOverride("font_color", new Color(0.9f, 0.93f, 0.96f));
        layer.AddChild(_hint);
    }

    private void Refresh()
    {
        for (int i = 0; i < _buttons.Count; i++)
            _buttons[i].Modulate = i == _clip ? new Color(1f, 0.85f, 0.4f) : Colors.White;
        _hint.Text =
            $"[{Clips[_clip]}]  속도 {_speed:0.0}x  {(_paused ? "· 일시정지" : "")}\n"
            + "1~8 애니메이션   Q/E 속도   W 1.0x   SPACE 일시정지   R 처음부터   Z/X/C 시점   L 이름표\n"
            + "왼쪽부터  여우 · 강아지 · 고양이 · 양 · 토끼 · 늑대";
    }
}
