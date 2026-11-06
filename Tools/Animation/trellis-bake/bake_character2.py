"""Bake a TRELLIS character GLB (vertex colours) into flat per-triangle colours with
StyleDefender part encoding. Writes Unity-space triangles as text:
'ax ay az bx by bz cx cy cz nx ny nz r g b part skin item'.

    blender -b -P bake_character.py -- in.glb out.txt height triangles [debug_prefix]
"""
import bpy, bmesh, sys, math, mathutils
import numpy as np
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
glb, out, height, target = args[0], args[1], float(args[2]), int(args[3])
debug = args[4] if len(args) > 4 else None

# Pivots in Unity mesh space (height 1.2), read off the silhouette; must match the material.
SHOULDER = (0.30, 0.78)  # |x|, y
HAND = (0.41, 0.42)
HIP_Y = 0.47           # below: legs and kilt stay planted
ARM_X = 0.265          # outside this the faces belong to an arm
CHEST_Y = 0.68         # above this an arm starts further out (keeps the pecs on the body)
CHEST_ARM_X = 0.30
ARM_TOP = 0.88

BLUE = np.array([0.22, 0.5, 0.95])
BLUE_DARK = np.array([0.13, 0.3, 0.68])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
source = next(o for o in bpy.context.scene.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = source
source.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# Original colours per vertex, sRGB.
attribute = source.data.color_attributes[0]
src_colors = np.array([c.color_srgb[:3] for c in attribute.data])
per_corner = attribute.domain == "CORNER"
src_mesh = source.data
src_bm = bmesh.new()
src_bm.from_mesh(src_mesh)
src_bm.faces.ensure_lookup_table()
src_tree = BVHTree.FromBMesh(src_bm)
if per_corner:
    face_colors = np.array([src_colors[[l.index for l in f.loops]].mean(0) for f in src_bm.faces])
else:
    face_colors = np.array([src_colors[[v.index for v in f.verts]].mean(0) for f in src_bm.faces])

# Decimated copy.
low = source.copy()
low.data = source.data.copy()
bpy.context.scene.collection.objects.link(low)
# Vertices on the border of the blue paint resist decimation so the stripes stay straight.
def is_blue(c):
    r, g, b = c
    return b > r + 0.04 and b > g + 0.01
blue_face = np.array([is_blue(c) for c in face_colors])
border = set()
for e in src_bm.edges:
    if len(e.link_faces) == 2 and blue_face[e.link_faces[0].index] != blue_face[e.link_faces[1].index]:
        border.update(v.index for v in e.verts)
group = low.vertex_groups.new(name="paint")
group.add(sorted(border), 1.0, "REPLACE")
# Optional Laplacian smoothing first: removes the marching-cubes bumps so the decimated facets
# come out larger and flatter. SMOOTH env var = iterations.
import os
smooth_iterations = int(os.environ.get("SMOOTH", "0"))
if smooth_iterations > 0:
    sm = low.modifiers.new("smooth", "LAPLACIANSMOOTH")
    sm.iterations = smooth_iterations
    sm.lambda_factor = float(os.environ.get("SMOOTH_LAMBDA", "0.5"))
    sm.lambda_border = 0.0
    sm.use_volume_preserve = True
    sm.use_normalized = True
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.modifier_apply(modifier="smooth")
modifier = low.modifiers.new("decimate", "DECIMATE")
modifier.vertex_group = "paint"
modifier.invert_vertex_group = True
modifier.vertex_group_factor = float(args[5]) if len(args) > 5 else 10.0
modifier.ratio = min(1.0, target / len(src_mesh.polygons))
bpy.context.view_layer.objects.active = low
bpy.ops.object.modifier_apply(modifier="decimate")
mesh = low.data
mesh.calc_loop_triangles()
edge_faces = {}
for ti, tri in enumerate(mesh.loop_triangles):
    vs = tri.vertices
    for a, b in ((vs[0], vs[1]), (vs[1], vs[2]), (vs[2], vs[0])):
        edge_faces.setdefault((min(a, b), max(a, b)), []).append(ti)
neighbours = [[] for _ in mesh.loop_triangles]
for faces_on_edge in edge_faces.values():
    for a in faces_on_edge:
        for b in faces_on_edge:
            if a != b:
                neighbours[a].append(b)
areas = np.array([tri.area for tri in mesh.loop_triangles])

points = np.array([v.co[:] for v in mesh.vertices])
# Blender (x, y, z) -> Unity (-x, z, -y): Y up, the face (Blender -Y) towards +Z.
unity = np.stack([-points[:, 0], points[:, 2], -points[:, 1]], 1)
lo, hi = unity.min(0), unity.max(0)
scale = height / (hi[1] - lo[1])
unity = (unity - np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])) * scale


def sample(points_blender):
    colors = []
    for p in points_blender:
        location, normal, index, distance = src_tree.find_nearest(mathutils.Vector(p))
        if index is not None:
            colors.append(face_colors[index])
    return np.array(colors)


def kind(c):
    r, g, b = c
    # Any bluish tint is paint: nothing else on the character is blue.
    if b > r + 0.04 and b > g + 0.01:
        return "blue"
    return "other"


def is_skin(c, center):
    r, g, b = c
    x, y, z = center
    if r < 0.25 or r < g or r - b < 0.05 or (r - b) / r < 0.12:
        return False
    hue = (g - b) / (r - b)
    if hue > 0.6:
        return False
    belt = 0.46 < y < 0.58 and abs(x) < 0.3
    pouch = x < -0.14 and 0.33 < y < 0.58 and z > -0.08
    kilt = 0.25 < y < 0.47 and abs(x) < ARM_X
    return not belt and not pouch and not kilt


def part_of(center):
    x, y, z = center
    side = abs(x)
    arm_x = CHEST_ARM_X if y > CHEST_Y else ARM_X
    if side > arm_x and y < ARM_TOP and y > HAND[1] - 0.15:
        return 0.25 if x < 0 else 0.5
    return 1.0 if y < HIP_Y else 0.0


SKIN_GUESS = np.array([0.51, 0.35, 0.29])  # typical TRELLIS skin before flattening
lines = []
parts = {}
debug_tris = []
for tri in mesh.loop_triangles:
    corners_b = [np.array(mesh.vertices[i].co[:]) for i in tri.vertices]
    mid_b = sum(corners_b) / 3
    probe = [mid_b] + [mid_b + (c - mid_b) * t for c in corners_b for t in (0.4, 0.8)]
    samples = sample(probe)
    kinds = [kind(s) for s in samples]
    blue = [s for s, k in zip(samples, kinds) if k == "blue"]
    other = [s for s, k in zip(samples, kinds) if k == "other"]
    if len(blue) > len(other):
        t = np.clip((np.mean(blue, 0)[2] - 0.45) / 0.4, 0.0, 1.0)
        color = BLUE_DARK + (BLUE - BLUE_DARK) * t
    else:
        color = np.median(other, 0)
    corners = [unity[i] for i in tri.vertices]
    center = sum(corners) / 3
    if center[1] > 0.8 and center[1] < 1.08 and center[2] < 0.0 and color.max() - color.min() < 0.12 and color.mean() > 0.4:
        color = np.array([0.22, 0.14, 0.09])
    n = mathutils.Vector(tri.normal)
    normal = np.array([-n.x, n.z, -n.y])
    part = part_of(center)
    skin = 1.0 if (len(blue) <= len(other) and is_skin(color, center)) else 0.0
    if len(blue) <= len(other):
        # TRELLIS greys out one hand and streaks the shins with the kilt's shadow.
        hand = part in (0.25, 0.5) and center[1] < 0.5
        shin = part == 1.0 and 0.175 < center[1] < 0.32 and abs(center[0]) < 0.22 and color.mean() < 0.45
        if hand or shin:
            skin = 1.0
            color = SKIN_GUESS
    parts[part] = parts.get(part, 0) + 1
    debug_tris.append((corners, part, skin, color, normal, len(blue) > len(other)))

# TRELLIS colours carry baked light and shadow; the game shader adds its own. Flatten them:
# skin to the game's skin colour with a little of its variation, everything else into a small
# palette with the darkest tones lifted.
SKIN = np.array([0.8, 0.58, 0.42])
colors = np.array([d[3] for d in debug_tris])
skin_mask = np.array([d[2] > 0.5 for d in debug_tris])
blue_mask = np.array([d[5] for d in debug_tris])
rest = ~skin_mask & ~blue_mask
data = colors[rest]
rng = np.random.default_rng(1)
K = 7
centers = data[rng.choice(len(data), K, replace=False)]
for _ in range(30):
    label = ((data[:, None, :] - centers[None]) ** 2).sum(-1).argmin(1)
    centers = np.array([data[label == k].mean(0) if (label == k).any() else centers[k] for k in range(K)])
labels = np.zeros(len(colors), int)
labels[skin_mask] = K
labels[blue_mask] = K + 1
labels[rest] = label
# Area-weighted majority over neighbours removes speckles and straightens region borders.
for _ in range(6):
    new = labels.copy()
    for i, nb in enumerate(neighbours):
        if not nb:
            continue
        votes = {}
        votes[labels[i]] = areas[i] * 1.5
        for j in nb:
            votes[labels[j]] = votes.get(labels[j], 0.0) + areas[j]
        new[i] = max(votes, key=votes.get)
    labels = new
palette = list((0.05 + centers * 1.08) * np.array([1.0, 0.98, 0.9])) + [SKIN, BLUE]
colors = np.clip(np.array([palette[l] for l in labels]), 0, 1)
print("PALETTE", np.round(np.array(palette), 2).tolist())
debug_tris = [(d[0], d[1], 1.0 if labels[i] == K else 0.0, d[3], d[4], labels[i] == K + 1) for i, d in enumerate(debug_tris)]

for i, (corners, part, skin, color, normal, is_blue) in enumerate(debug_tris):
    values = [c for corner in corners for c in corner] + list(normal) + list(colors[i]) + [part, skin, 0.0]
    lines.append(" ".join("%.4f" % v for v in values))
debug_tris = [(d[0], d[1], d[2], colors[i]) for i, d in enumerate(debug_tris)]

with open(out, "w") as f:
    f.write("\n".join(lines))
print("BAKED", len(lines), "triangles; size", np.round(unity.max(0) - unity.min(0), 3), "parts", parts)

if debug:
    # Front and back renders: real colours, and parts (grey body, red left arm, green right
    # arm, dark static, skin faces lighter).
    for mode in ("color", "parts"):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        me = bpy.data.meshes.new("d")
        verts, faces, cols = [], [], []
        for corners, part, skin, color in debug_tris:
            base = len(verts)
            # Back to Blender space for rendering: Unity (x, y, z) -> Blender (-x, -z, y).
            verts += [(-c[0], -c[2], c[1]) for c in corners]
            faces.append((base, base + 1, base + 2))
            if mode == "color":
                cols.append(tuple(color))
            else:
                c = {0.0: (0.6, 0.6, 0.6), 0.25: (0.9, 0.2, 0.2), 0.5: (0.2, 0.8, 0.2), 1.0: (0.25, 0.25, 0.3)}[part]
                cols.append(tuple(min(1, v * (1.35 if skin else 1)) for v in c))
        me.from_pydata(verts, [], faces)
        attr = me.color_attributes.new("c", "FLOAT_COLOR", "CORNER")
        for poly, c in zip(me.polygons, cols):
            for li in poly.loop_indices:
                lin = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c]
                attr.data[li].color = (*lin, 1)
        ob = bpy.data.objects.new("d", me)
        bpy.context.scene.collection.objects.link(ob)
        mat = bpy.data.materials.new("m")
        mat.use_nodes = True
        node = mat.node_tree.nodes.new("ShaderNodeVertexColor")
        node.layer_name = "c"
        emit = mat.node_tree.nodes.new("ShaderNodeEmission")
        mat.node_tree.links.new(node.outputs["Color"], emit.inputs["Color"])
        mat.node_tree.links.new(emit.outputs["Emission"], mat.node_tree.nodes["Material Output"].inputs["Surface"])
        me.materials.append(mat)
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_EEVEE"
        scene.render.resolution_x, scene.render.resolution_y = 700, 700
        scene.view_settings.view_transform = "Standard"
        scene.world = bpy.data.worlds.new("w")
        scene.world.color = (0.3, 0.3, 0.3)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = 1.35
        scene.collection.objects.link(cam)
        scene.camera = cam
        for name, yaw in (("front", 0), ("back", 180)):
            y = math.radians(yaw)
            cam.location = (math.sin(y) * 5, -math.cos(y) * 5, 0.6)
            cam.rotation_euler = (math.radians(90), 0, y)
            scene.render.filepath = "%s_%s_%s.png" % (debug, mode, name)
            bpy.ops.render.render(write_still=True)
