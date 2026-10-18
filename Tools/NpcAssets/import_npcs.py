"""Prepare NPC runtime sprites and retain workbook evidence without changing deliveries."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Src/NPC（18，5pm完成'
DEST = ROOT / 'Project-Rule/Assets/_Project/Art/Sprites/Npcs'
GROUPS = [
    ('Merchant', '商人', 2.0), ('Mayor', '镇长', 2.0),
    ('ResidentA', '镇民/镇民-1.png', 1.8), ('ResidentB', '镇民/镇民-2.png', 1.8),
    ('ResidentC', '镇民/镇民-3.png', 1.8), ('ResidentD', '镇民/镇民-4.png', 1.8),
    ('GearKeeper', '齿轮人', 1.6), ('Guard', '士兵', 2.4),
    ('MysteryMerchant', '神秘商人', 1.5),
]

LINES = {
    'Merchant': ('商人', {'Talk': '世界就交给你了，来看看有什么能帮助你的商品吧。', 'Bye': '祝你好运。'}),
    'Mayor': ('镇长', {'Talk1': '孩子，你的能力还不足以挑战魔王，再去四处修炼一下吧。', 'Talk2': '你已经具备打败魔王的能力，去拯救世界吧。'}),
    'GearKeeper': ('齿轮人', {'Talk1': '这个钟表坏了。请先按按钮通电，再把卡住的齿轮拧开。', 'Talk2': '还是无法运作。按钮和齿轮都还需要检查。', 'Talk3': '通上电了，可齿轮还是卡着。', 'Talk4': '齿轮不卡了，但还是没电。', 'Talk5': '谢谢你！我来拧动发条。'}),
    'Guard': ('守卫', {'Talk1': '有入侵者！', 'Talk2': '有通行证，请进。'}),
    'MysteryMerchant': ('神秘商人', {'Talk1': '你还没有资格买我的好货。先去看看小镇商人的商品吧。', 'Talk2': '我有一些真正的好东西，但是要花点价钱哦。', 'Bye': '我回去睡觉了。'}),
    **{f'Resident{letter}': (f'居民{letter}', {'Talk': text}) for letter, text in zip('ABCD', [
        '今天镇上很安静。', '走累了，就在镇上歇一会儿吧。', '出门前去看看商人的货物吧。', '一路小心。'])},
}


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    actors = []
    for actor, folder, height in GROUPS:
        source = SOURCE / folder
        paths = [source] if source.is_file() else sorted(source.glob('*.png'))
        images = [Image.open(p).convert('RGBA') for p in paths]
        boxes = [im.getchannel('A').getbbox() for im in images]
        if not images or any(b is None for b in boxes):
            raise ValueError(f'Missing or empty NPC art: {source}')
        # One union crop for each actor keeps the two delivered poses anchored identically.
        box = (min(b[0] for b in boxes), min(b[1] for b in boxes),
               max(b[2] for b in boxes), max(b[3] for b in boxes))
        frames = []
        for i, (path, im) in enumerate(zip(paths, images), 1):
            frame = im.crop(box)
            frame.thumbnail((512, 512), Image.Resampling.LANCZOS)
            target = DEST / f'{actor}_{i:02d}.png'
            frame.save(target)
            frames.append(dict(path=target.relative_to(ROOT / 'Project-Rule').as_posix(),
                               source=path.relative_to(ROOT).as_posix(),
                               sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                               width=frame.width, height=frame.height, ppu=frame.height / height))
        actors.append(dict(id=actor, height=height, pivot=[0.5, 0], frames=frames,
                           note='静态姿态；两张图可用于交谈切换，不代表完整动作动画'))
    (DEST / 'NpcArtManifest.json').write_text(json.dumps(dict(actors=actors), ensure_ascii=False, indent=2) + '\n')

    workbook = ROOT / 'Src/角色动作需求表（增加NPC需求和行为数据）.xlsb.xlsx'
    ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    with zipfile.ZipFile(workbook) as archive:
        strings = [''.join(n.itertext()) for n in ET.fromstring(archive.read('xl/sharedStrings.xml'))]
        sheets = []
        for number, title in [(2, 'NPC需求表'), (3, 'NPC行为逻辑')]:
            rows = []
            for row in ET.fromstring(archive.read(f'xl/worksheets/sheet{number}.xml')).findall('s:sheetData/s:row', ns):
                cells = {}
                for cell in row:
                    value = cell.find('s:v', ns)
                    if value is not None:
                        cells[cell.get('r')] = strings[int(value.text)] if cell.get('t') == 's' else value.text
                if cells:
                    rows.append(cells)
            sheets.append(dict(name=title, rows=rows))
    evidence = ROOT / 'Doc/References/Npcs'
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / 'NpcRequirements.json').write_text(json.dumps(dict(
        source=workbook.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(workbook.read_bytes()).hexdigest(),
        sheets=sheets), ensure_ascii=False, indent=2) + '\n')
    dialogue = ROOT / 'Project-Rule/Assets/_Project/Data/Npcs'
    dialogue.mkdir(parents=True, exist_ok=True)
    yarn = ['// 按 NPC行为逻辑中的概念示例配置；居民为试播短句，正式文案可直接替换。\n']
    for actor, (name, lines) in LINES.items():
        for state, text in lines.items():
            key = f'{actor}_{state}'
            yarn.append(f'title: {key}\n---\n=> {name}: {text} #line:npc_{key.lower()}\n===\n')
            yarn.append(f'title: {key}Fallback\n---\n{name}: {text} #line:npc_{key.lower()}_fallback\n===\n')
    # Never overwrite authored dialogue when refreshing artwork.
    script = dialogue / 'ConfiguredNpcs.yarn'
    if not script.exists():
        script.write_text('\n'.join(yarn), encoding='utf-8')
    project = dialogue / 'ConfiguredNpcs.yarnproject'
    if not project.exists():
        project.write_text(json.dumps(dict(projectFileVersion=3, sourceFiles=['ConfiguredNpcs.yarn'],
                                          excludeFiles=[], localisation={}, baseLanguage='zh-CN'), indent=2) + '\n')
    print(f'Prepared {len(actors)} NPCs, {sum(len(a["frames"]) for a in actors)} poses; sources unchanged.')


if __name__ == '__main__':
    main()
