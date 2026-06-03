#!/usr/bin/env python
# Mirror the archery-range logic for melee:
#  * Age 0 Building_Barracks  -> Age-0 only (trains Spearman; canUpgradeTo KingsCourt).
#  * Alanthor Building_KingsCourt -> lvl 1/2/3 ladder + Swordsman/Sentinel + effect-based
#    unit-upgrade pool (Seasoned/Veteran/Elite Infantry + Shield Wall + Charge).
import os, yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BARRACKS = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Age0/Building_Barracks.asset")
KINGS    = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Alanthor/Building_KingsCourt.asset")

SPEAR, SWORD, SENT = "Spearman", "Swordsman", "Alanthor_Sentinel"
# UnitStat: Hp0 LOS1 Range2 Dmg3 RoF%4 Speed5 Cd6 Carry7 Defense8 MoveSpeed%9
HP, DMG, DEF, MOVE = 0, 3, 8, 9
def buff(stat, amt, unit): return dict(kind=0, unit=unit, stat=stat, amount=amt, ability="")
def enable(ability, unit): return dict(kind=1, unit=unit, stat=0, amount=0, ability=ability)
def U(id, name, effects, req="", s=0, i=0, t=0, desc=""):
    return dict(id=id, displayName=name, description=desc, requires=req, effects=effects,
                cost=dict(Supplies=s, Iron=i), researchTime=t)
def melee_tier(units, hp, move, dmg, dfn):
    e = []
    for u in units:
        e += [buff(HP, hp, u), buff(MOVE, move, u), buff(DMG, dmg, u), buff(DEF, dfn, u)]
    return e

UPGRADES = [
    U("SeasonedInfantry", "Seasoned Spearmen", melee_tier([SPEAR], 30, 5, 1, 1),
      s=100, i=40, t=28, desc="Spearmen: +30 HP, +5% move speed, +1 attack, +1 defense."),
    U("ShieldWall", "Shield Wall", [enable("ShieldWall", SPEAR)],
      s=120, i=50, t=30, desc="Enables the Shield Wall ability for Spearmen."),
    U("VeteranInfantry", "Veteran Infantry", melee_tier([SPEAR, SWORD], 30, 10, 1, 1),
      req="SeasonedInfantry", s=180, i=80, t=35, desc="Spearmen + Swordsmen: +30 HP, +10% move speed, +1 attack, +1 defense."),
    U("Charge", "Charge", [enable("Charge", SPEAR), enable("Charge", SWORD)],
      s=160, i=70, t=33, desc="Enables the Charge ability for Spearmen and Swordsmen."),
    U("EliteInfantry", "Elite Infantry", melee_tier([SPEAR, SWORD], 30, 15, 2, 2),
      req="VeteranInfantry", s=280, i=120, t=45, desc="Spearmen + Swordsmen: +30 HP, +15% move speed, +2 attack, +2 defense."),
]

def atk(en, mt=1, dmg=12, rng=22, cd=1.5): return dict(enabled=en, damage=dmg, damageType="ranged", range=rng, cooldown=cd, maxTargets=mt)
KINGS_LEVELS = [
    dict(level=1, variantName="King's Court", culture="Alanthor", trainSpeedBonusPct=0,
         trains=[SPEAR], availableUpgrades=["SeasonedInfantry", "ShieldWall"], attack=atk(False)),
    dict(level=2, variantName="King's Court", culture="Alanthor", trainSpeedBonusPct=0,
         trains=[SPEAR, SWORD],
         availableUpgrades=["SeasonedInfantry", "ShieldWall", "VeteranInfantry", "Charge"], attack=atk(False)),
    dict(level=3, variantName="King's Court", culture="Alanthor", trainSpeedBonusPct=0,
         trains=[SPEAR, SWORD, SENT],
         availableUpgrades=["SeasonedInfantry", "ShieldWall", "VeteranInfantry", "Charge", "EliteInfantry"], attack=atk(False)),
]
BARRACKS_LEVELS = [dict(level=0, variantName="Barracks", culture="", trainSpeedBonusPct=0,
                        trains=[SPEAR], availableUpgrades=[], attack=atk(False))]

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

def write_building(path, *, levels, upgrades, canUpgradeTo, flat_trains, description=None):
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
        f"  description: {ys(desc)}\n  hp: {num(m.get('hp',800))}\n  armorType: {ys(m.get('armorType') or 'structure_human')}\n"
        "  defense:\n"
        f"    melee: {num(d.get('melee',0))}\n    ranged: {num(d.get('ranged',0))}\n    siege: {num(d.get('siege',0))}\n    magic: {num(d.get('magic',0))}\n"
        f"  radius: {num(m.get('radius',1.6))}\n  lineOfSight: {num(m.get('lineOfSight',18))}\n"
        + seq("trains", flat_trains, 2) + seq("research", m.get("research"), 2)
        + f"  minEra: {num(m.get('minEra',0))}\n" + cost_block(c, 2)
        + attack_block(atk(False), 2) + levels_block(levels) + upgrades_block(upgrades)
        + f"  prefabPath: {ys(m.get('prefabPath'))}\n" + seq("canUpgradeTo", canUpgradeTo, 2))
    open(path,"w",encoding="utf-8",newline="\n").write(out)

write_building(BARRACKS, levels=BARRACKS_LEVELS, upgrades=[], canUpgradeTo=["KingsCourt"], flat_trains=[SPEAR])
write_building(KINGS, levels=KINGS_LEVELS, upgrades=UPGRADES, canUpgradeTo=[], flat_trains=[SPEAR, SWORD, SENT],
               description="Alanthor's cultured melee hall. Trains Spearmen, then Swordsmen (lvl 2) and "
                           "Sentinels (lvl 3); unlocks the Seasoned/Veteran/Elite infantry upgrades plus "
                           "Shield Wall and Charge.")
print("authored: Barracks (Age 0, Spearman) + King's Court (Alanthor melee ladder)")
