// Recolors ONLY the blue "team-color" region of a Synty character atlas with
// the faction colour, leaving skin / leather / metal untouched. Unlike a flat
// _BaseColor tint (which washes the whole textured character one colour), this
// produces a per-faction copy of the atlas where blue-team texels are swapped
// to the faction hue at the same brightness, so shading is preserved.
//
// Detection is hue-based (b > g > r, sufficiently saturated) rather than an
// exact marker match, because the Synty team region spans a family of shaded
// blues — e.g. (62,107,158) plus darker (48,81,120) / (34,58,87) — that an
// exact-match would only partially catch. See BuildingFactionColorMarker for
// the building-atlas (exact marker) counterpart.
//
// Requirement: the atlas texture must have Read/Write Enabled. If it isn't,
// Apply returns false (silently) and the caller falls back to its normal tint.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public static class SyntyTeamColorRecolor
    {
        /// <summary>Master switch — flip false to see the source atlas unmodified.</summary>
        public static bool Enabled = true;

        // Per (source texture, faction RGB key) -> recolored copy. Bounded:
        // ≤ 8 factions × a handful of character atlases per session.
        private static readonly Dictionary<(Texture2D, int), Texture2D> _cache = new();
        // Negative cache: textures with no team-blue pixels (or unreadable) so
        // we don't reprocess them every spawn.
        private static readonly HashSet<(Texture2D, int)> _noMatch = new();

        /// <summary>
        /// Recolor the blue team region of every readable atlas under
        /// <paramref name="go"/> to <paramref name="factionColor"/>. Returns
        /// true if at least one material's atlas was recolored (so the caller
        /// can skip its flat-tint fallback).
        /// </summary>
        public static bool Apply(GameObject go, Color factionColor)
        {
            if (!Enabled || go == null) return false;

            int key = PackKey(factionColor);
            bool any = false;

            var renderers = go.GetComponentsInChildren<Renderer>();
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r] != null ? renderers[r].materials : null; // instanced clones
                if (mats == null) continue;

                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;

                    var src = GetBaseTexture(mat) as Texture2D;
                    if (src == null) continue;

                    var cacheKey = (src, key);
                    if (_noMatch.Contains(cacheKey)) continue;

                    Texture2D recolored;
                    if (!_cache.TryGetValue(cacheKey, out recolored))
                    {
                        recolored = BuildRecolored(src, factionColor);
                        if (recolored == null) { _noMatch.Add(cacheKey); continue; }
                        _cache[cacheKey] = recolored;
                    }

                    AssignBaseTexture(mat, recolored);
                    // Clear any prior flat tint so the texture shows true.
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                    if (mat.HasProperty("_Color")) mat.color = Color.white;
                    any = true;
                }
            }
            return any;
        }

        // Build the per-faction copy. Returns null if the texture is unreadable
        // or contains no blue-team pixels.
        private static Texture2D BuildRecolored(Texture2D src, Color factionColor)
        {
            Color32[] pixels;
            try { pixels = src.GetPixels32(); }
            catch { return null; } // not Read/Write enabled

            // Faction luminance used to scale each team texel to its own brightness.
            float facLum = 0.299f * factionColor.r + 0.587f * factionColor.g + 0.114f * factionColor.b;
            if (facLum < 0.001f) facLum = 0.001f;

            int matched = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (!IsTeamBlue(p)) continue;

                // Preserve the source texel's luminance, swap in the faction hue.
                float lum = (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) / 255f;
                float scale = lum / facLum;
                pixels[i] = new Color32(
                    (byte)Mathf.Clamp(factionColor.r * scale * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(factionColor.g * scale * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(factionColor.b * scale * 255f, 0f, 255f),
                    p.a);
                matched++;
            }

            if (matched == 0) return null;

            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, src.mipmapCount > 1);
            copy.name = src.name + "_TeamRecolor";
            copy.wrapMode = src.wrapMode;
            copy.filterMode = src.filterMode;
            copy.SetPixels32(pixels);
            copy.Apply(true);
            return copy;
        }

        // Blue team cloth: blue clearly dominant, green in the middle, red lowest
        // (b > g > r). Excludes purples (g < r) and near-greys (low saturation).
        private static bool IsTeamBlue(Color32 p)
        {
            return p.b > p.g && p.g >= p.r && (p.b - p.r) > 28 && p.b > 60;
        }

        private static Texture GetBaseTexture(Material mat)
        {
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return mat.GetTexture("_BaseMap");
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) return mat.GetTexture("_MainTex");
            return null;
        }

        private static void AssignBaseTexture(Material mat, Texture tex)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        private static int PackKey(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            return (r << 16) | (g << 8) | b;
        }
    }
}
