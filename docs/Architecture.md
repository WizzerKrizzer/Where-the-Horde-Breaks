# Tower Defense Roguelite Architecture

## Runtime Shape

The current prototype is intentionally simulation-first:

- Data assets define enemies, towers, waves, levels, skill nodes, rewards, and performance profiles.
- Runtime managers execute the level loop: path following, spawning, tower placement, active weapon damage, rewards, and save/load.
- Input is translated into a `GameInputState`, keeping device reads out of gameplay systems.
- The bootstrap scene creates temporary geometry and sample content at runtime so design iteration can start before an art pipeline exists.

## Data-Driven Content

The MVP uses `ScriptableObject` definitions for content. Later production content should move from runtime sample creation to checked-in assets under `Assets/Content`.

Recommended folders:

- `Assets/Content/Enemies`
- `Assets/Content/Towers`
- `Assets/Content/Levels`
- `Assets/Content/Waves`
- `Assets/Content/SkillTrees`
- `Assets/Content/PerformanceProfiles`

## Official crowd standard (2026-10-04)

The user-approved compact-body variant is the production default, not a
CrowdLab-only preview or a per-level option. `CrowdSolverProfile.Default` is the
single versioned source of body-envelope tuning.

- Physical radius is visual scale × 0.56. Sprite-card padding, ears and weapons
  do not create an invisible body-to-body barrier.
- All GPU horde levels (2, 2.1, 4, 5, 6 and 7) run the same XPBD contact solver
  at 60 Hz with eight position iterations. No legacy movement selector or
  level-specific physics profile remains in the runtime.
- Minimum collision diameter, spawn clearance, navigation wall inset and
  diagnostic contact rings use the calibrated envelope.
- Persistent deep contacts receive stronger unilateral correction after four
  consecutive deep-contact ticks. The persistence gate remains >0.2 seconds.
- Levels 1 and 3 keep individual CPU actors so healing, revival, flying and
  vampire combat behavior are preserved. They now share the same 0.56 body
  radius, with only a 1% numerical contact skin and no old 0.62 m minimum gap.
  Their primitive presentation is narrowed in X/Z to match the physical body;
  character height is unchanged. This is a shared packing standard, not a
  claim that those two levels have been migrated to GPU XPBD.

Wave assets may still vary population, movement speed, mass, spawn cadence and
navigation geometry. They cannot select a different GPU solver quality.
Deprecated `HordePhysicsProfile` data is hidden for serialization compatibility
and is not read by runtime movement.

## GPU horde pipeline

Each horde enemy is an authoritative GPU structured-buffer record rather than a
GameObject or Unity physics body. Compact spawn, damage, status, projectile and
dynamic-target commands are uploaded; targeting results and lifecycle/combat
events return through asynchronous readback. Normal gameplay does not read back
the full agent buffer.

The fixed-tick movement path integrates flow and external impulses, builds a
compact count/scan/scatter contact grid, solves body/blocker/wall constraints,
and resolves velocity from corrected travel. Contact generation and diagnostics
do not use the capped gameplay-query grid. The latter remains for targeting and
render-list queries, with explicit overflow diagnostics.

The flow field rasterizes authored road geometry (including secondary/tertiary
routes and square corners) or an explicitly configured open field. Wall
clearance and exit integration guide the crowd without assigning permanent
lanes. Arena waves use the same solver with their configured movement mode.

Presentation interpolates resolved tick positions. Sprite bodies retain their
visual scale while contact radius excludes transparent card padding. The GPU
culls and compacts visible agents for indirect rendering. Platforms without
compute-shader support report an error rather than silently switching the
horde to a CPU implementation.

## Validation and diagnostics

`CrowdLab.unity` is an isolated interactive solver test scene. It uses explicit
physical disks, shared production kernels, fixed ticks and contact diagnostics.
It is not the playable Main scene.

The accepted compact-body run on 2026-10-04 passes the 18-test focused suite,
captured-contact replay, and the real Level 2 10,000-enemy merge/tail gate.
That real-wave gate reported zero persistent contacts, dropped bodies, grid
failures and walkability violations, with 1.901 m/s mean forward flow.
These results do not imply that every legacy gameplay test passes.

The rollout additionally audits the actual solver configuration of every horde
level and tests individual-actor equal/mixed/small-scale contact envelopes.
See `docs/CrowdRebuildProgress.md` for current results and local evidence paths.
Generated logs and images stay under `Logs/CrowdRebuild/` and are ignored by Git.

## MVP Acceptance

The MVP is considered good enough for the next phase when:

- One continuous wave can be won/lost/restarted.
- Tower placement persists per level.
- The active weapon feels useful but cannot carry the level alone.
- The UI remains readable at common desktop resolutions.
- 1,000+ enemies over a wave can be handled without obvious hitching on a development PC.

## Current Balance Intent

The sample level is tuned as a rough tactical test, not a tutorial. With the temporary 3/3/3 tower availability, a careless first layout should leak enough enemies to lose, while a better layout using overlapping ranges and active weapon timing should be able to recover. Future progression should start with only Archer Tower unlocked, then add tower types and higher limits through permanent upgrades.
