"""프롤로그/DAY0 연출에 필요한 임시 효과음을 만든다.

외부 에셋 없이 파이썬 표준 라이브러리만으로 합성한다(프로젝트의 기존 sfx 와 같은
22050Hz / 16bit / 모노 규격). 최종 사운드가 준비되면 같은 파일명으로 덮어쓰면 된다.

    python tools/make_prologue_sfx.py

만드는 파일 (assets/audio/sfx/):
    siren.wav          공습경보식 사이렌 — 이어 붙여도 끊기지 않게 루프 규격
    footsteps_run.wav  구두를 신고 복도를 뛰는 소리
    rubble_collapse.wav 구조물이 무너지는 소리
    glass_shatter.wav  유리/집기가 박살나는 소리
    impact_blunt.wav   머리를 둔기로 맞은 듯한 충격음
    body_fall.wav      사람이 책상에 엎어지는 "퍽" 소리
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "audio", "sfx")


def write(name, samples, peak=0.85):
    """[-1,1] 실수 리스트를 16bit 모노 wav 로 저장한다(피크 정규화)."""
    hi = max(1e-6, max(abs(s) for s in samples))
    k = peak / hi
    data = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * k)) * 32767)) for s in samples)
    path = os.path.normpath(os.path.join(OUT, name))
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data)
    print("%-22s %6.2fs" % (name, len(samples) / RATE))


def lowpass(src, cutoff):
    """1차 IIR 저역통과 — 노이즈를 '멀고 묵직하게' 만든다."""
    a = math.exp(-2.0 * math.pi * cutoff / RATE)
    out, prev = [], 0.0
    for s in src:
        prev = s * (1 - a) + prev * a
        out.append(prev)
    return out


def highpass(src, cutoff):
    lp = lowpass(src, cutoff)
    return [s - l for s, l in zip(src, lp)]


def noise(n):
    return [random.uniform(-1.0, 1.0) for _ in range(n)]


def sec(t):
    return int(RATE * t)


# --- 사이렌 : 음정이 오르내리는 톱니파. 시작/끝 위상이 같아 루프해도 딸깍거리지 않는다 ---
def make_siren(duration=4.0, lo=430.0, hi=760.0):
    n = sec(duration)
    out, phase = [], 0.0
    for i in range(n):
        t = i / n
        f = lo + (hi - lo) * (0.5 - 0.5 * math.cos(2 * math.pi * t))  # 한 주기 왕복
        phase = (phase + f / RATE) % 1.0
        saw = 2.0 * phase - 1.0
        out.append(0.55 * saw + 0.25 * math.sin(2 * math.pi * phase))
    out = lowpass(out, 2600)
    hum = [0.10 * math.sin(2 * math.pi * 60 * i / RATE) for i in range(n)]
    return [a + b for a, b in zip(out, hum)]


# --- 달리는 구두 소리 : 딱딱한 어택 + 낮은 발구름 ---
def make_footsteps(steps=9, gap=0.17):
    n = sec(gap * steps + 0.25)
    out = [0.0] * n
    for s in range(steps):
        start = sec(s * gap + random.uniform(-0.012, 0.012))
        heel = 1.0 - 0.04 * s                      # 멀어지지 않고 일정하게
        click = highpass(noise(sec(0.045)), 2200)
        for i, v in enumerate(click):
            if start + i >= n:
                break
            out[start + i] += v * heel * math.exp(-i / sec(0.010))
        f0 = random.uniform(95, 130)
        for i in range(sec(0.12)):
            if start + i >= n:
                break
            env = math.exp(-i / sec(0.035))
            out[start + i] += 0.8 * heel * env * math.sin(2 * math.pi * f0 * i / RATE)
    return out


# --- 무너지는 소리 : 저역 럼블 + 흩어지는 파편 타격 ---
def make_collapse(duration=2.3):
    n = sec(duration)
    rumble = lowpass(noise(n), 160)
    out = []
    for i, v in enumerate(rumble):
        t = i / n
        env = min(1.0, t * 14) * math.exp(-t * 2.2)
        out.append(v * env * 1.6)
    for _ in range(26):
        start = sec(random.uniform(0.02, duration * 0.8))
        hit = highpass(noise(sec(0.09)), random.uniform(700, 2600))
        for i, v in enumerate(hit):
            if start + i >= n:
                break
            out[start + i] += v * 0.55 * math.exp(-i / sec(0.020))
    return out


# --- 유리 파열 : 밝은 파열음 + 잘게 떨어지는 파편 ---
def make_glass(duration=1.5):
    n = sec(duration)
    out = [0.0] * n
    burst = highpass(noise(sec(0.20)), 2800)
    for i, v in enumerate(burst):
        out[i] += v * math.exp(-i / sec(0.045)) * 1.2
    for _ in range(70):
        start = sec(random.uniform(0.03, duration * 0.85))
        f = random.uniform(1800, 6500)
        length = sec(random.uniform(0.02, 0.08))
        amp = random.uniform(0.10, 0.45) * (1.0 - start / n)
        for i in range(length):
            if start + i >= n:
                break
            out[start + i] += amp * math.exp(-i / sec(0.012)) * math.sin(2 * math.pi * f * i / RATE)
    return out


# --- 둔기 충격 : 머리를 세게 맞은 듯한 낮은 '퍽' + 짧은 크랙 ---
def make_impact(duration=0.55):
    n = sec(duration)
    out = []
    for i in range(n):
        t = i / RATE
        f = 150.0 * math.exp(-t * 16)             # 급격히 내려앉는 피치
        env = math.exp(-t * 9.0)
        out.append(1.0 * env * math.sin(2 * math.pi * f * t))
    crack = lowpass(noise(sec(0.10)), 1400)
    for i, v in enumerate(crack):
        out[i] += v * 0.9 * math.exp(-i / sec(0.018))
    return out


# --- 책상에 엎어지는 소리 : 둔탁한 몸통 충격 + 나무 울림 + 옷 스침 ---
def make_body_fall(duration=0.9):
    n = sec(duration)
    out = [0.0] * n
    for i in range(n):
        t = i / RATE
        out[i] += 0.9 * math.exp(-t * 13) * math.sin(2 * math.pi * 88 * math.exp(-t * 6) * t)
    wood = lowpass(noise(sec(0.22)), 900)
    for i, v in enumerate(wood):
        out[i] += v * 0.8 * math.exp(-i / sec(0.030))
    cloth = highpass(noise(sec(0.35)), 1800)
    for i, v in enumerate(cloth):
        idx = sec(0.05) + i
        if idx >= n:
            break
        out[idx] += v * 0.18 * math.exp(-i / sec(0.12))
    return out


if __name__ == "__main__":
    random.seed(20260918)
    write("siren.wav", make_siren(), peak=0.70)
    write("footsteps_run.wav", make_footsteps())
    write("rubble_collapse.wav", make_collapse())
    write("glass_shatter.wav", make_glass())
    write("impact_blunt.wav", make_impact(), peak=0.95)
    write("body_fall.wav", make_body_fall(), peak=0.92)
