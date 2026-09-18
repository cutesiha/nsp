using Godot;
using Godot.Collections;

namespace NSP.Data;

// 직원 한 명이 "오늘의 기분"으로 쓸 수 있는 표현 풀.
//
// 기분상태는 능력치 같은 수치 스탯이 아니라, 플레이어가 근무 배치 전에 읽는 추리 단서다.
// 따라서 다음 규칙을 데이터 단계에서 지킨다.
//  · 아무 감정이나 랜덤으로 고르지 않는다 — 캐릭터 성격에 맞는 표현만 이 풀에 넣는다.
//  · 방해자 여부는 선택에 전혀 개입하지 않는다(기분만 보고 범인을 확정할 수 없어야 한다).
//  · 기분은 본인이 적어 내는 자기보고라 거짓말하지 않는다. 다만 약하게/모호하게 적을 수는 있다.
[GlobalClass]
public partial class MoodPoolDef : Resource
{
    [Export] public string EmployeeId = "";

    // 전날 특별한 일이 없었을 때 쓰는 무난한 표현. DAY1 은 이전 사건이 없으므로 여기만 쓴다.
    [Export] public Array<string> CalmMoods = new();

    // "잘 모르겠음 / 별생각 없음" 처럼 단서 가치가 낮은 모호한 표현.
    // 한 DAY 에 ConfigData.MoodVagueMaxPerDay 명까지만 쓸 수 있다.
    [Export] public Array<string> VagueMoods = new();

    // 이후 DAY 확장용 — 전날 가벼운 사건(작은 작업실 사고 / 이상한 소리 / 수상한 목격).
    [Export] public Array<string> LightEventMoods = new();

    // 이후 DAY 확장용 — 전날 강한 사건(동료 사망 / 시신 발견 / 공격 피해 / 심각한 이상현상).
    [Export] public Array<string> StrongEventMoods = new();
}
