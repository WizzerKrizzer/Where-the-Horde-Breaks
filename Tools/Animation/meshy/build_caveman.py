"""Builds the caveman: the Meshy "Bearclaw Berserker" rigged in Mixamo, with three Mixamo club
swings and the spear hunter's Mixamo "Breathing Idle" retargeted onto him. Run in Blender 5.2:

    blender -b --factory-startup -P build_caveman.py -- skin.fbx clips_dir idle.fbx out.fbx out.png

`skin.fbx` is the swing downloaded With Skin; the other swings in `clips_dir` came Without Skin
for the same character, so they play on his rig as they are. The idle was made for the spear
hunter, whose rest pose differs, so it is retargeted by world rotation (as in build_hunter.py).

Clips (30 fps): Idle; AttackDown, AttackSide, AttackBack (the three swings as downloaded); and
Fight, a loop of idle, the downward and the sideways swing joined by crossfades. A RightHandProp bone sits in the
right fist; the capture test hangs the big bone on it.
"""
import bpy, sys, os, json, math
from mathutils import Matrix, Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
skin_path, clips_dir, idle_path, fbx_path, tex_path = args[:5]
FPS = 30
P = "mixamorig:"

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def import_fbx(path):
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    scene.render.fps = FPS
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
    return a.normalized().rotation_difference(b.normalized()).to_matrix()


# ---------------------------------------------------------------- the caveman
objs, acts = import_fbx(skin_path)
arm = next(o for o in objs if o.type == "ARMATURE")
body = next(o for o in objs if o.type == "MESH")
arm.name = "Caveman"
attack_down = acts[0]
attack_down.name = "Source_AttackDown"
attack_down.use_fake_user = True
W = arm.matrix_world.copy()
W_rot = rot3(W)
report = {}

# The hand prop bone: in the middle of the fist.
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb_hand = arm.data.edit_bones[P + "RightHand"]
prop = arm.data.edit_bones.new(P + "RightHandProp")
prop.parent = eb_hand
prop.use_connect = False
head, tail = eb_hand.head.copy(), eb_hand.tail.copy()
prop.head = head + (tail - head) * 0.6
prop.tail = prop.head + (tail - head) * 0.5
prop.roll = eb_hand.roll
bpy.ops.object.mode_set(mode="OBJECT")

REST = {b.name: b.matrix_local.copy() for b in arm.data.bones}
PARENT = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
ORDER = [b.name for b in arm.data.bones]
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"


def solve(world_rot, hip_arm):
    """Pose bone bases (loc, quat) from desired world rotations; unmapped bones keep their rest."""
    posed, basis = {}, {}
    for name in ORDER:
        p = PARENT[name]
        rest_rel = REST[name] if p is None else REST[p].inverted() @ REST[name]
        inherited = rest_rel if p is None else posed[p] @ rest_rel
        if name in world_rot:
            m = (W_rot.transposed() @ world_rot[name]).to_4x4()
            m.translation = hip_arm if p is None else inherited.translation
        else:
            m = inherited.copy()
            if p is None:
                m.translation = hip_arm
        posed[name] = m
        loc, q, _ = (inherited.inverted() @ m).decompose()
        basis[name] = (loc if p is None else Vector((0, 0, 0)), q)
    return basis


def frames_on_rig(action):
    """Pose bases of a clip made for this character, one per frame."""
    arm.animation_data_create()
    arm.animation_data.action = action
    if action.slots:
        arm.animation_data.action_slot = action.slots[0]
    f0, f1 = (int(v) for v in action.frame_range)
    out = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        out.append({n: (pb.location.copy(), pb.rotation_quaternion.copy()) for n, pb in arm.pose.bones.items()})
    arm.animation_data.action = None
    return out


hips_t = W @ REST[P + "Hips"].translation
floor_t = min((W @ arm.data.bones[P + n].head_local).z for n in ("LeftToeBase", "RightToeBase"))


def retarget(path):
    """A Mixamo clip made for another character, by world rotation change from its rest."""
    objs, acts = import_fbx(path)
    src = next(o for o in objs if o.type == "ARMATURE")
    act = acts[0]
    src.animation_data.action = act
    if act.slots:
        src.animation_data.action_slot = act.slots[0]
    Ws = src.matrix_world.copy()
    Ws_rot = rot3(Ws)
    names = [n for n in ORDER if n in src.pose.bones]
    srest = {n: Ws_rot @ rot3(src.data.bones[n].matrix_local) for n in names}
    srest_hips = Ws @ src.data.bones[P + "Hips"].head_local
    sfloor = min((Ws @ src.data.bones[P + n].head_local).z for n in ("LeftToeBase", "RightToeBase"))
    ratio = (hips_t.z - floor_t) / max(1e-6, srest_hips.z - sfloor)
    align = {}
    for n in names:
        rt = W_rot @ REST[n].to_3x3()
        align[n] = swing(rt.col[1], srest[n].col[1]) @ rt
    out = []
    W_inv = W.inverted()
    f0, f1 = (int(v) for v in act.frame_range)
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        world_rot = {n: Ws_rot @ rot3(src.pose.bones[n].matrix) @ srest[n].transposed() @ align[n] for n in names}
        hips = Ws @ src.pose.bones[P + "Hips"].matrix.translation
        hip_world = Vector((hips_t.x + (hips.x - srest_hips.x) * ratio, hips_t.y + (hips.y - srest_hips.y) * ratio,
                            hips.z * ratio))  # Mixamo clips stand on z = 0
        out.append(solve(world_rot, W_inv @ hip_world))
    remove(objs)
    for a in acts:
        bpy.data.actions.remove(a)
    return out


def write_action(name, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    slot = act.slots.new(id_type="OBJECT", name=arm.name)
    arm.animation_data.action_slot = slot
    n = len(frames)
    for bone in ORDER:
        base = f'pose.bones["{bone}"]'
        prev = None
        qs = []
        for fr in frames:
            q = fr[bone][1].copy()
            if prev is not None and q.dot(prev) < 0:
                q.negate()
            qs.append(q)
            prev = q
        for i in range(3):
            fc = act.fcurve_ensure_for_datablock(arm, base + ".location", index=i)
            fc.keyframe_points.add(n)
            fc.keyframe_points.foreach_set("co", [v for k in range(n) for v in (k + 1, frames[k][bone][0][i])])
        for i in range(4):
            fc = act.fcurve_ensure_for_datablock(arm, base + ".rotation_quaternion", index=i)
            fc.keyframe_points.add(n)
            fc.keyframe_points.foreach_set("co", [v for k in range(n) for v in (k + 1, qs[k][i])])
    arm.animation_data.action = None
    return act


def blend(a, b, t):
    out = {}
    for bone in a:
        la, qa = a[bone]
        lb, qb = b[bone]
        if qa.dot(qb) < 0:
            qb = -qb
        out[bone] = (la.lerp(lb, t), qa.slerp(qb, t))
    return out


def join(parts, fade):
    clip = list(parts[0])
    for part in parts[1:]:
        for k in range(fade):
            t = (k + 1) / (fade + 1)
            clip[-fade + k] = blend(clip[-fade + k], part[k], t)
        clip += part[fade:]
    for k in range(fade):  # close the loop
        t = (k + 1) / (fade + 1)
        clip[-fade + k] = blend(clip[-fade + k], clip[0], t)
    return clip


down = frames_on_rig(attack_down)
side_objs, side_acts = import_fbx(os.path.join(clips_dir, "Standing Melee Attack Horizontal.fbx"))
remove(side_objs)
side = frames_on_rig(side_acts[0])
back_objs, back_acts = import_fbx(os.path.join(clips_dir, "Standing Melee Attack Backhand.fbx"))
remove(back_objs)
back = frames_on_rig(back_acts[0])
idle = retarget(idle_path)

# How far each swing carries the hips, to see whether a clip walks.
for name, frames in (("down", down), ("side", side), ("back", back)):
    xs = [fr[P + "Hips"][0] for fr in frames]
    report[name + "_hip_travel"] = max((a - xs[0]).length for a in xs) * W.to_scale()[0]

# The fight loop: a breath of idle between swings, crossfaded so nothing pops. The backhand steps
# forward about a third of his height, so it stays out of the loop (it would slide back).
FADE = 8
breath = idle[: int(0.8 * FPS)]
fight = join([breath, down, breath, side], FADE)

clips = {"Idle": idle, "AttackDown": down, "AttackSide": side, "AttackBack": back, "Fight": fight}
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
for name, frames in clips.items():
    write_action(name, frames)
    report[name + "_frames"] = len(frames)
# Where each swing lands in the fight loop: the frame the prop moves fastest.
prop_pb = arm.pose.bones[P + "RightHandProp"]
fight_act = bpy.data.actions["Fight"]
arm.animation_data.action = fight_act
arm.animation_data.action_slot = fight_act.slots[0]
track = []
for k in range(len(fight)):
    scene.frame_set(k + 1)
    track.append((W @ prop_pb.matrix.translation).copy())
speeds = [(track[k + 1] - track[k]).length for k in range(len(track) - 1)]
starts = [0, len(breath) - FADE, len(breath) * 2 + len(down) - 2 * FADE, len(fight)]
report["fight_hits"] = [max(range(starts[i], min(starts[i + 1], len(speeds))), key=lambda k: speeds[k]) for i in (1, 2)]
arm.animation_data.action = None
report["fight_seconds"] = len(fight) / FPS

img = next((i for i in bpy.data.images if i.size[0] > 0), None)
if img:
    img.filepath_raw = tex_path
    img.file_format = "PNG"
    img.save()
scene.render.fps = FPS
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
body.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=fbx_path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0, apply_scale_options="FBX_SCALE_UNITS", path_mode="STRIP")
with open(os.path.splitext(fbx_path)[0] + "-report.json", "w") as f:
    json.dump(report, f, indent=1)
print("CAVEMAN", json.dumps(report))
