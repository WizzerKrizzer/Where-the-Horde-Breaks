# Crowd spawning and animation validation — 2026-09-15

The sustained three-stream correction and final rendered 10K validation are in
[`CrowdMergeValidation.md`](CrowdMergeValidation.md). That report supersedes the
movement conclusions below and records both successful and rejected runs.

The latest implementation and measurements are in the 2026-09-16 follow-up at
the end. Earlier sections retain the historical runs.

## Changes

- Spawning uploads through separate staging buffers and a GPU copy kernel.
- Only visibility culling writes instance counts. Previously a spawn replaced the
  detailed draw count with the total spawned population, including between fixed
  simulation ticks when the visible-index list had not been rebuilt.
- Spawn preparation runs once per rendered frame, rather than again in every
  simulation substep.
- Contact projection no longer feeds positional corrections back into the next
  tick's drive velocity. Actual travel is tracked separately for presentation.
- Ordinary crowd locomotion intent remains ready while contacts hold a body in
  place. When room opens, every connected row can advance on the first 60 Hz
  simulation tick instead of inheriting the previous row's stale zero speed.
  Queues attached to a living barrier still retain their strict contact limit;
  each body remembers that barrier's GPU slot and releases it immediately when
  the slot is removed or destroyed.
- Rendering interpolates positions between simulation ticks. Lateral and pushed
  travel advances stepping. Low speed blends into a planted brace with breathing
  and weight shifting, using existing animation assets on the GPU. Starting the
  run pose now reacts immediately to resolved movement; only settling back into
  the brace is smoothed.
- All maps allow a 2.5 m minimum camera height, with finer wheel steps close up.

## Measured rendering throughput

Unity 6000.3.19f1, Windows, RTX 5070 Ti, Ryzen 9 9950X3D. The explicit PlayMode
benchmark uses the actual Right-Angle Collision Lab wave, red goblin visuals,
1x speed, fixed initial random seed, a 1280×720 render target, and manual camera
rendering on every measured frame. Vsync and the frame cap are disabled.
Spawning samples begin after the first 500 enemies; settled samples begin 60
frames after all 10,000 have spawned.

| Measurement | Before | After |
| --- | ---: | ---: |
| Spawning frame mean | 7.235 ms | 1.676 ms |
| Spawning frame p95 | 25.621 ms | 5.759 ms |
| Spawning frame maximum | 59.292 ms | 8.405 ms |
| Spawn CPU mean | 0.124 ms | 0.055 ms |
| Spawn CPU p95 | 0.902 ms | 0.279 ms |
| Settled frame mean | 9.311 ms | 7.647 ms |
| Settled frame p95 | 10.292 ms | 8.447 ms |
| Time to spawn 10,000 | 29.25 s | 24.28 s |

These are editor/test-runner frame throughput measurements, including CPU and
GPU synchronization, not isolated GPU timings or a standalone FPS guarantee.
Spawn timing depends on available entrance space and asynchronous clearance
queries, so sample counts and exact crowd trajectories differ between runs.
Raw measurements are in `Logs/CrowdSmooth/before-metrics.txt` and
`Logs/CrowdSmooth/after-metrics.txt`.

## Regression checks

The final focused run passes all **7/7** checks:

- Staged spawning preserves existing movement, health, and culled draw counts.
- Stationary goblins keep their presentation clock; side pushes advance gait.
- A packed goblin queue settles without overlapping or running in place.
- A 24-body packed queue releases all 24 bodies and all 24 run poses on the
  first tick after its barrier is removed (previously 3/24 bodies and 0/24 poses).
- A dense queue traverses a narrow right-angle turn.
- A complete wave forwards all escape events.
- Actual VAT rendering, distant LOD, attacks, and the close braced pose.
- The rendered 10,000-enemy spawning benchmark above.

The wave-completion smoke test now allows four seconds for the final body and
its asynchronous escape event, instead of racing the last event at two seconds.
The goblin queue measured a 0.3016 m median spacing and a maximum settled step
of about 0.00049 m in the initial regression run.

A broader 50-test run passed 42 tests. Seven of its eight failures were also
reproduced against the pre-change runtime: knockback-chain propagation, culling
expectations, damage/escape events, overflow diagnostics, slow/burn status,
accelerated spawning ratio, and Level 7 authored content. The remaining wave
completion timeout is covered by the final passing run. The existing failures
were not weakened or disabled. XML reports are retained in `Logs/CrowdSmooth`.

Visual captures: `Logs/CrowdSmooth/braced-close-45.png` and
`Logs/CrowdSmooth/braced-close-89.png`.

Windows Development build succeeded. The copied player is
`Builds/CrowdSmooth/WhereTheHordeBreaks.exe`; build output is recorded in
`Logs/CrowdSmooth/build.log`. The normal player's existing goblin-preview
preference/default is preserved. The fixes are also available directly in the
main Unity project on the next Play Mode start.

## Immediate queue release follow-up — 2026-09-15

The release regression packs 24 animated goblins behind a live GPU barrier,
settles the queue, removes the barrier, and reads the very next 60 Hz simulation
tick. Before the follow-up, only 3/24 bodies moved and 0/24 visuals had entered
their run pose. After it, 24/24 bodies moved, 24/24 visuals entered their run
pose, and the minimum forward travel was 0.00890 m on that first tick. The ready
drive begins at a shared safe queue speed and normal acceleration supplies the
rest; this avoids collision churn while still removing the dead period.

The final focused regression passes 8/8 tests. A fresh 10K rendered benchmark
also passes: spawning mean 1.216 ms, p95 3.116 ms, max 8.498 ms; settled mean
9.852 ms, p95 13.273 ms, max 14.304 ms. The settled p95 remains below the
16.667 ms budget for 60 FPS. Raw output is retained in
`Logs/CrowdSmooth/queue-release-metrics.txt` and the focused XML in
`Logs/CrowdSmooth/queue-release-tests.xml`.

The follow-up collision correction now propagates the same safe ready speed
through a free queue before movement, so followers do not consume the gap to
the body ahead. The queue still releases in one tick, and the regression records
zero overlap pairs after 120 moving ticks. The initial ready drive is sixteen
percent of requested speed; ordinary acceleration supplies the rest.

Spawn clearance now uses at least 108% of the measured body diameter, including
inside each uploaded batch, and contact movement preserves the same visible air
gap. In the final 10K collision-lab benchmark the HUD diagnostics report 0 pairs
and 0.00% deepest overlap. Spawning p95 is 8.121 ms and settled p95 is 9.254 ms.

## Sustained movement and retired GPU bodies — 2026-09-16

The supplied 17:55 recording showed zero collisions while the pack barely
advanced. Free aligned queues now propagate a neighbour's predicted acceleration
in the current dispatch. Crossing flows do not impose a stopped-leader limit.
Sideways separation opens room without applying a longitudinal separation spring.
Held queues use resolved forward travel rather than velocity intent, and suppress
soft density/overtaking pressure until their barrier is removed.

Active-only ping-pong kernels previously left retired slots untouched in the
other buffer. A swap could restore an escaped or dead body's old live record,
leaving it in the collision grid. Grid construction now carries excluded records
into the other buffer. An agent already in the exit cross-section also emits its
escape event before returning. Both changes are checked by escape, death, and
complete-wave regressions.

Hard silhouette cleanup updates bodies sequentially inside separated cells. Cell
membership stays fixed for each complete color cycle, while neighbour positions
are read live. This prevents a corrected body from moving twice after entering a
later cell color. Grid rebuilds occur between cycles, and cleanup runs once on the
presented tick of a catch-up frame. Spawn clearance checks also run during fast
forward and use at least 150% of the base body diameter.

The final run passes **8/8** tests in
`Logs/CrowdSmooth/motion-retired-buffers.xml`:

- On the actual three-lane map, 10,000 live bodies are compared across two seconds
  at 1x speed. **85.2%** travel faster than 0.75 m/s; mean actual displacement speed
  is **2.12 m/s**, with **0 collision pairs**. These numbers measure position
  changes, not the animation clock or retained drive velocity.
- A held 96-body queue has 0.3257 m median/minimum spacing, a maximum settled
  single-tick displacement of 0.00055 m, and 0.0001 m mean last-second gait travel.
- Removing a barrier starts **24/24 bodies and 24/24 running visuals on the first
  60 Hz tick**, with minimum forward travel of 0.01024 m. The following two seconds
  exercise four-tick catch-up frames and retain zero collision pairs, mean forward
  speed above 1.5 m/s, and every body's forward speed above 0.5 m/s.
- Death/escape events, retirement across empty active-grid swaps, base damage
  forwarding, and a complete wave all pass.

The rendered 10K right-angle benchmark uses the same editor, GPU, fixed seed,
1280×720 target, and 1x settings documented above:

| Measurement | Latest run |
| --- | ---: |
| Spawning frame mean / p95 / max | 2.335 / 8.620 / 52.751 ms |
| Spawn CPU mean / p95 / max | 0.052 / 0.249 / 0.694 ms |
| Settled frame mean / p95 / max | 8.462 / 11.598 / 18.104 ms |
| Time to spawn 10,000 | 64.34 s |
| Maximum cell occupancy / dropped references | 9 / 0 |
| Collision pairs / deepest overlap | 0 / 0.00% |

The independent CPU position check also finds no overlapping pair. Raw data is
in `Logs/CrowdSmooth/spawning-metrics.txt` and the final test log. These are editor
throughput measurements; the recorded maximum includes a 52.751 ms spawning
frame, so the result does not establish a frame-by-frame standalone FPS floor.
The wider entrance clearance affects total spawn duration.

The updated Windows player is `Builds/CrowdSmooth/WhereTheHordeBreaks.exe`;
the build log is `Logs/CrowdSmooth/motion-build-retired.log`.

## Contact jitter correction — 2026-09-17

Movement allowed an eight-percent body gap, while free-crowd projection enforced
50 percent. Alternating full pair ownership plus a separate backward queue push
repeatedly displaced valid neighbours. Projection now shares the movement gap
and averages complementary pair corrections; sequential hard cleanup remains.

Three targeted PlayMode tests pass in **8.28 seconds**:

- 144 moving goblins around a right-angle turn: mean change in per-tick contact
  correction **0.68 mm**, p95 **4.20 mm**, mean actual travel **3.26 m/s**, and
  **zero collision pairs**.
- Released queue: **24/24** bodies and running visuals start on the first tick;
  subsequent catch-up frames retain body spacing and forward travel.
- Held 96-body queue: maximum settled step **0.01 mm**, with planted animation.

Results: `Logs/CrowdSmooth/jitter-short.xml` and `jitter-short.log`. Tests use an
isolated project copy because the user's editor is open. No full 10K benchmark
or Windows build was repeated. This correction is in the Unity project; the
previous Windows executable above predates it.
