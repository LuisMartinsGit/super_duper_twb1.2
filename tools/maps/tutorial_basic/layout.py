# layout.py — the BASIC TUTORIAL mission map, 320 x 192 m, two territories.
#
# Run:  python tools/maps/tutorial_basic/layout.py [--export]
# docs/Design/Tutorial_Missions.md. Not a lobby map: the scene lives under
# Assets/GameData/Scenes/Tutorial/ and only the Tutorial menu entry loads it.
#
# West, the WILDWOOD: a path through solid forest (Litharch -> three glades of
# lost workers -> the camp clearing) and four side glades of lost Spearmen off
# the clearing. East, the BLIGHT: open ground and the curse node, reached by
# one gap in the trees.
#
# THE SITES ARE PRINTED AT THE END AND MUST MATCH TutorialMissionSystem.asset.
# Every one is checked to stand on walkable ground joined to the Litharch.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Tutorial Basic", 320, 192, group_identity(), HERE, seed=7)

LITHARCH = (-138.0, -72.0)
WORKERS = [(-118.0, -18.0), (-138.0, 38.0), (-92.0, 68.0)]
CAMP = (-14.0, 0.0)
SPEARMEN = [(-62.0, -74.0), (40.0, -76.0), (42.0, 74.0), (-22.0, 80.0)]
NODE = (122.0, 0.0)
RESOURCE_NODES = [(-52.0, 26.0), (-50.0, -24.0), (18.0, 36.0), (24.0, -30.0), (42.0, 6.0), (-14.0, 42.0), (-16.0, -40.0)]   # supply x3, iron x3, veilstone
BORDER_X = 72.0

def west(X, Z, E): return X + E * 3.0 < BORDER_X
def east(X, Z, E): return X + E * 3.0 >= BORDER_X

P.terrain_paint("land", everywhere())
P.region_paint("Wildwood", 0, "Start", west, start=CAMP)
P.region_paint("Blight", 0, "Empty", east)

glades = [disc(*LITHARCH, 22, rough=3)]
glades += [disc(*w, 21, rough=3) for w in WORKERS]
glades += [disc(*s, 18, rough=3) for s in SPEARMEN]
open_ground = [
    blob(*CAMP, 72, 58, 0, 0.12),                                    # the camp clearing
    blob(*NODE, 44, 72, 0, 0.12),                                    # the Blight's open ground
    lane([LITHARCH, WORKERS[0], WORKERS[1], WORKERS[2], (-60.0, 30.0)], 22, wob=0.08),
    lane([(45.0, 0.0), (85.0, 0.0)], 24, wob=0.08),                  # the only way east
]
open_ground += [lane([CAMP, s], 18, wob=0.08) for s in SPEARMEN]
clear = glades + open_ground
def woods(X, Z, E):
    m = np.zeros(X.shape, bool)
    for c in clear: m |= c(X, Z, E)
    return ~m
P.forest_zone = woods

def display(rec): return {"Wildwood": "The Wildwood", "Blight": "The Blight"}[rec["canon"]]

P.finish("TutorialBasic", display, notes=[
    "Litharch, three glades of workers, the camp.",
    "Four glades of lost Spearmen off the clearing.",
    "One gap east to the Blight and the curse node.",
])

# every mission site must stand on walkable ground joined to the Litharch
lab, _ = ndimage.label(P.walk)
def root(pt, radius=4):
    r, c = P.img(*pt)
    sub = lab[r - radius:r + radius + 1, c - radius:c + radius + 1]
    vals = sub[sub > 0]
    return int(np.bincount(vals).argmax()) if vals.size else 0
home = root(LITHARCH)
bad = [p for p in [*WORKERS, CAMP, *SPEARMEN, NODE, *RESOURCE_NODES] if root(p) != home]
if home == 0 or bad:
    print("SITE PROBLEM, cut off from the Litharch:", bad); sys.exit(1)
print("sites ok; asset values:")
print(" litharchStart", LITHARCH, "\n campSpot", CAMP, "\n nodePosition", NODE)
print(" workerSites", WORKERS, "\n spearmanSites", SPEARMEN, "\n resource nodes", RESOURCE_NODES)
