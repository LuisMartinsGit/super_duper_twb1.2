# layout.py — FIENDSTONE PASS v2, the six-player cliff-shelf map, 512 x 512 m.
#
# Run:  python tools/maps/fiendstone_pass_v2/layout.py [--export] [--table]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout.
# Shared machinery: tools/maps/layoutkit.py.
#
# v2 (2026-11-14): the half-size redesign on the shared kit. Units were
# halved; the 2 m build grid and every building footprint were not, so the
# 1024 m original (tools/maps/fiendstone_pass/layout.py, its own generator)
# was far too large. v2 keeps its concept on a quarter of the ground and
# brings the players closer. It is a new layout, not the old one scaled: a
# building does not shrink, so every territory is drawn big enough for its
# nodes, a Farm with its whole field and Houses, and each home for a full
# base (the kit's building-room probe, passed against six node draws).
#
# The concept, kept: cliff shelves stepping down to a river crossed by three
# WADED fords (west, centre, east), each split between the two banks' low
# ground so claims cross where armies wade; the south bank is the north bank
# turned 180 degrees exactly (group_rotation(2), canonical = the north half);
# every way between levels is a narrow ramp under a cliff edge (archers
# above). Per bank, from the river up:
#   level 0  low ground, three territories (west, the centre ford, east)
#   level 1  the cursed vein in the bank (one ramp to the centre ford ground)
#            and the Sanctum crypt under the pass (one ramp up to it)
#   level 2  two plateaus, each with two ramps down to the low ground, and
#            the mountain pass joining them behind the low ground
#   level 3  a start shelf under each start, and each start's private pocket
#   level 4  the starts, one ramp down to their shelf
#
# Every deviation from the original, and why:
#   * 15 territories a bank, not 21. Half the ground holds about a quarter
#     the territories at the same building size, so:
#     - the two RIM pockets (Westrim / Eastrim Hollow, iron rich off the
#       plateaus) are cut: the plateaus need their width for a Farm field
#       plus four nodes, and the start pockets are iron rich already;
#     - the pass is ONE territory (Fiendstone Pass): its centre and the two
#       saddle lanes that join it to the plateaus. A saddle lane narrow
#       enough to read as a pass cannot hold a Farm field on its own.
#   * The start pockets are as tall as the start, beside it in the start row
#     (still level 3, one ramp off the start): the corner seats' outboard
#     against the map edge, the middle seat's in the gap toward the east
#     start. The original's were slivers in the same places; a pocket with
#     three iron nodes now needs room for a Farm field too.
#   * The rows touch: the start sits on its shelf, the shelf on its plateau
#     (or the pass), the plateaus on the low ground, with cliffs and no
#     mountain band between them. The low ground's top cliff dips in the
#     middle to leave the Sanctum crypt its own ledge under the pass.
#   * No mountain spurs between the low grounds: the cursed vein stands in
#     the bank between the west and centre low ground instead, so the two
#     meet only along the river (the spur's job, a narrowing).
#   * Territory types are the original's, every one (the pass stays Empty,
#     the crossing); the cursed vein is PINNED (P.pin_curse): on a rotation
#     map TerritoryResources would re-deal the curse to a random territory
#     per match, which is unfair between seats.
#   * Forest only on the west / east low ground and the plateaus
#     (P.forest_banned for the rest): on half the ground a Farm field needs
#     it more.
#   * Ramps are 10 m (P.ramp_half 5), chokes for half-size units; the river
#     is 24 m wide, the fords ~18 m.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *
from layoutkit import _h

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Fiendstone Pass v2", 512, 512, group_rotation(2, 90.0), HERE, seed=7, warp=(4.0, 1.5))
P.ramp_half = 5.0                # 10 m ramps: a choke for half-size units

RIVER_HALF = 12.0                # half the river's width (deep water but at its banks)
FORDS = [-170.0, 0.0]            # the east ford is the west one turned 180 degrees


def ry(x):
    """the river's centre line: odd in x, so the 180-degree turn keeps it."""
    return 10.0 * np.sin(np.asarray(x, float) / 85.0)


def box(cx, cz, rx, rz, p=3.4, wob=0.06, rough=0.07):
    """a rounded box (superellipse) with a wobbled rim: far more buildable
    ground than an ellipse of the same reach, without reading as a box."""
    def f(X, Z, E):
        lx, lz = (X - cx) / rx, (Z - cz) / rz
        a = np.arctan2(lz, lx)
        r = 1.0
        for k, fq in enumerate((2, 3, 5)):
            r = r + wob * (0.6 / (k + 1)) * np.sin(fq * a + 6.28 * _h(f"{cx},{cz}", k))
        return (np.abs(lx) ** p + np.abs(lz) ** p) ** (1.0 / p) + E * rough < r
    return f


def low_top(X):
    """the first cliff above the low ground: lower in the middle, where the
    Sanctum crypt sits between the ford ground and the pass."""
    return 66.0 - 18.0 * np.exp(-(X / 50.0) ** 2)


north = lambda X, Z, E: Z > ry(X)
low = lambda X, Z, E: north(X, Z, E) & (Z < low_top(X) + E * 2.0)

# ── level 0: the low ground, west / the centre ford / east ───────────────────
P.region_paint("L1", 0, "NormalIron", AND(low, lambda X, Z, E: X + E * 3 < -60))
P.region_paint("L2", 0, "NormalVeilstone", AND(low, lambda X, Z, E: np.abs(X + E * 3) <= 60))
P.region_paint("L3", 0, "NormalIron", AND(low, lambda X, Z, E: X + E * 3 > 60))
# ── level 1: the cursed vein in the bank, the Sanctum crypt under the pass ──
P.region_paint("Rk1", 1, "VeilstoneRich", box(-84, 62, 38, 27))
P.region_paint("Rk3", 1, "Sanctum", box(0, 70, 32, 16))
# ── level 2: the plateaus and the pass between them ─────────────────────────
pass_lane = OR(lane([(-128, 100), (-80, 106), (-30, 102)], 9, wob=0.15),
               lane([(30, 102), (80, 106), (128, 100)], 9, wob=0.15))
P.region_paint("PassC", 2, "Empty", OR(pass_lane, box(0, 101, 38, 18)))
P.region_paint("PlW", 2, "NormalVeilstone", box(-185, 90, 64, 30))
P.region_paint("PlE", 2, "NormalVeilstone", box(185, 90, 64, 30))
# ── level 3: start shelves and the private pockets ──────────────────────────
P.region_paint("Sh1", 3, "NormalIron", box(-155, 144, 50, 28))
P.region_paint("Sh2", 3, "NormalIron", box(0, 142, 48, 27))
P.region_paint("Sh3", 3, "NormalIron", box(155, 144, 50, 28))
# ── level 4: the starts ──────────────────────────────────────────────────────
P.region_paint("P1", 4, "Start", box(-148, 208, 52, 42), start=(-148, 206))
P.region_paint("P2", 4, "Start", box(0, 208, 52, 42), start=(0, 206))
P.region_paint("P3", 4, "Start", box(155, 208, 52, 42), start=(155, 206))
P.region_paint("Sp1", 3, "IronRich", box(-226, 210, 26, 40))
P.region_paint("Sp2", 3, "IronRich", box(73, 200, 21, 46))
P.region_paint("Sp3", 3, "IronRich", box(229, 210, 25, 40))

# ── the river and its fords ──────────────────────────────────────────────────
river = lambda X, Z, E: np.abs(Z - ry(X)) + E * 1.2 < RIVER_HALF
P.terrain_paint("water", river)
for fx in FORDS:
    c = float(ry(fx))
    P.wade_paint(AND(river, lane([(fx, c - 18), (fx, c + 18)], 9, wob=0.15)))

# ── ramps (canonical; each painted in both banks) ────────────────────────────
for a, b, x, z in [
    ("P1", "Sh1", -150, 166), ("P2", "Sh2", 0, 166), ("P3", "Sh3", 155, 166),
    ("Sh1", "PlW", -165, 120), ("Sh3", "PlE", 165, 120), ("Sh2", "PassC", 0, 117),
    ("PlW", "L1", -225, 62), ("PlW", "L1", -140, 62),
    ("PlE", "L3", 225, 62), ("PlE", "L3", 140, 62),
    ("Rk1", "L2", -58, 55), ("Rk3", "PassC", 0, 85),
    ("Sp1", "P1", -200, 210), ("Sp2", "P2", 52, 205), ("Sp3", "P3", 205, 210),
]:
    P.ramp(a, b, x, z)

P.pin_curse = True
P.forest_banned |= {"L2", "Rk1", "Rk3", "PassC", "Sh1", "Sh2", "Sh3", "Sp1", "Sp2", "Sp3"}
P.forest_cover = 1.0

NAMES = {   # (north bank, south bank) — the original's names
    "L1": ("Westmere Shallows", "Southmere Shallows"), "L2": ("Fiendstone Ford", "Cinder Ford"),
    "L3": ("Eastmere Shallows", "Reedmere Shallows"),
    "PlW": ("Greywold Tableland", "Emberfell Tableland"), "PlE": ("Ashcrown Tableland", "Blackmoor Tableland"),
    "PassC": ("Fiendstone Pass", "Cinder Pass"),
    "Sh1": ("Harrow Ledge", "Wrath Ledge"), "Sh2": ("Gallow Ledge", "Mourn Ledge"), "Sh3": ("Thorn Ledge", "Ash Ledge"),
    "P1": ("Harrowhold", "Wrathhold"), "P2": ("Gallowhold", "Mournhold"), "P3": ("Thornhold", "Ashhold"),
    "Rk1": ("Sunken Vein", "Drowned Vein"), "Rk3": ("Saddle Crypt", "Cinder Crypt"),
    "Sp1": ("Harrow Cache", "Wrath Cache"), "Sp2": ("Gallow Cache", "Mourn Cache"), "Sp3": ("Thorn Cache", "Ash Cache"),
}
def display(rec):
    return NAMES[rec["canon"]][rec["k"] or 0]

# trees and rocks (2026-11-14 reskin): copses along the territory borders,
# a deeper rock scatter into the mountains; the room probe runs after them
P.hedgerows = dict(width=2.0, cover=0.4, skip_types=("Start", "Sanctum", "VeilstoneRich", "NormalVeilstone", "IronRich"))
P.fringe_depth = 12.0
P.soft_ramps = True

P.finish("Fiendstone_Pass_v2", display, notes=[
    "v2 (2026-11-14): half the ground, players closer.",
    "Cliff shelves down to a river; three waded fords,",
    "each split between the banks. South = north",
    "turned 180 degrees. Per bank: 3 low grounds,",
    "a cursed vein (pinned) and a Sanctum crypt,",
    "2 plateaus + the pass, 3 shelves, 3 starts,",
    "each with a private iron pocket. 10 m ramps.",
])
