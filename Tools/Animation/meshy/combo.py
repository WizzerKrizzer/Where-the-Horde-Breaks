"""Sling throw = Mixamo "Throw" body (time-warped to the 2.6 s cycle) + our overhead whirl on the
right arm + our reload on the left arm, with the simulated sling from throw2.py.

Body bones play the Mixamo clip retargeted to the Meshy skeleton; the arm chains blend in world
space between Mixamo and the keyed throw2 arms (right arm ours while lifting and whirling, Mixamo
for the release and follow-through; left arm ours only while loading). The right fingers keep the
fist from throw2. The wrist turns along the cords as in throw2.

Usage: SPIN_DOWN=0.55 python combo.py rest.json mix/throw.json out.json
"""
import sys, os, json
import numpy as np
from scipy.interpolate import PchipInterpolator
from scipy.spatial.transform import Rotation, Slerp

mix_path = sys.argv[2]
out_path = sys.argv[3]
sys.argv = [sys.argv[0], sys.argv[1], out_path]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import throw2 as T  # noqa: E402
from retarget import REST, PARENT, ORDER, head, rot, axis_angle, normalize  # noqa: E402

clip = json.load(open(mix_path))
MIX_FPS = clip["fps"]
MIX_LEN = (len(clip["frames"]) - 1) / MIX_FPS

# ---------------------------------------------------------------- Mixamo retarget (as mix_retarget.py)
MAP = {"Hips": "Hips", "Spine02": "Spine", "Spine01": "Spine1", "Spine": "Spine2", "neck": "Neck", "Head": "Head"}
for side in ("Left", "Right"):
    for b in ("Shoulder", "Arm", "ForeArm", "Hand", "UpLeg", "Leg", "Foot", "ToeBase"):
        MAP[side + b] = side + b
    for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
        for i in (1, 2, 3):
            MAP[f"{side}Hand{finger}{i}"] = f"{side}Hand{finger}{i}"
MAP = {k: "mixamorig:" + v for k, v in MAP.items() if k in REST and ("mixamorig:" + v) in clip["rest"]}


def rotation(m):
    r = np.array(m)[:3, :3]
    return r / np.linalg.norm(r, axis=0)


MIX_REST = {n: rotation(m) for n, m in clip["rest"].items()}
ALIGN = {mb: T.swing(rot(mb)[:, 1], MIX_REST[xb][:, 1]) @ rot(mb) for mb, xb in MAP.items()}
mix_hips0 = np.array(clip["frames"][0]["mixamorig:Hips"])[:3, 3]
ratio = head("Hips")[2] / mix_hips0[2]

# Per Mixamo frame: Meshy world rotation of every bone (unmapped ones follow their parent), hips.
B_rot = {name: [] for name in ORDER}
B_hips = []
for frame in clip["frames"]:
    wr = {}
    for name in ORDER:
        p = PARENT[name]
        if name in MAP:
            wr[name] = rotation(frame[MAP[name]]) @ MIX_REST[MAP[name]].T @ ALIGN[name]
        elif p is None:
            wr[name] = rot(name)
        else:
            wr[name] = wr[p] @ (rot(p).T @ rot(name))
        B_rot[name].append(wr[name])
    pos = np.array(frame["mixamorig:Hips"])[:3, 3]
    B_hips.append([head("Hips")[0] + (pos[0] - mix_hips0[0]) * ratio, head("Hips")[1] + (pos[1] - mix_hips0[1]) * ratio, pos[2] * ratio])
B_hips = np.array(B_hips)
mix_times = np.arange(len(clip["frames"])) / MIX_FPS
B_slerp = {name: Slerp(mix_times, Rotation.from_matrix(np.array(B_rot[name]))) for name in ORDER}

# ---------------------------------------------------------------- timing
# Our cycle time -> Mixamo time. The Mixamo wind-up is stretched over our lift and whirl, its
# throw lands on our release, and the clip (which loops) ends with the cycle.
MIX_RELEASE = 0.80
WARP = PchipInterpolator([0.0, 0.30, 1.28, T.RELEASE, 2.10, T.PERIOD],
                         [0.0, 0.12, MIX_RELEASE - 0.18, MIX_RELEASE, 1.55, MIX_LEN])


def mix_time(t):
    return float(np.clip(WARP(t % T.PERIOD), 0, MIX_LEN))


def right_weight(t):
    t %= T.PERIOD
    if t <= 1.28:
        return 1.0
    if t <= T.RELEASE:
        return float(1 - T.smoothstep(1.28, T.RELEASE, t))
    return float(T.smoothstep(1.95, 2.25, t))


def left_weight(t):
    t %= T.PERIOD
    if t >= 1.9:
        return float(T.smoothstep(1.95, 2.15, t))
    return float(1 - T.smoothstep(0.15, 0.40, t))


RIGHT_CHAIN = ("RightShoulder", "RightArm", "RightForeArm", "RightHand")
LEFT_CHAIN = ("LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand")
RIGHT_FINGERS = {n for n in ORDER if n.startswith("RightHand") and n != "RightHand"} | {n for n in ORDER if n.startswith("mixamorig:RightHand")}


def blend(a, b, w):
    if w <= 0:
        return a
    if w >= 1:
        return b
    return Slerp([0, 1], Rotation.from_matrix(np.stack([a, b])))([w]).as_matrix()[0]


ours_solve = T.solve


def solve(t, aim=None):
    if aim is None:
        aim = T.AIM.get(int(round(t * T.FPS)) % T.FRAMES)
    A_rot, _ = ours_solve(t, False)
    m = mix_time(t)
    B = {name: B_slerp[name]([m]).as_matrix()[0] for name in ORDER}
    hips = np.array([np.interp(m, mix_times, B_hips[:, k]) for k in range(3)])
    wR, wL = right_weight(t), left_weight(t)
    world_rot, world_head = {}, {}
    for name in ORDER:
        p = PARENT[name]
        world_head[name] = hips if p is None else world_head[p] + world_rot[p] @ (rot(p).T @ (head(name) - head(p)))
        if name in RIGHT_CHAIN:
            world_rot[name] = blend(B[name], A_rot[name], wR)
        elif name in LEFT_CHAIN:
            world_rot[name] = blend(B[name], A_rot[name], wL)
        elif p is None:
            world_rot[name] = B[name]
        elif name in RIGHT_FINGERS:
            world_rot[name] = world_rot[p] @ (A_rot[p].T @ A_rot[name])
        else:
            world_rot[name] = world_rot[p] @ (B[p].T @ B[name])
        if name == "RightHand" and aim is not None:
            along = world_rot[name] @ rot(name).T @ (head("RightHandMiddle1") - head(name))
            want = normalize(aim - world_head[name])
            fore = normalize(world_head[name] - world_head["RightForeArm"])
            angle = np.arccos(np.clip(np.dot(fore, want), -1, 1))
            if angle > T.HAND_CONE:
                want = axis_angle(normalize(np.cross(fore, want)), T.HAND_CONE) @ fore
            world_rot[name] = T.swing(along, want) @ world_rot[name]
    return world_rot, world_head


T.solve = solve

if __name__ == "__main__":
    T.main()
