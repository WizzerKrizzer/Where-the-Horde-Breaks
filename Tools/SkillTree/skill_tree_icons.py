"""Pixel icons for the skill tree (16x16), drawn from simple primitives, outlined, with an
optional corner badge for the Kill Essence upgrades of a special. Writes a preview sheet and,
with --emit <path>, a C# file holding the icons as palette strings."""
import sys, math
from PIL import Image, ImageDraw

N = 16
OUT = (26, 22, 18, 255)
T = None

def rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)

class G:
    def __init__(self):
        self.p = [[T] * N for _ in range(N)]
    def set(self, x, y, c):
        if 0 <= x < N and 0 <= y < N:
            self.p[y][x] = c
    def disc(self, cx, cy, r, c):
        for y in range(N):
            for x in range(N):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.set(x, y, c)
    def ring(self, cx, cy, r, w, c, a0=0, a1=360):
        for y in range(N):
            for x in range(N):
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                d = math.hypot(dx, dy)
                ang = math.degrees(math.atan2(-dy, dx)) % 360
                inside = (a0 <= ang <= a1) if a0 <= a1 else (ang >= a0 or ang <= a1)
                if r - w <= d <= r and inside:
                    self.set(x, y, c)
    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)
    def line(self, x0, y0, x1, y1, c, w=1):
        steps = max(abs(x1 - x0), abs(y1 - y0)) * 2 + 1
        for i in range(steps + 1):
            t = i / steps
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for ox in range(w):
                for oy in range(w):
                    self.set(int(round(x)) + ox - (w - 1) // 2, int(round(y)) + oy - (w - 1) // 2, c)
    def poly(self, pts, c):
        img = Image.new('L', (N, N), 0)
        ImageDraw.Draw(img).polygon(pts, fill=255)
        for y in range(N):
            for x in range(N):
                if img.getpixel((x, y)):
                    self.set(x, y, c)
    def outline(self):
        q = [row[:] for row in self.p]
        for y in range(N):
            for x in range(N):
                if self.p[y][x] is T:
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        xx, yy = x + dx, y + dy
                        if 0 <= xx < N and 0 <= yy < N and self.p[yy][xx] not in (T, OUT):
                            q[y][x] = OUT
                            break
        self.p = q

STONE, STONE_HI, STONE_LO = rgb('9a948c'), rgb('c9c2b8'), rgb('66615b')
WOOD, WOOD_HI, WOOD_LO = rgb('a0703f'), rgb('c99a5e'), rgb('6e4a28')
FUR, FUR_LO = rgb('8a5a36'), rgb('5e3b22')
IVORY = rgb('efe6cf')
RED, RED_HI, RED_LO = rgb('d8483a'), rgb('f2867a'), rgb('8e2a22')
ORANGE, YELLOW, WHITE = rgb('f08a24'), rgb('f7d23e'), rgb('fff6e0')
GREEN, GREEN_HI, GREEN_LO = rgb('5fbf4a'), rgb('a6e38c'), rgb('2f7a2a')
BLUE, BLUE_HI = rgb('4a8fd8'), rgb('9cc8f2')
SKIN = rgb('d9a777')
DARK = rgb('2a2420')
EARTH, EARTH_LO = rgb('8c6a46'), rgb('5c4430')

def rock_ball(g, cx=8, cy=8.5, r=6):
    g.disc(cx, cy, r, STONE)
    g.disc(cx + r * 0.25, cy + r * 0.3, r * 0.75, STONE)
    for (x, y) in ((int(cx - r * 0.45), int(cy - r * 0.5)), (int(cx - r * 0.25), int(cy - r * 0.6)), (int(cx - r * 0.55), int(cy - r * 0.25))):
        g.set(x, y, STONE_HI)
    g.ring(cx, cy, r, 1.2, STONE_LO, 200, 340)

def flame(g, cx=8, base=14, h=11, w=5):
    top = base - h
    g.poly([(cx - w, base), (cx - w, base - h * 0.45), (cx - w * 0.3, top + h * 0.25), (cx, top), (cx + w * 0.5, top + h * 0.35), (cx + w, base - h * 0.5), (cx + w, base)], ORANGE)
    g.poly([(cx - w * 0.5, base), (cx - w * 0.5, base - h * 0.35), (cx, top + h * 0.45), (cx + w * 0.55, base - h * 0.3), (cx + w * 0.5, base)], YELLOW)
    g.poly([(cx - 1, base), (cx - 1, base - h * 0.2), (cx + 0.5, base - h * 0.32), (cx + 1.5, base)], WHITE)

ICONS = {}
def icon(name):
    def deco(fn):
        ICONS[name] = fn
        return fn
    return deco

@icon('rock')
def _(g):
    rock_ball(g)
    g.line(0, 11, 2, 11, STONE_HI); g.line(0, 14, 2, 14, STONE_HI)

@icon('slingers')
def _(g):
    g.ring(7, 8, 6.5, 1.3, WOOD, 20, 300)
    g.line(12, 4, 13, 3, WOOD)
    g.disc(12.5, 11.5, 2.6, STONE); g.set(11, 10, STONE_HI)

@icon('spear')
def _(g):
    g.line(2, 14, 11, 5, WOOD, 2)
    g.poly([(10, 4), (15, 0), (14, 5), (12, 7)], STONE)
    g.set(13, 2, STONE_HI)
    g.line(2, 12, 4, 14, WOOD_LO)

@icon('mammoth')
def _(g):
    g.disc(8, 6.5, 5.5, FUR)
    g.disc(3, 7, 3, FUR_LO); g.disc(13, 7, 3, FUR_LO)
    g.rect(7, 9, 9, 13, FUR); g.rect(7, 13, 8, 14, FUR_LO)
    g.line(5, 10, 3, 13, IVORY, 2); g.line(3, 13, 4, 15, IVORY)
    g.line(11, 10, 13, 13, IVORY, 2); g.line(13, 13, 12, 15, IVORY)
    g.set(6, 6, DARK); g.set(10, 6, DARK)

@icon('cave')
def _(g):
    g.poly([(0, 15), (2, 7), (5, 3), (10, 2), (13, 5), (15, 10), (15, 15)], STONE)
    g.poly([(4, 15), (5, 10), (8, 7), (11, 10), (12, 15)], DARK)
    g.set(6, 4, STONE_HI); g.set(9, 3, STONE_HI); g.set(12, 6, STONE_HI)
    g.line(1, 15, 14, 15, STONE_LO)

@icon('damage')
def _(g):
    pts = []
    for i in range(16):
        r = 7 if i % 2 == 0 else 3
        a = math.radians(i * 22.5 + 11)
        pts.append((8 + r * math.cos(a), 8 + r * math.sin(a)))
    g.poly(pts, RED); g.disc(8, 8, 2.5, YELLOW)

@icon('speed')
def _(g):
    for x0 in (2, 8):
        g.line(x0, 3, x0 + 5, 8, YELLOW, 2); g.line(x0 + 5, 8, x0, 13, YELLOW, 2)

@icon('range')
def _(g):
    g.ring(8, 8, 7.5, 1.3, BLUE)
    g.ring(8, 8, 4.5, 1.3, BLUE_HI)
    g.disc(8, 8, 1.6, WHITE)

@icon('health')
def _(g):
    g.disc(5, 6, 3.5, RED); g.disc(11, 6, 3.5, RED)
    g.poly([(1.5, 7), (14.5, 7), (8, 14)], RED)
    g.set(4, 4, RED_HI); g.set(3, 5, RED_HI)

@icon('capacity')
def _(g):
    for cx, c in ((5, SKIN), (11, SKIN)):
        g.disc(cx, 4.5, 2.3, c); g.rect(cx - 2, 7, cx + 1, 12, FUR)
    g.rect(7, 12, 9, 14, GREEN); g.rect(6, 13, 10, 13, GREEN)

@icon('cooldown')
def _(g):
    g.rect(3, 1, 12, 2, WOOD); g.rect(3, 13, 12, 14, WOOD)
    g.poly([(4, 3), (11, 3), (8.5, 7.5), (8.5, 8.5), (11, 12), (4, 12), (6.5, 8.5), (6.5, 7.5)], BLUE_HI)
    g.poly([(5.5, 12), (9.5, 12), (7.5, 9.5)], YELLOW); g.rect(5, 4, 9, 4, YELLOW)

@icon('roll')
def _(g):
    rock_ball(g, 5, 9, 4.2)
    g.line(10, 9, 14, 9, WHITE); g.line(13, 7, 15, 9, WHITE); g.line(13, 11, 15, 9, WHITE)
    g.line(0, 14, 15, 14, EARTH)

@icon('stun')
def _(g):
    g.ring(8, 9, 6.5, 1.2, YELLOW, 200, 160)
    for (x, y) in ((3, 5), (12, 5), (8, 13)):
        g.set(x, y, WHITE); g.set(x - 1, y, YELLOW); g.set(x + 1, y, YELLOW); g.set(x, y - 1, YELLOW); g.set(x, y + 1, YELLOW)
    g.disc(8, 8.5, 2.3, STONE)

@icon('ricochet')
def _(g):
    g.line(1, 13, 5, 4, WHITE); g.line(5, 4, 9, 13, WHITE); g.line(9, 13, 12, 6, WHITE)
    g.disc(13, 4.5, 2.4, STONE); g.set(12, 3, STONE_HI)
    g.set(5, 3, YELLOW); g.set(9, 14, YELLOW)

@icon('shrapnel')
def _(g):
    g.disc(4, 8, 3, STONE); g.set(3, 6, STONE_HI)
    for (x, y) in ((10, 3), (13, 6), (14, 9), (12, 12), (9, 14), (10, 8)):
        g.disc(x + 0.5, y + 0.5, 1.2, STONE_HI)
    g.line(7, 7, 9, 5, YELLOW); g.line(7, 9, 9, 11, YELLOW)

@icon('quake')
def _(g):
    g.rect(0, 10, 15, 15, EARTH)
    g.line(3, 10, 6, 13, DARK); g.line(6, 13, 9, 11, DARK); g.line(9, 11, 12, 15, DARK)
    for x in (3, 8, 13):
        g.line(x, 2, x - 1, 4, YELLOW); g.line(x - 1, 4, x, 6, YELLOW); g.line(x, 6, x - 1, 8, YELLOW)

@icon('fire')
def _(g):
    flame(g)

@icon('shatter')
def _(g):
    g.poly([(2, 6), (6, 2), (7, 7), (3, 10)], STONE)
    g.poly([(9, 2), (14, 5), (12, 9), (8, 7)], STONE)
    g.poly([(4, 12), (8, 9), (12, 11), (9, 15)], STONE)
    g.set(5, 3, STONE_HI); g.set(11, 3, STONE_HI); g.set(7, 11, STONE_HI)
    g.set(1, 1, STONE_HI); g.set(14, 13, STONE_HI); g.set(1, 14, STONE_HI)

@icon('poison')
def _(g):
    g.poly([(8, 1), (13, 9), (8, 15), (3, 9)], GREEN)
    g.disc(8, 10, 4.6, GREEN)
    g.disc(6.5, 9, 1.3, GREEN_HI); g.disc(9.5, 12, 1.1, GREEN_LO)

@icon('pierce')
def _(g):
    g.disc(5, 8, 2.8, GREEN); g.disc(10, 8, 2.8, GREEN)
    g.line(0, 8, 13, 8, WOOD, 2)
    g.poly([(12, 5.5), (16, 8), (12, 10.5)], STONE)

@icon('biggame')
def _(g):
    g.ring(8, 8, 7, 1.4, RED)
    g.line(8, 0, 8, 4, RED); g.line(8, 12, 8, 15, RED); g.line(0, 8, 4, 8, RED); g.line(12, 8, 15, 8, RED)
    g.disc(8, 8, 2.4, IVORY); g.line(5, 5, 6, 7, IVORY); g.line(11, 5, 10, 7, IVORY)

@icon('rage')
def _(g):
    g.disc(8, 8.5, 6.5, RED)
    g.line(4, 5, 7, 7, DARK, 1); g.line(12, 5, 9, 7, DARK, 1)
    g.set(5, 8, WHITE); g.set(10, 8, WHITE)
    g.line(5, 12, 11, 12, DARK); g.set(4, 11, DARK); g.set(12, 11, DARK)

@icon('riders')
def _(g):
    g.poly([(1, 9), (3, 6), (11, 5), (14, 7), (15, 11), (13, 11), (12, 15), (10, 15), (10, 12), (5, 12), (5, 15), (3, 15), (3, 12), (1, 12)], FUR)
    g.line(14, 10, 13, 13, IVORY)
    g.disc(7, 2.5, 1.6, SKIN); g.rect(6, 4, 8, 5, WOOD)

@icon('armor')
def _(g):
    g.poly([(2, 2), (14, 2), (14, 8), (8, 15), (2, 8)], WOOD_LO)
    g.poly([(3.5, 3.5), (12.5, 3.5), (12.5, 8), (8, 13.3), (3.5, 8)], STONE)
    g.rect(7, 3, 8, 13, STONE_LO); g.rect(3, 6, 12, 7, STONE_LO)
    g.set(4, 4, STONE_HI)

@icon('shield')
def _(g):
    g.disc(8, 8, 7, WOOD)
    g.ring(8, 8, 7, 1.2, WOOD_LO)
    g.line(3, 8, 13, 8, WOOD_LO); g.line(8, 3, 8, 13, WOOD_LO)
    g.disc(8, 8, 2, STONE); g.set(7, 7, STONE_HI)

@icon('chief')
def _(g):
    g.disc(8, 11, 4, SKIN)
    g.rect(4, 8, 12, 9, RED)
    for x, c in ((5, WHITE), (8, RED), (11, WHITE)):
        g.line(x, 7, x, 1, c, 2)
    g.set(6, 11, DARK); g.set(10, 11, DARK)

@icon('torch')
def _(g):
    g.line(6, 15, 9, 8, WOOD, 2)
    g.rect(7, 7, 10, 8, WOOD_LO)
    flame(g, 9, 7, 7, 3.5)

BADGES = {}
def badge(name):
    def deco(fn):
        BADGES[name] = fn
        return fn
    return deco

# Badges sit in the bottom-right 7x7 corner over a dark plate.
def plate(g):
    g.rect(9, 9, 15, 15, OUT)

@badge('up')
def _(g):
    plate(g); g.poly([(12.5, 9.5), (15, 13), (10, 13)], GREEN_HI); g.rect(11, 13, 13, 15, GREEN_HI)

@badge('plus')
def _(g):
    plate(g); g.rect(10, 12, 14, 12, GREEN_HI); g.rect(12, 10, 12, 14, GREEN_HI)

@badge('time')
def _(g):
    plate(g); g.ring(12.5, 12.5, 3, 1, WHITE); g.set(12, 11, WHITE); g.set(12, 12, WHITE); g.set(13, 12, WHITE)

@badge('fast')
def _(g):
    plate(g)
    for x0 in (10, 12):
        g.line(x0, 10, x0 + 2, 12, YELLOW); g.line(x0 + 2, 12, x0, 14, YELLOW)

@badge('heart')
def _(g):
    plate(g); g.disc(11.2, 11.5, 1.6, RED); g.disc(13.8, 11.5, 1.6, RED); g.poly([(9.6, 12), (15.4, 12), (12.5, 15)], RED)

def build(key):
    base, _, b = key.partition('|')
    g = G()
    ICONS[base](g)
    g.outline()
    if b:
        BADGES[b](g)
    return g.p

def keys_in_use():
    return sorted(set(sys.argv[sys.argv.index('--keys') + 1].split(','))) if '--keys' in sys.argv else sorted(ICONS)

def preview(keys, path, scale=6):
    cols = 8
    rows = (len(keys) + cols - 1) // cols
    cell = N * scale + 12
    img = Image.new('RGBA', (cols * cell, rows * cell), (42, 42, 46, 255))
    for i, k in enumerate(keys):
        p = build(k)
        ox, oy = (i % cols) * cell + 6, (i // cols) * cell + 6
        for y in range(N):
            for x in range(N):
                if p[y][x] is not T:
                    for sy in range(scale):
                        for sx in range(scale):
                            img.putpixel((ox + x * scale + sx, oy + y * scale + sy), p[y][x])
    img.save(path)

def emit(keys, path):
    out = ['// Generated by Tools/SkillTree/skill_tree_icons.py (pixel art, 16x16). Do not edit by hand:',
           '// each icon is 16 rows, top first; each character indexes the palette ("." is clear).',
           'using System.Collections.Generic;', 'using UnityEngine;', '', 'namespace TowerDefense.UI', '{',
           '    internal static partial class SkillTreePixelIcons', '    {',
           '        private static readonly Dictionary<string, (string[] rows, Color32[] palette)> Data = new()', '        {']
    alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
    for k in keys:
        p = build(k)
        colors = []
        rows = []
        for y in range(N):
            row = ''
            for x in range(N):
                c = p[y][x]
                if c is T:
                    row += '.'
                else:
                    if c not in colors:
                        colors.append(c)
                    row += alphabet[colors.index(c)]
            rows.append(row)
        pal = ', '.join('new Color32(%d, %d, %d, 255)' % c[:3] for c in colors)
        out.append('            ["%s"] = (new[]' % k)
        out.append('            {')
        for r in rows:
            out.append('                "%s",' % r)
        out.append('            }, new Color32[] { %s }),' % pal)
    out += ['        };', '    }', '}', '']
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(out))

if __name__ == '__main__':
    keys = keys_in_use()
    if '--preview' in sys.argv:
        preview(keys, sys.argv[sys.argv.index('--preview') + 1])
    if '--emit' in sys.argv:
        emit(keys, sys.argv[sys.argv.index('--emit') + 1])
    print(len(keys), 'icons')
