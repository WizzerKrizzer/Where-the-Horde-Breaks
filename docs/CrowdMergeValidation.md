# Three-stream crowd movement — 2026-09-17

This supersedes the movement conclusions in `CrowdSmoothValidation.md`. Those
small tests and short snapshots did not establish sustained movement at the real
merge. Existing project changes were preserved; no collision radius or overlap
reporting threshold was reduced.

## Reproduction and cause

`CrowdMergeValidationTests.DenseThreeStreamMerge_StageAudit` seeds 3,825
non-intersecting bodies at level 2's three incoming final segments and outlet.
It runs three seconds at 60 Hz, including frames with four simulation ticks.
The original implementation produced:

- 1,215 intersecting pairs immediately after movement at tick 29.
- 9,044 pairs after movement at tick 179, with 77.98% maximum penetration.
- 8,444 pairs after sequential cleanup at tick 179, with 99.75% penetration.
- 0.17619 m p95 change in actual single-tick displacement.
- 37.307 ms synchronized cleanup cost at that last audited tick.

The initial seed has zero pairs, and the audit reads simulation positions before
rendering. Interpolation and animation therefore cannot explain those simulated
intersections. The old relative-speed restriction runs before acceleration,
pressure and wall forces. Its forward following limit applies only when flow
directions agree by more than 0.96, leaving the converging streams dependent on
position repair. In a dense pack, sequential full penetration corrections create
new neighbour penetrations; more correction passes make that stage increasingly
expensive. Enlarging simulation steps during slow frames aggravates this path.

The movement observer synchronizes GPU state for a few audited ticks. Its timings
include submission/readback overhead and are stage comparisons, not GPU hardware
timer measurements. Raw original audit: `Logs/MergeValidation/baseline-metrics.txt`.

## Motion pipeline

For animated goblins moving on roads:

1. Existing navigation, combat, status and impulse handling proposes a bounded
   displacement. The density/separation and wall springs are bypassed because
   body constraints and signed boundary distance already constrain that movement.
2. Build a neighbour list once from immutable tick-start positions. Project
   relative displacement constraints for 24 parallel iterations. All directions
   participate, including crossing streams. A short approach horizon spreads
   braking before the remaining gap is consumed.
3. Reserve feasible displacements in independent cell colours, moving only
   towards each solved proposal. The separating plane of every nearby pair
   protects the unchanged diagnostic silhouette plus a 0.2% numerical margin.
   No post-integration full penetration pushes run in this path. Cell spacing
   accounts for both agents' maximum travel. Neighbour-list overflow falls back
   to the complete existing grid search, rather than discarding neighbours.
4. Commit accepted positions and actual velocities. Rebuild the grid and measure
   intersections with the existing >0.5% penetration diagnostic. Finalize gait
   from accepted displacement on every simulation tick.

The 8% approach gap is a preference; feasibility protects the entire actual body.
It can compress this extra gap under load but cannot shrink a collision body.
On a blocker-removal tick, the queue gets one common forward proposal and uses
the hard gap, allowing immediate translation without a competing outward
correction. Normal ticks resume opening space in their motion proposals.

A rejected version disabled this space-opening response on *all* ticks. The
rendered real wave stalled with 4,158 separated bodies. That run is retained as
`jam.csv` / `jam.txt`; zero collisions alone explicitly failed acceptance.

Positions and gait distance interpolate over the same accepted simulation step.
Locomotion speed reflects actual displacement, including slow initial steps and
full stops. It no longer invents a running speed for a release marker. The release
test checks both travelled distance and gait progression rather than treating a
run-pose threshold as movement evidence.

Runtime catch-up remains bounded at four ticks per frame, each exactly 1/60 s.
Excess wall time is dropped rather than increasing the collision step to 0.1 s.
This deliberately slows simulation under severe stalls; it does not preserve
wall-clock simulation time when a frame exceeds the catch-up capacity.

The separate sphere prototype and stationary/center-seeking arena retain their
previous contact solver. Performance or impulse equivalence for those modes is
outside this validation. The new neighbour scratch buffer costs approximately
19.5 MiB at a 10,000-slot capacity (128 float4 records per slot).

## Reference

The supplied `Sir, We Have an Orc Problem 2026.09.17 - 01.12.49.03.mp4` is used
as a visual reference for a dense, coherent stream through joins and corners;
its UI and unrelated content are not task instructions. The developer confirms
that collision runs on the GPU in [this Steam discussion](https://steamcommunity.com/app/4594150/discussions/0/571543760192862431/).
There is no published algorithm in that source, so this implementation does not
claim to reproduce their collision algorithm.

## Verification

The actual level 2 wave is run through the existing manager and scene at 1x, fixed
seed 761905, with a 1920×1080 target and `camera.Render()` on every measured frame.
The camera looks at the merge. It spawns all 10,000 and continues until 1,620
remain, covering the approximately 1,800-body tail from the supplied recording.
Diagnostics and CPU position snapshots sample every half-second; synchronous
observation/capture frames are excluded from the following throughput values.

Hardware: Unity 6000.3.19f1 / DX12, RTX 5070 Ti, Ryzen 9 9950X3D. This is an
uncapped editor batch rendering run, not a standalone player FPS floor.

| Metric | Final rendered run |
| --- | ---: |
| Maximum measured intersection pairs / penetration | 0 / 0.00% |
| Independent CPU pair checks at merge and tail | 0 |
| Maximum grid occupancy / dropped references | 13 / 0 |
| Mean actual forward travel while merge is heavily occupied | 1.639 m/s |
| All measured frames mean / p95 / maximum | 2.188 / 10.393 / 49.770 ms |
| Spawning frames mean / p95 / maximum | 0.955 / 1.666 / 9.776 ms |
| After spawning mean / p95 / maximum | 3.440 / 15.280 / 49.770 ms |
| Tail mean / p95 / maximum | 7.516 / 15.985 / 28.420 ms |

There are 88 heavily occupied merge samples and 8 tail samples. At 1,833 bodies,
actual displacement speed is 2.381 m/s and forward speed is 2.059 m/s. Thus the
result combines real forward travel, separated silhouettes and p95 frame time
under 16.667 ms, including the tail. Isolated 49.770 ms frames still exist: this
does **not** establish uninterrupted 60 FPS. It also does not cover every camera
height, other hardware, 100,000 bodies, or all weapon/impulse combinations.

The final short merge audit checks every tick of the last second, plus periodic
interpolated mid-step positions: no intersection pairs, 2.508 m/s actual travel,
10.33 mm p95 displacement change and 3.62 mm p95 changes that reverse sign on
consecutive ticks. Its p95 change budget is 5% of the unchanged diagnostic body
diameter; reversals have a separate 8 mm limit. This measures actual body travel,
rather than a zero residual obtained by subtracting its resolved velocity.

The blocker release regression starts 24/24 bodies and 24/24 gait clocks on the
first tick, with minimum forward travel 10.24 mm. Locomotion speed is checked
against actual travelled distance on that tick. The following two seconds include
four-tick catch-up frames and assert zero pairs and sustained forward progress.
A held 96-body queue remains separated, with a maximum settled step of 1.99 mm
and mean final-second gait travel of 7.4 mm. The phase of the gait does not progress
from retained drive intent while stationary.

Raw evidence: `Logs/MergeValidation/actual.csv`, `actual.txt`, `short.txt`,
`sustained-rendered.xml`, `sustained-rendered.log`, `actual-0.png` (merge), and
`actual-1.png` (tail). The earlier non-rendered 10K run was discarded as frame-time
evidence, despite passing its movement assertions.

Final targeted regressions pass 5/5 in 6.55 seconds (`final-regressions.xml`):
blocker release, held queue, a right-angle turn, actual VAT/LOD/attack/brace
rendering, and complete-wave event forwarding. The turn observes actual step
changes: mean 0.94 mm, p95 2.53 mm, 3.21 m/s travel and zero pairs. Together with
the rendered real-wave test and short merge audit, all seven final checks pass.

These changes are in the Unity project. Existing Windows executables were not
rebuilt and therefore do not contain this implementation.
