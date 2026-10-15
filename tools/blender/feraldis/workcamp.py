"""Work Camp — the Feraldis Gatherer's Hut (2x2 cells = 4 m), a supply
extractor staffed by slaves. Lv1 an open shed, sawhorse, log pile, crates and
a slave stake; Lv2 a second shed and a hoist arm; Lv3 a hoist tower, a small
furnace and iron-banded stores."""
import math
from kit import MB

NAME = "WorkCamp"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/WorkCamp/Art"
LEVELS = ["Lv1", "Lv2", "Lv3"]
F = 3.9


def build(tag):
    lvl = int(tag[-1])
    m = MB("Model")
    m.box((F, F, 0.16), (0, 0, 0), "Fer_Stone")
    z = 0.16
    # the shed (back-left), plank roof on posts
    m.leanto((-0.85, 0.85, z), 2.0, 1.8, 2.2, 1.6, mat="Fer_Plank", open_front=False)
    # sawhorse + beam (front-left)
    for sx in (-1, 1):
        m.beam((-1.5 + sx * 0.25, -1.1, z), (-1.5 - sx * 0.25, -1.1, z + 0.75), t=0.07)
        m.beam((-0.4 + sx * 0.25, -1.1, z), (-0.4 - sx * 0.25, -1.1, z + 0.75), t=0.07)
    m.cyl(0.14, 1.9, (-0.95, -1.1, z + 0.78), "Fer_Plank", segs=6, rot=(0, math.pi / 2, 0), anchor_bottom=False)
    m.axe((-0.7, -1.0, z + 0.9), rot=(-1.1, 0, 0.3), s=0.8)
    # log pile (right)
    m.log_pile((1.2, 0.9, z), n=3 if lvl < 3 else 6, length=1.7, r=0.17, yaw=math.pi / 2)
    # crates
    m.crate((1.3, -0.6, z), s=0.6)
    m.crate((1.3, -0.6, z + 0.6), s=0.5, yaw=0.3)
    # slave stake with a chain
    m.post((0.2, -1.5, z), 1.3, r=0.1)
    m.chain((0.2, -1.5, z + 1.2), (0.6, -1.3, z + 0.3))
    m.disc(0.45, (0.5, -1.3, z), "Fer_Blood", segs=8, jitter=0.4)
    if lvl >= 2:
        # second shed (back-right over the logs) and a hoist arm
        m.leanto((1.2, 1.0, z), 1.4, 1.6, 2.4, 1.9, mat="Fer_Hide")
        m.post((0.3, 0.2, z), 2.8, r=0.12)
        m.beam((0.3, 0.2, z + 2.7), (1.1, -0.5, z + 2.2), t=0.11)
        m.beam((0.3, 0.2, z + 1.9), (0.8, -0.25, z + 2.35), t=0.07)
        m.chain((1.05, -0.45, z + 2.2), (1.05, -0.45, z + 1.1))
        m.crate((1.05, -0.45, z + 0.55), s=0.5, yaw=0.5)
        m.band((2.0, 1.8, 0.1), (-0.85, 0.85, z + 1.3), rivets=False)
    if lvl == 1:
        m.brazier((0.9, -1.4, z), r=0.28, h=0.55)
    if lvl >= 3:
        # hoist tower with a crane beam, and a small furnace with a stack
        m.watch_platform((-1.2, -0.2, z), h=3.4, w=0.8, roof=False)
        m.beam((-1.2, -0.2, z + 3.5), (0.2, -1.3, z + 3.0), t=0.12)
        m.chain((0.15, -1.25, z + 3.0), (0.15, -1.25, z + 1.9))
        m.box((0.5, 0.5, 0.5), (0.15, -1.25, z + 1.4), "Fer_Iron")
        m.box((1.0, 0.9, 1.1), (1.35, -1.4, z), "Fer_Stone")
        m.furnace_mouth((1.35, -1.85, z + 0.1), w=0.5, h=0.5)
        m.furnace_stack((1.35, -1.3, z + 1.1), r=0.22, h=2.3, bands=2, segs=7)
        m.rune_post((-1.6, 1.65, z), h=1.4, w=0.3)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((1.75, -1.75, z), h=2.3 + 0.3 * lvl, w=0.45, drop=0.9)
    objs.append(b.build())
    return objs
