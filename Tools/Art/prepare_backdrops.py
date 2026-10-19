#!/usr/bin/env python3
"""把本地交付的背景图整理成 Unity 用的贴图。

源：本地文档/纯色大背景/{出生地,积木}/*.png、本地文档/积木室内背景.png
出：Project-Rule/Assets/_Project/Art/Sprites/Backdrops/{Gradient,Painted}/

规则：原图都是 16:9，只做 Lanczos 缩放，不裁切。
世界尺寸固定 16x9（Gradient PPU 64 / Painted PPU 128），PPU 只决定清晰度。
"""
import json
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "\u672c\u5730\u6587\u6863")
ASSETS = os.path.join(ROOT, "Project-Rule", "Assets", "_Project", "Art", "Sprites", "Backdrops")

JOBS = [
    {
        "source": os.path.join(SOURCE, "\u7eaf\u8272\u5927\u80cc\u666f", "\u51fa\u751f\u5730", "\u51fa\u751f\u5730-4.png"),
        "output": os.path.join(ASSETS, "Gradient", "Start_Backdrop.png"),
        "size": (1024, 576),
        "ppu": 64,
        "note": "出生地/默认背景，暖白渐变",
    },
    {
        "source": os.path.join(SOURCE, "\u7eaf\u8272\u5927\u80cc\u666f", "\u79ef\u6728", "\u79ef\u6728-1.png"),
        "output": os.path.join(ASSETS, "Gradient", "Brick_Backdrop.png"),
        "size": (1024, 576),
        "ppu": 64,
        "note": "积木王国背景，淡紫到米黄",
    },
    {
        "source": os.path.join(SOURCE, "\u79ef\u6728\u5ba4\u5185\u80cc\u666f.png"),
        "output": os.path.join(ASSETS, "Painted", "BrickBoss_Backdrop.png"),
        "size": (2048, 1152),
        "ppu": 128,
        "note": "积木 BOSS 房背景，室内手绘",
    },
]

MANIFEST = os.path.join(ROOT, "Tools", "Art", "BackdropManifest.json")


def main():
    entries = []
    for job in JOBS:
        src = job["source"]
        if not os.path.isfile(src):
            raise SystemExit("缺少源文件: " + src)

        os.makedirs(os.path.dirname(job["output"]), exist_ok=True)
        with Image.open(src) as raw:
            image = raw.convert("RGB")
            width, height = image.size
            if abs(width / height - 16 / 9) > 0.01:
                raise SystemExit("不是 16:9，先裁切再处理: " + src)
            image = image.resize(job["size"], Image.LANCZOS)
            image.save(job["output"], optimize=True)

        entries.append(
            {
                "source": os.path.relpath(src, ROOT).replace("\\", "/"),
                "output": os.path.relpath(job["output"], ROOT).replace("\\", "/"),
                "sourceSize": [width, height],
                "outputSize": list(job["size"]),
                "pixelsPerUnit": job["ppu"],
                "worldSize": [job["size"][0] / job["ppu"], job["size"][1] / job["ppu"]],
                "note": job["note"],
            }
        )
        print("%-28s %s -> %s" % (job["note"], (width, height), job["size"]))

    with open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump({"generatedBy": "Tools/Art/prepare_backdrops.py", "items": entries}, handle,
                  ensure_ascii=False, indent=2)
    print("manifest ->", os.path.relpath(MANIFEST, ROOT))


if __name__ == "__main__":
    main()
