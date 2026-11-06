"""Big single-view frames of a time range: python strike_preview.py baked.txt out.png t0 t1 step yaw pitch"""
import sys
import numpy as np
from PIL import Image, ImageDraw
import rig
from preview import render

data = rig.build(sys.argv[1])
t0, t1, step = float(sys.argv[3]), float(sys.argv[4]), int(sys.argv[5])
yaw, pitch = float(sys.argv[6]), float(sys.argv[7])
faces = np.concatenate([np.array(data["faces"]), np.array(data["sling_faces"]) + data["body_count"]])
colors = np.concatenate([np.array(data["colors"]), np.array(data["sling_colors"])])
idx = list(range(int(round(t0 * rig.FPS)), int(round(t1 * rig.FPS)) + 1, step))
size, cols = 330, 6
rows = (len(idx) + cols - 1) // cols
img = Image.new("RGB", (size * cols, size * rows), (70, 92, 60))
for k, f in enumerate(idx):
    tris = data["frames"][f % rig.FRAMES][faces]
    render(tris, colors, image=img, offset=((k % cols) * size, (k // cols) * size), size=size, scale=230, center=(0.05, 0.68, 0.05), yaw=yaw, pitch=pitch)
    ImageDraw.Draw(img).text(((k % cols) * size + 5, (k // cols) * size + 5), "t=%.2f" % (f / rig.FPS), fill=(255, 255, 0))
img.save(sys.argv[2])
