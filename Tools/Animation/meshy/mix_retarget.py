"""Retarget a dumped Mixamo clip (mix_dump.py) onto the Meshy skeleton.

Every mapped Meshy bone takes the Mixamo bone's world rotation change since its rest pose, after
first being swung so its rest direction matches the Mixamo rest direction (T-pose vs A-pose).
Hips translation is scaled by the hip-height ratio.

python mix_retarget.py rest.json clip.json out.json
"""
import sys, os, json
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retarget as base  # noqa: E402
from retarget import REST, PARENT, ORDER, head, rot, axis_angle, normalize  # noqa: E402

clip = json.load(open(sys.argv[2]))
MAP = {
    "Hips": "Hips", "Spine02": "Spine", "Spine01": "Spine1", "Spine": "Spine2", "neck": "Neck", "Head": "Head",
}
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


def swing(a, b):
    a, b = normalize(a), normalize(b)
    axis = np.cross(a, b)
    s = np.linalg.norm(axis)
    if s < 1e-9:
        return np.eye(3)
    return axis_angle(axis, np.arctan2(s, np.dot(a, b)))


MIX_REST = {n: rotation(m) for n, m in clip["rest"].items()}
ALIGN = {}
for mb, xb in MAP.items():
    ALIGN[mb] = swing(rot(mb)[:, 1], MIX_REST[xb][:, 1]) @ rot(mb)

f0 = clip["frames"][0]
mix_hips0 = np.array(f0["mixamorig:Hips"])[:3, 3]
ratio = head("Hips")[2] / mix_hips0[2]


def solve(frame):
    world_rot, world_head = {}, {}
    for name in ORDER:
        p = PARENT[name]
        if p is None:
            pos = np.array(frame["mixamorig:Hips"])[:3, 3]
            world_head[name] = np.array([head(name)[0] + (pos[0] - mix_hips0[0]) * ratio,
                                         head(name)[1] + (pos[1] - mix_hips0[1]) * ratio, pos[2] * ratio])
        else:
            world_head[name] = world_head[p] + world_rot[p] @ (rot(p).T @ (head(name) - head(p)))
        if name in MAP:
            xb = MAP[name]
            world_rot[name] = rotation(frame[xb]) @ MIX_REST[xb].T @ ALIGN[name]
        elif p is None:
            world_rot[name] = rot(name)
        else:
            world_rot[name] = world_rot[p] @ (rot(p).T @ rot(name))
    return world_rot, world_head


frames = []
for frame in clip["frames"]:
    wr, wh = solve(frame)
    frames.append({name: base.basis(name, wr, wh) for name in ORDER})
json.dump(dict(fps=clip["fps"], frames=frames, mapped=sorted(MAP)), open(sys.argv[3], "w"))
print("retargeted", len(frames), "frames,", len(MAP), "bones mapped, hip ratio", round(ratio, 3))
