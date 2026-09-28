# Goblin Runner visual test

Run `Builds/GoblinPreview/GoblinPreview.exe`. Space starts the wave; R restarts;
the usual camera controls and mouse wheel still work. The test uses the actual
Main scene, route, camera controller and GPU horde simulation, with a maximum of
150 enemies per level. It has a separate product name/save directory and does
not overwrite the ordinary Development/Release builds.

The normal game's visuals are unchanged by default. In the Editor, use
`Tools > Goblin Preview > Enable for next wave`, then restart Play Mode. The
adjacent menu restores the original visuals. The animated crowd LOD is also used
in the ordinary Editor game; it has been exercised with the 10,000-enemy wave.

## What is implemented

- One consistent Goblin Runner model: the red-tunic goblin is used for every
  runner so colour remains a reliable gameplay cue for future enemy types.
- Locomotion uses the measured compact `Running` clip with independent phase offsets.
  The other three source run clips remain available in the bake but exceed the protected envelope.
- Two melee clips (`Charged_Slash` and `Charged_Upward_Slash`) selected per
  agent while it owns a combat slot. Their cycle is aligned to attack timing.
- The red model uses its native Meshy animation exports.
- Red: 11,218 vertices / 15,175 triangles with its embedded base-colour texture.
- GPU vertex animation, per-agent phase offset, facing movement direction.
- Runner size variation is limited to +/-4%. The temporary brute proxy is only
  about 8% larger than a runner until it receives its own model.
- Goblin locomotion contact radius is 0.96 times the visual scale (full measured gait envelope),
  rather than the radius of the previous sphere. Spawn and road clearance use
  the same footprint. Outside-road cells no longer count as imaginary crowds
  that push bodies away from the edge. Locomotion speed is 80% of the authored wave speed.
- Failure to load the optional asset falls back to the original geometry.

The distant tier uses a simplified version of the red goblin: 1,774 vertices /
1,799 triangles versus 11,218 / 15,175 close up. It keeps the original UVs and
source vertex IDs. Ordinary distant movement uses a deliberately stepped,
two-pose run at five updates per second. Bodies continue to follow the fully
smooth resolved simulation position, but the low pose cadence reads as a simple
pixel-art-like crowd rather than independently flickering limbs. Melee bodies
still use their authored attack VAT. LOD selection uses projected size (96 pixels
of character height), including camera altitude and orthographic zoom. There is
no sphere/blob replacement for the goblin.
Lighting uses baked vertex normals with a warm directional key, cool ambient
fill and subtle rough highlights, not the full Meshy PBR material. The preview
player and crowd renderer disable mesh-cast shadows. The model is lifted to the road's surface
height. The original non-goblin renderer still has shadows disabled.
Run cadence follows resolved travel, including lateral pushes. A smoothed speed
blends nearly stationary agents into a compact planted recovery pose, with procedural upper-body breathing and weight shifting. Its
simulation clock continues while blocked and pauses with the game. Facing
persists when stopped. GPU rendering interpolates positions between simulation
ticks; contact projection is kept separate from drive velocity to avoid feeding
positional corrections back into stop/start waves. Locomotion intent remains
ready behind ordinary crowd contacts, so a newly available gap is used on the
first simulation tick. Free aligned queues predict their leading neighbour's
current-tick acceleration, so large packs keep advancing instead of propagating
speed one row per frame. Free goblins fan sideways and retain wider personal
space; a constraint projection before movement allows feasible sideways/forward slides while preserving the full body gap.
Barrier-linked queues use resolved forward travel and suppress soft sideways
pressure while held. They retain their strict limit while the
barrier is alive and release together as soon as its GPU slot becomes inactive.
The visual transition into running is immediate, while settling into the brace
remains eased. Run loops interpolate across
their seam. There is no authored idle/death clip yet. The two charged slashes are unarmed motions,
so their final suitability still depends on the intended melee silhouette.

## Reproduce

Native clip:

`python Tools/BakeGoblinPreview.py <clip_withSkin.glb> <variant-output> --clip-name Running`

Retargeted clip:

`python Tools/BakeGoblinPreview.py <target-model_withSkin.glb> <variant-output> --animation-source <clip_withSkin.glb> --clip-name Running`

Requires numpy. The GLB is read-only. The converter reflects X and reverses
winding for Unity, flips UV V, skins the source animation, and normalizes the
whole cycle to 2.4 local units tall before the existing per-enemy scale.

To rebuild the LOD, use meshoptimizer 1.2.0's `meshopt_simplifier.js` module:
`node Tools/BakeGoblinLod.mjs <path-to-meshopt_simplifier.mjs> Assets/Resources/GoblinPreview/Red`.
The offline simplifier is attribute-aware (normals/UVs), and never changes the
full-detail mesh or the existing animation atlas. See the
[meshoptimizer documentation](https://github.com/zeux/meshoptimizer/blob/master/js/README.md#simplifier).

`Tools > Goblin Preview > Validate crowd fixes` runs focused tests in the editor.
`GoblinCrowdSceneTests` additionally exercises 10,000 enemies on the actual
three-lane map and captures near/far views to `Logs/CrowdFix`. Its frame times
are editor/test-runner measurements, not a standalone-player FPS guarantee.

Build using `Tools > Goblin Preview > Build separate playable test`. The build
symbol `GOBLIN_VISUAL_PREVIEW` enables the preview and count cap only in that
player. The PlayMode test `GoblinPreviewPlayModeTests` exercises the real GPU
backend and saves render checks under `Logs/GoblinPreview`.

The camera can now approach to 2.5 meters on all maps. Wheel steps become smaller
at close range, while the existing drag sensitivity at normal height is retained.
`CrowdSpawningPerformanceTests.CollisionLab_LiveSpawningAndSettledFrames` is an
explicit 10K benchmark that renders each measured frame and reports mean/p95/max
frame time and spawn CPU time in `Logs/CrowdSmooth/spawning-metrics.txt`.

The current envelope, gait calibration, order audit, and acceptance evidence are in
[`docs/CrowdEnvelopeValidation.md`](../docs/CrowdEnvelopeValidation.md).
