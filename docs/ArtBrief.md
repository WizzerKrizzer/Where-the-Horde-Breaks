# Art brief: Where the Horde Breaks

Status: starting point. Derived from gameplay only. The art style is reset (2026-11-02);
nothing here refers to any earlier visual direction. Items marked **TBD** are measured
or decided in Phase 4.2-4.4.

## 1. Camera (what the art must survive)

- Top-down, controlled by `TopDownCameraController`; field of view and min/max zoom
  height come from each level (`LevelDefinition`).
- Enemy body scale is about 0.34-0.45 m (`EnemyDefinition.visualScale`, spawn scale 0.34).
- **TBD:** pixels per enemy at 1K and at 100K on screen at 1080p, at min and max zoom.
  Measured in the greybox readability test (section 4).

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
