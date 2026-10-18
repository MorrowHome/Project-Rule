
# -*- coding: utf-8 -*-
"""生成《三棍支架陷阱》状态草图（纯色占位）。"""
import math, os
from PIL import Image, ImageDraw, ImageFont

FONT = "Project-Rule/Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf"
OUT  = "Doc/三棍支架陷阱_草图.png"

BG=(16,21,28); PANEL=(25,33,47); EDGE=(44,58,78)
INK=(232,237,244); DIM=(146,162,183)
GROUND=(43,52,64); POST=(140,150,168); LEG=(214,142,62); PLANK=(74,144,217)
ENTITY=(228,94,74); ARROW=(245,196,81); OK=(110,200,140); DASH=(96,116,142)

f_h  = ImageFont.truetype(FONT, 31)
f_s  = ImageFont.truetype(FONT, 18)
f_pt = ImageFont.truetype(FONT, 22)
f_lb = ImageFont.truetype(FONT, 18)
f_sm = ImageFont.truetype(FONT, 15)

W,H = 1640, 962
img = Image.new("RGB",(W,H),BG); d = ImageDraw.Draw(img)

d.text((26,20),"三棍支架陷阱（TrestleTrap）— 状态草图", font=f_h, fill=INK)
d.text((26,60),"右腿固定 · 左腿可被「质量 ≥ 阈值」的实体顶倒 · 顶梁绕右柱顶下摆成三角斜坡，并把推动者关在笼内", font=f_s, fill=DIM)

# ---- 图例 ----
lx = 26; ly = 92
def swatch(x, color, label):
    d.rectangle([x, ly, x+16, ly+16], fill=color)
    d.text((x+24, ly-1), label, font=f_sm, fill=DIM)
    return x + 34 + d.textlength(label, font=f_sm)
lx = swatch(lx, POST, "右腿（固定）")
lx = swatch(lx, LEG, "左腿（活动·可倒）")
lx = swatch(lx, PLANK, "顶梁（转下来 = 斜坡）")
lx = swatch(lx, ENTITY, "推动者 / 被困实体")
d.ellipse([lx+2, ly+2, lx+14, ly+14], outline=INK, width=2)
d.text((lx+22, ly-1), "铰链", font=f_sm, fill=DIM)

# ---- 面板 ----
M=24; GAP=20
PW = (W - M*2 - GAP)//2
PH = (H - 128 - M - GAP)//2
PX = [M, M+PW+GAP]
PY = [128, 128+PH+GAP]

WX0, WX1 = 0.9, 7.6
WY0, WY1 = -0.5, 3.35
IN_W, IN_H = PW-32, PH-58
SC = min(IN_W/(WX1-WX0), IN_H/(WY1-WY0))

XL, XR = 2.5, 6.5          # 左腿 / 右腿
HH = 2.2                   # 柱高
LT = 4.0                   # 顶梁长 = 跨距
TH_ROD, TH_PLANK = 0.28, 0.30
LEG_REST = 90.0
PLANK_REST = 26.0          # 梢端下缘落在倒下的左腿上（y = 0.28）

class P:
    def __init__(self, i, title, note):
        self.px, self.py = PX[i%2], PY[i//2]
        self.ox = self.px + 16 + (IN_W - (WX1-WX0)*SC)/2
        self.oy = self.py + 44
        d.rounded_rectangle([self.px, self.py, self.px+PW, self.py+PH], 10, fill=PANEL, outline=EDGE, width=2)
        d.text((self.px+16, self.py+12), title, font=f_pt, fill=INK)
        d.text((self.px+PW-16, self.py+17), note, font=f_sm, fill=DIM, anchor="ra")
    def X(self,wx): return self.ox + (wx-WX0)*SC
    def Y(self,wy): return self.oy + (WY1-wy)*SC
    def pt(self,p): return (self.X(p[0]), self.Y(p[1]))
    def poly(self, pts, fill, outline=None, width=1):
        d.polygon([self.pt(p) for p in pts], fill=fill, outline=outline, width=width)
    def ground(self):
        self.poly([(WX0,0),(WX1,0),(WX1,WY0),(WX0,WY0)], GROUND)
        d.line([self.pt((WX0,0)), self.pt((WX1,0))], fill=(70,84,102), width=3)
        for wx in [x/10 for x in range(int(WX0*10), int(WX1*10)+1, 4)]:
            d.line([self.pt((wx,0)), self.pt((wx-0.16,-0.24))], fill=(58,70,86), width=1)
    def hinge(self, p):
        x,y = self.pt(p); r=7
        d.ellipse([x-r,y-r,x+r,y+r], fill=(20,26,36), outline=INK, width=2)
    def rod(self, pivot, length, thick, ang, color, outline=(12,16,22)):
        a=math.radians(ang); dx,dy=math.sin(a),math.cos(a); px_,py_=-dy,dx
        h=thick/2
        pts=[(pivot[0]+px_*h, pivot[1]+py_*h),
             (pivot[0]+dx*length+px_*h, pivot[1]+dy*length+py_*h),
             (pivot[0]+dx*length-px_*h, pivot[1]+dy*length-py_*h),
             (pivot[0]-px_*h, pivot[1]-py_*h)]
        self.poly(pts, color, outline, 2)
    def plank(self, hinge, length, thick, ang, color, outline=(12,16,22)):
        a=math.radians(ang); dx,dy=-math.cos(a),-math.sin(a); px_,py_=-dy,dx
        h=thick/2
        pts=[(hinge[0]+px_*h, hinge[1]+py_*h),
             (hinge[0]+dx*length+px_*h, hinge[1]+dy*length+py_*h),
             (hinge[0]+dx*length-px_*h, hinge[1]+dy*length-py_*h),
             (hinge[0]-px_*h, hinge[1]-py_*h)]
        self.poly(pts, color, outline, 2)
    def post(self, color, fixed=False):
        self.poly([(XR-0.17,0),(XR+0.17,0),(XR+0.17,HH),(XR-0.17,HH)], color, (12,16,22), 2)
        if fixed:
            for i in range(-3,4):
                x0=XR+0.17+i*0.13
                d.line([self.pt((x0,0)), self.pt((x0-0.18,-0.2))], fill=(120,132,150), width=2)
    def entity(self, cx, w=0.8, h=1.1, color=ENTITY):
        self.poly([(cx-w/2,0),(cx+w/2,0),(cx+w/2,h),(cx-w/2,h)], color, (12,16,22), 2)
    def arrow(self, a, b, color=ARROW, w=4, head=0.26):
        d.line([self.pt(a), self.pt(b)], fill=color, width=w)
        dx,dy = b[0]-a[0], b[1]-a[1]; L=math.hypot(dx,dy); ux,uy=dx/L,dy/L
        px_,py_=-uy,ux
        t=(b[0]-ux*head, b[1]-uy*head)
        d.polygon([self.pt(b), self.pt((t[0]+px_*head*0.5, t[1]+py_*head*0.5)),
                   self.pt((t[0]-px_*head*0.5, t[1]-py_*head*0.5))], fill=color)
    def txt(self, wx, wy, s, color=DIM, font=None, anchor="la"):
        d.text(self.pt((wx,wy)), s, font=font or f_lb, fill=color, anchor=anchor)
    def badge(self, wx, wy, s, color):
        x,y = self.pt((wx,wy)); tw = d.textlength(s, font=f_sm)
        d.rounded_rectangle([x-6,y-4,x+tw+6,y+20], 6, fill=(20,26,36), outline=color, width=2)
        d.text((x,y), s, font=f_sm, fill=color)

# ============ ① 初始 ============
p = P(0,"① 初始 — 三根棍子搭成平台","顶梁可行走 · 左腿是唯一的支点")
p.ground(); p.post(POST, fixed=True)
p.rod((XL,0), HH, TH_ROD, 0, LEG)
p.plank((XR,HH), LT, TH_PLANK, 0, PLANK)
p.hinge((XL,0)); p.hinge((XR,HH))
p.entity(1.5)
p.arrow((1.9,0.55),(2.3,0.55))
p.txt(XL+0.1, -0.42, "底铰链（可转）", anchor="ma", font=f_sm)
p.txt(XR-0.05, -0.42, "固定腿（不动）", anchor="ma", font=f_sm)
p.badge(0.95, 2.75, "顶梁：可站立行走", PLANK)
p.badge(0.95, 2.28, "质量 < 阈值 → 顶不动", DIM)
p.txt(3.1, 1.15, "实体顶上来，", font=f_sm); p.txt(3.1, 0.82, "和撞一堵墙一样", font=f_sm)

# ============ ② 触发 ============
p = P(1,"② 触发 — 质量 ≥ 阈值的实体顶倒左腿","左腿绕底铰链倒向内侧（＝实体前进方向）")
p.ground(); p.post(POST, fixed=True)
p.rod((XL,0), HH, TH_ROD, 30, LEG)
p.plank((XR,HH), LT, TH_PLANK, 0, PLANK)
p.hinge((XL,0)); p.hinge((XR,HH))
p.entity(2.0)
p.arrow((1.3,0.55),(1.8,0.55))
p.badge(0.95, 2.75, "质量 ≥ 阈值 → 顶得动", OK)
p.txt(XL+0.05, -0.42, "铰链转起来", anchor="ma", font=f_sm)
p.txt(2.9, 1.9, "左腿失去直立 →", font=f_sm)
p.txt(2.9, 1.57, "顶梁失去左支点", font=f_sm)
p.txt(6.0, 2.75, "顶梁暂时还是平的", anchor="ma", font=f_sm)

# ============ ③ 下摆 ============
p = P(2,"③ 下摆 — 顶梁绕右柱顶铰链转下来","梢端扫过笼子：把人往里推、把左侧出口封死")
p.ground(); p.post(POST, fixed=True)
p.rod((XL,0), HH, TH_ROD, 72, LEG)
p.plank((XR,HH), LT, TH_PLANK, 16, PLANK)
p.hinge((XL,0)); p.hinge((XR,HH))
# 梢端扫过的弧线
prev=None
for k in range(0,19):
    a=math.radians(16*k/18); t=(XR-LT*math.cos(a), HH-LT*math.sin(a))
    if prev: d.line([p.pt(prev), p.pt(t)], fill=DASH, width=2)
    prev=t
p.entity(5.15)
p.badge(0.95, 2.75, "顶梁下摆中", PLANK)
p.txt(4.0, 1.35, "倒下的左腿把人往前扫", font=f_sm)
p.txt(3.35, 0.35, "梢端轨迹", font=f_sm, color=DASH)

# ============ ④ 成型 ============
p = P(3,"④ 成型 — 三角斜坡 + 推动者被困","三角 = 地面 + 右腿 + 顶梁（斜坡）；倒下的左腿成了门槛")
p.ground(); p.post(POST, fixed=True)
p.rod((XL,0), HH, TH_ROD, LEG_REST, LEG)
p.plank((XR,HH), LT, TH_PLANK, PLANK_REST, PLANK)
p.hinge((XL,0)); p.hinge((XR,HH))
# 三角形区域
tx = XR - LT*math.cos(math.radians(PLANK_REST))
tri = [(tx,0.0),(XR,0.0),(XR,HH)]
ov = Image.new("RGBA",(W,H),(0,0,0,0)); od=ImageDraw.Draw(ov)
od.polygon([p.pt(q) for q in tri], fill=(245,196,81,36))
img.paste(Image.alpha_composite(img.convert("RGBA"), ov).convert("RGB"),(0,0))
d = ImageDraw.Draw(img)
for q in tri: d.ellipse([p.pt(q)[0]-3,p.pt(q)[1]-3,p.pt(q)[0]+3,p.pt(q)[1]+3], fill=ARROW)
p.entity(5.45)
p.badge(0.95, 2.75, "笼内净空够站人 → 关得住", OK)
p.badge(0.95, 2.28, "推动者被关在三角里", ENTITY)
p.txt(2.62, -0.42, "倒下的左腿＝门槛", anchor="ma", font=f_sm, color=LEG)
p.arrow((5.6,2.42),(6.25,2.98), color=OK, head=0.24)
p.txt(4.55, 3.02, "斜坡可走上右柱顶", font=f_sm, color=OK)
p.txt(3.55, 0.40, "关住的实体", font=f_sm, color=ENTITY)

img.save(OUT)
print("saved", OUT, os.path.getsize(OUT))
