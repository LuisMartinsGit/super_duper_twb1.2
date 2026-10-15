"""Slave Pens — the Feraldis Hut (2x2 cells = 4 m). Lv1 a stake-fenced pen
with a hide lean-to and a cage; Lv2 a taller fence, two shelters and a watch
post; Lv3 an iron cage block under a chained plank roof with a watch post
and a skull totem."""
import math
from kit import MB

NAME = "SlavePens"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/SlavePens/Art"
LEVELS = ["Lv1", "Lv2", "Lv3"]
F = 3.9   # footprint (4 m cells, a hair inside)


def build(tag):
    lvl = int(tag[-1])
    m = MB("Model")
    # ground: packed earth plinth with a blood stain
    m.box((F, F, 0.18), (0, 0, 0), "Fer_Stone")
    m.disc(0.6 + 0.2 * lvl, (0.5, -0.6, 0.18), "Fer_Blood", segs=10, jitter=0.35)
    h = F / 2
    fence_h = 1.2 + 0.3 * lvl
    # stake fence on three sides, gate at the front (-y)
    m.stake_row((-h + 0.2, -h + 0.2, 0.18), (-h + 0.2, h - 0.2, 0.18), h=fence_h)
    m.stake_row((-h + 0.2, h - 0.2, 0.18), (h - 0.2, h - 0.2, 0.18), h=fence_h)
    m.stake_row((h - 0.2, h - 0.2, 0.18), (h - 0.2, -h + 0.2, 0.18), h=fence_h)
    m.stake_row((h - 0.2, -h + 0.2, 0.18), (0.75, -h + 0.2, 0.18), h=fence_h)
    m.stake_row((-h + 0.2, -h + 0.2, 0.18), (-0.75, -h + 0.2, 0.18), h=fence_h)
    # gate frame with a hanging chain
    m.post((-0.75, -h + 0.2, 0.18), fence_h + 0.9, r=0.13)
    m.post((0.75, -h + 0.2, 0.18), fence_h + 0.9, r=0.13)
    m.box((1.8, 0.2, 0.2), (0, -h + 0.2, 0.18 + fence_h + 0.9), "Fer_Timber")
    m.chain((0, -h + 0.2, 0.18 + fence_h + 0.9), (0, -h + 0.2, 0.18 + fence_h * 0.5))
    m.skull((0, -h + 0.1, 0.18 + fence_h + 1.1), s=0.3)

    if lvl == 1:
        m.leanto((-0.6, 0.75, 0.18), 1.9, 1.5, 1.8, 1.25, mat="Fer_Hide")
        m.cage((1.1, 0.8, 0.18), size=(0.9, 0.9, 1.3))
        m.brazier((1.2, -0.7, 0.18), r=0.3, h=0.6)
    elif lvl == 2:
        m.leanto((-0.75, 0.75, 0.18), 1.8, 1.5, 2.0, 1.35, mat="Fer_Hide")
        m.leanto((0.95, 0.95, 0.18), 1.3, 1.3, 1.8, 1.25, mat="Fer_Plank")
        m.cage((1.0, -0.6, 0.18), size=(0.9, 0.9, 1.4))
        m.band((0.9, 0.9, 0.12), (1.0, -0.6, 0.18 + 0.7), rivets=False)
        m.watch_platform((-1.25, -1.0, 0.18), h=2.4, w=0.8, roof=False)
    else:
        # the iron cage block at the front-left: two tall cages under a plank roof
        m.box((2.4, 2.0, 0.25), (-0.5, -0.1, 0.18), "Fer_Stone")
        m.cage((-1.2, -0.1, 0.43), size=(1.0, 1.4, 1.8), bars=5)
        m.cage((0.2, -0.1, 0.43), size=(1.0, 1.4, 1.8), bars=5)
        for sx in (-1, 1):
            m.post((-0.5 + sx * 1.35, -1.15, 0.18), 2.9, r=0.13)
            m.post((-0.5 + sx * 1.35, 0.95, 0.18), 2.9, r=0.13)
        m.wedge((3.0, 2.5, 0.5), (-0.5, -0.1, 3.0), "Fer_Slate")
        m.box((3.0, 2.5, 0.08), (-0.5, -0.1, 2.95), "Fer_Slate")
        m.band((2.9, 2.4, 0.12), (-0.5, -0.1, 2.75), rivets=True)
        m.chain((-1.7, -0.1, 2.95), (-1.2, -0.1, 2.3))
        m.chain((0.7, -0.1, 2.95), (0.2, -0.1, 2.3))
        # watch post at the back-right corner, skull totem back-left
        m.watch_platform((1.3, 1.25, 0.18), h=2.9, w=0.8, roof=True)
        m.post((-1.5, 1.4, 0.18), 1.8, r=0.1)
        m.skull((-1.5, 1.4, 1.98), s=0.32)
        m.horn((-1.62, 1.4, 2.15), length=0.4, r=0.04, rot=(0, -0.6, 0))
        m.horn((-1.38, 1.4, 2.15), length=0.4, r=0.04, rot=(0, 0.6, 0))
    objs = [m.build()]
    b = MB("Banner")
    b.banner((h - 0.45, -h + 0.5, 0.18), h=2.4 + 0.3 * lvl, w=0.5, drop=1.0)
    objs.append(b.build())
    return objs
