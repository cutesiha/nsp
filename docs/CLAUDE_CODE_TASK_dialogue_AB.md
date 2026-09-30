# 대사 조립기 개편 작업 지시 (A + B)

브랜치: `ver3(simple_version)` · 대상: `scripts/dialogue/`, `data/dialogue/lines/`, `data/dialogue/voices/`

## 배경 (먼저 읽고 시작할 것)

대사는 LLM 없이 `data/dialogue/lines/*.txt` 의 "완성 문장 틀" 을 `DialogueComposer.Build` 가 조각(Opener / Core / Caveat / Memory / Impression / Extra / Back) 단위로 이어 붙여 만든다. 어색한 답의 원인은 **이미 완결된 핵심 틀 뒤에 또 다른 완결된 문장을 붙이는 것**이다. 실제 샘플(`tools/dialogue_samples/fox.md`):

- `제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? 괜히 이름 대면 저만 곤란해지잖아요~` ← nosight(닫힌 틀) + volunteer.nosight
- `코어실에 있었어요~ 관리자님은 그때 어디 계셨어요? 그때는 혼자였어요~` ← 되물은 뒤 다시 진술
- `설마요~ 관리자님, 농담이시죠? 양 씨도 그 자리에 있었어요~ 표정이 볼만했죠.` ← deny 에 동석자 기억이 붙음
- `흐음, 이상현상이요? 이상한 거요~? 없었어요.` ← 되받기 두 번
- `꽤 볼만했죠~ 소리만 들었어요~` ← 못 봤는데 "볼만했다"
- `마침 제 눈앞이었어요. 설비가 멈췄어요. 구경거리였죠~` ← `{what}` 이 `@char any` 공통 문장이라 캐릭터 말투 사이에 밋밋한 문장이 박힘

여우는 `MaxSentences = 2` 인데 샘플 598개 중 215개가 3문장 이상이다. 원인은 `DialogueComposer.Build` 의 상한이 **문장 수가 아니라 조각(parts) 수**이기 때문.

작업 원칙:
- 사실 계층(`DialogueResponsePlanner`, `InterviewReplyPlanner`, `ShiftMemory` 의 사실 선택 로직)은 건드리지 않는다. 바꾸는 것은 "무엇을 붙이느냐 / 몇 문장이냐" 뿐이다.
- 결번자 거짓말 검증(`InterviewScenarioTest`, `DialogueScenarioTest` 의 "거짓말 중 실제 방을 흘리지 않는다" 류)은 전부 그대로 PASS 해야 한다.
- 모든 작업 후 `scenes/debug/DialogueSampleDump.tscn` 을 돌려 `tools/dialogue_samples/*.md` 를 다시 뽑고, 아래 "검수 기준" 을 확인한다.

---

## A. 조립 규칙 수정 (`scripts/dialogue/`)

### A-1. `DialogueComposer.Build` — 상한을 문장 수로

- `int max = budget;` 이후의 잘라내기 루프를 `parts.Count` 가 아니라 **모든 조각의 문장 수 합**(`SentenceCount` 를 각 조각에 적용해 합산) 기준으로 바꾼다.
- `keep = Core + Caveat` 의 문장 수를 하한으로 두는 것은 유지한다(핵심·보정은 절대 안 빠짐).
- 인상(Impression)에 주던 `max++` 예외는 제거한다. 핵심이 사람을 댔을 때 인상을 붙이고 싶으면 핵심 틀이 1문장일 때만 붙게 이미 되어 있으니 그것으로 충분하다.
- `SentenceCount` 는 `~` 뒤 공백도 문장 경계로 보고 있으니 그대로 쓴다.

### A-2. `DialogueComposer.Build` — "닫힌 핵심 틀" 뒤에는 붙이지 않는다

`IsClosed(string core)` 헬퍼를 추가한다. 다음 중 하나면 닫힌 틀:
- `SentenceCount(core) >= 2`
- 마지막 문장이 `?` 로 끝난다 (`~?` 포함)
- 마지막 문장이 `인데요~`, `잖아요~`, `걸요?`, `텐데.` 처럼 이미 "덧붙임 성격" 으로 끝난다 — 이 목록은 상수 배열로 두고 늘릴 수 있게

닫힌 틀이면:
- `Part.Extra`, `Part.Back`, `Part.Memory`, `Part.Impression` 을 **아예 추가하지 않는다** (TryAdd 호출 전에 건너뜀).
- `Part.Caveat` 는 붙이되, 핵심 **앞**에 넣는다 (`parts.Insert(0, …)` — Opener 가 있으면 Opener 다음). 예: `직접 본 건 아니에요~ 벽 너머였죠. 궁금하긴 하더라고요~`
- `LastTrace` 에 `(닫힘)` 표시를 남겨 샘플 덤프에서 확인할 수 있게 한다.

### A-3. `DialogueComposer.Build` — 되받기 중복 판정 확장

`OpensWithQuestion(core)` 는 현재 `?` 가 앞 6글자 안에 있을 때만 참이라 `수상한 사람이라~ 딱히요` 를 놓친다. 다음으로 교체:
- 첫 문장(`Sentences(core).First()`)이 **14글자 이하**이고 `?` 또는 `~` 로 끝나면 이미 되받은 것으로 본다.
- 또는 첫 문장이 `f.Vars` 의 echo 주제어(`이상`, `수상`, `저요`, `지금`, `그때`, `누구`)로 시작하면 되받은 것으로 본다.
이 경우 `OpenerText`(EchoText) 를 버리고, `OpenerSlot` 이 `react.*` / `emotion.*` 이면 그것도 버린다.

### A-4. `DialogueUtterancePlanner.ReactionSlot` — 감정 여는 말을 지식 수준에 맞춘다

- `SituationTone.Alarmed` 일 때 `plan.Knowledge == KnowledgeLevel.Indirect`(또는 `plan.NeedsIndirectCaveat`) 이면 `"emotion.alarm.indirect"` 를, 직접 목격이면 `"emotion.alarm"` 을 돌려준다.
- `emotion.alarm.indirect` 슬롯이 그 캐릭터 파일에 없으면 `DialogueLineBank.Has` 로 확인해 **여는 말을 비운다**(공통으로 떨어뜨리지 않는다).
- 6개 캐릭터 파일에 `## emotion.alarm.indirect` 를 2개씩 추가한다. 시각 표현("볼만했다", "봤다") 금지. 예(여우): `소리가 꽤 컸어요~` / `벽이 울릴 정도였죠~`

### A-5. 근무 기억 화이트리스트

기억(Addenda)은 **위치·동행·동선을 묻는 답**에만 붙는다.

- `scripts/dialogue/LocalDialogueGenerator.cs` `Recall(...)`: `DialogueQuestions.Accuse => RecallTopic.Accuse` 줄을 지우고, `Suspicious`(NoSighting) 도 지운다. 남기는 것: `ShiftReview`, `Anomaly`(직접/간접 목격일 때만 — 이미 조건 있음), `Where`, `GeneralStatus`.
- `scripts/dialogue/InterviewReplyPlanner.cs` 의 `memTopic` 설정: `ReplyTopic.Confront`, `IncidentKnown`(none 변형), `MoodReason/MoodBefore/MoodRelated`, `ExactTime`, `ConfirmTestimony` 에서는 `memTopic = RecallTopic.None` 이 되도록 한다. 남기는 것: `WhereAtIncident`, `Companion`, `RouteAround`, `BeforeIncident`, `ActionThere`, `PresenceReason`, `MoveReason`, `NextLocation`.
- `ShiftMemory.Recall` 의 `companionTopic` 에서 `RecallTopic.Accuse` 를 뺀다. (enum 값 자체는 남겨도 된다.)

### A-6. 말투 값 조정 (`data/dialogue/voices/*.tres`)

| 파일 | 항목 | 현재 | 변경 |
|---|---|---|---|
| fox_voice | ImpressionChance | 0.75 | 0.35 |
| fox_voice | BackQuestionChance | 0.45 | 0.25 |
| fox_voice | QuestionEchoChance | 0.45 | 0.3 |
| dog_voice / rabbit_voice | VolunteerInfoChance | (확인) | 0.15 낮춤 |

이유: 여우 핵심 틀 자체가 이미 되묻기로 끝나는 것이 많아, 확률로 또 붙이면 되묻기가 둘이 된다.

### A-7. 검수 기준 (DialogueSampleDump 재실행 후 `python3` 로 세어서 보고할 것)

각 캐릭터 `tools/dialogue_samples/<id>.md` 에서:
- 문장 수가 `MaxSentences` 를 넘는 답: **0** (양·토끼는 caveat 포함 +1 허용)
- 물음표가 2개 이상인 답: 여우 **5개 이하**, 나머지 **0**
- `deny` / `nosight` / `noanomaly` / `status.*` 틀에 `기억[...]` 이 붙은 답: **0**
- `(닫힘)` 표시된 답 뒤에 Extra/Back/Memory 가 붙은 것: **0**
- 기존 기계 검사 40개: 전부 PASS

이 수치를 `tools/dialogue_samples/_개요.md` 의 "기계 검사" 항목에 추가한다(테스트 코드는 `DialogueScenarioTest` 에 붙인다).

---

## B. 덧붙임을 슬롯 변형으로 흡수 (`data/dialogue/lines/`, `InterviewReplyPlanner`)

목표: 지금 "핵심 + 기억 한 줄" 로 조합하는 것 중 가장 잦은 두 가지(동석자 · 혼자)를 **사람이 통째로 쓴 한 문장**으로 바꾼다. 조립기는 "틀 하나 고르기 + caveat" 로 줄어든다.

### B-1. 슬롯 신설 (`SLOTS.md` 표에도 추가)

| 슬롯 | 뜻 | 변수 | 대체하는 조합 |
|---|---|---|---|
| `WhereAtIncident.alone` | 그때 어디 있었나 — 혼자였다까지 한 문장 | room, time | `WhereAtIncident.any` + `mem.alone` |
| `WhereAtIncident.with` | 그때 어디 있었나 — 누구와 있었다까지 | room, who | `WhereAtIncident.any` + `mem.with` |
| `selfloc.alone` / `selfloc.with` | 기본 질문 "그때 어디 있었나" 의 같은 변형 | room / room, who | `selfloc` + `mem.alone/with` |
| `incident.direct.with` | 사고를 직접 봤고 옆에 누가 있었다 | iroom, what, who | `incident.direct` + `mem.with.incident` |

### B-2. 계획기 수정

- `InterviewReplyPlanner` `AskWhereAtIncident` 분기: `DialogueContextBuilder.OccupantsAt(room, day, t, id)` 로 동석자를 구해 `others.Count == 0 → Variant = "alone"`, `1명 이상 → Variant = "with"` + `f.Set("who", …)` 로 둔다. 단 **결번자가 거짓 알리바이를 대는 중(`!truthful`)이면 주장한 방 기준으로 동석자를 계산**한다(지금 `ShiftMemory.Recall` 이 `Lying` 일 때 하는 것과 같은 기준 — `AnchorRoom = claim.ClaimedRoomId`). 이 경우 `covered.Add(MemoryKind.Companion)`.
- 해당 캐릭터 파일에 그 변형 슬롯이 없으면(`DialogueLineBank.Has(id, "WhereAtIncident.alone", formal)` 가 false) `Variant = "any"` 로 되돌리고 기억 조합을 그대로 쓴다 — 캐릭터 파일을 하나씩 채워도 되게 하기 위함.
- `LocalDialogueGenerator` 의 `CoreKind.SelfLocation` 도 같은 방식으로 `selfloc.alone/with` 를 먼저 시도한다.
- `incident.direct.with` 는 `mem.with.incident` 후보가 있을 때만 시도.

### B-3. `{what}` 을 캐릭터 말투로

- `KoreanDialogueComposer.IncidentClause` 에서 `phrase.direct.<종류>` 를 찾을 때 **캐릭터 파일 우선**(`DialogueLineBank.Get(employeeId, …)` 는 이미 캐릭터 → fml/sft → any 순서로 떨어지므로, 캐릭터 파일에 `## phrase.direct.TaskFailed` 등을 추가하기만 하면 된다).
- 6개 캐릭터 파일에 `phrase.direct.TaskFailed` / `phrase.direct.PowerOutage` / `phrase.sound.any` 를 2~3개씩 추가. 동사 줄기(과거형 음절로 끝남) 규칙 유지. 예(여우): `기계가 딱 멈추더라고요~` 는 안 됨 — 줄기 형식이라 `기계가 딱 멈췄` 로 쓰고 어미는 코드가 붙인다. 여우 느낌은 앞뒤 틀에서 낸다.
- 대신 `incident.direct` 틀에 `{what_stem}` 변수를 새로 허용한다: `KoreanDialogueComposer.Vars` 에 `["what_stem"] = 어미 없는 줄기` 를 추가하고, 틀 예: `- {iroom}에서 {what_stem}는 걸 제 눈으로 봤죠~` (줄기 뒤에 `는 걸` 을 붙일 수 있게 줄기는 `멈추` 처럼 **어간**으로 두는 별도 슬롯 `phrase.stem.<종류>` 를 만든다. 기존 `phrase.direct.*` 는 그대로 둔다.)

### B-4. 여우 예문 (`data/dialogue/lines/fox.txt` 에 그대로 추가)

```
## WhereAtIncident.alone
- {room}에 혼자 있었어요~ 증인이 없어서 아쉽네요.
- 저요? {room}에서 혼자 얌전히 있었죠~ 믿기 어려우시겠지만.
- {room}이요/요~ 혼자였고요. 그게 수상해 보이세요?
- 그때는 {room}에 저 하나였어요~ 오붓하게요.

## WhereAtIncident.with
- {room}이요/요~ {who} 씨랑 같이 있었어요.
- {room}에 있었죠~ 옆에 {who} 씨가 있었으니까 물어보셔도 돼요.
- {who} 씨랑 {room}에 있었어요~ 벌벌 떠는 거 구경하면서요.
- {room}이었/였어요. {who} 씨가 증인이니까 안심하세요~

## selfloc.alone
- {room}에 혼자 있었어요~ 조용해서 좋았죠.
- 저 혼자 {room}이었/였어요~ 딴 데 있었으면 좋으시겠어요?

## selfloc.with
- {room}에서 {who} 씨랑 있었어요~
- {who} 씨랑 {room}이요/요~ 그건 왜 물으실까?

## incident.direct.with
- {iroom}에서 봤죠~ {what} {who} 씨 표정이 볼만했어요.
- 마침 {who} 씨랑 {iroom}에 있었어요~ {what}

## emotion.alarm.indirect
- 소리가 꽤 컸어요~
- 벽이 울릴 정도였죠~

## phrase.stem.TaskFailed
- 기계가 딱 멈추
- 설비가 갑자기 서

## phrase.direct.TaskFailed
- 기계가 딱 멈췄
- 설비가 갑자기 서 버렸
```

주의: `{who}` 는 `data/dialogue/lines/SLOTS.md` 규칙대로 조사는 슬래시(`{who}이랑/랑`) 로 쓰되 위 예문은 `씨` 가 붙어 받침이 고정이라 그대로 써도 된다. 추가 후 `python3 tools/lint_dialogue_lines.py` 통과 확인.

### B-5. 나머지 캐릭터

여우 예문을 기준으로 나머지 5명도 같은 슬롯을 각 3개 이상 채운다. 캐릭터 파일 상단의 말투 기준 주석과 기존 `Companion.with/alone`, `WhereAtIncident.any` 문장을 참고해 그 캐릭터 목소리로 쓴다. 늑대는 합쇼체·느낌표 없음, 양은 더듬기 포함, 토끼는 느낌표, 고양이는 느낌표 없음 — 기존 기계 검사가 이걸 잡는다.

---

## 작업 순서

1. A-1 → A-2 → A-3 (Composer 만 건드림) → 샘플 덤프 → 수치 확인
2. A-4 → A-5 → A-6 → 샘플 덤프 → 수치 확인, 기존 테스트 전부 PASS 확인
3. B-1 → B-2 → B-4(여우만) → 샘플 덤프에서 여우 `WhereAtIncident.alone/with` 가 실제로 뽑히는지 확인
4. B-3 → B-5
5. `_개요.md` 검수 항목 갱신, 커밋은 A 와 B 를 나눠서

각 단계마다 `tools/dialogue_samples/fox.md` 에서 "어색한 답 20개" 를 뽑아 보고할 것 — 수치가 통과해도 사람 귀에 이상한 건 남을 수 있다.
