using System;

namespace NSP.View;

// 비트가 "언제 · 누구와 함께" 재생되는가. 대사 자체는 StoryBeat 가 가진다.
//
// StoryBeat 에 이 필드들을 직접 붙이지 않는다 — DAY0 교육 비트(@beat, PrologueScript)는
// day/need/when 이 없고, 거기에 늘 비어 있는 칸이 생기면 "왜 비어 있는가"를 매번 설명해야 한다.
public enum StoryBeatKind
{
    Core,    // 그 DAY 에 반드시 재생(재생 불가면 조용히 생략)
    Pool,    // 후보 — 조건 맞는 것 중 무작위
    React,   // 어제 일에 대한 반응 — 그 DAY 맨 앞에 최대 1개
}

public sealed class StoryBeatEntry
{
    public readonly StoryBeat Beat = new();
    // 0 = any(어느 날이든).
    public int Day;
    // 이 직원이 전부 "근무 가능" 해야 재생한다. 하나라도 어긋나면 비트를 통째로 건너뛴다.
    public string[] Need = Array.Empty<string>();
    public string When = "always";
    public StoryBeatKind Kind = StoryBeatKind.Pool;

    public string Id => Beat.BeatId;
}
