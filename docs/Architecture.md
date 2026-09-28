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

The fixed grid stores at most 96 neighbour references per cell. When occupancy exceeds that limit, every agent continues to simulate, but collision sampling becomes incomplete. An emergency overflow path applies a density-gradient pressure toward less crowded cells and adds a small stable per-agent scatter term without shrinking enemy contact diameters. The F3 panel reports the raw maximum occupancy, overflowing cells, and dropped neighbour references. This is a safety mechanism, not a replacement for a future sorted/prefix-sum grid if production maps routinely exceed 96 agents in one 0.62 m cell.

Projectile and area-effect commands are uploaded once and consumed sequentially by one compute dispatch per command batch. This preserves command ordering and avoids concurrent read/modify/write races when several effects hit the same enemy, while removing up to 255 CPU dispatch calls per batch. Direct damage and status changes remain compact dirty-command uploads. The normal GPU fast path passes no complete controls or impulses arrays; compatibility uploads are limited to the spawned high-water range.

Enemy creation is range-batched once per render frame. New records and controls upload to separate read-only staging buffers, then `ApplySpawns` copies the range into both state buffers and live controls on the GPU. This avoids CPU writes to resources used by movement and rendering. Visibility owns the indirect instance counts; spawning does not overwrite those counts with an unculled high-water mark. The staging buffers are allocated once at wave capacity and disposed with the simulation.

Presentation interpolates the previous and resolved positions between simulation ticks. Animated enemies keep resolved locomotion speed and a presentation clock separate from collision velocity: positional contact corrections do not become next-tick drive impulses. Resolved travel drives stepping, including side pushes; low speed smoothly blends to a planted brace with upper-body breathing. The brace clock uses simulation time, so pause and slow motion remain consistent.

Free queues match the leading body's predicted acceleration in the current dispatch, rather than inheriting its previous tick's speed one row at a time. Only aligned flows use this follower constraint; crossing flows do not become stationary leaders. Barrier-linked queues instead use the leading body's actual resolved forward travel and suppress soft sideways pressure while held. Removing the remembered barrier enables the ready drive in the same tick.

Animated goblins use the same eight-percent gap outside the hard body diameter for movement, parallel contact projection and final cleanup. Free bodies average complementary contact corrections; alternating full pair ownership and an extra backward queue push caused visible jitter around turns. After the parallel contact passes, a colored-cell GPU pass updates bodies sequentially inside each selected cell. Moving cells are separated by more than a body plus two maximum corrections, so neighbouring bodies remain stationary during the in-place projection. Cell membership stays fixed for each complete color cycle while neighbour positions are read live. This final silhouette cleanup runs once on the presented tick when a render frame catches up multiple simulation ticks. Diagnostics continue to measure the hard body diameter. Grid construction carries excluded records into the other ping-pong buffer, preventing retired bodies from returning when active-only kernels swap buffers.

Movement uses a fixed 60 Hz GPU tick independent of render frame rate and developer time scale. Camera distance never changes simulation frequency, so zooming out or switching between 1x and 10x cannot select a different crowd trajectory. Rendering remains split into near and far indirect lists entirely on the GPU, but both lists use the same rounded 120-triangle mesh, footprint, and lighting. This removes the moving LOD seam across a dense crowd while providing a smoother silhouette at a moderate geometry cost. Hardware without compute-shader support cannot start a horde wave and reports a clear error instead of silently changing behavior.

Rare visual variants are derived directly from the stable GPU agent index and require no extra state or upload. Every 1,000th spawned enemy is rendered purple in both LOD lists.

## Horde Movement Prototype

`HordeEnemyManager` does not use path distance plus lane offsets for movement. At wave start it rasterizes the union of the level's primary and secondary road polylines into a walkable grid, computes wall clearance, and integrates a shared cost field back from the exit. A harmonic pressure potential between full-width entrance and exit cross-sections supplies the movement vectors, preventing shortest-path streamlines from collapsing at bends. Each enemy samples a smoothly interpolated local direction, retains velocity, slides against non-walkable cells, and applies short-range body separation from the existing spatial hash.

The field adds a soft cost beside walls so the preferred direction does not hug inside corners. Separation remains a local pressure term rather than a lane assignment, allowing dense groups to occupy the full road width without creating persistent artificial formations. `pathDistances` in the manager is now derived from flow-field cost and is retained only as a targeting score for First/Last tower modes.

The flow-field stores wall clearance, exit integration cost, harmonic width potential, and a walkable flag per cell. If harmonic relaxation produces a locally flat cell, field generation falls back to the descending integration-cost neighbour so the GPU never receives a zero direction inside a reachable corridor. GPU movement combines the local vector with penetration-only separation, wall-gradient pressure, and a bounded post-integration contact projection. The projection prevents visible body intersection while retaining enough compliance for stable massively parallel simulation.

Dense movement also applies a smoothed occupancy gradient every frame, projected primarily across the local flow. This fills empty road width without persistent lane offsets. Animated goblins use soft separation only sideways; hard contacts use each pair's normalized body radius. Positional contact correction is bounded, and ordinary crowd velocity stays inside a broad forward cone; explicit knockback bypasses that cone. This prevents dense cells from becoming sideways avalanches while preserving gradual width filling and hard contact. Wall collisions first remove the outward normal component of displacement, producing tangent sliding on diagonal corners before falling back to axis constraints. Wall pressure is prevented from applying a backward component against the flow. The 100K Level 5 stress scenario uses a per-level 54-meter road and widely separated turns so its physical carrying capacity matches the test population instead of forcing tens of thousands of discs into a normal gameplay corridor.

## Validation and Diagnostics

The F3 performance panel identifies the GPU compute backend, reports CPU/GPU frame timing, active/drawn counts, and uniform-grid maximum occupancy, overflow cells, and dropped entries. Frame timing is workload timing, not total operating-system GPU utilization.

PlayMode coverage includes multi-corner width preservation, walkability and exit completion, all four targeting modes, flying filters, damage/death/escape events, multi-command projectile and area batches, intentional spatial-grid overflow, and indirect culling. The explicit `GpuHordeStressBenchmarkTests` scenario measures 1k, 5k, 10k, 25k, 50k, and 100k densely seeded agents and writes `gpu-horde-detailed-benchmark.json` under `Application.persistentDataPath`. It records CPU dispatch/draw submission, complete-upload cost, 256-command batch submission, allocations, average/p95/max frame stability, state stride/residency, and grid diagnostics.

Latest like-for-like reference run (RTX 5070 Ti, Direct3D 12, Unity 6000.3.19f1, 2026-08-22):

| Agents | Frame avg before → after | Frame p95 before → after | Dropped before → after |
| ---: | ---: | ---: | ---: |
| 1,000 | 0.345 → 0.210 ms | 0.765 → 0.270 ms | 65 → 0 |
| 5,000 | 0.431 → 0.200 ms | 0.802 → 0.260 ms | 96 → 0 |
| 10,000 | 0.451 → 0.240 ms | 0.780 → 0.510 ms | 86 → 0 |
| 25,000 | 0.503 → 0.290 ms | 0.843 → 0.460 ms | 78 → 0 |
| 50,000 | 0.945 → 0.430 ms | 1.332 → 0.970 ms | 83 → 0 |
| 100,000 | 2.741 → 1.030 ms | 3.174 → 2.070 ms | 90 → 1 |

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
