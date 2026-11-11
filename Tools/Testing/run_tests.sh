#!/usr/bin/env bash
# Runs Unity tests in batch mode and keeps their results in order under Logs/ (not in git):
#
#   Logs/TestRuns/<date>/<name>.xml and <name>.log   results and the Unity log of each run
#   Logs/Captures/<topic>/                            frames and videos the capture tests record
#   Logs/Reports/<topic>/                             reports with numbers (balance, performance, crowd)
#   Logs/Old/                                         captures from before the 2026-11-02 art reset
#
# It stops Unity at once if the GPU hangs (a d3d12 fence error), instead of letting every later test
# time out, and prints each failed test at the end.
#
#   Tools/Testing/run_tests.sh "SpearHunterCaptureTests.RecordSpearHunter" spear-hunter
#   PLATFORM=EditMode Tools/Testing/run_tests.sh "" editmode       # an empty filter runs everything
set -uo pipefail
FILTER=${1:-}
NAME=${2:-run}
PLATFORM=${PLATFORM:-PlayMode}
UNITY=${UNITY:-"/c/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Unity.exe"}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
cd "$ROOT"
DIR="Logs/TestRuns/$(date +%F)"
mkdir -p "$DIR"
LOG="$DIR/$NAME.log"
XML="$DIR/$NAME.xml"
rm -f "$LOG" "$XML"
ARGS=(-batchmode -automated -projectPath . -runTests -testPlatform "$PLATFORM" -testResults "$XML" -logFile "$LOG")
[ -n "$FILTER" ] && ARGS+=(-testFilter "$FILTER")
"$UNITY" "${ARGS[@]}" &
PID=$!
sleep 15
while kill -0 $PID 2>/dev/null; do
  if grep -q "failed to wait for fence" "$LOG" 2>/dev/null; then
    echo "GPU HANG during: $(grep -o '"phase":"Begin"[^}]*' "$LOG" | grep -o '"name":"[^"]*"' | tail -1)"
    taskkill //F //IM Unity.exe >/dev/null 2>&1
    break
  fi
  sleep 5
done
grep -n "error CS" "$LOG" | head -5
grep -o '"phase":"End"[^}]*' "$LOG" | grep -o '"name":"[^"]*","state":[0-9]' | sed 's/"name":"TowerDefense.Tests.//' |
  awk -F'"state":' '$2==5{print "FAILED " $1} {c[$2]++} END{print "passed " c[4]+0 ", failed " c[5]+0 ", skipped " c[2]+0}'
echo "Results: $XML"
