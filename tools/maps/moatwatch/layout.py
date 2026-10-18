# layout.py — MOATWATCH, the four-player moated-corners map, 512 x 512 m.
#
# Run:  python tools/maps/moatwatch/layout.py [--export]
# Writes natural.png and preview.png beside this file; --export also writes
# heights.r16, the masks and layout.json for MapBuilderFromLayout (no Unity
# builder exists yet). Shared machinery: tools/maps/layoutkit.py.
#
# Mirrored across BOTH axes: the south-west quadrant is authored and the
# others are its mirror images, so TerritoryResources.MirrorPartners lays the
# same nodes for every seat. Nothing straddles an axis (the centre is a rock
# tor, unclaimable).
#
# Each corner is a KEEP behind a moat (deep water) crossed by two waded fords
# that face the middle. Inside the moat: the home and two small garths, poor
# but safe. Outside: one big open expanse cut into many small territories,
# poor by the moats and along the edges, rich toward the centre, with a raised
# barrow (the Sanctum) in each quadrant.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Moatwatch", 512, 512, group_mirror_both(), HERE, seed=4)

C = (-256.0, -256.0)            # the canonical corner
MOAT_IN, MOAT_OUT = 146.0, 164.0  # moat ring, metres from the corner
FORDS = [30.0, 60.0]           # degrees from the corner, east = 0
HOME_SECTOR = (17.0, 73.0)

def corner_pt(deg, r):
    a = np.radians(deg)
    return C[0] + r * np.cos(a), C[1] + r * np.sin(a)

P.terrain_paint("land", everywhere())
P.terrain_paint("mountain", disc(0, 0, 20, rough=4))                     # the centre tor
P.terrain_paint("mountain", blob(-168, -46, 11, 7, 20, 0.25))            # outcrops in the expanse
P.terrain_paint("mountain", blob(-46, -168, 7, 11, -20, 0.25))
P.terrain_paint("mountain", blob(-112, -150, 6, 5, 0, 0.3))
P.terrain_paint("mountain", blob(-150, -112, 5, 6, 0, 0.3))
moat = AND(ring(*C, MOAT_IN, MOAT_OUT, rough=2.5), sector(*C, -5, 95))
P.terrain_paint("water", moat)

# inside the moat
start = corner_pt(45, 82)
inner = disc(*C, MOAT_IN + 1, rough=2.5)
P.region_paint("Keep", 0, "Start", inner, start=start)
# the garths lie along the moat's two ends, against the map edge
P.region_paint("GarthS", 0, "Normal", AND(inner, blob(*corner_pt(5, 118), 36, 56, 5, 0.1)))
P.region_paint("GarthW", 0, "NormalIron", AND(inner, blob(*corner_pt(85, 118), 36, 56, 85, 0.1)))
# the fords: waded water, each its own (Empty) territory, so the keep borders
# the expanse and claims can cross (a territory ringed by unclaimed water
# borders nothing)
for i, deg in enumerate(FORDS):
    a, b = corner_pt(deg, MOAT_IN - 6), corner_pt(deg, MOAT_OUT + 6)
    P.ford_paint(f"Ford{i}", "Empty", AND(moat, lane([a, b], 10, wob=0.15)))
P.labels.update({"Ford0": "Ford", "Ford1": "Ford"})

# the Sanctum barrow: a raised mound, two ramps
P.region_paint("Barrow", 1, "Sanctum", blob(-78, -78, 26, 19, 45, 0.15))
P.ramp("Barrow", None, -60, -60)
P.ramp("Barrow", None, -96, -96)

# the expanse: 11 territories a quadrant, rich toward the centre
def types(seeds):
    order = np.argsort([np.hypot(x, z) for x, z in seeds])
    plan = ["VeilstoneRich", "NormalVeilstone", "IronRich", "NormalVeilstone", "IronRich",
            "NormalVeilstone", "Normal", "NormalIron", "NormalIron", "Normal", "NormalIron"]
    out = [None] * len(seeds)
    for rank, i in enumerate(order): out[i] = (plan[rank], None)
    return out
P.split("Field", 0, NOT(disc(*C, MOAT_OUT + 1, rough=2.5)), 11, types, anchor=np.array([-128.0, -128.0]))
P.forest_banned |= {"Barrow"}
P.forest_cover = 1.25

SEAT = ["Ashmere", "Brackwater", "Coldfen", "Dunmoor"]   # SW, SE, NW, NE (group order)
WORDS = ["Heart", "Lea", "Reach", "Wold", "Field", "Common", "Marches", "Steading", "Hythe", "Holt", "Brake"]
def display(rec):
    s = SEAT[rec["k"]] if rec["k"] is not None else ""
    c = rec["canon"]
    if c == "Keep": return f"{s} Keep"
    if c == "GarthS": return f"{s} South Garth" if rec["k"] in (0, 1) else f"{s} North Garth"
    if c == "GarthW": return f"{s} Garth"
    if c.startswith("Ford"): return f"{s} Ford {'I' if c == 'Ford0' else 'II'}"
    if c == "Barrow": return f"{s} Barrow"
    return f"{s} {WORDS[int(c[5:]) % len(WORDS)]}"

P.finish("Moatwatch", display, notes=[
    "Each keep: home + two garths inside a moat,",
    "two waded fords (Empty) toward the middle.",
    "Poor and safe inside; rich and open outside.",
    "Barrow = raised Sanctum, two ramps.",
    "Centre = rock tor (unclaimable).",
])
