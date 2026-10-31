using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.View;

// LATE — 코어 미달 + 지목 정답. 당신이 전임자가 된다.
//
// 지목은 맞았다. 다만 늦었다. 그래서 TRUE 의 회상은 하지 않는다 — 이 엔딩의 핵심은
// "시간이 없다" 이고, 뒤를 돌아볼 여유가 없다.
//
// 2026-10-31 사용자 요청으로 BAD 와 같은 결로 바꿨다 — 조명 없이 꺼지는 컴퓨터, 얕게 깔리는 사이렌,
// 새빨간 방, 밖에서 두드리는 문, 저절로 열리는 문. 다른 것은 **끝**이다:
//   BAD  — 그것이 들이닥치고 관리자가 죽는다.
//   LATE — 아무것도 들어오지 않는다. 당신은 봉쇄 스위치를 직접 내렸고(프롤로그의 총괄 관리자처럼),
//          시설은 지상과 끊겼다. 불이 꺼진 어둠 속, 바로 옆에서 숨소리가 난다 — 같이 갇혔다.
public partial class EndingDirector
{
    private async Task LateEnd()
    {
        // ① 조회 — TRUE 와 같다. 어둠 속에서 모니터2 하나만 켜져 있다. 사이렌은 이미 얕게 돈다.
        await IdentityQueryFailed();
        QueryPush("▸ 격리 이송 완료", EndingMonitorView.Tone.Normal, 26);
        await QueryWait(0.4);
        for (int i = 0; i < 3; i++) { Sfx.Instance?.Play("alert_beep3", -4f); await Wait(0.45); }

        // ② 차폐 붕괴 — 카운트다운이 실시간으로 떨어진다. 방은 여전히 어둡다(화면빛뿐).
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);
        _ctl?.ClearFocus(0.9f);
        await Wait(0.6);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenTint(new Color(1f, 0.6f, 0.55f));
        _ctl?.SetScreenBrightness(1f);
        Sfx.Instance?.Play("crt_on", -8f);
        if (_m1 != null) _m1.LightColor = new Color(1f, 0.42f, 0.38f);
        if (_m2 != null) _m2.LightColor = new Color(1f, 0.42f, 0.38f);
        SetLights(0.45f, 0f);
        await Wait(0.8);

        left?.SetAlarm(true);
        right?.SetAlarm(true);
        left?.Clear("CONTAINMENT CORE", "FINAL RECOVERY SEQUENCE");
        left?.SetBar(_core, failed: true);
        left?.Push($"복구율 {_core:0.0}% - 목표 미달", EndingMonitorView.Tone.Bad, 24);
        right?.Clear("비상 차폐", "EMERGENCY SEAL");

        for (int t = 9; t >= 0; t--)
        {
            right?.ReplaceLast($"00:00:{t:00}", t <= 3 ? EndingMonitorView.Tone.Bad : EndingMonitorView.Tone.Normal);
            Sfx.Instance?.Play("tick", -18f);
            await Wait(1.0);
        }
        Sfx.Instance?.Play("power_down", -4f);
        _ctl?.SetScreenBrightnessFor("02", 0f);
        await Wait(0.8);

        // ③ 중앙제어실이 새빨갛게 켜진다 — 쾅 하고 한 번에. 그 다음에 GUIDE-0.
        Sfx.Instance?.Play("switch", -4f, 0.62f);
        Sfx.Instance?.Play("boom", -4f, 0.72f);
        BadRoomRed();
        _ctl?.ShakeCamera(1.2f, 0.4f);
        await Wait(1.2);

        // GUIDE-0 — sneer 가 아니라 normal. 유일하게 사무적이지 않은 순간이다.
        // 얼굴은 왼쪽(모니터1), 말은 오른쪽(모니터2). 같은 화면에 두면 얼굴창이 대사를 덮는다.
        Sfx.Instance?.Play("window_open", -10f);
        GuideCornerFace.SetAlarmTint(true);
        ShowGuide("normal");
        left?.Clear();
        right?.Clear();
        _ctl?.SetScreenBrightnessFor("02", 1f);
        await Wait(0.9);
        await GuideSpeakAt(right, 1.1f, "관리자님의 판단은 옳았습니다.");
        await Wait(1.2);
        await GuideSpeakAt(right, 1.1f,
            "다만, 시간이 부족했습니다.",
            "영구 봉쇄 절차를 개시합니다. 권한 확인이 필요합니다.");
        await Wait(0.8);

        // ④ 스위치 — 프롤로그에서 총괄 관리자가 눌렀던 그 자리다.
        var panel = _ctl?.GetNodeOrNull<Node3D>("ControlRoom/PowerSwitchPanel");
        if (panel != null) _ctl?.FocusProp(panel, 1.2f, 0.40f);
        await Wait(1.4);

        var lamp = BuildSealLamp(panel);
        if (lamp != null)
        {
            var blink = CreateTween().SetLoops(2);
            blink.TweenMethod(Callable.From<float>(v => lamp.EmissionEnergyMultiplier = v), 0.1f, 3.2f, 0.55);
            blink.TweenMethod(Callable.From<float>(v => lamp.EmissionEnergyMultiplier = v), 3.2f, 0.1f, 0.55);
        }
        await Wait(2.2);

        // 손이 올라가 스위치 앞에서 **1.5초 멈춘다.** 입력은 요구하지 않는다 —
        // 어차피 되돌릴 수 없다. 이 1.5초가 이 엔딩의 감정이다. 아무 소리도 넣지 마라.
        var arms = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
        var tip = panel?.GlobalPosition ?? Vector3.Zero;
        arms?.PlaySwitchFlip(false, tip + new Vector3(0f, 0.22f, 0.06f), null);
        await Wait(1.5);

        // 손이 내려간다 → 프롤로그의 그 소리.
        Sfx.Instance?.Play("switch", -2f);
        if (lamp != null) lamp.EmissionEnergyMultiplier = 0.1f;

        // 동시에 총괄 관리자의 마지막 컷이 흑백으로 한 번 스친다.
        FlashDirectorCut();
        _ctl?.SetScreenNoise(0.5f);
        await Wait(0.45);
        _ctl?.SetScreenNoise(0.02f);

        // 프롤로그와 **완전히 같은 양식**의 창. 숫자만 0 이다.
        ShowSealWindow("영구 봉쇄", "00:00:00", "PERMANENT SEAL ENGAGED");
        Sfx.Instance?.Play("boom", -3f);   // 흔들림은 넣지 않는다. 조용히 울리기만 한다.
        await Wait(3.0);
        HideSealWindow();
        HideGuide();
        Sfx.Instance?.Play("cctv_cut", -6f);
        _ctl?.SetScreenBrightness(0f);
        SetLights(0f, 0f);   // 화면빛이 사라지고 붉은 등만 남는다
        _ctl?.ClearFocus(1.0f);
        await Wait(1.2);

        // ⑤ 문 — 봉쇄는 걸렸다. 그런데 밖에서 무언가가 문을 친다 … 그리고 문이 열린다.
        await DoorKnockAndOpen();

        // ⑥ 아무것도 들어오지 않는다. 검은 문간만 본다.
        await Wait(2.6);

        // 붉은 등이 꺼진다 — 사이렌도 같이 끊긴다. 완전한 어둠.
        Sfx.Instance?.Play("power_down", -4f, 0.7f);
        BadLightsOff();
        Sfx.Instance?.StopLoop("siren");
        foreach (var k in new[] { "machinery_loop", "drone_loop", "vent_loop" }) Sfx.Instance?.StopLoop(k);
        _black.Color = _black.Color with { A = 1f };
        await Wait(2.2);

        // 어둠 속, 바로 옆에서 숨소리. 그것은 이미 들어와 있다 — 보여 주지 않는다.
        Sfx.Instance?.Play("breath_close", -3f, 0.78f);
        await Wait(2.6);
        await Banner("BAD END - 대상 확인, 복구 실패", new Color(0.85f, 0.80f, 0.70f));
        EndingState.Record(EndingState.Kind.Late);
    }


    // 전원 스위치 박스 위의 붉은 램프(영구 봉쇄 권한 확인).
    private StandardMaterial3D BuildSealLamp(Node3D panel)
    {
        if (panel == null) return null;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.02f, 0.02f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.18f, 0.12f),
            EmissionEnergyMultiplier = 0.1f,
        };
        panel.AddChild(new MeshInstance3D
        {
            Name = "EndingSealLamp",
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 8, Rings = 5 },
            Position = new Vector3(0f, 0.16f, 0.09f),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return mat;
    }

    // 프롤로그 컷(총괄 관리자의 마지막 모습)이 흑백으로 0.4초 스친다.
    private void FlashDirectorCut()
    {
        const string path = "res://assets/cutscene/prologue/disaster_13_director_last.png";
        if (!ResourceLoader.Exists(path)) return;
        var tex = GD.Load<Texture2D>(path);
        if (tex == null) return;
        var rect = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            // 흑백 — 색을 빼고 명도만 남긴다.
            Material = new CanvasItemMaterial(),
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shader = new ShaderMaterial { Shader = GrayShader() };
        rect.Material = shader;
        _layer.AddChild(rect);
        var t = CreateTween();
        t.TweenProperty(rect, "modulate:a", 0.85f, 0.12);
        t.TweenProperty(rect, "modulate:a", 0f, 0.28);
        t.TweenCallback(Callable.From(() => { if (IsInstanceValid(rect)) rect.QueueFree(); }));
    }

    private static Shader _gray;

    private static Shader GrayShader()
    {
        if (_gray != null) return _gray;
        _gray = new Shader
        {
            Code = "shader_type canvas_item;\n"
                 + "void fragment() {\n"
                 + "    vec4 c = texture(TEXTURE, UV);\n"
                 + "    float g = dot(c.rgb, vec3(0.299, 0.587, 0.114));\n"
                 + "    COLOR = vec4(vec3(g), c.a);\n"
                 + "}\n",
        };
        return _gray;
    }

    // ── 프롤로그와 같은 시설 시스템 창 ────────────────────────────────────
    private SealWindow _seal;

    private void ShowSealWindow(string title, string big, string sub)
    {
        _seal?.QueueFree();
        _seal = new SealWindow
        {
            Title = title, Big = big, Sub = sub,
            Size = new Vector2(520f, 180f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _seal.SetAnchorsPreset(Control.LayoutPreset.Center);
        _seal.AnchorLeft = 0.5f; _seal.AnchorRight = 0.5f;
        _seal.AnchorTop = 0.5f; _seal.AnchorBottom = 0.5f;
        _seal.OffsetLeft = -260f; _seal.OffsetRight = 260f;
        _seal.OffsetTop = -90f; _seal.OffsetBottom = 90f;
        _layer.AddChild(_seal);
        _seal.Reset();
        _sealT = 0;
    }

    private double _sealT;

    private void HideSealWindow()
    {
        _seal?.QueueFree();
        _seal = null;
    }

    // SealWindow 는 밖에서 흐른 시간을 받아 맥동한다(컷씬 플레이어와 같은 방식).
    private void TickSeal(double delta)
    {
        if (_seal == null || !IsInstanceValid(_seal)) return;
        _sealT += delta;
        _seal.Tick(_sealT);
    }
}
