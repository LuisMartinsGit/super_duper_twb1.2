#!/usr/bin/env python
# Author the Archery Range building SO: keep its existing flat fields, add the level
# ladder (lvl 0 pre-culture -> lvl 1..3 Alanthor "Practice Range"), the unit-upgrade
# pool, and per-level building ranged attack. Idempotent (rewrites the whole asset
# from current values + this spec).
import os, yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
F = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Age0/Building_ArcheryRange.asset")

# ---------- spec ----------
def up(id, name, applies, hp=0, los=0, rng=0, dmg=0, rof=0, ability="", req="",
       s=0, i=0, t=0, desc=""):
    return dict(id=id, displayName=name, description=desc, requires=req, appliesTo=applies,
                addHp=hp, addLineOfSight=los, addAttackRange=rng, addDamage=dmg,
                rateOfFireBonusPct=rof, unlocksAbility=ability,
                cost=dict(Supplies=s, Iron=i), researchTime=t)

UPGRADES = [
    up("SeasonedArchers", "Seasoned Archers", ["Archer"], hp=20, los=3, rng=2, s=80, i=20, t=25,
       desc="+20 HP, +3 LOS, +2 attack range."),
    up("VeteranArchers", "Veteran Archers", ["Archer"], hp=20, los=3, rng=2, dmg=1,
       req="SeasonedArchers", s=120, i=40, t=30, desc="+20 HP, +3 LOS, +2 range, +1 attack."),
    up("EliteArchers", "Elite Archers", ["Archer"], hp=50, los=3, rng=2, dmg=2,
       req="VeteranArchers", s=200, i=80, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    up("VeteranCrossbowmen", "Veteran Crossbowmen", ["Crossbowman"], hp=20, los=3, rng=2, dmg=1,
       s=120, i=40, t=30, desc="+20 HP, +3 LOS, +2 range, +1 attack."),
    up("EliteCrossbowmen", "Elite Crossbowmen", ["Crossbowman"], hp=50, los=3, rng=2, dmg=2,
       req="VeteranCrossbowmen", s=200, i=80, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    up("EliteLongbowmen", "Elite Longbowmen", ["Longbowman"], hp=50, los=3, rng=2, dmg=2,
       s=220, i=90, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    up("ArrowVolley", "Arrow Volley", ["Archer", "Crossbowman", "Longbowman"], rof=30,
       s=120, i=30, t=35, desc="+30% rate of fire for ranged units."),
    up("ArrowShower", "Arrow Shower", ["Archer", "Crossbowman", "Longbowman"], rof=50,
       req="ArrowVolley", s=200, i=60, t=45, desc="+50% rate of fire for ranged units."),
    up("DeployStakes", "Deploy Stakes", ["Archer", "Crossbowman", "Longbowman"],
       ability="DeployStakes", s=150, i=50, t=40, desc="Unlocks the Deploy Stakes ability on ranged units."),
]

def atk(enabled, mt=1, dmg=12, rng=22, cd=1.5):
    return dict(enabled=enabled, damage=dmg, damageType="ranged", range=rng, cooldown=cd, maxTargets=mt)

LEVELS = [
    dict(level=0, variantName="Archery Range", culture="", trainSpeedBonusPct=0,
         trains=["Archer"], availableUpgrades=[], attack=atk(False)),
    dict(level=1, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=10,
         trains=["Archer"], availableUpgrades=["SeasonedArchers"], attack=atk(False)),
    dict(level=2, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=20,
         trains=["Archer", "Crossbowman"],
         availableUpgrades=["SeasonedArchers", "VeteranArchers", "VeteranCrossbowmen", "ArrowVolley"],
         attack=atk(True, mt=1)),
    dict(level=3, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=35,
         trains=["Archer", "Crossbowman", "Longbowman"],
         availableUpgrades=["SeasonedArchers", "VeteranArchers", "EliteArchers",
                            "VeteranCrossbowmen", "EliteCrossbowmen", "EliteLongbowmen",
                            "ArrowVolley", "ArrowShower", "DeployStakes"],
         attack=atk(True, mt=2)),
]

# ---------- emit helpers ----------
def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if float(v).is_integer() else repr(v)
    return "0"
def ys(s): return "'" + str(s or "").replace("'", "''") + "'"
def seq(name, items, ind):
    pad = " " * ind
    if not items: return f"{pad}{name}: []\n"
    return f"{pad}{name}:\n" + "".join(f"{pad}- {ys(x)}\n" for x in items)
def attack_block(a, ind):
    p = " " * ind
    return (f"{p}attack:\n"
            f"{p}  enabled: {num(a['enabled'])}\n{p}  damage: {num(a['damage'])}\n"
            f"{p}  damageType: {ys(a['damageType'])}\n{p}  range: {num(a['range'])}\n"
            f"{p}  cooldown: {num(a['cooldown'])}\n{p}  maxTargets: {num(a['maxTargets'])}\n")
def cost_block(c, ind):
    p = " " * ind; c = c or {}
    return (f"{p}cost:\n{p}  Supplies: {num(c.get('Supplies',0))}\n{p}  Iron: {num(c.get('Iron',0))}\n"
            f"{p}  Crystal: {num(c.get('Crystal',0))}\n{p}  Veilsteel: {num(c.get('Veilsteel',0))}\n"
            f"{p}  Glow: {num(c.get('Glow',0))}\n")

def levels_block(levels):
    out = "  levels:\n"
    for L in levels:
        out += (f"  - level: {num(L['level'])}\n"
                f"    variantName: {ys(L['variantName'])}\n    culture: {ys(L['culture'])}\n"
                f"    trainSpeedBonusPct: {num(L['trainSpeedBonusPct'])}\n"
                + seq("trains", L["trains"], 4) + seq("availableUpgrades", L["availableUpgrades"], 4)
                + attack_block(L["attack"], 4))
    return out

def upgrades_block(ups):
    out = "  unitUpgrades:\n"
    for u in ups:
        out += (f"  - id: {ys(u['id'])}\n    displayName: {ys(u['displayName'])}\n"
                f"    description: {ys(u['description'])}\n    requires: {ys(u['requires'])}\n"
                + seq("appliesTo", u["appliesTo"], 4) +
                f"    addHp: {num(u['addHp'])}\n    addLineOfSight: {num(u['addLineOfSight'])}\n"
                f"    addAttackRange: {num(u['addAttackRange'])}\n    addDamage: {num(u['addDamage'])}\n"
                f"    rateOfFireBonusPct: {num(u['rateOfFireBonusPct'])}\n"
                f"    unlocksAbility: {ys(u['unlocksAbility'])}\n"
                + cost_block(u["cost"], 4) + f"    researchTime: {num(u['researchTime'])}\n")
    return out

# ---------- load current flat fields ----------
raw = open(F, encoding="utf-8").read()
body = [l for l in raw.splitlines() if not l.startswith("%")]
body = ["---" if l.startswith("--- !u!") else l for l in body]
m = yaml.safe_load("\n".join(body))["MonoBehaviour"]
sg = None
for line in raw.splitlines():
    if "m_Script:" in line and "guid:" in line:
        sg = line.split("guid:", 1)[1].split(",")[0].strip()

d = m.get("defense") or {}; c = m.get("cost") or {}
out = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
    "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
    "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
    "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
    f"  m_Script: {{fileID: 11500000, guid: {sg}, type: 3}}\n"
    "  m_Name: Building_ArcheryRange\n  m_EditorClassIdentifier: \n"
    f"  id: {ys(m['id'])}\n  displayName: {ys(m.get('displayName'))}\n  role: {ys(m.get('role'))}\n"
    f"  description: {ys(m.get('description'))}\n"
    f"  hp: {num(m.get('hp',600))}\n  armorType: {ys(m.get('armorType') or 'structure_human')}\n"
    "  defense:\n"
    f"    melee: {num(d.get('melee',0))}\n    ranged: {num(d.get('ranged',0))}\n"
    f"    siege: {num(d.get('siege',0))}\n    magic: {num(d.get('magic',0))}\n"
    f"  radius: {num(m.get('radius',1.6))}\n  lineOfSight: {num(m.get('lineOfSight',18))}\n"
    + seq("trains", m.get("trains"), 2) + seq("research", m.get("research"), 2)
    + f"  minEra: {num(m.get('minEra',0))}\n" + cost_block(c, 2)
    + attack_block(atk(False), 2)            # base attack (disabled; per-level governs)
    + levels_block(LEVELS) + upgrades_block(UPGRADES)
    + f"  prefabPath: {ys(m.get('prefabPath'))}\n" + seq("canUpgradeTo", m.get("canUpgradeTo"), 2))

open(F, "w", encoding="utf-8", newline="\n").write(out)
print("authored Archery Range: levels=%d upgrades=%d" % (len(LEVELS), len(UPGRADES)))
