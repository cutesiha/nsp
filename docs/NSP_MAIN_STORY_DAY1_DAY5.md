# NSP MAIN STORY — DAY1~DAY5
## REVISED GROUP CONVERSATION VERSION
## 대상 브랜치
`ver3(simple_version)`

---

# 0. 이 문서의 역할

이 문서는 DAY1~5 시작 시 재생되는 메인 스토리 런타임 문서입니다.

핵심 변경점:

1. DAY1~5의 메인 스토리를 **2인 대화 묶음**이 아니라 **휴게실에 모인 직원들의 집단 대화**로 변경합니다.
2. DAY1의 세계관 오류를 수정합니다.
   - 직원들이 재난 이후 새로 배정된 것이 아닙니다.
   - 원래 제7지하시설에서 일하던 직원들 중 살아남은 5명 + 직원으로 둔갑한 결번자 1명입니다.
3. DAY1은 평화로운 자기소개로 시작하지 않습니다.
   - 대재난 직후 패닉 / 상실 / 공포 / 체념이 먼저 나옵니다.
   - 이후 관리자가 처음으로 이들을 이끌겠다는 선택을 합니다.
   - 그 다음에야 서로 이름과 기존 업무를 확인합니다.
4. DAY1~5 매일 **플레이어 선택지**가 최소 1회 등장합니다.
5. 선택지는 기본 3개를 동시에 보여 줍니다.
6. 선택은 기본적으로 즉각적인 대사 반응만 바꾸며 게임 밸런스/범인 판정에는 영향을 주지 않습니다.

---

# 1. 핵심 연출 원칙

## 1.1 직원 6명이 "한 공간에서 같이 이야기한다"

DAY 시작 스토리는 기본적으로 휴게실에서 진행합니다.

직원 6명이 긴 테이블 주변에 앉아 있으며,
누군가 말하면 다른 사람들이:

- 받아치고
- 끼어들고
- 말리거나
- 동의하거나
- 무시하거나
- 분위기를 바꾸는

식으로 이어져야 합니다.

금지:

- 토끼↔여우 4줄
- 양↔강아지 4줄
- 고양이↔늑대 4줄

처럼 서로 완전히 분리된 2인 대화 여러 개를 이어 붙이는 구성.

관계가 좋은 둘이 잠깐 주고받는 것은 가능하지만,
그 대화에는 다른 직원이 최소 한 번 이상 자연스럽게 끼어들어
**같은 테이블에서 모두 듣고 있다는 느낌**을 유지합니다.

---

# 2. 결번자 관련 원칙

결번이 누구인지에 따라 메인 스토리를 분기하지 않습니다.

메인 스토리 대사는:

- rabbit
- cat
- fox
- sheep
- wolf
- dog

누가 결번이어도 어색하지 않아야 합니다.

스토리에서 범인을 알려 주는 단서를 만들지 않습니다.

추리는 기존:

- Facility Log
- CCTV Evidence
- 직원 진술
- 대화 기록
- 인터뷰
- 모순 추궁

이 담당합니다.

---

# 3. DAY1 세계관 원칙

기존 문서의:

> 여섯 명 모두 "원래 여기 올 사람이 아니었다"

설정은 폐기합니다.

대재난 이전 제7지하시설에는 많은 직원이 있었습니다.

재난 이후:

- 총괄 관리자 사망
- 다수 직원 사망
- 기존 관제 담당 관리자에게 비상 권한 승계
- 현장 활동 가능 인원은 겉보기상 6명
- 실제 인간 생존자는 5명
- 나머지 1명은 직원으로 둔갑한 결번자

입니다.

DAY1 등장인물들은 **재난이 터진 뒤 갑자기 다른 시설에서 배정받아 온 사람들이 아닙니다.**

모두 제7지하시설 내부에서 일하던 직원이었으며,
서로 다른 구역/업무 때문에 서로 잘 모르던 사이였다고 처리합니다.

따라서 DAY1 자기소개는:

"왜 이 시설에 새로 왔나"

가 아니라:

- 이름/코드네임
- 원래 어떤 업무를 했는가
- 사고 당시 어디쯤 있었는가
- 어떻게 여기까지 살아남았는가

정도를 공유하는 장면입니다.

모든 정보는 결번자가 죽은 직원의 이력을 흉내 내고 있어도 성립할 정도로 작성합니다.

---

# 4. 관리자와 직원의 대화 수단

관리자가 직원에게 직접 말하는 순간은 기존 세계관 규칙대로 **전화**를 사용합니다.

직원끼리 이야기:
- 휴게실 3D 공간에서 직접 대화

관리자가 응답해야 하는 순간:
- 휴게실 공용 전화 또는 기존 전화 연출을 사용
- 새 무전/영상통신 시스템을 만들지 않음
- 2D 스탠딩은 어디까지나 게임 연출

스토리 선택지는 "관리자가 전화로 답하는 문장"으로 취급합니다.

---

# 5. 그룹 대화용 런타임 형식 변경

기존 `@beat` 형식은 유지하되 그룹 대화를 지원합니다.

## 5.1 그룹 비트

```text
@beat <id>
day: <1~5 | any>
when: <조건>
kind: core | pool | react
group: true
min: <최소 생존/근무 가능 화자 수>

say?: <직원id> | <표정> | <L|R> | <대사>
```

### `say?`

`?`가 붙은 대사는 해당 직원이:

- 사망
- 기절
- 격리

등으로 스토리에 참여할 수 없으면 **그 줄만 건너뜁니다.**

그룹 비트 전체를 삭제하지 않습니다.

단 남은 참여자가 `min`보다 적으면 비트 전체 생략.

중요:
각 `say?` 대사는 특정 직전 한 줄이 없어져도 최대한 자연스럽게 이어지게 작성합니다.

### 배역(`@`) — 그날 실제로 그 일을 겪은 사람

`<직원id>` 자리에 직원 이름 대신 **배역**을 적을 수 있습니다. 재생할 때 그날의
실제 기록을 읽어 사람으로 바뀝니다(`StoryRoleCast`).

| 배역 | 누구인가 |
| --- | --- |
| `@witness` | 어제 이상 개체를 **직접 본** 사람(그 작업실에 서 있던 사람) |
| `@heard1` … `@heard5` | 보지 못한 사람. 1부터 차례로 서로 다른 사람 |

직원 id 를 박아 두면 그 방에 없던 사람이 "저도 봤어요"라고 말합니다. 목격담은
반드시 배역으로 적습니다.

맡을 사람이 없으면 `say?`는 그 줄만 빠지고, `say`(필수)는 비트 전체가 생략됩니다 —
본 사람이 아무도 없는 날에는 목격담 비트가 아예 뜨지 않습니다.

---

# 6. 플레이어 선택지 문법

스토리 중 선택지를 지원합니다.

```text
@choice <choice_id>
prompt: <선택지 위에 표시할 짧은 문장>
option: <플레이어 대사> | <branch_id>
option: <플레이어 대사> | <branch_id>
option: <플레이어 대사> | <branch_id>

@branch <branch_id>
say?: <직원id> | <표정> | <L|R> | <반응>
say?: ...
@endbranch
```

규칙:

- 기본 3개 동시 표시
- 선택지는 관리자 발화
- 선택 즉시 해당 branch의 직원 반응 재생
- gameplay 수치 변화 없음
- 범인 판정 변화 없음
- 직원 관계도 수치 변화도 이번 구현에서는 없음
- 선택 기록은 `StoryChoiceHistory` 정도로 저장 가능하지만 필수 gameplay source of truth로 사용하지 않음

---

# 7. when 조건

기존 조건 유지:

- `always`
- `ghost_seen`
- `death_yesterday`
- `faint_yesterday`
- `isolated_yesterday`
- `core_behind`
- `core_ok`
- `record_bad`
- `record_good`

가능하면 기존 시스템 값만 사용합니다.

새로운 조건이 정말 필요하면 기존 데이터에서 읽을 수 있는 값만 추가합니다.

---

# 8. 말투

## 토끼
해요체 / 반응 큼 / 말 빠름 / 질문과 감탄 많음.

## 고양이
해요체 / 짧고 퉁명 / 감정 과장 없음 / 걱정을 핀잔으로 숨김.

## 여우
해요체 / 여유 / 능글 / 상황을 가볍게 흘리는 것 같지만 관찰 중.

## 양
해요체 / 소심 / 불안 / 가끔 더듬음 / 매 문장 더듬지 않음.

## 늑대
합쇼체 / 짧고 핵심 / 침착 / 허세 없음.

## 강아지
해요체 / 온순 / 다정 / 다른 직원 상태를 챙김.

---

# 9. DAY별 감정 아크

| DAY | 중심 감정 |
|---|---|
| DAY1 | 재난 직후 충격 / 패닉 / 상실 → 새 관리자에게 기대 또는 불신 → 서로 소개 |
| DAY2 | 첫 실제 근무 후 불안 / 이상현상 / 아직 서로를 완전히 믿지는 못함 |
| DAY3 | 스트레스 / 피로 / 신경이 날카로워짐 / 서로 챙기면서도 충돌 |
| DAY4 | 방해자가 내부에 있다는 공포 / 의심 / 관리자에 대한 평가 |
| DAY5 | 마지막 근무 직전 / 체념과 희망 / 살아서 나가고 싶은 마음 |

---

# 10. DAY1 — 재난 직후

## 10.1 집단 패닉

```text
@beat d1_after_disaster
day: 1
when: always
kind: core
group: true
min: 6

say?: rabbit | bad | L | 밖에... 진짜 다 끝난 거예요? 여기 말고 살아 있는 사람은 없는 거예요?
say?: sheep | bad | R | 제가 있던 쪽은... 아까까지 사람들이 있었는데... 지금은 아무 소리도 안 나요.
say?: dog | bad | L | 양 씨, 일단 숨부터 쉬어요. 아직 여기 있는 사람들은 살아 있잖아요.
say?: cat | bad | R | 그런 말 계속해도 달라지는 건 없어요. 숨 돌렸으면 상황부터 정리하죠.
say?: fox | normal | L | 다 죽는 거 아니냐, 살려 달라... 뭐, 틀린 말은 아니네요~ 그래도 아직 여섯이나 남았잖아요.
say?: cat | bad | R | 그 가벼운 말투 좀 그만하면 안 돼요?
say?: fox | smile | L | 제가 같이 울어 드리면 좀 나아지나요~?
say?: wolf | normal | R | 비상 차폐는 오래 버티지 못합니다. 봉쇄 코어를 복구하지 못하면 그 뒤는 없습니다.
say?: rabbit | bad | L | 그럼 우리 이제 어떡해요? 총괄 관리자도 죽었다면서요. 누가 우리한테 뭘 하라고 해요?
```

---

## 10.2 첫 관리자 선택

휴게실의 공용 전화가 연결되고,
토끼가 관리자에게 직접 묻습니다.

```text
@choice d1_leadership
prompt: 직원들이 당신의 대답을 기다리고 있습니다.
option: 제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다. | d1_lead_reassure
option: 우선 살아남는 것부터 생각합시다. 제가 상황을 정리하겠습니다. | d1_lead_protect
option: 봉쇄 코어 복구가 우선입니다. 지금부터 제 지시에 따라 움직여 주세요. | d1_lead_strict

@branch d1_lead_reassure
say?: rabbit | bad | L | ...진짜죠? 그럼 저 믿을게요.
say?: dog | normal | R | 네. 부탁드릴게요. 저희도 할 수 있는 건 다 할게요.
say?: wolf | normal | L | 명령을 기다리겠습니다.
say?: cat | normal | R | 말만 그렇게 끝나지 않았으면 좋겠네요.
say?: fox | smile | L | 새 관리자님이시네요~ 잘 부탁드려요.
say?: sheep | bad | R | 저도... 할게요. 시키는 건 어떻게든...
@endbranch

@branch d1_lead_protect
say?: dog | smile | L | ...그 말 들으니까 조금 안심되네요.
say?: rabbit | bad | R | 살아남을 수 있는 거죠? 진짜로요?
say?: cat | normal | L | 안심시키는 건 나중에 해요. 일단 계획부터 보여 주세요.
say?: wolf | normal | R | 생존과 복구는 결국 같은 문제입니다. 따르겠습니다.
say?: fox | normal | L | 살려만 주신다면야, 저야 뭐든 하죠~
say?: sheep | bad | R | ...부탁드릴게요.
@endbranch

@branch d1_lead_strict
say?: wolf | normal | L | 알겠습니다.
say?: cat | normal | R | 적어도 우왕좌왕하는 것보단 낫겠네요.
say?: rabbit | bad | L | 네... 네. 할게요.
say?: dog | normal | R | 너무 무리하는 사람만 없게 봐 주세요.
say?: fox | smile | L | 벌써 무서운 상사 느낌인데요~ 뭐, 선택지가 있는 상황도 아니지만.
say?: sheep | bad | R | 저... 뒤처지지 않게 할게요.
@endbranch
```

---

## 10.3 여섯 명 자기소개

패닉이 조금 가라앉은 뒤,
이제서야 서로 누군지 확인합니다.

```text
@beat d1_introductions
day: 1
when: always
kind: core
group: true
min: 6

say?: rabbit | normal | L | 그러고 보니까... 우리 서로 이름도 제대로 모르네요. 저부터 할게요. 토끼예요.
say?: rabbit | bad | L | 원래 이 시설 설비 지원 쪽에서 일했어요. 사고 나고 정신없이 뛰다가 여기까지 왔고요.
say?: cat | normal | R | 고양이예요. 코어 정비 쪽이었어요.
say?: cat | normal | R | 사고 직전까지도 코어 근처에 있었습니다. 제가 말할 건 그 정도예요.
say?: sheep | bad | L | 야, 양이에요... 환경 감시 쪽에서 일했어요.
say?: sheep | bad | L | 같이 있던 사람들은... 저 말고 여기까지 온 사람은 못 봤어요.
say?: dog | normal | R | 강아지예요. 시설 지원 쪽에서 일했어요.
say?: dog | bad | R | 사고 뒤에는 다친 사람들 챙기면서 이동했어요. 여기서도 제가 도울 수 있는 건 도울게요.
say?: wolf | normal | L | 늑대입니다. 격리구역 경비 담당이었습니다.
say?: wolf | normal | L | 사고 당시 격리구역이 뚫렸습니다. 이후 살아남은 사람들을 따라 이쪽으로 이동했습니다.
say?: fox | smile | R | 저는 여우예요~ 기록이랑 지원 업무를 맡고 있었고요.
say?: fox | normal | R | 저도 뭐... 운이 좋아서 살아남은 거죠. 자세한 얘기는 나중에 해도 되잖아요?
say?: cat | normal | L | 소개 끝났으면 움직이죠. 가만히 앉아 있다고 코어가 고쳐지진 않으니까.
```

---

# 11. DAY2 — 첫 실제 근무 이후

## 11.1 전날 Ghost를 봤을 경우 그룹 반응

```text
@beat d2_ghost_group
day: 2
when: ghost_seen
kind: react
group: true
min: 2

say: @witness | bad | L | 어제 그거... 저 혼자 본 거 아니죠? 사람처럼 생겼는데, 사람은 아니었어요.
say?: @heard1 | bad | R | 저는 소리만 들었어요. 그쪽 방에서 뭔가... 사람 소리 같은 게 났거든요.
say?: @heard2 | bad | R | 저도 들었어요. 그게 무슨 소리였는지는 아직도 모르겠어요.
say?: @witness | bad | L | 소리만 들은 게 나아요. 저는 눈앞에 그게 서 있었어요.
say?: @heard3 | normal | R | 본 사람이 있으니까 이제 없는 척할 수도 없겠네요.
say?: @heard4 | normal | R | 오늘은 아무도 혼자 두지 말아요. 이상하면 바로 말하고요.
```

---

## 11.2 DAY2 메인 그룹 대화

```text
@beat d2_main_group
day: 2
when: always
kind: core
group: true
min: 2

say?: cat | bad | L | 어제 복구율 봤죠? 생각보다 진도가 안 나왔어요.
say?: rabbit | bad | R | 아침부터 꼭 그 얘기 해야 돼요?
say?: cat | normal | L | 해야죠. 시간이 늘어나는 것도 아닌데.
say?: dog | normal | R | 그래도 어제보다는 오늘 서로 어떻게 움직이는지 알잖아요. 조금은 나을 거예요.
say?: sheep | bad | L | 저는... 이름 아는 사람이 다치는 게 더 무서워졌어요.
say?: fox | smile | R | 하루 만에 벌써 정들었어요~?
say?: sheep | bad | L | 그런 뜻이 아니라...
say?: wolf | normal | R | 둘 다 맞습니다. 익숙해진 만큼 잃을 것도 늘었습니다.
say?: rabbit | bad | L | 그런 말까지 들으니까 더 긴장되잖아요...
```

### DAY2 관리자 선택

```text
@choice d2_policy
prompt: 오늘 근무를 앞두고 직원들이 관리자의 방침을 묻습니다.
option: 이상한 것이 보이면 바로 보고하세요. 제가 CCTV와 기록을 확인하겠습니다. | d2_policy_watch
option: 위험하다고 느끼면 작업을 멈추고 기다리세요. 사람을 잃는 것보다 낫습니다. | d2_policy_safe
option: 오늘도 코어 복구가 우선입니다. 각자 맡은 일을 끝까지 유지해 주세요. | d2_policy_work

@branch d2_policy_watch
say?: rabbit | normal | L | 네! 이번엔 이상하면 바로 말할게요.
say?: sheep | normal | R | 저도... 괜히 참지 않을게요.
say?: cat | normal | L | 기록을 제대로 보고 있다면 그게 제일 낫겠네요.
say?: fox | smile | R | 그럼 관리자님 눈을 피하는 쪽이 더 어려워지겠네요~
say?: wolf | normal | L | 확인했습니다.
@endbranch

@branch d2_policy_safe
say?: dog | smile | L | 그게 좋아요. 무리하다 쓰러지는 것보다 훨씬 나아요.
say?: cat | normal | R | 그러다 작업이 계속 밀리면 그것도 문제예요.
say?: rabbit | normal | L | 그래도... 어제보단 마음이 좀 놓이네요.
say?: wolf | normal | R | 판단이 필요한 상황은 제가 보고하겠습니다.
say?: fox | normal | L | 목숨값이 코어보다 비싸다, 그런 뜻이네요~
@endbranch

@branch d2_policy_work
say?: cat | normal | L | 알겠어요. 그럼 사람 배치만 제때 바꿔 주세요.
say?: wolf | normal | R | 수행하겠습니다.
say?: dog | bad | L | ...그래도 상태 안 좋은 사람은 꼭 봐 주세요.
say?: rabbit | normal | R | 네. 저도 최대한 할게요.
say?: fox | smile | L | 역시 오늘도 열심히 일하는 날이네요~
@endbranch
```

---

# 12. DAY3 — 스트레스와 균열

## 12.1 DAY3 그룹 대화

```text
@beat d3_main_group
day: 3
when: always
kind: core
group: true
min: 2

say?: sheep | bad | L | 저... 오늘도 들어가야 하죠? 손이 아직도 조금 떨려요.
say?: dog | bad | R | 어제부터 계속 그랬잖아요. 너무 심하면 꼭 말해요.
say?: cat | bad | L | 다들 힘든 건 알아요. 그런데 지금 한 명 빠지면 다른 사람이 그만큼 더 해야 해요.
say?: rabbit | bad | R | 그걸 꼭 그렇게 말해야 돼요?
say?: cat | bad | L | 그럼 뭐라고 해요. 괜찮다고만 하면 진짜 괜찮아져요?
say?: wolf | normal | R | 싸울 힘이 있다면 아껴 두십시오. 근무가 시작되면 필요합니다.
say?: fox | normal | L | 늑대 씨 말이 제일 무서운데요~ 맞는 말이라 더 그렇고.
say?: dog | normal | R | 오늘은 서로 상태라도 좀 봐 줘요. 자기 상태는 자기가 제일 늦게 알아차리니까.
say?: rabbit | normal | L | ...그건 진짜 그래요. 저도 제가 이 정도로 지친 줄 몰랐어요.
```

### DAY3 관리자 선택

```text
@choice d3_priority
prompt: 지친 직원들이 오늘의 우선순위를 기다립니다.
option: 상태가 나쁜 직원은 바로 의무실로 보내겠습니다. 무리하지 마세요. | d3_priority_health
option: 코어 복구를 멈출 수는 없습니다. 가능한 사람부터 최대한 움직여 주세요. | d3_priority_core
option: 서로 상태를 확인하면서 버팁시다. 위험해지면 제가 바로 재배치하겠습니다. | d3_priority_balance

@branch d3_priority_health
say?: dog | smile | L | 네. 그게 제일 좋을 것 같아요.
say?: sheep | normal | R | ...그러면 저도 조금은 말할 수 있을 것 같아요.
say?: cat | normal | L | 대신 의무실까지 망가지게 두진 마세요.
say?: wolf | normal | R | 합리적입니다.
say?: fox | smile | L | 오늘은 쓰러지기 전에 누울 수 있겠네요~
@endbranch

@branch d3_priority_core
say?: cat | normal | L | 알겠어요. 적어도 방향은 확실하네요.
say?: wolf | normal | R | 수행하겠습니다.
say?: rabbit | bad | L | ...다들 진짜 쓰러지지만 않았으면 좋겠어요.
say?: dog | bad | R | 제가 옆에 있는 사람들은 최대한 볼게요.
say?: fox | normal | L | 열심히 살아남으면서 열심히 일해야겠네요~
@endbranch

@branch d3_priority_balance
say?: dog | smile | L | 네. 서로 보는 건 제가 도울게요.
say?: rabbit | normal | R | 저도 말해 줄게요. 얼굴 이상하면 바로요.
say?: cat | normal | L | 괜히 호들갑만 안 떨면 됩니다.
say?: sheep | normal | R | 저... 저도 볼게요.
say?: wolf | normal | L | 이상 징후가 있으면 즉시 보고하겠습니다.
@endbranch
```

---

# 13. DAY4 — 의심

## 13.1 관리자 성적에 따른 그룹 반응

### record_bad

```text
@beat d4_blame_group
day: 4
when: record_bad
kind: core
group: true
min: 2

say?: cat | bad | L | 솔직히 말할게요. 지금 배치가 계속 이렇게 꼬이면 누가 먼저 쓰러질지만 달라져요.
say?: rabbit | bad | R | 고양이 씨, 그렇게까지 말할 건...
say?: cat | bad | L | 위에서 숫자만 보면 안 보여요. 여기선 사람이 직접 내려가야 하니까.
say?: dog | bad | R | 관리자님도 일부러 그런 건 아니겠지만... 오늘은 조금 더 봐 주셨으면 좋겠어요.
say?: wolf | normal | L | 불만은 이해합니다. 그래도 지휘 체계가 무너지면 더 위험합니다.
say?: fox | normal | R | 벌써 관리자님 평가회가 됐네요~ 없는 자리에서 하는 게 제일 솔직하긴 하죠.
say?: sheep | bad | L | 저... 오늘은 정말 아무도 안 쓰러졌으면 좋겠어요.
```

### record_good

```text
@beat d4_trust_group
day: 4
when: record_good
kind: core
group: true
min: 2

say?: dog | smile | L | 그래도 지금까지 버틴 건 관리자님이 계속 봐 주신 덕분이라고 생각해요.
say?: cat | normal | R | 아직 끝난 건 아니에요. 칭찬은 나가서 하죠.
say?: rabbit | smile | L | 그래도 잘하고 있다고 한마디 정도는 해도 되잖아요!
say?: wolf | normal | R | 동의합니다. 지금까지의 판단에는 큰 문제가 없었습니다.
say?: fox | smile | L | 늑대 씨한테 저 정도 평가면 거의 최고점 아닌가요~?
say?: sheep | normal | R | 저도... 조금은 믿어도 될 것 같아요.
```

---

## 13.2 DAY4 의심 그룹 대화

```text
@beat d4_suspicion_group
day: 4
when: always
kind: core
group: true
min: 2

say?: rabbit | bad | L | 저 요즘 누가 같은 방에 들어오면 먼저 얼굴부터 보게 돼요.
say?: sheep | bad | R | 저도요... 근데 혼자 있으면 그게 더 무서워요.
say?: cat | bad | L | 결국 우리 중 하나라는 거잖아요. 계속 모른 척할 수도 없고.
say?: dog | bad | R | 그렇다고 아무나 의심하면... 진짜 사람까지 서로 못 믿게 돼요.
say?: fox | normal | L | 이미 그렇게 된 것 같은데요~ 다들 저 한 번씩은 쳐다봤잖아요.
say?: rabbit | normal | R | 여우 씨는 평소에도 좀 수상하게 말하잖아요.
say?: fox | smile | L | 억울하네요~ 제 평소 행실이 이렇게 돌아오다니.
say?: wolf | normal | R | 확실하지 않은 추측으로 움직이면 안 됩니다. 기록과 사실만 봐야 합니다.
say?: cat | normal | L | ...그건 맞아요. 느낌으로 사람 묶었다가 틀리면 끝이니까.
```

### DAY4 관리자 선택

```text
@choice d4_suspicion_policy
prompt: 서로를 의심하기 시작한 직원들에게 방침을 전달합니다.
option: 확실한 증거 없이 서로를 몰아가지 마십시오. 기록과 진술을 제가 확인하겠습니다. | d4_policy_evidence
option: 수상한 행동을 보면 전부 보고해 주세요. 사소한 것도 제가 판단하겠습니다. | d4_policy_report
option: 필요하면 격리를 지시하겠습니다. 서로의 행동을 더 주의해서 보세요. | d4_policy_isolate

@branch d4_policy_evidence
say?: wolf | normal | L | 동의합니다. 증거 없는 지목은 위험합니다.
say?: dog | normal | R | 저도 그게 좋아요. 괜히 서로 상처 주고 싶진 않아요.
say?: cat | normal | L | 기록을 놓치지만 마세요.
say?: fox | smile | R | 그럼 저는 평소처럼 수상하게 있어도 되겠네요~
say?: rabbit | normal | L | 그건 좀 줄여 주세요...
@endbranch

@branch d4_policy_report
say?: rabbit | normal | L | 네. 이상한 거 보면 바로 말할게요.
say?: sheep | bad | R | 저도... 봤는데 확실하지 않은 것도 괜찮아요?
say?: cat | normal | L | 판단은 관리자님이 한다잖아요. 일단 말해요.
say?: fox | normal | R | 신고가 넘쳐나겠네요~
say?: wolf | normal | L | 사실과 추측을 구분해서 보고하겠습니다.
@endbranch

@branch d4_policy_isolate
say?: cat | normal | L | 필요하면 해야죠. 다만 틀리면 인원 한 명을 그냥 버리는 거예요.
say?: dog | bad | R | 격리하기 전에 한 번만 더 확인해 주세요.
say?: wolf | normal | L | 격리는 최후 수단으로 두는 것이 좋습니다.
say?: fox | smile | R | 그 방 침대는 별로 편해 보이지 않던데요~
say?: rabbit | bad | L | 그런 농담 지금 하지 마요...
@endbranch
```

---

# 14. DAY5 — 마지막 근무 전

```text
@beat d5_main_group
day: 5
when: always
kind: core
group: true
min: 2

say?: cat | normal | L | 오늘이 마지막이에요. 차폐가 버티는 것도 오늘까지고.
say?: rabbit | bad | R | 진짜 마지막이라고 하니까 오히려 더 무서운데요.
say?: dog | normal | L | 다들 무사히 끝내고 나가면... 한 번 같이 밥이라도 먹어요.
say?: fox | smile | R | 밖에 식당이 남아 있으면요~
say?: dog | bad | L | 그런 말 좀 하지 마요.
say?: fox | normal | R | 미안해요. 저도 그냥... 끝나고 할 일을 하나쯤 정해 두고 싶어서요.
say?: sheep | normal | L | 저는... 밖에 나가면 제일 먼저 하늘부터 보고 싶어요.
say?: cat | normal | R | 하늘이 어떤 상태인지도 모르잖아요.
say?: sheep | bad | L | 알아요. 그래도요.
say?: wolf | normal | R | 나가서 확인하면 됩니다. 그 전에는 오늘 근무에 집중하십시오.
say?: rabbit | smile | L | 그럼 늑대 씨도 밥 먹으러 오는 거예요?
say?: wolf | normal | R | 모두 무사히 나간다면 가겠습니다.
say?: fox | smile | L | 약속 들었어요. 이제 못 빠지시겠네요~
```

### DAY5 관리자 선택

```text
@choice d5_final_order
prompt: 마지막 근무를 앞둔 직원들에게 마지막 지시를 내립니다.
option: 오늘 봉쇄 코어를 복구하고, 모두 살아서 나갑시다. | d5_final_together
option: 오늘은 봉쇄 코어 복구만 봅니다. 마지막까지 집중해 주세요. | d5_final_mission
option: 끝까지 서로를 믿되, 이상한 행동은 놓치지 마십시오. | d5_final_watch

@branch d5_final_together
say?: rabbit | smile | L | 네! 다 같이 나가요. 진짜로요.
say?: dog | smile | R | ...좋아요. 그 말 믿을게요.
say?: sheep | normal | L | 저도... 끝까지 해볼게요.
say?: cat | normal | R | 그럼 한 명도 놓치지 마세요, 관리자님.
say?: wolf | normal | L | 마지막까지 따르겠습니다.
say?: fox | smile | R | 전원 생환이라~ 꽤 마음에 드는 목표네요.
@endbranch

@branch d5_final_mission
say?: cat | normal | L | 알겠어요. 오늘 끝냅시다.
say?: wolf | normal | R | 임무를 완료하겠습니다.
say?: rabbit | bad | L | 네... 끝내야죠. 오늘 아니면 안 되니까.
say?: dog | normal | R | 다들 무리할 것 같으면 제가 먼저 말할게요.
say?: fox | normal | L | 마지막 날까지 야근 분위기네요~
say?: sheep | bad | R | ...할게요.
@endbranch

@branch d5_final_watch
say?: wolf | normal | L | 끝까지 경계를 유지하겠습니다.
say?: cat | normal | R | 맞아요. 마지막이라고 방심하면 제일 바보 같은 거니까.
say?: rabbit | bad | L | 그래도 이제 서로 좀 믿고 싶었는데...
say?: dog | normal | R | 믿는 거랑 확인하는 건 다른 거니까요.
say?: fox | smile | L | 마지막까지 서로 감시라. 우리답네요~
say?: sheep | normal | R | 그래도... 같이 나갈 수 있으면 좋겠어요.
@endbranch
```

---

# 15. 동적 반응 비트도 그룹 대화로

## 사망 발생

```text
@beat r_death_group
day: any
when: death_yesterday
kind: react
group: true
min: 2

say?: rabbit | bad | L | 그분이 진짜... 돌아가신 거예요? 아직도 잘 모르겠어요.
say?: dog | bad | R | ...네. 아무리 생각해도 괜찮지가 않네요.
say?: cat | bad | L | 한 명 줄었어요. 일은 그대로고요.
say?: sheep | bad | R | 그런 식으로 말하지 마세요...
say?: cat | bad | L | 알아요. 저도 이런 말 하고 싶은 게 아니에요.
say?: wolf | normal | R | 오늘은 어제보다 더 조심해야 합니다.
say?: fox | normal | L | ...오늘은 농담할 말도 없네요.
```

## 기절 발생

```text
@beat r_faint_group
day: any
when: faint_yesterday
kind: react
group: true
min: 2

say?: dog | bad | L | 어제 쓰러진 사람, 깨어났어도 괜찮아 보이진 않았어요.
say?: rabbit | bad | R | 저도 봤어요. 말은 괜찮다고 하는데 얼굴이 아니던데요.
say?: cat | normal | L | 그러니까 버티는 걸 잘하는 거랑 상태가 좋은 건 다른 거예요.
say?: sheep | bad | R | 저... 저도 너무 심해지기 전에 말할게요.
say?: wolf | normal | L | 그렇게 하십시오.
```

## 격리 발생

```text
@beat r_isolated_group
day: any
when: isolated_yesterday
kind: react
group: true
min: 2

say?: cat | normal | L | 격리실에 사람이 들어가니까 분위기가 확 달라지네요.
say?: fox | normal | R | 맞는 사람이었으면 좋겠네요~ 아니면 그냥 일손 하나 줄인 거니까.
say?: dog | bad | L | 그런 식으로 말하면 더 불안해져요.
say?: wolf | normal | R | 격리했다고 끝난 것이 아닙니다. 계속 확인해야 합니다.
say?: rabbit | bad | L | ...틀린 사람이 아니었으면 좋겠어요.
```

## Ghost 발생

```text
@beat r_ghost_group
day: any
when: ghost_seen
kind: react
group: true
min: 2

say: @witness | bad | L | 어제 그거... 저 혼자 본 거 아니죠? 진짜 거기 있었어요.
say?: @heard1 | bad | R | 저는 소리만 들었어요. 그 방에서 뭔가 났는데, 가 보진 못했고요.
say?: @witness | bad | L | 다시 보고 싶진 않아요. 그런데 눈앞에 있던 건 확실해요.
say?: @heard2 | normal | R | 본 사람이 있으니까 이제 없는 척은 못 하겠네요.
say?: @heard3 | bad | R | 오늘 또 나오면 혼자 버티지 말고 바로 말해요.
say?: @heard4 | normal | R | 관리자님이 계속 봐 주시면 사라진다는 건 알았잖아요. 당황하지 말아요.
```

---

# 16. 재생 순서

DAY 시작 시:

1. 조건에 맞는 `react` 그룹 비트 최대 1개
2. 해당 DAY `core` 그룹 비트
3. 해당 DAY `@choice`
4. 필요 시 추가 `core`
5. 이후 게임 시작

DAY1은 고정:

1. `d1_after_disaster`
2. `d1_leadership`
3. `d1_introductions`
4. DAY1 근무 시작

DAY2~5는 기본:

1. 전날 사건 반응 0~1개
2. 메인 그룹 대화
3. 관리자 선택지
4. 근무 시작

스토리 길이가 너무 길어지지 않도록
DAY2~5에서 기존 pair `pool` 비트 2~3개를 추가로 랜덤 재생하지 않습니다.

---

# 17. 결원 처리

DAY1은 시작 시 6명 모두 보이는 상태이므로 `min: 6`.

DAY2~5는 사망/기절/격리를 고려하여:

`min: 2`

기본.

`group: true` 비트의 `say?`는 참여 불가능한 직원만 줄 단위로 생략.

중요:

- 죽은 직원이 말하지 않음
- 격리 중인 직원이 휴게실에 앉아 말하지 않음
- 기절 중인 직원이 말하지 않음
- 나머지 직원들의 대화는 계속 자연스럽게 이어짐

---

# 18. 플레이어 선택지 UI

선택지:

- 한 번에 3개
- 화면에서 동시에 보임
- 마우스 클릭
- 키보드 1/2/3 선택 가능하면 좋음

선택 중:

- 스토리 시간 정지 유지
- 직원 스탠딩은 마지막 화자 상태로 유지
- mouth closed

선택 후:

- 선택한 관리자 문장을 짧게 표시
- branch 반응 실행
- 이후 스토리 계속

---

# 19. 선택지와 전화 연출

선택지는 직원들이 관리자에게 직접 질문하는 순간입니다.

따라서 세계관상:

**휴게실 공용 전화 ↔ 관리자 전화기**

로 연결된 것으로 처리합니다.

새 COMM / 영상통화 시스템을 추가하지 마세요.

가능하면 휴게실 긴 테이블 중앙 또는 가까운 위치에
작은 공용 전화기를 배치하고,
관리자 선택지가 등장하는 장면에서 해당 전화가 사용 중임을 연출합니다.

---

# 20. 구현 변경

기존 StoryScript / StoryBeatSelector를 확장합니다.

필요:

- `group: true`
- `min:`
- `say?`
- `@choice`
- `option`
- `@branch`
- `@endbranch`

기존 `say:` / `need:` 형식은
다른 문서/과거 데이터 호환을 위해 가능하면 유지.

새 Story engine을 처음부터 만들지 않습니다.

---

# 21. Story Choice 데이터

최소 구조 예:

```csharp
StoryChoice
{
    string Id;
    string Prompt;
    List<StoryChoiceOption> Options;
}

StoryChoiceOption
{
    string Text;
    string BranchId;
}
```

Branch는 기존 `StoryLine` 리스트 재사용.

---

# 22. 선택 결과의 범위

이번 단계에서 선택지는:

- 즉각적인 직원 반응
- 연출
- `StoryChoiceHistory` 기록

만 담당.

금지:

- 코어 진행률 증가/감소
- 스트레스 직접 변경
- 직원 관계도 직접 변경
- 방해자 확률 변경
- 엔딩 판정 변경

추후 필요하면 별도 설계.

---

# 23. 기존 3D 휴게실 연출과 연결

그룹 스토리가 재생되는 동안:

- 휴게실 3D 배경 유지
- 실제 직원 모델은 각자 좌석에 앉아 있음
- 현재 화자 쪽을 다른 직원들이 약하게 바라봄
- 2D 스탠딩은 현재 화자 강조용
- 화자가 바뀌면 2D 컷인도 교체
- 직원 6명이 같은 공간에 있다는 인상 유지

pair VN 화면처럼 두 명만 따로 고립시키지 마세요.

---

# 24. 검수

반드시 확인:

1. DAY1이 평화로운 자기소개로 바로 시작하지 않음
2. 재난 직후 공포와 상실이 먼저 느껴짐
3. DAY1 직원 누구도 "재난 이후 새로 여기 배정받았다"고 말하지 않음
4. 첫 관리자 선택지에 다음 문장이 존재:
   `제가 당신들을 관리하여 봉쇄 코어를 복구하겠습니다.`
5. DAY1 선택 후 여섯 직원이 각자 성격대로 반응
6. 그 뒤 6명이 자기소개
7. DAY2~5 메인 장면에서 가능한 경우 4~6명이 같은 대화에 참여
8. 2인 대화 여러 개를 이어 붙인 느낌이 나지 않음
9. DAY1~5 각각 관리자 선택지가 최소 1회
10. 각 선택 UI에 기본 3개 옵션 동시 표시
11. 결원 발생 시 그 직원 줄만 자연스럽게 생략
12. 죽은/격리/기절 직원이 스토리에 등장하지 않음
13. 선택지 때문에 gameplay 수치가 변하지 않음
14. 결번이 누구인지 암시하는 고정 스토리 분기 없음
15. 전화기의 세계관 역할 유지
16. `dotnet build` 성공
17. export build에서도 MD 파싱/선택지 정상

---

# 25. 가장 중요한 완료 기준

DAY 시작 장면은 더 이상:

> A와 B가 네 줄 대화
> C와 D가 네 줄 대화
> E와 F가 네 줄 대화

가 아닙니다.

플레이어가 보기에:

> **한 테이블에 살아남은 사람들이 모여 있고,
> 한 사람이 말을 꺼내면 다른 사람들이 자연스럽게 끼어들면서
> 하나의 공통된 대화를 나누는 장면**

이어야 합니다.

그리고 DAY1은:

> 자기소개 모임

이 아니라:

> **대재난 직후 살아남은 사람들이 처음으로 현실을 받아들이는 장면**

이어야 합니다.
