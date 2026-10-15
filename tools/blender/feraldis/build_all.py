"""
build_all.py — regenerate every Feraldis building FBX (and preview PNGs).

    blender -b --python tools/blender/feraldis/build_all.py -- [--preview DIR] [names...]

Each building module beside this file exposes:
    NAME        the export stem (e.g. "SlavePens")
    OUT_DIR     Assets/... folder the FBX lands in (relative to the repo root)
    LEVELS      the level tags to build (e.g. ["Lv1", "Lv2", "Lv3"], or
                ["Lv0", "Lv1", "Lv2", "Lv3"] for a culture-only building with
                a construction frame, or ["Model"] for a single-form building)
    build(tag)  -> list of Blender objects for that level

No module reads any other culture's art. Run from the repo root.
"""
import sys, os, importlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)

import kit  # noqa: E402

ALL = ["slavepens", "workcamp", "warhall", "fortress", "greatforge", "hallofaxes",
       "rigyard", "engineworks", "wartotem", "longhouse", "huntinglodge", "loggingstation",
       "mine", "bloodaltar", "shelter"]


PALETTES = {
    # prefix -> the palette.json the matching Unity author step reads
    "Fer_":  "Assets/GameData/TechTree/Civs/Feraldis/Buildings/Art/palette.json",
    "Age0_": "Assets/GameData/TechTree/Age0/Buildings/Fortress/Art/palette.json",
}


def write_palette():
    """The material palettes, for FeraldisArtAuthor.cs — the generator is the
    one source of every colour. Feraldis materials go under Civs/Feraldis/
    (edition-stripped), the culture-neutral Age 0 ones beside the Shelter."""
    import json
    for prefix, rel in PALETTES.items():
        rows = []
        for name, (base, metal, rough, em, strength) in kit.PALETTE.items():
            if not name.startswith(prefix):
                continue
            rows.append({"name": name, "base": list(base), "metallic": metal, "roughness": rough,
                         "emission": list(em) if em else [0, 0, 0], "strength": strength})
        path = os.path.join(ROOT, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"materials": rows}, f, indent=1)
        print("PALETTE", path)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = None
    names = []
    i = 0
    while i < len(argv):
        if argv[i] == "--preview":
            preview = argv[i + 1]; i += 2
        elif argv[i] == "--palette-only":
            write_palette(); return
        else:
            names.append(argv[i]); i += 1
    if not names:
        names = ALL
    write_palette()
    failures = []
    for n in names:
        try:
            mod = importlib.import_module(n)
            importlib.reload(mod)
            out_dir = os.path.join(ROOT, mod.OUT_DIR)
            for tag in mod.LEVELS:
                kit.reset_scene()
                objs = mod.build(tag)
                kit.export_building(mod.NAME, objs, out_dir, preview, tag)
        except Exception as e:
            import traceback
            traceback.print_exc()
            failures.append((n, str(e)))
    print("BUILD_DONE failures=%d" % len(failures))
    for n, e in failures:
        print("FAILED", n, e)


if __name__ == "__main__":
    main()
