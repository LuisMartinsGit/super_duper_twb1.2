// File: Assets/Scripts/Presentation/Buildings/BuildingFactionColorMarker.cs
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
        /// Team-color regions in the building atlases are painted in BLUE —
        /// but as shaded art (dark roof shadow up to sky-lit highlight), not
        /// one flat marker value. Matching is therefore done in HSV: a pixel
        /// counts as team-color when its hue falls in [HueMin, HueMax]
        /// degrees AND its saturation exceeds MinSaturation (keeps greys /
        /// near-neutrals out). The replacement keeps the pixel's VALUE
        /// (brightness) so the painted shading survives — a dark-blue roof
        /// shadow becomes dark-red for the Red player, a highlight stays a
        /// highlight.
        /// </summary>
        public static float HueMin = 190f;
        public static float HueMax = 260f;
        public static float MinSaturation = 0.20f;

        /// <summary>
        /// Legacy exact-marker color, still used by the flat-color material
        /// fallback path (materials with no base texture whose _BaseColor is
        /// the marker blue).
        /// </summary>
        public static Color Marker = new Color(0x3A / 255f, 0x7A / 255f, 0xBD / 255f, 1f);

        /// <summary>Match tolerance for the flat-color fallback (RGB
        /// Euclidean distance squared, channels 0..1).</summary>
        public static float ToleranceSquared = 0.04f;

        /// <summary>
        /// Diagnostic — log the first replacement statistics for each
        /// distinct atlas processed (texture name, dimensions, matched
        /// pixel count and percentage). Helps tune Marker / ToleranceSquared
        /// without staring at the model. Set false in production.
        /// </summary>
        public static bool LogReplacementStats = false;

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
        /// THE building faction-color rule. Every path that produces or
        /// refreshes a building visual must funnel through here — spawn,
        /// culture/level variant switch, prefab upgrade swap, age-up refresh.
        /// Three sub-rules are applied per renderer, in priority order:
        ///   1. GameObject named *roof*   → solid faction color (albedo whited out)
        ///   2. GameObject named *stripe* → faction tint over the authored albedo
        ///   3. otherwise                 → atlas pixel swap (marker hue → faction),
        ///      falling back to flat _BaseColor replacement for untextured materials
        /// plus _StripeColor on every material that exposes it.
        /// </summary>
        public static void Apply(GameObject go, Color factionColor)
        {
            if (!Enabled || go == null) return;

            int factionKey = PackKey(factionColor);

            // includeInactive: TRUE. Multi-variant prefabs (BuildingVariantVisual)
            // deactivate every culture branch, level node and tech visual at setup,
            // and BuildingPrefabSwapSystem instantiates upgrade prefabs whose
            // branches are already off. Active-only meant those renderers kept the
            // authored BLUE roof forever and only ever got recolored if that exact
            // branch happened to be re-shown later — the "blue roof after upgrade"
            // bug. Color the whole hierarchy up front; the atlas swap is cached per
            // (texture, faction) so the extra renderers cost lookups, not pixels.
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null) continue;
                // Particles/trails/lines carry their own non-albedo materials.
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
                    renderer is LineRenderer) continue;

                string objName = renderer.gameObject.name;
                bool isRoof = objName.IndexOf("roof",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool isStripe = objName.IndexOf("stripe",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;

                // Renderer.materials returns instanced clones — modifying
                // them won't affect the source prefab or other instances.
                var mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;

                    if (mat.HasProperty("_StripeColor")) mat.SetColor("_StripeColor", factionColor);

                    // Name-tagged parts are a flat solid faction color. They must
                    // NOT also run the atlas swap: the roof rule assigns
                    // Texture2D.whiteTexture, which the swap would then try to
                    // GetPixels32 on every re-apply (unreadable → warning spam).
                    if (isRoof)   { PaintSolid(mat, factionColor); continue; }
                    if (isStripe) { SetTint(mat, factionColor);    continue; }

                    if (TryReplaceAtlasTexture(mat, factionColor, factionKey)) continue;
                    TryReplaceFlatColor(mat, factionColor);
                }
            }
        }

        /// <summary>Roof rule — blank the albedo so the faction color reads solid.</summary>
        private static void PaintSolid(Material mat, Color factionColor)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            SetTint(mat, factionColor);
        }

        /// <summary>Stripe rule — tint only, authored albedo detail survives.</summary>
        private static void SetTint(Material mat, Color factionColor)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", factionColor);
            else if (mat.HasProperty("_Color")) mat.color = factionColor;
        }

        // ──────────────────────────────────────────────────────────────────
        // ATLAS TEXTURE PATH (preferred — used by hand-authored prefabs)
        // ──────────────────────────────────────────────────────────────────

        // Only the MAIN ALBEDO slots are candidates for the recolor swap.
        // Walking every texture property was actively harmful:
        //   * normal maps are blue-dominant BY ENCODING (128,128,255 = hue
        //     ~240) — the hue matcher would repaint the entire bump map and
        //     shred the lighting;
        //   * detail/mask/metallic maps carry non-color data with their own
        //     sampling semantics (e.g. the GatherersHut renders through a
        //     detail-albedo MULX2 with a white base — swapping that slot
        //     blew the whole building out to white).
        private static readonly string[] AlbedoProps = { "_BaseMap", "_MainTex" };

        private static bool TryReplaceAtlasTexture(Material mat, Color factionColor, int factionKey)
        {
            bool replacedAny = false;
            for (int p = 0; p < AlbedoProps.Length; p++)
            {
                string prop = AlbedoProps[p];
                if (!mat.HasProperty(prop)) continue;
                Texture2D source = mat.GetTexture(prop) as Texture2D;
                if (source == null) continue;
                // The two albedo aliases often point at the same texture on
                // URP materials — swapping one is enough.
                if (p == 1 && replacedAny) break;

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
            var pixels = source.GetPixels32();

            // Faction hue/saturation replace the pixel's; the pixel's VALUE
            // is kept so the atlas shading (shadow/highlight painting on the
            // blue regions) carries through to the recolored building.
            Color.RGBToHSV(factionColor, out float facH, out float facS, out float facV);

            int replaced = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var c = new Color(p.r / 255f, p.g / 255f, p.b / 255f, 1f);
                Color.RGBToHSV(c, out float h, out float s, out float v);

                float hueDeg = h * 360f;
                if (hueDeg < HueMin || hueDeg > HueMax) continue;
                if (s < MinSaturation) continue;

                // Keep the pixel's brightness; take the faction's hue and a
                // saturation blended toward the faction's (fully faction-
                // saturated highlights look plasticky, so weight by the
                // pixel's own saturation).
                var outColor = Color.HSVToRGB(facH, facS * Mathf.Clamp01(s / Mathf.Max(0.001f, facS)), v);
                pixels[i].r = (byte)Mathf.Clamp(Mathf.RoundToInt(outColor.r * 255f), 0, 255);
                pixels[i].g = (byte)Mathf.Clamp(Mathf.RoundToInt(outColor.g * 255f), 0, 255);
                pixels[i].b = (byte)Mathf.Clamp(Mathf.RoundToInt(outColor.b * 255f), 0, 255);
                replaced++;
            }

            if (LogReplacementStats)
            {
                float pct = pixels.Length > 0 ? (100f * replaced / pixels.Length) : 0f;
                Debug.Log($"[BuildingFactionColorMarker] '{source.name}' " +
                          $"{source.width}x{source.height}: {replaced}/{pixels.Length} pixels matched " +
                          $"(~{pct:F1}%) — hue [{HueMin:F0}..{HueMax:F0}], minSat {MinSaturation:F2}");
            }

            if (replaced == 0) return null;

            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32,
                mipChain: source.mipmapCount > 1, linear: false);
            clone.name = $"{source.name}_Faction_{PackKey(factionColor):X6}";
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
