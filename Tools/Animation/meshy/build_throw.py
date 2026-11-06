"""Blender: add the sling (bones + skinned mesh) and the Throw action to the Meshy warrior, then
export FBX + texture. Optionally dump posed triangles of chosen frames for a preview.

blender -b -P build_throw.py -- in.glb throw.json out.fbx out.png [preview.npz]
"""
import bpy, sys, json, math
import numpy as np
from mathutils import Matrix, Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
src, throw_path, fbx_path, tex_path = args[:4]
preview_path = args[4] if len(args) > 4 else None
data = json.load(open(throw_path))
import os
PREVIEW_FRAMES = [int(x) for x in os.environ.get("PREVIEW_FRAMES", "0,9,18,24,28,33,40,48,56,66").split(",")]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
scene = bpy.context.scene
fps = scene.render.fps

# ---------------------------------------------------------------- sling bones
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
hand = arm.data.edit_bones["RightHand"]
for b in data["sling_bones"]:
    eb = arm.data.edit_bones.new(b["name"])
    m = Matrix(b["matrix"])
    eb.head = Vector(b["head"])
    eb.tail = Vector(b["tail"])
    # Align the roll so the edit bone's matrix matches the one used for the keys.
    eb.align_roll(m.col[2].to_3d())
    eb.parent = hand
    eb.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")
for b in data["sling_bones"]:
    got = np.array(arm.data.bones[b["name"]].matrix_local)
    err = np.abs(got - np.array(b["matrix"])).max()
    if err > 1e-3:
        print("ROLL MISMATCH", b["name"], err)

# ---------------------------------------------------------------- sling mesh
ROPE = (0.82, 0.72, 0.5, 1)
LEATHER = (0.52, 0.33, 0.19, 1)
STONE = (0.62, 0.58, 0.52, 1)
n = len([b for b in data["sling_bones"] if b["name"].startswith("Sling") and b["name"][5:].isdigit()])
pts = [np.array(b["head"]) for b in data["sling_bones"][:n]] + [np.array(data["sling_bones"][n - 1]["tail"])]
pts = np.array(pts)
verts, faces, colors, groups = [], [], [], []
scale = data["scale"]


def add_tube(points, radius, bones_for_ring, sides=3):
    base = len(verts)
    for i, p in enumerate(points):
        d = points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]
        d /= np.linalg.norm(d)
        a = np.cross(d, [0.3, 0.2, 1.0]); a /= np.linalg.norm(a)
        b = np.cross(d, a)
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            verts.append(p + radius * (math.cos(ang) * a + math.sin(ang) * b))
            groups.append(bones_for_ring[i])
    for i in range(len(points) - 1):
        for k in range(sides):
            a0, a1 = base + i * sides + k, base + i * sides + (k + 1) % sides
            b0, b1 = a0 + sides, a1 + sides
            faces.append((a0, b0, a1)); colors.append(ROPE)
            faces.append((a1, b0, b1)); colors.append(ROPE)


def add_blob(center, size, seed, group, color):
    rng = np.random.default_rng(seed)
    dirs = np.array([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]], float)
    dirs *= 1 + rng.uniform(-0.15, 0.15, (6, 1))
    base = len(verts)
    for d in dirs:
        verts.append(center + d * size)
        groups.append(group)
    for f in [(0, 4, 2), (4, 1, 2), (1, 5, 2), (5, 0, 2), (4, 0, 3), (1, 4, 3), (5, 1, 3), (0, 5, 3)]:
        faces.append(tuple(base + i for i in f)); colors.append(color)


ring_bones = [f"Sling{min(i, n - 1)}" for i in range(n + 1)]
down = pts[-1] - pts[-2]; down /= np.linalg.norm(down)
side = np.array([1.0, 0, 0])
for s in (-1, 1):
    offs = (np.linspace(0, 1, n + 1) ** 2)[:, None] * side * 0.028 * scale * s
    add_tube(pts + offs, 0.008 * scale, ring_bones)
add_blob(pts[-1] + down * 0.015 * scale, np.array([0.05, 0.05, 0.03]) * scale, 3, "SlingPouch", LEATHER)
add_blob(pts[-1] + down * 0.02 * scale, np.array([0.037, 0.037, 0.035]) * scale, 5, "SlingStone", STONE)

me = bpy.data.meshes.new("Sling")
me.from_pydata([tuple(v) for v in verts], [], faces)
me.update()
col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
for poly, c in zip(me.polygons, colors):
    for li in poly.loop_indices:
        col.data[li].color = c
sling = bpy.data.objects.new("Sling", me)
scene.collection.objects.link(sling)
sling.parent = arm
for name in set(groups):
    sling.vertex_groups.new(name=name)
for i, g in enumerate(groups):
    sling.vertex_groups[g].add([i], 1.0, "REPLACE")
mod = sling.modifiers.new("Armature", "ARMATURE")
mod.object = arm
mat = bpy.data.materials.new("SlingMaterial")
me.materials.append(mat)

# ---------------------------------------------------------------- Throw action
act = bpy.data.actions.new("Throw")
if arm.animation_data is None:
    arm.animation_data_create()
arm.animation_data.action = act
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
frames = data["frames"]
period_frames = data["period"] * fps
prev = {}
count = len(frames)
for f in range(count + 1):
    keys = frames[f % count]
    fr = f * period_frames / count
    for name, (loc, q, sc) in keys.items():
        pb = arm.pose.bones[name]
        q = Quaternion(q)
        if name in prev and prev[name].dot(q) < 0:
            q.negate()
        prev[name] = q.copy()
        pb.location = loc
        pb.rotation_quaternion = q
        pb.scale = sc
        pb.keyframe_insert("location", frame=fr)
        pb.keyframe_insert("rotation_quaternion", frame=fr)
        pb.keyframe_insert("scale", frame=fr)


def fcurves_of(action):
    try:
        return list(action.fcurves)
    except AttributeError:
        out = []
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    out += list(bag.fcurves)
        return out


for fc in fcurves_of(act):
    for kp in fc.keyframe_points:
        kp.interpolation = "LINEAR"
act.use_fake_user = True
print("THROW keys", count, "frames", period_frames)

# ---------------------------------------------------------------- preview dump
if preview_path:
    out = {}
    dg = bpy.context.evaluated_depsgraph_get()
    tex = None
    for img in bpy.data.images:
        if img.size[0] > 0:
            tex = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4)[:, :, :3]
            break
    for f in PREVIEW_FRAMES:
        scene.frame_set(0)
        scene.frame_float = f * period_frames / count
        frame_int = int(math.floor(scene.frame_float))
        scene.frame_set(frame_int, subframe=scene.frame_float - frame_int)
        dg.update()
        tris, cols = [], []
        for obj in (body, sling):
            ev = obj.evaluated_get(dg)
            m = ev.to_mesh()
            m.calc_loop_triangles()
            co = np.array([v.co for v in m.vertices])
            co = (np.array(obj.matrix_world) @ np.c_[co, np.ones(len(co))].T).T[:, :3]
            if obj is body:
                uv = np.array([d.uv for d in m.uv_layers.active.data])
            for lt in m.loop_triangles:
                tris.append(co[list(lt.vertices)])
                if obj is body:
                    u = uv[list(lt.loops)].mean(0)
                    px = int(np.clip(u[0] % 1, 0, 0.999) * tex.shape[1])
                    py = int(np.clip(u[1] % 1, 0, 0.999) * tex.shape[0])
                    cols.append(tex[py, px])
                else:
                    cols.append(colors[lt.polygon_index][:3])
            ev.to_mesh_clear()
        out[f"t{f}"] = np.array(tris)
        out[f"c{f}"] = np.array(cols)
    np.savez_compressed(preview_path, **out)
    print("PREVIEW", preview_path)

# ---------------------------------------------------------------- export
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = tex_path
        img.file_format = "PNG"
        img.save()
        break
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=False, object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0, apply_scale_options="FBX_SCALE_UNITS", path_mode="STRIP", embed_textures=False,
    colors_type="SRGB")
print("WROTE", fbx_path)
