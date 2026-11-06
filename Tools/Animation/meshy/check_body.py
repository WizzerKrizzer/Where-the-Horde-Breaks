"""How close the sling comes to the torso after the release (character space)."""
import json, sys, numpy as np
d = json.load(open(sys.argv[1]))
M = np.array([[-1.0, 0, 0], [0, 0, -1.0], [0, 1.0, 0]])
worst = []
for f, chain in enumerate(d["chains"]):
    t = f / 30
    if t < 1.45:
        continue
    pts = np.array(chain) @ M  # Blender -> character space (M orthogonal: M.T b == b @ M)
    # torso: vertical capsule round x=0, z=0 from 0.7 to 1.3 m, radius 0.24
    inside = [(p, np.hypot(p[0], p[2] - 0.05)) for p in pts if 0.6 < p[1] < 1.35]
    if inside:
        m = min(r for _, r in inside)
        worst.append((m, t))
worst.sort()
print("closest to torso axis (m, t):", [(round(a, 3), round(b, 2)) for a, b in worst[:5]])
