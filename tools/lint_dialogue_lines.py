"""대사 뱅크(data/dialogue/lines/*.txt) 검사기.

    python tools/lint_dialogue_lines.py            # 전체
    python tools/lint_dialogue_lines.py wolf       # 한 캐릭터

게임 안의 KoreanParticle.Lint 와 같은 조사 규칙 + 슬롯별 허용 변수 + 필수 슬롯 +
말투(격식체/해요체) + 캐릭터별 금지 패턴을 본다. ERROR 가 하나라도 있으면 종료 코드 1.
"""
import os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LINES = os.path.join(ROOT, "data", "dialogue", "lines")
IDS = ["rabbit", "cat", "fox", "sheep", "wolf", "dog"]
FORMAL = {"wolf"}

PAIRS = [("은", "는"), ("이", "가"), ("을", "를"), ("과", "와"), ("으로", "로"), ("이었", "였"),
         ("이에요", "예요"), ("이죠", "죠"), ("이요", "요"), ("이라고", "라고"), ("이랑", "랑"),
         ("이야", "야"), ("이나", "나"), ("이라", "라")]
BARE = ["이에요", "이었", "이요", "이라고", "이랑", "이야", "으로", "에요",
        "은", "는", "이", "가", "을", "를", "과", "와", "로", "예요", "였", "요", "랑", "야"]

ALLOWED = {
    "greet.interview": [], "greet.call": [], "call.prefix": [],
    "selfloc": ["room"], "incident.direct": ["iroom", "what"], "incident.indirect": ["iroom", "sound"],
    "noanomaly": [], "sight": ["who", "sroom"], "nosight": [], "opinion": ["target", "trait"],
    "deny": [], "deny.evidence": [],
    "status.ok": [], "status.quiet": [], "status.busy": ["iroom"], "status.hard": [], "status.blocked": [],
    "status.repair": [], "status.stress": [], "status.idle": [], "status.moving": [], "comply": [],
    "report.direct": ["iroom", "what"], "report.indirect": ["iroom", "sound"], "report.blackout": [],
    "accept": [], "decline": [],
    "opener.repeat": [], "react.accused": [], "emotion.alarm": [], "emotion.fear": [], "emotion.annoy": [],
    "caveat.indirect": [], "caveat.cause": [],
    "support.task": [], "support.taskname": ["task"], "support.nothing": [], "support.hedge": [],
    "support.justify": [], "support.minimize": [], "support.vague": [], "support.redirect": ["who"],
    "support.seen": [], "volunteer.noanomaly": [], "volunteer.nosight": [], "volunteer.sight": [],
    "closer": [], "closer.back": [],
    "prevloc.same": ["room"], "prevloc.moved": ["droom", "room"], "nextact.stayed": ["room"],
    "nextact.moved": ["droom"], "present.alone": [], "present.with": ["dname"], "witness.alone": [],
    "witness.with": ["dname"], "heard": [], "seen.saw": [], "seen.heard": [], "certain.sure": [],
    "certain.unsure": [], "detail.direct": ["iroom", "what"], "detail.indirect": [], "detail.sight": [],
    "sightplace": ["room"], "reason.sight": [], "reason.move": [], "reason.opinion": [],
    "opinionq.today": [], "opinionq.suspicion": [], "challenge.honest": [], "challenge.evasive": [],
    "MoveReason.ordered": ["to", "from"], "MoveReason.dispatched": ["to"], "MoveReason.repair": ["to"],
    "MoveReason.task": ["to", "task"], "MoveReason.check": ["to"], "MoveReason.plain": ["to"],
    "MoveReason.evasive": [],
    "PresenceReason.task": ["room", "task"], "PresenceReason.assigned": ["room"],
    "PresenceReason.check": ["room"], "PresenceReason.evasive": ["room"],
    "Companion.with": ["who", "room"], "Companion.alone": ["room"],
    "NextLocation.moved": ["next", "room"], "NextLocation.stayed": ["room"], "NextLocation.evasive": [],
    "ActionThere.task": ["task", "room"], "ActionThere.repair": ["room"], "ActionThere.check": ["room"],
    "ActionThere.evasive": ["room"],
    "IncidentKnown.direct": ["room"], "IncidentKnown.indirect": ["room"], "IncidentKnown.none": ["room"],
    "WhereAtIncident.any": ["room", "time"],
    "BeforeIncident.task": ["room", "task"], "BeforeIncident.moved": ["prev", "room"], "BeforeIncident.plain": ["room"],
    "WhoSeenNear.someone": ["who"], "WhoSeenNear.none": [],
    "RouteAround.full": ["prev", "room", "next"], "RouteAround.short": ["room"], "RouteAround.evasive": [],
    "ConfirmTestimony.admit": ["room"], "ConfirmTestimony.deny": ["room"], "Restate.same": ["room"],
    "MoodReason.incident": ["room"], "MoodReason.calm": [], "MoodReason.plain": [],
    "MoodBefore.yes": [], "MoodBefore.no": [],
    "MoodRelated.person": ["who"], "MoodRelated.incident": ["room"], "MoodRelated.none": [],
    "ExactTime.exact": ["time"], "ExactTime.vague": [],
    "Confront.honest": ["room", "time"], "Confront.evasive": ["room", "time"], "Confront.deny": ["room", "time"],
    "Unknown.any": [],
    "mem.worked": ["task", "room"], "mem.worked.repair": ["room"], "mem.with": ["who", "room"],
    "mem.with.two": ["who", "who2", "room"], "mem.with.incident": ["who", "room"], "mem.with.day": ["who"],
    "mem.with.vague": [], "mem.alone": ["room"], "mem.relocated": ["room", "time"],
    "mem.dispatched": ["iroom"], "mem.call.stay": ["iroom"], "mem.call.reported": ["iroom"],
    "mem.call.missed": ["iroom"], "mem.called": ["room"], "mem.repair": ["room"], "mem.heard": ["iroom"],
    "mem.incident.here": ["room"], "mem.sum.moves": ["n"], "mem.sum.calls": ["n"],
    "slip.assert": [], "slip.concern.vague": [],
    "echo.wrap": ["subject"], "echo.certain": [], "echo.seen": [], "echo.heard": [], "echo.where": [],
    "time.vague": [],
}

# 캐릭터 파일에 반드시 있어야 하는 슬롯(없으면 공통 문장이 대신 나가 말투가 흐려진다).
REQUIRED = [s for s in ALLOWED if not s.startswith(("echo.", "time.", "Unknown", "phrase."))
            and s not in ("mem.with.vague", "slip.assert", "slip.concern.vague", "emotion.annoy")]

VAR = re.compile(r"\{(\w+)\}")

TAILS = ["습니다", "했어요", "했는데요", "이에요", "예요", "어요", "아요", "죠", "요"]


def core(t):
    t = re.sub(r"[.!?…,~\s]", "", t)
    for tail in TAILS:
        if t.endswith(tail): return t[:-len(tail)]
    return t


# DialogueNaturalnessFilter.Repeats 와 같은 기준 — 한 틀 안에서 같은 말을 두 번 하는가.
def repeats(a, b):
    x, y = core(a), core(b)
    if not x or not y: return False
    if (len(y) >= 3 and y in x) or (len(x) >= 3 and x in y): return True
    same = 0
    while same < len(x) and same < len(y) and x[same] == y[same]: same += 1
    if same >= 6: return True
    # 끝이 길게 겹치면("…소리가 들렸습니다. 쿵 하는 소리가 들렸습니다") 같은 말을 두 번 한 것이다.
    tail = 0
    while tail < len(x) and tail < len(y) and x[-1 - tail] == y[-1 - tail]: tail += 1
    return tail >= 5


# 반복 검사용 실제 값 — 변수가 실제로 채워졌을 때 같은 말이 겹치는지 본다.
SAMPLE = {"room": "저장고", "iroom": "발전실", "sroom": "정비실", "droom": "코어실", "to": "경비실",
          "from": "정비실", "prev": "코어실", "next": "경비실", "who": "토끼", "who2": "여우", "dname": "고양이",
          "target": "늑대", "trait": "겁이 많은 편", "task": "자재 생산", "time": "22시 40분",
          "mood": "괜찮음", "n": "두", "subject": "이상현상"}


def fill(t, formal):
    what = "설비가 멈췄습니다." if formal else "설비가 멈췄어요."
    sound = "쿵 하는 소리가 들렸습니다." if formal else "쿵 하는 소리가 들렸어요."
    t = t.replace("{what}", what).replace("{sound}", sound)
    return VAR.sub(lambda m: SAMPLE.get(m.group(1), m.group(1)), t)


def self_repeat(t):
    parts = [p for p in re.split(r"(?<=[.!?])\s+", t) if p]
    return any(repeats(parts[i], parts[j]) for i in range(len(parts)) for j in range(i + 1, len(parts)))


def particle_lint(t):
    at = 0
    while True:
        at = t.find("}", at)
        if at < 0: return ""
        at += 1
        rest = t[at:]
        if any(rest.startswith(a + "/" + b) for a, b in PAIRS): continue
        for b in BARE:
            if rest.startswith(b):
                return f"변수 뒤 맨 조사 '{b}'"


def parse(path):
    owner, slot = "any", ""
    out = collections.OrderedDict()
    errs = []
    for n, raw in enumerate(open(path, encoding="utf-8"), 1):
        line = raw.strip()
        if not line or (line.startswith("#") and not line.startswith("## ")): continue
        if line.startswith("@char "): owner = line[6:].strip(); slot = ""; continue
        if line.startswith("## "): slot = line[3:].strip(); out.setdefault((owner, slot), []); continue
        if line.startswith("- "):
            if not slot: errs.append(f"{n}: 슬롯 없는 문장"); continue
            out.setdefault((owner, slot), []).append((n, line[2:].strip()))
            continue
        errs.append(f"{n}: 알 수 없는 줄: {line}")
    return out, errs


def lint_file(cid):
    path = os.path.join(LINES, cid + ".txt")
    slots, errs = parse(path)
    warns = []
    formal = cid in FORMAL
    all_lines = []
    for (owner, slot), lines in slots.items():
        if owner != cid: errs.append(f"@char {owner} 가 {cid}.txt 에 있음"); continue
        if slot not in ALLOWED and not slot.startswith("phrase."):
            warns.append(f"[{slot}] 알 수 없는 슬롯(오타?)")
        if not lines: errs.append(f"[{slot}] 문장이 없음")
        for n, t in lines:
            all_lines.append((slot, t))
            if self_repeat(fill(t, formal)):
                errs.append(f"{n} [{slot}] 한 문장 틀 안에서 같은 말을 두 번 함: {t}")
            # 근무 기억 문장은 여러 질문 뒤에 붙는다 — 앞 문장이 장소를 말했다고 가정하면 안 된다.
            if slot.startswith("mem.") and re.search(r"(거기|그리로|그쪽|그 방)", t) and not re.search(r"\{i?room\}", t):
                errs.append(f"{n} [{slot}] 근무 기억 문장의 '거기/그리로' 가 가리킬 곳이 없다 — {{room}} 을 쓰세요: {t}")
            p = particle_lint(t)
            if p: errs.append(f"{n} [{slot}] {p}: {t}")
            for v in VAR.findall(t):
                if slot in ALLOWED and v not in ALLOWED[slot]:
                    errs.append(f"{n} [{slot}] 이 슬롯에서 쓸 수 없는 변수 {{{v}}}: {t}")
            end = re.sub(r"[.!?…~\s]+$", "", t)
            if slot in ("opener.repeat",) or slot.startswith(("time.", "phrase.")): continue
            if formal and re.search(r"(요|죠|네요|거든요|잖아요)$", end):
                warns.append(f"{n} [{slot}] 늑대는 합쇼체 — 해요체로 끝남: {t}")
            if not formal and re.search(r"(습니다|습니까|입니다|십시오)$", end):
                warns.append(f"{n} [{slot}] 해요체 캐릭터가 합쇼체로 끝남: {t}")
    have = {s for (o, s) in slots}
    for s in REQUIRED:
        if s not in have: errs.append(f"필수 슬롯 없음: {s}")

    texts = [t for _, t in all_lines]
    total = max(1, len(texts))
    if cid == "sheep":
        ell = sum(("..." in t or "…" in t) for t in texts)
        stut = sum(bool(re.search(r"(^|\s)(\S),\s\2", t)) for t in texts)
        if ell / total > 0.3: warns.append(f"말줄임 비율 {ell}/{total} — 30% 이하 권장")
        if stut > total * 0.12: warns.append(f"더듬기 {stut}/{total} — 12% 이하 권장")
    if cid == "rabbit":
        ex = sum("!" in t for t in texts)
        if ex / total > 0.45: warns.append(f"느낌표 문장 {ex}/{total} — 45% 이하 권장")
        multi = [t for t in texts if t.count("!") >= 2]
        for t in multi: warns.append(f"느낌표 2개 이상: {t}")
    if cid == "dog":
        yes = sum(t.startswith(("네", "예")) for t in texts)
        if yes / total > 0.15: warns.append(f"'네'로 시작 {yes}/{total} — 15% 이하 권장")
    if cid == "wolf":
        for bad in ("크큭", "흥", "겁먹었나", "후후"):
            for t in texts:
                if bad in t: errs.append(f"늑대 금지 표현 '{bad}': {t}")
    return errs, warns, len(texts), len(have)


def main():
    ids = sys.argv[1:] or IDS
    bad = False
    for cid in ids:
        errs, warns, n, s = lint_file(cid)
        print(f"== {cid}: 슬롯 {s}개 · 문장 {n}개 · ERROR {len(errs)} · WARN {len(warns)}")
        for e in errs: print("  ERROR", e)
        for w in warns: print("  WARN ", w)
        bad |= bool(errs)
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
