"""The horde goblin (user, 2026-11-13): the Meshy "Grimble the Goblin", cut down to two levels of
detail, rigged and walked here, and baked for the GPU horde.

The horde draws up to 100 000 goblins in a few instanced draws, so no skinning runs in the game:
every pose's vertex positions are baked ("vertex animation"), and the shader picks the pose.

- Three levels of detail are made with Blender's collapse decimation: about 4000 triangles near the
  camera, 350 far away, and 120 (with what it holds and wears cut down too) for goblins only a few
  dozen pixels tall (optimization, 2026-11-16).
- Three things to hold are added in the right hand, each marked with its own number so the shader
  shows one of them (or none) on each goblin: a wooden club (1), a bone (2) and a stone (3). The
  club arm swings forward with the left leg, as in the approved walk.
- The skeleton is placed by hand from the model's proportions (it has no rig), and each vertex
  follows the bones nearest to it, blended near the joints.
- The walk is the approved one (2026-11-04): six held poses a cycle, the arms swinging against the
  legs, a slight forward lean and a bob, no twist. Pose 6 stands still, pose 7 raises the weapon
  and pose 8 brings it down (the blow on a caveman or the wall). Then four ways to lie dead, each
  as two rows: the pose, flat on the ground, and its vertex normals (for the corpse's outline):
  on its back with arms flung out, face down, curled on its side, and sprawled on its back.
- Hair, so goblins differ a little (user, 2026-11-15: hoods, skulls and mantles made a crowd harder
  to read; only small differences), each marked with its own number so the shader shows it on some
  goblins only: a long tail of hair hanging down the back (1) and a taller, spikier crest (2). They
  follow the head.

Writes `goblin.bytes` (format below) and `goblin-tex.png`: the Meshy texture at 512 with a strip of
wood, bone, stone, fur and hair for the things it holds and wears along the bottom.

    "GOBN", int version (4), int lodCount, float metres the body moves in a walk cycle
    per level: int vertexCount, int indexCount, int frameCount,
               float2 uv[vertexCount], float held[vertexCount] (0 body, 1 club, 2 bone, 3 stone),
               float wear[vertexCount] (0 body, 1 hair tail, 2 hair crest),
               int index[indexCount],
               float3 position[frameCount][vertexCount]   (Unity axes, metres per unit of scale)

Run: blender -b --python build_goblin.py -- <goblin.fbx> <texture.png> <out folder> [preview folder]
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

# The goblin's height in the game, metres, and the horde's scale for a goblin (EnemyDefinition
# visualScale 0.36): the mesh is written in units of that scale.
HEIGHT = 1.28
HORDE_SCALE = 0.36
LODS = (4000, 350, 60, 24)
# The things held and worn, decimated only in the third, tiny level (optimization, 2026-11-16: far
# from the camera, at a few dozen pixels tall, the full club and hair were half the vertices drawn).
# Halved again for 100 000 enemies (2026-11-16): 107 -> 183 fps there, and the same look at 70 m.
PIECE_TRIS = (None, None, 10, 0)
# The fourth level only casts the shadows: no held or worn pieces (0). Its texture seams stay split:
# welded (48 vertices instead of 147) it drew slower, as the welded tiny level did (2026-11-17).
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
# Blender axes as imported: x to the goblin's left, -y forward, z up; metres of the Meshy model
# (0.95 tall). Measured from height slices of the mesh.
def mirror(p):
    return (-p[0], p[1], p[2])

JOINTS = {
    'hips': (0.0, 0.12, 0.36), 'chest': (0.0, 0.08, 0.52), 'neck': (0.0, 0.03, 0.57),
    'head_top': (0.0, -0.04, 0.92),
    'hip.L': (0.07, 0.12, 0.33), 'knee.L': (0.12, 0.11, 0.20), 'ankle.L': (0.12, 0.14, 0.07),
    'toe.L': (0.14, 0.02, 0.02),
    'shoulder.L': (0.13, 0.10, 0.52), 'elbow.L': (0.21, 0.14, 0.43), 'wrist.L': (0.25, 0.07, 0.29),
    'finger.L': (0.28, 0.03, 0.12),
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
            d[points[:, 2] < 0.50, i] = 1e3
        if b.startswith(('thigh', 'shin', 'foot')):
            d[points[:, 2] > 0.40, i] = 1e3
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
        a['spine'] = 2.0
        return a, 0.0
    if blow == 'raise':
        # The weapon up behind the head, the body leaning back.
        a['spine'] = -8.0
        a['head'] = -4.0
        a['upperarm.R'] = -150.0
        a['forearm.R'] = -35.0
        a['upperarm.L'] = 15.0
        return a, 0.01
    if blow == 'strike':
        # Brought down in front, the body thrown forward over a bent front leg.
        a['spine'] = 20.0
        a['head'] = -8.0
        a['upperarm.R'] = -62.0
        a['forearm.R'] = -8.0
        a['upperarm.L'] = 25.0
        a['thigh.L'] = -14.0
        a['shin.L'] = 22.0
        return a, -0.015
    s = math.sin(2 * math.pi * phase)
    c = math.cos(2 * math.pi * phase)
    swing, knee, arm = 38.0, 55.0, 36.0
    a['thigh.L'] = -swing * s
    a['thigh.R'] = swing * s
    # The knee bends while its leg swings forward, and keeps a little bend on the ground.
    a['shin.L'] = 8.0 + knee * max(0.0, c)
    a['shin.R'] = 8.0 + knee * max(0.0, -c)
    a['foot.L'] = -0.6 * (a['thigh.L'] + a['shin.L'])
    a['foot.R'] = -0.6 * (a['thigh.R'] + a['shin.R'])
    # The club (right) arm forward with the left leg.
    a['upperarm.R'] = -arm * s
    a['upperarm.L'] = arm * s
    a['forearm.R'] = -10.0 - 8.0 * max(0.0, s)
    a['forearm.L'] = -10.0 - 8.0 * max(0.0, -s)
    a['spine'] = 6.0 + 2.0 * abs(s)
    a['head'] = -3.0
    # Lowest with both feet down, highest as a leg passes the other.
    bob = 0.03 * (c * c) - 0.008
    return a, bob


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
# Inside the fist: just in from the knuckles, towards the palm (the body side).
GRIP = J['knuckle.R'] + np.array([0.03, 0.0, -0.012])
GRIP_DIRECTION = (0.0, -0.95, -0.18)

# The weapon hand is squeezed shut round the handle (user, 2026-11-15: bending the fingers alone
# still read as an open hand): every vertex of the hand is drawn in towards the handle's line, to
# within FIST_RADIUS of it and FIST_HALF along it either side of the grip, more the further it is
# from the wrist, so the hand becomes a fist wrapped round the handle and stays joined at the wrist.
FIST_RADIUS = 0.032
FIST_HALF = 0.03


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
    return points, w


def add_held(kind):
    """Something in the right fist, pointing forward and a little down: a knobbly wooden club, a
    bone with a knuckle at each end, or a rough stone held in the hand."""
    if kind == 'club':
        bpy.ops.mesh.primitive_cone_add(vertices=7, radius1=0.022, radius2=0.05, depth=0.27)
    elif kind == 'bone':
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.017, depth=0.24)
    else:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.055)
    club = bpy.context.active_object
    for v in club.data.vertices:
        if kind == 'club' and v.co.z > 0.1:
            v.co.x *= 1.0 + 0.25 * math.sin(v.index * 1.7)
            v.co.y *= 1.0 + 0.25 * math.cos(v.index * 2.3)
        if kind == 'stone':
            v.co *= 1.0 + 0.22 * math.sin(v.index * 2.1)
            v.co.z *= 0.8
    if kind == 'bone':
        for end in (-0.12, 0.12):
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.034, location=(0.012, 0, end))
            a = bpy.context.active_object
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.034, location=(-0.012, 0, end))
            b = bpy.context.active_object
            for o in (a, b, club):
                o.select_set(True)
            bpy.context.view_layer.objects.active = club
            bpy.ops.object.join()
    grip = GRIP
    direction = Vector(GRIP_DIRECTION).normalized()
    club.rotation_mode = 'QUATERNION'
    club.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(direction)
    club.location = Vector(grip) + direction * (0.12 if kind == 'club' else 0.07 if kind == 'bone' else 0.02)
    bpy.ops.object.select_all(action='DESELECT')
    club.select_set(True)
    bpy.context.view_layer.objects.active = club
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    club.name = kind
    return club


HELD = [add_held(kind) for kind in ('club', 'bone', 'stone')]


# ---------------------------------------------------------------- hair
# The head, measured from slices: the scalp at 0.88, the black tuft above it to 0.95 a little behind
# the middle (+y 0.03 to 0.07).
def add_hair(kind):
    # A tail of hair hanging from the tuft down the back of the head, or a taller crest of spikes
    # along the middle of the scalp.
    if kind == 'tail':
        direction = Vector((0.0, 0.75, -0.66)).normalized()
        bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.028, radius2=0.01, depth=0.2)
        part = bpy.context.active_object
        part.rotation_mode = 'QUATERNION'
        part.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(direction)
        part.location = Vector((0.0, 0.1, 0.9)) + direction * 0.1
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.022, radius2=0.0, depth=0.1, location=(0.0, -0.02, 0.92))
        part = bpy.context.active_object
        for y, height in ((0.03, 0.12), (0.08, 0.09)):
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.022, radius2=0.0, depth=height,
                                            location=(0.0, y, 0.88 + height * 0.5),
                                            rotation=(math.radians(18), 0.0, 0.0))
            spike = bpy.context.active_object
            for o in (spike, part):
                o.select_set(True)
            bpy.context.view_layer.objects.active = part
            bpy.ops.object.join()
    bpy.ops.object.select_all(action='DESELECT')
    part.select_set(True)
    bpy.context.view_layer.objects.active = part
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    part.name = kind
    return part


WORN = [add_hair(kind) for kind in ('tail', 'crest')]
# Strip segments (fifths of the width): wood, bone, stone, fur, hair. Both are hair.
WORN_SEGMENT = {'tail': 4, 'crest': 4}
SEGMENTS = 5


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
for level_number, target in enumerate(LODS):
    bp, buv, bidx = mesh_arrays(body, target)
    # Body uvs move up into the top of the texture; the held and worn things sample the strip
    # below, a fifth of its width each: wood, bone, stone, fur, hide.
    buv = np.stack([buv[:, 0], STRIP / (TEX + STRIP) + buv[:, 1] * TEX / (TEX + STRIP)], 1)
    points, uvs, idx, held, wear = [bp], [buv], [bidx], [np.zeros(len(bp))], [np.zeros(len(bp))]
    count = len(bp)
    for number, thing in enumerate(HELD):
        if PIECE_TRIS[level_number] == 0:
            continue
        cp, cuv, cidx = mesh_arrays(thing, PIECE_TRIS[level_number])
        u = (number + 0.1 + 0.8 * (np.arange(len(cp)) % 7) / 6.0) / SEGMENTS
        points.append(cp)
        uvs.append(np.stack([u, np.full(len(cp), 0.5 * STRIP / (TEX + STRIP))], 1))
        idx.append(cidx + count)
        held.append(np.full(len(cp), number + 1.0))
        wear.append(np.zeros(len(cp)))
        count += len(cp)
    for number, thing in enumerate(WORN):
        if PIECE_TRIS[level_number] == 0:
            continue
        cp, cuv, cidx = mesh_arrays(thing, PIECE_TRIS[level_number])
        # One colour each (the facets shade it): a spread across the strip read as stripes.
        u = np.full(len(cp), (WORN_SEGMENT[thing.name] + 0.5) / SEGMENTS)
        points.append(cp)
        uvs.append(np.stack([u, np.full(len(cp), 0.5 * STRIP / (TEX + STRIP))], 1))
        idx.append(cidx + count)
        held.append(np.zeros(len(cp)))
        wear.append(np.full(len(cp), number + 1.0))
        count += len(cp)
    points = np.concatenate(points)
    uvs = np.concatenate(uvs)
    idx = np.concatenate(idx)
    held = np.concatenate(held)
    wear = np.concatenate(wear)
    # The switch to Unity's axes mirrors the winding: reverse each triangle so its outside faces out
    # (otherwise Unity culls the outside and the face shows through the top of the head).
    idx = idx.reshape(-1, 3)[:, ::-1].ravel()
    w = weights_for(points)
    points, w = clench(points, w, ((held == 0) & (wear == 0)).astype(float), GRIP, GRIP_DIRECTION)
    # What is held follows the right hand, all of it; the hair follows the head.
    w[held > 0.5] = 0.0
    w[held > 0.5, ORDER.index('hand.R')] = 1.0
    w[wear > 0.5] = 0.0
    w[wear > 0.5, ORDER.index('head')] = 1.0
    frames = []
    for k in range(POSES):
        angles, bob = pose(k / POSES)
        frames.append(to_unity(skin(points, w, angles, bob)))
    angles, bob = pose(0.0, still=True)
    frames.append(to_unity(skin(points, w, angles, bob)))
    for blow in ('raise', 'strike'):
        angles, bob = pose(0.0, blow=blow)
        frames.append(to_unity(skin(points, w, angles, bob)))
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
    levels.append((uvs, idx, frames, held, wear))
    if stride is None:
        # How far the body goes in one cycle, metres: each foot slides back under the body by the
        # length of a step while it is down, and a cycle is two steps. The game turns the walk at
        # this rate, so the feet keep pace with the ground.
        ankle = np.argmin(np.linalg.norm(points - J['ankle.L'], axis=1))
        reach = [fr[ankle, 2] for fr in frames[:POSES]]
        planted = 2.0 * (max(reach) - min(reach)) * HORDE_SCALE
        # Measured in Level 2 (2026-11-14) the goblins cover 1.8 m a second. With only the ground
        # the planted feet cover (about 0.6 m a cycle) that took three cycles a second and read as
        # frantic running on the spot (user). They lope instead: a bounding run covers more than
        # the legs reach, about 1.1 body heights a cycle, so the legs turn about 1.5 times a second.
        # The user then asked for half that rate again, which slid the feet too far; settled between
        # the two (2026-11-14): 1.15 body heights a cycle, about 1.2 cycles a second.
        stride = max(planted, 1.15 * HEIGHT)
        print('STRIDE metres a cycle', round(stride, 3), 'feet alone', round(planted, 3))
    print('LOD', target, 'vertices', len(points), 'triangles', len(idx) // 3)

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, 'goblin.bytes'), 'wb') as f:
    f.write(b'GOBN')
    f.write(struct.pack('<iif', 4, len(levels), stride))
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
strip = np.zeros((STRIP, TEX, 4))
# Wood, bone, stone, fur (grey-brown), hair (the tuft's near black).
materials = (np.array([0.36, 0.22, 0.11, 1.0]), np.array([0.80, 0.74, 0.60, 1.0]), np.array([0.42, 0.41, 0.40, 1.0]),
             np.array([0.36, 0.30, 0.25, 1.0]), np.array([0.07, 0.07, 0.06, 1.0]))
for x in range(TEX):
    segment = min(SEGMENTS - 1, x * SEGMENTS // TEX)
    if segment == 0:
        shade = 0.75 + 0.35 * ((x // (TEX // 21)) % 2) + 0.08 * math.sin(x * 0.7)
    else:
        shade = 0.92 + 0.1 * math.sin(x * 0.37)
    strip[:, x] = np.clip(materials[segment] * shade, 0, 1)
strip[:, :, 3] = 1.0
full = np.concatenate([strip, small], 0)  # Blender images are stored bottom row first.
out = bpy.data.images.new('goblin-tex', TEX, TEX + STRIP)
out.pixels = full.ravel().tolist()
out.filepath_raw = os.path.join(OUT, 'goblin-tex.png')
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
    cam.data.ortho_scale = 3.6
    scene.collection.objects.link(cam)
    scene.camera = cam
    for i, fr in enumerate(frames):
        # Back to Blender axes (z up) for the render.
        p = np.stack([-fr[:, 0], -fr[:, 2], fr[:, 1]], 1)
        me = bpy.data.meshes.new('f%d' % i)
        me.from_pydata(p.tolist(), [], idx.reshape(-1, 3).tolist())
        layer = me.uv_layers.new()
        for li, loop in enumerate(me.loops):
            layer.data[li].uv = uvs[loop.vertex_index]
        ob = bpy.data.objects.new('f%d' % i, me)
        ob.data.materials.append(mat)
        scene.collection.objects.link(ob)
        for view, (yaw, pitch) in (('side', (90, 5)), ('front', (25, 10)), ('top', (0, 75))):
            a, pt = math.radians(yaw), math.radians(pitch)
            centre = Vector((0, 0, 1.45))
            cam.location = centre + Vector((math.sin(a) * math.cos(pt), -math.cos(a) * math.cos(pt), math.sin(pt))) * 8
            cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(PREVIEW, '%s_%d.png' % (view, i))
            bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(ob)
