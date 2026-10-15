"""Hall of Axes — the Feraldis ranged building (4x4 cells = 8 m). An
antler-crowned hall at the back, a throwing yard of targets and axe racks in
front. Lv2 adds a raised throwing porch and iron bands; Lv3 an archer tower,
skulls and a spiked crest. Lv0 is the construction frame."""
import math
from kit import MB, construction_frame

NAME = "HallOfAxes"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/HallOfAxes/Art"
LEVELS = ["Lv0", "Lv1", "Lv2", "Lv3"]
F = 7.8
HALL_L, HALL_W, WALL_H, ROOF_H = 5.6, 3.4, 2.4, 2.3


def build(tag):
    lvl = int(tag[-1])
    if lvl == 0:
        return construction_frame(F, HALL_L, HALL_W, WALL_H, ROOF_H, axis='y')
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    hy = 0.9
    m.hall((0, hy, z), HALL_L, HALL_W, WALL_H + 0.15 * lvl, ROOF_H + 0.25 * lvl, axis='y',
           dragons=False, bands=0 if lvl == 1 else lvl, door_w=1.2, door_h=1.8)
    m.antlers((0, hy - HALL_L / 2 - 0.4, z + WALL_H + 0.15 * lvl + ROOF_H + 0.25 * lvl - 1.5), s=1.3)
    m.antlers((0, hy + HALL_L / 2 + 0.4, z + WALL_H + 0.15 * lvl + ROOF_H + 0.25 * lvl - 1.5), s=1.0)
    # axes crossed over the door
    m.axe((-0.35, hy - HALL_L / 2 - 0.1, z + 2.0), rot=(0, 0.6, 0), s=1.1)
    m.axe((0.35, hy - HALL_L / 2 - 0.1, z + 2.0), rot=(0, -0.6, 0), s=1.1)
    # targets along the right, racks on the left
    for i in range(2 + lvl):
        m.target((3.2, -3.2 + i * 1.5, z), h=1.5 + 0.1 * i, r=0.38)
    m.post((-3.3, -3.2, z), 1.4, r=0.08); m.post((-3.3, -1.2, z), 1.4, r=0.08)
    m.box((0.08, 2.1, 0.1), (-3.3, -2.2, z + 1.3), "Fer_Plank")
    for i in range(5):
        m.axe((-3.3, -3.0 + i * 0.42, z + 0.4), rot=(0, 0.15, 0), s=0.9)
    m.crate((-2.3, -3.0, z), s=0.6)
    m.brazier((-1.6, -2.0, z), r=0.3, h=0.7)
    m.disc(0.8, (1.4, -2.6, z), "Fer_Blood", segs=10, jitter=0.4)
    if lvl >= 2:
        # raised throwing porch across the front of the hall
        m.box((HALL_W + 1.6, 1.6, 0.5), (0, hy - HALL_L / 2 - 0.9, z), "Fer_Plank")
        for sx in (-1, 1):
            m.post((sx * (HALL_W / 2 + 0.7), hy - HALL_L / 2 - 1.6, z + 0.5), 2.3, r=0.12)
        m.wedge((HALL_W + 1.9, 1.9, 0.6), (0, hy - HALL_L / 2 - 0.9, z + 2.8), "Fer_Slate")
        m.box((HALL_W + 1.9, 1.9, 0.08), (0, hy - HALL_L / 2 - 0.9, z + 2.75), "Fer_Slate")
        m.box((HALL_W + 1.6, 0.06, 0.5), (0, hy - HALL_L / 2 - 1.7, z + 0.5), "Fer_Timber")
        for x in (-1.2, 1.2):
            m.skull((x, hy - HALL_L / 2 - 1.75, z + 1.0), s=0.26)
    if lvl >= 3:
        m.watch_platform((-2.7, 2.6, z), h=4.6, w=1.3, roof=True)
        m.crest((0, hy - HALL_L / 2, z + WALL_H + 0.45 + ROOF_H + 0.75), (0, hy + HALL_L / 2, z + WALL_H + 0.45 + ROOF_H + 0.75), n=6, h=0.4)
        m.furnace_stack((2.3, hy + 1.6, z + 1.0), r=0.26, h=3.6, bands=2, segs=7)
        m.box((1.2, 1.2, 1.0), (2.3, hy + 1.6, z), "Fer_Stone")
        m.rune_post((3.4, 3.4, z), h=1.8, w=0.4)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.3, 0.2, z), h=3.4 + 0.4 * lvl, w=0.65, drop=1.3, yaw=math.pi / 2)
    if lvl >= 2:
        b.banner((3.4, 1.6, z), h=3.2 + 0.4 * lvl, w=0.6, drop=1.2, yaw=-math.pi / 2)
    objs.append(b.build())
    return objs
