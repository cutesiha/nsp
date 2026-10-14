"""프롤로그 보강 연출에 필요한 추가 효과음을 만든다.

make_prologue_sfx.py 와 같은 규격(22050Hz / 16bit / 모노)이고, 표준 라이브러리만 쓴다.
기존 파일은 건드리지 않는다 — 여기서 만드는 건 전부 새 파일이다.

    python tools/make_prologue_sfx_extra.py

만드는 파일 (assets/audio/sfx/):
    intro_machine.wav   게임 시작 직후의 낮은 기계음(기록 영상이 걸리는 소리)
    crt_on.wav          CRT 브라운관이 탁 하고 켜지는 소리
    alert_beep3.wav     긴급 경보 "삐- 삐- 삐-"
    gauge_tick.wav      출력 게이지가 한 칸 떨어질 때의 짧은 경고음
    radio_cut.wav       무전이 치직 하고 끊기는 소리
    tinnitus.wav        기절 직후의 이명 "삐————"
    breath_faint.wav    의식이 돌아올 때 희미하게 들리는 숨소리
    window_open.wav     시스템 창이 뜨는 "띠롱"
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "audio", "sfx")
random.seed(70401)


def write(name, samples, peak=0.85):
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


def mix(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, s in enumerate(t):
            out[i] += s
    return out


def pad(src, n):
    return src + [0.0] * max(0, n - len(src))


def at(dst, start, src, gain=1.0):
    """dst 의 start 지점에 src 를 겹쳐 쓴다(길이가 모자라면 늘린다)."""
    need = start + len(src)
    if need > len(dst):
        dst.extend([0.0] * (need - len(dst)))
    for i, s in enumerate(src):
        dst[start + i] += s * gain
    return dst


def tone(freq, dur, amp=1.0, fade_in=0.004, fade_out=0.01, harmonic=0.0):
    n = sec(dur)
    out = []
    for i in range(n):
        t = i / RATE
        v = math.sin(2 * math.pi * freq * t)
        if harmonic:
            v += harmonic * math.sin(4 * math.pi * freq * t)
        env = 1.0
        if t < fade_in:
            env *= t / fade_in
        rest = dur - t
        if rest < fade_out:
            env *= max(0.0, rest / fade_out)
        out.append(v * amp * env)
    return out


# ── 게임 시작 직후의 기계음 ────────────────────────────────────────────────
# 낡은 재생 장치에 테이프가 걸리는 느낌 — 낮은 모터음 + 릴레이 딸깍.
def intro_machine():
    n = sec(1.35)
    out = []
    for i in range(n):
        t = i / RATE
        spin = 1.0 - math.exp(-t * 6.0)           # 모터가 서서히 돈다
        f = 52.0 + 18.0 * spin
        v = math.sin(2 * math.pi * f * t) * 0.9
        v += math.sin(2 * math.pi * f * 2.02 * t) * 0.28
        v += math.sin(2 * math.pi * f * 3.01 * t) * 0.12
        env = min(1.0, t / 0.18) * max(0.0, min(1.0, (1.35 - t) / 0.35))
        out.append(v * env * spin)
    grit = lowpass(noise(n), 900)
    out = [s + g * 0.10 for s, g in zip(out, grit)]
    # 릴레이 딸깍 두 번
    for pos in (0.02, 1.02):
        clk = highpass(noise(sec(0.03)), 1800)
        clk = [s * math.exp(-i / sec(0.008)) for i, s in enumerate(clk)]
        at(out, sec(pos), clk, 0.55)
    return out


# ── CRT 가 켜지는 소리 ────────────────────────────────────────────────────
def crt_on():
    n = sec(0.75)
    out = [0.0] * n
    # 고압이 걸리는 "틱" + 화면이 부풀어 오르는 저역 쿵
    thump = []
    for i in range(sec(0.22)):
        t = i / RATE
        thump.append(math.sin(2 * math.pi * (120 - 60 * t / 0.22) * t) * math.exp(-t * 16))
    at(out, 0, thump, 0.9)
    tick = highpass(noise(sec(0.02)), 3000)
    tick = [s * math.exp(-i / sec(0.005)) for i, s in enumerate(tick)]
    at(out, 0, tick, 0.7)
    # 브라운관 특유의 고음 휘파람이 서서히 자리잡는다
    whine = []
    for i in range(sec(0.75)):
        t = i / RATE
        env = min(1.0, t / 0.18) * max(0.0, min(1.0, (0.75 - t) / 0.3))
        whine.append(math.sin(2 * math.pi * 2450 * t) * env)
    at(out, 0, whine, 0.16)
    hiss = lowpass(highpass(noise(n), 1200), 6000)
    out = [s + h * 0.07 for s, h in zip(out, hiss)]
    return out


# ── 긴급 경보 삐- 삐- 삐- ────────────────────────────────────────────────
def alert_beep3():
    out = [0.0] * sec(1.15)
    for k in range(3):
        b = tone(1180, 0.15, harmonic=0.35)
        b = [s + 0.4 * math.sin(2 * math.pi * 1770 * (i / RATE)) * (1 if 0 < i < len(b) else 0)
             for i, s in enumerate(b)]
        at(out, sec(0.02 + k * 0.36), b, 0.8)
    return out


# ── 게이지가 한 칸 떨어질 때 ─────────────────────────────────────────────
def gauge_tick():
    out = tone(760, 0.075, harmonic=0.25)
    low = tone(190, 0.11, amp=0.5, fade_out=0.06)
    return mix(pad(out, len(low)), low)


# ── 무전이 끊기는 치직 ───────────────────────────────────────────────────
def radio_cut():
    n = sec(0.42)
    src = highpass(noise(n), 700)
    src = lowpass(src, 3400)
    out = []
    for i, s in enumerate(src):
        t = i / RATE
        # 처음에 확 튀었다가 급격히 사라진다
        env = math.exp(-t * 9.0) * (1.0 if t > 0.004 else t / 0.004)
        # 스퀠치가 끊기며 한 번 덜컥인다
        if 0.10 < t < 0.13:
            env *= 0.25
        out.append(s * env)
    click = highpass(noise(sec(0.012)), 4000)
    at(out, 0, click, 0.8)
    return out


# ── 기절 직후의 이명 ─────────────────────────────────────────────────────
def tinnitus():
    dur = 2.6
    n = sec(dur)
    out = []
    for i in range(n):
        t = i / RATE
        # 아주 미세하게 흔들리는 고음 — 완전한 순음이면 기계음처럼 들린다
        wob = 1.0 + 0.0016 * math.sin(2 * math.pi * 5.5 * t)
        v = math.sin(2 * math.pi * 3850 * wob * t)
        v += 0.22 * math.sin(2 * math.pi * 5210 * t)
        env = min(1.0, t / 0.05) * max(0.0, min(1.0, (dur - t) / 1.1))
        out.append(v * env)
    return out


# ── 의식이 돌아올 때의 희미한 숨소리 ──────────────────────────────────────
def breath_faint():
    out = [0.0] * sec(2.4)
    for k, (start, inh, exh) in enumerate([(0.05, 0.42, 0.55), (1.20, 0.38, 0.52)]):
        # 들숨 — 고역이 살아 있는 바람 소리
        n = sec(inh)
        src = lowpass(highpass(noise(n), 500), 2600)
        seg = []
        for i, s in enumerate(src):
            t = i / n * math.pi
            seg.append(s * math.sin(t) ** 1.4)
        at(out, sec(start), seg, 0.9)
        # 날숨 — 더 낮고 길게
        n2 = sec(exh)
        src2 = lowpass(highpass(noise(n2), 260), 1500)
        seg2 = []
        for i, s in enumerate(src2):
            t = i / n2 * math.pi
            seg2.append(s * math.sin(t) ** 1.2)
        at(out, sec(start + inh + 0.06), seg2, 0.75)
    return out


# ── 시스템 창이 뜨는 띠롱 ────────────────────────────────────────────────
def window_open():
    out = [0.0] * sec(0.5)
    at(out, 0, tone(880, 0.10, harmonic=0.2), 0.8)
    at(out, sec(0.08), tone(1320, 0.16, harmonic=0.18), 0.7)
    return out


def main():
    write("intro_machine.wav", intro_machine(), peak=0.72)
    write("crt_on.wav", crt_on(), peak=0.80)
    write("alert_beep3.wav", alert_beep3(), peak=0.82)
    write("gauge_tick.wav", gauge_tick(), peak=0.70)
    write("radio_cut.wav", radio_cut(), peak=0.85)
    write("tinnitus.wav", tinnitus(), peak=0.62)
    write("breath_faint.wav", breath_faint(), peak=0.55)
    write("window_open.wav", window_open(), peak=0.70)


if __name__ == "__main__":
    main()
