"""Prints a Meshy model's cross-sections, to place a hand-made skeleton: for each height (after
standing it on the ground), the clusters of vertices along x (legs, arms, body) with their x and y
ranges. Blender axes as imported: x to the model's left, -y forward, z up.

Run: blender -b --python measure_slices.py -- <model.fbx> [slices]
"""
import sys

import bpy
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
FBX = argv[0]
SLICES = int(argv[1]) if len(argv) > 1 else 40

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
co = np.concatenate([np.array([o.matrix_world @ v.co for v in o.data.vertices]) for o in meshes])
co[:, 2] -= co[:, 2].min()
height = co[:, 2].max()
print('SLICE height', round(height, 3), 'x', round(co[:, 0].min(), 3), round(co[:, 0].max(), 3),
      'y', round(co[:, 1].min(), 3), round(co[:, 1].max(), 3))
band = height / SLICES
for k in range(SLICES, -1, -1):
    z = k * band
    sel = co[np.abs(co[:, 2] - z) < band * 0.5]
    if len(sel) == 0:
        continue
    xs = np.sort(sel[:, 0])
    gaps = np.where(np.diff(xs) > 0.02)[0]
    starts = np.concatenate([[0], gaps + 1])
    ends = np.concatenate([gaps, [len(xs) - 1]])
    parts = []
    for s, e in zip(starts, ends):
        x0, x1 = xs[s], xs[e]
        inside = sel[(sel[:, 0] >= x0) & (sel[:, 0] <= x1)]
        parts.append('x[%.3f,%.3f] y[%.3f,%.3f]' % (x0, x1, inside[:, 1].min(), inside[:, 1].max()))
    print('SLICE z=%.3f  %s' % (z, '  |  '.join(parts)))
