"""Builds a Mixamo-rigged unit (the cavemen and their chief) into one FBX with all its clips.
Run in Blender 5.2:

    blender -b --factory-startup -P build_mixamo_unit.py -- out.fbx out.png skin.fbx Name=clip.fbx [Name=clip.fbx ...]

`skin.fbx` is any clip downloaded With Skin: it brings the rigged model. Every clip, whichever
character it was downloaded for, is retargeted onto this rig by world rotation change from each
rig's rest pose (as in build_hunter.py), so a clip made for the chief plays on a caveman and the
other way round. The hips keep their height but not their travel across the ground: the game moves
the unit, and the report gives each clip's ground speed so walks can be played at the right rate.

Also:
- Hides and cloaks hang off the arm and leg bones in Mixamo's weights and stretch when a limb
  moves; weight on vertices far from a limb's own bones moves to the shoulder or the hips.
- A RightHandProp bone sits in the right fist, for the weapon the game hangs on it.
- Each clip named Attack* gets a hit time: when the fist moves fastest.
- `Fight` (when AttackDown and AttackSide exist) loops idle and the two swings, for capture tests.
- `Name=mirror:Other` makes Name the mirror image of the clip Other (left and right swapped): the
  chief's left-hand swings (user, 2026-11-11).
"""
import bpy, bmesh, sys, os, json, math
from mathutils import Matrix, Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path, tex_path, skin_path = args[:3]
clip_args = [a.split("=", 1) for a in args[3:]]
FPS = 30
P = "mixamorig:"
LIMB_RADIUS = 0.055

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def import_fbx(path):
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    scene.render.fps = FPS
    return [o for o in bpy.data.objects if o not in before], [a for a in bpy.data.actions if a not in actions_before]


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


objs, acts = import_fbx(skin_path)
arm = next(o for o in objs if o.type == "ARMATURE")
body = next(o for o in objs if o.type == "MESH")
for a in acts:
    bpy.data.actions.remove(a)
if arm.animation_data:
    arm.animation_data.action = None
arm.name = os.path.splitext(os.path.basename(fbx_path))[0]
W = arm.matrix_world.copy()
W_rot = rot3(W)
report = {}

# ---------------------------------------------------------------- weights off the limbs
mesh = body.data
to_world = body.matrix_world
groups = {g.name: g.index for g in body.vertex_groups}


def bone_world(name):
    b = arm.data.bones[name]
    return W @ b.head_local, W @ b.tail_local


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (a + ab * t - p).length


moved = {}
for side in ("Left", "Right"):
    # Only the upper limb's own weight moves: what hangs off the upper arm (a pelt over the
    # shoulder) or the thigh (a cloak, a loincloth). Bracers and leg wraps follow the forearm and
    # the shin, so moving their weight stretched them into flat sheets when the arm swung.
    for limb, upper, lower, anchor, radius in (("arm", "Arm", "ForeArm", "Shoulder", LIMB_RADIUS * 1.1),
                                               ("leg", "UpLeg", "Leg", "Hips", LIMB_RADIUS * 1.6)):
        upper_name = P + side + upper
        segs = [bone_world(upper_name), bone_world(P + side + lower)]
        target = P + (anchor if anchor == "Hips" else side + anchor)
        count = 0
        for v in mesh.vertices:
            w = {body.vertex_groups[g.group].name: g.weight for g in v.groups}
            weight = w.get(upper_name, 0.0)
            if weight <= 0.0:
                continue
            p = to_world @ v.co
            if min(seg_dist(p, a, b) for a, b in segs) <= radius:
                continue
            body.vertex_groups[upper_name].remove([v.index])
            body.vertex_groups.get(target) or body.vertex_groups.new(name=target)
            body.vertex_groups[target].add([v.index], weight, "ADD")
            count += 1
        moved[side + limb] = count
report["vertices_taken_off_limbs"] = moved

# ---------------------------------------------------------------- the prop bone
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
hips_t = W @ REST[P + "Hips"].translation
floor_t = min((W @ arm.data.bones[P + n].head_local).z for n in ("LeftToeBase", "RightToeBase"))


def solve(world_rot, hip_arm):
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


def retarget(path):
    """A Mixamo clip on this rig, by world rotation change from each rig's rest. The hips keep
    their height (scaled to this rig) but stay over the rest spot; their travel is reported."""
    objs, acts = import_fbx(path)
    src = next(o for o in objs if o.type == "ARMATURE")
    act = acts[0]
    src.animation_data_create()
    src.animation_data.action = act
    if act.slots:
        src.animation_data.action_slot = act.slots[0]
    Ws = src.matrix_world.copy()
    Ws_rot = rot3(Ws)
    names = [n for n in ORDER if n in src.data.bones]
    srest = {n: Ws_rot @ rot3(src.data.bones[n].matrix_local) for n in names}
    srest_hips = Ws @ src.data.bones[P + "Hips"].head_local
    sfloor = min((Ws @ src.data.bones[P + n].head_local).z for n in ("LeftToeBase", "RightToeBase"))
    ratio = (hips_t.z - floor_t) / max(1e-6, srest_hips.z - sfloor)
    align = {n: swing((W_rot @ REST[n].to_3x3()).col[1], srest[n].col[1]) @ W_rot @ REST[n].to_3x3() for n in names}
    out, travel, raw = [], [], []
    W_inv = W.inverted()
    f0, f1 = (int(v) for v in act.frame_range)
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        world_rot = {n: Ws_rot @ rot3(src.pose.bones[n].matrix) @ srest[n].transposed() @ align[n] for n in names}
        hips = Ws @ src.pose.bones[P + "Hips"].matrix.translation
        travel.append(Vector((hips.x - srest_hips.x, hips.y - srest_hips.y)) * ratio)
        hip_world = Vector((hips_t.x, hips_t.y, hips.z * ratio))  # Mixamo clips stand on z = 0
        out.append(solve(world_rot, W_inv @ hip_world))
        raw.append((world_rot, W_inv @ hip_world))
    remove(objs)
    for a in acts:
        bpy.data.actions.remove(a)
    return out, travel, raw


# The mirror across the rig's middle: its left-right axis, from the right arm to the left.
REST_W = {n: W_rot @ REST[n].to_3x3() for n in ORDER}
_lateral = ((W @ REST[P + "LeftArm"].translation) - (W @ REST[P + "RightArm"].translation)).normalized()
MIRROR = Matrix.Identity(3) - 2.0 * Matrix([[_lateral[i] * _lateral[j] for j in range(3)] for i in range(3)])


def other_side(name):
    if "Left" in name:
        return name.replace("Left", "Right")
    if "Right" in name:
        return name.replace("Right", "Left")
    return name


def mirrored(raw):
    """A clip's mirror image: each bone takes its other-side twin's world rotation, reflected, with a
    fixed correction so the rest pose maps onto itself. The hips stay over the rest spot."""
    out = []
    for world_rot, hip_arm in raw:
        flipped = {}
        for name in world_rot:
            twin = other_side(name)
            if twin not in world_rot or twin not in REST_W:
                continue
            correction = (MIRROR @ REST_W[twin] @ MIRROR).inverted() @ REST_W[name]
            flipped[name] = MIRROR @ world_rot[twin] @ MIRROR @ correction
        out.append(solve(flipped, hip_arm))
    return out


def write_action(name, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    arm.animation_data.action_slot = act.slots.new(id_type="OBJECT", name=arm.name)
    n = len(frames)
    for bone in ORDER:
        base = f'pose.bones["{bone}"]'
        prev, qs = None, []
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
    for k in range(fade):
        t = (k + 1) / (fade + 1)
        clip[-fade + k] = blend(clip[-fade + k], clip[0], t)
    return clip


clips, travels, raws = {}, {}, {}
for name, path in clip_args:
    if path.startswith("mirror:"):
        continue
    clips[name], travels[name], raws[name] = retarget(path)
for name, path in clip_args:
    if path.startswith("mirror:"):
        source = path[len("mirror:"):]
        clips[name] = mirrored(raws[source])
        travels[name] = travels[source]
if "Idle" in clips and "AttackDown" in clips and "AttackSide" in clips:
    breath = clips["Idle"][: int(0.8 * FPS)]
    clips["Fight"] = join([breath, clips["AttackDown"], breath, clips["AttackSide"]], 8)

for name, frames in clips.items():
    act = write_action(name, frames)
    entry = {"frames": len(frames), "seconds": round(len(frames) / FPS, 3)}
    if name in travels:
        t = travels[name]
        entry["ground_travel"] = round((t[-1] - t[0]).length, 4)  # in the same units as "height"
        entry["ground_speed"] = round(entry["ground_travel"] / max(1e-6, entry["seconds"]), 4)
    if name.startswith("Attack") or name in ("ShieldBlock", "Battlecry"):
        arm.animation_data.action = act
        arm.animation_data.action_slot = act.slots[0]
        # A mirrored swing is struck with the left hand.
        pb = arm.pose.bones[P + ("LeftHand" if name.endswith("Left") else "RightHandProp")]
        track = []
        for k in range(len(frames)):
            scene.frame_set(k + 1)
            track.append((W @ pb.matrix.translation).copy())
        speeds = [(track[k + 1] - track[k]).length for k in range(len(track) - 1)]
        entry["hit_seconds"] = round(max(range(len(speeds)), key=lambda k: speeds[k]) / FPS, 3)
        arm.animation_data.action = None
    report[name] = entry
report["height"] = round(max((to_world @ v.co).z for v in mesh.vertices) - min((to_world @ v.co).z for v in mesh.vertices), 4)

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
print("UNIT", json.dumps(report))
