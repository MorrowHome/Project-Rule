"""Import the rabbit delivery without rebuilding other enemies or changing source files."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
from PIL import Image
sys.dont_write_bytecode = True
from import_resources import ROOT, DEST, natural


def import_rabbit(source):
    manifest_path = DEST / 'EnemyArtManifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    manifest['frames'] = [f for f in manifest['frames'] if '/RabbitBoss/' not in f['path']]
    manifest['sequences'] = [s for s in manifest['sequences'] if s['actor'] != 'RabbitBoss']
    groups = {
        'Pop': '兔子冒出/冒出', 'Throw': '兔子扔东西/扔东西',
        'Hide': '兔子缩回/兔子缩', 'Turn': '帽子反转/反转',
        'Flight': '帽子正立飞行/飞行', 'Dead': '死亡/死亡',
        'HatWobble': '礼帽摇晃/礼帽摇晃', 'Walk': '蹦跳前行/前进',
        'Burrow': '蹦跳前行（兔子缩回/前进',
        'ProjectileBall': '投掷物/弹珠', 'ProjectileChecker': '投掷物/跳棋',
        'ProjectileDuck': '投掷物/鸭子',
    }
    paths = {}
    # All body frames share the delivery canvas. A fixed anchor preserves the authored hops.
    # Standing rabbit is 428 px tall; use one PPU for every body action.
    ppu = 428 / 3
    for action, folder in groups.items():
        files = sorted((source / folder).glob('*.png'), key=natural)
        if not files:
            raise ValueError(f'Missing frames: {source / folder}')
        paths[action] = []
        projectile_ppu = None
        for index, original in enumerate(files, 1):
            target = DEST / 'RabbitBoss' / f'RabbitBoss_{action}_{index:02d}.png'
            target.parent.mkdir(parents=True, exist_ok=True)
            with Image.open(original) as image:
                width, height = image.size
                pivot_x, pivot_y, frame_ppu = .5, 1 - (720 if action == 'Dead' else 596) / height, ppu
                if action.startswith('Projectile'):
                    # Supplied toys have an opaque white export canvas. Create a small runtime
                    # derivative with a white matte removed; retain the source unchanged.
                    image = image.convert('RGBA')
                    pixels = []
                    for r, g, b, a in image.getdata():
                        matte = min(r, g, b)
                        alpha = min(a, 255 - matte)
                        if alpha:
                            pixels.append((max(0, round((r - matte) * 255 / alpha)),
                                           max(0, round((g - matte) * 255 / alpha)),
                                           max(0, round((b - matte) * 255 / alpha)), alpha))
                        else:
                            pixels.append((0, 0, 0, 0))
                    image.putdata(pixels)
                    bounds = image.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
                    if bounds is None:
                        raise ValueError(f'Empty projectile: {original}')
                    image = image.crop((max(0, bounds[0]-2), max(0, bounds[1]-2),
                                        min(width, bounds[2]+2), min(height, bounds[3]+2)))
                    image.save(target)
                    width, height = image.size
                    pivot_x = pivot_y = .5
                    if projectile_ppu is None:
                        projectile_ppu = max(width, height) / 1.1
                    frame_ppu = projectile_ppu
                else:
                    shutil.copyfile(original, target)
            asset = str(target.relative_to(ROOT / 'Project-Rule'))
            manifest['frames'].append(dict(path=asset, source=str(original), width=width, height=height,
                                          pivotX=pivot_x, pivotY=pivot_y, ppu=frame_ppu,
                                          sourceSha256=hashlib.sha256(original.read_bytes()).hexdigest()))
            paths[action].append(asset)
    sequences = {**paths, 'Idle': paths['Pop'][-1:], 'Hurt': paths['Pop'][-1:],
                 'Flip': paths['Hide'] + paths['Turn'], 'FlipThrow': paths['Flight'],
                 'HideThrow': paths['Pop'] + paths['Throw'],
                 'Magic': paths['Hide'] + paths['HatWobble'] + paths['Pop']}
    for action, frames in sequences.items():
        manifest['sequences'].append(dict(actor='RabbitBoss', action=action, facesRight=False,
                                          nativeFrameRate=True, frames=frames))
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Rabbit: {sum(len(p) for p in paths.values())} frames, {len(sequences)} sequences at 8 fps')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    import_rabbit(parser.parse_args().source.resolve())
