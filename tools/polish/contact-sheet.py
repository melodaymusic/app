"""Build internal QA overview sheets from app-owned UI renders."""
from pathlib import Path
from PIL import Image, ImageDraw
import sys

folder = Path(sys.argv[1])
files = sorted(folder.glob('*.png'))
files = [p for p in files if not p.name.startswith('overview-')]
for start in range(0, len(files), 12):
    group = files[start:start + 12]
    sheet = Image.new('RGB', (1440, 1112), '#dddddd')
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(group):
        x, y = index % 3 * 480, index // 3 * 278
        try:
            with Image.open(path) as original:
                original.thumbnail((474, 248))
                sheet.paste(original.convert('RGB'), (x + 3, y + 24))
        except OSError:
            draw.text((x + 4, y + 36), 'Render still being written', fill='#111111')
        draw.text((x + 4, y + 4), path.stem, fill='#111111')
    sheet.save(folder / f'overview-{start // 12 + 1:02}.jpg')
print(f'{len(files)} renders in {(len(files) + 11) // 12} overview sheets')
