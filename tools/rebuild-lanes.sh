#!/usr/bin/env bash
# Rebuild the two hunt installs WITHOUT destroying their match logs.
#
# WHY (2026-09-13). Every rebuild in the 2026-09-12 session ran
# `rm -rf Build/HeadlessHunt5 Build/HeadlessHunt6` first, and each install's
# `logs/` folder lives inside it. That deleted the evidence for two of the
# three lockstep forks the hunt had caught, plus the match in which the first
# headless elimination was observed. A rebuild must never touch logs again:
# set them aside, build, put them back.
set -eu
ROOT="C:/Users/overw/Documents/The Waning Border 1.2"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.0.37f1/Editor/Unity.exe"
LOG="${1:?usage: rebuild-lanes.sh <unity-log-path>}"
cd "$ROOT"
mkdir -p Build/_logkeep
for L in HeadlessHunt5 HeadlessHunt6; do
  if [ -d "Build/$L/logs" ]; then
    rm -rf "Build/_logkeep/$L"
    # The dashboard refresh reads these logs once a minute; a move that lands
    # on its open handle is denied. Retry rather than abort with the logs of
    # one lane already set aside (2026-09-13).
    for attempt in 1 2 3 4 5 6; do
      if mv "Build/$L/logs" "Build/_logkeep/$L" 2>/dev/null; then break; fi
      echo "logs of $L busy, retry $attempt"; sleep 10
    done
    [ -d "Build/$L/logs" ] && { echo "could not set aside Build/$L/logs; aborting before any rm"; exit 1; }
  fi
done
rm -rf Build/HeadlessHunt5 Build/HeadlessHunt6
"$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" \
  -executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build \
  -buildPath "$ROOT/Build/HeadlessHunt5" -logFile "$LOG"
cp -r Build/HeadlessHunt5 Build/HeadlessHunt6
for L in HeadlessHunt5 HeadlessHunt6; do
  if [ -d "Build/_logkeep/$L" ]; then
    rm -rf "Build/$L/logs"
    mv "Build/_logkeep/$L" "Build/$L/logs"
  fi
done
echo "REBUILT; logs preserved: $(ls Build/HeadlessHunt5/logs 2>/dev/null | wc -l) + $(ls Build/HeadlessHunt6/logs 2>/dev/null | wc -l) match folders"
