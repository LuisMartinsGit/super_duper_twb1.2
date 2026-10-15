"""Shelter — the Age 0 capital every faction starts with (5x5 cells = 10 m),
the Lv0 of the Fortress prefab. Culture-NEUTRAL: a survivors' refuge of
natural timber, thatch, canvas and fieldstone — a common hall, two lean-tos,
a firepit, a rough stake fence, crates and barrels. No tar, iron, bone or
carved ornament, so it reads as nobody's culture yet.

Parts are exported as separate objects numbered 1..6: BuildingRiseData
raises them in ascending order during construction (the number anywhere in
the name is the step; the old Fortress Lv0 used the same "3_Tower0" style).
"""
import math
from kit import MB

NAME = "Shelter"
OUT_DIR = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Art"
LEVELS = ["Lv0"]
F = 9.6


def build(tag):
    objs = []
    z = 0.25

    # 1 — packed-earth ground with fieldstone patches and the firepit ring
    g = MB("1_Ground")
    g.pyramid(F, z, (0, 0, 0), "Age0_Earth", top=F - 0.4)
    rng = g.rng
    for i in range(14):
        x, y = (rng.random() - 0.5) * 8.0, (rng.random() - 0.5) * 8.0
        g.disc(0.3 + rng.random() * 0.35, (x, y, z), "Age0_Stone", segs=7, jitter=0.4, thick=0.08)
    g.disc(0.9, (1.6, -2.4, z), "Age0_Stone", segs=10, thick=0.22)
    g.disc(0.55, (1.6, -2.4, z + 0.22), "Age0_Earth", segs=10, thick=0.03)
    # a worn path to the gate
    g.box((1.6, 4.2, 0.03), (0, -2.6, z), "Age0_Stone")
    objs.append(g.build())

    # 2 — rough stake fence: uneven natural stakes, wooden tips, a gap in front
    f = MB("2_Fence")
    h = F / 2 - 0.3
    def fence(a, b):
        f.stake_row(a, b, h=1.25, spacing=0.5, r=0.09, mat="Age0_Wood", jitter=0.25)
    fence((-h, -h, z), (-h, h, z))
    fence((-h, h, z), (h, h, z))
    fence((h, h, z), (h, -h, z))
    fence((-h, -h, z), (-1.3, -h, z))
    fence((1.3, -h, z), (h, -h, z))
    f.post((-1.3, -h, z), 2.0, r=0.13, mat="Age0_Wood")
    f.post((1.3, -h, z), 2.0, r=0.13, mat="Age0_Wood")
    f.box((3.0, 0.16, 0.16), (0, -h, z + 1.9), "Age0_WoodDark")
    objs.append(f.build())

    # 3 — the camp: two canvas lean-tos, crates, barrels, woodpile, trough, fire
    c = MB("3_Camp")
    c.leanto((-3.0, -1.6, z), 2.2, 2.0, 2.0, 1.3, mat="Age0_Canvas")
    c.leanto((3.2, 1.6, z), 2.0, 2.2, 1.9, 1.25, mat="Age0_Canvas")
    for i, (x, y, s) in enumerate(((3.0, -1.2, 0.7), (3.6, -0.7, 0.55), (3.0, -1.2, 0.5), (-3.4, 2.4, 0.65))):
        c.box((s, s, s), (x, y, z + (0.7 if i == 2 else 0)), "Age0_Wood", rot=(0, 0, 0.3 * i))
    for (x, y) in ((2.3, -3.6), (2.9, -3.3)):
        c.cyl(0.32, 0.8, (x, y, z), "Age0_WoodDark", segs=8)
        c.cyl(0.34, 0.08, (x, y, z + 0.25), "Age0_Rope", segs=8)
        c.cyl(0.34, 0.08, (x, y, z + 0.6), "Age0_Rope", segs=8)
    c.log_pile((-3.4, -3.4, z), n=6, length=1.6, r=0.15, yaw=0, mat="Age0_Wood")
    c.box((1.6, 0.6, 0.5), (-0.4, 3.9, z), "Age0_WoodDark")      # trough
    c.box((1.4, 0.4, 0.06), (-0.4, 3.9, z + 0.44), "Age0_Canvas")  # water
    c.sphere(0.32, (1.6, -2.4, z + 0.3), "Age0_Fire", sub=1, scale=(1.3, 1.3, 0.9))
    c.cyl(0.07, 1.1, (1.1, -2.9, z), "Age0_Wood", segs=5, rot=(0.5, 0.3, 0))
    c.cyl(0.07, 1.1, (2.1, -2.9, z), "Age0_Wood", segs=5, rot=(0.5, -0.3, 0))
    c.beam((1.1, -2.9, z + 1.0), (2.1, -2.9, z + 1.0), t=0.06, mat="Age0_Wood")
    c.box((0.5, 0.4, 0.3), (-1.4, -3.6, z), "Age0_Rope")           # sacks
    c.sphere(0.3, (-2.0, -3.7, z + 0.3), "Age0_Rope", sub=1, scale=(1, 1, 0.8))
    objs.append(c.build())

    # 4 — the common hall: post frame, wattle-and-plank walls, a doorway
    hl, hw, wh = 5.6, 3.8, 2.4
    hy = 0.9
    w = MB("4_Hall")
    w.box((hl, hw, 0.25), (0, hy, z), "Age0_Stone")
    w.box((hl - 0.2, hw - 0.2, wh), (0, hy, z + 0.25), "Age0_WoodDark")
    for i in range(5):
        x = -hl / 2 + i * hl / 4
        w.post((x, hy - hw / 2, z + 0.25), wh, r=0.13, mat="Age0_Wood")
        w.post((x, hy + hw / 2, z + 0.25), wh, r=0.13, mat="Age0_Wood")
    for j in range(2):
        w.box((hl + 0.1, 0.1, 0.12), (0, hy - hw / 2, z + 0.25 + 0.8 + j * 0.9), "Age0_Wood")
        w.box((hl + 0.1, 0.1, 0.12), (0, hy + hw / 2, z + 0.25 + 0.8 + j * 0.9), "Age0_Wood")
    w.box((1.2, 0.14, 1.9), (0, hy - hw / 2 - 0.02, z + 0.3), "Age0_Fire")   # firelit doorway
    w.box((1.5, 0.2, 0.2), (0, hy - hw / 2 - 0.05, z + 2.2), "Age0_Wood")
    for sx in (-1, 1):
        w.box((0.7, 0.12, 0.6), (sx * 1.8, hy - hw / 2 - 0.02, z + 1.3), "Age0_Fire")  # shuttered windows, lit
        w.box((0.08, 0.14, 0.7), (sx * 1.8, hy - hw / 2 - 0.05, z + 1.25), "Age0_Wood")
    objs.append(w.build())

    # 5 — the thatch: steep gable, layered thatch steps, a ridge pole, a smoke
    # hole. NOT named "*Roof*": BuildingFactionColorMarker paints any part so
    # named solid dark slate (the Art_Direction roof rule), and a survivors'
    # thatch must stay straw.
    r = MB("5_Thatch")
    rz = z + 0.25 + wh
    r.prism(hl + 0.9, hw + 0.9, 2.5, (0, hy, rz), "Age0_Thatch", axis='x')
    for k in range(4):
        t = (k + 0.5) / 4
        half = (hw / 2 + 0.45) * (1 - t)
        r.box((hl + 1.0, 0.14, 0.08), (0, hy - half, rz + 2.5 * t + 0.03), "Age0_Thatch")
        r.box((hl + 1.0, 0.14, 0.08), (0, hy + half, rz + 2.5 * t + 0.03), "Age0_Thatch")
    r.cyl(0.11, hl + 1.3, (0, hy, rz + 2.45), "Age0_Wood", segs=6, rot=(0, math.pi / 2, 0), anchor_bottom=False)
    for sx in (-1, 1):
        r.beam((sx * (hl / 2 + 0.45), hy - hw / 2 - 0.45, rz), (sx * (hl / 2 + 0.45), hy, rz + 2.5), t=0.1, mat="Age0_Wood")
        r.beam((sx * (hl / 2 + 0.45), hy + hw / 2 + 0.45, rz), (sx * (hl / 2 + 0.45), hy, rz + 2.5), t=0.1, mat="Age0_Wood")
    r.box((0.7, 0.5, 0.35), (1.2, hy, rz + 2.3), "Age0_WoodDark")
    r.box((0.5, 0.3, 0.1), (1.2, hy, rz + 2.65), "Age0_Fire")
    objs.append(r.build())

    # 6 — a pennant: the one player-colour slot
    p = MB("6_Pennant")
    p.cyl(0.06, 4.0, (-2.6, -4.0, z), "Age0_Wood", segs=6)
    p.plane((1.0, 0.45), (-2.1, -4.03, z + 3.65), "Age0_PlayerColor_Cloth", rot=(math.pi / 2, 0, 0), segs=(3, 2), wave=0.03)
    p.plane((1.0, 0.45), (-2.1, -3.99, z + 3.65), "Age0_PlayerColor_Cloth", rot=(-math.pi / 2, 0, 0), segs=(3, 2), wave=0.03)
    objs.append(p.build())
    return objs
