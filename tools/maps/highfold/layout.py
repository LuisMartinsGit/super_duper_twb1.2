# layout.py — HIGHFOLD, the eight-player mountains-and-fields map, 768 x 768 m.
#
# Run:  python tools/maps/highfold/layout.py [--export]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout (no Unity
# builder exists yet). Shared machinery: tools/maps/layoutkit.py.
#
# Mirrored across BOTH axes (the south-west quadrant is authored), so
# TerritoryResources.MirrorPartners lays the same nodes for every seat of a
# kind. Nothing straddles an axis; the centre is a pond.
#
# Two kinds of seat, four of each:
#   MOUNTAIN seats (1-4) sit in a valley in each corner massif: the home, two
#     small high pockets, and a ledge, all cliff-bound, with ONE ramp down to
#     the fields. Easy to hold, poor (supply, a little iron), far from the
#     centre.
#   FIELD seats (5-8) sit in the open fields: no cliffs, no chokes, and the
#     richest ground on the map (veilstone, iron-rich, the curse, a Sanctum
#     barrow) all around them and their neighbours.
# Between the massifs, edge hollows (one ramp each) hold iron and veilstone
# for whoever reaches them first.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Highfold", 768, 768, group_mirror_both(), HERE, seed=8)

C = (-384.0, -384.0)
MASSIF = 252.0               # massif radius from the corner

def corner_pt(deg, r):
    a = np.radians(deg)
    return C[0] + r * np.cos(a), C[1] + r * np.sin(a)

P.terrain_paint("land", everywhere())
# the massif, and a mountain rim along every map edge
P.terrain_paint("mountain", OR(disc(*C, MASSIF, rough=10),
                               lambda X, Z, E: (X + E * 4 < -384 + 16) | (Z + E * 4 < -384 + 16)))
P.terrain_paint("water", disc(0, 0, 42, rough=4))   # the centre pond (~50 m: the warp squeezes the middle)
P.terrain_paint("mountain", blob(-205, -150, 9, 7, 30, 0.3))        # tors in the fields
P.terrain_paint("mountain", blob(-150, -205, 7, 9, -30, 0.3))
P.terrain_paint("mountain", blob(-40, -110, 8, 6, 0, 0.3))
P.terrain_paint("mountain", blob(-110, -40, 6, 8, 0, 0.3))

# ── the mountain seat ──
crag_start = corner_pt(45, 112)
P.region_paint("Crag", 3, "Start", blob(*corner_pt(45, 118), 62, 54, 45, 0.08), start=crag_start)
P.region_paint("Eyrie", 4, "Normal", blob(*corner_pt(13, 148), 27, 42, 13, 0.12))
P.region_paint("Cairn", 4, "NormalIron", blob(*corner_pt(77, 148), 27, 42, 77, 0.12))
P.region_paint("Ledge", 2, "Normal", blob(*corner_pt(45, 214), 52, 44, 45, 0.10))
P.ramp("Crag", "Eyrie", *corner_pt(20, 128))
P.ramp("Crag", "Cairn", *corner_pt(70, 128))
P.ramp("Crag", "Ledge", *corner_pt(45, 176))
P.ramp("Ledge", None, *corner_pt(45, 266))        # the only way down

# ── the field seat ──
field_start = (-150.0, -138.0)
P.region_paint("Meadow", 0, "Start", blob(-152, -140, 56, 52, 40, 0.10), start=field_start)

# ── edge hollows between the massifs, and the Sanctum barrow ──
P.region_paint("HollowS", 1, "IronRich", blob(-62, -352, 42, 24, 0, 0.12))
P.region_paint("HollowW", 1, "NormalVeilstone", blob(-352, -62, 24, 42, 0, 0.12))
P.ramp("HollowS", None, -62, -322)
P.ramp("HollowW", None, -322, -62)
P.region_paint("Barrow", 1, "Sanctum", blob(-78, -66, 24, 18, 40, 0.15))
P.ramp("Barrow", None, -60, -50)
P.ramp("Barrow", None, -96, -82)

# ── the fields: 18 territories a quadrant, richest toward the centre ──
def types(seeds):
    order = np.argsort([np.hypot(x, z) for x, z in seeds])
    plan = ["VeilstoneRich", "IronRich", "NormalVeilstone", "IronRich", "NormalVeilstone", "NormalVeilstone",
            "NormalVeilstone", "Normal", "NormalIron", "NormalVeilstone", "NormalIron", "Normal",
            "NormalIron", "Empty", "Normal", "NormalIron", "Normal", "NormalIron"]
    out = [None] * len(seeds)
    for rank, i in enumerate(order): out[i] = (plan[rank], None)
    return out
P.split("Field", 0, everywhere(), 18, types)
P.forest_banned |= {"Barrow", "HollowS", "HollowW"}
P.forest_cover = 1.2

SEAT_M = ["Grimscar", "Hawkfell", "Ironcrag", "Wolfhowe"]      # SW, SE, NW, NE
SEAT_F = ["Applegarth", "Barleyfold", "Cressmead", "Dewfield"]
WORDS = ["Lea", "Acre", "Wold", "Ing", "Croft", "Furlong", "Hide", "Common", "Holme", "Thwaite",
         "Mead", "Strand", "Riggs", "Brake", "Leys", "Garth", "Nook", "Flatt"]
def display(rec):
    k = rec["k"]
    c = rec["canon"]
    m, f = SEAT_M[k], SEAT_F[k]
    return {"Crag": f"{m} Crag", "Eyrie": f"{m} Eyrie", "Cairn": f"{m} Cairn", "Ledge": f"{m} Ledge",
            "Meadow": f"{f}", "HollowS": f"{m} Delve", "HollowW": f"{m} Hollow",
            "Barrow": f"{f} Barrow"}.get(c) or f"{f} {WORDS[int(c[5:]) % len(WORDS)]}"

P.finish("Highfold", display, notes=[
    "Seats 1-4: mountain valleys - home, two high",
    "pockets and a ledge, ONE ramp to the fields.",
    "Seats 5-8: open fields, no chokes, the rich",
    "centre (curse, iron, veilstone) around them.",
    "Edge hollows: one ramp, iron / veilstone.",
    "Barrow = raised Sanctum. Centre = a pond.",
])
