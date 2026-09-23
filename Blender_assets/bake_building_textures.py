# bake_building_textures.py
# Bake a building's procedural materials to images, headlessly.
#
#   blender <file.blend> --background --python bake_building_textures.py -- \
#       --out "D:/Users/overw/Documents/The Waning Border 1.2/Assets/GameData/TechTree/Age0/Buildings/Wall" \
#       [--objects Foundation OwnershipCloth WoodenBeams] [--size 2048] [--normals]
#
# WHY A SCRIPT. An all-black bake is almost always one of two settings, and
# both are easy to leave wrong in the UI:
#
#   * Bake Type "Combined", or "Diffuse" with Direct/Indirect still ticked, in
#     a scene with NO LIGHTS. There is nothing to light the surface, so every
#     texel bakes black. Deleting the default lamp before exporting — the right
#     thing to do for the FBX — is exactly what causes this.
#   * A material whose colour arrives through an EMISSION shader rather than a
#     Principled BSDF. A Diffuse bake reads only the diffuse component and
#     returns black for it.
#
# This script sidesteps both. It bakes DIFFUSE with colour only (no lighting
# involved at all) for Principled materials, and falls back to an EMIT bake —
# temporarily rerouting the colour into an Emission shader — for anything else.
# The node tree is restored afterwards, so the .blend is left as it was.
#
# It also does the step people most often miss: it SAVES the images to disk.
# A baked image lives in memory and has no filepath until saved.
#
# A SOLID-COLOUR BAKE (the grain vanished) is a different fault: a base-colour
# bake captures base colour and nothing else. In most procedural wood/stone
# setups the noise drives BUMP and ROUGHNESS, not Base Color — the grain is
# shading relief, so an albedo map is correctly, uselessly flat. Bake the
# other channels too: --normals captures bump, --roughness captures the
# roughness variation. If the noise really is meant to be IN the colour, mix
# it into Base Color in Blender first.
#
# Pipeline context: docs/Art_Pipeline_FBX.md.

import argparse
import os
import sys

import bpy


# ── arguments after the "--" separator ────────────────────────────────────
def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True, help="folder the PNGs are written to")
    p.add_argument("--objects", nargs="*", default=None,
                   help="object names to bake; default = every visible mesh")
    p.add_argument("--size", type=int, default=2048)
    p.add_argument("--margin", type=int, default=16)
    p.add_argument("--samples", type=int, default=16)
    p.add_argument("--normals", action="store_true",
                   help="also bake tangent-space normals (captures Bump nodes — "
                        "this is where procedural grain usually lives)")
    p.add_argument("--roughness", action="store_true",
                   help="also bake roughness (the other half of procedural detail)")
    p.add_argument("--unwrap", action="store_true",
                   help="Smart UV Project anything with no UV layer")
    return p.parse_args(argv)


def principled_of(mat):
    if not mat or not mat.use_nodes:
        return None
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            return n
    return None


def output_of(mat):
    for n in mat.node_tree.nodes:
        if n.type == "OUTPUT_MATERIAL" and n.is_active_output:
            return n
    for n in mat.node_tree.nodes:
        if n.type == "OUTPUT_MATERIAL":
            return n
    return None


def make_target(mat, name, size, non_color=False):
    """An unconnected, selected Image Texture node — the bake writes here."""
    img = bpy.data.images.get(name)
    if img is None or img.size[0] != size:
        img = bpy.data.images.new(name, width=size, height=size, alpha=False)
    img.colorspace_settings.name = "Non-Color" if non_color else "sRGB"

    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    node.label = "BAKE TARGET"
    node.location = (-900, 500)
    for n in mat.node_tree.nodes:
        n.select = False
    node.select = True
    mat.node_tree.nodes.active = node
    return node, img


def emit_reroute(mat, principled):
    """
    Temporarily drive the material output from an Emission shader fed by the
    same colour the surface uses. Returns what is needed to undo it.

    This is the escape hatch for materials that are not a plain Principled
    BSDF: EMIT bakes exactly what is emitted, with no lighting in the picture,
    so it cannot come out black unless the input is black.
    """
    tree = mat.node_tree
    out = output_of(mat)
    if out is None:
        return None
    original = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None

    emission = tree.nodes.new("ShaderNodeEmission")
    emission.location = (out.location.x - 200, out.location.y - 200)

    src = None
    if principled is not None:
        base = principled.inputs["Base Color"]
        if base.links:
            src = base.links[0].from_socket
        else:
            emission.inputs["Color"].default_value = base.default_value
    if src is not None:
        tree.links.new(src, emission.inputs["Color"])

    tree.links.new(emission.outputs["Emission"], out.inputs["Surface"])
    return (out, original, emission)


def emit_restore(mat, state):
    if state is None:
        return
    out, original, emission = state
    tree = mat.node_tree
    for link in list(out.inputs["Surface"].links):
        tree.links.remove(link)
    if original is not None:
        tree.links.new(original, out.inputs["Surface"])
    tree.nodes.remove(emission)


def bake_object(obj, args, report):
    scene = bpy.context.scene

    mats = [s.material for s in obj.material_slots if s.material]
    if not mats:
        report.append((obj.name, "-", "skipped: no material"))
        return

    if args.unwrap and not obj.data.uv_layers:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(island_margin=0.02)
        bpy.ops.object.mode_set(mode="OBJECT")
    if not obj.data.uv_layers:
        report.append((obj.name, "-", "SKIPPED: no UV layer (re-run with --unwrap)"))
        return

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    # ── base colour ──
    targets, states, images = [], [], []
    all_principled = True
    for mat in mats:
        node, img = make_target(mat, "%s_BaseColor" % mat.name, args.size)
        targets.append((mat, node))
        images.append((mat, img, "%s_BaseColor.png" % mat.name))
        if principled_of(mat) is None:
            all_principled = False

    if all_principled:
        # Colour only: no direct light, no bounce, so an unlit scene is fine.
        scene.render.bake.use_pass_direct = False
        scene.render.bake.use_pass_indirect = False
        scene.render.bake.use_pass_color = True
        bake_type = "DIFFUSE"
    else:
        for mat in mats:
            states.append((mat, emit_reroute(mat, principled_of(mat))))
        bake_type = "EMIT"

    scene.render.bake.margin = args.margin
    scene.render.bake.use_clear = True
    scene.render.bake.use_selected_to_active = False

    try:
        bpy.ops.object.bake(type=bake_type)
    except RuntimeError as e:
        report.append((obj.name, bake_type, "FAILED: %s" % e))
        for mat, st in states:
            emit_restore(mat, st)
        for mat, node in targets:
            mat.node_tree.nodes.remove(node)
        return
    finally:
        for mat, st in states:
            emit_restore(mat, st)

    for mat, img, filename in images:
        path = os.path.join(args.out, filename)
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        report.append((obj.name, mat.name, "%s  (%s bake)" % (filename, bake_type)))

    for mat, node in targets:
        mat.node_tree.nodes.remove(node)

    # ── the channels the grain usually lives in ──
    if args.normals:
        bake_channel(obj, mats, args, report, "NORMAL", "Normal")
    if args.roughness:
        bake_channel(obj, mats, args, report, "ROUGHNESS", "Roughness")


def bake_channel(obj, mats, args, report, bake_type, suffix):
    """
    One non-colour pass — normals or roughness. Both are Non-Color data, and
    both are where a procedural material's noise actually shows up when the
    base-colour bake comes out flat.
    """
    targets, images = [], []
    for mat in mats:
        node, img = make_target(mat, "%s_%s" % (mat.name, suffix), args.size, non_color=True)
        targets.append((mat, node))
        images.append((mat, img, "%s_%s.png" % (mat.name, suffix)))
    try:
        bpy.ops.object.bake(type=bake_type)
        for mat, img, filename in images:
            path = os.path.join(args.out, filename)
            img.filepath_raw = path
            img.file_format = "PNG"
            img.save()
            report.append((obj.name, mat.name, "%s  (%s)" % (filename, suffix.lower())))
    except RuntimeError as e:
        report.append((obj.name, suffix.lower(), "FAILED: %s" % e))
    for mat, node in targets:
        mat.node_tree.nodes.remove(node)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"          # EEVEE cannot bake to textures
    scene.cycles.samples = args.samples
    scene.cycles.use_denoising = False

    if args.objects:
        objects = [bpy.data.objects[n] for n in args.objects if n in bpy.data.objects]
        missing = [n for n in args.objects if n not in bpy.data.objects]
        for n in missing:
            print("[bake] no object named '%s'" % n)
    else:
        objects = [o for o in scene.objects if o.type == "MESH"]

    if not objects:
        print("[bake] nothing to bake")
        return

    report = []
    for obj in objects:
        bake_object(obj, args, report)

    print("\n[bake] ---- result ----")
    for obj_name, mat_name, note in report:
        print("[bake] %-18s %-18s %s" % (obj_name, mat_name, note))
    print("[bake] written to %s" % args.out)
    print("[bake] A flat base colour is not necessarily a failed bake: if the")
    print("[bake] material's noise drives bump/roughness, re-run with --normals")
    print("[bake] --roughness and the detail is in THOSE maps.")
    print("[bake] Next: set Texture Type = Normal map on any *_Normal.png in Unity,")
    print("[bake] assign the maps to the .mat files, then run")
    print("[bake] 'Waning Border > Walls > Bind Wall Art'.")


if __name__ == "__main__":
    main()
