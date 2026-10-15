"""Logging Station — Feraldis (4x4 cells = 8 m): an open saw shed over a
saw pit, log piles, a log chute, a timber crane and a stump with an axe."""
import math
from kit import MB

NAME = "LoggingStation"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/LoggingStation/Art"
LEVELS = ["Model"]
F = 7.8


def build(tag):
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    # saw shed: four posts, plank roof, with a pit and a log on trestles
    m.leanto((-0.6, 1.4, z), 4.2, 3.0, 2.8, 2.1, mat="Fer_Plank", open_front=True)
    m.box((2.2, 1.0, 0.1), (-0.6, 1.4, z), "Fer_Timber")
    m.box((2.0, 0.8, 0.3), (-0.6, 1.4, z - 0.2), "Fer_Timber")
    for sx in (-1, 1):
        m.beam((-0.6 + sx * 1.3 - 0.3, 1.4, z), (-0.6 + sx * 1.3 + 0.3, 1.4, z + 1.0), t=0.08)
        m.beam((-0.6 + sx * 1.3 + 0.3, 1.4, z), (-0.6 + sx * 1.3 - 0.3, 1.4, z + 1.0), t=0.08)
    m.cyl(0.22, 3.4, (-0.6, 1.4, z + 1.1), "Fer_Plank", segs=7, rot=(0, math.pi / 2, 0), anchor_bottom=False)
    # a great saw blade
    m.cyl(0.5, 0.05, (0.2, 1.4, z + 1.1), "Fer_Iron", segs=12, rot=(0, math.pi / 2, 0), anchor_bottom=False)
    # log piles
    m.log_pile((2.7, 1.6, z), n=6, length=2.6, r=0.2, yaw=math.pi / 2)
    m.log_pile((2.3, -2.4, z), n=3, length=2.0, r=0.2, yaw=0)
    # log chute down to the yard
    m.wedge((1.2, 3.0, 1.1), (-2.9, -1.2, z), "Fer_Plank")
    m.box((1.2, 3.0, 0.08), (-2.9, -1.2, z + 0.02), "Fer_Timber")
    m.cyl(0.18, 1.6, (-2.9, -1.0, z + 0.7), "Fer_Plank", segs=6, rot=(0.35, 0, 0), anchor_bottom=False)
    # crane
    m.post((0.9, -1.6, z), 3.6, r=0.15)
    m.beam((0.9, -1.6, z + 3.5), (-0.9, -2.8, z + 2.7), t=0.12)
    m.beam((0.9, -1.6, z + 2.2), (0.0, -2.2, z + 3.1), t=0.08)
    m.chain((-0.85, -2.75, z + 2.7), (-0.85, -2.75, z + 1.2))
    m.cyl(0.2, 1.6, (-0.85, -2.75, z + 1.0), "Fer_Plank", segs=6, rot=(0, math.pi / 2, 0.4), anchor_bottom=False)
    # stump with an axe, crates, blood
    m.cyl(0.4, 0.6, (-2.4, -3.0, z), "Fer_Plank", segs=8, r2=0.35)
    m.axe((-2.3, -3.0, z + 0.55), rot=(0.5, 0.2, 0), s=1.0)
    m.crate((3.2, -0.3, z), s=0.65); m.crate((3.2, -0.3, z + 0.65), s=0.5, yaw=0.3)
    m.disc(0.7, (1.0, -3.1, z), "Fer_Blood", segs=10, jitter=0.4)
    m.brazier((-3.3, 3.2, z), r=0.3, h=0.7)
    m.stake_row((-3.6, 3.5, z), (-1.2, 3.5, z), h=1.2)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((3.4, -3.4, z), h=3.4, w=0.65, drop=1.3)
    objs.append(b.build())
    return objs
