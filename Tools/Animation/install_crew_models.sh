#!/usr/bin/env bash
# Installs the Spear Throwers' and the Slingers' models for the game (user, 2026-11-11), from what
# build_spear_hunter.sh and build_slinger.sh made, into the git-ignored Resources folders:
#   Resources/Mixamo/spearhunter.fbx, stonehide.fbx      (the rigs, Mixamo and Kevin Iglesias motion)
#   Resources/Meshy/spearrock.fbx and the textures       (Meshy models stay local for now)
#   Resources/Meshy/slinger-tower.bytes                  (the approved one-deck tipi, as triangles)
# The capture tests load the same paths, so each rig is kept in one place only.
#
#   Tools/Animation/install_crew_models.sh
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
TEST="$ROOT/Assets/Tests/PlayMode/StyleAssets"
GAME="$ROOT/Assets/Resources"
mkdir -p "$GAME/Mixamo" "$GAME/Meshy"
# The rigs move (with their .meta, so Unity keeps them): two copies under Resources/Mixamo would
# make Resources.Load pick either.
for f in spearhunter stonehide; do
  if [ -f "$TEST/Resources/Mixamo/$f.fbx" ]; then
    mv -f "$TEST/Resources/Mixamo/$f.fbx" "$GAME/Mixamo/$f.fbx"
    [ -f "$TEST/Resources/Mixamo/$f.fbx.meta" ] && mv -f "$TEST/Resources/Mixamo/$f.fbx.meta" "$GAME/Mixamo/$f.fbx.meta"
  fi
done
copy() { if [ -f "$1" ]; then cp "$1" "$2"; else echo "missing: $1"; fi; }
copy "$TEST/Resources/spearrock.fbx" "$GAME/Meshy/spearrock.fbx"
copy "$TEST/Resources/spearrock-tex.png" "$GAME/Meshy/spearrock-tex.png"
copy "$TEST/Resources/spearhunter-tex.png" "$GAME/Meshy/spearhunter-tex.png"
copy "$TEST/Resources/stonehide.png" "$GAME/Meshy/stonehide-tex.png"
copy "$TEST/meshy-tipi.txt" "$GAME/Meshy/slinger-tower.bytes"
# The game reads the rock's triangles to stand the hunters on it: Unity must keep them readable
# (the .meta appears after Unity first imports the model; run this again then).
[ -f "$GAME/Meshy/spearrock.fbx.meta" ] && sed -i 's/isReadable: 0/isReadable: 1/' "$GAME/Meshy/spearrock.fbx.meta"
echo "Installed the spear hunter, the rock, the slinger and the tipi"
