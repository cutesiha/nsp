# legacy/

2026-09-21 직원 6인 교체(해파리→양, 올빼미→늑대, 까마귀→강아지) 때 은퇴한 데이터 보관소.

- `.gdignore` 가 있어서 Godot 은 이 폴더를 스캔·임포트·로드하지 않는다. 런타임에 절대 쓰이지 않는다.
- `old_employees/data/` : 옛 EmployeeDef / 오늘의 기분 / 말투(voice) .tres — 설정 참고용.
- `old_employees/art/`  : 어디서도 참조하지 않던 옛 까마귀 원화.

옛 스탠딩 원화·보이스는 삭제하지 않고 새 캐릭터 이름으로 옮겨 **임시 원화/보이스**로 쓰고 있다
(`assets/characters/standing_v1/{sheep,wolf,dog}_stand.png`, `assets/audio/voice/{sheep,wolf,dog}_0N.wav`).
최종 에셋이 나오면 같은 경로에 덮어쓰기만 하면 된다.
