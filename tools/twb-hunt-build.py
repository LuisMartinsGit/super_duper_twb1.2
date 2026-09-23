#!/usr/bin/env python3
"""Build the Desync Hunt Console: logs on disk -> one self-contained page.

    python tools/twb-hunt-build.py <logs-root> [<logs-root> ...] --out page.html

Two steps, one command. twb-hunt-data.py joins every artefact of a run into
one record per match; this drops that json into tools/twb-hunt-page.html at
the /*__TWB_DATA__*/ marker. The page carries its own data, so it can be
published as an artifact and read anywhere with no server behind it.

Keep it under the artifact size ceiling by trimming the oldest matches
first -- the hunt cares about what just ran.
"""
import io, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
# The Muster Rolls page: the 2026-09-07 replay's visual language, driven by
# the live match data. Override with --template for anything else.
TEMPLATE = os.path.join(HERE, "twb-muster-page.html")
EXTRACT = os.path.join(HERE, "twb-hunt-data.py")
MARKER = "/*__TWB_DATA__*/"
CEILING = 14 * 1024 * 1024      # the artifact page limit is 16 MB
# RAISED FROM 11 MB (2026-09-12). The overflow guard below DROPS whole
# matches to fit, oldest first, and the page had reached 10.83 MB -- so
# the next few minutes of a running batch would have started silently
# throwing away match history to make room. Embedded map art is ~520 KB
# of fixed overhead and per-second traces are the rest. 14 MB still
# leaves 2 MB of headroom under the hard limit.

args = list(sys.argv[1:])
OUT = "twb-hunt.html"
if "--out" in args:
    i = args.index("--out"); OUT = args[i + 1]; del args[i:i + 2]
# Forwarded, not consumed: the append-only run history belongs to the
# extractor. Without this the flag and its value were read as two more log
# roots and the history silently reset to the default path every refresh.
HISTORY = None
if "--template" in args:
    i = args.index("--template"); TEMPLATE = args[i + 1]; del args[i:i + 2]
if "--history" in args:
    i = args.index("--history"); HISTORY = args[i + 1]; del args[i:i + 2]
SINCE = ""
if "--since" in args:
    i = args.index("--since"); SINCE = args[i + 1]; del args[i:i + 2]
ROOTS = args or ["logs"]

tmp = os.path.join(os.path.dirname(os.path.abspath(OUT)) or ".", ".twb-hunt-data.json")
cmd = [sys.executable, EXTRACT] + ROOTS + ["--out", tmp]
if HISTORY:
    cmd += ["--history", HISTORY]
if SINCE:
    cmd += ["--since", SINCE]
r = subprocess.run(cmd, capture_output=True, text=True)
sys.stdout.write(r.stdout)
if r.returncode != 0:
    sys.stderr.write(r.stderr)
    raise SystemExit(r.returncode)

with io.open(tmp, encoding="utf-8") as fh:
    doc = json.load(fh)

with io.open(TEMPLATE, encoding="utf-8") as fh:
    page = fh.read()
if MARKER not in page:
    raise SystemExit("template lost its %s marker" % MARKER)


def emit(doc):
    # "</script>" inside the payload would close the tag that carries it.
    blob = json.dumps(doc, separators=(",", ":")).replace("</", "<\\/")
    return page.replace(MARKER, blob)


html = emit(doc)
while len(html.encode("utf-8")) > CEILING and len(doc["matches"]) > 3:
    doc["matches"] = doc["matches"][:-1]          # oldest first out
    html = emit(doc)

with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write(html)

# REPORT ALL TIME, not the window. The page carries only the newest N
# matches and folders are pruned off disk behind them, so a window total
# FALLS as the hunt runs -- "538,000 tick rows" became "461,000" an hour
# later with nothing wrong. The history file is the cumulative record and is
# the number worth quoting.
T = doc.get("totals", {})
mp = [m for m in doc["matches"] if m.get("kind") == "mp"]
print("%s: window %d matches (%d mp) | all time %s runs, %s lockstep, "
      "%s forked, %s tick rows | %.2f MB"
      % (OUT, len(doc["matches"]), len(mp),
         format(T.get("runs", 0), ","), format(T.get("mp", 0), ","),
         format(T.get("forks", 0), ","), format(T.get("compared", 0), ","),
         os.path.getsize(OUT) / 1e6))
try:
    os.remove(tmp)
except OSError:
    pass
