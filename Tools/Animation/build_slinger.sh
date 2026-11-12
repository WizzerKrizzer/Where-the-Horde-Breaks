#!/usr/bin/env bash
# Rebuilds the approved slinger (Meshy "Stonehide Warrior" + Mixamo "Throw" body + overhead whirl,
# variant B spin-down) into Assets/Tests/PlayMode/StyleAssets/Resources/Mixamo/stonehide.fbx.
#
#   Tools/Animation/build_slinger.sh            # build and install
#   NO_INSTALL=1 Tools/Animation/build_slinger.sh   # build into Tools/Animation/work only
#
# Needs Blender 5.2 and a Python with numpy, scipy and Pillow (the TRELLIS venv has them).
# The Mixamo files in Art/Characters/Slinger/Mixamo are not in git (see README.md).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
PY=${PYTHON:-/c/AI/trellis/.venv/Scripts/python.exe}
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
mkdir -p "$HERE/work"
WORK=$(cd "$HERE/work" && pwd -W)
SRC=$(cd "$ROOT/Art/Characters/Slinger" && pwd -W)

"$BLENDER" -b --factory-startup -P "$HERE/meshy/dump_rest.py" -- "$SRC/Meshy/stonehide-warrior.glb" "$WORK/rest.json"
"$BLENDER" -b --factory-startup -P "$HERE/meshy/mix_dump.py" -- "$SRC/Mixamo/mixamo-throw.fbx" "$WORK/mixamo-throw.json"
SPIN_DOWN=0.55 "$PY" "$HERE/meshy/combo.py" "$WORK/rest.json" "$WORK/mixamo-throw.json" "$WORK/throw.json"
"$BLENDER" -b --factory-startup -P "$HERE/meshy/build_throw.py" -- "$SRC/Meshy/stonehide-warrior.glb" "$WORK/throw.json" "$WORK/stonehide.fbx" "$WORK/stonehide.png"

if [ -z "${NO_INSTALL:-}" ]; then
    RES="$ROOT/Assets/Tests/PlayMode/StyleAssets/Resources"
    mkdir -p "$RES/Mixamo"
    # The rig goes to the game's Resources (one copy only; the capture tests load it from there).
    mkdir -p "$ROOT/Assets/Resources/Mixamo"
    rm -f "$RES/Mixamo/stonehide.fbx" "$RES/Mixamo/stonehide.fbx.meta"
    cp "$HERE/work/stonehide.fbx" "$ROOT/Assets/Resources/Mixamo/"
    cp "$HERE/work/stonehide.png" "$RES/"
    "$HERE/install_crew_models.sh"
    echo "Installed stonehide.fbx and stonehide.png"
fi
