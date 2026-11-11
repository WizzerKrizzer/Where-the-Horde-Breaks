"""Builds the tamed mammoth: the Meshy "Crimson Mammoth" widened, rigged with a small quadruped
skeleton, skinned by region, and given four keyed clips. Run in Blender 5.2:

    blender -b --factory-startup -P build_mammoth.py -- model.fbx out.fbx out.png report.json

The model faces -Y with +Z up; its left is +X. All lengths below are in the model's units (it is
about 0.95 long), measured from the Meshy mesh (2026-11-10).

Clips (30 fps):
- Run: a looping charge, 16 frames. The feet are placed by IK on a stance/swing path and baked,
  so they stay planted while in contact; the report gives the ground distance per cycle.
- Scoop: two scoops, 24 frames each (StompSeconds 0.8 in BeastCharge): dip the head, then swing
  the tusks up and to one side; the blow lands at frame 6 (ScoopHitSeconds 0.2). Left, then right.
- Rage: 36 frames: shakes its head, trunk up, stamps its front feet.
- Death: 54 frames, not looping: the front legs buckle and it rolls onto its side.
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Matrix, Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
model_path, out_fbx, out_png, report_path = args[:4]
FPS = 30
WIDEN = 1.375  # it was too narrow from above (user, 2026-11-09)
GROUND = -0.307

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=model_path)
scene.render.fps = FPS  # the import sets the file's own rate (24)
body = next(o for o in scene.objects if o.type == "MESH")
body.name = "Mammoth"
mesh = body.data
# Bake the object transform, then widen.
mesh.transform(body.matrix_world)
body.matrix_world = Matrix.Identity(4)
for v in mesh.vertices:
    v.co.x *= WIDEN
mesh.update()
report = {"widen": WIDEN}

LEG_X = 0.07 * WIDEN
LEGS = {  # name: (x, y, top z)
    "FrontL": (LEG_X, -0.09, 0.0),
    "FrontR": (-LEG_X, -0.09, 0.0),
    "BackL": (LEG_X, 0.29, 0.02),
    "BackR": (-LEG_X, 0.29, 0.02),
}
KNEE_Z, ANKLE_Z = -0.17, -0.285

# ---------------------------------------------------------------- skeleton
arm_data = bpy.data.armatures.new("MammothRig")
arm = bpy.data.objects.new("MammothRig", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones


def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.roll = 0.0
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b


bone("Root", (0, 0, GROUND), (0, -0.1, GROUND))
bone("Hips", (0, 0.30, 0.02), (0, 0.12, 0.05), "Root")
bone("Spine", (0, 0.12, 0.05), (0, -0.03, 0.08), "Hips", True)
bone("Chest", (0, -0.03, 0.08), (0, -0.16, 0.10), "Spine", True)
bone("Neck", (0, -0.16, 0.10), (0, -0.22, 0.12), "Chest", True)
bone("Head", (0, -0.22, 0.12), (0, -0.30, 0.04), "Neck", True)
bone("Trunk1", (0, -0.31, 0.02), (0, -0.335, -0.06), "Head")
bone("Trunk2", (0, -0.335, -0.06), (0, -0.335, -0.15), "Trunk1", True)
bone("Trunk3", (0, -0.335, -0.15), (0, -0.32, -0.27), "Trunk2", True)
bone("Tail1", (0, 0.40, 0.04), (0, 0.43, -0.04), "Hips")
bone("Tail2", (0, 0.43, -0.04), (0, 0.45, -0.16), "Tail1", True)
for name, (x, y, top) in LEGS.items():
    parent = "Chest" if name.startswith("Front") else "Hips"
    # A slight forward bend at rest tells the IK which way the knee goes.
    bone(name + "Upper", (x, y, top), (x, y - 0.012, KNEE_Z), parent)
    bone(name + "Lower", (x, y - 0.012, KNEE_Z), (x, y, ANKLE_Z), name + "Upper", True)
    bone(name + "Foot", (x, y, ANKLE_Z), (x, y - 0.025, GROUND), name + "Lower", True)
bpy.ops.object.mode_set(mode="OBJECT")
BONES = [b.name for b in arm_data.bones]

# ---------------------------------------------------------------- skinning by region
segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones}


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
island_of = [-1] * len(bm.verts)
islands = []
for v in bm.verts:
    if island_of[v.index] >= 0:
        continue
    stack, comp = [v], []
    island_of[v.index] = len(islands)
    while stack:
        x = stack.pop()
        comp.append(x.index)
        for e in x.link_edges:
            o = e.other_vert(x)
            if island_of[o.index] < 0:
                island_of[o.index] = len(islands)
                stack.append(o)
    islands.append(comp)
bm.free()
main = max(range(len(islands)), key=lambda i: len(islands[i]))


TRUNK = ["Trunk1", "Trunk2", "Trunk3"]
TRUNK_JOINTS = [(0.02, "Head"), (-0.02, "Trunk1"), (-0.10, "Trunk2"), (-0.20, "Trunk3")]


def trunk_weights(z):
    """The trunk follows its chain by height: each bone owns a band, blended linearly at the joints,
    so no trunk vertex is left on a body bone to stretch into a sheet when it curls."""
    for (z0, a), (z1, b) in zip(TRUNK_JOINTS, TRUNK_JOINTS[1:]):
        if z >= z1:
            t = (z0 - z) / (z0 - z1)
            return {a: 1.0 - t, b: t} if t > 0 else {a: 1.0}
    return {"Trunk3": 1.0}


def body_bones(p):
    return ["Hips", "Spine", "Chest", "Neck"]


def candidates(p):
    x, y, z = p
    if y < -0.36 or (y < -0.285 and abs(x) > 0.035 * WIDEN and -0.13 < z < 0.0):
        return {"Head": 1.0}  # tusks
    # The trunk: everything hanging under the face, its tip curling back included.
    if z < 0.02 and y < -0.24 and abs(x) < 0.05 * WIDEN:
        return trunk_weights(z)
    if y > 0.39 and abs(x) < 0.04 * WIDEN and z < 0.08:
        return ["Hips", "Tail1", "Tail2"]
    if z < -0.13:
        for name, (lx, ly, top) in LEGS.items():
            if math.hypot(x - lx, y - ly) < 0.075:
                parent = "Chest" if name.startswith("Front") else "Hips"
                chain = [name + "Upper", name + "Lower", name + "Foot"]
                return chain + ([parent] if z > -0.19 else [])
    if y < -0.15 and z > -0.06:
        return ["Chest", "Neck", "Head"]
    return body_bones(p)


def weights_for(p, names):
    if isinstance(names, dict):
        return names
    d = {n: seg_dist(p, *segs[n]) for n in names}
    w = {n: 1.0 / max(dd, 1e-4) ** 4 for n, dd in d.items()}
    top = sorted(w.items(), key=lambda t: -t[1])[:3]
    total = sum(v for _, v in top)
    return {n: v / total for n, v in top}


for name in BONES:
    body.vertex_groups.new(name=name)
for i, comp in enumerate(islands):
    if i == main:
        continue
    # Loose pieces (ears, halter, belly ropes) ride rigidly on the nearest head or body bone.
    c = sum((mesh.vertices[k].co for k in comp), Vector()) / len(comp)
    names = ["Head", "Neck"] if c.y < -0.12 else ["Hips", "Spine", "Chest"]
    nearest = min(names, key=lambda n: seg_dist(c, *segs[n]))
    body.vertex_groups[nearest].add(comp, 1.0, "REPLACE")
for k in islands[main]:
    p = mesh.vertices[k].co
    for n, w in weights_for(p, candidates(p)).items():
        body.vertex_groups[n].add([k], w, "REPLACE")

body.parent = arm
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm

# ---------------------------------------------------------------- posing helpers
REST = {b.name: b.matrix_local.to_quaternion() for b in arm_data.bones}
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def world_rot(name, *turns):
    """A pose rotation for `name` from rotations about armature axes, in degrees, applied in order."""
    q = Quaternion()
    for axis, deg in turns:
        q = Quaternion(axis, math.radians(deg)) @ q
    r = REST[name]
    return r.inverted() @ q @ r


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def ease(a, b, t):
    return a + (b - a) * smooth(t)


# IK: one target per leg at the ankle, and a pole in front so every knee (the front legs' wrists
# too) bends forward, the foot swinging back under the body as it lifts.
targets, iks = {}, {}
for name, (x, y, top) in LEGS.items():
    t = bpy.data.objects.new("IK_" + name, None)
    scene.collection.objects.link(t)
    t.location = (x, y, ANKLE_Z)
    targets[name] = t
    pole = bpy.data.objects.new("Pole_" + name, None)
    scene.collection.objects.link(pole)
    pole.location = (x, y - 0.5, KNEE_Z)
    c = arm.pose.bones[name + "Lower"].constraints.new("IK")
    c.target = t
    c.pole_target = pole
    c.chain_count = 2
    c.use_tail = True
    iks[name] = c
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"

# The pole angle that puts the knee towards the pole depends on the bones' rolls: try the four.
for name, (x, y, top) in LEGS.items():
    targets[name].location = (x, y + 0.04, ANKLE_Z + 0.08)
best = None
for deg in (-180, -90, 0, 90):
    for c in iks.values():
        c.pole_angle = math.radians(deg)
    bpy.context.view_layer.update()
    knee = sum(arm.pose.bones[n + "Lower"].head.y for n in LEGS)
    if best is None or knee < best[1]:
        best = (deg, knee)
for c in iks.values():
    c.pole_angle = math.radians(best[0])
report["pole_angle"] = best[0]


def pose_frame(pose, feet):
    """pose: {bone: quaternion} plus 'RootLoc'; feet: {leg: (dy, dz)} offsets of the ankle targets."""
    for pb in arm.pose.bones:
        pb.rotation_quaternion = pose.get(pb.name, Quaternion())
        pb.location = (0, 0, 0)
    arm.pose.bones["Root"].location = pose.get("RootLoc", Vector())
    for name, (x, y, top) in LEGS.items():
        dx, dy, dz = feet.get(name, (0.0, 0.0, 0.0))
        targets[name].location = (x + dx, y + dy, ANKLE_Z + dz)


def bake(frames, ik=True):
    """Evaluates each (pose, feet) frame with the IK on and keeps the resulting local bone poses."""
    out = []
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.influence = 1.0 if ik else 0.0
    for pose, feet in frames:
        pose_frame(pose, feet)
        bpy.context.view_layer.update()
        frame = {}
        for pb in arm.pose.bones:
            m = arm.convert_space(pose_bone=pb, matrix=pb.matrix, from_space="POSE", to_space="LOCAL")
            loc, q, _ = m.decompose()
            frame[pb.name] = (loc, q)
        out.append(frame)
    return out


# Root moves are in the Root bone's own space; build them as armature-space vectors.
ROOT_REST = arm_data.bones["Root"].matrix_local.to_3x3()


def root_loc(v):
    return ROOT_REST.inverted() @ Vector(v)


# ---------------------------------------------------------------- Run
RUN_N = 16
STRIDE = 0.26  # ankle travel per stance
DUTY = 0.4
LIFT = 0.05
PHASE = {"BackL": 0.0, "FrontL": 0.25, "BackR": 0.5, "FrontR": 0.75}  # lateral sequence


def run_frames():
    frames = []
    for f in range(RUN_N):
        t = f / RUN_N
        feet = {}
        for leg, o in PHASE.items():
            u = (t + o) % 1.0
            if u < DUTY:
                dy, dz = STRIDE * (u / DUTY - 0.5), 0.0
            else:
                s = (u - DUTY) / (1 - DUTY)
                dy = STRIDE * (0.5 - smooth(s))
                dz = LIFT * math.sin(math.pi * s)
            feet[leg] = (0.0, dy, dz)
        w = 2 * math.pi * t
        pose = {
            "RootLoc": root_loc((0.004 * math.sin(w), 0.0, -0.012 + 0.009 * math.cos(2 * w))),
            "Root": world_rot("Root", (Y, 2.0 * math.sin(w)), (X, 1.5 * math.sin(2 * w + 0.6))),
            "Hips": world_rot("Hips", (Z, 3.0 * math.sin(w))),
            "Chest": world_rot("Chest", (Z, -3.0 * math.sin(w)), (X, -1.5 * math.cos(2 * w))),
            # Charging: head a little low, tusks forward, bobbing behind the body.
            "Neck": world_rot("Neck", (X, 6.0 + 2.5 * math.sin(2 * w - 1.0))),
            "Head": world_rot("Head", (X, 6.0 + 3.0 * math.sin(2 * w - 1.5)), (Z, 2.0 * math.sin(w - 0.8))),
            # The trunk hangs and swings a little behind the bob, forward with the stride.
            "Trunk1": world_rot("Trunk1", (X, -4 + 4 * math.sin(2 * w - 2.0)), (Y, 5 * math.sin(w - 1.0))),
            "Trunk2": world_rot("Trunk2", (X, -5 + 6 * math.sin(2 * w - 2.6)), (Y, 7 * math.sin(w - 1.6))),
            "Trunk3": world_rot("Trunk3", (X, -6 + 8 * math.sin(2 * w - 3.2)), (Y, 9 * math.sin(w - 2.2))),
            "Tail1": world_rot("Tail1", (X, -20.0), (Y, 15 * math.sin(w - 1.2))),
            "Tail2": world_rot("Tail2", (X, -10.0), (Y, 20 * math.sin(w - 2.0))),
        }
        frames.append((pose, feet))
    return frames


# ---------------------------------------------------------------- Scoop
SCOOP_N = 24
SCOOP_HIT = 6


def scoop_frames():
    frames = []
    for side in (1, -1):  # +X is its left
        for f in range(SCOOP_N):
            # 0 -> 1 dip by frame 4, snap up by the hit, hold, ease back from frame 13.
            if f <= 4:
                dip, up = smooth(f / 4), 0.0
            elif f <= SCOOP_HIT + 1:
                dip, up = 1.0 - smooth((f - 4) / 3), smooth((f - 4) / 3)
            elif f <= 12:
                dip, up = 0.0, 1.0
            else:
                dip, up = 0.0, 1.0 - smooth((f - 12) / (SCOOP_N - 12))
            yaw = side * 16 * up
            pose = {
                "RootLoc": root_loc((0.0, -0.012 * dip - 0.02 * up, -0.012 * dip + 0.004 * up)),
                "Root": world_rot("Root", (X, 3.0 * dip - 3.0 * up), (Y, -side * 2.5 * up)),
                "Chest": world_rot("Chest", (X, 4.0 * dip - 4.0 * up), (Z, yaw * 0.3)),
                "Neck": world_rot("Neck", (X, 12 * dip - 14 * up), (Z, yaw * 0.4)),
                "Head": world_rot("Head", (X, 20 * dip - 30 * up), (Z, yaw * 0.6), (Y, -side * 10 * up)),
                "Trunk1": world_rot("Trunk1", (X, 6 * dip - 18 * up)),
                "Trunk2": world_rot("Trunk2", (X, 8 * dip - 24 * up)),
                "Trunk3": world_rot("Trunk3", (X, 8 * dip - 28 * up)),
                "Tail1": world_rot("Tail1", (X, -15.0 - 10 * up), (Y, side * 10 * up)),
                "Tail2": world_rot("Tail2", (X, -10.0)),
            }
            frames.append((pose, {}))
    return frames


# ---------------------------------------------------------------- Rage
RAGE_N = 36


def rage_frames():
    frames = []
    for f in range(RAGE_N):
        t = f / RAGE_N
        shake = math.sin(2 * math.pi * 3 * t) * (1 - smooth((t - 0.75) / 0.25))
        rise = smooth(t / 0.2) * (1 - smooth((t - 0.8) / 0.2))
        jitter = 0.002 * math.sin(f * 2.7) * rise
        feet = {}
        for leg, at in (("FrontL", 10), ("FrontR", 22)):
            s = (f - (at - 6)) / 8.0  # up for 6 frames, stamp down at `at`
            feet[leg] = (0.0, -0.01 * math.sin(math.pi * max(0, min(1, s))), 0.06 * math.sin(math.pi * max(0.0, min(1.0, s))) if 0 <= s <= 1 else 0.0)
        pose = {
            "RootLoc": root_loc((jitter, 0.0, 0.006 * rise + jitter)),
            "Root": world_rot("Root", (X, -4.0 * rise), (Y, 3.0 * shake)),
            "Chest": world_rot("Chest", (X, -3.0 * rise)),
            "Neck": world_rot("Neck", (X, -10 * rise), (Z, 12 * shake)),
            "Head": world_rot("Head", (X, -14 * rise), (Z, 18 * shake), (Y, 10 * shake)),
            "Trunk1": world_rot("Trunk1", (X, -20 * rise), (Y, 10 * shake)),
            "Trunk2": world_rot("Trunk2", (X, -28 * rise), (Y, 12 * shake)),
            "Trunk3": world_rot("Trunk3", (X, -32 * rise), (Y, 14 * shake)),
            "Tail1": world_rot("Tail1", (X, -35 * rise), (Y, 20 * shake)),
            "Tail2": world_rot("Tail2", (X, -20 * rise)),
        }
        frames.append((pose, feet))
    return frames


# ---------------------------------------------------------------- Death (FK, the IK is off)
DEATH_N = 54


def death_frames():
    frames = []
    for f in range(DEATH_N):
        kneel = smooth(f / 16)  # the front legs buckle
        roll = smooth((f - 12) / 28)  # it rolls onto its left side
        settle = smooth((f - 40) / 14)
        pose = {
            "RootLoc": root_loc((0.24 * roll, 0.0, -0.06 * kneel + 0.17 * roll - 0.015 * settle)),
            "Root": world_rot("Root", (X, 9 * kneel * (1 - roll)), (Y, 84 * roll + 4 * settle)),
            "Chest": world_rot("Chest", (X, 6 * kneel)),
            "Neck": world_rot("Neck", (X, 12 * kneel + 6 * roll), (Z, -8 * roll)),
            "Head": world_rot("Head", (X, 14 * kneel + 10 * roll), (Y, 10 * roll)),
            "Trunk1": world_rot("Trunk1", (X, -8 * roll), (Y, 25 * roll)),
            "Trunk2": world_rot("Trunk2", (X, -10 * roll), (Y, 25 * roll)),
            "Trunk3": world_rot("Trunk3", (X, -10 * roll), (Y, 20 * roll)),
            "Tail1": world_rot("Tail1", (X, -5.0)),
        }
        for leg in LEGS:
            front = leg.startswith("Front")
            left = leg.endswith("L")
            fold = kneel if front else smooth((f - 6) / 20)
            # Legs fold (upper forward, lower back), then go limp and splay a little as it lies.
            pose[leg + "Upper"] = world_rot(leg + "Upper", (X, (-35 if front else -20) * fold * (1 - 0.5 * roll)),
                                            (Y, (6 if left else -6) * roll))
            pose[leg + "Lower"] = world_rot(leg + "Lower", (X, (70 if front else 45) * fold * (1 - 0.6 * roll)))
            pose[leg + "Foot"] = world_rot(leg + "Foot", (X, 20 * fold))
        frames.append((pose, {}))
    return frames


def write_action(name, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    slot = act.slots.new(id_type="OBJECT", name=arm.name)
    arm.animation_data.action_slot = slot
    n = len(frames)
    for b in BONES:
        base = f'pose.bones["{b}"]'
        for i in range(3):
            fc = act.fcurve_ensure_for_datablock(arm, base + ".location", index=i)
            fc.keyframe_points.add(n)
            fc.keyframe_points.foreach_set("co", [v for f in range(n) for v in (f, frames[f][b][0][i])])
        prev = None
        qs = []
        for f in range(n):
            q = frames[f][b][1].copy()
            if prev is not None and q.dot(prev) < 0:
                q.negate()
            qs.append(q)
            prev = q
        for i in range(4):
            fc = act.fcurve_ensure_for_datablock(arm, base + ".rotation_quaternion", index=i)
            fc.keyframe_points.add(n)
            fc.keyframe_points.foreach_set("co", [v for f in range(n) for v in (f, qs[f][i])])
    for fc in act.fcurves if hasattr(act, "fcurves") else []:
        for k in fc.keyframe_points:
            k.interpolation = "LINEAR"
    return act


def loop(frames):
    return frames + [frames[0]]


clips = {
    "Run": loop(bake(run_frames())),
    "Scoop": loop(bake(scoop_frames())),
    "Rage": loop(bake(rage_frames())),
    "Death": bake(death_frames(), ik=False),
}

# Remove the IK setup before writing the baked clips.
for pb in arm.pose.bones:
    for c in list(pb.constraints):
        pb.constraints.remove(c)
    pb.rotation_quaternion = Quaternion()
    pb.location = (0, 0, 0)
for t in [o for o in scene.objects if o.type == "EMPTY"]:
    bpy.data.objects.remove(t)
for name, frames in clips.items():
    write_action(name, frames)
    report[name + "_frames"] = len(frames)
arm.animation_data.action = None

report["run_cycle_seconds"] = RUN_N / FPS
report["run_ground_per_cycle"] = STRIDE / DUTY
report["run_ground_speed"] = STRIDE / DUTY / (RUN_N / FPS)
report["scoop_hit_frames"] = [SCOOP_HIT, SCOOP_N + SCOOP_HIT]
report["height"] = max(v.co.z for v in mesh.vertices) - GROUND
report["length"] = max(v.co.y for v in mesh.vertices) - min(v.co.y for v in mesh.vertices)
report["width"] = 2 * max(abs(v.co.x) for v in mesh.vertices)

# Texture beside the FBX.
img = next((i for i in bpy.data.images if i.size[0] > 0), None)
if img:
    img.filepath_raw = out_png
    img.file_format = "PNG"
    img.save()

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0, apply_scale_options="FBX_SCALE_UNITS", path_mode="STRIP", mesh_smooth_type="FACE")
with open(report_path, "w") as f:
    json.dump(report, f, indent=1)
print("MAMMOTH", json.dumps(report))
