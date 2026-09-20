"""프롤로그/DAY0 전용 신규 타이핑 보이스와 무전 효과음을 만든다.

기존 직원 6명(cat/crow/fox/jellyfish/owl/rabbit)의 보이스는 건드리지 않는다.
여기서 만드는 것은 새로 추가되는 화자 두 명과, 직원 보이스에 씌울 무전 질감뿐이다.

    python tools/make_prologue_voices.py

만드는 파일
  assets/audio/voice/director_01..03.wav   연구소 총괄 관리자 — 낮고 굵지만 작은 스피커에서도 들리는 블립
  assets/audio/voice/guide0_01..03.wav     GUIDE-0 — 밝고 깨끗한 디지털 블립(약간 높은 음역)
  assets/audio/sfx/radio_click_on.wav      무전 개시 "치직"
  assets/audio/sfx/radio_click_off.wav     무전 종료 "치직"
  assets/audio/sfx/radio_static.wav        무전 중 약한 잡음(루프용)

보이스는 기존 직원 보이스와 같은 규격(11025Hz / 16bit / 모노 / 40~70ms)으로 맞춘다.
최종 음원이 준비되면 같은 파일명으로 덮어쓰기만 하면 된다.
"""
import math
import os
import random
import struct
import wave

VOICE_RATE = 11025          # 기존 직원 보이스와 동일
SFX_RATE = 22050            # 기존 효과음과 동일
BASE = os.path.dirname(__file__)
VOICE_DIR = os.path.normpath(os.path.join(BASE, "..", "assets", "audio", "voice"))
SFX_DIR = os.path.normpath(os.path.join(BASE, "..", "assets", "audio", "sfx"))


def write(path, samples, rate, peak=0.85):
    hi = max(1e-6, max(abs(s) for s in samples))
    k = peak / hi
    data = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * k)) * 32767)) for s in samples)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(data)
    print("%-42s %5.3fs" % (os.path.basename(path), len(samples) / rate))


def lowpass(src, cutoff, rate):
    a = math.exp(-2.0 * math.pi * cutoff / rate)
    out, prev = [], 0.0
    for s in src:
        prev = s * (1 - a) + prev * a
        out.append(prev)
    return out


def highpass(src, cutoff, rate):
    return [s - l for s, l in zip(src, lowpass(src, cutoff, rate))]


def noise(n):
    return [random.uniform(-1.0, 1.0) for _ in range(n)]


# ── 총괄 관리자 ────────────────────────────────────────
# 중후하고 굵은 남성 저음.
#
# ★ 주의 — 이전 판은 기음을 f0/2, f0/4 에 실어 실제 에너지가 40~100Hz 에만
#   몰려 있었고, 저역을 620Hz 에서 두 번 깎았다. 노트북/모니터 스피커는 그
#   대역을 사실상 내지 못해서 "보이스가 아예 없다"로 들렸다.
#   그래서 기음을 남성 저음역(120Hz 언저리)으로 올리고, 들리는 무게는
#   200~1200Hz 의 배음(성대 공명)이 만들게 했다. 사람 귀는 배음 간격으로
#   음높이를 읽으므로, 작은 스피커에서도 "굵고 낮은 목소리"로 들린다.
def director_blip(f0, seconds=0.086):
    n = int(VOICE_RATE * seconds)
    # 배음 세기 — 1번째보다 2~3번째를 크게 잡아 가슴에서 울리는 질감을 낸다.
    harm = (0.42, 0.66, 0.52, 0.30, 0.18, 0.10, 0.06)
    out = []
    for i in range(n):
        t = i / VOICE_RATE
        f = f0 * (1.0 - 0.09 * (t / seconds))       # 끝으로 갈수록 살짝 내려앉는다
        ph = 2 * math.pi * f * t
        v = 0.14 * math.sin(ph * 0.5)               # 서브는 질감만 — 여기에 힐을 실으면 안 들린다
        for k, a in enumerate(harm, start=1):
            v += a * math.sin(ph * k)
        attack = min(1.0, t / 0.006)                # 느슬한 어택 = 무게감
        decay = math.exp(-t * 23)
        out.append(v * attack * decay)
    # 손상되지 않는 저역 럼블을 거두고(헤드룸 도둑 방지),
    # 고역은 2.2kHz 에서 깎아 직원들의 밝은 블립과 확실히 구분한다.
    return lowpass(highpass(out, 80, VOICE_RATE), 2200, VOICE_RATE)


# ── GUIDE-0 ─────────────────────────────────────────────────────────────
# 밝고 깨끗한 디지털 블립. 살짝 올라가는 짧은 처프로 명랑하게 들리되,
# 고역을 3.5kHz 에서 잘라 길게 들어도 귀가 피곤하지 않게 한다.
def guide_blip(f0, seconds=0.042):
    n = int(VOICE_RATE * seconds)
    out = []
    for i in range(n):
        t = i / VOICE_RATE
        f = f0 * (1.0 + 0.12 * (t / seconds))          # 끝으로 갈수록 살짝 상승
        ph = 2 * math.pi * f * t
        tri = 2.0 / math.pi * math.asin(math.sin(ph))  # 삼각파 — 사인보다 밝고 톱니보다 부드럽다
        v = 0.70 * math.sin(ph) + 0.35 * tri
        attack = min(1.0, t / 0.002)
        decay = math.exp(-t * 52)
        out.append(v * attack * decay)
    return lowpass(out, 3500, VOICE_RATE)


# ── 무전 스퀠치 "치직" ───────────────────────────────────────────────────
def radio_click(seconds=0.11, bright=True):
    n = int(SFX_RATE * seconds)
    src = noise(n)
    band = highpass(lowpass(src, 2800 if bright else 2200, SFX_RATE), 700, SFX_RATE)
    out = []
    for i, v in enumerate(band):
        t = i / SFX_RATE
        # 짧게 터졌다가 바로 잦아드는 두 번의 지직
        env = math.exp(-t * 44) + 0.45 * math.exp(-max(0.0, t - 0.035) * 60)
        out.append(v * env)
    return out


# ── 무전 배경 잡음(루프) ─────────────────────────────────────────────────
# 타이핑 보이스를 덮지 않도록 대역을 좁히고 아주 낮은 레벨로만 쓴다(재생 시 -22dB).
def radio_static(seconds=1.6):
    n = int(SFX_RATE * seconds)
    band = highpass(lowpass(noise(n), 2600, SFX_RATE), 600, SFX_RATE)
    out = []
    for i, v in enumerate(band):
        t = i / seconds / SFX_RATE * seconds
        wobble = 0.85 + 0.15 * math.sin(2 * math.pi * 3.1 * i / SFX_RATE)
        # 시작/끝 40ms 를 페이드해 루프 이음새가 튀지 않게 한다
        edge = min(1.0, i / (SFX_RATE * 0.04), (n - i) / (SFX_RATE * 0.04))
        out.append(v * wobble * edge)
    return out


if __name__ == "__main__":
    import sys
    random.seed(20260919)
    # 인자를 주면 그 그룹만 다시 만든다: director / guide0 / radio
    only = sys.argv[1] if len(sys.argv) > 1 else ""

    if only in ("", "director"):
        for i, f in enumerate((116.0, 126.0, 136.0), start=1):
            write(os.path.join(VOICE_DIR, "director_%02d.wav" % i), director_blip(f), VOICE_RATE, peak=0.88)
    if only == "director":
        raise SystemExit(0)

    for i, f in enumerate((565.0, 640.0, 712.0), start=1):
        write(os.path.join(VOICE_DIR, "guide0_%02d.wav" % i), guide_blip(f), VOICE_RATE, peak=0.72)

    write(os.path.join(SFX_DIR, "radio_click_on.wav"), radio_click(0.11, True), SFX_RATE, peak=0.75)
    write(os.path.join(SFX_DIR, "radio_click_off.wav"), radio_click(0.14, False), SFX_RATE, peak=0.70)
    write(os.path.join(SFX_DIR, "radio_static.wav"), radio_static(), SFX_RATE, peak=0.55)
