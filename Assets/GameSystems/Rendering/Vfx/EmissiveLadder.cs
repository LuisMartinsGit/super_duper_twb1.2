// EmissiveLadder.cs
// The HDR emissive ladder from docs/Design/Art_Direction.md §3.4 — the ONLY
// surfaces that cross the bloom threshold, and how bright each one is.
// Every runtime visual that makes something glow reads its colour from here,
// so the map's light sources stay one ranked set instead of a dozen
// `color * 1.6f` literals that drift apart.
//
// Values are HDR (> 1). The bloom threshold in DayNightCycle.asset is what
// decides which rungs bloom; the ladder decides their order.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class EmissiveLadder
    {
        static Color Hdr(int hex, float intensity)
            => new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f)
               * intensity;

        /// <summary>Building windows — warm, soft bloom.</summary>
        public static readonly Color Window = Hdr(0xFFB05A, 2.5f);

        /// <summary>Veilstone outcropping crystals — cyan. The resource, never the curse.</summary>
        public static readonly Color Veilstone = Hdr(0x5FD8E8, 3.0f);

        /// <summary>Curse crystals (well, pockets) — purple, the strongest static glow.</summary>
        public static readonly Color Curse = Hdr(0xB14BFF, 4.0f);

        /// <summary>Curse pool — sickly green, faint.</summary>
        public static readonly Color CursePool = Hdr(0x7FCB3A, 2.0f);

        /// <summary>Ember cracks on burning ground.</summary>
        public static readonly Color Ember = Hdr(0xFF6A2A, 3.0f);

        /// <summary>Multiplier applied to a faction's glow colour for lanterns
        /// and sconces (FactionColors.GetGlow × this).</summary>
        public const float LanternIntensity = 3.0f;

        /// <summary>Owned territory border line multiplier (glow colour × this).</summary>
        public const float BorderLineIntensity = 4.0f;

        /// <summary>
        /// The Shatter Stone gem shader (SS_Gemstone) that both the veilstone
        /// outcropping and the curse well/pockets are built from exposes an
        /// HDR _EmissionColor masked by the gem's own emission map, plus an
        /// _AlbedoHueShift in turns (0..1). Cyan is the authored gem; the
        /// curse is the same prefab shifted a quarter-turn to purple (§6.1:
        /// purple is the curse, cyan is veilstone — never mixed on one object).
        /// </summary>
        public const float CurseHueShift = 0.25f;

        /// <summary>
        /// Set the emissive colour (and optionally the albedo hue shift) on
        /// every renderer under <paramref name="root"/>. Instanced materials,
        /// so the shared asset is untouched.
        /// </summary>
        public static void ApplyCrystalGlow(GameObject root, Color hdrEmission, float albedoHueShift = 0f)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", hdrEmission);
                    }
                    if (albedoHueShift != 0f && mat.HasProperty("_AlbedoHueShift"))
                        mat.SetFloat("_AlbedoHueShift", albedoHueShift);
                }
            }
        }
    }
}
