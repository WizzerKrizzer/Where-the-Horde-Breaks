#!/usr/bin/env bash
# Builds the cavemen and their chief with meshy/build_mixamo_unit.py:
#   Assets/Resources/Mixamo/caveman.fbx and caveman-chief.fbx with their textures (the game's
#   models, also used by CavemanCaptureTests; not in git)
# and installs the Meshy cave for the capture test.
#
#   Tools/Animation/build_caveman.sh
#
# Clips come from Art/Characters/Caveman/Mixamo and Art/Characters/CavemanChief/Mixamo, whichever
# character they were downloaded for, plus the spear hunter's breathing idle. Needs Blender 5.2.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
BLENDER=${BLENDER:-"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"}
ART="$ROOT/Art"
CM="$ART/Characters/Caveman/Mixamo"
CH="$ART/Characters/CavemanChief/Mixamo"
IDLE="$ART/Characters/SpearHunter/Mixamo/Breathing Idle.fbx"
mkdir -p "$HERE/work/caveman"
WORK=$(cd "$HERE/work/caveman" && pwd -W)
W() { cygpath -w "$1"; }
build() { "$BLENDER" -b --factory-startup -P "$HERE/meshy/build_mixamo_unit.py" -- "$@" | grep -E "^UNIT|Error|Traceback" || true; }

SHARED=(
  "Idle=$(W "$IDLE")"
  "AttackDown=$(W "$CM/Standing Melee Attack Downward.fbx")"
  "AttackSide=$(W "$CM/Standing Melee Attack Horizontal.fbx")"
  "AttackBack=$(W "$CM/Standing Melee Attack Backhand.fbx")"
  "Walk=$(W "$CH/Walking.fbx")"
  "WalkB=$(W "$CH/Walking (1).fbx")"
  "WalkHeavy=$(W "$CH/Orc Walk.fbx")"
  "Hit=$(W "$CM/Sword And Shield Impact.fbx")"
  "Death=$(W "$CM/Sword And Shield Death.fbx")"
)
build "$WORK/caveman.fbx" "$WORK/caveman-tex.png" "$(W "$CM/Standing Melee Attack Downward.fbx")" "${SHARED[@]}" \
  "ShieldIdle=$(W "$CM/Sword And Shield Idle.fbx")" "ShieldBlock=$(W "$CM/Sword And Shield Block.fbx")" \
  "PowerUp=$(W "$CM/Sword And Shield Power Up.fbx")"
build "$WORK/caveman-chief.fbx" "$WORK/caveman-chief-tex.png" "$(W "$CH/Orc Walk.fbx")" "${SHARED[@]}" \
  "Battlecry=$(W "$CH/Standing Taunt Battlecry.fbx")" \
  "AttackDownLeft=mirror:AttackDown" "AttackSideLeft=mirror:AttackSide"

unzip -o -q "$ART/Buildings/Cave/Meshy/"*.zip -d "$HERE/work/caveman/cave"

GAME="$ROOT/Assets/Resources/Mixamo"
TEST="$ROOT/Assets/Tests/PlayMode/StyleAssets/Resources"
mkdir -p "$GAME" "$TEST"
for f in caveman caveman-chief; do
  cp "$HERE/work/caveman/$f.fbx" "$GAME/"
  cp "$HERE/work/caveman/$f-tex.png" "$GAME/"
done
# One copy only: two Resources folders with the same path would make Resources.Load pick either.
rm -f "$TEST/Mixamo/caveman.fbx" "$TEST/Mixamo/caveman.fbx.meta" "$TEST/caveman-tex.png" "$TEST/caveman-tex.png.meta"
cp "$HERE"/work/caveman/cave/*/*.fbx "$TEST/cave.fbx"
cp "$HERE"/work/caveman/cave/*/*.png "$TEST/cave-tex.png"
# The capture test reads the cave's vertices, so Unity must keep them readable.
[ -f "$TEST/cave.fbx.meta" ] && sed -i 's/isReadable: 0/isReadable: 1/' "$TEST/cave.fbx.meta"
echo "Installed caveman.fbx, caveman-chief.fbx and cave.fbx"
