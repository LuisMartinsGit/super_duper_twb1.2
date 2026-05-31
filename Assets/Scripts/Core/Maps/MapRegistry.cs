// MapRegistry.cs
// Authoritative list of playable maps. The skirmish / multiplayer lobby
// dropdowns read this list; bootstrap scene-gates accept any entry's
// SceneName as a valid gameplay scene; GameBootstrap reads IsProcedural
// to decide whether to spin up ProceduralTerrain or leave the scene's
// baked-in Unity Terrain alone.
//
// To register a new map:
//   1. Add an entry below.
//   2. Add the .unity scene to File → Build Settings → Scenes in Build.
//   3. Place markers (PlayerStartMarker, IronPatchMarker, etc.) in the
//      scene if you want hand-authored spawn positions. Without markers
//      the game falls back to procedural placement, which assumes a
//      ProceduralTerrain — so non-procedural maps should always have at
//      least PlayerStartMarkers.

using System.Collections.Generic;

namespace TheWaningBorder.Core.Maps
{
    /// <summary>One entry in the playable-map list.</summary>
    public readonly struct MapEntry
    {
        /// <summary>Label shown in the lobby dropdown.</summary>
        public readonly string DisplayName;

        /// <summary>Scene name as it appears in Build Settings (no path, no .unity).</summary>
        public readonly string SceneName;

        /// <summary>
        /// True → GameBootstrap creates ProceduralTerrain and lets it generate
        /// a noise-driven heightmap. False → the scene already contains a
        /// baked Unity Terrain (e.g. MapMagic output) and ProceduralTerrain
        /// is skipped entirely.
        /// </summary>
        public readonly bool IsProcedural;

        public MapEntry(string display, string scene, bool procedural)
        {
            DisplayName = display;
            SceneName = scene;
            IsProcedural = procedural;
        }
    }

    public static class MapRegistry
    {
        // Order matters — index 0 is the default selection.
        // Procedural generation has been removed; only hand-authored maps remain.
        public static readonly IReadOnlyList<MapEntry> Maps = new[]
        {
            new MapEntry("Yiel Lymwérra",        "yielLymwérra",  procedural: false),
        };

        public static MapEntry Default => Maps[0];

        /// <summary>True if the given scene is one of the registered playable
        /// maps. Bootstrap scene-name gates use this to decide whether to
        /// run their "we're in a game" branch.</summary>
        public static bool IsGameplayScene(string sceneName)
        {
            for (int i = 0; i < Maps.Count; i++)
                if (Maps[i].SceneName == sceneName) return true;
            return false;
        }

        /// <summary>Find the entry for the given scene name, or the default
        /// entry if the scene isn't registered.</summary>
        public static MapEntry GetEntry(string sceneName)
        {
            for (int i = 0; i < Maps.Count; i++)
                if (Maps[i].SceneName == sceneName) return Maps[i];
            return Default;
        }

        public static int IndexOf(string sceneName)
        {
            for (int i = 0; i < Maps.Count; i++)
                if (Maps[i].SceneName == sceneName) return i;
            return 0;
        }

        /// <summary>
        /// Authoritative check for "should procedural systems (ProceduralTerrain,
        /// ObstacleBootstrap, the procedural splat layers) run for this scene?"
        ///
        /// Returns true ONLY when both:
        ///   1. The scene's registry entry says IsProcedural, AND
        ///   2. There is no Unity Terrain already in the scene.
        ///
        /// Rule 2 protects against Unicode normalization issues where the
        /// scene name has accented characters (yielLymwérra etc.) and the
        /// registry string match silently fails — the baked Terrain is a more
        /// reliable "the designer hand-authored this map" signal than the
        /// string lookup.
        /// </summary>
        public static bool ShouldRunProceduralGeneration(string sceneName)
        {
            var entry = GetEntry(sceneName);
            if (!entry.IsProcedural) return false;
            // Even if the registry says procedural, a baked Terrain in the
            // scene overrides — never run procedural on top of one.
            return UnityEngine.Terrain.activeTerrain == null;
        }
    }
}
