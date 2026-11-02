// Faction-color masking for hand-authored building prefabs that share a
// single color ATLAS texture. Artists paint dynamic team-color regions
// in the atlas in a blue marker hue band.
//
// PLAYER COLOUR IS A SHADER PROPERTY (2026-10-10). The recolour runs per
// pixel on the GPU (Assets/Shaders/Include/TWBFactionColor.hlsl, used by
// TheWaningBorder/BuildingLit and BuildingLitDissolve); this
// class only sets `_TWB_FactionHSV` (the faction's HSV + a mode) on each
// material. It used to copy every albedo atlas per faction on the CPU —
// GetPixels32 + RGBToHSV over ~4 M pixels per 2048² texture, ~170 MB of
// garbage — and the first sight of a new prefab for a faction (the Trading
// Outpost's House_1 at age-up) froze the game for 1-3 s, once per player.
//
// Building materials are TheWaningBorder/BuildingLit on disk (Waning Border >
// Buildings > Convert Materials To BuildingLit). A URP Lit material that
// slipped through is switched to it at runtime, with a one-time warning.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
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
        /// Roofs are dark slate (docs/Design/Art_Direction.md §4): the player
        /// colour lives in lantern / window glow and cloth, not on the roof.
        /// This is what a name-tagged <c>*roof*</c> part is painted. No current
        /// building model has one (all 43 FBXs scanned 2026-09-18) — the blue
        /// roofs on screen are atlas pixels through the swap below — so this is
        /// the guard for authored roofs, not the thing that darkens today's.
        /// </summary>
        public static Color RoofSlate = new Color(0x3B / 255f, 0x3E / 255f, 0x46 / 255f, 1f);

        /// <summary>
        /// INTERIM (Art_Direction.md §4.3), removed when the atlas repaint lands.
        /// Until roofs leave the marker hue band and lanterns / cloth enter it,
        /// every swapped pixel IS a roof, so the swap darkens (value ×) what it
        /// recolours: a dark faction-coloured roof instead of the saturated
        /// slab. Saturation is NOT reduced: the first cut (0.6) turned Blue's
        /// roof into a chroma-starved slab that read as brown against the navy
        /// scene — the player colour has to stay a colour. Set both to 1 to see
        /// the plain swap.
        /// </summary>
        public static float InterimSwapValue = 0.65f;
        public static float InterimSwapSaturation = 1.0f;

        /// <summary>
        /// Player-tint parts (the *_Player / *Castle_Roof* rule below) take the
        /// player colour VIVID (2026-11-02): the lobby swatch with its
        /// saturation pulled this fraction of the way to full. The reference
        /// look is the Fortress's old blue - the kit's teal slate multiplied by
        /// the Blue swatch, which came out at ~0.95 saturation because the two
        /// hues agreed. Over the neutral `*_Player` tile the swatch alone gives
        /// ~0.8 and reads faded, and the 30 % grey fade this replaced (2026-11-01)
        /// made every roof slate. 0 = the swatch as is, 1 = fully saturated.
        /// A grey swatch (Silver, Black) has no hue to boost and stays grey.
        /// </summary>
        public static float PlayerTintSaturate = 0.75f;

        /// <summary>
        /// Value (brightness) multiplier on the tinted swatch. The teal slate x
        /// Blue landed at value ~0.38 where the neutral tile x the swatch lands
        /// at ~0.48, so 0.8 keeps the vivid roof as deep as the one it matches.
        /// </summary>
        public static float PlayerTintValue = 0.8f;

        /// <summary>
        /// Legacy exact-marker color, still used by the flat-color material
        /// fallback path (materials with no base texture whose _BaseColor is
        /// the marker blue).
        /// </summary>
        public static Color Marker = new Color(0x3A / 255f, 0x7A / 255f, 0xBD / 255f, 1f);

        /// <summary>Match tolerance for the flat-color fallback (RGB
        /// Euclidean distance squared, channels 0..1).</summary>
        public static float ToleranceSquared = 0.04f;

        // ──────────────────────────────────────────────────────────────────
        // SHADER CONTRACT (TWBFactionColor.hlsl)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>`_TWB_FactionHSV.w`: no recolour.</summary>
        public const float ModeOff = 0f;
        /// <summary>`_TWB_FactionHSV.w`: marker-hue atlas swap.</summary>
        public const float ModeHueSwap = 1f;
        /// <summary>`_TWB_FactionHSV.w`: tint under `_TWB_FactionMask`.</summary>
        public const float ModeMask = 2f;

        private static readonly int FactionHsvId  = Shader.PropertyToID("_TWB_FactionHSV");
        private static readonly int FactionMaskId = Shader.PropertyToID("_TWB_FactionMask");
        private static readonly int HueBandId     = Shader.PropertyToID("_TWB_FactionHueBand");
        private static readonly int SwapId        = Shader.PropertyToID("_TWB_FactionSwap");

        /// <summary>The URP Lit copy that carries the recolour.</summary>
        public const string BuildingLitShaderName = "TheWaningBorder/BuildingLit";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private static Shader _buildingLit;
        private static bool _buildingLitMissing;

        // Materials reported as not converted on disk — once each.
        private static readonly HashSet<string> _reportedUnconverted = new HashSet<string>();

        /// <summary>
        /// Carry a material's player colour onto a material built from it (the
        /// battle-damage and level-up dissolve materials copy the albedo and
        /// would otherwise render the authored blue).
        /// </summary>
        public static void CopyFactionProperties(Material from, Material to)
        {
            if (from == null || to == null) return;
            if (!from.HasProperty(FactionHsvId) || !to.HasProperty(FactionHsvId)) return;
            to.SetVector(FactionHsvId, from.GetVector(FactionHsvId));
            if (from.HasProperty(FactionMaskId) && to.HasProperty(FactionMaskId))
                to.SetTexture(FactionMaskId, from.GetTexture(FactionMaskId));
        }

        /// <summary>
        /// The property names a building material may keep its albedo in, in
        /// the order they are tried: URP-Lit / BuildingLit, the legacy name,
        /// and Synty's Generic_Basic shader graph (the raw kit pieces a
        /// hand-assembled prefab keeps). Every reader of "the albedo" — the
        /// hue swap, the level-up dissolve, the placement ghost's blanking —
        /// goes through this list, so a new shader is one entry here.
        /// </summary>
        public static readonly string[] AlbedoProperties = { "_BaseMap", "_MainTex", "_Albedo_Map" };

        /// <summary>
        /// The albedo texture of <paramref name="mat"/> and its tiling /
        /// offset, from whichever of <see cref="AlbedoProperties"/> it
        /// exposes and has set; null (and identity ST) when it has none.
        /// </summary>
        public static Texture AlbedoOf(Material mat, out Vector4 st)
        {
            st = new Vector4(1f, 1f, 0f, 0f);
            var tex = FirstTexture(mat, AlbedoProperties, out string prop);
            if (tex != null && mat.HasProperty(prop + "_ST")) st = mat.GetVector(prop + "_ST");
            return tex;
        }

        /// <summary>Normal-map slots: URP-Lit / BuildingLit, then Synty's
        /// graph (whose strength is `_Normal_Amount`).</summary>
        static readonly string[] NormalProperties = { "_BumpMap", "_Normal_Map" };
        static readonly string[] NormalScaleProperties = { "_BumpScale", "_Normal_Amount" };

        /// <summary>The normal map of <paramref name="mat"/> and its strength
        /// (1 when the shader has no strength), or null.</summary>
        public static Texture NormalMapOf(Material mat, out float scale)
        {
            scale = 1f;
            var tex = FirstTexture(mat, NormalProperties, out string prop);
            if (tex == null) return null;
            int i = System.Array.IndexOf(NormalProperties, prop);
            if (mat.HasProperty(NormalScaleProperties[i])) scale = mat.GetFloat(NormalScaleProperties[i]);
            return tex;
        }

        /// <summary>The emission of <paramref name="mat"/>: its colour (black
        /// when it emits nothing — Synty's graph gates the map behind
        /// `_Enable_Emission`) and, when it has one, its map.</summary>
        public static Texture EmissionOf(Material mat, out Color color)
        {
            color = Color.black;
            if (mat == null) return null;
            if (mat.HasProperty("_EmissionColor")) color = mat.GetColor("_EmissionColor");
            else if (mat.HasProperty("_Emission_Color")) color = mat.GetColor("_Emission_Color");
            if (mat.HasProperty("_Enable_Emission") && mat.GetFloat("_Enable_Emission") < 0.5f) color = Color.black;
            if (mat.HasProperty("_EmissionMap")) return mat.GetTexture("_EmissionMap");
            if (mat.HasProperty("_Emission_Map")) return mat.GetTexture("_Emission_Map");
            return null;
        }

        /// <summary>The occlusion map of <paramref name="mat"/>, or null.</summary>
        public static Texture OcclusionOf(Material mat)
            => mat != null && mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;

        /// <summary>The first of <paramref name="names"/> the material exposes
        /// AND has a texture in; <paramref name="found"/> is that name.</summary>
        static Texture FirstTexture(Material mat, string[] names, out string found)
        {
            found = null;
            if (mat == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                if (!mat.HasProperty(names[i])) continue;
                var tex = mat.GetTexture(names[i]);
                if (tex == null) continue;
                found = names[i];
                return tex;
            }
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE building faction-color rule. Every path that produces or
        /// refreshes a building visual must funnel through here — spawn,
        /// culture/level variant switch, prefab upgrade swap, age-up refresh.
        /// Five sub-rules are applied per material, in priority order:
        ///   0. _DetailMask holds a *playercolormask* texture → albedo recolored under the mask
        ///   1. MATERIAL named *playercolor* → solid faction colour (albedo whited out)
        ///   2. GameObject named *roof*   → solid dark slate (albedo whited out; §4)
        ///   3. GameObject named *stripe* → faction tint over the authored albedo
        ///   4. otherwise                 → atlas pixel swap (marker hue → faction),
        ///      unless the model has a rule-0/1 part, which is then the ONLY
        ///      part that takes the colour,
        ///      falling back to flat _BaseColor replacement for untextured materials
        /// plus _StripeColor on every material that exposes it.
        /// </summary>
        public static void Apply(GameObject go, Color factionColor)
        {
            if (!Enabled || go == null) return;

            PushGlobals();
            Color.RGBToHSV(factionColor, out float facH, out float facS, out float facV);
            var hsv = new Vector4(facH, facS, facV, 0f);

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
            // bug. Color the whole hierarchy up front; it is property writes, not pixels.
            var renderers = go.GetComponentsInChildren<Renderer>(true);

            // A model that NAMES its player-colour part (a painted mask, or a
            // material called *playercolor*) is coloured there and nowhere
            // else: the hue swap is a guess for atlas art with no such slot,
            // and on hand-textured parts (the Alanthor House's walls, door,
            // lanterns) it caught any bluish pixel. Only the named part adapts.
            bool namedSlot = HasNamedPlayerColorSlot(renderers);

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

                    // A textured part with a painted player-colour mask (the
                    // Hut's roof tiles). Before every other rule: the mask says
                    // exactly where the colour goes, so nothing may guess.
                    if (TryApplyMaskedColor(mat, hsv)) continue;

                    // The rules below paint the part themselves, so the shader
                    // must not recolour on top of them (ModeOff).
                    // An artist-authored player-colour slot: the model names the
                    // MATERIAL, so the colour is exact and needs no atlas marker.
                    // Checked before the roof rule so a roof mesh can still carry
                    // a player-colour trim. Instances read "Playercolor (Instance)".
                    if (IsPlayerColorMaterial(mat)) { SetMode(mat, hsv, ModeOff); PaintSolid(mat, factionColor); continue; }

                    // A PLAYER-TINT material takes the player's colour as a
                    // multiply, so its texture survives. Two names qualify:
                    // "*_Player" — a luminance-only (white) copy of a Synty
                    // texture authored for exactly this (Castle_Roof_Player,
                    // Flag_Symbol_Player in Civs/Alanthor/Buildings/Art/,
                    // 2026-11-01), and the castle kit's own teal slate
                    // "Castle_Roof_01" (2026-10-02; supersedes "roofs are dark
                    // slate" for the castle set — docs/Design/Art_Direction.md
                    // §4), which still tints but shifts hue (teal x Red = purple) -
                    // no building prefab should keep it: `Waning Border > Art >
                    // Roofs > Castle_Roof_01 -> Castle_Roof_Player` swaps every
                    // slot (RoofPlayerMaterialPass, 2026-11-02). By MATERIAL,
                    // so the gallery's timber, the roof's finial and the flag's
                    // pole are untouched.
                    if (IsPlayerTintMaterial(mat)) { SetMode(mat, hsv, ModeOff); SetTint(mat, PlayerTint(factionColor)); continue; }

                    // Name-tagged parts are a flat solid faction color.
                    if (isRoof)   { SetMode(mat, hsv, ModeOff); PaintSolid(mat, RoofSlate);    continue; }
                    if (isStripe) { SetMode(mat, hsv, ModeOff); SetTint(mat, factionColor);    continue; }

                    if (namedSlot) { SetMode(mat, hsv, ModeOff); continue; }
                    if (TryHueSwap(mat, hsv)) continue;
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

        /// <summary>Does any material under these renderers carry a rule-0 mask
        /// or a rule-1 player-colour name?</summary>
        private static bool HasNamedPlayerColorSlot(Renderer[] renderers)
        {
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (IsPlayerColorMaterial(mat)) return true;
                    if (mat.HasProperty(MaskProp) && mat.GetTexture(MaskProp) is Texture2D mask && mask != null
                        && mask.name.IndexOf(MaskNameTag, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>A material the player colour multiplies into: an authored
        /// white "*_Player" copy, or the castle kit's slate (Castle_Roof_01).
        /// Instances read "... (Instance)", so this is a contains test.</summary>
        private static bool IsPlayerTintMaterial(Material mat)
            => mat.name.IndexOf("_Player", System.StringComparison.OrdinalIgnoreCase) >= 0
            || mat.name.IndexOf("Castle_Roof", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The faction colour as a tinted part wears it: saturation
        /// pulled <see cref="PlayerTintSaturate"/> of the way to full, value x
        /// <see cref="PlayerTintValue"/>, hue untouched. Public so the wall art
        /// (WallModuleArt.ApplyOwnerColor, property blocks) paints its roofs
        /// the same colour as every other building's.</summary>
        public static Color PlayerTint(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = 1f - (1f - s) * (1f - Mathf.Clamp01(PlayerTintSaturate));
            v *= Mathf.Max(0f, PlayerTintValue);
            var o = Color.HSVToRGB(h, s, v);
            o.a = c.a;
            return o;
        }

        /// <summary>A material the artist named as the player-colour slot (e.g. the Hut's "Playercolor").</summary>
        private static bool IsPlayerColorMaterial(Material mat)
            => mat.name.IndexOf("playercolor", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Roof rule — blank the albedo so the solid colour reads flat.</summary>
        private static void PaintSolid(Material mat, Color solid)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            // BuildingDamage samples the albedo only when _UseBaseMap is on; a
            // roof re-colored after the damage swap would otherwise ignore the
            // white map we just assigned.
            if (mat.HasProperty("_UseBaseMap")) mat.SetFloat("_UseBaseMap", 1f);
            SetTint(mat, solid);
        }

        /// <summary>Stripe rule — tint only, authored albedo detail survives.</summary>
        private static void SetTint(Material mat, Color factionColor)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", factionColor);
            else if (mat.HasProperty("_Color")) mat.color = factionColor;
        }

        // ──────────────────────────────────────────────────────────────────
        // MASKED PATH (a per-part texture set with a painted colour mask)
        // ──────────────────────────────────────────────────────────────────

        // URP Lit has no player-colour slot, so the artist's mask rides in
        // _DetailMask: Lit only samples it when a detail albedo is also set,
        // which a masked material never has. The texture NAME is the opt-in — a
        // detail mask that is not a player-colour mask (the GatherersHut's
        // MULX2 detail setup) must keep going down the other rules. The shader
        // reads it from _TWB_FactionMask, on the albedo's UVs.
        private const string MaskProp = "_DetailMask";
        private const string MaskNameTag = "playercolormask";

        /// <summary>
        /// Rule 0. Tint the albedo wherever the mask is white; the mask's grey
        /// levels blend. Returns true when the material carries a player-colour
        /// mask at all, so the hue-guessing swap never runs over a masked set.
        /// </summary>
        private static bool TryApplyMaskedColor(Material mat, Vector4 hsv)
        {
            if (!mat.HasProperty(MaskProp) || !mat.HasProperty("_BaseMap")) return false;
            var mask = mat.GetTexture(MaskProp) as Texture2D;
            if (mask == null ||
                mask.name.IndexOf(MaskNameTag, System.StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (mat.GetTexture("_BaseMap") == null) return true;
            if (!EnsureRecolorShader(mat)) return true;
            mat.SetTexture(FactionMaskId, mask);
            SetMode(mat, hsv, ModeMask);
            return true;
        }

        // ──────────────────────────────────────────────────────────────────
        // ATLAS HUE SWAP (preferred — used by hand-authored prefabs)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rule 4. A textured material: the shader swaps the marker hue band to
        /// the faction colour, keeping each pixel's value. False for an
        /// untextured material (the flat-colour fallback handles those).
        /// </summary>
        private static bool TryHueSwap(Material mat, Vector4 hsv)
        {
            Texture albedo = AlbedoOf(mat, out _);
            if (albedo == null || albedo == Texture2D.whiteTexture) { SetMode(mat, hsv, ModeOff); return false; }
            if (!EnsureRecolorShader(mat)) return false;
            SetMode(mat, hsv, ModeHueSwap);
            return true;
        }

        private static void SetMode(Material mat, Vector4 hsv, float mode)
        {
            if (!mat.HasProperty(FactionHsvId)) return;
            hsv.w = mode;
            mat.SetVector(FactionHsvId, hsv);
        }

        /// <summary>
        /// True when the material's shader carries the recolour. A stock URP
        /// Lit material (not converted on disk) is switched to BuildingLit — it
        /// has every Lit property and keyword, so nothing else changes — and
        /// named once in the log so the asset can be converted. Any other
        /// shader cannot be recoloured and is named too.
        /// </summary>
        private static bool EnsureRecolorShader(Material mat)
        {
            if (mat.HasProperty(FactionHsvId)) return true;
            var shader = mat.shader;
            if (shader != null && shader.name == UrpLitShaderName && BuildingLit != null)
            {
                mat.shader = BuildingLit;
                if (_reportedUnconverted.Add(mat.name))
                    Debug.LogWarning($"[BuildingFactionColorMarker] Material '{mat.name}' is URP Lit, not " +
                        $"{BuildingLitShaderName}; switched at runtime. Run Waning Border > Buildings > " +
                        "Convert Materials To BuildingLit so its variants ship with the build.");
                return mat.HasProperty(FactionHsvId);
            }
            if (_reportedUnconverted.Add(mat.name))
                Debug.LogWarning($"[BuildingFactionColorMarker] Material '{mat.name}' uses " +
                    $"'{(shader != null ? shader.name : "null")}', which has no player colour — team colour skipped.");
            return false;
        }

        private static Shader BuildingLit
        {
            get
            {
                if (_buildingLit != null || _buildingLitMissing) return _buildingLit;
                _buildingLit = Shader.Find(BuildingLitShaderName);
                if (_buildingLit == null)
                {
                    _buildingLitMissing = true;
                    Debug.LogError($"[BuildingFactionColorMarker] Shader '{BuildingLitShaderName}' not found — " +
                        "no building can take team colour.");
                }
                return _buildingLit;
            }
        }

        /// <summary>The hue band and the interim swap levels, as the shader's
        /// globals. Cheap; set on every Apply so a tweak to the statics lands.</summary>
        private static void PushGlobals()
        {
            Shader.SetGlobalVector(HueBandId, new Vector4(HueMin / 360f, HueMax / 360f, MinSaturation, 0f));
            Shader.SetGlobalVector(SwapId, new Vector4(InterimSwapSaturation, InterimSwapValue, 0f, 0f));
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
    }
}
