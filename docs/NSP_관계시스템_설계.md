# 야간근무지침(NSP) — 직원 관계 시스템 설계

버전 기준: `ver3(simple_version)` · 대상 로스터: 고양이·강아지·여우·토끼·양·늑대

---

## 1. 목표

직원 간 상호작용을 "플레이어↔직원 1:1 전화/심문"에서 벗어나게 한다. 직원들이 서로 얽히고, 사이가 나빠지면 **같은 방 근무를 거부**하며, 그 관계가 **배치·감시·심문 전부에 스며들게** 한다.

핵심 원칙은 세 가지다.

- **관찰-반응**: 플레이어는 직원을 조종하지 않는다. 관계는 플레이어가 *엿보고*(CCTV) *배치로 개입*하는 형태로만 드러난다. 관리자 판타지를 그대로 유지한다.
- **데이터 주도**: 관계값·임계치·증감량은 코드에 하드코딩하지 않고 `res://data/relationships/`의 리소스에 둔다. 기존 `EmployeeDef`/`MoodPoolDef`/`RoomDef` 규약과 동일하다.
- **단계적**: 정적 관계값 + 배치 거부(가장 체감 큼)부터. 동적 변화·생성형 대화는 이후 확장.

---

## 2. 관계 모델

### 2.1 방향성 감정 (Affinity)

관계는 **방향**을 가진다. `from → to`가 느끼는 감정을 -100~+100의 정수(Affinity)로 저장한다. 대칭 관계도 두 방향을 각각 적는다. 방향을 나누면 짝사랑, 편향된 증언, "무서워하면서 좋아함" 같은 비대칭을 표현할 수 있다.

| 값 | 의미 |
|---|---|
| +100 | 연인·헌신 |
| +70 이상 | 밀접(친밀·강한 신뢰) |
| +30 ~ +69 | 우호 |
| -29 ~ +29 | 중립 |
| -30 ~ -69 | 불편·경계 |
| -70 이하 | 극혐 |

### 2.2 관계 타입 (RelationType)

배치 규칙보다는 **대사·연출의 색**을 정하는 태그.

`Neutral, Friend, Trust, Care, Crush, MutualCrush, Lover, Wary, Friction, Hatred`

### 2.3 플래그

특수 규칙(주로 증언 편향)용.

- `fear` — 대상을 무서워함. 그 대상에 대한 보고를 꺼린다. (양→늑대)
- `admire` — 대상을 동경. 대사 색용. (토끼→여우)
- `looksdown` — 대상을 깔봄. 증언을 진지하게 하지 않는다. (여우→고양이)

### 2.4 파생 값 — PairScore & Band

배치·페널티 판정은 **두 방향의 평균(PairScore)**으로 본다.

```
PairScore(a,b) = (Affinity(a→b) + Affinity(b→a)) / 2
```

| Band | 조건(PairScore) | 배치 결과 |
|---|---|---|
| **Refuse** | ≤ -70 | 동실 **거부**(하드 차단) |
| **Uneasy** | -69 ~ -30 | 불편 — 배치 가능하나 **소프트 페널티** |
| **Neutral** | -29 ~ +29 | 영향 없음 |
| **Friendly** | +30 ~ +69 | 우호 — 소폭 보너스 |
| **Close** | ≥ +70 | 밀접 — 보너스, 단 한쪽 위험 시 다른쪽 동요↑ |

임계치는 전부 데이터(`RelationshipTableDef`)에서 조정한다.

---

## 3. 초기 관계 시드 (30 방향 / 15쌍)

`[확정]` = 기획 확정 · `[설계]` = 성격·대사 근거 제안.

| from → to | Affinity | Type | Flags | 근거 |
|---|---|---|---|---|
| cat → dog | +90 | Lover | | [확정] 연인 |
| dog → cat | +95 | Lover | | [확정] 연인 |
| sheep → wolf | +70 | MutualCrush | fear | [확정] 무서워하면서 좋아함 |
| wolf → sheep | +65 | MutualCrush | | [확정] 티 안 내는 호감·보호 |
| rabbit → fox | +80 | Crush | admire | [확정] 짝사랑 |
| fox → rabbit | +35 | Friend | | [확정] 친구(감정은 옅음) |
| cat → fox | -90 | Hatred | | [확정] 극혐 |
| fox → cat | -60 | Hatred | looksdown | [확정] 깔봄 |
| cat → rabbit | +45 | Care | | [설계] 츤데레 챙김(대사 근거) |
| rabbit → cat | +25 | Friend | | [설계] |
| cat → sheep | -35 | Friction | | [설계] 답답해함 |
| sheep → cat | -40 | Wary | | [설계] 까칠해서 눈치 봄 |
| cat → wolf | +40 | Trust | | [설계] 실무 궁합 |
| wolf → cat | +40 | Trust | | [설계] |
| dog → fox | +35 | Friend | | [설계] 순진한 호의 |
| fox → dog | -15 | Wary | looksdown | [설계] 다루기 쉬운 상대로 봄 |
| dog → rabbit | +55 | Friend | | [설계] 밝은 콤비 |
| rabbit → dog | +55 | Friend | | [설계] |
| dog → sheep | +60 | Care | | [설계] 챙김(대사 근거) |
| sheep → dog | +55 | Friend | | [설계] |
| dog → wolf | +50 | Trust | | [설계] 든든한 동료 |
| wolf → dog | +50 | Trust | | [설계] |
| fox → sheep | -35 | Wary | | [설계] 껄끄러움(간파 위험) |
| sheep → fox | -30 | Wary | | [설계] 속을 못 읽어 불안 |
| fox → wolf | -40 | Wary | | [설계] 부담스러움 |
| wolf → fox | -45 | Friction | | [설계] 못 미더워함 |
| rabbit → sheep | +45 | Friend | | [설계] 보완 |
| sheep → rabbit | +40 | Friend | | [설계] |
| rabbit → wolf | +45 | Trust | | [설계] 든든해함 |
| wolf → rabbit | +40 | Care | | [설계] 제동·보호 |

### 3.1 밴드 요약 (배치에 미치는 영향)

- **Refuse (동실 거부) — 1쌍**: 고양이–여우 (score -75). 1일차부터 같은 방 불가.
- **Uneasy (소프트 페널티) — 3쌍**: 고양이–양(-37), 여우–양(-32), 여우–늑대(-42).
- **나머지**: 중립~밀접. 배치 제약 없음.

> 두 기술3 인력(고양이·여우)이 극혐이라, 코어실(`RepairMinWorkers = 2`)·발전실의 최적 조합을 짤 때 제약이 생긴다. 여우의 부정 관계가 대부분(고양이 극혐 + 양·늑대 긴장)이라, 방해자로 걸렸을 때 사회적으로 자연스럽게 고립된다. 강아지–여우는 배치상 중립이지만 서사상 "여우가 강아지를 이용하기 쉬운" 위험 관계이므로, 배치 제약이 아니라 증언·이벤트에서 다룬다.

---

## 4. 이벤트 → 관계 증감 (Phase 3, 동적 변화)

정적 시드로 시작하고, 이후 아래 이벤트를 **기존 이벤트/기분 파이프라인**에 얹어 런타임에 관계값을 움직인다. 수치는 데이터에서 튜닝.

| 이벤트 | 영향 방향 | 기본 Δ | 성격 보정 |
|---|---|---|---|
| 같은 방에서 사고를 함께 겪고 버팀 | 양방향 | +6 | 담력 높으면 유대↑ / 겁 많은 양은 보호자에게 +↑ |
| 싫어하는 상대와 강제 동실(Uneasy) 유지 | 양방향 | -3 / 틱 | 고양이는 감소 빠름 |
| 수상한 행동 목격 | 관찰자→행위자 | -12 | 관찰 스탯 높을수록 확신↑ |
| 위험 작업을 대신 맡아줌 | 수혜자→보호자 | +10 | 늑대→양 라인 강화 |
| 관리자가 A를 남들 앞에서 질책 | 목격자→A | -4 | |
| 말다툼 이벤트 발생 | 양방향 | -8 | 여우는 겉으론 -작게(은폐) |
| 방해자의 이간질 성공 | 지정 쌍 | -15 | |

---

## 5. 추리 통합 — 증언 편향 (Phase 3)

관계값이 심문/보고의 **신뢰도에 색을 입힌다**. `RelationshipSystem.ReportBias(observer, about)`가 -1.0(적극 지목)~+1.0(적극 은폐)을 돌려주고, `InterviewSession`/`DialogueComposer`가 이를 참고한다.

| observer→about | 증언 성향 |
|---|---|
| ≥ +70 | 목격 사실 축소·알리바이 제공(연인·헌신) |
| +30 ~ +69 | 애매하게 감쌈 |
| -30 ~ -69 | 의심을 강조 |
| ≤ -70 | 적극 지목(단, 과장이라 신뢰도 낮음) |
| `fear` | 대상 보고를 지연·회피 (양→늑대) |
| `looksdown` | 대상 증언을 진지하게 안 함 (여우→고양이) |

예: 늑대가 방해자여도 양은 잘 신고하지 못한다. 연인인 고양이·강아지는 서로를 감싸는 증언을 한다. 이것이 그대로 추리 난도의 축이 된다.

---

## 6. CCTV 2인 대사 트리거 (Phase 2)

같은 방 2인의 대사를 `(Band, 상황)`으로 생성해 `CctvView`로 엿듣게 한다.

| Band | 평상 | 사고 직후 | 방해 정황 |
|---|---|---|---|
| Close / Friendly | 잡담·농담 | 서로 안부·안심 | "쟤 좀 이상하지 않았어?" 공유 |
| Neutral | 업무 대화 | 짧은 확인 | 무관심 |
| Uneasy | 가시 돋친 말 | 책임 전가 | 서로 의심 |
| Refuse | (동실 불가) | — | — |

MVP는 밴드×상황별 **미리 쓴 짧은 대사** 몇 개. 확장은 `DialogueComposer`를 2인 발화(화자 슬롯 추가)로.

---

## 7. 데이터 스키마

동봉 파일: `RelationshipDef.cs`, `RelationshipSystem.cs`, `relationships.tres`.

- **`RelationshipDef.cs`** (`NSP.Data`) — `RelationType` enum + `RelationshipTableDef : Resource`. 시드를 `"from,to,affinity,type[,flags]"` 문자열 배열로 담아 손으로 쉽게 편집. 밴드 임계치도 여기 export.
- **`relationships.tres`** (`res://data/relationships/`) — 위 3장 시드가 채워진 실제 데이터.
- **`RelationshipSystem.cs`** (`NSP.Facility`) — `RoomStaffing`과 같은 **정적 창구**. `Affinity / PairScore / Band / CanCoAssign / CanCoAssignAll / Apply / ReportBias` 제공. `Load()`로 시드를 파싱한다.

> `[Export] Array<string> Seed` 방식은 `.tres`를 손으로 안전하게 편집하기 위한 선택이다. 나중에 구조화된 서브리소스로 마이그레이션해도 `RelationshipSystem` 인터페이스는 그대로 유지된다.

---

## 8. 기존 코드 연결 지점

| 목적 | 파일 |
|---|---|
| 배치 UI(밴드 아이콘·거부·경고) | `scripts/ui/ScheduleScreen.cs` · `scenes/schedule/ScheduleScene.tscn` |
| 동실 페널티(효율·패닉·사보 확률) | `scripts/facility/RoomStaffing.cs` · `FacilitySimulation.cs` |
| 관계 드리프트(이벤트 소스) | `scripts/facility/DailyMoodSystem.cs` · `EmployeeState.cs` · `SaboteurPlan.cs` |
| 2인 대사 | `scripts/ui/CctvView.cs` · `scenes/facility/facility_cctv_world.tscn` · `scripts/dialogue/DialogueComposer.cs` |
| 증언 편향 | `scripts/dialogue/InterviewSession.cs` · `DialogueComposer.cs` |
| 데이터 로딩 규약 | `scripts/core/ResourceDir.cs` (`res://data/*` 스캔 → `GD.Load`) |
| 전역 튜너블(선택) | `data/config.tres` · `scripts/core/Config.cs` |

---

## 9. 단계별 로드맵

- **Phase 0 (지금 · UI만)**: 근무 배치 화면을 종이 → 2모니터 콘솔/홀로그램으로. 환경 설정 창을 종이 → 기존 로그·대화 기록과 같은 홀로그램 창으로. **로직·데이터 흐름은 그대로**, 표현만 교체.
- **Phase 1 (질문1 · 3층)**: `RelationshipSystem` + 시드 도입. 배치 화면에 **거부/경고 + 밴드 아이콘**, `RoomStaffing`/`FacilitySimulation`에 **Uneasy 소프트 페널티**.
- **Phase 2 (질문1 · 2층)**: `CctvView`에서 같은 방 2인 대사 엿듣기.
- **Phase 3 (질문1 · 1층)**: 이벤트 기반 동적 관계 변화 + 증언 편향.
