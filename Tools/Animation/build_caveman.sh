#!/usr/bin/env bash
# Builds the caveman (Meshy model rigged in Mixamo, three Mixamo club swings, and the spear
# hunter's Mixamo "Breathing Idle" retargeted onto him) into
# Assets/Tests/PlayMode/StyleAssets/Resources/Mixamo/caveman.fbx with its texture, and installs the
# Meshy cave beside it.
#
#   Tools/Animation/build_caveman.sh
#
# Needs Blender 5.2. The Mixamo files are not in git (see README.md).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
ART="$ROOT/Art"
CAVEMAN="$ART/Characters/Caveman/Mixamo"
mkdir -p "$HERE/work/caveman"
WORK=$(cd "$HERE/work/caveman" && pwd -W)
W() { cygpath -w "$1"; }

"$BLENDER" -b --factory-startup -P "$HERE/meshy/build_caveman.py" -- \
    "$(W "$CAVEMAN/Standing Melee Attack Downward.fbx")" "$(W "$CAVEMAN")" \
    "$(W "$ART/Characters/SpearHunter/Mixamo/Breathing Idle.fbx")" "$WORK/caveman.fbx" "$WORK/caveman-tex.png"

unzip -o -q "$ART/Buildings/Cave/Meshy/"*.zip -d "$HERE/work/caveman/cave"

RES="$ROOT/Assets/Tests/PlayMode/StyleAssets/Resources"
mkdir -p "$RES/Mixamo"
cp "$HERE/work/caveman/caveman.fbx" "$RES/Mixamo/"
cp "$HERE/work/caveman/caveman-tex.png" "$RES/"
cp "$HERE"/work/caveman/cave/*/*.fbx "$RES/cave.fbx"
cp "$HERE"/work/caveman/cave/*/*.png "$RES/cave-tex.png"
# The capture test reads the cave's vertices, so Unity must keep them readable.
[ -f "$RES/cave.fbx.meta" ] && sed -i 's/isReadable: 0/isReadable: 1/' "$RES/cave.fbx.meta"
echo "Installed caveman.fbx and cave.fbx"
