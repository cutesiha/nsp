# 컷씬 이미지 넣는 곳

여기에 파일을 넣기만 하면 임시 패널이 자동으로 최종 이미지로 바뀐다.
**코드도, 데이터 파일도 고칠 필요 없다.** (경로는 이미 `docs/NSP_PROLOGUE_RUNTIME.md` 에 적혀 있다.)

- 권장 규격: 가로:세로 = 약 **2:1** (예: 1344 x 688). 화면에서 672 x 344 칸에 비율 유지로 들어간다.
- 형식: `.png` (투명 없어도 됨)
- 파일이 없는 동안에는 `[ IMAGE ] + 장면 설명` 임시 패널이 대신 뜬다.

## prologue/ — 프롤로그 #1 기록 영상

| 파일명 | 장면 |
|---|---|
| `archive_01_facility.png` | 밝고 멀쩡한 시설 전경 · 연구원들 · 정상 가동 중인 코어 |
| `archive_02_director.png` | 연구소 총괄 관리자 클로즈업 |
| `archive_03_habitat.png` | 연구동 내부 · 안정적으로 유지되는 생활 구역 |
| `archive_04_core.png` | 봉쇄 코어 클로즈업 · 푸른 빛으로 안정 가동 |

## prologue/ — 프롤로그 #2 대재난

| 파일명 | 장면 |
|---|---|
| `disaster_01_director_glitch.png` | 총괄 관리자의 얼굴이 일그러지며 화면이 찢어짐 |
| `disaster_02_signal_lost.png` | 완전히 깨진 화면 |
| `disaster_03_surface_red.png` | 지상 관측 카메라가 붉게 물듦 |
| `disaster_04_lab_wreck.png` | 연구실 파손 · 집기가 쏟아짐 |
| `disaster_05_staff_running.png` | 직원이 허겁지겁 복도를 뛰어감 |
| `disaster_06_door_closing.png` | 차폐문이 닫힘 |
| `disaster_07_cctv_shadow.png` | CCTV 가장자리를 무언가가 스쳐 지나감 |
| `disaster_08_core_drop.png` | 코어 출력 게이지가 급락 |
| `disaster_09_core_alert.png` | 코어실 · 붉게 점멸하는 봉쇄 코어 |
| `disaster_10_core_breach.png` | 코어실 · 차폐막이 깨지는 순간 |
| `disaster_11_core_3pct.png` | 코어 출력 3% |
| `disaster_12_radio.png` | 노이즈가 낀 무전 화면 (무전 3줄이 같이 씀) |
| `disaster_13_director_last.png` | 총괄 관리자 · 마지막 지시 |

마지막 두 장면(`EMERGENCY SEAL` 암전 · 플레이어가 쓰러짐)은 일부러 이미지가 없다.

## outage/ — DAY0 STEP7 결번자 영상

| 파일명 | 장면 |
|---|---|
| `ghost_01_corridor.png` | 텅 빈 통로 · 정지된 녹화 화면 |
| `ghost_02_standing.png` | 화면 안쪽에 결번자가 가만히 서 있다 |
| `ghost_03_approach.png` | 결번자가 카메라 바로 앞까지 다가온다 |
| `ghost_04_impact.png` | 결번자가 카메라에 머리를 들이받으며 비명 |

마지막 `SIGNAL LOST` 장면은 이미지 없이 암전으로 처리된다.

---

슬라이드를 더 넣거나, 순서/길이/효과음을 바꾸려면 `docs/NSP_PROLOGUE_RUNTIME.md` 의
`@slide` 블록만 고치면 된다(`hold:` = 초, `fx:` = glitch/cut/siren/shake/blackout/typing).
