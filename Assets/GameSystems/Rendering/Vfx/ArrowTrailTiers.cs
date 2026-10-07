// ArrowTrailTiers.cs
// How an arrow looks in flight is the READOUT for a faction's arrow-tip
// research (docs/Design/Vfx_Assignments.md). For ARROWS (ApplyArrow):
//
//   (no research)          nothing
//   StoneTippedArrows      white trail
//   IronTippedArrows       blue trail
//   VeilstoneTippedArrows  no trail — the arrowhead is replaced by the Lana
//                          dark-magic projectile effect, scaled small
//   ShardTippedArrows      same, with the Lana electric projectile (Veilsteel)
//
// Their hits follow the same ladder (UnitCombatVfx): nothing below Veilstone.
// Ballista bolts are not arrows and keep the original trail ladder (Apply):
// faint grey / grey / blue emissive / golden emissive.
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

        private static ArrowTrailTiersConfig _cfg;
        private static ArrowTrailTiersConfig Cfg
            => _cfg != null ? _cfg : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<ArrowTrailTiersConfig>());

        /// <summary>Stone / Iron ribbon width multiplier (1 when the config is missing).</summary>
        private static float RibbonWidth(ArrowTrailTier tier)
        {
            var c = Cfg;
            if (c == null) return 1f;
            float w = tier == ArrowTrailTier.Stone ? c.stoneTrailWidth : c.ironTrailWidth;
            return w > 0f ? w : 1f;
        }

        /// <summary>World scale of a tier's arrow HIT effect (UnitCombatVfx);
        /// 0 for tiers with no hit.</summary>
        public static float HitScale(ArrowTrailTier tier)
        {
            var look = Cfg?.LookFor(tier);
            return look != null ? look.hitScale : 0f;
        }

        private const string TipName = "Tip";
        private const string TierTipName = "TierTip";

        /// <summary>
        /// Dress one ARROW for a tier: its trail (white / blue at Stone /
        /// Iron) or, at Veilstone and Veilsteel, no trail and the projectile
        /// effect in place of the arrowhead. Pooled arrows are re-dressed on
        /// every spawn, so a lower tier puts the plain head back.
        /// </summary>
        public static void ApplyArrow(GameObject arrow, TrailRenderer trail, ArrowTrailTier tier)
        {
            bool fxTip = tier >= ArrowTrailTier.Veilstone;
            if (trail != null)
            {
                if (tier == ArrowTrailTier.None || fxTip) { trail.Clear(); trail.enabled = false; }
                else
                {
                    trail.enabled = true;
                    trail.sharedMaterial = MaterialFor(tier);
                    trail.colorGradient = ArrowGradient(tier);
                    trail.time = tier == ArrowTrailTier.Stone ? 0.18f : 0.25f;
                    trail.startWidth = (tier == ArrowTrailTier.Stone ? 0.05f : 0.06f) * RibbonWidth(tier);
                    trail.endWidth = 0f;
                    trail.Clear();
                }
            }
            if (arrow != null) DressTip(arrow.transform, fxTip ? tier : ArrowTrailTier.None);
        }

        private static void DressTip(Transform arrow, ArrowTrailTier tier)
        {
            var head = arrow.Find(TipName);
            var existing = arrow.Find(TierTipName);
            var look = Cfg?.LookFor(tier);
            var prefab = look?.tipPrefab;

            if (head != null && head.TryGetComponent<Renderer>(out var headR)) headR.enabled = prefab == null;

            // Keep a matching tip copied from the CURRENT prefab; a different
            // tier, or a prefab edited since (Inspector, live), is rebuilt.
            if (existing != null)
            {
                var tag = existing.GetComponent<ArrowTipTier>();
                if (prefab != null && tag != null && tag.Tier == tier && tag.PrefabVersion == PrefabEdits)
                {
                    PlaceTip(arrow, existing, look);
                    foreach (var ps in existing.GetComponentsInChildren<ParticleSystem>(true))
                    { ps.Clear(true); ps.Play(true); }    // a recycled arrow must not streak from its last flight
                    foreach (var tr in existing.GetComponentsInChildren<TrailRenderer>(true))
                        tr.Clear();
                    return;
                }
                Object.Destroy(existing.gameObject);
            }
            if (prefab == null) return;

            // Everything about the tip's look is the prefab's own Particle
            // System settings; only its loop and hierarchy scaling are forced,
            // so it keeps running for the whole flight at the arrow's scale.
            var fx = Object.Instantiate(prefab, arrow);
            fx.name = TierTipName;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            var tipTag = fx.AddComponent<ArrowTipTier>();
            tipTag.Tier = tier;
            tipTag.PrefabVersion = PrefabEdits;
            PlaceTip(arrow, fx.transform, look);
        }

        /// <summary>At the arrowhead, at the configured world size (re-read
        /// every spawn, so the panel's tip slider shows on the next shot).</summary>
        private static void PlaceTip(Transform arrow, Transform fx, ArrowTrailTiersConfig.TipLook look)
        {
            var head = arrow.Find(TipName);
            fx.localPosition = head != null ? head.localPosition : new Vector3(0f, 0f, 0.25f);
            fx.localRotation = Quaternion.Euler(0f, look.tipYaw, 0f);
            var lossy = arrow.lossyScale;
            float k = look.tipScale;
            fx.localScale = new Vector3(k / Mathf.Max(1e-4f, lossy.x), k / Mathf.Max(1e-4f, lossy.y), k / Mathf.Max(1e-4f, lossy.z));
        }

        /// <summary>
        /// Counts edits to any asset while the Editor runs, so a tip copied
        /// before an Inspector edit to its prefab is rebuilt on the next arrow
        /// (live tuning). Always 0 in a player build — prefabs never change there.
        /// </summary>
        private static int PrefabEdits
        {
            get
            {
#if UNITY_EDITOR
                if (!_watching)
                {
                    UnityEditor.ObjectChangeEvents.changesPublished += (ref UnityEditor.ObjectChangeEventStream stream) => _edits++;
                    _watching = true;
                }
#endif
                return _edits;
            }
        }
        private static int _edits;
#if UNITY_EDITOR
        private static bool _watching;
#endif

        /// <summary>Longest an arrow may linger after landing (a safety cap on
        /// a mis-authored trail time / particle lifetime).</summary>
        private const float MaxFadeSeconds = 4f;

        /// <summary>
        /// The arrow has landed: stop every trail and particle from emitting so
        /// what is already drawn fades out over its own time / lifetime, and
        /// hide the shaft and head meshes. Returns how long to keep the visual
        /// before recycling it — the longest trail time or particle lifetime
        /// on it, 0 when there is nothing to fade. The visual stays out of the
        /// pool meanwhile, so a fresh arrow never inherits a fading one.
        /// </summary>
        public static float BeginFade(GameObject arrow)
        {
            if (arrow == null) return 0f;

            // The tier tip (Veilstone / Veilsteel projectile effect) IS the
            // arrowhead, so it goes with the arrow: cleared and switched off
            // now, never left to fade. Its looping particles (and their world-
            // space sub-trails) otherwise kept glowing at the landing point for
            // their full lifetime — seconds after the arrow itself was gone.
            // ResetAfterFade switches it back on for the next flight.
            var tip = arrow.transform.Find(TierTipName);
            if (tip != null)
            {
                foreach (var ps in tip.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var tr in tip.GetComponentsInChildren<TrailRenderer>(true))
                { tr.emitting = false; tr.Clear(); }
                tip.gameObject.SetActive(false);
            }

            float linger = 0f;
            foreach (var t in arrow.GetComponentsInChildren<TrailRenderer>(false))
            {
                if (!t.enabled) continue;
                t.emitting = false;
                linger = Mathf.Max(linger, t.time);
            }
            foreach (var ps in arrow.GetComponentsInChildren<ParticleSystem>(false))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                if (ps.particleCount > 0) linger = Mathf.Max(linger, ps.main.startLifetime.constantMax);
            }
            if (linger <= 0f) return 0f;
            foreach (var r in arrow.GetComponentsInChildren<MeshRenderer>(false)) r.enabled = false;
            return Mathf.Min(linger, MaxFadeSeconds);
        }

        /// <summary>Undo BeginFade on a recycled arrow before it is dressed
        /// again: the shaft and head meshes back on (DressTip then hides the
        /// head where a tip effect replaces it).</summary>
        public static void ResetAfterFade(GameObject arrow)
        {
            if (arrow == null) return;
            foreach (var r in arrow.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = true;
            var tip = arrow.transform.Find(TierTipName);
            if (tip != null) tip.gameObject.SetActive(true);   // switched off by BeginFade
        }

        /// <summary>
        /// Hide an arrow's trail and tip particles for the frame it is
        /// (re)spawned on: a pooled arrow teleported back to the bow would
        /// otherwise draw one streak from its last impact point.
        /// </summary>
        public static void HideForOneFrame(GameObject arrow, TrailRenderer trail)
        {
            if (arrow == null) return;
            if (!arrow.TryGetComponent<ArrowSpawnHide>(out var hide)) hide = arrow.AddComponent<ArrowSpawnHide>();
            hide.Begin(trail);
        }

        private static Gradient ArrowGradient(ArrowTrailTier tier)
        {
            Color c; float head, mid;
            if (tier == ArrowTrailTier.Stone) { c = new Color(0.96f, 0.96f, 0.96f); head = 0.55f; mid = 0.2f; }
            else { c = new Color(0.36f, 0.62f, 1.00f); head = 0.8f; mid = 0.32f; }
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(head, 0f), new GradientAlphaKey(mid, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>
        /// Dress one BALLISTA BOLT's trail for a tier (the original ladder),
        /// or switch it off entirely at <see cref="ArrowTrailTier.None"/>.
        /// Arrows use <see cref="ApplyArrow"/>.
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
