#!/usr/bin/env python3
"""Aggregate a finished headless batch into one HTML report.

Drives every panel from the five CSVs MatchMetrics writes per session:
Faction, Units, Buildings, Research, Placement.

    python tools/batch-report.py <logs-dir> <out.html>
"""
import csv, os, re, sys, glob, json, time, html
from collections import defaultdict

LOGS, OUT = sys.argv[1], sys.argv[2]
TARGET, CEILING = 120, 200
STEP = 15
def _detect_limit(logs):
    t_max = 0.0
    for f in glob.glob(os.path.join(logs, "*", "Metrics_Faction.csv")):
        try:
            with open(f, newline="", encoding="utf-8", errors="ignore") as fh:
                for r in csv.DictReader(fh):
                    t_max = max(t_max, float(r["t"] or 0))
        except Exception:
            pass
    # Round up to a clean 5-minute boundary; 1200 when there is no data yet.
    import math as _m
    return max(1200, int(_m.ceil(t_max / 300.0)) * 300)
LIMIT = _detect_limit(sys.argv[1])
GRID = list(range(STEP, LIMIT + 1, STEP))
FACTIONS = ["Blue", "Green", "Red", "Yellow"]
FIDX = {f: i for i, f in enumerate(FACTIONS)}


def rows(path):
    if not os.path.exists(path):
        return []
    try:
        with open(path, newline="", encoding="utf-8", errors="ignore") as f:
            return list(csv.DictReader(f))
    except Exception:
        return []


def num(v, d=0.0):
    try:
        return float(v)
    except Exception:
        return d


sessions = sorted(glob.glob(os.path.join(LOGS, "*/")))

# ── LATEST VERSION ONLY (2026-08-31 directive) ──────────────────────────────
# Every session's Summary.txt names the build fingerprint it ran under. The
# dashboard shows ONLY the newest build's matches: mixing datasets from
# different code versions made every aggregate a blend of rule sets. Each new
# build's batch therefore REPLACES the dashboard content automatically.
def _fingerprint(d):
    try:
        with open(os.path.join(d, "Summary.txt"), encoding="utf-8", errors="ignore") as f:
            for line in f:
                if line.startswith("Fingerprint"):
                    return line.split(":", 1)[1].strip()
                if line.startswith("Build "):
                    _fingerprint.build = line.split(":", 1)[1].strip()
    except Exception:
        pass
    return None
_fingerprint.build = "?"

_fps = {s: _fingerprint(s) for s in sessions}

# The authoritative "current version" line: the exe's own build time. Any
# session STARTED after it ran on the current build — finished or not. The
# fingerprint check remains as the tie-breaker for finished sessions (it
# catches a manually swapped exe), but the exe mtime is what lets the very
# first LIVE matches of a fresh build replace the previous build's data
# immediately instead of waiting for one of them to finish.
import datetime
# Newest of the code DLL and the serialized game data: a data-only
# rebuild (an SO cost change) leaves the DLL untouched but is still a new
# version of the game.
_root = os.path.dirname(LOGS.rstrip("/\\"))
_cands = [os.path.join(_root, "The Waning Border_Data", "Managed", "TheWaningBorder.Runtime.dll"),
          os.path.join(_root, "The Waning Border_Data", "resources.assets")]
_cands = [c for c in _cands if os.path.exists(c)]
_exe = max(_cands, key=os.path.getmtime) if _cands else None
def _started(d):
    try:
        return datetime.datetime.strptime(
            os.path.basename(d.rstrip("/\\"))[:19], "%Y-%m-%d_%H-%M-%S")
    except Exception:
        return None
if _exe:
    _cut = datetime.datetime.fromtimestamp(os.path.getmtime(_exe))
    _current = [s for s in sessions
                if _started(s) and _started(s) >= _cut - datetime.timedelta(minutes=2)]
    if _current:
        dropped = len(sessions) - len(_current)
        live = sum(1 for s in _current if _fps[s] is None)
        sessions = _current
        BUILD_NAME = _fingerprint.build
        LATEST_FP = next((_fps[s] for s in reversed(sessions) if _fps[s]), None) or "live"
        print("current exe (built %s): %d session(s) (%d live); %d older excluded"
              % (_cut.strftime("%H:%M"), len(sessions), live, dropped))

LATEST_FP2 = next((_fps[s] for s in reversed(sessions) if _fps[s]), None)
LATEST_FP = LATEST_FP2 if '_current' not in dir() or not _current else LATEST_FP
BUILD_NAME = _fingerprint.build
if LATEST_FP and not (_exe and _current):
    # An IN-FLIGHT session has no Summary.txt yet (it is written at match
    # end), so it carries no fingerprint. For the live 5-minute updates those
    # must still show: include any unfinished session NEWER than the newest
    # fingerprinted one — in the consecutive-batch pipeline that is exactly
    # the running batch of the current build.
    newest_done = max((s for s in sessions if _fps[s]), key=lambda s: s)
    keep = [s for s in sessions
            if _fps[s] == LATEST_FP or (_fps[s] is None and s > newest_done)]
    dropped = len(sessions) - len(keep)
    live = sum(1 for s in keep if _fps[s] is None)
    sessions = keep
    print("latest build %s (fingerprint %s): %d session(s) (%d live); %d older excluded"
          % (BUILD_NAME, LATEST_FP, len(sessions), live, dropped))

def build_data(sessions):
    pop_s, cap_s, army_s = [], [], []
    RES_KEYS = ("Supplies", "Iron", "Veilstone", "Veilsteel")
    res_at = {k: defaultdict(list) for k in RES_KEYS}
    # Net bank flow per faction-match: bank delta over the trailing minute at
    # each grid step. The CSV records BANKS, not income, so this is income minus
    # spending — negative during a spend-down is signal, not error.
    flow_s = {k: [] for k in RES_KEYS}
    # Spend rate: the sum of bank DROPS between consecutive samples over the
    # trailing minute. Income landing in the same 15 s window as a purchase
    # offsets it, so this slightly undercounts — the best a bank ledger allows.
    spend_s = {k: [] for k in RES_KEYS}
    comp_end, bld_end = defaultdict(float), defaultdict(float)
    tech_done = defaultdict(int)
    places = []
    place_ids = []
    _pidx = {}

    def pid(bid):
        if bid not in _pidx:
            _pidx[bid] = len(place_ids)
            place_ids.append(bid)
        return _pidx[bid]
    peak_cap, final_units, peak_army, complete = [], [], [], 0
    aged, aged_seen, age_times = [], {}, []
    place_synth = False
    t_end_max = 0

    for d in sessions:
        frows = rows(os.path.join(d, "Metrics_Faction.csv"))
        if not frows:
            continue
        t_end = max(num(r["t"]) for r in frows)
        t_end_max = max(t_end_max, t_end)
        if t_end >= LIMIT - STEP * 2:
            complete += 1

        # Sample times DRIFT -- the sampler fires on a 15s budget but lands on
        # 601, 1052, 1174 and so on. An exact grid lookup therefore missed most
        # rows and reported every faction as never reaching the cap. Resample by
        # carrying the last sample at or before each grid time instead.
        by = defaultdict(list)
        for r in frows:
            f = r.get("faction")
            if f in FIDX:
                by[f].append((num(r["t"]), r))

        for f, samples in by.items():
            fi = FIDX[f]
            samples.sort(key=lambda tr: tr[0])
            t_last = samples[-1][0]

            p, c, a = [], [], []
            banks = {k: [] for k in RES_KEYS}
            j, cur = 0, None
            for t in GRID:
                while j < len(samples) and samples[j][0] <= t:
                    cur = samples[j][1]
                    j += 1
                # Past this faction's final sample the series ends rather than
                # flat-lining: a faction wiped out at 16 minutes has no 20-minute
                # value, and drawing one would invent data.
                if cur is None or t > t_last + STEP * 2:
                    p.append(None); c.append(None); a.append(None)
                    for k in RES_KEYS:
                        banks[k].append(None)
                    continue
                p.append(round(num(cur["pop"])))
                c.append(round(num(cur["popMax"])))
                a.append(round(num(cur["units"])))
                for k in res_at:
                    res_at[k][t].append(num(cur[k.lower()]))
                    banks[k].append(num(cur[k.lower()]))

            pop_s.append({"f": fi, "v": p})
            cap_s.append({"f": fi, "v": c})
            army_s.append({"f": fi, "v": a})

            # Trailing-minute delta: 60 s is 4 grid steps of 15 s.
            LAG = 60 // STEP
            for k in RES_KEYS:
                b = banks[k]
                fl, sp = [], []
                for i in range(len(b)):
                    if i < LAG or b[i] is None or b[i - LAG] is None:
                        fl.append(None)
                        sp.append(None)
                        continue
                    fl.append(round(b[i] - b[i - LAG]))
                    drop = 0.0
                    for j in range(i - LAG + 1, i + 1):
                        if b[j] is not None and b[j - 1] is not None and b[j] < b[j - 1]:
                            drop += b[j - 1] - b[j]
                    sp.append(round(drop))
                flow_s[k].append({"f": fi, "v": fl})
                spend_s[k].append({"f": fi, "v": sp})

            # Peaks and finals come from the RAW rows, not the resampled grid.
            peak_cap.append(max(num(r["popMax"]) for _, r in samples))
            peak_army.append(int(max(num(r["units"]) for _, r in samples)))
            final_units.append(num(samples[-1][1]["units"]))

        # Composition and building counts at the LAST sample of the match.
        for src, sink in ((("Metrics_Units.csv"), comp_end), (("Metrics_Buildings.csv"), bld_end)):
            rs = rows(os.path.join(d, src))
            if not rs:
                continue
            bt = max(num(r["t"]) for r in rs)
            key = "unitId" if "unitId" in rs[0] else "buildingId"
            for r in rs:
                if num(r["t"]) == bt:
                    sink[r[key]] += num(r["count"])

        for r in rows(os.path.join(d, "Metrics_Research.csv")):
            if r.get("faction") in FIDX:
                tech_done[r["tech"]] += 1

        # Age-up is the gate on the entire Age-1 tree, and no CSV records it.
        # The AI log does, once per faction that gets there.
        for lg in glob.glob(os.path.join(d, "AI_*.log")):
            f = os.path.basename(lg)[3:-4]
            if f not in FIDX:
                continue
            aged_seen[f] = aged_seen.get(f, 0)
            try:
                txt = open(lg, encoding="utf-8", errors="ignore").read()
            except Exception:
                continue
            m = re.search(r"\[(\d+):(\d+)[.,]\d\] CULTURE: age-up culture =", txt)
            if m:
                aged.append(f)
                age_times.append(int(m.group(1)) + int(m.group(2)) / 60.0)
            elif "aged up to era" in txt:
                aged.append(f)

        prows = rows(os.path.join(d, "Metrics_Placement.csv"))
        for r in prows:
            f = r.get("faction")
            if f in FIDX:
                bid = r.get("buildingId") or "unknown"
                places.append([round(num(r["x"])), round(num(r["z"])), FIDX[f],
                               1 if bid == "Hall" else 0, pid(bid)])
        # The end-state ledger AND the log reconstruction, always both: the
        # ledger only records what is STANDING at dump time, so an
        # eliminated faction's whole construction history vanished from the
        # map and the replay. The logs keep its claims and extractors; the
        # duplicate dots where both sources agree are harmless overdraw.
        if True:
            # The placement ledger is written only at the match's FINAL dump, so
            # a session still in flight has none and the map went blank mid-run.
            # Reconstruct what the AI logs record live — extractors on nodes and
            # claimed Halls carry positions. Partial by construction (base ring
            # buildings log no position), and flagged so the page says so.
            for lg in glob.glob(os.path.join(d, "AI_*.log")):
                f = os.path.basename(lg)[3:-4]
                if f not in FIDX:
                    continue
                try:
                    txt = open(lg, encoding="utf-8", errors="ignore").read()
                except Exception:
                    continue
                for m in re.finditer(r"EXTRACT: (\S+) on a free node at \((-?\d+),(-?\d+)\)", txt):
                    places.append([int(m.group(2)), int(m.group(3)), FIDX[f], 0,
                                   pid(m.group(1))])
                    place_synth = True
                for m in re.finditer(r"CLAIM: claiming .+? at \((-?\d+),(-?\d+)\)", txt):
                    places.append([int(m.group(1)), int(m.group(2)), FIDX[f], 1,
                                   pid("Hall")])
                    place_synth = True


    # ── combat: kills and deaths per minute, mean across matches ────────────
    minutes_n = LIMIT // 60
    kills_at = defaultdict(lambda: [0.0] * minutes_n)   # faction -> per-minute
    deaths_at = defaultdict(lambda: [0.0] * minutes_n)
    combat_sessions = 0
    for d in sessions:
        rs = rows(os.path.join(d, "Metrics_Combat.csv"))
        if not rs:
            continue
        combat_sessions += 1
        for r in rs:
            f = r.get("faction")
            if f not in FIDX:
                continue
            m = int(num(r.get("minute")))
            if m < 0 or m >= minutes_n:
                continue
            kills_at[f][m] += num(r.get("kills"))
            deaths_at[f][m] += num(r.get("deaths"))
    if combat_sessions:
        for f in list(kills_at):
            kills_at[f] = [round(v / combat_sessions, 2) for v in kills_at[f]]
        for f in list(deaths_at):
            deaths_at[f] = [round(v / combat_sessions, 2) for v in deaths_at[f]]


    def median(xs):
        xs = sorted(x for x in xs if x is not None)
        return xs[len(xs) // 2] if xs else 0


    res_series = {k: [round(median(res_at[k].get(t, [])), 1) for t in GRID] for k in res_at}

    # A rich (-twbRich) run pins every bank at the cap, so nothing about costs,
    # income or pacing can be read from it. Detect that from the data rather than
    # take a flag someone has to remember to pass.
    RICH_CAP = 100000
    _su = [v for v in res_series["Supplies"][2:] if v]
    rich = bool(_su) and all(v >= RICH_CAP * 0.99 for v in _su)

    n_fm = len(pop_s) or 1                       # faction-matches
    hit = sum(1 for c in peak_cap if c >= TARGET)
    zero = sum(1 for u in final_units if u < 1)

    # ── Expenditure by category (2026-08-30) ────────────────────────────────
    # Priced from the SO cost data on disk (the same assets TechCatalog loads,
    # so the report cannot drift from the game), quantities from count DELTAS
    # in the Buildings/Units ledgers — a count rising by N at time t is N
    # bought. The first sample's holdings are the free starting assets.
    # Research has no timestamps in its ledger, so it is totals-only.
    # Upgrades and refunds are not ledgered anywhere; they are absent here.
    def load_costs():
        root = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "..", "Assets", "GameData", "TechTree")
        costs = {}
        for f in glob.glob(os.path.join(root, "**", "*.asset"), recursive=True):
            try:
                txt = open(f, encoding="utf-8", errors="ignore").read()
            except Exception:
                continue
            m = re.search(r"^  id: '?([^'\n]+?)'?\s*$", txt, re.M)
            c = re.search(r"^  cost:\s*\n    Supplies: (-?\d+)\n    Iron: (-?\d+)"
                          r"\n    Veilstone: (-?\d+)\n    Veilsteel: (-?\d+)", txt, re.M)
            if m and c:
                costs[m.group(1).strip()] = tuple(int(c.group(i)) for i in range(1, 5))
        return costs


    COSTS = load_costs()
    CAT_MIN_N = LIMIT // 60
    cat_min = {c: {k: [0.0] * CAT_MIN_N for k in RES_KEYS} for c in ("Buildings", "Army")}
    cat_tot = {c: {k: 0.0 for k in RES_KEYS} for c in ("Buildings", "Army", "Research")}


    def accumulate_category(d, csvname, idkey, cat):
        rs = rows(os.path.join(d, csvname))
        if not rs:
            return
        bytime = defaultdict(list)
        for r in rs:
            bytime[num(r["t"])].append(r)
        ts = sorted(bytime)
        t0 = ts[0]
        prev = {}
        for t in ts:
            for r in bytime[t]:
                f = r.get("faction")
                if f not in FIDX:
                    continue
                key = (f, r[idkey])
                cnt = num(r["count"])
                if t == t0:
                    prev[key] = cnt
                    continue
                delta = cnt - prev.get(key, 0.0)
                prev[key] = cnt
                if delta <= 0:
                    continue
                c = COSTS.get(r[idkey])
                if not c:
                    continue
                mi = min(int(t) // 60, CAT_MIN_N - 1)
                for ri, k in enumerate(RES_KEYS):
                    amt = delta * c[ri]
                    cat_tot[cat][k] += amt
                    cat_min[cat][k][mi] += amt


    for d in sessions:
        accumulate_category(d, "Metrics_Buildings.csv", "buildingId", "Buildings")
        accumulate_category(d, "Metrics_Units.csv", "unitId", "Army")
        for r in rows(os.path.join(d, "Metrics_Research.csv")):
            c = COSTS.get((r.get("tech") or "").strip())
            if c and r.get("faction") in FIDX:
                for ri, k in enumerate(RES_KEYS):
                    cat_tot["Research"][k] += c[ri]

    for cat in cat_min:
        for k in RES_KEYS:
            cat_min[cat][k] = [round(v / n_fm, 1) for v in cat_min[cat][k]]
    cat_totals = [[cat] + [round(cat_tot[cat][k] / n_fm) for k in RES_KEYS]
                  for cat in ("Buildings", "Army", "Research")]

    # Per-minute building counts per faction/type — the replay's clock.
    # Single-session only: replaying eight overlaid matches means nothing.
    bld_min = None
    if len(sessions) == 1:
        mn = LIMIT // 60
        bld_min = {}
        rs = rows(os.path.join(sessions[0], "Metrics_Buildings.csv"))
        series = defaultdict(list)
        for r in rs:
            f = r.get("faction")
            if f in FIDX:
                series[(f, r["buildingId"])].append((num(r["t"]), num(r["count"])))
        for (f, bid), samples in series.items():
            samples.sort()
            out = [0] * mn
            j, cur = 0, 0
            for mi in range(mn):
                tt = (mi + 1) * 60
                while j < len(samples) and samples[j][0] <= tt:
                    cur = samples[j][1]
                    j += 1
                out[mi] = int(cur)
            bld_min.setdefault(f, {})[bid] = out

    # Unit-position frames and death events for the replay — from the two
    # streamed ledgers (2026-08-30). Absent in sessions recorded by an older
    # build; the panel says so. Single-session only, like bld_min.
    deaths = None
    unit_frames = None
    bld_events = None
    if len(sessions) == 1:
        ers = rows(os.path.join(sessions[0], "Metrics_BuildingEvents.csv"))
        if ers:
            # The exact ledger (2026-08-31): every building's appearance and
            # destruction with time and position — replaces the reveal
            # inference wholesale, eliminated factions included.
            bld_events = [[int(num(r["t"]) // 60), round(num(r["x"])),
                           round(num(r["z"])), FIDX[r["faction"]],
                           1 if r.get("buildingId") == "Hall" else 0,
                           1 if r.get("event") == "add" else -1]
                          for r in ers if r.get("faction") in FIDX]
        drs = rows(os.path.join(sessions[0], "Metrics_Deaths.csv"))
        if drs:
            deaths = [[int(num(r["t"]) // 60), round(num(r["x"])), round(num(r["z"])),
                       FIDX[r["victim"]]]
                      for r in drs if r.get("victim") in FIDX]
        prs = rows(os.path.join(sessions[0], "Metrics_UnitPositions.csv"))
        if prs:
            mn = LIMIT // 60
            # Positions stream every 30 s; a frame is the LATEST sample of
            # its minute per faction, binned to 8 m so a formation reads as
            # one weighted blip instead of eighty.
            latest_t = {}
            for r in prs:
                f = r.get("faction")
                if f not in FIDX:
                    continue
                mi = min(int(num(r["t"]) // 60), mn - 1)
                tt = num(r["t"])
                if latest_t.get((mi, f), -1) < tt:
                    latest_t[(mi, f)] = tt
            bins = {}
            for r in prs:
                f = r.get("faction")
                if f not in FIDX:
                    continue
                mi = min(int(num(r["t"]) // 60), mn - 1)
                if num(r["t"]) != latest_t.get((mi, f)):
                    continue
                bx = int(num(r["x"])) // 8
                bz = int(num(r["z"])) // 8
                k = (mi, FIDX[f], bx, bz)
                bins[k] = bins.get(k, 0) + 1
            unit_frames = [[] for _ in range(mn)]
            for (mi, fi, bx, bz), n in bins.items():
                unit_frames[mi].append([bx * 8 + 4, bz * 8 + 4, fi, n])

    tot_u = sum(comp_end.values()) or 1
    comp = sorted(((k, 100.0 * v / tot_u) for k, v in comp_end.items()), key=lambda kv: -kv[1])[:10]
    bld = sorted(((k, v / n_fm) for k, v in bld_end.items()), key=lambda kv: -kv[1])[:12]
    tech = sorted(((k, 100.0 * v / n_fm) for k, v in tech_done.items()), key=lambda kv: -kv[1])[:14]

    # ── Which map was this batch played on? ─────────────────────────────────
    # HeadlessBatch logs "map <SceneName>" once per match; the baked MapInfo
    # thumbnail for that map (top-down ortho, +Z up, region lattice burned in)
    # then becomes the placement chart's background. World size per scene is
    # needed to pin dots to the image: generated terrains are centred on the
    # origin, so extent = +/- size/2.
    import base64
    MAP_SIZE = {"Veilmarch": 1024, "SunderedCrown": 512, "TwinSpans": 352,
                "SunderedReach": 704}
    # The session folder name is "<stamp>_<SceneName>[-instance]" (MatchLogSession
    # appends the scene), which is a far more reliable source than grepping logs.
    map_name, map_img, map_half = None, None, 0
    for d in sessions:
        base = os.path.basename(d.rstrip("/\\"))
        parts = base.split("_", 2)
        if len(parts) == 3 and parts[2]:
            map_name = re.sub(r"-\d+$", "", parts[2])
            break
    map_nodes = {}
    map_starts = {}
    if map_name and map_name in MAP_SIZE:
        map_half = MAP_SIZE[map_name] / 2.0
        for f in glob.glob(os.path.join(
                "Assets", "GameData", "Scenes", "Maps", "*", map_name + " Thumbnail.png")):
            with open(f, "rb") as fh:
                map_img = "data:image/png;base64," + base64.b64encode(fh.read()).decode()
            break

        # Resource locations, from the map's baked MapInfo — the same normalized
        # {x, y} arrays the lobby reads, converted to world metres the way the
        # placement dots are. AUTHORED nodes only: the runtime quota top-up
        # (SupplyNodeBootstrap / ResourceNodeCoverage) spawns extras that are not
        # in the bake until the map is rebuilt and rebaked in the editor.
        NODE_FIELDS = {"IronDeposits": "Iron", "VeilstoneNodes": "Veilstone",
                       "VeilsteelNodes": "Veilsteel", "SupplyNodes": "Supply",
                       "CurseNodes": "Curse well", "PlayerStarts": "__starts"}
        start_factions = ""
        for f in glob.glob(os.path.join(
                "Assets", "GameData", "Scenes", "Maps", "*", map_name + " MapInfo.asset")):
            cur = None
            for line in open(f, encoding="utf-8", errors="ignore"):
                m = re.match(r"^  (\w+):\s*$", line)
                if m:
                    cur = NODE_FIELDS.get(m.group(1))
                    continue
                m = re.match(r"^  PlayerStartFactions: ([0-9a-fA-F]+)", line)
                if m:
                    start_factions = m.group(1)
                    cur = None
                    continue
                m = re.match(r"^  - \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}", line)
                if m and cur:
                    map_nodes.setdefault(cur, []).append(
                        [round((float(m.group(1)) - 0.5) * 2 * map_half),
                         round((float(m.group(2)) - 0.5) * 2 * map_half)])
                elif not m and not line.startswith("  - "):
                    cur = None
            break

        # Start positions by faction index: PlayerStarts pairs are baked in
        # the canonical order PlayerStartFactions indexes into
        # (docs/Design/Lobby_Setup.md).
        raw_starts = map_nodes.pop("__starts", [])
        fmap = {0: "Blue", 1: "Red", 2: "Green", 3: "Yellow"}
        for i, pt in enumerate(raw_starts):
            if i * 2 + 1 < len(start_factions):
                fname = fmap.get(int(start_factions[i * 2:i * 2 + 2], 16))
                if fname in FIDX:
                    map_starts[FIDX[fname]] = pt

        # Log-reconstructed sessions know Halls only from CLAIM lines — the
        # STARTING Hall is spawned, never claimed, so a faction that had not
        # expanded yet showed no rings at all. Every faction always has its
        # home: synthesize it from the baked start positions.
        for fi_s, pt in map_starts.items():
            places.append([pt[0], pt[1], int(fi_s), 1, pid("Hall")])

    # ── How each match ENDED (2026-08-30) ───────────────────────────────────
    # A match now ends the moment one side is left standing (HeadlessBatch's
    # victory exit); the time limit is only the stalemate guard. Summary.txt's
    # Outcome line is the authoritative record: "<Faction> WINS (winner=...)"
    # for a decided match, "quit" for a guard-capped one, and no Summary at all
    # for a session still in flight.
    outcomes = []
    for d in sessions:
        frows = rows(os.path.join(d, "Metrics_Faction.csv"))
        if not frows:
            continue
        t_end = int(max(num(r["t"]) for r in frows))
        name = os.path.basename(d.rstrip("/\\"))
        out, seed = "", ""
        sp = os.path.join(d, "Summary.txt")
        in_flight = not os.path.exists(sp)
        if not in_flight:
            try:
                txt = open(sp, encoding="utf-8", errors="ignore").read()
                m = re.search(r"Outcome\s*:\s*(.+)", txt)
                out = m.group(1).strip() if m else "?"
            except Exception:
                out = "?"
            # The app-quit hook can overwrite a recorded victory with "quit";
            # the AI logs' GAME OVER line is the authoritative record.
            if "WINS" not in out and "VICTORY" not in out and "DEFEAT" not in out:
                for lg in glob.glob(os.path.join(d, "AI_*.log")):
                    try:
                        gm = re.search(r"GAME OVER: .*winner=(\w+)",
                                       open(lg, encoding="utf-8", errors="ignore").read())
                    except Exception:
                        gm = None
                    if gm:
                        out = gm.group(1) + " WINS"
                        break
        cp = os.path.join(d, "Console.log")
        if os.path.exists(cp):
            try:
                m = re.search(r"Seed:\s*(\d+)",
                              open(cp, encoding="utf-8", errors="ignore").read(500))
                seed = m.group(1) if m else ""
            except Exception:
                pass
        outcomes.append({
            "s": name, "seed": seed, "t": t_end,
            "out": "in flight" if in_flight else out,
            "decided": ("WINS" in out) or ("VICTORY" in out) or ("DEFEAT" in out),
        })

    DATA = {
        "grid": GRID, "target": TARGET, "ceiling": CEILING,
        "outcomes": outcomes,
        "mapName": map_name, "mapImg": map_img, "mapHalf": map_half,
        "mapNodes": map_nodes,
        "kills": dict(kills_at), "deaths": dict(deaths_at),
        "combatN": combat_sessions,
        "pop": pop_s, "cap": cap_s, "army": army_s, "res": res_series,
        "flow": flow_s, "spend": spend_s,
        "catSpend": cat_min, "catTotals": cat_totals,
            "comp": comp, "bld": bld, "tech": tech, "place": places,
        "placeSynth": place_synth, "placeIds": place_ids,
        "starts": map_starts, "bldMin": bld_min,
        "deaths": deaths, "unitFrames": unit_frames, "bldEvents": bld_events,
        "endMin": int(t_end_max // 60) + 1,
        "rich": rich,
        "verdict": {
            "matches": len(sessions), "complete": complete, "fm": n_fm,
            "hit": hit, "pct": round(100.0 * hit / n_fm),
            "medCap": median(peak_cap), "medArmy": median(final_units), "zero": zero,
            "aged": len(aged), "agedPct": round(100.0 * len(aged) / n_fm),
            "ageMedianMin": round(median(age_times), 1) if age_times else 0,
            "peakArmy": max(peak_army) if peak_army else 0,
            "decided": sum(1 for o in outcomes if o["decided"]),
        },
    }

    return DATA


def _dataset_key(d):
    return os.path.basename(d.rstrip("/\\"))


# One dataset per match plus the aggregate, so the page can render any
# single session on its own. The grid/LIMIT stay global so every dataset
# shares the same axes and switching matches never rescales time.
ALL = build_data(sessions)
DATASETS = {"All matches": ALL}
for _s in sessions:
    try:
        DATASETS[_dataset_key(_s)] = build_data([_s])
    except Exception as _e:
        print("per-session dataset failed:", _dataset_key(_s), _e)

# The map thumbnail is identical in every dataset — ship it once.
MAPIMG = ALL.get("mapImg") or ""
for _v in DATASETS.values():
    _v["mapImg"] = None
DATA = ALL
rich = ALL.get("rich", False)
places = ALL["place"]

V = DATA["verdict"]
doc = """<title>Waning Border Batch Watch</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Archivo:wght@500;600;700&family=JetBrains+Mono:wght@400;600&family=Source+Sans+3:wght@400;600&display=swap">

<style>
:root{
  --ink:#12161C; --ink-2:#3C4654; --ink-3:#6A7686;
  --ground:#F4F6F9; --surface:#FFFFFF; --surface-2:#EAEEF4;
  --line:#D6DDE7; --line-soft:#E6EBF2;
  --accent:#5B49B8; --accent-soft:#E9E5F8;
  --good:#2E8B57; --warn:#B87514; --crit:#C0392F;
  --f-blue:#3B6FC4; --f-green:#3E8C5A; --f-red:#B44139; --f-yellow:#B08420;
  --grid:#DDE3EC;
}
@media (prefers-color-scheme:dark){
  :root:not([data-theme="light"]){
    --ink:#E4E9F0; --ink-2:#A8B3C2; --ink-3:#75808F;
    --ground:#0E1116; --surface:#161B22; --surface-2:#1D242E;
    --line:#2A323D; --line-soft:#222933;
    --accent:#9585E4; --accent-soft:#241F3D;
    --good:#4FB477; --warn:#D9A03F; --crit:#E0605A;
    --f-blue:#5C8FE0; --f-green:#57B27A; --f-red:#D2635A; --f-yellow:#D6AA46;
    --grid:#242C36;
  }
}
:root[data-theme="dark"]{
  --ink:#E4E9F0; --ink-2:#A8B3C2; --ink-3:#75808F;
  --ground:#0E1116; --surface:#161B22; --surface-2:#1D242E;
  --line:#2A323D; --line-soft:#222933;
  --accent:#9585E4; --accent-soft:#241F3D;
  --good:#4FB477; --warn:#D9A03F; --crit:#E0605A;
  --f-blue:#5C8FE0; --f-green:#57B27A; --f-red:#D2635A; --f-yellow:#D6AA46;
  --grid:#242C36;
}

*{box-sizing:border-box}
body{
  margin:0; background:var(--ground); color:var(--ink);
  font-family:"Source Sans 3",ui-sans-serif,system-ui,sans-serif;
  font-size:15px; line-height:1.55;
}
.wrap{max-width:1180px; margin:0 auto; padding:40px 24px 72px}

header{border-bottom:1px solid var(--line); padding-bottom:22px; margin-bottom:28px}
.eyebrow{
  font-family:"JetBrains Mono",ui-monospace,monospace; font-size:11px;
  letter-spacing:.14em; text-transform:uppercase; color:var(--accent); margin:0 0 10px
}
h1{
  font-family:Archivo,ui-sans-serif,sans-serif; font-weight:700; font-size:clamp(30px,4.4vw,46px);
  letter-spacing:-.022em; line-height:1.04; margin:0 0 12px; text-wrap:balance
}
.sub{margin:0; color:var(--ink-2); max-width:66ch}
.warnbar{
  margin-top:18px; padding:10px 14px; border-left:3px solid var(--good);
  background:var(--surface); color:var(--ink-2); font-size:13.5px
}
.warnbar strong{color:var(--ink); font-weight:600}

h2{
  font-family:Archivo,ui-sans-serif,sans-serif; font-weight:600; font-size:13px;
  letter-spacing:.1em; text-transform:uppercase; color:var(--ink-3);
  margin:38px 0 14px; padding-bottom:8px; border-bottom:1px solid var(--line-soft)
}

.verdicts{display:grid; grid-template-columns:repeat(auto-fit,minmax(215px,1fr)); gap:14px}
.card{
  background:var(--surface); border:1px solid var(--line); border-radius:6px;
  padding:16px 18px; display:flex; flex-direction:column; gap:6px
}
.card .lbl{
  font-family:"JetBrains Mono",monospace; font-size:10.5px; letter-spacing:.1em;
  text-transform:uppercase; color:var(--ink-3)
}
.card .val{
  font-family:Archivo,sans-serif; font-weight:700; font-size:34px; line-height:1;
  font-variant-numeric:tabular-nums; letter-spacing:-.02em
}
.card .note{font-size:13px; color:var(--ink-2)}
.pill{
  align-self:flex-start; font-family:"JetBrains Mono",monospace; font-size:10.5px;
  padding:2px 7px; border-radius:3px; letter-spacing:.06em; text-transform:uppercase
}
.p-good{background:color-mix(in srgb,var(--good) 16%,transparent); color:var(--good)}
.p-warn{background:color-mix(in srgb,var(--warn) 18%,transparent); color:var(--warn)}
.p-crit{background:color-mix(in srgb,var(--crit) 16%,transparent); color:var(--crit)}

.grid2{display:grid; grid-template-columns:repeat(auto-fit,minmax(430px,1fr)); gap:16px}
.panel{background:var(--surface); border:1px solid var(--line); border-radius:6px; padding:16px 18px 12px}
.panel h3{
  font-family:Archivo,sans-serif; font-weight:600; font-size:15px; margin:0 0 2px; letter-spacing:-.01em
}
.panel p.cap{margin:0 0 12px; font-size:12.5px; color:var(--ink-3)}
.chart{width:100%; overflow-x:auto}
svg{display:block; max-width:100%}
.axis{fill:var(--ink-3); font-family:"JetBrains Mono",monospace; font-size:9.5px}
.gridline{stroke:var(--grid); stroke-width:1}
.target{stroke:var(--accent); stroke-width:1.5; stroke-dasharray:5 4}
.ceil{stroke:var(--ink-3); stroke-width:1; stroke-dasharray:2 4; opacity:.7}
.runline{fill:none; stroke-width:1.1; opacity:.42}
/* .16 was effectively invisible, so the bold MEDIAN was all anyone could
   read -- and a median across 48 faction-runs averages away exactly the
   spikes and collapses that matter. Individual runs swing by 27 to 144
   units; the chart showed a flat line. */
.medline{fill:none; stroke-width:2.4}

.legend{display:flex; flex-wrap:wrap; gap:12px; margin-top:10px}
.legend span{
  display:inline-flex; align-items:center; gap:6px;
  font-family:"JetBrains Mono",monospace; font-size:11px; color:var(--ink-2)
}
.sw{width:10px; height:10px; border-radius:2px; flex:none}

table{width:100%; border-collapse:collapse; font-size:13.5px}
th,td{text-align:left; padding:7px 10px; border-bottom:1px solid var(--line-soft)}
th{
  font-family:"JetBrains Mono",monospace; font-size:10px; letter-spacing:.09em;
  text-transform:uppercase; color:var(--ink-3); font-weight:400
}
td.n{font-family:"JetBrains Mono",monospace; font-variant-numeric:tabular-nums; text-align:right}
td.id{font-family:"JetBrains Mono",monospace; font-size:12.5px}
.bar{height:7px; border-radius:2px; background:var(--accent); display:block}
.bartrack{background:var(--surface-2); border-radius:2px; width:100%}

footer{margin-top:44px; padding-top:18px; border-top:1px solid var(--line); color:var(--ink-3); font-size:13px}
code{font-family:"JetBrains Mono",monospace; font-size:12.5px; color:var(--ink-2)}
@media (prefers-reduced-motion:no-preference){
  .card,.panel{transition:border-color .18s ease}
}
.card:hover,.panel:hover{border-color:color-mix(in srgb,var(--accent) 45%,var(--line))}
</style>

<div class="wrap">
<header>
  <p class="eyebrow">Headless batch &middot; __MATCHES__ matches &middot; build __BUILDFP__ (latest only) &middot; __LIMIT__s limit</p>
  <h1>Batch Watch</h1>
  <p class="sub">What a full batch tells you that one watched match cannot. Every panel is drawn from the five CSVs <code>MatchMetrics</code> writes into each session folder.</p>
  <div style="margin-top:16px;display:flex;align-items:center;gap:10px">
    <label for="dsel" style="font-family:'JetBrains Mono',monospace;font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:var(--ink-3)">Match</label>
    <select id="dsel" style="background:var(--surface);color:var(--ink);border:1px solid var(--line);border-radius:4px;padding:6px 10px;font:inherit;max-width:420px"></select>
  </div>
  __BANNER__
  <div class="warnbar"><strong>__HIT__ of __FM__ factions reached the __TARGET__ population cap.</strong> Before the budget and housing fixes, none did &mdash; and no faction in any match built a single military building.</div>
</header>

<h2>Verdict</h2>
<div class="verdicts" id="verdicts"></div>

<h2>Match outcomes</h2>
<div class="panel">
  <h3>How each match ended</h3>
  <p class="cap">A match ends the moment one side is left standing; the time limit is only the stalemate guard. <code>quit</code> is a guard-capped run, not a victory; a session with no summary yet is still in flight.</p>
  <div id="t-out"></div>
</div>

<h2>Trajectories</h2>
<div class="grid2">
  <div class="panel">
    <h3>Population against the ceiling</h3>
    <p class="cap">Population in use, every faction in every match. Median bold. Dashed line is the __TARGET__ acceptance bar; dotted is the __CEILING__ ceiling.</p>
    <div class="chart" id="c-pop"></div>
    <div class="legend" id="l-pop"></div>
  </div>
  <div class="panel">
    <h3>Population cap</h3>
    <p class="cap">The housing itself. A line that climbs and then falls is a faction losing buildings, not one that stopped building.</p>
    <div class="chart" id="c-cap"></div>
    <div class="legend" id="l-cap"></div>
  </div>
</div>
<div class="grid2" style="margin-top:16px">
  <div class="panel">
    <h3>Army size over time</h3>
    <p class="cap">Each faded line is one faction in one match; bold is the median. Read the faded lines -- the median flattens the very spikes and collapses this panel exists to show.</p>
    <div class="chart" id="c-army"></div>
    <div class="legend" id="l-army"></div>
  </div>
  <div class="panel">
    <h3>__RESTITLE__</h3>
    <p class="cap">Median bank across all factions. A resource climbing without limit is one nothing spends.</p>
    <div class="chart" id="c-res"></div>
    <div class="legend" id="l-res"></div>
  </div>
</div>

<h2>Resource flow per minute</h2>
<p class="sub" style="font-size:13px;color:var(--ink-3);margin:-6px 0 12px">Net bank change over the trailing minute &mdash; income minus spending, from the bank ledger. Below zero is a spend-down, not an error. One panel per resource; faint lines are individual faction-runs, bold is each faction's median.</p>
<div class="grid2" id="p-flow"></div>

<h2>Resources spent per minute</h2>
<p class="sub" style="font-size:13px;color:var(--ink-3);margin:-6px 0 12px">Sum of bank DROPS between samples over the trailing minute. Income landing in the same 15&nbsp;s window as a purchase offsets it, so this slightly undercounts true spending.</p>
<div class="grid2" id="p-spend"></div>

<h2>Expenditure by category</h2>
<p class="sub" style="font-size:13px;color:var(--ink-3);margin:-6px 0 12px">Quantities from count deltas in the Buildings/Units ledgers, priced from the SO cost data on disk. Mean per faction. Research has no timestamps in its ledger, so it appears in the totals only; upgrades and refunds are not ledgered anywhere.</p>
<div class="grid2" id="p-cat"></div>
<div class="panel" style="margin-top:16px">
  <h3>Total spend per faction</h3>
  <p class="cap">Mean over all faction-matches, by category and resource.</p>
  <div id="t-cat"></div>
</div>

<h2>Combat</h2>
<div class="grid2">
  <div class="panel">
    <h3>Kills per minute</h3>
    <p class="cap">Mean across matches, credited to the LAST faction that damaged the victim. Spikes are battles; a flat line is an army that never fought.</p>
    <div class="chart" id="c-kills"></div>
    <div class="legend" id="l-kills"></div>
  </div>
  <div class="panel">
    <h3>Deaths per minute</h3>
    <p class="cap">Every unit death, attributed or not &mdash; curse exposure and other unattributed deaths count here but credit no killer.</p>
    <div class="chart" id="c-deaths"></div>
    <div class="legend" id="l-deaths"></div>
  </div>
</div>

<h2>Composition and construction</h2>
<div class="grid2">
  <div class="panel">
    <h3>Army composition</h3>
    <p class="cap">Share of every unit alive at match end. Tests whether cavalry and siege are reachable at all.</p>
    <div id="t-comp"></div>
  </div>
  <div class="panel">
    <h3>Buildings per faction</h3>
    <p class="cap">Mean count at match end, across all __FM__ faction-matches.</p>
    <div id="t-bld"></div>
  </div>
</div>

<h2>Research</h2>
<div class="panel">
  <h3>Completion rate per technology</h3>
  <p class="cap">Share of faction-matches that finished each tech. A tier missing here is one nobody reaches.</p>
  <div id="t-res"></div>
</div>

<h2>Placement</h2>
<div class="panel">
  <h3>Where everything got built</h3>
  <p class="cap">Every building from every match, by map position and owner. Ringed marks are Halls &mdash; each one past the first is a territory claim.</p>
  <div class="chart" id="c-map"></div>
  <div class="legend" id="l-map"></div>
</div>

<h2>Match replay</h2>
<div class="panel">
  <h3>Construction over time</h3>
  <p class="cap">The end-state map replayed against each building type's count curve: a type's dots appear nearest-its-start-first as its recorded count rises. An approximation, honestly labelled &mdash; the ledger records positions once and counts every 15&nbsp;s, so reveal ORDER within a type is inferred, unit movements are not recorded at all, and destroyed buildings stay shown (their positions are never logged).</p>
  <div style="display:flex;align-items:center;gap:12px;margin:6px 0 10px;flex-wrap:wrap">
    <button id="rp-play" style="background:var(--accent);color:#fff;border:0;border-radius:4px;padding:7px 16px;font:inherit;cursor:pointer">Play</button>
    <input id="rp-slider" type="range" min="0" max="1" value="0" style="flex:1;min-width:220px">
    <span id="rp-clock" style="font-family:'JetBrains Mono',monospace;font-size:13px;min-width:52px;text-align:right">0:00</span>
  </div>
  <div class="chart" id="c-replay"></div>
  <div class="legend" id="l-replay"></div>
</div>

<footer>
  Sources: <code>Metrics_Faction.csv</code>, <code>Metrics_Units.csv</code>, <code>Metrics_Buildings.csv</code>, <code>Metrics_Research.csv</code>, <code>Metrics_Placement.csv</code> &mdash; one set per session, aggregated by <code>tools/batch-report.py</code>. Generated __STAMP__.
</footer>
</div>

<script>
const DATASETS = __DATA__;
const MAPIMG = __MAPIMG__;
let D = DATASETS["All matches"];
const FAC = [["Blue","--f-blue"],["Green","--f-green"],["Red","--f-red"],["Yellow","--f-yellow"]];
const N = D.grid.length;
const css = n => getComputedStyle(document.documentElement).getPropertyValue(n).trim();

function median(arrs, i){
  const v = arrs.map(a => a.v[i]).filter(x => x !== null).sort((a,b) => a-b);
  return v.length ? v[Math.floor(v.length/2)] : null;
}

// Path that breaks across gaps, so a faction wiped out early stops rather
// than drawing a false line to the end of the match.
function pathOf(vals, x, y){
  let d = "", pen = false;
  for (let i = 0; i < vals.length; i++){
    const v = vals[i];
    if (v === null){ pen = false; continue; }
    d += (pen ? "L" : "M") + x(i).toFixed(1) + " " + y(v).toFixed(1) + " ";
    pen = true;
  }
  return d.trim();
}

function lineChart(el, legendEl, series, opts){
  const W = 560, H = 230, P = {l:42, r:12, t:10, b:26};
  let maxY = opts.maxY;
  if (!maxY){
    let m = 0;
    series.forEach(s => s.v.forEach(v => { if (v !== null && v > m) m = v; }));
    maxY = Math.max(10, Math.ceil(m * 1.1 / 10) * 10);
  }
  const x = i => P.l + (i/(N-1)) * (W-P.l-P.r);
  const y = v => H - P.b - (v/maxY) * (H-P.t-P.b);
  let s = '<svg viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="' + opts.aria + '">';
  for (let g = 0; g <= 4; g++){
    const v = (maxY/4)*g;
    s += '<line class="gridline" x1="'+P.l+'" y1="'+y(v)+'" x2="'+(W-P.r)+'" y2="'+y(v)+'"/>';
    s += '<text class="axis" x="'+(P.l-7)+'" y="'+(y(v)+3)+'" text-anchor="end">'+Math.round(v)+'</text>';
  }
  const lastMin = Math.round(D.grid[N-1] / 60);
  for (let m = 0; m <= lastMin; m += 5){
    const i = (m*60)/15; if (i > N-1) continue;
    s += '<text class="axis" x="'+x(i)+'" y="'+(H-8)+'" text-anchor="middle">'+m+'m</text>';
  }
  if (opts.ceiling && opts.ceiling <= maxY)
    s += '<line class="ceil" x1="'+P.l+'" y1="'+y(opts.ceiling)+'" x2="'+(W-P.r)+'" y2="'+y(opts.ceiling)+'"/>';
  if (opts.target && opts.target <= maxY){
    s += '<line class="target" x1="'+P.l+'" y1="'+y(opts.target)+'" x2="'+(W-P.r)+'" y2="'+y(opts.target)+'"/>';
    s += '<text class="axis" x="'+(W-P.r)+'" y="'+(y(opts.target)-5)+'" text-anchor="end" fill="'+css('--accent')+'">target '+opts.target+'</text>';
  }
  series.forEach(d => {
    s += '<path class="runline" stroke="'+css(FAC[d.f][1])+'" d="'+pathOf(d.v, x, y)+'"/>';
  });
  FAC.forEach((F, fi) => {
    const mine = series.filter(d => d.f === fi);
    if (!mine.length) return;
    const med = []; for (let i = 0; i < N; i++) med.push(median(mine, i));
    s += '<path class="medline" stroke="'+css(F[1])+'" d="'+pathOf(med, x, y)+'"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = FAC.map(F => '<span><i class="sw" style="background:'+css(F[1])+'"></i>'+F[0]+'</span>').join('')
    + '<span style="color:var(--ink-3)">faint = individual runs</span>';
}

function resChart(el, legendEl){
  const W = 560, H = 230, P = {l:52, r:12, t:10, b:26};
  const defs = [["Supplies","--good"],["Iron","--ink-2"],["Veilstone","--accent"],["Veilsteel","--warn"]];
  let maxY = 0;
  defs.forEach(([k]) => D.res[k].forEach(v => { if (v > maxY) maxY = v; }));
  maxY = Math.max(100, Math.ceil(maxY * 1.08 / 100) * 100);
  const x = i => P.l + (i/(N-1)) * (W-P.l-P.r);
  const y = v => H - P.b - (v/maxY) * (H-P.t-P.b);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" role="img" aria-label="Median resource banks over time">';
  for (let g = 0; g <= 4; g++){
    const v = (maxY/4)*g;
    s += '<line class="gridline" x1="'+P.l+'" y1="'+y(v)+'" x2="'+(W-P.r)+'" y2="'+y(v)+'"/>';
    s += '<text class="axis" x="'+(P.l-7)+'" y="'+(y(v)+3)+'" text-anchor="end">'+Math.round(v)+'</text>';
  }
  const lastMin = Math.round(D.grid[N-1] / 60);
  for (let m = 0; m <= lastMin; m += 5){
    const i = (m*60)/15; if (i > N-1) continue;
    s += '<text class="axis" x="'+x(i)+'" y="'+(H-8)+'" text-anchor="middle">'+m+'m</text>';
  }
  defs.forEach(([k, tok]) => {
    s += '<path class="medline" stroke="'+css(tok)+'" d="'+pathOf(D.res[k], x, y)+'"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = defs.map(([n,t]) => '<span><i class="sw" style="background:'+css(t)+'"></i>'+n+'</span>').join('');
}

function barTable(el, rows, unit){
  if (!rows.length){ el.innerHTML = '<p class="cap">Nothing recorded.</p>'; return; }
  const max = Math.max.apply(null, rows.map(r => r[1])) || 1;
  el.innerHTML = '<table><thead><tr><th>'+unit[0]+'</th><th style="width:46%"></th>'
    + '<th style="text-align:right">'+unit[1]+'</th></tr></thead><tbody>'
    + rows.map(r => '<tr><td class="id">'+r[0]+'</td>'
      + '<td><span class="bartrack" style="display:block"><span class="bar" style="width:'
      + (r[1]/max*100).toFixed(1) + '%"></span></span></td>'
      + '<td class="n">'+r[1].toFixed(1)+(unit[2]||'')+'</td></tr>').join('')
    + '</tbody></table>';
}

function mapChart(el, legendEl){
  // With a baked map thumbnail the chart IS the map: square canvas, dots on
  // the real terrain, region borders already burned into the bake. Without
  // one (unknown map), fall back to the abstract auto-scaled grid.
  const hasMap = !!(MAPIMG && D.mapHalf);
  const W = hasMap ? 760 : 1080, H = hasMap ? 760 : 430, P = 22;
  let R = 10;
  if (hasMap) R = D.mapHalf;
  else {
    D.place.forEach(p => { R = Math.max(R, Math.abs(p[0]), Math.abs(p[1])); });
    R = Math.ceil(R * 1.06 / 25) * 25;
  }
  const x = v => P + ((v+R)/(2*R)) * (W-2*P);
  const y = v => P + ((R-v)/(2*R)) * (H-2*P);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" style="max-width:'+W+'px;margin:0 auto" role="img" aria-label="Building placement across all matches">';
  s += '<rect x="0" y="0" width="'+W+'" height="'+H+'" fill="'+css('--surface-2')+'" rx="4"/>';
  if (hasMap){
    // MapInfoBaker renders top-down with +Z up and +X right — the same
    // orientation as this chart's y-flip, so the image pins corner-to-corner.
    s += '<image href="'+MAPIMG+'" x="'+x(-R)+'" y="'+y(R)+'" width="'+(x(R)-x(-R))
       + '" height="'+(y(-R)-y(R))+'" preserveAspectRatio="none" opacity="0.9"/>';
  } else {
    const gs = R > 200 ? 100 : 50;
    for (let g = -R; g <= R; g += gs){
      s += '<line class="gridline" x1="'+x(g)+'" y1="'+P+'" x2="'+x(g)+'" y2="'+(H-P)+'"/>';
      s += '<line class="gridline" x1="'+P+'" y1="'+y(g)+'" x2="'+(W-P)+'" y2="'+y(g)+'"/>';
    }
  }
  // Resource locations from the map bake, UNDER the building dots: the
  // nodes are why the buildings stand where they do.
  const NODE_STYLE = {
    "Supply":     ["--good",  "ring"],
    "Iron":       ["--ink-3", "sq"],
    "Veilstone":  ["--accent","diamond"],
    "Veilsteel":  ["--warn",  "diamond"],
    "Curse well": ["--crit",  "x"],
  };
  Object.entries(D.mapNodes || {}).forEach(([kind, pts]) => {
    const st = NODE_STYLE[kind]; if (!st) return;
    const c = css(st[0]);
    pts.forEach(p => {
      const px = x(p[0]), py = y(p[1]);
      if (st[1] === "ring")
        s += '<circle cx="'+px.toFixed(1)+'" cy="'+py.toFixed(1)+'" r="3.6" fill="none" stroke="'+c+'" stroke-width="1.8" opacity=".95"/>';
      else if (st[1] === "sq")
        s += '<rect x="'+(px-2.6).toFixed(1)+'" y="'+(py-2.6).toFixed(1)+'" width="5.2" height="5.2" fill="'+c+'" opacity=".95"/>';
      else if (st[1] === "diamond")
        s += '<rect x="'+(px-2.8).toFixed(1)+'" y="'+(py-2.8).toFixed(1)+'" width="5.6" height="5.6" fill="'+c+'" opacity=".95" transform="rotate(45 '+px.toFixed(1)+' '+py.toFixed(1)+')"/>';
      else
        s += '<path d="M'+(px-3.4).toFixed(1)+' '+(py-3.4).toFixed(1)+'L'+(px+3.4).toFixed(1)+' '+(py+3.4).toFixed(1)
           + 'M'+(px+3.4).toFixed(1)+' '+(py-3.4).toFixed(1)+'L'+(px-3.4).toFixed(1)+' '+(py+3.4).toFixed(1)
           + '" stroke="'+c+'" stroke-width="2.2" opacity=".95"/>';
    });
  });
  // TWO PASSES: plain dots first, ringed Halls always on top. Single-pass
  // draw order followed the ledger, so a faction's own later dots painted
  // over its Hall rings — which factions lost their white outlines was an
  // accident of row order (Blue and Yellow, as it happened).
  D.place.forEach(p => {
    if (p[3]) return;
    const c = css(FAC[p[2]][1]);
    s += '<circle cx="'+x(p[0]).toFixed(1)+'" cy="'+y(p[1]).toFixed(1)+'" r="2.5" fill="'+c+'" stroke="#fff" stroke-width=".7" opacity="'+(hasMap ? '.85' : '.5')+'"/>';
  });
  D.place.forEach(p => {
    if (!p[3]) return;
    const c = css(FAC[p[2]][1]);
    const px = x(p[0]).toFixed(1), py = y(p[1]).toFixed(1);
    s += '<circle cx="'+px+'" cy="'+py+'" r="4.6" fill="none" stroke="#fff" stroke-width="3.2" opacity=".8"/>'
       + '<circle cx="'+px+'" cy="'+py+'" r="4.6" fill="none" stroke="'+c+'" stroke-width="1.8"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = FAC.map(F => '<span><i class="sw" style="background:'+css(F[1])+'"></i>'+F[0]+'</span>').join('')
    + '<span style="color:var(--ink-3)">ringed = Hall</span>'
    + '<span style="color:var(--ink-3)">'+D.place.length+' buildings</span>'
    + (hasMap ? '<span style="color:var(--ink-3)">map: '+D.mapName+' — light lines are region borders</span>' : '')
    + (Object.keys(D.mapNodes || {}).length
        ? '<span><i class="sw" style="background:'+css('--good')+';border-radius:50%"></i>supply node</span>'
        + '<span><i class="sw" style="background:'+css('--ink-3')+'"></i>iron</span>'
        + '<span><i class="sw" style="background:'+css('--accent')+';transform:rotate(45deg)"></i>veilstone</span>'
        + '<span><i class="sw" style="background:'+css('--warn')+';transform:rotate(45deg)"></i>veilsteel</span>'
        + '<span style="color:var(--crit);font-weight:600">&times;</span><span style="color:var(--ink-3)">curse well — authored nodes from the map bake; runtime quota top-ups appear after a rebake</span>'
        : '')
    + (D.placeSynth ? '<span style="color:var(--warn)">live sessions: dots reconstructed from AI logs (extractors + claimed Halls only) — the full ledger lands when each match dumps</span>' : '');
}

function combatChart(el, legendEl, byFaction, aria){
  const mins = Math.floor(D.grid[D.grid.length-1] / 60);
  const W = 560, H = 230, P = {l:42, r:12, t:10, b:26};
  let maxY = 1;
  FAC.forEach(F => (byFaction[F[0]] || []).forEach(v => { if (v > maxY) maxY = v; }));
  maxY = Math.ceil(maxY * 1.15);
  const x = i => P.l + (i/(mins-1)) * (W-P.l-P.r);
  const y = v => H - P.b - (v/maxY) * (H-P.t-P.b);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" role="img" aria-label="'+aria+'">';
  for (let g = 0; g <= 4; g++){
    const v = (maxY/4)*g;
    s += '<line class="gridline" x1="'+P.l+'" y1="'+y(v)+'" x2="'+(W-P.r)+'" y2="'+y(v)+'"/>';
    s += '<text class="axis" x="'+(P.l-7)+'" y="'+(y(v)+3)+'" text-anchor="end">'+v.toFixed(v < 4 ? 1 : 0)+'</text>';
  }
  for (let m = 0; m <= mins; m += 5)
    s += '<text class="axis" x="'+x(Math.min(m, mins-1))+'" y="'+(H-8)+'" text-anchor="middle">'+m+'m</text>';
  FAC.forEach(F => {
    const v = byFaction[F[0]];
    if (!v) return;
    let d = "";
    for (let i = 0; i < Math.min(mins, v.length); i++)
      d += (i ? "L" : "M") + x(i).toFixed(1) + " " + y(v[i]).toFixed(1) + " ";
    s += '<path class="medline" stroke="'+css(F[1])+'" d="'+d.trim()+'"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = FAC.map(F => '<span><i class="sw" style="background:'+css(F[1])+'"></i>'+F[0]+'</span>').join('')
    + '<span style="color:var(--ink-3)">mean of '+D.combatN+' match(es)</span>';
}

function verdicts(){
  const V = D.verdict;
  const cards = [
    ["Reached cap "+D.target, V.pct+"%", V.hit+" of "+V.fm+" faction-matches",
      V.pct >= 90 ? ["p-good","target met"] : V.pct >= 70 ? ["p-warn","most"] : ["p-crit","failing"]],
    ["Reached Age 1", V.agedPct+"%",
      V.aged+" of "+V.fm+(V.ageMedianMin ? " -- median at "+V.ageMedianMin+" min (target 3-6)" : ""),
      V.agedPct >= 75 ? ["p-good","tree open"] : V.agedPct >= 25 ? ["p-warn","partial"] : ["p-crit","tree locked"]],
    ["Median peak cap", V.medCap, "ceiling is "+D.ceiling,
      V.medCap >= D.target ? ["p-good","above bar"] : ["p-crit","below bar"]],
    ["Peak army", V.peakArmy, "largest force any faction massed",
      V.peakArmy >= 60 ? ["p-good","armies form"] : ["p-warn","small"]],
    ["Median army at 20m", V.medArmy, "median of FINAL samples -- low because factions get wiped",
      V.medArmy >= 40 ? ["p-good","spiral broken"] : ["p-warn","thin"]],
    ["Ending at zero", V.zero+"/"+V.fm, "eliminated before the limit",
      V.zero > V.fm*0.35 ? ["p-crit","high"] : ["p-good","normal attrition"]],
    ["Matches completed", V.complete+"/"+V.matches, "no hangs, no exceptions",
      V.complete === V.matches ? ["p-good","clean"] : ["p-warn","incomplete"]],
    ["Decided by elimination", V.decided+"/"+V.matches, "one side left standing before any limit",
      V.decided === V.matches ? ["p-good","all decided"]
        : V.decided > 0 ? ["p-warn","some guard-capped"] : ["p-warn","none yet"]],
  ];
  document.getElementById('verdicts').innerHTML = cards.map(c =>
    '<div class="card"><span class="lbl">'+c[0]+'</span><span class="val">'+c[1]+'</span>'
    + '<span class="pill '+c[3][0]+'">'+c[3][1]+'</span><span class="note">'+c[2]+'</span></div>').join('');
}

const RES = [["Supplies","--good"],["Iron","--ink-2"],["Veilstone","--accent"],["Veilsteel","--warn"]];

// Per-faction rate chart with negative support: a zero line, faint
// individual runs, bold per-faction medians. The lineChart above pins its
// floor at 0, which would fold every spend-down into the axis.
function rateChart(el, legendEl, series, aria, allowNeg){
  const W = 560, H = 230, P = {l:52, r:12, t:10, b:26};
  let maxY = 10, minY = 0;
  series.forEach(s => s.v.forEach(v => {
    if (v === null) return;
    if (v > maxY) maxY = v;
    if (allowNeg && v < minY) minY = v;
  }));
  maxY = Math.ceil(maxY * 1.08); if (allowNeg) minY = Math.floor(minY * 1.08);
  const x = i => P.l + (i/(N-1)) * (W-P.l-P.r);
  const y = v => H - P.b - ((v-minY)/(maxY-minY)) * (H-P.t-P.b);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" role="img" aria-label="'+aria+'">';
  for (let g = 0; g <= 4; g++){
    const v = minY + ((maxY-minY)/4)*g;
    s += '<line class="gridline" x1="'+P.l+'" y1="'+y(v)+'" x2="'+(W-P.r)+'" y2="'+y(v)+'"/>';
    s += '<text class="axis" x="'+(P.l-7)+'" y="'+(y(v)+3)+'" text-anchor="end">'+Math.round(v)+'</text>';
  }
  if (minY < 0)
    s += '<line x1="'+P.l+'" y1="'+y(0)+'" x2="'+(W-P.r)+'" y2="'+y(0)
       + '" stroke="'+css('--ink-3')+'" stroke-width="1.3"/>';
  const lastMin = Math.round(D.grid[N-1] / 60);
  for (let m = 0; m <= lastMin; m += 5){
    const i = (m*60)/15; if (i > N-1) continue;
    s += '<text class="axis" x="'+x(i)+'" y="'+(H-8)+'" text-anchor="middle">'+m+'m</text>';
  }
  series.forEach(d => {
    s += '<path class="runline" stroke="'+css(FAC[d.f][1])+'" d="'+pathOf(d.v, x, y)+'"/>';
  });
  FAC.forEach((F, fi) => {
    const mine = series.filter(d => d.f === fi);
    if (!mine.length) return;
    const med = []; for (let i = 0; i < N; i++) med.push(median(mine, i));
    s += '<path class="medline" stroke="'+css(F[1])+'" d="'+pathOf(med, x, y)+'"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = FAC.map(F => '<span><i class="sw" style="background:'+css(F[1])+'"></i>'+F[0]+'</span>').join('')
    + '<span style="color:var(--ink-3)">faint = individual runs</span>';
}

function ratePanels(containerId, byRes, ariaSuffix, allowNeg){
  const cont = document.getElementById(containerId);
  cont.innerHTML = RES.map(([k], i) =>
    '<div class="panel"><h3>'+k+'</h3>'
    + '<div class="chart" id="'+containerId+'-c'+i+'"></div>'
    + '<div class="legend" id="'+containerId+'-l'+i+'"></div></div>').join('');
  RES.forEach(([k], i) => rateChart(
    document.getElementById(containerId+'-c'+i),
    document.getElementById(containerId+'-l'+i),
    D === null ? [] : (allowNeg ? D.flow[k] : D.spend[k]),
    k + ' ' + ariaSuffix, allowNeg));
}

// Per-category spend chart: one line per RESOURCE, per-minute means.
function catChart(el, legendEl, byRes, aria){
  const mins = byRes.Supplies.length;
  const W = 560, H = 230, P = {l:52, r:12, t:10, b:26};
  let maxY = 10;
  RES.forEach(([k]) => byRes[k].forEach(v => { if (v > maxY) maxY = v; }));
  maxY = Math.ceil(maxY * 1.08);
  const x = i => P.l + (i/(Math.max(mins,2)-1)) * (W-P.l-P.r);
  const y = v => H - P.b - (v/maxY) * (H-P.t-P.b);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" role="img" aria-label="'+aria+'">';
  for (let g = 0; g <= 4; g++){
    const v = (maxY/4)*g;
    s += '<line class="gridline" x1="'+P.l+'" y1="'+y(v)+'" x2="'+(W-P.r)+'" y2="'+y(v)+'"/>';
    s += '<text class="axis" x="'+(P.l-7)+'" y="'+(y(v)+3)+'" text-anchor="end">'+Math.round(v)+'</text>';
  }
  for (let m = 0; m <= mins; m += 5)
    s += '<text class="axis" x="'+x(Math.min(m, mins-1))+'" y="'+(H-8)+'" text-anchor="middle">'+m+'m</text>';
  RES.forEach(([k, tok]) => {
    let d = "";
    for (let i = 0; i < mins; i++)
      d += (i ? "L" : "M") + x(i).toFixed(1) + " " + y(byRes[k][i]).toFixed(1) + " ";
    s += '<path class="medline" stroke="'+css(tok)+'" d="'+d.trim()+'"/>';
  });
  el.innerHTML = s + '</svg>';
  legendEl.innerHTML = RES.map(([n,t]) => '<span><i class="sw" style="background:'+css(t)+'"></i>'+n+'</span>').join('');
}

function catPanels(){
  const cont = document.getElementById('p-cat');
  const cats = Object.keys(D.catSpend);
  cont.innerHTML = cats.map((c, i) =>
    '<div class="panel"><h3>'+c+' spend per minute</h3>'
    + '<p class="cap">Mean per faction, per minute of match time.</p>'
    + '<div class="chart" id="p-cat-c'+i+'"></div>'
    + '<div class="legend" id="p-cat-l'+i+'"></div></div>').join('');
  cats.forEach((c, i) => catChart(
    document.getElementById('p-cat-c'+i), document.getElementById('p-cat-l'+i),
    D.catSpend[c], c + ' expenditure per minute'));

  const rows = D.catTotals || [];
  document.getElementById('t-cat').innerHTML =
    '<table><thead><tr><th>Category</th>'
    + RES.map(([k]) => '<th style="text-align:right">'+k+'</th>').join('')
    + '</tr></thead><tbody>'
    + rows.map(r => '<tr><td class="id">'+r[0]+'</td>'
      + r.slice(1).map(v => '<td class="n">'+v+'</td>').join('') + '</tr>').join('')
    + '</tbody></table>';
}

function outcomesTable(){
  const rows = D.outcomes || [];
  const el = document.getElementById('t-out');
  if (!rows.length){ el.innerHTML = '<p class="cap">No sessions.</p>'; return; }
  el.innerHTML = '<table><thead><tr><th>Session</th><th style="text-align:right">Seed</th>'
    + '<th style="text-align:right">Sim time</th><th>Outcome</th></tr></thead><tbody>'
    + rows.map(r => '<tr><td class="id">'+r.s+'</td><td class="n">'+r.seed+'</td>'
      + '<td class="n">'+Math.floor(r.t/60)+'m'+String(r.t%60).padStart(2,'0')+'s</td>'
      + '<td><span class="pill '+(r.decided ? 'p-good' : r.out === 'in flight' ? 'p-warn' : 'p-crit')
      + '">'+r.out+'</span></td></tr>').join('')
    + '</tbody></table>';
}

function drawAll(){
  verdicts();
  outcomesTable();
  ratePanels('p-flow', null, 'net bank change per minute', true);
  ratePanels('p-spend', null, 'spent per minute', false);
  catPanels();
  lineChart(document.getElementById('c-pop'), document.getElementById('l-pop'), D.pop,
    {maxY: D.ceiling+20, target: D.target, ceiling: D.ceiling, aria:'Population in use over time'});
  lineChart(document.getElementById('c-cap'), document.getElementById('l-cap'), D.cap,
    {maxY: D.ceiling+20, target: D.target, ceiling: D.ceiling, aria:'Population cap over time'});
  lineChart(document.getElementById('c-army'), document.getElementById('l-army'), D.army,
    {aria:'Army size over time'});
  resChart(document.getElementById('c-res'), document.getElementById('l-res'));
  if (D.combatN > 0){
    combatChart(document.getElementById('c-kills'), document.getElementById('l-kills'),
      D.kills, 'Kills per minute per faction');
    combatChart(document.getElementById('c-deaths'), document.getElementById('l-deaths'),
      D.deaths, 'Deaths per minute per faction');
  } else {
    document.getElementById('c-kills').innerHTML =
      '<p class="cap">No combat ledger in these sessions (pre-ledger build).</p>';
    document.getElementById('c-deaths').innerHTML = '';
  }
  barTable(document.getElementById('t-comp'), D.comp, ["Unit","Share","%"]);
  barTable(document.getElementById('t-bld'), D.bld, ["Building","Mean",""]);
  barTable(document.getElementById('t-res'), D.tech, ["Technology","Completion","%"]);
  mapChart(document.getElementById('c-map'), document.getElementById('l-map'));
  rpInit();
}

// ── Match replay ────────────────────────────────────────────────────
// End-state positions revealed against each type's count curve. State is
// module-level so the play timer survives redraws; changing dataset
// resets it.
const RP = { timer: null, minute: 0, order: null, minutes: 0 };

function rpStop(){
  if (RP.timer){ clearInterval(RP.timer); RP.timer = null; }
  const b = document.getElementById('rp-play');
  if (b) b.textContent = 'Play';
}

function rpPrepare(){
  // Ordered reveal lists per faction+type: nearest that faction's start
  // first — construction radiates outward from the base.
  RP.order = {};
  RP.minutes = 0;
  if (!D.bldMin) return;
  const byKey = {};
  D.place.forEach(p => {
    const key = p[2] + '|' + (D.placeIds[p[4]] || 'unknown');
    (byKey[key] = byKey[key] || []).push(p);
  });
  Object.entries(byKey).forEach(([key, pts]) => {
    const fi = +key.split('|')[0];
    const st = (D.starts || {})[fi] || [0, 0];
    pts.sort((a, b) =>
      ((a[0]-st[0])**2 + (a[1]-st[1])**2) - ((b[0]-st[0])**2 + (b[1]-st[1])**2));
    RP.order[key] = pts;
  });
  // The dataset's own end, not the global grid: the grid is sized to the
  // LONGEST session in the batch, so shorter matches froze at their last
  // real minute and the replay looked like it stopped halfway.
  RP.minutes = D.endMin || 1;
}

function rpFrame(){
  const el = document.getElementById('c-replay');
  const leg = document.getElementById('l-replay');
  if (!D.bldMin){
    rpStop();
    el.innerHTML = '<p class="cap">Select a single match in the Match dropdown to replay it.</p>';
    leg.innerHTML = '';
    return;
  }
  const m = RP.minute;
  document.getElementById('rp-slider').max = Math.max(1, RP.minutes - 1);
  document.getElementById('rp-slider').value = m;
  document.getElementById('rp-clock').textContent = m + ':00';

  const hasMap = !!(MAPIMG && D.mapHalf);
  const W = 620, H = 620, P = 16, R = hasMap ? D.mapHalf : 512;
  const x = v => P + ((v+R)/(2*R)) * (W-2*P);
  const y = v => P + ((R-v)/(2*R)) * (H-2*P);
  let s = '<svg viewBox="0 0 '+W+' '+H+'" style="max-width:'+W+'px;margin:0 auto" role="img" aria-label="Match replay at minute '+m+'">';
  s += '<rect x="0" y="0" width="'+W+'" height="'+H+'" fill="'+css('--surface-2')+'" rx="4"/>';
  if (hasMap)
    s += '<image href="'+MAPIMG+'" x="'+x(-R)+'" y="'+y(R)+'" width="'+(x(R)-x(-R))
       + '" height="'+(y(-R)-y(R))+'" preserveAspectRatio="none" opacity="0.9"/>';

  const halls = [];   // ringed marks draw over every plain dot
  if (D.bldEvents){
    // Exact event ledger: draw what is ALIVE at minute m — razed bases
    // disappear, eliminated factions keep their history, every faction's
    // rings exist from frame zero.
    const alive = new Map();
    D.bldEvents.forEach(e => {
      if (e[0] > m) return;
      const k = e[1]+'|'+e[2]+'|'+e[3]+'|'+e[4];
      alive.set(k, (alive.get(k) || 0) + e[5]);
    });
    alive.forEach((n, k) => {
      if (n <= 0) return;
      const parts = k.split('|').map(Number);
      const c = css(FAC[parts[2]][1]);
      const px = x(parts[0]).toFixed(1), py = y(parts[1]).toFixed(1);
      if (parts[3])
        halls.push('<circle cx="'+px+'" cy="'+py+'" r="4.4" fill="none" stroke="#fff" stroke-width="3" opacity=".85"/>'
          + '<circle cx="'+px+'" cy="'+py+'" r="4.4" fill="none" stroke="'+c+'" stroke-width="1.7"/>');
      else
        s += '<circle cx="'+px+'" cy="'+py+'" r="2.4" fill="'+c+'" stroke="#fff" stroke-width=".6" opacity=".9"/>';
    });
  }
  else FAC.forEach((F, fi) => {
    const byId = D.bldMin[F[0]];
    if (!byId) return;
    const c = css(F[1]);
    Object.entries(byId).forEach(([bid, counts]) => {
      // Cumulative max: a destroyed building's position is unknown, so
      // dots never disappear — the count curve only reveals.
      let n = 0;
      for (let i = 0; i <= Math.min(m, counts.length - 1); i++)
        n = Math.max(n, counts[i]);
      const pts = RP.order[fi + '|' + bid] || [];
      for (let i = 0; i < Math.min(n, pts.length); i++){
        const px = x(pts[i][0]).toFixed(1), py = y(pts[i][1]).toFixed(1);
        if (pts[i][3])
          halls.push('<circle cx="'+px+'" cy="'+py+'" r="4.4" fill="none" stroke="#fff" stroke-width="3" opacity=".85"/>'
             + '<circle cx="'+px+'" cy="'+py+'" r="4.4" fill="none" stroke="'+c+'" stroke-width="1.7"/>');
        else
          s += '<circle cx="'+px+'" cy="'+py+'" r="2.4" fill="'+c+'" stroke="#fff" stroke-width=".6" opacity=".9"/>';
      }
    });
  });
  s += halls.join('');

  // Army positions: weighted blips from the 30 s position ledger — the
  // match actually unfolding, not just its construction.
  const uf = (D.unitFrames && D.unitFrames[Math.min(m, D.unitFrames.length - 1)]) || [];
  uf.forEach(u => {
    const r = Math.min(6, 1.5 + Math.sqrt(u[3]) * 0.7);
    s += '<circle cx="'+x(u[0]).toFixed(1)+'" cy="'+y(u[1]).toFixed(1)+'" r="'+r.toFixed(1)
       + '" fill="'+css(FAC[u[2]][1])+'" opacity=".75"/>';
  });

  // Battles: every death up to now as a faint scar, the last two minutes
  // as bright crosses in the victim's colour. Clusters ARE the fights.
  (D.deaths || []).forEach(d => {
    if (d[0] > m) return;
    const px = x(d[1]), py = y(d[2]);
    if (d[0] >= m - 1){
      const c = css(FAC[d[3]][1]);
      s += '<path d="M'+(px-3).toFixed(1)+' '+(py-3).toFixed(1)+'L'+(px+3).toFixed(1)+' '+(py+3).toFixed(1)
         + 'M'+(px+3).toFixed(1)+' '+(py-3).toFixed(1)+'L'+(px-3).toFixed(1)+' '+(py+3).toFixed(1)
         + '" stroke="'+c+'" stroke-width="1.8" opacity=".95"/>';
    } else {
      s += '<circle cx="'+px.toFixed(1)+'" cy="'+py.toFixed(1)+'" r="1.3" fill="'+css('--crit')+'" opacity=".22"/>';
    }
  });

  el.innerHTML = s + '</svg>';

  // HUD: live pop / army per faction at this minute (grid is 15 s steps).
  const gi = Math.min(m * 4, N - 1);
  leg.innerHTML = FAC.map((F, fi) => {
    const pop = D.pop.filter(d => d.f === fi).map(d => d.v[gi]).find(v => v !== null && v !== undefined);
    const army = D.army.filter(d => d.f === fi).map(d => d.v[gi]).find(v => v !== null && v !== undefined);
    const dead = pop === undefined || pop === null;
    return '<span><i class="sw" style="background:'+css(F[1])+'"></i>'+F[0]
      + (dead ? ' &dagger;' : ' pop '+pop+' &middot; army '+(army ?? 0)) + '</span>';
  }).join('')
  + (D.unitFrames ? '<span style="color:var(--ink-3)">blips = armies (30 s samples) &middot; &times; = deaths last 2 min &middot; faint red = older deaths</span>'
                  : '<span style="color:var(--ink-3)">no unit/death ledger in this session (recorded by a pre-ledger build) — construction only</span>');
}

function rpInit(){
  rpStop();
  RP.minute = 0;
  rpPrepare();
  rpFrame();
}

document.getElementById('rp-play').addEventListener('click', () => {
  if (!D.bldMin) return;
  if (RP.timer){ rpStop(); return; }
  document.getElementById('rp-play').textContent = 'Pause';
  if (RP.minute >= RP.minutes - 1) RP.minute = 0;
  RP.timer = setInterval(() => {
    RP.minute++;
    if (RP.minute >= RP.minutes - 1){ RP.minute = RP.minutes - 1; rpStop(); }
    rpFrame();
  }, 140);
});
document.getElementById('rp-slider').addEventListener('input', e => {
  rpStop();
  RP.minute = +e.target.value;
  rpFrame();
});

// Match selector: every panel re-renders from the chosen dataset — one
// session alone, or the whole batch. Axes stay fixed (the grid is shared),
// so switching matches never rescales time out from under a comparison.
const dsel = document.getElementById('dsel');
Object.keys(DATASETS).forEach(k => {
  const o = document.createElement('option');
  o.value = k; o.textContent = k;
  dsel.appendChild(o);
});
dsel.addEventListener('change', () => { D = DATASETS[dsel.value]; drawAll(); });

drawAll();
matchMedia('(prefers-color-scheme:dark)').addEventListener('change', drawAll);
</script>
"""

for k, v in [
    ("__DATA__", json.dumps(DATASETS, separators=(",", ":"))),
    ("__MAPIMG__", json.dumps(MAPIMG)),
    ("__BANNER__", (
        '<div class="warnbar" style="border-left-color:var(--warn)">'
        '<strong>Diagnostic run &mdash; every faction pinned at the resource cap.</strong> '
        'This isolates AI BEHAVIOUR from the economy: what it builds and fields when cost '
        'is never a constraint. Nothing about costs, income, pacing or balance can be read '
        'from these numbers.</div>') if rich else ""),
    ("__RESTITLE__", "Resource banks (pinned at the cap)" if rich
     else "What piles up and what starves"),
    ("__MATCHES__", str(V["matches"])), ("__HIT__", str(V["hit"])),
    ("__BUILDFP__", html.escape((BUILD_NAME + " / " + LATEST_FP) if LATEST_FP else "unknown")),
    ("__FM__", str(V["fm"])), ("__TARGET__", str(TARGET)),
    ("__CEILING__", str(CEILING)), ("__LIMIT__", str(LIMIT)),
    ("__STAMP__", time.strftime("%Y-%m-%d %H:%M")),
]:
    doc = doc.replace(k, v)

with open(OUT, "w", encoding="utf-8") as f:
    f.write(doc)

print("%d matches | %d/%d at cap %d (%d%%) | med cap %d | med army %d | %d placements"
      % (V["matches"], V["hit"], V["fm"], TARGET, V["pct"], V["medCap"],
         V["medArmy"], len(places)))
