# Registers the AssetLibrary folder as a Blender asset library named "Fantasy Kingdom".
# Run:  blender -b --python register_asset_library.py

import bpy

LIB_DIR = r"C:\Users\overw\Documents\The Waning Border 1.2\Blender_assets\AssetLibrary"

prefs = bpy.context.preferences
existing = {l.name: l for l in prefs.filepaths.asset_libraries}
if "Fantasy Kingdom" in existing:
    lib = existing["Fantasy Kingdom"]
    lib.path = LIB_DIR
else:
    bpy.ops.preferences.asset_library_add(directory=LIB_DIR)
    lib = prefs.filepaths.asset_libraries[-1]
    lib.name = "Fantasy Kingdom"
    # The list re-sorts on rename; re-fetch by name before touching it further
    lib = prefs.filepaths.asset_libraries.get("Fantasy Kingdom")

# Drag-and-drop appends real editable geometry (5.1 dropped APPEND_REUSE)
lib.import_method = "APPEND"

bpy.ops.wm.save_userpref()
print("REGISTERED '%s' -> %s (import: %s)" % (lib.name, lib.path, lib.import_method), flush=True)
