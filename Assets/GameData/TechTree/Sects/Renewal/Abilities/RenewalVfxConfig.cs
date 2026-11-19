using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="RenewalVfx"/>. The asset is RenewalVfx.asset,
    /// beside RenewalVfx.cs. No field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Renewal Vfx",
                     fileName = "RenewalVfx")]
    public sealed class RenewalVfxConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Looping on a unit while Hands of Plenty III's regen tail runs.</summary>
        public GameObject regenTailPrefab;
        /// <summary>Looping on a unit while Second Wind's death ward holds it up.</summary>
        public GameObject deathWardPrefab;
        /// <summary>Looping on a standing Healing Ward for its whole life
        /// (HealingWard_Aura.prefab).</summary>
        public GameObject healingWardAuraPrefab;
        /// <summary>Unit effect scale per metre of unit height.</summary>
        public float unitScalePerMetre;
        /// <summary>Healing Ward aura: metres of radius the prefab is authored
        /// at; it is scaled to the Healing Ward card's Radius.</summary>
        public float healingWardAuthoredRadius;
        public float fadeSeconds;
        public float pollSeconds;
    }
}
