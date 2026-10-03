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
        /// <summary>Looping on a deployed Field Hospital for its whole life.</summary>
        public GameObject fieldHospitalAuraPrefab;
        /// <summary>Unit effect scale per metre of unit height.</summary>
        public float unitScalePerMetre;
        /// <summary>Field Hospital aura: metres of heal radius the prefab is
        /// authored at (the hospital heals out to 15 m).</summary>
        public float hospitalAuthoredRadius;
        public float hospitalHealRadius;
        public float fadeSeconds;
        public float pollSeconds;
    }
}
