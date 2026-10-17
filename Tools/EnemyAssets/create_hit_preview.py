#!/usr/bin/env python3
"""Package the actual URP validation captures as PR-ready PNG/GIF media.

Run EnemyHitFeedbackValidation.RunBatch in a disposable project first, then:
python3 Tools/EnemyAssets/create_hit_preview.py /tmp/rule-enemy-hit-20261017/Logs/EnemyHitFeedback
"""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
WIDTH = 1200
BACKGROUND = (8, 13, 20)
FOREGROUND = (236, 240, 246)
MUTED = (150, 165, 184)
GOLD = (255, 184, 80)
RED = (255, 122, 96)
CROP = (0, 200, 1440, 590)
TIMES = (0, 30, 60, 80, 100, 120, 150, 180, 210, 220, 240, 270, 300, 330, 360, 390, 420)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "Doc/特效预览")
    parser.add_argument("--font", type=Path, default=Path("/System/Library/Fonts/STHeiti Medium.ttc"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    def font(size):
        return ImageFont.truetype(str(args.font), size)

    def capture(name):
        with Image.open(args.captures / name) as source:
            return source.convert("RGB").crop(CROP).resize((WIDTH, 325), Image.Resampling.LANCZOS)

    before = capture("Before.png")
    after = {ms: capture(f"Hit_{ms:03d}ms.png") for ms in TIMES}

    # Locate the four separated silhouettes for labels, rather than assuming image-space anchors.
    background = before.getpixel((0, 0))
    occupied = []
    pixels = before.load()
    for x in range(WIDTH):
        count = sum(sum(abs(pixels[x, y][c] - background[c]) for c in range(3)) > 25
                    for y in range(before.height))
        if count > 5:
            occupied.append(x)
    groups = []
    for x in occupied:
        if not groups or x - groups[-1][-1] > 10:
            groups.append([x])
        else:
            groups[-1].append(x)
    if len(groups) != 4:
        raise ValueError(f"Expected four separated silhouettes; found {len(groups)}. Check the capture layout.")
    centers = [(group[0] + group[-1]) // 2 for group in groups]

    def centered(draw, text, x, y, size, color):
        selected = font(size)
        bounds = draw.textbbox((0, 0), text, font=selected)
        draw.text((x - (bounds[2] - bounds[0]) // 2, y), text, font=selected, fill=color)

    def header(canvas, subtitle):
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle((30, 29, 37, 76), radius=3, fill=GOLD)
        draw.text((52, 25), "敌人受伤视觉反馈", font=font(32), fill=FOREGROUND)
        draw.text((53, 69), subtitle, font=font(17), fill=MUTED)
        return draw

    def labels(draw, y):
        names = ("积木 Boss", "关卡小怪（占位）", "蝙蝠 Boss", "兔子 Boss")
        kinds = ("普通命中", "普通命中", "破韧命中", "致命一击")
        for index, x in enumerate(centers):
            centered(draw, names[index], x, y, 19, FOREGROUND)
            centered(draw, kinds[index], x, y + 29, 16, GOLD if index < 2 else RED)

    def animated_frame(picture, ms=None):
        canvas = Image.new("RGB", (WIDTH, 590), BACKGROUND)
        draw = header(canvas, "暖白剪影 · 定向火花 · 冲击半环 | URP 实际渲染")
        stamp = "受击前" if ms is None else f"命中 +{ms:03d} ms"
        draw.text((966, 44), stamp, font=font(21), fill=MUTED if ms is None else GOLD)
        canvas.paste(picture, (0, 112))
        labels(draw, 441)
        draw.line((40, 521, 1160, 521), fill=(35, 46, 60), width=3)
        if ms is not None:
            draw.line((40, 521, 40 + round(1120 * min(ms / 420, 1)), 521), fill=GOLD, width=3)
        draw.text((40, 545), "普通闪白 0.30s · 白色保持约 0.165s · 固定姿态展示", font=font(17), fill=MUTED)
        return canvas

    frames = [animated_frame(before)] + [animated_frame(after[ms], ms) for ms in TIMES]
    durations = [850] + [TIMES[i + 1] - ms for i, ms in enumerate(TIMES[:-1])] + [900]
    # One shared palette avoids background/color flicker between GIF frames.
    palette_source = Image.new("RGB", (WIDTH, 590 * 4))
    for index, selected in enumerate((frames[0], frames[1], frames[7], frames[-3])):
        palette_source.paste(selected, (0, 590 * index))
    palette = palette_source.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    gif_path = args.output / "EnemyHitFeedback.gif"
    indexed[0].save(gif_path, save_all=True, append_images=indexed[1:], duration=durations,
                    loop=0, optimize=False, disposal=2)

    comparison = Image.new("RGB", (WIDTH, 955), BACKGROUND)
    draw = header(comparison, "受击前 / 命中 150ms 后 | 延长闪白保持阶段")
    draw.text((40, 120), "受击前", font=font(20), fill=MUTED)
    comparison.paste(before, (0, 147))
    draw.line((40, 481, 1160, 481), fill=(35, 46, 60), width=1)
    draw.text((40, 501), "命中 +150ms：仍保持清晰白色", font=font(20), fill=GOLD)
    comparison.paste(after[150], (0, 529))
    labels(draw, 860)
    draw.text((40, 928), "实际 URP 截图合成；保留原材质、身体比例和现有动画。", font=font(16), fill=MUTED)
    png_path = args.output / "EnemyHitFeedback_Comparison.png"
    comparison.save(png_path, optimize=True)
    for path in (gif_path, png_path):
        print(f"{path}: {path.stat().st_size / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
