# NSP 성능 — 무엇이 비싼가 (하나씩 꺼 보기)

- Intel(R) UHD Graphics · Intel(R) Core(TM) i3-10110U CPU @ 2.10GHz
- 품질 Low · V-Sync Disabled
- 끈 것은 **측정 동안만**이고 바로 되돌린다. 설정을 고치지 않는다.

## 짝지어 본 차이 (앞 기준 → 끈 뒤)

| 끈 것 | 기준 ms | 끈 뒤 ms | 차이 ms | 기준 FPS | 끈 뒤 FPS | VRAM MB 차이 | draw call 차이 |
|---|---|---|---|---|---|---|---|
| ② CCTV 3D 월드 렌더 OFF | 41.8 | 43.5 | **+1.7** | 23.9 | 23.0 | 0 | -4 |
| ③ CRT SubViewport 전부 렌더 OFF | 43.4 | 44.5 | **+1.1** | 23.1 | 22.5 | 0 | -133 |
| ④ 그림자 전부 OFF (광원 13개 · 상한) | 68.3 | 44.2 | **-24.0** | 14.7 | 22.6 | 0 | -338 |
| ⑤ 메인 3D 스케일 0.72 → 0.36 (상한) | 54.0 | 48.9 | **-5.0** | 18.5 | 20.4 | -18 | -3 |
| ⑥ 제어실 3D 전체 숨김 (상한) | 48.1 | 35.8 | **-12.2** | 20.8 | 27.9 | 0 | -12 |
| ⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM) | 43.3 | 41.5 | **-1.9** | 23.1 | 24.1 | -29 | -1 |
| ⑧ 그림자 — 스포트라이트만 남김 | 40.1 | 37.9 | **-2.1** | 25.0 | 26.4 | 0 | -338 |
| ⑨ 메인 3D 스케일 0.72 → 0.60 | 39.6 | 34.3 | **-5.4** | 25.2 | 29.2 | -7 | 0 |
| ⑨ 메인 3D 스케일 0.72 → 0.52 | 36.9 | 30.1 | **-6.8** | 27.1 | 33.2 | -11 | -1 |
| ⑩ CRT 렌더 배율 0.70 → 0.58 | 37.3 | 35.9 | **-1.4** | 26.8 | 27.9 | -4 | -1 |
| ⑩ CRT 렌더 배율 0.70 → 0.46 | 41.0 | 40.1 | **-0.9** | 24.4 | 24.9 | -9 | -12 |

> 차이가 **음수**면 그만큼 빨라진 것이다. 기준 줄끼리 비교하면 안 된다 —
> 이 기계는 측정 내내 성능이 흔들려서 떨어진 두 줄은 서로 비교할 수 없다.

## 잰 그대로

| 상황 | 프레임 | 평균 FPS | 1% Low | 최저 FPS | 평균 ms | P95 ms | P99 ms | 최악 ms | >50ms | >100ms | GC 할당 MB | draw call |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 〇 몸풀기(버리는 측정) | 72 | 24.0 | 16.3 | 16.3 | 41.68 | 51.37 | 61.19 | 61.19 | 5 | 0 | 6.48 | 801 |
| · 기준 — ② CCTV 3D 월드 렌더 OFF | 72 | 23.9 | 16.8 | 16.8 | 41.82 | 48.62 | 59.44 | 59.44 | 2 | 0 | 6.48 | 803 |
| ② CCTV 3D 월드 렌더 OFF | 69 | 23.0 | 14.3 | 14.3 | 43.55 | 52.74 | 69.71 | 69.71 | 5 | 0 | 6.22 | 799 |
| · 기준 — ③ CRT SubViewport 전부 렌더 OFF | 70 | 23.1 | 15.2 | 15.2 | 43.38 | 50.00 | 65.83 | 65.83 | 4 | 0 | 6.30 | 801 |
| ③ CRT SubViewport 전부 렌더 OFF | 68 | 22.5 | 14.7 | 14.7 | 44.52 | 55.11 | 67.87 | 67.87 | 11 | 0 | 6.15 | 668 |
| · 기준 — ④ 그림자 전부 OFF (광원 13개 · 상한) | 44 | 14.7 | 6.7 | 6.7 | 68.26 | 133.33 | 150.00 | 150.00 | 29 | 10 | 4.13 | 799 |
| ④ 그림자 전부 OFF (광원 13개 · 상한) | 68 | 22.6 | 19.5 | 19.5 | 44.25 | 50.00 | 51.22 | 51.22 | 5 | 0 | 6.12 | 461 |
| · 기준 — ⑤ 메인 3D 스케일 0.72 → 0.36 (상한) | 56 | 18.5 | 6.7 | 6.7 | 53.96 | 133.35 | 149.10 | 149.10 | 19 | 5 | 5.07 | 802 |
| ⑤ 메인 3D 스케일 0.72 → 0.36 (상한) | 62 | 20.4 | 16.2 | 16.2 | 48.92 | 57.89 | 61.62 | 61.62 | 33 | 0 | 5.59 | 799 |
| · 기준 — ⑥ 제어실 3D 전체 숨김 (상한) | 63 | 20.8 | 16.1 | 16.1 | 48.05 | 54.06 | 62.12 | 62.12 | 27 | 0 | 5.68 | 801 |
| ⑥ 제어실 3D 전체 숨김 (상한) | 84 | 27.9 | 21.0 | 21.0 | 35.81 | 42.60 | 47.58 | 47.58 | 0 | 0 | 7.54 | 789 |
| · 기준 — ⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM) | 70 | 23.1 | 17.0 | 17.0 | 43.33 | 50.34 | 58.86 | 58.86 | 6 | 0 | 6.30 | 800 |
| ⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM) | 73 | 24.1 | 18.4 | 18.4 | 41.48 | 50.00 | 54.38 | 54.38 | 5 | 0 | 6.57 | 799 |
| · 기준 — ⑧ 그림자 — 스포트라이트만 남김 | 75 | 25.0 | 18.8 | 18.8 | 40.06 | 44.44 | 53.10 | 53.10 | 2 | 0 | 6.73 | 799 |
| ⑧ 그림자 — 스포트라이트만 남김 | 80 | 26.4 | 19.5 | 19.5 | 37.92 | 45.20 | 51.28 | 51.28 | 3 | 0 | 7.17 | 461 |
| · 기준 — ⑨ 메인 3D 스케일 0.72 → 0.60 | 76 | 25.2 | 15.7 | 15.7 | 39.63 | 48.23 | 63.74 | 63.74 | 2 | 0 | 6.83 | 799 |
| ⑨ 메인 3D 스케일 0.72 → 0.60 | 88 | 29.2 | 23.5 | 23.5 | 34.26 | 38.48 | 42.49 | 42.49 | 0 | 0 | 7.88 | 799 |
| · 기준 — ⑨ 메인 3D 스케일 0.72 → 0.52 | 82 | 27.1 | 19.5 | 19.5 | 36.91 | 41.67 | 51.30 | 51.30 | 1 | 0 | 7.36 | 800 |
| ⑨ 메인 3D 스케일 0.72 → 0.52 | 100 | 33.2 | 26.0 | 26.0 | 30.09 | 33.33 | 38.40 | 38.40 | 0 | 0 | 8.95 | 799 |
| · 기준 — ⑩ CRT 렌더 배율 0.70 → 0.58 | 81 | 26.8 | 19.9 | 19.9 | 37.30 | 42.01 | 50.16 | 50.16 | 1 | 0 | 7.28 | 800 |
| ⑩ CRT 렌더 배율 0.70 → 0.58 | 84 | 27.9 | 18.8 | 18.8 | 35.88 | 42.79 | 53.15 | 53.15 | 1 | 0 | 7.54 | 799 |
| · 기준 — ⑩ CRT 렌더 배율 0.70 → 0.46 | 74 | 24.4 | 19.7 | 19.7 | 41.02 | 45.46 | 50.82 | 50.82 | 1 | 0 | 6.66 | 799 |
| ⑩ CRT 렌더 배율 0.70 → 0.46 | 75 | 24.9 | 17.2 | 17.2 | 40.12 | 50.00 | 58.14 | 58.14 | 5 | 0 | 6.74 | 787 |

| 상황 | GPU 메모리 MB | draw call | 렌더 오브젝트 |
|---|---|---|---|
| 〇 몸풀기(버리는 측정) | 659.2 | 801 | 2724 |
| · 기준 — ② CCTV 3D 월드 렌더 OFF | 659.2 | 803 | 2725 |
| ② CCTV 3D 월드 렌더 OFF | 659.2 | 799 | 2716 |
| · 기준 — ③ CRT SubViewport 전부 렌더 OFF | 659.2 | 801 | 2724 |
| ③ CRT SubViewport 전부 렌더 OFF | 659.2 | 668 | 1690 |
| · 기준 — ④ 그림자 전부 OFF (광원 13개 · 상한) | 659.2 | 799 | 2716 |
| ④ 그림자 전부 OFF (광원 13개 · 상한) | 659.2 | 461 | 2172 |
| · 기준 — ⑤ 메인 3D 스케일 0.72 → 0.36 (상한) | 659.7 | 802 | 2724 |
| ⑤ 메인 3D 스케일 0.72 → 0.36 (상한) | 642.0 | 799 | 2716 |
| · 기준 — ⑥ 제어실 3D 전체 숨김 (상한) | 659.7 | 801 | 2724 |
| ⑥ 제어실 3D 전체 숨김 (상한) | 659.2 | 789 | 2701 |
| · 기준 — ⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM) | 659.3 | 800 | 2716 |
| ⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM) | 630.5 | 799 | 2716 |
| · 기준 — ⑧ 그림자 — 스포트라이트만 남김 | 659.2 | 799 | 2716 |
| ⑧ 그림자 — 스포트라이트만 남김 | 659.3 | 461 | 2172 |
| · 기준 — ⑨ 메인 3D 스케일 0.72 → 0.60 | 659.3 | 799 | 2716 |
| ⑨ 메인 3D 스케일 0.72 → 0.60 | 652.2 | 799 | 2716 |
| · 기준 — ⑨ 메인 3D 스케일 0.72 → 0.52 | 659.2 | 800 | 2716 |
| ⑨ 메인 3D 스케일 0.72 → 0.52 | 648.5 | 799 | 2716 |
| · 기준 — ⑩ CRT 렌더 배율 0.70 → 0.58 | 659.2 | 800 | 2716 |
| ⑩ CRT 렌더 배율 0.70 → 0.58 | 654.8 | 799 | 2704 |
| · 기준 — ⑩ CRT 렌더 배율 0.70 → 0.46 | 659.2 | 799 | 2716 |
| ⑩ CRT 렌더 배율 0.70 → 0.46 | 650.5 | 787 | 2649 |

## 장면 구성

- 활성 SubViewport: 4개 / 전체 20개
- 3D 스케일: 0.72 · CRT 렌더 배율 0.70 · 창 (1800, 980)
- 조명(OmniLight3D+SpotLight3D): 69개 · 그림자 켠 것 12개
- 보이는 MeshInstance3D: 1695개

### 그림자를 켠 광원

- `/root/PerfProbeRun/MainScene3D_Test/ControlRoom/Monitor01/M01_ScreenLight` (SpotLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/ControlRoom/Monitor02/M02_ScreenLight` (SpotLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/ControlRoom/Lights/CeilingLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/PowerRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/VentRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/MaintenanceRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/MedicalRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/GuardRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/CoreRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/StorageRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/IsolationRoom/RoomBase/Lights/RoomLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/IsolationRoom/RedLight` (OmniLight3D)
- `/root/PerfProbeRun/MainScene3D_Test/@SubViewport@329/FacilityCctvWorld/RoomRest/RoomBase/Lights/RoomLight` (OmniLight3D)
