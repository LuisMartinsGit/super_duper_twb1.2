#!/usr/bin/env bash
# Rebuild the Desync Hunt Console from every log root the hunt has ever
# written to. Called on a ONE-MINUTE beat (operator directive, 2026-09-12;
# it was five). Prints ONE line, which is the notification the operator sees.
#
# Roots accumulate for two reasons. A player build cannot overwrite an exe
# that four headless peers are running, so each rebuild goes to a fresh
# folder and the hunt moves to it. And each concurrent hunt LANE gets its own
# install, because the match-log folder is named from the lockstep player
# index -- two lanes in one install would both write "_host" and the peer
# pairing could cross between them.
set -u
cd "$(dirname "$0")/.."
OUT="${1:?usage: twb-hunt-refresh.sh <out.html> [all]}"
# LATEST RUN ONLY unless a second argument "all" is given (2026-09-13). The
# run is everything since the current player was built: read the build
# time straight off the assembly the lanes are executing, so a rebuild
# starts a new run automatically and nothing has to be edited by hand.
SINCE=""
if [ "${2:-}" != "all" ]; then
  DLL="Build/HeadlessHunt5/The Waning Border_Data/Managed/TheWaningBorder.Runtime.dll"
  [ -f "$DLL" ] && SINCE=$(date -r "$DLL" "+%Y-%m-%d_%H-%M-%S")
fi

python tools/twb-hunt-build.py \
  "Build/HeadlessHunt5/logs" \
  "Build/HeadlessHunt6/logs" \
  "Build/HeadlessHunt4/logs" \
  "Build/HeadlessHunt4B/logs" \
  "Build/HeadlessHuntB/logs" \
  "Build/HeadlessHunt3/logs" \
  "Build/HeadlessHunt2/logs" \
  "Build/HeadlessMpHunt/logs" \
  "Build/HeadlessDesync/logs" \
  "Build/Headless/logs" \
  "logs" \
  --history "logs/hunt-history.json" \
  ${SINCE:+--since "$SINCE"} \
  --out "$OUT" 2>&1 | tail -1
