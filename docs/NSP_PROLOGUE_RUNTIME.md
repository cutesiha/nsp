# NSP PROLOGUE / DAY0 RUNTIME
#
# 프롤로그 컷씬 · GUIDE-0 대사 · DAY0 튜토리얼 안내문을 전부 담는 런타임 데이터 파일.
# 원안은 docs/NSP_PROLOGUE_AND_TUTORIAL.md 이고, 이 파일은 코드가 읽는 실행용 사본이다.
#
# ★ 문구를 고치고 싶으면 이 파일만 고치면 된다. 코드는 건드릴 필요가 없다.
# ★ 컷씬 이미지도 image: 줄의 경로만 바꿔 끼우면 된다.
#   - image: 가 비어 있거나 파일이 없으면 화면에 "[ IMAGE ] + imagenote" 임시 패널이 뜬다.
#   - image: 와 imagenote: 를 둘 다 비우면 임시 패널 없이 '검은 화면 컷'이 된다.
#   - 최종 일러스트가 나오면 assets/cutscene/... 에 넣고 image: 경로만 채우면 끝.
#
# ── 파싱 규칙 ────────────────────────────────────────────────────────────
#   '#' 로 시작하는 줄, 빈 줄 = 무시
#   text: / overlay: 안의 \n 은 줄바꿈으로 바뀐다.
#
#   @cutscene <id>      컷씬 시작. 이후 @slide 들이 이 컷씬에 순서대로 들어간다.
#     title: <text>        영상 머리말(다음 슬라이드들이 물려받는다)
#     @slide               새 슬라이드(= 한 장면/한 호흡)
#       image:     <res 경로>   최종 이미지. 없으면 임시 패널.
#       imagenote: <text>       임시 패널 안에 띄울 장면 설명(최종 이미지가 들어가면 안 보임)
#       figure:    <res 경로>   배경 앞에 서는 인물 일러스트. 없으면 [ FIGURE ] 임시 칸.
#       figurenote:<text>       그 인물 칸에 띄울 설명
#       title:     <text>       이 슬라이드부터 머리말 교체
#       speaker:   <text>       화면에 보이는 말하는 사람 라벨 (없으면 안 뜸)
#       voice:     <보이스 id>   글자가 찍힐 때 울리는 타이핑 보이스
#                               director = 연구소 총괄 관리자 / guide0 = GUIDE-0
#                               rabbit cat fox sheep wolf dog = 기존 직원 보이스 그대로
#       radio:     true         무전/인터컴으로 들리게 한다(앞뒤 치직 + 약한 잡음 + 무전 필터).
#                               직원 보이스를 바꾸지 않고 Radio 버스로만 통과시킨다.
#       signal:    <0~100>      무전 슬라이드의 좌하단 수신 상태 HUD(신호 세기 + 파형).
#                               값이 낮을수록 파형이 거칠고 화면 노이즈가 늘어난다.
#       cutoff:    true         대사가 다 찍히는 순간 통신이 치직 하고 끊긴다.
#       text:      <text>       자막 한 줄 → 이 줄이 있으면 절대 자동으로 안 넘어간다
#                               (스페이스 / 엔터 / 클릭으로만 진행)
#       overlay:   <text>       화면 한가운데 크게 뜨는 문구(한글 주정보). 타이핑된다.
#       sub:       <text>       overlay 아래에 작게 깔리는 영문 보조 문구.
#       hold:      <초>         대사가 없는 컷을 몇 초 보여줄지(타이핑이 끝난 뒤부터 센다).
#                               0 이면 그 컷도 입력을 기다린다.
#       sfx:       <키>         assets/audio/sfx/<키>.wav 를 슬라이드 시작에 1회 재생
#       sfxafter:  <키>         자막/문구가 다 찍힌 직후에 1회 재생(스위치 조작음 등)
#       sfxloop:   <키>         그 효과음을 반복 재생 시작(사이렌 등). 컷씬이 끝나면 자동 중단.
#       sfxloopstop:<키>        반복 재생 중단
#       shake:     <px>         화면이 계속 미세하게 떨리는 세기. 다음 슬라이드로 이어진다(0 이면 해제)
#       jolt:      <초>         슬라이드가 뜨는 순간 좌우로 짧게 흔들리는 시간
#       ken:       off          느린 줌/팬(켄 번스)을 끈다. 기본은 켜짐 — 정지 이미지도 살아 움직인다.
#       fx:        none|glitch|cut|siren|shake|blackout|typing|impact|alert|flicker|crt|warp|warphold
#                               cut   = 컷이 바뀔 때마다 무작위로 하나(펀치 줌/흔들림/붉은 섬광/팬/글리치)
#                               alert = 화면 전체 붉은 점멸(경보창 컷)
#                               flicker = 조명이 깜빡이듯 밝기가 튄다
#                               crt   = 브라운관이 켜지는 순간(가로선 → 노이즈 → 영상)
#                               warp  = 그 컷의 그림이 그 자리에서 찢어지며 기괴하게 일그러진다
#                                       (새 그림을 띄우지 않는다 — 직전 컷과 같은 image: 를 쓰면 된다)
#                               warphold = 일그러진 그 상태로 멈춘다(신호 두절 문구를 올릴 때)
#
#       ── 시설 시스템 창 ──
#       win:     <창 제목>        화면 한가운데 뜨는 시설 시스템 창(비상 차폐 등)
#       winbig:  <큰 글자>        창 한가운데 크게
#       winsub:  <작은 줄>        그 아래 작게
#
#       ── 비상 경보창 ──
#       alert:     <한글 제목> | <영문 부제>   실제 시설 경보 패널처럼 크게 뜬다
#       alertrow:  <항목> | <값>               여러 줄 가능. 한 줄씩 차례로 켜진다
#       alertfoot: <맨 아랫줄 지시>            항목이 다 뜬 뒤 깜빡이며 나타난다
#
#       ── 게이지(코어 출력 저하 연출) ──
#       gauge:      <한글 제목>       예: 봉쇄 코어 출력
#       gaugesteps: <숫자, 숫자, ...>  이 값들을 순서대로 '실제로 내려가며' 보여준다
#       gaugesub:   <영문 보조 문구>   게이지 아래 작게
#       gaugealert: <경고 문구>        마지막 값에 닿는 순간 붉게 터지는 한글 경고
#
#   @console <id>       오른쪽 CRT 의 cmd 콘솔 연출
#     line: <text>         한 줄 출력 (값이 없으면 빈 줄)
#     wait: <초>           다음 줄까지 대기
#     ok:   <text>         성공 메시지(밝게 + 띠롱 효과음)
#
#   @window <id>        화면 한가운데 잠깐 떴다 사라지는 시스템 창
#     title: <text>        창 제목(한글)
#     big:   <text>        창 한가운데 크게
#     line:  <text>        그 아래 작은 줄(여러 개 가능)
#     hold:  <초>          창이 떠 있는 시간
#
#   @guide <id>         GUIDE-0 홀로그램 대사 묶음
#     portrait: <표정키>   assets/ui/guide0/guide0_<표정키>.png 를 찾는다(없으면 임시 초상)
#     voice: <보이스 id>   이 묶음의 타이핑 보이스(기본 guide0)
#     line: <text>         대사 한 줄(순서대로)
#     icons: employees     그 자리에서 직원 아이콘 6개를 띄운다
#     panel: <종류>        창 오른쪽의 보조 정보판을 바꾼다(말하는 동안 화면이 움직이게)
#                          none / alert(재난·신원오류) / authority(권한 계층도) / mission(차폐 잔여+코어)
#     fx:   noise          짧은 노이즈
#
#   @menu <id>          GUIDE-0 선택지
#     mode: all                             모든 항목을 한 번씩 확인해야 메뉴가 끝난다(순서 자유).
#                                           이미 확인한 항목은 체크 표시 + 비활성으로 남는다.
#     option: <보이는 문구> | <@guide id>   고르면 답변 후 메뉴로 돌아온다
#     final:  <보이는 문구> | <@guide id>   고르면 그 자리에서 메뉴가 끝난다(mode: all 이면 안 씀)
#
#   @scripted <id>      튜토리얼 전용 고정 답변
#     text: <text>         {FROM_ROOM} 등 치환자 사용 가능
#
#   ── GUIDE 대사 강조색 ──
#   @guide 의 `line:` 안에서 쓴다.
#
#   /0  수동 강조 종료 → 기본/자동 강조로 복귀
#   /1  빨강    경고 · 위험 · 방해공작 · 실패 · 긴급
#   /2  핑크    중요한 키워드 · 상태 · 추리상 주목할 내용
#   /3  주황    작업 · 행동 지시 · 시설/설비
#   /4  초록    정상 · 성공 · 회복 · 안전
#   /5  파랑    시스템 정보 · 기록 · CCTV · 데이터
#   /6  하늘색  선택지와 같은 색
#
#   예:
#   line: 직원의 /2스트레스/0를 확인하십시오.
#   line: /1방해공작이 발생했습니다./0 CCTV를 확인하십시오.
#
#   색은 다음 /n 명령이 나올 때까지 유지된다.
#   각 line 은 자동/기본 상태에서 새로 시작한다(앞 줄의 색이 넘어오지 않는다).
#   명령어 자체는 게임 화면에도 대화 기록에도 표시되지 않고, 타이핑 속도에도 영향을 주지 않는다.
#
#   기존 자동 강조는 그대로 적용된다.
#     직원 이름   = 직원 고유색
#     작업실 이름 = 주황색
#   수동 강조 범위 안에서는 수동 색이 우선한다(그 안의 직원 이름도 지정한 색으로 나온다).
#   그래서 직원 이름·작업실 이름에 굳이 /n 을 다시 쓰지 않는다.
#
#   /7 이후·/x 처럼 목록에 없는 것은 명령이 아니라 글자 그대로 남는다(대사가 깨지지 않는다).
#   "3/4" 처럼 숫자 바로 뒤에 오는 /n 도 명령으로 보지 않는다.
#
# ════════════════════════════════════════════════════════════════════════


# ========================================================================
# 프롤로그 #1 — 낡은 연구소 홍보 기록 영상
# ========================================================================
@cutscene prologue_archive
title: 국가특수에너지연구원 제7지하시설 / ARCHIVE 01

# 검은 화면 + 낮은 기계음 + 표제. 그림 없이 글자만 뜨는 컷이다.
@slide
title:
image:
imagenote:
sfx: intro_machine
overlay: 국가특수에너지연구원\n제7지하시설\n\n정기 안전교육 기록
sub: ARCHIVE 01
hold: 2.2
fx: typing

# CRT 가 켜지고 노이즈가 걷히며 기록 영상이 시작된다.
# scene3d: 가 붙은 슬라이드는 정지 그림 대신 **실제 3D 컷씬**이 돈다.
# image: 는 그대로 남겨 둔다 — 3D 장면을 못 찾으면 그 그림으로 돌아간다(fallback).
@slide
title: 국가특수에너지연구원 제7지하시설 / ARCHIVE 01
image: res://assets/cutscene/prologue/archive_02_director.png
scene3d: archive_director
imagenote: 연구소 총괄 관리자 클로즈업
sfx: crt_on
hold: 1.6
fx: crt

@slide
image: res://assets/cutscene/prologue/archive_02_director.png
scene3d: archive_director
imagenote: 연구소 총괄 관리자 클로즈업
speaker: 총괄 관리자
voice: director
text: 제7지하시설 근무자 여러분, 반갑습니다.
hold: 3.0

@slide
image: res://assets/cutscene/prologue/archive_02_director.png
scene3d: archive_director
imagenote: 연구소 총괄 관리자 클로즈업
speaker: 총괄 관리자
voice: director
text: 본 기록은 시설의 핵심 설비와 비상 절차를 안내하기 위해 제작되었습니다.
hold: 4.2

@slide
image: res://assets/cutscene/prologue/archive_01_facility.png
scene3d: archive_facility
imagenote: 밝고 멀쩡한 시설 전경 · 연구원들 · 정상 가동 중인 코어
speaker: 총괄 관리자
voice: director
text: 국가특수에너지연구원은 미지의 개체, 통칭 ‘존재’가 발생시키는 에너지를 연구해 왔습니다.
hold: 4.6

@slide
image: res://assets/cutscene/prologue/archive_03_habitat.png
scene3d: archive_habitat
imagenote: 연구동 내부 · 안정적으로 유지되는 생활 구역
speaker: 총괄 관리자
voice: director
text: 이 연구를 통해 우리는 외부 환경과 단절된 시설에서도 안정적인 생존 환경을 유지할 수 있었습니다.
hold: 5.0

@slide
image: res://assets/cutscene/prologue/archive_04_core.png
scene3d: archive_core
imagenote: 봉쇄 코어 클로즈업 · 푸른 빛으로 안정 가동
speaker: 총괄 관리자
voice: director
text: 시설의 중심에는 생명 유지와 외부 차폐를 담당하는 봉쇄 코어가 있습니다.
hold: 4.8


# ========================================================================
# 프롤로그 #2 — 대재난
# ========================================================================
@cutscene prologue_disaster
title: 국가특수에너지연구원 제7지하시설 / ARCHIVE 01

# 새 컷을 띄우지 않는다 — 직전 아카이브 화면(봉쇄 코어)이 그 자리에서 기괴하게 일그러진다.
# shake: 는 다음 슬라이드로 계속 이어진다 — 암전 컷에서 0 으로 되돌린다.
# sfxloop: siren 도 sfxloopstop 을 만날 때까지 계속 울린다.
@slide
image: res://assets/cutscene/prologue/archive_04_core.png
scene3d: core_warning
imagenote: 직전 컷 그대로 — 코어가 흔들리고 영상이 찢어진다
ken: off
sfx: noise
sfxloop: siren
# scare: 사이렌이 터지는 이 프레임에 화면 전체가 붉게 번쩍이고 제어실 카메라 · 컷 화면이 거칠게 떨린다.
scare: 1.0
shake: 3.0
hold: 2.6
fx: warp

# 일그러진 그 상태에서 신호가 끊긴다.
@slide
title: SIGNAL LOST
image: res://assets/cutscene/prologue/archive_04_core.png
scene3d: core_warning
imagenote:
ken: off
overlay: SIGNAL LOST
sfx: alarm
hold: 1.6
fx: warphold

# ── 비상 경보창 : 실제 시설 경보 패널처럼 뜬다 ──
# 프롤로그는 처음부터 끝까지 **모니터1 안에서 돌아가는 기록영상**이다. 재난 구간에서
# 틀을 깨고 전체 화면으로 나가면 그 순간 '영상' 이 아니게 되어 버린다 — 그래서 끝까지
# 영상으로 둔다. 경보창 · 게이지 · 무전 HUD 는 그 영상 위에 겹쳐 뜬다.
@slide
title: EMERGENCY BROADCAST
scene3d: core_warning
image:
imagenote:
alert: 대재난 경보 | FACILITY EMERGENCY
alertrow: CONTAINMENT | CRITICAL
alertrow: CORE OUTPUT | !
alertrow: FACILITY LOCKDOWN | !
alertfoot: 즉시 대피
sfx: alert_beep3
hold: 3.0
fx: alert

@slide
image:
imagenote:
alert: 격리 시스템 이상 | CONTAINMENT FAILURE
alertrow: RESEARCH ZONE | LOCKDOWN FAILED
alertrow: BULKHEAD SEAL | OPEN
alertrow: SPECIMEN CONTAINMENT | LOST
alertfoot: 연구구역 봉쇄 실패
sfx: alert_beep3
hold: 3.0
fx: alert

# ── 지상 : 밖은 이미 끝났다 ──
# 사이렌 → 시설 내부 긴장 → **여기서 한 번 밖을 본다** → 다시 지하.
# 지하 안만 보여 주면 "이 시설만의 사고" 로 읽힌다. 세 컷, 전부 합쳐 11초쯤.
# 소리가 먼저 오고(거의 암전), 붉은 하늘 아래 폐허가 드러나고, 먼지 속에서 끊긴다.
@slide
title: SURFACE RELAY / LAST SIGNAL
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
scene3d: surface_wide
imagenote: 지상 중계 — 붉은 하늘 아래 무너진 지상, 건물 한 동이 실제로 무너진다
overlay: 지상 중계 회선 — 최종 수신
sfx: alarm
hold: 5.0

@slide
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
scene3d: surface_road
imagenote: 잔해가 쌓인 도로 · 철골 틈에서 스파크가 튄다
hold: 3.2
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
scene3d: surface_last
imagenote: 먼지가 시야를 덮고 회선이 끊긴다
hold: 3.4

# ── 빠르게 지나가는 몽타주 ──
# fx: cut 은 컷마다 무작위로 효과 하나를 고른다(펀치 줌 / 흔들림 / 붉은 섬광 / 팬 / 글리치).
@slide
title: ARCHIVE 02 / EMERGENCY RECORD
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
scene3d: core_warning
imagenote: 경보가 울리는 코어실 — 아직 터지기 전이다
sfx: alarm
hold: 0.7
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_04_lab_wreck.png
scene3d: disaster_lab
imagenote: 연구실 파손 · 집기가 쏟아짐
hold: 3.4

@slide
image: res://assets/cutscene/prologue/disaster_05_staff_running.png
scene3d: disaster_run
imagenote: 직원들이 복도를 실제로 달려 지나간다
hold: 4.2

@slide
image: res://assets/cutscene/prologue/disaster_06_door_closing.png
scene3d: disaster_bulkhead
imagenote: 차폐문이 실제로 내려와 닫힌다
hold: 3.6

@slide
image: res://assets/cutscene/prologue/disaster_07_cctv_shadow.png
scene3d: disaster_cctv
imagenote: 고정 CCTV — 화면 바로 앞을 검은 형체가 가로지른다
hold: 1.8

# ── 봉쇄 코어 출력 저하 : 숫자와 막대가 실제로 내려간다 ──
# 영문은 보조 문구일 뿐이고, 플레이어가 읽어야 하는 주 정보는 전부 한글이다.
@slide
title: CORE CHAMBER / LIVE
image: res://assets/cutscene/prologue/disaster_08_core_drop.png
scene3d: core_drop
imagenote: 거대 봉쇄 코어실 · 코어 자체가 꺼져 간다
gauge: 봉쇄 코어 출력
gaugesteps: 100, 74, 41
gaugesub: CORE OUTPUT DROPPING
shake: 2.0
hold: 4.4
fx: flicker

@slide
image: res://assets/cutscene/prologue/disaster_10_core_breach.png
scene3d: core_drop
imagenote: 코어실 · 출력이 한 자리까지 떨어진다
gauge: 봉쇄 코어 출력
gaugesteps: 41, 16, 3
gaugesub: CORE OUTPUT CRITICAL
gaugealert: ⚠ 치명적 출력 저하
hold: 4.0
fx: alert

# 0% → **정적 한 박자** → 대폭발. 이 컷이 프롤로그에서 가장 강한 장면이다.
# 폭발 · 섬광 · 파편 · 이명은 전부 3D 장면(core_explode)이 낸다 — 여기서는 길이만 준다.
@slide
image: res://assets/cutscene/prologue/disaster_10_core_breach.png
scene3d: core_explode
imagenote: 봉쇄 코어 폭발
gauge: 봉쇄 코어 출력
gaugesteps: 3, 0
gaugesub: CORE OUTPUT LOST
gaugealert: ⚠ 봉쇄 실패
shake: 0
hold: 6.0

# ── 직원 무전 : 신호가 점점 죽는다 ──
@slide
title: INCOMING RADIO
image: res://assets/cutscene/prologue/disaster_12_radio.png
scene3d: disaster_after
imagenote: 부서진 코어실을 배경으로 무전이 들어온다
speaker: 직원 무전
voice: rabbit
radio: true
signal: 62
text: 지상 관측망이 전부 끊겼습니다!
sfx: noise
jolt: 0.25
hold: 3.0

@slide
image: res://assets/cutscene/prologue/disaster_12_radio.png
scene3d: disaster_after
imagenote: 부서진 코어실을 배경으로 무전이 들어온다
speaker: 직원 무전
voice: wolf
radio: true
signal: 41
text: 격리 구역에서 개체들이 빠져나왔어요!
sfx: noise
jolt: 0.25
hold: 3.0
fx: glitch

@slide
image: res://assets/cutscene/prologue/disaster_12_radio.png
scene3d: disaster_after
imagenote: 부서진 코어실을 배경으로 무전이 들어온다
speaker: 직원 무전
voice: cat
radio: true
signal: 18
cutoff: true
text: 봉쇄 코어 출력이 비정상적으로 떨어졌습니다! 이대로 가다간--!!
sfx: noise
jolt: 0.3
hold: 3.4
fx: glitch

@slide
title: ARCHIVE 02 / EMERGENCY RECORD
image: res://assets/cutscene/prologue/disaster_13_director_last.png
scene3d: director_last
imagenote: 총괄 관리자가 비상 차폐 레버로 걸어가 직접 내린다
speaker: 총괄 관리자
voice: director
text: 큰일이군. 비상 차폐를 가동해!
hold: 7.0

# 암전 + EMERGENCY SEAL — 여기서 사이렌과 지속 흔들림이 멈춘다.
@slide
title:
image:
imagenote:
win: 비상 차폐
winbig: 120:00:00
winsub: EMERGENCY SEAL ENGAGED
sfx: boom
sfxloopstop: siren
shake: 0
hold: 2.8

# 영상이 끊기고 한 박자 쉰다. 화면 구조는 그대로 — 틀을 깨지 않는다.
@slide
title:
image:
imagenote:
hold: 1.6
sfx: cloth_rustle

# 플레이어가 머리를 세게 얻어맞고 책상에 엎어진다.
# fx: impact 가 충격음(impact_blunt) → 강한 흔들림 → 쓰러지는 소리(body_fall) → 암전까지 한 번에 처리한다.
# 이 뒤의 '이명 → 의식 회복' 한 박자는 PrologueDirector 가 맡는다.
@slide
title:
image:
imagenote:
overlay:
hold: 2.6
fx: impact


# ========================================================================
# 프롤로그 #3 — 관리자 권한 승계 콘솔 (오른쪽 CRT)
# ========================================================================
@console authority_transfer
line: NSP FACILITY CONTROL SYSTEM
wait: 0.7
line:
line: > auth --transfer --emergency
wait: 0.9
line: EMERGENCY AUTHORITY TRANSFER...
wait: 1.1
line:
line: PREVIOUS FACILITY ADMINISTRATOR
line:   ID      : ******
line:   STATUS  : DECEASED
wait: 1.0
line:
line: SCANNING SUCCESSOR BIOSIGN...
wait: 1.2
line:   MATCH   : 1 / 1
wait: 0.6
line:
ok: SUCCESSOR AUTHORIZED

# 콘솔이 사라지고 나서 잠깐 떴다 닫히는 승계 완료 창.
# 이 창이 닫힌 뒤에야 GUIDE-0.exe 가 뜬다 — 권한 승계와 GUIDE-0 등장은 별개의 사건이다.
@window authority_done
title: 비상 관리자 권한 승계
big: 완료
line: FACILITY ADMINISTRATOR
line: ACCESS LEVEL : 01
hold: 1.5


# ========================================================================
# 프롤로그 #3 — GUIDE-0 등장
# ========================================================================
@guide g_intro
portrait: normal
panel: none
line: 안녕하세요.
line: 관리자 권한 승계가 완료되었습니다.
line: 현재 상황에 대한 설명이 필요하십니까?

# mode: all — 세 질문을 각각 한 번씩 모두 확인해야 다음 단계로 넘어간다(순서는 자유).
@menu g_main
mode: all
option: 무슨 일이 벌어진 거지? | g_what_happened
option: 나는 누구지? | g_who_am_i
option: 내가 해야 할 일은? | g_mission

# 여기서는 120시간 이야기를 하지 않는다 — 그건 '내가 해야 할 일은?' 의 몫이다.
@guide g_what_happened
portrait: normal
panel: alert
line: 지상에서 /1대규모 재난/0이 발생했고, 동시에 시설 내부의 격리 시스템도 붕괴했습니다.
line: 그 과정에서 생명 유지 장치인 /5봉쇄 코어/0가 심각하게 /1손상/0되었습니다.
icons: employees
line: 현재 현장에서 활동 가능한 직원은 여섯 명입니다.
fx: noise
portrait: sneer
line: 그리고... 직원 여섯 명 중 하나가 /1결번/0으로 확인되었습니다.
line: 사번은 등록되어 있으나, 그 사람이 아닙니다.
line: 시설을 복구하며 이를 알아내는 것이 당신의 과제이겠군요.

@guide g_who_am_i
portrait: normal
panel: authority
line: 관리자님은 사고 이전까지 이 시설의 관제 업무를 담당했습니다.
line: 전임 시설 총괄 관리자는 사고 당시 /1사망/0했습니다.
line: 비상 승계 규정에 따라 모든 관리 권한이 관리자님께 이전되었습니다.
line: 지금부터 직원 배치와 시설 운영의 최종 판단은 관리자님의 몫입니다.

# 이 세 줄이 게임 전체 목표 그 자체다. 옆 정보판에 120:00:00 과 코어 3% → 100% 가 함께 뜬다.
@guide g_mission
portrait: normal
panel: mission
line: 비상 차폐는 앞으로 /5120시간/0만 유지됩니다.
line: 그 안에 직원들을 지휘하여 봉쇄 코어를 /5100% 복구/0하십시오.
line: 그리고 시설 로그와 진술을 비교해 직원들 사이에 숨어 있는 개체를 찾아내십시오.


# ========================================================================
# 프롤로그 #4 — DAY 0 예고
# ========================================================================
@guide g_day0_open
portrait: smile
panel: none
line: 기본 안내가 완료되었습니다.
line: 실제 관리자 업무를 익혀보시죠. 제가 도와드리겠습니다.


# ========================================================================
# 가상 시뮬레이션 기동 — DAY 0 교육 직전 (오른쪽 CRT)
#   이어지는 교육은 실제 근무가 아니라 시뮬레이션이다. 그 사실을 문구가 아니라
#   "시뮬레이터가 실제로 올라가는 화면"으로 보여준다.
# ========================================================================
@console sim_boot
line: NSP TRAINING SUBSYSTEM
wait: 0.7
line:
line: > sim --load nightshift --mode=training
wait: 0.9
line: BUILDING VIRTUAL FACILITY ...
wait: 0.8
line:   STAFF PROFILES      OK
wait: 0.3
line:   FACILITY LAYOUT     OK
wait: 0.3
line:   SCENARIO SCRIPT     OK
wait: 0.6
line:
ok: VIRTUAL SIMULATION READY


# ========================================================================
# DAY 0 튜토리얼 — STEP 별 GUIDE-0 안내
# ========================================================================

# STEP 1 — 배치 화면
@guide tut_intro
portrait: normal
line: 가상 교육 시뮬레이션을 시작하겠습니다.
line: 먼저 현장 직원들을 확인하십시오.

@guide tut_mood
portrait: normal
line: 오늘의 기분은 직원들이 직접 작성한 것입니다.

# STEP 2 — 배치
@guide tut_assign
portrait: normal
line: 먼저 토끼를 끌어다 {ROOM}에 놓아 보십시오.

# 엉뚱하게 놓았을 때의 되짚기. 맞게 놓을 때까지 이 두 줄만 번갈아 뜬다.
@guide tut_assign_wrong_room
portrait: normal
line: 토끼 직원은 {ROOM}에 배치하십시오.

@guide tut_assign_wrong_person
portrait: sneer
line: 눈이 잘못되셨나요? {ROOM}에는 토끼를 배치해 보십시오.

@guide tut_assign_rest
portrait: smile
line: 좋습니다. 작업실을 누르면 그 방이 무엇을 하는 곳인지 오른쪽에 뜹니다.
line: 남은 직원도 배치한 뒤 /5‘근무 시작’/0을 누르십시오.

@guide tut_shift_start
portrait: normal
line: 직원들이 시설 복구 작업을 하고 있습니다.
# 교란(Cross-Room Tamper)을 나중에 이해시키기 위한 씨앗 한 줄. 여기서는 설명만 하고 넘어간다 —
# 별도 단계도, 조작 요구도, 어려운 용어도 쓰지 않는다. (예전에는 작업실 순회 안내 끝에 있었다.)
line: 각 작업실의 기계는 서로 연결되어 있습니다. 한쪽의 문제가 다른 작업실에 영향을 줄 수도 있습니다.

# STEP 3 — 자재가 떨어진다 → 정비실
# 코어 복구가 자재를 다 쓰고 멈춘 **뒤에** 뜬다. 설명이 먼저 오지 않는다.
# 전화(tutorial_material_short)로 직원이 먼저 알리고, GUIDE-0 는 원인과 해결만 한 줄씩 말한다.
@guide tut_materials
portrait: normal
line: 봉쇄 코어 복구에는 /3자재/0가 필요합니다. 지금 보유량이 없습니다.
portrait: normal
line: /5정비실/0을 열었습니다. 직원을 배치하면 자재를 생산할 수 있습니다.

# 작업실을 연 직후 — 배치가 드래그라는 것을 모른 채 멈춰 서 있던 자리다.
# 정비실 · 저장고가 열릴 때 같은 줄을 쓴다(TutorialDirector).
@guide tut_room_assign
portrait: normal
line: 직원을 끌어서 배치해 보십시오.

@guide tut_materials_done
portrait: smile
line: 자재가 들어왔습니다. 코어 복구가 다시 진행됩니다.

# STEP 4 — 보관 한도에 닿는다 → 저장고
@guide tut_storage
portrait: normal
line: 자재 보관 한도가 찼습니다. 넘치는 자재는 그대로 사라집니다.
portrait: normal
line: /5저장고/0를 열었습니다. 직원을 배치하면 보관 한도가 올라갑니다.

@guide tut_storage_done
portrait: normal
line: 저장고는 코어 복구에 드는 /3자재/0 소모도 줄여 줍니다.

# STEP 5 — 사고
# 발전실 사고는 전력 용량을 깎는다 — 조명 · CCTV · 패드 중 하나가 저절로 꺼진다.
# 그 일이 실제로 일어났을 때만 아래 한 묶음이 뜬다(문서 §16).
@guide tut_power_short
portrait: normal
line: 발전 출력이 떨어졌습니다. 전력이 모자라면 일부 장비를 쓸 수 없습니다.
portrait: normal
line: 책상 위 전력 패널에서 지금 무엇이 꺼졌는지 확인하십시오. 수리가 끝나면 되돌아옵니다.

@guide tut_incident
portrait: normal
line: 작업실에 /1사고/0가 발생했습니다. 왼쪽 모니터를 확인하십시오.

@guide tut_relocate
portrait: normal
line: 직원을 끌어다 방을 옮길 수 있습니다. 토끼를 {ROOM}로 옮겨 수리하십시오.
# 큰 설비는 혼자 못 고친다(문서 §17). 수리 막대가 "현재/필요 인원"을 직접 보여 주므로
# 숫자는 되풀이하지 않고 규칙만 한 줄로 말한다.
line: 큰 설비는 혼자 수리할 수 없습니다. 수리 막대의 인원 표시를 확인하십시오.

@guide tut_repair_done
portrait: smile
line: 좋습니다.

# 교육용 사고를 고친 직후 — 실제 근무의 방해공작을 설명한다(DAY0 에 결번은 없다).
@guide tut_sabotage_intro
portrait: normal
line: 실제 근무에서는 방금과 같은 작업실 사고 뿐만 아니라, 누군가 의도적으로 시설을 /1방해/0하는 경우도 있습니다.
portrait: sneer
line: /1방해공작/0이 발생해도 시스템이 범인의 신원까지 알려주지는 않습니다.
portrait: normal
line: 관리자님께서 직접 누가 방해공작을 했는지 알아내셔야 합니다.

# 사고 수리 승인 절차(G-2) — 발전실 사고 수리 중에 한 번만 가르친다.
# 이 블록이 흐르는 동안 RepairApprovalSystem.Paused 로 제한 시간이 멈춘다(TutorialDirector).
@guide tut_approval
portrait: normal
line: 작업실 사고가 발생하면 /1사고 수리 승인 요청/0이 뜹니다. 책상 위 패드에서 /3‘예’/0를 눌러 보십시오.

@guide tut_approval_maze
portrait: normal
line: 승인 요청을 하기 위해선 /5퍼즐/0을 풀어야 합니다.
line: 제한 시간 안에 도착 지점까지 /3방향키/0로 이동해야 하며, 한 번이라도 벽에 닿거나 잘못된 길을 가면 실패합니다.
portrait: sneer
line: 실패하면 수리 시간이 길어지니 주의하십시오.

# STEP 3-B — 이상 개체 (관측으로 소멸)
@guide tut_anomaly_intro
portrait: normal
line: 또한, 최근 작업실에 /1정체불명의 개체/0가 목격되고 있습니다.
portrait: sneer
line: 해당 개체는 관리자 패드에도 뜨지 않고, 경고도 울리지 않습니다. 지금, /1한 마리/0가 들어와 있군요.

@guide tut_anomaly_find
portrait: normal
line: 어느 작업실인지는 알려드릴 수 없습니다. /5CCTV/0를 직접 돌려 찾으십시오.

@guide tut_anomaly_watch
portrait: normal
line: 찾으셨군요. 그 개체는 /2관측되는 것을 견디지 못합니다./0
portrait: normal
line: 화면을 돌리지 말고 /5그대로 계속 보십시오./0

@guide tut_anomaly_done
portrait: normal
line: 개체가 /1소멸/0했습니다. 이렇듯, 개체가 나타나면 관측하여 소멸시켜야 합니다.
portrait: sneer
line: 개체를 놓치면... 어떻게 되는지는 직접 알게 되실 겁니다.

# STEP 4 — 기절 · 의무실 이송
# 교육일에는 스트레스가 잠겨 있어 TutorialDirector 가 직접 쓰러뜨린다(TriggerTutorialFaint).
@guide tut_faint
portrait: sneer
line: 직원 한 명이 쓰러졌습니다. 왼쪽 모니터에서 그 직원 아이콘을 눌러 확인하십시오.

@guide tut_faint_carry
portrait: normal
line: /1스트레스/0가 한계에 도달해 기절했습니다. 기절한 직원은 스스로 한 발짝도 움직이지 못합니다.
line: 이때는 /5다른 직원/0을 불러와 /1의무실/0로 이송해야 합니다.
line: 토끼 직원을 그 작업실로 옮겨 보십시오.

@guide tut_faint_done
portrait: normal
line: 이송이 끝났습니다. 의무실에 눕힌 직원은 시간이 지나면 깨어납니다.
portrait: sneer
line: 쓰러진 채로 방치하면 깨어나지 않습니다. 그 전에 옮기십시오.

# STEP 5 — 전화
@guide tut_call
portrait: normal
line: 직원에게 전화가 왔습니다. 수화기를 들어 보십시오.

@guide tut_call_done
portrait: normal
line: 직원에게 직접 전화를 걸 수도 있고, 직원이 먼저 걸 수도 있습니다.
portrait: sneer
line: 다만 직원의 모든 진술이 /5진실/0일 거란 보장은 없습니다.

# STEP 6 — 기록 비교
@guide tut_endshift
portrait: normal
line: 이제 근무를 종료해 보십시오. 왼쪽 모니터 오른쪽 위의 /3‘근무 종료’/0를 누르십시오.

@guide tut_rest
portrait: normal
line: 근무를 종료하고 나서, 직원들을 심문할 수 있습니다.
portrait: sneer
line: 직원으로 둔갑하여 숨어있는 개체를 추리해야 합니다.
portrait: normal
line: 충분히 의심되는 직원은 /1격리/0할 수 있습니다.
line: /1격리/0된 직원은 근무에서 빠지므로, 판단은 신중하게 하십시오.
line: 이제 토끼 직원을 선택한 뒤, 수화기를 들어 통화해 보십시오.

@guide tut_ask_where
portrait: normal
line: 왼쪽 모니터의 /5조사 자료/0에 그 직원의 기록이 모여 있습니다.
line: 숫자 키 '1'을 눌러 왼쪽 모니터를 확대할 수 있습니다.
line: 토끼 직원의 이동 기록을 누르면, 대화창에 그 자료로 묻는 선택지가 생깁니다.
line: 대화창이 거슬린다면 통화를 종료하지 말고 「접기」를 누르십시오.

@guide tut_contradiction
portrait: normal
line: 진술은 그 자체로 증거가 되지 않습니다.
line: L키로 /5시설 기록/0을 열어 방금 들은 말과 맞대어 보십시오.

@guide tut_dialogue_log
portrait: normal
line: D키를 눌러 이전 진술을 확인할 수 있습니다.

# 대화 기록(D)을 실제로 연 것을 확인한 뒤에야 다음 줄이 뜬다 — 위 줄과 한 블록이면 D 를 누르기도
# 전에 "통화를 종료하라"까지 읽혀 버린다(TutorialDirector 가 두 블록을 차례로 기다린다).
@guide tut_end_call
portrait: normal
line: 이제 통화를 종료해보십시오.

# STEP 7 — 종료
@guide tut_complete
portrait: smile
line: 관리자 교육이 성공적으로 완료되었습니다.
portrait: normal
line: 가상 교육 시뮬레이션을 종료합니다. 다음 근무부터는 실제 시설 기록이 적용됩니다.
portrait: smile
line: 그럼 이제, DAY 1 근무를 시작합니다.

# ── 근무 중 한 번만 뜨는 안내 ─────────────────────────────────────────
# 작은 교란(Cross-Room Tamper)이 처음 화면에 나타난 직후. 한 판에 하루 한 번만 뜬다.
# GUIDE-0 는 범인을 모른다 — "누가 조작했다" 로 단정하지 않는다.
@guide ops_cross_signal
portrait: normal
line: 방금 이상 신호가 잡혔습니다. 문제가 생긴 곳과 원인이 시작된 곳이 다를 수도 있습니다.

# 환기가 처음 멈춘 직후. 한 판에 한 번만 뜬다.
# 상태 변화(전 직원 스트레스 상승)가 이미 시작된 뒤에 설명한다 — 문서 §19.
@guide ops_vent_down
portrait: normal
line: 환기가 멈췄습니다. 수리할 때까지 직원 전원의 /1스트레스/0가 계속 오릅니다.
portrait: normal
line: 환기실로 직원을 보내 고치십시오. 평소에 비워 두면 그만큼 빨리 고장 납니다.

# 실제 근무에서 **의도적인 시설 손상**이 처음 확인된 직후. 한 판에 한 번만 뜬다.
# 정답을 알려주지 않는다 — 누구인지도, 몇 명인지도 말하지 않는다(문서 §21).
@guide ops_first_sabotage
portrait: normal
line: 단순 고장이 아닌 흔적이 확인되었습니다.
portrait: sneer
line: 직원 중 누군가가 시설을 방해하고 있을 가능성이 있습니다.
portrait: normal
line: 누구인지는 시스템이 알려주지 않습니다. 기록과 진술을 맞대어 보십시오.

# ── 시스템 해금 안내 ──────────────────────────────────────────────────
# 잠겨 있던 시스템이 열리는 날, 배치 화면에 들어오는 순간 한 묶음만 뜬다.
# 교육(tut_*)에서 미리 설명하지 않는다 — 쓸 수 없는 것을 먼저 배우면 잊는다.
# 스트레스 두 줄은 원래 tut_mood 에 있던 것을 여기로 옮겼다.
@guide unlock_stress
portrait: normal
line: 오늘부터 직원들의 /1스트레스/0가 기록됩니다. 11 이상 주의, 31 이상 위험이며 위험 구간에서는 작업 효율이 65%까지 떨어집니다.
line: 46에 이르면 /1기절/0합니다. 쓰러진 직원은 스스로 일어나지 못하므로 다른 직원을 보내 /1의무실/0로 옮겨야 합니다.

# 근무 중 1회성 안내 — 어떤 직원이 처음으로 스트레스 '주의' 에 들어갔을 때 한 번만 뜬다.
# {NAME} = 해당 직원 이름. 한 회차에 한 번뿐이라 이후 주의/위험에는 다시 뜨지 않는다.
@scripted hint_stress_caution
text: {NAME}의 스트레스가 '주의' 단계에 진입했습니다. 직원 상태를 확인하십시오.

# ── DAY5 마지막 절차 : 최종 격리 보고서 ───────────────────────────────
# 5일 근무가 끝나고 보고서 화면이 뜨기 직전에 GUIDE-0 이 말한다.
# 여기서 지목한 사람이 엔딩을 가른다 — 제출 뒤에는 되돌릴 수 없다.
@guide final_report
portrait: normal
line: 5일간의 근무가 종료되었습니다.
line: 마지막 절차가 남았습니다. /1격리 대상 지정 보고서/0를 제출하십시오.
line: 지목된 인원은 즉시 격리 후 이송됩니다. 제출 후에는 정정할 수 없습니다.

# 결번 개체가 이미 사망한 판 — 그 카드는 고를 수 없다. 사실만 한 줄 덧붙인다.
@guide final_report_gone
portrait: normal
line: 일부 인원은 이미 응답하지 않습니다.

# STEP 6 — 토끼의 고정 진술(교육용 모순). {FROM_ROOM} = 로그에 남은 원래 작업실.
@scripted tut_rabbit_where
text: 그 시간에는 계속 {FROM_ROOM}에 있었어요. 한 번도 안 나갔는데요?

# "그 뒤에는 어떻게 했습니까?" — 수리를 끝낸 뒤 기절한 동료를 의무실로 옮긴 것까지 말한다.
@scripted tut_rabbit_then
text: 수리 끝내고 바로 고양이 직원 업고 의무실로 갔어요. 침대에 눕히고 나왔습니다.


# ========================================================================
# 개체 영상 (왼쪽 모니터에서 재생되는 ‘영상’. 실제 CCTV 시스템 아님)
#
# ※ 지금은 어디서도 재생하지 않는다. 교육 마지막에 붙어 있었지만
#    "가상 시뮬레이션 종료 → DAY 1" 흐름을 끊어서 뺐다. 데이터는 남겨 둔다.
# ========================================================================
@cutscene outage_ghost
title: ARCHIVE ??? / PLAYBACK

@slide
image: res://assets/cutscene/outage/ghost_01_corridor.png
imagenote: 텅 빈 통로 · 정지된 녹화 화면
sfx: cctv_cut
hold: 1.8
fx: cut

@slide
image: res://assets/cutscene/outage/ghost_02_standing.png
imagenote: 화면 안쪽에 개체가 가만히 서 있다
sfx: drone_loop
hold: 2.4

@slide
image: res://assets/cutscene/outage/ghost_03_approach.png
imagenote: 개체가 카메라 바로 앞까지 다가온다
sfx: pipe_knock
hold: 1.2
fx: shake

@slide
image: res://assets/cutscene/outage/ghost_04_impact.png
imagenote: 개체가 카메라에 머리를 들이받으며 비명을 지른다
sfx: taboo_break
jolt: 0.3
hold: 1.4
fx: glitch

@slide
title:
image:
imagenote:
overlay: SIGNAL LOST
sfx: noise
hold: 0
fx: blackout


# ========================================================================
# DAY 0 스토리 컷인 (@beat) — 3D 관제 화면 위 2D 스탠딩 + 자막
# ========================================================================
#
# 풀스크린 비주얼 노벨이 아니다(문서 §24). 화면은 그대로 두고 스탠딩 한두 명만 올린다.
#
#   speaker / side / expression / hold / exit 를 한 번 적으면 다음 line 들이 물려받는다.
#   side 는 left · right 두 자리뿐이고, 같은 자리에 다른 직원이 오면 그 자리가 교체된다.
#   expression 은 smile · bad. 그 그림이 없으면 그 직원의 평소 표정으로 떨어진다.
#
# 여기 있는 것은 **직원끼리 주고받는 말**이다. 직원이 관리자에게 직접 하는 말은
# 전부 전화(NSP_DIALOGUE_RUNTIME.md)로만 간다 — 문서 §2.1.

# ── Beat A — 가상 시뮬레이션 시작 (배치 화면, tut_mood 다음) ─────────────
# 직원 소개를 길게 하지 않는다. 세 사람이 각자 다른 태도를 한 줄씩 보이면 그걸로 끝이다.
@beat day0_start
speaker: rabbit
side: left
expression: smile
line: 진짜 시작하는 거예요? 생각보다 본격적이네요!
speaker: cat
side: right
expression:
line: 연습이면 빨리 끝내죠.
speaker: dog
side: left
expression: smile
line: 천천히 하면 괜찮을 거예요. 다들 처음이잖아요.

# ── Beat B — 첫 사고를 수습한 직후 (근무 중) ────────────────────────────
# "사고와 재배치가 직원에게는 실제 상황이었다"를 보여 준다.
# 덤으로 큰 설비는 둘이 붙어야 한다는 규칙(문서 §17)을 사람 말로 한 번 더 짚는다.
@beat day0_incident_done
speaker: wolf
side: left
line: 발전기 출력, 정상 범위로 돌아왔습니다.
speaker: sheep
side: right
expression: bad
line: 저, 저는 또 뭐가 터지는 줄 알았어요...
speaker: wolf
side: left
line: 둘이 붙었으니 끝난 겁니다. 혼자였으면 아직 매달려 있었을 겁니다.

# ── Beat C — 휴게시간 맛보기 (근무 종료 후) ─────────────────────────────
# 관계성과 함께 "진술은 그 자체로 증거가 아니다"라는 다음 단계의 공기를 깐다.
@beat day0_rest
speaker: fox
side: left
expression: smile
line: 관리자님이 아까부터 우리를 하나씩 불러 보시던데요~
speaker: cat
side: right
line: 물어보면 대답하면 되죠. 숨길 게 있는 사람이나 신경 쓰겠죠.
speaker: fox
side: left
expression:
line: 그렇죠. 숨길 게 있는 사람이나.

# ── 트루엔딩 — 마지막 휴게실 (모니터2) ──────────────────────────────────
# 코어 복구와 지목이 둘 다 끝난 뒤, 시설이 다시 돌기 시작한 그 밤의 휴게실.
# DAY1 의 공기와 정반대다 — 긴장이 풀렸고, 피곤하고, 살아남았다는 안도와 약간의 허무함.
#
# 누가 남아 있는지는 판마다 다르다(지목된 직원은 이 자리에 없다). 그래서 전부 line? 로
# 적는다 — 자리에 없는 직원의 줄은 조용히 건너뛴다. 마지막 한 줄만은 반드시 나와야 하므로
# 남아 있을 확률이 가장 높은 쪽에 두지 않고, 앞의 누군가가 받도록 두 번 적어 둔다.
@beat ending_true_rest
pause: true
# 엔딩에는 플레이어 입력이 없다(책상이 잠겨 있다) — 한 줄씩 저절로 넘어가게 hold 를 건다.
hold: 2.6
speaker: dog
side: left
line?: 끝났네요. 진짜로.
speaker: sheep
side: right
expression: bad
line?: 아직도 손이 떨려요. 다 끝났다는 게 실감이 안 나서...
speaker: cat
side: left
line?: 실감은 자고 일어나면 날 거예요. 지금은 그냥 앉아 있어도 돼요.
speaker: fox
side: right
expression: smile
line?: 이 방이 이렇게 조용한 건 처음이네요~ 기계 소리도 안 나고.
speaker: rabbit
side: left
line?: 저, 솔직히 어제까진 못 버틸 줄 알았어요.
speaker: wolf
side: right
line?: 버텼습니다. 그거면 됩니다.
# 마지막 한마디 — 5일간의 관리를 직원들이 인정하는 자리다(연출 문서 §23).
speaker: dog
side: left
expression: smile
line?: 관리자님. 정말 고생 많으셨어요.
speaker: cat
side: left
line?: 관리자님도 이제 좀 쉬세요.

# ── Beat D — 교육 종료 직전 ─────────────────────────────────────────────
# 짧은 불안 하나만 남기고 끝낸다. 설명하지 않는다.
@beat day0_end
speaker: rabbit
side: left
line: 오늘 거 전부 연습인 거죠? 진짜 아니죠?
speaker: sheep
side: right
expression: bad
line: ...연습인데도, 저는 왜 이렇게 무서웠을까요.
