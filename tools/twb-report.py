#!/usr/bin/env python3
"""THE match report: logs on disk -> the Muster Rolls page.

    python tools/twb-report.py <logs-root> [...] --folder site/   # index.html + matches/*.json
    python tools/twb-report.py <logs-root> [...] --out page.html  # ONE self-contained file
    python tools/twb-report.py <logs-root> --text                 # console summary only

Two steps, one command. twb-match-data.py joins every artefact of a run into
one record per match; this puts those records behind tools/twb-report-page.html.

TWO SHAPES (2026-10-03):

  --folder DIR   index.html carries the page, the run ledger and a light
                 summary per match; each match's full record (replay tracks,
                 AI account, series...) is DIR/matches/<key>.json, fetched by
                 the page when the match is selected, and each map picture is
                 DIR/maps/<map>.png. Nothing is shared between matches, so
                 nothing is squeezed between them: every unit of every match
                 is carried at the extractor's track tolerance (0.75 m). A
                 file over FILE_CEILING has its tracks re-simplified at a
                 larger tolerance until it fits, and says so. This is the
                 shape to PUBLISH (an artifact takes 16 MB per file, 255
                 files, 64 MB per version) -- and to serve over http; a
                 browser will not fetch() local files from a file:// page.

  --out FILE     (or --inline) everything embedded in one html, as before --
                 the shape to open straight from disk. One file has one
                 ceiling (CEILING), so when the page is over it the replay
                 TOLERANCE is raised across the matches (units are never
                 dropped); only past the last tolerance step are whole
                 matches dropped, oldest first, and the page lists them.

THIS IS THE ONLY REPORT (2026-09-24). aggregate-metrics.py (console batch
economy), batch-dashboard.py (live batch HTML) and batch-report.py (finished
batch HTML) all read the same five CSVs from the same folders and drew
overlapping panels from them; all three are deleted and what they showed is
here. --text is what aggregate-metrics printed.
"""
import base64, importlib.util, io, json, os, re, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
# The Muster Rolls page: the 2026-09-07 replay's visual language, driven by
# the live match data. Override with --template for anything else.
TEMPLATE = os.path.join(HERE, "twb-report-page.html")
EXTRACT = os.path.join(HERE, "twb-match-data.py")
MARKER = "/*__TWB_DATA__*/"
CEILING = 14 * 1024 * 1024      # one-file page: the artifact page limit is 16 MB
FILE_CEILING = 15 * 1000 * 1000  # folder mode: per match file
TOTAL_CEILING = 62 * 1000 * 1000  # folder mode: an artifact version holds 64 MB
# Tolerance steps (metres) tried in order when a page or a file is too big;
# each re-simplifies the extractor's own keyframes, so the error bound is the
# extractor's 0.75 m PLUS the step (a step at or under 0.75 m changes nothing:
# the keyframes already satisfy it). Units are never dropped for size.
RETRACK_STEPS = [1.0, 1.5, 2.25, 3.0, 4.5, 6.0, 9.0, 12.0, 24.0]

args = list(sys.argv[1:])
OUT = None
FOLDER = None
if "--out" in args:
    i = args.index("--out"); OUT = args[i + 1]; del args[i:i + 2]
if "--folder" in args:
    i = args.index("--folder"); FOLDER = args[i + 1]; del args[i:i + 2]
if "--inline" in args:
    i = args.index("--inline"); del args[i:i + 1]
    if FOLDER:
        raise SystemExit("--inline and --folder are two different shapes; pick one")
if OUT and FOLDER:
    raise SystemExit("--out writes one file, --folder writes a folder; pick one")
if not OUT and not FOLDER:
    OUT = "twb-report.html"
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
MAX = None
if "--max" in args:
    i = args.index("--max"); MAX = args[i + 1]; del args[i:i + 2]
TEXT = False
if "--text" in args:
    i = args.index("--text"); TEXT = True; del args[i:i + 1]
ROOTS = args or ["logs"]

base_dir = os.path.abspath(FOLDER) if FOLDER else (os.path.dirname(os.path.abspath(OUT)) or ".")
os.makedirs(base_dir, exist_ok=True)
tmp = os.path.join(base_dir, ".twb-match-data.json")
cmd = [sys.executable, EXTRACT] + ROOTS + ["--out", tmp]
if HISTORY:
    cmd += ["--history", HISTORY]
if SINCE:
    cmd += ["--since", SINCE]
if MAX:
    cmd += ["--max", MAX]
if FOLDER:
    cmd += ["--per-match"]
r = subprocess.run(cmd, capture_output=True, text=True)
sys.stdout.write(r.stdout)
if r.returncode != 0:
    sys.stderr.write(r.stderr)
    raise SystemExit(r.returncode)

with io.open(tmp, encoding="utf-8") as fh:
    doc = json.load(fh)
try:
    os.remove(tmp)
except OSError:
    pass


def extractor():
    """twb-match-data.py as a module, for its track re-simplifier. Its
    top level only parses arguments; nothing runs until main()."""
    spec = importlib.util.spec_from_file_location("twb_match_data", EXTRACT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def blob(obj):
    # "</script>" inside the payload would close the tag that carries it.
    return json.dumps(obj, separators=(",", ":")).replace("</", "<\\/")


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
    raise SystemExit(0)

with io.open(TEMPLATE, encoding="utf-8") as fh:
    page = fh.read()
if MARKER not in page:
    raise SystemExit("template lost its %s marker" % MARKER)


def emit(d):
    return page.replace(MARKER, blob(d))


def totals_line(where, size):
    T = doc.get("totals", {})
    mp = [m for m in doc["matches"] if m.get("kind") == "mp"]
    # REPORT ALL TIME, not the window. The page carries only the newest N
    # matches and folders are pruned off disk behind them, so a window total
    # FALLS as the hunt runs. The history file is the cumulative record.
    print("%s: window %d matches (%d mp) | all time %s runs, %s lockstep, "
          "%s forked, %s tick rows | %.2f MB"
          % (where, len(doc["matches"]), len(mp),
             format(T.get("runs", 0), ","), format(T.get("mp", 0), ","),
             format(T.get("forks", 0), ","), format(T.get("compared", 0), ","),
             size / 1e6))


# What index.html keeps of each match in folder mode: enough for the run
# picker, the header and the ledger before the match's own file arrives.
SUMMARY_KEYS = ("key", "map", "stamp", "kind", "peers", "outcome", "duration",
                "decidedAt", "winner", "build", "exceptions", "errors", "warnings",
                "tmax", "config", "fidelity", "desync", "hasReplay", "trackInfo",
                "error", "wall", "realtime", "label", "aiRoster", "scoreLeader")


def safe(name):
    return re.sub(r"[^A-Za-z0-9._-]+", "_", name)


if FOLDER:
    # ── folder mode ────────────────────────────────────────────────────────
    mdir = os.path.join(base_dir, "matches")
    adir = os.path.join(base_dir, "maps")
    for d in (mdir, adir):
        if os.path.isdir(d):
            shutil.rmtree(d)          # a stale match file must never be served
        os.makedirs(d)
    # Map pictures as real PNG files, referenced by relative path.
    art = {}
    for name, uri in (doc.get("mapArt") or {}).items():
        mm = re.match(r"data:image/png;base64,(.*)$", uri or "", re.S)
        if not mm:
            continue
        fn = "maps/%s.png" % safe(name)
        with open(os.path.join(base_dir, fn), "wb") as fh:
            fh.write(base64.b64decode(mm.group(1)))
        art[name] = fn
    mod = None
    summaries, sizes = [], []
    for rec in doc["matches"]:
        fn = "matches/%s.json" % safe(rec["key"])
        text = blob(rec)
        step = 0
        base = ([list(t[2]) for t in rec["tracks"]], dict(rec.get("trackInfo") or {})) \
            if rec.get("tracks") else None
        while len(text.encode("utf-8")) > FILE_CEILING and base \
                and step < len(RETRACK_STEPS):
            mod = mod or extractor()
            before = len(text.encode("utf-8"))
            # from the extractor's keyframes every time: bound = its tol + step
            for t, c in zip(rec["tracks"], base[0]):
                t[2] = list(c)
            rec["trackInfo"] = dict(base[1])
            mod.retrack(rec, RETRACK_STEPS[step])
            text = blob(rec)
            print("  %s: %.1f MB > %.1f MB per file -- tracks re-simplified, "
                  "tolerance now %.2f m (%.1f MB)"
                  % (rec["key"], before / 1e6, FILE_CEILING / 1e6,
                     rec["trackInfo"]["tol"], len(text.encode("utf-8")) / 1e6))
            step += 1
        with io.open(os.path.join(base_dir, fn), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        s = {k: rec[k] for k in SUMMARY_KEYS if k in rec}
        s["file"] = fn
        s["bytes"] = len(text.encode("utf-8"))
        summaries.append(s)
        sizes.append((fn, s["bytes"]))
    idx = dict(doc)
    idx["matches"] = summaries
    idx["mapArt"] = art
    idx["folder"] = True
    html = emit(idx)
    ipath = os.path.join(base_dir, "index.html")
    with io.open(ipath, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(html)
    total = os.path.getsize(ipath) + sum(b for _, b in sizes) + sum(
        os.path.getsize(os.path.join(base_dir, f)) for f in art.values())
    print("files:")
    print("  %-56s %8.2f MB" % ("index.html", os.path.getsize(ipath) / 1e6))
    for fn, b in sizes:
        print("  %-56s %8.2f MB" % (fn, b / 1e6))
    for fn in sorted(art.values()):
        print("  %-56s %8.2f MB" % (fn, os.path.getsize(os.path.join(base_dir, fn)) / 1e6))
    n_files = 1 + len(sizes) + len(art)
    print("%d files, %.2f MB total" % (n_files, total / 1e6))
    if total > TOTAL_CEILING:
        print("WARNING: %.1f MB is over the %.0f MB an artifact version holds -- "
              "build with fewer roots or --max" % (total / 1e6, TOTAL_CEILING / 1e6))
    if n_files > 255:
        print("WARNING: %d files is over the 255 an artifact version holds" % n_files)
    totals_line(ipath, total)
    raise SystemExit(0)

# ── one-file mode ──────────────────────────────────────────────────────────
html = emit(doc)
size = len(html.encode("utf-8"))
step = 0
mod = None
# RAISE THE TOLERANCE BEFORE DROPPING ANYTHING. Every unit stays on the page;
# what one file can no longer afford is precision.
# Every step starts again from the extractor's own keyframes, so the bound is
# its tolerance plus the step -- never a sum of all the steps tried.
base = {m["key"]: ([list(t[2]) for t in m["tracks"]], dict(m.get("trackInfo") or {}))
        for m in doc["matches"] if m.get("tracks")}
while size > CEILING and step < len(RETRACK_STEPS) and base:
    mod = mod or extractor()
    for m in doc["matches"]:
        if m["key"] not in base:
            continue
        codes, info = base[m["key"]]
        for t, c in zip(m["tracks"], codes):
            t[2] = list(c)
        m["trackInfo"] = dict(info)
        mod.retrack(m, RETRACK_STEPS[step])
    html = emit(doc)
    print("  page %.1f MB > %.1f MB: tracks re-simplified, +%.2f m -> %.1f MB"
          % (size / 1e6, CEILING / 1e6, RETRACK_STEPS[step], len(html.encode("utf-8")) / 1e6))
    size = len(html.encode("utf-8"))
    step += 1
# SAY WHAT WAS DROPPED (2026-09-24). This guard silently discarded whole
# matches to fit the ceiling, which is the one thing a report must never do
# without saying so. Record them; the page shows the list.
dropped = []
while size > CEILING and len(doc["matches"]) > 3:
    gone = doc["matches"].pop()                   # oldest first out
    dropped.append("%s %s" % (gone.get("stamp", "?"), gone.get("map", "?")))
    doc["overflow"] = dropped
    html = emit(doc)
    size = len(html.encode("utf-8"))
if dropped:
    print("OVERFLOW: dropped %d match(es) to fit %.1f MB: %s"
          % (len(dropped), CEILING / 1e6, ", ".join(dropped)))

with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write(html)
totals_line(OUT, os.path.getsize(OUT))
