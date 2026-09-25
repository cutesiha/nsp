# 토끼 — 대사 샘플

`scenes/debug/DialogueSampleDump.tscn` 이 만든 파일입니다. 실행할 때마다 새로 뽑힙니다.
장면 1~4 는 장면을 10번 처음부터 다시 만들어 매번 같은 질문 묶음을 던진 결과입니다(질문 종류마다 답 10개). 결번자 장면은 한 번씩입니다.
각 답 뒤의 `틀:` 은 쓰인 문장 슬롯, `기억[...]` 은 근무 기억에서 덧붙인 슬롯(`(잘림)` = 답에 안 들어감)입니다.
어색한 줄을 찾으면 `data/dialogue/lines/rabbit.txt` 의 그 슬롯을 고치면 됩니다.

## 장면 1 — 혼자 배치

- 배치: 토끼=정비실 · 고양이=저장고 · 여우=코어실 · 양=의무실 · 늑대=경비실 · 강아지=발전실 (모두 혼자)
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 토끼에게 전화
- 22:40 발전실 설비 고장(강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(고양이 목격)
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>
- 할 게 없어요! 뭐든 시켜 주세요! <sub>틀: status.idle</sub>
- 지금 비었어요! 어디든 보내 주세요! <sub>틀: status.idle</sub>
- 놀고 있어요! 제 탓 아니에요! <sub>틀: status.idle</sub>
- 오, 지금이요? 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 오, 지금이요? 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>
- 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 지금 비었어요! 어디든 보내 주세요! <sub>틀: status.idle</sub>
- 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 이상현상이요? 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상현상이요? 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 이상현상이요? 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 평화로웠어요! 저 열심히 일했어요! <sub>틀: status.quiet</sub>
- 평화로웠어요! 저 열심히 일했어요! <sub>틀: status.quiet</sub>
- 평화로웠어요! 저 열심히 일했어요! <sub>틀: status.quiet</sub>
- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>
- 조용했어요! 좀 심심했지만 좋았어요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>
- 조용했어요! 좀 심심했지만 좋았어요! <sub>틀: status.quiet</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 없었어요! 오늘은 평화로웠어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 이상현상이요? 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 이상현상이요? 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 오, 수상한 사람이요? 아무도요! 다들 평소 같았어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 없었어요! 제가 좀 두리번거리는 편인데도요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 못 봤어요! 봤으면 벌써 말했죠! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 진짜 없었어요! 있었으면 제가 제일 먼저 알았을걸요! <sub>틀: nosight</sub>
- 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 아무도요! 다들 평소 같았어요! <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 전혀요! 발전실에서 그런 일이 있었어요? <sub>틀: IncidentKnown.none</sub>
- 어? 그런 일이 있었어요? 처음 들어요! <sub>틀: IncidentKnown.none</sub>
- 몰랐어요! 무슨 일이었어요? <sub>틀: IncidentKnown.none</sub>
- 전혀요! 발전실에서 그런 일이 있었어요? <sub>틀: IncidentKnown.none</sub>
- 전혀요! 발전실에서 그런 일이 있었어요? <sub>틀: IncidentKnown.none</sub>
- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 몰랐어요! 무슨 일이었어요? <sub>틀: IncidentKnown.none</sub>
- 전혀요! 발전실에서 그런 일이 있었어요? <sub>틀: IncidentKnown.none</sub>
- 전혀요! 발전실에서 그런 일이 있었어요? <sub>틀: IncidentKnown.none</sub>
- 처음 들어요! 무슨 일이었는데요? <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 그때요? 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called]</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. 그때는 혼자였어요! <sub>틀: WhereAtIncident.any + 기억[mem.alone]</sub>
- 그때요? 정비실이었어요! 그때는 혼자였어요! <sub>틀: WhereAtIncident.any + 기억[mem.alone]</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. 그때는 혼자였어요! <sub>틀: WhereAtIncident.any + 기억[mem.alone]</sub>
- 밤 10시 40분쯤엔 정비실에 있었어요! 그때는 혼자였어요! 통화할 때도 그 자리였어요! <sub>틀: WhereAtIncident.any + 기억[mem.alone, mem.called]</sub>
- 그때요? 정비실이었어요! 통화할 때도 그 자리였어요! 그때는 혼자였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called, mem.alone]</sub>
- 저요? 정비실에 딱 붙어 있었어요! 통화할 때도 그 자리였어요! 그때는 혼자였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called, mem.alone]</sub>
- 밤 10시 40분쯤엔 정비실에 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. 그때는 혼자였어요! 통화할 때도 그 자리였어요. <sub>틀: WhereAtIncident.any + 기억[mem.alone, mem.called]</sub>
- 그 시간이면 정비실이었어요! 기억 확실해요! 통화할 때도 그 자리였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 혼자였어요! 그래도 열심히 했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 그때는 저 혼자였어요! 혼자서도 잘했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 정비실엔 저 혼자였어요! 통화할 때도 그 자리였어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 그때는 저 혼자였어요! 혼자서도 잘했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 그때는 저 혼자였어요! 혼자서도 잘했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 혼자였어요! 그래도 열심히 했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 그때는 저 혼자였어요! 혼자서도 잘했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 혼자였어요! 그래도 열심히 했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 정비실엔 저 혼자였어요! 통화할 때도 그 자리였어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>
- 그때는 저 혼자였어요! 혼자서도 잘했어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.alone + 기억[mem.called]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소처럼 일하고 있었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 특별한 건 없었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 변한 거 없어요! 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 아까 말한 그대로예요! 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 그대로예요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 안 바뀌어요! 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 아까 말한 그대로예요! 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 안 바뀌어요! 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>

### 기분 · 이유

Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '활기참'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '즐거움'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기대됨'이라고 적으셨습니다. 이유가 무엇입니까?  

- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 맞아요! 근무 전부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 왜 저예요?! 저 진짜 아무것도 안 했어요! 정비실엔 저 혼자였어요! <sub>틀: deny + 기억[mem.alone]</sub>
- 진짜 아니에요! 저 계속 일만 했어요! <sub>틀: deny</sub>
- 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! 정비실엔 저 혼자였어요! 관리자님은 뭐 아는 거 있어요? <sub>틀: deny + 기억[mem.alone]</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! <sub>틀: deny</sub>
- 진짜 아니에요! 저 계속 일만 했어요! <sub>틀: deny</sub>
- 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 진짜 아니에요! 저 계속 일만 했어요! <sub>틀: deny</sub>
- 저요?! 절대 아니에요! 진짜예요! <sub>틀: deny</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 아까 말했잖아요! 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 아까 말했잖아요! 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 또요? 음, 다시 말하면요, 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 또요? 음, 다시 말하면요, 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 또요? 음, 다시 말하면요, 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>

## 장면 2 — 둘이 배치

- 배치: 고양이·강아지=발전실 · 여우·양=저장고 · 토끼·늑대=정비실
- 배치표 로그 없음(근무 시작 배치만 기록) · 22:30 관리자가 늑대에게 전화
- 22:40 발전실 설비 고장(고양이·강아지 목격) → 강아지 신고 · 대기 지시 / 23:10 저장고 설비 고장(여우·양 목격) → 양 전화했지만 관리자 부재
- 사고 질문은 22:40 발전실 건 기준

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 오, 지금이요? 지금 비었어요! 어디든 보내 주세요! <sub>틀: status.idle</sub>
- 지금이요? 할 게 없어요! 뭐든 시켜 주세요! <sub>틀: status.idle</sub>
- 심심해요! 일 주세요! <sub>틀: status.idle</sub>
- 지금이요? 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>
- 오, 지금이요? 놀고 있어요! 제 탓 아니에요! <sub>틀: status.idle</sub>
- 할 게 없어요! 뭐든 시켜 주세요! <sub>틀: status.idle</sub>
- 할 게 없어요! 뭐든 시켜 주세요! <sub>틀: status.idle</sub>
- 지금 비었어요! 어디든 보내 주세요! <sub>틀: status.idle</sub>
- 오, 지금이요? 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>
- 놀고 있어요! 제 탓 아니에요! <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 전혀요! 완전 조용했어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 전혀요! 완전 조용했어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 이상현상이요? 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 평화로웠어요! 저 열심히 일했어요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>
- 조용했어요! 좀 심심했지만 좋았어요! <sub>틀: status.quiet</sub>
- 조용했어요! 좀 심심했지만 좋았어요! <sub>틀: status.quiet</sub>
- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 별일 없었어요! 오늘 같은 날 최고예요! <sub>틀: status.quiet</sub>
- 무난했어요! 뿌듯해요! <sub>틀: status.quiet</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 오, 이상현상이요? 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 오, 이상현상이요? 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 이상현상이요? 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 없었어요! 오늘은 평화로웠어요! 무슨 일 있으면 제가 제일 먼저 알려드릴게요! <sub>틀: noanomaly</sub>
- 오, 이상현상이요? 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 딱히 없었어요! 좀 심심할 정도였어요! 있었으면 제가 벌써 떠들고 다녔을걸요! <sub>틀: noanomaly</sub>
- 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 진짜 없었어요! 있었으면 제가 제일 먼저 알았을걸요! 다들 열심히 일하던데요! <sub>틀: nosight</sub>
- 아무도요! 다들 평소 같았어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 다들 멀쩡해 보였어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! 다들 열심히 일하던데요! <sub>틀: nosight</sub>
- 못 봤어요! 봤으면 벌써 말했죠! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 아무도요! 다들 평소 같았어요! <sub>틀: nosight</sub>
- 못 봤어요! 봤으면 벌써 말했죠! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 다들 멀쩡해 보였어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 처음 들어요! 무슨 일이었는데요? <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 몰랐어요! 무슨 일이었어요? <sub>틀: IncidentKnown.none</sub>
- 어? 그런 일이 있었어요? 처음 들어요! <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 바로 정비실이요! 물어보실 줄 알았어요! <sub>틀: WhereAtIncident.any</sub>
- 그때요? 정비실이었어요! <sub>틀: WhereAtIncident.any</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. 늑대 씨도 있었어요! 솔직히 포스가 있으셔서 좀 무서웠는데, 별 일 없었어요. <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. <sub>틀: WhereAtIncident.any</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. <sub>틀: WhereAtIncident.any</sub>
- 정비실이요! 확인해 보셔도 돼요! 참, 늑대 씨도 같이 있었어요! 솔직히 포스가 있으셔서 좀 무서웠는데, 별 일 없었어요. <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>
- 그때요? 정비실이었어요! <sub>틀: WhereAtIncident.any</sub>
- 저요? 정비실에 딱 붙어 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! 늑대 씨도 있었어요! 솔직히 포스가 있으셔서 좀 무서웠는데, 별 일 없었어요. <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>
- 저요? 정비실에 딱 붙어 있었어요! <sub>틀: WhereAtIncident.any</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 늑대 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>
- 늑대 씨랑 있었어요! 말은 없으신데 은근 멋있어요! <sub>틀: Companion.with</sub>
- 늑대 씨요! <sub>틀: Companion.with</sub>
- 늑대 씨요! 말은 없으신데 은근 멋있어요! <sub>틀: Companion.with</sub>
- 늑대 씨요! 말은 없으신데 은근 멋있어요! <sub>틀: Companion.with</sub>
- 같이 있던 사람이요? 늑대 씨요! <sub>틀: Companion.with</sub>
- 같이 있던 사람이요? 늑대 씨요! <sub>틀: Companion.with</sub>
- 정비실에 늑대 씨 있었어요! 솔직히 포스가 있으셔서 좀 무서웠는데, 별 일 없었어요. <sub>틀: Companion.with</sub>
- 정비실에 늑대 씨 있었어요! 말은 없으신데 은근 멋있어요! <sub>틀: Companion.with</sub>
- 늑대 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 그대로예요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 변한 거 없어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 안 바뀌어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 똑같아요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 똑같아요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 아까 말한 그대로예요! 정비실이요! <sub>틀: Restate.same</sub>
- 안 바뀌어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 그대로예요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 아까 말한 그대로예요! 정비실이요! <sub>틀: Restate.same</sub>
- 그대로예요! 정비실에 있었어요! <sub>틀: Restate.same</sub>

### 기분 · 이유

Q: 오늘 기분을 '활기참'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '즐거움'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기대됨'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  

- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 맞아요! 근무 전부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 억울해요! 저 그런 거 안 해요! 관리자님은 뭐 아는 거 있어요? <sub>틀: deny</sub>
- 오, 저요? 진짜 아니에요! 저 계속 일만 했어요! 참, 늑대 씨도 같이 있었어요! <sub>틀: deny + 기억[mem.with]</sub>
- 에이, 저 아니에요! 저 일만 했어요! 왜요, 뭐 있었어요? <sub>틀: deny</sub>
- 진짜 아니에요! 저 계속 일만 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! 근데 무슨 일 있어요? <sub>틀: deny</sub>
- 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 저 억울해요! 저 아니에요! <sub>틀: deny</sub>
- 억울해요! 저 그런 거 안 해요! 옆에 늑대 씨 있었어요! <sub>틀: deny + 기억[mem.with]</sub>
- 억울해요! 저 그런 거 안 해요! <sub>틀: deny</sub>
- 저요? 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 아까 말했잖아요! 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 아까 말했잖아요! 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 아까 말했잖아요! 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 딱히 없었어요! 좀 심심할 정도였어요! <sub>틀: noanomaly</sub>
- 또요? 음, 다시 말하면요, 없었어요! 오늘은 평화로웠어요! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>
- 한 번 더 말할게요! 전혀요! 완전 조용했어요! <sub>틀: noanomaly</sub>
- 또요? 음, 다시 말하면요, 이상한 거요? 없었어요! 있었으면 제가 먼저 뛰어갔죠! <sub>틀: noanomaly</sub>

## 장면 3 — 재배치

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 양=저장고 · 늑대·강아지=경비실 (배치표 로그 있음)
- 22:25 강아지 발전실로 재배치 / 22:30 관리자가 토끼에게 전화 / 22:40 발전실 설비 고장(강아지 목격, 늑대 옆방)
- 22:41 늑대 신고 → 확인 지시 → 발전실 수리 → 23:00 복구 / 23:10 정비실 설비 고장(토끼·고양이 목격, 양 옆방)
- 23:11 양이 전화했지만 관리자 부재 / 23:12 고양이 신고 → 대기 지시 / 23:20 토끼 저장고로 재배치
- 사고 질문은 22:40 발전실 건 기준 · 늑대 · 양 · 강아지는 발전실 사고 신고 전화도 건다

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 오, 지금이요? 지금 비었어요! 어디든 보내 주세요! 재고 정리 하고 있었어요! <sub>틀: status.idle + 기억[mem.worked]</sub>
- 지금이요? 손이 놀고 있어요! 뭐 도울 거 없어요? 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요! <sub>틀: status.idle + 기억[mem.relocated]</sub>
- 할 게 없어요! 뭐든 시켜 주세요! 재고 정리 하고 있었어요! <sub>틀: status.idle + 기억[mem.worked]</sub>
- 할 게 없어요! 뭐든 시켜 주세요! 밤 11시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요! <sub>틀: status.idle + 기억[mem.relocated]</sub>
- 대기 중이에요! 부르시면 바로 뛰어갈게요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요. 재고 정리 하던 중이었어요. <sub>틀: status.idle + 기억[mem.relocated, mem.worked]</sub>
- 오, 지금이요? 지금 비었어요! 어디든 보내 주세요! 그때 재고 정리 거의 끝나가던 참이었어요! <sub>틀: status.idle + 기억[mem.worked]</sub>
- 할 게 없어요! 뭐든 시켜 주세요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요. <sub>틀: status.idle + 기억[mem.relocated]</sub>
- 할 게 없어요! 뭐든 시켜 주세요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요. 재고 정리 하고 있었어요. <sub>틀: status.idle + 기억[mem.relocated, mem.worked]</sub>
- 대기 중이에요! 부르시면 바로 뛰어갈게요! 그때 재고 정리 거의 끝나가던 참이었어요! 원래 자리는 아니었어요. 옮기라고 하셔서 왔어요. <sub>틀: status.idle + 기억[mem.worked, mem.relocated]</sub>
- 대기 중이에요! 부르시면 바로 뛰어갈게요! 밤 11시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요! <sub>틀: status.idle + 기억[mem.relocated]</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 그때 정비실이요! 장비 하나가 나갔어요. 무섭기도 했는데 좀 신기했어요! 고양이 씨도 같이 봤어요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요! 제가 아는 건 그게 다예요. <sub>틀: incident.direct</sub>
- 바로 앞에서요! 설비가 멈췄어요. 심장 떨어지는 줄 알았어요! 고양이 씨도 그 자리에 있었어요. 엄청 놀란 것 같더라고요. 제가 말 걸면 싫어하시는 것 같아요. 그래도 계속 걸 거예요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 진짜 깜짝 놀랐어요! 바로 앞에서요! 기계가 갑자기 섰어요. 심장 떨어지는 줄 알았어요. <sub>틀: incident.direct</sub>
- 오, 이상현상이요? 진짜 봤어요! 정비실이었어요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 진짜 봤어요! 정비실이었어요! 장비 하나가 나갔어요. 고양이 씨도 같이 봤어요. 계속 투덜거리셔서 좀 무서웠어요. 그래도 일은 진짜 잘하세요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 바로 앞에서요! 장비 하나가 나갔어요. 심장 떨어지는 줄 알았어요! 고양이 씨도 같이 봤어요. 제가 말 걸면 싫어하시는 것 같아요. 그래도 계속 걸 거예요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 완전 제 눈앞이었어요! 설비가 멈췄어요. 제가 잘못 봤을 수도 있어요! <sub>틀: incident.direct</sub>
- 완전 제 눈앞이었어요! 설비가 멈췄어요. <sub>틀: incident.direct</sub>
- 심장 떨어지는 줄 알았어요! 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요. <sub>틀: incident.direct</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 바빴어요! 정비실 쪽 신경 쓰느라 딴 생각할 틈도 없었어요! <sub>틀: status.busy</sub>
- 정비실 일 때문에 정신없었어요! 그래도 재밌었어요! <sub>틀: status.busy</sub>
- 엄청 바빴어요! 정비실에서 사고가 났잖아요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 오늘은 정비실 때문에 하루가 엄청 빨리 갔어요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 휴, 힘들었어요! 정비실에서 사고가 났잖아요! 그래도 버텼어요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 바빴어요! 정비실 쪽 신경 쓰느라 딴 생각할 틈도 없었어요! <sub>틀: status.busy</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 바로 앞에서요! 기계가 갑자기 섰어요. 심장 떨어지는 줄 알았어요! 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 이상현상이요? 정비실이요! 설비가 멈췄어요. 무섭기도 했는데 좀 신기했어요! <sub>틀: incident.direct</sub>
- 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요! <sub>틀: incident.direct</sub>
- 정비실에서요! 장비 하나가 나갔어요. 진짜 깜짝 놀랐어요! 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요! <sub>틀: incident.direct</sub>
- 진짜 봤어요! 정비실이었어요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 심장 떨어지는 줄 알았어요! 완전 제 눈앞이었어요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>
- 이상현상이요? 봤어요, 봤어요! 정비실에서요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 심장 떨어지는 줄 알았어요! 그때 정비실에서요! 장비 하나가 나갔어요. 진짜 깜짝 놀랐어요. <sub>틀: incident.direct</sub>
- 진짜 깜짝 놀랐어요! 완전 제 눈앞이었어요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 진짜 없었어요! 있었으면 제가 제일 먼저 알았을걸요! 다들 열심히 일하던데요! <sub>틀: nosight</sub>
- 아무도요! 다들 평소 같았어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 다들 멀쩡해 보였어요! 다들 열심히 일하던데요! <sub>틀: nosight</sub>
- 없었어요! 제가 좀 두리번거리는 편인데도요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! <sub>틀: nosight</sub>
- 진짜 없었어요! 있었으면 제가 제일 먼저 알았을걸요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 아무도요! 다들 평소 같았어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>
- 못 봤어요! 봤으면 벌써 말했죠! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 발전실에서 난 이 사고를 알고 있었습니까?  

- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 진짜요? 전혀 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 어? 그런 일이 있었어요? 처음 들어요! <sub>틀: IncidentKnown.none</sub>
- 몰랐어요! 무슨 일이었어요? <sub>틀: IncidentKnown.none</sub>
- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>
- 처음 들어요! 무슨 일이었는데요? <sub>틀: IncidentKnown.none</sub>
- 에? 저 하나도 몰랐어요! <sub>틀: IncidentKnown.none</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 밤 10시 40분쯤엔 정비실에 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 그 시간이면 정비실이었어요! 기억 확실해요! 통화할 때도 그 자리였어요! 참, 고양이 씨도 같이 있었어요. <sub>틀: WhereAtIncident.any + 기억[mem.called, mem.with]</sub>
- 그때요? 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called]</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. 참, 고양이 씨도 같이 있었어요! 통화할 때도 그 자리였어요. <sub>틀: WhereAtIncident.any + 기억[mem.with, mem.called]</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! 통화할 때도 그 자리였어요! <sub>틀: WhereAtIncident.any + 기억[mem.called]</sub>
- 정비실이요! 확인해 보셔도 돼요! 참, 고양이 씨도 같이 있었어요! 통화할 때도 그 자리였어요. <sub>틀: WhereAtIncident.any + 기억[mem.with, mem.called]</sub>
- 그때요? 정비실이었어요! <sub>틀: WhereAtIncident.any</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! 고양이 씨도 있었어요! 통화할 때도 그 자리였어요. <sub>틀: WhereAtIncident.any + 기억[mem.with, mem.called]</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! 고양이 씨도 있었어요! <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! 옆에 고양이 씨 있었어요! 제가 말 걸면 싫어하시는 것 같아요. 그래도 계속 걸 거예요. <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 고양이 씨요! 제가 말 걸면 싫어하시는 것 같아요! 그래도 계속 걸 거예요! 통화할 때도 그 자리였어요. <sub>틀: Companion.with + 기억[mem.called]</sub>
- 정비실에 고양이 씨 있었어요! 계속 투덜거리셔서 좀 무서웠어요! 그래도 일은 진짜 잘하세요! <sub>틀: Companion.with</sub>
- 그때는 고양이 씨가 옆에 있었어요! 물어보셔도 돼요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.with + 기억[mem.called]</sub>
- 정비실에 고양이 씨 있었어요! 계속 투덜거리셔서 좀 무서웠어요! 그래도 일은 진짜 잘하세요! 통화할 때도 그 자리였어요. <sub>틀: Companion.with + 기억[mem.called]</sub>
- 고양이 씨랑 있었어요! 제가 말 걸면 싫어하시는 것 같아요! 그래도 계속 걸 거예요! 관리자님이 전화하셨을 때도 정비실에 있었어요. <sub>틀: Companion.with + 기억[mem.called]</sub>
- 정비실에 고양이 씨 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Companion.with + 기억[mem.called]</sub>
- 그때는 고양이 씨가 옆에 있었어요! 물어보셔도 돼요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.with + 기억[mem.called]</sub>
- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.with + 기억[mem.called]</sub>
- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.with + 기억[mem.called]</sub>
- 저랑 고양이 씨요! 같이 있었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: Companion.with + 기억[mem.called]</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 별거 없었어요! 정비실에서 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 특별한 건 없었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 평소처럼 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 특별한 건 없었어요! 관리자님이 전화하셨을 때도 정비실에 있었어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>
- 특별한 건 없었어요! 통화할 때도 그 자리였어요! <sub>틀: BeforeIncident.plain + 기억[mem.called]</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 안 바뀌어요! 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 변한 거 없어요! 정비실이었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 아까 말한 그대로예요! 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 아까 말한 그대로예요! 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 아까 말한 그대로예요! 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 몇 번 물어보셔도 정비실이요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 똑같아요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>
- 변한 거 없어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 그대로예요! 정비실에 있었어요! 통화할 때도 그 자리였어요! <sub>틀: Restate.same + 기억[mem.called]</sub>

### 이동 · 이유

Q: 밤 11시 22분경 정비실에서 저장고로 이동한 이유는 무엇입니까?  

- 지시대로 갔어요! 저 말 잘 듣죠? 그때 재고 정리 거의 끝나가던 참이었어요! <sub>틀: MoveReason.ordered + 기억[mem.worked]</sub>
- 옮기라고 하셨잖아요! 그래서 저장고로 갔어요! 재고 정리 하고 있었어요! 참, 양 씨도 같이 있었어요. <sub>틀: MoveReason.ordered + 기억[mem.worked, mem.with]</sub>
- 지시대로 갔어요! 저 말 잘 듣죠? 참, 양 씨도 같이 있었어요! 재고 정리 하고 있었어요! <sub>틀: MoveReason.ordered + 기억[mem.with, mem.worked]</sub>
- 정비실에서 저장고로요! 관리자님이 시키셨잖아요! 참, 양 씨도 같이 있었어요! <sub>틀: MoveReason.ordered + 기억[mem.with]</sub>
- 정비실에서 저장고로요! 관리자님이 시키셨잖아요! 참, 양 씨도 같이 있었어요! 재고 정리 하던 중이었어요. <sub>틀: MoveReason.ordered + 기억[mem.with, mem.worked]</sub>
- 지시대로 갔어요! 저 말 잘 듣죠? 재고 정리 하던 중이었어요! <sub>틀: MoveReason.ordered + 기억[mem.worked]</sub>
- 정비실에서 저장고로요! 관리자님이 시키셨잖아요! 재고 정리 하던 중이었어요! 참, 양 씨도 같이 있었어요. <sub>틀: MoveReason.ordered + 기억[mem.worked, mem.with]</sub>
- 정비실에서 저장고로요! 관리자님이 시키셨잖아요! 재고 정리 하던 중이었어요! 양 씨도 있었어요. <sub>틀: MoveReason.ordered + 기억[mem.worked, mem.with]</sub>
- 옮기라고 하셨잖아요! 그래서 저장고로 갔어요! 옆에 양 씨 있었어요! 겁이 많으셔서 제가 지켜드려야 할 것 같았어요. <sub>틀: MoveReason.ordered + 기억[mem.with]</sub>
- 정비실에서 저장고로요! 관리자님이 시키셨잖아요! 재고 정리 하고 있었어요! <sub>틀: MoveReason.ordered + 기억[mem.worked]</sub>

### 이동 · 거기서 한 일

Q: 저장고에서는 무엇을 했습니까?  

- 저장고에서 재고 정리 했어요! 양 씨도 있었어요! <sub>틀: ActionThere.task + 기억[mem.with]</sub>
- 저장고에서 재고 정리 했어요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 저장고에서 재고 정리 했어요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 재고 정리 했어요! 밤 11시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 저장고에서 재고 정리 했어요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 저장고에서 재고 정리 했어요! 옆에 양 씨 있었어요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요. <sub>틀: ActionThere.task + 기억[mem.with, mem.relocated]</sub>
- 재고 정리 열심히 했어요! 밤 11시 20분쯤에 관리자님이 저장고로 옮기라고 하셨잖아요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 재고 정리 열심히 했어요! 원래 자리는 아니었어요! 옮기라고 하셔서 왔어요! <sub>틀: ActionThere.task + 기억[mem.relocated]</sub>
- 재고 정리 했어요! <sub>틀: ActionThere.task</sub>
- 재고 정리 열심히 했어요! <sub>틀: ActionThere.task</sub>

### 기분 · 이유

Q: 오늘 기분을 '활기참'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '나쁘지 않음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '즐거움'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기대됨'이라고 적으셨습니다. 이유가 무엇입니까?  

- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 기록이요? 그래도 저 아니에요! 진짜로요! <sub>틀: deny.evidence</sub>
- 으앗, 그렇게 나왔어요? 근데 저 아니에요! <sub>틀: deny.evidence</sub>
- 기록이요? 그래도 저 아니에요! 진짜로요! <sub>틀: deny.evidence</sub>
- 으앗, 그렇게 나왔어요? 근데 저 아니에요! <sub>틀: deny.evidence</sub>
- 그건 이유가 있었어요! 제가 한 거 아니에요! <sub>틀: deny.evidence</sub>
- 기록이요? 그래도 저 아니에요! 진짜로요! <sub>틀: deny.evidence</sub>
- 오, 저요? 그건 이유가 있었어요! 제가 한 거 아니에요! <sub>틀: deny.evidence</sub>
- 그건 이유가 있었어요! 제가 한 거 아니에요! 관리자님은 뭐 아는 거 있어요? <sub>틀: deny.evidence</sub>
- 으앗, 그렇게 나왔어요? 근데 저 아니에요! <sub>틀: deny.evidence</sub>
- 기록이요? 그래도 저 아니에요! 진짜로요! 왜요, 뭐 있었어요? <sub>틀: deny.evidence</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 한 번 더 말할게요! 정비실이요! 장비 하나가 나갔어요. 무섭기도 했는데 좀 신기했어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 정비실에서요! 장비 하나가 나갔어요. 진짜 깜짝 놀랐어요! <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 제가 봤어요! 정비실에서요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>
- 한 번 더 말할게요! 정비실이요! 기계가 갑자기 섰어요. 무섭기도 했는데 좀 신기했어요. <sub>틀: incident.direct</sub>
- 아까 말했잖아요! 정비실에서요! 설비가 멈췄어요. 진짜 깜짝 놀랐어요. <sub>틀: incident.direct</sub>
- 한 번 더 말할게요! 완전 제 눈앞이었어요! 설비가 멈췄어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 완전 제 눈앞이었어요! 설비가 멈췄어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 바로 앞에서요! 장비 하나가 나갔어요. 심장 떨어지는 줄 알았어요! <sub>틀: incident.direct</sub>
- 한 번 더 말할게요! 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 봤어요, 봤어요! 정비실에서요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>

## 장면 4 — 미배치 포함

- 배치: 토끼·고양이=정비실 · 여우=코어실 · 늑대·강아지=경비실 · 양=배치 안 됨(시작실 저장고에 서 있음)
- 배치표 로그 없음(근무 시작 배치만 기록)
- 22:10 코어실 설비 고장(여우 목격) / 22:20 여우 저장고로 재배치 / 22:40 저장고 설비 고장(여우 목격)
- 23:00 정비실 설비 고장(토끼·고양이 목격)
- 사고 질문은 22:40 저장고 건 기준 · 양의 이름은 누구의 답에도 나오면 안 된다(양 자신의 답은 근무하지 않은 사람의 답)

### 근무 중 전화 · 작업 상태

Q: 작업은 잘 되어가나요?  

- 지금이요? 대기 중이에요! 부르시면 바로 뛰어갈게요! <sub>틀: status.idle</sub>
- 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 지금이요? 심심해요! 일 주세요! <sub>틀: status.idle</sub>
- 심심해요! 일 주세요! <sub>틀: status.idle</sub>
- 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 지금이요? 손이 놀고 있어요! 뭐 도울 거 없어요? <sub>틀: status.idle</sub>
- 오, 지금이요? 심심해요! 일 주세요! <sub>틀: status.idle</sub>
- 놀고 있어요! 제 탓 아니에요! <sub>틀: status.idle</sub>
- 오, 지금이요? 놀고 있어요! 제 탓 아니에요! <sub>틀: status.idle</sub>

### 근무 중 전화 · 이상현상

Q: 주변에 이상현상은 없었나요?  

- 조금 전에 정비실에서요! 설비가 멈췄어요. 진짜 깜짝 놀랐어요! <sub>틀: incident.direct</sub>
- 제가 봤어요! 정비실에서요! 장비 하나가 나갔어요. 고양이 씨도 같이 봤어요. 근데 확실하진 않아요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 봤어요, 봤어요! 정비실에서요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 심장 떨어지는 줄 알았어요! 진짜 봤어요! 정비실이었어요. 설비가 멈췄어요. 고양이 씨도 그 자리에 있었어요. 엄청 놀란 것 같더라고요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 진짜 깜짝 놀랐어요! 바로 앞에서요! 설비가 멈췄어요. 심장 떨어지는 줄 알았어요. <sub>틀: incident.direct</sub>
- 오, 이상현상이요? 완전 제 눈앞이었어요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>
- 진짜 봤어요! 정비실이었어요! 설비가 멈췄어요. 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 제가 봤어요! 정비실에서요! 설비가 멈췄어요. 고양이 씨도 그 자리에 있었어요. 엄청 놀란 것 같더라고요. 더 알게 되면 바로 말할게요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>
- 진짜 깜짝 놀랐어요! 바로 앞에서요! 기계가 갑자기 섰어요. 심장 떨어지는 줄 알았어요. <sub>틀: incident.direct</sub>
- 진짜 깜짝 놀랐어요! 정비실이요! 장비 하나가 나갔어요. 무섭기도 했는데 좀 신기했어요. 고양이 씨도 그 자리에 있었어요. 엄청 놀란 것 같더라고요. <sub>틀: incident.direct + 기억[mem.with.incident]</sub>

### 최초 진술 · 근무 소감

Q: (심문을 열면 먼저 하는 말)  

- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 오늘은 정비실 때문에 하루가 엄청 빨리 갔어요! <sub>틀: status.busy</sub>
- 휴, 힘들었어요! 정비실에서 사고가 났잖아요! 그래도 버텼어요! <sub>틀: status.busy</sub>
- 엄청 바빴어요! 정비실에서 사고가 났잖아요! <sub>틀: status.busy</sub>
- 휴, 힘들었어요! 정비실에서 사고가 났잖아요! 그래도 버텼어요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>
- 오늘은 정비실 때문에 하루가 엄청 빨리 갔어요! <sub>틀: status.busy</sub>
- 휴, 힘들었어요! 정비실에서 사고가 났잖아요! 그래도 버텼어요! <sub>틀: status.busy</sub>
- 오늘은 정비실 때문에 하루가 엄청 빨리 갔어요! <sub>틀: status.busy</sub>
- 정신없었어요! 정비실 쪽 일 때문에 계속 두근두근했어요! <sub>틀: status.busy</sub>

### 최초 진술 · 이상한 점

Q: (심문을 열면 먼저 하는 말)  

- 정비실이요! 설비가 멈췄어요. 무섭기도 했는데 좀 신기했어요! 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 정비실이요! 기계가 갑자기 섰어요. 무섭기도 했는데 좀 신기했어요! 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 완전 제 눈앞이었어요! 장비 하나가 나갔어요. 근데 확실하진 않아요! <sub>틀: incident.direct</sub>
- 바로 앞에서요! 기계가 갑자기 섰어요. 심장 떨어지는 줄 알았어요! 더 알게 되면 바로 말할게요. <sub>틀: incident.direct</sub>
- 정비실이요! 기계가 갑자기 섰어요. 무섭기도 했는데 좀 신기했어요! 제가 아는 건 그게 다예요. <sub>틀: incident.direct</sub>
- 바로 앞에서요! 설비가 멈췄어요. 심장 떨어지는 줄 알았어요! 제가 아는 건 그게 다예요. <sub>틀: incident.direct</sub>
- 봤어요, 봤어요! 정비실에서요! 장비 하나가 나갔어요. 제가 아는 건 그게 다예요. <sub>틀: incident.direct</sub>
- 완전 제 눈앞이었어요! 장비 하나가 나갔어요. 제가 아는 건 그게 다예요! <sub>틀: incident.direct</sub>
- 심장 떨어지는 줄 알았어요! 제가 봤어요! 정비실에서요. 설비가 멈췄어요. <sub>틀: incident.direct</sub>
- 오, 이상현상이요? 제가 봤어요! 정비실에서요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>

### 최초 진술 · 수상한 사람

Q: (심문을 열면 먼저 하는 말)  

- 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! <sub>틀: nosight</sub>
- 없었어요! 제가 좀 두리번거리는 편인데도요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! 봤으면 벌써 말했죠! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! <sub>틀: nosight</sub>
- 오, 수상한 사람이요? 음, 없었어요! 제 눈엔 다 똑같이 일하는 걸로 보였어요! <sub>틀: nosight</sub>
- 수상한 사람이요? 못 봤어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 아무도요! 다들 평소 같았어요! 괜히 아무나 찍으면 안 되잖아요! <sub>틀: nosight</sub>
- 못 봤어요! 봤으면 벌써 말했죠! <sub>틀: nosight</sub>

### 사고 · 알았나

Q: 밤 10시 40분경 저장고에서 난 이 사고를 알고 있었습니까?  

- 에? 저 하나도 몰랐어요! 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 몰랐어요! 무슨 일이었어요? 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 처음 들어요! 무슨 일이었는데요? 정비실에서 사고 났을 때 저도 거기 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 진짜요? 전혀 몰랐어요! 정비실에서 사고 났을 때 저도 거기 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 진짜요? 전혀 몰랐어요! 정비실에서 사고 났을 때 저도 거기 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 몰랐어요! 무슨 일이었어요? 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 몰랐어요! 무슨 일이었어요? 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 처음 들어요! 무슨 일이었는데요? 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 처음 들어요! 무슨 일이었는데요? 정비실에서 사고 났을 때 저도 거기 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>
- 몰랐어요! 무슨 일이었어요? 사고 날 때 정비실에 있었어요! <sub>틀: IncidentKnown.none + 기억[mem.incident.here]</sub>

### 사고 · 그때 어디

Q: 밤 10시 40분경 당신은 어디에 있었습니까?  

- 그 시간이면 정비실이었어요! 기억 확실해요! <sub>틀: WhereAtIncident.any</sub>
- 정비실이요! 확인해 보셔도 돼요! <sub>틀: WhereAtIncident.any</sub>
- 저요? 정비실에 딱 붙어 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 밤 10시 40분쯤엔 정비실에 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! <sub>틀: WhereAtIncident.any</sub>
- 그때요? 정비실이었어요! <sub>틀: WhereAtIncident.any</sub>
- 정비실이요! 확인해 보셔도 돼요! 옆에 고양이 씨 있었어요! <sub>틀: WhereAtIncident.any + 기억[mem.with]</sub>
- 저요? 정비실에 딱 붙어 있었어요! <sub>틀: WhereAtIncident.any</sub>
- 바로 정비실이요! 물어보실 줄 알았어요! <sub>틀: WhereAtIncident.any</sub>
- 정비실에 있었어요! 거기서 열심히 일했어요! 칭찬해 주세요. <sub>틀: WhereAtIncident.any</sub>

### 사고 · 같이 있던 사람

Q: 밤 10시 40분경 함께 있던 직원이 있었습니까?  

- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>
- 고양이 씨요! 제가 말 걸면 싫어하시는 것 같아요! 그래도 계속 걸 거예요! <sub>틀: Companion.with</sub>
- 같이 있던 사람이요? 고양이 씨요! <sub>틀: Companion.with</sub>
- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>
- 고양이 씨랑 있었어요! 제가 말 걸면 싫어하시는 것 같아요! 그래도 계속 걸 거예요! <sub>틀: Companion.with</sub>
- 같이 있던 사람이요? 고양이 씨요! <sub>틀: Companion.with</sub>
- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>
- 고양이 씨요! 계속 투덜거리셔서 좀 무서웠어요! 그래도 일은 진짜 잘하세요! <sub>틀: Companion.with</sub>
- 같이 있던 사람이요? 고양이 씨요! <sub>틀: Companion.with</sub>
- 고양이 씨랑 같이 있었어요! 확인해 보셔도 돼요! <sub>틀: Companion.with</sub>

### 사고 · 직전

Q: 이 사고 직전에는 무엇을 하고 있었습니까?  

- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 특별한 건 없었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 별거 없었어요! 정비실에서 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>
- 평소처럼 일하고 있었어요! <sub>틀: BeforeIncident.plain</sub>

### 진술 재확인

Q: 밤 10시 40분경 정비실에 있었다고 하셨습니다. 다시 설명해 주십시오.  

- 변한 거 없어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 안 바뀌어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 몇 번 물어보셔도 정비실이요! <sub>틀: Restate.same</sub>
- 안 바뀌어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 안 바뀌어요! 정비실이었어요! <sub>틀: Restate.same</sub>
- 똑같아요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 아까 말한 그대로예요! 정비실이요! <sub>틀: Restate.same</sub>
- 똑같아요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 똑같아요! 정비실에 있었어요! <sub>틀: Restate.same</sub>
- 몇 번 물어보셔도 정비실이요! <sub>틀: Restate.same</sub>

### 기분 · 이유

Q: 오늘 기분을 '기분 좋음'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '즐거움'이라고 적으셨습니다. 이유가 무엇입니까?  
Q: 오늘 기분을 '기대됨'이라고 적으셨습니다. 이유가 무엇입니까?  

- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 그냥요! 기분이 그랬어요! <sub>틀: MoodReason.plain</sub>
- 오늘 일이 잘 풀렸어요! <sub>틀: MoodReason.calm</sub>
- 이유 없어요! 그냥 그런 날이에요! <sub>틀: MoodReason.plain</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>
- 딱히 나쁜 일이 없었거든요! <sub>틀: MoodReason.calm</sub>

### 기분 · 근무 전부터

Q: 근무 전부터 그런 상태였습니까?  

- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 출근할 때부터요! 여기 와서 생긴 건 아니에요! <sub>틀: MoodBefore.yes</sub>
- 그랬어요! 오기 전부터 쭉이요! <sub>틀: MoodBefore.yes</sub>
- 네, 올 때부터 그랬어요! <sub>틀: MoodBefore.yes</sub>
- 원래 그랬어요! <sub>틀: MoodBefore.yes</sub>

### 의심받을 때

Q: 당신을 의심하고 있습니다.  

- 왜 저예요?! 저 진짜 아무것도 안 했어요! 왜요, 뭐 있었어요? <sub>틀: deny</sub>
- 오, 저요? 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! 왜요, 뭐 있었어요? <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! <sub>틀: deny</sub>
- 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 왜 저예요?! 저 진짜 아무것도 안 했어요! <sub>틀: deny</sub>
- 오, 저요? 에이, 저 아니에요! 저 일만 했어요! <sub>틀: deny</sub>
- 저요?! 절대 아니에요! 진짜예요! <sub>틀: deny</sub>
- 억울해요! 저 그런 거 안 해요! 왜요, 뭐 있었어요? <sub>틀: deny</sub>

### 기본 질문 다시(이상한 점)

Q: 오늘 이상한 점을 느꼈습니까? (다시)  

- 또요? 음, 다시 말하면요, 봤어요, 봤어요! 정비실에서요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 정비실이요! 장비 하나가 나갔어요. 무섭기도 했는데 좀 신기했어요! <sub>틀: incident.direct</sub>
- 한 번 더 말할게요! 정비실에서요! 장비 하나가 나갔어요. 진짜 깜짝 놀랐어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 정비실에서요! 기계가 갑자기 섰어요. 진짜 깜짝 놀랐어요! <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 제가 봤어요! 정비실에서요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 제가 봤어요! 정비실에서요! 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>
- 또요? 음, 다시 말하면요, 제가 봤어요! 정비실에서요! 장비 하나가 나갔어요. <sub>틀: incident.direct</sub>
- 아까 말했잖아요! 봤어요, 봤어요! 정비실에서요. 설비가 멈췄어요. <sub>틀: incident.direct</sub>
- 한 번 더 말할게요! 바로 앞에서요! 설비가 멈췄어요. 심장 떨어지는 줄 알았어요. <sub>틀: incident.direct</sub>
- 아까 말했잖아요! 제가 봤어요! 정비실에서요. 기계가 갑자기 섰어요. <sub>틀: incident.direct</sub>

