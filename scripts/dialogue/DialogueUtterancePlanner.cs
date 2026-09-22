using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Dialogue;

// 캐릭터별 말투 설정(data/dialogue/voices/*.tres). 파일이 없으면 코드 기본값으로 돈다 —
// 리소스 스캔이 실패해도 대사가 통째로 사라지지 않게 한다.
public static class DialogueVoices
{
    private const string Folder = "res://data/dialogue/voices";

    private static readonly Dictionary<string, DialogueVoiceDef> _defs = new();
    private static bool _loaded;

    public static DialogueVoiceDef Get(string employeeId)
    {
        EnsureLoaded();
        if (_defs.TryGetValue(employeeId ?? "", out var d)) return d;
        return _defs.TryGetValue("cat", out var fallback) ? fallback : Fallback;
    }

    private static readonly DialogueVoiceDef Fallback = new() { EmployeeId = "", Formal = true };

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (string path in ResourceDir.ListFiles(Folder, ".tres"))
        {
            var res = GD.Load<DialogueVoiceDef>(path);
            if (res != null && !string.IsNullOrEmpty(res.EmployeeId)) _defs[res.EmployeeId] = res;
        }
        if (_defs.Count == 0)
            GD.PushWarning($"DialogueVoices: {Folder} 에서 말투 설정을 찾지 못했습니다.");
    }

    public static void Reload()
    {
        _defs.Clear();
        _loaded = false;
        EnsureLoaded();
    }
}

// "이 사실을 어떤 모양으로 말할까"를 정한다. 문장은 만들지 않는다.
//
// 조각마다 따로 주사위를 굴리지 않는다. 발화 형태 하나를 먼저 고르고, 그 형태가
// 요구하는 자리만 채운다. 그래서 "반응 + 반응", "핵심 + 핵심과 같은 말" 같은 조합이
// 애초에 만들어지지 않는다.
public static class DialogueUtterancePlanner
{
    public static DialogueUtterancePlan Plan(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceDef voice, IReadOnlyDictionary<string, string> vars, string coreSlot)
    {
        var up = new DialogueUtterancePlan
        {
            CoreSlot = coreSlot,
            Tone = ToneOf(ctx, plan),
            MaxSentences = voice.MaxSentences,
            MaxExclamations = voice.MaxExclamations,
        };

        // ── 상황이 무거우면 장식부터 줄인다 ────────────────────────────
        switch (up.Tone)
        {
            case SituationTone.Fearful:
                up.MaxExclamations = 0;
                up.MaxSentences = Mathf.Min(up.MaxSentences, 2);
                up.AllowPause = true;
                break;
            case SituationTone.Alarmed:
                // 놀란 순간에도 토끼 같은 사람은 느낌표가 튀어나온다 — 둘까지는 둔다.
                up.MaxExclamations = Mathf.Min(up.MaxExclamations, 2);
                up.AllowPause = voice.PauseChance > 0.2f;
                break;
            default:
                up.AllowPause = GD.Randf() < voice.PauseChance;
                break;
        }

        // ── 사실 정확성에 필요한 보정은 형태와 무관하게 남는다 ──────────
        if (plan.NeedsIndirectCaveat) up.CaveatSlots.Add("caveat.indirect");
        if (plan.NeedsUnknownCauseCaveat) up.CaveatSlots.Add("caveat.cause");

        // ── 짧게 끝내야 하는 답변 ──────────────────────────────────────
        bool terse = plan.Core is CoreKind.DispatchAccept or CoreKind.DispatchDecline
            or CoreKind.IncidentReport or CoreKind.Comply;
        if (terse)
        {
            up.Shape = UtteranceShape.CoreOnly;
            return up;
        }

        // 위치 답변에는 시간 표현을 붙이지 않는다 — 질문이 이미 시각을 말했고,
        // "아까 경비실입니다" 처럼 명사로 끝나는 답에 얹으면 사람 말이 아니게 된다.
        up.TimeWord = plan.Core == CoreKind.SelfLocation ? "" : TimeWord(ctx, plan, voice);
        up.EchoText = EchoText(ctx, plan, voice, vars);
        up.ReactionSlot = ReactionSlot(ctx, plan, up.Tone);
        up.ExtraSlot = ExtraSlot(ctx, plan, voice);
        up.BackQuestionSlot = "closer.back";
        up.RepeatSlot = "opener.repeat";

        up.Shape = ChooseShape(ctx, plan, voice, up);
        // 형태가 요구하지 않는 자리는 비워 둔다 — 남겨 두면 다시 붙는다.
        if (up.Shape != UtteranceShape.TopicEchoCore) up.EchoText = "";
        if (up.Shape != UtteranceShape.ReactionCore) up.ReactionSlot = "";
        if (up.Shape != UtteranceShape.CoreBackQuestion) up.BackQuestionSlot = "";
        if (up.Shape != UtteranceShape.RepeatCore) up.RepeatSlot = "";
        // 덧붙이는 정보는 CoreVolunteer 형태에서만. 단 방해자의 전략(생략·합리화 등)은
        // 표현이 아니라 의미라서 어느 형태에서든 남는다.
        if (up.Shape != UtteranceShape.CoreVolunteer && !IsDeceptionExtra(plan)) up.ExtraSlot = "";

        DialoguePatternMemory.RememberShape(ctx.EmployeeId, up.Shape);
        return up;
    }

    // --- 상황 톤 ----------------------------------------------------------

    private static SituationTone ToneOf(DialogueContext ctx, DialogueResponsePlan plan)
    {
        if (plan.Core == CoreKind.DenyAccusation || plan.Core == CoreKind.ChallengeResponse)
            return SituationTone.Defensive;

        var type = plan.IncidentType;
        if (plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect
            or CoreKind.IncidentReport or CoreKind.IncidentDetail)
        {
            if (type is LogEventType.Death or LogEventType.TabooViolation) return SituationTone.Fearful;
            return SituationTone.Alarmed;
        }

        // 사건을 직접 말하는 답변이 아니어도, 오늘 사람이 죽었다면 아무도 평소처럼 굴지 않는다.
        if (ctx.Subject != null)
        {
            if (ctx.Subject.Type is LogEventType.Death or LogEventType.TabooViolation)
                return SituationTone.Fearful;
            if (ctx.Subject.Type is LogEventType.Sabotage or LogEventType.TaskFailed
                or LogEventType.PowerOutage)
                return SituationTone.Concerned;
        }
        return SituationTone.Normal;
    }

    // --- 주제 되받기 ------------------------------------------------------

    // "그거요?" 같은 범용 반응 대신, 그 질문이 무엇을 물었는지를 그대로 짧게 되받는다.
    // 값이 없으면 빈 문자열 — 억지로 만들지 않는다.
    private static string EchoText(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceDef voice, IReadOnlyDictionary<string, string> vars)
    {
        // 꼬리질문처럼 명사 하나로 되받을 수 없는 질문은 통째로 되받는다("확실하냐고요?").
        string readySlot = plan.Core switch
        {
            CoreKind.CertaintyAnswer => "echo.certain",
            CoreKind.SeenConfirm => "echo.seen",
            CoreKind.HeardDetail => "echo.heard",
            CoreKind.SightingPlace => "echo.where",
            _ => "",
        };
        if (readySlot.Length > 0) return DialogueLineBank.Any(ctx.EmployeeId, readySlot, voice.Formal);

        string subject = EchoSubject(ctx, plan, vars);
        if (string.IsNullOrEmpty(subject)) return "";
        // 격식체는 "… 말씀이십니까?", 해요체는 "…요?" — 틀은 대사 뱅크의 echo.wrap 에 있다.
        string wrap = DialogueLineBank.Any(ctx.EmployeeId, "echo.wrap", voice.Formal);
        if (wrap.Length == 0) return "";
        return KoreanParticle.Resolve(wrap.Replace("{subject}", subject));
    }

    private static string EchoSubject(DialogueContext ctx, DialogueResponsePlan plan,
        IReadOnlyDictionary<string, string> vars)
    {
        string Var(string k) => vars.TryGetValue(k, out var v) ? v : "";

        switch (ctx.QuestionId)
        {
            case DialogueQuestions.Anomaly:
            case DialogueQuestions.GeneralAnomaly: return "이상현상";
            case DialogueQuestions.Suspicious: return "수상한 사람";
            case DialogueQuestions.Opinion:
                return string.IsNullOrEmpty(Var("target")) ? "" : Var("target") + " 씨";
            case DialogueQuestions.Accuse: return "저";
            case DialogueQuestions.GeneralStatus: return "지금";
            case DialogueQuestions.Where: return "그 시간";
        }

        // 꼬리질문은 무엇을 물었는지가 답변 종류에 그대로 드러난다.
        return plan.Core switch
        {
            CoreKind.WhoWasPresent => "같이 있던 사람",
            CoreKind.WitnessAnswer => "확인해 줄 사람",
            CoreKind.PreviousLocation => "그 전",
            CoreKind.NextAction => "그 다음",
            _ => "",
        };
    }

    // --- 반응 -------------------------------------------------------------

    // 반응은 질문이 놀랄 만할 때만 나온다. 평범한 질문에 "아, 네!" 를 붙이지 않는다.
    private static string ReactionSlot(DialogueContext ctx, DialogueResponsePlan plan, SituationTone tone)
    {
        // 의심받는 답변(DenyAccusation)은 핵심 문장 자체가 이미 반응이다 — 두 번 놀라지 않는다.
        if (plan.Core == CoreKind.ChallengeResponse) return "react.accused";
        if (plan.Core == CoreKind.DenyAccusation) return "";
        if (tone == SituationTone.Fearful) return "emotion.fear";
        if (tone == SituationTone.Alarmed) return "emotion.alarm";
        return "";
    }

    // --- 덧붙이는 한 마디 --------------------------------------------------

    private static bool IsDeceptionExtra(DialogueResponsePlan plan) => plan.Deception
        is DeceptionMode.Justify or DeceptionMode.Minimize or DeceptionMode.Vague or DeceptionMode.Redirect;

    // 이 답변에 자연스럽게 붙일 수 있는 한 마디. 없으면 빈 값.
    private static string ExtraSlot(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceDef voice)
    {
        // ① 방해자의 전략은 표현이 아니라 의미다 — 형태와 무관하게 실린다.
        switch (plan.Deception)
        {
            case DeceptionMode.Justify when plan.Core == CoreKind.SelfLocation: return "support.justify";
            case DeceptionMode.Minimize when plan.Core == CoreKind.SelfLocation: return "support.minimize";
            case DeceptionMode.Vague when plan.Core != CoreKind.ChallengeResponse: return "support.vague";
            case DeceptionMode.Redirect: return "support.redirect";
        }
        if (!plan.AllowSupport) return "";

        // ② 확신이 낮으면 스스로 낮춰 말한다(양이 가장 자주).
        bool hedgeable = plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect
            or CoreKind.SuspiciousSighting or CoreKind.Opinion or CoreKind.SeenConfirm;
        if (hedgeable && plan.CaveatFree && (plan.Certainty == Certainty.Low
                                             || GD.Randf() < voice.HedgeChance))
            return "support.hedge";

        // ③ 묻지 않았지만 이 사람이라면 덧붙일 법한 정보.
        return plan.Core switch
        {
            CoreKind.SelfLocation when plan.MentionTask => "support.task",
            CoreKind.NoAnomaly => "volunteer.noanomaly",
            CoreKind.NoSighting => "volunteer.nosight",
            CoreKind.IncidentDirect or CoreKind.IncidentIndirect => "support.nothing",
            CoreKind.Opinion => "support.seen",
            CoreKind.StatusReport when plan.MentionTask => "support.taskname",
            CoreKind.SuspiciousSighting => "volunteer.sight",
            _ => "",
        };
    }

    // --- 발화 형태 --------------------------------------------------------

    private static UtteranceShape ChooseShape(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceDef voice, DialogueUtterancePlan up)
    {
        // 같은 질문을 다시 받았으면 그 사실부터 짚는다.
        if (plan.IsRepeat) return UtteranceShape.RepeatCore;

        var weights = new List<(UtteranceShape Shape, float W)>
        {
            (UtteranceShape.CoreOnly, 0.5f + voice.Directness * 1.6f - voice.Talkativeness * 0.6f),
        };

        if (!string.IsNullOrEmpty(up.ReactionSlot))
            weights.Add((UtteranceShape.ReactionCore,
                voice.ReactionIntensity * (up.Tone == SituationTone.Defensive ? 2.2f : 1.2f)));

        if (!string.IsNullOrEmpty(up.EchoText))
            weights.Add((UtteranceShape.TopicEchoCore, voice.QuestionEchoChance * 1.6f));

        if (!string.IsNullOrEmpty(up.ExtraSlot))
            weights.Add((UtteranceShape.CoreVolunteer, voice.VolunteerInfoChance * 1.6f));

        // 되묻기는 분위기가 무겁지 않을 때만. 사람이 죽은 직후에 농담하듯 되묻지 않는다.
        if (voice.BackQuestionChance > 0f && up.Tone is SituationTone.Normal or SituationTone.Defensive
            && plan.Core is not (CoreKind.SelfLocation or CoreKind.IncidentDirect))
            weights.Add((UtteranceShape.CoreBackQuestion, voice.BackQuestionChance * 1.4f));

        // 최근에 쓴 형태는 가중치를 크게 낮춘다 — 같은 모양이 연달아 나오지 않게.
        float total = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            float w = Mathf.Max(0.02f, weights[i].W);
            if (DialoguePatternMemory.ShapeUsedRecently(ctx.EmployeeId, weights[i].Shape)) w *= 0.25f;
            weights[i] = (weights[i].Shape, w);
            total += w;
        }

        float roll = GD.Randf() * total;
        foreach (var (shape, w) in weights)
        {
            roll -= w;
            if (roll <= 0f) return shape;
        }
        return UtteranceShape.CoreOnly;
    }

    // --- 시간 표현 --------------------------------------------------------

    private static string TimeWord(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceDef voice) => plan.Time switch
    {
        TimeRef.Vague => DialogueLineBank.Any(ctx.EmployeeId, "time.vague", voice.Formal),
        TimeRef.Exact => KoreanParticle.TimePhrase(plan.IncidentTimeSeconds, voice.Formal),
        _ => "",
    };

}
