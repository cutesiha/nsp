using System.Collections.Generic;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 작업실 효과를 시설 로그에 한 줄로 내보낸다.
//
// 코어 복구는 4.3초, 자재 생산은 3초마다 한 번씩 완료된다. 완료마다 한 줄을 쓰면
// 근무 한 번에 70줄 가까이 쌓여, 정작 휴게시간에 읽어야 할 이동·사고 줄이 묻힌다.
// 그래서 **같은 방의 같은 종류는 10초 동안 모았다가 한 줄로** 내보낸다.
//   "정비실 | 자재 +3 (총 24)"
// 즉각적인 반응(미니맵 점멸 · 코어 게이지 옆 +1)은 모으지 않고 발생 즉시 나간다 —
// 눈에 보이는 반응과 나중에 읽는 기록의 역할이 다르기 때문이다.
//
// 한 번뿐인 사건(환기 재개, 자재 효율 변화, 의무실 이송)은 Once 로 바로 나간다.
public static class RoomEffectLog
{
    private const float BatchSeconds = 10f;

    private enum Kind { Core, Materials }

    private sealed class Batch
    {
        public float Amount;
        public float Age;
    }

    private static readonly Dictionary<(string, Kind), Batch> Pending = new();

    public static void ResetDay() => Pending.Clear();

    // 코어 복구 1회분. 누적해 두었다가 한 줄로 낸다.
    public static void NoteCore(string roomId, float percent) => Note(roomId, Kind.Core, percent);

    // 자재 생산 1회분.
    public static void NoteMaterials(string roomId, int count) => Note(roomId, Kind.Materials, count);

    private static void Note(string roomId, Kind kind, float amount)
    {
        if (string.IsNullOrEmpty(roomId) || amount <= 0f) return;
        if (!Pending.TryGetValue((roomId, kind), out var b))
            Pending[(roomId, kind)] = b = new Batch();
        b.Amount += amount;
    }

    // 모아 둔 줄을 시간이 되면 내보낸다. FacilitySimulation.Tick 에서 부른다.
    public static void Tick(float delta)
    {
        if (Pending.Count == 0) return;
        List<(string, Kind)> done = null;
        foreach (var (key, b) in Pending)
        {
            b.Age += delta;
            if (b.Age < BatchSeconds) continue;
            Emit(key.Item1, key.Item2, b.Amount);
            (done ??= new List<(string, Kind)>()).Add(key);
        }
        if (done == null) return;
        foreach (var key in done) Pending.Remove(key);
    }

    // 근무가 끝나면 남은 몫을 마저 내보낸다(마지막 10초가 통째로 사라지지 않게).
    public static void FlushAll()
    {
        if (Pending.Count == 0) return;
        foreach (var (key, b) in Pending) Emit(key.Item1, key.Item2, b.Amount);
        Pending.Clear();
    }

    private static void Emit(string roomId, Kind kind, float amount)
    {
        if (amount <= 0f) return;
        string room = RoomName(roomId);
        string text = kind switch
        {
            Kind.Core => $"{room} — 복구 +{amount:0.#}% (오늘 {RoomEffectStats.CoreUpToday:0.#}%)",
            _ => $"{room} — 자재 +{amount:0} (총 {GameState.Instance?.Materials ?? 0})",
        };
        EventLog.Instance?.LogEvent(LogEventType.RoomEffect, "", roomId, text);
    }

    // 한 번뿐인 작업실 효과. 문장은 "작업실 — 내용" 형태로 넘긴다.
    public static void Once(string roomId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        EventLog.Instance?.LogEvent(LogEventType.RoomEffect, "", roomId, text);
    }

    private static string RoomName(string roomId) =>
        FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;
}
