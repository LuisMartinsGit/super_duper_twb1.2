#!/usr/bin/env python
# Gate culture content by BUILDING and author upgrades as effect-based editable objects.
#  * Age 0 Building_ArcheryRange      -> Age-0 only (Archer; canUpgradeTo Practice Range).
#  * Alanthor Building_Alanthor_PracticeRange -> lvl 1/2/3 ladder + Crossbowman/Longbowman
#    + unit-upgrade pool (each upgrade = list of Buff/EnableAbility effects) + per-level attack.
import os, yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARCHERY = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Age0/Building_ArcheryRange.asset")
PRACTICE = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Alanthor/Building_Alanthor_PracticeRange.asset")

ARCHER, XBOW, LBOW = "Archer", "Alanthor_Crossbowman", "Longbowman"
# UpgradeEffectKind: BuffStat=0, EnableAbility=1.  UnitStat: Hp0 LOS1 Range2 Dmg3 RoF%4
HP, LOS, RNG, DMG, ROF = 0, 1, 2, 3, 4
def buff(stat, amt, unit): return dict(kind=0, unit=unit, stat=stat, amount=amt, ability="")
def enable(ability, unit): return dict(kind=1, unit=unit, stat=0, amount=0, ability=ability)

def U(id, name, effects, req="", s=0, i=0, t=0, desc=""):
    return dict(id=id, displayName=name, description=desc, requires=req, effects=effects,
                cost=dict(Supplies=s, Iron=i), researchTime=t)

def tier(unit, hp, dmg=0):  # standard "+HP, +3 LOS, +2 range [, +dmg]" buff bundle
    e = [buff(HP, hp, unit), buff(LOS, 3, unit), buff(RNG, 2, unit)]
    if dmg: e.append(buff(DMG, dmg, unit))
    return e
def rof_all(pct): return [buff(ROF, pct, ARCHER), buff(ROF, pct, XBOW), buff(ROF, pct, LBOW)]

UPGRADES = [
    U("SeasonedArchers", "Seasoned Archers", tier(ARCHER, 20),           s=80,  i=20, t=25, desc="+20 HP, +3 LOS, +2 range."),
    U("VeteranArchers",  "Veteran Archers",  tier(ARCHER, 20, 1), req="SeasonedArchers", s=120, i=40, t=30, desc="+20 HP, +3 LOS, +2 range, +1 attack."),
    U("EliteArchers",    "Elite Archers",    tier(ARCHER, 50, 2), req="VeteranArchers",  s=200, i=80, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    U("VeteranCrossbowmen","Veteran Crossbowmen", tier(XBOW, 20, 1),     s=120, i=40, t=30, desc="+20 HP, +3 LOS, +2 range, +1 attack."),
    U("EliteCrossbowmen","Elite Crossbowmen", tier(XBOW, 50, 2), req="VeteranCrossbowmen", s=200, i=80, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    U("EliteLongbowmen", "Elite Longbowmen", tier(LBOW, 50, 2),          s=220, i=90, t=40, desc="+50 HP, +3 LOS, +2 range, +2 attack."),
    U("ArrowVolley",     "Arrow Volley",     rof_all(30),                s=120, i=30, t=35, desc="+30% rate of fire for ranged units."),
    U("ArrowShower",     "Arrow Shower",     rof_all(50), req="ArrowVolley", s=200, i=60, t=45, desc="+50% rate of fire for ranged units."),
    U("DeployStakes",    "Deploy Stakes",    [enable("DeployStakes", ARCHER), enable("DeployStakes", XBOW), enable("DeployStakes", LBOW)],
      s=150, i=50, t=40, desc="Unlocks the Deploy Stakes ability on ranged units."),
]

def atk(en, mt=1, dmg=12, rng=22, cd=1.5): return dict(enabled=en, damage=dmg, damageType="ranged", range=rng, cooldown=cd, maxTargets=mt)
PRACTICE_LEVELS = [
    dict(level=1, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=10,
         trains=[ARCHER], availableUpgrades=["SeasonedArchers"], attack=atk(False)),
    dict(level=2, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=20,
         trains=[ARCHER, XBOW], availableUpgrades=["SeasonedArchers","VeteranArchers","VeteranCrossbowmen","ArrowVolley"], attack=atk(True, 1)),
    dict(level=3, variantName="Practice Range", culture="Alanthor", trainSpeedBonusPct=35,
         trains=[ARCHER, XBOW, LBOW],
         availableUpgrades=["SeasonedArchers","VeteranArchers","EliteArchers","VeteranCrossbowmen","EliteCrossbowmen","EliteLongbowmen","ArrowVolley","ArrowShower","DeployStakes"],
         attack=atk(True, 2)),
]
ARCHERY_LEVELS = [dict(level=0, variantName="Archery Range", culture="", trainSpeedBonusPct=0,
                       trains=[ARCHER], availableUpgrades=[], attack=atk(False))]

def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if float(v).is_integer() else repr(v)
    return "0"
def ys(s): return "'" + str(s or "").replace("'", "''") + "'"
def seq(name, items, ind):
    p=" "*ind
    return f"{p}{name}: []\n" if not items else f"{p}{name}:\n" + "".join(f"{p}- {ys(x)}\n" for x in items)
def attack_block(a, ind):
    p=" "*ind
    return (f"{p}attack:\n{p}  enabled: {num(a['enabled'])}\n{p}  damage: {num(a['damage'])}\n{p}  damageType: {ys(a['damageType'])}\n"
            f"{p}  range: {num(a['range'])}\n{p}  cooldown: {num(a['cooldown'])}\n{p}  maxTargets: {num(a['maxTargets'])}\n")
def cost_block(c, ind):
    p=" "*ind; c=c or {}
    return (f"{p}cost:\n{p}  Supplies: {num(c.get('Supplies',0))}\n{p}  Iron: {num(c.get('Iron',0))}\n"
            f"{p}  Crystal: {num(c.get('Crystal',0))}\n{p}  Veilsteel: {num(c.get('Veilsteel',0))}\n{p}  Glow: {num(c.get('Glow',0))}\n")
def levels_block(levels):
    out="  levels:\n" if levels else "  levels: []\n"
    for L in levels:
        out += (f"  - level: {num(L['level'])}\n    variantName: {ys(L['variantName'])}\n    culture: {ys(L['culture'])}\n"
                f"    trainSpeedBonusPct: {num(L['trainSpeedBonusPct'])}\n"
                + seq("trains", L["trains"], 4) + seq("availableUpgrades", L["availableUpgrades"], 4) + attack_block(L["attack"], 4))
    return out
def effects_block(effs, ind):
    p=" "*ind
    if not effs: return f"{p}effects: []\n"
    out=f"{p}effects:\n"
    for e in effs:
        out += (f"{p}- kind: {num(e['kind'])}\n{p}  unit: {ys(e['unit'])}\n{p}  stat: {num(e['stat'])}\n"
                f"{p}  amount: {num(e['amount'])}\n{p}  ability: {ys(e['ability'])}\n")
    return out
def upgrades_block(ups):
    out="  unitUpgrades:\n" if ups else "  unitUpgrades: []\n"
    for u in ups:
        out += (f"  - id: {ys(u['id'])}\n    displayName: {ys(u['displayName'])}\n    description: {ys(u['description'])}\n"
                f"    requires: {ys(u['requires'])}\n" + effects_block(u["effects"], 4) + cost_block(u["cost"], 4)
                + f"    researchTime: {num(u['researchTime'])}\n")
    return out

def write_building(path, *, levels, upgrades, base_attack, canUpgradeTo, flat_trains, description=None):
    raw = open(path, encoding="utf-8").read()
    body=["---" if l.startswith("--- !u!") else l for l in raw.splitlines() if not l.startswith("%")]
    m=yaml.safe_load("\n".join(body))["MonoBehaviour"]
    sg=next((line.split("guid:",1)[1].split(",")[0].strip() for line in raw.splitlines() if "m_Script:" in line and "guid:" in line), None)
    d=m.get("defense") or {}; c=m.get("cost") or {}
    desc = description if description is not None else m.get("description","")
    out=("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
        "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
        "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
        f"  m_Script: {{fileID: 11500000, guid: {sg}, type: 3}}\n"
        f"  m_Name: Building_{m['id']}\n  m_EditorClassIdentifier: \n"
        f"  id: {ys(m['id'])}\n  displayName: {ys(m.get('displayName'))}\n  role: {ys(m.get('role'))}\n"
        f"  description: {ys(desc)}\n  hp: {num(m.get('hp',600))}\n  armorType: {ys(m.get('armorType') or 'structure_human')}\n"
        "  defense:\n"
        f"    melee: {num(d.get('melee',0))}\n    ranged: {num(d.get('ranged',0))}\n    siege: {num(d.get('siege',0))}\n    magic: {num(d.get('magic',0))}\n"
        f"  radius: {num(m.get('radius',1.6))}\n  lineOfSight: {num(m.get('lineOfSight',18))}\n"
        + seq("trains", flat_trains, 2) + seq("research", m.get("research"), 2)
        + f"  minEra: {num(m.get('minEra',0))}\n" + cost_block(c, 2)
        + attack_block(base_attack, 2) + levels_block(levels) + upgrades_block(upgrades)
        + f"  prefabPath: {ys(m.get('prefabPath'))}\n" + seq("canUpgradeTo", canUpgradeTo, 2))
    open(path,"w",encoding="utf-8",newline="\n").write(out)

write_building(ARCHERY, levels=ARCHERY_LEVELS, upgrades=[], base_attack=atk(False),
               canUpgradeTo=["Alanthor_PracticeRange"], flat_trains=[ARCHER])
write_building(PRACTICE, levels=PRACTICE_LEVELS, upgrades=UPGRADES, base_attack=atk(False),
               canUpgradeTo=[], flat_trains=[ARCHER, XBOW, LBOW],
               description="Alanthor's cultured Archery Range. Trains ranged units and unlocks the "
                           "Seasoned/Veteran/Elite ladder; gains its own arrow volley at lvl 2 (double-target at lvl 3).")
print("authored: Archery Range (Age 0) + Practice Range (Alanthor, effect-based upgrades)")
