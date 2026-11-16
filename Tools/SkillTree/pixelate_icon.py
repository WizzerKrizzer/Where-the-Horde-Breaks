"""Turns a picture drawn by Gemini into real pixel art (user, 2026-11-12: "it looks too AI").

Gemini's "pixel art" has pixels of uneven size, soft edges, gradients and dozens of shades. This
snaps it to a 32x32 grid and a small palette shared with the other icons, so every pixel is the
same size and each material has 3 clear shades:
  1. clear the grey background (flooded from the edges, as import_gemini_icons.py does);
  2. map every source pixel to the nearest palette colour, then give each grid cell the colour
     most of its pixels have (a vote, so outlines and highlights survive, nothing is blended);
  3. clean up: small specks of one colour inside another area take that area's colour (the brown
     "eyes" in Gemini's dust), and the outline is redrawn one pixel around the whole shape.
Glowing yellow cracks become dark cracks in the earth (a crack in the ground is not a light).

Usage: python Tools/SkillTree/pixelate_icon.py <source> <target.png> [grid] [preview.png]
"""
import sys
from collections import Counter, deque

import numpy as np
from PIL import Image

OUTLINE = (26, 22, 18)
PALETTE = {
    'outline': OUTLINE,
    'wood_hi': (240, 196, 92),
    'wood': (204, 146, 40),
    'wood_lo': (146, 96, 28),
    'wrap_hi': (96, 82, 58),
    'wrap': (58, 48, 34),
    'flint_hi': (240, 236, 226),
    'flint': (196, 194, 188),
    'flint_lo': (138, 136, 132),
    'dust_hi': (234, 218, 186),
    'dust': (200, 178, 138),
    'dust_lo': (152, 128, 94),
    'earth': (104, 64, 36),
    'earth_lo': (70, 42, 24),
    'crack': (38, 24, 14),
}
NAMES = list(PALETTE)
COLOURS = np.array([PALETTE[n] for n in NAMES], float)


def background_mask(rgb):
    h, w, _ = rgb.shape
    sat = rgb.max(2) - rgb.min(2)
    lum = rgb.mean(2)
    backgroundish = (sat < 16) & (lum > 100)
    reached = np.zeros((h, w), bool)
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if backgroundish[y, x]:
                reached[y, x] = True
                queue.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if backgroundish[y, x]:
                reached[y, x] = True
                queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and not reached[yy, xx] and backgroundish[yy, xx]:
                reached[yy, xx] = True
                queue.append((yy, xx))
    return ~reached


def nearest(rgb):
    flat = rgb.reshape(-1, 3)
    d = ((flat[:, None, :] - COLOURS[None, :, :]) ** 2).sum(2)
    return d.argmin(1).reshape(rgb.shape[:2])


def main():
    source, target = sys.argv[1], sys.argv[2]
    grid = int(sys.argv[3]) if len(sys.argv) > 3 else 32
    preview = sys.argv[4] if len(sys.argv) > 4 else None
    rgb = np.asarray(Image.open(source).convert('RGB')).astype(float)
    mask = background_mask(rgb)
    ys, xs = np.where(mask)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    side = max(y1 - y0, x1 - x0) + 1
    cy, cx = (y0 + y1) / 2.0, (x0 + x1) / 2.0
    top, left = int(round(cy - side / 2.0)), int(round(cx - side / 2.0))

    labels = nearest(rgb)
    # Glowing yellow-orange cracks: saturated, bright, low in blue, in the lower part (the earth),
    # where the golden shaft is not.
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    glow = (r > 200) & (b < 70) & (r - b > 150)
    rows = np.arange(rgb.shape[0])[:, None]
    earth_rows = rows > (y0 + (y1 - y0) * 0.72)
    labels[glow & earth_rows & mask] = NAMES.index('crack')

    cell = side / grid
    cells = -np.ones((grid, grid), int)
    for gy in range(grid):
        for gx in range(grid):
            sy0, sy1 = int(top + gy * cell), int(top + (gy + 1) * cell)
            sx0, sx1 = int(left + gx * cell), int(left + (gx + 1) * cell)
            sy0, sx0 = max(sy0, 0), max(sx0, 0)
            block = mask[sy0:sy1, sx0:sx1]
            if block.size == 0 or block.mean() < 0.5:
                continue
            votes = Counter(labels[sy0:sy1, sx0:sx1][block].ravel().tolist())
            # Thin dark lines (outlines, cracks) count three times, so they survive the vote.
            for thin in ('outline', 'crack'):
                votes[NAMES.index(thin)] *= 3
            cells[gy, gx] = votes.most_common(1)[0][0]

    # Gemini shades its dust with the same golden tones as the shaft. The shaft is the biggest
    # connected run of wood; every other bit of wood is dust in the same shade.
    wood = {NAMES.index(n): NAMES.index(d) for n, d in (('wood_hi', 'dust_hi'), ('wood', 'dust'), ('wood_lo', 'dust_lo'))}
    components = []
    taken = np.zeros_like(cells, bool)
    for gy in range(grid):
        for gx in range(grid):
            if cells[gy, gx] in wood and not taken[gy, gx]:
                part, queue = [], deque([(gy, gx)])
                taken[gy, gx] = True
                while queue:
                    y, x = queue.popleft()
                    part.append((y, x))
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        yy, xx = y + dy, x + dx
                        if 0 <= yy < grid and 0 <= xx < grid and not taken[yy, xx] and cells[yy, xx] in wood:
                            taken[yy, xx] = True
                            queue.append((yy, xx))
                components.append(part)
    components.sort(key=len, reverse=True)
    for part in components[1:]:
        for y, x in part:
            cells[y, x] = wood[cells[y, x]]

    # Small specks (up to 12 cells) of one colour wholly inside dust become dust (the brown "eyes").
    # Repeated, so a speck of two colours goes once its first colour has.
    for _ in range(3):
        seen = np.zeros_like(cells, bool)
        for gy in range(grid):
            for gx in range(grid):
                if cells[gy, gx] < 0 or seen[gy, gx]:
                    continue
                colour = cells[gy, gx]
                region, border = [], Counter()
                queue = deque([(gy, gx)])
                seen[gy, gx] = True
                while queue:
                    y, x = queue.popleft()
                    region.append((y, x))
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        yy, xx = y + dy, x + dx
                        if not (0 <= yy < grid and 0 <= xx < grid):
                            border[-1] += 1
                        elif cells[yy, xx] == colour:
                            if not seen[yy, xx]:
                                seen[yy, xx] = True
                                queue.append((yy, xx))
                        else:
                            border[cells[yy, xx]] += 1
                name = NAMES[colour]
                if len(region) <= 20 and name not in ('outline', 'crack', 'flint_hi') and not name.startswith('dust') and border:
                    surround, count = border.most_common(1)[0]
                    if surround >= 0 and count >= sum(border.values()) * 0.55 and NAMES[surround].startswith('dust'):
                        for y, x in region:
                            cells[y, x] = surround

    # Outlines between things: where one thing is in front of another (the flint before the shaft
    # before the dust before the earth), the one behind gets a dark edge, as a pixel artist draws it.
    group = {'flint_hi': 4, 'flint': 4, 'flint_lo': 4, 'wood_hi': 3, 'wood': 3, 'wood_lo': 3, 'wrap_hi': 3, 'wrap': 3,
             'dust_hi': 2, 'dust': 2, 'dust_lo': 2, 'earth': 1, 'earth_lo': 1, 'crack': 1}
    edged = cells.copy()
    for gy in range(grid):
        for gx in range(grid):
            if cells[gy, gx] < 0 or NAMES[cells[gy, gx]] not in group:
                continue
            mine = group[NAMES[cells[gy, gx]]]
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                yy, xx = gy + dy, gx + dx
                if 0 <= yy < grid and 0 <= xx < grid and cells[yy, xx] >= 0:
                    other = NAMES[cells[yy, xx]]
                    if other in group and group[other] > mine and not (mine == 1 and group[other] == 2):
                        edged[gy, gx] = NAMES.index('outline')
                        break
    cells = edged

    # The outline: one pixel of it round the whole shape.
    outlined = cells.copy()
    for gy in range(grid):
        for gx in range(grid):
            if cells[gy, gx] >= 0:
                continue
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                yy, xx = gy + dy, gx + dx
                if 0 <= yy < grid and 0 <= xx < grid and cells[yy, xx] >= 0 and NAMES[cells[yy, xx]] != 'outline':
                    outlined[gy, gx] = NAMES.index('outline')
                    break

    image = np.zeros((grid, grid, 4), np.uint8)
    for gy in range(grid):
        for gx in range(grid):
            if outlined[gy, gx] >= 0:
                image[gy, gx] = (*PALETTE[NAMES[outlined[gy, gx]]], 255)
    result = Image.fromarray(image, 'RGBA')
    result.save(target)
    if preview:
        big = result.resize((grid * 8, grid * 8), Image.NEAREST)
        sheet = Image.new('RGBA', (grid * 8 + 120, grid * 8 + 20), (0, 0, 0, 255))
        sheet.paste(big, (10, 10), big)
        small = result.resize((76, 76), Image.NEAREST)
        sheet.paste(small, (grid * 8 + 30, 10), small)
        sheet.save(preview)


if __name__ == '__main__':
    main()
