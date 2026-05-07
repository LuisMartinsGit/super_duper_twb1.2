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
        /// Master switch. Flip false at runtime to bypass the marker
        /// replacement entirely (e.g. for debugging — see the original
        /// prefab unmodified). Defaults true.
        /// </summary>
        public static bool Enabled = true;

        /// <summary>
        /// Marker color in authored prefabs / atlas textures. Default
        /// matches the project's actual marker (#3A7ABD ≈ 58/122/189) —
        /// the muted cornflower blue artists paint on team-color regions
        /// of the building atlas. Materials whose base map contains
        /// pixels within Tolerance of this hue have those pixels
        /// recolored to the faction color at spawn / swap time.
        /// </summary>
        public static Color Marker = new Color(0x3A / 255f, 0x7A / 255f, 0xBD / 255f, 1f);

        /// <summary>
        /// Match tolerance in RGB Euclidean distance squared (per channel
        /// values are 0..1). Default 0.01 (≈ ±0.1 per channel, ±10%) is
        /// strict enough to catch only the marker hue and not adjacent
        /// shades of blue baked into the atlas. If your art uses an
        /// off-pure-blue marker (e.g. RGB 50,80,255), bump this to ~0.04
        /// or set Marker to the exact hue used.
        /// </summary>
        public static float ToleranceSquared = 0.01f;

        /// <summary>
        /// Diagnostic — log the first replacement statistics for each
        /// distinct atlas processed (texture name, dimensions, matched
        /// pixel count and percentage). Helps tune Marker / ToleranceSquared
        /// without staring at the model. Set false in production.
        /// </summary>
        public static bool LogReplacementStats = true;

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
            if (!Enabled || go == null) return;

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
            // Walk every texture property the shader exposes — custom
            // shaders rarely use the URP-default _BaseMap / _MainTex names,
            // so we look at all of them and swap any whose texture is a
            // readable Texture2D containing marker pixels. (_BaseMap and
            // _MainTex show up in GetTexturePropertyNames too, so this
            // also covers the standard URP / Built-in cases.)
            var propNames = mat.GetTexturePropertyNames();
            bool replacedAny = false;
            for (int p = 0; p < propNames.Length; p++)
            {
                string prop = propNames[p];
                Texture2D source = mat.GetTexture(prop) as Texture2D;
                if (source == null) continue;

                if (!source.isReadable)
                {
                    if (_warnedUnreadable.Add(source))
                    {
                        Debug.LogWarning(
                            $"[BuildingFactionColorMarker] Texture '{source.name}' (shader prop '{prop}') " +
                            "is not Read/Write enabled — atlas pixel replacement skipped. Enable Read/Write " +
                            "in the texture's import settings.");
                    }
                    continue;
                }

                var key = (source, factionKey);
                if (_swappedCache.TryGetValue(key, out var cached))
                {
                    if (cached == null) continue; // negative cache: no marker pixels
                    mat.SetTexture(prop, cached);
                    replacedAny = true;
                    continue;
                }

                var swapped = BuildSwappedAtlas(source, factionColor);
                _swappedCache[key] = swapped; // null entry too — negative cache
                if (swapped == null) continue;

                mat.SetTexture(prop, swapped);
                replacedAny = true;
            }
            return replacedAny;
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

            int replaced = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int dr = pixels[i].r - markerR;
                int dg = pixels[i].g - markerG;
                int db = pixels[i].b - markerB;
                if (dr * dr + dg * dg + db * db > toleranceByteSq) continue;
                pixels[i].r = facR;
                pixels[i].g = facG;
                pixels[i].b = facB;
                replaced++;
            }

            if (LogReplacementStats)
            {
                float pct = pixels.Length > 0 ? (100f * replaced / pixels.Length) : 0f;
                Debug.Log($"[BuildingFactionColorMarker] '{source.name}' " +
                          $"{source.width}×{source.height}: {replaced}/{pixels.Length} pixels matched " +
                          $"(~{pct:F1}%) — Marker={Marker}, Tol²={ToleranceSquared:F4}");
            }

            if (replaced == 0) return null;

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

        private static int PackKey(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }
    }
}
