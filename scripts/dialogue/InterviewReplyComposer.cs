using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace NSP.Dialogue;

// 증거 기반 심문의 답변을 만든다.
//
// 기존 KoreanDialogueComposer 는 [반응][핵심][보정][보조][감정][끝맺음] 조각을 확률로
// 이어 붙인다. 다양성은 나오지만 "문법은 맞는데 사람이 이렇게 말하진 않는" 문장이 섞인다.
//
// 여기서는 순서를 뒤집는다.
//   1) 무엇을 말할지(ReplyFrame)를 먼저 완전히 확정하고
//   2) 그 의미를 캐릭터별 '완성된 문장' 템플릿 하나로 표현한다.
// 조각을 접합하지 않으므로 어색한 접합면 자체가 생기지 않는다. 캐릭터 차이는 조각 확률이
// 아니라 문장 그 자체에 들어 있다.
public enum ReplyTopic
{
    MoveReason,
    PresenceReason,
    Companion,
    NextLocation,
    ActionThere,
    IncidentKnown,
    WhereAtIncident,
    BeforeIncident,
    WhoSeenNear,
    RouteAround,
    ConfirmTestimony,
    Restate,
    MoodReason,
    MoodBefore,
    MoodRelated,
    ExactTime,
    Confront,
    Unknown,
}

// 답변 한 줄이 담을 의미 전부. 여기까지 정해지면 문장은 표현만 남는다.
public sealed class ReplyFrame
{
    public string EmployeeId = "";
    public ReplyTopic Topic = ReplyTopic.Unknown;
    // 같은 주제 안에서 갈리는 결. 예: Companion 의 with / alone
    public string Variant = "";
    public readonly Dictionary<string, string> Vars = new();

    public string Slot => Topic + (string.IsNullOrEmpty(Variant) ? "" : "." + Variant);

    public ReplyFrame Set(string key, string value)
    {
        Vars[key] = value ?? "";
        return this;
    }
}

public static class InterviewReplyComposer
{
    public static string Compose(ReplyFrame frame)
    {
        if (frame == null) return "…";
        var pool = Pool(frame.EmployeeId, frame.Slot);
        if (pool.Length == 0) pool = Pool(frame.EmployeeId, frame.Topic + ".any");
        if (pool.Length == 0) return "…";

        // 변수가 비어 있는 문장은 후보에서 뺀다 — "에서 뭘 했냐" 같은 빈칸이 새지 않게.
        var usable = pool.Where(t => Tokens(t).All(k => frame.Vars.TryGetValue(k, out var v)
                                                        && !string.IsNullOrEmpty(v))).ToArray();
        if (usable.Length == 0) return "…";

        // 같은 직원에게 같은 문장이 연달아 나오지 않게 한 번 더 뽑아 본다.
        string text = "";
        for (int i = 0; i < 4; i++)
        {
            text = Fill(usable[(int)(GD.Randi() % (uint)usable.Length)], frame.Vars);
            if (!DialogueClaimState.WasRecent(frame.EmployeeId, text)) break;
        }
        DialogueClaimState.Remember(frame.EmployeeId, text);
        return text;
    }

    private static string[] Pool(string employeeId, string slot)
    {
        if (Templates.TryGetValue($"{employeeId}|{slot}", out var own)) return own;
        string register = Formal(employeeId) ? "fml" : "sft";
        if (Templates.TryGetValue($"{register}|{slot}", out var reg)) return reg;
        return Templates.GetValueOrDefault($"any|{slot}") ?? System.Array.Empty<string>();
    }

    private static bool Formal(string employeeId) =>
        DialogueVoiceProfiles.Get(employeeId).Register == SpeechRegister.Formal;

    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static IEnumerable<string> Tokens(string text)
    {
        foreach (Match m in TokenPattern.Matches(text)) yield return m.Groups[1].Value;
    }

    private static string Fill(string text, Dictionary<string, string> vars)
    {
        foreach (var kv in vars) text = text.Replace("{" + kv.Key + "}", kv.Value);
        text = KoreanParticle.Resolve(text);
        text = TokenPattern.Replace(text, "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\s+([.,!?…])", "$1");
        if (text.Length > 0 && !".!?…~".Contains(text[^1])) text += ".";
        return text;
    }

    // ===================================================================
    // 문장 템플릿.
    //
    // 키는 "<대상>|<주제>.<결>" 이고 <대상> 은 직원 id / 말투(fml·sft) / any 순으로 찾는다.
    // 여기 들어가는 것은 조각이 아니라 그 자체로 완결된 대답이다.
    //
    // 변수: {room} {from} {to} {time} {who} {task} {mood} {next} {prev}
    // ===================================================================
    private static readonly Dictionary<string, string[]> Templates = new()
    {
        // ── 왜 그 방으로 이동했는가 ──────────────────────────────────
        ["owl|MoveReason.ordered"] = new[]
        {
            "관리자님 지시를 받고 {to}으로/로 이동했습니다.",
            "재배치 지시가 내려와서 {from}에서 바로 넘어갔습니다.",
        },
        ["cat|MoveReason.ordered"] = new[]
        {
            "관리자님이 가라고 하셨잖아요. 그래서 갔어요.",
            "지시받고 옮긴 건데요.",
        },
        ["jellyfish|MoveReason.ordered"] = new[]
        {
            "지시가 와서요... 바로 {to}으로/로 갔어요.",
            "제가 가야 한다고 하셔서... 그래서 옮겼어요.",
        },
        ["rabbit|MoveReason.ordered"] = new[]
        {
            "관리자님이 가라고 하셨잖아요! 그래서 바로 뛰어갔어요.",
            "지시받고 갔어요! 저 시킨 건 잘하거든요.",
        },
        ["crow|MoveReason.ordered"] = new[]
        {
            "지시에 따라 이동했습니다.",
            "재배치 명령. 그대로 수행했습니다.",
        },
        ["fox|MoveReason.ordered"] = new[]
        {
            "관리자님이 보내셨잖아요. 잊으셨어요?",
            "지시대로 움직인 것뿐인데요.",
        },

        ["owl|MoveReason.repair"] = new[]
        {
            "{to}에 수리가 걸려 있어서 손을 보러 갔습니다.",
            "고장 신호가 떠 있길래 {to}으로/로 넘어갔습니다.",
        },
        ["cat|MoveReason.repair"] = new[]
        {
            "{to} 고장났잖아요. 누가 가긴 가야 하니까요.",
            "수리하러 갔어요. 그거 안 하면 더 커지거든요.",
        },
        ["jellyfish|MoveReason.repair"] = new[]
        {
            "{to}이/가 고장났다고 떠서... 제가 제일 가까워서 갔어요.",
            "수리하러요... 무서웠지만 가야 할 것 같았어요.",
        },
        ["rabbit|MoveReason.repair"] = new[]
        {
            "{to} 고장나서요! 제가 얼른 가서 고쳤어요.",
            "수리하러 갔어요! 빨리 안 가면 큰일 나잖아요.",
        },
        ["crow|MoveReason.repair"] = new[]
        {
            "{to} 수리. 그게 전부입니다.",
            "고장 처리를 위해 이동했습니다.",
        },
        ["fox|MoveReason.repair"] = new[]
        {
            "{to}이/가 영 상태가 안 좋아 보여서요. 손 좀 봤습니다.",
            "수리하러 갔죠. 누가 해도 할 일이었잖아요?",
        },

        ["owl|MoveReason.task"] = new[]
        {
            "{to}에서 {task}을/를 맡아 이동했습니다.",
            "{task} 때문에 자리를 옮겼습니다.",
        },
        ["cat|MoveReason.task"] = new[]
        {
            "{task} 하러요. 그게 제 일이라서요.",
            "{to}에서 {task} 있었어요.",
        },
        ["jellyfish|MoveReason.task"] = new[]
        {
            "{task} 때문에요... {to}으로/로 옮겼어요.",
            "그, {to}에서 {task} 해야 해서요.",
        },
        ["rabbit|MoveReason.task"] = new[]
        {
            "{task} 하러 갔어요! 저 그거 맡았거든요.",
            "{to}에서 {task} 있었어요!",
        },
        ["crow|MoveReason.task"] = new[]
        {
            "{task} 수행을 위해 이동했습니다.",
            "{to}, {task}. 그뿐입니다.",
        },
        ["fox|MoveReason.task"] = new[]
        {
            "{task} 때문이죠. 별거 아닙니다.",
            "{to}에 {task}이/가 남아 있었거든요.",
        },

        ["owl|MoveReason.check"] = new[]
        {
            "{to} 설비 상태가 마음에 걸려 직접 확인하러 갔습니다.",
            "이상이 있는지 확인차 {to}에 들렀습니다.",
        },
        ["cat|MoveReason.check"] = new[]
        {
            "{to} 상태 보러 갔어요. 그게 이상해서요.",
            "확인하러요. 그냥 두면 더 귀찮아지잖아요.",
        },
        ["jellyfish|MoveReason.check"] = new[]
        {
            "{to} 쪽이 좀 이상한 것 같아서... 확인하러 갔어요.",
            "그냥... 괜찮은지 보고 오려고요.",
        },
        ["rabbit|MoveReason.check"] = new[]
        {
            "{to} 좀 이상한 것 같아서 보러 갔어요!",
            "확인하러요! 궁금하면 바로 가봐야 하잖아요.",
        },
        ["crow|MoveReason.check"] = new[]
        {
            "{to} 상태 확인. 그 목적이었습니다.",
            "이상 여부를 직접 확인했습니다.",
        },
        ["fox|MoveReason.check"] = new[]
        {
            "{to} 상태가 영 좋지 않아 보여서요. 확인하러 갔습니다.",
            "잠깐 들여다보러 갔죠. 그게 문제가 되나요?",
        },

        ["fml|MoveReason.plain"] = new[]
        {
            "특별한 이유는 없었습니다. 잠시 자리를 옮긴 것뿐입니다.",
            "{to}에 잠깐 들렀다가 돌아왔습니다.",
        },
        ["sft|MoveReason.plain"] = new[]
        {
            "특별한 이유는 없었어요. 잠깐 옮긴 거예요.",
            "{to}에 잠깐 갔다 왔어요.",
        },

        // 방해자가 동선을 감출 때. 이유를 대지 않고 흐린다.
        ["owl|MoveReason.evasive"] = new[]
        {
            "…정확히 왜였는지는 지금 바로 떠오르지 않습니다.",
            "업무 중 한 이동이었습니다. 그 이상은 기억이 흐립니다.",
        },
        ["cat|MoveReason.evasive"] = new[]
        {
            "그걸 일일이 기억해야 하나요? 그냥 일하다 옮긴 거예요.",
            "이유요? 딱히 없는데요.",
        },
        ["jellyfish|MoveReason.evasive"] = new[]
        {
            "어... 그게, 왜 갔더라... 죄송해요, 잘 기억이 안 나요.",
            "그, 그냥... 그때는 그래야 할 것 같아서요...",
        },
        ["rabbit|MoveReason.evasive"] = new[]
        {
            "어? 음... 그냥 왔다 갔다 한 거예요. 별 이유 없었어요.",
            "그게... 기억이 잘 안 나요. 이상한가요?",
        },
        ["crow|MoveReason.evasive"] = new[]
        {
            "기억나지 않습니다.",
            "이동한 것은 맞습니다. 이유는 기록에 없습니다.",
        },
        ["fox|MoveReason.evasive"] = new[]
        {
            "글쎄요, 그건 왜 물으시죠? 그냥 다녀온 겁니다.",
            "이유까지 하나하나 기억하고 다니진 않아서요.",
        },

        // ── 그 시각 그 방에 있었던 이유 ─────────────────────────────
        ["fml|PresenceReason.task"] = new[]
        {
            "{room}에서 {task}을/를 하고 있었습니다.",
            "{task} 때문에 계속 {room}에 있었습니다.",
        },
        ["sft|PresenceReason.task"] = new[]
        {
            "{room}에서 {task} 하고 있었어요.",
            "{task} 때문에 계속 거기 있었어요.",
        },
        ["fml|PresenceReason.assigned"] = new[]
        {
            "{room}이/가 제 배치 자리였습니다.",
            "거기가 오늘 제 자리입니다. 계속 {room}에 있었습니다.",
        },
        ["sft|PresenceReason.assigned"] = new[]
        {
            "거기가 제 자리잖아요. {room}에 있었어요.",
            "{room}이/가 오늘 제 배치였어요.",
        },
        ["fml|PresenceReason.check"] = new[]
        {
            "{room} 상태를 확인하고 있었습니다.",
            "이상이 없는지 보러 {room}에 들어가 있었습니다.",
        },
        ["sft|PresenceReason.check"] = new[]
        {
            "{room} 상태 보고 있었어요.",
            "이상 없나 보러 들어가 있었어요.",
        },
        ["fml|PresenceReason.evasive"] = new[]
        {
            "…거기 있었던 건 맞습니다. 이유는 지금 설명하기 어렵습니다.",
            "잠깐 지나간 것뿐입니다.",
        },
        ["sft|PresenceReason.evasive"] = new[]
        {
            "거기 있었던 건 맞는데요... 그냥 지나간 거예요.",
            "딱히 이유는 없었어요. 잠깐이었고요.",
        },

        // ── 그때 같이 있던 사람 ────────────────────────────────────
        ["owl|Companion.with"] = new[]
        {
            "{who} 직원이 같은 자리에 있었습니다. 확인해 보셔도 됩니다.",
            "{who} 직원과 함께 있었습니다.",
        },
        ["cat|Companion.with"] = new[]
        {
            "{who} 씨 있었어요. 물어보시든가요.",
            "{who} 씨랑 같이 있었는데요.",
        },
        ["jellyfish|Companion.with"] = new[]
        {
            "{who} 씨도 있었어요. 물어보시면 아실 거예요...",
            "네, {who} 씨가 옆에 있었어요.",
        },
        ["rabbit|Companion.with"] = new[]
        {
            "{who} 씨요! 같이 있었어요. 물어보세요!",
            "네! {who} 씨가 봤을 거예요.",
        },
        ["crow|Companion.with"] = new[]
        {
            "{who} 직원. 같은 자리에 있었습니다.",
            "{who}. 확인 가능합니다.",
        },
        ["fox|Companion.with"] = new[]
        {
            "{who} 씨도 있었죠. 절 봤을 텐데요?",
            "{who} 씨한테 물어보시면 되겠네요.",
        },

        ["owl|Companion.alone"] = new[]
        {
            "혼자였습니다. 증명해 줄 사람은 없습니다.",
            "그 자리에는 저 혼자 있었습니다.",
        },
        ["cat|Companion.alone"] = new[]
        {
            "혼자였어요. 그게 문제인가요?",
            "아뇨, 저뿐이었어요.",
        },
        ["jellyfish|Companion.alone"] = new[]
        {
            "혼자... 혼자 있었어요. 그래서 더 무서웠고요.",
            "아무도 없었어요...",
        },
        ["rabbit|Companion.alone"] = new[]
        {
            "혼자 있었어요! 아무도 안 왔어요.",
            "저 혼자였어요. 좀 심심했어요.",
        },
        ["crow|Companion.alone"] = new[]
        {
            "혼자였습니다.",
            "동행 없음.",
        },
        ["fox|Companion.alone"] = new[]
        {
            "혼자였습니다. 아쉽게도 증인은 없네요.",
            "저 혼자요. 그게 불리하게 들리나요?",
        },

        // ── 그 뒤에 어디로 갔는가 ──────────────────────────────────
        ["fml|NextLocation.moved"] = new[]
        {
            "일을 마치고 {next}으로/로 이동했습니다.",
            "{next}으로/로 갔습니다. 그 뒤로는 계속 거기 있었습니다.",
        },
        ["sft|NextLocation.moved"] = new[]
        {
            "끝내고 {next}으로/로 갔어요.",
            "{next}으로/로 옮겼어요. 그 뒤엔 쭉 거기 있었고요.",
        },
        ["fml|NextLocation.stayed"] = new[]
        {
            "그 뒤로는 계속 {room}에 있었습니다.",
            "옮기지 않았습니다. 근무가 끝날 때까지 그 자리였습니다.",
        },
        ["sft|NextLocation.stayed"] = new[]
        {
            "그 뒤론 계속 {room}에 있었어요.",
            "안 옮겼어요. 끝까지 거기 있었어요.",
        },
        ["fml|NextLocation.evasive"] = new[]
        {
            "…그 뒤의 동선은 정확히 말씀드리기 어렵습니다.",
            "몇 군데 돌았습니다. 순서까지는 기억나지 않습니다.",
        },
        ["sft|NextLocation.evasive"] = new[]
        {
            "그 뒤엔... 여기저기 좀 돌았어요. 순서는 잘 모르겠고요.",
            "어디부터였는지는 기억이 잘 안 나요.",
        },

        // ── 거기서 무엇을 했는가 ───────────────────────────────────
        ["fml|ActionThere.task"] = new[]
        {
            "{task}을/를 처리했습니다.",
            "{room}에서 {task}을/를 끝내고 나왔습니다.",
        },
        ["sft|ActionThere.task"] = new[]
        {
            "{task} 했어요.",
            "{task} 끝내고 나왔어요.",
        },
        ["fml|ActionThere.repair"] = new[]
        {
            "고장난 설비를 손봤습니다.",
            "{room} 수리 작업을 했습니다.",
        },
        ["sft|ActionThere.repair"] = new[]
        {
            "고장난 거 고쳤어요.",
            "{room} 수리했어요.",
        },
        ["fml|ActionThere.check"] = new[]
        {
            "계기와 설비 상태만 확인하고 나왔습니다.",
            "특별히 한 건 없습니다. 상태만 봤습니다.",
        },
        ["sft|ActionThere.check"] = new[]
        {
            "상태만 보고 나왔어요.",
            "딱히 한 건 없어요. 확인만 했어요.",
        },
        ["fml|ActionThere.evasive"] = new[]
        {
            "…특별히 기억에 남는 건 없습니다.",
            "잠깐 있다 나왔습니다. 그게 전부입니다.",
        },
        ["sft|ActionThere.evasive"] = new[]
        {
            "별거 안 했어요. 잠깐 있다 나왔어요.",
            "기억에 남을 만한 건 없는데요.",
        },

        // ── 그 사고를 알고 있었는가 ────────────────────────────────
        ["owl|IncidentKnown.direct"] = new[]
        {
            "네. {room}에 있었기 때문에 직접 봤습니다.",
            "알고 있습니다. 그 자리에 있었습니다.",
        },
        ["cat|IncidentKnown.direct"] = new[]
        {
            "네, 봤어요. 제가 거기 있었으니까요.",
            "알죠. 눈앞에서 났는데요.",
        },
        ["jellyfish|IncidentKnown.direct"] = new[]
        {
            "네... 봤어요. 바로 앞에서요.",
            "알아요. 제가 {room}에 있었어요...",
        },
        ["rabbit|IncidentKnown.direct"] = new[]
        {
            "네! 봤어요! 제가 거기 있었거든요.",
            "알아요! 바로 앞에서 났어요!",
        },
        ["crow|IncidentKnown.direct"] = new[]
        {
            "알고 있습니다. 직접 목격했습니다.",
            "{room}. 현장에 있었습니다.",
        },
        ["fox|IncidentKnown.direct"] = new[]
        {
            "알죠. 제 눈앞에서 났으니까요.",
            "봤습니다. 꽤 요란했죠.",
        },

        ["owl|IncidentKnown.indirect"] = new[]
        {
            "소리는 들었습니다. 다만 직접 보지는 못했습니다.",
            "옆 작업실에 있어서 소리만 들었습니다. 원인은 모릅니다.",
        },
        ["cat|IncidentKnown.indirect"] = new[]
        {
            "소리만 들었어요. 뭔지까지는 몰라요.",
            "들리긴 했는데 보진 못했어요.",
        },
        ["jellyfish|IncidentKnown.indirect"] = new[]
        {
            "소리는 들었어요... 근데 직접 본 건 아니라서요.",
            "뭔가 나긴 났는데... 뭔지는 모르겠어요.",
        },
        ["rabbit|IncidentKnown.indirect"] = new[]
        {
            "소리 들었어요! 근데 직접 본 건 아니에요.",
            "쿵 하는 건 들었는데 뭔지는 몰라요.",
        },
        ["crow|IncidentKnown.indirect"] = new[]
        {
            "소리만 확인했습니다. 원인은 보지 못했습니다.",
            "청취만 했습니다. 시각 확인은 없습니다.",
        },
        ["fox|IncidentKnown.indirect"] = new[]
        {
            "소리는 들었죠. 본 건 아니라 뭐라 말씀드리긴 어렵네요.",
            "벽 너머로 들었습니다. 그 이상은 모릅니다.",
        },

        ["fml|IncidentKnown.none"] = new[]
        {
            "그 사고는 지금 처음 듣습니다.",
            "몰랐습니다. 제가 있던 쪽에서는 아무 신호도 없었습니다.",
        },
        ["sft|IncidentKnown.none"] = new[]
        {
            "그건 처음 듣는데요.",
            "몰랐어요. 제가 있던 데선 아무것도 없었어요.",
        },

        // ── 그 시각 어디에 있었는가 ────────────────────────────────
        ["owl|WhereAtIncident.any"] = new[]
        {
            "{room}에 있었습니다. 배치받은 자리 그대로였습니다.",
            "{time}경이면 {room}입니다.",
        },
        ["cat|WhereAtIncident.any"] = new[]
        {
            "{room}이요/요. 계속 거기 있었어요.",
            "{room}에 있었어요. 그게 다예요.",
        },
        ["jellyfish|WhereAtIncident.any"] = new[]
        {
            "{room}에 있었어요... 계속요.",
            "저는 {room}이요/요. 안 움직였어요.",
        },
        ["rabbit|WhereAtIncident.any"] = new[]
        {
            "{room}에 있었어요! 계속 거기서 일했어요.",
            "{room}이요/요! 확인해 보셔도 돼요.",
        },
        ["crow|WhereAtIncident.any"] = new[]
        {
            "{room}입니다.",
            "{time}경, {room}. 이동 없었습니다.",
        },
        ["fox|WhereAtIncident.any"] = new[]
        {
            "{room}에 있었죠. 제 일 하고 있었고요.",
            "{room}입니다. 왜, 아니라는 기록이라도 있나요?",
        },

        // ── 사고 직전에 무엇을 하고 있었는가 ───────────────────────
        ["fml|BeforeIncident.task"] = new[]
        {
            "{room}에서 {task}을/를 하고 있었습니다.",
            "그 직전까지 {task} 중이었습니다.",
        },
        ["sft|BeforeIncident.task"] = new[]
        {
            "{room}에서 {task} 하고 있었어요.",
            "그 직전까지 {task} 중이었어요.",
        },
        ["fml|BeforeIncident.moved"] = new[]
        {
            "{prev}에서 {room}으로/로 막 넘어온 참이었습니다.",
            "직전에 {prev}에서 자리를 옮겼습니다.",
        },
        ["sft|BeforeIncident.moved"] = new[]
        {
            "{prev}에서 {room}으로/로 막 옮긴 참이었어요.",
            "직전에 {prev}에서 넘어왔어요.",
        },
        ["fml|BeforeIncident.plain"] = new[]
        {
            "평소와 다르지 않았습니다. {room}에서 자리를 지키고 있었습니다.",
            "특별한 건 없었습니다.",
        },
        ["sft|BeforeIncident.plain"] = new[]
        {
            "평소랑 똑같았어요. {room}에 있었고요.",
            "별거 없었어요.",
        },

        // ── 그 무렵 누구를 봤는가 ──────────────────────────────────
        ["fml|WhoSeenNear.someone"] = new[]
        {
            "{who} 직원을 봤습니다. 그 이상은 모릅니다.",
            "{who} 직원이 근처에 있었습니다.",
        },
        ["sft|WhoSeenNear.someone"] = new[]
        {
            "{who} 씨 봤어요. 그게 다예요.",
            "{who} 씨가 근처에 있었어요.",
        },
        ["fml|WhoSeenNear.none"] = new[]
        {
            "아무도 보지 못했습니다.",
            "그 무렵에는 주변에 사람이 없었습니다.",
        },
        ["sft|WhoSeenNear.none"] = new[]
        {
            "아무도 못 봤어요.",
            "그때는 주변에 아무도 없었어요.",
        },

        // ── 전후 이동 경로 ────────────────────────────────────────
        ["fml|RouteAround.full"] = new[]
        {
            "{prev}에서 {room}으로/로, 그 다음 {next}으로/로 이동했습니다.",
            "{prev} → {room} → {next} 순서였습니다.",
        },
        ["sft|RouteAround.full"] = new[]
        {
            "{prev}에서 {room}, 그 다음 {next}으로/로 갔어요.",
            "{prev} 다음에 {room}, 그리고 {next}이요/요.",
        },
        ["fml|RouteAround.short"] = new[]
        {
            "{room}에서 거의 움직이지 않았습니다.",
            "그 시간대에는 {room}에만 있었습니다.",
        },
        ["sft|RouteAround.short"] = new[]
        {
            "{room}에서 거의 안 움직였어요.",
            "그 시간엔 {room}에만 있었어요.",
        },
        ["fml|RouteAround.evasive"] = new[]
        {
            "순서까지는 정확히 말씀드리기 어렵습니다.",
            "여러 번 오갔습니다. 하나하나 기억나지는 않습니다.",
        },
        ["sft|RouteAround.evasive"] = new[]
        {
            "순서까지는 잘 모르겠어요.",
            "여러 번 왔다 갔다 해서요... 정확히는 기억이 안 나요.",
        },

        // ── 다른 직원의 증언이 사실인가 ────────────────────────────
        ["owl|ConfirmTestimony.admit"] = new[]
        {
            "사실입니다. 그 시각 {room}에 있었습니다.",
            "맞습니다. 숨길 이유가 없습니다.",
        },
        ["cat|ConfirmTestimony.admit"] = new[]
        {
            "맞아요. {room}에 있었어요.",
            "사실이에요. 그게 왜요?",
        },
        ["jellyfish|ConfirmTestimony.admit"] = new[]
        {
            "네... 맞아요. {room}에 있었어요.",
            "사실이에요. 숨긴 거 아니에요...",
        },
        ["rabbit|ConfirmTestimony.admit"] = new[]
        {
            "맞아요! {room}에 있었어요.",
            "네! 사실이에요.",
        },
        ["crow|ConfirmTestimony.admit"] = new[]
        {
            "사실입니다.",
            "{room}. 맞습니다.",
        },
        ["fox|ConfirmTestimony.admit"] = new[]
        {
            "맞습니다. 절 봤다면 제가 있었던 거죠.",
            "사실이에요. 굳이 부정할 일도 아니고요.",
        },

        ["owl|ConfirmTestimony.deny"] = new[]
        {
            "아닙니다. 그 시각 저는 {room}에 있었습니다.",
            "사실이 아닙니다. 잘못 보신 것 같습니다.",
        },
        ["cat|ConfirmTestimony.deny"] = new[]
        {
            "아닌데요. 저 그때 {room}에 있었어요.",
            "잘못 봤겠죠. 저 아니에요.",
        },
        ["jellyfish|ConfirmTestimony.deny"] = new[]
        {
            "네...? 아니에요, 저 그때 {room}에 있었어요...",
            "그, 그건 아닌 것 같은데요...",
        },
        ["rabbit|ConfirmTestimony.deny"] = new[]
        {
            "네?! 아니에요! 저 {room}에 있었어요!",
            "그거 잘못 본 거예요! 저 아니에요.",
        },
        ["crow|ConfirmTestimony.deny"] = new[]
        {
            "아닙니다. 저는 {room}에 있었습니다.",
            "사실과 다릅니다.",
        },
        ["fox|ConfirmTestimony.deny"] = new[]
        {
            "글쎄요, 잘못 보신 것 같은데요. 저는 {room}에 있었습니다.",
            "재미있네요. 저는 그 시간에 다른 데 있었는데요.",
        },

        // ── 앞서 한 진술을 다시 설명 ───────────────────────────────
        ["fml|Restate.same"] = new[]
        {
            "말씀드린 그대로입니다. {room}에 있었습니다.",
            "몇 번을 물으셔도 같습니다. {room}입니다.",
        },
        ["sft|Restate.same"] = new[]
        {
            "아까 말한 그대로예요. {room}에 있었어요.",
            "몇 번을 물으셔도 똑같아요. {room}이요/요.",
        },

        // ── 오늘의 기분 ───────────────────────────────────────────
        ["fml|MoodReason.incident"] = new[]
        {
            "{room} 쪽 일이 계속 마음에 걸려서 그렇게 적었습니다.",
            "오늘 시설이 조용하지 않았습니다. 그래서입니다.",
        },
        ["sft|MoodReason.incident"] = new[]
        {
            "{room} 쪽 일이 계속 신경 쓰여서요.",
            "오늘 시설이 좀 시끄러웠잖아요. 그래서예요.",
        },
        ["fml|MoodReason.plain"] = new[]
        {
            "특별한 이유는 없습니다. 근무 전 느낌 그대로 적었습니다.",
            "그냥 그랬습니다. 깊은 뜻은 없습니다.",
        },
        ["sft|MoodReason.plain"] = new[]
        {
            "딱히 이유는 없어요. 그냥 그랬어요.",
            "그냥 적은 거예요. 별 뜻 없어요.",
        },
        ["fml|MoodBefore.yes"] = new[]
        {
            "네. 출근할 때부터 그랬습니다.",
            "근무 전부터였습니다. 시설 일과는 상관없습니다.",
        },
        ["sft|MoodBefore.yes"] = new[]
        {
            "네, 올 때부터 그랬어요.",
            "근무 전부터요. 여기 일이랑은 상관없어요.",
        },
        ["fml|MoodBefore.no"] = new[]
        {
            "아닙니다. 근무 들어와서 그렇게 됐습니다.",
            "처음엔 괜찮았습니다. 중간부터 그랬습니다.",
        },
        ["sft|MoodBefore.no"] = new[]
        {
            "아뇨, 들어와서 그렇게 됐어요.",
            "처음엔 괜찮았는데 중간부터요.",
        },
        ["fml|MoodRelated.person"] = new[]
        {
            "{who} 직원과 마주친 뒤로 신경이 쓰였습니다.",
            "굳이 꼽자면 {who} 직원 쪽입니다.",
        },
        ["sft|MoodRelated.person"] = new[]
        {
            "{who} 씨 만나고 나서 좀 그랬어요.",
            "굳이 따지면 {who} 씨 쪽이요.",
        },
        ["fml|MoodRelated.incident"] = new[]
        {
            "{room} 쪽 일 때문입니다.",
            "사고 이후로 계속 그랬습니다.",
        },
        ["sft|MoodRelated.incident"] = new[]
        {
            "{room} 쪽 일 때문이에요.",
            "그 사고 나고부터 계속 그랬어요.",
        },
        ["fml|MoodRelated.none"] = new[]
        {
            "특정한 사람이나 사건과는 관계없습니다.",
            "아닙니다. 그런 건 없습니다.",
        },
        ["sft|MoodRelated.none"] = new[]
        {
            "특별히 누구 때문은 아니에요.",
            "아뇨, 그런 건 없어요.",
        },

        // ── 정확히 몇 시였는가 ─────────────────────────────────────
        ["fml|ExactTime.exact"] = new[]
        {
            "{time}경입니다.",
            "{time} 전후였습니다.",
        },
        ["sft|ExactTime.exact"] = new[]
        {
            "{time}쯤이요.",
            "{time} 전후였어요.",
        },
        ["fml|ExactTime.vague"] = new[]
        {
            "시계를 보지 않아 정확히는 모르겠습니다.",
            "정확한 시각까지는 기억나지 않습니다.",
        },
        ["sft|ExactTime.vague"] = new[]
        {
            "시계를 안 봐서 정확히는 몰라요.",
            "몇 시였는지까지는 기억이 안 나요.",
        },

        // ── 모순 추궁에 대한 대응 ──────────────────────────────────
        // 결백한 직원: 기록을 인정하고 착오를 인정한다. 알리바이를 새로 만들지 않는다.
        ["owl|Confront.honest"] = new[]
        {
            "…제 기억이 틀렸을 수 있습니다. 기록이 맞다면 그쪽이 사실일 겁니다.",
            "정정하겠습니다. 시간까지 정확히 기억하고 있진 않았습니다.",
        },
        ["cat|Confront.honest"] = new[]
        {
            "아, 그러네요. 제가 시간을 헷갈렸나 봐요.",
            "기록이 그렇다면 그게 맞겠죠. 숨길 생각은 없었어요.",
        },
        ["jellyfish|Confront.honest"] = new[]
        {
            "어...? 그, 그럼 제가 잘못 말한 거예요. 일부러 그런 건 아니에요...",
            "죄송해요, 제가 시간을 착각했나 봐요...",
        },
        ["rabbit|Confront.honest"] = new[]
        {
            "어?! 진짜요? 그럼 제가 헷갈린 거예요. 거짓말한 거 아니에요!",
            "아 맞다, 그때 잠깐 갔었네요. 까먹었어요!",
        },
        ["crow|Confront.honest"] = new[]
        {
            "기록이 맞습니다. 제 진술을 정정합니다.",
            "착오였습니다. 기록을 따르십시오.",
        },
        ["fox|Confront.honest"] = new[]
        {
            "아, 그랬나요. 제가 시간을 잘못 짚었나 봅니다.",
            "기록이 그렇다면야. 굳이 우길 일은 아니죠.",
        },

        // 방해자가 흐릴 때.
        ["owl|Confront.evasive"] = new[]
        {
            "…그 기록만으로 단정하시는 건 이르다고 봅니다.",
            "설명드릴 수 있습니다. 다만 지금 당장은 정리가 되지 않습니다.",
        },
        ["cat|Confront.evasive"] = new[]
        {
            "기록이 그렇다고 제가 뭘 했다는 건 아니잖아요.",
            "그 시간에 잠깐 지나갔을 수는 있죠. 그게 문제예요?",
        },
        ["jellyfish|Confront.evasive"] = new[]
        {
            "그, 그게... 제가 착각했나 봐요. 근데 이상한 짓은 안 했어요...",
            "저, 정말 아무것도 안 했어요... 기록이 왜 그런지는 저도 모르겠어요...",
        },
        ["rabbit|Confront.evasive"] = new[]
        {
            "에? 아니, 그게... 저 진짜 아무것도 안 했는데요?!",
            "그, 그럼 제가 잘못 말했나 봐요. 근데 진짜 별일 없었어요!",
        },
        ["crow|Confront.evasive"] = new[]
        {
            "기록은 기록입니다. 제 행동과는 별개입니다.",
            "그 이상은 답변하지 않겠습니다.",
        },
        ["fox|Confront.evasive"] = new[]
        {
            "재미있는 조합이네요. 그래서 제가 뭘 했다는 겁니까?",
            "기록 두 줄로 사람을 몰아가시는 건 좀 성급하지 않나요?",
        },

        // 방해자가 정면 부정할 때(불리한 기록이 이미 여러 건).
        ["owl|Confront.deny"] = new[]
        {
            "그 기록이 저를 가리키는 건 압니다. 그래도 제가 한 일은 아닙니다.",
            "아닙니다. 기록을 다시 확인해 주십시오.",
        },
        ["cat|Confront.deny"] = new[]
        {
            "저 아니에요. 몇 번을 물으셔도 같아요.",
            "그거 가지고 절 몰아가시는 건 무리예요.",
        },
        ["jellyfish|Confront.deny"] = new[]
        {
            "저 아니에요...! 정말 아니에요...",
            "아니에요, 진짜 아니에요. 믿어주세요...",
        },
        ["rabbit|Confront.deny"] = new[]
        {
            "저 아니에요! 진짜 아니라고요!",
            "아니에요! 왜 자꾸 저만 의심하세요?!",
        },
        ["crow|Confront.deny"] = new[]
        {
            "아닙니다.",
            "부정합니다. 그 이상 말할 것은 없습니다.",
        },
        ["fox|Confront.deny"] = new[]
        {
            "아닙니다. 그 두 기록으로는 아무것도 증명되지 않아요.",
            "부정하겠습니다. 근거가 더 있으시면 보여주시죠.",
        },

        // ── 마지막 안전망 ─────────────────────────────────────────
        ["fml|Unknown.any"] = new[] { "그건 잘 모르겠습니다." },
        ["sft|Unknown.any"] = new[] { "그건 잘 모르겠어요." },
    };
}
