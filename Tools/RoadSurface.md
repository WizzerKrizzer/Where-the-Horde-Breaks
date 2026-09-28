# Continuous weathered road

The normal Unity map bootstrap now creates `RoadSurface_Continuous` for all
non-circular route maps. No separate preview executable is required. Circular
arena floors are intentionally unchanged.

## Art direction

- Muted charcoal asphalt, restrained aggregate, sparse hairline repairs.
- Faded warm road paint; dusty gravel shoulder and subdued olive terrain.
- Low bevelled concrete kerb only on the exterior union contour. No old
  per-route boundary lines, floating road slabs or cyan navigation overlay.
- Existing route points, widths, collision and enemy movement are unchanged.

## Implementation

`Assets/Scripts/Runtime/RoadSurface.cs` creates one linear RGBA32 footprint
texture (12 samples/metre, maximum 2048 per dimension, mipmapped). Pure CPU
sampling is parallelized during level creation; meshes/materials/uploads remain
on the main thread. There is no per-frame road work.

The analytic signed-distance union mirrors `HordeFlowField`'s variable-radius
segments, square joint clipping and orthogonal corner fills. If those simulation
rules change, update this representation and rerun the alignment test too.

The only new renderers are the ground/road surface and one batched exterior kerb.
Both disable shadow casting. Runtime-owned textures, meshes and materials are
destroyed when their map is unloaded. The shader is in Resources for player
build inclusion: `Assets/Resources/RoadSurface.shader`.

`HordeEnemyManager.showNavigationBoundary` is an opt-in diagnostic Inspector
setting, off by default. It takes effect when the next wave starts and does not
change navigation.

## Validation (2026-09-14)

PlayMode tests: RoadSurfaceTests, GoblinCrowdSceneTests,
RedGoblinQueue_TouchesBodiesAndSettlesWithoutRunningInPlace: 3 passed.

- Level 2: 17,764 footprint samples agree with navigation; road build 0.587s.
- Level 6: 21,916 samples agree, including square corners; road build 0.311s.
- Actual 10,000-goblin wave rendered at wide, close and junction cameras.
- Batch Editor wide-view frame interval: 5.94ms (previous crowd-only run 5.80ms).
  This is not a standalone GPU frame-time benchmark or a guaranteed player FPS.

Screenshots: `Logs/RoadReview/level-2-view-0.png` (overview),
`level-2-view-1.png` (junction), `level-2-view-2.png` (surface detail),
and `Logs/CrowdFix/crowd-exit.png` (crowded junction).
Results: `Logs/RoadReview/final-results.xml`.

Existing EXE builds have not been rebuilt by this change; use Play in Unity or
make a fresh normal player build to see the new road.
