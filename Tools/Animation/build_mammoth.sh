#!/usr/bin/env bash
# Builds the tamed mammoth (the Meshy "Crimson Mammoth", widened, rigged and keyed in Blender by
# meshy/build_mammoth.py) into Assets/Tests/PlayMode/StyleAssets/Resources/mammoth.fbx with its
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

RES="$ROOT/Assets/Tests/PlayMode/StyleAssets/Resources"
cp "$HERE/work/mammoth/mammoth.fbx" "$RES/"
cp "$HERE/work/mammoth/mammoth-tex.png" "$RES/"
echo "Installed mammoth.fbx"
