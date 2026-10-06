# NSP MAIN STORY — DAY1~DAY5
## 대상 브랜치
`ver3(simple_version)`

## 이 문서의 역할

`NSP_STORY_TUTORIAL_REWORK.md` §36 이 예고한 **DAY1~5 메인 스토리 문서**입니다.

이 문서는 설계서이자 **런타임 데이터 파일**입니다. 프로젝트의 기존 방식 그대로입니다.

| 파일 | 읽는 코드 |
|---|---|
| `docs/NSP_DIALOGUE_RUNTIME.md` | `DialogueRepository` |
| `docs/NSP_PROLOGUE_RUNTIME.md` | `PrologueScript` (`res://docs/NSP_PROLOGUE_RUNTIME.md`) |
| **`docs/NSP_MAIN_STORY_DAY1_DAY5.md`** ← 이 문서 | **`StoryScript` (신규)** |

즉 **§9 의 대본을 고치면 그게 바로 게임에 반영됩니다.** 별도 데이터 파일로 옮기지 마세요.

---

# 1. 무엇을 만드는가

DAY1~5 **각 DAY 시작 시점**(배치 화면 진입 전)에 재생되는 스토리 컷인입니다.

- Phase 1~2 에서 만든 `StoryCutinDirector` / `StoryCutinHud` / `StoryBeat` 를 **그대로** 씁니다.
- 새 연출 시스템을 만들지 않습니다.
- 3D 시설 배경 + 2D 스탠딩 컷인 + 자막 — 기존 구조 그대로.

---

# 2. 절대 바꾸면 안 되는 설계 원칙

## 2.1 결번이 누구인지로 분기하지 않는다

**이 문서의 대본에는 결번(방해자) 조건 분기가 하나도 없습니다. 추가하지 마세요.**

이유는 프로젝트가 이미 여러 문서에서 못 박아 둔 것과 같습니다.

`NSP_V3_REWORK.md` §12:
> 2. 방해자 여부만으로 기분을 결정하지 않는다.
> 3. 기분만 보고 범인을 확정할 수 없어야 한다.

`data/dialogue/lines/SLOTS.md` 쓰는 원칙 4:
> 결번자도 같은 슬롯을 씁니다. (…) 정상 직원이 쓸 때도 어색하지 않아야 합니다(**범인 표시가 되면 안 됨**).

스토리가 결번 기준으로 갈리면 대사 시스템 전체를 "결번이 정상 직원처럼 말하게" 만든 작업이 무효가 됩니다. 두 번째 플레이하는 사람이 "이 대사 나오면 여우구나" 를 바로 배우기 때문입니다.

추리 단서는 **로그 · 진술 · 증거 카드**가 담당합니다. 스토리는 담당하지 않습니다.

## 2.2 대신 — 모든 자기소개가 거짓일 수 있게 쓴다

DAY1 에서 여섯 명 전부가 **"원래 여기 올 사람이 아니었다"** 는 이야기를 합니다.

재난으로 인사가 엉망이 됐으니 전부 자연스럽고, 그래서 **그중 하나가 죽은 사람의 이력을 읽고 있어도 티가 나지 않습니다.**

엔딩에서 결번이 밝혀지는 순간 그 사람의 DAY1 대사가 통째로 뒤집힙니다. 여섯 명 중 누가 걸려도 똑같이 작동합니다. 분기 0개로 여섯 가지 공포를 만드는 구조입니다.

**대본을 고칠 때도 이 규칙을 지키세요** — 어떤 직원의 어떤 대사도 "이 사람이 결번이면 말이 안 되는" 내용이면 안 됩니다.

## 2.3 직원끼리의 대화다

관리자에게 직접 말하지 않습니다. 그건 전화의 몫입니다(`NSP_STORY_TUTORIAL_REWORK.md` §2.1).

관리자는 CCTV 로 엿듣는 입장입니다. 그래서 DAY1 자기소개가 성립합니다 — 서로 처음 만난 사이라 **서로에게** 소개하는 겁니다.

## 2.4 사건 사실을 새로 만들지 않는다

시스템이 실제로 기록한 일만 말합니다. 스토리가 게임 판정의 source of truth 가 되면 안 됩니다(`NSP_STORY_TUTORIAL_REWORK.md` §38).

---

# 3. 비트 풀 구조

긴 대본 하나가 아니라 **짧고 독립적인 비트 여러 개**를 날마다 골라 조립합니다.

**비트 1개 = 2~5줄, 등장인물 1~2명, 그 자체로 완결.**

이렇게 하는 이유:

- 죽거나 기절했거나 격리된 직원이 낀 비트는 **통째로 건너뛰면 됩니다.** 대사를 한 줄씩 빼면 남은 줄이 허공에 대답하게 됩니다("그렇죠?" 앞에 아무 말도 없음).
- 한 판에 쓰는 글의 비율이 높아지고, 재플레이마다 조합이 달라집니다.

---

# 4. 파일 형식

`#` 로 시작하는 줄과 빈 줄은 무시합니다. (이 문서의 산문 구역도 그래서 무시됩니다 — 파서는 `@beat` 부터 읽습니다.)

```
@beat <id>              비트 시작
day:  <1~5 | any>       어느 DAY 에 뽑히는가
need: <id> <id> ...     이 직원이 전부 "근무 가능" 해야 재생
when: <조건>            아래 §5. 생략하면 always
kind: core | pool | react
say:  <직원id> | <표정> | <L|R> | <대사>
```

- **표정**: `normal` / `smile` / `bad`. 리소스가 없으면 `EmployeeDef.StandingImage` 로 떨어집니다.
- **L|R**: 스탠딩이 설 자리. 같은 쪽에 다른 사람이 오면 교체됩니다(기존 `CutinSide` 그대로).
- **직원 id**: `rabbit` `cat` `fox` `sheep` `wolf` `dog`
- 대사에 `|` 를 쓰지 마세요(구분자입니다).

## kind

| 값 | 뜻 |
|---|---|
| `core` | 그 DAY 에 **반드시** 재생. `need` 가 안 맞으면 조용히 생략 |
| `pool` | 후보. 조건 맞는 것 중 무작위로 2~3개 |
| `react` | 어제 일에 대한 반응. 그 DAY 의 **맨 앞**에 최대 1개 |

---

# 5. when 조건

전부 **플레이어도 아는 사실**입니다. 그래서 반응해도 답이 새지 않습니다.

| 조건 | 판정 |
|---|---|
| `always` | 항상 |
| `ghost_seen` | 어제 귀신이 나타났다 (`GhostHauntSystem.AppearedToday > 0` 를 전날 값으로) |
| `death_yesterday` | 어제 사망자가 나왔다 |
| `faint_yesterday` | 어제 기절자가 나왔다 |
| `isolated_yesterday` | 어제 격리를 집행했다 (오격리 포함) |
| `core_behind` | 코어 복구가 일정보다 뒤처졌다 (DAY N 기준 목표치 미달) |
| `core_ok` | 코어 복구가 순조롭다 |
| `record_bad` | 사망자가 있거나 `core_behind` |
| `record_good` | 사망자 없고 `core_ok` |

조건 판정은 **기존 시스템의 값만 읽습니다.** 새 상태를 만들지 마세요.

---

# 6. 재생 순서

DAY 시작 시 다음 순서로 큐를 만듭니다.

```
1) react  — 조건 맞는 것 중 재생 가능한 것 1개 (없으면 생략)
2) core   — 조건 맞고 재생 가능한 것 전부, 문서에 적힌 순서대로
3) pool   — 조건 맞고 재생 가능한 것 중 무작위 2~3개
```

- **같은 비트는 한 판에서 두 번 재생하지 않습니다.** 재생한 BeatId 를 기록해 두세요.
- 재생 가능한 비트가 하나도 없으면 그 DAY 는 스토리 없이 넘어갑니다(에러 아님).
- `PauseGameplay = true`, `SuppressAmbientDialogue = true` (§27, §28).

## 재생 가능 판정

`need` 에 적힌 직원이 **전부** 다음을 만족해야 합니다.

- 살아 있다 (`Alive`)
- 기절 상태가 아니다 (`Incapacitated == false`)
- 격리 중이 아니다 (`Isolated == false`)

하나라도 어긋나면 그 비트는 통째로 건너뜁니다.

---

# 7. 말투 기준

`data/employees/*.tres` 의 `SpeechStyle` 과 `data/dialogue/voices/*_voice.tres` 를 따릅니다. 대본을 고칠 때도 지키세요.

| 직원 | 말투 | 주의 |
|---|---|---|
| **토끼** rabbit | 해요체 · 말 빠름 · 감탄사와 질문 많음 · 느낌표 허용(최대 3) | `Observation=1` — 뭔가를 잘 못 봅니다 |
| **고양이** cat | 해요체 · 짧고 퉁명 · **느낌표 없음** · 감상보다 정보 | 걱정을 핀잔으로 덮는 츤데레 |
| **여우** fox | 해요체 · 능글 · `~` 자주 · 되묻기 | 속내를 보이지 않습니다. 곤란하면 되물어 넘깁니다 |
| **양** sheep | 해요체 · 더듬기(받침 뺀 음절: "야, 양") · 말끝 흐림 | `Observation=3` — 제일 잘 봅니다. **매 문장 더듬지 않습니다**(과장 금지) |
| **늑대** wolf | **합쇼체**(…습니다) · 짧고 핵심 · 느낌표와 물결 없음 | 허세 없음. 모르면 모른다고 합니다 |
| **강아지** dog | 해요체 · 부드러움 · 남의 상태를 챙김 | "네 알겠습니다" 만 하는 수동적 말투가 되면 안 됩니다 |

---

# 8. 스토리 아크

| DAY | 내용 |
|---|---|
| 1 | 자기소개 · 왜 여기 오게 됐는가. 전부 "원래 올 사람이 아니었다" |
| 2 | 불안 · 귀신 · 이 속도로 되겠는가 |
| 3 | 패닉 · 신경이 끊어지기 시작 |
| 4 | 의심 · 그리고 관리자에 대한 불만/신뢰 (성적에 따라 갈림) |
| 5 | 마지막 근무 전 · 각자의 끝 |

---

# 9. 대본

여기부터가 파서가 읽는 구역입니다.

## DAY 1 — 자기소개 · 배경

여섯 명은 서로 처음 만난 사이입니다. 세 비트 전부 재생됩니다(DAY1 에는 아직 아무도 빠지지 않음).

```
@beat d1_intro_a
day: 1
need: rabbit cat
when: always
kind: core
say: rabbit | smile | L | 다들 오늘 처음 뵙는 거 맞죠? 저는 토끼예요!
say: rabbit | smile | L | 원래 제2시설 배정이었는데 어제 갑자기 여기로 바뀌더라고요. 뭐, 어디든 일은 똑같겠죠!
say: cat | normal | R | 고양이예요. 코어 정비 쪽이라 불려 왔고요.
say: cat | normal | R | 인사는 이쯤 하죠. 지금 여섯 명으로 돌릴 시설이 아니거든요, 여기.

@beat d1_intro_b
day: 1
need: sheep wolf
when: always
kind: core
say: sheep | bad | L | 야, 양이에요... 제5지하시설에 있었는데, 거기는 지금 아무도 없어요.
say: sheep | bad | L | 저만 남아서 여기로 보내졌어요. 왜 저였는지는... 저도 잘 모르겠어요.
say: wolf | normal | R | 늑대입니다. 격리구역 경비 담당이었습니다.
say: wolf | normal | R | 제가 지키던 구역은 사고 당시 뚫렸습니다. 그 이상은 말씀드릴 게 없습니다.

@beat d1_intro_c
day: 1
need: fox dog
when: always
kind: core
say: fox | smile | L | 여우예요~ 저는 뭐, 어쩌다 보니 여기 있네요.
say: fox | smile | L | 다들 사연이 깊으신데 저까지 보태면 분위기가 더 가라앉을 것 같아서요~
say: dog | smile | R | 강아지예요. 저는 원래 오기로 하신 분이 못 오게 돼서 대신 왔어요.
say: dog | smile | R | 다들 고생 많으셨겠어요. 필요한 거 있으면 편하게 말씀해 주세요.
```

> **메모** — 늑대의 "제가 지키던 구역은 사고 당시 뚫렸습니다" 는 프롤로그 무전의 *"격리 구역에서 개체들이 빠져나왔어요!"* 를 받습니다. 강아지의 "원래 오기로 하신 분이 못 오게 돼서 대신 왔어요" 는 결번 설정과 가장 직접 맞물립니다. 둘 다 **그 사람이 결번이 아니어도 완전히 자연스러운** 문장입니다.

## DAY 2 — 불안 · 귀신 · 이 속도로 되겠는가

```
@beat d2_ghost
day: 2
need: sheep dog
when: ghost_seen
kind: pool
say: sheep | bad | L | 저, 어제... 뭔가 봤어요. 사람 같았는데, 사람은 아니었어요.
say: sheep | bad | L | 제가 잘못 본 거면 좋겠는데... 그렇게 생각하려고 해도 잘 안 돼요.
say: dog | normal | R | 혼자 보셨으면 많이 무서우셨겠어요. 다음엔 저 부르세요, 같이 가면 되니까요.
say: dog | bad | R | ...그런데 그런 걸 본 게 양 씨만은 아니더라고요.

@beat d2_progress
day: 2
need: cat wolf
when: always
kind: pool
say: cat | bad | L | 어제 복구율 보셨어요? 이 속도면 닷새로는 안 끝나요.
say: cat | bad | L | 자재는 계속 쓰이는데 만드는 사람이 모자라요. 계산이 안 맞아요.
say: wolf | normal | R | 압니다. 그래서 더 움직일 생각입니다.
say: wolf | normal | R | 불가능하다고 말하는 건 끝난 다음에 해도 늦지 않습니다.

@beat d2_fox_light
day: 2
need: fox rabbit
when: always
kind: pool
say: fox | smile | L | 다들 표정이 왜 그래요~ 아직 사흘이나 남았잖아요.
say: rabbit | bad | R | 사흘 '이나' 요? 저는 사흘 '밖에' 로 들렸는데요!
say: fox | smile | L | 그것도 맞네요~ 뭐, 저는 어느 쪽이든 상관없어서요.
say: rabbit | bad | R | 그 여유는 대체 어디서 나오는 거예요...

@beat d2_sleep
day: 2
need: rabbit sheep
when: always
kind: pool
say: rabbit | bad | L | 저 어제 거의 못 잤어요. 환풍구에서 계속 무슨 소리가 나가지고요.
say: sheep | bad | R | 그, 그거 저도 들었어요. 근데 저는... 그게 환풍구 소리가 맞는지 모르겠어요.
say: rabbit | bad | L | ...그런 말을 그렇게 조용히 하지 마세요. 더 무섭잖아요.

@beat d2_wolf_fear
day: 2
need: wolf fox
when: always
kind: pool
say: fox | smile | L | 늑대 씨는 안 무서우세요~? 어제 그 소리요.
say: wolf | normal | R | 무섭습니다.
say: fox | normal | L | 어, 의외네요. 아니라고 하실 줄 알았는데.
say: wolf | normal | R | 무서운 것과 할 일을 하는 건 다릅니다.
```

## DAY 3 — 패닉

```
@beat d3_panic_sheep
day: 3
need: sheep dog
when: always
kind: pool
say: sheep | bad | L | 저... 저 못 하겠어요. 아까부터 손이 계속 떨려서 공구를 못 잡겠어요.
say: sheep | bad | L | 죄송해요. 제가 이러면 안 되는 거 아는데...
say: dog | normal | R | 사과 안 하셔도 돼요. 지금 안 떨리는 사람이 이상한 거예요.
say: dog | smile | R | 오늘은 제가 양 씨 몫까지 할게요. 대신 내일은 좀 도와주세요.

@beat d3_cat_snap
day: 3
need: cat rabbit
when: always
kind: pool
say: cat | bad | L | 토끼 씨, 아까 그 밸브 잠그고 나왔어요?
say: rabbit | bad | R | 어... 그, 잠갔던 것 같은데요?
say: cat | bad | L | '같은데' 로 되는 게 아니에요. 한 번 틀리면 사람이 죽어요, 여기서는.
say: cat | normal | L | ...미안해요. 다시 가서 확인만 해 주세요.

@beat d3_wolf_hold
day: 3
need: wolf dog
when: always
kind: pool
say: dog | normal | L | 늑대 씨, 안 쉬세요? 어제부터 계속 움직이시던데.
say: wolf | normal | R | 쉬면 생각이 납니다. 움직이는 편이 낫습니다.
say: dog | bad | L | ...그 말, 양 씨한테는 하지 마세요. 따라 할 것 같아서요.
say: wolf | normal | R | 알겠습니다.

@beat d3_fox_crack
day: 3
need: fox cat
when: always
kind: pool
say: fox | smile | L | 고양이 씨, 잠깐만요. 손 좀 보여 주실래요~?
say: cat | bad | R | 왜요.
say: fox | normal | L | 아니요. 저만 떨리는 줄 알았는데 아니어서 다행이다 싶어서요~
say: cat | normal | R | ...농담할 기운은 있나 보네요.

@beat d3_rabbit_down
day: 3
need: rabbit fox
when: always
kind: pool
say: rabbit | bad | L | 저, 아까 발전실에서... 벽에 뭔가 긁힌 자국 같은 게 있었는데요.
say: rabbit | bad | L | 그게 원래 있던 건지 아닌지를 모르겠어요. 제가 그런 걸 잘 못 봐서...
say: fox | normal | R | 관리자님께 말씀드렸어요~?
say: rabbit | bad | L | ...아직요. 괜히 아무것도 아닌 걸로 소란 피우는 걸까 봐요.
```

## DAY 4 — 의심 · 관리자에 대한 불만

`d4_blame` 과 `d4_trust` 는 성적에 따라 **한쪽만** 재생됩니다.

```
@beat d4_suspect_open
day: 4
need: cat wolf
when: always
kind: core
say: cat | bad | L | 이건 고장이 아니에요. 누가 손을 댄 거예요.
say: cat | bad | L | 저 설비는 저절로 저렇게 안 어긋나요. 제가 매일 보는 건데.
say: wolf | normal | R | 같은 생각입니다.
say: wolf | normal | R | 여섯 명 중에 있다는 뜻입니다. 저를 포함해서요.

@beat d4_blame
day: 4
need: cat rabbit
when: record_bad
kind: core
say: cat | bad | L | 관리자님은 대체 뭘 보고 사람을 배치하는 거예요?
say: cat | bad | L | 위에서 숫자만 보니까 여기서 누가 쓰러지는지는 안 보이겠죠.
say: rabbit | bad | R | 그, 그래도 관리자님도 사정이 있으시겠죠...
say: cat | bad | L | 사정 없는 사람이 어디 있어요. 우리는 사정이 있어도 내려가야 하는데.

@beat d4_trust
day: 4
need: dog wolf
when: record_good
kind: core
say: dog | smile | L | 그래도 지금까지 아무도 크게 안 다친 건, 관리자님 덕분이라고 생각해요.
say: wolf | normal | R | 동의합니다.
say: dog | smile | L | 그렇게 말하면서 왜 한 번도 직접 말씀 안 드려요?
say: wolf | normal | R | ...다음 근무 끝나고 하겠습니다.

@beat d4_suspect_sheep
day: 4
need: sheep dog
when: always
kind: pool
say: sheep | bad | L | 저... 그제 밤에, 누가 저장고 쪽으로 가는 걸 봤어요.
say: sheep | bad | L | 근데 얼굴은 못 봤어요. 그러니까... 제가 아무 말도 안 한 걸로 해 주세요.
say: dog | bad | R | 그렇게 혼자 담아두시면 더 힘들어요.
say: dog | normal | R | ...그래도 확실하지 않은 걸 말하면 누군가는 다칠 테니까요. 알겠어요.

@beat d4_fox_deflect
day: 4
need: fox rabbit
when: always
kind: pool
say: rabbit | normal | L | 여우 씨는 누가 의심돼요?
say: fox | smile | R | 글쎄요~ 저는 사람 보는 눈이 없어서요.
say: rabbit | normal | L | 에이, 제일 잘 보시잖아요.
say: fox | normal | R | 그렇게 보이면 좀 곤란한데요~ 보는 사람이 제일 먼저 의심받거든요.

@beat d4_fear_all
day: 4
need: rabbit sheep
when: always
kind: pool
say: rabbit | bad | L | 저 이제 누구랑 같은 방에 있는 게 좀 무서워요.
say: sheep | bad | R | 저... 저도요.
say: rabbit | bad | L | 근데 혼자 있는 건 더 무섭고요.
say: sheep | bad | R | ...저도요.
```

## DAY 5 — 마지막 근무 전

마지막이므로 재생 가능한 것을 **최대 4개**까지 뽑습니다.

```
@beat d5_last_morning
day: 5
need: cat wolf
when: always
kind: pool
say: cat | normal | L | 오늘이 마지막이에요. 차폐가 버티는 건 오늘까지.
say: wolf | normal | R | 압니다.
say: cat | normal | L | ...끝나면 뭐 하실 거예요?
say: wolf | normal | R | 생각해 본 적 없습니다. 오늘이 끝나면 생각하겠습니다.

@beat d5_fox_truth
day: 5
need: fox cat
when: always
kind: pool
say: fox | smile | L | 고양이 씨, 하나만 물어봐도 돼요~?
say: cat | normal | R | 빨리요.
say: fox | normal | L | 여기 들어올 때, 나갈 수 있을 거라고 생각하셨어요?
say: cat | bad | R | ...아니요.
say: fox | smile | L | 저도요. 그럼 오늘은 둘 다 운이 좋은 날이네요~

@beat d5_promise
day: 5
need: dog wolf
when: always
kind: pool
say: dog | smile | L | 다들 무사히 나가면, 한 번 모여서 밥이라도 먹어요.
say: wolf | normal | R | ...좋습니다.
say: dog | smile | L | 약속한 거예요. 늑대 씨가 약속 어기는 사람은 아니니까요.
say: wolf | normal | R | 지키겠습니다.

@beat d5_closing
day: 5
need: sheep rabbit
when: always
kind: pool
say: sheep | normal | L | 저, 토끼 씨.
say: rabbit | normal | R | 네?
say: sheep | smile | L | 처음 왔을 때 제일 먼저 말 걸어 주셔서... 고마웠어요.
say: rabbit | bad | R | ...갑자기 왜 그런 말을 해요. 끝난 것처럼.
```

## 반응 비트

어제 일에 대한 반응입니다. 그 DAY 의 **맨 앞**에 하나만 붙습니다. 조건이 겹치면 재생 가능한 것 중 하나를 고릅니다.

```
@beat r_death_a
day: any
need: rabbit dog
when: death_yesterday
kind: react
say: rabbit | bad | L | 그, 그분이 정말... 돌아가신 거예요?
say: dog | bad | R | ...네. 제가 마지막으로 봤어요.
say: rabbit | bad | L | 누가 그랬는데요?! 사고 맞아요? 사고 맞죠?
say: dog | bad | R | ...모르겠어요. 그게 제일 무서워요.

@beat r_death_b
day: any
need: cat wolf
when: death_yesterday
kind: react
say: cat | bad | L | 한 명 줄었어요. 일은 그대로고요.
say: wolf | normal | R | 압니다.
say: cat | bad | L | ...그런 말이 하고 싶었던 게 아니에요.
say: wolf | normal | R | 압니다.

@beat r_faint
day: any
need: dog
when: faint_yesterday
kind: react
say: dog | bad | L | 어제 쓰러진 분, 의무실에서 깨어났대요.
say: dog | bad | L | 괜찮다고는 하시는데... 괜찮아 보이진 않았어요.

@beat r_isolated
day: any
need: cat fox
when: isolated_yesterday
kind: react
say: cat | normal | L | 격리실에 사람이 들어갔어요.
say: fox | smile | R | 맞는 사람이었으면 좋겠네요~ 아니면 그냥 한 명 줄어든 거니까요.

@beat r_ghost
day: any
need: sheep
when: ghost_seen
kind: react
say: sheep | bad | L | 어제 그거... 다들 보셨죠? 저만 본 거 아니죠?
say: sheep | bad | L | 저만 본 거면... 제가 이상한 거잖아요.
```

---

# 10. 구현

## 10.1 새로 만드는 것 (최소)

| 클래스 | 역할 |
|---|---|
| `StoryScript` | 이 문서를 파싱해 `StoryBeat` + 메타(day/need/when/kind) 로 만든다. `PrologueScript` 의 파싱 방식을 그대로 베낀다 |
| `StoryBeatSelector` | DAY · 생존 상태 · when 조건으로 재생할 비트 큐를 만든다 |

`StoryBeat` 에 메타 필드를 추가하거나, `StoryBeatEntry { StoryBeat Beat; int Day; string[] Need; string When; BeatKind Kind; }` 래퍼를 둡니다. **둘 중 더 단순한 쪽으로.**

## 10.2 재사용하는 것

- `StoryCutinDirector` / `StoryCutinHud` / `StoryBeat` / `StoryLine` — Phase 1~2 결과물. 수정 최소화.
- `PrologueScript` 의 파일 읽기·줄 파싱 패턴.
- `FacilitySimulation` / `EmployeeState` — 생존·기절·격리 판정.
- `GameState` — 코어 진행률, DAY.

## 10.3 연결 지점

`ShiftFlowController` 의 **DAY 시작(배치 화면 진입 전)** 에 한 번 호출합니다.

- DAY0(튜토리얼)에서는 재생하지 않습니다.
- 스토리가 끝나면 평소대로 배치 화면으로 넘어갑니다.
- 재생할 비트가 없으면 바로 배치 화면으로 (에러 아님).

## 10.4 export_presets.cfg

**이걸 빼먹으면 에디터에서는 되는데 빌드에서 스토리가 통째로 사라집니다.**

13행과 65행의 `include_filter` 두 군데 전부에 추가하세요.

```
include_filter="docs/NSP_DIALOGUE_RUNTIME.md,docs/NSP_PROLOGUE_RUNTIME.md,docs/NSP_MAIN_STORY_DAY1_DAY5.md,data/dialogue/lines/*.txt"
```

## 10.5 fail-safe

`NSP_STORY_TUTORIAL_REWORK.md` §38 대로, 스토리가 실패해도 게임이 잠기면 안 됩니다.

- 파싱 실패 → 경고만 띄우고 스토리 없이 진행
- 알 수 없는 직원 id / 조건 → 그 비트만 버림
- 컷인 종료 실패 → 타임아웃 후 강제 해제

---

# 11. 검수

1. DAY1~5 를 통과하며 각 DAY 에 스토리가 뜨는지. DAY0 에는 안 뜨는지.
2. **직원 한 명을 죽이고** 다음 DAY 진행 → 그 직원이 `need` 에 든 비트가 재생되지 않는지. 남은 비트만으로 대화가 자연스러운지.
3. 기절 · 격리도 같은지.
4. 같은 비트가 한 판에서 두 번 나오지 않는지.
5. `record_bad` / `record_good` 가 실제 성적대로 갈리는지. **둘이 같이 나오지 않는지.**
6. 스토리 중 ambient 3D 대화 자막이 겹치지 않는지(§28).
7. 스토리 중 근무 시간이 흐르지 않는지(`PauseGameplay`).
8. 클릭 스킵: 1차 = 문장 완성, 2차 = 다음 줄 (§33).
9. `dotnet build` 통과.
10. **export 빌드**에서도 스토리가 나오는지 (10.4 확인).

---

# 12. 이 문서를 고칠 때

- 대사만 고치는 건 자유입니다. 코드 수정 없이 반영됩니다.
- **비트를 추가**할 때는 `@beat` 블록 하나를 통째로 넣으세요. `need` 를 정확히 적어야 결원 시 깨지지 않습니다.
- §2 의 원칙(결번 분기 금지 · 모든 소개가 거짓일 수 있게 · 직원끼리의 대화 · 사실 창작 금지)은 고치지 마세요.
- 말투는 §7 표를 따르세요.
