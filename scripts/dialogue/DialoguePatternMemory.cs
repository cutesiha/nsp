using System.Collections.Generic;

namespace NSP.Dialogue;

// 같은 말버릇이 연달아 나오는 것을 막는다.
//
// 문자열이 다르다고 다른 말이 아니다. "네! 봤어요!" 다음에 "네! 확실해요!" 가 나오면
// 사람 귀에는 같은 말버릇의 반복이다. 그래서 완성 문장뿐 아니라
//   · 어떤 템플릿을 썼는지(pool 안의 몇 번째 문장인지)
//   · 어떤 반응어로 시작했는지
//   · 어떤 발화 형태였는지
// 를 함께 기억하고, 최근에 쓴 것은 한동안 뽑히지 않게 한다.
//
// 기억은 직원별이며 근무가 바뀌면(DialogueClaimState.ResetAll) 함께 지워진다.
public static class DialoguePatternMemory
{
    // 이 횟수 안에 다시 쓰지 않는다.
    private const int TemplateCooldown = 4;
    private const int MarkerCooldown = 3;
    private const int ShapeCooldown = 2;

    private sealed class Memory
    {
        public readonly List<string> Templates = new();
        public readonly List<string> Markers = new();
        public readonly List<string> Shapes = new();
    }

    private static readonly Dictionary<string, Memory> _byEmployee = new();

    private static Memory Of(string employeeId)
    {
        string key = employeeId ?? "";
        if (_byEmployee.TryGetValue(key, out var m)) return m;
        return _byEmployee[key] = new Memory();
    }

    private static void Push(List<string> list, string value, int keep)
    {
        if (string.IsNullOrEmpty(value)) return;
        list.Add(value);
        while (list.Count > keep) list.RemoveAt(0);
    }

    // --- 템플릿 -----------------------------------------------------------

    public static bool TemplateUsedRecently(string employeeId, string templateId) =>
        Of(employeeId).Templates.Contains(templateId);

    public static void RememberTemplate(string employeeId, string templateId) =>
        Push(Of(employeeId).Templates, templateId, TemplateCooldown);

    // --- 반응어 / 담화 표지 ------------------------------------------------

    // 문장을 여는 짧은 말. "네", "아", "글쎄요", "음", "그거요" 같은 것.
    private static readonly string[] Markers =
    {
        "네", "아", "어", "음", "저기", "저", "글쎄요", "그거", "그게", "확실히", "정말",
    };

    public static string MarkerOf(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        foreach (string m in Markers)
            if (text.StartsWith(m)) return m;
        return "";
    }

    public static bool MarkerUsedRecently(string employeeId, string marker) =>
        !string.IsNullOrEmpty(marker) && Of(employeeId).Markers.Contains(marker);

    public static void RememberSurface(string employeeId, string text)
    {
        Push(Of(employeeId).Markers, MarkerOf(text), MarkerCooldown);
    }

    // --- 발화 형태 ---------------------------------------------------------

    public static bool ShapeUsedRecently(string employeeId, UtteranceShape shape) =>
        Of(employeeId).Shapes.Contains(shape.ToString());

    public static void RememberShape(string employeeId, UtteranceShape shape) =>
        Push(Of(employeeId).Shapes, shape.ToString(), ShapeCooldown);

    public static void ResetAll() => _byEmployee.Clear();
}
