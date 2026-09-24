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

# CRT 가 켜지고 노이즈가 걷히며 흑백 기록 영상이 시작된다.
@slide
title: 국가특수에너지연구원 제7지하시설 / ARCHIVE 01
image: res://assets/cutscene/prologue/archive_02_director.png
imagenote: 연구소 총괄 관리자 클로즈업
sfx: crt_on
hold: 1.6
fx: crt

@slide
image: res://assets/cutscene/prologue/archive_02_director.png
imagenote: 연구소 총괄 관리자 클로즈업
speaker: 총괄 관리자
voice: director
text: 제7지하시설 근무자 여러분, 반갑습니다.
hold: 3.0

@slide
image: res://assets/cutscene/prologue/archive_02_director.png
imagenote: 연구소 총괄 관리자 클로즈업
speaker: 총괄 관리자
voice: director
text: 본 기록은 시설의 핵심 설비와 비상 절차를 안내하기 위해 제작되었습니다.
hold: 4.2

@slide
image: res://assets/cutscene/prologue/archive_01_facility.png
imagenote: 밝고 멀쩡한 시설 전경 · 연구원들 · 정상 가동 중인 코어
speaker: 총괄 관리자
voice: director
text: 국가특수에너지연구원은 미지의 개체, 통칭 ‘존재’가 발생시키는 에너지를 연구해 왔습니다.
hold: 4.6

@slide
image: res://assets/cutscene/prologue/archive_03_habitat.png
imagenote: 연구동 내부 · 안정적으로 유지되는 생활 구역
speaker: 총괄 관리자
voice: director
text: 이 연구를 통해 우리는 외부 환경과 단절된 시설에서도 안정적인 생존 환경을 유지할 수 있었습니다.
hold: 5.0

@slide
image: res://assets/cutscene/prologue/archive_04_core.png
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
imagenote: 직전 컷 그대로 — 화면이 찢어지며 뒤틀린다
ken: off
sfx: noise
sfxloop: siren
shake: 3.0
hold: 2.6
fx: warp

# 일그러진 그 상태에서 신호가 끊긴다.
@slide
title: SIGNAL LOST
image: res://assets/cutscene/prologue/archive_04_core.png
imagenote:
ken: off
overlay: SIGNAL LOST
sfx: alarm
hold: 1.6
fx: warphold

# ── 비상 경보창 : 실제 시설 경보 패널처럼 뜬다 ──
@slide
title: EMERGENCY BROADCAST
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

# ── 빠르게 지나가는 몽타주 ──
# fx: cut 은 컷마다 무작위로 효과 하나를 고른다(펀치 줌 / 흔들림 / 붉은 섬광 / 팬 / 글리치).
@slide
title: ARCHIVE 02 / EMERGENCY RECORD
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
imagenote: 지상 관측 카메라가 붉게 물듦
sfx: alarm
hold: 0.7
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_04_lab_wreck.png
imagenote: 연구실 파손 · 집기가 쏟아짐
sfx: glass_shatter
hold: 0.7
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_05_staff_running.png
imagenote: 직원이 허겁지겁 복도를 뛰어감
sfx: footsteps_run
hold: 0.8
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_06_door_closing.png
imagenote: 차폐문이 닫힘
sfx: metal_clang
hold: 0.7
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_07_cctv_shadow.png
imagenote: CCTV 화면 가장자리를 무언가가 스쳐 지나감
sfx: cctv_cut
hold: 0.8
fx: cut

# ── 봉쇄 코어 출력 저하 : 숫자와 막대가 실제로 내려간다 ──
# 영문은 보조 문구일 뿐이고, 플레이어가 읽어야 하는 주 정보는 전부 한글이다.
@slide
title: CORE CHAMBER / LIVE
image: res://assets/cutscene/prologue/disaster_08_core_drop.png
imagenote: 코어실 · 출력 게이지가 급락하기 시작
gauge: 봉쇄 코어 출력
gaugesteps: 100, 74, 41
gaugesub: CORE OUTPUT DROPPING
sfx: power_down
shake: 3.4
hold: 3.0
fx: flicker

@slide
image: res://assets/cutscene/prologue/disaster_10_core_breach.png
imagenote: 코어실 · 차폐막이 깨지는 순간
gauge: 봉쇄 코어 출력
gaugesteps: 41, 16, 3
gaugesub: CORE OUTPUT CRITICAL
gaugealert: ⚠ 치명적 출력 저하
sfx: rubble_collapse
hold: 3.4
fx: alert

# ── 직원 무전 : 신호가 점점 죽는다 ──
@slide
title: INCOMING RADIO
image: res://assets/cutscene/prologue/disaster_12_radio.png
imagenote: 노이즈가 낀 무전 화면
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
imagenote: 노이즈가 낀 무전 화면
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
imagenote: 노이즈가 낀 무전 화면
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
image: res://assets/cutscene/prologue/disaster_12_radio.png
imagenote: 총괄 관리자 · 마지막 지시
speaker: 총괄 관리자
voice: director
text: 큰일이군. 비상 차폐를 가동해!
sfxafter: switch
hold: 3.2

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
line: 그리고... 직원들 사이에 숨어 우리를 방해하려는 /1정체불명의 개체/0가 확인되었습니다. 
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
line: 또한 직원들은 근무 중 /1스트레스/0를 받을 수 있습니다.
line: 스트레스가 한계에 도달하면 직원이 /1기절/0할 수 있으니 상태를 확인하십시오.

# ── 시설 CCTV 투어 (tut_mood 와 tut_assign 사이) ──────────────────────
# 왼쪽 지도의 작업실을 **직접 누르면** MONITOR 02 가 그 방 CCTV 로 바뀌고 한 줄이 나온다.
# 여덟 방을 다 눌러야 넘어간다. 한 방당 한 줄만 쓴다.
@guide tut_facility_intro
portrait: normal
line: 배치에 앞서 작업실의 기능을 간단히 안내하겠습니다.
portrait: normal
line: 왼쪽 지도에서 작업실을 하나 눌러 보십시오.

# 두 번째 방을 누를 때 딱 한 번만 뜬다(여덟 번 반복하면 읽지 않게 된다).
@guide tut_facility_pick
portrait: normal
line: 나머지 작업실도 눌러 보십시오.

@guide tut_room_core
portrait: normal
line: 코어실은 /5봉쇄 코어/0를 관리하고 복구하는 시설의 핵심 작업실입니다.

@guide tut_room_maintenance
portrait: normal
line: 정비실은 봉쇄 코어 복구에 필요한 /3자재/0를 생산합니다.

@guide tut_room_storage
portrait: normal
line: 저장고는 봉쇄 코어 복구에 필요한 /3자재/0의 상한을 관리합니다.

@guide tut_room_guard
portrait: normal
line: 경비실은 시설을 감시합니다. 인원을 배치하면 /1방해공작/0 억제에 도움이 됩니다.

@guide tut_room_power
portrait: normal
line: 발전실은 시설 필요한 /5전력/0을 공급합니다. /5전력/0이 없으면 cctv, 조명 등을 켤 수 없습니다.

@guide tut_room_vent
portrait: normal
line: 환기실은 시설 공조를 유지해 직원들의 /1스트레스/0 상승을 억제합니다.

@guide tut_room_medical
portrait: normal
line: 의무실은 스트레스가 높은 직원을 /4회복/0시킵니다. /1기절/0한 직원도 이곳으로 이송됩니다.

@guide tut_room_isolation
portrait: sneer
line: /1격리실/0은 의심되는 직원을 다른 직원들과 분리하는 공간입니다.

@guide tut_facility_end
portrait: smile
line: 설명은 여기까지입니다. 이제 직접 배치해 보시죠.

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
line: 좋습니다. 오른쪽 방 카드의 /5첫 줄/0이 그 방이 지금 무엇을 만들어 내는지 알려 줍니다.
line: 남은 직원도 배치한 뒤 /5‘근무 시작’/0을 누르십시오.

@guide tut_shift_start
portrait: normal
line: 직원들이 시설 복구 작업을 하고 있습니다.

# STEP 3 — 사고
@guide tut_incident
portrait: normal
line: 작업실에 /1사고/0가 발생했습니다. 왼쪽 모니터를 확인하십시오.

@guide tut_relocate
portrait: normal
line: 직원을 끌어다 방을 옮길 수 있습니다. 토끼를 {ROOM}로 옮겨 수리하십시오.

@guide tut_repair_done
portrait: smile
line: 좋습니다.

# 교육용 사고를 고친 직후 — 실제 근무의 방해공작을 설명한다(DAY0 에 방해자는 없다).
@guide tut_sabotage_intro
portrait: normal
line: 실제 근무에서는 방금과 같은 작업실 사고 뿐만 아니라, 누군가 의도적으로 시설을 /1방해/0하는 경우도 있습니다.
portrait: sneer
line: /1방해공작/0이 발생해도 시스템이 범인의 신원까지 알려주지는 않습니다.
portrait: normal
line: 관리자님께서 직접 누가 방해공작을 했는지 알아내셔야 합니다.

# STEP 3-B — 이상 개체 (관측으로 소멸)
@guide tut_anomaly_intro
portrait: normal
line: 또한, 최근 작업실에 /1정체불명의 개체/0가 목격되고 있습니다.
portrait: sneer
line: 해당 개체는 센서에도 잡히지 않고, 경고도 울리지 않습니다. 지금, /1한 마리/0가 들어와 있군요.

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
line: 이제 통화를 종료해보십시오.

# STEP 7 — 종료
@guide tut_complete
portrait: smile
line: 관리자 교육이 성공적으로 완료되었습니다.
line: 그럼 이제, DAY 1 근무를 시작합니다.

# 근무 중 1회성 안내 — 어떤 직원이 처음으로 스트레스 '주의' 에 들어갔을 때 한 번만 뜬다.
# {NAME} = 해당 직원 이름. 한 회차에 한 번뿐이라 이후 주의/위험에는 다시 뜨지 않는다.
@scripted hint_stress_caution
text: {NAME}의 스트레스가 '주의' 단계에 진입했습니다. 직원 상태를 확인하십시오.

# STEP 6 — 토끼의 고정 진술(교육용 모순). {FROM_ROOM} = 로그에 남은 원래 작업실.
@scripted tut_rabbit_where
text: 그 시간에는 계속 {FROM_ROOM}에 있었어요. 한 번도 안 나갔는데요?


# ========================================================================
# 결번자 영상 (왼쪽 모니터에서 재생되는 ‘영상’. 실제 CCTV 시스템 아님)
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
imagenote: 화면 안쪽에 결번자가 가만히 서 있다
sfx: drone_loop
hold: 2.4

@slide
image: res://assets/cutscene/outage/ghost_03_approach.png
imagenote: 결번자가 카메라 바로 앞까지 다가온다
sfx: pipe_knock
hold: 1.2
fx: shake

@slide
image: res://assets/cutscene/outage/ghost_04_impact.png
imagenote: 결번자가 카메라에 머리를 들이받으며 비명을 지른다
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
