"""Blender: build the spear hunter (Meshy model, Mixamo rig) with his tower clips.

blender -b --factory-startup -P build_hunter.py -- skin.fbx breathing.fbx ki_dir out.fbx out.png mirror

- skin.fbx: the hunter as Mixamo exported him with skin (any clip).
- breathing.fbx: the Mixamo "Breathing Idle" clip (same rig), used as is.
- ki_dir: Kevin Iglesias "Human Throwing Animations FREE" clips (HumanM@*.fbx). They are
  retargeted onto the Mixamo bones: every mapped bone takes the source bone's world rotation
  change since its rest pose, after its rest direction is swung onto the source's.
- mirror 1 flips the model left to right (lion skin to the left shoulder, spears to the right,
  where the right hand reaches for them). The rig is symmetric, so only the mesh and its
  vertex group names change.

The spear bundle and the hides are taken off the arm bones so they ride on the back instead
of stretching when an arm moves. A RightHandProp bone follows the source's hand prop bone; the
spear and atlatl are drawn on it.

Actions: Idle, Draw, Aim, Throw (single clips) and Cycle (Draw, Aim, Throw blended into one).
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Matrix, Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
skin_path, breathing_path, ki_dir, fbx_path, tex_path, mirror = args[:6]
mirror = mirror == "1"
FPS = 30

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS


def import_fbx(path):
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.data.objects if o not in before]
    acts = [a for a in bpy.data.actions if a not in actions_before]
    return objs, acts


def remove(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def rot3(m):
    r = m.to_3x3()
    for i in range(3):
        col = Vector((r[0][i], r[1][i], r[2][i])).normalized()
        r[0][i], r[1][i], r[2][i] = col
    return r


def swing(a, b):
    a, b = a.normalized(), b.normalized()
    return a.rotation_difference(b).to_matrix()


# ---------------------------------------------------------------- the hunter
objs, acts = import_fbx(skin_path)
arm = next(o for o in objs if o.type == "ARMATURE")
body = next(o for o in objs if o.type == "MESH")
arm.name = "SpearHunter"
for a in acts:
    bpy.data.actions.remove(a)
if arm.animation_data:
    arm.animation_data.action = None
P = "mixamorig:"
W = arm.matrix_world.copy()
W_rot = rot3(W)
report = {}

# Mesh coordinates are world coordinates here (the mesh has an identity world matrix).
mesh = body.data
groups = {g.name: g.index for g in body.vertex_groups}


def bone_world(name):
    b = arm.data.bones[P + name]
    return W @ b.head_local, W @ b.tail_local


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


def set_weights(v, weights):
    for g in list(v.groups):
        body.vertex_groups[g.group].remove([v.index])
    for name, w in weights.items():
        if w > 1e-4:
            body.vertex_groups[name].add([v.index], w, "REPLACE")


# Spears: the loose pieces up behind the shoulders ride rigidly on the upper spine.
bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for v in bm.verts:
    if v.index in seen:
        continue
    stack, comp = [v], []
    seen.add(v.index)
    while stack:
        x = stack.pop()
        comp.append(x.index)
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    islands.append(comp)
bm.free()
spear_verts = []
for comp in islands:
    co = [mesh.vertices[i].co for i in comp]
    if len(comp) > 60 and min(c.y for c in co) > 0.06 and min(c.z for c in co) > 0.24:
        spear_verts += comp
for i in spear_verts:
    set_weights(mesh.vertices[i], {P + "Spine2": 1.0})
report["spear_vertices"] = len(spear_verts)

# Hides and bundle: arm-chain weight on anything that is not the arm itself moves to that
# side's shoulder, so a raised arm no longer drags the pelt and quiver into a long tube.
ARM_RADIUS = 0.05
moved = 0
for side in ("Left", "Right"):
    # Every bone of the arm down to the finger tips: a finger tip lies past the hand bone, and
    # moving it to the shoulder drew a long sliver from the hand like a stick.
    segs = [bone_world(bn.name[len(P):]) for bn in arm.data.bones
            if bn.name.startswith(P + side) and any(k in bn.name for k in ("Arm", "Hand"))]
    chain = [g for g in groups if g.startswith(P + side) and any(k in g for k in ("Arm", "Hand"))]
    chain_idx = {groups[g] for g in chain}
    shoulder = P + side + "Shoulder"
    for v in mesh.vertices:
        if v.index in spear_verts:
            continue
        w = {body.vertex_groups[g.group].name: g.weight for g in v.groups}
        on_chain = sum(w.get(g, 0.0) for g in chain)
        if on_chain <= 0.0:
            continue
        d = min(seg_dist(v.co, a, b) for a, b in segs)
        if d <= ARM_RADIUS:
            continue
        for g in chain:
            w.pop(g, None)
        w[shoulder] = w.get(shoulder, 0.0) + on_chain
        set_weights(v, w)
        moved += 1
report["vertices_taken_off_arms"] = moved

if mirror:
    for v in mesh.vertices:
        v.co.x = -v.co.x
    for g in body.vertex_groups:
        if "Left" in g.name:
            g.name = g.name.replace("Left", "@@")
    for g in body.vertex_groups:
        if "Right" in g.name:
            g.name = g.name.replace("Right", "Left")
    for g in body.vertex_groups:
        if "@@" in g.name:
            g.name = g.name.replace("@@", "Right")
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

# The hand prop bone: same offset from the hand as the source rig's, scaled to this rig.
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb_hand = arm.data.edit_bones[P + "RightHand"]
prop = arm.data.edit_bones.new(P + "RightHandProp")
prop.parent = eb_hand
prop.use_connect = False
hand_head, hand_tail = eb_hand.head.copy(), eb_hand.tail.copy()
prop.head = hand_head + (hand_tail - hand_head) * 1.4
prop.tail = prop.head + (hand_tail - hand_head)
prop.roll = eb_hand.roll
# The spear bundle's axis, for drawing a spear out of it: SpearBundle runs from the bundle's
# bottom to its top (tips up), SpearBundleTip sits at the top. Both ride on the upper spine.
pts = np.array([tuple(mesh.vertices[i].co) for i in spear_verts])
centre = pts.mean(axis=0)
axis = np.linalg.svd(pts - centre)[2][0]
if axis[2] < 0:
    axis = -axis
along = (pts - centre) @ axis
to_arm = W.inverted()
base = to_arm @ Vector(centre + axis * along.min())
top = to_arm @ Vector(centre + axis * along.max())
eb_spine = arm.data.edit_bones[P + "Spine2"]
bundle = arm.data.edit_bones.new(P + "SpearBundle")
bundle.parent = eb_spine
bundle.use_connect = False
bundle.head, bundle.tail = base, top
bundle_tip = arm.data.edit_bones.new(P + "SpearBundleTip")
bundle_tip.parent = bundle
bundle_tip.use_connect = False
bundle_tip.head, bundle_tip.tail = top, top + (top - base) * 0.1
report["bundle_length"] = float(along.max() - along.min())
bpy.ops.object.mode_set(mode="OBJECT")

REST = {b.name: b.matrix_local.copy() for b in arm.data.bones}
PARENT = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
ORDER = [b.name for b in arm.data.bones]  # parents come first


def rest_world_rot(name):
    return W_rot @ REST[name].to_3x3()


# Floor and hip height of the hunter, in world units.
floor_t = min((W @ arm.data.bones[P + n].head_local).z for n in ("LeftToeBase", "RightToeBase", "LeftToe_End", "RightToe_End"))
hips_t = W @ REST[P + "Hips"].translation
report["hunter_hip_height"] = hips_t.z - floor_t

# ---------------------------------------------------------------- Kevin Iglesias retarget
KI_MAP = {"B-hips": "Hips", "B-spine": "Spine", "B-chest": "Spine2", "B-neck": "Neck", "B-head": "Head"}
for s, side in ((".L", "Left"), (".R", "Right")):
    KI_MAP.update({f"B-shoulder{s}": side + "Shoulder", f"B-upperArm{s}": side + "Arm", f"B-forearm{s}": side + "ForeArm",
                   f"B-hand{s}": side + "Hand", f"B-thigh{s}": side + "UpLeg", f"B-shin{s}": side + "Leg",
                   f"B-foot{s}": side + "Foot", f"B-toe{s}": side + "ToeBase"})
    for ki, mx in (("indexFinger", "Index"), ("middleFinger", "Middle"), ("ringFinger", "Ring"), ("pinky", "Pinky"), ("thumb", "Thumb")):
        for i in (1, 2, 3):
            KI_MAP[f"B-{ki}0{i}{s}"] = f"{side}Hand{mx}{i}"
KI_MAP["B-handProp.R"] = "RightHandProp"
KI_MAP = {k: P + v for k, v in KI_MAP.items() if P + v in REST}


def read_ki(path):
    """Per output frame (30 fps): source world rotations of mapped bones and hip position."""
    objs, acts = import_fbx(path)
    src = next(o for o in objs if o.type == "ARMATURE")
    act = src.animation_data.action
    src_fps = 25.0
    f0, f1 = act.frame_range
    Ws = src.matrix_world.copy()
    Ws_rot = rot3(Ws)
    frames = []
    n_out = int(round((f1 - f0) / src_fps * FPS)) + 1
    for k in range(n_out):
        f = f0 + k * src_fps / FPS
        fi = int(math.floor(f))
        scene.frame_set(fi, subframe=f - fi)
        rots = {name: Ws_rot @ rot3(src.pose.bones[name].matrix) for name in KI_MAP if name in src.pose.bones}
        hips = Ws @ src.pose.bones["B-hips"].matrix.translation
        frames.append((rots, hips))
    # Rest under the (constant) root rotation: every other bone at identity.
    root = src.pose.bones["B-root"]
    scene.frame_set(int(f0))
    root_q = root.rotation_quaternion.copy()
    # The root also carries the rig's scale and offset in the animation; keep them.
    root_loc, root_scale = root.location.copy(), root.scale.copy()
    src.animation_data.action = None
    for pb in src.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    root.rotation_quaternion = root_q
    root.location, root.scale = root_loc, root_scale
    bpy.context.view_layer.update()
    rest = {name: Ws_rot @ rot3(src.pose.bones[name].matrix) for name in KI_MAP if name in src.pose.bones}
    rest_hips = Ws @ src.pose.bones["B-hips"].matrix.translation
    rest_floor = min((Ws @ src.pose.bones[n].matrix.translation).z for n in ("B-toe.L", "B-toe.R"))
    remove(objs)
    for a in acts:
        bpy.data.actions.remove(a)
    scene.render.fps = FPS
    return frames, rest, rest_hips, rest_floor


def basis_frames_ki(path):
    frames, srest, srest_hips, sfloor = read_ki(path)
    ratio = (hips_t.z - floor_t) / max(1e-6, srest_hips.z - sfloor)
    align = {}
    for s_name, t_name in KI_MAP.items():
        if s_name not in srest:
            continue
        rt = rest_world_rot(t_name)
        align[t_name] = (swing(rt.col[1], srest[s_name].col[1]) @ rt, s_name)
    out = []
    W_inv = W.inverted()
    for rots, hips in frames:
        world_rot = {}
        for t_name, (a, s_name) in align.items():
            world_rot[t_name] = rots[s_name] @ srest[s_name].transposed() @ a
        hip_world = Vector((hips_t.x + (hips.x - srest_hips.x) * ratio, hips_t.y + (hips.y - srest_hips.y) * ratio,
                            # Mixamo clips stand on z = 0, not on the bind pose's feet.
                            (hips.z - sfloor) * ratio))
        out.append(solve(world_rot, W_inv @ hip_world))
    return out


def solve(world_rot, hip_arm):
    """Pose bone bases (loc, quat) from desired world rotations; unmapped bones keep their rest."""
    posed, basis = {}, {}
    for name in ORDER:
        p = PARENT[name]
        rest_rel = REST[name] if p is None else REST[p].inverted() @ REST[name]
        inherited = rest_rel if p is None else posed[p] @ rest_rel
        if name in world_rot:
            r = (W_rot.transposed() @ world_rot[name]).to_4x4()
            r.translation = hip_arm if p is None else inherited.translation
            m = r
        else:
            m = inherited.copy()
            if p is None:
                m.translation = hip_arm
        posed[name] = m
        b = inherited.inverted() @ m
        loc, q, _ = b.decompose()
        basis[name] = (loc if p is None else Vector((0, 0, 0)), q)
    return basis


def basis_frames_action(action):
    """Pose bases of a clip already on this rig (Mixamo), one per frame."""
    arm.animation_data_create()
    arm.animation_data.action = action
    if action.slots:
        arm.animation_data.action_slot = action.slots[0]
    f0, f1 = (int(v) for v in action.frame_range)
    out = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        out.append({name: (pb.location.copy(), pb.rotation_quaternion.copy()) for name, pb in arm.pose.bones.items()})
    arm.animation_data.action = None
    return out


def write_action(name, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    slot = act.slots.new(id_type="OBJECT", name=arm.name) if not act.slots else act.slots[0]
    arm.animation_data.action_slot = slot
    n = len(frames)
    for bone in ORDER:
        base = f'pose.bones["{bone}"]'
        series = [("location", 3, lambda fr, i: fr[bone][0][i]), ("rotation_quaternion", 4, lambda fr, i: fr[bone][1][i])]
        for path, count, get in series:
            for i in range(count):
                fc = act.fcurve_ensure_for_datablock(arm, f"{base}.{path}", index=i)
                fc.keyframe_points.add(n)
                co = []
                for k, fr in enumerate(frames):
                    co += [k + 1, get(fr, i)]
                fc.keyframe_points.foreach_set("co", co)
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
                fc.update()
    arm.animation_data.action = None
    return act


def continuous(frames):
    """Keep quaternions in one hemisphere from frame to frame."""
    prev = {}
    for fr in frames:
        for bone, (loc, q) in fr.items():
            if bone in prev and prev[bone].dot(q) < 0:
                q.negate()
            prev[bone] = q
    return frames


def blend(a, b, t):
    out = {}
    for bone in a:
        la, qa = a[bone]
        lb, qb = b[bone]
        if qa.dot(qb) < 0:
            qb = -qb
        out[bone] = (la.lerp(lb, t), qa.slerp(qb, t))
    return out


for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"

# Idle from Mixamo (same rig).
objs, acts = import_fbx(breathing_path)
remove(objs)
idle = basis_frames_action(acts[0])
for a in acts:
    bpy.data.actions.remove(a)

ki = lambda n: os.path.join(ki_dir, n)
draw = basis_frames_ki(ki("HumanM@UnsheatheBack01_R.fbx"))
aim = basis_frames_ki(ki("HumanM@ThrowSpear01_R_-_Hold.fbx"))
throw = basis_frames_ki(ki("HumanM@ThrowSpear01_R.fbx"))

# One cycle: draw from the back, a short aim, the throw. The throw clip opens by bringing a new
# spear up into the aim (its first ~18 frames); the draw replaces that, so the throw joins at
# the aim and is cut once the hunter has recovered. Each join is a crossfade.
FADE = 8
THROW_FROM, THROW_TO = 18, 70
aim_part = aim[: int(0.5 * FPS)]
throw_part = throw[THROW_FROM:THROW_TO]
cycle = list(draw)
for clip in (aim_part, throw_part):
    for k in range(FADE):
        t = (k + 1) / (FADE + 1)
        cycle[-FADE + k] = blend(cycle[-FADE + k], clip[k], t)
    cycle += clip[FADE:]
# Close the loop: the last frames ease into the first.
for k in range(FADE):
    t = (k + 1) / (FADE + 1)
    cycle[-FADE + k] = blend(cycle[-FADE + k], cycle[0], t)

actions = {}
for name, frames in (("Idle", idle), ("Draw", draw), ("Aim", aim), ("Throw", throw), ("Cycle", cycle)):
    actions[name] = write_action(name, continuous(frames))
    report[f"{name}_frames"] = len(frames)
report["cycle_parts_frames"] = [len(draw), len(aim_part), len(throw_part)]

# Markers in the cycle (frame index from 0, at 30 fps): when the hand is at the bundle (the spear
# appears), and when it lets go (fastest forward hand speed during the throw).
act = actions["Cycle"]
arm.animation_data.action = act
arm.animation_data.action_slot = act.slots[0]
hand_pb = arm.pose.bones[P + "RightHand"]
track = []
for k in range(len(cycle)):
    scene.frame_set(k + 1)
    track.append((W @ hand_pb.matrix.translation).copy())
arm.animation_data.action = None
up = Vector((0, 0, 1))
grab = max(range(len(draw)), key=lambda k: track[k].z + track[k].y * 0.5)  # high and behind (+y is back)
throw_start = len(draw) + len(aim_part) - 2 * FADE
release = max(range(throw_start, len(cycle) - 1), key=lambda k: -(track[k + 1].y - track[k].y))
report["grab_frame"] = grab
report["release_frame"] = release
report["cycle_seconds"] = len(cycle) / FPS

# ---------------------------------------------------------------- export
# Importing the 25 fps clips set the scene to 25 fps; the keys are 30 fps frames.
scene.render.fps = FPS
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = tex_path
        img.file_format = "PNG"
        img.save()
        break
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=False, object_types={"ARMATURE", "MESH"},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0, apply_scale_options="FBX_SCALE_UNITS", path_mode="STRIP", embed_textures=False)
json.dump(report, open(os.path.splitext(fbx_path)[0] + "-report.json", "w"), indent=1)
print("HUNTER", json.dumps(report))
