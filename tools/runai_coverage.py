#!/usr/bin/env python3
"""Runai coverage over a batch of headless matches (docs/Design/Age_1_Runai.md).

    python tools/runai_coverage.py <logs-root> [...]  [--since <stamp>] [--json out.json]

For every match: which factions CHOSE Runai (their AI's landmark pick), which
aged up into it, and — for each Runai faction — which of the culture's
buildings, units, technologies and abilities the AI actually used, and which
it never touched. The roster is read from the Runai SOs, so it follows the data.
"""
import csv, glob, json, os, re, sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TT = os.path.join(ROOT, "Assets", "GameData", "TechTree")

def so_ids(pattern, field="id"):
    out = set()
    for p in glob.glob(os.path.join(TT, pattern), recursive=True):
        m = re.search(r"(?m)^  " + field + r": (\S+)", open(p, encoding="utf-8", errors="replace").read())
        if m: out.add(m.group(1).strip("'\""))
    return out

TECHS = sorted(t for t in so_ids("Civs/Runai/**/Research/*.asset") if t.startswith("Runai_"))
TRAINED = ["Spearman", "Runai_DuneGuard", "Runai_RoadWarden", "Runai_Sandstrider",
           "Runai_Skirmisher", "Runai_Slinger", "Runai_SunArcher",
           "Runai_Raider", "Runai_HorseArcher", "Runai_CamelLancer",
           "Runai_SkyBarge", "Runai_WarWagon", "Runai_SiegeCaravan",
           "Runai_CaravanMaster", "Runai_Wayfinder"]
AUTO = ["Runai_Caravan", "Runai_EscortGuard", "Runai_EscortArcher", "Runai_EscortRider"]
BUILDINGS = ["ThessarasCrossing", "Runai_GrazingGrounds", "Runai_Arrowyard", "Runai_SiegeWorkshop",
             "Runai_Sanctuary", "Hut (Waystation)", "GatherersHut (Trading Post)", "Mine"]
ABILITIES = ["Scatter", "Wayfinding", "Mirage", "Caravan of Swords", "Exodus", "Unload", "The Call"]

def rows(path):
    try:
        with open(path, newline="", encoding="utf-8", errors="replace") as f:
            return list(csv.DictReader(f))
    except OSError:
        return []

def analyse(mdir):
    m = {"match": os.path.basename(mdir.rstrip("/\\")), "factions": {}}
    summ = os.path.join(mdir, "Summary.txt")
    if os.path.exists(summ):
        for l in open(summ, encoding="utf-8", errors="replace"):
            if l.startswith(("Outcome", "Duration", "AI ", "Score")):
                m[l.split(":")[0].strip().lower()] = l.split(":", 1)[1].strip()
    units = rows(os.path.join(mdir, "Metrics_Units.csv"))
    events = rows(os.path.join(mdir, "Metrics_BuildingEvents.csv"))
    research = rows(os.path.join(mdir, "Metrics_Research.csv"))
    income = rows(os.path.join(mdir, "Metrics_Income.csv"))
    for log in glob.glob(os.path.join(mdir, "AI_*.log")):
        fac = os.path.basename(log)[3:-4]
        text = open(log, encoding="utf-8", errors="replace").read()
        pick = re.search(r"CULTURE: age-up landmark = (\S+) \((\w+)\)", text)
        f = {"pick": pick.group(2) if pick else "?"}
        fu = defaultdict(int); first_caravan = None
        for r in units:
            if r["faction"] != fac: continue
            fu[r["unitId"]] = max(fu[r["unitId"]], int(r["count"]))
            if r["unitId"] == "Runai_Caravan" and first_caravan is None: first_caravan = int(r["t"])
        f["agedRunai"] = first_caravan is not None
        if f["pick"] != "Runai" and not f["agedRunai"]:
            m["factions"][fac] = f; continue
        f["ageUpBy"] = first_caravan
        built = defaultdict(int)
        for r in events:
            if r["faction"] != fac or r["event"] != "add": continue
            b = r["buildingId"]; t = int(r["t"])
            if b == "Hut" and first_caravan is not None and t >= first_caravan: b = "Hut (Waystation)"
            if b == "GatherersHut" and first_caravan is not None and t >= first_caravan: b = "GatherersHut (Trading Post)"
            built[b] += 1
        techs = {r["tech"] for r in research if r["faction"] == fac}
        casts = defaultdict(int)
        for a in re.findall(r"ABILITY: (.+?) cast", text): casts[a] += 1
        trade = sum(float(r["supplies"]) + float(r["iron"]) + float(r["veilstone"]) + float(r["veilsteel"])
                    for r in income if r["faction"] == fac and r["flow"] == "in" and r["source"].lower() == "trade")
        f["buildings"] = {b: built.get(b, 0) for b in BUILDINGS}
        f["units"] = {u: fu.get(u, 0) for u in TRAINED + AUTO + ["Runai_MirageRider"]}
        f["techs"] = sorted(t for t in techs if t in TECHS)
        f["techsMissing"] = [t for t in TECHS if t not in techs]
        f["abilities"] = {a: casts.get(a, 0) for a in ABILITIES}
        f["veilLadder"] = sum(1 for t in ("Runai_VeiledPilgrims", "Runai_HushedVeil", "Runai_Communion",
                                          "Runai_Veilbound", "Runai_VoiceOfTheVeil") if t in techs)
        f["tradeIncomeSampled"] = round(trade)
        m["factions"][fac] = f
    return m

def main(argv):
    out_json = None
    since = ""
    if "--since" in argv:
        i = argv.index("--since"); since = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    if "--json" in argv:
        i = argv.index("--json"); out_json = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    matches = []
    for root in argv:
        for mdir in sorted(glob.glob(os.path.join(root, "*_*"))):
            if os.path.basename(mdir) < since: continue
            if os.path.isdir(mdir) and os.path.exists(os.path.join(mdir, "Metrics_Faction.csv")):
                matches.append(analyse(mdir))
    agg_units, agg_bld, agg_tech, agg_ab = defaultdict(int), defaultdict(int), defaultdict(int), defaultdict(int)
    runai_f = chose = aged = 0
    for m in matches:
        print(f"\n== {m['match']}  {m.get('outcome', '?')}  ({m.get('duration', '?')})")
        for fac, f in sorted(m["factions"].items()):
            if f["pick"] == "Runai": chose += 1
            if not f.get("agedRunai"):
                print(f"  {fac:7s} picked {f['pick']}" + ("  — did NOT age up" if f["pick"] == "Runai" else ""))
                continue
            aged += 1; runai_f += 1
            used_u = [u for u, n in f["units"].items() if n]
            for u in used_u: agg_units[u] += 1
            for b, n in f["buildings"].items():
                if n: agg_bld[b] += 1
            for t in f["techs"]: agg_tech[t] += 1
            for a, n in f["abilities"].items():
                if n: agg_ab[a] += 1
            print(f"  {fac:7s} RUNAI (aged by {f['ageUpBy']}s) | units {len(used_u)}/{len(f['units'])} | "
                  f"techs {len(f['techs'])}/{len(TECHS)} | veil ladder {f['veilLadder']}/5 | trade (sampled) {f['tradeIncomeSampled']}")
            print(f"          buildings: " + ", ".join(f"{b} {n}" for b, n in f["buildings"].items()))
            print(f"          never trained: " + (", ".join(u for u, n in f["units"].items() if not n) or "-"))
            print(f"          abilities: " + ", ".join(f"{a} {n}" for a, n in f["abilities"].items()))
    print(f"\n== Over the batch: {chose} AI faction(s) chose Runai, {aged} aged up into it.")
    if runai_f:
        def line(title, keys, agg):
            print(f"{title}: " + ", ".join(f"{k} {agg.get(k, 0)}/{runai_f}" for k in keys))
        line("Units", TRAINED + AUTO + ["Runai_MirageRider"], agg_units)
        line("Buildings", BUILDINGS, agg_bld)
        line("Abilities", ABILITIES, agg_ab)
        never = [t for t in TECHS if not agg_tech.get(t)]
        print(f"Techs researched by any Runai AI: {len(TECHS) - len(never)}/{len(TECHS)}")
        print("Never researched: " + (", ".join(never) or "-"))
    if out_json:
        json.dump(matches, open(out_json, "w"), indent=1)

if __name__ == "__main__":
    main(sys.argv[1:])
