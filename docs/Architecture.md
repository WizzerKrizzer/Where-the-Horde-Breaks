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

## GPU Horde Pipeline

The scalable horde is data-oriented and GPU-authoritative. Each enemy is one structured-buffer record; it is not a `GameObject`, `CharacterController`, or Unity physics body. The record includes movement, health, armor/resistances, status timers, and melee state. The frame pipeline is ordered as follows:

1. Upload compact lifecycle, damage, status, and projectile commands.
2. Apply dynamic-object lifecycle commands and build their GPU spatial grid.
3. Apply direct damage/status commands, then clear and populate the enemy uniform grid.
4. Resolve projectile segment hits, splash, pierce, area effects, and batched target queries.
5. Integrate flow, blocker selection/melee, soft-disc separation, wall constraints, velocity, and escape state.
6. Resolve blocker damage and append compact health/death events.
7. Cull against the camera, compact visible indices, and write indirect instance counts.
8. Render visible particles directly from GPU state with indirect draws.

Target query results and death/escape/object events use asynchronous readback. Queries are cached for a few frames; damage commands are combined by enemy index before upload. Events are persistent and read back in compact ranges, so gameplay does not stall on a per-tower or per-hit `GetData` call. Normal gameplay never reads back the full enemy-state buffer.

The runtime no longer executes a CPU loop over every enemy and has no CPU horde fallback. Slow, burn, armor/resistance, damage-over-time, melee blocking, splash, pierce, knockback, and slow-aura capacity are processed by compute kernels against GPU spatial grids. Barricades and allied blockers use generation-tagged GPU slots, so stale build/sell/destroy commands and delayed events cannot affect a reused object. CPU receives only selected target records, compact gameplay events, and diagnostics.

`BuildGrid` also compacts living slots into an active-index buffer and writes indirect compute arguments. Movement and culling dispatch only the active list, so terminal slots no longer consume simulation threads. Visible agents are split into near and far lists: the near list uses the detailed sphere mesh, while the far list uses an eight-triangle octahedron in a second indirect draw.

The fixed grid still stores at most 48 neighbor references per cell. When occupancy exceeds that limit, every agent continues to simulate, but collision sampling becomes incomplete. An emergency overflow path therefore reduces the local collision constraint, applies a density-gradient pressure toward less crowded cells, and adds a small stable per-agent scatter term. The F3 panel reports the overflow and displays an explicit warning while this fallback is active. This is a safety mechanism, not a replacement for a future sorted/prefix-sum grid.

Off-camera simulation uses distance-based update strides of 1, 2, or 4. A skipped far particle stays in GPU memory and catches up with a proportionally larger integration step; nearby enemies always run at full fidelity. Hardware without compute-shader support cannot start a horde wave and reports a clear error instead of silently changing behavior.

## Horde Movement Prototype

`HordeEnemyManager` does not use path distance plus lane offsets for movement. At wave start it rasterizes the union of the level's primary and secondary road polylines into a walkable grid, computes wall clearance, and integrates a shared cost field back from the exit. A harmonic pressure potential between full-width entrance and exit cross-sections supplies the movement vectors, preventing shortest-path streamlines from collapsing at bends. Each enemy samples a smoothly interpolated local direction, retains velocity, slides against non-walkable cells, and applies short-range body separation from the existing spatial hash.

The field adds a soft cost beside walls so the preferred direction does not hug inside corners. Separation remains a local pressure term rather than a lane assignment, allowing dense groups to occupy the full road width without creating persistent artificial formations. `pathDistances` in the manager is now derived from flow-field cost and is retained only as a targeting score for First/Last tower modes.

The flow-field stores wall clearance, exit integration cost, harmonic width potential, and a walkable flag per cell. GPU movement combines the local vector with penetration-only separation and wall-gradient pressure. This intentionally behaves like compressible soft discs rather than exact rigid-body physics.

Dense movement also applies a smoothed occupancy gradient every frame, projected primarily across the local flow. This fills empty road width without persistent lane offsets. Separation is stronger laterally than longitudinally, uses each pair's visual radii instead of one identical diameter, and therefore avoids crystallizing the horde into a uniform lattice. Wall collisions first remove the outward normal component of displacement, producing tangent sliding on diagonal corners before falling back to axis constraints. Wall pressure is prevented from applying a backward component against the flow.

## Validation and Diagnostics

The F3 performance panel identifies the GPU compute backend, reports CPU/GPU frame timing, active/drawn counts, and uniform-grid maximum occupancy, overflow cells, and dropped entries. Frame timing is workload timing, not total operating-system GPU utilization.

PlayMode coverage includes multi-corner width preservation, walkability and exit completion, all four targeting modes, flying filters, damage/death/escape events, intentional spatial-grid overflow, and indirect culling. The explicit `GpuHordeStressBenchmarkTests` scenario measures 1k, 5k, 10k, 25k, 50k, and 100k agents and writes `gpu-horde-benchmark.json` under `Application.persistentDataPath`. Benchmark numbers are synthetic engineering measurements and are not a minimum hardware guarantee.

Latest reference run (RTX 5070 Ti, Direct3D 12, 2026-08-18):

| Agents | CPU submit | GPU/sync frame | Max cell | Overflow / dropped |
| ---: | ---: | ---: | ---: | ---: |
| 1,000 | 0.033 ms | 0.188 ms | 1 | 0 / 0 |
| 5,000 | 0.037 ms | 0.122 ms | 1 | 0 / 0 |
| 10,000 | 0.042 ms | 0.122 ms | 1 | 0 / 0 |
| 25,000 | 0.061 ms | 0.130 ms | 1 | 0 / 0 |
| 50,000 | 0.086 ms | 0.347 ms | 1 | 0 / 0 |
| 100,000 | 0.143 ms | 0.535 ms | 2 | 0 / 0 |

In headless/batch runs where Unity does not expose `FrameTimingManager.gpuFrameTime`, the GPU column is the synchronous fence-wait proxy sampled every ten measured frames. It is suitable for regressions on the same machine, not cross-machine marketing comparisons.

## MVP Acceptance

The MVP is considered good enough for the next phase when:

- One continuous wave can be won/lost/restarted.
- Tower placement persists per level.
- The active weapon feels useful but cannot carry the level alone.
- The UI remains readable at common desktop resolutions.
- 1,000+ enemies over a wave can be handled without obvious hitching on a development PC.

## Current Balance Intent

The sample level is tuned as a rough tactical test, not a tutorial. With the temporary 3/3/3 tower availability, a careless first layout should leak enough enemies to lose, while a better layout using overlapping ranges and active weapon timing should be able to recover. Future progression should start with only Archer Tower unlocked, then add tower types and higher limits through permanent upgrades.
