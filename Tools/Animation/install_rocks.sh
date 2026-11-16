#!/usr/bin/env bash
# Installs the active weapon's rocks (the Meshy "Fractured Stone Planet" and, for the fire rock,
# "Molten Heart", as they come) into Assets/Resources/Meshy/rock.fbx and fire-rock.fbx with their
# textures (ignored by git: Meshy models stay local for now).
#
#   Tools/Animation/install_rocks.sh
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
RES="$ROOT/Assets/Resources/Meshy"
SRC="$ROOT/Art/Weapons/Rock/Meshy"
mkdir -p "$RES" "$HERE/work"
install_one() {
  local zip="$1" name="$2"
  rm -rf "$HERE/work/$name"
  unzip -o -q "$zip" -d "$HERE/work/$name"
  cp "$HERE"/work/"$name"/*/*.fbx "$RES/$name.fbx"
  cp "$HERE"/work/"$name"/*/*.png "$RES/$name-tex.png"
  echo "Installed $name.fbx"
}
install_one "$SRC/"*Fractured_Stone_Planet*.zip rock
install_one "$SRC/"*Molten_Heart*.zip fire-rock
