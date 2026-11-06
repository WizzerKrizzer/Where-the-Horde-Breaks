"""python export.py baked.txt rig_out.txt vat_out.bytes"""
import sys
import numpy as np
import rig

data = rig.build(sys.argv[1])
frames = data["frames"].astype(np.float32)          # (F, N, 3)
F, N, _ = frames.shape
nb = data["body_count"]
faces = np.concatenate([np.array(data["faces"]), np.array(data["sling_faces"]) + nb])
colors = np.concatenate([np.array(data["colors"]), np.array(data["sling_colors"])])
skin = np.concatenate([np.array(data["skin"]), np.zeros(len(data["sling_faces"]))])
stone = data["release_stone"]
with open(sys.argv[2], "w") as f:
    f.write("vat %d %d %g %g %g %.4f %.4f %.4f\n" % (N, F, rig.FPS, rig.PERIOD, rig.RELEASE, *stone))
    for (a, b, c), col, s in zip(faces, colors, skin):
        f.write("%d %d %d %.4f %.4f %.4f %d\n" % (a, b, c, col[0], col[1], col[2], int(s > 0.5)))
texels = np.zeros((F, N, 4), np.float16)
texels[:, :, :3] = frames
texels.tofile(sys.argv[3])
print("vertices", N, "frames", F, "triangles", len(faces), "release stone", np.round(stone, 3), "bytes", texels.nbytes)
