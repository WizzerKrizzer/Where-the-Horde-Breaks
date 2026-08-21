# Where the Horde Breaks

**Where the Horde Breaks** is a PC tower defense roguelite prototype about building a defense before each battle, then surviving relentless waves with permanent progression and an actively aimed weapon.

This repository currently contains an early Unity prototype focused on validating the core loop: build, start wave, fight, earn currencies, retry, spend upgrades, and test increasingly dense enemy flow.

## Current Prototype

- Top-down 2.5D tower defense camera with pan and zoom.
- Build phase before each wave.
- Combat phase with tower automation and a mouse-aimed active weapon.
- Continuous waves with a GPU-authoritative flow-field horde, dynamic blockers, melee/projectile combat, compact events, and indirect culling/rendering; there is no legacy CPU horde mode.
- Persistent per-level tower layout.
- Permanent currencies and profile save data.
- Icon-based permanent skill tree with prerequisite links, delayed hover details, zoom, pan, rank indicators, and upgrade purchasing.
- Dev wallet for testing currencies, upgrade reset, test speed, level loading, save snapshots, and an isolated Best Bot balance run.
- Enemy health bars, tower placement preview, range display, and placement error popups.
- Three prototype levels:
  - Level 1: playable balance target and first progression slice.
  - Level 2: larger split-road map using the shared GPU flow-field horde simulation.
  - Level 3: foundation map with mixed undead/support/flying enemies for future design work.

## Unity Version

Built for:

- Unity `6000.3.19f1` / Unity 6.3 LTS

Open through Unity Hub and allow Unity to regenerate project files if prompted.

The horde runtime requires compute-shader support. It intentionally has no legacy CPU enemy-simulation fallback.

## Running The Project

1. Open Unity Hub.
2. Add this repository folder as a Unity project.
3. Open with Unity 6.3 LTS.
4. Open `Assets/Scenes/Main.unity`.
5. Press Play.

The scene currently creates sample gameplay content at runtime, so there is no final art/content pipeline yet.

## Controls

- `WASD` or right mouse drag: pan camera
- Mouse wheel: zoom toward cursor
- `F3`: toggle the CPU/GPU horde performance and grid-diagnostics panel
- `1`, `2`, `3`: select tower type during build phase
- Left mouse during build phase: place selected tower
- `Delete`: remove nearest tower
- `Backspace`: remove all towers
- `Space` / `Enter`: start the wave
- Left mouse during combat: fire active weapon
- `R`: return to build phase / retry with saved tower positions
- `Tab`: open damage stats
- `G`: open the Breaker's Grimoire
- `` ` ``: open developer tools

Inside the skill tree:

- Hover a node for `0.3` seconds: show its current stats and next-rank preview
- Left click a node: immediately purchase its next rank when unlocked and affordable
- `BUY ALL`: purchase affordable ranks across all unlocked nodes, one rank per node per pass, until no further purchase is possible
- Left mouse drag: pan the tree, including while hovering a node and a small inspection margin when the full tree fits onscreen
- Mouse wheel: zoom the tree, including while hovering a node

## Prototype UI

- Bottom active weapon slot shows cooldown and readiness.
- Result panel appears after victory or defeat.
- `Retry` returns to build phase.
- `Tab` opens the single detailed damage-statistics view; there is no duplicate always-visible damage panel.
- `Upgrades` opens the permanent skill tree. Nodes use compact icons and rank text; compact hover cards show the per-rank increase, the live value before and after purchase, and a centered currency cost. Affordable price numbers are white and unavailable price numbers are red, while currency symbols use a warm orange-yellow accent. Purchases happen directly on the node.
- `Dev Wallet` is for testing only and lets the developer add currencies, reset upgrades, test speed, save/load dev snapshots, switch prototype levels, and run balance automation.
- `BEST BOT: START` runs Level 1 repeatedly at `20x` on a separate fresh profile. Each attempt uses a reproducible spawn seed; after a loss the bot scores affordable upgrades by expected combat value per cost, rebuilds every unlocked tower type using route coverage and spacing, and retries until victory or manual stop. The current Level 1 policy never buys base-life ranks, reserves currency to maximize the repeatable end-of-run essence upgrade, then targets an approximately `60/40` tower-to-active-weapon combat-upgrade spend split.
- The Best Bot restores the real player profile, selected level, layout, random state, auto-active setting, reward multiplier, and time scale when it finishes. Its report includes attempts, simulated and real time, winning seed, lives, kills, damage split, tower count, purchases, currencies, and recent failed runs. `PURCHASES` expands a scrollable attempt-by-attempt purchase history where every purchased rank includes a concise description of its effect.
- The Breaker's Grimoire contains prototype entries for turrets, active weapons, enemies, bosses, and levels.

## Design Direction

The long-term goal is a tower defense / roguelite with huge enemy hordes, permanent progression, and towers evolving visually and mechanically through historical and futuristic eras.

Planned systems include:

- Currency-specific skill-tree node styling and final icon art.
- Unlockable tower types and tower limits.
- Multiple permanent currencies.
- Level replay rewards and challenge objectives.
- More enemy roles, support units, saboteurs, bosses, multi-path maps, and high-density horde optimization.
- Future mobile-port-friendly architecture.

## Status

This is not final gameplay, art, balance, or UI. It is a living prototype used to test the foundations before expanding scope.

## Validation

The project includes EditMode and PlayMode coverage for flow-field construction, dense corner movement, GPU targeting, combat events, overflow handling, and stress scenarios. A Windows Development build is used as the final verification step for completed runtime changes.

Detailed GPU pipeline and simulation notes are available in [`docs/Architecture.md`](docs/Architecture.md).
