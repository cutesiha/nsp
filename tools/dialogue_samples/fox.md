# 여우 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/fox.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어서 구경 중이에요~ <sub>틀: status.idle</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 지금은 손이 비었어요~ 뭐 시키실 거 있어요? <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 지금은 손이 비었어요~ 뭐 시키실 거 있어요? <sub>틀: status.idle(닫힘)</sub>
- 지금은 손이 비었어요~ 뭐 시키실 거 있어요? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 소리만 들었어요~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보진 못했어요~ 저장고 쪽이요. 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 저장고 쪽이었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였죠. 벽이 웅 하고 떨렸어요. 궁금하긴 하더라고요~ <sub>틀: incident.indirect(닫힘)</sub>
- 제 눈으로 본 건 아니고요. 들리긴 했어요. 쿵 하고 울렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였죠. 쿵 하고 울렸어요. 궁금하긴 하더라고요~ <sub>틀: incident.indirect(닫힘)</sub>
- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 제 눈으로 본 건 아니고요. 들리긴 했어요. 쿵 하고 울렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 밤 11시 10분쯤 저장고 쪽에서 쿵 하고 울렸어요~ 제 눈으로 본 건 아니고요. <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 본 건 아니에요~ 들리긴 했어요. 뭔가 요란하게 부서지는 소리가 났어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 오늘은 저장고가 주인공이었죠~ <sub>틀: status.busy</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 저장고 쪽 때문에 다들 정신없더라고요~ 보는 재미는 있었어요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 직접 본 건 아니에요~ 들리긴 했어요. 쿵 하고 울렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요~ 저장고 쪽이었어요~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 들리긴 했어요. 벽이 웅 하고 떨렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 보진 못했어요~ 저장고 쪽이요. 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 저장고 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 조각[보정]</sub>
- 직접 본 건 아니에요~ 저장고 쪽이었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 옆방 일이었죠~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 눈에 걸리는 사람은 없었어요~ 괜히 이름 대면 저만 곤란해지잖아요~ <sub>틀: nosight + 조각[덧붙임]</sub>
- 눈에 걸리는 사람은 없었어요~ <sub>틀: nosight</sub>
- 수상한 사람이요~? 눈에 걸리는 사람은 없었어요~ <sub>틀: nosight + 조각[여는말]</sub>
- 딱히요~ 다들 얌전하던걸요? <sub>틀: nosight(닫힘)</sub>
- 눈에 걸리는 사람은 없었어요~ 괜히 이름 대면 저만 곤란해지잖아요~ <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 글쎄요~ 다들 착하게 일하던데요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 어머, 그런 일이 있었어요~? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요~ 재밌네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 어머, 그런 일이 있었어요~? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요~? 금시초문인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 전혀요~ 제가 모르는 일도 있네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요~ 재밌네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 어머, 그런 일이 있었어요~? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저요? 코어실에서 혼자 얌전히 있었죠~ 믿기 어려우시겠지만. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 코어실에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저요? 코어실에서 혼자 얌전히 있었죠~ 믿기 어려우시겠지만. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 코어실에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 코어실에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저요? 코어실에서 혼자 얌전히 있었죠~ 믿기 어려우시겠지만. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 아무도 없었어요~ <sub>틀: Companion.alone</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 없었어요~ 저만 있었죠. <sub>틀: Companion.alone(닫힘)</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요~ <sub>틀: Companion.alone</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 코어실엔 저 혼자였어요~ 아쉽게도 증인은 없네요. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요~ <sub>틀: Companion.alone</sub>
- 없었어요~ 저만 있었죠. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>

### 진술 재확인

Q: 밤 10시 40분경 코어실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 똑같아요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 물으셔도 코어실이요~ <sub>틀: Restate.same</sub>
- 똑같아요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요~ 코어실이었죠. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 물으셔도 코어실이요~ <sub>틀: Restate.same</sub>
- 안 바뀌어요~ 코어실이었죠. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '꽤 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 저 아니에요. 관리자님, 사람 잘못 보셨어요~ <sub>틀: deny(닫힘)</sub>
- 저요~? 설마 이런 상황에 저부터 의심하시는 건 아니겠죠~ <sub>틀: deny(닫힘)</sub>
- 에이~ 저는 그런 번거로운 짓 안 해요. <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 소리만 들었어요~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 저장고 쪽이었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 11시 10분쯤 저장고 쪽에서 쿵 하고 울렸어요. 제 눈으로 본 건 아니고요~ <sub>틀: incident.indirect + 조각[보정]</sub>
- 소리만 들었어요~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 밤 11시 10분쯤 저장고 쪽이었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요~ 옆방 일이었죠~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 저장고 쪽에서 벽이 웅 하고 떨렸어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 조각[보정]</sub>
- 저장고 쪽에서 벽이 웅 하고 떨렸어요. 제 눈으로 본 건 아니고요~ <sub>틀: incident.indirect + 조각[보정]</sub>
- 저장고 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 조각[보정]</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요~ 뭐 재밌는 일 없어요? <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요~ 뭐 재밌는 일 없어요? <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어서 구경 중이에요~ <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 조금 전에 저장고에서 봤죠~ 기계가 딱 멈췄어요. 양 씨 표정이 볼만했어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 흐음, 이상현상이요? 저장고에서 설비가 갑자기 서는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct + 조각[여는말]</sub>
- 제가 딱 있을 때였어요~ 저장고에서요. 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서 기계가 딱 멈추는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct</sub>
- 그쯤 저장고에서 봤죠~ 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 이상현상이요~? 그쯤 저장고에서 설비가 갑자기 서는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct + 조각[여는말]</sub>
- 조금 전에 저장고였어요. 기계가 딱 멈췄어요. 운이 좋았다고 해야 하나~? <sub>틀: incident.direct(닫힘)</sub>
- 마침 제 눈앞이었어요. 기계가 딱 멈췄어요. 구경거리였죠~ <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서 봤죠~ 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서 봤죠~ 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 저장고 일로 좀 시끌벅적했죠~ <sub>틀: status.busy</sub>
- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 저장고 일로 좀 시끌벅적했죠~ <sub>틀: status.busy</sub>
- 나름 스펙터클했어요~ 저장고에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 저장고에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 저장고 일로 좀 시끌벅적했죠~ <sub>틀: status.busy</sub>
- 저장고 쪽 때문에 다들 정신없더라고요~ 보는 재미는 있었어요. <sub>틀: status.busy(닫힘)</sub>
- 저장고 일로 좀 시끌벅적했죠~ <sub>틀: status.busy</sub>
- 저장고 일로 좀 시끌벅적했죠~ <sub>틀: status.busy</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 구경 제대로 했죠. 저장고에서 기계가 딱 멈췄어요~ <sub>틀: incident.direct(닫힘)</sub>
- 밤 11시 10분쯤 저장고에서 봤죠~ 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서 봤죠~ 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 구경 제대로 했죠. 저장고에서 기계가 딱 멈췄어요~ <sub>틀: incident.direct(닫힘)</sub>
- 제가 딱 있을 때였어요~ 저장고에서요. 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 마침 제 눈앞이었어요. 설비가 갑자기 서 버렸어요. 구경거리였죠~ <sub>틀: incident.direct(닫힘)</sub>
- 봤어요~ 저장고에서요. 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 흐음, 이상현상이요? 저장고에서 설비가 갑자기 서는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct + 조각[여는말]</sub>
- 눈앞에서 벌어졌죠~ 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 마침 제 눈앞이었어요. 설비가 갑자기 서 버렸어요. 구경거리였죠~ <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 없었어요. 저도 나름 사람 보는 눈은 있는데 말이죠~ <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 눈에 걸리는 사람은 없었어요~ <sub>틀: nosight</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 눈에 걸리는 사람은 없었어요~ <sub>틀: nosight</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 딱히요~ 다들 얌전하던걸요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 딱히요~ 다들 얌전하던걸요? <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 전혀요~ 제가 모르는 일도 있네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요~? 금시초문인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요~ 재밌네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 전혀요~ 제가 모르는 일도 있네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 전혀요~ 제가 모르는 일도 있네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요~ 재밌네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이~ 누가 그러던가요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 듣는데요~ 재밌네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 어머, 그런 일이 있었어요~? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저장고요~ 양 씨랑 같이 있었어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고였어요. 양 씨가 증인이니까 안심하세요~ <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고였어요. 양 씨가 증인이니까 안심하세요~ <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 양 씨랑 저장고에 있었어요~ 벌벌 떠는 거 구경하면서요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 양 씨랑 저장고에 있었어요~ 벌벌 떠는 거 구경하면서요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고였어요. 양 씨가 증인이니까 안심하세요~ <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 양 씨랑 저장고에 있었어요~ 벌벌 떠는 거 구경하면서요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고에 있었죠~ 옆에 양 씨가 있었으니까 물어보셔도 돼요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고에 있었죠~ 옆에 양 씨가 있었으니까 물어보셔도 돼요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고요~ 양 씨랑 같이 있었어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 양 씨요~ <sub>틀: Companion.with</sub>
- 같이 있던 사람이라~ 양 씨죠. <sub>틀: Companion.with(닫힘)</sub>
- 양 씨랑 있었어요~ <sub>틀: Companion.with</sub>
- 저장고에 양 씨가 있었죠~ <sub>틀: Companion.with</sub>
- 저장고에 양 씨가 있었죠~ <sub>틀: Companion.with</sub>
- 같이 있던 사람이라~ 양 씨죠. <sub>틀: Companion.with(닫힘)</sub>
- 그때 옆엔 양 씨가 있었어요~ <sub>틀: Companion.with</sub>
- 누구냐면~ 양 씨요. 왜, 뭐 걸리는 거라도? <sub>틀: Companion.with(닫힘)</sub>
- 양 씨요~ <sub>틀: Companion.with</sub>
- 양 씨요~ <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 별거 없었어요~ 저장고에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 저장고에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 평소처럼요~ <sub>틀: BeforeIncident.plain</sub>
- 평소처럼요~ <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 저장고에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 저장고에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>

### 진술 재확인

Q: 밤 10시 40분경 저장고에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요~ 저장고였죠. <sub>틀: Restate.same(닫힘)</sub>
- 아까 말씀드린 그대로예요~ 저장고요. <sub>틀: Restate.same(닫힘)</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 저장고였죠. <sub>틀: Restate.same(닫힘)</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 저장고였죠. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '꽤 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '여유로움'이라고 적으셨습니다. 이유가 무엇입니까?  

- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 에이~ 저는 그런 번거로운 짓 안 해요. <sub>틀: deny(닫힘)</sub>
- 에이~ 저는 그런 번거로운 짓 안 해요. <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 눈앞에서 벌어졌죠~ 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고였어요. 설비가 갑자기 서 버렸어요. 운이 좋았다고 해야 하나~? <sub>틀: incident.direct(닫힘)</sub>
- 저장고였어요. 설비가 갑자기 서 버렸어요. 운이 좋았다고 해야 하나~? <sub>틀: incident.direct(닫힘)</sub>
- 마침 제 눈앞이었어요. 기계가 딱 멈췄어요. 구경거리였죠~ <sub>틀: incident.direct(닫힘)</sub>
- 봤어요~ 저장고에서요. 설비가 갑자기 서 버렸어요. <sub>틀: incident.direct(닫힘)</sub>
- 아까 말씀드렸는데~ 저장고에서 기계가 딱 멈추는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct + 조각[여는말]</sub>
- 저장고에서 봤죠~ 기계가 딱 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 구경 제대로 했죠. 저장고에서 설비가 갑자기 서 버렸어요~ <sub>틀: incident.direct(닫힘)</sub>
- 밤 11시 10분쯤 저장고였어요. 기계가 딱 멈췄어요. 운이 좋았다고 해야 하나~? <sub>틀: incident.direct(닫힘)</sub>
- 또 물어보시네요~ 저장고에서 기계가 딱 멈추는 걸 제 눈으로 봤죠~ <sub>틀: incident.direct + 조각[여는말]</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어서 구경 중이에요~ <sub>틀: status.idle</sub>
- 지금이요~? 할 일이 없어서 구경 중이에요~ <sub>틀: status.idle + 조각[여는말]</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요~ 뭐 재밌는 일 없어요? <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 한가해요~ 뭐 재밌는 일 없어요? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 무난했죠~ 이런 날도 있어야죠. <sub>틀: status.quiet(닫힘)</sub>
- 조용했어요. 조금 심심할 정도로요~ <sub>틀: status.quiet(닫힘)</sub>
- 조용했어요. 조금 심심할 정도로요~ <sub>틀: status.quiet(닫힘)</sub>
- 조용했어요. 조금 심심할 정도로요~ <sub>틀: status.quiet(닫힘)</sub>
- 별일 없었어요~ 관리자님 쪽은 어떠셨어요? <sub>틀: status.quiet(닫힘)</sub>
- 무난했죠~ 이런 날도 있어야죠. <sub>틀: status.quiet(닫힘)</sub>
- 무난했죠~ 이런 날도 있어야죠. <sub>틀: status.quiet(닫힘)</sub>
- 딱히 할 얘기가 없을 만큼 평범했어요~ <sub>틀: status.quiet</sub>
- 조용했어요. 조금 심심할 정도로요~ <sub>틀: status.quiet(닫힘)</sub>
- 조용했어요. 조금 심심할 정도로요~ <sub>틀: status.quiet(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>
- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 글쎄요~ 다들 착하게 일하던데요? <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>
- 눈에 걸리는 사람은 없었어요~ 괜히 이름 대면 저만 곤란해지잖아요~ <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 딱히요~ 다들 얌전하던걸요? <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 글쎄요~ 다들 착하게 일하던데요? <sub>틀: nosight(닫힘)</sub>
- 없었어요. 저도 나름 사람 보는 눈은 있는데 말이죠~ <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이~ 누가 그러던가요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요~? 금시초문인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요~? 금시초문인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 전혀요~ 제가 모르는 일도 있네요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이~ 누가 그러던가요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이~ 누가 그러던가요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요~? 금시초문인데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요~ 관리자님은 어떻게 아셨어요? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실이요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 코어실에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 코어실에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘)</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였죠~ 그게 수상해 보이세요? <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요~ <sub>틀: Companion.alone</sub>
- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘)</sub>
- 코어실엔 저 혼자였어요~ 아쉽게도 증인은 없네요. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요~ <sub>틀: Companion.alone</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 그때는 혼자였어요~ 누가 같이 있었으면 좋았을걸. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였죠~ 그게 수상해 보이세요? <sub>틀: Companion.alone(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 평소처럼요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>
- 특별한 건 없었어요~ <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요~ 코어실에서 일하고 있었죠. <sub>틀: BeforeIncident.plain(닫힘)</sub>

### 진술 재확인

Q: 밤 10시 40분경 코어실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 코어실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 물으셔도 코어실이요~ <sub>틀: Restate.same</sub>
- 몇 번 물으셔도 코어실이요~ <sub>틀: Restate.same</sub>
- 아까 말씀드린 그대로예요~ 코어실이요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 물으셔도 코어실이요~ <sub>틀: Restate.same</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 코어실이었죠. <sub>틀: Restate.same(닫힘)</sub>
- 안 바뀌어요~ 코어실이었죠. <sub>틀: Restate.same(닫힘)</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 코어실이었죠. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '여유로움'이라고 적으셨습니다. 이유가 무엇입니까?  

- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬죠~ 여기 오기 전부터요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 저 아니에요. 관리자님, 사람 잘못 보셨어요~ <sub>틀: deny(닫힘)</sub>
- 아니에요~ 제가 그렇게 수상해 보여요? <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 아니에요~ 제가 그렇게 수상해 보여요? <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 딱히~ 관리자님이 뭘 기대하셨는지는 모르겠지만요. <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 제 쪽은 조용했어요~ 아쉽게도. <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 없었어요. 있었으면 제가 먼저 재밌게 말씀드렸죠~ <sub>틀: noanomaly(닫힘)</sub>
- 이상한 거요~? 없었어요. 뭔가 들으신 게 있으신가 보네요~ <sub>틀: noanomaly(닫힘)</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 대기 중이에요~ 이런 시간도 나쁘진 않네요. <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 불러주시면 가죠~ <sub>틀: status.idle(닫힘)</sub>
- 놀고 있죠~ 불러주시면 가고요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 정비실 쪽에서 쿵 하고 울렸어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 정비실 쪽에서 쿵 하고 울렸어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 벽 너머였죠. 쿵 하고 울렸어요. 궁금하긴 하더라고요~ <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 들리긴 했어요. 쿵 하고 울렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 소리만 들었어요~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 벽 너머였죠. 쿵 하고 울렸어요. 궁금하긴 하더라고요~ <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 들리긴 했어요. 벽이 웅 하고 떨렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 소리만 들었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 바빴어요. 정비실에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 정비실에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 나름 스펙터클했어요~ 정비실에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 오늘은 정비실이 주인공이었죠~ <sub>틀: status.busy</sub>
- 바빴어요. 정비실에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 정비실 쪽 때문에 다들 정신없더라고요~ 보는 재미는 있었어요. <sub>틀: status.busy(닫힘)</sub>
- 나름 스펙터클했어요~ 정비실에서 사고가 났으니까요. <sub>틀: status.busy(닫힘)</sub>
- 심심할 틈은 없었죠~ 정비실 일 덕분에요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 정비실에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 정비실에서 사고가 났으니까요~ <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 정비실 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 아까 정비실 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 직접 본 건 아니에요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 그쯤 정비실 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 제 눈으로 본 건 아니고요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 정비실 쪽에서 뭔가 요란하게 부서지는 소리가 났어요. 제 눈으로 본 건 아니고요~ <sub>틀: incident.indirect + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 직접 보진 못했어요~ 정비실 쪽이요. 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보진 못했어요~ 정비실 쪽이요. 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>
- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)] + 조각[보정]</sub>
- 제 눈으로 본 건 아니고요. 들리긴 했어요. 뭔가 요란하게 부서지는 소리가 났어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였죠. 쿵 하고 울렸어요. 궁금하긴 하더라고요~ <sub>틀: incident.indirect(닫힘) + 기억[mem.incident.here(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수상한 사람이라~ 딱히요. 관리자님은 짚이는 분이라도? <sub>틀: nosight(닫힘)</sub>
- 없었어요. 저도 나름 사람 보는 눈은 있는데 말이죠~ <sub>틀: nosight(닫힘)</sub>
- 딱히요~ 다들 얌전하던걸요? <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>
- 없었어요. 저도 나름 사람 보는 눈은 있는데 말이죠~ <sub>틀: nosight(닫힘)</sub>
- 제 눈엔 안 띄었어요~ 관리자님 눈엔 띄었나 봐요? <sub>틀: nosight(닫힘)</sub>
- 아쉽게도 없었어요~ 있었으면 재밌었을 텐데. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 알죠~ 제가 거기 있었는걸요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 알죠~ 제가 거기 있었는걸요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 눈앞에서 났는데 모를 리가요~ 그때는 혼자였어요~ <sub>틀: IncidentKnown.direct + 기억[mem.alone, mem.heard(잘림)]</sub>
- 눈앞에서 났는데 모를 리가요~ 그때는 혼자였어요~ <sub>틀: IncidentKnown.direct + 기억[mem.alone]</sub>
- 봤어요~ 꽤 인상적이었죠. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 눈앞에서 났는데 모를 리가요~ 그때는 혼자였어요~ <sub>틀: IncidentKnown.direct + 기억[mem.alone]</sub>
- 봤어요~ 꽤 인상적이었죠. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 봤어요~ 꽤 인상적이었죠. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림), mem.heard(잘림)]</sub>
- 알죠~ 제가 거기 있었는걸요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 봤어요~ 꽤 인상적이었죠. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저장고에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 저장고에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.worked(잘림), mem.relocated(잘림)]</sub>
- 저장고요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.relocated(잘림)]</sub>
- 그때는 저장고에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 그때는 저장고에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저요? 저장고에서 혼자 얌전히 있었죠~ 믿기 어려우시겠지만. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고요~ 혼자였고요. 그게 수상해 보이세요? <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.relocated(잘림)]</sub>
- 그때는 저장고에 저 하나였어요~ 오붓하게요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.worked(잘림), mem.relocated(잘림)]</sub>
- 저장고에 혼자 있었어요~ 증인이 없어서 아쉽네요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.relocated(잘림)]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였죠~ 그게 수상해 보이세요? <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였죠~ 그게 수상해 보이세요? <sub>틀: Companion.alone(닫힘)</sub>
- 없었어요~ 저만 있었죠. <sub>틀: Companion.alone(닫힘)</sub>
- 저장고엔 저 혼자였어요~ 아쉽게도 증인은 없네요. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘) + 기억[mem.relocated(잘림), mem.worked(잘림)]</sub>
- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 없었어요~ 정비실 쪽이 한 번 시끄러웠죠~ <sub>틀: Companion.alone + 기억[mem.heard, mem.relocated(잘림)]</sub>
- 저장고엔 저 혼자였어요~ 아쉽게도 증인은 없네요. <sub>틀: Companion.alone(닫힘) + 기억[mem.heard(잘림)]</sub>
- 혼자였어요~ 조용해서 좋았죠. <sub>틀: Companion.alone(닫힘)</sub>
- 없었어요~ 저만 있었죠. <sub>틀: Companion.alone(닫힘) + 기억[mem.heard(잘림)]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 직전까지 재고 정리 중이었죠~ <sub>틀: BeforeIncident.task</sub>
- 저장고에서 재고 정리 하던 참이었어요~ <sub>틀: BeforeIncident.task</sub>
- 저장고에서 재고 정리 하던 참이었어요~ <sub>틀: BeforeIncident.task + 기억[mem.relocated(잘림), mem.heard(잘림)]</sub>
- 직전까지 재고 정리 중이었죠~ <sub>틀: BeforeIncident.task</sub>
- 재고 정리 하고 있었어요~ <sub>틀: BeforeIncident.task</sub>
- 재고 정리 하고 있었어요~ <sub>틀: BeforeIncident.task</sub>
- 직전까지 재고 정리 중이었죠~ <sub>틀: BeforeIncident.task</sub>
- 재고 정리 하고 있었어요~ 정비실 쪽이 한 번 시끄러웠죠~ <sub>틀: BeforeIncident.task + 기억[mem.heard]</sub>
- 직전까지 재고 정리 중이었죠~ 밤 10시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요~ <sub>틀: BeforeIncident.task + 기억[mem.relocated]</sub>
- 직전까지 재고 정리 중이었죠~ <sub>틀: BeforeIncident.task</sub>

### 진술 재확인

Q: 밤 10시 40분경 저장고에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.relocated(잘림)]</sub>
- 똑같아요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 저장고였죠. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로예요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 제 말이 바뀌길 기다리시나 봐요~ 저장고였죠. <sub>틀: Restate.same(닫힘) + 기억[mem.heard(잘림)]</sub>
- 안 바뀌어요~ 저장고였죠. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요~ 저장고에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 물으셔도 저장고요~ <sub>틀: Restate.same</sub>

### 이동 · 이유

Q: 밤 10시 22분경 코어실에서 저장고로 이동한 이유는 무엇입니까?  

- 지시대로 옮긴 거예요~ 저장고엔 저 혼자였죠~ <sub>틀: MoveReason.ordered + 기억[mem.alone]</sub>
- 관리자님이 옮기라고 하셨잖아요~ 그래서 저장고로 갔죠. <sub>틀: MoveReason.ordered(닫힘)</sub>
- 코어실에서 저장고로요. 관리자님 지시였는데, 벌써 잊으셨어요~? <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.worked(잘림), mem.alone(잘림)]</sub>
- 관리자님이 옮기라고 하셨잖아요~ 그래서 저장고로 갔죠. <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.alone(잘림)]</sub>
- 관리자님이 옮기라고 하셨잖아요~ 그래서 저장고로 갔죠. <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.worked(잘림), mem.alone(잘림)]</sub>
- 지시대로 옮긴 거예요~ <sub>틀: MoveReason.ordered</sub>
- 코어실에서 저장고로요. 관리자님 지시였는데, 벌써 잊으셨어요~? <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.alone(잘림)]</sub>
- 코어실에서 저장고로요. 관리자님 지시였는데, 벌써 잊으셨어요~? <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.worked(잘림)]</sub>
- 지시대로 옮긴 거예요~ <sub>틀: MoveReason.ordered</sub>
- 지시대로 옮긴 거예요~ 그때 재고 정리 거의 끝나가던 참이었어요~ <sub>틀: MoveReason.ordered + 기억[mem.worked]</sub>

### 이동 · 거기서 한 일

Q: 저장고에서는 무엇을 했습니까?  

- 저장고에서 재고 정리 했죠~ <sub>틀: ActionThere.task + 기억[mem.relocated(잘림)]</sub>
- 재고 정리 하고 나왔어요~ <sub>틀: ActionThere.task</sub>
- 재고 정리 했어요~ <sub>틀: ActionThere.task + 기억[mem.relocated(잘림)]</sub>
- 재고 정리 하고 나왔어요~ 밤 10시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요~ <sub>틀: ActionThere.task + 기억[mem.relocated, mem.alone(잘림)]</sub>
- 재고 정리 했어요~ <sub>틀: ActionThere.task</sub>
- 재고 정리 했어요~ <sub>틀: ActionThere.task</sub>
- 재고 정리 하고 나왔어요~ 밤 10시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요~ <sub>틀: ActionThere.task + 기억[mem.relocated, mem.alone(잘림)]</sub>
- 재고 정리 했어요~ 밤 10시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요~ <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 재고 정리 하고 나왔어요~ 저장고엔 저 혼자였죠~ <sub>틀: ActionThere.task + 기억[mem.alone]</sub>
- 저장고에서 재고 정리 했죠~ <sub>틀: ActionThere.task</sub>

### 기분 · 이유

Q: 오늘 기분을 '여유로움'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '꽤 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 별일 없었으니까요~ <sub>틀: MoodReason.calm</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>
- 그냥 기분이 괜찮았어요~ 이런 날도 있어야죠. <sub>틀: MoodReason.calm(닫힘)</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네~ 근무랑은 상관없는 일이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근할 때부터요~ 사적인 거라 넘어가 주시면 좋겠고요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요~ <sub>틀: MoodBefore.yes</sub>
- 맞아요~ 그냥 그런 날이에요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 아니에요~ 제가 그렇게 수상해 보여요? <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 의심받으니까 좀 섭섭한데요~ 아니에요. <sub>틀: deny(닫힘)</sub>
- 설마요~ 관리자님, 농담이시죠? <sub>틀: deny(닫힘)</sub>
- 증거라도 있으세요~? 저는 아닌데. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요~ 옆방 일이었죠~ 뭔가 요란하게 부서지는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 보진 못했어요~ 정비실 쪽이요. 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 제 눈으로 본 건 아니고요. 옆방 일이었죠~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 제 눈으로 본 건 아니고요. 들리긴 했어요. 벽이 웅 하고 떨렸어요. 무슨 일인지는 관리자님이 더 잘 아시겠죠~? <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 정비실 쪽에서 쿵 하고 울렸어요~ 제 눈으로 본 건 아니고요. <sub>틀: incident.indirect + 조각[보정]</sub>
- 소리만 들었어요~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요~ 옆방 일이었죠~ 벽이 웅 하고 떨렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요~ 정비실 쪽이었어요~ 쿵 하고 울렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

