# build_fortress.py - the Alanthor Fortress L1-L3 in the House's architecture
# (docs/Design/Art_Direction.md § 13, 2026-10-20), kitbashed from House_1.fbx's
# own meshes. Developer: "fortress is growing when upgraded" — the old castle
# art nearly doubled the Shelter's height at age-up and grew 30 % more by L3.
# These levels stand on ONE walled terrace of the footprint's size and grow
# only a little: L1 is about the Shelter's height, L2 adds the hall's upper
# roof tier and rear towers, L3 a third storey on the towers and lanterns.
#
#   blender -b --factory-startup --python tools/blender/alanthor/build_fortress.py -- [1 2 3]
#
# Writes Civs/Alanthor/Buildings/Fortress/Art/alanthor_Fortress_lv<N>.{blend,fbx}.
# (The Shelter, the Fortress's Age 0 form, keeps its prefab.)
import sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh, mathutils
from twb_common import *
from kit import *

OUT = os.path.join(REPO, "Assets", "GameData", "TechTree", "Civs", "Alanthor", "Buildings", "Fortress", "Art")
PREVIEW = os.path.join(REPO, "Temp", "fortress-previews")

DAIS = 0.6          # the palace dais stands this high on the terrace
PIV = (0.0, 0.97, 0.63)   # House body footprint centre, floor level


def build(level):
    reset()
    bpy.context.preferences.filepaths.save_version = 0
    src = import_house(); hook_textures()
    B, BU, CH, LA, MU, MR, PI, PO, TO = [src[n] for n in (
        'Base', 'Buissons', 'Chambranle_fenetres_et_lumiere', 'Lanterne', 'Muret',
        'Murs', 'Piliers', 'Porte_et_lumiere_porte', 'Toit')]
    made = []
    def D(o, n): d = dup(o, n); made.append(d); return d

    # ── the terrace: the House's walled platform, widened and deepened ──
    XC, XW, XN = 2.45, 0.5, 5
    YC, YW, YN = -1.2, 0.9, 5
    DX, DY = XW * XN, -YW * YN
    for o in (B, MU):
        t = D(o, 'Terrace_' + o.name)
        extend(t, 'x', XC, XW, XN, +1)
        extend(t, 'x', -XC, XW, XN, -1)
        extend(t, 'y', YC, YW, YN, -1)
    # one continuous floor (the Garrison's respread)
    bm = bmesh.new(); bm.from_mesh(made[0].data); uv = bm.loops.layers.uv.active
    nx0, nx1 = -2.76 - DX, 2.76 + DX; ny0, ny1 = -1.43 + DY, 2.84
    for f in bm.faces:
        if f.normal.z > 0.95 and abs(f.calc_center_median().z - 0.63) < 0.03:
            for l in f.loops:
                x = -2.76 + (l.vert.co.x - nx0) * (5.52 / (nx1 - nx0))
                y = -1.43 + (l.vert.co.y - ny0) * (4.27 / (ny1 - ny0))
                l[uv].uv = (0.1414 * x + 0.5028, 0.1404 * y + 0.3131)
    bm.to_mesh(made[0].data); bm.free()
    # planters along the front wall
    for sx in (+1, -1):
        big = (lambda lo, hi: lo.x > 0.9 and hi.x < 2.05) if sx > 0 else (lambda lo, hi: hi.x < -0.9 and lo.x > -2.05)
        small = (lambda lo, hi: lo.x > 1.8) if sx > 0 else (lambda lo, hi: hi.x < -1.8)
        for off in (0.0, 0.85, 1.7, 2.45):
            b = D(BU, 'Bush'); keep_parts(b, big); xform(b, loc=(sx * off, DY, 0))
        b = D(BU, 'Bush'); keep_parts(b, small); xform(b, loc=(sx * DX, DY, 0))

    # ── the palace dais: the House platform (and its stair), raised ──
    dais = D(B, 'Dais')
    keep_parts(dais, lambda lo, hi: (hi.x - lo.x) > 5.0 or (abs(lo.x + 0.8) < 0.05 and abs(hi.x - 0.8) < 0.05))
    xform(dais, loc=(0, 0, DAIS))

    # ── the hall on the dais: two door bays ──
    hall = [D(o, 'Hall_' + o.name) for o in (MR, PI, CH, PO, LA, TO)]
    for h in hall:
        extend(h, 'x', 0.9, 1.8, 1, +1, copy_inside=True)
        xform(h, loc=(-0.9, 0, DAIS))
    xform(D(LA, 'Lantern'), loc=(-0.9, 0, DAIS))

    if level >= 2:
        # the hall's upper roof tier
        s_xy, s_z = 0.55, 0.62
        base_z = DAIS + 0.63 + (3.6 - 0.63)
        for o in (MR, PI, CH, PO):
            xform(D(o, 'Upper_' + o.name), scale=(s_xy, s_xy, s_z), pivot=PIV, loc=(0, 0.97, base_z))
        xform(D(TO, 'Upper_Toit'), scale=(s_xy, s_xy, s_xy), pivot=(0, 0.97, 2.79),
              loc=(0, 0.97, base_z + (2.79 - 0.63) * s_z))

    # ── corner towers (two-storey pagodas; three at L3) ──
    def storey(parts, s_xy, s_z, base_z, cx, cy, rot, name):
        for o in parts:
            p = D(o, name + '_' + o.name)
            if o is TO:
                xform(p, scale=(s_xy, s_xy, s_xy), pivot=(0, 0.97, 2.79), rotz=rot,
                      loc=(cx, cy, base_z + (2.79 - 0.63) * s_z))
            else:
                xform(p, scale=(s_xy, s_xy, s_z), pivot=PIV, rotz=rot, loc=(cx, cy, base_z))
    def tower(cx, cy, rot, storeys):
        z = 0.63
        s0, z0 = 0.44, 0.7
        storey((MR, PI, CH, TO), s0, z0, z, cx, cy, rot, 'TowerA')
        z = z + (2.79 - 0.63) * z0 + (3.6 - 2.79) * s0
        storey((MR, PI, CH, TO), 0.32, 0.5, z, cx, cy, rot, 'TowerB')
        if storeys >= 3:
            z2 = z + (2.79 - 0.63) * 0.5 + (3.6 - 2.79) * 0.32
            storey((MR, PI, TO), 0.24, 0.42, z2, cx, cy, rot, 'TowerC')
    FRONT_Y = -1.47 + DY + 1.15
    corners = [(+4.0, FRONT_Y, math.pi), (-4.0, FRONT_Y, math.pi)]
    if level >= 2:
        corners += [(+4.3, 1.9, 0.0), (-4.3, 1.9, 0.0)]
    for cx, cy, rot in corners:
        tower(cx, cy, rot, 3 if level >= 3 else 2)
        if level >= 3:
            l = D(LA, 'TowerLantern')
            xform(l, scale=(0.8, 0.8, 0.8), pivot=(1.17, -0.5, 2.4),
                  loc=(cx - math.copysign(0.6, cx), cy - 0.85 if rot else cy + 0.85, 0.63 + 1.1))

    # ── join by material, digit-free names, centred, on the ground ──
    for o in src.values(): bpy.data.objects.remove(o)
    for m in [m for m in bpy.data.materials if m.users]:
        group = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.materials and o.data.materials[0] == m]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group: o.select_set(True)
        bpy.context.view_layer.objects.active = group[0]
        if len(group) > 1: bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        nm = m.name.replace('_texture', ''); j.name = nm; j.data.name = nm
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo = [min(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    cx = (lo[0] + hi[0]) / 2; cy = (lo[1] + hi[1]) / 2
    for o in meshes: o.data.transform(mathutils.Matrix.Translation((-cx, -cy, -lo[2])))
    size = [round(hi[i] - lo[i], 2) for i in range(3)]
    name = "alanthor_Fortress_lv%d" % level
    fit = 10.0 / max(size[0], size[1])
    print("MODEL", name, "size", size, "fitted height @10m", round(size[2] * fit, 2))

    os.makedirs(PREVIEW, exist_ok=True)
    render(os.path.join(PREVIEW, name + "_rts.png"), (-12, -18, 15), (0, 0, 1.5))
    render(os.path.join(PREVIEW, name + "_front.png"), (0, -40, 3.5), (0, 0, 3.5), ortho=12.5)
    for n in ("Cam", "Sun"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), object_types={'MESH'},
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    try: bpy.ops.file.make_paths_relative(); bpy.ops.wm.save_mainfile()
    except Exception as e: print("relative paths:", e)
    print("WROTE", name)


args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for lv in ([int(a) for a in args] or [1, 2, 3]):
    build(lv)
print("DONE")
