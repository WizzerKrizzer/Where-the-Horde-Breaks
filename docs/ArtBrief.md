# Art brief: Where the Horde Breaks

Status: starting point. Derived from gameplay only. The art style is reset (2026-11-02);
nothing here refers to any earlier visual direction. Items marked **TBD** are measured
or decided in Phase 4.2-4.4.

## 1. Camera (what the art must survive)

- Top-down, controlled by `TopDownCameraController`; field of view and min/max zoom
  height come from each level (`LevelDefinition`).
- Enemy body scale is about 0.34-0.45 m (`EnemyDefinition.visualScale`, spawn scale 0.34).
- Orthographic, pitch 75 degrees, field of view 55; the zoom height is the camera height
  (Level 5: 20 m minimum drag reference, 150 m start, 360 m maximum).
- Measured at 1080p (`GpuHordeReadabilityTests`, see section 7): an enemy covers about
  `2400 x scale / zoom height` pixels, independent of crowd size. A scale 0.45 runner is
  53 px at 20 m, 18 px at 60 m, 7 px at 150 m and 3 px at 360 m.

## 2. Roles that must read at a glance

Enemies (distinguish by silhouette and size, not colour):
- standard runner (the mass)
- heavy / brute (large, slow)
- fast swarm unit
- flying unit
- rare marker (one in a thousand)

Towers and units:
- projectile tower (archer, ballista)
- area / control tower (bell, catapult)
- barrier (blocks the path)
- barracks (spawns allied units)

Functional signals, kept even in grey: slowed, damaged, selected, burning. Each is a
brightness or shape change, never a hue.

## 3. Eras (framing only)

Prehistoric, medieval, gunpowder, industrial, electric, future. Each era introduces new
decisions (see the vision document). The first production era is prehistoric.

## 4. Budget

- Horde: GPU instancing, one indirect draw call for the horde, 100K bodies.
- Dead bodies: a separate GPU ring-buffer layer (16K), one draw call, no physics.
- No per-enemy textures or per-enemy materials. Variation comes from the instance data
  already on the GPU (scale, tint band, mass).
- **TBD:** producer of the art (you, generated, purchased, procedural). This decides which
  styles are realistic.

## 5. Open requirements for any style

1. At 100K, three to four enemy types stay distinguishable by silhouette alone.
2. Road, terrain and enemy keep contrast in a dense crowd.
3. Towers read above the crowd at max zoom.
4. Death layer stays visibly distinct from living bodies.

## 6. Next steps

1. Greybox readability test: minimum size and contrast per enemy type at 100K.
2. Three style directions applied to one enemy, one tower and one terrain piece
   (prehistoric), compared at 1K and 100K.
3. Decision and style guide written back into this file and the vision document.
4. Production for the first era only after step 3.

## 7. Readability measurement (greybox, 2026-11-02)

Rendered through the real GPU draw path, one shared mesh, enemy base brightness 0.55 on a
0.40 road. Pixel diameter of an isolated enemy:

| zoom height | swarm 0.30 | runner 0.45 | brute 0.70 | heavy 1.00 |
|---|---|---|---|---|
| 20 m | 36 px | 53 px | 83 px | 118 px |
| 60 m | 12 px | 18 px | 28 px | 39 px |
| 150 m | 5 px | 7 px | 10 px | 16 px |
| 360 m | 2 px | 3 px | 5 px | 6 px |

Findings:
- Up to 60 m every class is clear. At the 150 m start height the classes still separate by
  size (ratios 1.4 to 1.6), but the swarm is at the edge at 5 px.
- At the 360 m maximum zoom only the heavy class (6 px) is recognisable; swarm and runner are
  2 to 3 px and merge into texture.
- Brightness was a weak channel at first: the enemy base read at only 1.13:1 against the
  0.40 road. The greybox road is now 0.22, which gives 1.76:1 for a normal enemy and 2.33:1
  for a slowed one (a 1.32 step between the two states). The table above is size only and
  unchanged.
- Crowd count does not change per-enemy size. 1K and 100K screenshots are in
  `Logs/Captures/Readability/crowd-*.png` (regenerate with the test).

Minimum rules for any style:
1. A class needs at least 6 px to be recognised: scale >= 0.0025 x zoom height
   (0.37 at 150 m, 0.9 at 360 m). Smaller classes are only readable when zoomed in.
2. Neighbouring classes need a size ratio of at least 1.4.
3. Enemy and road need a luminance contrast of at least 1.5:1; value steps used as a signal
   (slowed, hit) need at least 1.2:1 between steps.
4. Because the horde is one mesh and one draw call, class differences must come from scale,
   brightness and per-instance data. Per-class silhouettes need extra draw calls or an
   atlas, which the art style decision has to budget for.
