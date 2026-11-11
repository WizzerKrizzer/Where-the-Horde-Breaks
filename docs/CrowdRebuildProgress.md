# GPU crowd rebuild progress

Last updated: 2026-10-04

This is the durable checklist for the XPBD crowd rebuild. A checked validation
item means the stated criterion passed and its evidence was saved locally under
`Logs/Reports/CrowdRebuild/`. Generated logs are intentionally ignored by Git; the test
and acceptance criteria remain tracked in the repository.

## Baseline and infrastructure

- [x] Preserve the original dirty crowd tuning as
  `Logs/Reports/CrowdRebuild/baseline-current-dirty.patch`.
- [x] Add the shared 60 Hz, eight-iteration `CrowdSolverProfile`.
- [x] Add the fixed-tick `CrowdLabRunner` backed by the production GPU solver.
- [x] Add the interactive `Assets/Scenes/CrowdLab.unity` preview with scenario,
  play/pause/reset/step, slow-motion, disk/contact, flow-vector, trail, camera,
  and live-diagnostic controls.
- [x] Add JSON/CSV lab reporting and solver diagnostics, including separate
  disk, blocker, and wall deep-contact counts and true >0.2-second persistence.
- [x] Replace fixed per-cell storage in the XPBD candidate with the compact
  count/scan/scatter grid and explicit capacity diagnostics.
- [x] Keep XPBD behind a temporary editor/test selector while validation is in
  progress, then remove the selector after the real-wave and regression gates.

## Automated Crowd Lab gates

- [x] Two-body overlap resolves without persistent deep penetration.
- [x] Blocked queue restarts eligible bodies within three fixed ticks.
- [x] Wall impact remains inside the route and recovers into forward flow.
- [x] Right-angle bend retains walkability and sustained forward flow.
- [x] Three-way merge retains walkability and sustained forward flow.
- [x] Narrow blocked exit forms a stable local queue after 300 approach ticks
  plus a 120-tick late-crowding observation window.
- [x] Explosion into a packed group propagates impulse and recovers without
  persistent deep contact.
- [x] 10,000-body stress run has no persistent deep contacts or capacity loss.
- [x] 50,000-body wide-field run has no lost bodies or capacity loss.

## Determinism matrix

- [x] Presentation batching of one versus four ticks produces matching state
  within 1 mm for the seeded bend scenario.
- [x] Run every lab scenario at 30, 60, and 120 rendered-FPS schedules.
- [x] Run every lab scenario at 1x and 10x game speed.
- [x] Confirm matching aggregate diagnostics for identical seed/tick counts.
  Physical state comparison treats identical disks as an unordered set within
  each lifecycle state, so a harmless identity exchange is not a mismatch.
- [x] Replay a captured real Level 2 persistent-contact state for 120 fixed
  ticks. The retained solver clears the captured pair before the 0.2-second
  threshold and produces zero persistent bodies during the replay.

## Gameplay integration

- [x] Re-run the real Level 2 10,000-enemy XPBD candidate through merge and tail
  with the corrected full diagnostic readback and wall-contact measurement.
  - The accepted 2026-10-03 run spawned all 10,000 bodies and completed 233
    merge samples plus 26 tail samples. It reported zero persistent deep-contact
    bodies, zero grid/pair omissions, zero dropped bodies, and zero boundary
    violations. The maximum transient deep-contact count was one disk pair;
    blocker and wall deep-contact counts remained zero.
- [x] Re-measure Level 2 merge speed and rendered p95 on the reference PC after
  the retained CrowdLab tuning: the accepted run measured 1.478 m/s mean
  forward speed, 0.733 ms rendered p95, and 0.089 ms GPU timestamp p95 on the
  RTX 5070 Ti reference system.
- [x] Run combat, damage/status, targeting, blocker, death/escape, culling,
  save/load, and build regressions with XPBD enabled.
  - The 2026-10-02 broad PlayMode comparison produced exactly the same result
    with and without the test-only XPBD switch: 55 passed, 24 failed, and 9
    skipped, with identical failing test names. XPBD therefore adds zero
    failures relative to the preserved dirty baseline.
  - After promotion on 2026-10-03, the no-override production-path PlayMode run
    again produced 55 passed, 24 failed, and 9 skipped with the identical
    baseline failure-name set. The Windows development player build also
    succeeds with XPBD compiled as the production horde path.
  - The backend-wide migration is now complete. The final production-default
    XPBD run produced 61 passed, 18 failed, and 9 skipped: all five XPBD-only
    fixture failures from the first audit were corrected, while XPBD also fixes
    six failures present in the preserved baseline. The remaining 18 names are
    all members of the pre-existing baseline set.
  - After deleting the legacy runtime on 2026-10-04, the final broad run improved
    to 63 passed, 16 failed, and 9 skipped. It adds no failure relative to the
    preceding 61/18/9 production-XPBD result; the dense merge stage audit and
    data-horde entrance-coverage test now also pass.
  - Capped area knockback now writes the external-impulse channel. A strong
    impulse keeps 74.86 m/s after its first tick while resolved travel remains
    capped to the configured 0.12 m. Wall and blocked-row shockwaves both release
    and reflect through the shared XPBD constraint result in the impact tick.
  - The initially XPBD-only barricade failure was fixed by making the reserved
    attacker stop and by using the same contact envelope for engagement. Its
    targeted gameplay regression and the 12/12 CrowdLab suite both pass.
  - EditMode is 29/31: save/load coverage passes; the two failures are existing
    flow-field/progression expectations unrelated to the solver adapter.
  - The isolated Windows development player build succeeds.
- [x] Make XPBD the production path for every horde level and remove the
  editor/environment comparison selector.
- [x] Remove the legacy movement, separation, cleanup, predictive-motion, and
  special-profile branches.
  - The dead compute kernels, motion buffers, CPU separation prototype,
    reservation/sliding test knobs, comparison selector, and per-level runtime
    physics-profile branches are deleted. Existing serialized
    `HordePhysicsProfile` fields remain hidden only so old assets deserialize;
    no runtime code reads them.
  - The single retained crowd path uses the compact XPBD grid and eight fixed
    iterations at 60 Hz. The capped fixed-cell grid remains only for gameplay
    targeting/render-list queries and is not used to generate contacts or
    solver diagnostics.

## Art pass

- [ ] Begin only after all solver and gameplay gates above pass.
- [ ] Author and bake the compact goblin-raider atlas.
- [ ] Add the separate contact-shadow pass and 16 authored facings.
- [ ] Validate the art at normal size, 100 bodies, and the Level 2 10,000-body
  run before making it the default.

## Painted-body contact calibration (2026-10-04)

- [x] Separate the painted torso's physical radius from the full sprite card's
  render scale. `bodyRadiusPerVisualScale` is now 0.56, reducing production
  body-to-body spacing by 44% without resizing the art. Apply the same factor
  to the minimum collision diameter, spawn clearance, and navigation wall inset.
- [x] Keep the physical disks and diagnostic outlines consistent with this
  calibration. The art-free CrowdLab still uses its explicit 0.43 m disks.
- [x] Prioritize deep disk contacts after four consecutive deep-contact ticks,
  ramping up their unilateral correction before the 0.2-second persistence gate.
  The initial tighter candidate failed this gate; the retained correction passes
  both replay of that captured state and a fresh real-wave run.
- [x] Pass the 18-test focused suite: all 12 CrowdLab tests, the new equal/mixed
  sprite-scale packing regression, barricade combat, capped knockback, and three
  shockwave regressions. The barricade fixture now uses a narrow road so a random
  lateral spawn cannot correctly bypass its target.
- [x] Pass the real Level 2 10,000-enemy merge/tail gate: 84 merge samples and 10
  tail samples; zero persistent contacts, grid failures, dropped bodies, or
  walkability violations. Mean forward flow is 1.901 m/s; rendered p95 is
  0.819 ms, and GPU timestamp p95 is 0.108 ms. Transient deep disk contacts peaked
  at 20; blocker and wall deep-contact counts stayed zero.

Current packing evidence is in `Logs/Reports/CrowdRebuild/TorsoPacking-20261004/`:
`Focused-results.xml`, `Replay-results.xml`, `Level2-results.xml`, and
`Level2-final/actual.txt` plus `Level2-final/actual-1-1.png`. The separate `Level2/`
folder contains debugging history; use `Level2-final/` for current results.
The broad PlayMode results below predate this calibration; that complete suite
was not rerun for the packing change. See the official rollout below for the
new focused results and Windows build.

## Official compact-body standard rollout (2026-10-04)

- [x] Retain the user-approved compact-body variant as the shared production
  default, not a preview-only setting. The profile and dependent production
  implementation are included in the Git checkpoint.
- [x] Audit authored navigation and production solver configuration for every
  GPU horde level: 2, 2.1, 4, 5, 6 and 7. All use the same default profile,
  60 Hz tick, eight iterations, per-body radii and calibrated collision floor.
- [x] Fix the rollout audit's Level 5 startup failure: its 383,910-cell grid
  exceeded the original single-chunk 65,536-cell prefix scan. Block totals now
  scan in 256-entry chunks with a shared prefix carry. The explicit dispatch
  limit is 4,194,240 cells and is checked before resource allocation.
- [x] Add a multi-chunk regression that verifies every cell's exclusive prefix
  offset, the final total and unique agent membership beyond two scan chunks.
- [x] Apply the same 0.56 contact-radius standard to individual actors in
  levels 1 and 3. Remove their 0.62 m minimum gap and match primitive body width
  to the envelope without changing height. Keep the individual-actor gameplay
  backend so shaman healing, zombie revival, flying and vampire combat remain
  intact. These two levels are **not** a GPU XPBD migration.
- [x] Pass the official focused suite: 25/25, including the original 18 gates,
  individual-actor tests, all-horde configuration audit and large-grid scan.
- [x] Build the post-rollout Windows development player successfully in the
  isolated validation project. Evidence: `Official-build.log` in the same folder.
- [x] Repeat the real Level 2 10,000-enemy merge/tail gate after the scan fix:
  1/1 passed, 84 merge and 10 tail samples, zero persistent bodies, grid failures,
  dropped bodies and walkability violations. Forward flow remains 1.901 m/s;
  rendered p95 excluding observation/capture stalls is 0.811 ms and GPU timestamp
  p95 is 0.108 ms. `Level2-results.xml` and `Level2-final/` now contain this run.

Official evidence: `Logs/Reports/CrowdRebuild/TorsoPacking-20261004/Official-results.xml`
and `Official.log`. The first audit failed at Level 5; the results listed here
are the post-fix passing run. The complete broad PlayMode suite was not rerun
in this rollout; its pre-existing failures remain documented above.

## Latest local evidence

- `Logs/Reports/CrowdRebuild/PlayMode-legacy-retired-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-legacy-retired-final.log`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-legacy-retired-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-legacy-retired.log`
- `Logs/Reports/CrowdRebuild/Level2-10000-legacy-retired-20261004/`
- `Logs/Reports/CrowdRebuild/CrowdLab-legacy-retired-final-results.xml`
- `Logs/Reports/CrowdRebuild/CrowdLab-legacy-retired-final.log`
- `Logs/Reports/CrowdRebuild/Build-Windows-Development-legacy-retired.log`
- `Logs/Reports/CrowdRebuild/PlayMode-shockwave-blocked-row-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-shockwave-return-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-explosion-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-production-xpbd-backend-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-production-xpbd-backend-final.log`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-backend-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-backend-final.log`
- `Logs/Reports/CrowdRebuild/Level2-10000-backend-final-20261003/`
- `Logs/Reports/CrowdRebuild/Build-Windows-Development-backend-final.log`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-shockwave-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-xpbd-area-knockback-final-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-final-20261003-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-final-20261003.log`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-passed-20261003-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-passed-20261003.log`
- `Logs/Reports/CrowdRebuild/Level2-10000-passed-20261003/`
- `Logs/Reports/CrowdRebuild/PlayMode-production-xpbd-regression-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-production-xpbd-regression.log`
- `Logs/Reports/CrowdRebuild/Build-Windows-Development-production-xpbd.log`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-retained-20261002-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-retained-20261002.log`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-postforensic-20261002-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-crowdlab-blockerfix-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-best-20261002-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-best-20261002.log`
- `Logs/Reports/CrowdRebuild/PlayMode-level2-10000-forensic-20261002-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-persistent-replay-20261002-results.xml`
- `Logs/Reports/CrowdRebuild/Level2-10000-forensic-20261002/`
- `Logs/Reports/CrowdRebuild/PlayMode-current-baseline-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-xpbd-regression-blockerfix-results.xml`
- `Logs/Reports/CrowdRebuild/PlayMode-xpbd-barricade-stop-warm-results.xml`
- `Logs/Reports/CrowdRebuild/EditMode-current-results.xml`
- `Logs/Reports/CrowdRebuild/Build-Windows-Development.log`
- `Logs/Reports/CrowdRebuild/CrowdLab-preview-smoke-20261002.png`
- `Logs/Reports/CrowdRebuild/RightAngleBend-1451.json`
- `Logs/Reports/CrowdRebuild/ThreeWayMerge-761905.json`
- `Logs/Reports/CrowdRebuild/BlockedExit-8201.json`
- `Logs/Reports/CrowdRebuild/ExplosionRecovery-9101.json`
- `Logs/Reports/CrowdRebuild/StressTenThousand-10001.json`
- `Logs/Reports/CrowdRebuild/StressFiftyThousand-50001.json`
- `Logs/Reports/CrowdRebuild/WallImpact-1701.json`

The final 2026-10-04 CrowdLab result is the complete passing 12/12 class,
including every scenario at all six FPS/game-speed schedules. The post-removal
Level 2 run spawned all 10,000 enemies, reached 237 merge and 26 tail samples,
reported zero persistent contacts, omissions, dropped bodies, and boundary
violations, and measured 1.464 m/s mean forward flow with 0.738 ms rendered p95
and 0.091 ms GPU timestamp p95. Its maximum transient deep-contact count was
three disk pairs; blocker and wall counts remained zero. Older Level 2 captures
remain as forensic history but must not replace this passing 19-counter run.

## Using the interactive preview

1. Open `Assets/Scenes/CrowdLab.unity`, or choose
   `Tools > Crowd Lab > Open Interactive Preview` in Unity.
2. Press Play. The preview starts paused on the wall-impact scenario.
3. Choose a scenario, then use Play, Pause, Reset, Step 1 Tick, and the speed
   slider. A speed of 0.10x is useful for inspecting individual corrections.
4. Toggle contacts, flow vectors, and trails. Yellow rings indicate touching or
   wall-constrained bodies; red rings indicate deep body overlap.
5. Use the mouse wheel to zoom and right-drag to pan. The live panel reports
   deep/persistent contacts, constraints, occupancy, capacity failures, and
   walkability violations.

## Phase 2/3: knockback unification and corpse layer (2026-11-02)

Changes: projectile knockback uses the shared `knockbackVelocity` channel
(`_KnockbackDamping`, default 1.8), contact pairs can exchange knockback
(`_ImpulseTransfer`, currently 0 = off), dead bodies are recorded in a GPU ring
buffer (16K) drawn with one indirect call. The new buffers share one `_Aux` UAV
because `Simulate` and `SolveXpbdPairs` are at the 8-UAV limit.

Benchmark `MeasureGpuHordeWithPeriodicImpacts` (shockwave area push + catapult
splash every 30 frames, 240 frames), RTX 5070 Ti, Direct3D12, batchmode:

| Agents | CPU frame before | CPU frame after | Change |
|-------:|-----------------:|----------------:|-------:|
| 1 000 | 0.404 ms | 0.802 ms | within run-to-run noise at this scale |
| 100 000 | 11.402 ms | 11.333 ms | -0.6% (target: regression <= ~10%) |

"Before" is the Phase 0 simulation (`8e03186`), "after" is Phase 2. GPU frame
timings were not available in batchmode (reported 0), so only CPU frame time is
compared. `_ImpulseTransfer` is off, so the neighbour exchange cost is not
measured yet; repeat with it enabled before turning it on.

Full PlayMode suite: 95 tests, 55 passed, 31 failed. The same 31 failed after
Phase 0 (and most already failed before the art removal), so Phase 2 added no
failures.
