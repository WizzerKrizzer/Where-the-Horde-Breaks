"""Check where slingers can stand on a tower: their feet must rest on the planks, not hang over
an edge or stand on a tusk or a prop.

    python footing.py tower.txt feet-track.txt --seats "237,2.4,2.5 265,2.4,2.5 ..."
    python footing.py tower.txt feet-track.txt --scan 2.5 1.6 2.9

tower.txt is a baked tower (StyleTowerTests.Baked format). feet-track.txt comes from the Unity test
RenderMeshyWarrior: per frame of the throw, heel and toe tip of each foot, facing +Z.
Seats are 'angle,radius,deck' in the tower's frame (270 degrees is the front, -Z), deck being the
rough deck height; the tool reports the height the slinger should stand at.

Under every foot point, for every way the slinger may turn (YAWS) and every frame, the top surface
below must be close to the seat height (the median surface under the feet): see DROP and RISE. The result is the share of bad points.
"""
import argparse

import numpy as np

# Slingers turn to goblins on the road in front of the tower: yaw 180 is straight at -Z.
YAWS = np.radians(np.arange(140, 221, 10))
# A foot is off the deck when the surface under it drops more than DROP (past an edge or into a
# wide gap), or stands on something when it rises more than RISE (a tusk, a prop, the tent).
DROP = 0.3
RISE = 0.2
# Feet reach a little past the bones: the heel behind the ankle, the toe tip past the toe bone.
MARGIN = 0.07


def load_tower(path):
    data = np.loadtxt(path)
    return data[:, :9].reshape(-1, 3, 3)


def foot_points(path):
    track = np.loadtxt(path).reshape(-1, 4, 3)
    points = []
    for heel, toe in ((0, 1), (2, 3)):
        h, t = track[:, heel], track[:, toe]
        along = t - h
        along[:, 1] = 0
        along /= np.linalg.norm(along, axis=1, keepdims=True)
        side = np.stack([along[:, 2], np.zeros(len(along)), -along[:, 0]], 1)
        for p in (h - along * MARGIN, t + along * MARGIN):
            points += [p + side * 0.05, p - side * 0.05]
    points = np.concatenate(points)
    return points[:, [0, 2]]


class Surface:
    """Highest triangle under a point, below a height, with a grid of buckets for speed."""

    def __init__(self, tris, cell=0.2):
        self.tris = tris
        self.cell = cell
        lo = tris[:, :, [0, 2]].min(1)
        hi = tris[:, :, [0, 2]].max(1)
        self.buckets = {}
        for i, (a, b) in enumerate(zip(np.floor(lo / cell).astype(int), np.floor(hi / cell).astype(int))):
            for x in range(a[0], b[0] + 1):
                for z in range(a[1], b[1] + 1):
                    self.buckets.setdefault((x, z), []).append(i)
        self.buckets = {k: np.array(v) for k, v in self.buckets.items()}

    def top(self, x, z, below):
        idx = self.buckets.get((int(np.floor(x / self.cell)), int(np.floor(z / self.cell))))
        if idx is None:
            return -np.inf
        t = self.tris[idx]
        a, b, c = t[:, 0], t[:, 1], t[:, 2]
        v0 = c[:, [0, 2]] - a[:, [0, 2]]
        v1 = b[:, [0, 2]] - a[:, [0, 2]]
        v2 = np.array([x, z]) - a[:, [0, 2]]
        d00 = (v0 * v0).sum(1)
        d01 = (v0 * v1).sum(1)
        d11 = (v1 * v1).sum(1)
        d20 = (v2 * v0).sum(1)
        d21 = (v2 * v1).sum(1)
        den = d00 * d11 - d01 * d01
        ok = np.abs(den) > 1e-12
        u = np.where(ok, (d11 * d20 - d01 * d21) / np.where(ok, den, 1), -1)
        v = np.where(ok, (d00 * d21 - d01 * d20) / np.where(ok, den, 1), -1)
        inside = (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1 + 1e-6)
        y = a[:, 1] + u * (c[:, 1] - a[:, 1]) + v * (b[:, 1] - a[:, 1])
        y = y[inside & (y < below)]
        return y.max() if len(y) else -np.inf


def bad_share(surface, points, angle, radius, deck):
    """Share of bad foot points and the seat height: the median surface under the feet. deck is
    roughly the deck's height, to ignore anything more than 0.9 m above it."""
    seat = np.array([np.cos(np.radians(angle)) * radius, np.sin(np.radians(angle)) * radius])
    tops = []
    for yaw in YAWS:
        # Unity yaw turns +Z towards +X.
        rot = np.array([[np.cos(yaw), np.sin(yaw)], [-np.sin(yaw), np.cos(yaw)]])
        world = seat + points[::3] @ rot
        tops += [surface.top(x, z, deck + 0.9) for x, z in world]
    tops = np.array(tops)
    finite = tops[np.isfinite(tops) & (tops > deck - 0.5)]
    height = float(np.median(finite)) if len(finite) else deck
    return float(np.mean((tops < height - DROP) | (tops > height + RISE))), height


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("tower")
    parser.add_argument("feet")
    parser.add_argument("--seats")
    parser.add_argument("--scan", nargs=3, type=float, metavar=("HEIGHT", "RMIN", "RMAX"))
    args = parser.parse_args()
    surface = Surface(load_tower(args.tower))
    points = foot_points(args.feet)
    if args.seats:
        for seat in args.seats.split():
            angle, radius, deck = map(float, seat.split(","))
            bad, height = bad_share(surface, points, angle, radius, deck)
            print(f"{angle:6.1f} {radius:4.2f}  height {height:4.2f}  bad {bad:5.1%}")
    if args.scan:
        deck, rmin, rmax = args.scan
        radii = np.arange(rmin, rmax + 1e-6, 0.1)
        print("angle " + " ".join(f"{r:4.1f}" for r in radii))
        for angle in range(0, 360, 5):
            row = [bad_share(surface, points, angle, r, deck)[0] for r in radii]
            print(f"{angle:5d} " + " ".join("  ok" if b == 0 else f"{min(99, round(b * 100)):4d}" for b in row))


if __name__ == "__main__":
    main()
