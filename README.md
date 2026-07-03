# Where the Horde Breaks

**Where the Horde Breaks** is a PC tower defense roguelite prototype about building a defense before each battle, then surviving relentless waves with permanent progression and an actively aimed weapon.

This repository currently contains an early Unity prototype focused on validating the core loop: build, start wave, fight, earn currencies, retry, and spend upgrades.

## Current Prototype

- Top-down 2.5D tower defense camera with pan and zoom.
- Build phase before each wave.
- Combat phase with tower automation and a mouse-aimed active weapon.
- Fixed enemy path with continuous wave spawning.
- Persistent per-level tower layout.
- Permanent currencies and profile save data.
- Prototype skill tree / upgrade panel.
- Dev wallet for testing currencies, upgrade reset, and test speed.
- Enemy health bars, tower placement preview, range display, and placement error popups.

## Unity Version

Built for:

- Unity `6000.3.19f1` / Unity 6.3 LTS

Open through Unity Hub and allow Unity to regenerate project files if prompted.

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
- `1`, `2`, `3`: select tower type during build phase
- Left mouse during build phase: place selected tower
- `Delete`: remove nearest tower
- `Backspace`: remove all towers
- `Space` / `Enter`: start the wave
- Left mouse during combat: fire active weapon
- `R`: return to build phase / retry with saved tower positions

## Prototype UI

- Bottom active weapon slot shows cooldown and readiness.
- Result panel appears after victory or defeat.
- `Retry` returns to build phase.
- `Upgrades` opens the prototype skill tree.
- `Dev Wallet` is for testing only and lets the developer add currencies, reset upgrades, and test speed.

## Design Direction

The long-term goal is a tower defense / roguelite with huge enemy hordes, permanent progression, and towers evolving visually and mechanically through historical and futuristic eras.

Planned systems include:

- Larger radial/network skill tree.
- Unlockable tower types and tower limits.
- Multiple permanent currencies.
- Level replay rewards and challenge objectives.
- More enemy roles, support units, saboteurs, bosses, and high-density horde optimization.
- Future mobile-port-friendly architecture.

## Status

This is not final gameplay, art, balance, or UI. It is a living prototype used to test the foundations before expanding scope.
