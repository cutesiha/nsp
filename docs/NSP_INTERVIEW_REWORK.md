# NSP 휴게시간 심문 리워크 — 추리 규칙 · 자료 카드 · UI 배치

> 대상 브랜치: `ver3(simple_version)`
> 작성 기준 커밋: `40f1939` → 1차 반영 후 `cefe396` 기준으로 §3-6 개정, §3-7 추가 (2026-09-24)
> 마감: 1차 10/15 (교내 대회) · 최종 10/31 (직업계고 IT 게임개발대회)
> 이 문서는 "왜 고치는가 → 무엇을 어떻게 고치는가 → 파일별 작업 → 수용 기준" 순서다. 코드를 고치는 사람(Claude Code 에이전트 포함)은 §4 작업 목록과 §5 수용 기준만 보고도 작업할 수 있어야 한다.

---

## 0. 한 줄 요약

지금 심문 엔진(`EvidenceContradiction`)은 **"자료 두 장의 위치가 어긋나는가"** 만 판정한다. 그런데 V2 이후 결번자는 **제자리에서 범행하고 위치에 대해 거짓말을 하지 않는다**. 잡을 모순이 생성되지 않으니 추궁이 계속 실패한다. 판정을 시뮬레이션이 실제로 남기는 단서(그 방에 있었는가 · 설비 쪽 이상 행동 · 동료 목격)에 맞추고, UI는 "MON01 = 조사 노트, MON02 + 자막 띠 = 대화"로 역할을 나눈다.

---

## 1. 진단 — 코드에서 확인한 사실

### 1-1. "이 직원의 위치를 확인할 수 있는 자료가 아닙니다"가 계속 뜨는 이유

`scripts/dialogue/EvidenceContradiction.cs` `Check()`:

```
if (a.SubjectEmployeeId != targetEmployeeId || b.SubjectEmployeeId != targetEmployeeId)
    fail.Notice = "이 직원의 위치를 확인할 수 있는 자료가 아닙니다.";
if (!a.CanAnchorPosition || !b.CanAnchorPosition)
    fail.Notice = "시각과 위치가 함께 기록된 자료여야 비교할 수 있습니다.";
```

`scripts/dialogue/InterviewEvidenceBoard.cs` `AddLogRows()` 는 **사고 기록을 `SubjectEmployeeId = ""`, `Position = PositionClaim.None`** 으로 만든다. 플레이어가 가장 먼저 집는 카드가 사고 기록인데, 이 카드는 어떤 카드와 짝지어도 첫 조건에서 막힌다. 기분 카드도 `Position = None` 이라 막힌다.

결과: 실제로 비교 가능한 조합은 `CCTV ↔ 이동기록 ↔ 시각이 붙은 본인 진술` 뿐이다.

### 1-2. 그 조합이 성립할 일이 거의 없다

- `scripts/facility/SaboteurPlan.cs` 상단 주석: *"결번자는 스스로 방을 옮기지 않는다. 플레이어가 배치해 준 자리에서 정상 직원인 척 일하다가 기회를 잡는다."*
- `scripts/dialogue/DialogueResponsePlanner.cs` `EnsureClaimedRoom()`:
  ```
  string real  = RoomAtSubject (없으면 AssignedRoomId);
  string cover = AssignedRoomId;
  claim.ClaimedRoomId = hides && cover != real ? cover : real;
  ```
  결번자가 배치된 방에서 범행하므로 `real == cover` → **`ClaimTruthful = true`** → 결번자는 위치에 대해 거짓말하지 않는다. (예외: 플레이어가 사고 뒤 결번자를 다른 방으로 옮긴 경우.)
- 결번자가 실제로 쓰는 전략은 `Omit / Vague / Minimize / Justify`. 전부 "흐리기"이고, 반박 가능한 거짓 주장이 아니다.

→ 위치 모순 판정은 V1(결번자가 돌아다니던 시절)용 규칙이다. 지금 시뮬레이션에는 잡을 거리가 없다.

### 1-3. 시뮬레이션이 실제로 만드는 단서

`SaboteurPlan.TickPrecursors / CollectClues`, `EmployeeBehaviorSystem`, `FacilityLogFormatter`:

| 단서 | 어디서 생기나 | 플레이어에게 어떻게 도달하나 |
|---|---|---|
| 방해공작 로그 한 줄 (방 · 시각 · **이름 없음**) | `FacilityLogFormatter` ④ | 시설 로그 화면 → `Incident` 카드 |
| 그 시각 그 방에 누가 있었나 | 최초 배치 줄 · `RecordCctvObservation` · 순찰 기록 | 시설 로그 / `Cctv` 카드 |
| 설비 접근 (준비 35%) | `MarkSuspiciousAction` | CCTV로 그 방을 보고 있으면 잡힘 |
| 수치 미세 이상 (준비 60%) | `TriggerMicroFault` | 수치를 보고 있으면 눈에 띔 |
| **동료 목격 "설비 쪽에 평소보다 오래 머물렀다"** (준비 80%) | `RecordOddBehaviour` → `Neglect` 이벤트 + witness | 그 동료에게 Q3(수상한 사람) 을 물어야 나옴 → `Testimony` 카드 |
| 범행 순간 직접 목격 | `CollectClues` (awareness ≥ 임계) | 동일 |
| 가짜 단서: 결백한 직원의 "설비 확인" | `EmployeeBehaviorSystem.Tick` | 동일 경로로 목격됨 |

즉 이 게임의 추리는 **"그 방에 있던 사람 중 누가 이상 행동을 했고, 누가 봤으며, 본인은 그걸 어떻게 얼버무리는가"** 다. "누구의 진술이 기록과 어긋나는가"가 아니다.

### 1-4. 단서가 카드가 되는 순간 정보가 사라진다

- `PlayerKnownEvidence.SightingStatement` 는 `RoomId` 만 저장한다. "설비 쪽에 오래 머물렀다"는 내용이 버려지고 카드에는 `늑대 · 정비실에서 봤다` 만 남는다. 가장 중요한 단서의 알맹이가 빠진다.
- `LocalDialogueGenerator.RecordEvidence()` 는 `plan.Core` 가 `SelfLocation / SuspiciousSighting / SightingPlace` 일 때만 기록한다. 최초 진술 3개(`ShiftReview / Anomaly / Suspicious`) 중 Q1 답변("정비실 고장 때 저도 그 방에 있었어요")은 Core가 `IncidentDirect` 라 **카드가 되지 않는다.** 화면에 크게 뜨는 진술이 자료로는 못 쓰인다.

### 1-5. UI

- `PhoneCallHud.Open()` 에서 `_session.SetTab(NoteTab.ByIncident)` → 시작부터 전원 자료. 대부분 회색(사용 불가) 카드라 시각 잡음이다.
- 질문 선택은 MON01, 답변 타이핑은 하단 자막 띠, 얼굴은 MON02 → 시선이 세 군데로 갈라져 대화하는 느낌이 없다.
- `RestInterviewConsole` 800×600 한 장에 진술 블록 3 + 꼬리질문 + 자료 목록 + 슬롯 2 + 버튼 4. 기울어진 CRT에서 읽힐 양이 아니다.
- 추궁이 성립하든 말든 휴게실 화면(`RestRosterView`)에는 아무 흔적이 남지 않는다. 심문 → 격리 결정 사이에 피드백 고리가 없다.

---

## 2. 목표

1. 플레이어가 자료를 고르면 **거의 항상 무언가를 물을 수 있다.** "자료가 아닙니다" 류의 거절은 원칙적으로 없앤다.
2. 결번자에게 **잡을 수 있는 거짓말**을 최소 하나 준다. 결백한 직원은 같은 상황에서 순순히 인정한다.
3. 직원이 말한 것이 **그 자리에서 카드가 되는 것**이 보인다.
4. 대화는 한 곳(MON02 + 자막 띠)에서, 자료는 한 곳(MON01)에서.
5. 추궁의 결과가 휴게실 화면에 남아 격리 판단의 재료가 된다. 단, 게임은 정답을 알려주지 않는다.

---

## 3. 설계

### 3-1. 추궁 규칙 — 하나에서 셋으로

`EvidenceContradiction` 을 유지하되 결과에 `Kind` 를 추가하고, 판정을 세 규칙으로 늘린다. 규칙은 위에서 아래로 검사하며 **처음 성립하는 것**을 쓴다.

```csharp
public enum ConfrontKind { None, Presence, Behavior, Location }
```

**① Presence(재석 추궁)** — `Incident` 카드 + 이 직원이 그 방에 있었다는 카드
- 조건: 한쪽이 `Kind == Incident`, 다른 쪽이 `SubjectEmployeeId == target && CanAnchorPosition`, `RoomClaimedAt(other, incident.AnchorTime) == incident.SubjectRoomId`, 시간 차 ≤ `WindowMinutes`.
- 질문문: `"{time}경 {room}에서 사고가 났을 때 그 방에 계셨습니다. 무엇을 하고 있었습니까?"`
- 답변: 기존 `ConfrontAnswer` 경로 그대로. 결백 → `Confront.honest`, 결번자(Omit/Vague) → `Confront.evasive`, 증거 2개 이상 → `Confront.deny`.
- 이 규칙 하나로 §1-1의 벽이 사라진다.

**② Behavior(행동 추궁)** — `Testimony`(Detail 있음) 또는 설비 접근 `Cctv` 카드 + 같은 방·같은 시간대의 `Incident` 카드
- 조건: 한쪽이 `Incident`, 다른 쪽이 `SubjectEmployeeId == target` 이고 `BehaviorDetail` 이 비어 있지 않음, 같은 방, 시간 차 ≤ `WindowMinutes * 2` (전조는 사고보다 앞서 나오므로 폭을 두 배로).
- 질문문: `"사고 직전 {room}에서 {detail}는 증언이 있습니다. 설명해 주시죠."`
- 답변: 결번자가 `DeniesEquipmentContact` 를 주장한 상태면 `Confront.deny` 계열(§3-2), 아니면 evasive. 결백한 직원(가짜 단서의 주인)은 honest — "네, 수치가 이상해서 봤습니다" 류로 인정한다.

**③ Location(위치 추궁)** — 현재 규칙 그대로. 플레이어가 사고 뒤 결번자를 옮긴 경우에만 성립하는 보너스 루트.

**성립하지 않는 조합** — 거절하지 않는다. `Kind = None` 으로 돌려주고, UI 는 "두 자료를 함께 제시한다" 선택지를 그대로 띄운다. 답변은 새 슬롯 `Confront.neutral` (결번자·결백 공통, 중립 반응: "그 둘이 무슨 상관이죠?" 류). 화면에 **"모순 성립" 표시만 붙지 않는다.** 틀린 조합을 시도하는 것도 추리의 일부다.

### 3-2. 결번자에게 반박 가능한 거짓말 하나

`DialogueClaim` 에 필드 추가:

```csharp
public bool DeniesEquipmentContact;   // "설비 근처에는 가지 않았다"
public bool EquipmentDenialDecided;
```

결정 시점: `DialogueResponsePlanner.Plan()` 에서 `EnsureClaimedRoom` 직후, `ctx.IsSaboteur && ctx.IsSubjectActor && mode is Omit or Vague or Deny` 이면 `DeniesEquipmentContact = true`. 한 번 정하면 그 사건 동안 바뀌지 않는다(알리바이와 같은 규칙).

표출: 결번자가 이 사건에 대해 `SelfLocation` / `PresenceReason` / `ActionAtDestination` 을 답할 때 `DeniesEquipmentContact` 면 문장 뒤에 `Denial.equipment` 슬롯 한 줄을 덧붙인다("설비 쪽엔 손도 안 댔어요~"). 결백한 직원은 이 슬롯을 쓰지 않는다.

기록: 이 문장이 나가면 `PlayerKnownEvidence.RecordBehaviorClaim(employeeId, incidentKey, "설비 근처에 가지 않았다", time)` → `OwnStatement` 카드로 올라온다(Body: `본인 · 설비 쪽에 안 갔다`). 이 카드 + 동료 목격 `Testimony` 카드 = Behavior 규칙 성립.

주의: `data/dialogue/lines/SLOTS.md` 규칙대로 결번자 전용 슬롯이라도 결백한 직원이 써도 어색하지 않아야 한다. 다만 이 슬롯은 결백한 직원이 부르지 않으므로 표현은 자유롭다.

### 3-3. 카드에 내용을 싣기

`PlayerKnownEvidence.SightingStatement` 에 `public string Detail = "";` 추가. `RecordSighting(speaker, subject, room, time, detail = "")` 로 시그니처 확장.

호출부:
- `LocalDialogueGenerator.RecordEvidence()` `CoreKind.SuspiciousSighting` — `ctx.KnownSuspicious` 의 원문에서 `what` 을 꺼내 넘긴다. `RecordOddBehaviour` 가 남기는 `Neglect` 이벤트 Description 은 `"{Codename} — {what}"` 형식이므로 `" — "` 뒤를 자르면 된다. `DialogueFact` 가 Description 을 밖으로 내보내지 않는 정책이므로, `DialogueContext.KnownSuspiciousDetail` 필드를 하나 두고 `DialogueContextBuilder` 에서 채운다.
- `InterviewReplyPlanner` 의 `RecordSighting` 두 곳 — 함께 있던 사람 기록이므로 `detail` 은 빈 값 그대로.

`InterviewEvidence` 에 `public string BehaviorDetail = "";` 추가. `InterviewEvidenceBoard.AddTestimonies()` 에서 `Detail` 이 있으면
- `Body = $"{Codename(speaker)} · {detail}"` (방 이름은 `TimeText` 옆 태그로),
- `BehaviorDetail = detail`.

`AddCctv()` 에서 그 관찰이 `HasSuspiciousAction` 이 켜져 있던 시각이면 `BehaviorDetail = "설비 쪽에 접근"` 을 붙인다. (`CctvObservation` 에 `bool SuspiciousAction` 필드를 추가하고 `FacilitySimulation` 의 `RecordCctvObservation` 호출부에서 `HasSuspiciousAction(room)` 을 넘긴다.)

### 3-4. 최초 진술을 카드로

`LocalDialogueGenerator.RecordEvidence()` 에 분기 추가:

```csharp
case CoreKind.IncidentDirect:
case CoreKind.IncidentIndirect:
    if (ctx.HasSubjectTime && !string.IsNullOrEmpty(plan.RoomId))
        PlayerKnownEvidence.RecordLocationStatement(ctx.EmployeeId, key, plan.RoomId,
            plan.Time == TimeRef.Exact, ctx.SubjectTime);
    break;
```

`plan.RoomId` 는 결번자면 `ClaimedRoomId`, 아니면 실제 방이다(이미 그렇게 세팅됨). 이로써 "이상한 점" 답변이 시각+방이 붙은 `OwnStatement` 카드가 되어 세 규칙 전부에 쓸 수 있다.

### 3-5. 추궁 결과 → 휴게실 화면

`InterviewSession.Confronted` 이벤트 시그니처를 `(string employeeId, ConfrontKind kind, string variant)` 로 확장. `RestRosterView`(또는 `BreakRoomTopView`) 가 구독해서 직원 아이콘 옆에 작은 표식을 누적한다.

- variant `evasive`/`deny` → 붉은 점 하나 추가. 아이콘 아래 `진술 흔들림 ×N`.
- variant `honest` → 회색 점 하나. `해명 ×N`.
- `neutral`(성립 안 함) → 표식 없음.

이 표식은 **플레이어의 메모**다. 게임 판정(격리 성공 여부)에 쓰지 않는다. 결백한 직원도 evasive 슬롯을 쓸 수 있으므로(SLOTS.md 규칙) 붉은 점 = 범인이 아니다.

### 3-6. UI 배치 (1.5차에서 개정)

> 1차 커밋(`4f199f4`, `5ef7aca`)의 "대화를 자막 띠 한 곳으로" 방식은 플레이테스트에서 실패했다. 최초 진술이 자동 재생으로 지나가고, 답변이 남지 않으며, 자막 띠가 MON01 을 가리고, 대화창을 치우려면 통화를 끊어야 했다. **대화창은 맨 처음 휴게시간 방식(화면 중앙 아래 창 + 선택지 목록)으로 되돌린다.** 1차의 추리 규칙 커밋(`8a59cc1`, `675039d`)은 그대로 둔다.

세 면의 역할:

| 면 | 역할 | 내용 |
|---|---|---|
| MON02 (`InterviewCCTVView`) | 얼굴 | 스탠딩 · 입 모양 · 긴장 발광 (변경 없음) |
| 대화창 (`PhoneCallHud`, 일반 통화와 같은 위치·크기) | **대화 전부** | 직전 문답 2~3개(흐리게) · 현재 답변 · **선택지 목록** · 접기 |
| MON01 (`RestInterviewConsole`) | **조사 자료** | 조사 목표 한 줄 · 큼직한 카드 · 상세칸 · 슬롯 A/B · (2차) 직원별 띠 시간표 |

**대화창**
- 위치·크기는 일반 통화(`AnchorLeft 0.24 / Right 0.76 / Top 0.62 / Bottom 0.95`)와 같다. `SetInterviewLayout(true)` 의 자막 띠 레이아웃은 버린다.
- **자동 재생 금지.** `AfterMode.InterviewOpening` / `NextOpening()` 을 제거한다. 최초 진술은 인사 뒤 선택지로 고른다.
- 선택지 구성(위에서 아래로):
  1. 기본 질문 3개 — "오늘 근무는 어땠습니까?" · "오늘 이상한 점을 느꼈습니까?" · "수상한 행동을 한 사람을 봤습니까?" (`InterviewSession.Openings` 를 그대로 쓴다. 이미 들은 것은 ✓ 표시, 다시 골라 다시 들을 수 있다.)
  2. 방금 답변의 꼬리질문 0~2개 (`OpeningFollowUps()` / `Turn.FollowUps`)
  3. MON01 슬롯에 카드가 1장이면 `[자료: 본문 앞 10자]로 묻는다`, 2장이면 `두 자료를 제시한다` — `CheckContradiction().Kind != None` 이면 붉은색 + 질문문 미리보기, 아니면 기본색(중립 반응)
  4. `통화를 종료한다.` — **세션을 끝내는 유일한 버튼.** 확인 없이 바로 끊는다(기존 동작).
- 답변이 나오는 순간 MON01 에 카드가 추가되면 `FlashNotes()` 1회. 기존 커밋의 연출을 재사용한다.
- **직전 문답 남기기**: `_message` 위에 `RichTextLabel _history` 를 두고, 마지막 2~3 문답(`관리자 ▸ 질문` / `직원 ▸ 답변`)을 40% 밝기로 표시한다. 새 답변이 들어오면 위로 밀린다. 전체 기록은 그대로 `D 대화 기록`.
- **접기**: 창 손잡이(`_dragBar`) 오른쪽 끝에 `▼ 접기` 버튼. 접으면 이름 한 줄(높이 36px)만 남고 세션·선택지·타이핑 상태는 유지된다. 다시 누르면 펼쳐진다. MON01 을 조작하는 동안 접어 두는 용도다. 접힌 상태에서 MON01 슬롯이 바뀌면 자동으로 펼친다(선택지가 바뀌었으니).
- 상단의 `통화 종료 ✕`(`_hangUp`) 는 심문 중에는 숨긴다(일반 통화에서는 그대로). 콘솔 상단의 `통화 종료` 버튼은 제거하고 같은 자리에 `대화창 펼치기` 를 둔다 (`EndPressed` → `ExpandRequested` 로 이름 변경).
- `BlocksCrtInput()`: 대화창 사각형 위를 누를 때만 CRT 입력을 막는다(지금 심문 분기 동작 유지). 접힌 상태에서는 이름 줄 위만 막는다.

**MON01 (조사 자료, 1.5차)**
- 1차 커밋의 조사 목표 줄 · 상세칸 · 시작 탭 `CurrentEmployee` · 전원 토글 · ★ 은 유지한다.
- 카드를 **큼직하게**: 한 장 높이 44px 이상, 글자 `ViewFont.S(16)`, 한 화면 6~7장. 왼쪽에 태그를 아이콘 대신 **색 띠 + 두 글자**(`기록` 청록 / `CCTV` 흰색 / `증언` 노랑 / `진술` 직원 고유색 / `사고` 빨강 / `기분` 회색)로. 본문은 12자 안팎, 넘치면 `…`. 시각은 오른쪽 정렬.
- 카드를 누르면: 상세칸에 전문, 같은 시간대 카드(`SameWindowIds`) 테두리 밝게. 다시 누르면 슬롯에 담긴다(지금 토글 동작). 슬롯에 담기면 카드에 `A` / `B` 배지.
- 슬롯 A/B 는 하단 유지. `[이 자료로 질문] [두 자료 비교]` 버튼은 MON01 에 두지 않는다 — 대화창 선택지가 그 역할을 한다.
- 다른 직원의 카드는 전원 토글을 켰을 때만 보인다(회색으로 나열하지 않는다).

### 3-7. 직원별 띠 시간표 (2차)

시설 로그의 텍스트 나열이 헷갈린다는 의견에 대한 답이다. 추리 질문이 "그 시각 그 방에 누가 있었나"이므로, 그 답이 한눈에 보이는 형태로 그린다. **새 데이터 구조는 만들지 않는다** — `FacilityLogFormatter.Build()` 가 주는 `DisplayLogEntry` 의 최초 배치·이동 줄(`FromRoomId`/`ToRoomId`/`Timestamp`)만으로 그린다.

```
        22:00      23:00      00:00      01:00      02:00      03:00
여우   ▓▓정비실▓▓▓▓▓▓▓▓▓▓░░저장고░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
고양이 ▓▓발전실▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
늑대   ▓▓정비실▓▓▓▓▓▓▓▓▓●▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
                        ▲ 23:47 정비실 방해공작 (붉은 세로선)      ● = CCTV/증언/진술 핀
```

- 새 컨트롤 `scripts/view/StaffTimelineView.cs` 하나. 입력: `List<DisplayLogEntry>`, 직원 id 목록, 사고 목록, 핀 목록(`InterviewEvidence`, 선택). 출력: `_Draw()` 로 그린다(노드 수백 개 만들지 않는다).
- 가로축 = `DayLengthSeconds`(120초) 를 22:00~04:00 으로 환산(`SaboteurPlan.Clock()` 과 같은 식). 세로 = 활성 직원(격리·사망 포함, 흐리게).
- 띠 색 = 방. `RoomDef` 에 `[Export] Color MapColor` 를 추가하고 `data/rooms/*.tres` 5개에 값을 넣는다. 통로(빈 값)는 어두운 회색.
- 사고(`Severity == Critical/Sabotage`)는 **모든 띠를 관통하는 세로선**. 방해공작 빨강, 실제 고장 주황(시설 로그 팔레트 그대로). 위쪽 라벨에 `①  23:47 · 정비실 · 방해공작` + 방 색 네모. 방은 `InterviewEvidenceBoard.IncidentRoomOf()` 로 찾는다. 사고가 여럿이면 ①②③ 번호를 붙이고 띠 아래 한 줄 범례에 전문을, 라벨이 겹치면 위에는 번호만 남긴다. 세로선을 누르면 상세칸에 로그 원문 한 줄이 뜨고, MON01 에서는 그 `사고` 카드를 고른 것과 같게 동작한다. 경고·안정화·자재·순찰 줄은 **그리지 않는다** — 선이 서너 개를 넘지 않게.
- 핀: 심문 중 MON01 에서는 `InterviewSession.Board` 의 `Cctv/Testimony/OwnStatement/Mood(시각 없음 → 왼쪽 끝)` 카드를 그 직원 띠 위 해당 시각에 점으로 찍는다. 핀을 누르면 그 카드가 선택된다(카드 목록의 같은 카드를 누른 것과 동일).
- 두 곳에 쓴다:
  1. 근무 중 `L 로그` 창(`Day1HistoryOverlay.BuildLogPanel`) 상단에 띠를 두고, 아래 텍스트 목록은 그대로 둔다. 띠의 구간을 누르면 텍스트 목록이 그 시각으로 스크롤한다.
  2. 휴게시간 MON01 상단(조사 목표 줄 아래). 카드 목록은 띠 아래에 남는다. 세로 공간은 띠 6줄 × 18px ≈ 130px.
- 게임 판정에는 전혀 쓰지 않는다. 그리기 전용.

## 4. 파일별 작업 목록

### 1차 (10/15 마감 전 · 벽을 없애고 대화를 되돌린다)

| # | 파일 | 작업 |
|---|---|---|
| 1 | `scripts/dialogue/EvidenceContradiction.cs` | `ConfrontKind` 추가. `Check()` 를 Presence → Behavior → Location 순으로 검사하도록 재구성. 성립 안 하면 `Kind = None`, `Notice` 는 남기되 UI 가 거절에 쓰지 않는다. `Confront()` 문장 생성을 Kind 별로 분기. |
| 2 | `scripts/dialogue/PlayerKnownEvidence.cs` | `SightingStatement.Detail`, `CctvObservation.SuspiciousAction` 추가. `RecordSighting` / `RecordCctvObservation` 시그니처 확장(기본값으로 기존 호출 호환). |
| 3 | `scripts/dialogue/DialogueContext.cs`, `DialogueContextBuilder.cs` | `KnownSuspiciousDetail` 필드. `FindKnownSuspicious` 결과의 Description 에서 `" — "` 뒤를 잘라 채운다. |
| 4 | `scripts/dialogue/LocalDialogueGenerator.cs` | `RecordEvidence()`: SuspiciousSighting 에 detail 전달, IncidentDirect/Indirect 분기 추가(§3-4). |
| 5 | `scripts/dialogue/InterviewEvidence.cs`, `InterviewEvidenceBoard.cs` | `BehaviorDetail` 필드. `AddTestimonies` / `AddCctv` 에서 채우고 Body 에 반영(§3-3). |
| 6 | `scripts/facility/FacilitySimulation.cs` | `RecordCctvObservation` 호출 4곳에 `HasSuspiciousAction(room)` 전달. |
| 7 | `scripts/dialogue/InterviewSession.cs` | `Confront()` 가 `Kind == None` 이어도 턴을 만들도록 변경(`Confront.neutral`). `Confronted` 이벤트 시그니처 확장. |
| 8 | `scripts/dialogue/InterviewReplyPlanner.cs` | `ConfrontAnswer()` 에 `Kind` 분기: `None` → `neutral`, `Behavior` 이고 `DeniesEquipmentContact` → `deny`. |
| 9 | `data/dialogue/lines/*.txt`, `SLOTS.md` | `Confront.neutral` 슬롯을 6명 + common 에 추가(각 3줄). |
| 10 | `scripts/view/PhoneCallHud.cs` | 심문 선택지를 `_choices`/`_tail` 로 이동. `SubtitleHeight` 확대. 최초 진술을 순차 타이핑(`AfterMode.InterviewOpening` 추가). 카드 추가 연출 훅. MON01 의 ask/confront 버튼 제거에 맞춰 `AttachConsole()` 정리. |
| 11 | `scripts/view/RestInterviewConsole.cs` | Interview 페이지 제거, Notes 전체 화면, 조사 목표 줄, 상세칸, 시작 탭 `CurrentEmployee`, 전원 토글. |
| 12 | `scripts/debug/RestEvidenceTest.cs`, `scripts/dialogue/InterviewScenarioTest.cs` | §5 수용 기준의 케이스 추가. 기존 위치 모순 케이스는 그대로 통과해야 한다. |

### 1.5차 (10/15 마감 전 · 1차 UI 원복)

| # | 파일 | 작업 |
|---|---|---|
| 12a | `scripts/view/PhoneCallHud.cs` | `AfterMode.InterviewOpening` · `NextOpening()` · `_openingIndex` 제거. `SetInterviewLayout(true)` 를 일반 통화와 같은 레이아웃으로. `BuildInterviewMenu()` 를 §3-6 선택지 구성으로 재작성. `_history`(직전 문답) 추가. 접기 버튼 + `Collapsed` 상태. 심문 중 `_hangUp` 숨김. `BlocksCrtInput` 접힘 대응. |
| 12b | `scripts/view/RestInterviewConsole.cs` | `EndPressed` → `ExpandRequested`, 버튼 라벨 `대화창 펼치기`. 카드 크기·글자·태그 색 띠·A/B 배지(§3-6). 슬롯 변경 시 `SlotsChanged` 이벤트(대화창 자동 펼침용). |
| 12c | `scripts/debug/InterviewHudShot.cs` | 캡처 씬을 새 레이아웃에 맞춤. |
| 12d | `scripts/prologue/TutorialDirector.cs` | DAY0 교육이 자막 띠 위치·"진술 자동 재생"을 전제로 하는 단계가 있으면 선택지 방식에 맞게 문구·조건 수정. |

### 2차 (10/31 마감 전 · 결번자에게 거짓말을 주고, 띠 시간표를 넣고, 피드백을 닫는다)

| # | 파일 | 작업 |
|---|---|---|
| 13 | `scripts/dialogue/DialogueClaimState.cs`, `DialogueResponsePlanner.cs` | `DeniesEquipmentContact` 결정(§3-2). |
| 14 | `scripts/dialogue/KoreanDialogueComposer.cs` 또는 `InterviewReplyComposer.cs` | `Denial.equipment` 슬롯 덧붙이기. `PlayerKnownEvidence.RecordBehaviorClaim` 호출. |
| 15 | `data/dialogue/lines/*.txt`, `SLOTS.md` | `Denial.equipment` 슬롯(6명, 각 3줄). 결번자 전용이지만 성격은 유지. |
| 16 | `scripts/dialogue/InterviewEvidenceBoard.cs` | `AddOwnStatements` 에 행동 주장 카드(`Body = "본인 · 설비 쪽에 안 갔다"`, `BehaviorDetail` 채움). |
| 17 | `scripts/data/RoomDef.cs`, `data/rooms/*.tres` | `MapColor` 추가 · 5개 방 색 지정. |
| 18 | `scripts/view/StaffTimelineView.cs` (신규) | §3-7 띠 시간표 컨트롤. |
| 19 | `scripts/view/Day1HistoryOverlay.cs` | `L 로그` 창 상단에 띠, 구간 클릭 → 목록 스크롤. |
| 20 | `scripts/view/RestInterviewConsole.cs`, `PhoneCallHud.cs` | MON01 상단에 띠 + 핀. 핀 클릭 = 카드 선택. |
| 21 | `scripts/view/RestRosterView.cs` / `BreakRoomTopView` | `Confronted` 구독, 표식 누적(§3-5). 다음 날로 넘어가면 초기화. |
| 22 | `scripts/view/InterviewCCTVView.cs` | `Confronted` 의 Kind/variant 에 따라 긴장 발광 세기 차등(deny 최대). |

### 건드리지 않는 것

- `EventLog` 원본, `FacilityLogFormatter` 의 공개 범위(범인 이름 비공개 규칙).
- `SaboteurPlan` 의 행동 모델(제자리 범행 · 전조 3단계).
- `PlayerKnownEvidence` 의 유일한 규칙 — **플레이어가 보지 못한 사실은 카드가 되지 않는다.** 새 카드 종류도 전부 플레이어가 실제로 본/들은 것에서만 만든다.
- `FollowUpQuestionGenerator` 의 "추궁을 자동 생성하지 않는다" 규칙. 추궁은 여전히 플레이어가 카드 두 장을 고를 때만 만들어진다.
- DAY0 튜토리얼이 듣는 `InterviewSession.Asked` 이벤트 동작.

---

## 5. 수용 기준 (테스트로 확인)

`scripts/debug/RestEvidenceTest.cs` 또는 `InterviewScenarioTest.cs` 에 추가한다. `At(n)` 은 기존 헬퍼.

1. **Presence 성립**: 사고 기록(정비실, At(40)) + CCTV(cat, 정비실, At(38)) → `Kind == Presence`, `QuestionText` 에 "정비실" 과 시각 포함.
2. **Presence 불성립(다른 방)**: 사고 기록(정비실, At(40)) + CCTV(cat, 저장고, At(38)) → `Kind == None`, 그래도 `InterviewSession.Confront()` 가 빈 턴이 아닌 `neutral` 답변을 돌려준다.
3. **Behavior 성립**: `RecordSighting("wolf","cat","maintenance_room",At(30),"설비 쪽에 평소보다 오래 머물렀다")` + 사고 기록(정비실, At(40)) → `Kind == Behavior`, 질문문에 detail 포함.
4. **Testimony 카드 본문**: 3번 상황에서 cat 의 Board 에 `Body` 가 "늑대 · 설비 쪽에 평소보다 오래 머물렀다" 인 카드가 있다.
5. **최초 진술 카드화**: cat 이 `Anomaly` 에 `IncidentDirect` 로 답한 뒤 `PlayerKnownEvidence.StatementsBy("cat")` 에 시각이 붙은 진술이 1건 있다.
6. **결번자 거짓말(2차)**: 결번자 = cat, 사건 actor = cat, mode Omit → `DeniesEquipmentContact == true`, 두 번 물어도 값이 바뀌지 않는다. 결백한 cat 은 false.
7. **결백 인정(2차)**: 결백한 직원이 가짜 단서(`EmployeeBehaviorSystem`)의 주인일 때 Behavior 추궁 variant 는 `honest`.
8. **기존 위치 모순 케이스**: `RestEvidenceTest` 의 기존 케이스 전부 통과(`Kind == Location`).
9. **UI 스모크**: `scenes/debug/InterviewHudShot.tscn` 에서 심문을 열었을 때 MON01 에 진술 블록이 없고, 자막 띠에 선택지가 있으며, 시작 탭이 현재 직원이다.
10. **DAY0 튜토리얼**: `TutorialDirector` 의 "무엇이든 물었는가" 조건이 여전히 통과한다.
11. **자동 재생 없음(1.5차)**: 심문을 열고 인사가 끝난 뒤 아무 입력 없이 5초가 지나도 `_message` 내용이 바뀌지 않는다.
12. **접기(1.5차)**: 접은 뒤 `IsOpen == true`, `_session != null`. 펼치면 접기 전 선택지가 그대로 있다.
13. **종료 경로(1.5차)**: 세션을 `null` 로 만드는 경로는 `통화를 종료한다.` 선택지와 3D 전화기 클릭(`RequestClose`) 둘뿐이다. 콘솔에는 종료 버튼이 없다.
14. **띠 시간표(2차)**: 여우가 22:00 정비실 배치 → 23:10 저장고 이동이면 여우 띠는 [22:00,23:10) 정비실 색, [23:10,04:00) 저장고 색. 23:47 방해공작 붉은 선이 그 시각에 있다. 경고·안정화 줄은 띠에 나타나지 않는다.

---

## 6. 대사 슬롯 초안

`Confront.neutral` (성립하지 않는 조합 · 전원 공통 톤, 각 캐릭터 말투로 변형)
- 그 둘이 무슨 상관인지 모르겠는데요.
- …네? 둘 다 맞는 얘기 아닌가요?
- 뭘 물으시려는 건지 다시 말씀해 주세요.

`Denial.equipment` (결번자만 · 각 캐릭터 말투로)
- 설비 쪽엔 손도 안 댔어요.
- 저는 제 자리에서 제 일만 했습니다.
- 기계는 제 담당이 아니라서요.

`Confront.honest` 에 Behavior 용 변형 1줄씩 추가 권장
- 네, 수치가 이상해 보여서 잠깐 봤습니다. 그게 다예요.

---

## 7. 멘토 피드백과의 대응

| 피드백 (프로젝트 지식) | 이 문서에서 |
|---|---|
| "인터뷰를 진행할 때 어떤 단서를 파악해야 하는지 궁금하다" | MON01 조사 목표 줄 (§3-6) |
| "단서를 정리할 수 있는 게 있어야 됨" | 조사 노트 단일화 · 시간축 · ★ (§3-6), 휴게실 표식 (§3-5) |
| "'그래서 범인이 누군데?' 짜증을 유발해야" | 표식은 누적되지만 정답은 안 알려줌 (§3-5) |
| "휴게시간 대화창 끄기가 어려움" | 선택지 맨 아래 `통화 종료` 고정 (§3-6) |
| "10% 독창성" | 규칙은 셋으로만 · 자유도는 거절을 없애는 것으로 (§3-1) |
