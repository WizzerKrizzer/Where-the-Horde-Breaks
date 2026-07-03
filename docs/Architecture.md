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

## Scaling Path

The first implementation uses pooled/managed GameObject actors where clarity matters most. Horde scaling should happen in stages:

1. Pool enemies and projectiles aggressively.
2. Replace projectile GameObject spam with pooled projectiles or direct batched hits.
3. Move far enemy movement to a batch simulation using path progress values.
4. Render distant enemies with instancing or impostor-style visual groups.
5. Keep nearby enemies as individual actors for readable combat and status effects.

## MVP Acceptance

The MVP is considered good enough for the next phase when:

- One continuous wave can be won/lost/restarted.
- Tower placement persists per level.
- The active weapon feels useful but cannot carry the level alone.
- The UI remains readable at common desktop resolutions.
- 1,000+ enemies over a wave can be handled without obvious hitching on a development PC.

## Current Balance Intent

The sample level is tuned as a rough tactical test, not a tutorial. With the temporary 3/3/3 tower availability, a careless first layout should leak enough enemies to lose, while a better layout using overlapping ranges and active weapon timing should be able to recover. Future progression should start with only Archer Tower unlocked, then add tower types and higher limits through permanent upgrades.
