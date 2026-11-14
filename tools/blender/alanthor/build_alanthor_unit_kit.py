# The Alanthor UNIT KIT (2026-11-11): purpose-built, unit-scale pieces for the
# Roman / Japanese Alanthor look — thin-eaved kawara roofs with small tiles
# in corroded copper (verdigris: "Alanthor has roofs tiled in cyan"),
# slender marble columns, a battered stone base, thin plaster walls with a
# player-colour band. Everything is PROCEDURAL (no Synty mesh is read), so
# every thickness is chosen here. Pieces are authored 1 : 1: a building made
# of them is composed at footprint fit 1.0 and Synty props stand beside them
# at their native (unit) scale.
#
#   blender -b --python tools/blender/alanthor/build_alanthor_unit_kit.py -- [out dir]
#
# Out dir defaults to Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/UnitKit.
# Writes one FBX per piece, UK_Atlas.png (the one painted texture) and
# uk_manifest.json (piece -> material slots + facts the Unity side reports).
# Then: Waning Border > Art > Bind Unit Kit (UnitKitBinder.cs) makes a prefab
# per FBX with the materials on.
#
# AUTHORING SPACE is Unity's: x right, y up, z forward; a piece's "outside" /
# front is -Z, its pivot at the bottom centre unless noted. Vertices are
# converted to Blender on write (x, y, z) -> (-x, -z, y); the FBX settings are
# the Oriental kit's, which bring that back to Unity as authored.
#
# Material slots: Atlas (UK_Atlas.png, U repeats, each surface kind is a
# horizontal strip), Band (the player colour: a recessed chest-high band on
# the plaster walls, bound to a *playercolor* material), Glow (lantern panes).
#
# The atlas strips repeat along U only. A roof is therefore built course by
# course — each course of tiles is its own stepped strip of geometry mapped
# once across a tile strip — which is also what gives the roof its fine,
# shadowed surface. Painted, not modelled: tile ribs, tile-by-tile colour,
# plaster grime rising from the base, stone courses, marble veins.

import bpy, bmesh, sys, os, math, json, zlib, struct
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if len(argv) > 0 else os.path.join(ROOT, "Assets", "GameData", "TechTree", "Civs", "Alanthor", "Buildings", "Art", "UnitKit")
os.makedirs(OUT, exist_ok=True)

SLOTS = ["Atlas", "Band", "Glow"]
MANIFEST = {}
RNG = np.random.default_rng(20261111)

# ─── the atlas ───────────────────────────────────────────────────────────────
SIZE = 1024
# name: (top row, height in px, metres per full width)
STRIPS = {
    "tile0": (0, 80, 3.52), "tile1": (80, 80, 3.52), "tile2": (160, 80, 3.52),
    "ridge": (240, 32, 2.0),
    "plaster": (272, 224, 4.0),
    "stone": (496, 224, 4.0),
    "marble": (720, 96, 2.0),
    "timber": (816, 48, 2.0),
    "door": (864, 112, 1.76),
    "swatch": (976, 48, 1.0),
}
SWATCH = {"gold": 0, "iron": 1, "earth": 2, "dark": 3, "paper": 4, "flag": 5, "soffit": 6, "moss": 7}
TILE_W, TILES_ACROSS = 0.22, 16


def uv(strip, u_m, t):
    """u in metres along the strip (repeats), t 0..1 from the strip's bottom to its top."""
    top, h, period = STRIPS[strip]
    inset = 2.0
    v_px = top + h - inset - t * (h - 2 * inset)
    return (u_m / period, 1.0 - v_px / SIZE)


def sw(name):
    top, h, _ = STRIPS["swatch"]
    return ((SWATCH[name] + 0.5) / 8.0, 1.0 - (top + h * 0.5) / SIZE)


def periodic_noise(h, w, cells_y, cells_x):
    """Smooth value noise that wraps in x."""
    g = RNG.random((cells_y + 1, cells_x))
    ys = np.linspace(0, cells_y, h, endpoint=False)
    xs = np.linspace(0, cells_x, w, endpoint=False)
    y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    x1 = (x0 + 1) % cells_x; y1 = np.minimum(y0 + 1, cells_y)
    a = g[y0][:, x0]; b = g[y0][:, x1]; c = g[y1][:, x0]; d = g[y1][:, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def paint_atlas():
    img = np.zeros((SIZE, SIZE, 3), np.float32)

    # Kawara in CORRODED COPPER (verdigris): a dull cyan-green patina, chalky mint
    # where the rain runs, dark teal in the joints and under the overlaps, and
    # warm bronze showing through on a minority of tiles and on the worn lower
    # lip. Greener and duller than veilstone (a glowing cyan) on purpose.
    patina = np.array([0.30, 0.56, 0.49]); mint = np.array([0.60, 0.78, 0.68])
    teal = np.array([0.07, 0.19, 0.18]); bronze = np.array([0.43, 0.29, 0.15])
    for k in range(3):
        top, h, _ = STRIPS[f"tile{k}"]
        cw = SIZE // TILES_ACROSS
        streaks = periodic_noise(h, SIZE, 2, 110)                                # thin runs down the slope
        blotch = periodic_noise(h, SIZE, 6, 40)
        for i in range(TILES_ACROSS):
            base = patina * (0.80 + 0.36 * RNG.random()) + np.array([0.0, 0.03, -0.02]) * (RNG.random() - 0.5) * 2
            r = RNG.random()
            worn = 0.0
            if r < 0.12: worn = 0.55 + 0.35 * RNG.random()                       # the patina has worn through to bronze
            elif r < 0.30: base = base * 0.55 + mint * 0.45                      # a chalky, pale tile
            elif r < 0.42: base = base * 0.72 + teal * 0.20                      # a dark, wet one
            x = np.arange(cw) / cw
            rib = 0.70 + 0.42 * np.clip(np.cos((x - 0.42) * math.pi * 1.35), 0, 1) ** 1.5    # the round tile's soft highlight
            y = np.arange(h) / h                                                 # 0 = up-slope
            fall = 0.66 + 0.42 * y                                               # shaded under the course above
            cell = np.empty((h, cw, 3), np.float32)
            cell[:] = base[None, None, :]
            s_ = streaks[:, i * cw:(i + 1) * cw]; b_ = blotch[:, i * cw:(i + 1) * cw]
            m = np.clip((s_ - 0.52) * 3.2, 0, 1)[:, :, None] * 0.55               # mint rain streaks
            cell = cell * (1 - m) + mint[None, None, :] * m
            w = np.clip(worn * (0.55 + 0.9 * b_) - 0.25, 0, 0.85)[:, :, None]     # bronze showing through
            cell = cell * (1 - w) + bronze[None, None, :] * w
            cell *= rib[None, :, None] * fall[:, None, None]
            # joints: dark teal between tiles and under the overlap; a worn bronze lip
            for cols in (slice(0, 3), slice(cw - 3, cw)):
                cell[:, cols] = cell[:, cols] * 0.35 + teal[None, None, :] * 0.65
            cell[:7] = cell[:7] * 0.45 + teal[None, None, :] * 0.55
            lip = np.linspace(0.0, 0.5, 6)[:, None, None] * (0.5 + RNG.random())
            cell[-8:-2] = cell[-8:-2] * (1 - lip) + (bronze * 1.15)[None, None, :] * lip
            cell[-2:] = cell[-2:] * 0.4 + teal[None, None, :] * 0.6
            cell *= (0.95 + 0.10 * RNG.random((h, cw, 1)))
            img[top:top + h, i * cw:(i + 1) * cw] = cell

    # Ridges, hips, eave edges, fins: weathered copper going to bronze.
    top, h, _ = STRIPS["ridge"]
    n = periodic_noise(h, SIZE, 3, 40); n2 = periodic_noise(h, SIZE, 2, 18)
    rr = (patina * 0.62)[None, None, :] * (0.8 + 0.45 * n[:, :, None])
    wr = np.clip((n2 - 0.42) * 2.6, 0, 0.8)[:, :, None]
    rr = rr * (1 - wr) + (bronze * 0.95)[None, None, :] * wr
    img[top:top + h] = rr
    img[top:top + h, ::64] *= 0.5; img[top:top + h, 1::64] *= 0.5              # joints of the ridge tiles

    # Plaster: warm off-white; rain streaks from the top, grime rising from the base.
    top, h, _ = STRIPS["plaster"]
    y = (np.arange(h) / h)[:, None]                                            # 0 top .. 1 base
    blot = periodic_noise(h, SIZE, 7, 20); fine = periodic_noise(h, SIZE, 40, 160)
    streak = periodic_noise(h, SIZE, 2, 90)
    lum = 0.93 - 0.07 * blot - 0.035 * fine
    lum -= 0.10 * np.clip(streak - 0.55, 0, 1) * 2.2 * np.clip(1 - y * 1.6, 0, 1)      # streaks under the eaves
    grime = np.clip((y - 0.62) / 0.38, 0, 1) ** 1.4 * (0.65 + 0.7 * periodic_noise(h, SIZE, 3, 30))
    col = np.stack([lum, lum * 0.975, lum * 0.925], -1)
    dirt = np.array([0.42, 0.36, 0.28])
    col = col * (1 - 0.8 * np.clip(grime, 0, 1)[:, :, None]) + dirt[None, None, :] * 0.8 * np.clip(grime, 0, 1)[:, :, None]
    img[top:top + h] = col

    # Stone: irregular courses, per-stone colour, dark joints, dirt and moss at the foot.
    top, h, _ = STRIPS["stone"]
    rows = [30, 26, 34, 28, 38, 32, 36]
    yy = 0
    stone = np.zeros((h, SIZE, 3), np.float32)
    for ri, rh in enumerate(rows):
        x = int(RNG.integers(0, 60))
        while x < SIZE + 60:
            wpx = int(RNG.integers(44, 110))
            c = np.array([0.50, 0.48, 0.45]) * (0.78 + 0.42 * RNG.random()) + np.array([0.04, 0.02, 0.0]) * RNG.random()
            for xx in range(x, x + wpx):
                stone[yy:yy + rh, xx % SIZE] = c
            for xx in (x, x + 1):
                stone[yy:yy + rh, xx % SIZE] *= 0.45
            x += wpx
        stone[yy:yy + 2] *= 0.45
        stone[yy + 2:yy + 5] *= 1.12                                           # the lit top arris of each course
        yy += rh
    stone *= (0.86 + 0.28 * periodic_noise(h, SIZE, 30, 120))[:, :, None]
    y = (np.arange(h) / h)[:, None]
    foot = np.clip((y - 0.55) / 0.45, 0, 1) ** 1.3 * (0.6 + 0.8 * periodic_noise(h, SIZE, 3, 26))
    foot = np.clip(foot, 0, 1)[:, :, None]
    stone = stone * (1 - 0.75 * foot) + np.array([0.30, 0.27, 0.19])[None, None, :] * 0.75 * foot
    moss = np.clip((y - 0.86) / 0.14, 0, 1) * np.clip(periodic_noise(h, SIZE, 4, 40) - 0.45, 0, 1) * 2.0
    stone = stone * (1 - moss[:, :, None]) + np.array([0.22, 0.30, 0.14])[None, None, :] * moss[:, :, None]
    img[top:top + h] = stone

    # Marble: warm white, soft grey-gold veins.
    top, h, _ = STRIPS["marble"]
    a = periodic_noise(h, SIZE, 5, 14); b = periodic_noise(h, SIZE, 12, 40)
    vein = np.abs(np.sin((a * 5.0 + b * 1.2 + np.linspace(0, 6, SIZE)[None, :] * 0) * math.pi))
    vein = np.clip(1 - vein * 6.0, 0, 1) ** 2
    lum = 0.95 - 0.04 * b
    col = np.stack([lum, lum * 0.985, lum * 0.95], -1)
    col = col * (1 - 0.35 * vein[:, :, None]) + np.array([0.62, 0.58, 0.50])[None, None, :] * 0.35 * vein[:, :, None]
    col[-10:] *= np.linspace(1, 0.82, 10)[:, None, None]                       # a foot shadow
    img[top:top + h] = col

    # Timber: dark, long grain.
    top, h, _ = STRIPS["timber"]
    g = periodic_noise(h, SIZE, 14, 5)
    img[top:top + h] = np.array([0.20, 0.135, 0.09])[None, None, :] * (0.7 + 0.6 * g[:, :, None])

    # Door: vertical planks, two iron straps, studs.
    top, h, _ = STRIPS["door"]
    planks = 8; pw = SIZE // planks
    d = np.zeros((h, SIZE, 3), np.float32)
    for i in range(planks):
        c = np.array([0.33, 0.21, 0.12]) * (0.8 + 0.4 * RNG.random())
        d[:, i * pw:(i + 1) * pw] = c
        d[:, i * pw:i * pw + 4] *= 0.45
    d *= (0.85 + 0.3 * periodic_noise(h, SIZE, 3, 60))[:, :, None]
    for band in (0.22, 0.74):
        r0 = int(band * h)
        d[r0:r0 + 9] = np.array([0.10, 0.10, 0.11])
        for sx in range(pw // 2, SIZE, pw):
            d[r0 + 2:r0 + 7, sx - 5:sx + 5] = np.array([0.34, 0.33, 0.32])
    d[-8:] *= 0.7
    img[top:top + h] = d

    top, h, _ = STRIPS["swatch"]
    cols = {"gold": (0.86, 0.63, 0.18), "iron": (0.11, 0.11, 0.12), "earth": (0.40, 0.34, 0.26), "dark": (0.05, 0.05, 0.06),
            "paper": (0.98, 0.86, 0.60), "flag": (0.85, 0.85, 0.85), "soffit": (0.16, 0.11, 0.08), "moss": (0.25, 0.32, 0.16)}
    for n, i in SWATCH.items():
        img[top:top + h, i * 128:(i + 1) * 128] = cols[n]

    data = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + data[r].tobytes() for r in range(SIZE))

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xffffffff)
    png = bytes([137, 80, 78, 71, 13, 10, 26, 10]) + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(os.path.join(OUT, "UK_Atlas.png"), "wb") as f:
        f.write(png)


# ─── mesh building ───────────────────────────────────────────────────────────
def V(*p):
    return np.array(p, float)


def to_blender(p):
    return (-p[0], -p[2], p[1])


class MB:
    """Faces in Unity space, each with the direction it must face."""
    def __init__(self):
        self.faces = []      # (points, uvs, slot, smooth)

    def face(self, pts, uvs, want, slot="Atlas", smooth=False):
        pts = [np.asarray(p, float) for p in pts]
        # Newell normal in authoring space; flip the order if it faces away.
        n = np.zeros(3)
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            n += np.cross(a, b)
        if np.linalg.norm(n) < 1e-9:
            return
        # Authoring space is left-handed relative to Blender: a face that is
        # counter-clockwise here is clockwise there, so "faces want" here means
        # the cross product points AWAY from want.
        if np.dot(n, np.asarray(want, float)) > 0:
            pts = pts[::-1]; uvs = uvs[::-1]
        self.faces.append((pts, list(uvs), slot, smooth))

    def quad_strip_uv(self, strip, u0, u1, t0=0.0, t1=1.0):
        return [uv(strip, u0, t0), uv(strip, u1, t0), uv(strip, u1, t1), uv(strip, u0, t1)]

    def box(self, lo, hi, strip="timber", swatch=None, faces="xXyYzZ", u_scale=1.0):
        """An axis box. Sides map the strip once over their height, U along their width."""
        x0, y0, z0 = lo; x1, y1, z1 = hi
        def uvs(w):
            if swatch: return [sw(swatch)] * 4
            return self.quad_strip_uv(strip, 0, w * u_scale)
        if "z" in faces: self.face([V(x0, y0, z0), V(x1, y0, z0), V(x1, y1, z0), V(x0, y1, z0)], uvs(x1 - x0), (0, 0, -1))
        if "Z" in faces: self.face([V(x1, y0, z1), V(x0, y0, z1), V(x0, y1, z1), V(x1, y1, z1)], uvs(x1 - x0), (0, 0, 1))
        if "x" in faces: self.face([V(x0, y0, z1), V(x0, y0, z0), V(x0, y1, z0), V(x0, y1, z1)], uvs(z1 - z0), (-1, 0, 0))
        if "X" in faces: self.face([V(x1, y0, z0), V(x1, y0, z1), V(x1, y1, z1), V(x1, y1, z0)], uvs(z1 - z0), (1, 0, 0))
        if "Y" in faces: self.face([V(x0, y1, z0), V(x1, y1, z0), V(x1, y1, z1), V(x0, y1, z1)], uvs(x1 - x0), (0, 1, 0))
        if "y" in faces: self.face([V(x0, y0, z0), V(x1, y0, z0), V(x1, y0, z1), V(x0, y0, z1)], uvs(x1 - x0), (0, -1, 0))

    def sweep(self, pts, w, h, strip="ridge", swatch=None, up=(0, 1, 0), taper_end=1.0):
        """A w x h bar along a polyline (its top h above the line)."""
        pts = [np.asarray(p, float) for p in pts]
        up = np.asarray(up, float)
        secs = []
        for i, p in enumerate(pts):
            d = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
            d /= max(np.linalg.norm(d), 1e-9)
            side = np.cross(up, d); side /= max(np.linalg.norm(side), 1e-9)
            k = 1.0 if len(pts) < 2 else 1.0 + (taper_end - 1.0) * i / (len(pts) - 1)
            a = side * w * 0.5 * k
            secs.append((p - a, p + a, p + a + up * h * k, p - a + up * h * k, p))
        run = 0.0
        for i in range(len(secs) - 1):
            s0, s1 = secs[i], secs[i + 1]
            ln = np.linalg.norm(s1[4] - s0[4])
            for k in range(4):
                a, b = s0[k], s0[(k + 1) % 4]; c, d = s1[(k + 1) % 4], s1[k]
                centre = (a + b + c + d) / 4
                want = centre - (s0[4] + s1[4]) / 2 - up * h * 0.5
                uvs = [sw(swatch)] * 4 if swatch else [uv(strip, run, 0), uv(strip, run, 1), uv(strip, run + ln, 1), uv(strip, run + ln, 0)]
                self.face([a, b, c, d], uvs, want)
            run += ln
        for s, sign in ((secs[0], -1), (secs[-1], 1)):
            d = (pts[-1] - pts[-2]) if sign > 0 else (pts[0] - pts[1])
            self.face([s[0], s[1], s[2], s[3]], [sw(swatch)] * 4 if swatch else self.quad_strip_uv(strip, 0, w), d)

    def lathe(self, profile, strip="marble", seg=12, centre=(0, 0)):
        """profile: (radius, y) from the bottom up; smooth."""
        cx, cz = centre
        for i in range(len(profile) - 1):
            r0, y0 = profile[i]; r1, y1 = profile[i + 1]
            for k in range(seg):
                a0 = 2 * math.pi * k / seg; a1 = 2 * math.pi * (k + 1) / seg
                p = [V(cx + r0 * math.cos(a0), y0, cz + r0 * math.sin(a0)), V(cx + r0 * math.cos(a1), y0, cz + r0 * math.sin(a1)),
                     V(cx + r1 * math.cos(a1), y1, cz + r1 * math.sin(a1)), V(cx + r1 * math.cos(a0), y1, cz + r1 * math.sin(a0))]
                am = (a0 + a1) / 2
                want = V(math.cos(am), (r0 - r1) / max(abs(y1 - y0), 1e-4) * 0.5, math.sin(am))
                if abs(y1 - y0) < 1e-6: want = V(0, 1 if r1 < r0 else -1, 0)
                ymin = profile[0][1]; span = max(profile[-1][1] - ymin, 1e-4)
                u0, u1 = k / seg * 1.0, (k + 1) / seg * 1.0
                t0, t1 = (y0 - ymin) / span, (y1 - ymin) / span
                self.face(p, [uv(strip, u0, t0), uv(strip, u1, t0), uv(strip, u1, t1), uv(strip, u0, t1)], want, smooth=True)
        r, y = profile[-1]
        ring = [V(cx + r * math.cos(2 * math.pi * k / seg), y, cz + r * math.sin(2 * math.pi * k / seg)) for k in range(seg)]
        self.face(ring, [uv(strip, 0.1, 0.5)] * seg, (0, 1, 0))


# ─── the kawara plane ────────────────────────────────────────────────────────
COURSE = 0.24        # a course of tiles, down the slope
STEP = 0.035         # how far each course stands proud of the one below
EAVE_EDGE = 0.055    # the whole thickness of the roof at its edge
PLANES = 0


def roof_plane(mb, E0, E1, T0, T1, lift=0.18, sag=0.0, concave=0.45, soffit=True):
    """A tiled plane from the eave line E0-E1 up to the top line T0-T1 (which
    may be a point). Concave (flatter at the eave), eave corners turned up by
    `lift`, the top sagging by `sag` at mid span. Returns (rows, cols) grid of
    surface points for hips and ridges."""
    global PLANES
    PLANES += 1
    E0, E1, T0, T1 = (np.asarray(p, float) for p in (E0, E1, T0, T1))
    eave_len = np.linalg.norm(E1 - E0)
    eave_dir = (E1 - E0) / eave_len
    mid_e, mid_t = (E0 + E1) / 2, (T0 + T1) / 2
    slope_len = np.linalg.norm(mid_t - mid_e)
    n = max(2, int(round(slope_len / COURSE)))
    m = max(4, int(round(eave_len / 0.55)))
    rise = mid_t[1] - mid_e[1]
    up = V(0, 1, 0)
    out = mid_e - mid_t; out[1] = 0; out /= max(np.linalg.norm(out), 1e-9)

    def P(s, t):
        e = E0 + (E1 - E0) * s; tp = T0 + (T1 - T0) * s
        p = e + (tp - e) * t
        f = (1 - concave) * t + concave * t * t
        p[1] = e[1] + rise * f + lift * (1 - t) ** 2 * abs(2 * s - 1) ** 3 - sag * t * t * (1 - (2 * s - 1) ** 2)
        return p

    grid = [[P(i / m, j / n) for i in range(m + 1)] for j in range(n + 1)]
    for j in range(n):
        strip = f"tile{(j * 7 + int(eave_len * 10)) % 3}"
        off = (j % 2) * TILE_W * 0.5                      # courses break joint
        for i in range(m):
            a = grid[j][i] + up * STEP; b = grid[j][i + 1] + up * STEP
            c = grid[j + 1][i + 1]; d = grid[j + 1][i]
            ua = np.dot(a - E0, eave_dir) + off; ub = np.dot(b - E0, eave_dir) + off
            uc = np.dot(c - E0, eave_dir) + off; ud = np.dot(d - E0, eave_dir) + off
            mb.face([a, b, c, d], [uv(strip, ua, 0), uv(strip, ub, 0), uv(strip, uc, 1), uv(strip, ud, 1)], up + out * 0.3)
            # The riser under this course's lip (the fascia, for the first course).
            lo_a = grid[j][i] - (up * (EAVE_EDGE - STEP) if j == 0 else 0); lo_b = grid[j][i + 1] - (up * (EAVE_EDGE - STEP) if j == 0 else 0)
            mb.face([lo_a, lo_b, b, a], [uv("ridge", ua, 0), uv("ridge", ub, 0), uv("ridge", ub, 1), uv("ridge", ua, 1)], out)
    if soffit:
        k = max(1, n // 3)
        for j in range(0, n, k):
            j1 = min(n, j + k)
            for i in range(m):
                d0 = up * (EAVE_EDGE - STEP)
                mb.face([grid[j][i] - d0, grid[j][i + 1] - d0, grid[j1][i + 1] - d0, grid[j1][i] - d0], [sw("soffit")] * 4, -up)
    return grid


def hip_line(mb, grid, col, w=0.09, h=0.07):
    """The rounded ridge down a hip, following the plane's own curve."""
    pts = [row[col] + V(0, STEP, 0) for row in grid]
    mb.sweep(pts, w, h, "ridge")
    # The turned-up end tile at the eave corner.
    e = pts[0]; d = pts[0] - pts[1]; d[1] = 0; d /= max(np.linalg.norm(d), 1e-9)
    mb.sweep([e, e + d * 0.07 + V(0, 0.05, 0)], w * 1.1, h * 1.2, "ridge", taper_end=0.6)


def ridge_line(mb, a, b, sag=0.0, fins=True, w=0.12, h=0.15):
    a = np.asarray(a, float); b = np.asarray(b, float)
    n = max(2, int(np.linalg.norm(b - a) / 0.3))
    pts = []
    for i in range(n + 1):
        s = i / n
        p = a + (b - a) * s
        p[1] -= sag * (1 - (2 * s - 1) ** 2)
        pts.append(p)
    mb.sweep(pts, w, h, "ridge")
    if fins:
        d = (b - a) / max(np.linalg.norm(b - a), 1e-9)
        for end, sign in ((a, -1), (b, 1)):
            # The end ornament: a thin fin curling up and outward.
            base = end + V(0, h, 0)
            mb.sweep([base - d * sign * 0.12, base + d * sign * 0.04 + V(0, 0.10, 0), base + d * sign * 0.16 + V(0, 0.26, 0), base + d * sign * 0.14 + V(0, 0.36, 0)],
                     0.06, 0.07, "ridge", taper_end=0.35)


def hip_roof(mb, W, D, H, ridge=0.0, lift=0.2, sag=0.03, fins=True):
    """Eave rectangle W x D at y = 0 about the origin, ridge along X at y = H."""
    hx, hz, r = W / 2, D / 2, ridge / 2
    c = [V(-hx, 0, -hz), V(hx, 0, -hz), V(hx, 0, hz), V(-hx, 0, hz)]
    tl, tr = V(-r, H, 0), V(r, H, 0)
    g = roof_plane(mb, c[0], c[1], tl, tr, lift, sag)
    hip_line(mb, g, 0); hip_line(mb, g, -1)
    g = roof_plane(mb, c[2], c[3], tr, tl, lift, sag)
    hip_line(mb, g, 0); hip_line(mb, g, -1)
    roof_plane(mb, c[1], c[2], tr, tr, lift, 0)
    roof_plane(mb, c[3], c[0], tl, tl, lift, 0)
    if ridge > 0.05:
        ridge_line(mb, tl + V(0, 0.02, 0), tr + V(0, 0.02, 0), sag, fins)
    else:
        mb.lathe([(0.10, H - 0.02), (0.05, H + 0.10), (0.085, H + 0.18), (0.02, H + 0.36)], strip="ridge", seg=8)


def skirt_roof(mb, Wo, Do, Wi, Di, H, lift=0.2):
    """A roof skirt: eave rectangle Wo x Do at y = 0, meeting a wall rectangle Wi x Di at y = H."""
    o = [V(-Wo / 2, 0, -Do / 2), V(Wo / 2, 0, -Do / 2), V(Wo / 2, 0, Do / 2), V(-Wo / 2, 0, Do / 2)]
    i = [V(-Wi / 2, H, -Di / 2), V(Wi / 2, H, -Di / 2), V(Wi / 2, H, Di / 2), V(-Wi / 2, H, Di / 2)]
    for k in range(4):
        g = roof_plane(mb, o[k], o[(k + 1) % 4], i[k], i[(k + 1) % 4], lift, 0)
        hip_line(mb, g, 0)
        # A thin flashing where the tiles meet the wall.
        mb.sweep([i[k] + V(0, 0.02, 0), i[(k + 1) % 4] + V(0, 0.02, 0)], 0.10, 0.06, "ridge")


def gable_roof(mb, L, S, H, lift=0.10, sag=0.04, barge=True, fins=True):
    """Ridge along X, length L, eaves S apart at y = 0."""
    hx, hz = L / 2, S / 2
    gf = roof_plane(mb, V(-hx, 0, -hz), V(hx, 0, -hz), V(-hx, H, 0), V(hx, H, 0), lift, sag)
    gb = roof_plane(mb, V(hx, 0, hz), V(-hx, 0, hz), V(hx, H, 0), V(-hx, H, 0), lift, sag)
    ridge_line(mb, V(-hx, H + 0.02, 0), V(hx, H + 0.02, 0), sag, fins)
    if barge:
        for g, cols in ((gf, (0, -1)), (gb, (0, -1))):
            for col in cols:
                pts = [row[col] - V(0, 0.16, 0) for row in g]
                mb.sweep(pts, 0.04, 0.15, "timber")       # the thin bargeboard (hafu)


def lean_to(mb, L, depth, H, lift=0.06):
    """One slope: eave along X at z = -depth, y = 0, rising to the wall line z = 0 at y = H."""
    g = roof_plane(mb, V(-L / 2, 0, -depth), V(L / 2, 0, -depth), V(-L / 2, H, 0), V(L / 2, H, 0), lift, 0.02)
    for col in (0, -1):
        mb.sweep([row[col] - V(0, 0.14, 0) for row in g], 0.04, 0.13, "timber")
    mb.sweep([V(-L / 2, H + 0.02, 0), V(L / 2, H + 0.02, 0)], 0.10, 0.07, "ridge")


# ─── walls, stone, marble ────────────────────────────────────────────────────
def stone_face(mb, x0, x1, ys, z_of_y, jitter=0.018, want=(0, 0, -1), fixed_ends=True):
    """A battered stone face looking -Z between x0 and x1; ys are the course lines."""
    L = x1 - x0
    nx = max(2, int(round(L / 0.5)))
    y_lo, y_hi = 0.0, ys[-1]
    def tv(y): return min(1.0, max(0.0, (y + 0.15) / (y_hi + 0.15)))
    pts = []
    for j, y in enumerate(ys):
        row = []
        for i in range(nx + 1):
            dz = 0.0
            if 0 < j < len(ys) - 1 and (not fixed_ends or 0 < i < nx):
                dz = (RNG.random() - 0.5) * 2 * jitter
            dx = 0.0 if (i in (0, nx)) else (RNG.random() - 0.5) * 0.10
            row.append(V(x0 + L * i / nx + dx, y, z_of_y(y) + dz))
        pts.append(row)
    for j in range(len(ys) - 1):
        for i in range(nx):
            a, b, c, d = pts[j][i], pts[j][i + 1], pts[j + 1][i + 1], pts[j + 1][i]
            mb.face([a, b, c, d], [uv("stone", a[0], tv(a[1])), uv("stone", b[0], tv(b[1])), uv("stone", c[0], tv(c[1])), uv("stone", d[0], tv(d[1]))], want)


BASE_H, BASE_BATTER, BASE_BELOW, BASE_BACK = 1.2, 0.40, 1.3, 0.35
BASE_YS = [-BASE_BELOW, -0.55, 0.0, 0.27, 0.58, 0.86, BASE_H]


def base_z(y):
    """Outer face: vertical below ground, leaning in above it."""
    return -BASE_BATTER * (1 - max(0.0, y) / BASE_H)


def base_run(mb, L):
    """Top outer edge on z = 0; the wall stands behind it. Continues BASE_BELOW under ground."""
    stone_face(mb, -L / 2, L / 2, BASE_YS, base_z)
    mb.face([V(-L / 2, BASE_H, 0), V(L / 2, BASE_H, 0), V(L / 2, BASE_H, BASE_BACK), V(-L / 2, BASE_H, BASE_BACK)],
            [uv("stone", -L / 2, 0.80), uv("stone", L / 2, 0.80), uv("stone", L / 2, 0.92), uv("stone", -L / 2, 0.92)], (0, 1, 0))


def base_corner(mb):
    """Outer top corner at the origin; the block runs +x, +z; faces look -z and -x."""
    s = BASE_BACK
    for face in range(2):
        for j in range(len(BASE_YS) - 1):
            y0, y1 = BASE_YS[j], BASE_YS[j + 1]
            b0, b1 = base_z(y0), base_z(y1)
            def tv(y): return min(1.0, max(0.0, (y + 0.15) / (BASE_H + 0.15)))
            if face == 0:
                p = [V(b0, y0, b0), V(s, y0, b0), V(s, y1, b1), V(b1, y1, b1)]; want = (0, 0, -1)
                us = [0, s - b0, s - b1, 0]
            else:
                p = [V(b0, y0, s), V(b0, y0, b0), V(b1, y1, b1), V(b1, y1, s)]; want = (-1, 0, 0)
                us = [0, s - b0, s - b1, 0]
            mb.face(p, [uv("stone", 1.3 + us[k], tv(p[k][1])) for k in range(4)], want)
    mb.face([V(0, BASE_H, 0), V(s, BASE_H, 0), V(s, BASE_H, s), V(0, BASE_H, s)], [uv("stone", 0.2, 0.86)] * 4, (0, 1, 0))


def base_block(mb, W, D, H=1.0, batter=0.2):
    """A free-standing battered block on y = 0 (the keep's stone base): bottom W x D."""
    ys = [0.0, H * 0.27, H * 0.52, H * 0.76, H]
    def zf(half):
        return lambda y: -(half - batter * y / H)
    for k, (ln, half) in enumerate(((W, D / 2), (D, W / 2), (W, D / 2), (D, W / 2))):
        sub = MB()
        # each face is built looking -Z about its own centre, then turned
        L0 = ln
        nx = max(2, int(round(L0 / 0.5)))
        pts = []
        for j, y in enumerate(ys):
            inset = batter * y / H
            row = []
            for i in range(nx + 1):
                x = (-L0 / 2 + inset) + (L0 - 2 * inset) * i / nx
                dz = 0.0 if (j in (0, len(ys) - 1) or i in (0, nx)) else (RNG.random() - 0.5) * 0.03
                row.append(V(x, y, zf(half)(y) + dz))
            pts.append(row)
        ang = k * math.pi / 2
        ca, sa = math.cos(ang), math.sin(ang)
        def rot(p): return V(p[0] * ca + p[2] * sa, p[1], -p[0] * sa + p[2] * ca)
        for j in range(len(ys) - 1):
            for i in range(nx):
                q = [pts[j][i], pts[j][i + 1], pts[j + 1][i + 1], pts[j + 1][i]]
                uvs = [uv("stone", p[0] + k * 1.3, 0.25 + 0.75 * p[1] / H) for p in q]
                mb.face([rot(p) for p in q], uvs, rot(V(0, 0, -1)))
    mb.face([V(-W / 2 + batter, H, -D / 2 + batter), V(W / 2 - batter, H, -D / 2 + batter), V(W / 2 - batter, H, D / 2 - batter), V(-W / 2 + batter, H, D / 2 - batter)],
            [uv("stone", 0, 0.80), uv("stone", W, 0.80), uv("stone", W, 0.93), uv("stone", 0, 0.93)], (0, 1, 0))


def steps(mb, W=2.4, n=6, run=0.28, cheek=0.3):
    """Steps up the podium: the bottom riser on z = -BASE_BATTER, the landing edge at z = n * run - BASE_BATTER... cheeks either side."""
    rise = BASE_H / n
    z = -BASE_BATTER
    for i in range(n):
        y1 = rise * (i + 1)
        mb.face([V(-W / 2, -BASE_BELOW if i == 0 else rise * i, z), V(W / 2, -BASE_BELOW if i == 0 else rise * i, z), V(W / 2, y1, z), V(-W / 2, y1, z)],
                [uv("marble", 0, 0.05), uv("marble", W, 0.05), uv("marble", W, 0.35), uv("marble", 0, 0.35)], (0, 0, -1))
        mb.face([V(-W / 2, y1, z), V(W / 2, y1, z), V(W / 2, y1, z + run), V(-W / 2, y1, z + run)],
                [uv("marble", 0, 0.4), uv("marble", W, 0.4), uv("marble", W, 0.95), uv("marble", 0, 0.95)], (0, 1, 0))
        z += run
    depth = n * run
    for sx in (-1, 1):
        x0, x1 = (W / 2, W / 2 + cheek) if sx > 0 else (-W / 2 - cheek, -W / 2)
        z0, z1 = -BASE_BATTER, -BASE_BATTER + depth
        def tv(y): return min(1.0, max(0.0, (y + 0.15) / (BASE_H + 0.15)))
        for (ya, yb) in ((-BASE_BELOW, 0.0), (0.0, 0.6), (0.6, BASE_H)):
            mb.face([V(x0, ya, z0), V(x1, ya, z0), V(x1, yb, z0), V(x0, yb, z0)], [uv("stone", 2.0, tv(ya)), uv("stone", 2.0 + cheek, tv(ya)), uv("stone", 2.0 + cheek, tv(yb)), uv("stone", 2.0, tv(yb))], (0, 0, -1))
            xi = x0 if sx > 0 else x1
            mb.face([V(xi, ya, z0), V(xi, ya, z1), V(xi, yb, z1), V(xi, yb, z0)], [uv("stone", 0, tv(ya)), uv("stone", depth, tv(ya)), uv("stone", depth, tv(yb)), uv("stone", 0, tv(yb))], (-sx, 0, 0))
        mb.face([V(x0, BASE_H, z0), V(x1, BASE_H, z0), V(x1, BASE_H, z1), V(x0, BASE_H, z1)], [uv("stone", 0.2, 0.86)] * 4, (0, 1, 0))
        mb.box((x0 - 0.02, BASE_H, z0 - 0.02), (x1 + 0.02, BASE_H + 0.07, z0 + 0.34), strip="marble")     # a marble plinth for the column


def plaster_wall(mb, L, H, T=0.14, band=True, slit=False, window=False, rails=True):
    """Outside toward -Z, centred on z = 0. Timber rails, a recessed chest-high band (slot Band)."""
    h = T / 2
    rail_b, rail_t = (0.13, 0.10) if rails else (0.0, 0.0)
    b0, b1 = 1.12, 1.36                                  # the band, above the wall's own foot
    def tv(y): return 1.0 - y / H                         # grime at the foot
    def front(y0, y1, z, slot="Atlas", x0=-L / 2, x1=L / 2):
        uvs = [uv("plaster", x0, y0 / H), uv("plaster", x1, y0 / H), uv("plaster", x1, y1 / H), uv("plaster", x0, y1 / H)]
        mb.face([V(x0, y0, z), V(x1, y0, z), V(x1, y1, z), V(x0, y1, z)], uvs, (0, 0, -1), slot)
    cuts = [rail_b]
    if band and H > b1 + 0.3: cuts += [b0, b1]
    cuts.append(H - rail_t)
    hole = None
    if slit: hole = (-0.05, 0.05, min(H - 0.75, 1.5), min(H - 0.2, 2.05))
    if window: hole = (-0.24, 0.24, min(H - 0.85, 1.5), min(H - 0.3, 2.0))
    for i in range(len(cuts) - 1):
        y0, y1 = cuts[i], cuts[i + 1]
        is_band = band and H > b1 + 0.3 and abs(y0 - b0) < 1e-6
        if is_band:
            front(y0, y1, -h + 0.012, "Band")
            mb.face([V(-L / 2, y1, -h), V(L / 2, y1, -h), V(L / 2, y1, -h + 0.012), V(-L / 2, y1, -h + 0.012)], [sw("dark")] * 4, (0, -1, 0))
        elif hole and y0 < hole[2] < y1:
            hx0, hx1, hy0, hy1 = hole
            front(y0, hy0, -h); front(hy1, y1, -h)
            front(hy0, hy1, -h, x1=hx0); front(hy0, hy1, -h, x0=hx1)
            mb.box((hx0, hy0, -h), (hx1, hy1, -h + 0.07), swatch="dark", faces="Z")
            mb.face([V(hx0, hy0, -h + 0.07), V(hx1, hy0, -h + 0.07), V(hx1, hy1, -h + 0.07), V(hx0, hy1, -h + 0.07)], [sw("dark")] * 4, (0, 0, -1))
            for (xa, xb, ya, yb, wn) in ((hx0, hx0, hy0, hy1, (1, 0, 0)), (hx1, hx1, hy0, hy1, (-1, 0, 0))):
                mb.face([V(xa, ya, -h), V(xa, ya, -h + 0.07), V(xa, yb, -h + 0.07), V(xa, yb, -h)], [sw("soffit")] * 4, wn)
            mb.face([V(hx0, hy0, -h), V(hx1, hy0, -h), V(hx1, hy0, -h + 0.07), V(hx0, hy0, -h + 0.07)], [sw("soffit")] * 4, (0, 1, 0))
            mb.face([V(hx0, hy1, -h), V(hx1, hy1, -h), V(hx1, hy1, -h + 0.07), V(hx0, hy1, -h + 0.07)], [sw("soffit")] * 4, (0, -1, 0))
            if window:
                mb.box((hx0 - 0.04, hy0 - 0.05, -h - 0.015), (hx1 + 0.04, hy0, -h + 0.02))        # sill
                mb.box((hx0 - 0.04, hy1, -h - 0.015), (hx1 + 0.04, hy1 + 0.04, -h + 0.02))        # head
                for bx in (-0.08, 0.08):
                    mb.box((bx - 0.012, hy0, -h + 0.01), (bx + 0.012, hy1, -h + 0.03))            # lattice bars
        else:
            front(y0, y1, -h)
    # back, top, ends
    mb.face([V(L / 2, 0, h), V(-L / 2, 0, h), V(-L / 2, H, h), V(L / 2, H, h)], [uv("plaster", 0, 0), uv("plaster", L, 0), uv("plaster", L, 1), uv("plaster", 0, 1)], (0, 0, 1))
    mb.face([V(-L / 2, H, -h), V(L / 2, H, -h), V(L / 2, H, h), V(-L / 2, H, h)], [sw("soffit")] * 4, (0, 1, 0))
    for sx in (-1, 1):
        x = sx * L / 2
        mb.face([V(x, 0, -h), V(x, 0, h), V(x, H, h), V(x, H, -h)], [uv("plaster", 0, 0), uv("plaster", T, 0), uv("plaster", T, 1), uv("plaster", 0, 1)], (sx, 0, 0))
    if rails:
        mb.box((-L / 2, 0, -h - 0.012), (L / 2, rail_b, h + 0.012))
        mb.box((-L / 2, H - rail_t, -h - 0.012), (L / 2, H, h + 0.012))


def column(mb, H, r=0.13):
    """Base, shaft with entasis, capital."""
    prof = [(r * 1.55, 0.0), (r * 1.55, 0.05), (r * 1.3, 0.09), (r * 1.38, 0.13), (r * 1.08, 0.17)]
    for k in range(7):
        t = k / 6
        prof.append((r * (1.0 + 0.06 * math.sin(t * math.pi * 0.62) - 0.14 * t), 0.17 + (H - 0.17 - 0.22) * t))
    top = H - 0.22
    prof += [(r * 0.90, top + 0.02), (r * 1.02, top + 0.05), (r * 0.92, top + 0.08), (r * 1.30, top + 0.15)]
    mb.lathe(prof, seg=12)
    mb.box((-r * 1.5, top + 0.15, -r * 1.5), (r * 1.5, H, r * 1.5), strip="marble")        # abacus


def extrude(mb, profile, L, strip="marble"):
    """profile: (z, y) points going round an outline; extruded along X, centred."""
    n = len(profile)
    run = 0.0
    cz = sum(p[0] for p in profile) / n; cy = sum(p[1] for p in profile) / n
    for i in range(n):
        (z0, y0), (z1, y1) = profile[i], profile[(i + 1) % n]
        ln = math.hypot(z1 - z0, y1 - y0)
        want = V(0, (y0 + y1) / 2 - cy, (z0 + z1) / 2 - cz)
        # an outline edge faces away from the centre, across the edge
        e = V(0, y1 - y0, z1 - z0); nrm = V(0, e[2], -e[1])
        if np.dot(nrm, want) < 0: nrm = -nrm
        mb.face([V(-L / 2, y0, z0), V(L / 2, y0, z0), V(L / 2, y1, z1), V(-L / 2, y1, z1)],
                [uv(strip, 0, 0.1 + 0.8 * (run % 1.0)), uv(strip, L, 0.1 + 0.8 * (run % 1.0)), uv(strip, L, 0.1 + 0.8 * ((run + ln * 2) % 1.0)), uv(strip, 0, 0.1 + 0.8 * ((run + ln * 2) % 1.0))], nrm)
        run += ln * 2
    for sx in (-1, 1):
        mb.face([V(sx * L / 2, y, z) for (z, y) in profile], [uv(strip, 0.3 + z, 0.5) for (z, y) in profile], (sx, 0, 0))


ENTAB = [(-0.14, 0.0), (0.14, 0.0), (0.14, 0.30), (-0.20, 0.30), (-0.20, 0.25), (-0.15, 0.22), (-0.12, 0.17), (-0.14, 0.15)]


# ─── export ──────────────────────────────────────────────────────────────────
def export(name, mb, **meta):
    global PLANES
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    used = sorted({SLOTS.index(f[2]) for f in mb.faces})
    faces = sorted(mb.faces, key=lambda f: SLOTS.index(f[2]))
    for pts, uvs, slot, smooth in faces:
        vs = [bm.verts.new(to_blender(p)) for p in pts]
        try:
            f = bm.faces.new(vs)
        except ValueError:
            continue
        f.material_index = used.index(SLOTS.index(slot))
        f.smooth = smooth
        for loop, t in zip(f.loops, uvs):
            loop[uvl].uv = t
    if any(f[3] for f in faces):
        bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if any(f.smooth for f in v.link_faces) and all(f.smooth for f in v.link_faces)], dist=1e-5)
    bm.to_mesh(me); bm.free()
    for i in used:
        mat = bpy.data.materials.get(SLOTS[i]) or bpy.data.materials.new(SLOTS[i])
        me.materials.append(mat)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF', use_mesh_modifiers=False,
                             bake_anim=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    xs = [p for f in mb.faces for p in f[0]]
    lo = np.min(xs, axis=0); hi = np.max(xs, axis=0)
    info = {"slots": [SLOTS[i] for i in used], "tris": sum(len(f[0]) - 2 for f in mb.faces),
            "min": [round(float(v), 3) for v in lo], "max": [round(float(v), 3) for v in hi], "planes": PLANES}
    info.update(meta)
    MANIFEST[name] = info
    print(f"KIT {name}: {info['tris']} tris, slots {info['slots']}, min {info['min']} max {info['max']}, planes {PLANES}")
    PLANES = 0
    bpy.data.objects.remove(obj)


def piece(name, fn, **meta):
    global PLANES
    PLANES = 0
    mb = MB()
    fn(mb)
    export(name, mb, **meta)


def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    paint_atlas()

    # ── kawara ──
    for L in (2.5, 5.0):
        tag = f"{int(L * 10):02d}"
        piece(f"UK_Kawara_Gable_{tag}", lambda mb, L=L: gable_roof(mb, L, 3.4, 1.15))
        piece(f"UK_Kawara_Slope_{tag}", lambda mb, L=L: roof_plane(mb, V(-L / 2, 0, -1.7), V(L / 2, 0, -1.7), V(-L / 2, 1.15, 0), V(L / 2, 1.15, 0), 0.0, 0.02))
        piece(f"UK_Kawara_LeanTo_{tag}", lambda mb, L=L: lean_to(mb, L, 1.45, 0.55))
        piece(f"UK_Kawara_Coping_{tag}", lambda mb, L=L: gable_roof(mb, L, 0.62, 0.17, lift=0.0, sag=0.0, barge=False, fins=False))
    piece("UK_Kawara_Coping_Corner", lambda mb: hip_roof(mb, 0.62, 0.62, 0.2, 0.0, lift=0.03))
    piece("UK_Kawara_Hip_Corner", lambda mb: (roof_plane(mb, V(0, 0, -1.7), V(1.7, 0, -1.7), V(0, 1.15, 0), V(0, 1.15, 0), 0.2, 0),
                                              roof_plane(mb, V(1.7, 0, -1.7), V(1.7, 0, 0), V(0, 1.15, 0), V(0, 1.15, 0), 0.2, 0)))
    piece("UK_Kawara_Ridge_25", lambda mb: ridge_line(mb, V(-1.25, 0, 0), V(1.25, 0, 0), 0.03, True))
    piece("UK_Ridge_Ornament", lambda mb: mb.sweep([V(-0.12, 0, 0), V(0.04, 0.10, 0), V(0.16, 0.26, 0), V(0.14, 0.38, 0)], 0.07, 0.08, swatch="gold", taper_end=0.35))
    # sized for the Fortress: caps, the keep's tier skirts and top roofs, the gate
    piece("UK_Kawara_Cap_26", lambda mb: hip_roof(mb, 2.6, 2.6, 0.95, 0.0, lift=0.22))
    piece("UK_Kawara_Skirt_Turret", lambda mb: skirt_roof(mb, 2.95, 2.95, 1.72, 1.72, 0.42, lift=0.2))
    piece("UK_Kawara_Skirt_A", lambda mb: skirt_roof(mb, 5.5, 4.7, 3.82, 3.02, 0.55, lift=0.24))
    piece("UK_Kawara_Skirt_B", lambda mb: skirt_roof(mb, 4.6, 3.8, 3.02, 2.42, 0.50, lift=0.22))
    piece("UK_Kawara_Skirt_C", lambda mb: skirt_roof(mb, 3.8, 3.2, 2.22, 1.82, 0.46, lift=0.2))
    piece("UK_Kawara_Top_A", lambda mb: hip_roof(mb, 4.7, 3.9, 1.5, 2.0, lift=0.26, sag=0.05))
    piece("UK_Kawara_Top_B", lambda mb: hip_roof(mb, 3.9, 3.3, 1.3, 1.6, lift=0.24, sag=0.04))
    piece("UK_Kawara_Top_C", lambda mb: hip_roof(mb, 3.1, 2.7, 1.1, 1.2, lift=0.22, sag=0.03))
    piece("UK_Kawara_Gate", lambda mb: hip_roof(mb, 3.9, 2.5, 0.95, 2.0, lift=0.22, sag=0.04))

    # ── marble ──
    for H in (2.2, 2.6, 3.6):
        piece(f"UK_Marble_Column_{int(H * 10)}", lambda mb, H=H: column(mb, H, 0.13 if H < 3 else 0.16))
    piece("UK_Marble_Entablature_25", lambda mb: extrude(mb, ENTAB, 2.5))
    def pediment(mb):
        W, H, d = 3.2, 0.72, 0.14
        mb.face([V(-W / 2, 0, -d), V(W / 2, 0, -d), V(0, H, -d)], [uv("marble", 0, 0.1), uv("marble", W, 0.1), uv("marble", W / 2, 0.9)], (0, 0, -1))
        mb.face([V(W / 2, 0, d), V(-W / 2, 0, d), V(0, H, d)], [uv("marble", 0, 0.1), uv("marble", W, 0.1), uv("marble", W / 2, 0.9)], (0, 0, 1))
        for a, b in ((V(-W / 2 - 0.08, -0.02, 0), V(0, H + 0.02, 0)), (V(0, H + 0.02, 0), V(W / 2 + 0.08, -0.02, 0))):
            mb.sweep([a, b], 2 * d + 0.12, 0.07, "marble")                 # the thin raking cornice
        mb.box((-0.2, 0.18, -d - 0.02), (0.2, 0.42, -d), swatch="gold")    # a gilt device in the tympanum
    piece("UK_Marble_Pediment_32", pediment)
    def arch(mb):
        r, jamb = 0.8, 1.7
        for sx in (-1, 1):
            mb.box((sx * r - 0.11, 0, -0.10), (sx * r + 0.11, jamb, 0.10), strip="marble")
            mb.box((sx * r - 0.15, jamb - 0.08, -0.13), (sx * r + 0.15, jamb, 0.13), strip="marble")
        pts = [V(r * math.cos(a), jamb + r * math.sin(a), 0) for a in np.linspace(math.pi, 0, 13)]
        outward = [V(math.cos(a), math.sin(a), 0) for a in np.linspace(math.pi, 0, 13)]
        for i in range(12):
            a, b = pts[i], pts[i + 1]; oa, ob = outward[i], outward[i + 1]
            for z, wn in ((-0.10, (0, 0, -1)), (0.10, (0, 0, 1))):
                mb.face([a - oa * 0.10 + V(0, 0, z), b - ob * 0.10 + V(0, 0, z), b + ob * 0.12 + V(0, 0, z), a + oa * 0.12 + V(0, 0, z)],
                        mb.quad_strip_uv("marble", i * 0.2, i * 0.2 + 0.2, 0.2, 0.8), wn)
            mb.face([a + oa * 0.12 + V(0, 0, -0.10), b + ob * 0.12 + V(0, 0, -0.10), b + ob * 0.12 + V(0, 0, 0.10), a + oa * 0.12 + V(0, 0, 0.10)],
                    mb.quad_strip_uv("marble", i * 0.2, i * 0.2 + 0.2, 0.2, 0.8), (oa + ob))
            mb.face([a - oa * 0.10 + V(0, 0, -0.10), b - ob * 0.10 + V(0, 0, -0.10), b - ob * 0.10 + V(0, 0, 0.10), a - oa * 0.10 + V(0, 0, 0.10)],
                    mb.quad_strip_uv("marble", i * 0.2, i * 0.2 + 0.2, 0.2, 0.8), -(oa + ob))
    piece("UK_Marble_Arch_16", arch)
    def pilaster(mb):
        mb.box((-0.13, 0, -0.07), (0.13, 0.12, 0.0), strip="marble")
        mb.box((-0.10, 0.12, -0.05), (0.10, 2.42, 0.0), strip="marble")
        mb.box((-0.14, 2.42, -0.08), (0.14, 2.6, 0.0), strip="marble")
    piece("UK_Marble_Pilaster_26", pilaster)

    # ── the battered stone base ──
    piece("UK_Base_Run_25", lambda mb: base_run(mb, 2.5), below=BASE_BELOW)
    piece("UK_Base_Run_125", lambda mb: base_run(mb, 1.25), below=BASE_BELOW)
    piece("UK_Base_Corner", base_corner, below=BASE_BELOW)
    piece("UK_Base_Steps_24", steps, below=BASE_BELOW)
    piece("UK_Base_Keep_52x44", lambda mb: base_block(mb, 5.2, 4.4, 1.0, 0.2))
    piece("UK_Base_Turret_24", lambda mb: base_block(mb, 2.4, 2.4, 0.5, 0.1))
    def floor(mb):
        n, s = 6, 9.0
        for i in range(n):
            for j in range(n):
                x0, z0 = -s / 2 + s * i / n, -s / 2 + s * j / n
                x1, z1 = x0 + s / n, z0 + s / n
                u0 = (i * 1.37 + j * 0.61) % 4.0
                mb.face([V(x0, 0, z0), V(x1, 0, z0), V(x1, 0, z1), V(x0, 0, z1)], [uv("stone", u0, 0.42), uv("stone", u0 + 1.5, 0.42), uv("stone", u0 + 1.5, 0.98), uv("stone", u0, 0.98)], (0, 1, 0))
    piece("UK_Court_Floor_90", floor)
    def ground(mb):
        mb.box((-5, 0, -5), (5, 0.02, 5), swatch="earth")
    piece("UK_Ground_Patch_10", ground)

    # ── thin plaster walls ──
    WALL_H = 2.3
    piece("UK_Plaster_Wall_25", lambda mb: plaster_wall(mb, 2.5, WALL_H), band=True)
    piece("UK_Plaster_Wall_25_Slit", lambda mb: plaster_wall(mb, 2.5, WALL_H, slit=True), band=True)
    piece("UK_Plaster_Wall_25_Window", lambda mb: plaster_wall(mb, 2.5, WALL_H, window=True), band=True)
    piece("UK_Plaster_Wall_125", lambda mb: plaster_wall(mb, 1.25, WALL_H), band=True)
    piece("UK_Plaster_Wall_125_Slit", lambda mb: plaster_wall(mb, 1.25, WALL_H, slit=True), band=True)
    piece("UK_Keep_Wall_10", lambda mb: plaster_wall(mb, 1.0, 2.0, T=0.12, band=False))
    piece("UK_Keep_Wall_10_Window", lambda mb: plaster_wall(mb, 1.0, 2.0, T=0.12, band=False, window=True))
    piece("UK_Keep_Wall_10_Slit", lambda mb: plaster_wall(mb, 1.0, 2.0, T=0.12, band=False, slit=True))
    piece("UK_Timber_Post_23", lambda mb: mb.box((-0.08, 0, -0.09), (0.08, WALL_H, 0.09)))
    piece("UK_Timber_Post_20", lambda mb: mb.box((-0.07, 0, -0.08), (0.07, 2.0, 0.08)))
    piece("UK_Timber_Beam_25", lambda mb: mb.box((-1.25, 0, -0.06), (1.25, 0.14, 0.06)))
    def gate_frame(mb):
        mb.box((-1.10, 0, -0.10), (-0.92, 2.5, 0.10)); mb.box((0.92, 0, -0.10), (1.10, 2.5, 0.10))
        mb.box((-1.25, 2.28, -0.12), (1.25, 2.5, 0.12)); mb.box((-1.32, 2.5, -0.16), (1.32, 2.58, 0.16))
        for sx in (-1, 1): mb.box((sx * 1.18 - 0.07, 2.12, -0.2), (sx * 1.18 + 0.07, 2.28, 0.0))       # brackets
    piece("UK_Gate_Frame_22", gate_frame)
    def gate_leaves(mb):
        for x0, x1 in ((-0.92, -0.01), (0.01, 0.92)):
            mb.face([V(x0, 0.03, -0.03), V(x1, 0.03, -0.03), V(x1, 2.23, -0.03), V(x0, 2.23, -0.03)], mb.quad_strip_uv("door", x0 + 0.92, x1 + 0.92), (0, 0, -1))
            mb.face([V(x1, 0.03, 0.03), V(x0, 0.03, 0.03), V(x0, 2.23, 0.03), V(x1, 2.23, 0.03)], mb.quad_strip_uv("door", x0 + 0.92, x1 + 0.92), (0, 0, 1))
            mb.box((x0, 0.03, -0.03), (x1, 2.23, 0.03), swatch="iron", faces="xXY")
    piece("UK_Gate_Leaf_Pair_22", gate_leaves, leaf=2.2)
    def door_single(mb):
        mb.box((-0.62, 0, -0.08), (-0.50, 2.32, 0.08)); mb.box((0.50, 0, -0.08), (0.62, 2.32, 0.08)); mb.box((-0.70, 2.2, -0.10), (0.70, 2.34, 0.10))
        mb.face([V(-0.5, 0.02, -0.02), V(0.5, 0.02, -0.02), V(0.5, 2.2, -0.02), V(-0.5, 2.2, -0.02)], mb.quad_strip_uv("door", 0, 0.88), (0, 0, -1))
    piece("UK_Door_Single_22", door_single, leaf=2.18)

    # ── the small things that break an outline ──
    def bracket(mb):
        mb.box((-0.05, 0.0, -0.36), (0.05, 0.08, 0.0)); mb.box((-0.04, -0.22, -0.08), (0.04, 0.0, 0.0))
        mb.sweep([V(0, -0.20, -0.04), V(0, -0.02, -0.30)], 0.06, 0.05, "timber")
    piece("UK_Bracket", bracket)
    def stone_lantern(mb):
        mb.lathe([(0.20, 0.0), (0.20, 0.07), (0.09, 0.12), (0.075, 0.78), (0.17, 0.84), (0.17, 0.90)], strip="stone", seg=8)
        mb.box((-0.12, 0.90, -0.12), (0.12, 1.14, 0.12), swatch="paper", faces="xXzZ")
        for f in MB_LAST_FACES(mb, 4): pass
        mb.lathe([(0.27, 1.14), (0.25, 1.17), (0.06, 1.34), (0.04, 1.42), (0.07, 1.46), (0.0, 1.52)], strip="ridge", seg=8)
        for sx in (-1, 1):
            for sz in (-1, 1): mb.box((sx * 0.12 - 0.02, 0.90, sz * 0.12 - 0.02), (sx * 0.12 + 0.02, 1.14, sz * 0.12 + 0.02), swatch="iron")
    piece("UK_Lantern_Stone", stone_lantern)
    def hanging_lantern(mb):
        mb.box((-0.08, -0.30, -0.08), (0.08, -0.08, 0.08), swatch="paper", faces="xXzZy")
        for f in MB_LAST_FACES(mb, 5): pass
        mb.lathe([(0.13, -0.08), (0.11, -0.06), (0.02, 0.0)], strip="ridge", seg=8)
        mb.box((-0.09, -0.32, -0.09), (0.09, -0.30, 0.09), swatch="iron"); mb.box((-0.006, 0.0, -0.006), (0.006, 0.16, 0.006), swatch="iron")
    piece("UK_Lantern_Hanging", hanging_lantern)
    def banner_pole(mb):
        mb.box((-0.025, 0, -0.025), (0.025, 3.4, 0.025)); mb.box((-0.34, 3.18, -0.02), (0.34, 3.22, 0.02))
        mb.lathe([(0.04, 3.4), (0.055, 3.44), (0.0, 3.56)], strip="ridge", seg=6)
        mb.box((-0.06, 0, -0.06), (0.06, 0.10, 0.06), strip="stone")
    piece("UK_Banner_Pole", banner_pole)
    def standard(mb):
        mb.box((-0.03, 0, -0.03), (0.03, 2.9, 0.03))
        mb.box((-0.22, 2.55, -0.03), (0.22, 2.60, 0.03), swatch="gold")
        mb.sweep([V(-0.2, 2.9, 0), V(-0.34, 3.02, 0), V(-0.30, 3.2, 0)], 0.05, 0.05, swatch="gold", taper_end=0.4)   # the eagle's wings
        mb.sweep([V(0.2, 2.9, 0), V(0.34, 3.02, 0), V(0.30, 3.2, 0)], 0.05, 0.05, swatch="gold", taper_end=0.4)
        mb.lathe([(0.07, 2.9), (0.09, 3.0), (0.05, 3.12), (0.0, 3.2)], strip="ridge", seg=6)
        mb.lathe([(0.16, 0.0), (0.16, 0.12), (0.05, 0.2)], strip="marble", seg=8)
    piece("UK_Standard_Eagle", standard)

    with open(os.path.join(OUT, "uk_manifest.json"), "w") as f:
        json.dump({"eave_edge": EAVE_EDGE, "tile": [TILE_W, COURSE], "wall_thickness": 0.14, "pieces": MANIFEST}, f, indent=1)
    print(f"KIT DONE: {len(MANIFEST)} pieces -> {OUT}")


def MB_LAST_FACES(mb, n):
    """Re-slot the last n faces (lantern panes) to Glow."""
    for i in range(len(mb.faces) - n, len(mb.faces)):
        p, u, s, sm = mb.faces[i]
        mb.faces[i] = (p, u, "Glow", sm)
    return []


build_all()
