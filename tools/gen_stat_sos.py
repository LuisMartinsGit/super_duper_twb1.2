#!/usr/bin/env python
# Generate Unity ScriptableObject .asset files (UnitDefSO / BuildingDefSO) + a
# TechTreeCatalog from Assets/Resources/TechTree.json, organized by culture.
#
# This is a one-time bootstrap so the editable stat assets exist on disk without
# round-tripping through the in-editor generator. The in-Unity generator
# (TechTreeSOGenerator) remains the canonical regenerator; this mirrors its output.
#
# Run:  python tools/gen_stat_sos.py
import json, os, hashlib, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JSON_PATH = os.path.join(ROOT, "Assets", "Resources", "TechTree.json")
GAMEDATA  = os.path.join(ROOT, "Assets", "GameData")
TT        = os.path.join(GAMEDATA, "TechTree")
UNITS     = os.path.join(TT, "Units")
BLDS      = os.path.join(TT, "Buildings")

def read_guid(meta_path):
    for line in open(meta_path, encoding="utf-8"):
        m = re.match(r"\s*guid:\s*([0-9a-fA-F]+)", line)
        if m: return m.group(1)
    raise RuntimeError("no guid in " + meta_path)

UNIT_SCRIPT_GUID = read_guid(os.path.join(ROOT, "Assets/Scripts/Data/TechTree/Definitions/UnitDefSO.cs.meta"))
BLD_SCRIPT_GUID  = read_guid(os.path.join(ROOT, "Assets/Scripts/Data/TechTree/Definitions/BuildingDefSO.cs.meta"))
CAT_SCRIPT_GUID  = read_guid(os.path.join(ROOT, "Assets/Scripts/Data/TechTree/TechTreeCatalog.cs.meta"))

def guid_for(seed):
    return hashlib.md5(("twb-stat-so::" + seed).encode("utf-8")).hexdigest()[:32]

def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if v.is_integer() else repr(v)
    return "0"

def ystr(s):
    # single-quote, doubling embedded single quotes (Unity-compatible YAML scalar)
    if s is None: s = ""
    return "'" + str(s).replace("'", "''") + "'"

def defense_block(d):
    d = d or {}
    return ("  defense:\n"
            f"    melee: {num(d.get('melee',0))}\n"
            f"    ranged: {num(d.get('ranged',0))}\n"
            f"    siege: {num(d.get('siege',0))}\n"
            f"    magic: {num(d.get('magic',0))}\n")

def cost_block(c):
    c = c or {}
    return ("  cost:\n"
            f"    Supplies: {num(c.get('Supplies',0))}\n"
            f"    Iron: {num(c.get('Iron',0))}\n"
            f"    Crystal: {num(c.get('Crystal',0))}\n"
            f"    Veilsteel: {num(c.get('Veilsteel',0))}\n"
            f"    Glow: {num(c.get('Glow',0))}\n")

def str_array(name, arr):
    arr = arr or []
    if not arr: return f"  {name}: []\n"
    out = f"  {name}:\n"
    for it in arr: out += f"  - {ystr(it)}\n"
    return out

ASSET_HEADER = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
                "MonoBehaviour:\n"
                "  m_ObjectHideFlags: 0\n"
                "  m_CorrespondingSourceObject: {fileID: 0}\n"
                "  m_PrefabInstance: {fileID: 0}\n"
                "  m_PrefabAsset: {fileID: 0}\n"
                "  m_GameObject: {fileID: 0}\n"
                "  m_Enabled: 1\n"
                "  m_EditorHideFlags: 0\n")

def asset_meta(guid):
    return ("fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "NativeFormatImporter:\n"
            "  externalObjects: {}\n"
            "  mainObjectFileID: 11400000\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n")

def folder_meta(guid):
    return ("fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n")

created_folders = []
def ensure_folder(path):
    if not os.path.isdir(path):
        os.makedirs(path, exist_ok=True)
    meta = path + ".meta"
    if not os.path.exists(meta):
        rel = os.path.relpath(path, ROOT).replace("\\", "/")
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write(folder_meta(guid_for("folder:" + rel)))
        created_folders.append(rel)

def write_unit(uid, name, unit_class, culture, d):
    folder = os.path.join(UNITS, culture); ensure_folder(folder)
    fname = "Unit_" + uid
    path = os.path.join(folder, fname + ".asset")
    aguid = guid_for(fname)
    hp   = d.get("hp") or 100
    spd  = d.get("speed") or 5
    tt   = d.get("trainingTime") or 5
    dmg  = d.get("damage") or 10
    arng = d.get("attackRange") or 1.5
    body = ASSET_HEADER
    body += f"  m_Script: {{fileID: 11500000, guid: {UNIT_SCRIPT_GUID}, type: 3}}\n"
    body += f"  m_Name: {fname}\n  m_EditorClassIdentifier: \n"
    body += f"  id: {ystr(uid)}\n  displayName: {ystr(name)}\n  unitClass: {ystr(unit_class or '')}\n"
    body += f"  hp: {num(hp)}\n  speed: {num(spd)}\n  trainingTime: {num(tt)}\n"
    body += f"  damage: {num(dmg)}\n  damageType: {ystr(d.get('damageType') or 'melee')}\n"
    body += f"  armorType: {ystr(d.get('armorType') or 'infantry')}\n"
    body += defense_block(d.get("defense"))
    body += f"  attackCooldown: {num(d.get('attackCooldown',0))}\n"
    body += f"  attackRange: {num(arng)}\n  minAttackRange: {num(d.get('minAttackRange',0))}\n"
    body += f"  lineOfSight: {num(d.get('lineOfSight') or 20)}\n"
    body += cost_block(d.get("cost"))
    body += f"  minBuildingLevel: {num(d.get('minBuildingLevel',0))}\n"
    body += f"  buildSpeed: {num(d.get('buildSpeed',0))}\n"
    body += f"  gatheringSpeed: {num(d.get('gatheringSpeed',0))}\n"
    body += f"  carryCapacity: {num(d.get('carryCapacity',0))}\n"
    body += f"  healsPerSecond: {num(d.get('healsPerSecond',0))}\n"
    open(path, "w", encoding="utf-8", newline="\n").write(body)
    open(path + ".meta", "w", encoding="utf-8", newline="\n").write(asset_meta(aguid))
    return (uid, aguid, culture)

def write_building(bid, name, culture, d):
    folder = os.path.join(BLDS, culture); ensure_folder(folder)
    fname = "Building_" + bid
    path = os.path.join(folder, fname + ".asset")
    aguid = guid_for(fname)
    body = ASSET_HEADER
    body += f"  m_Script: {{fileID: 11500000, guid: {BLD_SCRIPT_GUID}, type: 3}}\n"
    body += f"  m_Name: {fname}\n  m_EditorClassIdentifier: \n"
    body += f"  id: {ystr(bid)}\n  displayName: {ystr(name or bid)}\n  role: {ystr(d.get('role') or '')}\n"
    body += f"  hp: {num(d.get('hp') or 1000)}\n"
    body += f"  armorType: {ystr(d.get('armorType') or 'structure_human')}\n"
    body += defense_block(d.get("baseDefense") or d.get("defense"))
    body += f"  radius: {num(d.get('radius') or 1.6)}\n"
    body += f"  lineOfSight: {num(d.get('lineOfSight') or 20)}\n"
    body += str_array("trains", d.get("trains"))
    body += str_array("research", d.get("research"))
    body += f"  minEra: {num(d.get('minEra',0))}\n"
    body += cost_block(d.get("cost"))
    open(path, "w", encoding="utf-8", newline="\n").write(body)
    open(path + ".meta", "w", encoding="utf-8", newline="\n").write(asset_meta(aguid))
    return (bid, aguid, culture)

# ---- build ----
data = json.load(open(JSON_PATH, encoding="utf-8"))
for p in (GAMEDATA, TT, UNITS, BLDS): ensure_folder(p)

units, buildings = [], []
eras = data.get("eras", [])

# Era 0 = pre-culture (Age0)
if eras:
    e0 = eras[0]
    for u in e0.get("units", []):
        units.append(write_unit(u["id"], u.get("name", u["id"]), u.get("class"), "Age0", u))
    for b in e0.get("buildings", []):
        buildings.append(write_building(b["id"], b.get("name"), "Age0", b))

# Era 1 = cultures
if len(eras) > 1:
    for cul in eras[1].get("cultures", []):
        cname = cul.get("id") or cul.get("culture") or "Culture"
        if "main" in cul and isinstance(cul["main"], dict):
            m = cul["main"]; buildings.append(write_building(m["id"], m.get("name"), cname, m))
        for b in cul.get("buildings", []):
            buildings.append(write_building(b["id"], b.get("name"), cname, b))
        for u in cul.get("units", []):
            units.append(write_unit(u["id"], u.get("name", u["id"]), u.get("class"), cname, u))

# Sect units (embedded) -> Sect culture, normalized id like the runtime parser
for s in (data.get("sects", {}) or {}).get("list", []) or []:
    su = s.get("unit")
    if not su or not su.get("id"): continue
    raw = su["id"]; nid = "Sect_" + raw.replace("_", ""); disp = raw.replace("_", " ")
    sd = dict(su)
    sd.setdefault("hp", 100); sd.setdefault("speed", 5); sd.setdefault("damage", 10)
    sd.setdefault("attackRange", 1.5); sd.setdefault("lineOfSight", 14)
    sd.setdefault("trainingTime", 15); sd.setdefault("armorType", "infantry_heavy")
    sd.setdefault("damageType", "melee")
    c = sd.get("cost") or {}
    if not (c.get("Supplies") or c.get("Iron") or c.get("Crystal")):
        sd["cost"] = {"Supplies": 100, "Iron": 50}
    units.append(write_unit(nid, disp, su.get("class"), "Sect", sd))

# Crystal-faction creatures (constants-only, not in JSON)
CRYSTAL = [
    ("Crystalling", "Crystalling", "magic",  dict(hp=72,  speed=5.5, damage=8,  lineOfSight=10, attackRange=1.5, attackCooldown=0.8, trainingTime=8,  armorType="infantry_light", damageType="magic", cost={"Crystal":50})),
    ("Veilstinger", "Veilstinger", "ranged", dict(hp=78,  speed=4.0, damage=18, lineOfSight=28, attackRange=24, minAttackRange=8, trainingTime=10, armorType="ranged", damageType="ranged", cost={"Crystal":150})),
    ("Godsplinter", "Godsplinter", "siege",  dict(hp=1440,speed=1.8, damage=80, lineOfSight=60, attackRange=60, attackCooldown=5.0, trainingTime=20, armorType="structure", damageType="siege", cost={"Crystal":500})),
]
for uid, nm, cls, d in CRYSTAL:
    units.append(write_unit(uid, nm, cls, "Crystal", d))

# ---- catalog ----
def ref(guid): return "{fileID: 11400000, guid: %s, type: 2}" % guid
cat = ASSET_HEADER
cat += f"  m_Script: {{fileID: 11500000, guid: {CAT_SCRIPT_GUID}, type: 3}}\n"
cat += "  m_Name: TechTreeCatalog\n  m_EditorClassIdentifier: \n"
cat += "  units:\n" + "".join(f"  - {ref(g)}\n" for (_id, g, _c) in units)
cat += "  buildings:\n" + "".join(f"  - {ref(g)}\n" for (_id, g, _c) in buildings)
cat_path = os.path.join(TT, "TechTreeCatalog.asset")
open(cat_path, "w", encoding="utf-8", newline="\n").write(cat)
open(cat_path + ".meta", "w", encoding="utf-8", newline="\n").write(asset_meta(guid_for("TechTreeCatalog")))

print(f"Units: {len(units)}  Buildings: {len(buildings)}  Folders: {len(created_folders)}")
from collections import Counter
print("Units by culture:   ", dict(Counter(c for _i,_g,c in units)))
print("Buildings by culture:", dict(Counter(c for _i,_g,c in buildings)))
print("Catalog:", os.path.relpath(cat_path, ROOT))
