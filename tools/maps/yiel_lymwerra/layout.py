# layout.py — YIEL LYMWÉRRA, the two-player crescent-bay map, 384 x 192 m.
#
# Run:  python tools/maps/yiel_lymwerra/layout.py
# Writes, beside this file: natural.png (the map as it would look),
# preview.png (the annotated plan), heights.r16 (HRES x HRES, south row
# first — the heightmap is square, the map is not), the 1 m masks (nowalk /
# forest / dirt / fringe / grass / sand / bare, north at the top) and
# layout.json. Then in Unity:
#   Waning Border > Maps > Build + Bake Yiel Lymwerra
#
# PLAN UNITS (2026-10-17). The plan is drawn in plan units on a 256 x 128
# field; the world is the plan times SCALE (metres per unit), horizontally
# only — heights are metres. Drawn at 256 x 128 m first, it left no legal
# ground for a home's nodes (the 26 m start clearing alone is 52 m across), so
# it was enlarged to 384 x 192 m: SCALE 1.5. Every check below that the game
# measures in metres (slopes, walking distance, areas, the legend's grid)
# converts through SCALE; the export samples the plan at world positions.
#
# Authored in METRES, origin at the centre, north up: x -128..128, z -64..64,
# sampled M times per metre. The WEST half is authored; the east half is its
# mirror (x -> -x), exactly, so both seats get the same ground.
#
# North to south: mountains (unclaimable) -> grass plateau (the starts)
# -> coastal cliff -> beach -> the sea. The plateau is cut by a crescent BAY
# of SHALLOWS (wadeable, never buildable), ringed by cliffs, open to the sea
# in the south, with the cursed ISLAND Yiel in its middle. Two ramps lead
# down into the bay; the beaches are reached only through the bay. A Sanctum
# pocket sits up in the mountains, one ramp down to the Crown.
import os
from collections import deque
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
SX, SZ = 256, 128            # plan size, plan units (x, z)
SCALE = 1.5                  # world metres per plan unit -> 384 x 192 m
WX, WZ = int(SX * SCALE), int(SZ * SCALE)
M = 4                        # samples per plan unit (plan resolution)
NX, NZ = SX * M + 1, SZ * M + 1

# ── Plan numbers (metres) ────────────────────────────────────────────────────
COAST_Z = -46.0              # mean shoreline (beach meets sea)
BEACH_W = 12.0               # beach depth, sea to cliff foot
BAY_CX, BAY_CZ, BAY_RX, BAY_RZ = 0.0, -8.0, 33.0, 34.0
ISLAND_CX, ISLAND_CZ, ISLAND_RX, ISLAND_RZ = 0.0, -5.0, 19.0, 16.0   # room for the curse's 4 outcrops
MTN_Z = 36.0                 # mean foot of the northern mountains
BAY_RAMPS = [138.0]          # degrees from east, west half; mirrored to 42
RAMP_HALF = 5.0              # 10 m wide ramps into the bay
RAMP_DEPTH = 8.0             # m each side of the bay rim
POCKET = (0.0, 53.0, 13.0, 8.0)          # the Sanctum pocket in the mountains
POCKET_RAMP_Z, POCKET_RAMP_HALF, POCKET_RAMP_DEPTH = 44.0, 4.0, 8.0

WATER, WADE = 3.6, 0.5       # WaterPlane level and wadeDepth
H_BEACH0, H_BEACH1 = 3.9, 5.2
H_GRASS = 9.0                # 3.8 m over the beach top: a cliff
H_POCKET = 13.0
H_ISLAND = 5.6
H_SHALLOW = 3.27             # 0.33 m under the surface: waded

# Plateau territories, west half: seeds of a nearest-seed split. Crown sits
# on the axis and is shared.
SEEDS = [("WStart", -98, 4), ("WHighland", -44, 26), ("WBluff", -44, -28), ("Crown", 0, 36)]

zs, xs = np.mgrid[0:NZ, 0:NX]
wx = xs / M - SX / 2
wz = SZ / 2 - zs / M


def noise(scale_m, seed):
    """Smooth noise with features about scale_m metres across."""
    s = max(2, int(scale_m * M))
    rng = np.random.default_rng(seed)
    g = rng.standard_normal((NZ // s + 3, NX // s + 3)).astype(np.float32)
    img = Image.fromarray(g, mode="F").resize(((NX // s + 3) * s, (NZ // s + 3) * s), Image.BICUBIC)
    return np.asarray(img)[s:s + NZ, s:s + NX]


def mirror(a):
    return np.where(wx <= 0, a, np.fliplr(a))


def dist_m(mask):
    """Distance in metres to the nearest True sample."""
    return ndimage.distance_transform_edt(~mask) / M


edge = noise(12, 11) * 0.5 + noise(4, 12) * 0.32 + noise(1.5, 13) * 0.18
ux = wx + noise(40, 21) * 4 + noise(12, 22) * 1.5
uz = wz + noise(40, 23) * 4 + noise(12, 24) * 1.5

# ── Masks ────────────────────────────────────────────────────────────────────
coast = COAST_Z + 2.5 * np.sin(ux / 22.0) + 1.2 * np.sin(ux / 9.0 + 1.3) + edge * 1.5
sea = uz < coast

def blob(cx, cz, rx, rz, wob, phase):
    a = np.arctan2((uz - cz) / rz, (ux - cx) / rx)
    r = 1.0 + wob * (0.6 * np.sin(2 * a + phase) + 0.3 * np.sin(3 * a + 2 * phase) + 0.2 * np.sin(5 * a + 3 * phase))
    return np.hypot((ux - cx) / rx, (uz - cz) / rz) + edge * 0.05 < r

bay = blob(BAY_CX, BAY_CZ, BAY_RX, BAY_RZ, 0.05, 1.1) & (uz >= coast)
# the MOUTH: a channel from the bowl to the sea, cut in unwarped space so the
# warp can never close it; the beaches end in horns either side of it
mouth_half = 11.0 + np.clip((coast + BEACH_W - wz) * 0.35, 0, 5) + edge * 1.2
bay |= (np.abs(wx) < mouth_half) & (wz < BAY_CZ) & (uz >= coast)
island = blob(ISLAND_CX, ISLAND_CZ, ISLAND_RX, ISLAND_RZ, 0.16, 2.3)
shallows = bay & ~island
beach = ~sea & (uz < coast + BEACH_W + edge * 2 + 1.2 * np.sin(ux / 15.0)) & ~bay

# Mountains: a wall along the north, lifted in the middle (room for the
# Crown above the bay) and in lobes over each half.
lobe = (10 * np.exp(-(ux / 26.0) ** 2) + 5 * np.exp(-((np.abs(ux) - 72) / 18.0) ** 2)
        - 4 * np.exp(-((np.abs(ux) - 112) / 14.0) ** 2))
mtn_line = MTN_Z + lobe + 2.5 * np.sin(ux / 14.0 + 0.7) + edge * 3
pocket = blob(*POCKET, 0.12, 0.4)
mountain = ((uz > mtn_line) | (wz > SZ / 2 - 3)) & ~sea & ~beach & ~pocket

grass = ~sea & ~beach & ~bay & ~mountain & ~pocket
for m in ("sea", "bay", "island", "shallows", "beach", "mountain", "pocket", "grass"):
    globals()[m] = mirror(globals()[m])

# ── Territories ──────────────────────────────────────────────────────────────
# The shallows are territories too (two, split through the island): water
# forbids building at any depth, but without them the island would border
# nothing, and a territory bordering nothing can never be claimed.
names = [s[0] for s in SEEDS] + ["WBeach", "WShallows", "Sanctum", "Isle"]
dd = np.stack([(ux - x) ** 2 + (uz - z) ** 2 for _, x, z in SEEDS])
near = dd.argmin(0)
region = np.full((NZ, NX), -1, np.int32)
region[grass] = near[grass]
region = mirror(region)
west = np.full((NZ, NX), -1, np.int32)
west[beach] = names.index("WBeach")
west[shallows] = names.index("WShallows")
region = np.where(beach | shallows, mirror(west), region)
region[pocket] = names.index("Sanctum")
region[island] = names.index("Isle")
east_of = {}
for i, n in enumerate(list(names)):
    if n.startswith("W"):
        east_of[i] = len(names); names.append("E" + n[1:])
east = region.copy()
for wi, ei in east_of.items():
    east[region == wi] = ei
region = np.where(wx > 0, east, region)

# Levels: 0 shallows (under the surface), 1 beach, 2 island, 3 plateau,
#         4 the Sanctum pocket.
LEVEL = {"Isle": 2, "Sanctum": 4}
def level_of(n):
    if n in LEVEL: return LEVEL[n]
    if n.endswith("Shallows"): return 0
    return 1 if n.endswith("Beach") else 3
lvl_arr = np.array([level_of(n) for n in names])

# ── Ramps (geometry first, so the rolling ground can fade into them) ─────────
ramp_h = np.zeros((NZ, NX), np.float32)
ramp_mask = np.zeros((NZ, NX), bool)
ramp_centres = []
for deg in BAY_RAMPS:
    a = np.radians(deg)
    nx_, nz_ = np.cos(a), np.sin(a)                     # outward from the bowl centre
    for rr in np.arange(BAY_RX * 0.6, BAY_RX * 1.5, 0.25):
        px, pz = BAY_CX + nx_ * rr, BAY_CZ + nz_ * rr
        if not bay[int((SZ / 2 - pz) * M), int((px + SX / 2) * M)]: break
    along = (wx - px) * nx_ + (wz - pz) * nz_
    lateral = np.abs(-(wx - px) * nz_ + (wz - pz) * nx_)
    zone = (np.abs(along) <= RAMP_DEPTH) & (lateral <= RAMP_HALF) & (grass | shallows)
    t = np.clip((along + RAMP_DEPTH) / (2 * RAMP_DEPTH), 0, 1)
    ramp_h = np.where(zone, H_SHALLOW + (H_GRASS - H_SHALLOW) * t, ramp_h)
    ramp_mask |= zone
    ramp_centres.append((px, pz))
# the Sanctum's ramp down to the Crown
along = wz - POCKET_RAMP_Z
zone = (np.abs(along) <= POCKET_RAMP_DEPTH) & (np.abs(wx) <= POCKET_RAMP_HALF)
ramp_h = np.where(zone, H_GRASS + (H_POCKET - H_GRASS) * np.clip((along + POCKET_RAMP_DEPTH) / (2 * POCKET_RAMP_DEPTH), 0, 1), ramp_h)
ramp_mask |= zone
grass |= zone & mountain; mountain &= ~zone
# the ramp's ground is the Sanctum's, so the pocket borders the Crown below it
region[zone & (region < 0)] = names.index("Sanctum")
ramp_mask = mirror(ramp_mask); ramp_h = mirror(ramp_h)
ramp_centres += [(-x, z) for x, z in ramp_centres] + [(0.0, POCKET_RAMP_Z)]
valid = region >= 0

# ── Heights ──────────────────────────────────────────────────────────────────
ridge = 1 - np.abs(noise(16, 31)); ridge2 = 1 - np.abs(noise(6, 32)); ridge3 = 1 - np.abs(noise(2.5, 33))
d_sea = dist_m(sea)
d_land = dist_m(~sea)
d_mtn = dist_m(mountain)
d_mtn_in = dist_m(~mountain)
low = sea | beach | bay
d_low = dist_m(low)                     # plateau distance from the cliff edge
d_ramp0 = dist_m(ramp_mask)

h = np.zeros((NZ, NX), np.float32)
h[sea] = (WATER - 0.6 - np.minimum(d_land, 20) * 0.15 + noise(8, 41) * 0.15)[sea]
bt = np.clip(d_sea / BEACH_W, 0, 1)
h_beach = H_BEACH0 + (H_BEACH1 - H_BEACH0) * (bt * bt * (3 - 2 * bt)) + noise(2.5, 42) * 0.08 * bt
h[beach] = h_beach[beach]
floor = np.clip(H_SHALLOW + noise(2.5, 43) * 0.06 + noise(8, 44) * 0.04, WATER - WADE + 0.08, WATER - 0.16)
h_sh = floor - np.clip(1 - d_sea / 3.0, 0, 1) * 1.6     # wadeable to the last few metres, then the sea
h[shallows] = h_sh[shallows]
d_isl_in = dist_m(~island)
it = np.clip(d_isl_in / 4.0, 0, 1)
h_is = (WATER - 0.1) + (H_ISLAND - WATER + 0.1) * (it * it * (3 - 2 * it)) + noise(14, 45) * 0.25 * it   # gentle: under the 15-degree build limit, so the curse's outcrops keep room for their posts
h[island] = h_is[island]
roll = 0.5 + 0.5 * np.tanh(noise(24, 46) * 1.0 + noise(9, 47) * 0.55 + noise(3.5, 48) * 0.18)
fade = np.clip((np.minimum(np.minimum(d_low, d_mtn), d_ramp0) - 2.0) / 8.0, 0, 1)
h_gr = H_GRASS + 2.0 * roll * fade
h[grass] = h_gr[grass]
h[pocket] = H_POCKET + 0.5 * roll[pocket]
h_m = 22.0 + np.minimum(d_mtn_in, 25) * 1.1 + ridge * 9 + ridge2 * 4 + ridge3 * 1.5
h[mountain] = h_m[mountain]
lip = grass & (d_low < 0.6)
h[lip] -= (np.abs(noise(1, 49)) * 0.6)[lip]
h = np.where(ramp_mask, ramp_h, h)
h = mirror(h)
h = ndimage.gaussian_filter(h, 0.6 * M / 4)

# ── Forest (decoration, impassable): clumps at the mountains' feet and on
# the cliff tops, away from the starts, the ramps and the Crown.
d_ramp = dist_m(ramp_mask)
start_ids = [names.index("WStart"), names.index("EStart")]
start_pt = {}
d_start_c = np.full((NZ, NX), 1e9)
for sid in start_ids:
    zz, xx = np.nonzero(region == sid)
    cz, cx = zz.mean(), xx.mean()
    start_pt[sid] = (int(cz), int(cx))
    d_start_c = np.minimum(d_start_c, np.hypot(zs - cz, xs - cx) / M)
patch = noise(8, 51) * 0.65 + noise(3, 52) * 0.35
near_edge = np.clip(1 - d_mtn / 14.0, 0, 1) + np.clip(1 - d_low / 6.0, 0, 1) * 0.35
want = patch + near_edge - 0.85
forest = (grass & (want > 0.2) & (d_ramp > 10) & (d_start_c > 30)
          & (region != names.index("Crown")))
forest = ndimage.binary_opening(forest, iterations=M)
lab, n = ndimage.label(forest)
sizes = ndimage.sum(forest, lab, range(1, n + 1)) / (M * M)
forest = mirror(np.isin(lab, 1 + np.nonzero(sizes >= 20)[0]))

# ── Walk check (PassabilityGrid rule: slope > 1 blocks; deeper than WADE
# under the water blocks) ─────────────────────────────────────────────────────
st = M                      # central difference over +-1 m
gx_ = (np.roll(h, -st, 1) - np.roll(h, st, 1)) / 2.0
gz_ = (np.roll(h, -st, 0) - np.roll(h, st, 0)) / 2.0
slope = np.hypot(gx_, gz_) / SCALE            # rise per WORLD metre
walk = (h > WATER - WADE) & (slope <= 1.0) & ~mountain & ~forest
lab_w, _ = ndimage.label(walk)
cent = {}
for r in range(len(names)):
    zz, xx = np.nonzero((region == r) & walk)
    if len(zz):
        k = np.argmin((zz - zz.mean()) ** 2 + (xx - xx.mean()) ** 2)
        cent[r] = (int(zz[k]), int(xx[k]))
root = lab_w[cent[start_ids[0]]]
cut = [names[r] for r in range(len(names)) if r not in cent or lab_w[cent[r]] != root]
print("territories reachable:", len(names) - len(cut), "of", len(names), "" if not cut else f"CUT OFF {cut}")
cliff = (slope > 1.0) & ~mountain & ~sea & ~ndimage.binary_dilation(mountain, iterations=3)

# adjacency (outlines touching), so nothing ends up unclaimable
adj = set()
for a_, b_ in ((region[:, 1:], region[:, :-1]), (region[1:, :], region[:-1, :])):
    mm = (a_ != b_) & (a_ >= 0) & (b_ >= 0)
    for p, q in set(zip(a_[mm].tolist(), b_[mm].tolist())): adj.add(tuple(sorted((p, q))))
lonely = [names[r] for r in range(len(names)) if not any(r in pq for pq in adj)]
print("territories bordering nothing:", lonely or "none")

def geodesic(src):
    small = walk[::M, ::M]                  # 1 m steps
    hh, ww = small.shape
    dist = np.full((hh, ww), -1, np.int32)
    s = (src[0] // M, src[1] // M)
    q = deque([s]); dist[s] = 0
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < hh and 0 <= nx < ww and small[ny, nx] and dist[ny, nx] < 0:
                dist[ny, nx] = dist[y, x] + 1; q.append((ny, nx))
    return dist
dg = geodesic(start_pt[start_ids[0]])
def walk_m(rc):
    v = dg[rc[0] // M, rc[1] // M]
    return None if v < 0 else int(v * SCALE)
start_dist = walk_m(start_pt[start_ids[1]])
isle_dist = walk_m(cent[names.index("Isle")])
print("start to start (4-way grid, overestimates):", start_dist, "m; start to island:", isle_dist, "m")
for r, nm in enumerate(names):
    print(f"  {nm:10} {int((region == r).sum() / (M * M) * SCALE * SCALE):6} m2")

# ── Natural render ───────────────────────────────────────────────────────────
rng = np.random.default_rng(7)
hs = ndimage.gaussian_filter(h, 1.0)
gy, gx = np.gradient(hs)
gy *= M; gx *= M                          # per metre
relief = np.where(mountain, 0.30, 0.12)
shade = np.clip(1.0 + (-gx + gy) * 0.6 * relief, 0.35, 1.5)
var = noise(9, 61) * 0.5 + noise(2.5, 62) * 0.5
fine = noise(0.6, 63)
img = np.zeros((NZ, NX, 3), np.float32)

g_lo, g_hi = np.array([70, 108, 46]), np.array([136, 164, 76])
gmix = np.clip(0.25 + roll * 0.6 + var * 0.15, 0, 1)[..., None]
img[:] = g_lo + (g_hi - g_lo) * gmix
moss = np.clip(noise(6, 64) * 1.3 - 0.5, 0, 0.6)[..., None]
img = img * (1 - moss * 0.4) + np.array([62, 86, 44]) * moss * 0.4
straw = np.clip(noise(10, 71) * 1.4 - 1.0, 0, 0.45)[..., None]
img = img * (1 - straw) + np.array([168, 160, 96]) * straw
img += fine[..., None] * 6
sand_dry, sand_wet = np.array([224, 204, 156]), np.array([170, 150, 110])
bw = np.clip(1 - d_sea / 2.5, 0, 1)[..., None]
sand = sand_dry * (1 - bw) + sand_wet * bw + var[..., None] * 8 + fine[..., None] * 5
img[beach] = sand[beach]
isl_sand = np.clip(1 - d_isl_in / 2.5, 0, 1)[..., None]
isl = img * (1 - isl_sand) + sand_dry * isl_sand
img[island] = isl[island]
strata = np.sin(h * 0.9 + noise(5, 73) * 2.0) * 2
rock = np.stack([104 + ridge * 40, 98 + ridge * 36, 90 + ridge * 32], -1) + (var * 8 + strata)[..., None]
snow = np.clip((h - (h[mountain].max() - 6)) / 4.0 + noise(1.5, 74) * 0.3, 0, 1)[..., None] * (gy[..., None] > -0.4)
rock = rock * (1 - snow * 0.85) + np.array([236, 238, 242]) * snow * 0.85
mfoot = np.clip(1 - d_mtn_in / 3.0, 0, 1)[..., None]
rock = rock * (1 - mfoot * 0.45) + np.array([126, 120, 100]) * mfoot * 0.45
img[mountain] = rock[mountain]
rp = ramp_mask & ~sea
img[rp] = img[rp] * 0.3 + (np.array([166, 136, 98]) + fine[rp, None] * 8) * 0.7
d_grass = dist_m(grass | pocket)
face = (grass | pocket) & (d_low < 1.2) & ~ramp_mask
face_c = np.array([96, 86, 76]) + (noise(0.8, 75) * 14)[..., None]
img[face] = face_c[face]
scree = (beach | shallows) & (d_grass < 1.0) & ~ramp_mask
img[scree] = img[scree] * 0.55 + np.array([120, 110, 96]) * 0.45
img *= shade[..., None]
cliffline = face & (d_low < 0.4)
img[cliffline] *= 0.55

# forest: individual crowns (2 m build cells), lit from the north-west
pts = forest & (rng.random((NZ, NX)) < 0.012)
dt = ndimage.distance_transform_edt(~pts) / M
canopy = np.clip(1 - (dt / 1.1) ** 2, 0, 1) ** 0.5 * forest
cy_, cx_ = np.gradient(ndimage.gaussian_filter(canopy, 0.6))
lit = np.clip(1.0 + (-cx_ + cy_) * 1.6 * M / 4, 0.55, 1.4)
tone = noise(3, 76)[..., None]
fc = (np.array([42, 74, 38]) + tone * np.array([8, 12, 6])) * (0.55 + 0.45 * canopy[..., None]) * lit[..., None]
img[forest] = fc[forest]
fsh = np.roll(np.roll(ndimage.binary_dilation(forest, iterations=2), M, 0), M, 1) & ~forest & ~sea & ~shallows
img[fsh] *= 0.72

depth = np.clip(WATER - h, 0, None)
water = sea | shallows
deep_c, mid_c, shal_c = np.array([24, 58, 104]), np.array([38, 104, 150]), np.array([98, 150, 150])
t1 = np.clip(depth / 1.2, 0, 1)[..., None]
t2 = np.clip((depth - 1.2) / 2.5, 0, 1)[..., None]
wc = shal_c * (1 - t1) + mid_c * t1
wc = wc * (1 - t2) + deep_c * t2
show = np.clip(1 - depth / 1.0, 0, 1)[..., None] * 0.55
wc = wc * (1 - show) + np.array([196, 176, 128]) * show
wc += (noise(1, 67) * 4 + noise(4.5, 68) * 5)[..., None]
img[water] = wc[water]
d_dry = dist_m(~water)
foam = water & (d_dry < 0.7)
img[foam] = img[foam] * 0.45 + np.array([236, 240, 236]) * 0.55
wave = sea & (np.abs(d_dry - 3 - noise(5, 69) * 0.8) < 0.3) & (noise(1.8, 70) > -0.2)
img[wave] = img[wave] * 0.75 + np.array([210, 226, 232]) * 0.25
foot = np.roll(np.roll(ndimage.binary_dilation(face, iterations=M), M, 0), M, 1) & (beach | water) & ~face
img[foot] *= 0.8
natural = np.clip(img, 0, 255).astype(np.uint8)
Image.fromarray(natural).save(os.path.join(HERE, "natural.png"))

# ── Annotated plan ───────────────────────────────────────────────────────────
PLAYER = [(60, 110, 230), (220, 60, 60)]
plan = natural.astype(np.float32)
for i, sid in enumerate(start_ids):
    m = region == sid
    plan[m] = plan[m] * 0.8 + np.array(PLAYER[i]) * 0.2
plan[island] = plan[island] * 0.7 + np.array([150, 60, 190]) * 0.3
border = np.zeros((NZ, NX), bool)
border[:, 1:] |= region[:, 1:] != region[:, :-1]
border[1:, :] |= region[1:, :] != region[:-1, :]
border = ndimage.binary_dilation(border) & valid
dash = border & (((xs + zs) // 3) % 2 == 0)
plan[dash] = (250, 250, 240)
# a 10 m grid, faint, so the scale reads
g10 = 10 / SCALE * M                      # 10 world metres, in plan samples
grid10 = (np.abs(((xs + g10 / 2) % g10) - g10 / 2) < 0.5) | (np.abs(((zs + g10 / 2) % g10) - g10 / 2) < 0.5)
plan[grid10] = plan[grid10] * 0.85 + 255 * 0.15
plan = np.clip(plan, 0, 255).astype(np.uint8)

PAD = 380
canvas = Image.new("RGB", (NX + PAD, max(NZ, 600)), (236, 232, 222))
canvas.paste(Image.fromarray(plan), (0, 0))
d = ImageDraw.Draw(canvas)
try:
    font = ImageFont.truetype("arial.ttf", 13); big = ImageFont.truetype("arialbd.ttf", 22)
    bold = ImageFont.truetype("arialbd.ttf", 14)
except Exception:
    font = big = bold = ImageFont.load_default()
def px(x, z): return ((x + SX / 2) * M, (SZ / 2 - z) * M)
LABEL = {"Isle": "Yiel (curse)", "Sanctum": "Sanctum", "WShallows": "shallows", "EShallows": "shallows",
         "Crown": "Crown"}
for r, (cz, cx) in cent.items():
    n = names[r]
    if n.endswith("Start"):
        cz, cx = start_pt[r]
        i = start_ids.index(r)
        # the capital's 10 x 10 m footprint, to scale
        hw = 5 / SCALE * M
        d.rectangle((cx - hw, cz - hw, cx + hw, cz + hw), fill=PLAYER[i], outline=(0, 0, 0), width=2)
        d.text((cx, cz), str(i + 1), fill=(255, 255, 255), font=big, anchor="mm")
    else:
        lbl = LABEL.get(n, n[1:] if n[0] in "WE" and n[1].isupper() else n)
        d.text((cx + 1, cz + 1), lbl, fill=(0, 0, 0), font=bold, anchor="mm")
        d.text((cx, cz), lbl, fill=(255, 255, 255), font=bold, anchor="mm")
for x, z in ramp_centres:
    d.text(px(x, z), "ramp", fill=(255, 236, 160), font=bold, anchor="mm")
d.text(px(0, COAST_Z - 10), "the sea (unclaimable)", fill=(220, 235, 255), font=bold, anchor="mm")
# scale bar: 20 m
x0, y0 = 14, NZ - 18
bar = 20 / SCALE * M                      # 20 world metres
d.rectangle((x0 - 4, y0 - 16, x0 + bar + 30, y0 + 8), fill=(0, 0, 0))
d.line((x0, y0, x0 + bar, y0), fill=(255, 255, 255), width=3)
d.text((x0 + bar + 6, y0), "20 m", fill=(255, 255, 255), font=font, anchor="lm")

lx, y = NX + 18, 18
d.text((lx, y), "Yiel Lymwérra", fill=(0, 0, 0), font=big); y += 30
d.text((lx, y), f"2 players, {WX} x {WZ} m, mirrored west/east", fill=(0, 0, 0), font=font); y += 18
d.text((lx, y), "faint grid = 10 m; squares = the capital (10 m)", fill=(0, 0, 0), font=font); y += 28
for lab_, col in [("Grass plateau", (112, 146, 66)), ("Beach", (222, 200, 150)),
                  ("Shallows: wade, never build", (128, 160, 140)), ("Sea (impassable)", (30, 76, 124)),
                  ("Cliff", (70, 62, 56)), ("Ramp", (170, 140, 100)),
                  ("Mountain (unclaimable)", (140, 132, 122)), ("Forest (impassable)", (46, 82, 42)),
                  ("Island: curse start", (150, 90, 170))]:
    d.rectangle((lx, y, lx + 22, y + 16), fill=col, outline=(0, 0, 0)); d.text((lx + 32, y), lab_, fill=(0, 0, 0), font=font); y += 22
y += 10
for line in ["Start: the plateau beside the bay.",
             "Highland: north of the bay, holds a ramp.",
             "Bluff: clifftop over the bay and beach.",
             "Crown: shared, above both bay ramps.",
             "Sanctum: mountain pocket, one ramp.",
             "Beach: reached ONLY through the bay.",
             "Shallows: claimable, never buildable.",
             "Yiel: the curse's home, in the shallows."]:
    d.text((lx, y), line, fill=(0, 0, 0), font=font); y += 18
y += 12
nreg = len(set(region[valid].tolist()))
d.text((lx, y), f"{nreg} territories", fill=(0, 0, 0), font=font); y += 18
d.text((lx, y), f"Start to start: ~{start_dist} m walking", fill=(0, 0, 0), font=font); y += 18
d.text((lx, y), f"Start to island: ~{isle_dist} m walking", fill=(0, 0, 0), font=font); y += 24
ok = not cut and not lonely
d.text((lx, y), "All territories reachable and connected" if ok else f"PROBLEM: {cut + lonely}",
       fill=(0, 120, 0) if ok else (200, 0, 0), font=font)
canvas.save(os.path.join(HERE, "preview.png"))
print("saved natural.png and preview.png;", nreg, "territories")


# ═════════════════════════════════════════════════════════════════════════════
# EXPORT — what MapBuilderYielLymwerra (via MapBuilderFromLayout) reads
# ═════════════════════════════════════════════════════════════════════════════
import json
from skimage import measure

HRES = 513                   # heightmap resolution (square, 2^n + 1): 0.75 m in x, 0.375 m in z
MAX_H = 80.0                 # TerrainData height for a normalized 1.0
LEVEL_H = [H_SHALLOW, H_BEACH0, H_ISLAND, H_GRASS, H_POCKET]
ROLL_H = [0.3, 1.4, 0.6, 2.0, 0.5]   # how far each level's ground rises above LEVEL_H
assert h.max() < MAX_H and h.min() > -MAX_H, (h.min(), h.max())

def plan_rc(x, z):
    """Plan-grid (row, col) of a WORLD point, fractional."""
    return (SZ / 2 - z / SCALE) * M, (x / SCALE + SX / 2) * M

# heights.r16: row 0 is the SOUTH edge; bilinear from the plan
gx_w = -WX / 2 + np.arange(HRES) * WX / (HRES - 1)
gz_w = -WZ / 2 + np.arange(HRES) * WZ / (HRES - 1)
GX, GZ = np.meshgrid(gx_w, gz_w)
r_, c_ = plan_rc(GX, GZ)
hu = np.clip(ndimage.map_coordinates(h, [r_, c_], order=1, mode="nearest") / MAX_H, 0, 1)
(hu * 65535 + 0.5).astype("<u2").tofile(os.path.join(HERE, "heights.r16"))

# masks at 1 WORLD metre, north at the top: (WX+1) x (WZ+1)
mr = np.clip(np.round((SZ / 2 - (WZ / 2 - np.arange(WZ + 1)) / SCALE) * M).astype(int), 0, NZ - 1)
mc = np.clip(np.round(((np.arange(WX + 1) - WX / 2) / SCALE + SX / 2) * M).astype(int), 0, NX - 1)
def at1m(a): return a[np.ix_(mr, mc)]
def save_mask(name, m):
    Image.fromarray(at1m(m).astype(np.uint8) * 255).save(os.path.join(HERE, name))
def save_gray(name, m):
    m = ndimage.gaussian_filter(m.astype(np.float32), 0.6 * M)
    Image.fromarray((np.clip(at1m(m), 0, 1) * 255).astype(np.uint8)).save(os.path.join(HERE, name))

save_mask("nowalk.png", mountain | forest)
save_mask("forest.png", forest)
save_mask("dirt.png", ramp_mask & ~sea)
save_mask("fringe.png", mountain & (d_mtn_in < 2.5))
save_mask("grass.png", (((grass | pocket) & (d_low > 1.0)) | (island & (d_isl_in > 2.5))) & ~ramp_mask & ~forest)
sand = (beach | sea | shallows).astype(np.float32)
sand = np.maximum(sand, island * np.clip(1 - d_isl_in / 2.5, 0, 1))
sand = np.maximum(sand, (grass & (d_low < 1.0)) * 0.6)
save_gray("sand.png", sand * ~ramp_mask)
bare = np.clip(noise(7, 98) * 1.5 - 1.25, 0, 0.5) * grass * (d_ramp > 4)
save_gray("bare.png", mirror(bare))

# ── territories: outlines, kinds, resource types ─────────────────────────────
DISPLAY = {
    "WStart": "Gullhold", "EStart": "Ternhold",
    "WBluff": "Gull Bluff", "EBluff": "Tern Bluff",
    "WHighland": "Westerly Downs", "EHighland": "Easterly Downs",
    "WBeach": "Gull Strand", "EBeach": "Tern Strand",
    "WShallows": "West Lymwérra", "EShallows": "East Lymwérra",
    "Crown": "Bayhead", "Sanctum": "Stormcairn", "Isle": "Yiel",
}
# Every type is authored and mirrored, so neither seat is dealt better ground.
# Yiel is Veilstone rich and PINNED: the curse always starts there.
TYPES = {
    "Start": "Start", "Bluff": "NormalIron", "Highland": "NormalVeilstone",
    "Beach": "IronRich", "Shallows": "Empty",
    "Crown": "Normal", "Sanctum": "Sanctum", "Isle": "VeilstoneRich",
}
def rtype(code):
    return TYPES[code[1:] if code[0] in "WE" and code[1].isupper() else code]

regions_out, seed_bad = [], []
for r, code in enumerate(names):
    m = region == r
    if not m.any(): continue
    lab, n = ndimage.label(m)
    if n > 1:
        sizes = ndimage.sum(m, lab, range(1, n + 1)); m = lab == (1 + int(np.argmax(sizes)))
    c = max(measure.find_contours(np.pad(m, 1).astype(np.float32), 0.5), key=len)
    c = measure.approximate_polygon(c, tolerance=0.3 * M)[:-1]
    shape = []
    for (row, col) in c:
        shape += [round(float(((col - 1) / M - SX / 2) * SCALE), 2), round(float((SZ / 2 - (row - 1) / M) * SCALE), 2)]
    # deepest WALKABLE interior point; padded so the map edge counts as an edge
    dt = ndimage.distance_transform_edt(np.pad(m & walk, 1))[1:-1, 1:-1]
    sr, sc = np.unravel_index(np.argmax(dt), dt.shape)
    lv = int(lvl_arr[r])
    got = float(h[sr, sc])
    if got < LEVEL_H[lv] - 1.5 or got > LEVEL_H[lv] + ROLL_H[lv] + 1.5:
        seed_bad.append(f"{code} {got:.1f} vs {LEVEL_H[lv]:.1f}")
    regions_out.append({
        "code": code, "name": DISPLAY[code], "level": lv,
        "kind": "PlayerStart" if rtype(code) == "Start" else "Normal",
        "resources": rtype(code), "pinCurse": code == "Isle",
        "seedX": round(float((sc / M - SX / 2) * SCALE), 2), "seedZ": round(float((SZ / 2 - sr / M) * SCALE), 2),
        "shape": shape,
    })
print("seeds off their level:", seed_bad or "none")

starts_out = []
for code, faction in (("WStart", "Blue"), ("EStart", "Red")):
    rr = next(x for x in regions_out if x["code"] == code)
    starts_out.append({"faction": faction, "x": rr["seedX"], "z": rr["seedZ"], "region": rr["name"]})

bay_rows = np.nonzero(bay.any(1))[0]
layout = {
    "mapName": "Yiel Lymwérra", "size": WX, "sizeZ": WZ, "heightRes": HRES, "maxHeight": MAX_H,
    "levelHeights": LEVEL_H, "rollHeights": ROLL_H,
    "waterLevel": WATER, "wadeDepth": WADE,
    "waterMinZ": -WZ / 2, "waterMaxZ": round(float((SZ / 2 - bay_rows.min() / M) * SCALE) + 3.0, 2),
    "regions": regions_out, "starts": starts_out,
}
with open(os.path.join(HERE, "layout.json"), "w", encoding="utf-8") as f:
    json.dump(layout, f, indent=1, ensure_ascii=False)
print("exported", len(regions_out), "territories; starts", [(s_["x"], s_["z"]) for s_ in starts_out],
      "; heights", round(float(h.min()), 2), "..", round(float(h.max()), 2))
