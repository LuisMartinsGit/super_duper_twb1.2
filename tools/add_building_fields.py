#!/usr/bin/env python
# Add description / prefabPath / upgradesToId fields to the Age 0 building SO assets.
# Preserves every existing value (loads current asset, only adds the new fields), then
# re-emits in the BuildingDefSO field order. Descriptions are generated; prefabPath and
# upgradesToId are left blank for the designer to fill in the Inspector.
import os, glob, yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AGE0 = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Age0")

DESC = {
 "Hall": "The starting town center. Trains Workers and Scouts, banks gathered resources, and "
         "researches the core economy techs and the advance to Era II. Renamed to its cultured "
         "form (Town Hall / Trader's Hall / War Hall) at age-up.",
 "Barracks": "Primary melee training building. Produces Spearmen and researches melee upgrades. "
             "Becomes the culture's melee structure (Garrison / Route Guard / Longhouse) at age-up.",
 "ArcheryRange": "Ranged training building. Its level unlocks the archer ladder "
                 "(Archer at L1, Crossbowman at L2, Longbowman at L3) and it researches the "
                 "volley / fletching techs.",
 "GatherersHut": "Early Age 0 supply generator. Emits a +Supplies aura over a small radius. "
                 "At age-up it transforms into the culture's signature economy structure "
                 "(wall anchor / trade wagon / Hunting or Logging station).",
 "Hut": "Population housing for Age 0 (the 'House'). Each one raises the population cap. "
        "Its post-age-up behavior splits per culture.",
 "VaultOfAlmierra": "Resource bank and one of the three Age 0 choice buildings that unlock the "
                    "advance to Era II. Deposited resources earn compounding interest each minute; "
                    "banking-grade techs raise the rate (Alanthor +30%, Runai -30%).",
 "ShrineOfRidan": "Early religious building and one of the three Age 0 choice buildings. Trains "
                  "Litharch healers, slowly heals friendly units in radius, and grants Religion "
                  "Points on build (Runai +30% heal, Feraldis -30%).",
 "FiendstoneKeep": "Fortified keep and one of the three Age 0 choice buildings. Trains "
                   "non-religious, non-siege military faster than normal, generates modest "
                   "Supplies, and fires arrow volleys at attackers (Feraldis +50% HP, Alanthor -50%).",
}

def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if float(v).is_integer() else repr(v)
    return "0"
def ystr(s): return "'" + str(s or "").replace("'", "''") + "'"
def arr(name, a):
    a = a or []
    return f"  {name}: []\n" if not a else f"  {name}:\n" + "".join(f"  - {ystr(x)}\n" for x in a)
def script_guid(path):
    for line in open(path, encoding="utf-8"):
        if "m_Script:" in line and "guid:" in line:
            return line.split("guid:",1)[1].split(",")[0].strip()
    return None

def emit(m):
    d = m.get("defense") or {}; c = m.get("cost") or {}
    return ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
        "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
        "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
        f"  m_Script: {{fileID: 11500000, guid: {m['_sg']}, type: 3}}\n"
        f"  m_Name: Building_{m['id']}\n  m_EditorClassIdentifier: \n"
        f"  id: {ystr(m['id'])}\n  displayName: {ystr(m.get('displayName'))}\n  role: {ystr(m.get('role'))}\n"
        f"  description: {ystr(m.get('description'))}\n"
        f"  hp: {num(m.get('hp',1000))}\n  armorType: {ystr(m.get('armorType') or 'structure_human')}\n"
        "  defense:\n"
        f"    melee: {num(d.get('melee',0))}\n    ranged: {num(d.get('ranged',0))}\n"
        f"    siege: {num(d.get('siege',0))}\n    magic: {num(d.get('magic',0))}\n"
        f"  radius: {num(m.get('radius',1.6))}\n  lineOfSight: {num(m.get('lineOfSight',20))}\n"
        + arr("trains", m.get("trains")) + arr("research", m.get("research")) +
        f"  minEra: {num(m.get('minEra',0))}\n"
        "  cost:\n"
        f"    Supplies: {num(c.get('Supplies',0))}\n    Iron: {num(c.get('Iron',0))}\n"
        f"    Crystal: {num(c.get('Crystal',0))}\n    Veilsteel: {num(c.get('Veilsteel',0))}\n"
        f"    Glow: {num(c.get('Glow',0))}\n"
        f"  prefabPath: {ystr(m.get('prefabPath'))}\n"
        f"  upgradesToId: {ystr(m.get('upgradesToId'))}\n")

for f in sorted(glob.glob(os.path.join(AGE0, "*.asset"))):
    body=[l for l in open(f,encoding="utf-8").read().splitlines() if not l.startswith("%")]
    body=["---" if l.startswith("--- !u!") else l for l in body]
    m=yaml.safe_load("\n".join(body))["MonoBehaviour"]
    m["_sg"]=script_guid(f)
    m["description"]=DESC.get(m["id"], m.get("description",""))
    m.setdefault("prefabPath", m.get("prefabPath",""))
    m.setdefault("upgradesToId", m.get("upgradesToId",""))
    open(f,"w",encoding="utf-8",newline="\n").write(emit(m))
    print(f"  +fields {m['id']:16s} desc={len(m['description'])} chars")
