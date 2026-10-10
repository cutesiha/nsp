using System.Collections.Generic;
using System.Text;
using Godot;

namespace NSP.View;

// GUIDE-0 대사 원고(`docs/NSP_PROLOGUE_RUNTIME.md` 의 `line:`)에서 쓰는 수동 강조색.
//
//   line: 직원들은 근무 중 /2스트레스/0를 받을 수 있습니다.
//
// `/1~/6` 은 그 지점부터 색을 바꾸고, `/0` 은 수동 지정을 끝낸다.
// `/0` 은 '흰색으로 칠하라'가 아니라 **기본색 + 기존 자동 강조(DialogueHighlight)로 복귀**다.
//
// 강조 우선순위
//   1. `/1~6` 으로 직접 지정한 범위          ← 그 안에서는 자동 강조를 다시 얹지 않는다
//   2. DialogueHighlight 자동 강조(직원 이름 = 고유색, 작업실 이름 = 호박색)
//   3. 기본 대사색
//
// 명령 두 글자는 화면에도 대화 기록에도 남지 않는다. 타이핑 길이·보이스·입 모양은
// 전부 PlainText() 기준으로 세야 한다(BBCode 태그나 명령이 글자 수에 섞이면 안 된다).
public static class GuideTextMarkup
{
    // ── 팔레트 ─────────────────────────────────────────────────────────
    // 색은 전부 여기 한 곳에만 둔다. 어두운 CRT 위에서 읽히는 밝기로 잡았다.
    /// <summary>/1 — 경고 · 위험 · 방해공작 · 실패 · 긴급.</summary>
    public static readonly Color Red = new(1f, 0.42f, 0.34f);
    /// <summary>/2 — 중요한 키워드 · 상태 · 추리상 주목할 내용.</summary>
    public static readonly Color Pink = new(1f, 0.60f, 0.85f);
    /// <summary>/3 — 작업 · 행동 지시 · 시설/설비. 작업실 자동 강조와 같은 호박색을 그대로 쓴다.</summary>
    public static readonly Color Orange = DialogueHighlight.RoomColor;
    /// <summary>/4 — 정상 · 성공 · 회복 · 안전.</summary>
    public static readonly Color Green = new(0.65f, 1f, 0.6f);
    /// <summary>/5 — 시스템 정보 · 기록 · CCTV · 데이터. 선택지 하늘색보다 확실히 파랗다.</summary>
    public static readonly Color Blue = new(0.42f, 0.68f, 1f);
    /// <summary>/6 — GUIDE 선택지에 쓰는 하늘색. 선택지 UI 도 이 값을 쓴다.</summary>
    public static readonly Color ChoiceCyan = new(0.55f, 0.95f, 1f);

    // 명령 번호 → 색. 0 은 색이 아니라 '수동 지정 종료'라 null 이다.
    private static Color? ColorOf(int n) => n switch
    {
        1 => Red,
        2 => Pink,
        3 => Orange,
        4 => Green,
        5 => Blue,
        6 => ChoiceCyan,
        _ => null,
    };

    // ── 바깥에서 쓰는 두 가지 ───────────────────────────────────────────

    // 화면·기록·타이핑 계산에 쓰는 순수 문장. 명령 두 글자만 빠진다.
    public static string PlainText(string authored)
    {
        if (string.IsNullOrEmpty(authored)) return "";
        if (authored.IndexOf('/') < 0) return authored;
        var sb = new StringBuilder(authored.Length);
        foreach (var (text, _) in Split(authored)) sb.Append(text);
        return sb.ToString();
    }

    // 화면 표시용 BBCode. 수동 범위는 그 색으로, 나머지는 기존 자동 강조로 칠한다.
    public static string RichText(string authored)
    {
        if (string.IsNullOrEmpty(authored)) return "";
        var sb = new StringBuilder(authored.Length + 32);
        foreach (var (text, manual) in Split(authored))
        {
            if (manual is not Color c) { sb.Append(DialogueHighlight.Colorize(text)); continue; }
            // 수동 범위 안에서는 자동 강조를 다시 얹지 않는다 — 직원 이름도 이 색으로 읽힌다.
            sb.Append("[color=#").Append(c.ToHtml(false)).Append(']')
              .Append(text.Replace("[", "[lb]"))
              .Append("[/color]");
        }
        return sb.ToString();
    }

    // ── 파서 ───────────────────────────────────────────────────────────

    // 문장을 "수동 색이 걸린 구간 / 자동 모드 구간"으로 자른다.
    // 각 문장은 언제나 자동 모드(null)에서 시작한다 — 앞 대사의 색이 넘어오지 않는다.
    private static List<(string Text, Color? Manual)> Split(string authored)
    {
        var segments = new List<(string, Color?)>();
        var sb = new StringBuilder(authored.Length);
        Color? current = null;
        int textFrom = 0;               // 여기서부터가 '글자' 다 — 앞 명령의 숫자와 구분한다

        for (int i = 0; i < authored.Length; i++)
        {
            if (!IsCommandAt(authored, i, textFrom)) { sb.Append(authored[i]); continue; }

            var next = ColorOf(authored[i + 1] - '0');
            if (sb.Length > 0) { segments.Add((sb.ToString(), current)); sb.Clear(); }
            current = next;
            i++;                        // 명령은 두 글자다 — 뒤 숫자까지 건너뛴다
            textFrom = i + 1;
        }
        if (sb.Length > 0) segments.Add((sb.ToString(), current));
        return segments;
    }

    // 정식 명령은 `/0` ~ `/6` 뿐이다. `/7`, `/x` 같은 건 명령으로 보지 않고 글자 그대로 둔다
    // (원고 한 글자 때문에 대사가 통째로 깨지면 안 된다).
    // 글자인 숫자 바로 뒤의 `/n` 도 명령이 아니다 — "3/4" 같은 표기가 잘려 나가지 않게.
    // (앞 명령의 숫자는 글자가 아니므로 `/1/0` 처럼 명령이 붙어 나와도 둘 다 명령으로 읽힌다.)
    private static bool IsCommandAt(string s, int i, int textFrom)
    {
        if (s[i] != '/' || i + 1 >= s.Length) return false;
        char d = s[i + 1];
        if (d < '0' || d > '6') return false;
        return i <= textFrom || !char.IsDigit(s[i - 1]);
    }
}
