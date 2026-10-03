using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="UnitCombatVfx"/>. The asset is UnitCombatVfx.asset,
    /// beside UnitCombatVfx.cs. No field initialisers. The prefabs are the
    /// copies in Age0/Units/Vfx/ (docs/Design/Vfx_Assignments.md §1).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Unit Combat Vfx",
                     fileName = "UnitCombatVfx")]
    public sealed class UnitCombatVfxConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>A melee or ranged hit lands on a unit (Hovl Holy hit).</summary>
        public GameObject hitPrefab;
        /// <summary>A charging attacker's hit (Hovl Stones hit).</summary>
        public GameObject chargeHitPrefab;
        /// <summary>An arrow hit from a faction on Veilstone-tipped arrows
        /// (Lana Hit_dark_magic). Lower tiers' arrows show no hit.</summary>
        public GameObject arrowVeilstoneHitPrefab;
        /// <summary>An arrow hit on Veilsteel (Shard) -tipped arrows (Lana Hit_electric).</summary>
        public GameObject arrowVeilsteelHitPrefab;
        /// <summary>Looping while a unit moves faster than normal (Lana Fog_speedFast).</summary>
        public GameObject speedUpPrefab;
        /// <summary>Looping while a unit is slowed (Lana Fog_speedSlow).</summary>
        public GameObject slowDownPrefab;
        /// <summary>Looping while a unit deals bonus damage (Lana Fog_electric).</summary>
        public GameObject attackUpPrefab;

        /// <summary>Effect scale per metre of unit height.</summary>
        public float scalePerMetre;
        /// <summary>Height up the unit a hit plays at, as a fraction of its height.</summary>
        public float hitHeightFraction;
        /// <summary>A unit shows at most one hit effect per this many seconds.</summary>
        public float hitCooldownSeconds;
        /// <summary>Most hit effects spawned in one frame (a big melee stays readable).</summary>
        public int maxHitsPerFrame;
        /// <summary>How often buff states are re-read, seconds.</summary>
        public float buffPollSeconds;
        /// <summary>A multiplier counts as a buff only past 1 ± this.</summary>
        public float buffThreshold;
        public float fadeSeconds;
    }
}
