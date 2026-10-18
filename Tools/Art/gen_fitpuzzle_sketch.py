
# -*- coding: utf-8 -*-
"""Doc/拼装缺口_效果图.png —— 白盒侧视 + 规格面板（2000x1250）。"""
import math, os
from PIL import Image, ImageDraw, ImageFont

SS = 2
W, H = 2000, 1250

BG      = (13, 20, 28)
CARD    = (19, 28, 38)
ROOM    = (16, 24, 33)
PANEL2  = (24, 35, 46)
LINE    = (40, 56, 72)
WALL    = (52, 68, 84)
WALL_HI = (88, 108, 126)
ROCK    = (118, 138, 158)
ROCK_HI = (186, 204, 220)
DARK    = (8, 12, 18)
TXT     = (230, 238, 246)
MUTED   = (140, 158, 176)
AMBER   = (246, 173, 85)
CYAN    = (79, 209, 197)
VIOLET  = (170, 132, 242)
RED     = (232, 92, 92)
GREEN   = (72, 187, 120)
BODY    = (208, 220, 232)

FR = "C:/Windows/Fonts/msyh.ttc"
FB = "C:/Windows/Fonts/msyhbd.ttc"
_c = {}
def font(size, bold=False):
    k = (round(size * SS), bold)
    if k not in _c: _c[k] = ImageFont.truetype(FB if bold else FR, k[0])
    return _c[k]

img = Image.new("RGB", (W * SS, H * SS), BG)
d = ImageDraw.Draw(img)

def sc(v): return v * SS
def line(a, b, color, w=1.5):
    d.line([sc(a[0]), sc(a[1]), sc(b[0]), sc(b[1])], fill=color, width=max(1, int(w * SS)))
def rect(x0, y0, x1, y1, fill=None, outline=None, w=1.4):
    x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1)
    d.rectangle([sc(x0), sc(y0), sc(x1), sc(y1)], fill=fill, outline=outline, width=max(1, int(w * SS)))
def rrect(x0, y0, x1, y1, r, fill=None, outline=None, w=1.2):
    x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1)
    d.rounded_rectangle([sc(x0), sc(y0), sc(x1), sc(y1)], radius=sc(r), fill=fill,
                        outline=outline, width=max(1, int(w * SS)))
def poly(pts, fill=None, outline=None, w=1.6):
    d.polygon([(sc(x), sc(y)) for x, y in pts], fill=fill)
    if outline:
        for i in range(len(pts)): line(pts[i], pts[(i + 1) % len(pts)], outline, w)
def circ(cx, cy, r, fill=None, outline=None, w=1.6):
    d.ellipse([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], fill=fill, outline=outline,
              width=max(1, int(w * SS)))
def arc(cx, cy, r, a0, a1, color, w=1.8):
    d.arc([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], a0, a1, fill=color, width=max(1, int(w * SS)))
def text(x, y, s, size=16, color=TXT, anchor="la", bold=False):
    d.text((sc(x), sc(y)), s, font=font(size, bold), fill=color, anchor=anchor)
def tw(s, size, bold=False):
    bb = d.textbbox((0, 0), s, font=font(size, bold)); return (bb[2] - bb[0]) / SS, (bb[3] - bb[1]) / SS
def dashed(a, b, color, w=1.4, dash=8.0, gap=6.0):
    ax, ay = a; bx, by = b
    L = math.hypot(bx - ax, by - ay)
    if L < 1: return
    ux, uy = (bx - ax) / L, (by - ay) / L
    t = 0.0
    while t < L:
        t2 = min(t + dash, L)
        line((ax + ux * t, ay + uy * t), (ax + ux * t2, ay + uy * t2), color, w)
        t += dash + gap
def dashed_poly(pts, color, w=1.4, dash=7.0, gap=5.0):
    for i in range(len(pts)):
        dashed(pts[i], pts[(i + 1) % len(pts)], color, w, dash, gap)
def dashed_circle(cx, cy, r, color, w=1.4, dash=7.0, gap=5.0):
    n = max(10, int(2 * math.pi * r / max(1, (dash + gap))))
    step = 2 * math.pi / n
    for i in range(n):
        a0 = i * step; a1 = a0 + step * (dash / (dash + gap))
        d.arc([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], math.degrees(a0), math.degrees(a1),
              fill=color, width=max(1, int(w * SS)))
def arrow(a, b, color, w=2.0, head=9, dash=None):
    ax, ay = a; bx, by = b
    dx, dy = bx - ax, by - ay; L = math.hypot(dx, dy)
    if L < head * 1.5: return
    ux, uy = dx / L, dy / L
    if dash: dashed(a, (bx - ux * head * 0.9, by - uy * head * 0.9), color, w, dash, dash * 0.62)
    else: line((ax, ay), (bx - ux * head * 0.9, by - uy * head * 0.9), color, w)
    poly([(bx, by), (bx - ux * head - uy * head * 0.42, by - uy * head + ux * head * 0.42),
          (bx - ux * head + uy * head * 0.42, by - uy * head - ux * head * 0.42)], fill=color)
def chip(cx, cy, s, color, size=14, padx=9):
    w, h = tw(s, size, True); ww, hh = w + padx * 2, h + 11
    rrect(cx - ww / 2, cy - hh / 2, cx + ww / 2, cy + hh / 2, 6, fill=(22, 32, 43), outline=color, w=1.2)
    text(cx, cy, s, size, color, anchor="mm", bold=True)

class Frame:
    def __init__(self, x, y, w, h, wx0, wy0, ww, wh):
        self.x, self.y, self.w, self.h = x, y, w, h
        self.s = min(w / ww, h / wh)
        self.ox = x + (w - ww * self.s) / 2
        self.oy = y + (h - wh * self.s) / 2
        self.wtop, self.wx0 = wy0 + wh, wx0
    def __call__(self, wx, wy):
        return (self.ox + (wx - self.wx0) * self.s, self.oy + (self.wtop - wy) * self.s)

# ------------------------------------------------------------------ 世界
GX0, GX1, GTOP, GRISE = 0.8, 2.4, 3.2, 2.0
PK = [(1.05, 0.0), (2.40, 0.0), (1.72, 1.55)]
CEIL, SLAB = 5.2, 6.4
SX, DR, BR = 8.4, 0.80, 0.60
PIT0, PIT1, PITB = 7.6, 9.2, -1.42
RA, RB = (10.9, 0.70), (9.2, 0.06)
WALLX = 12.0
SIGN = (3.25, 3.95, 4.55, 4.75)

def tri(cx, cy, hw=0.80, h=1.55):
    return [(cx - hw, cy), (cx + hw, cy), (cx, cy + h)]

def stone(pts, f, fill=ROCK, edge=ROCK_HI):
    poly([f(x, y) for x, y in pts], fill=fill, outline=edge, w=1.8)

def ball(f, x, y, r=BR, gap=False):
    px, py = f(x, y); rp = r * f.s
    if gap:
        circ(px, py, rp + 3, fill=(6, 10, 15))
    circ(px, py, rp, fill=ROCK, outline=ROCK_HI, w=1.8)
    arc(px, py, rp * 0.62, 200, 330, (196, 212, 226), 1.4)
    line((px - rp * 0.55, py + rp * 0.55), (px + rp * 0.2, py + rp * 0.2), (86, 102, 118), 1.2)

def player(f, x, y, upside=False):
    s = f.s; hw, hh = 0.25 * s, 0.60 * s; px, py = f(x, y)
    if upside:
        body = (px - hw, py, px + hw, py + hh); head = (px, py + hh + 0.19 * s)
    else:
        body = (px - hw, py - hh, px + hw, py); head = (px, py - hh - 0.19 * s)
    rrect(body[0], body[1], body[2], body[3], 0.11 * s, fill=BODY, outline=(120, 140, 160), w=1.2)
    circ(head[0], head[1], 0.19 * s, fill=BODY, outline=(120, 140, 160), w=1.2)

def lamps(f, on_t, on_b):
    x0, y0, x1, y1 = SIGN
    p0, p1 = f(x0, y0), f(x1, y1)
    rrect(p0[0], p1[1], p1[0], p0[1], 5, fill=(20, 29, 39), outline=(60, 78, 96), w=1.2)
    r = 0.15 * f.s
    for kind, on, cx in (("tri", on_t, 0.34), ("ball", on_b, 0.66)):
        px, py = f(x0 + (x1 - x0) * cx, (y0 + y1) / 2)
        col = GREEN if on else (52, 66, 80)
        if kind == "tri":
            poly([(px, py - r), (px - r, py + r * 0.85), (px + r, py + r * 0.85)], fill=col)
        else:
            circ(px, py, r * 0.9, fill=col)

def room(f, rise=0.0, filled_t=False, filled_b=False):
    p = f(0, 0); q = f(12.6, SLAB)
    rect(p[0], p[1], q[0], q[1], fill=ROOM)
    # 出口通道
    a, b = f(0, 0), f(GX0, GTOP)
    rect(a[0], b[1], b[0], a[1], fill=DARK)
    # 地面 + 地基
    line(f(0, 0), f(PIT0, 0), WALL_HI, 3.2); line(f(PIT1, 0), f(WALLX, 0), WALL_HI, 3.2)
    a, b = f(0, 0), f(12.6, -0.32); rect(a[0], b[1], b[0], a[1], fill=(26, 36, 47))
    # 天花板 + 闸门槽
    a, b = f(0, CEIL), f(12.6, SLAB); rect(a[0], b[1], b[0], a[1], fill=(35, 47, 61))
    line(f(0, CEIL), f(GX0, CEIL), WALL_HI, 3.2); line(f(GX1, CEIL), f(12.6, CEIL), WALL_HI, 3.2)
    if rise > 0.01:
        a, b = f(GX0, 5.18), f(GX1, SLAB - 0.02); rect(a[0], b[1], b[0], a[1], fill=DARK)
        line(f(GX0, 5.18), f(GX0, SLAB), (70, 88, 106), 1.2)
        line(f(GX1, 5.18), f(GX1, SLAB), (70, 88, 106), 1.2)
    # 闸门
    gy = rise
    a, b = f(GX0, gy), f(GX1, gy + GTOP)
    rect(a[0], b[1], b[0], a[1], fill=WALL, outline=WALL_HI, w=1.6)
    # 三角凹位
    pts = [f(x, y + gy) for x, y in PK]
    poly(pts, fill=DARK)
    if not filled_t:
        dashed_poly([f(x * 1.0, y + gy) for x, y in PK], (74, 62, 44), 1.3, 6, 5)
    else:
        stone([(x, y + gy) for x, y in PK], f)
        for i in range(len(pts)): line(pts[i], pts[(i + 1) % len(pts)], (70, 88, 106), 1.2)
    # 圆窝
    cx, cy = f(SX, CEIL); r = DR * f.s
    d.pieslice([cx - r, cy - r, cx + r, cy + r], 180, 360, fill=(5, 8, 12))
    arc(SX, CEIL, DR, 180, 360, (132, 154, 176), 2.4)
    arc(SX, CEIL, DR - 0.10, 184, 356, (70, 88, 106), 1.0)
    if not filled_b:
        dashed_circle(cx, cy + (DR - BR) * f.s, BR * f.s, (74, 62, 44), 1.3, 6, 5)
        arc(SX, CEIL, 0.28, 180, 360, (52, 66, 82), 1.0)
    # 竖井
    a, b = f(PIT0, PITB), f(PIT1, 0)
    rect(a[0], b[1], b[0], a[1], fill=(12, 18, 25))
    line(f(PIT0, -0.32), f(PIT0, PITB), (70, 88, 106), 1.6)
    line(f(PIT1, -0.32), f(PIT1, PITB), (70, 88, 106), 1.6)
    poly([f(PIT0, PITB), f(SX, PITB - 0.16), f(PIT1, PITB)], fill=None, outline=WALL_HI, w=2.0)
    # 斜坡
    a, b = f(*RA), f(*RB)
    dx, dy = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dy)
    nx, ny = -dy / L, dx / L; th = 0.16 * f.s
    poly([(a[0], a[1]), (b[0], b[1]), (b[0] + nx * th, b[1] + ny * th), (a[0] + nx * th, a[1] + ny * th)],
         fill=(34, 46, 58), outline=(76, 94, 112), w=1.4)
    # 右墙
    a, b = f(WALLX, 0), f(12.6, SLAB); rect(a[0], b[1], b[0], a[1], fill=WALL, outline=WALL_HI, w=1.4)

def ceiling_line(f):
    line(f(0, CEIL), f(SX - DR, CEIL), WALL_HI, 3.2)
    line(f(SX + DR, CEIL), f(12.6, CEIL), WALL_HI, 3.2)

# ------------------------------------------------------------------ 页眉
text(40, 24, "拼装缺口 · 重块与凹位", 32, TXT, bold=True)
text(40, 70, "4 件套：三角块 / 圆球 / 三角凹位 / 圆窝 —— 常规形态推不动，只能靠规则改变世界", 16, MUTED)
line((40, 102), (1960, 102), (34, 48, 62), 1.4)

FW, FH = 560, 332
CAP = [
    "两块都在场上：三角块在地面，球在井底。推 60 N 纹丝不动；凹位里虚线的形状就是要填的那一件。",
    "「质量 × 失效」把 180 kg 压到 9 kg（静摩擦 44 N < 60 N），推进凹位。摩擦 × 失效也推得动，只是慢一倍多。",
    "球在井底：推得动，但举不起来。「重力 × 反转」把它直着顶进天花板圆窝，落位即锁死。",
    "两块都入位 → 闸门被抬起，三角跟着门一起升到门楣上，通道空了。",
]
TITLE = ["① 初始：两块都推不动", "② 三角块：质量 × 失效 → 推进凹位",
         "③ 圆球：井底举不起来 → 重力 × 反转顶进圆窝", "④ 完成：两灯亮 → 闸门升起"]
pos = [(40, 128), (640, 128), (40, 128 + FH + 98), (640, 128 + FH + 98)]
frames = []
for i, (x, y) in enumerate(pos):
    f = Frame(x + 8, y + 34, FW - 16, FH, 0, -1.55, 12.6, 8.05)
    frames.append(f)
    rrect(x, y, x + FW, y + FH + 92, 9, fill=CARD, outline=LINE, w=1.1)
    text(x + 14, y + 9, TITLE[i], 16, TXT, bold=True)
    # 说明文字（自动折行）
    s = CAP[i]; ln = ""; lines = []
    for ch in s:
        if (tw(ln + ch, 12.5)[0] > FW - 30):
            lines.append(ln); ln = ch
        else: ln += ch
    lines.append(ln)
    for k, L in enumerate(lines[:2]):
        text(x + 14, y + FH + 44 + k * 19, L, 12.5, MUTED)

# ------------------------------------------------------------------ ①
f = frames[0]
room(f)
stone(tri(4.5, 0), f)
ball(f, SX, -0.80)
player(f, 6.2, 0)
lamps(f, False, False)
text(*f(4.5, 1.75), "▲ 180 kg", 11, AMBER, anchor="ma")
text(*f(9.55, 0.52), "● 260 kg", 11, AMBER, anchor="la")
arrow((f(5.95, 0.95), ), f(5.4, 0.95), RED, 1.6, 7) if False else None
text(*f(6.05, 1.85), "推 60 N", 10.5, RED, anchor="la")
text(*f(6.05, 1.42), "纹丝不动", 10.5, RED, anchor="la")
line(f(6.22, 0.95), f(5.55, 0.95), RED, 1.4)
line(f(5.55, 0.80), f(5.75, 0.95), RED, 1.4)
line(f(5.55, 1.10), f(5.75, 0.95), RED, 1.4)
text(*f(1.72, 1.85), "三角凹位", 10.5, (120, 138, 156), anchor="ma")
text(*f(9.55, 4.35), "圆窝（天花板）", 10.5, (120, 138, 156), anchor="la")
arrow(f(9.55, 4.55), f(8.95, 5.05), (96, 116, 136), 0.9, 5)

# ------------------------------------------------------------------ ②
f = frames[1]
room(f, filled_t=True)
dashed_poly([f(x, y) for x, y in tri(4.5, 0)], (92, 106, 122), 1.2, 6, 5)
ball(f, SX, -0.80)
player(f, 5.65, 0)
lamps(f, True, False)
arrow(f(5.3, 0.62), f(2.62, 0.62), AMBER, 1.8, 8, dash=8)
chip(*f(3.95, 2.45), "质量 × 失效", AMBER, 14)
text(*f(3.95, 1.85), "180 → 9 kg", 11, (200, 212, 226), anchor="ma")
text(*f(3.55, 1.32), "μmg = 44 N", 10.5, MUTED, anchor="ma")

# ------------------------------------------------------------------ ③
f = frames[2]
room(f, filled_t=True, filled_b=True)
ball(f, SX, -0.80)

text(*f(9.35, 1.15), "推得动，举不起来", 10.5, MUTED, anchor="la")
arrow(f(SX, 0.05), f(SX, 4.50), VIOLET, 2.2, 9)
chip(*f(5.75, 3.95), "重力 × 反转", VIOLET, 14)
text(*f(8.72, 4.28), "直上 5.2 m", 10.5, VIOLET, anchor="la")
text(*f(5.75, 3.42), "到位即锁死", 10.5, (200, 212, 226), anchor="ma")
player(f, 10.9, CEIL, upside=True)
text(*f(10.9, 4.55), "玩家也倒挂", 10.5, MUTED, anchor="ma")
lamps(f, True, True)

# ------------------------------------------------------------------ ④
f = frames[3]
room(f, rise=GRISE, filled_t=True, filled_b=True)
ball(f, SX, CEIL + (DR - BR), gap=True)
ceiling_line(f)
player(f, 1.15, 0)
lamps(f, True, True)
arrow(f(1.05, 2.55), f(0.12, 2.55), GREEN, 1.8, 8)
text(*f(0.16, 2.18), "出口", 11, GREEN, anchor="la")
text(*f(3.55, 2.65), "通道空了", 11, (200, 212, 226), anchor="la")
arrow(f(3.6, 2.55), f(2.55, 2.15), (110, 130, 150), 0.9, 5)

# ------------------------------------------------------------------ 规格面板
PX = 1232
rrect(PX, 122, 1962, 1150, 10, fill=CARD, outline=LINE, w=1.2)
text(PX + 26, 142, "组件规格", 18, CYAN, bold=True)
line((PX + 26, 172), (1936, 172), LINE, 1.0)
rows = [
    ("▲ 三角块　FitBlock", "180 kg · 1.6 × 1.55 m · Dynamic · 冻结旋转", AMBER),
    ("● 圆球　FitBlock", "260 kg · Ø 1.2 m · Dynamic · 允许滚动", AMBER),
    ("▲ 三角凹位　FitSocket", "方形石板 2.4 m + 三角空缺 · 容差 ±0.28 m", CYAN),
    ("● 圆窝　FitSocket", "方形石板 2.0 m + 圆空缺 · 正对竖井", CYAN),
]
yy = 188
for t, sub, col in rows:
    rrect(PX + 24, yy, 1936, yy + 58, 7, fill=PANEL2, outline=(34, 48, 62), w=1.0)
    rect(PX + 24, yy, PX + 30, yy + 58, fill=col)
    text(PX + 42, yy + 8, t, 15, TXT, bold=True)
    text(PX + 42, yy + 32, sub, 12.5, MUTED)
    yy += 66

yy = 468
text(PX + 26, yy, "判定「拼上」= 三条同时成立", 18, CYAN, bold=True)
line((PX + 26, yy + 30), (1936, yy + 30), LINE, 1.0)
yy += 42
for a, b in [("形状对", "凹位只认自己的形状 ID，圆窝不收三角"),
             ("位置对", "块心进入座位容差盒 ±0.25 m"),
             ("已经停", "线速度 < 1.0 m/s 连续 0.12 s，防高速穿过误判")]:
    chip(PX + 58, yy + 8, a, GREEN, 12.5)
    text(PX + 104, yy, b, 12.5, (206, 218, 230))
    yy += 28
yy += 2
text(PX + 26, yy, "→ 成立即 snap：Kinematic + 吸附座位锚点 + 不再吃推力", 12.5, GREEN)
yy += 40

text(PX + 26, yy, "实现约束（踩了就废）", 18, CYAN, bold=True)
line((PX + 26, yy + 30), (1936, yy + 30), LINE, 1.0)
yy += 42
for c, col in [
    ("块必须是 Dynamic —— 只有它吃 Physics2D.gravity 与 Rigidbody2D.mass", AMBER),
    ("换成 Kinematic / Static，「重力 × 反转」「质量 × 失效」直接对它失效", MUTED),
    ("必须挂 RuleAffectedBody —— 质量规则靠它把 mass 写进去", TXT),
    ("必须放 Ground(8) 层 —— 玩家 Slide 只认这一层，否则穿过去", TXT),
    ("凹位判定用容差盒 + 静止计时，不靠碰撞体硬卡（会抖、会被顶出去）", TXT),
]:
    text(PX + 30, yy, "· " + c, 12.5, col if col != TXT else (206, 218, 230))
    yy += 25
yy += 12

text(PX + 26, yy, "顺序被机制自己锁死（不用写死判定）", 18, CYAN, bold=True)
line((PX + 26, yy + 30), (1936, yy + 30), LINE, 1.0)
yy += 40
for c in ["先拼三角：入位即 Kinematic，后面翻重力它不会飞",
          "若先翻重力：180 kg 的三角会被顶到天花板，球也进不了井",
          "于是「先横后竖」是玩家自己推出来的，不是提示给的"]:
    text(PX + 30, yy, "· " + c, 12.5, (206, 218, 230)); yy += 25
yy += 12

text(PX + 26, yy, "风险 / 待定", 18, CYAN, bold=True)
line((PX + 26, yy + 30), (1936, yy + 30), LINE, 1.0)
yy += 40
for c in ["玩家站在井口会挡住球 —— 保留，这是好玩的涌现",
          "球必须先在井底停稳，否则反转重力会斜着飞出井口",
          "两块参数（质量 / 尺寸）需在测试场景里实测再定"]:
    text(PX + 30, yy, "· " + c, 12.5, (206, 218, 230)); yy += 25

text(40, 1010, "读图说明", 14, CYAN, bold=True)
for i, s in enumerate([
    "灰白=石料 / 近黑=凹位空腔 / 虚线=该填的形状（也是缺件提示）",
    "①→④ 是同一间房间的四个时刻，不是四个房间",
    "画的是机制示意，尺寸只保证比例可信，不是最终关卡布局",
]):
    text(40, 1036 + i * 21, s, 11.5, MUTED)

img = img.resize((W, H), Image.LANCZOS)
out = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "Doc", "拼装缺口_效果图.png")
img.save(out); print(out)
