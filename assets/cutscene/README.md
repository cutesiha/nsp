# 컷씬 이미지 넣는 곳

여기에 파일을 넣기만 하면 임시 패널이 자동으로 최종 이미지로 바뀐다.
**코드도, 데이터 파일도 고칠 필요 없다.** (경로는 이미 `docs/NSP_PROLOGUE_RUNTIME.md` 에 적혀 있다.)

## 규격

배경 이미지가 들어가는 '영상 영역'은 **논리 좌표 780 x 448** 이다(비율 약 **1.74 : 1**).
CRT 는 이 논리 캔버스를 1.3배로 렌더하므로, 실제 화면에 찍히는 픽셀은 그래픽 품질에 따라:

| 그래픽 품질 | 실제 표시 픽셀 |
|---|---|
| 높음 (기본) | **1014 x 582** |
| 보통        | 862 x 495 |
| 낮음        | 710 x 408 |

- **권장: 1560 x 896** (논리 크기의 정확히 2배 — 어떤 품질에서도 선명하다)
- 최소: 1014 x 582
- 비율이 달라도 잘리지 않는다. 비율을 유지한 채 칸 안에 맞춰 넣고 남는 곳은 검게 둔다.
  16:9(예: 1600 x 900)로 만들면 위아래에 약 2% 정도 얇은 검은 띠가 생기는 정도다.
- 형식: `.png` (투명 없어도 됨)
- 파일이 없는 동안에는 `[ IMAGE ] + 장면 설명` 임시 패널이 대신 뜬다.

인물 일러스트(`figure:`) 칸도 기능은 살아 있다(논리 300 x 360, 권장 600 x 720 투명 PNG).
다만 현재 프롤로그는 이 칸을 쓰지 않는다 — 데이터 파일의 `figure:` 를 채우면 다시 뜬다.

> 자막 상자가 영상 아래쪽(논리 y 382~478)을 덮으므로, 배경의 아래 약 1/4 에는
> 꼭 봐야 하는 요소를 두지 않는 편이 좋다.

## prologue/ — 프롤로그 #1 기록 영상

| 파일명 | 장면 |
|---|---|
| `archive_01_facility.png` | 밝고 멀쩡한 시설 전경 · 연구원들 · 정상 가동 중인 코어 |
| `archive_02_director.png` | 연구소 총괄 관리자 클로즈업 |
| `archive_03_habitat.png` | 연구동 내부 · 안정적으로 유지되는 생활 구역 |
| `archive_04_core.png` | 봉쇄 코어 클로즈업 · 푸른 빛으로 안정 가동 |

맨 앞의 표제 컷(국가특수에너지연구원 / 제7지하시설 / 정기 안전교육 기록)은 그림 없이
검은 화면에 글자만 타이핑되므로 이미지가 필요 없다.
`archive_02_director.png` 는 CRT 가 켜지는 컷과 인사 두 줄에 연속으로 쓰인다.

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
