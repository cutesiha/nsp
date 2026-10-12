# NSP 성능 — 무엇이 비싼가 (하나씩 꺼 보기)

- Intel(R) UHD Graphics · Intel(R) Core(TM) i3-10110U CPU @ 2.10GHz
- 품질 Low · V-Sync Disabled
- 끈 것은 **측정 동안만**이고 바로 되돌린다. 설정을 고치지 않는다.

| 상황 | 프레임 | 평균 FPS | 1% Low | 최저 FPS | 평균 ms | P95 ms | P99 ms | 최악 ms | >50ms | >100ms | GC 할당 MB | draw call |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ① 기준 — 근무 중 CCTV 켜짐 | 56 | 18.4 | 16.0 | 16.0 | 54.24 | 56.64 | 62.50 | 62.50 | 56 | 0 | 5.20 | 806 |
| ② CCTV 3D 월드 렌더 OFF | 47 | 15.7 | 11.9 | 11.9 | 63.86 | 78.48 | 84.22 | 84.22 | 46 | 0 | 4.36 | 803 |
| ③ CRT SubViewport 19개 렌더 OFF | 57 | 18.9 | 14.0 | 14.0 | 52.85 | 66.26 | 71.38 | 71.38 | 43 | 0 | 5.28 | 672 |
| ④ 그림자 OFF (광원 13개) | 64 | 21.2 | 17.1 | 17.1 | 47.21 | 53.43 | 58.41 | 58.41 | 17 | 0 | 5.92 | 460 |
| ⑤ 메인 3D 스케일 0.72 → 0.36 | 79 | 26.2 | 15.7 | 15.7 | 38.21 | 49.29 | 63.77 | 63.77 | 2 | 0 | 7.28 | 805 |
| ⑥ 제어실 3D 전체 숨김(상한 확인) | 98 | 32.6 | 22.6 | 22.6 | 30.72 | 37.02 | 44.30 | 44.30 | 0 | 0 | 9.00 | 790 |
| ⑦ 안 쓰는 화면 16개 렌더 타깃 해제(VRAM) | 71 | 23.3 | 15.0 | 15.0 | 42.84 | 50.18 | 66.48 | 66.48 | 7 | 0 | 6.55 | 804 |
| ⑧ 그림자 — 스포트라이트만 남김(끈 것 11개) | 74 | 24.6 | 15.4 | 15.4 | 40.68 | 48.76 | 64.96 | 64.96 | 2 | 0 | 6.83 | 462 |
| ⑨ 메인 3D 스케일 0.72 → 0.60 | 85 | 28.3 | 20.0 | 20.0 | 35.34 | 43.23 | 50.00 | 50.00 | 0 | 0 | 7.82 | 806 |
| ⑨ 메인 3D 스케일 0.72 → 0.52 | 91 | 30.1 | 21.0 | 21.0 | 33.27 | 38.76 | 47.64 | 47.64 | 0 | 0 | 8.36 | 806 |
| ⑩ CRT 렌더 배율 0.70 → 0.58 | 79 | 26.3 | 18.3 | 18.3 | 38.02 | 45.57 | 54.50 | 54.50 | 1 | 0 | 7.28 | 799 |
| ⑩ CRT 렌더 배율 0.70 → 0.46 | 78 | 26.0 | 20.4 | 20.4 | 38.49 | 42.99 | 48.97 | 48.97 | 0 | 0 | 7.18 | 795 |

| 상황 | GPU 메모리 MB | draw call | 렌더 오브젝트 |
|---|---|---|---|
| ① 기준 — 근무 중 CCTV 켜짐 | 659.0 | 806 | 2720 |
| ② CCTV 3D 월드 렌더 OFF | 659.5 | 803 | 2720 |
| ③ CRT SubViewport 19개 렌더 OFF | 659.0 | 672 | 1694 |
| ④ 그림자 OFF (광원 13개) | 659.0 | 460 | 2172 |
| ⑤ 메인 3D 스케일 0.72 → 0.36 | 641.8 | 805 | 2719 |
| ⑥ 제어실 3D 전체 숨김(상한 확인) | 659.0 | 790 | 2696 |
| ⑦ 안 쓰는 화면 16개 렌더 타깃 해제(VRAM) | 630.3 | 804 | 2727 |
| ⑧ 그림자 — 스포트라이트만 남김(끈 것 11개) | 659.0 | 462 | 2180 |
| ⑨ 메인 3D 스케일 0.72 → 0.60 | 652.0 | 806 | 2727 |
| ⑨ 메인 3D 스케일 0.72 → 0.52 | 647.8 | 806 | 2728 |
| ⑩ CRT 렌더 배율 0.70 → 0.58 | 654.1 | 799 | 2699 |
| ⑩ CRT 렌더 배율 0.70 → 0.46 | 650.3 | 795 | 2660 |

## 장면 구성

- 활성 SubViewport: 4개 / 전체 20개
- 3D 스케일: 0.72 · 창 (1800, 980)
- 조명(OmniLight3D+SpotLight3D): 68개 · 그림자 켠 것 12개
- 보이는 MeshInstance3D: 1694개

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
