import sys, numpy as np, trimesh
from PIL import Image
from preview import render
scene = trimesh.load(sys.argv[1])
mesh = scene.dump(concatenate=True) if hasattr(scene, "dump") else scene
v = mesh.vertices; f = mesh.faces
col = mesh.visual.vertex_colors[:, :3] / 255.0
col = np.where(col <= 0.0031308, col * 12.92, 1.055 * col ** (1 / 2.4) - 0.055)
lo, hi = v.min(0), v.max(0)
print("tris", len(f), "bounds", lo, hi)
h = hi[1] - lo[1]
v = (v - [(lo[0]+hi[0])/2, lo[1], (lo[2]+hi[2])/2]) / h
tris = v[f]; colors = col[f].mean(1)
size = 700
sheet = Image.new("RGB", (size * 4, size))
for k, yaw in enumerate([0, 90, 180, 270]):
    render(tris, colors, yaw=yaw, pitch=0, size=size, scale=620, center=(0, 0.5, 0), image=sheet, offset=(k * size, 0))
sheet.save(sys.argv[2])
# close-up of head and hands
sheet2 = Image.new("RGB", (size * 2, size))
render(tris, colors, yaw=0, size=size, scale=1900, center=(0, 0.85, 0), image=sheet2, offset=(0, 0))
render(tris, colors, yaw=0, size=size, scale=1500, center=(-0.25, 0.45, 0), image=sheet2, offset=(size, 0))
sheet2.save(sys.argv[3])
