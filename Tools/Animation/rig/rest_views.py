import sys
from PIL import Image
import numpy as np
from preview import load, render

tris, colors, flags = load(sys.argv[1])
tris = tris[:, [0, 2, 1]]  # bake winding is reversed
print("bounds", tris.reshape(-1, 3).min(0), tris.reshape(-1, 3).max(0))
size = 520
sheet = Image.new("RGB", (size * 3, size))
for k, yaw in enumerate([0, 90, 180]):
    render(tris, colors, yaw=yaw, size=size, scale=380, center=(0, 0.6, 0), grid=True, image=sheet, offset=(k * size, 0))
sheet.save(sys.argv[2])
