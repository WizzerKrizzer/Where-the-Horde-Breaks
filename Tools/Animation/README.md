# Character animation tools

These scripts build the slinger's sling throw outside Unity, with Python and Blender. The approved
result is `Assets/Tests/PlayMode/StyleAssets/Resources/stonehide.fbx`. It is the Meshy "Stonehide
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
to `Logs/StyleDefenders/meshy-warrior`.

## Source files

- `stonehide-warrior.glb` (in git): the Meshy model with its rig, textured, with Walking and
  Running clips. Meshy output belongs to its creator.

The Mixamo clips below are ignored because the repository is public and Adobe's terms do not
allow redistributing raw Mixamo files. Download them again from Mixamo or keep a copy elsewhere.

- `mixamo-throw.fbx`: the Mixamo "Throw" clip, without skin, at 30 fps, with no keyframe
  reduction. The slinger's body comes from this clip.
- `mixamo-throw-object.fbx`, `mixamo-shooting-arrow.fbx`, `mixamo-goalie-throw.fbx`,
  `mixamo-throw-in.fbx`: kept for future towers (boulder thrower, archers).

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

## Older pipelines

- `rig/`: the first slinger. Its own 13-bone numpy rig was baked to a vertex animation texture
  (`StyleSlingerVat.shader`, test `RenderVatSlinger`).
- `trellis-bake/`: Blender scripts that cleaned TRELLIS image-to-3D meshes into flat-coloured
  low-poly bakes. They were used for the TRELLIS slinger and the tipi.
