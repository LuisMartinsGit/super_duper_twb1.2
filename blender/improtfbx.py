


import bpy, os, math, mathutils
from pathlib import Path

FBX_DIR    = r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\FBX"      # source FBX folder
LIBRARY_DIR = r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\FBX"             # output .blend folder
THUMB_SIZE  = 256

# 4 atlases. The "tag" becomes the asset name suffix.
ATLASES = [
    ("Texture_light_1",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_01.png"),
    ("Texture_light_2",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_02.png"),
    ("Texture_light_3",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_03.png"),
    ("Texture_light_4",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_04.png"),

    ("Texture_dark_1",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_01_Dark.png"),
    ("Texture_dark_2",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_02_Dark.png"),
    ("Texture_dark_3",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_03_Dark.png"),
    ("Texture_dark_4",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_Alt_04_Dark.png"),

    ("Texture_snow",  r"C:\Users\overw\Downloads\POLYGON_Knights_Source_Files_v3\Source_Files\Textures\Texture_01_Swap_Snow_To_Grass.png"), 
]
os.makedirs(LIBRARY_DIR, exist_ok=True)

def make_atlas_material(name, image_path):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out  = nt.nodes.new("ShaderNodeOutputMaterial");  out.location  = (300, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location  = (0, 0)
    tex  = nt.nodes.new("ShaderNodeTexImage");        tex.location  = (-400, 0)

    # Low-poly atlases usually want flat shading and crisp pixels.
    tex.interpolation = 'Closest'
    tex.image = bpy.data.images.load(image_path, check_existing=True)
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Specular IOR Level"].default_value = 0.0  # Blender 4.x; fall back ignored

    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat

def world_bounds(meshes):
    mn = mathutils.Vector(( math.inf,  math.inf,  math.inf))
    mx = mathutils.Vector((-math.inf, -math.inf, -math.inf))
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c)
            mn = mathutils.Vector(min(mn[i], w[i]) for i in range(3))
            mx = mathutils.Vector(max(mx[i], w[i]) for i in range(3))
    return mn, mx

for fbx in Path(FBX_DIR).rglob("*.fbx"):
    for tag, atlas_path in ATLASES:
        out_blend = Path(LIBRARY_DIR) / f"{fbx.stem}_{tag}.blend"
        if out_blend.exists():
            continue

        bpy.ops.wm.read_homefile(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(fbx))

        # Group everything into one collection
        coll = bpy.data.collections.new(f"{fbx.stem}_{tag}")
        bpy.context.scene.collection.children.link(coll)
        for obj in list(bpy.context.scene.collection.objects):
            bpy.context.scene.collection.objects.unlink(obj)
            coll.objects.link(obj)

        meshes = [o for o in coll.objects if o.type == 'MESH']
        if not meshes:
            continue

        # Force every mesh to use the atlas material
        mat = make_atlas_material(f"Atlas_{tag}", atlas_path)
        for m in meshes:
            m.data.materials.clear()
            m.data.materials.append(mat)

        # Camera framing
        mn, mx = world_bounds(meshes)
        center = (mn + mx) * 0.5
        radius = max((mx - mn).length * 0.5, 0.5)

        cam_data = bpy.data.cameras.new("ThumbCam")
        cam_obj  = bpy.data.objects.new("ThumbCam", cam_data)
        bpy.context.scene.collection.objects.link(cam_obj)
        direction = mathutils.Vector((1.0, -1.0, 0.7)).normalized()
        cam_obj.location = center + direction * radius * 3.0
        look = (center - cam_obj.location).normalized()
        cam_obj.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()

        light_data = bpy.data.lights.new("ThumbLight", type='SUN')
        light_data.energy = 3.0
        light_obj  = bpy.data.objects.new("ThumbLight", light_data)
        bpy.context.scene.collection.objects.link(light_obj)
        light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(30))

        scn = bpy.context.scene
        scn.camera = cam_obj
        # Eevee renders the actual material — Workbench would ignore the texture
        scn.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items} else 'BLENDER_EEVEE'
        scn.render.resolution_x = THUMB_SIZE
        scn.render.resolution_y = THUMB_SIZE
        scn.render.film_transparent = True

        thumb = str(Path(LIBRARY_DIR) / f"_{fbx.stem}_{tag}_thumb.png")
        scn.render.filepath = thumb
        bpy.ops.render.render(write_still=True)

        # Mark + attach preview
        coll.asset_mark()
        with bpy.context.temp_override(id=coll):
            bpy.ops.ed.lib_id_load_custom_preview(filepath=thumb)

        bpy.ops.wm.save_as_mainfile(filepath=str(out_blend))
        try: os.remove(thumb)
        except OSError: pass

print("done")