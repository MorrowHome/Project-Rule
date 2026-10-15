"""Copy supplied enemy PNG sequences and record importer metadata; source artwork stays intact."""
from pathlib import Path
from collections import deque
import json
import re
import shutil
import filecmp
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Doc" / "动画合集"
DEST = ROOT / "Project-Rule" / "Assets" / "_Project" / "Art" / "Enemies"


def natural(path):
    return [int(x) if x.isdigit() else x for x in re.split(r"(\d+)", path.name)]


def component_anchor(path):
    """Largest connected alpha component excludes isolated export marks, without editing pixels."""
    with Image.open(path) as image:
        width, height = image.size
        alpha = image.convert("RGBA").getchannel("A")
        sw = max(1, width // 8)
        sh = max(1, height // 8)
        small = alpha.resize((sw, sh))
        pixels = list(small.getdata())
        visited = bytearray(sw * sh)
        largest = []
        for start, opacity in enumerate(pixels):
            if opacity < 40 or visited[start]:
                continue
            visited[start] = 1
            queue = deque([start])
            group = []
            while queue:
                cell = queue.popleft()
                group.append(cell)
                x, y = cell % sw, cell // sw
                for nx, ny in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
                    if not (0 <= nx < sw and 0 <= ny < sh):
                        continue
                    other = ny * sw + nx
                    if not visited[other] and pixels[other] >= 40:
                        visited[other] = 1
                        queue.append(other)
            if len(group) > len(largest):
                largest = group
        if not largest:
            return width, height, 0.5, 0.0, float(height)
        xs = [v % sw for v in largest]
        ys = [v // sw for v in largest]
        return width, height, (sum(xs) / len(xs) + 0.5) / sw, 1 - (max(ys) + 1) / sh, (max(ys) - min(ys) + 1) * height / sh


def folder(character, leaf):
    choices = [p for p in character.rglob("*") if p.is_dir() and p.name == leaf and list(p.glob("*.png"))]
    if len(choices) != 1:
        raise ValueError(f"Expected one sequence folder: {character.name}/{leaf}, found {len(choices)}")
    files = sorted(choices[0].glob("*.png"), key=natural)
    if not files:
        raise ValueError(f"No frames: {choices[0]}")
    return files


def clock_face(path):
    """Locate the cream dial in the supplied art; only metadata is produced."""
    with Image.open(path) as image:
        width, height = image.size
        sw, sh = max(1, width // 4), max(1, height // 4)
        pixels = list(image.convert('RGBA').resize((sw, sh)).getdata())
        mask = bytearray(a > 100 and r > 150 and g > 140 and b > 80
                         and g - b > 20 and r - g < 55 for r, g, b, a in pixels)
        largest = []
        for start, exists in enumerate(mask):
            if not exists: continue
            mask[start] = 0
            group, queue = [], deque([start])
            while queue:
                cell = queue.popleft(); group.append(cell)
                x, y = cell % sw, cell // sw
                for nx, ny in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
                    if 0 <= nx < sw and 0 <= ny < sh:
                        index = ny * sw + nx
                        if mask[index]: mask[index] = 0; queue.append(index)
            if len(group) > len(largest): largest = group
        if not largest: return None
        xs, ys = [i % sw for i in largest], [i // sw for i in largest]
        return ((min(xs) + max(xs) + 1) * width / sw / 2,
                (min(ys) + max(ys) + 1) * height / sh / 2)


def main():
    brick = next(SOURCE.glob("积木人boss*"))
    clock = next(SOURCE.glob("发条闹钟*"))
    bat = next(SOURCE.glob("蝙蝠boss*"))
    mappings = {
        "BrickBoss": (brick, 3.2, False, {
            "Walk": ["前进"], "Hurt": ["受击序列"], "HammerPlayer": ["无波序列"],
            "HammerGround": ["有波序列"], "Throw": ["扔东西"], "Split": ["分裂"],
            "Angry": ["生气序列"], "Dead": ["死亡分裂"]}),
        "BrickMinion": (brick, 1.0, False, {"Walk": ["小怪前进"], "Dead": ["爆炸（忘命名了"]}),
        "ClockBoss": (clock, 3.0, True, {"Walk": ["行走序列"], "Stun": ["眩晕循环"],
            "Recover": ["发条弹出收回"], "Rewind": ["眩晕循环"], "Charge": ["乱跑起步", "乱跑循环（结束可以直接接静止帧"],
            "JumpShake": ["闹铃序列"], "Dead": ["死亡序列"]}),
        "BatBoss": (bat, 2.8, True, {"Fly": ["飞行序列"], "Hurt": ["受击"], "Slash": ["劈砍序列"],
            "Roar": ["大叫序列"], "WingSpread": ["展翅序列"], "Point": ["前指序列"],
            "Dash": ["冲刺（可循环，懒得拆了，可自拆"], "Shake": ["甩毛循环序列", "回正"],
            "Weak": ["落下", "晕循环", "起飞"], "Dead": ["死亡序列"]})
    }
    manifest = {"frames": [], "sequences": [], "source": "Doc/动画合集", "frameRate": 8}
    written = {}
    for actor, (character, body_height, faces_right, actions) in mappings.items():
        reference = folder(character, "飞行序列" if actor == "BatBoss" else "小怪前进" if actor == "BrickMinion" else "行走序列" if actor == "ClockBoss" else "前进")[0]
        *_, reference_height = component_anchor(reference)
        ppu = reference_height / body_height
        for action, leaves in actions.items():
            sources = []
            for leaf in leaves:
                # Folder names can repeat at the parent level; choose the one directly containing PNGs.
                choices = [p for p in character.rglob("*") if p.is_dir() and p.name == leaf and list(p.glob("*.png"))]
                if len(choices) != 1:
                    raise ValueError(f"Missing or ambiguous {actor}/{action}/{leaf}")
                sources += sorted(choices[0].glob("*.png"), key=natural)
            frames = []
            for source in sources:
                key = (actor, str(source))
                if key not in written:
                    target = DEST / actor / f"{actor}_{len(written):04d}.png"
                    target.parent.mkdir(parents=True, exist_ok=True)
                    if not target.exists() or not filecmp.cmp(source, target, shallow=False):
                        shutil.copyfile(source, target)
                    width, height, x, y, _ = component_anchor(source)
                    face = None
                    if actor == 'ClockBoss':
                        face = clock_face(source)
                        # 同一轴心的 X 对齐，消除动画中画在画布不同位置的横向轨迹。
                        if face: x = face[0] / width
                    asset_path = str(target.relative_to(ROOT / "Project-Rule")).replace("\\", "/")
                    written[key] = asset_path
                    metadata = {"path": asset_path, "source": str(source.relative_to(ROOT)).replace("\\", "/"),
                        "width": width, "height": height, "pivotX": x, "pivotY": y, "ppu": ppu}
                    if face:
                        metadata['clockFaceX'] = face[0] / width
                        metadata['clockFaceY'] = 1 - face[1] / height
                    manifest["frames"].append(metadata)
                frames.append(written[key])
            manifest["sequences"].append({"actor": actor, "action": action, "facesRight": faces_right, "frames": frames})
        moving = next(s for s in manifest["sequences"] if s["actor"] == actor and s["action"] in ("Walk", "Fly"))
        manifest["sequences"].append({**moving, "action": "Idle", "frames": moving["frames"][:1]})
        if not any(s["actor"] == actor and s["action"] == "Hurt" for s in manifest["sequences"]):
            manifest["sequences"].append({**moving, "action": "Hurt", "frames": moving["frames"][:1]})
    DEST.mkdir(parents=True, exist_ok=True)
    # Only minute/hour sprites are supplied. Their circular end is the rotation axle.
    reference = folder(clock, "行走序列")[0]
    *_, clock_height = component_anchor(reference)
    for index, needle in enumerate(("秒针", "分针", "时针")):
        source = next(clock.rglob(needle + ".png"), None)
        if source is None:
            print(f"Missing supplied clock hand: {needle}; builder will derive a thin hand from the supplied hour hand.")
            continue
        target = DEST / "ClockBoss" / f"ClockBoss_Hand_{index}.png"
        if not target.exists() or not filecmp.cmp(source, target, shallow=False):
            shutil.copyfile(source, target)
        width, height, x, y, _ = component_anchor(source)
        with Image.open(source) as image:
            bounds = image.getchannel('A').getbbox()
        x = (bounds[0] + bounds[2]) / (2 * width)
        base_y = bounds[1] + 9 if index == 1 else bounds[3] - 10
        y = 1 - base_y / height
        path = str(target.relative_to(ROOT / "Project-Rule")).replace("\\", "/")
        manifest["frames"].append({"path": path, "source": str(source.relative_to(ROOT)).replace("\\", "/"),
            "width": width, "height": height, "pivotX": x, "pivotY": y, "ppu": clock_height / 3.0})
        manifest["sequences"].append({"actor": "ClockBoss", "action": "Hand" + str(index), "facesRight": True, "frames": [path]})
    manifest_path = DEST / "EnemyArtManifest.json"
    if manifest_path.exists():
        previous = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["frames"] += [f for f in previous["frames"] if "/RabbitBoss/" in f["path"]]
        manifest["sequences"] += [s for s in previous["sequences"] if s["actor"] == "RabbitBoss"]
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Copied {len(manifest['frames'])} frames; {len(manifest['sequences'])} sequences.")


if __name__ == "__main__":
    main()
