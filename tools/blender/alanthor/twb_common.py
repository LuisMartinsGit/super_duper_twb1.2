# Shared Blender helpers for the Alanthor House-style kitbash (House_1.fbx
# meshes + textures). Used by build_garrison.py.
import bpy, bmesh, math, os, mathutils
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
HOUSE = os.path.join(REPO, "Assets", "GameData", "TechTree", "Civs", "Alanthor", "Buildings", "House")
TEX = {  # material -> texture folder, stem
 'Base_texture': ('Foundation (Base)','Foundation'),
 'Buissons_texture': ('Bushes (Buissons)','Bushes'),
 'Chambranle_fenetres_et_lumiere_texture': ('Window_Frames_and_Light (Chambranle_fenetres_et_lumiere)','Window_Frames_and_Light'),
 'Lanterne_texture': ('Lantern (Lanterne)','Lantern'),
 'Muret_texture': ('Low_Wall (Muret)','Low_Wall'),
 'Murs_texture': ('Wall (Murs)','Wall'),
 'Piliers_texture': ('Pillars (Piliers)','Pillars'),
 'Porte_et_lumiere_porte_texture': ('Door_and_light_door(Porte_et_lumiere_porte)','Door_and_light_door'),
 'Toit_texture': ('Roof (Toit)','Roof'),
}
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
def import_house():
    bpy.ops.import_scene.fbx(filepath=os.path.join(HOUSE,'House_1.fbx'))
    objs={o.name:o for o in bpy.data.objects if o.type=='MESH'}
    # bake world transforms into the meshes, unparent
    for o in objs.values():
        mw=o.matrix_world.copy(); o.parent=None; o.matrix_world=mw
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs.values(): o.select_set(True)
    bpy.context.view_layer.objects.active=next(iter(objs.values()))
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.data.objects):
        if o.type=='EMPTY': bpy.data.objects.remove(o)
    return objs
def hook_textures():
    for m in bpy.data.materials:
        if m.name not in TEX: continue
        folder,stem=TEX[m.name]
        m.use_nodes=True; nt=m.node_tree; nt.nodes.clear()
        out=nt.nodes.new('ShaderNodeOutputMaterial'); bsdf=nt.nodes.new('ShaderNodeBsdfPrincipled')
        nt.links.new(bsdf.outputs[0],out.inputs[0])
        p=os.path.join(HOUSE,'Textures',folder,stem+'_texture_AlbedoTransparency.png')
        if os.path.exists(p):
            t=nt.nodes.new('ShaderNodeTexImage'); t.image=bpy.data.images.load(p,check_existing=True)
            nt.links.new(t.outputs[0],bsdf.inputs['Base Color'])
        e=os.path.join(HOUSE,'Textures',folder,stem+'_texture_Emission.png')
        if os.path.exists(e):
            t2=nt.nodes.new('ShaderNodeTexImage'); t2.image=bpy.data.images.load(e,check_existing=True)
            nt.links.new(t2.outputs[0],bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value=1.0
        bsdf.inputs['Roughness'].default_value=0.8
def render(path, loc, look_at, ortho=None, res=(1100,850)):
    sc=bpy.context.scene
    sc.render.engine='BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = res
    if sc.world is None: sc.world=bpy.data.worlds.new('W')
    sc.world.use_nodes=True; bg=sc.world.node_tree.nodes.get('Background')
    bg.inputs[0].default_value=(0.42,0.46,0.42,1); bg.inputs[1].default_value=0.9
    if 'Sun' not in bpy.data.objects:
        ld=bpy.data.lights.new('Sun','SUN'); ld.energy=3.2; s=bpy.data.objects.new('Sun',ld); sc.collection.objects.link(s)
        s.rotation_euler=(math.radians(50),0,math.radians(35))
    cam=bpy.data.objects.get('Cam')
    if cam is None:
        cd=bpy.data.cameras.new('Cam'); cam=bpy.data.objects.new('Cam',cd); sc.collection.objects.link(cam)
    sc.camera=cam
    cam.location=loc
    d=mathutils.Vector(look_at)-mathutils.Vector(loc)
    cam.rotation_euler=d.to_track_quat('-Z','Y').to_euler()
    if ortho: cam.data.type='ORTHO'; cam.data.ortho_scale=ortho
    else: cam.data.type='PERSP'; cam.data.lens=50
    sc.render.filepath=path
    bpy.ops.render.render(write_still=True)
