"""Blood Altar — the Feraldis Temple of Ridan (4x4 cells = 8 m). A
basalt altar hall standing in a permanent blood pool: a stepped plinth, a
dark hall with rune-lit faces, a great sacrificial slab under an iron gallows
frame with chains, horned skull pillars and braziers. The six chapel faces
stay clear (chapels are separate entities around the footprint)."""
import math
from kit import MB

NAME = "BloodAltar"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/BloodAltar/Art"
LEVELS = ["Model"]
F = 7.8


def build(tag):
    m = MB("Model")
    # the permanent blood pool fills the footprint; stone steps rise out of it
    m.box((F, F, 0.1), (0, 0, 0), "Fer_Stone")
    m.disc(F / 2 - 0.1, (0, 0, 0.1), "Fer_Blood", segs=16, jitter=0.12, thick=0.06)
    z = 0.16
    m.pyramid(5.6, 0.45, (0, 0, z), "Fer_Stone", top=5.0)
    m.pyramid(4.6, 0.45, (0, 0, z + 0.45), "Fer_Stone", top=4.0)
    z += 0.9
    # the altar hall: a dark basalt block with an iron-plated steep roof
    m.hall((0, 0.5, z), 4.0, 3.2, 2.6, 2.4, axis='y', wall_mat="Fer_Stone", roof_mat="Fer_Iron",
           dragons=False, planks=False, bands=2, door_w=1.3, door_h=2.0, door_mat="Fer_Rune")
    for sx in (-1, 1):
        m.dragon_head((0, 0.5 + sx * (2.0 + 0.35), z + 2.6 + 2.4 - 0.9), yaw=(0.0 if sx < 0 else math.pi), s=0.9, mat="Fer_Iron")
    # rune strips on the hall faces
    for sx in (-1, 1):
        for j in range(3):
            m.box((0.08, 0.05, 0.6), (sx * 1.62, 0.5 - 1.0 + j * 1.0, z + 0.8), "Fer_Rune")
    m.crest((0, 0.5 - 2.0, z + 5.0), (0, 0.5 + 2.0, z + 5.0), n=6, h=0.45)
    # sacrificial slab in front of the door, under an iron gallows frame
    sy = -2.3
    m.box((1.6, 0.9, 0.7), (0, sy, 0.16), "Fer_Stone")
    m.box((1.7, 1.0, 0.1), (0, sy, 0.86), "Fer_Blood")
    for sx in (-1, 1):
        m.cyl(0.1, 3.2, (sx * 1.3, sy, 0.16), "Fer_Iron", segs=6)
    m.box((2.8, 0.14, 0.14), (0, sy, 3.3), "Fer_Iron")
    m.chain((-0.5, sy, 3.25), (-0.5, sy, 1.5))
    m.chain((0.5, sy, 3.25), (0.5, sy, 1.5))
    m.skull((0, sy - 0.2, 0.96), s=0.3)
    # horned skull pillars at the four plinth corners, braziers at the front
    for sx in (-1, 1):
        for sy2 in (-1, 1):
            m.rune_post((sx * 2.9, sy2 * 2.9, 0.16), h=2.0, w=0.45)
            m.skull((sx * 2.9, sy2 * 2.9, 2.5), s=0.34)
            m.horn((sx * 2.9 - 0.14, sy2 * 2.9, 2.7), length=0.5, r=0.05, rot=(0, -0.7, 0))
            m.horn((sx * 2.9 + 0.14, sy2 * 2.9, 2.7), length=0.5, r=0.05, rot=(0, 0.7, 0))
    m.brazier((-1.6, -1.2, 0.16), r=0.35, h=0.8)
    m.brazier((1.6, -1.2, 0.16), r=0.35, h=0.8)
    # bone piles in the pool
    for k in range(6):
        a = k * 1.05 + 0.4
        m.skull((math.cos(a) * 3.3, math.sin(a) * 3.3, 0.16), s=0.24, yaw=a)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.2, -3.2, 0.16), h=3.8, w=0.7, drop=1.5)
    b.banner((3.2, -3.2, 0.16), h=3.8, w=0.7, drop=1.5)
    objs.append(b.build())
    return objs
