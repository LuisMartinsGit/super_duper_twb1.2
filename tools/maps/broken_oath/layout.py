# layout.py — BROKEN OATH, the five-player 3v2 map, 768 x 448 m.
#
# Run:  python tools/maps/broken_oath/layout.py [--export]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout (no Unity
# builder exists yet). Shared machinery: tools/maps/layoutkit.py.
#
# Mirrored north/south only (the south half is authored); the two teams are
# NOT mirrors of each other. The balance is numbers against ground:
#   the THREE (seats 1-3, west) stand on open low ground, their homes close
#     together so they can help each other, with two small cliff pockets each
#     along the west edge;
#   the TWO (seats 4-5, east) hold the HIGHMARCH, a plateau above an
#     escarpment with only four ramps up, more safe ground per player and a
#     private tarn pocket each, but almost all of it poor.
# The middle third between them is low, open and rich: the curse, iron-rich
# and veilstone ground, and the Oathstone, a raised Sanctum with four ramps.
# Walking distance from every home to the middle is the same for both teams.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Broken Oath", 768, 448, group_mirror_z(), HERE, seed=5)

def esc(z):                  # the escarpment line, x of its foot
    return 150.0 + 10.0 * np.sin(np.asarray(z) / 38.0 + 0.6)
WEST_EDGE = -132.0           # where the trio's ground gives way to the middle

P.terrain_paint("land", everywhere())
P.terrain_paint("mountain", lambda X, Z, E: (np.abs(X) + E * 4 > 384 - 14) | (np.abs(Z) + E * 4 > 224 - 14))
P.terrain_paint("mountain", blob(-60, -150, 9, 7, 20, 0.3))           # tors in the middle
P.terrain_paint("mountain", blob(70, -95, 7, 9, -20, 0.3))
P.terrain_paint("mountain", blob(-20, -60, 6, 5, 0, 0.3))

# ── the trio (west): homes and cliff pockets ──
P.region_paint("Hold", 0, "Start", blob(-296, -146, 64, 58, 0, 0.08), start=(-298.0, -146.0))
P.region_paint("Oathhold", 0, "Start", blob(-314, 0, 54, 54, 0, 0.08), shared=True, start=(-316.0, 0.0))
P.region_paint("Crook", 1, "Normal", blob(-348, -78, 30, 38, 0, 0.12))
P.region_paint("Scar", 1, "NormalIron", blob(-336, -200, 34, 18, 0, 0.12))
P.ramp("Crook", None, -330, -78)
P.ramp("Scar", None, -300, -196)
def west_types(seeds):
    order = np.argsort([-x for x, z in seeds])     # nearest the middle last
    plan = ["Normal", "NormalIron", "Normal", "NormalIron", "NormalVeilstone", "NormalIron"]
    out = [None] * len(seeds)
    for rank, i in enumerate(order): out[i] = (plan[rank], None)
    return out
P.split("West", 0, lambda X, Z, E: X + E * 3 < WEST_EDGE, 6, west_types)

# ── the duo (east): homes, tarns and the Highmarch plateau, level 2 ──
P.region_paint("Bastion", 2, "Start", blob(294, -100, 66, 62, 0, 0.08), start=(294.0, -98.0))
P.region_paint("Tarn", 3, "Normal", blob(336, -174, 48, 30, 0, 0.10))
P.ramp("Tarn", "Bastion", 318, -156)
def plateau_types(seeds):
    # back to front: the plateau is safe and poor; one veilstone, at the back
    order = np.argsort([-x for x, z in seeds])
    plan = ["NormalVeilstone", "Normal", "NormalIron", "Empty", "Normal", "NormalIron", "Normal"]
    out = [None] * len(seeds)
    for rank, i in enumerate(order): out[i] = (plan[rank], None)
    return out
P.split("High", 2, lambda X, Z, E: X + E * 3 > esc(Z), 7, plateau_types)
for z in (-48.0, -178.0):    # four ramps up the escarpment (two each half)
    P.ramp(None, None, float(esc(z)), z)

# ── the middle: the Oathstone and the rich ground ──
P.region_paint("Oathstone", 1, "Sanctum", blob(0, 0, 32, 26, 0, 0.12), shared=True)
P.ramp("Oathstone", None, 0, -30)
P.ramp("Oathstone", None, -36, 0)
P.ramp("Oathstone", None, 36, 0)
def mid_types(seeds):
    order = np.argsort([np.hypot(x * 0.8, z) for x, z in seeds])
    plan = ["VeilstoneRich", "IronRich", "NormalVeilstone", "IronRich", "NormalVeilstone",
            "NormalVeilstone", "IronRich", "NormalVeilstone", "Normal", "NormalIron", "NormalIron"]
    out = [None] * len(seeds)
    for rank, i in enumerate(order): out[i] = (plan[rank], None)
    return out
P.split("Mid", 0, everywhere(), 11, mid_types)
P.forest_banned |= {"Oathstone", "Crook", "Scar", "Tarn"}
P.pin_curse = True           # not mirrored on both axes: a random curse draw would favour a seat
P.forest_cover = 1.2

def display(rec):
    k, c = rec["k"], rec["canon"]
    side = ["South", "North"][k] if k is not None else ""
    fixed = {"Bastion": f"{side} Bastion", "Tarn": f"{side} Tarn", "Hold": f"{side} Hold",
             "Oathhold": "Oathhold", "Crook": f"{side} Crook", "Scar": f"{side} Scar",
             "Oathstone": "The Oathstone"}
    if c in fixed: return fixed[c]
    pre = {"High": "Highmarch", "West": "Lowmarch", "Mid": "Middlemoor"}[c.rstrip("0123456789")]
    return f"{pre} {side} {int(c.lstrip('HighWestMid')) + 1}"

P.finish("Broken Oath", display, notes=[
    "TEAM A = seats 1-3 (west), TEAM B = 4-5 (east).",
    "A: open low ground, homes close together,",
    "two one-ramp cliff pockets each.",
    "B: the Highmarch plateau - four ramps up the",
    "escarpment, a private tarn each; safe, poor.",
    "Middle: rich, open; Oathstone = raised",
    "Sanctum (four ramps). Mirrored N/S only.",
])
