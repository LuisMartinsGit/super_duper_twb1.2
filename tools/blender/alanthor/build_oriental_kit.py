# The Alanthor ORIENTAL kit (2026-11-10): Synty castle-kit roof and pillar
# pieces re-shaped for Japanese roofs and red lacquer columns, written as new
# FBX files. Synty's own files are never touched.
#
#   blender -b --python tools/blender/alanthor/build_oriental_kit.py -- [out dir] [preview png stem]
#
# Out dir defaults to Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Oriental.
# Besides the FBX it writes Al_Lacquer_Palette.png (the flat colours the
# lacquer / gilt / plaster faces sample) and kit_manifest.json (piece ->
# material slot names, read by Waning Border > Art > Bind Oriental Kit, which
# makes one prefab per FBX with the materials on).
#
# What a roof piece gets (all in metres, Blender axes: a piece runs -X from its
# pivot, the eave points -Y, Z is up; Unity sees +X / +Z / +Y):
#   - the pitch is lowered and the slope made concave (shallow at the eave,
#     steep at the ridge),
#   - the eave is carried OVERHANG metres past the wall line and kicked up,
#   - "Tip" pieces and corners lift the eave toward the corner,
#   - round-tile ribs run down every slope, rafter tails are lacquer red.
# Slot names: Tile (player-colour tile), Wall (stone / plaster), Lacquer
# (the palette), Atlas (Synty's atlas, where a piece keeps atlas faces), Paper
# (the shoji panes: the palette's paper colour on a material that glows).
#
# Shoji walls have the castle kit's wall sizes and pivots: they run +X from the
# pivot (Unity -X), 0.5 m thick about the wall line.

import bpy, bmesh, sys, os, math, json
from mathutils import Vector
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SYN = os.path.join(ROOT, "Assets", "Synty", "PolygonFantasyKingdom", "Models")
ART = os.path.join(ROOT, "Assets", "GameData", "TechTree", "Civs", "Alanthor", "Buildings", "Art")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if len(argv) > 0 else os.path.join(ART, "Oriental")
PREVIEW = argv[1] if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

TILE, ATLAS, WALL, LAC, PAPER = "Tile", "Atlas", "Wall", "Lacquer", "Paper"
ORDER = [TILE, ATLAS, WALL, LAC, PAPER]

# --- the roof profile ------------------------------------------------------
OVERHANG = 1.25      # eave past the wall line
EAVE_SLOPE = 0.5     # rise per metre at the wall line
KICK = 0.2           # how far the eave tip turns back up
BASE = 0.35          # tile surface above the wall top at the wall line
THICK = 0.55         # vertical thickness kept from the Synty slab
LIFT = 0.75          # corner upturn at the eave tip
LIFT_IN = 1.5        # the upturn has faded this far inside the wall line
FAMILIES = {         # run, Synty top-line break, new rise, tip-lift falloff
    "M": dict(R=3.75, yb=2.87, H=2.9),
    "S": dict(R=2.5, yb=1.9, H=2.0),
}
RIB_STEP = 0.5

# --- the lacquer palette ---------------------------------------------------
PALETTE = [
    ("red", (0.64, 0.09, 0.06)), ("darkred", (0.40, 0.05, 0.04)), ("gold", (0.86, 0.62, 0.16)), ("black", (0.07, 0.06, 0.06)),
    ("white", (0.93, 0.91, 0.85)), ("timber", (0.33, 0.20, 0.12)), ("palegold", (0.95, 0.80, 0.42)), ("stone", (0.62, 0.60, 0.56)),
    ("paper", (0.97, 0.92, 0.80)), ("darktimber", (0.17, 0.10, 0.07)),
]
PAL = {n: i for i, (n, _) in enumerate(PALETTE)}


def pal_uv(name):
    i = PAL[name]
    return ((i % 4 + 0.5) / 4.0, 1.0 - (i // 4 + 0.5) / 4.0)


def write_palette():
    """The palette as a 64 px PNG, written by hand so it does not depend on Blender's image saving."""
    import zlib, struct
    size, cell = 64, 16
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            i = (y // cell) * 4 + x // cell
            c = PALETTE[i][1] if i < len(PALETTE) else (0.5, 0.5, 0.5)
            row += bytes(int(round(255 * v)) for v in c)
        rows.append(bytes(row))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)
    png = bytes([137, 80, 78, 71, 13, 10, 26, 10]) + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")
    path = os.path.join(OUT, "Al_Lacquer_Palette.png")
    with open(path, "wb") as f:
        f.write(png)
    return bpy.data.images.load(path)


# --- mesh helpers ------------------------------------------------------------
def load(name, slots):
    """A Synty piece as a bmesh in metres, faces re-indexed to ORDER."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SYN, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    obj = [o for o in new if o.type == 'MESH'][0]
    bpy.context.view_layer.update()
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    remap = [ORDER.index(s) for s in slots]
    for f in bm.faces:
        f.material_index = remap[f.material_index]
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return bm


def new_bm():
    bm = bmesh.new()
    bm.loops.layers.uv.new("map1")
    return bm


def uvl(bm):
    return bm.loops.layers.uv.active


def cut(bm, axis, vals):
    for v in vals:
        co = Vector((0, 0, 0)); co[axis] = v
        no = Vector((0, 0, 0)); no[axis] = 1
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-4, plane_co=co, plane_no=no)


def clip(bm, axis, val, keep_below=True):
    co = Vector((0, 0, 0)); co[axis] = val
    no = Vector((0, 0, 0)); no[axis] = 1 if keep_below else -1
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-4, plane_co=co, plane_no=no, clear_outer=True)


def steps(a, b, n):
    return [a + (b - a) * i / n for i in range(1, n)]


def paint(bm, faces, colour):
    uv = pal_uv(colour); L = uvl(bm)
    for f in faces:
        f.material_index = ORDER.index(LAC)
        for l in f.loops:
            l[L].uv = uv


def tile_fit(bm):
    """Affine (x, y) -> (u, v) of the piece's up-facing tile faces."""
    L = uvl(bm); rows = []; us = []; vs = []
    for f in bm.faces:
        if f.material_index != 0 or f.normal.z < 0.2:
            continue
        for l in f.loops:
            rows.append((l.vert.co.x, l.vert.co.y, 1.0)); us.append(l[L].uv.x); vs.append(l[L].uv.y)
    A = np.array(rows)
    cu = np.linalg.lstsq(A, np.array(us), rcond=None)[0]
    cv = np.linalg.lstsq(A, np.array(vs), rcond=None)[0]
    return lambda x, y: (cu[0] * x + cu[1] * y + cu[2], cv[0] * x + cv[1] * y + cv[2])


def prism(bm, pts, side, w, h, mat, uvf, cap=True):
    """A half-hexagon rib along the points; `side` is the horizontal direction across it."""
    L = uvl(bm); rings = []
    up = Vector((0, 0, 1))
    for p in pts:
        rings.append([bm.verts.new(p - side * w), bm.verts.new(p - side * w * 0.5 + up * h),
                      bm.verts.new(p + side * w * 0.5 + up * h), bm.verts.new(p + side * w)])
    faces = []
    for a, b in zip(rings, rings[1:]):
        for i in range(3):
            faces.append(bm.faces.new((a[i], a[i + 1], b[i + 1], b[i])))
    if cap:
        faces.append(bm.faces.new(rings[0]))
        faces.append(bm.faces.new(rings[-1]))
    mi = ORDER.index(mat) if isinstance(mat, str) and mat in ORDER else ORDER.index(LAC)
    for f in faces:
        f.material_index = mi
        for l in f.loops:
            l[L].uv = uvf(l.vert.co.x, l.vert.co.y) if callable(uvf) else uvf
    return faces


def box(bm, lo, hi, colour, taper_bottom=None):
    """An axis-aligned box painted one palette colour; taper_bottom scales the bottom ring in X/Y."""
    x0, y0, z0 = lo; x1, y1, z1 = hi
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    def b(x, y):
        if taper_bottom is None: return (x, y, z0)
        return (cx + (x - cx) * taper_bottom, cy + (y - cy) * taper_bottom, z0)
    v = [bm.verts.new(b(x0, y0)), bm.verts.new(b(x1, y0)), bm.verts.new(b(x1, y1)), bm.verts.new(b(x0, y1)),
         bm.verts.new((x0, y0, z1)), bm.verts.new((x1, y0, z1)), bm.verts.new((x1, y1, z1)), bm.verts.new((x0, y1, z1))]
    quads = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    faces = [bm.faces.new([v[i] for i in q]) for q in quads]
    paint(bm, faces, colour)
    return faces


def lathe(bm, profile, colour, sides=12, cap_top=False, cap_bottom=False):
    """Rings (z, radius) joined into a round shaft."""
    rings = []
    for z, r in profile:
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / sides), r * math.sin(2 * math.pi * i / sides), z)) for i in range(sides)])
    faces = []
    for a, b in zip(rings, rings[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    if cap_top: faces.append(bm.faces.new(rings[-1]))
    if cap_bottom: faces.append(bm.faces.new(list(reversed(rings[0]))))
    paint(bm, faces, colour)
    return faces


def extrude_profile(bm, pts, axis, lo, hi, colour):
    """A polygon (2D points) extruded along an axis: axis 'y' takes (x, z) points, axis 'x' takes (y, z)."""
    def mk(p, t):
        return (p[0], t, p[1]) if axis == 'y' else (t, p[0], p[1])
    a = [bm.verts.new(mk(p, lo)) for p in pts]
    b = [bm.verts.new(mk(p, hi)) for p in pts]
    faces = [bm.faces.new(a), bm.faces.new(list(reversed(b)))]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((a[j], a[i], b[i], b[j])))
    paint(bm, faces, colour)
    return faces


# --- the profile functions ---------------------------------------------------
def zf(d, fam):
    """Tile surface height over the wall top, d metres inside the wall line (negative = out on the eave)."""
    if d <= 0:
        return EAVE_SLOPE * d + KICK * (d / OVERHANG) ** 2
    R, H = fam["R"], fam["H"]
    b = (H - EAVE_SLOPE * R) / (R * R)
    return EAVE_SLOPE * d + b * d * d


def lift_w(d):
    t = (LIFT_IN - d) / (LIFT_IN + OVERHANG)
    t = max(0.0, min(1.0, t))
    return t * t


def synty_top(y, fam):
    return 0.49 + 4.51 * min(max(y, 0.0), fam["yb"]) / fam["yb"]


def stretch(y, fam):
    R = fam["R"]
    return -OVERHANG + y * (R + OVERHANG) / R


def map_slope(bm, fam, corner=None):
    """Synty slope space -> the Japanese profile. corner(x) gives 0..1, how much of the corner lift a vertex takes."""
    for v in bm.verts:
        x, y, z = v.co
        d = stretch(y, fam)
        c = corner(x) if corner else 0.0
        v.co = Vector((x, d, BASE + zf(d, fam) + (z - synty_top(y, fam)) * THICK + LIFT * c * lift_w(d)))


def ribs_slope(bm, fam, x0, x1, uvf):
    R = fam["R"]
    n = 12
    x = x0 + RIB_STEP / 2
    while x < x1 - 1e-3:
        pts = [Vector((x, -0.02 + (R + 0.02) * j / n, synty_top(-0.02 + (R + 0.02) * j / n, fam) - 0.05)) for j in range(n + 1)]
        prism(bm, pts, Vector((1, 0, 0)), 0.1, 0.22, TILE, uvf)
        x += RIB_STEP


def lacquer_underside(bm):
    """Everything that is not tile or wall becomes lacquer red (rafters, brackets, plates)."""
    paint(bm, [f for f in bm.faces if f.material_index == ORDER.index(ATLAS)], "red")


# --- roof pieces -------------------------------------------------------------
def slope_piece(famkey, width=5.0, tip=None):
    fam = FAMILIES[famkey]
    bm = load(f"SM_Bld_Castle_Roof_{famkey}_01", [TILE, ATLAS] if famkey == "M" else [ATLAS, TILE])
    uvf = tile_fit(bm)
    if width < 5.0:
        clip(bm, 0, -5.0 + width, keep_below=False)   # keep the pivot end
    cut(bm, 1, steps(0, fam["R"], 12))
    if tip:
        cut(bm, 0, steps(-width, 0, 8))
    ribs_slope(bm, fam, -width, 0.0, uvf)
    lacquer_underside(bm)
    corner = None
    if tip == "far":
        corner = lambda x: max(0.0, 1.0 - (x + width) / 5.0) ** 2
    elif tip == "near":
        corner = lambda x: max(0.0, 1.0 + x / 5.0) ** 2
    map_slope(bm, fam, corner)
    return bm


def hafu_piece(famkey):
    """The curved bargeboard of a gable end, lacquer red, with a gilt pendant under the ridge."""
    fam = FAMILIES[famkey]; R = fam["R"]
    bm = load(f"SM_Bld_Castle_Roof_{famkey}_End_01", [TILE, WALL])
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == ORDER.index(WALL)], context='FACES')
    cut(bm, 1, steps(0, R, 12))
    paint(bm, bm.faces[:], "red")
    # a gilt edge strip along the top of the board
    paint(bm, [f for f in bm.faces if f.normal.z > 0.3], "gold")
    map_slope(bm, fam, lambda x: 1.0)
    top = BASE + fam["H"]
    # gegyo: the pendant under the ridge (half of it; the other slope's piece brings the other half)
    extrude_profile(bm, [(R - 0.42, top - 0.55), (R, top - 1.25), (R, top - 0.35)], 'x', -0.36, 0.36, "gold")
    return bm


def gable_piece(famkey):
    """The wall triangle under a gable, cut to sit under the concave roof (half: wall line to ridge)."""
    fam = FAMILIES[famkey]; R = fam["R"]
    bm = load(f"SM_Bld_Castle_Roof_{famkey}_End_01", [TILE, WALL])
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != ORDER.index(WALL)], context='FACES')
    cut(bm, 1, steps(0.5, R, 10))
    y0, z0, y1, z1 = 1.07, 0.39, R, 4.76
    for v in bm.verts:
        x, y, z = v.co
        line = z0 if y <= y0 else z0 + (y - y0) * (z1 - z0) / (y1 - y0)
        frac = max(0.0, min(1.0, z / line))
        under = BASE + zf(y, fam) - 0.3
        v.co = Vector((x, y, frac * max(0.05, under)))
    return bm


def hip_corner_piece(famkey):
    fam = FAMILIES[famkey]; R = fam["R"]
    bm = load(f"SM_Bld_Castle_Roof_{famkey}_Corner_Sharp_01", [ATLAS, TILE])
    uvf = tile_fit(bm)
    cut(bm, 1, steps(0, R, 10))
    cut(bm, 0, steps(-R, 0, 10))
    top = lambda xb, y: synty_top(min(xb, y), fam)
    n = 10
    t = RIB_STEP / 2 + 0.25
    while t < R - 0.2:
        # face A (eave along X): a rib running up from the eave to the hip
        pts = [Vector((t - R, (t - 0.12) * j / n, top(t, (t - 0.12) * j / n) - 0.05)) for j in range(n + 1)]
        prism(bm, pts, Vector((1, 0, 0)), 0.1, 0.22, TILE, uvf)
        # face B (eave along Y)
        pts = [Vector(((t - 0.12) * j / n - R, t, top((t - 0.12) * j / n, t) - 0.05)) for j in range(n + 1)]
        prism(bm, pts, Vector((0, 1, 0)), 0.1, 0.22, TILE, uvf)
        t += RIB_STEP
    # the hip roll down the diagonal
    side = Vector((1, -1, 0)).normalized()
    pts = [Vector((-R + R * j / 14 - 0.03, R * j / 14 - 0.03, synty_top(R * j / 14, fam) - 0.05)) for j in range(15)]
    prism(bm, pts, side, 0.2, 0.34, TILE, uvf)
    lacquer_underside(bm)
    W = R + OVERHANG
    for v in bm.verts:
        x, y, z = v.co
        xb = x + R
        dA, dB = stretch(y, fam), stretch(xb, fam)
        d = min(dA, dB)
        c = max(0.0, 1.0 - abs(dA - dB) / W) ** 2
        v.co = Vector((dB - R, dA, BASE + zf(d, fam) + (z - synty_top(min(y, xb), fam)) * THICK + LIFT * c * lift_w(d)))
    # the gilt finial where the hip roll meets the eave corner
    tipz = BASE + zf(-OVERHANG, FAMILIES[famkey]) + LIFT * lift_w(-OVERHANG)
    tx, ty = -R - OVERHANG, -OVERHANG
    extrude_profile(bm, [(-0.05, tipz - 0.1), (0.55, tipz + 0.05), (0.28, tipz + 0.28), (-0.12, tipz + 0.75), (-0.3, tipz + 0.2)], 'y', -0.09, 0.09, "gold")
    # the profile was drawn on the X axis: turn it onto the diagonal and move it to the tip
    new = bm.verts[-10:]
    ang = math.radians(225)
    for v in new:
        px, py = v.co.x, v.co.y
        v.co = Vector((tx + (px * math.cos(ang) - py * math.sin(ang)), ty + (px * math.sin(ang) + py * math.cos(ang)), v.co.z))
    return bm


SKIRT_OFF = BASE - 0.21   # the Synty ledge's top face sits at 0.21


def ledge_top(y):
    """Top face of the Synty cornice ledge: flat lip, then a ramp up to the kerb."""
    return 0.21 if y < -1.05 else 0.21 + (min(y, 0.16) + 1.05) / 1.21 * 0.28


def skirt_piece(width=5.0, tip=None):
    """The Synty cornice ledge as a tiled pent eave (the eave ring of a storey, or of a gable wall)."""
    src = "SM_Bld_Castle_Roof_L_Edge_01" if width > 2.5 else "SM_Bld_Castle_Roof_L_Edge_Half_01"
    bm = load(src, [WALL, TILE])
    full = 5.0 if width > 2.5 else 2.5
    for v in bm.verts:
        v.co.x -= full          # run -X from the pivot like the slopes
    uvf = tile_fit(bm)
    cut(bm, 1, steps(-1.22, 0, 6))
    if tip:
        cut(bm, 0, steps(-full, 0, 8))
    x = -full + RIB_STEP / 2
    while x < -1e-3:
        pts = [Vector((x, -1.24 + 1.42 * j / 6, ledge_top(-1.24 + 1.42 * j / 6) - 0.05)) for j in range(7)]
        prism(bm, pts, Vector((1, 0, 0)), 0.1, 0.17, TILE, uvf)
        x += RIB_STEP
    paint(bm, [f for f in bm.faces if f.material_index == ORDER.index(WALL)], "red")
    fam = FAMILIES["M"]
    for v in bm.verts:
        x, y, z = v.co
        d = y * (OVERHANG / 1.22) if y < 0 else y
        c = 0.0
        if tip == "far": c = max(0.0, 1.0 - (x + full) / 5.0) ** 2
        elif tip == "near": c = max(0.0, 1.0 + x / 5.0) ** 2
        v.co = Vector((x, d, z + SKIRT_OFF + zf(min(d, 0.0), fam) + EAVE_SLOPE * max(d, 0.0) + LIFT * c * lift_w(d)))
    return bm


def skirt_corner_piece():
    """The square where two pent eaves meet, turned up at the tip. Wall corner at the pivot, eaves toward -X and -Y."""
    bm = load("SM_Bld_Castle_Roof_L_Edge_Corner_Sharp_01", [TILE, WALL])
    for v in bm.verts:
        v.co.y += 5.0
    clip(bm, 0, 0.25, keep_below=True)
    clip(bm, 1, 0.25, keep_below=True)
    uvf = tile_fit(bm)
    cut(bm, 0, steps(-1.22, 0, 6))
    cut(bm, 1, steps(-1.22, 0, 6))
    side = Vector((1, -1, 0)).normalized()
    pts = [Vector((-1.24 * (1 - j / 6) + 0.1 * j / 6, -1.24 * (1 - j / 6) + 0.1 * j / 6, ledge_top(-1.24 * (1 - j / 6) + 0.1 * j / 6) - 0.05)) for j in range(7)]
    prism(bm, pts, side, 0.18, 0.3, TILE, uvf)
    paint(bm, [f for f in bm.faces if f.material_index == ORDER.index(WALL)], "red")
    fam = FAMILIES["M"]
    for v in bm.verts:
        x, y, z = v.co
        dA = x * (OVERHANG / 1.22) if x < 0 else x
        dB = y * (OVERHANG / 1.22) if y < 0 else y
        d = min(dA, dB)
        v.co = Vector((dA, dB, z + SKIRT_OFF + zf(min(d, 0.0), fam) + EAVE_SLOPE * max(d, 0.0) + LIFT * lift_w(d)))
    tipz = BASE + zf(-OVERHANG, fam) + LIFT * lift_w(-OVERHANG)
    extrude_profile(bm, [(-0.05, tipz - 0.1), (0.55, tipz + 0.05), (0.28, tipz + 0.28), (-0.12, tipz + 0.75), (-0.3, tipz + 0.2)], 'y', -0.09, 0.09, "gold")
    ang = math.radians(225)
    for v in bm.verts[-10:]:
        px, py = v.co.x, v.co.y
        v.co = Vector((-OVERHANG + (px * math.cos(ang) - py * math.sin(ang)), -OVERHANG + (px * math.sin(ang) + py * math.cos(ang)), v.co.z))
    return bm


def ridge_piece(length=5.0):
    """The heavy ridge: a stacked tile course under a round roll. Pivot on the ridge line, runs -X."""
    bm = new_bm()
    uvf = lambda x, y: (0.4 * x, 0.06 + 0.1 * y)
    n = 2
    pts = [Vector((-length * j / n, 0, -0.12)) for j in range(n + 1)]
    L = uvl(bm)
    # the course: a trapezoid with a stepped shoulder
    prof = [(-0.36, -0.12), (-0.30, 0.22), (-0.22, 0.22), (-0.2, 0.46), (0.2, 0.46), (0.22, 0.22), (0.30, 0.22), (0.36, -0.12)]
    a = [bm.verts.new((0, p[0], p[1])) for p in prof]
    b = [bm.verts.new((-length, p[0], p[1])) for p in prof]
    faces = [bm.faces.new(list(reversed(a))), bm.faces.new(b)]
    for i in range(len(prof) - 1):
        faces.append(bm.faces.new((a[i], a[i + 1], b[i + 1], b[i])))
    for f in faces:
        f.material_index = 0
        for l in f.loops:
            l[L].uv = uvf(l.vert.co.x, l.vert.co.z)
    # a gilt line under the roll, then the roll
    box(bm, (-length, -0.23, 0.46), (0, 0.23, 0.52), "gold")
    prism(bm, [Vector((0, 0, 0.52)), Vector((-length, 0, 0.52))], Vector((0, 1, 0)), 0.19, 0.24, TILE, uvf)
    return bm


def ridge_end_piece():
    """The ridge-end ornament: a tile shield (onigawara) and a gilt fin curling up over it. Faces +X."""
    bm = new_bm()
    L = uvl(bm)
    shield = [(-0.52, -0.2), (-0.6, 0.35), (-0.42, 0.72), (0.0, 0.92), (0.42, 0.72), (0.6, 0.35), (0.52, -0.2)]
    faces = extrude_profile(bm, shield, 'x', -0.12, 0.14, "gold")
    for f in faces:
        f.material_index = 0
        for l in f.loops:
            l[L].uv = (0.4 * l.vert.co.y, 0.06 + 0.1 * l.vert.co.z)
    # the boss on the shield and its rim
    extrude_profile(bm, [(-0.2, 0.18), (0, 0.5), (0.2, 0.18), (0, 0.02)], 'x', 0.14, 0.2, "gold")
    # the fin: rises behind the shield and curls outward (+X) at the top
    fin = [(-0.75, 0.46), (-0.2, 0.5), (0.0, 0.95), (0.22, 1.5), (0.62, 1.82), (0.2, 1.86), (-0.22, 1.6), (-0.55, 1.05)]
    extrude_profile(bm, fin, 'y', -0.08, 0.08, "gold")
    return bm



# --- shoji walls -------------------------------------------------------------
def paper_box(bm, lo, hi):
    faces = box(bm, lo, hi, "paper")
    for f in faces:
        f.material_index = ORDER.index(PAPER)
    return faces


def lattice(bm, x0, x1, z0, z1, nv, nh, y=0.0):
    """Thin glazing bars over a pane: nv uprights and nh rails, evenly spaced."""
    for i in range(1, nv + 1):
        x = x0 + (x1 - x0) * i / (nv + 1)
        box(bm, (x - 0.025, y - 0.08, z0), (x + 0.025, y + 0.08, z1), "darktimber")
    for j in range(1, nh + 1):
        z = z0 + (z1 - z0) * j / (nh + 1)
        box(bm, (x0, y - 0.07, z - 0.025), (x1, y + 0.07, z + 0.025), "darktimber")


def shoji_piece(width=5.0, height=5.0, door=False):
    """A timber-framed paper wall the size of a castle-kit wall: sill, posts every 1.25 m, a boarded
    dado, the paper screen behind its lattice, a transom, and a lacquer head beam with a gilt line."""
    bm = new_bm()
    W, H = width, height
    bays = max(1, int(round(W / 1.25)))
    bw = W / bays
    low = H < 4.0
    head = 0.22 if low else 0.35
    top = H - head - 0.05                       # underside of the gilt line
    box(bm, (0, -0.25, H - head), (W, 0.25, H), "red")
    box(bm, (0, -0.265, top), (W, 0.265, H - head), "gold")
    open_bays = (1, 2) if door and bays == 4 else ()
    # sill: a full beam, cut down to a threshold under the doorway
    for b in range(bays):
        box(bm, (b * bw, -0.25, 0.0), ((b + 1) * bw, 0.25, 0.08 if b in open_bays else 0.25), "darktimber")
    for k in range(bays + 1):
        x0 = max(0.0, k * bw - 0.1); x1 = min(W, k * bw + 0.1)
        if k in (0, bays):
            x0, x1 = (0.0, 0.12) if k == 0 else (W - 0.12, W)
        if door and k == 2:
            box(bm, (x0, -0.22, 3.85), (x1, 0.22, top), "darktimber")   # only the transom's stub over the doorway
            continue
        box(bm, (x0, -0.22, 0.25 if not (door and k in (1, 3)) else 0.08), (x1, 0.22, top), "darktimber")
    dado = 0.0 if low else 1.25
    rail = top if low else 3.85
    for b in range(bays):
        x0, x1 = b * bw, (b + 1) * bw
        if b in open_bays:
            continue
        if not low:
            box(bm, (x0, -0.1, 0.25), (x1, 0.1, dado), "timber")
            box(bm, (x0, -0.2, dado), (x1, 0.2, dado + 0.15), "darktimber")
        z0 = 0.25 if low else dado + 0.15
        paper_box(bm, (x0, -0.03, z0), (x1, 0.03, rail))
        lattice(bm, x0 + 0.1, x1 - 0.1, z0, rail, 3, 3 if low else 4)
    if not low:
        # the transom runs the whole width, over the doorway too
        box(bm, (0, -0.2, rail), (W, 0.2, rail + 0.15), "darktimber")
        paper_box(bm, (0, -0.03, rail + 0.15), (W, 0.03, top))
        for b in range(bays):
            lattice(bm, b * bw + 0.1, (b + 1) * bw - 0.1, rail + 0.15, top, 5, 1)
    if door and bays == 4:
        # the two leaves, slid open in front of the outer bays
        for x0, x1 in ((0.14, 1.32), (3.68, 4.86)):
            y = 0.17
            paper_box(bm, (x0, y - 0.02, 1.28), (x1, y + 0.02, rail))
            box(bm, (x0, y - 0.02, 0.12), (x1, y + 0.02, 1.28), "timber")
            for fx0, fx1, fz0, fz1 in ((x0, x0 + 0.07, 0.1, rail), (x1 - 0.07, x1, 0.1, rail), (x0, x1, 0.1, 0.2), (x0, x1, rail - 0.08, rail), (x0, x1, 1.2, 1.28)):
                box(bm, (fx0, y - 0.05, fz0), (fx1, y + 0.05, fz1), "darktimber")
            lattice(bm, x0 + 0.07, x1 - 0.07, 1.28, rail - 0.08, 3, 4, y)
    return bm


# --- lacquer pieces ----------------------------------------------------------
def pillar_piece(style):
    """Synty's round stone pillar: its stone base kept, the shaft slimmed, swollen (entasis) and lacquered,
    the capital replaced by a bracket set (style 1) or a plain bearing block (style 2)."""
    bm = load("SM_Bld_Castle_Pillar_Stone_Round_01", [WALL])
    clip(bm, 2, 4.4, keep_below=True)                       # drop the stone capital
    cut(bm, 2, [1.6, 2.9, 3.9])
    shaft = [f for f in bm.faces if f.calc_center_median().z > 0.47]
    paint(bm, shaft, "red")
    for v in bm.verts:
        z = v.co.z
        if z < 0.47:
            continue
        t = (z - 0.48) / (4.4 - 0.48)
        r = 0.78 * (1.0 + 0.10 * math.sin(math.pi * min(1.0, t * 1.35)) - 0.14 * t)
        v.co.x *= r; v.co.y *= r
    top_r = 0.34
    lathe(bm, [(0.47, 0.50), (0.62, 0.50)], "black", cap_top=True)                 # iron shoe
    lathe(bm, [(0.62, 0.46), (0.68, 0.46)], "gold", cap_top=True)
    lathe(bm, [(3.72, top_r + 0.07), (3.80, top_r + 0.07)], "gold", cap_top=True, cap_bottom=True)
    lathe(bm, [(3.80, top_r + 0.05), (4.02, top_r + 0.05)], "black", cap_top=True)
    lathe(bm, [(4.02, top_r + 0.07), (4.10, top_r + 0.07)], "gold", cap_top=True)
    if style == 1:
        box(bm, (-0.42, -0.42, 4.25), (0.42, 0.42, 4.5), "red", taper_bottom=0.72)   # the great block
        box(bm, (-0.44, -0.44, 4.5), (0.44, 0.44, 4.54), "gold")
        for ax in (0, 1):
            # a bracket arm, its ends swept up
            prof = [(-0.8, 4.66), (-0.8, 4.78), (0.8, 4.78), (0.8, 4.66), (0.55, 4.54), (-0.55, 4.54)]
            extrude_profile(bm, prof, 'y' if ax == 0 else 'x', -0.13, 0.13, "red")
            for s in (-1, 1):
                lo = [-0.13, -0.13, 4.78]; hi = [0.13, 0.13, 4.92]
                lo[ax] = s * 0.66 - 0.16; hi[ax] = s * 0.66 + 0.16
                lo[1 - ax] = -0.16; hi[1 - ax] = 0.16
                box(bm, tuple(lo), tuple(hi), "red", taper_bottom=0.75)
                cap_lo = [0, 0, 4.57]; cap_hi = [0, 0, 4.75]
                cap_lo[ax] = s * 0.8 - 0.015 if s > 0 else s * 0.8 - 0.015; cap_hi[ax] = cap_lo[ax] + 0.03
                cap_lo[1 - ax] = -0.1; cap_hi[1 - ax] = 0.1
                box(bm, tuple(cap_lo), tuple(cap_hi), "gold")
        box(bm, (-0.16, -0.16, 4.78), (0.16, 0.16, 4.92), "red", taper_bottom=0.75)
        box(bm, (-0.82, -0.82, 4.92), (0.82, 0.82, 5.0), "black")
        box(bm, (-0.84, -0.84, 4.94), (0.84, 0.84, 4.98), "gold")
    else:
        box(bm, (-0.4, -0.4, 4.25), (0.4, 0.4, 4.62), "red", taper_bottom=0.72)
        box(bm, (-0.42, -0.42, 4.62), (0.42, 0.42, 4.68), "gold")
        box(bm, (-0.5, -0.5, 4.68), (0.5, 0.5, 4.9), "red", taper_bottom=0.8)
        box(bm, (-0.56, -0.56, 4.9), (0.56, 0.56, 4.97), "darkred")
        box(bm, (-0.58, -0.58, 4.97), (0.58, 0.58, 5.0), "gold")
    lathe(bm, [(4.10, top_r), (4.26, top_r)], "red")
    return bm


def beam_piece(length=5.0):
    """A lacquered tie beam with gilt shoes and a black band: lintels, arcades, gate ties. Runs -X, centred on Y."""
    bm = new_bm()
    box(bm, (-length, -0.16, 0.0), (0, 0.16, 0.4), "red")
    for x in (-length, -0.12):
        box(bm, (x - 0.01, -0.18, -0.02), (x + 0.13, 0.18, 0.42), "gold")
    box(bm, (-length / 2 - 0.2, -0.175, -0.015), (-length / 2 + 0.2, 0.175, 0.415), "black")
    box(bm, (-length / 2 - 0.06, -0.185, 0.08), (-length / 2 + 0.06, 0.185, 0.32), "gold")
    return bm


def kasagi_piece(length=7.5):
    """The top beam of a gate: ends swept up, a black cap on a red beam. Centred on the pivot, runs along X."""
    bm = new_bm()
    n = 16
    h = length / 2
    def z_at(x):
        return 0.5 * (abs(x) / h) ** 2.6
    for lo, hi, w0, w1, colour in ((0.0, 0.38, 0.17, 0.2, "red"), (0.38, 0.56, 0.27, 0.3, "black"), (0.56, 0.6, 0.2, 0.2, "gold")):
        rings = []
        for j in range(n + 1):
            x = -h + length * j / n
            z = z_at(x)
            rings.append([bm.verts.new((x, -w0, z + lo)), bm.verts.new((x, -w1, z + hi)), bm.verts.new((x, w1, z + hi)), bm.verts.new((x, w0, z + lo))])
        faces = [bm.faces.new(list(reversed(rings[0]))), bm.faces.new(rings[-1])]
        for a, b in zip(rings, rings[1:]):
            for i in range(4):
                j = (i + 1) % 4
                faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
        paint(bm, faces, colour)
    for s in (-1, 1):
        x = s * h
        box(bm, (min(x, x + s * 0.03), -0.19, z_at(x) - 0.02), (max(x, x + s * 0.03), 0.19, z_at(x) + 0.4), "gold")
    return bm


# --- export --------------------------------------------------------------------
MANIFEST = {}
MATS = {}
BUILT = []


def material(name, img=None, colour=(0.8, 0.8, 0.8, 1)):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = colour
    if img:
        nt = m.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
        tex.interpolation = 'Closest'
        nt.links.new(tex.outputs[0], nt.nodes["Principled BSDF"].inputs[0])
        nt.nodes.active = tex
    return m


def export(bm, name):
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    used = sorted(set(f.material_index for f in bm.faces))
    for f in bm.faces:
        f.material_index = used.index(f.material_index)
    # Unity numbers submeshes in the order the polygons first use them, not in slot order:
    # keep the two the same, or the prefab's materials land on the wrong faces.
    bm.faces.sort(key=lambda f: f.material_index)
    bm.faces.index_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for i in used:
        me.materials.append(MATS[ORDER[i]])
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(38))
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF', use_mesh_modifiers=False,
                             bake_anim=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    MANIFEST[name] = [ORDER[i] for i in used]
    ws = [v.co for v in me.vertices]
    print(f"KIT {name}: {len(me.polygons)} faces, slots {MANIFEST[name]}, "
          f"x {min(v.x for v in ws):.2f}..{max(v.x for v in ws):.2f} y {min(v.y for v in ws):.2f}..{max(v.y for v in ws):.2f} z {min(v.z for v in ws):.2f}..{max(v.z for v in ws):.2f}")
    BUILT.append(obj)
    return obj


def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    pal = write_palette()
    def img(path):
        return bpy.data.images.load(path) if os.path.exists(path) else None
    MATS[TILE] = material(TILE, img(os.path.join(ART, "Roof_Tile_Player.png")), (0.2, 0.3, 0.6, 1))
    MATS[WALL] = material(WALL, img(os.path.join(ART, "Castle_Wall_White.png")), (0.9, 0.9, 0.85, 1))
    MATS[ATLAS] = material(ATLAS, None, (0.4, 0.3, 0.2, 1))
    MATS[LAC] = material(LAC, pal, (0.7, 0.1, 0.07, 1))
    MATS[PAPER] = material(PAPER, pal, (0.97, 0.92, 0.8, 1))
    for k in ("M", "S"):
        export(slope_piece(k), f"SM_Bld_Kawara_{k}_01")
        export(slope_piece(k, 2.5), f"SM_Bld_Kawara_{k}_Half_01")
        export(slope_piece(k, tip="far"), f"SM_Bld_Kawara_{k}_Tip_Far_01")
        export(slope_piece(k, tip="near"), f"SM_Bld_Kawara_{k}_Tip_Near_01")
        export(hafu_piece(k), f"SM_Bld_Kawara_{k}_Hafu_01")
        export(gable_piece(k), f"SM_Bld_Kawara_{k}_Gable_01")
        export(hip_corner_piece(k), f"SM_Bld_Kawara_{k}_Corner_01")
    export(skirt_piece(5.0), "SM_Bld_Kawara_Eave_01")
    export(skirt_piece(2.5), "SM_Bld_Kawara_Eave_Half_01")
    export(skirt_piece(5.0, "far"), "SM_Bld_Kawara_Eave_Tip_Far_01")
    export(skirt_piece(5.0, "near"), "SM_Bld_Kawara_Eave_Tip_Near_01")
    export(skirt_corner_piece(), "SM_Bld_Kawara_Eave_Corner_01")
    export(ridge_piece(5.0), "SM_Bld_Kawara_Ridge_01")
    export(ridge_piece(2.5), "SM_Bld_Kawara_Ridge_Half_01")
    export(ridge_end_piece(), "SM_Bld_Kawara_Ridge_End_01")
    export(shoji_piece(5.0, 5.0), "SM_Bld_Shoji_Wall_01")
    export(shoji_piece(2.5, 5.0), "SM_Bld_Shoji_Wall_Half_01")
    export(shoji_piece(1.25, 5.0), "SM_Bld_Shoji_Wall_Quarter_01")
    export(shoji_piece(5.0, 2.5), "SM_Bld_Shoji_Wall_Low_01")
    export(shoji_piece(5.0, 5.0, door=True), "SM_Bld_Shoji_Wall_Door_01")
    export(pillar_piece(1), "SM_Bld_Lacquer_Pillar_Round_01")
    export(pillar_piece(2), "SM_Bld_Lacquer_Pillar_Round_02")
    export(beam_piece(5.0), "SM_Bld_Lacquer_Beam_01")
    export(beam_piece(2.5), "SM_Bld_Lacquer_Beam_Half_01")
    export(kasagi_piece(7.5), "SM_Bld_Lacquer_Gate_Beam_01")
    with open(os.path.join(OUT, "kit_manifest.json"), "w") as f:
        json.dump(MANIFEST, f, indent=1)


def preview(stem):
    """Lay the pieces out, and stand a small hall beside them, to check the joins."""
    sc = bpy.context.scene
    cols = 6
    for i, o in enumerate(BUILT):
        o.location = ((i % cols) * 9.0, -(i // cols) * 9.0, 0)
    def inst(name, loc, rot=0.0, sx=1.0):
        src = bpy.data.objects[name]
        o = bpy.data.objects.new(name + "_i", src.data)
        sc.collection.objects.link(o)
        o.location = loc; o.rotation_euler = (0, 0, math.radians(rot)); o.scale = (sx, 1, 1)
        return o
    # a 10 x 7.5 hall: walls as a grey box, its roof from the M pieces
    ox, oy = 70.0, -10.0
    bm = new_bm(); box(bm, (-10, 0, -5), (0, 7.5, 0), "white")
    me = bpy.data.meshes.new("hall"); bm.to_mesh(me); me.materials.append(MATS[LAC])
    hall = bpy.data.objects.new("hall", me); sc.collection.objects.link(hall); hall.location = (ox, oy, 0)
    fam = FAMILIES["M"]; top = BASE + fam["H"]
    K = 0.625   # roof past each gable wall
    # front slope (eave toward -Y): near tip at +X end, far tip at -X end
    inst("SM_Bld_Kawara_M_Tip_Near_01", (ox + K, oy, 0), 0, 1.125)
    inst("SM_Bld_Kawara_M_Tip_Far_01", (ox + K - 5.625, oy, 0), 0, 1.125)
    inst("SM_Bld_Kawara_M_Tip_Near_01", (ox - 10 - K, oy + 7.5, 0), 180, 1.125)
    inst("SM_Bld_Kawara_M_Tip_Far_01", (ox - 10 - K + 5.625, oy + 7.5, 0), 180, 1.125)
    for gx in (ox + K - 0.3, ox - 10 - K + 0.3):
        inst("SM_Bld_Kawara_M_Hafu_01", (gx, oy, 0), 0)
        inst("SM_Bld_Kawara_M_Hafu_01", (gx, oy + 7.5, 0), 180)
    for gx in (ox, ox - 10):
        inst("SM_Bld_Kawara_M_Gable_01", (gx, oy, 0), 0)
        inst("SM_Bld_Kawara_M_Gable_01", (gx, oy + 7.5, 0), 180)
    inst("SM_Bld_Kawara_Ridge_01", (ox + K, oy + 3.75, top), 0, (10 + 2 * K) / 5.0)
    inst("SM_Bld_Kawara_Ridge_End_01", (ox + K - 0.2, oy + 3.75, top), 0)
    inst("SM_Bld_Kawara_Ridge_End_01", (ox - 10 - K + 0.2, oy + 3.75, top), 180)
    for px in (ox, ox - 5, ox - 10):
        inst("SM_Bld_Lacquer_Pillar_Round_01", (px, oy - 0.2, -5), 0)
    # a storey belt of pent eaves below, with corners
    z = -2.6
    inst("SM_Bld_Kawara_Eave_Tip_Near_01", (ox, oy, z), 0)
    inst("SM_Bld_Kawara_Eave_Tip_Far_01", (ox - 5, oy, z), 0)
    inst("SM_Bld_Kawara_Eave_Corner_01", (ox - 10, oy, z), 0)
    inst("SM_Bld_Kawara_Eave_Corner_01", (ox, oy, z), 90)
    # a hipped pavilion from the S corners
    px, py = 70.0, -32.0
    for r in (0, 90, 180, 270):
        a = math.radians(r)   # four corners turned about their shared apex
        inst("SM_Bld_Kawara_S_Corner_01", (px + 2.5 * math.sin(a), py - 2.5 * math.cos(a), 0), r)
    inst("SM_Bld_Lacquer_Gate_Beam_01", (86.0, -10.0, 0), 0)
    inst("SM_Bld_Lacquer_Pillar_Round_02", (84.0, -10.0, -5), 0)
    inst("SM_Bld_Lacquer_Pillar_Round_02", (88.0, -10.0, -5), 0)

    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_cavity = True; sh.show_shadows = True
    sc.world = bpy.data.worlds.new("w")
    sc.render.film_transparent = False
    def shot(tag, target, dist, yaw, el, scale, res=(1800, 1100)):
        cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = scale
        co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co)
        e = math.radians(el); a = math.radians(yaw)
        co.location = (target[0] + dist * math.cos(e) * math.sin(a), target[1] - dist * math.cos(e) * math.cos(a), target[2] + dist * math.sin(e))
        co.rotation_euler = (math.radians(90) - e, 0, a)
        sc.camera = co
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = f"{stem}_{tag}.png"
        bpy.ops.render.render(write_still=True)
    shot("kit", (22.5, -13, 1), 120, -30, 35, 60, (2200, 1500))
    shot("hall_a", (65, -6, 0), 80, -35, 25, 24)
    shot("hall_b", (65, -6, 0), 80, 60, 20, 24)
    shot("hall_c", (65, -6, 0), 80, 200, 30, 24)
    shot("hut", (70, -32, 1), 60, -30, 30, 14, (1200, 900))
    shot("gate", (86, -10, -2), 60, -20, 15, 12, (1200, 900))
    shot("eave", (65, -11, -3), 60, -25, 28, 9, (1400, 900))
    for i, n in enumerate(("SM_Bld_Shoji_Wall_01", "SM_Bld_Shoji_Wall_Door_01", "SM_Bld_Shoji_Wall_Half_01", "SM_Bld_Shoji_Wall_Quarter_01", "SM_Bld_Shoji_Wall_Low_01")):
        inst(n, (100.0 + i * 5.6 if i < 2 else 100.0 + [11.2, 14.3, 16.2][i - 2], -10.0, 0), 0)
    shot("shoji", (110.5, -10, 2.5), 60, -18, 12, 24, (2000, 700))
    shot("pillars", (4.5 * 9 + 0.0, -27, 2.5), 60, -25, 15, 16, (1400, 900))


build_all()
if PREVIEW:
    preview(PREVIEW)
