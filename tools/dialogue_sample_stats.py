"""대사 샘플(tools/dialogue_samples/<id>.md) 검수 수치 — CLAUDE_CODE_TASK_dialogue_AB.md A-7.

    py -3 tools/dialogue_sample_stats.py            # 전체
    py -3 tools/dialogue_sample_stats.py fox 20     # 여우 · 어색한 답 20개도 같이 출력

세는 것(캐릭터마다):
  · 문장 수가 MaxSentences 를 넘는 답(양·토끼는 보정(caveat)이 붙은 답에 한해 +1 허용)
    - 조립기가 덧붙여서(여는 말 · 인상 · 덧붙임 · 되묻기 · 기억) 넘은 것 — 0 이어야 한다
    - 핵심 틀 자체가(또는 핵심 + 보정이) 넘은 것 — 틀을 손봐야 줄어든다(보정은 사실 정확성 때문에 못 뺀다)
  · 물음표가 2개 이상인 답
  · deny / nosight / noanomaly / status.* 틀에 기억[...] 이 실제로 붙은 답
  · (닫힘) 표시된 답 뒤에 덧붙임 · 되묻기 · 기억 · 인상이 붙은 답
문장 경계는 DialogueComposer.SentenceCount 와 같다 — [.!?~…] 뒤에 공백.
"""
import os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLES = os.path.join(ROOT, "tools", "dialogue_samples")
VOICES = os.path.join(ROOT, "data", "dialogue", "voices")
IDS = ["rabbit", "cat", "fox", "sheep", "wolf", "dog"]
CAVEAT_PLUS_ONE = {"sheep", "rabbit"}

BREAK = re.compile(r"[.!?~…](?=\s)")
LINE = re.compile(r"^- (.*) <sub>틀: (.*)</sub>$")
MEM = re.compile(r"기억\[([^\]]*)\]")
PARTS = re.compile(r"조각\[([^\]]*)\]")


def sentences(text):
    return len(BREAK.findall(text)) + 1


def max_sentences(cid):
    path = os.path.join(VOICES, f"{cid}_voice.tres")
    m = re.search(r"^MaxSentences = (\d+)", open(path, encoding="utf-8").read(), re.M)
    return int(m.group(1)) if m else 3


def kept_memories(trace):
    m = MEM.search(trace)
    if not m: return []
    return [s for s in m.group(1).split(", ") if s and not s.endswith("(잘림)")]


def parts(trace):
    m = PARTS.search(trace)
    return m.group(1).split(", ") if m else []


def load(cid):
    rows = []
    kind = ""
    for raw in open(os.path.join(SAMPLES, cid + ".md"), encoding="utf-8"):
        line = raw.rstrip("\n")
        if line.startswith("### "): kind = line[4:]
        m = LINE.match(line)
        if m: rows.append((kind, m.group(1), m.group(2)))
    return rows


def stats(cid, show=0):
    rows = load(cid)
    cap = max_sentences(cid)
    over, over_core, q2, memdeny, closed_plus = [], [], [], [], []
    for kind, ans, tr in rows:
        n = sentences(ans)
        slot = tr.split(" + ")[0].replace("(닫힘)", "")
        # 보정은 조립기의 "덧붙임"이 아니다 — 사실 정확성 때문에 반드시 붙는 하한이다.
        added = kept_memories(tr) or [p for p in parts(tr) if p != "보정"]
        has_caveat = "보정" in parts(tr)
        limit = cap + (1 if cid in CAVEAT_PLUS_ONE and has_caveat else 0)
        if n > limit:
            (over if added else over_core).append((kind, n, ans, tr))
        if ans.count("?") >= 2: q2.append((kind, ans, tr))
        if re.match(r"(deny|nosight|noanomaly|status\.)", slot) and kept_memories(tr):
            memdeny.append((kind, ans, tr))
        if "(닫힘)" in tr and (kept_memories(tr) or any(p in ("덧붙임", "되묻기", "인상") for p in parts(tr))):
            closed_plus.append((kind, ans, tr))
    print(f"== {cid} (MaxSentences {cap}) · 답 {len(rows)}개")
    print(f"   상한 초과(조립기가 덧붙여서) {len(over)} · 상한 초과(핵심 틀 자체 · 핵심+보정) {len(over_core)}")
    print(f"   물음표 2개 이상 {len(q2)} · deny/nosight/noanomaly/status 틀에 기억 붙음 {len(memdeny)}"
          f" · (닫힘) 뒤에 덧붙임/되묻기/기억/인상 {len(closed_plus)}")
    if show:
        print(f"   -- 어색 후보 {show}개 (상한 초과 · 물음표 2개 · 닫힘 뒤 덧붙임 순)")
        seen, k = set(), 0
        for kind, *rest in over + over_core + [(a, 0, b, c) for a, b, c in q2] + [(a, 0, b, c) for a, b, c in closed_plus]:
            ans = rest[1] if len(rest) == 3 else rest[0]
            tr = rest[2] if len(rest) == 3 else rest[1]
            if ans in seen: continue
            seen.add(ans); k += 1
            print(f"   {k:2d}. [{kind}] {ans}  ({tr})")
            if k >= show: break
    return {"over": len(over), "over_core": len(over_core), "q2": len(q2), "memdeny": len(memdeny),
            "closed_plus": len(closed_plus), "core_slots": collections.Counter(r[3].split(' + ')[0] for r in over_core)}


def main():
    ids = [a for a in sys.argv[1:] if a in IDS] or IDS
    show = next((int(a) for a in sys.argv[1:] if a.isdigit()), 0)
    for cid in ids:
        r = stats(cid, show)
        if r["over_core"]:
            print("   긴 핵심 틀:", ", ".join(f"{s}×{n}" for s, n in r["core_slots"].most_common(6)))


if __name__ == "__main__":
    main()
