# 고양이 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번자 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/cat.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 놀고 있어요. 제 탓은 아니고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 봤어요. 저장고에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서요. 덕분에 일 다 밀렸어요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 그때 저장고에서 기계가 멈추는 거 봤는데요. <sub>틀: incident.direct</sub>
- 하필 제 앞에서요. 저장고에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 그때 저장고에서요. 덕분에 일 다 밀렸어요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 이상현상이요? 저장고에서 기계가 멈추는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 바빴어요. 저장고 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 별로였어요. 저장고에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 저장고 쪽 일만 아니었으면 괜찮았어요. <sub>틀: status.busy</sub>
- 정신없었죠. 저장고에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 피곤했어요. 저장고 건으로 하루가 다 갔는데요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었죠. 저장고에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었죠. 저장고에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 좋은 상황은 아니었어요. 그쯤 저장고에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 저장고에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 저장고에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 저장고에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수상한 사람이요? 못 봤는데요. <sub>틀: nosight + 조각[여는말]</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 또 수상한 사람이요? 제가 본 데선 없었어요. <sub>틀: nosight + 조각[여는말]</sub>
- 아뇨. 짚이는 사람 없는데요. <sub>틀: nosight(닫힘)</sub>
- 아뇨. 짚이는 사람 없는데요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 못 봤는데요. <sub>틀: nosight</sub>
- 수상한 사람이요? 제가 본 데선 없었어요. <sub>틀: nosight + 조각[여는말]</sub>
- 수상한 사람이요? 못 봤는데요. <sub>틀: nosight + 조각[여는말]</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 처음 듣는데요. <sub>틀: IncidentKnown.none</sub>
- 발전실이요? 전혀요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실이요? 전혀요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실이요? 전혀요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐는데요. <sub>틀: IncidentKnown.none</sub>
- 들은 적 없어요. 저한텐 아무도 말 안 해줬거든요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모르는 일인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 제 쪽에선 아무 일도 없었어요. <sub>틀: IncidentKnown.none</sub>
- 몰랐는데요. <sub>틀: IncidentKnown.none</sub>
- 발전실이요? 전혀요. <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저장고요. 저 혼자였고요, 그게 다예요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자 있었어요. 증인 같은 건 없는데요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에 혼자 있었는데요. 로그 보세요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고요. 저 혼자였고요, 그게 다예요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자 있었어요. 증인 같은 건 없는데요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에 혼자 있었는데요. 로그 보세요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고요. 저 혼자였고요, 그게 다예요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자 있었어요. 증인 같은 건 없는데요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자 있었어요. 증인 같은 건 없는데요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자 있었어요. 증인 같은 건 없는데요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 없었어요. 저 혼자요. <sub>틀: Companion.alone(닫힘)</sub>
- 그때 저장고엔 저 하나였어요. <sub>틀: Companion.alone</sub>
- 그때 저장고엔 저 하나였어요. <sub>틀: Companion.alone</sub>
- 저장고엔 저 혼자였어요. 로그 보세요. <sub>틀: Companion.alone(닫힘)</sub>
- 저장고엔 저 혼자였어요. 로그 보세요. <sub>틀: Companion.alone(닫힘)</sub>
- 그때 저장고엔 저 하나였어요. <sub>틀: Companion.alone</sub>
- 없었어요. 저 혼자요. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요. 혼자 하는 게 편하거든요. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였는데요. <sub>틀: Companion.alone</sub>
- 혼자였는데요. <sub>틀: Companion.alone</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요. 저장고에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 저장고에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 안 바뀌어요. 저장고였어요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요. 저장고였어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 저장고요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 저장고요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 저장고에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요. 저장고였어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 저장고요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 저장고였어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 저장고였어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 저장고에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '조금 예민함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '귀찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평범함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '그냥 그럼'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 저장고 일 때문이죠. 일이 다 밀렸잖아요. <sub>틀: MoodReason.incident(닫힘)</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 저장고 일 때문이죠. 일이 다 밀렸잖아요. <sub>틀: MoodReason.incident(닫힘)</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 막힌 게 없었어요. 그래서요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 처음엔 괜찮았는데요. 중간부터요. <sub>틀: MoodBefore.no(닫힘)</sub>
- 출근 전부터였어요. 여기 일이랑은 상관없고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처음엔 괜찮았는데요. 중간부터요. <sub>틀: MoodBefore.no(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제가 그럴 이유가 있어요? <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제가 그럴 이유가 있어요? <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 아까 말했잖아요. 저장고에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 저장고에서 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 놀고 있어요. 제 탓은 아니고요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 비었는데요. 일 있으면 주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 비었는데요. 일 있으면 주세요. <sub>틀: status.idle(닫힘)</sub>
- 비었는데요. 일 있으면 주세요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 밤 10시 40분쯤 발전실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 좋은 상황은 아니었어요. 발전실에서 기계가 멈추는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 봤어요. 발전실에서요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 바빴어요. 발전실 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 별로였어요. 발전실에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 때문에 일이 다 밀렸는데요. <sub>틀: status.busy</sub>
- 발전실 쪽 일만 아니었으면 괜찮았어요. <sub>틀: status.busy</sub>
- 발전실 때문에 일이 다 밀렸는데요. <sub>틀: status.busy</sub>
- 정신없었죠. 발전실에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 별로였어요. 발전실에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 피곤했어요. 발전실 건으로 하루가 다 갔는데요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 발전실 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 발전실 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 하필 제 앞에서요. 발전실에서 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 이상현상이요? 발전실에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 정상은 아니었죠. 발전실에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 하필 제 앞에서요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 제가 본 데선 없었어요. 괜히 이름 대고 싶진 않아요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 아뇨. 짚이는 사람 없는데요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 못 봤는데요. 괜히 이름 대고 싶진 않아요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 못 봤는데요. 아무나 찍으면 일만 꼬여요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 못 봤는데요. 괜히 이름 대고 싶진 않아요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 아뇨. 짚이는 사람 없는데요. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 네, 봤어요. <sub>틀: IncidentKnown.direct</sub>
- 눈앞에서 났는데 모를 리가요. <sub>틀: IncidentKnown.direct</sub>
- 네, 봤어요. <sub>틀: IncidentKnown.direct</sub>
- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알죠. 제가 거기 있었는데요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 네, 봤어요. <sub>틀: IncidentKnown.direct</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 발전실에 강아지 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실이요. 강아지 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실에 강아지 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실이요. 강아지 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실이요. 강아지 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실이요. 강아지 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 강아지 씨랑 발전실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실이요. 강아지 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실에 강아지 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 강아지 씨랑 발전실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 그걸 왜 물어보시는데요? 강아지 씨요. 로그 보세요. <sub>틀: Companion.with(닫힘)</sub>
- 강아지 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 왜요, 강아지 씨 있었는데요. <sub>틀: Companion.with</sub>
- 그걸 왜 물어보시는데요? 강아지 씨요. 로그 보세요. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 건 강아지 씨예요. <sub>틀: Companion.with</sub>
- 강아지 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 강아지 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 왜요, 강아지 씨 있었는데요. <sub>틀: Companion.with</sub>
- 같이 있던 건 강아지 씨예요. <sub>틀: Companion.with</sub>
- 같이 있던 건 강아지 씨예요. <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 발전실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 똑같다니까요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같다니까요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 발전실이요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 발전실이요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 발전실이요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같다니까요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 발전실이요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '귀찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평범함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '그냥 그럼'이라고 적으셨습니다. 이유가 무엇입니까?  

- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 일이 잘 풀렸으니까요. 그게 다예요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 일이 잘 풀렸으니까요. 그게 다예요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 일이 잘 풀렸으니까요. 그게 다예요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무랑은 관계없어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무랑은 관계없어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였어요. 여기 일이랑은 상관없고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무랑은 관계없어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 저요? 저 아니에요. <sub>틀: deny + 조각[여는말]</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 저 아니에요. <sub>틀: deny</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 저요? 저 아니에요. <sub>틀: deny + 조각[여는말]</sub>
- 또 저요? 저 아니에요. <sub>틀: deny + 조각[여는말]</sub>
- 저 아니에요. <sub>틀: deny</sub>
- 또 저요? 저 아니에요. <sub>틀: deny + 조각[여는말]</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실이요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 발전실에서 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실이요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 몇 번을 물어보세요, 발전실에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>
- 발전실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서요. 덕분에 일 다 밀렸어요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 비었는데요. 일 있으면 주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 봤어요. 정비실에서요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 봤어요. 정비실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 봤어요. 정비실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 봤어요. 정비실에서요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 밤 11시 10분쯤 정비실이요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 피곤했어요. 정비실 건으로 하루가 다 갔는데요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었죠. 정비실에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 정비실 때문에 일이 다 밀렸는데요. <sub>틀: status.busy</sub>
- 별로였어요. 정비실에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 정비실 쪽이 계속 말썽이었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 피곤했어요. 정비실 건으로 하루가 다 갔는데요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 하필 제 앞에서요. 정비실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 조금 전에 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 하필 제 앞에서요. 정비실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 정비실에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 그때 정비실에서 설비가 서는 거 봤는데요. <sub>틀: incident.direct</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 못 봤는데요. 아무나 찍으면 일만 꼬여요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 제가 본 데선 없었어요. <sub>틀: nosight</sub>
- 또 수상한 사람이요? 제가 본 데선 없었어요. <sub>틀: nosight + 조각[여는말]</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 아뇨. 짚이는 사람 없는데요. <sub>틀: nosight(닫힘)</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요? 못 봤는데요. <sub>틀: nosight + 조각[여는말]</sub>
- 못 봤는데요. 아무나 찍으면 일만 꼬여요. <sub>틀: nosight + 조각[덧붙임]</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 몰랐는데요. <sub>틀: IncidentKnown.none</sub>
- 처음 듣는데요. <sub>틀: IncidentKnown.none</sub>
- 들은 적 없어요. 저한텐 아무도 말 안 해줬거든요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요. <sub>틀: IncidentKnown.none</sub>
- 들은 적 없어요. 저한텐 아무도 말 안 해줬거든요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 들은 적 없어요. 저한텐 아무도 말 안 해줬거든요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 제 쪽에선 아무 일도 없었어요. <sub>틀: IncidentKnown.none</sub>
- 제 쪽에선 아무 일도 없었어요. <sub>틀: IncidentKnown.none</sub>
- 들은 적 없어요. 저한텐 아무도 말 안 해줬거든요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모르는 일인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 왜요, 토끼 씨 있었는데요. <sub>틀: Companion.with</sub>
- 토끼 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 로그 보면 나오잖아요. 정비실에 토끼 씨 있었어요. <sub>틀: Companion.with(닫힘)</sub>
- 토끼 씨랑 있었는데요. 그게 왜요? <sub>틀: Companion.with(닫힘)</sub>
- 토끼 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 건 토끼 씨예요. 시끄러워서 집중이 안 됐는데요. <sub>틀: Companion.with + 조각[인상]</sub>
- 로그 보면 나오잖아요. 정비실에 토끼 씨 있었어요. <sub>틀: Companion.with(닫힘)</sub>
- 로그 보면 나오잖아요. 정비실에 토끼 씨 있었어요. <sub>틀: Companion.with(닫힘)</sub>
- 그걸 왜 물어보시는데요? 토끼 씨요. 로그 보세요. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 건 토끼 씨예요. <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 안 바뀌어요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 정비실이요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 정비실에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 정비실이요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 아까 말한 그대로예요. 정비실이요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번을 물어요. 정비실이요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '그냥 그럼'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '귀찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평범함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 예민함'이라고 적으셨습니다. 이유가 무엇입니까?  

- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 일이 잘 풀렸으니까요. 그게 다예요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 정비실 일 때문이죠. 일이 다 밀렸잖아요. <sub>틀: MoodReason.incident(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무랑은 관계없어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무랑은 관계없어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처음엔 괜찮았는데요. 중간부터요. <sub>틀: MoodBefore.no(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 저 볼 시간에 다른 데 보세요. <sub>틀: deny</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제가 그럴 이유가 있어요? <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제가 그럴 이유가 있어요? <sub>틀: deny(닫힘)</sub>
- 아닌데요. 제 일 하고 있었거든요. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실에서요. 덕분에 일 다 밀렸어요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 정비실에서 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실이요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 정비실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실에서요. 덕분에 일 다 밀렸어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 또요? 정비실에서 기계가 멈추는 거 봤는데요. <sub>틀: incident.direct + 조각[여는말]</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요. 이러다 노는 사람 취급받겠네요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 지금 비어 있어요. 배치 좀 제대로 해주세요. <sub>틀: status.idle(닫힘)</sub>
- 놀고 있어요. 제 탓은 아니고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요. 쓸데없이 서 있기만 하고요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없는데요. 뭐라도 주세요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 토끼 씨랑 정비실에 있었는데요. 기계가 멈췄어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘) + 기억[mem.heard(잘림)]</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. 토끼 씨한테도 물어보세요. <sub>틀: incident.direct.with(닫힘)</sub>
- 정비실에서 기계가 멈추는 거 봤는데요. 그 이상은 없어요. <sub>틀: incident.direct + 조각[덧붙임]</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 정비실에서 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. 토끼 씨한테도 물어보세요. <sub>틀: incident.direct.with(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. 토끼 씨한테도 물어보세요. <sub>틀: incident.direct.with(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 정신없었죠. 정비실에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 뭐, 버텼어요. 정비실 쪽 뒤치다꺼리하느라. <sub>틀: status.busy(닫힘)</sub>
- 정신없었죠. 정비실에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 정비실 쪽 일만 아니었으면 괜찮았어요. <sub>틀: status.busy</sub>
- 별로였어요. 정비실에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었죠. 정비실에서 사고가 났는데 멀쩡할 리가요. <sub>틀: status.busy(닫힘)</sub>
- 피곤했어요. 정비실 건으로 하루가 다 갔는데요. <sub>틀: status.busy(닫힘)</sub>
- 별로였어요. 정비실에서 사고 나는 바람에요. <sub>틀: status.busy(닫힘)</sub>
- 정비실 때문에 일이 다 밀렸는데요. <sub>틀: status.busy</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.heard(잘림)]</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.heard(잘림)]</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 봤어요. 정비실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘) + 기억[mem.heard(잘림)]</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 하필 제 앞에서요. 정비실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.heard(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 제가 본 데선 없었어요. <sub>틀: nosight</sub>
- 못 봤는데요. 아무나 찍으면 일만 꼬여요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 제가 본 데선 없었어요. <sub>틀: nosight</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 제가 본 데선 없었어요. 괜히 이름 대고 싶진 않아요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람은 없었어요. 제 일 하기도 바빴는데요. <sub>틀: nosight(닫힘)</sub>
- 없어요. 남 구경할 시간 없었거든요. <sub>틀: nosight(닫힘)</sub>
- 못 봤는데요. 괜히 이름 대고 싶진 않아요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 또 수상한 사람이요? 제가 본 데선 없었어요. <sub>틀: nosight + 조각[여는말]</sub>
- 제가 본 데선 없었어요. <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 소리만 들었어요. 뭔지는 몰라요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 들리긴 했는데요. 보진 못했고요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 소리만 들었어요. 뭔지는 몰라요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 들리긴 했는데요. 보진 못했고요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘)</sub>
- 알긴 알아요. 벽 너머였지만요. <sub>틀: IncidentKnown.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실이요. 토끼 씨도 있었으니까 확인해 보세요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 토끼 씨랑 정비실에 있었어요. 그게 왜요? <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 정비실에 토끼 씨랑 있었는데요. 본인한테 물어보시든가요. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 토끼 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 로그 보면 나오잖아요. 정비실에 토끼 씨 있었어요. <sub>틀: Companion.with(닫힘)</sub>
- 같이 있던 건 토끼 씨예요. <sub>틀: Companion.with</sub>
- 로그 보면 나오잖아요. 정비실에 토끼 씨 있었어요. <sub>틀: Companion.with(닫힘)</sub>
- 왜요, 토끼 씨 있었는데요. <sub>틀: Companion.with</sub>
- 토끼 씨요. 궁금하면 본인한테 물어보시든가요. <sub>틀: Companion.with(닫힘)</sub>
- 왜요, 토끼 씨 있었는데요. <sub>틀: Companion.with</sub>
- 왜요, 토끼 씨 있었는데요. <sub>틀: Companion.with</sub>
- 같이 있던 건 토끼 씨예요. <sub>틀: Companion.with</sub>
- 토끼 씨랑 있었는데요. 그게 왜요? <sub>틀: Companion.with(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요. 정비실에서 일하고 있었고요. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>
- 평소랑 같았는데요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요. 정비실에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 정비실에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 정비실에 있었다고요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같다니까요. 정비실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라질 게 없는데요. 정비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같다니까요. 정비실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '조금 예민함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '그냥 그럼'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '평범함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '귀찮음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 사고 났잖아요. 기분 좋을 리가요. <sub>틀: MoodReason.incident(닫힘)</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 막힌 게 없었어요. 그래서요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 이유 없어요. 그냥 적은 건데요. <sub>틀: MoodReason.plain(닫힘)</sub>
- 별 뜻 없어요. <sub>틀: MoodReason.plain</sub>
- 막힌 게 없었어요. 그래서요. <sub>틀: MoodReason.calm(닫힘)</sub>
- 정비실 일 때문이죠. 일이 다 밀렸잖아요. <sub>틀: MoodReason.incident(닫힘)</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 처음엔 괜찮았는데요. 중간부터요. <sub>틀: MoodBefore.no(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였어요. 여기 일이랑은 상관없고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였어요. 여기 일이랑은 상관없고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근 전부터였어요. 여기 일이랑은 상관없고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그래요. 신경 쓰지 마세요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네. 올 때부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그전부터요. 여기 와서 그런 거 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처음엔 괜찮았는데요. 중간부터요. <sub>틀: MoodBefore.no(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>
- 로그 보세요. 저 아니에요. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 설비가 서 버렸어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 기록에도 남았을걸요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈앞에서 그랬는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 정비실이요. 설비가 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었는데요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>

