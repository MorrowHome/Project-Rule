"""Import the delivered rotating block and clock laser sequences without rebuilding body animation."""
from pathlib import Path
import hashlib
import json
import math
import shutil
from PIL import Image
from import_resources import ROOT, SOURCE, DEST, folder


def import_effects():
    manifest_path = DEST / 'EnemyArtManifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    actions = {'ProjectileCube', 'BeamThin', 'BeamMedium', 'BeamThick'}
    previous = {f['path'] for s in manifest['sequences'] if s['action'] in actions for f in s['frames']}
    manifest['frames'] = [f for f in manifest['frames'] if f['path'] not in previous]
    manifest['sequences'] = [s for s in manifest['sequences'] if s['action'] not in actions]
    brick = next(SOURCE.glob('积木人boss*'))
    clock = next(SOURCE.glob('发条闹钟*'))
    groups = [('BrickBoss', 'ProjectileCube', folder(brick, '奇怪的旋转方块'))]
    groups += [('ClockBoss', action, folder(clock, leaf)) for action, leaf in
               [('BeamThin', '细激光序列'), ('BeamMedium', '中激光序列'), ('BeamThick', '粗激光序')]]
    for actor, action, sources in groups:
        paths = []
        bounds = [Image.open(p).convert('RGBA').getchannel('A').getbbox() for p in sources]
        # One fixed canvas anchor/PPU preserves the artist's four-frame tumbling motion.
        x0, y0 = min(b[0] for b in bounds), min(b[1] for b in bounds)
        x1, y1 = max(b[2] for b in bounds), max(b[3] for b in bounds)
        for i, source in enumerate(sources, 1):
            target = DEST / actor / f'{actor}_{action}_{i:02d}.png'
            with Image.open(source) as original:
                image = original.convert('RGBA')
                width, height = image.size
                pivot_x, pivot_y = (x0 + x1) / (2 * width), 1 - (y0 + y1) / (2 * height)
                ppu = max(x1 - x0, y1 - y0)
                if action.startswith('Beam'):
                    # The delivered laser points diagonally up-right. Rotate its long axis to +X
                    # so the runtime can fit the artwork to the authoritative beam segment.
                    # Use the full-length fourth frame as a stable reference for the entire clip.
                    with Image.open(sources[3]) as reference:
                        alpha = reference.convert('RGBA').getchannel('A')
                        points = [(x, y) for y in range(0, alpha.height, 4)
                                  for x in range(0, alpha.width, 4) if alpha.getpixel((x, y)) > 64]
                    mx = sum(x for x, _ in points) / len(points)
                    my = sum(y for _, y in points) / len(points)
                    xx = sum((x-mx)**2 for x, _ in points)
                    yy = sum((y-my)**2 for _, y in points)
                    xy = sum((x-mx)*(y-my) for x, y in points)
                    angle = math.degrees(.5 * math.atan2(2*xy, xx-yy))
                    image = image.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
                    box = image.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
                    image = image.crop(box)
                    width, height = image.size
                    pivot_x, pivot_y, ppu = 0, .5, height
                    image.save(target)
                elif not target.exists() or target.read_bytes() != source.read_bytes():
                    shutil.copyfile(source, target)
            path = target.relative_to(ROOT / 'Project-Rule').as_posix()
            manifest['frames'].append(dict(path=path, source=source.relative_to(ROOT).as_posix(),
                width=width, height=height, pivotX=pivot_x, pivotY=pivot_y, ppu=ppu,
                sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest()))
            paths.append(path)
        manifest['sequences'].append(dict(actor=actor, action=action, facesRight=True,
                                          nativeFrameRate=True, frames=paths))
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Imported 4 rotating block frames and 21 clock laser frames; body sequences preserved.')


if __name__ == '__main__':
    import_effects()
