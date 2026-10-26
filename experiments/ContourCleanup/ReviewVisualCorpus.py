import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw


root = Path(sys.argv[1])
manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))
output = root / 'review-after'
output.mkdir(exist_ok=True)

for half in range(2):
    sheet = Image.new('RGB', (1500, 1100), 'white')
    draw = ImageDraw.Draw(sheet)
    for position in range(25):
        item = manifest[half * 25 + position]
        image_id = item['id']
        x = position % 5 * 300
        y = position // 5 * 220
        draw.text((x + 3, y + 3), image_id, fill='black')
        for column, directory in enumerate(('clean', 'noisy', 'processed-noisy')):
            image = Image.open(root / directory / (image_id + '.png')).convert('RGB')
            sheet.paste(image, (x + column * 100 + 2, y + 20))
            draw.text((x + column * 100 + 2, y + 120), ('clean', 'noise', 'Tessera')[column], fill='black')
    path = output / f'overview-{half + 1:02}.png'
    sheet.save(path)
    print(path)
