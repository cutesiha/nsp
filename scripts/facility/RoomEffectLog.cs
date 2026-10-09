using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 작업실에서 **한 번만 일어나는 일**을 시설 로그에 한 줄로 내보낸다.
//
// 코어 복구와 자재 생산은 몇 초마다 한 번씩 완료된다. 그것까지 로그로 내면
// 근무 한 번에 "코어 복구 +2%" 같은 줄이 수십 개 쌓여, 정작 휴게시간에 읽어야 할
// 이동 · 사고 · 기절 · 이상 개체 · 방해공작 줄이 묻힌다.
// 그래서 **진행 상황은 로그에 쓰지 않는다** — 그건 코어 게이지와 자재 숫자가 이미 보여 준다.
// 여기로 나가는 것은 환기 재개 · 자재 효율 변화 · 의무실 이송처럼 "그때 한 번 있었던 일" 뿐이다.
public static class RoomEffectLog
{
    // 한 번뿐인 작업실 효과. 문장은 "작업실 — 내용" 형태로 넘긴다.
    public static void Once(string roomId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        EventLog.Instance?.LogEvent(LogEventType.RoomEffect, "", roomId, text);
    }
}
