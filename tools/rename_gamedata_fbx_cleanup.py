#!/usr/bin/env python3
"""Second pass: move/rename the source .fbx meshes (git-ignored) into the clean
per-entity folders, matching the prefab naming, then remove the empty old folders.
Run after rename_gamedata_clean.py."""
import os, re, shutil

ROOT = r"c:/Users/overw/Documents/The Waning Border 1.2"
TT = os.path.join(ROOT, "Assets/GameData/TechTree")

# (old_dir_rel, target_dir_rel, CleanName)
JOBS = [
    ("Buildings/Age0/Barracks",        "Buildings/Age 0/Barracks",     "Barracks"),
    ("Buildings/Age0/Gatherers_Hut",   "Buildings/Age 0/GatherersHut", "GatherersHut"),
    ("Buildings/Age0/Hall",            "Buildings/Age 0/Hall",         "Hall"),
    ("Buildings/Age0/Hut",             "Buildings/Age 0/Hut",          "Hut"),
    ("Buildings/Alanthor/House",       "Buildings/Alanthor/House",     "House"),
    ("Buildings/Alanthor/Kings_Court", "Buildings/Alanthor/KingsCourt","KingsCourt"),
]


def level_of(stem):
    m = re.search(r"(\d+)", stem)
    return int(m.group(1)) if m else None


def main():
    log = []
    for old_rel, new_rel, cn in JOBS:
        old = os.path.join(TT, old_rel)
        new = os.path.join(TT, new_rel)
        if not os.path.isdir(old):
            continue
        os.makedirs(new, exist_ok=True)
        for f in sorted(os.listdir(old)):
            if not f.endswith(".fbx"):
                continue
            stem = f[:-len(".fbx")]
            lvl = level_of(stem)
            newname = (f"{cn}_{lvl}" if lvl is not None else cn) + ".fbx"
            for ext in ("", ".meta"):
                src = os.path.join(old, f + ext)
                dst = os.path.join(new, newname + ext)
                if os.path.exists(src) and os.path.abspath(src) != os.path.abspath(dst):
                    shutil.move(src, dst)
                    log.append((os.path.relpath(src, ROOT), os.path.relpath(dst, ROOT)))
        # remove old dir + its folder meta if now empty (and it's a different dir)
        if os.path.abspath(old) != os.path.abspath(new) and os.path.isdir(old):
            if not os.listdir(old):
                os.rmdir(old)
                meta = old + ".meta"
                if os.path.exists(meta):
                    os.remove(meta)
                    log.append((os.path.relpath(meta, ROOT), "(removed)"))
            else:
                print(f"  WARN: not empty, kept: {old_rel} -> {os.listdir(old)}")

    # remove the now-empty Buildings/Age0 culture folder + meta
    age0 = os.path.join(TT, "Buildings/Age0")
    if os.path.isdir(age0) and not os.listdir(age0):
        os.rmdir(age0)
        meta = age0 + ".meta"
        if os.path.exists(meta):
            os.remove(meta)
            log.append((os.path.relpath(meta, ROOT), "(removed)"))
    elif os.path.isdir(age0):
        print(f"  WARN: Buildings/Age0 not empty: {os.listdir(age0)}")

    print(f"\n{len(log)} fbx ops:")
    for a, b in log:
        print(f"  {a}\n   -> {b}")


if __name__ == "__main__":
    main()
