"""수리 완료를 알리는 "띵동" 효과음을 만든다.

기존 효과음과 같은 규격(22050Hz / 16bit / 모노)이고 표준 라이브러리만 쓴다.

    python tools/make_repair_ding.py

만드는 파일
  assets/audio/sfx/ding.wav   작업실 수리가 끝났을 때의 두 음 차임(솔 → 미, 띵—동—)

작업 완료음(task_done)과 헷갈리지 않게 두 음으로 또렷하게 떨어뜨렸다.
"""
import math
import os
import struct
import wave

RATE = 22050
OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "assets", "audio", "sfx"))


def write(name, samples, peak=0.82):
    hi = max(1e-6, max(abs(s) for s in samples))
    k = peak / hi
    data = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * k)) * 32767)) for s in samples)
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data)
    print("%-16s %5.2fs" % (name, len(samples) / RATE))


def bell(freq, seconds, decay):
    """종처럼 울리는 한 음. 배음을 살짝 어긋나게 쌓아 금속 질감을 낸다."""
    n = int(RATE * seconds)
    out = []
    for i in range(n):
        t = i / RATE
        v = (1.00 * math.sin(2 * math.pi * freq * t) * math.exp(-t * decay)
             + 0.42 * math.sin(2 * math.pi * freq * 2.01 * t) * math.exp(-t * decay * 1.9)
             + 0.18 * math.sin(2 * math.pi * freq * 3.02 * t) * math.exp(-t * decay * 3.1)
             + 0.08 * math.sin(2 * math.pi * freq * 4.97 * t) * math.exp(-t * decay * 4.4))
        out.append(v * min(1.0, t / 0.002))     # 아주 짧은 어택
    return out


def mix_at(dst, start, src, gain=1.0):
    for i, v in enumerate(src):
        j = start + i
        if j < len(dst):
            dst[j] += v * gain


def ding_dong():
    total = int(RATE * 1.05)
    out = [0.0] * total
    mix_at(out, 0, bell(784.0, 0.95, 5.2), 1.0)                 # 띵 (G5)
    mix_at(out, int(RATE * 0.17), bell(659.3, 0.88, 4.6), 0.92)  # 동 (E5)
    return out


if __name__ == "__main__":
    write("ding.wav", ding_dong(), peak=0.80)
