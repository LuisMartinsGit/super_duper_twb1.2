# Extracts the ground-truth mesh -> material -> texture mapping from the Unity
# Fantasy Kingdom pack (prefab YAML + .mat files + .meta GUID index) into
# unity_material_map.json, consumed by build_asset_library.py.
# Run:  blender -b --python map_from_unity.py

import json, re
from pathlib import Path

PACK = Path(r"C:\Users\overw\Documents\The Waning Border 1.2\Assets\Synty\PolygonFantasyKingdom")
OUT = Path(r"C:\Users\overw\Documents\The Waning Border 1.2\Blender_assets\unity_material_map.json")

# 1. GUID index for every asset in the pack
guid_to_path = {}
for meta in PACK.rglob("*.meta"):
    m = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="ignore"))
    if m:
        guid_to_path[m.group(1)] = meta.with_suffix("")  # strip .meta

# 2. Material guid -> main texture path
mat_info = {}  # mat guid -> {"name":..., "texture": path-or-None}
for mat_path in PACK.rglob("*.mat"):
    meta = mat_path.with_suffix(".mat.meta")
    if not meta.exists():
        continue
    own_guid = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="ignore"))
    if not own_guid:
        continue
    text = mat_path.read_text(encoding="utf-8", errors="ignore")
    # Collect every texture property with a real guid; prefer albedo-ish names
    props = re.findall(r"- (_\w+):\s*\n\s*m_Texture:\s*\{fileID: \d+, guid: ([0-9a-f]{32})", text)
    tex_guid = None
    for name, g in props:
        if re.search(r"albedo|maintex|basemap|basecolor|diffuse", name, re.I):
            tex_guid = g
            break
    if tex_guid is None:
        for name, g in props:
            if not re.search(r"normal|bump|emission|occlusion|metallic|specular|detail", name, re.I):
                tex_guid = g
                break
    tex_path = guid_to_path.get(tex_guid) if tex_guid else None
    mat_info[own_guid.group(1)] = {
        "name": mat_path.stem,
        "texture": str(tex_path) if tex_path else None,
    }

# 3. Prefab -> ordered material list (as the guids appear in the file)
result = {}
mat_guid_pat = re.compile(r"guid: ([0-9a-f]{32})")
for prefab in PACK.rglob("*.prefab"):
    text = prefab.read_text(encoding="utf-8", errors="ignore")
    mats = []
    in_materials = False
    for line in text.splitlines():
        if "m_Materials:" in line:
            in_materials = True
            continue
        if in_materials:
            s = line.strip()
            if s.startswith("- {"):
                m = mat_guid_pat.search(s)
                if m and m.group(1) in mat_info:
                    info = mat_info[m.group(1)]
                    if info not in mats:
                        mats.append(info)
            else:
                in_materials = False
    if mats:
        result[prefab.stem] = mats

OUT.write_text(json.dumps(result, indent=1), encoding="utf-8")
print("GUIDS: %d  MATS: %d  PREFABS MAPPED: %d -> %s" % (len(guid_to_path), len(mat_info), len(result), OUT), flush=True)

# Show the previously-wrong cases for sanity
for probe in ("SM_Bld_Castle_Wall_01", "SM_Bld_House_Roof_Thatch_01", "SM_Prop_Flag_01", "SM_Bld_Castle_Floor_01"):
    print("%s -> %s" % (probe, result.get(probe)), flush=True)
