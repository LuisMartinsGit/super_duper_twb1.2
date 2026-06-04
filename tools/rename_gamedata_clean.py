#!/usr/bin/env python3
"""
Normalize GameData/TechTree for Age 0 + Alanthor to the clean template:

    Assets/GameData/TechTree/<Units|Buildings>/<Culture>/<CleanName>/<CleanName>.asset
    ... plus prefabs renamed to <CleanName>[ _<level> ].prefab in the same folder.

Rules:
  - Culture folder: "Age0" -> "Age 0" (space); "Alanthor" stays.
  - CleanName = asset filename minus "Building_"/"Unit_" and minus the "Alanthor_"
    culture prefix  (Alanthor_Crucible -> Crucible, KingsCourt stays, Walls splits
    into Wall / WallGate / WallTower since each .asset gets its own folder).
  - SO .asset renamed to <CleanName>.asset; internal m_Name set to <CleanName>.
    The `id:` field is NEVER touched (runtime key).
  - Prefabs: a trailing number is treated as a level -> <CleanName>_<n>.prefab;
    no number -> <CleanName>.prefab.  (Hall_al_1/2/3 -> KingsCourt_1/2/3,
    House_al_1_A/2_A/3_A -> House_1/2/3, Hall.prefab stays Hall.prefab.)
  - .meta files move with their asset/prefab (GUIDs preserved -> catalog refs hold).
  - Folder .meta files for renamed/removed folders are dropped; fresh ones are
    generated for every new folder (folder GUIDs are not referenced anywhere).

Moves are done on the filesystem; `git add -A` afterwards records them as renames.
"""
import os, re, shutil, uuid, sys

ROOT = r"c:/Users/overw/Documents/The Waning Border 1.2"
TT = os.path.join(ROOT, "Assets/GameData/TechTree")

CULTURE_NEW = {"Age0": "Age 0", "Alanthor": "Alanthor"}

FOLDER_META = (
    "fileFormatVersion: 2\n"
    "guid: {guid}\n"
    "folderAsset: yes\n"
    "DefaultImporter:\n"
    "  externalObjects: {{}}\n"
    "  userData: \n"
    "  assetBundleName: \n"
    "  assetBundleVariant: \n"
)


def clean_name(stem: str) -> str:
    for p in ("Building_", "Unit_"):
        if stem.startswith(p):
            stem = stem[len(p):]
    if stem.startswith("Alanthor_"):
        stem = stem[len("Alanthor_"):]
    return stem


def level_of(stem: str):
    m = re.search(r"(\d+)", stem)
    return int(m.group(1)) if m else None


def move(src, dst, log):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.move(src, dst)
    log.append(("mv", os.path.relpath(src, ROOT), os.path.relpath(dst, ROOT)))


def set_m_name(asset_path, clean):
    with open(asset_path, "r", encoding="utf-8") as f:
        txt = f.read()
    new = re.sub(r"^(  m_Name: ).*$", r"\g<1>" + clean, txt, count=1, flags=re.M)
    if new != txt:
        with open(asset_path, "w", encoding="utf-8") as f:
            f.write(new)


def main():
    log = []
    new_dirs = set()          # directories that will need a fresh folder .meta
    old_entity_dirs = []      # (path) to remove if empty afterwards
    old_culture_dirs = []

    for tree in ("Units", "Buildings"):
        for culture_old in ("Age0", "Alanthor"):
            base = os.path.join(TT, tree, culture_old)
            if not os.path.isdir(base):
                continue
            culture_new = CULTURE_NEW[culture_old]
            old_culture_dirs.append(base)
            new_dirs.add(os.path.join(TT, tree, culture_new))

            for entity in sorted(os.listdir(base)):
                ep = os.path.join(base, entity)
                if not os.path.isdir(ep):
                    continue
                files = sorted(os.listdir(ep))
                assets = [f for f in files if f.endswith(".asset")]
                prefabs = [f for f in files if f.endswith(".prefab")]
                others = [f for f in files
                          if not f.endswith((".asset", ".asset.meta",
                                             ".prefab", ".prefab.meta", ".meta"))]
                if others:
                    print(f"  WARN: leaving non-asset/prefab files in {entity}: {others}")

                old_entity_dirs.append(ep)

                # --- route assets (each to its own CleanName folder) ---
                # entity-level CleanName for prefab routing
                if len(assets) == 1:
                    entity_clean = clean_name(assets[0][:-len(".asset")])
                elif len(assets) == 0:
                    entity_clean = clean_name(entity)   # e.g. House
                else:
                    entity_clean = None                 # multi (Walls) -> no prefabs

                for a in assets:
                    cn = clean_name(a[:-len(".asset")])
                    ndir = os.path.join(TT, tree, culture_new, cn)
                    new_dirs.add(ndir)
                    dst_asset = os.path.join(ndir, cn + ".asset")
                    move(os.path.join(ep, a), dst_asset, log)
                    move(os.path.join(ep, a + ".meta"),
                         os.path.join(ndir, cn + ".asset.meta"), log)
                    set_m_name(dst_asset, cn)

                # --- route prefabs ---
                if prefabs:
                    assert entity_clean is not None, \
                        f"prefabs found in multi-asset folder {entity}: {prefabs}"
                    ndir = os.path.join(TT, tree, culture_new, entity_clean)
                    new_dirs.add(ndir)
                    for p in prefabs:
                        stem = p[:-len(".prefab")]
                        lvl = level_of(stem)
                        newname = (f"{entity_clean}_{lvl}" if lvl is not None
                                   else entity_clean) + ".prefab"
                        move(os.path.join(ep, p),
                             os.path.join(ndir, newname), log)
                        move(os.path.join(ep, p + ".meta"),
                             os.path.join(ndir, newname + ".meta"), log)

    # --- remove now-empty old entity dirs + their folder metas ---
    for ep in old_entity_dirs:
        if os.path.isdir(ep) and not os.listdir(ep):
            os.rmdir(ep)
            meta = ep + ".meta"
            if os.path.exists(meta):
                os.remove(meta)
                log.append(("rm-meta", os.path.relpath(meta, ROOT), ""))
        elif os.path.isdir(ep):
            print(f"  WARN: old dir not empty, kept: {os.path.relpath(ep, ROOT)} -> {os.listdir(ep)}")

    # --- remove now-empty old culture dirs (Age0) + their metas ---
    for cp in old_culture_dirs:
        if os.path.isdir(cp) and not os.listdir(cp):
            os.rmdir(cp)
            meta = cp + ".meta"
            if os.path.exists(meta):
                os.remove(meta)
                log.append(("rm-meta", os.path.relpath(meta, ROOT), ""))

    # --- generate fresh folder metas for any new folder lacking one ---
    for d in sorted(new_dirs):
        meta = d + ".meta"
        if os.path.isdir(d) and not os.path.exists(meta):
            with open(meta, "w", encoding="utf-8", newline="\n") as f:
                f.write(FOLDER_META.format(guid=uuid.uuid4().hex))
            log.append(("mk-meta", os.path.relpath(meta, ROOT), ""))

    # --- report ---
    print(f"\n{len(log)} operations:")
    for op, a, b in log:
        if op == "mv":
            print(f"  MV  {a}\n   -> {b}")
        else:
            print(f"  {op.upper():8} {a}")


if __name__ == "__main__":
    main()
