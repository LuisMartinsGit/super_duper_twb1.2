// TechTreeModelPostprocessor.cs
// Forces the importer settings TechTree art cannot work without, on every
// import, automatically.
//
// WHY THIS EXISTS. Re-exporting an FBX from Blender REWRITES ITS .meta, which
// resets every importer setting to the project default. So an artist who
// tweaks a model and exports again silently loses Read/Write — and a wall
// whose meshes are not readable cannot be baked into one mesh per segment, so
// it falls back to the procedural placeholder and looks like nothing changed.
// That round trip cost an afternoon on 2026-09-21.
//
// Fixing it by hand, or by remembering to run a menu item, is the same bet
// again every export. This makes the import itself correct:
//
//   * Read/Write on   — WallArtMesh reads vertices to bake the curtain.
//   * no cameras      — Blender's default camera rides along in the export.
//   * no lights       — so does its lamp; one per wall module would be a
//                       lighting catastrophe.
//
// It says NOTHING about the rig or about materials. Overriding animationType
// strips a character's avatar, and .fbx.meta is gitignored — there is no
// history to restore from. It is scoped to the WALL folder for the same
// reason: the blast radius of an importer rule is every asset it can reach.
//
// Material naming and extraction are likewise the artist's call.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public sealed class TechTreeModelPostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// THE WALL ONLY, and deliberately so.
        ///
        /// This was scoped to the whole of `GameData/TechTree/` for about an
        /// hour on 2026-09-21 and it was a disaster: the scope includes every
        /// UNIT, and forcing `animationType = None` on a rigged character
        /// strips its avatar. Every animation in the game stopped and workers
        /// spawned waist-deep in the ground, because a humanoid without its
        /// avatar loses the hip height the rig was placed by. Nothing below
        /// enforces an animation setting any more, and the scope is the one
        /// folder that actually needs Read/Write.
        /// </summary>
        const string Scope = "Assets/GameData/TechTree/Age0/Buildings/Wall/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Scope, System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            // Enforced on EVERY import, not just the first. A re-export
            // resets the .meta wholesale, so "set it once" is the bug this
            // exists to stop.
            //
            // NOTHING here touches the RIG. animationType, avatarSetup and the
            // human description are the artist's and the retarget's business,
            // and an importer that overrides them destroys work that cannot be
            // recovered — .fbx.meta is gitignored, so there is no history to
            // restore from.
            bool wasWrong = !importer.isReadable || importer.importCameras
                            || importer.importLights;

            if (!importer.isReadable) importer.isReadable = true;
            if (importer.importCameras) importer.importCameras = false;
            if (importer.importLights) importer.importLights = false;

            if (wasWrong)
                Debug.Log($"[TechTreeModel] {assetPath}: Read/Write on, cameras/lights/rig off " +
                          "(a Blender re-export resets these).");
        }
    }
}
