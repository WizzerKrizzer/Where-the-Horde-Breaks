# Character animation tools

These scripts build the slinger's sling throw outside Unity, with Python and Blender. The approved
result is `Assets/Tests/PlayMode/StyleAssets/Resources/Mixamo/stonehide.fbx`. It is the Meshy "Stonehide
Warrior" with a `Throw` clip and a sling: cords and a pouch on extra bones, plus a `SlingStone`
bone that is hidden after the release.

To rebuild it:

```bash
Tools/Animation/build_slinger.sh
```

To build into `Tools/Animation/work/` without installing:

```bash
NO_INSTALL=1 Tools/Animation/build_slinger.sh
```

The build needs Blender 5.2 and a Python with numpy, scipy and Pillow. By default it uses the
TRELLIS venv at `C:/AI/trellis/.venv`. Set the `BLENDER` and `PYTHON` environment variables to
use others. To see the result, run the Unity PlayMode test `RenderMeshyWarrior`. Its renders go
to `Logs/Captures/StyleDefenders/meshy-warrior`.

## Source files

All source art (Gemini concepts, Meshy models, Mixamo clips) lives in the root `Art/` folder,
one folder per item (`Art/Characters/Slinger/`, `Art/Buildings/Cave/`, ...), each with `Gemini/`,
`Meshy/` and, for characters, `Mixamo/` subfolders.

- `Art/Characters/Slinger/Meshy/stonehide-warrior.glb` (in git): the Meshy model with its rig,
  textured, with Walking and Running clips. Model created with Meshy.

## Mixamo files (not in git)

The repository is public, and Adobe's terms do not allow redistributing Mixamo animations as
files. Anything made with them goes in a folder named `Mixamo`, which the root `.gitignore`
skips. That includes the built slinger, `Resources/Mixamo/stonehide.fbx`, because its body plays
the Mixamo "Throw". Keep a copy elsewhere, or download the clips again and rebuild. Without the
FBX, the `RenderMeshyWarrior` test is skipped.

The clips go in `Art/Characters/Slinger/Mixamo/`:

- `mixamo-throw.fbx`: the Mixamo "Throw" clip, without skin, at 30 fps, with no keyframe
  reduction. The slinger's body comes from this clip.
- `mixamo-throw-object.fbx`, `mixamo-shooting-arrow.fbx`, `mixamo-goalie-throw.fbx`,
  `mixamo-throw-in.fbx`: kept for future towers (boulder thrower, archers).

## The spear hunter

`build_spear_hunter.sh` runs `meshy/build_hunter.py` and installs `Resources/Mixamo/spearhunter.fbx`,
its texture and the rock. The test `SpearHunterCaptureTests.RecordSpearHunter` renders
them to `Logs/Captures/SpearHunter`.

- The model is the Meshy hunter rigged in Mixamo (`Art/Characters/SpearHunter/Mixamo/Idle.fbx`,
  with skin). By default it is mirrored, so the spear bundle is over the right shoulder where the
  right hand reaches; `MIRROR=0` keeps it as made.
- The spears and the hides are taken off the arm bones, so they no longer stretch when an arm
  moves.
- `Idle` is the Mixamo "Breathing Idle". `Draw`, `Aim` and `Throw` are Kevin Iglesias's
  `UnsheatheBack01_R`, `ThrowSpear01_R - Hold` and `ThrowSpear01_R`, retargeted by world
  rotation change. `Cycle` joins draw, a short aim and the throw. Its release marker (frame 35 at 30 fps)
  is in the build report; the capture test finds the grab from the bundle bones.
- A `RightHandProp` bone follows the source hand prop, and the spear is drawn in that hand. The
  spears are thrown by hand; the Meshy atlatl did not fit and is not used.
- `SpearBundle` and `SpearBundleTip` bones (on `Spine2`) run along the bundle on the back. The
  hand reaches back empty, takes a spear where it comes closest to the bundle's top, and draws it
  up along the bundle's line. Then the spear turns straight into the aim's grip. The source prop
  rolls tip-down in between, like a sword's draw, so its axis is used only once the aim settles.

The Kevin Iglesias files are under the Unity Asset Store EULA: they may be used in the game but
not redistributed, so `Art/Characters/SpearHunter/KevinIglesias/` is ignored by git and the built
FBX is in a `Mixamo` folder.

## The cavemen and their chief

`build_caveman.sh` runs `meshy/build_mixamo_unit.py` twice and installs the game's models,
`Assets/Resources/Mixamo/caveman.fbx` and `caveman-chief.fbx` with their textures (not in git), and
the Meshy cave for `CavemanCaptureTests`. In the game, `CavemanVisual` replaces a caveman's block
with the model; without the files the block stays. `SpecialEffectsCaptureTests.RecordCavemenInGame`
records them in the game at 4, 11 and 40 m.

- The models are the Meshy "Bearclaw Berserker" (caveman) and "Fanghide Chieftain" (chief), rigged
  in Mixamo. Each comes from one clip downloaded with skin.
- Every clip is retargeted by world rotation change, whichever of the two (or the spear hunter) it
  was downloaded for. The hips stay over the spot; walks report their ground speed, and the game
  plays them at the rate that matches how fast the unit moves.
- Weight that hides, the bear pelt and the chief's cloak took from the upper arm or the thigh moves to
  the shoulder or the hips, so they no longer stretch when a limb swings.
- Clips: Idle (the spear hunter's breathing), AttackDown, AttackSide, AttackBack, Walk, WalkB,
  WalkHeavy, Hit, Death; the caveman also has ShieldIdle, ShieldBlock and PowerUp, the chief
  Battlecry. `Fight` is a loop for the capture test.
- The props are built in code (`UnitProps`): the big bone, the chief's tusk, the hide shield on the
  left forearm and, with the torches, a torch lashed to the shield. The flame is
  `StyleFlame.shader` (`FlameVisual`).
- In the game a swing is timed so the blow lands when the unit deals its damage; with the torch every
  other blow is the shield thrust. Hurt, a caveman flinches, or blocks with the shield. The chief roars
  when a fight starts. A dead caveman plays his death, then sinks away.

## The Spear Throwers and the Slingers in the game

`install_crew_models.sh` puts the spear hunter's and the slinger's rigs in `Resources/Mixamo/`, and the
rock, the textures and the approved one-deck tipi (`slinger-tower.bytes`, from `meshy-tipi.txt`) in
`Resources/Meshy/`; both folders are git-ignored. `build_spear_hunter.sh` and `build_slinger.sh` run it
after a build. `TowerCrewVisual` stands three hunters on the rock and eight slingers on the tipi's deck;
the tower starts a throw when it has a target and its shot leaves the thrower's hand at the release.

## The cave

`install_cave.sh` copies the Meshy "Primal Cave Dwelling" as it comes into `Resources/Meshy/cave.fbx`
and its texture. `CaveVisual` puts it beside the road in place of the cave tower's greybox, with its
mouth to the road, and the cavemen come out of its mouth.

## The mammoth

`build_mammoth.sh` runs `meshy/build_mammoth.py` and installs `Resources/mammoth.fbx` and its
texture. The test `MammothCaptureTests.RecordMammoth` renders them to `Logs/Captures/Mammoth`. Mixamo
cannot rig four-legged animals, so the rig and the clips are made in the script:

- The Meshy "Crimson Mammoth" is widened 1.375 times across, because it was too narrow from above.
- It gets a small skeleton: a spine, a neck and a head, a three-bone trunk, a tail, and three
  bones per leg. Skin weights are set by region: the tusks ride on the head, the trunk is shared
  out by height down its bones, and each leg's bones own the leg.
- `Run` (16 frames at 30 fps) places the feet by IK on a stance and swing path and bakes them, so a
  foot stays put while it is on the ground. It covers 0.65 model units per cycle.
- `Scoop` is two scoops of 24 frames, matching `BeastCharge`'s 0.8 s stomp. The head dips, then
  swings the tusks up and to one side; the blow lands at frames 6 and 30.
- `Rage` (36 frames) is a head shake with the trunk up and two stamps. `Death` (54 frames) buckles
  the front legs and rolls the mammoth onto its side.

The Meshy model is not in git for now, so neither is the built FBX.

## How the throw is made

Each throw cycle is 2.6 s, and the stone is released at 1.45 s.

1. `meshy/dump_rest.py` runs in Blender and writes the Meshy rest skeleton to JSON.
2. `meshy/mix_dump.py` runs in Blender and writes a Mixamo clip as world-space bone matrices.
3. `meshy/combo.py` does the main work:
   - It retargets the Mixamo body onto the Meshy bones. Each bone's rest direction is first
     swung onto the Mixamo rest direction, then the Mixamo rotation change is applied.
   - It time-warps the Mixamo clip so its throw lands on our release.
   - It blends in our keyed arms from `throw2.py`: the right arm during the lift and the
     overhead whirl, and the left arm during the reload.
4. `meshy/throw2.py` holds the keyed whirl throw itself:
   - The sling makes two flat turns over the head.
   - After the release it spins down and droops in front of the body (`SPIN_DOWN=0.55` is
     variant B).
   - The arm IK swings from a forward reference, so the twist never flips.
   - The wrist turns along the cords, within 80°, so the cords leave the fist instead of
     passing through the arm.
   - The sling is a Verlet rope, keyed while it whirls and simulated otherwise.
5. `meshy/retarget.py` has the shared skeleton and sling helpers. It started as a retarget of
   the older hand-keyed `rig/rig.py` throw.
6. `meshy/build_throw.py` runs in Blender. It adds the sling bones and the cord mesh, keys the
   `Throw` action and exports the FBX and the texture.

## Checks and previews

These scripts render without Unity, using a small painter's-algorithm renderer in `rig/preview.py`.

- `meshy/mix_retarget.py` and `meshy/mix_apply.py` retarget any Mixamo clip onto the warrior
  and pose it in Blender.
- `meshy/pose_video.py` and `meshy/show_preview.py` render quick frames from those poses.
- `meshy/check_body.py` reports whether the sling passes behind or through the body after the
  release.
- `meshy/check_arm.py` reports whether the cords pass through the right forearm.
- `meshy/footing.py` finds where slingers can stand on a tower so that their feet stay on the
  planks through the whole throw, whichever way they turn: not over an edge, not on a tusk. It
  reads the foot track that `RenderMeshyWarrior` writes to `feet-track.txt`. The eight seats on
  the tipi were picked with it.

## Older pipelines

- `rig/`: the first slinger. Its own 13-bone numpy rig was baked to a vertex animation texture
  (`StyleSlingerVat.shader`, test `RenderVatSlinger`).
- `trellis-bake/`: Blender scripts that cleaned TRELLIS image-to-3D meshes into flat-coloured
  low-poly bakes. They were used for the TRELLIS slinger and the tipi.
