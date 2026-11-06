"""Frames where the sling cords pass through the right forearm (before and after a change)."""
import sys, os, numpy as np
sys.argv = ["x", "rest.json"]
sys.path.insert(0, ".")
import throw2 as T


def seg_dist(p, a, b):
    ab = b - a
    t = np.clip(np.dot(p - a, ab) / np.dot(ab, ab), 0, 1)
    return np.linalg.norm(p - (a + t * ab))


chains = T.simulate()
for use_aim in (False, True):
    hits = []
    for f, chain in enumerate(chains):
        wr, wh = T.solve(f / T.FPS, chain[-1] if use_aim else None) if use_aim else T.solve.__wrapped__(f / T.FPS) if hasattr(T.solve, "__wrapped__") else None, None
        break
    break
# Compare: hand turned (AIM) vs not turned (aim forced to a point straight down the forearm).
for label in ("turned", "not turned"):
    hits = []
    for f, chain in enumerate(chains):
        t = f / T.FPS
        if label == "turned":
            wr, wh = T.solve(t, chain[-1])
        else:
            T.AIM.clear()
            wr, wh = T.solve(t, None)
        g = T.grip(wr, wh, "Right")
        cords = T.rope(g, chain[-1])
        elbow, wrist = wh["RightForeArm"], wh["RightHand"]
        fine = np.array([g + (chain[-1] - g) * k for k in np.linspace(0.02, 0.6, 30)])
        through = min(seg_dist(p, elbow, wrist) for p in fine) < 0.075
        along = wh["RightHandMiddle1"] - wrist
        cord = cords[1] - cords[0]
        back = np.degrees(np.arccos(np.clip(np.dot(along, cord) / np.linalg.norm(along) / np.linalg.norm(cord), -1, 1)))
        if through or back > 75:
            hits.append((round(t, 2), "arm" if through else "", int(back)))
    print(label, len(hits), "frames with cords inside the forearm:", hits[:12])
