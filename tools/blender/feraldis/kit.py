"""
kit.py — the shared Feraldis building kit for Blender 5.1 (headless).

Every Feraldis building generator (one file per building beside this one)
builds its geometry with these helpers and exports through `export_building`.
Nothing here touches any other culture's art: all meshes are generated from
primitives, in metres, on a 2 m build grid (docs/Design/Build_Grid.md).

Conventions
  * Blender Z is up; the building's "front" (doors, banners) faces -Y.
  * A building is modelled at its footprint size (cells x 2 m). The Unity
    spawn path snug-fits the bounds into the footprint, so every model
    carries a ground PLINTH spanning the footprint — the bounds are then the
    footprint and the fit is 1.
  * Geometry goes into `MB` builders (one bmesh per exported object) with a
    material INDEX per primitive. Material NAMES are the contract with
    Unity: FeraldisArtAuthor.cs maps each name to a BuildingLit .mat.
    `Fer_PlayerColor_Cloth` is the player-colour slot (the marker's
    "*playercolor*" rule paints it the faction colour).
  * Flat shading everywhere; a few thousand triangles per level.
"""
import bpy, bmesh, math, os, sys, random
from mathutils import Vector, Matrix, Euler

# ─────────────────────────────────────────────────────────────────────────
# Palette — name: (base rgb, metallic, roughness, emission rgb, strength)
# ─────────────────────────────────────────────────────────────────────────
PALETTE = {
    # Feraldis — a WIDE value range on purpose: the first pass was all dark
    # greys and rendered flat under URP (2026-10-13). Tar is the darkest, the
    # plinth stone and plank rows are clearly lighter, iron is specular, bone
    # and glow are the highlights (docs/Design/Art_Direction.md: dusk grade,
    # highlights and emissives carry the read).
    "Fer_Timber":            ((0.13, 0.095, 0.075), 0.0, 0.90, None, 0),  # tarred dark timber (darkest)
    "Fer_Plank":             ((0.50, 0.37, 0.24), 0.0, 0.80, None, 0),    # weathered plank rows / trim
    "Fer_Iron":              ((0.17, 0.18, 0.21), 0.85, 0.35, None, 0),   # riveted black iron, specular
    "Fer_Rust":              ((0.58, 0.30, 0.14), 0.35, 0.70, None, 0),
    "Fer_Slate":             ((0.15, 0.16, 0.19), 0.0, 0.80, None, 0),    # dark slate roof
    "Fer_Turf":              ((0.19, 0.24, 0.13), 0.0, 0.95, None, 0),    # turf roof
    "Fer_Stone":             ((0.40, 0.39, 0.37), 0.0, 0.90, None, 0),    # basalt plinths (lighter)
    "Fer_Bone":              ((0.90, 0.84, 0.70), 0.0, 0.70, None, 0),
    "Fer_Blood":             ((0.42, 0.04, 0.05), 0.0, 0.30, None, 0),
    "Fer_Hide":              ((0.52, 0.40, 0.29), 0.0, 0.90, None, 0),
    "Fer_Rope":              ((0.60, 0.50, 0.34), 0.0, 0.95, None, 0),
    "Fer_Ember":             ((1.00, 0.45, 0.15), 0.0, 0.60, (1.0, 0.45, 0.15), 3.5),  # forge glow (EmissiveLadder.Ember)
    "Fer_Window":            ((1.00, 0.69, 0.35), 0.0, 0.60, (1.0, 0.69, 0.35), 2.5),  # warm windows (EmissiveLadder.Window)
    "Fer_Rune":              ((1.00, 0.20, 0.20), 0.0, 0.60, (1.0, 0.20, 0.20), 2.5),  # blood-magic glow
    "Fer_Veilstone":         ((0.37, 0.85, 0.91), 0.0, 0.40, (0.37, 0.85, 0.91), 3.0), # veilstone cyan
    "Fer_PlayerColor_Cloth": ((0.23, 0.48, 0.74), 0.0, 0.80, None, 0),    # marker blue -> faction colour
    # Age 0 — culture-NEUTRAL survivors' materials for the Shelter (the
    # capital before age-up). Natural wood, thatch, canvas, fieldstone: no
    # tar, iron, bone or carved ornament, nothing that reads as a culture.
    "Age0_Wood":             ((0.56, 0.42, 0.27), 0.0, 0.90, None, 0),
    "Age0_WoodDark":         ((0.36, 0.26, 0.17), 0.0, 0.90, None, 0),
    "Age0_Thatch":           ((0.84, 0.70, 0.38), 0.0, 0.95, None, 0),
    "Age0_Canvas":           ((0.72, 0.66, 0.54), 0.0, 0.90, None, 0),
    "Age0_Stone":            ((0.50, 0.48, 0.45), 0.0, 0.90, None, 0),
    "Age0_Earth":            ((0.38, 0.32, 0.25), 0.0, 0.95, None, 0),
    "Age0_Rope":             ((0.62, 0.52, 0.36), 0.0, 0.95, None, 0),
    "Age0_Fire":             ((1.00, 0.55, 0.20), 0.0, 0.60, (1.0, 0.55, 0.20), 2.5),
    "Age0_PlayerColor_Cloth": ((0.23, 0.48, 0.74), 0.0, 0.80, None, 0),
}
MAT_ORDER = list(PALETTE.keys())

_materials = {}

def material(name):
    if name in _materials and _materials[name].name == name:
        return _materials[name]
    base, metal, rough, em, strength = PALETTE[name]
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf is None:
        bsdf = m.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
        out = m.node_tree.nodes.get("Material Output") or m.node_tree.nodes.new("ShaderNodeOutputMaterial")
        m.node_tree.links.new(bsdf.outputs[0], out.inputs[0])
    bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    if em:
        bsdf.inputs["Emission Color"].default_value = (*em, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    else:
        bsdf.inputs["Emission Strength"].default_value = 0.0
    m.diffuse_color = (*base, 1.0)
    m.metallic = metal
    m.roughness = rough
    _materials[name] = m
    return m


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _materials.clear()
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0


# ─────────────────────────────────────────────────────────────────────────
# MB — a mesh builder: one bmesh, material slots by name
# ─────────────────────────────────────────────────────────────────────────
class MB:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []          # material names in slot order
        self.rng = random.Random(hash(name) & 0xffff)

    # ── materials ──
    def slot(self, mat_name):
        if mat_name not in self.slots:
            self.slots.append(mat_name)
        return self.slots.index(mat_name)

    # ── core placement ──
    def _place(self, verts, pos, rot, mat_name):
        """Rotate (euler xyz radians) then translate the verts; tag faces."""
        if rot is not None and any(rot):
            m = Euler(rot, 'XYZ').to_matrix()
            bmesh.ops.rotate(self.bm, cent=(0, 0, 0), matrix=m, verts=verts)
        bmesh.ops.translate(self.bm, vec=Vector(pos), verts=verts)
        idx = self.slot(mat_name)
        faces = set()
        for v in verts:
            for f in v.link_faces:
                faces.add(f)
        for f in faces:
            f.material_index = idx
            f.smooth = False
        return verts

    def box(self, size, pos, mat, rot=None, anchor_bottom=True):
        """Axis-aligned box of `size` (x,y,z) with its bottom-centre (or
        centre) at pos, rotated about that point."""
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = r["verts"]
        bmesh.ops.scale(self.bm, vec=Vector(size), verts=verts)
        if anchor_bottom:
            bmesh.ops.translate(self.bm, vec=(0, 0, size[2] * 0.5), verts=verts)
        return self._place(verts, pos, rot, mat)

    def cyl(self, r, h, pos, mat, segs=8, r2=None, rot=None, cap=True, anchor_bottom=True):
        """Cylinder / truncated cone (r at bottom, r2 at top), axis Z."""
        r2 = r if r2 is None else r2
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False,
                                    segments=segs, radius1=r, radius2=max(r2, 1e-4), depth=h)
        verts = res["verts"]
        if anchor_bottom:
            bmesh.ops.translate(self.bm, vec=(0, 0, h * 0.5), verts=verts)
        return self._place(verts, pos, rot, mat)

    def cone(self, r, h, pos, mat, segs=8, rot=None):
        return self.cyl(r, h, pos, mat, segs=segs, r2=0.0001, rot=rot)

    def sphere(self, r, pos, mat, sub=1, scale=(1, 1, 1)):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=r)
        verts = res["verts"]
        bmesh.ops.scale(self.bm, vec=Vector(scale), verts=verts)
        return self._place(verts, pos, None, mat)

    def prism(self, length, width, height, pos, mat, axis='x', rot=None, base_z=0.0):
        """Triangular (gable) prism: ridge along `axis`, base `width` wide,
        apex `height` above the base, bottom at pos.z."""
        hl, hw = length * 0.5, width * 0.5
        if axis == 'x':
            pts = [(-hl, -hw, 0), (hl, -hw, 0), (hl, hw, 0), (-hl, hw, 0), (-hl, 0, height), (hl, 0, height)]
        else:
            pts = [(-hw, -hl, 0), (hw, -hl, 0), (hw, hl, 0), (-hw, hl, 0), (0, -hl, height), (0, hl, height)]
        vs = [self.bm.verts.new(p) for p in pts]
        faces = [(0, 1, 2, 3), (0, 4, 5, 1), (3, 2, 5, 4), (0, 3, 4), (1, 5, 2)]
        for f in faces:
            try:
                self.bm.faces.new([vs[i] for i in f])
            except ValueError:
                pass
        # flip winding so normals face out (base face down)
        bmesh.ops.recalc_face_normals(self.bm, faces=list({f for v in vs for f in v.link_faces}))
        return self._place(vs, pos, rot, mat)

    def wedge(self, size, pos, mat, rot=None):
        """Right-angle wedge: full box footprint, sloping from z=size.z at -y to 0 at +y."""
        sx, sy, sz = size
        pts = [(-sx/2, -sy/2, 0), (sx/2, -sy/2, 0), (sx/2, sy/2, 0), (-sx/2, sy/2, 0),
               (-sx/2, -sy/2, sz), (sx/2, -sy/2, sz)]
        vs = [self.bm.verts.new(p) for p in pts]
        for f in [(0, 1, 2, 3), (0, 4, 5, 1), (3, 2, 5, 4), (0, 3, 4), (1, 5, 2)]:
            try:
                self.bm.faces.new([vs[i] for i in f])
            except ValueError:
                pass
        bmesh.ops.recalc_face_normals(self.bm, faces=list({f for v in vs for f in v.link_faces}))
        return self._place(vs, pos, rot, mat)

    def pyramid(self, base, height, pos, mat, top=0.0, rot=None):
        """Square frustum (base width -> top width) — roofs, plinths, stake tips."""
        hb, ht = base * 0.5, max(top * 0.5, 0.0005)
        pts = [(-hb, -hb, 0), (hb, -hb, 0), (hb, hb, 0), (-hb, hb, 0),
               (-ht, -ht, height), (ht, -ht, height), (ht, ht, height), (-ht, ht, height)]
        vs = [self.bm.verts.new(p) for p in pts]
        for f in [(3, 2, 1, 0), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)]:
            try:
                self.bm.faces.new([vs[i] for i in f])
            except ValueError:
                pass
        bmesh.ops.recalc_face_normals(self.bm, faces=list({f for v in vs for f in v.link_faces}))
        return self._place(vs, pos, rot, mat)

    def disc(self, r, pos, mat, segs=12, jitter=0.0, thick=0.04):
        """Flat irregular disc (blood pools, ash rings)."""
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segs,
                                    radius1=r, radius2=r, depth=thick)
        verts = res["verts"]
        if jitter > 0:
            for v in verts:
                a = math.atan2(v.co.y, v.co.x)
                k = 1.0 + jitter * math.sin(a * 3.0 + self.rng.random()) * 0.5 + jitter * (self.rng.random() - 0.5)
                v.co.x *= k; v.co.y *= k
        bmesh.ops.translate(self.bm, vec=(0, 0, thick * 0.5), verts=verts)
        return self._place(verts, pos, None, mat)

    def plane(self, size, pos, mat, rot=None, segs=(1, 1), wave=0.0):
        """A grid plane in XZ (vertical cloth) or XY; wave = sin ripple along x."""
        res = bmesh.ops.create_grid(self.bm, x_segments=segs[0], y_segments=segs[1], size=0.5)
        verts = res["verts"]
        bmesh.ops.scale(self.bm, vec=(size[0], size[1], 1), verts=verts)
        if wave:
            for v in verts:
                v.co.z += math.sin(v.co.x * 4.0 + v.co.y * 2.0) * wave
        return self._place(verts, pos, rot, mat)

    # ── compound pieces ──
    def band(self, size, pos, mat="Fer_Iron", t=0.06, rivets=True, rot=None):
        """Iron strap around a box of `size` at height pos.z (box centred at
        pos.x/y): four thin boxes plus rivet studs."""
        sx, sy, _ = size
        h = size[2]
        x, y, z = pos
        self.box((sx + 2 * t, t, h), (x, y - sy / 2 - t / 2, z), mat, rot)
        self.box((sx + 2 * t, t, h), (x, y + sy / 2 + t / 2, z), mat, rot)
        self.box((t, sy + 2 * t, h), (x - sx / 2 - t / 2, y, z), mat, rot)
        self.box((t, sy + 2 * t, h), (x + sx / 2 + t / 2, y, z), mat, rot)
        if rivets:
            n = max(2, int(sx / 0.7))
            for i in range(n):
                px = x - sx / 2 + (i + 0.5) * sx / n
                for sy_ in (-1, 1):
                    self.box((0.09, 0.05, 0.09), (px, y + sy_ * (sy / 2 + t), z + h / 2 - 0.045), mat, rot)

    def ring_band(self, r, h, pos, mat="Fer_Iron", segs=10, t=0.07):
        self.cyl(r + t, h, pos, mat, segs=segs)

    def stake(self, pos, h=1.6, r=0.11, mat="Fer_Timber", tilt=0.0, tip="Fer_Iron"):
        """Spiked palisade stake: a trunk with a pointed (iron) tip."""
        rot = (tilt, 0, 0) if tilt else None
        self.cyl(r, h * 0.8, pos, mat, segs=6, r2=r * 0.85, rot=rot)
        # tip
        if rot:
            m = Euler(rot, 'XYZ').to_matrix()
            top = Vector(pos) + m @ Vector((0, 0, h * 0.8))
            self.cyl(r * 0.85, h * 0.2, top, tip, segs=6, r2=0.001, rot=rot)
        else:
            self.cyl(r * 0.85, h * 0.2, (pos[0], pos[1], pos[2] + h * 0.8), tip, segs=6, r2=0.001)

    def stake_row(self, a, b, h=1.6, spacing=0.42, r=0.11, mat="Fer_Timber", jitter=0.08):
        a, b = Vector(a), Vector(b)
        d = (b - a); L = d.length
        n = max(1, int(L / spacing))
        for i in range(n + 1):
            p = a + d * (i / n)
            hh = h * (1 + jitter * (self.rng.random() - 0.5))
            self.stake((p.x, p.y, p.z), h=hh, r=r, mat=mat)
        # rail
        mid = (a + b) * 0.5
        ang = math.atan2(d.y, d.x)
        self.box((L, 0.08, 0.14), (mid.x, mid.y, mid.z + h * 0.45), "Fer_Plank", rot=(0, 0, ang))

    def post(self, pos, h, r=0.14, mat="Fer_Timber", segs=6):
        self.cyl(r, h, pos, mat, segs=segs, r2=r * 0.9)

    def beam(self, a, b, t=0.16, mat="Fer_Timber"):
        """Box beam from a to b (any direction)."""
        a, b = Vector(a), Vector(b)
        d = b - a; L = d.length
        if L < 1e-5:
            return
        mid = (a + b) * 0.5
        # rotate +X to d
        q = d.normalized().to_track_quat('X', 'Z')
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = r["verts"]
        bmesh.ops.scale(self.bm, vec=(L, t, t), verts=verts)
        bmesh.ops.rotate(self.bm, cent=(0, 0, 0), matrix=q.to_matrix(), verts=verts)
        return self._place(verts, mid, None, mat)

    def chain(self, a, b, link=0.18, mat="Fer_Iron"):
        """Low-poly chain: alternating flat links from a to b, sagging a little."""
        a, b = Vector(a), Vector(b)
        d = b - a; L = d.length
        n = max(2, int(L / link))
        q = d.normalized().to_track_quat('X', 'Z')
        for i in range(n):
            t = (i + 0.5) / n
            p = a + d * t
            p.z -= math.sin(t * math.pi) * L * 0.08
            r = bmesh.ops.create_cube(self.bm, size=1.0)
            verts = r["verts"]
            sz = (link * 1.1, 0.03, 0.1) if i % 2 == 0 else (link * 1.1, 0.1, 0.03)
            bmesh.ops.scale(self.bm, vec=sz, verts=verts)
            bmesh.ops.rotate(self.bm, cent=(0, 0, 0), matrix=q.to_matrix(), verts=verts)
            self._place(verts, p, None, mat)

    def skull(self, pos, s=0.3, mat="Fer_Bone", yaw=0.0):
        self.sphere(s * 0.5, (pos[0], pos[1], pos[2] + s * 0.5), mat, sub=1, scale=(1, 1.1, 1))
        # jaw
        self.box((s * 0.6, s * 0.5, s * 0.25), (pos[0], pos[1] - s * 0.15, pos[2]), mat, rot=(0, 0, yaw))
        # eye sockets
        m = Euler((0, 0, yaw), 'XYZ').to_matrix()
        for sx in (-1, 1):
            e = Vector(pos) + m @ Vector((sx * s * 0.18, -s * 0.46, s * 0.55))
            self.box((s * 0.16, s * 0.1, s * 0.16), e, "Fer_Timber", rot=(0, 0, yaw), anchor_bottom=False)

    def horn(self, pos, length=0.6, r=0.07, mat="Fer_Bone", rot=(0.9, 0, 0)):
        self.cyl(r, length, pos, mat, segs=5, r2=0.01, rot=rot)

    def antlers(self, pos, s=1.0, mat="Fer_Bone", yaw=0.0):
        """A pair of branching antlers mounted at pos (base), facing -Y."""
        for sx in (-1, 1):
            base = Vector(pos) + Vector((sx * 0.12 * s, 0, 0))
            tip = base + Vector((sx * 0.55 * s, -0.05 * s, 0.75 * s))
            self.beam(base, tip, t=0.07 * s, mat=mat)
            for k, (dx, dz) in enumerate(((0.25, 0.35), (0.12, 0.6))):
                b = base + (tip - base) * (0.4 + 0.3 * k)
                self.beam(b, b + Vector((sx * dx * s * 0.6, -0.1 * s, dz * s * 0.6)), t=0.05 * s, mat=mat)

    def dragon_head(self, pos, yaw=0.0, s=1.0, mat="Fer_Timber"):
        """Carved gable dragon head: neck rising from pos, head jutting
        forward (local -Y after yaw)."""
        m = Euler((0, 0, yaw), 'XYZ').to_matrix()
        def P(v):
            return Vector(pos) + m @ Vector(v)
        # neck: angled box
        self.box((0.28 * s, 0.32 * s, 1.1 * s), P((0, -0.1 * s, 0)), mat, rot=(0.55, 0, yaw))
        # head
        self.box((0.36 * s, 0.7 * s, 0.34 * s), P((0, -0.75 * s, 0.75 * s)), mat, rot=(0.0, 0, yaw))
        # snout
        self.box((0.26 * s, 0.4 * s, 0.22 * s), P((0, -1.25 * s, 0.78 * s)), mat, rot=(0.0, 0, yaw))
        # lower jaw
        self.box((0.24 * s, 0.42 * s, 0.1 * s), P((0, -1.2 * s, 0.66 * s)), mat, rot=(0.25, 0, yaw))
        # horns
        for sx in (-1, 1):
            self.cyl(0.05 * s, 0.42 * s, P((sx * 0.16 * s, -0.55 * s, 1.05 * s)), "Fer_Bone", segs=5, r2=0.005, rot=(-0.9, sx * 0.3, yaw))
        # eyes (rune glow)
        for sx in (-1, 1):
            self.box((0.07 * s, 0.07 * s, 0.07 * s), P((sx * 0.19 * s, -0.95 * s, 0.9 * s)), "Fer_Rune", rot=(0, 0, yaw), anchor_bottom=False)
        # iron jaw band
        self.box((0.4 * s, 0.1 * s, 0.38 * s), P((0, -1.02 * s, 0.73 * s)), "Fer_Iron", rot=(0, 0, yaw))

    def torch(self, pos, h=1.2, mat="Fer_Iron"):
        self.cyl(0.05, h, pos, mat, segs=5)
        self.cyl(0.14, 0.2, (pos[0], pos[1], pos[2] + h), mat, segs=6, r2=0.17)
        self.sphere(0.14, (pos[0], pos[1], pos[2] + h + 0.32), "Fer_Ember", sub=1, scale=(1, 1, 1.5))

    def brazier(self, pos, r=0.4, h=0.9):
        self.cyl(0.08, h * 0.6, pos, "Fer_Iron", segs=5)
        self.cyl(r * 0.5, h * 0.4, (pos[0], pos[1], pos[2] + h * 0.6), "Fer_Iron", segs=8, r2=r)
        self.sphere(r * 0.6, (pos[0], pos[1], pos[2] + h + 0.1), "Fer_Ember", sub=1, scale=(1, 1, 1.3))
        for i in range(3):
            a = i * 2.094
            self.cyl(0.04, h * 0.65, (pos[0] + math.cos(a) * r * 0.5, pos[1] + math.sin(a) * r * 0.5, pos[2]), "Fer_Iron", segs=4, rot=(-math.sin(a) * 0.35, math.cos(a) * 0.35, 0))

    def furnace_stack(self, pos, r=0.45, h=3.5, bands=3, mat="Fer_Iron", glow=True, segs=8, taper=0.85):
        """Riveted iron furnace chimney with a glowing crown and vent slots."""
        self.cyl(r, h, pos, mat, segs=segs, r2=r * taper)
        for i in range(bands):
            z = pos[2] + h * (0.15 + 0.7 * i / max(1, bands - 1))
            rr = r + (r * taper - r) * ((z - pos[2]) / h)
            self.cyl(rr + 0.06, 0.16, (pos[0], pos[1], z), "Fer_Rust" if i % 2 else mat, segs=segs)
        # crown
        rt = r * taper
        self.cyl(rt + 0.1, 0.25, (pos[0], pos[1], pos[2] + h), mat, segs=segs, r2=rt + 0.16)
        if glow:
            self.cyl(rt * 0.75, 0.12, (pos[0], pos[1], pos[2] + h + 0.2), "Fer_Ember", segs=segs)
            # vent slots near the top
            for k in range(4):
                a = k * math.pi / 2 + 0.4
                vx = pos[0] + math.cos(a) * (rt + 0.02)
                vy = pos[1] + math.sin(a) * (rt + 0.02)
                self.box((0.1, 0.22, 0.35), (vx, vy, pos[2] + h * 0.78), "Fer_Ember", rot=(0, 0, a))

    def furnace_mouth(self, pos, w=1.0, h=0.8, yaw=0.0):
        """A glowing hearth opening in a wall face at pos (face centre at
        ground, opening faces local -Y)."""
        m = Euler((0, 0, yaw), 'XYZ').to_matrix()
        self.box((w, 0.12, h), Vector(pos) + m @ Vector((0, -0.02, 0.1)), "Fer_Ember", rot=(0, 0, yaw))
        self.box((w + 0.3, 0.16, 0.14), Vector(pos) + m @ Vector((0, -0.04, h + 0.1)), "Fer_Iron", rot=(0, 0, yaw))
        for sx in (-1, 1):
            self.box((0.14, 0.16, h + 0.1), Vector(pos) + m @ Vector((sx * (w / 2 + 0.08), -0.04, 0.05)), "Fer_Iron", rot=(0, 0, yaw))

    def banner(self, pos, h=3.2, w=0.7, drop=1.4, yaw=0.0):
        """The player-colour slot: iron pole + cross-arm + hanging cloth
        (Fer_PlayerColor_Cloth) + a spear finial."""
        m = Euler((0, 0, yaw), 'XYZ').to_matrix()
        self.cyl(0.06, h, pos, "Fer_Iron", segs=6)
        self.cone(0.08, 0.35, (pos[0], pos[1], pos[2] + h), "Fer_Iron", segs=5)
        arm_a = Vector(pos) + m @ Vector((-w / 2 - 0.1, 0, h - 0.15))
        arm_b = Vector(pos) + m @ Vector((w / 2 + 0.1, 0, h - 0.15))
        self.beam(arm_a, arm_b, t=0.07, mat="Fer_Iron")
        # cloth hangs from the arm, slightly forward
        c = Vector(pos) + m @ Vector((0, -0.06, h - 0.2 - drop / 2))
        self.plane((w, drop), c, "Fer_PlayerColor_Cloth", rot=(math.pi / 2, 0, yaw), segs=(2, 4), wave=0.025)
        self.plane((w, drop), c + m @ Vector((0, 0.02, 0)), "Fer_PlayerColor_Cloth", rot=(-math.pi / 2, 0, yaw), segs=(2, 4), wave=0.025)
        # jagged hem: three small triangles… kept as a thin strip of iron rings
        self.box((w, 0.05, 0.08), Vector(pos) + m @ Vector((0, -0.05, h - 0.2 - drop)), "Fer_Rust", rot=(0, 0, yaw))

    def shield(self, pos, r=0.42, yaw=0.0, mat="Fer_Plank"):
        """Round shield hung on a wall (face normal local -Y)."""
        self.cyl(r, 0.08, pos, mat, segs=10, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)
        self.cyl(r * 0.3, 0.14, pos, "Fer_Iron", segs=6, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)
        self.cyl(r + 0.04, 0.05, pos, "Fer_Iron", segs=10, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)

    def axe(self, pos, rot=(0, 0, 0), s=1.0):
        """A throwing axe stuck in / hung on something."""
        self.cyl(0.03 * s, 0.7 * s, pos, "Fer_Plank", segs=4, rot=rot)
        m = Euler(rot, 'XYZ').to_matrix()
        head = Vector(pos) + m @ Vector((0, 0, 0.62 * s))
        self.box((0.3 * s, 0.05 * s, 0.22 * s), head, "Fer_Iron", rot=rot)

    def log_pile(self, pos, n=4, length=2.0, r=0.2, yaw=0.0, mat="Fer_Plank"):
        rows = [3, 2, 1] if n >= 6 else [2, 1] if n >= 3 else [1]
        z = pos[2]
        for row, cnt in enumerate(rows):
            for i in range(cnt):
                off = (i - (cnt - 1) / 2) * r * 2.05
                m = Euler((0, 0, yaw), 'XYZ').to_matrix()
                p = Vector(pos) + m @ Vector((0, off, 0))
                self.cyl(r, length, (p.x, p.y, z + row * r * 1.75 + r), mat, segs=6, rot=(0, math.pi / 2, yaw), anchor_bottom=False)

    def wheel(self, pos, r=0.55, yaw=0.0, mat="Fer_Plank"):
        self.cyl(r, 0.14, pos, mat, segs=10, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)
        self.cyl(r + 0.05, 0.08, pos, "Fer_Iron", segs=10, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)
        self.cyl(r * 0.22, 0.2, pos, "Fer_Iron", segs=6, rot=(math.pi / 2, 0, yaw), anchor_bottom=False)
        # spikes (Mad Max rims)
        for k in range(6):
            a = k * math.pi / 3
            m = Euler((0, 0, yaw), 'XYZ').to_matrix()
            d = m @ Vector((math.cos(a) * r, 0, math.sin(a) * r))
            self.cyl(0.05, 0.26, Vector(pos) + d * 1.0, "Fer_Iron", segs=4, r2=0.005,
                     rot=tuple(d.normalized().to_track_quat('Z', 'Y').to_euler()), anchor_bottom=False)

    def cage(self, pos, size=(0.9, 0.9, 1.3), bars=4):
        sx, sy, sz = size
        self.box((sx, sy, 0.08), pos, "Fer_Iron")
        self.box((sx, sy, 0.08), (pos[0], pos[1], pos[2] + sz - 0.08), "Fer_Iron")
        for i in range(bars):
            t = -0.5 + (i + 0.5) / bars
            for e in ((sx * t, -sy / 2), (sx * t, sy / 2), (-sx / 2, sy * t), (sx / 2, sy * t)):
                self.cyl(0.025, sz, (pos[0] + e[0], pos[1] + e[1], pos[2]), "Fer_Iron", segs=4)

    def plinth(self, w, d, h=0.3, mat="Fer_Stone", pos=(0, 0, 0), slabs=True):
        """Footprint-sized ground platform (sets the model's XY bounds)."""
        self.pyramid(w, h, pos, mat, top=w - 0.5) if abs(w - d) < 1e-6 else self.box((w, d, h), pos, mat)
        if slabs:
            n = max(2, int(w / 2))
            for i in range(n):
                for j in range(max(2, int(d / 2))):
                    if (i + j) % 2:
                        continue
                    self.box((w / n - 0.15, d / max(2, int(d / 2)) - 0.15, 0.04),
                             (pos[0] - w / 2 + (i + 0.5) * w / n, pos[1] - d / 2 + (j + 0.5) * d / max(2, int(d / 2)), pos[2] + h), "Fer_Timber" if mat == "Fer_Stone" else "Fer_Stone")

    def hall(self, pos, length, width, wall_h, roof_h, axis='x', roof_mat="Fer_Slate",
             wall_mat="Fer_Timber", dragons=True, overhang=0.35, planks=True, bands=0, ridge=True,
             door=True, door_mat="Fer_Ember", door_w=1.0, door_h=1.6):
        """A Feraldis longhouse body: tarred plank walls, steep plank roof,
        ridge beam, dragon-head gable ends, optional iron bands and a lit door."""
        x, y, z = pos
        L, W = (length, width) if axis == 'x' else (width, length)
        self.box((L, W, wall_h), pos, wall_mat)
        # plank lines
        if planks:
            nrows = max(2, int(wall_h / 0.45))
            for i in range(nrows):
                zz = z + (i + 0.5) * wall_h / nrows
                self.box((L + 0.04, W + 0.04, 0.06), (x, y, zz), "Fer_Plank")
        # roof
        ro = overhang
        self.prism(length + ro * 2, width + ro * 2, roof_h, (x, y, z + wall_h), roof_mat, axis=axis)
        # roof plank steps (three per slope)
        for k in range(3):
            t = (k + 0.5) / 3
            # a thin box along each slope
            half_w = (width / 2 + ro) * (1 - t)
            zz = z + wall_h + roof_h * t
            if axis == 'x':
                self.box((length + ro * 2 + 0.1, 0.1, 0.07), (x, y - half_w, zz + 0.02), "Fer_Plank")
                self.box((length + ro * 2 + 0.1, 0.1, 0.07), (x, y + half_w, zz + 0.02), "Fer_Plank")
            else:
                self.box((0.1, length + ro * 2 + 0.1, 0.07), (x - half_w, y, zz + 0.02), "Fer_Plank")
                self.box((0.1, length + ro * 2 + 0.1, 0.07), (x + half_w, y, zz + 0.02), "Fer_Plank")
        if ridge:
            if axis == 'x':
                self.box((length + ro * 2 + 0.3, 0.2, 0.2), (x, y, z + wall_h + roof_h - 0.08), "Fer_Timber")
            else:
                self.box((0.2, length + ro * 2 + 0.3, 0.2), (x, y, z + wall_h + roof_h - 0.08), "Fer_Timber")
        for i in range(bands):
            zz = z + wall_h * (0.25 + 0.5 * i / max(1, bands - 1))
            self.band((L, W, 0.14), (x, y, zz))
        if dragons:
            if axis == 'x':
                self.dragon_head((x - length / 2 - ro, y, z + wall_h + roof_h - 0.9), yaw=-math.pi / 2, s=0.9)
                self.dragon_head((x + length / 2 + ro, y, z + wall_h + roof_h - 0.9), yaw=math.pi / 2, s=0.9)
            else:
                self.dragon_head((x, y - length / 2 - ro, z + wall_h + roof_h - 0.9), yaw=0.0, s=0.9)
                self.dragon_head((x, y + length / 2 + ro, z + wall_h + roof_h - 0.9), yaw=math.pi, s=0.9)
        if door:
            if axis == 'x':
                self.box((door_w, 0.12, door_h), (x, y - W / 2 - 0.02, z + 0.05), door_mat)
                self.box((door_w + 0.3, 0.14, 0.14), (x, y - W / 2 - 0.05, z + door_h + 0.05), "Fer_Iron")
            else:
                self.box((door_w, 0.12, door_h), (x, y - W / 2 - 0.02, z + 0.05), door_mat)
                self.box((door_w + 0.3, 0.14, 0.14), (x, y - W / 2 - 0.05, z + door_h + 0.05), "Fer_Iron")

    def leanto(self, pos, w, d, h_front, h_back, mat="Fer_Hide", posts=True, open_front=True):
        """Open shed: four posts and a sloping hide/plank roof (high at -y)."""
        x, y, z = pos
        if posts:
            for sx in (-1, 1):
                self.post((x + sx * (w / 2 - 0.1), y - d / 2 + 0.1, z), h_front, r=0.1)
                self.post((x + sx * (w / 2 - 0.1), y + d / 2 - 0.1, z), h_back, r=0.1)
        self.wedge((w + 0.3, d + 0.3, h_front - h_back), (x, y, z + h_back), mat)
        self.box((w + 0.3, d + 0.3, 0.06), (x, y, z + h_back - 0.06), mat)
        if not open_front:
            self.box((w, 0.1, h_back), (x, y + d / 2, z), "Fer_Timber")

    def watch_platform(self, pos, h=4.0, w=1.6, roof=True):
        """Timber watch post: four braced posts, a platform, railing, brazier."""
        x, y, z = pos
        for sx in (-1, 1):
            for sy in (-1, 1):
                self.post((x + sx * w / 2, y + sy * w / 2, z), h, r=0.12)
        self.box((w + 0.5, w + 0.5, 0.18), (x, y, z + h), "Fer_Plank")
        # railing
        for sx in (-1, 1):
            self.box((w + 0.5, 0.06, 0.5), (x, y + sx * (w / 2 + 0.22), z + h + 0.18), "Fer_Timber")
            self.box((0.06, w + 0.5, 0.5), (x + sx * (w / 2 + 0.22), y, z + h + 0.18), "Fer_Timber")
        # braces
        self.beam((x - w / 2, y - w / 2, z + h * 0.3), (x + w / 2, y - w / 2, z + h * 0.7), t=0.09)
        self.beam((x + w / 2, y + w / 2, z + h * 0.3), (x - w / 2, y + w / 2, z + h * 0.7), t=0.09)
        if roof:
            for sx in (-1, 1):
                for sy in (-1, 1):
                    self.post((x + sx * w / 2, y + sy * w / 2, z + h + 0.18), 1.6, r=0.07)
            self.pyramid(w + 0.9, 0.9, (x, y, z + h + 1.78), "Fer_Slate")
        self.brazier((x, y, z + h + 0.18), r=0.3, h=0.6)

    def rune_post(self, pos, h=2.4, w=0.5, mat="Fer_Stone"):
        """Basalt obelisk with glowing rune strips on its faces."""
        self.pyramid(w, h, pos, mat, top=w * 0.6)
        self.pyramid(w * 0.6, 0.3, (pos[0], pos[1], pos[2] + h), "Fer_Iron", top=0.05)
        for k in range(4):
            a = k * math.pi / 2
            m = Euler((0, 0, a), 'XYZ').to_matrix()
            for j in range(3):
                z = pos[2] + h * (0.25 + 0.2 * j)
                ww = w * (1 - (z - pos[2]) / h * 0.4)
                self.box((0.08, 0.04, 0.22), Vector(pos) + m @ Vector((0, -ww / 2 - 0.02, z)), "Fer_Rune", rot=(0, 0, a))

    def crest(self, a, b, n=6, h=0.35, mat="Fer_Iron"):
        """Row of iron spikes along a ridge from a to b."""
        a, b = Vector(a), Vector(b)
        for i in range(n):
            p = a + (b - a) * ((i + 0.5) / n)
            self.cone(0.07, h, p, mat, segs=4)

    def target(self, pos, h=1.5, r=0.4):
        """Axe-throwing target: a post with a round plank disc and an axe in it."""
        self.post(pos, h, r=0.1)
        c = (pos[0], pos[1] - 0.08, pos[2] + h - r - 0.1)
        self.cyl(r, 0.12, c, "Fer_Plank", segs=10, rot=(math.pi / 2, 0, 0), anchor_bottom=False)
        self.cyl(r * 0.35, 0.16, c, "Fer_Blood", segs=8, rot=(math.pi / 2, 0, 0), anchor_bottom=False)
        self.cyl(r + 0.04, 0.06, c, "Fer_Iron", segs=10, rot=(math.pi / 2, 0, 0), anchor_bottom=False)
        self.axe((c[0] + 0.12, c[1] - 0.1, c[2] - 0.1), rot=(1.3, 0.3, 0), s=0.8)

    def crate(self, pos, s=0.7, mat="Fer_Plank", yaw=0.0):
        self.box((s, s, s), pos, mat, rot=(0, 0, yaw))
        self.band((s, s, 0.07), (pos[0], pos[1], pos[2] + s * 0.5 - 0.035), rivets=False, t=0.03, rot=(0, 0, yaw))

    def hide_rack(self, pos, w=1.8, h=1.7, yaw=0.0):
        """Drying frame with a stretched hide."""
        m = Euler((0, 0, yaw), 'XYZ').to_matrix()
        for sx in (-1, 1):
            self.post(Vector(pos) + m @ Vector((sx * w / 2, 0, 0)), h, r=0.07)
        self.beam(Vector(pos) + m @ Vector((-w / 2, 0, h)), Vector(pos) + m @ Vector((w / 2, 0, h)), t=0.08)
        self.plane((w * 0.8, h * 0.7), Vector(pos) + m @ Vector((0, 0, h * 0.55)), "Fer_Hide", rot=(math.pi / 2, 0, yaw), segs=(3, 3), wave=0.03)
        self.plane((w * 0.8, h * 0.7), Vector(pos) + m @ Vector((0, 0.02, h * 0.55)), "Fer_Hide", rot=(-math.pi / 2, 0, yaw), segs=(3, 3), wave=0.03)

    def gantry(self, pos, w, d, h, mat="Fer_Timber"):
        """A-frame timber gantry: two A legs joined by a top beam along x."""
        x, y, z = pos
        for sx in (-1, 1):
            self.beam((x + sx * w / 2, y - d / 2, z), (x + sx * w / 2, y, z + h), t=0.16, mat=mat)
            self.beam((x + sx * w / 2, y + d / 2, z), (x + sx * w / 2, y, z + h), t=0.16, mat=mat)
            self.beam((x + sx * w / 2, y - d / 4, z + h * 0.5), (x + sx * w / 2, y + d / 4, z + h * 0.5), t=0.1, mat=mat)
        self.box((w + 0.4, 0.2, 0.2), (x, y, z + h - 0.05), mat)

    # ── finish ──
    def build(self, collection=None):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for n in self.slots:
            me.materials.append(material(n))
        for p in me.polygons:
            p.use_smooth = False
        ob = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        return ob


# ─────────────────────────────────────────────────────────────────────────
# Construction frame (Lv0 for culture-only buildings) — numbered parts
# ─────────────────────────────────────────────────────────────────────────
def construction_frame(footprint, hall_len, hall_w, wall_h, roof_h, axis='x', collection=None):
    """Lv0: a stone plinth, the timber post skeleton and the rafters, as
    three numbered objects so BuildingRiseData raises them in order."""
    objs = []
    p = MB("1_Plinth")
    p.plinth(footprint, footprint, 0.3)
    objs.append(p.build(collection))

    f = MB("2_Frame")
    L, W = (hall_len, hall_w) if axis == 'x' else (hall_w, hall_len)
    nx, ny = max(2, int(L / 2)), max(2, int(W / 2))
    for i in range(nx + 1):
        for j in range(ny + 1):
            if 0 < i < nx and 0 < j < ny:
                continue
            f.post((-L / 2 + i * L / nx, -W / 2 + j * W / ny, 0.3), wall_h, r=0.13)
    # top plates
    f.box((L + 0.2, 0.18, 0.18), (0, -W / 2, 0.3 + wall_h), "Fer_Timber")
    f.box((L + 0.2, 0.18, 0.18), (0, W / 2, 0.3 + wall_h), "Fer_Timber")
    f.box((0.18, W + 0.2, 0.18), (-L / 2, 0, 0.3 + wall_h), "Fer_Timber")
    f.box((0.18, W + 0.2, 0.18), (L / 2, 0, 0.3 + wall_h), "Fer_Timber")
    # scaffold & a crate
    f.box((1.0, 0.7, 0.6), (L / 2 + 0.9, -W / 2 + 0.5, 0.3), "Fer_Plank")
    f.log_pile((-L / 2 - 1.0, W / 2 - 0.3, 0.3), n=3, length=1.6, r=0.16, yaw=math.pi / 2)
    objs.append(f.build(collection))

    r = MB("3_Rafters")
    zt = 0.3 + wall_h
    if axis == 'x':
        r.box((L + 0.3, 0.18, 0.18), (0, 0, zt + roof_h - 0.1), "Fer_Timber")
        n = max(3, int(L / 1.2))
        for i in range(n + 1):
            x = -L / 2 + i * L / n
            r.beam((x, -W / 2 - 0.2, zt), (x, 0, zt + roof_h), t=0.12)
            r.beam((x, W / 2 + 0.2, zt), (x, 0, zt + roof_h), t=0.12)
    else:
        r.box((0.18, L + 0.3, 0.18), (0, 0, zt + roof_h - 0.1), "Fer_Timber")
        n = max(3, int(L / 1.2))
        for i in range(n + 1):
            y = -L / 2 + i * L / n
            r.beam((-W / 2 - 0.2, y, zt), (0, y, zt + roof_h), t=0.12)
            r.beam((W / 2 + 0.2, y, zt), (0, y, zt + roof_h), t=0.12)
    objs.append(r.build(collection))
    return objs


# ─────────────────────────────────────────────────────────────────────────
# Export + preview
# ─────────────────────────────────────────────────────────────────────────
def _bounds(objs):
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type != 'MESH':
            continue
        for v in o.bound_box:
            w = o.matrix_world @ Vector(v)
            mn.x = min(mn.x, w.x); mn.y = min(mn.y, w.y); mn.z = min(mn.z, w.z)
            mx.x = max(mx.x, w.x); mx.y = max(mx.y, w.y); mx.z = max(mx.z, w.z)
    return mn, mx


def tri_count(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')


def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, object_types={'MESH', 'EMPTY'}, use_mesh_modifiers=True,
        mesh_smooth_type='OFF', use_tspace=False, add_leaf_bones=False, bake_anim=False,
        path_mode='STRIP', embed_textures=False)


def render_preview(objs, path, size=(960, 760)):
    """Orthographic 3/4 view, EEVEE (emission shows) with a dusk world."""
    sc = bpy.context.scene
    mn, mx = _bounds(objs)
    c = (mn + mx) * 0.5
    dims = mx - mn
    diag = max(dims.x, dims.y, dims.z)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = diag * 1.35
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    sc.collection.objects.link(cam)
    d = Vector((1.0, -1.0, 0.85)).normalized()
    cam.location = c + d * (diag * 4)
    cam.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    sc.camera = cam
    # lights
    sun_d = bpy.data.lights.new("Sun", 'SUN'); sun_d.energy = 3.5; sun_d.angle = 0.3
    sun = bpy.data.objects.new("Sun", sun_d); sc.collection.objects.link(sun)
    sun.rotation_euler = Vector((0.6, -0.8, 1.2)).normalized().to_track_quat('Z', 'Y').to_euler()
    fill_d = bpy.data.lights.new("Fill", 'SUN'); fill_d.energy = 1.0
    fill = bpy.data.objects.new("Fill", fill_d); sc.collection.objects.link(fill)
    fill.rotation_euler = Vector((-0.7, 0.4, 0.9)).normalized().to_track_quat('Z', 'Y').to_euler()
    world = bpy.data.worlds.new("Dusk"); sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.09, 0.11, 0.17, 1.0)
        bg.inputs[1].default_value = 0.9
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    sc.render.image_settings.file_format = 'PNG'
    ok = False
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH'):
        try:
            sc.render.engine = eng
            if eng.startswith('BLENDER_EEVEE'):
                try:
                    sc.eevee.taa_render_samples = 16
                    sc.eevee.use_shadows = True
                except Exception:
                    pass
            else:
                sc.display.shading.light = 'STUDIO'
                sc.display.shading.color_type = 'MATERIAL'
                sc.display.shading.show_shadows = True
                sc.display.shading.show_cavity = True
            bpy.ops.render.render(write_still=True)
            ok = True
            break
        except Exception as e:
            print("render engine", eng, "failed:", e)
    # cleanup
    for o in (cam, sun, fill):
        bpy.data.objects.remove(o)
    return ok


def export_building(name, objs, out_dir, preview_dir, level_tag):
    """Export `objs` as <out_dir>/<name>_<level_tag>.fbx and render a preview."""
    fbx = os.path.join(out_dir, f"{name}_{level_tag}.fbx")
    export_fbx(objs, fbx)
    mn, mx = _bounds(objs)
    d = mx - mn
    print(f"EXPORT {name}_{level_tag}: {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m, {tri_count(objs)} tris -> {fbx}")
    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        png = os.path.join(preview_dir, f"{name}_{level_tag}.png")
        render_preview(objs, png)
        print(f"PREVIEW {png}")
    return fbx
