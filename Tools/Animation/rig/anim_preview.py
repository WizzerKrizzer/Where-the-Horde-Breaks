import sys, os
import numpy as np
from PIL import Image, ImageDraw
import rig
from preview import render

data = rig.build(sys.argv[1])
out = sys.argv[2]
os.makedirs(out, exist_ok=True)
faces = np.array(data["faces"])
nb = data["body_count"]
sf = np.array(data["sling_faces"]) + nb
all_faces = np.concatenate([faces, sf])
colors = np.concatenate([np.array(data["colors"]), np.array(data["sling_colors"])])
views = [dict(yaw=90, pitch=0), dict(yaw=35, pitch=15), dict(yaw=150, pitch=62)]
size = 360
step = int(sys.argv[3]) if len(sys.argv) > 3 else 1
for f in range(0, len(data["frames"]), step):
    v = data["frames"][f]
    tris = v[all_faces]
    img = Image.new("RGB", (size * len(views), size), (70, 92, 60))
    for k, view in enumerate(views):
        render(tris, colors, image=img, offset=(k * size, 0), size=size, scale=200, center=(0, 0.62, 0.05), **view)
    ImageDraw.Draw(img).text((6, 6), "t=%.2f" % (f / rig.FPS), fill=(255, 255, 0))
    img.save(os.path.join(out, "f_%03d.png" % f))
print("release stone", data["release_stone"])
