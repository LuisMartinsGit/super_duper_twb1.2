using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="BuildingEffectSystem"/>. The asset is
    /// BuildingEffectSystem.asset, beside BuildingEffectSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    ///
    /// The two effects are Hovl Studio prefabs (Magic effects pack, Smoke
    /// effects). Their own material is on the built-in particle shader, which
    /// URP draws magenta, so each effect also names the URP material its
    /// particle renderers are switched to when it spawns.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Building Effect System",
                     fileName = "BuildingEffectSystem")]
    public sealed class BuildingEffectSystemConfig : ScriptableObject, IComponentConfig
    {
        // ── Construction dust ──────────────────────────────────────────

        /// <summary>A CONTINUOUS dust loop (Hovl Dust loop) at the foot of a
        /// site for as long as workers are raising it.</summary>
        public GameObject constructionDustPrefab;
        public Material constructionDustMaterial;
        /// <summary>Dust scale per metre of footprint half-width (the prefab
        /// is authored about 2 m across).</summary>
        public float constructionDustScalePerMetre;
        /// <summary>Seconds without construction progress before the dust
        /// stops (workers left, or ran out of resources).</summary>
        public float constructionDustIdleSeconds;
        /// <summary>Seconds a stopped dust loop keeps fading before it is destroyed.</summary>
        public float constructionDustFadeSeconds;

        // ── Damage smoke ───────────────────────────────────────────────

        /// <summary>A looping plume rising from a finished building below
        /// <see cref="damageSmokeStartHealth"/>.</summary>
        public GameObject damageSmokePrefab;
        public Material damageSmokeMaterial;
        /// <summary>Health fraction below which the plume starts.</summary>
        public float damageSmokeStartHealth;
        /// <summary>Health fraction above which a repaired building's plume
        /// stops (above the start value, so it does not flicker).</summary>
        public float damageSmokeStopHealth;
        /// <summary>Plume scale per metre of footprint half-width.</summary>
        public float damageSmokeScalePerMetre;
        /// <summary>Extra plume scale at zero health, on top of the base
        /// scale (a nearly-dead building smokes harder).</summary>
        public float damageSmokeExtraScaleAtZero;
        /// <summary>Seconds a stopped plume keeps fading before it is destroyed.</summary>
        public float damageSmokeFadeSeconds;
        /// <summary>How often building health is polled, seconds.</summary>
        public float damageSmokePollSeconds;
    }
}
