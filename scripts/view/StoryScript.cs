using System;
using System.Collections.Generic;
using Godot;

namespace NSP.View;

// docs/NSP_MAIN_STORY_DAY1_DAY5.md 를 읽어 DAY1~5 스토리 컷인 비트를 제공한다.
//
// DialogueRepository · PrologueScript 와 같은 규약이다.
//  - 문구는 전부 그 문서에만 있고 코드에는 없다. 문서를 고치면 그게 곧 게임이다.
//  - 파일이 없거나 파싱이 깨져도 게임이 멈추지 않는다(빈 목록 + 경고 로그).
//  - 내보낸 빌드에 담으려면 export_presets.cfg 의 include_filter 에 넣어야 한다
//    (.md 는 리소스가 아니라 export_filter="all_resources" 로 담기지 않는다).
//
// 그 문서는 설계서이자 데이터 파일이라 산문과 표가 잔뜩 섞여 있다. 그래서 파서는
// **모르는 줄을 전부 흘린다.** 다만 §4 의 형식 설명 안에 예시로 적힌 `@beat <id>` 까지
// 비트로 읽어 버리면 안 되므로, 비트 id 가 소문자·숫자·밑줄이 아니면 그 블록을 통째로 버린다.
public static class StoryScript
{
    public const string RuntimePath = "res://docs/NSP_MAIN_STORY_DAY1_DAY5.md";

    private static readonly List<StoryBeatEntry> _entries = new();
    private static bool _loaded;

    public static IReadOnlyList<StoryBeatEntry> Entries
    {
        get { EnsureLoaded(); return _entries; }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try { Parse(); }
        catch (Exception e) { GD.PushWarning($"StoryScript: 파싱 실패 — {e.Message}"); }
    }

    // 테스트에서 문서를 다시 읽게 한다.
    public static void Reload()
    {
        _entries.Clear();
        _loaded = false;
        EnsureLoaded();
    }

    private static void Parse()
    {
        if (!FileAccess.FileExists(RuntimePath))
        {
            GD.PushError($"StoryScript: {RuntimePath} 를 찾지 못했습니다. DAY1~5 스토리가 통째로 " +
                "건너뛰어집니다. 내보내기 빌드라면 export_presets.cfg 의 include_filter 를 확인하십시오.");
            return;
        }
        using var f = FileAccess.Open(RuntimePath, FileAccess.ModeFlags.Read);
        if (f == null) return;

        StoryBeatEntry entry = null;
        while (!f.EofReached())
        {
            string line = f.GetLine().Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            if (line.StartsWith("@beat"))
            {
                entry = null;
                string id = line[5..].Trim();
                // §4 의 형식 설명에 든 "@beat <id>" 같은 예시는 여기서 걸러진다.
                if (!IsBeatId(id)) continue;
                entry = new StoryBeatEntry { Beat = { BeatId = id } };
                _entries.Add(entry);
                continue;
            }
            if (entry == null) continue;

            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim().ToLowerInvariant();
            string value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "day":
                    // any = 어느 날이든(반응 비트). 숫자가 아니면 0 으로 두고 any 로 읽는다.
                    entry.Day = int.TryParse(value, out int d) ? d : 0;
                    break;
                case "need":
                    entry.Need = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case "when":
                    entry.When = value.ToLowerInvariant();
                    break;
                case "kind":
                    entry.Kind = value.ToLowerInvariant() switch
                    {
                        "core" => StoryBeatKind.Core,
                        "react" => StoryBeatKind.React,
                        _ => StoryBeatKind.Pool,
                    };
                    break;
                case "say":
                    AddLine(entry, value);
                    break;
            }
        }

        // 대사가 한 줄도 없는 블록은 버린다(형식 설명에서 걸러진 찌꺼기 포함).
        _entries.RemoveAll(e => e.Beat.Lines.Count == 0);
        if (_entries.Count == 0)
            GD.PushWarning($"StoryScript: {RuntimePath} 에서 비트를 하나도 읽지 못했습니다.");
    }

    // say: <직원id> | <표정> | <L|R> | <대사>
    private static void AddLine(StoryBeatEntry entry, string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 4) return;
        string speaker = parts[0].Trim();
        string expression = parts[1].Trim();
        string side = parts[2].Trim();
        // 대사 안에 '|' 가 들어가면 구분자와 섞인다(문서 §4 가 금지). 그래도 들어왔다면
        // 넷째 칸부터 전부 대사로 되붙여 글자를 잃지 않는다.
        string text = string.Join("|", parts[3..]).Trim();
        if (speaker.Length == 0 || text.Length == 0) return;

        entry.Beat.Add(speaker, text,
            side.Equals("R", StringComparison.OrdinalIgnoreCase) ? CutinSide.Right : CutinSide.Left,
            expression.Equals("normal", StringComparison.OrdinalIgnoreCase) ? "" : expression);
    }

    // 비트 id — 소문자 · 숫자 · 밑줄만. 산문 속 예시(`<id>` 등)를 걸러내는 체다.
    private static bool IsBeatId(string id)
    {
        if (id.Length == 0) return false;
        foreach (char c in id)
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')) return false;
        return true;
    }
}
