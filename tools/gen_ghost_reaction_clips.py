# -*- coding: utf-8 -*-
"""
괴물(Ghost) 반응 클립 생성기 — employee_common.tres 에 직원별 반응 클립을 넣는다.

    python tools/gen_ghost_reaction_clips.py

여러 번 돌려도 된다(같은 이름의 클립은 지우고 다시 넣는다). 다른 클립은 건드리지 않는다.

이 반응들은 **캐릭터 한 명 전용**이다. 양·토끼·고양이는 여성 베이스(EmployeeCctvBase_F),
강아지·늑대·여우는 남성 베이스(EmployeeCctvBase_M)를 쓰므로, 무릎을 접어 주저앉거나 쪼그려 앉는
자세의 골반 높이(VisualRoot Y)를 그 체형의 다리 길이로 계산해 발이 바닥에 붙게 한다.

관절 규칙(캐릭터는 자기 -Z 를 본다, +X 가 오른쪽):
  · 팔/다리 x+  = 앞으로 든다(굴곡)        · 아래팔 x+ = 팔꿈치 굽힘      · 아래다리 x- = 무릎 굽힘
  · 몸통/가슴 x- = 앞으로 숙임              · 머리 x+ = 고개 젖힘(위를 봄)  · 머리 y+ = 왼쪽을 봄
  · 발 x+ = 발끝 들기                        · 벌리기(out) + = 바깥쪽(좌우 부호는 여기서 맞춘다)
Neck 은 어떤 클립도 쓰지 않는다 — 시선(귀신 쪽 보기)은 코드가 Neck 으로 따로 돌린다.
"""
import math
import os
import random
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIB = os.path.join(ROOT, "scenes", "cctv_characters", "anim", "employee_common.tres")

# 체형: 골반 높이 · 넓적다리 · 정강이 (EmployeeCctvBase_F / _M 의 실제 노드 값)
BODY = {
    "F": dict(hip=0.825, l1=0.40, l2=0.345),
    "M": dict(hip=1.00, l1=0.50, l2=0.42),
}

H = "VisualRoot/RigRoot/Hips"
C = H + "/Torso/Chest"
PATHS = {
    "VR": "VisualRoot:position",
    "HIPS": H + ":rotation",
    "TORSO": H + "/Torso:rotation",
    "CHEST": C + ":rotation",
    "HEAD": C + "/Neck/Head:rotation",
    "SHL": C + "/ShoulderL:rotation",
    "SHR": C + "/ShoulderR:rotation",
    "UAL": C + "/ShoulderL/UpperArmL:rotation",
    "UAR": C + "/ShoulderR/UpperArmR:rotation",
    "LAL": C + "/ShoulderL/UpperArmL/LowerArmL:rotation",
    "LAR": C + "/ShoulderR/UpperArmR/LowerArmR:rotation",
    "HNDL": C + "/ShoulderL/UpperArmL/LowerArmL/HandL:rotation",
    "HNDR": C + "/ShoulderR/UpperArmR/LowerArmR/HandR:rotation",
    "ULL": H + "/UpperLegL:rotation",
    "ULR": H + "/UpperLegR:rotation",
    "LLL": H + "/UpperLegL/LowerLegL:rotation",
    "LLR": H + "/UpperLegR/LowerLegR:rotation",
    "FL": H + "/UpperLegL/LowerLegL/FootL:rotation",
    "FR": H + "/UpperLegR/LowerLegR/FootR:rotation",
}
ORDER = list(PATHS.keys())
ANKLE_REST = 0.06   # 서 있을 때 발목 높이(두 체형 공통)


# ── 자세 도우미 ───────────────────────────────────────────────────────────
# 자세 = {관절: (x, y, z)}. 회전은 도(°), VR 은 미터.

def pose(**kw):
    p = {k: (0.0, 0.0, 0.0) for k in ORDER}
    for k, v in kw.items():
        p[k] = tuple(float(a) for a in v)
    return p


def merge(*parts):
    out = pose()
    for part in parts:
        for k, v in part.items():
            out[k] = v
    return out


def add(p, **kw):
    """자세 p 에 관절 값을 더한다(떨림·들썩임 같은 작은 변화)."""
    q = dict(p)
    for k, v in kw.items():
        a = q[k]
        q[k] = (a[0] + v[0], a[1] + v[1], a[2] + v[2])
    return q


def arms(ua_x, out, la, shrug=0.0, hand=0.0, ua_x_r=None, out_r=None, la_r=None, hand_r=None,
         ua_y=0.0, ua_y_r=None):
    """양팔. out + = 바깥으로 벌림, shrug + = 어깨 으쓱."""
    ua_x_r = ua_x if ua_x_r is None else ua_x_r
    out_r = out if out_r is None else out_r
    la_r = la if la_r is None else la_r
    hand_r = hand if hand_r is None else hand_r
    ua_y_r = -ua_y if ua_y_r is None else ua_y_r
    return {
        "SHL": (0, 0, -shrug), "SHR": (0, 0, shrug),
        "UAL": (ua_x, ua_y, -out), "UAR": (ua_x_r, ua_y_r, out_r),
        "LAL": (la, 0, 0), "LAR": (la_r, 0, 0),
        "HNDL": (hand, 0, 0), "HNDR": (hand_r, 0, 0),
    }


def _solve_leg(body, drop, thigh, out=0.0):
    """골반을 drop 만큼 내렸을 때 발이 바닥에 닿는 무릎 각도(도)와 발 각도."""
    b = BODY[body]
    need = (b["l1"] + b["l2"] - drop) / max(0.2, math.cos(math.radians(out)))
    a = math.radians(thigh)
    c = (need - b["l1"] * math.cos(a)) / b["l2"]
    if c > 1.0:
        c = 1.0   # 다리가 모자란다 — 발이 조금 뜬다(점프 중 같은 자세)
    c = max(-1.0, c)
    s = -math.acos(c)          # 정강이는 넓적다리보다 뒤로 접힌다(무릎은 앞으로)
    if s > a:
        s = a
    knee = math.degrees(s - a)
    return knee, -math.degrees(s)


def legs(body, drop, thigh_l, thigh_r=None, out_l=0.0, out_r=None, lift_l=0.0, lift_r=0.0,
         foot_l=0.0, foot_r=0.0):
    """두 발을 바닥에 붙인 채 골반을 drop 만큼 낮춘다. lift = 들어 올린 발(무릎 추가 굽힘)."""
    thigh_r = thigh_l if thigh_r is None else thigh_r
    out_r = out_l if out_r is None else out_r
    kl, fl = _solve_leg(body, drop, thigh_l, out_l)
    kr, fr = _solve_leg(body, drop, thigh_r, out_r)
    return {
        "VR": (0.0, -drop, 0.0),
        "ULL": (thigh_l, 0, -out_l), "ULR": (thigh_r, 0, out_r),
        "LLL": (kl - lift_l, 0, 0), "LLR": (kr - lift_r, 0, 0),
        "FL": (fl + lift_l * 0.5 + foot_l, 0, 0), "FR": (fr + lift_r * 0.5 + foot_r, 0, 0),
    }


def kneel(body, thigh=55.0, knee_h=0.075, shin=-86.0):
    """정좌하듯 무릎을 바닥에 대고 엉덩이를 발뒤꿈치 쪽으로 내린다(양 전용)."""
    b = BODY[body]
    a = math.radians(thigh)
    hip_joint = knee_h + b["l1"] * math.cos(a)
    drop = (b["hip"] - 0.02) - hip_joint
    knee = shin - thigh
    foot = -180.0 - shin + 8.0       # 발등이 바닥에 눕는다(발끝은 뒤)
    return {
        "VR": (0.0, -drop, 0.0),
        "ULL": (thigh, 0, -4), "ULR": (thigh, 0, 4),
        "LLL": (knee, 0, 0), "LLR": (knee, 0, 0),
        "FL": (foot, 0, 0), "FR": (foot, 0, 0),
    }


def with_vr(p, x=None, y=None, z=None):
    q = dict(p)
    vx, vy, vz = q["VR"]
    q["VR"] = (vx if x is None else x, vy if y is None else y, vz if z is None else z)
    return q


# ── 클립 ──────────────────────────────────────────────────────────────────

class Clip:
    def __init__(self, name, length, loop):
        self.name, self.length, self.loop = name, length, loop
        self.keys = []   # (time, pose)

    def key(self, t, p):
        self.keys.append((round(t, 4), p))
        return self


def tremble(rng, p, t_from, t_to, step, amp_vr=0.006, amp_rot=1.4, joints=("CHEST", "HEAD")):
    """불규칙한 잔떨림 — 같은 간격으로 흔들면 기계처럼 보인다."""
    out = []
    t = t_from
    sign = 1
    while t < t_to - 1e-4:
        q = add(p, VR=(sign * amp_vr * rng.uniform(0.6, 1.0), 0, rng.uniform(-0.3, 0.3) * amp_vr))
        for j in joints:
            q = add(q, **{j: (rng.uniform(-0.4, 0.4) * amp_rot, rng.uniform(-0.5, 0.5) * amp_rot,
                              sign * amp_rot * rng.uniform(0.5, 1.0))})
        out.append((t, q))
        sign = -sign
        t += step * rng.uniform(0.75, 1.3)
    return out


def build():
    rng = random.Random(4127)
    clips = []

    # ═══ 양 — 얼어붙었다가 다리가 풀려 주저앉고, 머리를 감싸 웅크린 채 운다 ═════════
    F = "F"
    stand_f = merge(legs(F, 0.0, 0))
    sheep_flinch = merge(legs(F, 0.012, 5), dict(
        VR=(0, -0.012, 0.03),
        TORSO=(9, 0, 0), CHEST=(6, 0, 0), HEAD=(16, 0, 0)),
        arms(32, -7, 100, shrug=16, hand=-15))
    sheep_buckle = merge(legs(F, 0.22, 28), dict(
        TORSO=(-6, 0, 0), CHEST=(-2, 0, 0), HEAD=(4, 0, 0)),
        arms(48, -4, 112, shrug=18, hand=-15))
    sheep_fall = merge(kneel(F, thigh=50), dict(
        TORSO=(-28, 0, 0), CHEST=(-14, 0, 0), HEAD=(-4, 0, 0)),
        arms(118, 4, 115, shrug=20, hand=-10))
    # 머리를 감싸 쥔다 — 팔은 얼굴 앞으로 올라가 팔꿈치를 모으고, 손은 정수리·뒤통수.
    sheep_cower = merge(kneel(F, thigh=62), dict(
        TORSO=(-34, 0, 0), CHEST=(-20, 0, 0), HEAD=(-26, 0, 0)),
        arms(128, 16, 122, shrug=24, hand=-20))

    c = Clip("ghost_sheep_drop", 0.9, False)
    c.key(0.0, stand_f).key(0.14, sheep_flinch)
    c.key(0.2, add(sheep_flinch, VR=(0.006, 0, 0), CHEST=(0, 0, 2)))
    c.key(0.27, add(sheep_flinch, VR=(-0.006, 0, 0), CHEST=(0, 0, -2)))
    c.key(0.48, sheep_buckle).key(0.68, sheep_fall).key(0.9, sheep_cower)
    clips.append(c)

    c = Clip("ghost_sheep_cower_loop", 2.4, True)
    c.key(0.0, sheep_cower)
    for t, q in tremble(rng, sheep_cower, 0.07, 0.5, 0.075, amp_vr=0.006, amp_rot=1.6):
        c.key(t, q)
    sob = add(sheep_cower, CHEST=(10, 0, 0), HEAD=(5, 0, 0), SHL=(0, 0, -10), SHR=(0, 0, 10),
              VR=(0, 0.012, 0))
    c.key(0.58, sob).key(0.7, add(sheep_cower, CHEST=(3, 0, 0)))
    c.key(0.8, add(sob, CHEST=(-3, 0, 0)))
    for t, q in tremble(rng, sheep_cower, 1.02, 1.6, 0.07, amp_vr=0.006, amp_rot=1.6):
        c.key(t, q)
    c.key(1.68, add(sob, CHEST=(3, 0, 0), HEAD=(3, 0, 0)))
    c.key(1.84, add(sheep_cower, CHEST=(2, 0, 0)))
    for t, q in tremble(rng, sheep_cower, 1.95, 2.36, 0.075, amp_vr=0.006, amp_rot=1.6):
        c.key(t, q)
    clips.append(c)

    c = Clip("ghost_sheep_recover", 1.1, False)
    sheep_lift = merge(kneel(F, thigh=52), dict(TORSO=(-30, 0, 0), CHEST=(-12, 0, 0), HEAD=(18, 0, 0)),
                       arms(40, 6, 40, shrug=10))
    sheep_push = merge(legs(F, 0.3, 42, 30), dict(TORSO=(-30, 0, 0), CHEST=(-8, 0, 0), HEAD=(8, 0, 0)),
                       arms(30, 8, 25, shrug=6))
    sheep_up = merge(legs(F, 0.01, 3), dict(TORSO=(-7, 0, 0), CHEST=(-4, 0, 0), HEAD=(-10, 0, 0)),
                     arms(8, 4, 18, shrug=4))
    c.key(0.0, sheep_cower).key(0.3, sheep_lift).key(0.7, sheep_push).key(1.1, sheep_up)
    clips.append(c)

    # ═══ 토끼 — 온몸으로 튀어 오르며 팔을 벌리고, 머리를 싸쥐고 비명, 긴장 유지 ═══════
    rabbit_jump = merge(legs(F, -0.07, 10, -8), dict(
        VR=(0, 0.07, 0.03),
        TORSO=(16, 0, 0), CHEST=(10, 0, 0), HEAD=(22, 26, 0)),
        arms(22, 118, 22, shrug=22, hand=10))
    rabbit_peak = merge(legs(F, -0.095, 14, -6), dict(
        VR=(0, 0.095, 0.05),
        TORSO=(19, 0, 0), CHEST=(12, 0, 0), HEAD=(26, 32, 0)),
        arms(18, 136, 18, shrug=26, hand=14))
    rabbit_land = merge(legs(F, 0.07, 20, 14), dict(
        TORSO=(12, 0, 0), CHEST=(8, 0, 0), HEAD=(18, 22, 0)),
        arms(30, 124, 34, shrug=22))
    rabbit_scream = merge(legs(F, 0.04, 10, -12), dict(
        TORSO=(15, 0, 0), CHEST=(10, 0, 0), HEAD=(20, -8, 0)),
        arms(62, 58, 128, shrug=24, hand=-10))
    rabbit_turn = add(rabbit_scream, HEAD=(-6, 50, 0), CHEST=(0, 12, 0), TORSO=(0, 0, -4))
    rabbit_flail = merge(rabbit_turn, arms(62, 58, 128, shrug=24, hand=-10,
                                         ua_x_r=40, out_r=104, la_r=26))
    rabbit_flail = add(rabbit_flail, HEAD=(0, -35, 0))
    rabbit_tense = merge(legs(F, 0.03, 7), dict(
        TORSO=(7, 0, 0), CHEST=(6, 0, 0), HEAD=(8, 0, 0)),
        arms(60, 30, 112, shrug=16, hand=-8))

    c = Clip("ghost_rabbit_shock", 0.95, False)
    c.key(0.0, stand_f).key(0.07, rabbit_jump).key(0.15, rabbit_peak).key(0.27, rabbit_land)
    c.key(0.42, rabbit_scream)
    # 한두 걸음 뒤로 — 오른발, 왼발(위치 이동 자체는 코드가 한다).
    c.key(0.5, merge(rabbit_scream, legs(F, 0.03, 6, -16, lift_r=35)))
    c.key(0.6, rabbit_turn)
    c.key(0.7, merge(rabbit_flail, legs(F, 0.03, 14, -8, lift_l=35)))
    c.key(0.8, merge(rabbit_flail, legs(F, 0.035, 8, -6)))
    c.key(0.95, rabbit_tense)
    clips.append(c)

    c = Clip("ghost_rabbit_scream_loop", 3.2, True)
    breath_in = add(rabbit_tense, CHEST=(5, 0, 0), SHL=(0, 0, -6), SHR=(0, 0, 6), HEAD=(3, 0, 0))
    c.key(0.0, rabbit_tense).key(0.42, breath_in).key(0.85, add(rabbit_tense, HEAD=(0, 9, 0)))
    c.key(1.25, add(breath_in, HEAD=(0, -8, 0))).key(1.7, rabbit_tense)
    # 다시 움찔 — "아직 저기 있잖아!"
    jolt = add(merge(rabbit_tense, arms(52, 58, 96, shrug=24, hand=6)),
               VR=(0, 0.028, 0.02), TORSO=(8, 0, 0), HEAD=(10, 14, 0))
    c.key(1.86, rabbit_tense).key(1.95, jolt).key(2.12, add(jolt, HEAD=(0, -18, 0)))
    c.key(2.45, breath_in).key(2.85, add(rabbit_tense, HEAD=(0, 5, 0)))
    clips.append(c)

    c = Clip("ghost_rabbit_recover", 0.6, False)
    rabbit_look_back = merge(legs(F, 0.015, 4), dict(
        TORSO=(3, 0, 0), CHEST=(2, 14, 0), HEAD=(4, 58, 0)),
        arms(22, 14, 45, shrug=8))
    c.key(0.0, rabbit_tense).key(0.3, rabbit_look_back)
    c.key(0.6, merge(stand_f, arms(4, 3, 12), dict(HEAD=(0, 8, 0))))
    clips.append(c)

    # ═══ 고양이 — "헉" 한 번, 곧바로 낮게 몸을 숙여 빠르게 빠진다, 구석에서 웅크려 살핀다 ══
    cat_flinch = merge(legs(F, 0.02, 6, -14), dict(
        TORSO=(8, 0, 0), CHEST=(4, 0, 0), HEAD=(9, 0, 0)),
        arms(30, -4, 82, shrug=13))
    cat_ready = merge(legs(F, 0.16, 32, 16), dict(
        TORSO=(-16, 0, 0), CHEST=(-6, 0, 0), HEAD=(15, 0, 0)),
        arms(22, -2, 72, shrug=8))
    c = Clip("ghost_cat_startle", 0.28, False)
    c.key(0.0, stand_f).key(0.1, cat_flinch).key(0.28, cat_ready)
    clips.append(c)

    # 낮고 짧은 보폭 — 팔은 몸에 붙인 채, 머리는 흔들리지 않는다.
    c = Clip("ghost_cat_retreat", 0.44, True)
    run_base = dict(TORSO=(-20, 0, 0), CHEST=(-6, 0, 0), HEAD=(16, 0, 0))
    c.key(0.0, merge(legs(F, 0.17, 30, -16), run_base, arms(12, -2, 78, shrug=8, ua_x_r=28, la_r=70)))
    c.key(0.11, merge(legs(F, 0.14, 8, 8, lift_r=48), run_base, arms(20, -2, 74, shrug=8)))
    c.key(0.22, merge(legs(F, 0.17, -16, 30), run_base, arms(28, -2, 70, shrug=8, ua_x_r=12, la_r=78)))
    c.key(0.33, merge(legs(F, 0.14, 8, 8, lift_l=48), run_base, arms(20, -2, 74, shrug=8)))
    clips.append(c)

    cat_hide = merge(legs(F, 0.46, 72), dict(
        TORSO=(-28, 0, 0), CHEST=(-12, 0, 0), HEAD=(24, 0, 0)),
        arms(58, -10, 48, shrug=10, hand=10))
    cat_peek = merge(legs(F, 0.39, 64), dict(
        TORSO=(-18, 0, 0), CHEST=(-2, 0, 0), HEAD=(30, 0, 0)),
        arms(56, -10, 50, shrug=12, hand=10))
    c = Clip("ghost_cat_hide_loop", 3.0, True)
    c.key(0.0, cat_hide).key(0.5, add(cat_hide, CHEST=(2, 0, 0)))
    c.key(0.9, cat_hide).key(1.08, cat_peek).key(1.55, add(cat_peek, HEAD=(-2, 0, 0)))
    c.key(1.72, cat_hide).key(2.3, add(cat_hide, CHEST=(2, 0, 0)))
    c.key(2.55, add(cat_hide, HEAD=(4, 0, 0), CHEST=(3, 0, 0)))
    clips.append(c)

    c = Clip("ghost_cat_recover", 0.7, False)
    cat_rise = merge(legs(F, 0.16, 26), dict(TORSO=(-10, 0, 0), CHEST=(-2, 0, 0), HEAD=(10, 0, 0)),
                     arms(20, -2, 55, shrug=6))
    c.key(0.0, cat_hide).key(0.35, cat_rise)
    c.key(0.7, merge(stand_f, arms(6, 2, 18), dict(TORSO=(-3, 0, 0))))
    clips.append(c)

    # ═══ 강아지 — 크게 놀라 허둥대다 도망, 구석에 웅크려 떨며 계속 확인 ═════════════
    M = "M"
    stand_m = merge(legs(M, 0.0, 0))
    dog_jolt = merge(legs(M, 0.05, 12, -26), dict(
        VR=(0, -0.03, 0.07),
        TORSO=(22, 0, 0), CHEST=(12, 0, 0), HEAD=(18, 0, 0)),
        arms(98, 56, 56, shrug=24, hand=10))
    dog_away = add(merge(dog_jolt, arms(112, 50, 70, shrug=26)), HEAD=(0, 52, 0), CHEST=(0, 12, 0))
    dog_back = merge(legs(M, 0.06, 6, -18), dict(
        VR=(0, -0.06, 0.07),
        TORSO=(18, 0, 5), CHEST=(10, 0, 0), HEAD=(12, -8, 0)),
        arms(100, 46, 62, shrug=24))
    dog_turn = merge(legs(M, 0.07, 14, -6), dict(
        TORSO=(-12, 0, -4), CHEST=(-2, 0, 0), HEAD=(8, 0, 0)),
        arms(52, 30, 70, shrug=14))
    c = Clip("ghost_dog_startle", 0.46, False)
    c.key(0.0, stand_m).key(0.1, dog_jolt).key(0.22, dog_away).key(0.34, dog_back).key(0.46, dog_turn)
    clips.append(c)

    # 허둥대며 뛴다 — 큰 보폭, 팔을 휘젓고 몸이 좌우로 흔들린다.
    c = Clip("ghost_dog_flee", 0.4, True)
    c.key(0.0, merge(legs(M, 0.07, 44, -26), dict(TORSO=(-14, 0, 6), CHEST=(-2, 8, 0), HEAD=(6, 0, 0)),
                     arms(-28, 34, 42, shrug=14, ua_x_r=62, la_r=86)))
    c.key(0.1, merge(legs(M, 0.0, 10, 10, lift_r=70), dict(VR=(0, 0.015, 0), TORSO=(-12, 0, 0),
                                                         CHEST=(-2, 0, 0), HEAD=(12, 0, 0)),
                     arms(20, 42, 60, shrug=16)))
    c.key(0.2, merge(legs(M, 0.07, -26, 44), dict(TORSO=(-14, 0, -6), CHEST=(-2, -8, 0), HEAD=(6, 0, 0)),
                     arms(62, 34, 86, shrug=14, ua_x_r=-28, la_r=42)))
    c.key(0.3, merge(legs(M, 0.0, 10, 10, lift_l=70), dict(VR=(0, 0.015, 0), TORSO=(-12, 0, 0),
                                                         CHEST=(-2, 0, 0), HEAD=(12, 0, 0)),
                     arms(20, 42, 60, shrug=16)))
    clips.append(c)

    dog_hide = merge(legs(M, 0.5, 64), dict(
        TORSO=(-8, 0, 0), CHEST=(-16, 0, 0), HEAD=(-18, 0, 0)),
        arms(62, -24, 118, shrug=18, hand=-10))
    dog_look = add(dog_hide, HEAD=(30, 0, 0), CHEST=(6, 0, 0), VR=(0, 0.03, 0))
    c = Clip("ghost_dog_hide_loop", 2.6, True)
    c.key(0.0, dog_hide)
    for t, q in tremble(rng, dog_hide, 0.07, 0.78, 0.07, amp_vr=0.004, amp_rot=1.1):
        c.key(t, q)
    c.key(0.9, dog_look)
    for t, q in tremble(rng, dog_look, 0.97, 1.45, 0.07, amp_vr=0.004, amp_rot=1.1):
        c.key(t, q)
    c.key(1.6, dog_hide)
    for t, q in tremble(rng, dog_hide, 1.68, 2.1, 0.07, amp_vr=0.004, amp_rot=1.1):
        c.key(t, q)
    c.key(2.22, add(dog_hide, HEAD=(16, 0, 0)))
    for t, q in tremble(rng, dog_hide, 2.35, 2.55, 0.07, amp_vr=0.004, amp_rot=1.1):
        c.key(t, q)
    clips.append(c)

    c = Clip("ghost_dog_recover", 1.0, False)
    dog_rise = merge(legs(M, 0.24, 34), dict(TORSO=(-10, 0, 0), CHEST=(-6, 12, 0), HEAD=(6, 50, 0)),
                     arms(40, -8, 80, shrug=12))
    dog_rise2 = merge(legs(M, 0.1, 14), dict(TORSO=(-6, 0, 0), CHEST=(-4, -12, 0), HEAD=(4, -50, 0)),
                      arms(26, -4, 60, shrug=10))
    c.key(0.0, dog_hide).key(0.42, dog_rise).key(0.72, dog_rise2)
    c.key(1.0, merge(stand_m, arms(8, 3, 22), dict(TORSO=(-4, 0, 0), HEAD=(-4, 0, 0))))
    clips.append(c)

    # ═══ 늑대 — 아주 짧게 움찔, 곧바로 다리를 벌리고 주먹을 올려 정면으로 맞선다 ═══════
    wolf_flinch = merge(stand_m, dict(TORSO=(8, 0, 0), CHEST=(3, 0, 0), HEAD=(4, 0, 0)),
                        arms(12, 4, 20, shrug=10))
    wolf_set = merge(legs(M, 0.03, 8, -4), dict(TORSO=(-4, 0, 0), HEAD=(6, 0, 0)),
                     arms(30, -4, 60, shrug=8))
    # 왼발 앞 · 오른발 뒤 · 골반은 왼쪽을 앞으로 비튼다. 주먹은 턱·가슴 앞.
    wolf_guard = merge(legs(M, 0.11, 24, -20, out_l=9, out_r=9), dict(
        HIPS=(0, -16, 0),
        TORSO=(-15, 12, 0), CHEST=(-6, 4, 0), HEAD=(17, 0, 0)),
        arms(66, -10, 104, shrug=9, hand=-12, ua_x_r=46, out_r=-14, la_r=124))
    c = Clip("ghost_wolf_startle", 0.46, False)
    c.key(0.0, stand_m).key(0.08, wolf_flinch).key(0.18, wolf_set)
    c.key(0.3, merge(wolf_guard, legs(M, 0.08, 18, -10, out_l=6, out_r=6, lift_l=20), dict(HIPS=(0, -10, 0))))
    c.key(0.46, wolf_guard)
    clips.append(c)

    c = Clip("ghost_wolf_guard_loop", 2.4, True)
    c.key(0.0, wolf_guard)
    c.key(0.4, add(wolf_guard, LAL=(6, 0, 0), LAR=(-4, 0, 0), CHEST=(2, 0, 0)))
    c.key(0.6, add(with_vr(wolf_guard, x=0.028), HIPS=(0, 0, 2)))
    c.key(0.85, add(with_vr(wolf_guard, x=0.02), LAL=(-4, 0, 0)))
    c.key(1.2, add(wolf_guard, CHEST=(-2, 0, 0)))
    # 가끔 앞 어깨를 한 번 더 내민다.
    c.key(1.38, add(wolf_guard, TORSO=(-4, -12, 0), UAL=(10, 0, 0), LAL=(-8, 0, 0)))
    c.key(1.62, add(wolf_guard, TORSO=(-3, -10, 0), UAL=(8, 0, 0)))
    c.key(1.8, add(with_vr(wolf_guard, x=-0.028), HIPS=(0, 0, -2)))
    c.key(2.1, add(with_vr(wolf_guard, x=-0.015), LAR=(5, 0, 0)))
    clips.append(c)

    c = Clip("ghost_wolf_recover", 0.8, False)
    c.key(0.0, wolf_guard).key(0.36, add(wolf_guard, HEAD=(-2, 0, 0)))
    c.key(0.62, merge(legs(M, 0.04, 10, -6), dict(HIPS=(0, -6, 0), TORSO=(-5, 4, 0), HEAD=(4, 0, 0)),
                      arms(22, 2, 42, shrug=4)))
    c.key(0.8, merge(stand_m, arms(6, 3, 16), dict(TORSO=(-2, 0, 0))))
    clips.append(c)

    return clips


# ── .tres 쓰기 ────────────────────────────────────────────────────────────

def fmt(v):
    s = f"{v:.4f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def emit(clip):
    keys = sorted(clip.keys, key=lambda k: k[0])
    if clip.loop:
        # 반복 클립은 끝 = 처음이어야 이음매가 튀지 않는다.
        keys = [k for k in keys if k[0] < clip.length - 1e-4] + [(clip.length, keys[0][1])]
    times = [k[0] for k in keys]
    lines = [f'[sub_resource type="Animation" id="Anim_{clip.name}"]',
             f'resource_name = "{clip.name}"',
             f"length = {fmt(clip.length)}"]
    if clip.loop:
        lines.append("loop_mode = 1")
    for i, joint in enumerate(ORDER):
        vals = []
        for _, p in keys:
            x, y, z = p[joint]
            if joint != "VR":
                x, y, z = (math.radians(a) for a in (x, y, z))
            vals.append(f"Vector3({fmt(x)}, {fmt(y)}, {fmt(z)})")
        lines += [
            f'tracks/{i}/type = "value"',
            f"tracks/{i}/imported = false",
            f"tracks/{i}/enabled = true",
            f'tracks/{i}/path = NodePath("{PATHS[joint]}")',
            f"tracks/{i}/interp = 2",
            f"tracks/{i}/loop_wrap = true",
            f"tracks/{i}/keys = {{",
            '"times": PackedFloat32Array(' + ", ".join(fmt(t) for t in times) + "),",
            '"transitions": PackedFloat32Array(' + ", ".join("1" for _ in times) + "),",
            '"update": 0,',
            '"values": [' + ", ".join(vals) + "]",
            "}",
        ]
    return "\n".join(lines) + "\n"


def main():
    clips = build()
    src = open(LIB, encoding="utf-8").read()
    names = [c.name for c in clips]

    # 같은 이름의 예전 클립을 지운다(여러 번 돌려도 한 벌만 남게).
    for n in names:
        src = re.sub(r'\[sub_resource type="Animation" id="Anim_%s"\]\n.*?\n(?=\[)' % re.escape(n), "",
                     src, flags=re.S)
        src = re.sub(r'&"%s": SubResource\("Anim_%s"\),?\n' % (re.escape(n), re.escape(n)), "", src)

    head, tail = src.split("[resource]", 1)
    head = head.rstrip("\n") + "\n\n" + "\n".join(emit(c) for c in clips) + "\n"

    m = re.search(r"_data = \{\n(.*?)\n\}", tail, re.S)
    entries = [e.strip().rstrip(",") for e in m.group(1).split("\n") if e.strip()]
    entries += [f'&"{n}": SubResource("Anim_{n}")' for n in names]
    entries.sort()
    tail = tail[:m.start()] + "_data = {\n" + ",\n".join(entries) + "\n}" + tail[m.end():]

    open(LIB, "w", encoding="utf-8", newline="\n").write(head + "[resource]" + tail)
    print(f"{len(clips)} clips → {os.path.relpath(LIB, ROOT)}")
    for c in clips:
        print(f"  {c.name:28s} {c.length:4.2f}s {'loop' if c.loop else 'once'}  keys={len(c.keys)}")


if __name__ == "__main__":
    main()
