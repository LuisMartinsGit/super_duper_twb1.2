"""War Hall — the Feraldis Barracks (4x4 cells = 8 m). A shield-hung
longhouse with a training yard. Lv2 adds iron bands, a smithy annex with a
furnace stack and antler trophies; Lv3 a glowing roof lantern, twin stacks,
a spiked ridge and skull-crowned gate posts."""
import math
from kit import MB

NAME = "WarHall"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/WarHall/Art"
LEVELS = ["Lv1", "Lv2", "Lv3"]
F = 7.8


def build(tag):
    lvl = int(tag[-1])
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    # main hall along x, set back so the yard is in front
    hy = 1.1
    L, W, WH, RH = 6.2, 3.4, 2.5 + 0.2 * lvl, 2.2 + 0.3 * lvl
    m.hall((0, hy, z), L, W, WH, RH, axis='x', bands=0 if lvl == 1 else 2 + (lvl - 2), door_w=1.1, door_h=1.7)
    # shields on the front wall
    n = 3 + lvl
    for i in range(n):
        x = -L / 2 + 0.7 + i * (L - 1.4) / (n - 1)
        if abs(x) < 0.9:
            continue
        m.shield((x, hy - W / 2 - 0.08, z + 1.5), r=0.36)
    # training yard: two stake dummies and a weapon rack
    for x in (-2.4, 2.4):
        m.post((x, -2.0, z), 1.7, r=0.1)
        m.box((0.8, 0.12, 0.12), (x, -2.0, z + 1.35), "Fer_Plank")
        m.shield((x + 0.1, -2.1, z + 1.0), r=0.3)
    m.post((-0.9, -2.9, z), 1.3, r=0.08); m.post((0.9, -2.9, z), 1.3, r=0.08)
    m.box((1.9, 0.08, 0.1), (0, -2.9, z + 1.2), "Fer_Plank")
    for i in range(4):
        m.axe((-0.7 + i * 0.47, -2.95, z + 0.35), rot=(0.1, 0, 0), s=0.9)
    # braziers either side of the door
    m.brazier((-1.1, hy - W / 2 - 0.6, z), r=0.3, h=0.7)
    m.brazier((1.1, hy - W / 2 - 0.6, z), r=0.3, h=0.7)
    # blood on the yard
    m.disc(0.9, (1.6, -1.0, z), "Fer_Blood", segs=10, jitter=0.4)
    if lvl >= 2:
        # smithy annex on the right with a furnace stack
        m.box((2.0, 2.2, 1.9), (L / 2 + 0.5, hy + 0.3, z), "Fer_Timber")
        m.wedge((2.3, 2.5, 0.9), (L / 2 + 0.5, hy + 0.3, z + 1.9), "Fer_Slate")
        m.band((2.0, 2.2, 0.14), (L / 2 + 0.5, hy + 0.3, z + 0.9))
        m.furnace_mouth((L / 2 + 0.5, hy - 0.8, z), w=0.8, h=0.7)
        m.furnace_stack((L / 2 + 0.9, hy + 0.9, z + 1.9), r=0.32, h=2.8 + 0.6 * lvl, bands=2)
        m.antlers((0, hy - W / 2 - 0.4, z + WH + RH - 1.3), s=0.9)
        # gate posts with skulls
        for x in (-1.5, 1.5):
            m.post((x, -3.5, z), 2.0 + 0.3 * lvl, r=0.13)
            m.skull((x, -3.5, z + 2.0 + 0.3 * lvl), s=0.3)
    if lvl >= 3:
        # roof lantern with glowing vents + second stack + spiked ridge
        m.box((2.4, 1.3, 0.9), (0, hy, z + WH + RH - 0.6), "Fer_Iron")
        for sx in (-1, 1):
            for i in range(3):
                m.box((0.4, 0.06, 0.4), (-0.75 + i * 0.75, hy + sx * 0.68, z + WH + RH - 0.35), "Fer_Ember")
        m.prism(2.7, 1.6, 0.6, (0, hy, z + WH + RH + 0.3), "Fer_Slate", axis='x')
        m.crest((-L / 2, hy, z + WH + RH + 0.05), (-1.4, hy, z + WH + RH + 0.05), n=4)
        m.crest((1.4, hy, z + WH + RH + 0.05), (L / 2, hy, z + WH + RH + 0.05), n=4)
        m.furnace_stack((-L / 2 - 0.3, hy + 0.9, z), r=0.3, h=5.4, bands=3)
        m.chain((-L / 2 - 0.3, hy + 0.9, z + 5.3), (-1.2, hy, z + WH + RH + 0.7))
        m.rune_post((-3.4, -3.4, z), h=1.6, w=0.35)
        m.rune_post((3.4, -3.4, z), h=1.6, w=0.35)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-3.3, -1.0 if lvl < 3 else 0.6, z), h=3.4 + 0.4 * lvl, w=0.7, drop=1.4)
    if lvl >= 2:
        b.banner((3.3, -2.6, z), h=3.0 + 0.4 * lvl, w=0.6, drop=1.2)
    objs.append(b.build())
    return objs
