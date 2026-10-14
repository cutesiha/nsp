using Godot;
using Godot.Collections;

namespace NSP.Data;

// CCTV 로 엿듣는 같은 방 두 사람의 짧은 대화(관계 시스템 Phase 2).
// 위치: res://data/relationships/overheard_lines.tres (config OverheardLinesPath)
//
// 한 줄이 "주고받는 대화 한 번"이다. 먼저 말하는 사람이 A, 받는 사람이 B.
//   "key|situation|A 대사|B 대사"
//   key       : 관계 밴드 — Close / Friendly / Neutral / Uneasy
//               또는 관계 타입 — type:Lover / type:MutualCrush / type:Crush …
//               (type 줄은 A 가 B 에게 그 감정을 가진 쪽일 때만 쓰인다. 예: type:Crush → 토끼가 A)
//   situation : normal(평상) / incident(사고 직후) / sabotage(방해 정황)
//   대사       : {a} = A 의 코드네임, {b} = B 의 코드네임. 받침 조사는 슬래시로 쓴다({b}은/는).
// "#" 로 시작하는 줄은 주석.
//
// 대사는 누가 방해자인지 가리키지 않는다 — 방해 정황 대사도 두 사람 모두 같은 말을 할 수 있게 쓴다.
[GlobalClass]
public partial class OverheardLineTableDef : Resource
{
    [Export] public Array<string> Lines = new();

    // 그 방 CCTV 를 켠 뒤 첫 대화까지의 시간.
    [Export] public float FirstDelaySeconds = 2.5f;
    // 대사 한 줄이 화면에 머무는 시간(글자가 다 찍힌 뒤 기준).
    [Export] public float LineHoldSeconds = 2.6f;
    // 글자가 찍히는 속도(초당 글자 수).
    [Export] public float CharsPerSecond = 22f;
    // 대화 한 번이 끝나고 다음 대화까지 쉬는 시간(이 범위에서 무작위).
    [Export] public float CooldownMinSeconds = 9f;
    [Export] public float CooldownMaxSeconds = 16f;
    // 이 시간 안에 그 방에서 사고(업무 실패) · 방해공작이 있었으면 "사고 직후" · "방해 정황" 대사를 쓴다.
    [Export] public float IncidentWindowSeconds = 35f;
    [Export] public float SabotageWindowSeconds = 45f;
    // 관계 타입 전용 줄(type:…)이 있을 때 그 줄을 고를 확률. 나머지는 밴드 줄.
    [Export(PropertyHint.Range, "0,1,0.05")] public float TypeLineChance = 0.5f;
}
