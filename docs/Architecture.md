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

`BuildGrid` scans only the spawned high-water range, compacts living slots into an active-index buffer, and writes indirect compute arguments. Movement and culling dispatch only the active list, so unspawned capacity and terminal slots no longer consume simulation threads. Visible agents are split into near and far lists: the near list uses the detailed sphere mesh, while the far list uses an eight-triangle octahedron in a second indirect draw.

The movement kernel reads neighbour position and radius from a compact 16-byte spatial record instead of fetching the complete 120-byte movement/combat record for every candidate. It also rejects grid cells whose bounds cannot intersect the collision radius. The complete state remains authoritative and ping-ponged on the GPU because movement itself uses health, status, slow, burn, and melee fields every step; a full hot/cold split would duplicate synchronization without removing those self-state accesses.

The fixed grid stores at most 96 neighbour references per cell. When occupancy exceeds that limit, every agent continues to simulate, but collision sampling becomes incomplete. An emergency overflow path therefore reduces the local collision constraint, applies a density-gradient pressure toward less crowded cells, and adds a small stable per-agent scatter term. The F3 panel reports the raw maximum occupancy, overflowing cells, and dropped neighbour references. This is a safety mechanism, not a replacement for a future sorted/prefix-sum grid if production maps routinely exceed 96 agents in one 0.62 m cell.

Projectile and area-effect commands are uploaded once and consumed sequentially by one compute dispatch per command batch. This preserves command ordering and avoids concurrent read/modify/write races when several effects hit the same enemy, while removing up to 255 CPU dispatch calls per batch. Direct damage and status changes remain compact dirty-command uploads. The normal GPU fast path passes no complete controls or impulses arrays; compatibility uploads are limited to the spawned high-water range.

Enemy creation is also range-batched. A rapid spawn window prepares new records in the manager's preallocated arrays, then uploads the contiguous state range to both ping-pong buffers and its controls buffer once. Level 5 uses this path to introduce 100,000 enemies in large bursts without issuing several GPU uploads per enemy.

Off-camera simulation uses distance-based update strides of 1, 2, or 4. A skipped far particle stays in GPU memory and catches up with a proportionally larger integration step; nearby enemies always run at full fidelity. Hardware without compute-shader support cannot start a horde wave and reports a clear error instead of silently changing behavior.

## Horde Movement Prototype

`HordeEnemyManager` does not use path distance plus lane offsets for movement. At wave start it rasterizes the union of the level's primary and secondary road polylines into a walkable grid, computes wall clearance, and integrates a shared cost field back from the exit. A harmonic pressure potential between full-width entrance and exit cross-sections supplies the movement vectors, preventing shortest-path streamlines from collapsing at bends. Each enemy samples a smoothly interpolated local direction, retains velocity, slides against non-walkable cells, and applies short-range body separation from the existing spatial hash.

The field adds a soft cost beside walls so the preferred direction does not hug inside corners. Separation remains a local pressure term rather than a lane assignment, allowing dense groups to occupy the full road width without creating persistent artificial formations. `pathDistances` in the manager is now derived from flow-field cost and is retained only as a targeting score for First/Last tower modes.

The flow-field stores wall clearance, exit integration cost, harmonic width potential, and a walkable flag per cell. GPU movement combines the local vector with penetration-only separation and wall-gradient pressure. This intentionally behaves like compressible soft discs rather than exact rigid-body physics.

Dense movement also applies a smoothed occupancy gradient every frame, projected primarily across the local flow. This fills empty road width without persistent lane offsets. Separation is stronger laterally than longitudinally, uses each pair's visual radii instead of one identical diameter, and therefore avoids crystallizing the horde into a uniform lattice. Wall collisions first remove the outward normal component of displacement, producing tangent sliding on diagonal corners before falling back to axis constraints. Wall pressure is prevented from applying a backward component against the flow.

## Validation and Diagnostics

The F3 performance panel identifies the GPU compute backend, reports CPU/GPU frame timing, active/drawn counts, and uniform-grid maximum occupancy, overflow cells, and dropped entries. Frame timing is workload timing, not total operating-system GPU utilization.

PlayMode coverage includes multi-corner width preservation, walkability and exit completion, all four targeting modes, flying filters, damage/death/escape events, multi-command projectile and area batches, intentional spatial-grid overflow, and indirect culling. The explicit `GpuHordeStressBenchmarkTests` scenario measures 1k, 5k, 10k, 25k, 50k, and 100k densely seeded agents and writes `gpu-horde-detailed-benchmark.json` under `Application.persistentDataPath`. It records CPU dispatch/draw submission, complete-upload cost, 256-command batch submission, allocations, average/p95/max frame stability, state stride/residency, and grid diagnostics.

Latest like-for-like reference run (RTX 5070 Ti, Direct3D 12, Unity 6000.3.19f1, 2026-08-22):

| Agents | Frame avg before → after | Frame p95 before → after | Dropped before → after |
| ---: | ---: | ---: | ---: |
| 1,000 | 0.345 → 0.228 ms | 0.765 → 0.765 ms | 65 → 0 |
| 5,000 | 0.431 → 0.278 ms | 0.802 → 0.575 ms | 96 → 1 |
| 10,000 | 0.451 → 0.329 ms | 0.780 → 0.716 ms | 86 → 0 |
| 25,000 | 0.503 → 0.392 ms | 0.843 → 0.764 ms | 78 → 1 |
| 50,000 | 0.945 → 0.497 ms | 1.332 → 0.971 ms | 83 → 0 |
| 100,000 | 2.741 → 1.132 ms | 3.174 → 1.812 ms | 90 → 1 |

At 100K, median CPU submission for 256 queued projectiles improved from 0.398 to 0.054 ms, and 256 area effects improved from 0.382 to 0.062 ms. Fast-path dispatch plus both indirect draw submissions is approximately 0.050 ms; uploading complete controls and impulses costs approximately 0.170 ms and is therefore retained only as a compatibility/test path. The benchmark observed zero managed allocations per measured frame. The two 120-byte state buffers occupy 22.89 MiB at 100K, with an additional 1.53 MiB compact spatial buffer; neighbour-state fetch width is reduced from 120 to 16 bytes.

Unity returned no valid `FrameTimingManager.gpuFrameTime` samples in this batch DX12 run, so the table deliberately reports measured frame throughput rather than inventing a GPU-only number. CPU submission is measured separately and is much smaller, making the throughput value useful for same-machine regressions but not a substitute for a RenderDoc/Nsight hardware-counter capture. The F3 panel can report GPU frame timing during an interactive player run when the driver exposes it.

The production recommendation is a 50K active-enemy soft limit for a typical discrete desktop GPU, with 25K as a conservative target for integrated/handheld-class hardware. 100K is a validated high-end stress capacity, not the default content budget. If a real level reports persistent overflow or more than a handful of dropped references, widen the occupied road region or move to a sorted/prefix-sum grid before raising the 96-entry cell limit again.

## MVP Acceptance

The MVP is considered good enough for the next phase when:

- One continuous wave can be won/lost/restarted.
- Tower placement persists per level.
- The active weapon feels useful but cannot carry the level alone.
- The UI remains readable at common desktop resolutions.
- 1,000+ enemies over a wave can be handled without obvious hitching on a development PC.

## Current Balance Intent

The sample level is tuned as a rough tactical test, not a tutorial. With the temporary 3/3/3 tower availability, a careless first layout should leak enough enemies to lose, while a better layout using overlapping ranges and active weapon timing should be able to recover. Future progression should start with only Archer Tower unlocked, then add tower types and higher limits through permanent upgrades.
