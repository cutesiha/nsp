"""프롤로그/DAY0 전용 신규 타이핑 보이스와 무전 효과음을 만든다.

기존 직원 6명(cat/crow/fox/jellyfish/owl/rabbit)의 보이스는 건드리지 않는다.
여기서 만드는 것은 새로 추가되는 화자 두 명과, 직원 보이스에 씌울 무전 질감뿐이다.

    python tools/make_prologue_voices.py

만드는 파일
  assets/audio/voice/director_01..03.wav   연구소 총괄 관리자 — 낮고 차분하며 무게감 있는 블립
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


# ── 총괄 관리자 ──────────────────────────────────────────────────────────
# 낮은 기음 + 한 옥타브 아래 서브. 피치가 살짝 내려앉아 "무게 있게 말을 맺는" 느낌.
# 고역을 크게 깎아 직원들의 밝은 블립과 확실히 구분된다.
def director_blip(f0, seconds=0.065):
    n = int(VOICE_RATE * seconds)
    out = []
    for i in range(n):
        t = i / VOICE_RATE
        f = f0 * (1.0 - 0.10 * (t / seconds))          # 끝으로 갈수록 약간 하강
        ph = 2 * math.pi * f * t
        v = 0.75 * math.sin(ph) + 0.22 * math.sin(2 * ph) + 0.30 * math.sin(ph * 0.5)
        attack = min(1.0, t / 0.004)                    # 부드러운 어택
        decay = math.exp(-t * 34)
        out.append(v * attack * decay)
    return lowpass(out, 1150, VOICE_RATE)


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
    random.seed(20260919)

    for i, f in enumerate((158.0, 172.0, 186.0), start=1):
        write(os.path.join(VOICE_DIR, "director_%02d.wav" % i), director_blip(f), VOICE_RATE, peak=0.80)

    for i, f in enumerate((565.0, 640.0, 712.0), start=1):
        write(os.path.join(VOICE_DIR, "guide0_%02d.wav" % i), guide_blip(f), VOICE_RATE, peak=0.72)

    write(os.path.join(SFX_DIR, "radio_click_on.wav"), radio_click(0.11, True), SFX_RATE, peak=0.75)
    write(os.path.join(SFX_DIR, "radio_click_off.wav"), radio_click(0.14, False), SFX_RATE, peak=0.70)
    write(os.path.join(SFX_DIR, "radio_static.wav"), radio_static(), SFX_RATE, peak=0.55)
