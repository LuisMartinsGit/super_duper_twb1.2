"""Fortress — the Feraldis capital (5x5 cells = 10 m). Redone 2026-10-13 for
silhouette and contrast: a two-storey tarred keep-longhouse (dark tar below,
lighter plank jetty above, warm windows) on a raised two-tier stone-and-iron
plinth, a dominant furnace-forge tower, a chained iron gate in a spiked
palisade ring, dragon-prow gables and a spiked ridge. Lv2 adds a second
tower, iron plating and a side wing; Lv3 crowns the great tower, raises a
roof lantern and plants rune obelisks at every corner."""
import math
from kit import MB

NAME = "Fortress"
OUT_DIR = "Assets/GameData/TechTree/Civs/Feraldis/Buildings/Fortress/Art"
LEVELS = ["Lv1", "Lv2", "Lv3"]
F = 9.8


def tower(m, pos, r, h, bands=4, crown=True, segs=10):
    """A furnace-forge tower: stone base, riveted iron drum, vent slits, glowing crown."""
    x, y, z = pos
    m.pyramid(r * 2.6, 0.9, (x, y, z), "Fer_Stone", top=r * 2.2)
    m.band((r * 2.2, r * 2.2, 0.16), (x, y, z + 0.6), rivets=True)
    m.furnace_stack((x, y, z + 0.9), r=r, h=h, bands=bands, segs=segs, taper=0.84, glow=crown)
    # a ring of warm windows a third of the way up
    for k in range(segs // 2):
        a = k * 2 * math.pi / (segs // 2) + 0.3
        rr = r + (r * 0.84 - r) * 0.3
        m.box((0.12, 0.26, 0.5), (x + math.cos(a) * (rr + 0.02), y + math.sin(a) * (rr + 0.02), z + 0.9 + h * 0.3), "Fer_Window", rot=(0, 0, a))


def build(tag):
    lvl = int(tag[-1])
    m = MB("Model")
    # ── the plinth: two stone tiers with iron-shod edges and a front stair ──
    m.pyramid(F, 0.6, (0, 0, 0), "Fer_Stone", top=F - 0.3)
    m.band((F - 0.3, F - 0.3, 0.12), (0, 0, 0.48), mat="Fer_Iron", rivets=True, t=0.05)
    m.pyramid(8.2, 0.5, (0, 0.3, 0.6), "Fer_Stone", top=7.9)
    z = 1.1
    for i in range(4):
        m.box((2.6, 0.45, 0.15 + i * 0.15), (0, -4.3 + i * 0.45 - 0.2, 0.0), "Fer_Stone")
    for i in range(3):
        m.box((2.0, 0.4, 0.17 + i * 0.17), (0, -3.95 + i * 0.4 + 0.3, 0.6), "Fer_Stone")

    # ── the keep-longhouse: tar below, plank jetty above, slate roof ──
    hy = 0.9
    L, W = 7.4, 5.0
    lower, upper = 2.4, 2.1 + 0.15 * lvl
    roof = 3.3 + 0.3 * lvl
    m.box((L, W, lower), (0, hy, z), "Fer_Timber")
    for i in range(3):
        m.box((L + 0.04, W + 0.04, 0.07), (0, hy, z + 0.4 + i * 0.75), "Fer_Plank")
    m.band((L, W, 0.16), (0, hy, z + lower - 0.3), rivets=True)
    if lvl >= 2:
        m.band((L, W, 0.16), (0, hy, z + 0.6), rivets=True)
        for sx in (-1, 1):
            m.box((0.9, 0.12, lower - 0.3), (sx * 2.6, hy - W / 2 - 0.04, z + 0.1), "Fer_Iron")
    # jetty storey
    zu = z + lower
    m.box((L + 0.5, W + 0.5, 0.25), (0, hy, zu), "Fer_Timber")
    for i in range(7):
        m.box((0.18, 0.4, 0.3), (-L / 2 + 0.3 + i * (L - 0.6) / 6, hy - W / 2 - 0.3, zu - 0.3), "Fer_Timber")
    m.box((L + 0.3, W + 0.3, upper), (0, hy, zu + 0.25), "Fer_Plank")
    for i in range(3):
        m.box((L + 0.34, W + 0.34, 0.06), (0, hy, zu + 0.25 + 0.5 + i * 0.6), "Fer_Timber")
    for i in range(6):
        x = -L / 2 + 0.75 + i * (L - 1.5) / 5
        m.box((0.5, 0.1, 0.8), (x, hy - W / 2 - 0.2, zu + 0.9), "Fer_Window")
        m.box((0.62, 0.08, 0.1), (x, hy - W / 2 - 0.22, zu + 1.72), "Fer_Timber")
    for sx in (-1, 1):
        for j in range(2):
            m.box((0.1, 0.5, 0.8), (sx * (L / 2 + 0.2), hy - 1.0 + j * 2.0, zu + 0.9), "Fer_Window")
    # roof
    zr = zu + 0.25 + upper
    m.prism(L + 0.9, W + 1.1, roof, (0, hy, zr), "Fer_Slate", axis='x')
    for k in range(4):
        t = (k + 0.5) / 4
        half = (W / 2 + 0.55) * (1 - t)
        m.box((L + 1.0, 0.12, 0.08), (0, hy - half, zr + roof * t + 0.03), "Fer_Plank")
        m.box((L + 1.0, 0.12, 0.08), (0, hy + half, zr + roof * t + 0.03), "Fer_Plank")
    m.box((L + 1.4, 0.26, 0.26), (0, hy, zr + roof - 0.1), "Fer_Timber")
    m.dragon_head((-L / 2 - 0.5, hy, zr + roof - 1.1), yaw=-math.pi / 2, s=1.3)
    m.dragon_head((L / 2 + 0.5, hy, zr + roof - 1.1), yaw=math.pi / 2, s=1.3)
    if lvl >= 2:
        m.crest((-L / 2 + 0.3, hy, zr + roof + 0.05), (-1.4, hy, zr + roof + 0.05), n=4, h=0.5)
        m.crest((1.4, hy, zr + roof + 0.05), (L / 2 - 0.3, hy, zr + roof + 0.05), n=4, h=0.5)
    # the great gate door: iron frame, ember-lit maw, skull keystone
    m.box((2.0, 0.16, 2.6), (0, hy - W / 2 - 0.04, z + 0.05), "Fer_Ember")
    for sx in (-1, 1):
        m.box((0.3, 0.3, 2.9), (sx * 1.2, hy - W / 2 - 0.15, z), "Fer_Iron")
    m.box((2.9, 0.3, 0.35), (0, hy - W / 2 - 0.15, z + 2.75), "Fer_Iron")
    m.skull((0, hy - W / 2 - 0.3, z + 3.05), s=0.4)
    m.torch((-1.7, hy - W / 2 - 0.5, z + 0.9), h=0.9)
    m.torch((1.7, hy - W / 2 - 0.5, z + 0.9), h=0.9)

    # ── the furnace-forge tower at the FRONT-left corner, the capital's
    # landmark: it has to stand clear of the hall in the 3/4 view ──
    th = {1: 6.5, 2: 8.0, 3: 9.6}[lvl]
    tx, ty = -3.3, -2.5
    tower(m, (tx, ty, 0.6), 1.15, th, bands=4)
    m.chain((tx + 1.0, ty + 0.6, 0.6 + 0.9 + th - 0.4), (-1.6, hy, zr + roof - 0.3))
    if lvl >= 2:
        tower(m, (3.3, 3.1, z), 0.85, th - 1.8, bands=3, segs=8)
        m.chain((3.3 - 0.8, 3.1, z + 0.9 + th - 2.2), (1.0, hy, zr + roof))
    if lvl >= 3:
        top = z + 0.9 + th
        top = 0.6 + 0.9 + th
        m.cyl(1.15 * 0.84 + 0.25, 0.5, (tx, ty, top + 0.15), "Fer_Iron", segs=10, r2=1.15 * 0.84 + 0.45)
        for k in range(8):
            a = k * math.pi / 4
            m.cone(0.14, 1.3, (tx + math.cos(a) * 1.25, ty + math.sin(a) * 1.25, top + 0.5), "Fer_Iron", segs=4,
                   rot=(-math.sin(a) * 0.45, math.cos(a) * 0.45, 0))
        m.cyl(0.8, 0.3, (tx, ty, top + 0.55), "Fer_Ember", segs=10)
        # roof lantern with window strips
        m.box((2.6, 1.6, 1.1), (0.4, hy, zr + roof - 0.5), "Fer_Iron")
        for sx in (-1, 1):
            for i in range(3):
                m.box((0.45, 0.06, 0.6), (-0.4 + i * 0.8, hy + sx * 0.83, zr + roof - 0.2), "Fer_Window")
        m.prism(2.9, 1.9, 0.7, (0.4, hy, zr + roof + 0.6), "Fer_Slate", axis='x')
        m.crest((-0.8, hy, zr + roof + 1.3), (1.6, hy, zr + roof + 1.3), n=3, h=0.5)

    # ── side wing (Lv2+): a lower hall on the right with its own stack ──
    if lvl >= 2:
        m.hall((L / 2 + 0.9, hy - 1.0, z), 3.0, 2.8, 2.2, 1.7, axis='y', bands=1, door=False, dragons=False, overhang=0.3)
        m.box((0.1, 0.5, 0.7), (L / 2 + 0.9 + 1.42, hy - 1.0, z + 1.0), "Fer_Window")
        m.furnace_stack((L / 2 + 1.5, hy + 0.2, z + 2.2), r=0.3, h=3.0 + 0.6 * lvl, bands=2, segs=7)

    # ── the palisade ring with a chained iron gate at the front ──
    h = F / 2 - 0.35
    sh = 1.9
    m.stake_row((-h, -h, 0.6), (-h, h, 0.6), h=sh, spacing=0.45, r=0.12)
    m.stake_row((-h, h, 0.6), (h, h, 0.6), h=sh, spacing=0.45, r=0.12)
    m.stake_row((h, h, 0.6), (h, -h, 0.6), h=sh, spacing=0.45, r=0.12)
    m.stake_row((-h, -h, 0.6), (-1.9, -h, 0.6), h=sh, spacing=0.45, r=0.12)
    m.stake_row((1.9, -h, 0.6), (h, -h, 0.6), h=sh, spacing=0.45, r=0.12)
    for sx in (-1, 1):
        m.box((0.55, 0.55, 3.6), (sx * 1.9, -h, 0.6), "Fer_Iron")
        m.band((0.55, 0.55, 0.14), (sx * 1.9, -h, 0.6 + 1.2), mat="Fer_Rust", rivets=False)
        m.band((0.55, 0.55, 0.14), (sx * 1.9, -h, 0.6 + 2.6), mat="Fer_Rust", rivets=False)
        m.cone(0.2, 0.6, (sx * 1.9, -h, 4.2), "Fer_Iron", segs=4)
        m.skull((sx * 1.9, -h - 0.3, 3.4), s=0.36)
    m.box((4.4, 0.3, 0.3), (0, -h, 3.9), "Fer_Iron")
    m.chain((-1.6, -h, 3.75), (-0.5, -h, 3.75))
    m.chain((0.5, -h, 3.75), (1.6, -h, 3.75))
    for i in range(6):   # the open gate's bars swung to the sides
        m.cyl(0.04, 2.2, (-1.95 + 0.0, -h + 0.3 + i * 0.3, 0.6), "Fer_Iron", segs=4)
        m.cyl(0.04, 2.2, (1.95, -h + 0.3 + i * 0.3, 0.6), "Fer_Iron", segs=4)
    m.brazier((2.7, -3.3, 0.6), r=0.4, h=0.9)
    m.disc(1.0, (2.0, -1.4, 0.6), "Fer_Blood", segs=10, jitter=0.4)
    m.skull((3.4, -1.4, 0.6), s=0.3, yaw=0.4)
    m.cage((3.6, -2.2, 0.6), size=(0.9, 0.9, 1.3))
    if lvl >= 2:
        m.rune_post((3.6, -4.0, 0.6), h=2.2, w=0.5)
        m.rune_post((-4.2, 0.2, 0.6), h=2.2, w=0.5)
    if lvl >= 3:
        for sx in (-1, 1):
            m.rune_post((sx * 4.2, 4.2, 0.6), h=2.6, w=0.5)
            m.chain((sx * 4.2, 4.2, 3.2), (sx * 1.2, hy + 1.0, zr + roof + 1.0))
    objs = [m.build()]
    b = MB("Banner")
    b.banner((-1.0, -3.9, 0.6), h=4.6 + 0.5 * lvl, w=0.85, drop=2.0)
    b.banner((4.0, -3.4, 0.6), h=4.6 + 0.5 * lvl, w=0.85, drop=2.0)
    if lvl >= 2:
        b.banner((-4.3, 2.4, 0.6), h=4.8, w=0.7, drop=1.6, yaw=math.pi / 2)
        b.banner((4.3, 1.2, 0.6), h=4.8, w=0.7, drop=1.6, yaw=-math.pi / 2)
    objs.append(b.build())
    return objs
