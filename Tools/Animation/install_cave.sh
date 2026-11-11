#!/usr/bin/env bash
# Installs the cavemen's cave (the Meshy "Primal Cave Dwelling", as it comes) into
# Assets/Resources/Meshy/cave.fbx with its texture (ignored by git: Meshy models stay local for now).
#
#   Tools/Animation/install_cave.sh
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
RES="$ROOT/Assets/Resources/Meshy"
mkdir -p "$RES" "$HERE/work"
rm -rf "$HERE/work/cave"
unzip -o -q "$ROOT/Art/Buildings/Cave/Meshy/"*.zip -d "$HERE/work/cave"
cp "$HERE"/work/cave/*/*.fbx "$RES/cave.fbx"
cp "$HERE"/work/cave/*/*.png "$RES/cave-tex.png"
echo "Installed cave.fbx"
