"""Render every frame of a posed npz (mix_apply.py) from two angles into PNGs for ffmpeg."""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rig"))
from preview import render  # noqa: E402

d = np.load(sys.argv[1])
out_dir, title = sys.argv[2], sys.argv[3]
os.makedirs(out_dir, exist_ok=True)
frames = sorted({int(k[1:]) for k in d.files if k.startswith("t")})
size = 540
try:
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 30)
except OSError:
    font = None
for i, f in enumerate(frames):
    img = Image.new("RGB", (size * 2, size), (70, 92, 60))
    b = d[f"t{f}"]
    u = np.stack([-b[..., 0], b[..., 2], -b[..., 1]], -1) / 1.81
    u = u[:, [0, 2, 1]]
    for k, yaw in enumerate((35.0, 270.0)):
        render(u, d[f"c{f}"], yaw=yaw, pitch=12, size=size, scale=380, center=(0, 0.55, 0.1), image=img, offset=(k * size, 0))
    draw = ImageDraw.Draw(img)
    draw.text((14, 10), f"{title}  {f / 30:.2f}s", fill=(255, 255, 255), font=font)
    img.save(os.path.join(out_dir, f"frame_{i:03d}.png"))
print("frames", len(frames))
