# layoutkit.py — the shared machinery for maps authored as DATA
# (tools/maps/<map>/layout.py -> MapBuilderFromLayout), 2026-10-18.
#
# Fiendstone Pass and Yiel Lymwérra each carry their own copy of this code;
# the maps drawn after them (Moatwatch, Highfold, Broken Oath, Trine Hollow)
# share it. A map's layout.py only says WHAT is where: shapes with a level,
# territories, ramps, types and names. This file does the rest:
#
#   symmetry   every map names a symmetry group (mirror both axes, mirror one
#              axis, or n-fold rotation). Shapes are authored once and painted
#              in every copy; noise is symmetrised; and the finished arrays are
#              FOLDED onto the canonical domain, so every seat gets exactly the
#              same ground (to the 1 m sample).
#   heights    levels with a 4.5 m step (a cliff) between them, rolling ground,
#              mountains, deep water with sloped banks, waded fords, ramps.
#   checks     the game's walk rule on the real heights, every territory
#              reachable on foot, every territory bordering another (or it can
#              never be claimed), the claim graph connected from every home,
#              territory areas, and a NODE-ROOM PROBE that replays the node
#              rules (start clearing, home band, border band, inset, spacing).
#   pictures   natural.png (the map as it would look) and preview.png (the
#              annotated plan: territory types tinted, areas, homes, checks).
#   export     only with --export: heights.r16, the 1 m masks and layout.json
#              in the format MapBuilderFromLayout reads.
#
# Units: 1 sample per metre, origin at the map centre, north up; arrays are
# indexed [row, col] with row 0 the NORTH edge.
import os, sys, json, zlib
import numpy as np
from collections import deque
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
from scipy.spatial import cKDTree

# ── Rule numbers the checks replay (the assets win; these mirror them) ───────
LEVEL_H = [5.0, 9.5, 14.0, 18.5, 23.0, 27.5]   # 4.5 m a step: a cliff
ROLL_H = [3.0, 2.2, 2.0, 1.5, 1.2, 1.0]        # rolling ground above a level
WATER, WADE, BED, FORD_H = 3.6, 0.5, 1.2, 3.25  # WaterPlane level, wadeDepth, river bed, ford top
# Mountains (2026-10-19: ONE THIRD of their old height, the same ruggedness).
# The original profile — MTN_BASE + MTN_DOME a metre in (up to 40 m in) + the
# ridge noise (8 m + 3 m) — is kept whole, and its relief OVER THE GROUND
# AROUND IT is scaled by MTN_SCALE: h = ground + (old - ground) / 3, noise
# included, so every peak and trough keeps its place and its proportion, just
# squashed. Never less than MTN_MIN over the ground beside it; mountains stay
# impassable by the NoWalk paint (nowalk.png) and unclaimable (no territory),
# not by height.
MTN_BASE, MTN_DOME = 34.0, 0.45      # the original (pre-2026-10-19) profile
MTN_SCALE, MTN_MIN = 1.0 / 3.0, 2.5
MAX_H = 72.0                 # TerrainData height for a normalised 1.0
NODES = {"Start": 7, "Normal": 3, "NormalIron": 3, "NormalVeilstone": 4,   # NV: worst case of the 1-2 draw
         "Empty": 0, "VeilstoneRich": 4, "IronRich": 3, "Sanctum": 0}
NODE_SPACING = 9.0           # TerritoryResources: m between a territory's nodes
BORDER_BAND = 10.0           # m a node's footprint keeps clear of another territory
BORDER_INSET = 8.0           # m a node's centre keeps from impassable ground
MARGIN_SHARE = 0.35          # ComputeMargins cap: share of the shorter half-width
START_CLEAR = 26.0           # StartClearing.asset radiusCells 13 x 2 m
HOME_OFFSET = 38.0           # TerritoryResources.asset homeNodeMinOffset
FOOT = 2.0                   # half a node's 4 m footprint
HOME_LEGAL_MIN = 1000        # m2 of legal node ground a home needs to be safe in game
FORD_LEVEL = len(LEVEL_H)    # exported level of a waded ford (its height: FORD_H)

RICH = ("NormalVeilstone", "IronRich", "VeilstoneRich", "Sanctum")
TYPE_STYLE = {   # preview tint, tag
    "Start": ((255, 255, 255), "Home"),
    "Normal": ((176, 196, 128), "S"),
    "NormalIron": ((168, 160, 150), "S+Fe"),
    "NormalVeilstone": ((70, 200, 214), "S+Vs"),
    "IronRich": ((84, 108, 190), "Fe+"),
    "VeilstoneRich": ((156, 70, 196), "Curse"),
    "Sanctum": ((236, 196, 64), "Sanct"),
    "Empty": ((236, 236, 236), "Empty"),
}
PLAYER = [(60, 110, 230), (220, 60, 60), (60, 170, 80), (230, 200, 40),
          (160, 80, 210), (240, 140, 40), (40, 190, 190), (230, 110, 170)]
FACTIONS = ["Blue", "Red", "Green", "Yellow", "Purple", "Orange", "Teal", "White"]   # enum Faction

LAND, MTN, WATERT = 0, 1, 2


def _h(name, k):
    return (zlib.crc32((str(name) + ":" + str(k)).encode()) % 1000) / 1000.0


# ── Symmetry groups: 2x2 orthogonal matrices acting on (x, z) ────────────────
def rot(deg):
    a = np.radians(deg)
    return np.array([[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]])


def group_mirror_both():
    """x -> -x and z -> -z: four quadrants. The canonical one is south-west,
    which is the one TerritoryResources.MirrorPartners lays nodes in."""
    G = [np.eye(2), np.diag([-1.0, 1.0]), np.diag([1.0, -1.0]), -np.eye(2)]
    def fold(x, z):
        k = np.where(x > 0, 1, 0) + np.where(z > 0, 2, 0)
        return -np.abs(x), -np.abs(z), k
    return G, fold, lambda x, z: (x <= 0) & (z <= 0)


def group_mirror_z():
    """z -> -z: north half the mirror of the south half (canonical: south)."""
    G = [np.eye(2), np.diag([1.0, -1.0])]
    def fold(x, z):
        return x, -np.abs(z), np.where(z > 0, 1, 0)
    return G, fold, lambda x, z: z <= 0


def group_rotation(n, centre_deg=90.0):
    """n-fold rotation; the canonical wedge is centred on centre_deg."""
    step = 360.0 / n
    G = [rot(k * step) for k in range(n)]
    lo = centre_deg - step / 2
    def fold(x, z):
        th = np.degrees(np.arctan2(z, x))
        k = np.floor(((th - lo) % 360.0) / step).astype(int) % n
        a = -np.radians(k * step)
        return x * np.cos(a) - z * np.sin(a), x * np.sin(a) + z * np.cos(a), k
    def canon(x, z):
        th = np.degrees(np.arctan2(z, x))
        return ((th - lo) % 360.0) < step
    return G, fold, canon


# ── Shapes, in canonical coordinates. Each returns f(X, Z, E) -> bool mask,
# where X, Z are (warped) coordinates and E the symmetric edge noise. ────────
def blob(cx, cz, rx, rz, ang=0.0, wob=0.12, seed=None, rough=0.09):
    seed = seed if seed is not None else f"{cx},{cz}"
    a0 = np.radians(ang)
    def f(X, Z, E):
        dx, dz = X - cx, Z - cz
        lx = (dx * np.cos(a0) + dz * np.sin(a0)) / rx
        lz = (-dx * np.sin(a0) + dz * np.cos(a0)) / rz
        a = np.arctan2(lz, lx)
        r = 1.0
        for k, fq in enumerate((2, 3, 5)):
            r = r + wob * (0.6 / (k + 1)) * np.sin(fq * a + 6.28 * _h(seed, k))
        return np.hypot(lx, lz) + E * rough < r
    return f


def lane(pts, hw, wob=0.25):
    def f(X, Z, E):
        best = np.full(X.shape, 1e9)
        for (ax, az), (bx, bz) in zip(pts[:-1], pts[1:]):
            vx, vz = bx - ax, bz - az
            t = np.clip(((X - ax) * vx + (Z - az) * vz) / (vx * vx + vz * vz), 0, 1)
            best = np.minimum(best, np.hypot(X - ax - t * vx, Z - az - t * vz))
        return best + E * hw * wob < hw
    return f


def ring(cx, cz, r0, r1, rough=3.0):
    def f(X, Z, E):
        d = np.hypot(X - cx, Z - cz) + E * rough
        return (d >= r0) & (d < r1)
    return f


def disc(cx, cz, r, rough=3.0):
    def f(X, Z, E):
        return np.hypot(X - cx, Z - cz) + E * rough < r
    return f


def sector(cx, cz, deg0, deg1, r0=0.0, r1=1e9, rough=0.0):
    """Angles measured from (cx, cz), counter-clockwise from east."""
    def f(X, Z, E):
        th = np.degrees(np.arctan2(Z - cz, X - cx)) % 360.0
        d = np.hypot(X - cx, Z - cz) + E * rough
        a, b = deg0 % 360.0, deg1 % 360.0
        inside = (th >= a) & (th < b) if a < b else (th >= a) | (th < b)
        return inside & (d >= r0) & (d < r1)
    return f


def everywhere():
    return lambda X, Z, E: np.ones(X.shape, bool)


def AND(*fs):
    return lambda X, Z, E: np.logical_and.reduce([f(X, Z, E) for f in fs])


def OR(*fs):
    return lambda X, Z, E: np.logical_or.reduce([f(X, Z, E) for f in fs])


def NOT(f):
    return lambda X, Z, E: ~f(X, Z, E)


# ═════════════════════════════════════════════════════════════════════════════
def _mtn_ground(ground, mtn, iters=300):
    """The ground a mountain stands on, inside the mass: equal to the real
    ground at its foot and relaxed (Jacobi, 4-neighbour mean) toward a smooth
    surface inside, so a mass spanning two shelves has no seam and its relief
    over the ground beside its foot is exactly the scaled old relief."""
    from scipy import ndimage as _nd
    gi = _nd.distance_transform_edt(mtn, return_distances=False, return_indices=True)
    g = _nd.gaussian_filter(ground[gi[0], gi[1]], 12).astype(np.float32)
    g[~mtn] = ground[~mtn]
    k = np.array([[0, .25, 0], [.25, 0, .25], [0, .25, 0]], np.float32)
    for _ in range(iters):
        g2 = _nd.convolve(g, k, mode="nearest")
        g[mtn] = g2[mtn]
    return g


class Plan:
    def __init__(self, title, sx, sz, group, here, seed=1, warp=(12.0, 4.0)):
        self.title, self.sx, self.sz, self.here = title, int(sx), int(sz), here
        self.NX, self.NZ = self.sx + 1, self.sz + 1
        zs, xs = np.mgrid[0:self.NZ, 0:self.NX]
        self.rows, self.cols = zs, xs
        self.wx = (xs - self.sx / 2).astype(np.float64)
        self.wz = (self.sz / 2 - zs).astype(np.float64)
        self.G, self.fold, self.in_canon = group
        self.seed = seed
        self._mul = self._mult_table()
        self.E = self.noise(48, 11) * 0.5 + self.noise(16, 12) * 0.32 + self.noise(6, 13) * 0.18
        self.E /= self.E.std()
        self.ux, self.uz = self._warp(*warp)
        self.terrain = np.full((self.NZ, self.NX), MTN, np.uint8)
        self.wade = np.zeros((self.NZ, self.NX), bool)
        self.region = np.full((self.NZ, self.NX), -1, np.int32)
        self.R = []                  # region records
        self._ids = {}               # (canon, k) -> id
        self.ramps = []              # (canonA, canonB, x, z) canonical hints
        self.ramp_half, self.ramp_depth = 8.0, 11.0
        self.start_pts = {}          # canon home -> (x, z) canonical
        self.forest_cover = 1.3
        self.forest_banned = set()   # canon codes kept clear of trees
        self.labels = {}             # canon -> short label override
        # pin_curse: the Veilstone rich territories keep the curse every match
        # (RegionSeedMarker.CurseStaysHere). Needed where the map is not
        # mirrored on both axes: there TerritoryResources re-deals the curse to
        # RANDOM territories, which is unfair between seats.
        self.pin_curse = False

    # ── symmetry helpers ────────────────────────────────────────────────────
    def _mult_table(self):
        n = len(self.G)
        t = np.zeros((n, n), int)
        for a in range(n):
            for b in range(n):
                m = self.G[a] @ self.G[b]
                t[a, b] = next(k for k in range(n) if np.allclose(self.G[k], m, atol=1e-6))
        return t

    def _raw(self, scale, seed):
        L = int(max(self.sx, self.sz) * 1.6) + 8
        s = max(2, int(scale))
        rng = np.random.default_rng(seed * 7919 + self.seed)
        g = rng.standard_normal((L // s + 4, L // s + 4)).astype(np.float32)
        img = Image.fromarray(g, mode="F").resize(((L // s + 4) * s, (L // s + 4) * s), Image.BICUBIC)
        return np.asarray(img), ((L // s + 4) * s) / 2.0

    def _sample(self, img, off, X, Z):
        return ndimage.map_coordinates(img, [off - Z, X + off], order=1, mode="reflect")

    def noise(self, scale, seed):
        """Smooth noise ~scale m across, symmetric under the map's group, std 1."""
        img, off = self._raw(scale, seed)
        acc = np.zeros((self.NZ, self.NX))
        for g in self.G:
            X = g[0, 0] * self.wx + g[0, 1] * self.wz
            Z = g[1, 0] * self.wx + g[1, 1] * self.wz
            acc += self._sample(img, off, X, Z)
        acc /= len(self.G)
        return acc / (acc.std() + 1e-9)

    def _warp(self, big, small):
        dx = np.zeros((self.NZ, self.NX)); dz = np.zeros((self.NZ, self.NX))
        for amp, sc, sd in ((big, 150, 21), (small, 50, 22)):
            ia, oa = self._raw(sc, sd); ib, ob = self._raw(sc, sd + 100)
            ex = np.zeros_like(dx); ez = np.zeros_like(dz)
            for g in self.G:
                X = g[0, 0] * self.wx + g[0, 1] * self.wz
                Z = g[1, 0] * self.wx + g[1, 1] * self.wz
                a = self._sample(ia, oa, X, Z); b = self._sample(ib, ob, X, Z)
                ex += g[0, 0] * a + g[1, 0] * b          # g^T (a, b)
                ez += g[0, 1] * a + g[1, 1] * b
            s = np.sqrt(np.mean(ex * ex + ez * ez)) + 1e-9
            dx += ex / s * amp; dz += ez / s * amp
        return self.wx + dx, self.wz + dz

    def copies(self, fn):
        """fn painted in every copy: list of (k, mask)."""
        out = []
        for k, g in enumerate(self.G):
            X = g[0, 0] * self.ux + g[1, 0] * self.uz      # g^T u
            Z = g[0, 1] * self.ux + g[1, 1] * self.uz
            out.append((k, fn(X, Z, self.E)))
        return out

    def union(self, fn):
        m = np.zeros((self.NZ, self.NX), bool)
        for _, mk in self.copies(fn): m |= mk
        return m

    def img(self, x, z):
        return int(round(self.sz / 2 - z)), int(round(x + self.sx / 2))

    # ── painting ────────────────────────────────────────────────────────────
    def terrain_paint(self, kind, fn):
        m = self.union(fn)
        if kind == "land":
            self.terrain[m] = LAND
        elif kind == "mountain":
            self.terrain[m] = MTN; self.region[m] = -1; self.wade[m] = False
        elif kind == "water":
            self.terrain[m] = WATERT; self.region[m] = -1; self.wade[m] = False
        return m

    def _new_region(self, canon, k, level, rtype, shared, kind):
        key = (canon, None if shared else k)
        if key in self._ids: return self._ids[key]
        rid = len(self.R)
        code = canon if shared else f"{canon}.{k}"
        self.R.append(dict(id=rid, code=code, canon=canon, k=None if shared else k,
                           level=level, type=rtype, shared=shared, kind=kind))
        self._ids[key] = rid
        return rid

    def region_paint(self, canon, level, rtype, fn, shared=False, kind="land", start=None, only_free=False):
        """A territory painted in every copy (one territory when shared).
        kind 'land' raises it to its level; 'ford' makes it waded water."""
        if start is not None: self.start_pts[canon] = start
        for k, m in self.copies(fn):
            if only_free: m = m & (self.region < 0)
            rid = self._new_region(canon, k, level, rtype, shared, kind)
            self.region[m] = rid
            if kind == "ford":
                self.terrain[m] = WATERT; self.wade[m] = True
            else:
                self.terrain[m] = LAND; self.wade[m] = False
        return canon

    def ford_paint(self, canon, rtype, fn, shared=False):
        """A SHALLOWS territory: waded water that is its own territory and is
        BUILDABLE (exported as buildableShallows -> RegionSeedMarker.
        BuildableShallows, 2026-10-19). Only for a wide stretch of shallows
        meant to be held; a river crossing is a wade_paint ford instead."""
        return self.region_paint(canon, 0, rtype, fn, shared=shared, kind="ford")

    def wade_paint(self, fn):
        """A FORD: waded water that is NO territory of its own (2026-10-19).
        build() hands each of its samples to the nearest land territory, so
        the inner half of a crossing joins the territory on one bank and the
        outer half the one on the other: the two banks BORDER each other
        across it, and claims cross where armies wade. Unbuildable (water is,
        outside a shallows territory)."""
        m = self.union(fn)
        self.terrain[m] = WATERT; self.wade[m] = True; self.region[m] = -1
        return m

    def split(self, prefix, level, zone, count, types, init=None, iters=40, min_mirror=30.0, anchor=None, min_area=2000):
        """Nearest-seed (Voronoi, in warped space) territories over the FREE
        land inside zone, count seeds per copy, Lloyd-relaxed to even areas.
        types: f(list of canonical seeds) -> list of (type, label-or-None)."""
        area = self.union(zone) & (self.terrain == LAND) & (self.region < 0)
        canon_area = area & self.in_canon(self.wx, self.wz)
        for g in self.G[1:]:
            canon_area &= np.hypot(g[0, 0] * self.wx + g[0, 1] * self.wz - self.wx,
                                   g[1, 0] * self.wx + g[1, 1] * self.wz - self.wz) >= min_mirror
        rng = np.random.default_rng(zlib.crc32(prefix.encode()))
        pts_z, pts_x = np.nonzero(canon_area)
        if init is None:
            # random interior points, at least half a cell apart
            cell = np.sqrt(canon_area.sum() / count)
            dt = ndimage.distance_transform_edt(canon_area)
            pts_z, pts_x = np.nonzero(dt > cell * 0.25)
            pick = []
            for j in rng.permutation(len(pts_x)):
                p = (self.ux[pts_z[j], pts_x[j]], self.uz[pts_z[j], pts_x[j]])
                if all(np.hypot(p[0] - q[0], p[1] - q[1]) > cell * 0.6 for q in pick): pick.append(p)
                if len(pick) == count: break
            seeds = np.array(pick, float)
        else:
            seeds = np.array(init, float)
        st = 2
        A = area[::st, ::st]
        az, ax = np.nonzero(A)
        U = np.stack([self.ux[::st, ::st][az, ax], self.uz[::st, ::st][az, ax]], 1)
        W = np.stack([self.wx[::st, ::st][az, ax], self.wz[::st, ::st][az, ax]], 1)
        n = len(seeds)
        if anchor is None: anchor = W[self.in_canon(W[:, 0], W[:, 1])].mean(0)
        inC = self.in_canon(W[:, 0], W[:, 1])
        for g in self.G[1:]:      # and well clear of every axis
            inC &= np.hypot(*(W @ g.T - W).T) >= min_mirror
        for it in range(iters + 1):
            allS = np.concatenate([seeds @ g.T for g in self.G])
            dist_, lab = cKDTree(allS).query(U)
            if it == iters: break
            sizes = np.bincount(lab, minlength=len(allS))[:n]
            for i in range(n):
                sel = lab == i
                if sizes[i] < 0.15 * sizes.mean() and it < iters - 5:
                    # a starved cell: move its seed to the emptiest spot
                    j = np.argmax(np.where(inC, dist_, -1))
                    seeds[i] = U[j]; dist_[np.hypot(*(W - W[j]).T) < 30] = 0
                    continue
                if sizes[i] == 0: continue
                c = U[sel].mean(0)          # warped space: where the cells live
                # never let a seed sit on (or near) a mirror/rotation axis: its
                # cell would straddle it and break the seat copies
                for g in self.G[1:]:
                    gc = g @ c
                    if np.hypot(*(gc - c)) < min_mirror:
                        c = c + (anchor - c) * 0.15
                seeds[i] = c
        # final full-resolution labels; a seed left with a sliver (a corner
        # between other shapes) is dropped and its ground shared out
        fz, fx = np.nonzero(area)
        Uf = np.stack([self.ux[fz, fx], self.uz[fz, fx]], 1)
        while True:
            n = len(seeds)
            allS = np.concatenate([seeds @ g.T for g in self.G])
            lab = cKDTree(allS).query(Uf)[1]
            sizes = np.bincount(lab, minlength=len(allS))[:n]
            if sizes.min() >= min_area or n <= 1: break
            print(f"  {prefix}: dropping a {int(sizes.min())} m2 sliver")
            seeds = np.delete(seeds, int(np.argmin(sizes)), 0)
        tl = types([tuple(s) for s in seeds])
        for j in range(len(allS)):
            k, i = divmod(j, n)
            rtype, label = tl[i]
            canon = f"{prefix}{i}"
            if label: self.labels[canon] = label
            rid = self._new_region(canon, k, level, rtype, False, "land")
            sel = lab == j
            self.region[fz[sel], fx[sel]] = rid
        return [tuple(s) for s in seeds]

    def ramp(self, a, b, x, z):
        """A ramp between territories a and b (b=None: whatever land of
        another level borders a nearest the hint), at the shared edge nearest
        the canonical hint (x, z); painted in every copy."""
        self.ramps.append((a, b, float(x), float(z)))

    # ── build: fold, heights, forest, checks ────────────────────────────────
    def _rid(self, canon, k):
        return self._ids.get((canon, None)) if (canon, None) in self._ids else self._ids.get((canon, k))

    def _fold_all(self):
        X, Z, K = self.fold(self.wx, self.wz)
        r = np.clip(np.round(self.sz / 2 - Z).astype(int), 0, self.NZ - 1)
        c = np.clip(np.round(X + self.sx / 2).astype(int), 0, self.NX - 1)
        self._fold_rc = (self.sz / 2 - Z, X + self.sx / 2)
        self._fold_k = K
        reg0 = self.region[r, c]
        out = np.full_like(reg0, -1)
        # region at the canonical point is (canon, j); at p = G[K] q it is copy K*j
        for rec in self.R:
            m = reg0 == rec["id"]
            if not m.any(): continue
            if rec["shared"]:
                out[m] = rec["id"]; continue
            for kk in range(len(self.G)):
                mm = m & (K == kk)
                if mm.any():
                    out[mm] = self._ids[(rec["canon"], int(self._mul[kk, rec["k"]]))]
        self.region = out
        self.terrain = self.terrain[r, c]
        self.wade = self.wade[r, c]

    def _fold_field(self, a, order=1):
        return ndimage.map_coordinates(a, list(self._fold_rc), order=order, mode="nearest")

    def build(self):
        # land no territory took joins its nearest territory
        hole = (self.terrain == LAND) & (self.region < 0)
        if hole.any():
            idx = ndimage.distance_transform_edt(self.region < 0, return_distances=False, return_indices=True)
            self.region[hole] = self.region[idx[0][hole], idx[1][hole]]
        # fords (wade_paint): each waded sample no territory holds joins the
        # nearest LAND territory, so a crossing is split between its banks
        ford = self.wade & (self.region < 0)
        if ford.any():
            owned = (self.region >= 0) & (self.terrain == LAND)
            idx = ndimage.distance_transform_edt(~owned, return_distances=False, return_indices=True)
            self.region[ford] = self.region[idx[0][ford], idx[1][ford]]
        self._fold_all()
        reg, ter = self.region, self.terrain
        self.valid = reg >= 0
        lvl_arr = np.array([r["level"] for r in self.R])
        self.level = np.where(self.valid, lvl_arr[np.maximum(reg, 0)], -1)
        land = self.valid & (ter == LAND)
        self.mtn = ter == MTN
        self.water = ter == WATERT
        self.land = land

        # cliffs: land samples whose neighbour is land of another level
        cliff = np.zeros(reg.shape, bool)
        LL = np.where(land, self.level, -9)
        cliff[:, 1:] |= (LL[:, 1:] >= 0) & (LL[:, :-1] >= 0) & (LL[:, 1:] != LL[:, :-1])
        cliff[1:, :] |= (LL[1:, :] >= 0) & (LL[:-1, :] >= 0) & (LL[1:, :] != LL[:-1, :])
        self.cliff = cliff

        # ramps, every copy
        ramp_mask = np.zeros(reg.shape, bool)
        self.ramp_info = []
        for a, b, x, z in self.ramps:
            for k, g in enumerate(self.G):
                if a is None:
                    # a = None too: the cliff nearest the hint, whoever owns it
                    px, pz = g @ np.array([x, z])
                    cy, cx = self.img(px, pz)
                    r0, c0 = max(cy - 60, 0), max(cx - 60, 0)
                    cw = cliff[r0:cy + 61, c0:cx + 61]
                    yy, xx = np.nonzero(cw)
                    if len(yy) == 0:
                        print(f"WARNING: ramp *-* copy {k}: no cliff near ({px:.0f},{pz:.0f})"); continue
                    j = np.argmin((yy + r0 - cy) ** 2 + (xx + c0 - cx) ** 2)
                    ia = int(reg[yy[j] + r0, xx[j] + c0]); ib = -2
                    ga, gb = self.R[ia]["canon"], None
                else:
                    ia = self._rid(a, k)
                    ib = self._rid(b, k) if b is not None else -2
                if ia is None or ib is None:
                    print("WARNING ramp names", a, b); continue
                px, pz = g @ np.array([x, z])
                cy, cx = self.img(px, pz)
                r0, c0 = max(cy - 60, 0), max(cx - 60, 0)
                win = reg[r0:cy + 61, c0:cx + 61]
                lw = LL[r0:cy + 61, c0:cx + 61]
                if ib == -2:
                    # b = None: whichever land territory of another level is
                    # across a's edge nearest the hint
                    other = (win != ia) & (lw >= 0) & (lw != lvl_arr[ia])
                    bm = (win == ia) & ndimage.binary_dilation(other)
                    yy, xx = np.nonzero(bm)
                    if len(yy) == 0:
                        print(f"WARNING: ramp {a}-* copy {k}: no edge near ({px:.0f},{pz:.0f})"); continue
                    j = np.argmin((yy + r0 - cy) ** 2 + (xx + c0 - cx) ** 2)
                    nb_ = win[max(yy[j] - 1, 0):yy[j] + 2, max(xx[j] - 1, 0):xx[j] + 2]
                    nl_ = lw[max(yy[j] - 1, 0):yy[j] + 2, max(xx[j] - 1, 0):xx[j] + 2]
                    ib = int(nb_[(nb_ != ia) & (nl_ >= 0) & (nl_ != lvl_arr[ia])][0])
                bm = np.zeros(win.shape, bool)
                bm[:, 1:] |= ((win[:, 1:] == ia) & (win[:, :-1] == ib)) | ((win[:, 1:] == ib) & (win[:, :-1] == ia))
                bm[1:, :] |= ((win[1:, :] == ia) & (win[:-1, :] == ib)) | ((win[1:, :] == ib) & (win[:-1, :] == ia))
                yy, xx = np.nonzero(bm)
                if len(yy) == 0:
                    print(f"WARNING: ramp {a}-{b} copy {k}: no shared border near ({px:.0f},{pz:.0f})"); continue
                j = np.argmin((yy + r0 - cy) ** 2 + (xx + c0 - cx) ** 2)
                ry, rx_ = int(yy[j] + r0), int(xx[j] + c0)
                key = (min(ia, ib), max(ia, ib), ry // 6, rx_ // 6)
                if any(abs(q[0] - ry) < 6 and abs(q[1] - rx_) < 6 for q in self.ramp_info): continue
                self.ramp_info.append((ry, rx_, ia, ib))
                ramp_mask |= (self.rows - ry) ** 2 + (self.cols - rx_) ** 2 < (self.ramp_half + 1) ** 2
        self.ramp_mask = ramp_mask

        # ── heights ──
        d_cliff = ndimage.distance_transform_edt(~cliff)
        d_ramp = ndimage.distance_transform_edt(~ramp_mask)
        d_water = ndimage.distance_transform_edt(~self.water)
        d_mtn = ndimage.distance_transform_edt(~self.mtn)
        self.d_cliff, self.d_ramp, self.d_water, self.d_mtn = d_cliff, d_ramp, d_water, d_mtn
        LH = np.array(LEVEL_H); RH = np.array(ROLL_H)
        roll = 0.5 + 0.5 * np.tanh(self.noise(90, 88) * 0.9 + self.noise(36, 89) * 0.5 + self.noise(14, 90) * 0.18)
        self.roll = roll
        fade = np.clip((np.minimum(np.minimum(d_cliff, d_ramp), np.minimum(d_water, d_mtn)) - 5.0) / 28.0, 0, 1)
        lv = np.maximum(self.level, 0)
        h = np.zeros(reg.shape, np.float32)
        h[land] = (LH[lv] + RH[lv] * roll * fade)[land]
        ridge = 1 - np.abs(self.noise(60, 31)); ridge2 = 1 - np.abs(self.noise(22, 32))
        self.ridge = ridge
        d_in = ndimage.distance_transform_edt(self.mtn)
        self.d_mtn_in = d_in
        if self.mtn.any():
            # the ground beside each mountain sample (water counts as level 0),
            # smoothed so a mass spanning two shelves has no seam in it
            ground = np.where(land, h, LEVEL_H[0]).astype(np.float32)
            gi = ndimage.distance_transform_edt(self.mtn, return_distances=False, return_indices=True)
            g_near = ground[gi[0], gi[1]]
            g_soft = _mtn_ground(ground, self.mtn)
            old = MTN_BASE + np.minimum(d_in, 40) * MTN_DOME + ridge * 8 + ridge2 * 3
            hm = g_soft + (old - g_soft) * MTN_SCALE          # the old relief, a third as high
            floor_ = g_near + MTN_MIN
            k_ = 0.5                       # a soft floor: never dips under it
            hm = floor_ + k_ * np.logaddexp(0, (hm - floor_) / k_)
            h[self.mtn] = hm[self.mtn]
            self.mtn_rise = (hm - g_near)[self.mtn]
            self.mtn_rise_old = (old - g_near)[self.mtn]
        # water: a bed, sloped banks, waded fords on top
        d_bank = ndimage.distance_transform_edt(self.water)
        bank_w = 7.0 + 2.5 * np.tanh(self.noise(26, 83))
        bt = np.clip(d_bank / bank_w, 0, 1)
        h_w = LEVEL_H[0] - (LEVEL_H[0] - BED) * (bt * bt * (3 - 2 * bt))
        h_w += self.noise(7, 84) * 0.25 * bt * (1 - bt) * 4
        h[self.water] = h_w[self.water]
        d_core = ndimage.distance_transform_edt(~self.wade)
        top = np.clip(FORD_H + self.noise(9, 86) * 0.07 + self.noise(30, 87) * 0.05, WATER - WADE + 0.07, WATER - 0.18)
        h = np.where(self.water, np.maximum(h, top - d_core * 0.45), h)
        # ramps (Fiendstone's: a straight slope across the border)
        for cy, cx, ia, ib in self.ramp_info:
            hi, lo = (ia, ib) if lvl_arr[ia] > lvl_arr[ib] else (ib, ia)
            y0, y1 = max(cy - 50, 0), min(cy + 51, self.NZ)
            x0, x1 = max(cx - 50, 0), min(cx + 51, self.NX)
            rw = reg[y0:y1, x0:x1]
            is_hi, is_lo = rw == hi, rw == lo
            s = np.where(is_hi, ndimage.distance_transform_edt(~is_lo), -ndimage.distance_transform_edt(~is_hi))
            sg = ndimage.gaussian_filter(s.astype(np.float32), 3)
            gy_, gx_ = np.gradient(sg)
            ly, lx_ = cy - y0, cx - x0
            nrm = np.array([gy_[ly, lx_], gx_[ly, lx_]]); nrm /= (np.linalg.norm(nrm) + 1e-6)
            tang = np.array([-nrm[1], nrm[0]])
            yy_, xx_ = np.mgrid[y0:y1, x0:x1]
            lateral = np.abs((yy_ - cy) * tang[0] + (xx_ - cx) * tang[1])
            dl = abs(lvl_arr[hi] - lvl_arr[lo])
            depth = self.ramp_depth * max(1, dl * 0.8)
            zone = (is_hi | is_lo) & (np.abs(s) <= depth) & (lateral <= self.ramp_half)
            t = np.clip((s + depth) / (2.0 * depth), 0, 1)
            rh = LH[lvl_arr[lo]] + (LH[lvl_arr[hi]] - LH[lvl_arr[lo]]) * t
            hw = h[y0:y1, x0:x1]
            hw[zone] = rh[zone]
            ramp_mask[y0:y1, x0:x1] |= zone
        self.ramp_mask = ramp_mask
        h = self._fold_field(h, 1).astype(np.float32)
        self.ramp_mask = self._fold_field(ramp_mask.astype(np.float32), 0) > 0.5
        self.h = ndimage.gaussian_filter(h, 0.6)
        self._forest()
        self._checks()

    def _walk(self, forest=None):
        h = self.h
        st = 1
        gx_ = (np.roll(h, -st, 1) - np.roll(h, st, 1)) / 2.0
        gz_ = (np.roll(h, -st, 0) - np.roll(h, st, 0)) / 2.0
        slope = np.hypot(gx_, gz_)
        self.slope = slope
        w = (h > WATER - WADE) & (slope <= 1.0) & ~self.mtn
        if forest is not None: w &= ~forest
        w[0, :] = w[-1, :] = w[:, 0] = w[:, -1] = False
        return w

    def _forest(self):
        walk0 = self._walk()
        banned = np.zeros(self.region.shape, bool)
        for rec in self.R:
            if rec["canon"] in self.forest_banned or rec["type"] == "Start" or rec["kind"] == "ford":
                banned |= self.region == rec["id"]
        d_start = np.full(self.region.shape, 1e9)
        for x, z in self.world_starts():
            r, c = self.img(x, z)
            d_start = np.minimum(d_start, np.hypot(self.rows - r, self.cols - c))
        patch = self.noise(30, 51) * 0.65 + self.noise(11, 52) * 0.35
        patch /= patch.std()
        near = np.clip(1 - np.minimum(self.d_mtn, self.d_cliff) / 40.0, 0, 1) + np.clip(1 - self.d_water / 20.0, 0, 1) * 0.5
        want = patch + near * 0.8          # forest_cover is the threshold: ~1.3 = clumps on a tenth of the open ground
        d_ramp = ndimage.distance_transform_edt(~self.ramp_mask)
        d_wade = ndimage.distance_transform_edt(~self.wade)
        base = self.land & ~banned & (d_ramp > 22) & (d_wade > 26) & (d_start > 40) & walk0
        for thr in (self.forest_cover, self.forest_cover + 0.3, self.forest_cover + 0.6, 2.5, 99):
            f = base & (want > thr)
            f = ndimage.binary_opening(f, iterations=2)
            lab, n = ndimage.label(f)
            if n:
                sizes = ndimage.sum(f, lab, range(1, n + 1))
                f = np.isin(lab, 1 + np.nonzero(sizes >= 180)[0])
            f = self._fold_field(f.astype(np.float32), 0) > 0.5
            if not self._unreachable(self._walk(f)): break
            print("forest at", thr, "cuts off", self._unreachable(self._walk(f))[:6], "- thinning")
        self.forest = f

    def world_starts(self):
        out = []
        for canon, (x, z) in self.start_pts.items():
            for g in self.G:
                p = g @ np.array([x, z])
                if not any(np.hypot(p[0] - q[0], p[1] - q[1]) < 1 for q in out): out.append((float(p[0]), float(p[1])))
        return out

    def homes(self):
        """[(region id, (x, z))] in seat order: copy-major per home canon."""
        out = []
        for canon, (x, z) in self.start_pts.items():
            for k, g in enumerate(self.G):
                rid = self._rid(canon, k)
                p = g @ np.array([x, z])
                if all(rid != o[0] for o in out): out.append((rid, (float(p[0]), float(p[1]))))
        return out

    def _unreachable(self, walk):
        lab, _ = ndimage.label(walk)
        homes = self.homes()
        r, c = self.img(*homes[0][1])
        root = lab[r, c]
        if root == 0:
            sub = lab[r - 6:r + 7, c - 6:c + 7]; root = sub[sub > 0][0] if (sub > 0).any() else 0
        bad = []
        for rec in self.R:
            m = (self.region == rec["id"]) & walk
            if not m.any() or not (lab[m] == root).any(): bad.append(rec["code"])
        return bad

    def _checks(self):
        self.walk = self._walk(self.forest)
        self.cut = self._unreachable(self.walk)
        reg = self.region
        nR = len(self.R)
        cnt = {}
        for a_, b_ in ((reg[:, 1:], reg[:, :-1]), (reg[1:, :], reg[:-1, :])):
            mm = (a_ != b_) & (a_ >= 0) & (b_ >= 0)
            pairs = np.minimum(a_[mm], b_[mm]) * nR + np.maximum(a_[mm], b_[mm])
            u, c = np.unique(pairs, return_counts=True)
            for p, n in zip(u.tolist(), c.tolist()): cnt[p] = cnt.get(p, 0) + n
        self.adj = {(p // nR, p % nR) for p, n in cnt.items() if n >= 3}
        nb = {i: set() for i in range(nR)}
        for a, b in self.adj: nb[a].add(b); nb[b].add(a)
        self.nb = nb
        self.lonely = [self.R[i]["code"] for i in range(nR) if not nb[i]]
        seen = set(); q = deque([self.homes()[0][0]]); seen.add(q[0])
        while q:
            i = q.popleft()
            for j in nb[i]:
                if j not in seen: seen.add(j); q.append(j)
        self.unclaimable = [self.R[i]["code"] for i in range(nR) if i not in seen]
        self.area = {rec["id"]: int((reg == rec["id"]).sum()) for rec in self.R}
        self._distances()
        self._node_probe()
        # cliffs the slope rule does not block (a leak), ramps it does (a wall)
        blk = self.slope > 1.0
        cl = self.cliff & ~ndimage.binary_dilation(self.ramp_mask, iterations=3)
        self.cliff_leaks = int((cl & ~ndimage.binary_dilation(blk, iterations=2)).sum())

    def _distances(self):
        from skimage.graph import MCP_Geometric
        st = 2
        cost = np.where(self.walk[::st, ::st], 1.0, np.inf)
        homes = self.homes()
        self.dist = {}
        for i, (rid, p) in enumerate(homes):
            r, c = self.img(*p)
            m = MCP_Geometric(cost)
            cum, _ = m.find_costs([(r // st, c // st)])
            row = []
            for j, (rj, pj) in enumerate(homes):
                rr, cc = self.img(*pj)
                v = cum[rr // st, cc // st]
                row.append(None if not np.isfinite(v) else int(v * st))
            self.dist[i] = row
        self._cum0 = None

    def _node_probe(self):
        reg = self.region
        imp = ~self.walk | self.water
        imp = np.pad(imp, 1, constant_values=True)
        d_imp = ndimage.distance_transform_edt(~imp)[1:-1, 1:-1]
        starts = [p for _, p in self.homes()]
        d_st = np.full(reg.shape, 1e9)
        for x, z in starts:
            r, c = self.img(x, z)
            d_st = np.minimum(d_st, np.hypot(self.rows - r, self.cols - c))
        home_of = {rid: p for rid, p in self.homes()}
        self.nodes = {}
        rng = np.random.default_rng(5)
        for rec in self.R:
            need = NODES[rec["type"]]
            rid = rec["id"]
            m = reg == rid
            if need == 0 or not m.any():
                self.nodes[rid] = (0, need, []); continue
            yy, xx = np.nonzero(m)
            ext = min(yy.max() - yy.min(), xx.max() - xx.min()) + 1
            cap = max(0.0, ext * 0.5 * MARGIN_SHARE)
            band, inset = min(BORDER_BAND, cap), min(BORDER_INSET, cap)
            y0, y1, x0, x1 = max(yy.min() - 20, 0), yy.max() + 21, max(xx.min() - 20, 0), xx.max() + 21
            other = (reg[y0:y1, x0:x1] >= 0) & (reg[y0:y1, x0:x1] != rid)
            d_o = ndimage.distance_transform_edt(~other)
            ok = m[y0:y1, x0:x1] & (d_o >= band + FOOT) & (d_imp[y0:y1, x0:x1] >= inset) \
                & (d_st[y0:y1, x0:x1] >= START_CLEAR + FOOT) & self.land[y0:y1, x0:x1] & self.walk[y0:y1, x0:x1]
            if rid in home_of:
                hr, hc = self.img(*home_of[rid])
                ok &= np.maximum(np.abs(self.rows[y0:y1, x0:x1] - hr), np.abs(self.cols[y0:y1, x0:x1] - hc)) >= HOME_OFFSET
            if rec["type"] in ("NormalVeilstone", "VeilstoneRich"):
                # an outcrop needs its four Trading Outpost side slots (4 m
                # posts flush N/E/S/W): a 12 m square of its own buildable
                # ground round its centre (TerritoryResources _needPostRoom)
                mine_ok = (m & self.land & self.walk)[y0:y1, x0:x1]
                ok &= ndimage.binary_erosion(mine_ok, structure=np.ones((13, 13), bool))
            if rec["type"] == "Start":
                # the home's one outcrop needs its four post slots as well
                mine_ok = (m & self.land & self.walk)[y0:y1, x0:x1]
                post_ok = ok & ndimage.binary_erosion(mine_ok, structure=np.ones((13, 13), bool))
                if not post_ok.any():
                    self.nodes[rid] = (need - 1, need, []); continue
            self.legal = getattr(self, "legal", {}); self.legal[rid] = int(ok.sum())
            cy, cx = np.nonzero(ok)
            # a few random orders, best kept: the runtime retries its random
            # tries too, so one unlucky greedy order is not a real shortfall
            placed = []
            for _try in range(4):
                order = rng.permutation(len(cy))
                got_ = []
                for j in order:
                    p = (cy[j] + y0, cx[j] + x0)
                    if all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 >= NODE_SPACING ** 2 for q in got_):
                        got_.append(p)
                        if len(got_) >= need: break
                if len(got_) > len(placed): placed = got_
                if len(placed) >= need: break
            if rec["type"] == "Start" and ok.sum() < HOME_LEGAL_MIN:
                # the runtime lays nodes by random tries in the bounding box:
                # a home whose legal ground is a thin band can come up short
                # in game though a patient search finds room (Broken Oath's
                # Bastions, 655 m2, lost a node each, 2026-10-18)
                self.nodes[rid] = (min(len(placed), need - 1), need, placed); continue
            self.nodes[rid] = (len(placed), need, placed)
        self.node_short = [(self.R[r]["code"], got, need) for r, (got, need, _) in self.nodes.items() if got < need]

    # ── report ──────────────────────────────────────────────────────────────
    def report(self):
        print(f"== {self.title}: {self.sx} x {self.sz} m, {len(self.R)} territories, {len(self.homes())} seats")
        print("unreachable on foot:", self.cut or "none")
        print("bordering nothing:", self.lonely or "none", "| outside the claim graph:", self.unclaimable or "none")
        print("cliff samples the walk rule does not block:", self.cliff_leaks)
        if self.mtn.any() and hasattr(self, "mtn_rise"):
            r_ = self.mtn_rise
            o_ = self.mtn_rise_old
            print(f"mountain height over the ground beside it: old median {np.median(o_):.1f} max {o_.max():.1f} m"
                  f" -> new min {r_.min():.1f} median {np.median(r_):.1f} max {r_.max():.1f} m"
                  f" (median ratio {np.median(r_ / np.maximum(o_, 1e-3)):.3f})")
        print("node shortfalls:", self.node_short or "none")
        for i, row in self.dist.items(): print(f"  seat {i + 1}:", row)
        if "--table" in sys.argv:
            for rec in self.R:
                if rec["k"] not in (0, None): continue
                got, need, _ = self.nodes[rec["id"]]
                yy, xx = np.nonzero(self.region == rec["id"])
                cen = f"({xx.mean() - self.sx / 2:6.0f},{self.sz / 2 - yy.mean():6.0f})" if len(xx) else "-"
                print(f"    {rec['code']:12} L{rec['level']} {rec['type']:16} {self.area[rec['id']]:6} m2 nodes {got}/{need} legal {getattr(self, 'legal', {}).get(rec['id'], 0):5} at {cen}")
        by = {}
        for rec in self.R:
            by.setdefault(rec["type"], []).append(self.area[rec["id"]])
        for t, a in sorted(by.items()):
            print(f"  {t:16} x{len(a):3}  area min {min(a):6} median {int(np.median(a)):6} max {max(a):6} m2")

    # ── pictures ────────────────────────────────────────────────────────────
    def _fonts(self):
        try:
            return (ImageFont.truetype("arial.ttf", 13), ImageFont.truetype("arialbd.ttf", 22),
                    ImageFont.truetype("arialbd.ttf", 12), ImageFont.truetype("arial.ttf", 11))
        except Exception:
            f = ImageFont.load_default(); return f, f, f, f

    def render_natural(self):
        h, valid = self.h, self.valid
        hs = ndimage.gaussian_filter(h, 1.0)
        gy, gx = np.gradient(hs)
        relief = np.where(self.mtn, 0.11, 0.22)
        shade = np.clip(1.0 + (-gx + gy) * relief, 0.4, 1.5)
        var = self.noise(35, 61) * 0.5 + self.noise(9, 62) * 0.5
        fine = self.noise(2, 63)
        img = np.zeros((self.NZ, self.NX, 3), np.float32)
        lv = np.maximum(self.level, 0)
        g_lo, g_hi = np.array([70, 108, 46]), np.array([136, 164, 76])
        gmix = np.clip(0.25 + self.roll * 0.6 + var * 0.08, 0, 1)[..., None]
        grass = g_lo + (g_hi - g_lo) * gmix
        tint = np.array([[0, 0, 0], [6, 2, -4], [12, 6, -2], [18, 12, 4], [24, 18, 8], [28, 22, 12]], float)
        grass = grass + tint[lv]
        moss = np.clip(self.noise(22, 64) * 0.6 - 0.25, 0, 0.6)[..., None]
        grass = grass * (1 - moss * 0.4) + np.array([62, 86, 44]) * moss * 0.4
        straw = np.clip(self.noise(40, 71) * 0.7 - 0.55, 0, 0.45)[..., None]
        grass = grass * (1 - straw) + np.array([168, 160, 96]) * straw
        img[:] = grass + fine[..., None] * 4
        strata = np.sin(h * 0.9 + self.noise(20, 73) * 2.0) * 2
        rk = self.ridge
        rock = np.stack([104 + rk * 40, 98 + rk * 36, 90 + rk * 32], -1) + (var * 6 + strata)[..., None]
        if self.mtn.any():
            snow = np.clip((h - (h[self.mtn].max() - 7)) / 5.0 + self.noise(6, 74) * 0.15, 0, 1)[..., None]
            rock = rock * (1 - snow * 0.85) + np.array([236, 238, 242]) * snow * 0.85
        mfoot = np.clip(1 - self.d_mtn_in / 4.0, 0, 1)[..., None]
        rock = rock * (1 - mfoot * 0.45) + np.array([126, 120, 100]) * mfoot * 0.45
        img[self.mtn] = rock[self.mtn]
        rp = self.ramp_mask & ~self.water
        img[rp] = img[rp] * 0.3 + (np.array([166, 136, 98]) + fine[rp, None] * 6) * 0.7
        img *= shade[..., None]
        dcl = ndimage.distance_transform_edt(~(self.cliff & ~self.ramp_mask))
        face = self.land & (dcl < 2.5) & ~self.ramp_mask
        img[face] = (np.array([96, 86, 76]) + (self.noise(3, 75) * 10)[..., None])[face] * shade[face, None]
        # forest crowns, lit from the north-west
        rng = np.random.default_rng(7)
        pts = self.forest & (rng.random(self.forest.shape) < 0.05)
        dt = ndimage.distance_transform_edt(~pts)
        canopy = np.clip(1 - (dt / 2.4) ** 2, 0, 1) ** 0.5 * self.forest
        cy_, cx_ = np.gradient(ndimage.gaussian_filter(canopy, 0.8))
        lit = np.clip(1.0 + (-cx_ + cy_) * 1.6, 0.55, 1.4)
        tone = self.noise(8, 76)[..., None]
        fc = (np.array([42, 74, 38]) + tone * np.array([6, 9, 4])) * (0.55 + 0.45 * canopy[..., None]) * lit[..., None]
        img[self.forest] = fc[self.forest]
        fsh = np.roll(np.roll(ndimage.binary_dilation(self.forest, iterations=2), 2, 0), 2, 1) & ~self.forest & ~self.water
        img[fsh] *= 0.72
        depth = np.clip(WATER - h, 0, None)
        deep_c, mid_c, shal_c = np.array([24, 58, 104]), np.array([38, 104, 150]), np.array([98, 150, 150])
        t1 = np.clip(depth / 1.2, 0, 1)[..., None]; t2 = np.clip((depth - 1.2) / 2.0, 0, 1)[..., None]
        wc = shal_c * (1 - t1) + mid_c * t1
        wc = wc * (1 - t2) + deep_c * t2
        show = np.clip(1 - depth / 1.0, 0, 1)[..., None] * 0.5
        wc = wc * (1 - show) + np.array([196, 176, 128]) * show
        wc += (self.noise(4, 67) * 3)[..., None]
        wet = self.water & (h < WATER)
        img[wet] = wc[wet]
        bank = self.water & ~wet
        img[bank] = img[bank] * 0.5 + np.array([150, 130, 100]) * 0.5
        d_dry = ndimage.distance_transform_edt(wet)
        foam = wet & (d_dry < 1.2)
        img[foam] = img[foam] * 0.5 + np.array([236, 240, 236]) * 0.5
        self.natural = np.clip(img, 0, 255).astype(np.uint8)
        Image.fromarray(self.natural).save(os.path.join(self.here, "natural.png"))

    def render_preview(self, notes=()):
        font, big, bold, small = self._fonts()
        plan = self.natural.astype(np.float32)
        reg = self.region
        homes = self.homes()
        seat = {rid: i for i, (rid, _) in enumerate(homes)}
        for rec in self.R:
            m = reg == rec["id"]
            if rec["id"] in seat:
                col, a = PLAYER[seat[rec["id"]] % 8], 0.30
            else:
                col, _ = TYPE_STYLE[rec["type"]]
                a = 0.42 if rec["type"] in RICH else 0.16
            plan[m] = plan[m] * (1 - a) + np.array(col) * a
        border = np.zeros(reg.shape, bool)
        border[:, 1:] |= reg[:, 1:] != reg[:, :-1]
        border[1:, :] |= reg[1:, :] != reg[:-1, :]
        border &= self.valid | np.roll(self.valid, 1, 0) | np.roll(self.valid, 1, 1)
        dash = border & (((self.rows + self.cols) // 3) % 2 == 0)
        plan[dash] = (250, 250, 240)
        plan = np.clip(plan, 0, 255).astype(np.uint8)
        PAD = 420
        H = max(self.NZ, 860)
        canvas = Image.new("RGB", (self.NX + PAD, H), (236, 232, 222))
        canvas.paste(Image.fromarray(plan), (0, 0))
        d = ImageDraw.Draw(canvas)
        # labels at each territory's deepest point
        for rec in self.R:
            m = reg == rec["id"]
            if not m.any() or rec["id"] in seat: continue
            dt = ndimage.distance_transform_edt(np.pad(m, 1))[1:-1, 1:-1]
            r, c = np.unravel_index(np.argmax(dt), dt.shape)
            tag = self.labels.get(rec["canon"]) or TYPE_STYLE[rec["type"]][1]
            got, need, _ = self.nodes[rec["id"]]
            txt2 = f"{self.area[rec['id']] / 1000:.1f}k" + ("" if got >= need else f" !{got}/{need}")
            for t_, dy, f_ in ((tag, -6, bold), (txt2, 7, small)):
                d.text((c + 1, r + dy + 1), t_, fill=(0, 0, 0), font=f_, anchor="mm")
                d.text((c, r + dy), t_, fill=(255, 255, 255) if "!" not in t_ else (255, 120, 120), font=f_, anchor="mm")
        for i, (rid, (x, z)) in enumerate(homes):
            r, c = self.img(x, z)
            d.rectangle((c - 6, r - 6, c + 6, r + 6), fill=PLAYER[i % 8], outline=(0, 0, 0), width=2)
            d.text((c, r - 18), str(i + 1), fill=(255, 255, 255), font=big, anchor="mm", stroke_width=2, stroke_fill=(0, 0, 0))
        # nodes the probe could lay (dots), to show room
        for rid, (_, _, pl) in self.nodes.items():
            for (r, c) in pl:
                d.ellipse((c - 1.5, r - 1.5, c + 1.5, r + 1.5), fill=(255, 255, 255))
        # 50 m scale bar
        x0, y0 = 14, self.NZ - 18
        d.rectangle((x0 - 4, y0 - 14, x0 + 50 + 34, y0 + 8), fill=(0, 0, 0))
        d.line((x0, y0, x0 + 50, y0), fill=(255, 255, 255), width=3)
        d.text((x0 + 56, y0), "50 m", fill=(255, 255, 255), font=font, anchor="lm")

        lx, y = self.NX + 16, 14
        d.text((lx, y), self.title, fill=(0, 0, 0), font=big); y += 30
        d.text((lx, y), f"{len(homes)} players, {self.sx} x {self.sz} m, {len(self.R)} territories", fill=(0, 0, 0), font=font); y += 24
        counts = {}
        for rec in self.R: counts[rec["type"]] = counts.get(rec["type"], 0) + 1
        for t in ("Start", "Normal", "NormalIron", "Empty", "NormalVeilstone", "IronRich", "VeilstoneRich", "Sanctum"):
            if t not in counts: continue
            col, tag = TYPE_STYLE[t]
            a = [self.area[r["id"]] for r in self.R if r["type"] == t]
            d.rectangle((lx, y, lx + 20, y + 14), fill=col, outline=(0, 0, 0))
            d.text((lx + 28, y), f"{tag:6} {t} x{counts[t]}   {min(a) / 1000:.1f}-{max(a) / 1000:.1f}k m2",
                   fill=(0, 0, 0), font=font); y += 19
        y += 4
        for lab_, col in [("Mountain (unclaimable)", (140, 132, 122)), ("Deep water (unclaimable)", (30, 76, 124)),
                          ("Ford / shallows (waded)", (120, 160, 160)), ("Cliff face", (80, 72, 64)),
                          ("Ramp", (170, 140, 100)), ("Forest (impassable)", (46, 82, 42))]:
            d.rectangle((lx, y, lx + 20, y + 14), fill=col, outline=(0, 0, 0)); d.text((lx + 28, y), lab_, fill=(0, 0, 0), font=font); y += 19
        y += 6
        d.text((lx, y), "Strong tint = rich (veilstone, iron, curse, sanctum);", fill=(0, 0, 0), font=small); y += 15
        d.text((lx, y), "faint = poor. Label: type, area. Dots = nodes the", fill=(0, 0, 0), font=small); y += 15
        d.text((lx, y), "probe could lay; !a/b = short of room.", fill=(0, 0, 0), font=small); y += 22
        for line in notes:
            d.text((lx, y), line, fill=(0, 0, 0), font=font); y += 17
        y += 8
        d.text((lx, y), "Walking distance between homes (m):", fill=(0, 0, 0), font=font); y += 18
        for i, row in self.dist.items():
            d.text((lx, y), f"{i + 1}: " + " ".join("-" if v is None else str(v) for j, v in enumerate(row) if j != i),
                   fill=(0, 0, 0), font=small); y += 15
        y += 8
        probs = []
        if self.cut: probs.append(f"unreachable {self.cut[:4]}")
        if self.lonely: probs.append(f"isolated {self.lonely[:4]}")
        if self.unclaimable: probs.append(f"outside claim graph {self.unclaimable[:4]}")
        if self.node_short: probs.append(f"{len(self.node_short)} short of node room")
        if self.cliff_leaks > 40: probs.append(f"{self.cliff_leaks} cliff leaks")
        ok = not probs
        for line in (["All territories reachable, connected, room for nodes"] if ok else probs):
            d.text((lx, y), line, fill=(0, 120, 0) if ok else (200, 0, 0), font=font); y += 17
        canvas.save(os.path.join(self.here, "preview.png"))

    # ── export (only with --export) ─────────────────────────────────────────
    def export(self, map_name, display):
        from skimage import measure
        here = self.here
        S = max(self.sx, self.sz)
        hres = 513
        while hres - 1 < S: hres = (hres - 1) * 2 + 1
        assert self.h.max() < MAX_H, self.h.max()
        gx_w = -self.sx / 2 + np.arange(hres) * self.sx / (hres - 1)
        gz_w = -self.sz / 2 + np.arange(hres) * self.sz / (hres - 1)
        GX, GZ = np.meshgrid(gx_w, gz_w)
        hu = np.clip(ndimage.map_coordinates(self.h, [self.sz / 2 - GZ, GX + self.sx / 2], order=1, mode="nearest") / MAX_H, 0, 1)
        (hu * 65535 + 0.5).astype("<u2").tofile(os.path.join(here, "heights.r16"))
        def save(name, m):
            Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)).save(os.path.join(here, name))
        save("nowalk.png", (self.mtn | self.forest).astype(float))
        save("forest.png", self.forest.astype(float))
        bankish = ndimage.binary_dilation(self.water, iterations=3) & ~ndimage.binary_erosion(self.water, iterations=8)
        save("dirt.png", (self.ramp_mask | bankish | (ndimage.binary_dilation(self.wade, iterations=6) & self.water)).astype(float))
        save("fringe.png", (self.mtn & (self.d_mtn_in < 7)).astype(float))
        d_forest = ndimage.distance_transform_edt(~self.forest)
        hollow = np.clip((0.5 - self.roll) * 2.4, 0, 1)
        moss = np.maximum(hollow, np.clip(1 - d_forest / 14.0, 0, 1) * 0.8)
        moss = np.maximum(moss, np.clip(1 - np.minimum(self.d_cliff, self.d_mtn) / 10.0, 0, 1) * 0.6)
        moss = moss * (1 - np.clip(self.noise(6, 97) * 0.25 + 0.1, 0, 0.45)) * self.land
        bare = np.clip(self.noise(26, 98) * 0.75 - 0.6, 0, 0.6) * self.land * (ndimage.distance_transform_edt(~self.ramp_mask) > 14)
        save("meadow.png", ndimage.gaussian_filter(moss, 1.5))
        save("bare.png", ndimage.gaussian_filter(bare, 1.5))
        home_pt = {rid: p for rid, p in self.homes()}
        regions_out = []
        for rec in self.R:
            m = self.region == rec["id"]
            if not m.any(): continue
            lab, n = ndimage.label(m)
            if n > 1:
                sizes = ndimage.sum(m, lab, range(1, n + 1)); m = lab == (1 + int(np.argmax(sizes)))
            c = max(measure.find_contours(np.pad(m, 1).astype(np.float32), 0.5), key=len)
            c = measure.approximate_polygon(c, tolerance=1.2)[:-1]
            shape = []
            for (row, col) in c:
                shape += [round(float(col - 1 - self.sx / 2), 2), round(float(self.sz / 2 - (row - 1)), 2)]
            if rec["id"] in home_pt:
                sxw, szw = home_pt[rec["id"]]
            else:
                # a land territory's seed stands on its land (it may hold half
                # a ford now); a shallows territory's in its waded water
                ground = (self.walk & self.wade) if rec["kind"] == "ford" else (self.walk & self.land)
                dt = ndimage.distance_transform_edt(np.pad(m & ground, 1))[1:-1, 1:-1]
                sr, sc = np.unravel_index(np.argmax(dt), dt.shape)
                sxw, szw = float(sc - self.sx / 2), float(self.sz / 2 - sr)
            # a waded ford stands under the water, below level 0: it gets its
            # own entry at the end of levelHeights so the builder's seed check
            # knows where its ground is
            lv = FORD_LEVEL if rec["kind"] == "ford" else int(rec["level"])
            regions_out.append({
                "code": rec["code"], "name": display(rec), "level": lv,
                "kind": "PlayerStart" if rec["type"] == "Start" else "Normal",
                "resources": rec["type"],
                "pinCurse": bool(self.pin_curse and rec["type"] == "VeilstoneRich"),
                # a shallows territory (ford_paint): its waded water is
                # buildable (RegionSeedMarker.BuildableShallows, 2026-10-19)
                "buildableShallows": rec["kind"] == "ford",
                "seedX": round(sxw, 2), "seedZ": round(szw, 2), "shape": shape,
            })
        starts_out = []
        for i, (rid, (x, z)) in enumerate(self.homes()):
            starts_out.append({"faction": FACTIONS[i], "x": round(x, 2), "z": round(z, 2),
                               "region": next(r["name"] for r in regions_out if r["code"] == self.R[rid]["code"])})
        wz_rows = np.nonzero((self.water & ~self.wade).any(1) | self.wade.any(1))[0]
        if len(wz_rows):
            wmin = float(self.sz / 2 - wz_rows.max()) - 3.0
            wmax = float(self.sz / 2 - wz_rows.min()) + 3.0
        else:   # no water: a sliver under the southern mountains, never seen
            wmin, wmax = -self.sz / 2, -self.sz / 2 + 2.0
        layout = {
            "mapName": map_name, "size": self.sx, "sizeZ": self.sz if self.sz != self.sx else 0,
            "heightRes": hres, "maxHeight": MAX_H,
            "levelHeights": LEVEL_H + [FORD_H], "rollHeights": ROLL_H + [0.3],
            "waterLevel": WATER, "wadeDepth": WADE,
            "waterMinZ": round(max(wmin, -self.sz / 2), 2), "waterMaxZ": round(min(wmax, self.sz / 2), 2),
            "regions": regions_out, "starts": starts_out,
        }
        with open(os.path.join(here, "layout.json"), "w", encoding="utf-8") as f:
            json.dump(layout, f, indent=1, ensure_ascii=False)
        print("exported", len(regions_out), "territories, heightRes", hres)

    def finish(self, map_name, display, notes=()):
        self.build()
        self.report()
        self.render_natural()
        self.render_preview(notes)
        print("saved natural.png and preview.png in", self.here)
        if "--export" in sys.argv:
            self.export(map_name, display)
