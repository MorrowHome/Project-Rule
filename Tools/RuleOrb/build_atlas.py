"""交付动作 -> 统一身体比例与脚底锚点的 17 帧图集。从 Unity 项目根目录运行。"""
from pathlib import Path
from PIL import Image
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, default=Path("../Doc/动画合集/主角动画（1s8帧）已添加蹬墙/手托光球"))
parser.add_argument("--output", type=Path, default=Path("Assets/_Project/Art/RuleOrb/RuleOrbAtlas.png"))
args = parser.parse_args()
groups = [("掏出序列", "掏出", 8), ("光球循环", "托光球", 4), ("收回序列", "收回", 5)]
sheet = Image.new("RGBA", (2048, 1920))
index = 0
for folder, prefix, count in groups:
    for number in range(1, count + 1):
        image = Image.open(args.source / folder / f"{prefix}-{number}.png").convert("RGBA")
        if image.size != (1280, 720):
            raise ValueError("源画布尺寸改变，请同时更新裁剪范围和 RuleOrbArt 锚点。")
        # 统一裁剪，不按单帧包围盒缩放；排除画布右下角残留，保留球光和披风。
        image = image.crop((70, 20, 880, 670)).resize((365, 293), Image.Resampling.LANCZOS)
        alpha = image.getchannel("A").point(lambda value: 0 if value < 4 else value)
        image.putalpha(alpha)
        cell = Image.new("RGBA", (512, 384))
        cell.paste(image, (13, 80))
        sheet.paste(cell, (index % 4 * 512, index // 4 * 384))
        index += 1
args.output.parent.mkdir(parents=True, exist_ok=True)
sheet.save(args.output, optimize=True)
print(f"Saved {index} artist frames: {args.output}")
