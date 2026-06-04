#!/usr/bin/env python3
"""
Split Assets/Scripts/Components/{BuildingComponents,UnitComponents}.cs into a
three-tier hierarchy mirroring the GameData scheme (Age 0 + Alanthor per-entity):

  Components/Buildings/BuildingComponents.cs            -> all-building generic (+ parked)
  Components/Buildings/Age 0/Age0BuildingComponents.cs  -> all-Age-0-building
  Components/Buildings/Age 0/Hall/HallComponents.cs     -> per-entity
  ... and the same for Units.

Every component type is in the GLOBAL namespace and stays in the SAME assembly, so
moving types between files is compile-neutral. The script verifies that the set of
type names emitted == the set parsed (nothing lost or duplicated).
"""
import os, re

ROOT = r"c:/Users/overw/Documents/The Waning Border 1.2"
CDIR = os.path.join(ROOT, "Assets/Scripts/Components")
SRC = {
    "Building": os.path.join(CDIR, "BuildingComponents.cs"),
    "Unit": os.path.join(CDIR, "UnitComponents.cs"),
}

# ---- classification: typename -> (relative_path_under_Components, section) ----
# section "main" = normal; "parked" = lumped not-yet-structured culture/sect/era-2 content.
B = "Buildings/BuildingComponents.cs"
U = "Units/UnitComponents.cs"
def b(p):  return f"Buildings/{p}"
def u(p):  return f"Units/{p}"

CLASS = {
    # ---------------- BUILDINGS ----------------
    # generic (all buildings)
    "BuildingTag": (B, "main"), "BuildingCollapseState": (B, "main"),
    "Buildable": (B, "main"), "UnderConstruction": (B, "main"),
    "AutoConstructTag": (B, "main"), "BuildingDamageState": (B, "main"),
    "DeferredDefense": (B, "main"), "Defense": (B, "main"),
    "TrainingState": (B, "main"), "TrainQueueItem": (B, "main"),
    "BuildingRangedAttack": (B, "main"), "BuildingSize": (B, "main"),
    "ObstacleTag": (B, "main"),
    # parked (Runai / Feraldis / Sect / Era-2 shared / unbuilt)
    "WorkshopTag": (B, "parked"), "DepotTag": (B, "parked"),
    "TempleOfRidanTag": (B, "parked"), "TempleTag": (B, "parked"),
    "TempleLevel": (B, "parked"), "TempleUpgradeState": (B, "parked"),
    "OutpostTag": (B, "parked"), "TradeHubTag": (B, "parked"),
    "BazaarTag": (B, "parked"), "SiegeWorkshopTag": (B, "parked"),
    "HuntingLodgeTag": (B, "parked"), "LoggingStationTag": (B, "parked"),
    "WarbrandFoundryTag": (B, "parked"), "LonghouseTag": (B, "parked"),
    "BatchTrainingTag": (B, "parked"), "TotemTowerTag": (B, "parked"),
    "FerSiegeYardTag": (B, "parked"), "ChapelSmallTag": (B, "parked"),
    "ChapelLargeTag": (B, "parked"), "ChapelTag": (B, "parked"),
    "SectUniqueBuildingTag": (B, "parked"), "TempleChapelSlot": (B, "parked"),
    "TempleOwner": (B, "parked"),
    # Age 0 tier (shared across several Age-0 buildings)
    "ChoiceBuildingTag": (b("Age 0/Age0BuildingComponents.cs"), "main"),
    # Age 0 per-entity
    "HallTag": (b("Age 0/Hall/HallComponents.cs"), "main"),
    "AgeUpState": (b("Age 0/Hall/HallComponents.cs"), "main"),
    "GathererHutTag": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "FarmBuildOrder": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "SelfDestructTimer": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "GathererHutAgeUpChoice": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "GathererHutConverting": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "HutConversionTarget": (b("Age 0/GatherersHut/GatherersHutComponents.cs"), "main"),
    "HutTag": (b("Age 0/Hut/HutComponents.cs"), "main"),
    "BarracksTag": (b("Age 0/Barracks/BarracksComponents.cs"), "main"),
    "ArcheryRangeTag": (b("Age 0/ArcheryRange/ArcheryRangeComponents.cs"), "main"),
    "ShrineTag": (b("Age 0/ShrineOfRidan/ShrineOfRidanComponents.cs"), "main"),
    "ShrineRPGranted": (b("Age 0/ShrineOfRidan/ShrineOfRidanComponents.cs"), "main"),
    "VaultTag": (b("Age 0/VaultOfAlmierra/VaultOfAlmierraComponents.cs"), "main"),
    "VaultStorage": (b("Age 0/VaultOfAlmierra/VaultOfAlmierraComponents.cs"), "main"),
    "FiendstoneKeepTag": (b("Age 0/FiendstoneKeep/FiendstoneKeepComponents.cs"), "main"),
    # Alanthor per-entity
    "CrucibleTag": (b("Alanthor/Crucible/CrucibleComponents.cs"), "main"),
    "SmelterTag": (b("Alanthor/Crucible/CrucibleComponents.cs"), "main"),
    "ForgeStorage": (b("Alanthor/Crucible/CrucibleComponents.cs"), "main"),
    "RoyalStableTag": (b("Alanthor/RoyalStable/RoyalStableComponents.cs"), "main"),
    "WatchTowerTag": (b("Alanthor/Tower/TowerComponents.cs"), "main"),
    "PracticeRangeTag": (b("Alanthor/PracticeRange/PracticeRangeComponents.cs"), "main"),
    "SiegeYardTag": (b("Alanthor/SiegeYard/SiegeYardComponents.cs"), "main"),
    # Alanthor walls (Wall / WallGate / WallTower entities)
    "WallTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallHubTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallSegmentTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallConnection": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallHubLink": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallEnclosureIncomeTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallEnclosureVertex": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallInstanceTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallInstanceParent": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallInstanceRef": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallUpgradeState": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallInstancePreviewTag": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallSegmentFocus": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallSegmentUpgradeState": (b("Alanthor/Wall/WallComponents.cs"), "main"),
    "WallTowerTag": (b("Alanthor/WallTower/WallTowerComponents.cs"), "main"),
    "WallGateTag": (b("Alanthor/WallGate/WallGateComponents.cs"), "main"),
    "WallGateState": (b("Alanthor/WallGate/WallGateComponents.cs"), "main"),
    "WallGateRegionTag": (b("Alanthor/WallGate/WallGateComponents.cs"), "main"),
    "WallGateGroup": (b("Alanthor/WallGate/WallGateComponents.cs"), "main"),
    # cross-cutting building/unit lifecycle that lived in BuildingComponents
    "DeathAnimationState": (U, "main"),     # applies to units
    "BuildOrder": (u("Age 0/Builder/BuilderComponents.cs"), "main"),
    "RepairOrder": (u("Age 0/Builder/BuilderComponents.cs"), "main"),
    "SectUniqueUnitTag": (U, "parked"),     # unit tag, sect-parked

    # ---------------- UNITS ----------------
    # generic (all units)
    "UnitClass": (U, "main"), "UnitTag": (U, "main"),
    "UnitRank": (U, "main"), "UnitRankApplied": (U, "main"),
    "GlowAbilityState": (U, "main"), "UpgradePile": (U, "main"),
    "CavalryTag": (U, "main"), "SiegeTag": (U, "main"),
    "UnhealableTag": (U, "main"), "ArmyTag": (U, "main"),
    # parked
    "BerserkerTag": (U, "parked"),
    # Age 0 per-entity
    "ArcherTag": (u("Age 0/Archer/ArcherComponents.cs"), "main"),
    "ArcherState": (u("Age 0/Archer/ArcherComponents.cs"), "main"),
    "ArrowProjectile": (u("Age 0/Archer/ArcherComponents.cs"), "main"),
    "CanBuild": (u("Age 0/Builder/BuilderComponents.cs"), "main"),
    "MinerTag": (u("Age 0/Miner/MinerComponents.cs"), "main"),
    "MinerWorkState": (u("Age 0/Miner/MinerComponents.cs"), "main"),
    "MinerState": (u("Age 0/Miner/MinerComponents.cs"), "main"),
    "ForgeSupplyOrder": (u("Age 0/Miner/MinerComponents.cs"), "main"),
    "CanHeal": (u("Age 0/Litharch/LitharchComponents.cs"), "main"),
    "LitharchTag": (u("Age 0/Litharch/LitharchComponents.cs"), "main"),
    "LitharchState": (u("Age 0/Litharch/LitharchComponents.cs"), "main"),
    "SpearmanTag": (u("Age 0/Spearman/SpearmanComponents.cs"), "main"),
    # Alanthor per-entity
    "CrossbowmanTag": (u("Alanthor/Crossbowman/CrossbowmanComponents.cs"), "main"),
    "LongbowmanTag": (u("Alanthor/Longbowman/LongbowmanComponents.cs"), "main"),
}

# scaffold tier files even if empty, so the structure is visible/ready
SCAFFOLD = [
    ("Buildings/Alanthor/AlanthorBuildingComponents.cs",
     "Components shared by ALL Alanthor buildings. (none yet — add here.)"),
    ("Units/Age 0/Age0UnitComponents.cs",
     "Components shared by ALL Age 0 units. (none yet — add here.)"),
    ("Units/Alanthor/AlanthorUnitComponents.cs",
     "Components shared by ALL Alanthor units. (none yet — add here.)"),
]

DROP = (
    re.compile(r"^\s*//\s*={3,}"),                       # ==== decorative ====
    re.compile(r"^\s*//\s*\w+Components\.cs\b"),         # file banner
    re.compile(r"^\s*//\s*Components specific"),
    re.compile(r"^\s*//\s*Place in:"),
    re.compile(r"^\s*//\s*Location:"),
)
TYPE_RE = re.compile(r"^\s*public\s+(struct|enum)\s+(\w+)")


def parse(path):
    lines = open(path, encoding="utf-8").read().splitlines()
    blocks = []      # (typename, [body_lines])
    pre = []
    i = 0
    while i < len(lines):
        m = TYPE_RE.match(lines[i])
        if not m:
            pre.append(lines[i]); i += 1; continue
        name = m.group(2)
        # capture balanced-brace body
        depth = 0; opened = False; j = i
        while j < len(lines):
            for ch in lines[j]:
                if ch == '{': depth += 1; opened = True
                elif ch == '}': depth -= 1
            if opened and depth == 0:
                break
            j += 1
        clean_pre = [l for l in pre if not any(d.search(l) for d in DROP)]
        while clean_pre and clean_pre[0].strip() == "":
            clean_pre.pop(0)
        blocks.append((name, clean_pre + lines[i:j+1]))
        pre = []
        i = j + 1
    return blocks


def header(title):
    return ("// " + title + "\n"
            "// Auto-organized by tools/split_components.py. All types are in the\n"
            "// global namespace (single assembly), so location is organizational only.\n\n"
            "using Unity.Entities;\n"
            "using Unity.Collections;\n"
            "using Unity.Mathematics;\n\n")


def main():
    parsed = {}
    for kind, path in SRC.items():
        parsed[kind] = parse(path)

    src_types = set()
    for blks in parsed.values():
        for n, _ in blks:
            if n in src_types:
                raise SystemExit(f"DUPLICATE in source: {n}")
            src_types.add(n)

    missing = [n for n in src_types if n not in CLASS]
    if missing:
        raise SystemExit(f"UNCLASSIFIED types: {missing}")

    # bucket blocks: file -> {"main":[], "parked":[]}
    files = {}
    for kind in ("Building", "Unit"):
        for name, body in parsed[kind]:
            rel, section = CLASS[name]
            files.setdefault(rel, {"main": [], "parked": []})
            files[rel][section].append((name, body))

    emitted = []
    for rel, sec in sorted(files.items()):
        out = os.path.join(CDIR, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        title = os.path.basename(rel)
        parts = [header(title)]
        for name, body in sec["main"]:
            parts.append("\n".join(body) + "\n\n")
            emitted.append(name)
        if sec["parked"]:
            parts.append(
                "// ===================================================================\n"
                "// PARKED — Runai / Feraldis / Sect / Era-2-shared content not yet\n"
                "// broken into per-culture/per-entity files (mirrors the parked SOs).\n"
                "// ===================================================================\n\n")
            for name, body in sec["parked"]:
                parts.append("\n".join(body) + "\n\n")
                emitted.append(name)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(parts).rstrip() + "\n")

    for rel, desc in SCAFFOLD:
        out = os.path.join(CDIR, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write(header(os.path.basename(rel)) + "// " + desc + "\n")

    # ---- verify parity ----
    em = set(emitted)
    dups = [n for n in emitted if emitted.count(n) > 1]
    print(f"source types: {len(src_types)}   emitted types: {len(em)}")
    print(f"missing (in source, not emitted): {sorted(src_types - em)}")
    print(f"extra   (emitted, not in source): {sorted(em - src_types)}")
    print(f"duplicates emitted: {sorted(set(dups))}")
    print(f"\nfiles written: {len(files)+len(SCAFFOLD)}")
    for rel in sorted(files):
        n = len(files[rel]['main']) + len(files[rel]['parked'])
        print(f"  {n:3}  {rel}")
    for rel, _ in SCAFFOLD:
        print(f"    0  {rel}  (scaffold)")
    ok = (src_types == em) and not dups
    print("\nPARITY OK" if ok else "\n*** PARITY FAILED ***")


if __name__ == "__main__":
    main()
