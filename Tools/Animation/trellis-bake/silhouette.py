"""Print front (x/y) and side (z/y) ASCII silhouettes of a GLB in Unity space, normalised to
a given height, so pivots can be read off."""
import bpy, sys
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
glb, height = args[0], float(args[1])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
points = []
for o in bpy.context.scene.objects:
    if o.type == "MESH":
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            points.append((-w.x, w.z, -w.y))  # Unity: x right, y up, z forward (+Z faces front)
p = np.array(points)
lo, hi = p.min(0), p.max(0)
s = height / (hi[1] - lo[1])
p = (p - np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])) * s
print("SIZE", np.round(p.max(0) - p.min(0), 3), "z range", np.round([p[:, 2].min(), p[:, 2].max()], 3))
step = 0.025
for axis, name in ((0, "FRONT x"), (2, "SIDE z")):
    xs = np.arange(-0.5, 0.5, step)
    print(name, "columns from -0.5 step", step)
    for y in np.arange(height, -0.001, -step * 2):
        row = p[(p[:, 1] >= y - step) & (p[:, 1] < y + step)]
        line = "".join("#" if ((row[:, axis] >= x) & (row[:, axis] < x + step)).any() else "." for x in xs)
        print("%5.2f %s" % (y, line))
