#!/usr/bin/env python
# Create the missing Spearman unit + the Alanthor "Garrison" building (the cultured
# Barracks), and put the melee ladder on the Garrison. Barracks -> canUpgradeTo Garrison.
import os, hashlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITS_AGE0 = os.path.join(ROOT, "Assets/GameData/TechTree/Units/Age0")
BLD_ALAN   = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Alanthor")
UNIT_GUID = "b6fc2bbfed21e12438424e2ff0b5dd06"   # UnitDefSO.cs
BLD_GUID  = "b98b52f4032a91549814ef127e8e0bff"   # BuildingDefSO.cs

def guid(seed): return hashlib.md5(("twb-stat-so::" + seed).encode()).hexdigest()[:32]
def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if float(v).is_integer() else repr(v)
    return "0"
def ys(s): return "'" + str(s or "").replace("'", "''") + "'"
def seq(name, items, ind):
    p=" "*ind
    return f"{p}{name}: []\n" if not items else f"{p}{name}:\n" + "".join(f"{p}- {ys(x)}\n" for x in items)
def asset_meta(g):
    return ("fileFormatVersion: 2\n" f"guid: {g}\n"
            "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
HDR=("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
     "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
     "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
     "  m_Enabled: 1\n  m_EditorHideFlags: 0\n")

# ---------------- Spearman unit (Age 0, from docs/Design/Age_0.md) ----------------
def write_spearman():
    path = os.path.join(UNITS_AGE0, "Unit_Spearman.asset")
    body = (HDR + f"  m_Script: {{fileID: 11500000, guid: {UNIT_GUID}, type: 3}}\n"
        "  m_Name: Unit_Spearman\n  m_EditorClassIdentifier: \n"
        "  id: 'Spearman'\n  displayName: 'Spearman'\n  unitClass: 'human_melee'\n"
        "  hp: 120\n  speed: 5.5\n  trainingTime: 7\n  damage: 10\n  damageType: 'melee'\n"
        "  armorType: 'infantry_heavy'\n"
        "  defense:\n    melee: 1\n    ranged: 0\n    siege: 0\n    magic: 0\n"
        "  attackCooldown: 1.5\n  attackRange: 1.5\n  minAttackRange: 0\n  lineOfSight: 16\n"
        "  cost:\n    Supplies: 80\n    Iron: 30\n    Crystal: 0\n    Veilsteel: 0\n    Glow: 0\n"
        "  minBuildingLevel: 0\n  buildSpeed: 0\n  gatheringSpeed: 0\n  carryCapacity: 0\n  healsPerSecond: 0\n")
    open(path,"w",encoding="utf-8",newline="\n").write(body)
    open(path+".meta","w",encoding="utf-8",newline="\n").write(asset_meta(guid("Unit_Spearman")))
    print("  created Unit_Spearman (id Spearman)")

# ---------------- melee ladder data (now on Garrison) ----------------
SPEAR, SWORD, SENT = "Spearman", "Swordsman", "Alanthor_Sentinel"
HP, DMG, DEF, MOVE = 0, 3, 8, 9
def buff(stat, amt, unit): return dict(kind=0, unit=unit, stat=stat, amount=amt, ability="")
def enable(ab, unit): return dict(kind=1, unit=unit, stat=0, amount=0, ability=ab)
def U(id, name, eff, req="", s=0, i=0, t=0, desc=""):
    return dict(id=id, displayName=name, description=desc, requires=req, effects=eff, cost=dict(Supplies=s, Iron=i), researchTime=t)
def tier(units, hp, mv, dmg, dfn):
    e=[]
    for u in units: e += [buff(HP,hp,u), buff(MOVE,mv,u), buff(DMG,dmg,u), buff(DEF,dfn,u)]
    return e
UPGRADES=[
    U("SeasonedInfantry","Seasoned Spearmen",tier([SPEAR],30,5,1,1),s=100,i=40,t=28,desc="Spearmen: +30 HP, +5% move speed, +1 attack, +1 defense."),
    U("ShieldWall","Shield Wall",[enable("ShieldWall",SPEAR)],s=120,i=50,t=30,desc="Enables the Shield Wall ability for Spearmen."),
    U("VeteranInfantry","Veteran Infantry",tier([SPEAR,SWORD],30,10,1,1),req="SeasonedInfantry",s=180,i=80,t=35,desc="Spearmen + Swordsmen: +30 HP, +10% move speed, +1 attack, +1 defense."),
    U("Charge","Charge",[enable("Charge",SPEAR),enable("Charge",SWORD)],s=160,i=70,t=33,desc="Enables the Charge ability for Spearmen and Swordsmen."),
    U("EliteInfantry","Elite Infantry",tier([SPEAR,SWORD],30,15,2,2),req="VeteranInfantry",s=280,i=120,t=45,desc="Spearmen + Swordsmen: +30 HP, +15% move speed, +2 attack, +2 defense."),
]
def atk(en,mt=1): return dict(enabled=en,damage=12,damageType="ranged",range=22,cooldown=1.5,maxTargets=mt)
LEVELS=[
    dict(level=1,variantName="Garrison",culture="Alanthor",trainSpeedBonusPct=0,trains=[SPEAR],
         availableUpgrades=["SeasonedInfantry","ShieldWall"],attack=atk(False)),
    dict(level=2,variantName="Garrison",culture="Alanthor",trainSpeedBonusPct=0,trains=[SPEAR,SWORD],
         availableUpgrades=["SeasonedInfantry","ShieldWall","VeteranInfantry","Charge"],attack=atk(False)),
    dict(level=3,variantName="Garrison",culture="Alanthor",trainSpeedBonusPct=0,trains=[SPEAR,SWORD,SENT],
         availableUpgrades=["SeasonedInfantry","ShieldWall","VeteranInfantry","Charge","EliteInfantry"],attack=atk(False)),
]
def attack_block(a,ind):
    p=" "*ind
    return (f"{p}attack:\n{p}  enabled: {num(a['enabled'])}\n{p}  damage: {num(a['damage'])}\n{p}  damageType: {ys(a['damageType'])}\n"
            f"{p}  range: {num(a['range'])}\n{p}  cooldown: {num(a['cooldown'])}\n{p}  maxTargets: {num(a['maxTargets'])}\n")
def cost_block(c,ind):
    p=" "*ind; c=c or {}
    return (f"{p}cost:\n{p}  Supplies: {num(c.get('Supplies',0))}\n{p}  Iron: {num(c.get('Iron',0))}\n"
            f"{p}  Crystal: {num(c.get('Crystal',0))}\n{p}  Veilsteel: {num(c.get('Veilsteel',0))}\n{p}  Glow: {num(c.get('Glow',0))}\n")
def levels_block(levels):
    out="  levels:\n"
    for L in levels:
        out += (f"  - level: {num(L['level'])}\n    variantName: {ys(L['variantName'])}\n    culture: {ys(L['culture'])}\n"
                f"    trainSpeedBonusPct: {num(L['trainSpeedBonusPct'])}\n"
                + seq("trains",L["trains"],4) + seq("availableUpgrades",L["availableUpgrades"],4) + attack_block(L["attack"],4))
    return out
def effects_block(effs,ind):
    p=" "*ind; out=f"{p}effects:\n"
    for e in effs:
        out += (f"{p}- kind: {num(e['kind'])}\n{p}  unit: {ys(e['unit'])}\n{p}  stat: {num(e['stat'])}\n"
                f"{p}  amount: {num(e['amount'])}\n{p}  ability: {ys(e['ability'])}\n")
    return out
def upgrades_block(ups):
    out="  unitUpgrades:\n"
    for u in ups:
        out += (f"  - id: {ys(u['id'])}\n    displayName: {ys(u['displayName'])}\n    description: {ys(u['description'])}\n"
                f"    requires: {ys(u['requires'])}\n" + effects_block(u["effects"],4) + cost_block(u["cost"],4)
                + f"    researchTime: {num(u['researchTime'])}\n")
    return out

def write_garrison():
    path = os.path.join(BLD_ALAN, "Building_Alanthor_Garrison.asset")
    body = (HDR + f"  m_Script: {{fileID: 11500000, guid: {BLD_GUID}, type: 3}}\n"
        "  m_Name: Building_Alanthor_Garrison\n  m_EditorClassIdentifier: \n"
        "  id: 'Alanthor_Garrison'\n  displayName: 'Garrison'\n  role: 'Alanthor melee training (cultured Barracks)'\n"
        "  description: 'Alanthor''s cultured Barracks. Trains Spearmen, then Swordsmen (lvl 2) and Sentinels "
        "(lvl 3); unlocks the Seasoned/Veteran/Elite infantry upgrades plus Shield Wall and Charge.'\n"
        "  hp: 1000\n  armorType: 'structure_human'\n"
        "  defense:\n    melee: 2\n    ranged: 2\n    siege: 0\n    magic: 0\n"
        "  radius: 1.6\n  lineOfSight: 18\n"
        + seq("trains",[SPEAR,SWORD,SENT],2) + seq("research",[],2) + "  minEra: 0\n"
        + cost_block(dict(Supplies=0),2) + attack_block(atk(False),2) + levels_block(LEVELS) + upgrades_block(UPGRADES)
        + "  prefabPath: ''\n" + seq("canUpgradeTo",[],2))
    open(path,"w",encoding="utf-8",newline="\n").write(body)
    open(path+".meta","w",encoding="utf-8",newline="\n").write(asset_meta(guid("Building_Alanthor_Garrison")))
    print("  created Building_Alanthor_Garrison (id Alanthor_Garrison): levels=%d upgrades=%d" % (len(LEVELS),len(UPGRADES)))

write_spearman()
write_garrison()
