# layout.py — HIGHFOLD v2, the eight-player mountains-and-fields map, 384 x 384 m.
#
# Run:  python tools/maps/highfold_v2/layout.py [--export] [--table]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout.
# Shared machinery: tools/maps/layoutkit.py.
#
# THE V2 MAPS (2026-11-14): units were halved, the 2 m build grid and every
# building footprint were not, so the first maps were too large. v2 keeps each
# map's concept on half the ground and brings the players closer. It is a new
# layout, not the old one scaled: a building does not shrink, so every
# territory is drawn big enough for its nodes, a Farm with its whole field and
# Houses, and the home for a full base (the kit's building-room probe, which
# every territory passes against six node draws).
#
# Highfold's concept, kept: mirrored on both axes (the south-west quadrant is
# authored), two kinds of seat, four of each.
#   MOUNTAIN seats (1-4): a valley (the Crag) in each corner massif, three
#     levels up, with a Ledge below it whose ONE ramp is the only way out to
#     the fields. Safe, poor (supply, a little iron on the Ledge), far from
#     the centre.
#   FIELD seats (5-8): the Meadow, open ground with no cliffs and no chokes,
#     beside the richest ground on the map (the curse, a Sanctum barrow round
#     the centre pond).
#   Edge hollows between the massifs, one ramp each, hold iron.
#
# What v2 changed, and why. A quadrant is a quarter of the old one (36,900
# m2) while a territory with nodes still needs ~4,000-4,500 m2 of COMPACT
# ground and a home ~6,500: seven such territories only fit as a near-exact
# tiling, so the quadrant is laid out as blocks (two columns) rather than the
# old massif disc + Voronoi fields. Every "keep it round and on the diagonal"
# version left the fields as 40 m strips along the axes, and a strip fails
# the Farm-field probe however long it is.
#   - west column, the massif: the Crag in the corner, the Ledge above it up
#     the west edge (not out along the diagonal), and above that the West
#     Field where the Ledge's one ramp comes down. A mountain shoulder runs
#     up the column's east side, so the Crag and the Ledge border the Hollow
#     and the Meadow only through rock. The Ledge is Normal + iron, taking
#     the iron of the old Cairn.
#   - east column, the fields: the Hollow on the south edge, the Meadow
#     above it, and the curse round the barrow by the pond. The field seat
#     sits beside the x axis, not on the diagonal; a mountain spur along the
#     axis keeps it from the neighbouring Meadow (the two meet round the
#     spur's north end, through the curse) and keeps the two hollows apart.
#   - the two high pockets (Eyrie, Cairn) are cut: the massif holds the Crag
#     and the Ledge and nothing more.
#   - ONE edge hollow a quadrant, on the south / north edges (iron rich, its
#     ramp up from the Meadow); the west / east edges hold the Ledges and the
#     West Fields. The old west hollow's veilstone moved to the West Field.
#   - the 18 field territories a quadrant become TWO: the West Field (Normal
#     + veilstone, where the Ledge comes down) and the East Field (Veilstone
#     rich, the curse, between the Meadow and the barrow). Pinned
#     (P.pin_curse): with this few territories a re-deal could land the curse
#     on a Ledge, a hollow or the West Field.
#   - one barrow a quadrant still, two ramps up from the curse, round a pond.
#   - no field tors and no forest on any territory (half-size ground: a Farm
#     field needs it more than a copse).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Highfold v2", 384, 384, group_mirror_both(), HERE, seed=8, warp=(4.0, 1.5))
P.ramp_half = 5.0                # 10 m ramps: a choke for half-size units

C = -192.0                       # the canonical corner is (C, C)
quad = lambda X, Z, E: (X < 0) & (Z < 0)   # a shape that runs on forever is held to this quadrant

def W(a, b):
    """corner-relative metres (a east, b north of the south-west corner) -> world."""
    return C + a, C + b

def along(X, Z):
    """distance along the diagonal from the corner."""
    return (X + Z - 2 * C) / np.sqrt(2.0)

def rect(a0, a1, b0, b1, rough=4.5):
    """a rectangle in corner-relative metres, its edges roughened by the noise."""
    x0, x1, z0, z1 = C + a0, C + a1, C + b0, C + b1
    return lambda X, Z, E: ((X - x0 + E * rough > 0) & (x1 - X + E * rough > 0) &
                            (Z - z0 - E * rough > 0) & (z1 - Z - E * rough > 0))

P.terrain_paint("land", everywhere())

# ── the mountain seat: the Crag in the corner, the Ledge above it up the west
# edge; the Ledge's one ramp is the only way out
P.region_paint("Crag", 3, "Start", rect(0, 96, 0, 84), start=W(46, 42))
P.region_paint("Ledge", 2, "NormalIron", rect(0, 96, 84, 140))
P.ramp("Crag", "Ledge", *W(40, 84))
P.ramp("Ledge", "FieldW", *W(62, 140))            # the only way down, to the West Field
# ── the west field: veilstone, where the Ledge comes down
P.region_paint("FieldW", 0, "NormalVeilstone", rect(0, 88, 140, 192))
# ── the edge hollow on the south edge, between this massif and the next
P.region_paint("Hollow", 1, "IronRich", rect(96, 192, 0, 48))
P.ramp("Hollow", None, *W(150, 48))
# ── the field seat: the Meadow, open ground between the hollow and the curse
P.region_paint("Meadow", 0, "Start", rect(96, 192, 48, 124), start=W(136, 86))
# ── the curse field round the Sanctum barrow by the pond
P.region_paint("Barrow", 1, "Sanctum", blob(*W(166, 166), 22, 21, 45, 0.06))
P.region_paint("FieldE", 0, "VeilstoneRich", rect(88, 192, 124, 192), only_free=True)
P.ramp("Barrow", "FieldE", *W(146, 168))
P.ramp("Barrow", "FieldE", *W(168, 146))
# mountains: a spur on the x axis keeps the two hollows and the two Meadows
# apart (the field seats meet round its north end), the rounded corner, and
# a thin rim along the map edge
P.terrain_paint("mountain", AND(quad, OR(
    lambda X, Z, E: (X + E * 1.0 > -7) & (Z - C < 127),
    lambda X, Z, E: along(X, Z) + E * 1.5 < 22,
    lambda X, Z, E: (np.abs(X - (C + 92)) + E * 1.0 < 4) & (Z - C < 136),
    lambda X, Z, E: (X - C < 4) | (Z - C < 4))))
P.terrain_paint("water", disc(0, 0, 12, rough=1.0))     # the centre pond
P.pin_curse = True
P.forest_banned |= {"Ledge", "Hollow", "Barrow", "FieldW", "FieldE"}

SEAT_M = ["Grimscar", "Hawkfell", "Ironcrag", "Wolfhowe"]      # SW, SE, NW, NE
SEAT_F = ["Applegarth", "Barleyfold", "Cressmead", "Dewfield"]
def display(rec):
    k = rec["k"]
    c = rec["canon"]
    m, f = SEAT_M[k], SEAT_F[k]
    return {"Crag": f"{m} Crag", "Ledge": f"{m} Ledge", "Meadow": f"{f}",
            "Hollow": f"{m} Delve", "Barrow": f"{f} Barrow",
            "FieldW": f"{m} Lea", "FieldE": f"{f} Wold"}[c]

# trees and rocks (2026-11-14 reskin): copses along the territory borders,
# a deeper rock scatter into the mountains; the room probe runs after them
P.hedgerows = dict(width=2.0, cover=0.4)
P.fringe_depth = 12.0
P.soft_ramps = True

P.finish("Highfold_v2", display, notes=[
    "v2 (2026-11-14): half the ground, players closer.",
    "Seats 1-4: corner Crag, a Ledge above it up the",
    "edge, ONE ramp from the Ledge to the West Field.",
    "Seats 5-8: the open Meadow, a hollow below, the",
    "curse (pinned) above, a spur to the next Meadow.",
    "Edge hollows N/S: one ramp, iron rich.",
    "Barrow = raised Sanctum. Centre = a pond.",
])
