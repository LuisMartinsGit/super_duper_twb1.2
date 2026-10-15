#!/usr/bin/env python
# author_runai.py — writes every Runai SO for the 2026-10-13 redesign
# (docs/Design/Age_1_Runai.md) and registers them in TechTreeCatalog.asset /
# AbilityCatalog.asset. Every number here is a PLACEHOLDER seed: once the
# assets exist, the SOs are the source of truth and this script is not re-run
# over hand-tuned values (it overwrites).
#
#   python tools/author_runai.py
#
# GUIDs are deterministic (md5 of the asset path) so a re-run rewrites the same
# assets; an asset that already has a .meta keeps its GUID.

import hashlib
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TT = os.path.join(ROOT, "Assets", "GameData", "TechTree")
RUNAI = os.path.join(TT, "Civs", "Runai")
CATALOG = os.path.join(ROOT, "Assets", "Resources", "TechTreeCatalog.asset")
ABILITY_CATALOG = os.path.join(ROOT, "Assets", "Resources", "AbilityCatalog.asset")

S_UNIT = "b6fc2bbfed21e12438424e2ff0b5dd06"
S_BLD = "b98b52f4032a91549814ef127e8e0bff"
S_LVL = "22ca33cd9934d11488e8d35e1d85f5b5"
S_TECH = "3d382254392e37abdaa41be769b9c36c"
S_ABIL = "b3ecc1043fa62454ebb9521a55d0673d"

# Borrowed art (placeholder): prefab refs + animator controllers of existing units.
P = {
    "spear":   ("{fileID: 8418979553366102446, guid: cfe87d0b4a8c3c24884e8aa21f1fdfe8, type: 3}",
                "{fileID: 9100000, guid: 91ad141dedc69214182b2819565ab633, type: 2}"),
    "skirm":   ("{fileID: 7278857431059564549, guid: 2be1e955419ad174ca83274b84159fa8, type: 3}",
                "{fileID: 9100000, guid: 8552705bb4647c5498d094f59f50b481, type: 2}"),
    "raider":  ("{fileID: 8624030962270631097, guid: 1dd16ba4a0ed580439b382d67473bac6, type: 3}",
                "{fileID: 9100000, guid: 8b33fa7113c5e2d42baad3f8a6c7e0af, type: 2}"),
    "fraider": ("{fileID: 3958872465679805044, guid: 0ac64b458f08ea041963b9e1136d4f25, type: 3}",
                "{fileID: 9100000, guid: 5105f202c362bb4488b894f908420206, type: 2}"),
    "catapult": ("{fileID: 1114465177375007938, guid: 1e430c61f00215a4ab5c515277145060, type: 3}",
                 "{fileID: 0}"),
    "caravan": ("{fileID: 8303799667744718446, guid: 43bdc3cdf69bfc94e88876dbb598e904, type: 3}",
                "{fileID: 0}"),
    "treb":    ("{fileID: 132866903991625, guid: 516d3df3946cc39f2f363f18aa224f48, type: 3}",
                "{fileID: 0}"),
}
PB = {
    "bazaar":  "{fileID: 919656388654685122, guid: abbc8ef6695675d4082328f0fe3acebb, type: 3}",
    "tradehub": "{fileID: 2014027055777286170, guid: 37407269ebea2f840b98e97ad3e7f4ce, type: 3}",
    "foundry": "{fileID: 7462119862421039891, guid: 4c8966e12021b084fb1a05e2b698dc02, type: 3}",
}

HDR = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
       "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
       "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
       "  m_Enabled: 1\n  m_EditorHideFlags: 0\n")

written = {"units": [], "buildings": [], "technologies": [], "buildingLevels": [], "abilities": []}


def ys(s):
    s = str(s or "")
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def num(v):
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        return str(int(v)) if v.is_integer() else repr(v)
    return str(v)


def guid_for(path):
    meta = path + ".meta"
    if os.path.exists(meta):
        m = re.search(r"guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read())
        if m:
            return m.group(1)
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    return hashlib.md5(("twb-runai::" + rel).encode()).hexdigest()


def write_asset(path, body, kind):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    g = guid_for(path)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    if not os.path.exists(path + ".meta"):
        with open(path + ".meta", "w", encoding="utf-8", newline="\n") as f:
            f.write("fileFormatVersion: 2\n" f"guid: {g}\n"
                    "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
                    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    written[kind].append(g)
    return g


def cost(s=0, i=0, v=0, vs=0, ind="  "):
    return (f"{ind}cost:\n{ind}  Supplies: {s}\n{ind}  Iron: {i}\n"
            f"{ind}  Veilstone: {v}\n{ind}  Veilsteel: {vs}\n")


def lst(name, items, ind="  "):
    if not items:
        return f"{ind}{name}: []\n"
    return f"{ind}{name}:\n" + "".join(f"{ind}- {x}\n" for x in items)


# ───────────────────────────── units ─────────────────────────────

def unit(folder, name, uid, display, cls, hp, speed, train, dmg, dtype, armor, dfn, cd, rng, los,
         c, pop, lvl, tags, bonus=(), art="spear", pid=0, minr=0, aim=0, radius=0.5, traj="",
         pspeed=0, aoe=0, abilities=()):
    prefab, ctrl = P[art]
    b = (HDR + f"  m_Script: {{fileID: 11500000, guid: {S_UNIT}, type: 3}}\n"
         f"  m_Name: {name}\n  m_EditorClassIdentifier: \n"
         f"  id: {uid}\n  displayName: {display}\n  unitClass: {cls}\n"
         f"  hp: {num(hp)}\n  speed: {num(speed)}\n  trainingTime: {num(train)}\n"
         f"  damage: {num(dmg)}\n  damageType: {dtype}\n  armorType: {armor}\n"
         f"  defense:\n    melee: {dfn[0]}\n    ranged: {dfn[1]}\n    siege: {dfn[2]}\n    magic: {dfn[3]}\n"
         f"  attackCooldown: {num(cd)}\n  attackRange: {num(rng)}\n  minAttackRange: {num(minr)}\n"
         f"  lineOfSight: {num(los)}\n  aimTime: {num(aim)}\n  radius: {num(radius)}\n"
         f"  trajectory: {traj}\n  projectileSpeed: {num(pspeed)}\n"
         + cost(*c) +
         f"  populationCost: {pop}\n  minBuildingLevel: {lvl}\n  buildSpeed: 0\n  gatheringSpeed: 0\n"
         f"  healsPerSecond: 0\n  healRange: 0\n  siegeRange: 0\n  siegeCooldown: 0\n  aoeRadius: {num(aoe)}\n"
         + lst("tags", tags)
         + ("  bonusVsTags: []\n" if not bonus else
            "  bonusVsTags:\n" + "".join(f"  - vsTag: {t}\n    amount: {a}\n" for t, a in bonus))
         + "  preferTargets: []\n"
         + lst("abilities", abilities)
         + f"  prefab: {prefab}\n  presentationId: {pid}\n  animatorController: {ctrl}\n")
    write_asset(os.path.join(folder, name + ".asset"), b, "units")


FORT = os.path.join(RUNAI, "Buildings", "Fortress")
RG = os.path.join(RUNAI, "Buildings", "RouteGuard")
GG = os.path.join(RUNAI, "Buildings", "GrazingGrounds")
WW = os.path.join(RUNAI, "Buildings", "SiegeWorkshop")
SANC = os.path.join(RUNAI, "Buildings", "Sanctuary")
AY = os.path.join(RUNAI, "Buildings", "Arrowyard")
ESC = os.path.join(RUNAI, "Units", "Escort")
CROSS_R = os.path.join(RUNAI, "Buildings", "ThessarasCrossing", "Research")

unit(os.path.join(FORT, "Units", "CaravanMaster"), "CaravanMaster", "Runai_CaravanMaster", "Caravan Master",
     "human_melee", 220, 6.5, 30, 8, "melee", "infantry_light", (1, 1, 0, 0), 1.5, 1.5, 22,
     (120, 60, 40, 0), 1, 2, ["Infantry", "Light"], art="spear", pid=440, abilities=["Scatter"])
unit(os.path.join(FORT, "Units", "Wayfinder"), "Wayfinder", "Runai_Wayfinder", "The Wayfinder",
     "human_cavalry", 600, 7.2, 45, 28, "melee", "cavalry", (3, 3, 0, 1), 1.4, 1.6, 24,
     (300, 200, 150, 0), 3, 3, ["Cavalry", "Heavy"], art="raider", pid=441, radius=0.7,
     abilities=["Wayfinding", "Mirage", "Caravan of Swords", "Exodus", "The Call"])

unit(os.path.join(AY, "Units", "Skirmisher"), "Skirmisher", "Runai_Skirmisher", "Skirmisher",
     "human_ranged", 95, 6.2, 18, 13, "ranged", "ranged", (0, 1, 0, 0), 1.3, 9, 20,
     (60, 40, 20, 0), 1, 1, ["Ranged", "Light"], bonus=[("Infantry", 8)], art="skirm", pid=331, aim=0.4)
unit(os.path.join(RG, "Units", "RoadWarden"), "RoadWarden", "Runai_RoadWarden", "Road Warden",
     "human_melee", 180, 5.4, 22, 13, "melee", "infantry_heavy", (3, 2, 0, 0), 1.6, 1.6, 18,
     (80, 70, 40, 0), 1, 2, ["Infantry", "Heavy"], bonus=[("Cavalry", 10)], art="spear", pid=442)
unit(os.path.join(RG, "Units", "Sandstrider"), "Sandstrider", "Runai_Sandstrider", "Sandstrider",
     "human_melee", 150, 6.8, 24, 18, "melee", "infantry_light", (2, 1, 0, 0), 1.2, 1.5, 20,
     (90, 70, 70, 10), 1, 3, ["Infantry", "Light"], art="skirm", pid=443)

unit(os.path.join(RG, "Units", "DuneGuard"), "DuneGuard", "Runai_DuneGuard", "Dune Guard",
     "human_melee", 140, 5.8, 18, 14, "melee", "infantry_heavy", (2, 1, 0, 0), 1.4, 1.5, 18,
     (70, 50, 20, 0), 1, 1, ["Infantry", "Light"], bonus=[("Siege", 10)], art="spear", pid=450)
unit(os.path.join(AY, "Units", "Slinger"), "Slinger", "Runai_Slinger", "Slinger",
     "human_ranged", 100, 6.4, 20, 16, "ranged", "ranged", (0, 1, 0, 0), 1.1, 14, 22,
     (70, 50, 40, 0), 1, 2, ["Ranged", "Light"], bonus=[("Cavalry", 8)], art="skirm", pid=451, aim=0.3)
unit(os.path.join(AY, "Units", "SunArcher"), "SunArcher", "Runai_SunArcher", "Sun Archer",
     "human_ranged", 110, 5.8, 26, 26, "ranged", "ranged", (0, 2, 0, 0), 1.6, 22, 26,
     (90, 70, 80, 20), 1, 3, ["Ranged", "Light"], art="skirm", pid=452, aim=0.6, traj="high", pspeed=34)
unit(os.path.join(WW, "Units", "SkyBarge"), "SkyBarge", "Runai_SkyBarge", "Sky Barge",
     "machinery_siege", 260, 4.2, 35, 40, "siege", "ranged", (0, 2, 0, 0), 4.5, 6, 26,
     (160, 120, 90, 0), 3, 1, ["Siege", "Flying"], bonus=[("Building", 35)], art="caravan", pid=453,
     aim=0.5, radius=1.4, traj="high", pspeed=12, aoe=2.5)
unit(ESC, "EscortGuard", "Runai_EscortGuard", "Escort Guard",
     "human_melee", 130, 6.4, 0, 11, "melee", "infantry_light", (1, 1, 0, 0), 1.4, 1.5, 18,
     (0, 0, 0, 0), 0, 0, ["Infantry", "Light"], art="spear", pid=454)
unit(ESC, "EscortArcher", "Runai_EscortArcher", "Escort Archer",
     "human_ranged", 90, 6.4, 0, 10, "ranged", "ranged", (0, 1, 0, 0), 1.4, 11, 20,
     (0, 0, 0, 0), 0, 0, ["Ranged", "Light"], art="skirm", pid=455, aim=0.4)
unit(ESC, "EscortRider", "Runai_EscortRider", "Escort Rider",
     "human_cavalry", 150, 7.2, 0, 14, "melee", "cavalry", (1, 0, 0, 0), 1.4, 1.5, 20,
     (0, 0, 0, 0), 0, 0, ["Cavalry", "Light"], art="raider", pid=456, radius=0.6)
unit(os.path.join(GG, "Units", "Raider"), "Raider", "Runai_Raider", "Raider",
     "human_cavalry", 150, 7.6, 22, 16, "melee", "cavalry", (1, 0, 0, 0), 1.4, 1.5, 22,
     (100, 60, 40, 0), 2, 1, ["Cavalry", "Light"], bonus=[("Ranged", 10)], art="raider", pid=332, radius=0.6)
unit(os.path.join(GG, "Units", "HorseArcher"), "HorseArcher", "Runai_HorseArcher", "Horse Archer",
     "human_cavalry", 140, 7.0, 26, 12, "ranged", "cavalry", (0, 1, 0, 0), 1.6, 10, 22,
     (110, 70, 60, 0), 2, 2, ["Cavalry", "Ranged", "Light"], bonus=[("Infantry", 6)], art="raider",
     pid=444, aim=0.3, radius=0.6, traj="flat", pspeed=30)
unit(os.path.join(GG, "Units", "CamelLancer"), "CamelLancer", "Runai_CamelLancer", "Camel Lancer",
     "human_cavalry", 260, 6.6, 30, 20, "melee", "cavalry", (3, 2, 0, 0), 1.6, 1.8, 20,
     (120, 90, 90, 10), 2, 3, ["Cavalry", "Heavy"], bonus=[("Cavalry", 12)], art="fraider", pid=445, radius=0.7)

unit(os.path.join(WW, "Units", "Catapult"), "Catapult", "Runai_Catapult", "Catapult",
     "machinery_siege", 200, 3.4, 30, 36, "siege", "ranged", (0, 1, 2, 0), 5, 20, 24,
     (150, 120, 80, 0), 3, 1, ["Siege", "Heavy"], bonus=[("Building", 30)], art="catapult", pid=337,
     minr=5.5, aim=1, radius=1.2, traj="high", pspeed=18, aoe=3)
unit(os.path.join(WW, "Units", "WarWagon"), "WarWagon", "Runai_WarWagon", "War Wagon",
     "machinery_siege", 700, 4.5, 40, 14, "ranged", "infantry_heavy", (4, 6, 1, 0), 1.5, 12, 22,
     (200, 160, 120, 0), 4, 2, ["Siege", "Heavy"], art="caravan", pid=446, aim=0.3, radius=1.3,
     traj="flat", pspeed=30, abilities=["Unload"])
unit(os.path.join(WW, "Units", "SiegeCaravan"), "SiegeCaravan", "Runai_SiegeCaravan", "Siege Caravan",
     "machinery_siege", 260, 3.0, 45, 80, "siege", "ranged", (0, 1, 2, 0), 7, 30, 26,
     (200, 200, 150, 30), 4, 3, ["Siege", "Heavy"], bonus=[("Building", 60)], art="treb", pid=447,
     minr=8, aim=1.5, radius=1.4, traj="high", pspeed=20, aoe=2)

unit(os.path.join(RUNAI, "Units", "Caravan"), "Caravan", "Runai_Caravan", "Caravan",
     "civilian_trade", 200, 5.6, 0, 0, "melee", "infantry_light", (0, 1, 0, 0), 1.4, 1.5, 12,
     (0, 0, 0, 0), 0, 0, ["Light"], art="caravan", pid=432, radius=0.6)
unit(os.path.join(RUNAI, "Units", "TraderWarrior"), "TraderWarrior", "Runai_TraderWarrior", "Trader-Warrior",
     "human_melee", 150, 6.4, 0, 12, "melee", "infantry_light", (1, 1, 0, 0), 1.4, 1.5, 18,
     (0, 0, 0, 0), 0, 0, ["Infantry", "Light"], art="spear", pid=448)
unit(os.path.join(FORT, "Units", "Wayfinder", "Abilities", "Mirage"), "MirageRider", "Runai_MirageRider",
     "Mirage Rider", "human_cavalry", 60, 7.6, 0, 0, "melee", "cavalry", (0, 0, 0, 0), 1.4, 1.5, 14,
     (0, 0, 0, 0), 0, 0, ["Cavalry", "Light"], art="raider", pid=449, radius=0.6)

# ─────────────────────────── buildings ───────────────────────────

def building(folder, name, bid, display, role, desc, hp, dfn, los, fp, build, c, trains, prefab, pid,
             min_era=2, max_per=0, radius=1.6):
    b = (HDR + f"  m_Script: {{fileID: 11500000, guid: {S_BLD}, type: 3}}\n"
         f"  m_Name: {name}\n  m_EditorClassIdentifier: \n"
         f"  id: {bid}\n  displayName: {display}\n  role: {ys(role)}\n  description: {ys(desc)}\n"
         f"  hp: {hp}\n  armorType: structure_human\n"
         f"  defense:\n    melee: {dfn[0]}\n    ranged: {dfn[1]}\n    siege: {dfn[2]}\n    magic: {dfn[3]}\n"
         f"  radius: {num(radius)}\n  lineOfSight: {los}\n  footprintCells: {{x: {fp}, y: {fp}}}\n"
         f"  buildTime: {build}\n  populationProvided: 0\n  suppliesPerTick: 0\n  suppliesInterval: 0\n"
         "  slotIncomePerMinute: []\n  interestPerMinute: 0\n  interestPrincipalCap: 0\n  interestMaxSeconds: 0\n"
         "  coffersRate: 0\n  merchantChartersRate: 0\n  sovereignBondsRate: 0\n"
         f"  maxPerFaction: {max_per}\n  minWallLevel: 0\n  garrisonSlots: 0\n  garrisonArrowsPerOccupant: 0\n"
         "  maxIron: 0\n  maxVeilstone: 0\n  segmentHp: 0\n  segmentLineOfSight: 0\n"
         + lst("trains", trains) + "  research: []\n"
         f"  minEra: {min_era}\n" + cost(*c) + "  tags:\n  - Building\n"
         "  attack:\n    enabled: 0\n    damage: 12\n    damageType: ranged\n    range: 22\n"
         "    cooldown: 1.5\n    maxTargets: 1\n    siegeShotDamage: 0\n"
         "  levels: []\n  unitUpgrades: []\n"
         f"  prefab: {prefab}\n  presentationId: {pid}\n  prefabPath: \n  canUpgradeTo: []\n")
    write_asset(os.path.join(folder, name + ".asset"), b, "buildings")


building(os.path.join(TT, "Age0", "Buildings", "ThessarasCrossing"), "ThessarasCrossing", "ThessarasCrossing",
         "Thessara's Crossing", "Runai landmark - ages the faction into Runai; the grand caravan market",
         "The Runai age-up landmark. Caravans that sell here are paid the Crossing premium; the trade research is studied here.",
         1600, (3, 6, 0, 2), 22, 4, 120, (600, 300, 200, 0), [], PB["bazaar"], 590, min_era=0, max_per=1, radius=1.8)
building(GG, "GrazingGrounds", "Runai_GrazingGrounds", "Grazing Grounds", "Runai mounted trainer",
         "Raider at L1, Horse Archer at L2, Camel Lancer at L3.",
         900, (1, 1, 0, 0), 16, 4, 35, (200, 80, 0, 0),
         ["Runai_Raider", "Runai_HorseArcher", "Runai_CamelLancer"], PB["tradehub"], 591)
building(AY, "Arrowyard", "Runai_Arrowyard", "Arrowyard", "Runai ranged trainer",
         "Skirmisher at L1, Slinger at L2, Sun Archer at L3.",
         800, (1, 1, 0, 0), 18, 4, 30, (180, 60, 0, 0),
         ["Runai_Skirmisher", "Runai_Slinger", "Runai_SunArcher"], PB["tradehub"], 593)
building(SANC, "Sanctuary", "Runai_Sanctuary", "Sanctuary",
         "Runai veilstone shrine - built atop an outcrop, Inactive or Cursed",
         "Never drains its outcrop. Veilstone on an Inactive outcrop, veilsteel on a Cursed one; a slow religion trickle; the curse sleeps under it.",
         800, (2, 2, 0, 2), 18, 2, 40, (150, 50, 0, 0), [], PB["foundry"], 592, radius=1.0)

# The Wagonworks keeps the old Siege Workshop's id, art and asset — rewrite its roster and name.
ws = os.path.join(WW, "SiegeWorkshop.asset")
txt = open(ws, encoding="utf-8").read()
txt = re.sub(r"(?m)^  displayName: .*$", "  displayName: Wagonworks", txt)
txt = re.sub(r"(?m)^  trains:\n(?:  - .*\n)*", "  trains:\n  - Runai_SkyBarge\n  - Runai_WarWagon\n  - Runai_SiegeCaravan\n", txt)
txt = re.sub(r"(?m)^  minEra: .*$", "  minEra: 2", txt)
open(ws, "w", encoding="utf-8", newline="\n").write(txt)

# ──────────────────────────── levels ─────────────────────────────

def levels(folder, stem, bid, display, upgrades, income=(0, 0, 0), pop=0, attack=None):
    hp = (1.1, 1.15, 1.2)
    tt = (0.87, 0.8, 0.714)
    ac = (0.91, 0.83, 0.77)
    for n in (1, 2, 3):
        s, i, v, t = upgrades[n - 1]
        b = (HDR + f"  m_Script: {{fileID: 11500000, guid: {S_LVL}, type: 3}}\n"
             f"  m_Name: {stem}_Lvl{n}\n  m_EditorClassIdentifier: \n"
             f"  buildingId: {bid}\n  culture: Runai\n  level: {n}\n  displayName: {display}\n"
             f"  upgradeCost:\n    Supplies: {s}\n    Iron: {i}\n    Veilstone: {v}\n    Veilsteel: 0\n"
             f"  upgradeSeconds: {t}\n  hpMultiplier: {hp[n-1]}\n  trainTimeMultiplier: {tt[n-1]}\n"
             f"  attackCooldownMultiplier: {ac[n-1]}\n  maxTargets: {n if bid == 'Fortress' else 0}\n"
             f"  populationProvided: {pop}\n  slotIncomePerMinute: {income[n-1]}\n"
             "  interestMultiplier: 0\n  tradeRateMultiplier: 0\n"
             + (("  attack:\n    enabled: 1\n    damage: %d\n    damageType: ranged\n    range: %s\n"
                 "    cooldown: %s\n    maxTargets: %d\n    siegeShotDamage: 0\n" % attack[n - 1]) if attack else
                ("  attack:\n    enabled: 0\n    damage: 12\n    damageType: ranged\n    range: 22\n"
                 "    cooldown: 1.5\n    maxTargets: 1\n    siegeShotDamage: 0\n"))
             + "  lineOfSight: 0\n  prefab: {fileID: 0}\n")
        write_asset(os.path.join(folder, f"{stem}_Lvl{n}.asset"), b, "buildingLevels")


STD = [(0, 0, 0, 20), (200, 60, 20, 30), (400, 120, 60, 45)]
levels(FORT, "Fortress", "Fortress", "Fortress", [(0, 0, 0, 20), (400, 200, 60, 45), (800, 400, 160, 60)])
levels(RG, "RouteGuard", "Barracks", "Route Guard", STD)
levels(os.path.join(RUNAI, "Buildings", "Waystation"), "Waystation", "Hut", "Waystation",
       [(0, 0, 0, 15), (120, 30, 0, 25), (240, 60, 0, 35)],
       attack=[(8, "18", "1.8", 1), (11, "20", "1.6", 1), (14, "22", "1.4", 2)])
levels(AY, "Arrowyard", "Runai_Arrowyard", "Arrowyard", STD)
levels(os.path.join(RUNAI, "Buildings", "TradingPost"), "TradingPost", "GatherersHut", "Trading Post",
       [(0, 0, 0, 20), (150, 40, 0, 30), (300, 100, 0, 45)], income=(130, 160, 220))
levels(os.path.join(RUNAI, "Buildings", "Mine"), "Mine", "Mine", "Mine",
       [(0, 0, 0, 20), (150, 60, 0, 30), (300, 120, 0, 45)], income=(140, 170, 270))
levels(GG, "GrazingGrounds", "Runai_GrazingGrounds", "Grazing Grounds", STD)
levels(WW, "Wagonworks", "Runai_SiegeWorkshop", "Wagonworks", STD)
levels(SANC, "Sanctuary", "Runai_Sanctuary", "Sanctuary",
       [(0, 0, 0, 20), (150, 80, 40, 35), (300, 160, 120, 50)], income=(60, 90, 140))

# ───────────────────────────── techs ─────────────────────────────

def fx(target, stat, op, value):
    return (target, stat, op, value)


def tech(folder, name, tid, display, desc, at, lvl, t, c, prereq=(), effects=(), role="Military"):
    body = (HDR + f"  m_Script: {{fileID: 11500000, guid: {S_TECH}, type: 3}}\n"
            f"  m_Name: {name}\n  m_EditorClassIdentifier: \n"
            f"  id: {tid}\n  displayName: {display}\n  role: {role}\n  effect: \n  desc: {ys(desc)}\n"
            f"  researchAt: {at}\n  minBuildingLevel: {lvl}\n  researchTime: {t}\n"
            + lst("prerequisites", list(prereq)) + "  culture: Runai\n" + cost(*c)
            + "  effects:\n    gatherSpeedMult: 0\n    meleeAttackSpeedMult: 0\n    meleeDefenseAdd: 0\n"
              "    meleeDamageAdd: 0\n    rangedDamageAdd: 0\n    archerRangeMult: 0\n")
    if effects:
        body += "  effectsList:\n" + "".join(
            f"  - Target: {ys(tg)}\n    Stat: {st}\n    Op: {op}\n    Value: {num(v)}\n" for tg, st, op, v in effects)
    else:
        body += "  effectsList: []\n"
    write_asset(os.path.join(folder, name + ".asset"), body, "technologies")


def per(units, stat, op, v):
    return [fx("unit:" + u, stat, op, v) for u in units]


RG_U = ["Spearman", "Runai_DuneGuard", "Runai_RoadWarden", "Runai_Sandstrider"]
AY_U = ["Runai_Skirmisher", "Runai_Slinger", "Runai_SunArcher"]
ESC_U = ["Runai_EscortGuard", "Runai_EscortArcher", "Runai_EscortRider"]
GG_U = ["Runai_Raider", "Runai_HorseArcher", "Runai_CamelLancer"]
WW_U = ["Runai_SkyBarge", "Runai_WarWagon", "Runai_SiegeCaravan"]
FR = os.path.join(FORT, "Research")

# Fortress
tech(FR, "IronTools", "Runai_IronTools", "Iron Tools", "Tool tier 2 - Runai. Workers build 30 percent faster.",
     "Fortress", 2, 40, (150, 80, 0, 0), effects=[fx("faction", "BuildSpeed", "Pct", 30)], role="Economy")
tech(FR, "VeilstoneTools", "Runai_VeilstoneTools", "Veilstone Tools", "Tool tier 3 - Runai. Workers build faster.",
     "Fortress", 3, 50, (200, 120, 80, 0), ["Runai_IronTools"], [fx("faction", "BuildSpeed", "Pct", 50)], role="Economy")
tech(FR, "VeilsteelTools", "Runai_VeilsteelTools", "Veilsteel Tools", "Tool tier 4 - Runai. Workers build faster still.",
     "Fortress", 3, 60, (250, 150, 100, 30), ["Runai_VeilstoneTools"], [fx("faction", "BuildSpeed", "Pct", 75)], role="Economy")
tech(FR, "PackAnimals", "Runai_PackAnimals", "Pack Animals", "Caravans carry more cargo.",
     "Fortress", 1, 40, (180, 60, 0, 0), role="Economy")
tech(FR, "DuneRiders", "Runai_DuneRiders", "Dune Riders", "Scouts ride: faster, and they see further.",
     "Fortress", 0, 30, (100, 40, 0, 0),
     effects=[fx("unit:Scout", "Speed", "Pct", 20), fx("unit:Scout", "LineOfSight", "Pct", 20)])

# Thessara's Crossing
CX = "ThessarasCrossing"
tech(CROSS_R, "LongHaulTariffs", "Runai_LongHaulTariffs", "Long-Haul Tariffs",
     "A higher distance premium, and a higher cap on it.", CX, 0, 45, (220, 40, 0, 0), role="Economy")
tech(CROSS_R, "SwiftWheels", "Runai_SwiftWheels", "Swift Wheels I", "Speed path: caravans move faster.",
     CX, 0, 35, (160, 60, 0, 0), role="Economy")
tech(CROSS_R, "SwiftWheels2", "Runai_SwiftWheels2", "Swift Wheels II", "Speed path: caravans move faster still.",
     CX, 0, 45, (260, 100, 40, 0), ["Runai_SwiftWheels"], role="Economy")
tech(CROSS_R, "WagonArchers", "Runai_WagonArchers", "Wagon Archers",
     "Retaliating fire: caravans shoot back at what attacks them.", CX, 0, 40, (200, 80, 40, 0), role="Economy")
for n in (1, 2, 3):
    tech(CROSS_R, f"Escort{n}", f"Runai_Escort{n}", f"Escort {'I' * n}",
         ["", "Every caravan travels with 1 melee escort.",
          "Escort squad: 1 melee and 2 ranged.",
          "Escort squad: 3 melee, 3 ranged and 2 riders."][n],
         CX, 0, 30 + 15 * n, (120 * n, 60 * n, 30 * (n - 1), 0),
         [f"Runai_Escort{n - 1}"] if n > 1 else [], role="Economy")
tech(CROSS_R, "HunkerDown", "Runai_HunkerDown", "Hunker Down",
     "A caravan that falls to half health stops and gains 80% armour for 15 seconds.",
     CX, 0, 40, (180, 80, 40, 0), role="Economy")
tech(CROSS_R, "StrongAxles", "Runai_StrongAxles", "Strong Axles I", "Caravan durability: caravans are tougher.",
     CX, 0, 35, (140, 80, 0, 0), effects=per(["Runai_Caravan"], "Hp", "Pct", 40), role="Economy")
tech(CROSS_R, "StrongAxles2", "Runai_StrongAxles2", "Strong Axles II", "Caravan durability: caravans are tougher still.",
     CX, 0, 45, (220, 120, 40, 0), ["Runai_StrongAxles"], per(["Runai_Caravan"], "DefenseAll", "Add", 2), role="Economy")
prev = ()
for n, word in ((2, "Second"), (3, "Third"), (4, "Fourth"), (5, "Fifth")):
    tid = f"Runai_Caravan{n}"
    tech(CROSS_R, f"{word}Caravan", tid, f"{word} Caravan",
         "Every depot keeps one more caravan, and one more trader-warrior to guard it.",
         CX, 0, 30 + 10 * n, (100 * n, 30 * n, 20 * (n - 1), 0), prev, role="Economy")
    prev = (tid,)
tech(CROSS_R, "HardenedEscorts", "Runai_HardenedEscorts", "Hardened Escorts I",
     "Escort durability: escorts are tougher and hit harder.", CX, 0, 40, (180, 100, 40, 0),
     effects=per(ESC_U, "Hp", "Pct", 30) + per(ESC_U, "Damage", "Pct", 20))
tech(CROSS_R, "HardenedEscorts2", "Runai_HardenedEscorts2", "Hardened Escorts II",
     "Escort durability: escorts are tougher still.", CX, 0, 50, (260, 140, 80, 0), ["Runai_HardenedEscorts"],
     per(ESC_U, "Hp", "Pct", 30) + per(ESC_U, "DefenseAll", "Add", 1))
tech(CROSS_R, "MarketRights", "Runai_MarketRights", "Market Rights",
     "Waystations become markets: a shorter, safer, poorer sale.", CX, 0, 45, (250, 80, 40, 0), role="Economy")

# Sanctuary — the curse ladder
SR = os.path.join(SANC, "Research")
SA = "Runai_Sanctuary"
tech(SR, "VeiledPilgrims", "Runai_VeiledPilgrims", "Veiled Pilgrims",
     "Curse units never auto-target your caravans, trader-warriors or workers.", SA, 1, 40, (120, 0, 60, 0), role="Religion")
tech(SR, "ConsecratedGround", "Runai_ConsecratedGround", "Consecrated Ground",
     "Sanctuaries may be built on cursed outcrops, even in ground the curse holds; the node under them sleeps.",
     SA, 1, 50, (150, 60, 80, 0), role="Religion")
tech(SR, "HushedVeil", "Runai_HushedVeil", "Hushed Veil",
     "Curse units notice your units only up close, and no curse wave is sent against you.",
     SA, 2, 60, (200, 60, 120, 0), ["Runai_VeiledPilgrims"], role="Religion")
tech(SR, "Communion", "Runai_Communion", "Communion",
     "Your units regenerate in ground the curse holds.", SA, 2, 60, (200, 80, 150, 0), ["Runai_HushedVeil"], role="Religion")
tech(SR, "Veilbound", "Runai_Veilbound", "Veilbound",
     "Curse units near your Sanctuaries join you, one at a time.", SA, 3, 90, (300, 150, 200, 40), ["Runai_Communion"], role="Religion")
tech(SR, "VoiceOfTheVeil", "Runai_VoiceOfTheVeil", "Voice of the Veil",
     "The curse is at peace with you, and Veilbound reaches every curse unit far around your Sanctuaries.",
     SA, 3, 120, (500, 250, 300, 80), ["Runai_Veilbound"], role="Religion")

# Route Guard (researchAt Barracks — the Route Guard IS the Barracks)
RR = os.path.join(RG, "Research")
for n, (tier, lvl, pct, c, pre) in enumerate([
        ("Iron", 1, 10, (0, 120, 60, 0), "StoneWeapons"),
        ("Veilstone", 2, 15, (0, 150, 150, 0), "Runai_IronWeapons"),
        ("Veilsteel", 3, 20, (0, 180, 200, 40), "Runai_VeilstoneWeapons")]):
    tech(RR, f"{tier}Weapons", f"Runai_{tier}Weapons", f"{tier} Weapons",
         f"Route Guard weapon tier {n + 2}: +{pct}% damage.", "Barracks", lvl, 40 + 10 * n, c, [pre],
         per(RG_U, "Damage", "Pct", pct))
for n in (1, 2, 3):
    tech(RR, f"StrideInfantry{n}", f"Runai_StrideInfantry{n}", f"Stride {'I' * n}",
         "Route Guard units move faster.", "Barracks", n, 30 + 10 * n, (80 * n, 40 * n, 30 * (n - 1), 0),
         [f"Runai_StrideInfantry{n-1}"] if n > 1 else [], per(RG_U, "Speed", "Pct", 4))
for n, (nm, lvl) in enumerate([("Seasoned", 1), ("Veteran", 2), ("Elite", 3)]):
    tech(RR, f"{nm}Infantry", f"Runai_{nm}Infantry", f"{nm} Infantry", "Route Guard units gain health.",
         "Barracks", lvl, 35 + 10 * n, (100 + 60 * n, 50 + 40 * n, 30 * n, 0),
         [f"Runai_{['Seasoned', 'Veteran'][n-1]}Infantry"] if n else [], per(RG_U, "Hp", "Pct", 10 + 5 * n))
tech(RR, "RoadMarch", "Runai_RoadMarch", "Road March",
     "Every Runai unit moves faster near your Waystations, not only the caravans.", "Barracks", 1, 35, (150, 60, 0, 0))
tech(RR, "EscortDuty", "Runai_EscortDuty", "Escort Duty",
     "Road Wardens near a caravan take less damage.", "Barracks", 2, 40, (150, 80, 40, 0))
tech(RR, "ScalingHooks", "Runai_ScalingHooks", "Scaling Hooks",
     "Runai infantry climb onto an enemy wall's rampart anywhere along it.", "Barracks", 2, 50, (200, 120, 60, 0))
tech(RR, "DesertCloaks", "Runai_DesertCloaks", "Desert Cloaks",
     "Route Guard units gain armour.", "Barracks", 3, 45, (200, 100, 80, 0), effects=per(RG_U, "DefenseAll", "Add", 1))

# Arrowyard
AR = os.path.join(AY, "Research")
AA = "Runai_Arrowyard"
for n, (tier, lvl, pct, c) in enumerate([("Iron", 1, 12, (0, 120, 60, 0)), ("Veilstone", 2, 18, (0, 150, 150, 0)),
                                          ("Veilsteel", 3, 24, (0, 180, 200, 40))]):
    tech(AR, f"{tier}Arrows", f"Runai_{tier}Arrows", f"{tier}-Tipped Javelins",
         f"Ranged weapon tier {n + 1}: +{pct}% damage.", AA, lvl, 40 + 10 * n, c,
         [f"Runai_{['Iron', 'Veilstone'][n-1]}Arrows"] if n else [], per(AY_U, "Damage", "Pct", pct))
for n in (1, 2, 3):
    tech(AR, f"StrideRanged{n}", f"Runai_StrideRanged{n}", f"Archer Stride {'I' * n}",
         "Arrowyard units move faster.", AA, n, 30 + 10 * n, (80 * n, 40 * n, 30 * (n - 1), 0),
         [f"Runai_StrideRanged{n-1}"] if n > 1 else [], per(AY_U, "Speed", "Pct", 4))
for n, (nm, lvl) in enumerate([("Seasoned", 1), ("Veteran", 2), ("Elite", 3)]):
    tech(AR, f"{nm}Archers", f"Runai_{nm}Archers", f"{nm} Archers", "Arrowyard units gain health.",
         AA, lvl, 35 + 10 * n, (100 + 60 * n, 50 + 40 * n, 30 * n, 0),
         [f"Runai_{['Seasoned', 'Veteran'][n-1]}Archers"] if n else [], per(AY_U, "Hp", "Pct", 10 + 5 * n))
tech(AR, "Fletching", "Runai_Fletching", "Fletching", "Arrowyard units reach further.",
     AA, 1, 35, (150, 60, 20, 0), effects=per(AY_U, "AttackRange", "Pct", 12))

# Grazing Grounds
GR = os.path.join(GG, "Research")
GA = "Runai_GrazingGrounds"
for n, (tier, lvl, pct, c) in enumerate([("Iron", 1, 10, (0, 140, 60, 0)), ("Veilstone", 2, 15, (0, 160, 160, 0)),
                                          ("Veilsteel", 3, 20, (0, 200, 200, 40))]):
    tech(GR, f"{tier}Lances", f"Runai_{tier}Lances", f"{tier} Lances",
         f"Mounted weapon tier {n + 1}: +{pct}% damage.", GA, lvl, 40 + 10 * n, c,
         [f"Runai_{['Iron', 'Veilstone'][n-1]}Lances"] if n else [], per(GG_U, "Damage", "Pct", pct))
for n in (1, 2, 3):
    tech(GR, f"StrideCavalry{n}", f"Runai_StrideCavalry{n}", f"Riding Stride {'I' * n}",
         "Mounted units move faster.", GA, n, 30 + 10 * n, (90 * n, 50 * n, 30 * (n - 1), 0),
         [f"Runai_StrideCavalry{n-1}"] if n > 1 else [], per(GG_U, "Speed", "Pct", 4))
for n, (nm, lvl) in enumerate([("Seasoned", 1), ("Veteran", 2), ("Elite", 3)]):
    tech(GR, f"{nm}Riders", f"Runai_{nm}Riders", f"{nm} Riders", "Mounted units gain health.",
         GA, lvl, 35 + 10 * n, (110 + 60 * n, 60 + 40 * n, 30 * n, 0),
         [f"Runai_{['Seasoned', 'Veteran'][n-1]}Riders"] if n else [], per(GG_U, "Hp", "Pct", 10 + 5 * n))
tech(GR, "Remounts", "Runai_Remounts", "Remounts", "Cavalry heal while out of combat.", GA, 1, 35, (160, 60, 20, 0))
tech(GR, "ParthianShot", "Runai_ParthianShot", "Parthian Shot", "Horse Archers fire on the move.",
     GA, 2, 45, (200, 100, 80, 0))
tech(GR, "FeignedRetreat", "Runai_FeignedRetreat", "Feigned Retreat",
     "Wounded cavalry ride faster.", GA, 3, 45, (220, 120, 100, 0))

# Wagonworks
WR = os.path.join(WW, "Research")
WA = "Runai_SiegeWorkshop"
for n, (tier, lvl, pct, c) in enumerate([("Iron", 1, 10, (0, 150, 60, 0)), ("Veilstone", 2, 15, (0, 180, 160, 0)),
                                          ("Veilsteel", 3, 20, (0, 220, 220, 40))]):
    tech(WR, f"{tier}Shot", f"Runai_{tier}Shot", f"{tier} Shot", f"Munition tier {n + 1}: +{pct}% damage.",
         WA, lvl, 40 + 10 * n, c, [f"Runai_{['Iron', 'Veilstone'][n-1]}Shot"] if n else [],
         per(WW_U, "Damage", "Pct", pct))
    tech(WR, f"{tier}Plating", f"Runai_{tier}Plating", f"{tier} Plating", f"Siege armour tier {n + 1}.",
         WA, lvl, 40 + 10 * n, c, [f"Runai_{['Iron', 'Veilstone'][n-1]}Plating"] if n else [],
         per(WW_U, "DefenseAll", "Add", 1))
for n, (nm, lvl) in enumerate([("Seasoned", 1), ("Veteran", 2), ("Elite", 3)]):
    tech(WR, f"{nm}Crews", f"Runai_{nm}Crews", f"{nm} Crews", "Siege engines gain health.",
         WA, lvl, 35 + 10 * n, (120 + 60 * n, 70 + 40 * n, 30 * n, 0),
         [f"Runai_{['Seasoned', 'Veteran'][n-1]}Crews"] if n else [], per(WW_U, "Hp", "Pct", 10 + 5 * n))
tech(WR, "WagonLaager", "Runai_WagonLaager", "Wagon Laager",
     "A War Wagon standing still gains armour.", WA, 2, 40, (180, 100, 40, 0))

# ─────────────────────────── abilities ───────────────────────────
# Effect kinds: AbilityEffectKind in GameData/TechTree/Shared/Abilities/AbilityCard.cs (26-30 Runai).

def ability(folder, name, display, kind, value, duration, cooldown, radius, unlocks=1, cast=0, affects=2):
    b = (HDR + f"  m_Script: {{fileID: 11500000, guid: {S_ABIL}, type: 3}}\n"
         f"  m_Name: {name}\n  m_EditorClassIdentifier: \n"
         f"  abilityName: {ys(display)}\n  activation: 0\n  targeting: 0\n  affects: {affects}\n"
         f"  castTime: {num(cast)}\n  duration: {num(duration)}\n  cooldown: {num(cooldown)}\n"
         f"  radius: {num(radius)}\n  range: 0\n"
         f"  effects:\n  - kind: {kind}\n    value: {num(value)}\n"
         f"  aftermath: []\n  unlocksAtLevel: {unlocks}\n  damage: 0\n  damageType: 0\n  aimedAtPoint: 0\n"
         "  icon: {fileID: 0}\n  vfxPrefab: {fileID: 0}\n")
    write_asset(os.path.join(folder, name + ".asset"), b, "abilities")


WA_AB = os.path.join(FORT, "Units", "Wayfinder", "Abilities")
ability(os.path.join(WA_AB, "Wayfinding"), "Wayfinding", "Wayfinding", 26, 30, 10, 60, 14, 1)
ability(os.path.join(WA_AB, "Mirage"), "Mirage", "Mirage", 27, 4, 15, 90, 6, 3)
ability(os.path.join(WA_AB, "CaravanOfSwords"), "CaravanOfSwords", "Caravan of Swords", 28, 50, 30, 150, 0, 5)
ability(os.path.join(WA_AB, "Exodus"), "Exodus", "Exodus", 29, 0, 0, 300, 16, 10, cast=4)
ability(os.path.join(ESC, "Abilities", "TheCall"), "TheCall", "The Call", 31, 0, 60, 240, 0, 1)
ability(os.path.join(ESC, "Abilities", "ReturnToWagons"), "ReturnToWagons", "Return to Wagons", 32, 0, 0, 5, 0, 1)
ability(os.path.join(FORT, "Units", "CaravanMaster", "Abilities", "Scatter"), "Scatter", "Scatter", 30, 0, 0, 45, 30, 1)

# ─────────────────────────── retirements ─────────────────────────
retired = []
for p in [os.path.join(RUNAI, "Buildings", "ThessarasBazaar", "Research", "EscortedCaravans.asset"),
          os.path.join(RUNAI, "Buildings", "ThessarasBazaar", "Research", "PackBazaar.asset")]:
    if os.path.exists(p):
        retired.append(guid_for(p))
        os.remove(p)
        os.remove(p + ".meta")

# ──────────────────────────── catalogs ───────────────────────────

def register(path, sections):
    text = open(path, encoding="utf-8").read()
    for g in retired:
        text = re.sub(r"(?m)^  - \{fileID: 11400000, guid: " + g + r", type: 2\}\n", "", text)
    for sec, guids in sections.items():
        # Self-repairing: a GUID already listed anywhere is moved into its section.
        for g in guids:
            text = re.sub(r"(?m)^  - \{fileID: 11400000, guid: " + g + r", type: 2\}\n", "", text)
        m = re.search(r"(?m)^  " + sec + r":\n((?:  - .*\n)*)", text)
        if not m:
            raise SystemExit(f"section {sec} not found in {path}")
        existing = m.group(1)
        add = "".join(f"  - {{fileID: 11400000, guid: {g}, type: 2}}\n" for g in guids if g not in existing)
        text = text[:m.end(1)] + add + text[m.end(1):]
    open(path, "w", encoding="utf-8", newline="\n").write(text)


register(CATALOG, {k: written[k] for k in ("units", "buildings", "technologies", "buildingLevels")})
register(ABILITY_CATALOG, {"abilities": written["abilities"]})

# Rosters: the Age 0 trainers list the Runai units (culture-gated by id prefix at runtime).
def add_trains(path, ids, drop=()):
    t = open(path, encoding="utf-8").read()
    for i in ids:
        t = re.sub(r"(?m)^  - " + re.escape(i) + r"\n", "", t)
    m = re.search(r"(?m)^  trains:\n((?:  - .*\n)*)", t)
    block = [l for l in m.group(1).splitlines() if l.strip()[2:] not in drop]
    for i in ids:
        if f"  - {i}" not in block:
            block.append(f"  - {i}")
    t = t[:m.start(1)] + "\n".join(block) + "\n" + t[m.end(1):]
    open(path, "w", encoding="utf-8", newline="\n").write(t)


add_trains(os.path.join(TT, "Age0", "Buildings", "Barracks", "Barracks.asset"),
           ["Runai_DuneGuard", "Runai_RoadWarden", "Runai_Sandstrider"], drop=("Runai_Skirmisher",))
add_trains(os.path.join(TT, "Age0", "Buildings", "Fortress", "Fortress.asset"),
           ["Runai_CaravanMaster", "Runai_Wayfinder"])

for k, v in written.items():
    print(f"{k}: {len(v)}")
print("retired:", len(retired))
