using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Debug;

// 성능 측정의 **단 하나의 수집기**. 개발자 전용이고 기본은 꺼져 있다.
//
// 규칙 세 가지.
//   ① 꺼져 있으면 비용이 거의 0 이다 — Running 검사 한 번으로 빠져나간다.
//   ② **엔진이 실제로 주는 값만** 적는다. Godot 4.7 이 내주지 않는 값(GPU 프레임 시간
//      등)은 추정하지 않고 "측정 불가" 로 남긴다. 성능 작업에서 지어낸 숫자는 해롭다.
//   ③ 측정 자체의 비용도 잰다(OverheadMs). 그 값이 크면 측정을 믿을 수 없다.
//
// 쓰는 법
//   PerfStats.Begin("상황 이름");           // 구간 시작
//   using (PerfStats.Scope(PerfSpan.SimTick)) { ... }   // 코드 구간 시간
//   PerfStats.Mark("jumpscare");            // 순간 표시(지연 측정의 시작점)
//   var r = PerfStats.End();                // 결과
public static class PerfStats
{
    // 재는 구간의 종류. enum 으로 두어 이름 문자열을 매 프레임 만들지 않는다.
    public enum Span
    {
        SimTick,        // FacilitySimulation.Tick
        ControlRoom,    // ControlRoom3DController._Process
        CctvWorld,      // FacilityCctvWorld._Process
        CctvMonitor,    // CCTVMonitorView._Process
        MonsterLoad,    // MonsterActor.Use — 모델 읽기 · 생성
        Snapshot,       // CctvSnapshotRecorder.Grab — GPU → CPU 읽기
        Count,
    }

    public static bool Running { get; private set; }
    public static string Label { get; private set; } = "";

    // ── 프레임 표본 ──────────────────────────────────────────────────
    private static readonly List<double> _frameMs = new(4096);
    private static double _startedAt;
    private static long _gcAllocStart;
    private static readonly int[] _gcStart = new int[3];

    // 구간별 누적(ms)과 호출 횟수. 프레임당이 아니라 구간 전체의 합이다.
    private static readonly double[] _spanMs = new double[(int)Span.Count];
    private static readonly int[] _spanHits = new int[(int)Span.Count];
    private static readonly double[] _spanWorst = new double[(int)Span.Count];

    // 한 번짜리 사건(점프스케어 지연 등).
    private static readonly List<(string Name, double Ms)> _marks = new();
    private static readonly Dictionary<string, ulong> _pending = new();

    private static double _overheadMs;

    // ── 구간 ────────────────────────────────────────────────────────

    public static void Begin(string label)
    {
        _frameMs.Clear();
        _marks.Clear();
        _pending.Clear();
        Array.Clear(_spanMs);
        Array.Clear(_spanHits);
        Array.Clear(_spanWorst);
        _overheadMs = 0;
        Label = label ?? "";
        _gcAllocStart = GC.GetTotalAllocatedBytes();
        for (int i = 0; i < 3; i++) _gcStart[i] = GC.CollectionCount(i);
        _startedAt = Time.GetTicksUsec() / 1000.0;
        Running = true;
    }

    public static Result End()
    {
        Running = false;
        return Snapshot();
    }

    // 매 프레임 한 번. PerfHud 가 부른다.
    public static void Sample(double deltaSeconds)
    {
        if (!Running) return;
        ulong t0 = Time.GetTicksUsec();
        _frameMs.Add(deltaSeconds * 1000.0);
        _overheadMs += (Time.GetTicksUsec() - t0) / 1000.0;
    }

    // ── 코드 구간 ────────────────────────────────────────────────────
    //
    // struct 로 둔다 — class 면 구간마다 쓰레기가 생겨서 GC 측정 자체를 오염시킨다.
    public readonly struct Timer : IDisposable
    {
        private readonly Span _span;
        private readonly ulong _t0;
        internal Timer(Span span) { _span = span; _t0 = Time.GetTicksUsec(); }

        public void Dispose()
        {
            if (!Running) return;
            double ms = (Time.GetTicksUsec() - _t0) / 1000.0;
            int i = (int)_span;
            _spanMs[i] += ms;
            _spanHits[i]++;
            if (ms > _spanWorst[i]) _spanWorst[i] = ms;
        }
    }

    public static Timer Scope(Span span) => new(span);

    // ── 순간 표시 ────────────────────────────────────────────────────

    // 지연의 시작점. 같은 이름으로 MarkEnd 를 부르면 그 사이 시간이 남는다.
    public static void MarkBegin(string name)
    {
        if (!Running || string.IsNullOrEmpty(name)) return;
        _pending[name] = Time.GetTicksUsec();
    }

    public static void MarkEnd(string name)
    {
        if (!Running || !_pending.TryGetValue(name, out ulong t0)) return;
        _pending.Remove(name);
        _marks.Add((name, (Time.GetTicksUsec() - t0) / 1000.0));
    }

    // ── 결과 ────────────────────────────────────────────────────────

    public sealed class Result
    {
        public string Label = "";
        public int Frames;
        public double Seconds;
        public double AvgFps, AvgMs, MinFps;
        public double LowOnePercentFps;     // 1% Low — 가장 느린 1% 프레임의 평균 FPS
        public double P95Ms, P99Ms, WorstMs;
        public int Over50Ms, Over100Ms;
        public double ProcessMs, PhysicsMs; // 엔진이 보고하는 처리 시간
        public double OverheadMs;           // 측정 자체의 비용
        public long GcAllocBytes;
        public int[] GcCollections = new int[3];
        public double StaticMemMb, VideoMemMb;
        public long DrawCalls, Primitives, RenderObjects;
        public long Nodes, Objects, Orphans;
        public readonly List<(string Name, double Ms, int Hits, double Worst)> Spans = new();
        public readonly List<(string Name, double Ms)> Marks = new();

        public string Fps => Frames == 0 ? "N/A" : $"{AvgFps:0.0}";
    }

    public static Result Snapshot()
    {
        var r = new Result
        {
            Label = Label,
            Frames = _frameMs.Count,
            Seconds = Time.GetTicksUsec() / 1000.0 - _startedAt,
            OverheadMs = _overheadMs,
            GcAllocBytes = GC.GetTotalAllocatedBytes() - _gcAllocStart,
        };
        for (int i = 0; i < 3; i++) r.GcCollections[i] = GC.CollectionCount(i) - _gcStart[i];

        if (_frameMs.Count > 0)
        {
            var sorted = _frameMs.OrderBy(x => x).ToList();
            r.AvgMs = _frameMs.Average();
            r.AvgFps = r.AvgMs > 0 ? 1000.0 / r.AvgMs : 0;
            r.WorstMs = sorted[^1];
            r.MinFps = r.WorstMs > 0 ? 1000.0 / r.WorstMs : 0;
            r.P95Ms = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))];
            r.P99Ms = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];
            r.Over50Ms = _frameMs.Count(x => x >= 50.0);
            r.Over100Ms = _frameMs.Count(x => x >= 100.0);
            // 1% Low — 가장 느린 1% 프레임만 평균한다(평균 FPS 가 숨기는 끊김이 여기 보인다).
            int worstCount = Math.Max(1, sorted.Count / 100);
            double worstAvg = sorted.Skip(sorted.Count - worstCount).Average();
            r.LowOnePercentFps = worstAvg > 0 ? 1000.0 / worstAvg : 0;
        }

        r.ProcessMs = Mon(Performance.Monitor.TimeProcess) * 1000.0;
        r.PhysicsMs = Mon(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        r.StaticMemMb = Mon(Performance.Monitor.MemoryStatic) / 1048576.0;
        r.VideoMemMb = Mon(Performance.Monitor.RenderVideoMemUsed) / 1048576.0;
        r.DrawCalls = (long)Mon(Performance.Monitor.RenderTotalDrawCallsInFrame);
        r.Primitives = (long)Mon(Performance.Monitor.RenderTotalPrimitivesInFrame);
        r.RenderObjects = (long)Mon(Performance.Monitor.RenderTotalObjectsInFrame);
        r.Nodes = (long)Mon(Performance.Monitor.ObjectNodeCount);
        r.Objects = (long)Mon(Performance.Monitor.ObjectCount);
        r.Orphans = (long)Mon(Performance.Monitor.ObjectOrphanNodeCount);

        for (int i = 0; i < (int)Span.Count; i++)
            if (_spanHits[i] > 0)
                r.Spans.Add((((Span)i).ToString(), _spanMs[i], _spanHits[i], _spanWorst[i]));
        r.Marks.AddRange(_marks);
        return r;
    }

    private static double Mon(Performance.Monitor m)
    {
        try { return Performance.GetMonitor(m); }
        catch { return 0; }
    }

    // ── 보고서 ──────────────────────────────────────────────────────

    public static string Row(Result r) =>
        $"| {r.Label} | {r.Frames} | {r.AvgFps:0.0} | {r.LowOnePercentFps:0.0} | {r.MinFps:0.0} | " +
        $"{r.AvgMs:0.00} | {r.P95Ms:0.00} | {r.P99Ms:0.00} | {r.WorstMs:0.00} | " +
        $"{r.Over50Ms} | {r.Over100Ms} | {r.GcAllocBytes / 1048576.0:0.00} | {r.DrawCalls} |";

    public const string Header =
        "| 상황 | 프레임 | 평균 FPS | 1% Low | 최저 FPS | 평균 ms | P95 ms | P99 ms | 최악 ms | >50ms | >100ms | GC 할당 MB | draw call |\n" +
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|";

    public static string Detail(Result r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"- 측정 오버헤드 {r.OverheadMs:0.00} ms (구간 전체 합)");
        sb.AppendLine($"- 엔진 보고: process {r.ProcessMs:0.00} ms · physics {r.PhysicsMs:0.00} ms");
        sb.AppendLine($"- 메모리: static {r.StaticMemMb:0.0} MB · video {r.VideoMemMb:0.0} MB");
        sb.AppendLine($"- GC: gen0 {r.GcCollections[0]} · gen1 {r.GcCollections[1]} · gen2 {r.GcCollections[2]}");
        sb.AppendLine($"- 노드 {r.Nodes} · 오브젝트 {r.Objects} · 고아 노드 {r.Orphans}");
        sb.AppendLine($"- 렌더: draw call {r.DrawCalls} · 오브젝트 {r.RenderObjects} · 프리미티브 {r.Primitives}");
        foreach (var (name, ms, hits, worst) in r.Spans)
            sb.AppendLine($"- `{name}` {ms:0.00} ms / {hits}회 (평균 {ms / hits:0.000} · 최악 {worst:0.00})");
        foreach (var (name, ms) in r.Marks)
            sb.AppendLine($"- **{name}** {ms:0.00} ms");
        return sb.ToString();
    }
}
