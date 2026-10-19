
"""冲撞小方（ChargingCube）三帧：复用炸弹图换色 + 加脚 + 迈步。

母版是交付的 CityBomb_Idle.png（224×224）：只改身体色相，黑描边、渐变、橙眼原样保留，
所以和炸弹是同一套视觉语言。脚是程序画的，是这套图里唯一「新画」的部分。
迈步只转身体、不转脚 —— 脚要贴着地面。
"""
from pathlib import Path
import colorsys
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "Project-Rule/Assets/_Project/Art/Sprites/Components/CityBomb_Idle.png"
DEST = ROOT / "Project-Rule/Assets/_Project/Art/Enemies/ChargingCube"
CANVAS = 224
INK = (26, 26, 30, 255)

BODY_HUE = 280.0
BODY_SAT = 0.62
BODY_SCALE = 0.84
FOOT_W, FOOT_H = 0.185, 0.115
FOOT_SPREAD = 0.185


def recolor(img):
    px = img.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            r, g, b, a = px[x, y]
            if a < 8:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if v < 0.25 or s < 0.35:
                continue
            if 15 < h * 360 < 60:
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(BODY_HUE / 360, BODY_SAT * s, v)
            px[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return img


def compose(step, lean, bounce):
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    foot_w, foot_h = int(CANVAS * FOOT_W), int(CANVAS * FOOT_H)
    body_side = int(CANVAS * BODY_SCALE)
    base_y = 2 + body_side - int(foot_h * 0.42)
    foot_y = base_y + bounce
    for index, x in enumerate((int(CANVAS * 0.5 - CANVAS * FOOT_SPREAD - foot_w / 2),
                               int(CANVAS * 0.5 + CANVAS * FOOT_SPREAD - foot_w / 2))):
        dy = step if index == 0 else -step
        draw.rounded_rectangle([x, foot_y + dy, x + foot_w, foot_y + foot_h + dy],
                               radius=foot_h // 2, fill=INK)

    body = recolor(Image.open(BASE).convert("RGBA")).resize((body_side, body_side), Image.LANCZOS)
    if lean:
        body = body.rotate(-lean, resample=Image.BICUBIC, center=(body_side // 2, body_side))
    canvas.alpha_composite(body, ((CANVAS - body_side) // 2, 2))
    return canvas


DEST.mkdir(parents=True, exist_ok=True)
compose(step=0, lean=0, bounce=0).save(DEST / "ChargingCube_Idle.png", optimize=True)
compose(step=6, lean=2, bounce=0).save(DEST / "ChargingCube_Run_01.png", optimize=True)
compose(step=6, lean=7, bounce=2).save(DEST / "ChargingCube_Run_02.png", optimize=True)
print("saved", DEST)
