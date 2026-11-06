"""Skeleton, skin weights, keyed throw and simulated sling for the baked TRELLIS slinger.

Everything is in Unity mesh space (Y up, the face towards +Z, the right hand at +X, 1.2 tall).
Usage: python rig.py baked.txt out_dir [preview]
Writes out_dir/slinger-rig.txt (triangles as vertex indices + colours + skin flag, plus a header
with the clip info) and out_dir/slinger-vat.bytes (float16 xyz per vertex per frame).
"""
import sys, os, json
import numpy as np

from preview import load

PERIOD = 2.6
FPS = 30
FRAMES = int(round(PERIOD * FPS))
RELEASE = 1.10

# ---------------------------------------------------------------- rotations (shader conventions)
def rx(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def ry(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rz(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def euler(yaw, pitch, roll):
    return ry(yaw) @ rx(pitch) @ rz(roll)


def normalize(v):
    return v / (np.linalg.norm(v) + 1e-12)


def frame(head, tail, pole):
    y = normalize(tail - head)
    z = normalize(pole - np.dot(pole, y) * y)
    x = np.cross(y, z)
    return np.stack([x, y, z], 1)


# ---------------------------------------------------------------- skeleton (rest pose)
J = {
    "root": np.array([0.0, 0.50, 0.0]),
    "waist": np.array([0.0, 0.60, 0.0]),
    "neck": np.array([0.0, 0.88, -0.01]),
}
for side, s in (("R", 1), ("L", -1)):
    J["shoulder" + side] = np.array([0.29 * s, 0.785, 0.0])
    J["elbow" + side] = np.array([0.365 * s, 0.615, -0.02])
    J["wrist" + side] = np.array([0.40 * s, 0.475, 0.01])
    J["hand" + side] = np.array([0.41 * s, 0.40, 0.03])  # grip point
    J["hip" + side] = np.array([0.12 * s, 0.46, 0.0])
    J["knee" + side] = np.array([0.13 * s, 0.26, 0.01])
    J["ankle" + side] = np.array([0.13 * s, 0.085, 0.0])

ARM_POLE_REST = {"R": normalize(np.array([0.6, 0.0, -0.8])), "L": normalize(np.array([-0.6, 0.0, -0.8]))}
LEG_POLE_REST = np.array([0.0, 0.0, 1.0])
UPPER = np.linalg.norm(J["elbowR"] - J["shoulderR"])
FORE = np.linalg.norm(J["wristR"] - J["elbowR"])
THIGH = np.linalg.norm(J["kneeR"] - J["hipR"])
SHIN = np.linalg.norm(J["ankleR"] - J["kneeR"])

BONES = ["pelvis", "chest", "head", "upperR", "foreR", "upperL", "foreL", "thighR", "shinR", "footR", "thighL", "shinL", "footL"]
B = {name: i for i, name in enumerate(BONES)}


def rest_frames():
    f = {}
    for name in ("pelvis", "chest", "head"):
        f[name] = (np.eye(3), J["root"] if name == "pelvis" else (J["waist"] if name == "chest" else J["neck"]))
    for side in "RL":
        f["upper" + side] = (frame(J["shoulder" + side], J["elbow" + side], ARM_POLE_REST[side]), J["shoulder" + side])
        f["fore" + side] = (frame(J["elbow" + side], J["wrist" + side], ARM_POLE_REST[side]), J["elbow" + side])
        f["thigh" + side] = (frame(J["hip" + side], J["knee" + side], LEG_POLE_REST), J["hip" + side])
        f["shin" + side] = (frame(J["knee" + side], J["ankle" + side], LEG_POLE_REST), J["knee" + side])
        f["foot" + side] = (np.eye(3), J["ankle" + side])
    return f


REST = rest_frames()


def two_bone(start, target, a, b, pole):
    d_vec = target - start
    d = np.clip(np.linalg.norm(d_vec), abs(a - b) + 1e-3, a + b - 1e-3)
    u = normalize(d_vec)
    p = normalize(pole - np.dot(pole, u) * u)
    cos_a = (a * a + d * d - b * b) / (2 * a * d)
    sin_a = np.sqrt(max(0.0, 1 - cos_a * cos_a))
    mid = start + a * (cos_a * u + sin_a * p)
    end = start + d * u
    return mid, end


# ---------------------------------------------------------------- keyed animation
# Channels keyed at times (seconds in the 2.6 s cycle). Values are interpolated with periodic
# Catmull-Rom splines, so every motion eases in and out with no jumps.
READY_R = [0.17, 0.53, 0.24]
READY_L = [-0.03, 0.50, 0.29]
KEYS = [
    # t, pelvis offset, pelvis yaw/pitch/roll, chest yaw/pitch/roll, R hand, R pole, L hand, L pole, R foot (xyz, yaw, lift), L foot
    dict(t=0.00, po=[0, -0.05, 0.0], pr=[0.05, 0.16, 0.0], cr=[0.05, 0.14, 0.0],
         rh=READY_R, rp=[0.6, -0.3, -0.7], lh=READY_L, lp=[-0.6, -0.4, -0.6],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.12, 0.085, 0.06, -0.1, 0]),
    # Back-swing: the right hand sweeps back and low, the torso turns away, the left hand
    # reaches towards the target.
    dict(t=0.30, po=[0.01, -0.06, -0.01], pr=[0.25, 0.16, 0.0], cr=[0.35, 0.12, -0.03],
         rh=[0.40, 0.46, -0.24], rp=[1.0, 0.0, 0.2], lh=[-0.16, 0.68, 0.36], lp=[-1, -0.3, 0],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.12, 0.085, 0.06, -0.1, 0]),
    # Sling up behind the shoulder.
    dict(t=0.60, po=[0.02, -0.04, -0.02], pr=[0.35, 0.06, -0.03], cr=[0.42, -0.04, 0.10],
         rh=[0.42, 0.96, -0.20], rp=[1.0, 0.0, -0.3], lh=[-0.14, 0.80, 0.42], lp=[-1, -0.2, 0],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.12, 0.085, 0.06, -0.1, 0]),
    # Loop over the head.
    dict(t=0.80, po=[0.02, -0.035, -0.02], pr=[0.28, 0.04, -0.04], cr=[0.32, -0.06, 0.16],
         rh=[0.18, 1.20, -0.04], rp=[1.0, 0.3, 0.2], lh=[-0.15, 0.82, 0.42], lp=[-1, -0.2, 0],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.12, 0.10, 0.08, -0.1, 0.1]),
    # Cocked behind the head, the front foot lifts for the step.
    dict(t=0.95, po=[0.01, -0.03, 0.0], pr=[0.12, 0.04, -0.04], cr=[0.26, -0.08, 0.16],
         rh=[0.24, 1.04, -0.22], rp=[0.3, 1.0, 0.6], lh=[-0.20, 0.78, 0.40], lp=[-1, -0.2, 0],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.13, 0.15, 0.13, -0.1, 0.2]),
    # Release: step down, hips and shoulders whip round, the arm drives at the target.
    dict(t=RELEASE, po=[-0.01, -0.06, 0.08], pr=[-0.30, 0.14, 0.04], cr=[-0.35, 0.20, 0.10],
         rh=[0.18, 0.96, 0.38], rp=[1.0, -0.6, 0.0], lh=[-0.32, 0.56, -0.08], lp=[-0.5, 0, -1],
         rf=[0.15, 0.09, -0.05, 0.15, 0.15], lf=[-0.13, 0.085, 0.22, -0.1, 0]),
    # Follow-through: the arm stays pointed at the target, the back foot comes up.
    dict(t=1.32, po=[-0.02, -0.07, 0.11], pr=[-0.45, 0.20, 0.04], cr=[-0.38, 0.24, 0.06],
         rh=[0.08, 0.80, 0.42], rp=[1.0, -0.6, 0.0], lh=[-0.33, 0.56, -0.12], lp=[-0.5, 0, -1],
         rf=[0.15, 0.17, -0.12, 0.15, 0.6], lf=[-0.13, 0.085, 0.22, -0.1, 0]),
    dict(t=1.55, po=[-0.02, -0.06, 0.10], pr=[-0.40, 0.18, 0.03], cr=[-0.30, 0.20, 0.04],
         rh=[0.12, 0.72, 0.38], rp=[1.0, -0.6, 0.0], lh=[-0.32, 0.56, -0.08], lp=[-0.5, 0, -1],
         rf=[0.15, 0.10, -0.06, 0.15, 0.25], lf=[-0.13, 0.085, 0.22, -0.1, 0]),
    # Recover: back foot down, weight back, the front foot steps back, the sling hangs.
    dict(t=1.85, po=[0, -0.035, 0.05], pr=[-0.15, 0.10, 0.0], cr=[-0.10, 0.08, 0.0],
         rh=[0.30, 0.55, 0.18], rp=[0.7, -0.3, -0.6], lh=[-0.30, 0.55, 0.04], lp=[-0.6, -0.3, -0.7],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.13, 0.085, 0.20, -0.1, 0]),
    dict(t=2.05, po=[0, -0.03, 0.02], pr=[0.0, 0.10, 0.0], cr=[0.0, 0.08, 0.0],
         rh=[0.26, 0.54, 0.22], rp=[0.7, -0.3, -0.6], lh=[-0.12, 0.55, 0.24], lp=[-0.6, -0.3, -0.7],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.125, 0.12, 0.12, -0.1, 0.1]),
    # Load: the left hand takes the pouch and fits a stone, crouching into the ready pose.
    dict(t=2.30, po=[0, -0.045, 0.0], pr=[0.03, 0.15, 0.0], cr=[0.03, 0.13, 0.0],
         rh=[0.18, 0.54, 0.24], rp=[0.6, -0.3, -0.7], lh=[-0.02, 0.48, 0.30], lp=[-0.6, -0.4, -0.6],
         rf=[0.15, 0.085, -0.05, 0.15, 0], lf=[-0.12, 0.085, 0.06, -0.1, 0]),
]
CHANNELS = ["po", "pr", "cr", "rh", "rp", "lh", "lp", "rf", "lf"]


def sample(channel, t):
    times = np.array([k["t"] for k in KEYS])
    values = np.array([k[channel] for k in KEYS], dtype=float)
    n = len(times)
    t = t % PERIOD
    i = np.searchsorted(times, t, side="right") - 1
    t0, t1 = times[i], times[(i + 1) % n] + (PERIOD if i + 1 >= n else 0)
    tm, tp = times[i - 1] - (PERIOD if i - 1 < 0 else 0), times[(i + 2) % n] + (PERIOD if i + 2 >= n else 0)
    p0, p1, p2, p3 = values[i - 1], values[i], values[(i + 1) % n], values[(i + 2) % n]
    # Non-uniform Catmull-Rom tangents (finite differences scaled to the segment length).
    h = t1 - t0
    m1 = ((p1 - p0) / (t0 - tm) * (t1 - t0) + (p2 - p1)) * 0.5 if True else 0
    m2 = ((p2 - p1) + (p3 - p2) / (tp - t1) * (t1 - t0)) * 0.5
    s = (t - t0) / h
    h00 = 2 * s ** 3 - 3 * s ** 2 + 1
    h10 = s ** 3 - 2 * s ** 2 + s
    h01 = -2 * s ** 3 + 3 * s ** 2
    h11 = s ** 3 - s ** 2
    return h00 * p1 + h10 * m1 + h01 * p2 + h11 * m2


def pose(t):
    """Bone transforms (R, rest head, posed head) and posed joint points at time t."""
    c = {name: sample(name, t) for name in CHANNELS}
    out = {}
    pts = {}
    Rp = euler(*c["pr"])
    root = J["root"] + c["po"]
    out["pelvis"] = (Rp, J["root"], root)
    pelvis = lambda p: Rp @ (p - J["root"]) + root
    Rc = Rp @ euler(*c["cr"])
    waist = pelvis(J["waist"])
    out["chest"] = (Rc, J["waist"], waist)
    chest = lambda p: Rc @ (p - J["waist"]) + waist
    # The head keeps looking at the target.
    yaw = -(c["pr"][0] + c["cr"][0]) * 0.8
    pitch = -(c["pr"][1] + c["cr"][1]) * 0.6
    roll = -(c["pr"][2] + c["cr"][2]) * 0.6
    Rh = Rc @ euler(yaw, pitch, roll)
    neck = chest(J["neck"])
    out["head"] = (Rh, J["neck"], neck)
    for side, hand_key, pole_key in (("R", "rh", "rp"), ("L", "lh", "lp")):
        shoulder = chest(J["shoulder" + side])
        pole = normalize(np.array(c[pole_key]))
        elbow, wrist = two_bone(shoulder, np.array(c[hand_key]), UPPER, FORE, pole)
        Ru = frame(shoulder, elbow, pole) @ REST["upper" + side][0].T
        Rf = frame(elbow, wrist, pole) @ REST["fore" + side][0].T
        out["upper" + side] = (Ru, J["shoulder" + side], shoulder)
        out["fore" + side] = (Rf, J["elbow" + side], elbow)
        pts["hand" + side] = Rf @ (J["hand" + side] - J["elbow" + side]) + elbow
        pts["wrist" + side] = wrist
    for side, key in (("R", "rf"), ("L", "lf")):
        foot = np.array(c[key])
        hip = pelvis(J["hip" + side])
        Rfoot = ry(foot[3]) @ rx(-foot[4])
        pole = Rfoot @ LEG_POLE_REST + np.array([0.15 if side == "R" else -0.15, 0, 0])
        knee, ankle = two_bone(hip, foot[:3], THIGH, SHIN, pole)
        out["thigh" + side] = (frame(hip, knee, pole) @ REST["thigh" + side][0].T, J["hip" + side], hip)
        out["shin" + side] = (frame(knee, ankle, pole) @ REST["shin" + side][0].T, J["knee" + side], knee)
        out["foot" + side] = (Rfoot, J["ankle" + side], ankle)
    return out, pts


# ---------------------------------------------------------------- mesh and weights
def weld(tris):
    flat = tris.reshape(-1, 3)
    keys = np.round(flat / 1e-4).astype(np.int64)
    uniq, index = np.unique(keys, axis=0, return_inverse=True)
    verts = np.zeros((len(uniq), 3))
    np.add.at(verts, index, flat)
    verts /= np.bincount(index, minlength=len(uniq))[:, None]
    return verts, index.reshape(-1, 3)


def seg_dist(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def weights(verts, faces, tri_parts):
    n = len(verts)
    W = np.zeros((n, len(BONES)))
    part = np.full(n, 0.0)
    for k in range(3):
        # Arm labels win over body labels on shared vertices.
        arm = (tri_parts > 0.1) & (tri_parts < 0.6)
        part[faces[arm, k]] = tri_parts[arm]
        rest = ~arm
        cur = part[faces[rest, k]]
        part[faces[rest, k]] = np.where((cur > 0.1) & (cur < 0.6), cur, tri_parts[rest])
    x, y, z = verts.T

    def inv_dist(cands):
        d = np.stack([seg_dist(verts, *seg)[0] for seg in cands.values()], 1)
        w = 1.0 / (d + 0.015) ** 4
        return w / w.sum(1, keepdims=True), list(cands.keys())

    # Arms: chest near the shoulder, upper arm, forearm (the hand goes with the forearm).
    for side, label, s in (("R", 0.5, 1), ("L", 0.25, -1)):
        m = np.abs(part - label) < 0.05
        w, names = inv_dist({
            "upper" + side: (J["shoulder" + side], J["elbow" + side]),
            "fore" + side: (J["elbow" + side], J["hand" + side] + (J["hand" + side] - J["wrist" + side])),
        })
        for k, name in enumerate(names):
            W[m, B[name]] = w[m, k]
        # Near the armpit and shoulder top some chest.
        _, t_up = seg_dist(verts, J["shoulder" + side], J["elbow" + side])
        chest_share = (1 - smoothstep(0.0, 0.35, t_up)) * 0.5 * (1 - smoothstep(0.25, 0.33, x * s))
        W[m] *= (1 - chest_share[m])[:, None]
        W[m, B["chest"]] += chest_share[m]

    body = np.abs(part) < 0.05
    pel = 1 - smoothstep(0.55, 0.68, y)
    head = smoothstep(0.86, 0.95, y)
    chest = 1 - pel - head
    chest = np.clip(chest, 0, 1)
    W[body, B["pelvis"]] = pel[body]
    W[body, B["chest"]] = chest[body]
    W[body, B["head"]] = head[body]
    # Shoulder caps that the bake left on the body follow the upper arms a little.
    for side, s in (("R", 1), ("L", -1)):
        cap = smoothstep(0.22, 0.30, x * s) * smoothstep(0.66, 0.74, y) * 0.6
        W[body] *= (1 - cap[body])[:, None]
        W[body, B["upper" + side]] += cap[body]

    # Legs and kilt.
    low = np.abs(part - 1.0) < 0.05
    for side, s in (("R", 1), ("L", -1)):
        side_w = smoothstep(-0.05, 0.05, x * s)
        thigh = smoothstep(0.20, 0.30, y) * (1 - smoothstep(0.42, 0.50, y))
        shin = smoothstep(0.07, 0.11, y) * (1 - smoothstep(0.20, 0.30, y))
        foot = 1 - smoothstep(0.07, 0.11, y)
        W[low, B["thigh" + side]] += (side_w * thigh)[low]
        W[low, B["shin" + side]] += (side_w * shin)[low]
        W[low, B["foot" + side]] += (side_w * foot)[low]
    W[low, B["pelvis"]] += smoothstep(0.42, 0.50, y)[low]
    # The kilt (outside the legs, above the knees) is carried half by the pelvis.
    kilt = low & (y > 0.26)
    leg_dist = np.minimum(np.abs(x - J["kneeR"][0]), np.abs(x - J["kneeL"][0]))
    kilt_share = (smoothstep(0.26, 0.34, y) * 0.55)
    W[kilt] *= (1 - kilt_share[kilt])[:, None]
    W[kilt, B["pelvis"]] += kilt_share[kilt]
    W /= W.sum(1, keepdims=True) + 1e-12

    # Smooth over the mesh so blends have no seams.
    nbr = [[] for _ in range(n)]
    for f in faces:
        for a in range(3):
            nbr[f[a]].extend([f[(a + 1) % 3], f[(a + 2) % 3]])
    nbr = [np.unique(v) for v in nbr]
    for _ in range(4):
        W2 = W.copy()
        for i in range(n):
            if len(nbr[i]):
                W2[i] = 0.5 * W[i] + 0.5 * W[nbr[i]].mean(0)
        W = W2
    W /= W.sum(1, keepdims=True)
    return W, part


def skin(verts, W, bones):
    out = np.zeros_like(verts)
    for name, i in B.items():
        R, rest_head, head = bones[name]
        w = W[:, i]
        m = w > 1e-4
        if m.any():
            out[m] += w[m, None] * ((verts[m] - rest_head) @ R.T + head)
    return out


# ---------------------------------------------------------------- sling
SLING_LEN = 0.42
SLING_N = 7
GRAVITY = np.array([0, -5.0, 0])
PIN_START, PIN_END = 2.18, 0.12  # the left hand holds the pouch (wraps round the cycle)
LOADED_FROM = 2.36               # the stone goes into the pouch


def pinned(t):
    t %= PERIOD
    # Blend weight for the pouch towards the left hand.
    if t >= PIN_START:
        return smoothstep(PIN_START, PIN_START + 0.1, t)
    if t <= PIN_END:
        return 1 - smoothstep(PIN_END - 0.08, PIN_END, t)
    return 0.0


# During the throw the sling direction (hand to pouch) is keyed, so the stone makes one clean
# overhand loop: trailing back on the back-swing, up over the head, whipped forward to the
# release. Outside this window the pouch is simulated.
GUIDE = [
    (0.20, (0.15, -0.95, 0.25)),
    (0.35, (0.25, -0.55, -0.80)),
    (0.55, (0.25, 0.30, -0.92)),
    (0.75, (0.15, 0.85, -0.50)),
    (0.95, (0.10, 0.99, -0.10)),
    (1.03, (0.05, 0.85, 0.50)),
    (1.10, (0.0, 0.40, 0.92)),
    (1.18, (0.0, 0.0, 1.0)),
]


def guide(t):
    """(direction, weight) of the keyed sling at time t."""
    t %= PERIOD
    w = smoothstep(0.20, 0.36, t) * (1 - smoothstep(1.10, 1.22, t))
    times = [k[0] for k in GUIDE]
    vals = [normalize(np.array(k[1], float)) for k in GUIDE]
    if t <= times[0]:
        return vals[0], w
    if t >= times[-1]:
        return vals[-1], w
    i = max(j for j in range(len(times)) if times[j] <= t)
    p0, p1 = vals[max(i - 1, 0)], vals[i]
    p2, p3 = vals[min(i + 1, len(vals) - 1)], vals[min(i + 2, len(vals) - 1)]
    s = (t - times[i]) / (times[i + 1] - times[i])
    v = 0.5 * ((2 * p1) + (-p0 + p2) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * s * s + (-p0 + 3 * p1 - 3 * p2 + p3) * s ** 3)
    return normalize(v), w


def simulate_sling():
    """The pouch is a weight on a rope from the right hand (never longer than the sling, slack
    allowed), simulated with Verlet over a few cycles so the recorded cycle loops. Returns per
    frame a polyline from the grip to the pouch: straight when taut, sagging when slack."""
    sub = 60
    dt = 1.0 / (FPS * sub)
    _, pts = pose(0.0)
    stone = pts["handR"] + np.array([0, -SLING_LEN, 0])
    prev = stone.copy()
    record = []
    cycles = 3
    for frame_index in range(FRAMES * cycles):
        for s in range(sub):
            t = (frame_index * sub + s) * dt
            _, pts = pose(t)
            hand = pts["handR"]
            vel = (stone - prev) * 0.9995
            prev = stone.copy()
            stone = stone + vel + GRAVITY * dt * dt
            d = stone - hand
            L = np.linalg.norm(d)
            if L > SLING_LEN:
                stone = hand + d / L * SLING_LEN
            direction, w = guide(t)
            if w > 0:
                stone = stone + (hand + direction * SLING_LEN - stone) * w
            pin = pinned(t)
            if pin > 0:
                target = pts["handL"] + np.array([0.0, -0.03, 0.0])
                stone = stone + (target - stone) * pin
        if frame_index >= FRAMES * (cycles - 1):
            record.append(rope(hand, stone))
    return record


def rope(hand, end, n=SLING_N):
    d = end - hand
    L = np.linalg.norm(d)
    slack = np.sqrt(max(0.0, SLING_LEN ** 2 - L ** 2)) * 0.5
    s = np.linspace(0, 1, n + 1)[:, None]
    return hand + d * s + np.array([0, -1, 0]) * slack * 4 * s * (1 - s)


def tube(points, radius, sides=3):
    """Ring vertices around a polyline; returns (verts, faces)."""
    verts, faces = [], []
    n = len(points)
    for i, p in enumerate(points):
        d = normalize(points[min(i + 1, n - 1)] - points[max(i - 1, 0)])
        a = normalize(np.cross(d, [0.3, 1, 0.2]))
        b = np.cross(d, a)
        for k in range(sides):
            ang = 2 * np.pi * k / sides
            verts.append(p + radius * (np.cos(ang) * a + np.sin(ang) * b))
    for i in range(n - 1):
        for k in range(sides):
            a0, a1 = i * sides + k, i * sides + (k + 1) % sides
            b0, b1 = a0 + sides, a1 + sides
            faces += [(a0, a1, b0), (a1, b1, b0)]  # outward by Cross(b - a, c - a), as Unity meshes
    return np.array(verts), faces


def blob(center, size, rng_seed):
    """Low-poly lump (octahedron with jitter)."""
    rng = np.random.default_rng(rng_seed)
    dirs = np.array([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]], float)
    dirs *= 1 + rng.uniform(-0.15, 0.15, (6, 1))
    faces = [(0, 2, 4), (4, 2, 1), (1, 2, 5), (5, 2, 0), (4, 3, 0), (1, 3, 4), (5, 3, 1), (0, 3, 5)]
    return center + dirs * size, faces


ROPE = (0.82, 0.72, 0.5)
LEATHER = (0.52, 0.33, 0.19)
STONE = (0.62, 0.58, 0.52)


def sling_geometry(chain, loaded):
    """Two cords from the grip to the pouch sides, the pouch and the stone."""
    hand, end = chain[0], chain[-1]
    d = normalize(end - chain[-2])
    side = normalize(np.cross(d, [0, 1, 0.01]))
    verts, faces, colors = [], [], []
    for s in (-1, 1):
        offs = np.linspace(0, 1, len(chain))[:, None] ** 2 * side * 0.028 * s
        v, f = tube(chain + offs, 0.008)
        base = sum(len(x) for x in verts)
        verts.append(v)
        faces += [(a + base, b + base, c + base) for a, b, c in f]
        colors += [ROPE] * len(f)
    base = sum(len(x) for x in verts)
    v, f = blob(end + d * 0.015, np.array([0.05, 0.03, 0.05]), 3)
    verts.append(v)
    faces += [(a + base, b + base, c + base) for a, b, c in f]
    colors += [LEATHER] * len(f)
    base = sum(len(x) for x in verts)
    v, f = blob(end + d * 0.02, np.array([0.037, 0.035, 0.037]), 5)
    if not loaded:
        v = np.repeat((end + d * 0.02)[None], len(v), 0)
    verts.append(v)
    faces += [(a + base, b + base, c + base) for a, b, c in f]
    colors += [STONE] * len(f)
    return np.concatenate(verts), faces, colors


def is_loaded(t):
    t %= PERIOD
    return t < RELEASE or t >= LOADED_FROM


# ---------------------------------------------------------------- main
def build(path):
    tris, colors, flags = load(path)
    verts, faces = weld(tris)
    faces = faces[:, [0, 2, 1]]  # the bake's winding is reversed; make Cross(b - a, c - a) outward
    W, part = weights(verts, faces, flags[:, 0])
    chains = simulate_sling()
    frames = []
    for f in range(FRAMES):
        t = f / FPS
        bones, pts = pose(t)
        body = skin(verts, W, bones)
        sv, sf, sc = sling_geometry(chains[f], is_loaded(t))
        frames.append(np.concatenate([body, sv]))
    release_frame = RELEASE * FPS
    f0 = int(np.floor(release_frame))
    stone = chains[f0][-1]
    return dict(verts=verts, faces=faces, colors=colors, skin=flags[:, 1], W=W, part=part,
                frames=np.array(frames), sling_faces=sf, sling_colors=sc, body_count=len(verts),
                chains=chains, release_stone=stone)


if __name__ == "__main__":
    data = build(sys.argv[1])
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    np.save(os.path.join(out, "frames.npy"), data["frames"])
    print("verts", data["body_count"], "frame verts", data["frames"].shape, "release stone", data["release_stone"])
