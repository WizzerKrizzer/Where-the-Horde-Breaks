"""Retarget the approved slinger throw (rig.py, Unity space) onto the Meshy skeleton (Blender
armature space) and re-simulate the sling with the Meshy hands. Writes per-frame pose-bone
basis transforms (loc, quat wxyz, scale) for every bone plus the extra sling bones.

Usage: python retarget.py rest.json out.json
"""
import sys, os, json
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "rig"))
import rig  # noqa: E402
from rig import normalize, frame, two_bone, smoothstep  # noqa: E402

rest = json.load(open(sys.argv[1]))
BONES = {b["name"]: b for b in rest["bones"]}
ORDER = [b["name"] for b in rest["bones"]]  # Blender lists parents before children
REST = {n: np.array(b["matrix"]) for n, b in BONES.items()}
PARENT = {n: b["parent"] for n, b in BONES.items()}

# Unity (x right, y up, z forward) -> Blender (character's right at -X, faces -Y, Z up).
M = np.array([[-1.0, 0, 0], [0, 0, -1.0], [0, 1.0, 0]])
S = REST["RightArm"][2, 3] / rig.J["shoulderR"][1]  # shoulder heights


def U(v):
    return M @ np.asarray(v, float)


def head(name):
    return REST[name][:3, 3].copy()


def rot(name):
    return REST[name][:3, :3].copy()


def slerp_rot(a, b, t):
    """Interpolate two rotation matrices."""
    from scipy.spatial.transform import Rotation, Slerp
    r = Rotation.from_matrix(np.stack([a, b]))
    return Slerp([0, 1], r)([t]).as_matrix()[0]


def axis_angle(axis, angle):
    axis = normalize(np.asarray(axis, float))
    K = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.eye(3) + np.sin(angle) * K + (1 - np.cos(angle)) * K @ K


ARM_LEN = {s: np.linalg.norm(head(s + "ForeArm") - head(s + "Arm")) + np.linalg.norm(head(s + "Hand") - head(s + "ForeArm")) for s in ("Right", "Left")}
LEG_LEN = {s: np.linalg.norm(head(s + "Leg") - head(s + "UpLeg")) + np.linalg.norm(head(s + "Foot") - head(s + "Leg")) for s in ("Right", "Left")}
MY_ARM = rig.UPPER + rig.FORE
MY_LEG = rig.THIGH + rig.SHIN

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
# Finger curl (radians per joint) for the grip: the right hand holds the sling cords in a fist.
CURL_R = 1.05
CURL_L = 0.45
CURL_AXIS = np.array([1.0, 0, 0])  # bone-local flexion axis (checked in the preview)
CURL_SIGN = float(os.environ.get("CURL_SIGN", "1"))


def solve(t):
    """World (armature-space) 3x3 rotations and heads for every Meshy bone at time t."""
    bones, pts = rig.pose(t)
    c = {name: rig.sample(name, t) for name in rig.CHANNELS}
    D = {n: M @ bones[n][0] @ M.T for n in ("pelvis", "chest", "head")}
    spine_delta = {
        "Hips": D["pelvis"],
        "Spine02": slerp_rot(D["pelvis"], D["chest"], 0.34),
        "Spine01": slerp_rot(D["pelvis"], D["chest"], 0.67),
        "Spine": D["chest"],
        "neck": slerp_rot(D["chest"], D["head"], 0.5),
        "Head": D["head"],
        "LeftShoulder": D["chest"],
        "RightShoulder": D["chest"],
    }
    world_rot, world_head, ik = {}, {}, {}

    def arm_ik(side, key, pkey, my):
        shoulder = world_head[side + "Arm"]
        target = head(side + "Arm") + U(np.array(c[key]) - rig.J["shoulder" + my]) * (ARM_LEN[side] / MY_ARM)
        pole = U(normalize(np.array(c[pkey])))
        upper = np.linalg.norm(head(side + "ForeArm") - head(side + "Arm"))
        fore = np.linalg.norm(head(side + "Hand") - head(side + "ForeArm"))
        elbow, wrist = two_bone(shoulder, target, upper, fore, pole)
        rest_pole = U(rig.ARM_POLE_REST[my])
        du = frame(shoulder, elbow, pole) @ frame(head(side + "Arm"), head(side + "ForeArm"), rest_pole).T
        df = frame(elbow, wrist, pole) @ frame(head(side + "ForeArm"), head(side + "Hand"), rest_pole).T
        ik[side + "Arm"] = du @ rot(side + "Arm")
        ik[side + "ForeArm"] = df @ rot(side + "ForeArm")
        ik[side + "Hand"] = df @ rot(side + "Hand")

    def leg_ik(side, key, my):
        hip = world_head[side + "UpLeg"]
        foot = np.array(c[key])
        Rfoot = rig.ry(foot[3]) @ rig.rx(-foot[4])
        pole = U(Rfoot @ rig.LEG_POLE_REST + np.array([0.15 if my == "R" else -0.15, 0, 0]))
        target = head(side + "Foot") + U(foot[:3] - rig.J["ankle" + my]) * (LEG_LEN[side] / MY_LEG)
        thigh = np.linalg.norm(head(side + "Leg") - head(side + "UpLeg"))
        shin = np.linalg.norm(head(side + "Foot") - head(side + "Leg"))
        knee, ankle = two_bone(hip, target, thigh, shin, pole)
        rest_pole = U(rig.LEG_POLE_REST)
        ik[side + "UpLeg"] = frame(hip, knee, pole) @ frame(head(side + "UpLeg"), head(side + "Leg"), rest_pole).T @ rot(side + "UpLeg")
        ik[side + "Leg"] = frame(knee, ankle, pole) @ frame(head(side + "Leg"), head(side + "Foot"), rest_pole).T @ rot(side + "Leg")
        ik[side + "Foot"] = (M @ Rfoot @ M.T) @ rot(side + "Foot")

    curls = {}
    for side, amount in (("Right", CURL_R), ("Left", CURL_L)):
        for finger in FINGERS:
            for i in (1, 2, 3):
                curls[side + "Hand" + finger + str(i)] = amount * (0.6 if finger == "Thumb" else 1.0)

    for name in ORDER:
        p = PARENT[name]
        if p is None:
            world_head[name] = head(name) + U(c["po"]) * S
        else:
            world_head[name] = world_head[p] + world_rot[p] @ (rot(p).T @ (head(name) - head(p)))
        if name == "RightArm":
            arm_ik("Right", "rh", "rp", "R")
        elif name == "LeftArm":
            arm_ik("Left", "lh", "lp", "L")
        elif name == "RightUpLeg":
            leg_ik("Right", "rf", "R")
        elif name == "LeftUpLeg":
            leg_ik("Left", "lf", "L")
        if name in spine_delta:
            world_rot[name] = spine_delta[name] @ rot(name)
        elif name in ik:
            world_rot[name] = ik[name]
        elif p is None:
            world_rot[name] = rot(name)
        else:
            world_rot[name] = world_rot[p] @ (rot(p).T @ rot(name))
            if name in curls:
                world_rot[name] = world_rot[name] @ axis_angle(CURL_AXIS, curls[name] * CURL_SIGN)
    return world_rot, world_head


def grip(world_rot, world_head, side):
    return 0.5 * (world_head[side + "HandMiddle1"] + world_head[side + "Hand"]) + world_rot[side + "Hand"] @ (rot(side + "Hand").T @ (head(side + "HandMiddle1") - head(side + "Hand"))) * 0.5


SLING_LEN = rig.SLING_LEN * S
GRAVITY = np.array([0, 0, -5.0 * S])


def simulate():
    sub = 20
    dt = 1.0 / (rig.FPS * sub)
    wr, wh = solve(0.0)
    hand = grip(wr, wh, "Right")
    stone = hand + np.array([0, 0, -SLING_LEN])
    prev = stone.copy()
    record = []
    cycles = 3
    for frame_index in range(rig.FRAMES * cycles):
        if frame_index >= rig.FRAMES * (cycles - 1):
            wr, wh = solve(frame_index / rig.FPS)
            record.append(rope(grip(wr, wh, "Right"), stone))
        for s in range(sub):
            t = (frame_index * sub + s) * dt
            wr, wh = solve(t)
            hand = grip(wr, wh, "Right")
            vel = (stone - prev) * 0.9985
            prev = stone.copy()
            stone = stone + vel + GRAVITY * dt * dt
            d = stone - hand
            L = np.linalg.norm(d)
            if L > SLING_LEN:
                stone = hand + d / L * SLING_LEN
            direction, w = rig.guide(t)
            if w > 0:
                stone = stone + (hand + U(direction) * SLING_LEN - stone) * w
            pin = rig.pinned(t)
            if pin > 0:
                target = grip(wr, wh, "Left") + np.array([0.0, 0.0, -0.04])
                stone = stone + (target - stone) * pin
    return record


def rope(hand, end, n=rig.SLING_N):
    d = end - hand
    L = np.linalg.norm(d)
    slack = np.sqrt(max(0.0, SLING_LEN ** 2 - L ** 2)) * 0.5
    s = np.linspace(0, 1, n + 1)[:, None]
    return hand + d * s + np.array([0, 0, -1.0]) * slack * 4 * s * (1 - s)


def quat(m):
    from scipy.spatial.transform import Rotation
    x, y, z, w = Rotation.from_matrix(m).as_quat()
    return [w, x, y, z]


def basis(name, wr, wh, sling_rest=None):
    """Pose-bone basis (loc, quat, scale) from armature-space rotation and head."""
    world = np.eye(4)
    world[:3, :3] = wr[name]
    world[:3, 3] = wh[name]
    restm = REST[name] if sling_rest is None else sling_rest[name]
    p = PARENT.get(name)
    if p is None:
        b = np.linalg.inv(restm) @ world
    else:
        pw = np.eye(4)
        pw[:3, :3] = wr[p]
        pw[:3, 3] = wh[p]
        b = np.linalg.inv(np.linalg.inv(REST[p]) @ restm) @ np.linalg.inv(pw) @ world
    scale = np.linalg.norm(b[:3, :3], axis=0)
    return b[:3, 3].tolist(), quat(b[:3, :3] / scale), scale.tolist()


def sling_rest_frames():
    """Rest pose of the sling bones: hanging straight down from the rest right grip."""
    wr, wh = {n: rot(n) for n in BONES}, {n: head(n) for n in BONES}
    g = grip(wr, wh, "Right")
    pts = [g + np.array([0, 0, -SLING_LEN * i / rig.SLING_N]) for i in range(rig.SLING_N + 1)]
    return pts


def aim_matrix(a, b, up=np.array([0.0, 1.0, 0.0])):
    y = normalize(b - a)
    x = normalize(np.cross(y, up)) if abs(np.dot(y, up)) < 0.99 else normalize(np.cross(y, [1.0, 0, 0]))
    z = np.cross(x, y)
    return np.stack([x, y, z], 1)


def main():
    chains = simulate()
    rest_pts = sling_rest_frames()
    n = rig.SLING_N
    sling_rest = {}
    sling_bones = []
    for i in range(n):
        m = np.eye(4)
        m[:3, :3] = aim_matrix(rest_pts[i], rest_pts[i + 1])
        m[:3, 3] = rest_pts[i]
        sling_rest[f"Sling{i}"] = m
        sling_bones.append(dict(name=f"Sling{i}", head=rest_pts[i].tolist(), tail=rest_pts[i + 1].tolist(), matrix=m.tolist()))
    for name in ("SlingPouch", "SlingStone"):
        m = np.eye(4)
        m[:3, :3] = aim_matrix(rest_pts[n - 1], rest_pts[n])
        m[:3, 3] = rest_pts[n]
        sling_rest[name] = m
        tail = rest_pts[n] + m[:3, 1] * 0.05
        sling_bones.append(dict(name=name, head=rest_pts[n].tolist(), tail=tail.tolist(), matrix=m.tolist()))

    def from_world(restm, parent, wr, wh, world):
        pw = np.eye(4)
        pw[:3, :3] = wr[parent]
        pw[:3, 3] = wh[parent]
        b = np.linalg.inv(np.linalg.inv(REST[parent]) @ restm) @ np.linalg.inv(pw) @ world
        scale = np.linalg.norm(b[:3, :3], axis=0)
        return b[:3, 3].tolist(), quat(b[:3, :3] / np.maximum(scale, 1e-6)), scale.tolist()

    seg_rest = SLING_LEN / n
    frames = []
    for f in range(rig.FRAMES):
        t = f / rig.FPS
        wr, wh = solve(t)
        keys = {name: basis(name, wr, wh) for name in ORDER}
        chain = chains[f]
        for i in range(n):
            world = np.eye(4)
            m = aim_matrix(chain[i], chain[i + 1])
            m[:, 1] *= np.linalg.norm(chain[i + 1] - chain[i]) / seg_rest
            world[:3, :3] = m
            world[:3, 3] = chain[i]
            keys[f"Sling{i}"] = from_world(sling_rest[f"Sling{i}"], "RightHand", wr, wh, world)
        for name in ("SlingPouch", "SlingStone"):
            world = np.eye(4)
            s = 1.0 if (name == "SlingPouch" or rig.is_loaded(t)) else 0.001
            world[:3, :3] = aim_matrix(chain[n - 1], chain[n]) * s
            world[:3, 3] = chain[n]
            keys[name] = from_world(sling_rest[name], "RightHand", wr, wh, world)
        frames.append(keys)
    # Stone position at release (armature space), for checking.
    f0 = int(np.floor(rig.RELEASE * rig.FPS))
    out = dict(fps=rig.FPS, period=rig.PERIOD, release=rig.RELEASE, frames=frames, sling_bones=sling_bones,
               sling_len=SLING_LEN, release_stone=chains[f0][-1].tolist(), scale=S,
               chains=[c.tolist() for c in chains])
    json.dump(out, open(sys.argv[2], "w"))
    print("scale", S, "sling", SLING_LEN, "release stone", np.round(chains[f0][-1], 3))


if __name__ == "__main__":
    main()
