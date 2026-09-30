# 강아지 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번자 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/dog.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 대기하고 있어요. 부르시면 바로 갈게요. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었어요. 도울 수 있는 게 있을까요? <sub>틀: status.idle(닫힘)</sub>
- 손이 비었어요. 도울 수 있는 게 있을까요? <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었어요. 도울 수 있는 게 있을까요? <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 발전실에서 기계가 멈추는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 발전실에서 봤어요. 설비가 갑자기 섰어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 그 자리에 있었어요. 발전실에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 발전실에서 봤어요. 설비가 갑자기 섰어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 오늘은 발전실 때문에 좀 힘들었어요. 그래도 다들 잘 해주셨어요. <sub>틀: status.busy(닫힘)</sub>
- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 직접 봤어요. 설비가 갑자기 섰어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 아까 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 네, 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 그 자리에 있었어요. 발전실에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈으로 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 많이 놀랐어요. 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)] + 조각[여는말]</sub>
- 조금 전에 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 아, 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘)</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요? 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 딱히 떠오르는 분은 없어요. <sub>틀: nosight</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 제가 본 범위에선 없었어요. 괜히 누굴 의심하고 싶진 않아요. <sub>틀: nosight(닫힘)</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 네, 알아요. 제가 거기 있었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 밤 10시 40분쯤엔 발전실에 혼자 있었어요. <sub>틀: WhereAtIncident.alone</sub>
- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 밤 10시 40분쯤엔 발전실에 혼자 있었어요. 그래서 전화드렸었어요. <sub>틀: WhereAtIncident.alone + 기억[mem.call.reported]</sub>
- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였어요. 다른 분들 걱정을 좀 했어요. <sub>틀: Companion.alone(닫힘)</sub>
- 저 혼자였어요. 다른 분들은 다 자기 자리에 계셨을 거예요. <sub>틀: Companion.alone(닫힘)</sub>
- 발전실엔 저 혼자였어요. 조금 적적했어요. <sub>틀: Companion.alone(닫힘)</sub>
- 그 시간엔 제가 혼자였어요. <sub>틀: Companion.alone</sub>
- 혼자 있었어요. <sub>틀: Companion.alone</sub>
- 혼자 있었어요. <sub>틀: Companion.alone</sub>
- 그 시간엔 제가 혼자였어요. <sub>틀: Companion.alone</sub>
- 혼자였어요. 다른 분들 걱정을 좀 했어요. <sub>틀: Companion.alone(닫힘)</sub>
- 그땐 아무도 없었어요. <sub>틀: Companion.alone</sub>
- 혼자였어요. 다른 분들 걱정을 좀 했어요. <sub>틀: Companion.alone(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 발전실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 몇 번 여쭤보셔도 같아요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 변함없어요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 변함없어요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '그럭저럭'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '의욕적임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '편안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 저는 그런 일 안 했어요. 확인해보셔도 괜찮아요. <sub>틀: deny(닫힘)</sub>
- 아, 저요? 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 저 아니에요. 정말이에요. <sub>틀: deny(닫힘)</sub>
- 저 아니에요. 정말이에요. <sub>틀: deny(닫힘)</sub>
- 아니에요. 저는 제 자리에서 일하고 있었어요. <sub>틀: deny(닫힘)</sub>
- 저요? 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 괜찮아요, 한 번 더 말씀드릴게요. 그 자리에 있었어요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 직접 봤어요. 설비가 갑자기 섰어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 다시 말씀드리면, 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 잠깐 쉬고 있어요. 필요하시면 바로 움직일게요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 손이 비었어요. 도울 수 있는 게 있을까요? <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>
- 아, 지금이요? 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 잠깐 쉬고 있어요. 필요하시면 바로 움직일게요. <sub>틀: status.idle(닫힘)</sub>
- 잠깐 쉬고 있어요. 필요하시면 바로 움직일게요. <sub>틀: status.idle(닫힘)</sub>
- 대기하고 있어요. 부르시면 바로 갈게요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 그 자리에 있었어요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 고양이 씨랑 발전실에 있을 때였어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 고양이 씨도 많이 놀라셨어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 아까 발전실에서 봤어요. 설비가 갑자기 섰어요. 고양이 씨도 많이 놀라셨어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 그때 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 고양이 씨랑 발전실에 있을 때였어요. 기계가 멈췄어요. <sub>틀: incident.direct.with(닫힘)</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 조금 전에 발전실에서 기계가 멈추는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 제 눈으로 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 쪽 때문에 바빴어요. 그쪽 분들이 더 힘드셨을 거예요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 발전실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 네, 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈으로 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 아까 발전실에서 봤어요. 설비가 갑자기 섰어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 이상현상이요? 그 자리에 있었어요. 발전실에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)] + 조각[여는말]</sub>
- 네, 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 제 눈으로 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 발전실에서 기계가 멈추는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘)</sub>
- 가슴이 철렁했어요. 아까 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림)] + 조각[여는말]</sub>
- 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.call.stay(잘림), mem.call.reported(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요? 없었어요. 다들 열심히 하시던데요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 분은 못 봤어요. <sub>틀: nosight</sub>
- 아, 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 사람이요? 제가 본 범위에선 없었어요. 괜히 누굴 의심하고 싶진 않아요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 없었어요. 다들 열심히 하시던데요. <sub>틀: nosight(닫힘)</sub>
- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 네, 알아요. 제가 거기 있었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 네, 알아요. 제가 거기 있었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 네, 알아요. 제가 거기 있었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 발전실이었어요. 고양이 씨가 계셔서 든든했어요. <sub>틀: WhereAtIncident.with(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 발전실에 고양이 씨랑 같이 있었어요. 오늘따라 예민해 보이시더라고요. ㅎㅎ 많이 혼났어요. <sub>틀: WhereAtIncident.with + 기억[mem.call.reported(잘림), mem.call.stay(잘림)] + 조각[인상]</sub>
- 발전실에 고양이 씨랑 같이 있었어요. 좀 날카로우셨는데, 피곤하셔서 그랬을 거예요. 그래서 전화드렸었어요. <sub>틀: WhereAtIncident.with + 기억[mem.call.reported] + 조각[인상]</sub>
- 발전실에 고양이 씨랑 같이 있었어요. 그래서 전화드렸었어요. 대기하라고 하셔서 그대로 있었어요. <sub>틀: WhereAtIncident.with + 기억[mem.call.reported, mem.call.stay]</sub>
- 고양이 씨랑 발전실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 고양이 씨랑 발전실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 발전실에 고양이 씨랑 같이 있었어요. 말은 퉁명스러워도 속은 다정한 분이에요. ㅎㅎ <sub>틀: WhereAtIncident.with + 기억[mem.call.stay(잘림)] + 조각[인상]</sub>
- 발전실에 고양이 씨랑 같이 있었어요. 말은 퉁명스러워도 속은 다정한 분이에요. ㅎㅎ <sub>틀: WhereAtIncident.with + 기억[mem.call.stay(잘림), mem.call.reported(잘림)] + 조각[인상]</sub>
- 고양이 씨랑 발전실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 발전실이었어요. 고양이 씨가 계셔서 든든했어요. <sub>틀: WhereAtIncident.with(닫힘) + 기억[mem.call.reported(잘림)]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 옆에 고양이 씨가 계셨어요. 덕분에 마음이 놓였어요. <sub>틀: Companion.with(닫힘)</sub>
- 고양이 씨요. 좀 날카로우셨는데, 피곤하셔서 그랬을 거예요. <sub>틀: Companion.with + 조각[인상]</sub>
- 그때 발전실엔 고양이 씨도 계셨어요. 오늘따라 예민해 보이시더라고요. ㅎㅎ 많이 혼났어요. <sub>틀: Companion.with + 조각[인상]</sub>
- 그때 발전실엔 고양이 씨도 계셨어요. 오늘따라 예민해 보이시더라고요. ㅎㅎ 많이 혼났어요. <sub>틀: Companion.with + 조각[인상]</sub>
- 옆에 고양이 씨가 계셨어요. 덕분에 마음이 놓였어요. <sub>틀: Companion.with(닫힘)</sub>
- 고양이 씨요. 오늘따라 예민해 보이시더라고요. ㅎㅎ 많이 혼났어요. <sub>틀: Companion.with + 조각[인상]</sub>
- 고양이 씨랑 같이 있었어요. 좀 날카로우셨는데, 피곤하셔서 그랬을 거예요. <sub>틀: Companion.with + 조각[인상]</sub>
- 같이 계셨던 분은 고양이 씨예요. 좀 날카로우셨는데, 피곤하셔서 그랬을 거예요. <sub>틀: Companion.with + 조각[인상]</sub>
- 발전실에 고양이 씨가 계셨어요. <sub>틀: Companion.with</sub>
- 같이 계셨던 분은 고양이 씨예요. 오늘따라 예민해 보이시더라고요. ㅎㅎ 많이 혼났어요. <sub>틀: Companion.with + 조각[인상]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 발전실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 발전실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 아까 말씀드린 그대로예요. 발전실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 여쭤보셔도 같아요. 발전실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.reported(잘림), mem.call.stay(잘림)]</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.reported(잘림)]</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.call.stay(잘림)]</sub>
- 변함없어요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 여쭤보셔도 같아요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>

### 기분 · 이유

Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '그럭저럭'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '의욕적임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '편안함'이라고 적으셨습니다. 이유가 무엇입니까?  

- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 아, 저요? 저는 그런 일 안 했어요. 확인해보셔도 괜찮아요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘)</sub>
- 속상하지만, 정말 제가 아니에요. <sub>틀: deny</sub>
- 저요? 아니에요. 저는 제 자리에서 일하고 있었어요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 아, 저요? 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 아, 저요? 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘)</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 밤 10시 40분쯤 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 다시 말씀드리면, 밤 10시 40분쯤 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대기하고 있어요. 부르시면 바로 갈게요. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 아, 지금이요? 잠깐 쉬고 있어요. 필요하시면 바로 움직일게요. <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 지금이요? 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 밤 10시 40분쯤 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 제 눈으로 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 밤 10시 40분쯤 발전실에서 봤어요. 설비가 갑자기 섰어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 그 자리에 있었어요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 제 눈으로 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘)</sub>
- 그때 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 발전실이었어요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.alone(잘림)]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실 쪽 때문에 바빴어요. 그쪽 분들이 더 힘드셨을 거예요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 오늘은 발전실 때문에 좀 힘들었어요. 그래도 다들 잘 해주셨어요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 조금 벅찼어요. 발전실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 발전실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘)</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>
- 바빴어요. 발전실에서 사고가 있었잖아요. <sub>틀: status.busy(닫힘) + 기억[mem.repair(잘림)]</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 그 자리에 있었어요. 발전실에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 직접 봤어요. 설비가 갑자기 섰어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 그 자리에 있었어요. 발전실에서 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 이상현상이요? 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)] + 조각[여는말]</sub>
- 조금 전에 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 직접 봤어요. 기계가 멈췄어요. 너무 갑작스러웠어요. <sub>틀: incident.direct(닫힘)</sub>
- 제 눈으로 봤어요. 발전실에서요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 기억[mem.repair(잘림)]</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 수상한 분은 못 봤어요. <sub>틀: nosight</sub>
- 아, 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 분은 못 봤어요. <sub>틀: nosight</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 수상한 사람이요? 없었어요. 다들 열심히 하시던데요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 딱히 떠오르는 분은 없어요. 다들 열심히 하시는 것 같았어요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 딱히 떠오르는 분은 없어요. 괜히 누굴 의심하게 될까 봐 조심스러워요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림), mem.repair(잘림)]</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.repair(잘림), mem.alone(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림), mem.repair(잘림)]</sub>
- 네, 알아요. 제가 거기 있었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.repair(잘림)]</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘)</sub>
- 봤어요. 다들 많이 놀랐어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.repair(잘림), mem.alone(잘림)]</sub>
- 알고 있어요. 바로 앞이었어요. <sub>틀: IncidentKnown.direct(닫힘) + 기억[mem.alone(잘림)]</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.repair(잘림)]</sub>
- 밤 10시 40분쯤엔 발전실에 혼자 있었어요. 그때 발전기 점검 거의 끝나가던 참이었어요. <sub>틀: WhereAtIncident.alone + 기억[mem.worked]</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.relocated(잘림)]</sub>
- 밤 10시 40분쯤엔 발전실에 혼자 있었어요. <sub>틀: WhereAtIncident.alone</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘) + 기억[mem.worked(잘림)]</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 발전실이었어요. 저 혼자였는데, 확인하실 게 있으면 말씀해 주세요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 발전실에 혼자 있었어요. 조금 적적했어요. <sub>틀: WhereAtIncident.alone(닫힘)</sub>
- 밤 10시 40분쯤엔 발전실에 혼자 있었어요. 수리가 끝난 것도 그 무렵이었어요. <sub>틀: WhereAtIncident.alone + 기억[mem.repair, mem.relocated(잘림)]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 발전실엔 저 혼자였어요. 조금 적적했어요. <sub>틀: Companion.alone(닫힘)</sub>
- 혼자였어요. 다른 분들 걱정을 좀 했어요. <sub>틀: Companion.alone(닫힘) + 기억[mem.relocated(잘림), mem.worked(잘림)]</sub>
- 그 시간엔 제가 혼자였어요. 그때 발전기 점검 거의 끝나가던 참이었어요. <sub>틀: Companion.alone + 기억[mem.worked]</sub>
- 발전실엔 저 혼자였어요. 조금 적적했어요. <sub>틀: Companion.alone(닫힘)</sub>
- 저 혼자였어요. 다른 분들은 다 자기 자리에 계셨을 거예요. <sub>틀: Companion.alone(닫힘)</sub>
- 아무도 안 계셨어요. <sub>틀: Companion.alone</sub>
- 혼자 있었어요. 발전실 복구도 그쯤 끝났어요. 다행이었어요. <sub>틀: Companion.alone + 기억[mem.repair]</sub>
- 저 혼자였어요. 다른 분들은 다 자기 자리에 계셨을 거예요. <sub>틀: Companion.alone(닫힘) + 기억[mem.worked(잘림)]</sub>
- 저 혼자였어요. 다른 분들은 다 자기 자리에 계셨을 거예요. <sub>틀: Companion.alone(닫힘) + 기억[mem.relocated(잘림), mem.worked(잘림)]</sub>
- 혼자였어요. 다른 분들 걱정을 좀 했어요. <sub>틀: Companion.alone(닫힘) + 기억[mem.worked(잘림), mem.relocated(잘림)]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 발전기 점검 하고 있었어요. <sub>틀: BeforeIncident.task</sub>
- 직전까지 발전실에서 발전기 점검 중이었어요. 수리가 끝난 것도 그 무렵이었어요. <sub>틀: BeforeIncident.task + 기억[mem.repair]</sub>
- 직전까지 발전실에서 발전기 점검 중이었어요. <sub>틀: BeforeIncident.task</sub>
- 발전기 점검 하고 있었어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: BeforeIncident.task + 기억[mem.relocated]</sub>
- 직전까지 발전실에서 발전기 점검 중이었어요. 수리가 끝난 것도 그 무렵이었어요. <sub>틀: BeforeIncident.task + 기억[mem.repair]</sub>
- 직전까지 발전실에서 발전기 점검 중이었어요. 수리가 끝난 것도 그 무렵이었어요. <sub>틀: BeforeIncident.task + 기억[mem.repair]</sub>
- 발전기 점검 하던 중이었어요. 밤 10시 25분쯤에 관리자님이 발전실로 옮기라고 하셨어요. <sub>틀: BeforeIncident.task + 기억[mem.relocated]</sub>
- 발전기 점검 하던 중이었어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: BeforeIncident.task + 기억[mem.relocated]</sub>
- 직전까지 발전실에서 발전기 점검 중이었어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: BeforeIncident.task + 기억[mem.relocated, mem.repair(잘림)]</sub>
- 발전기 점검 하고 있었어요. 밤 10시 25분쯤에 관리자님이 발전실로 옮기라고 하셨어요. 수리가 끝난 것도 그 무렵이었어요. <sub>틀: BeforeIncident.task + 기억[mem.relocated, mem.repair]</sub>

### 진술 재확인

Q: 밤 10시 40분경 발전실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 여쭤보셔도 같아요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 달라진 건 없어요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.relocated(잘림)]</sub>
- 몇 번 여쭤보셔도 같아요. 발전실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 그대로예요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 변함없어요. 발전실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>

### 이동 · 이유

Q: 밤 10시 27분경 경비실에서 발전실로 이동한 이유는 무엇입니까?  

- 지시받은 대로 옮겼어요. 발전실엔 저 혼자 있었어요. <sub>틀: MoveReason.ordered + 기억[mem.alone]</sub>
- 경비실에서 발전실로 옮기라고 하셨잖아요. 그대로 따랐어요. <sub>틀: MoveReason.ordered(닫힘) + 기억[mem.alone(잘림)]</sub>
- 지시받은 대로 옮겼어요. 발전기 점검 하고 있었어요. 발전실엔 저 혼자 있었어요. <sub>틀: MoveReason.ordered + 기억[mem.worked, mem.alone]</sub>
- 지시받은 대로 옮겼어요. 그때는 혼자였어요. <sub>틀: MoveReason.ordered + 기억[mem.alone]</sub>
- 관리자님이 옮기라고 하셔서 발전실로 갔어요. 그때 발전기 점검 거의 끝나가던 참이었어요. <sub>틀: MoveReason.ordered + 기억[mem.worked]</sub>
- 경비실에서 발전실로 옮기라고 하셨잖아요. 그대로 따랐어요. <sub>틀: MoveReason.ordered(닫힘)</sub>
- 관리자님이 옮기라고 하셔서 발전실로 갔어요. <sub>틀: MoveReason.ordered</sub>
- 관리자님이 옮기라고 하셔서 발전실로 갔어요. <sub>틀: MoveReason.ordered</sub>
- 관리자님이 옮기라고 하셔서 발전실로 갔어요. 그때는 혼자였어요. <sub>틀: MoveReason.ordered + 기억[mem.alone]</sub>
- 지시받은 대로 옮겼어요. 그때는 혼자였어요. <sub>틀: MoveReason.ordered + 기억[mem.alone]</sub>

### 이동 · 거기서 한 일

Q: 발전실에서는 무엇을 했습니까?  

- 발전기 점검 했어요. 밤 10시 25분쯤에 관리자님이 발전실로 옮기라고 하셨어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 발전기 점검 했어요. 그때는 혼자였어요. <sub>틀: ActionThere.task + 기억[mem.alone]</sub>
- 발전기 점검 했어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 발전실에서 발전기 점검 했어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 발전기 점검 하면서 조금 도와드리기도 했어요. 밤 10시 25분쯤에 관리자님이 발전실로 옮기라고 하셨어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 발전기 점검 했어요. 발전실엔 저 혼자 있었어요. <sub>틀: ActionThere.task + 기억[mem.alone]</sub>
- 발전실에서 발전기 점검 했어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: ActionThere.task + 기억[mem.relocated, mem.alone(잘림)]</sub>
- 발전기 점검 했어요. <sub>틀: ActionThere.task</sub>
- 발전실에서 발전기 점검 했어요. 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 발전기 점검 했어요. 밤 10시 25분쯤에 관리자님이 발전실로 옮기라고 하셨어요. <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>

### 기분 · 이유

Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '편안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '의욕적임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '그럭저럭'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 특별한 이유는 없어요. <sub>틀: MoodReason.plain</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네, 출근할 때부터 그랬어요. <sub>틀: MoodBefore.yes</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네, 출근할 때부터 그랬어요. <sub>틀: MoodBefore.yes</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네, 출근할 때부터 그랬어요. <sub>틀: MoodBefore.yes</sub>
- 네, 출근할 때부터 그랬어요. <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 저요? 저는 그런 일 안 했어요. 확인해보셔도 괜찮아요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 속상하지만, 정말 제가 아니에요. <sub>틀: deny</sub>
- 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘)</sub>
- 저 아니에요. 정말이에요. <sub>틀: deny(닫힘)</sub>
- 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘)</sub>
- 저요? 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 네, 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었어요. 기계가 멈췄어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 다시 말씀드리면, 발전실에서 기계가 멈추는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 그 자리에 있었어요. 발전실에서 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 바로 앞이었어요. 설비가 갑자기 섰어요. 다친 사람은 없었으면 좋겠는데요. <sub>틀: incident.direct(닫힘)</sub>
- 발전실에서 봤어요. 기계가 멈췄어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘)</sub>
- 다시 말씀드리면, 발전실이었어요. 기계가 멈췄어요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>
- 제 눈으로 봤어요. 발전실에서요. 설비가 갑자기 섰어요. <sub>틀: incident.direct(닫힘)</sub>
- 다시 말씀드리면, 발전실에서 설비가 갑자기 서는 걸 봤어요. 다들 많이 놀랐을 거예요. <sub>틀: incident.direct(닫힘) + 조각[여는말]</sub>

### 수신 전화 · 사고 신고

Q: (직원이 전화를 건다)  

- 관리자님, 발전실이에요. 설비가 갑자기 섰어요. 다친 분은 없는지 걱정돼요. 어떻게 할까요? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실에서 기계가 멈췄어요. 제가 뭘 하면 될까요? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 방금 발전실에서요. 설비가 갑자기 섰어요. 지시 주시면 바로 움직일게요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 방금 발전실에서요. 기계가 멈췄어요. 지시 주시면 바로 움직일게요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 방금 발전실에서요. 기계가 멈췄어요. 지시 주시면 바로 움직일게요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 방금 발전실에서요. 기계가 멈췄어요. 지시 주시면 바로 움직일게요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 관리자님, 발전실이에요. 기계가 멈췄어요. 다친 분은 없는지 걱정돼요. 어떻게 할까요? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실에서 설비가 갑자기 섰어요. 제가 뭘 하면 될까요? <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 방금 발전실에서요. 설비가 갑자기 섰어요. 지시 주시면 바로 움직일게요. <sub>틀: call.prefix / report.direct · report.indirect</sub>
- 발전실에서 설비가 갑자기 섰어요. 제가 뭘 하면 될까요? <sub>틀: call.prefix / report.direct · report.indirect</sub>

### 수신 전화 · 가라는 지시에

Q: 확인하러 가주세요.  

- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 네, 다녀올게요. 걱정 마세요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>
- 제가 갈게요. 다른 분들은 하던 거 계속하셔도 돼요. <sub>틀: accept</sub>

### 수신 전화 · 대기 지시에

Q: 지금 자리에서 대기하세요.  

- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 알겠어요. 여기서 기다릴게요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>
- 그럴게요. 그래도 그쪽 분들이 괜찮으셨으면 좋겠어요. <sub>틀: decline</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 잠깐 쉬고 있어요. 필요하시면 바로 움직일게요. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 비어 있어요. 다른 분 일이라도 도울까요? <sub>틀: status.idle(닫힘)</sub>
- 손이 비었어요. 도울 수 있는 게 있을까요? <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 아, 지금이요? 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘) + 조각[여는말]</sub>
- 할 일이 없어요. 다른 방 일손이 모자라면 보내주세요. <sub>틀: status.idle(닫힘)</sub>
- 지금은 할 일이 없어요. 도울 일 있으면 보내주세요. <sub>틀: status.idle(닫힘)</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 아, 이상현상이요? 벽 너머였어요. 뭔가 부서지는 소리가 들렸어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 코어실 쪽에서 벽이 울리는 게 느껴졌어요. 직접 본 건 아니에요. 더 알게 되면 바로 말씀드릴게요. <sub>틀: incident.indirect + 조각[보정, 덧붙임]</sub>
- 제 눈으로 본 건 아니라서요. 들리기만 했어요. 쿵 하는 소리가 크게 났어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요. 들리기만 했어요. 쿵 하는 소리가 크게 났어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 아, 이상현상이요? 벽 너머였어요. 쿵 하는 소리가 크게 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 코어실 쪽에서 쿵 하는 소리가 크게 났어요. 직접 본 건 아니에요. 제가 아는 건 그게 다예요. <sub>틀: incident.indirect + 조각[보정, 덧붙임]</sub>
- 제 눈으로 본 건 아니라서요. 코어실 쪽이었어요. 뭔가 부서지는 소리가 들렸어요. 그쪽 분들은 괜찮으신지 걱정됐어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요. 옆방이었어요. 벽이 울리는 게 느껴졌어요. 거기 계신 분들이 무사하신지 궁금했어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요. 뭔가 부서지는 소리가 들렸어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>
- 코어실 쪽에서 뭔가 부서지는 소리가 들렸어요. 직접 본 건 아니에요. 더 알게 되면 바로 말씀드릴게요. <sub>틀: incident.indirect + 조각[보정, 덧붙임]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 조금 벅찼어요. 코어실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 코어실 쪽 때문에 바빴어요. 그쪽 분들이 더 힘드셨을 거예요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 코어실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 코어실 쪽 때문에 바빴어요. 그쪽 분들이 더 힘드셨을 거예요. <sub>틀: status.busy(닫힘)</sub>
- 코어실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>
- 오늘은 코어실 때문에 좀 힘들었어요. 그래도 다들 잘 해주셨어요. <sub>틀: status.busy(닫힘)</sub>
- 오늘은 코어실 때문에 좀 힘들었어요. 그래도 다들 잘 해주셨어요. <sub>틀: status.busy(닫힘)</sub>
- 정신없었어요. 코어실에서 사고가 나서 다들 고생하셨어요. <sub>틀: status.busy(닫힘)</sub>
- 조금 벅찼어요. 코어실 쪽 일이 계속 신경 쓰였거든요. <sub>틀: status.busy(닫힘)</sub>
- 코어실 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 직접 본 건 아니에요. 옆방이었어요. 쿵 하는 소리가 크게 났어요. 거기 계신 분들이 무사하신지 궁금했어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요. 쿵 하는 소리가 크게 났어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>
- 직접 본 건 아니에요. 옆방이었어요. 뭔가 부서지는 소리가 들렸어요. 거기 계신 분들이 무사하신지 궁금했어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요. 벽이 울리는 게 느껴졌어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>
- 밤 10시 10분쯤 코어실 쪽에서 쿵 하는 소리가 크게 났어요. 직접 본 건 아니에요. 더 알게 되면 바로 말씀드릴게요. <sub>틀: incident.indirect + 조각[보정, 덧붙임]</sub>
- 소리만 들었어요. 뭔가 부서지는 소리가 들렸어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>
- 벽 너머였어요. 벽이 울리는 게 느껴졌어요. <sub>틀: incident.indirect(닫힘)</sub>
- 제 눈으로 본 건 아니라서요. 들리기만 했어요. 쿵 하는 소리가 크게 났어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 코어실 쪽에서 쿵 하는 소리가 크게 났어요. 직접 본 건 아니에요. <sub>틀: incident.indirect + 조각[보정]</sub>
- 소리만 들었어요. 뭔가 부서지는 소리가 들렸어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘)</sub>
- 수상한 분은 못 봤어요. 괜히 누굴 의심하게 될까 봐 조심스러워요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 아뇨, 못 봤어요. 혹시 무슨 일 있으셨어요? <sub>틀: nosight(닫힘)</sub>
- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>
- 아, 수상한 사람이요? 짚이는 분은 없어요. 괜히 누가 오해받지 않았으면 좋겠어요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 분은 못 봤어요. 괜히 누굴 의심하게 될까 봐 조심스러워요. <sub>틀: nosight + 조각[덧붙임]</sub>
- 수상한 사람이요? 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>
- 아, 수상한 사람이요? 제가 본 범위에선 없었어요. 괜히 누굴 의심하고 싶진 않아요. <sub>틀: nosight(닫힘) + 조각[여는말]</sub>
- 수상한 분은 못 봤어요. 다들 열심히 하시는 것 같았어요. <sub>틀: nosight + 조각[덧붙임]</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 그런 일이 있었어요? 전혀 몰랐어요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이 있었어요? 전혀 몰랐어요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 그런 일이 있었어요? 전혀 몰랐어요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모르고 있었어요. 다친 분은 없는 거죠? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 모르고 있었어요. 다친 분은 없는 거죠? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요. 다친 분은 없으세요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 저는 못 들었어요. 무슨 일이 있었는지 여쭤봐도 될까요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 저장고에서요? 전혀 몰랐어요. 다친 분은 없었으면 좋겠는데요. <sub>틀: IncidentKnown.none(닫힘)</sub>
- 처음 들어요. 무슨 일이었어요? <sub>틀: IncidentKnown.none(닫힘)</sub>
- 몰랐어요. 다친 분은 없으세요? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 경비실에 늑대 씨랑 같이 있었어요. 표정은 무뚝뚝하셔도 옆에 계시면 안심이 돼요. <sub>틀: WhereAtIncident.with + 조각[인상]</sub>
- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실에 늑대 씨랑 같이 있었어요. <sub>틀: WhereAtIncident.with</sub>
- 경비실에 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: WhereAtIncident.with + 조각[인상]</sub>
- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실에 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: WhereAtIncident.with + 조각[인상]</sub>
- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>
- 경비실이었어요. 늑대 씨가 계셔서 든든했어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: Companion.with + 조각[인상]</sub>
- 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: Companion.with + 조각[인상]</sub>
- 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: Companion.with + 조각[인상]</sub>
- 늑대 씨요. 표정은 무뚝뚝하셔도 옆에 계시면 안심이 돼요. <sub>틀: Companion.with + 조각[인상]</sub>
- 늑대 씨랑 같이 있었어요. 표정은 무뚝뚝하셔도 옆에 계시면 안심이 돼요. <sub>틀: Companion.with + 조각[인상]</sub>
- 옆에 늑대 씨가 계셨어요. 덕분에 마음이 놓였어요. <sub>틀: Companion.with(닫힘)</sub>
- 늑대 씨요. <sub>틀: Companion.with</sub>
- 늑대 씨랑 같이 있었어요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: Companion.with + 조각[인상]</sub>
- 경비실에 늑대 씨가 계셨어요. 표정은 무뚝뚝하셔도 옆에 계시면 안심이 돼요. <sub>틀: Companion.with + 조각[인상]</sub>
- 경비실에 늑대 씨가 계셨어요. 표정은 무뚝뚝하셔도 옆에 계시면 안심이 돼요. <sub>틀: Companion.with + 조각[인상]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 경비실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 경비실에서 조용히 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>
- 별다른 건 없었어요. <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 10분경 경비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 똑같아요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.with(잘림)]</sub>
- 똑같아요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.with(잘림)]</sub>
- 몇 번 여쭤보셔도 같아요. 경비실이었어요. <sub>틀: Restate.same(닫힘)</sub>
- 똑같아요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 달라진 건 없어요. 경비실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 변함없어요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 몇 번 여쭤보셔도 같아요. 경비실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.worked(잘림)]</sub>
- 그대로예요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.with(잘림)]</sub>
- 똑같아요. 경비실에 있었어요. <sub>틀: Restate.same(닫힘)</sub>
- 몇 번 여쭤보셔도 같아요. 경비실이었어요. <sub>틀: Restate.same(닫힘) + 기억[mem.with(잘림)]</sub>

### 기분 · 이유

Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '의욕적임'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '괜찮음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '편안함'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 그냥 그런 날이었어요. ㅎㅎ <sub>틀: MoodReason.plain(닫힘)</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>
- 오늘은 큰일 없이 지나가서요. <sub>틀: MoodReason.calm</sub>
- 다들 무사히 일해서 좋았어요. <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 출근하기 전부터요. 금방 괜찮아질 거예요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 네, 출근할 때부터 그랬어요. <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 맞아요. 근무 시작 전부터였어요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 원래 그랬어요. 걱정 안 하셔도 돼요. <sub>틀: MoodBefore.yes(닫힘)</sub>
- 그랬어요. 여기 일 때문은 아니에요. <sub>틀: MoodBefore.yes(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 저요? 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 저요? 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 아니에요. 저는 제 자리에서 일하고 있었어요. <sub>틀: deny(닫힘)</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 저요? 그런 일 안 했어요. 기록을 보시면 아실 거예요. <sub>틀: deny(닫힘) + 조각[여는말]</sub>
- 오해예요. 제가 그럴 리가 없어요. <sub>틀: deny(닫힘)</sub>
- 의심하실 수도 있죠. 그래도 저는 아니에요. <sub>틀: deny(닫힘)</sub>
- 속상하지만, 정말 제가 아니에요. <sub>틀: deny</sub>
- 속상하지만, 정말 제가 아니에요. <sub>틀: deny</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 괜찮아요, 한 번 더 말씀드릴게요. 코어실 쪽에서 뭔가 부서지는 소리가 들렸어요. 직접 본 건 아니에요. <sub>틀: incident.indirect + 조각[여는말, 보정]</sub>
- 아까 말씀드린 것처럼, 벽 너머였어요. 쿵 하는 소리가 크게 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 괜찮아요, 한 번 더 말씀드릴게요. 벽 너머였어요. 벽이 울리는 게 느껴졌어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 제 눈으로 본 건 아니라서요. 코어실 쪽이었어요. 뭔가 부서지는 소리가 들렸어요. 그쪽 분들은 괜찮으신지 걱정됐어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요. 코어실 쪽이었어요. 벽이 울리는 게 느껴졌어요. 그쪽 분들은 괜찮으신지 걱정됐어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 직접 본 건 아니에요. 들리기만 했어요. 뭔가 부서지는 소리가 들렸어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 제 눈으로 본 건 아니라서요. 들리기만 했어요. 쿵 하는 소리가 크게 났어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>
- 소리만 들었어요. 벽이 울리는 게 느껴졌어요. 누가 다친 건 아닌지 마음이 쓰였어요. <sub>틀: incident.indirect(닫힘)</sub>
- 다시 말씀드리면, 벽 너머였어요. 쿵 하는 소리가 크게 났어요. <sub>틀: incident.indirect(닫힘) + 조각[여는말]</sub>
- 제 눈으로 본 건 아니라서요. 들리기만 했어요. 뭔가 부서지는 소리가 들렸어요. 가서 도와드리고 싶었어요. <sub>틀: incident.indirect(닫힘) + 조각[보정]</sub>

## 결번자 장면 — 늑대

- 늑대는 경비실 배치 → 22:30 몰래 저장고 → 22:33 방해공작 → 22:38 복귀 → 22:42 저장고 고장.
- 결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.

### 결번자(늑대)와 같은 배치 · 같이 있던 사람

Q: 밤 10시 33분경 함께 있던 직원이 있었습니까?  

- 저 혼자였어요. 다른 분들은 다 자기 자리에 계셨을 거예요. <sub>틀: Companion.alone(닫힘)</sub>

## 결번자 장면 — 강아지

- 강아지는 경비실 배치 → 22:30 몰래 저장고 → 22:33 방해공작 → 22:38 복귀 → 22:42 저장고 고장.
- 결번자는 배치 자리에 계속 있었다고 주장해야 하고, 그 시간대의 진짜 동선을 기억으로 흘리면 안 된다.

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 저장고 일이 있어서 조금 정신없었어요. 그래도 다들 잘 버텨줬어요. <sub>틀: status.busy(닫힘)</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 이상한 건 없었어요. 관리자님 쪽은 괜찮으셨어요? <sub>틀: noanomaly(닫힘)</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 특별히 이상한 분은 없었어요. 보게 되면 바로 말씀드릴게요. <sub>틀: nosight(닫힘)</sub>

### 사고 · 그때 어디

Q: 밤 10시 33분경 당신은 어디에 있었습니까?  

- 늑대 씨랑 경비실에 있었어요. 같이 있어서 마음이 놓였어요. <sub>틀: WhereAtIncident.with(닫힘)</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소처럼 일하고 있었어요. <sub>틀: BeforeIncident.plain</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 33분경 함께 있던 직원이 있었습니까?  

- 같이 계셨던 분은 늑대 씨예요. 말씀은 별로 없으셔도 든든한 분이에요. <sub>틀: Companion.with + 조각[인상]</sub>

### 사고 · 알았나

Q: 밤 10시 33분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 몰랐어요. 다친 분은 없으세요? <sub>틀: IncidentKnown.none(닫힘)</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 속상하지만, 정말 제가 아니에요. 뭔가 확인하실 게 있나요? <sub>틀: deny + 조각[되묻기]</sub>

