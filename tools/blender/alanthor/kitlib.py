# kitlib.py - the House kit as building blocks (docs/Design/Art_Direction.md § 13).
#
# Every Alanthor model is composed from House_1.fbx's own meshes, so every
# texel is the House's hand-painted art. Parts are LENGTHENED by
# slice-and-repeat (kit.extend), never stretched. An Assembly imports the
# House once, builds pieces as duplicates, then joins by material (nine meshes,
# digit-free names), centres the model, previews it and writes
# <out>/<name>.blend + <out>/<name>.fbx.
#
# House coordinates (Blender, Z up, the front faces -Y):
#   platform top z 0.63; body x ±1.85, y -0.43..2.36, walls to 2.94;
#   roof soffit 2.79, ridge 4.48; yard x ±2.8, y -2.27..2.9.
import os, math
import bpy, bmesh, mathutils
from twb_common import *
from kit import *

PIV = (0.0, 0.97, 0.63)        # House body footprint centre, floor level
ROOF_PIV = (0.0, 0.97, 2.79)   # roof soffit
BODY = ('Murs', 'Piliers', 'Chambranle_fenetres_et_lumiere', 'Porte_et_lumiere_porte', 'Lanterne', 'Toit')
TT = os.path.join(REPO, "Assets", "GameData", "TechTree")


def fill_platform_hole(obj):
    """The House platform has a hole under its own walls (x ±1.7,
    y -0.23..2.2). Wherever no hall stands on it the ground showed through, so
    every platform piece gets it closed: the boundary loop at the platform's
    top is filled and mapped like the rest of the floor (planar, the House's
    own floor mapping)."""
    bm = bmesh.new(); bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    edges = [e for e in bm.edges if e.is_boundary
             and all(abs(v.co.z - 0.63) < 0.03 and abs(v.co.x) < 2.0 and -0.5 < v.co.y < 2.5 for v in e.verts)]
    if edges:
        r = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        for f in r['faces']:
            if f.normal.z < 0: f.normal_flip()
            for l in f.loops:
                l[uv].uv = (0.1414 * l.vert.co.x + 0.5028, 0.1404 * l.vert.co.y + 0.3131)
    bm.to_mesh(obj.data); bm.free()


def keep_faces(obj, pred):
    """Delete every face whose centre/normal fails pred(centre, normal) —
    for cutting a piece out of a part (the House's plain back wall)."""
    bm = bmesh.new(); bm.from_mesh(obj.data)
    kill = [f for f in bm.faces if not pred(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(obj.data); bm.free()


class Assembly:
    def __init__(self):
        reset()
        bpy.context.preferences.filepaths.save_version = 0
        self.src = import_house()
        hook_textures()
        self.made = []
        s = self.src
        self.B, self.BU, self.CH, self.LA = s['Base'], s['Buissons'], s['Chambranle_fenetres_et_lumiere'], s['Lanterne']
        self.MU, self.MR, self.PI, self.PO, self.TO = s['Muret'], s['Murs'], s['Piliers'], s['Porte_et_lumiere_porte'], s['Toit']

    def D(self, o, n):
        d = dup(o, n); self.made.append(d); return d

    # ── ground ──────────────────────────────────────────────────────
    def terrace(self, xn=5, yn=5, wall=True, planters=True, x_mod=0.5, y_mod=0.9):
        """The House's walled platform, widened by xn modules a side and
        deepened by yn; returns the front-wall y. wall=False drops the low
        wall (a bare stone platform)."""
        DX, DY = x_mod * xn, -y_mod * yn
        parts = [self.B] + ([self.MU] if wall else [])
        for o in parts:
            t = self.D(o, 'Terrace_' + o.name)
            if o is self.B: fill_platform_hole(t)
            if not wall and o is self.B:
                keep_parts(t, lambda lo, hi: (hi.x - lo.x) > 5.0 or (abs(lo.x + 0.8) < 0.05 and abs(hi.x - 0.8) < 0.05))
            if xn: extend(t, 'x', 2.45, x_mod, xn, +1); extend(t, 'x', -2.45, x_mod, xn, -1)
            if yn: extend(t, 'y', -1.2, y_mod, yn, -1)
            if o is self.B: self._respread(t, DX, DY)
        if wall and planters:
            for sx in (+1, -1):
                big = (lambda lo, hi: lo.x > 0.9 and hi.x < 2.05) if sx > 0 else (lambda lo, hi: hi.x < -0.9 and lo.x > -2.05)
                small = (lambda lo, hi: lo.x > 1.8) if sx > 0 else (lambda lo, hi: hi.x < -1.8)
                n = max(1, int(DX / 0.85) + 1)
                for k in range(n):
                    b = self.D(self.BU, 'Bush'); keep_parts(b, big); xform(b, loc=(sx * min(k * 0.85, DX - 0.1), DY, 0))
                b = self.D(self.BU, 'Bush'); keep_parts(b, small); xform(b, loc=(sx * DX, DY, 0))
        self.DX, self.DY = DX, DY
        return -1.47 + DY

    def _respread(self, o, DX, DY):
        bm = bmesh.new(); bm.from_mesh(o.data); uv = bm.loops.layers.uv.active
        nx0, nx1 = -2.76 - DX, 2.76 + DX; ny0, ny1 = -1.43 + DY, 2.84
        for f in bm.faces:
            if f.normal.z > 0.95 and abs(f.calc_center_median().z - 0.63) < 0.03:
                for l in f.loops:
                    x = -2.76 + (l.vert.co.x - nx0) * (5.52 / max(1e-6, nx1 - nx0))
                    y = -1.43 + (l.vert.co.y - ny0) * (4.27 / max(1e-6, ny1 - ny0))
                    l[uv].uv = (0.1414 * x + 0.5028, 0.1404 * y + 0.3131)
        bm.to_mesh(o.data); bm.free()

    def dais(self, z=0.6, loc=(0, 0)):
        """The House platform and its stair, raised onto the terrace."""
        d = self.D(self.B, 'Dais')
        fill_platform_hole(d)
        keep_parts(d, lambda lo, hi: (hi.x - lo.x) > 5.0 or (abs(lo.x + 0.8) < 0.05 and abs(hi.x - 0.8) < 0.05))
        xform(d, loc=(loc[0], loc[1], z))
        return z

    # ── buildings ───────────────────────────────────────────────────
    def hall(self, bays=1, loc=(0, 0, 0), rot=0.0, scale=(1, 1, 1), parts=BODY, lanterns=True, door=True):
        """The House body, `bays` door bays long, centred on loc."""
        objs = []
        for o in (self.MR, self.PI, self.CH, self.PO, self.LA, self.TO):
            if o.name not in parts: continue
            if o is self.PO and not door: continue
            if o is self.LA and not lanterns: continue
            h = self.D(o, 'Hall_' + o.name)
            if bays > 1: extend(h, 'x', 0.9, 1.8, bays - 1, +1, copy_inside=True)
            objs.append(h)
        if lanterns and bays > 1 and 'Lanterne' in parts:
            for k in range(bays - 1):
                l = self.D(self.LA, 'Lantern'); xform(l, loc=(k * 1.8, 0, 0)); objs.append(l)
        shift = -(bays - 1) * 0.9
        for h in objs:
            xform(h, loc=(shift, 0, 0))
            xform(h, scale=scale, pivot=PIV, rotz=rot, loc=(loc[0], loc[1] + 0.97, loc[2] + 0.63))
        return objs

    def upper(self, s_xy=0.55, s_z=0.62, loc=(0, 0), base_z=3.6, door=True, rot=0.0):
        """A roofed upper storey set into a roof (z = the roof's mid height)."""
        for o in (self.MR, self.PI, self.CH) + ((self.PO,) if door else ()):
            xform(self.D(o, 'Upper_' + o.name), scale=(s_xy, s_xy, s_z), pivot=PIV, rotz=rot,
                  loc=(loc[0], loc[1] + 0.97, base_z))
        xform(self.D(self.TO, 'Upper_Toit'), scale=(s_xy, s_xy, s_xy), pivot=ROOF_PIV, rotz=rot,
              loc=(loc[0], loc[1] + 0.97, base_z + (2.79 - 0.63) * s_z))
        return base_z + (2.79 - 0.63) * s_z + (4.48 - 2.79) * s_xy

    def storey(self, parts, s_xy, s_z, base_z, cx, cy, rot, name):
        for o in parts:
            p = self.D(o, name + '_' + o.name)
            if o is self.TO:
                xform(p, scale=(s_xy, s_xy, s_xy), pivot=ROOF_PIV, rotz=rot,
                      loc=(cx, cy, base_z + (2.79 - 0.63) * s_z))
            else:
                xform(p, scale=(s_xy, s_xy, s_z), pivot=PIV, rotz=rot, loc=(cx, cy, base_z))

    def tower(self, cx, cy, rot=math.pi, storeys=2, s0=0.44, z=0.63, door=True):
        """A pagoda tower: each storey smaller, set into the roof below."""
        s, sz = s0, 0.7
        for k in range(storeys):
            parts = (self.MR, self.PI, self.CH) + ((self.PO,) if (door and k == 0) else ()) + (self.TO,)
            self.storey(parts, s, sz, z, cx, cy, rot, 'Tower%s' % chr(65 + k))
            z = z + (2.79 - 0.63) * sz + (3.6 - 2.79) * s
            s, sz = s * 0.74, max(0.4, sz * 0.75)
        return z

    def pavilion(self, bays=1, loc=(0, 0, 0), scale=(1, 1, 1), rot=0.0):
        """An open shelter: the House's pillars and roof, no walls — middle
        pillars added at every bay boundary."""
        objs = []
        for o in (self.PI, self.TO):
            h = self.D(o, 'Pav_' + o.name)
            if bays > 1: extend(h, 'x', 0.9, 1.8, bays - 1, +1)
            objs.append(h)
        for k in range(bays - 1):
            for front in (True, False):
                p = self.D(self.PI, 'PavPost')
                keep_parts(p, (lambda lo, hi: lo.x > 1.3 and hi.y < 0.1) if front else (lambda lo, hi: lo.x > 1.3 and lo.y > 1.9))
                # evenly between the corner pillars (x -1.77 .. 1.77 + 1.8(bays-1))
                span = 3.54 + 1.8 * (bays - 1)
                xform(p, loc=((-1.77 + (k + 1) * span / bays) - 1.77, 0, 0))
                objs.append(p)
        shift = -(bays - 1) * 0.9
        for h in objs:
            xform(h, loc=(shift, 0, 0))
            xform(h, scale=scale, pivot=PIV, rotz=rot, loc=(loc[0], loc[1] + 0.97, loc[2] + 0.63))
        return objs

    def posts(self, points, scale=(0.75, 0.75, 0.6), z=0.63):
        """Free-standing red-banded posts (training pells, hitching posts)."""
        src = self.D(self.PI, 'PostSrc'); keep_parts(src, lambda lo, hi: lo.x > 1.3 and hi.y < 0.1)
        lo, hi = bounds(src); pc = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)
        for (px, py) in points:
            xform(self.D(src, 'Post'), scale=scale, pivot=pc, loc=(px, py, z))
        self.made.remove(src); bpy.data.objects.remove(src)

    def lantern(self, x, y, z, s=0.8, post=None):
        """A lantern hung at (x, y, z). The House lantern is wall-hung art, so
        a free-standing one (below 2 m — no wall behind it) stands on a slim
        post of the House pillar."""
        xform(self.D(self.LA, 'Lantern'), scale=(s, s, s), pivot=(1.17, -0.5, 2.4), loc=(x, y, z))
        if post is None: post = z < 2.0
        if post:
            sz = max(0.15, (z + 0.15 - 0.63) / 2.52)
            self.posts([(x, y + 0.25 * s)], scale=(0.3, 0.3, sz))

    def bushes(self, points, s=1.0):
        big = lambda lo, hi: lo.x > 0.9 and hi.x < 2.05
        for (x, y) in points:
            b = self.D(self.BU, 'Bush'); keep_parts(b, big)
            lo, hi = bounds(b)
            xform(b, scale=(s, s, s), pivot=((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z), loc=(x, y, 0.63))

    def low_wall_ring(self, half, z=0.63, h=0.6, front_gap=True):
        """A low stone parapet ring of half-size `half` on a platform —
        the House's yard wall, scaled flat (emplacements)."""
        w = self.D(self.MU, 'Parapet')
        xform(w, scale=(half / 2.82, half / 2.6, h / 1.06), pivot=(0, 0.31, 0.02), loc=(0, 0, z))

    def plank_roofs(self):
        """Age 0 look: every roof's teal tiles become the atlas's wood."""
        for o in self.made:
            if o.data.materials and o.data.materials[0].name == 'Toit_texture':
                _plank(o)

    # ── output ──────────────────────────────────────────────────────
    def finish(self, name, out_dir, preview_dir):
        for o in self.src.values():
            if o.name in bpy.data.objects: bpy.data.objects.remove(o)
        for m in [m for m in bpy.data.materials if m.users]:
            group = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.materials and o.data.materials[0] == m]
            if not group: continue
            bpy.ops.object.select_all(action='DESELECT')
            for o in group: o.select_set(True)
            bpy.context.view_layer.objects.active = group[0]
            if len(group) > 1: bpy.ops.object.join()
            j = bpy.context.view_layer.objects.active
            nm = m.name.replace('_texture', ''); j.name = nm; j.data.name = nm
        meshes = [o for o in bpy.data.objects if o.type == 'MESH']
        lo = [min(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
        hi = [max(v.co[i] for o in meshes for v in o.data.vertices) for i in range(3)]
        cx = (lo[0] + hi[0]) / 2; cy = (lo[1] + hi[1]) / 2
        for o in meshes: o.data.transform(mathutils.Matrix.Translation((-cx, -cy, -lo[2])))
        size = [round(hi[i] - lo[i], 2) for i in range(3)]
        print("MODEL", name, "size", size)
        os.makedirs(preview_dir, exist_ok=True)
        big = max(size[0], size[1], size[2] * 0.8)
        render(os.path.join(preview_dir, name + ".png"), (-big * 1.05, -big * 1.6, big * 1.45), (0, 0, size[2] * 0.3),
               res=(700, 560))
        for n in ("Cam", "Sun"):
            if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
        os.makedirs(out_dir, exist_ok=True)
        bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), object_types={'MESH'},
            apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
            mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, name + ".blend"))
        try: bpy.ops.file.make_paths_relative(); bpy.ops.wm.save_mainfile()
        except Exception as e: print("relative paths:", e)
        print("WROTE", name, size)
        return size


def _plank(obj):
    img = None
    m = obj.data.materials[0]
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE': img = n.image
    if img is None: return
    W, H = img.size
    px = img.pixels[:]
    def teal(u, v):
        x = min(W - 1, max(0, int(u * W))); y = min(H - 1, max(0, int(v * H)))
        i = (y * W + x) * 4
        r, g, b = px[i], px[i + 1], px[i + 2]
        return b > r * 1.25 and g > r
    bm = bmesh.new(); bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    faces = [f for f in bm.faces if teal(sum(l[uv].uv.x for l in f.loops) / len(f.loops),
                                         sum(l[uv].uv.y for l in f.loops) / len(f.loops))]
    if faces:
        us = [l[uv].uv.x for f in faces for l in f.loops]; vs = [l[uv].uv.y for f in faces for l in f.loops]
        u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
        W0, W1, H0, H1 = 0.07, 0.48, 0.47, 0.535
        for f in faces:
            for l in f.loops:
                l[uv].uv.x = W0 + (l[uv].uv.x - u0) / max(1e-6, u1 - u0) * (W1 - W0)
                l[uv].uv.y = H0 + (l[uv].uv.y - v0) / max(1e-6, v1 - v0) * (H1 - H0)
    bm.to_mesh(obj.data); bm.free()
