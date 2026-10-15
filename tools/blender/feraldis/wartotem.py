"""War Totem — the Feraldis blood totem (2x2 cells = 4 m). A carved
post of stacked drums with rune-lit faces and horns, skulls at its feet,
chains to iron stakes, torches, and the blood pool it drinks from."""
import math
from kit import MB

NAME = "WarTotem"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/WarTotem/Art"
LEVELS = ["Model"]
F = 3.9


def build(tag):
    m = MB("Model")
    # ground: a stone ring around a blood pool, bounds = footprint
    m.disc(F / 2, (0, 0, 0), "Fer_Stone", segs=14, thick=0.12)
    m.disc(1.5, (0, 0, 0.12), "Fer_Blood", segs=12, jitter=0.3, thick=0.05)
    z = 0.17
    # base stones
    m.pyramid(1.3, 0.5, (0, 0, z), "Fer_Stone", top=1.0)
    z += 0.5
    # four carved drums, each with a rune face and horns, narrowing upward
    for i in range(4):
        r = 0.52 - i * 0.06
        h = 1.05
        m.cyl(r, h, (0, 0, z), "Fer_Timber", segs=8, r2=r * 0.95)
        m.ring_band(r * 0.95, 0.14, (0, 0, z + h - 0.14), segs=8)
        # face: rune eyes and a mouth on the front (-y)
        for sx in (-1, 1):
            m.box((0.12, 0.08, 0.12), (sx * 0.16, -r + 0.02, z + h * 0.62), "Fer_Rune")
        m.box((0.3, 0.08, 0.08), (0, -r + 0.02, z + h * 0.3), "Fer_Rune")
        # horns
        m.horn((-r + 0.05, 0, z + h * 0.75), length=0.45 + 0.1 * i, r=0.06, rot=(0, -1.1, 0))
        m.horn((r - 0.05, 0, z + h * 0.75), length=0.45 + 0.1 * i, r=0.06, rot=(0, 1.1, 0))
        z += h
    # crown: skull with great antlers and an iron spike
    m.skull((0, -0.1, z), s=0.55)
    m.antlers((0, 0.05, z + 0.4), s=1.3)
    m.cone(0.09, 0.9, (0, 0, z + 0.55), "Fer_Iron", segs=4)
    # skulls at the foot
    for k in range(5):
        a = k * 2 * math.pi / 5 + 0.3
        m.skull((math.cos(a) * 0.95, math.sin(a) * 0.95, 0.17), s=0.28, yaw=a + math.pi / 2)
    # chains to three iron stakes
    for k in range(3):
        a = k * 2 * math.pi / 3 + 0.5
        sx, sy = math.cos(a) * 1.65, math.sin(a) * 1.65
        m.cyl(0.07, 0.9, (sx, sy, 0.12), "Fer_Iron", segs=5, r2=0.01, rot=(-math.sin(a) * 0.4, math.cos(a) * 0.4, 0))
        m.chain((sx * 0.95, sy * 0.95, 0.9), (0, 0, 2.6))
    m.torch((1.5, -1.1, 0.12), h=1.3)
    m.torch((-1.5, -1.1, 0.12), h=1.3)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((0.0, 1.55, 0.12), h=2.8, w=0.5, drop=1.1)
    objs.append(b.build())
    return objs
