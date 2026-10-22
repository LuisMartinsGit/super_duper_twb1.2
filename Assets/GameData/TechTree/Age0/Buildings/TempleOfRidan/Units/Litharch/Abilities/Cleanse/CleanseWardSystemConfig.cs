using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// The Cleansing Ward's numbers (Territory_Claims.md §6.9.7). The asset is
    /// CleanseWardSystem.asset, beside CleanseWardSystem.cs. No field
    /// initialisers: the values live in the asset and nowhere else. The
    /// circle's radii and growth are the Cleanse CARD's (Cleanse.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Border/CleanseWardSystem",
                     fileName = "CleanseWardSystem")]
    public sealed class CleanseWardSystemConfig : ScriptableObject, IComponentConfig
    {
        static CleanseWardSystemConfig _i;
        public static CleanseWardSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<CleanseWardSystemConfig>());

        /// <summary>Seconds between pulses: the aura burst and the ground
        /// clearing happen together on each one.</summary>
        public float pulseSeconds;

        // ── How many, how close (2026-10-22, the Field Hospital's rule) ──
        /// <summary>Most wards a team (a faction and its allies) may have
        /// standing at once; a cast past the cap is refused (0 = no cap).</summary>
        public int capPerTeam;
        /// <summary>Least distance (m) between two of the team's wards; a cast
        /// closer than this to one is refused.</summary>
        public float minSpacingMetres;

        // ── The ward as a building ──
        public int maxHealth;
        public float bodyRadius;
        public float sight;
        public int defenseMelee;
        public int defenseRanged;
        public int defenseSiege;
        public int defenseMagic;

        // ── Presentation ──
        /// <summary>The ward's model (Waning Border > Art > Build Cleansing Ward Prefab).</summary>
        public GameObject wardPrefab;
        /// <summary>The one-shot aura played on every pulse, scaled to the
        /// circle's radius over <see cref="pulseAuthoredRadius"/>.</summary>
        public GameObject pulsePrefab;
        public float pulseAuthoredRadius;
        /// <summary>How often the presentation polls the wards for pulses.</summary>
        public float pollSeconds;
    }
}
