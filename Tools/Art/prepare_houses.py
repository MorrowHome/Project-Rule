#!/usr/bin/env python3
"""把「本地文档/房子（完成」里的 9 张全画布 PNG 处理成 Unity 用的背景/平台素材。

做三件事：
  1. 按 alpha > 8 裁掉透明边（画布底边不是房子的落地线，必须裁）。
  2. 统一缩到 0.4 —— 配合 PPU 128，世界尺寸 = 原图像素 / 320。
  3. 输出英文文件名 + HouseArtManifest.json（给 Unity 侧的预制体生成器读）。

原图不动；输出写到 Unity 工程里。
"""

import json
import os
from PIL import Image

SRC = r"D:\project\GameJam\本地文档\房子（完成"
OUT = r"D:\project\GameJam\Project-Rule\Assets\_Project\Art\Sprites\Background\Houses"
MANIFEST = r"D:\project\GameJam\Tools\Art\HouseArtManifest.json"

SCALE = 0.4
PPU = 128
ALPHA_THRESHOLD = 8

# 源文件 -> (输出文件名, 用途)
MAPPING = {
    "冒险家协会.png": ("House_AdventurerGuild.png", "background"),
    "水果店.png": ("House_FruitShop.png", "background"),
    "甜品小店.png": ("House_DessertShop.png", "background"),
    "花店_20261018_190315.png": ("House_FlowerShop_Pink.png", "background"),
    "花店sp.png": ("House_FlowerShop_Teal.png", "background"),
    "邻街商铺1.png": ("House_StreetShop_01.png", "background"),
    "邻街商铺2.png": ("House_StreetShop_02.png", "background"),
    "邻街商铺3.png": ("House_StreetShop_03.png", "platform"),
    "首饰店.png": ("House_JewelryShop.png", "background"),
}


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)

    manifest = []
    for src_name, (out_name, role) in MAPPING.items():
        src_path = os.path.join(SRC, src_name)
        if not os.path.exists(src_path):
            raise FileNotFoundError(src_path)

        image = Image.open(src_path).convert("RGBA")
        alpha = image.getchannel("A")
        mask = alpha.point(lambda v: 255 if v > ALPHA_THRESHOLD else 0)
        bbox = mask.getbbox()
        if bbox is None:
            raise ValueError(f"{src_name} 整张都是透明的")

        cropped = image.crop(bbox)
        cropped_w, cropped_h = cropped.size
        target_w = max(1, int(round(cropped_w * SCALE)))
        target_h = max(1, int(round(cropped_h * SCALE)))
        resized = cropped.resize((target_w, target_h), Image.LANCZOS)

        out_path = os.path.join(OUT, out_name)
        resized.save(out_path, "PNG")

        manifest.append({
            "source": src_name,
            "output": out_name,
            "role": role,
            "crop": list(bbox),
            "sourceSize": [cropped_w, cropped_h],
            "outputSize": [target_w, target_h],
            "worldSize": [round(target_w / PPU, 3), round(target_h / PPU, 3)],
            "pivot": [0.5, 0.0],
            "ppu": PPU,
        })
        print(f"{src_name:32s} -> {out_name:32s} {target_w}x{target_h}px  "
              f"{target_w/PPU:.2f}x{target_h/PPU:.2f}u  ({role})")

    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print(f"\nmanifest -> {MANIFEST}")
    print(f"output   -> {OUT}")


if __name__ == "__main__":
    main()
