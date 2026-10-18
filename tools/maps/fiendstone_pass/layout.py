# layout.py — FIENDSTONE PASS, the six-player cliff-shelf map.
# The layout source for MapBuilderFiendstonePass (Assets/Scripts/Editor).
#
# Run:  python tools/maps/fiendstone_pass/layout.py
# Writes, beside this file: preview.png (the annotated plan), heights.r16
# (1025x1025 terrain heights, south row first), nowalk.png / forest.png /
# dirt.png / fringe.png (masks, north at the top) and layout.json (territory
# outlines, kinds, resource types, starts, water). Then in Unity:
#   Waning Border > Maps > Build Fiendstone Pass
#
# 1 m per sample on a 1024 m map, origin at the centre, north up. The north
# bank is authored; the south bank is its 180-degree turn, exactly.
#
# Every feature is a SHAPE with a height level; each shape is a territory.
# Mountains fill whatever no shape covers. Borders between different levels
# are CLIFFS (a 4.5 m step, steeper than the 45-degree walk limit) except at
# the listed RAMPS (a 24 m wide slope); same-level borders are open.
#
# Levels: 0 low ground (river banks), 1 rich pockets, 2 mid plateaus + pass,
#         3 start shelves + start pockets, 4 starts.
import sys, zlib
from collections import deque
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

import os
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "preview.png")
N, R = 1025, 512             # terrain samples: world -512 .. +512, 1 m apart
RIVER_HALF, FORD_HALF = 24, 20
FORDS = [-380, 0, 380]
RAMP_HALF = 12               # 24 m chokes
ARCHER_RANGE = 10            # Archer.asset attackRange
KILLZONE_ALONG = 40


def river_y(x):
    return 34.0 * np.sin(np.asarray(x, float) / 160.0)


# ── Shapes, north bank. Painted in order (later wins). ───────────────────────
#   ("blob", name, level, cx, cz, rx, rz, wobble)          organic ellipse
#   ("lane", name, level, [(x,z),...], half_width, wobble)  corridor
#   ("low",  name, level, x0, x1, top, wobble)             river-bank band slice
SHAPES = [
    # low ground: one band from the river to the first cliff, split by
    # nearest seed; the mountain spurs below make the splits natural
    ("low", "L1", 0, -360, 70, 175),
    ("low", "L2", 0, 0, 60, 140),
    ("low", "L3", 0, 360, 70, 175),
    # mid plateaus
    ("blob", "PlW", 2, -300, 215, 140, 95, 0.12),
    ("blob", "PlE", 2, 300, 215, 140, 95, 0.12),
    # mountain spurs reaching toward the river (the bank stays open by the water)
    ("spur", "XW", -1, [(-175, 195), (-185, 150), (-168, 102)], 26, 0.35),
    ("spur", "XE", -1, [(175, 195), (185, 150), (168, 102)], 26, 0.35),
    # the mountain pass joining the plateaus
    ("lane", "PassW", 2, [(-180, 225), (-100, 245), (-30, 252)], 20, 0.25),
    ("lane", "PassE", 2, [(30, 252), (100, 245), (180, 225)], 20, 0.25),
    ("blob", "PassC", 2, 0, 255, 46, 30, 0.10),
    # start shelves and starts
    ("blob", "Sh1", 3, -370, 330, 100, 58, 0.15),
    ("blob", "Sh2", 3, 0, 324, 80, 54, 0.15),
    ("blob", "Sh3", 3, 370, 330, 100, 58, 0.15),
    ("blob", "P1", 4, -385, 428, 78, 58, 0.12),
    ("blob", "P2", 4, 0, 428, 78, 58, 0.12),
    ("blob", "P3", 4, 385, 428, 78, 58, 0.12),
    # neutral rich pockets (level 1, one ramp each)
    ("blob", "Rk1", 1, -118, 135, 38, 28, 0.18),    # in the bank under the west pass
    ("blob", "Rk2", 1, 470, 205, 28, 44, 0.18),     # east rim, off the east plateau
    ("blob", "Rk3", 1, 0, 207, 42, 26, 0.18),       # under the pass centre
    ("blob", "Rk4", 1, -452, 232, 36, 52, 0.18),    # west rim, off the west plateau
    # start pockets (level 3, one ramp off the start)
    ("blob", "Sp1", 3, -468, 448, 34, 42, 0.15),
    ("blob", "Sp2", 3, 92, 452, 40, 30, 0.15),
    ("blob", "Sp3", 3, 482, 440, 26, 36, 0.15),
]
RAMPS = [
    ("P1", "Sh1", -385, 375), ("P2", "Sh2", 0, 375), ("P3", "Sh3", 385, 375),
    ("Sh1", "PlW", -330, 290), ("Sh3", "PlE", 330, 290),
    ("Sh2", "PassC", 0, 280),
    ("PlW", "L1", -380, 140), ("PlW", "L1", -225, 150),
    ("PlE", "L3", 380, 140), ("PlE", "L3", 225, 150),
    ("Rk1", "L2", -118, 105), ("Rk2", "PlE", 440, 210),
    ("Rk3", "PassC", 0, 228), ("Rk4", "PlW", -440, 225),
    ("Sp1", "P1", -455, 440), ("Sp2", "P2", 65, 445), ("Sp3", "P3", 462, 435),
]

# ── Paint ────────────────────────────────────────────────────────────────────
ys, xs = np.mgrid[0:N, 0:N]
wx = (xs - R).astype(float)  # sample points sit ON the metres, as the terrain's do
wz = (R - ys).astype(float)

def noise(scale, seed):
    rng = np.random.default_rng(seed)
    g = rng.standard_normal((N // scale + 3, N // scale + 3)).astype(np.float32)
    img = Image.fromarray(g, mode="F").resize((N + 3 * scale, N + 3 * scale), Image.BICUBIC)
    return np.asarray(img)[scale:scale + N, scale:scale + N]

edge = noise(48, 11) * 0.5 + noise(16, 12) * 0.32 + noise(6, 13) * 0.18   # fractal edge roughness
# the whole layout is drawn through a gentle warp, so no shape reads as an
# ellipse or a straight line
ux = wx + noise(150, 21) * 30 + noise(50, 22) * 10
uz = wz + noise(150, 23) * 30 + noise(50, 24) * 10
LOW = [(sh[1], sh[3], sh[4], sh[5]) for sh in SHAPES if sh[0] == "low"]

def hsh(name, k):
    return (zlib.crc32((name + str(k)).encode()) % 1000) / 1000.0

def blob(name, cx, cz, rx, rz, wob):
    a = np.arctan2((uz - cz) / rz, (ux - cx) / rx)
    r = 1.0
    for k, f in enumerate((2, 3, 5)):
        r = r + wob * (0.6 / (k + 1)) * np.sin(f * a + 6.28 * hsh(name, k))
    d = np.hypot((ux - cx) / rx, (uz - cz) / rz)
    return d + edge * 0.09 < r

def lane(name, pts, hw, wob):
    best = np.full((N, N), 1e9)
    for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
        vx, vz = bx - ax, bz - az
        t = np.clip(((ux - ax) * vx + (uz - az) * vz) / (vx * vx + vz * vz), 0, 1)
        best = np.minimum(best, np.hypot(ux - ax - t * vx, uz - az - t * vz))
    return best + edge * hw * wob < hw

_low_cache = {}
def low(name, sx, sz, top):
    if not _low_cache:
        dd = np.stack([(ux - x) ** 2 + (uz - z) ** 2 for _, x, z, _ in LOW])
        near = dd.argmin(0)
        tops = np.array([t for *_, t in LOW], float)
        _low_cache["near"] = near
        _low_cache["lim"] = ndimage.gaussian_filter(tops[near], 30) + edge * 9
    near, lim = _low_cache["near"], _low_cache["lim"]
    mine = near == [n for n, *_ in LOW].index(name)
    return mine & (wz > river_y(wx)) & (uz - river_y(ux) < lim)

region = np.full((N, N), -1, np.int32)     # north only for now
names, levels, kinds = [], [], []
for sh in SHAPES:
    kind, name, lvl = sh[0], sh[1], sh[2]
    if kind == "blob": m = blob(name, *sh[3:])
    elif kind in ("lane", "spur"): m = lane(name, *sh[3:])
    else: m = low(name, *sh[3:])
    if kind == "spur":
        region[m & (wz > river_y(wx))] = -1
        continue
    side_n = wz > river_y(wx)
    m &= side_n & (np.abs(wx) < R - 14) & (np.abs(wz) < R - 14)
    i = len(names); names.append(name); levels.append(lvl); kinds.append(kind)
    region[m] = i
K = len(names)

side = np.sign(wz - river_y(wx))
south = np.rot90(region, 2)
region = np.where(side > 0, region, np.where(south >= 0, south + K, -1))

def south_name(n):
    if n[0] == "P" and n[1:].isdigit(): return "P" + str(int(n[1:]) + 3)
    if n[:2] == "Sh": return "Sh" + str(int(n[2:]) + 3)
    if n[0] == "L" and n[1:].isdigit(): return "L" + str(int(n[1:]) + 3)
    if n[:2] == "Rk": return "Rk" + str(int(n[2:]) + 4)
    if n[:2] == "Sp": return "Sp" + str(int(n[2:]) + 3)
    return n + "'"
names = names + [south_name(n) for n in names]
levels = levels + levels
lvl_arr = np.array(levels)
# The river's edge is ragged (2026-10-17): its half-width wobbles by a few
# metres, authored on the north bank and turned 180 degrees for the south so
# both banks stay identical.
bank_wob = np.tanh(noise(40, 81) * 1.2) * 5.0 + noise(11, 82) * 1.6
bank_wob = np.where(side > 0, bank_wob, np.rot90(bank_wob, 2))
river_half = RIVER_HALF + bank_wob
river = np.abs(wz - river_y(wx)) < river_half
region[river] = -1
# Fords meander: the crossing's centre line swings and its width breathes
# with the distance across the river (rd). Both are 0-phase at the centre
# line, so the mirrored halves meet without a seam.
rd = wz - river_y(wx)
ford = np.zeros((N, N), bool)
for k, fx in enumerate(FORDS):
    swing = 7.0 * np.sin(np.abs(rd) / 11.0) * (1 if k % 2 == 0 else -1)
    half = FORD_HALF * (1 + 0.2 * np.cos(rd / 6.0)) + edge * 3.0
    ford |= river & (np.abs(wx - fx - swing) < half)
ford = np.where(side > 0, ford, np.rot90(ford, 2))
valid = region >= 0
level = np.where(valid, lvl_arr[np.maximum(region, 0)], -1)

# ── Cliffs, ramps (north computed, mirrored) ─────────────────────────────────
def shifted_pairs(a):
    out = []
    out.append((a[:, 1:], a[:, :-1], (slice(None), slice(1, None))))
    out.append((a[1:, :], a[:-1, :], (slice(1, None), slice(None))))
    return out

cliff = np.zeros((N, N), bool)
for a, b, sl in shifted_pairs(region):
    la = np.where(a >= 0, lvl_arr[np.maximum(a, 0)], -9)
    lb = np.where(b >= 0, lvl_arr[np.maximum(b, 0)], -9)
    cliff[sl] |= (a >= 0) & (b >= 0) & (la != lb)

ramp_mask = np.zeros((N, N), bool)
ramp_info = []
for an, bn, hx, hz in RAMPS:
    ia, ib = names.index(an), names.index(bn)
    m = np.zeros((N, N), bool)
    for a, b, sl in shifted_pairs(region):
        m[sl] |= ((a == ia) & (b == ib)) | ((a == ib) & (b == ia))
    yy, xx = np.nonzero(m)
    if len(yy) == 0:
        print("WARNING: no shared border", an, bn); continue
    k = np.argmin((xx - (hx + R)) ** 2 + (yy - (R - hz)) ** 2)
    cy, cx = yy[k], xx[k]
    ramp_info.append((cy, cx, ia, ib))
    ramp_mask |= (ys - cy) ** 2 + (xs - cx) ** 2 < RAMP_HALF ** 2

def mirror(m):
    return np.where(side > 0, m, np.rot90(m, 2))
cliff = mirror(cliff); ramp_mask = mirror(ramp_mask)
cliff_drawn = cliff & ~ramp_mask
passable = (valid | ford) & ~ndimage.binary_dilation(cliff_drawn, iterations=1)

start_ids = [names.index(f"P{i}") for i in range(1, 7)]
cent = {}
for r in range(len(names)):
    yy, xx = np.nonzero(region == r)
    if len(yy): cent[r] = (int(yy.mean()), int(xx.mean()))
# ── Forest (decoration, impassable, unclaimable) ─────────────────────────────
# Clumps grow at the feet of mountains and cliffs and along the river banks,
# never on a ramp's approach, a ford, a pocket or a start. North computed,
# mirrored. If any region stops being reachable the woods shrink until all are.
FOREST_COVER = 0.18          # noise threshold: lower = more forest
mtn0 = ~valid & ~river
d_mtn = ndimage.distance_transform_edt(~mtn0)
d_cliff = ndimage.distance_transform_edt(~cliff)
d_ramp = ndimage.distance_transform_edt(~ramp_mask)
d_ford = ndimage.distance_transform_edt(~ford)
d_river = ndimage.distance_transform_edt(~river)
patch_n = noise(34, 51) * 0.65 + noise(12, 52) * 0.35
is_start_like = np.array([n.startswith(("P", "Rk", "Sp")) and not n.startswith("Pl") for n in names])   # starts, pockets and the narrow pass stay clear
banned = is_start_like[np.maximum(region, 0)] & valid
near_edge = np.clip(1 - np.minimum(d_mtn, d_cliff) / 55.0, 0, 1)
near_bank = np.clip(1 - (d_river - RIVER_HALF * 0) / 26.0, 0, 1) * (d_river > 2)
want = patch_n + near_edge * 0.9 + near_bank * 0.7 - 0.95

def place(thr):
    f = valid & ~banned & (d_ramp > 40) & (d_ford > 50) & (want > thr)
    f = ndimage.binary_opening(f, iterations=2)          # no single-tree specks
    lab, n = ndimage.label(f)
    sizes = ndimage.sum(f, lab, range(1, n + 1))
    keep = np.isin(lab, 1 + np.nonzero(sizes >= 250)[0])
    return np.where(side > 0, keep, np.rot90(keep, 2))

def all_reachable(pmask):
    st = 2
    small = pmask[::st, ::st]
    lab, _ = ndimage.label(small)
    sy, sx = cent[start_ids[0]]
    root = lab[sy // st, sx // st]
    bad = []
    for r, (cy, cx) in cent.items():
        # any passable pixel of the region in the start's component?
        ys_r, xs_r = np.nonzero((region[::st, ::st] == r) & small)
        if len(ys_r) == 0 or not (lab[ys_r, xs_r] == root).any(): bad.append(names[r])
    return bad

for thr in (FOREST_COVER, 0.3, 0.42, 0.55, 0.7, 9):
    forest = place(thr)
    bad = all_reachable(passable & ~forest)
    if not bad: break
    print("forest at", thr, "cuts off", bad[:5], "- shrinking")
print("forest cover:", int(forest.sum()), "m2")
passable = passable & ~forest

kill = np.zeros((N, N), bool)
for cy, cx, ia, ib in ramp_info:
    hi, lo = (ia, ib) if lvl_arr[ia] > lvl_arr[ib] else (ib, ia)
    near = (ys - cy) ** 2 + (xs - cx) ** 2 < KILLZONE_ALONG ** 2
    up_edge = ndimage.binary_dilation(cliff & (region == hi), iterations=1) & near
    if not up_edge.any(): continue
    kill |= (region == lo) & (ndimage.distance_transform_edt(~up_edge) <= ARCHER_RANGE) & near
kill = mirror(kill)

# ── Distances ────────────────────────────────────────────────────────────────
def geodesic(sy, sx, step=2):
    small = passable[::step, ::step]
    h, w = small.shape
    dist = np.full((h, w), -1, np.int32)
    q = deque([(sy // step, sx // step)]); dist[sy // step, sx // step] = 0
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and small[ny, nx] and dist[ny, nx] < 0:
                dist[ny, nx] = dist[y, x] + 1; q.append((ny, nx))
    return dist, step

report = {}
for i, s in enumerate(start_ids):
    d, st = geodesic(*cent[s])
    report[f"P{i+1}"] = {f"P{j+1}": (None if d[cent[t][0] // st, cent[t][1] // st] < 0 else int(d[cent[t][0] // st, cent[t][1] // st] * st))
                         for j, t in enumerate(start_ids)}
# pockets: can each be reached, and through how many ramps (should be 1)?

# ── Render (natural look) ────────────────────────────────────────────────────
PLAYER = [(60, 110, 230), (220, 60, 60), (60, 170, 80), (230, 200, 40), (160, 80, 210), (240, 140, 40)]
is_pocket = np.array([n.startswith(("Rk", "Sp")) for n in names])
pocket = valid & is_pocket[np.maximum(region, 0)]
mtn = ~valid & ~river
ridge = 1 - np.abs(noise(60, 31)); ridge2 = 1 - np.abs(noise(22, 32))
dist_open = ndimage.distance_transform_edt(mtn)
elev = np.where(valid, level * 12.0 + noise(70, 33) * 1.5, 0).astype(np.float32)
elev = np.where(river, -5.0, elev)
elev = np.where(mtn, 50 + np.minimum(dist_open, 60) * 0.9 + ridge * 28 + ridge2 * 10, elev)
elev = ndimage.gaussian_filter(elev, 1.0)
gy, gx = np.gradient(elev)
lit = np.clip(1.0 + (-gx * 0.6 + gy * 0.6) * 0.045, 0.6, 1.4)
var = noise(35, 41) * 0.5 + noise(9, 42) * 0.5
LOWC = np.array([[118, 158, 84], [134, 160, 88], [150, 158, 92], [168, 160, 104], [186, 172, 122]], float)
img = np.zeros((N, N, 3), np.float32)
for l in range(5):
    m = valid & (level == l)
    img[m] = LOWC[l] * (1 + var[m, None] * 0.06)
img[pocket] = img[pocket] * 0.6 + np.array([214, 172, 72]) * 0.4
mcol = np.stack([92 + ridge * 46, 84 + ridge * 40, 76 + ridge * 34], -1)
img[mtn] = mcol[mtn]
img *= lit[..., None]
cl = ndimage.binary_dilation(cliff_drawn, iterations=1)
dcl = ndimage.distance_transform_edt(~cl)
face = valid & (dcl < 4)
img[face] = img[face] * (0.42 + 0.145 * dcl[face, None])
img[cl] = (58, 50, 46)
for i, sid in enumerate([names.index(f"P{k}") for k in range(1, 7)]):
    m = region == sid
    img[m] = img[m] * 0.78 + np.array(PLAYER[i]) * 0.22
rd = np.abs(wz - river_y(wx))
img[river] = np.stack([60 + rd[river] * 1.2, 104 + rd[river] * 1.2, 176 + rd[river] * 0.8], -1)
img[ford] = (138, 178, 200)
# forest: dark canopy, lighter crowns, a soft shadow on the south-east edge
crown = noise(5, 61) * 0.6 + noise(13, 62) * 0.4
fc = np.stack([44 + crown * 18, 78 + crown * 26, 40 + crown * 14], -1)
img[forest] = fc[forest]
fsh = ndimage.binary_dilation(forest, iterations=3) & ~forest & valid
img[np.roll(np.roll(fsh, 2, 0), 2, 1) & ~forest] *= 0.82
rm = ramp_mask & ndimage.binary_dilation(cliff, iterations=5)
img[rm] = (196, 160, 110)
img[kill & (((xs + ys) % 7) < 2)] = (210, 60, 46)
border = np.zeros((N, N), bool)
border[:, 1:] |= region[:, 1:] != region[:, :-1]; border[1:, :] |= region[1:, :] != region[:-1, :]
dash = border & valid & ~cliff & (((xs + ys) % 4) < 2)
img[dash] = img[dash] * 0.7
img = np.clip(img, 0, 255).astype(np.uint8)

canvas = Image.new("RGB", (N + 340, N), (236, 232, 222))
canvas.paste(Image.fromarray(img), (0, 0))
d = ImageDraw.Draw(canvas)
try:
    font = ImageFont.truetype("arial.ttf", 13); big = ImageFont.truetype("arialbd.ttf", 22); bold = ImageFont.truetype("arialbd.ttf", 14)
except Exception:
    font = big = bold = ImageFont.load_default()
for r, (cy, cx) in cent.items():
    n = names[r]
    if n[0] == "P" and n[1:].isdigit():
        i = int(n[1:]) - 1
        d.ellipse((cx - 15, cy - 15, cx + 15, cy + 15), fill=PLAYER[i], outline=(0, 0, 0), width=2)
        d.text((cx, cy), n[1:], fill=(255, 255, 255), font=big, anchor="mm")
    else:
        d.text((cx, cy), n.replace("'", ""), fill=(20, 20, 20), font=bold if n.startswith(("Rk", "Sp")) else font, anchor="mm")
for fx in FORDS:
    d.text((fx + R, R - float(river_y(fx)) - 30), "ford", fill=(20, 40, 90), font=font, anchor="mm")

lx, y = N + 18, 18
d.text((lx, y), "Fiendstone Pass", fill=(0, 0, 0), font=big); y += 38
LCOL = {l: tuple(int(c) for c in LOWC[l]) for l in range(5)}
for lab, col in [("Start (level 4, player tint)", LCOL[4]), ("Start shelf (level 3)", LCOL[3]),
                 ("Plateau / pass (level 2)", LCOL[2]), ("Low ground (level 0)", LCOL[0]),
                 ("Rich pocket (gold)", (200, 170, 84)), ("Mountain (unclaimable)", (140, 130, 124)),
                 ("Forest (impassable)", (52, 90, 46)),
                 ("River (unclaimable)", (66, 110, 180)), ("Ford / shallows", (138, 178, 200))]:
    d.rectangle((lx, y, lx + 22, y + 16), fill=col, outline=(0, 0, 0)); d.text((lx + 32, y), lab, fill=(0, 0, 0), font=font); y += 22
d.line((lx, y + 8, lx + 22, y + 8), fill=(58, 50, 46), width=4); d.text((lx + 32, y), "Cliff face", fill=(0, 0, 0), font=font); y += 22
d.rectangle((lx, y, lx + 22, y + 16), fill=(196, 160, 110)); d.text((lx + 32, y), "Ramp (24 m choke)", fill=(0, 0, 0), font=font); y += 22
d.rectangle((lx, y, lx + 22, y + 16), fill=(225, 50, 40)); d.text((lx + 32, y), "Kill zone (archers above, 10 m)", fill=(0, 0, 0), font=font); y += 30
d.text((lx, y), "L = low ground, Pl = plateau, Pass = mountain", fill=(0, 0, 0), font=font); y += 18
d.text((lx, y), "pass, Sh = start shelf, Rk = rich pocket,", fill=(0, 0, 0), font=font); y += 18
d.text((lx, y), "Sp = start pocket", fill=(0, 0, 0), font=font); y += 28
nreg = len(set(region[valid].tolist()))
d.text((lx, y), f"{nreg} regions, {2 * len(ramp_info)} ramps", fill=(0, 0, 0), font=font); y += 24
d.text((lx, y), "Walking distance between starts (m):", fill=(0, 0, 0), font=font); y += 20
for n, row in report.items():
    near = sorted(v for m_, v in row.items() if m_ != n and v is not None)
    d.text((lx, y), f"{n}: " + ", ".join(f"{m_} {v}" for m_, v in row.items() if m_ != n and v is not None), fill=(0, 0, 0), font=font); y += 18
unreach = [(a, b) for a, r in report.items() for b, v in r.items() if v is None]
d.text((lx, y + 6), "All starts connected" if not unreach else f"UNREACHABLE {unreach[:3]}",
       fill=(0, 120, 0) if not unreach else (200, 0, 0), font=font)
canvas.save(OUT)
print("saved", OUT, nreg, "regions")
for n, row in report.items(): print(n, row)
# every pocket reachable from a start?
d0, st0 = geodesic(*cent[start_ids[0]])
for r, n in enumerate(names):
    if n.startswith(("Rk", "Sp")) and r in cent:
        cy, cx = cent[r]
        print("pocket", n, "reach" if d0[cy // st0, cx // st0] >= 0 else "UNREACHABLE")

# ═════════════════════════════════════════════════════════════════════════════
# EXPORT — what MapBuilderFiendstonePass reads
# ═════════════════════════════════════════════════════════════════════════════
import json
from skimage import measure

MAX_H = 60.0                 # TerrainData height for a normalized 1.0
LEVEL_H = np.array([5.0, 9.5, 14.0, 18.5, 23.0])   # per level; 4.5 m = a cliff
WATER = 3.6                  # WaterPlane level: more than WADE below it nothing walks
BED = 1.2                    # river bed
WADE = 0.5                   # WaterPlane.wadeDepth: ground this shallow is still walked
FORD_H = 3.25                # the fords: 0.35 m UNDER the surface, waded (2026-10-17)
MTN_BASE = 29.0              # mountains start well above the top shelf: a wall
RAMP_DEPTH = 12              # m a ramp reaches into each side (24 m long)

mtn = ~valid & ~river
h = np.zeros((N, N), np.float32)
h[valid] = LEVEL_H[level[valid]]
d_in = ndimage.distance_transform_edt(mtn)
h[mtn] = (MTN_BASE + np.minimum(d_in, 40) * 0.4 + ridge * 8 + ridge2 * 3)[mtn]

# River: a bed, then a bank of varying width and rough slope up to the low
# ground (2026-10-17: was a straight 8 m smoothstep).
rdist = np.abs(wz - river_y(wx))
bank_w = 8.0 + 3.5 * np.tanh(noise(26, 83))
bank_t = np.clip((rdist - (river_half - bank_w)) / bank_w, 0, 1)
h_river = BED + (LEVEL_H[0] - BED) * (bank_t * bank_t * (3 - 2 * bank_t))
h_river += (noise(7, 84) * 0.35 + noise(3, 85) * 0.15) * bank_t * (1 - bank_t) * 4
h[river] = h_river[river]
# Fords: a bumpy top just under the surface, its sides sloping into the bed
# instead of a straight-cut bar. The top stays inside (WATER - WADE, WATER):
# wadeable everywhere, dry nowhere.
d_core = ndimage.distance_transform_edt(~ford)
ford_top = np.clip(FORD_H + noise(9, 86) * 0.07 + noise(30, 87) * 0.05,
                   WATER - WADE + 0.07, WATER - 0.18)
h = np.where(river, np.maximum(h, ford_top - d_core * 0.45), h)

# Rolling ground (2026-10-17): gentle rises on the flats, never dips (so no
# land sinks under the water), fading out near cliffs and ramps (so a cliff
# keeps its full step and a ramp its slope) and toward the shore.
roll = 0.5 + 0.5 * np.tanh(noise(90, 88) * 1.0 + noise(36, 89) * 0.55 + noise(14, 90) * 0.18)
fade = np.clip((np.minimum(d_cliff, d_ramp) - 6.0) / 30.0, 0, 1)
fade_shore = np.clip((d_river - 2.0) / 40.0, 0, 1)
ROLL_AMP = np.array([5.0, 1.5, 4.0, 2.0, 1.2])    # m, per level
h = np.where(valid, h + ROLL_AMP[np.maximum(level, 0)] * roll * fade * fade_shore, h)

# Ramps (north authored, mirrored with everything else below): a straight
# slope from the low side to the high one, RAMP_HALF either side of the ramp
# centre along the border and RAMP_DEPTH deep into each region.
for cy, cx, ia, ib in ramp_info:
    hi, lo = (ia, ib) if lvl_arr[ia] > lvl_arr[ib] else (ib, ia)
    y0, y1 = max(cy - 40, 0), min(cy + 41, N)
    x0, x1 = max(cx - 40, 0), min(cx + 41, N)
    reg_w = region[y0:y1, x0:x1]
    is_hi, is_lo = reg_w == hi, reg_w == lo
    s = np.where(is_hi, ndimage.distance_transform_edt(~is_lo),
                 -ndimage.distance_transform_edt(~is_hi))
    sg = ndimage.gaussian_filter(s.astype(np.float32), 3)
    gy_, gx_ = np.gradient(sg)
    ly, lx_ = cy - y0, cx - x0
    nrm = np.array([gy_[ly, lx_], gx_[ly, lx_]]); nrm /= (np.linalg.norm(nrm) + 1e-6)
    tang = np.array([-nrm[1], nrm[0]])
    yy_, xx_ = np.mgrid[y0:y1, x0:x1]
    lateral = np.abs((yy_ - cy) * tang[0] + (xx_ - cx) * tang[1])
    zone = (is_hi | is_lo) & (np.abs(s) <= RAMP_DEPTH) & (lateral <= RAMP_HALF)
    t = np.clip((s + RAMP_DEPTH) / (2.0 * RAMP_DEPTH), 0, 1)
    ramp_h = LEVEL_H[lvl_arr[lo]] + (LEVEL_H[lvl_arr[hi]] - LEVEL_H[lvl_arr[lo]]) * t
    hw = h[y0:y1, x0:x1]
    hw[zone] = ramp_h[zone]

h = np.where(side > 0, h, np.rot90(h, 2))
h = ndimage.gaussian_filter(h, 0.6)        # anti-alias only: a cliff stays a cliff

# Check the walk rule the game uses (PassabilityGrid: 1.5 m central slope, > 1 blocks)
def slope_blocked(hh):
    st = 2  # 1.5 m step rounded to the 1 m grid: be strict, test 2 samples apart
    dx = (np.roll(hh, -st, 1) - np.roll(hh, st, 1)) / (2.0 * st)
    dz = (np.roll(hh, -st, 0) - np.roll(hh, st, 0)) / (2.0 * st)
    return np.hypot(dx, dz) > 1.0
blk = slope_blocked(h)
leaks = cliff_drawn & ~ndimage.binary_dilation(blk, iterations=2) & ~ndimage.binary_dilation(ramp_mask, iterations=3)
print("cliff samples NOT blocked by slope:", int(leaks.sum()))
ramp_blocked = ramp_mask & blk & valid
print("ramp samples blocked by slope:", int(ramp_blocked.sum()), "of", int((ramp_mask & valid).sum()))

# heights.r16: Unity's heightmap row 0 is the SOUTH edge.
hu = np.clip(h[::-1, :] / MAX_H, 0, 1)
(hu * 65535 + 0.5).astype("<u2").tofile(os.path.join(HERE, "heights.r16"))

def save_mask(name, m):
    Image.fromarray((m.astype(np.uint8) * 255)).save(os.path.join(HERE, name))

save_mask("nowalk.png", mtn | forest)
save_mask("forest.png", forest)
rampish = ramp_mask & (ndimage.binary_dilation(cliff, iterations=RAMP_DEPTH))
bankish = ndimage.binary_dilation(river, iterations=3) & ~ndimage.binary_erosion(river, iterations=9)
# the ford's sloping sides are riverbed too, not grass under the water
ford_bed = ndimage.binary_dilation(ford, iterations=7) & river
save_mask("dirt.png", rampish | ford_bed | bankish)
save_mask("fringe.png", mtn & (d_in < 7))
# Ground variation (2026-10-17), two soft 0..1 masks, mirrored like the
# heights they follow:
#   meadow.png -> the Meadow layer, which on this map is MOSS (darker, mottled):
#     the hollows between the rises, the feet of forests and cliffs, and loose
#     patches, so bright grass on the crests against dark moss in the dips
#     makes the rolling ground read from the RTS camera.
#   bare.png   -> extra Dirt weight: small worn patches of bare earth.
d_forest = ndimage.distance_transform_edt(~forest)
hollow = np.clip((0.5 - roll) * 2.4, 0, 1) * fade
moss = np.maximum(hollow, np.clip(1 - d_forest / 14.0, 0, 1) * 0.8)
moss = np.maximum(moss, np.clip(1 - np.minimum(d_cliff, d_mtn) / 10.0, 0, 1) * 0.6)
moss = np.maximum(moss, np.clip(noise(22, 96) * 1.3 - 0.6, 0, 0.75))
moss = moss * (1 - np.clip(noise(6, 97) * 0.5 + 0.1, 0, 0.45)) * valid    # mottled, never solid
bare = np.clip(noise(26, 98) * 1.5 - 1.25, 0, 0.6) * valid * (d_ramp > 14)
bare = np.maximum(bare, np.clip(noise(9, 99) * 1.2 - 1.3, 0, 0.45) * valid)
for name_, m_ in (("meadow.png", moss), ("bare.png", bare)):
    m_ = np.where(side > 0, m_, np.rot90(m_, 2))
    Image.fromarray((ndimage.gaussian_filter(m_, 1.5) * 255).astype(np.uint8)).save(os.path.join(HERE, name_))

# ── territories: outlines, kinds, resource types ─────────────────────────────
DISPLAY = {
    "L1": "Westmere Shallows", "L2": "Fiendstone Ford", "L3": "Eastmere Shallows",
    "PlW": "Greywold Tableland", "PlE": "Ashcrown Tableland",
    "PassW": "Western Saddle", "PassE": "Eastern Saddle", "PassC": "Fiendstone Pass",
    "Sh1": "Harrow Ledge", "Sh2": "Gallow Ledge", "Sh3": "Thorn Ledge",
    "P1": "Harrowhold", "P2": "Gallowhold", "P3": "Thornhold",
    "Rk1": "Sunken Vein", "Rk2": "Eastrim Hollow", "Rk3": "Saddle Crypt", "Rk4": "Westrim Hollow",
    "Sp1": "Harrow Cache", "Sp2": "Gallow Cache", "Sp3": "Thorn Cache",
    # south bank
    "L4": "Southmere Shallows", "L5": "Cinder Ford", "L6": "Reedmere Shallows",
    "PlW'": "Emberfell Tableland", "PlE'": "Blackmoor Tableland",
    "PassW'": "Ember Saddle", "PassE'": "Moor Saddle", "PassC'": "Cinder Pass",
    "Sh4": "Wrath Ledge", "Sh5": "Mourn Ledge", "Sh6": "Ash Ledge",
    "P4": "Wrathhold", "P5": "Mournhold", "P6": "Ashhold",
    "Rk5": "Drowned Vein", "Rk6": "Southrim Hollow", "Rk7": "Cinder Crypt", "Rk8": "Moorrim Hollow",
    "Sp4": "Wrath Cache", "Sp5": "Mourn Cache", "Sp6": "Ash Cache",
}
# Every territory's type is AUTHORED, the same on both banks and on both
# sides of a bank, so no seat is dealt better ground (Auto would roll them per
# match). Veilstone-rich (the cursed territories) is authored once per bank on
# the bank pocket, but only as a COUNT: every match swaps it with a random
# territory not next to a start (TerritoryResourcesConfig.randomizeCurseTerritories),
# and the map needs at least one (Territory_Claims.md §9).
TYPES = {
    "L1": "NormalIron", "L2": "NormalVeilstone", "L3": "NormalIron",
    "PlW": "NormalVeilstone", "PlE": "NormalVeilstone",
    "PassW": "Normal", "PassE": "Normal", "PassC": "Empty",   # too small for a node set
    "Sh1": "NormalIron", "Sh2": "NormalIron", "Sh3": "NormalIron",
    "Rk1": "VeilstoneRich", "Rk2": "IronRich", "Rk3": "Sanctum", "Rk4": "IronRich",
    "Sp1": "IronRich", "Sp2": "IronRich", "Sp3": "IronRich",
}
SOUTH_OF = {"L4": "L1", "L5": "L2", "L6": "L3", "Sh4": "Sh1", "Sh5": "Sh2", "Sh6": "Sh3",
            "Rk5": "Rk1", "Rk6": "Rk2", "Rk7": "Rk3", "Rk8": "Rk4", "Sp4": "Sp1", "Sp5": "Sp2", "Sp6": "Sp3"}
def resources(code):
    c = code.rstrip("'")
    if c[0] == "P" and c[1:].isdigit(): return "Start"
    return TYPES[SOUTH_OF.get(c, c)]

regions_out = []
for r, code in enumerate(names):
    m = region == r
    if not m.any(): continue
    lab, n = ndimage.label(m)
    if n > 1:   # keep the main body; slivers heal to a neighbour at runtime
        sizes = ndimage.sum(m, lab, range(1, n + 1)); m = lab == (1 + int(np.argmax(sizes)))
    padded = np.pad(m, 1).astype(np.float32)
    contours = measure.find_contours(padded, 0.5)
    c = max(contours, key=len)
    c = measure.approximate_polygon(c, tolerance=1.2)[:-1]
    shape = []
    for (row, col) in c:
        shape += [round(float(col - 1 - R), 2), round(float(R - (row - 1)), 2)]
    dt = ndimage.distance_transform_edt(m)
    sy, sx = np.unravel_index(np.argmax(dt), dt.shape)    # deepest interior point
    regions_out.append({
        "code": code, "name": DISPLAY.get(code, code), "level": int(lvl_arr[r]),
        "kind": "PlayerStart" if resources(code) == "Start" else "Normal",
        "resources": resources(code),
        "seedX": float(sx - R), "seedZ": float(R - sy),
        "shape": shape,
    })

FACTIONS = ["Blue", "Red", "Green", "Yellow", "Purple", "Orange"]
starts_out = []
for i in range(6):
    rr = next(x for x in regions_out if x["code"] == f"P{i + 1}")
    starts_out.append({"faction": FACTIONS[i], "x": rr["seedX"], "z": rr["seedZ"], "region": rr["name"]})

layout = {
    "mapName": "Fiendstone Pass", "size": 1024, "heightRes": N, "maxHeight": MAX_H,
    "levelHeights": LEVEL_H.tolist(), "rollHeights": ROLL_AMP.tolist(),
    "waterLevel": WATER, "wadeDepth": WADE,
    "waterMinZ": -float(np.max(np.abs(river_y(np.arange(-R, R + 1)))) + RIVER_HALF + 16),
    "waterMaxZ": float(np.max(np.abs(river_y(np.arange(-R, R + 1)))) + RIVER_HALF + 16),
    "regions": regions_out, "starts": starts_out,
}
json.dump(layout, open(os.path.join(HERE, "layout.json"), "w"), indent=1)
print("exported", len(regions_out), "territories, heights", float(h.min()), "..", float(h.max()))

# ── Verify on the REAL heights with the game's walk rule ─────────────────────
walk = (h > WATER - WADE) & ~blk & ~mtn & ~forest
lab_w, _ = ndimage.label(walk)
sy0, sx0 = cent[start_ids[0]]
root_w = lab_w[sy0, sx0]
bad = []
for r, code in enumerate(names):
    m = (region == r) & walk
    if not m.any() or not (lab_w[m] == root_w).any(): bad.append(code)
print("territories reachable on the real heights:", len(names) - len(bad), "of", len(names),
      "" if not bad else f"- CUT OFF: {bad}")
