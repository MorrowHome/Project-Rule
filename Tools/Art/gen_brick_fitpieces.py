
# -*- coding: utf-8 -*-
"""积木风格的拼装件与凹位石板（320px，程序生成，色板取 Tiles/Regions/Brick*）。
   统一语言：平涂 + 深色描边 + 底边一道暗面 + 顶边一道亮棱。"""
import math, os
from PIL import Image, ImageDraw

S = 4
N = 320
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                   "Project-Rule", "Assets", "_Project", "Art", "Sprites", "Brick")
os.makedirs(OUT, exist_ok=True)

RED = (248, 82, 70);     RED_D = (208, 60, 51);   RED_L = (255, 128, 114);  RED_O = (108, 26, 22)
BLUE = (71, 195, 253);   BLUE_D = (48, 162, 222); BLUE_L = (170, 230, 255); BLUE_O = (18, 70, 102)
YELLOW = (253, 218, 92); YELLOW_D = (226, 186, 66); YELLOW_L = (255, 242, 180); YELLOW_O = (126, 96, 20)
GREEN = (73, 187, 40);   GREEN_D = (54, 156, 28); GREEN_L = (156, 226, 124); GREEN_O = (26, 76, 10)

HOLE = (46, 33, 28)
HOLE_EDGE = (12, 8, 7)


def canvas():
    im = Image.new("RGBA", (N * S, N * S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def save(im, name):
    im.resize((N, N), Image.LANCZOS).save(os.path.join(OUT, name))
    print(name)


def inset(pts, d):
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    out = []
    for x, y in pts:
        vx, vy = x - cx, y - cy
        L = math.hypot(vx, vy) or 1.0
        out.append((x - vx / L * d, y - vy / L * d))
    return out


# ---------------------------------------------------------------- 三角件
im, d = canvas()
apex, base_y = 12 * S, (N - 12) * S
half = (N // 2 - 12) * S
tri = [(N * S // 2, apex), (N * S // 2 - half, base_y), (N * S // 2 + half, base_y)]

d.polygon(tri, fill=RED)
inner = inset(tri, 12 * S)
# 底边暗面（厚度）
d.polygon(inset(tri, 2 * S)[:1] + [inner[1], inner[2]], fill=RED_D)
d.line([inner[1], inner[0]], fill=RED_L, width=9 * S)   # 左棱受光
d.line([inner[0], inner[2]], fill=RED_D, width=5 * S)   # 右棱背光
d.line(tri + [tri[0]], fill=RED_O, width=9 * S, joint="curve")
save(im, "BrickTriangle.png")

# ---------------------------------------------------------------- 圆球
im, d = canvas()
c, r = N * S // 2, N * S // 2 - 12 * S
d.ellipse([c - r, c - r, c + r, c + r], fill=BLUE)
# 下半暗面：一条圆弧带
d.chord([c - r, c - r, c + r, c + r], 20, 160, fill=BLUE_D)
d.ellipse([c - r, c - r, c + r, c + r], fill=None, outline=BLUE_O, width=10 * S)
# 小高光
hr = int(r * 0.30)
hx, hy = c - int(r * 0.42), c - int(r * 0.46)
d.ellipse([hx - hr, hy - hr, hx + hr, hy + hr], fill=BLUE_L)
d.arc([c - r + 6 * S, c - r + 6 * S, c + r - 6 * S, c + r - 6 * S], 190, 350, fill=BLUE_L, width=7 * S)
save(im, "BrickBall.png")


# ---------------------------------------------------------------- 槽位（淡白半透明，就是件的外形）
def socket(kind):
    """淡白色玻璃感的凹槽：只有形状 + 一圈亮边，不做深色空腔。"""
    mask = Image.new("L", (N * S, N * S), 0)
    d = ImageDraw.Draw(mask)

    if kind == "tri":
        pts = [(N * S // 2, 12 * S), (N * S // 2 - (N // 2 - 12) * S, (N - 12) * S),
               (N * S // 2 + (N // 2 - 12) * S, (N - 12) * S)]
        d.polygon(pts, fill=76)                                   # 本体：很淡的白
        d.line(pts + [pts[0]], fill=214, width=9 * S, joint="curve")   # 外圈：亮边
        inner = inset(pts, 16 * S)
        d.line(inner + [inner[0]], fill=126, width=7 * S, joint="curve")  # 内圈：稍亮一圈
        d.line([inner[1], inner[2]], fill=150, width=5 * S)       # 下沿再亮一点
    else:
        c = N * S // 2
        r = N * S // 2 - 12 * S
        d.ellipse([c - r, c - r, c + r, c + r], fill=76)
        d.ellipse([c - r, c - r, c + r, c + r], outline=214, width=9 * S)
        rr = r - 16 * S
        d.ellipse([c - rr, c - rr, c + rr, c + rr], outline=126, width=7 * S)
        d.arc([c - rr, c - rr, c + rr, c + rr], 20, 160, fill=158, width=6 * S)

    im = Image.new("RGBA", (N * S, N * S), (255, 255, 255, 255))
    im.putalpha(mask)
    return im


save(socket("tri"), "BrickSocketTriangle.png")
save(socket("round"), "BrickSocketRound.png")