# -*- coding: utf-8 -*-
"""초상화 PNG에서 '흰 배경 제거' 때 같이 지워진 몸통 안쪽을 흰색으로 되메운다.

배경을 흰색으로 지우면 **옷·피부의 흰 부분까지 같이 투명해진다.** 강아지 초상
(portraits/dog_portrait.png)은 그래서 오른쪽 어깨 안쪽이 통째로 뚫려 있었다 —
그 자리가 어두운 카드 배경이 그대로 비치는 검은 구멍으로 보인다(1013 QA 8번).

지우면 안 되는 바깥 배경과 되메워야 하는 안쪽은 두 가지로 가른다.

① **투명 덩어리 검사** — 투명 픽셀을 연결 덩어리로 묶고, 화면 **왼·오른·위** 테두리에
   닿지 않는 덩어리는 그림 안쪽이다. 흉상 초상은 몸이 아래 테두리까지 내려오므로
   어깨 구멍이 아래로 빠져나가는 일이 있는데, 그래도 좌우·위로는 못 빠져나간다.
   바깥 배경은 반드시 좌우나 위에 닿는다.
② **사방 광선 검사** — ①에 걸리지 않는 작은 구멍(선 사이 틈)을 메운다. 어떤 투명
   픽셀에서 상·하·좌·우로 광선을 쏴 네 방향 모두 불투명에 닿으면 안쪽이다.

실루엣(머리카락·귀 바깥선)은 둘 다 건드리지 않는다.

사용:
    py -3 tools/fix_portrait_holes.py --dry  assets/characters/portraits/*.png
    py -3 tools/fix_portrait_holes.py        assets/characters/portraits/dog2.png
"""
import sys
from collections import deque

from PIL import Image

ALPHA_SOLID = 24      # 이 값 이상이면 '그림이 있다'
FILL = (255, 255, 255)


def enclosed_blobs(alpha, w, h):
    """위 테두리에 닿지 않고 아래쪽 절반에서 시작하는 투명 덩어리(= 몸통 구멍)."""
    px = alpha.load()
    seen = bytearray(w * h)
    out = []
    for sy in range(h):
        for sx in range(w):
            if seen[sy * w + sx] or px[sx, sy] >= ALPHA_SOLID:
                continue
            q = deque([(sx, sy)])
            seen[sy * w + sx] = 1
            blob = []
            escapes = False
            top = h
            while q:
                x, y = q.popleft()
                blob.append((x, y))
                # 바깥 배경은 흉상 초상에서 **반드시 위 테두리에 닿는다**(머리 좌우 위가
                # 비어 있으므로). 위에 닿지 않는 덩어리는 그림에 둘러싸인 구멍이다 —
                # 몸이 아래 테두리까지 내려오므로 구멍이 아래·옆으로 빠져나가기도 한다.
                if y == 0:
                    escapes = True
                if y < top:
                    top = y
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] \
                            and px[nx, ny] < ALPHA_SOLID:
                        seen[ny * w + nx] = 1
                        q.append((nx, ny))
            # 머리 옆 빈 공간처럼 위쪽에서 시작하는 덩어리는 배경이다 — 아래 절반에서
            # 시작한 것만 몸통 구멍으로 본다.
            if not escapes and top > h * 0.45:
                out.extend(blob)
    return out


def interior_holes(alpha, w, h):
    """사방 광선 검사로 '그림 안쪽의 투명 픽셀' 좌표를 모은다."""
    px = alpha.load()

    # 행/열마다 불투명 픽셀의 최소·최대 위치를 미리 구해 두면 광선 검사가 O(1)이 된다.
    row_lo = [w] * h
    row_hi = [-1] * h
    col_lo = [h] * w
    col_hi = [-1] * w
    for y in range(h):
        for x in range(w):
            if px[x, y] >= ALPHA_SOLID:
                if x < row_lo[y]:
                    row_lo[y] = x
                row_hi[y] = x
                if y < col_lo[x]:
                    col_lo[x] = y
                col_hi[x] = y

    out = []
    for y in range(h):
        if row_hi[y] < 0:
            continue
        for x in range(row_lo[y] + 1, row_hi[y]):
            if px[x, y] >= ALPHA_SOLID:
                continue
            if col_lo[x] < y < col_hi[x]:
                out.append((x, y))
    return out


def fix(path, dry):
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    a = im.split()[3]
    holes = sorted(set(enclosed_blobs(a, w, h)) | set(interior_holes(a, w, h)))
    if not holes:
        print('%-52s 구멍 없음' % path)
        return False
    xs = [p[0] for p in holes]
    ys = [p[1] for p in holes]
    print('%-52s 구멍 %6d px  box=(%d,%d)-(%d,%d)%s'
          % (path, len(holes), min(xs), min(ys), max(xs), max(ys), '  [미적용]' if dry else ''))
    if dry:
        return True
    px = im.load()
    for x, y in holes:
        px[x, y] = (FILL[0], FILL[1], FILL[2], 255)
    im.save(path)
    return True


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if a != '--dry']
    dry = '--dry' in sys.argv
    for p in args:
        fix(p, dry)
