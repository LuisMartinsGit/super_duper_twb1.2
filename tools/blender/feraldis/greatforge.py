"""Great Forge — Feraldis-only, one per faction (5x5 cells = 10 m). A long
iron-roofed hall pierced by a great riveted furnace drum and its stack, with
FOUR BAYS at the plinth corners (exported as Bay1..Bay4 so each is a visible
slot). Lv2 raises every stack and hangs crucibles; Lv3 adds the towering
central stack, pipes between stacks and rune obelisks. Lv0 is the
construction frame."""
import math
from kit import MB, construction_frame

NAME = "GreatForge"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/GreatForge/Art"
LEVELS = ["Lv0", "Lv1", "Lv2", "Lv3"]
F = 9.8
HALL_L, HALL_W, WALL_H, ROOF_H = 8.0, 3.8, 2.3, 1.5


def bay(idx, lvl, z):
    """One furnace bay at a plinth corner, mouth facing outward."""
    b = MB(f"Bay{idx}")
    sx = -1 if idx in (1, 4) else 1
    sy = -1 if idx in (1, 2) else 1
    x, y = sx * 3.5, sy * 3.2
    yaw = 0.0 if sy < 0 else math.pi
    b.box((2.0, 1.6, 1.5), (x, y, z), "Fer_Stone")
    b.box((2.1, 1.7, 0.3), (x, y, z + 1.5), "Fer_Iron")
    b.band((2.0, 1.6, 0.14), (x, y, z + 0.7), rivets=True)
    b.furnace_mouth((x, y - sy * 0.8, z), w=1.0, h=0.9, yaw=yaw)
    b.furnace_stack((x + sx * 0.45, y + sy * 0.3, z + 1.8), r=0.3, h=1.6 + 0.9 * lvl, bands=2, segs=7)
    # anvil block beside the mouth
    b.box((0.5, 0.3, 0.5), (x - sx * 1.5, y - sy * 0.9, z), "Fer_Iron")
    b.box((0.7, 0.36, 0.16), (x - sx * 1.5, y - sy * 0.9, z + 0.5), "Fer_Iron")
    if lvl >= 2:
        b.chain((x, y, z + 1.8 + 1.6 + 0.9 * lvl), (x - sx * 1.2, y - sy * 1.2, z + 1.2))
        b.cyl(0.22, 0.3, (x - sx * 1.2, y - sy * 1.2, z + 0.9), "Fer_Iron", segs=7, r2=0.28)
        b.cyl(0.18, 0.08, (x - sx * 1.2, y - sy * 1.2, z + 1.2), "Fer_Ember", segs=7)
    return b.build()


def build(tag):
    lvl = int(tag[-1])
    if lvl == 0:
        return construction_frame(F, HALL_L, HALL_W, WALL_H, ROOF_H, axis='x')
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    # the hall: tarred timber with an iron-plated roof, no dragons — pipes instead
    m.hall((0, 0.3, z), HALL_L, HALL_W, WALL_H, ROOF_H, axis='x', roof_mat="Fer_Iron",
           dragons=False, bands=1 + lvl, door_w=1.4, door_h=1.9)
    for sx in (-1, 1):
        m.dragon_head((sx * (HALL_L / 2 + 0.35), 0.3, z + WALL_H + ROOF_H - 0.9), yaw=sx * math.pi / 2, s=0.85, mat="Fer_Iron")
    # the furnace drum through the roof
    dr = 2.0
    m.cyl(dr, 1.0, (0, 0.6, z), "Fer_Stone", segs=12)
    m.cyl(dr - 0.2, 3.6 + 0.4 * lvl, (0, 0.6, z + 1.0), "Fer_Iron", segs=12, r2=dr - 0.5)
    for i in range(3):
        m.ring_band(dr - 0.2 - 0.3 * i / 3.6 * (0.9 + i), 0.2, (0, 0.6, z + 1.6 + i * 1.1), segs=12)
    m.cyl(dr - 0.5, 0.5, (0, 0.6, z + 4.6 + 0.4 * lvl), "Fer_Iron", segs=12, r2=dr - 0.8)
    # drum glow slits
    for k in range(8):
        a = k * math.pi / 4 + 0.2
        r = dr - 0.35
        m.box((0.14, 0.5, 0.9), (math.cos(a) * r, 0.6 + math.sin(a) * r, z + 2.0), "Fer_Ember", rot=(0, 0, a))
    # central stack
    ch = {1: 3.0, 2: 4.2, 3: 7.0}[lvl]
    m.furnace_stack((0, 0.6, z + 5.1 + 0.4 * lvl), r=0.8 if lvl < 3 else 1.0, h=ch, bands=3, segs=10, taper=0.8)
    # the four bays sit outside the hall; the hall's front door faces -y
    m.disc(1.1, (-2.6, -2.4, z), "Fer_Blood", segs=10, jitter=0.4)
    m.cage((2.4, -2.4, z), size=(0.9, 0.9, 1.4))
    m.chain((2.4, -2.4, z + 1.4), (2.4, -2.4, z + 2.6))
    m.post((2.4, -2.9, z), 2.8, r=0.1)
    m.beam((2.4, -2.9, z + 2.7), (2.4, -2.3, z + 2.65), t=0.08)
    if lvl >= 2:
        # pipes from the drum to the hall's gable ends, iron ridge crest
        for sx in (-1, 1):
            m.beam((sx * 1.6, 0.6, z + 3.6), (sx * (HALL_L / 2 + 0.2), 0.3, z + WALL_H + ROOF_H - 0.3), t=0.22, mat="Fer_Rust")
        m.crest((-HALL_L / 2, 0.3, z + WALL_H + ROOF_H), (-2.4, 0.3, z + WALL_H + ROOF_H), n=5, h=0.4)
        m.crest((2.4, 0.3, z + WALL_H + ROOF_H), (HALL_L / 2, 0.3, z + WALL_H + ROOF_H), n=5, h=0.4)
        m.furnace_stack((-3.0, 0.9, z + WALL_H + ROOF_H * 0.4), r=0.3, h=3.2, bands=2, segs=7)
        m.furnace_stack((3.0, 0.9, z + WALL_H + ROOF_H * 0.4), r=0.3, h=3.2, bands=2, segs=7)
    if lvl >= 3:
        # iron crown of spikes on the stack, rune obelisks, chains to the stack
        top = z + 5.1 + 0.4 * lvl + ch
        for k in range(8):
            a = k * math.pi / 4
            m.cone(0.12, 1.0, (math.cos(a) * 1.0, 0.6 + math.sin(a) * 1.0, top + 0.3), "Fer_Iron", segs=4,
                   rot=(-math.sin(a) * 0.5, math.cos(a) * 0.5, 0))
        for sx in (-1, 1):
            m.rune_post((sx * 4.4, -4.3, z), h=2.2, w=0.45)
            m.chain((sx * 4.4, -4.3, z + 2.4), (sx * 0.9, 0.6, top - 1.0))
        m.furnace_stack((-1.9, 1.4, z + WALL_H + ROOF_H * 0.5), r=0.26, h=4.0, bands=2, segs=7)
        m.furnace_stack((1.9, 1.4, z + WALL_H + ROOF_H * 0.5), r=0.26, h=4.0, bands=2, segs=7)
    objs = [m.build()]
    for i in (1, 2, 3, 4):
        objs.append(bay(i, lvl, z))
    b = MB("Banner")
    b.banner((-1.3, -4.3, z), h=3.6 + 0.4 * lvl, w=0.7, drop=1.4)
    b.banner((1.3, -4.3, z), h=3.6 + 0.4 * lvl, w=0.7, drop=1.4)
    objs.append(b.build())
    return objs
