"""Hunting Lodge — Feraldis (4x4 cells = 8 m): an antler-crowned lodge
with drying racks of hides, a meat-hanging frame, a pelt stretcher, a fire
pit and a skull pile."""
import math
from kit import MB

NAME = "HuntingLodge"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/HuntingLodge/Art"
LEVELS = ["Model"]
F = 7.8


def build(tag):
    m = MB("Model")
    m.plinth(F, F, 0.3)
    z = 0.3
    hy = 1.0
    L, W, WH, RH = 5.2, 3.4, 2.2, 2.4
    m.hall((0, hy, z), L, W, WH, RH, axis='y', dragons=False, bands=1, door_w=1.1, door_h=1.7)
    m.antlers((0, hy - L / 2 - 0.45, z + WH + RH - 1.5), s=1.5)
    m.antlers((-0.9, hy - L / 2 - 0.3, z + 1.9), s=0.7)
    m.antlers((0.9, hy - L / 2 - 0.3, z + 1.9), s=0.7)
    # drying racks (left), meat frame (right)
    m.hide_rack((-2.9, -0.4, z), w=1.8, h=1.8, yaw=math.pi / 2)
    m.hide_rack((-2.9, 1.9, z), w=1.6, h=1.6, yaw=math.pi / 2)
    m.post((2.4, -0.6, z), 2.3, r=0.1); m.post((3.4, 2.2, z), 2.3, r=0.1)
    m.beam((2.4, -0.6, z + 2.3), (3.4, 2.2, z + 2.3), t=0.09)
    for i in range(4):
        t = (i + 0.5) / 4
        px, py = 2.4 + t * 1.0, -0.6 + t * 2.8
        m.chain((px, py, z + 2.25), (px, py, z + 1.6))
        m.box((0.22, 0.18, 0.5), (px, py, z + 1.1), "Fer_Blood")
    # pelt stretcher frame in front
    m.box((1.4, 0.08, 0.08), (0.0, -2.9, z + 1.4), "Fer_Timber")
    m.box((1.4, 0.08, 0.08), (0.0, -2.9, z + 0.3), "Fer_Timber")
    m.box((0.08, 0.08, 1.2), (-0.7, -2.9, z + 0.3), "Fer_Timber")
    m.box((0.08, 0.08, 1.2), (0.7, -2.9, z + 0.3), "Fer_Timber")
    m.plane((1.1, 0.9), (0, -2.9, z + 0.85), "Fer_Hide", rot=(math.pi / 2, 0, 0), segs=(3, 3), wave=0.03)
    m.plane((1.1, 0.9), (0, -2.88, z + 0.85), "Fer_Hide", rot=(-math.pi / 2, 0, 0), segs=(3, 3), wave=0.03)
    # fire pit and skull pile
    m.disc(0.65, (-2.2, -2.7, z), "Fer_Stone", segs=10, thick=0.2)
    m.sphere(0.28, (-2.2, -2.7, z + 0.35), "Fer_Ember", sub=1, scale=(1.2, 1.2, 0.9))
    for k in range(4):
        a = k * 1.6
        m.skull((2.6 + math.cos(a) * 0.35, -2.7 + math.sin(a) * 0.35, z), s=0.26, yaw=a)
    m.skull((2.6, -2.7, z + 0.26), s=0.28, yaw=0.5)
    m.disc(0.9, (1.4, -1.6, z), "Fer_Blood", segs=10, jitter=0.4)
    m.log_pile((2.6, 3.2, z), n=3, length=1.6, r=0.16, yaw=0)
    m.stake_row((-3.6, 3.5, z), (1.2, 3.5, z), h=1.2)
    objs = [m.build()]
    b = MB("Banner")
    b.banner((3.4, -3.4, z), h=3.4, w=0.65, drop=1.3)
    objs.append(b.build())
    return objs
