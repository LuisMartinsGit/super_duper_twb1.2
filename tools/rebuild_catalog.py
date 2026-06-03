#!/usr/bin/env python
# Rebuild Assets/Resources/TechTreeCatalog.asset from the CURRENT on-disk
# UnitDefSO/BuildingDefSO assets under Assets/GameData/TechTree/**.
#
# Does NOT modify any unit/building asset — only (re)writes the catalog so it
# references whatever is on disk now (picks up hand edits / renames / new files).
# Dedups by `id` (first wins, warns) so an accidental id collision can't break it.
# Preserves the catalog GUID so an existing scene reference keeps working.
import glob, os, yaml, hashlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITS = os.path.join(ROOT, "Assets/GameData/TechTree/Units")
BLDS  = os.path.join(ROOT, "Assets/GameData/TechTree/Buildings")
OLD_CATALOG = os.path.join(ROOT, "Assets/GameData/TechTree/TechTreeCatalog.asset")
RES = os.path.join(ROOT, "Assets/Resources")
NEW_CATALOG = os.path.join(RES, "TechTreeCatalog.asset")

CAT_SCRIPT_GUID = "16f20a137d373fd4cb360dd2197d598d"   # TechTreeCatalog.cs

def read_guid(meta):
    for line in open(meta, encoding="utf-8"):
        if line.startswith("guid:"): return line.split(":",1)[1].strip()
    return None

def catalog_guid():
    m = OLD_CATALOG + ".meta"
    if os.path.exists(m):
        g = read_guid(m)
        if g: return g
    return hashlib.md5(b"twb-resources-catalog").hexdigest()[:32]

def load_id(asset):
    b=[l for l in open(asset,encoding="utf-8").read().splitlines() if not l.startswith("%")]
    b=["---" if l.startswith("--- !u!") else l for l in b]
    return yaml.safe_load("\n".join(b))["MonoBehaviour"].get("id")

def collect(folder):
    seen={}; out=[]
    for f in sorted(glob.glob(os.path.join(folder, "**/*.asset"), recursive=True)):
        meta=f+".meta"
        if not os.path.exists(meta): continue
        i=load_id(f); g=read_guid(meta)
        if not i or not g: continue
        fname=os.path.splitext(os.path.basename(f))[0]
        if i in seen:
            print(f"  WARN dup id '{i}': keeping {seen[i]}, skipping {fname}")
            continue
        # prefer a file whose name matches the id (so Building_<id> wins over a renamed copy)
        seen[i]=fname; out.append((i,g))
    return out

units = collect(UNITS)
blds  = collect(BLDS)

def refs(items): return "".join(f"  - {{fileID: 11400000, guid: {g}, type: 2}}\n" for _i,g in items)

cat = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
       "MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n"
       "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n"
       "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
       f"  m_Script: {{fileID: 11500000, guid: {CAT_SCRIPT_GUID}, type: 3}}\n"
       "  m_Name: TechTreeCatalog\n  m_EditorClassIdentifier: \n"
       "  units:\n" + refs(units) +
       "  buildings:\n" + refs(blds))

g = catalog_guid()
meta = ("fileFormatVersion: 2\n" f"guid: {g}\n"
        "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
        "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

os.makedirs(RES, exist_ok=True)
open(NEW_CATALOG, "w", encoding="utf-8", newline="\n").write(cat)
open(NEW_CATALOG + ".meta", "w", encoding="utf-8", newline="\n").write(meta)

# remove the old GameData catalog (moved to Resources, guid preserved)
for p in (OLD_CATALOG, OLD_CATALOG + ".meta"):
    if os.path.exists(p): os.remove(p)

print(f"Catalog rebuilt at Assets/Resources/TechTreeCatalog.asset  guid={g}")
print(f"  units: {len(units)}   buildings: {len(blds)}")
