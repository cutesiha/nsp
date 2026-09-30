# 늑대 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번자 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/wolf.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금 말씀이십니까? 대기 중입니다. <sub>틀: status.idle + 조각[여는말]</sub>
- 비어 있습니다. 필요하면 부르십시오. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없습니다. 지시하십시오. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 비어 있습니다. 필요하면 부르십시오. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 업무가 없습니다. <sub>틀: status.idle</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 발전실 쪽에서 둔탁한 소리가 났습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 발전실 쪽에서 둔탁한 소리가 났습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 밤 10시 40분경 발전실 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 10시 40분경 발전실 쪽에서 충격음이 들렸습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 들은 것뿐입니다. 벽을 통해 진동이 왔습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 평소보다 바빴습니다. 발전실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 사고가 있었습니다. 발전실입니다. <sub>틀: status.busy(닫힘)</sub>
- 평소보다 바빴습니다. 발전실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 평소보다 바빴습니다. 발전실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 평소보다 바빴습니다. 발전실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실에서 사고가 있었습니다. 그 외에는 평소와 같았습니다. <sub>틀: status.busy(닫힘)</sub>
- 평소보다 바빴습니다. 발전실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 발전실에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 10시 40분경 발전실 쪽에서 충격음이 들렸습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 보지는 못했습니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 짚이는 사람 없습니다. <sub>틀: nosight</sub>
- 짚이는 사람 없습니다. 확인되지 않은 이름은 대지 않겠습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 짚이는 사람 없습니다. <sub>틀: nosight</sub>
- 제가 본 범위에서는 없습니다. <sub>틀: nosight</sub>
- 짚이는 사람 없습니다. <sub>틀: nosight</sub>
- 수상한 사람은 없었습니다. <sub>틀: nosight</sub>
- 제가 본 범위에서는 없습니다. <sub>틀: nosight</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>
- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 경비실입니다. 저 혼자 있었고, 자리를 비운 적 없습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였습니다. <sub>틀: Companion.alone</sub>
- 저뿐이었습니다. 그게 편합니다. <sub>틀: Companion.alone(닫힘)</sub>
- 경비실에는 저 혼자였습니다. <sub>틀: Companion.alone</sub>
- 그때는 혼자 있었습니다. <sub>틀: Companion.alone</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘)</sub>
- 경비실에는 저 혼자였습니다. <sub>틀: Companion.alone</sub>
- 경비실에는 저 혼자였습니다. <sub>틀: Companion.alone</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘)</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였습니다. <sub>틀: Companion.alone</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 경비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 변동 없습니다. 경비실이었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 변동 없습니다. 경비실이었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 바뀌지 않습니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 바뀌지 않습니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 이미 말씀드렸습니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 그대로입니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '무난함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '집중 잘됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '별생각 없음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 적은 그대로입니다. <sub>틀: MoodReason.plain</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 적은 그대로입니다. <sub>틀: MoodReason.plain</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 적은 그대로입니다. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>
- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 제가 아닙니다. 기록을 확인하십시오. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>
- 아닙니다. 그럴 이유가 없습니다. <sub>틀: deny(닫힘)</sub>
- 근거 없는 의심입니다. 저는 아닙니다. <sub>틀: deny(닫힘)</sub>
- 저는 제 일만 했습니다. <sub>틀: deny</sub>
- 저는 제 일만 했습니다. <sub>틀: deny</sub>
- 부정합니다. 제가 한 일이 아닙니다. <sub>틀: deny(닫힘)</sub>
- 부정합니다. 제가 한 일이 아닙니다. <sub>틀: deny(닫힘)</sub>
- 저는 제 일만 했습니다. <sub>틀: deny</sub>
- 제가 아닙니다. 기록을 확인하십시오. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 직접 보지는 못했습니다. 발전실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 발전실 쪽에서 둔탁한 소리가 났습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 들은 것뿐입니다. 벽을 통해 진동이 왔습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 비어 있습니다. 필요하면 부르십시오. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 업무가 없습니다. <sub>틀: status.idle</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 업무가 없습니다. <sub>틀: status.idle</sub>
- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 직접 보지는 못했습니다. 저장고 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 11시 10분경 저장고 쪽에서 충격음이 들렸습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 눈으로 본 것은 아닙니다. 저장고 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 11시 10분경 저장고 쪽에서 둔탁한 소리가 났습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 평소보다 바빴습니다. 저장고 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 저장고에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 저장고 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘)</sub>
- 사고가 있었습니다. 저장고입니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 저장고에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 저장고 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 저장고에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 저장고에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 저장고 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 저장고에서 사고가 있었습니다. 그 외에는 평소와 같았습니다. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 저장고 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 그쯤 저장고 쪽에서 둔탁한 소리가 났습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 보지는 못했습니다. 옆방입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 저장고 쪽에서 벽을 통해 진동이 왔습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 짚이는 사람 없습니다. <sub>틀: nosight</sub>
- 제가 본 범위에서는 없습니다. <sub>틀: nosight</sub>
- 수상한 사람은 없었습니다. 확인되지 않은 이름은 대지 않겠습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 제가 본 범위에서는 없습니다. <sub>틀: nosight</sub>
- 못 봤습니다. <sub>틀: nosight</sub>
- 짚이는 사람 없습니다. 추측으로 사람을 지목하진 않습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>
- 짚이는 사람 없습니다. 확인되지 않은 이름은 대지 않겠습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람은 없었습니다. <sub>틀: nosight</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 몰랐습니다. <sub>틀: IncidentKnown.none</sub>
- 발전실 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>
- 몰랐습니다. <sub>틀: IncidentKnown.none</sub>
- 발전실 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>
- 발전실 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>
- 들은 바 없습니다. <sub>틀: IncidentKnown.none</sub>
- 몰랐습니다. <sub>틀: IncidentKnown.none</sub>
- 발전실 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>
- 제 쪽에서는 아무 일도 없었습니다. <sub>틀: IncidentKnown.none</sub>
- 발전실 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 토끼 직원과 정비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 있었습니다. 토끼 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 있었습니다. 토끼 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 있었습니다. 토끼 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실입니다. 토끼 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘) + 기억[mem.called(잘림)]</sub>
- 토끼 직원과 정비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실입니다. 토끼 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 있었습니다. 토끼 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실입니다. 토끼 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실입니다. 토끼 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 토끼 직원입니다. 필요하면 본인에게 물어보십시오. <sub>틀: Companion.with(닫힘)</sub>
- 정비실에 토끼 직원이 있었습니다. 그 이상은 모릅니다. <sub>틀: Companion.with(닫힘) + 기억[mem.called(잘림)]</sub>
- 기억은 잘 안 나는데, 아마 토끼 직원과 같이 있었을 겁니다. <sub>틀: Companion.with</sub>
- 토끼 직원입니다. 필요하면 본인에게 물어보십시오. <sub>틀: Companion.with(닫힘) + 기억[mem.called(잘림)]</sub>
- 같이 있던 사람은 토끼 직원입니다. 통화 중에도 정비실을 떠나지 않았습니다. <sub>틀: Companion.with + 기억[mem.called]</sub>
- 토끼 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘) + 기억[mem.called(잘림)]</sub>
- 그때 정비실에는 토끼 직원이 있었습니다. <sub>틀: Companion.with + 기억[mem.called(잘림)]</sub>
- 정비실에 토끼 직원이 있었습니다. 그 이상은 모릅니다. <sub>틀: Companion.with(닫힘) + 기억[mem.called(잘림)]</sub>
- 기억은 잘 안 나는데, 아마 토끼 직원과 같이 있었을 겁니다. <sub>틀: Companion.with</sub>
- 확인해 보십시오. 토끼 직원이 같이 있었습니다. <sub>틀: Companion.with(닫힘) + 기억[mem.called(잘림)]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 정비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain + 기억[mem.called(잘림)]</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 정비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 정비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. 관리자님이 전화하셨을 때도 정비실에 있었습니다. <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 정비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain + 기억[mem.called(잘림)]</sub>
- 평소와 같았습니다. 통화 중에도 정비실을 떠나지 않았습니다. <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 정비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘) + 기억[mem.called(잘림)]</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 그대로입니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 바뀌지 않습니다. 정비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.called(잘림)]</sub>
- 이미 말씀드렸습니다. 정비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.called(잘림)]</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 정비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '평온함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '별생각 없음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '무난함'이라고 적으셨습니다. 이유가 무엇입니까?  

- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 그렇습니다. 근무 전부터입니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 제가 아닙니다. 기록을 확인하십시오. <sub>틀: deny(닫힘)</sub>
- 부정합니다. 제가 한 일이 아닙니다. <sub>틀: deny(닫힘)</sub>
- 제가 아닙니다. 기록을 확인하십시오. <sub>틀: deny(닫힘)</sub>
- 아닙니다. 그럴 이유가 없습니다. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>
- 아닙니다. <sub>틀: deny</sub>
- 부정합니다. 제가 한 일이 아닙니다. <sub>틀: deny(닫힘)</sub>
- 아닙니다. 그럴 이유가 없습니다. <sub>틀: deny(닫힘)</sub>
- 아닙니다. <sub>틀: deny</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 눈으로 본 것은 아닙니다. 저장고 쪽입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 저장고 쪽입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 저장고 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 저장고 쪽에서 둔탁한 소리가 났습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 저장고 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금은 할 업무가 없습니다. <sub>틀: status.idle</sub>
- 비어 있습니다. 필요하면 부르십시오. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었습니다. 배치를 바꾸셔도 됩니다. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없습니다. 지시하십시오. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 직접 보지는 못했습니다. 발전실 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 10시 40분경 발전실 쪽에서 둔탁한 소리가 났습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 보지는 못했습니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 기억[mem.dispatched(잘림)] + 조각[보정]</sub>
- 벽 너머였습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 직접 보지는 못했습니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.reported(잘림)] + 조각[보정]</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 사고가 있었습니다. 발전실입니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 발전실에서 사고가 났습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실에서 사고가 있었습니다. 그 외에는 평소와 같았습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 무난하지는 않았습니다. 발전실에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실에서 사고가 있었습니다. 그 외에는 평소와 같았습니다. <sub>틀: status.busy(닫힘)</sub>
- 발전실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실에서 사고가 있었습니다. 그 외에는 평소와 같았습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 무난하지는 않았습니다. 발전실에서 사고가 났습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 직접 보지는 못했습니다. 밤 10시 40분경 발전실 쪽입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 직접 보지는 못했습니다. 밤 10시 40분경 발전실 쪽입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 짚이는 사람 없습니다. <sub>틀: nosight</sub>
- 못 봤습니다. <sub>틀: nosight</sub>
- 수상한 사람 말씀이십니까? 제가 본 범위에서는 없습니다. <sub>틀: nosight + 조각[여는말]</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>
- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>
- 특이한 사람은 없었습니다. <sub>틀: nosight</sub>
- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>
- 짚이는 사람 없습니다. 확인되지 않은 이름은 대지 않겠습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 못 봤습니다. <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알고 있습니다. 벽 너머였습니다. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 소리만 들었습니다. 무엇인지는 모릅니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 들었습니다. 보지는 못했습니다. <sub>틀: IncidentKnown.indirect(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 경비실입니다. 저 혼자 있었고, 자리를 비운 적 없습니다. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 경비실입니다. 저 혼자 있었고, 자리를 비운 적 없습니다. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 경비실입니다. 저 혼자 있었고, 자리를 비운 적 없습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 경비실입니다. 저 혼자 있었고, 자리를 비운 적 없습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분경에는 경비실에 혼자 있었습니다. <sub>틀: WhereAtIncident.alone</sub>
- 경비실에 있었습니다. 혼자였습니다. <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였습니다. <sub>틀: Companion.alone</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었습니다. 발전실 확인 지시를 받고 갔습니다. <sub>틀: Companion.alone + 기억[mem.dispatched]</sub>
- 그때는 혼자 있었습니다. <sub>틀: Companion.alone</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘)</sub>
- 저뿐이었습니다. 그게 편합니다. <sub>틀: Companion.alone(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 아무도 없었습니다. <sub>틀: Companion.alone</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 단독이었습니다. 문제없습니다. <sub>틀: Companion.alone(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 아무도 없었습니다. 전화로 발전실에 가라는 지시를 받았습니다. <sub>틀: Companion.alone + 기억[mem.dispatched]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소와 같았습니다. 전화로 발전실에 가라는 지시를 받았습니다. <sub>틀: BeforeIncident.plain + 기억[mem.dispatched]</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. 전화로 발전실에 가라는 지시를 받았습니다. <sub>틀: BeforeIncident.plain + 기억[mem.dispatched]</sub>
- 평소와 같았습니다. 발전실 확인 지시를 받고 갔습니다. <sub>틀: BeforeIncident.plain + 기억[mem.dispatched]</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. 발전실 확인 지시를 받고 갔습니다. <sub>틀: BeforeIncident.plain + 기억[mem.dispatched]</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 경비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 이미 말씀드렸습니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 그대로입니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 이미 말씀드렸습니다. 경비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 바뀌지 않습니다. 경비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.dispatched(잘림)]</sub>
- 바뀌지 않습니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>

### 이동 · 이유

Q: 밤 10시 45분경 경비실에서 발전실로 이동한 이유는 무엇입니까?  

- 전화로 지시받았습니다. 발전실입니다. <sub>틀: MoveReason.dispatched(닫힘) + 기억[mem.worked.repair(잘림)]</sub>
- 가보라고 하셨습니다. 그래서 갔습니다. <sub>틀: MoveReason.dispatched(닫힘)</sub>
- 전화로 지시받았습니다. 발전실입니다. <sub>틀: MoveReason.dispatched(닫힘)</sub>
- 전화로 지시받았습니다. 발전실입니다. <sub>틀: MoveReason.dispatched(닫힘)</sub>
- 확인하라는 지시를 받고 발전실에 갔습니다. 고장을 처리하고 있었습니다. <sub>틀: MoveReason.dispatched + 기억[mem.worked.repair]</sub>
- 가보라고 하셨습니다. 그래서 갔습니다. <sub>틀: MoveReason.dispatched(닫힘) + 기억[mem.worked.repair(잘림)]</sub>
- 확인하라는 지시를 받고 발전실에 갔습니다. 수리 중이었습니다. <sub>틀: MoveReason.dispatched + 기억[mem.worked.repair]</sub>
- 가보라고 하셨습니다. 그래서 갔습니다. <sub>틀: MoveReason.dispatched(닫힘) + 기억[mem.worked.repair(잘림)]</sub>
- 전화로 지시받았습니다. 발전실입니다. <sub>틀: MoveReason.dispatched(닫힘)</sub>
- 확인하라는 지시를 받고 발전실에 갔습니다. 고장을 처리하고 있었습니다. <sub>틀: MoveReason.dispatched + 기억[mem.worked.repair]</sub>

### 이동 · 거기서 한 일

Q: 발전실에서는 무엇을 했습니까?  

- 수리했습니다. 수리는 그쯤 완료됐습니다. <sub>틀: ActionThere.repair + 기억[mem.repair]</sub>
- 발전실 수리를 완료했습니다. <sub>틀: ActionThere.repair + 기억[mem.heard(잘림)]</sub>
- 발전실 수리를 완료했습니다. <sub>틀: ActionThere.repair</sub>
- 수리했습니다. <sub>틀: ActionThere.repair</sub>
- 수리했습니다. 전화로 발전실에 가라는 지시를 받았습니다. <sub>틀: ActionThere.repair + 기억[mem.dispatched]</sub>
- 수리했습니다. <sub>틀: ActionThere.repair</sub>
- 수리했습니다. 전화로 발전실에 가라는 지시를 받았습니다. <sub>틀: ActionThere.repair + 기억[mem.dispatched]</sub>
- 수리했습니다. 발전실 확인 지시를 받고 갔습니다. <sub>틀: ActionThere.repair + 기억[mem.dispatched]</sub>
- 고장을 처리했습니다. <sub>틀: ActionThere.repair</sub>
- 발전실 수리를 완료했습니다. <sub>틀: ActionThere.repair</sub>

### 기분 · 이유

Q: 오늘 기분을 '별생각 없음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '무난함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평온함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평소와 비슷함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 출근 전부터였습니다. <sub>틀: MoodBefore.yes</sub>
- 그렇습니다. 근무 전부터입니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 기록만으로 판단하지 마십시오. 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 그 기록에는 이유가 있습니다. 제가 한 것은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 그 기록에는 이유가 있습니다. 제가 한 것은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 기록만으로 판단하지 마십시오. 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 그 기록에는 이유가 있습니다. 제가 한 것은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 기록이 그렇다면 확인하십시오. 제가 한 일은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 그 기록에는 이유가 있습니다. 제가 한 것은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 기록이 그렇다면 확인하십시오. 제가 한 일은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 그 기록에는 이유가 있습니다. 제가 한 것은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>
- 기록이 그렇다면 확인하십시오. 제가 한 일은 아닙니다. <sub>틀: deny.evidence(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 발전실 쪽입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 눈으로 본 것은 아닙니다. 밤 10시 40분경 발전실 쪽입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 밤 10시 40분경 발전실 쪽입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 들은 것뿐입니다. 벽을 통해 진동이 왔습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 발전실 쪽에서 둔탁한 소리가 났습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 발전실 쪽에서 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 벽을 통해 진동이 왔습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>

### 수신 전화 · 사고 신고

Q: (직원이 전화를 건다)  

- 발전실 쪽에서 충격음이 들렸습니다. 확인하러 가겠습니까? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽에서 벽을 통해 진동이 왔습니다. 확인하러 가겠습니까? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽입니다. 벽을 통해 진동이 왔습니다. 지시하십시오. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽입니다. 둔탁한 소리가 났습니다. 지시하십시오. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽에서 충격음이 들렸습니다. 확인하러 가겠습니까? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽입니다. 벽을 통해 진동이 왔습니다. 지시하십시오. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽에서 벽을 통해 진동이 왔습니다. 확인하러 가겠습니까? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽입니다. 둔탁한 소리가 났습니다. 지시하십시오. <sub>틀: call.prefix / report.direct · report.indirect</sub>

### 수신 전화 · 가라는 지시에

Q: 확인하러 가주세요.  

- 알겠습니다. 바로 갑니다. <sub>틀: accept</sub>
- 제가 맡겠습니다. <sub>틀: accept</sub>
- 제가 맡겠습니다. <sub>틀: accept</sub>
- 가겠습니다. <sub>틀: accept</sub>
- 가겠습니다. <sub>틀: accept</sub>
- 제가 맡겠습니다. <sub>틀: accept</sub>
- 가겠습니다. <sub>틀: accept</sub>
- 제가 맡겠습니다. <sub>틀: accept</sub>
- 가겠습니다. <sub>틀: accept</sub>
- 제가 맡겠습니다. <sub>틀: accept</sub>

### 수신 전화 · 대기 지시에

Q: 지금 자리에서 대기하세요.  

- 자리를 지키겠습니다. <sub>틀: decline</sub>
- 자리를 지키겠습니다. <sub>틀: decline</sub>
- 자리를 지키겠습니다. <sub>틀: decline</sub>
- 그렇게 하겠습니다. <sub>틀: decline</sub>
- 그렇게 하겠습니다. <sub>틀: decline</sub>
- 자리를 지키겠습니다. <sub>틀: decline</sub>
- 자리를 지키겠습니다. <sub>틀: decline</sub>
- 알겠습니다. 대기하겠습니다. <sub>틀: decline</sub>
- 알겠습니다. 대기하겠습니다. <sub>틀: decline</sub>
- 그렇게 하겠습니다. <sub>틀: decline</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없습니다. 지시하십시오. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 쉬고 있지는 않습니다. 지시를 기다리는 중입니다. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 업무가 없습니다. <sub>틀: status.idle</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>
- 비어 있습니다. 필요하면 부르십시오. <sub>틀: status.idle(닫힘)</sub>
- 대기 중입니다. <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 10시 10분경 코어실 쪽에서 벽을 통해 진동이 왔습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 밤 10시 10분경 코어실 쪽에서 충격음이 들렸습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었습니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 코어실 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 코어실 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>
- 사고가 있었습니다. 코어실입니다. <sub>틀: status.busy(닫힘)</sub>
- 평소보다 바빴습니다. 코어실 건 때문입니다. <sub>틀: status.busy(닫힘)</sub>
- 사고가 있었습니다. 코어실입니다. <sub>틀: status.busy(닫힘)</sub>
- 무난하지는 않았습니다. 코어실에서 사고가 났습니다. <sub>틀: status.busy(닫힘)</sub>
- 코어실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘)</sub>
- 코어실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘)</sub>
- 코어실 쪽 때문에 바빴습니다. 문제는 없었습니다. <sub>틀: status.busy(닫힘)</sub>
- 사고가 있었습니다. 코어실입니다. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 직접 보지는 못했습니다. 코어실 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 코어실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 코어실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 코어실 쪽에서 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 눈으로 본 것은 아닙니다. 옆방입니다. 충격음이 들렸습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 코어실 쪽에서 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 코어실 쪽에서 벽을 통해 진동이 왔습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 보지는 못했습니다. 밤 10시 10분경 코어실 쪽입니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보지는 못했습니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>
- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>
- 제가 본 범위에서는 없습니다. <sub>틀: nosight</sub>
- 못 봤습니다. <sub>틀: nosight</sub>
- 제가 본 범위에서는 없습니다. 확인되지 않은 이름은 대지 않겠습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 특이한 사람은 없었습니다. <sub>틀: nosight</sub>
- 특이한 사람은 없었습니다. 추측으로 사람을 지목하진 않습니다. <sub>틀: nosight + 조각[덧붙임]</sub>
- 특이한 사람은 없었습니다. <sub>틀: nosight</sub>
- 수상한 사람은 없었습니다. <sub>틀: nosight</sub>
- 없습니다. 남의 동선까지 보고 있진 않았습니다. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 제 쪽에서는 아무 일도 없었습니다. <sub>틀: IncidentKnown.none</sub>
- 몰랐습니다. <sub>틀: IncidentKnown.none</sub>
- 저장고 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>
- 제 쪽에서는 아무 일도 없었습니다. <sub>틀: IncidentKnown.none</sub>
- 전혀 몰랐습니다. 제 위치에서는 알 수 없었습니다. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣습니다. <sub>틀: IncidentKnown.none</sub>
- 몰랐습니다. <sub>틀: IncidentKnown.none</sub>
- 처음 듣습니다. <sub>틀: IncidentKnown.none</sub>
- 들은 바 없습니다. <sub>틀: IncidentKnown.none</sub>
- 저장고 건은 모릅니다. <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 경비실입니다. 강아지 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실에 있었습니다. 강아지 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 강아지 직원과 경비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실입니다. 강아지 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실입니다. 강아지 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실입니다. 강아지 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실에 있었습니다. 강아지 직원도 같이 있었습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 강아지 직원과 경비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실입니다. 강아지 직원이 확인해 줄 겁니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 강아지 직원과 경비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 같이 있던 사람은 강아지 직원입니다. <sub>틀: Companion.with</sub>
- 강아지 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘)</sub>
- 강아지 직원입니다. 필요하면 본인에게 물어보십시오. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 사람은 강아지 직원입니다. <sub>틀: Companion.with</sub>
- 그때 경비실에는 강아지 직원이 있었습니다. <sub>틀: Companion.with</sub>
- 강아지 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘)</sub>
- 강아지 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 사람은 강아지 직원입니다. <sub>틀: Companion.with</sub>
- 강아지 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘)</sub>
- 기억은 잘 안 나는데, 아마 강아지 직원과 같이 있었을 겁니다. <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 특별한 것은 없었습니다. <sub>틀: BeforeIncident.plain</sub>
- 평소와 같았습니다. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 10분경 경비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 이미 말씀드렸습니다. 경비실입니다. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로입니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>
- 그대로입니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 같습니다. 경비실에 있었습니다. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 변동 없습니다. 경비실이었습니다. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 변동 없습니다. 경비실이었습니다. <sub>틀: Restate.same(닫힘)</sub>
- 다시 말씀드립니다. 경비실입니다. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '평소와 비슷함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '별생각 없음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '집중 잘됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평온함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '무난함'이라고 적으셨습니다. 이유가 무엇입니까?  

- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없습니다. <sub>틀: MoodReason.plain</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 업무가 문제없이 끝났습니다. <sub>틀: MoodReason.calm</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>
- 적은 그대로입니다. <sub>틀: MoodReason.plain</sub>
- 별일 없었습니다. <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그렇습니다. 근무 전부터입니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그렇습니다. <sub>틀: MoodBefore.yes</sub>
- 맞습니다. 업무와는 무관합니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그렇습니다. 근무 전부터입니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 예. 여기 오기 전부터였습니다. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그렇습니다. 근무 전부터입니다. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 아닙니다. 그럴 이유가 없습니다. <sub>틀: deny(닫힘)</sub>
- 근거 없는 의심입니다. 저는 아닙니다. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>
- 저는 제 일만 했습니다. <sub>틀: deny</sub>
- 제가 아닙니다. 기록을 확인하십시오. <sub>틀: deny(닫힘)</sub>
- 아닙니다. <sub>틀: deny</sub>
- 근거 없는 의심입니다. 저는 아닙니다. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>
- 근거 없는 의심입니다. 저는 아닙니다. <sub>틀: deny(닫힘)</sub>
- 확인해 보십시오. 결과는 같을 겁니다. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었습니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 코어실 쪽에서 충격음이 들렸습니다. 눈으로 본 것은 아닙니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 밤 10시 10분경 코어실 쪽에서 둔탁한 소리가 났습니다. 직접 보지는 못했습니다. <sub>틀: incident.indirect + 조각[보정]</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였습니다. 벽을 통해 진동이 왔습니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 충격음이 들렸습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 들은 것뿐입니다. 둔탁한 소리가 났습니다. 원인은 모릅니다. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보지는 못했습니다. 옆방입니다. 둔탁한 소리가 났습니다. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

## 결번자 장면 — 늑대

- 늑대는 경비실 배치 → 22:30 몰래 저장고 → 22:33 방해공작 → 22:38 복귀 → 22:42 저장고 고장.
- 결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 저장고 건이 있었습니다. 처리했습니다. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 없었습니다. <sub>틀: noanomaly</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 본 게 없습니다. 추측으로는 말하지 않겠습니다. <sub>틀: nosight(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 33분경 당신은 어디에 있었습니까?  

- 강아지 직원과 경비실에 있었습니다. 그 이상은 없습니다. <sub>틀: WhereAtIncident.with(닫힘) + 기억[slip.assert(잘림)]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 경비실에서 업무 중이었습니다. <sub>틀: BeforeIncident.plain</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 33분경 함께 있던 직원이 있었습니까?  

- 강아지 직원이 있었습니다. 딱히 신경 쓰진 않았습니다. <sub>틀: Companion.with(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 33분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 들은 바 없습니다. <sub>틀: IncidentKnown.none</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 근거 없는 의심입니다. 저는 아닙니다. <sub>틀: deny(닫힘)</sub>

## 결번자 장면 — 강아지

- 강아지는 경비실 배치 → 22:30 몰래 저장고 → 22:33 방해공작 → 22:38 복귀 → 22:42 저장고 고장.
- 결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.

### 결번자(강아지)와 같은 배치 · 같이 있던 사람

Q: 밤 10시 33분경 함께 있던 직원이 있었습니까?  

- 경비실에는 저 혼자였습니다. <sub>틀: Companion.alone</sub>

