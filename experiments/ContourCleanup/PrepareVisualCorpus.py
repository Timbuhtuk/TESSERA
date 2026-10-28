import json
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


NAMES = [
    'dragon', 'spaceship', 'bicycle', 'fish', 'cottage',
    'cactus', 'book', 'key', 'camera', 'cake',
    'owl', 'map', 'hourglass', 'sailboat', 'teapot',
    'beetle', 'guitar', 'wizard-hat', 'rocket', 'strawberry',
    'crown', 'snowman', 'lantern', 'mushroom-house', 'fox',
    'pirate-skull', 'apple', 'knight-helmet', 'balloon', 'cat',
    'compass', 'butterfly', 'train', 'milk-carton', 'bouquet',
    'turtle', 'crystal', 'boxing-glove', 'violin', 'mug',
    'octopus', 'castle', 'watering-can', 'diving-helmet', 'pine',
    'coin', 'parrot', 'ice-cream', 'clock', 'robot-face',
]


def crop_sheet(sheet, position):
    row, col = divmod(position, 5)
    step_x = sheet.width / 5
    step_y = sheet.height / 5
    margin = 5
    box = (round(col * step_x) + margin, round(row * step_y) + margin,
           round((col + 1) * step_x) - margin, round((row + 1) * step_y) - margin)
    return sheet.crop(box).resize((96, 96), Image.Resampling.NEAREST).convert('RGBA')


def noise_locations(clean, seed):
    array = np.asarray(clean)
    rgb = array[:, :, :3]
    linear = np.where(rgb / 255 <= 0.04045, rgb / 255 / 12.92,
                      ((rgb / 255 + 0.055) / 1.055) ** 2.4)
    xyz = linear @ np.array([[0.4124564, 0.2126729, 0.0193339],
                             [0.3575761, 0.7151522, 0.1191920],
                             [0.1804375, 0.0721750, 0.9503041]])
    y_value = xyz[:, :, 1]
    lightness = 116 * np.where(y_value > 0.008856, np.cbrt(y_value), 7.787 * y_value + 16 / 116) - 16
    black = lightness < 5
    candidates = []
    for y in range(5, clean.height - 5):
        for x in range(5, clean.width - 5):
            center = rgb[y, x]
            if lightness[y, x] < 30 or np.min(center) > 235:
                continue
            neighbors = black[y-1:y+2, x-1:x+2]
            dark = int(np.sum(neighbors))
            if 1 <= dark <= 3:
                candidates.append((x, y))
    random.Random(seed).shuffle(candidates)
    chosen = []
    for x, y in candidates:
        if any(abs(x - px) <= 3 and abs(y - py) <= 3 for px, py in chosen):
            continue
        chosen.append((x, y))
        if len(chosen) == 8:
            break
    return chosen


def main():
    root = Path(sys.argv[1])
    for name in ('clean', 'noisy', 'review'):
        (root / name).mkdir(exist_ok=True)
    sheets = [Image.open(root / f'source-{q:02}.png').convert('RGB') for q in (1, 2)]
    manifest = []
    for q, name in enumerate(NAMES):
        image_id = f'{q + 1:02}-{name}'
        clean = crop_sheet(sheets[q // 25], q % 25)
        noisy = clean.copy()
        locations = noise_locations(clean, 20261026 + q * 1009)
        if len(locations) != 8:
            print(image_id, 'only', len(locations), 'noise candidates')
        for index, (x, y) in enumerate(locations):
            noisy.putpixel((x, y), (2, 3, 5, 255) if index % 2 == 0 else (7, 6, 9, 255))
        clean.save(root / 'clean' / f'{image_id}.png')
        noisy.save(root / 'noisy' / f'{image_id}.png')
        manifest.append({'id': image_id, 'motif': name, 'injected': [{'x': x, 'y': y} for x, y in locations]})
    (root / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    for half in range(2):
        sheet = Image.new('RGB', (1000, 1000), 'white')
        draw = ImageDraw.Draw(sheet)
        for position in range(25):
            q = half * 25 + position
            x = position % 5 * 200
            y = position // 5 * 200
            clean = Image.open(root / 'clean' / f'{q+1:02}-{NAMES[q]}.png').convert('RGB')
            sheet.paste(clean, (x + 4, y + 20))
            draw.text((x + 4, y + 4), NAMES[q], fill='black')
        sheet.save(root / 'review' / f'clean-{half+1:02}.png')
    print('images=', len(manifest), 'injected=', sum(len(item['injected']) for item in manifest))


if __name__ == '__main__':
    main()
