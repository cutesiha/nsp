using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 어떤 괴물을 쓸지 고르는 **단 한 곳**.
//
// 게임에 나오는 개체는 data/threats 의 셋뿐이다 — 형체 · 작은 것 · 다리 많은 것.
// 작업실에 나타나는 것도, 복도로 걸어오는 것도, CCTV 를 채우는 점프스케어도,
// 모니터에 비치는 그림자도 전부 이 셋에서 나온다. 연출마다 다른 모델을 쓰면
// "내가 본 게 그것이었나" 를 플레이어가 맞춰 볼 수 없다.
//
// (예전에는 entity.glb 라는 네 번째 모델이 공포 연출 전용으로 따로 있었다. 그래서
//  작업실에서 본 것과 화면에 비친 것이 서로 다른 존재였다 — 지금은 지웠다.)
public static class MonsterCast
{
    // 그 날짜에 나올 수 있는 개체 중 하나. 가중치(MonsterDef.Weight)를 따른다.
    public static MonsterDef Pick(int day, RandomNumberGenerator rng = null)
    {
        var pool = MonsterThreatSystem.Monsters
            .Where(m => m.FirstDay <= day && !string.IsNullOrEmpty(m.ModelPath))
            .ToList();
        if (pool.Count == 0) return null;

        float total = pool.Sum(m => Mathf.Max(0.01f, m.Weight));
        float roll = rng?.RandfRange(0f, total) ?? (float)GD.RandRange(0.0, total);
        foreach (var m in pool)
        {
            roll -= Mathf.Max(0.01f, m.Weight);
            if (roll <= 0f) return m;
        }
        return pool[^1];
    }

    public static MonsterDef ById(string monsterId) =>
        string.IsNullOrEmpty(monsterId)
            ? null
            : MonsterThreatSystem.Monsters.FirstOrDefault(m => m.MonsterId == monsterId);

    // 그 개체가 "움직이는 중" 으로 보일 때 쓸 클립. 없는 모델(거미)은 빈 문자열이고,
    // 그때는 MonsterActor 가 코드로 몸을 흔든다.
    public static string MoveClip(MonsterDef def) =>
        def == null ? "" :
        !string.IsNullOrEmpty(def.AnimWalk) ? def.AnimWalk :
        !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun : def.AnimIdle;

    // 얼굴을 들이미는 연출에서 쓸 클립.
    public static string ScareClip(MonsterDef def) =>
        def == null ? "" :
        !string.IsNullOrEmpty(def.AnimJumpscare) ? def.AnimJumpscare :
        !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun : def.AnimIdle;

    // 그 클립에서 **앞을 보고 있는 지점**(초). 앞부분이 웅크렸다 도는 준비 동작인
    // 모델이 있어서, 짧은 연출은 거기서 멈추면 뒷모습만 보여 주고 끝난다.
    public static float ScareClipAt(MonsterDef def) =>
        def != null && !string.IsNullOrEmpty(def.AnimJumpscare) ? def.AnimJumpscareAtSeconds : 0f;
}
