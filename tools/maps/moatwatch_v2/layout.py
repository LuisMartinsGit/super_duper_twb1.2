# layout.py — MOATWATCH v2, the four-player moated-corners map, 256 x 256 m.
#
# Run:  python tools/maps/moatwatch_v2/layout.py [--export] [--table]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout
# (MapBuilderMoatwatchV2). Shared machinery: tools/maps/layoutkit.py.
#
# THE V2 MAPS (2026-11-14): units were halved, the 2 m build grid and every
# building footprint were not, so the first maps were too large. v2 keeps each
# map's concept on half the ground and brings the players closer. It is a new
# layout, not the old one scaled: a building does not shrink, so every
# territory is drawn big enough for its nodes, a Farm with its whole field and
# Houses, and the home for a full base (the kit's building-room probe, which
# every territory passes against six node draws).
#
# Moatwatch's concept: each corner is a KEEP behind a moat (deep water)
# crossed by two waded fords that face the middle, each split between the keep
# and the ground across; the middle is open and rich, with a raised Sanctum.
# Mirrored on both axes; nothing that holds nodes straddles an axis
# (TerritoryResources mirrors the node layout to every seat only then).
#
# v2 on half the ground: the moat is an ELLIPSE along the diagonal, so the
# keep holds the whole home but stops well short of the map edges, and the
# ground outside is two territories a quadrant, round one shared middle:
#   two ARMS along the map edges, each meeting the neighbour's at the axis:
#     the contested ground between two keeps, IRON-rich toward one neighbour
#     and CURSED toward the other (pinned, so a match never moves it; once
#     cleansed it is rich veilstone). A round moat left them a thin strip with
#     no room for a Farm field, and a territory with three or four random
#     nodes needs ~3,500-4,000 m2 of compact ground to keep one every draw;
#   the Sanctum BARROW, ONE raised mound in the centre where the tor was,
#     a ramp up from each quadrant: the hill all four keeps fight for (four
#     quadrant barrows took the ground the arms need).
# The keep holds the home alone: a garth beside it left neither room enough.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Moatwatch v2", 256, 256, group_mirror_both(), HERE, seed=4, warp=(4.0, 1.5))
P.ramp_half = 5.0                # 10 m ramps: a choke for half-size units

C = (-128.0, -128.0)             # the canonical corner
U0, A, B = 49.0, 66.0, 37.0      # the keep: an ellipse centred U0 along the diagonal, semi-axes A x B
MOAT_W = 9.0                     # moat width
FORDS = [-0.55, 0.55]            # fords at these v / B across the ellipse's far end

def diag(u, v):
    """corner-relative diagonal coordinates (u along, v across) -> world."""
    return C[0] + (u + v) / np.sqrt(2), C[1] + (u - v) / np.sqrt(2)

def keep_shape(grow=0.0, rough=1.0):
    def f(X, Z, E):
        p, q = X - C[0], Z - C[1]
        u, v = (p + q) / np.sqrt(2), (p - q) / np.sqrt(2)
        return ((u - U0) / (A + grow)) ** 2 + (v / (B + grow)) ** 2 + E * rough / (B + grow) < 1
    return f

def on_keep_rim(t, grow):
    """the point on the ellipse grown by grow at across-parameter t (-1..1)."""
    v = t * (B + grow)
    u = U0 + (A + grow) * np.sqrt(max(0.0, 1 - t * t))
    return diag(u, v)

P.terrain_paint("land", everywhere())
quad = lambda X, Z, E: (X < 0) & (Z < 0)   # a shape that runs on forever is held to this quadrant
moat = AND(quad, keep_shape(MOAT_W), NOT(keep_shape(0.0)))
P.terrain_paint("water", moat)

# the keep: the home inside the moat
start = diag(U0 + 6, 0.0)
P.region_paint("Keep", 0, "Start", AND(quad, keep_shape(1.0)), start=start)
# the fords: waded water, no territory of their own; each is split between
# the keep and the arm across, so claims cross where armies wade
for t in FORDS:
    a, b = on_keep_rim(t, -5.0), on_keep_rim(t, MOAT_W + 5.0)
    P.wade_paint(AND(moat, lane([a, b], 5, wob=0.12)))

outside = AND(quad, NOT(keep_shape(MOAT_W + 1.0)))
# the Sanctum: ONE raised barrow in the centre, shared by all four seats,
# a ramp up from each quadrant (a node-less territory may sit across the
# axes: TerritoryResources.MirrorPartners skips it)
P.region_paint("Barrow", 1, "Sanctum", disc(0, 0, 23, rough=1.2), shared=True)
P.ramp("Barrow", None, -17, -17)
# the arms along the edges, split on the diagonal: iron toward one
# neighbour, the curse toward the other (pinned: it never moves)
P.region_paint("ArmS", 0, "IronRich", AND(outside, lambda X, Z, E: X - Z + E * 1.5 > 10.0), only_free=True)
P.region_paint("ArmW", 0, "VeilstoneRich", AND(outside, lambda X, Z, E: X - Z + E * 1.5 <= 10.0), only_free=True)
P.pin_curse = True
P.forest_banned |= {"Barrow", "ArmS", "ArmW"}   # half-size ground: a Farm field needs it more than a copse

SEAT = ["Ashmere", "Brackwater", "Coldfen", "Dunmoor"]   # SW, SE, NW, NE (group order)
def display(rec):
    s = SEAT[rec["k"]] if rec["k"] is not None else ""
    c, k = rec["canon"], rec["k"]
    if c == "Barrow": return "The Moat Barrow"
    return {"Keep": f"{s} Keep",
            "ArmS": f"{s} {'South' if k in (0, 1) else 'North'} Reach",
            "ArmW": f"{s} {'West' if k in (0, 2) else 'East'} Reach"}[c]

# trees and rocks (2026-11-14 reskin): copses along the territory borders,
# a deeper rock scatter into the mountains; the room probe runs after them
P.hedgerows = dict(width=2.0, cover=0.25)   # the arms are all the open ground there is: a thin hedge
P.fringe_depth = 12.0
P.soft_ramps = True
P.bank_trees = dict(width=2.5, cover=0.6)   # the fen: trees along the moats (no mountains here)

P.finish("Moatwatch_v2", display, notes=[
    "v2 (2026-11-14): half the ground, players closer.",
    "Each keep: the home inside an elliptical moat,",
    "two waded fords toward the middle, split",
    "between keep and the ground across.",
    "Outside: two edge arms meeting the neighbours'",
    "(iron-rich / cursed, pinned). Centre: ONE",
    "raised Sanctum barrow, a ramp from each side.",
])
