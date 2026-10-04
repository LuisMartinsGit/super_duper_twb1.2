#!/usr/bin/env python3
"""Collapse every artefact a headless run leaves behind into ONE json file.

    python tools/twb-match-data.py <logs-root> [<logs-root> ...] --out data.json

THE ONE EXTRACTOR (2026-09-24). Six tools used to read these folders, each
answering one question and each with its own idea of what a "session" is:
aggregate-metrics (batch economy), batch-dashboard (live batch state),
batch-report (finished batch), mp-diff (per-tick cross-peer agreement), the
Desync_* dumps, and the AI_*.log audit trail. Nothing joined them, so "did
the desync happen in the match where Red was starving" was a question you
answered by hand, across four windows. The first three are now DELETED and
everything they read is carried here.

This joins them on the match. One record per MATCH -- peers folded together,
because a four-peer lockstep match is ONE match seen four times -- carrying:

  * the desync verdict, from both the SYNC flag and a per-tick cross-peer
    comparison of every Lockstep.log column
  * the AI's own account of itself per faction, parsed out of AI_*.log:
    wallets and their weights, what it refused to buy and why, posture,
    plan, claims, research, and the INTEL line
  * the economy / military / territory time series from Metrics_Faction.csv
  * everything needed to REPLAY the match on a map: a track per unit
    (traced matches; sampled position frames otherwise), building add/remove
    events, and death events
  * the end-of-match rolls the old batch reports were built on: what each
    faction had STANDING (Metrics_Buildings), what it had RESEARCHED
    (Metrics_Research), WHERE it built (Metrics_Placement) and its kills
    and deaths per minute (Metrics_Combat)

Peers of one match are matched by map name and a start time within two
minutes, the same rule mp-diff.ps1 uses.
"""
import base64, csv, io, json, os, re, sys, glob, time
from collections import defaultdict

# -- args ------------------------------------------------------------------
args = list(sys.argv[1:])
OUT = "twb-match-data.json"
if "--out" in args:
    i = args.index("--out"); OUT = args[i + 1]; del args[i:i + 2]
MAX_MATCHES = 40          # newest first; the page has to stay openable

# REPLAY BUDGETS (2026-10-03, rewritten). Units are no longer SAMPLED to fit a
# point budget: every unit the trace saw is carried, as its own track (see
# "per-unit tracks" below), and what bounds the size is the track TOLERANCE --
# how far, in metres, the interpolated replay may stray from a recorded sample.
#   TRACK_TOL   the starting tolerance; 0.75 m keeps every unit within less
#               than half a build cell of where the game actually had it.
#   TRACK_CAP   bytes of encoded tracks one match may carry. A match over it
#               is re-simplified from its raw samples at twice the tolerance,
#               and again, until it fits -- and says so. 12 MB leaves room for
#               the rest of the record under a 15 MB per-file limit.
# A single-file page (twb-report.py --inline) may still have to fit several
# matches in ONE file; it raises the tolerance further itself (retrack), and
# never drops units.
TRACK_TOL = 0.75
TRACK_CAP = 12 * 1000 * 1000
# Sampled (pre-trace) matches still have frames, and still have a budget.
SAMPLED_TOTAL_BUDGET = 200000
SAMPLED_SHARE = 60000
# Death events per match. thin() keeps an EVEN spread and always keeps the
# last row, so a capped list still ends where the match ended.
DEATH_TOTAL_BUDGET = 30000
DEATH_CAP = 5000
# The AI's own series, per faction. Set by main() from the window size.
AI_SERIES_TOTAL = 2400
AI_SERIES_CAP = 400
AI_EVENT_CAP = 300
if "--max" in args:
    i = args.index("--max"); MAX_MATCHES = int(args[i + 1]); del args[i:i + 2]
if "--track-tol" in args:
    i = args.index("--track-tol"); TRACK_TOL = float(args[i + 1]); del args[i:i + 2]
if "--track-cap" in args:
    i = args.index("--track-cap"); TRACK_CAP = int(float(args[i + 1])); del args[i:i + 2]
# ONE FILE PER MATCH (twb-report.py --folder): nothing is shared between the
# matches any more, so nothing is divided between them either -- each match
# gets the single-match caps, whatever else is in the window.
PER_MATCH = False
if "--per-match" in args:
    i = args.index("--per-match"); PER_MATCH = True; del args[i:i + 1]
# The append-only record. Match folders are pruned off disk as the hunt runs
# and the page only carries the newest MAX_MATCHES anyway, so without this a
# fork found at 03:00 has silently vanished from the totals by 09:00 -- the
# one number the whole exercise exists to accumulate.
HISTORY = "hunt-history.json"
if "--history" in args:
    i = args.index("--history"); HISTORY = args[i + 1]; del args[i:i + 2]
# LATEST RUN ONLY (2026-09-13, operator: "the report screen with only the
# latest run's matches"). A run is everything started since the player the
# lanes are running was BUILT -- the refresh script passes that build time.
# Everything older is still read (so totals and the append-only history keep
# accumulating) but is left off the page.
SINCE = ""
if "--since" in args:
    i = args.index("--since"); SINCE = args[i + 1]; del args[i:i + 2]
ROOTS = args or ["logs"]

FACTIONS = ["Blue", "Red", "Green", "Yellow", "Purple", "Orange", "Teal", "White"]

# Log floats may be pt-PT ("0,08") or invariant ("0.08"): the AI log's own
# timestamps were only made invariant on 2026-09-12 and every older match
# still on disk has commas. Accept both, always.
def f(x, d=0.0):
    try:
        return float(str(x).strip().replace(",", "."))
    except Exception:
        return d


def i_(x, d=0):
    try:
        return int(float(str(x).strip().replace(",", ".")))
    except Exception:
        return d


def read_csv(path):
    if not os.path.exists(path):
        return []
    try:
        with io.open(path, newline="", encoding="utf-8", errors="ignore") as fh:
            return list(csv.DictReader(fh))
    except Exception:
        return []


def read_lines(path):
    """Peers may still hold the file open; never take an exclusive handle."""
    if not os.path.exists(path):
        return []
    try:
        with io.open(path, encoding="utf-8", errors="ignore") as fh:
            return fh.read().splitlines()
    except Exception:
        return []


# -- folder discovery ------------------------------------------------------
STAMP = re.compile(r"^(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(.+?)(_host|_client\d+|-\d+)?$")


def scan(root):
    out = []
    if not os.path.isdir(root):
        return out
    for d in sorted(os.listdir(root)):
        full = os.path.join(root, d)
        if not os.path.isdir(full):
            continue
        m = STAMP.match(d)
        if not m:
            continue
        stamp, mapname, suffix = m.group(1), m.group(2), m.group(3) or ""
        role = ("host" if suffix == "_host"
                else "client" if suffix.startswith("_client")
                else "solo")
        # WARM-UP WORLDS ARE NOT MATCHES (2026-09-13, operator: "most of them
        # have 0 length"). Before each lockstep match the hunt boots a 60 s
        # throwaway world on every peer, so a four-peer match leaves FOUR
        # extra folders named "<Map>-<peer>". They write no metrics -- they
        # exist only so the real match starts with a previous world's residue
        # -- and they were being counted as 148 zero-length "runs" out of 206.
        # A "-N" suffix with no Metrics_Faction.csv is that shape exactly.
        # The "-N" suffix only dates from 2026-09-12; before that a warm-up
        # folder was named like any match. The one signal every warm-up has
        # ever written is its own console line, so that is the test -- read
        # only when the folder has no metrics, which is already rare.
        if role == "solo" and not os.path.exists(os.path.join(full, "Metrics_Faction.csv")):
            for cn in ("Console.log", "Console-1.log", "Console-2.log",
                       "Console-3.log", "Console-4.log"):
                cp = os.path.join(full, cn)
                if not os.path.exists(cp):
                    continue
                try:
                    with io.open(cp, encoding="utf-8", errors="replace") as fh:
                        head = fh.read(200000)
                    if "warm-up over" in head or "warm-up world" in head:
                        role = "warm"
                        break
                except Exception:
                    pass
        out.append(dict(dir=full, name=d, stamp=stamp, map=mapname, role=role,
                        peer=(0 if role == "host"
                              else i_(suffix[7:]) if role == "client" else -1),
                        root=root))
    return out


def to_epoch(stamp):
    try:
        return time.mktime(time.strptime(stamp, "%Y-%m-%d_%H-%M-%S"))
    except Exception:
        return 0.0


# -- grouping: peers of one lockstep match are ONE match -------------------
def group(folders):
    """Fold a lockstep match's peer folders into one match.

    SAME ROOT ONLY. Two hunt lanes run at once, each in its own install, and
    the folder name carries the LOCKSTEP player index -- so both lanes write
    a "_host". Pairing on map + time alone stitched lane A's host to lane B's
    clients and produced a seven-peer chimera whose peers were playing
    different matches with different seeds; it duly "forked" on the rng
    column at tick 0. A false fork is worse than a missed one: it spends the
    investigation the real ones deserve. The root is the lane, so the root is
    the boundary.
    """
    mp = [x for x in folders if x["role"] in ("host", "client")]
    solo = [x for x in folders if x["role"] == "solo"]
    # Warm-ups are counted, never listed: see scan().
    group.warmups = sum(1 for x in folders if x["role"] == "warm")
    group.warmkeys = set(x["name"] for x in folders if x["role"] == "warm")
    matches, used = [], set()
    for h in sorted((x for x in mp if x["role"] == "host"),
                    key=lambda x: x["stamp"], reverse=True):
        peers = [h]
        used.add(h["name"])
        for c in mp:
            if c["role"] != "client" or c["name"] in used:
                continue
            if c["root"] != h["root"]:      # different lane, different match
                continue
            if c["map"] != h["map"]:
                continue
            if abs(to_epoch(c["stamp"]) - to_epoch(h["stamp"])) > 120:
                continue
            peers.append(c)
            used.add(c["name"])
        # A peer set larger than the match declared is a pairing failure, not
        # a finding. Keep the ones nearest the host in time and say so.
        if len(peers) > 4:
            peers.sort(key=lambda x: abs(to_epoch(x["stamp"]) - to_epoch(h["stamp"])))
            for extra in peers[4:]:
                used.discard(extra["name"])
            peers = peers[:4]
        matches.append(dict(kind="mp", key=h["name"], map=h["map"],
                            stamp=h["stamp"], peers=peers, root=h["root"]))
    for s in solo:
        matches.append(dict(kind="sp", key=s["name"], map=s["map"],
                            stamp=s["stamp"], peers=[s], root=s["root"]))
    matches.sort(key=lambda m: m["stamp"], reverse=True)
    return matches


# -- desync verdict --------------------------------------------------------
SUM = re.compile(r"^sum\s+tick=(\d+)\s")
COLS = ["pos", "rot", "hp", "nav", "cbt", "wrk", "bank",
        "tech", "rng", "veil", "cost"]


def lockstep_rows(path):
    rows = {}
    for line in read_lines(path):
        if not line.startswith("sum"):
            continue
        m = SUM.match(line)
        if m:
            rows[int(m.group(1))] = line
    return rows


def cols_of(line):
    return dict(re.findall(r"(\w+)=0x([0-9A-F]+)", line))


CMDLINE = re.compile(r"^tick=(\d+)\s+cmds=(\d+)(.*)$")
CMDITEM = re.compile(r"p(\d+)#\d+\s+(\w+)")


def commands_of(host_dir, fork_tick=None):
    """What the peers actually ORDERED, from the host's Lockstep.log.

    Two products. A per-faction count bucketed by 15 s, which is the only
    view of AI activity that is not the AI's own account of itself -- the
    brain can log an intention the router then refuses. And, when the match
    forked, the raw order lines around the fork: a command landing on one
    tick here and another tick there is the first thing to rule out, and
    until now that meant opening four Lockstep.logs by hand.
    """
    buckets = defaultdict(lambda: defaultdict(int))
    kinds = defaultdict(int)
    near = []
    lo = hi = None
    if fork_tick is not None:
        lo, hi = fork_tick - 120, fork_tick + 30
    for line in read_lines(os.path.join(host_dir, "Lockstep.log")):
        if not line.startswith("tick="):
            continue
        m = CMDLINE.match(line)
        if not m:
            continue
        tick, n, rest = int(m.group(1)), int(m.group(2)), m.group(3)
        if n == 0:
            continue
        if lo is not None and lo <= tick <= hi:
            near.append(line[:600])
        bucket = int(tick / 30.0 / 15.0) * 15      # 30 Hz -> 15 s buckets
        for pi, kind in CMDITEM.findall(rest):
            buckets[bucket]["p" + pi] += 1
            kinds[kind] += 1
    series = [dict(t=b, by=dict(v)) for b, v in sorted(buckets.items())]
    return dict(series=series, kinds=dict(kinds), near=near[:80])


def desync_of(match):
    """Both verdicts: the 30-tick SYNC flag, and every tick compared."""
    v = dict(flagged=False, flagTick=None, forkTick=None, forkCols=[],
             compared=0, ticks=0, dumps=[], peers=len(match["peers"]))
    for p in match["peers"]:
        lines = []
        for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log"):
            lines += read_lines(os.path.join(p["dir"], cn))
        for line in lines:
            if "DESYNC at tick" in line:
                v["flagged"] = True
                m = re.search(r"DESYNC at tick (\d+)", line)
                if m:
                    t = int(m.group(1))
                    v["flagTick"] = t if v["flagTick"] is None else min(v["flagTick"], t)
                m = re.search(r"Forked subsystems: ([^.]+)", line)
                if m:
                    v["forkCols"] = [c.strip() for c in m.group(1).split(",")]
        for d in glob.glob(os.path.join(p["dir"], "Desync_*")):
            v["dumps"].append(os.path.basename(d))

    if match["kind"] != "mp" or len(match["peers"]) < 2:
        return v
    host = next((p for p in match["peers"] if p["role"] == "host"), None)
    if host is None:
        return v
    href = lockstep_rows(os.path.join(host["dir"], "Lockstep.log"))
    v["ticks"] = len(href)
    if not href:
        return v
    for p in match["peers"]:
        if p is host:
            continue
        for t, line in sorted(lockstep_rows(os.path.join(p["dir"], "Lockstep.log")).items()):
            if t not in href:
                continue            # trailing tick, peers stop a tick apart
            v["compared"] += 1
            if href[t] == line:
                continue
            if v["forkTick"] is None or t < v["forkTick"]:
                a, b = cols_of(href[t]), cols_of(line)
                v["forkTick"] = t
                v["forkCols"] = [c for c in COLS if a.get(c) != b.get(c)]
    return v


# -- the AI's account of itself --------------------------------------------
TS = re.compile(r"^\[(\d+):([\d.,]+)\]\s+([A-Z]+):\s*(.*)$")
# The wave story is spread over three line shapes, all category WAVE:
#   objective (x,z) [why] army N, mustering|striking...
#   wave N LAUNCHED at Ts (min M, posture P); next at Ts
#   wave N reinforced with S unit(s) (C already committed, A on the objective)
# The objective line carries the SIZE and comes first; the LAUNCHED line
# carries the number and the sim time. Pair them.
W_OBJ = re.compile(r"objective \(([-\d]+),([-\d]+)\)\s*\[([^\]]*)\]\s*army (\d+)")
W_LAUNCH = re.compile(r"wave (\d+) LAUNCHED at (\d+)s (?:with (\d+) unit\(s\) )?\(min (\d+), posture (\w+)\)")
# A mission that expired without killing its objective. The ground it was
# aimed at is blocked for a while afterwards, which is why the next wave
# suddenly picks a different target -- worth showing, or that looks random.
W_TIMEOUT = re.compile(r"mission timed out at \(([-\d]+),([-\d]+)\) after (\d+)s with (\d+) alive")
W_REINF = re.compile(r"wave (\d+) reinforced with (\d+) unit\(s\) \((\d+) already committed, (\d+) on the objective\)")
W_SPENT = re.compile(r"wave (\d+) SPENT")
W_BLOCK = re.compile(r"wave (\d+) BLOCKED at (\d+)s \(need (\d+) idle")
# Held by the full-population rule past minute 25 (Game_AI.md 6a). A distinct
# shape from BLOCKED: the army exists, it is just not the whole army yet.
W_HELD = re.compile(r"wave (\d+) HELD at (\d+)s .*?is (\d+)/(\d+)")
BUDGET = re.compile(
    r"w\(adv/mil/eco\)=\(([\d.,]+)/([\d.,]+)/([\d.,]+)\)\s*"
    r"S\[([^\]]*)\].*?emaS=([\d.,-]+)/s\s+emaI=([\d.,-]+)/s")
WALLET = re.compile(r"([\d.,-]+)s,([\d.,-]+)i,([\d.,-]+)v,([\d.,-]+)vs")
INTEL = re.compile(
    r"sightings (\d+) \(mil (\d+), eco (\d+), struct (\d+)\)\s+"
    r"enemyStrength (-?\d+) knownBases (\d+) lastSeen (-?\d+)s\s+threatAtContact (-?\d+)")
GOALS_REF = re.compile(r"([A-Za-z@_]+) (\d+)/(\d+) \[([^\]]+)\]")


def parse_ai(path):
    """One faction's log -> time series + event list. Everything the brain
    said about itself, which is the only honest account of why it did what
    it did."""
    out = dict(budget=[], intel=[], goals=[], posture=[], plan=[], claim=[],
               research=[], cmds=defaultdict(int), refusals=defaultdict(int),
               waves=[], lines=0)
    pending_obj = None      # the objective line that precedes each LAUNCHED
    for line in read_lines(path):
        m = TS.match(line)
        if not m:
            continue
        out["lines"] += 1
        t = int(m.group(1)) * 60 + f(m.group(2))
        cat, body = m.group(3), m.group(4)

        if cat == "BUDGET":
            b = BUDGET.search(body)
            if b:
                wal = [[f(x) for x in w] for w in WALLET.findall(b.group(4))]
                while len(wal) < 3:
                    wal.append([0.0, 0.0, 0.0, 0.0])
                out["budget"].append(dict(
                    t=round(t, 1),
                    w=[f(b.group(1)), f(b.group(2)), f(b.group(3))],
                    adv=wal[0], mil=wal[1], eco=wal[2],
                    emaS=f(b.group(5)), emaI=f(b.group(6))))
        elif cat == "INTEL":
            g = INTEL.search(body)
            if g:
                out["intel"].append(dict(
                    t=round(t, 1), sightings=int(g.group(1)),
                    mil=int(g.group(2)), eco=int(g.group(3)),
                    struct=int(g.group(4)), strength=int(g.group(5)),
                    bases=int(g.group(6)), lastSeen=int(g.group(7)),
                    threat=int(g.group(8))))
        elif cat == "GOALS":
            refs = GOALS_REF.findall(body)
            for name, have, want, why in refs:
                out["refusals"][why.split("[")[0].strip()[:40]] += 1
            out["goals"].append(dict(t=round(t, 1), n=len(refs), text=body[:300]))
        elif cat == "POSTURE":
            out["posture"].append(dict(t=round(t, 1), text=body[:120]))
        elif cat == "PLAN":
            out["plan"].append(dict(t=round(t, 1), text=body[:160]))
        elif cat == "CLAIM":
            out["claim"].append(dict(t=round(t, 1), text=body[:160]))
        elif cat == "RESEARCH":
            out["research"].append(dict(t=round(t, 1), text=body[:80]))
        elif cat == "WAVE":
            m2 = W_OBJ.search(body)
            if m2:
                pending_obj = dict(x=int(m2.group(1)), z=int(m2.group(2)),
                                   why=m2.group(3)[:110], army=int(m2.group(4)),
                                   direct="striking direct" in body)
                continue
            m2 = W_LAUNCH.search(body)
            if m2:
                # group 3 is the real launched size and is present only in
                # logs written after 2026-09-12; before that the objective
                # line's `army N` was the only record of it.
                _size = int(m2.group(3)) if m2.group(3) else (
                    pending_obj["army"] if pending_obj else 0)
                w = dict(n=int(m2.group(1)), t=int(m2.group(2)),
                         minUnits=int(m2.group(4)), posture=m2.group(5),
                         army=_size,
                         x=pending_obj["x"] if pending_obj else 0,
                         z=pending_obj["z"] if pending_obj else 0,
                         why=pending_obj["why"] if pending_obj else "",
                         direct=bool(pending_obj and pending_obj["direct"]),
                         reinforced=0, arrived=0, spent=False, blocked=False)
                out["waves"].append(w)
                pending_obj = None
                continue
            m2 = W_TIMEOUT.search(body)
            if m2 and out["waves"]:
                out["waves"][-1]["timedOut"] = True
                out["waves"][-1]["endT"] = round(t, 1)
                continue
            m2 = W_REINF.search(body)
            if m2 and out["waves"]:
                n = int(m2.group(1))
                for w in reversed(out["waves"]):
                    if w["n"] == n:
                        w["reinforced"] += int(m2.group(2))
                        # The peak, not the last: arrivals fall as units die.
                        w["arrived"] = max(w["arrived"], int(m2.group(4)))
                        break
                continue
            m2 = W_SPENT.search(body)
            if m2:
                n = int(m2.group(1))
                for w in reversed(out["waves"]):
                    if w["n"] == n:
                        w["spent"] = True
                        w["endT"] = round(t, 1)
                        break
                continue
            m2 = W_HELD.search(body)
            if m2:
                out["waves"].append(dict(n=int(m2.group(1)), t=int(m2.group(2)),
                                         minUnits=0, posture="",
                                         army=0, x=0, z=0,
                                         why="held: %s/%s population"
                                             % (m2.group(3), m2.group(4)),
                                         direct=False, reinforced=0, arrived=0,
                                         spent=False, blocked=True))
                continue
            m2 = W_BLOCK.search(body)
            if m2:
                out["waves"].append(dict(n=int(m2.group(1)), t=int(m2.group(2)),
                                         minUnits=int(m2.group(3)), posture="",
                                         army=0, x=0, z=0, why="blocked: not enough idle",
                                         direct=False, reinforced=0, arrived=0,
                                         spent=False, blocked=True))
        elif cat == "CMD":
            out["cmds"][body.split(" ")[0][:24]] += 1
    out["cmds"] = dict(out["cmds"])
    out["refusals"] = dict(out["refusals"])
    return out


# -- one match record ------------------------------------------------------
def thin(rows, cap=400):
    """Evenly thin a time series to at most `cap` points, ALWAYS keeping the
    last one.

    A three-hour match samples 720 points per faction per metric, and a chart
    600 pixels wide cannot show them. Keeping the final point matters more
    than it sounds: every "where each faction finished" figure on the page
    reads it, and a naive stride would drop it and report the standing from
    two minutes before the end.
    """
    n = len(rows)
    if n <= cap:
        return rows
    step = (n + cap - 1) // cap
    out = rows[::step]
    if out[-1] is not rows[-1]:
        out.append(rows[-1])
    return out



CONFIG = re.compile(r"\[HeadlessMp\] CONFIG (.+)")


def config_of(peers):
    """What this run was actually testing. Prefers HeadlessMp's own CONFIG
    line (2026-09-12); falls back to what the match header can tell us, so
    runs recorded before that line existed still place on the matrix."""
    cfg = {}
    for p in peers:
        for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log"):
            for line in read_lines(os.path.join(p["dir"], cn)):
                m = CONFIG.search(line)
                if m:
                    for kv in m.group(1).split():
                        if "=" in kv:
                            k, _, v = kv.partition("=")
                            cfg[k] = v
                    return cfg
                if line.startswith("Map:") or " Age: " in line:
                    a = re.search(r"Age:\s*Age(\d)", line)
                    if a:
                        cfg.setdefault("age", a.group(1))
    return cfg


AUDIT = re.compile(r"\[(ResourceNodeCoverage|SupplyNodeBootstrap)\]\s*(.+)$")


def map_audit(host_dir):
    """What the map itself was short of.

    The node bootstraps audit every territory against the Regions.md quota on
    each boot and log the shortfall. Nothing read those lines, so an economy
    chart that flatlines looked like an AI problem when the map simply had no
    supply nodes to gather from. Since resources became authored-only there is
    no runtime seeding left to hide it, which makes this the first thing to
    check before blaming a brain.
    """
    out = []
    for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log"):
        for line in read_lines(os.path.join(host_dir, cn)):
            m = AUDIT.search(line)
            if not m:
                continue
            txt = m.group(2).strip()
            sev = "warn" if "WARNING" in line else "info"
            row = dict(src=m.group(1), text=txt[:200], sev=sev)
            if row not in out:
                out.append(row)
        if out:
            break
    return out[:24]


FRAME = re.compile(r"FRAME:\s*([\d.,]+)\s*ms")


def frame_health(host_dir):
    """How hard this peer was breathing.

    PerfSpikeLog records only frames over its threshold, so this is a census
    of the bad ones, not the average. It matters to the hunt because a peer
    that cannot keep up is a peer that may be dropped as lost, and a match
    that ends early for that reason should not be read as a clean run. Boot
    frames are excluded: the first seconds are terrain generation and prefab
    prewarm, and they are always enormous.
    """
    vals = []
    for line in read_lines(os.path.join(host_dir, "Perf.log")):
        m = FRAME.search(line)
        if not m:
            continue
        t = re.match(r"\[\s*([\d.,]+)s", line)
        if t and f(t.group(1)) < 25.0:
            continue                      # boot, not gameplay
        vals.append(f(m.group(1)))
    if not vals:
        return dict(spikes=0, worst=0.0, over100=0, over500=0, total=0.0)
    return dict(spikes=len(vals), worst=round(max(vals), 1),
                over100=sum(1 for v in vals if v >= 100),
                over500=sum(1 for v in vals if v >= 500),
                total=round(sum(vals) / 1000.0, 1))


WORLD = re.compile(r"covering world X\[(-?\d+)\.\.(-?\d+)\] Z\[(-?\d+)\.\.(-?\d+)\]")
FOOT = re.compile(r"(\d+)x(\d+),n\d+,u\d+,([A-Za-z_]+)")

# Map assets carry every resource node in NORMALISED coordinates; the nav-grid
# line in the console carries the world extent. Together they put the real map
# under a replay for matches recorded long before MapTrace existed.
_MAPINFO_CACHE = {}


def map_nodes(mapname, world):
    key = (mapname, tuple(world))
    if key in _MAPINFO_CACHE:
        return _MAPINFO_CACHE[key]
    out = []
    root = os.path.join("Assets", "GameData", "Scenes", "Maps")
    path = None
    if os.path.isdir(root):
        for folder in os.listdir(root):
            if folder.replace(" ", "") != mapname:
                continue
            for f in os.listdir(os.path.join(root, folder)):
                if f.endswith("MapInfo.asset"):
                    path = os.path.join(root, folder, f)
    if path:
        txt = chr(10).join(read_lines(path))
        w = float(world[2] - world[0]) or 1.0
        h = float(world[3] - world[1]) or 1.0
        for section, kind in (("IronDeposits", "iron"), ("VeilstoneNodes", "veilstone"),
                              ("VeilsteelNodes", "veilsteel"), ("SupplyNodes", "supply"),
                              ("CurseNodes", "well")):
            pat = section + ":" + chr(92) + "s*" + chr(92) + "n"
            pat += "((?:" + chr(92) + "s*-" + chr(92) + "s*"
            pat += chr(92) + "{[^}]*" + chr(92) + "}" + chr(92) + "s*"
            pat += chr(92) + "n)+)"
            m = re.search(pat, txt)
            if not m:
                continue
            for a, b in re.findall(r"x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+)", m.group(1)):
                out.append(dict(kind=kind,
                                x=round(world[0] + float(a) * w, 1),
                                z=round(world[1] + float(b) * h, 1)))
    _MAPINFO_CACHE[key] = out
    return out


# -- the actual map ---------------------------------------------------------
#
# THE REPLAY IS DRAWN ON THE REAL MAP (2026-09-12, operator: "there is no map
# image, bring back the old maps, i have requested this multiple times").
# Every map already ships a baked top-down render for the lobby -- the same
# picture the player picks the map from -- and it is framed on exactly the
# terrain bounds (MapLobbyImageBaker.GetMapBounds: terrain position + terrain
# size). The nav grid reports those same bounds as "covering world X[..] Z[..]",
# so the image drops onto the replay's own coordinate frame with no fitting.
#
# Embedded once per MAP and referenced by name -- forty matches on five maps
# would otherwise carry forty copies of the same picture.
MAP_ART = os.path.join("Assets", "GameData", "Scenes", "Maps")


def map_image(map_name):
    """Data URI for a map's baked top-down render, or None. `map_name` is the
    SCENE name (no spaces); the folder that holds it has them."""
    if not os.path.isdir(MAP_ART):
        return None
    for folder in sorted(os.listdir(MAP_ART)):
        full = os.path.join(MAP_ART, folder)
        if not os.path.isdir(full):
            continue
        if folder.replace(" ", "").lower() != map_name.replace(" ", "").lower():
            continue
        # Lobby first: it is the 768 px one and it is composed for viewing.
        for suffix in (" Lobby.png", " Thumbnail.png"):
            png = os.path.join(full, folder + suffix)
            if os.path.exists(png):
                with open(png, "rb") as fh:
                    b64 = base64.b64encode(fh.read()).decode("ascii")
                return "data:image/png;base64," + b64
    return None


def world_and_footprints(host_dir):
    """World extent and every building id's real footprint, both from the
    console: the nav-grid line and the cost-stamp lines. The stamp is the
    only place a footprint is written down outside the SOs."""
    world = None
    foot = {}
    for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log"):
        for line in read_lines(os.path.join(host_dir, cn)):
            if world is None and "covering world" in line:
                m = WORLD.search(line)
                if m:
                    world = [int(m.group(1)), int(m.group(3)),
                             int(m.group(2)), int(m.group(4))]
            if "CostStampSized" in line:
                for w, h, name in FOOT.findall(line):
                    if name:
                        foot[name] = [int(w), int(h)]
        if world:
            break
    return world, foot


# Trace line shapes. Fields are only ever APPENDED (MapTrace.cs header), so
# the optional tails below are what a 2026-10-03+ trace adds; an older trace
# simply leaves those groups empty.
#   B t id faction name x z w h [type cx cz cw ch yaw site]
#   U t id faction name [cls fl]
#   L t id level          C t id
TR_R = re.compile(r"^R (\d+) (\S+) (-?[\d.]+) (-?[\d.]+)")
TR_N = re.compile(r"^N (\S+) (\S+) (-?[\d.]+) (-?[\d.]+)")
TR_B = re.compile(r"^B ([\d.]+) (\S+) (\S+) (\S+) (-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+)"
                  r"(?: (\S+) (-?\d+|\*) (-?\d+|\*) (\d+) (\d+) (-?\d+) (\d+))?")
TR_U = re.compile(r"^U ([\d.]+) (\S+) (\S+) (\S+)(?: (-?\d+) (\d+))?")
TR_P = re.compile(r"^P ([\d.]+) (\S+) (-?[\d.]+) (-?[\d.]+) (\d+)")
TR_D = re.compile(r"^D ([\d.]+) (\S+)")
TR_L = re.compile(r"^L ([\d.]+) (\S+) (-?\d+)")
TR_C = re.compile(r"^C ([\d.]+) (\S+)")
# THE REAL TERRITORY PARTITION (2026-10-04; MapTrace.cs header). Written once:
#   TG cell x0 z0 w h          raster of RegionMap.RegionAt on the build grid
#   TR j id:n id:n ...         row j (south first), run-length encoded
# then as events:
#   TO t idx owner             owner change (faction name, Border, or "-")
#   TM t idx holder pct cont   the ownership meter, summarised
# A trace from before these lines simply has none, and the page then draws
# no territory layer.
TR_TG = re.compile(r"^TG ([\d.]+) (-?[\d.]+) (-?[\d.]+) (\d+) (\d+)")
TR_TO = re.compile(r"^TO ([\d.]+) (\d+) (\S+)")
TR_TM = re.compile(r"^TM ([\d.]+) (\d+) (\S+) (\d+) (\d)")
METER_CAP = 20000

# -- per-unit tracks (2026-10-03) ----------------------------------------------
# THE REPLAY IS A TRACK PER UNIT, NOT A FRAME PER SECOND. MapTrace records
# every unit once a second; the page used to get those as frames, thinned to a
# point budget -- with six hour-long matches on one page an army of 230 was
# drawn as 16-40 dots, frames seconds apart. Now every unit is kept, and what
# is thinned is the REDUNDANCY in each unit's own path: a unit walking a
# straight line at a steady pace needs two keyframes, not sixty.
#
# simplify_track is a time-aware Douglas-Peucker: a dropped sample is never
# further than `tol` metres from where linear interpolation IN TIME between
# the kept keyframes puts it -- the exact rule the page draws it by. Every
# sample where the flags change is kept (so moving / formation / fighting
# switch on the right second), and the first and last always are (spawn and
# death).
#
# ENCODING, per unit: [typeIndex, factionIndex, codes] with codes a flat list
# of (frameCode, dx, dz) triples, all integers:
#   frameCode = (frame index - previous frame index) * 8 + flags (0..7)
#   dx, dz    = position change in DECIMETRES (the trace's own 0.1 m)
# the first triple is absolute (frame index, x, z). Frame indices point into
# the match's `ft` list: frame times in deciseconds, delta-encoded. Small
# integers in JSON arrays compress very well over the wire.
FLAG_MASK = 7


def simplify_track(s, T, tol):
    """Indices of the samples to keep. s = [(frame, x_dm, z_dm, flags)],
    T = frame times in seconds, tol in metres."""
    n = len(s)
    if n <= 2:
        return list(range(n))
    keep = [False] * n
    keep[0] = keep[n - 1] = True
    for k in range(1, n):
        if s[k][3] != s[k - 1][3]:
            keep[k] = True
    tol2 = (tol * 10.0) ** 2
    anchors = [i for i in range(n) if keep[i]]
    stack = [(anchors[i], anchors[i + 1]) for i in range(len(anchors) - 1)
             if anchors[i + 1] - anchors[i] > 1]
    while stack:
        a, b = stack.pop()
        sa, sb = s[a], s[b]
        ta = T[sa[0]]
        span = (T[sb[0]] - ta) or 1e-9
        ax, az = sa[1], sa[2]
        dx, dz = sb[1] - ax, sb[2] - az
        best, bi = -1.0, -1
        for k in range(a + 1, b):
            sk = s[k]
            fr = (T[sk[0]] - ta) / span
            ex = sk[1] - ax - dx * fr
            ez = sk[2] - az - dz * fr
            e = ex * ex + ez * ez
            if e > best:
                best, bi = e, k
        if best > tol2:
            keep[bi] = True
            if bi - a > 1:
                stack.append((a, bi))
            if b - bi > 1:
                stack.append((bi, b))
    return [i for i in range(n) if keep[i]]


def encode_track(s, idx):
    out = []
    pf = px = pz = 0
    for i in idx:
        fi, x, z, fl = s[i]
        out.append((fi - pf) * 8 + (fl & FLAG_MASK))
        out.append(x - px)
        out.append(z - pz)
        pf, px, pz = fi, x, z
    return out


def decode_track(codes):
    s = []
    fi = x = z = 0
    for j in range(0, len(codes), 3):
        c = codes[j]
        fi += c >> 3
        x += codes[j + 1]
        z += codes[j + 2]
        s.append((fi, x, z, c & FLAG_MASK))
    return s


def frame_seconds(ft):
    """The `ft` list (deciseconds, delta-encoded) back to seconds."""
    out, acc = [], 0
    for d in ft:
        acc += d
        out.append(acc / 10.0)
    return out


def encode_all(samples, T, tol):
    enc = [encode_track(s, simplify_track(s, T, tol)) for s in samples]
    return enc, len(json.dumps(enc, separators=(",", ":")))


def retrack(rec, extra_tol):
    """Re-simplify a built record's tracks from their KEYFRAMES at a larger
    tolerance (twb-report.py, when one page or one file is still too big).
    The error bound is then the old tolerance plus the new one, and the
    record says so in trackInfo.tol."""
    if not rec.get("tracks"):
        return 0
    T = frame_seconds(rec.get("ft") or [])
    for tr in rec["tracks"]:
        s = decode_track(tr[2])
        tr[2] = encode_track(s, simplify_track(s, T, extra_tol))
    info = rec.get("trackInfo") or {}
    info["tol"] = round(info.get("tol", TRACK_TOL) + extra_tol, 2)
    info["keys"] = sum(len(t[2]) // 3 for t in rec["tracks"])
    rec["trackInfo"] = info
    return info["keys"]


# -- the 2 m build grid ------------------------------------------------------
# BuildGrid.cs: CellSize 2 m, anchored at the WORLD ORIGIN; a footprint of an
# odd cell count is centred on a cell centre, an even one on a cell boundary.
# Mirrored here so a trace written before MapTrace recorded its cells (and the
# sampled path, which never had them) can still be placed on the grid -- but
# only when the recorded position really IS the snapped one. A wall is drawn,
# not snapped (Build_Grid.md 5), and must never be forced onto cells.
GRID = 2.0
WALLISH = re.compile(r"wall|palisade|gate", re.I)


def grid_cells(x, z, w, h):
    """[minCellX, minCellZ, cellsX, cellsZ] when (x, z) is exactly where
    BuildGrid.Snap would put a w x h metre footprint, else None."""
    import math
    cw = max(1, int(math.ceil(w / GRID - 1e-6)))
    ch = max(1, int(math.ceil(h / GRID - 1e-6)))

    def snap(c, n):
        if n & 1:
            return math.floor(c / GRID) * GRID + GRID / 2
        return round(c / GRID) * GRID

    if abs(snap(x, cw) - x) > 0.15 or abs(snap(z, ch) - z) > 0.15:
        return None
    return [int(round((x - cw * GRID / 2) / GRID)),
            int(round((z - ch * GRID / 2) / GRID)), cw, ch]


def read_trace(host_dir):
    """MapTrace.txt: the full-fidelity feed. Units carry identity and state
    (moving / in formation / fighting), buildings carry real footprints, and
    the region partition and every node are written once at the top.

    Positions are kept PER UNIT (samples[id] = [(frame, x_dm, z_dm, flags)])
    at the trace's own 0.1 m -- never rounded to whole metres -- against one
    shared list of frame times in deciseconds (ftimes)."""
    path = os.path.join(host_dir, "MapTrace.txt")
    if not os.path.exists(path):
        return None
    regions, nodes, blds, units = [], [], {}, {}
    samples = defaultdict(list)
    fidx, ftimes = {}, []
    deaths = []
    levels = defaultdict(list)      # id -> [[t, level], ...]   (L lines)
    built = {}                      # id -> t construction completed (C lines)
    terr = None                     # TG/TR partition + TO/TM events
    try:
        fh = io.open(path, encoding="utf-8", errors="ignore")
    except Exception:
        return None
    with fh:
        for line in fh:
            # P is ~99% of the file: split, never regex, and never hold the
            # whole 70 MB file as a list of lines.
            if line.startswith("P "):
                p = line.split()
                if len(p) < 6:
                    continue
                try:
                    t = int(round(float(p[1].replace(",", ".")) * 10))
                    x = int(round(float(p[3].replace(",", ".")) * 10))
                    z = int(round(float(p[4].replace(",", ".")) * 10))
                    fl = int(p[5])
                except ValueError:
                    continue
                fi = fidx.get(t)
                if fi is None:
                    fi = fidx[t] = len(ftimes)
                    ftimes.append(t)
                samples[p[2]].append((fi, x, z, fl))
                continue
            line = line.rstrip("\r\n")
            if line.startswith("T"):
                if line.startswith("TM ") and terr is not None:
                    m = TR_TM.match(line)
                    if m:
                        terr["meter"].append([round(f(m.group(1)), 1), int(m.group(2)),
                                              m.group(3), int(m.group(4)), int(m.group(5))])
                    continue
                if line.startswith("TO ") and terr is not None:
                    m = TR_TO.match(line)
                    if m:
                        terr["own"].append([round(f(m.group(1)), 1), int(m.group(2)), m.group(3)])
                    continue
                if line.startswith("TR ") and terr is not None:
                    p = line.split()
                    try:
                        j = int(p[1])
                        runs = []
                        for tok in p[2:]:
                            a, _, b = tok.partition(":")
                            runs.append(int(a))
                            runs.append(int(b))
                    except (ValueError, IndexError):
                        continue
                    if 0 <= j < terr["h"]:
                        terr["rows"][j] = runs
                    continue
                if line.startswith("TG "):
                    m = TR_TG.match(line)
                    if m:
                        h = int(m.group(5))
                        terr = dict(cell=f(m.group(1)), x0=f(m.group(2)), z0=f(m.group(3)),
                                    w=int(m.group(4)), h=h, rows=[None] * h, own=[], meter=[])
                    continue
            m = TR_L.match(line)
            if m:
                levels[m.group(2)].append([round(f(m.group(1)), 1), int(m.group(3))])
                continue
            m = TR_U.match(line)
            if m:
                units[m.group(2)] = dict(f=m.group(3), name=m.group(4), t0=f(m.group(1)),
                                         cls=(int(m.group(5)) if m.group(5) else None),
                                         fl=(int(m.group(6)) if m.group(6) else 0))
                continue
            m = TR_B.match(line)
            if m:
                rec = dict(t0=f(m.group(1)), f=m.group(3), name=m.group(4),
                           x=f(m.group(5)), z=f(m.group(6)),
                           w=f(m.group(7)), h=f(m.group(8)))
                if m.group(9):
                    rec["type"] = m.group(9)
                    rec["cells"] = (None if m.group(10) == "*" else
                                    [int(m.group(10)), int(m.group(11)),
                                     int(m.group(12)), int(m.group(13))])
                    rec["yaw"] = int(m.group(14))
                    rec["site"] = int(m.group(15))
                blds[m.group(2)] = rec
                continue
            m = TR_C.match(line)
            if m:
                built[m.group(2)] = round(f(m.group(1)), 1)
                continue
            m = TR_D.match(line)
            if m:
                deaths.append([f(m.group(1)), m.group(2)])
                continue
            m = TR_R.match(line)
            if m:
                regions.append(dict(name=m.group(2).replace("_", " "),
                                    x=f(m.group(3)), z=f(m.group(4))))
                continue
            m = TR_N.match(line)
            if m:
                nodes.append(dict(kind=m.group(2), x=f(m.group(3)), z=f(m.group(4))))
    if not samples:
        return None
    return dict(regions=regions, nodes=nodes, buildings=blds, units=units,
                samples=samples, ftimes=ftimes, deaths=deaths,
                levels=dict(levels), built=built, terr=territory_of(terr))


def territory_of(terr):
    """The partition + ownership record for the page, or None (an older
    trace, or a raster with missing rows -- a hole in the partition would be
    drawn as ground nobody can own, which is a lie, so it is dropped whole).

    rows: per row, flat [id, n, id, n, ...] runs, south row first -- kept
    run-length encoded, the page decodes it once. own: [t, idx, owner] change
    events. meter: [t, idx, holder, pct, contested] -- if a very long match
    overflows METER_CAP, the 10-point steps are dropped first; holder and
    contested changes and the 0 / 100 endpoints are always kept."""
    if not terr:
        return None
    if any(r is None for r in terr["rows"]):
        print("  territory raster incomplete (%d of %d rows) -- layer dropped"
              % (sum(1 for r in terr["rows"] if r is not None), terr["h"]))
        return None
    meter = terr["meter"]
    if len(meter) > METER_CAP:
        last, keep = {}, []
        for e in meter:
            p = last.get(e[1])
            if p is None or p[2] != e[2] or p[4] != e[4] or e[3] in (0, 100):
                keep.append(e)
            last[e[1]] = e
        meter = keep[-METER_CAP:] if len(keep) > METER_CAP else keep
    terr["meter"] = meter
    return terr


def roster_of(hd):
    """Every faction's full roll -- each unit id and building id with its
    count -- at every 15 s metrics sample, as CHANGE EVENTS.

    Metrics_Units.csv / Metrics_Buildings.csv write a row per (sample,
    faction, id) with a positive count; a 3 h match is tens of thousands of
    rows that mostly repeat the previous sample. Only the changes are kept:
    [t, idIndex, newCount], with a 0 when an id drops out (or the faction is
    gone), so the page can rebuild the roll at any replay time by walking the
    list once. Ids share one string table across units and buildings."""
    ids, idx = [], {}

    def iid(s):
        if s not in idx:
            idx[s] = len(ids)
            ids.append(s)
        return idx[s]

    out = dict(ids=ids, ts=[], u={}, b={})
    for kind, fname, col in (("u", "Metrics_Units.csv", "unitId"),
                             ("b", "Metrics_Buildings.csv", "buildingId")):
        rows = read_csv(os.path.join(hd, fname))
        if not rows:
            continue
        per = defaultdict(lambda: defaultdict(dict))   # faction -> t -> {id: n}
        times = set()
        for r in rows:
            t = i_(r.get("t"))
            times.add(t)
            n = i_(r.get("count"))
            if n > 0:
                per[r.get("faction", "?")][t][r.get(col) or "unknown"] = n
        times = sorted(times)
        if len(times) > len(out["ts"]):
            out["ts"] = times
        for fa, by_t in per.items():
            prev, ch = {}, []
            for t in times:
                cur = by_t.get(t, {})
                for k, n in cur.items():
                    if prev.get(k) != n:
                        ch.append([t, iid(k), n])
                for k in prev:
                    if k not in cur:
                        ch.append([t, iid(k), 0])
                prev = cur
            out[kind][fa] = ch
    return out


# -- the curse's own story ---------------------------------------------------
# CurseTerritorySystem writes one console line per event -- MERGE, TAKEN,
# RAID, party lost, WAVE, CONQUEST -- with the sim time in front. That is the
# whole record of what the curse did in a match, and until 2026-09-13 the
# page did not show any of it while the curse was the thing being tuned.
CURSE_LINE = re.compile(r"^\[([\d.,]+)s\]\s+LOG:\s+\[CurseTerritory\]\s+(.*)$")


def curse_story(host_dir, deaths):
    events = []
    for cn in ("Console.log", "Console-1.log", "Console-2.log", "Console-3.log", "Console-4.log"):
        for line in read_lines(os.path.join(host_dir, cn)):
            m = CURSE_LINE.match(line)
            if not m:
                continue
            text = m.group(2).replace("—", "-").strip()
            kind = ("taken" if text.startswith("TAKEN") else
                    "merge" if text.startswith("MERGE") else
                    "raid" if text.startswith("RAID") else
                    "lost" if "lost" in text else
                    "wave" if text.startswith("WAVE") else "other")
            events.append(dict(t=int(f(m.group(1))), kind=kind, text=text[:160]))
        if events:
            break
    killed = sum(1 for d in deaths if d.get("v") == "Border")
    kills = sum(1 for d in deaths if d.get("k") == "Border")
    return dict(events=events[:400], killed=killed, kills=kills,
                taken=sum(1 for e in events if e["kind"] == "taken"),
                lost=sum(1 for e in events if e["kind"] == "lost"))

def summary_of(d):
    out = {}
    for line in read_lines(os.path.join(d, "Summary.txt")):
        if ":" in line and not line.startswith("==="):
            k, _, v = line.partition(":")
            out[k.strip()] = v.strip()

    # SUMMARY.TXT LIES ABOUT A WON MATCH (2026-09-24). MatchLogSession.End is
    # called from OnQuitting with the literal "quit", so a match that someone
    # WON reports the same outcome as one that hit a time limit -- which makes
    # the ledger useless for the only question an unlimited run asks: when
    # does a match solve itself. The runner says so plainly in the console;
    # believe that instead, and carry the simulated second it happened at.
    for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log",
               "Console-5.log", "Console-6.log", "Console-7.log", "Console-8.log"):
        for line in read_lines(os.path.join(d, cn)):
            m = re.search(r"match decided at (\d+)s\s*[^\w]*\s*(\w+) wins", line)
            if m:
                out["DecidedAt"] = int(m.group(1))
                out["Winner"] = m.group(2)
                out["Outcome"] = "%s wins" % m.group(2)
                return out
    return out


def economy_of(hd):
    """Gross income by source and spending by category, per faction, binned
    to whole minutes, from Metrics_Income.csv (written since 2026-10-04 by
    MatchMetrics from the game's EconomyLedger).

    Each CSV row is what moved during the sample period ENDING at t, so a row
    at t lands in minute floor((t-1)/60) -- the minute it happened in.

    Returns None for a match that predates the file, else
      {"minutes": N,
       "f": {faction: {"in":  {source:   [[s,i,v,vs], ... N]},
                       "out": {category: [[s,i,v,vs], ... N]}}}}
    with amounts rounded to whole units. A source a faction never touched is
    absent, which is what keeps the record small."""
    path = os.path.join(hd, "Metrics_Income.csv")
    if not os.path.exists(path):
        return None
    rows = read_csv(path)
    if not rows:
        return None
    acc = defaultdict(lambda: defaultdict(lambda: defaultdict(lambda: defaultdict(lambda: [0.0] * 4))))
    nmin = 0
    for r in rows:
        t = f(r.get("t"))
        m = max(0, int((t - 1) // 60))
        nmin = max(nmin, m + 1)
        flow = "out" if r.get("flow") == "out" else "in"
        cell = acc[r.get("faction", "?")][flow][r.get("source", "other")][m]
        for k, col in enumerate(("supplies", "iron", "veilstone", "veilsteel")):
            cell[k] += f(r.get(col))
    out = {}
    for fac, flows in acc.items():
        out[fac] = {}
        for flow, srcs in flows.items():
            out[fac][flow] = {}
            for src, mins in srcs.items():
                arr = [[0, 0, 0, 0] for _ in range(nmin)]
                for m, v in mins.items():
                    arr[m] = [int(round(x)) for x in v]
                if any(any(c) for c in arr):
                    out[fac][flow][src] = arr
    return dict(minutes=nmin, f=out)


def build(match):
    peers = match["peers"]
    host = next((p for p in peers if p["role"] == "host"), peers[0])
    hd = host["dir"]

    fac_rows = read_csv(os.path.join(hd, "Metrics_Faction.csv"))
    series = defaultdict(list)
    for r in fac_rows:
        series[r["faction"]].append(dict(
            t=i_(r["t"]), pop=i_(r["pop"]), popMax=i_(r["popMax"]),
            s=i_(r["supplies"]), i=i_(r["iron"]),
            v=i_(r["veilstone"]), vs=i_(r["veilsteel"]),
            terr=i_(r.get("territories")), units=i_(r["units"]),
            bld=i_(r["buildings"])))
    for k in series:
        series[k].sort(key=lambda x: x["t"])
        series[k] = thin(series[k])

    # ELIMINATIONS (2026-09-13). A dead faction stops appearing in the
    # faction metrics rather than reporting zeros, so "who died and when" was
    # only ever recoverable by noticing an absence -- which is how one
    # elimination was miscounted earlier in this project. Name it: a faction
    # whose last row is materially before the match's last row, or whose last
    # row shows nothing left, is out, at that row's time.
    tmax_all = max([s[-1]["t"] for s in series.values() if s] or [0])
    eliminated = {}
    for k, rows in series.items():
        if not rows:
            continue
        last = rows[-1]
        gone = (tmax_all - last["t"] > 45) or (
            (last.get("units") or 0) == 0 and (last.get("bld") or 0) == 0)
        if gone and tmax_all > 0:
            eliminated[k] = last["t"]

    # Replay frames. Positions are already integers; keep them that way.
    frames = defaultdict(lambda: defaultdict(list))
    for r in read_csv(os.path.join(hd, "Metrics_UnitPositions.csv")):
        frames[i_(r["t"])][r["faction"]].append([i_(r["x"]), i_(r["z"])])
    replay = [dict(t=t, u={fa: pts for fa, pts in fs.items()})
              for t, fs in sorted(frames.items())]

    # THIN, NEVER TRUNCATE -- AND SPEND THE BUDGET ON TIME, NOT ON DOTS.
    #
    # A three-hour match samples 720 position frames; forty of those in one
    # page is tens of megabytes and the artifact stops opening. The first
    # version dropped whole frames, which for a 3 h match meant one frame
    # every two minutes and units that teleport across the map -- useless for
    # the long runs that exist to be watched.
    #
    # Temporal resolution is what makes a replay readable, so the budget goes
    # there: cap how many units each faction contributes to a frame, keeping
    # an EVEN SPATIAL SPREAD rather than the first N (which would be whatever
    # order the chunks happened to be in, i.e. one corner of the map), and
    # only drop frames if that still is not enough. Cutting the tail is never
    # an option: it would hide exactly the late-game state these runs reach.
    POINT_BUDGET = SAMPLED_SHARE
    PER_FACTION_CAP = 70

    def _points(rs):
        return sum(len(p) for r in rs for p in r["u"].values())

    if _points(replay) > POINT_BUDGET:
        for fr in replay:
            for fa, pts in fr["u"].items():
                if len(pts) > PER_FACTION_CAP:
                    step = len(pts) / float(PER_FACTION_CAP)
                    fr["u"][fa] = [pts[int(i * step)] for i in range(PER_FACTION_CAP)]
    while len(replay) > 240 and _points(replay) > POINT_BUDGET:
        replay = replay[::2]

    builds = [dict(t=i_(r["t"]), f=r["faction"], id=r["buildingId"],
                   x=i_(r["x"]), z=i_(r["z"]), e=r["event"])
              for r in read_csv(os.path.join(hd, "Metrics_BuildingEvents.csv"))]
    builds.sort(key=lambda b: b["t"])

    # DEATHS GROW WITHOUT BOUND in a match with no limit, and nothing capped
    # them. Keep an even spread across the match rather than the first N, so
    # the late game -- the part an unlimited run exists to reach -- survives.
    deaths = [dict(t=i_(r["t"]), v=r["victim"], k=r["killer"],
                   x=i_(r["x"]), z=i_(r["z"]))
              for r in read_csv(os.path.join(hd, "Metrics_Deaths.csv"))]
    deaths.sort(key=lambda d: d["t"])
    deaths = thin(deaths, DEATH_CAP)

    # Map extent from whatever the match actually touched, padded. Reading it
    # from MapInfo would be better but that asset is not on the log path.
    xs = [p[0] for fr in replay for pts in fr["u"].values() for p in pts] \
        + [b["x"] for b in builds] + [d["x"] for d in deaths]
    zs = [p[1] for fr in replay for pts in fr["u"].values() for p in pts] \
        + [b["z"] for b in builds] + [d["z"] for d in deaths]
    if xs and zs:
        pad = 40
        ext = [min(xs) - pad, min(zs) - pad, max(xs) + pad, max(zs) + pad]
    else:
        ext = [-100, -100, 100, 100]

    # -- THE MAP ITSELF --------------------------------------------------
    world, foot = world_and_footprints(hd)
    trace = read_trace(hd)
    if world is None:
        world = ext                      # fall back to what moved
    nodes = trace["nodes"] if trace else map_nodes(match["map"], world)
    regions = trace["regions"] if trace else []
    utypes, utype, ulv = [], {}, {}
    tracks, tfac, frame_dt, track_info = None, [], [], None

    if trace:
        # PER-UNIT TRACKS (2026-10-03). Every unit the trace saw, each as its
        # own simplified path (simplify_track) -- no unit is sampled away,
        # whatever else is in the window. A unit's id is its index in
        # `tracks` (first-sighting order), so identity survives exactly as it
        # did when frames carried a renumbered id: the page interpolates a
        # unit only ever along ITS OWN keyframes.
        who = trace["units"]
        died = {u: t for t, u in trace["deaths"]}
        S = trace["samples"]
        T = [x / 10.0 for x in trace["ftimes"]]
        order = sorted(S.keys(), key=lambda u: (S[u][0][0], who.get(u, {}).get("t0", 0.0)))
        # UNIT TYPES, AS A PER-MATCH DICTIONARY: [name, class, flags] -- class
        # and flags are null/0 in a trace written before MapTrace recorded
        # them, and the page then infers the shape from the name alone.
        tix, fix, meta = {}, {}, []
        lv_all = trace.get("levels", {})
        for n, uid in enumerate(order):
            u = who.get(uid, {})
            key = (u.get("name", "?"), u.get("cls"), u.get("fl", 0))
            if key not in tix:
                tix[key] = len(utypes)
                utypes.append(list(key))
            fa = u.get("f", "?")
            if fa not in fix:
                fix[fa] = len(tfac)
                tfac.append(fa)
            meta.append((tix[key], fix[fa]))
            if uid in lv_all:
                ulv[n] = lv_all[uid][:40]
        raw = [S[u] for u in order]
        tol = TRACK_TOL
        enc, size = encode_all(raw, T, tol)
        while size > TRACK_CAP and tol < 64:
            print("  %s: tracks %.1f MB at %.2f m > cap %.1f MB -- raising the tolerance"
                  % (match["key"], size / 1e6, tol, TRACK_CAP / 1e6))
            tol *= 2
            enc, size = encode_all(raw, T, tol)
        tracks = [[a, b, c] for (a, b), c in zip(meta, enc)]
        fts = trace["ftimes"]
        frame_dt = ([fts[0]] + [fts[i] - fts[i - 1] for i in range(1, len(fts))]) if fts else []
        track_info = dict(tol=tol, units=len(order), frames=len(fts),
                          samples=sum(len(s) for s in raw),
                          keys=sum(len(c) // 3 for c in enc), bytes=size)
        frames = []

        built = trace.get("built", {})
        blds = []
        for k, b in trace["buildings"].items():
            rec = dict(t0=round(b["t0"], 1), t1=round(died[k], 1) if k in died else None,
                       f=b["f"], id=b["name"],
                       x=round(b["x"], 1), z=round(b["z"], 1),
                       w=int(b["w"]), h=int(b["h"]))
            if "type" in b:
                rec["ty"] = b["type"]
                rec["yaw"] = b["yaw"]
                if b["cells"]:
                    rec["g"] = b["cells"]
                if b["site"]:
                    # first seen as a site: when it completed (None = never)
                    rec["tc"] = built.get(k)
            elif not (WALLISH.search(b["name"]) or
                      (b["name"] == "building" and b["f"] != "Border")):
                # An old trace: derive the cells, but only where the recorded
                # position is exactly the snapped one.
                g = grid_cells(b["x"], b["z"], b["w"], b["h"])
                if g:
                    rec["g"] = g
            if k in lv_all:
                rec["lv"] = lv_all[k][:40]
            blds.append(rec)
        fidelity = "trace"
    else:
        # Reconstructed: 15 s position samples, no identity, no state, but
        # real footprints from the cost stamp and the real map underneath.
        # NO IDENTITY HERE, so id 0 on every point and the page must NOT
        # tween these -- see the note above. A 15 s sample snapped in place
        # reads as a slideshow, which is honest; tweening it reads as units
        # sprinting to a neighbour's position and back, which is a lie.
        frames = [dict(t=fr["t"], u={fa: [[0, p[0], p[1], 0] for p in pts]
                                     for fa, pts in fr["u"].items()})
                  for fr in replay]
        live, blds = {}, []
        for b in builds:
            key = (b["f"], b["id"], b["x"], b["z"])
            if b["e"] == "add":
                wh = foot.get(b["id"], [4, 4])
                rec = dict(t0=b["t"], t1=None, f=b["f"], id=b["id"], ty=b["id"],
                           x=b["x"], z=b["z"], w=wh[0], h=wh[1])
                # Metrics_BuildingEvents truncates to whole metres, and a
                # snapped centre always IS a whole metre, so the test is exact.
                if not WALLISH.search(b["id"]):
                    g = grid_cells(b["x"], b["z"], wh[0], wh[1])
                    if g:
                        rec["g"] = g
                live[key] = rec
                blds.append(rec)
            elif key in live:
                live[key]["t1"] = b["t"]
                del live[key]
        fidelity = "sampled"

    ai = {}
    for fa in FACTIONS:
        # The AI log for a faction lives on whichever peer ran that brain: the
        # host runs them all in MP, but a client keeps its own (thinner) copy.
        # Take the fullest.
        for p in peers:
            path = os.path.join(p["dir"], "AI_%s.log" % fa)
            if os.path.exists(path):
                a = parse_ai(path)
                if a["lines"] > ai.get(fa, {}).get("lines", -1):
                    ai[fa] = a
    ai = {k: v for k, v in ai.items() if v["lines"] > 0}
    # SHARED, LIKE THE REPLAY (2026-09-24). thin()'s default cap is 400 rows
    # PER FACTION PER SERIES -- eight factions on Veilmarch is 6,400 rows for
    # one match, and the window holds twelve. The AI's account of itself was
    # 2.5 MB of a 12 MB page and the second-largest thing in it, growing with
    # every match minute, which is how an unlimited run creeps into the
    # overflow guard even after the replay stops growing.
    for a in ai.values():
        for k in ("budget", "intel", "waves"):
            a[k] = thin(a[k], AI_SERIES_CAP)
        # Event lists are read as text, not plotted; keep the newest of each.
        for k in ("goals", "posture", "plan", "claim", "research"):
            if len(a[k]) > AI_EVENT_CAP:
                a[k] = a[k][-AI_EVENT_CAP:]

    # One flat, time-ordered wave list: the page shows the MATCH, not one
    # faction's view of it. Blocked attempts are dropped here -- they are in
    # the per-faction data for anyone chasing why a wave did not go.
    wavelist = []
    for _fa, _a in ai.items():
        for _w in _a.get("waves", []):
            if _w.get("blocked"):
                continue
            _r = dict(_w)
            _r["faction"] = _fa
            wavelist.append(_r)
    wavelist.sort(key=lambda w: w["t"])

    # What each faction actually FIELDED at the last sample -- an army of 18
    # Scholars and an army of 18 Swordsmen are the same number and not the
    # same army.
    _comp_rows = read_csv(os.path.join(hd, "Metrics_Units.csv"))
    composition = {}
    if _comp_rows:
        _tl = max(i_(r["t"]) for r in _comp_rows)
        _per = defaultdict(list)
        for r in _comp_rows:
            if i_(r["t"]) == _tl and i_(r["count"]) > 0:
                _per[r["faction"]].append([r["unitId"], i_(r["count"])])
        for k in _per:
            _per[k].sort(key=lambda x: -x[1])
        composition = dict(_per)

    # STRUCTURES, RESEARCH, PLACEMENT, COMBAT (2026-09-24). Absorbed from
    # batch-report.py / aggregate-metrics.py when the three overlapping
    # reports were folded into this one. The game writes all four and
    # nothing read them here, so every panel driven by them lived in a
    # separate tool reading the same folders.
    _bld_rows = read_csv(os.path.join(hd, "Metrics_Buildings.csv"))
    structures = {}
    if _bld_rows:
        _tl = max(i_(r["t"]) for r in _bld_rows)
        _per = defaultdict(list)
        for r in _bld_rows:
            if i_(r["t"]) == _tl and i_(r["count"]) > 0:
                _per[r["faction"]].append([r["buildingId"], i_(r["count"])])
        for k in _per:
            _per[k].sort(key=lambda x: -x[1])
        structures = dict(_per)

    research = defaultdict(list)
    for r in read_csv(os.path.join(hd, "Metrics_Research.csv")):
        t = (r.get("tech") or "").strip()
        if t:
            research[r.get("faction", "?")].append(t)
    for k in research:
        research[k].sort()

    placement = [dict(f=r.get("faction", "?"), id=r.get("buildingId", "?"),
                      x=f(r.get("x")), z=f(r.get("z")), r=i_(r.get("region"), -1))
                 for r in read_csv(os.path.join(hd, "Metrics_Placement.csv"))]

    combat = [dict(m=i_(r.get("minute")), f=r.get("faction", "?"),
                   k=i_(r.get("kills")), d=i_(r.get("deaths")))
              for r in read_csv(os.path.join(hd, "Metrics_Combat.csv"))]

    summ = summary_of(hd)
    # DID THE PEERS HOLD REAL TIME? The simulation is pinned to wall clock in
    # multiplayer, so a match of N ticks should take N/30 seconds. The ratio
    # of actual to ideal is the one number that says whether a peer was
    # starved -- far more useful than a spike count, which counts frames the
    # log threshold happened to catch. Above about 1.5 and a peer risks being
    # dropped as lost, which would end a match for a reason that has nothing
    # to do with determinism.
    dm = re.search(r"(?:(\d+)m\s*)?([\d.,]+)s", summ.get("Duration", ""))
    wall = (int(dm.group(1) or 0) * 60 + f(dm.group(2))) if dm else 0.0
    cfg = config_of(peers)
    ds = desync_of(match)
    ft = ds.get("forkTick") if ds.get("forkTick") is not None else ds.get("flagTick")
    cmds = commands_of(hd, ft)
    cmds["series"] = thin(cmds["series"])
    audit = map_audit(hd)
    perf = frame_health(hd)
    tmax = max([s[-1]["t"] for s in series.values() if s] or [0])
    # No metrics rows (an editor play session, or an old-format run): the
    # summary's Duration is the honest length, not 0:00 (2026-09-13).
    if tmax == 0:
        _dm = re.search(r"(?:(\d+)m\s*)?([\d.,]+)s", summ.get("Duration", ""))
        if _dm:
            tmax = int(int(_dm.group(1) or 0) * 60 + f(_dm.group(2)))

    return dict(
        key=match["key"], map=match["map"], stamp=match["stamp"],
        kind=match["kind"], peers=len(peers),
        outcome=summ.get("Outcome", "?"), duration=summ.get("Duration", "?"),
        decidedAt=summ.get("DecidedAt"), winner=summ.get("Winner", ""),
        build=summ.get("Build", "?"), exceptions=i_(summ.get("Exceptions")),
        errors=i_(summ.get("Errors")), warnings=i_(summ.get("Warnings")),
        tmax=tmax, extent=ext, config=cfg,
        world=world, nodes=nodes, regions=regions, blds=blds,
        # The game's real territory partition and its owners over time
        # (MapTrace TG/TR/TO/TM); None for a trace that predates them.
        terr=(trace or {}).get("terr"),
        frames=frames, fidelity=fidelity,
        # Per-unit tracks (traced matches; see "per-unit tracks"). `frames`
        # is then empty and only sampled matches carry it.
        tracks=tracks, ft=frame_dt, tfac=tfac, trackInfo=track_info,
        hasReplay=bool(tracks) or len(frames) > 1,
        waveList=wavelist, composition=composition, structures=structures,
        research={k: v for k, v in research.items()},
        placement=placement, combat=combat,
        desync=ds, series=dict(series), ai=ai, orders=cmds, audit=audit,
        perf=perf, wall=round(wall, 1),
        realtime=(round(wall / (ds["ticks"] / 30.0), 2)
                  if ds.get("ticks", 0) >= 900 and wall > 0 else 0),
        # NO `replay` KEY (2026-09-24). The page reads m.frames and has never
        # read m.replay -- zero references. It was 2.44 MB of a 14 MB page on
        # a 12-match window and grew with every match minute, which is how an
        # unlimited run walks into the build script's overflow guard and
        # starts discarding whole matches to fit.
        builds=builds, deaths=deaths, eliminated=eliminated,
        utypes=utypes, utype=utype, ulv=ulv, roster=roster_of(hd), grid=GRID,
        # Income by source / spending by category per minute; None for a
        # match recorded before Metrics_Income.csv existed.
        econ=economy_of(hd),
        curse=curse_story(hd, deaths))


# -- main ------------------------------------------------------------------
def main():
    folders = []
    for r in ROOTS:
        folders += scan(r)
    allmatches = group(folders)
    if SINCE:
        # Only this run gets a full record; older folders are still walked
        # below for the cheap verdict, so the history never shrinks.
        thisrun = [m for m in allmatches if m["stamp"] >= SINCE]
        matches = thisrun[:MAX_MATCHES]
        older = [m for m in allmatches if m not in matches]
    else:
        matches = allmatches[:MAX_MATCHES]
        older = allmatches[MAX_MATCHES:]
    # Divide the page's budgets among the matches that will be in it -- unless
    # each match is its own file (--per-match), when nothing is shared.
    global SAMPLED_SHARE, DEATH_CAP, AI_SERIES_CAP, AI_EVENT_CAP
    if PER_MATCH:
        SAMPLED_SHARE = 60000
        DEATH_CAP = 20000
        AI_SERIES_CAP = 400
        AI_EVENT_CAP = 300
    else:
        n = max(1, len(matches))
        SAMPLED_SHARE = max(12000, SAMPLED_TOTAL_BUDGET // n)
        DEATH_CAP = max(600, DEATH_TOTAL_BUDGET // n)
        AI_SERIES_CAP = max(80, AI_SERIES_TOTAL // n)
        AI_EVENT_CAP = max(60, 1800 // n)

    records = []
    records_tail = []      # cheap verdicts for everything past the window
    for m in matches:
        try:
            records.append(build(m))
        except Exception as e:
            records.append(dict(key=m["key"], map=m["map"], stamp=m["stamp"],
                                kind=m["kind"], peers=len(m["peers"]),
                                error="%s: %s" % (type(e).__name__, e),
                                outcome="?", duration="?", build="?",
                                exceptions=0, errors=0, warnings=0,
                                desync=dict(flagged=False, compared=0, ticks=0,
                                            dumps=[], forkTick=None,
                                            forkCols=[], flagTick=None, peers=0),
                                series={}, ai={}, replay=[], builds=[],
                                deaths=[], extent=[-100, -100, 100, 100],
                                tmax=0))

    # -- append-only history ----------------------------------------------
    # Matches past the page's window still get a CHEAP verdict: the console
    # line and the presence of a Desync_* dump. Reading every Lockstep.log on
    # disk to re-derive a per-tick verdict the history already holds would
    # cost minutes per refresh and answer nothing new -- but a fork that
    # scrolled out of the window must never scroll out of the TOTALS.
    for m in older:
        v = dict(flagged=False, flagTick=None, forkCols=[], dumps=[])
        for pr in m["peers"]:
            for cn in ("Console.log", "Console-2.log", "Console-3.log", "Console-4.log"):
                for line in read_lines(os.path.join(pr["dir"], cn)):
                    if "DESYNC at tick" in line:
                        v["flagged"] = True
                        mm = re.search(r"DESYNC at tick (\d+)", line)
                        if mm:
                            t = int(mm.group(1))
                            v["flagTick"] = t if v["flagTick"] is None else min(v["flagTick"], t)
                        mm = re.search(r"Forked subsystems: ([^.]+)", line)
                        if mm:
                            v["forkCols"] = [c.strip() for c in mm.group(1).split(",")]
            v["dumps"] += glob.glob(os.path.join(pr["dir"], "Desync_*"))
        # LENGTH AND OUTCOME ARE CHEAP; READ THEM (2026-09-13, operator:
        # "most of them have 0 length"). The verdict above deliberately skips
        # the per-tick Lockstep.log, but the match length is the LAST LINE of
        # Metrics_Faction.csv and the outcome is one line of Summary.txt --
        # neither costs anything, and without them every run past the window
        # showed a length of 0:00, which reads as "this match never ran".
        tail_tmax, tail_outcome = 0, "?"
        for pr in m["peers"]:
            mf = os.path.join(pr["dir"], "Metrics_Faction.csv")
            try:
                with open(mf, "rb") as fh:
                    fh.seek(0, 2)
                    back = min(fh.tell(), 4096)
                    fh.seek(-back, 2)
                    last = fh.read().decode("utf-8", "replace").strip().splitlines()
                    if last:
                        tail_tmax = max(tail_tmax, i_(last[-1].split(",")[0]))
            except Exception:
                pass
            summ = summary_of(pr["dir"])
            if tail_outcome == "?":
                tail_outcome = summ.get("Outcome", "?")
            # No metrics file at all -- an editor play session in the project
            # `logs` root, or an old-format run -- still has a Duration line in
            # its summary ("0m 41,4s"). Use it, so the ledger says 0:41 and
            # "quit" instead of 0:00 and "?", which reads as a match that
            # never happened.
            if tail_tmax == 0:
                dm = re.search(r"(?:(\d+)m\s*)?([\d.,]+)s", summ.get("Duration", ""))
                if dm:
                    tail_tmax = int(int(dm.group(1) or 0) * 60 + f(dm.group(2)))
        records_tail.append(dict(
            key=m["key"], stamp=m["stamp"], map=m["map"], kind=m["kind"],
            peers=len(m["peers"]), tmax=tail_tmax, outcome=tail_outcome,
            desync=dict(flagged=v["flagged"] or bool(v["dumps"]),
                        flagTick=v["flagTick"], forkTick=None,
                        forkCols=v["forkCols"], compared=0, ticks=0,
                        dumps=[os.path.basename(x) for x in v["dumps"]]),
            exceptions=0))

    hist = {}
    if os.path.exists(HISTORY):
        try:
            with io.open(HISTORY, encoding="utf-8") as fh:
                for row in json.load(fh).get("runs", []):
                    # Purge warm-up worlds recorded before scan() learned to
                    # tell them apart (2026-09-13): a "-N" key with no length
                    # and no fork is one, and it is not a run.
                    if row.get("key") in getattr(group, "warmkeys", set()):
                        continue
                    # A solo row with no length, no outcome and no fork is a
                    # warm-up recorded before the scanner could tell (its
                    # folder is gone, so the console cannot be re-read). A
                    # real run that ever sat in the full window would have
                    # its length recorded here; this one never did.
                    if row.get("kind") == "sp" and not row.get("tmax")                             and not row.get("forked")                             and row.get("outcome") in (None, "?", "", "unfinished / quit"):
                        continue
                    km = STAMP.match(row.get("key", ""))
                    if km and (km.group(3) or "").startswith("-")                             and not row.get("tmax") and not row.get("forked"):
                        continue
                    hist[row["key"]] = row
        except Exception:
            hist = {}
    for r in records + records_tail:
        d = r.get("desync") or {}
        prev = hist.get(r["key"], {})
        row = dict(key=r["key"], stamp=r["stamp"], map=r["map"], kind=r["kind"],
                   peers=r.get("peers", 1), tmax=r.get("tmax", 0),
                   outcome=r.get("outcome", "?"),
                   ticks=d.get("ticks", 0), compared=d.get("compared", 0),
                   forked=bool(d.get("flagged") or d.get("forkTick") is not None),
                   forkTick=d.get("forkTick") if d.get("forkTick") is not None
                            else d.get("flagTick"),
                   forkCols=d.get("forkCols", []),
                   config=r.get("config", {}),
                   exceptions=r.get("exceptions", 0))
        # A match still in flight reports fewer ticks than the finished one;
        # never let a later, thinner read shrink what was already recorded.
        if prev.get("compared", 0) > row["compared"]:
            row["compared"] = prev["compared"]
            row["ticks"] = max(row["ticks"], prev.get("ticks", 0))
        # Same rule for length and outcome: a match that has scrolled out of
        # the full window is re-read thinly every refresh, and that thin read
        # must never overwrite what a full read already recorded.
        if prev.get("tmax", 0) > row["tmax"]:
            row["tmax"] = prev["tmax"]
        if row["outcome"] in ("?", "") and prev.get("outcome") not in (None, "?", ""):
            row["outcome"] = prev["outcome"]
        if not row["config"] and prev.get("config"):
            row["config"] = prev["config"]
        if prev.get("forked"):
            row["forked"] = True
            row["forkTick"] = prev.get("forkTick", row["forkTick"])
            row["forkCols"] = prev.get("forkCols") or row["forkCols"]
        hist[r["key"]] = row
    runs = sorted(hist.values(), key=lambda x: x["stamp"], reverse=True)
    try:
        with io.open(HISTORY, "w", encoding="utf-8") as fh:
            json.dump(dict(updated=time.strftime("%Y-%m-%d %H:%M:%S"), runs=runs),
                      fh, separators=(",", ":"))
    except Exception:
        pass

    live = set(r["key"] for r in records)
    totals = dict(
        warmups=getattr(group, "warmups", 0),
        runs=len(runs),
        mp=sum(1 for r in runs if r["kind"] == "mp"),
        forks=sum(1 for r in runs if r["forked"]),
        compared=sum(r["compared"] for r in runs),
        simSeconds=sum(r["tmax"] for r in runs),
        since=(runs[-1]["stamp"] if runs else ""))
    # Only the rows the page cannot show in full: a fork whose folder is gone
    # still has to appear in the ledger, or the hunt forgets its own findings.
    pruned = [r for r in runs if r["key"] not in live and r["forked"]]

    # COVERAGE. A hunt reports what it tried; the useful half of that is what
    # it has NOT tried. Cells are counted over the whole history, so a
    # combination stays covered after its folder is pruned.
    cov = {}
    for r in runs:
        if r["kind"] != "mp":
            continue
        c = r.get("config") or {}
        cell = "%s|age%s|%s|%s" % (
            r["map"],
            c.get("age", "?"),
            "warm" if c.get("warm") == "1" else "fresh" if "warm" in c else "?",
            "monkey" if c.get("monkey") == "1" else "chaos" if "monkey" in c else "?")
        e = cov.setdefault(cell, dict(cell=cell, map=r["map"],
                                      age=c.get("age", "?"),
                                      warm=c.get("warm", "?"),
                                      monkey=c.get("monkey", "?"),
                                      runs=0, forks=0, ticks=0))
        e["runs"] += 1
        e["ticks"] += r["compared"]
        if r["forked"]:
            e["forks"] += 1
    coverage = sorted(cov.values(), key=lambda x: (x["map"], x["age"], x["cell"]))

    # One copy of each map's picture, keyed by the name the matches carry.
    art = {}
    for r in records:
        mn = r.get("map")
        if mn and mn not in art:
            img = map_image(mn)
            if img:
                art[mn] = img

    # EVERY RUN GOES TO THE PAGE (2026-09-13, operator: "200 matches I can't
    # see"). `matches` carries full replay data for the newest MAX_MATCHES
    # because that is all the page can hold; `history` carries a ~200-byte
    # verdict row for EVERY run the hunt has ever recorded, so the ledger on
    # the page is the whole ledger. `pruned` used to be the only tail the
    # page got, and it was forked-and-gone rows only -- the page never even
    # rendered it, so 160 of 200 matches were simply invisible.
    # The ledger follows the same cut as the replays when a run is selected;
    # the all-time totals stay whole so the number the hunt exists to
    # accumulate is never hidden.
    ledger = [r for r in runs if not SINCE or r["stamp"] >= SINCE]
    doc = dict(generated=time.strftime("%Y-%m-%d %H:%M:%S"),
               roots=ROOTS, matches=records, mapArt=art,
               history=ledger, live=sorted(live), since=SINCE,
               totals=totals, pruned=pruned[:40], coverage=coverage)
    with io.open(OUT, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, separators=(",", ":"))

    mp = [r for r in records if r.get("kind") == "mp"]
    bad = [r for r in mp if r["desync"]["flagged"] or r["desync"]["forkTick"] is not None]
    print("%s: %d matches (%d mp), %d with a fork | all time: %d runs, "
          "%d forks, %s tick rows | %.2f MB"
          % (OUT, len(records), len(mp), len(bad), totals["runs"],
             totals["forks"], format(totals["compared"], ","),
             os.path.getsize(OUT) / 1e6))


if __name__ == "__main__":
    main()
