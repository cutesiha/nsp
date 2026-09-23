# 대사 뱅크 슬롯 사양 (Local Dialogue V2)

`data/dialogue/lines/*.txt` 를 고칠 때 보는 문서입니다. 게임은 이 `.md` 를 읽지 않습니다.

## 파일 형식

```
@char wolf            ← 이후 슬롯의 주인 (직원 id / fml=격식체 공통 / sft=해요체 공통 / any)
## selfloc            ← 슬롯
- {room}입니다.        ← 후보 문장. 하나가 무작위로 뽑힌다(최근에 쓴 문장은 피함)
```

- 캐릭터 파일에 슬롯이 없으면 `common.txt` 의 `fml`(늑대) / `sft`(나머지) 문장을 씁니다.
- **변수 뒤 조사는 반드시 슬래시로**: `{room}은/는` `{room}이/가` `{room}을/를` `{room}으로/로` `{room}이에요/예요` `{room}이요/요` `{room}이었/였어요` `{trait}이죠/죠` `{who}이랑/랑`
  - `{room}에` `{room}에서` `{room}입니다` `{room} 쪽` 은 받침과 무관하므로 그대로 씁니다.
  - 틀리게 쓰면 게임 실행 시 경고가 뜨고 `CharacterRosterTest` 의 검사가 실패합니다.
- 문장에 쓴 변수가 그 상황에서 비어 있으면 그 문장은 후보에서 빠집니다(빈칸이 새지 않게).
- `{what}` `{sound}` 는 **완성된 한 문장**(예: `설비가 멈췄어요.`)으로 들어갑니다. 문장 끝에 두세요.

## 쓰는 원칙

1. **사실을 만들지 않는다.** 문장은 슬롯이 보장하는 사실만 말합니다. "다들 무사해요", "누가 했어요" 같은 새 사실 금지.
2. **캐릭터 차이는 문장부호가 아니라 의미 구조로.** 무엇을 먼저 말하나 · 얼마나 확신하나 · 남을 언급하나 · 행동을 제안하나 · 추측과 사실을 나누나.
3. 한 문장 틀 = 사람이 통째로 한 번에 할 말. 1~2문장.
4. 결번자도 같은 슬롯을 씁니다. `*.evasive` `support.justify/minimize/vague` `deny.evidence` `challenge.evasive` `Confront.*` 는 결번자가 주로 쓰는 자리지만, 정상 직원이 쓸 때도 어색하지 않아야 합니다(범인 표시가 되면 안 됨).

## 슬롯 목록

### 인사 · 전화
| 슬롯 | 상황 | 변수 |
|---|---|---|
| greet.interview | 휴게시간 심문 시작 인사 | — |
| greet.call | 근무 중 관리자가 먼저 건 전화의 첫 마디 | — |
| call.prefix | 직원이 수상한 행동을 목격해 먼저 전화할 때 첫 마디(뒤에 목격 문장이 붙음) | — |

### 기본 질문
| 슬롯 | 상황 | 변수 |
|---|---|---|
| selfloc | "그때 어디 있었나" — 방을 댄다 | room |
| incident.direct | 사고를 그 방에서 직접 봤다 | iroom, what |
| incident.indirect | 옆방이라 소리·진동만 알았다. **원인을 말하면 안 됨** | iroom, sound |
| noanomaly | 아는 이상 없음 | — |
| sight | 실제로 목격한 다른 직원의 수상한 모습(그 자리에 있었다는 것까지) | who, sroom |
| nosight | 수상한 사람 못 봄. **누구도 지목하지 않는다** | — |
| opinion | 동료 평가. trait = "무난한 편"/"겁이 많은 편" 같은 '…편' 명사구 | target, trait |
| deny | 의심받음(불리한 기록 없음) | — |
| deny.evidence | 의심받음(불리한 기록 있음). 자백 금지 | — |

### 근무 상태
| 슬롯 | 상황 | 변수 |
|---|---|---|
| status.quiet / status.busy / status.hard | 휴게시간 "오늘 근무 어땠나"(과거형). busy = 오늘 겪은 사고가 있음 | busy: iroom |
| status.ok / blocked / repair / stress / idle / moving | 근무 중 통화 "작업은 잘 되나"(현재형) | — |
| comply | "작업에 더 집중하라" 에 대한 대답 | — |

### 수신 전화(직원이 먼저 거는 사고 신고)
| 슬롯 | 상황 | 변수 |
|---|---|---|
| report.direct | 사고를 직접 봄 → 신고 + 지시 요청 | iroom, what |
| report.indirect | 소리만 들음 → 신고 + 지시 요청 | iroom, sound |
| report.blackout | 정전 | — |
| accept / decline | "확인하러 가라" / "대기하라" 에 대한 대답 | — |

### 여는 말 · 보정 · 덧붙임
| 슬롯 | 상황 |
|---|---|
| opener.repeat | 같은 질문을 또 받았을 때 여는 말(쉼표로 끝나도 됨) |
| react.accused | 추궁받을 때 짧은 반응 |
| emotion.alarm / emotion.fear / emotion.annoy | 사고 이야기 전 짧은 반응 |
| caveat.indirect | "직접 보지는 못했다" — 간접 인지일 때 반드시 붙음 |
| caveat.cause | "원인은 모른다" |
| support.task / support.taskname(task) / support.nothing / support.hedge | 덧붙이는 한 마디 |
| support.justify / support.minimize / support.vague / support.redirect(who) | 결번자 전략(합리화·축소·흐리기·시선 돌리기) |
| support.seen | 평가 대상을 오늘 봤다 |
| volunteer.noanomaly / volunteer.nosight / volunteer.sight | 묻지 않았지만 덧붙이는 말 |
| closer / closer.back | 끝맺음 / 되묻기 |

### 꼬리질문(구 시스템 — 테스트에서 사용)
prevloc.same(room) · prevloc.moved(droom, room) · nextact.stayed(room) · nextact.moved(droom) · present.alone · present.with(dname) · witness.alone · witness.with(dname) · heard · seen.saw · seen.heard · certain.sure · certain.unsure · detail.direct(iroom, what) · detail.indirect · detail.sight · sightplace(room) · reason.sight · reason.move · reason.opinion · opinionq.today · opinionq.suspicion · challenge.honest · challenge.evasive

### 증거 기반 심문
| 슬롯 | 상황 | 변수 |
|---|---|---|
| MoveReason.ordered | 관리자가 재배치해서 옮김 | to, from |
| MoveReason.dispatched | 전화로 "확인하러 가라" 해서 옮김 | to |
| MoveReason.repair | 수리 업무 때문에 | to |
| MoveReason.task | 업무 때문에 | to, task |
| MoveReason.check / MoveReason.plain | 확인차 / 별 이유 없음(정상 직원) | to |
| MoveReason.evasive | 설명 못 하는 이동(결번자) | — |
| PresenceReason.task / assigned / check / evasive | 그 시각 그 방에 있던 이유 | room, task |
| Companion.with / Companion.alone | 그때 같이 있던 사람 | who, room |
| NextLocation.moved / stayed / evasive | 그 뒤 어디로 | next, room |
| ActionThere.task / repair / check / evasive | 거기서 한 일 | task, room |
| IncidentKnown.direct / indirect / none | 이 사고를 알았나 | room |
| WhereAtIncident.any | 그 시각 어디 있었나 | room, time |
| BeforeIncident.task / moved / plain | 사고 직전 | room, task, prev |
| WhoSeenNear.someone / none | 그 무렵 본 사람 | who |
| RouteAround.full / short / evasive | 앞뒤 동선 | prev, room, next |
| ConfirmTestimony.admit / deny | 다른 직원 증언 확인 | room |
| Restate.same | 진술 재확인 | room |
| MoodReason.incident / calm / plain | 기분 이유 | room |
| MoodBefore.yes / no · MoodRelated.person / incident / none | 기분 꼬리질문 | who, room |
| ExactTime.exact / vague | 정확한 시각 | time |
| Confront.honest / evasive / deny | 자료 두 장으로 모순을 들이밀었을 때 | room, time |
| Confront.neutral | 자료 두 장이 어떤 추궁 규칙에도 걸리지 않았을 때(거절 대신 되묻는다) | room, time |
| Denial.equipment | 결번자가 "설비 근처에 가지 않았다"고 못 박을 때 답변 뒤에 붙는 한 줄 | room, time |

### 근무 기억(ShiftMemory) — 답변 뒤에 덧붙는 한 문장
실제 로그·통화 기록에서 골라 붙습니다. 캐릭터 말투가 몇 개를, 무엇을 먼저 꺼낼지 정합니다.

| 슬롯 | 뜻 | 변수 |
|---|---|---|
| mem.worked | 그 방에서 하던 업무 | task, room |
| mem.worked.repair | 그 방에서 하던 수리 | room |
| mem.with / mem.with.two | 그때 같은 방에 있던 사람 | who, (who2) |
| mem.with.incident | 사고 난 그 방에 같이 있던 사람(그 사람 반응을 말해도 됨 — 추측으로) | who |
| mem.with.day | 오늘 가장 오래 같이 일한 사람 | who |
| mem.alone | 그때 혼자였다 | room |
| mem.relocated | 관리자가 재배치해서 그 방으로 옴 | room, time |
| mem.dispatched | 전화로 확인 지시를 받고 그 방에 감 | iroom |
| mem.call.stay | 전화로 대기 지시를 받음 | iroom |
| mem.call.reported | 그 일로 관리자에게 전화했다 | iroom |
| mem.call.missed | 전화했는데 관리자가 안 받았다 | iroom |
| mem.called | 관리자가 전화했을 때도 그 방에 있었다 | room |
| mem.repair | 그 방 복구가 끝났다 | room |
| mem.heard | 그 무렵 옆방에서 소리가 났다(원인 말하지 않기) | iroom |
| mem.incident.here | 그 방에서 사고가 났을 때 거기 있었다 | room |
| mem.sum.moves / mem.sum.calls | 하루 요약 — n = "두", "세" … | n |
| mem.with.vague | (결번자 흉내 어긋남) 사람 기억이 뭉뚱그려짐 | — |
| slip.assert / slip.concern.vague | (결번자 흉내 어긋남) 지나친 단정 / 구체성 없는 걱정 | — |

### 동료 인상 — 캐릭터 파일에만
| 슬롯 | 뜻 | 변수 |
|---|---|---|
| about.<직원 id> | 답변에 그 동료가 이름으로 나오면 뒤에 붙는 "그 사람을 어떻게 보는가" 한마디(예: 강아지 about.cat "오늘따라 예민해 보이시더라고요. ㅎㅎ") | who |
| about.any | 위 슬롯이 없을 때 | who |

- 붙는 빈도는 `data/dialogue/voices/<id>_voice.tres` 의 `ImpressionChance`. 사람을 물은 질문(같이 있던 사람 등)에서는 그 확률 그대로, 근무 기억 속 동료에는 절반 확률로 붙는다.
- 핵심 문장 틀이 이미 두 문장 이상이면(자기 한마디를 품고 있으면) 붙지 않는다.
- 사건 사실을 새로 만들지 않는다 — 인상 · 감정만. 나중에 인물 관계도가 생기면 이 슬롯을 채우면 된다.

### 말버릇 안전망(`DialogueVoiceTics`)
`<id>_voice.tres` 의 `StutterChance`(첫 낱말 더듬기) · `TrailOffChance`(말끝 흐리기) · `TildeChance`("요." → "요~")는
대사 뱅크 문장이 그 표식을 놓쳤을 때만 한 번 덧댄다. 양 · 여우 문장은 되도록 뱅크에서 직접 표식을 넣는다.
더듬기는 받침을 뺀 음절로 쓴다("바, 발전실" · "자, 잘"). '아, 어, 음, 네' 로 더듬지 않는다.

### 표현 조각(common.txt 의 `@char any`)
`phrase.direct.<사건종류>` / `phrase.sound.<사건종류>` — 과거형 동사 줄기("설비가 멈췄"). 말투에 따라 "어요/습니다" 가 붙습니다.
`echo.wrap`(subject) · `echo.certain/seen/heard/where` · `time.vague` — 되받기와 시간 표현.
