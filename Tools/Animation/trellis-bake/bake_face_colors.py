"""Import a Meshy FBX, bake its texture into one flat colour per triangle and write the
triangles in Unity coordinates (x, y up, z) as text: 'ax ay az bx by bz cx cy cz nx ny nz r g b'."""
import bpy, sys, mathutils
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
fbx, out, height = args[0], args[1], float(args[2])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
mesh = obj.data
mesh.calc_loop_triangles()

image = bpy.data.images[0]
w, h = image.size
pixels = np.empty(w * h * 4, dtype=np.float32)
image.pixels.foreach_get(pixels)
pixels = pixels.reshape(h, w, 4)

def sample(uv):
    x = int(uv[0] % 1.0 * (w - 1))
    y = int(uv[1] % 1.0 * (h - 1))
    return pixels[y, x, :3]

# The game's player blue (StyleTowerTests PlayerBlue / PlayerBlueDark).
BLUE = np.array([0.22, 0.5, 0.95])
BLUE_DARK = np.array([0.13, 0.3, 0.68])

def kind(c):
    r, g, b = c
    if b > 0.3 and b > r * 1.6 + 0.1 and b > g * 1.3 + 0.1:
        return "blue"
    if b > g + 0.08 and b > 0.25:
        return "mixed"  # purple texels where blue paint blurs into brown hide
    return "other"

def face_color(uvs):
    mid = (uvs[0] + uvs[1] + uvs[2]) / 3
    points = [mid] + [mid + (uv - mid) * t for uv in uvs for t in (0.45, 0.85)]
    samples = [sample(p) for p in points]
    kinds = [kind(s) for s in samples]
    blue = [s for s, k in zip(samples, kinds) if k == "blue"]
    other = [s for s, k in zip(samples, kinds) if k == "other"]
    if not blue and not other:
        mean = np.mean(samples, axis=0)
        if mean[2] > mean[0]:
            blue = samples
        else:
            other = [np.array([s[0], s[1], min(s[2], s[1] * 0.75)]) for s in samples]
    if len(blue) > len(other):
        # Snap the painted band to the player colour, keeping light and dark strokes.
        t = np.clip((np.mean(blue, axis=0)[2] - 0.45) / 0.4, 0.0, 1.0)
        return BLUE_DARK + (BLUE - BLUE_DARK) * t
    return np.mean(other, axis=0)

world = [obj.matrix_world @ v.co for v in mesh.vertices]
lo = mathutils.Vector([min(p[i] for p in world) for i in range(3)])
hi = mathutils.Vector([max(p[i] for p in world) for i in range(3)])
scale = height / (hi.z - lo.z)
center = (lo + hi) / 2
normal_matrix = obj.matrix_world.to_3x3().inverted().transposed()

def unity(p):
    q = (p - mathutils.Vector((center.x, center.y, lo.z))) * scale
    return (q.x, q.z, q.y)  # Blender Z-up to Unity Y-up; front (-Y) becomes -Z

uv_layer = mesh.uv_layers.active.data
lines = []
for tri in mesh.loop_triangles:
    uvs = [mathutils.Vector(uv_layer[l].uv) for l in tri.loops]
    color = face_color(uvs)
    corners = [unity(world[i]) for i in tri.vertices]
    n = (normal_matrix @ tri.normal).normalized()
    values = [c for corner in corners for c in corner] + [n.x, n.z, n.y] + list(color)
    lines.append(" ".join("%.4f" % v for v in values))

with open(out, "w") as f:
    f.write("\n".join(lines))
print("BAKED", len(lines), "triangles, scale", round(scale, 3), "size", round((hi.x - lo.x) * scale, 2), round((hi.y - lo.y) * scale, 2), round((hi.z - lo.z) * scale, 2))
