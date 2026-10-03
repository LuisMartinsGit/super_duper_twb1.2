using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="UnitHealVfx"/>. The asset is UnitHealVfx.asset,
    /// beside UnitHealVfx.cs. No field initialisers: the values live in the
    /// asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Unit Heal Vfx",
                     fileName = "UnitHealVfx")]
    public sealed class UnitHealVfxConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Played on a unit each time it is healed (Lana Studio
        /// Regeneration_health; the copy lives in Age0/Units/Vfx/).</summary>
        public GameObject healPrefab;
        /// <summary>Effect scale per metre of the unit's height.</summary>
        public float scalePerMetre;
        /// <summary>How often unit health is sampled, seconds.</summary>
        public float pollSeconds;
        /// <summary>A unit replays the effect at most this often, seconds —
        /// a heal that ticks every second reads as a steady pulse, not a strobe.</summary>
        public float cooldownSeconds;
        /// <summary>Smallest heal shown, as a fraction of max HP per sample.
        /// Keeps the +1 HP/s rank regen from pulsing on every veteran.</summary>
        public float minHealFraction;
    }
}
