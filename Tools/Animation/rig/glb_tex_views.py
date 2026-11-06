import sys, numpy as np, trimesh
from PIL import Image
from preview import render
scene = trimesh.load(sys.argv[1])
mesh = scene.to_geometry()
v = mesh.vertices; f = mesh.faces
tex = np.asarray(mesh.visual.material.baseColorTexture.convert("RGB")) / 255.0
uv = mesh.visual.uv
th, tw = tex.shape[:2]
fuv = uv[f].mean(1)
px = np.clip((fuv[:, 0] % 1) * tw, 0, tw - 1).astype(int); py = np.clip((1 - fuv[:, 1] % 1) * th, 0, th - 1).astype(int)
colors = tex[py, px]
lo, hi = v.min(0), v.max(0); h = hi[1] - lo[1]
print("tris", len(f), "tex", tex.shape, "bounds", lo, hi)
v = (v - [(lo[0]+hi[0])/2, lo[1], (lo[2]+hi[2])/2]) / h
tris = v[f]
size = 600
sheet = Image.new("RGB", (size * 4, size))
for k, yaw in enumerate([0, 90, 180, 270]):
    render(tris, colors, yaw=yaw, size=size, scale=540, center=(0, 0.5, 0), image=sheet, offset=(k * size, 0))
sheet.save(sys.argv[2])
Image.fromarray((tex * 255).astype(np.uint8)).resize((512, 512)).save(sys.argv[3])
