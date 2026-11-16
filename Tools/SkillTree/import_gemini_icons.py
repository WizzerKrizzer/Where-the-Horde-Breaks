"""Skill tree icons drawn by Gemini (user, 2026-11-12: "Gemini's are better than yours").

Reads the pictures in Art/UI/SkillTree/Gemini/<key>.jpg|png (1024x1024, pixel art on a painted
grey checkerboard or plain grey instead of real transparency), clears the background by flooding it
from the edges (the outlined shapes stop the flood, so grey flint inside them stays), or keys out a
bright green background with soft edges (the low poly icons), crops to the drawing,
and writes 128x128 PNGs with transparency (or, for the keys in PIXELATED, 32x32 pixel art) to Assets/Resources/Gemini/SkillTreeIcons/<key>.png.
SkillTreePixelIcons uses them before its own 16x16 drawings. A key "crit" also gets "crit_plus"
and "crit_up" with the corner badges of the Kill Essence upgrades.

Both folders are git-ignored: Gemini images stay local for now (user, 2026-11-11).
Run: python Tools/SkillTree/import_gemini_icons.py
"""
import os
import sys
from collections import deque

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, 'Art', 'UI', 'SkillTree', 'Gemini')
TARGET = os.path.join(ROOT, 'Assets', 'Resources', 'Gemini', 'SkillTreeIcons')
SIZE = 128
# Keys that also get badge variants (the special's two Kill Essence upgrades).
BADGED = {'crit': ('plus', 'up'), 'rage': ('up', 'fast'), 'armor': ('up', 'fast'), 'riders': ('plus', 'up'),
          'stun': ('up', 'time'), 'ricochet': ('plus', 'up'), 'shrapnel': ('plus', 'up'),
          'poison': ('up', 'time'), 'pierce': ('plus', 'up'), 'biggame': ('up',), 'spear': ('up',)}
# Keys snapped to real 32x32 pixel art by pixelate_icon.py (user, 2026-11-12: "it looks too AI").
PIXELATED = set()
OUTLINE = (26, 22, 18, 255)
GREEN = (166, 227, 140, 255)
YELLOW = (240, 200, 70, 255)
WHITE = (240, 236, 224, 255)


def clear_background(rgb):
    """Alpha mask: False where the flood from the edges reaches the background. A plain flat grey
    (almost all of the border one colour, the low poly icons) is matched tightly, so light grey
    flint touching it stays; a painted checkerboard is matched loosely, as any light grey."""
    h, w, _ = rgb.shape
    sat = rgb.max(2) - rgb.min(2)
    lum = rgb.mean(2)
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    plain = np.median(border, axis=0)
    if (np.abs(border - plain).max(1) <= 6).mean() > 0.9:
        backgroundish = np.abs(rgb - plain).max(2) <= 7
    else:
        backgroundish = (sat < 16) & (lum > 100)
    reached = np.zeros((h, w), bool)
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if backgroundish[y, x] and not reached[y, x]:
                reached[y, x] = True
                queue.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if backgroundish[y, x] and not reached[y, x]:
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


def badge(image, kind):
    """The corner badge, as on the drawn icons: a dark plate, a green plus or arrow."""
    s = SIZE / 16.0
    draw = ImageDraw.Draw(image)
    draw.rectangle([9 * s, 9 * s, 16 * s - 1, 16 * s - 1], fill=OUTLINE)
    if kind == 'plus':
        draw.rectangle([10 * s, 12 * s, 15 * s - 1, 13 * s - 1], fill=GREEN)
        draw.rectangle([12 * s, 10 * s, 13 * s - 1, 15 * s - 1], fill=GREEN)
    elif kind == 'time':
        draw.ellipse([9.6 * s, 9.6 * s, 15.4 * s, 15.4 * s], outline=WHITE, width=int(s))
        draw.line([(12.5 * s, 11 * s), (12.5 * s, 12.6 * s), (13.8 * s, 12.6 * s)], fill=WHITE, width=int(s))
    elif kind == 'fast':
        for x0 in (10, 12):
            draw.line([(x0 * s + s / 2, 10 * s + s / 2), ((x0 + 2) * s + s / 2, 12 * s + s / 2), (x0 * s + s / 2, 14 * s + s / 2)], fill=YELLOW, width=int(s))
    else:
        draw.polygon([(12.5 * s, 9.6 * s), (15.2 * s, 13 * s), (9.8 * s, 13 * s)], fill=GREEN)
        draw.rectangle([11 * s, 13 * s, 14 * s - 1, 15.6 * s], fill=GREEN)
    return image


def drop_specks(mask, smallest=600):
    """Leaves only the drawing: tiny islands the flood left behind (bits of noise in the
    background, often in the corners) are cleared."""
    h, w = mask.shape
    seen = np.zeros_like(mask)
    for y in range(h):
        for x in range(w):
            if not mask[y, x] or seen[y, x]:
                continue
            island, queue = [], deque([(y, x)])
            seen[y, x] = True
            while queue:
                cy, cx = queue.popleft()
                island.append((cy, cx))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    yy, xx = cy + dy, cx + dx
                    if 0 <= yy < h and 0 <= xx < w and mask[yy, xx] and not seen[yy, xx]:
                        seen[yy, xx] = True
                        queue.append((yy, xx))
            if len(island) < smallest:
                for cy, cx in island:
                    mask[cy, cx] = False
    return mask


def green_screen(rgb):
    """True when the picture is on a bright green background (the low poly icons, user
    2026-11-13), judged by its border: most of it green (the drawing may run off one corner)."""
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    green = (border[:, 1] > 150) & (border[:, 1] - np.maximum(border[:, 0], border[:, 2]) > 80)
    return green.mean() > 0.6


def key_green(rgb):
    """Soft alpha for a picture on green: how much greener than red and blue a pixel is says how
    much of it is background; the green spilled onto the edges is taken back out."""
    r, g, b = rgb[..., 0].astype(float), rgb[..., 1].astype(float), rgb[..., 2].astype(float)
    excess = g - np.maximum(r, b)
    alpha = np.clip(1.0 - (excess - 30.0) / 70.0, 0.0, 1.0)
    cleaned = rgb.astype(float)
    cleaned[..., 1] = np.minimum(g, np.maximum(r, b) + 12.0)
    return cleaned.clip(0, 255).astype(np.uint8), alpha


def convert(path):
    rgb = np.asarray(Image.open(path).convert('RGB')).astype(int)
    if green_screen(rgb):
        colours, alpha = key_green(rgb)
        # Small: the clean green keeps little pieces (pebbles, dotted arcs) apart from noise.
        mask = drop_specks(alpha > 0.5, 120)
        ys, xs = np.where(mask)
        pad = 8
        y0, y1 = max(ys.min() - pad, 0), min(ys.max() + pad, rgb.shape[0] - 1)
        x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad, rgb.shape[1] - 1)
        side = max(y1 - y0, x1 - x0) + 1
        rgba = np.zeros((side, side, 4), np.uint8)
        oy = (side - (y1 - y0 + 1)) // 2
        ox = (side - (x1 - x0 + 1)) // 2
        keep = alpha * (mask | (alpha > 0.0))
        rgba[oy:oy + y1 - y0 + 1, ox:ox + x1 - x0 + 1, :3] = colours[y0:y1 + 1, x0:x1 + 1]
        rgba[oy:oy + y1 - y0 + 1, ox:ox + x1 - x0 + 1, 3] = (keep[y0:y1 + 1, x0:x1 + 1] * 255).astype(np.uint8)
        return Image.fromarray(rgba, 'RGBA').convert('RGBa').resize((SIZE, SIZE), Image.LANCZOS).convert('RGBA')

    mask = drop_specks(clear_background(rgb))
    ys, xs = np.where(mask)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    side = max(y1 - y0, x1 - x0) + 1
    rgba = np.zeros((side, side, 4), np.uint8)
    oy = (side - (y1 - y0 + 1)) // 2
    ox = (side - (x1 - x0 + 1)) // 2
    crop = rgb[y0:y1 + 1, x0:x1 + 1]
    rgba[oy:oy + crop.shape[0], ox:ox + crop.shape[1], :3] = crop
    rgba[oy:oy + crop.shape[0], ox:ox + crop.shape[1], 3] = mask[y0:y1 + 1, x0:x1 + 1] * 255
    # Premultiplied while shrinking, so no grey fringe bleeds in from the cleared pixels.
    image = Image.fromarray(rgba, 'RGBA').convert('RGBa').resize((SIZE, SIZE), Image.LANCZOS).convert('RGBA')
    return image


def main():
    if not os.path.isdir(SOURCE):
        sys.exit('No Gemini icons in ' + SOURCE)
    os.makedirs(TARGET, exist_ok=True)
    for name in sorted(os.listdir(SOURCE)):
        key, ext = os.path.splitext(name)
        if ext.lower() not in ('.jpg', '.jpeg', '.png'):
            continue
        if key in PIXELATED:
            import subprocess
            subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'pixelate_icon.py'),
                            os.path.join(SOURCE, name), os.path.join(TARGET, key + '.png'), '32'], check=True)
            print(key, '(pixel art 32x32)')
            continue
        image = convert(os.path.join(SOURCE, name))
        image.save(os.path.join(TARGET, key + '.png'))
        print(key)
        for kind in BADGED.get(key, ()):
            badge(image.copy(), kind).save(os.path.join(TARGET, key + '_' + kind + '.png'))
            print(key + '_' + kind)


if __name__ == '__main__':
    main()
