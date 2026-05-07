// File: Assets/Scripts/Presentation/BuildingFactionColorMarker.cs
// Faction-color masking for hand-authored building prefabs that share a
// single color ATLAS texture. Artists paint dynamic team-color regions
// in the atlas with a flat marker hue (default: pure blue, RGB 0,0,1).
// At spawn / swap time we walk each material, take the atlas texture,
// produce a per-faction copy with the marker pixels replaced by the
// faction color, and reassign that copy to the material instance.
//
// Per (source atlas, faction color) the swapped texture is cached and
// reused — at most one allocation per atlas per faction per session
// (≤ 8 player factions × small handful of atlases = bounded memory).
//
// Requirement: the atlas texture must have Read/Write Enabled in its
// import settings, otherwise GetPixels32 throws and we silently fall
// back to flat-color replacement (logs a warning the first time per
// texture so the missing flag is easy to find).

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class BuildingFactionColorMarker
    {
        /// <summary>
        /// Marker color in authored prefabs / atlas textures. Default:
        /// pure blue (0,0,1). Materials whose base map contains pixels
        /// within Tolerance of this hue have those pixels recolored to
        /// the faction color at spawn / swap time.
        /// </summary>
        public static Color Marker = new Color(0f, 0f, 1f, 1f);

        /// <summary>
        /// Match tolerance in RGB Euclidean distance squared (per channel
        /// values are 0..1). 0.0625 ≈ ±0.25 per channel — wide enough to
        /// absorb texture compression rounding, narrow enough not to
        /// recolor genuine-blue art on the same atlas.
        /// </summary>
        public static float ToleranceSquared = 0.25f * 0.25f;

        // ──────────────────────────────────────────────────────────────────
        // CACHES
        // ──────────────────────────────────────────────────────────────────

        // Per (source texture, faction RGB key) — the swapped variant.
        // Null entries are negative cache hits (source had no marker
        // pixels) so we don't reprocess the same atlas every spawn.
        private static readonly Dictionary<(Texture2D, int), Texture2D> _swappedCache
            = new Dictionary<(Texture2D, int), Texture2D>();

        // Textures we've warned about for missing Read/Write — so we don't
        // spam the console once per frame for the same asset.
        private static readonly HashSet<Texture2D> _warnedUnreadable
            = new HashSet<Texture2D>();

        // ──────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Walk every Renderer under <paramref name="go"/>; replace the
        /// atlas texture on each material with a faction-recolored copy
        /// (pixels close to Marker → factionColor). Falls back to flat
        /// _BaseColor replacement for materials with no base texture.
        /// </summary>
        public static void Apply(GameObject go, Color factionColor)
        {
            if (go == null) return;

            int factionKey = PackKey(factionColor);

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

                    if (TryReplaceAtlasTexture(mat, factionColor, factionKey)) continue;
                    TryReplaceFlatColor(mat, factionColor);
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // ATLAS TEXTURE PATH (preferred — used by hand-authored prefabs)
        // ──────────────────────────────────────────────────────────────────

        private static bool TryReplaceAtlasTexture(Material mat, Color factionColor, int factionKey)
        {
            Texture2D source = ReadBaseTexture(mat) as Texture2D;
            if (source == null) return false;

            if (!source.isReadable)
            {
                if (_warnedUnreadable.Add(source))
                {
                    Debug.LogWarning(
                        $"[BuildingFactionColorMarker] Texture '{source.name}' is not Read/Write " +
                        "enabled — atlas pixel replacement skipped. Enable Read/Write in the texture's " +
                        "import settings for faction coloring to work on prefabs that use it.");
                }
                return false;
            }

            var key = (source, factionKey);
            if (_swappedCache.TryGetValue(key, out var cached))
            {
                if (cached == null) return false; // negative cache: no marker pixels
                WriteBaseTexture(mat, cached);
                return true;
            }

            var swapped = BuildSwappedAtlas(source, factionColor);
            _swappedCache[key] = swapped; // null entry too — negative cache
            if (swapped == null) return false;

            WriteBaseTexture(mat, swapped);
            return true;
        }

        /// <summary>
        /// Allocate a new RGBA32 Texture2D the same size as <paramref name="source"/>
        /// and copy pixels with the marker hue replaced by <paramref name="factionColor"/>.
        /// Returns null if the atlas contains no marker pixels (caller treats this
        /// as a negative-cache hit so the source isn't reprocessed).
        /// </summary>
        private static Texture2D BuildSwappedAtlas(Texture2D source, Color factionColor)
        {
            // Color32 (byte channels) is ~4x faster than Color floats and the
            // tolerance check still works at byte precision.
            var pixels = source.GetPixels32();

            int markerR = Mathf.RoundToInt(Marker.r * 255f);
            int markerG = Mathf.RoundToInt(Marker.g * 255f);
            int markerB = Mathf.RoundToInt(Marker.b * 255f);
            int toleranceByteSq = Mathf.RoundToInt(ToleranceSquared * 255f * 255f);

            byte facR = (byte)Mathf.Clamp(Mathf.RoundToInt(factionColor.r * 255f), 0, 255);
            byte facG = (byte)Mathf.Clamp(Mathf.RoundToInt(factionColor.g * 255f), 0, 255);
            byte facB = (byte)Mathf.Clamp(Mathf.RoundToInt(factionColor.b * 255f), 0, 255);

            bool any = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                int dr = pixels[i].r - markerR;
                int dg = pixels[i].g - markerG;
                int db = pixels[i].b - markerB;
                if (dr * dr + dg * dg + db * db > toleranceByteSq) continue;
                pixels[i].r = facR;
                pixels[i].g = facG;
                pixels[i].b = facB;
                any = true;
            }

            if (!any) return null;

            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32,
                mipChain: source.mipmapCount > 1, linear: false);
            clone.name = $"{source.name}_Faction_{facR:X2}{facG:X2}{facB:X2}";
            clone.wrapMode  = source.wrapMode;
            clone.filterMode = source.filterMode;
            clone.SetPixels32(pixels);
            clone.Apply(updateMipmaps: clone.mipmapCount > 1, makeNoLongerReadable: false);
            return clone;
        }

        // ──────────────────────────────────────────────────────────────────
        // FLAT-COLOR FALLBACK (for materials with no base texture)
        // ──────────────────────────────────────────────────────────────────

        private static void TryReplaceFlatColor(Material mat, Color factionColor)
        {
            Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                            : mat.HasProperty("_Color")     ? mat.color
                            : Color.white;
            float dr = baseColor.r - Marker.r;
            float dg = baseColor.g - Marker.g;
            float db = baseColor.b - Marker.b;
            if (dr * dr + dg * dg + db * db > ToleranceSquared) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", factionColor);
            else if (mat.HasProperty("_Color")) mat.color = factionColor;
        }

        // ──────────────────────────────────────────────────────────────────
        // INTERNAL HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static Texture ReadBaseTexture(Material mat)
        {
            if (mat.HasProperty("_BaseMap")) return mat.GetTexture("_BaseMap");
            if (mat.HasProperty("_MainTex")) return mat.mainTexture;
            return null;
        }

        private static void WriteBaseTexture(Material mat, Texture tex)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (mat.HasProperty("_MainTex")) mat.mainTexture = tex;
        }

        private static int PackKey(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }
    }
}
