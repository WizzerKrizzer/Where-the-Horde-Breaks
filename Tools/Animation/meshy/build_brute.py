"""The horde's Orc Brute (user, 2026-11-14): the Meshy "Skullbearer Orc", made for the GPU horde the
same way as the goblin (build_goblin.py, whose approved walk, blows and four ways of lying dead it
follows), but 2.5 m tall and heavier in its walk, with a club with a stone lashed to its end
in its right hand. Other weapons are built too (2 a huge thigh bone, 3 a stone maul) but the game
shows only the club (user, 2026-11-15: many kinds made a crowd harder to read). Some brutes differ
a little in their hair, numbered as the goblin's (1 a long tail of hair down the back, 2 a crest of
spikes), so the horde's shader picks them the same way (HordeGoblinLooks.cginc). Its skin is lightened (the Meshy texture came out
almost black-green, user: "Осветли кожата"), the bone, hide and wraps are left as they are.

Writes `brute.bytes` (the goblin's format, "GOBN" version 4, with held and worn numbers) and
`brute-tex.png`: the texture at 512 over a strip of wood, stone, bone, fur and hair.

Run: blender -b --python build_brute.py -- <orc.fbx> <texture.png> <out folder> [preview folder]
"""
import math
import os
import struct
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
FBX, TEXTURE, OUT = argv[0], argv[1], argv[2]
PREVIEW = argv[3] if len(argv) > 3 else None

# The brute's height in the game, metres (user, 2026-11-15: 2.5 m; it was about 2 m), and the horde's scale for a
# brute (EnemyDefinition visualScale 0.4875, grown with the height so the body in the crowd is as much wider): the mesh is written in units of that scale. Brutes are
# few, so their near level of detail has more triangles than a goblin's.
HEIGHT = 2.5
HORDE_SCALE = 0.4875
LODS = (6000, 700)
# How much lighter the skin is made: the Meshy texture's green is about 40% darker than the goblin's.
SKIN_LIGHTEN = 1.45
POSES = 6
# 1024: up close (the player may stand in front of them) a face at 512 was a blur (user, 2026-11-15).
TEX = 1024
STRIP = 128

# ---------------------------------------------------------------- the model
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
co = np.array([v.co for v in body.data.vertices])
lift = -co[:, 2].min()
source_height = co[:, 2].max() - co[:, 2].min()
for v in body.data.vertices:
    v.co.z += lift

# ---------------------------------------------------------------- skeleton
# Blender axes as imported: x to the model's left, -y forward, z up; metres of the Meshy model
# (0.95 tall, in an A-pose). Measured from height slices of the mesh (measure_slices.py).
def mirror(p):
    return (-p[0], p[1], p[2])

JOINTS = {
    'hips': (0.0, 0.0, 0.42), 'chest': (0.0, 0.0, 0.70), 'neck': (0.0, -0.01, 0.79),
    'head_top': (0.0, -0.05, 0.95),
    'hip.L': (0.08, 0.0, 0.40), 'knee.L': (0.095, -0.01, 0.25), 'ankle.L': (0.11, 0.03, 0.08),
    'toe.L': (0.12, -0.10, 0.02),
    'shoulder.L': (0.20, 0.02, 0.72), 'elbow.L': (0.30, 0.01, 0.60), 'wrist.L': (0.38, -0.05, 0.47),
    'finger.L': (0.44, -0.07, 0.35),
}
# The knuckles, a little under half way down the hand: the fingers bend there into a fist round
# what the hand holds (user, 2026-11-15: open hands with the club only placed in them).
JOINTS['knuckle.L'] = tuple(np.array(JOINTS['wrist.L']) * 0.55 + np.array(JOINTS['finger.L']) * 0.45)
for k in list(JOINTS):
    if k.endswith('.L'):
        JOINTS[k[:-2] + '.R'] = mirror(JOINTS[k])
J = {k: np.array(v) for k, v in JOINTS.items()}

# name: (head joint, tail joint, parent)
BONES = {
    'spine': ('hips', 'chest', None),
    'head': ('neck', 'head_top', 'spine'),
}
for s in ('L', 'R'):
    BONES.update({
        'thigh.' + s: ('hip.' + s, 'knee.' + s, 'spine'),
        'shin.' + s: ('knee.' + s, 'ankle.' + s, 'thigh.' + s),
        'foot.' + s: ('ankle.' + s, 'toe.' + s, 'shin.' + s),
        'upperarm.' + s: ('shoulder.' + s, 'elbow.' + s, 'spine'),
        'forearm.' + s: ('elbow.' + s, 'wrist.' + s, 'upperarm.' + s),
        'hand.' + s: ('wrist.' + s, 'knuckle.' + s, 'forearm.' + s),
        'fingers.' + s: ('knuckle.' + s, 'finger.' + s, 'hand.' + s),
    })
ORDER = ['spine', 'head'] + [b + '.' + s for s in ('L', 'R') for b in
                             ('thigh', 'shin', 'foot', 'upperarm', 'forearm', 'hand', 'fingers')]


def segment_distance(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0.0, 1.0)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


def weights_for(points):
    """Each vertex follows the bones nearest to it; a high power keeps the limbs rigid and blends
    only near the joints. Limbs pull only vertices on their own side."""
    d = np.stack([segment_distance(points, J[BONES[b][0]], J[BONES[b][1]]) for b in ORDER], 1)
    for i, b in enumerate(ORDER):
        if b.endswith('.L'):
            d[points[:, 0] < -0.01, i] = 1e3
        elif b.endswith('.R'):
            d[points[:, 0] > 0.01, i] = 1e3
        if b == 'head':
            d[points[:, 2] < 0.76, i] = 1e3
        if b.startswith(('thigh', 'shin', 'foot')):
            d[points[:, 2] > 0.46, i] = 1e3
        # The arms hang well away from the body in the A-pose: nothing near the middle follows them.
        if b.startswith(('upperarm', 'forearm', 'hand', 'fingers')):
            d[np.abs(points[:, 0]) < 0.16, i] = 1e3
    w = 1.0 / (d + 0.004) ** 6
    w /= w.sum(1, keepdims=True)
    return w


# ---------------------------------------------------------------- walk
def rot_x(deg):
    """A bone's rotation: degrees about x, (axis, degrees) for another axis, or a list of those
    applied first to last."""
    if isinstance(deg, list):
        R = np.eye(3)
        for d in deg:
            R = rot_x(d) @ R
        return R
    if isinstance(deg, tuple):
        return np.array(Matrix.Rotation(math.radians(deg[1]), 3, deg[0]))
    return np.array(Matrix.Rotation(math.radians(deg), 3, 'X'))


# The body's lean, degrees (forward positive): between the two the user turned down on 2026-11-15
# (16, bowed as if about to fall on its face; -3, so upright it looked about to slip onto its back),
# about the goblin's own lean, which the user likes.
LEAN = 7.0
# How much of that lean the legs take off again: half, so the hips sit a little back over the feet
# with the body leaning over them, as a heavy walker carries itself.
LEGS_UNDER = 0.5


def pose(phase, still=False, blow=None):
    """Local rotations (degrees about x; positive tips the bone's tail forwards, -y) and a lift
    of the whole body, for a point of the walk cycle."""
    a = {b: 0.0 for b in ORDER}
    if blow == 'dead_front':
        # Face down: tipped forward, head turned to one side, one arm stretched up past the head,
        # the other along the body, one knee drawn up to the side.
        a['spine'] = 90.0
        a['head'] = [('Z', 40.0), -20.0]
        a['upperarm.R'] = -150.0
        a['forearm.R'] = -15.0
        a['upperarm.L'] = ('Y', -20.0)
        a['forearm.L'] = -30.0
        a['thigh.L'] = [('Y', -35.0), -45.0]
        a['shin.L'] = 70.0
        a['thigh.R'] = ('Y', 8.0)
        a['shin.R'] = 10.0
        a['foot.L'] = 40.0
        a['foot.R'] = 40.0
        return a, 0.0
    if blow == 'dead_side':
        # Curled on its side: rolled over to the left, knees drawn up, arms in front, head bowed.
        a['spine'] = [('Y', -90.0), 10.0]
        a['head'] = 25.0
        a['upperarm.L'] = -60.0
        a['forearm.L'] = -70.0
        a['upperarm.R'] = -40.0
        a['forearm.R'] = -80.0
        a['thigh.L'] = -70.0
        a['shin.L'] = 95.0
        a['thigh.R'] = -55.0
        a['shin.R'] = 80.0
        return a, 0.0
    if blow == 'dead_sprawl':
        # On its back, twisted: one arm thrown over the head, the other out low, one knee up,
        # the other leg kicked out, the head rolled to the side.
        a['spine'] = [-90.0, ('Z', 14.0)]
        a['head'] = [('Z', -35.0), -10.0]
        a['upperarm.L'] = [-160.0, ('Y', -20.0)]
        a['forearm.L'] = -40.0
        a['upperarm.R'] = ('Y', 35.0)
        a['forearm.R'] = 30.0
        a['thigh.R'] = -55.0
        a['shin.R'] = 90.0
        a['thigh.L'] = ('Y', -28.0)
        a['shin.L'] = 5.0
        return a, 0.0
    if blow == 'dead':
        # On its back: the whole body tipped back about the hips, arms flung out to the sides, legs
        # a little apart, the head rolled back. Lowered onto the ground after skinning.
        a['spine'] = -90.0
        a['head'] = -18.0
        a['upperarm.L'] = ('Y', -75.0)
        a['upperarm.R'] = ('Y', 75.0)
        a['forearm.L'] = 25.0
        a['forearm.R'] = 25.0
        a['thigh.L'] = ('Y', -16.0)
        a['thigh.R'] = ('Y', 16.0)
        a['shin.L'] = -20.0
        a['shin.R'] = -20.0
        return a, 0.0
    if still:
        # Standing heavily: knees and elbows a little bent, leaning in a little.
        a['spine'] = LEAN
        a['head'] = -5.0
        a['thigh.L'] = a['thigh.R'] = -10.0 - LEAN * LEGS_UNDER
        a['shin.L'] = a['shin.R'] = 18.0
        a['foot.L'] = a['foot.R'] = -8.0
        a['forearm.L'] = a['forearm.R'] = -22.0
        return a, 0.0
    if blow == 'raise':
        # The weapon up behind the head, the body leaning back.
        a['spine'] = -8.0
        a['head'] = -4.0
        a['upperarm.R'] = -150.0
        a['forearm.R'] = -35.0
        # The club is carried pointing up: the wrist turns it back over the shoulder.
        a['hand.R'] = -60.0
        a['upperarm.L'] = 15.0
        return a, 0.01
    if blow == 'strike':
        # Brought down in front, the body thrown forward over a bent front leg.
        a['spine'] = 20.0
        a['head'] = -8.0
        a['upperarm.R'] = -62.0
        a['forearm.R'] = -8.0
        # The wrist turns the club down onto the target.
        a['hand.R'] = 90.0
        a['upperarm.L'] = 25.0
        a['thigh.L'] = -14.0
        a['shin.L'] = 22.0
        return a, -0.015
    # Walked like the goblin's approved lope but heavier (user, 2026-11-15: the brute walked stiff
    # and upright, "like a robot"): always crouched, knees and elbows bent, the back bowed and the
    # head pushed forward, the arms swinging forward and back with the legs.
    s = math.sin(2 * math.pi * phase)
    c = math.cos(2 * math.pi * phase)
    swing, knee, arm = 32.0, 55.0, 22.0
    # The legs hang from the spine: take its lean off them, so they stay under the body.
    a['thigh.L'] = -10.0 - LEAN * LEGS_UNDER - swing * s
    a['thigh.R'] = -10.0 - LEAN * LEGS_UNDER + swing * s
    # The knee bends while its leg swings forward, and keeps a good bend on the ground.
    a['shin.L'] = 20.0 + knee * max(0.0, c)
    a['shin.R'] = 20.0 + knee * max(0.0, -c)
    a['foot.L'] = -0.6 * (a['thigh.L'] + a['shin.L'])
    a['foot.R'] = -0.6 * (a['thigh.R'] + a['shin.R'])
    # The club (right) arm forward with the left leg, both a little forward of hanging.
    a['upperarm.R'] = -8.0 - arm * s
    a['upperarm.L'] = -8.0 + arm * s
    # The club arm bends less as it swings forward, so the club stays pointing ahead, not up.
    a['forearm.R'] = -20.0 - 6.0 * max(0.0, s)
    a['forearm.L'] = -26.0 - 14.0 * max(0.0, -s)
    # Leaning in a little, rocking with each step like the goblin.
    a['spine'] = LEAN + 2.0 * abs(s)
    a['head'] = -5.0
    # The body's height comes from the legs: each pose is set down on its lowest foot (below).
    return a, 0.0


def between(start, end, t):
    """A pose part way from one (angles, bob) to another; only plain angles in degrees about x."""
    (a, bob_a), (b, bob_b) = start, end
    return {k: a[k] + (b[k] - a[k]) * t for k in a}, bob_a + (bob_b - bob_a) * t


# The arms in the A-pose hang about 30 degrees out; brought in towards the body for walking,
# standing and fighting (degrees about y; the left arm turns the other way).
# 24 (user, 2026-11-15: at 14 the arms stood out too far from the body).
ARMS_IN = 24.0


def arms_in(angles):
    for side, sign in (('L', 1.0), ('R', -1.0)):
        b = 'upperarm.' + side
        angles[b] = [('Y', sign * ARMS_IN)] + (angles[b] if isinstance(angles[b], list) else [angles[b]])
    return angles


# The fingers curl in towards the palm about the front-to-back axis: the right hand into a tight
# fist round its weapon, the left loosely (degrees about y; the left turns the other way).
FIST_CURL = 95.0
RELAXED_CURL = 45.0


def fists(angles):
    # The right hand is clenched in its shape (clench); its finger bone carries nothing.
    angles['fingers.R'] = 0.0
    angles['fingers.L'] = ('Y', RELAXED_CURL)
    return angles


def skin(points, w, angles, bob):
    angles = fists(dict(angles))
    world = {}
    for b in ORDER:
        head = J[BONES[b][0]]
        parent = BONES[b][2]
        local = rot_x(angles[b])
        if parent is None:
            R = local
            t = head + np.array([0.0, 0.0, bob]) - local @ head
        else:
            PR, Pt = world[parent]
            R = PR @ local
            t = (PR @ head + Pt) - R @ head
        world[b] = (R, t)
    out = np.zeros_like(points)
    for i, b in enumerate(ORDER):
        R, t = world[b]
        out += w[:, i:i + 1] * (points @ R.T + t)
    return out


# ---------------------------------------------------------------- the club
# Strip segments (fifths of its width).
WOOD, STONE, BONE, FUR, HAIR = range(5)
SEGMENTS = 5
# Inside the fist: just in from the knuckles, towards the palm (the body side).
GRIP = J['knuckle.R'] + np.array([0.015, 0.0, 0.02])
# Forward and up, a little out to the side (user, 2026-11-15, with drawings: pointing down it looked
# limp, and seen from the side it must point forward, not straight up). The bent forearm of the walk
# tilts it further up, to about 45 degrees.
GRIP_DIRECTION = (-0.2, -0.92, 0.33)

# The weapon hand is squeezed shut round the handle (user, 2026-11-15: bending the fingers alone
# still read as an open hand): every vertex of the hand is drawn in towards the handle's line, to
# within FIST_RADIUS of it and FIST_HALF along it either side of the grip, more the further it is
# from the wrist, so the hand becomes a fist wrapped round the handle and stays joined at the wrist.
FIST_RADIUS = 0.04
FIST_HALF = 0.035


def clench(points, w, body, grip, direction):
    fingers = ORDER.index('fingers.R')
    hand = ORDER.index('hand.R')
    wrist, knuckle = J['wrist.R'], J['knuckle.R']
    span = knuckle - wrist
    from_wrist = np.clip(((points - wrist) @ span) / (span @ span), 0.0, 1.0)
    t = np.clip(w[:, fingers] + w[:, hand] * np.clip(from_wrist * 1.6, 0.0, 1.0), 0.0, 1.0) * body
    d = np.array(direction) / np.linalg.norm(direction)
    along = (points - grip) @ d
    radial = points - grip - along[:, None] * d
    length = np.linalg.norm(radial, axis=1)
    squeezed = (grip + np.clip(along, -FIST_HALF, FIST_HALF)[:, None] * d
                + radial * (np.minimum(length, FIST_RADIUS) / np.maximum(length, 1e-6))[:, None])
    points = points + (squeezed - points) * t[:, None]
    # The squeezed fingers now move with the hand.
    w = w.copy()
    w[:, hand] += w[:, fingers]
    w[:, fingers] = 0.0
    return points, w, t



def finish(obj, name):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = name
    return obj


def in_fist(obj, along):
    """Turns a weapon made along +z (the grip at the origin) into the right fist, pointing forward
    and a little down; `along` is how far the fist is up the handle."""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    direction = Vector(GRIP_DIRECTION).normalized()
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(direction)
    obj.location = Vector(GRIP) - direction * along


def lumpy(obj, centre, amount, squash=1.0):
    for v in obj.data.vertices:
        v.co = centre + (v.co - centre) * (1.0 + amount * math.sin(v.index * 2.1))
        v.co.z = centre.z + (v.co.z - centre.z) * squash


def make_pieces():
    """Every held and worn thing as (object, strip segment, held number, worn number); the pieces
    with one number move and show together."""
    pieces = []
    # 1: the club (user, 2026-11-16, from a picture of a spiked bat): a wooden bat thickening to a
    # rounded end, a corded grip and a knob below the fist, a wide band of hide round the thick end,
    # and through the hide, instead of iron spikes, fangs and bone points sticking out all round.
    # Half as long as it was made at first (user, 2026-11-16: twice the length looked comical in a
    # crowd), and three quarters as thick, so the grip still fills the fist.
    def add(obj, segment, name):
        in_fist(obj, 0.03)
        pieces.append((finish(obj, name), segment, 1, 0))

    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.0225, radius2=0.051, depth=0.33, location=(0, 0, 0.145))
    add(bpy.context.active_object, WOOD, 'club_wood')
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.051, location=(0, 0, 0.31))
    cap = bpy.context.active_object
    cap.scale = (1.0, 1.0, 0.55)
    add(cap, WOOD, 'club_end')
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.032, depth=0.03, location=(0, 0, -0.035))
    add(bpy.context.active_object, FUR, 'club_knob')
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.027, depth=0.08, location=(0, 0, 0.02))
    add(bpy.context.active_object, STONE, 'club_cord')
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.053, depth=0.1, location=(0, 0, 0.235))
    add(bpy.context.active_object, FUR, 'club_hide')
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.043, depth=0.02, location=(0, 0, 0.15))
    add(bpy.context.active_object, FUR, 'club_tie')
    # Three rings of five, each ring turned half a gap from the last, a little different in length,
    # tipped a little up the club.
    for ring, z in enumerate((0.2, 0.235, 0.27)):
        for k in range(5):
            angle = (k + 0.5 * ring) * 2.0 * math.pi / 5.0
            length = (0.042, 0.054, 0.039, 0.051, 0.045)[(k + 2 * ring) % 5]
            out = Vector((math.cos(angle), math.sin(angle), 0.25)).normalized()
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.01, radius2=0.0, depth=length)
            fang = bpy.context.active_object
            fang.rotation_mode = 'QUATERNION'
            fang.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(out)
            fang.location = Vector((0, 0, z)) + Vector((math.cos(angle), math.sin(angle), 0.0)) * 0.05 + out * (length * 0.5)
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            add(fang, BONE, 'club_fang')
    # 2: a huge thigh bone, a knuckle at each end.
    bpy.ops.mesh.primitive_cylinder_add(vertices=7, radius=0.024, depth=0.34, location=(0, 0, 0.14))
    bone = bpy.context.active_object
    for end in (-0.03, 0.31):
        for side in (-0.022, 0.022):
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.045, location=(side, 0, end))
            knob = bpy.context.active_object
            for o in (knob, bone):
                o.select_set(True)
            bpy.context.view_layer.objects.active = bone
            bpy.ops.object.join()
    in_fist(bone, 0.0)
    pieces.append((finish(bone, 'thighbone'), BONE, 2, 0))
    # 3: a stone maul, a long handle and a big squared stone head.
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.018, depth=0.36, location=(0, 0, 0.16))
    handle = bpy.context.active_object
    in_fist(handle, 0.02)
    pieces.append((finish(handle, 'maul_wood'), WOOD, 3, 0))
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.35))
    head = bpy.context.active_object
    head.scale = (0.16, 0.085, 0.1)
    bpy.ops.object.transform_apply(scale=True)
    lumpy(head, Vector((0, 0, 0.35)), 0.12)
    in_fist(head, 0.02)
    pieces.append((finish(head, 'maul_stone'), STONE, 3, 0))

    # The head, measured from slices: about 0.07 to each side, the face at -y 0.12, the back of the
    # head at +y 0.08, the crown at 0.95 with short hair.
    # 1: a tail of hair hanging from the crown down the back of the head.
    direction = Vector((0.0, 0.7, -0.71)).normalized()
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.02, radius2=0.008, depth=0.15)
    tail = bpy.context.active_object
    tail.rotation_mode = 'QUATERNION'
    tail.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(direction)
    tail.location = Vector((0.0, 0.08, 0.93)) + direction * 0.075
    pieces.append((finish(tail, 'tail'), HAIR, 0, 1))
    # 2: a crest of spikes along the middle of the crown.
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.016, radius2=0.0, depth=0.06, location=(0.0, -0.04, 0.955))
    crest = bpy.context.active_object
    for y, height in ((0.0, 0.07), (0.04, 0.055)):
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.016, radius2=0.0, depth=height,
                                        location=(0.0, y, 0.935 + height * 0.5), rotation=(math.radians(18), 0.0, 0.0))
        spike = bpy.context.active_object
        for o in (spike, crest):
            o.select_set(True)
        bpy.context.view_layer.objects.active = crest
        bpy.ops.object.join()
    pieces.append((finish(crest, 'crest'), HAIR, 0, 2))
    return pieces


PIECES = make_pieces()


# ---------------------------------------------------------------- the animation
# The walk and the blow come from Meshy animations of the same model on a Mixamo skeleton, when
# they are beside the model (Animated/all.fbx; user, 2026-11-15: posed by hand the brute moved like
# a stick). Every few frames of each only, to keep the game's stepped look.
# The walk: "Slow_Orc_Walk" (the user's choice, 2026-11-15: slow and heavy, not like a man; "Walking"
# looked like a stroll and "Running" like a marathon). One cycle is its frames 1 to 82 (the action
# holds two), in place, hunched and swaying; ten poses evenly through it. Its own steps cover only
# about 1 m a cycle, and a brute moves at 2.14 m a second, so the steps are made longer
# (WALK_REACH: the legs reach that much further forward and back from the hips) and the feet are
# allowed to glide a little (WALK_GLIDE), so the legs turn about once a second: slow, long strides.
# The blow: the first of "Weapon Combo 2", overhead and down with the right hand, the feet planted:
# the wind-up, then the blow landing (frame 37) and held (40).
ANIMATED_FBX = os.path.join(os.path.dirname(FBX), 'Animated', 'all.fbx')
# Off (user, 2026-11-15): in a crowd the animated walk swayed and flung its arms so much that the
# brutes seemed to thrash about, so the walk is again the one posed by hand below (pose()), which
# the user had approved; only the blow comes from the animation.
WALK_ANIMATED = False
# How much further the legs of the hand-posed walk reach forward and back from the hips, see
# hand_walk. As posed its feet covered only 1.18 m a cycle (0.6 m a step for a 2 m brute), so at
# its 2.14 m a second the legs turned 1.8 times a second: many small quick steps for how fast it
# went (user, 2026-11-15). At 1.3 a cycle covers about 1.53 m and the legs turn 1.4 times a second
# (at 1.5 the strides became lunges, the back shin nearly flat).
HAND_REACH = 1.3
WALK_ACTION = 'Slow_Orc_Walk'
WALK_CYCLE = (1, 82)
WALK_FRAMES = tuple(1.0 + k * 81.0 / 10.0 for k in range(10))
WALK_REACH = 1.6
WALK_GLIDE = 1.4
# The body bowed forward over the walk, degrees: at the hips and again at the chest, and the head
# lifted back by HEAD_UP so it still looks ahead (the goblin's hunched lope is what the user likes).
# (None for this walk: it is hunched as it is.)
WALK_HUNCH = (0.0, 0.0)
HEAD_UP = 0.0
ATTACK_ACTION = 'Weapon_Combo_2'
ATTACK_FRAMES = (7, 13, 19, 25, 29, 33, 37, 40)
# Different blows (user, 2026-11-16: every blow was the same), each eight frames: six of the
# wind-up, then the blow landing and held. The game picks one for each blow, never the same twice
# running (HordeGoblin.cginc _AttackRows.w). Each is drawn on the spot: the animations step into
# their blows, and the hips' sideways and forward drift is taken out (each frame is moved back by how far its hips are off where they stand at the start of the animation).
BLOWS = (
    # Overhead and straight down (the first blow of "Weapon Combo 2").
    ('overhead', 'Weapon_Combo_2', ATTACK_FRAMES),
    # From high on the right, across and down to the left (its second).
    ('across', 'Weapon_Combo_2', (44, 48, 52, 55, 58, 61, 63, 65)),
    # High over the head, then bent deep forward into a heavy smash ("Weapon Combo", its second).
    ('smash', 'Weapon_Combo', (48, 52, 56, 59, 62, 65, 70, 73)),
)


def load_animation(path, wanted):
    """The animated model's standing vertices and their bone weights, the bones' names, for each
    action wanted (name: frames) the matrices of every bone that carry a standing point to its place
    in each frame, and how far the feet carry the body in one cycle of the walk (the model's own
    units)."""
    scene = bpy.context.scene
    before = set(scene.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    added = [o for o in scene.objects if o not in before]
    arm = next(o for o in added if o.type == 'ARMATURE')
    geo = next(o for o in added if o.type == 'MESH')
    bpy.context.view_layer.update()
    gw = np.array(geo.matrix_world)
    rest = np.array([v.co for v in geo.data.vertices]) @ gw[:3, :3].T + gw[:3, 3]
    names = [b.name for b in arm.data.bones]
    group_bone = {g.index: names.index(g.name) for g in geo.vertex_groups if g.name in names}
    weights = np.zeros((len(rest), len(names)))
    for v in geo.data.vertices:
        for g in v.groups:
            if g.group in group_bone:
                weights[v.index, group_bone[g.group]] = g.weight
    weights[weights.sum(1) <= 0.0, 0] = 1.0
    weights /= weights.sum(1, keepdims=True)
    aw = np.array(arm.matrix_world)
    awi = np.linalg.inv(aw)
    bind = [np.linalg.inv(np.array(arm.data.bones[n].matrix_local)) for n in names]
    matrices = {}
    drift = {}
    covered = 0.0
    for label, (action_name, frames) in wanted.items():
        action = next(a for a in bpy.data.actions if '|' + action_name + '|' in a.name)
        arm.animation_data.action = action
        if hasattr(arm.animation_data, 'action_slot') and getattr(action, 'slots', None):
            arm.animation_data.action_slot = action.slots[0]
        scene.frame_set(1)
        bpy.context.view_layer.update()
        home = np.array(arm.matrix_world @ arm.pose.bones['mixamorig:Hips'].head)
        matrices[label] = []
        drift[label] = []

        def sample(key):
            bpy.context.view_layer.update()
            matrices.setdefault(key, []).append(np.stack([aw @ np.array(arm.pose.bones[n].matrix) @ bind[i] @ awi
                                                          for i, n in enumerate(names)]))
            hips = np.array(arm.matrix_world @ arm.pose.bones['mixamorig:Hips'].head)
            drift.setdefault(key, []).append(np.array([hips[0] - home[0], hips[1] - home[1], 0.0]))

        for frame in frames:
            scene.frame_set(int(frame), subframe=frame - int(frame))
            sample(label)
        if label != WALK_ACTION:
            # Into the blow and out of it by the bones themselves (user, 2026-11-16: from resting
            # the club jumped behind the back, and in between the club left the hand and the body
            # bent out of shape, each point going straight from one place to the other): every
            # bone turned part of the way from how it stands at the start of the animation (its
            # first frame, standing) to how it is at the blow's first frame, and from the blow's
            # last frame back towards standing.
            def basis(frame):
                scene.frame_set(int(frame))
                bpy.context.view_layer.update()
                return {b.name: b.matrix_basis.copy() for b in arm.pose.bones}

            standing_pose, first_pose, last_pose = basis(1), basis(frames[0]), basis(frames[-1])
            arm.animation_data.action = None
            for key, (a, b, t) in (('standing', (standing_pose, first_pose, 0.0)),
                                   ('into', (standing_pose, first_pose, 0.5)),
                                   ('out', (last_pose, standing_pose, 0.5))):
                for pb in arm.pose.bones:
                    la, ra, sa = a[pb.name].decompose()
                    lb, rb, sb = b[pb.name].decompose()
                    pb.matrix_basis = Matrix.LocRotScale(la.lerp(lb, t), ra.slerp(rb, t), sa.lerp(sb, t))
                sample((label, key))
        if action_name == WALK_ACTION:
            # The foot on the ground (the lower one) slides back under the body: the sum of that
            # over the cycle is the ground one cycle covers (forward is -y).
            first, last = WALK_CYCLE
            feet = []
            for frame in range(first, last + 1):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                feet.append([np.array(arm.matrix_world @ arm.pose.bones['mixamorig:' + side + 'Foot'].head)
                             for side in ('Left', 'Right')])
            for now, then in zip(feet[:-1], feet[1:]):
                down = 0 if now[0][2] + then[0][2] < now[1][2] + then[1][2] else 1
                covered += max(0.0, then[down][1] - now[down][1])
    heads = {b.name: aw[:3, :3] @ np.array(b.head_local) + aw[:3, 3] for b in arm.data.bones}
    parents = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}
    for o in added:
        bpy.data.objects.remove(o)
    return rest, weights, names, matrices, covered, heads, parents, drift


def bowed(animation, bones):
    """The bones of one frame with the body bowed forward: everything from the hips' first spine
    bone up turned forward about that bone, then everything from the chest up again, and the head
    turned back up."""
    names, heads, parents = animation[2], animation[5], animation[6]

    def below(root):
        out = []
        for n in names:
            k = n
            while k is not None and k != root:
                k = parents[k]
            if k == root:
                out.append(names.index(n))
        return out

    bones = bones.copy()
    for root, degrees in (('mixamorig:Spine', WALK_HUNCH[0]), ('mixamorig:Spine2', WALK_HUNCH[1]),
                          ('mixamorig:Head', -HEAD_UP)):
        i = names.index(root)
        pivot = bones[i][:3, :3] @ heads[root] + bones[i][:3, 3]
        turn = np.eye(4)
        turn[:3, :3] = rot_x(degrees)
        turn[:3, 3] = pivot - turn[:3, :3] @ pivot
        for k in below(root):
            bones[k] = turn @ bones[k]
    return bones


ANIMATION = None
if os.path.exists(ANIMATED_FBX):
    wanted = {WALK_ACTION: (WALK_ACTION, WALK_FRAMES)}
    wanted.update({label: (action, frames) for label, action, frames in BLOWS})
    ANIMATION = load_animation(ANIMATED_FBX, wanted)
    if WALK_ANIMATED:
        POSES = len(WALK_FRAMES)
    # The animated model is the same shape at another size: this model's points go into its space
    # by their heights and the middles of the two.
    rest = ANIMATION[0]
    own = np.array([v.co for v in body.data.vertices])
    ANIMATION_SCALE = (rest[:, 2].max() - rest[:, 2].min()) / (own[:, 2].max() - own[:, 2].min())
    ANIMATION_SHIFT = np.array([
        (rest[:, 0].min() + rest[:, 0].max()) * 0.5 - (own[:, 0].min() + own[:, 0].max()) * 0.5 * ANIMATION_SCALE,
        (rest[:, 1].min() + rest[:, 1].max()) * 0.5 - (own[:, 1].min() + own[:, 1].max()) * 0.5 * ANIMATION_SCALE,
        rest[:, 2].min() - own[:, 2].min() * ANIMATION_SCALE])
    print('ANIMATION', os.path.basename(ANIMATED_FBX), 'walk', WALK_FRAMES, 'blows', [b[0] for b in BLOWS],
          'scale', round(ANIMATION_SCALE, 4))


# Walking, the club keeps its angle to the body while the hand carries it (as a heavy weapon is
# held steady): turned with the chest, not with the swinging wrist, which tipped it at the ground on
# every back swing. Tilted up from how it is built, to about 45 degrees (the angle the user settled
# on, 2026-11-15); degrees about x, up negative.
WALK_CLUB_TILT = -25.0 - sum(WALK_HUNCH)


def animated(animation, action_name, points, held, wear, clenched, steady=None, on_the_spot=False):
    """This model's points in each frame of one action of the animation: every point moves as the
    animated model's nearest vertex does; what is held, and the fist clenched round it, wholly with
    the right hand, and the hair with the head."""
    rest, weights, names, matrices = animation[:4]
    matrices = matrices[action_name]
    there = points * ANIMATION_SCALE + ANIMATION_SHIFT
    nearest = np.zeros(len(there), dtype=np.int64)
    for start in range(0, len(there), 400):
        part = there[start:start + 400]
        nearest[start:start + 400] = np.argmin(((part[:, None, :] - rest[None, :, :]) ** 2).sum(-1), axis=1)
    w = weights[nearest].copy()
    hand = np.zeros(len(names))
    hand[names.index('mixamorig:RightHand')] = 1.0
    head = np.zeros(len(names))
    head[names.index('mixamorig:Head')] = 1.0
    grip = np.where(held > 0.5, 1.0, clenched)[:, None]
    w = w * (1.0 - grip) + hand[None, :] * grip
    w[wear > 0.5] = head
    homogeneous = np.concatenate([there, np.ones((len(there), 1))], 1)
    out = []
    chest = names.index('mixamorig:Spine2')
    right = names.index('mixamorig:RightHand')
    centre = GRIP * ANIMATION_SCALE + ANIMATION_SHIFT
    drift = animation[7][action_name]
    for k, bones in enumerate(matrices):
        if steady is not None:
            bones = bowed(animation, bones)
        posed = np.einsum('vb,bij,vj->vi', w, bones, homogeneous)[:, :3]
        if steady is not None:
            # Longer steps: each leg reaches further forward and back from its hip (a shear along
            # the way it walks, so the foot stays flat and the leg barely lengthens).
            for side in ('Left', 'Right'):
                leg = [names.index('mixamorig:' + side + part) for part in ('UpLeg', 'Leg', 'Foot', 'ToeBase', 'Toe_End')]
                hip = bones[leg[0]][:3, :3] @ animation[5]['mixamorig:' + side + 'UpLeg'] + bones[leg[0]][:3, 3]
                posed[:, 1] += (WALK_REACH - 1.0) * (posed[:, 1] - hip[1]) * w[:, leg].sum(1)
            # What is held, and the fist round it: carried by the hand's grip and turned only the
            # way the chest faces (not with its lean or sway, nor with the swinging wrist).
            carried = bones[right][:3, :3] @ centre + bones[right][:3, 3]
            ahead = bones[chest][:3, :3] @ np.array([0.0, -1.0, 0.0])
            facing = math.degrees(math.atan2(ahead[0], -ahead[1]))
            turn = rot_x(('Z', -facing)) @ rot_x(steady)
            with_hand = there @ bones[right][:3, :3].T + bones[right][:3, 3]
            with_chest = (there - centre) @ turn.T + carried
            posed += grip * (with_chest - with_hand)
        if on_the_spot:
            posed = posed - drift[k]
        out.append((posed - ANIMATION_SHIFT) / ANIMATION_SCALE)
    return out


def grounded(posed, held, wear):
    posed = posed.copy()
    posed[:, 2] -= posed[(held == 0) & (wear == 0), 2].min()
    return posed


# ---------------------------------------------------------------- export
def mesh_arrays(obj, target_tris):
    """Positions (Blender metres), uvs and triangles of a decimated copy; vertices split where
    their uvs differ."""
    copy = obj.copy()
    copy.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(copy)
    tris = sum(len(p.vertices) - 2 for p in copy.data.polygons)
    if target_tris and tris > target_tris:
        m = copy.modifiers.new('decimate', 'DECIMATE')
        m.ratio = target_tris / tris
        m.use_collapse_triangulate = True
    m = copy.modifiers.new('tri', 'TRIANGULATE')
    deps = bpy.context.evaluated_depsgraph_get()
    mesh = copy.evaluated_get(deps).to_mesh()
    uv = mesh.uv_layers.active.data if mesh.uv_layers.active else None
    key_to_index, positions, uvs, indices = {}, [], [], []
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            u = tuple(round(x, 5) for x in uv[li].uv) if uv else (0.0, 0.0)
            key = (vi, u)
            if key not in key_to_index:
                key_to_index[key] = len(positions)
                positions.append(tuple(mesh.vertices[vi].co))
                uvs.append(u)
            indices.append(key_to_index[key])
    bpy.data.objects.remove(copy)
    return np.array(positions), np.array(uvs), np.array(indices, dtype=np.int32)


def carried_between(a, b, t, club, fist):
    """A row t of the way from row a to row b: the body in a straight line, the club turned
    as one piece about the fist and carried with it (so it does not shrink on the way, as a
    straight line from one end to the other would make it, nor leave the hand)."""
    out = a * (1.0 - t) + b * t
    pa, pb = a[club], b[club]
    ca, cb = a[fist].mean(0), b[fist].mean(0)
    u, _, vt = np.linalg.svd((pa - ca).T @ (pb - cb))
    d = np.sign(np.linalg.det(vt.T @ u.T))
    r = vt.T @ np.diag([1.0, 1.0, d]) @ u.T
    angle = math.acos(max(-1.0, min(1.0, (np.trace(r) - 1.0) * 0.5)))
    if angle > 1e-5 and math.sin(angle) > 1e-4:
        axis = np.array([r[2, 1] - r[1, 2], r[0, 2] - r[2, 0], r[1, 0] - r[0, 1]]) / (2.0 * math.sin(angle))
        k = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
        part = angle * t
        r = np.eye(3) + math.sin(part) * k + (1.0 - math.cos(part)) * (k @ k)
    else:
        r = np.eye(3) if angle <= 1e-5 else r
    out[club] = (pa - ca) @ r.T + ca * (1.0 - t) + cb * t
    return out


def to_unity(p):
    """Blender (x left, -y forward, z up) to Unity (x right, y up, z forward), in units of the
    horde's goblin scale."""
    k = HEIGHT / source_height / HORDE_SCALE
    return np.stack([-p[:, 0], p[:, 2], -p[:, 1]], 1) * k


def vertex_normals(p, idx):
    """Smooth normals of a posed mesh (Unity axes), one per place: the texture's seams split
    vertices, and an outline pushed out along split normals would tear open there. Turned to point
    away from the body."""
    tri = idx.reshape(-1, 3)
    n = np.cross(p[tri[:, 1]] - p[tri[:, 0]], p[tri[:, 2]] - p[tri[:, 0]])
    acc = np.zeros_like(p)
    for k in range(3):
        np.add.at(acc, tri[:, k], n)
    keys = np.round(p * 2000.0).astype(np.int64)
    _, group = np.unique(keys, axis=0, return_inverse=True)
    group = group.ravel()
    welded = np.zeros((group.max() + 1, 3))
    np.add.at(welded, group, acc)
    out = welded[group]
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-9)
    if np.sum(out * (p - p.mean(0))) < 0.0:
        out = -out
    return out


levels = []
stride = None
for target in LODS:
    bp, buv, bidx = mesh_arrays(body, target)
    # Body uvs move up into the top of the texture; each held or worn piece takes one colour from
    # the strip below (the facets shade it).
    buv = np.stack([buv[:, 0], STRIP / (TEX + STRIP) + buv[:, 1] * TEX / (TEX + STRIP)], 1)
    points, uvs, idx, held, wear = [bp], [buv], [bidx], [np.zeros(len(bp))], [np.zeros(len(bp))]
    count = len(bp)
    for thing, segment, held_number, worn_number in PIECES:
        cp, cuv, cidx = mesh_arrays(thing, None)
        points.append(cp)
        uvs.append(np.stack([np.full(len(cp), (segment + 0.5) / SEGMENTS),
                             np.full(len(cp), 0.5 * STRIP / (TEX + STRIP))], 1))
        idx.append(cidx + count)
        held.append(np.full(len(cp), float(held_number)))
        wear.append(np.full(len(cp), float(worn_number)))
        count += len(cp)
    points = np.concatenate(points)
    if not levels:
        first_points = points
    uvs = np.concatenate(uvs)
    idx = np.concatenate(idx)
    held = np.concatenate(held)
    wear = np.concatenate(wear)
    # The switch to Unity's axes mirrors the winding: reverse each triangle so its outside faces out
    # (otherwise Unity culls the outside and the face shows through the top of the head).
    idx = idx.reshape(-1, 3)[:, ::-1].ravel()
    w = weights_for(points)
    points, w, clenched = clench(points, w, ((held == 0) & (wear == 0)).astype(float), GRIP, GRIP_DIRECTION)
    # What is held follows the right hand, all of it; the hair follows the head.
    w[held > 0.5] = 0.0
    w[held > 0.5, ORDER.index('hand.R')] = 1.0
    w[wear > 0.5] = 0.0
    w[wear > 0.5, ORDER.index('head')] = 1.0
    frames = []
    def standing(angles, bob):
        # Set down on its lowest foot: bent legs carry the body lower, and it rises as a leg passes.
        posed = skin(points, w, arms_in(angles), bob)
        posed[:, 2] -= posed[(held == 0) & (wear == 0), 2].min()
        return to_unity(posed)

    spine_only = np.zeros((2, len(ORDER)))
    spine_only[:, ORDER.index('spine')] = 1.0
    hips_at = np.array([J['hip.L'], J['hip.R']])

    def hand_walk(phase):
        # The walk posed by hand at one moment of its cycle, set down on its lowest foot; each leg
        # reaching HAND_REACH times as far forward and back from its hip (a shear along the way it
        # walks, so the feet stay flat and the legs barely lengthen).
        angles, bob = pose(phase)
        angles = arms_in(angles)
        posed = skin(points, w, angles, bob)
        hips = skin(hips_at, spine_only, angles, bob)
        for k, side in enumerate(('L', 'R')):
            leg = [ORDER.index(part + '.' + side) for part in ('thigh', 'shin', 'foot')]
            posed[:, 1] += (HAND_REACH - 1.0) * (posed[:, 1] - hips[k, 1]) * w[:, leg].sum(1)
        return grounded(posed, held, wear)

    if ANIMATION and WALK_ANIMATED:
        walk = animated(ANIMATION, WALK_ACTION, points, held, wear, clenched, steady=WALK_CLUB_TILT)
        frames.extend(to_unity(grounded(posed, held, wear)) for posed in walk)
    else:
        for k in range(POSES):
            frames.append(to_unity(hand_walk(k / POSES)))
    if ANIMATION and WALK_ANIMATED:
        # Standing: two opposite moments of the walk averaged (the feet side by side in both), so
        # stopping and starting do not jump between the animated walk and a pose made by hand.
        frames.append(to_unity(grounded((walk[0] + walk[len(walk) // 2]) * 0.5, held, wear)))
    else:
        angles, bob = pose(0.0, still=True)
        frames.append(standing(angles, bob))
    for blow in ('raise', 'strike'):
        angles, bob = pose(0.0, blow=blow)
        frames.append(standing(angles, bob))
    for way in ('dead', 'dead_front', 'dead_side', 'dead_sprawl'):
        angles, bob = pose(0.0, blow=way)
        dead = skin(points, w, angles, bob)
        dead[:, 2] -= dead[(held == 0) & (wear == 0), 2].min()
        # Centred over the spot it died on, so each way of lying covers the same ground.
        lying = dead[(held == 0) & (wear == 0)]
        dead[:, :2] -= (lying[:, :2].min(0) + lying[:, :2].max(0)) * 0.5
        dead = to_unity(dead)
        frames.append(dead)
        frames.append(vertex_normals(dead, idx))
    # The blow's rows, after the dead rows (so the goblin's rows keep their places): the wind-up,
    # then the last three always the blow landing, held, and half way back to standing
    # (HordeGoblin.cginc _AttackRows).
    still_row = frames[POSES]
    if ANIMATION:
        # From the Meshy animation (user, 2026-11-15: posed by hand the blow moved the whole body
        # like a stick, with nothing in the legs or the back).
        # Each blow: two rows from standing into the wind-up's first pose (user, 2026-11-16: the
        # club jumped from where it rests to behind the back), its eight rows, and one on the way
        # back to standing. The first goes half way from the game's standing pose to the
        # animation's own, the club turned about the fist; the second and the last are the
        # animation's bones turned half way (load_animation).
        club = np.abs(held - 1.0) < 0.5
        fist = clenched > 0.5

        def rows(key):
            return [to_unity(grounded(posed, held, wear))
                    for posed in animated(ANIMATION, key, points, held, wear, clenched, on_the_spot=True)]

        for label, _, _ in BLOWS:
            blow_rows = rows(label)
            frames.append(carried_between(still_row, rows((label, 'standing'))[0], 0.5, club, fist))
            frames.extend(rows((label, 'into')))
            frames.extend(blow_rows)
            frames.extend(rows((label, 'out')))
    else:
        still = pose(0.0, still=True)
        raised = pose(0.0, blow='raise')
        struck = pose(0.0, blow='strike')
        lifting = between(still, raised, 0.5)
        # The wrist turns the club back only near the top: half turned it went through the head.
        lifting[0]['hand.R'] = 25.0
        blow_rows = [standing(angles, bob) for angles, bob in (lifting, raised, between(raised, struck, 0.5), struck, struck)]
        frames.extend(blow_rows)
        frames.append((blow_rows[-1] + still_row) * 0.5)
    levels.append((uvs, idx, frames, held, wear))
    if stride is None:
        # How far the body goes in one cycle, metres: each foot slides back under the body by the
        # length of a step while it is down, and a cycle is two steps. The game turns the walk at
        # this rate, so the feet keep pace with the ground.
        ankle = np.argmin(np.linalg.norm(points - J['ankle.L'], axis=1))
        reach = [fr[ankle, 2] for fr in frames[:POSES]]
        planted = 2.0 * (max(reach) - min(reach)) * HORDE_SCALE
        # A heavy walk keeps to what its feet cover, a little more: about 0.6 of its height a
        # cycle, so at a brute's pace (about 1.5 m a second) the legs turn a little over once a second.
        stride = max(planted, 0.6 * HEIGHT)
        # Measured (user, 2026-11-15: the brute took more steps than the ground it covered): through
        # the cycle in small steps, the foot on the ground (the lower ankle) slides back under the
        # body, and the sum of that is the ground one cycle covers. The rule above took the ankle's
        # whole swing, with the part where the foot is in the air.
        ankles = [np.argmin(np.linalg.norm(points - J['ankle.' + side], axis=1)) for side in ('L', 'R')]
        moments = [hand_walk(k / 96.0)[ankles] for k in range(97)]
        covered = 0.0
        for now, then in zip(moments[:-1], moments[1:]):
            down = 0 if now[0, 2] + then[0, 2] < now[1, 2] + then[1, 2] else 1
            covered += max(0.0, then[down, 1] - now[down, 1])
        measured = covered * HEIGHT / source_height
        print('HAND WALK feet cover', round(measured, 3), 'm a cycle; old rule', round(stride, 3))
        stride = measured
        if ANIMATION and WALK_ANIMATED:
            # The animation's own: what its planted foot covers in a cycle, at the brute's height
            # (user, 2026-11-15: the legs turned faster than the ground went by).
            rest = ANIMATION[0]
            # With the longer reach of the legs, and the little glide allowed.
            stride = ANIMATION[4] * WALK_REACH * WALK_GLIDE * HEIGHT / (rest[:, 2].max() - rest[:, 2].min())
        print('STRIDE metres a cycle', round(stride, 3), 'feet alone', round(planted, 3))
    print('LOD', target, 'vertices', len(points), 'triangles', len(idx) // 3)

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, 'brute.bytes'), 'wb') as f:
    f.write(b'GOBN')
    # Version 5: the number of walk poses follows the stride (the animated walk has 8, not 6).
    # Version 6: and how many different blows follow the dead rows.
    f.write(struct.pack('<iifii', 6, len(levels), stride, POSES, len(BLOWS) if ANIMATION else 1))
    for uvs, idx, frames, held, wear in levels:
        f.write(struct.pack('<iii', len(uvs), len(idx), len(frames)))
        f.write(uvs.astype('<f4').tobytes())
        f.write(held.astype('<f4').tobytes())
        f.write(wear.astype('<f4').tobytes())
        f.write(idx.astype('<i4').tobytes())
        for fr in frames:
            f.write(fr.astype('<f4').tobytes())

# The texture: the Meshy one shrunk, over a strip of wood.
img = bpy.data.images.load(TEXTURE)
w0, h0 = img.size
src = np.array(img.pixels[:]).reshape(h0, w0, 4)
ys = (np.arange(TEX) + 0.5) * h0 / TEX
xs = (np.arange(TEX) + 0.5) * w0 / TEX
small = np.zeros((TEX, TEX, 4))
# Box filter by averaging the source block under each pixel.
step = max(1, w0 // TEX)
small = src[:TEX * step, :TEX * step].reshape(TEX, step, TEX, step, 4).mean((1, 3))
# Lighter skin: the green pixels (green above red and blue), lightened smoothly by how green they
# are, so the bone, hide and wraps keep their colours.
rgb = small[:, :, :3]
greenness = np.clip((rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])) / 0.04, 0.0, 1.0)
small[:, :, :3] = np.clip(rgb * (1.0 + (SKIN_LIGHTEN - 1.0) * greenness[:, :, None]), 0.0, 1.0)
strip = np.zeros((STRIP, TEX, 4))
# Wood, stone, bone, fur (dark brown), hair (near black); one flat colour each.
materials = (np.array([0.58, 0.38, 0.19, 1.0]), np.array([0.46, 0.45, 0.43, 1.0]), np.array([0.80, 0.74, 0.60, 1.0]),
             np.array([0.30, 0.22, 0.16, 1.0]), np.array([0.07, 0.07, 0.06, 1.0]))
for x in range(TEX):
    strip[:, x] = materials[min(SEGMENTS - 1, x * SEGMENTS // TEX)]
strip[:, :, 3] = 1.0
full = np.concatenate([strip, small], 0)  # Blender images are stored bottom row first.
out = bpy.data.images.new('brute-tex', TEX, TEX + STRIP)
out.pixels = full.ravel().tolist()
out.filepath_raw = os.path.join(OUT, 'brute-tex.png')
out.file_format = 'PNG'
out.save()

# ---------------------------------------------------------------- preview
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    for o in list(bpy.context.scene.objects):
        bpy.data.objects.remove(o)
    uvs, idx, frames, held, wear = levels[0]
    mat = bpy.data.materials.new('M')
    mat.use_nodes = True
    nt = mat.node_tree
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = out
    t.interpolation = 'Closest'
    nt.links.new(t.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
    nt.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.9
    scene = bpy.context.scene
    names = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in names else 'BLENDER_EEVEE'
    scene.render.resolution_x = 360
    scene.render.resolution_y = 420
    world = bpy.data.worlds.new('W')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.75, 0.75, 1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    cam.data.type = 'ORTHO'
    # PREVIEW_HAND=1: close on the right hand (the fist round the club) instead of the whole body.
    hand_view = os.environ.get('PREVIEW_HAND') == '1'
    cam.data.ortho_scale = 1.4 if hand_view else 7.5
    scene.collection.objects.link(cam)
    scene.camera = cam
    for i, fr in enumerate(frames):
        # Back to Blender axes (z up) for the render.
        p = np.stack([-fr[:, 0], -fr[:, 2], fr[:, 1]], 1)
        me = bpy.data.meshes.new('f%d' % i)
        tri = idx.reshape(-1, 3)
        if hand_view:
            # Only the body and the club the game shows.
            tri = tri[np.all(held[tri] < 1.5, axis=1) & np.all(wear[tri] < 0.5, axis=1)]
        me.from_pydata(p.tolist(), [], tri.tolist())
        layer = me.uv_layers.new()
        for li, loop in enumerate(me.loops):
            layer.data[li].uv = uvs[loop.vertex_index]
        ob = bpy.data.objects.new('f%d' % i, me)
        ob.data.materials.append(mat)
        scene.collection.objects.link(ob)
        for view, (yaw, pitch) in ((('side', (-90, 5)), ('front', (0, 5)), ('top', (-40, 30))) if hand_view
                                   else (('side', (90, 5)), ('front', (25, 10)), ('top', (0, 75)))):
            a, pt = math.radians(yaw), math.radians(pitch)
            centre = Vector((0, 0, 2.6))
            if hand_view:
                # The fist in this frame, back in Blender axes.
                body = np.where((held == 0) & (wear == 0))[0]
                nearest = body[np.argmin(np.linalg.norm(first_points[body] - GRIP, axis=1))]
                centre = Vector(tuple(p[nearest]))
            cam.location = centre + Vector((math.sin(a) * math.cos(pt), -math.cos(a) * math.cos(pt), math.sin(pt))) * 8
            cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(PREVIEW, '%s_%d.png' % (view, i))
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(ob)
