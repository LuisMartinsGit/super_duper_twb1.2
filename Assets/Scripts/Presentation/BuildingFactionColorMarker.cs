// File: Assets/Scripts/Presentation/BuildingFactionColorMarker.cs
// Faction-color masking for hand-authored building prefabs (Hall_al_1,
// Barracks_al_2, etc.). Artists paint regions that should adopt the
// player's faction color with a flat marker color (default: pure blue,
// RGB 0,0,1). At runtime, after the prefab is instantiated, we walk
// each renderer's materials, detect any whose _BaseColor is close to
// the marker, and replace it with the faction color.
//
// Texture-based marker detection (replacing pixels in a baked texture)
// would need a custom shader and a separate mask texture per material.
// This system covers the simpler authoring convention: sub-meshes with
// flat-color materials. If a prefab needs textured team-color regions
// later, switch the mask material to a flat marker hue OR move to a
// shader-based replacement.
//
// Tunables live as static fields so the marker hue / tolerance can be
// tweaked from anywhere without touching call sites.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class BuildingFactionColorMarker
    {
        /// <summary>
        /// Marker color in authored prefabs. Default: pure blue (0,0,1).
        /// Materials with _BaseColor (or _Color) within Tolerance of this
        /// hue are recolored to the faction color at spawn / swap time.
        /// </summary>
        public static Color Marker = new Color(0f, 0f, 1f, 1f);

        /// <summary>
        /// Match tolerance in RGB Euclidean distance (squared). 0.25 ≈
        /// allows ±15% RGB drift before the match fails — wide enough
        /// to absorb texture compression rounding, narrow enough to
        /// avoid coloring genuine-blue art assets.
        /// </summary>
        public static float ToleranceSquared = 0.25f * 0.25f;

        /// <summary>
        /// Walk every Renderer under <paramref name="go"/>; for each
        /// material whose base color is within tolerance of Marker,
        /// replace it with <paramref name="factionColor"/>. Material
        /// instancing happens automatically via Renderer.materials.
        /// </summary>
        public static void Apply(GameObject go, Color factionColor)
        {
            if (go == null) return;

            var renderers = go.GetComponentsInChildren<Renderer>();
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null) continue;

                // Renderer.materials returns instanced clones — modifying
                // them won't affect the source prefab or other instances.
                var mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;

                    Color baseColor = ReadBaseColor(mat);
                    if (!IsCloseToMarker(baseColor)) continue;

                    WriteBaseColor(mat, factionColor);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // INTERNAL HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static Color ReadBaseColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))     return mat.color;
            return Color.white;
        }

        private static void WriteBaseColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.color = c;
        }

        private static bool IsCloseToMarker(Color c)
        {
            float dr = c.r - Marker.r;
            float dg = c.g - Marker.g;
            float db = c.b - Marker.b;
            return (dr * dr + dg * dg + db * db) <= ToleranceSquared;
        }
    }
}
