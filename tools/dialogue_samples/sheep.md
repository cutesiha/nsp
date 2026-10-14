# 양 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/sheep.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 그, 그냥 서 있어요... 괜찮은 건가요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 지금이요...? 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘)</sub>
- 지금이요...? 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 소, 손이 비었어요... 제가 할 수 있는 거 있을까요...? <sub>틀: status.idle(닫힘)</sub>
- 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘)</sub>
- 저, 저 뭐 하면 될까요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 그, 그냥 서 있어요... 괜찮은 건가요...? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 그, 이상현상이요...? 저, 저는 이상한 거 못 봤어요... <sub>틀: noanomaly + 조각[여는말]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 저, 저는 이상한 거 못 봤어요... <sub>틀: noanomaly</sub>
- 이상현상이요...? 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>
- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>
- 벼, 별일 없었어요... 제가 모르는 일만 없다면요... <sub>틀: status.quiet(닫힘)</sub>
- 조, 조용했어요... 다행이에요... <sub>틀: status.quiet(닫힘)</sub>
- 조, 조용했어요... 다행이에요... <sub>틀: status.quiet(닫힘)</sub>
- 벼, 별일 없었어요... 제가 모르는 일만 없다면요... <sub>틀: status.quiet(닫힘)</sub>
- 무, 무사히 지나갔어요... 다행히요... <sub>틀: status.quiet(닫힘)</sub>
- 조, 조용했어요... 다행이에요... <sub>틀: status.quiet(닫힘)</sub>
- 벼, 별일 없었어요... 제가 모르는 일만 없다면요... <sub>틀: status.quiet(닫힘)</sub>
- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 저, 저는 이상한 거 못 봤어요... 제, 제가 놓친 게 있을 수도 있어요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 저, 저는 이상한 거 못 봤어요... 제, 제가 놓친 게 있을 수도 있어요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 저, 저는 이상한 거 못 봤어요... 뭐, 뭔가 있으면 바로 말씀드릴게요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 저, 저는 이상한 거 못 봤어요... 뭐, 뭔가 있으면 바로 말씀드릴게요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘)</sub>
- 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 그, 수상한 사람이요...? 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요...? 저, 저는 못 봤어요... <sub>틀: nosight + 조각[여는말]</sub>
- 수, 수상한 분은 없었던 것 같아요... <sub>틀: nosight</sub>
- 수상한 사람이요...? 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘)</sub>
- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요...? 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 저, 전혀 몰랐어요... <sub>틀: IncidentKnown.none</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 의무실에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 의무실에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 의무실에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저, 저는 의무실에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 의무실에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 의무실에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 의무실에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 의무실에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 의무실에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 의무실에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 호, 혼자였어요... <sub>틀: Companion.alone</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 의무실엔 저, 저 혼자였어요... 많이 무서웠어요... <sub>틀: Companion.alone(닫힘)</sub>
- 아, 아무도 없었어요... <sub>틀: Companion.alone</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 의무실에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 의무실에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 의무실에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 의무실에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 의무실에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 의무실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그, 그대로예요... 의무실에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 그, 그대로예요... 의무실에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 그, 그대로예요... 의무실에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 의무실에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 의무실이었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 안 바뀌어요... 의무실이었어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 의무실에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 벼, 변한 건 없어요... 의무실이었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 의무실이었어요... <sub>틀: Restate.same(닫힘)</sub>
- 벼, 변한 건 없어요... 의무실이었어요... <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '조금 긴장됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '잘 모르겠음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '신경 쓰임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 불안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '걱정됨'이라고 적으셨습니다. 이유가 무엇입니까?  

- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 오, 오늘은 조용해서요... <sub>틀: MoodReason.calm</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 여, 여기 오기 전부터요... 신경 쓰이셨다면 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 오, 올 때부터 그랬어요... <sub>틀: MoodBefore.yes</sub>
- 오, 올 때부터 그랬어요... <sub>틀: MoodBefore.yes</sub>
- 오, 올 때부터 그랬어요... <sub>틀: MoodBefore.yes</sub>
- 워, 원래 그래요... 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 워, 원래 그래요... 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 오, 올 때부터 그랬어요... <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 그, 저요...? 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 아, 아니에요... 저 정말 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 제, 제가 그런 걸 어떻게 해요... <sub>틀: deny</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 제, 제가 그런 걸 어떻게 해요... 왜, 왜 물어보시는 거예요...? <sub>틀: deny + 조각[되묻기]</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 저, 저 아니에요.... 정말이에요... <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 다, 다시 말씀드리면요, 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 다, 다시 말씀드리면요, 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 다, 다시 말씀드리면요, 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 소, 손이 비었어요... 제가 할 수 있는 거 있을까요...? <sub>틀: status.idle(닫힘)</sub>
- 그, 지금이요...? 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 저, 저 뭐 하면 될까요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 그, 그냥 서 있어요... 괜찮은 건가요...? <sub>틀: status.idle(닫힘)</sub>
- 소, 손이 비었어요... 제가 할 수 있는 거 있을까요...? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 봐, 봤어요... 저장고에서요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 저장고에서요... 기, 기계가 멈췄어요. 여우 씨도 옆에 있었어요... <sub>틀: incident.direct.with(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 저, 저 봤어요... 설비가 갑자기 섰어요. 여우 씨도 같이 봤어요... <sub>틀: incident.direct.with(닫힘)</sub>
- 저, 저 봤어요... 저장고에서요. 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 너, 너무 놀랐어요... 스, 스스로도 믿기지 않아요... 저장고에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)] + 조각[여는말]</sub>
- 스, 스스로도 믿기지 않아요... 저장고에서 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 봐, 봤어요... 저장고에서요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 봐, 봤어요... 저장고에서요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 스, 스스로도 믿기지 않아요... 저장고에서 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 너, 너무 놀랐어요... 제, 제 눈앞이었어요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 오, 오늘은... 저장고 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 오, 오늘은... 저장고 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 오, 오늘은... 저장고 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘)</sub>
- 오, 오늘은... 저장고 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘)</sub>
- 저장고 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 저장고 일 때문에... 저, 정신이 없었어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 저, 정신없었어요... 저장고에서 사고가 나서요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 저장고 일 때문에... 저, 정신이 없었어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 무, 무서웠어요... 저장고에서 사고가 났잖아요... <sub>틀: status.busy(닫힘)</sub>
- 히, 힘들었어요... 저장고 쪽 소식에 자꾸 손이 떨려서요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 무, 무서웠어요... 저장고에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 무, 무서웠어요... 저장고에서요. 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 봐, 봤어요... 저장고에서요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 봐, 봤어요... 저장고에서요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 무, 무서웠어요... 저장고에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 무, 무서웠어요... 저장고에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 시, 심장이 멎는 줄 알았어요... 제, 제 눈앞이었어요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)] + 조각[여는말]</sub>
- 그쯤 저장고에서... 그, 그걸 봤어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 저, 저 봤어요... 저장고에서요. 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서... 그, 그걸 봤어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.missed(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 저, 저는 못 봤어요... <sub>틀: nosight</sub>
- 수상한 사람이요...? 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요...? 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘)</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘)</sub>
- 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘)</sub>
- 그, 수상한 사람이요...? 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저장고요... 여우 씨가 가, 같이 있었어요... 물어보셔도 돼요... <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... 여, 여우 씨는... 가끔 웃는 게 좀 무서워요. <sub>틀: WhereAtIncident.with + 조각[인상]</sub>
- 저장고요... 여우 씨가 가, 같이 있었어요... 물어보셔도 돼요... <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... <sub>틀: WhereAtIncident.with</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... <sub>틀: WhereAtIncident.with</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... <sub>틀: WhereAtIncident.with</sub>
- 저장고요... 여우 씨가 가, 같이 있었어요... 물어보셔도 돼요... <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 저장고요... 여우 씨가 가, 같이 있었어요... 물어보셔도 돼요... <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... <sub>틀: WhereAtIncident.with</sub>
- 그, 그때는 여우 씨랑 저장고에 있었어요... <sub>틀: WhereAtIncident.with</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 여우 씨랑 있었어요... 제, 제가 뭘 잘못했나요...? <sub>틀: Companion.with(닫힘)</sub>
- 여우 씨요... 가, 같이 있었어요... <sub>틀: Companion.with(닫힘)</sub>
- 여, 옆에 여우 씨가 계셨어요... 그래서 조금 덜 무서웠어요... <sub>틀: Companion.with(닫힘)</sub>
- 그, 그때는 여우 씨랑 있었어요... 왜 물어보시는 거예요...? <sub>틀: Companion.with(닫힘)</sub>
- 저장고에 여우 씨가 계, 계셨어요... 여, 여우 씨는... 가끔 웃는 게 좀 무서워요. <sub>틀: Companion.with + 조각[인상]</sub>
- 가, 같이 있던 분은... 여우 씨예요... <sub>틀: Companion.with(닫힘)</sub>
- 여우 씨랑 있었어요... 제, 제가 뭘 잘못했나요...? <sub>틀: Companion.with(닫힘)</sub>
- 여우 씨랑 있었어요... 제, 제가 뭘 잘못했나요...? <sub>틀: Companion.with(닫힘)</sub>
- 여, 옆에 여우 씨가 계셨어요... 그래서 조금 덜 무서웠어요... <sub>틀: Companion.with(닫힘)</sub>
- 가, 같이 있던 분은... 여우 씨예요... <sub>틀: Companion.with(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 저장고에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 아, 아까 말씀드린 그대로예요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 안 바뀌어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 벼, 변한 건 없어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 안 바뀌어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 그, 그대로예요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 불안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 긴장됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '잘 모르겠음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '신경 쓰임'이라고 적으셨습니다. 이유가 무엇입니까?  

- 오, 오늘은 조용해서요... <sub>틀: MoodReason.calm</sub>
- 사, 사고가 났잖아요... 지금도 무서워요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 저장고에서 그런 일이 있었잖아요... 무, 무서워서요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 저장고에서 그런 일이 있었잖아요... 무, 무서워서요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 사, 사고가 났잖아요... 지금도 무서워요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 사, 사고가 났잖아요... 지금도 무서워요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 저장고 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>
- 사, 사고가 났잖아요... 지금도 무서워요... <sub>틀: MoodReason.incident(닫힘)</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 아, 아뇨... 일하다가요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 아, 아뇨... 일하다가요... <sub>틀: MoodBefore.no(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 그, 저요...? 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 저요...? 아, 아니에요... 저 정말 아무것도 안 했어요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 저요...? 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 제, 제가 그런 걸 어떻게 해요... 무, 무슨 일 있어요...? <sub>틀: deny + 조각[되묻기]</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 봐, 봤어요... 저장고에서요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 제, 제가 아까 뭐 틀리게 말했나요...? 스, 스스로도 믿기지 않아요... 저장고에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 봐, 봤어요... 저장고에서요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 제, 제가 아까 뭐 틀리게 말했나요...? 바, 바로 앞이었어요... 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 봐, 봤어요... 저장고에서요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 무, 무서웠어요... 저장고에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 다, 다시 말씀드리면요, 제, 제 눈앞이었어요... 기, 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 스, 스스로도 믿기지 않아요... 저장고에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 저장고에서... 기, 기계가 멈추는 걸 봤어요... 무, 무서웠어요... <sub>틀: incident.direct(닫힘)</sub>
- 저장고에서... 그, 그걸 봤어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘)</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 그, 그냥 서 있어요... 괜찮은 건가요...? <sub>틀: status.idle(닫힘)</sub>
- 저, 저 뭐 하면 될까요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 소, 손이 비었어요... 제가 할 수 있는 거 있을까요...? <sub>틀: status.idle(닫힘)</sub>
- 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 제, 제 눈으로 본 건 아니라서요... 여, 옆방이었어요... 쿠, 쿵 하는 소리가 났어요. 지금도 떨려요... <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>
- 소, 소리가 너무 커서... 그, 그러니까 정비실 쪽에서 쿠, 쿵 하는 소리가 났어요. 제, 제 눈으로 본 건 아니라서요... <sub>틀: incident.indirect + 조각[여는말, 보정]</sub>
- 그, 그러니까 정비실 쪽에서 무, 뭔가 부서지는 소리가 들렸어요. 제, 제 눈으로 본 건 아니라서요... 전, 전화드렸었는데 바쁘셨나 봐요. <sub>틀: incident.indirect + 기억[mem.call.missed] + 조각[보정]</sub>
- 소, 소리만 들었어요... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 소, 소리가 너무 커서... 그, 그러니까 정비실 쪽에서 무, 뭔가 부서지는 소리가 들렸어요. 제, 제 눈으로 본 건 아니라서요... <sub>틀: incident.indirect + 조각[여는말, 보정]</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 벼, 벽이 울렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소, 소리만 들었어요... 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 쿠, 쿵 하는 소리가 났어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 벼, 벽이 울렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 무, 뭔가 부서지는 소리가 들렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 무, 무서웠어요... 정비실에서 사고가 났잖아요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 오, 오늘은... 정비실 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 오, 오늘은... 정비실 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 정비실 일 때문에... 저, 정신이 없었어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 정비실 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 정비실 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 오, 오늘은... 정비실 때문에 계속 조마조마했어요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 정비실 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘)</sub>
- 저, 정신없었어요... 정비실에서 사고가 나서요... <sub>틀: status.busy(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 정비실 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 벽 너머였어요... 저, 정말 무서웠어요. 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)]</sub>
- 그, 그러니까 정비실 쪽에서 쿠, 쿵 하는 소리가 났어요. 지, 직접 본 건 아니에요... <sub>틀: incident.indirect + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>
- 지, 직접 본 건 아니에요... 드, 들렸어요... 무, 뭔가 부서지는 소리가 들렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 벼, 벽이 울렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 지, 직접 본 건 아니에요... 조금 전에 정비실 쪽이었어요... 그, 그게... 벼, 벽이 울렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 지, 직접 본 건 아니에요... 드, 들렸어요... 벼, 벽이 울렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 기억[mem.call.missed(잘림)] + 조각[보정]</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수상한 사람이요...? 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘)</sub>
- 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘)</sub>
- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘)</sub>
- 그, 수상한 사람이요...? 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 사람이요...? 저, 저는 못 봤어요... <sub>틀: nosight + 조각[여는말]</sub>
- 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘)</sub>
- 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘)</sub>
- 모, 못 봤어요... 제가 겁이 많아서 잘 못 둘러봤지만요... <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 저, 전혀 몰랐어요... <sub>틀: IncidentKnown.none</sub>
- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 발전실에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 저, 저는 저장고에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 저장고에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저장고에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 저장고엔 저, 저 혼자였어요... 많이 무서웠어요... <sub>틀: Companion.alone(닫힘)</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 아, 아무도 없었어요... <sub>틀: Companion.alone</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 저장고엔 저, 저 혼자였어요... 많이 무서웠어요... <sub>틀: Companion.alone(닫힘)</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 저장고에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 저장고에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 벼, 변한 건 없어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 그, 그대로예요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 그, 그대로예요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 벼, 변한 건 없어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 안 바뀌어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 아까 말씀드린 그대로예요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 벼, 변한 건 없어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 아, 안 바뀌어요... 저장고였어요... <sub>틀: Restate.same(닫힘)</sub>
- 또, 똑같아요... 저장고에 있었어요... <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '신경 쓰임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '걱정됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 불안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '잘 모르겠음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 정비실에서 그런 일이 있었잖아요... 무, 무서워서요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 사, 사고가 났잖아요... 지금도 무서워요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 정비실 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>
- 정비실 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 정비실 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>
- 정비실에서 그런 일이 있었잖아요... 무, 무서워서요... <sub>틀: MoodReason.incident(닫힘)</sub>
- 정비실 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 정비실 일이 자, 자꾸 생각나서요... <sub>틀: MoodReason.incident</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 아, 아뇨... 일하다가요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 처, 처음엔 괜찮았는데... 중간부터요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 아, 아뇨... 일하다가요... <sub>틀: MoodBefore.no(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 아, 아뇨... 일하다가요... <sub>틀: MoodBefore.no(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 저요...? 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 제, 제가 그런 걸 어떻게 해요... 무, 무슨 일 있어요...? <sub>틀: deny + 조각[되묻기]</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 제, 제가 아까 뭐 틀리게 말했나요...? 소, 소리만 들었어요... 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 벼, 벽이 울렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 벽 너머였어요... 저, 정말 무서웠어요. 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘)</sub>
- 제, 제 눈으로 본 건 아니라서요... 드, 들렸어요... 벼, 벽이 울렸어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 제, 제 눈으로 본 건 아니라서요... 정비실 쪽이었어요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 다, 다시 말씀드리면요, 지, 직접 보진 못했어요... 정비실 쪽에서 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 지, 직접 본 건 아니에요... 드, 들렸어요... 쿠, 쿵 하는 소리가 났어요. 뭐였는지는 모르겠어요... <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 다, 다시 말씀드리면요, 지, 직접 보진 못했어요... 정비실 쪽에서 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 제, 제가 아까 뭐 틀리게 말했나요...? 지, 직접 보진 못했어요... 정비실 쪽에서 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 그, 그러니까 정비실 쪽에서 벼, 벽이 울렸어요. 제, 제 눈으로 본 건 아니라서요... <sub>틀: incident.indirect + 조각[여는말, 보정]</sub>

### 수신 전화 · 사고 신고

Q: (직원이 전화를 건다)  

- 발전실 쪽이요... 그, 그게... 쿠, 쿵 하는 소리가 났어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 쿠, 쿵 하는 소리가 났어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 무, 뭔가 부서지는 소리가 들렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 벼, 벽이 울렸어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실 쪽이요... 그, 그게... 쿠, 쿵 하는 소리가 났어요. <sub>틀: call.prefix / report.direct · report.indirect</sub>

### 수신 전화 · 가라는 지시에

Q: 확인하러 가주세요.  

- 아, 알겠어요... 가, 가볼게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 아, 알겠어요... 가, 가볼게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>
- 제, 제가요...? 무섭긴 한데... 필요하면 할게요... <sub>틀: accept</sub>

### 수신 전화 · 대기 지시에

Q: 지금 자리에서 대기하세요.  

- 여, 여기 있을게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>
- 그, 그럴게요... 무슨 일 있으면 바로 말씀드릴게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>
- 그, 그럴게요... 무슨 일 있으면 바로 말씀드릴게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>
- 그, 그럴게요... 무슨 일 있으면 바로 말씀드릴게요... <sub>틀: decline</sub>
- 여, 여기 있을게요... <sub>틀: decline</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 저, 저 뭐 하면 될까요...? <sub>틀: status.idle(닫힘)</sub>
- 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘)</sub>
- 소, 손이 비었어요... 제가 할 수 있는 거 있을까요...? <sub>틀: status.idle(닫힘)</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 지금이요...? 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 지, 지금 비어 있어요... 제가 놀고 있는 건 아니에요... <sub>틀: status.idle(닫힘)</sub>
- 그, 그냥 서 있어요... 괜찮은 건가요...? <sub>틀: status.idle(닫힘)</sub>
- 그, 지금이요...? 대, 대기 중이에요... 불러주시면 갈게요... <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 하, 할 일이 없어요... 뭐라도 해야 할까요...? <sub>틀: status.idle(닫힘)</sub>
- 저, 저 뭐 하면 될까요...? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘)</sub>
- 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 저, 저는 이상한 거 못 봤어요... 뭐, 뭔가 있으면 바로 말씀드릴게요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 그, 이상현상이요...? 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 저, 저는 이상한 거 못 봤어요... 뭐, 뭔가 있으면 바로 말씀드릴게요... <sub>틀: noanomaly + 조각[덧붙임]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>
- 조, 조용했어요... 다행이에요... <sub>틀: status.quiet(닫힘)</sub>
- 조, 조용했어요... 다행이에요... <sub>틀: status.quiet(닫힘)</sub>
- 무, 무사히 지나갔어요... 다행히요... <sub>틀: status.quiet(닫힘)</sub>
- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>
- 무, 무사히 지나갔어요... 다행히요... <sub>틀: status.quiet(닫힘)</sub>
- 벼, 별일 없었어요... 제가 모르는 일만 없다면요... <sub>틀: status.quiet(닫힘)</sub>
- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>
- 벼, 별일 없었어요... 제가 모르는 일만 없다면요... <sub>틀: status.quiet(닫힘)</sub>
- 그, 그냥 평소처럼 일했어요... <sub>틀: status.quiet</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 이상현상이요...? 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 그, 이상현상이요...? 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 저, 저는 이상한 거 못 봤어요... <sub>틀: noanomaly</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수, 수상한 분은 없었던 것 같아요... 괘, 괜히 제가 누굴 의심하게 될까 봐 무서워요... <sub>틀: nosight + 조각[덧붙임]</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>
- 누, 누굴 의심할 만한 건... 못 봤어요... <sub>틀: nosight(닫힘)</sub>
- 따, 딱히 이상한 분은... 없었던 것 같아요... <sub>틀: nosight(닫힘)</sub>
- 저, 저는 못 봤어요... <sub>틀: nosight</sub>
- 제, 제가 본 데서는 없었어요... 제가 다 본 건 아니지만요... <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 마, 말씀 듣고 처음 알았어요... 괘, 괜찮은 거죠...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그, 그런 일이 있었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 저장고에서요...? 드, 듣지 못했어요... <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 저, 전혀 몰랐어요... <sub>틀: IncidentKnown.none</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>
- 모, 몰랐어요... 무슨 일이었어요...? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처, 처음 들어요... <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저, 저는 통로에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저, 저는 통로에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 통로에... 저 혼자였어요... 그, 그게 이상한가요...? <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 저, 저는 통로에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 통로에 혼자... 이, 있었어요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 통로엔 저, 저 혼자였어요... 많이 무서웠어요... <sub>틀: Companion.alone(닫힘)</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>
- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>
- 통로엔 저, 저 혼자였어요... 많이 무서웠어요... <sub>틀: Companion.alone(닫힘)</sub>
- 마, 말할 사람이 없었어요... 혼자라서요... <sub>틀: Companion.alone(닫힘)</sub>
- 그, 그때는 저밖에 없었어요... <sub>틀: Companion.alone</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 통로에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 통로에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 통로에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>
- 트, 특별한 건 없었어요... <sub>틀: BeforeIncident.plain</sub>
- 통로에서 조, 조용히 있었어요... <sub>틀: BeforeIncident.plain</sub>

### 기분 · 이유

Q: 오늘 기분을 '신경 쓰임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '조금 불안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '잘 모르겠음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '걱정됨'이라고 적으셨습니다. 이유가 무엇입니까?  

- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 크, 큰일이 없어서요... 다행이었어요... <sub>틀: MoodReason.calm(닫힘)</sub>
- 크, 큰일이 없어서요... 다행이었어요... <sub>틀: MoodReason.calm(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 트, 특별한 이유는 없어요... <sub>틀: MoodReason.plain</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>
- 그, 그냥요... 잘 모르겠어요... <sub>틀: MoodReason.plain(닫힘)</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 여, 여기 오기 전부터요... 신경 쓰이셨다면 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 오, 올 때부터 그랬어요... <sub>틀: MoodBefore.yes</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 여, 여기 오기 전부터요... 신경 쓰이셨다면 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 여, 여기 오기 전부터요... 신경 쓰이셨다면 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 워, 원래 그래요... 죄송해요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 마, 맞아요... 출근할 때부터 그랬어요... <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그, 그랬어요... 근무 전부터요... <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 그, 저요...? 아, 아니에요... 저 정말 아무것도 안 했어요... <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 아, 아니에요... 저 정말 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 아, 아니에요... 저 정말 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 제, 제가 그런 걸 어떻게 해요... 무, 무슨 일 있어요...? <sub>틀: deny + 조각[되묻기]</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 무, 무서워요... 정말 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 자, 잘못 보신 거예요... 저 아니에요... <sub>틀: deny(닫힘)</sub>
- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>
- 제, 제가 그런 걸 어떻게 해요... 무, 무슨 일 있어요...? <sub>틀: deny + 조각[되묻기]</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 다, 다시 말씀드리면요, 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 제, 제가 아까 뭐 틀리게 말했나요...? 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 제, 제 쪽은 조용했어요... 그래서 오히려 좀 무서웠어요... <sub>틀: noanomaly(닫힘)</sub>
- 다, 다시 말씀드리면요, 없, 없었던 것 같아요... 제가 놓친 게 아니라면요... <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 다, 다시 말씀드리면요, 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘) + 조각[여는말]</sub>
- 딱, 딱히 없었어요... 혹시 제가 뭘 놓쳤나요...? <sub>틀: noanomaly(닫힘)</sub>

## 결번 장면 — 양

- 양은 저장고 배치 → 22:30 몰래 코어실 → 22:33 방해공작 → 22:38 복귀 → 22:42 코어실 고장.
- 결번 개체는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 코어실 쪽이 계속 신경 쓰여서... 소, 손이 잘 안 갔어요... <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 제, 제 눈으로 본 건 아니라서요... 코어실 쪽이었어요... 그, 그게... 쿠, 쿵 하는 소리가 났어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 그, 그런 분은 못 봤어요... 다들 자기 일 하시는 것 같았어요... <sub>틀: nosight(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 33분경 당신은 어디에 있었습니까?  

- 저, 저는 저장고에 혼자 있었어요... 증명해 줄 사람이 없어서... 죄, 죄송해요... <sub>틀: WhereAtIncident.alone(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 펴, 평소처럼 일하고 있었어요... <sub>틀: BeforeIncident.plain</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 33분경 함께 있던 직원이 있었습니까?  

- 누, 누구도 없었어요... 조용했어요... <sub>틀: Companion.alone(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 33분경 코어실에서 난 이 사고를 알고 있었습니까?  

- 소, 소리만 들었어요... 뭔지는 몰라요... <sub>틀: IncidentKnown.indirect(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 매, 맹세해요... 저는 아무것도 안 했어요... <sub>틀: deny(닫힘)</sub>

