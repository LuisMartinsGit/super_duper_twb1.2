# layout.py — the ALANTHOR TUTORIAL mission map, 384 x 256 m.
#
# Run:  python tools/maps/tutorial_alanthor/layout.py [--export]
# docs/Design/Tutorial_Missions.md section 4. Not a lobby map: the scene lives
# under Assets/GameData/Scenes/Tutorial/.
#
# South-west, the player's town and two fields to claim. In the middle the cursed
# Sanctum (one node, so one monument); north of it a free Sanctum. East, the rival
# kingdom: its seat and two territories it already holds.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from layoutkit import *

HERE = os.path.dirname(os.path.abspath(__file__))
P = Plan("Tutorial Alanthor", 384, 256, group_identity(), HERE, seed=11)

HOME = (-128.0, -68.0)
RIVAL = (132.0, 66.0)
NODE = (-14.0, 14.0)
SEEDS = [
    ("Home", (-128.0, -68.0), "Start", HOME),
    ("WestField", (-136.0, 66.0), "NormalVeilstone", None),
    ("SouthField", (-6.0, -96.0), "NormalIron", None),
    ("CursedSanctum", (-14.0, 8.0), "Sanctum", None),
    ("NorthSanctum", (-30.0, 100.0), "Sanctum", None),
    ("Rival", (132.0, 66.0), "Start", RIVAL),
    ("RivalSouth", (128.0, -72.0), "Normal", None),
    ("RivalWest", (56.0, 84.0), "Normal", None),
]
RIVAL_LANDS = [(128.0, -72.0), (56.0, 84.0)]
NAMES = {"Home": "The Town", "WestField": "West Field", "SouthField": "South Field",
         "CursedSanctum": "The Cursed Sanctum", "NorthSanctum": "The North Sanctum",
         "Rival": "The Rival Seat", "RivalSouth": "Rival March", "RivalWest": "Rival Vale"}

def cell(i):
    pts = np.array([s[1] for s in SEEDS])
    def f(X, Z, E):
        d = np.stack([(X - p[0]) ** 2 + (Z - p[1]) ** 2 for p in pts], 0)
        return d.argmin(0) == i
    return f

P.terrain_paint("land", everywhere())
for i, (code, _, rtype, start) in enumerate(SEEDS):
    P.region_paint(code, 0, rtype, cell(i), start=start)
P.forest_banned |= {"CursedSanctum", "NorthSanctum", "WestField", "SouthField", "RivalSouth", "RivalWest"}
P.forest_cover = 1.6

def display(rec): return NAMES[rec["canon"]]
P.finish("TutorialAlanthor", display, notes=[
    "Town SW, two fields to claim, a cursed Sanctum in the middle,",
    "a free Sanctum north, the rival kingdom east.",
])
print(" node", NODE, "\n rival lands", RIVAL_LANDS)
