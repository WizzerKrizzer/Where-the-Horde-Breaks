#!/usr/bin/env bash
# Builds the tamed mammoth (the Meshy "Crimson Mammoth", widened, rigged and keyed in Blender by
# meshy/build_mammoth.py) into Assets/Resources/Meshy/mammoth.fbx (with the pen's model) with its
# texture. The clips are Run, Scoop, Rage and Death. Needs Blender 5.2.
#
#   Tools/Animation/build_mammoth.sh
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
mkdir -p "$HERE/work/mammoth"
unzip -o -q "$ROOT/Art/Characters/Mammoth/Meshy/"*.zip -d "$HERE/work/mammoth/source"
WORK=$(cd "$HERE/work/mammoth" && pwd -W)
MODEL=$(ls "$HERE"/work/mammoth/source/*/*.fbx | head -1)

"$BLENDER" -b --factory-startup -P "$HERE/meshy/build_mammoth.py" -- \
    "$(cygpath -w "$MODEL")" "$WORK/mammoth.fbx" "$WORK/mammoth-tex.png" "$WORK/mammoth-report.json"

# The game's Resources (ignored by git: Meshy models stay local for now).
RES="$ROOT/Assets/Resources/Meshy"
mkdir -p "$RES"
cp "$HERE/work/mammoth/mammoth.fbx" "$RES/"
cp "$HERE/work/mammoth/mammoth-tex.png" "$RES/"

# The pen (the Meshy "Palisade Camp"), as it comes.
rm -rf "$HERE/work/mammoth/pen"
unzip -o -q "$ROOT/Art/Buildings/MammothPen/Meshy/"*.zip -d "$HERE/work/mammoth/pen"
cp "$HERE"/work/mammoth/pen/*/*.fbx "$RES/mammoth-pen.fbx"
cp "$HERE"/work/mammoth/pen/*/*.png "$RES/mammoth-pen-tex.png"
echo "Installed mammoth.fbx and mammoth-pen.fbx"
