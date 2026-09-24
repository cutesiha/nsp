namespace NSP.Facility;

// 오늘 근무에서 작업실들이 실제로 만들어 낸 결과의 합계.
//
// 표시 전용이다. 시뮬레이션은 이 값을 읽지 않으며, 여기 있는 어떤 값도 판정에 쓰이지
// 않는다. 방 카드의 "보이는 숫자 한 줄"과 휴게 진입 요약이 같은 수치를 쓰도록
// 한 곳에 모아 둔 것뿐이다.
//
// 채우는 쪽은 FacilitySimulation 의 효과 발생 지점이고, 읽는 쪽은 NSP.Ui.RoomEffectText
// 와 RestRosterView 다.
public static class RoomEffectStats
{
    // 코어실 — 오늘 올린 복구량(%). 깎인 양은 여기 들어오지 않는다.
    public static float CoreUpToday;
    // 정비실 — 오늘 생산한 자재 개수.
    public static int MaterialsToday;
    // 경비실 — 오늘 남은 재석 기록 수(순찰 + 자리 이탈).
    public static int GuardRecordsToday;
    // 환기실 — 오늘 누군가가 찍은 스트레스 최고치.
    public static float StressPeakToday;

    // 그 방이 방금 제 일을 해냈다 — 미니맵이 상자를 한 번 밝히는 데만 쓴다.
    // 시뮬레이션이 화면을 직접 알지 않도록 여기를 거쳐 간다.
    public static System.Action<string> RoomWorked;

    // 자재가 방금 늘었다 — 화면 위 자재 숫자가 한 번 커졌다 돌아온다.
    public static System.Action MaterialsGained;

    // 환기가 다시 돌기 시작했다 — 미니맵이 방들을 잠깐 푸르게 물들인다.
    public static System.Action VentilationRestored;

    // 실내 조명을 한 번 깜빡여 달라는 요청(발전실 출력 저하). 3D 중앙제어실이 받는다.
    public static System.Action LightFlickerRequested;

    public static void Pulse(string roomId)
    {
        if (!string.IsNullOrEmpty(roomId)) RoomWorked?.Invoke(roomId);
    }

    public static void ResetDay()
    {
        CoreUpToday = 0f;
        MaterialsToday = 0;
        GuardRecordsToday = 0;
        StressPeakToday = 0f;
    }

    public static void NoteStress(float stress)
    {
        if (stress > StressPeakToday) StressPeakToday = stress;
    }
}
