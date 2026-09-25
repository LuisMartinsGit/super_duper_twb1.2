#!/usr/bin/env python3
"""THE match report: logs on disk -> one self-contained page.

    python tools/twb-report.py <logs-root> [<logs-root> ...] --out page.html
    python tools/twb-report.py <logs-root> --text        # console summary only

Two steps, one command. twb-match-data.py joins every artefact of a run into
one record per match; this drops that json into tools/twb-report-page.html at
the /*__TWB_DATA__*/ marker.

THIS IS THE ONLY REPORT (2026-09-24). aggregate-metrics.py (console batch
economy), batch-dashboard.py (live batch HTML) and batch-report.py (finished
batch HTML) all read the same five CSVs from the same folders and drew
overlapping panels from them; all three are deleted and what they showed is
here. --text is what aggregate-metrics printed.

The page carries its own data, so it can be published as an artifact and
read anywhere with no server behind it.

Keep it under the artifact size ceiling by trimming the oldest matches
first -- the hunt cares about what just ran.
"""
import io, json, os, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
# The Muster Rolls page: the 2026-09-07 replay's visual language, driven by
# the live match data. Override with --template for anything else.
TEMPLATE = os.path.join(HERE, "twb-report-page.html")
EXTRACT = os.path.join(HERE, "twb-match-data.py")
MARKER = "/*__TWB_DATA__*/"
CEILING = 14 * 1024 * 1024      # the artifact page limit is 16 MB
# RAISED FROM 11 MB (2026-09-12). The overflow guard below DROPS whole
# matches to fit, oldest first, and the page had reached 10.83 MB -- so
# the next few minutes of a running batch would have started silently
# throwing away match history to make room. Embedded map art is ~520 KB
# of fixed overhead and per-second traces are the rest. 14 MB still
# leaves 2 MB of headroom under the hard limit.

args = list(sys.argv[1:])
OUT = "twb-report.html"
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
TEXT = False
if "--text" in args:
    i = args.index("--text"); TEXT = True; del args[i:i + 1]
ROOTS = args or ["logs"]

tmp = os.path.join(os.path.dirname(os.path.abspath(OUT)) or ".", ".twb-match-data.json")
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


def summarise(doc):
    """The console roll-up aggregate-metrics.py used to print.

    Not "what happened" but "what happens": one line per match, then the
    spread across them. A single match is an anecdote; the spread is the
    finding.
    """
    ms = doc.get("matches", [])
    if not ms:
        print("no matches under: " + ", ".join(ROOTS))
        return
    print("%-22s %-15s %5s %6s %7s %6s %s"
          % ("match", "map", "kind", "dur", "outcome", "errs", "verdict"))
    for m in ms:
        d = m.get("desync") or {}
        forked_here = d.get("flagged") or d.get("forkTick") is not None
        verdict = "FORK" if forked_here else ("sync" if m.get("kind") == "mp" else "-")
        print("%-22s %-15s %5s %6s %7s %6s %s"
              % (str(m.get("stamp"))[:22], str(m.get("map"))[:15], m.get("kind"),
                 m.get("duration"), str(m.get("outcome"))[:7],
                 m.get("errors", 0), verdict))

    # The spread. Population and territory are the two numbers that say
    # whether the AI played a game at all.
    import statistics as st
    pops, terrs, units = [], [], []
    for m in ms:
        for f, ser in (m.get("series") or {}).items():
            if not ser:
                continue
            last = ser[-1]
            pops.append(last.get("pop", 0))
            terrs.append(last.get("terr", 0))
            units.append(last.get("units", 0))

    def spread(name, xs):
        if not xs:
            print("  %-12s no samples" % name)
            return
        print("  %-12s min %4d   median %4d   max %4d   (n=%d)"
              % (name, min(xs), int(st.median(xs)), max(xs), len(xs)))

    print(os.linesep + "across %d match(es), per faction at the last sample:" % len(ms))
    spread("population", pops)
    spread("territories", terrs)
    spread("units", units)
    forked = [m for m in ms if (m.get("desync") or {}).get("forkTick") is not None
              or (m.get("desync") or {}).get("flagged")]
    if forked:
        print(os.linesep + "%d FORKED match(es):" % len(forked))
        for m in forked:
            print("  %s on %s" % (m.get("stamp"), m.get("map")))


if TEXT:
    summarise(doc)
    try:
        os.remove(tmp)
    except OSError:
        pass
    raise SystemExit(0)

html = emit(doc)
# SAY WHAT WAS DROPPED (2026-09-24). This guard silently discarded whole
# matches to fit the ceiling, which is the one thing a report must never do
# without saying so -- a batch can lose its baseline runs and read as though
# they were never run. Record them; the page shows the list.
dropped = []
while len(html.encode("utf-8")) > CEILING and len(doc["matches"]) > 3:
    gone = doc["matches"].pop()                   # oldest first out
    dropped.append("%s %s" % (gone.get("stamp", "?"), gone.get("map", "?")))
    doc["overflow"] = dropped
    html = emit(doc)
if dropped:
    print("OVERFLOW: dropped %d match(es) to fit %.1f MB: %s"
          % (len(dropped), CEILING / 1e6, ", ".join(dropped)))

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
