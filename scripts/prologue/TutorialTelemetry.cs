using System.Collections.Generic;
using Godot;

namespace NSP.Prologue;

// DAY0 교육 블라인드 테스트용 기록(문서 §35 PHASE 6).
//
// 목적은 하나다 — 처음 하는 사람이 **어디서 막히는가**를 서너 명 테스트로 보는 것.
// 분석 시스템이 아니다. 판정에 전혀 관여하지 않고, 게임 상태를 하나도 바꾸지 않으며,
// 화면에도 아무것도 띄우지 않는다. 기록하는 것은 셋뿐이다.
//
//   · 단계별 체류 시간   Step("assign") → Step("materials") 사이의 초
//   · 되풀이한 실수      Count("assign_wrong")
//   · 도움말 재열람      Count("manual_open")
//
// 교육이 끝나면 콘솔에 한 번 찍고 user://tutorial_blindtest.txt 에 덧붙인다
// (내보낸 빌드에서도 남는다 — 그러라고 만든 것이다).
public static class TutorialTelemetry
{
    public const string LogPath = "user://tutorial_blindtest.txt";

    // 오래 머문 단계로 볼 기준(초). 보고서에서 이 줄에만 표식을 붙인다.
    private const double SlowStepSeconds = 60.0;

    public static bool IsRecording { get; private set; }

    private static readonly List<(string Id, double Seconds)> _steps = new();
    private static readonly Dictionary<string, int> _counts = new();
    private static string _current = "";
    private static ulong _stepStartMsec;
    private static ulong _runStartMsec;

    public static void Begin()
    {
        _steps.Clear();
        _counts.Clear();
        _current = "";
        _runStartMsec = Time.GetTicksMsec();
        _stepStartMsec = _runStartMsec;
        IsRecording = true;
    }

    // 지금까지의 단계를 닫고 새 단계를 연다. 같은 id 를 다시 열면 따로 한 줄로 쌓인다.
    public static void Step(string id)
    {
        if (!IsRecording) return;
        CloseCurrent();
        _current = id ?? "";
        _stepStartMsec = Time.GetTicksMsec();
    }

    // 되풀이한 실수 · 도움말 열람 같은 "횟수". 교육 중이 아니면 세지 않는다.
    public static void Count(string key)
    {
        if (!IsRecording || string.IsNullOrEmpty(key)) return;
        _counts[key] = _counts.GetValueOrDefault(key) + 1;
    }

    // 교육이 끝났다(또는 중단됐다). 한 번 찍고 기록을 닫는다.
    public static void End(bool completed)
    {
        if (!IsRecording) return;
        CloseCurrent();
        IsRecording = false;

        double total = (Time.GetTicksMsec() - _runStartMsec) / 1000.0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"── DAY0 교육 기록 · {Time.GetDatetimeStringFromSystem()} · " +
                      $"{(completed ? "완료" : "중단")} · 전체 {total:0}초");
        foreach (var (id, sec) in _steps)
            sb.AppendLine($"   {(sec >= SlowStepSeconds ? "!" : " ")} {id,-22} {sec,6:0.0}초");
        if (_counts.Count > 0)
        {
            var parts = new List<string>();
            foreach (var (k, v) in _counts) parts.Add($"{k} {v}");
            sb.AppendLine($"     {string.Join(" · ", parts)}");
        }

        string text = sb.ToString();
        GD.Print(text);
        Append(text);
    }

    private static void CloseCurrent()
    {
        if (_current.Length == 0) return;
        _steps.Add((_current, (Time.GetTicksMsec() - _stepStartMsec) / 1000.0));
        _current = "";
    }

    // 파일에 덧붙인다. 실패해도 조용히 넘어간다 — 기록이 안 남는다고 게임이 멈추면 안 된다.
    private static void Append(string text)
    {
        try
        {
            using var f = FileAccess.FileExists(LogPath)
                ? FileAccess.Open(LogPath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(LogPath, FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.SeekEnd();
            f.StoreString(text);
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"TutorialTelemetry: 기록을 남기지 못했습니다 — {e.Message}");
        }
    }
}
