// ArrowTrailTiers.cs
// The flight trail an arrow leaves is the READOUT for a faction's arrow-tip
// research. Four techs, four looks, and nothing at all before the first one:
//
//   (no research)          no trail
//   StoneTippedArrows      faint grey
//   IronTippedArrows       grey
//   VeilstoneTippedArrows  blue, emissive
//   ShardTippedArrows      golden, emissive   (the Veilsteel tier)
//
// Every arrow used to leave the same white streak, so a fully-upgraded army
// looked exactly like a starting one — the ladder was invisible on the field,
// which is the one place it matters. Making the base arrow leave NOTHING is
// what gives the first upgrade something to be.
//
// The last tech is `ShardTippedArrows` and not "VeilsteelTippedArrows": that
// is its id everywhere (its SO reads "Arrow tier 4 … Needs Veilsteel"), and
// Veilsteel is what it costs. Same tier, older name.
//
// Costs per arrow: one dictionary hit at spawn, and only when the faction's
// tier is not already cached. Materials are SHARED — assigning through
// `.material` would instantiate one per arrow, which is why every write here
// goes through `sharedMaterial` and the per-instance colour rides the
// TrailRenderer's own gradient instead.

using UnityEngine;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Rendering
{
    /// <summary>Which arrow-tip tech the firing faction has reached.</summary>
    public enum ArrowTrailTier
    {
        None = 0,
        Stone = 1,
        Iron = 2,
        Veilstone = 3,
        Veilsteel = 4,
    }

    public static class ArrowTrailTiers
    {
        /// <summary>Highest first — the first one researched wins.</summary>
        private static readonly (string Tech, ArrowTrailTier Tier)[] Ladder =
        {
            ("ShardTippedArrows",     ArrowTrailTier.Veilsteel),
            ("VeilstoneTippedArrows", ArrowTrailTier.Veilstone),
            ("IronTippedArrows",      ArrowTrailTier.Iron),
            ("StoneTippedArrows",     ArrowTrailTier.Stone),
        };

        // Faction is Blue=0 .. White=7. -1 = not resolved yet.
        private const int Factions = 8;
        private static readonly int[] _cached = NewCache();
        private static bool _subscribed;

        private static int[] NewCache()
        {
            var a = new int[Factions];
            for (int i = 0; i < a.Length; i++) a[i] = -1;
            return a;
        }

        /// <summary>
        /// The tier a faction currently shoots at. Cached, because this is
        /// asked once per arrow spawned and research changes a handful of
        /// times a match.
        /// </summary>
        public static ArrowTrailTier Of(Faction faction)
        {
            int idx = (int)faction;
            if (idx < 0 || idx >= Factions) return ArrowTrailTier.None;

            var research = FactionResearchState.Instance;
            if (research == null) return ArrowTrailTier.None;

            // Invalidate on the tech that CAUSES the change rather than
            // re-reading every frame — one event, no polling.
            if (!_subscribed)
            {
                research.OnTechCompleted += Forget;
                _subscribed = true;
            }

            if (_cached[idx] >= 0) return (ArrowTrailTier)_cached[idx];

            var tier = ArrowTrailTier.None;
            for (int i = 0; i < Ladder.Length; i++)
            {
                if (!research.HasResearched(faction, Ladder[i].Tech)) continue;
                tier = Ladder[i].Tier;
                break;
            }

            _cached[idx] = (int)tier;
            return tier;
        }

        private static void Forget(Faction faction, string techId)
        {
            int idx = (int)faction;
            if (idx >= 0 && idx < Factions) _cached[idx] = -1;
        }

        /// <summary>
        /// Drop every cached tier and the subscription with it. A new match
        /// gets a new FactionResearchState, so a tier cached from the last one
        /// would otherwise have an upgraded army firing on turn one.
        /// </summary>
        public static void Reset()
        {
            for (int i = 0; i < _cached.Length; i++) _cached[i] = -1;
            _subscribed = false;
        }

        // ── the looks ───────────────────────────────────────────────────

        private static readonly Material[] _mats = new Material[5];

        /// <summary>
        /// Dress one arrow's trail for a tier, or switch it off entirely at
        /// <see cref="ArrowTrailTier.None"/>.
        /// </summary>
        public static void Apply(TrailRenderer trail, ArrowTrailTier tier)
        {
            if (trail == null) return;

            if (tier == ArrowTrailTier.None)
            {
                trail.Clear();
                trail.enabled = false;
                return;
            }

            trail.enabled = true;
            trail.sharedMaterial = MaterialFor(tier);
            trail.colorGradient = GradientFor(tier);

            // Better tips fly brighter AND further: length and width climb
            // with the tier so the upgrade reads at a glance from a camera
            // that is never close enough to see an arrowhead.
            switch (tier)
            {
                case ArrowTrailTier.Stone:
                    trail.time = 0.18f; trail.startWidth = 0.05f; break;
                case ArrowTrailTier.Iron:
                    trail.time = 0.25f; trail.startWidth = 0.06f; break;
                case ArrowTrailTier.Veilstone:
                    trail.time = 0.34f; trail.startWidth = 0.08f; break;
                default:
                    trail.time = 0.42f; trail.startWidth = 0.10f; break;
            }
            trail.endWidth = 0f;
            trail.Clear();
        }

        private static Gradient GradientFor(ArrowTrailTier tier)
        {
            // Colour lives on the gradient, not the material, so the four
            // tiers can share two materials and no arrow allocates one.
            Color c;
            float head, mid;
            switch (tier)
            {
                case ArrowTrailTier.Stone:
                    c = new Color(0.62f, 0.62f, 0.60f); head = 0.30f; mid = 0.12f; break;
                case ArrowTrailTier.Iron:
                    c = new Color(0.74f, 0.76f, 0.80f); head = 0.65f; mid = 0.28f; break;
                case ArrowTrailTier.Veilstone:
                    c = new Color(0.34f, 0.62f, 1.00f); head = 0.95f; mid = 0.45f; break;
                default:
                    c = new Color(1.00f, 0.80f, 0.32f); head = 1.00f; mid = 0.50f; break;
            }

            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[]
                {
                    new GradientAlphaKey(head, 0f),
                    new GradientAlphaKey(mid, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        private static Material MaterialFor(ArrowTrailTier tier)
        {
            int i = (int)tier;
            if (_mats[i] != null) return _mats[i];

            // Same stripping-safe shader chain the rest of the procedural VFX
            // use — a bare unreferenced shader is dropped from player builds.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);

            bool emissive = tier >= ArrowTrailTier.Veilstone;
            if (emissive)
            {
                // Additive is what makes "emissive" read without an HDR
                // gradient: the streak adds to whatever is behind it, so it
                // blooms over dark ground instead of merely being coloured.
                // Same recipe as BuildingLevelUpEffect's sparks.
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3100;
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2);
                mat.EnableKeyword("_ALPHABLEND_ON");

                // Push the tint past 1 so bloom has something to catch. The
                // gradient stays LDR — Color keys clamp — so the overbright
                // has to come from the material.
                var hdr = tier == ArrowTrailTier.Veilstone
                    ? new Color(0.9f, 1.6f, 2.6f)
                    : new Color(2.6f, 1.9f, 0.8f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdr);
                else mat.color = hdr;
            }

            _mats[i] = mat;
            return mat;
        }
    }
}
