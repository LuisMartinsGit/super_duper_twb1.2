# layout.py — BROKEN OATH v2, the five-player 3v2 map, 384 x 224 m.
#
# Run:  python tools/maps/broken_oath_v2/layout.py [--export] [--table]
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
# Broken Oath's concept, kept: mirrored north/south only (the south half is
# authored); the two teams are NOT mirrors of each other, the balance is
# numbers against ground:
#   the THREE (seats 1-3, west) stand on open low ground, their homes close
#     together, with a one-ramp cliff pocket each half;
#   the TWO (seats 4-5, east) hold the HIGHMARCH, a plateau above an
#     escarpment with few ramps up, more safe ground per player and a private
#     tarn pocket each, but poor;
#   the middle between them is low, open and rich: the curse, iron-rich
#     ground and the Oathstone, a raised Sanctum with four ramps.
#
# What v2 changed, and why (a territory with three or four random nodes needs
# ~3,800-4,200 m2 of compact ground to keep a Farm field every draw, a home
# ~6,500, so 224 m north-south holds one band of five homes and little else):
#   - the map is drawn as three BANDS of near-rectangular territories (west,
#     middle, plateau) instead of Voronoi fields: blobs and Voronoi cells
#     wasted the ground the Farms need;
#   - the trio has ONE cliff pocket a half (the Scar, in the corner behind
#     each Hold), not two, and no Lowmarch: the homes and pockets fill the
#     whole west band;
#   - the Highmarch is ONE shared territory on the axis between the two
#     Bastions (it was seven a half), and the Bastions reach the escarpment
#     themselves; the plateau keeps its one Normal + veilstone (now the
#     Highmarch) and the Tarns;
#   - the escarpment has THREE ramps (one into each Bastion, one into the
#     Highmarch on the axis), not four: the Highmarch is too narrow for two;
#   - the middle is TWO territories a half round the Oathstone (the curse
#     beside it, iron-rich toward the edge) instead of eleven, and its tors
#     are gone; the curse stays pinned to the two Veilstone rich territories
#     flanking the Oathstone on the axis;
#   - the Scar is Normal + iron and the Tarn Normal (the original had a
#     Normal Crook and a Normal + iron Scar a half; one pocket keeps the iron);
#   - the mountain rim is 4 m (14 m before) and there is no forest on
#     claimable ground (both ate Farm room), so this map has no forest.
# Walking distance from every home to the Oathstone stays equal: 108-112 m
# for all five seats (the starts are placed for it).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Broken Oath v2", 384, 224, group_mirror_z(), HERE, seed=5, warp=(4.0, 1.5))
P.ramp_half = 5.0                # 10 m ramps: a choke for half-size units

EDGE = 4.0                       # the mountain rim
WM = -46.0                       # the west band | the middle
WX = -116.0                      # the Scar / Oathhold | the Hold
R = 2.5                          # edge roughness of the bands

def esc(z):                      # the escarpment line, x of its foot
    return 54.0 + 4.0 * np.sin(np.asarray(z) / 22.0 + 0.6)

def box(x0, x1, z0, z1):
    return lambda X, Z, E: (X + E * R > x0) & (X + E * R < x1) & (Z + E * R > z0) & (Z + E * R < z1)

plateau = lambda X, Z, E: X + E * R >= esc(Z)

P.terrain_paint("land", everywhere())

# ── the trio (west): homes and cliff pockets, low ground ──
P.region_paint("Hold", 0, "Start", box(WX, WM, -999, 0), start=(-100.0, -72.0))
P.region_paint("Oathhold", 0, "Start", box(-999, WX, -50, 50),
               shared=True, start=(-128.0, 0.0))
P.region_paint("Scar", 1, "NormalIron", box(-999, WX, -999, -50))
P.ramp("Scar", "Hold", WX, -94)

# ── the middle: the curse beside the Oathstone, iron-rich toward the edge ──
P.region_paint("Mid0", 0, "VeilstoneRich", AND(box(WM, 999, -64, 0), NOT(plateau)))
P.region_paint("Mid1", 0, "IronRich", AND(box(WM, 999, -999, -64), NOT(plateau)))
P.region_paint("Oathstone", 1, "Sanctum", disc(0, 0, 21, rough=1.5), shared=True)
P.ramp("Oathstone", None, 0, -21)
P.ramp("Oathstone", None, -21, 0)
P.ramp("Oathstone", None, 21, 0)

# ── the duo (east): the Highmarch plateau, level 2, a tarn pocket above ──
P.region_paint("Bastion", 2, "Start", AND(plateau, box(-999, 999, -999, -16)), start=(96.0, -62.0))
P.region_paint("High", 2, "NormalVeilstone", AND(plateau, box(-999, 999, -16, 16)), shared=True)
P.region_paint("Tarn", 3, "Normal", box(142, 999, -999, -16))
P.ramp("Tarn", "Bastion", 142, -28)
P.ramp(None, None, float(esc(-46.0)), -46.0)      # into each Bastion
P.ramp("High", None, float(esc(0.0)), 0.0)        # into the Highmarch, on the axis

# the rim last: a mountain clears whatever territory it covers
P.terrain_paint("mountain", lambda X, Z, E: (np.abs(X) + E * 2 > 192 - EDGE) | (np.abs(Z) + E * 2 > 112 - EDGE))

P.forest_banned |= {"Hold", "Oathhold", "Scar", "Mid0", "Mid1", "Oathstone", "Bastion", "High", "Tarn"}
P.pin_curse = True           # not mirrored on both axes: a random curse draw would favour a seat

def display(rec):
    k, c = rec["k"], rec["canon"]
    side = ["South", "North"][k] if k is not None else ""
    return {"Hold": f"{side} Hold", "Oathhold": "Oathhold", "Scar": f"{side} Scar",
            "Bastion": f"{side} Bastion", "Tarn": f"{side} Tarn", "High": "The Highmarch",
            "Oathstone": "The Oathstone",
            "Mid0": f"Middlemoor {side} 1", "Mid1": f"Middlemoor {side} 2"}[c]

# trees and rocks (2026-11-14 reskin): copses along the territory borders,
# a deeper rock scatter into the mountains; the room probe runs after them
P.hedgerows = dict(width=2.0, cover=0.4)
P.fringe_depth = 12.0
P.soft_ramps = True

P.finish("Broken_Oath_v2", display, notes=[
    "v2 (2026-11-14): half the ground, players closer.",
    "TEAM A = seats 1-3 (west), TEAM B = 4-5 (east).",
    "A: open low ground, homes close together,",
    "a one-ramp cliff pocket (Scar) each half.",
    "B: the Highmarch plateau - three ramps up the",
    "escarpment, a private tarn each; safe, poor.",
    "Middle: the curse and iron-rich ground round",
    "the Oathstone (raised Sanctum, four ramps).",
])
