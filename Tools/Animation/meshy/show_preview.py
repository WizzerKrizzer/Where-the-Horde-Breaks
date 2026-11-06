import sys, os
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rig"))
from preview import render  # noqa: E402

d = np.load(sys.argv[1])
yaws = [float(y) for y in sys.argv[3].split(",")] if len(sys.argv) > 3 else [35.0]
frames = sorted({int(k[1:]) for k in d.files if k.startswith("t")})
size = 380
sheet = Image.new("RGB", (size * len(frames), size * len(yaws)))
draw = ImageDraw.Draw(sheet)
for row, yaw in enumerate(yaws):
    for k, f in enumerate(frames):
        b = d[f"t{f}"]
        u = np.stack([-b[..., 0], b[..., 2], -b[..., 1]], -1) / 1.81
        # Flip winding with the handedness change so the painter's shading stays outward.
        u = u[:, [0, 2, 1]]
        render(u, d[f"c{f}"], yaw=yaw, pitch=12, size=size, scale=300, center=(0, 0.62, 0.1), image=sheet, offset=(k * size, row * size))
        draw.text((k * size + 6, row * size + 6), f"{f / 30:.2f}s", fill=(255, 255, 255))
sheet.save(sys.argv[2])
