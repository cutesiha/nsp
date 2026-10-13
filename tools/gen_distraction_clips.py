# -*- coding: utf-8 -*-
"""
직원 "딴짓" 애니메이션 생성기 — employee_common.tres 에 12종 × (진입·반복·적발·복귀) 클립을 넣는다.

    python tools/gen_distraction_clips.py

여러 번 돌려도 된다(같은 이름의 클립은 지우고 다시 넣는다). 다른 클립은 건드리지 않는다.
gen_cctv_clips.py / gen_ghost_reaction_clips.py 와 서로 독립이다 — 자세 도우미만 모듈로 빌려 쓴다.

딴짓은 **캐릭터 한 명 전용**이다. 양·토끼·고양이는 여성 베이스(EmployeeCctvBase_F),
강아지·늑대·여우는 남성 베이스(EmployeeCctvBase_M)라 체형이 하나로 정해진다 — _m/_f 짝이 없다.

관절 규칙(캐릭터는 자기 -Z 를 본다, +X 가 오른쪽):
  · 팔/다리 x+  = 앞으로 든다(굴곡)        · 아래팔 x+ = 팔꿈치 굽힘      · 아래다리 x- = 무릎 굽힘
  · 몸통/가슴 x- = 앞으로 숙임              · 머리 x+ = 고개 젖힘(위를 봄)  · 머리 y+ = 왼쪽을 봄
  · 발 x+ = 발끝 들기                        · 벌리기(out) + = 바깥쪽
Neck 은 어떤 클립도 쓰지 않는다 — "CCTV 를 쳐다보는" 고개 돌림은 코드가 Neck 으로 따로 돌린다
(RoomWorkVisualController.Distraction.cs). 그래서 적발 클립에는 HEAD 의 y(좌우)를 거의 쓰지 않고,
위를 올려다보는 x(피치)와 몸의 반응만 넣는다 — 두 개가 서로 싸우지 않게.

누워 있는 자세는 **클립이 직접** RigRoot 를 눕힌다(lying_idle · isolated_struggle 과 같은 방식).
RigRoot 를 θ 만큼 돌리면 골반(rig 기준 (0,hip,0))이 (0, hip·cosθ, hip·sinθ) 로 간다 —
그래서 골반이 가야 할 곳(앉기 → 눕기)을 먼저 정하고 VisualRoot 위치로 그 차이를 메운다(rig_vr).
"""
import math
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_ghost_reaction_clips as G  # noqa: E402
from gen_ghost_reaction_clips import (  # noqa: E402
    pose, merge, add, arms, legs, kneel, Clip, tremble, ORDER, PATHS, LIB, ROOT, fmt)
from gen_cctv_clips import (  # noqa: E402
    reach, stand, kneel_one, fk, head_point, chest_point, seated_legs)

REACH_CS = os.path.join(ROOT, "scripts", "view", "DistractionClipReach.cs")

# 손이 실제 설비에 닿아야 하는 클립 — 생성 뒤 FK 로 재서 DistractionClipReach.cs 에 쓴다.
TOUCH_CLIPS = [  # 설비를 만지는 클립만 — 서는 거리를 이 값으로 정한다
    "dis_rabbit_peek_loop", "dis_dog_probe_loop", "dis_wolf_kick_loop",
]

F, M = "F", "M"


# ── 도우미 ────────────────────────────────────────────────────────────────

def rig_vr(body, theta_deg, hip_y, hip_z=0.0):
    """RigRoot 를 theta 만큼 눕혔을 때 골반이 (0, hip_y, hip_z) 에 오도록 하는 VisualRoot 위치."""
    h = G.BODY[body]["hip"]
    t = math.radians(theta_deg)
    return (0.0, hip_y - h * math.cos(t), hip_z - h * math.sin(t))


def lie(body, theta, hip_y, hip_z, legs_arms):
    """누운(또는 눕는 중) 자세 — 다리·팔 자세에 VisualRoot 보정만 덮는다."""
    p = dict(legs_arms)
    p["VR"] = rig_vr(body, theta, hip_y, hip_z)
    return p


def phone_hold(body, p, side, point, name=""):
    """손바닥을 point 에 둔다(휴대폰은 그 손 노드에 붙는다)."""
    return reach(body, p, **{side: point}, name=name)


def wobble(p, t0, t1, step, rng, amp=1.2, joints=("CHEST", "HEAD")):
    """느린 흔들림 — 숨·체중 이동. tremble 보다 크고 느리다."""
    out = []
    t = t0
    s = 1
    while t < t1 - 1e-4:
        q = p
        for j in joints:
            q = add(q, **{j: (s * amp * rng.uniform(0.5, 1.0), s * amp * 0.4, s * amp * 0.3)})
        out.append((round(t, 3), q))
        s = -s
        t += step * rng.uniform(0.8, 1.25)
    return out


# ── 여우 ──────────────────────────────────────────────────────────────────
#
# FOX_01 기계 앞에서 휴대폰 — 자리를 떠나지 않는다. 한쪽 다리에 체중을 싣고 느긋하게 본다.
# FOX_02 빈 바닥에 대자로 누워 영상 — 걸어가서, 쪼그려 앉고, 등을 내리고, 두 손으로 든다.

def fox_clips(rng):
    out = []
    body = M
    # 휴대폰을 보는 높이 — 배~가슴 사이, 몸에서 30cm 앞.
    hold_pt = (0.11, 1.17, -0.30)
    base = stand(body, TORSO=(-4, 0, 0), CHEST=(-2, 0, 0))
    # 한쪽(왼) 다리에 체중 — 골반을 조금 기울이고 오른 무릎을 느슨하게.
    slouch = merge(legs(body, 0.035, 4, 10, out_r=6), dict(
        HIPS=(0, 0, -3), TORSO=(-3, 0, 2), CHEST=(-2, 0, 1), HEAD=(-24, 0, 0)))

    # ① 진입 — 손이 멈추고, 상체를 조금 뒤로, 허리 주머니에서 꺼내 가슴 앞으로.
    c = Clip("dis_fox_phone_enter", 1.75, False)
    work = merge(stand(body, TORSO=(-7, 0, 0), CHEST=(-3, 0, 0), HEAD=(-10, 0, 0)),
                 arms(42, 6, 62))
    c.key(0.0, work)
    c.key(0.3, merge(stand(body, TORSO=(-5, 0, 0), HEAD=(-6, 0, 0)), arms(30, 6, 44)))
    # 허리 주머니로 손이 내려간다(오른 허벅지 바깥).
    pocket = reach(body, merge(stand(body, 0.02, TORSO=(-1, 0, 2), HEAD=(-4, 0, 0)), arms(10, 5, 16)),
                   R=(0.19, 0.93, -0.06), name=c.name)
    c.key(0.62, pocket)
    c.key(0.78, add(pocket, UAR=(-4, 0, 0), HNDR=(-14, 0, 0)))     # 쥐는 순간
    # 가슴 앞까지 올린다.
    mid = phone_hold(body, merge(slouch, arms(24, 5, 54)), "R", (0.16, 1.03, -0.24), name=c.name)
    c.key(1.1, mid)
    c.key(1.45, phone_hold(body, merge(slouch, arms(26, 4, 62)), "R", (0.12, 1.14, -0.29), name=c.name))
    look = phone_hold(body, merge(slouch, arms(28, 4, 66)), "R", hold_pt, name=c.name)
    # 왼손은 힘을 뺀 채 허리 근처.
    look = reach(body, look, L=(-0.17, 0.99, -0.12), name=c.name)
    c.key(1.75, look)
    out.append(c)

    # ② 반복 — 엄지로 화면을 넘긴다. 가끔 입가·어깨가 움직인다.
    c = Clip("dis_fox_phone_loop", 3.6, True)
    c.key(0.0, look)
    # 엄지 스크롤 = 손목(HNDR) 을 짧게 접었다 편다. 간격은 불규칙하게.
    t = 0.18
    while t < 3.3:
        c.key(round(t, 3), add(look, HNDR=(-13, 0, 0), HEAD=(-1.5, 0, 0)))
        c.key(round(t + 0.14, 3), add(look, HNDR=(4, 0, 0)))
        t += rng.uniform(0.42, 0.72)
    # 중간에 한 번 피식 — 어깨가 들썩이고 고개가 조금 들린다.
    c.key(1.46, add(look, SHL=(0, 0, -7), SHR=(0, 0, 7), CHEST=(2, 0, 0), HEAD=(5, 0, 0)))
    c.key(1.62, add(look, SHL=(0, 0, -3), SHR=(0, 0, 3), HEAD=(2, 0, 0)))
    # 체중을 반대 다리로 옮긴다.
    shift = merge(legs(body, 0.035, 10, 4, out_l=6), dict(
        HIPS=(0, 0, 3), TORSO=(-3, 0, -2), CHEST=(-2, 0, -1), HEAD=(-24, 0, 0)))
    shift = reach(body, merge(shift, arms(28, 4, 66)), R=hold_pt, L=(-0.17, 0.99, -0.12), name=c.name)
    c.key(2.35, shift)
    c.key(3.1, add(shift, HNDR=(-10, 0, 0)))
    out.append(c)

    # ③ 적발 — 손가락이 먼저 멈추고, 어깨를 능청스럽게 한 번.
    c = Clip("dis_fox_phone_notice", 1.5, False)
    c.key(0.0, look)
    c.key(0.12, add(look, HNDR=(2, 0, 0), HEAD=(3, 0, 0)))          # 엄지 멈춤
    up = add(look, HEAD=(26, 0, 0), CHEST=(3, 0, 0), TORSO=(2, 0, 0))
    c.key(0.34, up)                                                  # 고개를 든다(코드가 Neck 으로 돌린다)
    c.key(0.62, add(up, SHL=(0, 0, -9), SHR=(0, 0, 9), HEAD=(2, 0, 0)))   # 어깨 으쓱
    c.key(0.9, add(up, SHL=(0, 0, -4), SHR=(0, 0, 4)))
    # 휴대폰을 몸 옆으로 슬쩍 내린다(감추는 시작).
    hide = reach(body, merge(slouch, dict(HEAD=(14, 0, 0)), arms(16, 4, 36)),
                 R=(0.21, 1.0, -0.14), name=c.name)
    c.key(1.2, hide)
    c.key(1.5, hide)
    out.append(c)

    # ④ 복귀 — 화면을 끄고 주머니에, 기계 쪽으로 몸을 돌린다.
    c = Clip("dis_fox_phone_recover", 1.9, False)
    c.key(0.0, hide)
    c.key(0.28, add(hide, HNDR=(-12, 0, 0), HEAD=(-6, 0, 0)))       # 화면 끄기
    pk = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 1), HEAD=(-10, 0, 0)), arms(10, 5, 14)),
               R=(0.19, 0.92, -0.05), name=c.name)
    c.key(0.75, pk)                                                  # 주머니(여기서 소품을 숨긴다)
    c.key(1.0, add(pk, UAR=(4, 0, 0), HNDR=(6, 0, 0)))
    c.key(1.35, merge(stand(body, TORSO=(-4, 0, 0), HEAD=(-6, 0, 0)), arms(16, 5, 24)))
    c.key(1.9, work)
    out.append(c)

    # ── FOX_02 바닥에 누워 영상 ──
    # 진입: 선 자세(휴대폰을 든 채) → 쪼그려 앉기 → 바닥에 앉기 → 등을 내려 눕기.
    c = Clip("dis_fox_floor_enter", 3.0, False)
    c.rig = [(0.0, (0, 0, 0))]
    standing = reach(body, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(-14, 0, 0)), arms(24, 5, 56)),
                     R=(0.13, 1.12, -0.26), name=c.name)
    c.key(0.0, standing)
    # 바닥을 내려다보며 자세를 낮춘다.
    peek = reach(body, merge(legs(body, 0.1, 22), dict(TORSO=(-16, 0, 0), CHEST=(-6, 0, 0), HEAD=(-4, 0, 0)),
                             arms(20, 5, 50)), R=(0.14, 1.0, -0.3), name=c.name)
    c.key(0.42, peek)
    # 무릎을 굽혀 쪼그린다 — 왼손으로 바닥을 짚는다.
    squat = merge(legs(body, 0.46, 74, 70, out_l=8, out_r=10),
                  dict(TORSO=(-24, 0, 0), CHEST=(-8, 0, 0), HEAD=(6, 0, 0)), arms(20, 6, 50))
    squat = reach(body, squat, L=(-0.16, 0.42, -0.45), R=(0.16, 0.66, -0.26), name=c.name)
    c.key(1.0, squat)
    # 엉덩이를 바닥에 — 아직 상체는 세운 채(한 손으로 뒤를 짚는다).
    sit = merge({"ULL": (74, 0, -7), "ULR": (70, 0, 9), "LLL": (-78, 0, 0), "LLR": (-74, 0, 0),
                 "FL": (-6, 0, 0), "FR": (-6, 0, 0)},
                dict(TORSO=(-8, 0, 0), CHEST=(-2, 0, 0), HEAD=(2, 0, 0)), arms(20, 8, 46))
    sit_p = lie(body, 10, 0.17, -0.04, sit)
    sit_p = reach(body, sit_p, L=(-0.22, 0.1, 0.1), R=(0.17, 0.56, -0.2), name=c.name)
    sit_p["VR"] = rig_vr(body, 10, 0.17, -0.04)
    c.key(1.6, sit_p)
    # 팔꿈치로 받치며 등을 내린다.
    half = merge({"ULL": (46, 0, -6), "ULR": (44, 0, 8), "LLL": (-50, 0, 0), "LLR": (-48, 0, 0),
                  "FL": (-14, 0, 0), "FR": (-14, 0, 0)},
                 dict(TORSO=(4, 0, 0), CHEST=(2, 0, 0), HEAD=(-14, 0, 0)),
                 arms(-28, 20, 54, ua_x_r=10, out_r=12, la_r=40))
    c.key(2.15, lie(body, 54, 0.17, -0.06, half))
    # 등이 바닥에 닿는다.
    down = merge({"ULL": (10, 0, -5), "ULR": (8, 0, 7), "LLL": (-14, 0, 0), "LLR": (-12, 0, 0),
                  "FL": (-26, 0, 0), "FR": (-26, 0, 0)},
                 dict(TORSO=(2, 0, 0), HEAD=(-8, 0, 0)), arms(-10, 16, 40, ua_x_r=4, out_r=10, la_r=44))
    c.key(2.62, lie(body, 86, 0.13, -0.02, down))
    # 두 손으로 휴대폰을 얼굴 위에.
    watch = merge({"ULL": (7, 0, -8), "ULR": (6, 0, 10), "LLL": (-10, 0, 0), "LLR": (-9, 0, 0),
                   "FL": (-28, 0, 0), "FR": (-28, 0, 0)},
                  dict(TORSO=(1, 0, 0), HEAD=(-6, 0, 0)), arms(66, 14, 92, shrug=6))
    watch["VR"] = rig_vr(body, 90, 0.12, 0.0)
    c.key(3.0, watch)
    c.rig = [(0.0, (0, 0, 0)), (0.42, (0, 0, 0)), (1.0, (2, 0, 0)), (1.6, (10, 0, 0)),
             (2.15, (54, 0, 0)), (2.62, (86, 0, 0)), (3.0, (90, 0, 0))]
    out.append(c)

    # 반복 — 천장을 보며 영상. 손목 각도를 바꾸고 고개를 조금 움직인다.
    c = Clip("dis_fox_floor_loop", 4.4, True)
    c.rig = [(0.0, (90, 0, 0))]
    c.key(0.0, watch)
    c.key(0.9, add(watch, HNDL=(-9, 0, 0), HNDR=(-9, 0, 0), HEAD=(-2, 0, 0)))
    c.key(1.7, add(watch, LAL=(5, 0, 0), LAR=(5, 0, 0), HEAD=(2, 0, 0)))
    # 한쪽 무릎을 세웠다 내린다(편한 자세로 고쳐 눕기).
    knee = add(watch, ULR=(26, 0, 4), LLR=(-46, 0, 0), FR=(10, 0, 0))
    c.key(2.5, knee)
    c.key(3.2, add(knee, HNDL=(-6, 0, 0), HNDR=(-6, 0, 0), HEAD=(-3, 0, 0)))
    c.key(3.9, add(watch, ULR=(10, 0, 4), LLR=(-20, 0, 0), HEAD=(1, 0, 0)))
    out.append(c)

    # 적발 — 휴대폰을 든 채 멈추고, 고개를 CCTV 쪽으로(코드) · 몸이 한 번 움찔.
    c = Clip("dis_fox_floor_notice", 1.7, False)
    c.rig = [(0.0, (90, 0, 0))]
    c.key(0.0, watch)
    c.key(0.13, add(watch, CHEST=(0, 0, 0), HEAD=(6, 0, 0), SHL=(0, 0, -5), SHR=(0, 0, 5)))
    # 휴대폰을 조금 내리고 턱을 당긴다(화면 밖을 본다).
    seen = add(watch, UAL=(-10, 0, 0), UAR=(-10, 0, 0), LAL=(-14, 0, 0), LAR=(-14, 0, 0),
               HEAD=(10, 0, 0), TORSO=(-3, 0, 0))
    c.key(0.42, seen)
    c.key(0.75, add(seen, HEAD=(2, 0, 0)))
    # 일어날 준비 — 복근으로 상체를 조금 든다.
    ready = add(seen, TORSO=(-9, 0, 0), CHEST=(-4, 0, 0), ULR=(22, 0, 4), LLR=(-40, 0, 0))
    c.key(1.2, ready)
    c.key(1.7, ready)
    out.append(c)

    # 복귀 — 가슴으로 내리고 → 팔꿈치로 일으키고 → 앉고 → 한 무릎 세우고 → 선다.
    c = Clip("dis_fox_floor_recover", 3.4, False)
    chest_hold = add(watch, UAL=(-24, 0, 0), UAR=(-24, 0, 0), LAL=(-18, 0, 0), LAR=(-18, 0, 0),
                     ULR=(22, 0, 4), LLR=(-40, 0, 0))
    c.key(0.0, chest_hold)
    # 팔꿈치로 받쳐 상체를 든다(오른손은 휴대폰을 쥔 채).
    elbow = merge({"ULL": (16, 0, -5), "ULR": (40, 0, 7), "LLL": (-22, 0, 0), "LLR": (-62, 0, 0),
                   "FL": (-20, 0, 0), "FR": (-2, 0, 0)},
                  dict(TORSO=(-16, 0, 0), CHEST=(-6, 0, 0), HEAD=(-10, 0, 0)),
                  arms(-34, 22, 72, ua_x_r=34, out_r=10, la_r=66))
    c.key(0.6, lie(body, 62, 0.15, -0.04, elbow))
    # 앉은 자세.
    up_sit = merge({"ULL": (72, 0, -7), "ULR": (68, 0, 9), "LLL": (-76, 0, 0), "LLR": (-72, 0, 0),
                    "FL": (-6, 0, 0), "FR": (-6, 0, 0)},
                   dict(TORSO=(-10, 0, 0), CHEST=(-4, 0, 0), HEAD=(-4, 0, 0)),
                   arms(-20, 16, 56, ua_x_r=28, out_r=8, la_r=58))
    c.key(1.2, lie(body, 12, 0.25, -0.04, up_sit))
    # 한쪽 무릎을 세운다.
    one = merge(kneel_one(body, "L"), dict(TORSO=(-22, 0, 0), CHEST=(-8, 0, 0), HEAD=(4, 0, 0)),
                arms(16, 6, 40))
    one = reach(body, one, L=(-0.18, 0.42, -0.3), R=(0.2, 0.56, -0.2), name=c.name)
    c.key(1.95, one)
    # 선다.
    c.key(2.45, reach(body, merge(legs(body, 0.2, 38, 26), dict(TORSO=(-20, 0, 0), CHEST=(-6, 0, 0), HEAD=(2, 0, 0)),
                                  arms(18, 6, 44)), R=(0.19, 0.88, -0.2), name=c.name))
    # 휴대폰을 주머니에(소품을 숨기는 시점).
    pk2 = reach(body, merge(stand(body, 0.02, TORSO=(-4, 0, 1), HEAD=(-6, 0, 0)), arms(12, 5, 18)),
                R=(0.19, 0.93, -0.05), name=c.name)
    c.key(2.9, pk2)
    c.key(3.4, merge(stand(body, TORSO=(-3, 0, 0)), arms(8, 4, 14)))
    c.rig = [(0.0, (90, 0, 0)), (0.6, (62, 0, 0)), (1.2, (12, 0, 0)), (1.95, (0, 0, 0)), (3.4, (0, 0, 0))]
    out.append(c)
    return out


# ── 양 ────────────────────────────────────────────────────────────────────
#
# SHEEP_01 작업 중 꾸벅꾸벅 졸기 — 자리에서. 적발되면 화들짝 놀라 허둥지둥 일한다.
# SHEEP_02 무서워서 다른 오브젝트 옆에 웅크리기 — 주변을 살피고 걸어가 쪼그려 떤다.

def sheep_clips(rng):
    out = []
    body = F
    work = merge(stand(body, TORSO=(-6, 0, 0), CHEST=(-3, 0, 0), HEAD=(-10, 0, 0)), arms(38, 5, 58))

    # ① 졸음 진입 — 팔이 느려지고 어깨가 처지고 고개가 두어 번 떨어진다.
    c = Clip("dis_sheep_doze_enter", 2.4, False)
    c.key(0.0, work)
    slow = merge(stand(body, 0.01, TORSO=(-7, 0, 0), CHEST=(-4, 0, 0), HEAD=(-16, 0, 0)),
                 arms(32, 5, 50, shrug=-4))
    c.key(0.45, slow)
    nod1 = merge(stand(body, 0.02, TORSO=(-9, 0, 0), CHEST=(-6, 0, 0), HEAD=(-30, 0, 0)),
                 arms(26, 5, 44, shrug=-7))
    c.key(0.95, nod1)
    c.key(1.2, add(nod1, HEAD=(16, 0, 0), CHEST=(2, 0, 0)))          # 잠깐 든다
    c.key(1.6, add(nod1, HEAD=(-8, 0, 0)))                            # 다시 내려간다
    c.key(1.82, add(nod1, HEAD=(10, 0, 0)))
    doze = merge(stand(body, 0.03, TORSO=(-12, 0, 0), CHEST=(-8, 0, 0), HEAD=(-36, 0, 0)),
                 arms(18, 4, 32, shrug=-10))
    c.key(2.4, doze)
    out.append(c)

    # ② 반복 — 고개가 더 깊게 떨어지고 손이 완전히 멈춘다. 숨만 오간다.
    c = Clip("dis_sheep_doze_loop", 3.8, True)
    c.key(0.0, doze)
    deep = merge(stand(body, 0.04, TORSO=(-15, 0, 0), CHEST=(-10, 0, 0), HEAD=(-44, 0, 0)),
                 arms(14, 4, 26, shrug=-12))
    c.key(0.8, deep)
    c.key(1.5, add(deep, HEAD=(12, 0, 0), CHEST=(3, 0, 0)))           # 흠칫 들었다
    c.key(1.75, deep)                                                  # 다시 떨어진다
    for t, q in wobble(deep, 2.0, 3.0, 0.34, rng, amp=1.1):
        c.key(t, q)
    c.key(3.2, add(deep, HEAD=(8, 0, 0)))
    c.key(3.55, deep)
    out.append(c)

    # ③ 적발 — 고개를 확 들고 두 어깨가 동시에 움찔, 상체가 뒤로 젖혀진다.
    c = Clip("dis_sheep_doze_notice", 1.4, False)
    c.key(0.0, deep)
    jolt = merge(legs(body, -0.012, 6, -4), dict(
        VR=(0, 0.012, 0.02), TORSO=(9, 0, 0), CHEST=(7, 0, 0), HEAD=(32, 0, 0)),
        arms(44, 14, 86, shrug=20, hand=-12))
    c.key(0.14, jolt)                                                  # 0.14초 — 가장 빠른 반응
    c.key(0.3, add(jolt, HEAD=(-4, 0, 0), VR=(0, -0.004, 0)))
    c.key(0.52, add(jolt, HEAD=(2, 0, 0), SHL=(0, 0, -4), SHR=(0, 0, 4)))
    c.key(0.9, add(jolt, HEAD=(-6, 0, 0), TORSO=(-4, 0, 0), CHEST=(-3, 0, 0)))
    c.key(1.4, merge(stand(body, 0.01, TORSO=(-4, 0, 0), CHEST=(-2, 0, 0), HEAD=(6, 0, 0)),
                     arms(34, 8, 66, shrug=12)))
    out.append(c)

    # ④ 복귀 — 황급히 기계 쪽으로. (작업 모션 가속은 코드가 재생 속도로 준다.)
    c = Clip("dis_sheep_doze_recover", 1.3, False)
    c.key(0.0, merge(stand(body, 0.01, TORSO=(-4, 0, 0), HEAD=(6, 0, 0)), arms(34, 8, 66, shrug=12)))
    c.key(0.3, merge(stand(body, 0.02, TORSO=(-10, 0, 0), CHEST=(-4, 0, 0), HEAD=(-6, 0, 0)),
                     arms(46, 6, 74, shrug=8)))
    c.key(0.7, merge(stand(body, 0.01, TORSO=(-8, 0, 0), CHEST=(-4, 0, 0), HEAD=(-14, 0, 0)),
                     arms(40, 5, 62, shrug=4)))
    c.key(1.3, work)
    out.append(c)

    # ── SHEEP_02 웅크리기 ──
    # 진입(자리에서) — 손이 멈추고 고개를 천천히 좌우로, 어깨를 움츠린다.
    c = Clip("dis_sheep_hide_scan", 2.6, False)
    c.key(0.0, work)
    halt = merge(stand(body, 0.01, TORSO=(-5, 0, 0), CHEST=(-3, 0, 0), HEAD=(-4, 0, 0)),
                 arms(26, 5, 44, shrug=4))
    c.key(0.35, halt)
    # 좌 → 우 (고개의 좌우는 여기서는 괜찮다 — 적발 반응이 아니라 '살피는' 동작이다)
    c.key(0.9, add(halt, HEAD=(2, 34, 0), CHEST=(0, 8, 0)))
    c.key(1.5, add(halt, HEAD=(2, -30, 0), CHEST=(0, -7, 0)))
    shrink = merge(stand(body, 0.03, TORSO=(-11, 0, 0), CHEST=(-7, 0, 0), HEAD=(-14, 0, 0)),
                   arms(30, -8, 72, shrug=16, hand=-10))
    c.key(2.0, shrink)
    for t, q in tremble(rng, shrink, 2.1, 2.55, 0.1, amp_vr=0.004, amp_rot=1.0):
        c.key(t, q)
    out.append(c)

    # 도착해서 웅크린다 — 무릎을 굽혀 앉고 상체를 깊이 숙이고 팔을 모은다.
    c = Clip("dis_sheep_hide_crouch", 1.5, False)
    c.key(0.0, shrink)
    c.key(0.45, merge(legs(body, 0.2, 40), dict(TORSO=(-20, 0, 0), CHEST=(-10, 0, 0), HEAD=(-6, 0, 0)),
                      arms(34, -10, 86, shrug=16, hand=-12)))
    crouch = merge(legs(body, 0.44, 70), dict(TORSO=(-34, 0, 0), CHEST=(-16, 0, 0), HEAD=(-18, 0, 0)),
                   arms(62, -14, 112, shrug=22, hand=-16))
    c.key(1.5, crouch)
    out.append(c)

    # 반복 — 몸을 말고 미세하게 떨며 가끔 주변을 올려다본다.
    c = Clip("dis_sheep_hide_loop", 3.6, True)
    c.key(0.0, crouch)
    for t, q in tremble(rng, crouch, 0.08, 0.85, 0.085, amp_vr=0.005, amp_rot=1.5):
        c.key(t, q)
    peek = add(crouch, HEAD=(30, 0, 0), CHEST=(6, 0, 0), VR=(0, 0.02, 0))
    c.key(1.0, peek)
    c.key(1.35, add(peek, HEAD=(0, 22, 0)))
    c.key(1.7, crouch)
    for t, q in tremble(rng, crouch, 1.8, 2.6, 0.08, amp_vr=0.005, amp_rot=1.5):
        c.key(t, q)
    c.key(2.8, add(crouch, CHEST=(4, 0, 0), HEAD=(10, 0, 0)))
    for t, q in tremble(rng, crouch, 2.95, 3.5, 0.09, amp_vr=0.005, amp_rot=1.4):
        c.key(t, q)
    out.append(c)

    # 적발 — 화들짝 상체를 들고 CCTV 를 올려다보고 잠깐 굳은 뒤 황급히 일어난다.
    c = Clip("dis_sheep_hide_notice", 1.7, False)
    c.key(0.0, crouch)
    shock = merge(legs(body, 0.36, 60), dict(TORSO=(-14, 0, 0), CHEST=(-2, 0, 0), HEAD=(34, 0, 0)),
                  arms(70, 6, 120, shrug=26, hand=-18))
    c.key(0.16, shock)
    c.key(0.34, add(shock, HEAD=(-6, 0, 0), VR=(0, -0.01, 0)))
    c.key(0.6, add(shock, HEAD=(4, 0, 0)))                            # 굳어 있다
    c.key(0.95, add(shock, HEAD=(2, 0, 0), CHEST=(2, 0, 0)))
    rise = merge(legs(body, 0.14, 26), dict(TORSO=(-16, 0, 0), CHEST=(-8, 0, 0), HEAD=(10, 0, 0)),
                 arms(44, 2, 96, shrug=20, hand=-12))
    c.key(1.3, rise)
    stoop = merge(stand(body, 0.03, TORSO=(-13, 0, 0), CHEST=(-8, 0, 0), HEAD=(-6, 0, 0)),
                  arms(34, -6, 76, shrug=18, hand=-10))
    c.key(1.7, stoop)
    out.append(c)

    # 복귀 — 아직 어깨를 움츠린 채. (걸어 돌아가는 것은 코드가 walk 로 한다.)
    c = Clip("dis_sheep_hide_recover", 1.5, False)
    c.key(0.0, stoop)
    for t, q in tremble(rng, stoop, 0.1, 0.7, 0.1, amp_vr=0.004, amp_rot=1.1):
        c.key(t, q)
    c.key(0.9, merge(stand(body, 0.02, TORSO=(-9, 0, 0), CHEST=(-5, 0, 0), HEAD=(-10, 0, 0)),
                     arms(34, 2, 62, shrug=10)))
    c.key(1.5, work)
    out.append(c)
    return out


# ── 고양이 ────────────────────────────────────────────────────────────────
#
# CAT_01 즐겁게 개인 통화 — 오른 귀에 휴대폰. 적발돼도 태연하게 통화를 조금 더 한다.
# CAT_02 벽에 기대 잠깐 잠들기 — 짜증스럽게 깨어난다.

def cat_clips(rng):
    out = []
    body = F
    work = merge(stand(body, TORSO=(-6, 0, 0), CHEST=(-3, 0, 0), HEAD=(-9, 0, 0)), arms(36, 5, 56))

    # 귀 옆 — 머리 기준 좌표로 잡아야 고개를 움직여도 손이 귀에 붙어 있다.
    def ear_pose(p, extra_head=(0, 0, 0), name=""):
        q = add(p, HEAD=extra_head)
        pt = tuple(head_point(body, q, (0.125, 0.015, 0.0)))
        return reach(body, q, R=pt, name=name)

    # ① 진입 — 작업을 멈추고 꺼내 화면을 확인한 뒤 귀로.
    c = Clip("dis_cat_call_enter", 1.9, False)
    c.key(0.0, work)
    c.key(0.26, merge(stand(body, TORSO=(-4, 0, 0), HEAD=(-6, 0, 0)), arms(24, 5, 40)))
    pocket = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 2), HEAD=(-6, 0, 0)), arms(10, 5, 14)),
                   R=(0.17, 0.78, -0.05), name=c.name)
    c.key(0.56, pocket)
    screen = reach(body, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(-22, 0, 0)), arms(24, 5, 60)),
                   R=(0.1, 0.98, -0.24), name=c.name)
    c.key(0.95, screen)                                               # 화면 확인
    c.key(1.2, add(screen, HNDR=(-12, 0, 0)))
    lean = merge(legs(body, 0.02, 4, 9, out_r=5), dict(
        HIPS=(0, 0, -2), TORSO=(-2, 0, 2), CHEST=(-1, 0, 1), HEAD=(2, 0, 0)))
    talk = ear_pose(merge(lean, arms(40, 10, 100, shrug=6)), name=c.name)
    talk = reach(body, talk, L=(-0.16, 0.8, -0.08), name=c.name)      # 왼손은 허리
    c.key(1.55, talk)
    c.key(1.9, talk)
    out.append(c)

    # ② 반복 — 어깨·상체가 작게 움직이고 가볍게 웃는다. 듣는 동안 잠깐 멈춘다.
    c = Clip("dis_cat_call_loop", 4.0, True)
    c.key(0.0, talk)
    c.key(0.4, add(talk, CHEST=(0, 7, 0), HEAD=(3, 0, 0), SHR=(0, 0, 4)))
    c.key(0.75, add(talk, CHEST=(0, -5, 0), HEAD=(-2, 0, 0)))
    laugh = add(talk, CHEST=(5, 0, 0), HEAD=(8, 0, 0), SHL=(0, 0, -8), SHR=(0, 0, 8), VR=(0, 0.008, 0))
    c.key(1.15, laugh)
    c.key(1.35, add(talk, HEAD=(3, 0, 0)))
    # 듣는 구간 — 거의 멈춘다.
    listen = add(talk, HEAD=(-4, 0, 0), CHEST=(0, 3, 0))
    c.key(1.8, listen)
    c.key(2.4, add(listen, HEAD=(-2, 0, 0)))
    c.key(2.8, add(talk, CHEST=(0, 6, 0), HEAD=(2, 0, 0)))
    # 체중을 반대 다리로.
    lean2 = merge(legs(body, 0.02, 9, 4, out_l=5), dict(
        HIPS=(0, 0, 2), TORSO=(-2, 0, -2), CHEST=(-1, 0, -1), HEAD=(2, 0, 0)))
    talk2 = ear_pose(merge(lean2, arms(40, 10, 100, shrug=6)), name=c.name)
    talk2 = reach(body, talk2, L=(-0.16, 0.8, -0.08), name=c.name)
    c.key(3.3, talk2)
    c.key(3.75, add(talk2, CHEST=(0, -4, 0), HEAD=(-2, 0, 0)))
    out.append(c)

    # ③ 적발 — 고개를 돌려 잠깐 보고, 끊지 않는다. 뻔뻔하게 통화를 잇는다.
    c = Clip("dis_cat_call_notice", 2.1, False)
    c.key(0.0, talk)
    c.key(0.22, ear_pose(merge(lean, arms(40, 10, 100, shrug=6)), (12, 0, 0), name=c.name))
    look = ear_pose(merge(lean, arms(40, 10, 100, shrug=7)), (22, 0, 0), name=c.name)
    c.key(0.5, look)
    c.key(0.95, add(look, CHEST=(0, 4, 0)))                           # 보면서도 통화 계속
    c.key(1.35, add(look, HEAD=(-4, 0, 0), CHEST=(0, -3, 0), SHL=(0, 0, -5), SHR=(0, 0, 5)))
    c.key(1.7, ear_pose(merge(lean, arms(40, 10, 100, shrug=6)), (10, 0, 0), name=c.name))
    c.key(2.1, talk)
    out.append(c)

    # ④ 복귀 — 마지막 한마디 → 귀에서 떼고 → 화면 확인 → 주머니.
    c = Clip("dis_cat_call_recover", 2.1, False)
    c.key(0.0, talk)
    c.key(0.3, add(talk, HEAD=(5, 0, 0), CHEST=(0, 5, 0)))
    off = reach(body, merge(stand(body, 0.01, TORSO=(-2, 0, 0), HEAD=(-14, 0, 0)), arms(30, 6, 74)),
                R=(0.12, 1.0, -0.2), name=c.name)
    c.key(0.75, off)
    c.key(1.1, add(off, HNDR=(-14, 0, 0), HEAD=(-6, 0, 0)))           # 화면 확인
    pk = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 1), HEAD=(-8, 0, 0)), arms(10, 5, 14)),
               R=(0.17, 0.78, -0.04), name=c.name)
    c.key(1.55, pk)                                                    # 주머니(소품 숨김)
    c.key(2.1, work)
    out.append(c)

    # ── CAT_02 벽에 기대 잠들기 ──
    # (몸을 벽에 붙이는 위치·방향은 코드가 정한다. 클립은 '기대는 몸'만 만든다.)
    c = Clip("dis_cat_wall_enter", 1.8, False)
    stand_f = merge(stand(body), dict(TORSO=(-2, 0, 0)))
    c.key(0.0, stand_f)
    # 등을 벽에 댄다 — 어깨가 먼저 닿고 골반이 앞으로 나간다.
    touch = merge(legs(body, 0.02, 10, 2, out_r=4), dict(
        HIPS=(0, 0, 0), TORSO=(5, 0, 0), CHEST=(2, 0, 0), HEAD=(-4, 0, 0)), arms(8, 6, 16))
    c.key(0.55, touch)
    lean = merge(legs(body, 0.06, 16, 3, out_l=3, out_r=7), dict(
        HIPS=(0, 0, -4), TORSO=(7, 0, 3), CHEST=(3, 0, 2), HEAD=(-14, 0, 0)),
        arms(6, 9, 22, shrug=-5))
    c.key(1.1, lean)
    nap = merge(legs(body, 0.07, 18, 3, out_l=3, out_r=8), dict(
        HIPS=(0, 0, -5), TORSO=(8, 0, 4), CHEST=(2, 0, 2), HEAD=(-34, 0, 0)),
        arms(10, 7, 30, shrug=-9, hand=-8))
    c.key(1.8, nap)
    out.append(c)

    c = Clip("dis_cat_wall_loop", 4.2, True)
    c.key(0.0, nap)
    for t, q in wobble(nap, 0.3, 1.6, 0.4, rng, amp=1.0):
        c.key(t, q)
    c.key(1.9, add(nap, HEAD=(9, 0, 0), CHEST=(2, 0, 0)))              # 고개가 한 번 들렸다
    c.key(2.2, add(nap, HEAD=(-4, 0, 0)))
    for t, q in wobble(nap, 2.5, 3.8, 0.42, rng, amp=1.0):
        c.key(t, q)
    out.append(c)

    # 적발 — 천천히 고개를 들고 짜증스럽게 도리도리.
    c = Clip("dis_cat_wall_notice", 2.2, False)
    c.key(0.0, nap)
    c.key(0.3, add(nap, HEAD=(14, 0, 0)))
    up = merge(legs(body, 0.06, 15, 3, out_l=3, out_r=7), dict(
        HIPS=(0, 0, -4), TORSO=(6, 0, 3), CHEST=(2, 0, 1), HEAD=(16, 0, 0)),
        arms(10, 7, 28, shrug=-4))
    c.key(0.62, up)                                                    # 올려다본다
    c.key(0.95, add(up, HEAD=(12, 12, 0)))                             # 도리도리 ①
    c.key(1.2, add(up, HEAD=(12, -12, 0)))                             # ②
    c.key(1.45, add(up, HEAD=(13, 8, 0)))
    c.key(1.75, add(up, HEAD=(8, 0, 0), SHL=(0, 0, -5), SHR=(0, 0, 5)))
    c.key(2.2, add(up, HEAD=(4, 0, 0)))
    out.append(c)

    # 복귀 — 벽에서 등을 떼고 자세를 바로잡는다.
    c = Clip("dis_cat_wall_recover", 1.7, False)
    c.key(0.0, add(up, HEAD=(4, 0, 0)))
    c.key(0.5, merge(legs(body, 0.03, 8, 3), dict(HIPS=(0, 0, -2), TORSO=(2, 0, 1), HEAD=(2, 0, 0)),
                     arms(12, 6, 24)))
    c.key(1.0, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(-4, 0, 0)), arms(10, 4, 18)))
    c.key(1.7, stand_f)
    out.append(c)
    return out


# ── 토끼 ──────────────────────────────────────────────────────────────────
#
# RABBIT_01 작업 기기를 호기심 있게 조사 — 손가락으로 톡톡 두드린다.
# RABBIT_02 기분 좋은 날 셀카 — 팔을 뻗고 V 포즈.

def rabbit_clips(rng):
    out = []
    body = F
    work = merge(stand(body, TORSO=(-6, 0, 0), CHEST=(-3, 0, 0), HEAD=(-9, 0, 0)), arms(36, 5, 56))
    # 기기 표면 — 몸에서 앞으로 0.40m, 손 높이 1.02m(여성 체형 기준 패널 높이).
    PANEL = 0.40
    PH = 1.02

    c = Clip("dis_rabbit_peek_enter", 1.6, False)
    c.key(0.0, work)
    lean_in = merge(legs(body, 0.04, 12), dict(TORSO=(-20, 0, 0), CHEST=(-8, 0, 0), HEAD=(6, 0, 0)),
                    arms(30, 5, 54))
    lean_in = reach(body, lean_in, R=(0.12, PH + 0.04, -PANEL), name=c.name)
    c.key(0.55, lean_in)
    tilt = add(lean_in, HEAD=(4, 20, 0), CHEST=(0, 5, 0))
    c.key(1.0, tilt)
    c.key(1.6, add(lean_in, HEAD=(2, -8, 0)))
    out.append(c)

    # 반복 — 두드리기(손가락) · 고개 갸웃 · 숙였다 펴기.
    c = Clip("dis_rabbit_peek_loop", 4.2, True)
    tap = reach(body, lean_in, R=(0.13, PH, -PANEL - 0.02), name=c.name)
    off = reach(body, lean_in, R=(0.13, PH + 0.05, -PANEL + 0.05), name=c.name)
    c.key(0.0, off)
    for t in (0.2, 0.52, 0.84):
        c.key(round(t, 3), tap)
        c.key(round(t + 0.13, 3), off)
    side = reach(body, merge(legs(body, 0.05, 14), dict(TORSO=(-22, 0, 10), CHEST=(-8, 0, 4), HEAD=(2, 26, 0)),
                             arms(30, 5, 54)), R=(0.2, PH - 0.1, -PANEL + 0.04), name=c.name)
    c.key(1.35, side)                                                  # 옆면을 살핀다
    c.key(1.8, add(side, HEAD=(-6, 18, 0), TORSO=(-26, 0, 10)))
    back = reach(body, merge(legs(body, 0.08, 20), dict(TORSO=(-28, 0, -8), CHEST=(-10, 0, -4), HEAD=(0, -30, 0)),
                             arms(26, 5, 48)), L=(-0.2, PH - 0.14, -PANEL + 0.06), name=c.name)
    c.key(2.4, back)                                                   # 뒤쪽을 들여다본다
    c.key(2.9, add(back, HEAD=(6, -22, 0)))
    c.key(3.3, off)
    c.key(3.6, tap)
    c.key(3.74, off)
    out.append(c)

    # 적발 — 한 손으로 입을 가리고 반대 손은 몸 가까이. 어깨가 움찔.
    c = Clip("dis_rabbit_peek_notice", 1.5, False)
    c.key(0.0, off)
    c.key(0.12, add(off, HEAD=(16, 0, 0), CHEST=(3, 0, 0), SHL=(0, 0, -7), SHR=(0, 0, 7)))
    upright = merge(stand(body, 0.01, TORSO=(-2, 0, 0), CHEST=(0, 0, 0), HEAD=(22, 0, 0)),
                    arms(74, 16, 126, shrug=14))
    mouth = tuple(head_point(body, upright, (0.075, -0.05, -0.125)))
    cover = reach(body, upright, R=mouth, L=(-0.11, 0.86, -0.14), name=c.name)
    c.key(0.34, cover)                                                 # 입을 가린다
    c.key(0.62, add(cover, VR=(0, 0, 0.02), HEAD=(2, 0, 0)))
    c.key(1.0, add(cover, HEAD=(-2, 0, 0), SHL=(0, 0, -3), SHR=(0, 0, 3)))
    c.key(1.5, cover)
    out.append(c)

    c = Clip("dis_rabbit_peek_recover", 1.7, False)
    c.key(0.0, cover)
    c.key(0.5, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(8, 0, 0)), arms(22, 6, 44, shrug=6)))
    c.key(0.95, reach(body, merge(legs(body, 0.02, 6), dict(TORSO=(-10, 0, 0), CHEST=(-4, 0, 0), HEAD=(0, 14, 0)),
                                  arms(26, 5, 48)), R=(0.12, PH + 0.02, -PANEL + 0.06), name=c.name))
    c.key(1.7, work)
    out.append(c)

    # ── RABBIT_02 셀카 ──
    c = Clip("dis_rabbit_selfie_enter", 1.7, False)
    c.key(0.0, work)
    pocket = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 2), HEAD=(-6, 0, 0)), arms(10, 5, 14)),
                   R=(0.17, 0.78, -0.05), name=c.name)
    c.key(0.42, pocket)
    screen = reach(body, merge(stand(body, 0.01, TORSO=(-2, 0, 0), HEAD=(-18, 0, 0)), arms(26, 6, 62)),
                   R=(0.1, 1.0, -0.22), name=c.name)
    c.key(0.8, screen)
    # 얼굴보다 높게 — 팔을 위로 뻗는다.
    hi = merge(legs(body, 0.0, 2), dict(TORSO=(1, 0, -3), CHEST=(2, 0, -2), HEAD=(12, 0, 6)),
               arms(30, 8, 50))
    selfie = reach(body, hi, R=(0.2, 1.62, -0.3), L=(-0.14, 0.82, -0.1), name=c.name)
    c.key(1.25, selfie)
    c.key(1.7, selfie)
    out.append(c)

    # 반복 — V 포즈 · 고개 기울임 · 촬영 버튼(손목) · 몸 기울임.
    c = Clip("dis_rabbit_selfie_loop", 3.8, True)
    # 반대 손으로 V — 얼굴 옆으로 올린다.
    vpose = reach(body, add(hi, HEAD=(10, 0, 8)), R=(0.2, 1.62, -0.3), L=(-0.14, 1.32, -0.26), name=c.name)
    vpose["HNDL"] = (0, 0, -18)
    c.key(0.0, vpose)
    c.key(0.5, add(vpose, HEAD=(8, -6, 10), TORSO=(1, 0, -5)))
    c.key(0.85, add(vpose, HNDR=(-16, 0, 0)))                          # 셔터 ①
    c.key(1.0, add(vpose, HNDR=(2, 0, 0)))
    c.key(1.45, add(vpose, HEAD=(12, 8, -8), TORSO=(1, 0, 4), CHEST=(2, 0, 3)))
    tilt2 = reach(body, add(hi, HEAD=(10, 6, -9)), R=(0.14, 1.6, -0.34), L=(-0.1, 1.3, -0.28), name=c.name)
    tilt2["HNDL"] = (0, 0, 16)
    c.key(2.0, tilt2)
    c.key(2.35, add(tilt2, HNDR=(-16, 0, 0)))                          # 셔터 ②
    c.key(2.5, add(tilt2, HNDR=(2, 0, 0)))
    c.key(3.0, add(vpose, HEAD=(9, -4, 9)))
    c.key(3.45, add(vpose, SHL=(0, 0, -5), SHR=(0, 0, 5), CHEST=(3, 0, 0)))
    out.append(c)

    # 적발 — 동작이 갑자기 멈추고 반대 손으로 입을 가리고 뒤로 움찔.
    c = Clip("dis_rabbit_selfie_notice", 1.6, False)
    c.key(0.0, vpose)
    c.key(0.1, add(vpose, HEAD=(16, 0, 4), CHEST=(2, 0, 0)))
    upright = merge(stand(body, 0.0, TORSO=(2, 0, 0), HEAD=(20, 0, 0)), arms(74, 18, 126, shrug=16))
    mouth = tuple(head_point(body, upright, (-0.08, -0.05, -0.125)))
    oops = reach(body, upright, L=mouth, R=(0.2, 1.24, -0.2), name=c.name)
    c.key(0.32, oops)
    c.key(0.52, add(oops, VR=(0, 0, 0.035), TORSO=(4, 0, 0), HEAD=(16, 0, 0)))   # 뒤로 움찔
    c.key(0.8, add(oops, VR=(0, 0, 0.015)))
    c.key(1.15, add(oops, HEAD=(12, 0, 0)))
    c.key(1.6, oops)
    out.append(c)

    # 복귀 — 휴대폰을 내리고 주머니에, 옷매무새를 정리.
    c = Clip("dis_rabbit_selfie_recover", 1.9, False)
    c.key(0.0, oops)
    c.key(0.42, reach(body, merge(stand(body, 0.01, TORSO=(-2, 0, 0), HEAD=(2, 0, 0)), arms(26, 6, 56)),
                      R=(0.12, 1.02, -0.22), name=c.name))
    pk = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 1), HEAD=(-8, 0, 0)), arms(10, 5, 14)),
               R=(0.17, 0.78, -0.04), name=c.name)
    c.key(0.9, pk)                                                     # 주머니(소품 숨김)
    # 옷매무새 — 두 손으로 앞섶을 쓸어내린다.
    tidy = reach(body, merge(stand(body, 0.0, TORSO=(-5, 0, 0), HEAD=(-12, 0, 0)), arms(24, 6, 60)),
                 L=(-0.1, 0.96, -0.16), R=(0.1, 0.96, -0.16), name=c.name)
    c.key(1.2, tidy)
    c.key(1.45, reach(body, merge(stand(body, 0.0, TORSO=(-4, 0, 0), HEAD=(-10, 0, 0)), arms(18, 6, 44)),
                      L=(-0.11, 0.82, -0.12), R=(0.11, 0.82, -0.12), name=c.name))
    c.key(1.9, work)
    out.append(c)
    return out


# ── 늑대 ──────────────────────────────────────────────────────────────────
#
# WOLF_01 벽에 기대 잠깐 — 고양이와 같은 유형이지만 훨씬 절제돼 있다(팔짱, 거의 안 움직인다).
# WOLF_02 기기를 발로 차고 손으로 두드린다 — 발차기와 손 타격의 박자가 다르다.

def wolf_clips(rng):
    out = []
    body = M
    work = merge(stand(body, TORSO=(-6, 0, 0), CHEST=(-3, 0, 0), HEAD=(-8, 0, 0)), arms(40, 5, 60))

    c = Clip("dis_wolf_wall_enter", 2.1, False)
    stand_m = merge(stand(body), dict(TORSO=(-2, 0, 0)))
    c.key(0.0, work)
    heavy = merge(stand(body, 0.02, TORSO=(-6, 0, 0), CHEST=(-4, 0, 0), HEAD=(-12, 0, 0)),
                  arms(12, 4, 20, shrug=-10))
    c.key(0.5, heavy)                                                  # 어깨를 내린다
    touch = merge(legs(body, 0.02, 8, 2, out_r=4), dict(
        TORSO=(5, 0, 0), CHEST=(2, 0, 0), HEAD=(-6, 0, 0)), arms(10, 5, 18, shrug=-6))
    c.key(1.1, touch)
    # 팔짱 — 가슴 앞에서 두 팔을 겹친다.
    fold = merge(legs(body, 0.05, 13, 3, out_l=3, out_r=6), dict(
        HIPS=(0, 0, -3), TORSO=(7, 0, 2), CHEST=(2, 0, 1), HEAD=(-18, 0, 0)), arms(22, 6, 92))
    cl = tuple(chest_point(body, fold, (0.13, -0.11, -0.2)))
    cr = tuple(chest_point(body, fold, (-0.14, -0.07, -0.23)))
    folded = reach(body, fold, L=cl, R=cr, name=c.name)
    c.key(1.6, folded)
    nap = add(folded, HEAD=(-10, 0, 0), CHEST=(-2, 0, 0))
    c.key(2.1, nap)
    out.append(c)

    c = Clip("dis_wolf_wall_loop", 4.6, True)
    c.key(0.0, nap)
    # 호흡만 — 가슴이 아주 조금, 느리게.
    t = 0.0
    while t < 4.3:
        c.key(round(t + 0.9, 3), add(nap, CHEST=(2.2, 0, 0), SHL=(0, 0, -2.5), SHR=(0, 0, 2.5)))
        c.key(round(t + 1.8, 3), nap)
        t += 1.8
    c.key(2.6, add(nap, HEAD=(-3, 0, 0), HIPS=(0, 0, -4)))             # 체중을 조금 옮긴다
    out.append(c)

    # 적발 — 크게 움찔하지 않는다. 조용히 보고 작게 끄덕인다.
    c = Clip("dis_wolf_wall_notice", 1.8, False)
    c.key(0.0, nap)
    c.key(0.26, add(folded, HEAD=(8, 0, 0)))
    look = add(folded, HEAD=(18, 0, 0), CHEST=(1, 0, 0))
    c.key(0.5, look)
    c.key(1.0, look)                                                   # 0.5초 조용히 바라본다
    c.key(1.2, add(look, HEAD=(10, 0, 0)))                             # 작은 끄덕임
    c.key(1.4, add(look, HEAD=(17, 0, 0)))
    c.key(1.8, add(look, HEAD=(13, 0, 0)))
    out.append(c)

    # 복귀 — 벽에서 몸을 떼고 허리와 어깨를 바로잡는다.
    c = Clip("dis_wolf_wall_recover", 1.9, False)
    c.key(0.0, add(look, HEAD=(13, 0, 0)))
    c.key(0.45, merge(legs(body, 0.03, 8, 2), dict(TORSO=(3, 0, 1), HEAD=(6, 0, 0)), arms(18, 5, 46)))
    c.key(0.9, merge(stand(body, 0.01, TORSO=(-2, 0, 0), HEAD=(0, 0, 0)), arms(12, 4, 22, shrug=4)))
    c.key(1.25, merge(stand(body, TORSO=(1, 0, 0), CHEST=(2, 0, 0), HEAD=(-2, 0, 0)),
                      arms(10, 4, 18, shrug=6)))                        # 어깨를 펴고
    c.key(1.9, stand_m)
    out.append(c)

    # ── WOLF_02 기기 타격 ──
    # 발이 닿는 곳 — 기기 하단(높이 0.30m, 앞 0.52m). 손은 옆면(높이 1.10m, 앞 0.44m).
    KICK_H, KICK_F = 0.30, 0.52
    SLAP_H, SLAP_F = 1.10, 0.44

    c = Clip("dis_wolf_kick_enter", 1.7, False)
    c.key(0.0, work)
    down = merge(stand(body, 0.02, TORSO=(-14, 0, 0), CHEST=(-6, 0, 0), HEAD=(-22, 0, 0)),
                 arms(18, 4, 30))
    c.key(0.45, down)                                                  # 기기를 내려다본다
    c.key(0.85, add(down, HEAD=(2, 26, 0), CHEST=(0, 6, 0)))           # 고개를 옆으로
    fist = merge(stand(body, 0.02, TORSO=(-6, 0, 0), CHEST=(-2, 0, 0), HEAD=(-6, 0, 0)),
                 arms(14, 4, 34, shrug=8, hand=-18))
    c.key(1.25, fist)                                                  # 주먹을 쥔다
    c.key(1.7, add(fist, SHL=(0, 0, -4), SHR=(0, 0, 4), TORSO=(-3, 0, 0)))
    out.append(c)

    # 반복 — 뒤로 뺐다 → 발차기 → 중심 회복 → 손바닥 타격 ×2(박자가 다르다).
    c = Clip("dis_wolf_kick_loop", 3.2, True)
    ready = merge(legs(body, 0.03, 6, 10, out_r=5), dict(
        TORSO=(4, 0, 0), CHEST=(1, 0, 0), HEAD=(-6, 0, 0)), arms(16, 5, 36, shrug=6, hand=-16))
    c.key(0.0, ready)
    c.key(0.22, add(ready, VR=(0, 0, 0.05), TORSO=(7, 0, 0), ULR=(-16, 0, 0)))   # 몸을 뒤로
    # 발차기 — 오른 다리를 들어 앞으로. 발바닥이 기기 하단에 닿는다.
    kick = merge({"ULL": (-4, 0, -4), "ULR": (52, 0, 6), "LLL": (-6, 0, 0), "LLR": (-14, 0, 0),
                  "FL": (2, 0, 0), "FR": (-14, 0, 0), "VR": (0, -0.01, 0.03)},
                 dict(TORSO=(9, 0, 0), CHEST=(3, 0, 0), HEAD=(-10, 0, 0)),
                 arms(10, 10, 30, shrug=4, hand=-16))
    c.key(0.42, kick)                                                  # ← 타격 순간(0.42s)
    c.key(0.56, merge({"ULL": (-2, 0, -4), "ULR": (30, 0, 6), "LLL": (-6, 0, 0), "LLR": (-40, 0, 0),
                       "FL": (2, 0, 0), "FR": (-6, 0, 0), "VR": (0, -0.012, 0.01)},
                      dict(TORSO=(6, 0, 0), HEAD=(-8, 0, 0)), arms(12, 8, 32, hand=-14)))
    c.key(0.85, ready)                                                 # 중심 회복
    # 손바닥 타격 ① — 오른손으로 옆면을 때린다.
    wind = reach(body, merge(stand(body, 0.03, TORSO=(-4, 0, -6), CHEST=(-2, 0, -3), HEAD=(-10, 0, 0)),
                             arms(16, 5, 70, hand=-10)), R=(0.26, SLAP_H + 0.16, -SLAP_F + 0.22), name=c.name)
    c.key(1.25, wind)
    hit1 = reach(body, merge(stand(body, 0.035, TORSO=(-12, 0, 2), CHEST=(-5, 0, 1), HEAD=(-14, 0, 0)),
                             arms(20, 5, 42, hand=4)), R=(0.14, SLAP_H, -SLAP_F), name=c.name)
    c.key(1.45, hit1)                                                  # ← 타격 순간(1.45s)
    c.key(1.62, add(hit1, UAR=(-6, 0, 0), LAR=(8, 0, 0)))
    # 손바닥 타격 ② — 조금 더 짧은 간격으로 한 번 더.
    c.key(1.95, add(wind, UAR=(-6, 0, 0)))
    hit2 = reach(body, merge(stand(body, 0.03, TORSO=(-10, 0, 2), CHEST=(-4, 0, 1), HEAD=(-12, 0, 0)),
                             arms(20, 5, 44, hand=2)), R=(0.17, SLAP_H - 0.08, -SLAP_F + 0.02), name=c.name)
    c.key(2.12, hit2)                                                  # ← 타격 순간(2.12s)
    c.key(2.3, add(hit2, UAR=(-8, 0, 0), LAR=(10, 0, 0)))
    c.key(2.7, add(ready, HEAD=(-4, 0, 0)))
    c.key(3.05, ready)
    out.append(c)

    # 적발 — 즉시 멈추고 천천히 고개를 들어 무표정하게 본 뒤 팔을 내린다.
    c = Clip("dis_wolf_kick_notice", 1.9, False)
    c.key(0.0, hit1)
    c.key(0.14, add(hit1, UAR=(-10, 0, 0), HEAD=(4, 0, 0)))            # 멈춤
    c.key(0.5, merge(stand(body, 0.02, TORSO=(-2, 0, 0), HEAD=(12, 0, 0)), arms(18, 5, 40, hand=-6)))
    blank = merge(stand(body, 0.01, TORSO=(0, 0, 0), CHEST=(1, 0, 0), HEAD=(19, 0, 0)),
                  arms(10, 4, 20))
    c.key(0.9, blank)                                                  # 무표정하게 본다
    c.key(1.4, blank)
    c.key(1.9, merge(stand(body, TORSO=(-1, 0, 0), HEAD=(14, 0, 0)), arms(6, 3, 12)))
    out.append(c)

    # 복귀 — 장갑·소매를 정리하고 작업 자세로.
    c = Clip("dis_wolf_kick_recover", 1.8, False)
    base = merge(stand(body, TORSO=(-3, 0, 0), HEAD=(-10, 0, 0)), arms(24, 6, 70))
    cuff = reach(body, base, L=(-0.09, 1.0, -0.2), R=(0.09, 1.0, -0.22), name=c.name)
    c.key(0.0, merge(stand(body, TORSO=(-1, 0, 0), HEAD=(14, 0, 0)), arms(6, 3, 12)))
    c.key(0.35, cuff)
    c.key(0.6, reach(body, base, L=(-0.14, 0.98, -0.16), R=(0.09, 1.0, -0.22), name=c.name))
    c.key(0.9, reach(body, base, L=(-0.09, 1.0, -0.2), R=(0.15, 0.98, -0.16), name=c.name))
    c.key(1.25, merge(stand(body, TORSO=(-4, 0, 0), HEAD=(-6, 0, 0)), arms(20, 5, 40)))
    c.key(1.8, work)
    out.append(c)
    return out


# ── 강아지 ────────────────────────────────────────────────────────────────
#
# DOG_01 고양이와 즐겁게 통화 — 몸을 좌우로 흔든다. 들키면 멋쩍게 고개를 숙인다.
# DOG_02 다른 설비를 살펴보고 두드린다 — 소리가 난 쪽으로 귀를 기울인다.

def dog_clips(rng):
    out = []
    body = M
    work = merge(stand(body, TORSO=(-6, 0, 0), CHEST=(-3, 0, 0), HEAD=(-9, 0, 0)), arms(40, 5, 60))

    def ear_pose(p, extra_head=(0, 0, 0), name=""):
        q = add(p, HEAD=extra_head)
        pt = tuple(head_point(body, q, (0.135, 0.015, 0.0)))
        return reach(body, q, R=pt, name=name)

    c = Clip("dis_dog_call_enter", 1.8, False)
    c.key(0.0, work)
    c.key(0.25, merge(stand(body, TORSO=(-4, 0, 0), HEAD=(-6, 0, 0)), arms(26, 5, 42)))
    pocket = reach(body, merge(stand(body, 0.015, TORSO=(-2, 0, 2), HEAD=(-6, 0, 0)), arms(10, 5, 14)),
                   R=(0.19, 0.93, -0.06), name=c.name)
    c.key(0.52, pocket)
    screen = reach(body, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(-20, 0, 0)), arms(26, 5, 62)),
                   R=(0.11, 1.14, -0.26), name=c.name)
    c.key(0.9, screen)
    talk = ear_pose(merge(stand(body, 0.01, TORSO=(-2, 0, 0)), arms(44, 10, 104, shrug=8)), name=c.name)
    talk = reach(body, talk, L=(-0.18, 0.94, -0.1), name=c.name)
    c.key(1.35, talk)
    c.key(1.8, talk)
    out.append(c)

    # 반복 — 기분 좋게. 몸을 좌우로 작게 흔들고 고개가 움직인다.
    c = Clip("dis_dog_call_loop", 3.8, True)
    c.key(0.0, talk)
    swayL = ear_pose(merge(legs(body, 0.02, 5, 8, out_r=4), dict(
        HIPS=(0, 0, -3), TORSO=(-2, 0, 3), CHEST=(-1, 0, 2)), arms(44, 10, 104, shrug=8)),
        (4, 0, 0), name=c.name)
    swayL = reach(body, swayL, L=(-0.18, 0.94, -0.1), name=c.name)
    swayR = ear_pose(merge(legs(body, 0.02, 8, 5, out_l=4), dict(
        HIPS=(0, 0, 3), TORSO=(-2, 0, -3), CHEST=(-1, 0, -2)), arms(44, 10, 104, shrug=8)),
        (4, 0, 0), name=c.name)
    swayR = reach(body, swayR, L=(-0.18, 0.94, -0.1), name=c.name)
    c.key(0.5, swayL)
    laugh = add(swayL, CHEST=(6, 0, 0), HEAD=(9, 0, 0), SHL=(0, 0, -9), SHR=(0, 0, 9), VR=(0, 0.01, 0))
    c.key(0.85, laugh)
    c.key(1.05, swayL)
    c.key(1.5, swayR)
    c.key(1.95, add(swayR, HEAD=(6, 0, 0), CHEST=(4, 0, 0)))
    c.key(2.4, talk)
    c.key(2.8, add(swayL, HEAD=(-3, 0, 0)))                            # 듣는 구간
    c.key(3.3, swayR)
    out.append(c)

    # 적발 — 살짝 놀라고 움직임이 멈추고 멋쩍게 고개를 숙인다.
    c = Clip("dis_dog_call_notice", 1.9, False)
    c.key(0.0, swayL)
    jolt = ear_pose(merge(legs(body, -0.008, 4, -2), dict(
        VR=(0, 0.008, 0.015), TORSO=(5, 0, 0), CHEST=(4, 0, 0)), arms(46, 10, 108, shrug=16)),
        (20, 0, 0), name=c.name)
    c.key(0.18, jolt)                                                  # 작게 놀란다
    c.key(0.42, add(jolt, HEAD=(-2, 0, 0), VR=(0, 0, 0.005)))
    c.key(0.7, add(jolt, HEAD=(4, 0, 0)))                              # 멈춰 있다
    shy = ear_pose(merge(stand(body, 0.02, TORSO=(-8, 0, 0), CHEST=(-5, 0, 0)),
                         arms(46, 8, 106, shrug=10)), (-18, 0, 0), name=c.name)
    c.key(1.1, shy)                                                    # 멋쩍게 고개를 숙인다
    c.key(1.45, add(shy, HEAD=(4, 0, 0), CHEST=(2, 0, 0)))             # 통화를 마무리
    c.key(1.9, shy)
    out.append(c)

    c = Clip("dis_dog_call_recover", 1.9, False)
    c.key(0.0, shy)
    off = reach(body, merge(stand(body, 0.01, TORSO=(-4, 0, 0), HEAD=(-14, 0, 0)), arms(30, 6, 76)),
                R=(0.13, 1.16, -0.22), name=c.name)
    c.key(0.5, off)
    c.key(0.85, add(off, HNDR=(-12, 0, 0), HEAD=(-6, 0, 0)))
    pk = reach(body, merge(stand(body, 0.015, TORSO=(-3, 0, 1), HEAD=(-8, 0, 0)), arms(10, 5, 14)),
               R=(0.19, 0.93, -0.05), name=c.name)
    c.key(1.3, pk)                                                     # 주머니(소품 숨김)
    c.key(1.9, work)
    out.append(c)

    # ── DOG_02 다른 설비 조사 ──
    PH, PF = 1.08, 0.42

    # 자리에서 — 손이 멈추고 고개를 기울여 소리가 난 쪽을 듣는다.
    c = Clip("dis_dog_probe_listen", 1.8, False)
    c.key(0.0, work)
    halt = merge(stand(body, 0.01, TORSO=(-4, 0, 0), CHEST=(-2, 0, 0), HEAD=(-4, 0, 0)),
                 arms(24, 5, 42))
    c.key(0.35, halt)
    c.key(0.8, add(halt, HEAD=(6, 30, -10), CHEST=(0, 8, 0)))          # 귀를 기울인다
    c.key(1.25, add(halt, HEAD=(4, -22, 8), CHEST=(0, -6, 0)))
    c.key(1.8, add(halt, HEAD=(2, 10, -4)))
    out.append(c)

    # 도착 — 상체를 숙이며 다가가 옆면을 관찰한다.
    c = Clip("dis_dog_probe_enter", 1.5, False)
    c.key(0.0, merge(stand(body, 0.01, TORSO=(-3, 0, 0), HEAD=(-2, 0, 0)), arms(16, 5, 28)))
    lean = reach(body, merge(legs(body, 0.05, 14), dict(TORSO=(-22, 0, 0), CHEST=(-8, 0, 0), HEAD=(6, 0, 0)),
                             arms(28, 5, 52)), R=(0.13, PH, -PF), name=c.name)
    c.key(0.6, lean)
    c.key(1.1, add(lean, HEAD=(2, 18, 0), CHEST=(0, 5, 0)))
    c.key(1.5, lean)
    out.append(c)

    # 반복 — 손바닥으로 톡톡 · 아래를 들여다봄 · 고개 갸웃 · 옆면을 만져 봄 · 잠깐 생각.
    c = Clip("dis_dog_probe_loop", 4.0, True)
    off2 = reach(body, lean, R=(0.14, PH + 0.06, -PF + 0.06), name=c.name)
    tap = reach(body, lean, R=(0.14, PH, -PF - 0.02), name=c.name)
    c.key(0.0, off2)
    c.key(0.18, tap)
    c.key(0.34, off2)
    c.key(0.52, tap)
    c.key(0.68, off2)
    low = reach(body, merge(legs(body, 0.3, 54), dict(TORSO=(-34, 0, 0), CHEST=(-10, 0, 0), HEAD=(16, 0, 0)),
                            arms(26, 5, 48)), R=(0.14, 0.5, -PF + 0.02), name=c.name)
    c.key(1.3, low)                                                    # 아래쪽을 살핀다
    c.key(1.8, add(low, HEAD=(12, 24, 0), CHEST=(0, 6, 0)))
    c.key(2.3, add(lean, HEAD=(4, -14, 6)))
    side = reach(body, merge(legs(body, 0.05, 14), dict(TORSO=(-20, 0, 8), CHEST=(-8, 0, 3), HEAD=(4, 22, 0)),
                             arms(28, 5, 52)), R=(0.22, PH - 0.12, -PF + 0.06), name=c.name)
    c.key(2.8, side)                                                   # 측면을 만져 본다
    c.key(3.2, add(side, HNDR=(-14, 0, 0)))
    think = merge(stand(body, 0.02, TORSO=(-10, 0, 0), CHEST=(-4, 0, 0), HEAD=(-6, 0, 0)),
                  arms(20, 5, 36))
    c.key(3.6, think)                                                  # 잠깐 멈춰 생각
    c.key(3.85, off2)
    out.append(c)

    # 적발 — 살짝 보고 손을 떼고 곧바로 태연한 자세.
    c = Clip("dis_dog_probe_notice", 1.6, False)
    c.key(0.0, off2)
    c.key(0.14, add(off2, HEAD=(14, 0, 0), CHEST=(3, 0, 0), SHL=(0, 0, -5), SHR=(0, 0, 5)))
    up = merge(stand(body, 0.01, TORSO=(-2, 0, 0), HEAD=(20, 0, 0)), arms(26, 7, 54, shrug=10))
    c.key(0.4, up)                                                     # 손을 뗀다
    c.key(0.7, add(up, HEAD=(14, 0, 0), SHL=(0, 0, -3), SHR=(0, 0, 3)))
    calm = merge(stand(body, TORSO=(-2, 0, 0), HEAD=(6, 0, 0)), arms(10, 4, 18))
    c.key(1.1, calm)                                                   # 태연한 자세 회복
    c.key(1.6, calm)
    out.append(c)

    c = Clip("dis_dog_probe_recover", 1.2, False)
    c.key(0.0, calm)
    c.key(0.5, merge(stand(body, TORSO=(-3, 0, 0), HEAD=(-4, 0, 0)), arms(12, 4, 22)))
    c.key(1.2, merge(stand(body, TORSO=(-2, 0, 0)), arms(8, 4, 14)))
    out.append(c)
    return out


# ── .tres 쓰기 ────────────────────────────────────────────────────────────

def emit_dis(clip):
    """G.emit 에 RigRoot 트랙(여러 키)을 더한다 — 누운 자세 전환용."""
    txt = G.emit(clip)
    rig = getattr(clip, "rig", None)
    if not rig:
        return txt
    n = len(re.findall(r"tracks/(\d+)/type", txt))
    times = ", ".join(fmt(t) for t, _ in rig)
    vals = ", ".join("Vector3(%s, %s, %s)" % tuple(fmt(math.radians(a)) for a in v) for _, v in rig)
    txt = txt.rstrip("\n") + "\n" + "\n".join([
        f'tracks/{n}/type = "value"', f"tracks/{n}/imported = false", f"tracks/{n}/enabled = true",
        f'tracks/{n}/path = NodePath("VisualRoot/RigRoot:rotation")', f"tracks/{n}/interp = 1",
        f"tracks/{n}/loop_wrap = true", f"tracks/{n}/keys = {{",
        '"times": PackedFloat32Array(' + times + "),",
        '"transitions": PackedFloat32Array(' + ", ".join("1" for _ in rig) + "),",
        '"update": 0,', f'"values": [{vals}]', "}"]) + "\n"
    return txt


BODY_OF = dict(fox=M, wolf=M, dog=M, sheep=F, rabbit=F, cat=F)


def measure(body, poses):
    """클립 전체에서 "작업하는 손"의 평균 앞거리·높이(m) — 설비 앞에 세울 거리."""
    fw, hy = [], []
    for p in poses:
        k = fk(body, p)
        l, r = k["palmL"], k["palmR"]
        front = l if -l[2] > -r[2] else r
        fw.append(-front[2])
        hy.append(front[1])
    return (sum(fw) / len(fw), sum(hy) / len(hy)) if fw else (0.0, 0.0)


def write_reach(clips_by_name):
    rows = []
    for name in TOUCH_CLIPS:
        c = clips_by_name.get(name)
        if c is None:
            continue
        body = BODY_OF[name.split("_")[1]]
        f, h = measure(body, [p for _, p in c.keys])
        rows.append((name, f, h))
        print(f"  reach {name:26s} fwd {f:.3f}  h {h:.3f}")
    lines = [
        "// 자동 생성 — tools/gen_distraction_clips.py 가 쓴다. 손으로 고치지 말 것.",
        "using System.Collections.Generic;",
        "",
        "namespace NSP.View;",
        "",
        "// 딴짓 클립마다 \"발 사이 중심에서 설비를 만지는 손까지 앞 거리 · 손 높이\"(m). 표현 전용이다.",
        "// RoomWorkVisualController.Distraction 이 이 값으로 직원을 설비(InteractionTarget)에서",
        "// 알맞은 거리에 세운다 — 두드리는 손이 허공에 뜨거나 기계 속에 들어가지 않게.",
        "public static class DistractionClipReach",
        "{",
        "    private static readonly Dictionary<string, (float Forward, float Height)> _reach = new()",
        "    {",
    ]
    for name, f, h in rows:
        lines.append(f'        ["{name}"] = ({f:.3f}f, {h:.3f}f),')
    lines += [
        "    };",
        "",
        "    public static bool TryGet(string clip, out float forward, out float height)",
        "    {",
        "        forward = height = 0f;",
        "        if (string.IsNullOrEmpty(clip) || !_reach.TryGetValue(clip, out var r)) return false;",
        "        (forward, height) = r;",
        "        return true;",
        "    }",
        "}",
        "",
    ]
    open(REACH_CS, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def build():
    rng = random.Random(70413)
    clips = []
    for f in (fox_clips, sheep_clips, cat_clips, rabbit_clips, wolf_clips, dog_clips):
        clips += f(rng)
    return clips


def main():
    clips = build()
    names = [c.name for c in clips]
    if len(set(names)) != len(names):
        raise SystemExit("클립 이름이 겹친다: " + str([n for n in names if names.count(n) > 1]))

    src = open(LIB, encoding="utf-8").read()
    # 같은 이름의 예전 클립을 지운다(여러 번 돌려도 한 벌만 남게). 다른 클립은 건드리지 않는다.
    for n in names:
        src = re.sub(r'\[sub_resource type="Animation" id="Anim_%s"\]\n.*?\n(?=\[)' % re.escape(n), "",
                     src, flags=re.S)
        src = re.sub(r'&"%s": SubResource\("Anim_%s"\),?\n' % (re.escape(n), re.escape(n)), "", src)

    head, tail = src.split("[resource]", 1)
    head = head.rstrip("\n") + "\n\n" + "\n".join(emit_dis(c) for c in clips) + "\n"

    m = re.search(r"_data = \{\n(.*?)\n\}", tail, re.S)
    entries = [e.strip().rstrip(",") for e in m.group(1).split("\n") if e.strip()]
    entries += [f'&"{n}": SubResource("Anim_{n}")' for n in names]
    entries.sort()
    tail = tail[:m.start()] + "_data = {\n" + ",\n".join(entries) + "\n}" + tail[m.end():]

    open(LIB, "w", encoding="utf-8", newline="\n").write(head + "[resource]" + tail)
    write_reach({c.name: c for c in clips})
    print(f"{len(clips)} clips → {os.path.relpath(LIB, ROOT)}  ·  {os.path.relpath(REACH_CS, ROOT)}")
    for c in clips:
        print(f"  {c.name:28s} {c.length:4.2f}s {'loop' if c.loop else 'once'}  keys={len(c.keys)}")


if __name__ == "__main__":
    main()
