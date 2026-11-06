"""Overhead-whirl sling throw for the Meshy warrior, keyed directly on the Meshy skeleton.

Keys are in character space: Unity axes (x right, y up, z forward / towards the target),
metres, the Meshy model's own size. The sling is whirled flat over the head for two turns and
let go when the pouch passes the right side moving forward, then the arm whips forward.

Arm bones use swing-from-forward rotations (minimal rotation from a forward-pointing reference),
so twist never flips: every arm direction in this throw stays well away from pointing backwards.

Usage: python throw2.py rest.json out.json   (same output format as retarget.py)
"""
import sys, os, json
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retarget as base  # noqa: E402  (reads rest.json from argv[1])
from retarget import M, U, REST, PARENT, ORDER, BONES, head, rot, slerp_rot, axis_angle, quat  # noqa: E402

PERIOD = 2.6
FPS = 30
FRAMES = int(round(PERIOD * FPS))
RELEASE = 1.45
LOADED_FROM = 2.36
PIN_START, PIN_END = 2.15, 0.15
SLING_LEN = base.SLING_LEN
SLING_N = 7
GRAVITY = np.array([0, 0, -5.0 * base.S])


def normalize(v):
    return v / (np.linalg.norm(v) + 1e-12)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def ry(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rx(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rz(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def euler(yaw, pitch, roll):
    return ry(yaw) @ rx(pitch) @ rz(roll)


def C(name):
    """Rest head of a bone in character space."""
    return M.T @ head(name)


OVER = [0.25, 1.64, 0.02]  # centre of the whirl: the right wrist up above the right shoulder
KEYS = [
    # Ready: sling hand at the right hip, the left hand holds the pouch in front of the belly.
    dict(t=0.00, sh=[0.0], po=[0, -0.03, 0], pr=[0.05, 0.06, 0], cr=[0.05, 0.08, 0],
         rw=[0.30, 0.80, 0.18], rp=[0.6, -0.2, -0.8], lw=[0.02, 0.86, 0.30], lp=[-0.6, -0.2, -0.8],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.06, -0.1, 0]),
    # Lift the sling up the right side.
    dict(t=0.30, sh=[0.15], po=[0, -0.02, 0], pr=[0.08, 0.03, 0], cr=[0.10, 0.0, 0.05],
         rw=[0.32, 1.30, 0.12], rp=[1, -0.3, -0.3], lw=[-0.05, 0.95, 0.30], lp=[-0.8, -0.3, -0.5],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.06, -0.1, 0]),
    # Over the head: the whirl starts. The left arm points at the target.
    dict(t=0.55, sh=[0.42], po=[0, -0.02, 0], pr=[0.10, 0.03, 0], cr=[0.10, -0.02, 0.04],
         rw=OVER, rp=[1, 0.1, 0.3], lw=[-0.20, 1.02, 0.34], lp=[-1, -0.3, 0],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.06, -0.1, 0]),
    dict(t=0.95, sh=[0.42], po=[0, -0.025, 0], pr=[0.16, 0.04, 0], cr=[0.14, -0.03, 0.04],
         rw=OVER, rp=[1, 0.1, 0.3], lw=[-0.20, 1.02, 0.34], lp=[-1, -0.3, 0],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.06, -0.1, 0]),
    # Wound up, the front foot lifts for the step.
    dict(t=1.28, sh=[0.42], po=[0, -0.02, -0.01], pr=[0.24, 0.02, 0], cr=[0.20, -0.05, 0.05],
         rw=[0.26, 1.63, -0.02], rp=[1, 0.1, 0.2], lw=[-0.22, 1.00, 0.32], lp=[-1, -0.3, 0],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0.06, 0.08, -0.1, 0.2]),
    # Release: step down, hips and shoulders whip round, the arm drives forward.
    dict(t=RELEASE, sh=[0.25], po=[0, -0.05, 0.07], pr=[-0.18, 0.12, 0.03], cr=[-0.22, 0.14, 0.05],
         rw=[0.16, 1.40, 0.30], rp=[1, -0.2, 0.2], lw=[-0.26, 0.95, 0.16], lp=[-0.8, -0.3, -0.5],
         rf=[0, 0, 0, 0.15, 0.1], lf=[0, 0, 0.20, -0.1, 0]),
    # Follow-through across the body, the back heel comes up.
    dict(t=1.68, sh=[0.05], po=[0, -0.06, 0.09], pr=[-0.34, 0.18, 0.03], cr=[-0.28, 0.20, 0.04],
         rw=[0.02, 1.02, 0.40], rp=[1, -0.6, 0], lw=[-0.30, 0.92, 0.05], lp=[-0.7, -0.3, -0.6],
         rf=[0, 0.07, -0.05, 0.15, 0.5], lf=[0, 0, 0.20, -0.1, 0]),
    # Recover.
    dict(t=1.92, sh=[0.0], po=[0, -0.035, 0.04], pr=[-0.12, 0.08, 0], cr=[-0.08, 0.06, 0],
         rw=[0.25, 0.90, 0.25], rp=[0.7, -0.3, -0.6], lw=[-0.22, 0.90, 0.14], lp=[-0.7, -0.3, -0.6],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.18, -0.1, 0]),
    # The front foot steps back, the left hand takes the pouch.
    dict(t=2.15, sh=[0.0], po=[0, -0.03, 0.01], pr=[0.0, 0.07, 0], cr=[0.0, 0.08, 0],
         rw=[0.28, 0.82, 0.22], rp=[0.6, -0.2, -0.8], lw=[0.05, 0.84, 0.30], lp=[-0.6, -0.2, -0.8],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0.04, 0.10, -0.1, 0.1]),
    # Load a stone.
    dict(t=2.36, sh=[0.0], po=[0, -0.04, 0], pr=[0.04, 0.08, 0], cr=[0.04, 0.10, 0],
         rw=[0.30, 0.81, 0.19], rp=[0.6, -0.2, -0.8], lw=[0.04, 0.85, 0.30], lp=[-0.6, -0.2, -0.8],
         rf=[0, 0, 0, 0.15, 0], lf=[0, 0, 0.06, -0.1, 0]),
]
CHANNELS = ["sh", "po", "pr", "cr", "rw", "rp", "lw", "lp", "rf", "lf"]
TIMES = np.array([k["t"] for k in KEYS])
VALUES = {ch: np.array([k[ch] for k in KEYS], float) for ch in CHANNELS}


def sample(channel, t):
    """Periodic non-uniform Catmull-Rom (eased, no jumps)."""
    times, values, n = TIMES, VALUES[channel], len(TIMES)
    t = t % PERIOD
    i = np.searchsorted(times, t, side="right") - 1
    t0 = times[i]
    t1 = times[(i + 1) % n] + (PERIOD if i + 1 >= n else 0)
    tm = times[i - 1] - (PERIOD if i - 1 < 0 else 0)
    tp = times[(i + 2) % n] + (PERIOD if i + 2 >= n else 0)
    p0, p1, p2, p3 = values[i - 1], values[i], values[(i + 1) % n], values[(i + 2) % n]
    h = t1 - t0
    m1 = ((p1 - p0) / (t0 - tm) * h + (p2 - p1)) * 0.5
    m2 = ((p2 - p1) + (p3 - p2) / (tp - t1) * h) * 0.5
    s = (t - t0) / h
    return (2 * s ** 3 - 3 * s ** 2 + 1) * p1 + (s ** 3 - 2 * s ** 2 + s) * m1 + (-2 * s ** 3 + 3 * s ** 2) * p2 + (s ** 3 - s ** 2) * m2


# ---------------------------------------------------------------- whirl
WHIRL_START = 0.38
OMEGA = 2 * np.pi * 1.7  # turns per second at full speed


def omega(t):
    return OMEGA * smoothstep(WHIRL_START, 0.85, t)


_grid = np.linspace(WHIRL_START, RELEASE, 2000)
_cum = np.concatenate([[0], np.cumsum((omega(_grid[1:]) + omega(_grid[:-1])) * 0.5 * np.diff(_grid))])


def theta(t):
    """Pouch angle round the hand (from +x towards +z); 0 (mod 2 pi) at the release."""
    t = t % PERIOD
    return float(np.interp(t, _grid, _cum) - _cum[-1])


# After the release the empty sling is still keyed: it keeps turning the same way, slowing to a
# stop within SPIN_DOWN seconds (so it ends up in front of the body, never behind it), and droops
# down to hang from the hand; then the simulation takes over.
SPIN_DOWN = float(os.environ.get("SPIN_DOWN", "0.55"))
HANG = np.pi * 0.5 * 0.92


def whirl_weight(t):
    t = t % PERIOD
    if t < RELEASE:
        return float(smoothstep(WHIRL_START, 0.62, t))
    after = t - RELEASE
    return float(1 - smoothstep(SPIN_DOWN + 0.25, SPIN_DOWN + 0.5, after))


def hand_weight(t):
    t = t % PERIOD
    if t < RELEASE:
        return float(smoothstep(WHIRL_START, 0.62, t))
    return float(1 - smoothstep(0.0, 0.15, t - RELEASE))


def whirl_dir(t):
    t = t % PERIOD
    if t < RELEASE:
        a = theta(t)
        droop = 0.35 - 0.2 * smoothstep(0.6, 1.0, t)
    else:
        after = t - RELEASE
        spin = min(after, SPIN_DOWN)
        a = OMEGA * (spin - spin * spin / (2 * SPIN_DOWN))
        droop = 0.15 + (HANG - 0.15) * smoothstep(SPIN_DOWN * 0.4, SPIN_DOWN + 0.3, after)
    return np.array([np.cos(a) * np.cos(droop), -np.sin(droop), np.sin(a) * np.cos(droop)])


# ---------------------------------------------------------------- pose
def swing(a, b):
    """Minimal rotation taking direction a to direction b."""
    a, b = normalize(a), normalize(b)
    axis = np.cross(a, b)
    s = np.linalg.norm(axis)
    if s < 1e-9:
        return np.eye(3)
    return axis_angle(axis, np.arctan2(s, np.dot(a, b)))


FORWARD = U([0, 0, 1.0])
ARM_REF = {}
for side in ("Right", "Left"):
    rest_dir = head(side + "ForeArm") - head(side + "Arm")
    ARM_REF[side] = swing(rest_dir, FORWARD) @ rot(side + "Arm")


def two_bone(start, target, a, b, pole, reach=0.97):
    d_vec = target - start
    d = np.linalg.norm(d_vec)
    # Soft limit so the arm never snaps straight.
    limit = (a + b) * reach
    if d > limit * 0.9:
        x = (d - limit * 0.9) / (limit * 0.1)
        d = limit * 0.9 + limit * 0.1 * np.tanh(x)
    d = max(d, abs(a - b) + 1e-3)
    u = normalize(d_vec)
    p = normalize(pole - np.dot(pole, u) * u)
    cos_a = np.clip((a * a + d * d - b * b) / (2 * a * d), -1, 1)
    sin_a = np.sqrt(1 - cos_a * cos_a)
    return start + a * (cos_a * u + sin_a * p), start + d * u


# The sling hand turns to point along the cords (so they leave the fist instead of running back
# through the wrist), within HAND_CONE of the forearm. AIM holds the pouch per recorded frame.
HAND_CONE = np.radians(float(os.environ.get("HAND_CONE", "80")))
AIM = {}


def solve(t, aim=None):
    if aim is False:
        aim = None
    elif aim is None:
        aim = AIM.get(int(round(t * FPS)) % FRAMES)
    c = {ch: sample(ch, t) for ch in CHANNELS}
    Rp = euler(*c["pr"])
    Rc = Rp @ euler(*c["cr"])
    yaw = -(c["pr"][0] + c["cr"][0]) * 0.8
    pitch = -(c["pr"][1] + c["cr"][1]) * 0.6
    roll = -(c["pr"][2] + c["cr"][2]) * 0.6
    Rh = Rc @ euler(yaw, pitch, roll)
    D = {"pelvis": M @ Rp @ M.T, "chest": M @ Rc @ M.T, "head": M @ Rh @ M.T}
    spine_delta = {
        "Hips": D["pelvis"],
        "Spine02": slerp_rot(D["pelvis"], D["chest"], 0.34),
        "Spine01": slerp_rot(D["pelvis"], D["chest"], 0.67),
        "Spine": D["chest"],
        "neck": slerp_rot(D["chest"], D["head"], 0.5),
        "Head": D["head"],
        "LeftShoulder": D["chest"],
        "RightShoulder": D["chest"] @ (M @ rz(c["sh"][0]) @ M.T),
    }
    # The right hand circles a little ahead of the pouch while whirling.
    w = hand_weight(t)
    a = theta(t) + np.pi * 0.5
    hand_circle = np.array([np.cos(a), 0, np.sin(a)]) * 0.06 * w
    wrist_target = {"Right": U(np.array(c["rw"]) + hand_circle), "Left": U(c["lw"])}
    poles = {"Right": U(normalize(c["rp"])), "Left": U(normalize(c["lp"]))}
    world_rot, world_head, ik = {}, {}, {}

    def arm_ik(side):
        shoulder = world_head[side + "Arm"]
        upper = np.linalg.norm(head(side + "ForeArm") - head(side + "Arm"))
        fore = np.linalg.norm(head(side + "Hand") - head(side + "ForeArm"))
        elbow, wrist = two_bone(shoulder, wrist_target[side], upper, fore, poles[side])
        Ru = swing(FORWARD, elbow - shoulder) @ ARM_REF[side]
        carried = Ru @ rot(side + "Arm").T @ (head(side + "Hand") - head(side + "ForeArm"))
        Rf = swing(carried, wrist - elbow) @ Ru @ rot(side + "Arm").T @ rot(side + "ForeArm")
        ik[side + "Arm"] = Ru
        ik[side + "ForeArm"] = Rf
        ik[side + "Hand"] = Rf @ rot(side + "ForeArm").T @ rot(side + "Hand")

    def leg_ik(side, key):
        hip = world_head[side + "UpLeg"]
        foot = np.array(c[key])
        Rfoot = ry(foot[3]) @ rx(-foot[4])
        x_out = 0.15 if side == "Right" else -0.15
        pole = U(Rfoot @ np.array([0, 0, 1.0]) + np.array([x_out, 0, 0]))
        target = head(side + "Foot") + U(foot[:3])
        thigh = np.linalg.norm(head(side + "Leg") - head(side + "UpLeg"))
        shin = np.linalg.norm(head(side + "Foot") - head(side + "Leg"))
        knee, ankle = two_bone(hip, target, thigh, shin, pole, reach=0.995)
        Rt = swing(head(side + "Leg") - head(side + "UpLeg"), knee - hip) @ rot(side + "UpLeg")
        carried = Rt @ rot(side + "UpLeg").T @ (head(side + "Foot") - head(side + "Leg"))
        Rs = swing(carried, ankle - knee) @ Rt @ rot(side + "UpLeg").T @ rot(side + "Leg")
        ik[side + "UpLeg"] = Rt
        ik[side + "Leg"] = Rs
        ik[side + "Foot"] = (M @ Rfoot @ M.T) @ rot(side + "Foot")

    curls = {}
    for side, amount in (("Right", base.CURL_R), ("Left", base.CURL_L)):
        for finger in base.FINGERS:
            for i in (1, 2, 3):
                curls[side + "Hand" + finger + str(i)] = amount * (0.6 if finger == "Thumb" else 1.0)

    for name in ORDER:
        p = PARENT[name]
        if p is None:
            world_head[name] = head(name) + U(c["po"])
        else:
            world_head[name] = world_head[p] + world_rot[p] @ (rot(p).T @ (head(name) - head(p)))
        if name == "RightArm":
            arm_ik("Right")
        elif name == "LeftArm":
            arm_ik("Left")
        elif name == "RightUpLeg":
            leg_ik("Right", "rf")
        elif name == "LeftUpLeg":
            leg_ik("Left", "lf")
        if name in spine_delta:
            world_rot[name] = spine_delta[name] @ rot(name)
        elif name in ik:
            world_rot[name] = ik[name]
            if name == "RightHand" and aim is not None:
                along = world_rot[name] @ rot(name).T @ (head("RightHandMiddle1") - head(name))
                want = normalize(aim - world_head[name])
                fore = normalize(world_head[name] - world_head["RightForeArm"])
                angle = np.arccos(np.clip(np.dot(fore, want), -1, 1))
                if angle > HAND_CONE:
                    axis = normalize(np.cross(fore, want))
                    want = axis_angle(axis, HAND_CONE) @ fore
                world_rot[name] = swing(along, want) @ world_rot[name]
        elif p is None:
            world_rot[name] = rot(name)
        else:
            world_rot[name] = world_rot[p] @ (rot(p).T @ rot(name))
            if name in curls:
                world_rot[name] = world_rot[name] @ axis_angle(base.CURL_AXIS, curls[name] * base.CURL_SIGN)
    return world_rot, world_head


def grip(wr, wh, side):
    return wh[side + "HandMiddle1"]


def pinned(t):
    t %= PERIOD
    if t >= PIN_START:
        return float(smoothstep(PIN_START, PIN_START + 0.25, t))
    if t <= PIN_END:
        return float(1 - smoothstep(PIN_END - 0.08, PIN_END, t))
    return 0.0


def is_loaded(t):
    t %= PERIOD
    return t < RELEASE or t >= LOADED_FROM


def rope(hand, end, n=SLING_N):
    d = end - hand
    L = np.linalg.norm(d)
    slack = np.sqrt(max(0.0, SLING_LEN ** 2 - L ** 2)) * 0.5
    s = np.linspace(0, 1, n + 1)[:, None]
    return hand + d * s + np.array([0, 0, -1.0]) * slack * 4 * s * (1 - s)


def simulate():
    sub = 20
    dt = 1.0 / (FPS * sub)
    wr, wh = solve(0.0)
    hand = grip(wr, wh, "Right")
    stone = hand + np.array([0, 0, -SLING_LEN])
    prev = stone.copy()
    record = []
    cycles = 3
    for frame_index in range(FRAMES * cycles):
        if frame_index >= FRAMES * (cycles - 1):
            AIM[frame_index % FRAMES] = stone.copy()
            wr, wh = solve(frame_index / FPS, stone)
            record.append(rope(grip(wr, wh, "Right"), stone))
        for s in range(sub):
            t = (frame_index * sub + s) * dt
            wr, wh = solve(t, stone)
            hand = grip(wr, wh, "Right")
            vel = (stone - prev) * 0.9985
            prev = stone.copy()
            stone = stone + vel + GRAVITY * dt * dt
            d = stone - hand
            L = np.linalg.norm(d)
            if L > SLING_LEN:
                stone = hand + d / L * SLING_LEN
            w = whirl_weight(t)
            if w > 0:
                stone = stone + (hand + U(whirl_dir(t)) * SLING_LEN - stone) * w
            pin = pinned(t)
            if pin > 0:
                target = grip(wr, wh, "Left") + np.array([0.0, 0.0, -0.04])
                stone = stone + (target - stone) * pin
    return record


def main():
    base.rig.FPS = FPS
    base.rig.FRAMES = FRAMES
    base.rig.PERIOD = PERIOD
    base.rig.RELEASE = RELEASE
    base.rig.is_loaded = is_loaded
    base.solve = solve
    base.simulate = simulate
    base.grip = grip
    base.rope = rope
    base.main()


if __name__ == "__main__":
    main()
