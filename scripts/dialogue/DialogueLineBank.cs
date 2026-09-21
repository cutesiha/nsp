using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.Dialogue;

// 대사 뱅크 — 캐릭터별 "완성 문장 틀"을 data/dialogue/lines/*.txt 에서 읽는다.
//
// 코드에는 문장이 없다. 문장을 고치거나 늘리려면 텍스트 파일만 고치면 된다
// (게임을 다시 실행하면 반영된다 — 핫 리로드는 없다).
//
// 파일 형식
//   # 로 시작하는 줄        주석
//   @char <id>             이후 슬롯의 주인. 직원 id 또는 fml(격식체 공통) / sft(해요체 공통) / any
//   ## <slot>              슬롯 시작. 예: "## selfloc", "## MoveReason.ordered", "## mem.with"
//   - <문장>               그 슬롯의 후보 문장 하나. {room} {who} 같은 변수를 쓴다.
//
// 찾는 순서: 직원 id → 말투 공통(fml/sft) → any. 변수 뒤 조사는 반드시 "은/는" 처럼
// 슬래시로 쓴다 — KoreanParticle.Lint 가 읽을 때 검사해 경고를 띄운다.
//
// 내보내기 빌드는 .txt 를 자동으로 싣지 않는다 — export_presets.cfg 의 include_filter 에
// data/dialogue/lines/*.txt 가 들어 있어야 한다.
public static class DialogueLineBank
{
    public const string Folder = "res://data/dialogue/lines";

    // 직원이 추가돼도 코드를 고칠 필요는 없다. 폴더 스캔이 실패할 때만 쓰는 목록이다.
    private static readonly string[] KnownFiles =
        { "common", "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private static readonly Dictionary<string, string[]> _slots = new();
    private static readonly List<string> _problems = new();
    private static bool _loaded;

    public static IReadOnlyList<string> Problems { get { EnsureLoaded(); return _problems; } }

    // "owner|slot" 목록 — 검증 씬이 슬롯 누락을 볼 때 쓴다.
    public static IEnumerable<string> Keys { get { EnsureLoaded(); return _slots.Keys; } }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var files = new List<string>();
        using (var dir = DirAccess.Open(Folder))
        {
            if (dir != null)
            {
                dir.ListDirBegin();
                for (string name = dir.GetNext(); name != ""; name = dir.GetNext())
                    if (!dir.CurrentIsDir() && name.EndsWith(".txt")) files.Add($"{Folder}/{name}");
                dir.ListDirEnd();
            }
        }
        if (files.Count == 0) files.AddRange(KnownFiles.Select(n => $"{Folder}/{n}.txt"));

        foreach (string path in files.OrderBy(p => p))
            Parse(path);

        if (_slots.Count == 0)
            GD.PushError($"DialogueLineBank: {Folder} 에서 문장을 하나도 읽지 못했습니다. " +
                         "내보내기 빌드라면 export_presets.cfg 의 include_filter 를 확인하십시오.");
        foreach (string p in _problems) GD.PushWarning("DialogueLineBank: " + p);
    }

    public static void Reload()
    {
        _slots.Clear();
        _problems.Clear();
        _loaded = false;
        EnsureLoaded();
    }

    private static void Parse(string path)
    {
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;

        string owner = "any", slot = "";
        var current = new List<string>();
        int lineNo = 0;

        void Flush()
        {
            if (string.IsNullOrEmpty(slot)) return;
            string key = owner + "|" + slot;
            if (_slots.TryGetValue(key, out var old))
                current.InsertRange(0, old);   // 같은 슬롯이 두 번 나오면 합친다
            _slots[key] = current.ToArray();
            current = new List<string>();
        }

        while (!f.EofReached())
        {
            lineNo++;
            string raw = f.GetLine();
            string line = raw.Trim();
            if (line.Length == 0 || (line.StartsWith("#") && !line.StartsWith("##"))) continue;

            if (line.StartsWith("@char "))
            {
                Flush();
                owner = line[6..].Trim();
                slot = "";
                continue;
            }
            if (line.StartsWith("## "))
            {
                Flush();
                slot = line[3..].Trim();
                continue;
            }
            if (line.StartsWith("- "))
            {
                string text = line[2..].Trim();
                if (text.Length == 0) continue;
                if (string.IsNullOrEmpty(slot))
                {
                    _problems.Add($"{path}:{lineNo} 슬롯(## …) 없이 문장이 나왔습니다: {text}");
                    continue;
                }
                // 표현 조각(phrase.*)은 문장이 아니라 동사 줄기라 조사 검사를 하지 않는다.
                if (!slot.StartsWith("phrase."))
                {
                    string lint = KoreanParticle.Lint(text);
                    if (lint.Length > 0) _problems.Add($"{path}:{lineNo} [{owner}|{slot}] {lint}");
                }
                current.Add(text);
                continue;
            }
            _problems.Add($"{path}:{lineNo} 알 수 없는 줄: {line}");
        }
        Flush();
    }

    // 이 직원이 이 슬롯에서 쓸 수 있는 문장 후보. 없으면 빈 배열.
    public static string[] Get(string employeeId, string slot, bool formal)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(slot)) return System.Array.Empty<string>();
        if (_slots.TryGetValue($"{employeeId}|{slot}", out var own) && own.Length > 0) return own;
        if (_slots.TryGetValue($"{(formal ? "fml" : "sft")}|{slot}", out var reg) && reg.Length > 0) return reg;
        return _slots.TryGetValue($"any|{slot}", out var any) ? any : System.Array.Empty<string>();
    }

    public static bool Has(string employeeId, string slot, bool formal) => Get(employeeId, slot, formal).Length > 0;

    // 하나를 무작위로 — 동사 줄기 같은 표현 조각을 뽑을 때 쓴다(반복 회피 없음).
    public static string Any(string employeeId, string slot, bool formal)
    {
        var pool = Get(employeeId, slot, formal);
        return pool.Length == 0 ? "" : pool[(int)(GD.Randi() % (uint)pool.Length)];
    }
}
