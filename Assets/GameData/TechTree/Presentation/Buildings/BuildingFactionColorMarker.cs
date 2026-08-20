// File: Assets/GameData/TechTree/Presentation/Buildings/BuildingFactionColorMarker.cs
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
// Read/Write Enabled on the atlas is the FAST path. When it is off we
// take a GPU copy (Blit into a temporary sRGB RenderTexture, ReadPixels
// back) and swap that instead, so the recolor no longer depends on an
// import flag being remembered per asset. Before this, an unreadable
// atlas silently produced no team color at all — that is what left the
// Gatherer's Hut rings authored-blue for every player.

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

        /// <summary>
        /// One cache slot per (source texture, faction RGB key).
        /// <para>
        /// HadMarkerPixels is stored SEPARATELY from Swapped on purpose. The
        /// cache used to be a plain texture-or-null dictionary where null meant
        /// "this atlas has no marker pixels, never look again" — but the swapped
        /// textures are runtime objects, and a destroyed one compares equal to
        /// null through Unity's overloaded operator. So the moment a swapped
        /// atlas died (scene unload between matches), its slot silently turned
        /// into a permanent negative hit and every building using that atlas
        /// stayed authored-blue for the rest of the session. With the flag kept
        /// apart, a dead texture is rebuilt and only a genuine no-marker atlas
        /// short-circuits.
        /// </para>
        /// </summary>
        private struct SwapEntry
        {
            public Texture2D Swapped;
            public bool HadMarkerPixels;
        }

        private static readonly Dictionary<(Texture2D, int), SwapEntry> _swappedCache
            = new Dictionary<(Texture2D, int), SwapEntry>();

        // Reverse map for the textures WE generated: clone -> (original source,
        // faction key it was built for). Re-applying the rule to an already
        // recolored material would otherwise feed our own clone back into
        // BuildSwappedAtlas — which finds no marker hue (they are the faction
        // hue now), caches a fresh negative entry per clone, and grows the
        // dictionary on every upgrade / tech-visual refresh.
        private static readonly Dictionary<Texture2D, (Texture2D Source, int FactionKey)> _cloneOrigin
            = new Dictionary<Texture2D, (Texture2D, int)>();

        // Textures we've reported as needing the slow GPU-copy path — so we
        // don't repeat the message once per frame for the same asset.
        private static readonly HashSet<Texture2D> _reportedUnreadable
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

            // Remember the owner's color ON the visual so any later pass that
            // rewrites materials (the level-up dissolve, the battle-damage
            // shader swap) can put it back without having to reach into ECS
            // for the FactionTag. See Reapply.
            var stamp = go.GetComponent<BuildingFactionColorStamp>();
            if (stamp == null) stamp = go.AddComponent<BuildingFactionColorStamp>();
            stamp.Value = factionColor;

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
                    // read back on every re-apply — a pointless GPU copy of a
                    // 4x4 white texture that can never contain a marker pixel.
                    if (isRoof)   { PaintSolid(mat, factionColor); continue; }
                    if (isStripe) { SetTint(mat, factionColor);    continue; }

                    if (TryReplaceAtlasTexture(mat, factionColor, factionKey)) continue;
                    TryReplaceFlatColor(mat, factionColor);
                }
            }
        }

        /// <summary>
        /// Re-run the rule on a visual that was already colored once, using the
        /// color recorded by the last <see cref="Apply"/>. The stamp is looked
        /// up on <paramref name="go"/> or any ancestor, so callers can pass a
        /// single culture/level BRANCH of a multi-variant prefab and still get
        /// the whole building recolored from its root.
        /// <para>
        /// This is what makes the rule survive the level-up dissolve, which
        /// binds its own lit-dissolve instances to every renderer for two
        /// seconds and hands the captured originals back at the end — that
        /// restore is the last word on the visual's materials, so the recolor
        /// has to be re-asserted after it or the upgraded building settles back
        /// into its authored colors.
        /// </para>
        /// Returns false when the visual was never colored (nothing to redo).
        /// </summary>
        public static bool Reapply(GameObject go)
        {
            if (!Enabled || go == null) return false;
            var stamp = go.GetComponentInParent<BuildingFactionColorStamp>(includeInactive: true);
            if (stamp == null) return false;
            Apply(stamp.gameObject, stamp.Value);
            return true;
        }

        /// <summary>Roof rule — blank the albedo so the faction color reads solid.</summary>
        private static void PaintSolid(Material mat, Color factionColor)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            // BuildingDamage samples the albedo only when _UseBaseMap is on; a
            // roof re-colored after the damage swap would otherwise ignore the
            // white map we just assigned.
            if (mat.HasProperty("_UseBaseMap")) mat.SetFloat("_UseBaseMap", 1f);
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

                // The slot may already hold one of OUR clones (re-apply after a
                // level-up / dissolve / damage swap). Same faction: nothing to
                // do. Different faction: rebuild from the ORIGINAL atlas, never
                // from the clone — recoloring a recolor compounds the hue.
                if (_cloneOrigin.TryGetValue(source, out var origin))
                {
                    if (origin.FactionKey == factionKey) { replacedAny = true; continue; }
                    source = origin.Source;
                    if (source == null) continue;
                }

                var key = (source, factionKey);
                if (_swappedCache.TryGetValue(key, out var cached))
                {
                    // A cached texture that has since been destroyed (scene
                    // unload) must be rebuilt, NOT read as "no marker pixels".
                    if (cached.Swapped != null)
                    {
                        mat.SetTexture(prop, cached.Swapped);
                        replacedAny = true;
                        continue;
                    }
                    if (!cached.HadMarkerPixels) continue; // genuine negative hit
                }

                var swapped = BuildSwappedAtlas(source, factionColor);
                _swappedCache[key] = new SwapEntry
                {
                    Swapped         = swapped,
                    HadMarkerPixels = swapped != null,
                };
                if (swapped == null) continue;

                _cloneOrigin[swapped] = (source, factionKey);
                mat.SetTexture(prop, swapped);
                replacedAny = true;
            }
            return replacedAny;
        }

        /// <summary>
        /// Pixels of <paramref name="source"/>, whether or not the asset was
        /// imported with Read/Write Enabled. Readable textures are read
        /// directly; the rest go around through the GPU (Blit into a temporary
        /// sRGB RenderTexture, ReadPixels back), which costs one copy per atlas
        /// per faction and is cached exactly like the fast path.
        /// </summary>
        private static Color32[] ReadPixels(Texture2D source)
        {
            if (source.isReadable) return source.GetPixels32();

            if (_reportedUnreadable.Add(source))
            {
                Debug.Log(
                    $"[BuildingFactionColorMarker] Texture '{source.name}' is not Read/Write enabled — " +
                    "taking a GPU copy to recolor it. Enabling Read/Write in its import settings skips " +
                    "the copy (run Waning Border > Buildings > Audit Faction Colors to fix them all).");
            }

            RenderTexture rt = null;
            Texture2D readable = null;
            var previous = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32,
                    mipChain: false, linear: false);
                readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return readable.GetPixels32();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildingFactionColorMarker] GPU copy of '{source.name}' failed " +
                                 $"({e.Message}) — team color skipped for this atlas.");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readable != null) Object.Destroy(readable);
            }
        }

        /// <summary>
        /// Allocate a new RGBA32 Texture2D the same size as <paramref name="source"/>
        /// and copy pixels with the marker hue replaced by <paramref name="factionColor"/>.
        /// Returns null if the atlas contains no marker pixels (caller treats this
        /// as a negative-cache hit so the source isn't reprocessed).
        /// </summary>
        private static Texture2D BuildSwappedAtlas(Texture2D source, Color factionColor)
        {
            var pixels = ReadPixels(source);
            if (pixels == null) return null;

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
            // The cache outlives any single scene; without this an
            // UnloadUnusedAssets sweep can reap a clone that is only reachable
            // from runtime material instances.
            clone.hideFlags = HideFlags.HideAndDontSave;
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
