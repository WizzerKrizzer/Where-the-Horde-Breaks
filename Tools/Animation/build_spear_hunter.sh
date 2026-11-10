#!/usr/bin/env bash
# Builds the spear hunter (Meshy model rigged in Mixamo + Mixamo "Breathing Idle" + the Kevin
# Iglesias draw, aim and spear throw, retargeted) into
# Assets/Tests/PlayMode/StyleAssets/Resources/Mixamo/spearhunter.fbx, with its texture and the
# rock beside it. The spears are thrown by hand; the atlatl model is not used.
#
#   Tools/Animation/build_spear_hunter.sh              # mirrored model (default), build and install
#   MIRROR=0 Tools/Animation/build_spear_hunter.sh     # the model as Meshy made it
#
# Needs Blender 5.2. The Mixamo and Kevin Iglesias files are not in git (see README.md).
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
ART="$ROOT/Art"
HUNTER="$ART/Characters/SpearHunter"
mkdir -p "$HERE/work/spearhunter"
WORK=$(cd "$HERE/work/spearhunter" && pwd -W)
W() { cygpath -w "$1"; }

"$BLENDER" -b --factory-startup -P "$HERE/meshy/build_hunter.py" -- \
    "$(W "$HUNTER/Mixamo/Idle.fbx")" "$(W "$HUNTER/Mixamo/Breathing Idle.fbx")" "$(W "$HUNTER/KevinIglesias")" \
    "$WORK/spearhunter.fbx" "$WORK/spearhunter-tex.png" "${MIRROR:-1}"

unzip -o -q "$ART/Buildings/SpearRock/Meshy/"*.zip -d "$HERE/work/spearhunter/rock"

RES="$ROOT/Assets/Tests/PlayMode/StyleAssets/Resources"
mkdir -p "$RES/Mixamo"
cp "$HERE/work/spearhunter/spearhunter.fbx" "$RES/Mixamo/"
cp "$HERE/work/spearhunter/spearhunter-tex.png" "$RES/"
cp "$HERE"/work/spearhunter/rock/*/*.fbx "$RES/spearrock.fbx"
cp "$HERE"/work/spearhunter/rock/*/*.png "$RES/spearrock-tex.png"
echo "Installed spearhunter.fbx and spearrock.fbx"
