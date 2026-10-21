# build_house_family.py - the Hut line in the House's architecture
# (docs/Design/Art_Direction.md § 13, 2026-10-20): one .blend + one FBX per
# building level, kitbashed from House_1.fbx's own meshes so every texture is
# the House's hand-painted one.
#
#   blender -b --factory-startup --python tools/blender/alanthor/build_house_family.py -- [model ...]
#
# Models (default: the House levels):
#   Hut_Age_0            the Age 0 hut: platform, body, PLANK roof (no culture look)
#   alanthor_House_lv1   the House as authored (House_1)
#   alanthor_House_lv2   one bay longer: a second door and lantern
#   alanthor_House_lv3   lv2 with a roofed upper storey
#
# Each writes <Art>/<model>.blend and <Art>/<model>.fbx; previews go to
# Temp/house-family-previews/. Nine meshes per model, one per House material,
# digit-free names (BuildingRiseData reads the first digit run as a rise label).
import sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh, mathutils
from twb_common import *
from kit import *

TT = os.path.join(REPO, "Assets", "GameData", "TechTree")
ART = {
    "Hut_Age_0": os.path.join(TT, "Age0", "Buildings", "Hut", "Art"),
    "alanthor_House_lv1": os.path.join(TT, "Civs", "Alanthor", "Buildings", "House", "Art"),
    "alanthor_House_lv2": os.path.join(TT, "Civs", "Alanthor", "Buildings", "House", "Art"),
    "alanthor_House_lv3": os.path.join(TT, "Civs", "Alanthor", "Buildings", "House", "Art"),
}
PREVIEW = os.path.join(REPO, "Temp", "house-family-previews")

NAMES = ('Base', 'Buissons', 'Chambranle_fenetres_et_lumiere', 'Lanterne', 'Muret',
         'Murs', 'Piliers', 'Porte_et_lumiere_porte', 'Toit')


def start():
    reset()
    bpy.context.preferences.filepaths.save_version = 0   # no .blend1 backups
    src = import_house()
    hook_textures()
    return src


def plank_roof(obj):
    """Age 0 has no culture look (Regions.md): the teal roof tiles become the
    atlas's brown wood. Every face whose texel is teal is remapped, as ONE
    affine of the teal UV bounds, into the wood band of Roof_texture."""
    img = None
    m = obj.data.materials[0]
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE': img = n.image
    if img is None: return
    W, H = img.size
    px = img.pixels[:]
    def teal(u, v):
        x = min(W - 1, max(0, int(u * W))); y = min(H - 1, max(0, int(v * H)))
        i = (y * W + x) * 4
        r, g, b = px[i], px[i + 1], px[i + 2]
        return b > r * 1.25 and g > r
    bm = bmesh.new(); bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    faces = []
    for f in bm.faces:
        cu = sum(l[uv].uv.x for l in f.loops) / len(f.loops)
        cv = sum(l[uv].uv.y for l in f.loops) / len(f.loops)
        if teal(cu, cv): faces.append(f)
    if faces:
        us = [l[uv].uv.x for f in faces for l in f.loops]
        vs = [l[uv].uv.y for f in faces for l in f.loops]
        u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
        # The wood band (brown vertical grain, left middle of the atlas).
        W0, W1, H0, H1 = 0.07, 0.48, 0.47, 0.535
        for f in faces:
            for l in f.loops:
                l[uv].uv.x = W0 + (l[uv].uv.x - u0) / max(1e-6, u1 - u0) * (W1 - W0)
                l[uv].uv.y = H0 + (l[uv].uv.y - v0) / max(1e-6, v1 - v0) * (H1 - H0)
    bm.to_mesh(obj.data); bm.free()


def body_extend(objs, bays):
    """Lengthen the House body by whole door bays (the Garrison's rule)."""
    if bays <= 0: return
    for o in objs:
        extend(o, 'x', 0.9, 1.8, 1, +1, copy_inside=True)
        if bays >= 2:
            extend(o, 'x', -0.9, 1.75, 1, -1, copy_inside=True)


def finish(name, made):
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o not in made: bpy.data.objects.remove(o)
    for m in [m for m in bpy.data.materials if m.users]:
        group = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.materials and o.data.materials[0] == m]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group: o.select_set(True)
        bpy.context.view_layer.objects.active = group[0]
        if len(group) > 1: bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        nm = m.name.replace('_texture', '')
        j.name = nm; j.data.name = nm
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo = [min(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    cx = (lo[0] + hi[0]) / 2; cy = (lo[1] + hi[1]) / 2
    for o in meshes: o.data.transform(mathutils.Matrix.Translation((-cx, -cy, -lo[2])))
    size = [round(hi[i] - lo[i], 2) for i in range(3)]
    print("MODEL", name, "size", size, "meshes", [o.name for o in meshes])

    os.makedirs(PREVIEW, exist_ok=True)
    big = max(size[0], size[1])
    render(os.path.join(PREVIEW, name + "_rts.png"), (-big * 1.1, -big * 1.7, big * 1.5), (0, 0, size[2] * 0.3))
    render(os.path.join(PREVIEW, name + "_front.png"), (0, -40, size[2] / 2), (0, 0, size[2] / 2), ortho=big * 1.15)

    out = ART[name]
    os.makedirs(out, exist_ok=True)
    # The .blend is saved without the preview rig.
    for n in ("Cam", "Sun"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), object_types={'MESH'},
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, name + ".blend"))
    try: bpy.ops.file.make_paths_relative(); bpy.ops.wm.save_mainfile()
    except Exception as e: print("relative paths:", e)
    print("WROTE", name)


def build_hut_age0():
    src = start()
    made = []
    def D(o, n): d = dup(o, n); made.append(d); return d
    # Platform and stairs only: no low wall, no planters (Age 0 is plain).
    b = D(src['Base'], 'Base')
    keep_parts(b, lambda lo, hi: (hi.x - lo.x) > 5.0 or (abs(lo.x + 0.8) < 0.05 and abs(hi.x - 0.8) < 0.05))
    for n in ('Murs', 'Piliers', 'Porte_et_lumiere_porte', 'Chambranle_fenetres_et_lumiere'):
        D(src[n], n)
    plank_roof(D(src['Toit'], 'Toit'))
    finish("Hut_Age_0", made)


def build_house(level):
    src = start()
    made = []
    def D(o, n): d = dup(o, n); made.append(d); return d
    body = [D(src[n], n) for n in ('Murs', 'Piliers', 'Chambranle_fenetres_et_lumiere',
                                   'Porte_et_lumiere_porte', 'Lanterne', 'Toit')]
    yard = [D(src[n], n) for n in ('Base', 'Muret', 'Buissons')]
    if level >= 2:
        # One bay longer, the yard one module wider each side to match.
        body_extend(body, 1)
        # A lantern beside the first door too (the original moved with its bay).
        body.append(D(src['Lanterne'], 'Lanterne'))
        # The body grew to +x only: centre it on the yard.
        for o in body: xform(o, loc=(-0.9, 0, 0))
        for o in yard:
            extend(o, 'x', 2.45, 0.5, 2, +1)
            extend(o, 'x', -2.45, 0.5, 2, -1)
    if level >= 3:
        # A roofed upper storey on the ridge (the Garrison towers' rule).
        PIV = (0.0, 0.97, 0.63)
        s_xy, s_z = 0.55, 0.62
        base_z = 0.63 + (3.6 - 0.63)
        cx = 0.0
        for n in ('Murs', 'Piliers', 'Chambranle_fenetres_et_lumiere', 'Porte_et_lumiere_porte'):
            p = D(src[n], 'Upper' + n)
            xform(p, scale=(s_xy, s_xy, s_z), pivot=PIV, loc=(cx, 0.97, base_z))
        roof = D(src['Toit'], 'UpperToit')
        xform(roof, scale=(s_xy, s_xy, s_xy), pivot=(0, 0.97, 2.79),
              loc=(cx, 0.97, base_z + (2.79 - 0.63) * s_z))
    finish("alanthor_House_lv%d" % level, made)


# Hut_Age_0 is NOT built by default: the Hut keeps HutAge0.prefab (developer,
# 2026-10-20). Run it by name to author the plank-roof variant anyway.
OPTIONAL = {"Hut_Age_0": build_hut_age0}
BUILDERS = {
    "alanthor_House_lv1": lambda: build_house(1),
    "alanthor_House_lv2": lambda: build_house(2),
    "alanthor_House_lv3": lambda: build_house(3),
}

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for name in (args or list(BUILDERS)):
    (BUILDERS.get(name) or OPTIONAL[name])()
print("DONE")
