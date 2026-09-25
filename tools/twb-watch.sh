#!/usr/bin/env bash
# Rebuild the match report on a fixed beat while matches are running.
#
#   tools/twb-watch.sh <out.html> <interval-seconds> <logs-root> [<logs-root> ...]
#
# One line per refresh, which is the notification an operator watches. The
# report is regenerated from whatever is on disk AT THAT MOMENT, so matches
# still in flight are included -- a match folder is written as the match
# runs, not at the end, and the page is useful long before the batch is.
#
# Runs until the stop file (<out>.STOP) appears, so it can be ended without
# hunting for a pid.
set -u
cd "$(dirname "$0")/.."
OUT="${1:?usage: twb-watch.sh <out.html> <interval-s> <logs-root>...}"
EVERY="${2:?interval in seconds}"
shift 2
ROOTS=("$@")
STOP="${OUT}.STOP"
rm -f "$STOP"

echo "watch: $OUT every ${EVERY}s from ${ROOTS[*]} (stop: touch $STOP)"
while [ ! -f "$STOP" ]; do
  started=$(date +%s)
  line=$(python tools/twb-report.py "${ROOTS[@]}" --out "$OUT" 2>&1 | tail -1)
  # How much has actually been produced, so a refresh that finds nothing new
  # is visibly different from one that finds a match.
  folders=0
  for r in "${ROOTS[@]}"; do
    [ -d "$r" ] || continue
    n=$(find "$r" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l)
    folders=$((folders + n))
  done
  echo "[$(date +%H:%M:%S)] refresh: ${folders} match folder(s) | ${line}"
  # Sleep the REMAINDER, so a slow rebuild does not push the beat out.
  elapsed=$(( $(date +%s) - started ))
  rest=$(( EVERY - elapsed ))
  [ "$rest" -lt 5 ] && rest=5
  for _ in $(seq 1 "$rest"); do
    [ -f "$STOP" ] && break
    sleep 1
  done
done
echo "watch: stop file seen, ended"
