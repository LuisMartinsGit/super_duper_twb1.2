# layout.py — TRINE HOLLOW, the three-player radial map, 640 x 640 m.
#
# Run:  python tools/maps/trine_hollow/layout.py [--export]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout (no Unity
# builder exists yet). Shared machinery: tools/maps/layoutkit.py.
#
# Three-fold ROTATIONAL symmetry: the wedge around seat 1 (north) is authored
# and turned 120 and 240 degrees for seats 2 and 3. (TerritoryResources
# mirrors node layouts only on maps mirrored across both axes, so on this map
# each territory's nodes are placed at random within it — the TYPES are the
# same for every seat, the node spots are not.)
#
# A bowl stepping down to the middle:
#   rim (level 2)      each home on the rim with two high pockets (level 3);
#                      mountains behind and between the seats
#   terraces (level 1) a ring below the rim, cut between seats by mountain
#                      spurs; the home has two ramps down to it
#   saddles (level 2)  a raised Sanctum on each spur, ramps to the terraces
#                      either side: the only way between two seats' terraces
#                      that avoids the basin
#   basin (level 0)    the open, rich floor, with a cursed mesa (level 1,
#                      three ramps) in the very centre
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Trine Hollow", 640, 640, group_rotation(3, 90.0), HERE, seed=3)

def pol(deg, r):
    a = np.radians(deg)
    return r * np.cos(a), r * np.sin(a)

BASIN, TERRACE = 136.0, 202.0
P.terrain_paint("land", everywhere())
P.terrain_paint("mountain", NOT(disc(0, 0, TERRACE, rough=6)))
P.terrain_paint("mountain", lane([pol(30, 150), pol(30, 330)], 20, wob=0.3))   # the spur between seats
P.terrain_paint("mountain", blob(*pol(62, 104), 7, 9, 0, 0.3))                # tors in the basin
P.terrain_paint("mountain", blob(*pol(118, 104), 9, 7, 0, 0.3))

# ── the seat, on the rim ──
P.region_paint("Throne", 2, "Start", blob(0, 238, 68, 50, 0, 0.08), start=(0.0, 238.0))
P.region_paint("Aerie", 3, "Normal", blob(*pol(70, 250), 24, 34, 70, 0.12))
P.region_paint("Spire", 3, "NormalIron", blob(*pol(110, 250), 24, 34, 110, 0.12))
P.ramp("Throne", "Aerie", *pol(76, 236))
P.ramp("Throne", "Spire", *pol(104, 236))
P.ramp("Throne", None, *pol(74, 196))          # two ramps down to the terraces
P.ramp("Throne", None, *pol(106, 196))

# ── the saddle on the spur: a raised Sanctum ──
P.region_paint("Saddle", 2, "Sanctum", blob(*pol(30, 214), 26, 30, 30, 0.10))
P.ramp("Saddle", None, *pol(21, 206))
P.ramp("Saddle", None, *pol(39, 206))

# ── the cursed mesa in the centre (one territory, all three seats') ──
P.region_paint("Heart", 1, "VeilstoneRich", disc(0, 0, 30, rough=4), shared=True)
P.ramp("Heart", None, *pol(90, 30))

def by_angle(plan):
    """types by how far a seed is from the seat's own axis (90 deg)."""
    def f(seeds):
        order = np.argsort([abs(((np.degrees(np.arctan2(z, x)) - 90 + 180) % 360) - 180) for x, z in seeds])
        out = [None] * len(seeds)
        for rank, i in enumerate(order): out[i] = (plan[rank], None)
        return out
    return f
# terraces: medium; the one under the home is the poorest
P.split("Terrace", 1, ring(0, 0, BASIN, TERRACE + 4, rough=5), 4,
        by_angle(["Normal", "NormalIron", "NormalVeilstone", "Empty"]))
for deg in (55.0, 125.0):    # terraces down to the basin
    P.ramp(None, None, *pol(deg, BASIN))
# basin: rich
P.split("Basin", 0, disc(0, 0, BASIN + 2, rough=5), 4,
        by_angle(["NormalVeilstone", "IronRich", "NormalVeilstone", "IronRich"]))
P.forest_banned |= {"Saddle", "Heart", "Aerie", "Spire"}
P.pin_curse = True           # not mirrored on both axes: a random curse draw would favour a seat
P.forest_cover = 1.25

SEAT = ["Northmoot", "Sunwake", "Duskfall"]
WORDS_T = ["Step", "Shelf", "Bench", "Brow", "Ledge"]
WORDS_B = ["Floor", "Lea", "Mere", "Bottom", "Flats"]
def display(rec):
    k, c = rec["k"], rec["canon"]
    if c == "Heart": return "The Trine Heart"
    s = SEAT[k]
    if c == "Throne": return f"{s}"
    if c in ("Aerie", "Spire"): return f"{s} {c}"
    if c == "Saddle": return f"{s} Saddle"
    if c.startswith("Terrace"): return f"{s} {WORDS_T[int(c[7:]) % 5]}"
    return f"{s} {WORDS_B[int(c[5:]) % 5]}"

P.finish("Trine Hollow", display, notes=[
    "Three-fold rotation: every seat the same ground.",
    "Rim: home + two high pockets, two ramps down.",
    "Terraces: a ring, split by spurs between seats.",
    "Saddles: raised Sanctums on the spurs - the",
    "back way between neighbours' terraces.",
    "Basin: open, rich; cursed mesa in the centre.",
])
