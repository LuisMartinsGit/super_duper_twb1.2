"""Engine Works — the Feraldis siege building (`Feraldis_SiegeYard`,
4x4 cells = 8 m). Timber gantries, a crane, big wheels, bolt racks and a
furnace. Lv2 roofs the gantry and adds a second crane; Lv3 encloses an
iron-roofed assembly hall with twin stacks. Lv0 is the frame."""
import math
from kit import MB, construction_frame

NAME = "EngineWorks"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/SiegeYard/Art"
LEVELS = ["Lv0", "Lv1", "Lv2", "Lv3"]
F = 7.8
HALL_L, HALL_W, WALL_H, ROOF_H = 6.0, 4.0, 2.6, 1.6


def build(tag):
    lvl = int(tag[-1])
    if lvl == 0:
        return construction_frame(F, HALL_L, HALL_W, WALL_H, ROOF_H, axis='x')
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    gy = 1.0
    if lvl < 3:
        # the big A-frame gantry hall
        m.gantry((0, gy, z), HALL_L, HALL_W, WALL_H + ROOF_H)
        m.gantry((0, gy, z), HALL_L - 2.4, HALL_W, WALL_H + ROOF_H)
        if lvl == 2:
            m.prism(HALL_L + 0.6, HALL_W + 0.4, 1.2, (0, gy, z + WALL_H + ROOF_H - 0.1), "Fer_Slate", axis='x')
            m.box((HALL_L + 0.6, HALL_W + 0.4, 0.1), (0, gy, z + WALL_H + ROOF_H - 0.2), "Fer_Plank")
    else:
        m.hall((0, gy, z), HALL_L, HALL_W, WALL_H, ROOF_H + 0.4, axis='x', roof_mat="Fer_Iron",
               dragons=False, bands=3, door_w=2.2, door_h=2.2)
        for sx in (-1, 1):
            m.dragon_head((sx * (HALL_L / 2 + 0.35), gy, z + WALL_H + ROOF_H + 0.4 - 0.9), yaw=sx * math.pi / 2, s=0.8, mat="Fer_Iron")
        m.crest((-HALL_L / 2, gy, z + WALL_H + ROOF_H + 0.4), (HALL_L / 2, gy, z + WALL_H + ROOF_H + 0.4), n=7, h=0.4)
        m.furnace_stack((-2.2, gy + 1.4, z + WALL_H + 0.3), r=0.3, h=4.0, bands=2, segs=7)
        m.furnace_stack((2.2, gy + 1.4, z + WALL_H + 0.3), r=0.3, h=4.0, bands=2, segs=7)
    # crane: a mast with a boom and a chain holding a catapult arm
    m.post((2.6, -1.4, z), 4.2 + 0.4 * lvl, r=0.16)
    m.beam((2.6, -1.4, z + 4.0 + 0.4 * lvl), (0.6, -2.6, z + 3.2 + 0.4 * lvl), t=0.13)
    m.beam((2.6, -1.4, z + 2.6), (1.5, -2.1, z + 3.5 + 0.4 * lvl), t=0.08)
    m.chain((0.7, -2.55, z + 3.2 + 0.4 * lvl), (0.7, -2.55, z + 1.6))
    m.cyl(0.1, 2.4, (0.7, -2.55, z + 1.5), "Fer_Plank", segs=5, rot=(0.3, 1.2, 0), anchor_bottom=False)
    m.cyl(0.25, 0.4, (-0.3, -2.9, z + 1.6), "Fer_Iron", segs=6, rot=(0.3, 1.2, 0), anchor_bottom=False)
    # a big spiked wheel pair and a chassis under construction (front-left)
    m.box((2.0, 1.1, 0.4), (-2.3, -2.2, z + 0.6), "Fer_Plank")
    m.band((2.0, 1.1, 0.12), (-2.3, -2.2, z + 0.75), rivets=True)
    for sx in (-1, 1):
        m.wheel((-2.3 + sx * 0.8, -2.2 - 0.7, z + 0.6), r=0.55)
        m.wheel((-2.3 + sx * 0.8, -2.2 + 0.7, z + 0.6), r=0.55)
    # bolt / spear rack on the right
    m.post((3.3, 1.0, z), 1.5, r=0.08); m.post((3.3, 2.8, z), 1.5, r=0.08)
    m.box((0.1, 1.9, 0.1), (3.3, 1.9, z + 1.4), "Fer_Plank")
    for i in range(5):
        m.cyl(0.035, 2.2, (3.25, 1.2 + i * 0.35, z + 0.1), "Fer_Plank", segs=4, rot=(0, -0.25, 0))
        m.cone(0.06, 0.3, (3.25 + 0.55, 1.2 + i * 0.35, z + 2.2), "Fer_Iron", segs=4, rot=(0, -0.25, 0))
    # furnace at the back-left
    m.box((1.3, 1.2, 1.2), (-3.0, 3.0, z), "Fer_Stone")
    m.furnace_mouth((-3.0, 2.4, z), w=0.7, h=0.7)
    m.furnace_stack((-3.0, 3.2, z + 1.2), r=0.28, h=2.6 + 0.6 * lvl, bands=2, segs=7)
    m.crate((3.2, -3.0, z), s=0.7); m.crate((3.2, -3.0, z + 0.7), s=0.55, yaw=0.4)
    m.disc(0.8, (0.5, -0.9, z), "Fer_Blood", segs=10, jitter=0.4)
    if lvl >= 2:
        m.gantry((-2.0, -0.2, z), 2.0, 1.6, 3.0)
        m.chain((-2.0, -0.2, z + 2.9), (-2.0, -0.2, z + 1.5))
        m.box((0.9, 0.9, 0.6), (-2.0, -0.2, z + 0.9), "Fer_Iron")
        m.band((0.9, 0.9, 0.1), (-2.0, -0.2, z + 1.15), rivets=False)
        m.rune_post((3.4, -3.4, z), h=1.6, w=0.36)
    if lvl >= 3:
        m.rune_post((-3.4, -3.4, z), h=1.9, w=0.4)
        for x in (-3.0, 3.0):
            m.skull((x, gy - HALL_W / 2 - 0.1, z + WALL_H + 0.2), s=0.28)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.4, 3.4, z), h=3.6 + 0.4 * lvl, w=0.65, drop=1.3)
    if lvl >= 2:
        b.banner((3.4, 3.4, z), h=3.6 + 0.4 * lvl, w=0.65, drop=1.3)
    objs.append(b.build())
    return objs
