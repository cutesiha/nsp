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

    private enum Part { Opener, Core, Caveat, Memory, Impression, Extra, Back }

    // 검증용 — 마지막 답변이 어떤 슬롯과 어떤 근무 기억으로 만들어졌는지(DialogueSampleDump 가 읽는다).
    public static string LastTrace { get; private set; } = "";
    // 마지막으로 조립한 답에 실제로 들어간 근무 기억.
    private static List<ReplyAddendum> _lastKept = new();

    // 문장 수 상한을 넘으면 이 순서대로(앞쪽부터) 뺀다.
    private static readonly Part[] DropOrder = { Part.Back, Part.Extra, Part.Opener, Part.Memory, Part.Impression };

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
        // 실제로 답에 들어간 동료 기억만 "이미 한 이야기"로 적는다(잘려 나간 줄은 아직 안 한 말이다).
        foreach (var a in _lastKept) ShiftMemory.MarkSaid(a);
        DialogueClaimState.Remember(f.EmployeeId, text);
        DialoguePatternMemory.RememberSurface(f.EmployeeId, text);
        return text;
    }

    private static string Build(ReplyFrame f)
    {
        _lastKept = new List<ReplyAddendum>();
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
        // 핵심 문장 틀이 이미 그 말을 품고 있어도 뺀다 — "오, 수상한 사람이요?" + "수상한 사람이요? 못 봤어요!",
        // "진짜 깜짝 놀랐어요!" + "… 진짜 깜짝 놀랐어요." 는 한 답 안에서 같은 말을 두 번 하는 것이다.
        if (opener.Length > 0 && RepeatsAnySentence(core, opener)) opener = "";
        if (opener.Length > 0) parts.Add((Part.Opener, opener));

        parts.Add((Part.Core, core));

        // ── 보정 — 사실 정확성 때문에 빠지지 않는다 ──────────────────────
        foreach (string slot in f.Caveats)
            if (!CaveatCovered(slot, core))
                TryAdd(parts, Part.Caveat, Pick(id, slot, f.Vars, formal));

        // ── 동료에 대한 인상 — 핵심이 사람을 댔을 때(같이 있던 사람 · 본 사람) ──
        // 질문이 그 사람을 물었으므로 근무 기억보다 먼저 남는다.
        // 핵심 문장 틀이 이미 한마디를 품고 있으면("{who} 씨요. 같이 있어서 든든했어요.") 겹쳐 붙이지 않는다.
        string whoInCore = WhoMentioned(core, f.Vars);
        if (whoInCore.Length > 0 && SentenceCount(core) <= 1 && GD.Randf() < voice.ImpressionChance)
            TryAdd(parts, Part.Impression, Impression(id, whoInCore, formal));

        // ── 근무 기억 ─────────────────────────────────────────────────
        string memWho = "";
        var memLines = new string[f.Addenda.Count];
        for (int i = 0; i < f.Addenda.Count; i++)
        {
            var a = f.Addenda[i];
            // 같은 질문을 다시 받으면 프레임(기억 포함)이 그대로 다시 온다 — 이미 한 동료 이야기는 건너뛴다.
            if (ShiftMemory.WasSaid(a)) continue;
            var vars = Merge(f.Vars, a.Vars);
            // 앞에서 이미 말한 방 · 사람을 다시 부르는 틀은 피한다("저장고에 있었어요. 저장고엔 저 혼자였어요.").
            string line = Pick(id, a.Slot, vars, formal, SaidSoFar(parts));
            int before = parts.Count;
            TryAdd(parts, Part.Memory, line);
            if (parts.Count > before) memLines[i] = line;
            if (parts.Count > before && memWho.Length == 0) memWho = WhoMentioned(line, vars);
        }
        // 기억 속 동료 이야기에는 가끔만, 문장 수에 여유가 있을 때만 인상을 붙인다(묻지 않은 사람 이야기라서).
        int budget = f.MaxSentences > 0 ? f.MaxSentences : voice.MaxSentences;
        if (memWho.Length > 0 && !parts.Any(p => p.Kind == Part.Impression) && parts.Count < budget
            && GD.Randf() < voice.ImpressionChance * 0.5f)
            TryAdd(parts, Part.Impression, Impression(id, memWho, formal));

        // ── 덧붙임 · 되묻기 ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(f.ExtraSlot) && !AlreadyCovered(f.ExtraSlot, core))
            TryAdd(parts, Part.Extra, Pick(id, f.ExtraSlot, f.Vars, formal, SaidSoFar(parts)));
        if (!string.IsNullOrEmpty(f.BackSlot))
            TryAdd(parts, Part.Back, Pick(id, f.BackSlot, f.Vars, formal));

        // ── 문장 수 상한 ─────────────────────────────────────────────
        int max = budget;
        // 사람을 물은 답에 붙은 인상은 상한을 한 칸 넘겨도 된다 — 그게 그 사람의 대답이다.
        if (whoInCore.Length > 0 && parts.Any(p => p.Kind == Part.Impression)) max++;
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
        // 기억 속 동료에 붙은 인상은 그 기억이 잘려 나가면 같이 빠진다.
        if (whoInCore.Length == 0 && memWho.Length > 0
            && !parts.Any(p => p.Kind == Part.Memory && p.Text.Contains(CodenameOf(memWho))))
            parts.RemoveAll(p => p.Kind == Part.Impression);

        // 기억 한 줄마다 실제로 답에 남았는지 표시한다(겹쳐서 안 붙었거나 상한에 잘렸으면 "(잘림)").
        LastTrace = f.Slot + (f.Addenda.Count == 0 ? "" : " + 기억[" + string.Join(", ",
            f.Addenda.Select((a, i) => a.Slot + (Kept(parts, memLines[i]) ? "" : "(잘림)"))) + "]");
        _lastKept = f.Addenda.Where((a, i) => Kept(parts, memLines[i])).ToList();
        string joined = Finalize(string.Join(" ", parts.Select(p => p.Text)));
        int ex = f.MaxExclamations >= 0 ? f.MaxExclamations : voice.MaxExclamations;
        // 말끝을 흐리는 게 버릇인 사람(양)은 말줄임을 더 허용한다.
        int ellipses = voice.TrailOffChance >= 0.5f ? 4 : 2;
        return DialogueVoiceTics.Apply(DialogueNaturalnessFilter.Clean(joined, ex, ellipses), voice);
    }

    private static bool Kept(List<(Part Kind, string Text)> parts, string line) =>
        line != null && parts.Any(p => p.Kind == Part.Memory && p.Text == line);

    // --- 동료 인상 ---------------------------------------------------------

    // 문장이 실제로 동료 이름을 말했는가. 말했으면 그 직원 id.
    private static string WhoMentioned(string line, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(line)) return "";
        foreach (string k in new[] { "who", "dname" })
            if (vars.TryGetValue(k, out var name) && !string.IsNullOrEmpty(name) && line.Contains(name))
                return IdOfCodename(name);
        return "";
    }

    // 말하는 사람이 그 동료를 어떻게 보는가 — about.<대상 id>, 없으면 about.any.
    private static string Impression(string speakerId, string targetId, bool formal)
    {
        if (string.IsNullOrEmpty(targetId) || targetId == speakerId) return "";
        string codename = CodenameOf(targetId);
        var vars = new Dictionary<string, string> { ["who"] = codename };
        string text = Pick(speakerId, "about." + targetId, vars, formal);
        return text.Length > 0 ? text : Pick(speakerId, "about.any", vars, formal);
    }

    private static readonly Dictionary<string, string> KnownCodenames = new()
    {
        ["고양이"] = "cat", ["강아지"] = "dog", ["여우"] = "fox",
        ["토끼"] = "rabbit", ["양"] = "sheep", ["늑대"] = "wolf",
    };

    private static string IdOfCodename(string codename)
    {
        var sim = NSP.Facility.FacilitySimulation.Instance;
        if (sim != null)
            foreach (string eid in sim.GetEmployeeIds())
                if (sim.GetEmployeeDef(eid)?.Codename == codename) return eid;
        return KnownCodenames.GetValueOrDefault(codename, "");
    }

    private static string CodenameOf(string employeeId)
    {
        string name = NSP.Facility.FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename;
        if (!string.IsNullOrEmpty(name)) return name;
        foreach (var kv in KnownCodenames) if (kv.Value == employeeId) return kv.Key;
        return "";
    }

    // 같은 뜻을 두 번 말하지 않는다 — 이미 들어간 문장과 겹치면 붙이지 않는다.
    private static void TryAdd(List<(Part Kind, string Text)> parts, Part kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var p in parts)
            if (RepeatsAnySentence(p.Text, text)) return;
        // 같은 종결 어미가 연달아 나오면 말버릇만 반복하는 것처럼 들린다("…밀렸는데요. …일했는데요.").
        // 보정은 사실 정확성 때문에 빠질 수 없으므로 예외.
        if (kind != Part.Caveat && parts.Count > 0 && SameTic(parts[^1].Text, text)) return;
        parts.Add((kind, text));
    }

    // 두 덩어리가 같은 말을 하는가 — 통째로, 그리고 문장끼리.
    // 틀 하나가 두 문장일 수 있어서 통째 비교만으로는 "다들 열심히 하시던데요." 와
    // "다들 열심히 하시는 것 같았어요." 가 겹치는 걸 못 본다. 문장 앞의 짧은 감탄("오, " "어, ")은 떼고 본다.
    private static bool RepeatsAnySentence(string a, string b)
    {
        if (DialogueNaturalnessFilter.Repeats(a, b)) return true;
        foreach (string x in Sentences(a))
            foreach (string y in Sentences(b))
                if (DialogueNaturalnessFilter.Repeats(x, y)) return true;
        return false;
    }

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?~…])\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingInterjection = new(@"^[가-힣]{1,2},\s+(?=\S{3,})", RegexOptions.Compiled);

    private static IEnumerable<string> Sentences(string text) =>
        SentenceSplit.Split(text ?? "")
            .Select(s => LeadingInterjection.Replace(s.Trim(), ""))
            .Where(s => s.Length > 0);

    private static readonly Regex SentenceBreak = new(@"[.!?~…](?=\s)", RegexOptions.Compiled);

    private static int SentenceCount(string text) => SentenceBreak.Matches(text).Count + 1;

    private static readonly string[] TicEndings = { "죠.", "는데요.", "거든요.", "잖아요.", "더군요.", "고요." };

    private static bool SameTic(string a, string b)
    {
        foreach (string e in TicEndings)
            if (a.EndsWith(e) && b.EndsWith(e)) return true;
        return false;
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

    // 한 답 안에서 두 번 부르면 되풀이로 들리는 값(방 · 사람 · 업무 이름).
    private static readonly string[] NamedVars = { "room", "iroom", "who", "who2", "task" };

    private static string SaidSoFar(List<(Part Kind, string Text)> parts) =>
        string.Join(" ", parts.Select(p => p.Text));

    // 이 틀이 앞에서 이미 말한 방 · 사람 · 업무를 다시 부르는가.
    private static bool NamesAgain(string template, Dictionary<string, string> vars, string said) =>
        Tokens(template).Any(k => NamedVars.Contains(k)
                                  && vars.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && said.Contains(v));

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
    //
    // said 를 주면 그 안에 이미 나온 방 · 사람 · 업무를 다시 부르는 틀도 뺀다(덧붙이는 문장용).
    // 남는 틀이 없으면 빈 값 — 그 덧붙임은 이번 답에서 빠진다.
    public static string Pick(string employeeId, string slot, Dictionary<string, string> vars, bool formal,
        string said = null)
    {
        if (string.IsNullOrEmpty(slot)) return "";
        var pool = DialogueLineBank.Get(employeeId, slot, formal);
        if (pool.Length == 0) return "";

        var usable = pool
            .Select((t, i) => (Text: t, Id: $"{slot}|{i}"))
            .Where(x => Tokens(x.Text).All(k => vars.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)))
            .Where(x => said == null || !NamesAgain(x.Text, vars, said))
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
        if (text.Length > 0 && !".!?…~ㅎ".Contains(text[^1])) text += ".";
        return text;
    }
}
