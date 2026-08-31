"""
Aggregate a batch of headless matches into one report.

    python tools/aggregate-metrics.py <logs-root> [--csv out.csv]

Reads every logs/<session>/ folder written by MatchMetrics and answers the
questions a single match cannot: not "what happened", but "what happens".
"""
import csv, os, sys, glob, collections, statistics as st

root = sys.argv[1] if len(sys.argv) > 1 else "logs"
sessions = sorted(d for d in glob.glob(os.path.join(root, "*")) if os.path.isdir(d))
if not sessions:
    print("no session folders under", root); raise SystemExit(1)


def rows(session, name):
    p = os.path.join(session, name)
    if not os.path.exists(p): return []
    with open(p, newline="", encoding="utf-8", errors="ignore") as fh:
        return list(csv.DictReader(fh))


def num(x, d=0):
    try: return int(float(x))
    except (TypeError, ValueError): return d


runs = []
for s in sessions:
    fac = rows(s, "Metrics_Faction.csv")
    if not fac: continue
    last_t = max(num(r["t"]) for r in fac)
    final = [r for r in fac if num(r["t"]) == last_t]
    runs.append(dict(session=os.path.basename(s), t=last_t, final=final,
                     faction=fac,
                     units=rows(s, "Metrics_Units.csv"),
                     builds=rows(s, "Metrics_Buildings.csv"),
                     research=rows(s, "Metrics_Research.csv"),
                     place=rows(s, "Metrics_Placement.csv")))

if not runs:
    print("session folders exist but hold no Metrics_Faction.csv —"
          " did the runs get past the loading screen?"); raise SystemExit(1)

print("=" * 74)
print("%d matches, median length %ds" % (len(runs), st.median(r["t"] for r in runs)))
print("=" * 74)


def spread(vals):
    vals = sorted(vals)
    if not vals: return "-"
    return "min %-5d med %-5d max %-5d" % (vals[0], st.median(vals), vals[-1])


# ── the headline: did anyone approach the population ceiling? ───────────
print("\nPOPULATION  (target: 200 reached inside 20 min)")
for label, key in (("peak pop", "pop"), ("peak cap", "popMax")):
    peaks = []
    for r in runs:
        by = collections.defaultdict(int)
        for row in r["faction"]:
            by[row["faction"]] = max(by[row["faction"]], num(row[key]))
        peaks += list(by.values())
    print("  %-10s %s" % (label, spread(peaks)))

# ── armies ─────────────────────────────────────────────────────────────
print("\nARMY")
peaks, finals = [], []
for r in runs:
    by = collections.defaultdict(int)
    for row in r["faction"]:
        by[row["faction"]] = max(by[row["faction"]], num(row["units"]))
    peaks += list(by.values())
    finals += [num(f["units"]) for f in r["final"]]
print("  peak units   %s" % spread(peaks))
print("  final units  %s" % spread(finals))

# ── composition ────────────────────────────────────────────────────────
print("\nARMY COMPOSITION  (share of all unit-samples across every match)")
comp = collections.Counter()
for r in runs:
    for row in r["units"]:
        comp[row["unitId"]] += num(row["count"])
tot = sum(comp.values()) or 1
for uid, n in comp.most_common(14):
    print("  %-26s %6.1f%%  %s" % (uid, 100.0 * n / tot, "#" * int(40.0 * n / tot)))

# ── buildings ──────────────────────────────────────────────────────────
print("\nBUILDINGS  (mean count per faction at end of match)")
bfinal = collections.Counter(); nfac = 0
for r in runs:
    last_t = r["t"]
    seen = collections.Counter()
    for row in r["builds"]:
        if num(row["t"]) == last_t: seen[row["buildingId"]] += num(row["count"])
    nfac += len({f["faction"] for f in r["final"]})
    bfinal += seen
for bid, n in bfinal.most_common(16):
    print("  %-26s %5.1f" % (bid, n / max(1, nfac)))

# ── research ───────────────────────────────────────────────────────────
print("\nRESEARCH  (how often a tech is completed, per faction-match)")
res = collections.Counter(); fm = 0
for r in runs:
    fm += len({f["faction"] for f in r["final"]})
    for row in r["research"]:
        res[row["tech"]] += 1
if res:
    for tech, n in res.most_common(14):
        print("  %-26s %5.0f%%" % (tech, 100.0 * n / max(1, fm)))
else:
    print("  (none completed in any match)")

# ── placement ──────────────────────────────────────────────────────────
print("\nPLACEMENT  (buildings per region, mean across matches)")
per = collections.Counter()
for r in runs:
    for row in r["place"]:
        per[row["region"]] += 1
if per:
    for reg, n in sorted(per.items(), key=lambda kv: -kv[1])[:10]:
        print("  region %-6s %5.1f" % (reg, n / len(runs)))
else:
    print("  (no placement dump — match may have been killed before the limit)")

# ── outcome ────────────────────────────────────────────────────────────
print("\nOUTCOME  (final territories per faction)")
terr = []
for r in runs:
    terr += [num(f["territories"]) for f in r["final"]]
print("  territories  %s" % spread(terr))
wiped = sum(1 for r in runs for f in r["final"] if num(f["units"]) == 0)
print("  factions finishing with NO army: %d of %d (%.0f%%)"
      % (wiped, len(terr), 100.0 * wiped / max(1, len(terr))))

if "--csv" in sys.argv:
    out = sys.argv[sys.argv.index("--csv") + 1]
    with open(out, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["session", "faction", "t", "pop", "popMax", "supplies", "iron",
                    "veilstone", "veilsteel", "territories", "units", "buildings"])
        for r in runs:
            for f in r["final"]:
                w.writerow([r["session"], f["faction"], f["t"], f["pop"], f["popMax"],
                            f["supplies"], f["iron"], f["veilstone"], f["veilsteel"],
                            f["territories"], f["units"], f["buildings"]])
    print("\nper-faction finals -> %s" % out)
