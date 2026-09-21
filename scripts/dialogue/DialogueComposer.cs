using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace NSP.Dialogue;

// Local Dialogue V2 의 유일한 문장 조립기.
//
// 인터뷰(기본 질문 · 증거 질문 · 추궁) · 플레이어가 거는 통화 · 직원이 거는 통화가 전부
// ReplyFrame 하나로 여기에 들어온다. 여기서 하는 일은 셋뿐이다.
//   1) 슬롯마다 DialogueLineBank 에서 "완성 문장 틀" 하나를 고른다(최근에 쓴 틀·말버릇은 피한다)
//   2) 문장 단위로만 잇는다 — 핵심 / 보정 / 근무 기억 / 여는 말 / 덧붙임 / 되묻기
//   3) 문장 수 상한을 넘으면 덜 중요한 것부터 뺀다(핵심과 보정은 절대 빠지지 않는다)
//
// 문장 안에서 조각을 이어 붙이지 않는다. 한 문장은 언제나 사람이 통째로 쓴 틀 하나다.
public static class DialogueComposer
{
    // 같은 답변이 연달아 나오면 몇 번까지 다시 뽑아 볼 것인가.
    private const int RetryOnRepeat = 6;

    private enum Part { Opener, Core, Caveat, Memory, Extra, Back }

    // 검증용 — 마지막 답변이 어떤 슬롯과 어떤 근무 기억으로 만들어졌는지(DialogueSampleDump 가 읽는다).
    public static string LastTrace { get; private set; } = "";

    // 문장 수 상한을 넘으면 이 순서대로(앞쪽부터) 뺀다.
    private static readonly Part[] DropOrder = { Part.Back, Part.Extra, Part.Opener, Part.Memory };

    public static string Compose(ReplyFrame f)
    {
        if (f == null || string.IsNullOrEmpty(f.EmployeeId)) return "…";
        string text = "";
        for (int attempt = 0; attempt < RetryOnRepeat; attempt++)
        {
            text = Build(f);
            if (string.IsNullOrEmpty(text)) continue;
            if (!DialogueClaimState.WasRecent(f.EmployeeId, text)) break;
        }
        if (string.IsNullOrEmpty(text)) text = "…";
        DialogueClaimState.Remember(f.EmployeeId, text);
        DialoguePatternMemory.RememberSurface(f.EmployeeId, text);
        return text;
    }

    private static string Build(ReplyFrame f)
    {
        var voice = DialogueVoices.Get(f.EmployeeId);
        bool formal = voice.Formal;
        string id = f.EmployeeId;

        string core = Pick(id, f.Slot, f.Vars, formal);
        if (core.Length == 0 && !string.IsNullOrEmpty(f.FallbackSlot)) core = Pick(id, f.FallbackSlot, f.Vars, formal);
        if (core.Length == 0 && string.IsNullOrEmpty(f.CustomSlot)) core = Pick(id, f.Topic + ".any", f.Vars, formal);
        if (core.Length == 0) core = Pick(id, "Unknown.any", f.Vars, formal);
        if (core.Length == 0) return "";
        // 시간 표현은 핵심 문장이 장소로 시작할 때만 앞에 붙인다("22시 40분경 발전실에서 …").
        // "직접 보지는 못했습니다. …" 같은 문장 앞에 붙이면 사람 말이 아니게 된다.
        if (!string.IsNullOrEmpty(f.TimeWord) && !HasTimeWord(core) && StartsWithPlace(core, f.Vars))
            core = f.TimeWord + " " + core;

        var parts = new List<(Part Kind, string Text)>();

        // ── 여는 말(하나만) ────────────────────────────────────────────
        string opener = "";
        if (!string.IsNullOrEmpty(f.OpenerText)) { if (!OpensWithQuestion(core)) opener = f.OpenerText; }
        else if (!string.IsNullOrEmpty(f.OpenerSlot)) opener = Pick(id, f.OpenerSlot, f.Vars, formal);
        // 여는 말이 핵심과 같은 말로 시작하면 뺀다("네." + "네, 자리를 …").
        if (opener.Length > 0 && core.Length >= 2 && opener.StartsWith(core[..2])) opener = "";
        if (opener.Length > 0) parts.Add((Part.Opener, opener));

        parts.Add((Part.Core, core));

        // ── 보정 — 사실 정확성 때문에 빠지지 않는다 ──────────────────────
        foreach (string slot in f.Caveats)
            if (!CaveatCovered(slot, core))
                TryAdd(parts, Part.Caveat, Pick(id, slot, f.Vars, formal));

        // ── 근무 기억 ─────────────────────────────────────────────────
        foreach (var a in f.Addenda)
            TryAdd(parts, Part.Memory, Pick(id, a.Slot, Merge(f.Vars, a.Vars), formal));

        // ── 덧붙임 · 되묻기 ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(f.ExtraSlot) && !AlreadyCovered(f.ExtraSlot, core))
            TryAdd(parts, Part.Extra, Pick(id, f.ExtraSlot, f.Vars, formal));
        if (!string.IsNullOrEmpty(f.BackSlot))
            TryAdd(parts, Part.Back, Pick(id, f.BackSlot, f.Vars, formal));

        // ── 문장 수 상한 ─────────────────────────────────────────────
        int max = f.MaxSentences > 0 ? f.MaxSentences : voice.MaxSentences;
        int keep = parts.Count(p => p.Kind is Part.Core or Part.Caveat);
        max = Mathf.Max(max, keep);
        foreach (var drop in DropOrder)
        {
            while (parts.Count > max)
            {
                int at = parts.FindLastIndex(p => p.Kind == drop);
                if (at < 0) break;
                parts.RemoveAt(at);
            }
        }

        LastTrace = f.Slot + (f.Addenda.Count == 0 ? "" : " + 기억[" + string.Join(", ",
            f.Addenda.Select(a => a.Slot + (parts.Any(p => p.Kind == Part.Memory) ? "" : "(잘림)"))) + "]");
        string joined = Finalize(string.Join(" ", parts.Select(p => p.Text)));
        int ex = f.MaxExclamations >= 0 ? f.MaxExclamations : voice.MaxExclamations;
        return DialogueNaturalnessFilter.Clean(joined, ex);
    }

    // 같은 뜻을 두 번 말하지 않는다 — 이미 들어간 문장과 겹치면 붙이지 않는다.
    private static void TryAdd(List<(Part Kind, string Text)> parts, Part kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var p in parts)
            if (DialogueNaturalnessFilter.Repeats(p.Text, text)) return;
        // 같은 종결 어미가 연달아 나오면 말버릇만 반복하는 것처럼 들린다.
        if (parts.Count > 0 && parts[^1].Text.EndsWith("죠.") && text.EndsWith("죠.")) return;
        parts.Add((kind, text));
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var m = new Dictionary<string, string>(a);
        foreach (var kv in b) m[kv.Key] = kv.Value;
        return m;
    }

    // 핵심 문장이 이미 되물으며 시작하는가("제가요? …"). 그렇다면 주제 되받기는 군더더기다.
    private static bool OpensWithQuestion(string core)
    {
        int q = core.IndexOf('?');
        return q >= 0 && q <= 6;
    }

    // 이미 시점을 품고 있는 문장에는 시간 표현을 덧대지 않는다("아까 계속 …" 방지).
    private static bool HasTimeWord(string s) =>
        s.Contains("그때") || s.Contains("아까") || s.Contains("조금 전") || s.Contains("그쯤")
        || s.Contains("시쯤") || s.Contains("시경") || s.StartsWith("계속");

    private static bool StartsWithPlace(string core, Dictionary<string, string> vars)
    {
        foreach (string k in new[] { "iroom", "room" })
            if (vars.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && core.StartsWith(v)) return true;
        return false;
    }

    // 핵심 문장 틀이 이미 그 보정을 품고 있으면 또 붙이지 않는다
    // ("직접 보지는 못했습니다. 발전실 쪽에서 …" + "보지 못했습니다. 방향만 압니다." 방지).
    private static readonly string[] SeenCaveatWords =
        { "직접", "보지 못", "보진 못", "보지는 못", "본 건 아니", "본 게 아니", "소리만", "못 봤", "들은 거", "들은 것", "너머" };

    private static bool CaveatCovered(string slot, string core) => slot switch
    {
        "caveat.indirect" => SeenCaveatWords.Any(core.Contains),
        "caveat.cause" => core.Contains("원인") || core.Contains("왜 그랬는지"),
        _ => false,
    };

    private static readonly string[] WorkWords = { "일 하", "일하", "근무", "업무", "작업" };

    // 이 덧붙임이 담는 정보를 핵심 문장이 이미 담고 있는가(문장 비교로 안 잡히는 의미 중복).
    private static bool AlreadyCovered(string extraSlot, string core) => extraSlot switch
    {
        "support.task" or "support.taskname" => WorkWords.Any(core.Contains),
        "volunteer.noanomaly" => core.Contains("말씀드렸") || core.Contains("보고"),
        "volunteer.nosight" => core.Contains("지목") || core.Contains("몰아가") || core.Contains("의심"),
        "support.hedge" => core.Contains("확실") || core.Contains("같아요") || core.Contains("수도 있"),
        _ => false,
    };

    // --- 문장 고르기 -------------------------------------------------------

    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static IEnumerable<string> Tokens(string text)
    {
        foreach (Match m in TokenPattern.Matches(text)) yield return m.Groups[1].Value;
    }

    // 이 슬롯에서 문장 하나를 고르고 변수를 채운다.
    //
    // 값이 비어 있는 변수를 요구하는 문장은 후보에서 뺀다 — "{who} 씨를 봤어요" 가
    // "씨를 봤어요" 로 새어 나가지 않게. 문자열이 달라도 같은 말버릇이면 사람 귀에는
    // 반복이므로 최근에 쓴 틀·시작 반응어는 한동안 피한다.
    public static string Pick(string employeeId, string slot, Dictionary<string, string> vars, bool formal)
    {
        if (string.IsNullOrEmpty(slot)) return "";
        var pool = DialogueLineBank.Get(employeeId, slot, formal);
        if (pool.Length == 0) return "";

        var usable = pool
            .Select((t, i) => (Text: t, Id: $"{slot}|{i}"))
            .Where(x => Tokens(x.Text).All(k => vars.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)))
            .ToList();
        if (usable.Count == 0) return "";

        var fresh = usable
            .Where(x => !DialoguePatternMemory.TemplateUsedRecently(employeeId, x.Id))
            .Where(x => !DialoguePatternMemory.MarkerUsedRecently(employeeId, DialoguePatternMemory.MarkerOf(x.Text)))
            .ToList();
        if (fresh.Count == 0)
            fresh = usable.Where(x => !DialoguePatternMemory.TemplateUsedRecently(employeeId, x.Id)).ToList();
        if (fresh.Count == 0) fresh = usable;

        var chosen = fresh[(int)(GD.Randi() % (uint)fresh.Count)];
        DialoguePatternMemory.RememberTemplate(employeeId, chosen.Id);

        string text = chosen.Text;
        foreach (var kv in vars) text = text.Replace("{" + kv.Key + "}", kv.Value);
        return text;
    }

    // 조사 정리 + 남은 자리표시자 제거 + 공백 정리. 최종 출력은 반드시 여기를 통과한다.
    public static string Finalize(string text)
    {
        text = KoreanParticle.Resolve(text);
        text = TokenPattern.Replace(text, "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\s+([.,!?…])", "$1");
        if (text.Length > 0 && !".!?…~".Contains(text[^1])) text += ".";
        return text;
    }
}
