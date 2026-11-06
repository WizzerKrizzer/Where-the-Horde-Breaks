"""Derive bake_character2.py from bake_character.py: flat colour regions instead of noisy
per-triangle colours. Every face gets a label (skin, blue paint, or one of the k-means
clusters), labels are smoothed by area-weighted majority over neighbouring faces, and each label
is painted one flat colour."""
import sys

src, dst = sys.argv[1], sys.argv[2]
s = open(src, encoding="utf-8").read()


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:60]
    s = s.replace(old, new)


# Face adjacency of the decimated mesh (loop triangles share mesh edges).
rep('''mesh = low.data
mesh.calc_loop_triangles()
''', '''mesh = low.data
mesh.calc_loop_triangles()
edge_faces = {}
for ti, tri in enumerate(mesh.loop_triangles):
    vs = tri.vertices
    for a, b in ((vs[0], vs[1]), (vs[1], vs[2]), (vs[2], vs[0])):
        edge_faces.setdefault((min(a, b), max(a, b)), []).append(ti)
neighbours = [[] for _ in mesh.loop_triangles]
for faces_on_edge in edge_faces.values():
    for a in faces_on_edge:
        for b in faces_on_edge:
            if a != b:
                neighbours[a].append(b)
areas = np.array([tri.area for tri in mesh.loop_triangles])
''')

# Replace the flattening block with region labels.
start = s.index("SKIN = np.array([0.8, 0.58, 0.42])")
end = s.index("for i, (corners, part, skin, color, normal, is_blue) in enumerate(debug_tris):")
s = s[:start] + '''SKIN = np.array([0.8, 0.58, 0.42])
colors = np.array([d[3] for d in debug_tris])
skin_mask = np.array([d[2] > 0.5 for d in debug_tris])
blue_mask = np.array([d[5] for d in debug_tris])
rest = ~skin_mask & ~blue_mask
data = colors[rest]
rng = np.random.default_rng(1)
K = 7
centers = data[rng.choice(len(data), K, replace=False)]
for _ in range(30):
    label = ((data[:, None, :] - centers[None]) ** 2).sum(-1).argmin(1)
    centers = np.array([data[label == k].mean(0) if (label == k).any() else centers[k] for k in range(K)])
labels = np.zeros(len(colors), int)
labels[skin_mask] = K
labels[blue_mask] = K + 1
labels[rest] = label
# Area-weighted majority over neighbours removes speckles and straightens region borders.
for _ in range(6):
    new = labels.copy()
    for i, nb in enumerate(neighbours):
        if not nb:
            continue
        votes = {}
        votes[labels[i]] = areas[i] * 1.5
        for j in nb:
            votes[labels[j]] = votes.get(labels[j], 0.0) + areas[j]
        new[i] = max(votes, key=votes.get)
    labels = new
palette = list((0.05 + centers * 1.08) * np.array([1.0, 0.98, 0.9])) + [SKIN, BLUE]
colors = np.clip(np.array([palette[l] for l in labels]), 0, 1)
print("PALETTE", np.round(np.array(palette), 2).tolist())
debug_tris = [(d[0], d[1], 1.0 if labels[i] == K else 0.0, d[3], d[4], labels[i] == K + 1) for i, d in enumerate(debug_tris)]

''' + s[end:]
open(dst, "w", encoding="utf-8").write(s)
print("written", dst)
