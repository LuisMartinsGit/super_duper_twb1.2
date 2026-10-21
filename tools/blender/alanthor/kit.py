# Slice-and-repeat kitbash helpers on the House_1 meshes.
import bpy, bmesh, mathutils
EPS=1e-4
AX={'x':0,'y':1,'z':2}

def dup(obj, name):
    o=obj.copy(); o.data=obj.data.copy(); o.name=name
    bpy.context.scene.collection.objects.link(o); return o

def _parts(bm):
    """face-connected parts: list of face lists"""
    seen=set(); parts=[]
    for f in bm.faces:
        if f.index in seen: continue
        stack=[f]; seen.add(f.index); comp=[]
        while stack:
            a=stack.pop(); comp.append(a)
            for e in a.edges:
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        parts.append(comp)
    return parts

def _verts(faces):
    s=set()
    for f in faces:
        for v in f.verts: s.add(v)
    return list(s)

def extend(obj, axis, cut, width, n, direction=+1, copy_inside=False):
    """Lengthen obj along axis: everything beyond `cut` (in `direction`) moves by n*width,
    and the slab of width `width` just inside the cut is repeated n times to fill the gap.
    Parts that do not span the whole slab are moved rigidly (by their centre)."""
    if n<=0: return
    a=AX[axis]; d=direction; delta=d*n*width
    lo_slab, hi_slab = (cut-width, cut) if d>0 else (cut, cut+width)
    me=obj.data; bm=bmesh.new(); bm.from_mesh(me)
    no=mathutils.Vector((0,0,0)); no[a]=1.0
    for c in (lo_slab, hi_slab):
        co=mathutils.Vector((0,0,0)); co[a]=c
        # only bisect parts that span the slab — collect first
    bm.faces.ensure_lookup_table(); bm.faces.index_update()
    parts=_parts(bm)
    span=[]; movers=[]; copies=[]
    for p in parts:
        vs=_verts(p); lo=min(v.co[a] for v in vs); hi=max(v.co[a] for v in vs)
        if lo < lo_slab-EPS and hi > hi_slab+EPS: span.append(p)
        else:
            beyond = (lo >= cut-EPS) if d>0 else (hi <= cut+EPS)
            inside = (hi <= lo_slab+EPS) if d>0 else (lo >= hi_slab-EPS)
            if beyond: movers.append(vs)
            elif inside: pass   # wholly on the side that stays
            elif copy_inside and lo_slab-0.12 <= lo and hi <= hi_slab+0.12:
                copies.append(vs)
            else:
                c=(lo+hi)/2
                if (d>0 and c>(lo_slab+hi_slab)/2) or (d<0 and c<(lo_slab+hi_slab)/2): movers.append(vs)
    off=mathutils.Vector((0,0,0)); off[a]=delta
    for vs in movers:
        for v in vs: v.co+=off
    for vs in copies:
        fs=set()
        for v in vs:
            for f in v.link_faces: fs.add(f)
        for k in range(1,n+1):
            r=bmesh.ops.duplicate(bm, geom=list(fs))
            o=mathutils.Vector((0,0,0)); o[a]=d*k*width
            for g in r['geom']:
                if isinstance(g,bmesh.types.BMVert): g.co+=o
    if span:
        geom=set()
        for p in span:
            for f in p:
                geom.add(f)
                for e in f.edges: geom.add(e)
                for v in f.verts: geom.add(v)
        geom=list(geom)
        for c in (lo_slab, hi_slab):
            co=mathutils.Vector((0,0,0)); co[a]=c
            r=bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, dist=EPS)
            geom=list(set(geom)|set(r['geom']))
            geom=[g for g in geom if g.is_valid]
        spanfaces=[g for g in geom if isinstance(g,bmesh.types.BMFace)]
        def fc(f): return f.calc_center_median()[a]
        beyond=[f for f in spanfaces if (fc(f)>cut if d>0 else fc(f)<cut)]
        slab=[f for f in spanfaces if lo_slab < fc(f) < hi_slab]
        # detach the far side and move it
        r=bmesh.ops.split(bm, geom=beyond, use_only_faces=False)
        nv=[g for g in r['geom'] if isinstance(g,bmesh.types.BMVert)]
        for v in nv: v.co+=off
        # repeat the slab
        for k in range(1,n+1):
            r=bmesh.ops.duplicate(bm, geom=slab)
            o=mathutils.Vector((0,0,0)); o[a]=d*k*width
            for g in r['geom']:
                if isinstance(g,bmesh.types.BMVert): g.co+=o
    bm.to_mesh(me); bm.free(); me.update()

def keep_parts(obj, pred):
    """delete face-connected parts whose bounds fail pred(lo,hi) (Vectors)"""
    bm=bmesh.new(); bm.from_mesh(obj.data); bm.faces.ensure_lookup_table(); bm.faces.index_update()
    kill=[]
    for p in _parts(bm):
        vs=_verts(p)
        lo=mathutils.Vector([min(v.co[i] for v in vs) for i in range(3)])
        hi=mathutils.Vector([max(v.co[i] for v in vs) for i in range(3)])
        if not pred(lo,hi): kill+=p
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(obj.data); bm.free()

def xform(obj, scale=(1,1,1), loc=(0,0,0), rotz=0.0, pivot=(0,0,0)):
    """bake scale (about pivot) then rotation about Z then translation into the mesh"""
    import math
    M=(mathutils.Matrix.Translation(loc) @ mathutils.Matrix.Rotation(rotz,4,'Z')
       @ mathutils.Matrix.Diagonal((*scale,1)) @ mathutils.Matrix.Translation([-p for p in pivot]))
    obj.data.transform(M); obj.data.update()

def bounds(obj):
    vs=[v.co for v in obj.data.vertices]
    return (mathutils.Vector([min(v[i] for v in vs) for i in range(3)]),
            mathutils.Vector([max(v[i] for v in vs) for i in range(3)]))
