# -*- coding: utf-8 -*-
"""
CCTV 직원 애니메이션 생성기 V2 — employee_common.tres 의 생성 클립 전부를 쓴다.

    python tools/gen_cctv_clips.py

  · 괴물 반응(첫 반응 · 반복)        — gen_ghost_reaction_clips.py 의 자세를 그대로 쓴다
  · 괴물이 사라진 뒤의 후속 반응 7종  — 양 바닥 떨림/기립 · 토끼 안도 · 고양이 땀 · 강아지 큰 숨 · 늑대 확인 · 여우 황당
  · 손으로 실제 물건을 만지는 작업 8종 × 체형 2 (panel_press_m / _f …)
  · 의자 앉기 / 일어나기, 침대에 눕기 / 일어나기, 결박된 발버둥
  · 박스 집기 / 나르기 / 내려놓기 — 남성 · 여성 · 양

손 위치는 IK 로 맞춘다: "손이 여기(몸 기준 좌표)에 있어야 한다"를 적으면 팔 각도를 푼다.
그래서 박스를 드는 손은 박스 옆면에, 버튼을 누르는 손은 패널 면에 정확히 간다.

생성 뒤 scripts/view/WorkClipReach.cs 도 같이 쓴다 — 클립마다 "몸 중심에서 작업 손까지 앞 거리".
RoomWorkVisualController 가 이 값으로 직원을 작업 대상(InteractionTarget)에서 알맞은 거리에 세운다.
여러 번 돌려도 된다(같은 이름의 클립은 지우고 다시 넣는다). 손으로 만든 다른 클립은 건드리지 않는다.
"""
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_ghost_reaction_clips as G  # noqa: E402
from gen_ghost_reaction_clips import (  # noqa: E402
    pose, merge, add, arms, legs, kneel, Clip, emit, tremble, ORDER, PATHS, LIB, ROOT)

REACH_CS = os.path.join(ROOT, "scripts", "view", "WorkClipReach.cs")

# 예전 생성 클립 중 V2 에서 이름이 바뀌어 더는 쓰지 않는 것(라이브러리에서 지운다).
RETIRED = ["ghost_rabbit_recover", "ghost_cat_recover", "ghost_dog_recover", "ghost_wolf_recover"]

# ── 체형 치수(EmployeeCctvBase_F / _M 의 실제 노드 오프셋) ─────────────────────
DIMS = {
    "F": dict(hips=0.825, torso=0.09, chest=0.19, neck=0.19, head=0.09,
              sh=(0.1165, 0.145), ua=(0.044, -0.02), la=0.28, hand=0.26,
              ul=(0.09, -0.02), ll=0.40, foot=0.345),
    "M": dict(hips=1.00, torso=0.115, chest=0.245, neck=0.21, head=0.09,
              sh=(0.128, 0.165), ua=(0.053, -0.02), la=0.315, hand=0.29,
              ul=(0.085, -0.02), ll=0.50, foot=0.42),
}
PALM = 0.06   # 손 노드(손목)에서 손바닥 중심까지


# ── FK ────────────────────────────────────────────────────────────────────

def _mm(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def _mv(a, v):
    return [sum(a[i][k] * v[k] for k in range(3)) for i in range(3)]


def _euler(deg):
    x, y, z = (math.radians(d) for d in deg)
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]
    return _mm(_mm(ry, rx), rz)   # Godot 기본 YXZ


class _T:
    def __init__(self, r=None, t=(0.0, 0.0, 0.0)):
        self.r = r or [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
        self.t = list(t)

    def child(self, off, rot_deg=(0, 0, 0)):
        return _T(_mm(self.r, _euler(rot_deg)), [a + b for a, b in zip(_mv(self.r, off), self.t)])

    def point(self, local):
        return [a + b for a, b in zip(_mv(self.r, local), self.t)]


def fk(body, p):
    """자세 p 의 주요 점(캐릭터 루트 기준). x 오른쪽 · y 위 · -z 앞."""
    d = DIMS[body]
    root = _T(t=p["VR"])
    hips = root.child((0, d["hips"], 0), p["HIPS"])
    chest = hips.child((0, d["torso"], 0), p["TORSO"]).child((0, d["chest"], 0), p["CHEST"])
    head = chest.child((0, d["neck"], 0)).child((0, d["head"], 0), p["HEAD"])
    out = {"hips": hips.t, "chest": chest, "head": head}
    for side, sx in (("L", -1), ("R", 1)):
        sh = chest.child((sx * d["sh"][0], d["sh"][1], 0), p["SH" + side])
        ua = sh.child((sx * d["ua"][0], d["ua"][1], 0), p["UA" + side])
        la = ua.child((0, -d["la"], 0), p["LA" + side])
        hand = la.child((0, -d["hand"], 0), p["HND" + side])
        out["palm" + side] = hand.point((0, -PALM, 0))
        out["elbow" + side] = la.t
        ul = hips.child((sx * d["ul"][0], d["ul"][1], 0), p["UL" + side])
        ll = ul.child((0, -d["ll"], 0), p["LL" + side])
        out["ankle" + side] = ll.child((0, -d["foot"], 0)).t
    return out


def head_point(body, p, local):
    return fk(body, p)["head"].point(local)


def chest_point(body, p, local):
    return fk(body, p)["chest"].point(local)


# ── IK — 손을 목표점에 ─────────────────────────────────────────────────────

def _solve_arm(body, p, side, target, seed):
    ua_key, la_key = "UA" + side, "LA" + side

    def cost(v):
        ua_x, out, la_x = v
        q = dict(p)
        q[ua_key] = (ua_x, p[ua_key][1], -out if side == "L" else out)
        q[la_key] = (la_x, p[la_key][1], p[la_key][2])
        h = fk(body, q)["palm" + side]
        e = sum((a - b) ** 2 for a, b in zip(h, target))
        # 팔을 옆으로 벌리는 것보다 앞으로 굽히는 쪽을 조금 더 좋게 본다(자연스러운 쪽).
        return e + 2e-7 * out * out + (1e-3 if la_x < 0 or la_x > 158 else 0) * (la_x ** 2 if la_x < 0 else (la_x - 158) ** 2)

    v = list(seed)
    best = cost(v)
    step = 16.0
    while step > 0.05:
        moved = False
        for i in range(3):
            for s in (step, -step):
                w = list(v)
                w[i] += s
                c = cost(w)
                if c < best:
                    best, v, moved = c, w, True
        if not moved:
            step *= 0.5
    return v, math.sqrt(max(0.0, best - 2e-7 * v[1] * v[1]))


def reach(body, p, L=None, R=None, name=""):
    """p 의 팔을 풀어 손바닥이 L/R(몸 기준 좌표)에 오게 한다. 못 닿으면 경고만 낸다."""
    q = dict(p)
    for side, target in (("L", L), ("R", R)):
        if target is None:
            continue
        best = None
        for seed in ((60, 8, 50), (100, 10, 90), (30, 4, 20), (140, 20, 110), (20, -8, 100)):
            v, err = _solve_arm(body, q, side, target, seed)
            if best is None or err < best[1]:
                best = (v, err)
        (ua_x, out, la_x), err = best
        q["UA" + side] = (ua_x, q["UA" + side][1], -out if side == "L" else out)
        q["LA" + side] = (la_x, q["LA" + side][1], q["LA" + side][2])
        if err > 0.025:
            print(f"  ! {name} {body}{side}: 손이 목표에서 {err * 100:.1f}cm 떨어짐 {target}")
    return q


def mirror_x(v):
    return (-v[0], v[1], v[2])


# ── 자세 부품 ─────────────────────────────────────────────────────────────

def stand(body, drop=0.0, **kw):
    return merge(legs(body, drop, kw.pop("thigh", 2.0)), arms(4, 3, 10), kw)


def kneel_one(body, knee="R", fwd_thigh=None):
    """한쪽 무릎을 바닥에 대고 다른 발은 앞에 딛는다(바닥 가까운 수리)."""
    b = G.BODY[body]
    knee_h = 0.075
    hip_joint = knee_h + b["l1"] * math.cos(math.radians(8))
    drop = (b["hip"] - 0.02) - hip_joint
    # 앞발: 정강이를 거의 세우고 넓적다리 각도를 푼다.
    need = hip_joint - G.ANKLE_REST
    c = (need - b["l2"] * math.cos(math.radians(-6))) / b["l1"]
    a = math.degrees(math.acos(max(-1.0, min(1.0, c)))) if fwd_thigh is None else fwd_thigh
    down = dict(thigh=8, knee=-86 - 8, foot=-180 + 86 + 8)
    up = dict(thigh=a, knee=-6 - a, foot=6)
    kl, kr = (down, up) if knee == "L" else (up, down)
    return {
        "VR": (0.0, -drop, 0.0),
        "ULL": (kl["thigh"], 0, -5), "ULR": (kr["thigh"], 0, 5),
        "LLL": (kl["knee"], 0, 0), "LLR": (kr["knee"], 0, 0),
        "FL": (kl["foot"], 0, 0), "FR": (kr["foot"], 0, 0),
    }


def seated_legs():
    # sit_typing / sit_assemble 과 같은 다리(골반 높이는 컨트롤러가 좌판에 맞춘다).
    return {"ULL": (90, 0, -3), "ULR": (90, 0, 3), "LLL": (-90, 0, 0), "LLR": (-90, 0, 0),
            "FL": (5.7, 0, 0), "FR": (5.7, 0, 0), "VR": (0, 0, 0)}


def walk_keys(body, T, stride, drop=0.02, lift=40, bob=0.012):
    """걷는 다리 4키(0 · T/4 · T/2 · 3T/4). 팔·몸통은 호출한 쪽이 덮는다."""
    return [
        (0.0, legs(body, drop, stride, -stride * 0.7)),
        (T * 0.25, merge(legs(body, drop - bob, 6, 6, lift_r=lift))),
        (T * 0.5, legs(body, drop, -stride * 0.7, stride)),
        (T * 0.75, merge(legs(body, drop - bob, 6, 6, lift_l=lift))),
    ]


# ── 작업 클립 ─────────────────────────────────────────────────────────────
# 목표 높이는 체형마다 따로 준다(키가 달라 같은 각도면 손 높이가 달라진다).

WORK_H = {  # 클립: (남성 손 높이, 여성 손 높이)
    "panel_press": (1.20, 1.04),
    "console_operate": (1.00, 0.95),
    "knob_turn": (1.26, 1.10),
    "lever_operate": (1.08, 0.95),
    "crouch_repair": (0.46, 0.42),
    "shelf_reach": (1.52, 1.36),
    "shelf_low": (0.58, 0.54),
    "clipboard_check": (1.12, 0.97),
}
WORK_FWD = 0.42   # 목표 앞 거리(대략) — 실제 값은 FK 로 다시 재서 WorkClipReach 에 쓴다


def work_clips(body):
    suf = "_" + body.lower()
    out = []
    f = WORK_FWD if body == "M" else WORK_FWD - 0.04

    # ① 패널 버튼·스위치 — 왼손은 패널을 짚고, 오른손이 버튼 셋과 스위치를 차례로 누른다.
    h = WORK_H["panel_press"][0 if body == "M" else 1]
    base = stand(body, TORSO=(-5, 0, 0), CHEST=(-3, 0, 0), HEAD=(-8, 0, 0))
    L0 = (-0.13, h, -f)
    seq = [(0.0, (0.10, h + 0.08, -f + 0.03), 0), (0.3, (0.10, h + 0.08, -f - 0.03), 6),
           (0.55, (0.15, h - 0.06, -f + 0.03), -4), (0.8, (0.15, h - 0.06, -f - 0.03), -2),
           (1.1, (0.21, h + 0.12, -f + 0.02), 8), (1.3, (0.21, h + 0.19, -f - 0.02), 10),
           (1.65, (0.10, h + 0.02, -f + 0.04), 4)]
    c = Clip("panel_press" + suf, 2.4, True)
    for t, r, hy in seq:
        q = reach(body, add(base, HEAD=(0, -hy, 0)), L=L0, R=r, name=c.name)
        c.key(t, q)
    c.key(1.95, reach(body, add(base, HEAD=(2, 10, 0)), L=(-0.13, h, -f - 0.03), R=(0.10, h + 0.02, -f + 0.05), name=c.name))
    out.append(c)

    # ② 작업대/콘솔 위 — 두 손이 상판 위에서 번갈아 조작한다(키 입력 · 슬라이더).
    h = WORK_H["console_operate"][0 if body == "M" else 1]
    base = stand(body, TORSO=(-16 if body == "M" else -9, 0, 0), CHEST=(-6, 0, 0), HEAD=(-18, 0, 0))
    c = Clip("console_operate" + suf, 2.2, True)
    pts = [((-0.15, h + 0.03, -f), (0.14, h, -f - 0.02)), ((-0.15, h, -f), (0.14, h + 0.04, -f + 0.01)),
           ((-0.13, h + 0.04, -f + 0.02), (0.18, h, -f - 0.05)), ((-0.12, h, -f - 0.03), (0.18, h + 0.03, -f)),
           ((-0.16, h + 0.03, -f), (0.10, h, -f - 0.03)), ((-0.16, h, -f - 0.02), (0.10, h + 0.03, -f))]
    for i, (l, r) in enumerate(pts):
        c.key(i * 2.2 / len(pts), reach(body, add(base, HEAD=(0, (-6, 4, 8, 2, -4, 0)[i], 0)), L=l, R=r, name=c.name))
    out.append(c)

    # ③ 다이얼 돌리기 — 왼손은 옆을 짚고, 오른손이 다이얼을 잡고 손목을 비튼다. 계기판을 올려다본다.
    h = WORK_H["knob_turn"][0 if body == "M" else 1]
    base = stand(body, TORSO=(-3, 0, 0), HEAD=(6, 0, 0))
    c = Clip("knob_turn" + suf, 2.6, True)
    for t, tw, lean in ((0.0, 0, 0), (0.45, 45, 1), (0.7, 45, 1), (1.05, 0, 0), (1.45, 50, 1), (1.75, 50, 1), (2.15, 5, 0)):
        q = reach(body, add(base, HEAD=(0, -6 * lean, 0)), L=(-0.16, h - 0.14, -f + 0.02), R=(0.07, h + 0.02, -f - 0.01), name=c.name)
        q["HNDR"] = (0, -tw, 0)
        c.key(t, q)
    out.append(c)

    # ④ 레버 — 두 손으로 손잡이를 잡고 몸을 실어 내렸다가 다시 올린다.
    h = WORK_H["lever_operate"][0 if body == "M" else 1]
    c = Clip("lever_operate" + suf, 2.4, True)
    for t, dy, dz, lean, drop in ((0.0, 0.0, 0.0, -6, 0.02), (0.5, -0.02, 0.0, -8, 0.03), (0.95, -0.16, 0.07, 4, 0.06),
                                  (1.3, -0.17, 0.08, 5, 0.06), (1.8, -0.02, 0.0, -7, 0.03)):
        base = stand(body, drop=drop, TORSO=(lean, 0, 0), HEAD=(-10, 0, 0))
        c.key(t, reach(body, base, L=(-0.05, h + dy, -f + dz), R=(0.07, h + dy + 0.01, -f + dz), name=c.name))
    out.append(c)

    # ⑤ 바닥 가까운 수리 — 오른 무릎을 꿇고 왼손으로 받치며 오른손 렌치를 돌린다(렌치 소품).
    h = WORK_H["crouch_repair"][0 if body == "M" else 1]
    base = merge(kneel_one(body, "R"), dict(TORSO=(-24, 0, 0), CHEST=(-8, 0, 0), HEAD=(-14, 0, 0)), arms(4, 3, 10))
    c = Clip("crouch_repair" + suf, 2.0, True)
    for t, r, hx in ((0.0, (0.10, h + 0.04, -f), 0), (0.35, (0.15, h + 0.01, -f - 0.02), 3),
                     (0.7, (0.11, h - 0.03, -f - 0.03), 0), (1.05, (0.07, h, -f - 0.01), -3), (1.5, (0.12, h + 0.03, -f), 2)):
        q = reach(body, add(base, HEAD=(hx, 0, 0)), L=(-0.14, h, -f + 0.04), R=r, name=c.name)
        q["HNDR"] = (0, 0, (-25, 10, 30, 0, -15)[int(t / 0.35) if t < 1.5 else 4])
        c.key(t, q)
    out.append(c)

    # ⑥ 선반 높은 칸 — 두 팔을 들어 물건을 잡고 옆으로 옮겨 정리한다.
    h = WORK_H["shelf_reach"][0 if body == "M" else 1]
    base = stand(body, TORSO=(2, 0, 0), HEAD=(16, 0, 0))
    c = Clip("shelf_reach" + suf, 2.8, True)
    for t, x0, dy in ((0.0, -0.08, 0.0), (0.5, -0.08, 0.05), (1.1, 0.12, 0.05), (1.4, 0.12, 0.0), (2.0, -0.02, -0.03)):
        c.key(t, reach(body, add(base, HEAD=(0, -x0 * 60, 0)), L=(x0 - 0.13, h + dy, -f + 0.04),
                       R=(x0 + 0.13, h + dy, -f + 0.04), name=c.name))
    out.append(c)

    # ⑦ 낮은 칸 · 상자 위 — 무릎과 허리를 굽혀 아래 칸의 물건을 꺼내고 넣는다.
    h = WORK_H["shelf_low"][0 if body == "M" else 1]
    base = merge(legs(body, 0.28 if body == "M" else 0.17, 52 if body == "M" else 44), dict(TORSO=(-34 if body == "M" else -30, 0, 0), CHEST=(-8, 0, 0), HEAD=(2, 0, 0)), arms(4, 3, 10))
    c = Clip("shelf_low" + suf, 2.6, True)
    for t, x0, dz in ((0.0, 0.10, 0.0), (0.55, 0.10, -0.05), (1.1, -0.10, -0.02), (1.6, -0.10, -0.06), (2.1, 0.02, 0.0)):
        c.key(t, reach(body, add(base, HEAD=(0, -x0 * 80, 0)), L=(x0 - 0.13, h, -f + dz), R=(x0 + 0.13, h, -f + dz), name=c.name))
    out.append(c)

    # ⑧ 점검 기록 — 왼손에 클립보드(소품), 오른손으로 적는다. 장비를 올려다봤다가 다시 적는다.
    h = WORK_H["clipboard_check"][0 if body == "M" else 1]
    base = stand(body, TORSO=(-3, 0, 0))
    c = Clip("clipboard_check" + suf, 3.0, True)
    for t, hx, px, py in ((0.0, -30, 0.02, 0.02), (0.3, -30, 0.06, 0.0), (0.6, -30, 0.02, -0.02), (0.9, -28, 0.07, -0.01),
                          (1.3, 6, 0.10, -0.08), (2.0, 8, 0.10, -0.08), (2.4, -30, 0.03, 0.01)):
        q = reach(body, add(base, HEAD=(hx, 0, 0)), L=(-0.06, h, -0.28), R=(0.04 + px, h + 0.03 + py, -0.29), name=c.name)
        q["HNDL"] = (0, 70, 0)
        c.key(t, q)
    out.append(c)
    return out


# ── 의자 · 침대 ───────────────────────────────────────────────────────────
# 골반 높이/앞뒤(VisualRoot)는 컨트롤러가 좌판 높이에 맞춰 같은 박자로 움직인다 — 클립에는 VR 이 없다.
# 박자: SIT_CURVE 는 "골반이 얼마나 내려갔는가(0~1)"를 클립 시간 비율로 적은 것. 컨트롤러와 같은 값이다.
SIT_CURVE = [(0.0, 0.0), (0.2, 0.12), (0.55, 0.72), (0.8, 1.0), (1.0, 1.0)]
STAND_CURVE = [(0.0, 1.0), (0.25, 0.95), (0.6, 0.35), (0.85, 0.0), (1.0, 0.0)]


def no_vr(c):
    c.no_vr = True
    return c


def seat_clips():
    out = []
    rest = merge(pose(), arms(4, 3, 10))
    sitting = merge(seated_legs(), dict(TORSO=(-12.6, 0, 0), CHEST=(-4.6, 0, 0), HEAD=(-9, 0, 0)),
                    arms(60, 7, 35.5, hand=-6))
    c = no_vr(Clip("chair_sit", 1.0, False))
    c.key(0.0, rest)
    c.key(0.2, merge({"ULL": (28, 0, -2), "ULR": (28, 0, 2), "LLL": (-34, 0, 0), "LLR": (-34, 0, 0), "FL": (6, 0, 0), "FR": (6, 0, 0)},
                     dict(TORSO=(-20, 0, 0), CHEST=(-4, 0, 0), HEAD=(6, 0, 0)), arms(18, 6, 24)))
    c.key(0.55, merge({"ULL": (72, 0, -3), "ULR": (72, 0, 3), "LLL": (-78, 0, 0), "LLR": (-78, 0, 0), "FL": (6, 0, 0), "FR": (6, 0, 0)},
                      dict(TORSO=(-26, 0, 0), CHEST=(-6, 0, 0), HEAD=(8, 0, 0)), arms(34, 8, 40)))
    c.key(0.8, merge(seated_legs(), dict(TORSO=(-16, 0, 0), CHEST=(-5, 0, 0), HEAD=(-4, 0, 0)), arms(48, 7, 38)))
    c.key(1.0, sitting)
    out.append(c)

    c = no_vr(Clip("chair_stand", 0.9, False))
    c.key(0.0, sitting)
    c.key(0.22, merge(seated_legs(), dict(TORSO=(-30, 0, 0), CHEST=(-6, 0, 0), HEAD=(6, 0, 0)), arms(28, 8, 58)))
    c.key(0.55, merge({"ULL": (52, 0, -3), "ULR": (52, 0, 3), "LLL": (-58, 0, 0), "LLR": (-58, 0, 0), "FL": (6, 0, 0), "FR": (6, 0, 0)},
                      dict(TORSO=(-24, 0, 0), CHEST=(-4, 0, 0), HEAD=(4, 0, 0)), arms(16, 6, 30)))
    c.key(0.78, merge({"ULL": (14, 0, -2), "ULR": (14, 0, 2), "LLL": (-16, 0, 0), "LLR": (-16, 0, 0)},
                      dict(TORSO=(-8, 0, 0), HEAD=(0, 0, 0)), arms(8, 4, 14)))
    c.key(0.9, rest)
    out.append(c)
    return out


def bed_clips():
    """침대 가장자리에 앉은 자세 ↔ 누운 자세. 몸 전체의 기울기(RigRoot)·위치는 컨트롤러가,
    팔다리는 이 클립이 맡는다. 끝 자세는 lying_idle / isolated 클립의 팔다리와 같다."""
    out = []
    edge = merge(seated_legs(), dict(TORSO=(-6, 0, 0), HEAD=(-6, 0, 0)), arms(10, 6, 20))
    lying = merge({"ULL": (4, 0, -3), "ULR": (4, 0, 3), "LLL": (-6, 0, 0), "LLR": (-6, 0, 0),
                   "FL": (-30, 0, 0), "FR": (-30, 0, 0), "VR": (0, 0, 0)},
                  dict(HEAD=(-10, 0, 0)), arms(8, 10, 12))
    c = no_vr(Clip("bed_lie_down", 1.6, False))
    c.key(0.0, edge)
    # 한 팔로 매트리스를 짚으며 상체를 뒤로 — 다리는 아직 침대 밖.
    c.key(0.45, merge(seated_legs(), dict(TORSO=(8, 0, 0), HEAD=(-18, 0, 0)), arms(-35, 18, 30, ua_x_r=10, out_r=8, la_r=20)))
    # 다리를 침대 위로 올린다.
    c.key(1.0, merge({"ULL": (40, 0, -3), "ULR": (40, 0, 3), "LLL": (-50, 0, 0), "LLR": (-50, 0, 0), "FL": (-10, 0, 0), "FR": (-10, 0, 0)},
                     dict(TORSO=(4, 0, 0), HEAD=(-14, 0, 0)), arms(-20, 14, 22)))
    c.key(1.6, lying)
    out.append(c)

    c = no_vr(Clip("bed_get_up", 1.5, False))
    c.key(0.0, lying)
    c.key(0.5, merge({"ULL": (40, 0, -3), "ULR": (40, 0, 3), "LLL": (-50, 0, 0), "LLR": (-50, 0, 0), "FL": (-10, 0, 0), "FR": (-10, 0, 0)},
                     dict(TORSO=(-6, 0, 0), HEAD=(-6, 0, 0)), arms(-30, 16, 28)))
    c.key(1.05, merge(seated_legs(), dict(TORSO=(-2, 0, 0), HEAD=(-8, 0, 0)), arms(-30, 18, 30)))
    c.key(1.5, edge)
    out.append(c)

    # 결박된 발버둥 — X 밴드·허리·다리 밴드에 막혀 크게 움직이지 못한다.
    #   어깨를 들려다 막히고(가슴이 조금 뜨다 떨어짐) · 팔은 몸 옆에서만 · 다리는 작게 · 고개는 좌우로.
    # 골반 높이(VisualRoot)는 컨트롤러가 침대 높이에 맞춘다 — 이 클립은 VR 을 쓰지 않는다.
    c = no_vr(Clip("isolated_struggle", 3.0, True))
    base = merge({"ULL": (4, 0, -3), "ULR": (4, 0, 3), "LLL": (-6, 0, 0), "LLR": (-6, 0, 0),
                  "FL": (-30, 0, 0), "FR": (-30, 0, 0)}, arms(6, 8, 10))
    keys = [
        (0.0, dict()),
        (0.3, dict(TORSO=(-9, 0, 0), CHEST=(-4, 0, 0), HEAD=(-18, 20, 0), UAL=(10, 0, -12), LAL=(22, 0, 0))),
        (0.48, dict(TORSO=(-3, 0, 2), HEAD=(-6, 28, 0), ULL=(10, 0, -3), LLL=(-16, 0, 0))),
        (0.8, dict(TORSO=(-8, 0, -2), CHEST=(-3, 0, 0), HEAD=(-16, -24, 0), UAR=(12, 0, 12), LAR=(26, 0, 0),
                   ULR=(12, 0, 3), LLR=(-18, 0, 0))),
        (1.0, dict(TORSO=(-2, 0, 0), HEAD=(-4, -30, 0))),
        (1.45, dict(TORSO=(-11, 0, 3), CHEST=(-5, 0, 0), HEAD=(-22, 10, 0), UAL=(14, 0, -14), UAR=(14, 0, 14),
                    LAL=(28, 0, 0), LAR=(28, 0, 0))),
        (1.62, dict(TORSO=(-2, 0, -2), HEAD=(-6, 0, 0), ULL=(12, 0, -3), LLL=(-20, 0, 0))),
        (2.1, dict(TORSO=(-7, 0, 0), HEAD=(-14, 26, 0), ULR=(10, 0, 3), LLR=(-14, 0, 0), UAR=(10, 0, 12))),
        (2.5, dict(TORSO=(-1, 0, 0), HEAD=(-4, -12, 0))),
    ]
    for t, extra in keys:
        q = dict(base)
        for k, v in extra.items():
            q[k] = v if k not in ("UAL", "UAR") else (v[0], 0, v[2])
        c.key(t, q)
    c.rigroot = (90, 0, 0)   # 누운 채로(이 클립만 RigRoot 트랙을 가진다 — lying_idle 과 같은 값)
    out.append(c)
    return out


# ── 박스 ──────────────────────────────────────────────────────────────────
# 박스 34×26×28cm. 손바닥은 박스 옆면 아래쪽을 받친다 — 두 손 간격 = 박스 폭 + 손 두께.
BOX_W, BOX_H, BOX_D = 0.34, 0.26, 0.28
HAND_T = 0.045
BOX_TIMING = {}   # 클립 → (쥐는/놓는 시점, 길이)


def box_clips():
    out = []
    x = BOX_W / 2 + HAND_T / 2
    styles = {
        # 이름: (체형, 박스 높이(가슴·배), 몸에서 박스 중심까지, 뒤로 젖힘, 보폭, 걸음 주기)
        "male": ("M", 1.14, 0.25, 3, 24, 1.0),
        "female": ("F", 0.93, 0.23, 7, 20, 0.95),
        "sheep": ("F", 0.90, 0.215, 10, 14, 1.25),
    }
    pile_y, pile_f = 0.46, 0.44     # 더미 맨 위 상자 중심 높이 · 앞 거리
    cart_y, cart_f = 0.70, 0.42     # 수레 위(둘째 단) 내려놓는 높이

    for style, (body, by, bz, lean, stride, T) in styles.items():
        grip = lambda y, z, dy=0.0: ((-x, y - BOX_H * 0.28 + dy, -z), (x, y - BOX_H * 0.28 + dy, -z))  # noqa: E731
        carry_l, carry_r = grip(by, bz)
        hold = dict(TORSO=(lean, 0, 0), CHEST=(1, 0, 0), HEAD=(-4, 0, 0))
        shrug = 10 if style == "sheep" else (5 if style == "female" else 0)

        # 나르기(걷기) — 팔은 몸 가까이, 박스는 가슴·배에 붙인다.
        c = Clip(f"carry_box_{style}", T, True)
        for i, (t, lg) in enumerate(walk_keys(body, T, stride, drop=0.03 if style == "sheep" else 0.02,
                                              lift=30 if style == "sheep" else 40)):
            q = merge(lg, hold, arms(20, 4, 60, shrug=shrug))
            if style == "sheep" and i == 2:
                # 박스를 다시 끌어올리는 작은 보정.
                q = reach(body, add(q, TORSO=(3, 0, 0)), L=carry_l[:1] + (carry_l[1] + 0.03,) + carry_l[2:],
                          R=carry_r[:1] + (carry_r[1] + 0.03,) + carry_r[2:], name=c.name)
            else:
                q = reach(body, q, L=carry_l, R=carry_r, name=c.name)
            q = add(q, TORSO=(0, 0, (2, 0, -2, 0)[i] * (1.6 if style == "sheep" else 1)))
            c.key(t, q)
        out.append(c)

        # 집기 — 박스 바로 앞에서 무릎·허리를 굽혀 두 손을 박스 옆면에 댄 뒤 들어 올린다.
        if style == "male":
            length, grab = 1.0, 0.5
            seq = [(0.0, stand(body), None, None),
                   (0.35, merge(legs(body, 0.36, 64), dict(TORSO=(-42, 0, 0), HEAD=(10, 0, 0)), arms(40, 4, 20)), (pile_y, pile_f), 0),
                   (0.5, merge(legs(body, 0.37, 66), dict(TORSO=(-41, 0, 0), HEAD=(10, 0, 0))), (pile_y, pile_f), 0),
                   (0.78, merge(legs(body, 0.12, 26), dict(TORSO=(-12, 0, 0), HEAD=(0, 0, 0))), (0.85, 0.33), 0),
                   (1.0, merge(legs(body, 0.02, 2), hold), (by, bz), 0)]
        elif style == "female":
            length, grab = 1.2, 0.6
            seq = [(0.0, stand(body), None, None),
                   (0.4, merge(legs(body, 0.25, 56), dict(TORSO=(-34, 0, 0), HEAD=(8, 0, 0)), arms(40, 4, 20)), (pile_y, pile_f), 0),
                   (0.6, merge(legs(body, 0.26, 58), dict(TORSO=(-34, 0, 0), HEAD=(8, 0, 0))), (pile_y, pile_f), 0),
                   # "낑" — 상체를 한 번 튕겨 올린다.
                   (0.78, merge(legs(body, 0.16, 36), dict(TORSO=(-14, 0, 0), HEAD=(10, 0, 0)), arms(0, 0, 0, shrug=12)), (0.66, 0.33), 0),
                   (0.95, merge(legs(body, 0.06, 12), dict(TORSO=(-2, 0, 0))), (0.82, 0.27), 0),
                   (1.2, merge(legs(body, 0.02, 2), hold), (by, bz), 0)]
        else:  # sheep
            length, grab = 1.75, 0.95
            seq = [(0.0, stand(body), None, None),
                   # 박스 앞에서 발을 한 번 고쳐 딛는다.
                   (0.3, merge(legs(body, 0.03, 8, -6, lift_l=30), dict(TORSO=(-6, 0, 0), HEAD=(-12, 0, 0)), arms(8, 4, 14)), None, None),
                   (0.7, merge(legs(body, 0.3, 66), dict(TORSO=(-24, 0, 0), HEAD=(8, 0, 0)), arms(40, 4, 20)), (pile_y, pile_f - 0.02), 0),
                   (0.95, merge(legs(body, 0.31, 68), dict(TORSO=(-24, 0, 0), HEAD=(8, 0, 0))), (pile_y, pile_f - 0.02), 0),
                   # 들어 올리다 멈칫.
                   (1.2, merge(legs(body, 0.2, 46), dict(TORSO=(-16, 0, 0), HEAD=(4, 0, 0)), arms(0, 0, 0, shrug=10)), (0.6, 0.32), 0),
                   (1.35, merge(legs(body, 0.2, 46), dict(TORSO=(-17, 0, 0), HEAD=(2, 0, 0)), arms(0, 0, 0, shrug=10)), (0.59, 0.32), 0),
                   (1.75, merge(legs(body, 0.03, 4), hold, arms(0, 0, 0, shrug=10)), (by, bz), 0)]
        c = Clip(f"pickup_box_{style}", length, False)
        for t, q, tgt, _ in seq:
            if tgt is not None:
                l, r = grip(tgt[0], tgt[1])
                if t < grab - 0.01:   # 손이 박스에 닿기 직전 — 옆면 바로 바깥
                    l, r = (l[0] - 0.03, l[1] + 0.03, l[2]), (r[0] + 0.03, r[1] + 0.03, r[2])
                q = reach(body, q, L=l, R=r, name=c.name)
            c.key(t, q)
        BOX_TIMING[c.name] = (grab, length)
        out.append(c)

        # 내려놓기 — 멈춰 서서 몸을 낮추고 팔을 앞으로, 수레에 닿는 순간 손을 놓고 다시 선다.
        if style == "male":
            length, release = 0.9, 0.48
            seq = [(0.0, merge(legs(body, 0.02, 2), hold), (by, bz), 0),
                   (0.48, merge(legs(body, 0.24, 46), dict(TORSO=(-30, 0, 0), HEAD=(2, 0, 0))), (cart_y, cart_f), 0),
                   (0.62, merge(legs(body, 0.23, 44), dict(TORSO=(-28, 0, 0))), (cart_y + 0.02, cart_f - 0.02), 0.04),
                   (0.9, stand(body), None, None)]
        elif style == "female":
            length, release = 1.0, 0.55
            seq = [(0.0, merge(legs(body, 0.02, 2), hold), (by, bz), 0),
                   (0.55, merge(legs(body, 0.14, 34), dict(TORSO=(-20, 0, 0), HEAD=(-4, 0, 0))), (cart_y - 0.05, cart_f), 0),
                   (0.7, merge(legs(body, 0.13, 32), dict(TORSO=(-18, 0, 0))), (cart_y - 0.03, cart_f - 0.02), 0.04),
                   (1.0, stand(body), None, None)]
        else:
            length, release = 1.35, 0.72
            seq = [(0.0, merge(legs(body, 0.03, 4), hold, arms(0, 0, 0, shrug=10)), (by, bz), 0),
                   (0.72, merge(legs(body, 0.18, 40), dict(TORSO=(-22, 0, 0), HEAD=(-6, 0, 0))), (cart_y - 0.06, cart_f), 0),
                   (0.86, merge(legs(body, 0.17, 38), dict(TORSO=(-20, 0, 0))), (cart_y - 0.04, cart_f - 0.02), 0.04),
                   # 내려놓고 어깨가 크게 한 번 내려간다.
                   (1.1, merge(legs(body, 0.03, 4), dict(TORSO=(-10, 0, 0), CHEST=(-6, 0, 0), HEAD=(-14, 0, 0)), arms(6, 3, 12, shrug=-8))),
                   (1.35, stand(body), None, None)]
            seq[3] = seq[3] + (None, None)
        c = Clip(f"place_box_{style}", length, False)
        for t, q, tgt, spread in seq:
            if tgt is not None:
                l, r = grip(tgt[0], tgt[1])
                l, r = (l[0] - spread, l[1], l[2]), (r[0] + spread, r[1], r[2])
                q = reach(body, q, L=l, R=r, name=c.name)
            c.key(t, q)
        BOX_TIMING[c.name] = (release, length)
        out.append(c)
    return out


# ── 괴물이 사라진 뒤 ──────────────────────────────────────────────────────

def recovery_clips():
    P = G.GHOST_POSES
    out = []
    F, M = "F", "M"
    rng = __import__("random").Random(9001)

    # 양 ① 아직 바닥에서 떤다 — 고개를 못 들고, 숨이 가라앉지 않는다(흐느낌 없이 가쁜 숨).
    cower = P["sheep_cower"]
    c = Clip("ghost_sheep_floor_loop", 3.0, True)
    c.key(0.0, cower)
    t = 0.07
    breath = 0.0
    while t < 2.95:
        phase = math.sin(t / 0.62 * math.pi * 2)
        q = add(cower, CHEST=(3.5 * phase, 0, 0), SHL=(0, 0, -4 * max(0, phase)), SHR=(0, 0, 4 * max(0, phase)))
        q = add(q, VR=(rng.uniform(-1, 1) * 0.004, 0, 0), HEAD=(rng.uniform(-1, 1), 0, rng.uniform(-1.2, 1.2)))
        c.key(round(t, 3), q)
        t += rng.uniform(0.07, 0.11)
    out.append(c)

    # 양 ② 힘겹게 일어난다 — 손을 바닥에 짚고, 한쪽 무릎부터 세우고, 비틀거리며 서서 귀신이 있던 쪽을 본다.
    c = Clip("ghost_sheep_recover", 2.4, False)
    c.key(0.0, cower)
    hands_down = merge(G.kneel(F, thigh=58), dict(TORSO=(-40, 0, 0), CHEST=(-14, 0, 0), HEAD=(10, 0, 0)), arms(4, 3, 10, shrug=8))
    hands_down = reach(F, hands_down, L=(-0.2, 0.08, -0.42), R=(0.2, 0.08, -0.42), name=c.name)
    c.key(0.45, hands_down)
    one_knee = merge(kneel_one(F, "L"), dict(TORSO=(-30, 0, 0), CHEST=(-10, 0, 0), HEAD=(4, 0, 0)), arms(4, 3, 10, shrug=8))
    knee_pt = fk(F, one_knee)
    one_knee = reach(F, one_knee, L=(-0.08, 0.5, -0.36), R=(0.1, 0.52, -0.38), name=c.name)
    c.key(1.0, one_knee)
    c.key(1.45, reach(F, merge(legs(F, 0.24, 42, 30), dict(TORSO=(-30, 0, 0), CHEST=(-8, 0, 0), HEAD=(0, 0, 0)), arms(4, 3, 10, shrug=8)),
                      L=(-0.12, 0.5, -0.26), R=(0.12, 0.5, -0.26), name=c.name))
    c.key(1.8, merge(legs(F, 0.04, 8, -4), dict(VR=(0.03, -0.04, 0), TORSO=(-12, 0, 4), CHEST=(-6, 0, 0), HEAD=(-4, 30, 0)),
                     arms(14, 6, 50, shrug=14)))
    c.key(2.1, merge(legs(F, 0.02, 4), dict(VR=(-0.015, -0.02, 0), TORSO=(-10, 0, -2), CHEST=(-5, 0, 0), HEAD=(-8, 36, 0)),
                     arms(18, 4, 70, shrug=16)))
    c.key(2.4, merge(legs(F, 0.01, 3), dict(TORSO=(-9, 0, 0), CHEST=(-5, 0, 0), HEAD=(-10, 0, 0)), arms(16, 4, 64, shrug=14)))
    out.append(c)

    # 토끼 — 긴장한 채 잠깐 멈춤 → 숨을 크게 들이쉬고 → 어깨를 떨어뜨리며 내쉼 → 귀신 자리 다시 확인.
    tense = P["rabbit_tense"]
    c = Clip("ghost_rabbit_relief", 1.8, False)
    c.key(0.0, tense).key(0.4, add(tense, HEAD=(2, 0, 0)))
    c.key(0.7, add(tense, CHEST=(7, 0, 0), SHL=(0, 0, -8), SHR=(0, 0, 8), HEAD=(8, 0, 0), VR=(0, 0.01, 0)))
    c.key(1.1, merge(legs(F, 0.02, 4), dict(TORSO=(-12, 0, 0), CHEST=(-8, 0, 0), HEAD=(-22, 0, 0)), arms(8, 5, 18, shrug=-6)))
    c.key(1.45, merge(legs(F, 0.01, 3), dict(TORSO=(-5, 0, 0), CHEST=(-2, 12, 0), HEAD=(-4, 44, 0)), arms(6, 4, 14)))
    c.key(1.8, merge(legs(F, 0.0, 2), dict(TORSO=(-3, 0, 0), HEAD=(-4, 6, 0)), arms(5, 3, 12)))
    out.append(c)

    # 고양이 — 숨은 채 확인 → 몸을 펴고 → 손등으로 이마를 쓸고 → 숨 정리 → 민망한 듯 고개를 한번 돌린다.
    hide = P["cat_hide"]
    c = Clip("ghost_cat_wipe_recover", 2.2, False)
    c.key(0.0, hide).key(0.4, add(hide, HEAD=(8, 0, 0), CHEST=(4, 0, 0)))
    up = merge(legs(F, 0.03, 6), dict(TORSO=(-4, 0, 0), HEAD=(0, 0, 0)), arms(6, 3, 14))
    c.key(0.85, up)
    wipe = merge(legs(F, 0.02, 4), dict(TORSO=(-5, 0, 0), HEAD=(-10, 0, 0)), arms(6, 3, 14))
    brow = lambda xx: tuple(head_point(F, wipe, (xx, 0.11, -0.115)))  # noqa: E731
    c.key(1.1, reach(F, wipe, R=brow(0.07), name=c.name))
    c.key(1.35, reach(F, add(wipe, HEAD=(0, 6, 0)), R=brow(-0.05), name=c.name))
    c.key(1.55, merge(legs(F, 0.02, 4), dict(TORSO=(-3, 0, 0), CHEST=(3, 0, 0), HEAD=(-2, 0, 0)), arms(10, 4, 30, shrug=4)))
    c.key(1.85, merge(legs(F, 0.01, 3), dict(TORSO=(-2, 0, 0), HEAD=(-6, -34, 0)), arms(5, 3, 12)))
    c.key(2.2, merge(legs(F, 0.0, 2), dict(HEAD=(-4, -4, 0)), arms(5, 3, 12)))
    out.append(c)

    # 강아지 — 사라진 것 확인 → 일어나 양손을 가슴에 → 크게 들이쉼 → 길게 내쉼 → 고개를 떨군다.
    hide = P["dog_hide"]
    c = Clip("ghost_dog_breathe_recover", 2.8, False)
    c.key(0.0, hide).key(0.45, add(hide, HEAD=(30, 0, 0), CHEST=(6, 0, 0)))
    rise = merge(legs(M, 0.08, 14), dict(TORSO=(-6, 0, 0), HEAD=(2, 0, 0)), arms(10, 4, 20))
    c.key(0.95, rise)
    base = merge(legs(M, 0.02, 4), dict(TORSO=(-2, 0, 0)), arms(10, 4, 20))
    chest_l = lambda b, dy=0.0: tuple(chest_point(M, b, (-0.07, 0.02 + dy, -0.21)))  # noqa: E731
    chest_r = lambda b, dy=0.0: tuple(chest_point(M, b, (0.08, -0.01 + dy, -0.21)))  # noqa: E731
    c.key(1.25, reach(M, base, L=chest_l(base), R=chest_r(base), name=c.name))
    inhale = add(base, CHEST=(8, 0, 0), SHL=(0, 0, -12), SHR=(0, 0, 12), HEAD=(10, 0, 0), VR=(0, 0.012, 0))
    c.key(1.65, reach(M, inhale, L=chest_l(inhale), R=chest_r(inhale), name=c.name))
    exhale = add(base, TORSO=(-12, 0, 0), CHEST=(-8, 0, 0), SHL=(0, 0, 4), SHR=(0, 0, -4), HEAD=(-24, 0, 0))
    c.key(2.2, reach(M, exhale, L=chest_l(exhale), R=chest_r(exhale), name=c.name))
    c.key(2.45, reach(M, add(exhale, HEAD=(-4, 0, 0)), L=chest_l(exhale, -0.04), R=chest_r(exhale, -0.04), name=c.name))
    c.key(2.8, merge(legs(M, 0.0, 2), dict(TORSO=(-4, 0, 0), HEAD=(-8, 0, 0)), arms(6, 3, 14)))
    out.append(c)

    # 늑대 — 자세를 바로 풀지 않고 응시 → 주먹을 내리고 팔짱 → 고개만 좌우로 → 다시 그 자리 → 팔짱을 푼다.
    guard = P["wolf_guard"]
    c = Clip("ghost_wolf_check_recover", 3.4, False)
    c.key(0.0, guard).key(0.55, add(guard, HEAD=(-2, 0, 0)))
    low = merge(legs(M, 0.03, 6, -4), dict(HIPS=(0, -6, 0), TORSO=(-3, 4, 0), HEAD=(2, 0, 0)), arms(16, 4, 30))
    c.key(0.85, low)
    fold = merge(legs(M, 0.01, 3), dict(TORSO=(1, 0, 0), HEAD=(0, 0, 0)), arms(20, 6, 90))
    cl = tuple(chest_point(M, fold, (0.12, -0.12, -0.22)))
    cr = tuple(chest_point(M, fold, (-0.13, -0.08, -0.25)))
    folded = reach(M, fold, L=cl, R=cr, name=c.name)
    c.key(1.2, folded)
    c.key(1.7, add(folded, HEAD=(0, 38, 0), CHEST=(0, 6, 0)))
    c.key(2.2, add(folded, HEAD=(0, -38, 0), CHEST=(0, -6, 0)))
    c.key(2.6, add(folded, HEAD=(2, 0, 0)))
    c.key(2.95, low)
    c.key(3.4, merge(legs(M, 0.0, 2), dict(TORSO=(-2, 0, 0)), arms(6, 3, 14)))
    out.append(c)

    # 여우 — "엥...? 뭐였냐 저건." 팔을 25~40° 만 벌려 손바닥을 위로, 고개를 천천히 좌우, 어깨를 살짝.
    for seated in (False, True):
        name = "ghost_fox_shrug_recover" + ("_seated" if seated else "")
        lg = seated_legs() if seated else legs(M, 0.0, 2)
        tor = (-10, 0, 0) if seated else (-2, 0, 0)
        c = no_vr(Clip(name, 2.0, False)) if seated else Clip(name, 2.0, False)
        rest = merge(lg, dict(TORSO=tor), arms(6, 3, 14) if not seated else arms(50, 7, 40))
        open_ = merge(lg, dict(TORSO=tor), arms(22, 32, 38, hand=0))
        open_["HNDL"] = (0, 80, 0)
        open_["HNDR"] = (0, -80, 0)
        c.key(0.0, rest)
        c.key(0.4, add(open_, HEAD=(-4, 16, 0)))
        c.key(0.7, add(open_, HEAD=(-4, 0, 0), SHL=(0, 0, -8), SHR=(0, 0, 8)))
        c.key(0.95, add(open_, HEAD=(-4, -16, 0)))
        c.key(1.2, add(open_, HEAD=(-4, 0, 0)))
        c.key(1.55, merge(lg, dict(TORSO=tor, HEAD=(-6, 0, 0)), arms(10, 8, 20)))
        c.key(2.0, rest)
        out.append(c)
    return out


# ── 쓰기 ──────────────────────────────────────────────────────────────────

def emit2(c):
    txt = emit(c)
    if getattr(c, "no_vr", False):
        # VisualRoot 는 컨트롤러가 좌판 높이에 맞춰 움직인다 — 이 클립은 건드리지 않는다.
        txt = _drop_track(txt, PATHS["VR"])
    if getattr(c, "rigroot", None):
        n = len(re.findall(r"tracks/(\d+)/type", txt))
        rad = [math.radians(a) for a in c.rigroot]
        v = "Vector3(%s, %s, %s)" % tuple(G.fmt(a) for a in rad)
        txt = txt.rstrip("\n") + "\n" + "\n".join([
            f'tracks/{n}/type = "value"', f"tracks/{n}/imported = false", f"tracks/{n}/enabled = true",
            f'tracks/{n}/path = NodePath("VisualRoot/RigRoot:rotation")', f"tracks/{n}/interp = 1",
            f"tracks/{n}/loop_wrap = true", f"tracks/{n}/keys = {{",
            '"times": PackedFloat32Array(0),', '"transitions": PackedFloat32Array(1),', '"update": 0,',
            f'"values": [{v}]', "}"]) + "\n"
    return txt


def _drop_track(txt, path):
    blocks = re.split(r"(?=tracks/\d+/type)", txt)
    head, tracks = blocks[0], [b for b in blocks[1:] if f'NodePath("{path}")' not in b]
    out = head
    for i, b in enumerate(tracks):
        out += re.sub(r"tracks/\d+/", f"tracks/{i}/", b)
    return out


# 기존(손으로 만든) 클립의 손 위치도 같은 방식으로 잰다 — 라이브러리에서 키를 읽어 FK 에 넣는다.
_INV = {v: k for k, v in PATHS.items()}


def parse_lib_clip(src, name):
    m = re.search(r'\[sub_resource type="Animation" id="Anim_%s"\]\n(.*?)\n(?=\[)' % re.escape(name), src, re.S)
    if not m:
        return []
    tracks = []
    for tr in re.finditer(r'path = NodePath\("([^"]+)"\).*?"times": PackedFloat32Array\(([^)]*)\).*?"values": \[(.*?)\]\n',
                          m.group(1), re.S):
        key = _INV.get(tr.group(1))
        if key is None:
            continue
        times = [float(t) for t in tr.group(2).split(",") if t.strip()]
        vals = [tuple(float(a) for a in v.split(",")) for v in re.findall(r"Vector3\(([^)]*)\)", tr.group(3))]
        tracks.append((key, times, vals))
    all_t = sorted({t for _, ts, _ in tracks for t in ts})
    poses = []
    for t in all_t:
        p = pose()
        for key, ts, vs in tracks:
            v = vs[-1]
            for i in range(len(ts) - 1):
                if ts[i] <= t <= ts[i + 1]:
                    f = 0 if ts[i + 1] == ts[i] else (t - ts[i]) / (ts[i + 1] - ts[i])
                    v = tuple(a + (b - a) * f for a, b in zip(vs[i], vs[i + 1]))
                    break
            if t < ts[0]:
                v = vs[0]
            p[key] = v if key == "VR" else tuple(math.degrees(a) for a in v)
        poses.append(p)
    return poses


def measure(body, poses):
    fw, hy = [], []
    for p in poses:
        k = fk(body, p)
        l, r = k["palmL"], k["palmR"]
        front = l if -l[2] > -r[2] else r
        fw.append(-front[2])
        hy.append(front[1])
    return (sum(fw) / len(fw), sum(hy) / len(hy)) if fw else (0.0, 0.0)


REACH_CLIPS = ["work", "repair", "hammer_work", "inspect"] + list(WORK_H.keys())


def write_reach(src, clips_by_name):
    rows = []
    for name in REACH_CLIPS:
        vals = {}
        for body, suf in (("M", "_m"), ("F", "_f")):
            c = clips_by_name.get(name + suf)
            poses = [p for _, p in c.keys] if c else parse_lib_clip(src, name)
            vals[body] = measure(body, poses)
        rows.append((name, vals))
        print(f"  reach {name:16s} M fwd {vals['M'][0]:.2f} h {vals['M'][1]:.2f} · F fwd {vals['F'][0]:.2f} h {vals['F'][1]:.2f}")
    lines = [
        "// 자동 생성 — tools/gen_cctv_clips.py 가 쓴다. 손으로 고치지 말 것.",
        "using System.Collections.Generic;",
        "",
        "namespace NSP.View;",
        "",
        "// 작업 클립마다 \"발 사이 중심에서 작업하는 손까지 앞 거리 · 손 높이\"(m). 표현 전용이다.",
        "// RoomWorkVisualController 가 이 값으로 직원을 작업 대상(InteractionTarget)에서 알맞은 거리에 세운다 —",
        "// 손이 허공에 뜨거나 기계 속으로 들어가지 않게.",
        "public static class WorkClipReach",
        "{",
        "    private static readonly Dictionary<string, (float MF, float MH, float FF, float FH)> _reach = new()",
        "    {",
    ]
    for name, v in rows:
        lines.append(f'        ["{name}"] = ({v["M"][0]:.3f}f, {v["M"][1]:.3f}f, {v["F"][0]:.3f}f, {v["F"][1]:.3f}f),')
    lines += [
        "    };",
        "",
        "    // 박스를 손에 쥐는 순간 / 놓는 순간(초) · 클립 길이.",
        "    private static readonly Dictionary<string, (float At, float Length)> _box = new()",
        "    {",
    ]
    for name, (at, length) in BOX_TIMING.items():
        lines.append(f'        ["{name}"] = ({at:.2f}f, {length:.2f}f),')
    lines += [
        "    };",
        "",
        "    // 체형 전용 클립(name_m / name_f)도 기본 이름으로 찾는다.",
        "    public static bool TryGet(string clip, bool female, out float forward, out float height)",
        "    {",
        "        forward = height = 0f;",
        "        if (string.IsNullOrEmpty(clip)) return false;",
        "        string key = clip.EndsWith(\"_m\") || clip.EndsWith(\"_f\") ? clip[..^2] : clip;",
        "        if (!_reach.TryGetValue(key, out var r)) return false;",
        "        (forward, height) = female ? (r.FF, r.FH) : (r.MF, r.MH);",
        "        return true;",
        "    }",
        "",
        "    public static (float At, float Length) Box(string clip) =>",
        "        _box.TryGetValue(clip, out var b) ? b : (0.5f, 1f);",
        "",
        "    // 의자 · 침대 가장자리에 앉고 일어날 때 골반이 내려간 정도(0~1) — chair_sit / chair_stand 와 같은 박자.",
        "    public static float SitCurve(float u) => Curve(u, SitKeys);",
        "    public static float StandCurve(float u) => Curve(u, StandKeys);",
        "    private static readonly float[,] SitKeys = { " + ", ".join("{ %.2ff, %.2ff }" % k for k in SIT_CURVE) + " };",
        "    private static readonly float[,] StandKeys = { " + ", ".join("{ %.2ff, %.2ff }" % k for k in STAND_CURVE) + " };",
        "",
        "    private static float Curve(float u, float[,] k)",
        "    {",
        "        if (u <= k[0, 0]) return k[0, 1];",
        "        for (int i = 0; i < k.GetLength(0) - 1; i++)",
        "            if (u <= k[i + 1, 0])",
        "            {",
        "                float f = (u - k[i, 0]) / (k[i + 1, 0] - k[i, 0]);",
        "                f = f * f * (3f - 2f * f);",
        "                return k[i, 1] + (k[i + 1, 1] - k[i, 1]) * f;",
        "            }",
        "        return k[k.GetLength(0) - 1, 1];",
        "    }",
        "}",
        "",
    ]
    open(REACH_CS, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def main():
    ghost = [c for c in G.build() if c.name not in RETIRED]
    v2 = recovery_clips() + seat_clips() + bed_clips() + box_clips()
    for body in ("M", "F"):
        v2 += work_clips(body)
    # 이름이 겹치면 V2 쪽이 이긴다(예: ghost_sheep_recover 는 V2 가 새로 만든다).
    names_v2 = {c.name for c in v2}
    clips = [c for c in ghost if c.name not in names_v2] + v2

    src = open(LIB, encoding="utf-8").read()
    for n in [c.name for c in clips] + RETIRED:
        src = re.sub(r'\[sub_resource type="Animation" id="Anim_%s"\]\n.*?\n(?=\[)' % re.escape(n), "", src, flags=re.S)
        src = re.sub(r'&"%s": SubResource\("Anim_%s"\),?\n' % (re.escape(n), re.escape(n)), "", src)

    head, tail = src.split("[resource]", 1)
    head = head.rstrip("\n") + "\n\n" + "\n".join(emit2(c) for c in clips) + "\n"
    m = re.search(r"_data = \{\n(.*?)\n\}", tail, re.S)
    entries = [e.strip().rstrip(",") for e in m.group(1).split("\n") if e.strip()]
    entries += [f'&"{c.name}": SubResource("Anim_{c.name}")' for c in clips]
    entries.sort()
    tail = tail[:m.start()] + "_data = {\n" + ",\n".join(entries) + "\n}" + tail[m.end():]
    open(LIB, "w", encoding="utf-8", newline="\n").write(head + "[resource]" + tail)

    write_reach(src, {c.name: c for c in clips})
    print(f"{len(clips)} clips → {os.path.relpath(LIB, ROOT)}  ·  {os.path.relpath(REACH_CS, ROOT)}")


if __name__ == "__main__":
    main()
