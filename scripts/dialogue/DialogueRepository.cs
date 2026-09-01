using System;
using System.Collections.Generic;
using Godot;

namespace NSP.Dialogue;

// docs/NSP_DIALOGUE_RUNTIME.md 를 읽어 전화 대사를 제공한다.
//  - 원본(야간근무지침_상황별_예시_대사_모음.md)의 문장을 그대로 옮긴 파일이며 여기서 수정하지 않는다.
//  - API 연결 여부와 무관하게 이 데이터만으로 통화가 완결되어야 한다.
//  - 파일이 없거나 파싱이 깨져도 게임이 멈추지 않도록 최소 폴백을 반환한다.
public static class DialogueRepository
{
    // 런타임 데이터 파일. 웹 빌드까지 포함하려면 export_presets.cfg 의 include_filter 에 추가한다.
    private const string RuntimePath = "res://docs/NSP_DIALOGUE_RUNTIME.md";

    // 캐릭터가 먼저 거는 전화(사고/비명/정전/목격/인터뷰).
    public const string EventAccidentNearby = "accident_nearby";
    public const string EventScreamNextRoom = "scream_next_room";
    public const string EventBlackout = "blackout";
    public const string EventWitnessSuspicious = "witness_suspicious";
    public const string EventInterviewSuspected = "interview_suspected";
    // 플레이어가 먼저 거는 일반 통화.
    public const string EventGeneralCall = "general_call";

    public sealed class Choice
    {
        public string Text = "";
        public string Reply = "";
    }

    public sealed class EventLine
    {
        public string Opening = "";
        public readonly List<Choice> Choices = new();
    }

    public sealed class QA
    {
        public string Question = "";
        public string Answer = "";
    }

    private sealed class GeneralLine
    {
        public string Greeting = "";
        public readonly List<QA> Questions = new();
    }

    // eventId -> (employeeId -> EventLine)
    private static readonly Dictionary<string, Dictionary<string, EventLine>> _events = new();
    // employeeId -> GeneralLine
    private static readonly Dictionary<string, GeneralLine> _general = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            Parse();
        }
        catch (Exception e)
        {
            GD.PushWarning($"DialogueRepository: 파싱 실패, 폴백 대사를 사용합니다 — {e.Message}");
        }
    }

    private static void Parse()
    {
        if (!FileAccess.FileExists(RuntimePath))
        {
            GD.PushWarning($"DialogueRepository: {RuntimePath} 를 찾지 못했습니다. 폴백 대사를 사용합니다.");
            return;
        }

        using var f = FileAccess.Open(RuntimePath, FileAccess.ModeFlags.Read);
        if (f == null) return;

        string curEvent = null;
        EventLine ev = null;
        GeneralLine gl = null;
        Choice pendingChoice = null;
        QA pendingQa = null;

        while (!f.EofReached())
        {
            string line = f.GetLine().Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            if (line.StartsWith("@event "))
            {
                curEvent = line[7..].Trim();
                ev = null; gl = null; pendingChoice = null; pendingQa = null;
                continue;
            }
            if (line.StartsWith("@char "))
            {
                string curChar = line[6..].Trim();
                pendingChoice = null; pendingQa = null;
                if (curEvent == EventGeneralCall)
                {
                    gl = new GeneralLine();
                    _general[curChar] = gl;
                    ev = null;
                }
                else if (!string.IsNullOrEmpty(curEvent))
                {
                    ev = new EventLine();
                    if (!_events.TryGetValue(curEvent, out var byChar))
                        _events[curEvent] = byChar = new Dictionary<string, EventLine>();
                    byChar[curChar] = ev;
                    gl = null;
                }
                continue;
            }

            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            string key, val;
            if (colon >= 0) { key = line[..colon]; val = line[(colon + 2)..]; }
            else if (line.EndsWith(":")) { key = line[..^1]; val = ""; }
            else continue;

            switch (key)
            {
                case "opening": if (ev != null) ev.Opening = val; break;
                case "greeting": if (gl != null) gl.Greeting = val; break;
                case "choice":
                    if (ev != null) { pendingChoice = new Choice { Text = val }; ev.Choices.Add(pendingChoice); }
                    break;
                case "reply":
                    if (pendingChoice != null) { pendingChoice.Reply = val; pendingChoice = null; }
                    break;
                case "q":
                    if (gl != null) { pendingQa = new QA { Question = val }; gl.Questions.Add(pendingQa); }
                    break;
                case "a":
                    if (pendingQa != null) { pendingQa.Answer = val; pendingQa = null; }
                    break;
            }
        }
    }

    // --- 캐릭터가 먼저 거는 전화 ------------------------------------------
    public static EventLine GetEvent(string eventId, string employeeId)
    {
        EnsureLoaded();
        return _events.TryGetValue(eventId, out var byChar) && byChar.TryGetValue(employeeId, out var line)
            ? line : null;
    }

    // --- 플레이어가 먼저 거는 일반 통화 ---------------------------------
    public static string Greeting(string employeeId)
    {
        EnsureLoaded();
        return _general.TryGetValue(employeeId, out var gl) && gl.Greeting.Length > 0
            ? gl.Greeting
            : "네, 무슨 일이세요?";
    }

    public static IReadOnlyList<QA> GeneralQuestions(string employeeId)
    {
        EnsureLoaded();
        return _general.TryGetValue(employeeId, out var gl) ? gl.Questions : Array.Empty<QA>();
    }

    public static string GeneralAnswer(string employeeId, int index)
    {
        var qs = GeneralQuestions(employeeId);
        return index >= 0 && index < qs.Count ? qs[index].Answer : "…알겠습니다.";
    }
}
