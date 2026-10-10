using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 대사 속 강조색 — 직원 이름은 그 직원의 고유색, 작업실 이름은 호박색.
// "먼저 토끼를 끌어다 정비실에 …" 에서 '토끼'는 분홍, '정비실'은 호박색으로 읽힌다.
//
// 어두운 배경(가이드 자막 · 통화창)용 BBCode 를 만든다. 원문 글자 수는 그대로라
// VisibleCharacters / VisibleRatio 타이핑이 원문 기준으로 그대로 맞는다.
public static class DialogueHighlight
{
    public static readonly Color RoomColor = new(1f, 0.80f, 0.36f);

    private static Regex _pattern;
    private static Dictionary<string, Color> _colors;

    // 한 글자 이름("양")이 "다양", "양쪽" 속에서 칠해지지 않게 앞은 한글이 아니고,
    // 직원 이름 뒤는 조사 · 호칭 · 문장부호만 허용한다.
    private const string NameTail = @"(?=씨|님|이랑|랑|이|가|을|를|은|는|과|와|의|도|만|에게|한테|께|에|요|\s|[.,!?~…'""」)\]]|$)";

    public static string Colorize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string safe = text.Replace("[", "[lb]");
        if (!EnsureBuilt()) return safe;
        return _pattern.Replace(safe, m =>
            _colors.TryGetValue(m.Value, out var c) ? $"[color=#{c.ToHtml(false)}]{m.Value}[/color]" : m.Value);
    }

    private static bool EnsureBuilt()
    {
        if (_pattern != null) return true;
        var sim = FacilitySimulation.Instance;
        if (sim == null) return false;

        _colors = new Dictionary<string, Color>();
        var people = new List<string>();
        foreach (string id in sim.GetEmployeeIds())
        {
            var def = sim.GetEmployeeDef(id);
            if (def == null || string.IsNullOrEmpty(def.Codename)) continue;
            _colors[def.Codename] = Readable(def.IconColor);
            people.Add(def.Codename);
        }
        var rooms = new List<string>();
        foreach (string id in sim.GetRoomIds())
        {
            string name = sim.GetRoomDef(id)?.DisplayName;
            if (string.IsNullOrEmpty(name) || _colors.ContainsKey(name)) continue;
            _colors[name] = RoomColor;
            rooms.Add(name);
        }
        if (_colors.Count == 0) return false;

        string Alt(IEnumerable<string> names) =>
            string.Join("|", names.OrderByDescending(n => n.Length).Select(Regex.Escape));
        var parts = new List<string>();
        if (rooms.Count > 0) parts.Add($"(?<![가-힣])(?:{Alt(rooms)})");
        if (people.Count > 0) parts.Add($"(?<![가-힣])(?:{Alt(people)}){NameTail}");
        _pattern = new Regex(string.Join("|", parts), RegexOptions.Compiled);
        return true;
    }

    // 어두운 고유색(늑대의 짙은 적색 등)은 검은 배경에서 안 읽힌다 — 색상은 두고 밝기만 올린다.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.6f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }
}
