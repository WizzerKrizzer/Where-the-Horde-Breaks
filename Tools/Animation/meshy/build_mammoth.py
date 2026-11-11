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
- Idle: 150 frames, looping (user, 2026-11-11): waiting in its pen it breathes, lowers its head
  to graze, swings its trunk to the ground and back, lifts its head again and looks about.
- DeathL, DeathR: 96 frames, not looping (user, 2026-11-11): it rears up on its hind legs, paddles
  its front legs, throws its head and trunk about, then comes down and crashes over onto its left
  (DeathL) or right side (DeathR), a little diagonally. The game turns it a little at random too.
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


# ---------------------------------------------------------------- Idle
IDLE_N = 150


def idle_frames():
    frames = []
    for f in range(IDLE_N):
        t = f / IDLE_N
        breath = math.sin(2 * math.pi * 3 * t)  # three slow breaths a loop
        # Head down to graze from 0.15 to 0.6 of the loop, up and looking about after.
        graze = smooth((t - 0.1) / 0.12) * (1 - smooth((t - 0.58) / 0.12))
        look = math.sin(2 * math.pi * (t - 0.7) / 0.3) * smooth((t - 0.68) / 0.06) * (1 - smooth((t - 0.94) / 0.06))
        # The trunk sweeps the ground while it grazes, and swings idly otherwise.
        sweep = math.sin(2 * math.pi * 4 * t) * graze
        idle_swing = math.sin(2 * math.pi * 2 * t) * (1 - graze)
        pose = {
            "RootLoc": root_loc((0.0, 0.0, 0.002 * breath - 0.006 * graze)),
            "Root": world_rot("Root", (X, 4.0 * graze)),
            "Chest": world_rot("Chest", (X, 1.0 * breath + 4.0 * graze)),
            "Neck": world_rot("Neck", (X, 24 * graze), (Z, 8 * look)),
            "Head": world_rot("Head", (X, 26 * graze - 3 * (1 - graze) * breath), (Z, 10 * look + 4 * sweep)),
            "Trunk1": world_rot("Trunk1", (X, 6 * graze), (Y, 8 * sweep + 6 * idle_swing)),
            "Trunk2": world_rot("Trunk2", (X, 10 * graze), (Y, 12 * sweep + 9 * idle_swing)),
            "Trunk3": world_rot("Trunk3", (X, 14 * graze - 10 * (1 - graze) * max(0.0, idle_swing)), (Y, 16 * sweep + 12 * idle_swing)),
            "Tail1": world_rot("Tail1", (X, -15.0), (Y, 18 * math.sin(2 * math.pi * 5 * t))),
            "Tail2": world_rot("Tail2", (X, -8.0), (Y, 22 * math.sin(2 * math.pi * 5 * t - 0.8))),
        }
        frames.append((pose, {}))
    return frames


# ---------------------------------------------------------------- Death (FK, the IK is off)
DEATH_N = 96
DEATH_IMPACT = 70  # the frame it hits the ground (the game finds it again from the clip)
HIND_PIVOT = Vector((0.0, 0.29, GROUND))  # it rears up about its hind feet
SIDE_EDGE = 0.2  # half its width: it tips over its own side


def pivoted(rotation, pivot, then=None, then_pivot=None):
    """Root location and rotation that turn the body by `rotation` about `pivot` (armature space),
    then by `then` about `then_pivot`: the Root bone itself turns about its head."""
    head = arm_data.bones["Root"].head_local
    q = rotation if then is None else then @ rotation
    # p -> P2 + R2 (P1 + R1 (p - P1) - P2), written as head + q (p - head) + t.
    if then is None:
        t = pivot - rotation @ pivot - head + q @ head
    else:
        t = then_pivot - then @ then_pivot + then @ pivot - q @ pivot - head + q @ head
    return q, root_loc(t)


def death_frames(side):
    """side +1: it falls onto its left (+X), -1: onto its right."""
    frames = []
    for f in range(DEATH_N):
        rear = smooth(f / 22) * (1 - smooth((f - 54) / 16))
        flail = smooth((f - 12) / 10) * (1 - smooth((f - 52) / 10))
        fall = smooth((f - 54) / (DEATH_IMPACT - 54))
        settle = smooth((f - DEATH_IMPACT) / (DEATH_N - DEATH_IMPACT - 8))
        bounce = math.sin(math.pi * min(1.0, max(0.0, (f - DEATH_IMPACT) / 8))) * (1 - settle)
        w = 2 * math.pi * f / 10  # the front legs paddle three times a second
        h = 2 * math.pi * f / 16  # the head throws about twice a second
        pitch = Quaternion(X, math.radians(-38 * rear + 2 * bounce))
        roll = Quaternion(Z, math.radians(side * 22 * fall)) @ Quaternion(Y, math.radians(side * (88 * fall - 3 * bounce)))
        edge = Vector((side * SIDE_EDGE, 0.0, GROUND))
        q, loc = pivoted(pitch, HIND_PIVOT, roll, edge)
        world_q = q
        pose = {
            "RootLoc": loc,
            "Root": REST["Root"].inverted() @ world_q @ REST["Root"],
            "Chest": world_rot("Chest", (X, -6 * rear + 4 * fall)),
            "Neck": world_rot("Neck", (X, -16 * rear + 10 * fall), (Z, 14 * flail * math.sin(h))),
            "Head": world_rot("Head", (X, -14 * rear + 12 * fall), (Z, 20 * flail * math.sin(h - 0.6)),
                              (Y, 10 * flail * math.sin(h + 0.9) + side * 12 * fall)),
            # The trunk goes up and lashes about, then falls limp.
            "Trunk1": world_rot("Trunk1", (X, -30 * rear + 6 * fall), (Y, 24 * flail * math.sin(h - 1.2) + side * 20 * fall)),
            "Trunk2": world_rot("Trunk2", (X, -34 * rear + 4 * fall), (Y, 28 * flail * math.sin(h - 1.8) + side * 20 * fall)),
            "Trunk3": world_rot("Trunk3", (X, -38 * rear), (Y, 32 * flail * math.sin(h - 2.4) + side * 15 * fall)),
            "Tail1": world_rot("Tail1", (X, -30 * rear - 5 * fall)),
            "Tail2": world_rot("Tail2", (X, -15 * rear)),
        }
        for leg in LEGS:
            front = leg.startswith("Front")
            left = leg.endswith("L")
            if front:
                # Lifted and paddling back and forth, the two out of step; limp once down.
                phase = 0.0 if left else math.pi
                swing = math.sin(w + phase)
                upper = -40 * rear - 30 * flail * swing
                lower = 55 * rear + 35 * flail * (0.5 + 0.5 * math.sin(w + phase - 1.2))
            else:
                # Hind legs crouch a little under the weight.
                upper = -10 * rear
                lower = 18 * rear
            limp = fall
            pose[leg + "Upper"] = world_rot(leg + "Upper", (X, upper * (1 - limp) + (-15 if front else -8) * limp),
                                            (Y, (8 if left else -8) * limp))
            pose[leg + "Lower"] = world_rot(leg + "Lower", (X, lower * (1 - limp) + 20 * limp))
            pose[leg + "Foot"] = world_rot(leg + "Foot", (X, 15 * rear * (1 - limp) + 10 * limp))
        frames.append((pose, {}))
    return frames


def render_preview(directory, clip_names):
    """Side and three-quarter stills of each clip every 6 frames, to check a clip by eye."""
    os.makedirs(directory, exist_ok=True)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 1.6
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name in clip_names:
        arm.animation_data.action = bpy.data.actions[name]
        arm.animation_data.action_slot = bpy.data.actions[name].slots[0]
        n = int(bpy.data.actions[name].frame_range[1]) + 1
        for view, location in (("side", (2.0, 0.0, 0.2)), ("front", (1.3, -1.6, 0.6))):
            cam.location = location
            direction = Vector((0.0, 0.05, -0.05)) - Vector(location)
            cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
            for f in range(0, n, 6):
                scene.frame_set(f)
                scene.render.filepath = os.path.join(directory, f"{name}_{view}_{f:03d}.png")
                bpy.ops.render.render(write_still=True)
    arm.animation_data.action = None


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
    "Idle": loop(bake(idle_frames())),
    "DeathL": bake(death_frames(1), ik=False),
    "DeathR": bake(death_frames(-1), ik=False),
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
if os.environ.get("MAMMOTH_PREVIEW"):
    render_preview(os.environ["MAMMOTH_PREVIEW"], os.environ.get("MAMMOTH_PREVIEW_CLIPS", "DeathL,DeathR").split(","))

report["death_impact_frame"] = DEATH_IMPACT
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
