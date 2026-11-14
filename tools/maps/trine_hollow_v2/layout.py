# layout.py — TRINE HOLLOW v2, the three-player radial map, 320 x 320 m.
#
# Run:  python tools/maps/trine_hollow_v2/layout.py [--export] [--table]
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
# Trine Hollow's concept, kept: three-fold ROTATIONAL symmetry (the wedge
# round seat 1, north, is authored and turned 120 and 240 degrees; node spots
# are random per territory, the TYPES are the same for every seat), and a
# bowl stepping down to the middle:
#   rim (level 2)      the home, with a high pocket (level 3) beside it
#   terrace (level 1)  a shelf below the rim, cut from the neighbour's ground
#                      by a mountain spur
#   saddle (level 2)   a raised Sanctum on each spur, ramps to the ground
#                      either side: the back way between neighbours that
#                      avoids the basin
#   basin (level 0)    the open, rich floor, with a cursed mesa (level 1,
#                      one ramp a seat) in the very centre, pinned
#
# What v2 changed, and why (a 120-degree wedge of a 160 m disc is ~25,000 m2,
# and a territory with three or four random nodes needs ~4,000 m2 of COMPACT
# ground to keep a Farm field every draw — thin rings fail):
#   - eleven territories a seat became five: home, ONE high pocket, ONE
#     terrace, ONE basin territory, the saddle. The original's 40-90 m bands
#     shrink to 30-40 m at half size, too thin to hold a Farm beside random
#     nodes, so each band is one territory a seat and the bands are deeper;
#   - the home reaches down from the rim to the basin's edge on the seat's
#     axis (a compact home needs the depth), so the terrace no longer runs
#     right round under it: it is the shelf on the home's east flank, and the
#     high pocket the west flank. Each spur therefore has one seat's terrace
#     on one side and the neighbour's pocket on the other, and the saddle on
#     it is the back way between those two;
#   - the second high pocket (Spire) and the Empty terrace are gone, and the
#     basin tors are dropped (no room for an unclaimable rock in the basin);
#   - the mesa is a 36 m disc (it was 30 m on a 640 m map): Veilstone rich
#     holds four nodes and must keep a Farm field itself;
#   - types per seat: home Start, pocket Normal (safe, poor), terrace Normal +
#     veilstone, basin Iron rich, saddle Sanctum; the Heart Veilstone rich.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Trine Hollow v2", 320, 320, group_rotation(3, 90.0), HERE, seed=3, warp=(4.0, 1.5))
P.ramp_half = 5.0                # 10 m ramps: a choke for half-size units

def pol(deg, r):
    a = np.radians(deg)
    return r * np.cos(a), r * np.sin(a)

HEART, BASIN, RIM = 36.0, 84.0, 157.0
SPUR = 30.0                      # the spur between seat 1 and seat 3 (wedge 30..150)
wedge = sector(0, 0, SPUR, SPUR + 120.0)
P.terrain_paint("land", everywhere())
P.terrain_paint("mountain", NOT(disc(0, 0, RIM, rough=3)))

# ── the cursed mesa in the centre (one territory, all three seats') ──
P.region_paint("Heart", 1, "VeilstoneRich", disc(0, 0, HEART, rough=2), shared=True)
P.ramp("Heart", None, *pol(90, HEART))

# ── the seat, from the rim down to the basin's edge ──
P.region_paint("Throne", 2, "Start", blob(-3, 115, 59, 43, 0, 0.06), start=(-4.0, 115.0))
# the high pocket: the home's west flank, rim to basin
P.region_paint("Aerie", 3, "Normal", AND(wedge, sector(0, 0, 90.0, 180.0), disc(0, 0, RIM, rough=3),
                                         NOT(disc(0, 0, BASIN, rough=3))), only_free=True)
# the terrace: the home's east flank
P.region_paint("Shelf", 1, "NormalVeilstone", AND(wedge, sector(0, 0, 0.0, 90.0), disc(0, 0, RIM, rough=3),
                                                  NOT(disc(0, 0, BASIN, rough=3))), only_free=True)
# the basin: the open floor round the mesa
P.region_paint("Floor", 0, "IronRich", AND(wedge, disc(0, 0, BASIN + 2, rough=3)), only_free=True)

# ── the spur between seats, with the raised Sanctum saddle on it ──
P.terrain_paint("mountain", lane([pol(SPUR, BASIN - 4), pol(SPUR, 200)], 5, wob=0.3))
P.region_paint("Saddle", 2, "Sanctum", blob(*pol(SPUR, 116), 19, 19, SPUR, 0.08))

P.ramp("Throne", "Aerie", *pol(122, 112))
P.ramp("Throne", "Shelf", *pol(62, 112))
P.ramp("Shelf", "Floor", *pol(50, BASIN))
P.ramp("Saddle", "Shelf", *pol(SPUR + 9, 112))
P.ramp("Saddle", None, *pol(SPUR - 9, 112))       # up to the neighbour's pocket

P.forest_banned |= {"Heart", "Throne", "Aerie", "Shelf", "Floor", "Saddle"}
P.pin_curse = True           # not mirrored on both axes: a random curse draw would favour a seat

SEAT = ["Northmoot", "Sunwake", "Duskfall"]
def display(rec):
    k, c = rec["k"], rec["canon"]
    if c == "Heart": return "The Trine Heart"
    s = SEAT[k]
    if c == "Throne": return f"{s}"
    if c == "Aerie": return f"{s} Aerie"
    if c == "Saddle": return f"{s} Saddle"
    if c == "Shelf": return f"{s} Shelf"
    return f"{s} Floor"

# trees and rocks (2026-11-14 reskin): copses along the territory borders,
# a deeper rock scatter into the mountains; the room probe runs after them
P.hedgerows = dict(width=2.0, cover=0.4)
P.fringe_depth = 12.0
P.soft_ramps = True

P.finish("Trine_Hollow_v2", display, notes=[
    "v2 (2026-11-14): half the ground, players closer.",
    "Three-fold rotation: every seat the same ground.",
    "Rim: home down to the basin edge, a high pocket",
    "(west) and a terrace shelf (east) beside it.",
    "Saddles: raised Sanctums on the spurs - the",
    "back way from a shelf to the neighbour's pocket.",
    "Basin: open, rich; cursed mesa in the centre.",
])
