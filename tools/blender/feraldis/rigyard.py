"""Rig Yard — the Feraldis cavalry / rig building (`Feraldis_Pasture`,
4x4 cells = 8 m). An open-sided stable hall with a rig chassis on spiked
wheels in the yard, a wheel rack and hitching posts. Lv2 adds a wheel hoist
gantry and a stack; Lv3 an iron-roofed hangar wing. Lv0 is the frame."""
import math
from kit import MB, construction_frame

NAME = "RigYard"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/Pasture/Art"
LEVELS = ["Lv0", "Lv1", "Lv2", "Lv3"]
F = 7.8
HALL_L, HALL_W, WALL_H, ROOF_H = 6.8, 3.6, 2.5, 1.7


def build(tag):
    lvl = int(tag[-1])
    if lvl == 0:
        return construction_frame(F, HALL_L, HALL_W, WALL_H, ROOF_H, axis='x')
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    hy = 1.4
    # open stable: posts, a back wall, plank roof
    for i in range(5):
        x = -HALL_L / 2 + i * HALL_L / 4
        m.post((x, hy - HALL_W / 2, z), WALL_H, r=0.14)
        m.post((x, hy + HALL_W / 2, z), WALL_H, r=0.14)
    m.box((HALL_L, 0.2, WALL_H), (0, hy + HALL_W / 2 - 0.1, z), "Fer_Timber")
    m.box((HALL_L + 0.2, 0.2, 0.2), (0, hy - HALL_W / 2, z + WALL_H), "Fer_Timber")
    m.box((HALL_L + 0.2, 0.2, 0.2), (0, hy + HALL_W / 2, z + WALL_H), "Fer_Timber")
    m.prism(HALL_L + 0.7, HALL_W + 0.7, ROOF_H + 0.2 * lvl, (0, hy, z + WALL_H), "Fer_Slate" if lvl < 3 else "Fer_Iron", axis='x')
    m.box((HALL_L + 1.0, 0.2, 0.2), (0, hy, z + WALL_H + ROOF_H + 0.2 * lvl - 0.08), "Fer_Timber")
    for sx in (-1, 1):
        m.dragon_head((sx * (HALL_L / 2 + 0.35), hy, z + WALL_H + ROOF_H + 0.2 * lvl - 0.9), yaw=sx * math.pi / 2, s=0.8)
    # stall dividers with hanging chains
    for i in range(1, 4):
        x = -HALL_L / 2 + i * HALL_L / 4
        m.box((0.1, HALL_W * 0.7, 1.2), (x, hy + 0.3, z), "Fer_Plank")
        m.chain((x, hy - 0.4, z + WALL_H), (x, hy - 0.4, z + 1.3))
    # the rig chassis in the yard
    m.box((2.4, 1.3, 0.5), (0.4, -1.8, z + 0.55), "Fer_Plank")
    m.box((2.6, 1.5, 0.12), (0.4, -1.8, z + 1.05), "Fer_Iron")
    m.band((2.4, 1.3, 0.12), (0.4, -1.8, z + 0.8), rivets=True)
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.wheel((0.4 + sx * 0.9, -1.8 + sy * 0.8, z + 0.55), r=0.5)
    m.beam((1.6, -1.8, z + 0.9), (3.0, -2.6, z + 0.6), t=0.1)
    m.box((0.6, 0.5, 0.4), (-0.3, -1.8, z + 1.1), "Fer_Iron")
    m.box((0.3, 0.3, 0.3), (-0.3, -1.8, z + 1.5), "Fer_Ember")
    # wheel rack on the left, hitching posts on the right
    m.box((0.12, 1.8, 1.2), (-3.2, -1.5, z), "Fer_Timber")
    for i in range(3):
        m.wheel((-2.95, -2.2 + i * 0.7, z + 0.55), r=0.5, yaw=math.pi / 2)
    for i in range(3):
        m.post((2.6 + i * 0.5, -3.3, z), 0.9, r=0.08)
    m.box((1.3, 0.08, 0.08), (3.1, -3.3, z + 0.8), "Fer_Plank")
    m.disc(0.9, (-1.8, -3.0, z), "Fer_Blood", segs=10, jitter=0.4)
    if lvl >= 2:
        m.gantry((2.4, -0.3, z), 2.2, 1.6, 3.4)
        m.chain((2.4, -0.3, z + 3.3), (2.4, -0.3, z + 1.6))
        m.wheel((2.4, -0.3, z + 1.1), r=0.5, yaw=0.3)
        m.furnace_stack((-3.2, hy + 1.3, z), r=0.28, h=3.6 + 0.5 * lvl, bands=2, segs=7)
        m.box((1.0, 1.0, 1.0), (-3.2, hy + 1.3, z), "Fer_Stone")
        m.band((HALL_L, 0.3, 0.14), (0, hy + HALL_W / 2 - 0.1, z + 1.1), rivets=True)
    if lvl >= 3:
        # iron hangar wing on the right, spiked ridge, skulls on the posts
        m.box((2.4, 3.2, 2.2), (HALL_L / 2 + 0.9, hy + 0.2, z), "Fer_Iron")
        m.band((2.4, 3.2, 0.16), (HALL_L / 2 + 0.9, hy + 0.2, z + 0.8), rivets=True)
        m.wedge((2.7, 3.5, 1.0), (HALL_L / 2 + 0.9, hy + 0.2, z + 2.2), "Fer_Slate")
        m.box((2.7, 3.5, 0.08), (HALL_L / 2 + 0.9, hy + 0.2, z + 2.15), "Fer_Slate")
        m.furnace_mouth((HALL_L / 2 + 0.9, hy - 1.4, z), w=1.0, h=0.9)
        m.crest((-HALL_L / 2, hy, z + WALL_H + ROOF_H + 0.6), (HALL_L / 2, hy, z + WALL_H + ROOF_H + 0.6), n=7, h=0.4)
        for x in (-HALL_L / 2, HALL_L / 2):
            m.skull((x, hy - HALL_W / 2, z + WALL_H), s=0.28)
        m.rune_post((-3.4, -3.4, z), h=1.8, w=0.4)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.4, 0.0, z), h=3.4 + 0.4 * lvl, w=0.65, drop=1.3, yaw=math.pi / 2)
    if lvl >= 2:
        b.banner((3.5, -3.5, z), h=3.2 + 0.4 * lvl, w=0.6, drop=1.2)
    objs.append(b.build())
    return objs
