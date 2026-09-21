using System;
using System.Collections.Generic;
using Godot;

namespace NSP.Prologue;

// docs/NSP_PROLOGUE_RUNTIME.md 를 읽어 프롤로그 컷씬 / 콘솔 / GUIDE-0 대사 / 튜토리얼
// 안내문을 제공한다. 문구·이미지 경로는 전부 그 파일에만 있고 코드에는 없다.
//
// DialogueRepository 와 같은 규약을 따른다.
//  - 파일이 없거나 파싱이 깨져도 게임이 멈추지 않는다(빈 데이터 + 경고 로그).
//  - 내보낸 빌드에 포함하려면 export_presets.cfg 의 include_filter 에 파일을 추가한다.
public static class PrologueScript
{
    private const string RuntimePath = "res://docs/NSP_PROLOGUE_RUNTIME.md";

    // --- 컷씬 -----------------------------------------------------------
    public enum SlideFx { None, Glitch, Cut, Siren, Shake, Blackout, Typing, Impact, Alert, Flicker, Crt, Warp, WarpHold }

    public sealed class Slide
    {
        public string Title = "";
        public string ImagePath = "";
        public string ImageNote = "";
        // 배경 앞에 서는 인물 일러스트(없으면 임시 [ FIGURE ] 칸).
        public string FigurePath = "";
        public string FigureNote = "";
        public string Speaker = "";
        // 이 슬라이드 자막을 읽는 목소리. 기존 직원 id(rabbit, wolf …) 또는 프롤로그 전용
        // director / guide0. 비워 두면 소리 없이 글자만 찍힌다.
        public string VoiceId = "";
        // 무전/인터컴으로 들리는가(Radio 버스 + 앞뒤 치직 + 약한 잡음).
        public bool Radio;
        public string Text = "";
        public string Overlay = "";
        // overlay 아래 작게 깔리는 영문 보조 문구(주 정보는 언제나 한글 overlay 다).
        public string Sub = "";
        public string Sfx = "";
        // 자막/문구가 다 찍힌 직후에 울리는 효과음(스위치 조작음 등).
        public string SfxAfter = "";
        // 이 슬라이드에서 시작/중단할 반복 효과음(사이렌 등). 컷씬이 끝나면 전부 자동 중단된다.
        public string SfxLoopStart = "";
        public string SfxLoopStop = "";
        public float Hold = 2.5f;
        // 화면이 계속 미세하게 흔들리는 세기(px). 다음 슬라이드로 이어진다(title 과 같은 상속 규칙).
        public float Shake;
        // 슬라이드가 뜨는 순간 좌우로 짧게 흔들리는 시간(초). 0 이면 흔들지 않는다.
        public float Jolt;
        // 무전 수신 상태 HUD(0 이면 안 띄운다). 낮을수록 파형이 거칠고 노이즈가 늘어난다.
        public int Signal;
        // 대사가 다 찍히는 순간 통신이 치직 하고 끊긴다.
        public bool Cutoff;
        // 느린 줌/팬(켄 번스). 정지 이미지가 슬라이드처럼 보이지 않게 기본으로 켜 둔다.
        public bool KenBurns = true;
        // 게이지 연출 — 숫자와 막대가 실제로 내려간다. GaugeSteps 가 비어 있으면 안 그린다.
        public string GaugeTitle = "";
        public string GaugeSub = "";
        public string GaugeAlert = "";
        public readonly List<float> GaugeSteps = new();
        // 시설 시스템 창(비상 차폐 등). WinTitle 이 비어 있으면 안 그린다.
        public string WinTitle = "";
        public string WinBig = "";
        public string WinSub = "";

        // 비상 경보창 — 빨간 경보 패널. AlertTitle 이 비어 있으면 안 그린다.
        public string AlertTitle = "";
        public string AlertSub = "";
        public string AlertFoot = "";
        public readonly List<(string Label, string Value)> AlertRows = new();
        public SlideFx Fx = SlideFx.None;
    }

    public sealed class Cutscene
    {
        public string Id = "";
        public readonly List<Slide> Slides = new();
    }

    // --- 콘솔 -----------------------------------------------------------
    public sealed class ConsoleStep
    {
        public string Text = "";     // 출력할 줄(빈 문자열이면 빈 줄)
        public float Wait;           // Text 가 비어 있고 Wait > 0 이면 순수 대기
        public bool IsOk;            // 성공 메시지(밝은 색 + 효과음)
        public bool IsWaitOnly;
    }

    public sealed class ConsoleBlock
    {
        public string Id = "";
        public readonly List<ConsoleStep> Steps = new();
    }

    // --- 시스템 창 ------------------------------------------------------
    // 콘솔이 끝난 뒤 화면 한가운데 잠깐 떴다 닫히는 창(권한 승계 완료 등).
    public sealed class WindowBlock
    {
        public string Id = "";
        public string Title = "";
        public string Big = "";
        public readonly List<string> Lines = new();
        public float Hold = 1.5f;
    }

    // --- GUIDE-0 --------------------------------------------------------
    // 한 beat = 화면에 한 번 표시되는 단위. 대사 한 줄이거나, 초상 교체/아이콘/노이즈 지시다.
    public enum GuideBeatKind { Line, Portrait, Icons, Noise, Panel }

    public sealed class GuideBeat
    {
        public GuideBeatKind Kind = GuideBeatKind.Line;
        public string Value = "";
    }

    public sealed class GuideBlock
    {
        public string Id = "";
        public string StartPortrait = "normal";
        // GUIDE-0 전용 보이스. 데이터에서 voice: 로 덮어쓸 수 있다.
        public string VoiceId = "guide0";
        // 블록이 열릴 때의 보조 정보판(none/alert/authority/mission). 중간에 panel: 로 바꿀 수 있다.
        public string StartPanel = "none";
        public readonly List<GuideBeat> Beats = new();
    }

    public sealed class MenuOption
    {
        public string Label = "";
        public string GuideId = "";
        // final: 로 적은 항목 — 고르면 그 자리에서 메뉴가 끝난다(mode: all 이면 쓰지 않는다).
        public bool IsFinal;
    }

    public sealed class MenuBlock
    {
        public string Id = "";
        // mode: all — 모든 항목을 한 번씩 확인해야 메뉴가 끝난다(순서는 자유).
        public bool RequireAll;
        public readonly List<MenuOption> Options = new();
    }

    private static readonly Dictionary<string, Cutscene> _cutscenes = new();
    private static readonly Dictionary<string, ConsoleBlock> _consoles = new();
    private static readonly Dictionary<string, WindowBlock> _windows = new();
    private static readonly Dictionary<string, GuideBlock> _guides = new();
    private static readonly Dictionary<string, MenuBlock> _menus = new();
    private static readonly Dictionary<string, string> _scripted = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try { Parse(); }
        catch (Exception e) { GD.PushWarning($"PrologueScript: 파싱 실패 — {e.Message}"); }
    }

    public static Cutscene GetCutscene(string id)
    {
        EnsureLoaded();
        return _cutscenes.GetValueOrDefault(id);
    }

    public static ConsoleBlock GetConsole(string id)
    {
        EnsureLoaded();
        return _consoles.GetValueOrDefault(id);
    }

    public static WindowBlock GetWindow(string id)
    {
        EnsureLoaded();
        return _windows.GetValueOrDefault(id);
    }

    public static GuideBlock GetGuide(string id)
    {
        EnsureLoaded();
        return _guides.GetValueOrDefault(id);
    }

    public static MenuBlock GetMenu(string id)
    {
        EnsureLoaded();
        return _menus.GetValueOrDefault(id);
    }

    // 튜토리얼 전용 고정 대사. 치환자는 호출부가 Replace 로 채운다.
    public static string GetScripted(string id)
    {
        EnsureLoaded();
        return _scripted.GetValueOrDefault(id, "");
    }

    private static void Parse()
    {
        if (!FileAccess.FileExists(RuntimePath))
        {
            GD.PushError($"PrologueScript: {RuntimePath} 를 찾지 못했습니다. " +
                "프롤로그와 튜토리얼이 통째로 건너뛰어집니다. " +
                "내보내기 빌드라면 export_presets.cfg 의 include_filter 에 이 파일이 있는지 확인하십시오 " +
                "(.md 는 리소스가 아니라 자동으로 담기지 않습니다).");
            return;
        }
        using var f = FileAccess.Open(RuntimePath, FileAccess.ModeFlags.Read);
        if (f == null) return;

        Cutscene cutscene = null;
        Slide slide = null;
        string inheritedTitle = "";
        float inheritedShake = 0f;
        ConsoleBlock console = null;
        WindowBlock window = null;
        GuideBlock guide = null;
        MenuBlock menu = null;
        string scriptedId = null;

        while (!f.EofReached())
        {
            string raw = f.GetLine();
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            if (line.StartsWith("@"))
            {
                var (tag, id) = SplitTag(line);
                switch (tag)
                {
                    case "@cutscene":
                        cutscene = new Cutscene { Id = id };
                        _cutscenes[id] = cutscene;
                        slide = null; inheritedTitle = ""; inheritedShake = 0f;
                        console = null; window = null; guide = null; menu = null; scriptedId = null;
                        continue;
                    case "@slide":
                        if (cutscene == null) continue;
                        slide = new Slide { Title = inheritedTitle, Shake = inheritedShake };
                        cutscene.Slides.Add(slide);
                        continue;
                    case "@console":
                        console = new ConsoleBlock { Id = id };
                        _consoles[id] = console;
                        cutscene = null; slide = null; window = null; guide = null; menu = null; scriptedId = null;
                        continue;
                    case "@window":
                        window = new WindowBlock { Id = id };
                        _windows[id] = window;
                        cutscene = null; slide = null; console = null; guide = null; menu = null; scriptedId = null;
                        continue;
                    case "@guide":
                        guide = new GuideBlock { Id = id };
                        _guides[id] = guide;
                        cutscene = null; slide = null; console = null; window = null; menu = null; scriptedId = null;
                        continue;
                    case "@menu":
                        menu = new MenuBlock { Id = id };
                        _menus[id] = menu;
                        cutscene = null; slide = null; console = null; window = null; guide = null; scriptedId = null;
                        continue;
                    case "@scripted":
                        scriptedId = id;
                        _scripted[id] = "";
                        cutscene = null; slide = null; console = null; window = null; guide = null; menu = null;
                        continue;
                    default:
                        continue;
                }
            }

            var (key, value) = SplitKeyValue(line);
            if (key == null) continue;

            if (slide != null && ApplySlideField(slide, key, value, ref inheritedTitle, ref inheritedShake)) continue;
            if (console != null && ApplyConsoleField(console, key, value)) continue;
            if (window != null && ApplyWindowField(window, key, value)) continue;
            if (guide != null && ApplyGuideField(guide, key, value)) continue;
            if (menu != null && ApplyMenuField(menu, key, value)) continue;
            if (scriptedId != null && key == "text") _scripted[scriptedId] = value;
        }
    }

    private static (string tag, string id) SplitTag(string line)
    {
        int sp = line.IndexOf(' ');
        return sp < 0 ? (line, "") : (line[..sp], line[(sp + 1)..].Trim());
    }

    // "key: value" / "key:" 둘 다 받는다. 값에 ':' 가 들어가도 첫 구분자만 쓴다.
    private static (string key, string value) SplitKeyValue(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return (null, null);
        string key = line[..colon].Trim().ToLowerInvariant();
        string value = line[(colon + 1)..].Trim();
        return (key, value);
    }

    private static bool ApplySlideField(Slide s, string key, string value, ref string inheritedTitle, ref float inheritedShake)
    {
        switch (key)
        {
            case "title": s.Title = value; inheritedTitle = value; return true;
            case "image": s.ImagePath = value; return true;
            case "imagenote": s.ImageNote = value; return true;
            case "voice": s.VoiceId = value; return true;
            case "radio": s.Radio = ParseBool(value); return true;
            case "figure": s.FigurePath = value; return true;
            case "figurenote": s.FigureNote = value; return true;
            case "sfxloop": s.SfxLoopStart = value; return true;
            case "sfxloopstop": s.SfxLoopStop = value; return true;
            case "shake":
                s.Shake = ParseFloat(value, 0f);
                inheritedShake = s.Shake;
                return true;
            case "jolt": s.Jolt = ParseFloat(value, 0f); return true;
            case "signal": s.Signal = Mathf.Clamp(Mathf.RoundToInt(ParseFloat(value, 0f)), 0, 100); return true;
            case "cutoff": s.Cutoff = ParseBool(value); return true;
            case "ken": s.KenBurns = !value.Equals("off", StringComparison.OrdinalIgnoreCase); return true;
            case "speaker": s.Speaker = value; return true;
            case "text": s.Text = Unescape(value); return true;
            case "overlay": s.Overlay = Unescape(value); return true;
            case "sub": s.Sub = value; return true;
            case "gauge": s.GaugeTitle = value; return true;
            case "gaugesub": s.GaugeSub = value; return true;
            case "gaugealert": s.GaugeAlert = value; return true;
            case "gaugesteps":
                s.GaugeSteps.Clear();
                foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    s.GaugeSteps.Add(ParseFloat(part.Trim(), 0f));
                return true;
            case "alert":
            {
                var (a, b) = SplitBar(value);
                s.AlertTitle = a; s.AlertSub = b;
                return true;
            }
            case "alertrow":
            {
                var (a, b) = SplitBar(value);
                s.AlertRows.Add((a, b));
                return true;
            }
            case "win": s.WinTitle = value; return true;
            case "winbig": s.WinBig = value; return true;
            case "winsub": s.WinSub = value; return true;
            case "alertfoot": s.AlertFoot = value; return true;
            case "sfxafter": s.SfxAfter = value; return true;
            case "sfx": s.Sfx = value; return true;
            case "hold": s.Hold = ParseFloat(value, s.Hold); return true;
            case "fx": s.Fx = ParseFx(value); return true;
            default: return false;
        }
    }

    // "왼쪽 | 오른쪽" 을 둘로 가른다. '|' 가 없으면 오른쪽은 빈 문자열.
    private static (string Left, string Right) SplitBar(string v)
    {
        int bar = v.IndexOf('|');
        return bar < 0 ? (v.Trim(), "") : (v[..bar].Trim(), v[(bar + 1)..].Trim());
    }

    // 데이터 파일에서는 줄바꿈을 역슬래시 n 두 글자로 적는다(한 줄에 한 항목이라는 규칙을 지키려고).
    private static string Unescape(string v) =>
        string.IsNullOrEmpty(v) ? v : v.Replace("\\n", "\n");

    private static bool ParseBool(string v) =>
        v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"
        || v.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static float ParseFloat(string v, float fallback) =>
        float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;

    private static SlideFx ParseFx(string v) => v.ToLowerInvariant() switch
    {
        "glitch" => SlideFx.Glitch,
        "cut" => SlideFx.Cut,
        "siren" => SlideFx.Siren,
        "shake" => SlideFx.Shake,
        "blackout" => SlideFx.Blackout,
        "typing" => SlideFx.Typing,
        "impact" => SlideFx.Impact,
        "alert" => SlideFx.Alert,
        "flicker" => SlideFx.Flicker,
        "crt" => SlideFx.Crt,
        "warp" => SlideFx.Warp,
        "warphold" => SlideFx.WarpHold,
        _ => SlideFx.None,
    };

    private static bool ApplyConsoleField(ConsoleBlock c, string key, string value)
    {
        switch (key)
        {
            case "line": c.Steps.Add(new ConsoleStep { Text = value }); return true;
            case "ok": c.Steps.Add(new ConsoleStep { Text = value, IsOk = true }); return true;
            case "wait":
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w))
                    c.Steps.Add(new ConsoleStep { Wait = w, IsWaitOnly = true });
                return true;
            default: return false;
        }
    }

    private static bool ApplyWindowField(WindowBlock w, string key, string value)
    {
        switch (key)
        {
            case "title": w.Title = value; return true;
            case "big": w.Big = value; return true;
            case "line": w.Lines.Add(value); return true;
            case "hold": w.Hold = ParseFloat(value, w.Hold); return true;
            default: return false;
        }
    }

    private static bool ApplyGuideField(GuideBlock g, string key, string value)
    {
        switch (key)
        {
            case "portrait":
                // 첫 portrait 는 블록의 시작 표정, 이후 것은 대사 중간의 표정 교체다.
                if (g.Beats.Count == 0) g.StartPortrait = value;
                else g.Beats.Add(new GuideBeat { Kind = GuideBeatKind.Portrait, Value = value });
                return true;
            case "voice": g.VoiceId = value; return true;
            case "panel":
                // 첫 panel 은 블록이 열릴 때의 정보판, 이후 것은 대사 중간의 교체다.
                if (g.Beats.Count == 0) g.StartPanel = value;
                else g.Beats.Add(new GuideBeat { Kind = GuideBeatKind.Panel, Value = value });
                return true;
            case "line": g.Beats.Add(new GuideBeat { Kind = GuideBeatKind.Line, Value = Unescape(value) }); return true;
            case "icons": g.Beats.Add(new GuideBeat { Kind = GuideBeatKind.Icons, Value = value }); return true;
            case "fx": g.Beats.Add(new GuideBeat { Kind = GuideBeatKind.Noise, Value = value }); return true;
            default: return false;
        }
    }

    private static bool ApplyMenuField(MenuBlock m, string key, string value)
    {
        if (key == "mode")
        {
            m.RequireAll = value.Equals("all", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        if (key is not ("option" or "final")) return false;
        int bar = value.LastIndexOf('|');
        if (bar < 0) return true;
        m.Options.Add(new MenuOption
        {
            Label = value[..bar].Trim(),
            GuideId = value[(bar + 1)..].Trim(),
            IsFinal = key == "final",
        });
        return true;
    }
}
