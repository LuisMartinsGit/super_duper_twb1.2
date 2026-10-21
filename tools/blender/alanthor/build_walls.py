# build_walls.py - the wall set in the House's architecture
# (docs/Design/Art_Direction.md § 13, 2026-10-20). One .blend + one FBX per
# piece per level, authored at GAME size so the sim and the art agree:
#
#   curtain module  3 m along the run (X) x 4 m deep (Y), walk deck at 3.0 m
#   hub             4.2 m square, deck at 3.0 m, centred on its pivot
#   gatehouse       9 m along the run (three modules), passage in the middle,
#                   two door leaves named DoorLeft / DoorRight (they swing)
#   wall tower      one module with a pagoda tower on its deck
#   mounts          one module, an empty named "Deck" at the engine's height
#
# The defining facade is named "Masonry" (WallModuleArt.IsPitchPart), the
# doors and the Deck marker stay separate objects; everything else joins by
# material. Stone Wall levels L1-L3 only, each piece written into its own
# folder, StoneWall/Lvl<N>/<Piece>/ (2026-10-21). The palisade's timber
# pieces are hand-authored (Age0/Buildings/Palisade/Lvl0/).
#
#   blender -b --factory-startup --python tools/blender/alanthor/build_walls.py -- [name ...]
import sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from kitlib import *

STONE = os.path.join(TT, "Civs", "Alanthor", "Buildings", "StoneWall")


def piece_dir(lv, folder):
    """One folder per piece per level: StoneWall/Lvl<N>/<Piece>/ holds only
    its .blend, .fbx and WallPieceArtSO (Art_Direction.md § 13)."""
    return os.path.join(STONE, "Lvl%d" % lv, folder)


PREVIEW = os.path.join(REPO, "Temp", "wall-previews")
DECK = 3.0
KEEP = set()       # object names finish_wall must not join


def facade(a, length, height, x0, y, facing, name='Facade', z0=0.0):
    """The House's plain back wall (plaster over a brick base), cut out and
    scaled to length x height; facing -1 faces -Y, +1 faces +Y, 'x+'/'x-' X."""
    f = a.D(a.MR, name)
    keep_faces(f, lambda c, n: n.y > 0.7)
    rot = {-1: math.pi, 1: 0.0, 'x+': -math.pi / 2, 'x-': math.pi / 2}[facing]
    xform(f, scale=(length / 3.7, 1.0, height / 2.31), pivot=(0, 2.3, 0.63), rotz=rot, loc=(x0, y, z0))
    return f


def deck(a, lx, ly, z=DECK, x0=0.0, y0=0.0):
    d = a.D(a.B, 'Deck_Slab')
    fill_platform_hole(d)
    keep_faces(d, lambda c, n: n.z > 0.9 and abs(c.z - 0.63) < 0.03 and -2.79 < c.x < 2.79 and -1.46 < c.y < 2.87)
    xform(d, scale=(lx / 5.6, ly / 4.35, 1.0), pivot=(0, 0.705, 0.63), loc=(x0, y0, z))
    return d


def parapet(a, length, x0, y, facing, z=DECK, h=0.8):
    p = a.D(a.MU, 'Parapet')
    keep_faces(p, lambda c, n: c.y > 2.6 and abs(c.x) < 2.7)
    rot = {-1: math.pi, 1: 0.0, 'x+': -math.pi / 2, 'x-': math.pi / 2}[facing]
    xform(p, scale=(length / 5.4, 1.0, h / 0.45), pivot=(0, 2.8, 0.63), rotz=rot, loc=(x0, y, z))


def hood(a, x, y, z=DECK + 0.85, s=(0.3, 0.12, 0.22), plank=False):
    r = a.D(a.TO, 'Hood')
    xform(r, scale=s, pivot=ROOF_PIV, loc=(x, y, z))


def post(a, x, y, h, z=0.0, w=0.45):
    a.posts([(x, y)], scale=(w, w, h / 2.52), z=z)


def body(a, length, depth=4.0, pitch=True, x0=0.0):
    """A run of stone wall: two facades, the walk deck, both parapets."""
    facade(a, length, DECK, x0, -depth / 2, -1, name='Masonry' if pitch else 'Facade')
    facade(a, length, DECK, x0, depth / 2, 1)
    deck(a, length, depth, x0=x0)
    parapet(a, length, x0, -depth / 2 + 0.15, -1)
    parapet(a, length, x0, depth / 2 - 0.15, 1)


def dress(a, length, lv, x0=0.0, depth=4.0):
    """The level's dressing along a run: L2 battlement hoods, L3 shield posts
    and lanterns."""
    n = max(1, int(round(length / 1.5)))
    for k in range(n):
        x = x0 - length / 2 + (k + 0.5) * length / n
        for side in (-1, 1):
            y = side * (depth / 2 - 0.15)
            if lv >= 2: hood(a, x, y)
            if lv >= 3: post(a, x, y, 1.6, z=DECK - 0.1, w=0.35)
    if lv >= 3:
        a.lantern(x0, -depth / 2 - 0.35, 2.3, post=False)


def curtain(lv):
    a = Assembly(); body(a, 3.0); dress(a, 3.0, lv); return a


def hub(lv):
    a = Assembly(); W = 4.2
    facade(a, W, DECK, 0, -W / 2, -1, name='Facade'); facade(a, W, DECK, 0, W / 2, 1)
    facade(a, W, DECK, W / 2, 0, 'x+'); facade(a, W, DECK, -W / 2, 0, 'x-')
    deck(a, W, W)
    for fc, x, y in ((-1, 0, -W / 2 + 0.15), (1, 0, W / 2 - 0.15), ('x+', W / 2 - 0.15, 0), ('x-', -W / 2 + 0.15, 0)):
        parapet(a, W, x, y, fc)
    for sx in (-1, 1):
        for sy in (-1, 1): post(a, sx * W / 2, sy * W / 2, DECK + 0.9, w=0.8)
    if lv >= 2:
        a.pavilion(1, loc=(0, -0.97, DECK - 0.63), scale=(0.6, 0.6, 0.55))
    if lv >= 3:
        a.upper(0.42, 0.45, loc=(0, -0.97), base_z=DECK + 1.9, door=False)
        for sx in (-1, 1): a.lantern(sx * 1.2, -W / 2 - 0.35, 2.3, post=False)
    return a


def gate(lv):
    a = Assembly(); SPAN, GAP, D_ = 9.0, 4.0, 4.0
    flank = (SPAN - GAP) / 2
    for sx in (-1, 1):
        x0 = sx * (GAP / 2 + flank / 2)
        facade(a, flank, DECK, x0, -D_ / 2, -1, name='Masonry')
        facade(a, flank, DECK, x0, D_ / 2, 1)
        facade(a, D_, DECK, sx * GAP / 2, 0, 'x-' if sx > 0 else 'x+')
        deck(a, flank, D_, x0=x0)
        parapet(a, flank, x0, -D_ / 2 + 0.15, -1); parapet(a, flank, x0, D_ / 2 - 0.15, 1)
        dress(a, flank, lv, x0=x0, depth=D_)
    # The lintel over the passage and its walk.
    deck(a, GAP, D_, z=DECK)
    facade(a, GAP, 0.7, 0, -D_ / 2, -1, z0=DECK - 0.7)
    facade(a, GAP, 0.7, 0, D_ / 2, 1, z0=DECK - 0.7)
    # The gatehouse roof on four posts over the passage.
    for sx in (-1, 1):
        for sy in (-1, 1): post(a, sx * (GAP / 2 - 0.25), sy * (D_ / 2 - 0.3), 1.9, z=DECK)
    r = a.D(a.TO, 'GateRoof')
    xform(r, scale=(GAP / 4.6 * 1.1, D_ / 3.7, 0.7), pivot=ROOF_PIV, loc=(0, 0, DECK + 1.85))
    if lv >= 3: a.upper(0.42, 0.45, loc=(0, -0.97), base_z=DECK + 2.6, door=False)
    # The two door leaves (they swing — WallGateDoors hinges each on its outer edge).
    for name, x in (("DoorLeft", -GAP / 4), ("DoorRight", GAP / 4)):
        d = a.D(a.PO, name)
        keep_parts(d, lambda lo, hi: (hi.x - lo.x) > 1.0 and (hi.z - lo.z) > 1.5)
        lo, hi = bounds(d)
        xform(d, scale=((GAP / 2) / (hi.x - lo.x), 1.0, (DECK - 0.7) / (hi.z - lo.z)),
              pivot=((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z), loc=(x, -D_ / 2 + 0.25, 0))
        KEEP.add(name)
    return a


def tower(lv):
    a = Assembly(); body(a, 3.0); dress(a, 3.0, min(lv, 2))
    a.tower(0, 0, rot=math.pi, storeys=3 if lv >= 3 else 2, s0=0.5, z=DECK)
    return a


def mount(lv, trebuchet):
    a = Assembly(); body(a, 3.0); dress(a, 3.0, lv)
    for sx in (-1, 1):
        post(a, sx * 1.35, -1.85, 1.2, z=DECK, w=0.5)
        if trebuchet: post(a, sx * 1.35, 1.85, 1.2, z=DECK, w=0.5)
    _deck_marker(DECK)
    return a


def _deck_marker(z):
    e = bpy.data.objects.new("Deck", None)
    bpy.context.scene.collection.objects.link(e)
    e.location = (0, 0, z)
    KEEP.add("Deck")


def finish_wall(a, name, out):
    """join by material, except the pitch facade, the doors and the Deck."""
    for o in a.src.values():
        if o.name in bpy.data.objects: bpy.data.objects.remove(o)
    pitch = [o for o in bpy.data.objects if o.type == 'MESH' and (o.name.startswith('Masonry') or o.name.startswith('Plank'))]
    keep = set(o.name for o in pitch) | KEEP
    for m in [m for m in bpy.data.materials if m.users]:
        group = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in keep
                 and o.data.materials and o.data.materials[0] == m]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group: o.select_set(True)
        bpy.context.view_layer.objects.active = group[0]
        if len(group) > 1: bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        nm = m.name.replace('_texture', ''); j.name = nm; j.data.name = nm
    # One pitch mesh per model: join the Masonry pieces into "Masonry".
    pitch = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('Masonry')]
    if len(pitch) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in pitch: o.select_set(True)
        bpy.context.view_layer.objects.active = pitch[0]; bpy.ops.object.join()
        pitch[0].name = 'Masonry'
    elif pitch: pitch[0].name = 'Masonry'
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo = [min(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    hi = [max(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
    print("MODEL", name, "size", [round(hi[i] - lo[i], 2) for i in range(3)], "objects", [o.name for o in bpy.data.objects])
    os.makedirs(PREVIEW, exist_ok=True)
    big = max(hi[0] - lo[0], hi[1] - lo[1], 4)
    render(os.path.join(PREVIEW, name + ".png"), (-big * 1.1, -big * 1.7, big * 1.3), (0, 0, 1.6), res=(700, 560))
    for n in ("Cam", "Sun"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
    os.makedirs(out, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), object_types={'MESH', 'EMPTY'},
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, name + ".blend"))
    try: bpy.ops.file.make_paths_relative(); bpy.ops.wm.save_mainfile()
    except Exception as e: print("relative paths:", e)
    print("WROTE", name)


# The pieces each level has (Age_1_Alanthor.md § The stone wall): every
# level the segment, hub, gate and wall tower; the Ballista mount from
# Battlemented (2), the Trebuchet from Shielded (3), where the hub is a tower.
MODELS = {}
for lv in (1, 2, 3):
    MODELS["alanthor_StoneWall_Segment_lv%d" % lv] = (lambda l=lv: (curtain(l), piece_dir(l, "Segment")))
    if lv < 3:
        MODELS["alanthor_StoneWall_Hub_lv%d" % lv] = (lambda l=lv: (hub(l), piece_dir(l, "Hub")))
    else:
        MODELS["alanthor_StoneWall_HubTower_lv3"] = (lambda: (hub(3), piece_dir(3, "Hub_Tower")))
    MODELS["alanthor_StoneWall_Gate_lv%d" % lv] = (lambda l=lv: (gate(l), piece_dir(l, "Gate")))
    MODELS["alanthor_StoneWall_Tower_lv%d" % lv] = (lambda l=lv: (tower(l), piece_dir(l, "Tower")))
    if lv >= 2:
        MODELS["alanthor_StoneWall_Ballista_lv%d" % lv] = (lambda l=lv: (mount(l, False), piece_dir(l, "Ballista")))
    if lv >= 3:
        MODELS["alanthor_StoneWall_Trebuchet_lv3"] = (lambda: (mount(3, True), piece_dir(3, "Trebuchet")))

if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in (args or list(MODELS)):
        KEEP.clear()
        a, out = MODELS[name]()
        finish_wall(a, name, out)
    print("DONE")
