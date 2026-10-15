"""Longhouse — the Feraldis hall (4x4 cells = 8 m): a long turf-roofed
house with dragon gables, a porch, a smoke hole glowing over the fire pit, a
side store and shields on the wall."""
import math
from kit import MB

NAME = "Longhouse"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/Longhouse/Art"
LEVELS = ["Model"]
F = 7.8


def build(tag):
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    hy = 0.6
    L, W, WH, RH = 7.0, 3.6, 2.2, 2.6
    m.hall((0, hy, z), L, W, WH, RH, axis='x', roof_mat="Fer_Turf", bands=1, door_w=1.2, door_h=1.8)
    # porch at the door
    for sx in (-1, 1):
        m.post((sx * 1.1, hy - W / 2 - 1.3, z), 2.0, r=0.12)
    m.wedge((2.8, 1.7, 0.6), (0, hy - W / 2 - 0.75, z + 2.0), "Fer_Turf")
    m.box((2.8, 1.7, 0.08), (0, hy - W / 2 - 0.75, z + 1.95), "Fer_Plank")
    # smoke hole glow on the ridge
    m.box((1.0, 0.7, 0.3), (0.6, hy, z + WH + RH - 0.2), "Fer_Timber")
    m.box((0.7, 0.4, 0.1), (0.6, hy, z + WH + RH + 0.1), "Fer_Ember")
    # shields and antlers on the front wall
    for x in (-2.6, -1.7, 1.7, 2.6):
        m.shield((x, hy - W / 2 - 0.08, z + 1.4), r=0.33)
    m.antlers((-2.2, hy - W / 2 - 0.2, z + 1.9), s=0.7)
    # side store with a slate roof, log pile and a fire pit in the yard
    m.box((2.0, 2.2, 1.6), (2.9, -2.3, z), "Fer_Timber")
    m.prism(2.3, 2.5, 1.0, (2.9, -2.3, z + 1.6), "Fer_Slate", axis='y')
    m.log_pile((-2.6, -2.6, z), n=6, length=1.8, r=0.17, yaw=0)
    m.disc(0.7, (0.3, -2.6, z), "Fer_Stone", segs=10, thick=0.2)
    m.sphere(0.3, (0.3, -2.6, z + 0.35), "Fer_Ember", sub=1, scale=(1.2, 1.2, 0.9))
    m.stake_row((-3.6, 3.5, z), (3.6, 3.5, z), h=1.3)
    m.disc(0.8, (-1.5, -1.6, z), "Fer_Blood", segs=10, jitter=0.4)
    m.crate((1.6, -3.2, z), s=0.6)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.4, -3.4, z), h=3.6, w=0.7, drop=1.4)
    objs.append(b.build())
    return objs
