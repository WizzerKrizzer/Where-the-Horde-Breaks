# Crowd envelope, gait and feasible movement audit — 2026-09-26

This follows `CrowdMergeValidation.md`. All pre-existing work was retained. The
starting tracked patch, source files (including untracked assets) and prior results
are archived in `.codex-temp/crowd-envelope/baseline`.

## What the geometry actually protects

`Tools/AuditGoblinLocomotion.py` reads every frame and vertex of the shipped
half-float vertex-animation data, using dominant skin bones from the source GLB
only to label torso, shoulders and feet. The old radius was 0.5 local units.
`RunFast` reaches 0.624 at the torso and 1.496 over its whole pose. The previous
zero-overlap diagnostic therefore did not establish that rendered models were
separated.

Locomotion now uses the compact `Running` clip with independent phase offsets.
Its complete horizontal envelope is 0.935 local units. The selected planted
recovery pose (frame 71 of 97 in `ChargedUpwardSlash`), including the conservative
breathing bound, fits in 0.797. The shared protected radius is **0.96**, plus the
existing 1.04 packing skin. This covers the entire locomotion pose, not only a
height slice through the torso. Rotating or interpolating these poses cannot
increase that circular envelope.

The manager uses this radius for entrance and road clearance. GPU contact and
overlap measurement use the same radius; animated bodies are no longer clipped
to a smaller nominal maximum diameter. The search and independent-cell spacing
also account for the largest actual spawned body. The >0.5% overlap threshold and
the 0.2% feasibility skin have not been reduced. Combat slashes deliberately have
a larger action envelope than locomotion; this is not a claim that attacking arms
never cross neighbouring space.

At scale 0.22, the protected diameter increased from 0.2288 to 0.4393 m. This
necessarily changes road capacity and spawning throughput. The short seed uses
0.51 m spacing instead of 0.265 m to preserve approximately the same occupied
area fraction with valid initial bodies. Its before/after population counts must
not be treated as identical workloads. The actual level-2 acceptance wave still
spawns all 10,000.

## Spawn clearance is a time-bounded observation

The rejected post-solver wave exposed one remaining source of impossible starts:
an asynchronous GPU query could report an empty entrance based on the instant it
was read back, even though a body had moved into it since the query was issued.
The targeting cache also rounded positions to quarter metres, which is acceptable
for combat selection but not for a body-clearance decision. The recorded pair was
at the entrance, 39.9% interpenetrating, and is retained in
`Logs/Reports/CrowdEnvelope/spawn-overlap-run` as rejected evidence.

Spawn clearance now uses an exact query key, records its submission frame and
the cumulative bounded predictive travel, and includes one such travel bound in
the GPU observation disk. A result is accepted only when the unconsumed disk
still covers the true body separation and the caller's lane frame watermark is
newer than every spawn it must observe. This keeps independent entrance lanes
concurrent; a short-lived global spawn generation was rejected because it
serialized those lanes unnecessarily.

The spawn spacing itself is the measured torso envelope plus an 8% skin. The old
1.5x floor was an additional queueing gap, rather than a collision requirement,
and stranded the final 85 bodies at the entrance. It has been removed. This does
not reduce the model envelope, collision radii, pair diagnostic, or feasibility
skin. `SpawnClearance_RejectsMovedQueriesAndDifferentPositions` exercises
movement between query and use, two formerly shared cache positions, and a spawn
in the same render frame.

## Why the separated pack could stop

The envelope-only real-wave run stopped with **2,508** bodies still alive and
zero reported overlaps. It is rejected, retained in
`Logs/Reports/CrowdEnvelope/stalled-run`, and is not used as successful acceptance.

The old reservation step accepted only a fraction of a proposed movement ray.
When its lateral component encountered a neighbour, it could cancel an available
forward step as well. A three-second tightly packed merge makes this repeatable:
with identical starting positions and body sizes, the ray implementation travels
**0.338 m/s**, despite reporting zero collisions.

The replacement chooses the closest feasible 2D displacement. It intersects the
neighbour separating half-planes, obstacle plane and maximum-travel disk. The
existing swept-plane check then protects the final road-constrained step before
positions are committed. Cell colours still prevent simultaneous conflicting
writes; overflow retains a complete-grid feasibility fallback. This is movement
constraint solving, not post-movement penetration repair or random position noise.
The same tight seed with the new step travels approximately **2.7 m/s**, with zero
sampled or interpolated collisions, p95 step change under 8 mm and alternating
change under 4 mm. The original absolute motion thresholds remain intact.

An attempted refinement from joint proposals was rejected because it produced
up to 22 intersecting pairs. It is not the shipped algorithm.

## Processing order and apparent grids

`CrowdOrderAuditTests` repeats the same seed with the ordinary order, an ordinary
repeat, reversed cell/agent order, and both orders of the corrected solver. It
then removes the outlet leaders without moving the surviving bodies and records
selected agents every tick for two more seconds.

The original seed is already hexagonal. Reversing traversal changes individual
trajectories, but ordinary repeats also diverge because GPU grid insertion is
concurrent. Coherence at the actual 0.62 m cell period remains low (roughly
0.01–0.04); these measurements do not prove that cell colouring itself generates
a visual lattice. The demonstrated defect is the ray restriction and loss of
available forward travel. No claim is made that every row in a compressed pack
is erroneous or that all ordering effects are eliminated.

With the corrected solver, the ordinary and reversed release audit starts
478/478 and 479/479 surviving bodies immediately, respectively, and sustains
3.25 and 3.22 m/s over the final second, with zero pairs. Full per-tick traces and
both control runs are retained in `Logs/Reports/CrowdEnvelope/final/order-*`.

## Animation and presentation

The old running strides were 8.2–9.2 local units per cycle. Stance-foot displacement
in the compact clip gives a median of 3.699. The configured stride is 3.73, including
horizontal packing scale when converted to world space (0.8534 m at scale 0.22).
The measured median absolute stance-foot drift fraction drops from 58% to 14%.
This is cadence calibration, not perfect foot-locking or a claim of zero residual
sliding in the source animation.

The brace transition is limited to 0.025–0.10 m/s; ordinary slow travel uses the
measured gait. Position and accumulated gait distance interpolate over the same
accepted simulation step. The held queue and first-tick release tests check actual
travel and gait progression together. Separate captures cover free movement,
LOD, combat and the planted pose. Collision rings are drawn at interpolated
positions, with matching close and overview cameras.

## Timing and reproducibility

The HUD now labels `WORK` as **CPU SUBMIT**. Frame timing statistics are enabled,
but this environment returns no GPU frame duration through `FrameTimingManager`.
The acceptance test therefore uses GPU timestamp markers around simulation and
explicit camera rendering. These are completed GPU queue intervals, with the
Recorder's documented three-frame delay, not CPU submission timings. They do not
claim to include an unmeasured final window presentation. See Unity's
[GPU Recorder timing documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Profiling.Recorder-gpuElapsedNanoseconds.html).

Every measured frame is rendered at 1920×1080. Cameras alternate every five
seconds. The primary wall-time distribution includes synchronous observation,
PNG capture and checkpoint stalls; a separate distribution identifies ordinary
frames. The raw CSV records extra simulation ticks, including four-tick catch-up.
Hardware: RTX 5070 Ti, Ryzen 9 9950X3D, Unity 6000.3.19f1, DirectX 12. Editor batch
results are not a standalone-player FPS guarantee.

Run from the project directory:

```powershell
python Tools/AuditGoblinLocomotion.py
./Tools/RunCrowdEnvelopeValidation.ps1 -Mode Short
./Tools/RunCrowdEnvelopeValidation.ps1 -Mode Order
./Tools/RunCrowdEnvelopeValidation.ps1 -Mode Full
```

The script uses an isolated project under `.codex-temp`, preserving the open main
editor. `Full` is deliberate and is not included in `Short`. The initial full run
hit NUnit's 180-second limit; a later one exposed the 2,508-body movement stall;
and the first freshness correction made the 1.5x staging gap visible. These are
retained as rejected evidence. The final test permits 420 seconds so it can reach
the real 1,700-body tail instead of treating an unfinished wave as a result.

## Accepted 10,000-wave result

The final level-2 run passed on 2026-09-26 in 301.68 seconds. It rendered every
measured frame at 1920×1080, alternated close and overview cameras, spawned all
10,000 agents, and reached 1,065 remaining bodies. It recorded 456 merge samples
and one tail sample. GPU and CPU diagnostics report zero contact pairs, zero
dropped agents, and a mean forward speed of 0.946 m/s over the merge; the final
tail observation moved 2.146 m/s forward.

Completed GPU timestamp scope (simulation plus explicit camera render) measured
3.687 ms mean and 14.224 ms p95. Ordinary wall frames excluding synchronous
observation and capture stalls measured 4.273 ms mean and 14.570 ms p95; the tail
measured 4.135 ms mean and 11.382 ms p95. The unfiltered distribution records the
expected capture/checkpoint outliers separately. Raw timing, state snapshots and
close/overview collision-outline captures are in `Logs/Reports/CrowdEnvelope/MergeValidation`.
