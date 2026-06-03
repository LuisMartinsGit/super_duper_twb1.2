#!/usr/bin/env python
# Align Age 0 building SO stats to docs/Design/Age_0.md (the authoritative design doc;
# it supersedes the old TechTree.json values the assets were generated from).
# Edits ONLY Age 0 building .asset files. Preserves fields the doc doesn't specify
# (armorType, defense, research) by reading them from the existing asset.
import os, yaml

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AGE0 = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings/Age0")

# Doc-specified overrides per source filename. Cost keys omitted = 0.
OVERRIDES = {
    "Building_Hall": dict(hp=2400, lineOfSight=24, cost={}),
    "Building_Barracks": dict(hp=800, lineOfSight=18, cost=dict(Supplies=220, Iron=40)),
    "Building_ArcheryRange": dict(hp=600, lineOfSight=18, cost=dict(Supplies=180, Iron=50)),
    "Building_GatherersHut": dict(hp=800, lineOfSight=16, cost=dict(Supplies=120, Iron=10)),
    "Building_Hut": dict(hp=600, lineOfSight=14, cost=dict(Supplies=80)),
    "Building_VaultOfAlmierra": dict(hp=1200, lineOfSight=14, cost=dict(Supplies=300, Crystal=100)),
    "Building_ShrineOfRidan": dict(hp=800, lineOfSight=16, cost=dict(Supplies=300, Crystal=100),
                                   trains=["Litharch"]),
    # FirnfdtonrKeep is a broken copy of the Vault (wrong id). Turn it into the
    # doc's Fiendstone Keep (Age 0 choice building).
    "Building_FirnfdtonrKeep": dict(id="FiendstoneKeep", displayName="Fiendstone Keep",
                                    role="Fortified training + supply (Age 0 choice)",
                                    hp=2000, lineOfSight=18, radius=2.4,
                                    cost=dict(Supplies=300, Crystal=100),
                                    trains=["Swordsman", "Archer"],
                                    defense=dict(melee=2, ranged=2, siege=0, magic=0)),
}

def num(v):
    if isinstance(v, bool): return "1" if v else "0"
    if isinstance(v, int): return str(v)
    if isinstance(v, float): return str(int(v)) if float(v).is_integer() else repr(v)
    return "0"

def ystr(s):
    return "'" + str(s or "").replace("'", "''") + "'"

def arr(name, a):
    a = a or []
    if not a: return f"  {name}: []\n"
    return f"  {name}:\n" + "".join(f"  - {ystr(x)}\n" for x in a)

def emit(m):
    d = m.get("defense") or {}
    c = m.get("cost") or {}
    s = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
         "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
         "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
         "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
         f"  m_Script: {{fileID: 11500000, guid: {m['_script_guid']}, type: 3}}\n"
         f"  m_Name: {m['_mname']}\n  m_EditorClassIdentifier: \n"
         f"  id: {ystr(m['id'])}\n  displayName: {ystr(m.get('displayName'))}\n  role: {ystr(m.get('role'))}\n"
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
         f"    Glow: {num(c.get('Glow',0))}\n")
    return s

def script_guid(path):
    for line in open(path, encoding="utf-8"):
        if "m_Script:" in line and "guid:" in line:
            return line.split("guid:",1)[1].split(",")[0].strip()
    return None

for fname, ov in OVERRIDES.items():
    path = os.path.join(AGE0, fname + ".asset")
    if not os.path.exists(path):
        print(f"  SKIP (missing): {fname}"); continue
    raw = open(path, encoding="utf-8").read()
    body=[l for l in raw.splitlines() if not l.startswith("%")]
    body=["---" if l.startswith("--- !u!") else l for l in body]
    m = yaml.safe_load("\n".join(body))["MonoBehaviour"]
    m["_script_guid"] = script_guid(path)
    # apply overrides
    for k, v in ov.items():
        m[k] = v
    # m_Name follows id (Building_<id>) for clarity
    m["_mname"] = "Building_" + m["id"]
    open(path, "w", encoding="utf-8", newline="\n").write(emit(m))
    print(f"  patched {fname} -> id={m['id']} hp={m.get('hp')} los={m.get('lineOfSight')} "
          f"cost={ {k:v for k,v in (m.get('cost') or {}).items() if v} }")
