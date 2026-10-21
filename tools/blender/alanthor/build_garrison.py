# build_garrison.py - the Alanthor Garrison (the Barracks at Lvl 1) in the
# House's architecture, kitbashed from House_1.fbx's own meshes so every
# texture is the House's hand-painted one (2026-10-20).
#
#   blender -b --factory-startup --python tools/blender/alanthor/build_garrison.py
#
# Writes Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Art/Garrison.fbx
# (+ previews in Temp/garrison-previews/). Layout follows the Garrison Lvl 1:
# a walled stone terrace, a long hall on the back (three bays, a door each),
# two-storey gate towers flanking the stair gate, training posts. Parts are
# LENGTHENED by slice-and-repeat (kit.extend), never stretched, so tiles and
# bricks repeat instead of smearing; the floor's UVs are re-spread as one
# patch. Nine meshes, one per House material, digit-free names
# (BuildingRiseData reads the first digit run as a rise label). Materials
# bind by name to House/Materials/*.mat through the FBX importer remap - the
# .fbx and .fbx.meta are gitignored, exactly as for the House.
import sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import bpy
from twb_common import *
from kit import *

reset(); src=import_house(); hook_textures()
B,BU,CH,LA,MU,MR,PI,PO,TO=[src[n] for n in ('Base','Buissons','Chambranle_fenetres_et_lumiere','Lanterne','Muret','Murs','Piliers','Porte_et_lumiere_porte','Toit')]
made=[]
def D(o,name): d=dup(o,name); made.append(d); return d

# ── the terrace: the House's walled platform, widened and deepened ──────
# Slabs chosen clear of the hole the platform has under the House's walls
# (x ±1.7, y -0.23..2.2) and of the planters' rounded ends (x > 2.5).
XC, XW, XN = 2.45, 0.5, 5    # widen each side by 2.5 m
YC, YW, YN = -1.2, 0.9, 5    # deepen by 4.5 m through the side walls
DX, DY = XW*XN, -YW*YN
for o in (B, MU):
    t=D(o,'Terrace_'+o.name)
    extend(t,'x', XC, XW, XN, +1)
    extend(t,'x',-XC, XW, XN, -1)
    extend(t,'y', YC, YW, YN, -1)
# One continuous floor: the repeated slabs tiled the painted floor into a
# checkerboard. The House maps its floor planar (u = .1414x + .5028,
# v = .1404y + .3131 over x ±2.76, y -1.43..2.84); spread that same patch
# over the whole widened platform instead.
def respread_floor(o):
    import bmesh
    bm=bmesh.new(); bm.from_mesh(o.data); uv=bm.loops.layers.uv.active
    nx0,nx1=-2.76-DX, 2.76+DX; ny0,ny1=-1.43+DY, 2.84
    for f in bm.faces:
        if f.normal.z>0.95 and abs(f.calc_center_median().z-0.63)<0.03:
            for l in f.loops:
                x=-2.76+(l.vert.co.x-nx0)*(5.52/(nx1-nx0))
                y=-1.43+(l.vert.co.y-ny0)*(4.27/(ny1-ny0))
                l[uv].uv=(0.1414*x+0.5028, 0.1404*y+0.3131)
    bm.to_mesh(o.data); bm.free()
respread_floor(made[0])

# the planters, filled: big clumps along the run, the small one at the far end
for sx in (+1,-1):
    big=(lambda lo,hi: lo.x>0.9 and hi.x<2.05) if sx>0 else (lambda lo,hi: hi.x<-0.9 and lo.x>-2.05)
    small=(lambda lo,hi: lo.x>1.8) if sx>0 else (lambda lo,hi: hi.x<-1.8)
    for k,off in enumerate((0.0, 0.85, 1.7, 2.45)):
        b=D(BU,'Bush'); keep_parts(b, big); xform(b, loc=(sx*off, DY, 0))
    b=D(BU,'Bush'); keep_parts(b, small); xform(b, loc=(sx*DX, DY, 0))

# ── the hall: the House body, three bays long (one door per bay) ────────
for o in (MR, PI, CH, PO, LA, TO):
    h=D(o,'Hall_'+o.name)
    extend(h,'x', 0.9, 1.8, 1, +1, copy_inside=True)
    extend(h,'x',-0.9, 1.75, 1, -1, copy_inside=True)  # 1.75: the first pass left the left half ending exactly on 0.9
for dx in (-1.75, 0.0):      # a lantern beside every door
    l=D(LA,'Lantern'); xform(l, loc=(dx,0,0))

# ── two-storey gate towers flanking the stairs, doors facing the yard ───
PIV=(0.0, 0.97, 0.63)        # House body footprint centre, floor level
def storey(parts, s_xy, s_z, base_z, cx, cy, name, roof_s=None):
    for o in parts:
        p=D(o,name+'_'+o.name)
        if o is TO:
            rs=roof_s or s_xy
            wall_top=(2.79-0.63)*s_z
            xform(p, scale=(rs,rs,rs), pivot=(0,0.97,2.79), rotz=math.pi, loc=(cx,cy,base_z+wall_top))
        else:
            xform(p, scale=(s_xy,s_xy,s_z), pivot=PIV, rotz=math.pi, loc=(cx,cy,base_z))
TY=-1.47+DY+1.25            # just inside the front wall
for sx in (+1,-1):
    cx=sx*2.15
    storey((MR,PI,CH,PO,TO), 0.52, 0.78, 0.63, cx, TY, 'TowerLow')
    low_roof_mid = 0.63 + (2.79-0.63)*0.78 + (3.6-2.79)*0.52
    storey((MR,PI,CH,PO,TO), 0.38, 0.55, low_roof_mid, cx, TY, 'TowerHigh')
    l=D(LA,'TowerLantern'); xform(l, scale=(0.8,0.8,0.8), pivot=(1.17,-0.5,2.4), rotz=0, loc=(cx-sx*0.75, TY-0.95, 0.63+1.25))

# ── training posts in the yard (the House pillar, cut down) ─────────────
post_src=D(PI,'PostSrc'); keep_parts(post_src, lambda lo,hi: lo.x>1.3 and hi.y<0.1)
lo,hi=bounds(post_src); pc=((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)
for sx in (+1,-1):
    for i,(px,py) in enumerate(((3.7,-1.9),(4.5,-1.9),(3.7,-2.8),(4.5,-2.8))):
        p=D(post_src,'Post'); xform(p, scale=(0.75,0.75,0.6), pivot=pc, loc=(sx*px,py,0.63))
made.remove(post_src); bpy.data.objects.remove(post_src)

# ── drop the sources, join by material, digit-free names ────────────────
for o in src.values(): bpy.data.objects.remove(o)
for m in [m for m in bpy.data.materials if m.users]:
    group=[o for o in bpy.data.objects if o.type=='MESH' and o.data.materials and o.data.materials[0]==m]
    if not group: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in group: o.select_set(True)
    bpy.context.view_layer.objects.active=group[0]
    if len(group)>1: bpy.ops.object.join()
    j=bpy.context.view_layer.objects.active
    nm=m.name.replace('_texture','')
    j.name=nm; j.data.name=nm
lo=[min(v.co[i] for o in bpy.data.objects if o.type=='MESH' for v in o.data.vertices) for i in range(3)]
hi=[max(v.co[i] for o in bpy.data.objects if o.type=='MESH' for v in o.data.vertices) for i in range(3)]
print("BOUNDS", [round(x,2) for x in lo], [round(x,2) for x in hi])
cx=(lo[0]+hi[0])/2; cy=(lo[1]+hi[1])/2
for o in bpy.data.objects:
    if o.type=='MESH': o.data.transform(mathutils.Matrix.Translation((-cx,-cy,-lo[2])))
print("OBJECTS", [(o.name, len(o.data.vertices)) for o in bpy.data.objects if o.type=='MESH'])

out=os.path.join(REPO, "Temp", "garrison-previews", "")
os.makedirs(out, exist_ok=True)
render(out+"gar_top.png",(0,0,40),(0,0,0),ortho=11.8)
render(out+"gar_rts.png",(-11,-17,15),(0,0,1.2))
render(out+"gar_front.png",(0,-40,2.4),(0,0,2.4),ortho=11.8)
render(out+"gar_back.png",(12,16,12),(0,0,1.2))
bpy.ops.export_scene.fbx(filepath=os.path.join(REPO, "Assets", "GameData", "TechTree", "Civs", "Alanthor", "Buildings", "Garrison", "Art", "Garrison.fbx"), object_types={'MESH'},
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False)
print("DONE")
