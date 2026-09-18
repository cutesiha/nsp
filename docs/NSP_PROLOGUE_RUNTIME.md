# NSP PROLOGUE / DAY0 RUNTIME
#
# 프롤로그 컷씬 · GUIDE-0 대사 · DAY0 튜토리얼 안내문을 전부 담는 런타임 데이터 파일.
# 원안은 docs/NSP_PROLOGUE_AND_TUTORIAL.md 이고, 이 파일은 코드가 읽는 실행용 사본이다.
#
# ★ 문구를 고치고 싶으면 이 파일만 고치면 된다. 코드는 건드릴 필요가 없다.
# ★ 컷씬 이미지도 image: 줄의 경로만 바꿔 끼우면 된다.
#   - image: 가 비어 있거나 파일이 없으면 화면에 "[ IMAGE ] + imagenote" 임시 패널이 뜬다.
#   - 최종 일러스트가 나오면 assets/cutscene/... 에 넣고 image: 경로만 채우면 끝.
#
# ── 파싱 규칙 ────────────────────────────────────────────────────────────
#   '#' 로 시작하는 줄, 빈 줄 = 무시
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
#                               cat crow fox jellyfish owl rabbit = 기존 직원 보이스 그대로
#       radio:     true         무전/인터컴으로 들리게 한다(앞뒤 치직 + 약한 잡음 + 무전 필터).
#                               직원 보이스를 바꾸지 않고 Radio 버스로만 통과시킨다.
#       text:      <text>       자막 한 줄 → 이 줄이 있으면 절대 자동으로 안 넘어간다
#                               (스페이스 / 엔터 / 클릭으로만 진행)
#       overlay:   <text>       화면 한가운데 크게 뜨는 텍스트 (EMERGENCY SEAL 등). 타이핑된다.
#       hold:      <초>         대사가 없는 컷을 몇 초 보여줄지(타이핑이 끝난 뒤부터 센다).
#                               0 이면 그 컷도 입력을 기다린다.
#       sfx:       <키>         assets/audio/sfx/<키>.wav 를 슬라이드 시작에 1회 재생
#       sfxloop:   <키>         그 효과음을 반복 재생 시작(사이렌 등). 컷씬이 끝나면 자동 중단.
#       sfxloopstop:<키>        반복 재생 중단
#       shake:     <px>         화면이 계속 미세하게 떨리는 세기. 다음 슬라이드로 이어진다(0 이면 해제)
#       jolt:      <초>         슬라이드가 뜨는 순간 좌우로 짧게 흔들리는 시간
#       fx:        none|glitch|cut|siren|shake|blackout|typing|impact
#
#   @console <id>       오른쪽 CRT 의 cmd 콘솔 연출
#     line: <text>         한 줄 출력 (값이 없으면 빈 줄)
#     wait: <초>           다음 줄까지 대기
#     ok:   <text>         성공 메시지(밝게 + 띠롱 효과음)
#
#   @guide <id>         GUIDE-0 홀로그램 대사 묶음
#     portrait: <표정키>   assets/ui/guide0/guide0_<표정키>.png 를 찾는다(없으면 임시 초상)
#     voice: <보이스 id>   이 묶음의 타이핑 보이스(기본 guide0)
#     line: <text>         대사 한 줄(순서대로)
#     icons: employees     그 자리에서 직원 아이콘 6개를 띄운다
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
# ════════════════════════════════════════════════════════════════════════


# ========================================================================
# 프롤로그 #1 — 낡은 연구소 홍보 기록 영상
# ========================================================================
@cutscene prologue_archive
title: 국가특수에너지연구원 제7지하시설 기록 영상 / ARCHIVE 01

@slide
image: res://assets/cutscene/prologue/archive_01_facility.png
imagenote: 밝고 멀쩡한 시설 전경 · 연구원들 · 정상 가동 중인 코어
sfx: crt_hum
hold: 2.2
fx: typing

@slide
image: res://assets/cutscene/prologue/archive_02_director.png
figure: res://assets/cutscene/prologue/figure_director.png
figurenote: 연구소 총괄 관리자 상반신 일러스트
imagenote: 연구소 총괄 관리자 클로즈업
speaker: 나레이션
voice: director
text: 국가특수에너지연구원은 미지의 개체, 통칭 ‘존재’가 발생시키는 에너지를 연구해 왔습니다.
hold: 4.6

@slide
image: res://assets/cutscene/prologue/archive_03_habitat.png
figure: res://assets/cutscene/prologue/figure_director.png
figurenote: 연구소 총괄 관리자 상반신 일러스트
imagenote: 연구동 내부 · 안정적으로 유지되는 생활 구역
speaker: 나레이션
voice: director
text: 이 연구를 통해 우리는 외부 환경과 단절된 시설에서도 안정적인 생존 환경을 유지할 수 있었습니다.
hold: 5.0

@slide
image: res://assets/cutscene/prologue/archive_04_core.png
figure: res://assets/cutscene/prologue/figure_director.png
figurenote: 연구소 총괄 관리자 상반신 일러스트
imagenote: 봉쇄 코어 클로즈업 · 푸른 빛으로 안정 가동
speaker: 나레이션
voice: director
text: 시설의 중심에는 생명 유지와 외부 차폐를 담당하는 봉쇄 코어가 있습니다.
hold: 4.8


# ========================================================================
# 프롤로그 #2 — 대재난
# ========================================================================
@cutscene prologue_disaster
title: 00 지하연구시설 기록 영상 / ARCHIVE 01

# 총괄 관리자의 얼굴이 일그러지고 신호가 깨진다
# shake: 는 다음 슬라이드로 계속 이어진다 — 암전 컷에서 0 으로 되돌린다.
# sfxloop: siren 도 sfxloopstop 을 만날 때까지 계속 울린다.
@slide
image: res://assets/cutscene/prologue/disaster_01_director_glitch.png
imagenote: 총괄 관리자의 얼굴이 일그러지며 화면이 찢어짐
sfx: noise
sfxloop: siren
shake: 2.5
hold: 2.6
fx: glitch

@slide
title: SIGNAL LOST
image: res://assets/cutscene/prologue/disaster_02_signal_lost.png
imagenote: 완전히 깨진 화면 · 삐-- 하는 신호음
sfx: alarm
hold: 1.6
fx: siren

# 빠르게 전환되는 컷들
@slide
title: ARCHIVE 02 / EMERGENCY RECORD
image: res://assets/cutscene/prologue/disaster_03_surface_red.png
imagenote: 지상 관측 카메라가 붉게 물듦
sfx: alarm
hold: 0.75
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_04_lab_wreck.png
imagenote: 연구실 파손 · 집기가 쏟아짐
sfx: glass_shatter
hold: 0.75
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_05_staff_running.png
imagenote: 직원이 허겁지겁 복도를 뛰어감
sfx: footsteps_run
hold: 0.9
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_06_door_closing.png
imagenote: 차폐문이 닫힘
sfx: metal_clang
hold: 0.75
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_07_cctv_shadow.png
imagenote: CCTV 화면 가장자리를 무언가가 스쳐 지나감
sfx: cctv_cut
hold: 0.85
fx: cut

@slide
image: res://assets/cutscene/prologue/disaster_08_core_drop.png
imagenote: 코어 출력 게이지가 급락
sfx: power_down
hold: 0.85
fx: cut

# 코어실 — 타이핑되는 경고
# 09 / 11 은 일부러 그림이 없다 — 검은 화면에 경고 문구만 타이핑되는 단말기 컷이다.
@slide
title: CORE CHAMBER / LIVE
image:
imagenote:
sfx: sensor_beep
overlay: EXTERNAL CATASTROPHE DETECTED
hold: 1.9
fx: typing

@slide
image: res://assets/cutscene/prologue/disaster_10_core_breach.png
imagenote: 코어실 · 차폐막이 깨지는 순간
overlay: CONTAINMENT FAILURE
sfx: rubble_collapse
hold: 1.9
fx: typing

@slide
image:
imagenote:
overlay: CORE OUTPUT 3%
sfx: alarm
hold: 2.2
fx: glitch

# 무전
@slide
image: res://assets/cutscene/prologue/disaster_12_radio.png
imagenote: 노이즈가 낀 무전 화면
speaker: 직원 무전
voice: owl
radio: true
text: 지상 관측망이 전부 끊겼습니다!
sfx: noise
jolt: 0.25
hold: 3.0

@slide
image: res://assets/cutscene/prologue/disaster_12_radio.png
imagenote: 노이즈가 낀 무전 화면
speaker: 직원 무전
voice: jellyfish
radio: true
text: 격리 구역에서 개체들이 빠져나왔어요!
sfx: noise
jolt: 0.25
hold: 3.0

@slide
image: res://assets/cutscene/prologue/disaster_12_radio.png
imagenote: 노이즈가 낀 무전 화면
speaker: 직원 무전
voice: crow
radio: true
text: 봉쇄 코어 출력이 비정상적으로 떨어졌습니다! 이대로 가다간--!!
sfx: noise
jolt: 0.25
hold: 3.4

@slide
image: res://assets/cutscene/prologue/disaster_13_director_last.png
imagenote: 총괄 관리자 · 마지막 지시
speaker: 총괄 관리자
voice: director
text: 큰일이군. 비상 차폐를 가동해!
sfx: switch
hold: 3.2

# 암전 + EMERGENCY SEAL — 여기서 사이렌과 지속 흔들림이 멈춘다.
@slide
title:
image:
imagenote:
overlay: EMERGENCY SEAL — 120:00:00
sfx: boom
sfxloopstop: siren
shake: 0
hold: 2.4
fx: blackout

# 플레이어가 머리를 세게 얻어맞고 책상에 엎어진다.
# fx: impact 가 충격음(impact_blunt) → 강한 흔들림 → 쓰러지는 소리(body_fall) → 암전까지 한 번에 처리한다.
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


# ========================================================================
# 프롤로그 #3 — GUIDE-0 등장
# ========================================================================
@guide g_intro
portrait: normal
line: 안녕하세요.
line: 관리자 권한 승계가 완료되었습니다.
line: 현재 상황에 대한 설명이 필요하십니까?

# mode: all — 세 질문을 각각 한 번씩 모두 확인해야 다음 단계로 넘어간다(순서는 자유).
@menu g_main
mode: all
option: 무슨 일이 벌어진 거지? | g_what_happened
option: 나는 누구지? | g_who_am_i
option: 내가 해야 할 일은? | g_mission

@guide g_what_happened
portrait: normal
line: 시설 외부에서 대규모 재난이 발생했습니다.
line: 현재 지상 환경은 생존에 적합하지 않은 상태입니다.
line: 동시에 시설 내부에서도 격리 사고가 발생하여 봉쇄 코어가 심각하게 손상되었습니다.
line: 현재 시설은 비상 차폐 시스템을 통해 외부 환경과 격리되어 있습니다.
line: 하지만 비상 차폐의 유지 가능 시간은 120시간입니다.
line: 현장 활동이 가능한 직원은 여섯 명입니다.
icons: employees
line: 직원들과 함께 봉쇄 코어를 복구해야 합니다.
fx: noise
line: 다만...
line: 직원 신원 기록 중 하나가 일치하지 않습니다.
line: 정체불명의 개체가 직원들 사이에 포함되어 있을 가능성이 있습니다.

@guide g_who_am_i
portrait: normal
line: 관리자님께서는 사고 이전부터 본 시설의 관제 업무를 담당하고 있었습니다.
line: 그러나 시설 전체에 대한 최종 관리 권한은 보유하고 있지 않았습니다.
line: 기존 시설 총괄 관리자는 사고 발생 당시 사망한 것으로 확인되었습니다.
line: 비상 관리 규정에 따라 차순위 권한자인 관리자님께 모든 시설 관리 권한이 승계되었습니다.
line: 현재 관리자님은 이 시설의 총괄 관리자입니다.
line: 현장 직원 여섯 명의 배치와 시설 운영, 그리고 비상 상황에 대한 최종 판단을 담당하게 됩니다.

@guide g_mission
portrait: normal
line: 비상 차폐 시스템의 예상 유지 시간은 120시간입니다.
line: 그전에 봉쇄 코어를 완전히 복구해야 합니다.
line: 봉쇄 코어가 복구되지 않을 경우 시설의 차폐 및 생명 유지 기능을 더 이상 유지할 수 없습니다.
line: 직원들을 작업실에 배치하여 시설을 복구하십시오.
line: 시설 로그와 직원들의 진술도 확인하십시오.
line: 현재 여섯 직원 중 정체불명의 개체가 포함되어 있을 가능성이 있습니다.
line: 시설 복구를 방해하는 개체를 찾아내는 것 역시 관리자님의 임무입니다.


# ========================================================================
# 프롤로그 #4 — DAY 0 예고
# ========================================================================
@guide g_day0_open
portrait: normal
line: 현재 상황에 대한 기본 안내가 완료되었습니다.
line: 관리자 업무는 이번이 처음이시군요.
portrait: smile
line: 걱정하지 마십시오.
line: 제가 도와드리겠습니다.


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
line: 배치할 때 참고할 수 있습니다.

# STEP 2 — 배치
@guide tut_assign
portrait: normal
line: 직원을 작업실에 배치하면 시설 복구 작업이 시작됩니다.
line: 먼저 토끼를 끌어다 정비실에 놓아 보십시오.

@guide tut_assign_rest
portrait: smile
line: 좋습니다.
line: 남은 직원도 배치한 뒤 ‘근무 시작’을 누르십시오.

@guide tut_shift_start
portrait: normal
line: 봉쇄 코어 복구율이 상승하고 있습니다.

# STEP 3 — 사고
@guide tut_incident
portrait: normal
line: 작업실에 문제가 발생했습니다.
line: MONITOR 01을 확인하십시오.

@guide tut_relocate
portrait: normal
line: 직원을 끌어다 방을 옮길 수 있습니다.
line: 토끼를 {ROOM}으로 옮겨 수리하십시오.

@guide tut_repair_done
portrait: smile
line: 좋습니다.

# STEP 4 — 시설 로그
@guide tut_log
portrait: normal
line: L키를 눌러 시설 로그를 열어 보십시오.

@guide tut_log_done
portrait: normal
line: 이 기록은 시설이 확인한 사실입니다.

# STEP 5 — 전화
@guide tut_call
portrait: normal
line: 직원에게 전화가 왔습니다. 수화기를 들어 보십시오.

@guide tut_call_done
portrait: normal
line: 직원의 진술은 시설 기록과 일치하지 않을 수도 있습니다.
line: 불일치가 발견되면 그 원인을 확인하십시오.

# STEP 6 — 기록 비교
@guide tut_endshift
portrait: normal
line: 이제 근무를 종료해 보십시오.
line: MONITOR 01 오른쪽 위의 ‘근무 종료’를 누르십시오.

@guide tut_rest
portrait: normal
line: 다음은 교육용 사례를 보여드리겠습니다.
line: 토끼 직원을 선택한 뒤, 수화기를 들어 통화해 보십시오.

@guide tut_ask_where
portrait: normal
line: ‘사고 당시 어디에 있었습니까?’ 를 물어보십시오.

@guide tut_contradiction
portrait: normal
line: 시설 기록과 진술이 일치하지 않습니다.
line: L키로 시설 기록을 확인해 보십시오.

@guide tut_dialogue_log
portrait: normal
line: D키를 눌러 이전 진술을 확인할 수 있습니다.

# STEP 7 — 종료 + 결번자
@guide tut_complete
portrait: smile
line: 관리자 교육이 성공적으로 완료되었습니다.

@guide tut_after_ghost
portrait: normal
line: ...
line: ...영상 오류입니다.
line: 그럼 이제, DAY 1 근무를 시작합니다.

# STEP 6 — 토끼의 고정 진술(교육용 모순). {FROM_ROOM} = 로그에 남은 원래 작업실.
@scripted tut_rabbit_where
text: 그 시간에는 계속 {FROM_ROOM}에 있었어요. 한 번도 안 나갔는데요?


# ========================================================================
# STEP 7 — 결번자 영상 (왼쪽 모니터에서 재생되는 ‘영상’. 실제 CCTV 시스템 아님)
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
