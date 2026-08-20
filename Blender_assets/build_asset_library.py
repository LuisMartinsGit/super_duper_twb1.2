# Builds the Fantasy Kingdom asset library from the Synty FBX dump.
# Run per category:  blender -b --python build_asset_library.py -- <Category>
# Categories: Buildings Props Items Weapons Environment Generic Characters Vehicles Misc

import bpy, os, sys, math, uuid, json, traceback, mathutils
from pathlib import Path

FBX_DIR   = Path(r"C:\Users\overw\Documents\The Waning Border 1.2\Blender_assets\Fantasy Kingdom Assets")
LIB_DIR   = Path(r"C:\Users\overw\Documents\The Waning Border 1.2\Blender_assets\AssetLibrary")
TEX_DIR   = Path(r"C:\Users\overw\Documents\The Waning Border 1.2\Assets\Synty\PolygonFantasyKingdom\Textures")
ATLAS_TEX = TEX_DIR / "Alts" / "PolygonFantasyKingdom_01_A.png"
GLASS_TEX = TEX_DIR / "Misc" / "PolygonFantasyKingdom_Texture_Glass_01_Crossed.png"
THUMB_SIZE = 256
UUID_NS = uuid.uuid5(uuid.NAMESPACE_URL, "twb-fantasy-kingdom-library")

CATEGORY_MAP = {
    "Bld": "Buildings", "Prop": "Props", "Item": "Items", "Wep": "Weapons",
    "Env": "Environment", "Generic": "Generic", "Chr": "Characters", "Veh": "Vehicles",
}
SUB_SPLIT = {"Castle", "House", "Mod"}  # subjects that get a third catalog level

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
TARGET_CATEGORY = argv[0] if argv else "Misc"

# Ground-truth mesh -> material mapping parsed from the Unity pack's prefabs
# (see map_from_unity.py). The FBX-embedded texture refs are stale dev-machine
# paths, so this is the authority; the FBX heuristics below are the fallback.
_map_path = Path(__file__).parent / "unity_material_map.json"
UNITY_MAP = json.loads(_map_path.read_text(encoding="utf-8")) if _map_path.exists() else {}


def entry_to_key(entry):
    """Unity material entry -> shared-material cache key."""
    name_l = entry["name"].lower()
    tex = entry.get("texture") or ""
    base = tex.replace("\\", "/").split("/")[-1].lower()
    if "glass" in name_l or "glass" in base:
        return "GLASS"
    if not tex or "polygonfantasykingdom" in base or "emmisive" in base:
        return "ATLAS"
    return tex


def classify(stem):
    """FBX stem -> catalog path tuple, e.g. ('Buildings', 'Castle', 'Wall')."""
    t = stem.split("_")
    if len(t) >= 3 and t[0] in {"SM", "SK", "FX"} and t[1] in CATEGORY_MAP:
        cat, subject = CATEGORY_MAP[t[1]], t[2]
        if subject in SUB_SPLIT and len(t) >= 4 and not t[3].isdigit():
            return (cat, subject, t[3])
        return (cat, subject)
    return ("Misc",)


def catalog_uuid(path_tuple):
    return str(uuid.uuid5(UUID_NS, "/".join(path_tuple)))


def write_catalog_file(all_stems):
    """Regenerate blender_assets.cats.txt from every stem (deterministic UUIDs)."""
    paths = set()
    for s in all_stems:
        p = classify(s)
        for i in range(1, len(p) + 1):
            paths.add(p[:i])
    lines = ["VERSION 1", ""]
    for p in sorted(paths):
        lines.append("%s:%s:%s" % (catalog_uuid(p), "/".join(p), "-".join(p)))
    (LIB_DIR / "blender_assets.cats.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def make_material(name, image_path, glass=False, closest=True):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (300, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (0, 0)
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.location = (-400, 0)
    tex.interpolation = "Closest" if closest else "Linear"
    tex.image = bpy.data.images.load(str(image_path), check_existing=True)
    bsdf.inputs["Roughness"].default_value = 0.85
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.0
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    if glass:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "BLENDED"
        if hasattr(mat, "blend_method"):
            mat.blend_method = "BLEND"
    return mat


# Most meshes UV to the atlas, but castle walls/floors, roofs, flags, paintings
# and horses are UV-mapped for the pack's dedicated tiling textures. The FBX
# materials carry the original texture reference; map its basename to the right
# texture from the Unity project.
def pick_texture(base):
    """FBX texture basename -> texture path, 'GLASS', or None for the atlas."""
    if "glass" in base:
        return "GLASS"
    if "normal" in base or not base:
        return None
    if "roof" in base:
        return TEX_DIR / "Castle" / "Roof_Tile_01.png"
    if base == "wall_01.psd":
        return TEX_DIR / "Castle" / "Wall_Brick_01.png"
    if base == "wall.bmp":
        return TEX_DIR / "Castle" / "Wall_Large_Brick_01.png"
    if "symbols" in base:
        return TEX_DIR / "Flags" / "Flag_Lion_01_Red.png"
    if "painting" in base:
        return TEX_DIR / "Misc" / "Paintings_01.png"
    if "horse" in base:
        return TEX_DIR / "Misc" / "Horse_01.png"
    return None


def diffuse_basename(mat):
    """Basename of the image feeding the imported material's Base Color."""
    if not mat or not mat.use_nodes:
        return ""
    img = None
    nt = mat.node_tree
    principled = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if principled:
        sock = principled.inputs.get("Base Color")
        if sock and sock.links:
            seen, stack = set(), [sock.links[0].from_node]
            while stack:
                nd = stack.pop()
                if nd in seen:
                    continue
                seen.add(nd)
                if nd.type == "TEX_IMAGE" and nd.image:
                    img = nd.image
                    break
                for inp in nd.inputs:
                    stack.extend(l.from_node for l in inp.links)
    if img is None:
        for nd in nt.nodes:
            if nd.type == "TEX_IMAGE" and nd.image:
                cand = (nd.image.filepath or nd.image.name).lower()
                if "normal" not in cand:
                    img = nd.image
                    break
    if img is None:
        return ""
    return (img.filepath or img.name).replace("\\", "/").split("/")[-1].lower()


def world_bounds(objs):
    mn = mathutils.Vector((math.inf,) * 3)
    mx = mathutils.Vector((-math.inf,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c)
            mn = mathutils.Vector(min(mn[i], w[i]) for i in range(3))
            mx = mathutils.Vector(max(mx[i], w[i]) for i in range(3))
    return mn, mx


def import_fbx(path):
    before = set(bpy.data.objects)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path))
    return [o for o in bpy.data.objects if o not in before]


def main():
    LIB_DIR.mkdir(parents=True, exist_ok=True)
    all_fbx = sorted(FBX_DIR.glob("*.fbx"))
    write_catalog_file([f.stem for f in all_fbx])
    todo = [f for f in all_fbx if classify(f.stem)[0] == TARGET_CATEGORY]
    print("CATEGORY %s: %d files" % (TARGET_CATEGORY, len(todo)), flush=True)
    if not todo:
        return

    bpy.ops.wm.read_homefile(use_empty=True)
    scn = bpy.context.scene

    engines = {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    scn.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    if hasattr(scn, "eevee") and hasattr(scn.eevee, "taa_render_samples"):
        scn.eevee.taa_render_samples = 8
    scn.render.resolution_x = THUMB_SIZE
    scn.render.resolution_y = THUMB_SIZE
    scn.render.film_transparent = True

    world = bpy.data.worlds.new("ThumbWorld")
    scn.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.9, 0.9, 0.9, 1.0)
        bg.inputs[1].default_value = 0.7

    cam_data = bpy.data.cameras.new("ThumbCam")
    cam_obj = bpy.data.objects.new("ThumbCam", cam_data)
    scn.collection.objects.link(cam_obj)
    scn.camera = cam_obj

    light_data = bpy.data.lights.new("ThumbLight", type="SUN")
    light_data.energy = 3.0
    light_obj = bpy.data.objects.new("ThumbLight", light_data)
    scn.collection.objects.link(light_obj)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(30))

    mat_cache = {}

    def shared_material(key):
        if key not in mat_cache:
            if key == "ATLAS":
                mat_cache[key] = make_material("FantasyKingdom_Atlas_01A", ATLAS_TEX)
            elif key == "GLASS":
                mat_cache[key] = make_material("FantasyKingdom_Glass", GLASS_TEX, glass=True)
            else:
                mat_cache[key] = make_material("FK_" + Path(key).stem, key, closest=False)
        return mat_cache[key]

    def resolve_slot(src_mat, entries, idx, total):
        base = diffuse_basename(src_mat)
        src_glass = "glass" in base or (src_mat and "glass" in src_mat.name.lower())
        if entries:
            # Slot counts match the prefab's material list -> align by order
            if total == len(entries):
                return shared_material(entry_to_key(entries[idx]))
            if src_glass:
                return shared_material("GLASS")
            non_glass = [e for e in entries if entry_to_key(e) != "GLASS"]
            return shared_material(entry_to_key(non_glass[0] if non_glass else entries[0]))
        if src_glass:
            return shared_material("GLASS")
        tex = pick_texture(base)
        if tex == "GLASS":
            return shared_material("GLASS")
        return shared_material(str(tex) if tex else "ATLAS")

    # One PNG per asset: lib_id_load_custom_preview caches by filepath, so a
    # reused path would give every asset the first render's thumbnail.
    thumb_dir = LIB_DIR / ("_thumbs_%s" % TARGET_CATEGORY)
    thumb_dir.mkdir(exist_ok=True)
    failed = []

    for idx, fbx in enumerate(todo):
        stem = fbx.stem
        try:
            objs = import_fbx(fbx)
            meshes = [o for o in objs if o.type == "MESH"]
            if not objs:
                failed.append((stem, "no objects imported"))
                continue

            # Bake the Y-up -> Z-up axis conversion into the geometry. The
            # importer stores it as object rotation, which asset drag-drop
            # resets, leaving dropped assets standing on end. Rigged assets
            # are left alone so the armature binding stays intact.
            if not any(o.type == "ARMATURE" for o in objs):
                for o in bpy.context.view_layer.objects:
                    o.select_set(False)
                for o in objs:
                    o.select_set(True)
                bpy.context.view_layer.objects.active = objs[0]
                try:
                    if any(o.parent for o in objs):
                        bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
                    bpy.ops.object.make_single_user(type="SELECTED_OBJECTS", object=True, obdata=True)
                    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
                except RuntimeError as e:
                    print("  transform-apply skipped for %s: %s" % (stem, e), flush=True)

            entries = UNITY_MAP.get(stem)
            total_slots = sum(len(m.material_slots) for m in meshes)
            slot_idx = 0
            for m in meshes:
                if not m.material_slots:
                    m.data.materials.append(shared_material("ATLAS"))
                    continue
                for slot in m.material_slots:
                    slot.material = resolve_slot(slot.material, entries, slot_idx, total_slots)
                    slot_idx += 1

            # Frame the camera on the new objects and render the thumbnail
            bounds_src = meshes if meshes else objs
            mn, mx = world_bounds(bounds_src)
            center = (mn + mx) * 0.5
            radius = max((mx - mn).length * 0.5, 0.5)
            direction = mathutils.Vector((1.0, -1.0, 0.7)).normalized()
            cam_obj.location = center + direction * radius * 3.0
            look = (center - cam_obj.location).normalized()
            cam_obj.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()

            thumb_path = str(thumb_dir / (stem + ".png"))
            scn.render.filepath = thumb_path
            bpy.ops.render.render(write_still=True)

            # Single mesh with no rig -> object asset; anything else -> collection asset
            armatures = [o for o in objs if o.type == "ARMATURE"]
            if len(meshes) == 1 and not armatures and len(objs) == 1:
                asset_id = meshes[0]
                asset_id.name = stem
                asset_id.data.name = stem
            else:
                coll = bpy.data.collections.new(stem)
                for o in objs:
                    for c in list(o.users_collection):
                        c.objects.unlink(o)
                    coll.objects.link(o)
                asset_id = coll

            asset_id.asset_mark()
            asset_id.use_fake_user = True
            asset_id.asset_data.catalog_id = catalog_uuid(classify(stem))
            asset_id.asset_data.author = "Synty POLYGON Fantasy Kingdom"
            # Empty tokens (double underscores in the name) crash tags.new()
            for tag in stem.split("_")[1:]:
                if tag and not tag.isdigit():
                    try:
                        asset_id.asset_data.tags.new(tag.lower(), skip_if_exists=True)
                    except TypeError:
                        asset_id.asset_data.tags.new(tag.lower())
            with bpy.context.temp_override(id=asset_id):
                bpy.ops.ed.lib_id_load_custom_preview(filepath=thumb_path)

            # Detach from the scene so the depsgraph stays small
            keep = asset_id if isinstance(asset_id, bpy.types.Collection) else None
            for o in objs:
                for c in list(o.users_collection):
                    if c is not keep:
                        try:
                            c.objects.unlink(o)
                        except RuntimeError:
                            pass
            if (idx + 1) % 25 == 0 or idx == len(todo) - 1:
                print("  [%d/%d] %s" % (idx + 1, len(todo), stem), flush=True)
        except Exception as e:
            failed.append((stem, str(e)))
            traceback.print_exc()

    # Drop leftover import junk (unused images/materials from the FBX files)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

    bpy.ops.file.pack_all()
    out_blend = LIB_DIR / ("Library_%s.blend" % TARGET_CATEGORY)
    bpy.ops.wm.save_as_mainfile(filepath=str(out_blend), compress=True)

    import shutil
    shutil.rmtree(thumb_dir, ignore_errors=True)
    print("SAVED %s  (%d assets, %d failed)" % (out_blend.name, len(todo) - len(failed), len(failed)), flush=True)
    if failed:
        log = LIB_DIR / ("_failed_%s.txt" % TARGET_CATEGORY)
        log.write_text("\n".join("%s: %s" % f for f in failed), encoding="utf-8")
        for f in failed:
            print("  FAILED %s: %s" % f, flush=True)


main()
