"""Mine — the Feraldis iron mine (2x2 cells = 4 m), staffed by slaves. A
timber pit-head over a shaft with a chain winch, ore carts, a slave stake
and a stack. Lv2 adds an iron-banded head frame and a hoist wheel; Lv3 a
furnace, a taller frame and an iron cage lift."""
import math
from kit import MB

NAME = "Mine"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/Mine/Art"
LEVELS = ["Lv1", "Lv2", "Lv3"]
F = 3.9


def build(tag):
    lvl = int(tag[-1])
    m = MB("Model")
    m.box((F, F, 0.16), (0, 0, 0), "Fer_Stone")
    z = 0.16
    # the shaft mouth: a dark square hole with a timber collar
    m.box((1.5, 1.5, 0.05), (0, 0.3, z), "Fer_Timber")
    m.band((1.5, 1.5, 0.25), (0, 0.3, z), mat="Fer_Plank", t=0.14, rivets=False)
    # head frame: four legs to an apex beam carrying the winch wheel
    fh = 2.6 + 0.5 * lvl
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.beam((sx * 0.95, 0.3 + sy * 0.95, z), (sx * 0.35, 0.3 + sy * 0.35, z + fh), t=0.13,
                   mat="Fer_Timber" if lvl < 2 else "Fer_Iron")
    m.box((1.1, 0.2, 0.2), (0, 0.3, z + fh), "Fer_Timber" if lvl < 2 else "Fer_Iron")
    m.cyl(0.4 + 0.1 * lvl, 0.12, (0, 0.3, z + fh - 0.2), "Fer_Iron", segs=10, rot=(math.pi / 2, 0, 0), anchor_bottom=False)
    m.cyl(0.14, 0.14, (0, 0.3, z + fh - 0.2), "Fer_Rust", segs=6, rot=(math.pi / 2, 0, 0), anchor_bottom=False)
    m.chain((0, 0.3, z + fh - 0.5), (0, 0.3, z + 0.4))
    if lvl >= 2:
        for h in (0.9, 1.9):
            m.band((1.1, 1.1, 0.1), (0, 0.3, z + h), rivets=True, t=0.05)
    if lvl >= 3:
        m.cage((0, 0.3, z + 0.3), size=(0.7, 0.7, 1.0), bars=3)
    # ore carts on a short rail
    m.box((0.08, 2.4, 0.06), (-1.3, -0.3, z), "Fer_Iron")
    m.box((0.08, 2.4, 0.06), (-0.9, -0.3, z), "Fer_Iron")
    for i in range(1 if lvl == 1 else 2):
        cy = -1.0 + i * 1.1
        m.box((0.6, 0.8, 0.5), (-1.1, cy, z + 0.2), "Fer_Iron")
        for sx in (-1, 1):
            for sy in (-1, 1):
                m.cyl(0.11, 0.08, (-1.1 + sx * 0.3, cy + sy * 0.28, z + 0.16), "Fer_Rust", segs=6, rot=(0, math.pi / 2, 0), anchor_bottom=False)
        m.sphere(0.25, (-1.1, cy, z + 0.6), "Fer_Rust", sub=1, scale=(1.1, 1.3, 0.6))
    # slave stake, blood, lean-to roof over the carts
    m.post((1.4, -1.4, z), 1.3, r=0.1)
    m.chain((1.4, -1.4, z + 1.2), (0.9, -1.0, z + 0.3))
    m.disc(0.5, (1.0, -0.9, z), "Fer_Blood", segs=8, jitter=0.4)
    m.leanto((1.2, 1.1, z), 1.3, 1.5, 2.0, 1.5, mat="Fer_Plank")
    m.crate((1.3, 1.2, z), s=0.55)
    m.log_pile((-1.3, 1.4, z), n=3, length=1.2, r=0.14, yaw=0)
    if lvl >= 2:
        m.furnace_stack((-1.5, 1.4, z + 0.5), r=0.2, h=2.2 + 0.6 * lvl, bands=2, segs=7, glow=lvl >= 3)
    if lvl >= 3:
        m.box((1.0, 0.9, 1.0), (-1.4, 1.35, z), "Fer_Stone")
        m.furnace_mouth((-1.4, 0.9, z), w=0.5, h=0.5)
        m.rune_post((1.65, 1.65, z), h=1.3, w=0.3)
        m.skull((0, 0.3, z + fh + 0.1), s=0.3)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((1.75, -1.75, z), h=2.3 + 0.3 * lvl, w=0.45, drop=0.9)
    objs.append(b.build())
    return objs
