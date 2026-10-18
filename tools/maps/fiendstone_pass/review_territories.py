# review_territories.py — how defensible is each Fiendstone Pass territory?
# (2026-10-18 review; reads the SHIPPED layout.json, writes review.png, and
# changes nothing the map builder reads.)
#
# Run:  python tools/maps/fiendstone_pass/review_territories.py
#
# Per territory: area, OPEN frontage (metres of border shared with a
# neighbour on the same level — ground an army walks straight across), the
# number of ramps into it, and its neighbours. Every territory pays at most
# its type's few nodes whatever its size (Territory_Claims.md §11), so a big
# territory is the same prize with more border to guard: the ownership meter
# freezes as soon as one hostile unit stands anywhere inside it.
#
# Oversized territories get a PROPOSED split (k-means on the north bank, the
# south bank turned 180 degrees like the map itself), drawn on review.png.
import json, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
L = json.load(open(os.path.join(HERE, "layout.json"), encoding="utf-8"))
N, R = L["heightRes"], L["size"] // 2
TARGET = 6000                # m2 a split territory aims at
LIMIT_FILL, LIMIT_HOME = 8000, 11000

regs = L["regions"]
grid = np.full((N, N), -1, np.int32)
for i, r in enumerate(regs):
    s = r["shape"]
    img = Image.new("L", (N, N), 0)
    ImageDraw.Draw(img).polygon([(s[j] + R, R - s[j + 1]) for j in range(0, len(s), 2)], fill=1)
    grid[np.asarray(img, bool) & (grid < 0)] = i
# the outlines are simplified polygons: close the hairline gaps between them
dist, (iy, ix) = ndimage.distance_transform_edt(grid < 0, return_indices=True)
gap = (grid < 0) & (dist <= 3)
grid[gap] = grid[iy[gap], ix[gap]]
lvl = np.array([r["level"] for r in regs])
level = np.where(grid >= 0, lvl[np.maximum(grid, 0)], -1)

# open frontage: border samples against a same-level neighbour; a ramp is a
# short stretch against a different-level one (counted from the builder's
# RAMPS list would be exact; here: neighbours on another level the map ramps to)
open_m = np.zeros(len(regs)); nbs = [set() for _ in regs]
for a, b in ((grid[:, 1:], grid[:, :-1]), (grid[1:, :], grid[:-1, :])):
    m = (a != b) & (a >= 0) & (b >= 0)
    for p, q in zip(a[m].tolist(), b[m].tolist()):
        nbs[p].add(q); nbs[q].add(p)
        if lvl[p] == lvl[q]: open_m[p] += 1; open_m[q] += 1
open_m /= 2      # each border sample is seen from both sides of the 1 m grid

K = len(regs) // 2           # north bank first, south bank its 180-degree turn
proposal = np.full((N, N), -1, np.int32)
rows = []
for i, r in enumerate(regs):
    area = int((grid == i).sum())
    home = r["resources"] == "Start"
    limit = LIMIT_HOME if home else LIMIT_FILL
    k = 1 if area <= limit or home else int(round(area / TARGET))
    rows.append((r["code"], r["name"], r["resources"], area, int(open_m[i]), len(nbs[i]), k))
    if i >= K or k <= 1: continue
    yy, xx = np.nonzero(grid == i)
    P = np.stack([yy, xx], 1).astype(float)
    rng = np.random.default_rng(i)
    C = P[rng.choice(len(P), k, replace=False)]
    for _ in range(40):
        lab = np.argmin(((P[:, None, :] - C[None]) ** 2).sum(-1), 1)
        C = np.array([P[lab == j].mean(0) if (lab == j).any() else C[j] for j in range(k)])
    proposal[yy, xx] = i * 16 + lab
    proposal[N - 1 - yy, N - 1 - xx] = (i + K) * 16 + lab        # the south bank

# ── picture: the shipped preview, proposed new borders in red ──
base = Image.open(os.path.join(HERE, "preview.png")).convert("RGB")
mapimg = np.asarray(base.crop((0, 0, N, N))).astype(np.float32)
over = np.zeros((N, N), bool)
pr = np.where(proposal >= 0, proposal, -1)
over[:, 1:] |= (pr[:, 1:] != pr[:, :-1]) & (pr[:, 1:] >= 0) & (pr[:, :-1] >= 0) & ((pr[:, 1:] // 16) == (pr[:, :-1] // 16))
over[1:, :] |= (pr[1:, :] != pr[:-1, :]) & (pr[1:, :] >= 0) & (pr[:-1, :] >= 0) & ((pr[1:, :] // 16) == (pr[:-1, :] // 16))
over = ndimage.binary_dilation(over, iterations=1)
big = np.zeros((N, N), bool)
for i, row in enumerate(rows):
    if row[6] > 1: big |= grid == i
mapimg[big] = mapimg[big] * 0.82 + np.array([255, 120, 90]) * 0.18
mapimg[over] = (225, 30, 30)
canvas = Image.new("RGB", (N + 520, N), (236, 232, 222))
canvas.paste(Image.fromarray(np.clip(mapimg, 0, 255).astype(np.uint8)), (0, 0))
d = ImageDraw.Draw(canvas)
try:
    font = ImageFont.truetype("arial.ttf", 12); big_f = ImageFont.truetype("arialbd.ttf", 20); bold = ImageFont.truetype("arialbd.ttf", 12)
except Exception:
    font = big_f = bold = ImageFont.load_default()
lx, y = N + 14, 14
d.text((lx, y), "Fiendstone Pass - territory review", fill=(0, 0, 0), font=big_f); y += 28
d.text((lx, y), "Red lines: proposed splits (~6k m2 each).", fill=(160, 0, 0), font=font); y += 16
d.text((lx, y), "Open = m of border on the same level (no cliff).", fill=(0, 0, 0), font=font); y += 22
d.text((lx, y), f"{'territory':22} {'type':15} {'m2':>6} {'open':>5} nb  split", fill=(0, 0, 0), font=bold); y += 16
for code, name, res, area, om, nb, k in sorted(rows[:K], key=lambda t: -t[3]):
    col = (170, 0, 0) if k > 1 else (0, 0, 0)
    d.text((lx, y), f"{name[:22]:22} {res[:15]:15} {area:6} {om:5} {nb:2}  {'x' + str(k) if k > 1 else ''}", fill=col, font=font); y += 15
d.text((lx, y + 6), "(north bank; the south bank is identical)", fill=(0, 0, 0), font=font)
canvas.save(os.path.join(HERE, "review.png"))

print(f"{'code':7} {'name':22} {'type':16} {'area':>6} {'open':>5} {'nb':>3} split")
for code, name, res, area, om, nb, k in sorted(rows[:K], key=lambda t: -t[3]):
    print(f"{code:7} {name:22} {res:16} {area:6} {om:5} {nb:3} {'x' + str(k) if k > 1 else ''}")
total = sum(r[6] for r in rows)
print(f"territories now {len(regs)}, with the splits {total}")
