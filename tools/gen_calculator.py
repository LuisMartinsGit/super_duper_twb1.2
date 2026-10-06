#!/usr/bin/env python
"""Generate the tech-tree calculator from the game's ScriptableObject assets.

    python tools/gen_calculator.py

The SO assets (Assets/GameData/TechTree/**, reached through
Assets/Resources/TechTreeCatalog.asset and AbilityCatalog.asset) are the ONE
source of game data. This script reads them straight off disk (no Unity) and
writes the calculator as a read-only viewer of them:

  tools/calculator/techtree.json   {schema, buildings, abilities}
  tools/calculator/TechTree.html   the DEFAULT_SCHEMA / DEFAULT / DEFAULT_ABILITIES
  tools/calculator/TechTree.jsx    blocks are rewritten to match the JSON exactly

Scope: Age 0 (Assets/GameData/TechTree/Age0/) and Alanthor
(Civs/Alanthor/). Runai, Feraldis, sect and curse content is left out: a
Feraldis_/Runai_ unit a shared building trains, or a tech with culture
Runai/Feraldis, is skipped.

Do not hand-edit the generated files; change the SO in Unity and re-run.
"""
import glob
import json
import os
import re
import sys

import yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TT = os.path.join(ROOT, "Assets", "GameData", "TechTree")
CATALOG = os.path.join(ROOT, "Assets", "Resources", "TechTreeCatalog.asset")
ABILITY_CATALOG = os.path.join(ROOT, "Assets", "Resources", "AbilityCatalog.asset")
OUT_DIR = os.path.join(ROOT, "tools", "calculator")
OUT_JSON = os.path.join(OUT_DIR, "techtree.json")
PAGES = [os.path.join(OUT_DIR, "TechTree.html"), os.path.join(OUT_DIR, "TechTree.jsx")]

SCOPE_PREFIXES = ("Age0/", "Civs/Alanthor/")
CULTURE = "Alanthor"
OUT_OF_SCOPE_CULTURES = ("Runai", "Feraldis")


# ─── Unity YAML ────────────────────────────────────────────────────────────

def read_guid(meta_path):
    with open(meta_path, encoding="utf-8") as f:
        m = re.search(r"^guid: (\w+)", f.read(), re.M)
    return m.group(1) if m else None


def load_asset(path):
    """The MonoBehaviour body of a single-object Unity .asset file."""
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    # Unity writes "--- !u!114 &11400000" document headers YAML cannot read;
    # the asset's own object is the document anchored &11400000.
    heads = re.findall(r"^--- (.*)$", text, flags=re.M)
    docs = re.split(r"^--- .*$", text, flags=re.M)
    body = docs[1] if len(docs) > 1 else text
    for head, doc in zip(heads, docs[1:]):
        if "&11400000" in head:
            body = doc
            break
    try:
        doc = yaml.safe_load(body) or {}
    except yaml.YAMLError as e:
        raise SystemExit("cannot parse %s: %s" % (path, e))
    return doc.get("MonoBehaviour", {}) or {}


def guid_index():
    """guid -> absolute .asset path, for every asset under GameData/TechTree."""
    idx = {}
    for meta in glob.glob(os.path.join(TT, "**", "*.asset.meta"), recursive=True):
        g = read_guid(meta)
        if g:
            idx[g] = meta[:-len(".meta")]
    return idx


def rel(path):
    return os.path.relpath(path, TT).replace(os.sep, "/")


def in_scope(path):
    return rel(path).startswith(SCOPE_PREFIXES)


def catalog_lists(path):
    body = load_asset(path)
    out = {}
    for key, val in body.items():
        if isinstance(val, list) and val and isinstance(val[0], dict) and "guid" in val[0]:
            out[key] = [e["guid"] for e in val if e and e.get("guid")]
    return out


# ─── formatting ────────────────────────────────────────────────────────────

def num(v):
    if v is None:
        return "0"
    try:
        f = float(v)
    except (TypeError, ValueError):
        return str(v)
    if f == int(f):
        return str(int(f))
    return ("%.3f" % f).rstrip("0").rstrip(".")


def s(v):
    return "" if v is None else str(v)


def cost_of(block):
    block = block or {}
    # CostBlock.Veilstone is [FormerlySerializedAs("Crystal")]: an asset not
    # re-saved since the rename still stores it under the old key.
    veil = block.get("Veilstone", block.get("Crystal"))
    return {"s": int(block.get("Supplies") or 0), "i": int(block.get("Iron") or 0),
            "v": int(veil or 0), "vs": int(block.get("Veilsteel") or 0)}


def slug(text):
    return re.sub(r"[^A-Za-z0-9]+", "_", text).strip("_")


def listing(items):
    items = [s(i) for i in (items or []) if s(i)]
    return ", ".join(items) if items else "none"


# ─── schema (the stat rows the page shows / effects can target) ──────────

BUILDING_STATS = [
    ["id", "SO id"],
    ["Building Type", "enum: Basic, Alanthor"],
    ["HP", "hp"],
    ["Armor type", "text"],
    ["Melee defense", "number"],
    ["Ranged defense", "number"],
    ["Siege defense", "number"],
    ["Magic defense", "number"],
    ["Line of sight", "units"],
    ["Building radius", "units"],
    ["Footprint (cells)", "2 m cells"],
    ["Build time", "seconds"],
    ["Population", "pop"],
    ["Supplies per tick", "supplies"],
    ["Supplies interval", "seconds"],
    ["Slot income per min (L1/L2/L3)", "per minute"],
    ["Interest per minute", "per minute"],
    ["Interest principal cap", "resource"],
    ["Interest per minute (Coffers / Charters / Bonds)", "per minute"],
    ["Max per faction", "count"],
    ["Min wall level", "level"],
    ["Garrison slots", "units"],
    ["Garrison arrows per occupant", "arrows"],
    ["Max iron", "iron"],
    ["Max veilstone", "veilstone"],
    ["Segment HP", "hp"],
    ["Segment line of sight", "units"],
    ["Min era", "era"],
    ["Can attack", "boolean"],
    ["Attack damage", "number"],
    ["Attack damage type", "text"],
    ["Attack range", "units"],
    ["Attack cooldown", "seconds"],
    ["Auto-fire max targets", "number"],
    ["Train speed bonus", "percent"],
    ["Trains", "text"],
    ["Research", "text"],
    ["Tags", "text"],
    ["PresentationId", "id"],
]

UNIT_STATS = [
    ["Id", "SO id"],
    ["Class", "text"],
    ["HP", "hp"],
    ["Speed", "units/s"],
    ["Training time", "seconds"],
    ["Population cost", "pop"],
    ["Damage", "number"],
    ["Damage type", "text"],
    ["Armor type", "text"],
    ["Melee defense", "number"],
    ["Ranged defense", "number"],
    ["Siege defense", "number"],
    ["Magic defense", "number"],
    ["Attack cooldown", "seconds"],
    ["Min attack range", "units"],
    ["Attack range", "units"],
    ["Line of sight", "units"],
    ["Aim time", "seconds"],
    ["Radius", "units"],
    ["Trajectory", "text"],
    ["Projectile speed", "units/s"],
    ["Min building level", "level"],
    ["Build speed", "/s"],
    ["Gathering speed", "/s"],
    ["Heals per second", "hp/s"],
    ["Heal range", "units"],
    ["Siege range", "units"],
    ["Siege cooldown", "seconds"],
    ["AoE radius", "units"],
    ["Bonus vs tags", "text"],
    ["Tags", "text"],
    ["Abilities", "text"],
    ["PresentationId", "id"],
]

# effectsList Stat -> calculator stat rows (unit targets)
EFFECT_STATS = {
    "Hp": ["HP"],
    "Damage": ["Damage"],
    "AttackRange": ["Attack range"],
    "LineOfSight": ["Line of sight"],
    "Speed": ["Speed"],
    "AttackCooldown": ["Attack cooldown"],
    "DefenseAll": ["Melee defense", "Ranged defense", "Siege defense", "Magic defense"],
}
EFFECT_OPS = {"Add": "+", "Pct": "+%", "Set": "set"}

ACTIVATIONS = ["Active", "Passive", "OnDeath"]
TARGETINGS = ["self", "single target", "area", "aura", "global"]
AFFECTS = ["self", "allied units of the caster's culture", "all allied units",
           "allied cavalry", "enemies", "allied economy buildings", "allied ranged units"]
EFFECT_KINDS = {
    1: "+{v}% attack", 2: "+{v}% armor", 3: "+{v} armor", 4: "{v}% damage taken",
    5: "{v}% move speed", 6: "self damage {v}% of max HP over the duration",
    7: "HP never drops below {v}", 8: "+{v} charge damage", 9: "reveals fog (radius {v})",
    10: "+{v}% resource yield", 11: "cannot be automated", 12: "line of sight ramps while still",
    13: "+{v}% damage on the next charge", 14: "cannot attack while buffed",
    15: "deploys a field hospital", 16: "summons the pledge army (level-scaled)",
    17: "+{v}% fire rate", 18: "Shardbound Fury (hurls enemies, damages buildings)",
}


# ─── classification ────────────────────────────────────────────────────────

def unit_type(u):
    cls = s(u.get("unitClass")).lower()
    tags = [s(t).lower() for t in (u.get("tags") or [])]
    if float(u.get("buildSpeed") or 0) > 0 or float(u.get("gatheringSpeed") or 0) > 0 \
            or "support" in cls and float(u.get("damage") or 0) == 0 \
            and float(u.get("healsPerSecond") or 0) == 0:
        return "Economic"
    if "siege" in cls or "siege" in tags:
        return "Siege"
    if "cavalry" in s(u.get("armorType")).lower() or "cavalry" in tags:
        return "Cavalry"
    dt = s(u.get("damageType")).lower()
    if dt == "ranged":
        return "Ranged"
    if dt == "magic":
        return "Magic"
    return "Melee"


def unit_size(u):
    for t in (u.get("tags") or []):
        if s(t) in ("Small", "Medium", "Large", "Colossal"):
            return s(t)
    return ""


def unit_race(u):
    for t in (u.get("tags") or []):
        if s(t) in ("Human", "Crystal", "Soulless", "Machine", "Automaton"):
            return s(t)
    return ""


def culture_of(path, so_culture=""):
    if so_culture:
        return so_culture
    return CULTURE if rel(path).startswith("Civs/Alanthor/") else ""


# ─── builders ──────────────────────────────────────────────────────────────

def defense_rows(d):
    d = d or {}
    return [["Melee defense", num(d.get("melee"))], ["Ranged defense", num(d.get("ranged"))],
            ["Siege defense", num(d.get("siege"))], ["Magic defense", num(d.get("magic"))]]


def unit_stats(u):
    bonus = ", ".join("+%s vs %s" % (num(b.get("amount")), s(b.get("vsTag")))
                      for b in (u.get("bonusVsTags") or []) if b) or "none"
    rows = [["Id", s(u.get("id"))], ["Class", s(u.get("unitClass"))], ["HP", num(u.get("hp"))],
            ["Speed", num(u.get("speed"))], ["Training time", num(u.get("trainingTime"))],
            ["Population cost", num(u.get("populationCost"))], ["Damage", num(u.get("damage"))],
            ["Damage type", s(u.get("damageType"))], ["Armor type", s(u.get("armorType"))]]
    rows += defense_rows(u.get("defense"))
    rows += [["Attack cooldown", num(u.get("attackCooldown"))],
             ["Min attack range", num(u.get("minAttackRange"))],
             ["Attack range", num(u.get("attackRange"))], ["Line of sight", num(u.get("lineOfSight"))],
             ["Aim time", num(u.get("aimTime"))], ["Radius", num(u.get("radius"))],
             ["Trajectory", s(u.get("trajectory")) or "none"],
             ["Projectile speed", num(u.get("projectileSpeed"))],
             ["Min building level", num(u.get("minBuildingLevel"))],
             ["Build speed", num(u.get("buildSpeed"))], ["Gathering speed", num(u.get("gatheringSpeed"))],
             ["Heals per second", num(u.get("healsPerSecond"))], ["Heal range", num(u.get("healRange"))],
             ["Siege range", num(u.get("siegeRange"))], ["Siege cooldown", num(u.get("siegeCooldown"))],
             ["AoE radius", num(u.get("aoeRadius"))], ["Bonus vs tags", bonus],
             ["Tags", listing(u.get("tags"))], ["Abilities", listing(u.get("abilities"))],
             ["PresentationId", num(u.get("presentationId"))]]
    return rows


def building_stats(b, btype, trains_names, research_names):
    fp = b.get("footprintCells") or {}
    atk = b.get("attack") or {}
    slots = b.get("slotIncomePerMinute") or []
    rows = [["id", s(b.get("id"))], ["Building Type", btype], ["HP", num(b.get("hp"))],
            ["Armor type", s(b.get("armorType"))]]
    rows += defense_rows(b.get("defense"))
    rows += [["Line of sight", num(b.get("lineOfSight"))], ["Building radius", num(b.get("radius"))],
             ["Footprint (cells)", "%s x %s" % (num(fp.get("x")), num(fp.get("y")))],
             ["Build time", num(b.get("buildTime"))], ["Population", num(b.get("populationProvided"))],
             ["Supplies per tick", num(b.get("suppliesPerTick"))],
             ["Supplies interval", num(b.get("suppliesInterval"))],
             ["Slot income per min (L1/L2/L3)", " / ".join(num(x) for x in slots) if slots else "0"],
             ["Interest per minute", num(b.get("interestPerMinute"))],
             ["Interest principal cap", num(b.get("interestPrincipalCap"))],
             ["Interest per minute (Coffers / Charters / Bonds)",
              " / ".join(num(b.get(k)) for k in ("coffersRate", "merchantChartersRate", "sovereignBondsRate"))],
             ["Max per faction", num(b.get("maxPerFaction"))],
             ["Min wall level", num(b.get("minWallLevel"))],
             ["Garrison slots", num(b.get("garrisonSlots"))],
             ["Garrison arrows per occupant", num(b.get("garrisonArrowsPerOccupant"))],
             ["Max iron", num(b.get("maxIron"))], ["Max veilstone", num(b.get("maxVeilstone"))],
             ["Segment HP", num(b.get("segmentHp"))],
             ["Segment line of sight", num(b.get("segmentLineOfSight"))],
             ["Min era", num(b.get("minEra"))],
             ["Can attack", "yes" if atk.get("enabled") else "no"]]
    if atk.get("enabled"):
        rows += [["Attack damage", num(atk.get("damage"))], ["Attack damage type", s(atk.get("damageType"))],
                 ["Attack range", num(atk.get("range"))], ["Attack cooldown", num(atk.get("cooldown"))],
                 ["Auto-fire max targets", num(atk.get("maxTargets"))]]
    rows += [["Train speed bonus", "0"],
             ["Trains", listing(trains_names)], ["Research", listing(research_names)],
             ["Tags", listing(b.get("tags"))], ["PresentationId", num(b.get("presentationId"))]]
    return rows


def level_lines(lv):
    out = []
    def mult(label, key):
        v = float(lv.get(key) or 0)
        if v and abs(v - 1.0) > 1e-6:
            out.append("%s x%s" % (label, num(v)))
    mult("HP", "hpMultiplier")
    mult("Train time", "trainTimeMultiplier")
    mult("Attack cooldown", "attackCooldownMultiplier")
    if int(lv.get("maxTargets") or 0) > 0:
        out.append("Auto-fire max targets %s" % num(lv.get("maxTargets")))
    if int(lv.get("populationProvided") or 0) > 0:
        out.append("Population %s" % num(lv.get("populationProvided")))
    if float(lv.get("slotIncomePerMinute") or 0) > 0:
        out.append("Slot income %s / min" % num(lv.get("slotIncomePerMinute")))
    if float(lv.get("interestMultiplier") or 0) > 0:
        out.append("Interest x%s" % num(lv.get("interestMultiplier")))
    if float(lv.get("lineOfSight") or 0) > 0:
        out.append("Line of sight %s" % num(lv.get("lineOfSight")))
    atk = lv.get("attack") or {}
    if atk.get("enabled"):
        line = "Attack %s %s, range %s, every %s s, %s target(s)" % (
            num(atk.get("damage")), s(atk.get("damageType")), num(atk.get("range")),
            num(atk.get("cooldown")), num(atk.get("maxTargets")))
        if int(atk.get("siegeShotDamage") or 0) > 0:
            line += ", siege shot %s" % num(atk.get("siegeShotDamage"))
        out.append(line)
    return out


def node(nid, a, r, t, k, **kw):
    n = {"id": nid, "a": a, "r": r, "t": t, "k": k, "o": "", "arw": False, "type": "", "race": "",
         "size": "", "d": [], "req": [], "stats": [], "cost": cost_of(None), "up": False,
         "researchTime": 0, "culture": "", "scope": "global", "on": False, "effects": [],
         "abilities": []}
    n.update(kw)
    return n


def main():
    gidx = guid_index()
    cat = catalog_lists(CATALOG)

    def load_list(key):
        out, missing = [], []
        for g in cat.get(key, []):
            p = gidx.get(g)
            if not p:
                missing.append(g)
                continue
            out.append((p, load_asset(p)))
        if missing:
            print("warning: %d %s catalog entr%s point at no asset under GameData/TechTree"
                  % (len(missing), key, "y" if len(missing) == 1 else "ies"), file=sys.stderr)
        return out

    units = load_list("units")
    buildings = load_list("buildings")
    techs = load_list("technologies")
    levels = load_list("buildingLevels")

    unit_by_id = {s(u.get("id")): (p, u) for p, u in units}

    def resolve_unit(uid):
        for cand in (uid, "Alanthor_" + uid, uid.replace("Alanthor_", "", 1)):
            if cand in unit_by_id:
                return unit_by_id[cand]
        return None, None

    tech_by_id = {s(t.get("id")): (p, t) for p, t in techs}
    levels_by_bld = {}
    for p, lv in levels:
        if s(lv.get("culture")) != CULTURE:
            continue
        levels_by_bld.setdefault(s(lv.get("buildingId")), {})[int(lv.get("level") or 0)] = lv

    def uname(uid):
        p, u = resolve_unit(uid)
        return s(u.get("displayName")) if u else uid

    def tname(tid):
        return s(tech_by_id[tid][1].get("displayName")) if tid in tech_by_id else tid

    out_buildings = []
    placed_units = set()
    warnings = []

    scoped_buildings = [(p, b) for p, b in buildings if in_scope(p)]
    # Age 0 buildings first, then Alanthor; catalog order within each.
    scoped_buildings.sort(key=lambda pb: 0 if rel(pb[0]).startswith("Age0/") else 1)
    all_trained = set()
    for _, b in scoped_buildings:
        for uid in (b.get("trains") or []):
            _, u = resolve_unit(s(uid))
            if u is not None:
                all_trained.add(s(u.get("id")))

    for bpath, b in scoped_buildings:
        bid = s(b.get("id"))
        bdisp = s(b.get("displayName")) or bid
        is_age0 = rel(bpath).startswith("Age0/")
        btype = "Basic" if is_age0 else CULTURE
        lv = levels_by_bld.get(bid, {})

        # what it trains (in scope only), in trains[] order
        train_units = []
        for uid in (b.get("trains") or []):
            uid = s(uid)
            upath, u = resolve_unit(uid)
            if u is None:
                warnings.append("%s trains '%s' which has no UnitDefSO in the catalog" % (bid, uid))
                continue
            if not in_scope(upath) or s(u.get("id")).startswith(("Feraldis_", "Runai_")):
                continue
            train_units.append((upath, u))
        trained_here = {s(u.get("id")) for _, u in train_units}

        # units filed in this building's Units/ folder that NO building trains
        # (the emplacements' crews): shown here, marked as spawned.
        unit_dir = os.path.join(os.path.dirname(bpath), "Units") + os.sep
        spawned_units = [(p, u) for p, u in units
                         if p.startswith(unit_dir) and s(u.get("id")) not in all_trained
                         and s(u.get("id")) not in trained_here]

        # what it researches: derived from TechDefSO.researchAt (the game's rule),
        # catalog order, culture-filtered.
        research = [(p, t) for p, t in techs
                    if s(t.get("researchAt")) == bid
                    and s(t.get("culture")) not in OUT_OF_SCOPE_CULTURES]

        mil = any(unit_type(u) != "Economic" and float(u.get("damage") or 0) > 0
                  for _, u in train_units)

        nodes = []
        next_row = {}

        def take_row(a):
            r = next_row.get(a, 2)
            next_row[a] = r + 1
            return r

        def level_title(n):
            if n == 0 or n not in lv:
                return "%s - Lv %d" % (bdisp, n)
            return "%s - Lv %d" % (s(lv[n].get("displayName")) or bdisp, n)

        def gate_req(minlvl, culture):
            if minlvl <= 0:
                return []
            if culture == CULTURE and minlvl in lv:
                return [level_title(minlvl)]
            return ["%s Lv %d" % (bdisp, minlvl)]

        # row 1: the building's level ladder
        max_lvl = max(lv.keys()) if lv else 0
        if is_age0 or not lv:
            base_a = 0 if is_age0 else 1
            title = level_title(0) if is_age0 else bdisp
            d = [x for x in (s(b.get("role")), s(b.get("description"))) if x]
            nodes.append(node("so_lvl_%s_0" % slug(bid), base_a, 1, title, "lvl",
                              o="mil" if mil else "eco", arw=bool(lv), scope="per",
                              d=d))
        for n_lvl in sorted(lv):
            L = lv[n_lvl]
            req = ["Age 1"] if n_lvl == 1 else [level_title(n_lvl - 1)]
            nodes.append(node("so_lvl_%s_%d" % (slug(bid), n_lvl), min(n_lvl, 3), 1,
                              level_title(n_lvl), "lvl", o="mil" if mil else "eco",
                              arw=n_lvl < max_lvl, scope="per", up=True, culture=CULTURE,
                              d=level_lines(L), req=req, cost=cost_of(L.get("upgradeCost")),
                              researchTime=float(L.get("upgradeSeconds") or 0)))

        # units
        for upath, u in train_units + spawned_units:
            cul = culture_of(upath)
            minlvl = int(u.get("minBuildingLevel") or 0)
            a = minlvl if not cul else max(1, minlvl)
            ut = unit_type(u)
            uid = s(u.get("id"))
            placed_units.add(uid)
            nodes.append(node("so_unit_%s_%s" % (slug(bid), slug(uid)), min(a, 3), take_row(min(a, 3)),
                              s(u.get("displayName")) or uid, "eco" if ut == "Economic" else "mil",
                              type=ut, race=unit_race(u), size=unit_size(u),
                              d=["Trained at %s" % bdisp if uid in trained_here
                                 else "Spawned by %s (not trained)" % bdisp],
                              req=gate_req(minlvl, cul),
                              stats=unit_stats(u), cost=cost_of(u.get("cost")), culture=cul,
                              abilities=[s(x) for x in (u.get("abilities") or []) if s(x)]))

        # research
        for tpath, t in research:
            cul = s(t.get("culture"))
            minlvl = int(t.get("minBuildingLevel") or 0)
            a = minlvl if not cul else max(1, minlvl)
            d, effects = [], []
            for line in (s(t.get("desc")), s(t.get("effect"))):
                if line and line not in d:
                    d.append(line)
            for fx in (t.get("effectsList") or []):
                if not fx:
                    continue
                target, stat, op, val = s(fx.get("Target")), s(fx.get("Stat")), s(fx.get("Op")), fx.get("Value")
                page_op = EFFECT_OPS.get(op)
                page_target = None
                if target.startswith("unit:"):
                    upath, u = resolve_unit(target[5:])
                    if u is not None:
                        page_target = "unit:" + (s(u.get("displayName")) or s(u.get("id")))
                elif target.startswith("type:"):
                    page_target = target
                if page_target and page_op and stat in EFFECT_STATS:
                    for row in EFFECT_STATS[stat]:
                        effects.append({"target": page_target, "stat": row, "op": page_op,
                                        "value": float(val or 0)})
                else:
                    d.append("%s: %s %s %s" % (target, stat, op, num(val)))
            req = [tname(p) for p in (t.get("prerequisites") or []) if s(p)]
            req += gate_req(minlvl, cul)
            tid = s(t.get("id"))
            nodes.append(node("so_tech_%s" % slug(tid), min(a, 3), take_row(min(a, 3)),
                              s(t.get("displayName")) or tid, "tech", up=True, culture=cul,
                              d=d, req=req, cost=cost_of(t.get("cost")),
                              researchTime=float(t.get("researchTime") or 0),
                              scope="global", effects=effects))

        # grid sanity: no two nodes share a cell
        seen = {}
        for n in nodes:
            key = (n["a"], n["r"])
            if key in seen:
                raise SystemExit("layout collision in %s: %s and %s at %s"
                                 % (bid, seen[key], n["t"], key))
            seen[key] = n["t"]

        banner = bdisp
        if lv:
            names = sorted({s(L.get("displayName")) for L in lv.values()} - {bdisp})
            if names:
                banner += " -> " + " / ".join(names) + " (Alanthor)"
        if s(b.get("role")):
            banner += " - " + s(b.get("role"))

        out_buildings.append({
            "id": "so_b_%s" % slug(bid),
            "banner": banner,
            "mil": mil,
            "cost": cost_of(b.get("cost")),
            "stats": building_stats(b, btype, [uname(u.get("id")) for _, u in train_units],
                                    [tname(t.get("id")) for _, t in research]),
            "nodes": nodes,
        })

    for uid, (p, u) in unit_by_id.items():
        if in_scope(p) and uid not in placed_units:
            warnings.append("unit '%s' (%s) is in scope but no in-scope building trains it - not shown"
                            % (uid, rel(p)))

    # abilities: every AbilityDefSO in scope, catalog order
    abilities = []
    for g in catalog_lists(ABILITY_CATALOG).get("abilities", []):
        p = gidx.get(g)
        if not p or not in_scope(p):
            continue
        ab = load_asset(p)
        name = s(ab.get("abilityName"))
        fx = []
        for e in (ab.get("effects") or []):
            k = int(e.get("kind") or 0)
            tmpl = EFFECT_KINDS.get(k)
            if tmpl:
                fx.append(tmpl.format(v=num(e.get("value"))))
        aff = int(ab.get("affects") or 0)
        if 0 < aff < len(AFFECTS):
            fx.append("affects " + AFFECTS[aff])
        if float(ab.get("damage") or 0) > 0:
            fx.append("%s damage" % num(ab.get("damage")))
        if int(ab.get("unlocksAtLevel") or 1) > 1:
            fx.append("unlocks at hero level %s" % num(ab.get("unlocksAtLevel")))
        aftermath = [s(x) for x in (ab.get("aftermath") or []) if s(x)]
        if float(ab.get("cooldown") or 0) > 0:
            aftermath.append("%s s cooldown" % num(ab.get("cooldown")))
        act = int(ab.get("activation") or 0)
        tgt = int(ab.get("targeting") or 0)
        abilities.append({
            "id": "ab_" + slug(name).lower(), "name": name,
            "activation": ACTIVATIONS[act] if act < len(ACTIVATIONS) else "Active",
            "type": TARGETINGS[tgt] if tgt < len(TARGETINGS) else "self",
            "castingTime": float(ab.get("castTime") or 0),
            "castingDuration": float(ab.get("duration") if ab.get("duration") is not None else -1),
            "radius": float(ab.get("radius") or 0), "range": float(ab.get("range") or 0),
            "effects": fx, "aftermath": aftermath,
        })

    schema = {"buildingStats": BUILDING_STATS, "unitStats": UNIT_STATS}
    data = {
        "_generated": "GENERATED by tools/gen_calculator.py from the SO assets "
                      "(Resources/TechTreeCatalog.asset). Read-only - do not edit; change the SO "
                      "and re-run. Scope: Age 0 + Alanthor.",
        "schema": schema,
        "buildings": out_buildings,
        "abilities": abilities,
    }
    # Compact, diff-friendly layout: one schema row / node / ability per line.
    # Valid JSON and valid JS, so the page blocks and the .json are the same text.
    def one(v):
        return json.dumps(v, ensure_ascii=False)

    def dump_schema(sch, pad=""):
        parts = []
        for key in ("buildingStats", "unitStats"):
            rows = (",\n" + pad + "    ").join(one(r) for r in sch[key])
            parts.append('%s  "%s": [\n%s    %s\n%s  ]' % (pad, key, pad, rows, pad))
        return "{\n" + ",\n".join(parts) + "\n" + pad + "}"

    def dump_buildings(bs, pad=""):
        out = []
        for b in bs:
            head = {k: v for k, v in b.items() if k != "nodes"}
            fields = [pad + "    %s: %s" % (one(k), one(v)) for k, v in head.items()]
            nodes = (",\n" + pad + "      ").join(one(n) for n in b["nodes"])
            fields.append(pad + '    "nodes": [\n' + pad + "      " + nodes + "\n" + pad + "    ]")
            out.append(pad + "  {\n" + ",\n".join(fields) + "\n" + pad + "  }")
        return "[\n" + ",\n".join(out) + "\n" + pad + "]"

    def dump_list(items, pad=""):
        return "[\n" + ",\n".join(pad + "  " + one(i) for i in items) + "\n" + pad + "]"

    with open(OUT_JSON, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "_generated": %s,\n' % one(data["_generated"]))
        f.write('  "schema": %s,\n' % dump_schema(schema, "  "))
        f.write('  "buildings": %s,\n' % dump_buildings(out_buildings, "  "))
        f.write('  "abilities": %s\n' % dump_list(abilities, "  "))
        f.write("}\n")

    # rewrite the three generated blocks in each page
    blocks = [
        ("const DEFAULT_SCHEMA = ", "\n};\n", dump_schema(schema)),
        ("const DEFAULT = ", "\n];\n", dump_buildings(out_buildings)),
        ("const DEFAULT_ABILITIES = ", "\n];\n", dump_list(abilities)),
    ]
    for page in PAGES:
        with open(page, encoding="utf-8-sig") as f:
            text = f.read()
        for start, end, body in blocks:
            i = text.find(start)
            if i < 0:
                raise SystemExit("%s: '%s' block not found" % (page, start.strip()))
            j = text.find(end, i)
            if j < 0:
                raise SystemExit("%s: end of '%s' block not found" % (page, start.strip()))
            text = text[:i] + start + body + ";\n" + text[j + len(end):]
        with open(page, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)

    n_nodes = sum(len(b["nodes"]) for b in out_buildings)
    print("wrote %s: %d buildings, %d nodes, %d abilities"
          % (os.path.relpath(OUT_JSON, ROOT), len(out_buildings), n_nodes, len(abilities)))
    for p in PAGES:
        print("rewrote generated blocks in %s" % os.path.relpath(p, ROOT))
    for w in warnings:
        print("note: " + w)


if __name__ == "__main__":
    main()
